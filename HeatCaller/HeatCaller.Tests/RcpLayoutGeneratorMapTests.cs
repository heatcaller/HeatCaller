namespace HeatCaller.Tests;

/// <summary>
/// Build me a track's layouts work on the Big Screen's live track map
/// (Track Designer overhaul, Phase 5, 25 Oct 2026). Kept apart from
/// RcpLayoutGeneratorTests because TrackMap needs the whole app.
/// </summary>
public sealed class RcpLayoutGeneratorMapTests
{
    [Fact]
    public void Every_worked_out_layout_can_be_read_by_the_live_track_map()
    {
        var room = new RcpTrackDesign { TileSizeMm = 500, RoomWidthMm = 9000, RoomDepthMm = 6000 };

        foreach (var layout in RcpLayoutGenerator.For(room, null, 2).Where(l => !l.RcpOwn))
        {
            Assert.True(TrackLine.From(layout.Design) is not null, layout.Name);
        }
    }
}
