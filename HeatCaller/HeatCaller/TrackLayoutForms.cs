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
    private readonly ComboBox _cmbCarScale = new();
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
        // Built with its layout held - see LayoutFreeze.HoldWhileBuilding
        // (18 Nov 2026): built live, nearly every part had its window before
        // the theme pass, which made many of them again.
        using var building = LayoutFreeze.HoldWhileBuilding();

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

        var lblLengthHint = Hint("optional - gives Race Analytics its car lengths");

        // The scale of car raced here, so a gap is counted in cars of the
        // right length (RaceCoach.CarScales) - 14 Nov 2026, Asher.
        _cmbCarScale.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (var scale in RaceCoach.CarScales)
        {
            _cmbCarScale.Items.Add(scale.Name);
        }

        _cmbCarScale.SelectedIndex = RaceCoach.CarScales.ToList().IndexOf(RaceCoach.ScaleOf(Result.CarScale));

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
        fields.AddRow("Car scale:", _cmbCarScale);
        fields.AddRow("Average lap time (sec):", _numAvgLapSeconds);
        // The short-track warning that used to take this note's place under
        // 8 seconds was taken out on 13 Nov 2026: the announcer keeps up on
        // short laps now (Asher).
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
        (Font Font, int Count, int Width)? scaleWidth = null;
        _cmbCarScale.Width = Math.Max(NumberBoxWidth(_numTrackLength), Pieces.PickerWidth(_cmbCarScale, ref scaleWidth));
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
            AppDialog.Show(this,
                _pendingAttachPath is null ? $"Couldn't remove the photo: {ex.Message}" : $"Couldn't attach that file: {ex.Message}",
                _pendingAttachPath is null ? "Remove failed" : "Attach failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        Result.CarScale = _cmbCarScale.SelectedIndex >= 0 ? RaceCoach.CarScales[_cmbCarScale.SelectedIndex].Key : null;
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
/// The club's track layouts (13 Nov 2026, the rework Asher picked from four
/// mock-ups - B, "Plan Stage", with D's "racing on tonight"): a thin list of
/// layouts down the left, each with a picture of its plan, and the one picked
/// filling the rest of the page - its plan as large as the card allows, the
/// commands for it on the bar above the plan, and what is known about it in
/// a strip of facts under the plan.
///
/// The layout the open race day races on wears "Racing on tonight", and any
/// other can be made tonight's with Use Tonight - which sets the race day's
/// track, the one Race Control's Track box follows.
///
/// It used to be a table of four columns, mostly "(none yet)" and "-", with
/// Delete and Reset Record as loud as Add beside Edit, and the plan alone in
/// a tall card of its own. Reset Record and Delete are under More now.
/// </summary>
public sealed class TrackLayoutManagerControl : UserControl
{
    private readonly TrackLayoutStore _store;
    private readonly LayoutList _list = new();
    private readonly Label _lblPreviewName = new();
    private readonly Label _lblTonight = new();
    private readonly PictureBox _picPreview = new();
    private readonly Label _lblPreviewEmpty = new();
    private readonly Button _btnDrawPlan = new() { Text = "Draw It in Track Designer..." };
    private readonly Button _btnDesign = new() { Text = "Open in Track Designer..." };
    private readonly Button _btnUseTonight = new() { Text = "Use Tonight" };
    private readonly Button _btnEdit = new() { Text = "Edit Details..." };
    private readonly Button _btnMore = new() { Text = "More ▾" };

    /// <summary>The commands for the layout picked, on the bar above the plan - hidden, bar and all, with nothing picked.</summary>
    private readonly ButtonBar _commands = new(alignRight: true);
    private readonly ReadinessStrip _facts = new();
    private readonly Label _lblMore = new();
    private readonly AppSettings _settings;
    private readonly UndoService _undo;

    /// <summary>The race days, for which layout is tonight's (Use Tonight). Null in a host without them.</summary>
    private readonly RaceEventStore? _events;

    /// <summary>The rounds, so Use Tonight moves tonight's unrun ones too (bug hunt 21). Null in a host without them.</summary>
    private readonly RoundDefinitionStore? _rounds;

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The list's column at the normal text size.</summary>
    private const int ListWidth = 290;

    /// <summary>The least the picture gets at the normal text size - below that the page scrolls.</summary>
    private const int PictureLeastHeight = 200;

    /// <summary>The club's saved races, for the designer's lap time (Track Designer overhaul, Phase 6). Null in a host without them.</summary>
    private readonly SessionHistoryStore? _sessions;

    public TrackLayoutManagerControl(TrackLayoutStore store, AppSettings settings, UndoService undo, SessionHistoryStore? sessions = null,
        RaceEventStore? events = null, RoundDefinitionStore? rounds = null)
    {
        _sessions = sessions;
        _store = store;
        _undo = undo;
        _settings = settings;
        _events = events;
        _rounds = rounds;

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize. A short window scrolls the page (FillPage).
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshList();

        // Tonight's track can change on Setup, or a day be started, while
        // this page is up.
        if (_events is not null)
        {
            _events.CurrentChanged += OnDayChanged;
            _events.CurrentEdited += OnDayChanged;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_events is not null)
            {
                _events.CurrentChanged -= OnDayChanged;
                _events.CurrentEdited -= OnDayChanged;
            }

            _picPreview.Image?.Dispose();
            _list.ClearPictures();
        }

        base.Dispose(disposing);
    }

    /// <summary>The race day changed: which layout is tonight's may have. Posted - the store says nothing about its thread.</summary>
    private void OnDayChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(new Action(() =>
            {
                if (!IsDisposed)
                {
                    RefreshList();
                }
            }));
        }
        catch (InvalidOperationException)
        {
            // The window went between the check and the post.
        }
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

    /// <summary>The open race day - tonight - or null when none is open.</summary>
    private RaceEvent? Tonight => _events?.Active is { IsLegacy: false, IsOpen: true } day ? day : null;

    /// <summary>Whether a layout is the one the open race day races on.</summary>
    private bool IsTonights(TrackLayout track) =>
        Tonight is { } day && string.Equals(day.TrackLayoutName?.Trim(), track.Name.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>The layout picked in the list, by id, or null.</summary>
    private string? SelectedId => _list.SelectedItem is LayoutList.Entry entry ? entry.Id : null;

    /// <summary>For the tests: the layouts listed, the one picked, and what its stage says.</summary>
    internal (IReadOnlyList<string> Listed, string? Picked, string Tonight, IReadOnlyList<ReadinessStrip.Tile> Facts) Shows =>
        (_list.Items.Cast<LayoutList.Entry>().Select(e => e.Name).ToList(),
         SelectedId is { } id ? _store.Get(id)?.Name : null,
         _lblTonight.Visible ? _lblTonight.Text : string.Empty,
         _facts.Tiles);

    private void BuildLayout()
    {
        // --- The layouts ---
        _list.SelectedIndexChanged += (_, _) =>
        {
            RefreshPreview();

            if (SelectedId is { } id && _store.Get(id) is { } track)
            {
                _settings.LastViewedTrackLayoutName = track.Name;
                _settings.Save();
            }
        };
        _list.DoubleClick += (_, _) => EditSelected();
        _textSize.Keep(_list.Remeasure);

        // New Layout: a menu, because most layouts start in the designer and
        // some are only a name and a photo.
        var btnNew = new Button { Text = "+ New Layout" };
        var newMenu = new ContextMenuStrip();
        var draw = new ToolStripMenuItem("Draw It in Track Designer...");
        draw.Click += (_, _) => DesignNew();
        var named = new ToolStripMenuItem("Name and Photo Only...");
        named.Click += (_, _) => AddNew();
        newMenu.Items.Add(draw);
        newMenu.Items.Add(named);
        AppTheme.StyleContextMenu(newMenu);
        btnNew.Click += (_, _) => newMenu.Show(btnNew, new Point(0, btnNew.Height));
        AppTheme.StyleButton(btnNew, AppTheme.ButtonKind.Primary);

        var layouts = new Section("Track layouts");

        layouts.Add(new ButtonBar().With(btnNew));
        layouts.AddFill(_list, () => 3 * _list.ItemHeight, gapBelow: 0);

        // --- The selected track ---
        _lblPreviewName.AutoSize = true;
        _lblPreviewName.Padding = new Padding(0, UiLayout.AtDpi(2), UiLayout.AtDpi(8), 0);
        _textSize.Wear(_lblPreviewName, () => AppTheme.TitleFont);

        // "Racing on tonight · Friday Club Night" - D's tag, beside the name.
        _lblTonight.AutoSize = true;
        _lblTonight.ForeColor = AppTheme.Success;
        _lblTonight.Padding = new Padding(0, UiLayout.AtDpi(11), 0, 0);
        _lblTonight.Visible = false;
        _textSize.Wear(_lblTonight, () => AppTheme.MicroFont);

        _btnDesign.Click += (_, _) => DesignSelected();
        _btnDrawPlan.Click += (_, _) => DesignSelected();
        _btnUseTonight.Click += (_, _) => UseTonight();
        _btnEdit.Click += (_, _) => EditSelected();
        _btnMore.Click += (_, _) => ShowMoreMenu();
        AppTheme.StyleButton(_btnDesign, AppTheme.ButtonKind.Primary);
        AppTheme.StyleButton(_btnDrawPlan, AppTheme.ButtonKind.Primary);

        // The name on the left, the commands for it on the right, the
        // designer furthest right.
        var title = new ButtonBar().With(_lblPreviewName, _lblTonight);
        var commands = _commands;
        commands.With(_btnDesign, _btnUseTonight, _btnEdit, _btnMore);

        _picPreview.BorderStyle = BorderStyle.None;
        _picPreview.SizeMode = PictureBoxSizeMode.Zoom;
        _picPreview.BackColor = AppTheme.Surface;
        _picPreview.Visible = false;

        // Shown when there is no picture, over the room the picture keeps -
        // which stays, so the card does not change shape as the selection
        // does.
        _lblPreviewEmpty.Text = "Pick a track layout to see it here.";
        _lblPreviewEmpty.AutoSize = true;
        _lblPreviewEmpty.ForeColor = AppTheme.TextMuted;
        _btnDrawPlan.Visible = false;

        // What the facts strip cannot fit: the posts' names, the sector
        // records and the notes, in a line or two under it.
        _lblMore.AutoSize = true;
        _lblMore.ForeColor = AppTheme.TextMuted;
        _textSize.Wear(_lblMore, () => AppTheme.SmallFont);

        _textSize.Keep(_facts.Remeasure);
        _facts.TileClicked += key =>
        {
            if (key == "pieces")
            {
                DesignSelected();
            }
            else if (key != "record")
            {
                EditSelected();
            }
        };

        var preview = new Section("Selected track");

        preview.Add(new SplitBar(title, commands));
        preview.Add(_lblPreviewEmpty, gapBelow: UiLayout.HintGap);
        preview.Add(new ButtonBar().With(_btnDrawPlan));
        preview.AddFill(_picPreview, () => UiLayout.AtTextSize(PictureLeastHeight));
        preview.Add(_facts, gapBelow: UiLayout.HintGap);
        preview.Add(_lblMore, gapBelow: 0);

        // --- The page ---
        var band = new Beside(() => UiLayout.AtTextSize(ListWidth)) { SideOnLeft = true };

        band.AddFill(preview, preview.LeastHeight);
        band.AddSideFill(layouts, layouts.LeastHeight, gapBelow: 0);

        var page = new FillPage();

        page.AddFill(band, band.LeastHeight, gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>Called when this page becomes visible - layouts can gain records from Race Control while it's inactive.</summary>
    public void RefreshAll() => RefreshList();

    private void RefreshList()
    {
        var previouslySelectedId = SelectedId;
        var entries = new List<LayoutList.Entry>();

        foreach (var t in _store.All)
        {
            var bits = new List<string>();

            if (t.Design is { IsEmpty: false } design)
            {
                bits.Add($"{design.Tiles.Count} tiles");
            }
            else
            {
                bits.Add("No plan");
            }

            bits.Add(t.RecordLapMs > 0 ? $"{t.RecordLapMs / 1000.0:0.000}s" : "no record yet");

            entries.Add(new LayoutList.Entry(t.Id, t.Name, string.Join(" · ", bits), IsTonights(t), t));
        }

        _list.SetEntries(entries);

        // The layout picked before; else the one looked at last time; else
        // tonight's; else the first.
        var pick = entries.FindIndex(e => e.Id == previouslySelectedId);

        if (pick < 0)
        {
            pick = entries.FindIndex(e => string.Equals(e.Name, _settings.LastViewedTrackLayoutName, StringComparison.Ordinal));
        }

        if (pick < 0)
        {
            pick = entries.FindIndex(e => e.Tonight);
        }

        if (pick < 0 && entries.Count > 0)
        {
            pick = 0;
        }

        if (_list.SelectedIndex != pick)
        {
            _list.SelectedIndex = pick;
        }

        RefreshPreview();
    }

    /// <summary>
    /// The stage for the layout picked: its name and whether it is tonight's,
    /// its picture, and its facts - updated live as the selection changes.
    /// </summary>
    private void RefreshPreview()
    {
        _picPreview.Image?.Dispose();
        _picPreview.Image = null;

        var track = SelectedId is { } id ? _store.Get(id) : null;

        foreach (var command in new Control[] { _btnDesign, _btnEdit, _btnMore })
        {
            command.Visible = track is not null;
        }

        _commands.Visible = track is not null;

        if (track is null)
        {
            _lblPreviewName.Text = _store.All.Count == 0 ? "No layouts yet" : string.Empty;
            _lblTonight.Visible = false;
            _btnUseTonight.Visible = false;
            _picPreview.Visible = false;
            _btnDrawPlan.Visible = _store.All.Count == 0;
            _btnDrawPlan.Text = "Draw One in Track Designer...";
            _lblPreviewEmpty.Text = _store.All.Count == 0
                ? "Draw your first layout tile by tile, or start from one of the built-in RCP layouts."
                : "Pick a track layout to see it here.";
            _lblPreviewEmpty.Visible = true;
            _facts.SetTiles(Array.Empty<ReadinessStrip.Tile>());
            _facts.Visible = false;
            _lblMore.Text = string.Empty;
            return;
        }

        _lblPreviewName.Text = track.Name;

        // Tonight's, or a button to make it so - only with a race day open.
        var tonight = Tonight;
        var isTonights = IsTonights(track);

        _lblTonight.Text = AppTheme.Spaced("● RACING TONIGHT");
        _lblTonight.Visible = isTonights;
        _btnUseTonight.Visible = tonight is not null && !isTonights;

        var hasPlan = track.Design is { IsEmpty: false };
        _btnDesign.Text = hasPlan ? "Open in Track Designer..." : "Draw in Track Designer...";

        // Whichever picture this track is set to show - the drawn plan or an
        // uploaded photo - with a fallback to the other when there is only
        // one of them.
        var image = TrackImagery.MainPicture(
            track,
            new Size(Math.Max(240, _picPreview.Width * 2), Math.Max(160, _picPreview.Height * 2)),
            AppTheme.Surface);

        if (image is not null)
        {
            _picPreview.Image = image;
            _picPreview.Visible = true;
            _lblPreviewEmpty.Visible = false;
            _btnDrawPlan.Visible = false;
        }
        else
        {
            _picPreview.Visible = false;
            _lblPreviewEmpty.Text = string.IsNullOrWhiteSpace(track.AttachmentFileName)
                ? "No plan or photo yet. Draw the layout tile by tile, or attach a photo in Edit Details."
                : $"Attachment \"{track.AttachmentFileName}\" isn't an image that can be previewed here.";
            _lblPreviewEmpty.Visible = true;
            // The bar above already says "Draw in Track Designer".
            _btnDrawPlan.Visible = false;
        }

        _facts.SetTiles(FactsFor(track));
        _facts.Visible = true;
        _lblMore.Text = MoreAbout(track);
    }

    /// <summary>The strip under the plan: the lap record, the average lap, the length, the marshal posts and the pieces.</summary>
    internal List<ReadinessStrip.Tile> FactsFor(TrackLayout track)
    {
        var tiles = new List<ReadinessStrip.Tile>
        {
            track.RecordLapMs > 0
                ? new("record", "Lap record", $"{track.RecordLapMs / 1000.0:0.000}s",
                    string.Join(" · ", new[] { track.RecordHolderName, track.RecordSetUtc?.ToLocalTime().ToString("d MMM") }
                        .Where(s => !string.IsNullOrWhiteSpace(s))),
                    null, AppTheme.Warning, Stands: true)
                : new("record", "Lap record", "None yet", string.Empty, null, AppTheme.Warning),

            new("lap", "Average lap", track.AverageLapSeconds is { } lap ? $"{lap:0.0}s" : "—",
                track.AverageLapSeconds is null ? "not set" : string.Empty, null, AppTheme.Accent),

            new("length", "Length", track.TrackLengthFeet is { } feet ? $"{feet:0} ft" : "—",
                track.TrackLengthFeet is null ? "not set" : $"{RaceCoach.ScaleOf(track.CarScale).Key} cars", null, AppTheme.Accent),

            new("posts", "Marshal posts", track.MarshalPostCount > 0 ? track.MarshalPostCount.ToString() : "None",
                track.MarshalPostCount > 0 ? string.Empty : "not tracked", null, AppTheme.Accent),
        };

        if (track.Design is { IsEmpty: false } design)
        {
            var bounds = design.BoundsInTiles(includeRoute: false);
            var size = bounds.Width > 0 ? $"{Math.Ceiling(bounds.Width):0} × {Math.Ceiling(bounds.Height):0}" : string.Empty;
            var shortfall = PiecesShort(design);

            tiles.Add(new("pieces", "Pieces", $"{design.Tiles.Count} tiles",
                shortfall ?? size, null, shortfall is null ? AppTheme.Accent : AppTheme.Warning, Stands: shortfall is not null));
        }
        else
        {
            tiles.Add(new("pieces", "Pieces", "No plan", string.Empty, null, AppTheme.Accent));
        }

        return tiles;
    }

    /// <summary>"2 rail tiles short" - what the plan needs that the club doesn't own - or null when it owns enough, or hasn't said what it owns.</summary>
    private string? PiecesShort(RcpTrackDesign design)
    {
        try
        {
            var lines = _settings.TrackStock?.Check(RcpPlan.Count(design), design.TileSizeMm) ?? Array.Empty<RcpStockLine>();
            var shortLines = lines.Where(line => line.Known && line.Short).ToList();

            return shortLines.Count switch
            {
                0 => null,
                1 => $"{-shortLines[0].Left} {shortLines[0].Part.ToLowerInvariant()} short",
                _ => $"{shortLines.Count} kinds of piece short",
            };
        }
        catch (Exception)
        {
            // A stock list the check can't read says nothing, rather than
            // taking the page down.
            return null;
        }
    }

    /// <summary>The marshal posts by name, the sector records and the notes - a line each, when there are any.</summary>
    private static string MoreAbout(TrackLayout track)
    {
        var lines = new List<string>();

        if (track.MarshalPostCount > 0)
        {
            lines.Add("Marshal posts: " + string.Join(" · ",
                Enumerable.Range(0, track.MarshalPostCount).Select(i => $"{i + 1} {track.PostName(i)}")));
        }

        if (track.HasSectorRecords())
        {
            var records = track.CurrentSectorRecords().Where(r => r is not null).Select(r => r!).ToList();

            if (records.Count > 0)
            {
                lines.Add("Sector records: " + string.Join(" · ",
                    records.Select(r => $"S{r.Sector} {Sectors.Format(r.Ms)} {r.Holder}".Trim())));
            }
        }

        if (!string.IsNullOrWhiteSpace(track.Notes))
        {
            lines.Add("Notes: " + track.Notes.Trim().ReplaceLineEndings(" "));
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The More menu: the two commands that take something away, each asking first.</summary>
    private void ShowMoreMenu()
    {
        if (SelectedId is not { } id || _store.Get(id) is not { } track)
        {
            return;
        }

        var menu = new ContextMenuStrip();

        var reset = new ToolStripMenuItem("Reset Record...")
        {
            Enabled = track.RecordLapMs > 0 || track.HasSectorRecords(),
            ForeColor = AppTheme.Danger,
        };
        reset.Click += (_, _) => ResetSelectedRecord();

        var delete = new ToolStripMenuItem("Delete Layout...") { ForeColor = AppTheme.Danger };
        delete.Click += (_, _) => DeleteSelected();

        menu.Items.Add(reset);
        menu.Items.Add(delete);

        AppTheme.StyleContextMenu(menu);
        menu.Closed += (_, _) => BeginInvoke(new Action(menu.Dispose));
        menu.Show(_btnMore, new Point(_btnMore.Width - menu.PreferredSize.Width, _btnMore.Height));
    }

    /// <summary>
    /// Makes the layout picked tonight's: the open race day's track, which
    /// Race Control's Track box follows (RaceEventStore.CurrentEdited), and
    /// which records, pace and marshal posts are filed under. One undo puts
    /// the day's track back.
    /// </summary>
    internal void UseTonight()
    {
        if (_events is null || Tonight is not { } day || SelectedId is not { } id || _store.Get(id) is not { } track)
        {
            return;
        }

        var before = day.TrackLayoutName;
        var dayId = day.Id;
        var events = _events;
        var rounds = _rounds;

        // Tonight's rounds not yet run go to the new layout too (bug hunt 21,
        // 15 Nov 2026). A round keeps the layout it was built on, and Race
        // Control's Track box locks to a picked round's own layout - so the
        // page said RACING TONIGHT on the new one while every heat left was
        // timed, filed and marshalled on the old. Rounds already run stay
        // where they were raced. Each one's old layout is kept for the undo.
        var moved = UnrunRoundsOf(day, rounds, track.Name)
            .Select(r => (r.Id, Was: r.TrackLayoutName))
            .ToList();

        _undo.Capture($"Race on '{track.Name}' tonight", () =>
        {
            // Moved back only while still unrun and still on this layout:
            // one raced since, or moved again, is left as it is now.
            foreach (var (roundId, was) in moved)
            {
                if (rounds?.Get(roundId) is { IsRecorded: false } round
                    && string.Equals(round.TrackLayoutName, track.Name, StringComparison.Ordinal))
                {
                    round.TrackLayoutName = was;
                    rounds.Update(round);
                }
            }

            if (events.Get(dayId) is { } target)
            {
                target.TrackLayoutName = before;
                events.Upsert(target);
            }
        });

        // The rounds first: saving the day tells Race Control's Track box,
        // which then reads the picked round's layout (MainForm.SyncTrackLayoutToRaceDay).
        foreach (var (roundId, _) in moved)
        {
            if (rounds?.Get(roundId) is { } round)
            {
                round.TrackLayoutName = track.Name;
                rounds.Update(round);
            }
        }

        day.TrackLayoutName = track.Name;
        _events.Upsert(day);
        RefreshList();
    }

    /// <summary>The day's rounds that haven't been run and aren't on <paramref name="layout"/> already - what Use Tonight moves. Pure but for the store read.</summary>
    internal static IReadOnlyList<RoundDefinition> UnrunRoundsOf(RaceEvent day, RoundDefinitionStore? rounds, string layout)
    {
        if (rounds is null)
        {
            return Array.Empty<RoundDefinition>();
        }

        return (day.RoundIds ?? new List<string>())
            .Select(rounds.Get)
            .OfType<RoundDefinition>()
            .Where(r => !r.IsRecorded && !string.Equals(r.TrackLayoutName, layout, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>What a layout rename moved onto the new name tonight: whether the open day moved, and which of its unrun rounds did - for the undo.</summary>
    internal sealed record RenameCarry(bool DayMoved, IReadOnlyList<string> RoundIds)
    {
        public static readonly RenameCarry None = new(false, Array.Empty<string>());
    }

    /// <summary>
    /// Carries a layout's new name onto the open race day and its unrun
    /// rounds that name the old one. Pure but for the store writes.
    /// </summary>
    /// <remarks>
    /// WHY (23 Nov 2026, bug hunt 24): renaming a layout only renamed the
    /// layout. The open day and every heat left to run still named the old
    /// name, and they are looked up by name - so with nothing said the page
    /// dropped RACING TONIGHT from the layout being raced, the marshal rota
    /// for the rest of the night came back empty (no layout of that name, so
    /// no posts), and picking a round no longer moved the Track box. Rounds
    /// already run keep the old name: saved races are matched by name, and
    /// that history starting again after a rename is the documented limit.
    /// The rounds go first, as Use Tonight does, so saving the day tells the
    /// Track box after the round it reads has its new name.
    /// </remarks>
    internal static RenameCarry CarryRenameToTonight(RaceEvent? day, RaceEventStore? events, RoundDefinitionStore? rounds, string oldName, string newName)
    {
        var was = (oldName ?? string.Empty).Trim();
        var now = (newName ?? string.Empty).Trim();

        // Ordinal, so a fix of the name's capitals carries too.
        if (day is null || events is null || was.Length == 0 || now.Length == 0 || string.Equals(was, now, StringComparison.Ordinal))
        {
            return RenameCarry.None;
        }

        bool OnOld(string? name) => string.Equals(name?.Trim(), was, StringComparison.OrdinalIgnoreCase);

        var moved = new List<string>();
        if (rounds is not null)
        {
            foreach (var round in (day.RoundIds ?? new List<string>()).Select(rounds.Get).OfType<RoundDefinition>().ToList())
            {
                if (!round.IsRecorded && OnOld(round.TrackLayoutName))
                {
                    round.TrackLayoutName = now;
                    rounds.Update(round);
                    moved.Add(round.Id);
                }
            }
        }

        var dayMoved = OnOld(day.TrackLayoutName);
        if (dayMoved)
        {
            day.TrackLayoutName = now;
            events.Upsert(day);
        }

        return dayMoved || moved.Count > 0 ? new RenameCarry(dayMoved, moved) : RenameCarry.None;
    }

    /// <summary>
    /// The undo of <see cref="CarryRenameToTonight"/>: the rounds it moved
    /// and the day go back to the old name - each only while it still names
    /// the new one (and a round only while still unrun), as Use Tonight's
    /// undo does.
    /// </summary>
    internal static void UndoRenameCarry(RenameCarry carry, string? dayId, RaceEventStore? events, RoundDefinitionStore? rounds, string oldName, string newName)
    {
        var was = (oldName ?? string.Empty).Trim();
        var now = (newName ?? string.Empty).Trim();

        foreach (var roundId in carry.RoundIds)
        {
            if (rounds?.Get(roundId) is { IsRecorded: false } round
                && string.Equals(round.TrackLayoutName, now, StringComparison.Ordinal))
            {
                round.TrackLayoutName = was;
                rounds.Update(round);
            }
        }

        if (carry.DayMoved && events?.Get(dayId) is { } day
            && string.Equals(day.TrackLayoutName, now, StringComparison.Ordinal))
        {
            day.TrackLayoutName = was;
            events.Upsert(day);
        }
    }

    /// <summary>The designer on a blank floor, for a new layout - New Layout's first choice.</summary>
    private void DesignNew()
    {
        _list.SelectedIndex = -1;
        DesignSelected();

        // Cancelled: the stage goes back to a layout.
        if (_list.SelectedIndex < 0)
        {
            RefreshList();
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
        if (SelectedId is not { } id)
        {
            return;
        }

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
            existing.CarScale,
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

            // WHY (23 Nov 2026, bug hunt 24): a rename is carried onto
            // tonight's day and unrun heats (see CarryRenameToTonight) - filled
            // in after the save below, before the undo can ever run.
            var events = _events;
            var rounds = _rounds;
            var newName = form.Result.Name;
            var dayId = Tonight?.Id;
            var carry = RenameCarry.None;

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
                    else if (before.AttachmentFileName is null)
                    {
                        // There was no photo before: the one the edit attached
                        // goes aside (the undo holding folder clears it later).
                        // Only then - with a name before and nothing set
                        // aside, the folder there now is that photo's own.
                        TrackAttachmentStore.StashForTrack(trackId);
                    }

                    target.AttachmentFileName = before.AttachmentFileName;
                }

                target.Name = before.Name;
                target.Notes = before.Notes;
                target.TrackLengthFeet = before.TrackLengthFeet;
                target.CarScale = before.CarScale;
                target.AverageLapSeconds = before.AverageLapSeconds;
                target.MarshalPostCount = before.MarshalPostCount;
                target.PreferPlanImage = before.PreferPlanImage;
                store.Upsert(target);

                UndoRenameCarry(carry, dayId, events, rounds, before.Name, newName);
            });

            _store.Upsert(form.Result);
            carry = CarryRenameToTonight(Tonight, _events, _rounds, before.Name, form.Result.Name);
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
        var existing = SelectedId is { } selected ? _store.Get(selected) : null;

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

        // The lap the designer shows (Phase 6): this layout's laps raced
        // since its line last changed, and the estimate tuned against every
        // layout the club has both drawn and raced.
        var raced = _sessions?.Saved ?? Array.Empty<SessionRecord>();
        var racedHere = existing is null ? null : RcpLapModel.Raced(raced, existing.Name, existing.Design?.RouteChangedUtc);
        var (tuning, tunedLayouts) = RcpLapModel.Calibrate(_store.All
            .Where(t => t.Design is { Route.Count: >= 3, RouteClosed: true })
            .Select(t => (Design: t.Design!, Lap: RcpLapModel.Raced(raced, t.Name, t.Design!.RouteChangedUtc)))
            .Where(pair => pair.Lap is not null)
            .Select(pair => (pair.Design, pair.Lap!.Value.Seconds)));

        while (true)
        {
            using var form = new RcpTrackEditorForm(
                designToOpen,
                nameToOpen,
                existing?.AverageLapSeconds,
                unsavedOnOpen: reopened,
                useAsPhoto: useAsPhotoToOpen,
                stock: _settings.TrackStock,
                saveStock: owned =>
                {
                    // What the club owns is the same for every layout, so
                    // it is kept with the club's settings, not the plan.
                    _settings.TrackStock = owned;
                    return _settings.Save();
                },
                raced: racedHere,
                racedLine: existing?.Design?.Route,
                tuning: tuning,
                tunedLayouts: tunedLayouts);

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

        // WHY (23 Nov 2026, bug hunt 24): a rename saved from the designer is
        // carried onto tonight's day and unrun heats too (see
        // CarryRenameToTonight) - filled in after the save below.
        var events = _events;
        var rounds = _rounds;
        var dayId = Tonight?.Id;
        var carry = RenameCarry.None;

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

                UndoRenameCarry(carry, dayId, events, rounds, previousName, chosenName);
            }
        });

        // A racing line saved changed: the laps raced on the old one are for
        // a different track, so from now the designer counts only races
        // after this (RcpLapModel.Raced). A first line isn't a change.
        if (previousDesign is { Route.Count: > 0 } && !RcpLapModel.SameLine(previousDesign.Route, chosenDesign.Route, previousDesign.RouteClosed && chosenDesign.RouteClosed))
        {
            chosenDesign.RouteChangedUtc = DateTime.UtcNow;
        }

        track.Name = chosenName;
        track.Design = chosenDesign;

        // A preference, not a replacement: whatever photo was attached is
        // still attached, and can be switched back to at any time.
        track.PreferPlanImage = useAsPhoto && !chosenDesign.IsEmpty;

        _store.Upsert(track);

        // A new layout has no old name for tonight to be on.
        if (!isNew)
        {
            carry = CarryRenameToTonight(Tonight, _events, _rounds, previousName, chosenName);
        }

        _settings.LastViewedTrackLayoutName = track.Name;
        _settings.Save();

        RefreshList();
    }

    private void DeleteSelected()
    {
        if (SelectedId is not { } id || _store.Get(id) is not { } shown)
        {
            return;
        }

        var name = shown.Name;
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
        if (SelectedId is not { } id)
        {
            return;
        }

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
