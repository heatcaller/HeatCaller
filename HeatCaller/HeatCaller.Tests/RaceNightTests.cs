using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// A whole race night, start to finish, on the real app's own parts.
///
/// WHY. HeatCaller's rule is that nothing new ships until race nights run
/// clean, and a club of one has no race nights. This is the night instead,
/// run on every build: an empty laptop, a race day set up and driven from
/// the phone manager (RaceDayService - exactly what the /manage page and the
/// relay send), races run through RaceControlSession and filed the way Race
/// Control files them (RaceFiling), a crash in the middle of the A-main and
/// the race recovered from the journal on a restarted "laptop" whose stores
/// are read back from disk, and a result corrected from the phone at the end.
///
/// Every expected answer below is worked out by hand in the comments, from
/// the lap times in the tables - so a failure says which step of the night
/// went wrong, and what it should have said.
///
/// WHAT IT DOES NOT COVER. Laps are fed as synthetic crossings (race time
/// since the green, as ghost cars are), because a real decoder's clock is
/// checked against the wall clock on the first lap, and a test cannot spend
/// a minute per heat. So the decoder's own path - its clock, alternate
/// transponders, jump starts - is not exercised here; that is what a solo
/// rehearsal with the EasyLap is for (see Rehearsal), and what captured
/// decoder logs will test later (ROADMAP.md, "Next").
/// </summary>
public sealed class RaceNightTests : IDisposable
{
    // Declared first: everything the laptop loads comes from wherever it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- Who is racing ------------------------------------------------------------------
    //
    // Named so that alphabetical order - the order a class roster is read in,
    // and so the order heats are dealt from - is the order written here.

    private static readonly (string Transponder, string Name, string Class)[] Drivers =
    {
        ("S01", "Alice", "Stock"),
        ("S02", "Ben", "Stock"),
        ("S03", "Cara", "Stock"),
        ("S04", "Dan", "Stock"),
        ("S05", "Eve", "Stock"),
        ("S06", "Finn", "Stock"),
        ("S07", "Gus", "Stock"),  // checks in late, from the phone
        ("S08", "Hal", "Stock"),  // entered, never checks in: never in a heat
        ("M01", "Ivy", "Mod"),
        ("M02", "Jay", "Mod"),
        ("M03", "Kim", "Mod"),
    };

    // --- Lap times, in ms --------------------------------------------------------------
    //
    // Every car runs the same lap all heat, and its first crossing comes one
    // lap after the green. Five-lap races: the leader's fifth crossing puts
    // the checkered out, and everyone else takes it on their next crossing.
    // Everyone within a quarter of the leader's pace finishes on five laps,
    // with an elapsed time of 5 x their lap.

    private static readonly Dictionary<string, int> Q1 = new()
    {
        ["S01"] = 10_000, ["S02"] = 10_200, ["S03"] = 10_400, ["S04"] = 10_600,
        ["S05"] = 10_800, ["S06"] = 14_000, ["S07"] = 11_200,   // Finn has a bad heat
        ["M01"] = 12_000, ["M02"] = 12_500, ["M03"] = 13_000,
    };

    private static readonly Dictionary<string, int> Q2 = new()
    {
        ["S01"] = 10_000, ["S02"] = 10_200, ["S03"] = 10_400, ["S04"] = 10_900,
        ["S05"] = 10_800, ["S06"] = 11_000, ["S07"] = 10_100,   // Gus finds speed
        ["M01"] = 12_000, ["M02"] = 12_500, ["M03"] = 11_800,   // so does Kim
    };

    private static readonly Dictionary<string, int> BMain = new()
    {
        ["S04"] = 10_700, ["S05"] = 10_900, ["S06"] = 10_500,  // Finn wins it
    };

    private static readonly Dictionary<string, int> AMain = new()
    {
        ["S01"] = 10_000, ["S07"] = 10_100, ["S02"] = 10_200, ["S03"] = 10_400,
        ["S06"] = 10_300,  // bumped up from the B-main
    };

