namespace HeatCaller;

/// <summary>
/// Corrects a saved race result: add or take away laps for a driver, a time
/// penalty, a lap penalty, or DNF/DQ - for one or several drivers in one
/// sitting, with a required reason and a live preview of the new order.
///
/// Pure UI. The arithmetic and the rules for what a correction does are
/// ResultEditor's - the exact engine the phone's race-day manager uses
/// (RaceDayService.ResultAdjust) - so a correction made here and one made
/// from a phone can never disagree about what a five-second penalty does
/// to the finishing order. This form only reads the session, to show and
/// validate; SAVING - capturing undo, applying, persisting, republishing -
/// is the caller's job (see RaceHistoryControl.EditSelectedResult), exactly
/// as every other editor dialog in this app hands its result back rather
/// than writing it itself.
///
/// On the layout pieces (DialogFit): tiles across the top - the drivers,
/// the winner as it stands, how many drivers the correction changes and
/// moves (18 Nov 2026, dialog jazz-up) - then the corrections and the new
/// order, each a card with a line on using it, sharing the height - what
/// the correction comes to under the corrections, in their card - and the
/// reason beside its caption, Save Correction and Cancel along the foot.
/// It used to be
/// placed in pixels in a 920x660 window: the note, the two grids, the
/// reason with the buttons beside it and the summary each at a fixed
/// place, so at a larger text size the note ran into the grid under it and
/// the reason's caption into its box.
/// </summary>
public sealed class ResultEditForm : Form
{
    /// <summary>
    /// Each grid shows its headings and at least this many drivers before
    /// the window scrolls instead. Two grids share the height, so two each:
    /// at three, the least works out at about all the height a 1280x720
    /// screen leaves the window, which would open it scrolling. Opened, each
    /// still shows three or more.
    /// </summary>
    private const int LeastRows = 2;

    /// <summary>
    /// Lines kept for the summary under the reason, so the "no changes yet",
    /// an error and a line of changes can take each other's place without
    /// moving anything. A longer list of changes still grows it, and the
    /// grids give it the room.
    /// </summary>
    private const int SummaryLines = 2;

    private readonly SessionRecord _session;
    private readonly DataGridView _editGrid = new();
    private readonly DataGridView _previewGrid = new();
    private readonly Label _lblSummary = new();
    private readonly TextBox _txtReason = new();
    private readonly Button _btnSave = new() { Text = "Save Correction" };

    /// <summary>The correction at a glance across the top: the drivers, the winner as it stands, how many drivers it changes and how many it moves - see ShowTiles.</summary>
    private readonly StatTile[] _tiles = { new(), new(), new(), new() };

    /// <summary>The reason and its caption, at the foot - levelled with the buttons beside it once themed.</summary>
    private FieldGrid? _reasonRow;

    private bool _refreshing;

    /// <summary>Each driver's laps as raced, by transponder - what the Laps column is worked out from.</summary>
    private readonly Dictionary<string, int> _racedLaps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What to apply, once this form returns DialogResult.OK - one entry per
    /// driver in the result, in total (not as a delta on the last
    /// correction). The caller passes this straight to ResultEditor.Apply.
    /// </summary>
    public List<ResultAdjustment> Adjustments { get; private set; } = new();

    /// <summary>Why, as typed - required, and already trimmed.</summary>
    public string Reason { get; private set; } = string.Empty;

