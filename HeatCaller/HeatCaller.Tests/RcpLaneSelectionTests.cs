namespace HeatCaller.Tests;

/// <summary>
/// Picking up, moving, copying and pasting lanes and their rails (Track
/// Designer overhaul, Phase 3, 25 Oct 2026).
/// </summary>
public sealed class RcpLaneSelectionTests
{
    private static RcpTrackDesign RingLane()
    {
        var design = new RcpTrackDesign();

        foreach (var cell in RcpAutoRails.Ring(new Point(0, 0), new Point(7, 5), 1)!)
        {
            design.Tiles.Add(new RcpTile { Col = cell.X, Row = cell.Y, Lane = 1 });
        }

        RcpAutoRails.Apply(design);

        return design;
    }

    [Fact]
    public void Moving_a_lane_keeps_its_number_and_its_rails_stay_auto()
    {
        var design = RingLane();
        var picked = RcpSelection.Everything(design);

        picked.Move(3, 2);
        picked.Settle(design);

        Assert.All(design.Tiles, t => Assert.Equal(1, t.Lane));
        Assert.All(design.Walls, w => Assert.True(w.Auto));
        Assert.All(design.Curves, c => Assert.True(c.Auto));

        // Worked out again where it now is, nothing changes: the rails
        // moved with the lane.
        Assert.False(RcpAutoRails.Apply(design).Changed);
    }

    [Fact]
    public void Turning_a_lane_leaves_rails_that_need_no_working_out()
    {
        var design = RingLane();
        var picked = RcpSelection.Everything(design);

        picked.TurnClockwise();
        picked.Settle(design);

        Assert.False(RcpAutoRails.Apply(design).Changed);
    }

    [Fact]
    public void A_pasted_lane_is_a_lane_of_its_own()
    {
        var design = RingLane();
        var clip = RcpSelection.Everything(design).Copy();

        Assert.All(clip.Walls, w => Assert.True(w.Auto));

        // Right beside the original: the seam between them must be a
        // divider, which it is only if the two are different lanes.
        var pasted = RcpSelection.Paste(design, clip, new Point(8, 0));

        Assert.All(pasted.Tiles, t => Assert.Equal(2, t.Lane));

        RcpAutoRails.Apply(design);

        var between = design.WallAt(8, 2, vertical: true);

        Assert.NotNull(between);
        Assert.True(between!.Double);
    }
}
