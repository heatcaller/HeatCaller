namespace HeatCaller.Tests;

/// <summary>
/// Event Report scope (23 Oct 2026, bug hunt 10 #100): a race from a round
/// that belongs to no real race day - a main added by hand after Close Day,
/// or one since filed under the legacy holder - ran on the race day's date
/// but was left out of its report, and the dialog offers no other report of
/// that date. It now counts like a race with no round; another real race
/// day's rounds still stay out.
/// </summary>
public sealed class BugHunt10_EventReportBuilderTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    private readonly RaceEventStore _events = RaceEventStore.Load();
    private readonly RoundDefinitionStore _rounds = RoundDefinitionStore.Load();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_race_from_a_round_on_no_race_day_is_in_that_dates_race_day_report()
    {
        var saturday = DateTime.Today.AddHours(19);

        var dayA = Day("Race Day A");
        var dayB = Day("Race Day B");
        var main = Round("A-Main", dayA);
        var other = Round("Other main", dayB);
        var funFinal = Round("Fun final", null);

        var html = Report(dayA,
            Race(saturday, "A-Main race", main),
            Race(saturday.AddHours(1), "Other day race", other),
            Race(saturday.AddHours(2), "Fun final race", funFinal),
            Race(saturday.AddHours(3), "Quick race", null));

        Assert.Contains("A-Main race", html);
        Assert.Contains("Quick race", html);
        Assert.Contains("Fun final race", html);
        Assert.DoesNotContain("Other day race", html);
    }

    [Fact]
    public void A_race_from_a_legacy_holder_round_counts_as_on_no_race_day()
    {
        var saturday = DateTime.Today.AddHours(19);

        var dayA = Day("Race Day A");
        var main = Round("A-Main", dayA);
        var legacy = Round("Old main", _events.EnsureLegacy());

        var html = Report(dayA,
            Race(saturday, "A-Main race", main),
            Race(saturday.AddHours(1), "Legacy race", legacy));

        Assert.Contains("Legacy race", html);
    }

    [Fact]
    public void Without_an_event_store_a_round_off_the_race_day_stays_out_as_before()
    {
        var saturday = DateTime.Today.AddHours(19);

        var dayA = Day("Race Day A");
        var main = Round("A-Main", dayA);
        var funFinal = Round("Fun final", null);

        var html = Report(dayA, false,
            Race(saturday, "A-Main race", main),
            Race(saturday.AddHours(1), "Fun final race", funFinal));

        Assert.Contains("A-Main race", html);
        Assert.DoesNotContain("Fun final race", html);
    }

    // --- Helpers ----------------------------------------------------------------------------

    private RaceEvent Day(string name)
    {
        var day = new RaceEvent { Name = name };
        _events.Upsert(day);
        return day;
    }

    private RoundDefinition Round(string label, RaceEvent? day)
    {
        var round = new RoundDefinition { ClassName = "Stock", RoundLabel = label, Kind = RoundKind.Main };
        _rounds.Add(round);

        if (day is not null)
        {
            day.RoundIds.Add(round.Id);
            _events.Upsert(day);
        }

        return round;
    }

    private static SessionRecord Race(DateTime localStart, string label, RoundDefinition? round) => new()
    {
        Label = label,
        SessionType = RaceSessionType.Race,
        RoundId = round?.Id,
        StartedAtUtc = localStart.ToUniversalTime(),
        EndedAtUtc = localStart.AddMinutes(5).ToUniversalTime(),
        Results =
        {
            new SessionResultEntry
            {
                Transponder = "100", DriverName = "Alex", Position = 1, Laps = 3, ElapsedMs = 30_000,
                FastLapMs = 9_000, LapTimesMs = { 10_000, 9_000, 11_000 },
            },
        },
    };

    private string Report(RaceEvent day, params SessionRecord[] sessions) => Report(day, true, sessions);

    private string Report(RaceEvent day, bool passEvents, params SessionRecord[] sessions) =>
        EventReportBuilder.Build(
            new EventReportBuilder.Options(DateTime.Today, day.Name, IncludeQualifying: false, IncludeLapDetail: true,
                IncludeTech: false, IncludeRentals: false, Event: day),
            SessionHistoryStore.FromSessions(sessions),
            _rounds,
            RaceClassStore.Load(),
            ProfileStore.Load(),
            TrackLayoutStore.Load(),
            TechInspectionStore.Load(),
            RentalTransponderStore.Load(),
            handicaps: null,
            events: passEvents ? _events : null);
}
