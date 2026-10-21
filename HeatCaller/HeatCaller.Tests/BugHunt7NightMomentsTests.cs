namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 7, the night's moments and what is built on them: a
/// disqualified driver credited with the race's best moments, the wrong
/// driver named as the one the first lead was taken from, a driver's own
/// piece losing their moments to everybody else's, a podium clip losing the
/// finish, the honours stopping for the night when the hand-back ran dry,
/// and the continuity lines forgetting the night on a restart.
/// </summary>
public sealed class BugHunt7NightMomentsTests : IDisposable
{
    private readonly TempAppData _appData = new();

    public void Dispose() => _appData.Dispose();

    private static readonly DateTime Green = new(2026, 10, 12, 19, 0, 0, DateTimeKind.Utc);

    private static SessionResultEntry Row(string name, int position, long[] laps, int startPosition = 0, int lapsLed = 0) =>
        new()
        {
            Transponder = name.ToLowerInvariant(),
            DriverName = name,
            Position = position,
            Laps = laps.Length,
            LapTimesMs = laps.ToList(),
            StartPosition = startPosition,
            LapsLed = lapsLed,
            ElapsedMs = laps.Sum(),
        };

    /// <summary>As ResultEditor leaves a DQ: the outcome set, the laps to nought, the lap times kept.</summary>
    private static SessionResultEntry Disqualified(SessionResultEntry row)
    {
        row.Outcome = ResultEditor.Dq;
        row.Status = ResultEditor.Dq;
        row.Laps = 0;
        return row;
    }

    private static SessionRecord Race(string id, string label, params SessionResultEntry[] rows) =>
        new()
        {
            Id = id,
            SessionType = RaceSessionType.Race,
            Label = label,
            TrackLayoutName = "Winter Indoor",
            StartedAtUtc = Green,
            EndedAtUtc = Green.AddMinutes(8),
            Results = rows.ToList(),
        };

    // ------------------------------------------------------------------
    //  A disqualified driver is not a moment
    // ------------------------------------------------------------------

    [Fact]
    public void A_driver_disqualified_after_a_last_lap_lead_gets_no_moment_for_it()
    {
        // Sam takes the lead on the last lap and sets the quickest lap, and
        // is then thrown out at tech. Bill is moved up to the win.
        var found = NightMoments.ForRace(Race(
            "race-1",
            "A-Main",
            Disqualified(Row("Sam", 2, new long[] { 10500, 9000, 9000, 9000, 8500 }, startPosition: 2)),
            Row("Bill", 1, new long[] { 10000, 9000, 9000, 9000, 9500 }, startPosition: 1, lapsLed: 4)));

        Assert.DoesNotContain(found, one => one.Transponder == "sam");
        Assert.DoesNotContain(found, one => one.What is NightMoments.Kind.LastLapLead or NightMoments.Kind.LeadChange);
        Assert.Equal("bill", Assert.Single(found, one => one.What == NightMoments.Kind.FastestLap).Transponder);
    }

    [Fact]
    public void A_disqualified_car_is_not_on_the_timeline_the_real_leads_are_read_from()
    {
        // Dee was in front all race and is disqualified. Without her, Ann
        // taking Bill on the last lap IS the last-lap lead - with her left
        // on the timeline it was nothing, because Dee was still "ahead".
        var found = NightMoments.ForRace(Race(
            "race-1",
            "A-Main",
            Disqualified(Row("Dee", 3, new long[] { 9000, 9000, 9000, 9000, 9000 }, lapsLed: 5)),
            Row("Ann", 1, new long[] { 10500, 9000, 9000, 9000, 8500 }),
            Row("Bill", 2, new long[] { 10000, 9000, 9000, 9000, 9500 }, lapsLed: 4)));

        var lead = Assert.Single(found, one => one.What == NightMoments.Kind.LastLapLead);

        Assert.Equal("ann", lead.Transponder);
        Assert.Equal("bill", lead.Beaten);
        Assert.DoesNotContain(found, one => one.Transponder == "dee");
    }

    // ------------------------------------------------------------------
    //  Who the first lead was taken from
    // ------------------------------------------------------------------

