using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 22 (20 Nov 2026): Build the Finals made the Pole Shootout's
/// runs, and its undo removed them by id. A driver going home since built
/// the runs again with new ids, so the undo took the mains away and left
/// the rebuilt runs on the programme with nothing to grid.
/// </summary>
public sealed class BugHunt22_ShootoutUndoTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    private readonly RaceEventStore _events = RaceEventStore.Load();
    private readonly RoundDefinitionStore _rounds = RoundDefinitionStore.Load();
    private readonly SessionHistoryStore _history = SessionHistoryStore.Load();
    private readonly ProfileStore _profiles = ProfileStore.Load();
    private readonly CheckInStore _checkIns = CheckInStore.Load();
    private readonly RaceClassStore _classes = RaceClassStore.Load();
    private readonly UndoService _undo = new();

    private const string Stock = "Stock";

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Undoing_the_finals_after_the_shootout_was_built_again_takes_the_new_runs_too() => OnUiThread(() =>
    {
        var day = new RaceEvent { Name = "Tonight", CheckedInDriversOnly = true };
        _events.Upsert(day);
        _events.SetActive(day.Id);
        _checkIns.CurrentEventId = day.Id;
        _classes.Upsert(new RaceClass { Name = Stock, PoleShootout = true, PoleShootoutDrivers = 5 });

        var drivers = Enumerable.Range(1, 5).Select(i => $"D{i}").ToList();

        foreach (var t in drivers)
        {
            Assert.True(_profiles.TryAdd(new DriverProfile { Transponder = t, DriverName = t, ClassName = Stock }, out _));
            _checkIns.SetCheckedIn(t, true);
        }

        RunHeat("Q1", drivers.Select((t, i) => (t, 20 - i)).ToArray());

        using var program = new RaceProgramControl(_classes, _profiles, _rounds, TrackLayoutStore.Load(), _history, _checkIns, new AppSettings(), _undo, _events);

        var done = program.SeedMainsFor(Stock, string.Empty);
        Assert.True(done.Ok, done.Title + ": " + done.Message);

        var firstRuns = Shootout().Select(r => r.Id).ToList();
        Assert.Equal(5, firstRuns.Count);

        // A driver goes home: four in the shootout, built again with new ids.
        _checkIns.SetCheckedIn("D5", false);
        typeof(RaceProgramControl).GetMethod("KeepShootoutRunsInStep", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(program, new object?[] { Stock, _classes.All.Single(c => c.Name == Stock) });

        var rebuilt = Shootout();
        Assert.Equal(4, rebuilt.Count);
        Assert.DoesNotContain(rebuilt, r => firstRuns.Contains(r.Id));

        Assert.NotNull(_undo.Undo());

        Assert.Empty(Shootout());
        Assert.DoesNotContain(_rounds.ForClass(Stock), r => r.Kind == RoundKind.Main);
    });

    private List<RoundDefinition> Shootout() => _rounds.ForClass(Stock).Where(r => r.Kind == RoundKind.Shootout).ToList();

    private void RunHeat(string label, params (string Transponder, int Laps)[] finishers)
    {
        var round = new RoundDefinition { ClassName = Stock, RoundLabel = label, Kind = RoundKind.Qualifier };
        _rounds.Add(round);
        _events.AddRoundsToActive(new[] { round.Id });

        var session = new SessionRecord
        {
            Label = label,
            SessionType = RaceSessionType.Race,
            RoundId = round.Id,
            StartedAtUtc = DateTime.UtcNow,
            EndedAtUtc = DateTime.UtcNow,
            Results = finishers.Select((f, i) => new SessionResultEntry
            {
                Position = i + 1,
                Transponder = f.Transponder,
                DriverName = f.Transponder,
                Laps = f.Laps,
                ElapsedMs = 300_000,
            }).ToList(),
        };

        Assert.True(_history.Add(session));
        Assert.True(_rounds.MarkRecorded(round.Id, session.Id));
    }

    private static void OnUiThread(Action body) =>
        StaThread.Run(() =>
        {
            body();
            return true;
        }, TimeSpan.FromMinutes(1), () => "bug hunt 22, shootout undo");
}
