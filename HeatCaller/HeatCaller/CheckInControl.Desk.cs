using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// Desk mode on Who's Racing (13 Nov 2026, mock-up D, which Asher picked to go
/// with the pit board): for the half hour before the first heat, when there is
/// a queue at the desk. One big box the scanner types into; the driver just
/// scanned, large, with only what they still need and a button for each; a
/// running log of the evening at the desk, and what has been taken in money.
///
/// The roster is one click away on the switch at the top, and every check-in
/// made here is the same check-in the roster makes - CheckInAtDesk, timed as
/// a desk change.
/// </summary>
public sealed partial class CheckInControl
{
    /// <summary>Roster or Desk mode.</summary>
    private readonly SegmentedSwitch _modeSwitch = new(string.Empty, "Roster", "Desk mode");

    /// <summary>The room under the strip, which the roster and the desk share; the switch shows one.</summary>
    private readonly Panel _views = new() { Tag = AppTheme.KeepBackColor };

    private Section? _rosterView;
    private Beside? _deskView;

    private readonly TextBox _txtDeskScan = new();
    private readonly Label _lblDeskStatus = new();
    private readonly DeskDriverCard _deskCard = new();
    private readonly FlowLayoutPanel _deskNeeds = new()
    {
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Margin = Padding.Empty,
        Padding = Padding.Empty,
        Tag = AppTheme.KeepBackColor,
    };

    private readonly DeskLogView _deskLog = new();
    private readonly Label _lblCashBox = new() { AutoSize = true };
    private readonly Button _btnDeskSignups = new() { Text = "Inbox: Sign-ups" };

    /// <summary>The driver on the desk's card - the last one scanned or typed - and what the card says about them.</summary>
    private DriverProfile? _deskShown;
    private string _deskHeadline = string.Empty;
    private Color _deskTone;

    /// <summary>What the needs list was built from, so it is only rebuilt when that changes.</summary>
    private string _deskNeedsBuiltFor = "-";

    /// <summary>Set while the desk itself changes check-ins, so RefreshCheckInMarks does not log them as a phone's.</summary>
    private bool _deskMarking;

    /// <summary>For the tests: whether Desk mode is showing, and the driver on its card.</summary>
    internal (bool On, DriverProfile? Shown) Desk => (_modeSwitch.On, _deskShown);

    /// <summary>For the tests: the evening's log, newest first.</summary>
    internal IReadOnlyList<DeskLogView.Entry> DeskLog => _deskLog.Entries;

    private Beside BuildDesk()
    {
        // --- The scan box ---
        _txtDeskScan.PlaceholderText = "Scan a QR check-in, or type a name and press Enter";
        _textSize.Wear(_txtDeskScan, () => AppTheme.TitleFont);
        _txtDeskScan.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.Handled = e.SuppressKeyPress = true;
            HandleEntry(_txtDeskScan, fromDesk: true);
        };

        _lblDeskStatus.AutoSize = true;
        _lblDeskStatus.Text = string.Empty;
        _textSize.Wear(_lblDeskStatus, () => AppTheme.BoldFont);
        _textSize.ReserveLines(_lblDeskStatus, 1);

        _textSize.Keep(() => _deskCard.Remeasure());

        var lblNeeds = new Label { Text = "What they still need", AutoSize = true, ForeColor = AppTheme.TextMuted };
        _textSize.Wear(lblNeeds, () => AppTheme.MicroFont);

        // --- The things done before the gate opens, under it ---
        var btnAdd = new Button { Text = "Add Walk-Up..." };
        btnAdd.Click += (_, _) => AddDriver();

        var btnBadges = new Button { Text = "Print QR Check-ins..." };
        btnBadges.Click += (_, _) => GenerateBadges();

        var btnPoster = new Button { Text = "Self Check-In Poster..." };
        btnPoster.Click += (_, _) => ShowSelfCheckInPoster();

        _btnDeskSignups.Click += (_, _) => SignupsRequested?.Invoke();

        var scan = new Section("Scan to Check In");

