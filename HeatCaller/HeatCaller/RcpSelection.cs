namespace HeatCaller;

/// <summary>
/// A handful of the plan, picked out so it can be moved, turned, mirrored,
/// copied or taken away as one thing.
///
/// WHAT IT HOLDS. The actual pieces of the plan, by reference - not copies
/// of them, and not a region the pieces happen to be in. So moving a
/// selection moves the very rails that were picked, and anything that
/// walks the plan afterwards (the inventory, the kit check, the racing
/// line) sees the move without being told about it. The cost is that a
/// selection is only valid for the plan it came out of: Undo swaps in a
/// different copy of the plan, so the canvas drops the selection when it
/// does (see RcpTrackCanvas.Undo).
///
/// WHAT THE GRID MEANS, because the three kinds of piece sit on it
/// differently and every transform below has to get all three right:
///
///   - a TILE or a MARKING fills the cell (Col,Row), the square running
///     from grid point (Col,Row) to (Col+1,Row+1);
///   - a RAIL lies along a seam, from grid point (Col,Row) to either
///     (Col+1,Row) or (Col,Row+1) depending on Vertical;
///   - a TURN RAIL is anchored AT the grid point (Col,Row) and sweeps into
///     one quadrant of it.
///
/// So a quarter turn does three different things at once: it moves a cell
/// to a different cell, it turns a horizontal seam into a vertical one at a
/// different grid point, and it walks a quadrant round one place. Getting
/// any one of them wrong leaves rails half a tile off the floor they are
/// bolted to, which is why RcpSelectionTests checks each mapping on its
/// own rather than trusting a picture of the result.
///
/// WHERE THE RESULT LANDS. Every transform is worked out about the origin
/// and then slid back so the selection's top-left corner has not moved.
/// Not its centre: a centre lands on half a tile as often as not, and a
/// rail half a tile off the grid is not a piece anybody can bolt down.
/// Keeping the top-left still means four quarter turns and two mirrors
/// both come back to exactly where they started, which is the thing a
/// person actually relies on when they are nudging a layout into a room.
/// </summary>
public sealed class RcpSelection
{
    // Reference sets rather than lists: the painter asks "is this one of
    // them?" once per piece on the plan, every frame.
    private readonly HashSet<RcpTile> _tiles = new();
    private readonly HashSet<RcpWall> _walls = new();
    private readonly HashSet<RcpCurve> _curves = new();
    private readonly HashSet<RcpDecal> _decals = new();
    private readonly HashSet<RcpRoutePoint> _route = new();

    public IReadOnlyCollection<RcpTile> Tiles => _tiles;
    public IReadOnlyCollection<RcpWall> Walls => _walls;
    public IReadOnlyCollection<RcpCurve> Curves => _curves;
    public IReadOnlyCollection<RcpDecal> Decals => _decals;
    public IReadOnlyCollection<RcpRoutePoint> Route => _route;

    public int Count => _tiles.Count + _walls.Count + _curves.Count + _decals.Count + _route.Count;

    public bool IsEmpty => Count == 0;

    /// <summary>Just the track itself - what a turn or a mirror has to get right.</summary>
    public int Pieces => _tiles.Count + _walls.Count + _curves.Count + _decals.Count;

    public bool Holds(RcpTile tile) => _tiles.Contains(tile);

    public bool Holds(RcpWall wall) => _walls.Contains(wall);

    public bool Holds(RcpCurve curve) => _curves.Contains(curve);

    public bool Holds(RcpDecal decal) => _decals.Contains(decal);

    public bool Holds(RcpRoutePoint point) => _route.Contains(point);

    // Forgetting a piece that has been taken off the plan. A selection is
    // only valid for pieces still on it (see the class notes): one erased
    // while picked would otherwise go on being moved, settled - clearing
    // whatever real piece it landed on - and copied back. True when it was
    // picked.

    public bool Forget(RcpTile tile) => _tiles.Remove(tile);

    public bool Forget(RcpWall wall) => _walls.Remove(wall);

    public bool Forget(RcpCurve curve) => _curves.Remove(curve);

    public bool Forget(RcpDecal decal) => _decals.Remove(decal);

    public bool Forget(RcpRoutePoint point) => _route.Remove(point);

    // --- Picking -------------------------------------------------------------

