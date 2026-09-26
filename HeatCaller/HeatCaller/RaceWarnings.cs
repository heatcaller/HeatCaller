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
/// </summary>
public sealed class WarningLadderForm : Form
{
    private const int MaxSteps = 5;

    private readonly ComboBox[] _actions = new ComboBox[MaxSteps];
    private readonly NumericUpDown[] _seconds = new NumericUpDown[MaxSteps];

    /// <summary>The ladder chosen, or null for "follow the default".</summary>
    public List<WarningStep>? Result { get; private set; }

    private static readonly string[] ActionNames = { "Warning only", "Time penalty", "Black flag (DQ)", "(end)" };

    public WarningLadderForm(IReadOnlyList<WarningStep>? current, string title)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var hint = new Label
        {
            Text = "What each warning in a race costs the driver. After the last step, that step repeats. "
                   + "A black flag is announced and the saved result is marked DQ.",
            Location = new Point(16, 14),
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };
        Controls.Add(hint);

        var steps = current is { Count: > 0 } ? current : WarningRules.DefaultLadder();
        var top = hint.Top + hint.PreferredHeight + 14;

        for (var i = 0; i < MaxSteps; i++)
        {
            var label = new Label { Text = $"{WarningRules.Ordinal(i + 1)} warning:", Location = new Point(16, top + 4), AutoSize = true };

            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(130, top), Width = 150 };
            combo.Items.AddRange(ActionNames);

            var seconds = new NumericUpDown { Location = new Point(292, top), Width = 64, Minimum = 1, Maximum = 600, Value = 5 };
            var unit = new Label { Text = "seconds", Location = new Point(362, top + 4), AutoSize = true };

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
            Controls.AddRange(new Control[] { label, combo, seconds, unit });

            top += 34;
        }

        var btnDefault = new Button { Text = "Use Default" };
        btnDefault.Size = new Size(UiLayout.WidthFor(btnDefault), UiLayout.ButtonHeight);
        btnDefault.Location = new Point(16, top + 10);
        btnDefault.Click += (_, _) =>
        {
            Result = null;
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        btnCancel.Size = new Size(UiLayout.WidthFor(btnCancel), UiLayout.ButtonHeight);

        var btnSave = new Button { Text = "Save" };
        btnSave.Size = new Size(UiLayout.WidthFor(btnSave), UiLayout.ButtonHeight);
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

        ClientSize = new Size(460, btnDefault.Bottom + 16);
        btnCancel.Location = new Point(ClientSize.Width - 16 - btnCancel.Width, btnDefault.Top);
        btnSave.Location = new Point(btnCancel.Left - UiLayout.ButtonGap - btnSave.Width, btnDefault.Top);

        Controls.AddRange(new Control[] { btnDefault, btnSave, btnCancel });
        CancelButton = btnCancel;

        UpdateEnabled();
        AppTheme.ApplyRecursively(this);
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
