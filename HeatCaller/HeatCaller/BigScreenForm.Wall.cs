using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// The Big Screen's plates and flag pill (18 Nov 2026, dialog pass 7): the
/// clock band, the On Deck bar, the warning banner and the captions strip on
/// rounded plates, the race state in a pill the colour of the flag that is
/// out, and the leader's row marked. Looks only - every band keeps its size,
/// its words and when it shows.
/// </summary>
/// <remarks>
/// NOTHING NEW PER FRAME. Each plate keeps its outline path and its brushes
/// and makes them again only when its size or a colour changes; the flag
/// pill measures its words only when they change; and the 500-ms tick does
/// nothing here but compare a handful of values (ShowFlagState). The text
/// on every plate is drawn on an opaque back the plate's colour, never
/// see-through, so a ticking clock repaints only itself.
/// </remarks>
public sealed partial class BigScreenForm
{
    // The flags' own colours, the same whatever look the wall wears - as
    // the warning banner's yellow and black already were. A driver knows
    // these from the marshal's post; the look's palette doesn't get a say.
    private static readonly Color FlagGreen = Color.FromArgb(22, 163, 74);
    private static readonly Color FlagYellow = Color.FromArgb(250, 204, 21);
    private static readonly Color FlagRed = Color.FromArgb(220, 38, 38);
    private static readonly Color FlagBlack = Color.FromArgb(17, 17, 17);

    /// <summary>The leader's row: a tint of the race-state colour, with a bar of it down the left - see PaintLeaderStripe.</summary>
    private readonly DataGridViewCellStyle _leaderRowStyle = new();
    private SolidBrush? _leaderStripeBrush;
    private int _leaderStripeWidth = 6;

    /// <summary>Whether the clock band's plate is lit - there is a time on it. Unlit, the band is plain canvas, as it always was between races.</summary>
    private bool? _clockLit;

    private void BuildWall()
    {
        _grid.CellPainting += PaintLeaderStripe;
        Disposed += (_, _) => _leaderStripeBrush?.Dispose();
    }

    /// <summary>The plates' share of ApplyLook: run from it, so a change of look reaches them too.</summary>
    private void ColourWall()
    {
        _lblRaceState.Canvas = CanvasColor;

        _pnlCountdown.Canvas = CanvasColor;
        _pnlCountdown.Fill = PanelColor;
        _clockLit = null;

        _pnlOnDeck.BackColor = CanvasColor;
        _lblOnDeck.Canvas = CanvasColor;
        _lblOnDeck.BackColor = PanelColor;

        _lblWarning.Canvas = CanvasColor;
        _lblCaption.Canvas = CanvasColor;

        var tint = Blend(CanvasColor, AccentColor, 0.12);
        _leaderRowStyle.BackColor = tint;
        _leaderRowStyle.SelectionBackColor = tint;

        _leaderStripeBrush?.Dispose();
        _leaderStripeBrush = new SolidBrush(AccentColor);

        ShowFlagState();
    }

    /// <summary>The plates' share of ApplyScale: their corners, insets and the room their words keep from the edge.</summary>
    private void ScaleWall()
    {
        int Sized(int pixels) => Math.Max(1, (int)Math.Round(UiLayout.AtDpi(pixels) * _scale));

        _lblRaceState.Radius = Sized(14);
        _lblRaceState.InsetX = 0;
        _lblRaceState.InsetY = Sized(4);
        _lblRaceState.HugPad = Sized(22);

        // The plate is the band's full height, so the clocks keep every
        // pixel they had: they give up only the ring's width top and bottom,
        // and start right of the plate's rounded corners, so their square
        // backs never show past it.
        _pnlCountdown.Radius = Sized(18);
        _pnlCountdown.InsetX = Sized(16);
        _pnlCountdown.InsetY = 0;
        _pnlCountdown.Outline = Sized(4);
        _pnlCountdown.Padding = new Padding(Sized(36), _pnlCountdown.Outline, Sized(36), _pnlCountdown.Outline);

        _lblOnDeck.Radius = Sized(14);
        _lblOnDeck.InsetX = Sized(16);
        _lblOnDeck.InsetY = Sized(5);
        _lblOnDeck.Padding = new Padding(Sized(32), 0, Sized(32), 0);

        _lblWarning.Radius = Sized(16);
        _lblWarning.InsetX = Sized(16);
        _lblWarning.InsetY = Sized(6);
        _lblWarning.Padding = new Padding(Sized(32), 0, Sized(32), 0);

        _lblCaption.Radius = Sized(14);
        _lblCaption.InsetX = Sized(16);
        _lblCaption.InsetY = Sized(5);

        _leaderStripeWidth = Sized(6);
    }

