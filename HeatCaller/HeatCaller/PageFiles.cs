namespace HeatCaller;

/// <summary>
/// File names for a printout that runs to more than one page - the heat
/// sheet, the run sheet and the QR check-in sheets - and the one question
/// the save dialog can't ask about them.
///
/// The save dialog only asks before replacing the file it was given. The
/// other pages are named from it (heat-sheet-page2.png, ...), so a second
/// export to the same folder used to overwrite those without a word.
/// </summary>
internal static class PageFiles
{
    /// <summary>
    /// Where page <paramref name="index"/> (from 0) goes: the first page to
    /// the chosen file itself, every later one beside it as
    /// "&lt;name&gt;-page&lt;n&gt;&lt;ext&gt;".
    /// </summary>
    public static string PageName(string chosen, int index)
    {
        if (index == 0)
        {
            return chosen;
        }

        var directory = Path.GetDirectoryName(chosen) ?? string.Empty;
        return Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(chosen)}-page{index + 1}{Path.GetExtension(chosen)}");
    }

    /// <summary>
    /// Asks once before replacing any of <paramref name="paths"/> that
    /// already exist, other than <paramref name="chosen"/> - the save dialog
    /// asked about that one. True to go ahead: nothing is there, or Yes.
    /// </summary>
    public static bool ConfirmReplacing(IWin32Window owner, IReadOnlyList<string> paths, string chosen)
    {
        var existing = paths
            .Where(path => !string.Equals(path, chosen, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            .Select(Path.GetFileName)
            .ToList();

        if (existing.Count == 0)
        {
            return true;
        }

        return AppDialog.Show(owner,
            $"{(existing.Count == 1 ? "This page is" : "These pages are")} already in that folder:\n\n"
            + string.Join("\n", existing)
            + $"\n\nReplace {(existing.Count == 1 ? "it" : "them")}?",
            "Replace pages", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    }

    /// <summary>
    /// Saves every page as a PNG - the first to <paramref name="chosen"/>,
    /// the rest beside it (PageName) - after asking before replacing any of
    /// the later ones. Disposes the pages. Returns the files written, or
    /// null when the club chose not to replace.
    /// </summary>
    public static IReadOnlyList<string>? SavePages(IWin32Window owner, IReadOnlyList<Bitmap> pages, string chosen)
    {
        try
        {
            var paths = Enumerable.Range(0, pages.Count).Select(i => PageName(chosen, i)).ToList();

            if (!ConfirmReplacing(owner, paths, chosen))
            {
                return null;
            }

            for (var i = 0; i < pages.Count; i++)
            {
                pages[i].Save(paths[i], System.Drawing.Imaging.ImageFormat.Png);
            }

            return paths;
        }
        finally
        {
            foreach (var page in pages)
            {
                page.Dispose();
            }
        }
    }

    /// <summary>"Saved to: ..." for one page, or the folder and each file for several.</summary>
    public static string Describe(IReadOnlyList<string> written) =>
        written.Count == 1
            ? $"Saved to:\n{written[0]}"
            : $"Saved {written.Count} pages to:\n{Path.GetDirectoryName(written[0])}\n\n{string.Join("\n", written.Select(Path.GetFileName))}";
}
