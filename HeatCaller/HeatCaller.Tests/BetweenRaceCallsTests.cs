namespace HeatCaller.Tests;

/// <summary>
/// The gap between races (31 Oct 2026, Asher: "options to control how much
/// it talks, how frequently, when it does, what it says") - the rules the
/// Between races card saves, the gap that keeps them, the words, the presets
/// and Hear the gap. All pure: no window, no voice.
/// </summary>
public sealed class BetweenRaceCallsTests
{
    private static readonly DateTime T0 = new(2026, 10, 31, 19, 0, 0, DateTimeKind.Utc);

    private static GapFacts Next(DateTime? due = null, int marshals = 2, int notIn = 0) => new()
    {
        NextId = "r3",
        Round = "Heat 3",
        ClassName = "Box Stock",
        Drivers = new[] { "Sam", "Bill", "Frank" },
        Marshals = Enumerable.Range(1, marshals).Select(i => ($"Marshal {i}", $"Turn {i}")).ToList(),
        NotCheckedIn = Enumerable.Range(1, notIn).Select(i => $"Late {i}").ToList(),
        TwoOutRound = "Heat 4",
        TwoOutId = "r4",
        Track = "the club track",
        DueUtc = due,
    };

    /// <summary>A gap opened at T0 with the floor free from T0 and the next race set.</summary>
    private static GapState OpenGap(GapFacts facts)
    {
        var gap = new GapState();
        gap.Open("race-1", T0);
        gap.NextRace(facts.NextId);
        gap.FloorFree(T0);
        return gap;
    }

    // ---------------------------------------------------------------- rules

    [Fact]
    public void A_club_that_never_opens_the_card_gets_the_Race_day_defaults()
    {
        var rules = GapRules.From(new AppSettings());

        Assert.Equal(GapLevel.Normal, rules.Level);
        Assert.Equal(GapLineLength.Medium, rules.Length);
        Assert.Equal(4, rules.MostLines);
        Assert.Equal(30, rules.SpaceSeconds);
        Assert.Equal(120, rules.CallUpRepeatSeconds);
        Assert.Equal(3, rules.CallUpsPerRace);
        Assert.True(rules.CallUps);
        Assert.True(rules.Marshals);
        Assert.False(rules.TwoOut);
        Assert.False(rules.NotCheckedIn);
        Assert.False(rules.AutoCountdown);
        Assert.Equal(new[] { 5, 1 }, rules.CountdownMarks);
        Assert.Same(GapPreset.RaceDay, GapPreset.Matching(rules));
    }

    [Fact]
    public void The_rules_go_back_into_the_settings_and_come_out_the_same()
    {
        var settings = new AppSettings();
        var rules = GapRules.Default with
        {
            Level = GapLevel.Busy,
            Length = GapLineLength.Long,
            MostLines = 7,
            SpaceSeconds = 45,
            CallUpRepeatSeconds = 0,
            CallUpsPerRace = 2,
            AutoCountdown = true,
            CountdownMarks = new[] { 10, 2 },
            CountdownTimesUp = true,
            TwoOut = true,
            CustomLines = new[] { "Pit lane is closed.", "{round} is next." },
        };

        rules.SaveTo(settings);
        var back = GapRules.From(settings);

        Assert.Equal(GapLevel.Busy, back.Level);
        Assert.Equal(GapLineLength.Long, back.Length);
        Assert.Equal(7, back.MostLines);
        Assert.Equal(45, back.SpaceSeconds);
        Assert.Equal(0, back.CallUpRepeatSeconds);
        Assert.Equal(2, back.CallUpsPerRace);
        Assert.True(back.AutoCountdown);
        Assert.Equal(new[] { 10, 2 }, back.CountdownMarks);
        Assert.True(back.CountdownTimesUp);
        Assert.True(back.TwoOut);
        Assert.Equal(rules.CustomLines, back.CustomLines);
    }

