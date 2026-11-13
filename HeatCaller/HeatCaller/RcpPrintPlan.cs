namespace HeatCaller;

/// <summary>One sheet of a plan printed over several: which part of the plan it shows, in tiles.</summary>
/// <param name="Number">Its number, from 1, in the order the sheets print - along the top row, then the next.</param>
/// <param name="Column">Which sheet across it is, from 0.</param>
/// <param name="Row">Which sheet down it is, from 0.</param>
/// <param name="Tiles">The part of the plan it shows, in tiles - the same grid the plan is drawn on.</param>
public sealed record RcpPrintSheet(int Number, int Column, int Row, RectangleF Tiles);

/// <summary>
/// The arithmetic of printing a track plan to scale (Track Designer
/// overhaul, Phase 7, 25 Oct 2026) - kept apart from the printing itself so
/// it can be tested without a printer.
///
/// Everything is worked in millimetres on paper per tile: at 1:20 a 50 cm
/// tile is 25 mm. "Fit on one page" is simply the largest that fits, and is
/// as much to scale as the others - the scale bar on the sheet is drawn
/// from the same number, so a tape measure on the paper reads true either
/// way; the ratio it prints is just not a round one.
/// </summary>
public static class RcpPrintLayout
{
    /// <summary>The scales offered, as the N of 1:N. 0 is "fit on one page".</summary>
    public static readonly IReadOnlyList<int> Ratios = new[] { 10, 20, 25, 50 };

    /// <summary>The most sheets one print may take. A plan past this at the scale asked for is offered a smaller scale.</summary>
    public const int MostSheets = 60;

    /// <summary>How much room is left round the plan, in tiles, so the rails at its edge aren't cut by the edge of the drawing.</summary>
    public const float MarginTiles = 0.3f;

    /// <summary>The part of the plan printed: the floor, rails and line, with a little room round them. Empty for an empty plan.</summary>
    public static RectangleF Area(RcpTrackDesign design)
    {
        if (design.IsEmpty)
        {
            return RectangleF.Empty;
        }

        var bounds = design.BoundsInTiles(includeRoute: true);
        bounds.Inflate(MarginTiles, MarginTiles);

        return bounds;
    }

    /// <summary>Millimetres on paper per tile at 1:<paramref name="ratio"/>.</summary>
    public static double MmPerTile(double tileSizeMm, int ratio) => ratio <= 0 ? 0 : tileSizeMm / ratio;

    /// <summary>The largest millimetres per tile that puts <paramref name="area"/> in <paramref name="boxMm"/> whole.</summary>
    public static double FitMmPerTile(RectangleF area, SizeF boxMm)
    {
        if (area.Width <= 0 || area.Height <= 0 || boxMm.Width <= 0 || boxMm.Height <= 0)
        {
            return 0;
        }

        return Math.Min(boxMm.Width / area.Width, boxMm.Height / area.Height);
    }

    /// <summary>
    /// The sheets <paramref name="area"/> takes at <paramref name="mmPerTile"/>
    /// when each sheet's drawing is <paramref name="boxMm"/>: along the top
    /// row first, then down. Every sheet but the last in a row or column is
    /// full; the last shows what is left. Empty for nothing to print, or
    /// for more than MostSheets.
    /// </summary>
    public static IReadOnlyList<RcpPrintSheet> Sheets(RectangleF area, double mmPerTile, SizeF boxMm)
    {
        var (across, down) = Count(area, mmPerTile, boxMm);

        if (across == 0 || down == 0 || across * down > MostSheets)
        {
            return Array.Empty<RcpPrintSheet>();
        }

        var tilesAcross = (float)(boxMm.Width / mmPerTile);
        var tilesDown = (float)(boxMm.Height / mmPerTile);
        var sheets = new List<RcpPrintSheet>(across * down);

        for (var row = 0; row < down; row++)
        {
            for (var column = 0; column < across; column++)
            {
                var left = area.Left + column * tilesAcross;
                var top = area.Top + row * tilesDown;

                sheets.Add(new RcpPrintSheet(
                    sheets.Count + 1,
                    column,
                    row,
                    new RectangleF(left, top, Math.Min(tilesAcross, area.Right - left), Math.Min(tilesDown, area.Bottom - top))));
            }
        }

        return sheets;
    }

