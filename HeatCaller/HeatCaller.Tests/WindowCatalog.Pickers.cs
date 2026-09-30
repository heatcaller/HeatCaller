namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// Three windows that pick or show one thing from a list. A badge
    /// opened from the Achievements page (AchievementDetailForm, titled with
    /// the badge's name): the longest name in the catalogue, earned by thirty
    /// drivers with long names and long details - this driver among them -
    /// then a counting badge this driver is on the way to, and one nobody has
    /// earned yet, whose hint says so. The columns of a leaderboard
    /// (KpiPickerForm, titled "Choose columns - " and the leaderboard): Race
    /// Control's defaults, every column picked, and none. And the sidebar's
    /// pages (TabVisibilityForm): every page the sidebar can hide, a few of
    /// them hidden, and the fewest - one page, the race-night grouping off.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> PickerWindows() => new (string, Func<Form>)[]
    {
        ("Badge Details", () => PickerBadgeWindow(AchievementCatalogue.Bridesmaid, holders: 30, earned: true)),
        ("Badge Details (on the way to it)", () => PickerBadgeWindow(AchievementCatalogue.TenRaces, holders: 5, earned: false)),
        ("Badge Details (nobody yet)", () => PickerBadgeWindow(AchievementCatalogue.GrandSlam, holders: 0, earned: false)),
        ("Choose columns", () => new KpiPickerForm("Race Control", KpiCatalog.DefaultRaceControl, KpiCatalog.DefaultRaceControl)),
        ("Choose columns (every column picked)", () => new KpiPickerForm(
            "Live Results page",
            KpiCatalog.All.Select(definition => definition.Key).ToList(),
            KpiCatalog.DefaultWeb)),
        ("Choose columns (none picked)", () => new KpiPickerForm("Big Screen", new List<string>(), KpiCatalog.DefaultBigScreen)),
        ("Manage Navigation", () => new TabVisibilityForm(
            PickerNavPages(),
            new[] { "Ghost Cars", "Demo Mode", "Sponsors" },
            PickerNavPages(),
            raceNightFocus: true)),
        ("Manage Navigation (one page, race-night grouping off)", () => new TabVisibilityForm(
            new[] { "Race Control" },
            Array.Empty<string>(),
            defaultOrder: null,
            raceNightFocus: false)),
    };

    /// <summary>
    /// The badge <paramref name="badgeId"/> as the Achievements page opens
    /// it, for one driver, over the drivers in the test's data folder.
    ///
    /// The first <paramref name="holders"/> drivers (Drivers - names of
    /// every length) have earned it, oldest first a few weeks apart over
    /// two years, most with a detail as long as a club would make it; six
    /// more have raced without earning it, so it reads as rare. The driver
    /// shown is the one with the longest name among the holders when
    /// <paramref name="earned"/>, and otherwise one who has not got it,
    /// seven races into their career - on the way to a counting badge, or
    /// just "Not earned yet" for one that does not count.
    ///
    /// The badges are made in a store that is never saved. The drivers'
    /// names come from the profiles in the data folder: the ones not there
    /// already are added, and taken off again when the window is disposed,
    /// so the windows built after it find the folder as it was.
    /// </summary>
    private static AchievementDetailForm PickerBadgeWindow(string badgeId, int holders, bool earned)
    {
        var badge = AchievementCatalogue.All.First(definition => definition.Id == badgeId);
        var drivers = Drivers(holders + 6);

        var profiles = ProfileStore.Load();
        var added = drivers.Where(driver => profiles.Get(driver.Transponder) is null).ToList();

        if (added.Count > 0)
        {
            profiles.UpsertMany(added);
        }

        void Tidy()
        {
            if (added.Count > 0)
            {
                profiles.PutBack(Array.Empty<DriverProfile>(), added.Select(driver => driver.Transponder).ToList());
            }
        }

        try
        {
            var store = AchievementStore.Load();
            var firstEarned = new DateTime(2024, 4, 13, 19, 30, 0, DateTimeKind.Utc);

            for (var i = 0; i < drivers.Count; i++)
            {
                var transponder = drivers[i].Transponder;
                var record = store.For(transponder);

                record.RacesFinished = Math.Max(record.RacesFinished, 20 + i);

                if (i >= holders)
                {
                    continue;
                }

                var detail = i % 3 == 0
                    ? string.Empty
                    : $"{5 + i % 4} second places, the last of them in the Winter Series final at Riverside";

                store.Award(transponder, badgeId, sessionId: null, detail: detail);

                var award = record.Unlocked.First(unlocked => string.Equals(unlocked.Id, badgeId, StringComparison.Ordinal));
                award.UnlockedUtc = firstEarned.AddDays(i * 23);
            }

            // The longest name there is - Drivers gives every fourth driver
            // the long one: among the holders when this driver has the badge,
            // among the others when not.
            var shown = (earned && holders > 0 ? drivers.Take(holders) : drivers.Skip(holders))
                .OrderByDescending(driver => driver.DriverName.Length)
                .First();

            if (!earned)
            {
                store.For(shown.Transponder).RacesFinished = 7;
            }

            var form = new AchievementDetailForm(badge, shown.Transponder, store, profiles);
            form.Disposed += (_, _) => Tidy();

            return form;
        }
        catch
        {
            Tidy();
            throw;
        }
    }

    /// <summary>Every page the sidebar can hide, in its own order - all of them but Connection (MainForm.Pages).</summary>
    private static string[] PickerNavPages() => new[]
    {
        "Race Control", "Race Day", "Championship", "Driver Profiles", "Classes", "Rentals", "Handicaps",
        "Batteries", "Track Layouts", "Race History", "Race Analytics", "Achievements", "Garage", "Practice",
        "Ghost Cars", "Demo Mode", "Venues", "Themes", "Sponsors", "Local Screens", "Club Website", "Voice",
        "Sounds", "Backup & Restore",
    };
}
