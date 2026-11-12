using System.Drawing;

namespace HeatCaller;

/// <summary>
/// Announce the next night (12 Nov 2026, setup plan D13): the date, what
/// it's called, the classes on, the times, and whether drivers can say
/// they're coming from their phones. Publish puts it on the phones' Home
/// page; Take Down (DialogResult.Abort) takes it off.
/// </summary>
internal sealed class AnnounceNightForm : Form
{
    private readonly DateTimePicker _dtpDay = new() { Format = DateTimePickerFormat.Long, Width = 220 };
    private readonly TextBox _txtTitle = new() { Width = 220 };
    private readonly TextBox _txtVenue = new() { Width = 220 };
    private readonly CheckedListBox _lstClasses = new() { CheckOnClick = true, IntegralHeight = false, Height = 120, Width = 260 };
    private readonly CheckBox _chkFirst = new ThemedCheckBox { Text = "Set", AutoSize = true };
    private readonly TimePicker _dtpFirst = new() { AutoSize = true };
    private readonly CheckBox _chkClose = new ThemedCheckBox { Text = "Set", AutoSize = true };
    private readonly TimePicker _dtpClose = new() { AutoSize = true };
    private readonly CheckBox _chkEntries = new ThemedCheckBox { AutoSize = true };
    private readonly AnnouncedNight _start;

    /// <summary>The night as set, when Publish was pressed.</summary>
    public AnnouncedNight Night { get; private set; }

    public AnnounceNightForm(AnnouncedNight start, IReadOnlyList<string> classesOnFile, bool announcedAlready)
    {
        _start = start;
        Night = start;

        Text = "Announce the Next Night";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var lblHow = new Label
        {
            Text = "Puts the next race night on the Home page of the club's phone pages: the date, the classes on and the times. "
                   + "Drivers can say they're coming from their Me page, so the desk knows who to expect. Comes down by itself after the night.",
            AutoSize = true,
            MaximumSize = new Size(480, 0),
            ForeColor = AppTheme.TextMuted,
        };

        _dtpDay.Value = start.Day.Date < DateTime.Today ? DateTime.Today : start.Day.Date;
        _dtpDay.MinDate = DateTime.Today;
        _txtTitle.Text = start.Title;
        _txtVenue.Text = start.Venue;

        foreach (var name in start.Classes.Concat(classesOnFile.Where(c => !start.Classes.Contains(c, StringComparer.OrdinalIgnoreCase))))
        {
            _lstClasses.Items.Add(name, start.Classes.Contains(name, StringComparer.OrdinalIgnoreCase));
        }

        _chkFirst.Checked = start.FirstRace is not null;
        _dtpFirst.Value = start.FirstRace ?? TimeSpan.FromHours(19);
        _chkClose.Checked = start.EntriesClose is not null;
        _dtpClose.Value = start.EntriesClose ?? TimeSpan.FromHours(18.5);

        _chkEntries.Text = "Let drivers say they're coming from their phones";
        _chkEntries.Checked = start.TakingEntries;

        var grid = new FieldGrid();
        grid.AddRow("Night:", _dtpDay);
        grid.AddRow("Called:", _txtTitle);
        grid.AddRow("Venue:", _txtVenue);
        grid.AddRow("Classes on:", _lstClasses);
        grid.AddRow("First race at:", FieldGrid.Inline(_dtpFirst, _chkFirst));
        grid.AddRow("Entries close at:", FieldGrid.Inline(_dtpClose, _chkClose));
        grid.AddField(_chkEntries);

        var btnTakeDown = new Button { Text = "Take Down", DialogResult = DialogResult.Abort, AutoSize = true, Visible = announcedAlready };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var btnPublish = new Button { Text = "Publish", AutoSize = true };
        btnPublish.Click += (_, _) =>
        {
            Night = Read();
            DialogResult = DialogResult.OK;
        };

        var page = new FillPage();
        page.Add(lblHow, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(grid, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnPublish, btnTakeDown), gapBelow: 0);

        Controls.Add(page);
        AcceptButton = btnPublish;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);
        DialogFit.Keep(this, page, width: 560, height: 560);
    }

    private AnnouncedNight Read() => new()
    {
        Day = _dtpDay.Value.Date,
        Title = _txtTitle.Text.Trim(),
        Venue = _txtVenue.Text.Trim(),
        Classes = _lstClasses.CheckedItems.Cast<string>().ToList(),
        FirstRace = _chkFirst.Checked ? _dtpFirst.Value : null,
        EntriesClose = _chkClose.Checked ? _dtpClose.Value : null,
        TakingEntries = _chkEntries.Checked,

        // A night announced again is on again: calling off is its own button.
        Off = false,
        OffText = string.Empty,
    };
}

/// <summary>Call it off (setup plan D13): the club's words, and whether to email the regulars.</summary>
internal sealed class CallOffForm : Form
{
    private readonly TextBox _txtWords = new() { Multiline = true, Height = 70 };
    private readonly CheckBox _chkEmail = new ThemedCheckBox { AutoSize = true };
    private readonly CheckBox _chkPush = new ThemedCheckBox { AutoSize = true };

    public string Words => _txtWords.Text.Trim();

    public bool Email => _chkEmail.Checked && _chkEmail.Enabled;

    public bool Push => _chkPush.Checked && _chkPush.Enabled;

    /// <param name="canPush">The club website is on, so a push can go to phones signed up for alerts (setup plan step 7).</param>
    public CallOffForm(AnnouncedNight night, int regulars, string? mailProblem, bool canPush = false)
    {
        Text = "Call It Off";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var lblHow = new Label
        {
            Text = $"The next race night, {NextNight.When(night.Day)}, shows as called off on the phones' Home page, with your words. "
                   + "Drivers can't say they're coming to it any more.",
            AutoSize = true,
            MaximumSize = new Size(460, 0),
        };

        _txtWords.Text = string.IsNullOrWhiteSpace(night.OffText)
            ? $"Racing's off on {NextNight.When(night.Day)}. Sorry - see you at the next one."
            : night.OffText;

        _chkEmail.Text = regulars == 1
            ? "Email the 1 regular with an email address"
            : $"Email the {regulars} regulars with an email address (raced in the last 8 weeks, or said they're coming)";
        _chkEmail.Checked = regulars > 0 && mailProblem is null;
        _chkEmail.Enabled = regulars > 0 && mailProblem is null;
        _chkEmail.MaximumSize = new Size(460, 0);

        // Setup plan step 7: a push too, to phones signed up for alerts.
        _chkPush.Text = canPush
            ? "Send a push to phones signed up for the club's alerts"
            : "Send a push (needs the club website switched on)";
        _chkPush.Checked = canPush;
        _chkPush.Enabled = canPush;
        _chkPush.MaximumSize = new Size(460, 0);

        var lblMail = new Label
        {
            Text = mailProblem is null ? string.Empty : $"Email can't go: {mailProblem}",
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            ForeColor = AppTheme.Warning,
            Visible = mailProblem is not null,
        };

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var btnOff = new Button { Text = "Call It Off", DialogResult = DialogResult.OK, AutoSize = true };

        var page = new FillPage();
        page.Add(lblHow, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(_txtWords, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(_chkPush, stretch: false, gapBelow: 0);
        page.Add(_chkEmail, stretch: false, gapBelow: 0);
        page.Add(lblMail, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnOff), gapBelow: 0);

        Controls.Add(page);
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);
        DialogFit.Keep(this, page, width: 520, height: 340);
    }
}