    private static readonly Dictionary<string, int> ModMain = new()
    {
        ["M03"] = 11_800, ["M01"] = 12_000, ["M02"] = 12_500,
    };

    private const int LapLimit = 5;

    [Fact]
    public void A_whole_race_night_runs_from_an_empty_laptop_to_a_corrected_final() => OnUiThread(TheNight);

    private void TheNight()
    {
        var laptop = new Laptop();

        try
        {
            // --- Setting up -------------------------------------------------------------

            // The desk makes the classes (the Classes page); the phone sets
            // Stock's finals: mains of four, the B-main's winner moves up.
            laptop.Classes.Upsert(new RaceClass { Name = "Stock" });
            laptop.Classes.Upsert(new RaceClass { Name = "Mod" });
            laptop.Ask("class.setFormat", new { className = "Stock", mainSize = 4, bumpUpCount = 1 });

            laptop.Ask("day.create", new { name = "Rehearsal Night" });
            laptop.Ask("day.update", new
            {
                qualifyingRounds = 2,
                driversPerHeat = 4,
                checkedInOnly = true,
                classNames = new[] { "Stock", "Mod" },
            });

            foreach (var (transponder, name, className) in Drivers)
            {
                var arrived = transponder is not ("S07" or "S08");
                laptop.Ask("driver.add", new { transponder, name, className, checkIn = arrived });
            }

            Assert.Equal("buildQualifying", laptop.NextStage());

            // Gus arrives, and checks in from the phone.
            laptop.Ask("checkin.set", new { driverId = laptop.Phone.DriverIdFor("S07"), checkedIn = true });

            // --- Qualifying ----------------------------------------------------------------

            // Stock: 7 checked in (not Hal), 4 per heat -> 2 heats a round.
            // Mod: 3 -> 1 heat. Two rounds: (2 + 1) x 2 = 6 heats.
            var question = laptop.Question("program.buildQualifying");
            Assert.Contains("Build 6 heats for 2 classes?", question);
            Assert.Contains("Stock: 2 x 2 heats  (7 drivers)", question);
            Assert.Contains("Mod: 2 x 1 heat  (3 drivers)", question);

            var built = laptop.Ask("program.buildQualifying");
            Assert.Equal("Built 6 heats across 2 classes.", built.GetProperty("message").GetString());

            // Dealt from the roster in name order, round-robin across the
            // heats; each round starts 4 places further round the roster.
            //   Stock Q1: A B C D E F G          -> H1 A C E G, H2 B D F
            //   Stock Q2: from E: E F G A B C D  -> H1 E G B D, H2 F A C
            //   Mod Q1:   I J K; Mod Q2 from 4 % 3 = 1: J K I
            AssertRoster(laptop, "Stock", "Q1 Heat 1", "S01", "S03", "S05", "S07");
            AssertRoster(laptop, "Stock", "Q1 Heat 2", "S02", "S04", "S06");
            AssertRoster(laptop, "Stock", "Q2 Heat 1", "S05", "S07", "S02", "S04");
            AssertRoster(laptop, "Stock", "Q2 Heat 2", "S06", "S01", "S03");
            AssertRoster(laptop, "Mod", "Q1 Heat 1", "M01", "M02", "M03");
            AssertRoster(laptop, "Mod", "Q2 Heat 1", "M02", "M03", "M01");
            Assert.DoesNotContain(laptop.Rounds.All, r => r.AssignedTransponders.Contains("S08"));

            // Run in programme order, each heat the one the phone says is next.
            // Mod Q1 first (classes build in name order within a round).
            //   Mod Q1: Ivy 12.0 flags at 60.0; Jay 62.5, Kim 65.0.
            var modQ1 = laptop.NextRound("Mod", "Q1 Heat 1");
            laptop.Start(modQ1);
            laptop.Drive(Q1, after: 0, upTo: 25_000);

            // Nothing that would pull the heat out from under the race.
            Assert.Contains("on track", laptop.Refused("program.buildQualifying"));

            laptop.Drive(Q1, after: 25_000);
            AssertResult(laptop.File(modQ1), ("M01", 5, 60_000), ("M02", 5, 62_500), ("M03", 5, 65_000));

            //   Stock Q1 H1 (A C E G): Alice flags at 50.0; Cara 52.0, Eve 54.0, Gus 56.0.
            var stockQ1H1 = laptop.NextRound("Stock", "Q1 Heat 1");
            laptop.Start(stockQ1H1);
            laptop.Drive(Q1);
            AssertResult(laptop.File(stockQ1H1), ("S01", 5, 50_000), ("S03", 5, 52_000), ("S05", 5, 54_000), ("S07", 5, 56_000));

            //   Stock Q1 H2 (B D F): Ben flags at 51.0; Dan 53.0; Finn, on 14s
            //   laps, was on 3 at 42.0 and takes the flag on his 4th, at 56.0.
            var stockQ1H2 = laptop.NextRound("Stock", "Q1 Heat 2");
            laptop.Start(stockQ1H2);
            laptop.Drive(Q1);
            AssertResult(laptop.File(stockQ1H2), ("S02", 5, 51_000), ("S04", 5, 53_000), ("S06", 4, 56_000));

            //   Mod Q2 (J K I): Kim 11.8 flags at 59.0; Ivy 60.0, Jay 62.5.
            var modQ2 = laptop.NextRound("Mod", "Q2 Heat 1");
            laptop.Start(modQ2);
            laptop.Drive(Q2);
            AssertResult(laptop.File(modQ2), ("M03", 5, 59_000), ("M01", 5, 60_000), ("M02", 5, 62_500));

            //   Stock Q2 H1 (E G B D): Gus 10.1 flags at 50.5; Ben 51.0, Eve 54.0, Dan 54.5.
            var stockQ2H1 = laptop.NextRound("Stock", "Q2 Heat 1");
            laptop.Start(stockQ2H1);
            laptop.Drive(Q2);
            AssertResult(laptop.File(stockQ2H1), ("S07", 5, 50_500), ("S02", 5, 51_000), ("S05", 5, 54_000), ("S04", 5, 54_500));

            //   Stock Q2 H2 (F A C): Alice flags at 50.0; Cara 52.0, Finn 55.0.
            var stockQ2H2 = laptop.NextRound("Stock", "Q2 Heat 2");
            laptop.Start(stockQ2H2);
            laptop.Drive(Q2);
            AssertResult(laptop.File(stockQ2H2), ("S01", 5, 50_000), ("S03", 5, 52_000), ("S06", 5, 55_000));

            // --- Standings ------------------------------------------------------------------

            // Each driver's best heat: most laps, then least time.
            //   Alice 50.0, Gus 50.5 (Q2), Ben 51.0, Cara 52.0, Dan 53.0 (Q1),
            //   Eve 54.0, Finn 55.0 (Q2 - his Q1 was a lap down).
            Assert.Equal(new[] { "Alice", "Gus", "Ben", "Cara", "Dan", "Eve", "Finn" }, laptop.Standings("Stock"));
            Assert.Equal(new[] { "Kim", "Ivy", "Jay" }, laptop.Standings("Mod"));

            Assert.Equal("seedMains", laptop.NextStage());

            // --- The finals ------------------------------------------------------------------

            // Fours, fastest first: A = Alice Gus Ben Cara; B = Dan Eve Finn.
            var seeding = laptop.Question("program.seedMains", new { className = "Stock" });
            Assert.Contains("Put 7 qualified drivers in \"Stock\" into 2 mains", seeding);

            Assert.Equal("Built 2 mains for Stock.", laptop.Ask("program.seedMains", new { className = "Stock" }).GetProperty("message").GetString());
            Assert.Equal("Built 1 main for Mod.", laptop.Ask("program.seedMains", new { className = "Mod" }).GetProperty("message").GetString());

            AssertRoster(laptop, "Stock", "A-Main", "S01", "S07", "S02", "S03");
            AssertRoster(laptop, "Stock", "B-Main", "S04", "S05", "S06");
            AssertRoster(laptop, "Mod", "A-Main", "M03", "M01", "M02");

            // The lowest main runs first, so its winner can move up.
            //   B-main: Finn 10.5 flags at 52.5; Dan 53.5, Eve 54.5.
            var bMain = laptop.NextRound("Stock", "B-Main");
            laptop.Start(bMain);
            laptop.Drive(BMain);
            AssertResult(laptop.File(bMain), ("S06", 5, 52_500), ("S04", 5, 53_500), ("S05", 5, 54_500));

            Assert.Equal("bumpUps", laptop.NextStage());

            var bump = laptop.Question("program.bumpUps", new { className = "Stock" });
            Assert.Contains("Bump 1 driver from B-Main up into A-Main?", bump);
            Assert.Contains("Finn", bump);

            Assert.Contains("Promoted 1 driver into A-Main", laptop.Ask("program.bumpUps", new { className = "Stock" }).GetProperty("message").GetString());
            AssertRoster(laptop, "Stock", "A-Main", "S01", "S07", "S02", "S03", "S06");

            // --- The A-main, and the laptop going down in the middle of it ---------------

            var aMain = laptop.NextRound("Stock", "A-Main");
            var aMainId = aMain.Id;
            laptop.Start(aMain);

            // Two laps each by 25.0 (Alice 10.0/20.0 ... Cara 10.4/20.8).
            laptop.Drive(AMain, after: 0, upTo: 25_000);
            laptop.Journal.Record(laptop.Race, laptop.StartedUtc, string.Empty, force: true, roundId: aMainId, raceLabel: "Stock A-Main");
            Assert.True(File.Exists(_data.PathOf("race-in-progress.json")));

            // Down. A new laptop, everything read back from disk.
            laptop.Dispose();
            laptop = new Laptop();

            var unfinished = RaceJournal.Unfinished();
            Assert.NotNull(unfinished);
            Assert.Equal(aMainId, unfinished!.RoundId);

            RaceJournal.Restore(laptop.Race, unfinished);
            Assert.Equal(RaceState.Racing, laptop.Race.State);
            Assert.Equal(FlagState.Red, laptop.Race.Flag);
            Assert.All(laptop.Race.GetOrderedStandings(), s => Assert.Equal(2, s.Laps));

            // Green, and on. Each car's first crossing after the restore is
            // timed from its last counted lap, so nothing is lost:
            //   Alice 30/40/50 flags at 50.0; Gus 50.5, Ben 51.0, Finn 51.5, Cara 52.0.
            var restoredMain = laptop.Rounds.Get(aMainId)!;
            laptop.Resume(restoredMain, unfinished.SessionStartedAtUtc ?? DateTime.UtcNow);
            laptop.Drive(AMain, after: 25_000);

            AssertResult(laptop.File(restoredMain),
                ("S01", 5, 50_000), ("S07", 5, 50_500), ("S02", 5, 51_000), ("S06", 5, 51_500), ("S03", 5, 52_000));
            Assert.False(File.Exists(_data.PathOf("race-in-progress.json")));

            //   Mod A-main: Kim 11.8 flags at 59.0; Ivy 60.0, Jay 62.5.
            var modMain = laptop.NextRound("Mod", "A-Main");
            laptop.Start(modMain);
            laptop.Drive(ModMain);
            var modResult = laptop.File(modMain);
            AssertResult(modResult, ("M03", 5, 59_000), ("M01", 5, 60_000), ("M02", 5, 62_500));

            // --- A correction from the phone --------------------------------------------------

            // Kim takes five seconds for contact: 59.0 + 5.0 = 64.0, behind
            // Ivy (60.0) and Jay (62.5).
            var correction = new
            {
                sessionId = modResult.Id,
                reason = "Contact on lap 3",
                changes = new[] { new { driverId = laptop.Phone.DriverIdFor("M03"), timePenaltySeconds = 5 } },
            };

            Assert.Contains("New order", laptop.Question("result.adjust", correction));
            laptop.Ask("result.adjust", correction);

            var corrected = laptop.History.Get(modResult.Id)!;
            Assert.Equal(
                new[] { "M01", "M02", "M03" },
                corrected.Results.OrderBy(r => r.Position).Select(r => r.Transponder));

            // And the same from a laptop started again: it was saved.
            laptop.Dispose();
            laptop = new Laptop();

            Assert.Equal(
                new[] { "M01", "M02", "M03" },
                laptop.History.Get(modResult.Id)!.Results.OrderBy(r => r.Position).Select(r => r.Transponder));

            // --- The end of the night -----------------------------------------------------------

            Assert.Equal("complete", laptop.NextStage());
            Assert.All(laptop.Program.TodaysRounds(), r => Assert.True(r.IsRecorded, $"{r.ClassName} {r.RoundLabel} was not recorded"));
            Assert.Equal(9, laptop.Program.TodaysRounds().Count);
        }
        finally
        {
            laptop.Dispose();
        }
    }

