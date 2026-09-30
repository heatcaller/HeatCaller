using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Loans a unit to a driver.
///
/// The holder is a combo rather than a plain text box so an existing driver
/// can be picked, but it stays editable - walk-ins without a profile are
/// exactly the people who need a rental, and forcing a profile first would
/// put paperwork in front of the thing they came to do.
///
/// On the layout pieces (DialogFit): the transponder across the top, then
/// who is taking it and the deposit lined up in a field grid, each with its
/// hint under it, and Check Out and Cancel at the foot; as tall as that, and
/// wider than it was made when a driver's name needs the drop-down wider. It
/// used to be placed in pixels in a 420x230 window: the captions in a column
/// 94px wide, the fields 290px beside them however long the longest name on
/// file, and each hint wrapped at a fixed 290px.
/// </summary>
public sealed class RentalCheckOutForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size.</summary>
    private const int DialogWidth = 420;

    /// <summary>The fields' width at the normal text size - wider when a driver's name needs the drop-down wider.</summary>
    private const int FieldWidth = 290;

    private readonly ComboBox _cmbHolder = new();
    private readonly TextBox _txtDeposit = new();

    public string HolderName => _cmbHolder.Text.Trim();
    public string Deposit => _txtDeposit.Text.Trim();

    public RentalCheckOutForm(RentalTransponder unit, ProfileStore profiles)
    {
        Text = $"Check Out - {unit.DisplayName}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var lblUnit = new Label
        {
            Text = $"Transponder {unit.Transponder}",
            AutoSize = true,
            Font = AppTheme.BoldFont,
        };

        _cmbHolder.DropDownStyle = ComboBoxStyle.DropDown;
        foreach (var name in profiles.All.Select(p => p.DriverName)
                     .Where(n => !string.IsNullOrWhiteSpace(n))
                     .Distinct()
                     .OrderBy(n => n))
        {
            _cmbHolder.Items.Add(name);
        }

        _txtDeposit.PlaceholderText = "car key, licence, £20...";

        var btnOk = new Button { Text = "Check Out" };
        btnOk.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_cmbHolder.Text))
            {
                AppDialog.Show(this, "Enter who's taking it.", "Missing name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // Both fields run to the right-hand edge, where the buttons end, as
        // the 290px fields did beside their captions; each hint under its
        // field, wrapping to the field's width.
        var fields = new FieldGrid();
        var captions = new List<Label>
        {
            fields.AddRow("Loan to:", _cmbHolder, stretch: true),
        };

        fields.AddHint("Pick a saved driver or just type a name - a walk-in doesn't need a profile first.");

        captions.Add(fields.AddRow("Deposit held:", _txtDeposit, stretch: true));
        fields.AddHint("Whatever you're holding until it comes back. Recorded so whoever is on the counter later knows what to return.");

        var page = new FillPage();
        page.Add(lblUnit, gapBelow: UiLayout.RowGap);
        page.Add(fields);

        // Against the right-hand edge, Cancel furthest right, as they were -
        // and Tab still reaches Check Out first: the bar takes them right to
        // left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnOk), gapBelow: 0);
        btnOk.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        // The window's own button in the app's colour, as Save and OK are in
        // every other window - the theme goes by the caption, and "Check Out"
        // is not one it knows (layout pictures, 30 Sep).
        AppTheme.StyleButton(btnOk, AppTheme.ButtonKind.Primary);

        // As wide as it was at the normal text size, at the size in use - or
        // wider when the drop-down needs more for the longest name on file:
        // at a fixed 290px, a name longer than that came out cut short once
        // it was picked. Measured in the face the theme pass left it in; the
        // drop-down itself takes the whole of the fields' column (stretch).
        // The caption column beside it as the grid works it out: its widest
        // caption and the gap after it.
        (Font Font, int Count, int Width)? holderWidth = null;

        var fieldColumn = Math.Max(UiLayout.AtTextSize(FieldWidth), Pieces.PickerWidth(_cmbHolder, ref holderWidth));
        var captionColumn = captions.Max(caption => caption.GetPreferredSize(Size.Empty).Width + caption.Margin.Horizontal);

        var width = Math.Max(
            UiLayout.AtTextSize(DialogWidth),
            UiLayout.PageLeft + captionColumn + fieldColumn + UiLayout.PageRight);

        DialogFit.Keep(this, page, width, widthMeasured: true);
    }
}

