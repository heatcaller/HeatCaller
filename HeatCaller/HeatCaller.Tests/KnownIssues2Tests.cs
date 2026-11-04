namespace HeatCaller.Tests;

/// <summary>
/// The commentary and show items off the 22 Oct 2026 known-issues list:
/// the drama-shaped show fed at last, the minutes to the next race, a
/// hand-back with no gap to name, the hand-over following the club's ticks,
/// and "Segments run" on the Commentary page.
/// </summary>
public sealed class KnownIssues2Tests : IDisposable
{
    /// <summary>A folder of its own: the director's stores are loaded from it.</summary>
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // ------------------------------------------------------------------
    // The drama-shaped show
    // ------------------------------------------------------------------

    /// <summary>A two-car race: the winner, then second place a given way behind.</summary>
    private static SessionRecord Race(int secondLaps, long secondElapsedMs, long fastLapMs = 14_000)
    {
        var winnerLaps = Enumerable.Repeat(15_000L, 10).ToList();

        return new SessionRecord
        {
            SessionType = RaceSessionType.Race,
            TrackLayoutName = "Club Oval",
            StartedAtUtc = new DateTime(2026, 10, 22, 19, 0, 0, DateTimeKind.Utc),
            EndedAtUtc = new DateTime(2026, 10, 22, 19, 5, 0, DateTimeKind.Utc),
            Results = new List<SessionResultEntry>
            {
                new() { Position = 1, Transponder = "1", DriverName = "Ann", Laps = 10, ElapsedMs = 150_000, FastLapMs = fastLapMs, LapTimesMs = winnerLaps },
                new() { Position = 2, Transponder = "2", DriverName = "Bob", Laps = secondLaps, ElapsedMs = secondElapsedMs, FastLapMs = 14_500 },
            },
        };
    }

    [Fact]
    public void A_photo_finish_earns_the_bigger_open()
    {
        var drama = ShowDrama.Of(Race(10, 150_200), layout: null);

        Assert.Equal(0.2, drama.WinningMarginSeconds!.Value, 3);
        Assert.Equal(0, drama.LapsDown);
        Assert.True(drama.Notable);
    }

    [Fact]
    public void A_comfortable_win_and_a_lapped_second_place_do_not()
    {
        Assert.False(ShowDrama.Of(Race(10, 158_000), layout: null).Notable);

        // Two laps down is the margin, however small the seconds look -
        // DescribeMargin's rule, so the music never swells for a race the
        // words are about to call comfortable.
        var lapped = ShowDrama.Of(Race(8, 150_100), layout: null);

        Assert.Equal(2, lapped.LapsDown);
        Assert.Null(lapped.WinningMarginSeconds);
        Assert.False(lapped.Notable);
    }

    [Fact]
    public void Only_a_record_set_in_this_race_is_drama()
    {
        var race = Race(10, 160_000, fastLapMs: 13_900);

        var tonight = new TrackLayout { Name = "Club Oval", RecordLapMs = 13_900, RecordSetUtc = race.StartedAtUtc.AddMinutes(3) };
        var lastMonth = new TrackLayout { Name = "Club Oval", RecordLapMs = 13_900, RecordSetUtc = race.StartedAtUtc.AddDays(-30) };

        Assert.True(ShowDrama.Of(race, tonight).TrackRecord);
        Assert.True(ShowDrama.Of(race, tonight).Notable);
        Assert.False(ShowDrama.Of(race, lastMonth).TrackRecord);
    }

    [Fact]
    public void No_race_is_no_drama()
    {
        Assert.Equal(ShowDrama.None, ShowDrama.Of(null, null));
        Assert.Equal(ShowDrama.None, ShowDrama.Of(new SessionRecord(), null));
    }

    [Fact]
    public void A_notable_race_lengthens_both_ends_of_the_show_and_nothing_else()
    {
        var plain = ShowBilling.For(ShowCue.Rejoin);
        var bigger = ShowBilling.For(ShowCue.Rejoin, ShowDrama.Finish(0.1));

        Assert.True(bigger.BiggerOpen);
        Assert.Equal(plain.AirSeconds + ShowBilling.BiggerAir, bigger.AirSeconds, 6);
        Assert.Equal(plain.LiftSeconds + ShowBilling.LongerLift, bigger.LiftSeconds, 6);
        Assert.Equal(plain.Cue, bigger.Cue);
        Assert.Equal(plain.EnterOpenerLate, bigger.EnterOpenerLate);
    }

    // ------------------------------------------------------------------
    // The hand-over follows the ticks
    // ------------------------------------------------------------------

    [Fact]
    public void The_hand_over_is_dressed_while_anything_is_ticked_and_plain_when_nothing_is()
    {
        Assert.True(ShowSegments.Treats(ShowSegments.Parse(null), SegmentKind.ComingUpNext));
        Assert.True(ShowSegments.Treats(ShowSegments.Parse("RaceRecap"), SegmentKind.ComingUpNext));
        Assert.False(ShowSegments.Treats(ShowSegments.Parse(ShowSegments.Nothing), SegmentKind.ComingUpNext));

        // Every other segment is its own tick, as it always was.
        Assert.True(ShowSegments.Treats(ShowSegments.Parse(null), SegmentKind.ColdOpen));
        Assert.False(ShowSegments.Treats(ShowSegments.Parse(null), SegmentKind.RaceRecap));

        // And still no box of its own.
        Assert.DoesNotContain(SegmentKind.ComingUpNext, ShowSegments.Candidates);
    }

    // ------------------------------------------------------------------
    // The minutes to the next race
    // ------------------------------------------------------------------

