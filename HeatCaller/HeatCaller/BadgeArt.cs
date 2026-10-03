using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// The badges, drawn rather than typed.
///
/// They used to be single Unicode glyphs in a list column - a stopwatch
/// character, a lightning bolt - which meant every badge was the same size
/// as body text, in whatever the font happened to render, and two of them
/// showed up as an empty box on machines without the symbol font. A badge
/// nobody can see is not a reward.
///
/// So each one is a medallion here: a tier-coloured disc with a rim, and an
/// emblem built out of lines, arcs and polygons on top of it. No image
/// files, no icon font, nothing to ship or go missing, and it stays sharp at
/// any size the page decides to draw it.
///
/// Locked badges are drawn through the same path with a flat grey disc, so
/// the shape is still readable - the point of showing what you have not
/// earned is that you can see what it is.
/// </summary>
public static class BadgeArt
{
    /// <summary>
    /// Metal colours, deliberately NOT from the theme palette. A bronze
    /// badge should look like bronze in every theme - tying it to the
    /// accent colour would make gold and silver mean nothing the moment
    /// somebody switches to Synthwave.
    /// </summary>
    public static (Color Dark, Color Light) TierColours(AchievementTier tier) => tier switch
    {
        AchievementTier.Silver => (ColorTranslator.FromHtml("#77828F"), ColorTranslator.FromHtml("#C6CED8")),
        AchievementTier.Gold => (ColorTranslator.FromHtml("#C08A16"), ColorTranslator.FromHtml("#F5D573")),
        AchievementTier.Legend => (ColorTranslator.FromHtml("#6A3FC4"), ColorTranslator.FromHtml("#B79BF7")),
        _ => (ColorTranslator.FromHtml("#A85F26"), ColorTranslator.FromHtml("#E8A567")),
    };

    /// <summary>The single colour to write a tier's name in, on a card.</summary>
    public static Color TierInk(AchievementTier tier) => TierColours(tier).Dark;

    public static string TierName(AchievementTier tier) => tier switch
    {
        AchievementTier.Silver => "Silver",
        AchievementTier.Gold => "Gold",
        AchievementTier.Legend => "Legend",
        _ => "Bronze",
    };

    /// <summary>What colour a category reads as, so the groups are told apart at a glance.</summary>
    public static Color CategoryColour(AchievementCategory category) => category switch
    {
        AchievementCategory.Milestone => AppTheme.Warning,
        AchievementCategory.Progress => AppTheme.Success,
        AchievementCategory.Rivalry => AppTheme.Accent,
        _ => AppTheme.Primary,
    };

    public static string CategoryName(AchievementCategory category) => category switch
    {
        AchievementCategory.Milestone => "Career",
        AchievementCategory.Progress => "Progress",
        AchievementCategory.Rivalry => "Against others",
        _ => "In a race",
    };

    /// <summary>
    /// The whole medallion: rim, face, emblem, and - on an earned one - a
    /// soft halo so the earned ones carry across a page of locked ones
    /// without having to read anything.
    /// </summary>
    public static void DrawMedallion(Graphics g, RectangleF box, AchievementDefinition badge, bool earned)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var (dark, light) = TierColours(badge.Tier);

        if (!earned)
        {
            // Still a medallion, just a cold one. Drawing locked badges as
            // empty outlines made the page look broken rather than
            // unfinished.
            dark = AppTheme.Border;
            light = AppTheme.RowAlt;
        }

        if (earned)
        {
            using var halo = new SolidBrush(Color.FromArgb(38, light));
            g.FillEllipse(halo, RectangleF.Inflate(box, 4f, 4f));
        }

        // Lit from the top-left, like every other raised thing in the app.
        using (var face = new LinearGradientBrush(box, light, dark, LinearGradientMode.ForwardDiagonal))
        {
            g.FillEllipse(face, box);
        }

        using (var rim = new Pen(earned ? Color.FromArgb(150, Darken(dark, 0.35f)) : AppTheme.Border, Math.Max(1f, box.Width / 22f)))
        {
            g.DrawEllipse(rim, RectangleF.Inflate(box, -0.5f, -0.5f));
        }

        // An inner hairline, which is what makes it read as struck metal
        // rather than as a coloured circle.
        using (var inner = new Pen(Color.FromArgb(earned ? 90 : 40, Color.White), 1f))
        {
            g.DrawEllipse(inner, RectangleF.Inflate(box, -box.Width * 0.11f, -box.Height * 0.11f));
        }

        var emblemBox = RectangleF.Inflate(box, -box.Width * 0.27f, -box.Height * 0.27f);
        var ink = earned ? Color.FromArgb(240, Color.White) : AppTheme.TextMuted;

        DrawEmblem(g, emblemBox, badge.Art, ink);

