namespace HeatCaller.Tests;

/// <summary>
/// Mid-pack race lines (11 Nov 2026, build plan 7.4): RaceLines' personal
/// after-race lines - held off, quicker than earlier tonight, best class
/// finish this year - and where they go: each race of the night document
/// and the night email.
/// </summary>
public sealed class RaceLinesTests
{
    private const string Me = "3012345";
    private const string Sam = "3099999";
    private const string Stock = "stock-round";
    private const string Mod = "mod-round";

    private static readonly DateTime Tonight = new(2026, 11, 11, 19, 0, 0, DateTimeKind.Utc);

    private static readonly Func<string, RoundDefinition?> Rounds = id => id switch
    {
        Stock => new RoundDefinition { Id = Stock, ClassName = "Stock", RoundLabel = "Heat" },
        Mod => new RoundDefinition { Id = Mod, ClassName = "Mod", RoundLabel = "Heat" },
        _ => null,
    };

    private static string ClassOf(SessionRecord s) =>
        s.RoundId is { } id && Rounds(id) is { } round ? round.ClassName : string.Empty;

    private static string LabelOf(SessionRecord s) => s.Label;

    private static SessionResultEntry Row(string who, string name, int pos, long best, params long[] laps) => new()
    {
        Transponder = who,
        DriverName = name,
        Position = pos,
        Laps = Math.Max(0, laps.Length - 1),
        LapTimesMs = laps.ToList(),
        FastLapMs = best,
    };

