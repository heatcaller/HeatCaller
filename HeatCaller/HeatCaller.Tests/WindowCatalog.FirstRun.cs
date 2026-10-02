namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// Welcome to HeatCaller (FirstRunWizard) opened at each of its seven
    /// steps, as a fresh install sees it - settings as a new one has them and
    /// nothing on file - and, where a step shows what the club already has,
    /// as a club that has used the app sees it: ten classes on file (one
    /// whose name is long enough to widen the drivers' class picker past the
    /// note beside it), seven track layouts with long names, an I-Lap decoder
    /// remembered at a rate that is not one of the usual ones, and a track's
    /// name remembered from last time.
    ///
    /// The wizard is handed its stores, and here they are loaded from a data
    /// folder of their own for each window (FirstRunStores) rather than from
    /// the one the windows at the same size share: the windows before these
    /// have put classes and drivers in that one, so a club with nothing on
    /// file could not be shown from it, and the windows after these do not
    /// expect to find these classes and layouts there. The wizard saves
    /// nothing until a step is left, which the test never does.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> FirstRunWindows()
    {
        // The steps, in the wizard's order (FirstRunWizard.StepNames).
        const int welcome = 0, look = 1, timing = 2, track = 3, classes = 4, drivers = 5, allSet = 6;

        return new (string, Func<Form>)[]
        {
            ("Welcome to HeatCaller (welcome)", () => FirstRunWizardAt(welcome, clubSetUp: false)),
            ("Welcome to HeatCaller (look)", () => FirstRunWizardAt(look, clubSetUp: false)),
            ("Welcome to HeatCaller (look and sound)", () => FirstRunWizardAt(look, clubSetUp: false, sound: true)),
            ("Welcome to HeatCaller (timing, EasyLap)", () => FirstRunWizardAt(timing, clubSetUp: false)),
            ("Welcome to HeatCaller (timing, I-Lap)", () => FirstRunWizardAt(timing, clubSetUp: true)),
            ("Welcome to HeatCaller (track, none yet)", () => FirstRunWizardAt(track, clubSetUp: false)),
            ("Welcome to HeatCaller (track, seven set up)", () => FirstRunWizardAt(track, clubSetUp: true)),
            ("Welcome to HeatCaller (classes, none yet)", () => FirstRunWizardAt(classes, clubSetUp: false)),
            ("Welcome to HeatCaller (classes, ten set up)", () => FirstRunWizardAt(classes, clubSetUp: true)),
            ("Welcome to HeatCaller (drivers, no classes to pick)", () => FirstRunWizardAt(drivers, clubSetUp: false)),
            ("Welcome to HeatCaller (drivers, ten classes to pick)", () => FirstRunWizardAt(drivers, clubSetUp: true)),
            ("Welcome to HeatCaller (all set, nothing on file)", () => FirstRunWizardAt(allSet, clubSetUp: false)),
            ("Welcome to HeatCaller (all set, club already set up)", () => FirstRunWizardAt(allSet, clubSetUp: true)),
            ("Welcome to HeatCaller (all set, with the sound)", () => FirstRunWizardAt(allSet, clubSetUp: true, sound: true)),
        };
    }

    /// <summary>
    /// The wizard at a step. Not set up: new settings, nothing on file. Set
    /// up: a dark look and the 24-hour clock chosen, an I-Lap decoder on
    /// COM12 at 230400 baud, the last track's name remembered, and the
    /// classes and layouts of FirstRunStores on file. With the sound, the
    /// look step offers it too (SoundTestHost); without, as it is for a
    /// club that said not to ask.
    /// </summary>
    private static FirstRunWizard FirstRunWizardAt(int step, bool clubSetUp, bool sound = false)
    {
        var settings = new AppSettings();

        if (clubSetUp)
        {
            settings.AppLook = "night-endurance";
            settings.Use24HourTime = true;
            settings.LastConnectionType = DecoderConnection.ILap;
            settings.ILapPort = "COM12";
            settings.ILapBaud = 230400;
            settings.LastTrackLayoutName = "Club Championship Layout 2026 - Autumn Rebuild";
        }

        var (trackLayouts, raceClasses, profiles) = FirstRunStores(clubSetUp);

        return new FirstRunWizard(settings, trackLayouts, raceClasses, profiles, step, sound ? SoundTestHost() : null);
    }

    /// <summary>
    /// The wizard's stores, loaded from a folder of their own inside the
    /// test's data folder - empty, or with the club's classes and track
    /// layouts added - and the test's own folder put back before they are
    /// handed over. What they hold stays in them; the folder, and what
    /// adding to them wrote there, is deleted.
    /// </summary>
    private static (TrackLayoutStore TrackLayouts, RaceClassStore RaceClasses, ProfileStore Profiles) FirstRunStores(bool clubSetUp)
    {
        // AppPaths.Root is the redirected folder's "Roaming" (AppPaths.RedirectForTests) -
        // never the real one: the test project redirects before anything runs.
        if (!AppPaths.IsRedirected)
        {
            throw new InvalidOperationException("The first-run windows need the test's data folder.");
        }

        var shared = Path.GetDirectoryName(AppPaths.Root)
                     ?? throw new InvalidOperationException($"The test's data folder \"{AppPaths.Root}\" has no parent.");

        var own = Path.Combine(shared, "first-run-" + Guid.NewGuid().ToString("N")[..8]);

        AppPaths.RedirectForTests(own);

        try
        {
            var trackLayouts = TrackLayoutStore.Load();
            var raceClasses = RaceClassStore.Load();
            var profiles = ProfileStore.Load();

            if (clubSetUp)
            {
                // Seven, of which the step names the first six.
                foreach (var name in new[]
                         {
                             "Club Championship Layout 2026 - Autumn Rebuild",
                             "Garage Oval",
                             "Kyosho Mini-Z Grand Prix Circuit (Full Length)",
                             "Rally Stage Gravel Mat",
                             "Shop Layout A",
                             "Tuesday Tight Technical",
                             "Winter Indoor Carpet Series - Hall B",
                         })
                {
                    trackLayouts.Upsert(new TrackLayout { Name = name });
                }

                // Ten, of which the step names the first eight.
                foreach (var name in new[]
                         {
                             "F1 Spec",
                             "GT Pro",
                             "Kyosho Mini-Z MR-03 Stock 70T Touring Cup (Juniors)",
                             "Mod Touring",
                             "Novice",
                             "Rally Open",
                             "Stock Buggy",
                             "Vintage Trans-Am",
                             "Women's Open Championship",
                             "Yokomo Drift Showcase",
                         })
                {
                    raceClasses.Upsert(new RaceClass { Name = name });
                }
            }

            return (trackLayouts, raceClasses, profiles);
        }
        finally
        {
            AppPaths.RedirectForTests(shared);

            try
            {
                Directory.Delete(own, recursive: true);
            }
            catch
            {
                // Left inside the test's folder, which is deleted with it (TempAppData).
            }
        }
    }
}
