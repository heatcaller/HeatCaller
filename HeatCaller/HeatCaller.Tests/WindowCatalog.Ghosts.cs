namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// The Ghost Cars page's two windows. A ghost added (GhostEditForm,
    /// titled Add Ghost Car) as Add Ghost... opens it: a new ghost on a
    /// fixed lap time. And edited (titled Edit Ghost Car) for each set of
    /// rows its pace sources show: a Quick Rival of a driver with one of the
    /// longest names, as Quick Rival makes it; a live ghost of a driver's own
    /// pace - the source that shows the most rows - with every field long,
    /// the largest lap time and the longest line under the adjustment; a
    /// driver's best between two dates; the leader's live pace, under the lap
    /// time's longer caption; the track record, which shows no rows of its
    /// own; and a driver's best with nobody on file to pick. Over the forty
    /// drivers (Drivers), the ones not on file already added, as the other
    /// windows' builders leave them - but for the last, which takes every
    /// driver off while it is built and puts them back straight after.
    ///
    /// And the Quick Rival picker (GhostRivalPickerForm): over the forty,
    /// and over one driver on file with no name, only a transponder.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> GhostWindows() => new (string, Func<Form>)[]
    {
        ("Add Ghost Car", () => new GhostEditForm(null, GhostDriversOnFile())),
        ("Edit Ghost Car (a Quick Rival)", () => new GhostEditForm(GhostQuickRival(), GhostDriversOnFile())),
        ("Edit Ghost Car (live, every field long)", () => new GhostEditForm(GhostLiveEveryFieldLong(), GhostDriversOnFile())),
        ("Edit Ghost Car (best between two dates)", () => new GhostEditForm(
            new GhostCar
            {
                Name = "Last winter's Alexandria",
                CarModel = GhostLongNamedDriver().CarModel,
                PaceSource = GhostPaceSource.PersonalBestInRange,
                SourceTransponder = GhostLongNamedDriver().Transponder,
                RangeFromUtc = new DateTime(2025, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                RangeToUtc = new DateTime(2026, 2, 28, 23, 59, 59, DateTimeKind.Utc),
                PacePercent = 50,
                Consistency = GhostConsistency.Steady,
            },
            GhostDriversOnFile())),
        ("Edit Ghost Car (the leader's live pace)", () => new GhostEditForm(
            new GhostCar
            {
                Name = "Pace car",
                PaceSource = GhostPaceSource.LiveLeaderPace,
                FixedLapMs = 9_875,
                PacePercent = 100,
                Consistency = GhostConsistency.Metronome,
            },
            GhostDriversOnFile())),
        ("Edit Ghost Car (the track record)", () => new GhostEditForm(
            new GhostCar
            {
                Name = "The house record",
                CarModel = "Kyosho MR-03 EVO Sports",
                PaceSource = GhostPaceSource.TrackRecord,
                PacePercent = 103,
                Consistency = GhostConsistency.Steady,
            },
            GhostDriversOnFile())),
        ("Edit Ghost Car (nobody on file)", () => GhostEditorWithNobodyOnFile(
            new GhostCar
            {
                Name = "Sam's ghost",
                PaceSource = GhostPaceSource.DriverPersonalBest,
                SourceTransponder = "4009999",
                PacePercent = 98,
            })),
        ("Quick Rival", () => new GhostRivalPickerForm(Drivers(40))),
        ("Quick Rival (one driver, transponder only)", () => new GhostRivalPickerForm(
            new List<DriverProfile> { new() { Transponder = "4001644" } })),
    };

    /// <summary>One of the forty drivers (Drivers) with the longest names: "Alexandria Montgomery-Fitzwilliam 12", who drives a "Kyosho MR-03 EVO Sports".</summary>
    private static DriverProfile GhostLongNamedDriver() => Drivers(40)[11];

    /// <summary>A ghost as the Ghost Cars page's Quick Rival makes one: named for the driver, their car, 2% quicker than their best lap.</summary>
    private static GhostCar GhostQuickRival()
    {
        var driver = GhostLongNamedDriver();

        return new GhostCar
        {
            Name = $"{driver.DriverName}'s ghost",
            CarModel = driver.CarModel,
            PaceSource = GhostPaceSource.YourPersonalBest,
            SourceTransponder = driver.Transponder,
            PacePercent = 98,
            Consistency = GhostConsistency.Human,
        };
    }

    /// <summary>
    /// A live ghost of a driver's own pace - the driver, what it follows and
    /// the lap time until their laps come in all showing - switched off, with
    /// a long name and car, the largest lap time there is (120.000), the
    /// most it can be slowed by (the longest line under the adjustment) and
    /// the longest consistency.
    /// </summary>
    private static GhostCar GhostLiveEveryFieldLong()
    {
        var driver = GhostLongNamedDriver();

        return new GhostCar
        {
            Name = $"{driver.DriverName}'s shadow - one step ahead, lap after lap",
            CarModel = "Kyosho Mini-Z MR-03 EVO Sports, Porsche 911 GT3 RS (2025) body, 8500KV",
            Enabled = false,
            PaceSource = GhostPaceSource.LiveOwnPace,
            SourceTransponder = driver.Transponder,
            LiveBasis = GhostLiveBasis.RecentAverage,
            FixedLapMs = 120_000,
            PacePercent = 200,
            Consistency = GhostConsistency.Erratic,
        };
    }

    /// <summary>
    /// The drivers in the test's data folder, with the forty (Drivers) added
    /// where not there already - a window built earlier in the same folder
    /// may have put some there with details of its own, which are left as
    /// they are.
    /// </summary>
    private static ProfileStore GhostDriversOnFile()
    {
        var profiles = ProfileStore.Load();
        var missing = Drivers(40).Where(driver => profiles.Get(driver.Transponder) is null).ToList();

        if (missing.Count > 0)
        {
            profiles.UpsertMany(missing);
        }

        return profiles;
    }

    /// <summary>
    /// The ghost editor with nobody on file to base a pace on: every driver
    /// taken off while the window is built - it reads them only then - and
    /// put back straight after, so the windows built after it find the
    /// folder as it was. Taken off and put back the way Rentals' Check Out
    /// builder does it: the store's own undo of an add and of a delete,
    /// which neither stamps a first-seen date nor reports new cars.
    /// </summary>
    private static GhostEditForm GhostEditorWithNobodyOnFile(GhostCar ghost)
    {
        var profiles = ProfileStore.Load();
        var everyone = profiles.All.ToList();

        profiles.PutBack(Array.Empty<DriverProfile>(), everyone.Select(driver => driver.Transponder).ToList());

        try
        {
            return new GhostEditForm(ghost, profiles);
        }
        finally
        {
            if (everyone.Count > 0)
            {
                profiles.RestoreDeleted(everyone);
            }
        }
    }
}
