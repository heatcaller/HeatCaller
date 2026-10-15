namespace HeatCaller.Tests;

/// <summary>
/// The pacing dial: how much room the announcer leaves between the calls
/// it makes off the timing loop.
///
/// THE REPORT THIS CAME FROM is in AnnouncerDensity's own notes - an
/// eight-lap demo with three cars in it that threw away about twenty lap
/// calls, from a club that had already turned the chattiness dial down
/// looking for the control that would stop it. Most of this file is the
/// two properties that make this dial a different control from that one:
/// it is about the FIELD, and its default is not the old behaviour.
/// </summary>
public sealed class AnnouncerDensityTests
{
    private static RaceStature Heat => new(RaceBilling.Heat, false);

    private static RaceStature TopMain => new(RaceBilling.TopMain, false);

    private static RaceStature Practice => new(RaceBilling.Practice, false);

    // ------------------------------------------------------------------
    //  The dial itself
    // ------------------------------------------------------------------

    [Fact]
    public void A_key_nobody_shipped_reads_as_the_default()
    {
        // A settings file from a later build, restored from a backup. The
        // club gets the shipped pacing rather than a gap nobody chose -
        // and not an exception, which on this path is a race with no
        // announcer.
        Assert.Same(AnnouncerDensity.Balanced, AnnouncerDensity.For("a-stop-from-2027"));
        Assert.Same(AnnouncerDensity.Balanced, AnnouncerDensity.For(null));
        Assert.Same(AnnouncerDensity.Balanced, AnnouncerDensity.For("   "));
    }

    [Fact]
    public void A_key_is_read_whatever_case_and_spacing_it_was_written_in()
    {
        foreach (var level in AnnouncerDensity.All)
        {
            Assert.Same(level, AnnouncerDensity.For(level.Key));
            Assert.Same(level, AnnouncerDensity.For(level.Key.ToUpperInvariant()));
            Assert.Same(level, AnnouncerDensity.For("  " + level.Key + "  "));
        }
    }

