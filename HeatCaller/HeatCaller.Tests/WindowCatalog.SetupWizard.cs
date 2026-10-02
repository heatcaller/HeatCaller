namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// Set Up Tonight (RaceDaySetupWizard) opened at each of its steps, the
    /// way the Ready to Race bar opens it part-way through - over a club with
    /// eight classes (one whose name and format are long enough to wrap, and
    /// formats short and long) and forty drivers, with tonight's day open or
    /// not, and each way the program step can go. And the two small windows
    /// behind its Add Class... and Add Driver... buttons (QuickClassForm,
    /// QuickDriverForm).
    ///
    /// The wizard reads the stores in the test's data folder, which the other
    /// windows at the same size share - so what is put there is added to
    /// whatever is there already, and tonight's day is opened, or closed,
    /// here rather than assumed.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> SetupWizardWindows()
    {
        // The club's classes: one following the app default, some with a
        // format of their own, and one whose name - and whose format, with a
        // qualifier format as well - runs long enough to wrap beside its
        // Race Format... button.
        static List<RaceClass> Classes() => new()
        {
            new RaceClass { Name = "Stock Buggy" },
            new RaceClass { Name = "Mod Touring", Format = new RaceFormat { TimeLimitMinutes = 5 } },
            new RaceClass
            {
                Name = "Kyosho Mini-Z MR-03 Stock 70T Touring Cup (Juniors)",
                Format = new RaceFormat
                {
                    LapLimit = 25,
                    TimeLimitMinutes = 8,
                    StartType = RaceStartType.Qualifier,
                    WarmUpSeconds = 120,
                },
                QualifierFormat = new RaceFormat { TimeLimitMinutes = 5 },
            },
            new RaceClass { Name = "GT Pro" },
            new RaceClass { Name = "F1 Spec", Format = new RaceFormat { LapLimit = 20, StartType = RaceStartType.StaggeredGrid } },
            new RaceClass { Name = "Rally Open" },
            new RaceClass { Name = "Novice" },
            new RaceClass { Name = "Vintage Trans-Am" },
        };

        // The wizard at a step, over that club: forty drivers dealt round
        // the classes, a venue and two layouts - and, with a day open, the
        // classes in racing ticked (every one of them when null) and every
        // third driver checked in. The program step reads plan, or a
        // program ready to build.
        static RaceDaySetupWizard Wizard(int step, bool dayOpen, IReadOnlyList<string>? racing = null, ProgramAnswer? plan = null, bool sound = false)
        {
            const string venue = "Riverside Community Hall - Main Hall";
            const string track = "Club Championship Layout 2026";

            var settings = AppSettings.Load();
            var raceClasses = RaceClassStore.Load();
            var profiles = ProfileStore.Load();
            var trackLayouts = TrackLayoutStore.Load();
            var venues = VenueStore.Load();
            var events = RaceEventStore.Load();
            var checkIns = CheckInStore.Load();

            var classes = Classes();

            foreach (var raceClass in classes)
            {
                if (!raceClasses.All.Any(c => string.Equals(c.Name, raceClass.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    raceClasses.Upsert(raceClass);
                }
            }

            var drivers = Drivers(40);

            for (var i = 0; i < drivers.Count; i++)
            {
                drivers[i].ClassName = classes[i % classes.Count].Name;
            }

            profiles.UpsertMany(drivers);

            if (!venues.All.Any(v => v.Name == venue))
            {
                venues.Upsert(new VenueProfile { Name = venue });
            }

            foreach (var name in new[] { track, "Tuesday Tight Technical" })
            {
                if (!trackLayouts.All.Any(t => t.Name == name))
                {
                    trackLayouts.Upsert(new TrackLayout { Name = name });
                }
            }

            if (dayOpen)
            {
                var day = new RaceEvent
                {
                    Name = "Tuesday Club Night - 29 September 2026",
                    VenueName = venue,
                    TrackLayoutName = track,
                    ClassNames = (racing ?? classes.Select(c => c.Name).ToList()).ToList(),
                    QualifyingRounds = 3,
                    DriversPerHeat = 8,
                    CheckedInDriversOnly = true,
                    FirstRaceLocalTime = new TimeSpan(19, 30, 0),
                };

                events.Upsert(day);
                events.SetActive(day.Id);

                checkIns.CurrentEventId = day.Id;
                checkIns.CheckInAll(drivers.Where((_, i) => i % 3 == 0).Select(driver => driver.Transponder));
            }
            else
            {
                events.SetActive(null);
            }

            var answer = plan ?? new ProgramAnswer(true, "Ready to build",
                "24 heats for 8 classes - 40 drivers over 3 rounds, 8 to a heat. "
                + "About 2 hours 20 minutes of racing, finishing around 21:50.");

            // Nothing is started, changed or built: the window is only measured.
            return new RaceDaySetupWizard(
                settings, raceClasses, profiles, trackLayouts, venues, events, checkIns,
                new RaceDaySetupHost(
                    StartDay: (_, _) => false,
                    ChangeDay: _ => { },
                    SetClassesRacing: _ => { },
                    PlanBuild: () => answer,
                    Build: () => answer,
                    Sound: sound ? SoundTestHost() : null),
                step);
        }

        // Nothing checked in with the day open and no classes ticked: The day
        // DONE, Classes and Drivers NEEDED.
        var nothingToBuild = new ProgramAnswer(false, RaceProgramControl.NothingToBuildTitle,
            "None of tonight's classes has a driver checked in yet, and races are built from checked-in drivers only.");

        // No day open at all: all three cards NEEDED, each with its button.
        var dayClosed = new ProgramAnswer(false, RaceProgramControl.ClosedDayTitle,
            "Tonight's day has been closed. Open a new one to build a program for it.");

        // Refused for a reason no step fixes: the heading and the reason, no cards.
        var heatOnTrack = new ProgramAnswer(false, "A heat is on track",
            "Stock Buggy Qualifier 2 Heat 3 is running. Build the program once it has finished - "
            + "building it now would renumber the heats around the one on track.");

        return new (string, Func<Form>)[]
        {
            ("Set Up Tonight", () => Wizard(0, dayOpen: false)),
            ("Set Up Tonight (the day, none open yet)", () => Wizard(RaceDaySetupWizard.StepDay, dayOpen: false)),
            ("Set Up Tonight (the day, open)", () => Wizard(RaceDaySetupWizard.StepDay, dayOpen: true)),
            ("Set Up Tonight (the day, with the sound)", () => Wizard(RaceDaySetupWizard.StepDay, dayOpen: true, sound: true)),
            ("Set Up Tonight (classes, eight racing)", () => Wizard(RaceDaySetupWizard.StepClasses, dayOpen: true)),
            ("Set Up Tonight (classes, none ticked)", () => Wizard(RaceDaySetupWizard.StepClasses, dayOpen: true, racing: new[] { "Retired Class" })),
            ("Set Up Tonight (drivers, forty on file)", () => Wizard(RaceDaySetupWizard.StepDrivers, dayOpen: true)),
            ("Set Up Tonight (drivers, no day open)", () => Wizard(RaceDaySetupWizard.StepDrivers, dayOpen: false)),
            ("Set Up Tonight (program, ready to build)", () => Wizard(RaceDaySetupWizard.StepProgramme, dayOpen: true)),
            ("Set Up Tonight (program, steps still needed)",
                () => Wizard(RaceDaySetupWizard.StepProgramme, dayOpen: true, racing: Array.Empty<string>(), plan: nothingToBuild)),
            ("Set Up Tonight (program, no day open)", () => Wizard(RaceDaySetupWizard.StepProgramme, dayOpen: false, plan: dayClosed)),
            ("Set Up Tonight (program, a heat on track)", () => Wizard(RaceDaySetupWizard.StepProgramme, dayOpen: true, plan: heatOnTrack)),
            ("Set Up Tonight (all set)", () => Wizard(RaceDaySetupWizard.StepProgramme + 1, dayOpen: true)),
            ("Add a class", () => new QuickClassForm(_ => false)),
            ("Add a driver", () => new QuickDriverForm(Classes().Select(c => c.Name).ToList(), "Stock Buggy", _ => null)),
        };
    }

    /// <summary>
    /// The sound as the setup wizards offer it (SoundOffer), over the
    /// built-in soundscapes, with the longest of them applied last - so
    /// "Keep what's set now (...)" is as wide as it gets with them. Choosing
    /// changes nothing: the windows are only measured.
    /// </summary>
    private static SoundOfferHost SoundTestHost()
    {
        var soundscapes = Theme.CreateBuiltInDefaults();
        var longest = soundscapes.MaxBy(theme => theme.Name.Length)!.Name;

        return new SoundOfferHost(
            Soundscapes: () => soundscapes,
            SetNow: () => longest,
            ApplySoundscape: _ => { },
            ApplyLapTimesOnly: () => { });
    }
}