    // --- Checks ------------------------------------------------------------------------------

    private static void AssertRoster(Laptop laptop, string className, string label, params string[] expected)
    {
        var round = laptop.Program.TodaysRounds().Single(r => r.ClassName == className && r.RoundLabel == label);
        Assert.Equal(expected, round.AssignedTransponders);
    }

    private static void AssertResult(SessionRecord record, params (string Transponder, int Laps, long ElapsedMs)[] expected)
    {
        var actual = record.Results
            .OrderBy(r => r.Position)
            .Select(r => (r.Transponder, r.Laps, r.ElapsedMs))
            .ToArray();

        Assert.Equal(expected, actual);
    }

    // --- The laptop -------------------------------------------------------------------------

    /// <summary>
    /// The app's race-night parts, put together the way MainForm puts them
    /// together (see MainForm.WireRaceDayService): the stores read from the
    /// data folder, EventSpine binding them to the current race day, the
    /// Program tab, the race-day manager, the race session and the journal.
    /// No main window and no Race Control page: the phone never asks for a
    /// race.* op here, and races are started and filed by hand below, the
    /// way Race Control does it.
    /// </summary>
    private sealed class Laptop : IDisposable
    {
        public readonly AppSettings Settings = new() { AllowResultEditing = true };
        public readonly UndoService Undo = new();
        public readonly RaceEventStore Events = RaceEventStore.Load();
        public readonly RaceClassStore Classes = RaceClassStore.Load();
        public readonly ProfileStore Profiles = ProfileStore.Load();
        public readonly RoundDefinitionStore Rounds = RoundDefinitionStore.Load();
        public readonly SessionHistoryStore History = SessionHistoryStore.Load();
        public readonly CheckInStore CheckIns = CheckInStore.Load();
        public readonly TechInspectionStore Tech = TechInspectionStore.Load();
        public readonly DayScheduleStore Schedule = DayScheduleStore.Load();
        public readonly TrackLayoutStore Tracks = TrackLayoutStore.Load();
        public readonly RaceControlSession Race = new();
        public readonly RaceJournal Journal = new();
        public readonly List<string> Log = new();

