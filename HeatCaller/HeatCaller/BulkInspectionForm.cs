namespace HeatCaller;

/// <summary>
/// One inspection for several cars at once, from Who's Racing (1 Nov 2026):
/// the drivers picked there with Ctrl or Shift, then a double-click on one of
/// them or Inspect Selected Cars.
///
/// A scrutineer who has just waved a line of cars through, or turned a batch
/// back for the same reason, gives the result, their name and a note once,
/// and one inspection is filed for each car - each in the class its Tech cell
/// is showing (see BulkTech.Build). There is no checklist here: the cars can
/// be in different classes with different checklists, and a pass marks every
/// check on each car's own checklist OK.
///
/// On the layout pieces (DialogFit), like the Add a driver window: what it is
/// for and who it covers at the top, the result, inspector and notes lined up
/// in a field grid, and Save and Cancel at the foot.
/// </summary>
internal sealed class BulkInspectionForm : Form
{
    /// <summary>How wide it is inside its frame at the normal text size.</summary>
    private const int DialogWidth = 440 + UiLayout.PageLeft + UiLayout.PageRight;

    /// <summary>Lines of notes the box shows before it scrolls.</summary>
    private const int NotesLines = 3;

    private const string VerdictPassed = "Passed";
    private const string VerdictPassedWithNote = "Passed with a note";
    private const string VerdictFailed = "FAILED";

    private readonly ComboBox _cmbResult = new();
    private readonly TextBox _txtInspector = new();
    private readonly TextBox _txtNotes = new();

    /// <summary>The result picked for every car.</summary>
    public TechResult Result => _cmbResult.SelectedItem switch
    {
        VerdictFailed => TechResult.Failed,
        VerdictPassedWithNote => TechResult.PassedWithNote,
        _ => TechResult.Passed,
    };

    public string Inspector => _txtInspector.Text.Trim();

    public string Notes => _txtNotes.Text;

    /// <param name="driverNames">The drivers it is for, as the list shows them.</param>
    /// <param name="alreadyToday">How many of them already have an inspection today, which this updates rather than adding to.</param>
    public BulkInspectionForm(IReadOnlyList<string> driverNames, int alreadyToday = 0)
    {
        var count = driverNames.Count;

        Text = $"Log Inspection for {Plural.Of(count, "Driver")}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var lblWho = new Label
        {
            Text = BulkTech.NamesLine(driverNames),
            AutoSize = true,
            Font = AppTheme.BoldFont,
        };

        var intro = "One inspection is filed for each car, in the class its Tech cell shows. "
                    + "Passed marks every check on that class's checklist OK.";

        if (alreadyToday > 0)
        {
            intro += alreadyToday == count
                ? " They were all inspected today already, so those inspections are updated."
                : $" {Plural.Of(alreadyToday, "car was", "cars were")} inspected today already, so "
                  + $"{(alreadyToday == 1 ? "that inspection is" : "those inspections are")} updated.";
        }

        intro += " Leave Inspector or Notes blank to keep what an inspection already says.";

        var lblIntro = new Label
        {
            Text = intro,
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        };

        _cmbResult.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbResult.Items.AddRange(new object[] { VerdictPassed, VerdictPassedWithNote, VerdictFailed });
        _cmbResult.SelectedItem = VerdictPassed;

        _txtNotes.Multiline = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;

        var fields = new FieldGrid();
        fields.AddRow("Result:", _cmbResult);
        fields.AddRow("Inspector:", _txtInspector, stretch: true);
        fields.AddRow("Notes:", _txtNotes, stretch: true);

        var ok = new Button { Text = $"Save {Plural.Of(count, "Inspection")}" };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        ok.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        var page = new FillPage();
        page.Add(lblWho, gapBelow: UiLayout.HintGap);
        page.Add(lblIntro, gapBelow: UiLayout.SectionGap - UiLayout.HintGap);
        page.Add(fields, gapBelow: UiLayout.RowGap);

        // Against the right-hand edge, Cancel furthest right, as in the
        // other inspection window - and Tab reaches Save first.
        page.Add(new ButtonBar(alignRight: true).With(cancel, ok), gapBelow: 0);
        ok.TabIndex = 0;
        cancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = ok;
        CancelButton = cancel;

        AppTheme.ApplyRecursively(this);
        AppTheme.StyleButton(ok, AppTheme.ButtonKind.Primary);
        AppTheme.StyleButton(cancel, AppTheme.ButtonKind.Secondary);

        // A few lines of notes, in the face the theme pass leaves the box in.
        _txtNotes.Height = NotesLines * Pieces.LineHeight(_txtNotes.Font) + UiLayout.HintGap * 2;

        // The result as wide as a field at the text size in use, or as its
        // longest choice.
        (Font Font, int Count, int Width)? resultMeasured = null;
        _cmbResult.Width = Math.Max(UiLayout.AtTextSize(UiLayout.FieldWidth), Pieces.PickerWidth(_cmbResult, ref resultMeasured));

        DialogFit.Keep(this, page, width: DialogWidth);
    }
}
