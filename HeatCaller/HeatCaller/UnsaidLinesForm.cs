using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// Every timing call the announcer didn't say this race, stacked newest
/// first, each with what it was, who it was about, what it would have said
/// and why it went (20 Oct 2026).
///
/// ASKED FOR FROM THE FLOOR: "a popup window when I click on the lost
/// number of lines on race control. It should show each one stacked up
/// with details about the line and why it was dropped." The count on the
/// strip says the announcer is running out of room; this says which calls
/// it is losing and to what - a lap time lost behind the race recap is a
/// different problem from a fast lap held back by the pacing dial, and
/// only the list can tell them apart.
///
/// NOT MODAL. It is opened during a race to watch the lines go, so Race
/// Control stays usable behind it, and it refreshes itself once a second
/// while it is open - the same reason the room vote's tally does. One
/// window at a time: clicking the count again brings it to the front.
///
/// HELD LINES ARE LISTED TOO, with a tick to hide them, because a club
/// tuning the pacing dial wants to see both side by side. Lost lines are
/// marked in the warning colour and held ones are not, for the reason the
/// strip gives: held is the dial working.
/// </summary>
internal sealed class UnsaidLinesForm : Form
{
    /// <summary>How often the list is checked for new lines while the window is open.</summary>
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(1);

    /// <summary>Space inside each card, and between one card and the next.</summary>
    private const int Pad = 8;

    private const int CardGap = 6;

    /// <summary>A ListBox row can be no taller than this - Windows' own limit for owner-drawn rows.</summary>
    private const int TallestRow = 255;

    private readonly AnnouncerLoad _load;

    /// <summary>The AI commentary's own line for this race, or null - see NightAccount.RaceReport.</summary>
    private readonly Func<string?>? _aiThisRace;

    private readonly Label _lblReport = new();
    private readonly Label _lblAi = new();
    private readonly CheckBox _chkHeld = new ThemedCheckBox();
    private readonly ListBox _list = new();
    private readonly Label _lblCount = new();
    private readonly System.Windows.Forms.Timer _tick = new();

    /// <summary>What the list was last drawn from, so a tick with nothing new does nothing.</summary>
    private int _drawnVersion = -1;

    private bool _drawnWithHeld;

    public UnsaidLinesForm(AnnouncerLoad load, Func<string?>? aiThisRace = null)
    {
        _load = load;
        _aiThisRace = aiThisRace;

        Text = "Lines not said this race";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        _lblReport.AutoSize = true;
        _lblReport.MaximumSize = new Size(UiLayout.AtTextSize(640), 0);
        _lblReport.ForeColor = AppTheme.TextMuted;
        _lblReport.Font = AppTheme.SmallFont;

        // The AI's segments are not timing calls and never reach the list
        // below, so a quiet AI was the one thing this window could not
        // explain (3 Nov 2026).
        _lblAi.AutoSize = true;
        _lblAi.MaximumSize = new Size(UiLayout.AtTextSize(640), 0);
        _lblAi.ForeColor = AppTheme.TextMuted;
        _lblAi.Font = AppTheme.SmallFont;

        _chkHeld.Text = "Show lines held back by the pacing dial too";
        _chkHeld.AutoSize = true;
        _chkHeld.Checked = true;
        _chkHeld.CheckedChanged += (_, _) => Fill(force: true);

        _list.DrawMode = DrawMode.OwnerDrawVariable;
        _list.IntegralHeight = false;
        _list.BorderStyle = BorderStyle.None;
        _list.SelectionMode = SelectionMode.None;
        _list.MeasureItem += MeasureCard;
        _list.DrawItem += DrawCard;

        // Variable-height rows are measured when they are added, so a resize
        // re-adds them to wrap the words at the new width.
        _list.Resize += (_, _) => Fill(force: true);

        EmptyNote.For(_list, () => _chkHeld.Checked
            ? "Nothing has been lost or held back this race."
            : "Nothing has been lost this race.");

        _lblCount.AutoSize = true;
        _lblCount.ForeColor = AppTheme.TextMuted;
        _lblCount.Font = AppTheme.SmallFont;
        _lblCount.Anchor = AnchorStyles.Left;

        var btnClose = new Button { Text = "Close" };
        btnClose.Click += (_, _) => Close();

        var footer = new SplitBar(
            new ButtonBar().With(_lblCount),
            new ButtonBar(alignRight: true).With(btnClose));

        // (18 Nov 2026, dialog batch 5): what the race came to in a card of
        // its own over the list - the count, the AI's line and the tick -
        // and the window a little taller for it, so the list keeps the
        // height it had at 1280x720. No tiles: the room they need would come
        // out of the list, and the count already sits at its foot.
        var header = new Section("This race");
        header.Add(_lblReport, gapBelow: UiLayout.HintGap);
        header.Add(_lblAi, gapBelow: UiLayout.RowGap);
        header.Add(_chkHeld, gapBelow: 0);

        var page = new FillPage();
        page.Add(header, gapBelow: UiLayout.RowGap);
        page.AddFill(_list, _ => UiLayout.AtTextSize(260), gapBelow: UiLayout.RowGap);
        page.Add(footer, gapBelow: 0);

        Controls.Add(page);

        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);

