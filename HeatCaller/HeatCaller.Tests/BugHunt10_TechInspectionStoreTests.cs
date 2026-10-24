using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10, the tech inspection store: editing an old inspection that
/// has no race day (recorded before race days existed) must not file it
/// under the race day open now, or a months-old pass clears the car tonight.
/// </summary>
public sealed class BugHunt10_TechInspectionStoreTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static TechInspectionStore StoreWithOldBlankPass(out TechInspection old)
    {
        old = new TechInspection
        {
            Transponder = "T1",
            DriverName = "Zed",
            ClassName = "Stock",
            Result = TechResult.Passed,
            InspectedAtUtc = DateTime.UtcNow.AddDays(-90),
            EventId = string.Empty,
        };

        var store = TechInspectionStore.Load();
        store.Restore(JsonSerializer.Serialize(new List<TechInspection> { old }));
        store.CurrentEventId = "day-1";
        return store;
    }

    [Fact]
    public void Correcting_a_note_on_an_old_pass_with_no_race_day_does_not_clear_the_car_tonight()
    {
        var store = StoreWithOldBlankPass(out var old);
        Assert.Null(store.Cleared("T1", "Stock"));

        var edited = store.CopyOf(old.Id)!;
        edited.Notes = "typo fixed";
        store.Upsert(edited);

        Assert.Equal(string.Empty, store.Get(old.Id)!.EventId);
        Assert.Null(store.Cleared("T1", "Stock"));
        Assert.Empty(store.CountingNow());
    }

    [Fact]
    public void A_reinspection_of_an_old_pass_is_filed_under_tonight()
    {
        var store = StoreWithOldBlankPass(out var old);

        var edited = store.CopyOf(old.Id)!;
        edited.InspectedAtUtc = DateTime.UtcNow;
        edited.EventId = string.Empty;
        store.Upsert(edited);

        Assert.Equal("day-1", store.Get(old.Id)!.EventId);
        Assert.NotNull(store.Cleared("T1", "Stock"));
    }

    [Fact]
    public void A_new_inspection_is_still_filed_under_tonight()
    {
        var store = TechInspectionStore.Load();
        store.CurrentEventId = "day-1";

        var fresh = new TechInspection { Transponder = "T2", ClassName = "Stock" };
        store.Upsert(fresh);

        Assert.Equal("day-1", store.Get(fresh.Id)!.EventId);
    }
}
