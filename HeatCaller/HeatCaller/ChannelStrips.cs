using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// Channel strips like a radio board's (25 Oct 2026): VOICE, SFX, MUSIC and
/// CROWD (SFX split off VOICE on 26 Oct), each a pair of bars, left and
/// right - in the Broadcast window beside what is on air, and compact at the
/// right of Race Control's flag row (ForRaceControl). See AudioLevels for
/// where the levels come from.
///
/// A VU, AS ASKED ("smooth vu"): each bar shows how loud the sound is over
/// a fraction of a second - its power, not its peaks - and moves like a
/// VU's needle, settling on a new level in about 300ms both up and down,
/// so speech reads as a steady swing rather than a flicker. Scaled as a
/// digital VU is, so a full-scale tone reads 0: -40 at the foot, green to
/// -9, amber to -3, red above. A line over each bar holds the highest the
/// bar has been for a second and a half, then falls.
///
/// DARK WHATEVER THE LOOK, as a board's meters are: the bars are read by
/// their colour, and green, amber and red only read on dark.
///
/// ONLY WHILE IT IS ON SCREEN. It redraws about 30 times a second from a
/// timer that runs only while the strips can be seen - the window shut, it
/// costs nothing - and starts from silence each time it is shown, so it
/// never opens on a reading from the last time.
/// </summary>
internal sealed class ChannelStrips : Control
{
    /// <summary>The foot of the scale, in dB below full scale.</summary>
    internal const float FloorDb = -40f;

    /// <summary>Where amber starts.</summary>
    internal const float AmberDb = -9f;

    /// <summary>Where red starts.</summary>
    internal const float RedDb = -3f;

    /// <summary>
    /// The VU's time constant: a bar covers 99% of the way to a new level in
    /// about five of these - 300ms, the standard VU's integration time.
    /// </summary>
    internal const double VuSeconds = 0.065;

    /// <summary>How fast the peak line falls once it has held, in dB a second.</summary>
    internal const float FallDbPerSecond = 20f;

    /// <summary>
    /// Power to the scale: twice the mean square, so a sine at full scale
    /// (mean square one half) reads 0 dB, as a digital VU is calibrated.
    /// </summary>
    internal const float SineCalibration = 2f;

    /// <summary>How long the peak line holds before it falls too.</summary>
    internal static readonly TimeSpan PeakHold = TimeSpan.FromSeconds(1.5);

    private static readonly Color Panel = Color.FromArgb(0x0F, 0x17, 0x2A);
    private static readonly Color Lane = Color.FromArgb(0x1E, 0x29, 0x3B);
    private static readonly Color Green = Color.FromArgb(0x22, 0xC5, 0x5E);
    private static readonly Color Amber = Color.FromArgb(0xF5, 0x9E, 0x0B);
    private static readonly Color Red = Color.FromArgb(0xEF, 0x44, 0x44);
    private static readonly Color Caption = Color.FromArgb(0xCB, 0xD5, 0xE1);
    private static readonly Color ScaleText = Color.FromArgb(0x94, 0xA3, 0xB8);
    private static readonly Color HoldLine = Color.FromArgb(0xF8, 0xFA, 0xFC);

    /// <summary>The marks down the left: where each is on the scale.</summary>
    private static readonly float[] Marks = { 0f, -10f, -20f, -30f, -40f };

    /// <summary>
    /// The compact strips' marks: none. Fitted to the flag row their bars
    /// are too short for numbers to stand apart, and the colours say it -
    /// amber from -9, red from -3. The Broadcast window's strips have the scale.
    /// </summary>
    private static readonly float[] CompactMarks = Array.Empty<float>();

    /// <summary>
    /// The board, left to right (26 Oct 2026): the announcer, the effects,
    /// the music, the crowd - the two that speak to the room first.
    /// </summary>
    private static AudioLevels.Meter[] Board => new[] { AudioLevels.Voice, AudioLevels.Effects, AudioLevels.Music, AudioLevels.Crowd };

    /// <summary>Shorter bars, a lighter scale, less room round them: for Race Control's flag row (see ForRaceControl).</summary>
    private readonly bool _compact;

    /// <summary>
    /// How tall the compact strips are to be, all told - set by the row they
    /// sit in (FitHeight), so they take the height the row already has
    /// rather than making it taller. 0 until set.
    /// </summary>
    private int _fitHeight;

    private readonly AudioLevels.Meter[] _meters;
    private readonly Bar[] _bars;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastRead;

    /// <summary>The board at full size, with its scale - the Broadcast window's until 26 Oct 2026, when the meters left it for Race Control's row.</summary>
    internal ChannelStrips()
        : this(false, Board)
    {
    }

