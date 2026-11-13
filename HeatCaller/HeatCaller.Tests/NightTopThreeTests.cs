namespace HeatCaller.Tests;

/// <summary>A race's top three on a driver's night, for the result push's leaderboard (12 Nov 2026).</summary>
public sealed class NightTopThreeTests
{
    private static SessionResultEntry Row(int pos, string name, string tp, int laps) =>
        new() { Position = pos, DriverName = name, Transponder = tp, Laps = laps };

    [Fact]
    public void The_top_three_in_order_with_their_laps()
    {
        var results = new List<SessionResultEntry>
        {
            Row(4, "Dee Four", "4", 17), Row(2, "Bea Two", "2", 19), Row(1, "Ann One", "1", 20), Row(3, "Cal Three", "3", 18),
        };

        var top = DriverNightExtras.TopOf(results, practice: false, abbreviate: false)!;

        Assert.Equal(new[] { 1, 2, 3 }, top.Select(t => t.Pos));
        Assert.Equal(new[] { 20, 19, 18 }, top.Select(t => t.Laps));
        Assert.All(top, t => Assert.False(string.IsNullOrWhiteSpace(t.Name)));
    }

    [Fact]
    public void None_for_practice_or_a_race_of_one()
    {
        var results = new List<SessionResultEntry> { Row(1, "Ann One", "1", 20), Row(2, "Bea Two", "2", 19) };

        Assert.Null(DriverNightExtras.TopOf(results, practice: true, abbreviate: false));
        Assert.Null(DriverNightExtras.TopOf(results.Take(1).ToList(), practice: false, abbreviate: false));
    }
}
