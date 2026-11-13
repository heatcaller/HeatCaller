namespace HeatCaller.Tests;

/// <summary>
/// Race-night build plan, Phase 1 (11 Nov 2026), the rest-time guard: a
/// round's heats go in an order that gives a driver in two classes a race
/// off between their races, keeping the built order wherever it can.
/// </summary>
public sealed class RestOrderTests
{
    private static IReadOnlyCollection<string> Heat(params string[] drivers) => drivers;

    [Fact]
    public void Heats_with_nobody_in_common_keep_their_order()
    {
        var heats = new[] { Heat("a", "b"), Heat("c", "d"), Heat("e", "f") };

        Assert.Equal(new[] { 0, 1, 2 }, RestOrder.Arrange(heats));
    }

    [Fact]
    public void A_driver_in_the_last_stock_heat_and_the_first_mod_heat_gets_a_race_off()
    {
        // Stock heats 1-2, then Mod heats 1-2; Sam is in Stock 2 and Mod 1.
        var heats = new[] { Heat("a", "b"), Heat("c", "sam"), Heat("SAM", "d"), Heat("e", "f") };

        var order = RestOrder.Arrange(heats);

        Assert.Equal(new[] { 0, 1, 3, 2 }, order);
        Assert.Equal(1, RestOrder.BackToBacks(heats));
        Assert.Equal(0, RestOrder.BackToBacks(order.Select(i => heats[i]).ToList()));
    }

    [Fact]
    public void The_race_before_the_round_counts_too()
    {
        // Sam ran the last heat of Q1, so Q2 must not open with him.
        var heats = new[] { Heat("sam", "a"), Heat("b", "c") };

        Assert.Equal(new[] { 1, 0 }, RestOrder.Arrange(heats, before: Heat("sam", "x")));
    }

    [Fact]
    public void When_every_heat_clashes_the_built_order_stands()
    {
        // One tiny class: everybody is in every heat.
        var heats = new[] { Heat("a", "b"), Heat("a", "b") };

        Assert.Equal(new[] { 0, 1 }, RestOrder.Arrange(heats));
    }

    [Fact]
    public void Nothing_to_order_is_nothing()
    {
        Assert.Empty(RestOrder.Arrange(Array.Empty<IReadOnlyCollection<string>>()));
    }

    [Fact]
    public void The_build_adds_each_rounds_heats_in_rest_friendly_order()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        var source = File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", "RaceProgramControl.cs"));

        // 12 Nov 2026, setup plan step 8: with tonight's gap, not always one.
        Assert.Contains("var order = RestOrder.Arrange(rosters, recentHeats, restGap);", source);
    }
}
