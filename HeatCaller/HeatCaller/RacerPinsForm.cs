namespace HeatCaller;

/// <summary>
/// Every racer's personal check-in PIN, for the desk: who has which, where
/// it would be emailed, and whether it has been.
///
/// Read-only, and the PINs are masked until "Show PINs" is ticked - this
/// is the kind of window that gets opened on a laptop with a queue of
/// racers stood behind it.
///
/// On the layout pieces (DialogFit): tiles across the top - how many racers
/// have a PIN, how many have no email address and how many have been
/// emailed theirs (18 Nov 2026, dialog jazz-up) - then the racers taking
/// the height, saying what fills the list while it is empty, and under them
/// Show PINs and a line on what Copy All copies on the left, with Copy All
/// (the window's own button, in the app's colour) and Close against the
/// right-hand edge. The window is taller by the tiles, so the list is as
/// tall as it was. It used to be placed in pixels in a 760x540
/// window: the list a fixed distance above a row of buttons at a fixed
/// height from the bottom, the count beside the tick box at an x worked out
/// from the tick box's width, all of it anchored to follow a resize.
/// </summary>
public sealed class RacerPinsForm : Form
{
    private const string Mask = "\u2022\u2022\u2022\u2022\u2022\u2022";

    /// <summary>The list shows its headings and at least this many racers before the window scrolls instead.</summary>
    private const int LeastRows = 4;

    /// <summary>How tall the window is inside its frame at the normal text size: the 540 it had, and room for the tiles.</summary>
    private const int DialogHeight = 540 + 70;

    private readonly DataGridView _grid = new();
    private readonly CheckBox _chkShow = new ThemedCheckBox();

    /// <summary>The rows as shown, PINs and all - the grid holds the mask until Show is ticked.</summary>
    private readonly List<(string Name, string ClassName, string Pin, string Email, bool Emailed)> _rows;

