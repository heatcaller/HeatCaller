namespace HeatCaller.Tests;

/// <summary>
/// Race-night build plan, Phase 4 (11 Nov 2026): marshal swaps - a driver's
/// post for one round covered by somebody else, kept on the round and read
/// by the roster, the marshal call and the phones alike.
/// </summary>
public sealed class MarshalSwapTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly TrackLayout Oval = new()
    {
        Name = "Club Oval",
        MarshalPostCount = 2,
        MarshalPostNames = new List<string> { "Hairpin", "Sweeper" },
    };

    private static RoundDefinition Heat(int order, string label, params string[] grid) => new()
    {
        Order = order,
        ClassName = "Stock",
        RoundLabel = label,
        TrackLayoutName = "Club Oval",
        AssignedTransponders = grid.ToList(),
    };

    [Fact]
    public void A_covered_post_goes_to_the_driver_covering_it()
    {
        var heat1 = Heat(1, "Heat 1", "A", "B");
        var heat2 = Heat(2, "Heat 2", "C", "D");
        heat2.MarshalCovers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["A"] = "Z" };

        var posts = MarshalAssigner.Assign(new[] { heat1, heat2 }, Oval, ProfileStore.Load())[1].Posts;

        Assert.Equal(new[] { "Z", "B" }, posts.Select(p => p.Transponder));
        Assert.Contains("(covering A)", posts[0].DriverName);
    }

    [Fact]
    public void Cover_by_a_driver_in_the_race_is_ignored()
    {
        var heat1 = Heat(1, "Heat 1", "A", "B");
        var heat2 = Heat(2, "Heat 2", "C", "D");
        heat2.MarshalCovers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["A"] = "C" };

        var posts = MarshalAssigner.Assign(new[] { heat1, heat2 }, Oval, ProfileStore.Load())[1].Posts;

        Assert.Equal(new[] { "A", "B" }, posts.Select(p => p.Transponder));
    }

    [Fact]
    public void No_covers_is_the_heat_before_as_always()
    {
        var heat1 = Heat(1, "Heat 1", "A", "B");
        var heat2 = Heat(2, "Heat 2", "C", "D");

        var posts = MarshalAssigner.Assign(new[] { heat1, heat2 }, Oval, ProfileStore.Load())[1].Posts;

        Assert.Equal(new[] { "A", "B" }, posts.Select(p => p.Transponder));
    }

    [Fact]
    public void Covers_are_saved_with_the_round()
    {
        var rounds = RoundDefinitionStore.Load();
        var heat = Heat(1, "Heat 2", "C");
        heat.MarshalCovers = new Dictionary<string, string> { ["A"] = "Z" };
        rounds.Add(heat);

        var again = RoundDefinitionStore.Load().Get(heat.Id);

        Assert.Equal("Z", again!.MarshalCovers!["A"]);
    }
}
