namespace HeatCaller.Tests;

/// <summary>
/// The NASCAR-style broadcast features (18 Oct 2026): the lead change count,
/// sponsored moments and their ration, the driver rating, the driver
/// introductions and qualifying recap, the stream's moment banner, and the
/// battle-for-the-lead call. Everything here is the pure part of each - the
/// sums and the sentences - so a wrong number or a broken sentence shows up
/// here rather than over the PA.
/// </summary>
public sealed class BroadcastFeaturesTests
{
    // ------------------------------------------------------------------
    //  Lead changes
    // ------------------------------------------------------------------

    [Fact]
    public void The_first_car_to_lead_is_not_a_lead_change()
    {
        var leads = new LeadTracker();

        Assert.Null(leads.Observe("A"));
        Assert.Null(leads.Observe("A"));
        Assert.Equal(0, leads.Changes);
        Assert.Equal(1, leads.Leaders);
    }

    [Fact]
    public void Each_change_at_the_front_is_counted_with_the_drivers_who_have_led()
    {
        var leads = new LeadTracker();

        leads.Observe("A");
        var first = leads.Observe("B")!;
        var second = leads.Observe("A")!;
        var third = leads.Observe("C")!;

        Assert.Equal(1, first.Number);
        Assert.Equal("A", first.Previous);
        Assert.Equal(2, first.Leaders);

        Assert.Equal(2, second.Number);
        Assert.Equal(2, second.TimesLed);

        Assert.Equal(3, third.Number);
        Assert.Equal(3, third.Leaders);
        Assert.Equal(3, leads.Changes);
    }

    [Fact]
    public void A_leader_who_disappears_does_not_make_the_next_car_a_lead_change()
    {
        var leads = new LeadTracker();

        leads.Observe("GHOST-1");
        leads.Forget("GHOST-1");

        Assert.Null(leads.Observe("A"));
        Assert.Equal(0, leads.Changes);
    }

    [Fact]
    public void A_new_race_starts_the_count_again()
    {
        var leads = new LeadTracker();

        leads.Observe("A");
        leads.Observe("B");
        leads.Reset();

        Assert.Null(leads.Observe("C"));
        Assert.Equal(0, leads.Changes);
        Assert.Equal(1, leads.Leaders);
    }

    [Fact]
    public void The_count_is_said_from_the_second_change_on()
    {
        Assert.Equal(string.Empty, LeadTracker.Tail(new LeadChange("B", "A", 1, 2, 1)));
        Assert.Equal(" That's the fourth lead change, among three drivers.", LeadTracker.Tail(new LeadChange("B", "A", 4, 3, 2)));
    }

    [Fact]
    public void A_driver_retaking_the_lead_is_back_in_front()
    {
        Assert.Equal(string.Empty, LeadTracker.BackInFront(new LeadChange("B", "A", 1, 2, 1)));
        Assert.Equal("back in front for the second time", LeadTracker.BackInFront(new LeadChange("B", "A", 3, 2, 2)));
    }

    // ------------------------------------------------------------------
    //  Sponsored moments
    // ------------------------------------------------------------------

    private static SponsorBooking Sponsor(string id, string name, bool enabled = true, params SponsoredMoment[] presents) =>
        new() { Id = id, Name = name, Enabled = enabled, Presents = presents.Select(m => m.ToString()).ToList() };

    [Fact]
    public void The_first_sponsor_in_the_list_with_a_moment_ticked_presents_it()
    {
        var sponsors = new[]
        {
            Sponsor("a", "Off Tonight", enabled: false, SponsoredMoment.FastestLap),
            Sponsor("b", "Joe's Hobby Shop", true, SponsoredMoment.FastestLap),
            Sponsor("c", "Second Shop", true, SponsoredMoment.FastestLap, SponsoredMoment.Battle),
        };

        Assert.Equal("b", SponsoredMoments.SponsorFor(sponsors, SponsoredMoment.FastestLap)!.Id);
        Assert.Equal("c", SponsoredMoments.SponsorFor(sponsors, SponsoredMoment.Battle)!.Id);
        Assert.Null(SponsoredMoments.SponsorFor(sponsors, SponsoredMoment.LeadChange));
    }

    [Fact]
    public void A_moment_ticked_for_two_sponsors_is_reported_as_a_clash()
    {
        var sponsors = new[]
        {
            Sponsor("b", "Joe's Hobby Shop", true, SponsoredMoment.FastestLap),
            Sponsor("c", "Second Shop", true, SponsoredMoment.FastestLap),
        };

        var clash = Assert.Single(SponsoredMoments.Clashes(sponsors));

        Assert.Equal(SponsoredMoment.FastestLap, clash.Moment);
        Assert.Equal("Joe's Hobby Shop", clash.Winner);
        Assert.Equal(new[] { "Second Shop" }, clash.Others);
    }

