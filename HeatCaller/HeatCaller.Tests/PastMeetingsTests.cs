namespace HeatCaller.Tests;

/// <summary>
/// THE LONG CALLBACK (13 Oct 2026) - see <see cref="PastMeetings"/>.
///
/// WHAT THIS FILE IS ABOUT, AND IT IS NOT THE ARITHMETIC
///
/// Everything the announcer knew about a rivalry was a COUNT: seven-one in
/// eight meetings. True, and unknowable to anybody in the room - but
/// arithmetic does not sound like memory. "The last time these two shared
/// a front row, Dave took the lead on the last lap in the A-Main" does.
///
/// So most of what can go wrong here is saying something the saved result
/// does not prove, and most of this file is the refusals. Three worth
/// reading first:
///
/// <see cref="Nothing_is_recalled_from_a_race_with_no_grid_recorded"/> - a
/// race saved before the grid was kept has StartPosition zero for
/// everybody, and zero there means UNKNOWN, not "started at the back". A
/// front-row claim built on it would be invented.
///
/// <see cref="A_moment_belonging_to_a_third_driver_is_not_the_callback"/> -
/// the best moment in a race often belongs to somebody who is not on
/// tonight's grid, and a callback about a rivalry that names a third
/// person has lost its subject.
///
/// <see cref="The_race_is_named_once_and_only_once"/> - the clause and the
/// moment can each name the race, and the room must not be told it twice
/// in one breath.
/// </summary>
public sealed class PastMeetingsTests : IDisposable
{
    private readonly TempAppData _appData = new();

    public void Dispose() => _appData.Dispose();

    private static readonly DateTime Green = new(2026, 9, 19, 19, 0, 0, DateTimeKind.Utc);

    /// <summary>Three weeks after that race - a believable "tonight".</summary>
    private static readonly DateTime Tonight = new(2026, 10, 13, 19, 0, 0, DateTimeKind.Utc);

    private static SessionResultEntry Row(
        string name,
        int position,
        long[] laps,
        int startPosition = 0,
        long? elapsedMs = null) =>
        new()
        {
            Transponder = name.ToLowerInvariant(),
            DriverName = name,
            Position = position,
            Laps = laps.Length,
            LapTimesMs = laps.ToList(),
            StartPosition = startPosition,
            ElapsedMs = elapsedMs ?? laps.Sum(),
        };

    private static SessionRecord Race(
        bool recordsIncidents = true,
        string label = "A-Main",
        DateTime? endedUtc = null,
        params SessionResultEntry[] rows) =>
        new()
        {
            Id = "race-1",
            SessionType = RaceSessionType.Race,
            Label = label,
            TrackLayoutName = "Winter Indoor",
            StartedAtUtc = Green,
            EndedAtUtc = endedUtc ?? Green.AddMinutes(8),
            RecordsIncidents = recordsIncidents,
            Results = rows.ToList(),
        };

    /// <summary>
    /// Bill leads four laps; Sam takes it on the fifth. The same shape
    /// NightMomentsTests verified by hand, so the moment this produces is
    /// a known quantity rather than a guess.
    /// </summary>
    private static SessionRecord LastLapRace(int billStart = 0, int samStart = 0) =>
        Race(
            true,
            "A-Main",
            null,
            Row("Bill", 2, new long[] { 10_000, 9_000, 9_000, 9_000, 9_500 }, billStart),
            Row("Sam", 1, new long[] { 10_500, 9_000, 9_000, 9_000, 8_500 }, samStart));

    private static string? NameOf(string transponder) =>
        transponder switch { "bill" => "Bill", "sam" => "Sam", _ => null };

    // ------------------------------------------------------------------
    //  The callback itself
    // ------------------------------------------------------------------

    [Fact]
    public void A_meeting_is_recalled_with_how_long_ago_it_was()
    {
        var said = PastMeetings.Describe(LastLapRace(), "bill", "sam", NameOf, Tonight);

        Assert.NotNull(said);
        Assert.Contains("weeks ago", said!.Value.Clause);
    }

