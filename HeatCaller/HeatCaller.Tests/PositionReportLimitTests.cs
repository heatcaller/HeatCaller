namespace HeatCaller.Tests;

/// <summary>
/// How long a running order may wait for the speaker.
///
/// 31 Oct 2026: it was dropped once it had waited three seconds, because its
/// words were written when it was built and behind a long line they read out
/// an order the screen had already changed.
///
/// 1 Nov 2026: the order is now read again from the board just before it is
/// spoken, and the report waits for the last car it names - so it is queued
/// the moment that car crosses, right behind that car's own calls. Asher had
/// it set every 4 laps and never heard it: on the demo heats that afternoon
/// four of the six that came due in racing were dropped after waiting 3.7 to
/// 10.4 seconds, and the only one in the last heat was among them. A wait no
/// longer drops it; a newer report queued behind it still replaces it.
/// </summary>
public sealed class PositionReportLimitTests
{
    [Fact]
    public void A_newer_report_still_replaces_one_waiting_for_the_speaker()
    {
        // With no wait limit, this is what stops reports piling up.
        var queue = Source("AnnouncementService.VoiceQueue.cs");

        Assert.Contains("BinQueuedLocked(item => item.Type == AnnouncementEventType.PositionReport", queue, StringComparison.Ordinal);
    }

    [Fact]
    public void The_report_is_not_dropped_for_how_long_it_waited()
    {
        var report = Between(
            Source("AnnouncementService.LapCalls.cs"),
            "private void MaybeAnnouncePositions(",
            "private string WordRunningOrder(");
        var code = string.Join("\n", report.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        Assert.DoesNotContain("PositionReportStillTrue", code, StringComparison.Ordinal);
        Assert.DoesNotContain("builtAtMs", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_report_reads_the_board_again_before_it_is_spoken()
    {
        var report = Between(
            Source("AnnouncementService.LapCalls.cs"),
            "private void MaybeAnnouncePositions(",
            "private string WordRunningOrder(");

        var queued = report.IndexOf("EnqueueVoiceWork(AnnouncementEventType.PositionReport", StringComparison.Ordinal);
        var reread = report.IndexOf("RunningOrderNow(depth)", StringComparison.Ordinal);
        var spoken = report.IndexOf("await SynthesizeAndPlayAsync(said, \"[voice] Position report\"", StringComparison.Ordinal);

        // The words it was queued with may be out of date after the wait;
        // they are checked against the board once the voice reaches them -
        // see PositionReportOrderTests.
        Assert.True(queued >= 0, "the report is no longer queued for the voice");
        Assert.True(reread > queued && spoken > reread, "the running order is no longer read again just before it is spoken");
    }

    [Fact]
    public void The_running_order_has_no_rounded_or_vague_gap_wording()
    {
        // 1 Nov 2026: "right on his tail" for anything under a second and a
        // half, and "a second and a half back" for the rest, under a board
        // that showed the gap to the hundredth. Every gap the report says
        // is now the exact one - see RaceNumbers.GapExactly.
        var wording = Between(
            Source("AnnouncementService.LapCalls.cs"),
            "private string WordRunningOrder(",
            "private string SpokenNameFor(");
        var code = string.Join("\n", wording.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        Assert.DoesNotContain("tail", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GapSpoken", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RaceNumbers.Behind(", code, StringComparison.Ordinal);
        Assert.Contains("RaceNumbers.BehindExactly(", code, StringComparison.Ordinal);

        var gap = Between(Source("AnnouncementText.cs"), "internal static string SpokenGap(", "internal static string SpokenGaps(");
        var gapCode = string.Join("\n", gap.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        Assert.DoesNotContain("GapSpoken", gapCode, StringComparison.Ordinal);
        Assert.DoesNotContain("under a tenth", gapCode, StringComparison.Ordinal);
        Assert.Contains("RaceNumbers.BehindExactly(", gapCode, StringComparison.Ordinal);
    }

    [Fact]
    public void The_report_waits_for_the_cars_it_names_before_it_is_worded()
    {
        var report = Between(
            Source("AnnouncementService.LapCalls.cs"),
            "private void SayWaitingPositionReport()",
            "private void SayPositionReport(");

        var settled = report.IndexOf("PositionReportOrder.SettledCount(", StringComparison.Ordinal);
        var said = report.IndexOf("SayPositionReport(rows)", StringComparison.Ordinal);

        Assert.True(settled >= 0, "the report no longer checks that the cars it names are over the line");
        Assert.True(said > settled, "the report is worded before its cars are over the line");
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
