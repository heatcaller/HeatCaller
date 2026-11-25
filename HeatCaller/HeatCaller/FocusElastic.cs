namespace HeatCaller;

/// <summary>What a piece of the page is, for the Focus stage: how it moves.</summary>
internal enum FocusPieceKind
{
    /// <summary>Glides from its place before to its place after, its contents pinned to its edges.</summary>
    Card,

    /// <summary>Only before (Focus) or only after (Restore): fades out drifting right, or back in - the column's buttons and meters.</summary>
    Folding,
}

/// <summary>One piece of the page: where it was and where it goes, in the main window's client area. Order is its place from the top, for the folding ones.</summary>
internal readonly record struct FocusPiece(FocusPieceKind Kind, Rectangle Before, Rectangle After, int Order = 0);

/// <summary>One band of a piece's picture: rows <see cref="Top"/> to <see cref="Top"/> + <see cref="Height"/>, cut at its seam.</summary>
/// <param name="Quiet">Every row is the same as the one above it - plain, so it can be stretched or squeezed.</param>
/// <param name="SeamStart">Where the band's widest plain run of columns starts (absolute x). Left of it stays with the left edge.</param>
/// <param name="SeamEnd">Where it ends. From here on stays with the right edge. Equal to SeamStart when there is none.</param>
internal readonly record struct ElasticBand(int Top, int Height, bool Quiet, int SeamStart, int SeamEnd);

/// <summary>How a piece's picture is cut: its bands, and which one takes up a change of height (-1 for none).</summary>
internal sealed record ElasticPlan(Rectangle Source, IReadOnlyList<ElasticBand> Bands, int Absorb);

/// <summary>One copy: a part of the picture to a place on the stage, the same size or stretched.</summary>
internal readonly record struct ElasticBlit(Rectangle From, Rectangle To);

/// <summary>
/// DRAWING A PIECE AT ANOTHER SIZE WITHOUT STRETCHING ITS WORDS (25 Nov
/// 2026, the Focus motion - plans/focus-motion-plan.md). Every card on Race
/// Control changes size under Focus - the Session card gains a row, the Next
/// Up bar loses 246 px - and a picture of it scaled to fit would squash its
/// text. Instead what is at a card's left stays with its left edge, what is at
/// its right with its right edge, and only plain space stretches.
///
/// The picture is cut into bands at quiet rows (each the same as the row
/// above). The tallest quiet band takes up any change of height: the bands
/// above it stay with the top, those below with the bottom. In each band the
/// widest run of plain columns (each the same as the one on its left, all the
/// way down the band) is its seam: left of it stays with the left edge, right
/// of it with the right edge, and the seam is stretched - it is plain, so
/// stretching it cannot be seen.
///
/// Pure and over plain pixel arrays, so it is tested without a window.
/// </summary>
internal static class FocusElastic
{
    /// <summary>
    /// The bands of <paramref name="source"/> in a picture <paramref name="width"/>
    /// pixels wide, its pixels row by row in <paramref name="pixels"/>.
    /// </summary>
    public static ElasticPlan Analyse(int[] pixels, int width, Rectangle source, int lineGap = 12)
    {
        var height = pixels.Length / Math.Max(1, width);
        var r = Rectangle.Intersect(source, new Rectangle(0, 0, width, height));

        if (r.Width <= 0 || r.Height <= 0)
        {
            return new ElasticPlan(r, Array.Empty<ElasticBand>(), -1);
        }

        // Quiet rows: the same as the row above, across the piece.
        var quiet = new bool[r.Height];

        for (var y = 1; y < r.Height; y++)
        {
            quiet[y] = RowSame(pixels, width, r.Left, r.Right, r.Top + y);
        }

        var runs = new List<(int Start, int Height, bool Quiet)>();
        var start = 0;

        for (var y = 1; y <= r.Height; y++)
        {
            if (y == r.Height || quiet[y] != quiet[start])
            {
                runs.Add((start, y - start, quiet[start]));
                start = y;
            }
        }

        // One seam for each line of the picture. WHY (25 Nov 2026, found in
        // the frame strip): a button's padding above and below its words is
        // quiet rows, so it was a band of its own and picked its own seam -
        // inside the Reset button, whose flat pink was wider than the gap
        // beside it - and the button tore as the card narrowed. Bands parted
        // only by quiet runs shorter than a line gap are one line, cut at the
        // widest run that is plain all the way down it.
        var bands = new List<ElasticBand>();
        var line = 0;
        bool IsGap(int i) => runs[i].Quiet && runs[i].Height >= lineGap;

        while (line < runs.Count)
        {
            var end = line + 1;

            if (!IsGap(line))
            {
                while (end < runs.Count && !IsGap(end))
                {
                    end++;
                }
            }

            var top = runs[line].Start;
            var bottom = runs[end - 1].Start + runs[end - 1].Height;
            var (seamStart, seamEnd) = WidestSeam(pixels, width, r.Left, r.Right, r.Top + top, bottom - top);

            for (var i = line; i < end; i++)
            {
                bands.Add(new ElasticBand(r.Top + runs[i].Start, runs[i].Height, runs[i].Quiet, seamStart, seamEnd));
            }

            line = end;
        }

        // The tallest quiet band, not the first or the last row of the piece
        // (its border), takes up a change of height.
        var absorb = -1;

        for (var i = 0; i < bands.Count; i++)
        {
            if (bands[i].Quiet && (absorb < 0 || bands[i].Height > bands[absorb].Height))
            {
                absorb = i;
            }
        }

        return new ElasticPlan(r, bands, absorb);
    }

