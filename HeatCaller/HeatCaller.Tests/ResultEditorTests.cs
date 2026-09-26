namespace HeatCaller.Tests;

/// <summary>
/// ResultEditor - correcting a saved result, which both the laptop's Edit
/// Result and the phone's result.adjust go through. Pure arithmetic over one
/// SessionRecord, so these need no files.
/// </summary>
public sealed class ResultEditorTests
{
    // --- Taking a phantom crossing off (the 25 Sep audit) ------------------------------

    [Fact]
    public void Taking_a_phantom_crossing_off_takes_its_split_and_the_false_best_lap_with_it()
    {
        // A transponder double-read: one real 11.8s lap timed as 8.3s + 3.5s.
        // The 3.5s "lap" is what the timing saw as her best.
        var record = Race(Driver("A", 1_400, 12_000, 11_800, 8_300, 3_500));

        Apply(record, new ResultAdjustment("A", AddedLaps: -1, 0, 0, ""));

        var a = record.Results.Single();
        Assert.Equal(4, a.Laps);
        Assert.Equal(new long[] { 1_400, 12_000, 11_800, 11_800 }, a.LapTimesMs);
        Assert.Equal(11_800, a.FastLapMs);
    }

    [Fact]
    public void A_lap_added_by_hand_comes_off_first_and_costs_no_lap_time()
    {
        var entry = Driver("A", 1_400, 12_000, 12_100);
        entry.Laps = 4; // one more lap than timed crossings: added by hand in the race
        var record = Race(entry);

        Apply(record, new ResultAdjustment("A", AddedLaps: -1, 0, 0, ""));

        var a = record.Results.Single();
        Assert.Equal(3, a.Laps);
        Assert.Equal(new long[] { 1_400, 12_000, 12_100 }, a.LapTimesMs);
    }

    // --- Penalties are rulings, not corrections ----------------------------------------

    [Fact]
    public void A_lap_penalty_takes_laps_off_and_leaves_every_lap_time_alone()
    {
        var record = Race(Driver("A", 1_400, 12_000, 11_900, 12_100));

        Apply(record, new ResultAdjustment("A", 0, 0, LapPenalty: 1, ""));

        var a = record.Results.Single();
        Assert.Equal(3, a.Laps);
        Assert.Equal(new long[] { 1_400, 12_000, 11_900, 12_100 }, a.LapTimesMs);
        Assert.Equal(11_900, a.FastLapMs);
    }

    [Fact]
    public void A_time_penalty_can_cost_the_win()
    {
        var record = Race(
            Driver("A", 1_400, 12_000, 12_000, 12_000),  // 37.4s
            Driver("B", 1_400, 12_100, 12_100, 12_100)); // 37.7s

        Apply(record, new ResultAdjustment("A", 0, TimePenaltyMs: 5_000, 0, ""));

        Assert.Equal(new[] { "B", "A" }, record.Results.Select(r => r.Transponder));
        Assert.Equal(new[] { 1, 2 }, record.Results.Select(r => r.Position));
        Assert.Equal(42_400, record.Results.Single(r => r.Transponder == "A").ElapsedMs);
    }

    // --- DNF and DQ ---------------------------------------------------------------

    [Fact]
    public void Finishers_first_then_DNFs_then_DQs()
    {
        var record = Race(
            Driver("A", 1_400, 12_000, 12_000, 12_000),
            Driver("B", 1_400, 12_100, 12_100, 12_100),
            Driver("C", 1_400, 12_200, 12_200, 12_200));

        Apply(record,
            new ResultAdjustment("A", 0, 0, 0, ResultEditor.Dq),
            new ResultAdjustment("B", 0, 0, 0, ResultEditor.Dnf));

        Assert.Equal(new[] { "C", "B", "A" }, record.Results.Select(r => r.Transponder));
    }

    [Fact]
    public void A_DQ_counts_no_laps_but_keeps_what_the_race_recorded()
    {
        var record = Race(Driver("A", 1_400, 12_000, 12_000, 12_000));

        Apply(record, new ResultAdjustment("A", 0, 0, 0, "dsq")); // a phone's spelling

        var a = record.Results.Single();
        Assert.Equal(0, a.Laps);
        Assert.Equal(ResultEditor.Dq, a.Outcome);
        Assert.Equal(ResultEditor.Dq, a.Status);
        Assert.Equal(4, a.AsRaced!.Laps);
        Assert.Equal(new long[] { 1_400, 12_000, 12_000, 12_000 }, a.AsRaced.LapTimesMs);
    }

    // --- Corrections are totals, and undo cleanly --------------------------------------

    [Fact]
    public void Correcting_twice_works_from_the_race_s_figures_not_the_last_correction()
    {
        var record = Race(Driver("A", 1_400, 12_000, 11_800, 8_300, 3_500));

        Apply(record, new ResultAdjustment("A", -1, 0, 0, ""));
        Apply(record, new ResultAdjustment("A", -1, 2_000, 0, "")); // same lap, plus a penalty

        var a = record.Results.Single();
        Assert.Equal(4, a.Laps); // not 3
        Assert.Equal(new long[] { 1_400, 12_000, 11_800, 11_800 }, a.LapTimesMs);
    }

