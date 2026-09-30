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
            ("Start a Race Day", () => new NewRaceDayForm(ClassNames(8), ClassNames(3), RaceDayShape.RaceDay)),
            ("Start a Race Day (club night)", () => new NewRaceDayForm(ClassNames(8), ClassNames(3), RaceDayShape.ClubNight)),
            ("Start a Race Day (first run, many classes)", () => new NewRaceDayForm(ClassNames(16), new List<string>(), RaceDayShape.RaceDay)),
            ("Start a Race Day (no classes yet)", () => new NewRaceDayForm(new List<string>(), new List<string>(), RaceDayShape.RaceDay)),
            ("Drivers in Round", () => Roster(classSize: 18, inRound: 11)),
            ("Drivers in Round (nobody in the class)", () => Roster(classSize: 0, inRound: 0)),
            ("Add a Driver to This Round", () => new RoundRosterForm.DriverPicker(Drivers(30))),
        };
    }
}
