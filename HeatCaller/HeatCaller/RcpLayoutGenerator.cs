namespace HeatCaller;

/// <summary>One layout Build me a track offers.</summary>
/// <param name="Name">What the card calls it.</param>
/// <param name="What">One line on what it is like to drive.</param>
/// <param name="Design">The plan itself - lanes, their rails, the finish line and a traced racing line.</param>
/// <param name="LapTiles">The racing line's length, in tiles; 0 when it has none.</param>
/// <param name="Used">The parts it takes.</param>
/// <param name="RcpOwn">One of RCP's own layouts (RcpStockLayout) rather than one worked out for the room.</param>
public sealed record RcpBuiltLayout(string Name, string What, RcpTrackDesign Design, float LapTiles, RcpInventory Used, bool RcpOwn);

/// <summary>
/// Build me a track (Track Designer overhaul, Phase 5, 25 Oct 2026): layouts
/// that fit the room, the lane width and - when the club has said what it
/// owns - the cupboard.
///
/// HOW A SHAPE IS MADE. Each shape is its OUTLINE: a block of tiles the
/// size of the room, with notches cut out of it (an L is a block with a
/// corner cut away, a U has a slot down the middle, a serpentine a slot
/// from each side). The lane is the band just inside the outline, as wide
/// as the lane - the tiles within the lane's width of the edge - and the
/// rest is the island. Made of Phase 3's lane pieces: the tiles are one
/// lane, so RcpAutoRails puts the rails, the turns and the inside corners
/// round it exactly as if somebody had drawn it with the Lane tool, and it
/// can be reshaped with the Lane tool at once.
///
/// A band like that is the same width all the way round by construction,
/// so Trace route finds its middle (RcpPlan.CentreLine), and every layout
/// offered has a racing line that closes - one whose line doesn't is
/// dropped rather than offered.
///
/// WHAT FITS. The room less RoomFit's comfortable margin, in whole tiles.
/// With the club's stock entered, a shape too big for the cupboard is made
/// smaller, a tile at a time, until it fits or there is nothing left of
/// it - never "fits" by inventing parts (RcpStock.Check). Ranked by lap
/// length, longest first: a longer lap is a better use of a room.
///
/// Pure and fast: a dozen small plans worked out, a few milliseconds each.
/// </summary>
public static class RcpLayoutGenerator
{
    /// <summary>The most tiles a side the generator works to - far beyond any club hall, and a guard on the arithmetic.</summary>
    private const int MostTiles = 80;

    /// <summary>
    /// The layouts for the plan's room and tile size: RCP's own first (the
    /// ones in the box instructions), then the worked-out shapes, longest
    /// lap first. Empty with no room given.
    /// </summary>
    public static IReadOnlyList<RcpBuiltLayout> For(RcpTrackDesign plan, RcpStock? stock, int laneWidth)
    {
        var offered = new List<RcpBuiltLayout>();

        if (plan is null || !plan.HasRoom || plan.TileSizeMm <= 0)
        {
            return offered;
        }

        var (cols, rows) = RoomTiles(plan);

        // RCP's own: the ones that fit the room, in the plan's tile size.
        foreach (var choice in RcpStockLayout.All)
        {
            if (RcpStockLayout.Build(choice.Name) is not { } own || Math.Abs(own.TileSizeMm - plan.TileSizeMm) >= 1)
            {
                continue;
            }

            if (!FitsRoom(own, plan))
            {
                continue;
            }

            own.RoomWidthMm = plan.RoomWidthMm;
            own.RoomDepthMm = plan.RoomDepthMm;
            own.RoomInFeet = plan.RoomInFeet;

            // The plan's own lane turns, which loading it must not reset.
            own.LaneTurnRadius = plan.LaneTurnRadius;

            var route = RcpPlan.AutoRoute(own, out var problem);

            if (route is not null && string.IsNullOrEmpty(problem))
            {
                own.Route = route.Select(p => new RcpRoutePoint { X = p.X, Y = p.Y }).ToList();
                own.RouteClosed = true;
                own.RouteAuto = true;
            }

            offered.Add(new RcpBuiltLayout(choice.Name, choice.What, own, RcpPlan.RouteLengthInTiles(own), RcpPlan.Count(own), RcpOwn: true));
        }

        // The worked-out shapes.
        var worked = new List<RcpBuiltLayout>();

        foreach (var shape in Shapes)
        {
            if (Fitted(shape, cols, rows, laneWidth, plan, stock) is { } layout)
            {
                worked.Add(layout);
            }
        }

        offered.AddRange(worked.OrderByDescending(layout => layout.LapTiles).ThenBy(layout => layout.Name, StringComparer.Ordinal));

        return offered;
    }

