using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// RACE CONTROL'S FLAG ROW (25 Oct 2026, the "morphing bar" chosen on the
/// Race Control layouts canvas). Three parts on one line:
///
///   left    where the race is, in a word - READY, STAGING, RACING... -
///           not drawn since 26 Oct 2026 (see Arrange), kept for the screen reader
///   middle  the flag buttons, only those that mean something now (see
///           RaceBar), in a ButtonBar that wraps when it has to
///   right   the level meters - VOICE, SFX, MUSIC and CROWD, compact
///           (26 Oct 2026, asked for by Asher where NOW PLAYING was; see
///           ChannelStrips). NOW PLAYING moved to the strip under the row
///           (HoldStrip), the On Air bar; the row still keeps its text, for
///           the screen reader and the tests.
///
/// The word is painted here rather than put in a label: it changes four
/// times a second at most, never takes a click, and painted it is measured
/// with the fonts in force at that moment - so Ctrl + and Ctrl - move it
/// with everything else. It is cut short with "…" rather than pushing
/// anything else along.
///
/// THE CARD'S TITLE, AND THE METERS UP TO IT (26 Oct 2026, asked for by
/// Asher: "expand the height of the VU meters to the top of the text of the
/// card's title"). The row draws its card's title itself - RACE CONTROL, in
/// the face a card's title is in - above the buttons, so the meters at the
/// right can run from the top of that title to the foot of the buttons. A
/// card's title is a label of the card's own (Section), in a row the meters
/// could not reach into; Race Control's card is built without one and hands
/// its title to this row instead (see the constructor).
///
/// THE ON AIR BAR IN THE ROW, AND THE BUTTONS CENTRED OVER IT (26 Oct 2026,
/// asked for by Asher: "shorten the on air bar to right before the vu
/// meters. stretch vu meters taller. flag buttons should be centered above
/// on air bar"). Given the strip under the buttons (HoldStrip), the row
/// holds it too: the title, the buttons and the strip are a column on the
/// left, and the meters stand at the right the whole height of it - from
/// the top of the title to the foot of the strip. The strip ends a gap short
/// of the meters, and the buttons showing are centred across its width.
///
/// The row is never shorter than one row of buttons, so it does not jump
/// when the buttons change - an empty row during practice is as tall as a
/// full one.
/// </summary>
internal sealed class FlagRow : Panel
{
    private readonly ButtonBar _bar;

    /// <summary>The level meters at the right (26 Oct 2026) - see ChannelStrips.ForRaceControl.</summary>
    private readonly ChannelStrips _meters = ChannelStrips.ForRaceControl();

    private string _phase = string.Empty;
    private Color _phaseInk = Color.Empty;
    private string _now = string.Empty;

    private Rectangle _phaseBox;

    /// <summary>The card's title, as a card shows it (upper case, spaced), or empty for none - see the class notes.</summary>
    private readonly string _caption;

    private Rectangle _captionBox;

    /// <summary>The On Air bar under the buttons, held in this row - or null when the card holds it itself. See the class notes.</summary>
    private readonly HoldStrip? _strip;

    /// <param name="strip">
    /// The strip under the buttons (Race Control's On Air bar), to be held in
    /// the row - under the buttons, as wide as they have, with the meters
    /// beside both (26 Oct 2026). Null for a row without one.
    /// </param>
    /// <param name="caption">
    /// The title of the card the row is the top of, drawn above the buttons
    /// as Section draws its own - give the card none (26 Oct 2026). Empty for
    /// no title.
    /// </param>
    public FlagRow(ButtonBar bar, string caption = "", HoldStrip? strip = null)
    {
        LayoutFreeze.Adopt(this);

        _bar = bar;

        // As Section words its title.
        var title = (caption ?? string.Empty).Trim().ToUpperInvariant();
        _caption = title.Length == 0 ? string.Empty : title.Length <= 28 ? AppTheme.Spaced(title) : title;

        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        Tag = AppTheme.KeepBackColor;
        AccessibleRole = AccessibleRole.Grouping;
        AccessibleName = "Race Control flags";

        // Placed by this row, not by itself: a bar that sized itself would
        // take one line's width however narrow the space between the word
        // and the name is.
        bar.AutoSize = false;
        bar.Dock = DockStyle.None;
        bar.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        bar.Margin = Padding.Empty;

        Controls.Add(bar);

        _meters.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        _meters.Margin = Padding.Empty;
        Controls.Add(_meters);

        if (strip is not null)
        {
            // Placed by this row, as the bar is.
            _strip = strip;
            strip.AutoSize = false;
            strip.Dock = DockStyle.None;
            strip.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            strip.Margin = Padding.Empty;
            Controls.Add(strip);
        }
    }

