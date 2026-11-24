using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Sound effects that play over each other (24 Nov 2026, asked for by
/// Asher: "you should be able to hear multiple cars hitting a good lap").
/// Every effect used to wait for the one before it to end; now a car's
/// sounds play as they fire, up to three at once, while the start, finish
/// and flag sounds keep their order. A switch on the Sound Effects page
/// puts back the old one-at-a-time way.
/// </summary>
public sealed class SimultaneousSoundEffectsTests : IDisposable
{
    private readonly TempAppData _data = new();

    /// <summary>Lets every held tone end.</summary>
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Each tone as it starts: which sound, when, and at what level.</summary>
    private readonly ConcurrentQueue<(SoundEffectKind Kind, DateTime AtUtc, float Volume)> _starts = new();

    public void Dispose()
    {
        _release.TrySetResult();
        _data.Dispose();
    }

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly MethodInfo PlaySoundMethod = typeof(AnnouncementService).GetMethod("PlaySound", Private)!;

    private static readonly MethodInfo StopPlayingSoundMethod = typeof(AnnouncementService).GetMethod("StopPlayingSound", Private)!;

    /// <summary>An announcer whose sound settings have these effects on (and the overlap switch as given).</summary>
    private static AnnouncementService Announcer(bool overlap, params SoundEffectKind[] on)
    {
        var sounds = SoundEffectsStore.Load();
        var profile = SoundEffectsStore.Copy(sounds.Current);
        profile.EffectsOverlap = overlap;

        foreach (var kind in on)
        {
            profile.Effects[kind.ToString()].Enabled = true;
        }

        sounds.Save(profile);

        return new AnnouncementService(ProfileStore.Load(), VoiceSettingsStore.Load(), sounds);
    }

    /// <summary>A tone that notes when it starts, then holds the speaker until released or cut off.</summary>
    private Func<float, CancellationToken, Task> HeldTone(SoundEffectKind kind) => async (volume, cancel) =>
    {
        _starts.Enqueue((kind, DateTime.UtcNow, volume));
        await Task.WhenAny(_release.Task, Task.Delay(Timeout.Infinite, cancel));
    };

    private void Play(AnnouncementService announcer, SoundEffectKind kind) =>
        PlaySoundMethod.Invoke(announcer, new object?[] { kind, HeldTone(kind), kind.ToString(), null, 0 });

    private static async Task WaitFor(Func<bool> condition, string what)
    {
        var until = DateTime.UtcNow.AddSeconds(10);

        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, $"Timed out waiting for {what}.");
            await Task.Delay(20);
        }
    }

    // ------------------------------------------------- playing together

    [Fact]
    public async Task Two_cars_on_a_good_lap_are_both_heard_at_once()
    {
        var announcer = Announcer(overlap: true);

        Play(announcer, SoundEffectKind.GoodLap);
        Play(announcer, SoundEffectKind.GoodLap);

        // Both start while the first is still playing.
        await WaitFor(() => _starts.Count == 2, "both good lap chimes to start");
        Assert.Equal(2, announcer.SoundsPlayingForTests);

        // The second comes a hair after the first, so the room hears two
        // chimes rather than one louder one.
        var starts = _starts.ToArray();
        Assert.True(starts[1].AtUtc - starts[0].AtUtc >= SoundOverlap.SameSoundStagger - TimeSpan.FromMilliseconds(30));

        _release.TrySetResult();
        await WaitFor(() => announcer.SoundsPlayingForTests == 0, "both chimes to end");
    }

    [Fact]
    public async Task One_laps_sounds_play_together_the_later_one_a_little_quieter()
    {
        var announcer = Announcer(overlap: true, SoundEffectKind.PersonalBest);

        Play(announcer, SoundEffectKind.FastLap);
        await WaitFor(() => _starts.Count == 1, "the fast lap sound to start");

        Play(announcer, SoundEffectKind.PersonalBest);
        await WaitFor(() => _starts.Count == 2, "the personal best sound to start over it");

        var starts = _starts.ToArray();
        Assert.Equal(SoundEffectKind.FastLap, starts[0].Kind);
        Assert.Equal(SoundEffectKind.PersonalBest, starts[1].Kind);

        // One other playing: 0.8 of the level.
        Assert.Equal(starts[0].Volume * 0.8f, starts[1].Volume, 3);
    }

    [Fact]
    public async Task A_fourth_sound_waits_for_one_of_the_three_to_end()
    {
        var announcer = Announcer(overlap: true, SoundEffectKind.PersonalBest);

        Play(announcer, SoundEffectKind.GoodLap);
        Play(announcer, SoundEffectKind.FastLap);
        Play(announcer, SoundEffectKind.Streak);
        Play(announcer, SoundEffectKind.PersonalBest);

        await WaitFor(() => _starts.Count == 3, "three sounds to start");
        await Task.Delay(400);
        Assert.Equal(3, _starts.Count);

        _release.TrySetResult();
        await WaitFor(() => _starts.Count == 4, "the fourth to start once there was room");
        Assert.Equal(SoundEffectKind.PersonalBest, _starts.Last().Kind);
    }

    // ------------------------------------------------- the switch off

    [Fact]
    public async Task With_the_switch_off_they_play_one_after_another()
    {
        var announcer = Announcer(overlap: false);

        Play(announcer, SoundEffectKind.GoodLap);
        Play(announcer, SoundEffectKind.GoodLap);

        await WaitFor(() => _starts.Count == 1, "the first chime to start");
        await Task.Delay(400);
        Assert.Single(_starts);

        _release.TrySetResult();
        await WaitFor(() => _starts.Count == 2, "the second chime, once the first ended");

        // Never joined another, so never lowered.
        Assert.All(_starts, start => Assert.Equal(_starts.First().Volume, start.Volume, 3));
    }

    // ------------------------------------------------- the race sequence

    [Fact]
    public async Task Race_sequence_sounds_keep_their_order_while_a_lap_chime_plays_beside_them()
    {
        var announcer = Announcer(overlap: true, SoundEffectKind.RaceStaging, SoundEffectKind.CountdownStarted);

        Play(announcer, SoundEffectKind.RaceStaging);
        Play(announcer, SoundEffectKind.CountdownStarted);
        Play(announcer, SoundEffectKind.GoodLap);

        // The staging sound and the lap chime play; the countdown waits its turn.
        await WaitFor(() => _starts.Count == 2, "the staging sound and the chime to start");
        await Task.Delay(400);

        Assert.Equal(
            new[] { SoundEffectKind.GoodLap, SoundEffectKind.RaceStaging },
            _starts.Select(start => start.Kind).OrderBy(kind => kind.ToString()).ToArray());

        _release.TrySetResult();
        await WaitFor(() => _starts.Count == 3, "the countdown sound after the staging one");
        Assert.Equal(SoundEffectKind.CountdownStarted, _starts.Last().Kind);
    }

    // ------------------------------------------------- cutting them off

    [Fact]
    public async Task A_mute_stops_every_sound_playing_not_just_the_newest()
    {
        var announcer = Announcer(overlap: true);

        Play(announcer, SoundEffectKind.GoodLap);
        Play(announcer, SoundEffectKind.FastLap);
        Play(announcer, SoundEffectKind.Streak);
        await WaitFor(() => announcer.SoundsPlayingForTests == 3, "three sounds to play");

        announcer.Muted = true;

        await WaitFor(() => announcer.SoundsPlayingForTests == 0, "the mute to stop all three");
        announcer.Muted = false;
    }

    [Fact]
    public async Task Cutting_one_kind_leaves_the_others_playing()
    {
        var announcer = Announcer(overlap: true, SoundEffectKind.CountdownTicks);

        Play(announcer, SoundEffectKind.CountdownTicks);
        Play(announcer, SoundEffectKind.GoodLap);
        await WaitFor(() => announcer.SoundsPlayingForTests == 2, "the ticks and the chime to play");

        var cut = (bool)StopPlayingSoundMethod.Invoke(announcer, new object?[] { null, SoundEffectKind.CountdownTicks })!;

        Assert.True(cut);
        await WaitFor(() => announcer.SoundsPlayingForTests == 1, "the ticks to stop");
        await Task.Delay(200);
        Assert.Equal(1, announcer.SoundsPlayingForTests);
    }

    [Fact]
    public async Task Audio_counts_as_pending_while_any_overlapping_sound_plays()
    {
        var announcer = Announcer(overlap: true);

        Play(announcer, SoundEffectKind.GoodLap);
        Play(announcer, SoundEffectKind.FastLap);
        await WaitFor(() => _starts.Count == 2, "both sounds to start");

        Assert.True(announcer.IsAudioPending);

        _release.TrySetResult();
        await WaitFor(() => !announcer.IsAudioPending, "nothing pending once both ended");
    }

    // ------------------------------------------------- the switch kept

    [Fact]
    public void A_settings_file_from_before_the_switch_has_it_on()
    {
        var old = JsonSerializer.Deserialize<SoundEffectsProfile>("{\"MaxEffectsPerLap\":3}")!;

        Assert.True(old.EffectsOverlap);
    }

    [Fact]
    public void Applying_a_soundscape_keeps_the_clubs_choice()
    {
        var voice = VoiceSettingsStore.Load();
        var sound = SoundEffectsStore.Load();

        var off = SoundEffectsStore.Copy(sound.Current);
        off.EffectsOverlap = false;
        sound.Save(off);

        ThemeStore.Apply(Theme.CreateBuiltInDefaults().First(), voice, sound);

        Assert.False(sound.Current.EffectsOverlap);
    }

    // ------------------------------------------------- the rules

    [Fact]
    public void Only_moment_sounds_play_at_once_and_only_with_the_switch_on()
    {
        var sequence = new[]
        {
            SoundEffectKind.RaceStaging, SoundEffectKind.CountdownStarted, SoundEffectKind.CountdownTicks,
            SoundEffectKind.RaceStarted, SoundEffectKind.YellowFlag, SoundEffectKind.RedFlag,
            SoundEffectKind.GreenFlagResumed, SoundEffectKind.RaceTimeOver, SoundEffectKind.RaceCompleted,
        };

        foreach (var kind in Enum.GetValues<SoundEffectKind>())
        {
            Assert.False(SoundOverlap.PlaysAtOnce(kind, overlapOn: false));
            Assert.Equal(!sequence.Contains(kind), SoundOverlap.PlaysAtOnce(kind, overlapOn: true));
        }

        // Every per-lap sound is a moment sound.
        Assert.Subset(SoundOverlap.MomentKinds.ToHashSet(), SoundEffectsProfile.PerLapKinds);
    }

    [Fact]
    public void Each_sound_that_joins_is_a_little_quieter()
    {
        Assert.Equal(1f, SoundOverlap.StackGain(0));
        Assert.Equal(0.8f, SoundOverlap.StackGain(1));
        Assert.Equal(0.7f, SoundOverlap.StackGain(2));
        Assert.Equal(0.7f, SoundOverlap.StackGain(5));
    }

    [Fact]
    public void The_same_sound_is_held_only_until_the_stagger_has_passed()
    {
        var now = new DateTime(2026, 11, 24, 20, 0, 0, DateTimeKind.Utc);

        Assert.Equal(TimeSpan.Zero, SoundOverlap.StaggerFor(null, now));
        Assert.Equal(SoundOverlap.SameSoundStagger, SoundOverlap.StaggerFor(now, now));
        Assert.Equal(TimeSpan.FromMilliseconds(70), SoundOverlap.StaggerFor(now.AddMilliseconds(-50), now));
        Assert.Equal(TimeSpan.Zero, SoundOverlap.StaggerFor(now.AddSeconds(-1), now));
    }
}
