namespace HeatCaller;

/// <summary>
/// Every racer's personal check-in PIN, for the desk: who has which, where
/// it would be emailed, and whether it has been.
///
/// Read-only, and the PINs are masked until "Show PINs" is ticked - this
/// is the kind of window that gets opened on a laptop with a queue of
/// racers stood behind it.
/// </summary>
public sealed class RacerPinsForm : Form
{
    private const string Mask = "\u2022\u2022\u2022\u2022\u2022\u2022";

    private readonly DataGridView _grid = new();
    private readonly CheckBox _chkShow = new();

    /// <summary>The rows as shown, PINs and all - the grid holds the mask until Show is ticked.</summary>
    private readonly List<(string Name, string ClassName, string Pin, string Email, bool Emailed)> _rows;

    public RacerPinsForm(AppSettings settings, ProfileStore profiles)
    {
        Text = "Racer Check-in PINs";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 540);
        MinimumSize = new Size(600, 400);
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

        _grid.Location = new Point(12, 12);
        _grid.Size = new Size(ClientSize.Width - 24, ClientSize.Height - 12 - UiLayout.ButtonHeight - 24);
        _grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
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

        var buttonsTop = ClientSize.Height - 12 - UiLayout.ButtonHeight;

        _chkShow.Text = "Show PINs";
        _chkShow.AutoSize = true;
        _chkShow.Font = AppTheme.BodyFont;
        _chkShow.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _chkShow.CheckedChanged += (_, _) => ShowPins(_chkShow.Checked);

        var lblCount = new Label
        {
            Text = _rows.Count == 0
                ? "Nobody has a PIN yet."
                : $"{Plural.Of(_rows.Count, "racer")} - {_rows.Count(r => r.Email.Length == 0)} without an email address.",
            AutoSize = true,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        var btnCopy = new Button { Text = "Copy All", Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        btnCopy.Click += (_, _) => CopyAll(btnCopy);
        btnCopy.Enabled = _rows.Count > 0;

        var btnClose = new Button { Text = "Close", Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.OK };

        // Right-aligned: measured first, then placed from the right edge.
        var copyWidth = UiLayout.WidthFor(btnCopy);
        var closeWidth = UiLayout.WidthFor(btnClose);
        btnClose.Size = new Size(closeWidth, UiLayout.ButtonHeight);
        btnCopy.Size = new Size(copyWidth, UiLayout.ButtonHeight);
        btnClose.Location = new Point(ClientSize.Width - 12 - closeWidth, buttonsTop);
        btnCopy.Location = new Point(btnClose.Left - UiLayout.ButtonGap - copyWidth, buttonsTop);

        _chkShow.Location = new Point(12, UiLayout.CentreOn(_chkShow, buttonsTop, UiLayout.ButtonHeight));
        lblCount.Location = new Point(_chkShow.Left + _chkShow.PreferredSize.Width + 16, UiLayout.CentreOn(lblCount, buttonsTop, UiLayout.ButtonHeight));

        Controls.Add(_grid);
        Controls.Add(_chkShow);
        Controls.Add(lblCount);
        Controls.Add(btnCopy);
        Controls.Add(btnClose);

        AcceptButton = btnClose;
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);
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
        var lines = new List<string> { "Name\tClass\tPIN\tEmail\tEmailed" };

        lines.AddRange(_rows.Select(r =>
            string.Join("\t", Clean(r.Name), Clean(r.ClassName), r.Pin, Clean(r.Email), r.Emailed ? "Yes" : "No")));

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

    /// <summary>A tab or line break inside a name would split it across cells.</summary>
    private static string Clean(string text) =>
        text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
