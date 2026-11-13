namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, a driver's own piece and the place it states.
///
/// WHY. The saved Position was said as it stood. A disqualified or retired
/// car still has one (the result editor numbers every row, those last), so a
/// black flag read "finished fifth of 5" as a plain finish and could be named
/// their best of the night. And an older result with a ghost row ahead made
/// the real winner "second of 3", with no win counted.
/// </summary>
public sealed class BugHunt17_DriverCutTests : IDisposable
{
    private readonly TempAppData _appData = new();

    public void Dispose() => _appData.Dispose();

    private static readonly DateTime Green = new(2026, 11, 13, 19, 0, 0, DateTimeKind.Utc);

    private static SessionResultEntry Row(string name, int position, string outcome = "", string? transponder = null) =>
        new()
        {
            Transponder = transponder ?? name.ToLowerInvariant(),
            DriverName = name,
            Position = position,
            Laps = 3,
            LapTimesMs = new List<long> { 10000 + position * 100, 9000 + position * 100, 9000 + position * 100 },
            StartPosition = position,
            ElapsedMs = 28000 + position * 300,
            Outcome = outcome,
        };

    private static SessionRecord Heat(params SessionResultEntry[] rows) =>
        new()
        {
            Id = "heat-2",
            SessionType = RaceSessionType.Race,
            Label = "Heat 2",
            TrackLayoutName = "Winter Indoor",
            StartedAtUtc = Green,
            EndedAtUtc = Green.AddMinutes(8),
            Results = rows.ToList(),
        };

    [Theory]
    [InlineData("DQ", "was disqualified")]
    [InlineData("DNF", "did not finish")]
    public void A_disqualified_or_retired_car_is_not_said_to_have_finished(string outcome, string said)
    {
        var heat = Heat(Row("Bill", 1), Row("Ann", 2), Row("Joe", 3), Row("Kim", 4), Row("Sam", 5, outcome));

        var facts = DriverCut.FactsFor("sam", "Sam", new List<SessionRecord> { heat });

        Assert.Contains($"Sam raced Heat 2 and {said}.", facts.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("finished fifth", facts.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("best finish", facts.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ghost_ahead_in_an_older_result_does_not_cost_the_winner_the_win()
    {
        var heat = Heat(
            Row("Ghost", 1, transponder: Ghosts.Prefix + "PB"),
            Row("Sam", 2),
            Row("Bill", 3),
            Row("Ann", 4));

        var sam = DriverCut.FactsFor("sam", "Sam", new List<SessionRecord> { heat });
        var ann = DriverCut.FactsFor("ann", "Ann", new List<SessionRecord> { heat });

        Assert.Contains("finished first of 3", sam.Text, StringComparison.Ordinal);
        Assert.Contains("and won it", sam.Text, StringComparison.Ordinal);
        Assert.Contains("finished third of 3", ann.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ordinary_finish_reads_as_it_did()
    {
        var heat = Heat(Row("Bill", 1), Row("Sam", 2), Row("Ann", 3));

        var facts = DriverCut.FactsFor("sam", "Sam", new List<SessionRecord> { heat });

        Assert.Contains("Sam raced Heat 2 and finished second of 3.", facts.Text, StringComparison.Ordinal);
        Assert.Contains("with second their best finish", facts.Text, StringComparison.Ordinal);
    }
}
