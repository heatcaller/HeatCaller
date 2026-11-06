namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11 (5 Nov 2026), AfterRaceA: the honours plan was marked Started
/// before the render wait and the AI hand-over wait, so a Skip or Hold in that
/// silence - nothing of the segment heard yet - made the sequence's finally
/// skip the one-at-a-time calls and none of the race's badges was ever said.
/// Started is now set with nothing awaited between it and the first line.
/// </summary>
public sealed class BugHunt11_AfterRaceATests
{
    [Fact]
    public void Honours_are_marked_started_only_once_the_first_line_is_queued()
    {
        var speak = WithoutComments(MethodBody(Source("RaceControlControl.AfterRace.cs"),
            "private async Task SpeakHonoursAsync(", "private const int MostColourSegments"));

        var render = speak.IndexOf("await WithinAsync(rendered, FollowOnWait, nextRace)", StringComparison.Ordinal);
        var handOff = speak.IndexOf("await HandOffInTimeAsync(handOff, \"Achievements\", nextRace)", StringComparison.Ordinal);
        var loop = speak.IndexOf("for (var i = 0; i < lines.Count; i++)", StringComparison.Ordinal);
        var firstSay = speak.IndexOf("Say(", loop, StringComparison.Ordinal);

        Assert.True(render > 0 && handOff > render && loop > handOff && firstSay > loop,
            "SpeakHonoursAsync no longer has the shape this test expects - update its bounds.");

        // Every Started = true is either the render-failure branch (which
        // returns at once, unchanged) or after both waits.
        var marks = AllIndexes(speak, "plan.Started = true;");
        Assert.NotEmpty(marks);
        Assert.All(marks, mark => Assert.True(mark > render,
            "Started is set before the render wait - a Skip or Hold there loses every held badge."));

        var segmentMark = marks.Last();
        Assert.True(segmentMark > handOff && segmentMark < loop,
            "Started is not set between the hand-over wait and the first line of the segment.");

        // Nothing awaited between marking the segment started and queueing
        // its first line: a stop in that window would drop the badges. (The
        // loop's own awaits are under i > 0, so line 0 goes straight out.)
        var between = speak[segmentMark..loop];
        Assert.DoesNotContain("await ", between, StringComparison.Ordinal);

        // The one other mark is in the render-failure branch, just before it returns.
        foreach (var mark in marks.Take(marks.Count - 1))
        {
            Assert.True(mark < handOff, "An extra Started mark sits after the hand-over wait.");
            var after = speak[mark..];
            Assert.StartsWith("plan.Started = true;\n            return;", after, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_finally_still_calls_held_badges_one_at_a_time_when_the_segment_had_not_started()
    {
        var source = Source("RaceControlControl.AfterRace.cs");

        Assert.Contains(
            "if (honours is { Started: false, Unlocks: { Count: > 0 } held } && recapShow.StoppedBy is not null && !recapShow.NextRaceCalled)",
            source, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------

    private static List<int> AllIndexes(string text, string what)
    {
        var found = new List<int>();

        for (var at = text.IndexOf(what, StringComparison.Ordinal); at >= 0; at = text.IndexOf(what, at + 1, StringComparison.Ordinal))
        {
            found.Add(at);
        }

        return found;
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

    // Line endings made LF, so a test that names a line break works on a
    // checkout that turned them into CRLF.
    private static string Source(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName)).Replace("\r\n", "\n");

    private static string WithoutComments(string body) =>
        string.Join(
            "\n",
            body.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string MethodBody(string source, string from, string until)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);

        Assert.True(start >= 0, $"\"{from}\" is not in the file any more - this test needs its name updated.");

        var rest = source[start..];
        var end = rest.IndexOf(until, StringComparison.Ordinal);

        Assert.True(end >= 0, $"\"{until}\" no longer follows \"{from}\" - this test needs its bounds updated.");

        return rest[..end];
    }
}
