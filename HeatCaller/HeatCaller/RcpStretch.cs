namespace HeatCaller;

/// <summary>
/// Stretching a plan (Track Designer overhaul, Phase 8, 25 Oct 2026): a
/// straight made longer or shorter by whole tiles, by dragging the handle
/// on the right or the bottom edge of a selection.
///
/// THE CUT RUNS RIGHT ACROSS THE PLAN. A track is a loop: lengthen the top
/// straight of an oval and the bottom straight has to grow with it, or the
/// track tears in two. So a stretch is a cut along one grid line, the whole
/// height (or width) of the plan:
///   - LONGER: everything past the line moves out, and the row of tiles
///     just inside it - the selection's last column, or bottom row - is
///     laid again in the gap, with the rails along it. Rails across the
///     track (a vertical rail in a column stretch) aren't repeated - that
///     would wall the track off every tile.
///   - SHORTER: the tiles just inside the line come out, rails and
///     markings with them, and everything past it moves in.
/// A turn rail goes with whichever side its bulge is on. One a cut would go
/// through the middle of - a two-tile turn with the line inside it - stops
/// the stretch with a reason, since half a turn rail isn't a piece. A
/// lane's own rails are worked out again afterwards (RcpAutoRails), so the
/// lanes' turns follow the new shape by themselves; only a turn put down
/// by hand can stop it.
///
/// Rows are the same as columns with the plan read across the diagonal:
/// Rows() swaps every piece's column and row, stretches columns, and swaps
/// them back. Swapping twice puts every piece exactly back, so nothing but
/// the stretch changes.
/// </summary>
public static class RcpStretch
{
    /// <summary>
    /// Stretches <paramref name="design"/> at the vertical grid line
    /// <paramref name="at"/>: <paramref name="by"/> columns more (positive)
    /// or fewer (negative). Null when it was done, otherwise why not, and
    /// then nothing has changed.
    /// </summary>
    public static string? Columns(RcpTrackDesign design, int at, int by)
    {
        if (by == 0)
        {
            return null;
        }

        // Only inside the floor: a cut at or past its edge has no straight to
        // lengthen - it would only widen whatever runs along that edge.
        var floor = design.Tiles.Where(t => t.Kind != RcpTileKind.Infield).ToList();

        if (floor.Count == 0 || at <= floor.Min(t => t.Col) || at > floor.Max(t => t.Col))
        {
            return "Stretch inside the track: pick part of a straight, so its edge is across the track rather than along its end.";
        }

        // A hand turn the cut - or, for shorter, either edge of the strip
        // taken out - would touch: through its middle, or at either end,
        // where its straight carries on or its anchor sits on the line. A
        // turn the lanes put there is worked out again afterwards, so only a
        // hand turn stops it (review, 25 Oct 2026: a turn ending on the line
        // left a gap in its straight, and one anchored on it stayed behind
        // while its rails moved away).
        var lines = by > 0 ? new[] { at } : new[] { at + by, at };

        foreach (var curve in design.Curves.Where(c => !c.Auto))
        {
            var (dx, _) = RcpAutoRails.Step(curve.Quadrant);
            var from = Math.Min(curve.Col, curve.Col + dx * curve.Radius);
            var to = Math.Max(curve.Col, curve.Col + dx * curve.Radius);

            if (lines.Any(line => line >= from - 1e-4f && line <= to + 1e-4f))
            {
                return "That would cut through a turn rail put down by hand. Stretch along a straight, clear of the turns, or take the turn off first.";
            }
        }

        if (by > 0)
        {
            Longer(design, at, by);
        }
        else
        {
            Shorter(design, at, -by);
        }

        return null;
    }

    /// <summary>As Columns, at the horizontal grid line <paramref name="at"/>: rows more or fewer.</summary>
    public static string? Rows(RcpTrackDesign design, int at, int by)
    {
        Transpose(design);

        try
        {
            return Columns(design, at, by);
        }
        finally
        {
            Transpose(design);
        }
    }

    /// <summary>Which column a turn rail's bulge is in - the side of a cut it goes with.</summary>
    private static int BulgeColumn(RcpCurve curve) => RcpAutoRails.Step(curve.Quadrant).Dx > 0 ? curve.Col : curve.Col - 1;

