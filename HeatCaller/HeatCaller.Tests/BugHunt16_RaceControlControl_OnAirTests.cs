namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 16 (10 Nov 2026), RaceControlControl.OnAir.cs.
///
/// Source checks, as the rest of Race Control's are (OnAirSkipSilencesTests,
/// RaceControlMorphTests): the page cannot be built in a test.
/// </summary>
public sealed class BugHunt16_RaceControlControl_OnAirTests
{
    /// <summary>
    /// THE BIG SCREEN'S INTRO CARDS FOR A REAL RACE. They were armed only when
    /// OnAirAsync was given a round - and only a demo gives one. Every real
    /// start (Start Next, the Program, F1, the phone, 0) passes null and lets
    /// WriteOpeningAsync read the Round box, so the introductions were said
    /// but no cards were ever armed, and the Stage handed over to skips its
    /// own introductions, the only other place they are armed.
    /// </summary>
    [Fact]
    public void Intro_cards_are_armed_from_the_round_the_opening_was_written_for()
    {
        var onAir = WithoutComments(Source("RaceControlControl.OnAir.cs"));

        var cards = MethodBody(onAir, "if (said.Introductions is { } saidIntroductions)", "ArmIntroCards(");

        Assert.DoesNotContain("round is not null", cards, StringComparison.Ordinal);
        Assert.Contains("said.Round is { } introRound", cards, StringComparison.Ordinal);
        Assert.Contains("ReadIntroductionFacts(introRound,", cards, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_opening_run_WriteOpeningAsync_hands_back_carries_its_round()
    {
        var onAir = WithoutComments(Source("RaceControlControl.OnAir.cs"));

        Assert.Contains("RoundDefinition? Round = null);", onAir, StringComparison.Ordinal);

        var write = MethodBody(onAir, "private async Task<OpeningRun?> WriteOpeningAsync(", "private async Task<OpeningRun?> PitLaneOnlyAsync(");

        var returns = write.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("return ", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(returns);

        foreach (var line in returns)
        {
            Assert.True(
                line == "return null;"
                    || line.StartsWith("return ForRound(", StringComparison.Ordinal)
                    || line == "return new OpeningRun(",
                $"WriteOpeningAsync must mark every run with its round: {line}");
        }

        var built = MethodBody(write, "return new OpeningRun(", ");");
        Assert.Contains("Round: round", built, StringComparison.Ordinal);
    }

    /// <summary>
    /// A DEMO STARTED WHILE ON AIR IS RUNNING. The On Air already going was
    /// cancelled, but what it had queued - the other race's opening, its
    /// driver introductions and pit lane, all priority Reminders - played on
    /// over the demo, and the demo's staging call waited behind them. Every
    /// other way On Air is cut short silences it first (SilenceOnAir).
    /// </summary>
    [Fact]
    public void A_demo_started_during_On_Air_silences_what_On_Air_queued_before_starting()
    {
        var onAir = WithoutComments(Source("RaceControlControl.OnAir.cs"));

        var runOnAir = MethodBody(onAir, "private async Task RunOnAirAsync(", "private async Task RunOnAirHoldingPracticeAsync(");
        var demo = MethodBody(runOnAir, "if (start is not null)", "_onAirStageNow = true;");

        var cancelled = demo.IndexOf("running.Cancel", StringComparison.Ordinal);
        var silenced = demo.IndexOf("SilenceOnAir(", StringComparison.Ordinal);
        var started = demo.IndexOf("start();", StringComparison.Ordinal);

        Assert.True(cancelled > 0, "The demo branch must still call off the On Air already running.");
        Assert.True(silenced > cancelled, "The demo branch must silence what On Air had queued, after cancelling it.");
        Assert.True(started > silenced, "The silence must come before the demo starts, so its staging call is queued after the cut.");
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