    [Fact]
    public void The_tag_ends_the_sentence_once()
    {
        Assert.Equal(" Presented by Smith and Co.", SponsoredMoments.Tag("Smith and Co."));
        Assert.Equal(string.Empty, SponsoredMoments.Tag("  "));
    }

    [Fact]
    public void Each_sponsorable_call_belongs_to_its_moment()
    {
        Assert.Equal(SponsoredMoment.FastestLap, SponsoredMoments.ForEvent(AnnouncementEventType.FastLap));
        Assert.Equal(SponsoredMoment.LeadChange, SponsoredMoments.ForEvent(AnnouncementEventType.LeaderChange));
        Assert.Equal(SponsoredMoment.Battle, SponsoredMoments.ForEvent(AnnouncementEventType.CloseBattle));
        Assert.Equal(SponsoredMoment.DriverIntros, SponsoredMoments.ForEvent(AnnouncementEventType.DriverIntro));
        Assert.Null(SponsoredMoments.ForEvent(AnnouncementEventType.LapTime));
    }

    [Fact]
    public void A_repeating_moment_is_named_at_most_once_every_three_minutes()
    {
        var now = new DateTime(2026, 10, 18, 20, 0, 0, DateTimeKind.Local);
        var gate = new SponsoredMomentGate(() => now);

        Assert.True(gate.CanMention(SponsoredMoment.FastestLap));
        gate.Mentioned(SponsoredMoment.FastestLap, "b");

        now = now.AddMinutes(1);
        Assert.False(gate.CanMention(SponsoredMoment.FastestLap));

        now = now.AddMinutes(2);
        Assert.True(gate.CanMention(SponsoredMoment.FastestLap));
    }

    [Fact]
    public void A_once_a_race_moment_is_always_named_and_every_mention_is_counted()
    {
        var now = new DateTime(2026, 10, 18, 20, 0, 0, DateTimeKind.Local);
        var gate = new SponsoredMomentGate(() => now);

        gate.Mentioned(SponsoredMoment.DriverIntros, "b");
        Assert.True(gate.CanMention(SponsoredMoment.DriverIntros));
        gate.Mentioned(SponsoredMoment.DriverIntros, "b");
        gate.Mentioned(SponsoredMoment.FastestLap, "b");

        var tonight = gate.MentionsTonight();

        Assert.Equal(2, tonight["b"][SponsoredMoment.DriverIntros]);
        Assert.Equal(1, tonight["b"][SponsoredMoment.FastestLap]);
    }

    [Fact]
    public void The_mention_count_starts_again_on_the_next_race_night()
    {
        var now = new DateTime(2026, 10, 18, 20, 0, 0, DateTimeKind.Local);
        var gate = new SponsoredMomentGate(() => now);

        gate.Mentioned(SponsoredMoment.FastestLap, "b");
        now = now.AddDays(1);

        Assert.Empty(gate.MentionsTonight());
        Assert.True(gate.CanMention(SponsoredMoment.FastestLap));
    }

    [Fact]
    public void The_report_lists_a_sponsors_moments_under_their_reads()
    {
        var sponsor = Sponsor("b", "Joe's Hobby Shop", true, SponsoredMoment.FastestLap);
        var mentions = new Dictionary<string, IReadOnlyDictionary<SponsoredMoment, int>>
        {
            ["b"] = new Dictionary<SponsoredMoment, int> { [SponsoredMoment.FastestLap] = 3, [SponsoredMoment.LeadChange] = 1 },
        };

        var report = SponsorReport.ForTheNight(new[] { sponsor }, new Dictionary<string, int> { ["b"] = 2 },
            SponsorPlacement.Everywhere, "Club Night", new DateTime(2026, 10, 18), mentions);

        Assert.Contains("plus \"presented by\" on: fastest lap 3 times and lead changes once", report);
    }

    // ------------------------------------------------------------------
    //  Driver rating
    // ------------------------------------------------------------------

    [Fact]
    public void From_the_back_to_a_win_leading_every_lap_with_the_fastest_lap_is_the_full_rating()
    {
        var ratings = DriverRating.ForRace(new[]
        {
            new RatingInput("A", 1, 2, 10, 10, 10_000),
            new RatingInput("B", 2, 1, 10, 0, 10_500),
        });

        Assert.Equal(150, ratings["A"]);

        // Last, from pole, at 95% of the race's pace, the whole distance:
        // pace 17.06 + distance 15, nothing for places.
        Assert.Equal(32.1, ratings["B"]);
    }

