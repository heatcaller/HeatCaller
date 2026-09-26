namespace HeatCaller;

/// <summary>Edits one driver's handicap.</summary>
public sealed class HandicapEditForm : Form
{
    private readonly NumericUpDown _numTime = new();
    private readonly NumericUpDown _numLaps = new();
    private readonly CheckBox _chkLocked = new();
    private readonly TextBox _txtNotes = new();

    public DriverHandicap Result { get; }

    public HandicapEditForm(DriverHandicap handicap, string driverName, HandicapMode classMode)
    {
        Result = handicap;

        Text = $"Handicap - {driverName}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(440, 330);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
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
            Location = new Point(16, 16),
            AutoSize = true,
            MaximumSize = new Size(408, 0),
            ForeColor = classMode == HandicapMode.None ? AppTheme.Accent : AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        var lblTime = new Label { Text = "Time credit:", Location = new Point(16, 74), AutoSize = true };
        _numTime.Location = new Point(140, 71);
        _numTime.Width = 90;
        _numTime.DecimalPlaces = 1;
        _numTime.Increment = 0.5m;
        _numTime.Minimum = 0;
        _numTime.Maximum = 600;
        _numTime.Value = (decimal)Math.Clamp(handicap.TimeCreditSeconds, 0, 600);

        var lblTimeUnit = new Label
        {
            Text = "seconds off their total time",
            Location = new Point(238, 74),
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        var lblLaps = new Label { Text = "Lap credit:", Location = new Point(16, 112), AutoSize = true };
        _numLaps.Location = new Point(140, 109);
        _numLaps.Width = 90;
        _numLaps.Minimum = 0;
        _numLaps.Maximum = 20;
        _numLaps.Value = Math.Clamp(handicap.LapCredit, 0, 20);

        var lblLapsUnit = new Label
        {
            Text = "whole laps added",
            Location = new Point(238, 112),
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        _chkLocked.Text = "Locked - leave this driver alone when suggesting";
        _chkLocked.Location = new Point(16, 150);
        _chkLocked.AutoSize = true;
        _chkLocked.Checked = handicap.Locked;

        var lblLockHint = new Label
        {
            Text = "For a handicap agreed with the driver that shouldn't drift as their pace changes.",
            Location = new Point(36, 174),
            AutoSize = true,
            MaximumSize = new Size(388, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        var lblNotes = new Label { Text = "Notes:", Location = new Point(16, 210), AutoSize = true };
        _txtNotes.Location = new Point(140, 207);
        _txtNotes.Size = new Size(284, 60);
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
        AppTheme.LayoutButtonRow(230, 284, 8, btnSave, btnCancel);

        Controls.AddRange(new Control[]
        {
            lblMode, lblTime, _numTime, lblTimeUnit, lblLaps, _numLaps, lblLapsUnit,
            _chkLocked, lblLockHint, lblNotes, _txtNotes, btnSave, btnCancel,
        });

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);
    }
}

/// <summary>
/// Reviews suggested handicaps before any are applied.
///
/// Every suggestion is shown with the pace it was derived from and ticked
/// individually. Applying a whole set blind is how a handicap system loses
/// people's trust - they need to see why their number is what it is.
/// </summary>
public sealed class HandicapSuggestForm : Form
{
    private readonly HandicapStore _store;
    private readonly List<DriverProfile> _roster;
    private readonly SessionHistoryStore _history;

    private readonly NumericUpDown _numSessions = new();
    private readonly NumericUpDown _numLaps = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblSummary = new();

    private List<HandicapStore.Suggestion> _suggestions = new();

    public List<HandicapStore.Suggestion> Accepted { get; private set; } = new();

    public HandicapSuggestForm(HandicapStore store, List<DriverProfile> roster,
        SessionHistoryStore history, string className)
    {
        _store = store;
        _roster = roster;
        _history = history;

        Text = $"Suggest Handicaps - {className}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(680, 520);
        MinimumSize = new Size(600, 440);
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        BuildLayout();
        Recalculate();
        AppTheme.ApplyRecursively(this);
    }

    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Credits are worked out from the gap to the quickest driver, multiplied by a typical race distance - "
                 + "so the target is everyone finishing together. Nothing is applied until you tick it.",
            Location = new Point(16, 16),
            AutoSize = true,
            MaximumSize = new Size(648, 0),
            ForeColor = AppTheme.TextMuted,
        };

        var lblSessions = new Label { Text = "Use last:", Location = new Point(16, 66), AutoSize = true };
        _numSessions.Location = new Point(90, 63);
        _numSessions.Width = 60;
        _numSessions.Minimum = 1;
        _numSessions.Maximum = 50;
        _numSessions.Value = 5;
        _numSessions.ValueChanged += (_, _) => Recalculate();

        var lblSessionsUnit = new Label { Text = "sessions", Location = new Point(156, 66), AutoSize = true, ForeColor = AppTheme.TextMuted };

        var lblLaps = new Label { Text = "Typical race:", Location = new Point(240, 66), AutoSize = true };
        _numLaps.Location = new Point(330, 63);
        _numLaps.Width = 60;
        _numLaps.Minimum = 1;
        _numLaps.Maximum = 200;
        _numLaps.Value = 20;
        _numLaps.ValueChanged += (_, _) => Recalculate();

        var lblLapsUnit = new Label { Text = "laps", Location = new Point(396, 66), AutoSize = true, ForeColor = AppTheme.TextMuted };

        _grid.Location = new Point(16, 100);
        _grid.Size = new Size(648, 330);
        _grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        var apply = new DataGridViewCheckBoxColumn { Name = "apply", HeaderText = "Apply", FillWeight = 30 };
        _grid.Columns.Add(apply);
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 80, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "pace", HeaderText = "Avg best lap", FillWeight = 60, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "sessions", HeaderText = "From", FillWeight = 35, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "current", HeaderText = "Current", FillWeight = 45, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "suggested", HeaderText = "Suggested", FillWeight = 50, ReadOnly = true }
        );

