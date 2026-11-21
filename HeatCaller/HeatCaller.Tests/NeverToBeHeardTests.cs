namespace HeatCaller.Tests;

/// <summary>
/// Lines that will now never be heard (21 Nov 2026). A line binned or
/// dropped for a flag, a countdown, a reset, two cars that swapped or a mute
/// used to stay on the Big Screen and in the feed as said - only a stale or
/// no-longer-true line was taken back. And a correction that named a new
/// leader, dropped because the order moved again, left the lead change it
/// stood in for unsaid. The voice can't speak in a test, so these paths are
/// checked in the source, as the other speaker-side rules are
/// (StaleCallsAtTheSpeakerTests).
/// </summary>
public sealed class NeverToBeHeardTests
{
    [Fact]
    public void A_flag_and_a_reset_take_the_lines_they_bin_off_the_screens()
    {
        Assert.Contains("BinQueuedLocked(item => !SurvivesAFlag(item.Type), ref giveBack, withdrawText: true)", Source("AnnouncementService.PreparedLines.cs"), StringComparison.Ordinal);
        Assert.Contains("BinQueuedLocked(_ => true, ref giveBack, withdrawText: true)", Source("AnnouncementService.RaceEvents.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Lines_about_two_cars_that_swapped_come_off_the_screens_too()
    {
        var battles = Source("AnnouncementService.BattleLines.cs");

        Assert.Equal(2, Count(battles, "ref giveBack, withdrawText: true)"));
    }

    [Fact]
    public void A_line_replaced_by_a_newer_one_is_not_withdrawn()
    {
        // Its words may be the very ones the newer line has just put up.
        var queue = Source("AnnouncementService.VoiceQueue.cs");

        Assert.Contains("BinQueuedLocked(item => item.Type == type && item.Transponder == transponder, ref giveBack);", queue, StringComparison.Ordinal);
        Assert.Contains("private int BinQueuedLocked(Predicate<QueuedVoiceItem> match, ref List<Action>? giveBack, bool withdrawText = false)", queue, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_drop_at_the_speaker_withdraws_the_line()
    {
        var queue = Source("AnnouncementService.VoiceQueue.cs");

        // Flag, countdown, swap and mute - before the render and after it - and
        // the stale and whyNotNow drops that already did.
        Assert.True(Count(queue, "WithdrawAnnouncementText(_dequeuedFreshness.Value.Shown);") >= 10);
        Assert.Contains("WithdrawAnnouncementText(_dequeuedFreshness.Value.Shown);", Source("AnnouncementService.Streaming.cs"), StringComparison.Ordinal);

        // And the last check, once the line is registered as playing.
        var playback = Source("AnnouncementService.Playback.cs");
        Assert.Contains("if (!wanted)", playback, StringComparison.Ordinal);
    }

    [Fact]
    public void A_correction_dropped_because_the_order_moved_again_still_lets_the_lead_change_be_said()
    {
        var cut = Source("AnnouncementService.PositionReportCut.cs");

        Assert.Contains("if (!heard && movedAgain && place == 0 && !byRuling)", cut, StringComparison.Ordinal);
        Assert.Contains("CallTheLeadAfterADroppedCorrection(mover.Transponder, depth, sayBy);", cut, StringComparison.Ordinal);

        // Only a correction actually queued stands in for the lead change.
        Assert.Contains("return place == 0 && queued;", cut, StringComparison.Ordinal);
    }

    [Fact]
    public void A_final_laps_gap_call_is_tagged_with_the_two_cars()
    {
        Assert.Contains("using (AboutBattle(pair))", Source("AnnouncementService.FinalLapsGap.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_pacing_gap_is_timed_on_the_timing_clock()
    {
        var density = Source("AnnouncementService.Density.cs");

        Assert.DoesNotContain("DateTime.UtcNow - _lastRoutineAccepted", density, StringComparison.Ordinal);
        Assert.Contains("_lastRoutineAcceptedMs = TimingClock.NowMs;", density, StringComparison.Ordinal);
    }

    [Fact]
    public void A_held_over_call_expires_on_the_timing_clock_by_default()
    {
        var held = new HeldOver<string>();

        Assert.True(held.Offer("call", 100, 0, out _));
        Assert.Equal("call", held.Take(out var stale));
        Assert.Null(stale);
    }

    private static int Count(string text, string what)
    {
        var count = 0;

        for (var i = text.IndexOf(what, StringComparison.Ordinal); i >= 0; i = text.IndexOf(what, i + what.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file)).Replace("\r\n", "\n");
    }
}
