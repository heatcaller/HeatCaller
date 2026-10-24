using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// A driver's report card (CoachReport, 22 Oct 2026), and the place it rides
/// on their own night (DriverNight's `coach`).
///
/// The card is arithmetic over laps the loop read, so these tests pin the
/// sums and - as much as the sums - the places it says NOTHING: no ideal
/// lap without sectors, no consistency on too few clean laps, no setup
/// verdict without enough laps on both sides of the change. A card that
/// guessed would teach a driver the wrong thing about their car.
///
/// Nights here are UTC dates (NightOf = utc.Date) so nothing depends on the
/// machine's time zone; the club's clock is a fixed "7:40 pm".
/// </summary>
public sealed class CoachReportTests
{
    private const string Me = "1001";
    private const string Sam = "1002";
    private const string Layout = "Oval";

    private static readonly DateTime Tonight = new(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);

    // --- Helpers --------------------------------------------------------------

    /// <summary>A result whose first lap is the start lap (green to line), as saved results are.</summary>
    private static SessionResultEntry Row(string who, params long[] flying) => new()
    {
        Transponder = who,
        DriverName = who,
        LapTimesMs = new[] { 20_000L }.Concat(flying).ToList(),
        Laps = flying.Length,
        FastLapMs = flying.Length > 0 ? flying.Min() : 0,
    };

    /// <summary>A result with sectors on each flying lap - each lap's sectors must add up to it.</summary>
    private static SessionResultEntry RowWithSectors(string who, params long[][] sectorsPerLap)
    {
        var row = Row(who, sectorsPerLap.Select(s => s.Sum()).ToArray());
        row.SectorTimesMs = new List<long[]?> { null }.Concat(sectorsPerLap.Select(s => (long[]?)s)).ToList();
        return row;
    }

    private static SessionRecord Session(DateTime startUtc, string layout, params SessionResultEntry[] rows) => new()
    {
        SessionType = RaceSessionType.Race,
        TrackLayoutName = layout,
        StartedAtUtc = startUtc,
        EndedAtUtc = startUtc.AddMinutes(6),
        Results = rows.ToList(),
    };

    private static IReadOnlyList<string> Card(
        IEnumerable<SessionRecord> history,
        CoachReport.SetupChange? setup = null,
        string who = Me,
        string nightWord = "tonight") =>
        CoachReport.Lines(new CoachReport.Input(
            who,
            history,
            Tonight,
            utc => utc.Date,
            t => t == Sam ? "Sam" : string.Empty,
            setup,
            _ => "7:40 pm",
            nightWord));

    private static DateTime At(int hour, int minute = 0) => Tonight.AddHours(hour).AddMinutes(minute);

    // --- Nothing to say ----------------------------------------------------------

    [Fact]
    public void A_driver_who_has_not_run_tonight_has_no_card()
    {
        Assert.Empty(Card(Array.Empty<SessionRecord>()));
        Assert.Empty(Card(new[] { Session(At(19), Layout, Row(Sam, 9000, 9100)) }));
    }

    [Fact]
    public void A_ghost_has_no_card()
    {
        var ghost = Ghosts.Prefix + "1";

        Assert.Empty(Card(new[] { Session(At(19), Layout, Row(ghost, 9000, 9100)) }, who: ghost));
    }

    [Fact]
    public void A_demo_race_is_not_part_of_anybody_s_night()
    {
        var demo = Session(At(19), Layout, Row(Me, 8000, 8100));
        demo.IsDemo = true;

        Assert.Empty(Card(new[] { demo }));
    }

    // --- Best lap, and the ideal lap ---------------------------------------------

    [Fact]
    public void The_best_lap_never_counts_the_start_lap()
    {
        var card = Card(new[] { Session(At(19), Layout, Row(Me, 9000, 8900, 9100)) });

        Assert.Equal("Best lap tonight on Oval: 8.900s, from 3 laps.", card[0]);
    }

    [Fact]
    public void Best_sectors_add_up_to_the_ideal_lap_and_the_time_still_to_find()
    {
        var card = Card(new[]
        {
            Session(At(19), Layout, RowWithSectors(Me, new[] { 4000L, 5000L }, new[] { 4100L, 4800L }, new[] { 4050L, 5050L })),
        });

        Assert.Equal(
            "Best lap tonight on Oval: 8.900s, from 3 laps. Your best sectors add up to an ideal lap of 8.800s - 0.10s still to find.",
            card[0]);
    }