    /// <summary>How many sheets across and down <paramref name="area"/> takes - without making them, so a scale can be judged before it is printed.</summary>
    public static (int Across, int Down) Count(RectangleF area, double mmPerTile, SizeF boxMm)
    {
        if (area.Width <= 0 || area.Height <= 0 || mmPerTile <= 0 || boxMm.Width <= 0 || boxMm.Height <= 0
            || double.IsNaN(mmPerTile) || double.IsInfinity(mmPerTile))
        {
            return (0, 0);
        }

        // A hair under a whole sheet's worth is one sheet, not two: the
        // division can come out at 2.0000001 for a plan that fits exactly.
        static int Of(double length) => Math.Max(1, (int)Math.Ceiling(length - 1e-6));

        var across = area.Width * mmPerTile / boxMm.Width;
        var down = area.Height * mmPerTile / boxMm.Height;

        // Past any sensible print - and, for a scale far too big, past an
        // int: said as one more than the most, which is all a caller needs.
        if (across * down > MostSheets * 4.0)
        {
            return (MostSheets + 1, 1);
        }

        return (Of(across), Of(down));
    }

    /// <summary>The scale as printed: "1:20", or the nearest whole ratio for one that fits a page.</summary>
    public static string RatioText(double tileSizeMm, double mmPerTile) =>
        mmPerTile <= 0 ? string.Empty : $"1:{Math.Round(tileSizeMm / mmPerTile):0}";

    /// <summary>
    /// The scale bar for a sheet: the longest of half a metre, 1, 2, 5, 10
    /// or 20 metres that is no longer than <paramref name="mostMm"/> on
    /// paper - and its length on paper. Zero for none that fits.
    /// </summary>
    public static (double Metres, double PaperMm) ScaleBar(double mmPerTile, double tileSizeMm, double mostMm)
    {
        if (mmPerTile <= 0 || tileSizeMm <= 0)
        {
            return (0, 0);
        }

        var paperPerMetre = 1000.0 / tileSizeMm * mmPerTile;
        var best = (Metres: 0.0, PaperMm: 0.0);

        foreach (var metres in new[] { 0.5, 1, 2, 5, 10, 20 })
        {
            if (metres * paperPerMetre <= mostMm)
            {
                best = (metres, metres * paperPerMetre);
            }
        }

        return best;
    }

    /// <summary>A column of the grid by its letters, from 0: A to Z, then AA, AB.</summary>
    public static string ColumnName(int index)
    {
        if (index < 0)
        {
            return "?";
        }

        var name = string.Empty;

        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            name = (char)('A' + (n - 1) % 26) + name;
        }

        return name;
    }
}

/// <summary>One step of building a plan, as printed after it.</summary>
public sealed record RcpBuildStep(string Heading, IReadOnlyList<string> Lines);

/// <summary>
/// The order a track goes down in, for the sheet printed with the plan
/// (Track Designer overhaul, Phase 7, 25 Oct 2026): the pieces row by row -
/// each tile with its rail already on, since railed tiles Phase 3 (13 Nov
/// 2026), where it used to be the floor and then the rails, dividers and
/// turns - then the loose rail parts, and last the markings - each named by
/// the grid on the printed plan, columns
/// lettered A, B, C from the plan's left and rows numbered 1, 2, 3 from its
/// top (Cell).
/// </summary>
public static class RcpBuildList
{
    /// <summary>The most places named in one line before it gives the count alone - a list of sixty cells helps nobody.</summary>
    public const int MostNamed = 16;

    /// <summary>The grid column and row lettered A1: the plan's top-left tile, rail or turn.</summary>
    public static (int Col, int Row) Origin(RcpTrackDesign design)
    {
        if (design.IsEmpty)
        {
            return (0, 0);
        }

        var bounds = design.BoundsInTiles(includeRoute: false);

        return ((int)Math.Floor(bounds.Left + 1e-4f), (int)Math.Floor(bounds.Top + 1e-4f));
    }

    /// <summary>A tile by its name on the printed grid: "C4".</summary>
    public static string Cell((int Col, int Row) origin, int col, int row) =>
        RcpPrintLayout.ColumnName(col - origin.Col) + (row - origin.Row + 1);

    /// <summary>A piece by its name: "rail tile", "outer curve tile".</summary>
    public static string PieceName(RcpPiece piece) => piece switch
    {
        RcpPiece.Rail => "rail tile",
        RcpPiece.Finish => "finish rail tile",
        RcpPiece.Arched => "arched rail tile",
        RcpPiece.OuterCurve => "outer curve tile",
        RcpPiece.InnerCorner => "inner corner tile",
        RcpPiece.Blank => "blank tile",
        _ => "plain tile",
    };

