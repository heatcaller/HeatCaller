namespace HeatCaller;

/// <summary>
/// Picks an export format and scope.
///
/// Each format gets a plain description of its shape rather than just a
/// name, because "CSV" alone doesn't tell you whether you're getting one row
/// per driver or one row per lap - and that's the only thing that matters
/// when deciding which to pick.
///
/// On the layout pieces (DialogFit): the format beside its caption and its
/// shape under it, as tall as the longest of the shapes (SteadyLabel), so
/// picking another format never moves what is under it; then the sessions
/// to export under their heading, the note on the formats, and Export and
/// Cancel at the foot. All but the buttons is the window's body, which
/// scrolls on a screen too short for it. It used to be placed in pixels in
/// a 480x320 window: the shape in a box of a fixed 374x76 at every text
/// size, and the note wrapped at a fixed 448px - three lines of it, the
/// last of which came down past the top of the buttons.
/// </summary>
public sealed class ResultsExportForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size.</summary>
    private const int DialogWidth = 520;

    private readonly ComboBox _cmbFormat = new();
    private readonly RadioButton _rbSelected = new ThemedRadioButton();
    private readonly RadioButton _rbAll = new ThemedRadioButton();

    /// <summary>The shape of the format picked, under it - as tall as the longest of them, so picking another never moves what is under it.</summary>
    private readonly SteadyLabel _lblShape = new()
    {
        Wordings = Enum.GetValues<ResultsExporter.Format>().Select(ShapeOf).ToArray(),
    };

    public ResultsExporter.Format Format =>
        (_cmbFormat.SelectedItem as FormatItem)?.Format ?? ResultsExporter.Format.ResultsCsv;

    public bool SelectedOnly => _rbSelected.Checked;

    public ResultsExportForm(int selectedCount, int totalCount)
    {
        Text = "Export Results";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        _cmbFormat.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbFormat.Items.AddRange(new object[]
        {
            new FormatItem(ResultsExporter.Format.ResultsCsv),
            new FormatItem(ResultsExporter.Format.LapCsv),
            new FormatItem(ResultsExporter.Format.Json),
        });
        _cmbFormat.SelectedIndex = 0;
        _cmbFormat.SelectedIndexChanged += (_, _) => UpdateShape();

        _rbSelected.Text = selectedCount > 0
            ? $"Selected only ({selectedCount})"
            : "Selected only (nothing selected)";
        _rbSelected.AutoSize = true;
        _rbSelected.Enabled = selectedCount > 0;
        _rbSelected.Checked = selectedCount > 0;

        _rbAll.Text = $"All recorded sessions ({totalCount})";
        _rbAll.AutoSize = true;
        _rbAll.Checked = selectedCount == 0;

        var lblNote = new Label
        {
            Text = "RC timing software has no common file layout, so these formats are fully documented rather than a "
                 + "guess at one vendor's. If you need a specific layout, a sample file is enough to match it.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        var btnExport = new Button { Text = "Export...", DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // The format beside its caption, the box running to the right-hand
        // edge as it did, and its shape under it in the box's column, in the
        // small muted type of a hint (FieldGrid.AddHint).
        var fields = new FieldGrid();
        var lblFormat = fields.AddRow("Format:", _cmbFormat, stretch: true);
        fields.AddHint(_lblShape);

        var page = new FillPage();

        // Everything but the buttons: on a screen too short for all of it,
        // it scrolls and Export and Cancel stay in view. The two choices are
        // in the same column, so they are still one group - picking one
        // clears the other.
        //
        // In two cards (18 Nov 2026, dialog look): the format and its shape,
        // and which sessions go in - the two choices still in one column of
        // one card, so still one group. The note on the formats under them.
        var formatCard = new Section("The file");
        formatCard.Add(fields, gapBelow: 0);

        var sessionsCard = new Section("Sessions");
        sessionsCard.Add(_rbSelected, stretch: false, gapBelow: UiLayout.HintGap);
        sessionsCard.Add(_rbAll, stretch: false, gapBelow: 0);

        var body = DialogFit.Body(page);
        body.Add(formatCard, gapBelow: UiLayout.RowGap);
        body.Add(sessionsCard, gapBelow: UiLayout.RowGap);
        body.Add(lblNote, gapBelow: 0);

        // Against the right-hand edge, Cancel furthest right, as they were -
        // and Tab still reaches Export first: the bar takes them right to
        // left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnExport), gapBelow: 0);
        btnExport.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnExport;
        CancelButton = btnCancel;

        UpdateShape();
        AppTheme.ApplyRecursively(this);

        // As wide as it was at the normal text size, at the size in use - or
        // wider if the format box ever needs more for its longest name than
        // the fields' column leaves it there, which today it does not at any
        // text size. Measured in the face the theme pass left the box in; the
        // box itself takes the whole of the fields' column (stretch), beside
        // the caption and the gap after it.
        (Font Font, int Count, int Width)? formatWidth = null;

        var captionColumn = lblFormat.GetPreferredSize(Size.Empty).Width + lblFormat.Margin.Horizontal;

        var width = Math.Max(
            UiLayout.AtTextSize(DialogWidth),
            UiLayout.PageLeft + Section.Inset.Horizontal + captionColumn + Pieces.PickerWidth(_cmbFormat, ref formatWidth) + UiLayout.PageRight);

        DialogFit.Keep(this, page, width, body: body, widthMeasured: true);
    }

    /// <summary>Shows the actual column layout, so there's no guessing about what lands in the file.</summary>
    private void UpdateShape() => _lblShape.Text = ShapeOf(Format);

    /// <summary>A format's shape, in words. Every one of them is measured for the room under the format box (see _lblShape).</summary>
    private static string ShapeOf(ResultsExporter.Format format) => format switch
    {
        ResultsExporter.Format.ResultsCsv =>
            "Columns: SessionDate, SessionTime, SessionLabel, Round, Class, Track, Position, Driver, "
            + "Transponder, Car, Laps, TotalTimeSeconds, BestLapSeconds, Status, LapsCountedByHand",

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
