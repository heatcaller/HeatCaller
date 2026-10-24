using System.Collections.Concurrent;

namespace HeatCaller.Tests;

/// <summary>
/// P4b of the timing layer (23 Oct 2026): a lap the window is only told of
/// seconds late (threaded mode, after a freeze) still reaches the announcer's
/// running order, but nothing it would say or play is offered - the room
/// hears the present. Only the thread handling that lap is quietened.
/// </summary>
public sealed class TimingLayer_P4b_StaleCallsTests
{
    private static (AnnouncementService Announcer, ConcurrentQueue<string> Log) Announcer()
    {
        var announcer = new AnnouncementService(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load());
        var log = new ConcurrentQueue<string>();
        announcer.StatusMessage += log.Enqueue;
        return (announcer, log);
    }

    private static List<string> Skipped(ConcurrentQueue<string> log) =>
        log.Where(line => line.Contains("[voice] Skipped", StringComparison.Ordinal)).ToList();

    [Fact]
    public void Calls_offered_for_a_stale_lap_are_skipped_and_said_once_in_the_log()
    {
        var (announcer, log) = Announcer();

        announcer.WhileLapCallsAreStale(() => announcer.NotifyWarning("Warning, Sam, blocking.", blackFlag: false), ageMs: 4_200);

        var skipped = Skipped(log);
        Assert.Single(skipped);
        Assert.Contains("4.2s old", skipped[0]);

        // The same call outside the scope is offered as ever.
        announcer.NotifyWarning("Warning, Sam, blocking.", blackFlag: false);

        // Nothing actually played in a test.
        announcer.Muted = true;

        Assert.Single(Skipped(log));
    }

    [Fact]
    public void A_call_offered_on_another_thread_meanwhile_is_not_caught_by_the_scope()
    {
        var (announcer, log) = Announcer();

        announcer.WhileLapCallsAreStale(() =>
        {
            // A real thread: Task.Run waited on from a pool thread can run
            // inline on the same thread (24 Oct 2026, timing bug hunt).
            var other = new Thread(() => announcer.NotifyWarning("Warning, Sam, blocking.", blackFlag: false));
            other.Start();
            other.Join();
        });

        announcer.Muted = true;

        Assert.Empty(Skipped(log));
    }

    [Fact]
    public void The_running_order_still_learns_a_stale_lap()
    {
        var (announcer, _) = Announcer();

        announcer.WhileLapCallsAreStale(() => announcer.OnStandingUpdated(new RaceStanding
        {
            Transponder = "101",
            Laps = 3,
            ElapsedMs = 30_000,
            Status = "active",
            Id = "101",
        }));

        announcer.Muted = true;

        // The lead is counted whatever the call does (LeadTracker) - so the
        // driver who led during a freeze is not called as a new leader on
        // the next fresh lap.
        Assert.Equal("101", announcer.Leads.Leader);
    }
}
