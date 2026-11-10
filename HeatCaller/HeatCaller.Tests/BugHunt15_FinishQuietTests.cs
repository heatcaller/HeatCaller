using System.Collections.Concurrent;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15 (9 Nov 2026), B47: with the finish call switched off - a club
/// using the finish fanfare alone - nothing cleared the decks at the
/// checkered, so an AI line or a running order already queued or rendering
/// was still said over the finish. The decks are cleared by the finish call
/// itself; with it off, NotifyRaceCompleted now clears them, as PlayFlagSound
/// does for a flag whose call is off.
/// </summary>
public sealed class BugHunt15_FinishQuietTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static (AnnouncementService Announcer, ConcurrentQueue<string> Log) Announcer()
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
        }

        var announcer = new AnnouncementService(ProfileStore.Load(), voice, sounds);
        var log = new ConcurrentQueue<string>();
        announcer.StatusMessage += log.Enqueue;

        return (announcer, log);
    }

    [Fact]
    public void The_checkered_clears_the_decks_with_the_finish_call_switched_off()
    {
        var (announcer, log) = Announcer();

        announcer.NotifyRaceCompleted();

        Assert.Contains(log, line => line.StartsWith("[voice] RaceCompleted interrupting", StringComparison.Ordinal));
    }
}