    [Fact]
    public void Settings_out_of_range_are_brought_back_inside_the_cards_limits()
    {
        var settings = new AppSettings
        {
            BetweenRacesMostLines = 99,
            BetweenRacesSpaceSeconds = 1,
            BetweenRacesCallUpRepeatSeconds = 5,
            BetweenRacesCallUpsPerRace = 0,
            BetweenRacesLevel = "shouting",
            BetweenRacesCountdownMarks = "7, 5, x, 1, 5",
        };

        var rules = GapRules.From(settings);

        Assert.Equal(GapRules.MostLinesAllowed, rules.MostLines);
        Assert.Equal(GapRules.ShortestSpace, rules.SpaceSeconds);
        Assert.Equal(GapRules.ShortestRepeat, rules.CallUpRepeatSeconds);
        Assert.Equal(GapRules.FewestCallUps, rules.CallUpsPerRace);
        Assert.Equal(GapLevel.Normal, rules.Level);
        Assert.Equal(new[] { 5, 1 }, rules.CountdownMarks);
    }

    [Fact]
    public void Quiet_keeps_the_calls_only_and_Light_adds_the_hand_over_and_notices()
    {
        var quiet = GapRules.Default with { Level = GapLevel.Quiet, CustomLines = new[] { "Pit lane is closed." } };
        var light = quiet with { Level = GapLevel.Light };

        Assert.True(quiet.Allows(GapLine.CallUp));
        Assert.True(quiet.Allows(GapLine.Marshals));
        Assert.True(quiet.Allows(GapLine.Countdown));
        Assert.False(quiet.Allows(GapLine.HandOver));
        Assert.False(quiet.Allows(GapLine.Segment));
        Assert.False(quiet.Allows(GapLine.Sponsor));
        Assert.False(quiet.Allows(GapLine.Notice));

        Assert.True(light.Allows(GapLine.HandOver));
        Assert.True(light.Allows(GapLine.Notice));
        Assert.False(light.Allows(GapLine.Segment));
        Assert.False(light.Allows(GapLine.ComingUpNext));
    }

    [Fact]
    public void An_unticked_box_keeps_its_line_out_at_every_level()
    {
        var rules = GapRules.Default with { Level = GapLevel.Busy, CallUps = false, Segments = false };

        Assert.False(rules.Allows(GapLine.CallUp));
        Assert.False(rules.Allows(GapLine.Segment));
        Assert.True(rules.Allows(GapLine.ComingUpNext));
    }

    [Fact]
    public void Busy_gives_two_more_lines_and_halves_the_sponsor_spacing_to_no_less_than_a_minute()
    {
        var normal = GapRules.Default with { MostLines = 4, SponsorEveryMinutes = 4 };
        var busy = normal with { Level = GapLevel.Busy };

        Assert.Equal(4, normal.LinesAllowed);
        Assert.Equal(6, busy.LinesAllowed);
        Assert.Equal(TimeSpan.FromMinutes(4), normal.SponsorSpacing);
        Assert.Equal(TimeSpan.FromMinutes(2), busy.SponsorSpacing);
        Assert.Equal(TimeSpan.FromMinutes(1), (busy with { SponsorEveryMinutes = 1 }).SponsorSpacing);
    }

    [Fact]
    public void Line_length_tells_the_model_only_when_it_is_not_Medium()
    {
        Assert.Equal(string.Empty, GapRules.Default.LengthBrief);
        Assert.Equal(0, GapRules.Default.LengthExtraWords);
        Assert.Contains("ONE short sentence", (GapRules.Default with { Length = GapLineLength.Short }).LengthBrief, StringComparison.Ordinal);
        Assert.True((GapRules.Default with { Length = GapLineLength.Long }).LengthExtraWords > 0);
    }

    // -------------------------------------------------------------- presets

    [Fact]
    public void Every_preset_is_found_by_its_key_and_matches_what_it_sets()
    {
        foreach (var preset in GapPreset.All)
        {
            Assert.Same(preset, GapPreset.Find(preset.Key));
            Assert.Same(preset, GapPreset.Matching(preset.Over(GapRules.Default)));
        }

        Assert.Null(GapPreset.Find(string.Empty));
        Assert.Null(GapPreset.Find("nonsense"));
    }

    [Fact]
    public void A_preset_keeps_the_clubs_own_lines()
    {
        var own = GapRules.Default with { CustomLines = new[] { "Pit lane is closed." } };

        foreach (var preset in GapPreset.All)
        {
            Assert.Equal(own.CustomLines, preset.Over(own).CustomLines);
        }
    }

