namespace HeatCaller;

/// <summary>
/// The broadcast's controls in a window of their own (25 Oct 2026, asked
/// for by Asher): Hold, Play again, Replay Moment, Skip, Notes, Votes and
/// the count of lines not said, which used to be a row under the Next Up
/// bar on Race Control - with more said about each at a glance than a row
/// had room for.
///
/// THE SAME ROW, MOVED, NOT A SECOND COPY. The window holds Race Control's
/// own AnnouncerStrip, so every button does exactly what it did and is
/// wired exactly where it was (RaceControlControl.BuildLayout): Hold still
/// reaches the recap show, Skip still rides a replay down, the strip still
/// releases a hold when commentary is switched off. Two copies of a Hold
/// button would be two states to keep in step on the one screen where that
/// must never go wrong.
///
/// NOT MODAL, AND KEPT. Closing hides it rather than throwing it away, so
/// the row - and a hold standing in it - lives as long as Race Control
/// does. Race Control's Broadcast button says HELD while a hold stands, so
/// a hidden window never hides a hold (see RaceControlControl.ShowBroadcastButton).
///
/// NO LEVEL METERS (26 Oct 2026, asked for by Asher: "remove the vu meters
/// from the broadcast window"). The channel strips were beside the heading
/// here from 25 Oct; they are on Race Control's flag row now (FlagRow), the
/// one place they are wanted.
///
/// On the layout pieces: what is on air across the top, on a card of its
/// own in the title face behind an ON AIR sign (18 Nov 2026),
/// the controls in a card, what the announcer is doing in a column of
/// captions and values, and Close at the foot.
/// </summary>
internal sealed class BroadcastWindow : Form
{
    /// <summary>
    /// Wide enough for the row at its widest - held, with a replay moment
    /// waiting and both counts showing - in one line, at the normal text size.
    /// </summary>
    private const int WindowWidth = 780;

    /// <summary>
    /// How tall it opens, at the normal text size: all of it on a race night,
    /// with room for the longer readings (a held announcer, three lines on
    /// this race). It can be made taller or shorter; the content scrolls in
    /// a window made too short for it.
    /// </summary>
    private const int WindowHeight = 790;

    private readonly AnnouncerStrip _strip;
    private readonly Func<Keys, bool>? _raceKey;
    private readonly Button _btnClose = new() { Text = "Close" };

    /// <summary>The text size (Ctrl +/-) it was built and laid out for - see BuiltForTextSize.</summary>
    private readonly float _fontScale = AppTheme.FontScale;
    private readonly Label _lblOnAir = new();

    /// <summary>The card across the top that what is on air is shown in - see HeadingRow.</summary>
    private readonly HeadingRow _onAirRow;

    private readonly Label _lblAnnouncer = new();
    private readonly Label _lblLast = new();
    private readonly Label _lblTonight = new();
    private readonly Label _lblThisRace = new();
    private readonly Label _lblNotes = new();
    private readonly Label _lblCrewNotes = new();

    /// <summary>
    /// The last prompt tonight that drivers' own notes made long
    /// (CommentaryService.CrewNotesAlert), or null - for the Drivers' notes
    /// line (15 Nov 2026, driver crew).
    /// </summary>
    internal Func<string?>? CrewNotesAlert { get; set; }
    private readonly Label _lblVotes = new();
    private readonly Label _lblShow = new();
    private readonly Label _lblReplay = new();
    private readonly Label _lblGap = new();
    private readonly Button _btnSkipGap = new() { Text = "Skip Next Call-up" };

    /// <summary>Race Control's Skip Next Call-up, for the button beside Between races.</summary>
    internal Action? SkipGapLine { get; set; }

