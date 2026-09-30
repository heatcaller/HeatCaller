namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// The race days' own windows. Deleting a race day (DeleteRaceDayForm):
    /// one night with many results and a name long enough to wrap; a whole
    /// season of nights at once, the way the Calendar's select-all asks it,
    /// with counts running to four figures - the longest ticks; one of
    /// everything, which words every line in the singular; and a day made by
    /// mistake with nothing on it, every tick greyed. A line of the run sheet
    /// (ScheduleEntryForm): a new one over a club's classes, the longest name
    /// among them; one with a time, that class and a long description; one
    /// whose time the picker cannot read and whose class is not on file, a
    /// name longer than any that is; and a new one with no classes on file.
    /// And renaming a race day (EventEditForm, titled "Edit &lt;day&gt;"): a
    /// long name with notes over more lines than the box shows, and a day
    /// with no notes.
    ///
    /// None of them changes anything as it is built: the delete window only
    /// reads the counts it is handed, and the other two write to what they
    /// were given only on Save.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> EventWindows() => new (string, Func<Form>)[]
    {
        ("Delete race day", () => new DeleteRaceDayForm(
            EventLongDayName(),
            new DeleteRaceDayForm.Counts(Rounds: 18, Sessions: 16, Records: 2, Seasons: 3, Badges: 27))),
        ("Delete race day (a season of days)", () => new DeleteRaceDayForm(
            "42 race days",
            new DeleteRaceDayForm.Counts(Rounds: 756, Sessions: 731, Records: 14, Seasons: 12, Badges: 1284))),
        ("Delete race day (one of each)", () => new DeleteRaceDayForm(
            "Practice Evening - 29 September 2026",
            new DeleteRaceDayForm.Counts(Rounds: 1, Sessions: 1, Records: 1, Seasons: 1, Badges: 1))),
        ("Delete race day (nothing on it)", () => new DeleteRaceDayForm(
            "Race Day 30 Sep",
            new DeleteRaceDayForm.Counts(Rounds: 0, Sessions: 0, Records: 0, Seasons: 0, Badges: 0))),
        ("Add Schedule Entry", () => EventScheduleWindow(null, EventClubClasses())),
        ("Edit Schedule Entry", () => EventScheduleWindow(
            new ScheduleEntry
            {
                ScheduledTime = "19:30",
                ClassName = EventClubClasses()[1].Name,
                Description = "Qualifying Round 3 - Juniors first, then the Seniors' A and B heats back to back",
            },
            EventClubClasses())),
        ("Edit Schedule Entry (no set time, class not on file)", () => EventScheduleWindow(
            new ScheduleEntry
            {
                ScheduledTime = "TBD - after the last A-Main",
                ClassName = "Kyosho Mini-Z AWD Rally Sport - Visiting Club Invitational (Seniors)",
                Description = "Presentation, raffle and the season's trophies",
            },
            EventClubClasses())),
        ("Add Schedule Entry (no classes on file)", () => EventScheduleWindow(null, new List<RaceClass>())),
        ("Edit Race Day", () => new EventEditForm(new RaceEvent
        {
            Name = EventLongDayName(),
            Notes = "Doors open 18:00, racing from 19:30. Juniors run first so they are away by nine.\r\n"
                  + "Borrowed the Riverside club's timing loop - it goes back to them on Thursday.\r\n"
                  + "The Tuesday layout, reversed for the finals.\r\n"
                  + "Kitchen closed - bring your own food.\r\n"
                  + "Two new drivers booked in for a loan car each; transponders R4 and R7 set aside.\r\n"
                  + "Presentation for the winter series after the last A-Main.",
        })),
        ("Edit Race Day (no notes)", () => new EventEditForm(new RaceEvent { Name = "Race Day 30 Sep" })),
    };

    /// <summary>A race day named the way a club running a series names one - long enough to wrap in the delete window's heading.</summary>
    private static string EventLongDayName() =>
        "Kyosho Mini-Z Winter Series Round 4 - Tuesday Club Night at Riverside Community Hall (Juniors and Seniors)";

    /// <summary>A club's classes, the longest name among them second.</summary>
    private static List<RaceClass> EventClubClasses() => new()
    {
        new RaceClass { Name = "Stock Buggy" },
        new RaceClass { Name = "Kyosho Mini-Z MR-03 Stock 70T Touring Cup (Juniors)" },
        new RaceClass { Name = "Mini-Z MR-03 Modified" },
        new RaceClass { Name = "Novice" },
        new RaceClass { Name = "Vintage Trans-Am" },
        new RaceClass { Name = "1:28 Touring - Club Championship" },
    };

    /// <summary>
    /// A line of the run sheet, new (<paramref name="entry"/> null) or
    /// edited, over a class store holding exactly <paramref name="classes"/>
    /// while the window is built - it reads them only then, into its
    /// drop-down - and put back as it was straight after, so a window built
    /// after this one in the same data folder finds the classes it would
    /// have found.
    /// </summary>
    private static ScheduleEntryForm EventScheduleWindow(ScheduleEntry? entry, IReadOnlyList<RaceClass> classes)
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
            return new ScheduleEntryForm(entry, store);
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
}