    [Fact]
    public void A_race_days_preset_goes_over_the_clubs_settings_for_that_day()
    {
        var settings = new AppSettings { BetweenRacesLevel = "quiet" };

        Assert.Equal(GapLevel.Quiet, GapRules.From(settings).Level);
        Assert.Equal(GapLevel.Busy, GapRules.From(settings, "big").Level);
        Assert.Equal(GapLevel.Quiet, GapRules.From(settings, "not-a-preset").Level);
    }

    [Fact]
    public void Changing_one_answer_off_a_preset_reads_as_Custom()
    {
        Assert.Null(GapPreset.Matching(GapRules.Default with { SpaceSeconds = 55 }));
    }

    // ------------------------------------------------------------------ gap

    [Fact]
    public void Nothing_is_said_with_no_race_finished()
    {
        var gap = new GapState();

        Assert.Equal("no race has finished", gap.WhyNot(GapLine.CallUp, GapRules.Default, T0, Next(), false, false));
        Assert.Null(gap.WhyNotForTheDirector(GapLine.Segment, T0));
    }

    [Fact]
    public void Hold_Talk_stops_everything_in_the_gap()
    {
        var facts = Next();
        var gap = OpenGap(facts);

        foreach (var kind in Enum.GetValues<GapLine>())
        {
            Assert.Equal("between-race talk is held", gap.WhyNot(kind, GapRules.Default, T0.AddMinutes(1), facts, held: true, false));
        }

        Assert.Null(gap.MarkDue(GapRules.Default with { AutoCountdown = true }, T0, facts with { DueUtc = T0.AddMinutes(5) }, held: true));
    }

    [Fact]
    public void Nothing_goes_while_something_is_on_air_or_inside_the_quiet_after_the_recap()
    {
        var facts = Next();
        var gap = new GapState();
        gap.Open("race-1", T0);
        gap.NextRace(facts.NextId);
        gap.FloorBusy();

        Assert.Equal("something is on air", gap.WhyNot(GapLine.CallUp, GapRules.Default, T0, facts, false, false));

        gap.FloorFree(T0);

        Assert.Equal("inside the quiet after the recap", gap.WhyNot(GapLine.CallUp, GapRules.Default, T0.AddSeconds(5), facts, false, false));
        Assert.Null(gap.WhyNot(GapLine.CallUp, GapRules.Default, T0.AddSeconds(10), facts, false, false));
    }

    [Fact]
    public void Nothing_but_the_countdown_goes_inside_the_quiet_before_the_call()
    {
        var facts = Next(due: T0.AddSeconds(25));
        var gap = OpenGap(facts);

        Assert.Equal("inside the quiet before the call", gap.WhyNot(GapLine.CallUp, GapRules.Default, T0.AddSeconds(10), facts, false, false));
        Assert.NotEqual("inside the quiet before the call", gap.WhyNot(GapLine.Countdown, GapRules.Default, T0.AddSeconds(10), facts, false, false));
    }

    [Fact]
    public void The_call_up_goes_first_and_the_marshals_straight_after_it()
    {
        var facts = Next();
        var gap = OpenGap(facts);
        var now = T0.AddSeconds(10);

        Assert.Equal(GapLine.CallUp, gap.NextCall(GapRules.Default, now, facts, false, false, out _));
        Assert.Equal("the call-up goes first", gap.WhyNot(GapLine.Marshals, GapRules.Default, now, facts, false, false));
        Assert.Equal("the call-up goes first", gap.WhyNot(GapLine.Segment, GapRules.Default, now, facts, false, false));

        gap.Said(GapLine.CallUp, now);

        // Attached to the call-up: no spacing between the two.
        Assert.Equal(GapLine.Marshals, gap.NextCall(GapRules.Default, now.AddSeconds(4), facts, false, false, out _));
    }