    [Fact]
    public void What_happened_is_said_in_the_words_the_drivers_cut_uses()
    {
        // Through DriverCut.Describe, not a second wording of its own -
        // two wordings for one moment are two that drift, which is why
        // Describe is internal rather than private.
        var said = PastMeetings.Describe(LastLapRace(), "bill", "sam", NameOf, Tonight);

        Assert.NotNull(said);
        Assert.NotNull(said!.Value.Moment);

        var everyWording = NightMoments.ForRace(LastLapRace())
            .Where(m => m.Transponder is "bill" or "sam")
            .Select(m => DriverCut.Describe(m, m.Transponder == "sam" ? "Sam" : "Bill"))
            .ToList();

        // Which of the race's moments gets picked is PastMeetings' own
        // business and may change; that the words came from DriverCut
        // rather than a second wording of its own is the thing worth
        // holding.
        Assert.Contains(said.Value.Moment!, everyWording);
    }

    [Fact]
    public void A_front_row_shared_is_noticed()
    {
        var said = PastMeetings.Describe(LastLapRace(billStart: 2, samStart: 1), "bill", "sam", NameOf, Tonight);

        Assert.NotNull(said);
        Assert.True(said!.Value.FrontRow);
    }

    [Fact]
    public void A_front_row_is_a_front_row_in_either_order()
    {
        Assert.True(PastMeetings.SharedAFrontRow(LastLapRace(billStart: 1, samStart: 2), "bill", "sam"));
        Assert.True(PastMeetings.SharedAFrontRow(LastLapRace(billStart: 2, samStart: 1), "bill", "sam"));
    }

    [Fact]
    public void Row_three_is_not_a_front_row()
    {
        Assert.False(PastMeetings.SharedAFrontRow(LastLapRace(billStart: 3, samStart: 4), "bill", "sam"));
        Assert.False(PastMeetings.SharedAFrontRow(LastLapRace(billStart: 1, samStart: 3), "bill", "sam"));
    }

    [Fact]
    public void Nothing_is_recalled_from_a_race_with_no_grid_recorded()
    {
        // THE REFUSAL THAT MATTERS MOST. A race saved before the grid was
        // kept has StartPosition zero for every driver, and zero there
        // means UNKNOWN. Reading it as "row one" would have the announcer
        // claim a front row that nobody sat on.
        var old = Race(
            false,
            "A-Main",
            null,
            Row("Bill", 2, new long[] { 10_000, 9_000 }, 1),
            Row("Sam", 1, new long[] { 10_000, 8_000 }, 2));

        Assert.False(PastMeetings.SharedAFrontRow(old, "bill", "sam"));
    }

    [Fact]
    public void A_moment_belonging_to_a_third_driver_is_not_the_callback()
    {
        // Dave wins by a mile from the back of the grid - the strongest
        // moment in the race - and is not on tonight's grid. A callback
        // about Bill and Sam that named Dave would have lost its subject.
        var race = Race(
            true,
            "A-Main",
            null,
            Row("Dave", 1, new long[] { 9_000, 8_000, 8_000, 8_000, 8_000 }, 8),
            Row("Bill", 2, new long[] { 10_000, 9_000, 9_000, 9_000, 9_000 }, 1),
            Row("Sam", 3, new long[] { 10_500, 9_500, 9_500, 9_500, 9_500 }, 2));

        var said = PastMeetings.Describe(race, "bill", "sam", NameOf, Tonight);

        Assert.NotNull(said);
        Assert.DoesNotContain("Dave", said!.Value.Moment ?? string.Empty);
    }

    [Fact]
    public void A_race_that_proves_nothing_still_gets_its_clause()
    {
        // Two drivers, a comfortable win, nothing the saved result can
        // call a moment. "They last met in the A-Main three weeks ago" is
        // worth saying on its own.
        var dull = Race(
            true,
            "Heat 2",
            null,
            Row("Bill", 1, new long[] { 10_000, 10_000, 10_000 }, 1),
            Row("Sam", 2, new long[] { 12_000, 12_000, 12_000 }, 2));

        var said = PastMeetings.Describe(dull, "bill", "sam", NameOf, Tonight);

        Assert.NotNull(said);
        Assert.False(string.IsNullOrWhiteSpace(said!.Value.Clause));
    }

