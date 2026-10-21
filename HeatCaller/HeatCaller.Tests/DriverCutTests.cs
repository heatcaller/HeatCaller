namespace HeatCaller.Tests;

/// <summary>
/// ONE DRIVER'S NIGHT, AS A PIECE FOR THEM (12 Oct 2026) - see
/// <see cref="DriverCut"/>.
///
/// WHAT THIS FILE IS GUARDING
///
/// This is the one thing the app makes that is handed to a single person
/// who can check every word of it against what they remember. So the tests
/// are mostly about not saying something that driver knows is false, and
/// about the piece still working for the drivers who did not win - a piece
/// that only reads well for a winner is a piece most of a club never gets.
///
/// The facts are the testable half. What the model does with them is not,
/// and the instruction that steers it is pinned by
/// <see cref="The_instruction_tells_the_model_where_the_facts_end"/> only in
/// the sense that matters: that it says the facts are all of it.
/// </summary>
public sealed class DriverCutTests : IDisposable
{
    private readonly TempAppData _appData = new();

    public void Dispose() => _appData.Dispose();

    private static readonly DateTime Green = new(2026, 10, 12, 19, 0, 0, DateTimeKind.Utc);

    private static SessionResultEntry Row(
        string name,
        int position,
        long[] laps,
        int startPosition = 0,
        int lapsLed = 0,
        long? elapsedMs = null) =>
        new()
        {
            Transponder = name.ToLowerInvariant(),
            DriverName = name,
            Position = position,
            Laps = laps.Length,
            LapTimesMs = laps.ToList(),
            StartPosition = startPosition,
            LapsLed = lapsLed,
            ElapsedMs = elapsedMs ?? laps.Sum(),
        };

    private static SessionRecord Race(string id, string label, int minutesIn, params SessionResultEntry[] rows) =>
        new()
        {
            Id = id,
            SessionType = RaceSessionType.Race,
            Label = label,
            TrackLayoutName = "Winter Indoor",
            StartedAtUtc = Green.AddMinutes(minutesIn),
            EndedAtUtc = Green.AddMinutes(minutesIn + 8),
            Results = rows.ToList(),
        };

    /// <summary>A heat Bill wins from the back, and a main he loses on the last lap.</summary>
    private static List<SessionRecord> ANight() => new()
    {
        Race(
            "heat-1",
            "Heat 1",
            0,
            Row("Bill", 1, new long[] { 10000, 9000, 9000 }, startPosition: 5, lapsLed: 3),
            Row("Sam", 2, new long[] { 10200, 9100, 9100 }, startPosition: 1),
            // WHY (20 Oct 2026): this heat used to be Bill and Sam alone with
            // Bill on grid slot 5 - a slot that cannot exist in a two-car
            // race, which is exactly the stale grid DriverCut now refuses to
            // build a charge on. Three slower cars on slots 2 to 4 make
            // "fifth to first, four places" a real one. Not "Zed", whom
            // A_driver_who_raced_nowhere_tonight_has_no_piece needs absent.
            Row("Carl", 3, new long[] { 10400, 9300, 9300 }, startPosition: 2),
            Row("Eve", 4, new long[] { 10500, 9400, 9400 }, startPosition: 3),
            Row("Hal", 5, new long[] { 10600, 9500, 9500 }, startPosition: 4)),
        Race(
            "a-main",
            "A-Main",
            60,
            Row("Sam", 1, new long[] { 10500, 9000, 9000, 9000, 8500 }, startPosition: 2),
            Row("Bill", 2, new long[] { 10000, 9000, 9000, 9000, 9500 }, startPosition: 1, lapsLed: 4)),
    };

    private static PackedFacts For(string who, List<SessionRecord>? night = null) =>
        DriverCut.FactsFor(who.ToLowerInvariant(), who, night ?? ANight());

    // ------------------------------------------------------------------
    //  Their night, and nobody else's
    // ------------------------------------------------------------------

    [Fact]
    public void Each_race_they_ran_is_in_it_in_the_order_they_ran()
    {
        var facts = For("Bill");

        Assert.False(facts.Empty);

        var heat = facts.Text.IndexOf("Heat 1", StringComparison.Ordinal);
        var main = facts.Text.IndexOf("A-Main", StringComparison.Ordinal);

        Assert.True(heat >= 0, "The heat is not in it.");
        Assert.True(main > heat, "The main comes before the heat, or is missing.");
    }

