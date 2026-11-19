namespace HeatCaller;

/// <summary>
/// One line of the run sheet (DayScheduleControl), added or edited: when it
/// happens, the class it is for and what it says.
///
/// On the layout pieces (DialogFit): the time with its No set time tick
/// beside it, the class and the description lined up in a field grid, and
/// Save and Cancel at the foot; as tall as that, and wider than it was made
/// when a class's name needs the drop-down wider. It used to be placed in
/// pixels in a 360x210 window: the fields at a fixed x of 90 beside
/// captions that ran into them ("Description:" needs more than that at the
/// normal text size), the drop-down and the box 254px wide however long the
/// longest class's name, the time picker a fixed 130x26 that a larger text
/// size clipped, and Save and Cancel a fixed 90 and 80px wide.
/// </summary>
public sealed class ScheduleEntryForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size.</summary>
    private const int DialogWidth = 360;

    /// <summary>The fields' width at the normal text size - wider when a class's name needs the drop-down wider, or the time and its tick need more.</summary>
    private const int FieldWidth = 254;

    private readonly TimePicker _dtpTime = new();
    private readonly CheckBox _chkNoTime = new ThemedCheckBox();
    private readonly ComboBox _cmbClass = new();
    private readonly TextBox _txtDescription = new();

    /// <summary>The time as the line had it when this opened - Result is the stored line itself, so it is kept here (see TimeToSave).</summary>
    private readonly string _originalTime;

    /// <summary>Whether No set time has been ticked or unticked since this opened.</summary>
    private bool _noTimeToggled;

    public ScheduleEntry Result { get; private set; }

    public ScheduleEntryForm(ScheduleEntry? existing, RaceClassStore raceClasses)
    {
        Result = existing ?? new ScheduleEntry();
        _originalTime = Result.ScheduledTime ?? string.Empty;

        Text = existing is null ? "Add Schedule Entry" : "Edit Schedule Entry";
        Font = AppTheme.BodyFont;

        // As big as its time needs in its font (TimePicker.GetPreferredSize) -
        // at 130x26 it held the time at the normal text size and clipped it
        // at any larger one.
        _dtpTime.AutoSize = true;

        _chkNoTime.Text = "No set time";
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

        // After the box is set above, so only the user's own clicks count.
        _chkNoTime.CheckedChanged += (_, _) => _noTimeToggled = true;

        _cmbClass.DropDownStyle = ComboBoxStyle.DropDown;
        foreach (var c in raceClasses.All)
        {
            _cmbClass.Items.Add(c.Name);
        }
        _cmbClass.Text = Result.ClassName;

        _txtDescription.Text = Result.Description;
        _txtDescription.PlaceholderText = "e.g. Qualifying Round 1, A-Main";

        var btnSave = new Button { Text = "Save" };
        btnSave.Click += BtnSave_Click;
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // The time, and the tick that says there is none, on one line - on
        // two when the line is too narrow for both. The class and the
        // description run to the right-hand edge, as they did.
        var timeRow = FieldGrid.Inline(_dtpTime, _chkNoTime);

        var fields = new FieldGrid();
        var captions = new List<Label>
        {
            fields.AddRow("Time:", timeRow),
            fields.AddRow("Class:", _cmbClass, stretch: true),
            fields.AddRow("Description:", _txtDescription, stretch: true),
        };

        // One card (18 Nov 2026, dialog pass 4), with a line on what the
        // run sheet does with it.
        var card = new Section("Run sheet line");
        card.Add(NightSetupDialog.CardHint("A line of the day's run sheet: when it happens, the class it's for and what it says."), gapBelow: UiLayout.RowGap);
        card.Add(fields, gapBelow: 0);

        var page = new FillPage();
        page.Add(card, gapBelow: UiLayout.RowGap);

        // Against the right-hand edge, Cancel furthest right, as they were -
        // and Tab still reaches Save first: the bar takes them right to left,
        // in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);
        btnSave.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        CancelButton = btnCancel;
        AcceptButton = btnSave;

        BackColor = AppTheme.Background;
        AppTheme.ApplyRecursively(this);
        AppTheme.StyleButton(btnSave, AppTheme.ButtonKind.Primary);

        // As wide as it was at the normal text size, at the size in use - or
        // wider when the fields' column needs more: the drop-down as wide as
        // the longest class's name, or the name the line already has when it
        // is not one on file, and the time and its tick on one line. Measured
        // in the faces the theme pass left them in; the drop-down and the
        // box take the whole of the column (stretch). The caption column
        // beside it as the grid works it out: its widest caption and the gap
        // after it.
        (Font Font, int Count, int Width)? classWidth = null;

        var fieldColumn = Math.Max(
            Math.Max(UiLayout.AtTextSize(FieldWidth), Pieces.PickerWidth(_cmbClass, ref classWidth, Result.ClassName)),
            timeRow.GetPreferredSize(Size.Empty).Width + timeRow.Margin.Horizontal);

        var captionColumn = captions.Max(caption => caption.GetPreferredSize(Size.Empty).Width + caption.Margin.Horizontal);

        var width = Math.Max(
            UiLayout.AtTextSize(DialogWidth),
            UiLayout.PageLeft + captionColumn + fieldColumn + UiLayout.PageRight) + Section.Inset.Horizontal;

        DialogFit.Keep(this, page, width, widthMeasured: true);
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_cmbClass.Text) && string.IsNullOrWhiteSpace(_txtDescription.Text))
        {
            AppDialog.Show(this, "Enter at least a class or a description.", "Missing details", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Result.ScheduledTime = TimeToSave(_originalTime, _chkNoTime.Checked, _noTimeToggled, _dtpTime.FormattedValue);
        Result.ClassName = _cmbClass.Text.Trim();
        Result.Description = _txtDescription.Text.Trim();

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// The time a line is saved with. A time the picker could not show
    /// ("TBD", "~10:15") is kept while No set time is left as it opened; it
    /// used to be written back blank by any edit, a fixed typo included,
    /// and Fill In the Times then stamped a time over it. Unticking and
    /// ticking the box again clears it on purpose.
    /// </summary>
    internal static string TimeToSave(string originalTime, bool noTimeTicked, bool noTimeToggled, string pickedTime) =>
        !noTimeTicked ? pickedTime
        : noTimeToggled ? string.Empty
        : originalTime;
}
