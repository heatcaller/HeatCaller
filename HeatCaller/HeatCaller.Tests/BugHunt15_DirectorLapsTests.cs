namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15: the announcer page's lap feed to the AI director. The page
/// is a WinForms control, so its wiring is read from the source; what the
/// director does with it is tested in CommentaryDirectorTests.
/// </summary>
public sealed class BugHunt15_DirectorLapsTests
{
    [Fact]
    public void A_lap_taken_off_by_hand_drops_the_directors_note_as_well_as_the_bests()
    {
        var feed = FeedLaps();

        Assert.Contains("director.ForgetLaps(standing.TransponderId, surviving);", feed, StringComparison.Ordinal);
        Assert.DoesNotContain("director.Pace.Forget(", feed, StringComparison.Ordinal);
    }

    [Fact]
    public void Laps_already_run_when_the_count_starts_again_are_learned_silently()
    {
        var source = Source();
        var feed = FeedLaps();

        // Every clear of the count marks the next feed as a catch-up...
        Assert.Equal(
            System.Text.RegularExpressions.Regex.Matches(source, @"_lapsFed\.Clear\(\);").Count,
            System.Text.RegularExpressions.Regex.Matches(source, @"_lapsFed\.Clear\(\);\s*_lapsFedFromScratch = true;").Count);

        // ...which is taken once, and primes rather than announces.
        Assert.Contains("var catchingUp = _lapsFedFromScratch;", feed, StringComparison.Ordinal);
        Assert.Contains("_lapsFedFromScratch = false;", feed, StringComparison.Ordinal);

        var catchUp = feed.IndexOf("if (catchingUp)", StringComparison.Ordinal);
        var announce = feed.IndexOf("director.Lap(standing.TransponderId", StringComparison.Ordinal);

        Assert.True(catchUp >= 0 && catchUp < announce);
        Assert.Contains("director.LapAlreadyRun(", feed[catchUp..announce], StringComparison.Ordinal);
        Assert.DoesNotContain("RecordThreatened", feed[catchUp..announce], StringComparison.Ordinal);
    }

    private static string FeedLaps()
    {
        var source = Source();
        var start = source.IndexOf("private void FeedLaps(", StringComparison.Ordinal);
        var end = source.IndexOf("/// <summary>", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }

    private static string Source()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        var root = dir?.FullName ?? throw new InvalidOperationException("Couldn't find check.cmd.");
        return File.ReadAllText(Path.Combine(root, "HeatCaller", "HeatCaller", "AnnouncerControl.Director.cs"));
    }
}
