namespace HeatCaller.Tests;

/// <summary>
/// The commentary budget's ladder: what goes quiet first when a club's
/// month runs short, and what must never go quiet at all.
///
/// Every one of these is a table test rather than a race night somebody
/// has to sit through - the whole point of CommentaryBudget taking its
/// clock and its figures as inputs. The claims being held to here are the
/// design's own: intros and recaps survive any allowance, flourishes die
/// before colour and colour before core, the month cannot be blown by a
/// night that runs long, and no amount of spare budget makes the announcer
/// talk over itself.
/// </summary>
public sealed class CommentaryBudgetTests
{
    /// <summary>A clock the test drives, so airtime and gaps are decided rather than waited for.</summary>
    private sealed class Clock
    {
        public DateTime Now { get; private set; } = new(2026, 10, 7, 19, 0, 0, DateTimeKind.Unspecified);

        public void Advance(TimeSpan by) => Now += by;

        public Func<DateTime> Read => () => Now;
    }

    /// <summary>A budget mid-night: the month known, the programme half run.</summary>
    private static CommentaryBudget Mid(
        Clock clock,
        int used = 0,
        int allowance = 400,
        int nightsLeft = 4,
        int roundsRun = 15,
        int roundsTotal = 30)
    {
        var budget = new CommentaryBudget(clock.Read);

        budget.NewMeeting("day-1");
        budget.MonthlyUsage(used, allowance);
        budget.NightsLeftThisMonth = nightsLeft;
        budget.NightShape(roundsRun, roundsTotal);

        return budget;
    }

    /// <summary>
    /// A night with very little room: a hundred lines over five nights is
    /// twenty, all of it released because the programme has run. Every
    /// test that needs an exact headroom starts here and burns it down,
    /// rather than hunting for inputs that happen to divide nicely.
    /// </summary>
    private static CommentaryBudget Tight(Clock clock) =>
        Mid(clock, used: 0, allowance: 100, nightsLeft: 5, roundsRun: 30, roundsTotal: 30);

    /// <summary>
    /// Spends lines on intros - which nothing refuses - and then lets the
    /// airtime window empty, so whatever is asked next is decided by the
    /// budget alone and not by how recently the announcer spoke.
    /// </summary>
    private static void Burn(CommentaryBudget budget, Clock clock, int lines)
    {
        for (var i = 0; i < lines; i++)
        {
            Assert.True(Write(budget, SegmentKind.RaceIntro), $"intro {i} was refused while burning budget");
        }

        clock.Advance(TimeSpan.FromMinutes(11));
    }

    /// <summary>Reserve and settle in one go, as a caller does when the write came back.</summary>
    private static bool Write(CommentaryBudget budget, SegmentKind kind, string? className = null)
    {
        var verdict = budget.Reserve(kind, className);

        if (!verdict.Allowed)
        {
            return false;
        }

        budget.Settle(verdict.Ticket);

        return true;
    }

