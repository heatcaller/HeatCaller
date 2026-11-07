namespace HeatCaller.Tests;

/// <summary>
/// What the seventh bug hunt found in the Track Designer (13 Oct 2026):
/// an erased piece left in the selection, a nudge run that never landed
/// when something else came first, mirrored rail posts and grid boxes,
/// phantom end caps at every turn, and side-panel settings that the next
/// Ctrl+Z took back without being asked.
/// </summary>
public sealed class BugHunt7TrackEditorTests
{
    /// <summary>A row of floor from (0,0) to (3,0).</summary>
    private static RcpTrackDesign Row()
    {
        var design = new RcpTrackDesign();

        for (var col = 0; col < 4; col++)
        {
            design.Tiles.Add(new RcpTile { Col = col, Row = 0 });
        }

        return design;
    }

    private static int TilesAt(RcpTrackDesign design, int col, int row) =>
        design.Tiles.Count(t => t.Col == col && t.Row == row);

    // --- An erased piece leaves the selection -------------------------------

    [Fact]
    public void A_piece_erased_while_picked_is_not_moved_settled_or_copied_any_more()
    {
        // Tiles 0-2 picked, tile 3 not. Tile 2 is erased the way EraseAt
        // does it, then the rest are moved one along and settled - which
        // used to carry the erased tile onto (3,0) and clear the real one.
        var design = Row();
        var picked = RcpSelection.In(design, new Rectangle(0, 0, 3, 1));
        var erased = design.Tiles.Single(t => t.Col == 2);

        design.Tiles.Remove(erased);

        Assert.True(picked.Forget(erased));
        Assert.False(picked.Forget(erased));

        picked.Move(1, 0);
        picked.Settle(design);

        Assert.Equal(1, TilesAt(design, 3, 0));
        Assert.Equal(1, TilesAt(design, 1, 0));
        Assert.Equal(1, TilesAt(design, 2, 0));

        // And a copy holds only what is still on the plan.
        Assert.Equal(2, picked.Copy().Tiles.Count);
    }

    // --- A nudge run lands before anything else ------------------------------

    /// <summary>
    /// The canvas with one tile picked at (0,0) and nudged onto an unpicked
    /// one at (1,0), its settle still waiting on the timer - which never
    /// fires here, as nothing pumps messages on the test's thread.
    /// </summary>
    private static RcpTrackCanvas NudgedOntoATile()
    {
        var canvas = new RcpTrackCanvas();
        var design = new RcpTrackDesign();

        design.Tiles.Add(new RcpTile { Col = 0, Row = 0 });
        canvas.Design = design;
        canvas.Tool = RcpTool.Select;
        canvas.SelectEverything();

        // Put down after the pick, so it is not part of it.
        canvas.Design.Tiles.Add(new RcpTile { Col = 1, Row = 0, Kind = RcpTileKind.Infield });

        canvas.NudgeSelection(1, 0);

        return canvas;
    }

    private static T OnUiThread<T>(Func<T> body) =>
        StaThread.Run(body, TimeSpan.FromMinutes(1), () => "bug hunt 7, track editor");

    [Fact]
    public void Undo_then_redo_of_a_nudge_brings_back_a_plan_with_one_tile_in_the_cell()
    {
        var (afterRedo, kinds) = OnUiThread(() =>
        {
            using var canvas = NudgedOntoATile();

            canvas.Undo();
            canvas.Redo();

            return (TilesAt(canvas.Design, 1, 0), canvas.Design.Tiles.Select(t => t.Kind).ToList());
        });

        Assert.Equal(1, afterRedo);

        // The moved tile is the one that stays, as a drag would leave it.
        Assert.Equal(new[] { RcpTileKind.Straight }, kinds);
    }

    [Fact]
    public void Select_all_straight_after_a_nudge_lands_it_first()
    {
        var stacked = OnUiThread(() =>
        {
            using var canvas = NudgedOntoATile();

            canvas.SelectEverything();

            return TilesAt(canvas.Design, 1, 0);
        });

        Assert.Equal(1, stacked);
    }

    [Fact]
    public void Escape_straight_after_a_nudge_lands_it_and_tells_the_readouts()
    {
        // 6 Nov 2026: ClearSelection settled the run but never raised
        // DesignChanged, so the rails and line were not worked out again.
        var (stacked, told) = OnUiThread(() =>
        {
            using var canvas = NudgedOntoATile();
            var changes = 0;

            canvas.DesignChanged += () => changes++;
            canvas.ClearSelection();

            return (TilesAt(canvas.Design, 1, 0), changes);
        });

        Assert.Equal(1, stacked);
        Assert.Equal(1, told);
    }

    // --- Mirroring markings ----------------------------------------------------

    /// <summary>
    /// Where a point drawn at <paramref name="offset"/> from a marking's
    /// tile centre at no turns ends up, in grid units - turned clockwise
    /// about the centre the way RcpTrackPainter.DrawDecal turns it.
    /// </summary>
    private static (double X, double Y) Drawn(RcpDecal decal, (double X, double Y) offset)
    {
        var (x, y) = offset;

        for (var i = 0; i < ((decal.Turns % 4) + 4) % 4; i++)
        {
            (x, y) = (-y, x);
        }

        return (decal.Col + 0.5 + x, decal.Row + 0.5 + y);
    }