    /// <summary>
    /// The flag pill's colours and the clock plate's ring, from the race as
    /// it is. On the 500-ms tick: each setter is a compare unless something
    /// changed, so most ticks repaint nothing.
    /// </summary>
    private void ShowFlagState()
    {
        Color fill;
        Color ink;
        var checkers = false;

        if (_race.IsWarmingUp)
        {
            fill = HighlightColor;
            ink = Looks.ClearestTextOn(fill);
        }
        else
        {
            switch (_race.Flag)
            {
                case FlagState.Green:
                    fill = FlagGreen;
                    ink = Color.White;
                    break;
                case FlagState.Yellow:
                    fill = FlagYellow;
                    ink = Color.Black;
                    break;
                case FlagState.Red:
                    fill = FlagRed;
                    ink = Color.White;
                    break;
                case FlagState.Checkered:
                    // Black on a light wall, white on a dark one - the
                    // chequers at its ends say which flag either way.
                    fill = Looks.ClearestTextOn(CanvasColor);
                    ink = Looks.ClearestTextOn(fill);
                    checkers = true;
                    break;
                default:
                    // No flag out - staging, the countdown, or nothing on.
                    // Staging and the countdown are the room's cue to look
                    // up, so they get the race-state colour solid; idle is
                    // quiet, the colour on the panel.
                    if (_race.State is RaceState.Staging or RaceState.Countdown)
                    {
                        fill = AccentColor;
                        ink = Looks.ClearestTextOn(fill);
                    }
                    else
                    {
                        fill = PanelColor == CanvasColor ? GridLineColor : PanelColor;
                        ink = AccentColor;
                    }

                    break;
            }
        }

        _lblRaceState.Fill = fill;
        _lblRaceState.Checkers = checkers;

        if (_lblRaceState.ForeColor != ink)
        {
            _lblRaceState.ForeColor = ink;
        }

        // The clock's plate ringed in the flag that is out, so the flag reads
        // from the back of the room even before the pill does.
        _pnlCountdown.Ring = _race.IsWarmingUp ? Color.Empty : _race.Flag switch
        {
            FlagState.Green => FlagGreen,
            FlagState.Yellow => FlagYellow,
            FlagState.Red => FlagRed,
            FlagState.Checkered => TextColor,
            _ => Color.Empty,
        };

        LightClockPlate();
    }

    /// <summary>Lights the clock band's plate while there is a time on it, and puts it out when there isn't.</summary>
    private void LightClockPlate()
    {
        var lit = _lblCountdown.Text.Length > 0 || _lblElapsed.Text.Length > 0;

        if (_clockLit == lit)
        {
            return;
        }

        _clockLit = lit;
        _pnlCountdown.Lit = lit;

        var back = lit ? PanelColor : CanvasColor;
        _lblCountdown.BackColor = back;
        _lblElapsed.BackColor = back;
        _hcBug.BackColor = back;
    }

    /// <summary>
    /// Marks the leader's row: its tint (set on the row by RefreshAll), and a
    /// bar of the race-state colour down its left edge. Only the first cell
    /// of the first row is touched; every other cell paints as it always has.
    /// </summary>
    private void PaintLeaderStripe(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex != 0 || e.ColumnIndex != 0 || e.Graphics is null || _leaderStripeBrush is null)
        {
            return;
        }

        e.Paint(e.ClipBounds, DataGridViewPaintParts.All);