    /// <summary>
    /// The short mark a piece has in its cell on the printed sheets - the
    /// same as the build list names it by: R rail tile, F finish, A arched,
    /// OC outer curve, IC inner corner, B blank. Nothing for a plain tile.
    /// </summary>
    public static string Code(RcpPiece piece) => piece switch
    {
        RcpPiece.Rail => "R",
        RcpPiece.Finish => "F",
        RcpPiece.Arched => "A",
        RcpPiece.OuterCurve => "OC",
        RcpPiece.InnerCorner => "IC",
        RcpPiece.Blank => "B",
        _ => string.Empty,
    };

    /// <summary>What goes in one cell: "rail tile, rail north", "outer curve tile, turn across its north-east corner".</summary>
    public static string Describe(RcpTile tile, bool custom = false)
    {
        var what = tile.Piece switch
        {
            RcpPiece.Rail or RcpPiece.Finish or RcpPiece.Arched => $"{PieceName(tile.Piece)}, rail {EdgeName(tile.Facing)}",
            RcpPiece.OuterCurve => $"{PieceName(tile.Piece)}, turn across its {CornerName(tile.Corner)} corner",
            RcpPiece.InnerCorner => $"{PieceName(tile.Piece)}, piece in its {CornerName(tile.Corner)} corner",
            _ => PieceName(tile.Piece),
        };

        return custom ? what + " (custom: a second rail)" : what;
    }

    private static string EdgeName(RcpEdge edge) => edge switch
    {
        RcpEdge.North => "north",
        RcpEdge.East => "east",
        RcpEdge.South => "south",
        _ => "west",
    };

    private static string CornerName(RcpQuadrant corner) => corner switch
    {
        RcpQuadrant.NorthEast => "north-east",
        RcpQuadrant.SouthEast => "south-east",
        RcpQuadrant.SouthWest => "south-west",
        _ => "north-west",
    };