    [Fact]
    public void Lines_keep_their_spacing_and_the_gap_its_cap_but_calls_are_never_capped()
    {
        var facts = Next(marshals: 0);
        var rules = GapRules.Default with { MostLines = 2, SpaceSeconds = 30, CallUpRepeatSeconds = 60 };
        var gap = OpenGap(facts);

        gap.Said(GapLine.CallUp, T0.AddSeconds(10));

        Assert.StartsWith("only ", gap.WhyNot(GapLine.Segment, rules, T0.AddSeconds(20), facts, false, false));
        Assert.Null(gap.WhyNot(GapLine.Segment, rules, T0.AddSeconds(40), facts, false, false));

        gap.Said(GapLine.Segment, T0.AddSeconds(40));

        Assert.Equal("the gap has had its 2 lines", gap.WhyNot(GapLine.ComingUpNext, rules, T0.AddSeconds(90), facts, false, false));

        // The repeat call-up still goes: the cap is for talk, not instructions.
        Assert.Null(gap.WhyNot(GapLine.CallUp, rules, T0.AddSeconds(90), facts, false, false));
    }

    [Fact]
    public void The_call_up_repeats_on_its_own_time_up_to_the_most_per_race()
    {
        var facts = Next(marshals: 0);
        var rules = GapRules.Default with { CallUpRepeatSeconds = 60, CallUpsPerRace = 2 };
        var gap = OpenGap(facts);

        gap.Said(GapLine.CallUp, T0.AddSeconds(10));

        Assert.StartsWith("next repeat in", gap.WhyNot(GapLine.CallUp, rules, T0.AddSeconds(50), facts, false, false));
        Assert.Null(gap.WhyNot(GapLine.CallUp, rules, T0.AddSeconds(70), facts, false, false));

        gap.Said(GapLine.CallUp, T0.AddSeconds(70));

        Assert.Contains("the most Between races allows", gap.WhyNot(GapLine.CallUp, rules, T0.AddMinutes(10), facts, false, false));
    }

    [Fact]
    public void Repeat_Never_calls_the_race_once()
    {
        var facts = Next();
        var gap = OpenGap(facts);
        gap.Said(GapLine.CallUp, T0.AddSeconds(10));

        Assert.Equal("called once, and Repeat the call-up is Never",
            gap.WhyNot(GapLine.CallUp, GapRules.Default with { CallUpRepeatSeconds = 0 }, T0.AddMinutes(5), facts, false, false));
    }

    [Fact]
    public void A_new_next_race_starts_its_call_ups_from_nothing()
    {
        var facts = Next();
        var gap = OpenGap(facts);
        gap.Said(GapLine.CallUp, T0.AddSeconds(10));

        Assert.Equal(1, gap.CallUps);

        gap.NextRace("r4");

        Assert.Equal(0, gap.CallUps);
    }

    [Fact]
    public void Skip_Next_Call_up_counts_as_said_and_Push_back_moves_the_next_one()
    {
        var facts = Next(marshals: 0);
        var rules = GapRules.Default with { CallUpRepeatSeconds = 60 };
        var gap = OpenGap(facts);

        gap.SkipCallUp(T0.AddSeconds(10));

        Assert.Equal(1, gap.CallUps);
        Assert.StartsWith("next repeat in", gap.WhyNot(GapLine.CallUp, rules, T0.AddSeconds(30), facts, false, false));

        gap.PushBack(T0.AddSeconds(80));

        Assert.Equal(1, gap.PushedBackMinutes);
        Assert.Equal("the race was pushed back", gap.WhyNot(GapLine.CallUp, rules, T0.AddSeconds(100), facts, false, false));
        Assert.Null(gap.WhyNot(GapLine.CallUp, rules, T0.AddSeconds(141), facts, false, false));
    }

    [Fact]
    public void With_On_Air_opening_the_next_race_the_director_stands_down_and_the_calls_follow_the_tick()
    {
        var facts = Next();
        var gap = OpenGap(facts);
        var now = T0.AddSeconds(15);

        Assert.Equal("On Air opens the next race and says it there", gap.WhyNot(GapLine.HandOver, GapRules.Default, now, facts, false, onAirHasTheGap: true));
        Assert.Null(gap.WhyNot(GapLine.CallUp, GapRules.Default, now, facts, false, onAirHasTheGap: true));
        Assert.NotNull(gap.WhyNot(GapLine.CallUp, GapRules.Default with { CallUpsWithOnAir = false }, now, facts, false, onAirHasTheGap: true));
    }

