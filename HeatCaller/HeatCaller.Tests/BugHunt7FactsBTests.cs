namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 7, the commentary facts (part b): lines the PA reads out about
/// people in the room that the room could disprove.
/// </summary>
public sealed class BugHunt7FactsBTests : IDisposable
{
    // Declared first: every store below loads from wherever it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>Seven in the evening, which NightOf reads as that day's race night.</summary>
    private static readonly DateTime Now = new(2026, 10, 11, 19, 0, 0, DateTimeKind.Local);

    private const string Stock = "Box Stock";

    private static SessionRecord Raced(
        DateTime endedLocal,
        string? roundId = null,
        params (string Transponder, int Position, int Laps)[] finishers) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            SessionType = RaceSessionType.Race,
            RoundId = roundId,
            Label = "A-Main",
            TrackLayoutName = "Club Oval",
            StartedAtUtc = endedLocal.ToUniversalTime().AddMinutes(-6),
            EndedAtUtc = endedLocal.ToUniversalTime(),
            Results = finishers
                .Select(f => new SessionResultEntry
                {
                    Transponder = f.Transponder,
                    DriverName = f.Transponder,
                    Position = f.Position,
                    Laps = f.Laps,
                    FastLapMs = 11_500,
                })
                .ToList(),
        };

    private static ProfileStore Sam()
    {
        var profiles = ProfileStore.Load();

        profiles.Upsert(new DriverProfile { Transponder = "A", DriverName = "Sam" });

        return profiles;
    }

    private static DriverWeeks Weeks(
        int clubRan = 6,
        int out_ = 5,
        int missed = 1,
        int podiumRun = 0,
        int started = 5,
        bool finishedAll = false,
        string lastOut = "3 October",
        int missedSince = 0) =>
        new("A", "Sam", clubRan, out_, missed, podiumRun, started, finishedAll, lastOut, missedSince);

    // --- 31: a podium run can span a night they missed -------------------

    [Fact]
    public void A_podium_run_is_worded_as_the_nights_they_raced_not_the_clubs_last_nights()
    {
        // Run of three over the club's last four nights, one of them missed.
        var block = CommentaryWeekFacts.OneDriver(Weeks(clubRan: 4, out_: 3, missed: 1, podiumRun: 3), Stock, "night-1");

        Assert.NotNull(block);
        Assert.Contains("top three on each of the last three Box Stock nights they have raced", block!.Text);
        Assert.DoesNotContain("club's last", block.Text);
    }

    // --- 106: "finished every race" spans the whole window -----------------

    [Fact]
    public void Finished_every_race_names_the_nights_the_races_were_counted_over()
    {
        var block = CommentaryWeekFacts.OneDriver(
            Weeks(clubRan: 6, out_: 4, missed: 2, started: 8, finishedAll: true), Stock, "night-1");

        Assert.NotNull(block);
        Assert.Contains("finished every one of the 8 races they have started over the club's last six Box Stock nights", block!.Text);
    }

    // --- 32: "back after missing" counts the misses since they were last out -

    [Fact]
    public void A_driver_who_raced_last_week_is_not_back_after_missing_anything()
    {
        // Out at the three newest nights, missed the three before them.
        var sessions = new List<SessionRecord>();

        for (var week = 1; week <= 6; week++)
        {
            sessions.Add(week <= 3
                ? Raced(Now.AddDays(-7 * week), finishers: new[] { ("B", 1, 12), ("C", 2, 12), ("A", 5, 12) })
                : Raced(Now.AddDays(-7 * week), finishers: new[] { ("B", 1, 12), ("C", 2, 12) }));
        }

        var sam = CommentaryReaders.RecentWeeks(
            new[] { "A" }, string.Empty, Sam(), SessionHistoryStore.FromSessions(sessions), RoundDefinitionStore.Load(), Now).Single();

        Assert.Equal(3, sam.NightsMissed);
        Assert.Equal(0, sam.NightsMissedSinceLastOut);

        var block = CommentaryWeekFacts.OneDriver(sam, Stock, "night-1");

        Assert.True(block is null || !block.Text.Contains("back after missing"));
    }

    [Fact]
    public void A_driver_who_missed_the_clubs_last_three_nights_is_back_after_missing_them()
    {
        var sessions = new List<SessionRecord>();

        for (var week = 1; week <= 6; week++)
        {
            sessions.Add(week > 3
                ? Raced(Now.AddDays(-7 * week), finishers: new[] { ("B", 1, 12), ("C", 2, 12), ("A", 5, 12) })
                : Raced(Now.AddDays(-7 * week), finishers: new[] { ("B", 1, 12), ("C", 2, 12) }));
        }

        var sam = CommentaryReaders.RecentWeeks(
            new[] { "A" }, string.Empty, Sam(), SessionHistoryStore.FromSessions(sessions), RoundDefinitionStore.Load(), Now).Single();

        Assert.Equal(3, sam.NightsMissedSinceLastOut);

        var block = CommentaryWeekFacts.OneDriver(sam, Stock, "night-1");

        Assert.NotNull(block);
        Assert.Contains("back after missing the club's last three Box Stock nights - their last was", block!.Text);
    }

    // --- 99: a row saved with no laps is not a finish ----------------------

    [Fact]
    public void A_car_that_never_crossed_the_line_has_not_finished_or_made_the_top_three()
    {
        var sessions = new List<SessionRecord>
        {
            Raced(Now.AddDays(-21), finishers: new[] { ("A", 1, 12), ("B", 2, 12), ("C", 3, 12) }),
            Raced(Now.AddDays(-14), finishers: new[] { ("A", 1, 12), ("B", 2, 12), ("C", 3, 12) }),

            // Third of three on the board, with no laps and no outcome.
            Raced(Now.AddDays(-7), finishers: new[] { ("B", 1, 12), ("C", 2, 12), ("A", 3, 0) }),
        };

        var sam = CommentaryReaders.RecentWeeks(
            new[] { "A" }, string.Empty, Sam(), SessionHistoryStore.FromSessions(sessions), RoundDefinitionStore.Load(), Now).Single();

        Assert.False(sam.FinishedEverything);
        Assert.Equal(0, sam.PodiumNightsRunning);
        Assert.Equal(3, sam.RacesStarted);
    }

    // --- 75: "the first since" a win in another year -----------------------

    [Fact]
    public void A_win_five_weeks_ago_across_new_year_is_dated_by_month_and_year_in_english()
    {
        var was = System.Globalization.CultureInfo.CurrentCulture;

        try
        {
            // A German PC, whose own month name would be "November" too -
            // so the check below is the year, and the test below that is
            // the culture.
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            var history = SessionHistoryStore.FromSessions(new[]
            {
                Raced(new DateTime(2025, 11, 20, 12, 0, 0, DateTimeKind.Utc).ToLocalTime(), finishers: new[] { ("A", 1, 12), ("B", 2, 12) }),
            });

            var (grid, _) = CommentaryReaders.GridHistory(
                new[] { "A" }, null, Sam(), history, RoundDefinitionStore.Load(),
                now: new DateTime(2026, 1, 8, 12, 0, 0, DateTimeKind.Utc));

            Assert.Equal("November 2025", grid.Single().LastWinDescribed);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = was;
        }
    }

    [Fact]
    public void A_month_name_in_the_first_since_line_is_english_on_any_pc()
    {
        var was = System.Globalization.CultureInfo.CurrentCulture;

        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            var history = SessionHistoryStore.FromSessions(new[]
            {
                Raced(new DateTime(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc).ToLocalTime(), finishers: new[] { ("A", 1, 12), ("B", 2, 12) }),
            });

            var (grid, _) = CommentaryReaders.GridHistory(
                new[] { "A" }, null, Sam(), history, RoundDefinitionStore.Load(),
                now: new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc));

            // "März" on the German PC before.
            Assert.Equal("March", grid.Single().LastWinDescribed);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = was;
        }
    }

    // --- 103 / 150: this day at the club is a race night -------------------

    [Fact]
    public void A_past_night_that_ran_past_midnight_is_one_night_on_this_day()
    {
        var sessions = new[]
        {
            Raced(new DateTime(2025, 10, 10, 20, 0, 0, DateTimeKind.Local), finishers: new[] { ("A", 1, 12), ("B", 2, 12) }),

            // The same night's A-main, after midnight, with a driver only in it.
            Raced(new DateTime(2025, 10, 11, 0, 40, 0, DateTimeKind.Local), finishers: new[] { ("A", 1, 12), ("C", 2, 12) }),

            // The NEXT night, which is not this night's anniversary.
            Raced(new DateTime(2025, 10, 11, 20, 0, 0, DateTimeKind.Local), finishers: new[] { ("D", 1, 12) }),
        };

        var history = SessionHistoryStore.FromSessions(sessions);

        var evening = CommentaryReaders.OnThisDay(history, new DateTime(2026, 10, 10, 21, 0, 0, DateTimeKind.Local));

        Assert.Single(evening);
        Assert.Equal(2, evening[0].Races);
        Assert.Equal(3, evening[0].Drivers);

        // Asked at half past midnight, it is still the night of the 10th.
        var lateOn = CommentaryReaders.OnThisDay(history, new DateTime(2026, 10, 11, 0, 30, 0, DateTimeKind.Local));

        Assert.Single(lateOn);
        Assert.Equal(2, lateOn[0].Races);
        Assert.Equal(3, lateOn[0].Drivers);
    }

    // --- 104: a bigger heat earlier tonight ---------------------------------

    [Fact]
    public void A_bigger_field_earlier_tonight_stops_this_one_being_called_the_biggest()
    {
        var rounds = RoundDefinitionStore.Load();
        var heat = new RoundDefinition { ClassName = Stock, RoundLabel = "Heat 1", Kind = RoundKind.Qualifier };

        rounds.Add(heat);

        var four = new[] { ("A", 1, 12), ("B", 2, 12), ("C", 3, 12), ("D", 4, 12) };
        var six = new[] { ("A", 1, 12), ("B", 2, 12), ("C", 3, 12), ("D", 4, 12), ("E", 5, 12), ("F", 6, 12) };

        var sessions = new List<SessionRecord>
        {
            Raced(Now.AddDays(-28), heat.Id, four),
            Raced(Now.AddDays(-21), heat.Id, four),
            Raced(Now.AddDays(-14), heat.Id, four),
            Raced(Now.AddDays(-7), heat.Id, four),

            // Six out an hour ago.
            Raced(Now.AddHours(-1), heat.Id, six),
        };

        var field = CommentaryReaders.Field(
            new[] { "A", "B", "C", "D", "E" },
            Stock,
            SessionHistoryStore.FromSessions(sessions),
            rounds,
            Now);

        Assert.NotNull(field);
        Assert.True(field!.BiggerTonight);
        Assert.Null(CommentaryRaceFacts.Entry(field, "night-1"));
    }

    [Fact]
    public void A_field_as_big_as_tonights_biggest_is_still_sayable()
    {
        Assert.NotNull(CommentaryRaceFacts.Entry(new FieldStrength(Stock, 10, 20, null, 20, 0), "night-1"));
        Assert.Null(CommentaryRaceFacts.Entry(new FieldStrength(Stock, 10, 20, null, 20, 0, BiggerTonight: true), "night-1"));
    }

    // --- 107: "either" fits two -------------------------------------------

    [Fact]
    public void Three_retirements_in_a_row_is_any_of_the_last_three()
    {
        var three = CommentaryHistoryFacts.Streak(new DriverForm("sam", "Sam", 20, 3, 8, new[] { 0, 0, 0, 4 }), "race-1");

        Assert.NotNull(three);
        Assert.Contains("hasn't finished any of the last three", three!.Text);

        var two = CommentaryHistoryFacts.Streak(new DriverForm("sam", "Sam", 20, 3, 8, new[] { 0, 0, 3 }), "race-1");

        Assert.NotNull(two);
        Assert.Contains("hasn't finished either of the last two", two!.Text);
    }

    // --- 76: the last race of the night is tonight's -----------------------

    [Fact]
    public void Tomorrows_programme_does_not_take_last_of_the_night_from_tonights_main()
    {
        var rounds = RoundDefinitionStore.Load();
        var events = RaceEventStore.Load();

        var saturday = new RaceEvent { Name = "Saturday" };
        events.Upsert(saturday);
        events.SetActive(saturday.Id);

        var heat = new RoundDefinition { ClassName = Stock, RoundLabel = "Heat 1", Kind = RoundKind.Qualifier };
        var main = new RoundDefinition { ClassName = Stock, RoundLabel = "A-Main", Kind = RoundKind.Main, MainTier = 0 };
        rounds.Add(heat);
        rounds.Add(main);
        events.AddRoundsToActive(new[] { heat.Id, main.Id });

        // Sunday's heat, set up the night before - added later, so it has
        // the highest Order in the store.
        var sunday = new RoundDefinition { ClassName = Stock, RoundLabel = "Heat 1", Kind = RoundKind.Qualifier };
        rounds.Add(sunday);

        Assert.True(RaceStature.Of(main, rounds, events.ActiveRoundScope()).LastOfTheNight);
        Assert.False(RaceStature.Of(heat, rounds, events.ActiveRoundScope()).LastOfTheNight);
    }
}
