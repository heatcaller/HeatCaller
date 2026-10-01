namespace HeatCaller;

/// <summary>What a warning costs the driver, decided by where it falls on the class's ladder.</summary>
public enum WarningAction
{
    /// <summary>Logged, announced and shown - nothing more.</summary>
    Warning,

    /// <summary>Seconds added to the driver's time, exactly like the +5s / +10s buttons.</summary>
    TimePenalty,

    /// <summary>Called in: announced as a black flag, and the saved result is marked DQ.</summary>
    BlackFlag,
}

/// <summary>One rung of a warning ladder: what the Nth warning in a race does.</summary>
public sealed class WarningStep
{
    public WarningAction Action { get; set; }

    /// <summary>Only used by TimePenalty.</summary>
    public int PenaltySeconds { get; set; }

    public WarningStep()
    {
    }

    public WarningStep(WarningAction action, int penaltySeconds = 0)
    {
        Action = action;
        PenaltySeconds = penaltySeconds;
    }

    /// <summary>For settings and menus - "Warning only", "+5s penalty", "Black flag (DQ)".</summary>
    public string Describe() => Action switch
    {
        WarningAction.TimePenalty => $"+{PenaltySeconds}s penalty",
        WarningAction.BlackFlag => "Black flag (DQ)",
        _ => "Warning only",
    };
}

/// <summary>
/// One warning given to a driver during a race. Plain settable properties
/// so it round-trips through the race journal and the saved history as-is.
/// </summary>
public sealed class RaceWarning
{
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>How far into the race it was given, in ms. Zero before the green.</summary>
    public long RaceTimeMs { get; set; }

    /// <summary>1 for the driver's first warning of the race, 2 for the second...</summary>
    public int Number { get; set; }

    public string Reason { get; set; } = string.Empty;

    public WarningAction Action { get; set; }

    /// <summary>The time this warning added, if its step was a penalty. Kept so removing it gives exactly that back.</summary>
    public long PenaltyMs { get; set; }

    /// <summary>"Warning 2 - Blocking (+5s)" - for tooltips, logs and history.</summary>
    public string Summary()
    {
        var reason = string.IsNullOrWhiteSpace(Reason) ? "no reason given" : Reason;
        var cost = Action switch
        {
            WarningAction.TimePenalty => $" (+{PenaltyMs / 1000.0:0.#}s)",
            WarningAction.BlackFlag => " (BLACK FLAG)",
            _ => string.Empty,
        };

        var at = RaceTimeMs > 0 ? $" at {RaceTimeMs / 60000}:{RaceTimeMs / 1000 % 60:00}" : string.Empty;
        return $"Warning {Number} - {reason}{cost}{at}";
    }
}

/// <summary>The preset reasons and the default ladder, shared by every place a warning is given or shown.</summary>
public static class WarningRules
{
    /// <summary>One click each from the menu. Anything else goes through "Other reason...".</summary>
    public static readonly string[] PresetReasons =
    {
        "Rough driving",
        "Track cutting",
        "Blocking",
        "Unsportsmanlike conduct",
        "Ignoring the marshal",
        "Jump start",
        "Not marshalling",
    };

    /// <summary>1st: a warning. 2nd: +5s. 3rd: +10s. 4th: black flag.</summary>
    public static List<WarningStep> DefaultLadder() => new()
    {
        new WarningStep(WarningAction.Warning),
        new WarningStep(WarningAction.TimePenalty, 5),
        new WarningStep(WarningAction.TimePenalty, 10),
        new WarningStep(WarningAction.BlackFlag),
    };

    /// <summary>
    /// What the Nth warning (1-based) does. Past the end of the ladder the
    /// last rung repeats - a fifth warning on a ladder ending +10s is
    /// another +10s, not a free pass.
    /// </summary>
    public static WarningStep StepFor(IReadOnlyList<WarningStep>? ladder, int number)
    {
        var steps = ladder is { Count: > 0 } ? ladder : DefaultLadder();
        return steps[Math.Clamp(number, 1, steps.Count) - 1];
    }

    /// <summary>The mark after a driver's name on every board - empty for a clean driver.</summary>
    public static string Badge(IReadOnlyCollection<RaceWarning> warnings)
    {
        if (warnings.Count == 0)
        {
            return string.Empty;
        }

        return warnings.Any(w => w.Action == WarningAction.BlackFlag)
            ? "  ⚑ BLACK FLAG"
            : $"  ⚠{warnings.Count}";
    }

    /// <summary>"1st", "2nd", "3rd", "4th"...</summary>
    public static string Ordinal(int n) =>
        ((n % 100) is 11 or 12 or 13) ? $"{n}th"
        : (n % 10) switch { 1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th" };
}

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