        if (!earned)
        {
            DrawLock(g, new RectangleF(box.Right - box.Width * 0.34f, box.Bottom - box.Height * 0.34f, box.Width * 0.34f, box.Height * 0.34f));
        }
    }

    /// <summary>The little padlock on a locked badge's corner.</summary>
    private static void DrawLock(Graphics g, RectangleF box)
    {
        using var disc = new SolidBrush(AppTheme.Surface);
        using var edge = new Pen(AppTheme.Border, 1f);
        using var ink = new Pen(AppTheme.TextMuted, Math.Max(1.2f, box.Width / 9f));

        g.FillEllipse(disc, box);
        g.DrawEllipse(edge, RectangleF.Inflate(box, -0.5f, -0.5f));

        var body = new RectangleF(box.Left + box.Width * 0.28f, box.Top + box.Height * 0.46f, box.Width * 0.44f, box.Height * 0.3f);

        using var bodyBrush = new SolidBrush(AppTheme.TextMuted);
        using var bodyPath = AppTheme.RoundedRect(body, 2);
        g.FillPath(bodyBrush, bodyPath);

        // The shackle - an arc rather than a half-circle, so it reads as a
        // padlock at twelve pixels across.
        g.DrawArc(ink,
            box.Left + box.Width * 0.36f, box.Top + box.Height * 0.26f,
            box.Width * 0.28f, box.Height * 0.32f,
            180, 180);
    }

    /// <summary>
    /// One emblem, drawn inside the given box in one colour.
    ///
    /// Every shape is built from primitives at proportions of the box, so
    /// the same emblem works at 20px on a leaderboard row and at 80px on
    /// the badge page. An unknown name falls back to a star rather than
    /// drawing nothing, which is how a new badge with a typo in it would
    /// otherwise ship as an empty disc.
    /// </summary>
    public static void DrawEmblem(Graphics g, RectangleF b, string art, Color ink)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var stroke = Math.Max(1.3f, b.Width / 11f);

        using var pen = new Pen(ink, stroke)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        using var brush = new SolidBrush(ink);

        var cx = b.Left + b.Width / 2f;
        var cy = b.Top + b.Height / 2f;
        var r = Math.Min(b.Width, b.Height) / 2f;

        switch (art)
        {
            case "stopwatch":
            {
                var face = new RectangleF(cx - r * 0.82f, cy - r * 0.72f, r * 1.64f, r * 1.64f);
                g.DrawEllipse(pen, face);
                g.DrawLine(pen, cx - r * 0.26f, b.Top, cx + r * 0.26f, b.Top);
                g.DrawLine(pen, cx, b.Top, cx, face.Top);
                g.DrawLine(pen, cx, cy + r * 0.1f, cx + r * 0.45f, cy - r * 0.28f);
                break;
            }

            case "gear":
            {
                g.DrawEllipse(pen, cx - r * 0.5f, cy - r * 0.5f, r, r);

                for (var i = 0; i < 8; i++)
                {
                    var angle = i * Math.PI / 4;
                    var x1 = cx + (float)Math.Cos(angle) * r * 0.68f;
                    var y1 = cy + (float)Math.Sin(angle) * r * 0.68f;
                    var x2 = cx + (float)Math.Cos(angle) * r;
                    var y2 = cy + (float)Math.Sin(angle) * r;
                    g.DrawLine(pen, x1, y1, x2, y2);
                }

                break;
            }

            case "bolt":
            {
                var points = new[]
                {
                    new PointF(cx + r * 0.32f, b.Top),
                    new PointF(cx - r * 0.55f, cy + r * 0.12f),
                    new PointF(cx - r * 0.02f, cy + r * 0.12f),
                    new PointF(cx - r * 0.32f, b.Bottom),
                    new PointF(cx + r * 0.58f, cy - r * 0.18f),
                    new PointF(cx + r * 0.04f, cy - r * 0.18f),
                };

                g.FillPolygon(brush, points);
                break;
            }

            case "sparkle":
            {
                DrawFourPointStar(g, brush, cx, cy, r, r * 0.26f);
                DrawFourPointStar(g, brush, b.Left + r * 0.28f, b.Bottom - r * 0.22f, r * 0.42f, r * 0.12f);
                break;
            }

            case "crown":
            {
                var points = new[]
                {
                    new PointF(cx - r * 0.9f, cy + r * 0.45f),
                    new PointF(cx - r * 0.72f, cy - r * 0.6f),
                    new PointF(cx - r * 0.28f, cy + r * 0.02f),
                    new PointF(cx, cy - r * 0.78f),
                    new PointF(cx + r * 0.28f, cy + r * 0.02f),
                    new PointF(cx + r * 0.72f, cy - r * 0.6f),
                    new PointF(cx + r * 0.9f, cy + r * 0.45f),
                };

                g.FillPolygon(brush, points);
                g.FillRectangle(brush, cx - r * 0.9f, cy + r * 0.52f, r * 1.8f, r * 0.28f);
                break;
            }

            case "arrow-up":
            {
                g.DrawLine(pen, cx, b.Bottom, cx, b.Top + r * 0.18f);
                g.DrawLines(pen, new[]
                {
                    new PointF(cx - r * 0.55f, b.Top + r * 0.72f),
                    new PointF(cx, b.Top + r * 0.14f),
                    new PointF(cx + r * 0.55f, b.Top + r * 0.72f),
                });
                break;
            }

            case "chevron-up-double":
            {
                for (var i = 0; i < 2; i++)
                {
                    var y = cy + r * 0.62f - i * r * 0.78f;
                    g.DrawLines(pen, new[]
                    {
                        new PointF(cx - r * 0.7f, y),
                        new PointF(cx, y - r * 0.55f),
                        new PointF(cx + r * 0.7f, y),
                    });
                }

                break;
            }

            case "checkered-flag":
            {
                g.DrawLine(pen, b.Left + r * 0.22f, b.Top, b.Left + r * 0.22f, b.Bottom);

                var cell = r * 0.42f;
                var originX = b.Left + r * 0.28f;
                var originY = b.Top + r * 0.12f;

                for (var row = 0; row < 3; row++)
                {
                    for (var col = 0; col < 3; col++)
                    {
                        if ((row + col) % 2 != 0)
                        {
                            continue;
                        }

                        g.FillRectangle(brush, originX + col * cell, originY + row * cell, cell, cell);
                    }
                }

                using var outline = new Pen(ink, Math.Max(1f, stroke * 0.6f));
                g.DrawRectangle(outline, originX, originY, cell * 3, cell * 3);
                break;
            }

            case "star":
            {
                DrawStar(g, brush, cx, cy, r, r * 0.45f, 5);
                break;
            }

            case "infinity":
            {
                var d = r * 0.86f;
                g.DrawEllipse(pen, cx - d, cy - d / 2f, d, d);
                g.DrawEllipse(pen, cx, cy - d / 2f, d, d);
                break;
            }

            case "trophy":
            {
                var cup = new[]
                {
                    new PointF(cx - r * 0.62f, cy - r * 0.78f),
                    new PointF(cx + r * 0.62f, cy - r * 0.78f),
                    new PointF(cx + r * 0.42f, cy + r * 0.12f),
                    new PointF(cx - r * 0.42f, cy + r * 0.12f),
                };

                g.FillPolygon(brush, cup);
                g.DrawArc(pen, cx + r * 0.5f, cy - r * 0.72f, r * 0.62f, r * 0.62f, 270, 180);
                g.DrawArc(pen, cx - r * 1.12f, cy - r * 0.72f, r * 0.62f, r * 0.62f, 90, 180);
                g.FillRectangle(brush, cx - r * 0.14f, cy + r * 0.12f, r * 0.28f, r * 0.5f);
                g.FillRectangle(brush, cx - r * 0.6f, cy + r * 0.62f, r * 1.2f, r * 0.26f);
                break;
            }

            case "medal":
            {
                g.DrawLines(pen, new[]
                {
                    new PointF(cx - r * 0.55f, b.Top),
                    new PointF(cx - r * 0.18f, cy - r * 0.1f),
                });
                g.DrawLines(pen, new[]
                {
                    new PointF(cx + r * 0.55f, b.Top),
                    new PointF(cx + r * 0.18f, cy - r * 0.1f),
                });

                var disc = new RectangleF(cx - r * 0.62f, cy - r * 0.16f, r * 1.24f, r * 1.24f);
                g.DrawEllipse(pen, disc);
                DrawStar(g, brush, disc.Left + disc.Width / 2f, disc.Top + disc.Height / 2f, r * 0.42f, r * 0.19f, 5);
                break;
            }

            case "disc":
            {
                for (var i = 3; i >= 1; i--)
                {
                    var rr = r * (i / 3f);
                    g.DrawEllipse(pen, cx - rr, cy - rr, rr * 2, rr * 2);
                }

                g.FillEllipse(brush, cx - r * 0.12f, cy - r * 0.12f, r * 0.24f, r * 0.24f);
                break;
            }

            case "hourglass":
            {
                g.DrawLine(pen, cx - r * 0.7f, b.Top, cx + r * 0.7f, b.Top);
                g.DrawLine(pen, cx - r * 0.7f, b.Bottom, cx + r * 0.7f, b.Bottom);

                g.FillPolygon(brush, new[]
                {
                    new PointF(cx - r * 0.6f, b.Top + stroke * 0.4f),
                    new PointF(cx + r * 0.6f, b.Top + stroke * 0.4f),
                    new PointF(cx, cy),
                });

                g.FillPolygon(brush, new[]
                {
                    new PointF(cx - r * 0.6f, b.Bottom - stroke * 0.4f),
                    new PointF(cx + r * 0.6f, b.Bottom - stroke * 0.4f),
                    new PointF(cx, cy),
                });

                break;
            }

            case "camera":
            {
                var body = new RectangleF(b.Left, cy - r * 0.62f, b.Width, r * 1.24f);

                using (var path = AppTheme.RoundedRect(body, (int)Math.Max(2, r * 0.22f)))
                {
                    g.DrawPath(pen, path);
                }

                g.FillRectangle(brush, cx - r * 0.26f, body.Top - r * 0.22f, r * 0.52f, r * 0.24f);
                g.DrawEllipse(pen, cx - r * 0.34f, cy - r * 0.34f, r * 0.68f, r * 0.68f);
                break;
            }

            case "chart-up":
            {
                var barWidth = r * 0.42f;

                for (var i = 0; i < 3; i++)
                {
                    var height = r * (0.5f + i * 0.5f);
                    g.FillRectangle(brush, b.Left + i * (barWidth + r * 0.24f), b.Bottom - height, barWidth, height);
                }

                g.DrawLines(pen, new[]
                {
                    new PointF(cx + r * 0.16f, b.Top + r * 0.44f),
                    new PointF(b.Right, b.Top + r * 0.08f),
                });
                break;
            }

            case "shield":
            {
                using var path = new GraphicsPath();
                path.AddLines(new[]
                {
                    new PointF(cx, b.Top),
                    new PointF(b.Right, b.Top + r * 0.34f),
                    new PointF(b.Right - r * 0.12f, cy + r * 0.32f),
                    new PointF(cx, b.Bottom),
                    new PointF(b.Left + r * 0.12f, cy + r * 0.32f),
                    new PointF(b.Left, b.Top + r * 0.34f),
                });
                path.CloseFigure();

                g.FillPath(brush, path);
                break;
            }

            case "route":
            {
                using var path = new GraphicsPath();
                path.AddBezier(
                    b.Left, b.Bottom - r * 0.2f,
                    cx - r * 0.2f, b.Bottom - r * 0.1f,
                    cx - r * 0.9f, b.Top + r * 0.5f,
                    cx, cy - r * 0.1f);
                path.AddBezier(
                    cx, cy - r * 0.1f,
                    cx + r * 0.9f, cy + r * 0.3f,
                    cx + r * 0.2f, b.Top + r * 0.1f,
                    b.Right, b.Top + r * 0.22f);

                g.DrawPath(pen, path);
                g.FillEllipse(brush, b.Left - r * 0.16f, b.Bottom - r * 0.36f, r * 0.34f, r * 0.34f);
                g.FillEllipse(brush, b.Right - r * 0.18f, b.Top + r * 0.06f, r * 0.34f, r * 0.34f);
                break;
            }

            case "people":
            {
                g.FillEllipse(brush, cx - r * 0.82f, cy - r * 0.68f, r * 0.62f, r * 0.62f);
                g.FillEllipse(brush, cx + r * 0.2f, cy - r * 0.68f, r * 0.62f, r * 0.62f);

                g.DrawArc(pen, cx - r * 0.98f, cy + r * 0.02f, r * 0.94f, r * 0.94f, 200, 140);
                g.DrawArc(pen, cx + r * 0.04f, cy + r * 0.02f, r * 0.94f, r * 0.94f, 200, 140);
                break;
            }

            case "swords":
            {
                g.DrawLine(pen, b.Left + r * 0.12f, b.Bottom - r * 0.12f, b.Right - r * 0.2f, b.Top + r * 0.16f);
                g.DrawLine(pen, b.Right - r * 0.12f, b.Bottom - r * 0.12f, b.Left + r * 0.2f, b.Top + r * 0.16f);

                using var guard = new Pen(ink, Math.Max(1f, stroke * 0.8f));
                g.DrawLine(guard, b.Left + r * 0.04f, b.Bottom - r * 0.44f, b.Left + r * 0.48f, b.Bottom);
                g.DrawLine(guard, b.Right - r * 0.48f, b.Bottom, b.Right - r * 0.04f, b.Bottom - r * 0.44f);
                break;
            }

            case "podium":
            {
                var w = b.Width / 3.4f;

                g.FillRectangle(brush, cx - w / 2f, cy - r * 0.62f, w, r * 1.62f);
                g.FillRectangle(brush, cx - w / 2f - w - r * 0.08f, cy - r * 0.08f, w, r * 1.08f);
                g.FillRectangle(brush, cx + w / 2f + r * 0.08f, cy + r * 0.14f, w, r * 0.86f);
                break;
            }

            case "moon":
            {
                using var outer = new GraphicsPath();
                outer.AddEllipse(cx - r * 0.86f, cy - r * 0.86f, r * 1.72f, r * 1.72f);

                using var bite = new GraphicsPath();
                bite.AddEllipse(cx - r * 0.42f, cy - r * 1.02f, r * 1.6f, r * 1.6f);

                using var region = new Region(outer);
                region.Exclude(bite);

                g.FillRegion(brush, region);
                break;
            }

            case "calendar":
            {
                var body = new RectangleF(b.Left, b.Top + r * 0.26f, b.Width, b.Height - r * 0.26f);

                using (var path = AppTheme.RoundedRect(body, (int)Math.Max(2, r * 0.2f)))
                {
                    g.DrawPath(pen, path);
                }

                g.DrawLine(pen, body.Left, body.Top + r * 0.42f, body.Right, body.Top + r * 0.42f);
                g.DrawLine(pen, cx - r * 0.5f, b.Top, cx - r * 0.5f, body.Top + r * 0.1f);
                g.DrawLine(pen, cx + r * 0.5f, b.Top, cx + r * 0.5f, body.Top + r * 0.1f);
                g.FillRectangle(brush, cx - r * 0.18f, body.Top + r * 0.7f, r * 0.36f, r * 0.36f);
                break;
            }

            // --- The second set: drawn to the same rules as the first -
            // proportions of the box, one ink, cut-outs made with a Region
            // rather than a second colour, since an emblem only has one.

            case "rocket":
            {
                // Drawn upright, then tipped to forty-five degrees so it is
                // plainly going somewhere. Saved and restored so the tilt
                // never leaks into whatever the caller draws next.
                var state = g.Save();
                g.TranslateTransform(cx, cy);
                g.RotateTransform(45f);
                g.TranslateTransform(-cx, -cy);

                using (var hull = new GraphicsPath())
                {
                    hull.AddPolygon(new[]
                    {
                        new PointF(cx, cy - r * 0.95f),
                        new PointF(cx + r * 0.3f, cy - r * 0.45f),
                        new PointF(cx + r * 0.3f, cy + r * 0.42f),
                        new PointF(cx - r * 0.3f, cy + r * 0.42f),
                        new PointF(cx - r * 0.3f, cy - r * 0.45f),
                    });

                    using var fins = new GraphicsPath();
                    fins.AddPolygon(new[]
                    {
                        new PointF(cx - r * 0.3f, cy - r * 0.02f),
                        new PointF(cx - r * 0.66f, cy + r * 0.52f),
                        new PointF(cx - r * 0.3f, cy + r * 0.42f),
                    });
                    fins.AddPolygon(new[]
                    {
                        new PointF(cx + r * 0.3f, cy - r * 0.02f),
                        new PointF(cx + r * 0.66f, cy + r * 0.52f),
                        new PointF(cx + r * 0.3f, cy + r * 0.42f),
                    });

                    using var window = new GraphicsPath();
                    window.AddEllipse(cx - r * 0.15f, cy - r * 0.38f, r * 0.3f, r * 0.3f);

                    using var body = new Region(hull);
                    body.Union(fins);
                    body.Exclude(window);
                    g.FillRegion(brush, body);
                }

                // The exhaust.
                g.FillPolygon(brush, new[]
                {
                    new PointF(cx - r * 0.18f, cy + r * 0.54f),
                    new PointF(cx + r * 0.18f, cy + r * 0.54f),
                    new PointF(cx, cy + r * 0.95f),
                });

                g.Restore(state);
                break;
            }

            case "pulse":
            {
                // A heartbeat trace - the finish nobody could watch.
                g.DrawLines(pen, new[]
                {
                    new PointF(b.Left, cy),
                    new PointF(cx - r * 0.45f, cy),
                    new PointF(cx - r * 0.25f, cy - r * 0.72f),
                    new PointF(cx + r * 0.05f, cy + r * 0.78f),
                    new PointF(cx + r * 0.3f, cy - r * 0.35f),
                    new PointF(cx + r * 0.45f, cy),
                    new PointF(b.Right, cy),
                });
                break;
            }

            case "gem":
            {
                var girdle = cy - r * 0.15f;
                var tip = new PointF(cx, cy + r * 0.85f);

                g.DrawPolygon(pen, new[]
                {
                    new PointF(cx - r * 0.52f, cy - r * 0.6f),
                    new PointF(cx + r * 0.52f, cy - r * 0.6f),
                    new PointF(cx + r * 0.95f, girdle),
                    tip,
                    new PointF(cx - r * 0.95f, girdle),
                });

                // The facets, finer than the outline so the stone reads
                // as cut rather than as a shield.
                using var facet = new Pen(ink, Math.Max(1f, stroke * 0.55f));
                g.DrawLine(facet, cx - r * 0.95f, girdle, cx + r * 0.95f, girdle);
                g.DrawLine(facet, cx - r * 0.52f, cy - r * 0.6f, cx - r * 0.3f, girdle);
                g.DrawLine(facet, cx + r * 0.52f, cy - r * 0.6f, cx + r * 0.3f, girdle);
                g.DrawLine(facet, cx - r * 0.3f, girdle, tip.X, tip.Y);
                g.DrawLine(facet, cx + r * 0.3f, girdle, tip.X, tip.Y);
                break;
            }

            case "swoosh":
            {
                // Down, then all the way back up: the race from the back.
                var tip = new PointF(b.Right - r * 0.12f, b.Top + r * 0.12f);

                g.DrawBezier(pen,
                    new PointF(b.Left + r * 0.05f, cy - r * 0.2f),
                    new PointF(cx - r * 0.55f, b.Bottom),
                    new PointF(cx + r * 0.05f, cy + r * 0.75f),
                    tip);

                g.DrawLines(pen, new[]
                {
                    new PointF(tip.X - r * 0.52f, tip.Y + r * 0.02f),
                    tip,
                    new PointF(tip.X - r * 0.02f, tip.Y + r * 0.52f),
                });
                break;
            }

            case "heart":
            {
                using var path = new GraphicsPath();
                path.AddBezier(
                    cx, cy + r * 0.85f,
                    cx - r * 1.1f, cy + r * 0.05f,
                    cx - r * 0.6f, cy - r * 0.95f,
                    cx, cy - r * 0.38f);
                path.AddBezier(
                    cx, cy - r * 0.38f,
                    cx + r * 0.6f, cy - r * 0.95f,
                    cx + r * 1.1f, cy + r * 0.05f,
                    cx, cy + r * 0.85f);
                path.CloseFigure();

                g.FillPath(brush, path);
                break;
            }

            case "binoculars":
            {
                // Everybody else is a long way back.
                g.FillRectangle(brush, cx - r * 0.78f, cy - r * 0.68f, r * 0.5f, r * 0.62f);
                g.FillRectangle(brush, cx + r * 0.28f, cy - r * 0.68f, r * 0.5f, r * 0.62f);
                g.DrawLine(pen, cx - r * 0.28f, cy - r * 0.3f, cx + r * 0.28f, cy - r * 0.3f);
                g.DrawEllipse(pen, cx - r * 0.93f, cy - r * 0.08f, r * 0.8f, r * 0.8f);
                g.DrawEllipse(pen, cx + r * 0.13f, cy - r * 0.08f, r * 0.8f, r * 0.8f);
                break;
            }

            case "tortoise":
            {
                var oy = cy + r * 0.2f;

                using (var shell = new GraphicsPath())
                {
                    shell.AddArc(cx - r * 0.98f, oy - r * 0.75f, r * 1.56f, r * 1.5f, 180, 180);
                    shell.CloseFigure();
                    g.FillPath(brush, shell);
                }

                // Head, and two legs under the shell.
                g.FillEllipse(brush, cx + r * 0.46f, oy - r * 0.42f, r * 0.5f, r * 0.42f);
                g.FillRectangle(brush, cx - r * 0.78f, oy - r * 0.04f, r * 0.3f, r * 0.4f);
                g.FillRectangle(brush, cx + r * 0.02f, oy - r * 0.04f, r * 0.3f, r * 0.4f);
                break;
            }

            case "twin-bolt":
            {
                DrawBoltShape(g, brush, cx - r * 0.4f, cy + r * 0.1f, r * 0.66f);
                DrawBoltShape(g, brush, cx + r * 0.4f, cy - r * 0.1f, r * 0.66f);
                break;
            }

            case "shooting-star":
            {
                DrawStar(g, brush, cx + r * 0.3f, cy - r * 0.3f, r * 0.58f, r * 0.25f, 5);

                // The tail, streaming away down-left.
                using var trail = new Pen(ink, Math.Max(1f, stroke * 0.8f))
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                };

                g.DrawLine(trail, cx - r * 0.12f, cy + r * 0.12f, cx - r * 0.85f, cy + r * 0.85f);
                g.DrawLine(trail, cx + r * 0.1f, cy + r * 0.36f, cx - r * 0.35f, cy + r * 0.81f);
                g.DrawLine(trail, cx - r * 0.36f, cy - r * 0.1f, cx - r * 0.81f, cy + r * 0.35f);
                break;
            }

            case "dice":
            {
                // A pair, both showing two - a double, twice over.
                var size = r * 0.9f;
                var pip = r * 0.2f;
                var corner = (int)Math.Max(2, r * 0.16f);

                var left = new RectangleF(cx - r * 0.95f, cy - r * 0.12f, size, size);
                var right = new RectangleF(cx + r * 0.05f, cy - r * 0.8f, size, size);

                foreach (var die in new[] { left, right })
                {
                    using (var path = AppTheme.RoundedRect(die, corner))
                    {
                        g.DrawPath(pen, path);
                    }

                    g.FillEllipse(brush, die.Left + die.Width * 0.3f - pip / 2f, die.Top + die.Height * 0.3f - pip / 2f, pip, pip);
                    g.FillEllipse(brush, die.Left + die.Width * 0.7f - pip / 2f, die.Top + die.Height * 0.7f - pip / 2f, pip, pip);
                }

                break;
            }

            case "anvil":
            {
                g.FillPolygon(brush, new[]
                {
                    new PointF(cx - r * 0.95f, cy - r * 0.45f),
                    new PointF(cx + r * 0.85f, cy - r * 0.45f),
                    new PointF(cx + r * 0.85f, cy - r * 0.12f),
                    new PointF(cx + r * 0.32f, cy + r * 0.08f),
                    new PointF(cx + r * 0.32f, cy + r * 0.42f),
                    new PointF(cx + r * 0.62f, cy + r * 0.7f),
                    new PointF(cx - r * 0.62f, cy + r * 0.7f),
                    new PointF(cx - r * 0.32f, cy + r * 0.42f),
                    new PointF(cx - r * 0.32f, cy + r * 0.08f),
                    new PointF(cx - r * 0.4f, cy - r * 0.12f),
                    new PointF(cx - r * 0.7f, cy - r * 0.22f),
                });
                break;
            }

            case "sunrise":
            {
                var horizon = cy + r * 0.4f;

                g.FillPie(brush, cx - r * 0.5f, horizon - r * 0.5f, r, r, 180f, 180f);
                g.DrawLine(pen, b.Left, horizon, b.Right, horizon);

                for (var i = 1; i <= 5; i++)
                {
                    var angle = Math.PI + i * Math.PI / 6;
                    g.DrawLine(pen,
                        cx + (float)Math.Cos(angle) * r * 0.7f, horizon + (float)Math.Sin(angle) * r * 0.7f,
                        cx + (float)Math.Cos(angle) * r * 0.95f, horizon + (float)Math.Sin(angle) * r * 0.95f);
                }

                break;
            }

            case "cat":
            {
                // A black cat, which on a Friday the 13th is the whole joke.
                using var head = new GraphicsPath();
                head.AddEllipse(cx - r * 0.72f, cy - r * 0.42f, r * 1.44f, r * 1.22f);

                using var ears = new GraphicsPath();
                ears.AddPolygon(new[]
                {
                    new PointF(cx - r * 0.7f, cy - r * 0.05f),
                    new PointF(cx - r * 0.62f, cy - r * 0.9f),
                    new PointF(cx - r * 0.12f, cy - r * 0.4f),
                });
                ears.AddPolygon(new[]
                {
                    new PointF(cx + r * 0.7f, cy - r * 0.05f),
                    new PointF(cx + r * 0.62f, cy - r * 0.9f),
                    new PointF(cx + r * 0.12f, cy - r * 0.4f),
                });

                using var eyes = new GraphicsPath();
                eyes.AddEllipse(cx - r * 0.4f, cy - r * 0.02f, r * 0.24f, r * 0.3f);
                eyes.AddEllipse(cx + r * 0.16f, cy - r * 0.02f, r * 0.24f, r * 0.3f);

                using var face = new Region(head);
                face.Union(ears);
                face.Exclude(eyes);
                g.FillRegion(brush, face);
                break;
            }

            case "snowflake":
            {
                for (var i = 0; i < 6; i++)
                {
                    var angle = -Math.PI / 2 + i * Math.PI / 3;
                    var cos = (float)Math.Cos(angle);
                    var sin = (float)Math.Sin(angle);

                    g.DrawLine(pen, cx, cy, cx + cos * r * 0.95f, cy + sin * r * 0.95f);

                    // A small V on each arm, pointing outwards.
                    var mx = cx + cos * r * 0.55f;
                    var my = cy + sin * r * 0.55f;

                    foreach (var side in new[] { -0.8, 0.8 })
                    {
                        g.DrawLine(pen, mx, my,
                            mx + (float)Math.Cos(angle + side) * r * 0.3f,
                            my + (float)Math.Sin(angle + side) * r * 0.3f);
                    }
                }

                break;
            }

            case "globe":
            {
                using var fine = new Pen(ink, Math.Max(1f, stroke * 0.7f));

                g.DrawEllipse(pen, cx - r * 0.9f, cy - r * 0.9f, r * 1.8f, r * 1.8f);
                g.DrawEllipse(fine, cx - r * 0.4f, cy - r * 0.9f, r * 0.8f, r * 1.8f);
                g.DrawLine(fine, cx - r * 0.9f, cy, cx + r * 0.9f, cy);
                g.DrawLine(fine, cx - r * 0.78f, cy - r * 0.45f, cx + r * 0.78f, cy - r * 0.45f);
                g.DrawLine(fine, cx - r * 0.78f, cy + r * 0.45f, cx + r * 0.78f, cy + r * 0.45f);
                break;
            }

            case "car":
            {
                var wheelY = cy + r * 0.42f;

                using var shape = new GraphicsPath();
                shape.AddRectangle(new RectangleF(b.Left, cy - r * 0.08f, b.Width, r * 0.52f));

                using var cabin = new GraphicsPath();
                cabin.AddPolygon(new[]
                {
                    new PointF(cx - r * 0.55f, cy - r * 0.06f),
                    new PointF(cx - r * 0.3f, cy - r * 0.52f),
                    new PointF(cx + r * 0.32f, cy - r * 0.52f),
                    new PointF(cx + r * 0.62f, cy - r * 0.06f),
                });

                // Wheel arches and windows are cut out of the body, so the
                // wheels sit in gaps of their own rather than merging into
                // one silhouette.
                using var cuts = new GraphicsPath();
                cuts.AddEllipse(cx - r * 0.84f, wheelY - r * 0.3f, r * 0.6f, r * 0.6f);
                cuts.AddEllipse(cx + r * 0.24f, wheelY - r * 0.3f, r * 0.6f, r * 0.6f);
                cuts.AddPolygon(new[]
                {
                    new PointF(cx - r * 0.36f, cy - r * 0.14f),
                    new PointF(cx - r * 0.2f, cy - r * 0.42f),
                    new PointF(cx + r * 0.02f, cy - r * 0.42f),
                    new PointF(cx + r * 0.02f, cy - r * 0.14f),
                });
                cuts.AddPolygon(new[]
                {
                    new PointF(cx + r * 0.12f, cy - r * 0.14f),
                    new PointF(cx + r * 0.12f, cy - r * 0.42f),
                    new PointF(cx + r * 0.26f, cy - r * 0.42f),
                    new PointF(cx + r * 0.44f, cy - r * 0.14f),
                });

                using var body = new Region(shape);
                body.Union(cabin);
                body.Exclude(cuts);
                g.FillRegion(brush, body);

                g.FillEllipse(brush, cx - r * 0.76f, wheelY - r * 0.22f, r * 0.44f, r * 0.44f);
                g.FillEllipse(brush, cx + r * 0.32f, wheelY - r * 0.22f, r * 0.44f, r * 0.44f);
                break;
            }

            case "summit":
            {
                var peak = new PointF(cx - r * 0.1f, cy - r * 0.3f);

                g.FillPolygon(brush, new[]
                {
                    new PointF(cx - r, cy + r * 0.8f),
                    peak,
                    new PointF(cx + r * 0.25f, cy + r * 0.12f),
                    new PointF(cx + r * 0.48f, cy - r * 0.08f),
                    new PointF(cx + r, cy + r * 0.8f),
                });

                // A flag planted on the top.
                g.DrawLine(pen, peak.X, peak.Y, peak.X, b.Top + r * 0.05f);
                g.FillPolygon(brush, new[]
                {
                    new PointF(peak.X, b.Top + r * 0.05f),
                    new PointF(peak.X + r * 0.55f, b.Top + r * 0.22f),
                    new PointF(peak.X, b.Top + r * 0.4f),
                });
                break;
            }

            case "rosette":
            {
                // A ribbon rosette: the two tails first, so the disc sits on
                // top of where they meet.
                using (var tails = new Pen(ink, Math.Max(1.5f, stroke * 1.5f)))
                {
                    g.DrawLine(tails, cx - r * 0.12f, cy + r * 0.1f, cx - r * 0.45f, b.Bottom);
                    g.DrawLine(tails, cx + r * 0.12f, cy + r * 0.1f, cx + r * 0.45f, b.Bottom);
                }

                DrawStar(g, brush, cx, cy - r * 0.28f, r * 0.7f, r * 0.56f, 12);
                break;
            }

            case "clover":
            {
                var leaf = r * 0.36f;
                var reach = r * 0.36f;
                var heartY = cy - r * 0.12f;

                g.FillEllipse(brush, cx - leaf, heartY - reach - leaf, leaf * 2, leaf * 2);
                g.FillEllipse(brush, cx + reach - leaf, heartY - leaf, leaf * 2, leaf * 2);
                g.FillEllipse(brush, cx - leaf, heartY + reach - leaf, leaf * 2, leaf * 2);
                g.FillEllipse(brush, cx - reach - leaf, heartY - leaf, leaf * 2, leaf * 2);

                // The stem leaves through the gap between two leaves.
                g.DrawBezier(pen,
                    new PointF(cx, heartY),
                    new PointF(cx + r * 0.3f, heartY + r * 0.3f),
                    new PointF(cx + r * 0.45f, cy + r * 0.55f),
                    new PointF(cx + r * 0.7f, cy + r * 0.88f));
                break;
            }

            case "flame":
            {
                // Leaning, with a second lick of flame on its left side -
                // a symmetrical one reads as a drop of water.
                using var outer = new GraphicsPath();
                outer.AddBezier(
                    cx + r * 0.12f, b.Top,
                    cx + r * 0.95f, cy - r * 0.3f,
                    cx + r * 0.85f, cy + r * 0.4f,
                    cx + r * 0.5f, cy + r * 0.75f);
                outer.AddBezier(
                    cx + r * 0.5f, cy + r * 0.75f,
                    cx + r * 0.25f, cy + r * 0.98f,
                    cx - r * 0.25f, cy + r * 0.98f,
                    cx - r * 0.5f, cy + r * 0.75f);
                outer.AddBezier(
                    cx - r * 0.5f, cy + r * 0.75f,
                    cx - r * 0.85f, cy + r * 0.4f,
                    cx - r * 0.8f, cy - r * 0.05f,
                    cx - r * 0.48f, cy - r * 0.4f);
                outer.AddBezier(
                    cx - r * 0.48f, cy - r * 0.4f,
                    cx - r * 0.42f, cy - r * 0.1f,
                    cx - r * 0.28f, cy + r * 0.02f,
                    cx - r * 0.12f, cy - r * 0.1f);
                outer.AddBezier(
                    cx - r * 0.12f, cy - r * 0.1f,
                    cx - r * 0.2f, cy - r * 0.45f,
                    cx - r * 0.05f, cy - r * 0.75f,
                    cx + r * 0.12f, b.Top);
                outer.CloseFigure();

                // The hot core, cut out so the flame has a heart to it.
                using var core = new GraphicsPath();
                core.AddBezier(
                    cx, cy + r * 0.05f,
                    cx + r * 0.35f, cy + r * 0.35f,
                    cx + r * 0.3f, cy + r * 0.72f,
                    cx, cy + r * 0.72f);
                core.AddBezier(
                    cx, cy + r * 0.72f,
                    cx - r * 0.3f, cy + r * 0.72f,
                    cx - r * 0.35f, cy + r * 0.35f,
                    cx, cy + r * 0.05f);
                core.CloseFigure();

                using var fire = new Region(outer);
                fire.Exclude(core);
                g.FillRegion(brush, fire);
                break;
            }

            case "target":
            {
                using var fine = new Pen(ink, Math.Max(1f, stroke * 0.8f))
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                };

                g.DrawEllipse(pen, cx - r * 0.9f, cy - r * 0.9f, r * 1.8f, r * 1.8f);
                g.DrawEllipse(pen, cx - r * 0.52f, cy - r * 0.52f, r * 1.04f, r * 1.04f);
                g.FillEllipse(brush, cx - r * 0.2f, cy - r * 0.2f, r * 0.4f, r * 0.4f);

                // The arrow, dead centre, with its flights up and to the right.
                g.DrawLine(fine, cx, cy, cx + r * 0.72f, cy - r * 0.72f);
                g.DrawLine(fine, cx + r * 0.6f, cy - r * 0.6f, cx + r * 0.6f, cy - r * 0.95f);
                g.DrawLine(fine, cx + r * 0.6f, cy - r * 0.6f, cx + r * 0.95f, cy - r * 0.6f);
                break;
            }

            case "broom":
            {
                // Swept clean.
                g.DrawLine(pen, cx + r * 0.78f, b.Top + r * 0.05f, cx - r * 0.1f, cy + r * 0.1f);

                g.FillPolygon(brush, new[]
                {
                    new PointF(cx - r * 0.24f, cy - r * 0.02f),
                    new PointF(cx + r * 0.06f, cy + r * 0.24f),
                    new PointF(cx - r * 0.42f, cy + r * 0.95f),
                    new PointF(cx - r * 0.95f, cy + r * 0.52f),
                });

                DrawFourPointStar(g, brush, cx + r * 0.5f, cy + r * 0.5f, r * 0.32f, r * 0.09f);
                break;
            }

            case "bounce":
            {
                using var trail = new Pen(ink, Math.Max(1f, stroke * 0.7f))
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                };

                // Down to the ground, and a bigger bounce back up.
                var ground = new PointF(cx - r * 0.4f, cy + r * 0.75f);

                g.DrawBezier(trail,
                    new PointF(b.Left, cy + r * 0.05f),
                    new PointF(cx - r * 0.75f, cy + r * 0.35f),
                    new PointF(cx - r * 0.55f, ground.Y),
                    ground);
                g.DrawBezier(trail,
                    ground,
                    new PointF(cx - r * 0.15f, cy + r * 0.1f),
                    new PointF(cx + r * 0.05f, cy - r * 0.3f),
                    new PointF(cx + r * 0.25f, cy - r * 0.42f));

                g.DrawLine(pen, cx - r * 0.78f, cy + r * 0.9f, cx - r * 0.02f, cy + r * 0.9f);
                g.FillEllipse(brush, cx + r * 0.2f, cy - r * 0.88f, r * 0.62f, r * 0.62f);
                break;
            }

            case "slingshot":
            {
                var fork = new PointF(cx, cy + r * 0.12f);

                g.DrawLine(pen, cx, b.Bottom, fork.X, fork.Y);
                g.DrawLine(pen, fork.X, fork.Y, cx - r * 0.55f, cy - r * 0.5f);
                g.DrawLine(pen, fork.X, fork.Y, cx + r * 0.55f, cy - r * 0.5f);

                // The band, and the stone already on its way.
                using (var band = new Pen(ink, Math.Max(1f, stroke * 0.55f)))
                {
                    g.DrawLine(band, cx - r * 0.55f, cy - r * 0.5f, cx + r * 0.55f, cy - r * 0.5f);
                }

                g.FillEllipse(brush, cx - r * 0.16f, b.Top, r * 0.32f, r * 0.32f);
                break;
            }

            case "chain-broken":
            {
                // Two links, parted, with the snap between them.
                var state = g.Save();
                g.TranslateTransform(cx, cy);
                g.RotateTransform(-45f);
                g.TranslateTransform(-cx, -cy);

                g.DrawEllipse(pen, cx - r * 1.0f, cy - r * 0.28f, r * 0.8f, r * 0.56f);
                g.DrawEllipse(pen, cx + r * 0.2f, cy - r * 0.28f, r * 0.8f, r * 0.56f);

                g.Restore(state);

                using var spark = new Pen(ink, Math.Max(1f, stroke * 0.6f))
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                };

                g.DrawLine(spark, cx - r * 0.14f, cy - r * 0.14f, cx - r * 0.42f, cy - r * 0.42f);
                g.DrawLine(spark, cx + r * 0.14f, cy + r * 0.14f, cx + r * 0.42f, cy + r * 0.42f);
                break;
            }

            case "boomerang":
            {
                // It always comes back. Two curved arms from one elbow, in
                // a heavy stroke so it reads as a solid thing thrown.
                using var arm = new Pen(ink, stroke * 2f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round,
                };

                var elbow = new PointF(cx - r * 0.5f, cy - r * 0.5f);

                g.DrawBezier(arm,
                    new PointF(cx + r * 0.85f, cy - r * 0.2f),
                    new PointF(cx + r * 0.35f, cy - r * 0.55f),
                    new PointF(cx - r * 0.1f, cy - r * 0.62f),
                    elbow);
                g.DrawBezier(arm,
                    elbow,
                    new PointF(cx - r * 0.62f, cy - r * 0.1f),
                    new PointF(cx - r * 0.55f, cy + r * 0.35f),
                    new PointF(cx - r * 0.2f, cy + r * 0.85f));
                break;
            }

            case "rings":
            {
                // Two circles linked - two people who keep turning up to
                // the same races.
                g.DrawEllipse(pen, cx - r * 0.92f, cy - r * 0.5f, r, r);
                g.DrawEllipse(pen, cx - r * 0.08f, cy - r * 0.5f, r, r);
                break;
            }

            case "ghost":
            {
                // A sheet with a wavy hem and two eyes - the ghost car you
                // race against in practice.
                using var path = new GraphicsPath();
                var left = cx - r * 0.62f;
                var right = cx + r * 0.62f;
                var hem = cy + r * 0.78f;

                path.AddArc(left, cy - r * 0.86f, right - left, (right - left) * 1.05f, 180f, 180f);
                path.AddLine(right, cy - r * 0.21f, right, hem);

                var waves = 3;
                var step = (right - left) / waves;

                for (var i = 0; i < waves; i++)
                {
                    var x = right - i * step;
                    path.AddLine(x, hem, x - step / 2f, hem - r * 0.22f);
                    path.AddLine(x - step / 2f, hem - r * 0.22f, x - step, hem);
                }

                path.CloseFigure();
                g.DrawPath(pen, path);

                var eye = r * 0.2f;
                g.FillEllipse(brush, cx - r * 0.32f, cy - r * 0.32f, eye, eye * 1.3f);
                g.FillEllipse(brush, cx + r * 0.12f, cy - r * 0.32f, eye, eye * 1.3f);
                break;
            }

            case "wrench":
            {
                // An open-ended spanner: a handle up to a jaw open at the top
                // right - a setup change that paid off.
                var jaw = r * 0.4f;
                var jx = cx + r * 0.38f;
                var jy = cy - r * 0.38f;

                g.DrawLine(pen, cx - r * 0.72f, cy + r * 0.72f, jx - jaw * 0.6f, jy + jaw * 0.6f);
                g.DrawArc(pen, jx - jaw, jy - jaw, jaw * 2f, jaw * 2f, 0f, 270f);
                break;
            }

            case "battery":
            {
                // A pack standing up, its terminal on top and a bolt inside.
                var body = new RectangleF(cx - r * 0.46f, cy - r * 0.62f, r * 0.92f, r * 1.42f);
                g.DrawRectangle(pen, body.X, body.Y, body.Width, body.Height);
                g.FillRectangle(brush, cx - r * 0.2f, body.Top - r * 0.2f, r * 0.4f, r * 0.2f);
                g.FillPolygon(brush, new[]
                {
                    new PointF(cx + r * 0.1f, cy - r * 0.42f),
                    new PointF(cx - r * 0.22f, cy + r * 0.12f),
                    new PointF(cx - r * 0.02f, cy + r * 0.12f),
                    new PointF(cx - r * 0.12f, cy + r * 0.58f),
                    new PointF(cx + r * 0.22f, cy - r * 0.02f),
                    new PointF(cx + r * 0.02f, cy - r * 0.02f),
                });
                break;
            }

            default:
            {
                DrawStar(g, brush, cx, cy, r, r * 0.45f, 5);
                break;
            }
        }
    }

    /// <summary>A pointed star with any number of points, filled.</summary>
    private static void DrawStar(Graphics g, Brush brush, float cx, float cy, float outer, float inner, int points)
    {
        var path = new PointF[points * 2];

        for (var i = 0; i < points * 2; i++)
        {
            // Starting at -90 degrees so the star sits upright rather than
            // rolled onto one point.
            var angle = -Math.PI / 2 + i * Math.PI / points;
            var radius = i % 2 == 0 ? outer : inner;

            path[i] = new PointF(
                cx + (float)Math.Cos(angle) * radius,
                cy + (float)Math.Sin(angle) * radius);
        }

        g.FillPolygon(brush, path);
    }

    /// <summary>
    /// A lightning bolt centred on a point rather than filling a box - the
    /// same shape as the "bolt" emblem, for drawing more than one.
    /// </summary>
    private static void DrawBoltShape(Graphics g, Brush brush, float cx, float cy, float r)
    {
        g.FillPolygon(brush, new[]
        {
            new PointF(cx + r * 0.32f, cy - r),
            new PointF(cx - r * 0.55f, cy + r * 0.12f),
            new PointF(cx - r * 0.02f, cy + r * 0.12f),
            new PointF(cx - r * 0.32f, cy + r),
            new PointF(cx + r * 0.58f, cy - r * 0.18f),
            new PointF(cx + r * 0.04f, cy - r * 0.18f),
        });
    }

    /// <summary>The four-point "sparkle" star, which needs a much tighter waist than a five-pointer.</summary>
    private static void DrawFourPointStar(Graphics g, Brush brush, float cx, float cy, float outer, float inner)
    {
        g.FillPolygon(brush, new[]
        {
            new PointF(cx, cy - outer),
            new PointF(cx + inner, cy - inner),
            new PointF(cx + outer, cy),
            new PointF(cx + inner, cy + inner),
            new PointF(cx, cy + outer),
            new PointF(cx - inner, cy + inner),
            new PointF(cx - outer, cy),
            new PointF(cx - inner, cy - inner),
        });
    }

    private static Color Darken(Color color, float amount) => Color.FromArgb(
        color.A,
        (int)Math.Clamp(color.R * (1 - amount), 0, 255),
        (int)Math.Clamp(color.G * (1 - amount), 0, 255),
        (int)Math.Clamp(color.B * (1 - amount), 0, 255));
}