    /// <summary>The steps for <paramref name="design"/>. Empty for an empty plan; a step with nothing in it is left out.</summary>
    public static IReadOnlyList<RcpBuildStep> For(RcpTrackDesign design)
    {
        var steps = new List<RcpBuildStep>();

        if (design.IsEmpty)
        {
            return steps;
        }

        var origin = Origin(design);
        var inventory = RcpPlan.Count(design);


        string Named(IEnumerable<(int Col, int Row)> cells)
        {
            var list = cells.Distinct().OrderBy(c => c.Row).ThenBy(c => c.Col).ToList();

            return list.Count <= MostNamed
                ? string.Join(", ", list.Select(c => Cell(origin, c.Col, c.Row)))
                : string.Join(", ", list.Take(MostNamed).Select(c => Cell(origin, c.Col, c.Row))) + $" and {list.Count - MostNamed} more";
        }

        // --- The pieces ------------------------------------------------------
        // Each tile goes down with its rail already on, so the floor, the
        // rails, the dividers and the turns are one step, row by row, each
        // cell saying what goes there (railed tiles, Phase 3, 13 Nov 2026).
        // Worked out on a copy, so a plan whose pieces were never worked out
        // (a starter layout, a file) is read the same as one on the board.
        var pieces = design.Clone();
        var report = RcpPieces.Adopt(pieces);
        var custom = report.CustomTiles.ToHashSet();
        var floor = pieces.Tiles.Where(t => t.Kind != RcpTileKind.Infield).ToList();

        if (floor.Count > 0)
        {
            var counts = floor
                .GroupBy(t => t.Piece)
                .OrderBy(g => g.Key)
                .Select(g => Plural.Of(g.Count(), PieceName(g.Key)))
                .ToList();

            var lines = new List<string>
            {
                $"{Plural.Of(floor.Count, "piece")}: {string.Join(", ", counts)}. Start at the top and lay each row from the left, "
                + "each tile with its rail already on, joining each to the row before it. A blank square on the plan is left empty.",
            };

            foreach (var row in floor.GroupBy(t => t.Row).OrderBy(g => g.Key))
            {
                var cells = row.OrderBy(t => t.Col).ToList();
                var runs = new List<string>();

                for (var i = 0; i < cells.Count;)
                {
                    var what = Describe(cells[i], custom.Contains((cells[i].Col, cells[i].Row)));
                    var j = i;

                    while (j + 1 < cells.Count && cells[j + 1].Col == cells[j].Col + 1
                           && Describe(cells[j + 1], custom.Contains((cells[j + 1].Col, cells[j + 1].Row))) == what)
                    {
                        j++;
                    }

                    var from = RcpPrintLayout.ColumnName(cells[i].Col - origin.Col);
                    var to = RcpPrintLayout.ColumnName(cells[j].Col - origin.Col);
                    runs.Add($"{(i == j ? from : $"{from} to {to}")} {what}");

                    i = j + 1;
                }

                lines.Add($"Row {row.Key - origin.Row + 1}: {string.Join("; ", runs)}");
            }

            if (inventory.Dividers > 0)
            {
                lines.Add($"{Plural.Of(inventory.Dividers, "divider")} between the lanes: two rail tiles back to back, each lane's rail on its own tile. The tiles either side of a divider aren't joined across it.");
            }

            steps.Add(new RcpBuildStep("Lay the pieces", lines));
        }

        // --- What goes on after ----------------------------------------------
        // The loose rail parts: they join pieces already down, so they go on
        // last - and the rails the club has no piece for, which are custom.
        {
            var lines = new List<string>();

            if (inventory.EndCaps > 0)
            {
                lines.Add($"{Plural.Of(inventory.EndCaps, "end cap")} on the rail ends left open.");
            }

            // Each open end of a divider: two corner rail pieces, one on each
            // lane's tile, the rails plugged into them (Asher, 8 Nov 2026).
            if (inventory.DividerEndQuarters > 0)
            {
                lines.Add($"{Plural.Of(inventory.DividerEndQuarters, "corner rail piece")} closing the dividers' open ends, two at each - the inner corner piece.");
            }

            // Rail posts laid by hand: the same quarter piece, on the tiles
            // round each post (Asher, 8 Nov 2026).
            if (inventory.PostQuarters > 0)
            {
                var posts = design.Decals.Where(d => d.Kind == RcpDecalKind.Post).Select(d => (d.Col, d.Row));
                lines.Add($"{Plural.Of(inventory.PostQuarters, "rail post quarter")} - the inner corner piece - on the tiles at {Named(posts)}.");
            }

            if (report.SecondRails > 0)
            {
                lines.Add($"{Plural.Of(report.SecondRails, "second rail")} on a tile that already has one - custom work, two more holes and a rail cut to fit - at {Named(custom)}.");
            }

            if (report.RailsWithNoTile > 0)
            {
                lines.Add($"{Plural.Of(report.RailsWithNoTile, "rail")} with no tile under it - a loose rail, stood on its own.");
            }

            if (report.OtherTurns + report.TurnsWithNoTile > 0)
            {
                var turns = pieces.Curves.Where(c => RcpPieces.TurnTile(pieces, c) is null).ToList();
                lines.Add($"{Plural.Of(turns.Count, "turn rail")} that isn't one tile's own turn - a two-tile turn, a back-to-back pair or one with no outer curve tile under it - at {Named(turns.Select(RcpPlan.QuadrantCell))}.");
            }

            if (lines.Count > 0)
            {
                steps.Add(new RcpBuildStep("Put the loose rail parts on", lines));
            }
        }

        // --- The markings ------------------------------------------------------
        var labelled = design.Tiles.Where(t => !string.IsNullOrWhiteSpace(t.Label)).OrderBy(t => t.Row).ThenBy(t => t.Col).ToList();

        // Rail posts are rail parts, listed with the turns above, not here.
        var markings = design.Decals.Where(d => d.Kind != RcpDecalKind.Post).ToList();

        if (markings.Count > 0 || labelled.Count > 0)
        {
            var lines = new List<string>();

            // The tiles' names first: where the pits and the marshals go.
            if (labelled.Count > 0)
            {
                lines.Add("Named tiles: " + string.Join(", ", labelled.Select(t => $"{Cell(origin, t.Col, t.Row)} {t.Label.Trim()}")) + ".");
            }

            foreach (var kind in markings.GroupBy(d => d.Kind).OrderBy(g => g.Key))
            {
                var where = Named(kind.Select(d => (d.Col, d.Row)));

                lines.Add(kind.Key switch
                {
                    RcpDecalKind.FinishLine => $"The finish line at {where}.",
                    RcpDecalKind.GridBox => $"{Plural.Of(kind.Count(), "grid box", "grid boxes")} at {where}.",
                    RcpDecalKind.Post => $"{Plural.Of(kind.Count(), "rail post")} at {where}.",
                    RcpDecalKind.Kerb => $"{Plural.Of(kind.Count(), "kerb")} at {where}.",
                    RcpDecalKind.Furniture => $"The rostrum, table and timing gear at {where}.",
                    RcpDecalKind.SplitLoop1 => $"Split loop 1 at {where}.",
                    RcpDecalKind.SplitLoop2 => $"Split loop 2 at {where}.",
                    RcpDecalKind.SplitLoop3 => $"Split loop 3 at {where}.",
                    _ => $"{kind.Key} at {where}.",
                });
            }

            steps.Add(new RcpBuildStep("Put the markings down", lines));
        }

        // Numbered as they print.
        return steps.Select((s, i) => s with { Heading = $"{i + 1}. {s.Heading}" }).ToList();
    }
}
