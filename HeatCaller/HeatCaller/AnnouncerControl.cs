namespace HeatCaller;

/// <summary>
/// A view for whoever is on the microphone.
///
/// Deliberately not another leaderboard. The Big Screen already shows the
/// audience where everyone is; an announcer's problem is different - they
/// need things to SAY, and they need them without reading a grid mid-race.
///
/// So this leads with generated talking points (gaps closing, streaks
/// running, someone's personal best), keeps the running order to a glanceable
/// few, and lets one driver be pinned for a story to follow.
/// </summary>
public sealed class AnnouncerControl : UserControl
{
    private readonly RaceControlSession _race;
    private readonly ProfileStore _profiles;
    private readonly SessionHistoryStore _history;
    private readonly DayScheduleStore _schedule;
    private readonly CommentaryService? _commentary;
    private readonly AppSettings? _settings;

    /// <summary>Latest LLM-suggested line, shown at the top of the list. Null until one arrives.</summary>
    private string? _suggestedLine;

    /// <summary>Guards against stacking requests - the refresh runs twice a second and a model call takes seconds.</summary>
    private bool _suggestionInFlight;

    private DateTime _lastSuggestionUtc = DateTime.MinValue;

    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 500 };

    /// <summary>The race's state, as tall and as wide as the longest thing it can say - so the clock beside it never moves as it changes.</summary>
    private readonly SteadyLabel _lblState = new();

    /// <summary>The race clock, as wide as a race of a hundred minutes or more needs - so it never moves anything as it counts.</summary>
    private readonly SteadyLabel _lblClock = new();

    private readonly DataGridView _gridOrder = new();
    private readonly ListBox _lstTalkingPoints = new();
    private readonly ComboBox _cmbSpotlight = new();
    private readonly Label _lblSpotlight = new();
    private readonly Label _lblOnDeck = new();
    private readonly Button _btnRepeat = new();
    private readonly Label _lblLastSummary = new();

    /// <summary>The heading over the last summary, which says what kind of line it is - see RefreshRepeatButton. As tall as the longest it can say.</summary>
    private readonly SteadyLabel _lblSummaryCaption = new();

    /// <summary>Events worth mentioning, newest first. Capped so it stays readable at a glance.</summary>
    private readonly List<string> _feed = new();
    private const int MaxFeed = 40;
    private readonly AnnouncementService _announcer;

    /// <summary>For Clear Points. Optional so an older host still builds; the clear then isn't undoable.</summary>
    private readonly UndoService? _undo;

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The page the rest is laid out on - see LeastHeight.</summary>
    private readonly FillPage _page = new();

    /// <summary>
    /// The two bands of cards: the running order beside the talking points,
    /// and the spotlight beside the last summary. Both are shared out by
    /// SideWidth, so their columns line up. Held so a picker grown by a new
    /// driver list can have them share out again (SizeFields).
    /// </summary>
    private Beside? _topBand;
    private Beside? _bottomBand;

    /// <summary>The cards whose captions a column is never narrower than - see SideWidth.</summary>
    private Section? _orderCard;
    private Section? _talkCard;
    private Section? _spotlightCard;

    /// <summary>
    /// The state's and the clock's own faces, made by this page - see
    /// MakeOwnFonts. Made again at a new text size, and the old ones
    /// disposed; the last ones go with the page (Dispose).
    /// </summary>
    private Font? _stateFont;
    private Font? _clockFont;

    /// <summary>The spotlight picker's width, measured from its names - see Pieces.PickerWidth. Forgotten whenever the list is refilled.</summary>
    private (Font Font, int Count, int Width)? _spotlightWidth;

    /// <summary>The state's and the clock's sizes in points, at the normal text size.</summary>
    private const float StatePoints = 16F;
    private const float ClockPoints = 26F;

    /// <summary>The spotlight picker's width at the normal text size, which it was before it was measured - it is never narrower.</summary>
    private const int SpotlightPickerLeast = 250;

    /// <summary>The least the running order and the talking points can do with, in rows - below that the page scrolls instead (FillPage).</summary>
    private const int OrderLeastRows = 3;
    private const int TalkingPointsLeastRows = 3;

    /// <summary>
    /// Lines kept for the spotlight and the last summary whatever they say,
    /// so the cards do not hop as they are rewritten - the spotlight twice a
    /// second during a race. The spotlight's are its five kinds of line
    /// (standing, last three, personal best, car, note); four is a talking
    /// point or a short summary. Longer, and they grow.
    /// </summary>
    private const int SpotlightLines = 5;
    private const int SummaryLines = 4;

    /// <summary>The Repeat button's two captions - it says which line it will speak. The column it is in is wide enough for either (SideWidth).</summary>
    private const string RepeatSummaryCaption = "Repeat Race Summary";
    private const string RepeatLineCaption = "Repeat Last Line";

    private const string ClearPointsCaption = "Clear Points";

    /// <summary>The heading over a race summary. A talking point shown there instead has its own - see RefreshRepeatButton.</summary>
    private const string SummaryCaption = "LAST RACE SUMMARY";

    public AnnouncerControl(RaceControlSession race, ProfileStore profiles,
        SessionHistoryStore history, DayScheduleStore schedule, AnnouncementService announcer,
        CommentaryService? commentary = null, AppSettings? settings = null, UndoService? undo = null)
    {
        _undo = undo;
        _commentary = commentary;
        _settings = settings;
        _race = race;
        _profiles = profiles;
        _history = history;
        _schedule = schedule;
        _announcer = announcer;

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize.
        //
        // No Size or MinimumSize. The page used to insist on 900x560 whatever
        // its window, which WinForms honours over the size it is given - so
        // a shorter window cut the bottom of it off. A short window scrolls
        // the page instead (FillPage), and the window is never made shorter
        // than the page's least (AnnouncerForm).
        _textSize.Wear(this, () => AppTheme.BodyFont);
        ForeColor = AppTheme.TextDark;

        BuildLayout();

        // The announcer service already decides what's noteworthy, so its
        // spoken lines double as the feed rather than duplicating that logic
        // with a second set of rules that could disagree.
        announcer.AnnouncementTextReady += OnAnnouncementText;

        _race.JumpStartDetected += (transponder, penaltyMs) =>
            AddFeedLine($"JUMP START - {NameFor(transponder)}"
                        + (penaltyMs > 0 ? $" (+{penaltyMs / 1000.0:0.0}s)" : " (flagged)"));

        _refresh.Tick += (_, _) => RefreshLive();
        _refresh.Start();

        RefreshAll();
    }

    /// <summary>The page's own face changed - handed the body face at a new text size - so everything else it keeps in step follows. See TextSize.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>A new text size, reaching this page through its window's font - the only way it does, since the page wears a face of its own. See TextSize.</summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>
    /// Builds the page on the layout pieces (LayoutPieces.cs). Top to
    /// bottom:
    ///
    ///   - A card of what is happening now: the state and the clock in their
    ///     own big faces, Race Control against the right-hand edge, and
    ///     what is on deck under them, wrapping when it runs long.
    ///   - The running order beside the talking points, in two equal
    ///     columns taking the height that is left - the grid and the list
    ///     filling their cards, never shorter than three rows each (below
    ///     that the page scrolls), Clear Points under the list it clears.
    ///   - The spotlight beside the last summary, in the same two columns,
    ///     the two cards ending level: the driver picker and their story;
    ///     the summary's heading, the Repeat button and the line it speaks.
    ///
    /// The columns are shared out equally, as the page always had them,
    /// but never narrower than the picker's longest name or the Repeat
    /// button's longer caption need (SideWidth). Nothing is placed by hand,
    /// and a window too short for all of it scrolls the page.
    ///
    /// It used to be placed in pixels: two 470x300 boxes side by side at
    /// fixed positions under the clock, the buttons flowed along one row
    /// from x=380, and the spotlight and the summary shifted down by
    /// however far that row turned out to reach - all of it inside a
    /// 900x560 floor the page kept whatever its window, which was taller
    /// than a 1280x720 screen has room for.
    /// </summary>
    private void BuildLayout()
    {
        // --- Now: the state, the clock and what is on deck ---
        //
        // Each as wide and as tall as the longest thing it can say
        // (SteadyLabel), so the clock never shifts as the state changes and
        // nothing shifts as the clock counts. In faces the page makes itself,
        // at the text size in use - see MakeOwnFonts.
        _lblState.Wordings = Enum.GetValues<FlagState>()
            .Select(flag => $"{flag.ToString().ToUpperInvariant()} FLAG")
            .Concat(new[] { "COUNTDOWN", "STAGING", "FINISHED", "IDLE" })
            .ToArray();

        // Minutes are not wrapped into hours (RefreshLive), so a practice
        // left running past a hundred minutes has a third digit.
        _lblClock.Wordings = new[] { "00:00", "000:00" };

        // Centred up and down on the line they share, as FieldGrid.Inline
        // has anything that is not a button.
        _lblState.Anchor = AnchorStyles.Left;
        _lblClock.Anchor = AnchorStyles.Left;

        _textSize.Keep(MakeOwnFonts);

        // One line kept whatever it says, so the cards under it do not hop
        // when the schedule runs out and the line empties.
        _lblOnDeck.AutoSize = true;
        _lblOnDeck.ForeColor = AppTheme.Accent;
        _textSize.Wear(_lblOnDeck, () => AppTheme.BoldFont);
        _textSize.ReserveLines(_lblOnDeck, 1);

        // --- Running order, kept short on purpose ---
        _gridOrder.ReadOnly = true;
        _gridOrder.AllowUserToAddRows = false;
        _gridOrder.RowHeadersVisible = false;
        _gridOrder.MultiSelect = false;
        _gridOrder.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _gridOrder.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _gridOrder.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _gridOrder.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "pos", HeaderText = "", FillWeight = 18 },
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 90 },
            new DataGridViewTextBoxColumn { Name = "laps", HeaderText = "Laps", FillWeight = 28 },
            new DataGridViewTextBoxColumn { Name = "gap", HeaderText = "Gap", FillWeight = 40 },
            new DataGridViewTextBoxColumn { Name = "last", HeaderText = "Last", FillWeight = 40 }
        );
        _gridOrder.SelectionChanged += (_, _) =>
        {
            if (_gridOrder.CurrentRow?.Tag is string transponder)
            {
                SelectSpotlight(transponder);
            }
        };

        // --- Talking points ---
        _lstTalkingPoints.BackColor = AppTheme.Surface;
        _lstTalkingPoints.IntegralHeight = false;

        // A ListBox truncates anything wider than the control - it has no
        // wrapping of its own. Owner-drawing with variable height lets each
        // row measure and wrap its own text, which matters most for the
        // generated commentary line at the top: that's the one someone's
        // meant to read out loud, and it's also the longest.
        _lstTalkingPoints.DrawMode = DrawMode.OwnerDrawVariable;
        _lstTalkingPoints.MeasureItem += TalkingPoints_MeasureItem;
        _lstTalkingPoints.DrawItem += TalkingPoints_DrawItem;

        // Variable-height rows are measured once, when added - so a resize
        // would otherwise leave every existing row at its old height and
        // re-truncate the text at the new width. Re-running the refresh
        // re-adds the items, which re-measures them.
        _lstTalkingPoints.Resize += (_, _) => RefreshTalkingPoints();

        var btnClear = new Button { Text = ClearPointsCaption };
        btnClear.Click += (_, _) =>
        {
            if (_feed.Count == 0)
            {
                return;
            }

            // The feed lives only on this page - there is no store behind
            // it - so the undo has to put it back here. It says so plainly if
            // the page has gone rather than appearing to work.
            var cleared = _feed.ToList();
            _undo?.Capture($"Clear {Plural.Of(cleared.Count, "talking point")}", () =>
            {
                if (IsDisposed || Disposing)
                {
                    throw new InvalidOperationException("The Announcer screen has been closed, so its talking points can't be put back.");
                }

                // Behind anything said since the clear - they're older.
                _feed.AddRange(cleared);
                if (_feed.Count > MaxFeed)
                {
                    _feed.RemoveRange(MaxFeed, _feed.Count - MaxFeed);
                }

                RefreshTalkingPoints();
            });

            _feed.Clear();
            RefreshTalkingPoints();
        };

        // --- Spotlight ---
        //
        // As wide as its longest name (SizeFields), and the story under it
        // with its five kinds of line kept, so the card does not hop as the
        // race moves the driver's standing on.
        _cmbSpotlight.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbSpotlight.SelectedIndexChanged += (_, _) => RefreshSpotlight();

        _lblSpotlight.AutoSize = true;
        _lblSpotlight.ForeColor = AppTheme.TextDark;
        _textSize.Wear(_lblSpotlight, () => AppTheme.BodyFont);
        _textSize.ReserveLines(_lblSpotlight, SpotlightLines);

        // --- Last race summary ---
        //
        // Its heading is not the card's own: it says what KIND of line is
        // shown under it (RefreshRepeatButton), so it is a label the page
        // rewrites, dressed as a card's caption is - the small face, muted,
        // spaced when short (CardCaption).
        _lblSummaryCaption.ForeColor = AppTheme.TextMuted;
        _lblSummaryCaption.UseMnemonic = false;
        _lblSummaryCaption.Wordings = new[]
        {
            // The kinds CommentaryService records a line as.
            SummaryCaption, "LAST LINE (INTRO)", "LAST LINE (CLOSING)", "LAST LINE (TALKING POINT)", "LAST LINE (SUMMARY)",
        }.Select(caption => CardCaption(caption)).ToArray();
        _textSize.Wear(_lblSummaryCaption, () => AppTheme.MicroFont);
        SetSummaryCaption(SummaryCaption);

        _lblLastSummary.AutoSize = true;
        _lblLastSummary.ForeColor = AppTheme.TextMuted;
        _textSize.Wear(_lblLastSummary, () => AppTheme.BodyFont);
        _textSize.ReserveLines(_lblLastSummary, SummaryLines);

        _btnRepeat.Text = RepeatSummaryCaption;
        _btnRepeat.Enabled = false;
        _btnRepeat.Click += (_, _) =>
        {
            // Repeats the stored text rather than asking the model again -
            // regenerating could come back with different wording, or fail
            // outright if the model's since gone offline, neither of which
            // is what "repeat that" means.
            //
            // Prefers the race summary over whatever was generated most
            // recently: talking points regenerate every 20 seconds during a
            // race, so "most recent" would almost never be the summary by
            // the time anyone wants it repeated.
            var summary = _commentary?.LastRaceSummary;
            if (summary is { Length: > 0 } && _commentary is not null)
            {
                // Same tags and delivery as the automatic recap. The repeat
                // used to send the bare text with the profile's normal
                // voice settings, so it sounded different from the original.
                (double, double)? delivery = _settings?.SummaryUseBroadcastVoice == true
                    ? (_settings.SummaryVoiceStability, _settings.SummaryVoiceStyle)
                    : null;

                _announcer.SpeakReminder(_commentary.PrepareSummaryForSpeech(summary), delivery);
                return;
            }

            var line = _commentary?.LastLine;
            if (line is { Length: > 0 })
            {
                _announcer.SpeakReminder(line);
            }
        };

        // The other half of the pair - see RaceControlControl.
        var btnRaceControl = new Button { Text = "Race Control" };
        btnRaceControl.Click += (_, _) => RaceControlRequested?.Invoke();

        // --- The cards ---
        //
        // Now: no caption - the state is its heading. The state and the
        // clock on the left of one line and Race Control against the right,
        // Race Control going under them when the line is too narrow for both
        // (SplitBar).
        var now = new Section();

        now.Add(new SplitBar(
            new ButtonBar().With(_lblState, _lblClock),
            new ButtonBar(alignRight: true).With(btnRaceControl)));
        now.Add(_lblOnDeck, gapBelow: 0);

        var order = new Section("Running order");

        order.AddFill(_gridOrder, LeastOrderHeight, gapBelow: 0);

        var talk = new Section("Talking points");

        talk.AddFill(_lstTalkingPoints, LeastTalkingPointsHeight);
        talk.Add(new ButtonBar().With(btnClear), gapBelow: 0);

        var spotlight = new Section("Spotlight");

        spotlight.Add(_cmbSpotlight, stretch: false);
        Pieces.KeepHeightTold(_cmbSpotlight);
        spotlight.Add(_lblSpotlight, gapBelow: 0);

        // The summary sits directly under the Repeat button that speaks it,
        // so it's obvious what pressing that button will say - and the
        // button stays put however long the summary runs.
        var summaryCard = new Section();

        summaryCard.Add(_lblSummaryCaption, gapBelow: UiLayout.HeadingGap);
        summaryCard.Add(new ButtonBar().With(_btnRepeat));
        summaryCard.Add(_lblLastSummary, gapBelow: 0);

        // Before the bands, which ask for them as they are put together.
        _orderCard = order;
        _talkCard = talk;
        _spotlightCard = spotlight;

        // Each pair of cards ends level: the left one fills its column, the
        // right one takes whatever height its column has.
        var top = Band(sizesItself: false);

        top.AddFill(order, order.LeastHeight);
        top.AddSideFill(talk, talk.LeastHeight, gapBelow: 0);

        var bottom = Band(sizesItself: true);

        bottom.AddFill(spotlight, spotlight.LeastHeight);
        bottom.AddSideFill(summaryCard, summaryCard.LeastHeight, gapBelow: 0);

        _topBand = top;
        _bottomBand = bottom;

        // --- The page ---
        _page.Add(now);
        _page.AddFill(top, top.LeastHeight);
        _page.Add(bottom, gapBelow: 0);

        // Race Control last from the keyboard, as it was on the row of
        // buttons it shared with Repeat and Clear Points - it leaves this
        // window, the rest act on it.
        now.TabIndex = bottom.TabIndex + 1;

        // The page ends at its last card. The theme gives a page that
        // scrolls room under its last control; in a window of its own that
        // is a blank band the window has to be taller for, which on a
        // 1280x720 screen is room the running order does not get.
        ScrollTheming.NoRoomBelow(_page);

        Controls.Add(_page);

        // Once everything is on the page and wears its face - what they are
        // measured in.
        //
        // Every heading of the running order whole, at the text size in
        // use, and the position, laps, gap and last lap wide enough for
        // their values - the driver's name gives way first. See
        // Pieces.KeepHeadingsWhole.
        _textSize.Keep(() => Pieces.KeepHeadingsWhole(_gridOrder, OrderWidest));
        _textSize.Keep(SizeFields);
    }

    /// <summary>
    /// The least the page can do with at <paramref name="width"/>: what is
    /// on now, the running order's headings and three drivers beside three
    /// talking points and Clear Points, and the spotlight beside the last
    /// summary - for the Announcer window, which is never made shorter than
    /// this (AnnouncerForm). Shorter than that, the page scrolls.
    /// </summary>
    internal int LeastHeight(int width) => _page.LeastHeight(width);

    /// <summary>
    /// A band of two cards side by side, shared out by SideWidth. The top
    /// one takes the height the page has left; the bottom one is as tall as
    /// its taller card needs (it sizes itself).
    /// </summary>
    private Beside Band(bool sizesItself)
    {
        Beside? band = null;

        band = new Beside(() => SideWidth(band?.Width ?? 0));

        if (sizesItself)
        {
            band.AutoSize = true;
            band.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }

        return band;
    }

    /// <summary>
    /// The right-hand column of both bands - the talking points and the
    /// last summary. Half of the band, as the page always had it: two equal
    /// columns. But never so wide that the left-hand cards get less than
    /// the spotlight picker's longest name, the running order's headings or
    /// their captions need; and never narrower itself than the Repeat
    /// button's longer caption, Clear Points or its captions need. Too
    /// narrow for both, and each gives up the same share of its width, as
    /// Batteries' two cards do. Nothing here scrolls sideways (FillPage).
    ///
    /// The picker's width as it is meant to be, not as it is: a table cuts
    /// a field down to the column it has.
    /// </summary>
    private int SideWidth(int band)
    {
        // What the two share: all of the band but the gap a Beside leaves
        // between its parts and its column.
        var room = Math.Max(0, band - UiLayout.RowGap);

        var left = Math.Max(
            Math.Max(SpotlightPickerWidth(), GridHeadingsWidth()),
            Math.Max(_orderCard?.CaptionWidth ?? 0, _spotlightCard?.CaptionWidth ?? 0))
            + Section.Inset.Horizontal;

        // A button in a bar keeps a gap beside it, which the bar counts when
        // it decides whether the button fits on its line.
        var buttons = Math.Max(
            Math.Max(UiLayout.WidthFor(RepeatSummaryCaption), UiLayout.WidthFor(RepeatLineCaption)),
            UiLayout.WidthFor(ClearPointsCaption)) + UiLayout.ButtonGap;

        var right = Math.Max(
            buttons,
            Math.Max(_talkCard?.CaptionWidth ?? 0, _lblSummaryCaption.GetPreferredSize(Size.Empty).Width))
            + Section.Inset.Horizontal;

        var side = room / 2;

        if (room - side < left)
        {
            side = room - left;
        }

        side = Math.Max(side, right);

        if (left + right > room)
        {
            side = (int)((long)right * room / Math.Max(1, left + right));
        }

        return Math.Max(0, side);
    }

    /// <summary>
    /// The widest value each of the running order's short columns shows:
    /// a place to the nineties, laps to the hundreds, a gap as RaceGaps
    /// writes one ("+12.34", or "+3L"), and a lap time as RefreshOrder
    /// writes it. See Pieces.KeepHeadingsWhole.
    /// </summary>
    private static readonly (string Column, string Widest)[] OrderWidest =
    {
        ("pos", "99"),
        ("laps", "999"),
        ("gap", "+99.99"),
        ("last", "99.999"),
    };

    /// <summary>The running order's columns at their least, side by side - each heading whole and each short column's widest value (Pieces.KeepHeadingsWhole).</summary>
    private int GridHeadingsWidth() =>
        _gridOrder.Columns
            .Cast<DataGridViewColumn>()
            .Sum(column => Pieces.ColumnLeast(_gridOrder, column, OrderWidest));

    /// <summary>The spotlight picker: as wide as its longest name, and never narrower than it was at the normal text size.</summary>
    private int SpotlightPickerWidth() =>
        Math.Max(UiLayout.AtTextSize(SpotlightPickerLeast), Pieces.PickerWidth(_cmbSpotlight, ref _spotlightWidth));

    /// <summary>
    /// The fields' sizes at the text size in use: the spotlight picker as
    /// wide as its longest name. Run again when the text size changes
    /// (TextSize.Keep) and when the list is refilled (RefreshSpotlightList).
    /// </summary>
    private void SizeFields()
    {
        var width = SpotlightPickerWidth();

        // Only when it changed: a new width lays the card out again.
        if (_cmbSpotlight.Width == width)
        {
            return;
        }

        _cmbSpotlight.Width = width;

        // The columns are shared out from it (SideWidth), and a band only
        // asks when it is laid out - both, so the two stay lined up.
        _topBand?.PerformLayout();
        _bottomBand?.PerformLayout();
    }

    /// <summary>
    /// The state's and the clock's faces: the state in the body's family,
    /// bold, and the clock in Consolas, bold, so its digits do not shift as
    /// they count - at the sizes they always had, times the text size.
    /// Made again only when the text size has changed, the old one disposed
    /// once the label wears the new.
    /// </summary>
    private void MakeOwnFonts()
    {
        var scale = AppTheme.FontScale;

        if (_stateFont is null || Math.Abs(_stateFont.SizeInPoints - StatePoints * scale) > 0.01F)
        {
            var old = _stateFont;

            _stateFont = new Font(AppTheme.BodyFont.FontFamily, StatePoints * scale, FontStyle.Bold);
            _lblState.Font = _stateFont;
            old?.Dispose();
        }

        if (_clockFont is null || Math.Abs(_clockFont.SizeInPoints - ClockPoints * scale) > 0.01F)
        {
            var old = _clockFont;

            _clockFont = new Font("Consolas", ClockPoints * scale, FontStyle.Bold);
            _lblClock.Font = _clockFont;
            old?.Dispose();
        }
    }

    /// <summary>The least the running order can do with: its headings and three drivers. Below that the page scrolls instead - see FillPage.</summary>
    private int LeastOrderHeight() =>
        _gridOrder.ColumnHeadersHeight + OrderLeastRows * _gridOrder.RowTemplate.Height + 2;

    /// <summary>
    /// The least the talking points can do with: three one-line points, as
    /// TalkingPoints_MeasureItem sizes a row - its line and six pixels
    /// round it.
    /// </summary>
    private int LeastTalkingPointsHeight() =>
        TalkingPointsLeastRows * (Pieces.LineHeight(_lstTalkingPoints.Font) + 6) + 4;

    /// <summary>A heading as a card draws its own (Section): spaced capitals when it is short enough for the spacing to help.</summary>
    private static string CardCaption(string text) => text.Length <= 28 ? AppTheme.Spaced(text) : text;

    /// <summary>The summary's heading, dressed as a card's caption - and named plainly for a screen reader, which would spell the spaced one out.</summary>
    private void SetSummaryCaption(string text)
    {
        _lblSummaryCaption.Text = CardCaption(text);

        if (_lblSummaryCaption.AccessibleName != text)
        {
            _lblSummaryCaption.AccessibleName = text;
        }
    }

    /// <summary>
    /// Raised when Race Control is wanted. MainForm owns the navigation and
    /// wires this to it - this control does not know what pages exist.
    /// </summary>
    public event Action? RaceControlRequested;

    public void RefreshAll()
    {
        RefreshSpotlightList();
        RefreshLive();
    }

    private void OnAnnouncementText(string text)
    {
        if (InvokeRequired)
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(() => AddFeedLine(text));
            }
            return;
        }

        AddFeedLine(text);
    }

    private void AddFeedLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _feed.Insert(0, $"{AppTime.TimeOfDay(DateTime.Now)}   {text}");

        if (_feed.Count > MaxFeed)
        {
            _feed.RemoveRange(MaxFeed, _feed.Count - MaxFeed);
        }

        RefreshTalkingPoints();
    }

    /// <summary>
    /// Measures each row against the list's real width so its height fits
    /// the wrapped text. Without this the ListBox gives every row one line
    /// and silently cuts off anything longer - which hit the generated
    /// commentary line hardest, since that's both the longest and the one
    /// actually meant to be read aloud.
    /// </summary>
    private void TalkingPoints_MeasureItem(object? sender, MeasureItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _lstTalkingPoints.Items.Count)
        {
            return;
        }

        var text = _lstTalkingPoints.Items[e.Index]?.ToString() ?? string.Empty;

        // Leave room for the scrollbar, which would otherwise overlap the
        // last few characters of every wrapped line.
        var width = _lstTalkingPoints.ClientSize.Width - 24;

        var size = TextRenderer.MeasureText(
            e.Graphics, text, _lstTalkingPoints.Font,
            new Size(Math.Max(40, width), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

        e.ItemHeight = Math.Max(_lstTalkingPoints.Font.Height + 4, size.Height + 6);
        e.ItemWidth = width;
    }

    private void TalkingPoints_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _lstTalkingPoints.Items.Count)
        {
            return;
        }

        var text = _lstTalkingPoints.Items[e.Index]?.ToString() ?? string.Empty;

        e.DrawBackground();

        // Generated commentary is quoted and sits first; computed
        // observations are prefixed with "*". Colouring them differently
        // keeps the distinction visible now that rows are variable height
        // and no longer line up as a uniform list.
        var color = text.StartsWith('\u201C') ? AppTheme.Primary
            : text.StartsWith('*') ? AppTheme.TextDark
            : AppTheme.TextMuted;

        var bounds = Rectangle.Inflate(e.Bounds, -6, -3);
        TextRenderer.DrawText(
            e.Graphics, text, e.Font ?? _lstTalkingPoints.Font, bounds, color,
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

        e.DrawFocusRectangle();
    }

    private void RefreshTalkingPoints()
    {
        _lstTalkingPoints.BeginUpdate();
        _lstTalkingPoints.Items.Clear();

        // The LLM's suggested phrasing goes first when there is one - it's
        // the thing most likely to be read out verbatim. Marked distinctly
        // so it's never mistaken for a computed fact: the observations below
        // are measured, this one is written.
        if (!string.IsNullOrWhiteSpace(_suggestedLine))
        {
            _lstTalkingPoints.Items.Add($"\u201C{_suggestedLine}\u201D");
        }

        // Generated observations first - they're the ones that need saying
        // NOW, where the feed below is a record of what already happened.
        foreach (var point in GenerateObservations())
        {
            _lstTalkingPoints.Items.Add($"* {point}");
        }

        foreach (var line in _feed)
        {
            _lstTalkingPoints.Items.Add(line);
        }

        _lstTalkingPoints.EndUpdate();
    }

    /// <summary>
    /// Reads the current standings for things worth mentioning right now.
    ///
    /// Kept to a handful: an announcer scanning a list of twenty
    /// observations mid-race is no better off than reading the grid.
    /// </summary>
    private List<string> GenerateObservations()
    {
        var points = new List<string>();

        if (_race.State != RaceState.Racing)
        {
            return points;
        }

        var ordered = _race.GetOrderedStandings();
        if (ordered.Count == 0)
        {
            return points;
        }

        // A close fight for the lead is the single most useful thing to know.
        // Gaps measured at the line (see RaceGaps), not by comparing lap
        // counts - which differ for everyone the moment the leader crosses.
        if (ordered.Count >= 2 && RaceGaps.Between(ordered[1], ordered[0]) is { LapsDown: 0, GapMs: { } leadGapMs } && leadGapMs < 2000)
        {
            points.Add($"{NameFor(ordered[0].TransponderId)} leads {NameFor(ordered[1].TransponderId)} by only {leadGapMs / 1000.0:0.00}s");
        }

        // Anyone actually lapped.
        if (ordered.Count >= 2 && RaceGaps.Between(ordered[^1], ordered[0]).LapsDown is var lapsDown and >= 1)
        {
            points.Add($"{NameFor(ordered[^1].TransponderId)} is {lapsDown} lap{(lapsDown == 1 ? "" : "s")} down");
        }

        // Who holds the race's fastest lap.
        if (_race.RaceFastestLapMs > 0 && !string.IsNullOrWhiteSpace(_race.RaceFastestLapTransponder))
        {
            points.Add($"Fastest lap: {NameFor(_race.RaceFastestLapTransponder)} at {_race.RaceFastestLapMs / 1000.0:0.000}s");
        }

        // Close battles further down, which are easy to miss.
        for (var i = 1; i < ordered.Count - 1 && points.Count < 6; i++)
        {
            if (RaceGaps.Between(ordered[i + 1], ordered[i]) is not { LapsDown: 0, GapMs: { } battleGapMs })
            {
                continue;
            }

            var gap = battleGapMs / 1000.0;
            if (gap < 1.0)
            {
                points.Add($"P{i + 1} battle: {NameFor(ordered[i].TransponderId)} and {NameFor(ordered[i + 1].TransponderId)} split by {gap:0.00}s");
            }
        }

        return points;
    }

    private void RefreshLive()
    {
        if (IsDisposed || Disposing || !Visible)
        {
            return;
        }

        _lblState.Text = _race.State switch
        {
            RaceState.Racing => $"{_race.Flag.ToString().ToUpperInvariant()} FLAG",
            RaceState.Countdown => "COUNTDOWN",
            RaceState.Staging => "STAGING",
            RaceState.Finished => "FINISHED",
            _ => "IDLE",
        };
        _lblState.ForeColor = _race.Flag switch
        {
            FlagState.Green => AppTheme.Success,
            FlagState.Yellow => AppTheme.Accent,
            FlagState.Red => AppTheme.Danger,
            _ => AppTheme.TextMuted,
        };

        var elapsed = _race.ElapsedRaceTime;
        _lblClock.Text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";

        var next = _schedule.All.FirstOrDefault(e => !e.IsComplete);
        _lblOnDeck.Text = next is null
            ? string.Empty
            : $"ON DECK:  {next.ScheduledTime}  {next.ClassName}  {next.Description}".TrimEnd();

        RefreshOrder();
        RefreshTalkingPoints();
        RefreshSpotlight();
        MaybeRequestSuggestion();
        RefreshRepeatButton();
    }

    /// <summary>Keeps the Repeat button's state, label and displayed text matched to whatever CommentaryService last generated.</summary>
    private void RefreshRepeatButton()
    {
        // Mirrors the click handler's own preference exactly, so the text
        // shown below can never differ from the line that gets spoken.
        var summary = _commentary?.LastRaceSummary;
        var line = summary ?? _commentary?.LastLine;

        _btnRepeat.Enabled = !string.IsNullOrWhiteSpace(line);

        // The label tracks what it will actually say - falling back to the
        // generic wording when no summary exists yet avoids promising a
        // race summary it can't deliver.
        _btnRepeat.Text = summary is { Length: > 0 } ? RepeatSummaryCaption : RepeatLineCaption;

        // Shows the same line the button speaks, so what you'll hear is
        // visible at a glance. The caption tracks which KIND of line
        // it is, since the button falls back to the last talking point when
        // no summary exists - and a heading saying "race summary" over a
        // talking point would be quietly wrong.
        if (line is { Length: > 0 } shown)
        {
            _lblLastSummary.Text = $"\u201C{shown}\u201D";
            _lblLastSummary.ForeColor = summary is { Length: > 0 } ? AppTheme.TextDark : AppTheme.TextMuted;
            SetSummaryCaption(summary is { Length: > 0 }
                ? SummaryCaption
                : $"LAST LINE ({_commentary?.LastLineKind?.ToUpperInvariant()})");
        }
        else
        {
            SetSummaryCaption(SummaryCaption);
            _lblLastSummary.Text = _commentary?.IsAvailable == true
                ? "No race summary yet - one is generated after the checkered flag."
                : "Commentary is off. Enable it on the Commentary page.";
            _lblLastSummary.ForeColor = AppTheme.TextMuted;
        }
    }

    /// <summary>
    /// Asks the model for a suggested line, at most every 20 seconds and
    /// never while one is already in flight.
    ///
    /// Both guards matter: this method runs twice a second, and a local
    /// model takes seconds to answer - without them, requests would pile up
    /// far faster than they complete. Fire-and-forget by design, since
    /// nothing on screen should wait on it.
    /// </summary>
    private void MaybeRequestSuggestion()
    {
        if (_commentary is null
            || !_commentary.IsAvailable
            || _settings?.CommentaryTalkingPoints != true
            || _race.State != RaceState.Racing
            || _suggestionInFlight
            || (DateTime.UtcNow - _lastSuggestionUtc).TotalSeconds < Math.Max(5, _settings?.CommentaryTalkingPointIntervalSeconds ?? 20))
        {
            return;
        }

        var observations = GenerateObservations();
        if (observations.Count == 0)
        {
            return;
        }

        _suggestionInFlight = true;
        _lastSuggestionUtc = DateTime.UtcNow;

        _ = Task.Run(async () =>
        {
            try
            {
                var line = await _commentary.BuildTalkingPointAsync(observations);

                if (line is not null && IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke(() =>
                    {
                        _suggestedLine = line;
                        RefreshTalkingPoints();
                    });
                }
            }
            catch
            {
                // Suggestions are decoration. The client already swallows
                // its own failures and returns null; anything reaching here
                // is unexpected, and still must not disturb the page.
            }
            finally
            {
                _suggestionInFlight = false;
            }
        });
    }

    private void RefreshOrder()
    {
        var ordered = _race.GetOrderedStandings();

        // Rebuilding the whole grid twice a second would fight the user's
        // selection, so rows are only added or removed when the count
        // changes and cell values updated in place otherwise.
        if (_gridOrder.Rows.Count != ordered.Count)
        {
            _gridOrder.Rows.Clear();
            foreach (var _ in ordered)
            {
                _gridOrder.Rows.Add();
            }
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var s = ordered[i];
            var row = _gridOrder.Rows[i];

            var gap = i == 0
                ? "-"
                : RaceGaps.Format(RaceGaps.Between(s, ordered[0]));

            row.Cells[0].Value = i + 1;
            row.Cells[1].Value = NameFor(s.TransponderId);
            row.Cells[2].Value = s.Laps;
            row.Cells[3].Value = gap;
            row.Cells[4].Value = s.LapTimesMs.Count > 0 ? $"{s.LapTimesMs[^1] / 1000.0:0.000}" : "-";
            row.Tag = s.TransponderId;
        }
    }

    private void RefreshSpotlightList()
    {
        var previous = (_cmbSpotlight.SelectedItem as SpotlightItem)?.Transponder;

        _cmbSpotlight.Items.Clear();
        foreach (var profile in _profiles.All.OrderBy(p => p.DriverName))
        {
            _cmbSpotlight.Items.Add(new SpotlightItem(profile.Transponder, profile.DriverName));
        }

        // As wide as the longest name now in it - measured afresh, since a
        // refill can change the names without changing how many there are.
        _spotlightWidth = null;
        SizeFields();

        if (previous is not null)
        {
            SelectSpotlight(previous);
        }
        else if (_cmbSpotlight.Items.Count > 0)
        {
            _cmbSpotlight.SelectedIndex = 0;
        }
    }

    private void SelectSpotlight(string transponder)
    {
        for (var i = 0; i < _cmbSpotlight.Items.Count; i++)
        {
            if (_cmbSpotlight.Items[i] is SpotlightItem item
                && string.Equals(item.Transponder, transponder, StringComparison.OrdinalIgnoreCase))
            {
                if (_cmbSpotlight.SelectedIndex != i)
                {
                    _cmbSpotlight.SelectedIndex = i;
                }
                return;
            }
        }
    }

    /// <summary>
    /// A short story about one driver: how they're running now, and how that
    /// compares with their history. The comparison is the point - "4 tenths
    /// off his personal best" is worth saying, a bare lap time isn't.
    /// </summary>
    private void RefreshSpotlight()
    {
        if (_cmbSpotlight.SelectedItem is not SpotlightItem item)
        {
            _lblSpotlight.Text = string.Empty;
            return;
        }

        var profile = _profiles.Get(item.Transponder);
        var lines = new List<string>();

        var standing = _race.GetOrderedStandings()
            .Select((s, i) => (Standing: s, Position: i + 1))
            .FirstOrDefault(x => string.Equals(x.Standing.TransponderId, item.Transponder, StringComparison.OrdinalIgnoreCase));

        if (standing.Standing is not null)
        {
            var s = standing.Standing;
            lines.Add($"P{standing.Position}   {Plural.Of(s.Laps, "lap")}"
                      + (s.FastestLapMs > 0 ? $"   best this race {s.FastestLapMs / 1000.0:0.000}s" : ""));

            if (s.LapTimesMs.Count >= 3)
            {
                var recent = s.LapTimesMs.TakeLast(3).ToList();
                lines.Add($"Last three: {string.Join(", ", recent.Select(l => $"{l / 1000.0:0.000}"))}");
            }
        }

        // All-time best across recorded sessions, for context. Saved demo
        // races are left out - their lap times are invented, so quoting
        // one as somebody's personal best would be quoting a made-up lap.
        var allTime = _history.All
            .Where(sess => !sess.IsDemoSession())
            .SelectMany(sess => sess.Results.Where(r =>
                string.Equals(r.Transponder, item.Transponder, StringComparison.OrdinalIgnoreCase)))
            .Where(r => r.FastLapMs > 0)
            .Select(r => r.FastLapMs)
            .DefaultIfEmpty(0)
            .Min();

        if (allTime > 0)
        {
            var delta = standing.Standing?.FastestLapMs > 0
                ? standing.Standing.FastestLapMs - allTime
                : 0;

            lines.Add(delta switch
            {
                < 0 => $"Personal best {allTime / 1000.0:0.000}s - and they've just beaten it.",
                0 => $"Personal best {allTime / 1000.0:0.000}s.",
                _ => $"Personal best {allTime / 1000.0:0.000}s - running {delta / 1000.0:0.000}s off it today.",
            });
        }

        if (!string.IsNullOrWhiteSpace(profile?.CarModel))
        {
            lines.Add($"Car: {profile!.CarModel}"
                      + (string.IsNullOrWhiteSpace(profile.ClassName) ? "" : $"   Class: {profile.ClassName}"));
        }

        if (!string.IsNullOrWhiteSpace(profile?.Notes))
        {
            lines.Add($"Note: {profile!.Notes.Replace('\r', ' ').Replace('\n', ' ')}");
        }

        _lblSpotlight.Text = lines.Count == 0
            ? "No data for this driver yet."
            : string.Join(Environment.NewLine, lines);
    }

    private string NameFor(string transponder)
    {
        var profile = _profiles.Get(transponder);
        return string.IsNullOrWhiteSpace(profile?.DriverName) ? transponder : profile!.DriverName;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refresh.Stop();
            _refresh.Dispose();
        }

        base.Dispose(disposing);

        // After the labels that wear them are gone.
        if (disposing)
        {
            _stateFont?.Dispose();
            _stateFont = null;
            _clockFont?.Dispose();
            _clockFont = null;
        }
    }

    private sealed record SpotlightItem(string Transponder, string DriverName)
    {
        public override string ToString() =>
            string.IsNullOrWhiteSpace(DriverName) ? Transponder : DriverName;
    }
}
