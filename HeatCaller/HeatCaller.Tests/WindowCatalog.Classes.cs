namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// A class's settings (RaceClassEditForm): adding a new class, every
    /// setting at its default; a class with every option set - the longest
    /// name, notes over more lines than the box shows, best-consecutive
    /// scoring with its lap count in use, every number at its largest, jump
    /// starts detected with a penalty and the lap taken, a colour picked by
    /// hand, a checklist and both formats; and one with jump starts off,
    /// which greys what depends on them. The checklist its Tech
    /// Checklist... opens (RaceClassEditForm.TechChecklistForm), with more
    /// checks than it shows and the longest name in its title, and with
    /// none yet; and the line that checklist's Add... and Rename... ask for
    /// (TextPromptForm), empty and holding a long check. And
    /// the Race Classes dialog (RaceClassManagerForm) over a club's classes,
    /// the longest name among them, and over none.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> ClassWindows()
    {
        static string LongClassName() => "Kyosho Mini-Z MR-03 Stock 70T Touring Cup (Juniors)";

        // The six the checklist offers as common, then longer ones a club
        // writes itself - the longest wider than the list.
        static List<string> Checks(int count) => new List<string>
            {
                "Body secure and undamaged",
                "Motor legal for class",
                "Tyres legal for class",
                "Ride height",
                "Transponder fitted and working",
                "No loose parts",
                "Battery strapped down and its charge lead unplugged before the car goes on the grid",
                "Motor seal intact - 70T stock, handed out at tech",
                "Wheelbase (MM / LM) as declared at sign-in",
                "Body shell from the approved list, no wing",
                "Gear mesh and pinion as the class sheet says",
                "Tyre compound: 20 shore front, 30 shore rear",
            }
            .Take(count)
            .ToList();

        // Everything the editor shows, at the most it can hold.
        static RaceClass EveryOption() => new()
        {
            Name = LongClassName(),
            Notes = "Juniors only, with a parent or guardian in the pits.\r\n"
                  + "Stock 70T motors, sealed and handed out at tech; bodies from the approved list, no wings.\r\n"
                  + "Bring a spare set of tyres: the Tuesday layout eats them.\r\n"
                  + "Ask at the desk about a loan car if yours is in bits.",
            QualifyingMethod = QualifyingMethod.BestConsecutiveLaps,
            ConsecutiveLapCount = 20,
            CountBestRounds = 20,
            MainSize = 40,
            BumpUpCount = 10,
            DetectJumpStarts = true,
            JumpStartPenaltySeconds = 120,
            JumpStartCostsTheLap = true,
            WarnOnUninspected = true,
            ColorArgb = Color.FromArgb(255, 176, 38, 112).ToArgb(),
            TechCheckItems = Checks(12),
            Format = new RaceFormat { TimeLimitMinutes = 8, StartType = RaceStartType.StaggeredGrid },
            QualifierFormat = new RaceFormat { TimeLimitMinutes = 5, StartType = RaceStartType.Qualifier },
        };

        // A club's classes, set up the ways a club sets them up.
        static List<RaceClass> Club() => new()
        {
            EveryOption(),
            new RaceClass { Name = "Stock Buggy" },
            new RaceClass
            {
                Name = "Mini-Z MR-03 Modified",
                QualifyingMethod = QualifyingMethod.BestConsecutiveLaps,
                ConsecutiveLapCount = 3,
                CountBestRounds = 2,
                MainSize = 10,
                Format = new RaceFormat { LapLimit = 20, StartType = RaceStartType.StaggeredReverse },
                TechCheckItems = Checks(6),
            },
            new RaceClass { Name = "Novice", MainSize = 6, BumpUpCount = 0 },
            new RaceClass { Name = "Vintage Trans-Am", Format = new RaceFormat { TimeLimitMinutes = 6 } },
            new RaceClass { Name = "Kyosho Mini-Z AWD Rally Sport", CountBestRounds = 3, TechCheckItems = Checks(3) },
            new RaceClass { Name = "Open", MainSize = 12, BumpUpCount = 4 },
            new RaceClass { Name = "F1 Mini-Z Open", ColorArgb = Color.FromArgb(255, 20, 110, 200).ToArgb() },
            new RaceClass
            {
                Name = "1:28 Touring - Club Championship",
                QualifierFormat = new RaceFormat { TimeLimitMinutes = 4, StartType = RaceStartType.Qualifier },
            },
        };

        // The dialog over a store holding exactly these classes while it is
        // built - the Classes page reads them as it is made, and not again
        // unless a class is added, edited or deleted - and the store put
        // back as it was straight after, so a window built after this one in
        // the same data folder finds the classes it would have found (the
        // sign-up windows add three of their own).
        static RaceClassManagerForm Manager(IReadOnlyList<RaceClass> classes)
        {
            var store = RaceClassStore.Load();
            var before = store.All.ToList();

            foreach (var raceClass in before)
            {
                store.Delete(raceClass.Id);
            }

            foreach (var raceClass in classes)
            {
                store.Upsert(raceClass);
            }

            try
            {
                return new RaceClassManagerForm(store);
            }
            finally
            {
                foreach (var raceClass in classes)
                {
                    store.Delete(raceClass.Id);
                }

                foreach (var raceClass in before)
                {
                    store.Upsert(raceClass);
                }
            }
        }

        return new (string, Func<Form>)[]
        {
            ("Edit Class", () => new RaceClassEditForm(null, RaceClassStore.Load())),
            ("Edit Class (every option set)", () => new RaceClassEditForm(EveryOption(), RaceClassStore.Load())),
            ("Edit Class (jump starts off)", () => new RaceClassEditForm(
                new RaceClass { Name = "Stock Buggy", DetectJumpStarts = false, WarnOnUninspected = false },
                RaceClassStore.Load())),
            ("Tech Checklist", () => new RaceClassEditForm.TechChecklistForm(LongClassName(), Checks(12))),
            ("Tech Checklist (none yet)", () => new RaceClassEditForm.TechChecklistForm(string.Empty, new List<string>())),
            ("Add check", () => new TextPromptForm("Add check", "What should be checked?", string.Empty)),
            ("Rename check", () => new TextPromptForm("Rename check", "What should be checked?", Checks(12)[6])),
            ("Race Classes", () => Manager(Club())),
            ("Race Classes (none yet)", () => Manager(new List<RaceClass>())),
        };
    }
}
