namespace HeatCaller.Tests;

/// <summary>
/// What the stores do when their file is there but can't be used at startup.
///
/// The rule: a file that could not be READ - locked by a backup or sync tool
/// for a moment, the usual case - is almost certainly fine, and is never
/// written over that session. A file whose content is damaged is copied aside
/// first, and only then does the store start afresh.
/// </summary>
public sealed class StoreFailureTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- Locked at startup: never written over -------------------------------------

    [Fact]
    public void Race_history_locked_at_startup_is_not_replaced_by_tonight_s_first_race()
    {
        var yesterday = SessionHistoryStore.Load();
        Assert.True(yesterday.Add(new SessionRecord { Label = "Q1 Heat 1" }));
        Assert.True(yesterday.Add(new SessionRecord { Label = "A-Main" }));
        var before = Contents("race-history.json");

        var tonight = LoadWhileLocked("race-history.json", SessionHistoryStore.Load);

        Assert.True(tonight.LoadedUnreadable);
        Assert.False(tonight.Add(new SessionRecord { Label = "Q1 Heat 1" })); // says so, so the race keeps its journal
        Assert.Equal(before, Contents("race-history.json"));
    }

    [Fact]
    public void The_program_locked_at_startup_is_not_written_over()
    {
        RoundDefinitionStore.Load().Add(new RoundDefinition { ClassName = "Stock", RoundLabel = "Q1 Heat 1" });
        var before = Contents("race-rounds.json");

        var tonight = LoadWhileLocked("race-rounds.json", RoundDefinitionStore.Load);

        Assert.True(tonight.LoadedUnreadable);
        tonight.Add(new RoundDefinition { ClassName = "Stock", RoundLabel = "Q1 Heat 2" });
        Assert.Equal(before, Contents("race-rounds.json"));
    }

    [Fact]
    public void The_drivers_locked_at_startup_are_not_written_over()
    {
        ProfileStore.Load().Upsert(new DriverProfile { Transponder = "100", DriverName = "Alice" });
        var before = Contents("profiles.json");

        var tonight = LoadWhileLocked("profiles.json", ProfileStore.Load);

        Assert.True(tonight.LoadedUnreadable);
        tonight.Upsert(new DriverProfile { Transponder = "200", DriverName = "Bob" });
        Assert.Equal(before, Contents("profiles.json"));
    }

    [Fact]
    public void The_championships_locked_at_startup_are_not_written_over()
    {
        SeasonStore.Load().Upsert(new Season { Name = "Winter 2026" });
        var before = Contents("seasons.json");

        var tonight = LoadWhileLocked("seasons.json", SeasonStore.Load);

        Assert.True(tonight.LoadedUnreadable);
        tonight.Upsert(new Season { Name = "Summer 2027" });
        Assert.Equal(before, Contents("seasons.json"));
    }

    [Fact]
    public void The_classes_locked_at_startup_are_not_written_over()
    {
        RaceClassStore.Load().Upsert(new RaceClass { Name = "Stock" });
        var before = Contents("race-classes.json");

        var tonight = LoadWhileLocked("race-classes.json", RaceClassStore.Load);

        Assert.True(tonight.LoadedUnreadable);
        tonight.Upsert(new RaceClass { Name = "Mod" });
        Assert.Equal(before, Contents("race-classes.json"));
    }

    [Fact]
    public void Race_days_locked_at_startup_are_not_written_over()
    {
        File.WriteAllText(_data.PathOf("events.json"), "{\"Events\":[],\"ActiveEventId\":null}");
        var before = Contents("events.json");

        var tonight = LoadWhileLocked("events.json", RaceEventStore.Load);

        Assert.True(tonight.LoadedUnreadable);
        tonight.Save();
        Assert.Equal(before, Contents("events.json"));
    }

    [Fact]
    public void The_operator_is_told_nothing_will_be_saved_until_a_restart()
    {
        Assert.True(SessionHistoryStore.Load().Add(new SessionRecord { Label = "Q1 Heat 1" }));

        var messages = new List<string>();
        void Collect(string message) => messages.Add(message);

        SafeFile.WriteFailed += Collect;
        try
        {
            LoadWhileLocked("race-history.json", SessionHistoryStore.Load);
        }
        finally
        {
            SafeFile.WriteFailed -= Collect;
        }

        Assert.Contains(messages, m => m.Contains("race-history.json") && m.Contains("Nothing will be saved"));
    }

    // --- Damaged content: kept aside, then a fresh start -------------------------------

    [Fact]
    public void Damaged_race_history_is_kept_aside_and_the_store_carries_on()
    {
        File.WriteAllText(_data.PathOf("race-history.json"), "{ this is not json");

        var store = SessionHistoryStore.Load();

        Assert.False(store.LoadedUnreadable);
        Assert.Equal("{ this is not json", File.ReadAllText(Assert.Single(CopiesOf("race-history.json"))));
        Assert.True(store.Add(new SessionRecord { Label = "Q1 Heat 1" }));
    }

    public static TheoryData<string> EveryStoreFile => new()
    {
        "race-history.json",
        "race-rounds.json",
        "profiles.json",
        "seasons.json",
        "race-classes.json",
        "events.json",
        "checkin.json",
        "tech-inspections.json",
        "day-schedule.json",
        "venues.json",
        "track-layouts.json",
        "sound-effects.json",
        "handicaps.json",
        "garage.json",
    };

    [Theory]
    [MemberData(nameof(EveryStoreFile))]
    public void Every_store_keeps_a_copy_of_a_damaged_file_before_starting_afresh(string fileName)
    {
        File.WriteAllText(_data.PathOf(fileName), "{ this is not json");

        var failure = Record.Exception(() => LoadStoreFor(fileName));

        Assert.Null(failure); // a damaged file never stops the app opening
        Assert.Equal("{ this is not json", File.ReadAllText(Assert.Single(CopiesOf(fileName))));
    }

    // --- Helpers -------------------------------------------------------------------------

    private string Contents(string fileName) => File.ReadAllText(_data.PathOf(fileName));

    private IEnumerable<string> CopiesOf(string fileName) =>
        Directory.GetFiles(AppPaths.Root, fileName + ".unreadable-*");

    /// <summary>Loads a store while something else - a backup tool, say - has its file open exclusively.</summary>
    private T LoadWhileLocked<T>(string fileName, Func<T> load)
    {
        using (new FileStream(_data.PathOf(fileName), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            return load();
        }
    }

    private static void LoadStoreFor(string fileName)
    {
        switch (fileName)
        {
            case "race-history.json": SessionHistoryStore.Load(); break;
            case "race-rounds.json": RoundDefinitionStore.Load(); break;
            case "profiles.json": ProfileStore.Load(); break;
            case "seasons.json": SeasonStore.Load(); break;
            case "race-classes.json": RaceClassStore.Load(); break;
            case "events.json": RaceEventStore.Load(); break;
            case "checkin.json": CheckInStore.Load(); break;
            case "tech-inspections.json": TechInspectionStore.Load(); break;
            case "day-schedule.json": DayScheduleStore.Load(); break;
            case "venues.json": VenueStore.Load(); break;
            case "track-layouts.json": TrackLayoutStore.Load(); break;
            case "sound-effects.json": SoundEffectsStore.Load(); break;
            case "handicaps.json": HandicapStore.Load(); break;
            case "garage.json": CarStore.Load(); break;
            default: throw new ArgumentOutOfRangeException(nameof(fileName), fileName, "No store for that file.");
        }
    }
}