    [Fact]
    public void The_first_lead_change_names_the_driver_who_led_from_the_start()
    {
        // Ann leads laps 1 and 2, Bill takes it on lap 3 and holds lap 4,
        // and Cal leads from lap 5. Bill took the lead from ANN - not from
        // Cal, who had not led yet but finished ahead of her.
        var found = NightMoments.ForRace(Race(
            "race-1",
            "A-Main",
            Row("Cal", 1, new long[] { 10800, 10000, 9700, 10000, 9500, 10000 }, lapsLed: 2),
            Row("Bill", 2, new long[] { 10500, 10000, 9500, 10000, 12000, 11000 }, lapsLed: 2),
            Row("Ann", 3, new long[] { 10000, 10000, 11000, 11000, 11000, 11000 }, lapsLed: 2)));

        var leads = found
            .Where(one => one.What is NightMoments.Kind.LeadChange or NightMoments.Kind.LastLapLead)
            .OrderBy(one => one.Lap)
            .ToList();

        Assert.Equal(2, leads.Count);

        Assert.Equal("bill", leads[0].Transponder);
        Assert.Equal(3, leads[0].Lap);
        Assert.Equal("ann", leads[0].Beaten);

        Assert.Equal("cal", leads[1].Transponder);
        Assert.Equal("bill", leads[1].Beaten);
    }

    // ------------------------------------------------------------------
    //  A driver's own piece keeps their own moments
    // ------------------------------------------------------------------

