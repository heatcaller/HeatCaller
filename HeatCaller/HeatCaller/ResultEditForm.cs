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
/// </summary>
public sealed class ResultEditForm : Form
{
    private readonly SessionRecord _session;
    private readonly DataGridView _editGrid = new();
    private readonly DataGridView _previewGrid = new();
    private readonly Label _lblSummary = new();
    private readonly TextBox _txtReason = new();
    private readonly Button _btnSave = new() { Text = "Save Correction" };

    private bool _refreshing;

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
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(920, 660);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        BuildLayout();
        RefreshPreview();
        AppTheme.ApplyRecursively(this);
    }

    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Corrections are worked out from the race as it was actually run, not from the last correction - "
                 + "setting everything back to zero and Finished gives the original result back. Qualifying, the "
                 + "mains, the championship and the results pages all read the corrected figures from here on. "
                 + "Badges already earned for this race are not re-evaluated.",
            Location = new Point(16, 16),
            AutoSize = true,
            MaximumSize = new Size(888, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        var lblEdit = new Label { Text = "Corrections", Location = new Point(16, 76), AutoSize = true, Font = AppTheme.BoldFont };

        _editGrid.Location = new Point(16, 102);
        _editGrid.Size = new Size(888, 210);
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
            new DataGridViewTextBoxColumn { Name = "addedLaps", HeaderText = "Added Laps", FillWeight = 55 },
            new DataGridViewTextBoxColumn { Name = "timePenalty", HeaderText = "Time Penalty (s)", FillWeight = 65 },
            new DataGridViewTextBoxColumn { Name = "lapPenalty", HeaderText = "Lap Penalty", FillWeight = 55 });

        // Added on its own, after the plain text columns - see
        // HandicapSuggestForm for why a differently-typed column is added
        // separately rather than inside the same AddRange call.
        _editGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "outcome",
            HeaderText = "Outcome",
            FillWeight = 60,
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
                now.AddedLaps,
                (now.TimePenaltyMs / 1000.0).ToString("0.###"),
                now.LapPenalty,
                OutcomeCaption(now.Outcome));

            _editGrid.Rows[rowIndex].Tag = entry.Transponder;
        }

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

        var lblPreview = new Label { Text = "New order", Location = new Point(16, 328), AutoSize = true, Font = AppTheme.BoldFont };

        _previewGrid.Location = new Point(16, 354);
        _previewGrid.Size = new Size(888, 180);
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

        var lblReason = new Label { Text = "Reason (required, kept with the result):", Location = new Point(16, 546), AutoSize = true };
        _txtReason.Location = new Point(16, 570);
        _txtReason.Size = new Size(660, 26);
        _txtReason.MaxLength = 200;
        _txtReason.TextChanged += (_, _) => UpdateSaveEnabled();

        _lblSummary.Location = new Point(16, 602);
        _lblSummary.AutoSize = true;
        _lblSummary.MaximumSize = new Size(888, 0);
        _lblSummary.ForeColor = AppTheme.TextMuted;
        _lblSummary.Font = AppTheme.SmallFont;

        _btnSave.Enabled = false;
        _btnSave.Click += (_, _) => Save();

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        AppTheme.LayoutButtonRow(700, 568, 8, _btnSave, btnCancel);

        Controls.AddRange(new Control[]
        {
            lblIntro, lblEdit, _editGrid, lblPreview, _previewGrid,
            lblReason, _txtReason, _lblSummary, _btnSave, btnCancel,
        });

        AcceptButton = _btnSave;
        CancelButton = btnCancel;
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
                return;
            }

            if (ResultEditor.Validate(_session, adjustments) is { } invalid)
            {
                _lblSummary.Text = invalid;
                _btnSave.Enabled = false;
                return;
            }

            foreach (var row in ResultEditor.Preview(_session, adjustments))
            {
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
                }
            }

            var changes = ResultEditor.DescribeChanges(_session, adjustments);

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
