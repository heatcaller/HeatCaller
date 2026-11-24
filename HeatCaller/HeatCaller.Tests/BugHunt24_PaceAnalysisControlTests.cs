namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, #221 (F192): the Pace page's result clause measured lap
/// margins against a disqualified driver's zeroed laps, telling the DQ'd
/// driver they were "18 laps down" and the winner of a two-car race that they
/// "won it by 18 laps". A DQ now reads "Disqualified." and is never the
/// runner-up a winning margin is measured against.
/// </summary>
public sealed class BugHunt24_PaceAnalysisControlTests
{
    private static SessionResultEntry Row(int position, string transponder, string name, int laps, long elapsedMs, string outcome = "") => new()
    {
        Position = position,
        Transponder = transponder,
        DriverName = name,
        Laps = laps,
        ElapsedMs = elapsedMs,
        Outcome = outcome,
    };

    private static SessionRecord Race(params SessionResultEntry[] rows) => new()
    {
        SessionType = RaceSessionType.Race,
        Label = "A-Main",
        StartedAtUtc = new DateTime(2026, 11, 7, 20, 0, 0, DateTimeKind.Utc),
        Results = rows.ToList(),
    };

    [Fact]
    public void BugHunt24_A_disqualified_driver_is_not_given_a_lap_margin()
    {
        var a = Row(1, "1001", "Sam Okafor", 18, 300_000);
        var b = Row(2, "1002", "Leo Martins", 18, 302_000);
        var dq = Row(3, "1003", "Priya Shah", 0, 301_000, ResultEditor.Dq);
        var race = Race(a, b, dq);

        var clause = PaceAnalysisControl.ResultClause(race, dq);

        Assert.Equal("Disqualified.", clause);
        Assert.DoesNotContain("laps down", clause);
    }

    [Fact]
    public void BugHunt24_A_winner_whose_only_rival_was_disqualified_has_no_margin()
    {
        var a = Row(1, "1001", "Sam Okafor", 18, 300_000);
        var dq = Row(2, "1002", "Leo Martins", 0, 301_000, "dsq");
        var race = Race(a, dq);

        Assert.Equal("Won it.", PaceAnalysisControl.ResultClause(race, a));
    }

    [Fact]
    public void BugHunt24_A_winner_is_measured_against_the_real_runner_up_not_a_disqualified_car()
    {
        var a = Row(1, "1001", "Sam Okafor", 18, 300_000);
        var b = Row(2, "1002", "Leo Martins", 17, 302_000);
        var dq = Row(3, "1003", "Priya Shah", 0, 301_000, ResultEditor.Dq);
        var race = Race(a, b, dq);

        Assert.Equal("Won it by 1 lap.", PaceAnalysisControl.ResultClause(race, a));
        Assert.Equal("P2 of 3, 1 lap down on Sam Okafor.", PaceAnalysisControl.ResultClause(race, b));
    }
}
