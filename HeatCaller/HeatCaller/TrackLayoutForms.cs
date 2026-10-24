namespace HeatCaller;

/// <summary>
/// Small add/edit dialog for a single TrackLayout.
///
/// On the layout pieces (DialogFit): the name and the three figures lined up
/// in a field grid, the length's note beside its box and the other two notes
/// under theirs; the notes box under its heading; the photo or drawing - the
/// preview's place down the left, kept whether or not there is a picture in
/// it, and beside it the file's name over the three buttons that change it;
/// then which picture is the main one, its two choices side by side (under
/// each other on a line too short for both) and the note under them. All of
/// that is the window's body, which scrolls on a screen too short for it,
/// and Save and Cancel are under it at the foot. It opens as wide as it was
/// made at the normal text size, at the size in use - or wider, so the
/// photo's three buttons stay on one line beside the preview, and the
/// section does not grow a line as a file comes and goes. It used to be
/// placed in pixels, each row measured from the bottom of the one above into
/// a 440px column: the notes under the figures ran from the dialog's left
/// edge rather than under their boxes, the notes box was four lines of the
/// font's height rather than of the lines the box draws, the preview a fixed
/// 120x90 at every text size, Remove on a row of its own under Attach File
/// and Open, and a dialog that grew with the text size had nothing to stop
/// it running off the bottom of the screen.
/// </summary>
public sealed class TrackLayoutEditForm : Form
{
    private readonly TextBox _txtName = new();
    private readonly NumericUpDown _numTrackLength = new();
    private readonly NumericUpDown _numAvgLapSeconds = new();
    private readonly TextBox _txtNotes = new();
    private readonly NumericUpDown _numMarshalPosts = new();
    private readonly PictureBox _picPreview = new();
    private readonly Label _lblAttachmentName = new();
    private readonly Button _btnRemoveAttachment = new();
    private readonly Button _btnOpenAttachment = new();
    private readonly RadioButton _rdoPhotoMain = new ThemedRadioButton();
    private readonly RadioButton _rdoPlanMain = new ThemedRadioButton();

    public TrackLayout Result { get; private set; }

    /// <summary>True when Save attached a new photo or removed the old one - an undo of the edit then has files to put back.</summary>
    public bool AttachmentChanged { get; private set; }

    /// <summary>Where Save set the previous photo's folder aside (see TrackAttachmentStore.StashForTrack), or null when there was none.</summary>
    public string? SetAsideAttachment { get; private set; }

    // What Save will do to the photo. Nothing on disk or on the track is
    // touched before then: Attach File and Remove used to act the moment
    // they were pressed, on the stored track itself, so Cancel didn't undo
    // them - the old photo was already set aside and the track no longer
    // named it.
    private string? _pendingAttachPath;
    private bool _pendingRemove;

    // Asks whether another layout already has a name (see
    // TrackLayoutStore.IsNameTaken), and the name the layout had when the
    // dialog opened. Save refuses a name another layout uses, because a
    // layout is found everywhere else by its name alone - but a name left as
    // it was is let through, so a club whose file already has two tracks of
    // one name can still edit either of them (renaming one is the way out).
    private readonly Func<string, bool>? _isNameTaken;
    private readonly string _originalName;

    /// <summary>
    /// How wide the window is inside its frame at the normal text size, at
    /// the least: the 440px column it had and its margins. That column was
    /// widened from 348 once, for the two choices under "Main picture",
    /// which carry a whole sentence between them once a track plan is named
    /// ("Track plan (none drawn yet)"). Wider still when the photo's buttons
    /// need it - see the constructor.
    /// </summary>
    private const int DialogWidth = 472;

    /// <summary>The number boxes' width at the normal text size - wider when a box's largest value needs it.</summary>
    private const int NumberWidth = 110;

    /// <summary>Lines of text the notes box shows before it scrolls.</summary>
    private const int NotesLines = 4;

    /// <summary>The preview's size at the normal text size.</summary>
    private const int PreviewWidth = 120;
    private const int PreviewHeight = 90;

    /// <summary>Lines kept for the file's name beside the preview - see the constructor.</summary>
    private const int NameLines = 2;

