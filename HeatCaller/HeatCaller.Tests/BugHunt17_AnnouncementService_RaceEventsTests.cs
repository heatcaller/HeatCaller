using System.Collections.Concurrent;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026): with the "Race started" call switched off - a
/// club using the start horn alone - nothing cleared the decks at a green
/// pressed by hand, so the last race's recap or a sponsor read spoke on over
/// the new race and the horn waited for the speaker behind it. The same for
/// "Time's up" with its call off: the buzzer waited behind the lap call
/// being spoken. Each now clears the decks itself when its call did not, as
/// NotifyRaceCompleted does (bug hunt 15).
/// </summary>
public sealed class BugHunt17_AnnouncementService_RaceEventsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>An announcer with every call and sound off, and its log.</summary>
    private static (AnnouncementService Announcer, ConcurrentQueue<string> Log, SoundEffectsStore Sounds) Announcer()
    {
        var voice = VoiceSettingsStore.Load();
        foreach (var config in voice.Current.Events.Values)
        {
            config.Enabled = false;
            config.EnabledInPractice = false;
        }

        var sounds = SoundEffectsStore.Load();
        foreach (var effect in sounds.Current.Effects.Values)
        {
            effect.Enabled = false;
            effect.EnabledInPractice = false;
        }

        var announcer = new AnnouncementService(ProfileStore.Load(), voice, sounds);
        var log = new ConcurrentQueue<string>();
        announcer.StatusMessage += log.Enqueue;

        return (announcer, log, sounds);
    }

    private static int Clears(ConcurrentQueue<string> log, string call) =>
        log.Count(line => line.StartsWith($"[voice] {call} interrupting", StringComparison.Ordinal));

    [Fact]
    public void The_green_clears_the_decks_with_the_start_call_switched_off()
    {
        var (announcer, log, _) = Announcer();

        announcer.NotifyRaceStarted();

        Assert.Equal(1, Clears(log, "RaceStarted"));
    }

    [Fact]
    public void Times_up_clears_the_decks_for_the_buzzer_when_its_call_is_off()
    {
        var (announcer, log, sounds) = Announcer();

        // The buzzer on, and silent for the test.
        sounds.Current.Volume = 0;
        sounds.Current.Effects[nameof(SoundEffectKind.RaceTimeOver)].Enabled = true;
        sounds.Current.Effects[nameof(SoundEffectKind.RaceTimeOver)].EnabledInPractice = true;

        announcer.NotifyRaceTimeOver();

        Assert.Equal(1, Clears(log, "RaceTimeOver"));
    }

    [Fact]
    public void Times_up_with_neither_call_nor_buzzer_leaves_the_lap_calls_alone()
    {
        var (announcer, log, _) = Announcer();

        announcer.NotifyRaceTimeOver();

        Assert.Equal(0, Clears(log, "RaceTimeOver"));
    }

    [Fact]
    public void A_call_that_cleared_the_decks_itself_is_not_followed_by_a_second_clear()
    {
        // Read from the source: a start call that speaks needs a voice.
        var events = Source("AnnouncementService.RaceEvents.cs");

        var started = events[events.IndexOf("public void NotifyRaceStarted()", StringComparison.Ordinal)..];
        started = started[..started.IndexOf("public void NotifyRaceTimeOver()", StringComparison.Ordinal)];

        var before = started.IndexOf("var clearsBefore = Volatile.Read(ref _interruptGeneration);", StringComparison.Ordinal);
        var call = started.IndexOf("EnqueueVoice(AnnouncementEventType.RaceStarted, null, null);", StringComparison.Ordinal);
        var unchanged = started.IndexOf("if (Volatile.Read(ref _interruptGeneration) == clearsBefore)", StringComparison.Ordinal);
        var clear = started.IndexOf("ClearTheDecksFor(AnnouncementEventType.RaceStarted);", StringComparison.Ordinal);

        Assert.True(before >= 0 && call > before, "The clear count must be read before the start call is offered.");
        Assert.True(unchanged > call && clear > unchanged, "The decks are cleared here only when the start call did not clear them.");
    }

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }
}