    /// <summary>
    /// The copies that draw <paramref name="plan"/>'s piece into
    /// <paramref name="to"/>: each band's left and right parts at their
    /// edges, the same size, and its seam stretched between them. Copies may
    /// reach outside <paramref name="to"/>; the caller clips to it.
    /// </summary>
    public static List<ElasticBlit> Map(ElasticPlan plan, Rectangle to)
    {
        var blits = new List<ElasticBlit>();
        var src = plan.Source;

        if (plan.Bands.Count == 0 || to.Width <= 0 || to.Height <= 0)
        {
            return blits;
        }

        // The height the absorbing band is given; with none, everything stays with the top.
        var absorbHeight = plan.Absorb >= 0
            ? Math.Max(0, to.Height - (src.Height - plan.Bands[plan.Absorb].Height))
            : 0;

        var y = to.Top;

        for (var i = 0; i < plan.Bands.Count; i++)
        {
            var band = plan.Bands[i];
            int top, height;

            if (plan.Absorb < 0 || i < plan.Absorb)
            {
                top = to.Top + (band.Top - src.Top);
                height = band.Height;
            }
            else if (i == plan.Absorb)
            {
                top = y;
                height = absorbHeight;
            }
            else
            {
                // Below the absorbing band: with the bottom edge.
                top = to.Bottom - (src.Bottom - band.Top);
                height = band.Height;
            }

            y = top + height;

            if (height <= 0)
            {
                continue;
            }

            var leftWidth = band.SeamStart - src.Left;
            var rightWidth = src.Right - band.SeamEnd;
            var hasSeam = band.SeamEnd > band.SeamStart;

            if (!hasSeam)
            {
                // No plain run: it all stays with the left edge.
                leftWidth = src.Width;
                rightWidth = 0;
            }

            if (leftWidth > 0)
            {
                blits.Add(new ElasticBlit(new Rectangle(src.Left, band.Top, leftWidth, band.Height), new Rectangle(to.Left, top, leftWidth, height)));
            }

            if (hasSeam)
            {
                var gapLeft = to.Left + leftWidth;
                var gapRight = to.Right - rightWidth;

                if (gapRight > gapLeft)
                {
                    // At its own size, the seam itself; otherwise one of its
                    // columns, stretched - it is plain, so either looks the same.
                    var seamWidth = band.SeamEnd - band.SeamStart;
                    var from = gapRight - gapLeft == seamWidth && height == band.Height
                        ? new Rectangle(band.SeamStart, band.Top, seamWidth, band.Height)
                        : new Rectangle(band.SeamStart, band.Top, 1, band.Height);
                    blits.Add(new ElasticBlit(from, new Rectangle(gapLeft, top, gapRight - gapLeft, height)));
                }
            }

            if (rightWidth > 0)
            {
                blits.Add(new ElasticBlit(new Rectangle(band.SeamEnd, band.Top, rightWidth, band.Height), new Rectangle(to.Right - rightWidth, top, rightWidth, height)));
            }
        }

        return blits;
    }