    public TrackLayoutEditForm(TrackLayout? existing, Func<string, bool>? isNameTaken = null)
    {
        Result = existing ?? new TrackLayout();
        _isNameTaken = isNameTaken;
        _originalName = existing?.Name ?? string.Empty;

        Text = existing is null ? "Add Track Layout" : "Edit Track Layout";
        Font = AppTheme.BodyFont;

        // --- What is on the dialog ---
        _txtName.Text = Result.Name;

        _numTrackLength.DecimalPlaces = 1;
        _numTrackLength.Maximum = 100000;
        // Clamped: a value outside the box's range (a hand-edited or
        // restored file) threw here, and the track could not be edited.
        _numTrackLength.Value = (decimal)Math.Clamp(Result.TrackLengthFeet ?? 0, 0, 100000);

        var lblLengthHint = Hint("optional, informational only");

        _numAvgLapSeconds.DecimalPlaces = 2;
        _numAvgLapSeconds.Increment = 0.25m;
        _numAvgLapSeconds.Maximum = 600;
        _numAvgLapSeconds.Value = (decimal)Math.Clamp(Result.AverageLapSeconds ?? 0, 0, 600);

        _numMarshalPosts.Minimum = 0;
        _numMarshalPosts.Maximum = 20;
        _numMarshalPosts.Value = Math.Clamp(Result.MarshalPostCount, 0, 20);

        var lblNotes = new Label { Text = "Notes:", AutoSize = true, Font = AppTheme.BoldFont };
        _txtNotes.Multiline = true;
        _txtNotes.AcceptsReturn = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;
        _txtNotes.Text = Result.Notes;

        var lblAttachment = new Label { Text = "Photo or drawing:", AutoSize = true, Font = AppTheme.BoldFont };

        _picPreview.BorderStyle = BorderStyle.FixedSingle;
        _picPreview.SizeMode = PictureBoxSizeMode.Zoom;
        _picPreview.BackColor = AppTheme.Surface;

        // A fixed two lines rather than growing to fit. The text here is a
        // file name, and it changes while the dialog is up - "No file
        // attached." is one line, and the name that replaces it can wrap to
        // two - so a label that grew with it would move the buttons under it
        // as a file came and went. Anything longer than the two lines ends
        // in "..." instead (QuietEllipsis, so no pale Windows pop-up with
        // it). Given its height once the theme pass is done.
        _lblAttachmentName.AutoSize = false;
        _lblAttachmentName.ForeColor = AppTheme.TextMuted;
        QuietEllipsis.Attach(_lblAttachmentName);

        var btnAttach = new Button { Text = "Attach File..." };
        btnAttach.Click += BtnAttach_Click;

        _btnOpenAttachment.Text = "Open";
        _btnOpenAttachment.Click += (_, _) =>
        {
            var path = ShownAttachmentPath();

            // Belt and braces. TrackAttachmentStore.ResolvePath and
            // AttachProblem both refuse anything that is not on the allow-list
            // now, so this should never be the thing that stops it - but this
            // line hands a file name to the shell, and the bug it is guarding
            // against was precisely one read path forgetting to ask.
            if (path is null || !TrackAttachmentStore.IsAllowedAttachment(path))
            {
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                // No program associated with .dwg, or the file went while the
                // dialog was open. Said, rather than taking the dialog down
                // and losing everything else typed into it.
                AppDialog.Show(this, $"Couldn't open that file: {ex.Message}", "Open failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };

        _btnRemoveAttachment.Text = "Remove";
        _btnRemoveAttachment.Click += (_, _) =>
        {
            // Applied on Save - see _pendingRemove.
            _pendingAttachPath = null;
            _pendingRemove = true;
            RefreshAttachmentPreview();
        };

        // Which of the two pictures represents this track. Both are kept -
        // this only decides which one is shown where one is wanted.
        var lblMain = new Label { Text = "Main picture:", AutoSize = true, Font = AppTheme.BoldFont };

        _rdoPhotoMain.Text = "Uploaded photo";
        _rdoPhotoMain.AutoSize = true;
        _rdoPhotoMain.Checked = !Result.PreferPlanImage;

        _rdoPlanMain.Text = "Track plan";
        _rdoPlanMain.AutoSize = true;
        _rdoPlanMain.Checked = Result.PreferPlanImage;

        var lblMainHint = Hint("Draw a plan in Track Layouts → Track Designer.");

        var btnSave = new Button { Text = "Save" };
        btnSave.Click += BtnSave_Click;
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // --- Where it all goes ---

        // The four figures in one field grid, the captions in a column as
        // wide as the longest of them: four captions each pushing their own
        // box to a different x is what makes a dialog look thrown together
        // even when nothing actually overlaps. The length's note beside its
        // box, as it was - under it when the line is too short - and the
        // other two notes under their boxes, in the fields' column.
        var fields = new FieldGrid();
        fields.AddRow("Name:", _txtName, stretch: true);
        fields.AddRow("Track length (ft):", FieldGrid.Inline(_numTrackLength, lblLengthHint));
        fields.AddRow("Average lap time (sec):", _numAvgLapSeconds);
        fields.AddHint("optional - used to pace Demo Mode races on this track");
        fields.AddRow("Corner marshal posts:", _numMarshalPosts);
        fields.AddHint("0 = don't track marshals. Each heat is marshaled by the drivers from the previous heat.");

        // The preview's place: as big as the preview, whether or not there is
        // a picture to show in it, as it always was - the name and the
        // buttons beside it stay where they are as a file comes and goes. A
        // pixel all round the picture for the ring the theme draws outside
        // its edge (Pieces.RingRoom), which the edge of the place would
        // otherwise cut off.
        var previewSide = UiLayout.AtTextSize(PreviewWidth) + 2;

        var previewPlace = new Panel
        {
            Padding = new Padding(1),
            Height = UiLayout.AtTextSize(PreviewHeight) + 2,
            Tag = AppTheme.KeepBackColor,
        };

        _picPreview.Dock = DockStyle.Fill;
        previewPlace.Controls.Add(_picPreview);

        // Open and Remove hide themselves when there is nothing attached (see
        // RefreshAttachmentPreview). The window is made wide enough for all
        // three on one line (see below), so the line is one line either way.
        var attachButtons = new ButtonBar().With(btnAttach, _btnOpenAttachment, _btnRemoveAttachment);

        // The preview down the left, and the file's name over its buttons
        // beside it - as tall as the taller of the two, which at every text
        // size is the preview's place.
        var attachment = new Beside(previewSide)
        {
            SideOnLeft = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        attachment.AddSide(previewPlace, gapBelow: 0);
        attachment.Add(_lblAttachmentName, gapBelow: UiLayout.HintGap);
        attachment.Add(attachButtons, gapBelow: 0);

        // Side by side when they fit, under each other when they do not:
        // "Track plan (none drawn yet)" beside "Uploaded photo" is wider
        // than a narrow window at a large text size (FieldRow). One row, so
        // they are still one group of choices.
        var choices = new FieldRow();
        choices.Add(null, _rdoPhotoMain);
        choices.Add(null, _rdoPlanMain);

        // Everything but Save and Cancel is the body: on a screen too short
        // for all of it - 1280x720, or a larger text size - it scrolls, and
        // the two buttons under it stay in view.
        var page = new FillPage();
        var body = DialogFit.Body(page);

        body.Add(fields);
        body.Add(lblNotes, stretch: false, gapBelow: UiLayout.HintGap);
        body.Add(_txtNotes, gapBelow: UiLayout.SectionGap);

        // A pixel either side of the notes box for the ring the theme draws
        // round it, outside it, on what holds it (Pieces.RingRoom): stretched
        // across the body, the box's sides would be the body's edges, and the
        // ring's sides would be drawn past them, where nothing shows. The gap
        // under the box stays as it was.
        _txtNotes.Margin = new Padding(1, 0, 1, UiLayout.SectionGap);

        body.Add(lblAttachment, stretch: false, gapBelow: UiLayout.HintGap);
        body.Add(attachment);
        body.Add(lblMain, stretch: false, gapBelow: UiLayout.HintGap);
        body.Add(choices, gapBelow: UiLayout.HintGap);
        body.Add(lblMainHint, gapBelow: 0);

        // Bottom right, where this dialog has always put them, Cancel
        // furthest right - and Tab still reaches Save first, as it did: the
        // bar takes them right to left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);
        btnSave.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        CancelButton = btnCancel;

        BackColor = AppTheme.Background;
        AppTheme.ApplyRecursively(this);

        // Before the window is sized, not after: this is what fills in the
        // file name beside the preview and the wording on the Track plan
        // choice, which the layout measures.
        RefreshAttachmentPreview();

        // After the theme pass, in the faces it leaves them in: each number
        // box as wide as it was at the normal text size, at the size in use,
        // or as its largest value needs; the notes box its lines, and the
        // file's name its two, as the box and the label draw them
        // (Pieces.LineHeight).
        _numTrackLength.Width = NumberBoxWidth(_numTrackLength);
        _numAvgLapSeconds.Width = NumberBoxWidth(_numAvgLapSeconds);
        _numMarshalPosts.Width = NumberBoxWidth(_numMarshalPosts);
        _txtNotes.Height = NotesLines * Pieces.LineHeight(_txtNotes.Font) + UiLayout.HintGap * 2;
        _lblAttachmentName.Height = NameLines * Pieces.LineHeight(_lblAttachmentName.Font) + _lblAttachmentName.Padding.Vertical;

        // As wide as it was at the normal text size, at the size in use - or
        // wider when the photo's three buttons need more to stay on one line
        // beside the preview, as they do up to about one and a half times the
        // text size. On two lines, the line Open and Remove are on would come
        // and go with the file, and the body would scroll, or open a gap
        // above Save, every time a photo was attached or removed. Measured as
        // the bar measures itself on one line, in the faces the theme pass
        // left the buttons in, with room for the body's scrollbar, which
        // takes its width from everything in it on a screen too short.
        var width = Math.Max(
            UiLayout.AtTextSize(DialogWidth),
            UiLayout.PageLeft + previewSide + UiLayout.RowGap + attachButtons.OneLineWidth
                + SystemInformation.VerticalScrollBarWidth + UiLayout.PageRight);

        DialogFit.Keep(this, page, width, body: body, widthMeasured: true);
    }

    /// <summary>A muted note in the small font - beside the length's box, or under the two choices, where it wraps to the window's width.</summary>
    private static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = AppTheme.TextMuted,
        Font = AppTheme.SmallFont,
    };

    /// <summary>
    /// A number box's width: what the boxes had at the normal text size, at
    /// the size in use - or wider when the box's largest value needs more,
    /// written as the box writes it, in its own face, with room for its
    /// arrows.
    /// </summary>
    private static int NumberBoxWidth(NumericUpDown box)
    {
        var largest = box.Maximum.ToString("F" + box.DecimalPlaces, System.Globalization.CultureInfo.CurrentCulture);

        return Math.Max(
            UiLayout.AtTextSize(NumberWidth),
            TextRenderer.MeasureText(largest, box.Font).Width + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra);
    }

    private void BtnAttach_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Attach a photo or drawing",
            Filter = "Images and documents (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.pdf;*.dwg;*.dxf)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.pdf;*.dwg;*.dxf|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // Checked now, copied in on Save - see _pendingAttachPath.
        if (TrackAttachmentStore.AttachProblem(Result, dialog.FileName) is { } problem)
        {
            AppDialog.Show(this, $"Couldn't attach that file: {problem}", "Attach failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _pendingAttachPath = dialog.FileName;
        _pendingRemove = false;
        RefreshAttachmentPreview();
    }

    /// <summary>
    /// The file the dialog is showing as the photo: one just picked, the
    /// track's own, or none once Remove has been pressed. Null too when the
    /// track names a file that has gone missing.
    /// </summary>
    private string? ShownAttachmentPath() =>
        _pendingAttachPath ?? (_pendingRemove ? null : TrackAttachmentStore.ResolvePath(Result));

    /// <summary>Only offers a picture the track actually has.</summary>
    private void RefreshMainPictureChoice()
    {
        var hasPlan = Result.Design is { IsEmpty: false };
        var hasPhoto = _pendingAttachPath is not null
            || (!_pendingRemove && !string.IsNullOrWhiteSpace(Result.AttachmentFileName));

        _rdoPlanMain.Enabled = hasPlan;
        _rdoPhotoMain.Enabled = hasPhoto;

        _rdoPlanMain.Text = hasPlan
            ? $"Track plan ({Result.Design!.Tiles.Count} tiles)"
            : "Track plan (none drawn yet)";

        if (_rdoPlanMain.Checked && !hasPlan)
        {
            _rdoPhotoMain.Checked = true;
        }
        else if (_rdoPhotoMain.Checked && !hasPhoto && hasPlan)
        {
            _rdoPlanMain.Checked = true;
        }
    }

    private void RefreshAttachmentPreview()
    {
        RefreshMainPictureChoice();

        _picPreview.Image?.Dispose();
        _picPreview.Image = null;

        var path = ShownAttachmentPath();
        if (path is null)
        {
            _picPreview.Visible = false;
            _lblAttachmentName.Text = "No file attached.";
            _btnRemoveAttachment.Visible = false;
            _btnOpenAttachment.Visible = false;
            return;
        }

        _lblAttachmentName.Text = Path.GetFileName(path);
        _btnRemoveAttachment.Visible = true;
        _btnOpenAttachment.Visible = true;

        if (TrackAttachmentStore.IsImageFile(path))
        {
            var image = TrackAttachmentStore.LoadPreviewImageFrom(path);
            if (image is not null)
            {
                _picPreview.Image = image;
                _picPreview.Visible = true;
            }
            else
            {
                _picPreview.Visible = false;
            }
        }
        else
        {
            _picPreview.Visible = false;
        }
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        var name = _txtName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            AppDialog.Show(this, "Name is required.", "Missing name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Checked before the photo below, so a refused name leaves nothing
        // on disk changed and the dialog open to pick another.
        if (!string.Equals(name, _originalName.Trim(), StringComparison.Ordinal) && _isNameTaken?.Invoke(name) == true)
        {
            AppDialog.Show(this,
                $"There's already a track layout called '{name}'. Give this one a different name - the track picker, records and Demo pace tell layouts apart by name.",
                "Name already used", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtName.Focus();
            _txtName.SelectAll();
            return;
        }

        // The photo first: it's the one part of saving that can fail, and a
        // failure leaves the dialog open with the track as it was.
        try
        {
            if (_pendingAttachPath is not null)
            {
                SetAsideAttachment = TrackAttachmentStore.Attach(Result, _pendingAttachPath);
                AttachmentChanged = true;
            }
            else if (_pendingRemove)
            {
                SetAsideAttachment = TrackAttachmentStore.RemoveAttachment(Result);
                AttachmentChanged = true;
            }
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't attach that file: {ex.Message}", "Attach failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _pendingAttachPath = null;
        _pendingRemove = false;

        // Preserve everything not edited on this form (notably the
        // all-time record) rather than rebuilding from scratch - a
        // previous version of this dialog discarded the record silently
        // whenever a track's name or notes were edited.
        Result.Name = name;
        Result.Notes = _txtNotes.Text;
        Result.TrackLengthFeet = _numTrackLength.Value > 0 ? (double)_numTrackLength.Value : null;
        Result.AverageLapSeconds = _numAvgLapSeconds.Value > 0 ? (double)_numAvgLapSeconds.Value : null;
        Result.MarshalPostCount = (int)_numMarshalPosts.Value;
        Result.PreferPlanImage = _rdoPlanMain.Checked;

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// 17 Oct 2026, Bug Hunt 8, A31-6: the last preview is ours to dispose
    /// (TrackAttachmentStore.LoadPreviewImageFrom: "Caller owns and disposes
    /// it"), and a PictureBox does not dispose an image it is given - so the
    /// full-size decoded bitmap waited for the finalizer.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var image = _picPreview.Image;
            _picPreview.Image = null;
            image?.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Lists saved track layouts with add/edit/delete, and shows the one
/// selected beside the list.
///
/// On the layout pieces (LayoutPieces.cs): two cards side by side, the
/// list with its buttons under it, and the selected track - its name and
/// its picture, as big as the card. It used to be a 400px list and a 240px
/// preview box placed by hand over a 680x360 floor, the buttons at a fixed
/// y, all in faces handed out once - so a short window cut the buttons off
/// and a larger text size ran the name into the picture.
/// </summary>
public sealed class TrackLayoutManagerControl : UserControl
{
    private readonly TrackLayoutStore _store;
    private readonly ListView _list = new();
    private readonly Label _lblPreviewName = new();
    private readonly PictureBox _picPreview = new();
    private readonly Label _lblPreviewEmpty = new();
    private readonly AppSettings _settings;
    private readonly UndoService _undo;

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The selected track's card at the normal text size - about two fifths of the page, between these.</summary>
    private const int PreviewLeastWidth = 260;
    private const int PreviewMostWidth = 480;

    /// <summary>The least the picture gets at the normal text size - below that the page scrolls.</summary>
    private const int PictureLeastHeight = 160;

    public TrackLayoutManagerControl(TrackLayoutStore store, AppSettings settings, UndoService undo)
    {
        _store = store;
        _undo = undo;
        _settings = settings;

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize. No MinimumSize: the 680x360 floor was honoured
        // over the size the page was given. A short window scrolls the page
        // instead (FillPage).
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshList();
    }

    /// <summary>The page's own face changed - handed the body face at a new text size - so everything else it keeps in step follows. See TextSize.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>A new text size, reaching this page through its parent's font - the only way it does, since the page wears a face of its own. See TextSize.</summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
    }

    private void BuildLayout()
    {
        // --- The layouts ---
        _list.View = View.Details;
        _list.FullRowSelect = true;

        // Who holds each sector record shows on hover - see RefreshList.
        _list.ShowItemToolTips = true;
        _list.MultiSelect = false;
        _list.GridLines = true;
        _list.Columns.Add("Name");
        _list.Columns.Add("Record");
        _list.Columns.Add("Attachment");
        _list.Columns.Add("Plan");
        _list.DoubleClick += (_, _) => EditSelected();
        _list.SelectedIndexChanged += (_, _) =>
        {
            RefreshPreview();
            if (_list.SelectedItems.Count > 0)
            {
                _settings.LastViewedTrackLayoutName = _list.SelectedItems[0].Text;
                _settings.Save();
            }
        };

        // The name takes what the others leave - see Pieces.FitListColumns.
        Pieces.FitListColumns(_list, 0, 140, 130, 110, 110);

        var btnAdd = new Button { Text = "Add..." };
        btnAdd.Click += (_, _) => AddNew();

        var btnEdit = new Button { Text = "Edit..." };
        btnEdit.Click += (_, _) => EditSelected();

        var btnDelete = new Button { Text = "Delete" };
        btnDelete.Click += (_, _) => DeleteSelected();

        var btnResetRecord = new Button { Text = "Reset Record" };
        btnResetRecord.Click += (_, _) => ResetSelectedRecord();

        var btnDesign = new Button { Text = "Track Designer..." };
        btnDesign.Click += (_, _) => DesignSelected();

        var layouts = new Section("Track layouts");

        layouts.AddFill(_list, () => _list.Font.Height * 2 + 4 * (_list.Font.Height + 6));
        layouts.Add(new ButtonBar().With(btnAdd, btnEdit, btnDelete, btnResetRecord, btnDesign), gapBelow: 0);

        // --- The selected track ---
        _lblPreviewName.AutoSize = true;
        _textSize.Wear(_lblPreviewName, () => AppTheme.HeadingFont);

        _picPreview.BorderStyle = BorderStyle.FixedSingle;
        _picPreview.SizeMode = PictureBoxSizeMode.Zoom;
        _picPreview.BackColor = AppTheme.Surface;
        _picPreview.Visible = false;

        // Shown when there is no picture, over the room the picture keeps -
        // which stays, so the card does not change shape as the selection
        // does.
        _lblPreviewEmpty.Text = "Select a track layout to see it here.";
        _lblPreviewEmpty.AutoSize = true;
        _lblPreviewEmpty.ForeColor = AppTheme.TextMuted;

        var preview = new Section("Selected track");

        preview.Add(_lblPreviewName);
        preview.Add(_lblPreviewEmpty);
        preview.AddFill(_picPreview, () => UiLayout.AtTextSize(PictureLeastHeight), gapBelow: 0);

        // --- The page ---
        Beside? band = null;

        band = new Beside(() => PreviewWidth(band?.Width ?? 0));

        band.AddFill(layouts, layouts.LeastHeight);
        band.AddSideFill(preview, preview.LeastHeight, gapBelow: 0);

        var page = new FillPage();

        page.AddFill(band, band.LeastHeight, gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>The selected track's card: about two fifths of the page, between 260px and 480px at the normal text size, at the size in use.</summary>
    private static int PreviewWidth(int page) =>
        Math.Clamp((int)(page * 0.4f), UiLayout.AtTextSize(PreviewLeastWidth), UiLayout.AtTextSize(PreviewMostWidth));

    /// <summary>Called when this page becomes visible - layouts can gain records from Race Control while it's inactive.</summary>
    public void RefreshAll() => RefreshList();

    private void RefreshList()
    {
        var previouslySelectedId = _list.SelectedItems.Count > 0 ? (string)_list.SelectedItems[0].Tag! : null;

        _list.Items.Clear();
        foreach (var t in _store.All)
        {
            var item = new ListViewItem(t.Name) { Tag = t.Id };
            // The sector records after the lap record, for the way the lap is
            // cut now - hover for who holds each.
            var sectorRecords = t.HasSectorRecords()
                ? t.CurrentSectorRecords().Where(r => r is not null).Select(r => r!).ToList()
                : new List<SectorRecord>();

            var sectors = sectorRecords.Count == 0
                ? string.Empty
                : "  ·  " + string.Join(", ", sectorRecords.Select(r => $"S{r.Sector} {Sectors.Format(r.Ms)}"));

            item.SubItems.Add(t.RecordLapMs > 0
                ? $"{t.RecordLapMs / 1000.0:0.000}s - {t.RecordHolderName}{sectors}"
                : sectorRecords.Count > 0 ? "(none yet)" + sectors : "(none yet)");

            if (sectorRecords.Count > 0)
            {
                item.ToolTipText = string.Join(Environment.NewLine,
                    sectorRecords.Select(r => $"Sector {r.Sector} record: {Sectors.Format(r.Ms)} by {r.Holder}{(r.SetUtc is { } set ? $", {set.ToLocalTime():d MMM yyyy}" : string.Empty)}"));
            }
            item.SubItems.Add(string.IsNullOrWhiteSpace(t.AttachmentFileName) ? "-" : t.AttachmentFileName);

            item.SubItems.Add(t.Design is { IsEmpty: false } design
                ? $"{design.Tiles.Count} tiles"
                : "-");

            _list.Items.Add(item);

            var shouldSelect = previouslySelectedId is not null
                ? t.Id == previouslySelectedId
                : t.Name == _settings.LastViewedTrackLayoutName;
            if (shouldSelect)
            {
                item.Selected = true;
            }
        }

        RefreshPreview();
    }

    /// <summary>Shows the selected track's name and photo (if it has one) in the side panel - updates live as the selection changes, no double-click needed.</summary>
    private void RefreshPreview()
    {
        _picPreview.Image?.Dispose();
        _picPreview.Image = null;

        if (_list.SelectedItems.Count == 0)
        {
            _lblPreviewName.Text = string.Empty;
            _picPreview.Visible = false;
            _lblPreviewEmpty.Visible = true;
            return;
        }

        var id = (string)_list.SelectedItems[0].Tag!;
        var track = _store.Get(id);
        if (track is null)
        {
            return;
        }

        _lblPreviewName.Text = track.Name;

        // Whichever picture this track is set to show - the drawn plan or an
        // uploaded photo - with a fallback to the other when there is only
        // one of them.
        var image = TrackImagery.MainPicture(
            track,
            new Size(Math.Max(120, _picPreview.Width * 2), Math.Max(120, _picPreview.Height * 2)),
            AppTheme.Surface);

        if (image is not null)
        {
            _picPreview.Image = image;
            _picPreview.Visible = true;
            _lblPreviewEmpty.Visible = false;
        }
        else
        {
            _picPreview.Visible = false;
            _lblPreviewEmpty.Text = string.IsNullOrWhiteSpace(track.AttachmentFileName)
                ? "No photo or plan yet - attach one, or draw the layout in the Track Designer."
                : $"Attachment \"{track.AttachmentFileName}\" isn't an image that can be previewed here.";
            _lblPreviewEmpty.Visible = true;
        }
    }

    private void AddNew()
    {
        var store = _store;
        using var form = new TrackLayoutEditForm(null, name => store.IsNameTaken(name, null));
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            var newId = form.Result.Id;
            _undo.Capture($"Add track layout '{form.Result.Name}'", () =>
            {
                if (store.Get(newId) is not null)
                {
                    store.Delete(newId);
                }
            });

            _store.Upsert(form.Result);
            RefreshList();
        }
    }

    private void EditSelected()
    {
        if (_list.SelectedItems.Count == 0)
        {
            return;
        }

        var id = (string)_list.SelectedItems[0].Tag!;
        var existing = _store.Get(id);
        if (existing is null)
        {
            return;
        }

        // Everything the dialog can change, taken before it opens: it edits
        // the stored layout itself when Save is pressed (keeping the record
        // and the plan, which it doesn't show), so afterwards the old values
        // would be gone.
        var trackId = existing.Id;
        var before = (
            existing.Name,
            existing.Notes,
            existing.TrackLengthFeet,
            existing.AverageLapSeconds,
            existing.MarshalPostCount,
            existing.PreferPlanImage,
            existing.AttachmentFileName);

        using var form = new TrackLayoutEditForm(existing, name => _store.IsNameTaken(name, trackId));
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            // Only the fields the dialog edits go back, so a record set in a
            // race since - or a plan drawn in the designer - survives the undo.
            var attachmentChanged = form.AttachmentChanged;
            var setAside = form.SetAsideAttachment;
            var store = _store;
            _undo.Capture($"Edit track layout '{before.Name}'", () =>
            {
                if (store.Get(trackId) is not { } target)
                {
                    return;
                }

                if (attachmentChanged)
                {
                    if (setAside is not null)
                    {
                        // The old photo back, over the one the edit attached.
                        TrackAttachmentStore.RestoreForTrack(trackId, setAside);
                    }
                    else
                    {
                        // There was no photo before: the one the edit attached
                        // goes aside (the undo holding folder clears it later).
                        TrackAttachmentStore.StashForTrack(trackId);
                    }

                    target.AttachmentFileName = before.AttachmentFileName;
                }

                target.Name = before.Name;
                target.Notes = before.Notes;
                target.TrackLengthFeet = before.TrackLengthFeet;
                target.AverageLapSeconds = before.AverageLapSeconds;
                target.MarshalPostCount = before.MarshalPostCount;
                target.PreferPlanImage = before.PreferPlanImage;
                store.Upsert(target);
            });

            _store.Upsert(form.Result);
            RefreshList();
        }
    }

    /// <summary>
    /// Opens the tile designer for the selected track.
    ///
    /// The plan is saved onto the layout itself, and - unless the user says
    /// otherwise - a picture of it is saved as the track's photo, so it
    /// shows up on this page, in the results card and anywhere else a track
    /// photo is used, without anybody having to export and re-attach it.
    /// </summary>
    private void DesignSelected()
    {
        // With nothing selected this draws a NEW track rather than refusing:
        // starting from a blank floor is how most layouts begin, and having
        // to create an empty track first just to draw one is a step for the
        // sake of it.
        var existing = _list.SelectedItems.Count > 0
            ? _store.Get((string)_list.SelectedItems[0].Tag!)
            : null;

        // The designer's Save only checks that a name was typed; whether
        // another layout already has it is checked here, where the store is.
        // A layout is found everywhere else by its name alone (the Race
        // Control picker, the all-time record, the Demo pace), so a second
        // "Club Oval" would share - and overwrite - the first one's record.
        // A taken name opens the designer again on what was just drawn,
        // under the name typed, rather than throwing the drawing away. A
        // name left as it was is let through, so a track that already shares
        // its name in an older file can still have its plan redrawn.
        var designToOpen = existing?.Design;
        var nameToOpen = existing?.Name ?? string.Empty;
        RcpTrackDesign chosenDesign;
        string chosenName;
        bool useAsPhoto;

        // WHY (20 Oct 2026, Bug Hunt 8 #84): once a taken name reopens the
        // designer, the drawing in it has not been saved anywhere, so the
        // designer must ask before Escape, Cancel or the X throws it away -
        // even though nothing changed since it reopened.
        var reopened = false;

        // WHY (23 Oct 2026, bug hunt 10): the designer always ticked "Show
        // this plan as the track's main picture", so saving a plan silently
        // overrode a club's "photo is the main picture" choice. A track that
        // already has a plan opens with its own choice; a first plan still
        // defaults to showing it. A reopen keeps what was ticked.
        var useAsPhotoToOpen = existing?.Design is { IsEmpty: false } ? existing.PreferPlanImage : true;
        while (true)
        {
            using var form = new RcpTrackEditorForm(
                designToOpen,
                nameToOpen,
                existing?.AverageLapSeconds,
                unsavedOnOpen: reopened,
                useAsPhoto: useAsPhotoToOpen);

            if (form.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var unchanged = existing is not null && string.Equals(form.TrackName, existing.Name.Trim(), StringComparison.Ordinal);
            if (!unchanged && _store.IsNameTaken(form.TrackName, existing?.Id))
            {
                AppDialog.Show(this,
                    $"There's already a track layout called '{form.TrackName}'. Give this one a different name - the track picker, records and Demo pace tell layouts apart by name.",
                    "Name already used", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                designToOpen = form.Result;
                nameToOpen = form.TrackName;
                useAsPhotoToOpen = form.UseAsPhoto;
                reopened = true;
                continue;
            }

            chosenDesign = form.Result;
            chosenName = form.TrackName;
            useAsPhoto = form.UseAsPhoto;
            break;
        }

        var track = existing ?? new TrackLayout();
        var isNew = existing is null;

        // Everything the designer can change, captured first so one undo
        // puts the track back exactly as it was.
        var previousDesign = track.Design;
        var previousName = track.Name;
        var previousPreference = track.PreferPlanImage;
        var trackId = track.Id;

        // Store only: the host refreshes this page (RefreshAll) straight
        // after an undo, so the undo never reaches into a page that may have
        // gone.
        var store = _store;
        _undo.Capture(isNew ? $"Add track '{chosenName}'" : $"Track plan for '{track.Name}'", () =>
        {
            if (isNew)
            {
                if (store.Get(trackId) is not null)
                {
                    store.Delete(trackId);
                }
            }
            else if (store.Get(trackId) is { } target)
            {
                target.Design = previousDesign;
                target.Name = previousName;
                target.PreferPlanImage = previousPreference;
                store.Upsert(target);
            }
        });

        track.Name = chosenName;
        track.Design = chosenDesign;

        // A preference, not a replacement: whatever photo was attached is
        // still attached, and can be switched back to at any time.
        track.PreferPlanImage = useAsPhoto && !chosenDesign.IsEmpty;

        _store.Upsert(track);

        _settings.LastViewedTrackLayoutName = track.Name;
        _settings.Save();

        RefreshList();
    }

    private void DeleteSelected()
    {
        if (_list.SelectedItems.Count == 0)
        {
            return;
        }

        var id = (string)_list.SelectedItems[0].Tag!;
        var name = _list.SelectedItems[0].Text;
        var confirm = AppDialog.Show(this, $"Delete track layout '{name}'?", "Delete track layout", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm == DialogResult.Yes)
        {
            var removed = _store.Get(id);

            // Where the delete sets the photo aside - filled in below, before
            // the undo can ever run.
            string? setAside = null;

            if (removed is not null)
            {
                var store = _store;
                _undo.Capture($"Delete track layout '{name}'", () =>
                {
                    // Already back (by id) - never listed twice.
                    if (store.Get(removed.Id) is not null)
                    {
                        return;
                    }

                    // Bring the photo back too, not just the record. Only a
                    // folder this delete set aside: without one there was no
                    // photo, and the old per-track stash may hold another's.
                    if (setAside is not null)
                    {
                        TrackAttachmentStore.RestoreForTrack(removed.Id, setAside);
                    }

                    store.Upsert(removed);
                });
            }

            setAside = _store.Delete(id);
            RefreshList();
        }
    }

    private void ResetSelectedRecord()
    {
        if (_list.SelectedItems.Count == 0)
        {
            return;
        }

        var id = (string)_list.SelectedItems[0].Tag!;
        var track = _store.Get(id);
        var hasSectorRecords = track?.HasSectorRecords() == true;

        if (track is null || (track.RecordLapMs == 0 && !hasSectorRecords))
        {
            return;
        }

        var what = track.RecordLapMs > 0
            ? $"the all-time record ({track.RecordLapMs / 1000.0:0.000}s by {track.RecordHolderName})"
            : "the sector records";

        var confirm = AppDialog.Show(this,
            $"Clear {what} for '{track.Name}'?{(hasSectorRecords && track.RecordLapMs > 0 ? " Its sector records are cleared too." : string.Empty)} " +
            "Useful if the track's physical layout has changed since it was set.",
            "Reset track record", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        // Capture the record values before clearing them, so undo can put
        // exactly those back rather than approximating.
        var oldMs = track.RecordLapMs;
        var oldHolder = track.RecordHolderName;
        var oldSet = track.RecordSetUtc;
        var oldRecordSectors = track.RecordSectorsMs?.ToList();

        // 17 Oct 2026, Bug Hunt 8, A31-2: the cut those sectors were timed
        // against, put back with them. A record set between the reset and
        // the undo writes its own cut (or null), and the old sectors were
        // then paired with it.
        var oldRecordCut = track.RecordSectorCut;
        var oldSectorRecords = track.SectorRecordsCopy();
        var oldLatestCut = track.LatestSectorCut;
        var trackId = track.Id;

        // Store only - the host refreshes this page after an undo.
        var store = _store;
        _undo.Capture($"Reset record on '{track.Name}'", () =>
        {
            var restoreTarget = store.Get(trackId);
            if (restoreTarget is not null)
            {
                // The lap record the same way as the sectors below: a heat
                // run since the reset can have set a quicker one, and undo
                // used to put the old, slower record back over it. Only
                // when there is none now, or the old one was quicker - and
                // then its holder, date and sectors with it.
                if (oldMs > 0 && (restoreTarget.RecordLapMs == 0 || oldMs < restoreTarget.RecordLapMs))
                {
                    restoreTarget.RecordLapMs = oldMs;
                    restoreTarget.RecordHolderName = oldHolder;
                    restoreTarget.RecordSetUtc = oldSet;
                    restoreTarget.RecordSectorsMs = oldRecordSectors;
                    restoreTarget.RecordSectorCut = oldRecordCut;
                }

                // Sector records merged back, not put over: a heat run
                // since the reset can have set some, and they stand unless
                // one taken away was quicker.
                restoreTarget.RestoreSectorRecords(oldSectorRecords);
                restoreTarget.LatestSectorCut ??= oldLatestCut;
                store.Upsert(restoreTarget);
            }
        });

        track.RecordLapMs = 0;
        track.RecordHolderName = string.Empty;
        track.RecordSetUtc = null;
        track.RecordSectorsMs = null;
        track.RecordSectorCut = null;
        track.SectorRecordsByCut = null;
        track.LatestSectorCut = null;
        _store.Upsert(track);
        RefreshList();
    }
}
