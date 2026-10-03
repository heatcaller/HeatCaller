namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// A driver's profile (ProfileForm, titled Add or Edit Driver Profile) in
    /// each way it is opened: added from Driver Hub, empty, with the
    /// club's classes to pick from - one with a name long enough to widen the
    /// class drop-down; the same with only short class names, the narrowest
    /// the window comes; edited, every field filled with the longest there
    /// is, a garage of three cars and seven badges earned, so the badges line
    /// shows under the fields; and named from Race Control after a session -
    /// the transponder filled in and locked, nothing else yet, and three
    /// badges earned in the race just run.
    ///
    /// The classes and badges each one adds are taken away again when its
    /// window goes, so the windows built after it find the data folder as
    /// they would have without it.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> DriverProfileWindows()
    {
        // A club's classes: short names, and with them one long enough to be
        // the longest a class drop-down has to show.
        static string[] ClubClasses(bool withLongName) => withLongName
            ? new[] { "Stock Buggy", "Mini-Z MR-03 Modified", "Novice", "Kyosho Mini-Z MR-03 Stock 70T Touring Cup (Juniors)" }
            : new[] { "Stock Buggy", "Novice" };

        // The longest name Drivers makes, and every field filled in as long
        // as a club would fill it: three cars, the active one the longest,
        // several lines of setup and notes.
        static DriverProfile Everything()
        {
            var driver = Drivers(12)[11];
            var nl = Environment.NewLine;

            return new DriverProfile
            {
                Transponder = driver.Transponder,
                DriverName = driver.DriverName,
                AnnouncerName = "Alexandria \"The Rocket\" Montgomery-Fitzwilliam",
                CarModel = "Kyosho Mini-Z MR-03 EVO Sports Ferrari 488 GT3",
                Cars = new List<string>
                {
                    "Kyosho Mini-Z MR-03 EVO Sports Ferrari 488 GT3",
                    "Kyosho Mini-Z AWD MA-030 EVO Subaru Impreza",
                    driver.CarModel,
                },
                RaceNumber = "117",
                HomeTrack = "Riverside Mini-Z Raceway and Hobby Center, Riverside Township",
                SkillLevel = "Intermediate - moving up to Expert next season",
                Contact = "Mom (Catherine Montgomery-Fitzwilliam), (555) 010-0123",
                Email = "alexandria.montgomery-fitzwilliam.12@example.com",
                AlternateTransponder = (4_000_000 + 99 * 137).ToString(),
                ClassName = "Kyosho Mini-Z MR-03 Stock 70T Touring Cup (Juniors)",
                SetupConfiguration = "Front: 2.5 springs, 0.5mm camber shims, 20 shore tires" + nl
                    + "Rear: medium spring, 64T spur, 8T pinion, 30 shore tires" + nl
                    + "Ride height 3.2mm all round, diff at three clicks" + nl
                    + "Body: Ferrari 488 GT3 with the long wing" + nl
                    + "Battery: the club's AAA NiMH packs, charged at 0.8A",
                Notes = "Races with the juniors on Tuesdays and in the open class at the monthly meetings." + nl
                    + "Prefers a marshal post near the pit table." + nl
                    + "Collected at nine; call the contact number if the program runs late." + nl
                    + "Won the 2025 club championship in Stock; moving up next season.",
                ExcludeFromRecords = true,
                Team = "Riverside Mini-Z Club Junior Racing Squad",
                Sponsors = "Kyosho, PN Racing, Atomic, Reflex Racing, the Riverside Hobby Center, Mom and Dad",
                FirstSeenUtc = new DateTime(2025, 3, 14, 18, 30, 0, DateTimeKind.Utc),
            };
        }

        // The window as its caller opens it, on a class store holding
        // classNames and with the badges earned by the transponder it shows.
        // What it adds is taken away again when the window is disposed.
        static ProfileForm Window(DriverProfile? existing, string? fixedTransponder, string[] classNames, string[] badges)
        {
            var classes = RaceClassStore.Load();
            var addedClasses = new List<string>();

            foreach (var name in classNames)
            {
                if (!classes.All.Any(c => c.Name == name))
                {
                    var raceClass = new RaceClass { Name = name };
                    classes.Upsert(raceClass);
                    addedClasses.Add(raceClass.Id);
                }
            }

            var earner = fixedTransponder ?? existing?.Transponder;
            var achievements = AchievementStore.Load();
            var hadRecord = earner is not null
                && achievements.All.Any(d => string.Equals(d.Transponder, earner, StringComparison.OrdinalIgnoreCase));

            if (earner is not null && badges.Length > 0)
            {
                foreach (var id in badges)
                {
                    achievements.Award(earner, id);
                }

                achievements.Save();
            }

            void Tidy()
            {
                foreach (var id in addedClasses)
                {
                    classes.Delete(id);
                }

                if (earner is not null && badges.Length > 0 && !hadRecord)
                {
                    AchievementStore.Load().Delete(earner);
                }
            }

            try
            {
                var form = new ProfileForm(existing, classes, fixedTransponder, profiles: ProfileStore.Load());
                form.Disposed += (_, _) => Tidy();

                return form;
            }
            catch
            {
                Tidy();
                throw;
            }
        }

        return new (string, Func<Form>)[]
        {
            ("Driver Profile", () => Window(null, null, ClubClasses(withLongName: true), Array.Empty<string>())),
            ("Driver Profile (short class names)", () => Window(null, null, ClubClasses(withLongName: false), Array.Empty<string>())),
            ("Driver Profile (a full profile)", () =>
            {
                var driver = Everything();

                // Seven: the line names four, and "+3 more".
                return Window(driver, driver.Transponder, ClubClasses(withLongName: true), new[]
                {
                    AchievementCatalogue.FastestLap,
                    AchievementCatalogue.ComeFromBehind,
                    AchievementCatalogue.CountryMile,
                    AchievementCatalogue.SlowAndSteady,
                    AchievementCatalogue.FirstRace,
                    AchievementCatalogue.TenRaces,
                    AchievementCatalogue.FirstWin,
                });
            }),
            ("Driver Profile (fixed transponder)", () => Window(null, "8812345", ClubClasses(withLongName: true), new[]
            {
                AchievementCatalogue.FirstRace,
                AchievementCatalogue.FastestLap,
                AchievementCatalogue.Flawless,
            })),
        };
    }
}
