namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), RaceControlControl.BetweenRaces.cs.
///
/// A source check, as the rest of Race Control's are: the page cannot be
/// built in a test.
/// </summary>
public sealed class BugHunt17_RaceControlControl_BetweenRacesTests
{
    /// <summary>
    /// THE NEW DRIVERS' WELCOME, MARKED AS SAID WITHOUT BEING HEARD. It ran
    /// before the tick's "race started" and "muted or busy" returns and never
    /// asked about Hold Talk: a muted announcer marked the newcomers welcomed
    /// for the night in silence, and on the tick Auto start started the race
    /// the welcome went to the race after's newcomers.
    /// </summary>
    [Fact]
    public void The_welcome_waits_behind_the_race_started_muted_and_Hold_Talk_guards()
    {
        var tick = WithoutComments(MethodBody(Source("RaceControlControl.BetweenRaces.cs"),
            "private void BetweenRacesTick()", "private void SayCall("));

        var autoStart = tick.IndexOf("AutoStartTick(now, facts, busy);", StringComparison.Ordinal);
        var started = tick.IndexOf("if (_race.State != RaceState.Finished)", autoStart, StringComparison.Ordinal);
        var muted = tick.IndexOf("if (_announcer.Muted || busy)", StringComparison.Ordinal);
        var welcome = tick.IndexOf("WelcomeNewDrivers(busy);", StringComparison.Ordinal);

        Assert.True(autoStart > 0 && started > autoStart && muted > started && welcome > muted,
            "The welcome is said (and marked as said) before the tick knows the race has not started and the announcer is not muted.");

        Assert.EndsWith("if (!held)\n        {\n            ", tick[..welcome], StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------

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
        var end = rest.IndexOf(until, from.Length, StringComparison.Ordinal);

        Assert.True(end >= 0, $"\"{until}\" no longer follows \"{from}\" - this test needs its bounds updated.");

        return rest[..end];
    }
}
