namespace HeatCaller.Tests;

/// <summary>
/// The record watch: a driver's running PACE against the mark that still
/// stands, said before it falls.
///
/// The thing only a computer can do, and the thing most easily made
/// ridiculous. A human commentator calls a record after it happens because
/// that is when they find out; this app can see one coming four laps out,
/// and the whole risk is that it then tells a room the record is going
/// every time somebody has one good lap. Most of this file is the silence.
/// </summary>
public sealed class RecordWatchTests
{
    private const double Record = 5.04;

    private static RecordWatch Watching(double? record = Record, string? holder = "Dave")
    {
        var watch = new RecordWatch();

        if (record is { } mark)
        {
            watch.LayoutRecord = () => (mark, holder ?? string.Empty);
        }

        return watch;
    }

    private static RecordThreat? Lap(RecordWatch watch, string driver, double seconds) =>
        watch.Saw(new LapSeen(driver, seconds));

    private static List<RecordThreat> Run(RecordWatch watch, string driver, params double[] laps)
    {
        var said = new List<RecordThreat>();

        foreach (var lap in laps)
        {
            if (Lap(watch, driver, lap) is { } threat)
            {
                said.Add(threat);
            }
        }

        return said;
    }

    // ------------------------------------------------------------------
    //  What it says
    // ------------------------------------------------------------------

    [Fact]
    public void A_driver_closing_on_the_record_is_escalated_once_at_each_step()
    {
        // The three levels read as ONE story getting closer, each said
        // once. A watch that could repeat itself would be a meter
        // twitching rather than a broadcast.
        var said = Run(Watching(), "carl", 5.60, 5.45, 5.30, 5.22, 5.15, 5.10, 5.08, 5.05, 5.04, 5.02);

        Assert.Equal(
            new[] { RecordThreatLevel.Closing, RecordThreatLevel.OnTheDoorstep, RecordThreatLevel.OneMoreLikeThat },
            said.Select(one => one.Level).ToArray());
    }

    [Fact]
    public void A_watch_never_goes_backwards()
    {
        // A driver who comes within a hair, drops off, then comes back is
        // not announced twice.
        var watch = Watching();

        Run(watch, "carl", 5.10, 5.08, 5.06);

        var after = Run(watch, "carl", 5.90, 5.85, 5.12, 5.10, 5.09);

        Assert.DoesNotContain(RecordThreatLevel.OnTheDoorstep, after.Select(one => one.Level));
        Assert.DoesNotContain(RecordThreatLevel.Closing, after.Select(one => one.Level));
    }

    [Fact]
    public void The_arithmetic_it_reports_is_the_subtraction_and_nothing_more()
    {
        var said = Run(Watching(), "carl", 5.60, 5.45, 5.30, 5.22, 5.15);
        var threat = Assert.Single(said);

        Assert.Equal("carl", threat.DriverId);
        Assert.Equal(Record, threat.Record);
        Assert.Equal("Dave", threat.Holder);
        Assert.Equal(5.15, threat.BestThisRace, 3);
        Assert.Equal(5.15 - Record, threat.Needed, 3);
        Assert.True(threat.Improving);
    }

    [Fact]
    public void What_is_still_to_find_is_never_negative()
    {
        // A rounding that put this a hair under nought would produce "he
        // needs minus nothing".
        var watch = Watching();

        foreach (var threat in Run(watch, "carl", 5.10, 5.08, 5.06, 5.05, 5.04))
        {
            Assert.True(threat.Needed >= 0, $"needs {threat.Needed}");
        }
    }

    // ------------------------------------------------------------------
    //  The silence, which is most of it
    // ------------------------------------------------------------------

    [Fact]
    public void One_fluke_lap_says_nothing_at_all()
    {
        // THE WHOLE REASON THIS IS NOT THE EXISTING RecordPace CALL, which
        // fires off a single lap within two percent. One lap is a fluke, a
        // tow or a short-cut; what makes a record fall is pace.
        var said = Run(Watching(), "carl", 5.90, 5.85, 5.05, 5.88, 5.90);

        Assert.Empty(said);
    }

    [Fact]
    public void A_layout_with_no_record_standing_has_nothing_to_be_close_to()
    {
        var said = Run(Watching(record: null), "carl", 5.05, 5.04, 5.03, 5.02, 5.01);

        Assert.Empty(said);
    }

    [Fact]
    public void Nothing_is_said_before_a_driver_has_a_pace_to_report()
    {
        // A towed lap, a short-cut, or a decoder reading one car's
        // transponder as another produces one impossible time, and a watch
        // that fires off it has told the room the record is going when
        // nobody is near it.
        var watch = Watching();

        Assert.Null(Lap(watch, "carl", 5.05));
        Assert.Null(Lap(watch, "carl", 5.05));
    }

