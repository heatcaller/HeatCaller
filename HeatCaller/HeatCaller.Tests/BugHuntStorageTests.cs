namespace HeatCaller.Tests;

/// <summary>
/// checkin.json in shapes that are valid JSON but not a check-in file. These
/// used to load as an empty sheet with no copy kept, so the first check-in
/// at the desk wrote over every earlier race day's attendance.
/// </summary>
public sealed class BugHuntStorageTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private IEnumerable<string> CopiesOf(string fileName) =>
        Directory.GetFiles(AppPaths.Root, fileName + ".unreadable-*");

    [Theory]
    [InlineData("{\"Events\":[{\"CheckedIn\":[\"8467BE\",\"8467C1\"]}]}")]
    [InlineData("{\"Something\":1}")]
    [InlineData("\"8467BE\"")]
    public void A_check_in_file_of_the_wrong_shape_is_kept_aside_before_anything_is_saved(string json)
    {
        File.WriteAllText(_data.PathOf("checkin.json"), json);

        var store = CheckInStore.Load();

        Assert.Equal(json, File.ReadAllText(Assert.Single(CopiesOf("checkin.json"))));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void The_current_shape_loads_every_event_without_keeping_a_copy()
    {
        File.WriteAllText(_data.PathOf("checkin.json"),
            "{\"Events\":{\"e1\":{\"CheckedIn\":[\"8467BE\"]},\"e2\":null,\"e3\":{\"CheckedIn\":[\"8467C1\",\"8467BE\"]}}}");

        var store = CheckInStore.Load();

        Assert.Empty(CopiesOf("checkin.json"));
        Assert.False(store.LoadedUnreadable);
        Assert.Equal(new[] { "8467BE" }, store.CheckedInAt("e1"));
        Assert.Equal(2, store.EventsAttended("8467be"));
    }

    [Fact]
    public void An_empty_events_object_is_a_real_state_not_damage()
    {
        File.WriteAllText(_data.PathOf("checkin.json"), "{\"Events\":{}}");

        var store = CheckInStore.Load();

        Assert.Empty(CopiesOf("checkin.json"));
        Assert.False(store.LoadedUnreadable);
    }

    [Theory]
    [InlineData("{\"CheckedIn\":[\"8467BE\"],\"WaiverSigned\":[\"8467BE\"]}")]
    [InlineData("[\"8467BE\"]")]
    public void Older_files_still_land_in_the_legacy_event(string json)
    {
        File.WriteAllText(_data.PathOf("checkin.json"), json);

        var store = CheckInStore.Load();

        Assert.Empty(CopiesOf("checkin.json"));
        Assert.Equal(new[] { "8467BE" }, store.CheckedInAt(RaceEventStore.LegacyEventId));
    }
}
