namespace HeatCaller;

/// <summary>
/// Edits a class's warning ladder: up to five rungs, each "warning only",
/// "time penalty" or "black flag". A rung set to "(end)" ends the ladder
/// there, and the last real rung repeats for any warning after it.
///
/// On the layout pieces (DialogFit): the hint across the top, then the five
/// rungs in a field grid - each one's caption, and beside it what it does,
/// its seconds and the word "seconds" on one line - in a body that scrolls
/// on a screen too short for them; Use Default on the left at the foot, and
/// Save and Cancel against the right-hand edge. It used to be placed in
/// pixels in a 460px window: the fields at fixed x's past captions that a
/// larger text size ran into, and Use Default and Save - each a standard
/// button's width at the normal text size - overlapping by 26px.
/// </summary>
public sealed class WarningLadderForm : Form
{
    private const int MaxSteps = 5;

    /// <summary>How wide the window is inside its frame at the normal text size, as it was made - or wider, when the rungs or the buttons need more (see the constructor).</summary>
    private const int DialogWidth = 460;

    /// <summary>A rung's drop-down at the normal text size, as it was made - or wider, when its longest choice needs more.</summary>
    private const int ActionWidth = 150;

    private readonly ComboBox[] _actions = new ComboBox[MaxSteps];
    private readonly NumericUpDown[] _seconds = new NumericUpDown[MaxSteps];

    /// <summary>The ladder chosen, or null for "follow the default".</summary>
    public List<WarningStep>? Result { get; private set; }

    private static readonly string[] ActionNames = { "Warning only", "Time penalty", "Black flag (DQ)", "(end)" };

