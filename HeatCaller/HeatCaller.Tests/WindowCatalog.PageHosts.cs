namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// The two pages MainForm shows in a window of their own (PageWindow),
    /// each with its real page over the stores in the test's data folder. The
    /// scrutineering log (TechInspectionControl, from Who's Racing): a busy
    /// club night's inspections, all today - thirty-two cars over a club's
    /// eight classes, the longest driver and class names, all three
    /// verdicts, a car and an inspector that run on, and notes over several
    /// lines - and a night with nothing recorded yet. The timetable
    /// (DayScheduleControl, from the program's footer): three classes'
    /// heats and mains with the club's own lines between them, the first
    /// rounds done, one on track and running late; and a race day with
    /// nothing on its sheet.
    ///
    /// What a page reads as it is built is put in the folder first and taken
    /// away straight after, so a window built after these finds the folder as
    /// it was. The page itself is handed stores loaded while it was there,
    /// which keep it for as long as the window is up.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> PageHostWindows()
    {
        static string PageHostJuniors() => "Kyosho Mini-Z MR-03 Stock 70T Touring Cup (Juniors)";

        // A club's classes, the longest name second.
        static List<RaceClass> PageHostClub() => new()
        {
            new RaceClass { Name = "Stock Buggy" },
            new RaceClass { Name = PageHostJuniors() },
            new RaceClass { Name = "Open Modified Scale Trucks and Crawlers Division" },
            new RaceClass { Name = "Novice" },
            new RaceClass { Name = "Mod Touring" },
            new RaceClass { Name = "GT Pro" },
            new RaceClass { Name = "Vintage Trans-Am" },
            new RaceClass { Name = "F1 Mini-Z Open" },
        };

        // The six checks the class editor offers as common.
        static List<string> PageHostChecks() => new()
        {
            "Body secure and undamaged",
            "Motor legal for class",
            "Tyres legal for class",
            "Ride height",
            "Transponder fitted and working",
            "No loose parts",
        };

        // Tonight's scrutineering, from six o'clock today (local time, so
        // every one counts as today's wherever and whenever the test runs):
        // thirty-two cars dealt round the club's classes, a few minutes
        // apart. Mostly passed; every fourth passed with a note; three failed
        // on a check. Every fifth car and every third inspector with a name
        // that runs on, and notes on many - one over several lines.
        static List<TechInspection> PageHostInspections(IReadOnlyList<RaceClass> club)
        {
            var checks = PageHostChecks();
            var inspectors = new[] { "Sam", "Dev", "Margaret Fitzgerald-Hollingsworth, Chief Scrutineer" };

            return Drivers(32)
                .Select((driver, i) =>
                {
                    var failed = i % 11 == 3;

                    return new TechInspection
                    {
                        Transponder = driver.Transponder,
                        DriverName = driver.DriverName,
                        ClassName = club[i % club.Count].Name,
                        CarModel = i % 5 == 0
                            ? "Kyosho MR-03 EVO Sports, long-wheelbase chassis and the club's spare motor"
                            : driver.CarModel,
                        Result = failed ? TechResult.Failed : i % 4 == 1 ? TechResult.PassedWithNote : TechResult.Passed,
                        ItemsPassed = failed ? checks.Take(5).ToList() : checks.ToList(),
                        ItemsFailed = failed ? new List<string> { checks[5] } : new List<string>(),
                        Inspector = inspectors[i % inspectors.Length],
                        Notes = i == 6
                            ? "Pinion one tooth over the class sheet - swapped at the bench, re-check before the A-main.\r\n"
                              + "Front bumper cracked on the left; taped for tonight, needs a new one next week.\r\n"
                              + "Transponder clip loose - zip-tied."
                            : failed
                                ? "Rear body pin missing and the shell lifts at speed - back to the pits, re-inspect before the first heat."
                                : i % 4 == 1
                                    ? "Ride height at the front on the limit; told the driver to check it between rounds."
                                    : i % 3 == 0 ? "OK" : string.Empty,
                        InspectedAtUtc = DateTime.Today.AddHours(18).AddMinutes(i * 4).ToUniversalTime(),
                    };
                })
                .ToList();
        }

        // The log as Who's Racing opens it: over the club's classes, in
        // place of whatever classes the folder has, and - filled - the
        // night's inspections added to whatever it has.
        static PageWindow PageHostScrutineeringLog(bool filled)
        {
            var classes = RaceClassStore.Load();
            var classesBefore = classes.All.ToList();
            var inspections = TechInspectionStore.Load();
            var inspectionsBefore = inspections.Snapshot();
            var club = PageHostClub();

            try
            {
                foreach (var raceClass in classesBefore)
                {
                    classes.Delete(raceClass.Id);
                }

                foreach (var raceClass in club)
                {
                    classes.Upsert(raceClass);
                }

                if (filled)
                {
                    foreach (var inspection in PageHostInspections(club))
                    {
                        inspections.Upsert(inspection);
                    }
                }

                var log = new TechInspectionControl(
                    TechInspectionStore.Load(),
                    ProfileStore.Load(),
                    RaceClassStore.Load(),
                    new UndoService());

                return PageWindow.ScrutineeringLog(log);
            }
            finally
            {
                inspections.Restore(inspectionsBefore);

                foreach (var raceClass in club)
                {
                    classes.Delete(raceClass.Id);
                }

                foreach (var raceClass in classesBefore)
                {
                    classes.Upsert(raceClass);
                }
            }
        }

        // Tonight's program: three qualifying rounds - two heats of Stock
        // Buggy, one of the Juniors, two of Mod Touring - then each class's
        // mains, the B before the A. The first eight run and recorded.
        static List<RoundDefinition> PageHostProgram()
        {
            var classes = new (string ClassName, int Heats)[]
            {
                ("Stock Buggy", 2),
                (PageHostJuniors(), 1),
                ("Mod Touring", 2),
            };

            var program = new List<RoundDefinition>();

            for (var qualifier = 1; qualifier <= 3; qualifier++)
            {
                foreach (var (className, heats) in classes)
                {
                    for (var heat = 1; heat <= heats; heat++)
                    {
                        program.Add(new RoundDefinition
                        {
                            ClassName = className,
                            RoundLabel = heats == 1 ? $"Qualifier {qualifier}" : $"Qualifier {qualifier} Heat {heat}",
                            Kind = RoundKind.Qualifier,
                        });
                    }
                }
            }

            foreach (var (className, heats) in classes)
            {
                foreach (var main in heats == 1 ? new[] { "A-Main" } : new[] { "B-Main", "A-Main" })
                {
                    program.Add(new RoundDefinition { ClassName = className, RoundLabel = main, Kind = RoundKind.Main });
                }
            }

            for (var i = 0; i < 8; i++)
            {
                program[i].LinkedSessionId = $"pagehost-session-{i + 1}";
            }

            return program;
        }

        // A time of day as the timetable's lines carry one, from minutes past midnight.
        static string PageHostSlot(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

        // That program's run sheet: the club's own lines round it - doors
        // open, the drivers' briefing, a break after the second qualifying
        // round, the presentation at the end - and a line a round, every
        // seven minutes from half past seven. The recorded rounds ticked off,
        // each started a minute either side of its slot; the next one on
        // track, started five minutes late.
        static List<ScheduleEntry> PageHostSheet(IReadOnlyList<RoundDefinition> program)
        {
            var lines = new List<ScheduleEntry>
            {
                new() { ScheduledTime = "18:00", Description = "Doors open - practice on the club layout, pits and tech open", IsComplete = true, AfterRounds = 0 },
                new() { ScheduledTime = "19:15", Description = "Drivers' briefing, and the marshal rota read out", IsComplete = true, AfterRounds = 0 },
            };

            var slot = 19 * 60 + 30;

            for (var i = 0; i < program.Count; i++)
            {
                // After the second qualifying round: five rounds a qualifier.
                if (i == 10)
                {
                    lines.Add(new ScheduleEntry
                    {
                        ScheduledTime = PageHostSlot(slot),
                        Description = "Break - track sweep, and the marshals swap over with the drivers who have run",
                        AfterRounds = i,
                    });

                    slot += 15;
                }

                var round = program[i];
                DateTime? started = null;

                if (round.IsRecorded)
                {
                    started = DateTime.Today.AddMinutes(slot + i % 3 - 1).ToUniversalTime();
                }
                else if (i == 8)
                {
                    started = DateTime.Today.AddMinutes(slot + 5).ToUniversalTime();
                }

                lines.Add(new ScheduleEntry
                {
                    ScheduledTime = PageHostSlot(slot),
                    ClassName = round.ClassName,
                    Description = round.RoundLabel,
                    RoundId = round.Id,
                    IsComplete = round.IsRecorded,
                    ActualStartUtc = started,
                });

                slot += 7;
            }

            lines.Add(new ScheduleEntry
            {
                ScheduledTime = PageHostSlot(slot + 10),
                Description = "Presentation, raffle and the winter series trophies",
                AfterRounds = program.Count,
            });

            return lines;
        }

        // The timetable as the program's footer opens it, for a race day of
        // its own - so its sheet holds exactly what is put on it here - with,
        // filled, tonight's program in the folder beside it.
        static PageWindow PageHostTimetable(bool filled)
        {
            var day = filled ? "pagehost-club-night" : "pagehost-empty-day";
            var schedule = DayScheduleStore.Load();
            var rounds = RoundDefinitionStore.Load();
            var program = filled ? PageHostProgram() : new List<RoundDefinition>();

            schedule.CurrentEventId = day;

            try
            {
                foreach (var round in program)
                {
                    rounds.Add(round);
                }

                if (filled)
                {
                    foreach (var line in PageHostSheet(program))
                    {
                        schedule.Add(line);
                    }
                }

                var sheet = DayScheduleStore.Load();
                sheet.CurrentEventId = day;

                // The announcer MainForm makes. It speaks here only when the
                // page's countdown is started, which nothing does, and its
                // queues wait for lines that never come.
                var timetable = new DayScheduleControl(
                    sheet,
                    RaceClassStore.Load(),
                    new AnnouncementService(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load()),
                    new UndoService(),
                    RoundDefinitionStore.Load(),
                    RaceEventStore.Load());

                return PageWindow.Timetable(timetable);
            }
            finally
            {
                if (filled)
                {
                    schedule.ClearAll();
                }

                foreach (var round in program)
                {
                    rounds.Delete(round.Id);
                }
            }
        }

        return new (string, Func<Form>)[]
        {
            ("Scrutineering log", () => PageHostScrutineeringLog(filled: true)),
            ("Scrutineering log (nothing recorded)", () => PageHostScrutineeringLog(filled: false)),
            ("Timetable", () => PageHostTimetable(filled: true)),
            ("Timetable (nothing on it)", () => PageHostTimetable(filled: false)),
        };
    }
}