/// <summary>
/// Adds or edits a rental unit.
///
/// On the layout pieces (DialogFit): the transponder, the asset tag and the
/// notes lined up in a field grid, the hints under their fields and the
/// Retired tick under the notes, and Save and Cancel at the foot; as tall as
/// that, and wider than it was made when the Retired tick's caption needs
/// more than the fields had. It used to be placed in pixels in a 420x300
/// window: the captions in a column 114px wide, the fields 270px beside
/// them, the Retired tick at a fixed place with nothing to say its caption
/// fitted - at a larger text size it ran past the window's edge - and the
/// notes a fixed 70px tall at every text size.
/// </summary>
public sealed class RentalUnitEditForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size.</summary>
    private const int DialogWidth = 420;

    /// <summary>The fields' width at the normal text size - wider when the Retired tick's caption needs it.</summary>
    private const int FieldWidth = 270;

    /// <summary>How many lines of notes show before the box scrolls.</summary>
    private const int NotesLines = 3;

    private readonly TextBox _txtTransponder = new();
    private readonly TextBox _txtAssetTag = new();
    private readonly TextBox _txtNotes = new();
    private readonly CheckBox _chkRetired = new();

    /// <summary>
    /// What Save hands back - a COPY of the stored unit when editing. The
    /// dialog used to write straight onto the stored unit, so the undo
    /// snapshot the page takes afterwards already held the edit and Undo
    /// had nothing to put back. A copy leaves the store untouched until the
    /// page saves it, and makes Cancel a true cancel as well.
    /// </summary>
    public RentalTransponder Result { get; private set; }

    public RentalUnitEditForm(RentalTransponder? existing)
    {
        Result = existing is null
            ? new RentalTransponder { Transponder = string.Empty }
            : JsonSerializer.Deserialize<RentalTransponder>(JsonSerializer.Serialize(existing)) ?? existing;

        Text = existing is null ? "Add Rental Unit" : $"Edit {Result.DisplayName}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        _txtTransponder.Text = Result.Transponder;

        // The ID is the identity - changing it would orphan the unit's loan
        // history and any lap data recorded under it.
        _txtTransponder.ReadOnly = existing is not null;
        if (existing is not null)
        {
            _txtTransponder.BackColor = AppTheme.Surface;
        }

        _txtAssetTag.Text = Result.AssetTag;
        _txtAssetTag.PlaceholderText = "R4, sticker number...";

        _txtNotes.Multiline = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;
        _txtNotes.Text = Result.Notes;

        _chkRetired.Text = "Retired (out of service - keeps its history)";
        _chkRetired.AutoSize = true;
        _chkRetired.Checked = Result.Retired;

        var btnOk = new Button { Text = "Save" };
        btnOk.Click += BtnOk_Click;

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // Every field runs to the right-hand edge, where the buttons end, as
        // the 270px fields did beside their captions; the tick in the fields'
        // column under the notes, where it was.
        var fields = new FieldGrid();
        var captions = new List<Label>
        {
            fields.AddRow("Transponder ID:", _txtTransponder, stretch: true),
        };

        fields.AddHint(existing is null
            ? "Exactly as the decoder reports it."
            : "Can't be changed - it ties this unit to its loan history.");

        captions.Add(fields.AddRow("Asset tag:", _txtAssetTag, stretch: true));
        fields.AddHint("What people call it at the counter.");

        captions.Add(fields.AddRow("Notes:", _txtNotes, stretch: true));
        fields.AddField(_chkRetired);

        var page = new FillPage();
        page.Add(fields);

        // Against the right-hand edge, Cancel furthest right, as they were -
        // and Tab still reaches Save first: the bar takes them right to left,
        // in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnOk), gapBelow: 0);
        btnOk.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        // After the theme pass, in the face it leaves the box in: a few
        // lines of it, as the box draws them (Pieces.LineHeight).
        _txtNotes.Height = NotesLines * Pieces.LineHeight(_txtNotes.Font) + UiLayout.HintGap * 2;

        // As wide as it was at the normal text size, at the size in use - or
        // wider when the fields' column needs more: never narrower than the
        // 270px the fields had, and wide enough for the Retired tick's
        // caption on one line, which is wider than that at every text size.
        // The caption column beside it as the grid works it out: its widest
        // caption and the gap after it.
        var fieldColumn = Math.Max(
            UiLayout.AtTextSize(FieldWidth),
            _chkRetired.GetPreferredSize(Size.Empty).Width + _chkRetired.Margin.Horizontal);

        var captionColumn = captions.Max(caption => caption.GetPreferredSize(Size.Empty).Width + caption.Margin.Horizontal);

        var width = Math.Max(
            UiLayout.AtTextSize(DialogWidth),
            UiLayout.PageLeft + captionColumn + fieldColumn + UiLayout.PageRight);

        DialogFit.Keep(this, page, width, widthMeasured: true);
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        var id = _txtTransponder.Text.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            AppDialog.Show(this, "A transponder ID is required.", "Missing ID", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Result.Transponder = id;
        Result.AssetTag = _txtAssetTag.Text.Trim();
        Result.Notes = _txtNotes.Text;
        Result.Retired = _chkRetired.Checked;

        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>
/// The question the Rentals page asks as a unit is checked in - how it came
/// back (RentalTransponderControl.Prompt): the question, a line of text for
/// the answer, and OK and Cancel. A class of its own rather than built
/// inside that method, so the layout test can build it (WindowCatalog)
/// without a click. It wears the body face, as the page it opens from does.
///
/// On the layout pieces (DialogFit): the question wrapping across the top,
/// the box under it and the buttons at the foot, the window as tall as they
/// are. The question is the window's body - it names the driver and the
/// deposit, both typed in by hand - so on a screen too short for all of it,
/// the question scrolls and the box and the buttons stay in view. It used
/// to be built in pixels in a 440x220 window: the question wrapped at a
/// fixed 416px and the box at a fixed 140px down, so a question longer than
/// about seven lines - fewer at a larger text size - ran under the box.
/// </summary>
internal sealed class RentalPromptForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size.</summary>
    private const int DialogWidth = 440;

    private readonly TextBox _txt = new();

    /// <summary>What was typed in the box, trimmed. Only meaningful after DialogResult.OK.</summary>
    public string Answer => _txt.Text.Trim();

    public RentalPromptForm(string title, string prompt, string defaultValue)
    {
        Text = title;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var lbl = new Label { Text = prompt, AutoSize = true };

        _txt.Text = defaultValue;

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        var page = new FillPage();

        // The question, a hint's gap above the box it asks about.
        var body = DialogFit.Body(page, gapBelow: UiLayout.HintGap);
        body.Add(lbl, gapBelow: 0);

        page.Add(_txt, gapBelow: UiLayout.SectionGap);

        // A pixel either side of the box for the ring the theme draws round
        // it, outside it, on what holds it (Pieces.RingRoom): stretched across
        // the page, the box's sides would be the page's edges, and the ring's
        // sides would be drawn past them, where nothing shows. The gap under
        // the box stays as it was.
        _txt.Margin = new Padding(1, 0, 1, UiLayout.SectionGap);

        // Its height is the box's own, and changes when the theme takes its
        // border off - see Pieces.KeepHeightTold.
        Pieces.KeepHeightTold(_txt);

        // Against the right-hand edge, Cancel furthest right, as they were -
        // and Tab still reaches OK first: the bar takes them right to left,
        // in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(cancel, ok), gapBelow: 0);
        ok.TabIndex = 0;
        cancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = ok;
        CancelButton = cancel;

        AppTheme.ApplyRecursively(this);

        DialogFit.Keep(this, page, width: DialogWidth, body: body);
    }
}