    [Fact]
    public void A_driver_in_the_right_area_but_not_improving_says_nothing()
    {
        // Five percent off a record is a lot of cars on a good night; what
        // makes it worth a word is a driver who is still finding time.
        var said = Run(Watching(), "carl", 5.25, 5.25, 5.25, 5.25, 5.25);

        Assert.Empty(said);
    }

    [Fact]
    public void Once_the_record_actually_goes_this_watch_stops_having_an_opinion()
    {
        // From that moment the story is PaceWatch's. Two classes calling
        // the same record is the database sound this layer exists to
        // avoid.
        var watch = Watching();

        Run(watch, "carl", 5.10, 5.08, 5.06);

        var after = Run(watch, "carl", 5.00, 4.98, 5.05, 5.05, 5.04, 5.03);

        Assert.Empty(after);
    }

    [Fact]
    public void A_lap_that_is_not_a_lap_is_ignored()
    {
        var watch = Watching();

        Assert.Null(Lap(watch, "carl", 0));
        Assert.Null(Lap(watch, "carl", -1));
        Assert.Null(Lap(watch, "carl", 9000));
        Assert.Null(Lap(watch, "   ", 5.05));

        Assert.Equal(0, watch.Seen("carl"));
    }

    [Fact]
    public void Each_driver_is_watched_on_their_own()
    {
        // One car's pace must not get another car announced, and one car
        // being level with the record must not silence the next.
        var watch = Watching();

        Run(watch, "carl", 5.60, 5.45, 5.30, 5.22, 5.15);

        var dave = Run(watch, "dave", 5.60, 5.45, 5.30, 5.22, 5.15);

        Assert.Single(dave);
        Assert.Equal("dave", dave[0].DriverId);
    }

    [Fact]
    public void A_new_race_clears_the_session_but_not_the_layout_record()
    {
        var watch = Watching();

        Run(watch, "carl", 5.60, 5.45, 5.30, 5.22, 5.15);

        watch.Reset();

        Assert.Equal(0, watch.Seen("carl"));

        // Said again, because this is a different race - and the record is
        // still there to be measured against, since it belongs to the store
        // behind the callback.
        var again = Run(watch, "carl", 5.60, 5.45, 5.30, 5.22, 5.15);

        Assert.Single(again);
    }

    // ------------------------------------------------------------------
    //  The wording
    // ------------------------------------------------------------------

    [Fact]
    public void Every_line_is_a_subtraction_and_not_a_forecast()
    {
        // THE RULE THIS CLASS SHARES WITH NightPromises. There is no
        // sentence here that is false if the driver's next lap is a spin,
        // so none of them may claim what is going to happen.
        var watch = Watching();
        var said = Run(watch, "carl", 5.60, 5.45, 5.30, 5.22, 5.15, 5.10, 5.08, 5.05, 5.04);

        Assert.NotEmpty(said);

        foreach (var threat in said)
        {
            var line = RecordWatch.Say(threat, "Carl");

            Assert.False(string.IsNullOrWhiteSpace(line));
            Assert.Contains("Carl", line);

            foreach (var forecast in new[] { "will ", "going to ", "is about to beat", "catch him" })
            {
                Assert.DoesNotContain(forecast, line, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void A_line_names_who_holds_the_mark_when_it_is_known()
    {
        var said = Run(Watching(), "carl", 5.60, 5.45, 5.30, 5.22, 5.15);
        var line = RecordWatch.Say(said[0], "Carl");

        Assert.Contains("Dave", line);
    }

    [Fact]
    public void A_record_nobody_is_recorded_as_holding_still_reads_as_a_sentence()
    {
        var said = Run(Watching(holder: string.Empty), "carl", 5.60, 5.45, 5.30, 5.22, 5.15);
        var line = RecordWatch.Say(said[0], "Carl");

        Assert.False(string.IsNullOrWhiteSpace(line));
        Assert.DoesNotContain("'s record", line);
    }

    [Fact]
    public void A_line_reads_the_times_the_way_a_commentator_would()
    {
        // Through RaceNumbers, so the record watch cannot be the one place
        // in the app that still says "5 point zero four seconds".
        var said = Run(Watching(), "carl", 5.60, 5.45, 5.30, 5.22, 5.15);
        var line = RecordWatch.Say(said[0], "Carl");

        Assert.Contains("five oh four", line);
        Assert.DoesNotContain("point", line);
    }
}