        scan.Add(_txtDeskScan, gapBelow: UiLayout.HintGap);
        scan.Add(_lblDeskStatus);
        scan.Add(_deskCard);
        scan.Add(lblNeeds, gapBelow: UiLayout.HintGap);
        scan.Add(_deskNeeds, gapBelow: UiLayout.SectionGap);
        scan.Add(new ButtonBar().With(btnAdd, btnBadges, btnPoster, _btnDeskSignups), gapBelow: 0);

        // --- The log, beside it ---
        _lblCashBox.ForeColor = AppTheme.TextDark;
        _textSize.Wear(_lblCashBox, () => AppTheme.BoldFont);

        var log = new Section("Tonight at the Desk");

        log.AddFill(_deskLog, () => UiLayout.AtTextSize(160));
        log.Add(_lblCashBox, gapBelow: 0);

        var body = new Beside(() => UiLayout.AtTextSize(330));

        body.Add(scan, gapBelow: 0);
        body.AddSideFill(log, log.LeastHeight, gapBelow: 0);

        RefreshDeskCard();
        return body;
    }

    /// <summary>The least height the shared room can do with: the view showing's.</summary>
    private int ViewsLeastHeight(int width) =>
        _modeSwitch.On
            ? _deskView?.LeastHeight(width) ?? 0
            : _rosterView?.LeastHeight(width) ?? 0;

    /// <summary>Shows Desk mode, or the roster.</summary>
    internal void ShowDeskMode(bool on)
    {
        if (_modeSwitch.On != on)
        {
            // Raises OnChanged, which comes back here.
            _modeSwitch.On = on;
            return;
        }

        if (_rosterView is null || _deskView is null)
        {
            return;
        }

        SuspendLayout();
        _deskView.Visible = on;
        _rosterView.Visible = !on;
        ResumeLayout(true);

        // The page's scrolling floor is the view showing's.
        FindPage()?.LeastHeightMayHaveChanged();

        if (on)
        {
            // The strip counts everyone at the desk, not the roster's class
            // chip (bug hunt 21, see InClass): counted again without it.
            RefreshCounts();
            RefreshDeskCard();
            FocusDeskScan();
        }
        else
        {
            // Drawn while the desk was showing, the roster judged tech
            // without its class chip (JudgesTechFor): drawn again with it.
            RefreshDriverList();
            _txtScan.Focus();
        }
    }

    private FillPage? FindPage()
    {
        for (var c = _views.Parent; c is not null; c = c.Parent)
        {
            if (c is FillPage page)
            {
                return page;
            }
        }

        return null;
    }

    /// <summary>Back to the scan box, so the next scan lands there - after any button on the desk.</summary>
    private void FocusDeskScan()
    {
        if (_modeSwitch.On && _txtDeskScan.CanFocus)
        {
            _txtDeskScan.Focus();
        }
    }

    /// <summary>Puts a driver on the desk's card.</summary>
    private void ShowOnDesk(DriverProfile profile, string headline, Color tone)
    {
        _deskShown = profile;
        _deskHeadline = headline;
        _deskTone = tone;
        _deskNeedsBuiltFor = "-";
        RefreshDeskCard();
    }

    /// <summary>
    /// A desk change to the check-in marks - not to be logged as a phone's.
    /// Set Up Tonight's Drivers step calls it too: its ticks are the desk's.
    /// </summary>
    public void RefreshMarksFromDesk()
    {
        _deskMarking = true;

        try
        {
            RefreshCheckInMarks();
        }
        finally
        {
            _deskMarking = false;
        }
    }

    /// <summary>A line on the evening's log.</summary>
    private void LogDesk(string who, string what) =>
        _deskLog.Add(new DeskLogView.Entry(DateTime.Now, who, what));

    /// <summary>
    /// The card, what the driver on it still needs, and the money taken -
    /// after every change the page sees (UpdateSummary).
    /// </summary>
    private void RefreshDeskCard()
    {
        // Gone from the list since - deleted, or merged.
        if (_deskShown is { } shown)
        {
            _deskShown = _profiles.Get(shown.Transponder);
        }

        RefreshCashBox();

        if (_deskShown is not { } profile)
        {
            _deskCard.Show(null, "Nobody scanned yet", "Scan a driver's QR check-in, or type their name.", AppTheme.TextMuted);
            BuildNeeds(null);
            return;
        }

        var r = ReadinessOf(profile);
        var headline = DeskHeadline(r.Here, _deskHeadline);

        // WHY (23 Nov 2026, bug hunt 24): kept, so the needs list under it
        // offers Undo again - see DeskHeadline.
        if (r.Here && !string.Equals(headline, _deskHeadline, StringComparison.Ordinal))
        {
            _deskHeadline = headline;
            _deskTone = AppTheme.Success;
        }
        var tone = r.Ready ? AppTheme.Success : r.Here ? _deskTone : AppTheme.Warning;

        _deskCard.Show(profile, r.Ready ? $"{headline} · ready to race" : headline, ClassCellText(profile), tone);
        BuildNeeds(profile);
    }

    /// <summary>
    /// The card's headline: "Not checked in" while the driver is not here;
    /// otherwise what the desk last said about them.
    /// </summary>
    /// <remarks>
    /// WHY (23 Nov 2026, bug hunt 24): Undo stores "Not checked in", and a
    /// check-in from anywhere but the desk card (their phone, the tick, the
    /// right-click menu) never replaced it, so the card read NOT CHECKED IN
    /// over a "Checked in" line for a driver who was. Here again, it says so.
    /// </remarks>
    internal static string DeskHeadline(bool here, string stored) =>
        !here ? "Not checked in"
        : string.IsNullOrEmpty(stored) || string.Equals(stored, "Not checked in", StringComparison.Ordinal) ? "Checked in"
        : stored;

    /// <summary>
    /// One row per gate for the driver on the card: done ones as a quiet
    /// line, open ones with the buttons that close them.
    /// </summary>
    private void BuildNeeds(DriverProfile? profile)
    {
        var r = profile is null ? default : ReadinessOf(profile);
        var fees = TonightsFees();
        var payment = profile is not null && fees is { Charges: true } f ? _payments?.Get(f.Day.Id, profile.Transponder) : null;
        var problems = profile is not null && ChecksPapers ? PapersProblems(profile) : new List<string>();

        // The day and the fee as well (bug hunt 19): a class ticked in Roster,
        // or a fee edited in setup, changes what's owed without changing any
        // gate, and the list kept saying "Owes $15" while Paid Cash took $25.
        var fee = profile is not null && fees is { Charges: true } charging ? FeeFor(profile, charging.Day, ClassNamed) : 0m;
        var signature = profile is null
            ? string.Empty
            : $"{profile.Transponder}|{fees?.Day.Id}|{fee}|{r}|{payment?.How}|{payment?.Amount}|{payment?.OtherWay}|{string.Join(";", problems)}|{(_tech is not null ? FailedWhy(TechFor(profile)) : "")}";

        if (signature == _deskNeedsBuiltFor)
        {
            return;
        }

        _deskNeedsBuiltFor = signature;

        _deskNeeds.SuspendLayout();

        foreach (Control old in _deskNeeds.Controls.Cast<Control>().ToList())
        {
            old.Dispose();
        }

        _deskNeeds.Controls.Clear();

        if (profile is not null)
        {
            void Row(Gate state, string words, params (string Text, Action Act, bool Primary)[] actions)
            {
                var tone = state switch
                {
                    Gate.Done => AppTheme.Success,
                    Gate.Failed => AppTheme.Danger,
                    _ => AppTheme.Warning,
                };

                var label = new Label
                {
                    Text = (state == Gate.Done ? "✓  " : "●  ") + words,
                    AutoSize = true,
                    ForeColor = tone,
                    Padding = new Padding(0, UiLayout.AtDpi(9), UiLayout.AtDpi(12), 0),
                    Font = state == Gate.Done ? AppTheme.BodyFont : AppTheme.BoldFont,
                };

                var bar = new ButtonBar().With(label);

                foreach (var (text, act, primary) in actions)
                {
                    var button = new Button { Text = text };
                    button.Click += (_, _) =>
                    {
                        act();
                        FocusDeskScan();
                    };

                    // Built after the page was themed, so styled here.
                    AppTheme.StyleButton(button, primary ? AppTheme.ButtonKind.Primary : AppTheme.ButtonKind.Secondary);
                    bar.With(button);
                }

                // A row with no button is as tall as one with, so the list does
                // not shuffle as gates close.
                bar.MinimumSize = new Size(0, UiLayout.AtTextSize(UiLayout.ButtonHeight) + UiLayout.AtDpi(4));
                bar.Margin = new Padding(0, 0, 0, UiLayout.AtDpi(6));
                _deskNeeds.Controls.Add(bar);
            }

            var one = new[] { profile };

            if (!r.Here)
            {
                Row(Gate.Open, "Not checked in", ("Check In", () => CheckInAtDesk(profile, "at the desk"), true));
            }
            else if (_deskHeadline.StartsWith("Checked in", StringComparison.Ordinal))
            {
                Row(Gate.Done, "Checked in", ("Undo", () =>
                {
                    CheckOutDrivers(one);
                    _deskHeadline = "Not checked in";
                    RefreshDeskCard();
                }, false));
            }
            else
            {
                Row(Gate.Done, "Checked in");
            }

            switch (r.Tech)
            {
                case Gate.Done:
                    Row(Gate.Done, "Tech: passed");
                    break;
                case Gate.Failed:
                    var why = FailedWhy(TechFor(profile));
                    Row(Gate.Failed, why.Length > 0 ? $"Tech: failed - {why}" : "Tech: failed",
                        ("Inspect Again...", () => InspectRequested?.Invoke(profile), true));
                    break;
                case Gate.Open:
                    Row(Gate.Open, "Tech: car not looked at yet", ("Inspect Car...", () => InspectRequested?.Invoke(profile), true));
                    break;
            }

            if (r.Paid != Gate.NotRun && fees is { Charges: true } night)
            {
                if (r.Paid == Gate.Done)
                {
                    var words = DeskMoney.Words(payment, fee);
                    Row(Gate.Done, words.Length > 0 ? $"Paid: {words}" : "Nothing to pay tonight");
                }
                else
                {
                    Row(Gate.Open, DeskMoney.Words(payment, fee),
                        ("Paid Cash", () => Settle(night.Day, one, PaidHow.Cash, string.Empty), true),
                        ("Paid Card", () => Settle(night.Day, one, PaidHow.Card, string.Empty), false));
                }
            }

            if (r.Papers != Gate.NotRun)
            {
                if (r.Papers == Gate.Done)
                {
                    Row(Gate.Done, "Papers: in order");
                }
                else
                {
                    Row(Gate.Open, $"Papers: {DeskPapers.Cell(problems)}", ("Record Papers...", () => EditPapersFor(profile), true));
                }
            }
        }

        _deskNeeds.ResumeLayout(true);
        _deskNeeds.Parent?.PerformLayout();
    }

    /// <summary>"Taken tonight: $23.00 cash · $19.00 card" - what the cash box and the card reader should hold.</summary>
    private void RefreshCashBox()
    {
        if (_payments is null || TonightsFees() is not { Charges: true } fees)
        {
            _lblCashBox.Text = string.Empty;
            _lblCashBox.Visible = false;
            return;
        }

        decimal cash = 0, card = 0;
        var comps = 0;

        foreach (var profile in _profiles.All)
        {
            switch (_payments.Get(fees.Day.Id, profile.Transponder))
            {
                // A top-up taken the other way counts where it went (bug hunt 21).
                case { How: PaidHow.Cash or PaidHow.Card } p:
                    cash += DeskMoney.CashOf(p);
                    card += DeskMoney.CardOf(p);
                    break;
                case { How: PaidHow.Comp }:
                    comps++;
                    break;
            }
        }

        var text = $"Taken tonight: {DeskMoney.Money(cash)} cash · {DeskMoney.Money(card)} card" + (comps > 0 ? $" · {comps} comp" : string.Empty);

        if (_lblCashBox.Text != text)
        {
            _lblCashBox.Text = text;
        }

        _lblCashBox.Visible = true;
    }
}

