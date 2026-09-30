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

        var height = headerHeight + results.Count * rowHeight + footerHeight + badgeLines * 21;

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

        using (var titleBrush = new SolidBrush(textColor))
        using (var subtitleBrush = new SolidBrush(mutedColor))
        {
            g.DrawString(title, titleFont, titleBrush, new PointF(32, 28));
            g.DrawString(subtitle, subtitleFont, subtitleBrush, new PointF(32, 72));
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
                2 => ColorTranslator.FromHtml("#C7CCD6"),
                3 => ColorTranslator.FromHtml("#C98A4B"),
                _ => textColor,
            };

            using (var posBrush = new SolidBrush(positionColor))
            {
                g.DrawString($"{result.Position}", driverFont, posBrush, new PointF(32, y + 14));
            }

            using (var nameBrush = new SolidBrush(textColor))
            {
                var displayName = string.IsNullOrWhiteSpace(result.DriverName) ? result.Transponder : result.DriverName;
                g.DrawString(displayName, driverFont, nameBrush, new PointF(110, y + 14));

                if (!string.IsNullOrWhiteSpace(result.CarModel))
                {
                    using var carBrush = new SolidBrush(mutedColor);
                    g.DrawString(result.CarModel, cellFont, carBrush, new PointF(110, y + 36));
                }
            }

            using (var cellBrush = new SolidBrush(textColor))
            {
                g.DrawString(result.Laps.ToString(), cellFont, cellBrush, new PointF(width - 260, y + 18));

                var fastLapText = result.FastLapMs > 0 ? $"{result.FastLapMs / 1000.0:0.000}s" : "-";
                g.DrawString(fastLapText, cellFont, cellBrush, new PointF(width - 170, y + 18));
            }

            y += rowHeight;
        }

        using (var footerBrush = new SolidBrush(mutedColor))
        using (var linePen = new Pen(rowAltColor))
        {
            g.DrawLine(linePen, 32, y + 8, width - 32, y + 8);

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

            g.DrawString("HeatCaller", footerFont, footerBrush, new PointF(32, y + 20));
        }

        using (var accentPen = new Pen(accentColor, 4))
        {
            g.DrawLine(accentPen, 0, 0, width, 0);
        }

        return bitmap;
    }
}
