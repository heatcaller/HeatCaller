namespace HeatCaller.Tests;

/// <summary>
/// Fixes on the results side.
///
/// Marking a driver DNF no longer drops them below a car that never turned a
/// lap - that cost them a place's championship points.
///
/// The multi-session results export carries each result's status and
/// outcome, so a corrected DNF or DQ survives the trip out of the app.
/// </summary>
public sealed class BugHuntResultsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- A DNF and a car with no laps ----------------------------------------------------

    [Fact]
    public void A_DNF_with_laps_stays_ahead_of_a_car_that_never_crossed_the_line()
    {
        var record = Race(
            new SessionResultEntry { Transponder = "A", DriverName = "A", Laps = 10, ElapsedMs = 120_000, Status = "active" },
            new SessionResultEntry { Transponder = "C", DriverName = "C", Laps = 7, ElapsedMs = 110_000, Status = "active" },
            new SessionResultEntry { Transponder = "B", DriverName = "B", Laps = 0, ElapsedMs = 0, Status = "active" });

        var adjustments = new[] { new ResultAdjustment("C", 0, 0, 0, ResultEditor.Dnf) };
        Assert.Null(ResultEditor.Validate(record, adjustments));
        ResultEditor.Apply(record, adjustments, "test", "test");

        Assert.Equal(new[] { "A", "C", "B" }, record.Results.Select(r => r.Transponder));
        Assert.Equal(new[] { 1, 2, 3 }, record.Results.Select(r => r.Position));
    }

    [Fact]
    public void Without_a_DNF_a_car_with_no_laps_still_sorts_after_the_finishers_and_before_a_DQ()
    {
        var record = Race(
            new SessionResultEntry { Transponder = "A", DriverName = "A", Laps = 10, ElapsedMs = 120_000, Status = "active" },
            new SessionResultEntry { Transponder = "B", DriverName = "B", Laps = 0, ElapsedMs = 0, Status = "active" },
            new SessionResultEntry { Transponder = "C", DriverName = "C", Laps = 7, ElapsedMs = 110_000, Status = "active" });

        var adjustments = new[] { new ResultAdjustment("C", 0, 0, 0, ResultEditor.Dq) };
        Assert.Null(ResultEditor.Validate(record, adjustments));
        ResultEditor.Apply(record, adjustments, "test", "test");

        Assert.Equal(new[] { "A", "B", "C" }, record.Results.Select(r => r.Transponder));
    }

    // --- The results export keeps the outcome --------------------------------------------

    [Fact]
    public void The_results_CSV_has_a_status_column_that_shows_a_DQ()
    {
        var record = DisqualifiedRace();

        var csv = ResultsExporter.Build(ResultsExporter.Format.ResultsCsv, new[] { record }, null, ProfileStore.Load());
        var lines = csv.TrimStart('﻿').Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.EndsWith(",BestLapSeconds,Status", lines[0]);
        Assert.EndsWith(",DQ", lines.Single(l => l.Contains(",B,")));
        Assert.EndsWith(",active", lines.Single(l => l.Contains(",A,")));
    }

    [Fact]
    public void The_results_JSON_says_which_result_was_disqualified()
    {
        var record = DisqualifiedRace();

        var json = ResultsExporter.Build(ResultsExporter.Format.Json, new[] { record }, null, ProfileStore.Load());

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var results = doc.RootElement[0].GetProperty("results").EnumerateArray().ToList();

        var a = results.Single(r => r.GetProperty("transponder").GetString() == "A");
        var b = results.Single(r => r.GetProperty("transponder").GetString() == "B");

        Assert.Equal("DQ", b.GetProperty("outcome").GetString());
        Assert.Equal("DQ", b.GetProperty("status").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, a.GetProperty("outcome").ValueKind);
    }

    private static SessionRecord DisqualifiedRace()
    {
        var record = Race(
            new SessionResultEntry { Transponder = "A", DriverName = "A", Laps = 10, ElapsedMs = 120_000, Status = "active" },
            new SessionResultEntry { Transponder = "B", DriverName = "B", Laps = 9, ElapsedMs = 118_000, Status = "active" });

        var adjustments = new[] { new ResultAdjustment("B", 0, 0, 0, ResultEditor.Dq) };
        Assert.Null(ResultEditor.Validate(record, adjustments));
        ResultEditor.Apply(record, adjustments, "test", "test");
        return record;
    }

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
            TrackLayoutName = "Track",
            Results = results.ToList(),
        };
    }
}