    [Fact]
    public void No_sectors_no_ideal_lap()
    {
        var card = Card(new[] { Session(At(19), Layout, Row(Me, 9000, 8900, 9100)) });

        Assert.DoesNotContain(card, line => line.Contains("ideal", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(card, line => line.StartsWith("Sector", StringComparison.Ordinal));
    }

    // --- Consistency -------------------------------------------------------------

    [Fact]
    public void Consistency_is_the_average_distance_from_the_typical_clean_lap()
    {
        // 15.000 is a crash: past 107% of the best, so not a clean lap.
        var card = Card(new[] { Session(At(19), Layout, Row(Me, 9000, 9100, 8900, 9000, 9200, 15000)) });

        Assert.Contains("Consistency: your clean laps were on average 0.08s off your typical lap (5 of 6 laps clean).", card);
    }

    [Fact]
    public void Too_few_clean_laps_for_a_consistency_figure_says_nothing()
    {
        var card = Card(new[] { Session(At(19), Layout, Row(Me, 9000, 9100, 8900, 9000)) });

        Assert.DoesNotContain(card, line => line.StartsWith("Consistency", StringComparison.Ordinal));
    }

    [Fact]
    public void The_typical_lap_is_always_one_that_was_run()
    {
        Assert.Equal(9000, CoachReport.Median(new long[] { 9100, 9000 }));
        Assert.Equal(9000, CoachReport.Median(new long[] { 9200, 8900, 9000 }));
    }

    // --- The costliest sector ----------------------------------------------------

    [Fact]
    public void The_sector_a_clean_lap_loses_most_in_is_named()
    {
        // Sector two: 200 + 0 + 250 lost over three laps = 0.15s a lap.
        var card = Card(new[]
        {
            Session(At(19), Layout, RowWithSectors(Me, new[] { 4000L, 5000L }, new[] { 4100L, 4800L }, new[] { 4050L, 5050L })),
        });

        Assert.Contains("Sector two is where most of your time goes: on a clean lap you are 0.15s slower through it than your best.", card);
    }

    // --- The last night here -----------------------------------------------------

    [Fact]
    public void Tonight_is_compared_with_the_last_night_on_the_same_layout()
    {
        var card = Card(new[]
        {
            Session(At(19).AddDays(-14), Layout, Row(Me, 9300)),
            Session(At(19).AddDays(-7), Layout, Row(Me, 9100, 9200)),
            Session(At(19).AddDays(-3), "Figure Eight", Row(Me, 7000)),
            Session(At(19), Layout, Row(Me, 9000, 8900)),
        });

        var line = Assert.Single(card, l => l.Contains("your best here on", StringComparison.Ordinal));

        Assert.StartsWith("0.20s quicker than your best here on ", line);
        Assert.EndsWith("(9.100s).", line);
    }

    [Fact]
    public void Slower_than_last_time_says_so_plainly()
    {
        var card = Card(new[]
        {
            Session(At(19).AddDays(-7), Layout, Row(Me, 8700)),
            Session(At(19), Layout, Row(Me, 9000, 8900)),
        });

        Assert.Contains(card, l => l.StartsWith("0.20s off your best here on ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_first_night_on_a_layout_has_nothing_to_compare_with()
    {
        var card = Card(new[] { Session(At(19), Layout, Row(Me, 9000, 8900)) });

        Assert.DoesNotContain(card, l => l.Contains("your best here", StringComparison.Ordinal));
    }

    // --- Setup against pace ------------------------------------------------------

    private static SessionRecord[] AroundASetupChange(int lapsAfter)
    {
        var before = Session(At(19), Layout, Row(Me, Enumerable.Repeat(9000L, 8).ToArray()));
        var after = Session(At(20), Layout, Row(Me, Enumerable.Repeat(8850L, lapsAfter).ToArray()));
        return new[] { before, after };
    }

    [Fact]
    public void A_setup_change_is_judged_on_the_typical_lap_either_side_of_it()
    {
        var card = Card(AroundASetupChange(8), new CoachReport.SetupChange(At(19, 40), "Pinion 22 → 23"));

        Assert.Contains(
            "Since the setup change (Pinion 22 → 23) logged at 7:40 pm, your typical lap is 0.15s quicker (9.000s → 8.850s; 8 clean laps before, 8 after).",
            card);
    }

    [Fact]
    public void Too_few_laps_on_one_side_of_a_setup_change_says_nothing_about_it()
    {
        var card = Card(AroundASetupChange(7), new CoachReport.SetupChange(At(19, 40), "Pinion 22 → 23"));

        Assert.DoesNotContain(card, l => l.StartsWith("Since the setup change", StringComparison.Ordinal));
    }

    [Fact]
    public void A_run_going_on_when_the_setup_was_logged_belongs_to_neither_side()
    {
        // Logged three minutes into the first run: it ended after the change
        // and started before it, so it counts on neither side - and with no
        // laps before, there is nothing to say.
        var card = Card(AroundASetupChange(8), new CoachReport.SetupChange(At(19, 3), null));

        Assert.DoesNotContain(card, l => l.StartsWith("Since the setup change", StringComparison.Ordinal));
    }

    [Fact]
    public void A_change_that_made_no_difference_says_about_the_same()
    {
        var before = Session(At(19), Layout, Row(Me, Enumerable.Repeat(9000L, 8).ToArray()));
        var after = Session(At(20), Layout, Row(Me, Enumerable.Repeat(9010L, 8).ToArray()));

        var card = Card(new[] { before, after }, new CoachReport.SetupChange(At(19, 40), null));

        Assert.Contains(card, l => l.StartsWith("Since the setup change logged at 7:40 pm, your typical lap is about the same", StringComparison.Ordinal));
    }

    // --- Against the quickest ----------------------------------------------------

    [Fact]
    public void The_gap_to_the_quickest_driver_raced_with_tonight_is_given()
    {
        var card = Card(new[] { Session(At(19), Layout, Row(Me, 9000, 8900), Row(Sam, 8700, 8600)) });

        Assert.Equal("0.30s off Sam's best of 8.600s, the quickest lap of anyone you raced with tonight.", card[^1]);
    }

    [Fact]
    public void Most_of_the_gap_in_one_sector_is_said()
    {
        var card = Card(new[]
        {
            Session(At(19), Layout,
                RowWithSectors(Me, new[] { 4300L, 4600L }),
                RowWithSectors(Sam, new[] { 4000L, 4600L })),
        });

        Assert.Equal("0.30s off Sam's best of 8.600s, the quickest lap of anyone you raced with tonight - mostly in sector one.", card[^1]);
    }

    [Fact]
    public void A_ghost_is_not_somebody_they_raced_with()
    {
        var card = Card(new[] { Session(At(19), Layout, Row(Me, 9000, 8900), Row(Ghosts.Prefix + "9", 7000)) });

        Assert.DoesNotContain(card, l => l.Contains("7.000s", StringComparison.Ordinal));
    }

    [Fact]
    public void The_quickest_of_the_night_is_told_so()
    {
        var card = Card(new[] { Session(At(19), Layout, Row(Me, 8500), Row(Sam, 8700, 8600)) });

        Assert.Equal("Nobody you raced with tonight went quicker than your best lap.", card[^1]);
    }

    // --- One layout, and a past night -------------------------------------------

    [Fact]
    public void Two_layouts_in_one_night_the_card_is_about_the_latest()
    {
        var card = Card(new[]
        {
            Session(At(19), "Figure Eight", Row(Me, 7000, 7100)),
            Session(At(20), Layout, Row(Me, 9000, 8900)),
        });

        Assert.Equal("Best lap tonight on Oval: 8.900s, from 2 laps.", card[0]);
    }

    [Fact]
    public void A_past_night_is_that_night_not_tonight()
    {
        var card = Card(new[] { Session(At(19), Layout, Row(Me, 9000, 8900), Row(Sam, 8600)) }, nightWord: "that night");

        Assert.Equal("Best lap that night on Oval: 8.900s, from 2 laps.", card[0]);
        Assert.EndsWith("anyone you raced with that night.", card[^1]);
    }

    // --- On their own night -----------------------------------------------------

    private static readonly DateTime NightLocal = new(2026, 10, 22, 19, 42, 0, DateTimeKind.Local);
    private static readonly DateTime BuiltAt = new(2026, 10, 22, 21, 30, 0, DateTimeKind.Utc);

    private static PackedFacts Facts(params string[] lines) =>
        new(string.Join('\n', lines), Array.Empty<string>(), 0);

    [Fact]
    public void The_card_rides_on_the_night_as_a_list_of_its_own()
    {
        var document = DriverNight.Document("Bill Hargrove", NightLocal, null, Facts("Raced."), BuiltAt,
            new[] { "Best lap tonight: 8.900s, from 2 laps.", "  ", "Consistency: fine." });

        var night = JsonDocument.Parse(document!).RootElement;

        Assert.Equal(1, night.GetProperty("lines").GetArrayLength());
        Assert.Equal(2, night.GetProperty("coach").GetArrayLength());
        Assert.Equal("Raced.", night.GetProperty("lines")[0].GetString());
    }

    [Fact]
    public void A_night_without_a_card_is_the_document_it_always_was()
    {
        var without = DriverNight.Document("Bill Hargrove", NightLocal, null, Facts("Raced."), BuiltAt);
        var empty = DriverNight.Document("Bill Hargrove", NightLocal, null, Facts("Raced."), BuiltAt, Array.Empty<string>());

        Assert.Equal(without, empty);
        Assert.False(JsonDocument.Parse(without!).RootElement.TryGetProperty("coach", out _));
    }

    [Fact]
    public void A_card_alone_is_not_a_night()
    {
        // The night is the driver's races; a card with no races behind it
        // is not something to put their name on a relay for.
        Assert.Null(DriverNight.Document("Bill Hargrove", NightLocal, null, Facts(), BuiltAt, new[] { "Best lap tonight: 8.900s." }));
    }
    // --- The phone coach page ---------------------------------------------------

    private static WebServerService CoachServer(string transponder)
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = transponder, DriverName = "Coach Test" });

        var race = new RaceControlSession();
        race.Stage();
        race.GreenFlag(0);

        foreach (var at in new uint[] { 11_000, 20_000, 29_100 })
        {
            race.ProcessLapEvent(new DirectLapEvent
            {
                TransponderId = transponder,
                DeviceTimeMs = at,
                SignalHits = 1,
                SignalStrength = 100,
                IsSynthetic = true,
            });
        }

        return new WebServerService(new RaceTiming(race), profiles, settings: new AppSettings { DisplayLook = Looks.MidnightKey });
    }

    [Fact]
    public void A_target_set_by_race_control_goes_on_the_driver_s_card_for_the_phone()
    {
        const string car = "T7701";
        DriverTargets.ClearAll();

        try
        {
            var server = CoachServer(car);

            var before = System.Text.Json.Nodes.JsonNode.Parse(server.BuildStateJson())!.AsObject();
            var id = before["standings"]!.AsArray().Single()!["id"]!.GetValue<string>();

            Assert.Null(before["cards"]![id]!["target"]);

            DriverTargets.Set(car, 8_500);

            var after = System.Text.Json.Nodes.JsonNode.Parse(server.BuildStateJson())!.AsObject();

            Assert.Equal(8_500, after["cards"]![id]!["target"]!.GetValue<long>());

            // Under the opaque id, never the transponder - a "T" can never
            // turn up inside a lower-case hex id by chance, so this cannot
            // pass or fail on the luck of the roster key.
            Assert.DoesNotContain(car, after["cards"]!.ToJsonString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DriverTargets.ClearAll();
        }
    }

    [Fact]
    public void The_coach_page_reads_only_the_board_and_the_card()
    {
        var page = CoachServer("T7702").BuildCoachPage();

        // The same remembered pick as Live Results' Follow.
        Assert.Contains("'hcFollow'", page);
        Assert.Contains("fetch('/api/state', { signal: stop.signal })", page);
        Assert.Contains("/api/coach?id=", page);

        // Nothing else on the wire.
        Assert.DoesNotContain("/api/roster", page);
        Assert.DoesNotContain("/api/command", page);
    }

    [Fact]
    public void The_coach_page_can_say_the_drivers_own_lap_time_and_beeps_where_it_cannot_buzz()
    {
        var page = CoachServer("T7702").BuildCoachPage();

        // Say my lap time (22 Oct 2026): ticked on the phone, remembered there,
        // the phone's own voice - and only for a lap that has just come in.
        Assert.Contains("id=\"say\"", page);
        Assert.Contains("Say my lap time", page);
        Assert.Contains("'hcCoachSay'", page);
        Assert.Contains("new SpeechSynthesisUtterance(text)", page);
        Assert.Contains("laps.length > lapsSeen", page);

        // An iPhone has no vibration for a web page: the new best beeps there.
        Assert.Contains("if (!buzzed) beep();", page);
    }

    [Fact]
    public void A_report_card_copies_as_numbered_lines_and_draws_on_a_letter_page()
    {
        var lines = new[] { "Best 9.210s; ideal 9.050s.", "Clean laps within 0.21s." };

        Assert.Equal(
            "Report card - Sam - Wed 22 Oct" + Environment.NewLine + Environment.NewLine
            + "1. Best 9.210s; ideal 9.050s." + Environment.NewLine + "2. Clean laps within 0.21s.",
            ReportCardForm.AsText("Sam", "Wed 22 Oct", lines));

        using var page = ReportCardSheet.Render("Sam", "Wed 22 Oct", null, lines);

        Assert.Equal(1275, page.Width);
        Assert.Equal(1650, page.Height);

        using var empty = ReportCardSheet.Render("", "Wed 22 Oct", null, Array.Empty<string>());
        Assert.Equal(1275, empty.Width);
    }
}
