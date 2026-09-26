namespace HeatCaller.Tests;

/// <summary>
/// Qualifying with no race day open (25 Sep audit). The filter used to be
/// null with no day open, and to the calculator null means "no filter": after
/// End Race Day the standings ranked every heat the class had ever run as
/// tonight's order, and Seed Mains built an A-main from drivers who were not
/// at the track.
///
/// The rule now: qualifying counts the open race day's heats, and nothing
/// when no race day is open - and the Standings and Seed Mains answers say
/// why rather than showing an empty table.
/// </summary>
public sealed class NoRaceDayTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    private readonly RaceEventStore _events = RaceEventStore.Load();
    private readonly RoundDefinitionStore _rounds = RoundDefinitionStore.Load();
    private readonly SessionHistoryStore _history = SessionHistoryStore.Load();
    private readonly ProfileStore _profiles = ProfileStore.Load();

    private const string Stock = "Stock";

    public void Dispose() => _data.Dispose();

    // --- What qualifying counts --------------------------------------------------------

    [Fact]
    public void After_End_Race_Day_the_standings_are_not_the_club_s_whole_history()
    {
        var lastMonth = OpenDay("Last month");
        RunHeat("Q1", ("FAST-GONE", 14));
        _events.Close(lastMonth.Id);

        var tonight = OpenDay("Tonight");
        RunHeat("Q1", ("HERE", 10));
        _events.Close(tonight.Id);

        Assert.Empty(_events.QualifyingScope());
        Assert.Empty(Standings(_events.QualifyingScope()));
    }

    [Fact]
    public void A_club_that_has_never_opened_a_race_day_counts_nothing_rather_than_everything()
    {
        RunHeat("Q1", ("A", 10)); // filed against no day at all

        Assert.Empty(_events.QualifyingScope());
        Assert.Empty(Standings(_events.QualifyingScope()));
    }

    [Fact]
    public void With_a_race_day_open_only_its_heats_count()
    {
        var lastMonth = OpenDay("Last month");
        RunHeat("Q1", ("FAST-GONE", 14));
        _events.Close(lastMonth.Id);

        OpenDay("Tonight");
        var q1 = RunHeat("Q1", ("HERE", 10));

        Assert.Equal(new[] { q1.Id }, _events.QualifyingScope());
        Assert.Equal(new[] { "HERE" }, Standings(_events.QualifyingScope()).Select(e => e.Transponder));
    }

    [Fact]
    public void A_race_day_with_no_heats_yet_counts_nothing()
    {
        var lastMonth = OpenDay("Last month");
        RunHeat("Q1", ("FAST-GONE", 14));
        _events.Close(lastMonth.Id);

        OpenDay("Tonight");

        Assert.Empty(Standings(_events.QualifyingScope()));
    }

    // --- What the Program page answers --------------------------------------------------

    [Fact]
    public void Seed_Mains_with_no_race_day_open_says_so_rather_than_seeding_from_old_heats()
    {
        var tonight = OpenDay("Tonight");
        RunHeat("Q1", ("A", 10), ("B", 9));
        _events.Close(tonight.Id);

        using var program = ProgramPage();
        var answer = program.PlanSeedMains(Stock);

        Assert.False(answer.Ok);
        Assert.Equal(RaceProgramControl.NoRaceDayTitle, answer.Title);
        Assert.Empty(program.QualifyingRoundsCounted());
    }

    [Fact]
    public void Seed_Mains_on_tonight_s_open_race_day_still_goes_ahead()
    {
        // The same page and the same heats with the day still open - so the
        // refusal above is about the day, not something else in the setup.
        OpenDay("Tonight");
        var q1 = RunHeat("Q1", ("A", 10), ("B", 9));

        using var program = ProgramPage();
        var answer = program.PlanSeedMains(Stock);

        Assert.True(answer.Ok, answer.Title + ": " + answer.Message);
        Assert.Equal(new[] { q1.Id }, program.QualifyingRoundsCounted());
    }

    // --- Helpers ----------------------------------------------------------------------------

    private RaceEvent OpenDay(string name)
    {
        var day = new RaceEvent { Name = name };
        _events.Upsert(day);
        _events.SetActive(day.Id);
        return day;
    }

    /// <summary>A qualifying heat as the app leaves it once raced: on the open day (if any), its result saved and linked.</summary>
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

    private List<QualifyingCalculator.Entry> Standings(IReadOnlyCollection<string> scope) =>
        QualifyingCalculator.Compute(Stock, _rounds, _history, _profiles, null, scope);

    /// <summary>The Program page over these stores - the same objects the app hands it.</summary>
    private RaceProgramControl ProgramPage() => new(
        RaceClassStore.Load(),
        _profiles,
        _rounds,
        TrackLayoutStore.Load(),
        _history,
        CheckInStore.Load(),
        new AppSettings(),
        new UndoService(),
        _events);
}