        public readonly RaceProgramControl Program;
        public readonly RaceDayService Phone;

        /// <summary>The window the service answers on. Only its handle is needed: a request made on its thread runs there and then.</summary>
        private readonly Control _window = new();

        public Laptop()
        {
            EventSpine.Bind(Events, CheckIns, Tech, Schedule, Rounds);

            _ = _window.Handle;

            Program = new RaceProgramControl(Classes, Profiles, Rounds, Tracks, History, CheckIns, Settings, Undo, Events, Schedule);

            Phone = new RaceDayService(new RaceDayServiceHost
            {
                UiThread = _window,
                Settings = Settings,
                Undo = Undo,
                Race = Race,
                Events = Events,
                Classes = Classes,
                Profiles = Profiles,
                Rounds = Rounds,
                History = History,
                CheckIns = CheckIns,
                Tech = Tech,
                Schedule = Schedule,
                Program = Program,

                // Only the race.* ops use it, and this night never sends one.
                RaceControl = null!,

                ActiveRound = () => OnTrack,
                StartRound = (_, _, _) => "Races are started by the test, not the phone.",

                // MainForm.ApplyPhoneCheckIn, less the relay.
                SetCheckedIn = (transponder, wanted) =>
                {
                    if (CheckIns.IsCheckedIn(transponder) == wanted)
                    {
                        return false;
                    }

                    CheckIns.SetCheckedIn(transponder, wanted);
                    return true;
                },

                RepublishSession = _ => { },
                RefreshDesk = () => Program.RefreshAll(),
                Log = Log.Add,
            });
        }

