namespace HeatCaller;

/// <summary>
/// Picks an export format and scope.
///
/// Each format gets a plain description of its shape rather than just a
/// name, because "CSV" alone doesn't tell you whether you're getting one row
/// per driver or one row per lap - and that's the only thing that matters
/// when deciding which to pick.
/// </summary>
public sealed class ResultsExportForm : Form
{
    private readonly ComboBox _cmbFormat = new();
    private readonly RadioButton _rbSelected = new();
    private readonly RadioButton _rbAll = new();
    private readonly Label _lblShape = new();

    public ResultsExporter.Format Format =>
        (_cmbFormat.SelectedItem as FormatItem)?.Format ?? ResultsExporter.Format.ResultsCsv;

    public bool SelectedOnly => _rbSelected.Checked;

    public ResultsExportForm(int selectedCount, int totalCount)
    {
        Text = "Export Results";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 320);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var lblFormat = new Label { Text = "Format:", Location = new Point(16, 20), AutoSize = true };
        _cmbFormat.Location = new Point(90, 17);
        _cmbFormat.Width = 374;
        _cmbFormat.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbFormat.Items.AddRange(new object[]
        {
            new FormatItem(ResultsExporter.Format.ResultsCsv),
            new FormatItem(ResultsExporter.Format.LapCsv),
            new FormatItem(ResultsExporter.Format.Json),
        });
        _cmbFormat.SelectedIndex = 0;
        _cmbFormat.SelectedIndexChanged += (_, _) => UpdateShape();

        _lblShape.Location = new Point(90, 50);
        _lblShape.Size = new Size(374, 76);
        _lblShape.ForeColor = AppTheme.TextMuted;
        _lblShape.Font = AppTheme.SmallFont;

        var lblScope = new Label { Text = "Sessions", Location = new Point(16, 140), AutoSize = true, Font = AppTheme.BoldFont };

        _rbSelected.Text = selectedCount > 0
            ? $"Selected only ({selectedCount})"
            : "Selected only (nothing selected)";
        _rbSelected.Location = new Point(28, 166);
        _rbSelected.AutoSize = true;
        _rbSelected.Enabled = selectedCount > 0;
        _rbSelected.Checked = selectedCount > 0;

        _rbAll.Text = $"All recorded sessions ({totalCount})";
        _rbAll.Location = new Point(28, 192);
        _rbAll.AutoSize = true;
        _rbAll.Checked = selectedCount == 0;

        var lblNote = new Label
        {
            Text = "There's no single file layout that RC timing software agrees on, so these are formats whose shape is "
                 + "fully documented rather than a guess at one vendor's. If you need a specific layout, a sample file "
                 + "is enough to match it.",
            Location = new Point(16, 226),
            AutoSize = true,
            MaximumSize = new Size(448, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        var btnExport = new Button { Text = "Export...", DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        AppTheme.LayoutButtonRow(268, 276, 8, btnExport, btnCancel);

        Controls.AddRange(new Control[]
        {
            lblFormat, _cmbFormat, _lblShape, lblScope, _rbSelected, _rbAll, lblNote, btnExport, btnCancel,
        });

        AcceptButton = btnExport;
        CancelButton = btnCancel;

        UpdateShape();
        AppTheme.ApplyRecursively(this);
    }

    /// <summary>Shows the actual column layout, so there's no guessing about what lands in the file.</summary>
    private void UpdateShape() => _lblShape.Text = Format switch
    {
        ResultsExporter.Format.ResultsCsv =>
            "Columns: SessionDate, SessionTime, SessionLabel, Round, Class, Track, Position, Driver, "
            + "Transponder, Car, Laps, TotalTimeSeconds, BestLapSeconds",

        ResultsExporter.Format.LapCsv =>
            "Columns: SessionDate, SessionTime, SessionLabel, Round, Class, Driver, Transponder, "
            + "LapNumber, LapSeconds, CumulativeSeconds",

        _ => "Nested JSON: one object per session, each containing its results, and each result containing "
             + "its full list of lap times in seconds.",
    };

    private sealed record FormatItem(ResultsExporter.Format Format)
    {
        public override string ToString() => ResultsExporter.Describe(Format);
    }
}
