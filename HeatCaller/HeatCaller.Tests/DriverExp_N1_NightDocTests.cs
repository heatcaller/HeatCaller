using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Driver experience N1 (22 Oct 2026): the night document's extras -
/// races one by one with laps and penalty reasons, the 64 KB fallback,
/// `reach`, `progress`, `improve` and `firstNight` - and the promise the
/// existing DriverNightTests rest on: without an extra, a night is byte for
/// byte what it was.
/// </summary>
public sealed class DriverExp_N1_NightDocTests
{
    private const string Sam = "3012345";
    private const string Kim = "3099999";
    private const string Ghost = "GHOST-1";

    private static readonly DateTime Tonight = new(2026, 10, 22, 21, 0, 0, DateTimeKind.Local);
    private static readonly DateTime At = new(2026, 10, 22, 21, 30, 0, DateTimeKind.Utc);

    // --- The original document, unchanged ---------------------------------

    [Fact]
    public void Without_extras_the_document_is_the_original_byte_for_byte()
    {
        var facts = Facts("Sam raced Heat 2 and finished second of six.");

        Assert.Equal(
            DriverNight.Document("Sam Parker", Tonight, "Club Night", facts, At),
            DriverNight.Document(new NightParts("Sam Parker", Tonight, "Club Night", facts, At)));

        var coach = new[] { "Your laps were within 0.2 s of each other." };

        Assert.Equal(
            DriverNight.Document("Sam Parker", Tonight, "Club Night", facts, At, coach),
            DriverNight.Document(new NightParts("Sam Parker", Tonight, "Club Night", facts, At) { Coach = coach }));

        // Empty lists and a false flag are not extras.
        Assert.Equal(
            DriverNight.Document("Sam Parker", Tonight, null, facts, At),
            DriverNight.Document(new NightParts("Sam Parker", Tonight, null, facts, At)
            {
                Races = Array.Empty<NightRace>(),
                Reach = Array.Empty<NightReach>(),
                Improve = "  ",
                FirstNight = false,
            }));
    }

    [Fact]
    public void With_extras_the_rules_for_no_night_still_hold()
    {
        var extras = new NightParts(null, Tonight, null, Facts("Raced."), At) { FirstNight = true };

        Assert.Null(DriverNight.Document(extras));
        Assert.Null(DriverNight.Document(extras with { DriverName = "Sam Parker", Facts = Facts() }));
        Assert.NotNull(DriverNight.Document(extras with { DriverName = "Sam Parker" }));
    }

    [Fact]
    public void The_extras_sit_beside_the_lines_and_never_inside_them()
    {
        const string said = "Sam raced Heat 2 and finished second of six.";
        var night = Read(DriverNight.Document(new NightParts("Sam Parker", Tonight, null, Facts(said), At)
        {
            Improve = "Your middle lap is 0.40 s quicker than in your first race tonight (Heat 1)",
            FirstNight = true,
        }));

        Assert.Equal(new[] { said }, night.GetProperty("lines").EnumerateArray().Select(l => l.GetString()));
        Assert.Equal("Your middle lap is 0.40 s quicker than in your first race tonight (Heat 1)", night.GetProperty("improve").GetString());
        Assert.True(night.GetProperty("firstNight").GetBoolean());
        Assert.Equal("Sam Parker", night.GetProperty("driver").GetString());
        Assert.False(night.TryGetProperty("coach", out _));
        Assert.False(night.TryGetProperty("races", out _));
        Assert.False(night.TryGetProperty("progress", out _));
        Assert.False(night.TryGetProperty("reach", out _));
        Assert.Equal(At, DateTime.Parse(night.GetProperty("at").GetString()!,
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind));
    }

    // --- races[] ------------------------------------------------------------