    public ResultEditForm(SessionRecord session)
    {
        _session = session;

        Text = $"Edit Result - {(string.IsNullOrWhiteSpace(session.Label) ? "Untitled session" : session.Label)}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var page = BuildLayout();
        RefreshPreview();
        AppTheme.ApplyRecursively(this);
        AppTheme.StyleButton(_btnSave, AppTheme.ButtonKind.Primary);

        // The reason level with the buttons beside it, in the faces the
        // theme pass left them in.
        if (_reasonRow is not null)
        {
            var top = Math.Max(0, (_btnSave.Height - _reasonRow.GetPreferredSize(Size.Empty).Height) / 2);
            _reasonRow.Margin = new Padding(0, top, 0, 0);
        }

        // Every heading whole, the rest shared by weight - see Pieces.KeepHeadingsWhole.
        Pieces.KeepHeadingsWhole(_editGrid);
        Pieces.KeepHeadingsWhole(_previewGrid);

        // Taller than the 660 it was (18 Nov 2026): the tiles and the cards'
        // captions took about two drivers' rows from each grid, given back
        // where the screen has the room. Never taller than the screen leaves.
        // Wider than the 920 it was, by the cards' inset, so the grids keep
        // the width they had - Outcome's "Finished" whole.
        DialogFit.Keep(this, page, width: 920 + Section.Inset.Horizontal, height: 740);
    }

    /// <summary>
    /// The page, top to bottom: the correction at a glance in tiles, the
    /// corrections and the new order in cards of their own sharing the
    /// height the rest leave, the reason and what the correction comes to in
    /// a card under them, and Save Correction and Cancel at the foot. Returns
    /// it, for DialogFit.
    /// </summary>
    private FillPage BuildLayout()
    {
        _editGrid.AllowUserToAddRows = false;
        _editGrid.AllowUserToDeleteRows = false;
        _editGrid.RowHeadersVisible = false;
        _editGrid.MultiSelect = false;
        _editGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _editGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _editGrid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;

        _editGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "pos", HeaderText = "Pos", FillWeight = 26, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 110, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "raced", HeaderText = "Raced", FillWeight = 60, ReadOnly = true },

