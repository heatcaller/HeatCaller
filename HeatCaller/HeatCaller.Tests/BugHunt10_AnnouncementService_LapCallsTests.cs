namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10 (23 Oct 2026), AnnouncementService.LapCalls.cs.
///
/// A rivalry line went straight to EnqueueVoiceWork, so the club's
/// chattiness dial never saw it: a quiet announcer still called every
/// rivalry swap. FireRivalryLine now asks the dial first, at a close
/// battle's weight. It needs a live announcer and a race to run, so the
/// order of the checks is read from the source, and the dial itself is
/// checked for what the fix relies on: the shipped close-battle weight is
/// held at the quietest stop and said at the standard one.
/// </summary>
public sealed class BugHunt10_AnnouncementService_LapCallsTests
{
    [Fact]
    public void A_rivalry_asks_the_chattiness_dial_before_it_picks_a_line_or_queues()
    {
        var fire = Between(Source("AnnouncementService.LapCalls.cs"), "private void FireRivalryLine(", "private void HandleGatePassed(");

        var weight = fire.IndexOf("WeightOf(AnnouncementEventType.CloseBattle)", StringComparison.Ordinal);
        var dial = fire.IndexOf("ShowChatter.CallIsWorthIt(", StringComparison.Ordinal);
        var pick = fire.IndexOf("TemplateBag.Pick(", StringComparison.Ordinal);
        var queue = fire.IndexOf("EnqueueVoiceWork(", StringComparison.Ordinal);

        Assert.True(weight >= 0, "FireRivalryLine no longer weighs a rivalry like a close battle.");
        Assert.True(dial > weight, "FireRivalryLine no longer asks the chattiness dial.");
        Assert.True(pick > dial, "The dial must be asked before a line is drawn from the bag.");
        Assert.True(queue > dial, "The dial must be asked before the line is queued.");
    }

    [Fact]
    public void The_shipped_close_battle_weight_is_held_when_sparing_and_said_at_standard()
    {
        var weight = VoiceProfile.CreateDefault().Events[nameof(AnnouncementEventType.CloseBattle)].Weight;

        Assert.False(ShowChatter.CallIsWorthIt(weight, "sparing", alwaysSaid: false));
        Assert.True(ShowChatter.CallIsWorthIt(weight, "standard", alwaysSaid: false));
        Assert.True(ShowChatter.CallIsWorthIt(weight, null, alwaysSaid: false));
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