    public RacerPinsForm(AppSettings settings, ProfileStore profiles)
    {
        Text = "Racer Check-in PINs";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        _rows = profiles.All
            .Where(p => settings.RacerPins.ContainsKey(p.Transponder))
            .Select(p => (
                Name: string.IsNullOrWhiteSpace(p.DriverName) ? p.Transponder : p.DriverName.Trim(),
                ClassName: p.ClassName,
                Pin: settings.RacerPins[p.Transponder],
                Email: RacerPins.EmailFor(p) ?? string.Empty,
                Emailed: RacerPins.IsEmailed(settings, p.Transponder)))
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "name", HeaderText = "Name", FillWeight = 90 },
            new DataGridViewTextBoxColumn { Name = "class", HeaderText = "Class", FillWeight = 60 },
            new DataGridViewTextBoxColumn { Name = "pin", HeaderText = "PIN", FillWeight = 40 },
            new DataGridViewTextBoxColumn { Name = "email", HeaderText = "Email", FillWeight = 110 },
            new DataGridViewTextBoxColumn { Name = "emailed", HeaderText = "Emailed", FillWeight = 40 });

        foreach (var row in _rows)
        {
            // The PIN rides on the row itself: a click on a column header
            // re-sorts the grid, so a row's index is not its place in _rows.
            var index = _grid.Rows.Add(row.Name, row.ClassName, Mask, row.Email.Length == 0 ? "(none)" : row.Email, row.Emailed ? "Yes" : string.Empty);
            _grid.Rows[index].Tag = row.Pin;
        }

        _chkShow.Text = "Show PINs";
        _chkShow.AutoSize = true;
        _chkShow.Font = AppTheme.BodyFont;
        _chkShow.CheckedChanged += (_, _) => ShowPins(_chkShow.Checked);

        // The list at a glance, across the top (18 Nov 2026, dialog jazz-up).
        // The line beside Show PINs used to give the first two; it now says
        // what Copy All copies, and the tiles give the counts.
        var tiles = new[] { new StatTile(), new StatTile(), new StatTile() };

        foreach (var tile in tiles)
        {
            tile.Value.Font = AppTheme.TitleFont;
            tile.Caption.Font = AppTheme.MicroFont;
        }

        var noEmail = _rows.Count(r => r.Email.Length == 0);
        var culture = System.Globalization.CultureInfo.CurrentCulture;

        tiles[0].Set(_rows.Count.ToString(culture), _rows.Count == 1 ? "Racer with a PIN" : "Racers with a PIN");
        tiles[1].Set(noEmail.ToString(culture), "No email address");
        tiles[2].Set(_rows.Count(r => r.Emailed).ToString(culture), "Emailed their PIN");

        if (noEmail > 0)
        {
            tiles[1].Value.ForeColor = AppTheme.Warning;
        }

        // What fills the list, said while nobody has a PIN.
        EmptyNote.For(_grid, () => _rows.Count == 0
            ? "Nobody has a PIN yet. Each racer given a personal check-in PIN is listed here."
            : null);

        var lblCount = new Label
        {
            Text = _rows.Count == 0
                ? "Nobody has a PIN yet."
                : "Copy All copies the real PINs, shown or not.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        var btnCopy = new Button { Text = "Copy All" };
        btnCopy.Click += (_, _) => CopyAll(btnCopy);
        btnCopy.Enabled = _rows.Count > 0;

        var btnClose = new Button { Text = "Close", DialogResult = DialogResult.OK };

        // The tick box and the count level with the middle of the buttons,
        // as they were: in a row of their own, anchored to neither its top
        // nor its bottom, each is centred up and down on it.
        _chkShow.Anchor = AnchorStyles.Left;
        lblCount.Anchor = AnchorStyles.Left;

        // Show PINs and the count on the left, Copy All and Close against the
        // right-hand edge with Close furthest right, as they were - on one
        // line, or the buttons under the count when there is not room.
        // Tab still reaches Copy All before Close, as it did: the right-hand
        // bar takes its buttons right to left, in the order they were added.
        var footer = new SplitBar(
            new ButtonBar().With(_chkShow, lblCount),
            new ButtonBar(alignRight: true).With(btnClose, btnCopy));

        btnCopy.TabIndex = 0;
        btnClose.TabIndex = 1;

        var page = new FillPage();
        page.Add(new TileStrip(tiles), gapBelow: UiLayout.RowGap);
        page.AddFill(_grid, _ => _grid.ColumnHeadersHeight + LeastRows * _grid.RowTemplate.Height + 2, gapBelow: UiLayout.RowGap);
        page.Add(footer, gapBelow: 0);

        Controls.Add(page);

        AcceptButton = btnClose;
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);

        // Every heading whole, the rest shared by weight - see Pieces.KeepHeadingsWhole.
        Pieces.KeepHeadingsWhole(_grid);

        // Copy All in the app's colour: copying the list is what the window
        // is opened for. After the theme pass, which does not know it.
        AppTheme.StyleButton(btnCopy, AppTheme.ButtonKind.Primary);

        // The width it had at the normal text size, and taller by the tiles
        // (DialogHeight). It can be made bigger.
        DialogFit.Keep(this, page, width: 760, height: DialogHeight);
    }

    private void ShowPins(bool show)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            row.Cells["pin"].Value = show ? row.Tag as string ?? Mask : Mask;
        }
    }

    /// <summary>
    /// Tab-separated, so it pastes straight into a spreadsheet's columns.
    /// The real PINs, masked or not on screen: copying them is the point.
    /// </summary>
    private void CopyAll(Button button)
    {
        var lines = CopyAllLines(_rows);

        try
        {
            // Kept OUT of Windows' clipboard history and Cloud Clipboard.
            //
            // These are the racers' personal PINs. Clipboard.SetText alone
            // hands them to whatever else is watching the clipboard, and on
            // a PC with clipboard sync on they are uploaded and pushed to
            // every other device signed in to that Microsoft account - a
            // club's PIN list arriving on somebody's phone, long after the
            // paste it was copied for. These three formats are how Windows
            // is told not to: it ignores ones it does not know, so this is
            // safe on any version.
            var data = new DataObject();
            data.SetText(string.Join(Environment.NewLine, lines));
            data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[] { 0 }));
            data.SetData("CanIncludeInClipboardHistory", new MemoryStream(new byte[] { 0, 0, 0, 0 }));
            data.SetData("CanUploadToCloudClipboard", new MemoryStream(new byte[] { 0, 0, 0, 0 }));

            // copy: true, so the list is still there to paste after the app
            // is closed - which is what SetText did, and what somebody
            // pasting into an email expects.
            Clipboard.SetDataObject(data, copy: true);

            button.Text = "Copied";
        }
        catch (Exception ex)
        {
            // Another program holding the clipboard is enough to make this throw.
            AppDialog.Show(this, $"Couldn't copy: {ex.Message}", "Not copied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// The lines Copy All puts on the clipboard, header first.
    ///
    /// SPREADSHEET-SAFE (17 Oct 2026, Bug Hunt 8, A35-1), the way the CSV
    /// exports already are. Names come from online sign-up - public text -
    /// and went in raw, so "=HYPERLINK(...)" pasted as a live formula; they
    /// now go through <see cref="CsvHelper.NeutraliseFormula"/>. And a PIN
    /// is text: about one in ten starts with 0, and pasted bare "042917"
    /// became the number 42917, a PIN the check-in page refuses. The same
    /// leading apostrophe NeutraliseFormula uses keeps it as typed.
    /// </summary>
    internal static List<string> CopyAllLines(
        IEnumerable<(string Name, string ClassName, string Pin, string Email, bool Emailed)> rows)
    {
        var lines = new List<string> { "Name\tClass\tPIN\tEmail\tEmailed" };

        lines.AddRange(rows.Select(r =>
            string.Join(
                "\t",
                CsvHelper.NeutraliseFormula(Clean(r.Name)),
                CsvHelper.NeutraliseFormula(Clean(r.ClassName)),
                r.Pin.Length == 0 ? string.Empty : "'" + Clean(r.Pin),
                CsvHelper.NeutraliseFormula(Clean(r.Email)),
                r.Emailed ? "Yes" : "No")));

        return lines;
    }

    /// <summary>A tab or line break inside a name would split it across cells.</summary>
    private static string Clean(string text) =>
        text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
