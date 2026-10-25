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
public sealed partial class AnnouncerControl : UserControl
{
    private readonly RaceTiming _race;
    private readonly ProfileStore _profiles;
    private readonly SessionHistoryStore _history;
    private readonly DayScheduleStore _schedule;
    private readonly CommentaryService? _commentary;
    private readonly AppSettings? _settings;

    /// <summary>Latest LLM-suggested line, shown at the top of the list. Null until one arrives.</summary>
    private string? _suggestedLine;

    /// <summary>
    /// The race (RaceControlSession.SessionGeneration) the suggested line was
    /// written about. A suggestion talks about particular cars and gaps, so
    /// it is only true for the race it came from - see DropStaleSuggestion.
    /// </summary>
    private int _suggestedLineGeneration;

    /// <summary>Guards against stacking requests - the refresh runs twice a second and a model call takes seconds.</summary>
    private bool _suggestionInFlight;

    private DateTime _lastSuggestionUtc = DateTime.MinValue;

    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 500 };

    /// <summary>
    /// The UI thread's context and id, taken in the constructor - see
    /// RunOnUiThread for why InvokeRequired cannot be trusted here.
    /// </summary>
    private readonly SynchronizationContext? _uiContext;

    private readonly int _uiThreadId;

    /// <summary>The race's state, as tall and as wide as the longest thing it can say - so the clock beside it never moves as it changes.</summary>
    private readonly SteadyLabel _lblState = new();

    /// <summary>The race clock, as wide as a race of a hundred minutes or more needs - so it never moves anything as it counts.</summary>
    private readonly SteadyLabel _lblClock = new();

    private readonly DataGridView _gridOrder = new();
    private readonly ListBox _lstTalkingPoints = new();
    private readonly ComboBox _cmbSpotlight = new();
    private readonly Label _lblSpotlight = new();
    private readonly Label _lblOnDeck = new();
    private readonly Label _lblLastSummary = new();

    /// <summary>The heading over the last summary, which says what kind of line it is - see RefreshLastSummary. As tall as the longest it can say.</summary>
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

    private const string ClearPointsCaption = "Clear Points";

    /// <summary>The heading over a race summary. A talking point shown there instead has its own - see RefreshLastSummary.</summary>
    private const string SummaryCaption = "LAST RACE SUMMARY";

    // WHY (23 Oct 2026, bug hunt 10): the AI announcer's "personal best"
    // check ignored the club's personal-best resets. Read on the UI thread
    // only, as DriverStats.ApplyPersonalBests reads it - see
    // PersonalBestBeforeTonight.
    /// <summary>The club's personal-best resets, so a reset best is not the bar a lap has to beat. Null leaves every race in.</summary>
    internal PersonalBestResetStore? PersonalBestResets { get; init; }

    public AnnouncerControl(RaceTiming race, ProfileStore profiles,
        SessionHistoryStore history, DayScheduleStore schedule, AnnouncementService announcer,
        CommentaryService? commentary = null, AppSettings? settings = null, UndoService? undo = null)
    {
        // Taken first, on the thread building the page - which is the UI
        // thread, and which WinForms has already given its own context by
        // the time any control's constructor body runs.
        _uiContext = SynchronizationContext.Current;
        _uiThreadId = Environment.CurrentManagedThreadId;

        _undo = undo;
        _commentary = commentary;
        _settings = settings;
        _race = race;
        _profiles = profiles;
        _history = history;
        _schedule = schedule;
        _announcer = announcer;

        ConnectTheVoiceToSettings();

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

        // Raised from inside the lap handler, which is the decoder's thread
        // rather than the UI's - so it goes through the same marshaling as
        // the announcer's lines.
        _race.JumpStartDetected += (transponder, penaltyMs) =>
            RunOnUiThread(() => AddFeedLine($"JUMP START - {NameFor(transponder)}"
                        + (penaltyMs > 0 ? $" (+{penaltyMs / 1000.0:0.0}s)" : " (flagged)")));

        _refresh.Tick += (_, _) => RefreshLive();
        _refresh.Start();

        RefreshAll();
    }

    /// <summary>
    /// What the voice takes from the club's settings (22 Oct 2026, known
    /// issues). Here because this control is built once at start-up
    /// (MainForm.Pages) with both the voice and the settings to hand.
    ///
    /// Tonight's "presented by" credits are kept across a restart the way
    /// the paid reads are: laid back from settings now, and saved after
    /// every credit. The save is moved to the UI thread - a credit is
    /// counted on the voice's own, and settings.json is written from here.
    /// </summary>
    private void ConnectTheVoiceToSettings()
    {
        if (_settings is not { } settings)
        {
            return;
        }

        // And the club's "shorten published names" box, carried to the voices
        // that send a line off this PC (22 Oct 2026, known issues) - see
        // AnnouncementService.ShortNames. Read live, like the rest.
        _announcer.ShortenNamesForCloudVoices = () => settings.RelayAbbreviateNames;
        _announcer.SayFullNamesWithCloudVoices = () => settings.SayFullNamesWithOnlineVoices;
        _announcer.EnergyFollowsTheRace = () => settings.VoiceEnergyFollowsRace;

        var gate = _announcer.SponsorMentions;
        gate.Restore(settings.SponsorMentionsNight);

        gate.Changed = saved =>
        {
            void Keep()
            {
                settings.SponsorMentionsNight = saved;
                settings.Save();
            }

            if (_uiContext is { } ui && Environment.CurrentManagedThreadId != _uiThreadId)
            {
                ui.Post(_ => Keep(), null);
            }
            else
            {
                Keep();
            }
        };
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
    ///     the summary's heading and the line itself.
    ///
    /// The columns are shared out equally, as the page always had them,
    /// but never narrower than the picker's longest name or Clear Points
    /// needs (SideWidth). Nothing is placed by hand,
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

        // Empty between races: it says when it fills.
        EmptyNote.For(_gridOrder, () => "Nothing running. The order shows here once a race starts.");

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

        // Empty until there is something to say.
        EmptyNote.For(_lstTalkingPoints, () => "Talking points come up here as a race goes on.");
        _lstTalkingPoints.DrawItem += TalkingPoints_DrawItem;

        // Variable-height rows are measured once, when added - so a resize
        // would otherwise leave every existing row at its old height and
        // re-truncate the text at the new width. Re-running the refresh
        // re-adds the items, which re-measures them.
        // Forced, because the text itself has not changed - only the width
        // it has to wrap into - so the usual "nothing new, leave it alone"
        // shortcut would skip exactly the re-measure this is here for.
        _lstTalkingPoints.Resize += (_, _) => RefreshTalkingPoints(force: true);

        var btnClear = new Button { Text = ClearPointsCaption };
        btnClear.Click += (_, _) =>
        {
            // The suggested line sits at the top of the same list, so "Clear
            // Points" has to take it too - before, it stayed put, and when the
            // feed was empty the button did nothing at all while a quote was
            // plainly showing. It is not part of the undo: it is a passing
            // suggestion the next request replaces, not a record of the race.
            if (_suggestedLine is not null)
            {
                _suggestedLine = null;
                RefreshTalkingPoints();
            }

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
        _lblSpotlight.AutoEllipsis = true;
        _lblSpotlight.ForeColor = AppTheme.TextDark;
        _textSize.Wear(_lblSpotlight, () => AppTheme.BodyFont);

        // --- Last race summary ---
        //
        // Its heading is not the card's own: it says what KIND of line is
        // shown under it (RefreshLastSummary), so it is a label the page
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
        _lblLastSummary.AutoEllipsis = true;
        _lblLastSummary.ForeColor = AppTheme.TextMuted;
        _textSize.Wear(_lblLastSummary, () => AppTheme.BodyFont);

        // The lines the two keep, and the most they show - see KeepLowerCards.
        _textSize.Keep(KeepLowerCards);
        _page.Resize += (_, _) => KeepLowerCards();

        // NO REPEAT BUTTON HERE ANY MORE (17 Oct 2026). "Repeat Race
        // Summary" spoke the stored recap again, which Race Control's Play
        // again on the announcer strip now does better - with its show
        // round it, the exchange in its own voices, and the broadcast floor
        // claimed so the director cannot talk over it. Two buttons for one
        // job, on two screens, meant two different-sounding replays. The
        // card still shows the last summary. See
        // RaceControlControl.ReplayLastSegmentAsync.

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

        // The summary under its heading. It had the Repeat button between
        // the two until 17 Oct 2026 - see the note above.
        var summaryCard = new Section();

        summaryCard.Add(_lblSummaryCaption, gapBelow: UiLayout.HeadingGap);
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
    /// their captions need; and never narrower itself than
    /// Clear Points or its captions need. Too
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
        var buttons = UiLayout.WidthFor(ClearPointsCaption) + UiLayout.ButtonGap;

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

    /// <summary>
    /// The most of the page the spotlight and the last summary take with
    /// their text, under the running order: about a third.
    /// </summary>
    private const float LowerShare = 0.3f;

    /// <summary>
    /// The spotlight's and the last summary's lines: each keeps its lines
    /// (SpotlightLines, SummaryLines) whatever it says, so the cards do not
    /// hop - and shows no more lines than fit in LowerShare of the page with
    /// its card's caption and its picker or button, ending its last with
    /// "..." past that. Never fewer than two.
    ///
    /// At the normal text size that is all of them, as it was. At 200% it is
    /// three: the running order is what this window is for, and with five
    /// lines under it - more for a long note on the driver - it had room for
    /// four drivers (Asher, layout pictures, 30 Sep).
    /// </summary>
    private void KeepLowerCards()
    {
        var most = LowerCardLines();

        Keep(_lblSpotlight, SpotlightLines);
        Keep(_lblLastSummary, SummaryLines);

        void Keep(Label label, int reserved)
        {
            var line = Pieces.LineHeight(label.Font);
            var least = new Size(0, Math.Min(reserved, most) * line + label.Padding.Vertical);
            var greatest = new Size(0, most * line + label.Padding.Vertical);

            // In the order that never has the least above the most: WinForms
            // moves one to meet the other when they cross.
            if (greatest.Height >= label.MinimumSize.Height)
            {
                if (label.MaximumSize != greatest)
                {
                    label.MaximumSize = greatest;
                }

                if (label.MinimumSize != least)
                {
                    label.MinimumSize = least;
                }
            }
            else
            {
                if (label.MinimumSize != least)
                {
                    label.MinimumSize = least;
                }

                if (label.MaximumSize != greatest)
                {
                    label.MaximumSize = greatest;
                }
            }
        }
    }

    /// <summary>How many lines of text the spotlight and the last summary may show - see KeepLowerCards. Every one, while the page has no height yet.</summary>
    private int LowerCardLines()
    {
        var height = _page.ClientSize.Height;
        var line = Math.Max(1, Pieces.LineHeight(AppTheme.BodyFont));

        if (height <= 0)
        {
            return 40;
        }

        // What the taller of the two cards has besides its text: its
        // caption, the picker or the button under it, and the gaps and the
        // card's edges round them.
        var chrome = Pieces.LineHeight(AppTheme.MicroFont)
            + UiLayout.HeadingGap
            + _cmbSpotlight.Height
            + UiLayout.RowGap
            + Section.Inset.Vertical;

        return Math.Clamp(((int)(height * LowerShare) - chrome) / line, 2, 40);
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

    private void OnAnnouncementText(string text) => RunOnUiThread(() => AddFeedLine(text));

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread: at once when already
    /// on it, otherwise posted there.
    /// </summary>
    /// <remarks>
    /// NOT InvokeRequired. This page is built at startup but has no window,
    /// and so no handle, until the announcer's screen is first opened - and
    /// without a handle InvokeRequired answers false on every thread. The
    /// talking points used to be added straight from whatever thread spoke
    /// them (the race recap and some flag calls speak from Task.Run) while
    /// the refresh timer was rebuilding the same list on the UI thread, which
    /// could throw "Collection was modified" mid-race or tear the list. The
    /// UI thread's own context needs no handle of ours, so lines spoken
    /// before the window is ever opened are still there when it is.
    ///
    /// A line that cannot be posted - the app shutting down - is dropped, as
    /// it always was once the page had no handle to post through.
    /// </remarks>
    private void RunOnUiThread(Action action)
    {
        if (Environment.CurrentManagedThreadId == _uiThreadId || _uiContext is null)
        {
            if (!IsDisposed)
            {
                action();
            }
            return;
        }

        try
        {
            _uiContext.Post(_ =>
            {
                if (!IsDisposed && !Disposing)
                {
                    action();
                }
            }, null);
        }
        catch (InvalidOperationException)
        {
            // The UI thread's marshaling window is gone - shutdown.
        }
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

    /// <summary>
    /// Rebuilds the talking-points list from the suggested line, the current
    /// observations and the feed.
    ///
    /// This runs on every tick of the half-second refresh timer, so it only
    /// touches the ListBox when the rows have actually changed. Clearing a
    /// ListBox resets its scroll position to the top and drops the selected
    /// row, and the feed holds far more lines than the card shows - so an
    /// unconditional rebuild snapped the list back to the top twice a
    /// second, and an announcer scrolling down to an older line (a
    /// jump-start note, say) could never keep it on screen. When a rebuild
    /// is needed the scroll position and selected row are carried across it.
    /// <paramref name="force"/> rebuilds even when the text is unchanged,
    /// which the resize handler needs to re-measure the wrapped rows.
    /// </summary>
    private void RefreshTalkingPoints(bool force = false)
    {
        var rows = new List<string>();

        // The LLM's suggested phrasing goes first when there is one - it's
        // the thing most likely to be read out verbatim. Marked distinctly
        // so it's never mistaken for a computed fact: the observations below
        // are measured, this one is written.
        if (!string.IsNullOrWhiteSpace(_suggestedLine))
        {
            rows.Add($"\u201C{_suggestedLine}\u201D");
        }

        // Generated observations first - they're the ones that need saying
        // NOW, where the feed below is a record of what already happened.
        foreach (var point in GenerateObservations())
        {
            rows.Add($"* {point}");
        }

        rows.AddRange(_feed);

        var items = _lstTalkingPoints.Items;
        if (!force && items.Count == rows.Count)
        {
            var same = true;
            for (var i = 0; i < rows.Count; i++)
            {
                if (!string.Equals(items[i]?.ToString(), rows[i], StringComparison.Ordinal))
                {
                    same = false;
                    break;
                }
            }

            if (same)
            {
                return;
            }
        }

        // Kept by index for the scroll position, which is what the eye is
        // holding on to, but by text for the selection, so a new feed line
        // arriving at the top doesn't move the highlight onto a different
        // row. A row that has gone simply ends up unselected.
        var topIndex = _lstTalkingPoints.TopIndex;
        var selectedText = _lstTalkingPoints.SelectedIndex >= 0
            ? _lstTalkingPoints.SelectedItem?.ToString()
            : null;

        _lstTalkingPoints.BeginUpdate();
        try
        {
            items.Clear();
            foreach (var row in rows)
            {
                items.Add(row);
            }

            if (selectedText is not null)
            {
                var index = rows.IndexOf(selectedText);
                if (index >= 0)
                {
                    _lstTalkingPoints.SelectedIndex = index;
                }
            }

            // Set after the selection, because selecting a row can scroll it
            // into view, and the place the announcer had scrolled to wins.
            if (rows.Count > 0 && topIndex > 0)
            {
                _lstTalkingPoints.TopIndex = Math.Min(topIndex, rows.Count - 1);
            }
        }
        finally
        {
            _lstTalkingPoints.EndUpdate();
        }
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

        // Ghosts left out, as the director does (17 Oct 2026, Bug Hunt 8,
        // A07-3): a PB ghost could lead, be half of a battle, or be the
        // "lapped" last car in the talking points.
        var ordered = RacingField(_race.GetOrderedStandings());
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
        if (_race.RaceFastestLapMs > 0 && !string.IsNullOrWhiteSpace(_race.RaceFastestLapTransponder)
            && !Ghosts.IsGhost(_race.RaceFastestLapTransponder))
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
        if (IsDisposed || Disposing)
        {
            return;
        }

        // Ahead of the visibility check, so a page opened during the break
        // never flashes the last race's quote for a tick before it goes.
        DropStaleSuggestion();

        // Also ahead of it, and for a stronger reason: the broadcast
        // segments are spoken over the PA rather than shown here, so a
        // club that never opens the announcer screen must still get them.
        FeedTheDirector();

        if (!Visible)
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
        RefreshLastSummary();
    }

    /// <summary>Keeps the last summary's text and heading matched to whatever CommentaryService last generated.</summary>
    private void RefreshLastSummary()
    {
        // The race summary first, the last line when there is none.
        var summary = _commentary?.LastRaceSummary;
        var line = summary ?? _commentary?.LastLine;

        // The caption tracks which KIND of line it is, since this falls
        // back to the last talking point when no summary exists - and a
        // heading saying "race summary" over a talking point would be
        // quietly wrong.
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
                : "Commentary is off. Turn it on at Voice > AI Commentary.";
            _lblLastSummary.ForeColor = AppTheme.TextMuted;
        }
    }

    /// <summary>
    /// Forgets the suggested line once the race it was about is over.
    ///
    /// A suggestion names cars and gaps ("Smith is hunting Jones, 0.4s
    /// behind!"), and it sits first in the list, quoted, as the line most
    /// likely to be read out word for word. A new one is only asked for
    /// while racing, at most every 20 seconds and only once there is
    /// something to say, so without this the last race's quote stayed on
    /// top through the break, staging, the countdown and the opening laps
    /// of the next race - about drivers who might not even be in it.
    /// Leaving Racing (finished, reset, back to staging) or a new session
    /// starting both end it. A red flag does not: the state stays Racing,
    /// and the line is still about the race on track.
    /// </summary>
    private void DropStaleSuggestion()
    {
        if (_suggestedLine is null)
        {
            return;
        }

        if (_race.State != RaceState.Racing || _race.SessionGeneration != _suggestedLineGeneration)
        {
            _suggestedLine = null;
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
        // Not until the announcer's screen has been opened: before then
        // there is nobody to read a suggestion, and the answer was thrown
        // away on arrival anyway (it is handed back through BeginInvoke,
        // which needs a handle) - so every request was a wasted model call.
        if (!IsHandleCreated
            || _commentary is null
            || !_commentary.IsAvailable
            || _settings?.CommentaryTalkingPoints != true
            || _race.State != RaceState.Racing
            || _suggestionInFlight
            || (DateTime.UtcNow - _lastSuggestionUtc).TotalSeconds < Math.Max(5, _settings?.CommentaryTalkingPointIntervalSeconds ?? 20))
        {
            return;
        }

        // WHY (23 Oct 2026, bug hunt 10): talking points went straight to the
        // brain and never past the budget, so with relay commentary on they
        // took a line off the club's month every 20 s of racing - roughly a
        // third of a 1,000-line month on one night, for on-screen hints
        // nobody hears - and kept spending once the director had stopped
        // for the night. Now, when the relay writes them and its month is
        // known, they stand aside whenever the night has no headroom left,
        // just as the director's own live segments do, and whenever the
        // month is down to the reserve held for intros and recaps. Not while the club
        // has chosen to keep everything running, and not while the model on
        // this PC is writing for a spent month, which costs nothing.
        // _director rather than Director: asking here must not be what
        // builds it. LocalModelWriting is asked last: it reaches into the
        // relay client, and this runs every tick while it holds the hint back.
        if (_settings?.CommentaryUseRelay == true
            && _director?.Budget is { AllowanceKnown: true } budget
            && (budget.Headroom < 1
                || budget.RemainingThisMonth < budget.MonthAllowance * CommentaryBudget.EssentialReserve)
            && !budget.KeepEverythingRunning
            && !budget.LocalModelWriting)
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

        // Read here, on the UI thread, so the answer can be checked against
        // the race it was asked about when it lands.
        var generationAsked = _race.SessionGeneration;

        _ = Task.Run(async () =>
        {
            try
            {
                var line = await _commentary.BuildTalkingPointAsync(observations);

                if (line is not null && IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke(() =>
                    {
                        // A model call takes seconds, and the race can finish
                        // or be reset in that time. An answer about a race
                        // that is no longer running is thrown away rather
                        // than put at the top of the list for the next one.
                        if (IsDisposed
                            || _race.State != RaceState.Racing
                            || _race.SessionGeneration != generationAsked)
                        {
                            return;
                        }

                        _suggestedLine = line;
                        _suggestedLineGeneration = generationAsked;
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

        // Best before this race, for context. Saved demo races are left
        // out - their lap times are invented, so quoting one as somebody's
        // personal best would be quoting a made-up lap.
        //
        // WHY (23 Oct 2026, bug hunt 10): this was the fastest lap on ANY
        // layout, so on a long layout the card said "running 3.300s off it
        // today" about every driver who had raced the short one, while the
        // director's pace calls (per layout) called the same laps personal
        // bests. It now reads the director's own per-layout figure
        // (PersonalBestBeforeTonight - same demo and ghost filters, tonight's
        // saved heats counted), and with no layout selected the line is
        // left out rather than comparing laps from different tracks.
        var bestBefore = PersonalBestBeforeTonight(item.Transponder);
        var allTime = bestBefore is { } bestSeconds ? (long)Math.Round(bestSeconds * 1000.0) : 0L;
        var onLayout = _settings?.LastRaceControlTrackName?.Trim() is { Length: > 0 } layoutName
            ? $" on {layoutName}"
            : string.Empty;

        if (allTime > 0)
        {
            var delta = standing.Standing?.FastestLapMs > 0
                ? standing.Standing.FastestLapMs - allTime
                : 0;

            lines.Add(delta switch
            {
                < 0 => $"Personal best{onLayout} {allTime / 1000.0:0.000}s - and they've just beaten it.",
                0 => $"Personal best{onLayout} {allTime / 1000.0:0.000}s.",
                _ => $"Personal best{onLayout} {allTime / 1000.0:0.000}s - running {delta / 1000.0:0.000}s off it today.",
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