    /// <summary>
    /// The board, compact, for the right of Race Control's flag row (26 Oct
    /// 2026, asked for by Asher, where NOW PLAYING was): sized to the row's
    /// own height (FitHeight), so the row grows by little or nothing - never
    /// shorter than LeastCompactBar's bars and their captions.
    /// </summary>
    internal static ChannelStrips ForRaceControl() => new(true, Board);

    /// <summary>
    /// The compact board spread across whatever size it is given - for Race
    /// Control's right-hand column, under Broadcast (6 Nov 2026, asked for by
    /// Asher), where it takes the height the column has left
    /// (Beside.AddSideFill, never less than <see cref="LeastSpreadHeight"/>):
    /// each strip has an equal share of the width, centred in its share, and
    /// the bars run the height.
    /// </summary>
    internal static ChannelStrips ForRaceControl(bool spread) => new(true, Board) { _spread = spread };

    /// <summary>Spread across the size given rather than drawn at its own - see ForRaceControl(bool).</summary>
    private bool _spread;

    /// <summary>The least a spread board can be drawn in: its shortest bars, their captions and the room round them.</summary>
    /// Shorter bars than the flag row's least: Race Control's column at
    /// 1280x800 had 50px under Broadcast before the page would scroll, and
    /// the column gives them all the height it has spare on top of this.
    internal int LeastSpreadHeight => (2 * Pad) + LeastSpreadBar + CaptionGap + CaptionFont.Height;

    private static int LeastSpreadBar => UiLayout.AtTextSize(14);

    /// <summary>For the tests: any meters.</summary>
    internal ChannelStrips(params AudioLevels.Meter[] meters)
        : this(false, meters)
    {
    }

    private ChannelStrips(bool compact, AudioLevels.Meter[] meters)
    {
        _compact = compact;
        _meters = meters;
        _bars = new Bar[meters.Length * 2];

        for (var i = 0; i < _bars.Length; i++)
        {
            _bars[i] = new Bar();
        }

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;

        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = "Level meters: " + string.Join(", ", meters.Select(m => m.Name.ToLowerInvariant()));

        // The theme leaves this alone: its look is its own (above).
        Tag = AppTheme.KeepBackColor;

        _timer.Tick += (_, _) => Step();
        MeasureText();
        Size = Measure();
    }

    /// <summary>One bar's reading: its smoothed power, where that is drawn to and where its peak line is, both in dB.</summary>
    internal sealed class Bar
    {
        /// <summary>The smoothed power (mean square, scaled - see SineCalibration).</summary>
        public double Power;
        public float Db = FloorDb;
        public float HoldDb = FloorDb;
        public TimeSpan HeldAt;

        /// <summary>
        /// Moves the bar towards the power just heard, <paramref name="elapsed"/>
        /// after the last reading, as a VU's needle moves: the same way up
        /// and down, a share of the distance set by how long it has been.
        /// The peak line rises with the bar, holds, then falls.
        /// </summary>
        public void Take(double power, TimeSpan now, TimeSpan elapsed)
        {
            var dt = Math.Max(0, elapsed.TotalSeconds);
            var share = 1 - Math.Exp(-dt / VuSeconds);
            Power += (Math.Max(0, power) - Power) * share;

            Db = ToDb((float)Power);
            var fall = FallDbPerSecond * (float)dt;

            if (Db >= HoldDb)
            {
                HoldDb = Db;
                HeldAt = now;
            }
            else if (now - HeldAt > PeakHold)
            {
                HoldDb = Math.Max(Db, HoldDb - fall);
            }
        }

        public void Reset()
        {
            Power = 0;
            Db = FloorDb;
            HoldDb = FloorDb;
            HeldAt = TimeSpan.Zero;
        }
    }

    /// <summary>For the tests: each bar, two to a strip, left then right.</summary>
    internal IReadOnlyList<Bar> Bars => _bars;

    /// <summary>A power (scaled mean square, 1 at full scale) as dB on this scale: never under the foot or over the top.</summary>
    internal static float ToDb(float power) =>
        power <= 0f || float.IsNaN(power)
            ? FloorDb
            : Math.Clamp(10f * (float)Math.Log10(power), FloorDb, 0f);

    /// <summary>How far up a bar <paramref name="db"/> reaches, 0 at the foot to 1 at the top.</summary>
    internal static float Fraction(float db) => Math.Clamp((db - FloorDb) / -FloorDb, 0f, 1f);