        _lblSummary.Location = new Point(16, 442);
        _lblSummary.AutoSize = true;
        _lblSummary.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _lblSummary.ForeColor = AppTheme.TextMuted;

        var btnApply = new Button { Text = "Apply Ticked", Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        btnApply.Click += (_, _) =>
        {
            Accepted = new List<HandicapStore.Suggestion>();

            for (var i = 0; i < _grid.Rows.Count && i < _suggestions.Count; i++)
            {
                if (_grid.Rows[i].Cells[0].Value is true)
                {
                    Accepted.Add(_suggestions[i]);
                }
            }

            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button { Text = "Cancel", Anchor = AnchorStyles.Bottom | AnchorStyles.Left, DialogResult = DialogResult.Cancel };
        AppTheme.LayoutButtonRow(440, 476, 8, btnApply, btnCancel);

        Controls.AddRange(new Control[]
        {
            lblIntro, lblSessions, _numSessions, lblSessionsUnit,
            lblLaps, _numLaps, lblLapsUnit, _grid, _lblSummary, btnApply, btnCancel,
        });

        CancelButton = btnCancel;
    }

    private void Recalculate()
    {
        _suggestions = _store.Suggest(_roster, _history, (int)_numSessions.Value, (int)_numLaps.Value);

        _grid.Rows.Clear();

        foreach (var suggestion in _suggestions)
        {
            var locked = _store.Get(suggestion.Transponder)?.Locked == true;

            var rowIndex = _grid.Rows.Add(
                // Locked drivers start unticked, and the reason is visible
                // in the row rather than them just silently not changing.
                !locked,
                locked ? $"{suggestion.DriverName}  (locked)" : suggestion.DriverName,
                $"{suggestion.AverageBestLapSeconds:0.000}s",
                suggestion.SessionsUsed,
                suggestion.CurrentSeconds != 0 ? $"-{suggestion.CurrentSeconds:0.0}s" : "-",
                suggestion.SuggestedSeconds > 0 ? $"-{suggestion.SuggestedSeconds:0.0}s" : "scratch");

            if (locked)
            {
                _grid.Rows[rowIndex].DefaultCellStyle.ForeColor = AppTheme.TextMuted;
            }
        }

        _lblSummary.Text = _suggestions.Count == 0
            ? "Not enough recorded pace to suggest from - at least two drivers need a recorded best lap."
            : $"{Plural.Of(_suggestions.Count, "driver")} with enough history. The quickest is scratch.";
    }
}
