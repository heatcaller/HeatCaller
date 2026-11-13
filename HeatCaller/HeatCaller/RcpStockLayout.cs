namespace HeatCaller;

/// <summary>
/// A layout for every kit RCP sells, as a plan you can open and change.
///
/// WHAT IS EXACT AND WHAT IS NOT - read this before trusting one.
///
/// The FLOOR is exact, and so is the BILL OF PIECES. Every kit's tile
/// count is published, and for these layouts it factors into one sensible
/// rectangle: 96 tiles is 12 by 8, 54 is 9 by 6, 108 is 12 by 9. Where RCP
/// also publish what rails come in the box, the layout here fits inside
/// those counts - the presets are checked against their own kit by a test,
/// so opening one and reading the inventory never tells a club to buy
/// something extra.
///
/// They are NOT COPIES OF RCP'S OWN CIRCUITS. They started as
/// reconstructions off RCP's photographs, and the published rail counts
/// ruled those out: the boxes are far more curve-heavy than a photograph
/// suggests - the Wide L ships 16 straight rails against 24 curved pieces -
/// so the real tracks use big sweeps where a reconstruction drew tight
/// one-tile corners. What is here instead is, for each kit, a layout of
/// the right character that the box genuinely holds. The topology is the
/// one in the photograph - a serpentine with four passes, an L with one
/// end cap, an oval with two islands - and the lengths are solved against
/// the parts list rather than measured off a render.
///
/// Said plainly in the picker too, because a club laying out their hall
/// from this deserves to know it is a starting point rather than a
/// drawing of the box art.
///
/// BUILT FROM PIECES where the club's pieces can build it (railed tiles,
/// 13 Nov 2026): each rail on a tile of its own, so no layout asks for
/// custom work. The serpentines and the Wide L meet the wall with a turn
/// either side of the divider, and the 50cm Oval is split by a divider
/// rather than an island that left its lanes one tile wide. The Wide L,
/// the Oblong and the 30cm Oval Wide still sweep their corners with
/// two-tile turns: their boxes are built for them (the Wide L has 16
/// straight rails against 24 curved pieces), and nothing published says
/// which of RCP's pieces make one, so the board shows those turns as not
/// a piece of the club's rather than guessing.
///
/// The two 96-tile sets are ONE layout at two tile sizes - RCP use a
/// single photograph for both, and the only difference is whether the
/// tiles are 30cm or 50cm.
/// </summary>
public static class RcpStockLayout
{
    /// <summary>One layout a club can start from.</summary>
    /// <param name="Name">What the picker calls it.</param>
    /// <param name="KitName">The kit it is built out of - see RcpKit.</param>
    /// <param name="What">One line on what it is like to drive.</param>
    public sealed record Choice(string Name, string KitName, string What);

    public static IReadOnlyList<Choice> All { get; } = new[]
    {
        new Choice(
            "30cm Mini-96 - serpentine",
            "30cm Mini-96",
            "The full 96-tile circuit: four passes, a hairpin at each turnaround. 12 by 8 tiles."),

        new Choice(
            "50cm Large-96 - serpentine",
            "50cm Large-96",
            "The same circuit on 50cm tiles - twice the track in four times the room. 12 by 8 tiles."),

        new Choice(
            "50cm Wide L",
            "50cm Wide-L / Oblong",
            "One divider down from the top wall with a hairpin at its end, on two-tile sweepers. 8 by 5 tiles."),

        new Choice(
            "50cm Oblong",
            "50cm Wide-L / Oblong",
            "The same 40 tiles as a plain oblong - one small island, two long straights. 8 by 5 tiles."),

        new Choice(
            "50cm Oval",
            "50cm Oval",
            "A simple oval round a divider down the middle, two tiles wide each side. 4 by 7 tiles."),

        new Choice(
            "30cm Oval Wide",
            "30cm Oval Wide",
            "A wide oval with two-tile lanes all the way round. 9 by 6 tiles."),

        new Choice(
            "30cm Double Oval Wide",
            "30cm Double Oval Wide",
            "Two islands, so the lap crosses the middle twice. 12 by 9 tiles."),
    };

    public static Choice? ByName(string name) =>
        All.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Builds one, or null for a name nothing here knows. The plan comes
    /// back with its kit and tile size already set, so the kit check is
    /// answering from the moment it opens.
    /// </summary>
    public static RcpTrackDesign? Build(string name)
    {
        if (ByName(name) is not { } choice)
        {
            return null;
        }

        var kit = RcpKit.ByName(choice.KitName);

        var design = new RcpTrackDesign
        {
            TileSizeMm = kit?.TileSizeMm ?? 500,
            KitName = choice.KitName,
        };

        switch (choice.Name)
        {
            case "30cm Mini-96 - serpentine":
            case "50cm Large-96 - serpentine":
                Serpentine(design);
                break;

            case "50cm Wide L":
                WideL(design);
                break;

            case "50cm Oblong":
                Oblong(design);
                break;

            case "50cm Oval":
                SplitOval(design);
                break;

            case "30cm Oval Wide":
                Oval(design, 9, 6, corners: 2, island: new Rectangle(2, 2, 5, 2));
                break;

            case "30cm Double Oval Wide":
                DoubleOval(design);
                break;

            default:
                return null;
        }

        return design;
    }

