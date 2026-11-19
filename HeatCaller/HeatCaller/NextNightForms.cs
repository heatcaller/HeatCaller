using System.Drawing;

namespace HeatCaller;

/// <summary>
/// Announce the next night (12 Nov 2026, setup plan D13): the date, what
/// it's called, the classes on, the times, and whether drivers can say
/// they're coming from their phones. Publish puts it on the phones' Home
/// page; Take Down (DialogResult.Abort) takes it off.
///
/// On the layout pieces (DialogFit), in two columns (18 Nov 2026, dialog
/// jazz-up): cards for the night and the classes on down the left, the
/// classes list taking the height, and the times beside them, each card
/// with a hint line; Publish in the app's colour at the foot.
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

        // THE LOOK (18 Nov 2026, dialog jazz-up): three cards in the order
        // the fields always ran, so Tab goes the same way - the night, the
        // classes on (the list taking the height the window has spare) and
        // the times - each with a line on what it does. The note that was
        // over the grid is shared out as their hints, and Publish is in the
        // app's colour.
        var nightGrid = new FieldGrid();
        nightGrid.AddRow("Night:", _dtpDay);
        nightGrid.AddRow("Called:", _txtTitle);
        nightGrid.AddRow("Venue:", _txtVenue);

        var nightCard = new Section("The night");
        nightCard.Add(CardHint("Goes on the Home page of the club's phone pages, and comes down by itself after the night."), gapBelow: UiLayout.RowGap);
        nightCard.Add(nightGrid, gapBelow: 0);

        var classesCard = new Section("Classes on");
        classesCard.Add(CardHint("Tick the classes racing. Drivers see them in this order."), gapBelow: UiLayout.RowGap);
        classesCard.AddFill(_lstClasses, () => UiLayout.AtTextSize(ClassesLeastHeight), gapBelow: 0);

        var timesGrid = new FieldGrid();
        timesGrid.AddRow("First race at:", FieldGrid.Inline(_dtpFirst, _chkFirst));
        timesGrid.AddRow("Entries close at:", FieldGrid.Inline(_dtpClose, _chkClose));
        timesGrid.AddField(_chkEntries);

        var timesCard = new Section("Times and entries");
        timesCard.Add(CardHint("Tick Set to show a time. Drivers can say they're coming from their Me page, so the desk knows who to expect."), gapBelow: UiLayout.RowGap);
        timesCard.Add(timesGrid, gapBelow: 0);

        var btnTakeDown = new Button { Text = "Take Down", DialogResult = DialogResult.Abort, AutoSize = true, Visible = announcedAlready };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var btnPublish = new Button { Text = "Publish", AutoSize = true };
        btnPublish.Click += (_, _) =>
        {
            Night = Read();
            DialogResult = DialogResult.OK;
        };

        var page = new FillPage();
        // In two columns, so the window is no taller than it was: the night
        // and the classes on the left, the times beside them. Tab still
        // goes the night, the classes, the times - the left-hand column
        // comes first.
        var columns = new Beside(() => UiLayout.AtTextSize(SideWidth));
        columns.Add(nightCard, gapBelow: UiLayout.RowGap);
        columns.AddFill(classesCard, classesCard.LeastHeight);
        columns.AddSideCard(timesCard, gapBelow: 0);

        page.AddFill(columns, columns.LeastHeight, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnPublish, btnTakeDown), gapBelow: 0);

        Controls.Add(page);
        AcceptButton = btnPublish;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);
        AppTheme.StyleButton(btnPublish, AppTheme.ButtonKind.Primary);

        // The night's fields as wide as they were at the normal text size,
        // at the size in use: at 200% the date was cut to "Tuesday, ...".
        // The date wider, for a long one in full: "Wednesday, September 30, 2026".
        foreach (var field in new Control[] { _txtTitle, _txtVenue })
        {
            field.Width = UiLayout.AtTextSize(220);
        }

        _dtpDay.Width = UiLayout.AtTextSize(300);

        DialogFit.Keep(this, page, width: DialogWidth, height: DialogHeight);
    }

    /// <summary>How wide the window is inside its frame at the normal text size: the two columns.</summary>
    private const int DialogWidth = 880;

    /// <summary>How tall the window is inside its frame at the normal text size - lower than the 560 it had, the times beside the rest.</summary>
    private const int DialogHeight = 480;

    /// <summary>The right-hand column, the times, at the normal text size.</summary>
    private const int SideWidth = 420;

    /// <summary>The classes list's least height at the normal text size - the 120 it was made.</summary>
    private const int ClassesLeastHeight = 120;

    private static Label CardHint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = AppTheme.TextMuted,
        Font = AppTheme.SmallFont,
    };

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