    // ------------------------------------------------------------------
    //  The race is named once
    // ------------------------------------------------------------------

    [Fact]
    public void The_race_is_named_once_and_only_once()
    {
        // DriverCut.Describe ends "... in the A-Main", so a clause that
        // also named it would have the room told the label twice in one
        // breath.
        var said = PastMeetings.Describe(LastLapRace(), "bill", "sam", NameOf, Tonight);

        Assert.NotNull(said);
        Assert.NotNull(said!.Value.Moment);

        var times = (said.Value.Clause + " " + said.Value.Moment).Split("A-Main").Length - 1;

        Assert.Equal(1, times);
    }

    [Fact]
    public void A_race_that_proves_nothing_names_itself_in_the_clause()
    {
        var dull = Race(
            true,
            "Heat 2",
            null,
            Row("Bill", 1, new long[] { 10_000, 10_000, 10_000 }, 1),
            Row("Sam", 2, new long[] { 12_000, 12_000, 12_000 }, 2));

        var said = PastMeetings.Describe(dull, "bill", "sam", NameOf, Tonight);

        Assert.NotNull(said);
        Assert.Null(said!.Value.Moment);
        Assert.Contains("Heat 2", said.Value.Clause);
    }

    [Fact]
    public void A_fastest_lap_on_its_own_is_not_a_memory()
    {
        // THE BUG THIS FILE CAUGHT, and it was in the code rather than
        // here. Every race has a fastest lap, so NightMoments always
        // produces one - and the callback reached for it, coming out as
        // "they last met three weeks ago, and Bill set the fastest lap in
        // Heat 2". True, and available about every race ever run, which is
        // the repetition the whole depth plan was written against.
        //
        // NightMoments.OfTheNight had already made this judgement in those
        // words; PastMeetings now follows it.
        var dull = Race(
            true,
            "Heat 2",
            null,
            Row("Bill", 1, new long[] { 10_000, 10_000, 10_000 }, 1),
            Row("Sam", 2, new long[] { 12_000, 12_000, 12_000 }, 2));

        // There IS a fastest lap in that race - this is not a race with no
        // moments at all, which is what makes the test worth having.
        Assert.Contains(NightMoments.Over(new[] { dull }), m => m.What == NightMoments.Kind.FastestLap);

        Assert.Null(PastMeetings.WhatHappened(dull, "bill", "sam", NameOf));
    }

    [Fact]
    public void The_strongest_moment_is_the_one_recalled()
    {
        // Over ranks, ForRace does not. Taking whichever came out of the
        // walk first would have let a mid-race lead change beat a photo
        // finish by accident of discovery order.
        var said = PastMeetings.WhatHappened(LastLapRace(), "bill", "sam", NameOf);

        Assert.NotNull(said);

        var strongest = NightMoments.Over(new[] { LastLapRace() })
            .First(m => m.What != NightMoments.Kind.FastestLap);

        Assert.Equal(DriverCut.Describe(strongest, NameOf(strongest.Transponder)!), said);
    }

    // ------------------------------------------------------------------
    //  How long ago, as a club would say it
    // ------------------------------------------------------------------

    [Fact]
    public void How_long_ago_is_said_the_way_a_club_says_it()
    {
        var now = Tonight;

        Assert.Equal("earlier today", PastMeetings.HowLongAgo(now.AddHours(-3), now));
        Assert.Equal("yesterday", PastMeetings.HowLongAgo(now.AddDays(-1), now));
        Assert.Equal("a few days ago", PastMeetings.HowLongAgo(now.AddDays(-3), now));
        Assert.Equal("last week", PastMeetings.HowLongAgo(now.AddDays(-9), now));
        Assert.Equal("2 weeks ago", PastMeetings.HowLongAgo(now.AddDays(-15), now));
        Assert.Equal("last season", PastMeetings.HowLongAgo(now.AddDays(-400), now));
    }