    [Fact]
    public void Sponsor_reads_keep_their_spacing_across_gaps()
    {
        var facts = Next();
        var rules = GapRules.Default with { SponsorEveryMinutes = 2, CallUps = false };
        var gap = OpenGap(facts);

        gap.Said(GapLine.Sponsor, T0.AddSeconds(20));
        gap.Close();
        gap.Open("race-2", T0.AddSeconds(60));
        gap.FloorFree(T0.AddSeconds(60));

        Assert.StartsWith("a sponsor read went out", gap.WhyNot(GapLine.Sponsor, rules, T0.AddSeconds(90), facts, false, false));
        Assert.Null(gap.WhyNot(GapLine.Sponsor, rules, T0.AddSeconds(141), facts, false, false));
    }

    [Fact]
    public void Two_races_out_and_not_checked_in_wait_for_the_call_up_and_go_once()
    {
        var facts = Next(marshals: 0, notIn: 1);
        var rules = GapRules.Default with { TwoOut = true, NotCheckedIn = true, SpaceSeconds = 10 };
        var gap = OpenGap(facts);

        Assert.Equal("the call-up goes first", gap.WhyNot(GapLine.TwoOut, rules, T0.AddSeconds(15), facts, false, false));

        gap.Said(GapLine.CallUp, T0.AddSeconds(15));
        Assert.Equal(GapLine.NotCheckedIn, gap.NextCall(rules, T0.AddSeconds(30), facts, false, false, out _));

        gap.Said(GapLine.NotCheckedIn, T0.AddSeconds(30));
        Assert.Equal(GapLine.TwoOut, gap.NextCall(rules, T0.AddSeconds(45), facts, false, false, out _));

        gap.SaidTwoOut("r4", T0.AddSeconds(45));
        Assert.Equal("already said for that race", gap.WhyNot(GapLine.TwoOut, rules, T0.AddSeconds(90), facts, false, false));
    }

    [Fact]
    public void A_long_break_is_announced_once_and_the_call_up_waits_until_racing_is_near()
    {
        var facts = Next(due: T0.AddMinutes(20));
        var gap = OpenGap(facts);
        var now = T0.AddSeconds(15);

        Assert.Equal(GapLine.LongBreak, gap.NextCall(GapRules.Default, now, facts, false, false, out _));

        gap.Said(GapLine.LongBreak, now);

        Assert.Equal("a long break - the call-up waits until racing is near",
            gap.WhyNot(GapLine.CallUp, GapRules.Default, now.AddMinutes(1), facts, false, false));
        Assert.Null(gap.WhyNot(GapLine.CallUp, GapRules.Default, T0.AddMinutes(16), facts, false, false));
    }

    [Fact]
    public void The_automatic_countdown_calls_each_mark_once_and_lets_a_missed_one_go()
    {
        var facts = Next(due: T0.AddMinutes(6));
        var rules = GapRules.Default with { AutoCountdown = true, CountdownMarks = new[] { 5, 1 }, CountdownTimesUp = true };
        var gap = OpenGap(facts);

        Assert.Null(gap.MarkDue(rules, T0.AddSeconds(30), facts, false));
        Assert.Equal(5, gap.MarkDue(rules, T0.AddMinutes(1).AddSeconds(5), facts, false));

        gap.SaidMark(5, T0.AddMinutes(1).AddSeconds(5));

        Assert.Null(gap.MarkDue(rules, T0.AddMinutes(1).AddSeconds(10), facts, false));

        // The 1-minute mark missed under a long line: let go, not said late.
        Assert.Null(gap.MarkDue(rules, T0.AddMinutes(5).AddSeconds(50), facts, false));
        Assert.Equal(0, gap.MarkDue(rules, T0.AddMinutes(6).AddSeconds(2), facts, false));
    }

    [Fact]
    public void The_countdown_is_off_unless_it_is_ticked_and_the_run_sheet_has_a_time()
    {
        var gap = OpenGap(Next());

        Assert.Null(gap.MarkDue(GapRules.Default, T0.AddMinutes(1), Next(due: T0.AddMinutes(6)), false));
        Assert.Null(gap.MarkDue(GapRules.Default with { AutoCountdown = true }, T0.AddMinutes(1), Next(due: null), false));
    }

