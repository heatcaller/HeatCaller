namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 5, the stores: a settings file that is present and parses but
/// deserialises to nothing.
///
/// The literal JSON "null" - four bytes - is enough. The deserialiser hands
/// back null without throwing, so the catch that keeps a copy aside and holds
/// the store's writes never ran: six stores fell straight through to the
/// "fresh install" return with LoadedUnreadable left false, said nothing, and
/// the first change of the night wrote an empty store over the club's data.
/// RaceEventStore and CheckInStore already treated a file that parsed to the
/// wrong shape as unreadable; these six do now too.
///
/// One test per store: write "null" into its file, load, and check the store
/// holds its writes and that the first change does not replace the file.
/// </summary>
public sealed class BugHunt5StoresTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_race_history_holding_only_null_is_not_replaced_by_tonight_s_first_race()
    {
        var before = WriteNull("race-history.json");

        var store = SessionHistoryStore.Load();

        Assert.True(store.LoadedUnreadable);

        // False, so the race keeps its journal rather than being reported saved.
        Assert.False(store.Add(new SessionRecord { Label = "A-Main" }));
        Assert.Equal(before, Contents("race-history.json"));
    }

    [Fact]
    public void A_race_programme_holding_only_null_is_not_replaced_by_the_first_heat_added()
    {
        var before = WriteNull("race-rounds.json");

        var store = RoundDefinitionStore.Load();

        Assert.True(store.LoadedUnreadable);

        store.Add(new RoundDefinition { ClassName = "Stock", RoundLabel = "Q1 Heat 1" });
        Assert.Equal(before, Contents("race-rounds.json"));
    }

    [Fact]
    public void A_driver_roster_holding_only_null_is_not_replaced_by_the_next_driver_added()
    {
        var before = WriteNull("profiles.json");

        var store = ProfileStore.Load();

        Assert.True(store.LoadedUnreadable);

        store.Upsert(new DriverProfile { Transponder = "100", DriverName = "Alice" });
        Assert.Equal(before, Contents("profiles.json"));
    }

    [Fact]
    public void Track_layouts_holding_only_null_are_not_replaced_by_tonight_s_track()
    {
        var before = WriteNull("track-layouts.json");

        var store = TrackLayoutStore.Load();

        Assert.True(store.LoadedUnreadable);

        store.Upsert(new TrackLayout { Name = "Club Oval" });
        Assert.Equal(before, Contents("track-layouts.json"));
    }

    [Fact]
    public void Handicaps_holding_only_null_are_not_replaced_by_the_first_credit_set()
    {
        var before = WriteNull("handicaps.json");

        var store = HandicapStore.Load();

        Assert.True(store.LoadedUnreadable);

        store.Upsert(new DriverHandicap { Transponder = "100", LapCredit = 1 });
        Assert.Equal(before, Contents("handicaps.json"));
    }

    [Fact]
    public void A_music_library_holding_only_null_is_not_replaced_by_the_start_up_sync_or_the_mixer()
    {
        var before = WriteNull("music.json");

        var store = MusicStore.Load();

        Assert.True(store.LoadedUnreadable);

        // Load's own sync saves what it found in the music folder - which
        // used to be the bundled tracks alone, written over the club's
        // library before anyone had seen a word about it.
        Assert.Equal(before, Contents("music.json"));

        // And the Mixer's Music fader after it.
        store.Current.Volume = 0.9f;
        store.Save();

        Assert.Equal(before, Contents("music.json"));
    }

    // --- Helpers -------------------------------------------------------------------------

    /// <summary>Leaves the literal JSON null in a store's file, and returns it for comparison.</summary>
    private string WriteNull(string fileName)
    {
        File.WriteAllText(_data.PathOf(fileName), "null");
        return "null";
    }

    private string Contents(string fileName) => File.ReadAllText(_data.PathOf(fileName));
}