    /// <summary>The strip the row holds, or null - for the tests.</summary>
    internal HoldStrip? Strip => _strip;

    /// <summary>The card's title the row draws - for the tests.</summary>
    internal string CaptionText => _caption;

    /// <summary>Where the title is drawn - for the tests.</summary>
    internal Rectangle CaptionBox => _captionBox;

    /// <summary>The meters at the right of the row - for the tests.</summary>
    internal ChannelStrips Meters => _meters;

    /// <summary>The cyan Now Playing is written in - the look's own cyan where it has one, else one that reads on its card.</summary>
    internal static Color NowPlayingInk
    {
        get
        {
            if (AppTheme.CurrentMode == AppTheme.Mode.Synthwave)
            {
                return AppTheme.SynthCyan;
            }

            // On Air's live cyan on a dark card; on a light one a deeper
            // cyan, which the bright one would be unreadable against.
            return AppTheme.Surface.GetBrightness() < 0.5f
                ? Color.FromArgb(0x2E, 0xE6, 0xFF)
                : Color.FromArgb(0x00, 0x7A, 0x85);
        }
    }

    /// <summary>The word at the left, and its colour. Repaints only on a change.</summary>
    public void SetPhase(string word, Color ink)
    {
        if (string.Equals(_phase, word, StringComparison.Ordinal) && _phaseInk == ink)
        {
            return;
        }

        _phase = word;
        _phaseInk = ink;
        AccessibleName = word.Length == 0 ? "Race Control flags" : $"Race Control flags - {word}";
        Invalidate(_phaseBox);
    }

    /// <summary>
    /// What is playing, or blank for nothing. Not drawn on the row since 26
    /// Oct 2026 - the strip under it shows it (HoldStrip.SetNowPlaying) - but
    /// kept here for the screen reader, which reads the row.
    /// </summary>
    public void SetNowPlaying(string text)
    {
        text ??= string.Empty;

        if (string.Equals(_now, text, StringComparison.Ordinal))
        {
            return;
        }

        _now = text;
        AccessibleDescription = text.Length == 0 ? null : "Now playing: " + text;
    }

    /// <summary>What Now Playing says - for the tests.</summary>
    internal string NowPlayingText => _now;

    /// <summary>The word at the left - for the tests.</summary>
    internal string PhaseText => _phase;

    /// <summary>
    /// After buttons have been shown or hidden: the bar is laid out again
    /// and, when that changes how tall the row wants to be (the bar wrapping
    /// onto a second line, or back), the card it sits in as well.
    /// </summary>
    public void Rearrange()
    {
        var before = Height;

        PerformLayout();

        if (GetPreferredSize(new Size(Math.Max(1, Width), 0)).Height != before)
        {
            Parent?.PerformLayout();
            Parent?.Parent?.PerformLayout();
        }
    }

    private const int Gap = 12;

    /// <summary>The most lines of buttons the row stacks to keep the meters beside them; past that they give way (see Arrange).</summary>
    internal const int MostLinesWithMeters = 2;