    [Fact]
    public void The_director_is_judged_by_what_Race_Control_published_and_only_in_an_open_gap()
    {
        var facts = Next();
        var gap = OpenGap(facts);
        gap.Publish(GapRules.Default with { Segments = false }, facts, held: false, onAirHasTheGap: false);
        gap.Said(GapLine.CallUp, T0.AddSeconds(10));

        Assert.Equal("its box is unticked on Between races", gap.WhyNotForTheDirector(GapLine.Segment, T0.AddMinutes(2)));
        Assert.Null(gap.WhyNotForTheDirector(GapLine.ComingUpNext, T0.AddMinutes(2)));

        gap.Close();

        Assert.Null(gap.WhyNotForTheDirector(GapLine.Segment, T0.AddMinutes(2)));
    }

    [Fact]
    public void The_line_length_reaches_the_filler_but_never_the_recap_show_or_a_race()
    {
        var facts = Next();
        var gap = new GapState();
        gap.Open("race-1", T0);
        gap.Publish(GapRules.Default with { Length = GapLineLength.Short }, facts, false, false);

        // The recap show holds the floor.
        gap.FloorBusy();
        Assert.Equal((string.Empty, 0), gap.LengthFor(SegmentKind.FormGuide));

        gap.FloorFree(T0.AddSeconds(30));
        Assert.Contains("ONE short sentence", gap.LengthFor(SegmentKind.FormGuide).Brief, StringComparison.Ordinal);
        Assert.Equal(string.Empty, gap.LengthFor(SegmentKind.BattleWatch).Brief);
    }

    [Fact]
    public void The_directors_kinds_are_counted_as_the_card_names_them()
    {
        Assert.Equal(GapLine.ComingUpNext, GapState.LineFor(SegmentKind.ComingUpNext));
        Assert.Equal(GapLine.ClubNote, GapState.LineFor(SegmentKind.ClubAside));
        Assert.Equal(GapLine.Segment, GapState.LineFor(SegmentKind.FormGuide));
    }

    [Fact]
    public void A_line_before_the_first_race_does_not_count_toward_the_first_gap()
    {
        var gap = new GapState();
        gap.SaidByTheDirector(GapLine.Segment, T0);
        gap.Open("race-1", T0);

        Assert.Equal(0, gap.Lines);
    }

    [Fact]
    public void Each_refusal_is_told_once_a_gap()
    {
        var gap = OpenGap(Next());

        Assert.True(gap.FirstTimeTold("inside the quiet before the call"));
        Assert.False(gap.FirstTimeTold("inside the quiet before the call"));

        gap.Close();
        gap.Open("race-2", T0);

        Assert.True(gap.FirstTimeTold("inside the quiet before the call"));
    }

    [Fact]
    public void The_Broadcast_window_says_when_the_call_up_is_due()
    {
        var facts = Next();
        var gap = OpenGap(facts);

        Assert.Equal("Call-up for Heat 3 in 5s", gap.DueNext(GapRules.Default, T0.AddSeconds(5), facts, false, false));
        Assert.Equal("Call-up for Heat 3 now", gap.DueNext(GapRules.Default, T0.AddSeconds(10), facts, false, false));
        Assert.StartsWith("Between-race talk held", gap.DueNext(GapRules.Default, T0.AddSeconds(10), facts, true, false));
        Assert.Equal(string.Empty, new GapState().DueNext(GapRules.Default, T0, facts, false, false));
    }

    // ---------------------------------------------------------------- words

    [Fact]
    public void The_built_in_call_up_names_the_race_and_its_class()
    {
        var first = GapWords.CallUp(Next(), GapRules.Default, repeat: false, T0);
        var repeat = GapWords.CallUp(Next(), GapRules.Default, repeat: true, T0);

        Assert.Contains("Heat 3 for Box Stock", first, StringComparison.Ordinal);
        Assert.Contains("Heat 3 for Box Stock", repeat, StringComparison.Ordinal);
        Assert.DoesNotContain("{", first, StringComparison.Ordinal);
    }