    [Fact]
    public void Each_race_carries_its_place_field_grid_laps_and_best()
    {
        var heat = Race("Heat 2", Tonight.AddHours(-1),
            Row(Sam, 2, start: 3, laps: new long[] { 9000, 8500, 8400 }, best: 8400),
            Row(Kim, 1, start: 1),
            Row(Ghost, 3, start: 2));

        var race = Assert.Single(DriverNightExtras.Races(Sam, new[] { heat }, null));

        Assert.Equal(heat.Id, race.Sid);
        Assert.Equal("Heat 2", race.Label);
        Assert.Equal(2, race.Pos);
        Assert.Equal(2, race.Of); // the ghost is a recording, not a driver
        Assert.Equal(2, race.Start); // slot 3, behind the ghost: 2nd of the real cars (bug hunt 11)
        Assert.Equal(3, race.Laps);
        Assert.Equal(8400, race.BestMs);
        Assert.Equal(new long[] { 8500, 8400 }, race.LapsMs!);
        Assert.Null(race.Pen);
        Assert.Null(race.Out);
    }

    [Fact]
    public void Empty_race_fields_are_left_out_of_the_document()
    {
        var heat = Race("Heat 1", Tonight.AddHours(-1), Row(Sam, 1, start: 0, laps: Array.Empty<long>()));

        var night = Read(DriverNight.Document(new NightParts("Sam Parker", Tonight, null, Facts("Raced."), At)
        {
            Races = DriverNightExtras.Races(Sam, new[] { heat }, null),
        }));

        var race = night.GetProperty("races")[0];

        Assert.False(race.TryGetProperty("start", out _));
        Assert.False(race.TryGetProperty("lapsMs", out _));
        Assert.False(race.TryGetProperty("pen", out _));
        Assert.False(race.TryGetProperty("out", out _));
        Assert.Equal(1, race.GetProperty("pos").GetInt32());
        Assert.Equal(1, race.GetProperty("of").GetInt32());
    }

    [Fact]
    public void A_penalty_reads_as_its_reasons_in_the_drivers_own_night()
    {
        var row = Row(Sam, 5);
        row.JumpStarted = true;
        row.RacePenaltyMs = 5000;

        var heat = Race("Heat 3", Tonight.AddHours(-1), row);
        var race = Assert.Single(DriverNightExtras.Races(Sam, new[] { heat }, null));

        Assert.Equal(new[] { "+5 s - jump start" }, race.Pen!);
        Assert.Equal(PenaltyWords.For(row, heat).ToArray(), race.Pen!.ToArray());

        var night = Read(DriverNight.Document(new NightParts("Sam Parker", Tonight, null, Facts("Raced."), At) { Races = new[] { race } }));

        Assert.Equal("+5 s - jump start", night.GetProperty("races")[0].GetProperty("pen")[0].GetString());
    }

    [Fact]
    public void A_disqualification_says_so()
    {
        var row = Row(Sam, 6);
        row.Outcome = "DQ";

        var race = Assert.Single(DriverNightExtras.Races(Sam, new[] { Race("Heat 1", Tonight.AddHours(-1), row) }, null));

        Assert.Equal("DQ", race.Out);
    }

    [Fact]
    public void Races_run_oldest_first_and_only_the_drivers_own()
    {
        var later = Race("Heat 2", Tonight.AddMinutes(-20), Row(Sam, 1));
        var earlier = Race("Heat 1", Tonight.AddHours(-2), Row(Sam, 3));
        var notTheirs = Race("Heat 1b", Tonight.AddHours(-1), Row(Kim, 1));

        var demo = Race("[DEMO] Heat", Tonight.AddHours(-1), Row(Sam, 1));
        demo.IsDemo = true;

        var practice = Race("Practice", Tonight.AddHours(-3), Row(Sam, 1));
        practice.SessionType = RaceSessionType.Practice;

        var races = DriverNightExtras.Races(Sam, new[] { later, notTheirs, earlier, demo, practice }, null);

        Assert.Equal(new[] { "Heat 1", "Heat 2" }, races.Select(r => r.Label));
        Assert.Same(later, DriverNightExtras.LatestRace(Sam, new[] { later, earlier, notTheirs }));
        Assert.Null(DriverNightExtras.LatestRace("nobody", new[] { later }));
    }