    // ------------------------------------------------------------------
    // The one promise that cannot be broken
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(400, 0)]
    [InlineData(400, 200)]
    [InlineData(400, 399)]
    [InlineData(400, 400)]
    [InlineData(20, 19)]
    public void Intros_and_recaps_are_written_at_every_allowance(int allowance, int used)
    {
        var clock = new Clock();
        var budget = Mid(clock, used, allowance);

        for (var i = 0; i < 10; i++)
        {
            Assert.True(Write(budget, SegmentKind.RaceIntro), $"intro {i} was refused at {used}/{allowance}");
            clock.Advance(TimeSpan.FromMinutes(3));
            Assert.True(Write(budget, SegmentKind.RaceRecap), $"recap {i} was refused at {used}/{allowance}");
            clock.Advance(TimeSpan.FromMinutes(3));
        }
    }

    [Fact]
    public void A_race_is_never_sent_out_in_silence_because_the_announcer_has_been_busy()
    {
        var clock = new Clock();
        var budget = Mid(clock);

        // Fill the ten-minute window with core lines.
        for (var i = 0; i < CommentaryBudget.AirtimeCap; i++)
        {
            Assert.True(Write(budget, SegmentKind.ComingUpNext));
            clock.Advance(TimeSpan.FromSeconds(30));
        }

        Assert.False(budget.Peek(SegmentKind.ComingUpNext).Allowed);

        // A heat is staged. "Six lines in the last ten minutes" is not a
        // reason to say nothing about it.
        Assert.True(Write(budget, SegmentKind.RaceIntro));
        Assert.True(Write(budget, SegmentKind.RaceRecap));
    }

    [Fact]
    public void An_essential_falls_all_the_way_to_the_template_line_and_a_flourish_does_not_fall_at_all()
    {
        Assert.Equal(BrainFallback.Templates, CommentaryBudget.FallbackFor(SegmentTier.Essential));
        Assert.Equal(BrainFallback.LocalModel, CommentaryBudget.FallbackFor(SegmentTier.Core));
        Assert.Equal(BrainFallback.None, CommentaryBudget.FallbackFor(SegmentTier.Colour));
        Assert.Equal(BrainFallback.None, CommentaryBudget.FallbackFor(SegmentTier.Flourish));
    }

    // ------------------------------------------------------------------
    // The ladder
    // ------------------------------------------------------------------

    [Fact]
    public void The_night_goes_quiet_from_the_bottom_up()
    {
        var clock = new Clock();

        // A night with barely any room: 20 left over four nights is five
        // lines, released against a programme one round in.
        var budget = Mid(clock, used: 380, allowance: 400, nightsLeft: 4, roundsRun: 1, roundsTotal: 30);

        // Under a fifth of the month left, so the floor has already shut
        // everything but the essentials.
        Assert.False(budget.Peek(SegmentKind.ColdOpen).Allowed);
        Assert.False(budget.Peek(SegmentKind.FormGuide).Allowed);
        Assert.False(budget.Peek(SegmentKind.BattleWatch).Allowed);
        Assert.True(budget.Peek(SegmentKind.RaceIntro).Allowed);
    }

    [Fact]
    public void A_flourish_needs_more_headroom_than_a_core_segment_costing_the_same()
    {
        var clock = new Clock();
        var budget = Tight(clock);

        // Two lines of headroom: enough for a core segment, not enough for
        // a flourish, which must find two and a half for the same one line.
        Burn(budget, clock, 18);

        Assert.Equal(2.0, budget.Headroom, 3);

        Assert.True(budget.Peek(SegmentKind.BattleWatch).Allowed, "a core segment should fit in two lines of headroom");
        Assert.False(budget.Peek(SegmentKind.ColdOpen).Allowed, "a flourish should not fit in two lines of headroom");
    }

    [Fact]
    public void Colour_goes_before_core_and_after_flourish()
    {
        var clock = new Clock();
        var budget = Tight(clock);

        // Two lines left: core (weight 1) fits, colour (1.5) fits, the
        // flourish (2.5) has already gone.
        Burn(budget, clock, 18);

        Assert.True(budget.Peek(SegmentKind.PaceNote).Allowed);
        Assert.True(budget.Peek(SegmentKind.FormGuide).Allowed);
        Assert.False(budget.Peek(SegmentKind.ThisDayInHistory).Allowed);

        // One line left: colour goes too, and core is the last thing
        // standing between the essentials and silence.
        Burn(budget, clock, 1);

        Assert.Equal(1.0, budget.Headroom, 3);

        Assert.True(budget.Peek(SegmentKind.PaceNote).Allowed);
        Assert.False(budget.Peek(SegmentKind.FormGuide).Allowed);
    }

    [Fact]
    public void Essentials_do_not_ask_permission_but_they_do_spend()
    {
        var clock = new Clock();
        var budget = Tight(clock);

        Assert.True(budget.Peek(SegmentKind.FormGuide).Allowed);

        // Intros eat the night's released budget. Nothing refuses them -
        // not the budget, not the airtime cap, not a busy relay - and the
        // extras quietly stop being affordable. That is the whole
        // mechanism, and there is no special case for it anywhere else.
        Burn(budget, clock, 19);

        Assert.False(budget.Peek(SegmentKind.FormGuide).Allowed);
        Assert.True(budget.Peek(SegmentKind.RaceIntro).Allowed);
    }

    // ------------------------------------------------------------------
    // Pacing
    // ------------------------------------------------------------------

    [Fact]
    public void A_night_that_runs_half_as_long_again_does_not_go_silent_when_the_mains_are_seeded()
    {
        var clock = new Clock();
        var budget = Mid(clock, used: 0, allowance: 400, nightsLeft: 4, roundsRun: 20, roundsTotal: 20);

        var before = budget.Entitled;

        // The mains are seeded: ten more rounds appear and progress drops
        // from all of it to two thirds. Without the high water mark the
        // night would owe back a third of what it has earned and stop
        // talking at the most interesting point of the evening.
        budget.NightShape(20, 30);

        Assert.True(budget.Entitled >= before,
            $"entitlement fell from {before:0.##} to {budget.Entitled:0.##} when the programme grew");

        Assert.True(budget.Peek(SegmentKind.ChampionshipCheck).Allowed);
    }

    [Fact]
    public void Four_nights_of_a_realistic_meeting_stay_inside_the_month()
    {
        var clock = new Clock();
        var allowance = 400;
        var used = 0;

        for (var night = 0; night < 4; night++)
        {
            var budget = new CommentaryBudget(clock.Read);

            budget.NewMeeting($"day-{night}");
            budget.MonthlyUsage(used, allowance);
            budget.NightsLeftThisMonth = 4 - night;

            // Six classes, four heats each, plus a main: thirty rounds.
            const int rounds = 30;

            for (var round = 1; round <= rounds; round++)
            {
                budget.NightShape(round - 1, rounds);

                var cls = $"class-{round % 6}";

                if (Write(budget, SegmentKind.RaceIntro, cls))
                {
                    used++;
                }

                clock.Advance(TimeSpan.FromMinutes(2));

                // Everything else that might want a word this round.
                foreach (var kind in new[]
                         {
                             SegmentKind.BattleWatch, SegmentKind.PaceNote, SegmentKind.FormGuide,
                             SegmentKind.Honours, SegmentKind.ThisDayInHistory, SegmentKind.MidMeetingStandings,
                         })
                {
                    if (Write(budget, kind, cls))
                    {
                        used += CommentaryBudget.Spec(kind).Lines;
                    }

                    clock.Advance(TimeSpan.FromSeconds(50));
                }

                if (Write(budget, SegmentKind.RaceRecap, cls))
                {
                    used++;
                }

                clock.Advance(TimeSpan.FromMinutes(2));
            }
        }

        Assert.True(used <= allowance, $"four nights spent {used} of {allowance}");

        // And it should not have been so cautious that it said almost
        // nothing - a governor that silences the whole month is not
        // graceful, it is just off.
        Assert.True(used > allowance * 0.6, $"four nights only spent {used} of {allowance}");
    }

    // ------------------------------------------------------------------
    // Airtime, which has nothing to do with money
    // ------------------------------------------------------------------

    [Fact]
    public void The_announcer_does_not_talk_over_itself_however_much_budget_there_is()
    {
        var clock = new Clock();

        // No allowance heard from the relay at all, so there is no budget
        // gate whatsoever. Airtime still holds.
        var budget = new CommentaryBudget(clock.Read);
        budget.NewMeeting("day-1");

        Assert.False(budget.AllowanceKnown);

        var said = 0;

        for (var i = 0; i < 20; i++)
        {
            if (Write(budget, SegmentKind.ComingUpNext))
            {
                said++;
            }

            clock.Advance(TimeSpan.FromSeconds(25));
        }

        Assert.Equal(CommentaryBudget.AirtimeCap, said);
    }

    [Fact]
    public void A_segment_will_not_start_on_top_of_the_line_before_it()
    {
        var clock = new Clock();
        var budget = Mid(clock);

        Assert.True(Write(budget, SegmentKind.PaceNote));

        clock.Advance(TimeSpan.FromSeconds(5));

        var refused = budget.Peek(SegmentKind.BattleWatch);

        Assert.False(refused.Allowed);
        Assert.Contains("since the last line", refused.Reason);

        clock.Advance(TimeSpan.FromSeconds(60));

        Assert.True(budget.Peek(SegmentKind.BattleWatch).Allowed);
    }

    [Fact]
    public void Airtime_frees_up_again_once_the_ten_minutes_have_passed()
    {
        var clock = new Clock();
        var budget = new CommentaryBudget(clock.Read);
        budget.NewMeeting("day-1");

        for (var i = 0; i < CommentaryBudget.AirtimeCap; i++)
        {
            Assert.True(Write(budget, SegmentKind.ComingUpNext));
            clock.Advance(TimeSpan.FromSeconds(30));
        }

        Assert.False(budget.Peek(SegmentKind.ComingUpNext).Allowed);

        clock.Advance(TimeSpan.FromMinutes(11));

        Assert.True(budget.Peek(SegmentKind.ComingUpNext).Allowed);
    }

    // ------------------------------------------------------------------
    // Caps, reservations and giving things back
    // ------------------------------------------------------------------

    [Fact]
    public void A_once_a_night_segment_is_said_once_a_night()
    {
        var clock = new Clock();
        var budget = Mid(clock);

        Assert.True(Write(budget, SegmentKind.ColdOpen));

        clock.Advance(TimeSpan.FromMinutes(20));

        var again = budget.Peek(SegmentKind.ColdOpen);

        Assert.False(again.Allowed);
        Assert.StartsWith("already said tonight", again.Reason);
    }

    [Fact]
    public void A_once_a_class_segment_is_free_to_run_for_the_next_class()
    {
        var clock = new Clock();
        var budget = Mid(clock);

        Assert.True(Write(budget, SegmentKind.ChampionshipCheck, "Box Stock"));

        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.False(budget.Peek(SegmentKind.ChampionshipCheck, "Box Stock").Allowed);
        Assert.True(budget.Peek(SegmentKind.ChampionshipCheck, "Open").Allowed);
    }

    [Fact]
    public void A_write_that_never_happened_gives_back_its_lines_and_its_turn()
    {
        var clock = new Clock();
        var budget = Mid(clock);

        var first = budget.Reserve(SegmentKind.ColdOpen);

        Assert.True(first.Allowed);
        Assert.False(budget.Peek(SegmentKind.ColdOpen).Allowed);

        // The relay timed out. Nothing was said, so the cold open has not
        // had its one turn of the night.
        budget.Release(first.Ticket);

        Assert.Equal(0, budget.InFlight);
        Assert.Equal(0, budget.SpentTonight);
        Assert.True(budget.Peek(SegmentKind.ColdOpen).Allowed);
    }

    [Fact]
    public void Two_requests_in_the_air_cannot_both_spend_the_last_line()
    {
        var clock = new Clock();
        var budget = Tight(clock);

        // One line of headroom and two core segments that both want it.
        Burn(budget, clock, 19);

        Assert.Equal(1.0, budget.Headroom, 3);

        var first = budget.Reserve(SegmentKind.BattleWatch);

        Assert.True(first.Allowed);

        var second = budget.Reserve(SegmentKind.PaceNote);

        Assert.False(second.Allowed);
        Assert.Contains("headroom", second.Reason);
    }

    [Fact]
    public void A_busy_relay_is_kept_for_the_segments_that_matter_most()
    {
        var clock = new Clock();
        var budget = Mid(clock, used: 0, allowance: 4000, nightsLeft: 1, roundsRun: 29, roundsTotal: 30);

        var a = budget.Reserve(SegmentKind.RaceIntro);
        var b = budget.Reserve(SegmentKind.RaceRecap);

        Assert.True(a.Allowed);
        Assert.True(b.Allowed);
        Assert.Equal(CommentaryBudget.RelayInFlightCap, budget.InFlight);

        var colour = budget.Peek(SegmentKind.FormGuide);

        Assert.False(colour.Allowed);
        Assert.Contains("busy", colour.Reason);

        // Core and essential still get through - the cap is about what to
        // drop when the pipe is full, not a hard door.
        Assert.True(budget.Peek(SegmentKind.RaceIntro).Allowed);
        Assert.True(budget.Peek(SegmentKind.BattleWatch).Allowed);
    }

    // ------------------------------------------------------------------
    // The switches a club actually has
    // ------------------------------------------------------------------

    [Fact]
    public void An_unknown_allowance_is_not_treated_as_no_allowance()
    {
        var clock = new Clock();
        var budget = new CommentaryBudget(clock.Read);

        budget.NewMeeting("day-1");

        // The relay has not answered yet. Refusing here would silence a
        // club with three hundred lines in hand, and the relay enforces
        // the real cap anyway.
        Assert.False(budget.AllowanceKnown);
        Assert.True(budget.Peek(SegmentKind.ThisDayInHistory).Allowed);
    }

    [Fact]
    public void Keeping_everything_running_spends_against_next_week()
    {
        var clock = new Clock();
        var budget = Mid(clock, used: 395, allowance: 400, nightsLeft: 4, roundsRun: 1, roundsTotal: 30);

        Assert.False(budget.Peek(SegmentKind.ColdOpen).Allowed);

        budget.KeepEverythingRunning = true;

        Assert.True(budget.Peek(SegmentKind.ColdOpen).Allowed);
        Assert.Contains("next week", budget.Describe());
    }

    [Fact]
    public void A_club_that_never_wants_the_furniture_never_gets_it()
    {
        var clock = new Clock();
        var budget = Mid(clock, used: 0, allowance: 4000, nightsLeft: 1, roundsRun: 29, roundsTotal: 30);

        Assert.True(budget.Peek(SegmentKind.ColdOpen).Allowed);

        budget.LowestTier = SegmentTier.Core;

        var off = budget.Peek(SegmentKind.ColdOpen);

        Assert.False(off.Allowed);
        Assert.Contains("switched off", off.Reason);

        Assert.False(budget.Peek(SegmentKind.FormGuide).Allowed);
        Assert.True(budget.Peek(SegmentKind.BattleWatch).Allowed);
        Assert.True(budget.Peek(SegmentKind.RaceIntro).Allowed);
    }

    [Fact]
    public void A_segment_a_club_has_switched_off_never_runs_however_much_budget_there_is()
    {
        var clock = new Clock();
        var budget = Mid(clock, used: 0, allowance: 4000, nightsLeft: 1, roundsRun: 29, roundsTotal: 30);

        Assert.True(budget.Peek(SegmentKind.ColdOpen).Allowed);

        budget.SetOff(SegmentKind.ColdOpen, true);

        var off = budget.Peek(SegmentKind.ColdOpen);

        Assert.False(off.Allowed);
        Assert.Contains("switched off", off.Reason);

        // And only that one - switching off a flourish does not take the
        // rest of its tier with it.
        Assert.True(budget.Peek(SegmentKind.ThisDayInHistory).Allowed);

        budget.SetOff(SegmentKind.ColdOpen, false);

        Assert.True(budget.Peek(SegmentKind.ColdOpen).Allowed);
    }

    [Fact]
    public void Intros_and_recaps_cannot_be_switched_off_from_here()
    {
        // They have their own boxes under "What it writes", which decide
        // whether anything asks for one at all. A second switch for the
        // same thing in a different place is how a settings page starts
        // lying to people.
        var budget = Mid(new Clock());

        budget.SetOff(SegmentKind.RaceIntro, true);

        Assert.False(budget.IsOff(SegmentKind.RaceIntro));
        Assert.True(budget.Peek(SegmentKind.RaceIntro).Allowed);
    }

    [Fact]
    public void The_switches_are_saved_as_the_ones_that_are_off()
    {
        var budget = Mid(new Clock());

        budget.SetOff(SegmentKind.ColdOpen, true);
        budget.SetOff(SegmentKind.FormGuide, true);

        var saved = CommentaryBudget.SaveSwitches(budget.SwitchedOff);

        var other = Mid(new Clock());
        other.RestoreSwitches(saved);

        Assert.True(other.IsOff(SegmentKind.ColdOpen));
        Assert.True(other.IsOff(SegmentKind.FormGuide));
        Assert.False(other.IsOff(SegmentKind.BattleWatch));
    }

    [Fact]
    public void A_segment_added_in_a_later_version_arrives_switched_on()
    {
        // The saved list is what is OFF, so anything not in it is on. A
        // club should never have to go and find a tick box to get a
        // feature they already pay for.
        var budget = Mid(new Clock());

        budget.RestoreSwitches("ColdOpen");

        Assert.True(budget.IsOff(SegmentKind.ColdOpen));

        foreach (var spec in CommentaryBudget.AllSpecs.Where(s => s.Kind != SegmentKind.ColdOpen))
        {
            Assert.False(budget.IsOff(spec.Kind), $"{spec.Kind} should have arrived switched on");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NotASegment")]
    [InlineData("RaceIntro,RaceRecap")]
    [InlineData("ColdOpen,,NotASegment, FormGuide ")]
    public void A_switch_list_that_cannot_be_read_costs_nothing_worse_than_everything_being_on(string saved)
    {
        var budget = Mid(new Clock());

        budget.RestoreSwitches(saved);

        // Nothing unrecognised, and no essential, ever gets switched off.
        Assert.True(budget.Peek(SegmentKind.RaceIntro).Allowed);
        Assert.True(budget.Peek(SegmentKind.RaceRecap).Allowed);
        Assert.DoesNotContain(SegmentKind.RaceIntro, budget.SwitchedOff);
    }

    [Fact]
    public void Every_segment_the_page_offers_says_what_it_is_and_what_it_does()
    {
        foreach (var spec in CommentaryBudget.AllSpecs)
        {
            Assert.False(string.IsNullOrWhiteSpace(spec.Title), $"{spec.Kind} has no title");
            Assert.False(string.IsNullOrWhiteSpace(spec.What), $"{spec.Kind} has no description");
            Assert.EndsWith(".", spec.What);
        }

        Assert.Equal(
            CommentaryBudget.AllSpecs.Count,
            CommentaryBudget.AllSpecs.Select(s => s.Title).Distinct().Count());
    }

    [Fact]
    public void Nothing_new_to_say_is_asked_before_anything_is_paid_for()
    {
        var clock = new Clock();
        var budget = Mid(clock);

        budget.NothingNewToSay = (kind, _) => kind == SegmentKind.Honours;

        var refused = budget.Peek(SegmentKind.Honours);

        Assert.False(refused.Allowed);
        Assert.Contains("nothing new", refused.Reason);
        Assert.True(budget.Peek(SegmentKind.FormGuide).Allowed);
    }

    // ------------------------------------------------------------------
    // Across a restart
    // ------------------------------------------------------------------

    [Fact]
    public void Closing_the_laptop_between_the_heats_and_the_mains_does_not_refill_the_night()
    {
        var clock = new Clock();
        var first = Mid(clock, used: 0, allowance: 400, nightsLeft: 4, roundsRun: 10, roundsTotal: 30);

        for (var i = 0; i < 8; i++)
        {
            Assert.True(Write(first, SegmentKind.RaceIntro));
            clock.Advance(TimeSpan.FromMinutes(4));
        }

        Assert.True(Write(first, SegmentKind.ColdOpen));

        var saved = first.Save();
        var spent = first.SpentTonight;

        var second = new CommentaryBudget(clock.Read);

        second.Restore(saved, "day-1");
        second.MonthlyUsage(0, 400);
        second.NightsLeftThisMonth = 4;
        second.NightShape(10, 30);

        Assert.Equal(spent, second.SpentTonight);
        Assert.False(second.Peek(SegmentKind.ColdOpen).Allowed);
    }

    [Fact]
    public void Last_weeks_spend_is_not_charged_to_this_week()
    {
        var clock = new Clock();
        var last = Mid(clock);

        for (var i = 0; i < 6; i++)
        {
            Assert.True(Write(last, SegmentKind.RaceIntro));
            clock.Advance(TimeSpan.FromMinutes(4));
        }

        var fresh = new CommentaryBudget(clock.Read);

        fresh.Restore(last.Save(), "day-2");

        Assert.Equal(0, fresh.SpentTonight);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"DayId\":")]
    public void A_setting_that_cannot_be_read_costs_the_night_nothing_worse_than_a_fresh_start(string saved)
    {
        var budget = new CommentaryBudget();

        budget.Restore(saved, "day-1");

        Assert.Equal(0, budget.SpentTonight);
        Assert.True(budget.Peek(SegmentKind.RaceIntro).Allowed);
    }

    // ------------------------------------------------------------------
    // What the operator reads
    // ------------------------------------------------------------------

    [Fact]
    public void The_page_says_what_is_running_and_what_is_not()
    {
        var clock = new Clock();

        var healthy = Mid(clock, used: 0, allowance: 400, nightsLeft: 4, roundsRun: 15, roundsTotal: 30);

        Assert.Contains("everything running", healthy.Describe());

        var nearlyOut = Mid(clock, used: 390, allowance: 400, nightsLeft: 4, roundsRun: 15, roundsTotal: 30);

        Assert.Contains("intros and recaps", nearlyOut.Describe());

        var quiet = new CommentaryBudget(clock.Read);
        quiet.NewMeeting("day-1");

        Assert.Contains("0 lines written", quiet.Describe());
    }

    // ------------------------------------------------------------------
    // The table itself
    // ------------------------------------------------------------------

    [Fact]
    public void Every_segment_declares_what_it_costs_and_how_long_it_stays_true()
    {
        foreach (var spec in CommentaryBudget.AllSpecs)
        {
            Assert.True(spec.Lines >= 1, $"{spec.Kind} costs nothing");
            Assert.True(spec.NeedsBy > TimeSpan.Zero, $"{spec.Kind} has no deadline");
            Assert.True(spec.MaxPerMeeting >= 1, $"{spec.Kind} can never run");
            Assert.True(spec.MaxPerClass >= 1, $"{spec.Kind} can never run for a class");
            Assert.True(spec.MinGap >= TimeSpan.Zero, $"{spec.Kind} has a negative gap");
        }
    }

    [Fact]
    public void Every_segment_there_is_has_a_row_in_the_table()
    {
        foreach (var kind in Enum.GetValues<SegmentKind>())
        {
            var spec = CommentaryBudget.Spec(kind);

            Assert.Equal(kind, spec.Kind);
        }

        Assert.Equal(Enum.GetValues<SegmentKind>().Length, CommentaryBudget.AllSpecs.Count);
    }

    [Fact]
    public void An_in_race_segment_stops_being_worth_saying_sooner_than_one_between_races()
    {
        // The battle watch is the test of the whole freshness rule: a line
        // about laps four to nine is worthless by lap twenty however well
        // it reads, so its deadline has to be inside a single race.
        Assert.True(CommentaryBudget.Spec(SegmentKind.BattleWatch).NeedsBy <= TimeSpan.FromSeconds(30));
        Assert.True(CommentaryBudget.Spec(SegmentKind.QualifyingReport).NeedsBy >= TimeSpan.FromMinutes(1));
    }

    // ------------------------------------------------------------------
    // How chatty moves every limit (3 Nov 2026)
    // ------------------------------------------------------------------

    [Fact]
    public void Non_stop_lifts_the_nightly_and_per_class_limits()
    {
        var clock = new Clock();
        var budget = Mid(clock, allowance: 100000);
        budget.Chatter = "nonstop";

        var limit = CommentaryBudget.Spec(SegmentKind.BattleWatch).MaxPerClass;

        for (var i = 0; i < limit + 3; i++)
        {
            Assert.True(Write(budget, SegmentKind.BattleWatch, "Stock"), $"battle watch {i} was refused at Non-stop");
            clock.Advance(TimeSpan.FromMinutes(11));
        }
    }

    [Fact]
    public void Standard_keeps_the_table_s_own_limits()
    {
        var clock = new Clock();
        var budget = Mid(clock, allowance: 100000);

        var limit = CommentaryBudget.Spec(SegmentKind.BattleWatch).MaxPerClass;

        for (var i = 0; i < limit; i++)
        {
            Assert.True(Write(budget, SegmentKind.BattleWatch, "Stock"));
            clock.Advance(TimeSpan.FromMinutes(11));
        }

        var refused = budget.Peek(SegmentKind.BattleWatch, "Stock");

        Assert.False(refused.Allowed);
        Assert.Equal(BudgetRefusal.ItsCapForTheNight, refused.Why);
        Assert.Contains("Standard", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Once_a_night_stays_once_a_night_at_every_stop()
    {
        foreach (var level in ShowChatter.All)
        {
            var clock = new Clock();
            var budget = Mid(clock, allowance: 100000);
            budget.Chatter = level.Key;

            Assert.True(Write(budget, SegmentKind.ColdOpen));
            clock.Advance(TimeSpan.FromMinutes(20));

            Assert.False(budget.Peek(SegmentKind.ColdOpen).Allowed, $"a second cold open at {level.Name}");
        }
    }

    [Fact]
    public void Non_stop_shortens_the_gap_after_the_last_line()
    {
        var gap = CommentaryBudget.Spec(SegmentKind.BattleWatch).MinGap;
        var wait = ShowChatter.ScaledGap(gap, "nonstop") + TimeSpan.FromSeconds(1);

        Assert.True(wait < gap);

        var clock = new Clock();
        var standard = Mid(clock, allowance: 100000);
        var nonStop = Mid(clock, allowance: 100000);
        nonStop.Chatter = "nonstop";

        Assert.True(Write(standard, SegmentKind.PaceNote));
        Assert.True(Write(nonStop, SegmentKind.PaceNote));

        clock.Advance(wait);

        Assert.False(standard.Peek(SegmentKind.BattleWatch).Allowed);
        Assert.True(nonStop.Peek(SegmentKind.BattleWatch).Allowed);
    }

    [Fact]
    public void Non_stop_releases_the_whole_night_from_the_first_race()
    {
        var clock = new Clock();
        var standard = Mid(clock, roundsRun: 0);
        var nonStop = Mid(clock, roundsRun: 0);
        nonStop.Chatter = "nonstop";

        Assert.Equal(standard.NightAllotment * CommentaryBudget.OpeningFloat, standard.Entitled, 6);
        Assert.Equal(nonStop.NightAllotment, nonStop.Entitled, 6);
    }
}
