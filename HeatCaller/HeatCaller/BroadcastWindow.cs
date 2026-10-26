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
/// On the layout pieces: what is on air across the top in the heading face,
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

    private readonly Label _lblAnnouncer = new();
    private readonly Label _lblLast = new();
    private readonly Label _lblTonight = new();
    private readonly Label _lblThisRace = new();
    private readonly Label _lblNotes = new();
    private readonly Label _lblVotes = new();
    private readonly Label _lblShow = new();
    private readonly Label _lblReplay = new();

    /// <param name="strip">Race Control's own row of broadcast controls, moved in here.</param>
    /// <param name="raceKey">
    /// Race Control's flag keys (F1 to F8), for a key pressed while this
    /// window has the focus - it is a form of its own, so MainForm's keys do
    /// not reach it. True when the key was one of them.
    /// </param>
    internal BroadcastWindow(AnnouncerStrip strip, Func<Keys, bool>? raceKey = null)
    {
        _strip = strip;
        _raceKey = raceKey;

        Text = "Broadcast";
        Font = AppTheme.BodyFont;

        _lblOnAir.Font = AppTheme.HeadingFont;

        // The row's own "On air" label would say the heading again, a line
        // below it.
        _strip.ShowsSaying = false;

        var controls = new Section("Controls");
        controls.Add(_strip, stretch: false, gapBelow: UiLayout.HintGap);
        controls.Add(Hint("Hold stops the next thing; Skip stops the one on air. Play again puts the last segment out "
                          + "again, and Replay Moment the last 20 seconds the room heard. Click the count at the end of the "
                          + "row for each line the announcer didn't say."), gapBelow: 0);

        var glance = new Section("At a glance");
        var grid = new FieldGrid();

        foreach (var (caption, value) in new[]
                 {
                     ("Announcer:", _lblAnnouncer),
                     ("Last segment:", _lblLast),
                     ("Tonight:", _lblTonight),
                     ("This race:", _lblThisRace),
                     ("Club notes:", _lblNotes),
                     ("Room vote:", _lblVotes),
                     ("Recap show:", _lblShow),
                     ("Replay Moment:", _lblReplay),
                 })
        {
            value.AutoSize = true;
            value.MaximumSize = new Size(UiLayout.AtTextSize(WindowWidth - 200), 0);
            grid.AddRow(caption, value);
        }

        glance.Add(grid, gapBelow: 0);

        _btnClose.Click += (_, _) => Close();

        var page = new FillPage();
        var body = DialogFit.Body(page);
        body.Add(new HeadingRow(_lblOnAir), gapBelow: UiLayout.RowGap);
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
        _lblOnAir.Font = AppTheme.HeadingFont;
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
    /// F1 to F8 on to Race Control (see the raceKey the window was built
    /// with) - bare, as MainForm takes them. Everything else as any form.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is >= Keys.F1 and <= Keys.F8 && _raceKey?.Invoke(keyData) == true)
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
    /// </summary>
    private sealed class HeadingRow : Panel
    {
        private readonly Label _heading;

        public HeadingRow(Label heading)
        {
            _heading = heading;

            _heading.AutoSize = false;
            _heading.AutoEllipsis = true;
            _heading.TextAlign = ContentAlignment.MiddleLeft;

            AutoSize = true;
            Controls.Add(_heading);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var width = proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : Width;
            return new Size(width, _heading.Font.Height + 6);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);

            _heading.Bounds = new Rectangle(0, 0, Math.Max(1, Width), _heading.Font.Height + 6);
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
