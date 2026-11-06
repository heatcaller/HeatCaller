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

    [Fact]
    public void Two_split_one_loops_still_leave_the_bare_count()
    {
        var track = new TrackLayout { Name = "Built", Design = TwoTileFinish(out _) };
        track.Design!.Decals.Add(new RcpDecal { Col = 9, Row = 4, Kind = RcpDecalKind.SplitLoop1 });

        Assert.Equal("2", track.CutFor(2));
    }
}