    /// <summary>The room, less RoomFit's comfortable margin, in whole tiles across and deep.</summary>
    public static (int Cols, int Rows) RoomTiles(RcpTrackDesign plan)
    {
        int Tiles(double mm) =>
            Math.Clamp((int)Math.Floor((mm - RoomFit.ComfortableMarginMm) / plan.TileSizeMm + 1e-9), 0, MostTiles);

        return (Tiles(plan.RoomWidthMm), Tiles(plan.RoomDepthMm));
    }

    /// <summary>Whether a plan goes in the plan's room with room to walk round it (RoomFit), either way round.</summary>
    public static bool FitsRoom(RcpTrackDesign layout, RcpTrackDesign room)
    {
        var bounds = layout.BoundsInTiles(includeRoute: false);

        var fit = RoomFit.Check(
            bounds.Width * layout.TileSizeMm,
            bounds.Height * layout.TileSizeMm,
            room.RoomWidthMm,
            room.RoomDepthMm);

        return fit.Verdict == RoomFit.Verdict.Comfortable;
    }

    // --- The shapes -------------------------------------------------------------

    /// <summary>A shape: its name, a line on it, and its outline for a block of cols x rows with a lane of width w - or null when it can't be made that small.</summary>
    private sealed record Shape(string Name, string What, Func<int, int, int, HashSet<Point>?> Outline);

    private static readonly Shape[] Shapes =
    {
        new("Oval", "Round and round: two long straights and four corners. The quickest to learn, and fast.", Oval),
        new("L", "An oval with a corner cut away: an inside bend between two straights of different lengths.", ElL),
        new("U hairpin", "Down, round the hairpin at the far end and back - a slot up the middle makes the turnaround.", Hairpin),
        new("T", "A long top straight, then down the stem and back up: tight turns either side of the stem.", Tee),
        new("S-bend", "Two sweeping halves joined by a flowing S through the middle.", EsBend),
        new("Serpentine", "Down, up and down again round two slots - lots of corners in a small room, like RCP's 96-tile layouts.", Serpentine),
    };

    /// <summary>The least an arm of a shape can be across: the lane on each side, and an island between.</summary>
    private static int Arm(int w) => 2 * w + 1;

    private static HashSet<Point> Block(int cols, int rows, Func<int, int, bool> keep)
    {
        var cells = new HashSet<Point>();

        for (var x = 0; x < cols; x++)
        {
            for (var y = 0; y < rows; y++)
            {
                if (keep(x, y))
                {
                    cells.Add(new Point(x, y));
                }
            }
        }

        return cells;
    }

    private static HashSet<Point>? Oval(int cols, int rows, int w) =>
        cols >= Arm(w) && rows >= Arm(w) ? Block(cols, rows, (_, _) => true) : null;

    /// <summary>A block with its top-right quarter cut away.</summary>
    private static HashSet<Point>? ElL(int cols, int rows, int w)
    {
        var armAcross = Math.Max(Arm(w), cols / 2);
        var armDown = Math.Max(Arm(w), rows / 2);

        if (cols - armAcross < 1 || rows - armDown < 1)
        {
            return null;
        }

        return Block(cols, rows, (x, y) => x < armAcross || y >= rows - armDown);
    }

    /// <summary>A block with a slot down from the top in the middle: two arms joined at the bottom.</summary>
    private static HashSet<Point>? Hairpin(int cols, int rows, int w)
    {
        var arm = Math.Max(Arm(w), cols / 3);
        var slot = cols - 2 * arm;

        if (slot < 1 || rows < 2 * Arm(w))
        {
            return null;
        }

        var foot = Math.Max(Arm(w), rows / 3);

        return Block(cols, rows, (x, y) => x < arm || x >= cols - arm || y >= rows - foot);
    }

