namespace HeatCaller;

/// <summary>
/// Reading an entry list - a CSV file, or a table copied from a web page or
/// a spreadsheet - for Import Entries (11 Nov 2026) on Who's Racing, and for
/// the first-run wizard's Drivers step (12 Nov 2026, setup plan C2). One
/// copy, so the two never read a list differently.
/// </summary>
internal static class EntryImportFlow
{
    /// <summary>A list read and ready for its column picks: where it came from, its headings and rows, and each row's line.</summary>
    internal sealed record Read(string Source, List<string> Headers, List<List<string>> Rows, List<int> LineNumbers, bool FromFile);

    /// <summary>Asks for a CSV file and reads it, or null (cancelled, or nothing in it - already said).</summary>
    internal static Read? FromFile(IWin32Window owner)
    {
        using var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt|All files (*.*)|*.*" };

        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            return null;
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
            AppDialog.Show(owner, $"Couldn't read that file: {ex.Message}", "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }

        if (lines.Count < 2)
        {
            AppDialog.Show(owner,
                lines.Count == 0 ? "That file is empty." : "That file has a heading row and nothing under it.",
                "Nothing to import", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        var delimiter = DriverImport.DetectDelimiter(lines[0].Text);
        var headers = DriverImport.ParseLine(lines[0].Text, delimiter);
        var rows = lines.Skip(1).Select(line => DriverImport.ParseLine(line.Text, delimiter)).ToList();

        return new Read(Path.GetFileName(dialog.FileName), headers, rows, lines.Skip(1).Select(line => line.LineNumber).ToList(), FromFile: true);
    }

    /// <summary>Reads a table off the clipboard, or null (none there - already said).</summary>
    internal static Read? FromClipboard(IWin32Window owner)
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
            AppDialog.Show(owner, $"Couldn't read the clipboard just now ({ex.Message}). Try again.",
                "Import entries", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        // PastedTable reads a LiveRC entry list's one-table-per-class page
        // into one table with a Class column, the doubled names undoubled.
        if (PastedTable.Read(html, text) is not { } table)
        {
            AppDialog.Show(owner,
                "There's no table on the clipboard to import.\n\n"
                + "On the entry list's web page (LiveRC's, say) or in the spreadsheet, select the whole list - its headings "
                + "and every driver's row - and copy it (Ctrl+C). Then choose Paste a Table here.",
                "Nothing to paste", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        var source = PastedResult.SourceName(html) == "LiveRC" ? "a LiveRC entry list" : "a pasted table";

        return new Read(source, table.Headers.ToList(), table.Rows.ToList(), table.RowNumbers.ToList(), FromFile: false);
    }

    /// <summary>
    /// The column picks and the preview for a list read - the plan to carry
    /// out, and whether to check them in - or null when either was cancelled.
    /// </summary>
    /// <param name="checkInTo">What the preview's check-in tick names, or null for no tick (see EntryImportForm).</param>
    internal static (EntryImport.EntryPlan Plan, bool CheckIn)? Preview(
        IWin32Window owner, Read list, IReadOnlyList<DriverProfile> profiles, IReadOnlyList<string> classes, string? checkInTo)
    {
        using var mapping = new CsvMappingForm(list.Source, list.Headers, list.Rows, DriverImport.GuessAll(list.Headers), Array.Empty<Season>(),
            list.FromFile ? "in the file" : "in the table", entries: true);

        if (mapping.ShowDialog(owner) != DialogResult.OK)
        {
            return null;
        }

        var entryRows = EntryImport.ReadRows(list.Rows, list.LineNumbers, mapping.Columns, classes);

        using var preview = new EntryImportForm(list.Source, entryRows, profiles.ToList(), classes, checkInTo);

        return preview.ShowDialog(owner) == DialogResult.OK ? (preview.Plan, preview.CheckIn) : null;
    }
}