    [Fact]
    public void No_two_stops_share_a_key()
    {
        // A duplicate would make For() return whichever came first, and the
        // club's picker would quietly set the other one.
        var keys = AnnouncerDensity.All.Select(one => one.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Every_stop_has_something_for_a_club_to_read()
    {
        foreach (var level in AnnouncerDensity.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(level.Key), "a stop with no key cannot be stored");
            Assert.False(string.IsNullOrWhiteSpace(level.Name), "a stop with no name cannot be picked");
            Assert.False(string.IsNullOrWhiteSpace(level.Blurb), "a stop with no blurb explains nothing");
        }
    }

    [Fact]
    public void The_dial_reads_quietest_first_like_a_volume_control()
    {
        // The order IS the control - see the note on All. Wall to wall is
        // nought and sorts last on its own; every stop before it has to be
        // a smaller gap than the one before.
        var scales = AnnouncerDensity.All.Select(one => one.Scale).ToList();

        for (var i = 1; i < scales.Count; i++)
        {
            Assert.True(
                scales[i] < scales[i - 1],
                $"stop {i} scales by {scales[i]}, which is not quieter than {scales[i - 1]} before it");
        }
    }

    // ------------------------------------------------------------------
    //  The default, which is the departure
    // ------------------------------------------------------------------

    [Fact]
    public void Nothing_set_is_paced_rather_than_left_as_it_was()
    {
        // THE DEPARTURE, and the whole point of the feature. Every other
        // dial in this layer ships on what an existing club already had;
        // this one cannot, because what it already had is the fault being
        // fixed. A club that has never opened the page gets a gap.
        var gap = AnnouncerDensity.GapSeconds(Heat, null);

        Assert.True(gap > 0, "a club that has set nothing is still paced");
        Assert.Equal(AnnouncerDensity.GapSeconds(Heat, "balanced"), gap);
    }

    [Fact]
    public void The_old_behaviour_is_still_on_the_dial()
    {
        // A club that liked the old density, or runs one car at a time and
        // never had a collision, must not be told its only option is a gap
        // it does not want.
        Assert.Contains(AnnouncerDensity.WallToWall, AnnouncerDensity.All);

        foreach (var stature in new[] { Practice, Heat, TopMain })
        {
            Assert.Equal(0d, AnnouncerDensity.GapSeconds(stature, AnnouncerDensity.WallToWall.Key));
        }
    }

    [Fact]
    public void Wall_to_wall_says_what_it_costs()
    {
        // A stop that quietly loses most of the announcer's work has to say
        // so on the page, because the report this feature came from is a
        // club that could not work that out for itself.
        var said = AnnouncerDensity.Describe(AnnouncerDensity.WallToWall.Key);

        Assert.Contains("lost", said, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    //  The shape: quiet in practice, tight in the main
    // ------------------------------------------------------------------

    [Fact]
    public void Practice_gets_the_most_room_and_the_top_main_the_least()
    {
        // The same curve AirtimeCap has, upside down. A room that hears the
        // announcer working hardest in the A-main learns which race counts
        // without being told.
        var practice = AnnouncerDensity.GapSeconds(Practice, null);
        var heat = AnnouncerDensity.GapSeconds(Heat, null);
        var lesser = AnnouncerDensity.GapSeconds(new RaceStature(RaceBilling.LesserMain, false), null);
        var top = AnnouncerDensity.GapSeconds(TopMain, null);

        Assert.True(practice > heat, $"practice ({practice}s) should be roomier than a heat ({heat}s)");
        Assert.True(heat > lesser, $"a heat ({heat}s) should be roomier than a main ({lesser}s)");
        Assert.True(lesser > top, $"a main ({lesser}s) should be roomier than the top main ({top}s)");
    }

    [Fact]
    public void The_curve_survives_every_stop_on_the_dial()
    {
        // The argument ShowChatter makes for itself: the stature decides
        // the shape and the dial is the volume, so the quiet-early-loud-
        // late curve has to hold at every stop. Busy in a heat is still
        // roomier than Busy in the top main.
        foreach (var level in AnnouncerDensity.All.Where(one => one.Scale > 0))
        {
            var heat = AnnouncerDensity.GapSeconds(Heat, level.Key);
            var top = AnnouncerDensity.GapSeconds(TopMain, level.Key);

            Assert.True(heat > top, $"at {level.Name} a heat ({heat}s) is not roomier than the top main ({top}s)");
        }
    }

    [Fact]
    public void The_last_race_of_the_night_is_worked_a_little_harder()
    {
        // Everyone still in the hall is watching this one and there is no
        // later race to save the air for - the reason AirtimeCap adds two
        // segments to it.
        var heat = AnnouncerDensity.GapSeconds(Heat, null);
        var lastHeat = AnnouncerDensity.GapSeconds(new RaceStature(RaceBilling.Heat, true), null);

        Assert.True(lastHeat < heat, $"the last heat of the night ({lastHeat}s) is not tighter than a heat ({heat}s)");
    }

    [Fact]
    public void A_club_with_no_programme_is_still_paced()
    {
        // The second departure, and deliberate: a club with no programme is
        // a club running freeform practice with whoever turned up, which is
        // the busiest loop in the app and the case the report came from.
        var unknown = AnnouncerDensity.GapSeconds(RaceStature.Unknown, null);

        Assert.True(unknown > 0, "a race with nothing to size it by is still paced");
    }

    [Fact]
    public void A_gap_is_never_longer_than_the_ceiling_or_shorter_than_the_floor()
    {
        foreach (var level in AnnouncerDensity.All.Where(one => one.Scale > 0))
        {
            foreach (var billing in Enum.GetValues<RaceBilling>())
            {
                foreach (var last in new[] { false, true })
                {
                    var gap = AnnouncerDensity.GapSeconds(new RaceStature(billing, last), level.Key);

                    Assert.InRange(gap, AnnouncerDensity.ShortestGap, AnnouncerDensity.LongestGap);
                }
            }
        }
    }

    // ------------------------------------------------------------------
    //  What breaks in anyway
    // ------------------------------------------------------------------

    [Fact]
    public void A_change_of_lead_is_never_held_for_pacing()
    {
        // The fault this guard exists to prevent: a gap protecting
        // somebody's lap time from two seconds ago must not cost the room
        // the lead of the race changing hands.
        Assert.True(AnnouncerDensity.WorthBreakingIn(AnnouncerDensity.Commanding, 100));
        Assert.True(AnnouncerDensity.WorthBreakingIn(AnnouncerDensity.Commanding, 90));
        Assert.True(AnnouncerDensity.WorthBreakingIn(AnnouncerDensity.Commanding, 30));
    }

    [Fact]
    public void A_clearly_bigger_call_breaks_in_and_a_near_equal_one_does_not()
    {
        // Against the shipped weights: a fast lap (90) over a lap count
        // (40) or a lap time (30) breaks in; a lap time does not break in
        // over a lap count. The gap exists to stop the SMALL calls filling
        // the air, and those are the ones within a few points of each
        // other.
        Assert.True(AnnouncerDensity.WorthBreakingIn(90, 40));
        Assert.True(AnnouncerDensity.WorthBreakingIn(90, 30));

        Assert.False(AnnouncerDensity.WorthBreakingIn(30, 40));
        Assert.False(AnnouncerDensity.WorthBreakingIn(40, 30));
        Assert.False(AnnouncerDensity.WorthBreakingIn(60, 40));
    }

    // ------------------------------------------------------------------
    //  What the Commentary page shows
    // ------------------------------------------------------------------

    [Fact]
    public void Every_stop_describes_what_it_actually_buys()
    {
        // Worked out from the real tables rather than written down, so a
        // change to either cannot leave the page lying - the rule
        // ShowChatter.Describe goes by.
        foreach (var level in AnnouncerDensity.All)
        {
            var said = AnnouncerDensity.Describe(level.Key);

            Assert.False(string.IsNullOrWhiteSpace(said));
            Assert.Contains(level.Blurb, said);
        }
    }

    [Fact]
    public void A_stop_with_a_gap_names_the_real_figures()
    {
        var said = AnnouncerDensity.Describe("balanced");
        var heat = AnnouncerDensity.Gap(AnnouncerDensity.GapSeconds(Heat, "balanced"));

        Assert.Contains(heat, said);
    }

    [Fact]
    public void An_unknown_key_is_described_as_the_default()
    {
        Assert.Equal(AnnouncerDensity.Describe(null), AnnouncerDensity.Describe("a-stop-from-2027"));
    }

    [Fact]
    public void Every_page_line_promises_that_an_instruction_is_never_held()
    {
        // A dial about taste that could swallow a yellow is not a pacing
        // dial, it is a bug - and a club reading this page has to be able
        // to see that promise.
        foreach (var level in AnnouncerDensity.All.Where(one => one.Scale > 0))
        {
            Assert.Contains("flag", AnnouncerDensity.Describe(level.Key), StringComparison.OrdinalIgnoreCase);
        }
    }

    // ------------------------------------------------------------------
    //  The wording of a gap
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(4.0, "4 seconds")]
    [InlineData(1.0, "1 second")]
    [InlineData(6.5, "6 and a half seconds")]
    [InlineData(0.5, "half a second")]
    [InlineData(12.0, "12 seconds")]
    public void A_gap_is_said_the_way_a_club_would_say_it(double seconds, string expected)
    {
        Assert.Equal(expected, AnnouncerDensity.Gap(seconds));
    }
}