    [Fact]
    public void A_race_saved_without_a_label_takes_its_rounds()
    {
        var heat = Race(string.Empty, Tonight.AddHours(-1), Row(Sam, 1));
        heat.RoundId = "r1";
        var round = new RoundDefinition { Id = "r1", RoundLabel = "A-Main", ClassName = "Stock" };

        Assert.Equal("A-Main", DriverNightExtras.Races(Sam, new[] { heat }, id => id == "r1" ? round : null)[0].Label);
        Assert.Equal("Race", DriverNightExtras.Races(Sam, new[] { heat }, _ => null)[0].Label);
    }

    [Fact]
    public void A_lap_chart_holds_at_most_two_hundred_laps()
    {
        var laps = Enumerable.Range(0, 260).Select(i => 8000L + i).ToArray();
        var race = Assert.Single(DriverNightExtras.Races(Sam, new[] { Race("Enduro", Tonight.AddHours(-1), Row(Sam, 1, laps: laps)) }, null));

        Assert.Equal(DriverNightExtras.MostLaps, race.LapsMs!.Count);
        Assert.Equal(8001L, race.LapsMs![0]);
    }

    [Fact]
    public void Over_the_relays_limit_the_lap_charts_go_first()
    {
        // Sixty 200-lap runs: far more than a night, and well over 64 KB
        // with every lap in it.
        var laps = Enumerable.Range(0, 200).Select(i => 1_234_567L + i).ToArray();
        var races = Enumerable.Range(0, 60)
            .Select(i => Race($"Run {i}", Tonight.AddMinutes(-300 + i), Row(Sam, 1, laps: laps)))
            .ToList();

        var document = DriverNight.Document(new NightParts("Sam Parker", Tonight, null, Facts("Raced."), At)
        {
            Races = DriverNightExtras.Races(Sam, races, null),
        });

        Assert.NotNull(document);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(document!) <= DriverNight.MostDocumentBytes);

        var night = Read(document);

