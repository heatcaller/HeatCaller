namespace HeatCaller.Tests;

/// <summary>
/// Sector records on a plan whose finish line spans a two-tile lane - one
/// FinishLine decal per tile, as "Build me a track" lays it. Moving a split
/// loop must still put the old records aside (5 Nov 2026, bug hunt 11).
/// </summary>
public sealed class BugHunt11_TrackToolsTests
{
    private static readonly DateTime Day = new(2026, 11, 5, 18, 0, 0, DateTimeKind.Utc);

    private static RcpTrackDesign TwoTileFinish(out RcpDecal split)
    {
        var design = new RcpTrackDesign { TileSizeMm = 500, RouteClosed = true };

        foreach (var (x, y) in new[] { (0.5f, 0.5f), (9.5f, 0.5f), (9.5f, 5.5f), (0.5f, 5.5f) })
        {
            design.Route.Add(new RcpRoutePoint { X = x, Y = y });
        }

        design.Decals.Add(new RcpDecal { Col = 2, Row = 0, Kind = RcpDecalKind.FinishLine });
        design.Decals.Add(new RcpDecal { Col = 2, Row = 1, Kind = RcpDecalKind.FinishLine });
        split = new RcpDecal { Col = 9, Row = 2, Kind = RcpDecalKind.SplitLoop1 };
        design.Decals.Add(split);
        return design;
    }

    [Fact]
    public void A_finish_line_two_tiles_wide_still_keys_the_cut_on_where_the_loops_sit()
    {
        var track = new TrackLayout { Name = "Built", Design = TwoTileFinish(out var split) };

        Assert.Equal("2:2,0;9,2", track.CutFor(2));

        split.Row = 3;
        Assert.Equal("2:2,0;9,3", track.CutFor(2));
    }

    [Fact]
    public void Moving_a_split_loop_on_a_two_tile_finish_puts_the_old_records_aside()
    {
        var track = new TrackLayout { Name = "Built", Design = TwoTileFinish(out var split) };
        track.OfferSectors(new long[] { 4_000, 6_000 }, "Maya", Day);
        Assert.NotNull(track.CurrentSectorRecords()[0]);

        split.Row = 3;
        Assert.Null(track.CurrentSectorRecords()[0]);

        split.Row = 2;
        Assert.Equal("Maya", track.CurrentSectorRecords()[0]!.Holder);
    }

    // 5 Nov 2026, Bug hunt 11 review: a record lap saved while the cut was
    // still the bare count keeps its record-pace sectors after the upgrade
    // keys the cut on the tiles - until a lap is run with the loops there,
    // after which moving a loop puts them aside like the sector records.
    [Fact]
    public void A_record_lap_saved_with_the_bare_count_keeps_its_sectors_after_the_upgrade()
    {
        var track = new TrackLayout
        {
            Name = "Built",
            Design = TwoTileFinish(out var split),
            RecordLapMs = 10_000,
            RecordSectorsMs = new List<long> { 4_000, 6_000 },
            RecordSectorCut = "2",
        };

        Assert.Equal(new long[] { 4_000, 6_000 }, track.RecordSectorsAsCutNow(2));

        track.OfferSectors(new long[] { 4_100, 6_100 }, "Maya", Day);
        Assert.Equal("2:2,0;9,2", track.RecordSectorCut);
        Assert.Equal(new long[] { 4_000, 6_000 }, track.RecordSectorsAsCutNow(2));

        split.Row = 3;
        Assert.Null(track.RecordSectorsAsCutNow(2));
    }

    // 5 Nov 2026, Bug hunt 11 review: the finish tile the cut keys on is the
    // top-left one, whatever order the decals were saved in.
    [Fact]
    public void The_cut_does_not_depend_on_the_order_of_the_finish_tiles()
    {
        var design = TwoTileFinish(out _);
        var forward = new TrackLayout { Name = "Built", Design = design }.CutFor(2);
        var measured = RcpPlan.MeasureSectors(design);

        (design.Decals[0], design.Decals[1]) = (design.Decals[1], design.Decals[0]);

        Assert.Equal("2:2,0;9,2", forward);
        Assert.Equal(forward, new TrackLayout { Name = "Built", Design = design }.CutFor(2));
        Assert.Equal(measured!.Value.Sectors, RcpPlan.MeasureSectors(design)!.Value.Sectors);
    }

    [Fact]
    public void Two_split_one_loops_still_leave_the_bare_count()
    {
        var track = new TrackLayout { Name = "Built", Design = TwoTileFinish(out _) };
        track.Design!.Decals.Add(new RcpDecal { Col = 9, Row = 4, Kind = RcpDecalKind.SplitLoop1 });

        Assert.Equal("2", track.CutFor(2));
    }
}
