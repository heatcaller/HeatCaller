namespace HeatCaller;

/// <summary>
/// Import Entries on Who's Racing (11 Nov 2026, race-night build plan 7.1):
/// tonight's entry list - an RCSignup export, a LiveTime driver export or
/// any CSV, or a LiveRC entry list copied off the page - matched onto the
/// drivers here, their classes added, and everyone on it checked in.
///
/// WHY HERE AND NOT ON DRIVER HUB. Driver Hub's import is about the roster;
/// this one is about tonight, and its last step is the check-in, which
/// lives on this page. It reads the file and the clipboard exactly as
/// Driver Hub does (CsvHelper, DriverImport, PastedTable) and shows the
/// same column picks (CsvMappingForm, in its entry mode), so a club only
/// learns one way of doing it - then shows what will happen (EntryImportForm)
/// before anything is saved, and the whole of it is one step for Undo.
/// </summary>
public sealed partial class CheckInControl
{
    /// <summary>The button's menu: from a file, or from the clipboard.</summary>
    private ContextMenuStrip BuildEntriesMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add(new ToolStripMenuItem("From a CSV File...", null, (_, _) => ImportEntriesFromFile()));
        menu.Items.Add(new ToolStripMenuItem("Paste a Table (LiveRC Entry List)...", null, (_, _) => ImportEntriesFromClipboard()));

        AppTheme.StyleContextMenu(menu);
        return menu;
    }

    private void ImportEntriesFromFile()
    {
        using var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt|All files (*.*)|*.*" };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // As Driver Hub reads one: bytes decoded by CsvHelper (Excel's plain
        // CSV is in the Windows code page), blank lines dropped but each line
        // keeping its number for "Row 12".
        List<(string Text, int LineNumber)> lines;

        try
        {
            lines = CsvHelper.NonBlankLines(CsvHelper.DecodeText(File.ReadAllBytes(dialog.FileName)));
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't read that file: {ex.Message}", "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (lines.Count < 2)
        {
            AppDialog.Show(this,
                lines.Count == 0 ? "That file is empty." : "That file has a heading row and nothing under it.",
                "Nothing to import", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var delimiter = DriverImport.DetectDelimiter(lines[0].Text);
        var headers = DriverImport.ParseLine(lines[0].Text, delimiter);
        var rows = lines.Skip(1).Select(line => DriverImport.ParseLine(line.Text, delimiter)).ToList();

        ImportEntries(Path.GetFileName(dialog.FileName), headers, rows, lines.Skip(1).Select(line => line.LineNumber).ToList(), fromFile: true);
    }

    private void ImportEntriesFromClipboard()
    {
        string? html = null;
        string? text = null;

        try
        {
            if (Clipboard.ContainsText(TextDataFormat.Html))
            {
                html = Clipboard.GetText(TextDataFormat.Html);
            }

            if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
            {
                text = Clipboard.GetText(TextDataFormat.UnicodeText);
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException)
        {
            AppDialog.Show(this, $"Couldn't read the clipboard just now ({ex.Message}). Try again.",
                "Import entries", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // PastedTable reads a LiveRC entry list's one-table-per-class page
        // into one table with a Class column, the doubled names undoubled.
        if (PastedTable.Read(html, text) is not { } table)
        {
            AppDialog.Show(this,
                "There's no table on the clipboard to import.\n\n"
                + "On the entry list's web page (LiveRC's, say) or in the spreadsheet, select the whole list - its headings "
                + "and every driver's row - and copy it (Ctrl+C). Then choose Import Entries > Paste a Table here.",
                "Nothing to paste", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var source = PastedResult.SourceName(html) == "LiveRC" ? "a LiveRC entry list" : "a pasted table";

        ImportEntries(source, table.Headers.ToList(), table.Rows.ToList(), table.RowNumbers.ToList(), fromFile: false);
    }

    /// <summary>The column picks, the preview, and then the import itself - with its one Undo.</summary>
    private void ImportEntries(string source, List<string> headers, List<List<string>> rows, List<int> lineNumbers, bool fromFile)
    {
        using var mapping = new CsvMappingForm(source, headers, rows, DriverImport.GuessAll(headers), Array.Empty<Season>(),
            fromFile ? "in the file" : "in the table", entries: true);

        if (mapping.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var classes = _raceClasses.All.Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        var entryRows = EntryImport.ReadRows(rows, lineNumbers, mapping.Columns, classes);

        // What the check-in tick can do tonight: a closed day takes none
        // (CheckInStore.ClosedCurrentDay), and with no race day open the
        // desk still checks in, as it does by hand.
        string? checkInTo = CheckInStore.ClosedCurrentDay(_events) is not null
            ? null
            : _events?.Active is { IsLegacy: false } day ? $"\"{day.DisplayName}\"" : string.Empty;

        using var preview = new EntryImportForm(source, entryRows, _profiles.All.ToList(), classes, checkInTo);

        if (preview.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var plan = preview.Plan;

        // Only the check-ins this import makes are undone - a driver who was
        // checked in already stays checked in - and at the day they were
        // made, whatever day is current when Undo is pressed.
        var eventId = _checkIns.CurrentEventId;
        var newCheckIns = preview.CheckIn
            ? plan.CheckIns.Where(t => !_checkIns.IsCheckedIn(t)).ToList()
            : new List<string>();

        if (plan.Saves.Count > 0 || newCheckIns.Count > 0)
        {
            var store = _profiles;
            var before = plan.Before;
            var added = plan.Added;

            _undo.Capture($"Import {Plural.Of(plan.CheckIns.Count, "entry", "entries")} from {source}", () =>
            {
                var putBack = plan.Saves.Count > 0 ? store.PutBack(before, added) : 0;

                if (newCheckIns.Count > 0)
                {
                    _checkIns.RemoveCheckIns(eventId, newCheckIns);

                    if (string.Equals(eventId, _checkIns.CurrentEventId, StringComparison.Ordinal))
                    {
                        RaiseDeskChanges(newCheckIns);
                    }
                }

                if (putBack == 0 && newCheckIns.Count == 0)
                {
                    throw new InvalidOperationException("every driver that import touched has been changed or deleted since, so there is nothing to put back.");
                }

                if (!IsDisposed && !Disposing)
                {
                    RefreshAll();
                }
            });
        }

        if (plan.Saves.Count > 0)
        {
            _profiles.UpsertMany(plan.Saves);
        }

        if (newCheckIns.Count > 0)
        {
            _checkIns.CheckInAll(newCheckIns);
            RaiseDeskChanges(newCheckIns);
        }

        RefreshAll();

        var summary = plan.Summary();

        if (preview.CheckIn)
        {
            summary += $"\n\n{Plural.Of(newCheckIns.Count, "driver")} checked in"
                       + (plan.CheckIns.Count > newCheckIns.Count ? $" ({plan.CheckIns.Count - newCheckIns.Count} already were)." : ".");
        }

        AppDialog.Show(this, summary, "Entries imported", MessageBoxButtons.OK,
            plan.Skipped > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }
}