    // --- The layouts ---------------------------------------------------------

    /// <summary>
    /// The 96-tile circuit, 12 by 8: three dividers off alternate walls,
    /// making four passes with a hairpin at each turnaround.
    ///
    /// The dividers are 7, 7 and 6 tiles long, which is what is left of
    /// the box: the perimeter takes 32 of the 72 straight rails and a
    /// divider is two rails per tile, so 20 tiles of divider is the lot.
    /// They still overlap - each one reaches past where the next one
    /// starts - so the four passes thread into one lap rather than three
    /// dead ends.
    /// </summary>
    private static void Serpentine(RcpTrackDesign design)
    {
        Floor(design, 12, 8);
        Perimeter(design, 12, 8, corners: 1);

        // Each one anchored to a wall at one end and open at the other -
        // the open end is where RcpPlan puts the hairpin's end cap.
        Divider(design, fromCol: 0, row: 2, toCol: 7);
        Divider(design, fromCol: 5, row: 4, toCol: 12);
        Divider(design, fromCol: 0, row: 6, toCol: 6);

        // Where each meets the wall, a turn either side of it rather than a
        // square corner (railed tiles, 13 Nov 2026): a square corner there
        // is two rails on one tile, which is custom work, and the outer
        // curve tile is the piece made for it - the box has 12, and the
        // four round the outside leave eight.
        WallTee(design, wallCol: 0, row: 2, westWall: true);
        WallTee(design, wallCol: 12, row: 4, westWall: false);
        WallTee(design, wallCol: 0, row: 6, westWall: true);

        StartLine(design, 3, 0);
    }

    /// <summary>
    /// 40 tiles, 8 by 5: two-tile sweepers at the corners and one divider
    /// hanging down from the top wall, which is what makes the L.
    ///
    /// Two-tile corners rather than one, because the box is built for
    /// them: 16 straight rails against 24 curved pieces. Tight corners
    /// would spend the straights on the perimeter and leave nothing for
    /// the divider.
    /// </summary>
    private static void WideL(RcpTrackDesign design)
    {
        Floor(design, 8, 5);
        Perimeter(design, 8, 5, corners: 2);

        // Down from the top wall to an open end, where the hairpin goes.
        // Three tiles of it: the perimeter takes 10 of the 16 rails and a
        // divider is two rails a tile.
        for (var row = 0; row < 3; row++)
        {
            design.Walls.Add(new RcpWall { Col = 4, Row = row, Vertical = true, Double = true });
        }

        // A turn either side where it meets the top wall, not two square
        // corners - see WallTee.
        design.Walls.RemoveAll(w => !w.Vertical && w.Row == 0 && w.Col is 3 or 4);
        design.Walls.RemoveAll(w => w.Vertical && w.Col == 4 && w.Row == 0);
        design.Curves.Add(RcpPieces.TurnFor(3, 0, RcpQuadrant.NorthEast));
        design.Curves.Add(RcpPieces.TurnFor(4, 0, RcpQuadrant.NorthWest));

        StartLine(design, 3, 4);
    }

    /// <summary>The same 40 tiles the other way RCP show them: one island, two straights.</summary>
    private static void Oblong(RcpTrackDesign design)
    {
        Floor(design, 8, 5);
        Perimeter(design, 8, 5, corners: 2);
        Island(design, new Rectangle(3, 2, 2, 1));

        StartLine(design, 3, 4);
    }

    /// <summary>A plain oval: a floor, rails round it, and an island in the middle.</summary>
    private static void Oval(RcpTrackDesign design, int cols, int rows, int corners, Rectangle island)
    {
        Floor(design, cols, rows);
        Perimeter(design, cols, rows, corners);
        Island(design, island);

        // Halfway down the left-hand lane: clear of the corner sweeps,
        // which a start line laid in a corner tile would be drawn under.
        StartLine(design, 0, rows / 2);
    }

    /// <summary>
    /// The 50cm Oval's 28 tiles, 4 by 7, round a divider down the middle
    /// (railed tiles, 13 Nov 2026). It was an island two tiles wide, which
    /// left the lanes either side of it one tile wide - and a tile with the
    /// outside wall on one edge and the island's rail on the other is two
    /// rails on one tile, which the club's pieces aren't. A divider leaves
    /// both lanes two tiles wide, each tile with its one rail, and a hairpin
    /// at each end.
    /// </summary>
    private static void SplitOval(RcpTrackDesign design)
    {
        Floor(design, 4, 7);
        Perimeter(design, 4, 7, corners: 1);
        CentreDivider(design, col: 2, fromRow: 2, toRow: 5);

        StartLine(design, 0, 3);
    }

    /// <summary>108 tiles, 12 by 9, with two islands - so a lap crosses the middle twice.</summary>
    private static void DoubleOval(RcpTrackDesign design)
    {
        Floor(design, 12, 9);
        Perimeter(design, 12, 9, corners: 1);

        Island(design, new Rectangle(2, 2, 3, 5));
        Island(design, new Rectangle(7, 2, 3, 5));

        StartLine(design, 0, 4);
    }

