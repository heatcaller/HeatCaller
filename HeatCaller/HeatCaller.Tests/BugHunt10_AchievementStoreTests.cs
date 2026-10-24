namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10 (23 Oct 2026): deleting a race day must keep Record Collector,
/// because the TrackRecords list that earns it is not wound back.
/// </summary>
public sealed class BugHunt10_AchievementStoreTests : IDisposable
{
    // Declared first: everything below reads and writes where it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Deleting_a_race_keeps_Record_Collector_with_the_track_records_list()
    {
        var store = AchievementStore.Load();
        var driver = store.For("100");
        driver.TrackRecords.Add("A");
        driver.TrackRecords.Add("B");
        driver.TrackRecords.Add("C");

        Assert.True(store.Award("100", AchievementCatalogue.TrackRecordHolder, "s1"));
        Assert.True(store.Award("100", AchievementCatalogue.ThreeTrackRecords, "s1"));

        var removed = store.RemoveForSessions(new[] { "s1" });

        var only = Assert.Single(removed);
        Assert.Equal(AchievementCatalogue.TrackRecordHolder, only.Award.Id);
        Assert.True(store.For("100").Has(AchievementCatalogue.ThreeTrackRecords));
        Assert.Equal(3, store.For("100").TrackRecords.Count);

        // The next record anywhere must not announce it again as new.
        Assert.False(store.Award("100", AchievementCatalogue.ThreeTrackRecords, "s2"));
    }

    [Fact]
    public void Record_Collector_is_kept_with_the_totals()
    {
        Assert.Contains(AchievementCatalogue.ThreeTrackRecords, AchievementStore.KeptWithTheTotals);
        Assert.DoesNotContain(AchievementCatalogue.TrackRecordHolder, AchievementStore.KeptWithTheTotals);
    }
}