    /// <summary>A bar across the top and a stem down the middle.</summary>
    private static HashSet<Point>? Tee(int cols, int rows, int w)
    {
        var bar = Math.Max(Arm(w), rows / 2);
        var stem = Math.Max(Arm(w), cols / 3);
        var side = (cols - stem) / 2;

        if (side < 1 || rows - bar < 1)
        {
            return null;
        }

        return Block(cols, rows, (x, y) => y < bar || (x >= side && x < side + stem));
    }

    /// <summary>A block with its top-right and bottom-left corners cut away: an S lying down.</summary>
    private static HashSet<Point>? EsBend(int cols, int rows, int w)
    {
        var cut = (rows - Arm(w)) / 2;
        var cutAcross = cols - Math.Max(Arm(w), cols / 2);

        if (cut < 1 || cutAcross < 1 || cols - cutAcross < Arm(w))
        {
            return null;
        }

        return Block(cols, rows, (x, y) =>
            !(x >= cols - cutAcross && y < cut)
            && !(x < cutAcross && y >= rows - cut));
    }

    /// <summary>A block with a slot down from the top and another up from the bottom: down, up and down.</summary>
    private static HashSet<Point>? Serpentine(int cols, int rows, int w)
    {
        var slot = Math.Max(1, (cols - 3 * Arm(w)) / 4);
        var arm = (cols - 2 * slot) / 3;

        if (arm < Arm(w) || rows < 2 * Arm(w))
        {
            return null;
        }

        var firstSlot = arm;
        var secondSlot = 2 * arm + slot;
        var reach = rows - Arm(w);

        return Block(cols, rows, (x, y) =>
            !(x >= firstSlot && x < firstSlot + slot && y < reach)
            && !(x >= secondSlot && x < secondSlot + slot && y >= rows - reach));
    }

    // --- Making a shape into a plan --------------------------------------------

    /// <summary>
    /// The shape at the biggest size that fits the room and the cupboard,
    /// or null. Shrunk a tile at a time, from whichever side keeps it
    /// nearest the room's proportions.
    /// </summary>
    private static RcpBuiltLayout? Fitted(Shape shape, int cols, int rows, int laneWidth, RcpTrackDesign plan, RcpStock? stock)
    {
        var w = Math.Clamp(laneWidth, 1, 3);
        var owned = stock?.OwnedFor(plan.TileSizeMm);
        var checkStock = owned is { Anything: true };
        var ratio = (double)cols / Math.Max(1, rows);

        for (var guard = 0; guard < 2 * MostTiles && cols >= Arm(w) && rows >= Arm(w); guard++)
        {
            // Counted before it is made: the tiles a size takes are known
            // from its outline alone, and making it - rails, two traces -
            // is what made a big hall with a small cupboard take seconds
            // (review, 25 Oct 2026). Too many tiles, straight on to the next.
            var tooMany = checkStock && LaneTiles(shape, cols, rows, w) is { } count && count > owned!.Tiles;

            if (!tooMany && Made(shape, cols, rows, w, plan) is { } layout)
            {
                if (!checkStock || !stock!.Check(layout.Used, plan.TileSizeMm).Any(line => line.Short))
                {
                    return layout;
                }
            }

            // A tile off whichever side keeps it nearest the room's own
            // proportions - always the longer side came out square.
            if ((double)(cols - 1) / rows >= ratio || rows <= Arm(w))
            {
                cols--;
            }
            else
            {
                rows--;
            }
        }

        return null;
    }

    /// <summary>How many tiles of lane a shape takes at a size - its outline less its island - or null when it can't be made that small.</summary>
    private static int? LaneTiles(Shape shape, int cols, int rows, int w)
    {
        if (shape.Outline(cols, rows, w) is not { } outline)
        {
            return null;
        }

        return outline.Count(cell => !IslandOf(outline, cell, w));
    }