/// <summary>
/// Desk mode's card for the driver just scanned (13 Nov 2026): their initials,
/// their name large, what happened in the card's colour, and their classes.
/// Big enough to read from the far side of the desk.
/// </summary>
internal sealed class DeskDriverCard : Control
{
    private string _initials = string.Empty;
    private string _name = string.Empty;
    private string _headline = string.Empty;
    private string _detail = string.Empty;
    private Color _tone = Color.Empty;

    public DeskDriverCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                 | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);

        BackColor = Color.Transparent;
        Tag = AppTheme.KeepBackColor;
        AutoSize = true;
        Margin = Padding.Empty;
        AccessibleRole = AccessibleRole.StaticText;
    }

    /// <summary>What the card says. For the tests.</summary>
    public (string Name, string Headline, string Detail) Says => (_name, _headline, _detail);

    public void Show(DriverProfile? profile, string headline, string detail, Color tone)
    {
        var name = profile is null ? string.Empty
            : string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();

        if (name == _name && headline == _headline && detail == _detail && tone == _tone)
        {
            return;
        }

        _name = name;
        _initials = name.Length == 0 ? string.Empty : DriverPhotos.InitialsFor(name);
        _headline = headline;
        _detail = detail;
        _tone = tone;
        AccessibleName = $"{name} {headline} {detail}".Trim();
        Invalidate();
    }

    private static int Pad => UiLayout.AtDpi(18);
    private static int Avatar => AppTheme.TitleFont.Height * 2;

    public void Remeasure() => Parent?.PerformLayout();

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = AppTheme.MicroFont.Height + UiLayout.AtDpi(6) + AppTheme.TitleFont.Height + UiLayout.AtDpi(6) + AppTheme.BodyFont.Height;
        return new Size(proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : Width, Pad * 2 + Math.Max(Avatar, text));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var tone = _tone.IsEmpty ? AppTheme.TextMuted : _tone;
        var rect = new RectangleF(0, 0, Width, Height);

        using (var behind = new SolidBrush(RosterInk.Blend(tone, AppTheme.Surface, 0.9f)))
        using (var path = AppTheme.RoundedRect(RectangleF.Inflate(rect, -0.5f, -0.5f), UiLayout.AtDpi(12)))
        {
            g.FillPath(behind, path);
            using var pen = new Pen(RosterInk.Blend(tone, AppTheme.Surface, 0.55f), Math.Max(1f, UiLayout.AtDpi(1)));
            g.DrawPath(pen, path);
        }

        var x = Pad;

        if (_initials.Length > 0)
        {
            var box = new RectangleF(x, (Height - Avatar) / 2f, Avatar, Avatar);

            using (var path = AppTheme.RoundedRect(box, UiLayout.AtDpi(12)))
            {
                using var fill = new SolidBrush(AppTheme.Surface);
                g.FillPath(fill, path);
                using var pen = new Pen(tone, Math.Max(1.5f, UiLayout.AtDpi(2)));
                g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(g, _initials, AppTheme.TitleFont, Rectangle.Round(box), tone,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            x += Avatar + Pad;
        }

        var width = Math.Max(1, Width - x - Pad);
        var textHeight = AppTheme.MicroFont.Height + UiLayout.AtDpi(6) + AppTheme.TitleFont.Height + UiLayout.AtDpi(6) + AppTheme.BodyFont.Height;
        var y = (Height - textHeight) / 2;
        const TextFormatFlags line = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;

        TextRenderer.DrawText(g, AppTheme.Spaced(_headline.ToUpperInvariant()), AppTheme.MicroFont,
            new Rectangle(x, y, width, AppTheme.MicroFont.Height), tone, line);

        y += AppTheme.MicroFont.Height + UiLayout.AtDpi(6);

        TextRenderer.DrawText(g, _name.Length > 0 ? _name : "Ready for the next scan", AppTheme.TitleFont,
            new Rectangle(x, y, width, AppTheme.TitleFont.Height), _name.Length > 0 ? AppTheme.TextDark : AppTheme.TextMuted, line);

        y += AppTheme.TitleFont.Height + UiLayout.AtDpi(6);

        TextRenderer.DrawText(g, _detail, AppTheme.BodyFont, new Rectangle(x, y, width, AppTheme.BodyFont.Height), AppTheme.TextMuted, line);
    }
}

