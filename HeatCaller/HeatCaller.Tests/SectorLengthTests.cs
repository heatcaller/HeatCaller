namespace HeatCaller.Tests;

/// <summary>
/// Split loops on the Track Designer's plan, and the sector lengths and
/// speeds they give (RcpPlan.MeasureSectors, TrackLayout.SectorLengthsFeet,
/// KpiCatalog.SectorMph).
/// </summary>
public sealed class SectorLengthTests
{
    /// <summary>
    /// A 10-by-6 rectangle of racing line, driven clockwise from (0.5, 0.5):
    /// along the top, down the right, back along the bottom, up the left -
    /// 9 + 5 + 9 + 5 = 28 tiles round, closed.
    /// </summary>
    private static RcpTrackDesign Rectangle()
    {
        var design = new RcpTrackDesign { TileSizeMm = 500, RouteClosed = true };

        foreach (var (x, y) in new[] { (0.5f, 0.5f), (9.5f, 0.5f), (9.5f, 5.5f), (0.5f, 5.5f) })
        {
            design.Route.Add(new RcpRoutePoint { X = x, Y = y });
        }

        return design;
    }

    private static void Mark(RcpTrackDesign design, RcpDecalKind kind, int col, int row) =>
        design.Decals.Add(new RcpDecal { Col = col, Row = row, Kind = kind });

    [Fact]
    public void Sectors_are_measured_along_the_line_from_the_finish_and_add_up_to_the_lap()
    {
        var design = Rectangle();
        Mark(design, RcpDecalKind.FinishLine, 2, 0);   // 2 tiles along the top
        Mark(design, RcpDecalKind.SplitLoop1, 9, 2);   // 9 along the top, 2 down: 11
        Mark(design, RcpDecalKind.SplitLoop2, 3, 5);   // 9 + 5 + 6 along the bottom: 20

        var measured = RcpPlan.MeasureSectors(design);

        Assert.NotNull(measured);
        Assert.Null(measured.Value.Problem);

        var sectors = measured.Value.Sectors!;
        Assert.Equal(3, sectors.Length);
        Assert.Equal(9, sectors[0], 3);
        Assert.Equal(9, sectors[1], 3);
        Assert.Equal(10, sectors[2], 3);
        Assert.Equal(RcpPlan.RouteLengthInTiles(design), sectors.Sum(), 3);
    }

    [Fact]
    public void A_split_before_the_finish_is_measured_round_the_closed_line()
    {
        var design = Rectangle();
        Mark(design, RcpDecalKind.FinishLine, 9, 2);   // 11 tiles along the line
        Mark(design, RcpDecalKind.SplitLoop1, 2, 0);   // 2 tiles along: past the start of the line

        var sectors = RcpPlan.MeasureSectors(design)!.Value.Sectors!;
        Assert.Equal(19, sectors[0], 3);
        Assert.Equal(9, sectors[1], 3);
    }

    [Fact]
    public void What_stops_a_plan_measuring_is_said()
    {
        var design = Rectangle();

        // No split loops: nothing to measure, and nothing to say.
        Assert.Null(RcpPlan.MeasureSectors(design));

        Mark(design, RcpDecalKind.SplitLoop1, 9, 2);
        Assert.Contains("finish line", RcpPlan.MeasureSectors(design)!.Value.Problem);

        Mark(design, RcpDecalKind.FinishLine, 2, 0);
        Mark(design, RcpDecalKind.SplitLoop2, 5, 0);   // reached before Split 1

        // Both in the reverse order: the line runs against the cars.
        Assert.Contains("wrong way round", RcpPlan.MeasureSectors(design)!.Value.Problem);

        // Three, with only Split 2 out of place: numbered wrong.
        Mark(design, RcpDecalKind.SplitLoop3, 3, 5);
        Assert.Contains("comes before Split 1", RcpPlan.MeasureSectors(design)!.Value.Problem);

        var open = Rectangle();
        open.RouteClosed = false;
        Mark(open, RcpDecalKind.FinishLine, 2, 0);
        Mark(open, RcpDecalKind.SplitLoop1, 9, 2);
        Assert.Contains("closes into a lap", RcpPlan.MeasureSectors(open)!.Value.Problem);

        var noSplit1 = Rectangle();
        Mark(noSplit1, RcpDecalKind.FinishLine, 2, 0);
        Mark(noSplit1, RcpDecalKind.SplitLoop2, 9, 2);
        Assert.Contains("no Split 1", RcpPlan.MeasureSectors(noSplit1)!.Value.Problem);

        var offTheLine = Rectangle();
        Mark(offTheLine, RcpDecalKind.FinishLine, 2, 0);
        Mark(offTheLine, RcpDecalKind.SplitLoop1, 5, 3);   // the middle of the infield
        Assert.Contains("doesn't pass over Split 1", RcpPlan.MeasureSectors(offTheLine)!.Value.Problem);
    }

    [Fact]
    public void A_layout_gives_its_sector_lengths_in_feet()
    {
        var design = Rectangle();
        Mark(design, RcpDecalKind.FinishLine, 2, 0);
        Mark(design, RcpDecalKind.SplitLoop1, 9, 2);

        var layout = new TrackLayout { Name = "Oval", Design = design };
        var feet = layout.SectorLengthsFeet();

        Assert.NotNull(feet);

        // 9 and 19 tiles of half a metre.
        Assert.Equal(9 * 0.5 * 3.28084, feet[0], 3);
        Assert.Equal(19 * 0.5 * 3.28084, feet[1], 3);
    }

    [Fact]
    public void A_sector_speed_is_its_length_over_its_time_in_mph()
    {
        // 44 feet in a second is 30 mph.
        Assert.Equal(30.0, KpiCatalog.SectorMph(44, 1_000)!.Value, 6);
        Assert.Null(KpiCatalog.SectorMph(44, 0));
        Assert.Null(KpiCatalog.SectorMph(0, 1_000));
    }
}