    /// <summary>One frame: the loudest of each meter since the last, and the bars moved to it.</summary>
    internal void Step()
    {
        var now = _clock.Elapsed;
        var nowTicks = Stopwatch.GetTimestamp();
        var since = _lastRead == 0 ? nowTicks - (Stopwatch.Frequency / 10) : _lastRead;
        var elapsed = _lastRead == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double)(nowTicks - _lastRead) / Stopwatch.Frequency);
        _lastRead = nowTicks;

        var changed = false;

        for (var i = 0; i < _meters.Length; i++)
        {
            var (left, right, _) = _meters[i].Heard(since, nowTicks);
            changed |= Advance(_bars[i * 2], left * SineCalibration, now, elapsed);
            changed |= Advance(_bars[(i * 2) + 1], right * SineCalibration, now, elapsed);
        }

        if (changed)
        {
            Invalidate();
        }
    }

    private static bool Advance(Bar bar, double power, TimeSpan now, TimeSpan elapsed)
    {
        var before = (bar.Db, bar.HoldDb);
        bar.Take(power, now, elapsed);
        return Math.Abs(before.Db - bar.Db) > 0.05f || Math.Abs(before.HoldDb - bar.HoldDb) > 0.05f;
    }

    // --- Sizes, at the text size in use ---

    private int Pad => UiLayout.AtTextSize(_compact ? 4 : 10);
    private int BarWidth => UiLayout.AtTextSize(_compact ? 7 : 8);
    private int BarGap => UiLayout.AtTextSize(_compact ? 2 : 3);
    private int StripGap => UiLayout.AtTextSize(_compact ? 8 : 12);
    private int BarHeight => !_compact
        ? UiLayout.AtTextSize(72)
        : _spread
        ? Math.Max(LeastSpreadBar, Height - (2 * Pad) - CaptionGap - CaptionFont.Height)
        : Math.Max(LeastCompactBar, (_fitHeight > 0 ? _fitHeight : UiLayout.AtTextSize(56)) - (2 * Pad) - CaptionGap - CaptionFont.Height);

    /// <summary>The shortest a compact bar is drawn, however short the row: below this the colours stop reading.</summary>
    private static int LeastCompactBar => UiLayout.AtTextSize(24);

    private int ScaleGap => _compact ? 0 : UiLayout.AtTextSize(4);

    private int CaptionGap => UiLayout.AtTextSize(_compact ? 2 : 5);

    // The compact captions in the smallest face, so the bars keep the height.
    private Font CaptionFont => _compact ? AppTheme.MicroFont : AppTheme.SmallFont;

    /// <summary>
    /// The compact strips sized to <paramref name="height"/> in all (the row
    /// they sit in) - never shorter than their least. True when that changed
    /// their size.
    /// </summary>
    /// <summary>How tall the compact strips come out when fitted to <paramref name="height"/> - without fitting them (for measuring).</summary>
    internal int HeightFor(int height) =>
        (2 * Pad) + Math.Max(LeastCompactBar, height - (2 * Pad) - CaptionGap - CaptionFont.Height) + CaptionGap + CaptionFont.Height;

    internal bool FitHeight(int height)
    {
        if (!_compact || height == _fitHeight)
        {
            return false;
        }

        _fitHeight = height;
        Size = Measure();
        Invalidate();
        return true;
    }

    // Measured once, and again only when the font changes: these are read
    // for every mark and strip on every frame (the window is built again
    // when the text size changes - BroadcastWindow.BuiltForTextSize).
    private int _scaleWidth;
    private int _stripWidth;

    private int ScaleWidth => _scaleWidth;

    private int StripWidth => _stripWidth;

    private void MeasureText()
    {
        _scaleWidth = _compact ? 0 : TextRenderer.MeasureText("-40", CaptionFont).Width;
        var widestName = _meters.Length == 0 ? 0 : _meters.Max(m => TextRenderer.MeasureText(m.Name, CaptionFont).Width);
        _stripWidth = Math.Max((BarWidth * 2) + BarGap, widestName);
    }

    /// <summary>The size it draws at: worked out from the text size, never stretched.</summary>
    internal Size Measure()
    {
        var width = Pad + ScaleWidth + ScaleGap + (_meters.Length * StripWidth) + ((_meters.Length - 1) * StripGap) + Pad;
        var height = Pad + BarHeight + CaptionGap + CaptionFont.Height + Pad;

        // Spread: as wide as it has been made, never narrower than it needs.
        return new Size(_spread ? Math.Max(width, Width) : width, height);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var size = Measure();

        return _spread && proposedSize.Width is > 0 and < int.MaxValue / 4
            ? new Size(proposedSize.Width, size.Height)
            : size;
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        MeasureText();
        Size = Measure();
    }

    // --- On screen only ---

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Watch();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        Watch();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _timer.Stop();
        base.OnHandleDestroyed(e);
    }

    /// <summary>
    /// The timer on while the strips can be seen, and off - the bars back
    /// at the foot - while they cannot. Visible here is false while the
    /// window round them is hidden, and WinForms tells a child when its
    /// window is shown or hidden, so this follows the window too.
    /// </summary>
    private void Watch()
    {
        var showing = Visible && IsHandleCreated;

        if (showing == _timer.Enabled)
        {
            return;
        }

        if (!showing)
        {
            _timer.Stop();

            foreach (var bar in _bars)
            {
                bar.Reset();
            }

            _lastRead = 0;
            Invalidate();
            return;
        }

        _timer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }

    // --- Drawing ---

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? BackColor);

        // Spread and given too little height to read (Race Control's column
        // in a short window): nothing, rather than bars squashed flat.
        if (_spread && Height < LeastSpreadHeight)
        {
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;

        var size = Measure();

        using (var panel = Rounded(new Rectangle(0, 0, size.Width - 1, size.Height - 1), UiLayout.AtTextSize(10)))
        using (var brush = new SolidBrush(Panel))
        {
            g.FillPath(brush, panel);
        }

        g.SmoothingMode = SmoothingMode.None;

        var top = Pad;
        var foot = top + BarHeight;

        // The scale down the left.
        var scaleRight = Pad + ScaleWidth;

        foreach (var mark in _compact ? CompactMarks : Marks)
        {
            var y = foot - (int)Math.Round(Fraction(mark) * BarHeight);
            var text = mark == 0 ? "0" : mark.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            // Centred on its mark, but never past the panel's top or bottom:
            // the compact strips' padding is less than half a line.
            var labelTop = Math.Clamp(y - (CaptionFont.Height / 2), 1, Math.Max(1, foot - CaptionFont.Height + (CaptionFont.Height / 2)));
            var box = new Rectangle(Pad - 2, labelTop, ScaleWidth, CaptionFont.Height);
            TextRenderer.DrawText(g, text, CaptionFont, box, ScaleText, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        var x = scaleRight + ScaleGap;

        // Spread: each strip an equal share of the width, centred in it.
        var share = _spread && _meters.Length > 0
            ? Math.Max(StripWidth, (size.Width - x - Pad) / _meters.Length)
            : StripWidth + StripGap;
        var stripLeft = _spread ? (share - StripWidth) / 2 : 0;

        for (var i = 0; i < _meters.Length; i++)
        {
            var pairWidth = (BarWidth * 2) + BarGap;
            var barsLeft = x + stripLeft + ((StripWidth - pairWidth) / 2);

            DrawBar(g, _bars[i * 2], new Rectangle(barsLeft, top, BarWidth, BarHeight));
            DrawBar(g, _bars[(i * 2) + 1], new Rectangle(barsLeft + BarWidth + BarGap, top, BarWidth, BarHeight));

            var captionBox = _spread
                ? new Rectangle(x, foot + CaptionGap, share, CaptionFont.Height)
                : new Rectangle(x - (StripGap / 2), foot + CaptionGap, StripWidth + StripGap, CaptionFont.Height);
            TextRenderer.DrawText(g, _meters[i].Name, CaptionFont, captionBox, Caption, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);

            x += share;
        }
    }

    private static void DrawBar(Graphics g, Bar bar, Rectangle lane)
    {
        using (var back = new SolidBrush(Lane))
        {
            g.FillRectangle(back, lane);
        }

        // Each colour's part of the scale, as far up as the reading goes.
        var reached = lane.Bottom - (int)Math.Round(Fraction(bar.Db) * lane.Height);

        Fill(g, lane, reached, FloorDb, AmberDb, Green);
        Fill(g, lane, reached, AmberDb, RedDb, Amber);
        Fill(g, lane, reached, RedDb, 0f, Red);

        // The peak line, once there is one.
        if (bar.HoldDb > FloorDb + 0.5f)
        {
            var y = lane.Bottom - (int)Math.Round(Fraction(bar.HoldDb) * lane.Height);
            y = Math.Clamp(y, lane.Top, lane.Bottom - 2);

            using var hold = new SolidBrush(HoldLine);
            g.FillRectangle(hold, lane.Left, y, lane.Width, 2);
        }
    }

    private static void Fill(Graphics g, Rectangle lane, int reached, float fromDb, float toDb, Color colour)
    {
        var bottom = lane.Bottom - (int)Math.Round(Fraction(fromDb) * lane.Height);
        var top = Math.Max(reached, lane.Bottom - (int)Math.Round(Fraction(toDb) * lane.Height));

        if (top >= bottom)
        {
            return;
        }

        using var brush = new SolidBrush(colour);
        g.FillRectangle(brush, lane.Left, top, lane.Width, bottom - top);
    }

    private static GraphicsPath Rounded(Rectangle box, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(1, radius * 2);
        path.AddArc(box.Left, box.Top, d, d, 180, 90);
        path.AddArc(box.Right - d, box.Top, d, d, 270, 90);
        path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
        path.AddArc(box.Left, box.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
