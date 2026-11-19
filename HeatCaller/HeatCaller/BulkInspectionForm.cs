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
/// On the layout pieces (DialogFit), in two columns as the one-car
/// inspection window is (18 Nov 2026, dialog jazz-up - it was one column of
/// a names line, a note and a field grid): on the left a card naming the
/// cars it covers, with a line on how they are filed, then the result and
/// the inspector in a card of their own; on the right tiles counting the
/// cars and those already inspected today, then the notes taking the
/// height; and Save and Cancel at the foot, Save in the app's colour.
/// </summary>
internal sealed class BulkInspectionForm : Form
{
    /// <summary>How wide it is inside its frame at the normal text size.</summary>
    private const int DialogWidth = 760;

    /// <summary>How tall it opens inside its frame at the normal text size - never less than its content needs.</summary>
    private const int DialogHeight = 320;

    /// <summary>The right-hand column - the tiles and the notes - at the normal text size.</summary>
    private const int SideWidth = 320;

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

        var intro = "One inspection is filed for each car, in every class its Tech cell is still waiting on.";

        if (alreadyToday > 0)
        {
            intro += alreadyToday == count
                ? " They were all inspected today already, so those inspections are updated."
                : $" {Plural.Of(alreadyToday, "car was", "cars were")} inspected today already, so "
                  + $"{(alreadyToday == 1 ? "that inspection is" : "those inspections are")} updated.";
        }

        _cmbResult.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbResult.Items.AddRange(new object[] { VerdictPassed, VerdictPassedWithNote, VerdictFailed });
        _cmbResult.SelectedItem = VerdictPassed;

        _txtNotes.Multiline = true;

        // Enter starts a new line here, as in every other notes box.
        // Without this it pressed Save, and filed the half-written note
        // against every car picked.
        _txtNotes.AcceptsReturn = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;

        var fields = new FieldGrid();
        fields.AddRow("Result:", _cmbResult);
        fields.AddRow("Inspector:", _txtInspector, stretch: true);

        var ok = new Button { Text = $"Save {Plural.Of(count, "Inspection")}" };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        ok.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        // THE LOOK (18 Nov 2026, dialog jazz-up), after the one-car
        // inspection window: the cars and the result on the left, the counts
        // and the notes on the right. Tab still goes Result, Inspector,
        // Notes, Save: the left-hand column comes first. The dialog is built
        // at the text size in use, so the tiles take their faces here.
        var tiles = new[] { new StatTile(), new StatTile() };

        foreach (var tile in tiles)
        {
            tile.Value.Font = AppTheme.TitleFont;
            tile.Caption.Font = AppTheme.MicroFont;
        }

        var culture = System.Globalization.CultureInfo.CurrentCulture;

        tiles[0].Set(count.ToString(culture), count == 1 ? "Car" : "Cars");
        tiles[1].Set(alreadyToday.ToString(culture), "Updated");

        var carsCard = new Section("Cars");
        carsCard.Add(lblWho, gapBelow: UiLayout.HintGap);
        carsCard.Add(CardHint(intro), gapBelow: 0);

        var resultCard = new Section("Result");
        resultCard.Add(CardHint("Passed marks every check on each car's checklist OK. "
                                + "Leave Inspector blank to keep who an inspection already says."), gapBelow: UiLayout.RowGap);
        resultCard.Add(fields, gapBelow: 0);

        var notesCard = new Section("Notes");
        notesCard.Add(CardHint("The same note goes on every car. Leave it blank to keep what's there."), gapBelow: UiLayout.RowGap);
        notesCard.AddFill(_txtNotes, () => NotesLines * Pieces.LineHeight(_txtNotes.Font) + UiLayout.HintGap * 2, gapBelow: 0);

        var columns = new Beside(() => UiLayout.AtTextSize(SideWidth));
        columns.Add(carsCard, gapBelow: UiLayout.RowGap);
        columns.Add(resultCard, gapBelow: 0);
        columns.AddSideMeasured(new TileStrip(tiles), gapBelow: UiLayout.RowGap);
        columns.AddSideFill(notesCard, notesCard.LeastHeight, gapBelow: 0);

        var page = new FillPage();
        page.AddFill(columns, columns.LeastHeight, gapBelow: UiLayout.RowGap);

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

        // The result as wide as a field at the text size in use, or as its
        // longest choice.
        (Font Font, int Count, int Width)? resultMeasured = null;
        _cmbResult.Width = Math.Max(UiLayout.AtTextSize(UiLayout.FieldWidth), Pieces.PickerWidth(_cmbResult, ref resultMeasured));

        DialogFit.Keep(this, page, width: DialogWidth, height: DialogHeight);
    }

    private static Label CardHint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = AppTheme.TextMuted,
        Font = AppTheme.SmallFont,
    };
}
