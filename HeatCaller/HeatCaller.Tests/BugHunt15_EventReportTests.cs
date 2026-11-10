using System.Globalization;

namespace HeatCaller.Tests;

/// <summary>
/// 9 Nov 2026, Bug Hunt 15.
///
/// B35: the Event Report wrote its times in the PC's culture, so on a
/// decimal-comma PC the lap-by-lap list read "12,345, 13,456" - the comma in
/// a lap and the comma between laps the same character.
///
/// B52: handicaps were applied from the live store whenever a past race was
/// shown, so the credits Suggest Handicaps set after a meeting re-ranked
/// every old handicapped race and changed its class winner. The credits and
/// the class's mode are now kept with the race when it is filed.
/// </summary>
public sealed class BugHunt15_EventReportTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    private readonly RoundDefinitionStore _rounds = RoundDefinitionStore.Load();
    private readonly HandicapStore _handicaps = HandicapStore.Load();

    public void Dispose() => _data.Dispose();

    // --- B35 --------------------------------------------------------------------------------

    [Fact]
    public void Lap_by_lap_list_uses_a_decimal_point_on_a_comma_locale_pc()
    {
        var session = Race(null);
        session.Results[0].LapTimesMs = new List<long> { 12_345, 13_456, 9_876 };
        session.Results[0].FastLapMs = 9_876;
        session.Results[0].ElapsedMs = 35_677;

        var html = WithCulture("de-DE", () => Report(session));

        Assert.Contains("12.345, 13.456, 9.876", html);
        Assert.DoesNotContain("12,345", html);

        // The other times in the report too.
        Assert.Contains("9.876s", html);
        Assert.Contains("35.677s", html);
        Assert.DoesNotContain("9,876s", html);
    }

    [Fact]
    public void Handicap_credit_is_written_with_a_decimal_point_on_a_comma_locale_pc()
    {
        var round = Round();
        _handicaps.SetMode("Stock", HandicapMode.TimeCredit);
        Credit("B", 31.5);

        var session = Race(round);
        session.Handicap = HandicapCalculator.Snapshot(session.Results, _handicaps, "Stock");

        var html = WithCulture("fr-FR", () => Report(session));

        Assert.Contains("(-31.5s)", html);
    }

    // --- B52 --------------------------------------------------------------------------------

    [Fact]
    public void A_past_race_keeps_its_handicap_winner_after_the_credits_change()
    {
        var round = Round();
        _handicaps.SetMode("Stock", HandicapMode.TimeCredit);
        Credit("B", 31);

        // As filed: B's 297 s over 27 laps is 330 s over A's 30, less 31 s =
        // 299 s, so B (Pat) wins on handicap.
        var session = Race(round);
        session.Handicap = HandicapCalculator.Snapshot(session.Results, _handicaps, "Stock");

        // After the meeting Suggest Handicaps cuts Pat's credit.
        Credit("B", 10);

        var html = Report(session);

        Assert.Contains("<tr class=\"win\"><td class=\"num\">2</td><td>Pat</td>", html);
        Assert.DoesNotContain("<tr class=\"win\"><td class=\"num\">1</td>", html);
    }

    [Fact]
    public void A_past_race_keeps_its_handicap_column_after_the_class_mode_is_turned_off()
    {
        var round = Round();
        _handicaps.SetMode("Stock", HandicapMode.TimeCredit);
        Credit("B", 31);

        var session = Race(round);
        session.Handicap = HandicapCalculator.Snapshot(session.Results, _handicaps, "Stock");

        _handicaps.SetMode("Stock", HandicapMode.None);

        var (mode, adjusted) = HandicapCalculator.ForSession(session, "Stock", _handicaps);

        Assert.Equal(HandicapMode.TimeCredit, mode);
        Assert.Equal("B", adjusted!.Single(a => a.AdjustedPosition == 1).Result.Transponder);
        Assert.Contains("Handicap pos", Report(session));
    }

    [Fact]
    public void A_race_saved_before_the_snapshot_still_reads_todays_handicaps()
    {
        var round = Round();
        _handicaps.SetMode("Stock", HandicapMode.TimeCredit);
        Credit("B", 31);

        var (mode, adjusted) = HandicapCalculator.ForSession(Race(round), "Stock", _handicaps);

        Assert.Equal(HandicapMode.TimeCredit, mode);
        Assert.Equal("B", adjusted!.Single(a => a.AdjustedPosition == 1).Result.Transponder);
    }

    [Fact]
    public void A_snapshot_for_another_class_is_not_used()
    {
        _handicaps.SetMode("Stock", HandicapMode.None);

        var session = Race(null);
        session.Handicap = new SessionHandicap
        {
            ClassName = "Mod",
            Mode = HandicapMode.TimeCredit,
            Credits = { new SessionHandicapCredit { Transponder = "B", TimeCreditSeconds = 31 } },
        };

        var (mode, adjusted) = HandicapCalculator.ForSession(session, "Stock", _handicaps);

        Assert.Equal(HandicapMode.None, mode);
        Assert.Null(adjusted);
    }

    [Fact]
    public void The_snapshot_survives_a_save_and_load()
    {
        _handicaps.SetMode("Stock", HandicapMode.LapCredit);
        _handicaps.Upsert(new DriverHandicap { Transponder = "b", LapCredit = 2 });

        var session = Race(Round());
        session.Handicap = HandicapCalculator.Snapshot(session.Results, _handicaps, "Stock");

        var json = System.Text.Json.JsonSerializer.Serialize(session);
        var back = System.Text.Json.JsonSerializer.Deserialize<SessionRecord>(json)!;

        Assert.Equal(HandicapMode.LapCredit, back.Handicap!.Mode);
        Assert.Equal("Stock", back.Handicap.ClassName);
        Assert.Equal(2, back.Handicap.For("B")!.LapCredit);
        Assert.Null(back.Handicap.For("A"));
    }

    // --- Helpers ----------------------------------------------------------------------------

    private void Credit(string transponder, double seconds) =>
        _handicaps.Upsert(new DriverHandicap { Transponder = transponder, TimeCreditSeconds = seconds });

    private RoundDefinition Round()
    {
        var round = new RoundDefinition { ClassName = "Stock", RoundLabel = "A-Main", Kind = RoundKind.Main };
        _rounds.Add(round);
        return round;
    }

    private static SessionRecord Race(RoundDefinition? round)
    {
        var start = DateTime.Today.AddHours(19);

        return new SessionRecord
        {
            Label = "Stock A-Main",
            SessionType = RaceSessionType.Race,
            RoundId = round?.Id,
            StartedAtUtc = start.ToUniversalTime(),
            EndedAtUtc = start.AddMinutes(5).ToUniversalTime(),
            Results =
            {
                new SessionResultEntry { Position = 1, Transponder = "A", DriverName = "Alex", Laps = 30, ElapsedMs = 300_000, FastLapMs = 9_500 },
                new SessionResultEntry { Position = 2, Transponder = "B", DriverName = "Pat", Laps = 27, ElapsedMs = 297_000, FastLapMs = 10_500 },
            },
        };
    }

    private string Report(SessionRecord session) =>
        EventReportBuilder.Build(
            new EventReportBuilder.Options(DateTime.Today, "Club night", IncludeQualifying: false, IncludeLapDetail: true,
                IncludeTech: false, IncludeRentals: false),
            SessionHistoryStore.FromSessions(new[] { session }),
            _rounds,
            RaceClassStore.Load(),
            ProfileStore.Load(),
            TrackLayoutStore.Load(),
            TechInspectionStore.Load(),
            RentalTransponderStore.Load(),
            handicaps: _handicaps);

    private static string WithCulture(string name, Func<string> build)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(name);

        try
        {
            return build();
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
}
