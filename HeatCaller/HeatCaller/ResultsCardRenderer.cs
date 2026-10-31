namespace HeatCaller;

/// <summary>
/// Draws a clean, shareable summary image of a session's final results -
/// title/date/track and a ranked results table - suitable for pasting
/// straight into a club group chat or social post without needing any
/// other software. Pure GDI+ drawing onto a Bitmap, no external image
/// generation involved.
/// </summary>
public static class ResultsCardRenderer
{
    /// <summary>Badges printed on a card before it just becomes a list.</summary>
    private const int MaxCardBadges = 6;

    // The card's own dark scheme, used when no palette is supplied.
    private static readonly ChartPalette DefaultPalette = new()
    {
        Background = ColorTranslator.FromHtml("#0B0E14"),
        Text = Color.White,
        Muted = ColorTranslator.FromHtml("#8A93A6"),
        Grid = ColorTranslator.FromHtml("#161B26"),
        Axis = ColorTranslator.FromHtml("#2A3040"),
        Highlight = ColorTranslator.FromHtml("#FFC94C"),
        Series = new[] { ColorTranslator.FromHtml("#4C8DFF") },
    };

    /// <summary>Renders up to the top <paramref name="maxRows"/> results as a PNG-ready Bitmap. Caller is responsible for disposing it.</summary>
    public static Bitmap Render(SessionRecord session, ChartPalette? palette = null, int maxRows = 8)
        => Render(session, null, palette, maxRows);

    /// <param name="unlocks">
    /// Badges this race earned, printed under the result. Null or empty
    /// simply leaves the card as it was - a race where nobody unlocked
    /// anything shouldn't grow an empty heading.
    /// </param>
    public static Bitmap Render(
        SessionRecord session,
        IReadOnlyList<AchievementUnlock>? unlocks,
        ChartPalette? palette = null,
        int maxRows = 8)
    {
        // Reusing ChartPalette rather than inventing a second palette type
        // means the chart export dialog - with its print-friendly and
        // monochrome styles - works for results cards unchanged.
        var p = palette ?? DefaultPalette;
        var canvasColor = p.Background;
        var accentColor = p.Series[0];
        var highlightColor = p.Highlight;
        var rowAltColor = p.Grid;
        var mutedColor = p.Muted;
        var textColor = p.Text;

        const int width = 900;
        var results = session.Results.OrderBy(r => r.Position).Take(maxRows).ToList();
        var headerHeight = 150;
        var rowHeight = 56;
        var footerHeight = 50;

        // The badge block is measured into the canvas BEFORE it's drawn.
        // Drawing past the bottom of a bitmap doesn't fail, it just silently
        // vanishes - so the card has to be made tall enough first.
        var badgeLines = unlocks is { Count: > 0 }
            ? 1 + Math.Min(unlocks.Count, MaxCardBadges) + (unlocks.Count > MaxCardBadges ? 1 : 0)
            : 0;

        // Past maxRows the rest of the field was dropped without a word, and
        // the card still looked like the whole result - and a disqualified
        // driver, whom Edit Result puts last, is exactly who went. So the
        // card says how many it leaves out, and names any DNF or DQ among them.
        var hidden = session.Results.OrderBy(r => r.Position).Skip(results.Count).ToList();
        var hiddenLine = hidden.Count == 0
            ? null
            : $"+{hidden.Count} more not shown"
              + (OutcomeCounts(hidden) is { Length: > 0 } outcomes ? $", including {outcomes}" : string.Empty);

        var height = headerHeight + results.Count * rowHeight + footerHeight + badgeLines * 21 + (hiddenLine is null ? 0 : 21);

        var bitmap = new Bitmap(width, height);

        // 96 DPI, not the screen's: under --dpi-aware at 150% the point-size text would draw half as big again.
        bitmap.SetResolution(96, 96);

        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var bg = new SolidBrush(canvasColor))
        {
            g.FillRectangle(bg, 0, 0, width, height);
        }

