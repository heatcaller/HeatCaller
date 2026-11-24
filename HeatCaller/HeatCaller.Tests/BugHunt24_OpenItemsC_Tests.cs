namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 #269 (F256, 24 Nov 2026): a Pole Shootout run the director
/// deleted from the programme by hand came back the next time any race was
/// filed, with "built again for the new qualifying order" in the status
/// line though qualifying had not changed. The runs left now remember who
/// was left out and count only themselves, so the delete stands, the
/// shootout still finishes and sets the A-Main's front, a real change in
/// qualifying still builds the runs again (still without the driver left
/// out), and Undo puts the run back.
/// </summary>
public sealed class BugHunt24_OpenItemsC_Tests : IDisposable
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
    private static readonly string[] Field = { "q1", "q2", "q3", "q4", "q5" };

    public void Dispose() => _data.Dispose();

    // --- The rules, as table tests --------------------------------------------------

    [Fact]
    public void BugHunt24_269_A_run_deleted_and_recorded_as_left_out_is_unchanged_while_qualifying_is()
    {
        var runs = PoleShootout.BuildRuns(Stock, Field, 1, null, "Club");
        var deleted = runs.Single(r => r.AssignedTransponders[0] == "q3");
        var left = runs.Where(r => r != deleted).ToList();

        Assert.Equal(4, PoleShootout.LeaveOut(left, new[] { deleted }).Count);
        Assert.All(left, r => Assert.Equal(new[] { "q3" }, r.ShootoutLeftOut!));
        Assert.All(left, r => Assert.Equal(4, r.ShootoutRunCount));

        Assert.Equal(PoleShootout.RefreshResult.Unchanged, PoleShootout.Refresh(left, Field));
    }

    [Fact]
    public void BugHunt24_269_A_new_driver_in_the_top_five_still_builds_again_without_the_one_left_out()
    {
        var runs = PoleShootout.BuildRuns(Stock, Field, 1, null, "Club");
        var deleted = runs.Single(r => r.AssignedTransponders[0] == "q3");
        var left = runs.Where(r => r != deleted).ToList();
        PoleShootout.LeaveOut(left, new[] { deleted });

        var moved = new[] { "q6", "q1", "q2", "q3", "q4" };

        Assert.Equal(PoleShootout.RefreshResult.Rebuild, PoleShootout.Refresh(left, moved));

        var rebuilt = PoleShootout.BuildRuns(Stock, moved, 1, null, "Club", leftOut: PoleShootout.LeftOut(left));

        Assert.Equal(new[] { "q6", "q1", "q2", "q4" }, rebuilt.OrderBy(r => r.ShootoutQualifyingPlace).Select(r => r.AssignedTransponders.Single()));
        Assert.All(rebuilt, r => Assert.Equal(4, r.ShootoutRunCount));

        // And the rebuild is not built again at the next filed race.
        Assert.Equal(PoleShootout.RefreshResult.Unchanged, PoleShootout.Refresh(rebuilt, moved));
    }

    [Fact]
    public void BugHunt24_269_A_run_missing_with_nobody_left_out_is_still_built_again()
    {
        var runs = PoleShootout.BuildRuns(Stock, Field, 1, null, "Club");
        runs.RemoveAt(0);

        Assert.Equal(PoleShootout.RefreshResult.Rebuild, PoleShootout.Refresh(runs, Field));
    }

    [Fact]
    public void BugHunt24_269_Putting_the_run_back_counts_it_again_and_nobody_is_left_out()
    {
        var runs = PoleShootout.BuildRuns(Stock, Field, 1, null, "Club");
        var deleted = runs.Single(r => r.AssignedTransponders[0] == "q3");
        PoleShootout.LeaveOut(runs.Where(r => r != deleted).ToList(), new[] { deleted });

        PoleShootout.LeaveOut(runs, Array.Empty<RoundDefinition>());

        Assert.All(runs, r => Assert.Null(r.ShootoutLeftOut));
        Assert.All(runs, r => Assert.Equal(5, r.ShootoutRunCount));
        Assert.Equal(PoleShootout.RefreshResult.Unchanged, PoleShootout.Refresh(runs, Field));
    }

    // --- On the Program, as the director does it ------------------------------------

    [Fact]
    public void BugHunt24_269_A_deleted_run_stays_deleted_after_another_race_is_filed() => OnUiThread(() =>
    {
        using var program = BuildNight();

        var before = Shootout();
        var gone = before.Single(r => r.AssignedTransponders[0] == "D3");

        var answer = program.DeleteRounds(new[] { gone }, string.Empty);
        Assert.True(answer.Ok, answer.Message);
        Assert.Contains("left out of the Pole Shootout for Stock", answer.Message);

        // Another race filed, and the Program kept in step, as MainForm does.
        RunHeat("Other class heat", "Buggy", ("B1", 10));
        program.KeepFinalsFormatsInStep();
        program.KeepFinalsFormatsInStep();

        var after = Shootout();
        Assert.Equal(4, after.Count);
        Assert.Equal(before.Where(r => r.Id != gone.Id).Select(r => r.Id).OrderBy(i => i), after.Select(r => r.Id).OrderBy(i => i));
        Assert.DoesNotContain(after, r => r.AssignedTransponders.Contains("D3"));
    });

    [Fact]
    public void BugHunt24_269_The_shootout_still_finishes_and_sets_the_A_Main_front_with_the_runs_left() => OnUiThread(() =>
    {
        using var program = BuildNight();

        program.DeleteRounds(new[] { Shootout().Single(r => r.AssignedTransponders[0] == "D3") }, string.Empty);
        program.KeepFinalsFormatsInStep();

        var times = new Dictionary<string, long> { ["D1"] = 12_000, ["D2"] = 11_500, ["D4"] = 13_000, ["D5"] = 12_500 };

        foreach (var run in Shootout())
        {
            RecordRun(run, times[run.AssignedTransponders[0]]);
            program.KeepFinalsFormatsInStep();
        }

        var runs = PoleShootout.RunsOf(_rounds.ForClass(Stock), Stock);
        var standing = PoleShootout.Standings(runs, _history.Get, 1);

        Assert.True(standing.Complete);
        Assert.Equal(4, standing.Runs);
        Assert.Equal("D2", standing.Pole?.Transponder);

        var main = _rounds.ForClass(Stock).Single(r => r.Kind == RoundKind.Main);
        Assert.True(main.ShootoutGridSet);
        Assert.Equal(new[] { "D2", "D1", "D5", "D4" }, main.AssignedTransponders.Take(4));
    });

    [Fact]
    public void BugHunt24_269_A_real_change_in_qualifying_still_builds_the_runs_again() => OnUiThread(() =>
    {
        using var program = BuildNight();

        program.DeleteRounds(new[] { Shootout().Single(r => r.AssignedTransponders[0] == "D3") }, string.Empty);

        // A late heat puts D6 top of qualifying: D5 drops out of the five.
        AddDriver("D6", Stock);
        RunHeat("Q2", Stock, ("D6", 25));
        program.KeepFinalsFormatsInStep();

        var rebuilt = Shootout();
        var drivers = rebuilt.Select(r => r.AssignedTransponders.Single()).ToList();

        Assert.Equal(4, rebuilt.Count);
        Assert.Contains("D6", drivers);
        Assert.DoesNotContain("D3", drivers);
        Assert.DoesNotContain("D5", drivers);

        // Filed again: the rebuilt runs stay as they are.
        program.KeepFinalsFormatsInStep();
        Assert.Equal(rebuilt.Select(r => r.Id).OrderBy(i => i), Shootout().Select(r => r.Id).OrderBy(i => i));
    });

    [Fact]
    public void BugHunt24_269_An_undo_of_the_delete_brings_the_run_back() => OnUiThread(() =>
    {
        using var program = BuildNight();

        var before = Shootout().Select(r => r.Id).OrderBy(i => i).ToList();
        program.DeleteRounds(new[] { Shootout().Single(r => r.AssignedTransponders[0] == "D3") }, string.Empty);

        Assert.NotNull(_undo.Undo());

        var back = Shootout();
        Assert.Equal(before, back.Select(r => r.Id).OrderBy(i => i));
        Assert.All(back, r => Assert.Equal(5, r.ShootoutRunCount));
        Assert.All(back, r => Assert.True(r.ShootoutLeftOut is null or { Count: 0 }));

        program.KeepFinalsFormatsInStep();
        Assert.Equal(before, Shootout().Select(r => r.Id).OrderBy(i => i));
    });

    // --- Helpers ---------------------------------------------------------------------

    /// <summary>A night not counting check-ins, five Stock drivers qualified, the finals built with a five-car shootout.</summary>
    private RaceProgramControl BuildNight()
    {
        var day = new RaceEvent { Name = "Tonight" };
        _events.Upsert(day);
        _events.SetActive(day.Id);
        _checkIns.CurrentEventId = day.Id;
        _classes.Upsert(new RaceClass { Name = Stock, PoleShootout = true, PoleShootoutDrivers = 5 });
        _classes.Upsert(new RaceClass { Name = "Buggy" });

        var drivers = Enumerable.Range(1, 5).Select(i => $"D{i}").ToList();

        foreach (var t in drivers)
        {
            AddDriver(t, Stock);
        }

        AddDriver("B1", "Buggy");

        RunHeat("Q1", Stock, drivers.Select((t, i) => (t, 20 - i)).ToArray());

        var program = new RaceProgramControl(_classes, _profiles, _rounds, TrackLayoutStore.Load(), _history, _checkIns, new AppSettings(), _undo, _events);

        var done = program.SeedMainsFor(Stock, string.Empty);
        Assert.True(done.Ok, done.Title + ": " + done.Message);
        Assert.Equal(5, Shootout().Count);

        return program;
    }

    private void AddDriver(string transponder, string className) =>
        Assert.True(_profiles.TryAdd(new DriverProfile { Transponder = transponder, DriverName = transponder, ClassName = className }, out _));

    private List<RoundDefinition> Shootout() => _rounds.ForClass(Stock).Where(r => r.Kind == RoundKind.Shootout).ToList();

    private void RunHeat(string label, string className, params (string Transponder, int Laps)[] finishers)
    {
        var round = new RoundDefinition { ClassName = className, RoundLabel = label, Kind = RoundKind.Qualifier };
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

    private void RecordRun(RoundDefinition run, long timeMs)
    {
        var driver = run.AssignedTransponders.Single();

        var session = new SessionRecord
        {
            Label = run.RoundLabel,
            SessionType = RaceSessionType.Race,
            RoundId = run.Id,
            StartedAtUtc = DateTime.UtcNow,
            EndedAtUtc = DateTime.UtcNow,
            Results = new List<SessionResultEntry>
            {
                new() { Position = 1, Transponder = driver, DriverName = driver, Laps = 2, FastLapMs = timeMs, ElapsedMs = timeMs + 20_000 },
            },
        };

        Assert.True(_history.Add(session));
        Assert.True(_rounds.MarkRecorded(run.Id, session.Id));
    }

    private static void OnUiThread(Action body) =>
        StaThread.Run(() =>
        {
            body();
            return true;
        }, TimeSpan.FromMinutes(1), () => "bug hunt 24 #269, shootout run deleted by hand");
}