    /// <summary>Where everything goes at a width - shared by the measuring and the placing, so the two always agree.</summary>
    private (Rectangle Phase, Rectangle Bar, Rectangle Now, Rectangle Caption, Rectangle Strip, int Height) Arrange(int width)
    {
        width = Math.Max(1, width);

        var micro = AppTheme.MicroFont;

        // Measured for the widest word, so the buttons never move when it changes.
        // No word at the left since 26 Oct 2026 (asked for by Asher: "see if
        // you can make the race control card a little shorter. you can pull
        // out the status indicator"): the lit flag button and the board
        // already say where the race is, and the width it took is what lets
        // the buttons and the meters share one line. The word is still kept
        // (SetPhase) - for the screen reader, and the tests.
        var phaseWidth = 0;

        // The widest a button is (every button on the bar is one width), so
        // the bar is never squeezed narrower than one of them.
        var oneButton = _bar.Controls.OfType<Control>().Select(c => c.Width + c.Margin.Horizontal).DefaultIfEmpty(0).Max();
        var buttonHeight = _bar.Controls.OfType<Control>().Select(c => c.Height + c.Margin.Vertical - UiLayout.TightButtonGap).DefaultIfEmpty(0).Max();

        // The meters at the right, at their own size (26 Oct 2026); the
        // buttons have the middle.
        //
        // Room for the most buttons any phase shows (RaceBar.MostShown), not
        // the ones showing now: the row keeps one width split and one height
        // through the night, rather than a between-races row of five
        // wrapping where the racing row of four does not and the board
        // jumping at every phase. Every button on the bar is one width.
        // How many lines the most buttons take in a middle this wide.
        int Lines(int across)
        {
            var perLine = Math.Max(1, oneButton > 0 ? Math.Max(1, across) / oneButton : 1);
            return (RaceBar.MostShown + perLine - 1) / perLine;
        }

        var nowWidth = _meters.Measure().Width;
        // No gap kept for the word when there is none (phaseWidth is 0).
        var phaseGap = phaseWidth > 0 ? phaseWidth + Gap : 0;
        var middle = width - phaseGap - nowWidth - Gap;
        var withoutMeters = width - phaseGap;

        // Narrow: the meters give way first - all of them, never a strip cut
        // in half - then the word, so the flags always have room for at least
        // one button. And they give way when keeping them would stack the
        // buttons more than two lines deep: two lines is what the row kept
        // for Now Playing before the meters came (it reserved up to 360px),
        // and a wide window's row is that tall anyway. (26 Oct 2026: giving
        // way whenever they cost a line at all hid them on Asher's own
        // screen, where the six buttons the row keeps room for fit on one
        // line only without them.)
        if (middle < oneButton || (Lines(middle) > Lines(withoutMeters) && Lines(middle) > MostLinesWithMeters))
        {
            nowWidth = 0;
            middle = withoutMeters;
        }

        if (middle < oneButton)
        {
            phaseWidth = 0;
            nowWidth = 0;
            middle = width;
        }

        middle = Math.Max(1, middle);

        // Never less than one row of buttons, even with none on it (practice):
        // the row keeps its height, and the bar is never a sliver.
        // And as many lines as the most buttons would take at this width, so a
        // phase with fewer showing is no shorter than the busiest.
        var lines = Lines(middle);
        var mostHeight = lines * (buttonHeight + UiLayout.TightButtonGap) - UiLayout.TightButtonGap;
        var barHeight = Math.Max(Math.Max(Math.Max(1, buttonHeight), mostHeight), _bar.GetPreferredSize(new Size(middle, 0)).Height);

        // The card's title above the buttons, with the gap a card leaves
        // under its title (Section: UiLayout.HeadingGap).
        var captionHeight = _caption.Length > 0 ? TextRenderer.MeasureText(_caption, micro).Height : 0;
        var captionBlock = captionHeight > 0 ? captionHeight + UiLayout.HeadingGap : 0;

        // The On Air bar under the buttons, when the row holds it, with the
        // gap the card used to leave between the two.
        var stripHeight = _strip is null ? 0 : _strip.GetPreferredSize(new Size(Math.Max(1, middle), 0)).Height;
        var stripBlock = stripHeight > 0 ? UiLayout.HintGap + stripHeight : 0;

        // The meters fitted to the row's whole height (ChannelStrips.
        // FitHeight) - from the top of the title to the foot of the strip -
        // so they add little or nothing to it, and counted whether they show
        // or not, so a window narrowed past them does not change its height.
        var content = captionBlock + Math.Max(barHeight, micro.Height) + stripBlock;
        // Their panel's bottom edge on the On Air bar's card's bottom edge,
        // not on the strip's foot under it (26 Oct 2026: "bottom of vu meters
        // should match bottom of on air bar"): the card is drawn
        // HoldStrip.CardInsetBelow up from the foot, the panel one up from its own.
        var metersFit = stripHeight > 0 ? content - (HoldStrip.CardInsetBelow - 1) : content;
        var metersHeight = _meters.HeightFor(metersFit);

        // Meters at their least can be taller than all that. Then with the
        // strip the row grows UNDER the buttons, so the strip still ends
        // where the meters do; without one the block sits in the middle.
        var extra = Math.Max(0, metersHeight - metersFit);
        var height = content + extra;
        var top = stripHeight > 0 ? 0 : extra / 2;
        var stripTop = top + content - stripHeight + extra;

        var barLeft = phaseWidth > 0 ? phaseWidth + Gap : 0;
        var barTop = top + captionBlock + Math.Max(0, (Math.Max(barHeight, micro.Height) - barHeight) / 2);

        // The buttons showing centred across the column (26 Oct 2026). The
        // bar is as wide as the buttons on one line, their last gap included
        // - the flow wraps a bar any narrower - and placed so the BUTTONS sit
        // in the middle. Wider than the column, it wraps there as before.
        var barWidth = middle;
        var oneLine = _bar.OneLineWidth;

        if (oneLine > 0 && oneLine <= middle)
        {
            var counted = _bar.Controls.OfType<Control>().Where(c => !_bar.Visible || c.Visible).ToList();
            var firstGap = counted.Count > 0 ? counted[0].Margin.Left : 0;
            var lastGap = counted.Count > 0 ? counted[^1].Margin.Right : 0;
            var buttons = Math.Max(0, oneLine - firstGap - lastGap);

            barWidth = oneLine;
            barLeft += Math.Clamp((middle - buttons) / 2 - firstGap, 0, middle - oneLine);
        }

        // With a title the meters start level with its top; without one they
        // sit in the middle of the row, as they always did.
        var metersTop = captionHeight > 0 ? top : Math.Max(0, (height - metersHeight) / 2);
        var columnWidth = Math.Max(0, nowWidth > 0 ? width - nowWidth - Gap : width);

        return (
            new Rectangle(0, 0, phaseWidth, height),
            new Rectangle(barLeft, barTop, barWidth, barHeight),
            new Rectangle(width - nowWidth, metersTop, nowWidth, nowWidth > 0 ? metersHeight : 0),
            new Rectangle(0, top, columnWidth, captionHeight),
            new Rectangle(0, stripTop, Math.Max(1, columnWidth), stripHeight),
            height);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width is > 0 and < int.MaxValue / 4
            ? proposedSize.Width
            : _bar.OneLineWidth + 2 * Gap + 360 + 120;

        return new Size(width, Arrange(width).Height);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        var (phase, bar, now, caption, strip, _) = Arrange(ClientSize.Width);

        _phaseBox = phase;
        _captionBox = caption;

        if (_strip is not null && _strip.Bounds != strip)
        {
            _strip.Bounds = strip;
        }

        if (_bar.Bounds != bar)
        {
            _bar.Bounds = bar;
        }

        // The meters at their box, or off the row when it is too narrow for
        // them - their timer stops while they cannot be seen (ChannelStrips).
        var showMeters = now.Width > 0;

        if (showMeters)
        {
            _meters.FitHeight(now.Height);
        }

        if (showMeters && _meters.Bounds != now)
        {
            _meters.Bounds = now;
        }

        if (_meters.Visible != showMeters)
        {
            _meters.Visible = showMeters;
        }

        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        PerformLayout();
    }

