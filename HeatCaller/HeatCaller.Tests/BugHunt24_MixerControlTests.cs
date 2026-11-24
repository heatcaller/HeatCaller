using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): the Mixer's level check through a race.
///
/// Play Everything left on with the Mixer page showing kept saying "Level
/// check" and playing the Good Lap chime every few seconds through staging,
/// the countdown and the race whenever session sound did not start on its
/// own (off by default for races, or set to start at the green). The repeat
/// now asks the race before each line and stops once a race is under way.
/// </summary>
public sealed class BugHunt24_MixerControlTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Theory]
    [InlineData(RaceState.Idle, true)]
    [InlineData(RaceState.Finished, true)]
    [InlineData(RaceState.Staging, false)]
    [InlineData(RaceState.Countdown, false)]
    [InlineData(RaceState.Racing, false)]
    public void The_level_check_talks_only_between_races(RaceState state, bool talks)
    {
        Assert.Equal(talks, MixerControl.LevelCheckMayTalk(state));
    }

    [Theory]
    [InlineData(RaceState.Staging)]
    [InlineData(RaceState.Countdown)]
    [InlineData(RaceState.Racing)]
    public void The_level_check_repeat_stops_once_a_race_is_under_way(RaceState state)
    {
        var stillRepeating = RunTick(() => state);

        Assert.False(stillRepeating);
    }

    [Fact]
    public void The_level_check_repeat_carries_on_between_races()
    {
        var stillRepeating = RunTick(() => RaceState.Idle);

        Assert.True(stillRepeating);
    }

    /// <summary>
    /// Play Everything's state set by hand, then one tick of its repeat with
    /// the race reading <paramref name="raceState"/>. Muted so the between-
    /// races tick plays nothing. Returns whether the repeat is still on.
    /// </summary>
    private static bool RunTick(Func<RaceState> raceState)
    {
        var voice = VoiceSettingsStore.Load();
        var sounds = SoundEffectsStore.Load();
        var ambienceStore = AmbienceStore.Load();
        var musicStore = MusicStore.Load();
        var profiles = ProfileStore.Load();

        return StaThread.Run(() =>
        {
            using var crowd = new AmbienceService();
            using var music = new MusicPlayer();
            var announcer = new AnnouncementService(profiles, voice, sounds) { Muted = true };
            using var mixer = new MixerControl(voice, sounds, ambienceStore, crowd, musicStore, music, announcer, new AppSettings());

            FieldOf(mixer, "_auditioning").SetValue(mixer, true);
            var levelCheck = (System.Windows.Forms.Timer)FieldOf(mixer, "_auditionVoice").GetValue(mixer)!;
            levelCheck.Start();

            mixer.RaceStateNow = raceState;
            Call(mixer, "AuditionTick");

            var enabled = levelCheck.Enabled;
            levelCheck.Stop();
            FieldOf(mixer, "_auditioning").SetValue(mixer, false);
            return enabled;
        }, TimeSpan.FromSeconds(30), () => "one tick of the Mixer's level check");
    }

    private static object? Call(object owner, string name, params object?[] args)
    {
        var method = owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(owner.GetType().Name, name);

        try
        {
            return method.Invoke(owner, args);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            throw e.InnerException;
        }
    }

    private static FieldInfo FieldOf(object owner, string name) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(owner.GetType().Name, name);
}
