using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, the race-day manager's entry point (RaceDayService.cs):
///  - #261: a confirmation sent after it ran out was swept away before it
///    was looked up, so the manager was told the laptop never gave it
///    instead of "it ran out (they last a minute)".
///  - Asked by another fixer: an answer for a request that never ran
///    (closing or still starting, withdrawn, canceled) says notRun: true.
/// Windows-only: the laptop's Program tab is a real control.
/// </summary>
public sealed class BugHunt24_RaceDayServiceTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- #261: a late yes -----------------------------------------------------------------

    [Fact]
    public void A_confirmation_sent_after_a_minute_is_told_it_ran_out_not_that_the_laptop_never_gave_it() => OnUiThread(() =>
    {
        using var laptop = new Laptop();
        var round = ARoundToDelete(laptop);

        var asked = laptop.Send("program.deleteRound", new { roundId = round.Id });
        Assert.True(asked.GetProperty("needsConfirm").GetBoolean(), asked.GetRawText());
        var token = asked.GetProperty("confirmToken").GetString();

        laptop.Now += RaceDayService.ConfirmLifetime + TimeSpan.FromSeconds(1);

        var late = laptop.Send("program.deleteRound", new { roundId = round.Id }, token);

        Assert.False(late.GetProperty("ok").GetBoolean(), late.GetRawText());
        var error = late.GetProperty("error").GetString() ?? string.Empty;
        Assert.Contains("ran out (they last a minute)", error);
        Assert.DoesNotContain("isn't one the laptop gave", error);
        Assert.Contains(laptop.Rounds.All, r => r.Id == round.Id);

        // It was used up by being answered: sending it again is not "ran out" a second time.
        var again = laptop.Send("program.deleteRound", new { roundId = round.Id }, token);
        Assert.Contains("isn't one the laptop gave", again.GetProperty("error").GetString());
        Assert.Contains(laptop.Rounds.All, r => r.Id == round.Id);
    });

    [Fact]
    public void Another_request_arriving_first_does_not_turn_a_late_yes_into_a_forged_one() => OnUiThread(() =>
    {
        using var laptop = new Laptop();
        var round = ARoundToDelete(laptop);

        var asked = laptop.Send("program.deleteRound", new { roundId = round.Id });
        var token = asked.GetProperty("confirmToken").GetString();

        laptop.Now += RaceDayService.ConfirmLifetime + TimeSpan.FromSeconds(5);

        // Another phone reads the day in the meantime - every request sweeps old confirmations.
        Assert.True(laptop.Send("hello", null).GetProperty("ok").GetBoolean());

        var late = laptop.Send("program.deleteRound", new { roundId = round.Id }, token);

        Assert.Contains("ran out (they last a minute)", late.GetProperty("error").GetString());
        Assert.Contains(laptop.Rounds.All, r => r.Id == round.Id);
    });

    [Fact]
    public void A_yes_inside_the_minute_still_does_it() => OnUiThread(() =>
    {
        using var laptop = new Laptop();
        var round = ARoundToDelete(laptop);

        var asked = laptop.Send("program.deleteRound", new { roundId = round.Id });
        var token = asked.GetProperty("confirmToken").GetString();

        laptop.Now += TimeSpan.FromSeconds(50);

        var done = laptop.Send("program.deleteRound", new { roundId = round.Id }, token);

        Assert.True(done.GetProperty("ok").GetBoolean(), done.GetRawText());
        Assert.DoesNotContain(laptop.Rounds.All, r => r.Id == round.Id);
    });

    // --- notRun on answers for requests that never ran ------------------------------------

    [Fact]
    public void Closing_or_still_starting_says_the_request_never_ran() => OnUiThread(() =>
    {
        using var laptop = new Laptop(isClosing: true);

        var answer = laptop.Send("hello", null);

        Assert.False(answer.GetProperty("ok").GetBoolean());
        Assert.Contains("closing or still starting", answer.GetProperty("error").GetString());
        Assert.True(answer.GetProperty("notRun").GetBoolean(), answer.GetRawText());
    });

    [Fact]
    public void A_canceled_request_says_it_never_ran() => OnUiThread(() =>
    {
        using var laptop = new Laptop();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        // From another thread, so it is handed to the window's thread - which
        // is busy here, so the request is withdrawn before it is picked up.
        var json = Task.Run(() => laptop.Phone.HandleJsonAsync("{\"op\":\"hello\"}", RaceDayChannel.WiFi, canceled.Token))
            .GetAwaiter()
            .GetResult();

        using var document = JsonDocument.Parse(json);
        var answer = document.RootElement;

        Assert.False(answer.GetProperty("ok").GetBoolean());
        Assert.Contains("Canceled - nothing was changed.", answer.GetProperty("error").GetString());
        Assert.True(answer.GetProperty("notRun").GetBoolean(), answer.GetRawText());
    });

    [Fact]
    public void An_answer_for_a_request_that_ran_has_no_notRun() => OnUiThread(() =>
    {
        using var laptop = new Laptop();

        var ran = laptop.Send("hello", null);
        Assert.True(ran.GetProperty("ok").GetBoolean());
        Assert.False(ran.TryGetProperty("notRun", out _), ran.GetRawText());

        // A refusal ran too: the laptop looked at it.
        var refused = laptop.Send("no.such.op", null);
        Assert.False(refused.GetProperty("ok").GetBoolean());
        Assert.False(refused.TryGetProperty("notRun", out _), refused.GetRawText());
    });

    // --- Helpers --------------------------------------------------------------------------

    /// <summary>A run round on file, as BugHunt24_RaceDayOpsProgramTests' delete test sets one up: deleting it asks first.</summary>
    private static RoundDefinition ARoundToDelete(Laptop laptop)
    {
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

        return run;
    }

    private static void OnUiThread(Action body) =>
        StaThread.Run(() =>
        {
            body();
            return true;
        }, TimeSpan.FromMinutes(1), () => "bug hunt 24, race-day service");

    /// <summary>The race-day manager on the laptop's own stores, with its confirmation clock in the test's hands.</summary>
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

        public DateTime Now = new(2026, 11, 23, 19, 0, 0, DateTimeKind.Utc);

        private readonly Control _window = new();

        public Laptop(bool isClosing = false)
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
                SetCheckedIn = (_, _) => false,
                RepublishSession = _ => { },
                RefreshDesk = () => Program.RefreshAll(),
                Log = _ => { },
                IsClosing = () => isClosing,
            });

            Phone.ConfirmClock = () => Now;
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
