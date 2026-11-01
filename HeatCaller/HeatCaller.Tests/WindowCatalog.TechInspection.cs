namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// A car's tech inspection (TechInspectionEditForm), over a club of forty
    /// drivers - the longest names in the drop-down - and eight classes, some
    /// with long names: a new inspection, whose class is not picked yet, so
    /// the checklist says so and the result is picked by hand; the same for
    /// one driver, as Who's Racing opens it; an existing one with every field
    /// filled - a checklist longer than the window shows, one check no longer
    /// on it, a long car and inspector and notes past the box; one with a
    /// short checklist, every check passed; one whose class has no checklist,
    /// failed by hand; and one whose class has since been deleted, every
    /// check it recorded no longer on a checklist and the deleted class the
    /// longest choice in its drop-down.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> TechInspectionWindows()
    {
        static string JuniorsClass() => "Kyosho Mini-Z MR-03 Stock 70T Touring Cup (Juniors)";

        static string TrucksClass() => "Open Modified Scale Trucks and Crawlers Division";

        // The six the class editor offers as common.
        static List<string> CommonChecks() => new()
        {
            "Body secure and undamaged",
            "Motor legal for class",
            "Tyres legal for class",
            "Ride height",
            "Transponder fitted and working",
            "No loose parts",
        };

        // The common six, then longer ones a club writes itself - some long
        // enough to wrap beside their drop-downs. More than the window shows.
        static List<string> LongChecklist() => CommonChecks()
            .Concat(new[]
            {
                "Battery strapped down and its charge lead unplugged before the car goes on the grid",
                "Motor seal intact - 70T stock, handed out at tech",
                "Wheelbase (MM / LM) as declared at sign-in",
                "Body shell from the approved list, no wing",
                "Gear mesh and pinion as the class sheet says",
                "Weight at least 180g ready to run, with the transponder fitted",
                "Front track no wider than 92mm",
                "Radio failsafe set to neutral throttle",
            })
            .ToList();

        // A club's classes: two with checklists, one with none, and five more
        // to pick from.
        static List<RaceClass> Club() => new()
        {
            new RaceClass { Name = "Stock Buggy", TechCheckItems = CommonChecks() },
            new RaceClass { Name = JuniorsClass(), TechCheckItems = LongChecklist() },
            new RaceClass { Name = TrucksClass() },
            new RaceClass { Name = "Novice" },
            new RaceClass { Name = "Mod Touring" },
            new RaceClass { Name = "GT Pro" },
            new RaceClass { Name = "Vintage Trans-Am" },
            new RaceClass { Name = "F1 Mini-Z Open" },
        };

        // The window over the test's data folder: forty drivers on file - the
        // ones not there already added; a window built earlier in the same
        // folder may have put some there with details of its own, which are
        // left as they are - and the club's classes in place of whatever
        // classes the folder has while it is built, which is when it reads
        // them. Put back straight after, so a window built after this one
        // finds the classes it would have found.
        static TechInspectionEditForm Open(TechInspection? existing, string? presetTransponder = null)
        {
            var profiles = ProfileStore.Load();
            var missing = Drivers(40).Where(driver => profiles.Get(driver.Transponder) is null).ToList();

            if (missing.Count > 0)
            {
                profiles.UpsertMany(missing);
            }

            var classes = RaceClassStore.Load();
            var before = classes.All.ToList();
            var club = Club();

            foreach (var raceClass in before)
            {
                classes.Delete(raceClass.Id);
            }

            foreach (var raceClass in club)
            {
                classes.Upsert(raceClass);
            }

            try
            {
                return new TechInspectionEditForm(existing, profiles, classes, presetTransponder);
            }
            finally
            {
                foreach (var raceClass in club)
                {
                    classes.Delete(raceClass.Id);
                }

                foreach (var raceClass in before)
                {
                    classes.Upsert(raceClass);
                }
            }
        }

        // Every field as long as a club would make it: the longest driver's
        // name, the long class, a car and an inspector that run on and notes
        // past the box - and the long checklist, eight checks passed, two
        // failed, four not looked at, and one failed check no longer on it.
        static TechInspection EveryField()
        {
            var driver = Drivers(40)[11];
            var checks = LongChecklist();

            return new TechInspection
            {
                Transponder = driver.Transponder,
                DriverName = driver.DriverName,
                ClassName = JuniorsClass(),
                CarModel = "Kyosho MR-03 EVO Sports, long-wheelbase chassis and the club's spare motor",
                Result = TechResult.Failed,
                ItemsPassed = checks.Take(8).ToList(),
                ItemsFailed = new List<string> { checks[8], checks[10], "Rear bumper fitted (last season's rule)" },
                Inspector = "Margaret Fitzgerald-Hollingsworth, Chief Scrutineer",
                Notes = "Pinion one tooth over the class sheet - swapped at the bench, re-check before the A-main.\r\n"
                    + "Front bumper cracked on the left; taped for tonight, needs a new one next week.\r\n"
                    + "Transponder clip loose - zip-tied. Driver told about the ride height at the front.\r\n"
                    + "Weighed twice: 179g, then 181g with the body pins in.",
            };
        }

        // A short checklist, every check passed - the verdict in green, and a
        // checklist the window shows all of.
        static TechInspection AllPassed()
        {
            var driver = Drivers(40)[2];

            return new TechInspection
            {
                Transponder = driver.Transponder,
                DriverName = driver.DriverName,
                ClassName = "Stock Buggy",
                CarModel = driver.CarModel,
                Result = TechResult.Passed,
                ItemsPassed = CommonChecks(),
                Inspector = "Sam",
            };
        }

        // A class with no checklist, so the result picked by hand - FAILED -
        // and the checklist saying the class has none, long enough to wrap.
        static TechInspection FailedByHand()
        {
            var driver = Drivers(40)[7];

            return new TechInspection
            {
                Transponder = driver.Transponder,
                DriverName = driver.DriverName,
                ClassName = TrucksClass(),
                CarModel = "Axial SCX24 with the long-travel links",
                Result = TechResult.Failed,
                Inspector = "Sam",
                Notes = "Winch line frayed - swap it before the first heat.",
            };
        }

        // Recorded under a class since deleted: it is kept as the longest
        // choice in the drop-down, and every check it recorded is shown as
        // no longer on the checklist.
        static TechInspection ClassDeleted()
        {
            var driver = Drivers(40)[19];
            var checks = CommonChecks();

            return new TechInspection
            {
                Transponder = driver.Transponder,
                DriverName = driver.DriverName,
                ClassName = "Kyosho Mini-Z MR-03 Stock 70T Touring Cup 2025 (old rules)",
                CarModel = driver.CarModel,
                Result = TechResult.PassedWithNote,
                ItemsPassed = checks.Take(4).ToList(),
                ItemsFailed = new List<string> { checks[5] },
                Inspector = "Sam",
            };
        }

        return new (string, Func<Form>)[]
        {
            ("Tech Inspection", () => Open(null)),
            ("Tech Inspection (for one driver)", () => Open(null, Drivers(40)[11].Transponder)),
            ("Tech Inspection (an existing inspection, every field filled)", () => Open(EveryField())),
            ("Tech Inspection (a short checklist, every check passed)", () => Open(AllPassed())),
            ("Tech Inspection (a class with no checklist, failed by hand)", () => Open(FailedByHand())),
            ("Tech Inspection (its class since deleted)", () => Open(ClassDeleted())),

            // One inspection for several cars picked on Who's Racing: two, a
            // dozen with the longest names (six named, "and 6 more"), and a
            // dozen some of whom were inspected today already, which makes
            // the line about them the longest it gets.
            ("Log Inspection for Several Drivers", () => new BulkInspectionForm(
                Drivers(40).Take(2).Select(d => d.DriverName).ToList())),
            ("Log Inspection for Several Drivers (a dozen, long names)", () => new BulkInspectionForm(
                Drivers(40).OrderByDescending(d => d.DriverName.Length).Take(12).Select(d => d.DriverName).ToList())),
            ("Log Inspection for Several Drivers (some inspected today)", () => new BulkInspectionForm(
                Drivers(40).Skip(5).Take(12).Select(d => d.DriverName).ToList(), alreadyToday: 3)),
        };
    }
}
