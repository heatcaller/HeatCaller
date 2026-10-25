namespace HeatCaller;

/// <summary>
/// Print track and parts list (Track Designer overhaul, Phase 7, 25 Oct
/// 2026): the scale to print the plan at, how many sheets each takes on
/// the default printer's paper, and whether the parts list and build list
/// follow it. Print hands over to the printer's own dialog and a preview
/// (RcpPlanPrinter), where the paper can still change - the sheets are
/// worked out again from whatever is chosen there.
///
/// On the layout pieces (DialogFit): a line of what it is for, the scales
/// in a card, the parts tick in another, and the buttons at the foot.
/// </summary>
public sealed class RcpPrintForm : Form
{
    private const int DialogWidth = 520;

    /// <summary>The sheets that make the dialog pick a smaller scale to start with - a plan over more than this is a lot of taping.</summary>
    private const int ComfortableSheets = 6;

    private readonly List<(RadioButton Button, int Ratio)> _scales = new();
    private readonly CheckBox _chkParts = new ThemedCheckBox { Text = "Then the parts list and how to build it, row by row", Checked = true, AutoSize = true };

    /// <summary>The N of 1:N chosen, or 0 to fit on one page. Only meaningful after DialogResult.OK.</summary>
    public int Ratio => _scales.FirstOrDefault(s => s.Button.Checked).Ratio;

    /// <summary>Whether the parts list and build list follow the plan. Only meaningful after DialogResult.OK.</summary>
    public bool Parts => _chkParts.Checked;

    public RcpPrintForm(RcpTrackDesign design)
    {
        Text = "Print track and parts list";
        Font = AppTheme.BodyFont;

        var area = RcpPrintLayout.Area(design);
        var landscape = RcpPlanPrinter.Landscape(design);
        var (paperMm, paperName) = RcpPlanPrinter.DefaultPaper();
        var box = RcpPlanPrinter.DrawingBoxMm(paperMm, landscape, RcpPlanPrinter.MarginMm);
        var tileMm = design.TileSizeMm;

        var lblIntro = new Label
        {
            Text = "The plan prints to scale, so it can be measured with a ruler - over as many sheets as it takes, "
                   + "each with the grid's letters and numbers to put them together by.",
            AutoSize = true,
            Font = AppTheme.BodyFont,
        };

        var scaleCard = new Section("Scale");
        RadioButton? pick = null;
        RadioButton? smallestThatFits = null;

        foreach (var ratio in RcpPrintLayout.Ratios)
        {
            var mmPerTile = RcpPrintLayout.MmPerTile(tileMm, ratio);
            var (across, down) = RcpPrintLayout.Count(area, mmPerTile, box);
            var sheets = across * down;
            var tooMany = sheets > RcpPrintLayout.MostSheets;

            var button = new ThemedRadioButton
            {
                AutoSize = true,
                Enabled = !tooMany,
                Text = $"1:{ratio} - a tile is {mmPerTile:0.#} mm - "
                       + (tooMany ? $"more than {RcpPrintLayout.MostSheets} sheets" : sheets == 1 ? "one sheet" : $"{sheets} sheets, {across} across by {down} down"),
            };

            _scales.Add((button, ratio));
            scaleCard.Add(button, gapBelow: UiLayout.HintGap);

            if (!tooMany)
            {
                smallestThatFits = button;
            }

            // The biggest scale that stays a handful of sheets.
            if (pick is null && !tooMany && sheets <= ComfortableSheets)
            {
                pick = button;
            }
        }

        var fitMm = RcpPrintLayout.FitMmPerTile(area, box);
        var fit = new ThemedRadioButton
        {
            AutoSize = true,
            Text = fitMm > 0
                ? $"Fit on one sheet - about {RcpPrintLayout.RatioText(tileMm, fitMm)}, a tile is {fitMm:0.#} mm"
                : "Fit on one sheet",
        };

        _scales.Add((fit, 0));
        scaleCard.Add(fit, gapBelow: UiLayout.HintGap);

        (pick ?? smallestThatFits ?? fit).Checked = true;

        scaleCard.Add(Note($"Sheets counted for {paperName} paper, {(landscape ? "landscape" : "portrait")}. "
                           + "Change the paper in the next step and they're worked out again."), gapBelow: 0);

        var partsCard = new Section("Parts");
        partsCard.Add(_chkParts, gapBelow: UiLayout.HintGap);
        partsCard.Add(Note("What the plan takes against what the club owns, then the order to build it in: "
                           + "the floor, the rails, the dividers, the turns, the markings."), gapBelow: 0);

        var btnPrint = new Button { Text = "Print...", DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        var page = new FillPage();
        var body = DialogFit.Body(page);
        body.Add(lblIntro, gapBelow: UiLayout.RowGap);
        body.Add(scaleCard, gapBelow: UiLayout.RowGap);
        body.Add(partsCard, gapBelow: 0);

        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnPrint), gapBelow: 0);
        btnPrint.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnPrint;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        DialogFit.Keep(this, page, width: DialogWidth, body: body);
    }

    private static Label Note(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = AppTheme.SmallFont,
        ForeColor = AppTheme.TextMuted,
    };
}
