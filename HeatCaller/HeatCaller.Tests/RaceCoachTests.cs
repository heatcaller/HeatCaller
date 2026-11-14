namespace HeatCaller.Tests;

/// <summary>
/// The race engineer's shared rules (13 Nov 2026, the Race Analytics rework):
/// car lengths from a layout's length, mistake laps, and the headline and
/// "Try this" picked from a race's laps.
/// </summary>
public sealed class RaceCoachTests
{
    /// <summary>A driver's race: a start entry, then these laps in seconds.</summary>
    private static (PaceAnalysis.Row Row, List<long> Laps) Race(params double[] seconds)
    {
        var entry = new SessionResultEntry { Transponder = "1", DriverName = "Sam" };
        entry.LapTimesMs.Add(3_000);
        entry.LapTimesMs.AddRange(seconds.Select(s => (long)Math.Round(s * 1000)));

        return (PaceAnalysis.For(entry), RaceCoach.Laps(entry));
    }

    [Fact]
    public void A_gap_is_car_lengths_of_the_layout_s_length()
    {
        // 0.263 s of a 14.105 s lap on a 600 ft track of 1/10 cars (400
        // of them): 11.19 ft, 7.5 lengths.
        var lengths = RaceCoach.CarLengths(263, 14_105, 400);

        Assert.NotNull(lengths);
        Assert.Equal(7.46, lengths!.Value, 2);
        Assert.Equal("7 car lengths", RaceCoach.SayLengths(263, 14_105, 400));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    public void No_length_set_says_no_car_lengths(double? feet)
    {
        Assert.Null(RaceCoach.CarLengths(263, 14_105, feet));
        Assert.Null(RaceCoach.SayLengths(263, 14_105, feet));
    }

    [Fact]
    public void No_gap_says_no_car_lengths() => Assert.Null(RaceCoach.CarLengths(0, 14_105, 600));

    [Theory]
    [InlineData(0.4, "under a car length")]
    [InlineData(1.2, "1 car length")]
    [InlineData(2.6, "3 car lengths")]
    public void Lengths_are_said_in_whole_cars(double lengths, string said) =>
        Assert.Equal(said, RaceCoach.Lengths(lengths));

    [Fact]
    public void A_layout_s_length_is_found_by_the_session_s_name()
    {
        using var data = new TempAppData();
        var store = TrackLayoutStore.Load();
        store.Upsert(new TrackLayout { Name = "Club Infield", TrackLengthFeet = 600 });
        store.Upsert(new TrackLayout { Name = "Outer Loop" });

        // No scale set is 1/10: 600 ft of 18-inch cars.
        Assert.Equal(400, RaceCoach.TrackCars(store, " club infield ")!.Value, 6);
        Assert.Null(RaceCoach.TrackCars(store, "Outer Loop"));
        Assert.Null(RaceCoach.TrackCars(store, "Nowhere"));
        Assert.Null(RaceCoach.TrackCars(null, "Club Infield"));
    }

    [Fact]
    public void A_layout_s_car_scale_sets_how_long_a_car_length_is()
    {
        using var data = new TempAppData();
        var store = TrackLayoutStore.Load();
        store.Upsert(new TrackLayout { Name = "Mini-Z Carpet", TrackLengthFeet = 130, CarScale = "1/28" });
        store.Upsert(new TrackLayout { Name = "Big Dirt", TrackLengthFeet = 1000, CarScale = "1/8" });

        // 130 ft of 6.5-inch cars is 240 of them; 1000 ft of 20-inch cars, 600.
        Assert.Equal(240, RaceCoach.TrackCars(store, "Mini-Z Carpet")!.Value, 6);
        Assert.Equal(600, RaceCoach.TrackCars(store, "Big Dirt")!.Value, 6);

        // The same 0.3 s of a 10 s lap: 7 Mini-Z lengths, where counting
        // them as 1/10 cars said 3.
        Assert.Equal("7 car lengths", RaceCoach.SayLengths(300, 10_000, RaceCoach.TrackCars(store, "Mini-Z Carpet")));
    }

    [Theory]
    [InlineData(null, "1/10")]
    [InlineData("", "1/10")]
    [InlineData("1/7", "1/10")]
    [InlineData(" 1/28 ", "1/28")]
    [InlineData("1/12", "1/12")]
    public void An_unknown_or_missing_scale_is_1_10(string? key, string scale) =>
        Assert.Equal(scale, RaceCoach.ScaleOf(key).Key);

    [Fact]
    public void A_layout_s_scale_is_saved_with_it()
    {
        using var data = new TempAppData();
        var store = TrackLayoutStore.Load();
        store.Upsert(new TrackLayout { Name = "Mini-Z Carpet", TrackLengthFeet = 130, CarScale = "1/28" });

        var again = TrackLayoutStore.Load();

        Assert.Equal("1/28", again.All.Single(t => t.Name == "Mini-Z Carpet").CarScale);
    }

    [Fact]
    public void A_lap_more_than_four_percent_off_the_best_is_a_mistake()
    {
        var (_, laps) = Race(14.0, 14.1, 14.55, 14.1, 14.05, 14.9, 14.1);

        // 4% of 14.0 is 0.56: 14.55 is drift, 14.9 is a mistake.
        Assert.Equal(new[] { 5 }, RaceCoach.MistakeLaps(laps));
        Assert.Equal(900, RaceCoach.MistakeCostMs(laps));
    }

    [Fact]
    public void A_slow_warm_up_is_not_a_run_of_mistakes()
    {
        var (_, laps) = Race(15.40, 15.30, 15.20, 14.90, 14.80, 14.70, 14.50, 14.45, 14.40);

        Assert.Empty(RaceCoach.MistakeLaps(laps));
    }

    [Fact]
    public void A_crash_that_spills_into_a_second_lap_is_two_mistakes()
    {
        var (_, laps) = Race(14.2, 14.1, 14.2, 15.8, 15.9, 14.2, 14.1, 14.2);

        Assert.Equal(new[] { 3, 4 }, RaceCoach.MistakeLaps(laps));
    }

    [Fact]
    public void A_race_lost_in_one_crash_is_about_the_crash()
    {
        var (row, laps) = Race(14.62, 14.31, 14.22, 14.18, 14.15, 14.13, 14.105, 14.19, 15.84, 14.21,
            14.17, 14.26, 14.24, 14.31, 14.29, 14.38, 14.35, 14.42, 14.47, 14.51);

        Assert.Equal("Fast, but one mistake (lap 10) cost a third of the time lost.", RaceCoach.Headline(row, laps));

        // Lap 9 came two laps after the best: settle after a quick one.
        Assert.StartsWith("A mistake came right after one of your quickest laps.", RaceCoach.Drill(row, laps));
    }

    [Fact]
    public void Several_mistakes_are_counted()
    {
        var (row, laps) = Race(14.48, 14.30, 14.24, 14.95, 14.22, 14.61, 14.28, 14.33, 15.20, 14.26,
            14.29, 14.70, 14.25, 14.31, 14.88, 14.27, 14.35, 14.30, 14.62, 14.33);

        Assert.Equal("Quick laps, but 3 mistakes cost half of the time lost.", RaceCoach.Headline(row, laps));
    }

    [Fact]
    public void A_clean_race_keeps_pace_analysis_s_own_reading()
    {
        var (row, laps) = Race(14.02, 13.95, 13.91, 13.88, 13.90, 13.93, 13.89, 13.97, 13.92, 13.94);

        Assert.Empty(RaceCoach.MistakeLaps(laps));
        Assert.Equal(row.Verdict, RaceCoach.Headline(row, laps));
    }

    [Fact]
    public void A_driver_who_got_quicker_is_told_the_start_is_where_the_time_is()
    {
        var (row, laps) = Race(15.40, 15.30, 15.20, 14.90, 14.80, 14.70, 14.50, 14.45, 14.40);

        Assert.StartsWith("Do two hard laps in practice before the race.", RaceCoach.Drill(row, laps));
    }

    [Fact]
    public void Too_few_laps_gets_no_reading_beyond_that()
    {
        var (row, laps) = Race(14.0, 14.1);

        Assert.Equal(row.Verdict, RaceCoach.Headline(row, laps));
        Assert.StartsWith("Run a full heat next time", RaceCoach.Drill(row, laps));
    }

    [Theory]
    [InlineData(0.33, "a third")]
    [InlineData(0.5, "half")]
    [InlineData(0.8, "most")]
    [InlineData(0.62, "62%")]
    public void Shares_are_said_in_words_where_they_can_be(double share, string said) =>
        Assert.Equal(said, RaceCoach.Share(share));
}