    /// <param name="strip">Race Control's own row of broadcast controls, moved in here.</param>
    /// <param name="raceKey">
    /// Race Control's flag keys (F1 to F8), for a key pressed while this
    /// window has the focus - it is a form of its own, so MainForm's keys do
    /// not reach it. True when the key was one of them.
    /// </param>
    internal BroadcastWindow(AnnouncerStrip strip, Func<Keys, bool>? raceKey = null)
    {
        // Built with its layout held - see LayoutFreeze.HoldWhileBuilding
        // (18 Nov 2026): built live, most parts had their windows before the
        // theme pass, which made many of them again.
        using var building = LayoutFreeze.HoldWhileBuilding();

        _strip = strip;
        _raceKey = raceKey;

        Text = "Broadcast";
        Font = AppTheme.BodyFont;

        _lblOnAir.Font = AppTheme.TitleFont;
        _onAirRow = new HeadingRow(_lblOnAir);

        // The row's own "On air" label would say the heading again, a line
        // below it.
        _strip.ShowsSaying = false;

        var controls = new Section("Controls");
        controls.Add(_strip, stretch: false, gapBelow: UiLayout.HintGap);
        controls.Add(Hint("Hold stops the next thing; Skip stops the one on air. Play again puts the last segment out "
                          + "again, and Replay Moment the last 20 seconds the room heard. Click the count at the end of the "
                          + "row for each line the announcer didn't say."), gapBelow: 0);

        var glance = new Section("At a glance");
        glance.Add(Hint("What the announcer is doing, in full. A line in colour wants a look."), gapBelow: UiLayout.RowGap);
        var grid = new FieldGrid();

        foreach (var (caption, value) in new[]
                 {
                     ("Announcer:", _lblAnnouncer),
                     ("Last segment:", _lblLast),
                     ("Tonight:", _lblTonight),
                     ("This race:", _lblThisRace),
                     ("Club notes:", _lblNotes),
                     ("Drivers' notes:", _lblCrewNotes),
                     ("Room vote:", _lblVotes),
                     ("Recap show:", _lblShow),
                     ("Replay Moment:", _lblReplay),
                 })
        {
            value.AutoSize = true;
            value.MaximumSize = new Size(UiLayout.AtTextSize(WindowWidth - 200), 0);
            grid.AddRow(caption, value);
        }

        // BETWEEN RACES (31 Oct 2026). What the automatic caller says next
        // and when, with the one control a race director wants from here:
        // let the next call-up go by.
        _lblGap.AutoSize = true;
        _lblGap.MaximumSize = new Size(UiLayout.AtTextSize(WindowWidth - 380), 0);
        _lblGap.Margin = new Padding(0, 4, 10, 0);
        _btnSkipGap.AutoSize = true;
        _btnSkipGap.Enabled = false;
        _btnSkipGap.Click += (_, _) =>
        {
            SkipGapLine?.Invoke();
            ActiveControl = _btnClose;
        };
        var gapRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty,

            // The card's colour, not the page's the theme pass gives a panel:
            // it showed as a grey band across the card.
            BackColor = AppTheme.Surface,
            Tag = AppTheme.KeepBackColor,
        };
        gapRow.Controls.Add(_lblGap);
        gapRow.Controls.Add(_btnSkipGap);
        grid.AddRow("Between races:", gapRow);
        Set(_lblGap, "Nothing waiting.");

        glance.Add(grid, gapBelow: 0);

        _btnClose.Click += (_, _) => Close();

        var page = new FillPage();
        var body = DialogFit.Body(page);
        body.Add(_onAirRow, gapBelow: UiLayout.RowGap);
        body.Add(controls, gapBelow: UiLayout.RowGap);
        body.Add(glance, gapBelow: 0);
        page.Add(new ButtonBar(alignRight: true).With(_btnClose), gapBelow: 0);

        Controls.Add(page);
        CancelButton = _btnClose;

        Restyle();

        // Sizable (a height given) rather than as tall as its content: the
        // content changes every second while it is open, and a window sized
        // once to one reading of it would scroll at the next.
        DialogFit.Keep(this, page, width: WindowWidth, height: WindowHeight, body: body);

        // Hidden, not closed, by the X, Escape and Close alike: the row in it
        // - and a hold standing in it - belongs to Race Control. Closed for
        // real only when the app is going (Race Control disposes it).
        // THE FOCUS ON CLOSE, NEVER ON HOLD. Hold is the first thing in the
        // window, so a window opened or clicked back into would give it the
        // focus - and a stray Space or Enter would hold the show, or put the
        // last segment out again over a live PA. The row's buttons let the
        // focus go after a click for that reason (AnnouncerStrip); this
        // puts it somewhere harmless whenever the window comes to the front.
        Shown += (_, _) => ActiveControl = _btnClose;
        Activated += (_, _) =>
        {
            if (ActiveControl is null || _strip.Contains(ActiveControl))
            {
                ActiveControl = _btnClose;
            }
        };

        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    /// <summary>
    /// Whether it is laid out for the text size in use. Its fonts and width
    /// are fixed when it is built (DialogFit sizes a window once), so after
    /// Ctrl +/- Race Control builds a new one rather than show this one -
    /// see RaceControlControl.ShowBroadcastWindow.
    /// </summary>
    internal bool BuiltForTextSize => Math.Abs(_fontScale - AppTheme.FontScale) < 0.001f;