    [Fact]
    public void A_mirrored_marking_is_drawn_where_the_mirror_puts_it()
    {
        // A point on each marking that a wrong turn would move: the post's
        // disc (the tile's top-left corner at no turns), the grid box's
        // chequer (in the left part of the tile), the kerb's arc centre
        // (a little below the middle).
        var kinds = new (RcpDecalKind Kind, (double X, double Y) Offset)[]
        {
            (RcpDecalKind.Post, (-0.5, -0.5)),
            (RcpDecalKind.GridBox, (-0.17, 0)),
            (RcpDecalKind.Kerb, (0, 0.088)),
        };

        foreach (var (kind, offset) in kinds)
        {
            foreach (var leftRight in new[] { true, false })
            {
                for (var turns = 0; turns < 4; turns++)
                {
                    var design = new RcpTrackDesign();
                    var decal = new RcpDecal { Col = 2, Row = 2, Turns = turns, Kind = kind };

                    design.Decals.Add(decal);

                    // Floor beside and below, so the mirror line is not
                    // through the marking's own tile.
                    design.Tiles.Add(new RcpTile { Col = 5, Row = 2 });
                    design.Tiles.Add(new RcpTile { Col = 2, Row = 4 });

                    var picked = RcpSelection.Everything(design);
                    var box = picked.Bounds();
                    var before = Drawn(decal, offset);

                    var expected = leftRight
                        ? (X: box.Left + box.Right - before.X, Y: before.Y)
                        : (X: before.X, Y: box.Top + box.Bottom - before.Y);

                    if (leftRight)
                    {
                        picked.MirrorLeftRight();
                    }
                    else
                    {
                        picked.MirrorTopBottom();
                    }

                    var after = Drawn(decal, offset);
                    var what = $"{kind} at {turns} turns, mirrored {(leftRight ? "left to right" : "top to bottom")}";

                    Assert.True(
                        Math.Abs(after.X - expected.X) < 1e-6 && Math.Abs(after.Y - expected.Y) < 1e-6,
                        $"{what}: drawn at ({after.X},{after.Y}), should be ({expected.X},{expected.Y})");
                }
            }
        }
    }

    // --- End caps at turns ------------------------------------------------------

    [Fact]
    public void A_straight_that_meets_the_end_of_a_turn_is_not_an_open_end()
    {
        // A one-tile turn anchored at (0,0), its arc ending at (1,0) and
        // (0,1), with a straight leaving each of those. The only open ends
        // are the far ends of the two straights.
        var design = new RcpTrackDesign();

        design.Curves.Add(new RcpCurve { Col = 0, Row = 0, Quadrant = RcpQuadrant.SouthEast, Radius = 1f });
        design.Walls.Add(new RcpWall { Col = 1, Row = 0 });
        design.Walls.Add(new RcpWall { Col = 0, Row = 1, Vertical = true });

        var ends = RcpPlan.OpenEnds(design).Select(e => e.At).OrderBy(p => p.X).ToList();

        Assert.Equal(new[] { new Point(0, 2), new Point(2, 0) }, ends);
    }

    [Fact]
    public void The_stock_oval_needs_no_end_caps()
    {
        var oval = RcpStockLayout.Build("50cm Oval");

        Assert.NotNull(oval);
        Assert.Equal(0, RcpPlan.Count(oval!).EndCaps);
    }

    // --- Side-panel settings are undo steps --------------------------------

    [Fact]
    public void Undo_after_changing_the_tile_size_takes_back_only_the_tile_size()
    {
        using var canvas = new RcpTrackCanvas();

        canvas.Design = Row();
        canvas.SetRouteClosed(false);
        canvas.SetTileSize(300);

        canvas.Undo();

        Assert.Equal(500, canvas.Design.TileSizeMm);
        Assert.False(canvas.Design.RouteClosed);

        canvas.Undo();

        Assert.True(canvas.Design.RouteClosed);
        Assert.False(canvas.CanUndo);
    }

    [Fact]
    public void Choosing_a_kit_is_one_undo_step_with_its_tile_size()
    {
        using var canvas = new RcpTrackCanvas();

        canvas.Design = Row();
        canvas.SetKit("30cm Mini-96", 300);

        Assert.Equal("30cm Mini-96", canvas.Design.KitName);
        Assert.Equal(300, canvas.Design.TileSizeMm);

        canvas.Undo();

        Assert.Equal(string.Empty, canvas.Design.KitName);
        Assert.Equal(500, canvas.Design.TileSizeMm);
        Assert.False(canvas.CanUndo);
    }

    [Fact]
    public void A_setting_that_is_already_so_adds_nothing_to_undo()
    {
        using var canvas = new RcpTrackCanvas();

        canvas.Design = Row();
        canvas.SetTileSize(500);
        canvas.SetRouteClosed(true);
        canvas.SetBackdropOpacity(canvas.Design.BackdropOpacity);
        canvas.SetKit(string.Empty, null);

        Assert.False(canvas.CanUndo);
    }

    [Fact]
    public void One_drag_of_the_opacity_slider_is_one_undo_step()
    {
        using var canvas = new RcpTrackCanvas();

        canvas.Design = Row();

        var was = canvas.Design.BackdropOpacity;

        canvas.SetBackdropOpacity(60);
        canvas.SetBackdropOpacity(70);
        canvas.SetBackdropOpacity(80);

        canvas.Undo();

        Assert.Equal(was, canvas.Design.BackdropOpacity);
        Assert.False(canvas.CanUndo);
    }
}
