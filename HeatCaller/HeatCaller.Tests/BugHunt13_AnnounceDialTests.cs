namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 13 (7 Nov 2026), the chattiness dial on the lap calls that
/// skip EnqueueVoice.
///
/// A lapping (through FirePairLine), the longest-streak takeover and the
/// running order all go straight to the per-lap batch or EnqueueVoiceWork,
/// so the dial's floor in EnqueueVoice never saw them. Each was still said
/// at a stop the Commentary page told the club was holding it. Each now
/// asks DialLetsItSpeak before a line is drawn or queued.
///
/// Like bug hunt 10's rivalry test, these need a live announcer and a race
/// to run, so the order of the checks is read from the source, and the
/// dial is checked for what the fix relies on: the shipped weight is held
/// at the stop that says it holds it, and said at Standard.
/// </summary>
public sealed class BugHunt13_AnnounceDialTests
{
    // ------------------------------------------------------------------
    //  A lapping

    [Fact]
    public void A_pair_line_asks_the_dial_before_it_picks_a_line_or_queues()
    {
        var fire = Between(Source("AnnouncementService.LapCalls.cs"), "private bool FirePairLine(", "TidyTemplate()");

        var caution = fire.IndexOf("HeldByCaution(type, config)", StringComparison.Ordinal);
        var dial = fire.IndexOf("DialLetsItSpeak(type, config)", StringComparison.Ordinal);
        var pick = fire.IndexOf("TemplateBag.Pick(", StringComparison.Ordinal);

        Assert.True(caution >= 0, "FirePairLine no longer checks for a flag.");
        Assert.True(dial > caution, "FirePairLine no longer asks the chattiness dial.");
        Assert.True(pick > dial, "The dial must be asked before a line is drawn from the bag.");
    }

    [Fact]
    public void A_lapping_still_goes_through_the_pair_line()
    {
        var check = Between(Source("AnnouncementService.LapCalls.cs"), "private void CheckLapped(", "</summary>");

        Assert.Contains("FirePairLine(AnnouncementEventType.Lapped", check, StringComparison.Ordinal);
    }

    [Fact]
    public void The_shipped_lapping_weight_is_held_when_sparing_and_said_at_standard()
    {
        AssertHeldAt(AnnouncementEventType.Lapped, "sparing");
    }

    // ------------------------------------------------------------------
    //  The longest-streak takeover

    [Fact]
    public void A_streak_takeover_asks_the_dial_before_it_takes_a_per_lap_slot()
    {
        var check = Between(Source("AnnouncementService.LapCalls.cs"),
            "nameof(AnnouncementEventType.StreakLead)", "\"[voice] Longest streak\"");

        var dial = check.IndexOf("DialLetsItSpeak(AnnouncementEventType.StreakLead, config)", StringComparison.Ordinal);
        var pick = check.IndexOf("TemplateBag.Pick(", StringComparison.Ordinal);
        var offer = check.IndexOf("ConsiderPerLapAction(", StringComparison.Ordinal);

        Assert.True(dial >= 0, "The streak takeover no longer asks the chattiness dial.");
        Assert.True(pick > dial, "The dial must be asked before a line is drawn from the bag.");
        Assert.True(offer > dial, "The dial must be asked before the line is offered a per-lap slot.");
    }

    [Fact]
    public void The_shipped_streak_takeover_weight_is_held_when_sparing_and_said_at_standard()
    {
        AssertHeldAt(AnnouncementEventType.StreakLead, "sparing");
    }

    // ------------------------------------------------------------------
    //  The running order

    [Fact]
    public void The_running_order_asks_the_dial_before_it_starts_waiting_for_its_cars()
    {
        var maybe = Between(Source("AnnouncementService.LapCalls.cs"),
            "private void MaybeAnnouncePositions(", "private void SayWaitingPositionReport(");

        var dial = maybe.IndexOf("DialLetsItSpeak(AnnouncementEventType.PositionReport, config)", StringComparison.Ordinal);
        var everyLaps = maybe.IndexOf("PositionReportEveryLaps", StringComparison.Ordinal);

        Assert.True(dial >= 0, "MaybeAnnouncePositions no longer asks the chattiness dial.");
        Assert.True(everyLaps > dial, "The dial must be asked before the report is scheduled.");
    }

    [Fact]
    public void The_running_order_asks_the_dial_again_before_it_is_queued()
    {
        var say = Between(Source("AnnouncementService.LapCalls.cs"),
            "private void SayPositionReport(", "EnqueueVoiceWork(AnnouncementEventType.PositionReport");

        var dial = say.IndexOf("DialLetsItSpeak(AnnouncementEventType.PositionReport, config)", StringComparison.Ordinal);
        var pick = say.IndexOf("TemplateBag.Pick(", StringComparison.Ordinal);

        Assert.True(dial >= 0, "SayPositionReport no longer asks the chattiness dial.");
        Assert.True(pick > dial, "The dial must be asked before a line is drawn from the bag.");
    }

    [Fact]
    public void The_shipped_running_order_weight_is_held_when_measured_or_sparing_and_said_at_standard()
    {
        AssertHeldAt(AnnouncementEventType.PositionReport, "measured");
        AssertHeldAt(AnnouncementEventType.PositionReport, "sparing");
    }

    // ------------------------------------------------------------------
    //  Helpers

    /// <summary>
    /// The shipped weight of <paramref name="type"/> is held at
    /// <paramref name="key"/>, the Commentary page says so, and Standard
    /// still says it.
    /// </summary>
    private static void AssertHeldAt(AnnouncementEventType type, string key)
    {
        var config = VoiceProfile.CreateDefault().Events[type.ToString()];

        Assert.False(ShowChatter.CallIsWorthIt(config.Weight, key, alwaysSaid: false));
        Assert.True(ShowChatter.CallIsWorthIt(config.Weight, "standard", alwaysSaid: false));
        Assert.True(ShowChatter.CallIsWorthIt(config.Weight, null, alwaysSaid: false));
        Assert.Contains(VoiceProfile.Describe(type).Label, ShowChatter.CallsHeldBack(key));
    }

    private static string RepoRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);

            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            throw new DirectoryNotFoundException("No folder with check.cmd in it above " + AppContext.BaseDirectory);
        }
    }

    private static string Source(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName));

    private static string Between(string source, string from, string until)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);

        Assert.True(start >= 0, $"\"{from}\" is not in the file any more - this test needs its name updated.");

        var end = source.IndexOf(until, start + from.Length, StringComparison.Ordinal);

        Assert.True(end > start, $"\"{until}\" no longer follows \"{from}\" - this test needs its bounds updated.");

        return source[start..end];
    }
}
