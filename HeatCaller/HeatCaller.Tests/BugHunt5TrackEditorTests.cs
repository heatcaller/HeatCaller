namespace HeatCaller.Tests;

/// <summary>
/// What the fifth bug hunt found in the Track Designer's selection tools,
/// hours after they were written (9 Oct 2026).
///
/// All three of the serious ones came from the same place: RcpSelection.Settle
/// clears whatever the selection has landed on, which is right when a move
/// ENDS and wrong at every other moment. Duplicate put the copy one tile over
/// the original and settled, so the copy ate the thing it was copied from; the
/// arrow keys settled on every press, so holding one down ate a trail across
/// the plan; and Escape mid-drag emptied the selection before the settle, so
/// the pieces that had been moved were left stacked on top of whatever was
/// underneath - invisible, counted twice by the inventory and the kit check,
/// and saved that way.
///
/// The first two are tested here at the model level, where the arithmetic
/// lives. The third needs a drag on a real board, so it is not here; see the
/// comment on RcpTrackCanvas.ClearSelection for the shape of it.
/// </summary>
public sealed class BugHunt5TrackEditorTests
{
    /// <summary>A 4 by 2 block of floor at (3,2), railed all round, with a marking on it.</summary>
    private static RcpTrackDesign Block()
    {
        var design = new RcpTrackDesign();

        for (var col = 3; col < 7; col++)
        {
            for (var row = 2; row < 4; row++)
            {
                design.Tiles.Add(new RcpTile { Col = col, Row = row });
            }
        }

        for (var col = 3; col < 7; col++)
        {
            design.Walls.Add(new RcpWall { Col = col, Row = 2 });
            design.Walls.Add(new RcpWall { Col = col, Row = 4 });
        }

        design.Decals.Add(new RcpDecal { Col = 4, Row = 3, Kind = RcpDecalKind.GridBox });

        return design;
    }

    // --- Duplicate ----------------------------------------------------------

    [Fact]
    public void A_duplicate_leaves_the_block_it_was_copied_from_whole()
    {
        var design = Block();
        var picked = RcpSelection.Everything(design);

        var tiles = design.Tiles.Count;
        var walls = design.Walls.Count;

        var copy = picked.Copy();

        // Exactly what Ctrl+D does.
        RcpSelection.Paste(design, copy, RcpTrackCanvas.Beside(picked.Bounds()));

        Assert.Equal(tiles * 2, design.Tiles.Count);
        Assert.Equal(walls * 2, design.Walls.Count);
        Assert.Equal(2, design.Decals.Count);
    }

    [Fact]
    public void A_duplicate_one_tile_over_the_original_is_what_ate_it()
    {
        // The old behaviour, kept as a test so the reason for Beside is on
        // the record: the paste settles what it lands on, and one tile of
        // overlap is three of the original's tiles gone.
        var design = Block();
        var picked = RcpSelection.Everything(design);
        var box = picked.Bounds();

        RcpSelection.Paste(design, picked.Copy(), new Point(box.Left + 1, box.Top + 1));

        Assert.Equal(13, design.Tiles.Count);
    }

    [Fact]
    public void Beside_a_block_is_clear_of_it_whatever_shape_it_is()
    {
        foreach (var box in new[]
                 {
                     new Rectangle(0, 0, 1, 1),
                     new Rectangle(3, 2, 4, 2),
                     new Rectangle(-5, -5, 12, 9),
                     new Rectangle(0, 0, 0, 0),
                 })
        {
            var at = RcpTrackCanvas.Beside(box);

            Assert.True(
                at.X > box.Right,
                $"a copy of {box} put at {at} would share a seam with it");

            Assert.Equal(box.Top, at.Y);
        }
    }

    // --- A run of nudges ----------------------------------------------------