        /// <summary>The round in Race Control's "Round:" box - what is on track.</summary>
        public RoundDefinition? OnTrack { get; private set; }

        /// <summary>When the race on track started, as Race Control stamps it.</summary>
        public DateTime StartedUtc { get; private set; }

        public void Dispose()
        {
            Program.Dispose();
            _window.Dispose();
        }

        // --- The phone ---

        /// <summary>Sends a request, answers any "are you sure?" with yes, and returns the answer - which must be ok.</summary>
        public JsonElement Ask(string op, object? args = null)
        {
            var answer = Send(op, args, confirm: null);

            if (answer.GetProperty("needsConfirm").GetBoolean())
            {
                answer = Send(op, args, answer.GetProperty("confirmToken").GetString());
            }

            Assert.True(answer.GetProperty("ok").GetBoolean(), $"{op} was refused: {answer}");
            return answer;
        }

        /// <summary>The question a confirming op asks first - without answering it.</summary>
        public string Question(string op, object? args = null)
        {
            var answer = Send(op, args, confirm: null);

            Assert.True(answer.GetProperty("ok").GetBoolean(), $"{op} was refused: {answer}");
            Assert.True(answer.GetProperty("needsConfirm").GetBoolean(), $"{op} did not ask first: {answer}");

            return answer.GetProperty("message").GetString() ?? string.Empty;
        }