    /// <summary>
    /// Everything in a block of cells.
    ///
    /// <paramref name="cells"/> is in tiles, the way a Rectangle normally
    /// is: Left and Top are in, Right and Bottom are the lines past the
    /// last one. The marquee is snapped out to whole tiles before it gets
    /// here, so there is no question of a tile being half picked.
    ///
    /// A rail comes along when BOTH its ends are inside the block, lines
    /// included - which is what puts the rails round the edge of a block of
    /// tiles in with the block, rather than leaving a layout's outside wall
    /// behind every time somebody slides it across the room. A rail just
    /// outside has one end outside, so it stays where it is.
    /// </summary>
    public static RcpSelection In(RcpTrackDesign design, Rectangle cells)
    {
        var picked = new RcpSelection();

        if (design is null || cells.Width <= 0 || cells.Height <= 0)
        {
            return picked;
        }

        bool PointInside(Point p) =>
            p.X >= cells.Left && p.X <= cells.Right && p.Y >= cells.Top && p.Y <= cells.Bottom;

        foreach (var tile in design.Tiles)
        {
            if (cells.Contains(tile.Col, tile.Row))
            {
                picked._tiles.Add(tile);
            }
        }

        foreach (var decal in design.Decals)
        {
            if (cells.Contains(decal.Col, decal.Row))
            {
                picked._decals.Add(decal);
            }
        }

        foreach (var wall in design.Walls)
        {
            if (PointInside(wall.From) && PointInside(wall.To))
            {
                picked._walls.Add(wall);
            }
        }

        foreach (var curve in design.Curves)
        {
            if (PointInside(curve.At))
            {
                picked._curves.Add(curve);
            }
        }

        foreach (var point in design.Route)
        {
            if (point.X >= cells.Left && point.X <= cells.Right
                && point.Y >= cells.Top && point.Y <= cells.Bottom)
            {
                picked._route.Add(point);
            }
        }

        return picked;
    }

    /// <summary>The whole plan - what Ctrl+A picks, and what a copy of a plan is made out of.</summary>
    public static RcpSelection Everything(RcpTrackDesign design)
    {
        var picked = new RcpSelection();

        if (design is null)
        {
            return picked;
        }

        picked._tiles.UnionWith(design.Tiles);
        picked._walls.UnionWith(design.Walls);
        picked._curves.UnionWith(design.Curves);
        picked._decals.UnionWith(design.Decals);
        picked._route.UnionWith(design.Route);

        return picked;
    }

    /// <summary>Adds another selection to this one - what holding Shift and dragging again does.</summary>
    public void Add(RcpSelection other)
    {
        if (other is null)
        {
            return;
        }

        _tiles.UnionWith(other._tiles);
        _walls.UnionWith(other._walls);
        _curves.UnionWith(other._curves);
        _decals.UnionWith(other._decals);
        _route.UnionWith(other._route);
    }

    /// <summary>Takes a selection back out of this one - Shift over something already picked.</summary>
    public void Remove(RcpSelection other)
    {
        if (other is null)
        {
            return;
        }

        _tiles.ExceptWith(other._tiles);
        _walls.ExceptWith(other._walls);
        _curves.ExceptWith(other._curves);
        _decals.ExceptWith(other._decals);
        _route.ExceptWith(other._route);
    }

    /// <summary>Whether everything in <paramref name="other"/> is already picked.</summary>
    public bool HoldsAllOf(RcpSelection other) =>
        other is not null
        && other._tiles.IsSubsetOf(_tiles)
        && other._walls.IsSubsetOf(_walls)
        && other._curves.IsSubsetOf(_curves)
        && other._decals.IsSubsetOf(_decals)
        && other._route.IsSubsetOf(_route);

    // --- Where it is ---------------------------------------------------------