    [Fact]
    public void Setting_everything_back_to_zero_gives_the_race_s_result_back_exactly()
    {
        // B won on time (37.7s to 49.4s, five laps each); A second.
        var record = Race(
            Driver("B", 1_400, 12_100, 12_100, 8_300, 3_800),
            Driver("A", 1_400, 12_000, 12_000, 12_000, 12_000));

        var before = ResultEditor.Snapshot(record);

        Apply(record,
            new ResultAdjustment("A", 0, 30_000, 0, ""),
            new ResultAdjustment("B", -1, 0, 1, ResultEditor.Dnf));

        Apply(record,
            new ResultAdjustment("A", 0, 0, 0, ""),
            new ResultAdjustment("B", 0, 0, 0, ""));

        // Results as they were; only the correction log differs.
        var restoredResults = ResultEditor.Snapshot(new SessionRecord { Results = record.Results });
        var originalResults = ResultEditor.Snapshot(new SessionRecord { Results = Deserialize(before).Results });
        Assert.Equal(originalResults, restoredResults);
        Assert.All(record.Results, r => Assert.Null(r.AsRaced));
    }

    [Fact]
    public void A_preview_changes_nothing()
    {
        var record = Race(Driver("A", 1_400, 12_000, 12_000, 12_000), Driver("B", 1_400, 12_100, 12_100, 12_100));
        var before = ResultEditor.Snapshot(record);

        var preview = ResultEditor.Preview(record, new[] { new ResultAdjustment("A", 0, 0, 0, ResultEditor.Dq) });

        Assert.Equal("B", preview[0].Transponder);
        Assert.Equal(before, ResultEditor.Snapshot(record));
    }

    [Fact]
    public void A_correction_that_changes_nothing_is_not_logged()
    {
        var record = Race(Driver("A", 1_400, 12_000, 12_000, 12_000));

        var changed = ResultEditor.Apply(record, new[] { new ResultAdjustment("A", 0, 0, 0, "") }, "no-op", "test");

        Assert.Equal(0, changed);
        Assert.Empty(record.Edits);
    }

    [Fact]
    public void Every_change_is_logged_with_its_reason()
    {
        var record = Race(Driver("A", 1_400, 12_000, 12_000, 12_000));

        ResultEditor.Apply(record, new[] { new ResultAdjustment("A", 0, 5_000, 0, "") }, "  Cut the chicane  ", "laptop");

        var edit = Assert.Single(record.Edits);
        Assert.Equal("A", edit.Transponder);
        Assert.Equal("Cut the chicane", edit.Reason);
        Assert.Contains("time penalty", edit.Change);
    }

    // --- What is refused -----------------------------------------------------------

    [Fact]
    public void Practice_and_demo_results_cannot_be_corrected()
    {
        var practice = Race(Driver("A", 1_400, 12_000));
        practice.SessionType = RaceSessionType.Practice;

        var demo = Race(Driver("A", 1_400, 12_000));
        demo.IsDemo = true;

        var fine = new[] { new ResultAdjustment("A", 0, 5_000, 0, "") };

        Assert.NotNull(ResultEditor.Validate(practice, fine));
        Assert.NotNull(ResultEditor.Validate(demo, fine));
    }

    [Theory]
    [InlineData("Z", 0, 0L, 0, "")]              // not in the result
    [InlineData("A", 0, -1L, 0, "")]             // negative time penalty
    [InlineData("A", 0, 3_600_001L, 0, "")]      // over an hour
    [InlineData("A", 0, 0L, -1, "")]             // negative lap penalty
    [InlineData("A", 501, 0L, 0, "")]            // past the largest lap change
    [InlineData("A", 0, 0L, 0, "retired")]       // not an outcome
    public void Nonsense_is_refused_before_anything_changes(string transponder, int addedLaps, long timePenaltyMs, int lapPenalty, string outcome)
    {
        var record = Race(Driver("A", 1_400, 12_000, 12_000));

        var problem = ResultEditor.Validate(record, new[] { new ResultAdjustment(transponder, addedLaps, timePenaltyMs, lapPenalty, outcome) });

        Assert.NotNull(problem);
    }

    [Fact]
    public void The_same_driver_twice_in_one_correction_is_refused()
    {
        var record = Race(Driver("A", 1_400, 12_000, 12_000));

        var problem = ResultEditor.Validate(record, new[]
        {
            new ResultAdjustment("A", 0, 1_000, 0, ""),
            new ResultAdjustment("a", 0, 2_000, 0, ""), // same transponder, other case
        });

        Assert.NotNull(problem);
    }

    // --- Helpers -----------------------------------------------------------------------

    /// <summary>One driver's result: laps = crossings, time = the sum, best = the fastest after the start dash.</summary>
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

    /// <summary>A saved race with these results, in the order the race gave them.</summary>
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

    private static void Apply(SessionRecord record, params ResultAdjustment[] adjustments)
    {
        Assert.Null(ResultEditor.Validate(record, adjustments));
        ResultEditor.Apply(record, adjustments, "test", "test");
    }

    private static SessionRecord Deserialize(string snapshot)
    {
        var record = new SessionRecord();
        ResultEditor.Restore(record, snapshot);
        return record;
    }
}