    /// <summary>
    /// Whether a tile of an outline is island: the outline all round it, to
    /// the lane's width - the 8 round it, and so on out. The lane is the rest.
    /// </summary>
    private static bool IslandOf(HashSet<Point> outline, Point cell, int w)
    {
        for (var dx = -w; dx <= w; dx++)
        {
            for (var dy = -w; dy <= w; dy++)
            {
                if (!outline.Contains(new Point(cell.X + dx, cell.Y + dy)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// The shape made into a plan at one size: the lane band, its rails, a
    /// finish line across a straight, and the racing line traced. Null when
    /// the shape can't be made that small or its line doesn't close.
    /// </summary>
    private static RcpBuiltLayout? Made(Shape shape, int cols, int rows, int w, RcpTrackDesign plan)
    {
        if (shape.Outline(cols, rows, w) is not { } outline)
        {
            return null;
        }

        bool Island(Point cell) => IslandOf(outline, cell, w);

        var design = new RcpTrackDesign
        {
            TileSizeMm = plan.TileSizeMm,
            KitName = plan.KitName,
            RoomWidthMm = plan.RoomWidthMm,
            RoomDepthMm = plan.RoomDepthMm,
            RoomInFeet = plan.RoomInFeet,
            LaneTurnRadius = plan.LaneTurnRadius,
        };

        foreach (var cell in outline.OrderBy(c => c.Y).ThenBy(c => c.X))
        {
            if (!Island(cell))
            {
                design.Tiles.Add(new RcpTile { Col = cell.X, Row = cell.Y, Lane = 1 });
            }
        }

        if (design.Tiles.Count == outline.Count)
        {
            return null;
        }

        RcpAutoRails.Apply(design);

        var route = RcpPlan.AutoRoute(design, out var problem);

        if (route is null || !string.IsNullOrEmpty(problem))
        {
            return null;
        }

        // The finish line across the middle of the longest straight, and
        // the line traced again so the lap starts there.
        var (finishCells, alongRow) = FinishCells(route, design);

        foreach (var cell in finishCells)
        {
            // Across the track: a quarter turn where the straight runs
            // along a row, or the chequer lies down the lane (review).
            design.Decals.Add(new RcpDecal { Col = cell.X, Row = cell.Y, Kind = RcpDecalKind.FinishLine, Turns = alongRow ? 1 : 0 });
        }

        route = RcpPlan.AutoRoute(design, out problem);

        if (route is null || !string.IsNullOrEmpty(problem))
        {
            return null;
        }

        design.Route = route.Select(p => new RcpRoutePoint { X = p.X, Y = p.Y }).ToList();
        design.RouteClosed = true;
        design.RouteAuto = true;

        if (!FitsRoom(design, plan))
        {
            return null;
        }

        var name = $"{shape.Name}, {cols} x {rows}";

        return new RcpBuiltLayout(name, shape.What, design, RcpPlan.RouteLengthInTiles(design), RcpPlan.Count(design), RcpOwn: false);
    }

    /// <summary>
    /// The tiles a finish line lies across: at the middle of the racing
    /// line's longest straight, every lane tile across the lane there.
    /// </summary>
    private static (List<Point> Cells, bool AlongRow) FinishCells(List<PointF> route, RcpTrackDesign design)
    {
        // The longest run of points heading the same way.
        var bestStart = 0;
        var bestLength = 0;

        for (var i = 0; i < route.Count;)
        {
            var dir = Direction(route[i], route[(i + 1) % route.Count]);
            var j = i + 1;

            while (j < route.Count && Direction(route[j], route[(j + 1) % route.Count]) == dir)
            {
                j++;
            }

            if (j - i > bestLength)
            {
                bestLength = j - i;
                bestStart = i;
            }

            i = j;
        }

        var middle = route[(bestStart + bestLength / 2) % route.Count];
        var heading = Direction(middle, route[(bestStart + bestLength / 2 + 1) % route.Count]);
        var lanes = RcpAutoRails.LaneCells(design);

        // Across the lane: the column of tiles under the point when the
        // line runs along a row, the row of tiles when it runs down a column.
        var cells = new List<Point>();
        var across = heading.Y == 0;
        var at = new Point((int)MathF.Floor(middle.X), (int)MathF.Floor(middle.Y));

        for (var step = -3; step <= 3; step++)
        {
            var cell = across ? new Point(at.X, at.Y + step) : new Point(at.X + step, at.Y);

            // Within half a lane of the line, and on the lane.
            var off = across ? MathF.Abs(cell.Y + 0.5f - middle.Y) : MathF.Abs(cell.X + 0.5f - middle.X);

            if (off < 1.5f && lanes.ContainsKey((cell.X, cell.Y)))
            {
                cells.Add(cell);
            }
        }

        return (cells, across);
    }

    private static Point Direction(PointF a, PointF b) =>
        new(Math.Sign(MathF.Round(b.X - a.X, 3)), Math.Sign(MathF.Round(b.Y - a.Y, 3)));
}
