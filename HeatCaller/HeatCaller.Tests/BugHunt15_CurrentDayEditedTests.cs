namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15 (B49): Race Control's Track box only followed the day's
/// track on CurrentChanged. The Set Up Tonight wizard starts the new day
/// (CurrentChanged, still carrying last week's track) and only then writes
/// tonight's track through Upsert, which raised nothing - so freeform races
/// and practice were filed under last week's layout. Upsert of the day now
/// open raises CurrentEdited, which MainForm follows.
/// </summary>
public sealed class BugHunt15_CurrentDayEditedTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Writing_the_wizard_s_track_onto_the_new_day_is_heard_after_it_opens()
    {
        var store = RaceEventStore.Load();
        EventSpine.StartEvent(store, "Last week", trackLayoutName: "Indoor");

        // The wizard's StartDay: the new day carries the previous track.
        string? trackOnOpen = null;
        string? trackOnEdit = null;
        store.CurrentChanged += (_, _) => trackOnOpen = store.Current.TrackLayoutName;
        store.CurrentEdited += (_, _) => trackOnEdit = store.Current.TrackLayoutName;

        var tonight = EventSpine.StartEvent(store, "Tonight", trackLayoutName: "Indoor");
        Assert.Equal("Indoor", trackOnOpen);

        // The wizard's ChangeDay: edits the open day in place and upserts.
        tonight.TrackLayoutName = "Outdoor";
        store.Upsert(tonight);

        Assert.Equal("Outdoor", trackOnEdit);
    }

    [Fact]
    public void Saving_a_day_that_is_not_open_raises_nothing()
    {
        var store = RaceEventStore.Load();
        var other = EventSpine.StartEvent(store, "Other", trackLayoutName: "Indoor");
        EventSpine.StartEvent(store, "Tonight", trackLayoutName: "Indoor");

        var raised = 0;
        store.CurrentEdited += (_, _) => raised++;
        store.CurrentChanged += (_, _) => raised++;

        other.TrackLayoutName = "Outdoor";
        store.Upsert(other);

        Assert.Equal(0, raised);
    }
}