    // --- The pieces ----------------------------------------------------------

    private static void Floor(RcpTrackDesign design, int cols, int rows)
    {
        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                design.Tiles.Add(new RcpTile { Col = col, Row = row, Kind = RcpTileKind.Straight });
            }
        }
    }

    /// <summary>
    /// Rails round the outside, with a turn rail sweeping each corner.
    ///
    /// A turn rail is anchored at the grid point the corner turns AROUND -
    /// the floor's own corner - and bulges into the quadrant where the
    /// floor is, with its two ends landing one radius along each wall. So
    /// the top-left corner of a floor is a SouthEast curve at (0,0): both
    /// its arms run away into the floor, south and east. Getting this
    /// backwards draws an arc across the corner tile instead of round it,
    /// which is why it is spelled out here.
    ///
    /// The straights therefore stop <paramref name="corners"/> tiles short
    /// of each corner, leaving the arc exactly the room it sweeps through.
    /// A one-tile corner is the piece that comes in the box; a two-tile one
    /// is two of them, which the curve-heavy sets have plenty of and which
    /// is the only way their straights stretch far enough.
    /// </summary>
    private static void Perimeter(RcpTrackDesign design, int cols, int rows, int corners)
    {
        var r = Math.Max(1, corners);

        for (var col = r; col < cols - r; col++)
        {
            design.Walls.Add(new RcpWall { Col = col, Row = 0 });
            design.Walls.Add(new RcpWall { Col = col, Row = rows });
        }

        for (var row = r; row < rows - r; row++)
        {
            design.Walls.Add(new RcpWall { Col = 0, Row = row, Vertical = true });
            design.Walls.Add(new RcpWall { Col = cols, Row = row, Vertical = true });
        }

        Corner(0, 0, RcpQuadrant.SouthEast);
        Corner(cols, 0, RcpQuadrant.SouthWest);
        Corner(cols, rows, RcpQuadrant.NorthWest);
        Corner(0, rows, RcpQuadrant.NorthEast);

        void Corner(int col, int row, RcpQuadrant quadrant) =>
            design.Curves.Add(new RcpCurve { Col = col, Row = row, Quadrant = quadrant, Radius = r });
    }

    /// <summary>A run of divider - two rails back to back, with track down both sides.</summary>
    private static void Divider(RcpTrackDesign design, int fromCol, int row, int toCol)
    {
        for (var col = Math.Min(fromCol, toCol); col < Math.Max(fromCol, toCol); col++)
        {
            design.Walls.Add(new RcpWall { Col = col, Row = row, Double = true });
        }
    }

    /// <summary>
    /// Where a divider along grid row <paramref name="row"/> meets the side
    /// wall: a one-tile turn on the corner tile either side of it, in place
    /// of the wall's two rails there and the divider's last tile. The T the
    /// club's own floor has where a divider drops from another (Asher's
    /// Drift photos, 8 Nov 2026): a turn either side, the divider's two
    /// rails running into them. Square, each of those two tiles would carry
    /// two rails - custom work.
    /// </summary>
    private static void WallTee(RcpTrackDesign design, int wallCol, int row, bool westWall)
    {
        var col = westWall ? wallCol : wallCol - 1;

        design.Walls.RemoveAll(w => w.Vertical && w.Col == wallCol && (w.Row == row - 1 || w.Row == row));
        design.Walls.RemoveAll(w => !w.Vertical && w.Row == row && w.Col == col);

        design.Curves.Add(RcpPieces.TurnFor(col, row - 1, westWall ? RcpQuadrant.SouthWest : RcpQuadrant.SouthEast));
        design.Curves.Add(RcpPieces.TurnFor(col, row, westWall ? RcpQuadrant.NorthWest : RcpQuadrant.NorthEast));
    }

    /// <summary>A divider down the middle of the floor, open at both ends - the island of an oval two tiles wide each side.</summary>
    private static void CentreDivider(RcpTrackDesign design, int col, int fromRow, int toRow)
    {
        for (var row = fromRow; row < toRow; row++)
        {
            design.Walls.Add(new RcpWall { Col = col, Row = row, Vertical = true, Double = true });
        }
    }

    /// <summary>Rails round a block in the middle of the floor - the inside of an oval.</summary>
    private static void Island(RcpTrackDesign design, Rectangle at)
    {
        for (var col = at.Left; col < at.Right; col++)
        {
            design.Walls.Add(new RcpWall { Col = col, Row = at.Top });
            design.Walls.Add(new RcpWall { Col = col, Row = at.Bottom });
        }

        for (var row = at.Top; row < at.Bottom; row++)
        {
            design.Walls.Add(new RcpWall { Col = at.Left, Row = row, Vertical = true });
            design.Walls.Add(new RcpWall { Col = at.Right, Row = row, Vertical = true });
        }
    }

    private static void StartLine(RcpTrackDesign design, int col, int row) =>
        design.Decals.Add(new RcpDecal { Col = col, Row = row, Kind = RcpDecalKind.FinishLine });
}