    [Fact]
    public void The_marshals_are_called_by_post_or_by_name()
    {
        Assert.Equal("Marshals for Heat 3: Marshal 1 on Turn 1, Marshal 2 on Turn 2.",
            GapWords.Marshals(Next(), GapRules.Default, T0));

        var noPosts = Next() with { Marshals = new[] { ("Jo", ""), ("Pat", "") } };
        Assert.StartsWith("Marshals for Heat 3: Jo", GapWords.Marshals(noPosts, GapRules.Default, T0), StringComparison.Ordinal);
    }

    [Fact]
    public void More_than_four_not_checked_in_is_a_count_not_a_roll_call()
    {
        Assert.Contains("Late 1, check in", GapWords.NotCheckedIn(Next(notIn: 1)), StringComparison.Ordinal);
        Assert.Contains("6 drivers", GapWords.NotCheckedIn(Next(notIn: 6)), StringComparison.Ordinal);
    }

    [Fact]
    public void The_countdown_marks_are_the_Timetables_own_words()
    {
        Assert.Equal("5 minutes to Heat 3.", GapWords.Mark(5, "Heat 3"));
        Assert.Equal("1 minute to Heat 3.", GapWords.Mark(1, "Heat 3"));
        Assert.Equal("Time's up for Heat 3!", GapWords.Mark(0, "Heat 3"));
    }

    [Fact]
    public void The_clubs_lines_are_sorted_by_their_placeholders()
    {
        var sorted = GapWords.Sort(new[]
        {
            "{round} for {class} is next.",
            "Marshals for {round}: {marshals}.",
            "Racing resumes in about {minutes} minutes.",
            "Pit lane is closed.",
        });

        Assert.Single(sorted.CallUps);
        Assert.Single(sorted.MarshalCalls);
        Assert.Equal(2, sorted.Notices.Count);
    }

    [Fact]
    public void A_clubs_own_call_up_takes_the_place_of_the_built_in_ones()
    {
        var rules = GapRules.Default with { CustomLines = new[] { "{round} for {class}, to the stand: {drivers}." } };

        Assert.Equal("Heat 3 for Box Stock, to the stand: Sam, Bill and Frank.", GapWords.CallUp(Next(), rules, false, T0));
    }

    [Fact]
    public void A_line_whose_placeholder_has_nothing_behind_it_is_not_said()
    {
        Assert.Equal(string.Empty, GapWords.Fill("Marshals for {round}: {marshals}.", Next(marshals: 0), T0));
        Assert.Equal(string.Empty, GapWords.Fill("Racing resumes in about {minutes} minutes.", Next(due: null), T0));
        Assert.Equal("Racing resumes in about 12 minutes.", GapWords.Fill("Racing resumes in about {minutes} minutes.", Next(due: T0.AddMinutes(12)), T0));
    }

    [Fact]
    public void Every_example_fills_from_the_sample_race()
    {
        var sample = GapPreview.Sample(T0, TimeSpan.FromMinutes(4));

        foreach (var example in GapWords.Examples)
        {
            Assert.NotEqual(string.Empty, GapWords.Fill(example, sample, T0));
        }
    }

    [Fact]
    public void Durations_read_as_the_Broadcast_window_shows_them()
    {
        Assert.Equal("40s", GapWords.Duration(40));
        Assert.Equal("2 min", GapWords.Duration(120));
        Assert.Equal("2 min 10s", GapWords.Duration(130));
    }

    // ---------------------------------------------------------- hear the gap

    [Fact]
    public void Hear_the_gap_plays_the_call_up_and_its_marshals_and_keeps_to_the_cap()
    {
        var rules = GapRules.Default;
        var facts = GapPreview.Sample(T0, TimeSpan.FromMinutes(4));

        var steps = GapPreview.Run(rules, facts, T0, withTheDirector: true, commentaryOn: true, sponsorReads: true, onAirOpensTheNextRace: false).ToList();

        var call = steps.FindIndex(s => s.Kind == GapLine.CallUp);
        Assert.True(call >= 0, GapPreview.RunningOrder(steps));
        Assert.Equal(GapLine.Marshals, steps[call + 1].Kind);

        var talk = steps.Count(s => !GapRules.IsACall(s.Kind));
        Assert.True(talk <= rules.LinesAllowed, GapPreview.RunningOrder(steps));

        // Nothing before the quiet after the recap is over.
        Assert.All(steps, s => Assert.True(s.At >= TimeSpan.FromSeconds(rules.QuietAfterRecapSeconds)));
    }

