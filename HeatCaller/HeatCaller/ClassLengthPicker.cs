namespace HeatCaller;

/// <summary>
/// The two questions every window that adds a class asks (12 Nov 2026, setup
/// plan step 8): how long its races are, and how they finish - "Stop when
/// time's up" or "Time's up, then finish the lap".
///
/// WHY ONE PIECE: a class is added from three places - Add a Class on the
/// Classes page (RaceClassEditForm), Set Up Tonight's Add Class
/// (QuickClassForm) and the first-run wizard's Classes step. Three copies of
/// a minutes box and a finish picker would disagree about a clamp or a
/// caption by the end of the month, and a class's finish rule is exactly the
/// thing two drivers argue about after a close race. The answer is turned
/// into a format by RaceFormat.ForNewClass(appDefault, minutes, finishTheLap),
/// which is the one rule.
///
/// The rows go into the caller's FieldGrid (AddTo), so they line up with
/// whatever else that window asks; or into a grid of their own (Grid) where
/// the window has none.
/// </summary>
internal sealed class ClassLengthPicker
{
    private readonly RaceFormat? _appDefault;

    private readonly NumericUpDown _numMinutes = new() { Minimum = 1, Maximum = 180 };
    private readonly ComboBox _cmbFinish = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _lblUnit = new() { Text = "minutes", AutoSize = true };
    private readonly Label _lblHint = new() { AutoSize = true };

    private Label? _lblMinutesCaption;
    private Label? _lblFinishCaption;

    /// <param name="appDefault">The app's default format, which the suggestion starts from. Null uses the built-in values.</param>
    public ClassLengthPicker(RaceFormat? appDefault)
    {
        _appDefault = appDefault;

        _numMinutes.Value = RaceFormat.SuggestedMinutes(appDefault);

        // "Then finish the lap" first: it is the common club rule and the
        // app's default, so it is the one most people are agreeing to.
        _cmbFinish.Items.Add(RaceFormat.FinishTheLapWords);
        _cmbFinish.Items.Add(RaceFormat.StopAtTimeWords);
        _cmbFinish.SelectedIndex = RaceFormat.SuggestedFinishTheLap(appDefault) ? 0 : 1;

        _lblHint.Font = AppTheme.SmallFont;
        _lblHint.ForeColor = AppTheme.TextMuted;
        _lblHint.Text = HintFor(appDefault);
    }

    /// <summary>The race length asked for, in minutes.</summary>
    public int Minutes => (int)_numMinutes.Value;

    /// <summary>True for "Time's up, then finish the lap".</summary>
    public bool FinishTheLap => _cmbFinish.SelectedIndex != 1;

    /// <summary>The new class's format from the answers - see RaceFormat.ForNewClass(appDefault, minutes, finishTheLap).</summary>
    public RaceFormat? Format() => RaceFormat.ForNewClass(_appDefault, Minutes, FinishTheLap);

    /// <summary>The line under the two rows: what each finish means, and - for a club racing to a lap count - what leaving it alone does.</summary>
    internal static string HintFor(RaceFormat? appDefault)
    {
        var meaning = "\"Finish the lap\": when the time is up the leader takes the flag and everyone else finishes the lap they are on. "
                      + "\"Stop\": the race ends at the time.";

        return appDefault is { LapLimit: > 0, TimeLimitMinutes: 0 } laps
            ? $"{meaning} The club's default is {Plural.Of(laps.LapLimit, "lap")}; leave these as they are to race that, or change them to race to a time."
            : meaning;
    }

    /// <summary>Adds the two rows and the hint to a window's own field grid.</summary>
    public void AddTo(FieldGrid grid)
    {
        _lblMinutesCaption = grid.AddRow("Race length:", FieldGrid.Inline(_numMinutes, _lblUnit));
        _lblFinishCaption = grid.AddRow("When time's up:", _cmbFinish);
        grid.AddWide(_lblHint);
    }

    /// <summary>The two rows and the hint in a grid of their own, for a window that has none.</summary>
    public FieldGrid Grid()
    {
        var grid = new FieldGrid();
        AddTo(grid);
        return grid;
    }

    /// <summary>
    /// Shows a format set some other way - Format or a sanctioning
    /// preset - and greys the rows, because that format is what will be saved.
    /// </summary>
    public void ShowSetElsewhere(RaceFormat? format)
    {
        var effective = format ?? _appDefault;

        if (effective is { TimeLimitMinutes: > 0 } timed)
        {
            _numMinutes.Value = Math.Clamp(timed.TimeLimitMinutes, 1, 180);
        }

        _cmbFinish.SelectedIndex = (effective?.FinishOnNextCrossing ?? true) ? 0 : 1;
        _numMinutes.Enabled = false;
        _cmbFinish.Enabled = false;
        _lblHint.Text = $"Set under Format: {effective?.Describe() ?? "the app default"}.";

        foreach (var caption in new[] { _lblMinutesCaption, _lblFinishCaption })
        {
            if (caption is not null)
            {
                caption.ForeColor = AppTheme.TextMuted;
            }
        }
    }

    /// <summary>The fields' widths, after the theme pass: the minutes box a number box's, the finish picker its longer choice's.</summary>
    public void SizeFields()
    {
        _numMinutes.Width = Math.Max(
            UiLayout.AtTextSize(UiLayout.NumericWidth),
            TextRenderer.MeasureText("000", _numMinutes.Font).Width + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra);

        (Font Font, int Count, int Width)? kept = null;
        _cmbFinish.Width = Math.Max(UiLayout.AtTextSize(UiLayout.FieldWidth), Pieces.PickerWidth(_cmbFinish, ref kept));
    }
}
