using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: a driver seated in a qualifying heat under his alternate
/// transponder was offered for another heat of the same round with no heat
/// against his name, and the phone's "Add" then put him in it under his main
/// transponder - one driver on two grids of one qualifying round.
/// Windows-only: the laptop's Program tab is a real control.
/// </summary>
public sealed class BugHunt17_RaceDayOpsProgramTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_driver_seated_under_his_alternate_is_not_put_in_a_second_heat_of_the_round() => OnUiThread(() =>
    {
        using var laptop = new Laptop();
        var (heat1, heat2) = TwoHeatsWithBobUnderHisAlternate(laptop);

        var shown = laptop.Send("program.heat", new { roundId = heat2.Id });
        Assert.True(shown.GetProperty("ok").GetBoolean(), shown.GetRawText());

        // Offered for Heat 2, but shown as already in Heat 1.
        var bob = shown.GetProperty("data").GetProperty("available").EnumerateArray()
            .Single(d => d.GetProperty("name").GetString() == "Bob");
        Assert.Equal(new[] { "Q1 Heat 1" }, bob.GetProperty("inHeats").EnumerateArray().Select(h => h.GetString()));

        var added = laptop.Send("program.moveDriver", new { toRoundId = heat2.Id, driverId = bob.GetProperty("id").GetString() });

        Assert.False(added.GetProperty("ok").GetBoolean(), added.GetRawText());
        Assert.Equal(new[] { "S02" }, laptop.Rounds.All.Single(r => r.Id == heat2.Id).AssignedTransponders);
        Assert.Equal(new[] { "ALT555", "S01" }, laptop.Rounds.All.Single(r => r.Id == heat1.Id).AssignedTransponders);
    });

    [Fact]
    public void He_can_still_be_moved_from_the_one_heat_to_the_other() => OnUiThread(() =>
    {
        using var laptop = new Laptop();
        var (heat1, heat2) = TwoHeatsWithBobUnderHisAlternate(laptop);

        var shown = laptop.Send("program.heat", new { roundId = heat1.Id });
        var bobId = shown.GetProperty("data").GetProperty("drivers").EnumerateArray()
            .Single(d => d.GetProperty("name").GetString() == "Bob").GetProperty("id").GetString();

        var moved = laptop.Send("program.moveDriver", new { fromRoundId = heat1.Id, toRoundId = heat2.Id, driverId = bobId });

        Assert.True(moved.GetProperty("ok").GetBoolean(), moved.GetRawText());
        Assert.Equal(new[] { "S01" }, laptop.Rounds.All.Single(r => r.Id == heat1.Id).AssignedTransponders);
        Assert.Equal(new[] { "S02", "ALT555" }, laptop.Rounds.All.Single(r => r.Id == heat2.Id).AssignedTransponders);
    });

    private static (RoundDefinition Heat1, RoundDefinition Heat2) TwoHeatsWithBobUnderHisAlternate(Laptop laptop)
    {
        laptop.Classes.Upsert(new RaceClass { Name = "Stock" });
        Assert.True(laptop.Profiles.TryAdd(new DriverProfile { Transponder = "MAIN123", AlternateTransponder = "ALT555", DriverName = "Bob", ClassName = "Stock" }, out _));
        Assert.True(laptop.Profiles.TryAdd(new DriverProfile { Transponder = "S01", DriverName = "Ava", ClassName = "Stock" }, out _));
        Assert.True(laptop.Profiles.TryAdd(new DriverProfile { Transponder = "S02", DriverName = "Cy", ClassName = "Stock" }, out _));

        var heat1 = new RoundDefinition
        {
            ClassName = "Stock", Kind = RoundKind.Qualifier, RoundLabel = "Q1 Heat 1",
            AssignedTransponders = new List<string> { "ALT555", "S01" },
        };

        var heat2 = new RoundDefinition
        {
            ClassName = "Stock", Kind = RoundKind.Qualifier, RoundLabel = "Q1 Heat 2",
            AssignedTransponders = new List<string> { "S02" },
        };

        laptop.Rounds.Add(heat1);
        laptop.Rounds.Add(heat2);

        return (heat1, heat2);
    }

    private static void OnUiThread(Action body) =>
        StaThread.Run(() =>
        {
            body();
            return true;
        }, TimeSpan.FromMinutes(1), () => "bug hunt 17, race-day program ops");

    /// <summary>
    /// The race-day manager on the laptop's own stores, put together the way
    /// BugHunt4RaceDayTests' laptop is (and MainForm.WireRaceDayService).
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

        /// <summary>One request over Wi-Fi, on the window's own thread, and its answer - unconfirmed.</summary>
        public JsonElement Send(string op, object? args)
        {
            var request = new Dictionary<string, object?> { ["op"] = op, ["args"] = args ?? new { } };

            var json = Phone.HandleJsonAsync(JsonSerializer.Serialize(request), RaceDayChannel.WiFi, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
    }
}