    [Fact]
    public void Hear_the_gap_on_Just_the_call_ups_has_only_calls_in_it()
    {
        var rules = GapPreset.JustCallUps.Over(GapRules.Default);
        var facts = GapPreview.Sample(T0, TimeSpan.FromMinutes(4));

        var steps = GapPreview.Run(rules, facts, T0, true, true, true, false);

        Assert.NotEmpty(steps);
        Assert.All(steps, s => Assert.True(GapRules.IsACall(s.Kind), $"{s.Kind} in {GapPreview.RunningOrder(steps)}"));
    }

    [Fact]
    public void The_running_order_reads_as_minutes_and_seconds()
    {
        var steps = new[]
        {
            new GapPreview.Step(TimeSpan.FromSeconds(10), GapLine.HandOver, "x", false),
            new GapPreview.Step(TimeSpan.FromSeconds(75), GapLine.CallUp, "y", false),
        };

        Assert.Equal("0:10 hand-over · 1:15 call-up", GapPreview.RunningOrder(steps));
        Assert.StartsWith("Nothing would be said", GapPreview.RunningOrder(Array.Empty<GapPreview.Step>()), StringComparison.Ordinal);
    }

    // ------------------------------------------------- hold, skip, phones

    [Fact]
    public void Letting_Hold_Talk_go_waits_the_space_between_lines_before_talk_picks_up()
    {
        var facts = Next();
        var gap = OpenGap(facts);
        var resumed = T0.AddSeconds(60);

        Assert.Equal("between-race talk is held", gap.WhyNot(GapLine.CallUp, GapRules.Default, resumed, facts, true, false));

        gap.Resumed(resumed);

        Assert.StartsWith("only", gap.WhyNot(GapLine.CallUp, GapRules.Default, resumed.AddSeconds(5), facts, false, false), StringComparison.Ordinal);
        Assert.Null(gap.WhyNot(GapLine.CallUp, GapRules.Default, resumed.AddSeconds(GapRules.Default.SpaceSeconds), facts, false, false));
    }

    [Fact]
    public void Resuming_counts_no_line_toward_the_gap()
    {
        var rules = GapRules.Default with { MostLines = 1, HandOver = true };
        var facts = Next();
        var gap = OpenGap(facts);

        gap.Resumed(T0.AddSeconds(10));

        Assert.DoesNotContain("the gap has had", gap.WhyNot(GapLine.HandOver, rules, T0.AddSeconds(60), facts, false, false) ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void The_On_Air_bar_names_what_Skip_would_skip_and_says_when_talk_is_held()
    {
        Assert.Equal("Skip: Race Recap", GapWords.SkipNote("Race Recap"));
        Assert.Equal("Between-race talk held - press Resume Talk or F10", GapWords.TalkHeld);
    }

    [Fact]
    public void When_the_phones_and_Start_Next_disagree_the_console_says_which_is_which()
    {
        Assert.Equal("the drivers' phones show Heat 4 (Stock) up next, and Start Next would start A-Main",
            GapWords.PhoneAndPaDisagree("A-Main", "Heat 4 (Stock)"));
        Assert.Equal("A-Main has nobody on its grid, so no phone shows it up next",
            GapWords.PhoneAndPaDisagree("A-Main", null));
    }

    [Fact]
    public void Call_ups_and_the_countdown_go_out_with_captions_and_to_Listen_Along()
    {
        // The Timetable reminders' path: shown on the screens and spoken for
        // listeners (SpeakReminder's forListeners defaults to true).
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", "RaceControlControl.BetweenRaces.cs"));
        Assert.Contains("_announcer.SpeakReminder(text, showOnScreens: true, onAir: onAir);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_call_up_follows_the_race_the_phones_call_up_next()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", "RaceControlControl.BetweenRaces.cs"));
        Assert.Contains("UpcomingRaces(4)", source, StringComparison.Ordinal);
        Assert.Contains("GapWords.PhoneAndPaDisagree(", source, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException("check.cmd not found above the test folder.");
    }
}