        /// <summary>The refusal an op answers with, which it must.</summary>
        public string Refused(string op, object? args = null)
        {
            var answer = Send(op, args, confirm: null);

            Assert.False(answer.GetProperty("ok").GetBoolean(), $"{op} was not refused: {answer}");
            return answer.GetProperty("error").GetString() ?? string.Empty;
        }

        private JsonElement Send(string op, object? args, string? confirm)
        {
            var request = new Dictionary<string, object?> { ["op"] = op, ["args"] = args ?? new { } };

            if (confirm is not null)
            {
                request["confirm"] = confirm;
            }

            // On this thread, the window's own, so it runs straight away.
            var json = Phone.HandleJsonAsync(JsonSerializer.Serialize(request), RaceDayChannel.WiFi, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        /// <summary>The Next Steps banner's stage, as the phone's Today page reads it.</summary>
        public string NextStage() =>
            Ask("today.summary").GetProperty("data").GetProperty("nextStep").GetProperty("stage").GetString() ?? string.Empty;

        /// <summary>The next round to run, as the phone is told - which must be the one named.</summary>
        public RoundDefinition NextRound(string className, string label)
        {
            var next = Ask("today.summary").GetProperty("data").GetProperty("nextRound");

            Assert.Equal(JsonValueKind.Object, next.ValueKind);
            Assert.Equal(className, next.GetProperty("className").GetString());
            Assert.Equal(label, next.GetProperty("label").GetString());

            return Rounds.Get(next.GetProperty("id").GetString()!)!;
        }

        /// <summary>Qualifying standings for a class, names in order, as the phone shows them.</summary>
        public string[] Standings(string className) =>
            Ask("program.standings", new { className })
                .GetProperty("data")
                .GetProperty("standings")
                .EnumerateArray()
                .Select(e => e.GetProperty("name").GetString() ?? string.Empty)
                .ToArray();

        // --- Race Control ---

        /// <summary>Picks the round, stages, and throws the green - a five-lap race.</summary>
        public void Start(RoundDefinition round)
        {
            OnTrack = round;
            StartedUtc = DateTime.UtcNow;

            Race.LapLimit = LapLimit;
            Race.Stage();
            Race.GreenFlag(0);

            Assert.Equal(RaceState.Racing, Race.State);
        }

        /// <summary>A race recovered from the journal, under red: the round put back in the box, and the green thrown again.</summary>
        public void Resume(RoundDefinition round, DateTime startedUtc)
        {
            OnTrack = round;
            StartedUtc = startedUtc;

            Race.GreenFlag(0);

            Assert.Equal(FlagState.Green, Race.Flag);
        }

        /// <summary>
        /// Every crossing of the cars on track between the two race times,
        /// in time order, until the race finishes. Each car crosses once a
        /// lap: at 1, 2, 3... times its lap time.
        /// </summary>
        public void Drive(IReadOnlyDictionary<string, int> lapMs, long after = 0, long upTo = long.MaxValue)
        {
            var cars = OnTrack!.AssignedTransponders;

            var crossings = cars
                .SelectMany(car => Enumerable.Range(1, 30).Select(lap => (Time: (long)lap * lapMs[car], Car: car)))
                .Where(c => c.Time > after && c.Time <= upTo)
                .OrderBy(c => c.Time)
                .ThenBy(c => c.Car, StringComparer.Ordinal)
                .ToList();

            foreach (var (time, car) in crossings)
            {
                if (Race.State == RaceState.Finished)
                {
                    break;
                }

                Race.ProcessLapEvent(new DirectLapEvent
                {
                    TransponderId = car,
                    DeviceTimeMs = (uint)time,
                    SignalHits = 1,
                    SignalStrength = 100,
                    IsSynthetic = true,
                });
            }

            if (upTo == long.MaxValue)
            {
                Assert.Equal(RaceState.Finished, Race.State);
            }
        }

        /// <summary>
        /// Files the finished race the way Race Control does
        /// (RaceControlControl.SaveCompletedSessionToHistory): ghosts left
        /// out, the rows from RaceFiling.ResultsFrom, then RaceFiling.Commit
        /// against the round in the box. Returns what was saved.
        /// </summary>
        public SessionRecord File(RoundDefinition round)
        {
            Assert.Equal(RaceState.Finished, Race.State);

            var racers = Race.GetOrderedStandings()
                .Where(s => !GhostService.IsGhost(s.TransponderId))
                .ToList();

            var record = new SessionRecord
            {
                SessionType = RaceSessionType.Race,
                RoundId = round.Id,
                Label = $"{round.ClassName} {round.RoundLabel}",
                StartedAtUtc = StartedUtc,
                EndedAtUtc = DateTime.UtcNow,
                Results = RaceFiling.ResultsFrom(racers, Profiles),
                RecordsIncidents = true,
            };

            var outcome = RaceFiling.Commit(
                record,
                round,
                false,
                true,
                History,
                Rounds,
                Journal,
                afterSaved: () => { },
                beforeJournalCleared: () => { },
                log: Log.Add,
                raceDayOpen: true);

            Assert.Equal(RaceFiling.Outcome.Saved, outcome);
            Assert.True(Rounds.Get(round.Id)!.IsRecorded, $"{round.RoundLabel} was not marked as run");

            OnTrack = null;
            return record;
        }
    }

    // --- Running it where Windows Forms is at home ------------------------------------------

    /// <summary>
    /// Runs the night on a thread of its own, single-threaded apartment, the
    /// way a Windows Forms app's UI thread is - the Program tab is a real
    /// control, and the race-day manager runs a request on the thread its
    /// window belongs to. A failure on that thread is thrown again here, so
    /// the test reports it as usual.
    /// </summary>
    private static void OnUiThread(Action body)
    {
        ExceptionDispatchInfo? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        failure?.Throw();
    }
}
