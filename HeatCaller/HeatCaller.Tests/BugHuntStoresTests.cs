namespace HeatCaller.Tests;

/// <summary>
/// Two fixes in the smaller stores.
///
/// A handicap re-ranks drivers within the groups the result editor put them
/// in - finishers, then DNFs, then DQs - and never lifts a driver who did not
/// finish above one who did.
///
/// The rentals, tech inspections, handicaps and battery packs files are never
/// written over after they could not be read at startup, the same rule the
/// race history and the drivers already follow.
/// </summary>
public sealed class BugHuntStoresTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- Handicaps and DNF / DQ ----------------------------------------------------------

    [Fact]
    public void A_lap_credit_does_not_lift_a_DNF_above_a_finisher()
    {
        var handicaps = HandicapStore.Load();
        handicaps.Upsert(new DriverHandicap { Transponder = "B", LapCredit = 2 });

        var results = new List<SessionResultEntry>
        {
            new() { Position = 1, Transponder = "A", Laps = 10, ElapsedMs = 100_000 },
            new() { Position = 2, Transponder = "B", Laps = 9, ElapsedMs = 95_000, Outcome = ResultEditor.Dnf },
        };

        var adjusted = HandicapCalculator.Apply(results, handicaps, HandicapMode.LapCredit);

        Assert.Equal("A", adjusted[0].Result.Transponder);
        Assert.Equal(1, adjusted[0].AdjustedPosition);
        Assert.Equal("B", adjusted[1].Result.Transponder);
    }

    [Fact]
    public void A_lap_credit_does_not_lift_a_DQ_above_a_finisher()
    {
        var handicaps = HandicapStore.Load();
        handicaps.Upsert(new DriverHandicap { Transponder = "B", LapCredit = 2 });

        var results = new List<SessionResultEntry>
        {
            new() { Position = 1, Transponder = "A", Laps = 1, ElapsedMs = 30_000 },
            new() { Position = 2, Transponder = "B", Laps = 0, ElapsedMs = 20_000, Outcome = ResultEditor.Dq },
        };

        var adjusted = HandicapCalculator.Apply(results, handicaps, HandicapMode.LapCredit);

        Assert.Equal(new[] { "A", "B" }, adjusted.Select(a => a.Result.Transponder));
    }

    [Fact]
    public void A_lap_credit_still_reorders_finishers()
    {
        var handicaps = HandicapStore.Load();
        handicaps.Upsert(new DriverHandicap { Transponder = "B", LapCredit = 2 });

        var results = new List<SessionResultEntry>
        {
            new() { Position = 1, Transponder = "A", Laps = 10, ElapsedMs = 100_000 },
            new() { Position = 2, Transponder = "B", Laps = 9, ElapsedMs = 95_000 },
        };

        var adjusted = HandicapCalculator.Apply(results, handicaps, HandicapMode.LapCredit);

        Assert.Equal(new[] { "B", "A" }, adjusted.Select(a => a.Result.Transponder));
    }

    [Fact]
    public void A_time_credit_does_not_lift_a_DNF_on_the_last_lap_above_a_finisher()
    {
        var handicaps = HandicapStore.Load();
        handicaps.Upsert(new DriverHandicap { Transponder = "B", TimeCreditSeconds = 5 });

        // B was quicker and was timed right to the end, so on the clock alone
        // B counts as still running and the credit would hand B the win.
        var results = new List<SessionResultEntry>
        {
            new() { Position = 1, Transponder = "A", Laps = 10, ElapsedMs = 100_000 },
            new() { Position = 2, Transponder = "B", Laps = 10, ElapsedMs = 99_000, Outcome = ResultEditor.Dnf },
        };

        var adjusted = HandicapCalculator.Apply(results, handicaps, HandicapMode.TimeCredit);

        Assert.Equal(new[] { "A", "B" }, adjusted.Select(a => a.Result.Transponder));
    }

    // --- Locked at startup: never written over -------------------------------------------

    [Fact]
    public void Rentals_locked_at_startup_are_not_written_over()
    {
        RentalTransponderStore.Load().Upsert(new RentalTransponder { Transponder = "100", AssetTag = "R1" });
        var before = Contents("rental-transponders.json");

        var tonight = LoadWhileLocked("rental-transponders.json", RentalTransponderStore.Load);

        Assert.True(tonight.LoadedUnreadable);
        tonight.Upsert(new RentalTransponder { Transponder = "200", AssetTag = "R2" });
        Assert.Equal(before, Contents("rental-transponders.json"));
    }

    [Fact]
    public void Tech_inspections_locked_at_startup_are_not_written_over()
    {
        TechInspectionStore.Load().Upsert(new TechInspection { Transponder = "100" });
        var before = Contents("tech-inspections.json");

        var tonight = LoadWhileLocked("tech-inspections.json", TechInspectionStore.Load);

        Assert.True(tonight.LoadedUnreadable);
        tonight.Upsert(new TechInspection { Transponder = "200" });
        Assert.Equal(before, Contents("tech-inspections.json"));
    }

    [Fact]
    public void Handicaps_locked_at_startup_are_not_written_over()
    {
        HandicapStore.Load().Upsert(new DriverHandicap { Transponder = "100", LapCredit = 1 });
        var before = Contents("handicaps.json");

        var tonight = LoadWhileLocked("handicaps.json", HandicapStore.Load);

        Assert.True(tonight.LoadedUnreadable);
        tonight.Upsert(new DriverHandicap { Transponder = "200", LapCredit = 2 });
        Assert.Equal(before, Contents("handicaps.json"));
    }

    [Fact]
    public void Battery_packs_locked_at_startup_are_not_written_over()
    {
        BatteryPackStore.Load().Upsert(new BatteryPack { Label = "1" });
        var before = Contents("battery-packs.json");

        var tonight = LoadWhileLocked("battery-packs.json", BatteryPackStore.Load);

        Assert.True(tonight.LoadedUnreadable);
        tonight.Upsert(new BatteryPack { Label = "2" });
        Assert.Equal(before, Contents("battery-packs.json"));
    }

    [Fact]
    public void Damaged_rentals_are_kept_aside_and_the_store_carries_on()
    {
        File.WriteAllText(_data.PathOf("rental-transponders.json"), "{ this is not json");

        var store = RentalTransponderStore.Load();

        Assert.False(store.LoadedUnreadable);
        store.Upsert(new RentalTransponder { Transponder = "100" });
        Assert.Contains("\"100\"", Contents("rental-transponders.json"));
    }

    // --- Helpers -------------------------------------------------------------------------

    private string Contents(string fileName) => File.ReadAllText(_data.PathOf(fileName));

    /// <summary>Loads a store while something else - a backup tool, say - has its file open exclusively.</summary>
    private T LoadWhileLocked<T>(string fileName, Func<T> load)
    {
        using (new FileStream(_data.PathOf(fileName), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            return load();
        }
    }
}
