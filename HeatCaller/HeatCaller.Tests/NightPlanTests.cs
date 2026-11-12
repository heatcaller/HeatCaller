namespace HeatCaller.Tests;

/// <summary>
/// Race-night build plan, Phase 2 (11 Nov 2026): the rest of the night
/// planned - when each race should start, when the night finishes, the
/// out-by time and what would bring the night back inside it, and "we're
/// done after this round". See NightPlan.
/// </summary>
public sealed class NightPlanTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 19, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Five = TimeSpan.FromMinutes(5);

    private static RoundDefinition Heat(string label, string className = "Stock", bool run = false) => new()
    {
        RoundLabel = label,
        ClassName = className,
        Kind = RoundKind.Qualifier,
        LinkedSessionId = run ? Guid.NewGuid().ToString("N") : null,
    };

    private static RoundDefinition Main(string label, string className = "Stock", int tier = 0) => new()
    {
        RoundLabel = label,
        ClassName = className,
        Kind = RoundKind.Main,
        MainTier = tier,
    };

    private static NightPlan.Projection Plan(
        IReadOnlyList<RoundDefinition> rounds,
        string? onTrack = null,
        DateTime? lastFinish = null,
        TimeSpan? gap = null,
        TimeSpan? outByLocal = null,
        IEnumerable<NightPlan.UnbuiltMains>? unbuilt = null) =>
        NightPlan.For(rounds, _ => Five, unbuilt, onTrack, lastFinish, gap, outByLocal, Now)!;

    /// <summary>A local time of day that lands <paramref name="minutesAfterNow"/> after Now.</summary>
    private static TimeSpan LocalTimeOfDay(double minutesAfterNow) =>
        Now.AddMinutes(minutesAfterNow).ToLocalTime().TimeOfDay;

    [Fact]
    public void Nothing_left_to_run_is_no_plan()
    {
        Assert.Null(NightPlan.For(new[] { Heat("Q1 Heat 1", run: true) }, _ => Five, null, null, null, null, null, Now));
    }

    [Fact]
    public void Each_race_follows_the_last_by_its_length_and_the_gap()
    {
        var rounds = new[] { Heat("Q1 Heat 1", run: true), Heat("Q1 Heat 2"), Heat("Q1 Heat 3") };

        var plan = Plan(rounds);

        // Idle, nothing finished recently: the next one is now, then 5 + 3 later.
        Assert.Equal(Now, plan.StartOf(rounds[1].Id));
        Assert.Equal(Now.AddMinutes(8), plan.StartOf(rounds[2].Id));
        Assert.Null(plan.StartOf(rounds[0].Id));
        Assert.Equal(Now.AddMinutes(13), plan.FinishUtc);
        Assert.False(plan.GapIsTonights);
    }

    [Fact]
    public void A_night_built_early_starts_at_the_first_race_time_from_setup()
    {
        var rounds = new[] { Heat("Q1 Heat 1"), Heat("Q1 Heat 2") };

        var plan = NightPlan.For(rounds, _ => Five, null, null, null, null, null, Now, LocalTimeOfDay(90))!;

        Assert.Equal(Now.AddMinutes(90), plan.StartOf(rounds[0].Id));
        Assert.Equal(Now.AddMinutes(98), plan.StartOf(rounds[1].Id));
    }

    [Fact]
    public void A_first_race_time_already_passed_starts_now()
    {
        var rounds = new[] { Heat("Q1 Heat 1"), Heat("Q1 Heat 2") };

        var plan = NightPlan.For(rounds, _ => Five, null, null, null, null, null, Now, LocalTimeOfDay(-20))!;

        Assert.Equal(Now, plan.StartOf(rounds[0].Id));
    }

    [Fact]
    public void Once_a_race_has_run_the_first_race_time_no_longer_holds_the_night()
    {
        // Started early: the rest follows the night, not the setup time.
        var rounds = new[] { Heat("Q1 Heat 1", run: true), Heat("Q1 Heat 2") };

        var plan = NightPlan.For(rounds, _ => Five, null, null, null, null, null, Now, LocalTimeOfDay(30))!;

        Assert.Equal(Now, plan.StartOf(rounds[1].Id));
    }

    [Fact]
    public void Tonights_own_gap_replaces_the_default()
    {
        var rounds = new[] { Heat("Q1 Heat 1"), Heat("Q1 Heat 2") };

        var plan = Plan(rounds, gap: TimeSpan.FromMinutes(6));

        Assert.Equal(Now.AddMinutes(11), plan.StartOf(rounds[1].Id));
        Assert.True(plan.GapIsTonights);
    }

    [Fact]
    public void A_race_just_finished_holds_the_next_one_back_by_the_gap()
    {
        var rounds = new[] { Heat("Q1 Heat 2") };

        var plan = Plan(rounds, lastFinish: Now.AddMinutes(-1));

        Assert.Equal(Now.AddMinutes(2), plan.StartOf(rounds[0].Id));
    }

    [Fact]
    public void A_race_on_track_is_taken_as_half_done()
    {
        var rounds = new[] { Heat("Q1 Heat 1"), Heat("Q1 Heat 2") };

        var plan = Plan(rounds, onTrack: rounds[0].Id);

        // Half of five minutes left, then the gap.
        Assert.Equal(Now.AddMinutes(2.5 + 3), plan.StartOf(rounds[1].Id));
    }

    [Fact]
    public void Mains_not_built_yet_are_counted_lowest_first_after_everything_else()
    {
        var rounds = new[] { Heat("Q1 Heat 1") };

        var plan = Plan(rounds, unbuilt: new[] { new NightPlan.UnbuiltMains("Stock", 2, TimeSpan.FromMinutes(8)) });

        Assert.Equal(new[] { "Q1 Heat 1", "B-Main", "A-Main" }, plan.Slots.Select(s => s.Label));

        // 5 + 3, then 8 + 3, then 8.
        Assert.Equal(Now.AddMinutes(27), plan.FinishUtc);
    }

    [Fact]
    public void Inside_the_out_by_time_there_is_nothing_to_fix()
    {
        var plan = Plan(new[] { Heat("Q1 Heat 1") }, outByLocal: LocalTimeOfDay(60));

        Assert.Null(plan.Over);
        Assert.Empty(plan.Fixes);
        Assert.Contains("inside the", NightPlan.Note(plan));
    }

    [Fact]
    public void Past_the_out_by_time_the_overrun_and_the_fixes_are_given_biggest_first()
    {
        // Q1 runs, Q2 and Q3 still to come: 6 heats of 5 min with 3 min gaps,
        // finishing 45 minutes from now against an out-by 30 minutes away.
        var rounds = new[]
        {
            Heat("Q2 Heat 1"), Heat("Q2 Heat 2"), Heat("Q2 Heat 3"),
            Heat("Q3 Heat 1"), Heat("Q3 Heat 2"), Heat("Q3 Heat 3"),
        };

        var plan = Plan(rounds, outByLocal: LocalTimeOfDay(30));

        Assert.Equal(Now.AddMinutes(45), plan.FinishUtc);
        Assert.Equal(TimeSpan.FromMinutes(15), plan.Over);

        // Dropping Q3 saves 3 x (5 + 3) = 24; a minute off six qualifiers saves 6.
        Assert.Equal(2, plan.Fixes.Count);
        Assert.Equal("drop Q3 (3 races) to save 24 min", plan.Fixes[0].Text);
        Assert.Equal(3, plan.Fixes[0].DropsRound);
        Assert.Equal("run qualifiers a minute shorter to save 6 min", plan.Fixes[1].Text);

        var note = NightPlan.Note(plan);
        Assert.Contains("15 min past the", note);
        Assert.Contains("To finish in time: drop Q3", note);
    }

    [Fact]
    public void The_only_round_left_and_a_round_already_started_are_never_offered_for_dropping()
    {
        // Q2 Heat 1 has run, so Q2 can't be dropped; Q1 is never offered.
        var rounds = new[] { Heat("Q1 Heat 1"), Heat("Q2 Heat 1", run: true), Heat("Q2 Heat 2") };

        var plan = Plan(rounds, outByLocal: LocalTimeOfDay(1));

        Assert.DoesNotContain(plan.Fixes, f => f.DropsRound is not null);
    }

    [Fact]
    public void More_than_one_main_still_to_run_offers_one_fewer()
    {
        var rounds = new[] { Main("B-Main", tier: 1), Main("A-Main") };

        var plan = Plan(rounds, outByLocal: LocalTimeOfDay(1));

        Assert.Contains(plan.Fixes, f => f.Text == "run one main fewer in Stock to save 8 min");
    }

    [Fact]
    public void An_out_by_time_after_midnight_is_the_next_morning_not_this_morning()
    {
        var finishUtc = new DateTime(2026, 11, 11, 23, 50, 0, DateTimeKind.Local).ToUniversalTime();

        var outBy = NightPlan.NearestOccurrence(TimeSpan.FromMinutes(30), finishUtc);

        Assert.Equal(new DateTime(2026, 11, 12, 0, 30, 0, DateTimeKind.Local).ToUniversalTime(), outBy);
    }

    [Theory]
    [InlineData("Q2 Heat 3", 2)]
    [InlineData("q10 heat 1", 10)]
    [InlineData("Race 4 Heat 1", 4)]
    [InlineData("Race 3", 3)]
    [InlineData("Heat 2", 1)]
    [InlineData("A-Main", null)]
    [InlineData("Juniors special", null)]
    [InlineData("", null)]
    public void The_qualifying_round_is_read_from_the_label_the_program_gave(string label, int? expected)
    {
        Assert.Equal(expected, NightPlan.RoundNumber(label));
    }

    [Fact]
    public void Done_after_this_round_drops_every_later_round_still_to_run()
    {
        var rounds = new[]
        {
            Heat("Q1 Heat 1", run: true), Heat("Q1 Heat 2", run: true),
            Heat("Q2 Heat 1", run: true), Heat("Q2 Heat 2"),
            Heat("Q3 Heat 1"), Heat("Q3 Heat 2", className: "Mod"),
            Main("A-Main"),
        };

        var stop = NightPlan.AfterThisRound(rounds, onTrackRoundId: null)!;

        Assert.Equal("Q2", stop.Current.Name);
        Assert.Equal(new[] { "Q3 Heat 1", "Q3 Heat 2" }, stop.Dropped.Select(r => r.RoundLabel));
        Assert.Equal("Change of plan, everyone: qualifying finishes after Q2, and the finals are next.",
            NightPlan.Announcement(stop, clubNight: false));
    }

    [Fact]
    public void Done_after_this_round_finishes_the_round_on_track()
    {
        var rounds = new[] { Heat("Q1 Heat 1"), Heat("Q1 Heat 2"), Heat("Q2 Heat 1") };

        // Q1 Heat 2 is out first, so Q1 is the round under way.
        var stop = NightPlan.AfterThisRound(rounds, onTrackRoundId: rounds[1].Id)!;

        Assert.Equal(1, stop.Current.Number);
        Assert.Equal(new[] { rounds[2] }, stop.Dropped);
    }

    [Fact]
    public void Done_after_this_round_on_a_club_night_says_race()
    {
        var rounds = new[] { Heat("Race 1", run: true), Heat("Race 2"), Heat("Race 3") };

        var stop = NightPlan.AfterThisRound(rounds, null)!;

        Assert.Equal("Race 2", stop.Current.Name);
        Assert.Equal("Change of plan, everyone: tonight finishes after Race 2.", NightPlan.Announcement(stop, clubNight: true));
    }

    [Fact]
    public void Done_after_this_round_with_nothing_later_is_nothing()
    {
        Assert.Null(NightPlan.AfterThisRound(new[] { Heat("Q3 Heat 1"), Main("A-Main") }, null));
        Assert.Null(NightPlan.AfterThisRound(new[] { Main("A-Main") }, null));
    }

    [Fact]
    public void A_rounds_length_is_its_time_limit_plus_staging_and_countdown_or_five_minutes_for_a_lap_race()
    {
        var timed = new RaceFormat { TimeLimitMinutes = 4, StagingSeconds = 15, CountdownSeconds = 5 };
        var laps = new RaceFormat { TimeLimitMinutes = 0, LapLimit = 20 };

        Assert.Equal(TimeSpan.FromSeconds(260), NightPlan.LengthOf(timed));
        Assert.Equal(NightPlan.DefaultLength, NightPlan.LengthOf(laps));
    }

    [Fact]
    public void A_qualifier_takes_its_classs_qualifier_format_and_a_round_override_beats_both()
    {
        var raceClass = new RaceClass
        {
            Name = "Stock",
            Format = new RaceFormat { TimeLimitMinutes = 8, StagingSeconds = 0, CountdownSeconds = 0 },
            QualifierFormat = new RaceFormat { TimeLimitMinutes = 4, StagingSeconds = 0, CountdownSeconds = 0 },
        };
        var appDefault = new RaceFormat { TimeLimitMinutes = 6, StagingSeconds = 0, CountdownSeconds = 0 };

        var heat = Heat("Q1 Heat 1");
        var main = Main("A-Main");
        var overridden = Heat("Q1 Heat 2");
        overridden.FormatOverride = new RaceFormat { TimeLimitMinutes = 2, StagingSeconds = 0, CountdownSeconds = 0 };

        Assert.Equal(TimeSpan.FromMinutes(4), NightPlan.LengthOf(heat, raceClass, appDefault));
        Assert.Equal(TimeSpan.FromMinutes(8), NightPlan.LengthOf(main, raceClass, appDefault));
        Assert.Equal(TimeSpan.FromMinutes(2), NightPlan.LengthOf(overridden, raceClass, appDefault));
        Assert.Equal(TimeSpan.FromMinutes(6), NightPlan.LengthOf(main, null, appDefault));
    }

    [Theory]
    [InlineData(0, "0 min")]
    [InlineData(24, "24 min")]
    [InlineData(65, "1 h 05 min")]
    public void Savings_and_overruns_read_in_minutes(int minutes, string expected)
    {
        Assert.Equal(expected, NightPlan.Minutes(TimeSpan.FromMinutes(minutes)));
    }
}

