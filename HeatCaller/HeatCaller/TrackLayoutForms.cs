namespace HeatCaller;

/// <summary>Small add/edit dialog for a single TrackLayout.</summary>
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
    private readonly RadioButton _rdoPhotoMain = new();
    private readonly RadioButton _rdoPlanMain = new();

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

    /// <summary>The page margin, and the gap kept round everything on this dialog.</summary>
    private const int Pad = UiLayout.PageLeft;

    /// <summary>
    /// How wide the dialog's content column is.
    ///
    /// Wider than the 348 it used to be. The radio buttons under "Main
    /// picture" carry a whole sentence between them once a track plan is
    /// named ("Track plan (none drawn yet)"), and at the current text size
    /// the second one ran off the right-hand edge.
    /// </summary>
    private const int Content = 440;

    public TrackLayoutEditForm(TrackLayout? existing)
    {
        Result = existing ?? new TrackLayout();

        Text = existing is null ? "Add Track Layout" : "Edit Track Layout";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = AppTheme.BodyFont;

        // --- What is on the dialog ---
        //
        // Built without positions. Nothing here can measure itself yet: an
        // unparented control still carries the WinForms default font, so
        // every width and height it reports is for a size it will never be
        // drawn at. The layout happens further down, once these have a
        // parent, the app's font and their final text.
        var lblName = new Label { Text = "Name:", AutoSize = true };
        _txtName.Text = Result.Name;

        var lblLength = new Label { Text = "Track length (ft):", AutoSize = true };
        _numTrackLength.Width = 110;
        _numTrackLength.DecimalPlaces = 1;
        _numTrackLength.Maximum = 100000;
        // Clamped: a value outside the box's range (a hand-edited or
        // restored file) threw here, and the track could not be edited.
        _numTrackLength.Value = (decimal)Math.Clamp(Result.TrackLengthFeet ?? 0, 0, 100000);

        var lblLengthHint = Hint("optional, informational only");

        var lblAvgLap = new Label { Text = "Average lap time (sec):", AutoSize = true };
        _numAvgLapSeconds.Width = 110;
        _numAvgLapSeconds.DecimalPlaces = 2;
        _numAvgLapSeconds.Increment = 0.25m;
        _numAvgLapSeconds.Maximum = 600;
        _numAvgLapSeconds.Value = (decimal)Math.Clamp(Result.AverageLapSeconds ?? 0, 0, 600);

        var lblAvgLapHint = Hint("optional - used to pace Demo Mode races on this track");

        var lblMarshals = new Label { Text = "Corner marshal posts:", AutoSize = true };
        _numMarshalPosts.Width = 110;
        _numMarshalPosts.Minimum = 0;
        _numMarshalPosts.Maximum = 20;
        _numMarshalPosts.Value = Math.Clamp(Result.MarshalPostCount, 0, 20);

        var lblMarshalsHint = Hint("0 = don't track marshals. Each heat is marshalled by the drivers from the previous heat.");

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
        // file name, and it is not known at layout time - "No file
        // attached." measures as one line, and the name that replaces it
        // wraps to two. An AutoSize label would have grown downward onto
        // the Attach File button and, being in front of it, taken its
        // clicks. Anything longer than the two lines ends in "..." instead
        // (QuietEllipsis, so no pale Windows pop-up with it).
        _lblAttachmentName.AutoSize = false;
        _lblAttachmentName.ForeColor = AppTheme.TextMuted;

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

        Controls.Add(lblName);
        Controls.Add(_txtName);
        Controls.Add(lblLength);
        Controls.Add(_numTrackLength);
        Controls.Add(lblLengthHint);
        Controls.Add(lblAvgLap);
        Controls.Add(_numAvgLapSeconds);
        Controls.Add(lblAvgLapHint);
        Controls.Add(lblMarshals);
        Controls.Add(_numMarshalPosts);
        Controls.Add(lblMarshalsHint);
        Controls.Add(lblNotes);
        Controls.Add(_txtNotes);
        Controls.Add(lblAttachment);
        Controls.Add(_picPreview);
        Controls.Add(_lblAttachmentName);
        Controls.Add(btnAttach);
        Controls.Add(_btnOpenAttachment);
        Controls.Add(_btnRemoveAttachment);
        Controls.Add(lblMain);
        Controls.Add(_rdoPhotoMain);
        Controls.Add(_rdoPlanMain);
        Controls.Add(lblMainHint);
        Controls.Add(btnSave);
        Controls.Add(btnCancel);

        CancelButton = btnCancel;

        BackColor = AppTheme.Background;
        AppTheme.ApplyRecursively(this);

        // Before the layout, not after: this is what fills in the file name
        // beside the preview and the wording on the Track plan radio
        // button, and a control measured while its text is still empty is
        // measured at the wrong size.
        RefreshAttachmentPreview();

        // --- Where it all goes ---
        //
        // Measured top to bottom. Every coordinate on this dialog used to
        // be a number typed in by hand, chosen against a smaller font: at
        // the size the app runs now the Notes box reached down over "Photo
        // or drawing:" and over the file name beside the preview, so the
        // name of the attached file read through the bottom of the notes,
        // and the radio buttons ran off the right-hand edge. Each row is
        // placed from the measured bottom of the one above it now, so it
        // holds at any text size - Ctrl +/- included.
        const int right = Pad + Content;

        // One field column for the four label-and-box rows, set by the
        // longest caption. Four captions each pushing their own box to a
        // different x is what makes a dialog look thrown together even when
        // nothing actually overlaps.
        var fieldLeft = Pad + Math.Max(
            Math.Max(lblName.PreferredWidth, lblLength.PreferredWidth),
            Math.Max(lblAvgLap.PreferredWidth, lblMarshals.PreferredWidth)) + UiLayout.LabelGap;

        var y = UiLayout.PageTop;

        _txtName.Width = right - fieldLeft;
        _txtName.Location = new Point(fieldLeft, y);
        lblName.Location = new Point(Pad, UiLayout.CentreOn(lblName, y, _txtName.Height));
        y = UiLayout.Below(_txtName, UiLayout.RowGap);

        _numTrackLength.Location = new Point(fieldLeft, y);
        lblLength.Location = new Point(Pad, UiLayout.CentreOn(lblLength, y, _numTrackLength.Height));

        var hintLeft = fieldLeft + _numTrackLength.Width + UiLayout.LabelGap;
        lblLengthHint.MaximumSize = new Size(Math.Max(80, right - hintLeft), 0);
        lblLengthHint.Location = new Point(hintLeft, UiLayout.CentreOn(lblLengthHint, y, _numTrackLength.Height));
        y = UiLayout.Below(_numTrackLength, UiLayout.RowGap);

        _numAvgLapSeconds.Location = new Point(fieldLeft, y);
        lblAvgLap.Location = new Point(Pad, UiLayout.CentreOn(lblAvgLap, y, _numAvgLapSeconds.Height));
        y = UiLayout.Below(_numAvgLapSeconds, UiLayout.HintGap);

        lblAvgLapHint.MaximumSize = new Size(Content, 0);
        lblAvgLapHint.Location = new Point(Pad, y);
        y = UiLayout.Below(lblAvgLapHint, UiLayout.RowGap);

        _numMarshalPosts.Location = new Point(fieldLeft, y);
        lblMarshals.Location = new Point(Pad, UiLayout.CentreOn(lblMarshals, y, _numMarshalPosts.Height));
        y = UiLayout.Below(_numMarshalPosts, UiLayout.HintGap);

        lblMarshalsHint.MaximumSize = new Size(Content, 0);
        lblMarshalsHint.Location = new Point(Pad, y);
        y = UiLayout.Below(lblMarshalsHint, UiLayout.SectionGap);

        lblNotes.Location = new Point(Pad, y);
        y = UiLayout.Below(lblNotes, UiLayout.HintGap);

        // Four lines of the font it is actually drawn in, rather than a
        // flat eighty pixels that was four lines once and is two now.
        _txtNotes.Location = new Point(Pad, y);
        _txtNotes.Size = new Size(Content, Font.Height * 4 + UiLayout.HintGap * 2);
        y = UiLayout.Below(_txtNotes, UiLayout.SectionGap);

        lblAttachment.Location = new Point(Pad, y);
        y = UiLayout.Below(lblAttachment, UiLayout.HintGap);

        _picPreview.Size = new Size(120, 90);
        _picPreview.Location = new Point(Pad, y);

        var attachLeft = Pad + _picPreview.Width + UiLayout.LabelGap;
        _lblAttachmentName.Location = new Point(attachLeft, y);
        _lblAttachmentName.Size = new Size(Math.Max(120, right - attachLeft), Font.Height * 2);
        QuietEllipsis.Attach(_lblAttachmentName);

        // Wrapped rather than run off the edge if the captions grow - see
        // UiLayout.FlowButtons. Open and Remove hide themselves when there
        // is nothing attached, but they keep their place in the row either
        // way, so it does not jump about as a file comes and goes.
        var attachBottom = UiLayout.FlowButtons(
            attachLeft,
            _lblAttachmentName.Bottom + UiLayout.HintGap,
            right,
            btnAttach, _btnOpenAttachment, _btnRemoveAttachment);

        y = Math.Max(_picPreview.Bottom, attachBottom) + UiLayout.SectionGap;

        lblMain.Location = new Point(Pad, y);
        y = UiLayout.Below(lblMain, UiLayout.HintGap);

        _rdoPhotoMain.Location = new Point(Pad, y);

        // Side by side when they fit, stacked when they do not. "Track plan
        // (none drawn yet)" beside "Uploaded photo" is wider than this
        // dialog at a large text size, and the second one simply left the
        // screen.
        var planLeft = UiLayout.After(_rdoPhotoMain);

        if (planLeft + _rdoPlanMain.PreferredSize.Width <= right)
        {
            _rdoPlanMain.Location = new Point(planLeft, y);
            y = UiLayout.Below(_rdoPhotoMain, UiLayout.HintGap);
        }
        else
        {
            y = UiLayout.Below(_rdoPhotoMain, UiLayout.HintGap);
            _rdoPlanMain.Location = new Point(Pad, y);
            y = UiLayout.Below(_rdoPlanMain, UiLayout.HintGap);
        }

        lblMainHint.MaximumSize = new Size(Content, 0);
        lblMainHint.Location = new Point(Pad, y);
        y = UiLayout.Below(lblMainHint, UiLayout.SectionGap);

        // Bottom right, where this dialog has always put them.
        var saveWidth = UiLayout.WidthFor(btnSave);
        var cancelWidth = UiLayout.WidthFor(btnCancel);

        btnCancel.Size = new Size(cancelWidth, UiLayout.ButtonHeight);
        btnCancel.Location = new Point(right - cancelWidth, y);

        btnSave.Size = new Size(saveWidth, UiLayout.ButtonHeight);
        btnSave.Location = new Point(btnCancel.Left - UiLayout.ButtonGap - saveWidth, y);

        ClientSize = new Size(right + Pad, y + UiLayout.ButtonHeight + Pad);
    }

    /// <summary>A muted note under a field, in the small font, wrapped to the width it is given at layout time.</summary>
    private static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = AppTheme.TextMuted,
        Font = AppTheme.SmallFont,
    };

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
            item.SubItems.Add(t.RecordLapMs > 0
                ? $"{t.RecordLapMs / 1000.0:0.000}s - {t.RecordHolderName}"
                : "(none yet)");
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
        using var form = new TrackLayoutEditForm(null);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            var newId = form.Result.Id;
            var store = _store;
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

        using var form = new TrackLayoutEditForm(existing);
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

        using var form = new RcpTrackEditorForm(
            existing?.Design,
            existing?.Name ?? string.Empty,
            existing?.AverageLapSeconds);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
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
        _undo.Capture(isNew ? $"Add track '{form.TrackName}'" : $"Track plan for '{track.Name}'", () =>
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

        track.Name = form.TrackName;
        track.Design = form.Result;

        // A preference, not a replacement: whatever photo was attached is
        // still attached, and can be switched back to at any time.
        track.PreferPlanImage = form.UseAsPhoto && !form.Result.IsEmpty;

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
        if (track is null || track.RecordLapMs == 0)
        {
            return;
        }

        var confirm = AppDialog.Show(this,
            $"Clear the all-time record ({track.RecordLapMs / 1000.0:0.000}s by {track.RecordHolderName}) for '{track.Name}'? " +
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
        var trackId = track.Id;

        // Store only - the host refreshes this page after an undo.
        var store = _store;
        _undo.Capture($"Reset record on '{track.Name}'", () =>
        {
            var restoreTarget = store.Get(trackId);
            if (restoreTarget is not null)
            {
                restoreTarget.RecordLapMs = oldMs;
                restoreTarget.RecordHolderName = oldHolder;
                restoreTarget.RecordSetUtc = oldSet;
                store.Upsert(restoreTarget);
            }
        });

        track.RecordLapMs = 0;
        track.RecordHolderName = string.Empty;
        track.RecordSetUtc = null;
        _store.Upsert(track);
        RefreshList();
    }
}