    /// <summary>
    /// The theme put back on everything - when the window is first built and
    /// each time Race Control opens it. The pass resets every button's colours, so the
    /// row is asked to put Hold's amber back after it (AnnouncerStrip.Restyle).
    /// </summary>
    internal void Restyle()
    {
        AppTheme.ApplyRecursively(this);
        _lblOnAir.Font = AppTheme.TitleFont;
        _lblOnAir.BackColor = AppTheme.Surface;
        _strip.Restyle();
    }

    /// <summary>
    /// Shows it, in front - or brings it back to the front when it is open
    /// already. Race Control restyles and fills it first.
    /// </summary>
    internal void Bring(IWin32Window? owner)
    {
        if (Visible)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            Activate();
            BringToFront();
            return;
        }

        if (owner is null)
        {
            Show();
        }
        else
        {
            Show(owner);
        }
    }

    /// <summary>
    /// What the announcer is doing, said in full - from the same values the
    /// row is drawn from on the same tick (RaceControlControl's clock), and
    /// after the row has been drawn, so the heading is the row's own words.
    /// Race Control also calls it just before the window is shown.
    /// </summary>
    internal void ShowState(CommentaryDirector? director, bool enabled, bool speaking, bool muted, int notesWaiting,
        int votesIn, AnnouncerLoad? load, bool recapRunning, bool replaying, bool momentHeld)
    {
        // Filled in while hidden as well - eight labels set only on a change -
        // so it opens showing now, not a second ago.
        var on = director is not null && enabled;

        // The heading: the row's own reading of what is on air, lit as the
        // row lights it.
        Set(_lblOnAir, on ? _strip.Saying : "The announcer is off",
            on && _strip.SayingLit ? AppTheme.Accent : AppTheme.TextDark);
        _onAirRow.Lit = on && _strip.SayingLit;

        Set(_lblAnnouncer, !on
                ? "Commentary is switched off, so there is no announcer to hold or skip."
                : _strip.Holding
                    ? "HELD - no new segment, hand-over or recap show starts until you press Release."
                    : muted
                        ? "Muted on the Mixer - nothing it writes is heard."
                        : speaking
                            ? "Speaking."
                            : director!.Writing
                                ? "Writing a line - it will be on air in a few seconds."
                                : "Ready.",
            on && (_strip.Holding || director!.Writing || speaking) ? AppTheme.Accent : AppTheme.TextDark);

        Set(_lblLast, on && director!.LastSaid is { } last
            ? ShowBlock.NameFor(last.Kind) + (last.IsExchange ? " (two voices)" : string.Empty)
            : "Nothing yet.");

        Set(_lblTonight, on ? Tonight(director!.Describe()) : "-");

        Set(_lblThisRace, on && load is not null ? load.Report() : "-",
            on && load?.BadgeIsAWarning == true ? AppTheme.Accent : AppTheme.TextDark);

        Set(_lblNotes, notesWaiting > 0
                ? $"{Plural.Of(notesWaiting, "note")} waiting for you to approve - press Notes."
                : "None waiting.",
            notesWaiting > 0 ? AppTheme.Accent : AppTheme.TextDark);

        // Drivers' own notes (15 Nov 2026): said when one prompt carried a lot
        // of them, so a club can see why a line came out long or loose.
        var heavy = CrewNotesAlert?.Invoke();
        Set(_lblCrewNotes, heavy ?? "Nothing to say.", heavy is not null ? AppTheme.Accent : AppTheme.TextDark);

        Set(_lblVotes, votesIn > 0
                ? $"{Plural.Of(votesIn, "vote")} in so far - press Votes."
                : "No votes in.",
            votesIn > 0 ? AppTheme.Accent : AppTheme.TextDark);

        Set(_lblShow, recapRunning ? "Running - Skip stops it." : "Not running.");

        Set(_lblReplay, replaying
            ? "Playing - Skip stops it."
            : momentHeld
                ? "The last 20 seconds the room heard are ready to play again."
                : "Nothing to replay yet - it needs race recording on and a few seconds of a race.");
    }

    /// <summary>
    /// The automatic caller's next line, in a few words (GapState.DueNext) -
    /// empty when nothing is waiting, which also greys Skip Next Call-up.
    /// </summary>
    internal void ShowGap(string next)
    {
        var waiting = !string.IsNullOrWhiteSpace(next);
        Set(_lblGap, waiting ? next : "Nothing waiting.");

        var canSkip = waiting && next.StartsWith("Call-up for", StringComparison.Ordinal);
        if (_btnSkipGap.Enabled != canSkip)
        {
            _btnSkipGap.Enabled = canSkip;
        }
    }

    /// <summary>
    /// The budget's sentence without its own "Tonight:" - the caption beside
    /// it says that (CommentaryBudget.Describe starts every reading with it).
    /// </summary>
    internal static string Tonight(string described)
    {
        const string Lead = "Tonight: ";

        return described.StartsWith(Lead, StringComparison.Ordinal) && described.Length > Lead.Length
            ? char.ToUpperInvariant(described[Lead.Length]) + described[(Lead.Length + 1)..]
            : described;
    }

    /// <summary>
    /// F1 to F8, and the between-race keys F9, F10 and F12, on to Race
    /// Control (see the raceKey the window was built with) - bare, as
    /// MainForm takes them. Everything else as any form.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is (>= Keys.F1 and <= Keys.F10) or Keys.F12 && _raceKey?.Invoke(keyData) == true)
        {
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// The row is Race Control's, not this window's: it is let go before the
    /// window is disposed, so it is never disposed with it - Race Control
    /// carries it into the next window it builds, and disposes it itself.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _strip.Parent?.Controls.Remove(_strip);
            _strip.ShowsSaying = true;
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// What is on air, across the window. One line, ending in "..." when a
    /// name is too long for it, so the row is always one height and never
    /// has to be laid out again as the name changes - it changes every few
    /// seconds on a race night. (The level meters beside it went on 26 Oct
    /// 2026 - see the class notes.)
    ///
    /// On a card of its own (18 Nov 2026, dialog look), in the title face so
    /// it reads from across the room, with an ON AIR sign before it - lit in
    /// the accent, as the heading is, while something is on air, and unlit
    /// otherwise - and the card's edge lit with it.
    /// </summary>
    private sealed class HeadingRow : Panel
    {
        private const string Sign = "ON AIR";

        private readonly Label _heading;
        private bool _lit;

        public HeadingRow(Label heading)
        {
            _heading = heading;

            _heading.AutoSize = false;
            _heading.AutoEllipsis = true;
            _heading.TextAlign = ContentAlignment.MiddleLeft;

            AutoSize = true;
            BackColor = AppTheme.Surface;
            Tag = AppTheme.KeepBackColor;
            DoubleBuffered = true;
            ResizeRedraw = true;
            Controls.Add(_heading);
        }

        /// <summary>Whether the sign and the card's edge are lit.</summary>
        public bool Lit
        {
            get => _lit;
            set
            {
                if (_lit != value)
                {
                    _lit = value;
                    Invalidate();
                }
            }
        }

        private static int Pad => UiLayout.AtTextSize(12);

        private int RowHeight => _heading.Font.Height + 6 + Pad * 2;

        private Rectangle SignBounds()
        {
            var text = TextRenderer.MeasureText(AppTheme.Spaced(Sign), AppTheme.MicroFont, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            var width = text.Width + UiLayout.AtTextSize(20);
            var height = AppTheme.MicroFont.Height + UiLayout.AtTextSize(10);

            return new Rectangle(Pad + UiLayout.AtTextSize(4), (RowHeight - height) / 2, width, height);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var width = proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : Width;
            return new Size(width, RowHeight);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);

            var left = SignBounds().Right + UiLayout.AtTextSize(14);
            _heading.Bounds = new Rectangle(left, Pad, Math.Max(1, Width - left - Pad), _heading.Font.Height + 6);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;

            using (var behind = new SolidBrush(Parent?.BackColor ?? AppTheme.Background))
            {
                g.FillRectangle(behind, ClientRectangle);
            }

            AppTheme.DrawCard(g, new RectangleF(0, 0, Width, Height), _lit ? AppTheme.Accent : null);

            var sign = SignBounds();
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using var path = AppTheme.RoundedRect(RectangleF.Inflate(sign, -0.5f, -0.5f), Math.Max(2, sign.Height / 2));

            if (_lit)
            {
                using var fill = new SolidBrush(AppTheme.Accent);
                g.FillPath(fill, path);
            }
            else
            {
                using var ring = new Pen(AppTheme.Border, Math.Max(1f, UiLayout.AtTextSize(1.5f)));
                g.DrawPath(ring, path);
            }

            TextRenderer.DrawText(g, AppTheme.Spaced(Sign), AppTheme.MicroFont, sign,
                _lit ? AppTheme.OnAccent : AppTheme.TextMuted,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Text and colour, set only when they change - this runs once a second.</summary>
    private static void Set(Label label, string text, Color? colour = null)
    {
        if (!string.Equals(label.Text, text, StringComparison.Ordinal))
        {
            label.Text = text;
        }

        var wanted = colour ?? AppTheme.TextDark;

        if (label.ForeColor != wanted)
        {
            label.ForeColor = wanted;
        }
    }

    private static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = AppTheme.SmallFont,
        ForeColor = AppTheme.TextMuted,
    };
}