    [Fact]
    public void A_run_of_nudges_leaves_what_it_travelled_over_alone()
    {
        // One tile selected at (0,0) and four more in its path. The board
        // moves on every press and settles once the run stops, so travelling
        // over something costs it nothing.
        var design = Travelled();
        var picked = RcpSelection.In(design, new Rectangle(0, 0, 1, 1));

        for (var i = 0; i < 5; i++)
        {
            picked.Move(1, 0);
        }

        picked.Settle(design);

        // The one it stopped on is replaced; the three it passed over are not.
        Assert.Equal(4, design.Tiles.Count);
        Assert.Equal(new[] { 2, 3, 4 }, design.Tiles.Where(t => !picked.Holds(t)).Select(t => t.Col).OrderBy(c => c));
        Assert.Equal(5, picked.Tiles.Single().Col);
    }

    [Fact]
    public void Settling_on_every_press_is_what_ate_the_trail()
    {
        // The old behaviour, on the record for the same reason as the
        // duplicate one above.
        var design = Travelled();
        var picked = RcpSelection.In(design, new Rectangle(0, 0, 1, 1));

        for (var i = 0; i < 5; i++)
        {
            picked.Move(1, 0);
            picked.Settle(design);
        }

        Assert.Single(design.Tiles);
    }

    /// <summary>One tile at (0,0) to pick up, and tiles at (2,0) to (5,0) in its path.</summary>
    private static RcpTrackDesign Travelled()
    {
        var design = new RcpTrackDesign();

        design.Tiles.Add(new RcpTile { Col = 0, Row = 0 });

        for (var col = 2; col <= 5; col++)
        {
            design.Tiles.Add(new RcpTile { Col = col, Row = 0, Kind = RcpTileKind.Infield });
        }

        return design;
    }

    // --- The room the plan has to go in -------------------------------------

    [Fact]
    public void Typing_the_room_s_size_can_be_taken_back()
    {
        using var canvas = new RcpTrackCanvas();

        canvas.Design = Block();

        Assert.False(canvas.CanUndo);

        canvas.SetRoom(widthMm: 6000, depthMm: 4000, inFeet: false);

        Assert.True(canvas.CanUndo);
        Assert.Equal(6000, canvas.Design.RoomWidthMm);

        // And a rail drawn after it does not take the measurement with it
        // when IT is taken back - which is the bug, the other way round:
        // the room used to take no step at all, so one Ctrl+Z after drawing
        // blanked it.
        canvas.Undo();

        Assert.Equal(0, canvas.Design.RoomWidthMm);
        Assert.Equal(0, canvas.Design.RoomDepthMm);
    }

    [Fact]
    public void Setting_the_room_to_what_it_already_is_adds_nothing_to_undo()
    {
        using var canvas = new RcpTrackCanvas();

        canvas.Design = Block();
        canvas.SetRoom(widthMm: 6000, depthMm: 4000, inFeet: true);

        // Looking at the boxes, or the units being re-applied as the panel
        // loads, must not cost an undo step each.
        canvas.SetRoom(widthMm: 6000, depthMm: 4000, inFeet: true);
        canvas.SetRoom(widthMm: 6000, depthMm: 4000, inFeet: true);

        canvas.Undo();

        Assert.Equal(0, canvas.Design.RoomWidthMm);
        Assert.False(canvas.CanUndo);
    }

    [Fact]
    public void A_layout_that_only_fits_turned_is_not_a_layout_that_does_not_fit()
    {
        // What the painter now asks, rather than testing the straight-on
        // orientation by itself: a 10 by 4 layout really does go in a hall
        // 5 tiles wide and 12 deep, turned - and the outline round it has to
        // say the same thing the side panel says.
        var fit = RoomFit.Check(
            layoutWidthMm: 10 * 500,
            layoutDepthMm: 4 * 500,
            roomWidthMm: 5 * 500,
            roomDepthMm: 12 * 500);

        Assert.Equal(RoomFit.Verdict.TurnedOnly, fit.Verdict);
        Assert.True(fit.Fits);
    }
}
