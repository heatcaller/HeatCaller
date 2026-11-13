namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: the A-main overall names each leg by its own number, scores
/// a car that never crossed the line as not started, and takes only this
/// A-main's legs - not the class's legs from an earlier meeting.
/// </summary>
public sealed class BugHunt17_AMainLegsTests
{
    private static readonly DateTime Tonight = new(2026, 11, 13, 19, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Legs_are_named_by_their_own_number_when_one_is_missing()
    {
        // Leg 2 was skipped: legs 1 and 3 are the recorded ones.
        var legs = new[] { Leg(("A", 1), ("B", 2)), Leg(("B", 1), ("A", 2)) };

        var overall = AMainLegs.Overall(legs, 2, null, new[] { 1, 3 });

        Assert.Equal("Leg 1 2nd (2 pts), Leg 3 1st (1 pt)", overall.Single(s => s.Transponder == "B").Detail);
    }

    [Fact]
    public void A_lone_recorded_leg_is_named_by_its_own_number()
    {
        var overall = AMainLegs.Overall(new[] { Leg(("A", 1), ("B", 2)) }, 2, null, new[] { 3 });

        Assert.Equal("1 pt (Leg 3)", overall[0].Words());
        Assert.StartsWith("Leg 3 1st", overall[0].Detail);
    }

    [Fact]
    public void A_row_with_no_laps_did_not_start_the_leg()
    {
        // C and D never left the line in leg 2, but their rows were filed.
        var legs = new[]
        {
            Leg(("A", 1), ("B", 2), ("C", 3), ("D", 4)),
            new SessionRecord { Results = new List<SessionResultEntry> { R("A", 1), R("B", 2), NoLaps("C", 3), NoLaps("D", 4) } },
        };

        var overall = AMainLegs.Overall(legs, 2);

        // Field of 4 -> 5 points each, not 3 and 4 by row order.
        var c = overall.Single(s => s.Transponder == "C");
        var d = overall.Single(s => s.Transponder == "D");

        Assert.Equal(8, c.Points);
        Assert.Equal(9, d.Points);
        Assert.Contains("Leg 2 DNS (5 pts)", c.Detail);
        Assert.Contains("Leg 2 DNS (5 pts)", d.Detail);
    }

    [Fact]
    public void Legs_from_an_earlier_meeting_are_not_scored_into_tonights_overall()
    {
        var sessions = new Dictionary<string, SessionRecord>
        {
            // Last month: B won every leg.
            ["old1"] = Leg(("B", 1), ("A", 2), ("C", 3)),
            ["old2"] = Leg(("B", 1), ("A", 2), ("C", 3)),
            ["old3"] = Leg(("B", 1), ("A", 2), ("C", 3)),
            ["s1"] = Leg(("A", 1), ("B", 2), ("C", 3)),
            ["s2"] = Leg(("A", 1), ("C", 2), ("B", 3)),
        };
        var old = LegRounds(Tonight.AddDays(-30), "old1", "old2", "old3");
        var tonight = LegRounds(Tonight, "s1", "s2", null);

        // No race day open: the scope is every round there is.
        var decided = AMainLegs.AfterLastLeg(
            tonight[2], Leg(("C", 1), ("A", 2), ("B", 3)), old.Concat(tonight), sessions.GetValueOrDefault, 0);

        Assert.NotNull(decided);
        Assert.Equal(3, decided!.Legs);
        Assert.Equal(2, decided.Counting);
        Assert.Equal("A", decided.Winner.Transponder);
        Assert.Equal(2, decided.Winner.Points);
    }

    [Fact]
    public void An_old_leg_that_never_ran_does_not_silence_tonights_overall()
    {
        var sessions = new Dictionary<string, SessionRecord>
        {
            ["old1"] = Leg(("B", 1), ("A", 2)),
            ["s1"] = Leg(("A", 1), ("B", 2)),
        };
        var old = LegRounds(Tonight.AddDays(-30), "old1", null);
        var tonight = LegRounds(Tonight, "s1", null);

        var decided = AMainLegs.AfterLastLeg(
            tonight[1], Leg(("A", 1), ("B", 2)), old.Concat(tonight), sessions.GetValueOrDefault, 0);

        Assert.NotNull(decided);
        Assert.Equal(2, decided!.Legs);
        Assert.Equal("A", decided.Winner.Transponder);
    }

    private static List<RoundDefinition> LegRounds(DateTime built, params string?[] linked) =>
        Enumerable.Range(1, linked.Length).Select(leg => new RoundDefinition
        {
            ClassName = "Buggy",
            Kind = RoundKind.Main,
            MainTier = 0,
            MainLeg = leg,
            MainLegCount = linked.Length,
            RoundLabel = AMainLegs.LegLabel(leg),
            CreatedAtUtc = built.AddMilliseconds(leg),
            LinkedSessionId = linked[leg - 1],
        }).ToList();

    private static SessionResultEntry R(string transponder, int position) => new()
    {
        Transponder = transponder,
        DriverName = transponder,
        Position = position,
        Laps = 20 - position,
        ElapsedMs = 300_000,
    };

    private static SessionResultEntry NoLaps(string transponder, int position) => new()
    {
        Transponder = transponder,
        DriverName = transponder,
        Position = position,
    };

    private static SessionRecord Leg(params (string Transponder, int Position)[] finishers) => new()
    {
        Results = finishers.Select(f => R(f.Transponder, f.Position)).ToList(),
    };
}
