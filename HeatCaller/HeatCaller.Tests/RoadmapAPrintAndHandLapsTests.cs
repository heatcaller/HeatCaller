namespace HeatCaller.Tests;

/// <summary>
/// The roadmap round of 22 Oct 2026, group A: printing with a preview
/// (PagePrinting, ResultsSheetRenderer) and laps counted by hand being said
/// wherever a result is shown (HandCountedLaps). No printer here, so what is
/// tested is the arithmetic the printing rests on.
/// </summary>
public sealed class RoadmapAPrintAndHandLapsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- PagePrinting.FitInto ---

    [Fact]
    public void A_letter_page_fits_inside_the_margins_at_its_own_shape_and_centred()
    {
        // Letter at 150 DPI into A4's margin box (hundredths of an inch).
        var where = PagePrinting.FitInto(new Size(1275, 1650), new Rectangle(100, 100, 627, 969));

        Assert.True(where.Width <= 627 && where.Height <= 969);
        Assert.Equal(627, where.Width);
        Assert.InRange(where.Height / (double)where.Width, 1650 / 1275.0 - 0.01, 1650 / 1275.0 + 0.01);
        Assert.Equal(100, where.Left);

        // The spare height split above and below.
        var above = where.Top - 100;
        var below = 100 + 969 - where.Bottom;
        Assert.InRange(Math.Abs(above - below), 0, 1);
    }

    [Fact]
    public void A_wide_page_is_limited_by_the_width_and_never_stretched()
    {
        var where = PagePrinting.FitInto(new Size(2000, 1000), new Rectangle(0, 0, 800, 800));

        Assert.Equal(800, where.Width);
        Assert.Equal(400, where.Height);
        Assert.Equal(200, where.Top);
    }

    [Fact]
    public void Nothing_to_fit_or_nowhere_to_put_it_is_empty()
    {
        Assert.True(PagePrinting.FitInto(Size.Empty, new Rectangle(0, 0, 100, 100)).IsEmpty);
        Assert.True(PagePrinting.FitInto(new Size(10, 10), Rectangle.Empty).IsEmpty);
    }

    // --- ResultsSheetRenderer.PlanPages ---

    private static IEnumerable<(int Section, int Row)> RowsPlaced(IReadOnlyList<IReadOnlyList<ResultsSheetRenderer.Part>> plan) =>
        plan.SelectMany(page => page).SelectMany(part => Enumerable.Range(part.FirstRow, part.RowCount).Select(row => (part.Section, row)));

    [Fact]
    public void Every_row_of_every_race_is_on_exactly_one_page_in_order()
    {
        var rows = new[] { 8, 12, 3, 40, 0, 10, 150 };
        var plan = ResultsSheetRenderer.PlanPages(rows, rows.Select(_ => false).ToList());

        var placed = RowsPlaced(plan).ToList();
        var expected = rows.SelectMany((count, section) => Enumerable.Range(0, count).Select(row => (section, row))).ToList();

        Assert.Equal(expected, placed);

        // The race with no results still gets its block.
        Assert.Contains(plan.SelectMany(page => page), part => part.Section == 4 && part.RowCount == 0 && part.LastOfSection);
    }

    [Fact]
    public void A_race_that_fits_on_a_fresh_page_is_moved_whole_rather_than_split()
    {
        var plan = ResultsSheetRenderer.PlanPages(new[] { 8, 8, 8, 8, 8 }, new[] { false, false, false, false, false });

        Assert.All(plan.SelectMany(page => page), part => Assert.False(part.Continued));
        Assert.All(plan.SelectMany(page => page), part => Assert.True(part.LastOfSection));
        Assert.True(plan.Count >= 2);
    }

    [Fact]
    public void A_race_too_long_for_any_page_runs_on_with_its_heading_marked_continued()
    {
        var plan = ResultsSheetRenderer.PlanPages(new[] { 120 }, new[] { true });
        var parts = plan.SelectMany(page => page).ToList();

        Assert.True(parts.Count >= 2);
        Assert.False(parts[0].Continued);
        Assert.All(parts.Skip(1), part => Assert.True(part.Continued));
        Assert.True(parts[^1].LastOfSection);
        Assert.All(parts.Take(parts.Count - 1), part => Assert.False(part.LastOfSection));
    }

    [Fact]
    public void Nothing_selected_still_plans_one_page()
    {
        Assert.Single(ResultsSheetRenderer.PlanPages(Array.Empty<int>(), Array.Empty<bool>()));
    }

    [Fact]
    public void A_total_time_reads_as_minutes_seconds_and_thousandths()
    {
        Assert.Equal("5:03.456", ResultsSheetRenderer.TotalTime(303_456));
        Assert.Equal("-", ResultsSheetRenderer.TotalTime(0));
    }

    // --- HandCountedLaps ---

    [Fact]
    public void Laps_with_no_crossing_behind_them_are_the_ones_counted_by_hand()
    {
        var standing = new RacerStanding { TransponderId = "A", Laps = 5 };
        standing.LapTimesMs.AddRange(new long[] { 900, 10_000, 10_100 });

        Assert.Equal(2, HandCountedLaps.During(standing));
        Assert.Equal("5*", HandCountedLaps.LapsText(5, 2));
        Assert.Equal("5", HandCountedLaps.LapsText(5, 0));
        Assert.Equal("2 laps counted by hand", HandCountedLaps.Note(2));
        Assert.Equal("1 lap counted by hand", HandCountedLaps.Note(1));
        Assert.Equal(string.Empty, HandCountedLaps.Note(0));
    }

    [Fact]
    public void A_saved_result_counts_its_race_laps_by_hand_and_any_edit_result_added()
    {
        Assert.Equal(3, HandCountedLaps.In(new SessionResultEntry { Laps = 10, LapsByHand = 2, AddedLaps = 1 }));

        // A lap taken off afterwards comes off the hand-counted ones first,
        // and never below none.
        Assert.Equal(1, HandCountedLaps.In(new SessionResultEntry { Laps = 10, LapsByHand = 2, AddedLaps = -1 }));
        Assert.Equal(0, HandCountedLaps.In(new SessionResultEntry { Laps = 10, LapsByHand = 1, AddedLaps = -3 }));

        // Never more than the laps left: a DQ (no laps) cannot read "0*".
        Assert.Equal(0, HandCountedLaps.In(new SessionResultEntry { Laps = 0, LapsByHand = 2, Outcome = "DQ" }));
        Assert.Equal(1, HandCountedLaps.In(new SessionResultEntry { Laps = 1, LapsByHand = 3 }));
        Assert.Equal(0, HandCountedLaps.In(new SessionResultEntry()));
    }

    [Fact]
    public void An_old_result_with_no_hand_count_reads_back_as_none()
    {
        var entry = System.Text.Json.JsonSerializer.Deserialize<SessionResultEntry>("{\"Position\":1,\"Laps\":12}")!;

        Assert.Equal(0, entry.LapsByHand);
        Assert.Equal("12", HandCountedLaps.LapsText(entry.Laps, HandCountedLaps.In(entry)));
    }

    private static void Seen(RaceControlSession race, string car, uint deviceTimeMs) =>
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = car,
            DeviceTimeMs = deviceTimeMs,
            SignalHits = 60,
            SignalStrength = 247,
        });

    private static RacerStanding StandingOf(RaceControlSession race, string car) =>
        race.GetOrderedStandings().Single(s => string.Equals(s.TransponderId, car, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Plus_one_lap_in_a_race_is_counted_by_hand_and_taking_one_off_takes_that_first()
    {
        var race = new RaceControlSession { LapLimit = 20 };
        race.Stage();
        race.StartCountdown();
        race.GreenFlag(100_000);

        Seen(race, "A", 100_500);
        Seen(race, "A", 110_000);

        Assert.Equal(0, HandCountedLaps.During(StandingOf(race, "A")));

        // The transponder stops reading; race control keeps the count.
        Assert.True(race.AdjustLaps("A", +1));
        Assert.True(race.AdjustLaps("A", +1));
        Assert.Equal(2, HandCountedLaps.During(StandingOf(race, "A")));

        // It reads again: a real lap does not change the hand count.
        Seen(race, "A", 140_000);
        Assert.Equal(2, HandCountedLaps.During(StandingOf(race, "A")));

        // One too many added: the hand lap goes, not a timed one.
        var timed = StandingOf(race, "A").LapTimesMs.Count;
        Assert.True(race.AdjustLaps("A", -1));
        Assert.Equal(1, HandCountedLaps.During(StandingOf(race, "A")));
        Assert.Equal(timed, StandingOf(race, "A").LapTimesMs.Count);

        // And it is what the filed result carries.
        var rows = RaceFiling.ResultsFrom(race.GetOrderedStandings(), ProfileStore.Load());
        Assert.Equal(1, rows.Single(r => r.Transponder == "A").LapsByHand);
    }
}
