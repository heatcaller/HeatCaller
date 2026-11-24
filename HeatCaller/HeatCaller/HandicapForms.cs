namespace HeatCaller;

/// <summary>
/// Edits one driver's handicap.
///
/// On the layout pieces (DialogFit): tiles across the top - what the class
/// counts and the two credits, following the boxes - with what the class's
/// mode makes of these numbers under them; then three cards, each with a
/// line on what it is for: the credits, each with its unit beside it; the
/// Locked tick with its note a step in under it; and the notes. Save and
/// Cancel at the foot; as tall as that (18 Nov 2026, dialog jazz-up - it
/// was one field grid under the mode line). It used to be placed in pixels in a 440x330 window: the fields at
/// a fixed 140px from the left whatever their captions needed, each unit at
/// a fixed place beside its box, the notes a fixed 60px tall at every text
/// size, and Save and Cancel at a fixed height under them.
/// </summary>
public sealed class HandicapEditForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size.</summary>
    private const int DialogWidth = 480;

    /// <summary>The credit boxes' width at the normal text size - wider when the largest value either takes needs it.</summary>
    private const int CreditWidth = 90;

    /// <summary>How many lines of the notes show before the box scrolls.</summary>
    private const int NotesLines = 3;

    private readonly NumericUpDown _numTime = new();
    private readonly NumericUpDown _numLaps = new();
    private readonly CheckBox _chkLocked = new ThemedCheckBox();
    private readonly TextBox _txtNotes = new();

    /// <summary>The handicap at a glance across the top: what the class counts, the time credit and the lap credit.</summary>
    private readonly StatTile[] _tiles = { new(), new(), new() };

    public DriverHandicap Result { get; }

    public HandicapEditForm(DriverHandicap handicap, string driverName, HandicapMode classMode)
    {
        Result = handicap;

        Text = $"Handicap - {driverName}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var lblMode = new Label
        {
            Text = classMode switch
            {
                HandicapMode.TimeCredit => "This class uses TIME credit - the lap credit below is ignored.",
                HandicapMode.LapCredit => "This class uses LAP credit - the time credit below is ignored.",
                _ => "Handicaps are OFF for this class. Values here are stored but won't affect results until a mode is chosen in the Mode box on the Handicaps page.",
            },
            AutoSize = true,

            // Off is a caution, in the theme's caution colour. It was in the
            // accent, which in most looks is the blue of a link, and read as
            // one (layout pictures, 30 Sep).
            ForeColor = classMode == HandicapMode.None ? AppTheme.Warning : AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        _numTime.DecimalPlaces = 1;
        _numTime.Increment = 0.5m;
        _numTime.Minimum = 0;
        // WHY (20 Oct 2026): Suggest from History can give a credit over 600s
        // (a slow driver over a long race), and this box used to cut the shown
        // value to 600 - so pressing Save, even just to tick Locked or add a
        // note, quietly wrote 600 over the real credit. The ceiling now grows
        // to fit the stored value (600 is still the ceiling for new entries),
        // and the box's width below is worked out after this, so it still fits.
        var storedTime = double.IsFinite(handicap.TimeCreditSeconds)
            ? Math.Clamp(handicap.TimeCreditSeconds, 0, 1_000_000)
            : 0;
        _numTime.Maximum = Math.Max(600m, Math.Ceiling((decimal)storedTime));
        _numTime.Value = (decimal)storedTime;

        var lblTimeUnit = new Label
        {
            Text = "seconds off their total time",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        _numLaps.Minimum = 0;
        _numLaps.Maximum = 20;
        _numLaps.Value = Math.Clamp(handicap.LapCredit, 0, 20);

        var lblLapsUnit = new Label
        {
            Text = "whole laps added",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        _chkLocked.Text = "Locked - leave this driver alone when suggesting";
        _chkLocked.AutoSize = true;
        _chkLocked.Checked = handicap.Locked;

        var lblLockHint = new Label
        {
            Text = "For a handicap agreed with the driver that shouldn't drift as their pace changes.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        _txtNotes.Multiline = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;
        _txtNotes.Text = handicap.Notes;

        var btnSave = new Button { Text = "Save" };
        btnSave.Click += (_, _) =>
        {
            Result.TimeCreditSeconds = (double)_numTime.Value;
            Result.LapCredit = (int)_numLaps.Value;
            Result.Locked = _chkLocked.Checked;
            Result.Notes = _txtNotes.Text;
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // THE TOP (18 Nov 2026, dialog jazz-up): the handicap at a glance -
        // what the class counts and the two credits, following the boxes as
        // they change - with the line on what the mode makes of them under
        // the tiles. The dialog is built at the text size in use, so the
        // tiles take their faces here rather than through a TextSize.
        foreach (var tile in _tiles)
        {
            tile.Value.Font = AppTheme.TitleFont;
            tile.Caption.Font = AppTheme.MicroFont;
        }

        _tiles[0].Value.ForeColor = classMode == HandicapMode.None ? AppTheme.Warning : AppTheme.Primary;
        _tiles[0].Set(classMode switch
        {
            HandicapMode.TimeCredit => "Time",
            HandicapMode.LapCredit => "Laps",
            _ => "Off",
        }, "Mode");

        void ShowCredits()
        {
            _tiles[1].Set(TimeTile(_numTime.Value), "Time credit");
            _tiles[2].Set(LapTile(_numLaps.Value), "Lap credit");
        }

        _numTime.ValueChanged += (_, _) => ShowCredits();
        _numLaps.ValueChanged += (_, _) => ShowCredits();
        ShowCredits();

        // Each credit with its unit beside it, wrapping under the box on a
        // line too narrow for both, in a card with a line on what they do.
        var credits = new FieldGrid();
        credits.AddRow("Time credit:", FieldGrid.Inline(_numTime, lblTimeUnit));
        credits.AddRow("Lap credit:", FieldGrid.Inline(_numLaps, lblLapsUnit));

        var creditCard = new Section("Credit");
        creditCard.Add(CardHint("A head start for a slower driver. Only the one the class counts changes results."), gapBelow: UiLayout.RowGap);
        creditCard.Add(credits, gapBelow: 0);

        // The tick from the card's left-hand edge - it says what it is
        // itself - and its note under it, a step in so it sits under the
        // tick's caption rather than its box, a hint's gap from it.
        var lockCard = new Section("Lock");
        lockCard.Add(_chkLocked, stretch: false, gapBelow: UiLayout.HintGap);
        lockCard.Add(lblLockHint, gapBelow: 0);
        lblLockHint.Margin = new Padding(TickBox.CaptionIndent, 0, 0, 0);

        // The notes across the card, where the buttons end.
        var notesCard = new Section("Notes");
        notesCard.Add(CardHint("Why it is what it is, for whoever looks next."), gapBelow: UiLayout.RowGap);
        notesCard.Add(_txtNotes, gapBelow: 0);

        var page = new FillPage();
        page.Add(new TileStrip(_tiles), gapBelow: UiLayout.RowGap);
        page.Add(lblMode, gapBelow: UiLayout.RowGap);
        page.Add(creditCard, gapBelow: UiLayout.RowGap);
        page.Add(lockCard, gapBelow: UiLayout.RowGap);
        page.Add(notesCard);

        // Against the right-hand edge, Cancel furthest right, as they were -
        // and Tab still reaches Save first: the bar takes them right to
        // left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);
        btnSave.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);
        AppTheme.StyleButton(btnSave, AppTheme.ButtonKind.Primary);

        // After the theme pass, in the faces it leaves them in: the two
        // credit boxes one width - they sit one under the other, and boxes
        // of two widths would put their arrows in two places - the width
        // they had at the normal text size, at the size in use, or wider
        // for the larger of their largest values; the notes a few lines of
        // their font, as the box draws them (Pieces.LineHeight).
        var creditWidth = Math.Max(NumberWidth(_numTime), NumberWidth(_numLaps));
        _numTime.Width = creditWidth;
        _numLaps.Width = creditWidth;

        _txtNotes.Height = NotesLines * Pieces.LineHeight(_txtNotes.Font) + UiLayout.HintGap * 2;

        DialogFit.Keep(this, page, width: DialogWidth);
    }

    /// <summary>The time credit's tile: "12.5 s", or "None".</summary>
    internal static string TimeTile(decimal seconds) =>
        seconds <= 0 ? "None" : seconds.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + " s";

    /// <summary>The lap credit's tile: "2 laps", "1 lap", or "None".</summary>
    internal static string LapTile(decimal laps) =>
        laps <= 0 ? "None" : laps == 1 ? "1 lap" : $"{laps:0} laps";

    /// <summary>A card's line saying what it is for, in the small muted face.</summary>
    private static Label CardHint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = AppTheme.TextMuted,
        Font = AppTheme.SmallFont,
    };

    /// <summary>
    /// A credit box as wide as its largest value - every digit a nought,
    /// with its decimals - and its arrows need in its own font, and never
    /// narrower than it was at the normal text size, at this size.
    /// </summary>
    private static int NumberWidth(NumericUpDown number)
    {
        var digits = decimal.Truncate(number.Maximum).ToString(System.Globalization.CultureInfo.InvariantCulture).Length;

        var widest = new string('0', digits)
            + (number.DecimalPlaces > 0 ? "." + new string('0', number.DecimalPlaces) : string.Empty);

        return Math.Max(
            UiLayout.AtTextSize(CreditWidth),
            TextRenderer.MeasureText(widest, number.Font).Width + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra);
    }
}

/// <summary>
/// Reviews suggested handicaps before any are applied.
///
/// Every suggestion is shown with the pace it was derived from and ticked
/// individually. Applying a whole set blind is how a handicap system loses
/// people's trust - they need to see why their number is what it is.
///
/// On the layout pieces (DialogFit): how the credits are worked out across
/// the top, the two numbers they are worked from on a line under it, the
/// suggestions taking the height, what they come to under them and the
/// buttons at the foot. It used to be placed in pixels in a 680x520 window:
/// the explanation wrapped at a fixed 648px, each number and its unit at a
/// fixed place along one line, and the line under the list and the buttons
/// at fixed heights from the bottom, the list anchored to fill the rest.
/// </summary>
public sealed class HandicapSuggestForm : Form
{
    /// <summary>The suggestions show their headings and at least this many drivers before the window scrolls instead.</summary>
    private const int LeastRows = 4;

    /// <summary>What the line under the list says with nothing to suggest.</summary>
    private const string NothingToSuggest =
        "Not enough recorded pace to suggest from - at least two drivers need a recorded best lap.";

    private readonly HandicapStore _store;
    private readonly List<DriverProfile> _roster;
    private readonly SessionHistoryStore _history;

    // The class and programme the suggestions are worked out for - see
    // HandicapStore.SessionsFor (17 Oct 2026, Bug Hunt 8, A26-1).
    private readonly string _className;
    private readonly RoundDefinitionStore? _rounds;

    private readonly NumericUpDown _numSessions = new();
    private readonly NumericUpDown _numLaps = new();
    private readonly DataGridView _grid = new();

    /// <summary>Apply Ticked - on only while something is ticked (UpdateApply).</summary>
    private readonly Button _btnApply = new() { Text = "Apply Ticked" };

    /// <summary>What the suggestions come to - reworded whenever either number changes, and kept as tall as the longest thing it can say (see BuildLayout).</summary>
    private readonly SteadyLabel _lblSummary = new();

    private List<HandicapStore.Suggestion> _suggestions = new();

    public List<HandicapStore.Suggestion> Accepted { get; private set; } = new();

    public HandicapSuggestForm(HandicapStore store, List<DriverProfile> roster,
        SessionHistoryStore history, string className, RoundDefinitionStore? rounds = null)
    {
        _store = store;
        _roster = roster;
        _history = history;
        _className = className;
        _rounds = rounds;

        Text = $"Suggest Handicaps - {className}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var page = BuildLayout();
        Recalculate();
        AppTheme.ApplyRecursively(this);

        // After the theme pass, in the faces it leaves them in: every
        // heading whole, the rest shared by weight (Pieces.KeepHeadingsWhole),
        // and each number box as wide as its largest value needs.
        Pieces.KeepHeadingsWhole(_grid);

        _numSessions.Width = NumberWidth(_numSessions);
        _numLaps.Width = NumberWidth(_numLaps);

        // 66px taller than it was (520): the card's caption and insets come
        // out of that, so the suggestions keep the height they had. It can be
        // made bigger, for a long roster.
        DialogFit.Keep(this, page, width: 680, height: 586);
    }

    /// <summary>The page, top to bottom, with the suggestions taking the height the rest leave. Returns it, for DialogFit.</summary>
    private FillPage BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Credits are worked out from the gap to the quickest driver, multiplied by a typical race distance - "
                 + "so the target is everyone finishing together. Nothing is applied until you tick it.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        var lblSessions = new Label { Text = "Use last:", AutoSize = true };
        _numSessions.Minimum = 1;
        _numSessions.Maximum = 50;
        _numSessions.Value = 5;
        _numSessions.ValueChanged += (_, _) => Recalculate();

        var lblSessionsUnit = new Label { Text = "sessions", AutoSize = true, ForeColor = AppTheme.TextMuted };

        var lblLaps = new Label { Text = "Typical race:", AutoSize = true };
        _numLaps.Minimum = 1;
        _numLaps.Maximum = 200;
        _numLaps.Value = 20;
        _numLaps.ValueChanged += (_, _) => Recalculate();

        var lblLapsUnit = new Label { Text = "laps", AutoSize = true, ForeColor = AppTheme.TextMuted };

        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        // Ticked by a click anywhere in its cell, or Space on the selected
        // row, as the Timetable's Done column is - and read-only to the grid
        // itself: the grid's own tick answered only a click on Windows' 13px
        // glyph in the middle of the cell, and the box drawn there now is the
        // app's own, bigger at a larger text size (TickBox), so a click on its
        // edge did nothing.
        var apply = new DataGridViewCheckBoxColumn { Name = "apply", HeaderText = "Apply", FillWeight = 30, ReadOnly = true };
        _grid.Columns.Add(apply);

        _grid.CellClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == 0)
            {
                FlipApply(e.RowIndex);
            }
        };

        _grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Space && e.Modifiers == Keys.None && _grid.CurrentRow is { Index: >= 0 } row)
            {
                FlipApply(row.Index);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };

        void FlipApply(int rowIndex)
        {
            var cell = _grid.Rows[rowIndex].Cells[0];
            cell.Value = cell.Value is not true;
            UpdateApply();
        }
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 80, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "pace", HeaderText = "Avg best lap", FillWeight = 60, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "sessions", HeaderText = "From", FillWeight = 35, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "current", HeaderText = "Current", FillWeight = 45, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "suggested", HeaderText = "Suggested", FillWeight = 50, ReadOnly = true }
        );

        _lblSummary.ForeColor = AppTheme.TextMuted;

        // As tall as the longer of the two things it says, at the width it
        // has - nothing to suggest, or a suggestion for every driver in the
        // class, the most there can be - so changing either number never
        // moves the buttons under it.
        var most = Math.Max(2, _roster.Count);
        _lblSummary.Wordings = new[] { NothingToSuggest, SuggestedFor(most), SummaryWords(0, most), SummaryWords(most, most) };

        var btnApply = _btnApply;
        btnApply.Click += (_, _) =>
        {
            Accepted = new List<HandicapStore.Suggestion>();

            // Each row carries its own suggestion. They used to be matched
            // by position, so once a header click had sorted the list a
            // tick credited whoever sat at that place in the original
            // order - a locked driver left unticked could be given another
            // driver's credit, and the one ticked got nothing.
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.Cells[0].Value is true && row.Tag is HandicapStore.Suggestion suggestion)
                {
                    Accepted.Add(suggestion);
                }
            }

            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // The two numbers along one line, each with its unit beside it, as
        // they were - the second going under the first when the line is too
        // narrow for both.
        var numbers = new FieldRow();
        numbers.Add(lblSessions, FieldGrid.Inline(_numSessions, lblSessionsUnit));
        numbers.Add(lblLaps, FieldGrid.Inline(_numLaps, lblLapsUnit));

        // Everything in one card (18 Nov 2026, dialog jazz-up, as Handicap
        // beside it has cards): how the credits are worked out as its line
        // on what it is for, the two numbers, the suggestions taking the
        // height and what they come to under them. One card, not two: on a
        // 1280x720 screen a second card's caption and insets came out of the
        // list.
        var card = new Section("Suggestions");
        card.Add(lblIntro, gapBelow: UiLayout.RowGap);

        // The class's mode, when its time credits will not count - see ModeNote.
        if (ModeNote(_store.ModeFor(_className)) is { } note)
        {
            var lblMode = new Label
            {
                Text = note,
                AutoSize = true,
                ForeColor = _store.ModeFor(_className) == HandicapMode.None ? AppTheme.Warning : AppTheme.TextMuted,
                Font = AppTheme.SmallFont,
            };

            card.Add(lblMode, gapBelow: UiLayout.RowGap);
        }

        card.Add(numbers, gapBelow: UiLayout.RowGap);
        card.AddFill(_grid, () => _grid.ColumnHeadersHeight + LeastRows * _grid.RowTemplate.Height + 2);
        card.Add(_lblSummary, gapBelow: 0);

        var page = new FillPage();
        page.AddFill(card, card.LeastHeight);

        // Against the right-hand edge, Cancel furthest right, as they were -
        // and Tab still reaches Apply Ticked first: the bar takes them right
        // to left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnApply), gapBelow: 0);
        btnApply.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        CancelButton = btnCancel;

        return page;
    }

    private void Recalculate()
    {
        _suggestions = _store.Suggest(_roster, _history, (int)_numSessions.Value, (int)_numLaps.Value, _className, _rounds);

        // WHY (23 Nov 2026, bug hunt 24): the ticks the director has set are
        // kept across a change to either number. The rows used to be cleared
        // and every one re-added at its starting tick, so nudging "Use last"
        // or "Typical race" re-ticked every driver who had been unticked (and
        // unticked a locked driver ticked on purpose) - and Apply Ticked then
        // gave a credit the director had turned down.
        var previousTicks = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is HandicapStore.Suggestion was)
            {
                previousTicks[was.Transponder] = row.Cells[0].Value is true;
            }
        }

        _grid.Rows.Clear();

        foreach (var suggestion in _suggestions)
        {
            var locked = _store.Get(suggestion.Transponder)?.Locked == true;

            var rowIndex = _grid.Rows.Add(
                // Locked drivers start unticked, and the reason is visible
                // in the row rather than them just silently not changing.
                HandicapSuggestTicks.StartTicked(previousTicks, suggestion.Transponder, locked),
                locked ? $"{suggestion.DriverName}  (locked)" : suggestion.DriverName,
                $"{suggestion.AverageBestLapSeconds:0.000}s",
                suggestion.SessionsUsed,
                suggestion.CurrentSeconds != 0 ? $"-{suggestion.CurrentSeconds:0.0}s" : "-",
                suggestion.SuggestedSeconds > 0 ? $"-{suggestion.SuggestedSeconds:0.0}s" : "scratch");

            _grid.Rows[rowIndex].Tag = suggestion;

            if (locked)
            {
                _grid.Rows[rowIndex].DefaultCellStyle.ForeColor = AppTheme.TextMuted;
            }
        }

        // WHY (23 Nov 2026, bug hunt 24): suggestions now come only from races
        // on the newest layout, so a driver who has not raced it yet drops out
        // of the list. The line under it used to say nothing, and the
        // director was left to wonder where they had gone.
        var all = HandicapStore.SessionsFor(_history, _rounds, _className).ToList();
        var notOnLayout = NotOnNewestLayout(_roster, all, HandicapStore.OnNewestLayout(all).ToList());

        _lblSummary.Text = SummaryWords(_suggestions.Count, notOnLayout);

        UpdateApply();
    }

    /// <summary>
    /// How many of the roster have a recorded best lap in the class's races,
    /// but none on the newest layout - so Suggest left them out.
    /// </summary>
    internal static int NotOnNewestLayout(IEnumerable<DriverProfile> roster, IReadOnlyList<SessionRecord> all, IReadOnlyList<SessionRecord> onNewest)
    {
        static bool HasLap(IEnumerable<SessionRecord> sessions, string transponder) =>
            sessions.Any(s => s.Results.Any(r =>
                string.Equals(r.Transponder, transponder, StringComparison.OrdinalIgnoreCase) && r.FastLapMs > 0));

        return roster.Count(driver => HasLap(all, driver.Transponder) && !HasLap(onNewest, driver.Transponder));
    }

    /// <summary>The line under the list: what the suggestions come to, and who was left out for not having raced the newest layout.</summary>
    internal static string SummaryWords(int suggested, int notOnLayout)
    {
        var words = suggested == 0 ? NothingToSuggest : SuggestedFor(suggested);

        return notOnLayout > 0
            ? $"{words} {Plural.Of(notOnLayout, "driver")} {(notOnLayout == 1 ? "has" : "have")} not raced the newest layout yet, so {(notOnLayout == 1 ? "gets" : "get")} no suggestion."
            : words;
    }

    /// <summary>
    /// A plain note under the intro when the class does not count time
    /// credits, or nothing when it does.
    ///
    /// WHY (23 Nov 2026, bug hunt 24): the window said nothing about the
    /// class's mode, so on a lap-credit class, or one with handicaps off, the
    /// director only learned after Apply Ticked that the suggestions would not
    /// change its results.
    /// </summary>
    internal static string? ModeNote(HandicapMode mode) => mode switch
    {
        HandicapMode.TimeCredit => null,
        HandicapMode.LapCredit => "This class uses LAP credit - suggested time credits are saved but will not change its results.",
        _ => "Handicaps are OFF for this class - suggested time credits are saved but will not change its results until a mode is chosen in the Mode box on the Handicaps page.",
    };

    /// <summary>
    /// Apply Ticked on while something is ticked, and off with nothing to
    /// apply - no suggestions at all, or every one unticked. It was on with
    /// an empty list, in the app's colour, and pressing it applied nothing
    /// (layout pictures, 30 Sep).
    /// </summary>
    private void UpdateApply()
    {
        var any = false;

        for (var i = 0; i < _grid.Rows.Count && i < _suggestions.Count; i++)
        {
            if (_grid.Rows[i].Cells[0].Value is true)
            {
                any = true;
                break;
            }
        }

        _btnApply.Enabled = any;
    }

    /// <summary>What the line under the list says with <paramref name="count"/> drivers to suggest for.</summary>
    private static string SuggestedFor(int count) =>
        $"{Plural.Of(count, "driver")} with enough history. The quickest is scratch.";

    /// <summary>
    /// A number box as wide as its largest value - every digit a nought -
    /// and its arrows need in its own font, and never narrower than a number
    /// box at the normal text size, at this size. They were a fixed 60px.
    /// </summary>
    private static int NumberWidth(NumericUpDown number)
    {
        var digits = decimal.Truncate(number.Maximum).ToString(System.Globalization.CultureInfo.InvariantCulture).Length;

        return Math.Max(
            UiLayout.AtTextSize(UiLayout.NumericWidth),
            TextRenderer.MeasureText(new string('0', digits), number.Font).Width + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra);
    }
}

/// <summary>
/// Whether a driver's Apply box is ticked when Suggest Handicaps fills its
/// list. WHY (23 Nov 2026, bug hunt 24): a driver already in the list keeps
/// the tick the director gave them; only a driver new to the list starts
/// ticked, or unticked when their handicap is locked. Changing either number
/// used to throw every tick away.
/// </summary>
internal static class HandicapSuggestTicks
{
    public static bool StartTicked(IReadOnlyDictionary<string, bool> previous, string transponder, bool locked) =>
        previous.TryGetValue(transponder, out var ticked) ? ticked : !locked;
}
