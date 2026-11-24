using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, the race-day manager's programme ops (RaceDayOpsProgram.cs):
///  - #257: the Seed Mains question sent an unnamed car's whole transponder
///    through the relay.
///  - #258: the phone moved drivers in posted heats without asking first.
///  - #40: the phone's Delete round told a manager nothing was lost for a
///    round that had already been run.
/// The end-to-end facts are Windows-only: the laptop's Program tab is a real control.
/// </summary>
public sealed class BugHunt24_RaceDayOpsProgramTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- #257: Seed Mains question through the relay ------------------------------------

    [Fact]
    public void The_seed_mains_grid_names_an_unnamed_car_by_its_last_three_over_the_relay()
    {
        var profiles = ProfileStore.Load();
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "7654321", ClassName = "Stock" }, out _));
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "4440001", DriverName = "Bea", ClassName = "Stock" }, out _));

        // The unnamed car on file, a car with no profile at all (blank name,
        // so the preview prints its number), and Bea.
        var plan = MainsBuilder.PlanSeeding(new[] { "7654321", "8887776", "4440001" }, 2);
        var entries = new Dictionary<string, QualifyingCalculator.Entry>(StringComparer.OrdinalIgnoreCase)
        {
            ["7654321"] = new("7654321", "7654321", "", 15, 302_300, 20_000, 2, SourceRoundLabel: "Q2 Heat 1"),
            ["8887776"] = new("8887776", "", "", 14, 305_100, 20_100, 2, SourceRoundLabel: "Q1 Heat 2"),
            ["4440001"] = new("4440001", "Bea", "", 14, 301_000, 21_000, 2, SourceRoundLabel: "Q2 Heat 2"),
        };

        var desk = MainsPreview.Text(plan, entries, QualifyingMethod.TotalLaps, 3, bumpUp: 1);
        Assert.Contains("7654321", desk);
        Assert.Contains("8887776", desk);

        // "14" is known as a car too, so a lap count that matches a short
        // transponder must not be touched: only the name on the line is.
        var known = new[] { "7654321", "8887776", "4440001", "14" };
        var relay = RaceDayService.MaskSeedingQuestionForPhone(desk, profiles, known, RaceDayChannel.Relay);

        Assert.DoesNotContain("7654321", relay);
        Assert.DoesNotContain("8887776", relay);
        Assert.Contains("   1. ...321 - 15 laps", relay);
        Assert.Contains("   2. ...776 - 14 laps", relay);
        Assert.Contains("   3. Bea - 14 laps", relay);

        // On the Wi-Fi the desk's words, unchanged.
        Assert.Equal(desk, RaceDayService.MaskSeedingQuestionForPhone(desk, profiles, known, RaceDayChannel.WiFi));
    }

    [Fact]
    public void The_invitational_grid_names_an_unnamed_car_by_its_last_three_over_the_relay()
    {
        var profiles = ProfileStore.Load();
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "7654321", ClassName = "Stock" }, out _));
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "4440001", DriverName = "Bea", ClassName = "Stock" }, out _));

        // InvitationalQuestion's grid: "   N. name", nothing after it.
        var desk = "Build the Invitational for \"Stock\": 2 drivers, seeded in roster order, in 3 mains?\r\n\r\n"
                   + "Main 1's grid:\r\n   1. 7654321\r\n   2. Bea\r\n\r\nEvery driver races every main.";

        var relay = RaceDayService.MaskSeedingQuestionForPhone(desk, profiles, Array.Empty<string>(), RaceDayChannel.Relay);

        Assert.DoesNotContain("7654321", relay);
        Assert.Contains("   1. ...321\r\n   2. Bea\r\n", relay);
        Assert.StartsWith("Build the Invitational for \"Stock\"", relay);
    }

    // --- #258: moving a driver in posted heats from the phone ----------------------------

    [Fact]
    public void Taking_a_driver_out_of_a_posted_heat_asks_first() => OnUiThread(() =>
    {
        using var laptop = new Laptop();
        var (heat1, _) = TwoHeatsTonight(laptop, posted: true);

        var asked = laptop.Send("program.moveDriver", new { fromRoundId = heat1.Id, driverId = laptop.Phone.DriverIdFor("S01") });

        Assert.True(asked.GetProperty("ok").GetBoolean(), asked.GetRawText());
        Assert.True(asked.GetProperty("needsConfirm").GetBoolean(), asked.GetRawText());

        var question = asked.GetProperty("message").GetString() ?? string.Empty;
        Assert.Contains("The heats were posted at", question);
        Assert.Contains("This takes Ava out of Q1 Heat 1. Go ahead anyway?", question);
        Assert.DoesNotContain("right-click", question);

        // Nothing moved until it was answered.
        Assert.Equal(new[] { "S01", "S02" }, laptop.Rounds.All.Single(r => r.Id == heat1.Id).AssignedTransponders);

        var done = laptop.Send("program.moveDriver", new { fromRoundId = heat1.Id, driverId = laptop.Phone.DriverIdFor("S01") },
            asked.GetProperty("confirmToken").GetString());

        Assert.True(done.GetProperty("ok").GetBoolean(), done.GetRawText());
        Assert.Equal(new[] { "S02" }, laptop.Rounds.All.Single(r => r.Id == heat1.Id).AssignedTransponders);
    });

    [Fact]
    public void Moving_a_driver_between_posted_heats_asks_once_and_says_when_a_heat_empties() => OnUiThread(() =>
    {
        using var laptop = new Laptop();
        var (heat1, heat2) = TwoHeatsTonight(laptop, posted: true);

        var asked = laptop.Send("program.moveDriver", new { fromRoundId = heat2.Id, toRoundId = heat1.Id, driverId = laptop.Phone.DriverIdFor("S03") });

        Assert.True(asked.GetProperty("needsConfirm").GetBoolean(), asked.GetRawText());
        var question = asked.GetProperty("message").GetString() ?? string.Empty;
        Assert.Contains("This moves Cy from Q1 Heat 2 to Q1 Heat 1. Q1 Heat 2 will have no drivers left. Go ahead anyway?", question);
        Assert.Equal(new[] { "S03" }, laptop.Rounds.All.Single(r => r.Id == heat2.Id).AssignedTransponders);
    });

    [Fact]
    public void Before_the_heats_are_posted_a_driver_is_taken_out_without_asking() => OnUiThread(() =>
    {
        using var laptop = new Laptop();
        var (heat1, _) = TwoHeatsTonight(laptop, posted: false);

        var done = laptop.Send("program.moveDriver", new { fromRoundId = heat1.Id, driverId = laptop.Phone.DriverIdFor("S01") });

        Assert.True(done.GetProperty("ok").GetBoolean(), done.GetRawText());
        Assert.False(done.GetProperty("needsConfirm").GetBoolean(), done.GetRawText());
        Assert.Equal(new[] { "S02" }, laptop.Rounds.All.Single(r => r.Id == heat1.Id).AssignedTransponders);
    });

    [Fact]
    public void Putting_a_driver_into_a_posted_heat_from_nowhere_is_not_asked() => OnUiThread(() =>
    {
        using var laptop = new Laptop();
        var (_, heat2) = TwoHeatsTonight(laptop, posted: true);

        var done = laptop.Send("program.moveDriver", new { toRoundId = heat2.Id, driverId = laptop.Phone.DriverIdFor("S04") });

        Assert.True(done.GetProperty("ok").GetBoolean(), done.GetRawText());
        Assert.False(done.GetProperty("needsConfirm").GetBoolean(), done.GetRawText());
        Assert.Equal(new[] { "S03", "S04" }, laptop.Rounds.All.Single(r => r.Id == heat2.Id).AssignedTransponders);
    });

    [Fact]
    public void The_posted_heats_question_for_the_phone_has_no_desk_only_line()
    {
        var day = new RaceEvent { Name = "Tonight", HeatsPostedUtc = DateTime.UtcNow };

        var question = RaceDayService.PostedMoveQuestionForPhone(day, "This takes Ava out of Q1 Heat 1.");

        Assert.StartsWith("The heats were posted at ", question);
        Assert.EndsWith("so drivers and marshals may already have looked theirs up.\r\n\r\nThis takes Ava out of Q1 Heat 1. Go ahead anyway?", question);
    }

    // --- #40: deleting a round that has been run, from the phone --------------------------

    [Fact]
    public void Deleting_a_run_round_asks_the_desks_already_run_question()
    {
        var run = new RoundDefinition { ClassName = "Stock", Kind = RoundKind.Main, RoundLabel = "A-Main", LinkedSessionId = "s1" };
        Assert.True(run.IsRecorded);

        var question = RaceDayService.DeleteRoundQuestionForPhone(run);

        Assert.Equal(RaceProgramControl.RecordedDeleteQuestion(new[] { run }), question);
        Assert.Contains("has already been run", question);
        Assert.DoesNotContain("This does not delete any recorded session history", question);
    }

    [Fact]
    public void Deleting_a_round_still_to_run_keeps_the_plain_question()
    {
        var scheduled = new RoundDefinition { ClassName = "Stock", Kind = RoundKind.Qualifier, RoundLabel = "Q1 Heat 1" };

        Assert.Equal(
            "Delete round \"Q1 Heat 1\" (Stock) from the program?\r\n\r\nThis does not delete any recorded session history.",
            RaceDayService.DeleteRoundQuestionForPhone(scheduled));
    }

    [Fact]
    public void The_phones_delete_of_a_run_round_says_it_has_been_run() => OnUiThread(() =>
    {
        using var laptop = new Laptop();
        laptop.Classes.Upsert(new RaceClass { Name = "Stock" });

        var session = new SessionRecord
        {
            SessionType = RaceSessionType.Race,
            Label = "Stock A-Main",
            Results = new List<SessionResultEntry>
            {
                new() { Position = 1, Transponder = "S01", DriverName = "Ava", Laps = 5 },
            },
        };

        Assert.True(laptop.History.Add(session));

        var run = new RoundDefinition
        {
            ClassName = "Stock", Kind = RoundKind.Main, RoundLabel = "A-Main",
            AssignedTransponders = new List<string> { "S01" }, LinkedSessionId = session.Id,
        };

        laptop.Rounds.Add(run);

        var asked = laptop.Send("program.deleteRound", new { roundId = run.Id });

        Assert.True(asked.GetProperty("needsConfirm").GetBoolean(), asked.GetRawText());
        var question = asked.GetProperty("message").GetString() ?? string.Empty;
        Assert.Contains("\"A-Main\" has already been run", question);
        Assert.DoesNotContain("This does not delete any recorded session history", question);
        Assert.Contains(laptop.Rounds.All, r => r.Id == run.Id);
    });

    // --- Helpers --------------------------------------------------------------------------

    /// <summary>
    /// Tonight's race day with two Stock heats on it: Ava and Bo in Q1 Heat 1,
    /// Cy alone in Q1 Heat 2, and Di on file in no heat. Posted or not.
    /// </summary>
    private static (RoundDefinition Heat1, RoundDefinition Heat2) TwoHeatsTonight(Laptop laptop, bool posted)
    {
        laptop.Classes.Upsert(new RaceClass { Name = "Stock" });
        Assert.True(laptop.Profiles.TryAdd(new DriverProfile { Transponder = "S01", DriverName = "Ava", ClassName = "Stock" }, out _));
        Assert.True(laptop.Profiles.TryAdd(new DriverProfile { Transponder = "S02", DriverName = "Bo", ClassName = "Stock" }, out _));
        Assert.True(laptop.Profiles.TryAdd(new DriverProfile { Transponder = "S03", DriverName = "Cy", ClassName = "Stock" }, out _));
        Assert.True(laptop.Profiles.TryAdd(new DriverProfile { Transponder = "S04", DriverName = "Di", ClassName = "Stock" }, out _));

        var day = EventSpine.StartEvent(laptop.Events, "Tonight", classNames: new[] { "Stock" });

        var heat1 = new RoundDefinition
        {
            ClassName = "Stock", Kind = RoundKind.Qualifier, RoundLabel = "Q1 Heat 1",
            AssignedTransponders = new List<string> { "S01", "S02" },
        };

        var heat2 = new RoundDefinition
        {
            ClassName = "Stock", Kind = RoundKind.Qualifier, RoundLabel = "Q1 Heat 2",
            AssignedTransponders = new List<string> { "S03" },
        };

        laptop.Rounds.Add(heat1);
        laptop.Rounds.Add(heat2);

        var active = laptop.Events.Active!;
        Assert.Equal(day.Id, active.Id);
        active.RoundIds.Add(heat1.Id);
        active.RoundIds.Add(heat2.Id);
        active.HeatsPostedUtc = posted ? DateTime.UtcNow.AddMinutes(-10) : null;
        laptop.Events.Upsert(active);

        return (heat1, heat2);
    }

    private static void OnUiThread(Action body) =>
        StaThread.Run(() =>
        {
            body();
            return true;
        }, TimeSpan.FromMinutes(1), () => "bug hunt 24, race-day program ops");

    /// <summary>
    /// The race-day manager on the laptop's own stores, put together the way
    /// BugHunt17_RaceDayOpsProgramTests' laptop is - with a confirmation that
    /// can be sent back.
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

        public readonly RaceProgramControl Program;
        public readonly RaceDayService Phone;

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
                Race = new RaceTiming(Race),
                Events = Events,
                Classes = Classes,
                Profiles = Profiles,
                Rounds = Rounds,
                History = History,
                CheckIns = CheckIns,
                Tech = Tech,
                Schedule = Schedule,
                Program = Program,
                RaceControl = null!,
                ActiveRound = () => null,
                StartRound = (_, _, _) => "Not from here.",
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
                Log = _ => { },
            });
        }

        public void Dispose()
        {
            Program.Dispose();
            _window.Dispose();
        }

        /// <summary>One request over Wi-Fi, on the window's own thread, and its answer - with the confirmation token when given.</summary>
        public JsonElement Send(string op, object? args, string? confirm = null)
        {
            var request = new Dictionary<string, object?> { ["op"] = op, ["args"] = args ?? new { } };

            if (confirm is not null)
            {
                request["confirm"] = confirm;
            }

            var json = Phone.HandleJsonAsync(JsonSerializer.Serialize(request), RaceDayChannel.WiFi, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
    }
}