    private static SessionRecord Race(string label, DateTime at, string? round, params SessionResultEntry[] rows) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        SessionType = RaceSessionType.Race,
        Label = label,
        RoundId = round,
        TrackLayoutName = "Club Oval",
        StartedAtUtc = at,
        EndedAtUtc = at.AddMinutes(5),
        Results = rows.ToList(),
    };

    private static IReadOnlyList<string> For(SessionRecord race, IEnumerable<SessionRecord>? tonight = null, IEnumerable<SessionRecord>? history = null) =>
        RaceLines.For(Me, race, tonight ?? new[] { race }, history, ClassOf, LabelOf);

    // ---- held off

    [Fact]
    public void Held_off_the_car_behind_for_every_lap_it_was_within_a_second_to_the_flag()
    {
        // Me: crossings 2.0, 12.0, 22.0, 32.0, 42.0, 52.0. Sam: 2.2, 13.5,
        // 22.6, 32.4, 42.9, 52.3 - within a second on the last four laps.
        var race = Race("Heat 1", Tonight, Stock,
            Row(Me, "Asher Kent", 4, 10000, 2000, 10000, 10000, 10000, 10000, 10000),
            Row(Sam, "Sam Parker", 5, 9100, 2200, 11300, 9100, 9800, 10500, 9400));

        Assert.Equal(new[] { "Held off Sam for 4 laps" }, For(race));
    }

    [Fact]
    public void Nothing_held_off_under_three_laps_or_with_a_car_well_back()
    {
        var two = Race("Heat 1", Tonight, Stock,
            Row(Me, "Asher Kent", 4, 10000, 2000, 10000, 10000, 10000),
            Row(Sam, "Sam Parker", 5, 8900, 2100, 11500, 8900, 9900));
        Assert.Empty(For(two));

        var far = Race("Heat 1", Tonight, Stock,
            Row(Me, "Asher Kent", 4, 10000, 2000, 10000, 10000, 10000, 10000),
            Row(Sam, "Sam Parker", 5, 10200, 2500, 11500, 11000, 10200, 10300));
        Assert.Empty(For(far));
    }

    [Fact]
    public void Never_held_off_a_ghost_or_nobody_and_a_shared_first_name_is_the_full_name()
    {
        var ghost = Race("Heat 1", Tonight, Stock,
            Row(Me, "Asher Kent", 1, 10000, 2000, 10000, 10000, 10000, 10000),
            Row(Ghosts.Prefix + "x", "Ghost of last week", 2, 10000, 2300, 10000, 10000, 10000, 10000));
        Assert.Empty(For(ghost));

        var twoSams = Race("Heat 1", Tonight, Stock,
            Row(Me, "Asher Kent", 1, 10000, 2000, 10000, 10000, 10000, 10000),
            Row(Sam, "Sam Parker", 2, 10000, 2300, 10000, 10000, 10000, 10000),
            Row("3011111", "Sam Lee", 3, 10500, 2600, 10500, 10500, 10500, 10500));
        Assert.Equal(new[] { "Held off Sam Parker for 4 laps" }, For(twoSams));
    }

    [Fact]
    public void Never_held_off_after_a_dnf()
    {
        var race = Race("Heat 1", Tonight, Stock,
            Row(Me, "Asher Kent", 4, 10000, 2000, 10000, 10000, 10000, 10000),
            Row(Sam, "Sam Parker", 5, 10000, 2300, 10000, 10000, 10000, 10000));
        race.Results[0].Outcome = ResultEditor.Dnf;

        Assert.Empty(For(race));
    }

    // ---- quicker than earlier tonight

    [Fact]
    public void Best_lap_quicker_than_their_first_race_tonight_in_the_class()
    {
        var heat1 = Race("Heat 1", Tonight, Stock, Row(Me, "Asher Kent", 3, 9420, 2000, 9420));
        var heat2 = Race("Heat 2", Tonight.AddHours(1), Stock, Row(Me, "Asher Kent", 3, 9300, 2000, 9300));
        var main = Race("A Main", Tonight.AddHours(2), Stock, Row(Me, "Asher Kent", 3, 9110, 2000, 9110));
        var tonight = new[] { heat1, heat2, main };

        Assert.Empty(For(heat1, tonight));
        Assert.Equal(new[] { "Best lap 0.12s quicker than your Heat 1" }, For(heat2, tonight));
        Assert.Equal(new[] { "Best lap 0.31s quicker than your Heat 1" }, For(main, tonight));
    }

    [Fact]
    public void Nothing_for_a_slower_lap_a_wobble_or_another_class()
    {
        var heat1 = Race("Heat 1", Tonight, Stock, Row(Me, "Asher Kent", 3, 9420, 2000, 9420));
        var slower = Race("Heat 2", Tonight.AddHours(1), Stock, Row(Me, "Asher Kent", 3, 9500, 2000, 9500));
        var wobble = Race("Heat 3", Tonight.AddHours(2), Stock, Row(Me, "Asher Kent", 3, 9390, 2000, 9390));
        var mod = Race("Mod Heat", Tonight.AddHours(3), Mod, Row(Me, "Asher Kent", 3, 8000, 2000, 8000));
        var tonight = new[] { heat1, slower, wobble, mod };

        Assert.Empty(For(slower, tonight));
        Assert.Empty(For(wobble, tonight));
        Assert.Empty(For(mod, tonight));
    }

    // ---- best finish this year

    [Fact]
    public void Best_class_finish_this_year_once_there_are_three_to_beat()
    {
        var earlier = new[]
        {
            Race("Heat 1", Tonight.AddDays(-21), Stock, Row(Me, "Asher Kent", 6, 9500, 2000, 9500)),
            Race("Heat 1", Tonight.AddDays(-14), Stock, Row(Me, "Asher Kent", 5, 9500, 2000, 9500)),
            Race("Heat 1", Tonight.AddDays(-7), Stock, Row(Me, "Asher Kent", 7, 9500, 2000, 9500)),
            // Another class and last year do not count.
            Race("Mod Heat", Tonight.AddDays(-7), Mod, Row(Me, "Asher Kent", 1, 8000, 2000, 8000)),
            Race("Heat 1", Tonight.AddYears(-1), Stock, Row(Me, "Asher Kent", 1, 9500, 2000, 9500)),
        };
        var fourth = Race("A Main", Tonight, Stock, Row(Me, "Asher Kent", 4, 9500, 2000, 9500));

        Assert.Equal(new[] { "Best Stock finish this year" }, For(fourth, history: earlier));
        Assert.Empty(For(fourth, history: earlier.Take(2)));

        var level = Race("A Main", Tonight, Stock, Row(Me, "Asher Kent", 5, 9500, 2000, 9500));
        Assert.Empty(For(level, history: earlier));
    }

    [Fact]
    public void A_win_better_than_every_earlier_race_is_their_first_win_this_year()
    {
        var earlier = Enumerable.Range(1, 3)
            .Select(i => Race("Heat 1", Tonight.AddDays(-7 * i), Stock, Row(Me, "Asher Kent", 2, 9500, 2000, 9500)))
            .ToList();
        var win = Race("A Main", Tonight, Stock, Row(Me, "Asher Kent", 1, 9500, 2000, 9500));

        Assert.Equal(new[] { "First Stock win this year" }, For(win, history: earlier));
    }

    [Fact]
    public void The_race_itself_in_the_history_is_not_one_to_beat()
    {
        var earlier = Enumerable.Range(1, 3)
            .Select(i => Race("Heat 1", Tonight.AddDays(-7 * i), Stock, Row(Me, "Asher Kent", 6, 9500, 2000, 9500)))
            .ToList();
        var race = Race("A Main", Tonight, Stock, Row(Me, "Asher Kent", 4, 9500, 2000, 9500));

        Assert.Equal(new[] { "Best Stock finish this year" }, For(race, history: earlier.Append(race)));
    }

    // ---- where they go

    [Fact]
    public void Lines_go_on_the_race_in_the_night_document_and_never_on_a_demo()
    {
        var earlier = Enumerable.Range(1, 3)
            .Select(i => Race("Heat 1", Tonight.AddDays(-7 * i), Stock, Row(Me, "Asher Kent", 6, 9500, 2000, 9500)))
            .ToList();
        var main = Race("A Main", Tonight, Stock,
            Row(Me, "Asher Kent", 4, 10000, 2000, 10000, 10000, 10000, 10000),
            Row(Sam, "Sam Parker", 5, 10000, 2300, 10000, 10000, 10000, 10000));

        var race = Assert.Single(DriverNightExtras.Races(Me, new[] { main }, Rounds, history: earlier));
        Assert.Equal(new[] { "Best Stock finish this year", "Held off Sam for 4 laps" }, race.Lines);

        var json = System.Text.Json.JsonSerializer.Serialize(race);
        Assert.Contains("\"lines\":[\"Best Stock finish this year\",\"Held off Sam for 4 laps\"]", json);

        // Nothing to say: no "lines" at all.
        var quiet = Assert.Single(DriverNightExtras.Races(Me, new[] { Race("Heat 1", Tonight, Stock, Row(Me, "Asher Kent", 1, 9000, 2000, 9000)) }, Rounds));
        Assert.Null(quiet.Lines);
        Assert.DoesNotContain("\"lines\"", System.Text.Json.JsonSerializer.Serialize(quiet));

        // A demo run carries none, whatever happened in it.
        main.IsDemo = true;
        var demo = Assert.Single(DriverNightExtras.Races(Me, null, Rounds, runs: new[] { main }, history: earlier));
        Assert.Null(demo.Lines);
    }

    [Fact]
    public void The_email_prints_them_under_race_by_race_with_each_races_name()
    {
        var races = new[]
        {
            new NightRace { Label = "Heat 1" },
            new NightRace { Label = "A Main", Lines = new List<string> { "Held off Sam for 4 laps", "Best Stock finish this year" } },
        };
        var lines = RaceLines.ForEmail(races);
        Assert.Equal(new[] { "A Main: Held off Sam for 4 laps", "A Main: Best Stock finish this year" }, lines);

        var body = NightEmail.Build("Asher", "Acme RC", Tonight, null, new[] { "You raced twice." }, null, null, raceLines: lines);
        var nl = Environment.NewLine;
        Assert.Contains($"RACE BY RACE{nl}A Main: Held off Sam for 4 laps{nl}A Main: Best Stock finish this year{nl}", body);
        Assert.True(body.IndexOf("YOUR RACES", StringComparison.Ordinal) < body.IndexOf("RACE BY RACE", StringComparison.Ordinal));

        // None, and the email is what it always was.
        Assert.DoesNotContain("RACE BY RACE", NightEmail.Build("Asher", "Acme RC", Tonight, null, new[] { "You raced twice." }, null, null));
    }
}