    [Fact]
    public void A_drivers_piece_keeps_their_moment_when_the_rest_of_the_field_has_more_than_a_nights_worth()
    {
        // Thirteen drivers each come through four places, which is thirteen
        // charges - more than the night's twelve, and every one of them
        // ranks above Bill's fastest lap. Capped across the whole field
        // before it was cut down to Bill, his piece lost his own moment.
        var rows = new List<SessionResultEntry>();

        for (var place = 1; place <= 13; place++)
        {
            rows.Add(Row($"D{place:00}", place, new long[] { 9000, 9000 }, startPosition: place + 4));
        }

        rows.Add(Row("Bill", 14, new long[] { 8000, 8100 }));

        // WHY (20 Oct 2026): a charge now counts only the real cars that
        // lined up ahead, so with slots 1 to 4 empty the D drivers made up
        // nothing and the night's cap was never reached. Four cars on those
        // slots, finishing behind, give each D driver a real four places.
        for (var slot = 1; slot <= 4; slot++)
        {
            rows.Add(Row($"F{slot}", 14 + slot, new long[] { 9500, 9500 }, startPosition: slot));
        }

        var facts = DriverCut.FactsFor("bill", "Bill", new List<SessionRecord> { Race("heat-1", "Heat 1", rows.ToArray()) });

        Assert.Contains("Bill set the fastest lap in Heat 1", facts.Text, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    //  A podium clip keeps the finish
    // ------------------------------------------------------------------

    [Fact]
    public void A_winner_who_came_through_the_field_still_gets_the_finish_and_the_interview()
    {
        // Seven passes 25 seconds apart - seven separate stretches, 126
        // seconds of them - and the flag at 200 seconds. Spent earliest
        // first, the passes used the budget and the podium was dropped.
        var record = Race("race-1", "A-Main", Row("Ann", 1, new long[] { 10000 }));

        var marks = new List<RaceHighlights.Mark>();

        for (var at = 20_000L; at <= 170_000L; at += 25_000L)
        {
            marks.Add(new RaceHighlights.Mark("ann", RaceHighlights.Moment.Pass, at));
        }

        marks.Add(new RaceHighlights.Mark("ann", RaceHighlights.Moment.Podium, 200_000));

        var clip = Assert.Single(RaceHighlights.ClipsFor(record, marks, greenAtMs: 0, lengthMs: 400_000));

        Assert.Contains(clip.Windows, window => window.StartMs == 200_000 && window.EndMs == 200_000 + RaceHighlights.PodiumTailMs);
        Assert.True(clip.LengthMs <= RaceHighlights.MaxPerDriverMs, $"The clip runs {clip.LengthMs}ms.");

        // Still in the order the race ran, so the finish is the end of it.
        Assert.Equal(clip.Windows.OrderBy(window => window.StartMs).ToList(), clip.Windows);
        Assert.Equal(200_000L, clip.Windows[^1].StartMs);
    }

    [Fact]
    public void A_clip_that_fits_is_not_changed_by_keeping_the_podium()
    {
        var record = Race("race-1", "A-Main", Row("Ann", 1, new long[] { 10000 }));

        var marks = new[]
        {
            new RaceHighlights.Mark("ann", RaceHighlights.Moment.Pass, 20_000),
            new RaceHighlights.Mark("ann", RaceHighlights.Moment.Podium, 100_000),
        };

        var clip = Assert.Single(RaceHighlights.ClipsFor(record, marks, greenAtMs: 0, lengthMs: 400_000));

        Assert.Equal(2, clip.Windows.Count);
        Assert.Equal(20_000 - RaceHighlights.LeadInMs, clip.Windows[0].StartMs);
        Assert.Equal(100_000L, clip.Windows[1].StartMs);
    }

    // ------------------------------------------------------------------
    //  The honours do not stop for the night
    // ------------------------------------------------------------------

    private static readonly AchievementUnlock[] OneBadge =
    {
        new(
            "Billy Bob",
            "Billy Bob",
            new AchievementDefinition("id-Nail-Biter", "Nail-Biter", "Nail-Biter, earned.", AchievementCategory.Race, "star"),
            string.Empty),
    };

    [Fact]
    public void The_honours_keep_coming_once_every_hand_back_has_been_heard()
    {
        // No reader named: twelve hand-backs and sixteen throws. From the
        // thirteenth race the hand-back was dry and the whole segment went,
        // with throws still to spare.
        var voice = new ContinuityVoice(seed: 20261013);
        var previousBack = string.Empty;

        for (var race = 1; race <= 16; race++)
        {
            var turns = RaceHonours.Segment(OneBadge, null, voice);

            Assert.True(turns.Count == 3, $"No honours segment in race {race}.");
            Assert.NotEqual(previousBack, turns[2].Line);

            voice.Said(turns[0].Line);
            voice.Said(turns[2].Line);
            previousBack = turns[2].Line;
        }

        // The throw still never repeats: once every one is heard, the
        // segment is dropped as before.
        Assert.Empty(RaceHonours.Segment(OneBadge, null, voice));
    }

    [Fact]
    public void A_repeated_line_is_never_the_same_words_twice_running()
    {
        var voice = new ContinuityVoice(seed: 7);
        var lines = new[] { "One.", "Two.", "Three." };
        var previous = string.Empty;

        for (var i = 0; i < 30; i++)
        {
            var line = voice.PickOrRepeat("test", lines);

            Assert.NotNull(line);
            Assert.NotEqual(previous, line);

            voice.Said(line);
            previous = line!;
        }

        // Pick itself still refuses, which is what every bridge relies on.
        Assert.Null(voice.Pick("test", lines));
    }

    // ------------------------------------------------------------------
    //  The continuity lines survive a restart
    // ------------------------------------------------------------------

    [Fact]
    public void A_continuity_line_heard_before_a_restart_is_still_heard_after_it()
    {
        var before = new ContinuityVoice();
        before.NewMeeting("2026-10-13");
        before.Said("That was Q2 Heat 1 for Stock.");

        var after = new ContinuityVoice();
        after.Restore(before.Save(), "2026-10-13");

        Assert.True(after.AlreadyHeard("That was Q2 Heat 1 for Stock."));
        Assert.Equal(1, after.Heard);
    }

    [Fact]
    public void Last_weeks_continuity_lines_are_this_weeks_again()
    {
        var before = new ContinuityVoice();
        before.NewMeeting("2026-10-06");
        before.Said("That was Q2 Heat 1 for Stock.");

        var after = new ContinuityVoice();
        after.Restore(before.Save(), "2026-10-13");

        Assert.Equal(0, after.Heard);
    }

    [Fact]
    public void An_unreadable_saved_night_is_a_fresh_night()
    {
        var after = new ContinuityVoice();
        after.Restore("{ not json", "2026-10-13");

        Assert.Equal(0, after.Heard);
    }

    [Fact]
    public void Restoring_on_every_tick_does_not_lose_what_was_heard_since()
    {
        // The host calls the director's NewMeeting twice a second.
        var voice = new ContinuityVoice();
        voice.Restore(string.Empty, "2026-10-13");
        voice.Said("Still to come, the A-main.");

        voice.Restore(string.Empty, "2026-10-13");

        Assert.True(voice.AlreadyHeard("Still to come, the A-main."));
    }

    [Fact]
    public void The_director_remembers_the_continuity_lines_across_a_restart()
    {
        // No paid segment runs - the AI is off - and the lines are still
        // written down as they are heard.
        var settings = new AppSettings();
        var commentary = new CommentaryService(ProfileStore.Load(), settings);

        var director = new CommentaryDirector(commentary, settings);
        director.NewMeeting("day-1");
        director.Continuity.Said("That was Q2 Heat 1 for Stock.");

        var back = new CommentaryDirector(commentary, settings);
        back.NewMeeting("day-1");

        Assert.True(back.Continuity.AlreadyHeard("That was Q2 Heat 1 for Stock."));
    }
}
