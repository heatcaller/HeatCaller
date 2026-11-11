namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 14 (7 Nov 2026), the voice.
///
/// V1: a position report waiting for the cars it names kept its own timer
/// through a reset, and when the wait ran out it read the abandoned race's
/// order to the room, up to a minute after Reset.
///
/// V2: "one minute to go" had no say-by time, so behind a busy speaker it
/// could be heard with twenty or thirty seconds left.
/// </summary>
public sealed class BugHunt14_VoiceTests
{
    [Fact]
    public void A_reset_drops_the_position_report_still_waiting_for_its_cars()
    {
        var cut = Code(Between(
            Source("AnnouncementService.RaceEvents.cs"),
            "private void CutEverything(",
            "public static readonly TimeSpan CountdownTicksLength"));

        var resetOnly = cut.IndexOf("if (alsoFlagCalls)", cut.IndexOf("GiveBackLost(giveBack);", StringComparison.Ordinal), StringComparison.Ordinal);
        var dropped = cut.IndexOf("_waitingPositionReport = null;", StringComparison.Ordinal);
        var locked = cut.LastIndexOf("lock (_positionReportLock)", dropped < 0 ? 0 : dropped, StringComparison.Ordinal);

        Assert.True(dropped >= 0, "a reset no longer drops the position report waiting for its cars");
        Assert.True(resetOnly >= 0 && resetOnly < dropped, "the waiting report is no longer dropped only on a reset");
        Assert.True(locked > resetOnly, "the waiting report is no longer dropped under its own lock");
    }

    [Fact]
    public void A_reset_leaves_the_lap_counter_and_crossings_for_an_undo()
    {
        var cut = Code(Between(
            Source("AnnouncementService.RaceEvents.cs"),
            "private void CutEverything(",
            "public static readonly TimeSpan CountdownTicksLength"));

        Assert.DoesNotContain("_lastPositionReportLap", cut, StringComparison.Ordinal);
        Assert.DoesNotContain("_lapCrossings", cut, StringComparison.Ordinal);
    }

    [Fact]
    public void One_minute_to_go_has_a_say_by_time()
    {
        var notify = Between(
            Source("AnnouncementService.RaceEvents.cs"),
            "public void NotifyFinalMinute(",
            "internal const long FinalMinuteSayWithinMs");

        // Bug hunt 16 (10 Nov 2026): the say-by time is now worked out by
        // FinalMinuteSayByMs, from the minute itself - see BugHunt16_FinalMinuteTests.
        Assert.Contains("FinalMinuteSayByMs(TimingClock.NowMs, timeLeft)", Code(notify), StringComparison.Ordinal);
        Assert.Contains("sayByMs: sayByMs", Code(notify), StringComparison.Ordinal);
    }

    [Fact]
    public void One_minute_to_go_is_said_within_a_quarter_of_its_minute()
    {
        Assert.True(AnnouncementService.FinalSecondsSayWithinMs < AnnouncementService.FinalMinuteSayWithinMs);
        Assert.True(AnnouncementService.FinalMinuteSayWithinMs <= 15_000);
    }

    // ------------------------------------------------------------------
    //  Reading the source

    private static string Code(string source) =>
        string.Join("\n", source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

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