    [Fact]
    public void Flag_to_flag_from_pole_with_the_fastest_lap_is_142_and_a_half()
    {
        var ratings = DriverRating.ForRace(new[]
        {
            new RatingInput("A", 1, 1, 10, 10, 10_000),
            new RatingInput("B", 2, 2, 10, 0, 10_200),
            new RatingInput("C", 3, 3, 9, 0, 10_400),
        });

        Assert.Equal(142.5, ratings["A"]);
        Assert.True(ratings["B"] > ratings["C"]);
    }

    [Fact]
    public void Warnings_cost_five_each_and_a_disqualification_is_nothing()
    {
        var ratings = DriverRating.ForRace(new[]
        {
            new RatingInput("A", 1, 2, 10, 10, 10_000, Warnings: 2),
            new RatingInput("B", 2, 1, 10, 0, 10_500, Disqualified: true),
        });

        Assert.Equal(140, ratings["A"]);
        Assert.Equal(0, ratings["B"]);
    }

    [Fact]
    public void A_race_of_one_car_is_not_rated()
    {
        Assert.Empty(DriverRating.ForRace(new[] { new RatingInput("A", 1, 1, 10, 10, 10_000) }));
    }

    [Fact]
    public void A_saved_race_is_rated_among_the_real_cars_and_practice_is_not_rated()
    {
        var race = new SessionRecord
        {
            SessionType = RaceSessionType.Race,
            Results =
            {
                new SessionResultEntry { Transponder = "A", Position = 1, Laps = 10, LapsLed = 10, FastLapMs = 10_000 },
                new SessionResultEntry { Transponder = "GHOST-1", Position = 2, Laps = 10, FastLapMs = 9_000 },
                new SessionResultEntry { Transponder = "B", Position = 3, Laps = 10, FastLapMs = 10_500 },
            },
        };

        var ratings = DriverRating.ForSession(race);

        Assert.Equal(2, ratings.Count);
        Assert.False(ratings.ContainsKey("GHOST-1"));

        // A has the race's fastest lap among the real cars - the ghost's
        // quicker one is not anybody's to beat.
        Assert.True(ratings["A"] > 130);

        race.SessionType = RaceSessionType.Practice;
        Assert.Empty(DriverRating.ForSession(race));
    }

    // ------------------------------------------------------------------
    //  Driver introductions
    // ------------------------------------------------------------------

    [Fact]
    public void An_introduction_says_what_the_driver_has_and_nothing_they_have_not()
    {
        var full = DriverIntros.Intro(new IntroDriver("1", 1, "Sam", "Sam Smith", "7", "Riverside", "won last time out"), 6);

        Assert.Equal("On pole, number seven, Sam, from Riverside - won last time out", full.Spoken);
        Assert.Equal("On pole, #7, Sam Smith, from Riverside - won last time out", full.Screen);

        Assert.Equal("Starting third, Bill", DriverIntros.Intro(new IntroDriver("2", 3, "Bill", "Bill"), 6).Spoken);
        Assert.Equal("Rounding out the field, Frank", DriverIntros.Intro(new IntroDriver("3", 6, "Frank", "Frank"), 6).Spoken);
    }

    [Fact]
    public void A_race_without_a_grid_introduces_nobody_as_on_pole()
    {
        Assert.Equal(string.Empty, DriverIntros.GridPhrase(0, 4));
        Assert.Equal("Sam, from Riverside", DriverIntros.Intro(new IntroDriver("1", 0, "Sam", "Sam", Hometown: "Riverside"), 4).Spoken);
    }

    private static readonly DateTime Tonight = new(2026, 10, 18, 20, 0, 0, DateTimeKind.Utc);

    private static SessionRecord RaceOn(int daysAgo, int place, string transponder = "A") => new()
    {
        SessionType = RaceSessionType.Race,
        StartedAtUtc = Tonight.AddDays(-daysAgo),
        Results = Enumerable.Range(1, 3)
            .Select(p => new SessionResultEntry
            {
                Transponder = p == place ? transponder : $"other-{daysAgo}-{p}",
                Position = p,
                Laps = 10,
            })
            .ToList(),
    };

    [Fact]
    public void A_driver_with_no_races_is_making_a_first_start()
    {
        Assert.Equal("making a first start", DriverIntros.Stat(Array.Empty<SessionRecord>(), "A", Tonight));
    }

    [Fact]
    public void A_milestone_start_beats_every_other_line()
    {
        var nine = Enumerable.Range(1, 9).Select(day => RaceOn(day, 1)).ToList();

        Assert.Equal("about to make a tenth start", DriverIntros.Stat(nine, "A", Tonight));
    }

    [Fact]
    public void A_winning_run_is_one_they_are_going_for_the_next_of()
    {
        var history = new[] { RaceOn(1, 1), RaceOn(2, 1), RaceOn(3, 2) };

        Assert.Equal("going for three in a row", DriverIntros.Stat(history, "A", Tonight));
    }

