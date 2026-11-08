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
/// (Track Designer overhaul, Phase 7, 25 Oct 2026): the floor row by row,
/// then the rails round it, the dividers between the lanes, the turns, and
/// last the markings - each named by the grid on the printed plan, columns
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
        var (outer, inner) = RcpPlan.CornerTiles(design);

        string Named(IEnumerable<(int Col, int Row)> cells)
        {
            var list = cells.Distinct().OrderBy(c => c.Row).ThenBy(c => c.Col).ToList();

            return list.Count <= MostNamed
                ? string.Join(", ", list.Select(c => Cell(origin, c.Col, c.Row)))
                : string.Join(", ", list.Take(MostNamed).Select(c => Cell(origin, c.Col, c.Row))) + $" and {list.Count - MostNamed} more";
        }

        // --- The floor -------------------------------------------------------
        var floor = design.Tiles.Where(t => t.Kind != RcpTileKind.Infield).ToList();

        if (floor.Count > 0)
        {
            var lines = new List<string>
            {
                $"{Plural.Of(floor.Count, "tile")}: {inventory.PlainTiles} plain, {inventory.OuterCurveTiles} outer curve, "
                + $"{inventory.InnerCornerTiles} inner corner. Start at the top and lay each row from the left, joining each tile "
                + "to the row before it. A blank square on the plan is left empty.",
            };

            foreach (var row in floor.GroupBy(t => t.Row).OrderBy(g => g.Key))
            {
                var cols = row.Select(t => t.Col).Distinct().OrderBy(c => c).ToList();
                var runs = new List<string>();

                for (var i = 0; i < cols.Count;)
                {
                    var j = i;

                    while (j + 1 < cols.Count && cols[j + 1] == cols[j] + 1)
                    {
                        j++;
                    }

                    var from = RcpPrintLayout.ColumnName(cols[i] - origin.Col);
                    var to = RcpPrintLayout.ColumnName(cols[j] - origin.Col);
                    runs.Add(i == j ? from : $"{from} to {to}");

                    i = j + 1;
                }

                var outerHere = cols.Where(c => outer.Contains((c, row.Key))).Select(c => RcpPrintLayout.ColumnName(c - origin.Col)).ToList();
                var innerHere = cols.Where(c => inner.Contains((c, row.Key))).Select(c => RcpPrintLayout.ColumnName(c - origin.Col)).ToList();

                var corners = new List<string>();

                if (outerHere.Count > 0)
                {
                    corners.Add($"outer curve at {string.Join(", ", outerHere)}");
                }

                if (innerHere.Count > 0)
                {
                    corners.Add($"inner corner at {string.Join(", ", innerHere)}");
                }

                lines.Add($"Row {row.Key - origin.Row + 1}: {string.Join(", ", runs)} - {Plural.Of(cols.Count, "tile")}"
                          + (corners.Count > 0 ? $" ({string.Join("; ", corners)})" : string.Empty));
            }

            steps.Add(new RcpBuildStep("Lay the floor", lines));
        }

        // --- The rails ---------------------------------------------------------
        var single = design.Walls.Count(w => !w.Double);

        if (single > 0)
        {
            var lines = new List<string>
            {
                $"{Plural.Of(single, "straight rail")} along the edges drawn on the plan, each on the tile it runs beside.",
            };

            if (inventory.EndCaps > 0)
            {
                lines.Add($"{Plural.Of(inventory.EndCaps, "end cap")} on the rail ends left open.");
            }

            steps.Add(new RcpBuildStep("Put the rails round it", lines));
        }

        var dividers = design.Walls.Count(w => w.Double);

        if (dividers > 0)
        {
            steps.Add(new RcpBuildStep("Put in the dividers", new[]
            {
                $"{Plural.Of(dividers, "divider")} between the lanes - each one two straight rails back to back, so {dividers * 2} rails.",
            }));
        }

        // --- The turns ---------------------------------------------------------
        // The turn rails round the outside of bends, as the parts list counts
        // them (RcpInventory.OuterTurnRails). A turn put round an inside
        // corner bulges off the track and stands in for that corner's inner
        // corner piece, so it is the inner corner line that sends a builder
        // there - listed here too, it was a rail the parts never counted on
        // a square with no floor (review, 25 Oct 2026).
        var outerTurns = design.Curves.Where(c => outer.Contains(RcpPlan.QuadrantCell(c))).ToList();

        if (outerTurns.Count > 0 || inventory.InnerCornerPieces > 0)
        {
            var lines = new List<string>();

            foreach (var size in outerTurns.GroupBy(c => c.Radius).OrderBy(g => g.Key))
            {
                var name = size.Key switch
                {
                    < 0.99f => "half-tile",
                    < 1.01f => "one-tile",
                    < 2.01f and > 1.99f => "two-tile",
                    _ => $"{size.Key:0.#}-tile",
                };

                var rails = size.Sum(c => c.Double ? 2 : 1);

                lines.Add($"{Plural.Of(rails, $"{name} turn rail")}"
                          + (size.Any(c => c.Double) ? " (back-to-back pairs counted as two)" : string.Empty)
                          + $", on the tiles at {Named(size.Select(RcpPlan.QuadrantCell))}.");
            }

            if (inventory.InnerCornerTiles > 0)
            {
                lines.Add($"{Plural.Of(inventory.InnerCornerTiles, "inner corner piece")}, one on each inner corner tile: {Named(inner)}.");
            }

            // Rail posts laid by hand: the same quarter piece, on the tiles
            // round each post (Asher, 8 Nov 2026).
            if (inventory.PostQuarters > 0)
            {
                var posts = design.Decals.Where(d => d.Kind == RcpDecalKind.Post).Select(d => (d.Col, d.Row));
                lines.Add($"{Plural.Of(inventory.PostQuarters, "rail post quarter")} - the inner corner piece - on the tiles at {Named(posts)}.");
            }

            steps.Add(new RcpBuildStep("Put the turns in", lines));
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
