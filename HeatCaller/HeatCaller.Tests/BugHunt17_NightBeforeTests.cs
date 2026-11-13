namespace HeatCaller.Tests;

/// <summary>
/// The preview and a heat classes share (13 Nov 2026, bug hunt 17).
///
/// A shared heat is filed under one class and carries every class's
/// drivers. The preview counted the whole roster as entries for the class
/// it is filed under and never named the others: three Stock and four Mod
/// went out as "7 cars on the entry list for Stock".
/// </summary>
public sealed class BugHunt17_NightBeforeTests
{
    private static readonly DateTime When = new(2026, 11, 14, 10, 15, 0, DateTimeKind.Local);

    private static RoundDefinition Round(string className, string label, params string[] grid) =>
        new()
        {
            ClassName = className,
            RoundLabel = label,
            Kind = RoundKind.Qualifier,
            AssignedTransponders = grid.ToList(),
        };

    [Fact]
    public void A_shared_heat_counts_each_class_its_own_drivers_and_names_every_class()
    {
        var shared = Round("Stock", "Q1 Heat 1", "101", "102", "103");
        MixedHeats.RunTogether(shared, Round("Mod", "Q1 Heat 2", "201", "202", "203", "204"));

        var text = NightBefore.FactsFor("Club Night", When, "10:15", new[] { shared }, null, null).Text;

        Assert.Contains("across 2 classes: Stock, Mod", text);
        Assert.Contains("4 cars on the entry list for Mod so far", text);

        // Three Stock cars is under the least worth saying, and seven was never true.
        Assert.DoesNotContain("on the entry list for Stock", text);
        Assert.DoesNotContain("7 cars", text);
    }

    [Fact]
    public void A_class_sharing_one_heat_and_running_its_own_main_is_counted_once_per_car()
    {
        var shared = Round("Stock", "Q1 Heat 1", "101", "102", "103", "104");
        MixedHeats.RunTogether(shared, Round("Mod", "Q1 Heat 2", "201", "202", "203", "204"));

        var modMain = Round("Mod", "A-Main", "201", "202", "203", "204", "205");
        modMain.Kind = RoundKind.Main;

        var text = NightBefore.FactsFor("Club Night", When, "10:15", new[] { shared, modMain }, null, null).Text;

        Assert.Contains("4 cars on the entry list for Stock so far", text);
        Assert.Contains("5 cars on the entry list for Mod so far", text);
    }

    [Fact]
    public void A_one_class_heat_counts_as_it_always_did()
    {
        var text = NightBefore.FactsFor(
            "Club Night", When, "10:15",
            new[] { Round("Stock", "Q1 Heat 1", "101", "102", "103", "104") },
            null, null).Text;

        Assert.Contains("across 1 class: Stock", text);
        Assert.Contains("4 cars on the entry list for Stock so far", text);
    }
}
