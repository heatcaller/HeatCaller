namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15, B53: Build the Finals seeded from the whole qualifying
/// ranking, which holds everyone who ran a heat in the class today - on a
/// checked-in-only day that includes a driver who went home and was
/// un-checked-in. They took an A-main place from a driver who was there,
/// who was dropped into a one-driver B-main. The finals are now seeded only
/// from the class's roster, as Add Main's "from Qualifying" already was.
///
/// Windows-only: the laptop's Program tab is a real control.
/// </summary>
public sealed class BugHunt15SeedingTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    private readonly RaceEventStore _events = RaceEventStore.Load();
    private readonly RoundDefinitionStore _rounds = RoundDefinitionStore.Load();
    private readonly SessionHistoryStore _history = SessionHistoryStore.Load();
    private readonly ProfileStore _profiles = ProfileStore.Load();
    private readonly CheckInStore _checkIns = CheckInStore.Load();

    private const string Stock = "Stock";

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_driver_checked_out_after_qualifying_takes_no_main_place() => OnUiThread(() =>
    {
        var day = new RaceEvent { Name = "Tonight", CheckedInDriversOnly = true };
        _events.Upsert(day);
        _events.SetActive(day.Id);
        _checkIns.CurrentEventId = day.Id;

        // Nine qualifiers, D1 fastest; all checked in for the heat.
        var drivers = Enumerable.Range(1, 9).Select(i => $"D{i}").ToList();

        foreach (var t in drivers)
        {
            Assert.True(_profiles.TryAdd(new DriverProfile { Transponder = t, DriverName = t, ClassName = Stock }, out _));
            _checkIns.SetCheckedIn(t, true);
        }

        RunHeat("Q1", drivers.Select((t, i) => (t, 20 - i)).ToArray());

        // The third qualifier goes home and is checked out.
        _checkIns.SetCheckedIn("D3", false);

        using var program = ProgramPage();

        var plan = program.PlanSeedMains(Stock);
        Assert.True(plan.Ok, plan.Title + ": " + plan.Message);
        Assert.Contains("Put 8 qualified drivers", plan.Message);
        Assert.Contains("1 main", plan.Message);

        var done = program.SeedMainsFor(Stock, string.Empty);
        Assert.True(done.Ok, done.Title + ": " + done.Message);

        var main = Assert.Single(_rounds.ForClass(Stock), r => r.Kind == RoundKind.Main);
        Assert.DoesNotContain("D3", main.AssignedTransponders);
        Assert.Equal(drivers.Where(t => t != "D3").OrderBy(t => t), main.AssignedTransponders.OrderBy(t => t));
    });

    [Fact]
    public void With_every_qualifier_checked_out_there_is_nothing_to_seed_from() => OnUiThread(() =>
    {
        var day = new RaceEvent { Name = "Tonight", CheckedInDriversOnly = true };
        _events.Upsert(day);
        _events.SetActive(day.Id);
        _checkIns.CurrentEventId = day.Id;

        Assert.True(_profiles.TryAdd(new DriverProfile { Transponder = "D1", DriverName = "D1", ClassName = Stock }, out _));
        RunHeat("Q1", ("D1", 10));

        using var program = ProgramPage();

        var plan = program.PlanSeedMains(Stock);
        Assert.False(plan.Ok);
        Assert.Equal("Nothing to build the finals from", plan.Title);
    });

    // --- Helpers ----------------------------------------------------------------------------

    private RoundDefinition RunHeat(string label, params (string Transponder, int Laps)[] finishers)
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
        return round;
    }

    private RaceProgramControl ProgramPage() => new(
        RaceClassStore.Load(),
        _profiles,
        _rounds,
        TrackLayoutStore.Load(),
        _history,
        _checkIns,
        new AppSettings(),
        new UndoService(),
        _events);

    private static void OnUiThread(Action body) =>
        StaThread.Run(() =>
        {
            body();
            return true;
        }, TimeSpan.FromMinutes(1), () => "bug hunt 15, seeding");
}