    [Fact]
    public void A_single_week_and_a_single_month_are_not_plural()
    {
        var now = Tonight;

        Assert.DoesNotContain("1 weeks", PastMeetings.HowLongAgo(now.AddDays(-8), now));
        Assert.DoesNotContain("1 months", PastMeetings.HowLongAgo(now.AddDays(-40), now));
    }

    [Fact]
    public void A_clock_put_back_does_not_produce_a_race_in_the_future()
    {
        // Somebody's PC clock, or a daylight-saving hour. There is no
        // honest wording for a race that has not happened yet, and
        // "earlier today" is the one reading that cannot be far wrong.
        Assert.Equal("earlier today", PastMeetings.HowLongAgo(Tonight.AddHours(2), Tonight));
    }

    // ------------------------------------------------------------------
    //  Nothing to recall
    // ------------------------------------------------------------------

    [Fact]
    public void A_pair_who_have_never_met_recall_nothing()
    {
        Assert.Null(PastMeetings.Describe(null, "bill", "sam", NameOf, Tonight));
    }

    [Fact]
    public void A_meeting_too_long_ago_is_not_worth_recalling()
    {
        // Past this it stops being memory and starts being trivia - and
        // about two drivers who have raced weekly since, it is a claim the
        // room knows is wrong.
        var ancient = Race(
            true,
            "A-Main",
            Tonight - PastMeetings.StillWorthRecalling - TimeSpan.FromDays(1),
            Row("Bill", 2, new long[] { 10_000, 9_000 }, 1),
            Row("Sam", 1, new long[] { 10_000, 8_000 }, 2));

        Assert.Null(PastMeetings.Describe(ancient, "bill", "sam", NameOf, Tonight));
    }

    [Fact]
    public void A_meeting_right_on_the_edge_is_still_recalled()
    {
        var edge = Race(
            true,
            "A-Main",
            Tonight - PastMeetings.StillWorthRecalling,
            Row("Bill", 2, new long[] { 10_000, 9_000 }, 1),
            Row("Sam", 1, new long[] { 10_000, 8_000 }, 2));

        Assert.NotNull(PastMeetings.Describe(edge, "bill", "sam", NameOf, Tonight));
    }

    [Fact]
    public void A_race_with_no_end_time_cannot_be_placed_in_the_past()
    {
        // "They last met" with no when is the half of the sentence that
        // was worth having.
        var undated = Race(
            true,
            "A-Main",
            default(DateTime),
            Row("Bill", 2, new long[] { 10_000, 9_000 }, 1),
            Row("Sam", 1, new long[] { 10_000, 8_000 }, 2));

        Assert.Null(PastMeetings.Describe(undated, "bill", "sam", NameOf, Tonight));
    }

    [Fact]
    public void A_pair_with_a_missing_id_recalls_nothing()
    {
        foreach (var missing in new string?[] { null, string.Empty, "   " })
        {
            Assert.Null(PastMeetings.Describe(LastLapRace(), missing, "sam", NameOf, Tonight));
            Assert.Null(PastMeetings.Describe(LastLapRace(), "bill", missing, NameOf, Tonight));
        }
    }

    [Fact]
    public void A_driver_renamed_since_is_still_the_person_in_the_room()
    {
        // The club's current name wins over the one the race was saved
        // with. Null from the lookup falls back to the saved name rather
        // than losing the callback.
        var said = PastMeetings.Describe(LastLapRace(), "bill", "sam", _ => "Samuel Teague", Tonight);

        Assert.NotNull(said);
        Assert.Contains("Samuel Teague", said!.Value.Moment ?? string.Empty);

        var fallback = PastMeetings.Describe(LastLapRace(), "bill", "sam", _ => null, Tonight);

        Assert.NotNull(fallback);
        Assert.Contains("Sam", fallback!.Value.Moment ?? string.Empty);
    }

    [Fact]
    public void No_name_lookup_at_all_still_recalls_the_meeting()
    {
        var said = PastMeetings.Describe(LastLapRace(), "bill", "sam", null, Tonight);

        Assert.NotNull(said);
        Assert.False(string.IsNullOrWhiteSpace(said!.Value.Clause));
    }
}
