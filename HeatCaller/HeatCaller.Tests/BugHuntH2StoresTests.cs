namespace HeatCaller.Tests;

/// <summary>
/// Stores whose file cannot be read at startup.
///
/// Track layouts, check-in sheets, venues and personal-best resets all used
/// to start empty after a locked read and then write that empty store over
/// the club's real file at the first change. A file held open for a moment
/// at launch (a backup or sync tool) must now be left exactly as it is for
/// the rest of that session.
/// </summary>
public sealed class BugHuntH2StoresTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Track_layouts_locked_at_startup_are_not_written_over()
    {
        TrackLayoutStore.Load().Upsert(new TrackLayout { Name = "Club Oval" });
        var before = File.ReadAllText(_data.PathOf("track-layouts.json"));

        TrackLayoutStore tonight;
        using (new FileStream(_data.PathOf("track-layouts.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            tonight = TrackLayoutStore.Load();
        }

        Assert.True(tonight.LoadedUnreadable);
        Assert.Empty(tonight.All);

        // Still raised, so a layout added this session reaches the pickers.
        var changed = 0;
        tonight.Changed += () => changed++;
        tonight.Upsert(new TrackLayout { Name = "Tonight" });

        Assert.Equal(1, changed);
        Assert.Equal(before, File.ReadAllText(_data.PathOf("track-layouts.json")));
        Assert.Contains(TrackLayoutStore.Load().All, t => t.Name == "Club Oval");
    }

    [Fact]
    public void A_readable_track_layouts_file_is_not_held()
    {
        TrackLayoutStore.Load().Upsert(new TrackLayout { Name = "Club Oval" });

        var store = TrackLayoutStore.Load();

        Assert.False(store.LoadedUnreadable);
        store.Upsert(new TrackLayout { Name = "Infield" });
        Assert.Equal(2, TrackLayoutStore.Load().All.Count);
    }

    [Fact]
    public void Check_in_sheets_locked_at_startup_are_not_written_over()
    {
        CheckInStore.Load().SetCheckedIn("1001", true);
        var before = File.ReadAllText(_data.PathOf("checkin.json"));

        CheckInStore tonight;
        using (new FileStream(_data.PathOf("checkin.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            tonight = CheckInStore.Load();
        }

        Assert.True(tonight.LoadedUnreadable);

        // What the desk does when the first driver scans in.
        tonight.SetCheckedIn("2002", true);

        Assert.True(tonight.IsCheckedIn("2002"));
        Assert.Equal(before, File.ReadAllText(_data.PathOf("checkin.json")));
        Assert.True(CheckInStore.Load().IsCheckedIn("1001"));
    }

    [Fact]
    public void Venues_locked_at_startup_are_not_written_over()
    {
        VenueStore.Load().Upsert(new VenueProfile { Name = "Clubhouse" });
        var before = File.ReadAllText(_data.PathOf("venues.json"));

        VenueStore tonight;
        using (new FileStream(_data.PathOf("venues.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            tonight = VenueStore.Load();
        }

        Assert.True(tonight.LoadedUnreadable);
        tonight.Upsert(new VenueProfile { Name = "Tonight" });

        Assert.Equal(before, File.ReadAllText(_data.PathOf("venues.json")));
        Assert.Contains(VenueStore.Load().All, v => v.Name == "Clubhouse");
    }

    [Fact]
    public void Personal_best_resets_locked_at_startup_are_not_written_over()
    {
        var cutoff = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        PersonalBestResetStore.Load().SetCutoff("Alice", "Club Oval", cutoff);
        var before = File.ReadAllText(_data.PathOf("pb-resets.json"));

        PersonalBestResetStore tonight;
        using (new FileStream(_data.PathOf("pb-resets.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            tonight = PersonalBestResetStore.Load();
        }

        Assert.True(tonight.LoadedUnreadable);
        tonight.SetCutoff("Bob", "Club Oval", cutoff);

        Assert.Equal(before, File.ReadAllText(_data.PathOf("pb-resets.json")));
        Assert.Equal((DateTime?)cutoff, PersonalBestResetStore.Load().EffectiveCutoff("Alice", "Club Oval"));
    }

    [Fact]
    public void Damaged_venues_copied_aside_still_start_afresh_and_save()
    {
        File.WriteAllText(_data.PathOf("venues.json"), "{ this is not json");

        var store = VenueStore.Load();

        Assert.False(store.LoadedUnreadable);
        Assert.NotEmpty(Directory.GetFiles(AppPaths.Root, "venues.json.unreadable-*"));

        store.Upsert(new VenueProfile { Name = "Clubhouse" });
        Assert.Contains(VenueStore.Load().All, v => v.Name == "Clubhouse");
    }
}
