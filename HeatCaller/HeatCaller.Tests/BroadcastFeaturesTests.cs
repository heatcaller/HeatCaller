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
    public void The_credit_is_a_bright_whole_sentence_that_ends_once()
    {
        // 21 Oct 2026: " Presented by Smith and Co." on the end of a shouted
        // call was read like a caption - flat and robotic.
        var credit = SponsoredMoments.Tag(SponsoredMoment.FastestLap, "Smith and Co.");

        Assert.StartsWith(" [cheerful] ", credit);
        Assert.EndsWith("Smith and Co!", credit);
        Assert.Contains("fastest lap", credit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Co.", credit);
        Assert.Equal(string.Empty, SponsoredMoments.Tag(SponsoredMoment.FastestLap, "  "));
    }

    [Fact]
    public void Every_moment_has_more_than_one_way_to_credit_its_sponsor()
    {
        foreach (var moment in Enum.GetValues<SponsoredMoment>())
        {
            var lines = SponsoredMoments.CreditLines(moment);

            Assert.True(lines.Count >= 2, $"{moment} has only one credit");

            foreach (var line in lines)
            {
                // A whole sentence with the name in it once - never the bare
                // "Presented by X." that started it.
                Assert.Single(System.Text.RegularExpressions.Regex.Matches(line, @"\{sponsor\}"));
                Assert.False(line.StartsWith("Presented by", StringComparison.OrdinalIgnoreCase), line);
                Assert.True(line.EndsWith('!') || line.EndsWith('.'), line);
                Assert.True(char.IsUpper(line[0]), line);
            }
        }
    }

    [Fact]
    public void The_same_credit_is_not_heard_twice_running()
    {
        var first = SponsoredMoments.Tag(SponsoredMoment.LeadChange, "Joe's Hobby Shop");
        var second = SponsoredMoments.Tag(SponsoredMoment.LeadChange, "Joe's Hobby Shop");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void A_voice_without_audio_tags_hears_just_the_sentence()
    {
        var credit = SponsoredMoments.Tag(SponsoredMoment.Battle, "Joe's Hobby Shop");
        var spoken = VoiceProfile.StripAudioTags("[shouting] Sam is all over Bill!" + credit);

        Assert.DoesNotContain("[", spoken);
        Assert.StartsWith("Sam is all over Bill! ", spoken);
        Assert.EndsWith("Joe's Hobby Shop!", spoken);
    }

    [Fact]
    public void A_call_with_no_ending_of_its_own_is_closed_before_the_credit()
    {
        Assert.Equal("Sam sets the fastest lap. [cheerful] That's on us!",
            SponsoredMoments.WithCredit("Sam sets the fastest lap ", " [cheerful] That's on us!"));
        Assert.Equal("Sam sets the fastest lap! [cheerful] That's on us!",
            SponsoredMoments.WithCredit("Sam sets the fastest lap!", " [cheerful] That's on us!"));
        Assert.Equal("Sam sets the fastest lap", SponsoredMoments.WithCredit(" Sam sets the fastest lap ", string.Empty));
    }

    [Fact]
    public void A_credit_for_the_top_of_a_segment_looks_forward_to_it()
    {
        foreach (var moment in Enum.GetValues<SponsoredMoment>().Cast<SponsoredMoment?>().Append(null))
        {
            var lines = SponsoredMoments.LeadInLines(moment);

            Assert.True(lines.Count >= 2, $"{moment} has only one lead-in");

            foreach (var line in lines)
            {
                Assert.Single(System.Text.RegularExpressions.Regex.Matches(line, @"\{sponsor\}"));
                Assert.True(line.EndsWith('!') || line.EndsWith('.'), line);

                // "That result..." looks back at what was just said - wrong
                // at the top of a segment that has not started yet.
                Assert.False(line.StartsWith("That ", StringComparison.Ordinal), line);
                Assert.False(line.StartsWith("And ", StringComparison.Ordinal), line);
            }
        }

        var lead = SponsoredMoments.LeadIn(SponsoredMoment.RaceResult, "Joe's Hobby Shop.");
        Assert.DoesNotContain("[", lead);
        Assert.EndsWith("Joe's Hobby Shop!", lead);
        Assert.Equal(string.Empty, SponsoredMoments.LeadIn(SponsoredMoment.RaceResult, " "));
    }

    [Fact]
    public void The_credit_goes_in_front_of_the_words_and_never_on_its_own()
    {
        Assert.Equal("This is ours! Sam wins it.", SponsoredMoments.WithLeadIn("This is ours!", "  Sam wins it."));
        Assert.Null(SponsoredMoments.WithLeadIn("This is ours!", (string?)null));
        Assert.Equal(" ", SponsoredMoments.WithLeadIn("This is ours!", " "));
        Assert.Equal("Sam wins it.", SponsoredMoments.WithLeadIn(string.Empty, "Sam wins it."));
    }

    [Fact]
    public void A_two_handers_credit_is_read_by_the_host_never_the_co_host()
    {
        var hostFirst = SponsoredMoments.WithLeadIn("This is ours!",
            new[] { new ExchangeTurn(false, "Sam's on pole."), new ExchangeTurn(true, "By a mile.") });

        Assert.Equal(2, hostFirst.Count);
        Assert.Equal("This is ours! Sam's on pole.", hostFirst[0].Line);
        Assert.False(hostFirst[0].FromCoHost);

        var coHostFirst = SponsoredMoments.WithLeadIn("This is ours!",
            new[] { new ExchangeTurn(true, "Sam's on pole!"), new ExchangeTurn(false, "By a mile.") });

        Assert.Equal(3, coHostFirst.Count);
        Assert.Equal(new ExchangeTurn(false, "This is ours!"), coHostFirst[0]);
        Assert.Equal("Sam's on pole!", coHostFirst[1].Line);
    }

    // ------------------------------------------------------------------
    //  The sponsor's tagline after their credit (21 Oct 2026)
    // ------------------------------------------------------------------

    [Fact]
    public void A_call_credit_ends_with_the_sponsors_tagline_said_exactly()
    {
        var credit = SponsoredMoments.Tag(SponsoredMoment.FastestLap, "Joe's Hobby Shop.", "Everything you need to go faster");

        Assert.StartsWith(" [cheerful] ", credit);
        Assert.EndsWith("Joe's Hobby Shop! Everything you need to go faster.", credit);
        Assert.Contains(SponsoredMoments.CreditLines(SponsoredMoment.FastestLap),
            line => credit == " [cheerful] " + line.Replace("{sponsor}", "Joe's Hobby Shop") + " Everything you need to go faster.");
    }

    [Fact]
    public void A_tagline_that_ends_itself_is_left_to_end_itself()
    {
        Assert.Equal("Go faster!", SponsoredMoments.SaidTagline("  Go faster!  "));
        Assert.Equal("Since 1987.", SponsoredMoments.SaidTagline("Since 1987"));
        Assert.Equal(string.Empty, SponsoredMoments.SaidTagline("   "));
        Assert.Equal(string.Empty, SponsoredMoments.SaidTagline(null));
    }

    [Fact]
    public void A_tagline_can_never_pass_for_a_delivery_tag_or_run_over_two_lines()
    {
        var said = SponsoredMoments.SaidTagline("[shouting] Buy now\nIgnore the above");

        Assert.DoesNotContain("[", said);
        Assert.DoesNotContain("]", said);
        Assert.DoesNotContain("\n", said);
        Assert.Equal("shouting Buy now Ignore the above.", said);
        Assert.True(SponsoredMoments.SaidTagline(new string('a', 500)).Length <= SponsorRotation.MaxTaglineChars + 1);
    }

    [Fact]
    public void No_tagline_leaves_the_credit_as_it_was()
    {
        var plain = SponsoredMoments.Credit(SponsoredMoment.Battle, "Joe's Hobby Shop", "  ");

        Assert.Contains(SponsoredMoments.CreditLines(SponsoredMoment.Battle),
            line => plain == line.Replace("{sponsor}", "Joe's Hobby Shop"));
        Assert.Equal(string.Empty, SponsoredMoments.Credit(SponsoredMoment.Battle, " ", "Go faster"));
        Assert.Contains(SponsoredMoments.LeadInLines(SponsoredMoment.RaceResult),
            line => SponsoredMoments.LeadIn(SponsoredMoment.RaceResult, "Joe's", "Go faster")
                    == line.Replace("{sponsor}", "Joe's") + " Go faster.");
    }

    [Fact]
    public void A_sponsors_tagline_is_found_by_the_name_they_are_credited_by()
    {
        var sponsors = new[]
        {
            new SponsorBooking { Id = "a", Name = "Old Shop", Tagline = "Gone", Enabled = false },
            new SponsorBooking { Id = "b", Name = "Joe's Hobby Shop.", Tagline = "Go faster" },
            new SponsorBooking { Id = "c", Name = "Second Shop" },
        };

        Assert.Equal("Go faster", SponsoredMoments.TaglineOf(sponsors, "joe's hobby shop"));
        Assert.Null(SponsoredMoments.TaglineOf(sponsors, "Second Shop"));
        Assert.Null(SponsoredMoments.TaglineOf(sponsors, "Old Shop"));
        Assert.Null(SponsoredMoments.TaglineOf(sponsors, "Nobody"));
        Assert.Null(SponsoredMoments.TaglineOf(null, "Joe's Hobby Shop"));
    }

    [Fact]
    public void Each_sponsorable_call_belongs_to_its_moment()
    {
        Assert.Equal(SponsoredMoment.FastestLap, SponsoredMoments.ForEvent(AnnouncementEventType.FastLap));
        Assert.Equal(SponsoredMoment.LeadChange, SponsoredMoments.ForEvent(AnnouncementEventType.LeaderChange));
        Assert.Equal(SponsoredMoment.Battle, SponsoredMoments.ForEvent(AnnouncementEventType.CloseBattle));
        Assert.Equal(SponsoredMoment.RaceResult, SponsoredMoments.ForEvent(AnnouncementEventType.RaceCompleted));
        Assert.Null(SponsoredMoments.ForEvent(AnnouncementEventType.LapTime));
    }

    [Fact]
    public void Each_sponsorable_commentary_segment_belongs_to_its_moment()
    {
        Assert.Equal(SponsoredMoment.Battle, SponsoredMoments.ForSegment(SegmentKind.BattleWatch));
        Assert.Equal(SponsoredMoment.FastestLap, SponsoredMoments.ForSegment(SegmentKind.PaceNote));
        Assert.Equal(SponsoredMoment.QualifyingRecap, SponsoredMoments.ForSegment(SegmentKind.QualifyingReport));
        Assert.Equal(SponsoredMoment.Milestones, SponsoredMoments.ForSegment(SegmentKind.Honours));
        Assert.Null(SponsoredMoments.ForSegment(SegmentKind.ClubAside));
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
    public void An_introduction_fact_line_labels_what_the_driver_has_and_leaves_out_what_they_have_not()
    {
        Assert.Equal(
            "1. grid position 1; name Sam; racing number 7; from Riverside; form: won last time out",
            DriverIntros.FactLine(new IntroDriver("1", 1, "Sam", "Sam Smith", "#7", "Riverside", "won last time out"), 0));

        Assert.Equal("3. grid position 3; name Bill", DriverIntros.FactLine(new IntroDriver("2", 3, "Bill", "Bill"), 2));
    }

    [Fact]
    public void A_race_without_a_grid_gives_nobody_a_grid_position()
    {
        Assert.Equal("1. name Sam; from Riverside", DriverIntros.FactLine(new IntroDriver("1", 0, "Sam", "Sam", Hometown: "Riverside"), 0));
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

        Assert.Equal("From qualifying: Sam Smith takes pole with 24 laps in 5:00.000. Bill Jones lines up second, +0.214s, and Frank Lee third.", screen);
        Assert.StartsWith("From qualifying: Sam takes pole with twenty-four laps in five minutes flat. Bill lines up second, ", spoken);
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

        Assert.Equal("From qualifying: Sam takes pole with twenty-four laps in five minutes flat. Bill lines up second, a lap down.",
            DriverIntros.Recap(order, QualifyingMethod.TotalLaps, 3).Spoken);
    }

    [Fact]
    public void A_consecutive_laps_recap_quotes_the_average_of_the_best_run()
    {
        var order = new[] { new RecapCar("Sam", "Sam", 20, 250_000, 30_000, 9_800) };

        Assert.Equal("From qualifying: Sam takes pole averaging 10.000s over 3 laps.",
            DriverIntros.Recap(order, QualifyingMethod.BestConsecutiveLaps, 3).Screen);
    }

    [Theory]
    [InlineData(302_310, "five minutes and two point three one seconds", "5:02.310")]
    [InlineData(60_000, "one minute flat", "1:00.000")]
    [InlineData(48_500, "forty-eight point five zero seconds", "0:48.500")]
    public void A_heats_run_time_is_said_and_written_as_the_timing_sheet_has_it(long ms, string spoken, string screen)
    {
        Assert.Equal(spoken, DriverIntros.DurationSpoken(ms));
        Assert.Equal(screen, DriverIntros.Clock(ms));
    }

    [Fact]
    public void Nobody_qualified_is_no_recap()
    {
        Assert.Equal(string.Empty, DriverIntros.Recap(Array.Empty<RecapCar>(), QualifyingMethod.TotalLaps, 3).Spoken);
    }

    [Fact]
    public void The_host_throws_to_the_co_host_in_pit_lane_for_both_guests_or_one()
    {
        Assert.Equal(
            "Let's head down to pit lane, where Jess is with the Team Principal and the Technical Lead from Sam's team. Jess?",
            RaceControlControl.PitLaneThrow("Jess", principal: true, technicalLead: true, "Sam"));

        Assert.Equal(
            "Let's head down to pit lane, where Jess is with the Technical Lead from Sam's team. Jess?",
            RaceControlControl.PitLaneThrow("Jess", principal: false, technicalLead: true, "Sam"));

        Assert.Equal(
            "Let's head down to pit lane for a word with the Team Principal from the winning team.",
            RaceControlControl.PitLaneThrow(string.Empty, principal: true, technicalLead: false, string.Empty));
    }

    [Fact]
    public void Before_a_race_the_announcer_throws_to_the_co_host_with_the_drivers_team()
    {
        Assert.Equal(
            "Before we go, let's head down to pit lane. Jess, you're with Sam's team.",
            RaceControlControl.PitLaneThrowBefore("Jess", "Sam"));

        Assert.Equal(
            "Before we go, let's head down to pit lane for a word with the team.",
            RaceControlControl.PitLaneThrowBefore(string.Empty, string.Empty));
    }

    [Fact]
    public void Staging_waits_for_the_introductions_but_never_forever()
    {
        // The model's 30 seconds, then 5 a driver and 12 for the recap.
        Assert.Equal(TimeSpan.FromSeconds(30), RaceControlControl.IntroWait(0, recap: false));
        Assert.Equal(TimeSpan.FromSeconds(57), RaceControlControl.IntroWait(3, recap: true));
        Assert.Equal(TimeSpan.FromSeconds(180), RaceControlControl.IntroWait(40, recap: true));
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

/// <summary>
/// What the AI commentary is HANDED for the broadcast features (18 Oct 2026)
/// - the driver introductions, and the recap's laps led, lead changes,
/// driver ratings and sponsor - read off the prompt rather than out of a
/// model, since the model only ever phrases what it is given.
/// </summary>
public sealed class BroadcastCommentaryTests : IDisposable
{
    /// <summary>Declared first: ProfileStore.Load reads from wherever AppPaths points.</summary>
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private sealed class Scribe : ICommentaryBrain
    {
        public string Facts { get; private set; } = string.Empty;

        public string? Answer { get; set; } = "Here they are!";

        public event Action<string>? StatusMessage;

        public string Describe => "the test";

        public Task<string?> GenerateAsync(
            string systemPrompt,
            string userPrompt,
            int maxTokens,
            double temperature = 0.7,
            CancellationToken ct = default,
            TimeSpan? timeout = null)
        {
            Facts = userPrompt;
            StatusMessage?.Invoke(string.Empty);
            return Task.FromResult(Answer);
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>("ok");
    }

    private static (CommentaryService Desk, Scribe Model) Desk()
    {
        var model = new Scribe();

        var desk = new CommentaryService(
            ProfileStore.Load(),
            new AppSettings { CommentaryEnabled = true, CommentaryMaxWords = 60 })
        {
            Client = model,
        };

        return (desk, model);
    }

    [Fact]
    public async Task The_introductions_are_written_from_every_driver_in_grid_order_with_the_recap_and_sponsor()
    {
        var (desk, model) = Desk();

        var line = await desk.BuildDriverIntroductionsAsync(
            "A-Main",
            new[]
            {
                new IntroDriver("1", 1, "Sam", "Sam", "7", "Riverside", "won last time out"),
                new IntroDriver("2", 2, "Bill", "Bill"),
            },
            qualifying: "Sam took pole with twelve laps.",
            presentedBy: "Joe's Hobby Shop.");

        Assert.Contains("1. grid position 1; name Sam; racing number 7; from Riverside; form: won last time out", model.Facts);
        Assert.Contains("2. grid position 2; name Bill", model.Facts);
        Assert.Contains("Qualifying, exact: Sam took pole with twelve laps.", model.Facts);

        // 21 Oct 2026: the credit is HeatCaller's, at the top - so the
        // sponsor's tagline, when they have one, is said exactly.
        Assert.DoesNotContain("Joe's Hobby Shop", model.Facts);
        Assert.Contains("Do not mention any sponsor", model.Facts);
        Assert.Contains(SponsoredMoments.LeadInLines(SponsoredMoment.DriverIntros),
            lead => line == lead.Replace("{sponsor}", "Joe's Hobby Shop") + " Here they are!");
        Assert.Contains("Say each driver's grid position ONCE", model.Facts);
    }

    [Fact]
    public async Task Introductions_without_a_grid_tell_the_model_nobody_is_on_pole()
    {
        var (desk, model) = Desk();

        await desk.BuildDriverIntroductionsAsync(
            "Race 3",
            new[] { new IntroDriver("1", 0, "Sam", "Sam"), new IntroDriver("2", 0, "Bill", "Bill") });

        Assert.Contains("There is no grid for this race", model.Facts);
        Assert.DoesNotContain("grid position", model.Facts);
        Assert.DoesNotContain("presented by", model.Facts);
        Assert.DoesNotContain("Qualifying, exact", model.Facts);
    }

    [Fact]
    public async Task Nobody_to_introduce_asks_the_model_nothing()
    {
        var (desk, model) = Desk();

        Assert.Null(await desk.BuildDriverIntroductionsAsync("A-Main", Array.Empty<IntroDriver>()));
        Assert.Equal(string.Empty, model.Facts);
    }

    [Fact]
    public async Task The_recap_is_handed_laps_led_lead_changes_ratings_and_its_sponsor()
    {
        var (desk, model) = Desk();

        var race = new SessionRecord
        {
            Label = "A-Main",
            SessionType = RaceSessionType.Race,
            StartedAtUtc = new DateTime(2026, 10, 18, 19, 0, 0, DateTimeKind.Utc),
            EndedAtUtc = new DateTime(2026, 10, 18, 19, 10, 0, DateTimeKind.Utc),
            Results =
            {
                new SessionResultEntry { Transponder = "Sam", DriverName = "Sam", Position = 1, Laps = 12, ElapsedMs = 300_000, FastLapMs = 24_000, LapsLed = 8 },
                new SessionResultEntry { Transponder = "Bill", DriverName = "Bill", Position = 2, Laps = 12, ElapsedMs = 300_450, FastLapMs = 24_200, LapsLed = 4 },
            },
        };

        var recap = await desk.BuildRaceSummaryAsync(race, leadChanges: (3, 2), presentedBy: "Joe's Hobby Shop");

        Assert.Contains("Laps led: Sam 8 laps, Bill 4 laps", model.Facts);
        Assert.Contains("The lead changed hands 3 times, among 2 different leaders", model.Facts);
        Assert.Contains("Driver rating for this race", model.Facts);

        // 21 Oct 2026: on the recap show the sponsor is credited at the TOP,
        // by HeatCaller - the model is not handed them, so it cannot put the
        // credit at the end.
        Assert.DoesNotContain("Joe's Hobby Shop", model.Facts);
        Assert.Contains("Do not mention any sponsor", model.Facts);
        Assert.NotNull(recap);
        Assert.Contains(SponsoredMoments.LeadInLines(SponsoredMoment.RaceResult),
            lead => recap!.StartsWith(lead.Replace("{sponsor}", "Joe's Hobby Shop") + " Here they are!", StringComparison.Ordinal));
        Assert.Equal(recap, desk.LastRaceSummary);
    }

    [Fact]
    public async Task A_recap_that_never_came_back_has_no_credit_either()
    {
        var (desk, model) = Desk();
        model.Answer = null;

        var race = new SessionRecord
        {
            Label = "A-Main",
            SessionType = RaceSessionType.Race,
            StartedAtUtc = new DateTime(2026, 10, 18, 19, 0, 0, DateTimeKind.Utc),
            EndedAtUtc = new DateTime(2026, 10, 18, 19, 10, 0, DateTimeKind.Utc),
            Results =
            {
                new SessionResultEntry { Transponder = "Sam", DriverName = "Sam", Position = 1, Laps = 12, ElapsedMs = 300_000 },
            },
        };

        Assert.Null(await desk.BuildRaceSummaryAsync(race, presentedBy: "Joe's Hobby Shop"));
    }

    [Fact]
    public async Task A_recap_show_segment_opens_with_its_sponsor_and_counts_them_once_written()
    {
        var (desk, model) = Desk();
        var counted = 0;
        desk.SegmentSponsorTaken = _ => ("Joe's Hobby Shop", () => counted++);
        model.Answer = "Sam's hundredth race tonight.";

        var line = await desk.BuildSegmentAsync(SegmentKind.Honours,
            new PackedFacts("Sam started his hundredth race.", Array.Empty<string>(), 0), creditFirst: true);

        Assert.DoesNotContain("Joe's Hobby Shop", model.Facts);
        Assert.Contains(SponsoredMoments.LeadInLines(SponsoredMoment.Milestones),
            lead => line == lead.Replace("{sponsor}", "Joe's Hobby Shop") + " Sam's hundredth race tonight.");
        Assert.Equal(1, counted);
        Assert.Equal(line, desk.LastLine);
    }

    [Fact]
    public async Task A_recap_show_segment_that_never_came_back_takes_no_sponsor()
    {
        var (desk, model) = Desk();
        var asked = 0;
        desk.SegmentSponsorTaken = _ => { asked++; return ("Joe's Hobby Shop", () => { }); };
        model.Answer = null;

        Assert.Null(await desk.BuildSegmentAsync(SegmentKind.Honours,
            new PackedFacts("Sam started his hundredth race.", Array.Empty<string>(), 0), creditFirst: true));
        Assert.Equal(0, asked);
    }

    [Fact]
    public async Task A_recap_show_two_hander_opens_with_its_sponsor_in_the_hosts_voice()
    {
        var (desk, model) = Desk();
        desk.SegmentSponsorTaken = _ => ("Joe's Hobby Shop", () => { });
        model.Answer = "A: Sam took pole by a tenth.\nB: And Bill will not like that one bit.";

        var turns = await desk.BuildExchangeAsync(SegmentKind.QualifyingReport,
            new PackedFacts("Sam took pole by a tenth from Bill.", Array.Empty<string>(), 0),
            "Rick", "Jess", creditFirst: true);

        Assert.Equal(2, turns.Count);
        Assert.False(turns[0].FromCoHost);
        Assert.Contains(SponsoredMoments.LeadInLines(SponsoredMoment.QualifyingRecap),
            lead => turns[0].Line == lead.Replace("{sponsor}", "Joe's Hobby Shop") + " Sam took pole by a tenth.");
        Assert.Equal("And Bill will not like that one bit.", turns[1].Line);
    }

    [Fact]
    public async Task A_sponsored_segment_ends_with_a_whole_credit_about_its_moment()
    {
        var (desk, model) = Desk();
        desk.SegmentSponsorTaken = _ => ("Joe's Hobby Shop", () => { });
        model.Answer = "Sam and Bill are nose to tail";

        var line = await desk.BuildSegmentAsync(SegmentKind.BattleWatch,
            new PackedFacts("Sam and Bill are nose to tail for second.", Array.Empty<string>(), 0));

        // 21 Oct 2026: written by HeatCaller on the end, not by the model -
        // the model is not handed the sponsor at all.
        Assert.DoesNotContain("Joe's Hobby Shop", model.Facts);
        Assert.Contains("Do not mention any sponsor", model.Facts);
        Assert.Contains(SponsoredMoments.CreditLines(SponsoredMoment.Battle),
            credit => line == "Sam and Bill are nose to tail. " + credit.Replace("{sponsor}", "Joe's Hobby Shop"));
        Assert.Equal(line, desk.LastLine);
    }

    [Fact]
    public async Task A_segment_that_never_came_back_has_no_credit_on_its_own()
    {
        var (desk, model) = Desk();
        desk.SegmentSponsorTaken = _ => ("Joe's Hobby Shop", () => { });
        model.Answer = "  ";

        var line = await desk.BuildSegmentAsync(SegmentKind.BattleWatch,
            new PackedFacts("Sam and Bill are nose to tail for second.", Array.Empty<string>(), 0));

        Assert.True(string.IsNullOrWhiteSpace(line));
    }

    [Fact]
    public async Task Every_credit_on_a_written_line_carries_the_sponsors_tagline()
    {
        var (desk, model) = Desk();
        desk.SegmentSponsorTaken = _ => ("Joe's Hobby Shop", () => { });
        desk.SponsorTagline = name => name.StartsWith("Joe's Hobby Shop", StringComparison.Ordinal) ? "Everything for the racer" : null;
        model.Answer = "Here they are!";

        var segment = await desk.BuildSegmentAsync(SegmentKind.BattleWatch,
            new PackedFacts("Sam and Bill are nose to tail for second.", Array.Empty<string>(), 0));
        var lead = await desk.BuildSegmentAsync(SegmentKind.Honours,
            new PackedFacts("Sam started his hundredth race.", Array.Empty<string>(), 0), creditFirst: true);
        var intros = await desk.BuildDriverIntroductionsAsync(
            "A-Main", new[] { new IntroDriver("1", 1, "Sam", "Sam") }, presentedBy: "Joe's Hobby Shop.");
        var recap = await desk.BuildRaceSummaryAsync(new SessionRecord
        {
            Label = "A-Main",
            SessionType = RaceSessionType.Race,
            StartedAtUtc = new DateTime(2026, 10, 18, 19, 0, 0, DateTimeKind.Utc),
            EndedAtUtc = new DateTime(2026, 10, 18, 19, 10, 0, DateTimeKind.Utc),
            Results = { new SessionResultEntry { Transponder = "Sam", DriverName = "Sam", Position = 1, Laps = 12, ElapsedMs = 300_000 } },
        }, presentedBy: "Joe's Hobby Shop");

        Assert.EndsWith("Joe's Hobby Shop! Everything for the racer.", segment);
        Assert.Contains("Joe's Hobby Shop! Everything for the racer. Here they are!", lead);
        Assert.Contains("Joe's Hobby Shop! Everything for the racer. Here they are!", intros);
        Assert.Contains("Joe's Hobby Shop! Everything for the racer. Here they are!", recap);
        Assert.DoesNotContain("Everything for the racer", model.Facts);
    }

    [Fact]
    public async Task A_recap_without_the_broadcast_figures_carries_none_of_them()
    {
        var (desk, model) = Desk();

        var race = new SessionRecord
        {
            Label = "A-Main",
            SessionType = RaceSessionType.Race,
            StartedAtUtc = new DateTime(2026, 10, 18, 19, 0, 0, DateTimeKind.Utc),
            EndedAtUtc = new DateTime(2026, 10, 18, 19, 10, 0, DateTimeKind.Utc),
            Results =
            {
                new SessionResultEntry { Transponder = "Sam", DriverName = "Sam", Position = 1, Laps = 12, ElapsedMs = 300_000 },
                new SessionResultEntry { Transponder = "Bill", DriverName = "Bill", Position = 2, Laps = 12, ElapsedMs = 300_450 },
            },
        };

        await desk.BuildRaceSummaryAsync(race);

        Assert.DoesNotContain("Laps led:", model.Facts);
        Assert.DoesNotContain("lead changed hands", model.Facts);
        Assert.DoesNotContain("presented by", model.Facts);
    }
}