        var family = AppTheme.BoldFont.FontFamily;
        using var titleFont = new Font(family, 26F, FontStyle.Bold);
        using var subtitleFont = new Font(family, 13F);
        using var headerFont = new Font(family, 13F, FontStyle.Bold);
        using var driverFont = new Font(family, 16F, FontStyle.Bold);
        using var cellFont = new Font(family, 14F);
        using var footerFont = new Font(AppTheme.SmallFont.FontFamily, 9F);

        var trackPart = string.IsNullOrWhiteSpace(session.TrackLayoutName) ? "" : $" @ {session.TrackLayoutName}";
        var labelPart = string.IsNullOrWhiteSpace(session.Label) ? "" : $" — {session.Label}";
        var title = $"{session.SessionType}{labelPart}";
        var subtitle = $"{session.StartedAtUtc.ToLocalTime():dddd, MMMM d, yyyy}{trackPart}";

        // A long title - the class's whole name, and the round's - came off
        // the card's right-hand edge (layout pictures, 30 Sep). So it is made
        // smaller until it fits, down to 18pt, and ended with "..." past
        // that; the date and track line, and the names in the table, end
        // with "..." where they would run on.
        using var oneLine = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter };
        var across = width - 64f;

        using (var titleBrush = new SolidBrush(textColor))
        using (var subtitleBrush = new SolidBrush(mutedColor))
        using (var fitted = FitTitle(g, title, family, across))
        {
            // Kept on the line a 26pt title sits on, whatever size it is.
            var drop = (titleFont.GetHeight(g) - fitted.GetHeight(g)) / 2f;

            g.DrawString(title, fitted, titleBrush, new RectangleF(32, 28 + drop, across, fitted.GetHeight(g) + 2), oneLine);
            g.DrawString(subtitle, subtitleFont, subtitleBrush, new RectangleF(32, 72, across, subtitleFont.GetHeight(g) + 2), oneLine);
        }

        // Column header row.
        var headerY = headerHeight - 34;
        using (var headerBrush = new SolidBrush(mutedColor))
        {
            g.DrawString("POS", headerFont, headerBrush, new PointF(32, headerY));
            g.DrawString("DRIVER", headerFont, headerBrush, new PointF(110, headerY));
            g.DrawString("LAPS", headerFont, headerBrush, new PointF(width - 260, headerY));
            g.DrawString("FASTEST LAP", headerFont, headerBrush, new PointF(width - 170, headerY));
        }

        var y = headerHeight;
        foreach (var result in results)
        {
            if (result.Position % 2 == 0)
            {
                using var rowBrush = new SolidBrush(rowAltColor);
                g.FillRectangle(rowBrush, 0, y, width, rowHeight);
            }

            var positionColor = result.Position switch
            {
                1 => highlightColor,
                2 => p.Second,
                3 => p.Third,
                _ => textColor,
            };

            using (var posBrush = new SolidBrush(positionColor))
            {
                g.DrawString($"{result.Position}", driverFont, posBrush, new PointF(32, y + 14));
            }

            using (var nameBrush = new SolidBrush(textColor))
            {
                // Up to the laps column, and no further.
                var nameAcross = width - 260 - 110 - 16f;

                var displayName = string.IsNullOrWhiteSpace(result.DriverName) ? result.Transponder : result.DriverName;
                g.DrawString(displayName, driverFont, nameBrush, new RectangleF(110, y + 14, nameAcross, driverFont.GetHeight(g) + 2), oneLine);

                if (!string.IsNullOrWhiteSpace(result.CarModel))
                {
                    using var carBrush = new SolidBrush(mutedColor);
                    g.DrawString(result.CarModel, cellFont, carBrush, new RectangleF(110, y + 36, nameAcross, cellFont.GetHeight(g) + 2), oneLine);
                }
            }

            using (var cellBrush = new SolidBrush(textColor))
            {
                // The DNF or DQ from Edit Result, where the laps go. Left out,
                // a DQ was drawn as "0" laps beside a real fastest lap and a
                // DNF as an ordinary finisher - on the image that is posted
                // to the club chat as the night's result.
                var outcome = ResultEditor.NormaliseOutcome(result.Outcome) ?? string.Empty;
                var lapsText = outcome == ResultEditor.Dq ? outcome
                    : outcome.Length > 0 ? $"{result.Laps} {outcome}"
                    : result.Laps.ToString();

                g.DrawString(lapsText, cellFont, cellBrush, new PointF(width - 260, y + 18));

                var fastLapText = result.FastLapMs > 0 ? $"{result.FastLapMs / 1000.0:0.000}s" : "-";
                g.DrawString(fastLapText, cellFont, cellBrush, new PointF(width - 170, y + 18));
            }

            y += rowHeight;
        }

        using (var footerBrush = new SolidBrush(mutedColor))
        using (var linePen = new Pen(rowAltColor))
        {
            g.DrawLine(linePen, 32, y + 8, width - 32, y + 8);

            if (hiddenLine is not null)
            {
                g.DrawString(hiddenLine, footerFont, footerBrush, new PointF(32, y + 20));
                y += 21;
            }

            // Badges under the result, two to a line, capped so a big night
            // can't push the card off its own canvas.
            if (unlocks is { Count: > 0 })
            {
                var badgeY = y + 20;

                using (var badgeBrush = new SolidBrush(highlightColor))
                {
                    g.DrawString("UNLOCKED", footerFont, badgeBrush, new PointF(32, badgeY));
                }

                badgeY += 22;

                foreach (var line in unlocks
                             .Take(MaxCardBadges)
                             .Select(u => $"{u.Badge.Icon}  {u.DriverName} - {u.Badge.Name}"))
                {
                    g.DrawString(line, footerFont, footerBrush, new PointF(32, badgeY));
                    badgeY += 20;
                }

                if (unlocks.Count > MaxCardBadges)
                {
                    g.DrawString($"+{unlocks.Count - MaxCardBadges} more", footerFont, footerBrush, new PointF(32, badgeY));
                    badgeY += 20;
                }

                y = badgeY - 20;
            }

            // The relay's share cards' line beside HeatCaller's full-colour
            // logo (31 Oct 2026), so a card from the app and one from the
            // club's pages sign off the same way. The logo carries its own
            // dark square, so it reads on every card style.
            var footLine = footerFont.GetHeight(g);
            var logo = footLine * 1.5f;
            HeatCallerMark.DrawLogo(g, new RectangleF(32, y + 20 + (footLine - logo) / 2f, logo, logo));
            g.DrawString(HeatCallerMark.Credit, footerFont, footerBrush, new PointF(32 + logo + 8, y + 20));
        }

        using (var accentPen = new Pen(accentColor, 4))
        {
            g.DrawLine(accentPen, 0, 0, width, 0);
        }

        return bitmap;
    }

    /// <summary>"1 DQ", "2 DNF and 1 DQ" - the outcomes among these results, or empty when there are none.</summary>
    private static string OutcomeCounts(IEnumerable<SessionResultEntry> results)
    {
        var outcomes = results.Select(r => ResultEditor.NormaliseOutcome(r.Outcome)).ToList();
        var parts = new[] { ResultEditor.Dnf, ResultEditor.Dq }
            .Select(o => (Outcome: o, Count: outcomes.Count(x => x == o)))
            .Where(x => x.Count > 0)
            .Select(x => $"{x.Count} {x.Outcome}");

        return string.Join(" and ", parts);
    }

    /// <summary>
    /// The title's face: 26pt bold, or smaller - a point at a time, down to
    /// 18pt - until the title fits <paramref name="across"/>. The caller
    /// disposes it.
    /// </summary>
    private static Font FitTitle(Graphics g, string title, FontFamily family, float across)
    {
        for (var size = 26f; size > 18f; size -= 1f)
        {
            var font = new Font(family, size, FontStyle.Bold);

            if (g.MeasureString(title, font).Width <= across)
            {
                return font;
            }

            font.Dispose();
        }

        return new Font(family, 18f, FontStyle.Bold);
    }
}