    private static bool RowSame(int[] pixels, int width, int left, int right, int y)
    {
        var row = y * width;
        var above = (y - 1) * width;

        for (var x = left; x < right; x++)
        {
            if (pixels[row + x] != pixels[above + x])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The widest run of columns in [left, right) each the same as the one on its left, over rows top to top + height.</summary>
    private static (int Start, int End) WidestSeam(int[] pixels, int width, int left, int right, int top, int height)
    {
        int bestStart = right, bestEnd = right, runStart = -1;

        for (var x = left + 1; x <= right; x++)
        {
            var plain = x < right && ColumnSameAsLeft(pixels, width, x, top, height);

            if (plain && runStart < 0)
            {
                runStart = x;
            }
            else if (!plain && runStart >= 0)
            {
                if (x - runStart > bestEnd - bestStart)
                {
                    bestStart = runStart;
                    bestEnd = x;
                }

                runStart = -1;
            }
        }

        return (bestStart, bestEnd);
    }

    private static bool ColumnSameAsLeft(int[] pixels, int width, int x, int top, int height)
    {
        for (var y = top; y < top + height; y++)
        {
            var i = y * width + x;

            if (pixels[i] != pixels[i - 1])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The page's pieces from where each one was (<paramref name="before"/>)
    /// and, once the view has changed, where it is now
    /// (<paramref name="after"/>; null before then). The same object is the
    /// same piece on both sides. One on both sides that does not fold is a
    /// card, and glides; one that folds, or is on one side only, fades out (or
    /// back in) where it is, one after another from the top.
    /// </summary>
    public static List<FocusPiece> Pieces(IReadOnlyList<(object Key, bool Folds, Rectangle At)> before, IReadOnlyList<(object Key, bool Folds, Rectangle At)>? after)
    {
        var keys = new List<object>();
        var was = new Dictionary<object, (bool Folds, Rectangle At)>(ReferenceEqualityComparer.Instance);
        var now = new Dictionary<object, (bool Folds, Rectangle At)>(ReferenceEqualityComparer.Instance);

        foreach (var (key, folds, at) in before)
        {
            keys.Add(key);
            was[key] = (folds, at);
        }

        foreach (var (key, folds, at) in after ?? Array.Empty<(object, bool, Rectangle)>())
        {
            if (!was.ContainsKey(key))
            {
                keys.Add(key);
            }

            now[key] = (folds, at);
        }

        var cards = new List<FocusPiece>();
        var folding = new List<(Rectangle Before, Rectangle After)>();

        foreach (var key in keys)
        {
            var b = was.TryGetValue(key, out var w) ? w.At : Rectangle.Empty;
            var a = now.TryGetValue(key, out var n) ? n.At : Rectangle.Empty;
            var folds = w.Folds || n.Folds;

            if (b.IsEmpty && a.IsEmpty)
            {
                continue;
            }

            if (!folds && !b.IsEmpty && (after is null || !a.IsEmpty))
            {
                cards.Add(new FocusPiece(FocusPieceKind.Card, b, a));
            }
            else
            {
                folding.Add((b, a));
            }
        }

        // Those going and those coming each one after another from the top -
        // counted apart, so the first to go does not wait for one coming.
        int going = 0, coming = 0;

        foreach (var (b, a) in folding.OrderBy(f => (f.Before.IsEmpty ? f.After : f.Before).Top))
        {
            cards.Add(new FocusPiece(FocusPieceKind.Folding, b, a, b.IsEmpty ? coming++ : going++));
        }

        return cards;
    }

    /// <summary>
    /// Where a card is at <paramref name="glide"/> (0 to 1), with the sidebar
    /// moved <paramref name="shift"/> pixels so far of the
    /// <paramref name="change"/> it makes in all. Its left edge keeps to the
    /// sidebar's edge: where it is against the sidebar goes from before to
    /// after with the glide, and the sidebar carries it - so neither runs
    /// ahead of the other, however their curves differ. Its right edge, top
    /// and bottom glide from where they were (riding with the sidebar) to
    /// where they go. Before there is a place after, it only rides.
    ///
    /// WHY (25 Nov 2026, found in the frame strip): with the card's whole
    /// rectangle gliding, Restore's cards ran ahead of the sidebar as it
    /// opened and a strip of bare ground showed between them.
    /// </summary>
    public static Rectangle CardAt(Rectangle before, Rectangle after, double glide, int shift, int change)
    {
        if (after.IsEmpty || glide <= 0)
        {
            return new Rectangle(before.X + shift, before.Y, before.Width, before.Height);
        }

        static int L(double a, double b, double t) => (int)Math.Round(a + (b - a) * t);

        var left = L(before.Left, after.Left - change, glide) + shift;
        var right = L(before.Right + shift, after.Right, glide);

        return Rectangle.FromLTRB(left, L(before.Top, after.Top, glide), Math.Max(left, right), L(before.Bottom, after.Bottom, glide));
    }
}
