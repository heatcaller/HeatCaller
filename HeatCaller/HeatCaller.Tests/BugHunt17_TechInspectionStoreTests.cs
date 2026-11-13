using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, the tech inspection store: the newest inspection that
/// counts decides, whichever of the race day or the "no race day" holder
/// it was filed under. Saturday's pass under the race day used to beat
/// Sunday morning's fail made while the day was closed.
/// </summary>
public sealed class BugHunt17_TechInspectionStoreTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static TechInspectionStore StoreWith(params TechInspection[] inspections)
    {
        var store = TechInspectionStore.Load();
        store.Restore(JsonSerializer.Serialize(inspections.ToList()));
        store.CurrentEventId = "day-1";
        return store;
    }

    private static TechInspection Inspection(TechResult result, double hoursAgo, string eventId) => new()
    {
        Transponder = "T1",
        ClassName = "Stock",
        Result = result,
        InspectedAtUtc = DateTime.UtcNow.AddHours(-hoursAgo),
        EventId = eventId,
    };

    [Fact]
    public void A_newer_fail_made_while_the_day_was_closed_beats_the_days_older_pass()
    {
        var saturday = Inspection(TechResult.Passed, 24, "day-1");
        var sunday = Inspection(TechResult.Failed, 1, RaceEventStore.LegacyEventId);
        var store = StoreWith(saturday, sunday);

        Assert.Equal(sunday.Id, store.Latest("T1", "Stock")!.Id);
        Assert.Null(store.Cleared("T1", "Stock"));
        Assert.False(store.IsClearedToday("T1", "Stock"));
    }

    [Fact]
    public void A_newer_pass_made_while_the_day_was_closed_beats_the_days_older_fail()
    {
        var saturday = Inspection(TechResult.Failed, 24, "day-1");
        var sunday = Inspection(TechResult.Passed, 1, string.Empty);
        var store = StoreWith(saturday, sunday);

        Assert.Equal(sunday.Id, store.Cleared("T1", "Stock")!.Id);
    }

    [Fact]
    public void The_days_own_inspection_still_wins_when_it_is_the_newer()
    {
        var morning = Inspection(TechResult.Failed, 5, RaceEventStore.LegacyEventId);
        var afterOpening = Inspection(TechResult.Passed, 1, "day-1");
        var store = StoreWith(morning, afterOpening);

        Assert.Equal(afterOpening.Id, store.Cleared("T1", "Stock")!.Id);
    }

    [Fact]
    public void The_days_old_pass_still_counts_when_nothing_newer_was_filed()
    {
        var saturday = Inspection(TechResult.Passed, 30, "day-1");

        // Past the recent window, so it no longer counts at all.
        var lastWeek = Inspection(TechResult.Failed, 20, RaceEventStore.LegacyEventId);
        var store = StoreWith(saturday, lastWeek);

        Assert.Equal(saturday.Id, store.Cleared("T1", "Stock")!.Id);
    }
}