    [Fact]
    public void How_many_they_beat_is_in_it_and_not_only_where_they_came()
    {
        // "Sixth of nineteen" is a different night from "sixth of six", and
        // a piece that only ever says the position reads the same for both.
        var facts = For("Bill");

        Assert.Contains("of 2", facts.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_driver_who_was_not_in_a_race_has_nothing_said_about_it()
    {
        var night = ANight();

        night.Add(Race(
            "b-main",
            "B-Main",
            30,
            Row("Dot", 1, new long[] { 11000, 10000, 10000 }, startPosition: 1, lapsLed: 3)));

        var facts = For("Bill", night);

        Assert.DoesNotContain("B-Main", facts.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_driver_who_raced_nowhere_tonight_has_no_piece()
    {
        Assert.True(For("Zed").Empty);
    }

    [Fact]
    public void Nothing_filed_at_all_is_no_piece_rather_than_an_error()
    {
        Assert.True(DriverCut.FactsFor("bill", "Bill", null).Empty);
        Assert.True(DriverCut.FactsFor("bill", "Bill", new List<SessionRecord>()).Empty);
        Assert.True(DriverCut.FactsFor(null, "Bill", ANight()).Empty);
        Assert.True(DriverCut.FactsFor("bill", null, ANight()).Empty);
        Assert.True(DriverCut.FactsFor("bill", "   ", ANight()).Empty);
    }

    [Fact]
    public void A_practice_run_and_a_demo_are_not_part_of_their_night()
    {
        var night = ANight();

        var practice = Race("p-1", "Practice", 15, Row("Bill", 1, new long[] { 9000, 8000, 8000 }));
        practice.SessionType = RaceSessionType.Practice;

        var demo = Race("d-1", "[DEMO] A-Main", 20, Row("Bill", 1, new long[] { 5000, 4000, 4000 }));

        night.Add(practice);
        night.Add(demo);

        var facts = For("Bill", night);

        Assert.DoesNotContain("Practice", facts.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("DEMO", facts.Text, StringComparison.OrdinalIgnoreCase);

        // And the invented 4.0s laps are nowhere near their quickest of the
        // night, which is the whole reason a demo is kept out of anything.
        Assert.DoesNotContain("4.000", facts.Text, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    //  What happened to them
    // ------------------------------------------------------------------

    [Fact]
    public void The_nights_moments_are_what_make_it_more_than_three_positions()
    {
        // Without these a driver's piece is finishing positions and a lap
        // time. This is what NightMoments was built for.
        var facts = For("Bill");

        // Up from fifth in the heat - four places, which is a charge.
        Assert.Contains("made up 4 places", facts.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_moment_that_was_somebody_elses_is_not_claimed_for_them()
    {
        // Sam took the lead on the last lap of the main. It is the best
        // moment of the night and it is not Bill's.
        var bill = For("Bill");
        var sam = For("Sam");

        Assert.DoesNotContain("took the lead on the last lap", bill.Text, StringComparison.Ordinal);
        Assert.Contains("took the lead on the last lap", sam.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Their_quickest_lap_of_the_night_is_the_quickest_of_their_own()
    {
        // Bill's laps are 9.0s in the heat and 9.0s/9.5s in the main; Sam's
        // 8.5s in the main is quicker than anything Bill did and is not his.
        var facts = For("Bill");

        Assert.Contains("9.000", facts.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("8.500", facts.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dash_off_the_grid_is_never_their_best_lap()
    {
        var facts = DriverCut.FactsFor(
            "bill",
            "Bill",
            new List<SessionRecord>
            {
                Race("heat-1", "Heat 1", 0, Row("Bill", 1, new long[] { 1400, 9000, 9200 }, startPosition: 1, lapsLed: 3)),
            });

        Assert.Contains("9.000", facts.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("1.400", facts.Text, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    //  It has to work for the drivers who did not win
    // ------------------------------------------------------------------

    [Fact]
    public void A_driver_who_won_nothing_still_gets_a_piece_with_their_night_in_it()
    {
        var facts = DriverCut.FactsFor(
            "dot",
            "Dot",
            new List<SessionRecord>
            {
                Race(
                    "heat-1",
                    "Heat 1",
                    0,
                    Row("Bill", 1, new long[] { 10000, 9000, 9000 }, startPosition: 1, lapsLed: 3),
                    Row("Sam", 2, new long[] { 10100, 9200, 9200 }, startPosition: 2),
                    Row("Carl", 3, new long[] { 10200, 9400, 9400 }, startPosition: 3),
                    Row("Eve", 4, new long[] { 10300, 9600, 9600 }, startPosition: 4),
                    Row("Zed", 5, new long[] { 10400, 9800, 9800 }, startPosition: 5),
                    Row("Dot", 6, new long[] { 11000, 10500, 10400 }, startPosition: 6)),
            });

        Assert.False(facts.Empty);

        // Sixth of six, which is the point: a position on its own reads the
        // same in a field of six and a field of twenty.
        Assert.Contains("sixth of 6", facts.Text, StringComparison.Ordinal);
        Assert.Contains("10.400", facts.Text, StringComparison.Ordinal);
        Assert.Contains("their best finish", facts.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_night_of_wins_says_so_without_counting_them_wrongly()
    {
        var facts = DriverCut.FactsFor(
            "bill",
            "Bill",
            new List<SessionRecord>
            {
                Race("heat-1", "Heat 1", 0, Row("Bill", 1, new long[] { 10000, 9000 }, startPosition: 1, lapsLed: 2)),
                Race("a-main", "A-Main", 60, Row("Bill", 1, new long[] { 10000, 9000 }, startPosition: 1, lapsLed: 2)),
            });

        Assert.Contains("raced 2 times", facts.Text, StringComparison.Ordinal);
        Assert.Contains("winning every one of them", facts.Text, StringComparison.Ordinal);

        // Never "1 of thems", which is what running the count through
        // Plural would have made of it.
        Assert.DoesNotContain("thems", facts.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void One_race_and_won_it_reads_as_one_race()
    {
        var facts = DriverCut.FactsFor(
            "bill",
            "Bill",
            new List<SessionRecord>
            {
                Race("heat-1", "Heat 1", 0, Row("Bill", 1, new long[] { 10000, 9000 }, startPosition: 1, lapsLed: 2)),
            });

        Assert.Contains("raced 1 time", facts.Text, StringComparison.Ordinal);
        Assert.Contains("and won it", facts.Text, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    //  The shape of the piece
    // ------------------------------------------------------------------

    [Fact]
    public void It_is_shorter_than_the_nights_recap_because_it_has_less_to_get_through()
    {
        Assert.True(
            DriverCut.Words < NightRecap.Words,
            $"{DriverCut.Words} words is not shorter than the night's {NightRecap.Words}.");
    }

    [Fact]
    public void The_word_allowance_is_the_difference_over_the_clubs_own_limit()
    {
        // CommentaryService.BuildSystemPrompt ADDS its argument to the
        // club's per-line limit, so handing it the whole allowance gives a
        // club with a 120-word limit a 270-word piece - the trap the
        // night's recap caught on its way out.
        Assert.Equal(DriverCut.Words - 40, DriverCut.ExtraWordsOver(40));
        Assert.Equal(0, DriverCut.ExtraWordsOver(DriverCut.Words));
        Assert.Equal(0, DriverCut.ExtraWordsOver(DriverCut.Words + 100));
        Assert.Equal(DriverCut.Words, DriverCut.ExtraWordsOver(0));
        Assert.Equal(DriverCut.Words, DriverCut.ExtraWordsOver(-50));
    }

    [Fact]
    public void The_character_cap_is_not_the_one_sized_for_a_sponsors_read()
    {
        // Reaching for SponsorCopy.Tidy here is the exact mistake the
        // night's recap caught before it shipped: six hundred characters is
        // a sponsor's read and would throw away most of every piece in
        // silence.
        Assert.True(
            DriverCut.MostChars > SponsorCopy.MaxChars,
            $"{DriverCut.MostChars} is not above a sponsor read's {SponsorCopy.MaxChars}.");
    }

    [Fact]
    public void It_is_split_by_the_one_splitter_rather_than_a_second_one()
    {
        // Two implementations of "where may a voice take a breath" is the
        // sort of thing that agrees for a month and then disagrees about one
        // sentence with a decimal point in it.
        var script = "Bill raced Heat 1 and finished first of 2. Their best lap was 9.000s, on lap 2. "
                     + "Bill made up 4 places from the grid.";

        Assert.Equal(NightRecap.Chunks(script), DriverCut.Chunks(script));
    }

    [Fact]
    public void The_file_is_named_after_the_driver()
    {
        Assert.Equal("Bill Hargrove - their night", DriverCut.LabelFor("Bill Hargrove"));
        Assert.Equal("Bill Hargrove - their night", DriverCut.LabelFor("  Bill Hargrove  "));

        // Never a file called " - their night".
        Assert.Equal("A driver - their night", DriverCut.LabelFor(null));
        Assert.Equal("A driver - their night", DriverCut.LabelFor("   "));
    }

    [Fact]
    public void The_instruction_tells_the_model_where_the_facts_end()
    {
        // The one thing every prompt in this layer says, and the one that
        // matters most here: this is handed to the person who can check it.
        Assert.Contains("must come from the facts above", DriverCut.Instruction, StringComparison.Ordinal);
        Assert.Contains("shorter piece rather than filling it out", DriverCut.Instruction, StringComparison.Ordinal);

        // And the two things that make it not the night's recap.
        Assert.Contains("ONE DRIVER", DriverCut.Instruction, StringComparison.Ordinal);
        Assert.Contains("rather than about where they finished", DriverCut.Instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_fact_in_it_is_themed_to_that_driver()
    {
        // So that a piece asked for twice in a night cannot be cooled by the
        // variety rule into saying less the second time - and so the themes
        // mean what they mean everywhere else.
        var facts = For("Bill");

        Assert.False(facts.Empty);
        Assert.Contains(FactTheme.Driver("bill"), facts.Themes);
    }
}