        Assert.Equal(60, night.GetProperty("races").GetArrayLength());
        Assert.All(night.GetProperty("races").EnumerateArray(), r => Assert.False(r.TryGetProperty("lapsMs", out _)));
    }

    [Fact]
    public void A_night_still_too_big_without_its_charts_is_dropped()
    {
        var huge = new string('x', DriverNight.MostDocumentBytes + 1);

        Assert.Null(DriverNight.Document(new NightParts(huge, Tonight, null, Facts("Raced."), At) { FirstNight = true }));
    }

    [Fact]
    public void The_transponder_never_goes_into_the_document()
    {
        var row = Row(Sam, 1, laps: new long[] { 9000, 8500 });
        row.JumpStarted = true;
        var heat = Race("Heat 1", Tonight.AddHours(-1), row, Row(Kim, 2));

        var document = DriverNight.Document(new NightParts("Sam Parker", Tonight, null, Facts("Raced."), At)
        {
            Races = DriverNightExtras.Races(Sam, new[] { heat }, null),
            FirstNight = true,
        });

        Assert.DoesNotContain(Sam, document!);
        Assert.DoesNotContain(Kim, document!);
    }

    // --- improve ------------------------------------------------------------

    [Fact]
    public void Improvement_says_quicker_slower_or_the_same()
    {
        Assert.Equal(
            "Your middle lap is 0.40 s quicker than in your first race tonight (Q1 Heat 2)",
            DriverNightExtras.Improvement(Sam, new[] { Pace(Sam, 9000, 8600) }));

        Assert.Equal(
            "Your middle lap is 1.25 s slower than in your first race tonight (Q1 Heat 2)",
            DriverNightExtras.Improvement(Sam, new[] { Pace(Sam, 9000, 10_250) }));

        Assert.Equal(
            "Your middle lap is the same pace as in your first race tonight (Q1 Heat 2)",
            DriverNightExtras.Improvement(Sam, new[] { Pace(Sam, 9000, 9003) }));
    }

    [Fact]
    public void No_improvement_without_two_races()
    {
        Assert.Null(DriverNightExtras.Improvement(Sam, Array.Empty<DriverPaceTonight>()));
        Assert.Null(DriverNightExtras.Improvement(Sam, new[] { Pace(Kim, 9000, 8000) }));
        Assert.Null(DriverNightExtras.Improvement(Sam, new[] { Pace(Sam, 9000, 8000) with { Races = 1 } }));
        Assert.Null(DriverNightExtras.Improvement(Sam, null));
    }

    // --- progress -----------------------------------------------------------

    [Fact]
    public void Progress_is_their_best_each_night_oldest_first_tonight_last_at_most_six()
    {
        var history = new List<SessionRecord>();

        for (var week = 8; week >= 1; week--)
        {
            history.Add(Race("Heat", Tonight.AddDays(-7 * week), Row(Sam, 1, best: 9000 + week * 10)));
        }

        // Two on one night: the quicker counts.
        history.Add(Race("Heat", Tonight.AddDays(-7).AddMinutes(30), Row(Sam, 1, best: 8950)));
        history.Add(Race("Heat", Tonight.AddHours(-1), Row(Sam, 1, best: 8900)));

        var progress = DriverNightExtras.ProgressOver(Sam, history, "Club Oval", null, Tonight);

        Assert.NotNull(progress);
        Assert.Equal("Club Oval", progress!.Layout);
        Assert.Equal(DriverNightExtras.MostProgressNights, progress.Nights.Count);
        Assert.Equal("2026-10-22", progress.Nights[^1].Night);
        Assert.Equal(8900, progress.Nights[^1].BestMs);
        Assert.Equal(8950, progress.Nights[^2].BestMs);
        Assert.True(progress.Nights.Select(n => n.Night).SequenceEqual(progress.Nights.Select(n => n.Night).OrderBy(n => n, StringComparer.Ordinal)));
        Assert.Equal("Your best ever on Club Oval", progress.Since);
    }

    [Fact]
    public void Progress_says_since_when_an_earlier_night_was_quicker()
    {
        var history = new List<SessionRecord>
        {
            Race("Heat", new DateTime(2026, 9, 3, 20, 0, 0, DateTimeKind.Local), Row(Sam, 1, best: 8000)),
            Race("Heat", Tonight.AddDays(-14), Row(Sam, 1, best: 9500)),
            Race("Heat", Tonight.AddDays(-7), Row(Sam, 1, best: 9400)),
            Race("Heat", Tonight.AddHours(-1), Row(Sam, 1, best: 9000)),
        };

        Assert.Equal(
            "Your best on Club Oval since 3 September 2026",
            DriverNightExtras.ProgressOver(Sam, history, "Club Oval", null, Tonight)!.Since);
    }

    [Fact]
    public void Progress_says_nothing_when_last_week_was_quicker_or_there_is_one_night()
    {
        var slower = new List<SessionRecord>
        {
            Race("Heat", Tonight.AddDays(-7), Row(Sam, 1, best: 8000)),
            Race("Heat", Tonight.AddHours(-1), Row(Sam, 1, best: 9000)),
        };

        var progress = DriverNightExtras.ProgressOver(Sam, slower, "Club Oval", null, Tonight);

        Assert.NotNull(progress);
        Assert.Null(progress!.Since);
        Assert.Equal(2, progress.Nights.Count);

        Assert.Null(DriverNightExtras.ProgressOver(Sam, new[] { Race("Heat", Tonight.AddHours(-1), Row(Sam, 1, best: 9000)) }, "Club Oval", null, Tonight));
    }

    [Fact]
    public void Progress_honours_a_personal_best_reset_other_layouts_and_demos()
    {
        var reset = Tonight.AddDays(-10).ToUniversalTime();

        var demo = Race("[DEMO]", Tonight.AddDays(-3), Row(Sam, 1, best: 7000));
        demo.IsDemo = true;

        var elsewhere = Race("Heat", Tonight.AddDays(-4), Row(Sam, 1, best: 7000));
        elsewhere.TrackLayoutName = "Infield";

        var history = new List<SessionRecord>
        {
            Race("Heat", Tonight.AddDays(-14), Row(Sam, 1, best: 7500)), // before the reset
            Race("Heat", Tonight.AddDays(-7), Row(Sam, 1, best: 9400)),
            demo,
            elsewhere,
            Race("Heat", Tonight.AddHours(-1), Row(Sam, 1, best: 9000)),
        };

        DateTime? CutoffOf(string name, string track) => name == "Sam Parker" && track == "Club Oval" ? reset : null;

        var progress = DriverNightExtras.ProgressOver(Sam, history, "Club Oval", CutoffOf, Tonight);

        Assert.Equal(new long[] { 9400, 9000 }, progress!.Nights.Select(n => n.BestMs));
        Assert.Equal("Your best ever on Club Oval", progress.Since);
    }

    [Fact]
    public void No_progress_without_a_best_lap_tonight()
    {
        var history = new[]
        {
            Race("Heat", Tonight.AddDays(-7), Row(Sam, 1, best: 9000)),
            Race("Heat", Tonight.AddHours(-1), Row(Sam, 1, best: 0)),
        };

        Assert.Null(DriverNightExtras.ProgressOver(Sam, history, "Club Oval", null, Tonight));
        Assert.Null(DriverNightExtras.ProgressOver(Sam, history, "  ", null, Tonight));
        Assert.Null(DriverNightExtras.ProgressOver(Ghost, history, "Club Oval", null, Tonight));
    }

    // --- reach --------------------------------------------------------------

    [Fact]
    public void Reach_is_the_three_nearest_badges_in_achievement_reachs_own_words()
    {
        var driver = new DriverAchievements { Transponder = Sam, RacesFinished = 8, TotalLaps = 900, Podiums = 2 };
        driver.Unlocked.Add(new UnlockedAchievement { Id = AchievementCatalogue.FirstRace });
        driver.Unlocked.Add(new UnlockedAchievement { Id = AchievementCatalogue.FirstPodium });

        var reach = DriverNightExtras.Reach(driver, null);
        var near = AchievementReach.For(driver, DriverNightExtras.MostReach);

        Assert.Equal(near.Select(n => n.Id), reach.Select(r => r.Id));
        Assert.Equal(near.Select(n => n.Words), reach.Select(r => r.Words));
        Assert.True(reach.Count <= 3);
        Assert.Empty(DriverNightExtras.Reach(null, null));

        var night = Read(DriverNight.Document(new NightParts("Sam Parker", Tonight, null, Facts("Raced."), At) { Reach = reach }));
        var first = night.GetProperty("reach")[0];

        Assert.Equal(reach[0].Id, first.GetProperty("id").GetString());
        Assert.Equal(reach[0].Words, first.GetProperty("words").GetString());
        Assert.Equal(reach[0].Have, first.GetProperty("have").GetInt32());
        Assert.Equal(reach[0].Need, first.GetProperty("need").GetInt32());
    }

    // --- Scaffolding --------------------------------------------------------

    private static PackedFacts Facts(params string[] lines) =>
        new(string.Join('\n', lines), Array.Empty<string>(), 0);

    private static JsonElement Read(string? document)
    {
        Assert.NotNull(document);

        return JsonDocument.Parse(document!).RootElement;
    }

    private static DriverPaceTonight Pace(string who, long firstMs, long latestMs) =>
        new(who, firstMs, latestMs, 2, "Q1 Heat 2", "Q2 Heat 1");

    private static SessionResultEntry Row(string who, int pos, int start = 0, long[]? laps = null, long best = 8500) => new()
    {
        Transponder = who,
        DriverName = who == Sam ? "Sam Parker" : "Kim Lee",
        Position = pos,
        StartPosition = start,
        Laps = laps?.Length ?? 10,
        LapTimesMs = (laps ?? Array.Empty<long>()).ToList(),
        FastLapMs = best,
    };

    private static SessionRecord Race(string label, DateTime startLocal, params SessionResultEntry[] rows) => new()
    {
        SessionType = RaceSessionType.Race,
        Label = label,
        TrackLayoutName = "Club Oval",
        StartedAtUtc = startLocal.ToUniversalTime(),
        EndedAtUtc = startLocal.ToUniversalTime().AddMinutes(5),
        Results = rows.ToList(),
    };
}
