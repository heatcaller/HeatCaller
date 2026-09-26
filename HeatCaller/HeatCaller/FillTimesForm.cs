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
/// </summary>
public sealed class FillTimesForm : Form
{
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
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;

        var y = UiLayout.PageTop;

        var lblIntro = new Label
        {
            Text = "Every line gets a time, worked down the sheet from the first race.",
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            Font = AppTheme.BodyFont,
            Location = new Point(UiLayout.PageLeft, y),
        };

        Controls.Add(lblIntro);

        y = UiLayout.Below(lblIntro, UiLayout.RowGap);

        // Measured from the captions rather than set at a number, so a
        // larger text size does not clip the longer of the two.
        var fieldLeft = UiLayout.PageLeft;

        foreach (var caption in new[] { "First race at:", "Minutes per race:" })
        {
            var measured = TextRenderer.MeasureText(
                caption,
                AppTheme.BodyFont,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            fieldLeft = Math.Max(fieldLeft, UiLayout.PageLeft + measured.Width + UiLayout.LabelGap * 2);
        }

        void Row(string caption, Control field, int width)
        {
            var lbl = new Label { Text = caption, AutoSize = true, Font = AppTheme.BodyFont };

            field.Font = AppTheme.BodyFont;
            field.Width = width;
            field.Location = new Point(fieldLeft, y);
            lbl.Location = new Point(UiLayout.PageLeft, UiLayout.CentreOn(lbl, y, field.Height));

            Controls.Add(lbl);
            Controls.Add(field);

            y = UiLayout.Below(field, UiLayout.RowGap);
        }

        // TimePicker, not WinForms' DateTimePicker: that one ignores the
        // theme's colours and renders in system grey. See RaceDayEventControl.
        _dtpStart.Height = 26;
        _dtpStart.Value = startAt;

        Row("First race at:", _dtpStart, 130);

        _numMinutes.Minimum = 1;
        _numMinutes.Maximum = 240;
        _numMinutes.Value = Math.Clamp(minutesEach, 1, 240);

        Row("Minutes per race:", _numMinutes, 70);

        var lblHint = new Label
        {
            Text = "Start to start, so allow for the changeover as well as the race itself.\r\n\r\n"
                 + "Your own lines - doors, lunch, presentation - are filled in too. One that already has a "
                 + "time keeps it, and the rest of the day is worked on from there, so \"lunch 13:00\" moves "
                 + "the afternoon rather than being moved by it.",
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
            Location = new Point(UiLayout.PageLeft, y),
        };

        Controls.Add(lblHint);

        y = UiLayout.Below(lblHint, UiLayout.SectionGap);

        var btnOk = new Button { Text = "Fill In the Times", Font = AppTheme.BodyFont, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", Font = AppTheme.BodyFont, DialogResult = DialogResult.Cancel };

        btnOk.Size = new Size(UiLayout.WidthFor(btnOk), UiLayout.ButtonHeight);
        btnCancel.Size = new Size(UiLayout.WidthFor(btnCancel), UiLayout.ButtonHeight);

        var right = UiLayout.PageLeft + 460;

        btnCancel.Location = new Point(right - btnCancel.Width, y);
        btnOk.Location = new Point(btnCancel.Left - UiLayout.ButtonGap - btnOk.Width, y);

        Controls.Add(btnOk);
        Controls.Add(btnCancel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        ClientSize = new Size(
            right + UiLayout.PageLeft,
            UiLayout.Below(btnOk, UiLayout.PageTop));
    }
}