        var bounds = e.CellBounds;
        e.Graphics.FillRectangle(_leaderStripeBrush, bounds.Left, bounds.Top, _leaderStripeWidth, bounds.Height - 1);
        e.Handled = true;
    }

    /// <summary>The leader's row, given its look - called by RefreshAll once the rows are in.</summary>
    private void MarkLeaderRow()
    {
        if (_grid.Rows.Count > 0)
        {
            _grid.Rows[0].DefaultCellStyle = _leaderRowStyle;
        }
    }

    // ------------------------------------------------------------------
    //  The plates themselves
    // ------------------------------------------------------------------

    /// <summary>
    /// The outline, fill and ring of one rounded plate, kept between paints:
    /// the path is made again only when the plate's rectangle or corner
    /// changes, and each brush only when its colour does.
    /// </summary>
    private sealed class PlateArt : IDisposable
    {
        private GraphicsPath? _path;
        private GraphicsPath? _inner;
        private Rectangle _pathFor;
        private int _pathRadius;
        private int _pathRing;

        private SolidBrush? _fill;
        private SolidBrush? _ring;

        /// <summary>
        /// Fills the plate; with a ring, fills the whole plate in the ring's
        /// colour and the plate less the ring's width in the fill - so the
        /// ring has even, round corners, which a wide pen on a path doesn't.
        /// </summary>
        public void Paint(Graphics g, Rectangle plate, int radius, Color fill, Color ring, int ringWidth)
        {
            if (plate.Width <= 0 || plate.Height <= 0)
            {
                return;
            }

            var ringed = !ring.IsEmpty && ringWidth > 0;
            var width = ringed ? ringWidth : 0;

            if (_path is null || plate != _pathFor || radius != _pathRadius || width != _pathRing)
            {
                _path?.Dispose();
                _inner?.Dispose();
                _path = Rounded(plate, radius);
                _inner = width > 0 ? Rounded(Rectangle.Inflate(plate, -width, -width), Math.Max(1, radius - width)) : null;
                _pathFor = plate;
                _pathRadius = radius;
                _pathRing = width;
            }

            _fill = BrushOf(_fill, fill);

            var smoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (ringed && _inner is not null)
            {
                _ring = BrushOf(_ring, ring);
                g.FillPath(_ring, _path);
                g.FillPath(_fill, _inner);
            }
            else
            {
                g.FillPath(_fill, _path);
            }

            g.SmoothingMode = smoothing;
        }

        /// <summary>The brush kept for a colour - the same one again unless the colour changed.</summary>
        private static SolidBrush BrushOf(SolidBrush? held, Color colour)
        {
            if (held is not null && held.Color == colour)
            {
                return held;
            }

            held?.Dispose();
            return new SolidBrush(colour);
        }

        public void Dispose()
        {
            _path?.Dispose();
            _inner?.Dispose();
            _fill?.Dispose();
            _ring?.Dispose();
        }

        private static GraphicsPath Rounded(Rectangle box, int radius)
        {
            var path = new GraphicsPath();
            var r = Math.Max(1, Math.Min(radius, Math.Min(box.Width, box.Height) / 2));
            var d = r * 2;

            path.AddArc(box.Left, box.Top, d, d, 180, 90);
            path.AddArc(box.Right - d - 1, box.Top, d, d, 270, 90);
            path.AddArc(box.Right - d - 1, box.Bottom - d - 1, d, d, 0, 90);
            path.AddArc(box.Left, box.Bottom - d - 1, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>
    /// A label on a rounded plate: the canvas round it, the plate in its
    /// BackColor, its words drawn by Label as ever. Whatever sets BackColor
    /// and ForeColor today - the warning, the fastest sector, the captions'
    /// fade - goes on doing so; only the shape of the back is new.
    /// </summary>
    private class WallPlate : Label
    {
        private readonly PlateArt _art = new();
        private Color _canvas = Color.Black;
        private int _radius = 14;
        private int _insetX;
        private int _insetY;

        public WallPlate()
        {
            // Its own back is painted in full each time, so drawing it to a
            // buffer first costs nothing and stops a changing line flashing.
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        }

        public Color Canvas
        {
            get => _canvas;
            set { if (_canvas != value) { _canvas = value; Invalidate(); } }
        }

        public int Radius
        {
            get => _radius;
            set { if (_radius != value) { _radius = value; Invalidate(); } }
        }

        public int InsetX
        {
            get => _insetX;
            set { if (_insetX != value) { _insetX = value; Invalidate(); } }
        }

        public int InsetY
        {
            get => _insetY;
            set { if (_insetY != value) { _insetY = value; Invalidate(); } }
        }

        /// <summary>The plate's colour - its BackColor, unless a subclass says otherwise.</summary>
        protected virtual Color PlateColour => BackColor;

        /// <summary>Where the plate goes: the label less its insets, unless a subclass says otherwise.</summary>
        protected virtual Rectangle PlateBounds(Graphics g) =>
            new(_insetX, _insetY, ClientSize.Width - 2 * _insetX, ClientSize.Height - 2 * _insetY);

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(_canvas);
            var plate = PlateBounds(e.Graphics);
            _art.Paint(e.Graphics, plate, _radius, PlateColour, Color.Empty, 0);
            PaintOnPlate(e.Graphics, plate);
        }

        /// <summary>Anything a subclass draws on the plate, under the words.</summary>
        protected virtual void PaintOnPlate(Graphics g, Rectangle plate)
        {
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _art.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// The race state - "RACING — GREEN FLAG" - in a pill as wide as its
    /// words, the colour of the flag that is out, with a strip of chequers
    /// at each end when it's the chequered flag. The words are measured only
    /// when they, the face or the pill's padding change.
    /// </summary>
    private sealed class FlagPill : WallPlate
    {
        private Color _fill = Color.Gray;
        private bool _checkers;
        private int _hugPad = 22;

        private string? _measuredText;
        private Font? _measuredFont;
        private int _measuredWidth;

        private readonly SolidBrush _white = new(Color.White);
        private readonly SolidBrush _black = new(FlagBlack);

        public bool Hug { get; set; } = true;

        public Color Fill
        {
            get => _fill;
            set { if (_fill != value) { _fill = value; Invalidate(); } }
        }

        public bool Checkers
        {
            get => _checkers;
            set { if (_checkers != value) { _checkers = value; _measuredText = null; Invalidate(); } }
        }

        public int HugPad
        {
            get => _hugPad;
            set { if (_hugPad != value) { _hugPad = value; Invalidate(); } }
        }

        protected override Color PlateColour => _fill;

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override Rectangle PlateBounds(Graphics g)
        {
            var height = ClientSize.Height - 2 * InsetY;

            if (!Hug)
            {
                return base.PlateBounds(g);
            }

            if (!ReferenceEquals(_measuredText, Text) || !ReferenceEquals(_measuredFont, Font))
            {
                _measuredText = Text;
                _measuredFont = Font;
                _measuredWidth = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
            }

            // Room at each end for the chequers when they're out.
            var ends = _checkers ? CheckerBlock(height) : 0;
            var width = Math.Min(ClientSize.Width, _measuredWidth + 2 * (_hugPad + ends));

            return new Rectangle((ClientSize.Width - width) / 2, InsetY, width, height);
        }

        /// <summary>How wide each end's chequers are: two squares of a third of the pill's height... roughly, kept clear of its rounded ends.</summary>
        private static int CheckerBlock(int height) => Math.Max(4, height * 2 / 3);

        protected override void PaintOnPlate(Graphics g, Rectangle plate)
        {
            if (!_checkers || plate.Height <= 0)
            {
                return;
            }

            // Two rows of squares, two across, at each end - inside the
            // pill's straight part, clear of the round ends.
            var square = Math.Max(2, plate.Height / 3);
            var top = plate.Top + (plate.Height - 2 * square) / 2;
            var left = plate.Left + plate.Height / 2;
            var right = plate.Right - plate.Height / 2 - 2 * square;

            Squares(left);
            Squares(right);

            void Squares(int x0)
            {
                for (var row = 0; row < 2; row++)
                {
                    for (var col = 0; col < 2; col++)
                    {
                        var brush = (row + col) % 2 == 0 ? _white : _black;
                        g.FillRectangle(brush, x0 + col * square, top + row * square, square, square);
                    }
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Words centred on the whole label, which is where the pill is.
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _white.Dispose();
                _black.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// The clock band's panel: the elapsed clock, the countdown and the mark
    /// on one rounded plate while there's a time to show, ringed in the flag
    /// that is out. Its children sit inside its Padding, opaque, so the clock
    /// ticking repaints the clock and not the plate.
    /// </summary>
    private sealed class ClockPlate : Panel
    {
        private readonly PlateArt _art = new();
        private Color _canvas = Color.Black;
        private Color _fill = Color.Gray;
        private Color _ring = Color.Empty;
        private bool _lit;

        public ClockPlate()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        public int Radius { get; set; } = 18;

        public int InsetX { get; set; } = 16;

        public int InsetY { get; set; } = 4;

        public int Outline { get; set; } = 4;

        public Color Canvas
        {
            get => _canvas;
            set { if (_canvas != value) { _canvas = value; BackColor = value; Invalidate(); } }
        }

        public Color Fill
        {
            get => _fill;
            set { if (_fill != value) { _fill = value; Invalidate(); } }
        }

        public Color Ring
        {
            get => _ring;
            set { if (_ring != value) { _ring = value; Invalidate(); } }
        }

        public bool Lit
        {
            get => _lit;
            set { if (_lit != value) { _lit = value; Invalidate(); } }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(_canvas);

            if (_lit)
            {
                var plate = new Rectangle(InsetX, InsetY, ClientSize.Width - 2 * InsetX, ClientSize.Height - 2 * InsetY);
                _art.Paint(e.Graphics, plate, Radius, _fill, _ring, Outline);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _art.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
