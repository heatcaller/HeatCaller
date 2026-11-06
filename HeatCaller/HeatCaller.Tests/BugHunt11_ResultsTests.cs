namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11 (5 Nov 2026), Results: a phantom crossing taken off a result
/// with no flying lap left no longer keeps the phantom as the best lap; a
/// row with laps counted by hand no longer gets lap-by-lap positions a lap
/// out of step; and a pasted time with a decimal comma is read whole.
/// </summary>
public sealed class BugHunt11_ResultsTests
{
    // --- ResultEditor: the fold that leaves no flying lap -----------------------------

    [Fact]
    public void Taking_off_the_only_flying_lap_leaves_no_best_lap()
    {
        // Green flag to the line, then a 3.5s double read, then the car
        // retired. Taking the phantom off folds it into the start slot.
        var entry = Driver("A", 1_400, 3_500);
        Assert.Equal(3_500, entry.FastLapMs);
        var record = Race(entry);

        Assert.Null(ResultEditor.Validate(record, new[] { new ResultAdjustment("A", AddedLaps: -1, 0, 0, "") }));
        ResultEditor.Apply(record, new[] { new ResultAdjustment("A", AddedLaps: -1, 0, 0, "") }, "test", "test");

        var a = record.Results.Single();
        Assert.Equal(1, a.Laps);
        Assert.Equal(new long[] { 4_900 }, a.LapTimesMs);
        Assert.Equal(0, a.FastLapMs);
    }

    [Fact]
    public void A_fold_that_leaves_a_flying_lap_still_keeps_the_slower_best()
    {
        // The existing guard is unchanged when a flying lap remains.
        var record = Race(Driver("A", 1_400, 12_000, 11_800, 8_300, 3_500));

        ResultEditor.Apply(record, new[] { new ResultAdjustment("A", AddedLaps: -1, 0, 0, "") }, "test", "test");

        Assert.Equal(11_800, record.Results.Single().FastLapMs);
    }

    // --- LapByLap: laps counted by hand -----------------------------------------------

    [Fact]
    public void A_row_with_a_lap_counted_by_hand_gets_no_lap_by_lap_positions()
    {
        // A led all race; its 3rd crossing (50s) was missed and +1 Lap
        // kept the count. Its timed crossings are 10, 30, 70, 90.
        var a = Row("A", 10_000, 20_000, 40_000, 20_000);
        a.Laps = 5;
        a.LapsByHand = 1;
        var b = Row("B", 11_000, 21_000, 21_000, 21_000);

        var places = LapByLap.Positions(new[] { a, b });

        Assert.Empty(places[0]);
        // B is never wrongly put ahead of A: A's first timed crossing beat
        // B's, and later ones only ever count A ahead when it really was.
        Assert.Equal(2, places[1][0]);
    }

    [Fact]
    public void A_lap_added_in_edit_result_also_takes_the_rows_positions_away()
    {
        var a = Row("A", 10_000, 20_000, 40_000, 20_000);
        a.AddedLaps = 1;
        var b = Row("B", 11_000, 21_000, 21_000, 21_000);

        var places = LapByLap.Positions(new[] { a, b });

        Assert.Empty(places[0]);
        Assert.Equal(3, places[1].Length);
    }

    [Fact]
    public void A_row_with_no_hand_laps_keeps_its_positions()
    {
        var a = Row("A", 10_000, 20_000, 20_000);
        var b = Row("B", 11_000, 21_000, 21_000);

        var places = LapByLap.Positions(new[] { a, b });

        Assert.Equal(new[] { 1, 1 }, places[0]);
        Assert.Equal(new[] { 2, 2 }, places[1]);
    }

    // --- PastedResult: decimal comma ---------------------------------------------------

    [Theory]
    [InlineData("17,234", 17_234)]
    [InlineData("10:10,631", 610_631)]
    [InlineData("17,234 (5)", 17_234)]
    [InlineData("17.234", 17_234)]
    public void A_time_written_with_a_decimal_comma_is_read_whole(string cell, long expected)
    {
        Assert.True(PastedResult.TryTime(cell, out var ms));
        Assert.Equal(expected, ms);
    }

    [Fact]
    public void Laps_and_time_with_a_decimal_comma_is_read_whole()
    {
        Assert.True(PastedResult.TryLapsAndTime("18/10:10,631", out var laps, out var ms));
        Assert.Equal(18, laps);
        Assert.Equal(610_631, ms);
    }

    // --- helpers ------------------------------------------------------------------------

    private static SessionResultEntry Driver(string transponder, params long[] lapTimes) => new()
    {
        Transponder = transponder,
        DriverName = transponder,
        Laps = lapTimes.Length,
        ElapsedMs = lapTimes.Sum(),
        LapTimesMs = lapTimes.ToList(),
        FastLapMs = lapTimes.Skip(1).DefaultIfEmpty(0).Min(),
        Status = "active",
    };

    private static SessionResultEntry Row(string transponder, params long[] laps) => new()
    {
        Transponder = transponder,
        DriverName = transponder,
        Laps = laps.Length,
        LapTimesMs = laps.ToList(),
    };

    private static SessionRecord Race(params SessionResultEntry[] results)
    {
        for (var i = 0; i < results.Length; i++)
        {
            results[i].Position = i + 1;
        }

        return new SessionRecord
        {
            Label = "A-Main",
            SessionType = RaceSessionType.Race,
            StartedAtUtc = DateTime.UtcNow,
            Results = results.ToList(),
        };
    }
}
