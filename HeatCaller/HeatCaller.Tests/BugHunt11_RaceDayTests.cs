using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11, race day:
///
///  - results.publish from the phone sent a race to the online relay even
///    when its filing had failed (race-history.json locked, so
///    SessionHistoryStore.Add returned false). The race is still listed on
///    /manage (History.All), but every other publish path reads Saved /
///    SavedView, so a race that is not on disk is never published. The phone
///    op now refuses it until a retry files it.
///
/// Windows-only: the laptop's Program tab is a real control.
/// </summary>
public sealed class BugHunt11_RaceDayTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private bool AddWhileLocked(SessionHistoryStore store, SessionRecord record)
    {
        using (new FileStream(_data.PathOf("race-history.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            return store.Add(record);
        }
    }

    [Fact]
    public void A_race_that_failed_to_file_is_not_republished_from_the_phone() => OnUiThread(() =>
    {
        using var laptop = new Laptop();
        laptop.Settings.RelayEnabled = true;

        Assert.True(laptop.History.Add(new SessionRecord { Label = "Q1 Heat 1" }));

        var aMain = new SessionRecord { Label = "A-Main" };
        Assert.False(AddWhileLocked(laptop.History, aMain));
        Assert.True(laptop.History.IsUnsaved(aMain));

        var answer = laptop.Send("results.publish", new { sessionId = aMain.Id }, RaceDayChannel.WiFi);

        Assert.False(answer.GetProperty("ok").GetBoolean());
        Assert.Contains("isn't saved", answer.GetProperty("error").GetString());
        Assert.Empty(laptop.Republished);
    });

    [Fact]
    public void Once_the_retry_files_it_the_phone_can_republish_it() => OnUiThread(() =>
    {
        using var laptop = new Laptop();
        laptop.Settings.RelayEnabled = true;

        Assert.True(laptop.History.Add(new SessionRecord { Label = "Q1 Heat 1" }));

        var aMain = new SessionRecord { Label = "A-Main" };
        Assert.False(AddWhileLocked(laptop.History, aMain));
        Assert.True(laptop.History.Add(aMain));

        var answer = laptop.Send("results.publish", new { sessionId = aMain.Id }, RaceDayChannel.WiFi);

        Assert.True(answer.GetProperty("ok").GetBoolean(), answer.GetRawText());
        Assert.Equal(new[] { aMain.Id }, laptop.Republished);
    });

    private static void OnUiThread(Action body) =>
        StaThread.Run(() =>
        {
            body();
            return true;
        }, TimeSpan.FromMinutes(1), () => "bug hunt 11, race day");

    /// <summary>The race-day manager on the laptop's own stores, as BugHunt4RaceDayTests puts it together - counting republishes.</summary>
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
        public readonly List<string> Republished = new();

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
                SetCheckedIn = (_, _) => false,
                RepublishSession = session => Republished.Add(session.Id),
                RefreshDesk = () => Program.RefreshAll(),
                Log = _ => { },
            });
        }

        public void Dispose()
        {
            Program.Dispose();
            _window.Dispose();
        }

        public JsonElement Send(string op, object? args, RaceDayChannel channel)
        {
            var request = new Dictionary<string, object?> { ["op"] = op, ["args"] = args ?? new { } };

            var json = Phone.HandleJsonAsync(JsonSerializer.Serialize(request), channel, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
    }
}
