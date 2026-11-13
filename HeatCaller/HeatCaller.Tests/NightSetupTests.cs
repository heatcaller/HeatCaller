namespace HeatCaller.Tests;

/// <summary>
/// Setting up the night (12 Nov 2026, setup plan step 1): entries close
/// (D1), posting the heats (D2), the cars not heard tonight (D3) and the
/// planned breaks (D5). See NightSetup.cs.
/// </summary>
public sealed class NightSetupTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 11, 19, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Five = TimeSpan.FromMinutes(5);

    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static RoundDefinition Heat(string label, string className = "Stock", bool run = false, params string[] cars) => new()
    {
        RoundLabel = label,
        ClassName = className,
        Kind = RoundKind.Qualifier,
        LinkedSessionId = run ? Guid.NewGuid().ToString("N") : null,
        AssignedTransponders = cars.ToList(),
    };

    private static RoundDefinition Main(string label, bool run = false) => new()
    {
        RoundLabel = label,
        ClassName = "Stock",
        Kind = RoundKind.Main,
        MainTier = 0,
        LinkedSessionId = run ? Guid.NewGuid().ToString("N") : null,
    };

    private static PlannedBreak After(int round, int minutes = 10, string what = "") =>
        new() { AfterRound = round, Minutes = minutes, What = what };

    // --- D5: where a break falls ------------------------------------------

    [Fact]
    public void A_break_after_Q2_falls_between_Q2_and_Q3_but_not_inside_a_round()
    {
        var breaks = new[] { After(2) };

        Assert.Single(NightBreaks.Between(Heat("Q2 Heat 4"), Heat("Q3 Heat 1"), breaks));
        Assert.Empty(NightBreaks.Between(Heat("Q2 Heat 1"), Heat("Q2 Heat 2"), breaks));
        Assert.Empty(NightBreaks.Between(Heat("Q1 Heat 3"), Heat("Q2 Heat 1"), breaks));
    }

    [Fact]
    public void A_break_after_Q2_still_comes_when_Q3_was_dropped_and_the_finals_are_next()
    {
        Assert.Single(NightBreaks.Between(Heat("Q2 Heat 4"), Main("A-Main"), new[] { After(2) }));
    }

    [Fact]
    public void A_break_before_the_finals_falls_between_the_last_qualifier_and_the_first_main()
    {
        var breaks = new[] { After(PlannedBreak.BeforeFinals, 20, "dinner") };

        Assert.Single(NightBreaks.Between(Heat("Q3 Heat 2"), Main("C-Main"), breaks));
        Assert.Empty(NightBreaks.Between(Heat("Q2 Heat 2"), Heat("Q3 Heat 1"), breaks));
        Assert.Empty(NightBreaks.Between(Main("C-Main"), Main("B-Main"), breaks));
    }

    [Fact]
    public void No_break_goes_next_to_a_heat_whose_round_cant_be_told()
    {
        var renamed = Heat("Fun race");

        Assert.Empty(NightBreaks.Between(renamed, Heat("Q3 Heat 1"), new[] { After(2) }));
        Assert.Empty(NightBreaks.Between(null, Heat("Q1 Heat 1"), new[] { After(1) }));
    }

    [Fact]
    public void The_summary_and_the_announcement_say_the_breaks_plainly()
    {
        var breaks = new List<PlannedBreak> { After(PlannedBreak.BeforeFinals, 20, "dinner"), After(2, 10, "water the track") };

        Assert.Equal("After Q2, 10 min (water the track) · Before the finals, 20 min (dinner)", NightBreaks.Summary(breaks, clubNight: false));
        Assert.Equal("After Race 2, 10 min", NightBreaks.Summary(new[] { After(2) }, clubNight: true));
        Assert.Equal("None", NightBreaks.Summary(new List<PlannedBreak>(), clubNight: false));

        var line = NightBreaks.Announcement(new[] { After(2, 10, "water the track") }, Now);
        Assert.StartsWith("Time for a 10-minute break - water the track. Racing starts again about ", line);
    }

    // --- D5: the night plan allows for them --------------------------------

    [Fact]
    public void A_planned_break_pushes_back_every_race_after_it()
    {
        var rounds = new[] { Heat("Q1 Heat 1", run: true), Heat("Q1 Heat 2"), Heat("Q2 Heat 1"), Heat("Q2 Heat 2") };

        var plan = NightPlan.For(rounds, _ => Five, null, null, null, null, null, Now, breaks: new[] { After(1, 10) })!;

        // Q1 Heat 2 now; Q2 Heat 1 after 5 + 3 and the 10-minute break.
        Assert.Equal(Now, plan.StartOf(rounds[1].Id));
        Assert.Equal(Now.AddMinutes(18), plan.StartOf(rounds[2].Id));
        Assert.Equal(Now.AddMinutes(26), plan.StartOf(rounds[3].Id));
        Assert.Equal(TimeSpan.FromMinutes(10), plan.BreakTime);
        Assert.Contains("(with 10 min of breaks)", NightPlan.Note(plan));
    }

    [Fact]
    public void A_break_after_the_round_just_finished_counts_from_its_finish()
    {
        var rounds = new[] { Heat("Q1 Heat 1", run: true), Heat("Q1 Heat 2", run: true), Heat("Q2 Heat 1") };
        var finished = Now.AddMinutes(-1);

        var plan = NightPlan.For(rounds, _ => Five, null, null, finished, null, null, Now, breaks: new[] { After(1, 10) })!;

        // The flag a minute ago, then the 3-minute gap and the 10-minute break.
        Assert.Equal(finished.AddMinutes(13), plan.StartOf(rounds[2].Id));
    }

    [Fact]
    public void A_break_before_the_finals_comes_before_mains_still_to_be_built()
    {
        var rounds = new[] { Heat("Q1 Heat 1") };
        var unbuilt = new[] { new NightPlan.UnbuiltMains("Stock", 1, Five) };

        var plan = NightPlan.For(rounds, _ => Five, unbuilt, null, null, null, null, Now,
            breaks: new[] { After(PlannedBreak.BeforeFinals, 20) })!;

        // Q1 Heat 1 now, then 5 + 3, then the 20-minute break.
        Assert.Equal(Now.AddMinutes(28), plan.Slots[^1].StartUtc);
        Assert.Equal(TimeSpan.FromMinutes(20), plan.BreakTime);
    }

    [Fact]
    public void A_night_without_breaks_plans_as_it_always_did()
    {
        var rounds = new[] { Heat("Q1 Heat 1"), Heat("Q2 Heat 1") };

        var plan = NightPlan.For(rounds, _ => Five, null, null, null, null, null, Now)!;

        Assert.Equal(Now.AddMinutes(8), plan.StartOf(rounds[1].Id));
        Assert.Equal(TimeSpan.Zero, plan.BreakTime);
        Assert.DoesNotContain("breaks", NightPlan.Note(plan));
    }

    // --- D1: entries close --------------------------------------------------

    [Fact]
    public void The_ten_minute_call_comes_inside_the_last_ten_minutes_once()
    {
        var close = Now.AddMinutes(30);

        Assert.Equal(EntriesClose.Call.None, EntriesClose.Due(close, Now, racingStarted: false, saidSoon: false, saidClosed: false));
        Assert.Equal(EntriesClose.Call.Soon, EntriesClose.Due(close, close.AddMinutes(-10), false, false, false));
        Assert.Equal(EntriesClose.Call.None, EntriesClose.Due(close, close.AddMinutes(-9), false, saidSoon: true, saidClosed: false));

        // Inside the last minute it waits for the closed call instead.
        Assert.Equal(EntriesClose.Call.None, EntriesClose.Due(close, close.AddSeconds(-30), false, false, false));
    }

    [Fact]
    public void The_closed_call_comes_as_it_passes_but_not_long_after()
    {
        var close = Now;

        Assert.Equal(EntriesClose.Call.Closed, EntriesClose.Due(close, close.AddSeconds(10), false, false, false));
        Assert.Equal(EntriesClose.Call.None, EntriesClose.Due(close, close.AddSeconds(20), false, false, saidClosed: true));
        Assert.Equal(EntriesClose.Call.None, EntriesClose.Due(close, close + EntriesClose.ClosedCallFor, false, false, false));
    }

    [Fact]
    public void No_call_once_the_racing_has_started()
    {
        Assert.Equal(EntriesClose.Call.None, EntriesClose.Due(Now, Now.AddSeconds(5), racingStarted: true, false, false));
        Assert.Equal(EntriesClose.Call.None, EntriesClose.Due(Now.AddMinutes(5), Now, racingStarted: true, false, false));
    }

    [Fact]
    public void The_lines_say_how_long_is_left_and_that_it_has_closed()
    {
        Assert.StartsWith("Entries close in 10 minutes.", EntriesClose.Line(EntriesClose.Call.Soon, Now.AddMinutes(10), Now));
        Assert.StartsWith("Entries are now closed.", EntriesClose.Line(EntriesClose.Call.Closed, Now, Now));

        Assert.Contains("in 12 min", EntriesClose.Note(Now.AddMinutes(12), Now));
        Assert.StartsWith("Entries closed at", EntriesClose.Note(Now, Now.AddMinutes(1)));
    }

    [Fact]
    public void A_day_with_no_close_time_has_no_close_and_no_late_line()
    {
        var day = new RaceEvent { Name = "Tonight" };

        Assert.Null(EntriesClose.AtUtc(day, Now));
        Assert.Null(EntriesClose.LateLine(day, Now));

        day.EntriesCloseLocalTime = Now.AddMinutes(-5).ToLocalTime().TimeOfDay;
        Assert.NotNull(EntriesClose.LateLine(day, Now));
    }

    // --- D2: posting the heats ------------------------------------------

    [Fact]
    public void Built_heats_with_nothing_run_are_owed_a_posting_once()
    {
        var day = new RaceEvent { Name = "Tonight" };
        var rounds = new List<RoundDefinition> { Heat("Q1 Heat 1"), Heat("Q1 Heat 2") };

        Assert.True(HeatPosting.Owed(day, rounds, clubPostsHeats: true));
        Assert.False(HeatPosting.Owed(day, rounds, clubPostsHeats: false));
        Assert.False(HeatPosting.Owed(day, new List<RoundDefinition>(), true));

        day.HeatsPostedUtc = Now;
        Assert.False(HeatPosting.Owed(day, rounds, true));
        Assert.True(HeatPosting.Posted(day));
    }

    [Fact]
    public void Once_a_race_has_run_the_heats_are_out_and_no_posting_is_asked_for()
    {
        var day = new RaceEvent { Name = "Tonight" };
        var rounds = new List<RoundDefinition> { Heat("Q1 Heat 1", run: true), Heat("Q1 Heat 2") };

        Assert.False(HeatPosting.Owed(day, rounds, true));
    }

    [Fact]
    public void The_question_before_changing_posted_heats_says_when_they_were_posted()
    {
        var day = new RaceEvent { Name = "Tonight", HeatsPostedUtc = Now };
        var question = HeatPosting.ChangeQuestion(day, "This moves drivers in 2 heats of Stock.");

        Assert.Contains(AppTime.TimeOfDay(Now.ToLocalTime()), question);
        Assert.Contains("This moves drivers in 2 heats of Stock.", question);
        Assert.Contains("Add a Late Entry", question);
    }

    // --- D3: the cars not heard tonight --------------------------------------

    [Fact]
    public void Not_heard_lists_each_car_still_to_race_that_the_loop_has_not_heard()
    {
        var rounds = new[]
        {
            Heat("Q1 Heat 1", run: true, cars: new[] { "100" }),
            Heat("Q1 Heat 2", cars: new[] { "200", "300" }),
            Heat("Q2 Heat 1", cars: new[] { "300", "400" }),
        };

        var heard = new HashSet<string> { "200" };

        // 100 has raced already; 300 is listed once; in program order.
        Assert.Equal(new[] { "300", "400" }, HeatPosting.NotHeard(rounds, heard.Contains));
    }

    [Fact]
    public void Not_heard_says_who_in_a_line_and_nothing_when_everyone_is_in()
    {
        Assert.Equal(string.Empty, HeatPosting.NotHeardWords(Array.Empty<string>()));
        Assert.StartsWith("1 car on tonight's heats hasn't crossed the loop tonight: Al.", HeatPosting.NotHeardWords(new[] { "Al" }));
        Assert.StartsWith("4 cars on tonight's heats haven't crossed the loop tonight: Al, Rhoda, Hugh and 1 more.",
            HeatPosting.NotHeardWords(new[] { "Al", "Rhoda", "Hugh", "Axel" }));
    }

    [Fact]
    public void A_line_pass_counts_as_heard_from_then_on()
    {
        var transponder = "night-setup-" + Guid.NewGuid().ToString("N");

        Assert.False(TonightsSightings.HeardSince(transponder, Now.AddHours(-1)));

        TonightsSightings.Note(transponder, Now);

        Assert.True(TonightsSightings.HeardSince(transponder, Now.AddHours(-1)));
        Assert.False(TonightsSightings.HeardSince(transponder, Now.AddMinutes(1)));
    }

    [Fact]
    public void Tonight_starts_at_six_in_the_morning_so_a_night_past_midnight_is_one_night()
    {
        var evening = new DateTime(2026, 11, 11, 21, 0, 0, DateTimeKind.Local);
        var afterMidnight = new DateTime(2026, 11, 12, 0, 30, 0, DateTimeKind.Local);
        var sixAm = new DateTime(2026, 11, 11, 6, 0, 0, DateTimeKind.Local).ToUniversalTime();

        Assert.Equal(sixAm, TonightsSightings.NightStartUtc(evening));
        Assert.Equal(sixAm, TonightsSightings.NightStartUtc(afterMidnight));
    }

    [Fact]
    public void Races_saved_tonight_count_timed_laps_but_not_hand_counted_ones()
    {
        var tonight = new SessionRecord
        {
            EndedAtUtc = Now,
            Results =
            {
                new SessionResultEntry { Transponder = "500", LapTimesMs = { 21000, 20500 } },
                new SessionResultEntry { Transponder = "600" },
            },
        };

        var lastWeek = new SessionRecord
        {
            EndedAtUtc = Now.AddDays(-7),
            Results = { new SessionResultEntry { Transponder = "700", LapTimesMs = { 21000 } } },
        };

        var heard = TonightsSightings.InSessions(new[] { tonight, lastWeek }, Now.AddHours(-6));

        Assert.Equal(new[] { "500" }, heard.ToArray());
    }

    // --- Carried over from the last day at the venue -------------------------

    [Fact]
    public void A_new_day_at_the_same_venue_keeps_the_entries_close_and_the_breaks()
    {
        var store = RaceEventStore.Load();
        var lastWeek = EventSpine.StartEvent(store, "Last week", venueName: "Clubhouse");
        lastWeek.EntriesCloseLocalTime = TimeSpan.FromHours(18.5);
        lastWeek.Breaks = new List<PlannedBreak> { After(2, 10, "water the track") };
        store.Upsert(lastWeek);

        var tonight = EventSpine.StartEvent(store, "Tonight", venueName: "Clubhouse");

        Assert.Equal(TimeSpan.FromHours(18.5), tonight.EntriesCloseLocalTime);
        Assert.Single(tonight.Breaks);
        Assert.Equal("water the track", tonight.Breaks[0].What);
        Assert.Null(tonight.HeatsPostedUtc);

        // A copy, not the same list: changing tonight's leaves last week's.
        tonight.Breaks[0].Minutes = 15;
        Assert.Equal(10, store.Get(lastWeek.Id)!.Breaks[0].Minutes);

        var elsewhere = EventSpine.StartEvent(store, "Away", venueName: "Other track");
        Assert.Null(elsewhere.EntriesCloseLocalTime);
        Assert.Empty(elsewhere.Breaks);
    }
}
