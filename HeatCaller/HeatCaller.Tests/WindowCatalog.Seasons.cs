namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// The Handicaps page's windows and the Championship page's. One driver's
    /// handicap (HandicapEditForm, titled "Handicap - &lt;driver&gt;"): a
    /// locked time credit for the longest name, with notes over more lines
    /// than the box shows; a lap credit; and a class with handicaps off -
    /// the longest note on what the mode means - holding the largest credits
    /// the boxes take, for a driver with no profile, named by transponder.
    /// The suggestions from recent pace (HandicapSuggestForm, titled
    /// "Suggest Handicaps - &lt;class&gt;"): a class of twenty-four over eight
    /// club nights, some locked, some with a credit already and a few never
    /// timed; and the same class with nothing recorded, which suggests
    /// nothing. A championship's scoring rules (SeasonRulesForm, titled
    /// "Scoring rules - &lt;championship&gt;"): a season of thirty race days,
    /// the longest names among them, some ticked and some naming the
    /// championship themselves, over a club's classes with the longest
    /// chosen; a championship not named yet, picked by hand, with no race
    /// days to list and every round counted - the longer words beside the
    /// box; and one whose class is no longer on file - the longest choice
    /// the drop-down can have. And the name Championship's New... and
    /// Rename... ask for (TextPromptForm), empty and holding a long name.
    ///
    /// Only the handicaps and the scoring rules touch the data folder, and
    /// only while their window is built - see SeasonSuggestWindow and
    /// SeasonRulesWindow.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> SeasonWindows() => new (string, Func<Form>)[]
    {
        ("Handicap", () => new HandicapEditForm(
            new DriverHandicap
            {
                Transponder = Drivers(12)[11].Transponder,
                TimeCreditSeconds = 12.5,
                Locked = true,
                Notes = "Agreed at the AGM after two seasons of finishing a lap down.\r\n"
                      + "Review at the end of the winter series - check his pace against the Tuesday layout first.\r\n"
                      + "Keep locked until then.\r\n"
                      + "Lap credit tried in 2025 and dropped: the board was hard to follow.",
            },
            Drivers(12)[11].DriverName,
            HandicapMode.TimeCredit)),
        ("Handicap (lap credit)", () => new HandicapEditForm(
            new DriverHandicap { Transponder = Drivers(3)[2].Transponder, LapCredit = 2 },
            Drivers(3)[2].DriverName,
            HandicapMode.LapCredit)),
        ("Handicap (handicaps off, largest credits)", () => new HandicapEditForm(
            new DriverHandicap { Transponder = "7340112", TimeCreditSeconds = 600, LapCredit = 20 },
            "7340112",
            HandicapMode.None)),
        ("Suggest Handicaps", () => SeasonSuggestWindow(drivers: 24, withHistory: true)),
        ("Suggest Handicaps (not enough history)", () => SeasonSuggestWindow(drivers: 24, withHistory: false)),
        ("Scoring rules", () => SeasonRulesFull()),
        ("Scoring rules (hand-picked, nothing to list)", () => SeasonRulesWindow(
            new Season { AutoInclude = false, CountBestRounds = 0 },
            new List<RaceClass>(),
            days: null)),
        ("Scoring rules (class no longer on file)", () => SeasonRulesMissingClass()),
        ("New Season", () => new TextPromptForm("New Season", "Season name:", string.Empty)),
        ("Rename Season", () => new TextPromptForm("Rename Season", "Season name:", SeasonLongName())),
    };

    /// <summary>A championship named the way a club names one - long enough to wrap anywhere it could.</summary>
    private static string SeasonLongName() => "2026 Club Championship - Kyosho Mini-Z MR-03 Stock 70T Touring Cup (Juniors)";

    /// <summary>The longest class name a club has on file.</summary>
    private static string SeasonLongClass() => "Kyosho Mini-Z MR-03 Stock 70T Touring Cup (Juniors)";

    /// <summary>A club's classes, the longest name among them second.</summary>
    private static List<RaceClass> SeasonClubClasses() => new()
    {
        new RaceClass { Name = "Stock Buggy" },
        new RaceClass { Name = SeasonLongClass() },
        new RaceClass { Name = "Mini-Z MR-03 Modified" },
        new RaceClass { Name = "Novice" },
        new RaceClass { Name = "Vintage Trans-Am" },
        new RaceClass { Name = "1:28 Touring - Club Championship" },
    };

    /// <summary>
    /// Suggest Handicaps as the Handicaps page opens it
    /// (HandicapControl.SuggestFromHistory), for a class of
    /// <paramref name="drivers"/> - the longest names Drivers makes among
    /// them. With history: eight club nights of results, every ninth driver
    /// never timed, and in the data folder's handicaps every fifth driver
    /// locked (their row greyed and named "(locked)") and every third with a
    /// credit already. The window reads the handicaps as it is built, and
    /// again only when a number is changed, so they are put back as they
    /// were straight after (HandicapStore.PutBack). The history is a view
    /// over made-up sessions, which is never saved. Without history, nothing
    /// is recorded and the folder is not touched.
    /// </summary>
    private static HandicapSuggestForm SeasonSuggestWindow(int drivers, bool withHistory)
    {
        var roster = Drivers(drivers);

        if (!withHistory)
        {
            return new HandicapSuggestForm(
                HandicapStore.Load(),
                roster,
                SessionHistoryStore.FromSessions(new List<SessionRecord>()),
                SeasonLongClass());
        }

        var store = HandicapStore.Load();
        var before = store.CopiesFor(roster.Select(driver => driver.Transponder));

        try
        {
            for (var i = 0; i < roster.Count; i++)
            {
                if (i % 5 == 4)
                {
                    store.Upsert(new DriverHandicap
                    {
                        Transponder = roster[i].Transponder,
                        TimeCreditSeconds = 6.5,
                        Locked = true,
                        Notes = "Agreed with the driver",
                    });
                }
                else if (i % 3 == 2)
                {
                    store.Upsert(new DriverHandicap
                    {
                        Transponder = roster[i].Transponder,
                        TimeCreditSeconds = 3 + i * 0.4,
                    });
                }
            }

            return new HandicapSuggestForm(
                store,
                roster,
                SessionHistoryStore.FromSessions(SeasonClubNights(roster)),
                SeasonLongClass());
        }
        finally
        {
            store.PutBack(before);
        }
    }

    /// <summary>
    /// Eight Wednesday club nights of A-Main results for
    /// <paramref name="roster"/>: each driver's best lap a little slower down
    /// the list and wandering a little from night to night, and every ninth
    /// driver never timed at all.
    /// </summary>
    private static List<SessionRecord> SeasonClubNights(IReadOnlyList<DriverProfile> roster)
    {
        var sessions = new List<SessionRecord>();
        var firstNight = new DateTime(2026, 8, 5, 19, 0, 0, DateTimeKind.Utc);

        for (var night = 0; night < 8; night++)
        {
            var started = firstNight.AddDays(night * 7);
            var results = new List<SessionResultEntry>();

            for (var i = 0; i < roster.Count; i++)
            {
                if (i % 9 == 8)
                {
                    continue;
                }

                results.Add(new SessionResultEntry
                {
                    Position = results.Count + 1,
                    Transponder = roster[i].Transponder,
                    DriverName = roster[i].DriverName,
                    CarModel = roster[i].CarModel,
                    Laps = 30 - i / 4,
                    FastLapMs = 11_800 + i * 95 + (night * 37 + i * 13) % 160,
                    ElapsedMs = 300_000 + i * 1_234,
                });
            }

            sessions.Add(new SessionRecord
            {
                SessionType = RaceSessionType.Race,
                Label = $"Club Night {night + 1} - A-Main",
                StartedAtUtc = started,
                EndedAtUtc = started.AddMinutes(8),
                Results = results,
            });
        }

        return sessions;
    }

    /// <summary>
    /// A season's scoring rules with everything in use: added automatically,
    /// the longest class chosen, mains only, the best eight rounds counted,
    /// and a season of thirty race days - every third ticked, every fourth
    /// naming the championship itself.
    /// </summary>
    private static SeasonRulesForm SeasonRulesFull()
    {
        var season = new Season
        {
            Name = SeasonLongName(),
            AutoInclude = true,
            ClassName = SeasonLongClass(),
            MainsOnly = true,
            CountBestRounds = 8,
        };

        var days = SeasonRaceDays(season.Id, count: 30);

        season.IncludedEventIds = days
            .Where((_, i) => i % 3 == 0)
            .Select(day => day.Id)
            .ToList();

        return SeasonRulesWindow(season, SeasonClubClasses(), days);
    }

    /// <summary>
    /// A season's scoring rules naming a class no longer on file - the
    /// drop-down then offers "(missing class) &lt;name&gt;", longer than any
    /// class there is - over a few race days, one of them ticked along with
    /// one deleted since, and the best round counted.
    /// </summary>
    private static SeasonRulesForm SeasonRulesMissingClass()
    {
        var season = new Season
        {
            Name = "Winter Series",
            AutoInclude = true,
            ClassName = "Kyosho Mini-Z AWD Rally Sport - Visiting Club Invitational (Seniors)",
            MainsOnly = false,
            CountBestRounds = 1,
        };

        var days = SeasonRaceDays(season.Id, count: 4);

        season.IncludedEventIds = new List<string> { days[0].Id, "race-day-deleted-since" };

        return SeasonRulesWindow(season, SeasonClubClasses(), days);
    }

    /// <summary>
    /// <paramref name="count"/> weekly club nights from March 2026, each
    /// finished four hours after it started; every sixth a series round
    /// with a name long enough to run past the list's edge, and every
    /// fourth naming <paramref name="championshipId"/> from its own Setup
    /// page.
    /// </summary>
    private static List<RaceEvent> SeasonRaceDays(string championshipId, int count)
    {
        var firstNight = new DateTime(2026, 3, 4, 18, 0, 0, DateTimeKind.Utc);

        return Enumerable.Range(0, count)
            .Select(i =>
            {
                var started = firstNight.AddDays(i * 7);

                return new RaceEvent
                {
                    Name = i % 6 == 3
                        ? $"Kyosho Mini-Z Winter Series Round {i / 6 + 1} - Tuesday Club Night at Riverside Community Hall (Juniors and Seniors)"
                        : $"Club Night {i + 1}",
                    StartedUtc = started,
                    FinishedUtc = started.AddHours(4),
                    ChampionshipId = i % 4 == 1 ? championshipId : string.Empty,
                };
            })
            .ToList();
    }

    /// <summary>
    /// The scoring rules as the Championship page opens them
    /// (SeasonsControl.EditRules), with <paramref name="classes"/> added to
    /// the data folder's classes and <paramref name="days"/> to its race
    /// days while the window is built - it reads both only then, into its
    /// drop-down and its list - and taken out again straight after, so the
    /// windows built after it find the folder as it was. Null days is no
    /// race day store at all, as the page can be given, and an empty list.
    /// </summary>
    private static SeasonRulesForm SeasonRulesWindow(Season season, IReadOnlyList<RaceClass> classes, IReadOnlyList<RaceEvent>? days)
    {
        var classStore = RaceClassStore.Load();
        var events = days is null ? null : RaceEventStore.Load();

        try
        {
            foreach (var raceClass in classes)
            {
                classStore.Upsert(raceClass);
            }

            if (events is not null && days is not null)
            {
                foreach (var day in days)
                {
                    events.Upsert(day);
                }
            }

            return new SeasonRulesForm(season, classStore, events);
        }
        finally
        {
            foreach (var raceClass in classes)
            {
                classStore.Delete(raceClass.Id);
            }

            if (events is not null && days is not null)
            {
                foreach (var day in days)
                {
                    events.Delete(day.Id);
                }
            }
        }
    }
}
