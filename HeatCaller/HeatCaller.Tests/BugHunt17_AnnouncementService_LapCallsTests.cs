namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), two in the lap calls.
///
/// A position report in a heat classes share is worded by class place and
/// never with gaps, but with the gaps box ticked it was still thrown away as
/// "the order moved" when a car it named merely crossed the line in the same
/// place. It now counts as places only for that check.
///
/// And a driver's own lap beep played for a rejected pass - a double read at
/// the loop - as if the lap had counted.
///
/// Both sit inside the announcer's live paths (a report waiting for the
/// speaker, a tone played straight to the sound card), so the wiring is
/// read from the source and the rule it leans on is checked directly.
/// </summary>
public sealed class BugHunt17_AnnouncementService_LapCallsTests
{
    private static PositionReportOrder.Row Row(string transponder, int laps, long elapsedMs) =>
        new(new RaceStanding { Transponder = transponder, Laps = laps, ElapsedMs = elapsedMs }, new RaceGap(0, null), new RaceGap(0, null));

    [Fact]
    public void A_car_over_the_line_in_the_same_place_moves_nothing_for_a_places_only_report()
    {
        var read = new[] { Row("201", 10, 80_000), Row("202", 10, 82_000) };
        var now = new[] { Row("201", 11, 88_000), Row("202", 10, 82_000) };

        Assert.False(PositionReportOrder.Moved(read, now, placesOnly: true));
        Assert.True(PositionReportOrder.Moved(read, now, placesOnly: false));
    }

    [Fact]
    public void A_shared_heats_report_is_checked_as_places_only_whatever_the_gaps_box_says()
    {
        var source =Source("AnnouncementService.LapCalls.cs").Replace("\r\n", "\n");

        var box = source.IndexOf("var placesOnly = !config.PositionReportSayGaps;", StringComparison.Ordinal);
        var shared = source.IndexOf("if (SharedHeatClasses?.Invoke() is { Count: > 0 })\n        {\n            placesOnly = true;\n        }", StringComparison.Ordinal);
        var beforeSaid = source.IndexOf("PositionReportOrder.Moved(queuedRows, now, placesOnly)", StringComparison.Ordinal);
        var onceRendered = source.IndexOf("PositionReportOrder.Moved(worded, after, placesOnly)", StringComparison.Ordinal);

        Assert.True(box >= 0 && shared > box, "A shared heat's report must be marked places only after the gaps box is read.");

        // Before both checks: before it is said, and again once it is rendered.
        Assert.True(beforeSaid > shared && onceRendered > shared, "Both checks must come after a shared heat is marked places only.");
    }

    [Fact]
    public void A_drivers_lap_beep_is_only_for_a_lap_that_counted()
    {
        var source = Source("AnnouncementService.LapCalls.cs");
        var play = source.IndexOf("ToneGenerator.PlayDriverLapAsync(toneIndex, beepVolume)", StringComparison.Ordinal);

        Assert.True(play >= 0, "The driver's lap beep is no longer played from the lap calls.");

        var condition = source[source.LastIndexOf("if (", play, StringComparison.Ordinal)..play];

        Assert.Contains("gate.Valid &&", condition, StringComparison.Ordinal);
        Assert.Contains("LapBeeps?.ToneFor(transponder)", condition, StringComparison.Ordinal);
    }

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }
}