/// <summary>
/// Desk mode's log of the evening (13 Nov 2026): every check-in, check-out
/// and payment made at the desk, and the check-ins that came in from phones,
/// newest at the top.
/// </summary>
internal sealed class DeskLogView : Control
{
    public sealed record Entry(DateTime At, string Who, string What);

    private readonly List<Entry> _entries = new();

    public DeskLogView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                 | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);

        BackColor = Color.Transparent;
        Tag = AppTheme.KeepBackColor;
        Margin = Padding.Empty;
        AccessibleRole = AccessibleRole.List;
    }

    public IReadOnlyList<Entry> Entries => _entries;

    /// <summary>Takes the newest line off when it is this one - a change put straight back.</summary>
    public void RemoveNewest(string who, string what)
    {
        if (_entries.Count > 0 && _entries[0].Who == who && _entries[0].What == what)
        {
            _entries.RemoveAt(0);
            AccessibleName = _entries.Count > 0 ? $"{_entries[0].Who} {_entries[0].What}" : null;
            Invalidate();
        }
    }

    /// <summary>Empties the log - a new race day starts its own.</summary>
    public void Clear()
    {
        _entries.Clear();
        AccessibleName = null;
        Invalidate();
    }

    public void Add(Entry entry)
    {
        _entries.Insert(0, entry);

        if (_entries.Count > 300)
        {
            _entries.RemoveAt(_entries.Count - 1);
        }

        AccessibleName = $"{entry.Who} {entry.What}";
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        const TextFormatFlags line = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;

        if (_entries.Count == 0)
        {
            TextRenderer.DrawText(g, "Check-ins, payments and phone check-ins made tonight show here.", AppTheme.SmallFont,
                new Rectangle(0, 0, Width, Height), AppTheme.TextMuted, TextFormatFlags.Left | TextFormatFlags.WordBreak);
            return;
        }

        var timeWidth = TextRenderer.MeasureText(g, AppTime.TimeOfDay(DateTime.Today.AddHours(22).AddMinutes(48)), AppTheme.SmallFont, Size.Empty, TextFormatFlags.NoPadding).Width
            + UiLayout.AtDpi(12);
        var rowHeight = AppTheme.BoldFont.Height + AppTheme.SmallFont.Height + UiLayout.AtDpi(12);
        var y = 0;

        using var rule = new Pen(AppTheme.Border);

        foreach (var entry in _entries)
        {
            if (y + rowHeight > Height)
            {
                break;
            }

            var top = y + UiLayout.AtDpi(6);

            TextRenderer.DrawText(g, AppTime.TimeOfDay(entry.At), AppTheme.SmallFont, new Rectangle(0, top + 1, timeWidth, AppTheme.SmallFont.Height),
                AppTheme.TextMuted, line);

            var x = timeWidth;
            var width = Math.Max(1, Width - x);

            TextRenderer.DrawText(g, entry.Who, AppTheme.BoldFont, new Rectangle(x, top, width, AppTheme.BoldFont.Height), AppTheme.TextDark, line);
            TextRenderer.DrawText(g, entry.What, AppTheme.SmallFont, new Rectangle(x, top + AppTheme.BoldFont.Height, width, AppTheme.SmallFont.Height),
                AppTheme.TextMuted, line);

            y += rowHeight;
            g.DrawLine(rule, 0, y - 1, Width, y - 1);
        }
    }
}
