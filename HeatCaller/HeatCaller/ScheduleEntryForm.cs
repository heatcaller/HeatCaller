namespace HeatCaller;

public sealed class ScheduleEntryForm : Form
{
    private readonly TimePicker _dtpTime = new();
    private readonly CheckBox _chkNoTime = new();
    private readonly ComboBox _cmbClass = new();
    private readonly TextBox _txtDescription = new();

    public ScheduleEntry Result { get; private set; }

    public ScheduleEntryForm(ScheduleEntry? existing, RaceClassStore raceClasses)
    {
        Result = existing ?? new ScheduleEntry();

        Text = existing is null ? "Add Schedule Entry" : "Edit Schedule Entry";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(360, 210);
        Font = AppTheme.BodyFont;

        var lblTime = new Label { Text = "Time:", Location = new Point(16, 18), AutoSize = true };
        _dtpTime.Location = new Point(90, 15);
        _dtpTime.Size = new Size(130, 26);

        _chkNoTime.Text = "No set time";
        _chkNoTime.Location = new Point(222, 17);
        _chkNoTime.AutoSize = true;
        _chkNoTime.CheckedChanged += (_, _) => _dtpTime.Enabled = !_chkNoTime.Checked;

        // Existing entries were free text, and older ones may hold things
        // the picker can't represent ("~10:15", "TBD"). Anything that
        // parses cleanly loads into the picker; anything that doesn't falls
        // back to "No set time" rather than being silently rewritten.
        if (DateTime.TryParse(Result.ScheduledTime, out var parsedTime))
        {
            _dtpTime.Value = parsedTime.TimeOfDay;
        }
        else
        {
            _chkNoTime.Checked = true;
            _dtpTime.Enabled = false;

            // Default the picker to the top of the next hour so ticking the
            // box off lands somewhere sensible instead of "now, to the second".
            var next = DateTime.Now.AddHours(1);
            _dtpTime.Value = new TimeSpan(next.Hour, 0, 0);
        }

        var lblClass = new Label { Text = "Class:", Location = new Point(16, 50), AutoSize = true };
        _cmbClass.Location = new Point(90, 47);
        _cmbClass.Width = 254;
        _cmbClass.DropDownStyle = ComboBoxStyle.DropDown;
        foreach (var c in raceClasses.All)
        {
            _cmbClass.Items.Add(c.Name);
        }
        _cmbClass.Text = Result.ClassName;

        var lblDescription = new Label { Text = "Description:", Location = new Point(16, 82), AutoSize = true };
        _txtDescription.Location = new Point(90, 79);
        _txtDescription.Width = 254;
        _txtDescription.Text = Result.Description;
        _txtDescription.PlaceholderText = "e.g. Qualifying Round 1, A-Main";

        var btnSave = new Button { Text = "Save", Location = new Point(168, 160), Width = 90 };
        btnSave.Click += BtnSave_Click;
        var btnCancel = new Button { Text = "Cancel", Location = new Point(264, 160), Width = 80, DialogResult = DialogResult.Cancel };

        Controls.Add(lblTime);
        Controls.Add(_dtpTime);
        Controls.Add(_chkNoTime);
        Controls.Add(lblClass);
        Controls.Add(_cmbClass);
        Controls.Add(lblDescription);
        Controls.Add(_txtDescription);
        Controls.Add(btnSave);
        Controls.Add(btnCancel);

        CancelButton = btnCancel;
        AcceptButton = btnSave;

        BackColor = AppTheme.Background;
        AppTheme.ApplyRecursively(this);
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_cmbClass.Text) && string.IsNullOrWhiteSpace(_txtDescription.Text))
        {
            AppDialog.Show(this, "Enter at least a class or a description.", "Missing details", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Result.ScheduledTime = _chkNoTime.Checked ? string.Empty : _dtpTime.FormattedValue;
        Result.ClassName = _cmbClass.Text.Trim();
        Result.Description = _txtDescription.Text.Trim();

        DialogResult = DialogResult.OK;
        Close();
    }
}