    private static void Longer(RcpTrackDesign design, int at, int by)
    {
        var inside = at - 1;

        // What is repeated: the column just inside the line, as it stands
        // before anything moves.
        var tiles = design.Tiles.Where(t => t.Col == inside).ToList();
        var rails = design.Walls.Where(w => !w.Vertical && w.Col == inside).ToList();

        foreach (var tile in design.Tiles.Where(t => t.Col >= at))
        {
            tile.Col += by;
        }

        foreach (var wall in design.Walls.Where(w => w.Col >= at))
        {
            wall.Col += by;
        }

        foreach (var curve in design.Curves.Where(c => BulgeColumn(c) >= at))
        {
            curve.Col += by;
        }

        foreach (var decal in design.Decals.Where(d => d.Col >= at))
        {
            decal.Col += by;
        }

        foreach (var point in design.Route.Where(p => p.X >= at))
        {
            point.X += by;
        }

        for (var step = 1; step <= by; step++)
        {
            foreach (var tile in tiles)
            {
                // Not its name: "Pit lane" once is a pit lane, not three.
                design.Tiles.Add(new RcpTile { Col = inside + step, Row = tile.Row, Kind = tile.Kind, Turns = tile.Turns, Lane = tile.Lane });
            }

            foreach (var rail in rails)
            {
                design.Walls.Add(new RcpWall { Col = inside + step, Row = rail.Row, Vertical = false, Double = rail.Double, Auto = rail.Auto });
            }
        }
    }

    private static void Shorter(RcpTrackDesign design, int at, int by)
    {
        var from = at - by;

        bool Gone(int col) => col >= from && col < at;

        design.Tiles.RemoveAll(t => Gone(t.Col));
        design.Decals.RemoveAll(d => Gone(d.Col));

        // Rails along the strip, and those across it - but not the one on
        // its near edge, which stays where it is.
        design.Walls.RemoveAll(w => w.Vertical ? w.Col > from && w.Col < at : Gone(w.Col));
        design.Curves.RemoveAll(c => Gone(BulgeColumn(c)));

        foreach (var tile in design.Tiles.Where(t => t.Col >= at))
        {
            tile.Col -= by;
        }

        foreach (var wall in design.Walls.Where(w => w.Col >= at))
        {
            wall.Col -= by;
        }

        foreach (var curve in design.Curves.Where(c => BulgeColumn(c) >= at))
        {
            curve.Col -= by;
        }

        foreach (var decal in design.Decals.Where(d => d.Col >= at))
        {
            decal.Col -= by;
        }

        foreach (var point in design.Route)
        {
            if (point.X >= at)
            {
                point.X -= by;
            }
            else if (point.X > from)
            {
                // A point of the line in the strip taken out comes to its edge.
                point.X = from;
            }
        }

        // A rail on the strip's far edge now lands on its near edge: one
        // rail there, not two - the one put down by hand if either was, and
        // a divider if either was one.
        design.Walls = design.Walls.GroupBy(w => (w.Col, w.Row, w.Vertical)).Select(g =>
        {
            var kept = g.FirstOrDefault(w => !w.Auto) ?? g.First();
            kept.Double = g.Any(w => w.Double);
            return kept;
        }).ToList();
    }

    /// <summary>
    /// Every piece read across the diagonal: column for row. A rail along a
    /// row runs down a column instead; a turn's quarter is mirrored across
    /// the diagonal (north-east and south-west swap). Its own inverse.
    /// </summary>
    private static void Transpose(RcpTrackDesign design)
    {
        foreach (var tile in design.Tiles)
        {
            (tile.Col, tile.Row) = (tile.Row, tile.Col);
        }

        foreach (var wall in design.Walls)
        {
            (wall.Col, wall.Row) = (wall.Row, wall.Col);
            wall.Vertical = !wall.Vertical;
        }

        foreach (var curve in design.Curves)
        {
            (curve.Col, curve.Row) = (curve.Row, curve.Col);
            curve.Quadrant = curve.Quadrant switch
            {
                RcpQuadrant.NorthEast => RcpQuadrant.SouthWest,
                RcpQuadrant.SouthWest => RcpQuadrant.NorthEast,
                _ => curve.Quadrant,
            };
        }

        foreach (var decal in design.Decals)
        {
            (decal.Col, decal.Row) = (decal.Row, decal.Col);
        }

        foreach (var point in design.Route)
        {
            (point.X, point.Y) = (point.Y, point.X);
        }
    }
}
