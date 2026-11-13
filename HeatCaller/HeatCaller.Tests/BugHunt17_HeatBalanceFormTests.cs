namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: Group by pace and Spread by pace pooled everybody in a
/// round's heats, the other class's drivers in a shared heat included. With
/// no pace in this class they sorted last and were dealt into a heat nobody
/// shares - to race, and be scored, as this class. They now stay in the
/// heat they share.
/// </summary>
public sealed class BugHunt17_HeatBalanceFormTests
{
    private static readonly Dictionary<string, long> Pace = new(StringComparer.OrdinalIgnoreCase)
    {
        ["S1"] = 18_000,
        ["S2"] = 18_100,
        ["S3"] = 18_200,
        ["S4"] = 18_300,
        ["S5"] = 18_400,
    };

    private static List<(RoundDefinition Round, IReadOnlyList<string> Drivers)> StockWithModInHeatTwo()
    {
        var one = new RoundDefinition
        {
            ClassName = "Stock",
            RoundLabel = "Q1 Heat 1",
            Kind = RoundKind.Qualifier,
            AssignedTransponders = new() { "S1", "S2", "S3" },
        };

        var two = new RoundDefinition
        {
            ClassName = "Stock",
            RoundLabel = "Q1 Heat 2",
            Kind = RoundKind.Qualifier,
            AssignedTransponders = new() { "S4", "M1", "S5", "M2", "M3" },
            SharedClasses = new() { "Mod" },
            SharedClassOf = new()
            {
                ["S4"] = "Stock",
                ["S5"] = "Stock",
                ["M1"] = "Mod",
                ["M2"] = "Mod",
                ["M3"] = "Mod",
            },
        };

        return new()
        {
            (one, one.AssignedTransponders),
            (two, two.AssignedTransponders),
        };
    }

    [Fact]
    public void Group_by_pace_leaves_the_other_class_in_the_heat_it_shares()
    {
        var seeded = HeatBalanceForm.SeedRoundByPace(StockWithModInHeatTwo(), Pace, group: true);

        // The fastest Stock drivers into the last heat, as many as it had.
        Assert.Equal(new[] { "S3", "S4", "S5" }, seeded[0]);
        Assert.Equal(new[] { "S1", "S2", "M1", "M2", "M3" }, seeded[1]);
    }

    [Fact]
    public void Spread_by_pace_leaves_the_other_class_in_the_heat_it_shares()
    {
        var seeded = HeatBalanceForm.SeedRoundByPace(StockWithModInHeatTwo(), Pace, group: false);

        Assert.Equal(3, seeded[0].Count);
        Assert.DoesNotContain(seeded[0], t => t.StartsWith('M'));
        Assert.Equal(new[] { "M1", "M2", "M3" }, seeded[1].Where(t => t.StartsWith('M')));
        Assert.Equal(2, seeded[1].Count(t => t.StartsWith('S')));
    }

    [Fact]
    public void Heats_nobody_shares_are_sorted_as_they_were()
    {
        var heats = StockWithModInHeatTwo();
        heats[1].Round.SharedClasses = null;
        heats[1].Round.SharedClassOf = null;

        var seeded = HeatBalanceForm.SeedRoundByPace(heats, Pace, group: true);

        Assert.Equal(
            HeatSeeding.Group(new[] { 3, 5 }, new[] { "S1", "S2", "S3", "S4", "M1", "S5", "M2", "M3" }, Pace),
            seeded);
    }
}