    private static readonly DateTime HalfSeven = new(2026, 10, 22, 19, 30, 0, DateTimeKind.Local);

    [Fact]
    public void The_next_race_is_its_slot_moved_by_how_late_the_night_is()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), CommentaryReaders.NextRaceIn("19:40", true, false, null, HalfSeven));
        Assert.Equal(TimeSpan.FromMinutes(15), CommentaryReaders.NextRaceIn("19:40", true, false, 5, HalfSeven));

        // Its slot gone by, but the night running later than that.
        Assert.Equal(TimeSpan.FromMinutes(4), CommentaryReaders.NextRaceIn("19:26", true, false, 8, HalfSeven));
    }

    [Fact]
    public void No_estimate_where_the_timetable_cannot_honestly_give_one()
    {
        Assert.Null(CommentaryReaders.NextRaceIn(null, true, false, null, HalfSeven));
        Assert.Null(CommentaryReaders.NextRaceIn("TBD", true, false, null, HalfSeven));

        // Lunch is not a race; a race under way has no "next".
        Assert.Null(CommentaryReaders.NextRaceIn("19:40", false, false, null, HalfSeven));
        Assert.Null(CommentaryReaders.NextRaceIn("19:40", true, true, null, HalfSeven));

        // The grid is forming, or it is a break.
        Assert.Null(CommentaryReaders.NextRaceIn("19:31", true, false, null, HalfSeven));
        Assert.Null(CommentaryReaders.NextRaceIn("20:30", true, false, null, HalfSeven));
        Assert.Null(CommentaryReaders.NextRaceIn("19:10", true, false, null, HalfSeven));
    }

    [Fact]
    public void A_next_race_past_midnight_is_still_minutes_away()
    {
        var late = new DateTime(2026, 10, 22, 23, 55, 0, DateTimeKind.Local);

        Assert.Equal(TimeSpan.FromMinutes(10), CommentaryReaders.NextRaceIn("00:05", true, false, null, late));
    }

    // ------------------------------------------------------------------
    // A hand-back with no gap to name
    // ------------------------------------------------------------------

    private static SpokenSegment Recap() => new(SegmentKind.RaceRecap, "the recap", Array.Empty<ExchangeTurn>());

    private static ShowBlock? Around(ContinuityFacts facts) =>
        ContinuityShow.Around(Recap(), facts, new ContinuityVoice(seed: 2210), () => { }, (_, _) => { }, _ => { });

    [Fact]
    public void After_a_freeform_race_the_show_hands_back_without_promising_a_time()
    {
        var order = Around(new ContinuityFacts(null, Array.Empty<UpcomingRound>(), null, Freeform: true));

        Assert.NotNull(order);
        Assert.Equal(2, order.Items.Count);
        Assert.Equal(SegmentKind.RaceRecap, order.Items[0].Kind);

        var words = string.Join(" ", order.Items[1].Parts.Select(part => part.Words));

        Assert.DoesNotContain("moment", words, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("minute", words, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_night_nothing_is_known_about_still_says_nothing()
    {
        Assert.Null(Around(ContinuityFacts.Unknown));
        Assert.Null(Around(new ContinuityFacts(null, Array.Empty<UpcomingRound>(), null)));
    }

    [Fact]
    public void A_programmed_night_with_nothing_left_is_still_the_sign_off_s_to_close()
    {
        var over = new ContinuityFacts(new RoundJustRun("A-Main", "Box Stock"), Array.Empty<UpcomingRound>(), null);

        Assert.Equal(2, Around(over)!.Items.Count);
    }

    [Fact]
    public void Every_no_gap_phrasing_is_a_whole_sentence()
    {
        var voice = new ContinuityVoice(seed: 7);

        for (var i = 0; i < 8; i++)
        {
            var line = CommentaryContinuity.StayWithUs(voice);

            Assert.False(string.IsNullOrWhiteSpace(line));
            Assert.EndsWith(".", line);
        }
    }

    // ------------------------------------------------------------------
    // "Segments run"
    // ------------------------------------------------------------------

    // Folded into the tick boxes on 3 Nov 2026 - see AppSettings.FoldLowestTier.

    [Fact]
    public void A_hand_edited_tier_is_clamped()
    {
        Assert.Equal(SegmentTier.Essential, CommentaryDirector.LowestTierOf(-4));
        Assert.Equal(SegmentTier.Flourish, CommentaryDirector.LowestTierOf(99));
    }

    [Fact]
    public void A_saved_segments_run_choice_becomes_unticked_boxes()
    {
        var settings = new AppSettings
        {
            CommentaryLowestTier = (int)SegmentTier.Core,
            CommentarySegmentsOff = CommentaryBudget.SaveSwitches(new[] { SegmentKind.PaceNote }),
        };

        Assert.True(settings.FoldLowestTier());

        var off = CommentaryBudget.ParseSwitches(settings.CommentarySegmentsOff);

        Assert.Equal((int)SegmentTier.Flourish, settings.CommentaryLowestTier);

        // Everything above the old choice is off, what was already off stays
        // off, and what the choice kept is still ticked.
        Assert.All(CommentaryBudget.AllSpecs.Where(spec => spec.Tier > SegmentTier.Core),
            spec => Assert.Contains(spec.Kind, off));
        Assert.Contains(SegmentKind.PaceNote, off);
        Assert.DoesNotContain(SegmentKind.BattleWatch, off);
    }

    [Fact]
    public void Everything_ticked_folds_to_no_change()
    {
        var settings = new AppSettings();

        Assert.False(settings.FoldLowestTier());
        Assert.Equal(string.Empty, settings.CommentarySegmentsOff ?? string.Empty);
    }
}
