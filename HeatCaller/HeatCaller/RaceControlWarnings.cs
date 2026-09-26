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
        using var dialog = new Form
        {
            Text = $"Warning for {driverName}",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(380, 116),
            BackColor = AppTheme.Background,
            Font = AppTheme.BodyFont,
        };

        var label = new Label { Text = "Reason:", Location = new Point(12, 18), AutoSize = true };
        var text = new TextBox { Location = new Point(80, 15), Width = 288, MaxLength = 80 };

        var ok = new Button { Text = "Warn", Location = new Point(198, 66), Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Location = new Point(288, 66), Width = 80, DialogResult = DialogResult.Cancel };

        dialog.Controls.AddRange(new Control[] { label, text, ok, cancel });
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        AppTheme.ApplyRecursively(dialog);

        return dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(text.Text)
            ? text.Text.Trim()
            : null;
    }
}