    /// <summary>
    /// The box round it, in GRID POINTS rather than cells - so the box
    /// round one tile at (3,4) runs from (3,4) to (4,5), and a rail along
    /// that tile's bottom edge is inside it rather than one past it.
    ///
    /// Empty for an empty selection. A turn rail counts only its anchor
    /// point: its arc sweeps out to its radius, but the anchor is the part
    /// that has to land on the grid, and the box is here to keep things on
    /// the grid rather than to frame a picture (RcpTrackDesign.BoundsInTiles
    /// does that).
    /// </summary>
    public Rectangle Bounds()
    {
        if (IsEmpty)
        {
            return Rectangle.Empty;
        }

        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;

        void Take(int x, int y)
        {
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        foreach (var tile in _tiles)
        {
            Take(tile.Col, tile.Row);
            Take(tile.Col + 1, tile.Row + 1);
        }

        foreach (var decal in _decals)
        {
            Take(decal.Col, decal.Row);
            Take(decal.Col + 1, decal.Row + 1);
        }

        foreach (var wall in _walls)
        {
            Take(wall.From.X, wall.From.Y);
            Take(wall.To.X, wall.To.Y);
        }

        foreach (var curve in _curves)
        {
            Take(curve.Col, curve.Row);
        }

        foreach (var point in _route)
        {
            Take((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y));
            Take((int)MathF.Ceiling(point.X), (int)MathF.Ceiling(point.Y));
        }

        return Rectangle.FromLTRB(minX, minY, maxX, maxY);
    }

    // --- Moving it -----------------------------------------------------------

    /// <summary>Slides everything picked by whole tiles.</summary>
    public void Move(int cols, int rows)
    {
        if (cols == 0 && rows == 0)
        {
            return;
        }

        foreach (var tile in _tiles)
        {
            tile.Col += cols;
            tile.Row += rows;
        }

        foreach (var decal in _decals)
        {
            decal.Col += cols;
            decal.Row += rows;
        }

        foreach (var wall in _walls)
        {
            wall.Col += cols;
            wall.Row += rows;
        }

        foreach (var curve in _curves)
        {
            curve.Col += cols;
            curve.Row += rows;
        }

        foreach (var point in _route)
        {
            point.X += cols;
            point.Y += rows;
        }
    }

    // --- Turning and mirroring it --------------------------------------------

    /// <summary>
    /// A quarter turn clockwise, on the screen.
    ///
    /// The screen's y runs downwards, so clockwise about the origin sends
    /// (x,y) to (-y,x): a point out to the right swings down, and a point
    /// below swings to the left. Everything else here follows from that -
    /// see the class notes for why each kind of piece needs its own
    /// working.
    /// </summary>
    public void TurnClockwise()
    {
        if (IsEmpty)
        {
            return;
        }

        var was = Bounds();

        foreach (var tile in _tiles)
        {
            // The cell's own corners go to (-Row-1, Col) and (-Row, Col+1),
            // and a cell is named by its top-left one.
            (tile.Col, tile.Row) = (-tile.Row - 1, tile.Col);
            tile.Turns = (tile.Turns + 1) % 4;
        }

        foreach (var decal in _decals)
        {
            (decal.Col, decal.Row) = (-decal.Row - 1, decal.Col);
            decal.Turns = (decal.Turns + 1) % 4;
        }

        foreach (var wall in _walls)
        {
            if (wall.Vertical)
            {
                // Its ends (Col,Row) and (Col,Row+1) go to (-Row,Col) and
                // (-Row-1,Col): the same grid row, one col apart, so it is
                // now a horizontal seam held by the left-hand end.
                (wall.Col, wall.Row) = (-wall.Row - 1, wall.Col);
                wall.Vertical = false;
            }
            else
            {
                // Ends (Col,Row) and (Col+1,Row) go to (-Row,Col) and
                // (-Row,Col+1): one grid col, one row apart - a vertical
                // seam, held by the upper end.
                (wall.Col, wall.Row) = (-wall.Row, wall.Col);
                wall.Vertical = true;
            }
        }

        foreach (var curve in _curves)
        {
            (curve.Col, curve.Row) = (-curve.Row, curve.Col);
            curve.Quadrant = Turned(curve.Quadrant);
        }

        foreach (var point in _route)
        {
            (point.X, point.Y) = (-point.Y, point.X);
        }

        Reanchor(was);
    }

    /// <summary>A mirror image, left to right - about a line down the middle of the selection.</summary>
    public void MirrorLeftRight()
    {
        if (IsEmpty)
        {
            return;
        }

        var was = Bounds();

        foreach (var tile in _tiles)
        {
            tile.Col = -tile.Col - 1;
            tile.Turns = Unturned(tile.Turns);
        }

        foreach (var decal in _decals)
        {
            decal.Col = -decal.Col - 1;
            decal.Turns = DecalMirroredLeftRight(decal);
        }

        foreach (var wall in _walls)
        {
            // A vertical seam stays on a grid line, so its index just
            // flips. A horizontal one runs between two of them, so the end
            // that holds it becomes the other end.
            wall.Col = wall.Vertical ? -wall.Col : -wall.Col - 1;
        }

        foreach (var curve in _curves)
        {
            curve.Col = -curve.Col;
            curve.Quadrant = MirroredLeftRight(curve.Quadrant);
        }

        foreach (var point in _route)
        {
            point.X = -point.X;
        }

        Reanchor(was);
    }

    /// <summary>A mirror image, top to bottom.</summary>
    public void MirrorTopBottom()
    {
        if (IsEmpty)
        {
            return;
        }

        var was = Bounds();

        foreach (var tile in _tiles)
        {
            tile.Row = -tile.Row - 1;
            tile.Turns = Unturned(tile.Turns);
        }

        foreach (var decal in _decals)
        {
            decal.Row = -decal.Row - 1;
            decal.Turns = DecalMirroredTopBottom(decal);
        }

        foreach (var wall in _walls)
        {
            wall.Row = wall.Vertical ? -wall.Row - 1 : -wall.Row;
        }

        foreach (var curve in _curves)
        {
            curve.Row = -curve.Row;
            curve.Quadrant = MirroredTopBottom(curve.Quadrant);
        }

        foreach (var point in _route)
        {
            point.Y = -point.Y;
        }

        Reanchor(was);
    }

    /// <summary>
    /// A quadrant under a quarter turn clockwise. The quadrant is named by
    /// the direction the arc bulges, so it walks round one place.
    /// </summary>
    public static RcpQuadrant Turned(RcpQuadrant quadrant) => quadrant switch
    {
        RcpQuadrant.NorthEast => RcpQuadrant.SouthEast,
        RcpQuadrant.SouthEast => RcpQuadrant.SouthWest,
        RcpQuadrant.SouthWest => RcpQuadrant.NorthWest,
        _ => RcpQuadrant.NorthEast,
    };

    /// <summary>A quadrant in a left-to-right mirror: the east-west half of its name swaps.</summary>
    public static RcpQuadrant MirroredLeftRight(RcpQuadrant quadrant) => quadrant switch
    {
        RcpQuadrant.NorthEast => RcpQuadrant.NorthWest,
        RcpQuadrant.NorthWest => RcpQuadrant.NorthEast,
        RcpQuadrant.SouthEast => RcpQuadrant.SouthWest,
        _ => RcpQuadrant.SouthEast,
    };

    /// <summary>A quadrant in a top-to-bottom mirror: the north-south half swaps.</summary>
    public static RcpQuadrant MirroredTopBottom(RcpQuadrant quadrant) => quadrant switch
    {
        RcpQuadrant.NorthEast => RcpQuadrant.SouthEast,
        RcpQuadrant.SouthEast => RcpQuadrant.NorthEast,
        RcpQuadrant.NorthWest => RcpQuadrant.SouthWest,
        _ => RcpQuadrant.NorthWest,
    };

    /// <summary>
    /// Quarter turns under a mirror.
    ///
    /// A mirror is not a turn, so there is no turn that gives the mirror
    /// image of a shape that has no mirror line of its own. For a shape
    /// whose mirror line runs along the mirror's own at no turns, turning
    /// it the other way is the right answer - and the nearest available
    /// answer for anything else. Markings go through DecalMirroredLeftRight
    /// and DecalMirroredTopBottom, as not all of them are that shape.
    /// </summary>
    private static int Unturned(int turns) => (4 - (turns % 4)) % 4;

    /// <summary>
    /// A marking's turns in a left-to-right mirror, by what it looks like
    /// at no turns (RcpTrackPainter.DrawDecal turns it about the tile's
    /// centre).
    ///
    /// Unturned is only right for a marking whose mirror line runs up and
    /// down it at no turns - a kerb, and the bands, which have both. A post
    /// is not a shape at all: its turns pick the corner of the tile its disc
    /// sits on (0 top left, then clockwise), so a mirror swaps the corners -
    /// it used to come out one tile along from the join it was pinning. A
    /// grid box's mirror line runs across it, which needs half a turn more
    /// than Unturned gives, or its stagger stays on the same side.
    /// </summary>
    private static int DecalMirroredLeftRight(RcpDecal decal)
    {
        var turns = ((decal.Turns % 4) + 4) % 4;

        return decal.Kind switch
        {
            RcpDecalKind.Post => (5 - turns) % 4,
            RcpDecalKind.GridBox => (6 - turns) % 4,
            _ => Unturned(turns),
        };
    }

    /// <summary>A marking's turns in a top-to-bottom mirror - see DecalMirroredLeftRight.</summary>
    private static int DecalMirroredTopBottom(RcpDecal decal)
    {
        var turns = ((decal.Turns % 4) + 4) % 4;

        return decal.Kind switch
        {
            RcpDecalKind.Post => 3 - turns,
            RcpDecalKind.Kerb => (6 - turns) % 4,
            _ => Unturned(turns),
        };
    }

    /// <summary>
    /// Slides the result back so the selection's top-left corner is where
    /// it was before the turn. See the class notes.
    /// </summary>
    private void Reanchor(Rectangle was)
    {
        var now = Bounds();

        Move(was.Left - now.Left, was.Top - now.Top);
    }

    // --- Landing it ----------------------------------------------------------

    /// <summary>
    /// Clears whatever the selection has landed on top of.
    ///
    /// The plan holds one tile, one rail and one marking per place - that
    /// is what the editor's own tools assume everywhere (PaintTile,
    /// PlaceWall, PlaceMarker all find what is there and change it rather
    /// than stacking a second one on it). A move or a paste can easily put
    /// a tile where there already was one, and two tiles in one cell would
    /// be counted twice by the inventory and the kit check - so whatever
    /// was underneath goes, and what was moved there stays.
    ///
    /// Called when a drag ENDS rather than as it goes, so sliding a block
    /// across the plan and back does not eat everything on the way.
    /// </summary>
    public void Settle(RcpTrackDesign design)
    {
        if (design is null || IsEmpty)
        {
            return;
        }

        var cells = _tiles.Select(t => (t.Col, t.Row)).ToHashSet();
        var marks = _decals.Select(d => (d.Col, d.Row)).ToHashSet();
        var seams = _walls.Select(w => (w.Col, w.Row, w.Vertical)).ToHashSet();
        var anchors = _curves.Select(c => (c.Col, c.Row)).ToHashSet();

        design.Tiles.RemoveAll(t => !_tiles.Contains(t) && cells.Contains((t.Col, t.Row)));
        design.Decals.RemoveAll(d => !_decals.Contains(d) && marks.Contains((d.Col, d.Row)));
        design.Walls.RemoveAll(w => !_walls.Contains(w) && seams.Contains((w.Col, w.Row, w.Vertical)));
        design.Curves.RemoveAll(c => !_curves.Contains(c) && anchors.Contains((c.Col, c.Row)));

        // One Split 1 on a plan, one Split 2, one Split 3 - a track has one
        // of each, and RcpPlan.MeasureSectors measures between them. Paste
        // a block with a split loop in it and the one that was already
        // there is the one that goes.
        foreach (var split in _decals.Where(d => RcpPlan.SplitNumberOf(d.Kind) > 0).ToList())
        {
            design.Decals.RemoveAll(d => !_decals.Contains(d) && d.Kind == split.Kind);
        }
    }

    /// <summary>
    /// Whether some of the racing line is picked but not all of it (22 Oct
    /// 2026, known issues). The line is one ordered route, and Paste only
    /// puts route points down on a plan with no line of its own - so a cut
    /// that took part of it left the plan with a line and the clipboard
    /// with points that could never go back. A cut like that leaves the
    /// line where it is.
    /// </summary>
    public bool HoldsPartOfTheLine(RcpTrackDesign design) =>
        design is not null
        && _route.Count > 0
        && design.Route.Any(point => !_route.Contains(point));

    /// <summary>Takes everything picked off the plan - all but the racing line, when <paramref name="keepTheLine"/>.</summary>
    public void DeleteFrom(RcpTrackDesign design, bool keepTheLine = false)
    {
        if (design is null)
        {
            return;
        }

        design.Tiles.RemoveAll(_tiles.Contains);
        design.Walls.RemoveAll(_walls.Contains);
        design.Curves.RemoveAll(_curves.Contains);
        design.Decals.RemoveAll(_decals.Contains);

        if (!keepTheLine)
        {
            design.Route.RemoveAll(_route.Contains);
        }
    }

    // --- Copying it ----------------------------------------------------------

    /// <summary>
    /// A standalone copy of what is picked, with its top-left corner at
    /// (0,0) so it can be dropped anywhere.
    ///
    /// Copies, not the pieces themselves: what is on the clipboard has to
    /// go on being what was copied however the plan is edited afterwards,
    /// and pasting twice has to give two separate things rather than one
    /// thing in two places.
    /// </summary>
    public RcpTrackDesign Copy()
    {
        var bag = new RcpTrackDesign();

        foreach (var tile in _tiles)
        {
            bag.Tiles.Add(tile.Clone());
        }

        foreach (var wall in _walls)
        {
            bag.Walls.Add(wall.Clone());
        }

        foreach (var curve in _curves)
        {
            bag.Curves.Add(curve.Clone());
        }

        foreach (var decal in _decals)
        {
            bag.Decals.Add(decal.Clone());
        }

        foreach (var point in _route)
        {
            bag.Route.Add(new RcpRoutePoint { X = point.X, Y = point.Y });
        }

        var whole = Everything(bag);
        var bounds = whole.Bounds();

        whole.Move(-bounds.Left, -bounds.Top);

        return bag;
    }

    /// <summary>
    /// Drops a copy onto the plan with its top-left corner at
    /// <paramref name="at"/>, and hands back the selection it has become -
    /// so what was just pasted is what is picked, ready to be dragged,
    /// turned or pasted again.
    /// </summary>
    public static RcpSelection Paste(RcpTrackDesign design, RcpTrackDesign clip, Point at)
    {
        var pasted = new RcpSelection();

        if (design is null || clip is null)
        {
            return pasted;
        }

        // A pasted lane is a lane of its own (Track Designer overhaul,
        // Phase 3, 25 Oct 2026): with the number it was copied with, a copy
        // dropped beside the original would join up with it - no divider
        // between them, and one lane's rails worked out round both. Each
        // lane in the clip gets the next free number on this plan.
        var next = RcpAutoRails.NewLane(design);
        var lanes = new Dictionary<int, int>();

        foreach (var tile in clip.Tiles)
        {
            var copy = tile.Clone();

            if (copy.Lane > 0)
            {
                if (!lanes.TryGetValue(copy.Lane, out var renumbered))
                {
                    renumbered = next++;
                    lanes[copy.Lane] = renumbered;
                }

                copy.Lane = renumbered;
            }

            design.Tiles.Add(copy);
            pasted._tiles.Add(copy);
        }

        foreach (var wall in clip.Walls)
        {
            var copy = wall.Clone();
            design.Walls.Add(copy);
            pasted._walls.Add(copy);
        }

        foreach (var curve in clip.Curves)
        {
            var copy = curve.Clone();
            design.Curves.Add(copy);
            pasted._curves.Add(copy);
        }

        foreach (var decal in clip.Decals)
        {
            var copy = decal.Clone();
            design.Decals.Add(copy);
            pasted._decals.Add(copy);
        }

        // 17 Oct 2026, Bug Hunt 8, A31-3: the route is one ordered line,
        // and appending copied points to its end made it jump across the
        // plan (changing the lap length, sectors and pace car). Only put
        // down when the plan has no line of its own yet.
        if (design.Route.Count == 0)
        {
            foreach (var point in clip.Route)
            {
                var copy = new RcpRoutePoint { X = point.X, Y = point.Y };
                design.Route.Add(copy);
                pasted._route.Add(copy);
            }
        }

        pasted.Move(at.X, at.Y);
        pasted.Settle(design);

        return pasted;
    }

    // --- Saying what it is ---------------------------------------------------

    /// <summary>What the status line says about it, in the plan's own words.</summary>
    public string Describe()
    {
        if (IsEmpty)
        {
            return "Nothing picked.";
        }

        var parts = new List<string>();

        if (_tiles.Count > 0)
        {
            parts.Add(Plural.Of(_tiles.Count, "tile"));
        }

        if (_walls.Count > 0)
        {
            parts.Add(Plural.Of(_walls.Count, "rail"));
        }

        if (_curves.Count > 0)
        {
            parts.Add(Plural.Of(_curves.Count, "turn"));
        }

        if (_decals.Count > 0)
        {
            parts.Add(Plural.Of(_decals.Count, "marking"));
        }

        if (_route.Count > 0)
        {
            parts.Add(Plural.Of(_route.Count, "route point"));
        }

        var what = parts.Count switch
        {
            1 => parts[0],
            _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
        };

        var box = Bounds();
        var handles = Pieces > 0 && box.Width >= 1 && box.Height >= 1;

        return $"{what} picked. Drag to move, arrows to nudge, [ and ] to turn, Ctrl+C to copy, Delete to remove"
               + (handles ? " - or drag a square handle, where one shows on its edge, to stretch the plan." : ".");
    }
}