/// <summary>Race-night build plan, Phase 2 (11 Nov 2026): what is left before lights out. See NightClose.</summary>
public sealed class NightCloseTests
{
    private static readonly DateTime LastRace = new(2026, 11, 11, 22, 0, 0, DateTimeKind.Utc);

    private static NightClose.Facts Facts(
        IReadOnlyList<string>? rentals = null, int? emails = 0, int? owed = 0, DateTime? backup = null, bool open = false) =>
        new(rentals ?? Array.Empty<string>(), emails, owed, backup ?? LastRace.AddMinutes(5), open);

    [Fact]
    public void Nothing_left_is_lights_out()
    {
        Assert.Equal(" All wrapped up - lights out.", NightClose.Note(Facts(), LastRace));
    }

    [Fact]
    public void Everything_left_is_listed_in_the_order_to_do_it()
    {
        var facts = Facts(rentals: new[] { "Sam", "Jo" }, emails: 14, owed: 3, backup: LastRace.AddHours(-3), open: true);

        Assert.Equal(
            " Before lights out: 2 rentals still out (Sam, Jo) · 3 results not on the club website yet · 14 night emails to send · back up tonight's results · Close Day.",
            NightClose.Note(facts, LastRace));
    }

    [Fact]
    public void A_backup_taken_before_the_last_race_finished_has_not_got_it()
    {
        Assert.Contains("back up tonight's results", NightClose.Left(Facts(backup: LastRace.AddMinutes(-1)), LastRace));
        Assert.DoesNotContain("back up tonight's results", NightClose.Left(Facts(backup: LastRace), LastRace));
        Assert.Contains("back up tonight's results", NightClose.Left(new NightClose.Facts(Array.Empty<string>(), 0, 0, null, false), LastRace));
    }

    [Fact]
    public void Things_the_app_cannot_tell_are_left_off()
    {
        var left = NightClose.Left(Facts(emails: null, owed: null), LastRace);

        Assert.Empty(left);
    }

    [Fact]
    public void More_than_three_names_are_cut_short()
    {
        var left = NightClose.Left(Facts(rentals: new[] { "A", "B", "C", "D" }), LastRace);

        Assert.Equal("4 rentals still out (A, B, C, ...)", left[0]);
    }
}
