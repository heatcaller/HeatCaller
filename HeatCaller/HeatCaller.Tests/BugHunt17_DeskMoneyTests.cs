namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, entry-payments.json and pre-entries.json that could not be
/// read at startup.
///
/// WHY. Both stores swallowed the failed read and started empty with their
/// path still set, so the first driver marked paid (or the first phone "I'm
/// coming") wrote that one record over the club's whole file - every earlier
/// race day's takings, with no copy kept. They now hold their writes the way
/// the other stores do.
/// </summary>
public sealed class BugHunt17_DeskMoneyTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static EntryPayment Cash(decimal amount) => new() { How = PaidHow.Cash, Amount = amount };

    [Fact]
    public void A_payments_file_held_open_at_startup_is_not_saved_over()
    {
        EntryPaymentStore.Load().Set("day-1", "111", Cash(15));
        var before = File.ReadAllText(_data.PathOf("entry-payments.json"));

        EntryPaymentStore store;

        using (new FileStream(_data.PathOf("entry-payments.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            store = EntryPaymentStore.Load();
        }

        Assert.True(store.LoadedUnreadable);

        // The desk carries on in memory; the club's file stays as it was.
        store.Set("day-2", "222", Cash(20));
        Assert.NotNull(store.Get("day-2", "222"));
        Assert.Equal(before, File.ReadAllText(_data.PathOf("entry-payments.json")));
        Assert.NotNull(EntryPaymentStore.Load().Get("day-1", "111"));
    }

    [Fact]
    public void A_payments_file_holding_literal_null_is_not_saved_over()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(_data.PathOf("entry-payments.json"), "null");

        var store = EntryPaymentStore.Load();
        Assert.True(store.LoadedUnreadable);

        store.Set("day-1", "111", Cash(15));

        Assert.Equal("null", File.ReadAllText(_data.PathOf("entry-payments.json")));
    }

    [Fact]
    public void No_payments_file_is_a_new_install_and_saves()
    {
        var store = EntryPaymentStore.Load();
        Assert.False(store.LoadedUnreadable);

        store.Set("day-1", "111", Cash(15));

        Assert.Equal(15, EntryPaymentStore.Load().Get("day-1", "111")?.Amount);
    }

    [Fact]
    public void Two_keys_that_differ_only_by_case_do_not_lose_the_file()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(
            _data.PathOf("entry-payments.json"),
            "{\"day-1\":{\"abc\":{\"How\":1,\"Amount\":15},\"ABC\":{\"How\":2,\"Amount\":25},\"222\":{\"How\":1,\"Amount\":10}}}");

        var store = EntryPaymentStore.Load();

        Assert.False(store.LoadedUnreadable);
        Assert.Equal(25, store.Get("day-1", "abc")?.Amount);
        Assert.Equal(10, store.Get("day-1", "222")?.Amount);
    }

    [Fact]
    public void A_pre_entries_file_held_open_at_startup_is_not_saved_over()
    {
        var night = NextNight.KeyOf(DateTime.Today.AddDays(3));
        PreEntryStore.Load().Set(night, "111", new[] { "Stock" }, DateTime.UtcNow);
        var before = File.ReadAllText(_data.PathOf("pre-entries.json"));

        PreEntryStore store;

        using (new FileStream(_data.PathOf("pre-entries.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            store = PreEntryStore.Load();
        }

        Assert.True(store.LoadedUnreadable);

        store.Set(night, "222", new[] { "Stock" }, DateTime.UtcNow);

        Assert.Equal(before, File.ReadAllText(_data.PathOf("pre-entries.json")));
        Assert.Single(PreEntryStore.Load().For(night));
    }

    [Fact]
    public void No_pre_entries_file_is_a_new_install_and_saves()
    {
        var night = NextNight.KeyOf(DateTime.Today.AddDays(3));
        var store = PreEntryStore.Load();
        Assert.False(store.LoadedUnreadable);

        store.Set(night, "111", new[] { "Stock" }, DateTime.UtcNow);

        Assert.Single(PreEntryStore.Load().For(night));
    }
}
