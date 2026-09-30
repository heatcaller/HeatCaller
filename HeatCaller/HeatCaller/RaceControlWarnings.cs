namespace HeatCaller;

/// <summary>
/// Race Control's warnings: the reasons menu (toolbar button, right-click
/// and the W key all open the same list), the call-out when one is given,
/// and the black flag becoming a DQ when the result is saved.
///
/// The session does the bookkeeping - see RaceControlSession.IssueWarning,
/// which also applies the class's ladder. This file is only the page's
/// side of it.
/// </summary>
public sealed partial class RaceControlControl
{
    /// <summary>
    /// Keyboard entry point (MainForm's W): opens the reasons list at the
    /// selected driver's row, so 1-7 picks a reason without the mouse.
    /// </summary>
    public void TriggerWarning()
    {
        var row = _dgvLeaderboard.CurrentRow;

        if (row is null || SelectedTransponder() is null)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] W pressed with no driver selected - click a driver on the board first.");
            return;
        }

        var cell = _dgvLeaderboard.GetCellDisplayRectangle(Math.Max(0, DriverColumnIndex()), row.Index, cutOverflow: true);
        var at = cell.IsEmpty ? new Point(24, 24) : new Point(cell.Left + 12, cell.Bottom);

        ShowWarningMenu(_dgvLeaderboard, at);
    }

    /// <summary>Pops the reasons list up for the selected driver, anchored to a control.</summary>
    private void ShowWarningMenu(Control anchor, Point at)
    {
        var transponder = SelectedTransponder();

        if (transponder is null)
        {
            AppDialog.Show(this, "Select a racer in the leaderboard first.", "No racer selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (FindStanding(transponder) is null)
        {
            return;
        }

        // Built fresh each time and thrown away on close: it lists this
        // driver's name, count and next step, all of which change.
        var menu = new ContextMenuStrip();
        FillWarningItems(menu.Items, transponder);
        AppTheme.StyleContextMenu(menu);
        menu.Closed += (_, _) => BeginInvoke(() => menu.Dispose());
        menu.Show(anchor, at);
    }

    /// <summary>
    /// The reasons list for one driver: a heading saying what this warning
    /// will cost, the preset reasons numbered 1-7 (so a key picks one),
    /// "Other reason...", and taking the last warning back.
    /// </summary>
    private void FillWarningItems(ToolStripItemCollection items, string transponder)
    {
        items.Clear();

        var standing = FindStanding(transponder);
        var name = DriverNameFor(transponder);
        var count = standing?.Warnings.Count ?? 0;
        var next = _race.NextWarningStep(transponder);

        var heading = new ToolStripMenuItem($"{WarningRules.Ordinal(count + 1)} warning for {name}: {next.Describe()}")
        {
            Enabled = false,
        };
        items.Add(heading);
        items.Add(new ToolStripSeparator());

        for (var i = 0; i < WarningRules.PresetReasons.Length; i++)
        {
            var reason = WarningRules.PresetReasons[i];
            var item = new ToolStripMenuItem($"&{i + 1}  {reason}");
            item.Click += (_, _) => GiveWarning(transponder, reason);
            items.Add(item);
        }

        var other = new ToolStripMenuItem("&0  Other reason...");
        other.Click += (_, _) =>
        {
            if (AskForReason(name) is { } typed)
            {
                GiveWarning(transponder, typed);
            }
        };
        items.Add(other);

        items.Add(new ToolStripSeparator());

        var last = standing is { Warnings.Count: > 0 } ? standing.Warnings[^1] : null;
        var remove = new ToolStripMenuItem(last is null ? "Remove last warning" : $"Remove last warning ({last.Summary()})")
        {
            Enabled = last is not null,
        };
        remove.Click += (_, _) => TakeBackLastWarning(transponder);
        items.Add(remove);

        // Styled here rather than by AppTheme.StyleContextMenu: the leaderboard
        // menu refills this list every time it opens, and restyling the whole
        // menu each time would stack its resize handlers.
        foreach (ToolStripItem entry in items)
        {
            entry.BackColor = AppTheme.Surface;
            entry.ForeColor = entry.Enabled ? AppTheme.TextDark : AppTheme.TextMuted;
            entry.Font = AppTheme.BodyFont;

            if (entry is ToolStripMenuItem menuItem)
            {
                menuItem.Padding = new Padding(0, 3, 0, 3);
            }
        }

        heading.Font = AppTheme.BoldFont;
    }

    /// <summary>Gives the warning, with an undo that takes exactly this one back.</summary>
    private void GiveWarning(string transponder, string reason)
    {
        var warning = _race.IssueWarning(transponder, reason);

        if (warning is null)
        {
            return;
        }

        var name = DriverNameFor(transponder);

        _undo.Capture($"Warning for {name}", () =>
        {
            // Only if it is still the newest - an undo from further back
            // must not take a later warning off instead.
            if (FindStanding(transponder) is { Warnings.Count: > 0 } s && ReferenceEquals(s.Warnings[^1], warning))
            {
                _race.RemoveLastWarning(transponder);
                RenderLeaderboard();
            }
        });

        RenderLeaderboard();
    }

    private void TakeBackLastWarning(string transponder)
    {
        RaceWarning? removed = null;

        // Through EditStanding so the app's Undo can put the warning - and
        // any time it had added - back.
        var edit = _race.EditStanding(transponder, () =>
        {
            removed = _race.RemoveLastWarning(transponder);
            return removed is not null;
        });

        if (edit is not null && removed is not null)
        {
            CaptureStandingEditUndo($"Take back {DriverNameFor(transponder)}'s warning", edit);
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Took back {DriverNameFor(transponder)}'s {removed.Summary()}.");
            RenderLeaderboard();
        }
    }

    /// <summary>A warning was given - from the menu, the key, or anywhere else that calls the session. Says it, logs it, shows it.</summary>
    private void OnWarningIssued(RacerStanding standing, RaceWarning warning)
    {
        var name = DriverNameFor(standing.TransponderId);

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] {name}: {warning.Summary()}.");

        var reason = string.IsNullOrWhiteSpace(warning.Reason) ? string.Empty : $" {warning.Reason}.";

        var line = warning.Action switch
        {
            WarningAction.BlackFlag => $"Black flag, {name}.{reason} Please bring your car in and report to race control.",
            WarningAction.TimePenalty => $"Warning, {name}.{reason} That's a {warning.PenaltyMs / 1000.0:0.#} second penalty.",
            _ => $"Warning, {name}.{reason}",
        };

        _announcer.NotifyWarning(line, warning.Action == WarningAction.BlackFlag);
        RenderLeaderboard();
    }

    /// <summary>
    /// Turns any black flag in the finished race into a DQ on the result
    /// about to be saved, through ResultEditor so it lands in the result's
    /// correction log (and can be put back from Race History).
    /// </summary>
    private void ApplyBlackFlags(SessionRecord record)
    {
        if (record.IsDemoSession())
        {
            return;
        }

        var flagged = record.Results
            .Where(r => r.Warnings.Any(w => w.Action == WarningAction.BlackFlag))
            .ToList();

        if (flagged.Count == 0)
        {
            return;
        }

        var adjustments = flagged
            .Select(r => ResultEditor.CurrentOf(r) with { Outcome = ResultEditor.Dq })
            .ToList();

        if (ResultEditor.Validate(record, adjustments) is { } problem)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [session] Black flag not applied to the result - {problem}");
            return;
        }

        ResultEditor.Apply(record, adjustments, "Black flag from race control (warning limit reached)", "Laptop");

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [session] Black flag: "
            + string.Join(", ", flagged.Select(r => string.IsNullOrWhiteSpace(r.DriverName) ? r.Transponder : r.DriverName))
            + " marked DQ in the saved result. Race History > Edit Result puts it back if that was wrong.");
    }

    private int DriverColumnIndex() =>
        _kpiColumns.FindIndex(k => string.Equals(k.Key, "driver", StringComparison.OrdinalIgnoreCase));

    private RacerStanding? FindStanding(string transponder) =>
        _race.GetOrderedStandings().FirstOrDefault(s =>
            string.Equals(s.TransponderId, transponder, StringComparison.OrdinalIgnoreCase));

    private string DriverNameFor(string transponder)
    {
        var name = _profiles.Get(transponder)?.DriverName;
        return string.IsNullOrWhiteSpace(name) ? transponder : name;
    }

    /// <summary>A one-line reason typed by hand, or null if cancelled or left empty.</summary>
    private string? AskForReason(string driverName)
    {
        using var dialog = new WarningReasonForm(driverName);

        return dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.Reason)
            ? dialog.Reason.Trim()
            : null;
    }

    /// <summary>
    /// The window the warnings list's "Other reason..." opens: a reason of
    /// one line, typed by hand. A class of its own rather than built inside
    /// AskForReason, and internal, so the layout test can build it
    /// (WindowCatalog) without a race on the board.
    ///
    /// On the layout pieces (DialogFit): the caption and the box on one line
    /// (FieldRow), the box running to the right-hand edge, and Warn and
    /// Cancel at the foot; as tall as that. It used to be placed in pixels in
    /// a 380x116 window: the box at a fixed 80px from the left and 288px
    /// wide beside a caption that a larger text size ran into, and the
    /// buttons a fixed 80px wide at fixed places.
    /// </summary>
    internal sealed class WarningReasonForm : Form
    {
        /// <summary>How wide the window is inside its frame at the normal text size.</summary>
        private const int DialogWidth = 380;

        private readonly TextBox _txt = new() { MaxLength = 80 };

        /// <summary>What was typed, as typed - the caller trims it. Only meaningful after DialogResult.OK.</summary>
        public string Reason => _txt.Text;

        /// <param name="driverName">Who the warning is for, for the title.</param>
        public WarningReasonForm(string driverName)
        {
            Text = $"Warning for {driverName}";
            BackColor = AppTheme.Background;
            Font = AppTheme.BodyFont;

            var ok = new Button { Text = "Warn", DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

            // A FieldRow rather than a field grid: it keeps the pixel round
            // the box that the theme draws its ring in (Pieces.RingRoom), top
            // and bottom as well - a grid's last row loses the bottom of it.
            // The box takes the rest of the line. The least it can do with -
            // a standard text box's width - is only what the row asks for
            // when it is measured with no width to fit: the only pair on its
            // line, it never moves onto one of its own.
            var row = new FieldRow();
            row.AddLast(new Label { Text = "Reason:" }, _txt, () => UiLayout.AtTextSize(UiLayout.FieldWidth));

            var page = new FillPage();
            page.Add(row);

            // Against the right-hand edge, Cancel furthest right, as they
            // were - and Tab still reaches Warn first: the bar takes them
            // right to left, in the order they were added.
            page.Add(new ButtonBar(alignRight: true).With(cancel, ok), gapBelow: 0);
            ok.TabIndex = 0;
            cancel.TabIndex = 1;

            Controls.Add(page);

            AcceptButton = ok;
            CancelButton = cancel;

            AppTheme.ApplyRecursively(this);

            // The window's own button in the app's colour, as OK and Save are
            // in every other window: the theme goes by the caption, and
            // "Warn" is not one it knows, so it came out grey beside a grey
            // Cancel.
            AppTheme.StyleButton(ok, AppTheme.ButtonKind.Primary);

            DialogFit.Keep(this, page, width: DialogWidth);
        }
    }
}
