namespace HeatCaller.Tests;

/// <summary>
/// The running order is not read once it has waited too long (31 Oct 2026).
///
/// It is one of the calls that waits its turn behind a line already being
/// spoken rather than being dropped, and it was the only one of them with no
/// limit on the wait. Its words are written when it is built, so behind a
/// long line it read out an order the screen had already changed.
/// </summary>
public sealed class PositionReportLimitTests
{
    [Fact]
    public void A_running_order_said_within_the_limit_is_still_read()
    {
        Assert.True(AnnouncementService.PositionReportStillTrue(10_000, 10_000));
        Assert.True(AnnouncementService.PositionReportStillTrue(10_000, 10_000 + AnnouncementService.PositionReportSayWithinMs));
    }

    [Fact]
    public void A_running_order_that_waited_past_the_limit_is_dropped()
    {
        Assert.False(AnnouncementService.PositionReportStillTrue(10_000, 10_001 + AnnouncementService.PositionReportSayWithinMs));
        Assert.False(AnnouncementService.PositionReportStillTrue(10_000, 60_000));
    }

    [Fact]
    public void The_limit_outlasts_a_lap_call_but_not_a_short_lap()
    {
        // Long enough to wait out the lap call it usually sits behind, short
        // enough that a five-second Mini-Z lap has not gone by.
        Assert.InRange(AnnouncementService.PositionReportSayWithinMs, 2_000, 5_000);
    }

    [Fact]
    public void The_report_checks_the_limit_before_it_is_spoken()
    {
        var report = Between(
            Source("AnnouncementService.LapCalls.cs"),
            "private void MaybeAnnouncePositions(",
            "internal const long PositionReportSayWithinMs");

        var built = report.IndexOf("var builtAtMs = TimingClock.NowMs;", StringComparison.Ordinal);
        var check = report.IndexOf("PositionReportStillTrue(builtAtMs, TimingClock.NowMs)", StringComparison.Ordinal);
        var spoken = report.IndexOf("await SynthesizeAndPlayAsync(text, \"[voice] Position report\"", StringComparison.Ordinal);

        Assert.True(built >= 0, "the report no longer records when it was built");
        Assert.True(check > built, "the report no longer checks how long it waited");
        Assert.True(spoken > check, "the report is spoken before its wait is checked");
    }

    // ------------------------------------------------------------------
    //  Reading the source

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