    /// <summary>
    /// Laid out again when it comes on screen: off screen every button reads
    /// as hidden, so the bar is measured for all of them there, and the
    /// buttons showing would not be centred until the race next moved on.
    /// </summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (Visible)
        {
            PerformLayout();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;

        // The card's title, as Section's own label draws it.
        if (_captionBox.Width > 0 && _captionBox.Height > 0)
        {
            TextRenderer.DrawText(g, _caption, AppTheme.MicroFont, _captionBox, AppTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        if (_phaseBox.Width > 0 && _phase.Length > 0)
        {
            TextRenderer.DrawText(g, AppTheme.Spaced(_phase.ToUpperInvariant()), AppTheme.MicroFont, _phaseBox,
                _phaseInk.IsEmpty ? AppTheme.TextMuted : _phaseInk,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }

    }

    /// <summary>
    /// A small speaker, <paramref name="size"/> square at (left, top), in
    /// <paramref name="ink"/> - Now Playing's mark, on the strip under the
    /// row since 26 Oct 2026 (HoldStrip).
    /// </summary>
    internal static void PaintSpeaker(Graphics g, float left, float top, float size, Color ink)
    {
        var saved = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var pen = new Pen(ink, Math.Max(1.5f, size / 9f)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            var y = top;
            var s = size;

            using var cone = new GraphicsPath();
            cone.AddLines(new[]
            {
                new PointF(left, y + s * 0.38f),
                new PointF(left + s * 0.22f, y + s * 0.38f),
                new PointF(left + s * 0.5f, y + s * 0.15f),
                new PointF(left + s * 0.5f, y + s * 0.85f),
                new PointF(left + s * 0.22f, y + s * 0.62f),
                new PointF(left, y + s * 0.62f),
            });
            cone.CloseFigure();
            g.DrawPath(pen, cone);
            g.DrawArc(pen, left + s * 0.42f, y + s * 0.25f, s * 0.45f, s * 0.5f, -55, 110);
        }

        g.Restore(saved);
    }

    internal static Size Measure(string text, Font font) =>
        TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
}

/// <summary>
/// The line under the flag row (25 Oct 2026). Always there and always the
/// same height, so nothing on the page moves when a hold starts:
///
///   at rest    which buttons on the row want holding, in muted text
///   holding    "Keep holding to throw the red flag…", a bar filling as the
///              second goes by, and "let go to cancel"
///   let go     "Let go too soon - nothing was done." for a moment
///
/// THE ON AIR BAR (26 Oct 2026, asked for by Asher: "I want the on air bar
/// to remain up while everything is going ... it should list the actual
/// segment that is playing. the imaging element ... should be incorporated
/// into it"): while anything is going out, the strip is On Air's card - the
/// segment on air in its words, and the imaging element (Now Playing) inside
/// it with a small speaker, in cyan. Race Control decides what it says
/// (RaceControlControl.UpdateOnAirBar). A hold still takes the whole strip,
/// as before: it is the one thing being done, and lasts a second.
/// </summary>
internal sealed class HoldStrip : Control
{
    private static readonly TimeSpan CalledOffShownFor = TimeSpan.FromSeconds(1.6);

    private readonly System.Windows.Forms.Timer _calledOffTimer = new();
    private string _resting = string.Empty;
    private IShownHold? _holding;
    private long _calledOffUntil;

    // WORK UNDER WAY (25 Oct 2026, asked for by Asher: "use the press and
    // hold animation to denote the writing and voicing delay instead of the
    // on air button"). On Air getting the opening ready - writing it, voicing
    // it - shown as a hold is: the words, a bar filling, and what to press.
    // A hold in progress is shown over it; it comes back when the hold ends.
    private string? _work;
    private float? _workFill;
    private string _workRight = string.Empty;
    private Color _workColour;

    /// <summary>What is playing - see SetNowPlaying.</summary>
    private string _nowPlaying = string.Empty;

    public HoldStrip()
    {
        LayoutFreeze.Adopt(this);

        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor,
            true);

        SetStyle(ControlStyles.Selectable, false);

        AutoSize = true;
        Margin = Padding.Empty;
        TabStop = false;
        Tag = AppTheme.KeepBackColor;
        AccessibleRole = AccessibleRole.StatusBar;

        _calledOffTimer.Interval = (int)CalledOffShownFor.TotalMilliseconds;
        _calledOffTimer.Tick += (_, _) =>
        {
            _calledOffTimer.Stop();
            Invalidate();
        };
    }

    /// <summary>What it says when nothing is being held.</summary>
    public void SetResting(string text)
    {
        text ??= string.Empty;

        if (!string.Equals(_resting, text, StringComparison.Ordinal))
        {
            _resting = text;

            if (_work is not { Length: > 0 })
            {
                AccessibleName = text;
            }

            Invalidate();
        }
    }

    /// <summary>What it says at rest - for the tests.</summary>
    internal string RestingText => _resting;

    /// <summary>
    /// What is going out right now - an imaging piece, a bed, the music or
    /// the crowd (NowPlaying.Describe) - or blank for nothing. Shown inside
    /// On Air's card, after its words. Repaints only on a change.
    /// </summary>
    public void SetNowPlaying(string text)
    {
        text ??= string.Empty;

        if (string.Equals(_nowPlaying, text, StringComparison.Ordinal))
        {
            return;
        }

        _nowPlaying = text;
        AccessibleDescription = text.Length == 0 ? null : "Now playing: " + text;
        Invalidate();
    }

    /// <summary>What Now Playing says - for the tests.</summary>
    internal string NowPlayingText => _nowPlaying;

    /// <summary>
    /// Shows work under way - On Air writing and voicing the opening - as a
    /// hold is shown: <paramref name="doing"/> in the card,
    /// <paramref name="fill"/> as the bar (none when null), and
    /// <paramref name="right"/> at its right. Repaints only on a change.
    /// </summary>
    public void ShowWork(string doing, float? fill, string right, Color colour)
    {
        right ??= string.Empty;

        // The fill to a pixel's worth or so: it is moved four times a second.
        var sameFill = fill is null ? _workFill is null : _workFill is { } was && Math.Abs(was - fill.Value) < 0.002f;

        if (string.Equals(_work, doing, StringComparison.Ordinal) && sameFill
            && string.Equals(_workRight, right, StringComparison.Ordinal) && _workColour == colour)
        {
            return;
        }

        _work = doing;
        _workFill = fill;
        _workRight = right;
        _workColour = colour;
        AccessibleName = doing.Length > 0 ? doing : _resting;
        Invalidate();
    }

    /// <summary>The work is over: back to what it says at rest.</summary>
    public void EndWork()
    {
        if (_work is null)
        {
            return;
        }

        _work = null;
        _workFill = null;
        _workRight = string.Empty;
        AccessibleName = _resting;
        Invalidate();
    }

    /// <summary>The work it shows, or null - for the tests.</summary>
    internal string? WorkText => _work;

    /// <summary>A hold has moved on, or ended.</summary>
    public void Follow(IShownHold hold)
    {
        _holding = hold.Progress is null ? (ReferenceEquals(_holding, hold) ? null : _holding) : hold;

        if (_holding is not null)
        {
            _calledOffUntil = 0;
        }

        Invalidate();
    }

    /// <summary>A hold was let go too soon.</summary>
    public void CalledOff()
    {
        _holding = null;
        _calledOffUntil = Environment.TickCount64 + (long)CalledOffShownFor.TotalMilliseconds;
        _calledOffTimer.Stop();
        _calledOffTimer.Start();
        Invalidate();
    }

    /// <summary>One line of the bigger of the two fonts it writes in, and room round it.</summary>
    private static int StripHeight => Math.Max(AppTheme.BoldFont.Height, AppTheme.SmallFont.Height) + 12;

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : 400;
        return new Size(width, StripHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var box = ClientRectangle;

        using (var back = new SolidBrush(Parent?.BackColor ?? AppTheme.Surface))
        {
            g.FillRectangle(back, box);
        }

        if (_holding?.Progress is { } done)
        {
            PaintCard(g, box, $"Keep holding to {_holding.Doing}…", "let go to cancel", done, AppTheme.Danger);
            return;
        }

        // On Air's card - what is on air, with the imaging element going
        // out inside it (26 Oct 2026; see SetNowPlaying).
        //
        // A card with no words of its own - only the music or the crowd
        // going out, no segment and On Air not live - says what the strip
        // says at rest, so the press-and-hold hint is still there all night;
        // and a hold let go too soon says so here as it always has.
        if (_work is { } work)
        {
            var words = Environment.TickCount64 < _calledOffUntil ? CalledOffWords
                : work.Length > 0 ? work : _resting;
            PaintCard(g, box, words, _workRight, _workFill, _workColour, _nowPlaying);
            return;
        }

        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                                      | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;

        var text = Environment.TickCount64 < _calledOffUntil ? CalledOffWords : _resting;

        if (text.Length > 0)
        {
            TextRenderer.DrawText(g, text, AppTheme.SmallFont, box, AppTheme.TextMuted, flags);
        }
    }

    private const string CalledOffWords = "Let go too soon - nothing was done.";

    /// <summary>
    /// How far above the strip's foot its card's bottom edge is drawn - the
    /// line the meters beside it end on (FlagRow.Arrange, 26 Oct 2026:
    /// "bottom of vu meters should match bottom of on air bar").
    /// </summary>
    internal const int CardInsetBelow = 2;

    private static int SpeakerSize => Math.Max(10, AppTheme.BoldFont.Height * 3 / 4);

    /// <summary>
    /// The card a hold is shown in - and On Air's work since 25 Oct 2026:
    /// the words, a bar filling (none when <paramref name="done"/> is null),
    /// and a note at the right, all in <paramref name="colour"/>'s tint.
    /// <paramref name="playing"/> is the imaging element going out (26 Oct
    /// 2026): after the words, with a small speaker, in Now Playing's cyan -
    /// part of the card, not off at the strip's far end. Never on a hold's.
    /// </summary>
    private void PaintCard(Graphics g, Rectangle box, string doing, string cancel, float? done, Color colour, string playing = "")
    {
        if (box.Width <= 2)
        {
            return;
        }

        var danger = colour;
        var tint = Blend(Parent?.BackColor ?? AppTheme.Surface, danger, 0.16f);

        g.SmoothingMode = SmoothingMode.AntiAlias;

        var card = new Rectangle(box.Left, box.Top + 1, box.Width - 1, box.Height - 1 - CardInsetBelow);

        using (var path = Rounded(card, 6))
        {
            using var fill = new SolidBrush(tint);
            using var edge = new Pen(danger);
            g.FillPath(fill, path);
            g.DrawPath(edge, path);
        }

        g.SmoothingMode = SmoothingMode.Default;

        var inside = Rectangle.Inflate(card, -12, 0);
        var bold = AppTheme.BoldFont;
        var small = AppTheme.SmallFont;

        var doingWidth = TextRenderer.MeasureText(doing, bold, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
        var cancelWidth = TextRenderer.MeasureText(cancel, small, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;

        const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix
                                      | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;

        // The words first, then "let go to cancel" at the right when there
        // is room for it and a bar between them worth drawing.
        var leastBar = done is null ? 0 : 60;
        var showCancel = cancel.Length > 0 && inside.Width >= doingWidth + cancelWidth + leastBar + 2 * Gap;
        var doingBox = new Rectangle(inside.Left, inside.Top, Math.Min(doingWidth, Math.Max(0, inside.Width - leastBar - Gap)), inside.Height);

        if (doing.Length > 0)
        {
            TextRenderer.DrawText(g, doing, bold, doingBox, AppTheme.TextDark, flags | TextFormatFlags.Left);
        }
        else
        {
            doingBox.Width = 0;
        }

        var barRight = inside.Right;

        if (showCancel)
        {
            var cancelBox = new Rectangle(inside.Right - cancelWidth, inside.Top, cancelWidth, inside.Height);
            TextRenderer.DrawText(g, cancel, small, cancelBox, AppTheme.TextMuted, flags | TextFormatFlags.Right);
            barRight = cancelBox.Left - Gap;
        }

        var barLeft = doingBox.Width > 0 ? doingBox.Right + Gap : inside.Left;

        // The imaging element, after the words: as wide as it needs, up to
        // what the bar can spare (the bar keeps its least, so a wait still
        // shows it is moving), the name cut short with "…" past that.
        if (playing.Length > 0)
        {
            var ink = FlagRow.NowPlayingInk;
            var speaker = SpeakerSize;
            var wanted = speaker + 6 + FlagRow.Measure(playing, bold).Width;
            var room = barRight - barLeft - (done is null ? 0 : leastBar + Gap);
            var width = Math.Min(wanted, room);

            if (width >= speaker * 3)
            {
                FlagRow.PaintSpeaker(g, barLeft, inside.Top + (inside.Height - speaker) / 2f, speaker, ink);
                var nameBox = new Rectangle(barLeft + speaker + 6, inside.Top, width - speaker - 6, inside.Height);
                TextRenderer.DrawText(g, playing, bold, nameBox, ink, flags | TextFormatFlags.Left);
                barLeft += width + Gap;
            }
        }

        var barWidth = barRight - barLeft;

        if (barWidth < 8 || done is not { } fillTo)
        {
            return;
        }

        var thick = Math.Max(4, small.Height / 3);
        var track = new Rectangle(barLeft, inside.Top + (inside.Height - thick) / 2, barWidth, thick);

        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var path = Rounded(track, thick / 2))
        using (var under = new SolidBrush(Blend(tint, danger, 0.25f)))
        {
            g.FillPath(under, path);
        }

        var filled = new Rectangle(track.Left, track.Top, (int)Math.Round(track.Width * Math.Clamp(fillTo, 0f, 1f)), track.Height);

        if (filled.Width > 1)
        {
            using var path = Rounded(filled, thick / 2);
            using var fill = new SolidBrush(danger);
            g.FillPath(fill, path);
        }
    }

    private const int Gap = 12;

    private static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        (int)Math.Round(from.R + (to.R - from.R) * amount),
        (int)Math.Round(from.G + (to.G - from.G) * amount),
        (int)Math.Round(from.B + (to.B - from.B) * amount));

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));

        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _calledOffTimer.Dispose();
        }

        base.Dispose(disposing);
    }
}
