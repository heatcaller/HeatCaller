namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10, events.json: an explicit JSON null (a null day, a null
/// list, a day with null RoundIds) parsed without error and then threw in
/// the startup migration, so the app could not open. Load now repairs the
/// file the way the other stores have since bug hunt 8.
/// </summary>
public sealed class BugHunt10_RaceEventStoreTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_null_day_and_null_lists_are_repaired_on_load()
    {
        File.WriteAllText(
            _data.PathOf("events.json"),
            "{\"Events\":[null,{\"Id\":\"a\",\"Name\":null,\"RoundIds\":null,\"ClassNames\":null}],\"ActiveEventId\":null}");

        var store = RaceEventStore.Load();

        Assert.False(store.LoadedUnreadable);
        Assert.Single(store.All);

        var day = store.Get("a");
        Assert.NotNull(day);
        Assert.NotNull(day!.RoundIds);
        Assert.Empty(day.RoundIds);
        Assert.NotNull(day.ClassNames);
        Assert.Equal(string.Empty, day.Name);
        Assert.Null(store.EventForRound("x"));
    }

    [Fact]
    public void Null_entries_inside_a_day_s_lists_are_dropped()
    {
        File.WriteAllText(
            _data.PathOf("events.json"),
            "{\"Events\":[{\"Id\":\"a\",\"RoundIds\":[null,\"r1\"],\"ClassNames\":[null,\"Stock\"]}]}");

        var store = RaceEventStore.Load();
        var day = store.Get("a")!;

        Assert.Equal(new[] { "r1" }, day.RoundIds);
        Assert.Equal(new[] { "Stock" }, day.ClassNames);
        Assert.Same(day, store.EventForRound("r1"));
    }

    [Fact]
    public void A_null_event_list_loads_as_empty_and_the_legacy_holder_can_be_made()
    {
        File.WriteAllText(_data.PathOf("events.json"), "{\"Events\":null}");

        var store = RaceEventStore.Load();

        Assert.False(store.LoadedUnreadable);
        Assert.Empty(store.All);

        var legacy = store.EnsureLegacy();
        Assert.Equal(RaceEventStore.LegacyEventId, legacy.Id);
    }
}
