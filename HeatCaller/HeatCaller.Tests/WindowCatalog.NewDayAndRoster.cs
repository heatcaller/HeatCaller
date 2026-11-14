namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// Starting a race day (NewRaceDayForm): each kind of night - whose hints
    /// differ - a first run with every class ticked and more classes than the
    /// list shows, and a club with no classes yet. The drivers in a round
    /// (RoundRosterForm) with more drivers than the list shows and a
    /// two-figure count, and with nobody in the class; and the picker its Add
    /// Driver... opens (RoundRosterForm.DriverPicker), with more saved drivers
    /// than it shows.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> NewDayAndRosterWindows()
    {
        // A club's classes, every third with a name long enough to run past
        // the list's edge.
        static List<string> ClassNames(int count) =>
            Enumerable.Range(1, count)
                .Select(i => i % 3 == 0 ? $"Kyosho Mini-Z MR-03 Stock 70T Touring Cup {i}" : $"Class {i}")
                .ToList();

        // A heat of the class with the first few drivers in it, in reverse
        // order, and one who has since left the class and has no profile -
        // listed as "(unnamed)".
        static RoundRosterForm Roster(int classSize, int inRound)
        {
            var drivers = Drivers(classSize);

            var transponders = drivers.Take(inRound).Select(driver => driver.Transponder).Reverse().ToList();

            if (inRound > 0)
            {
                transponders.Add("4999999");
            }

            var round = new RoundDefinition
            {
                ClassName = "Stock Buggy",
                RoundLabel = "Qualifier 1 Heat 2",
                AssignedTransponders = transponders,
            };

            return new RoundRosterForm(round, ProfileStore.Load(), drivers);
        }

        return new (string, Func<Form>)[]
        {
            ("Start Tonight", () => new NewRaceDayForm(ClassNames(8), ClassNames(3), RaceDayShape.RaceDay)),
            ("Start Tonight (club night)", () => new NewRaceDayForm(ClassNames(8), ClassNames(3), RaceDayShape.ClubNight)),
            ("Start Tonight (first run, many classes)", () => new NewRaceDayForm(ClassNames(16), new List<string>(), RaceDayShape.RaceDay)),
            ("Start Tonight (no classes yet)", () => new NewRaceDayForm(new List<string>(), new List<string>(), RaceDayShape.RaceDay)),

            // Every way in on offer (12 Nov 2026, setup plan step 8): like last
            // time, two templates - one with a long name - and something
            // different, with a day still open.
            ("Start Tonight (like last time and templates)", () =>
            {
                var last = new RaceEvent { Name = "Club Night 4 Nov 2026", Shape = RaceDayShape.ClubNight, VenueName = "Riverside Raceway", ClassNames = ClassNames(4) };
                var templates = new[]
                {
                    new NightTemplate { Name = "Tuesday club night", Shape = RaceDayShape.ClubNight, ClassNames = ClassNames(3), Weekdays = new List<DayOfWeek> { DayOfWeek.Tuesday } },
                    new NightTemplate { Name = "Monthly points round for the Kyosho Mini-Z championship", ClassNames = ClassNames(6) },
                };
                var options = StartTonight.Options(last, templates);

                return new NewRaceDayForm(options, 0, ClassNames(8), ClassNames(3), RaceDayShape.ClubNight, openDay: "Race Day 11 Nov 2026");
            }),
            // A template edited on Setup's Templates tab (14 Nov 2026): every
            // field set, more classes than the list shows, and a forecast line
            // long enough to wrap; and a new one, with nothing to forecast.
            ("Edit Template", () => new TemplateEditForm(
                new NightTemplate
                {
                    Name = "Monthly points round for the Kyosho Mini-Z championship",
                    ClassNames = ClassNames(5),
                    QualifyingRounds = 3,
                    DriversPerHeat = 8,
                    EntryFee = 25,
                    ExtraClassFee = 10,
                    EntriesCloseLocalTime = TimeSpan.FromHours(9.5),
                    FirstRaceLocalTime = TimeSpan.FromHours(10),
                    OutByLocalTime = TimeSpan.FromHours(17),
                    Breaks = new List<PlannedBreak> { new() { AfterRound = 2, Minutes = 30, What = "Lunch" } },
                },
                isNew: false,
                new[] { "Tuesday club night" },
                ClassNames(12),
                new[] { "Riverside Raceway" },
                new[] { "Outdoor clay - long" },
                Array.Empty<Season>(),
                _ => ("~42 drivers (from the last 6 nights like it) · 18 heats + 10 finals · finishes about 16:50, 10 min to spare", false))),
            ("Edit Template (new, nothing to forecast)", () => new TemplateEditForm(
                new NightTemplate(), isNew: true, Array.Empty<string>(), ClassNames(3),
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<Season>(), _ => null)),
            ("Drivers in Round", () => Roster(classSize: 18, inRound: 11)),
            ("Drivers in Round (nobody in the class)", () => Roster(classSize: 0, inRound: 0)),
            ("Add a Driver to This Round", () => new RoundRosterForm.DriverPicker(Drivers(30))),
        };
    }
}