        // After the theme pass, which would give the list the surface colour
        // and leave the cards nothing to stand out from.
        _list.BackColor = AppTheme.Background;

        Fill(force: true);

        _tick.Interval = (int)CheckEvery.TotalMilliseconds;
        _tick.Tick += (_, _) => Fill(force: false);
        _tick.Start();

        // Stopped before the window goes, so a tick cannot land on a
        // disposed list.
        FormClosed += (_, _) =>
        {
            _tick.Stop();
            _tick.Dispose();
        };

        DialogFit.Keep(this, page, width: 640, height: 585);
    }

    /// <summary>
    /// Redraws the list when there is something new - a line kept, a new
    /// race clearing it, the tick box changed, or a resize. Newest first,
    /// and the reader's place is kept when new lines arrive above it.
    /// </summary>
    private void Fill(bool force)
    {
        if (IsDisposed)
        {
            return;
        }

        _lblReport.Text = _load.Report();
        _lblAi.Text = _aiThisRace?.Invoke() ?? "AI commentary this race: nothing weighed up yet.";

        var version = _load.LinesVersion;
        var withHeld = _chkHeld.Checked;

        if (!force && version == _drawnVersion && withHeld == _drawnWithHeld)
        {
            return;
        }

        var lines = _load.Lines
            .Where(line => withHeld || line.Kind == UnsaidKind.Lost)
            .Reverse()
            .ToList();

        var top = _list.Items.Count > 0 ? _list.TopIndex : 0;

        // The card at the top of the view, so the reader can be put back on
        // it after the refill (10 Nov 2026, bug hunt 16).
        var anchor = top > 0 && top < _list.Items.Count ? _list.Items[top] as UnsaidLine : null;

        _list.BeginUpdate();

        try
        {
            _list.Items.Clear();

            foreach (var line in lines)
            {
                _list.Items.Add(line);
            }

            // Reading further down when new lines came in at the top: stay
            // on the same card rather than being thrown back up.
            var keep = TopAfterRefill(anchor, top, lines);

            if (keep > 0)
            {
                _list.TopIndex = keep;
            }
        }
        finally
        {
            _list.EndUpdate();
        }

        _drawnVersion = version;
        _drawnWithHeld = withHeld;

        var lost = lines.Count(line => line.Kind == UnsaidKind.Lost);
        var held = lines.Count - lost;

        _lblCount.Text = withHeld
            ? $"{lost} lost, {held} held"
            : $"{lost} lost";

        if (_load.Lines.Count >= AnnouncerLoad.MostKept)
        {
            _lblCount.Text += $" (the last {AnnouncerLoad.MostKept} kept)";
        }
    }

    /// <summary>
    /// Where the view should start after the list is rebuilt: on the card
    /// the reader was looking at, wherever it has moved to, or the same row
    /// if that card has gone. 0 means leave it at the top.
    ///
    /// This used to shift the row by how much the list had grown, which
    /// stopped working once 300 lines were kept: each new line pushes the
    /// oldest out, the count stays the same, and the card the reader was on
    /// slid down a row with every new line (10 Nov 2026, bug hunt 16). The
    /// card is found by reference, since the lines are records and two can
    /// read the same.
    /// </summary>
    internal static int TopAfterRefill(UnsaidLine? anchor, int oldTop, IReadOnlyList<UnsaidLine> lines)
    {
        if (oldTop <= 0 || lines.Count == 0)
        {
            return 0;
        }

        if (anchor is not null)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                if (ReferenceEquals(lines[i], anchor))
                {
                    return i;
                }
            }
        }

        return Math.Min(oldTop, lines.Count - 1);
    }

    private int TextWidth() =>
        Math.Max(80, _list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - Pad * 2 - 2);

    /// <summary>A card's top line: the badge's chip, or the small face's line when that is taller.</summary>
    private static int HeadHeight() => Math.Max(AppTheme.SmallFont.Height, AppTheme.MicroFont.Height * 3 / 2);

    private const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;

    private void MeasureCard(object? sender, MeasureItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _list.Items.Count || _list.Items[e.Index] is not UnsaidLine line)
        {
            return;
        }

        var width = TextWidth();

        var head = HeadHeight();
        var words = TextRenderer.MeasureText(e.Graphics, WordsOf(line), AppTheme.BodyFont, new Size(width, int.MaxValue), Wrap).Height;
        var why = TextRenderer.MeasureText(e.Graphics, line.Why, AppTheme.SmallFont, new Size(width, int.MaxValue), Wrap).Height;

        e.ItemHeight = Math.Min(TallestRow, Pad + head + 4 + words + 4 + why + Pad + CardGap);
        e.ItemWidth = _list.ClientSize.Width;
    }

    private void DrawCard(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _list.Items.Count || _list.Items[e.Index] is not UnsaidLine line)
        {
            return;
        }

        var g = e.Graphics;
        var bounds = e.Bounds;

        using (var back = new SolidBrush(_list.BackColor))
        {
            g.FillRectangle(back, bounds);
        }

        var card = new Rectangle(bounds.X, bounds.Y, bounds.Width - SystemInformation.VerticalScrollBarWidth, bounds.Height - CardGap);

        var lost = line.Kind == UnsaidKind.Lost;

        // (18 Nov 2026, dialog batch 5): rounded, as the card lists on the
        // sidebar pages are, with a lost line's edge in the warning colour -
        // so a scroll down the list still shows where the trouble is.
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var radius = UiLayout.AtDpi(8);

        using (var path = AppTheme.RoundedRect(new RectangleF(card.X + 0.5f, card.Y + 0.5f, card.Width - 1f, card.Height - 1f), radius))
        {
            using (var surface = new SolidBrush(AppTheme.Surface))
            {
                g.FillPath(surface, path);
            }

            // The bar down the left edge, kept inside the rounded corners.
            var clip = g.Clip;
            g.SetClip(path, CombineMode.Intersect);

            using (var edge = new SolidBrush(lost ? AppTheme.Danger : AppTheme.Border))
            {
                g.FillRectangle(edge, card.X, card.Y, UiLayout.AtDpi(4), card.Height);
            }

            g.Clip = clip;

            using var border = new Pen(lost ? RosterInk.Blend(AppTheme.Danger, AppTheme.Surface, 0.55f) : AppTheme.Border);
            g.DrawPath(border, path);
        }

        var x = card.X + Pad;
        var y = card.Y + Pad;
        var width = TextWidth();

        // The top line: the badge - a chip, as Venues' "Applied" is - then
        // when, what and who.
        var badge = lost ? "LOST" : "HELD";
        var badgeText = TextRenderer.MeasureText(g, badge, AppTheme.MicroFont, Size.Empty, TextFormatFlags.NoPadding);
        var headHeight = HeadHeight();
        var chip = new RectangleF(x, y, badgeText.Width + AppTheme.MicroFont.Height, headHeight);
        var ink = lost ? AppTheme.Danger : AppTheme.TextMuted;

        using (var path = AppTheme.RoundedRect(chip, (int)(chip.Height / 2)))
        using (var fill = new SolidBrush(RosterInk.Blend(ink, AppTheme.Surface, lost ? 0.85f : 0.88f)))
        {
            g.FillPath(fill, path);
        }

        TextRenderer.DrawText(g, badge, AppTheme.MicroFont, Rectangle.Round(chip), ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

        var chipWidth = (int)Math.Ceiling(chip.Width);

        var head = $"{AppTime.TimeWithSeconds(line.AtUtc.ToLocalTime())}  ·  {line.Call}" + (line.Driver is { Length: > 0 } driver ? $"  ·  {driver}" : string.Empty);

        TextRenderer.DrawText(g, head, AppTheme.SmallFont,
            new Rectangle(x + chipWidth + 8, y, width - chipWidth - 8, headHeight),
            AppTheme.TextMuted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);

        y += headHeight + 4;

        // What it would have said.
        var words = WordsOf(line);
        var wordsHeight = TextRenderer.MeasureText(g, words, AppTheme.BodyFont, new Size(width, int.MaxValue), Wrap).Height;

        TextRenderer.DrawText(g, words, AppTheme.BodyFont, new Rectangle(x, y, width, wordsHeight),
            line.Words is null ? AppTheme.TextMuted : AppTheme.TextDark, Wrap);

        y += wordsHeight + 4;

        // Why it went.
        TextRenderer.DrawText(g, line.Why, AppTheme.SmallFont, new Rectangle(x, y, width, card.Bottom - y - Pad / 2),
            AppTheme.TextMuted, Wrap | TextFormatFlags.EndEllipsis);
    }

    private static string WordsOf(UnsaidLine line) =>
        line.Words is { Length: > 0 } words ? $"“{words}”" : "(the words weren't written yet when it was turned away)";
}
