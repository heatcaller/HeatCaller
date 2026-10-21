namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 8, RoundDefinitionStore: a race-rounds.json holding a stray null
/// entry must load as the real rounds without the null, not a list every
/// reader throws on.
/// </summary>
public sealed class BugHunt8_RoundDefinitionStore_csTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Load_DropsNullEntries_AndReadersWork()
    {
        var path = _data.PathOf("race-rounds.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            """[ { "Id": "a1", "Order": 0, "ClassName": "Stock" }, null, { "Id": "b2", "Order": 1, "ClassName": "Stock" } ]""");

        var store = RoundDefinitionStore.Load();

        Assert.False(store.LoadedUnreadable);
        Assert.Equal(new[] { "a1", "b2" }, store.All.Select(r => r.Id).ToArray());
        Assert.Equal(2, store.ForClass("Stock").Count);
        Assert.Equal(2, store.Unrecorded().Count);
        Assert.NotNull(store.Get("b2"));

        store.Add(new RoundDefinition { Id = "c3", ClassName = "Stock" });
        Assert.Equal(2, store.Get("c3")!.Order);
    }
}