    [Fact]
    public void Last_time_out_and_recent_wins_are_said_when_there_is_no_run()
    {
        Assert.Equal("won last time out", DriverIntros.Stat(new[] { RaceOn(1, 1), RaceOn(2, 2) }, "A", Tonight));
        Assert.Equal("two wins from their last three", DriverIntros.Stat(new[] { RaceOn(1, 2), RaceOn(2, 1), RaceOn(3, 1) }, "A", Tonight));
    }

    [Fact]
    public void Tonights_race_and_demos_are_not_part_of_a_drivers_form()
    {
        var later = RaceOn(-1, 1);
        var demo = RaceOn(1, 1);
        demo.IsDemo = true;

        Assert.Equal("making a first start", DriverIntros.Stat(new[] { later, demo }, "A", Tonight));
    }

    // ------------------------------------------------------------------
    //  Qualifying recap
    // ------------------------------------------------------------------

    [Fact]
    public void A_most_laps_recap_names_pole_its_laps_and_the_two_behind()
    {
        var order = new[]
        {
            new RecapCar("Sam", "Sam Smith", 24, 300_000, 0, 12_000),
            new RecapCar("Bill", "Bill Jones", 24, 300_214, 0, 12_100),
            new RecapCar("Frank", "Frank Lee", 23, 299_000, 0, 12_300),
        };

        var (spoken, screen) = DriverIntros.Recap(order, QualifyingMethod.TotalLaps, 3);

        Assert.Equal("From qualifying: Sam Smith takes pole with 24 laps. Bill Jones lines up second, +0.214s, and Frank Lee third.", screen);
        Assert.StartsWith("From qualifying: Sam takes pole with twenty-four laps. Bill lines up second, ", spoken);
        Assert.EndsWith(" back, and Frank third.", spoken);
    }

    [Fact]
    public void A_car_a_lap_down_in_qualifying_is_said_to_be()
    {
        var order = new[]
        {
            new RecapCar("Sam", "Sam", 24, 300_000, 0, 12_000),
            new RecapCar("Bill", "Bill", 23, 290_000, 0, 12_100),
        };

        Assert.Equal("From qualifying: Sam takes pole with twenty-four laps. Bill lines up second, a lap down.",
            DriverIntros.Recap(order, QualifyingMethod.TotalLaps, 3).Spoken);
    }

    [Fact]
    public void A_consecutive_laps_recap_quotes_the_average_of_the_best_run()
    {
        var order = new[] { new RecapCar("Sam", "Sam", 20, 250_000, 30_000, 9_800) };

        Assert.Equal("From qualifying: Sam takes pole averaging 10.000s over 3 laps.",
            DriverIntros.Recap(order, QualifyingMethod.BestConsecutiveLaps, 3).Screen);
    }

    [Fact]
    public void Nobody_qualified_is_no_recap()
    {
        Assert.Equal(string.Empty, DriverIntros.Recap(Array.Empty<RecapCar>(), QualifyingMethod.TotalLaps, 3).Spoken);
    }

    [Fact]
    public void Staging_waits_for_the_introductions_but_never_forever()
    {
        Assert.Equal(TimeSpan.FromSeconds(20), RaceControlControl.IntroWait(0, recap: false));
        Assert.Equal(TimeSpan.FromSeconds(56), RaceControlControl.IntroWait(3, recap: true));
        Assert.Equal(TimeSpan.FromSeconds(150), RaceControlControl.IntroWait(40, recap: true));
    }

    // ------------------------------------------------------------------
    //  The stream's moment banner, and the battle for the lead
    // ------------------------------------------------------------------

    [Fact]
    public void A_moment_is_up_for_a_few_seconds_and_each_one_is_numbered()
    {
        var now = new DateTime(2026, 10, 18, 20, 0, 0, DateTimeKind.Utc);
        var feed = new BroadcastMomentFeed(() => now);

        Assert.Null(feed.Current());

        feed.Post(new BroadcastMoment("lead", "Lead change", "Sam takes the lead from Bill"));
        feed.Post(new BroadcastMoment("milestone", "First win", "Sam"));
        feed.Post(new BroadcastMoment("milestone", "  ", "nobody"));

        var current = feed.Current();
        Assert.NotNull(current);
        Assert.Equal(2, current!.Value.Seq);
        Assert.Equal("First win", current.Value.Moment.Title);

        now = now.AddSeconds(13);
        Assert.Null(feed.Current());
    }

    [Fact]
    public void A_battle_for_the_lead_is_named_after_the_lines_delivery_tag()
    {
        Assert.Equal("[shouting] Battle for the lead! Sam is all over Bill!",
            AnnouncementService.BattleForTheLead("[shouting] Sam is all over Bill!"));
        Assert.Equal("Battle for the lead! Close one!", AnnouncementService.BattleForTheLead("Close one!"));
    }
}