            // The result's lap count, to type straight in (11 Nov 2026,
            // decoder-down mode): read off a hand-scoring sheet, down the
            // column with Enter, and Added Laps follows - see SyncLaps.
            new DataGridViewTextBoxColumn { Name = "laps", HeaderText = "Laps", FillWeight = 45 },
            new DataGridViewTextBoxColumn { Name = "addedLaps", HeaderText = "Added Laps", FillWeight = 55 },
            new DataGridViewTextBoxColumn { Name = "timePenalty", HeaderText = "Time Penalty (s)", FillWeight = 65 },
            new DataGridViewTextBoxColumn { Name = "lapPenalty", HeaderText = "Lap Penalty", FillWeight = 55 });

        // Added on its own, after the plain text columns - see
        // HandicapSuggestForm for why a differently-typed column is added
        // separately rather than inside the same AddRange call.
        //
        // Drawn as the app's own drop-down by the theme (AppTheme.StyleGrid),
        // not the grid's grey box. WHY the weight (22 Nov 2026, Asher): the
        // app's chevron wants more room than the grid's arrow did, and at 60
        // "Finished" was cut to "Finish..." in the Synthwave face; the room
        // comes off Driver, the widest column.
        _editGrid.Columns["driver"]!.FillWeight = 92;

        _editGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "outcome",
            HeaderText = "Outcome",
            FillWeight = 78,
            Items = { "Finished", "DNF", "DQ" },
        });

        foreach (var entry in _session.Results.OrderBy(r => r.Position))
        {
            var now = ResultEditor.CurrentOf(entry);
            var racedLaps = entry.AsRaced?.Laps ?? entry.Laps;

            var rowIndex = _editGrid.Rows.Add(
                entry.Position,
                string.IsNullOrWhiteSpace(entry.DriverName) ? entry.Transponder : entry.DriverName,
                Plural.Of(racedLaps, "lap"),
                HandScoring.TotalLaps(racedLaps, now.AddedLaps, now.LapPenalty),
                now.AddedLaps,
                (now.TimePenaltyMs / 1000.0).ToString("0.###"),
                now.LapPenalty,
                OutcomeCaption(now.Outcome));

            _editGrid.Rows[rowIndex].Tag = entry.Transponder;
            _racedLaps[entry.Transponder] = racedLaps;
        }

        // Before the preview, so it reads the figures the other column now has.
        _editGrid.CellEndEdit += (_, e) => SyncLaps(e.RowIndex, e.ColumnIndex);
        _editGrid.CellEndEdit += (_, _) => RefreshPreview();

        // The combo box commit below changes the value without ending the
        // edit, so CellEndEdit alone left the preview (and Save) showing
        // the old outcome until the cell was left. Hooked after the rows
        // are filled, so filling them does not run it.
        _editGrid.CellValueChanged += (_, _) => RefreshPreview();

        _editGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            // A combo box cell commits on selection, not on leaving the
            // cell - without this, picking "DQ" only took effect once the
            // driver clicked somewhere else in the grid.
            if (_editGrid.IsCurrentCellDirty)
            {
                _editGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };

        _previewGrid.ReadOnly = true;
        _previewGrid.AllowUserToAddRows = false;
        _previewGrid.AllowUserToDeleteRows = false;
        _previewGrid.RowHeadersVisible = false;
        _previewGrid.MultiSelect = false;
        _previewGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _previewGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _previewGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "pos", HeaderText = "Pos", FillWeight = 26 },
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 110 },
            new DataGridViewTextBoxColumn { Name = "laps", HeaderText = "Laps", FillWeight = 50 },
            new DataGridViewTextBoxColumn { Name = "time", HeaderText = "Time", FillWeight = 60 },
            new DataGridViewTextBoxColumn { Name = "outcome", HeaderText = "Outcome", FillWeight = 50 },
            new DataGridViewTextBoxColumn { Name = "moved", HeaderText = "", FillWeight = 45 });

        // The reason beside its caption, the box running to the right-hand
        // edge.
        _txtReason.MaxLength = 200;
        _txtReason.TextChanged += (_, _) => UpdateSaveEnabled();

        var reason = new FieldGrid();
        reason.AddRow("Reason:", _txtReason, stretch: true);

        _lblSummary.AutoSize = true;
        _lblSummary.ForeColor = AppTheme.TextMuted;
        _lblSummary.Font = AppTheme.SmallFont;

        // In the face it wears - see SummaryLines.
        _lblSummary.MinimumSize = new Size(0, SummaryLines * Pieces.LineHeight(_lblSummary.Font) + _lblSummary.Padding.Vertical);

        _btnSave.Enabled = false;
        _btnSave.Click += (_, _) => Save();

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // THE TOP (18 Nov 2026, dialog jazz-up): the correction at a glance,
        // following the grid as it is typed in (ShowTiles). The dialog is
        // built at the text size in use, so the tiles take their faces here
        // rather than through a TextSize.
        foreach (var tile in _tiles)
        {
            tile.Value.Font = AppTheme.TitleFont;
            tile.Caption.Font = AppTheme.MicroFont;
        }

        _tiles[0].Set(_session.Results.Count.ToString(System.Globalization.CultureInfo.CurrentCulture), "Drivers");

        // Two cards, each grid with a line on how to use it - the note that
        // ran across the top shared out between them - and what the
        // correction comes to under the grid it comes from. The reason goes
        // at the foot, beside Save Correction: typed last, just before Save.
        // A card of its own took the height of a driver's row from each
        // grid, which on a 1280x720 screen they have none to spare.
        _txtReason.PlaceholderText = "Needed to save - kept with the result";

        var correctionsCard = new Section("Corrections");
        correctionsCard.Add(CardHint("Type over a number; Laps and Added Laps follow each other. Worked out from the race "
            + "as run: all 0 and Finished puts the original result back."), gapBelow: UiLayout.HintGap);
        correctionsCard.AddFill(_editGrid, () => _editGrid.ColumnHeadersHeight + LeastRows * _editGrid.RowTemplate.Height + 2, gapBelow: UiLayout.HintGap);
        correctionsCard.Add(_lblSummary, gapBelow: 0);

        var orderCard = new Section("New order");
        orderCard.Add(CardHint("As it will be saved. Qualifying, the mains, the championship and the results pages all use it; "
            + "achievements already earned stay."), gapBelow: UiLayout.HintGap);
        orderCard.AddFill(_previewGrid, () => _previewGrid.ColumnHeadersHeight + LeastRows * _previewGrid.RowTemplate.Height + 2, gapBelow: 0);

        var page = new FillPage();

        page.Add(new TileStrip(_tiles), gapBelow: UiLayout.RowGap);
        page.AddFill(correctionsCard, correctionsCard.LeastHeight, gapBelow: UiLayout.RowGap);
        page.AddFill(orderCard, orderCard.LeastHeight, gapBelow: UiLayout.RowGap);

        // The foot: the reason running from the left-hand edge to the
        // buttons, and the buttons against the right-hand edge, Cancel
        // furthest right, as a dialog's buttons go - and Tab still reaches
        // Save Correction first, as it did: the bar takes them right to left,
        // in the order they were added.
        var buttons = new ButtonBar(alignRight: true).With(btnCancel, _btnSave);
        _btnSave.TabIndex = 0;
        btnCancel.TabIndex = 1;

        var foot = new Beside(() => buttons.GetPreferredSize(Size.Empty).Width)
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        _reasonRow = foot.Add(reason, gapBelow: 0);
        foot.AddSideMeasured(buttons, gapBelow: 0);

        page.Add(foot, gapBelow: 0);

        Controls.Add(page);

        AcceptButton = _btnSave;
        CancelButton = btnCancel;

        return page;
    }

    /// <summary>
    /// Keeps Laps and Added Laps saying the same thing (11 Nov 2026). A lap
    /// count typed into Laps sets Added Laps to make the result show it
    /// (HandScoring.AddedLapsFor, the lap penalty still standing on top);
    /// a change to Added Laps or Lap Penalty puts the new count in Laps. So
    /// a result scored by hand can be typed in one go, top to bottom, and
    /// the laps added show as counted by hand ("12*") in the result, as any
    /// added lap does. Something that isn't a whole number is left alone -
    /// ReadAdjustments says what is wrong with it.
    /// </summary>
    private void SyncLaps(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 0 || _editGrid.Rows[rowIndex].Tag is not string transponder
            || !_racedLaps.TryGetValue(transponder, out var raced))
        {
            return;
        }

        var row = _editGrid.Rows[rowIndex];
        var column = _editGrid.Columns[columnIndex].Name;

        if (!int.TryParse(Convert.ToString(row.Cells["lapPenalty"].Value), out var lapPenalty))
        {
            return;
        }

        if (column == "laps")
        {
            if (HandScoring.ParseLapCount(Convert.ToString(row.Cells["laps"].Value)) is { } wanted)
            {
                row.Cells["addedLaps"].Value = HandScoring.AddedLapsFor(raced, lapPenalty, wanted);
            }
        }
        else if (column is "addedLaps" or "lapPenalty"
                 && int.TryParse(Convert.ToString(row.Cells["addedLaps"].Value), out var added))
        {
            row.Cells["laps"].Value = HandScoring.TotalLaps(raced, added, lapPenalty);
        }
    }

    /// <summary>"Finished", "DNF" or "DQ" - matching the Outcome column's own items exactly, so a cell's text can be read straight back as one of them.</summary>
    private static string OutcomeCaption(string outcome) => string.IsNullOrEmpty(outcome) ? "Finished" : outcome;

    /// <summary>
    /// Reads the grid into one adjustment per row, or stops at the first
    /// cell that doesn't make sense and explains which. Called on every
    /// edit (to redraw the preview) as well as on Save, so it never trusts
    /// that a value typed a moment ago is still a valid number.
    /// </summary>
    private List<ResultAdjustment> ReadAdjustments(out string? error)
    {
        error = null;
        var list = new List<ResultAdjustment>();

        foreach (DataGridViewRow row in _editGrid.Rows)
        {
            if (row.Tag is not string transponder)
            {
                continue;
            }

            var driverName = Convert.ToString(row.Cells["driver"].Value) ?? transponder;

            if (!int.TryParse(Convert.ToString(row.Cells["addedLaps"].Value), out var addedLaps))
            {
                error = $"{driverName}: added laps has to be a whole number.";
                return list;
            }

            if (!TryParseSeconds(Convert.ToString(row.Cells["timePenalty"].Value), out var penaltySeconds))
            {
                error = $"{driverName}: time penalty has to be a number of seconds, like 5 or 2.5.";
                return list;
            }

            if (!int.TryParse(Convert.ToString(row.Cells["lapPenalty"].Value), out var lapPenalty))
            {
                error = $"{driverName}: lap penalty has to be a whole number.";
                return list;
            }

            // 13 Nov 2026, Bug hunt 17: a Laps entry SyncLaps could not read
            // ("12*", "l2") used to stay in the cell with Added Laps unmoved
            // and nothing said, so the correction saved without it.
            if (HandScoring.ParseLapCount(Convert.ToString(row.Cells["laps"].Value)) is null)
            {
                error = $"{driverName}: laps has to be a whole number, 0 or more.";
                return list;
            }

            var outcome = ResultEditor.NormaliseOutcome(Convert.ToString(row.Cells["outcome"].Value));

            if (outcome is null)
            {
                error = $"{driverName}: outcome has to be Finished, DNF or DQ.";
                return list;
            }

            list.Add(new ResultAdjustment(transponder, addedLaps, (long)Math.Round(penaltySeconds * 1000), lapPenalty, outcome));
        }

        return list;
    }

    /// <summary>
    /// Reads a number of seconds typed by hand, taking either a point or a
    /// comma as the decimal mark.
    ///
    /// It used to be read in the machine's own culture, which on a German
    /// or French laptop treats the point as a thousands separator and drops
    /// it: "5.5" became a 55-second penalty, silently. So one separator is
    /// allowed, of either kind, and always read as the decimal mark; two or
    /// more ("1.500,5", "1,000.5") are refused as ambiguous rather than
    /// guessed at, and so is anything that isn't a finite number.
    /// </summary>
    private static bool TryParseSeconds(string? text, out double seconds)
    {
        seconds = 0;
        var trimmed = (text ?? string.Empty).Trim();

        if (trimmed.Length == 0 || trimmed.Count(c => c is '.' or ',') > 1)
        {
            return false;
        }

        // The Unicode minus some cultures write (Swedish, for one) is the
        // same sign, so the grid's own figures read back as they were shown.
        return double.TryParse(trimmed.Replace(',', '.').Replace('\u2212', '-'), System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out seconds)
               && double.IsFinite(seconds);
    }

    /// <summary>Recomputes the new-order preview and the Save button's availability from whatever is in the grid right now.</summary>
    private void RefreshPreview()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;

        try
        {
            var adjustments = ReadAdjustments(out var error);

            _previewGrid.Rows.Clear();

            if (error is not null)
            {
                _lblSummary.Text = error;
                _btnSave.Enabled = false;
                ShowTiles(null, null, 0);
                return;
            }

            if (ResultEditor.Validate(_session, adjustments) is { } invalid)
            {
                _lblSummary.Text = invalid;
                _btnSave.Enabled = false;
                ShowTiles(null, null, 0);
                return;
            }

            string? winner = null;
            var movedCount = 0;

            foreach (var row in ResultEditor.Preview(_session, adjustments))
            {
                winner ??= string.IsNullOrWhiteSpace(row.DriverName) ? row.Transponder : row.DriverName;

                var was = _session.Results
                    .FirstOrDefault(r => string.Equals(r.Transponder, row.Transponder, StringComparison.OrdinalIgnoreCase))
                    ?.Position;

                var moved = was is { } before && before != row.Position
                    ? (row.Position < before ? $"▲ was P{before}" : $"▼ was P{before}")
                    : string.Empty;

                var rowIndex = _previewGrid.Rows.Add(
                    row.Position,
                    string.IsNullOrWhiteSpace(row.DriverName) ? row.Transponder : row.DriverName,
                    Plural.Of(row.Laps, "lap"),
                    FormatElapsed(row.ElapsedMs),
                    string.IsNullOrEmpty(row.Outcome) ? "-" : row.Outcome,
                    moved);

                if (moved.Length > 0)
                {
                    _previewGrid.Rows[rowIndex].DefaultCellStyle.ForeColor = AppTheme.Accent;
                    movedCount++;
                }
            }

            var changes = ResultEditor.DescribeChanges(_session, adjustments);

            ShowTiles(winner, changes.Count, movedCount);

            _lblSummary.Text = changes.Count == 0
                ? "No changes yet."
                : string.Join("    ", changes.Select(c =>
                    $"{(string.IsNullOrWhiteSpace(c.Entry.DriverName) ? c.Entry.Transponder : c.Entry.DriverName)}: {c.Change}"));

            UpdateSaveEnabled();
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>
    /// The tiles after the first, as the grid stands: who wins now, how many
    /// drivers the correction changes and how many it moves. A null
    /// <paramref name="changed"/> is a grid that does not read yet - the
    /// summary under the reason says why - shown as a dash rather than old
    /// figures. Changes and moves in the theme's main colour, so a glance
    /// says whether anything is about to happen.
    /// </summary>
    private void ShowTiles(string? winner, int? changed, int moved)
    {
        _tiles[1].Set(winner ?? "-", "Winner");
        _tiles[2].Set(changed switch { null => "-", 0 => "None", 1 => "1 driver", var n => $"{n} drivers" }, "Changed");
        _tiles[3].Set(changed is null ? "-" : moved == 0 ? "None" : $"{moved} places", "Order moves");

        _tiles[2].Value.ForeColor = changed is > 0 ? AppTheme.Primary : AppTheme.TextDark;
        _tiles[3].Value.ForeColor = changed is not null && moved > 0 ? AppTheme.Primary : AppTheme.TextDark;
    }

    /// <summary>A card's line saying how to use it, in the small muted face.</summary>
    private static Label CardHint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = AppTheme.TextMuted,
        Font = AppTheme.SmallFont,
    };

    private void UpdateSaveEnabled()
    {
        var adjustments = ReadAdjustments(out var error);

        _btnSave.Enabled = error is null
            && _txtReason.Text.Trim().Length > 0
            && ResultEditor.Validate(_session, adjustments) is null
            && ResultEditor.DescribeChanges(_session, adjustments).Count > 0;
    }

    private void Save()
    {
        var reason = _txtReason.Text.Trim();

        if (reason.Length == 0)
        {
            AppDialog.Show(this, "Give a reason for the correction - it is kept with the result.",
                "Reason needed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtReason.Focus();
            return;
        }

        var adjustments = ReadAdjustments(out var error);

        if (error is not null)
        {
            AppDialog.Show(this, error, "Can't save that", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (ResultEditor.Validate(_session, adjustments) is { } invalid)
        {
            AppDialog.Show(this, invalid, "Can't save that", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (ResultEditor.DescribeChanges(_session, adjustments).Count == 0)
        {
            AppDialog.Show(this, "Nothing was actually changed.", "Nothing to save", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Adjustments = adjustments;
        Reason = reason;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>"1:23.456" (or "1:02:03.456" past an hour) - the same shape RaceHistoryControl shows elapsed race time in.</summary>
    private static string FormatElapsed(long elapsedMs)
    {
        if (elapsedMs <= 0)
        {
            return string.Empty;
        }

        var ts = TimeSpan.FromMilliseconds(elapsedMs);
        return ts.ToString(ts.Hours > 0 ? @"h\:mm\:ss\.fff" : @"m\:ss\.fff");
    }
}
