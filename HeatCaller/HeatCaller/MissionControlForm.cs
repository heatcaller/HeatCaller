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

            TextRenderer.DrawText(g, AppTheme.Spaced(tile.Title.ToUpperInvariant()), AppTheme.MicroFont,
                new Rectangle(left, y, inner, AppTheme.MicroFont.Height), muted, Line);
            y += AppTheme.MicroFont.Height + UiLayout.AtTextSize(4);

            TextRenderer.DrawText(g, tile.Value, AppTheme.StatFont,
                new Rectangle(left, y, inner, AppTheme.StatFont.Height), ink, Line);
            y += AppTheme.StatFont.Height + UiLayout.AtTextSize(2);

            var detail = new Rectangle(left, y, inner, Math.Max(1, (int)r.Bottom - y - UiLayout.AtTextSize(8)));

            TextRenderer.DrawText(g, tile.Detail, AppTheme.SmallFont, detail, muted,
                TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
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