    public WarningLadderForm(IReadOnlyList<WarningStep>? current, string title)
    {
        Text = title;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var hint = new Label
        {
            Text = "What each warning in a race costs the driver. After the last step, that step repeats. "
                   + "A black flag is announced and the saved result is marked DQ.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        var steps = current is { Count: > 0 } ? current : WarningRules.DefaultLadder();

        // A rung to a row: its caption, and beside it what it does, its
        // seconds and the word "seconds" together on one line
        // (FieldGrid.Inline). The captions and the units are kept for the
        // width worked out below.
        var fields = new FieldGrid();
        var captions = new Label[MaxSteps];
        var units = new Label[MaxSteps];

        for (var i = 0; i < MaxSteps; i++)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            combo.Items.AddRange(ActionNames);

            var seconds = new NumericUpDown { Minimum = 1, Maximum = 600, Value = 5 };
            var unit = new Label { Text = "seconds", AutoSize = true };

            if (i < steps.Count)
            {
                // Clamped to the three real actions: the ladder comes from a
                // class saved on disk, and an action number this build does
                // not know (a newer build's, or a hand edit) threw here and
                // the editor never opened. 3 would read as "(end)".
                combo.SelectedIndex = Math.Clamp((int)steps[i].Action, 0, 2);
                seconds.Value = Math.Clamp(steps[i].PenaltySeconds <= 0 ? 5 : steps[i].PenaltySeconds, 1, 600);
            }
            else
            {
                combo.SelectedIndex = 3;
            }

            combo.SelectedIndexChanged += (_, _) => UpdateEnabled();

            _actions[i] = combo;
            _seconds[i] = seconds;
            units[i] = unit;

            captions[i] = fields.AddRow($"{WarningRules.Ordinal(i + 1)} warning:", FieldGrid.Inline(combo, seconds, unit));
        }

        var btnDefault = new Button { Text = "Use Default" };
        btnDefault.Click += (_, _) =>
        {
            Result = null;
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        var btnSave = new Button { Text = "Save" };
        btnSave.Click += (_, _) =>
        {
            var ladder = new List<WarningStep>();
            for (var i = 0; i < MaxSteps; i++)
            {
                if (_actions[i].SelectedIndex is < 0 or 3)
                {
                    break;
                }

                var action = (WarningAction)_actions[i].SelectedIndex;
                ladder.Add(new WarningStep(action, action == WarningAction.TimePenalty ? (int)_seconds[i].Value : 0));
            }

            if (ladder.Count == 0)
            {
                AppDialog.Show(this, "The first warning needs an action - pick \"Warning only\" if it should just be a warning.",
                    "Nothing set", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Result = ladder;
            DialogResult = DialogResult.OK;
            Close();
        };

        // The hint and the rungs are the body: on a screen too short for
        // them they scroll, and the buttons under them stay in view.
        var page = new FillPage();
        var body = DialogFit.Body(page);
        body.Add(hint, gapBelow: UiLayout.RowGap);
        body.Add(fields, gapBelow: 0);

        // Use Default on the left, Save and Cancel against the right-hand
        // edge, Cancel furthest right, as they were - on one line (SplitBar;
        // see the width worked out below). Tab still goes Use Default, Save,
        // Cancel, as it did: the right-hand bar is handed Cancel first, as
        // its first button goes furthest right, and would take it first -
        // so the order of the two is set here.
        var defaultBar = new ButtonBar().With(btnDefault);
        var exitBar = new ButtonBar(alignRight: true).With(btnCancel, btnSave);

        page.Add(new SplitBar(defaultBar, exitBar), gapBelow: 0);
        btnSave.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        CancelButton = btnCancel;

        UpdateEnabled();
        AppTheme.ApplyRecursively(this);

        // The fields' widths, in the faces the theme pass leaves them in:
        // the drop-downs as wide as they were made at the text size in use,
        // or as their longest choice needs; the seconds as wide as a number
        // box at the text size in use, or as their largest value needs. One
        // width for each kind, so the rungs line up down the grid. Kept in
        // locals, never read back from the fields - see the width below.
        (Font Font, int Count, int Width)? actionKept = null;
        var actionWidth = UiLayout.AtTextSize(ActionWidth);

        foreach (var combo in _actions)
        {
            actionWidth = Math.Max(actionWidth, Pieces.PickerWidth(combo, ref actionKept));
        }

        var digits = ((int)_seconds[0].Maximum).ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
        var secondsWidth = Math.Max(
            UiLayout.AtTextSize(UiLayout.NumericWidth),
            TextRenderer.MeasureText(new string('0', digits), _seconds[0].Font).Width + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra);

        for (var i = 0; i < MaxSteps; i++)
        {
            _actions[i].Width = actionWidth;
            _seconds[i].Width = secondsWidth;
        }

        // As wide as it was made, at the text size in use - or wider, when a
        // rung needs more to stay on one line beside the captions, or Use
        // Default, Save and Cancel to share one. At 460 the three buttons
        // came to 498px in a line of 428 at the normal text size, and Save
        // and Cancel would have gone onto a line of their own. Each rung
        // measured the way its row lays it out: every part with the gap the
        // row keeps after it; the caption column as the grid works it out,
        // its widest caption and the gap after it; and the two bars the way
        // the SplitBar decides, each on one line and a section gap between.
        var captionColumn = captions.Max(caption => caption.GetPreferredSize(Size.Empty).Width + caption.Margin.Horizontal);

        var rung = Enumerable.Range(0, MaxSteps).Max(i =>
            actionWidth + _actions[i].Margin.Horizontal
            + secondsWidth + _seconds[i].Margin.Horizontal
            + units[i].GetPreferredSize(Size.Empty).Width + units[i].Margin.Horizontal);

        var buttons = defaultBar.OneLineWidth + UiLayout.SectionGap + exitBar.OneLineWidth;

        var width = Math.Max(
            UiLayout.AtTextSize(DialogWidth),
            UiLayout.PageLeft + Math.Max(captionColumn + rung, buttons) + UiLayout.PageRight);

        DialogFit.Keep(this, page, width, body: body, widthMeasured: true);
    }

    /// <summary>Seconds only for penalty rungs; everything after the first "(end)" greyed out.</summary>
    private void UpdateEnabled()
    {
        var ended = false;
        for (var i = 0; i < MaxSteps; i++)
        {
            _actions[i].Enabled = !ended;
            _seconds[i].Enabled = !ended && _actions[i].SelectedIndex == (int)WarningAction.TimePenalty;

            if (_actions[i].SelectedIndex is < 0 or 3)
            {
                ended = true;
            }
        }
    }
}
