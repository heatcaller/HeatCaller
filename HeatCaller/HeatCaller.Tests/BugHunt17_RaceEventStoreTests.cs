namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: renaming a class rewrote each race day's ClassNames only, so
/// the day's class running order and its declined late entries went on
/// naming the old class - the renamed class ran after every listed one, and
/// a declined late entry was asked about again.
/// </summary>
public sealed class BugHunt17_RaceEventStoreTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Renaming_a_class_moves_it_in_the_days_class_order_and_declined_late_entries()
    {
        var events = RaceEventStore.Load();
        var tonight = new RaceEvent
        {
            Name = "Tuesday",
            ClassNames = new List<string> { "Juniors", "Stock", "Mod" },
            ClassRunningOrder = new List<string> { "Juniors", "Stock", "Mod" },
            LateEntriesDeclined = new List<string> { LateArrivals.Key("Stock", "1234"), LateArrivals.Key("Mod", "5678") },
        };
        events.Upsert(tonight);

        var moved = events.RenameClass("stock", "Stock 17.5");

        Assert.Equal(new[] { tonight.Id }, moved);

        var saved = RaceEventStore.Load().Get(tonight.Id)!;
        Assert.Equal(new[] { "Juniors", "Stock 17.5", "Mod" }, saved.ClassRunningOrder);
        Assert.Equal(new[] { "Stock 17.5|1234", "Mod|5678" }, saved.LateEntriesDeclined);
    }

    [Fact]
    public void A_day_that_only_names_the_class_in_its_order_is_saved_but_not_reported_as_listing_it()
    {
        var events = RaceEventStore.Load();
        var day = new RaceEvent { Name = "Template night", ClassRunningOrder = new List<string> { "Stock", "Mod" } };
        events.Upsert(day);

        // The returned list is the days whose ClassNames moved - the undo's.
        Assert.Empty(events.RenameClass("Stock", "Stock 17.5"));
        Assert.Equal(new[] { "Stock 17.5", "Mod" }, RaceEventStore.Load().Get(day.Id)!.ClassRunningOrder);
    }

    [Fact]
    public void Undoing_the_rename_puts_the_order_and_the_declined_entries_back_on_the_days_it_moved()
    {
        var events = RaceEventStore.Load();
        var tonight = new RaceEvent
        {
            Name = "Tuesday",
            ClassNames = new List<string> { "Stock", "Mod" },
            ClassRunningOrder = new List<string> { "Mod", "Stock" },
            LateEntriesDeclined = new List<string> { "Stock|1234" },
        };

        // Listed the new name all along - left over from a deleted class.
        var other = new RaceEvent
        {
            Name = "Saturday",
            ClassNames = new List<string> { "Stock 17.5" },
            ClassRunningOrder = new List<string> { "Stock 17.5" },
            LateEntriesDeclined = new List<string> { "Stock 17.5|9999" },
        };
        events.Upsert(tonight);
        events.Upsert(other);

        var already = events.WithClassName("Stock 17.5");
        var moved = events.RenameClass("Stock", "Stock 17.5");
        events.UndoRenameClass("Stock 17.5", "Stock", already, moved);

        var reloaded = RaceEventStore.Load();
        Assert.Equal(new[] { "Mod", "Stock" }, reloaded.Get(tonight.Id)!.ClassRunningOrder);
        Assert.Equal(new[] { "Stock|1234" }, reloaded.Get(tonight.Id)!.LateEntriesDeclined);
        Assert.Equal(new[] { "Stock 17.5" }, reloaded.Get(other.Id)!.ClassRunningOrder);
        Assert.Equal(new[] { "Stock 17.5|9999" }, reloaded.Get(other.Id)!.LateEntriesDeclined);
    }
}
