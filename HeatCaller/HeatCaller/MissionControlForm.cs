namespace HeatCaller;

/// <summary>
/// The Mission Control window: every check that matters before a green flag
/// (MissionControl) as a wall of tiles, green, amber or red, refreshed once
/// a second. A problem's tile glows and pulses so it is seen from across the
/// room; a tile for something not in use tonight sits dimmed.
///
/// Not modal, and one at a time (MainForm.ShowMissionControl): it is meant
/// to sit open on a second screen, or beside Race Control, all night.
///
/// On the layout pieces: the headline across the top, the tile wall taking
/// the height (one drawn control, so nothing in it can overlap or be cut
/// off), Close at the foot.
/// </summary>
public sealed class MissionControlForm : Form
{
    private readonly Func<IReadOnlyList<MissionControl.Tile>> _read;
    private readonly Label _lblHeadline = new();
    private readonly TileWall _wall = new();
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 1000 };

    internal MissionControlForm(Func<IReadOnlyList<MissionControl.Tile>> read)
    {
        _read = read;

        Text = "Mission Control";

        _lblHeadline.AutoSize = true;
        _lblHeadline.Font = AppTheme.HeadingFont;

        var btnClose = new Button { Text = "Close" };
        btnClose.Click += (_, _) => Close();

        var page = new FillPage();
        page.Add(_lblHeadline);
        page.AddFill(_wall, width => _wall.LeastHeight(width));
        page.Add(new ButtonBar(alignRight: true).With(btnClose), gapBelow: 0);

        Controls.Add(page);

        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);

        // The headline keeps the heading face the theme pass would otherwise
        // set back to the body's.
        _lblHeadline.Font = AppTheme.HeadingFont;

        // A dot in the headline's colour before it (18 Nov 2026, dialog
        // look): the night's state at a glance from across the room, in the
        // room the line already had.
        _lblHeadline.Padding = new Padding(UiLayout.AtTextSize(StatusDot + 10), 0, 0, 0);
        _lblHeadline.Paint += PaintStatusDot;

        DialogFit.Keep(this, page, width: 900, height: 560);

        // Shown beside the window it was opened from, not over it.
        ShowInTaskbar = true;

        ReadChecks();

        _refresh.Tick += (_, _) => ReadChecks();
        _refresh.Start();

        FormClosed += (_, _) =>
        {
            _refresh.Stop();
            _refresh.Dispose();
        };
    }

    /// <summary>Reads the checks again and shows them. A check that throws must not take the window - or the night - with it.</summary>
    private void ReadChecks()
    {
        IReadOnlyList<MissionControl.Tile> tiles;

        try
        {
            tiles = _read();
        }
        catch (Exception ex)
        {
            tiles = new[]
            {
                new MissionControl.Tile("failed", "Mission Control", "Couldn't check", ex.Message, MissionControl.Level.Watch),
            };
        }

        var headline = MissionControl.Headline(tiles);

        if (_lblHeadline.Text != headline)
        {
            _lblHeadline.Text = headline;
        }

        _lblHeadline.ForeColor = MissionControl.Worst(tiles) switch
        {
            MissionControl.Level.Problem => AppTheme.Danger,
            MissionControl.Level.Watch => AppTheme.Warning,
            _ => AppTheme.Success,
        };

        _wall.ShowTiles(tiles);
    }

    /// <summary>The headline's dot, at the normal text size.</summary>
    private const int StatusDot = 16;

    /// <summary>The dot before the headline: its colour, with a soft ring round it.</summary>
    private void PaintStatusDot(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var size = UiLayout.AtTextSize(StatusDot);
        var ring = Math.Max(2, UiLayout.AtTextSize(3));
        var top = (_lblHeadline.Height - size) / 2f;
        var colour = _lblHeadline.ForeColor;

        using (var halo = new SolidBrush(Looks.Mix(colour, _lblHeadline.BackColor, 0.7)))
        {
            g.FillEllipse(halo, 0, top, size, size);
        }

        using (var fill = new SolidBrush(colour))
        {
            g.FillEllipse(fill, ring, top + ring, size - ring * 2, size - ring * 2);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refresh.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// The tiles, drawn: as many to a row as fit at <see cref="TileWidth"/>,
    /// each a card with a coloured edge, its title, its value large, and a
    /// line or two of detail. A Problem tile is filled with a red that swells
    /// (NextUpBar.PulseAt - a glow, not a strobe); the motion runs only while
    /// one is showing.
    /// </summary>
    private sealed class TileWall : Control
    {
        /// <summary>A tile's width at the normal text size - the least; the row's tiles share what is left over.</summary>
        private const int TileWidth = 250;

        /// <summary>A tile's height at the normal text size.</summary>
        private const int TileHeight = 128;

        private const int Gap = 12;

        private IReadOnlyList<MissionControl.Tile> _tiles = Array.Empty<MissionControl.Tile>();
        private readonly System.Windows.Forms.Timer _pulse = new() { Interval = 50 };
        private readonly long _startedTicks = Environment.TickCount64;

        public TileWall()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw, true);

            DoubleBuffered = true;
            TabStop = false;

            _pulse.Tick += (_, _) => Invalidate();
        }

        public void ShowTiles(IReadOnlyList<MissionControl.Tile> tiles)
        {
            var sameShape = tiles.Count == _tiles.Count;
            _tiles = tiles;

            var alarming = tiles.Any(t => t.Level == MissionControl.Level.Problem);

            if (alarming && !_pulse.Enabled)
            {
                _pulse.Start();
            }
            else if (!alarming && _pulse.Enabled)
            {
                _pulse.Stop();
            }

            if (!sameShape)
            {
                Parent?.PerformLayout();
            }

            Invalidate();
        }

        private static int Columns(int width) =>
            Math.Max(1, (width + UiLayout.AtTextSize(Gap)) / (UiLayout.AtTextSize(TileWidth) + UiLayout.AtTextSize(Gap)));

        /// <summary>Tall enough for every tile at <paramref name="width"/> - the page scrolls below that.</summary>
        public int LeastHeight(int width)
        {
            var rows = (int)Math.Ceiling(Math.Max(1, _tiles.Count) / (double)Columns(width));
            return rows * UiLayout.AtTextSize(TileHeight) + (rows - 1) * UiLayout.AtTextSize(Gap);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using (var back = new SolidBrush(Parent?.BackColor ?? AppTheme.Background))
            {
                g.FillRectangle(back, ClientRectangle);
            }

            if (_tiles.Count == 0)
            {
                return;
            }

            var gap = UiLayout.AtTextSize(Gap);
            var columns = Columns(Width);
            var width = (Width - (columns - 1) * gap) / (float)columns;
            var height = UiLayout.AtTextSize(TileHeight);
            var pulse = NextUpBar.PulseAt(Environment.TickCount64 - _startedTicks);

            for (var i = 0; i < _tiles.Count; i++)
            {
                var column = i % columns;
                var row = i / columns;
                var bounds = new RectangleF(column * (width + gap), row * (height + gap), width, height);

                if (bounds.Top > Height)
                {
                    break;
                }

                DrawTile(g, bounds, _tiles[i], pulse);
            }
        }

        private void DrawTile(Graphics g, RectangleF bounds, MissionControl.Tile tile, double pulse)
        {
            var edge = tile.Level switch
            {
                MissionControl.Level.Problem => AppTheme.Danger,
                MissionControl.Level.Watch => AppTheme.Warning,
                MissionControl.Level.Good => AppTheme.Success,
                _ => AppTheme.Border,
            };

            var problem = tile.Level == MissionControl.Level.Problem;
            var off = tile.Level == MissionControl.Level.Off;

            var fill = problem
                ? Looks.Mix(AppTheme.DangerDark, Looks.Mix(AppTheme.Danger, Color.White, 0.15), pulse)
                : AppTheme.Surface;

            var ink = problem ? AppTheme.OnDanger : off ? AppTheme.TextMuted : AppTheme.TextDark;
            var muted = problem ? AppTheme.OnDanger : AppTheme.TextMuted;

            var r = RectangleF.Inflate(bounds, -0.5f, -0.5f);
            using var path = AppTheme.RoundedRect(r, AppTheme.CornerRadius);

            using (var brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
            }

            using (var pen = new Pen(problem ? Looks.Mix(fill, Color.White, 0.5) : AppTheme.Border, problem ? 2f : 1f))
            {
                g.DrawPath(pen, path);
            }

            // The coloured edge down the left, clipped to the card's corners.
            var saved = g.Save();
            g.SetClip(path);

            using (var bar = new SolidBrush(edge))
            {
                g.FillRectangle(bar, r.Left, r.Top, UiLayout.AtTextSize(6), r.Height);
            }

            g.Restore(saved);

            var pad = UiLayout.AtTextSize(16);
            var left = (int)r.Left + pad;
            var inner = Math.Max(1, (int)r.Width - pad - UiLayout.AtTextSize(10));
            var y = (int)r.Top + UiLayout.AtTextSize(10);

            const TextFormatFlags Line = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

            // The badge in the top-right corner, on the title's line - the
            // title stops short of it.
            var badge = UiLayout.AtTextSize(BadgeSize);
            var badgeBox = new RectangleF(r.Right - UiLayout.AtTextSize(10) - badge, y + (AppTheme.MicroFont.Height - badge) / 2f, badge, badge);
            var titleWidth = off ? inner : Math.Max(1, inner - badge - UiLayout.AtTextSize(6));

            TextRenderer.DrawText(g, AppTheme.Spaced(tile.Title.ToUpperInvariant()), AppTheme.MicroFont,
                new Rectangle(left, y, titleWidth, AppTheme.MicroFont.Height), muted, Line);

            if (!off)
            {
                DrawBadge(g, badgeBox, tile.Level, edge);
            }

            y += AppTheme.MicroFont.Height + UiLayout.AtTextSize(4);

            TextRenderer.DrawText(g, tile.Value, AppTheme.StatFont,
                new Rectangle(left, y, inner, AppTheme.StatFont.Height), ink, Line);
            y += AppTheme.StatFont.Height + UiLayout.AtTextSize(2);

            // Whole lines only: a box that ends part way down a line drew
            // the top half of it, cut through the letters, under the last
            // line that fitted.
            var lineHeight = Math.Max(1, AppTheme.SmallFont.Height);
            var lines = Math.Max(1, ((int)r.Bottom - y - UiLayout.AtTextSize(6)) / lineHeight);
            var detail = new Rectangle(left, y, inner, lines * lineHeight);

            TextRenderer.DrawText(g, tile.Detail, AppTheme.SmallFont, detail, muted,
                TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        /// <summary>The badge's size at the normal text size.</summary>
        private const int BadgeSize = 18;

        /// <summary>
        /// A small round badge saying the tile's level by its shape as well as
        /// its colour (18 Nov 2026, dialog look): a tick when it is fine, a
        /// "!" when it wants a look or is a problem - on a problem's red, a
        /// white badge with the "!" in red.
        /// </summary>
        private static void DrawBadge(Graphics g, RectangleF box, MissionControl.Level level, Color colour)
        {
            var problem = level == MissionControl.Level.Problem;
            var fill = problem ? AppTheme.OnDanger : colour;
            var mark = problem ? AppTheme.Danger : level == MissionControl.Level.Watch ? AppTheme.OnWarning : AppTheme.OnSuccess;

            using (var brush = new SolidBrush(fill))
            {
                g.FillEllipse(brush, box);
            }

            var stroke = Math.Max(1.5f, box.Width / 8f);

            using var pen = new Pen(mark, stroke)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round,
                LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
            };

            var cx = box.Left + box.Width / 2f;
            var w = box.Width;

            if (level == MissionControl.Level.Good)
            {
                g.DrawLines(pen, new[]
                {
                    new PointF(box.Left + w * 0.28f, box.Top + w * 0.52f),
                    new PointF(box.Left + w * 0.44f, box.Top + w * 0.68f),
                    new PointF(box.Left + w * 0.72f, box.Top + w * 0.36f),
                });

                return;
            }

            g.DrawLine(pen, cx, box.Top + w * 0.25f, cx, box.Top + w * 0.56f);

            using var dot = new SolidBrush(mark);
            var d = stroke * 1.1f;
            g.FillEllipse(dot, cx - d / 2f, box.Top + w * 0.72f - d / 2f, d, d);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _pulse.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
