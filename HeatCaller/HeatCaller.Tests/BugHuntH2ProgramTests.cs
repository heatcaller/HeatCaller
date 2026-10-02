namespace HeatCaller.Tests;

/// <summary>
/// Rebuilding qualifying after some heats have run: the rounds that already
/// have a recorded heat must not be built (or have their unrun heats
/// replaced) again, or drivers end up with a second Q1.
/// </summary>
public class BugHuntH2ProgramTests
{
    private static RoundDefinition Qualifier(string label, bool recorded = false) => new()
    {
        ClassName = "Stock",
        RoundLabel = label,
        Kind = RoundKind.Qualifier,
        LinkedSessionId = recorded ? Guid.NewGuid().ToString("N") : null,
    };

    [Fact]
    public void RecordedRoundsAreReportedAsRun()
    {
        var day = new List<RoundDefinition>
        {
            Qualifier("Q1 Heat 1", recorded: true),
            Qualifier("Q1 Heat 2", recorded: true),
            Qualifier("Q2 Heat 1"),
            Qualifier("Q2 Heat 2"),
            Qualifier("Q3 Heat 1"),
        };

        Assert.Equal(new[] { 1 }, RaceProgramControl.QualifyingRoundsAlreadyRun(day).OrderBy(n => n));
    }

    [Fact]
    public void UnrunHeatsOfARoundUnderWayAreNotReplaced()
    {
        var q1h1 = Qualifier("Q1 Heat 1", recorded: true);
        var q1h2 = Qualifier("Q1 Heat 2");
        var q2h1 = Qualifier("Q2 Heat 1");
        var day = new List<RoundDefinition> { q1h1, q1h2, q2h1 };

        var replaceable = RaceProgramControl.ReplaceableQualifiers(day);

        Assert.DoesNotContain(q1h1, replaceable);
        Assert.DoesNotContain(q1h2, replaceable);
        Assert.Contains(q2h1, replaceable);
    }

    [Fact]
    public void AnUnnumberedOneRoundNightCountsAsRoundOne()
    {
        var day = new List<RoundDefinition>
        {
            Qualifier("Heat 1", recorded: true),
            Qualifier("Heat 2"),
        };

        Assert.Equal(new[] { 1 }, RaceProgramControl.QualifyingRoundsAlreadyRun(day).ToArray());
        Assert.Empty(RaceProgramControl.ReplaceableQualifiers(day));
    }

    [Fact]
    public void ClubNightRaceNumbersAreRounds()
    {
        var day = new List<RoundDefinition>
        {
            Qualifier("Race 1", recorded: true),
            Qualifier("Race 2"),
        };

        Assert.Equal(new[] { 1 }, RaceProgramControl.QualifyingRoundsAlreadyRun(day).ToArray());
        Assert.Single(RaceProgramControl.ReplaceableQualifiers(day));
    }

    [Fact]
    public void NothingRunMeansEveryUnrunQualifierIsReplaced()
    {
        var day = new List<RoundDefinition>
        {
            Qualifier("Q1 Heat 1"),
            Qualifier("Q2 Heat 1"),
            new() { ClassName = "Stock", RoundLabel = "A-Main", Kind = RoundKind.Main, MainTier = 0 },
        };

        Assert.Empty(RaceProgramControl.QualifyingRoundsAlreadyRun(day));
        Assert.Equal(2, RaceProgramControl.ReplaceableQualifiers(day).Count);
    }

    [Fact]
    public void AHandAddedUnnumberedHeatBesideNumberedRoundsMarksNoRound()
    {
        var day = new List<RoundDefinition>
        {
            Qualifier("Q1 Heat 1"),
            Qualifier("Extra Heat", recorded: true),
        };

        Assert.Empty(RaceProgramControl.QualifyingRoundsAlreadyRun(day));
    }
}
