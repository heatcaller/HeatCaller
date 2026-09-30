namespace HeatCaller;

/// <summary>
/// When the first race goes out, and how long a race takes - the two
/// numbers a whole day's timetable can be worked out from.
///
/// WHY THIS EXISTS. Every line's time had to be typed in by hand, one
/// dialog at a time, and typed again from the top whenever a round was
/// added or the programme reordered. A twelve-round day was twelve dialogs
/// to get a sheet that a single reorder invalidated - which is a good part
/// of why the timetable was the one page in Race Day nobody used.
///
/// It is also what finally makes RaceEvent.FirstRaceLocalTime mean
/// something. That property has existed, with a comment describing a run
/// sheet measuring drift against it, and nothing has ever read or written
/// it. The start time chosen here is saved onto the day, so the field on
/// The Day and this dialog are the same number.
///
/// On the layout pieces (DialogFit): what it does in a line across the top,
/// the two fields beside their captions in one measured column, the note on
/// what the gap covers wrapping under them, and the buttons at the foot -
/// the window as tall as all of it, and on a screen too short for that, the
/// text and the fields scroll and the buttons stay in view. It used to be
/// placed in pixels: the text wrapped at a fixed 460px, the fields' column
/// worked out from the captions by hand, and the time picker a fixed 130x26
/// that held the time at the normal text size and clipped it at a larger one.
/// </summary>
public sealed class FillTimesForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size: the 460px its text wrapped at, and the margins either side.</summary>
    private const int DialogWidth = 492;

    private readonly TimePicker _dtpStart = new();
    private readonly NumericUpDown _numMinutes = new();

    /// <summary>When the first race goes out. Only meaningful after DialogResult.OK.</summary>
    public TimeSpan StartAt => _dtpStart.Value;

    /// <summary>How many minutes to allow each race, start to start. Only meaningful after DialogResult.OK.</summary>
    public int MinutesEach => (int)_numMinutes.Value;

    /// <param name="startAt">What to open on - the day's first-race time when it has one.</param>
    /// <param name="minutesEach">What to open on for the gap, remembered from last time.</param>
    public FillTimesForm(TimeSpan startAt, int minutesEach)
    {
        Text = "Fill in the times";
        Font = AppTheme.BodyFont;

        var lblIntro = new Label
        {
            Text = "Every line gets a time, worked down the sheet from the first race.",
            AutoSize = true,
            Font = AppTheme.BodyFont,
        };

        // TimePicker, not WinForms' DateTimePicker: that one ignores the
        // theme's colours and renders in system grey. See RaceDayEventControl.
        // As big as its time needs in its font (TimePicker.GetPreferredSize)
        // rather than the fixed 130x26 it was, which clipped the time at a
        // larger text size.
        _dtpStart.AutoSize = true;
        _dtpStart.Value = startAt;

        _numMinutes.Minimum = 1;
        _numMinutes.Maximum = 240;
        _numMinutes.Value = Math.Clamp(minutesEach, 1, 240);

        // The captions share one column, as wide as the longer of the two at
        // the text size in use, so neither is clipped, and the fields line up
        // beside it.
        var fields = new FieldGrid();
        fields.AddRow("First race at:", _dtpStart);
        fields.AddRow("Minutes per race:", _numMinutes);

        var lblHint = new Label
        {
            Text = "Start to start, so allow for the changeover as well as the race itself.\r\n\r\n"
                 + "Your own lines - doors, lunch, presentation - are filled in too. One that already has a "
                 + "time keeps it, and the rest of the day is worked on from there, so \"lunch 13:00\" moves "
                 + "the afternoon rather than being moved by it.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        var btnOk = new Button { Text = "Fill In the Times", DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        var page = new FillPage();

        // Everything but the buttons: on a screen too short for all of it at
        // a large text size, it scrolls and the buttons stay in view.
        var body = DialogFit.Body(page);
        body.Add(lblIntro, gapBelow: UiLayout.RowGap);
        body.Add(fields, gapBelow: UiLayout.RowGap);
        body.Add(lblHint, gapBelow: 0);

        // Against the right-hand edge, Cancel furthest right, as they were -
        // and Tab still reaches Fill In the Times first: the bar takes them
        // right to left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnOk), gapBelow: 0);
        btnOk.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        // The window's own button in the app's colour, as Save and OK are in
        // every other window - the theme goes by the caption, and "Fill In
        // the Times" is not one it knows, so it came out grey beside a grey
        // Cancel.
        AppTheme.StyleButton(btnOk, AppTheme.ButtonKind.Primary);

        // Wide enough for three digits - the most it goes to - in the face
        // the theme pass leaves it in, and never narrower than a number box
        // is anywhere else at this text size.
        _numMinutes.Width = Math.Max(
            UiLayout.AtTextSize(UiLayout.NumericWidth),
            TextRenderer.MeasureText("000", _numMinutes.Font).Width + SystemInformation.VerticalScrollBarWidth + 12);

        DialogFit.Keep(this, page, width: DialogWidth, body: body);
    }
}
