namespace HeatCaller;

/// <summary>
/// Manage sponsor/promo slides shown in rotation on the Big Screen, how
/// long each stays up - and the sponsors the announcer reads out, with the
/// sales copy each of them paid for.
///
/// The two halves are one page on purpose. A club's sponsors are one
/// arrangement: the same shop that gave them a banner for the screen gave
/// them the words to read, and setting them up is one sitting. See
/// BuildSponsorReadsCard.
/// </summary>
public sealed class SponsorsControl : UserControl
{
    private readonly SponsorStore _store;
    private readonly UndoService _undo;

    /// <summary>
    /// How many reads each sponsor has had tonight - wire this to the
    /// director's own rotation (CommentaryDirector.Sponsors.ReadsTonight).
    ///
    /// A READER rather than a figure pushed in, like every other count in
    /// this app: the night moves while this page is open, and a club that
    /// presses the report button after the last race must get the last
    /// race's reads in it.
    ///
    /// Null until wired, and then the report says no reads went out -
    /// which is true of a club whose announcer has never run.
    /// </summary>
    internal Func<IReadOnlyDictionary<string, int>>? ReadsTonight { get; set; }

    /// <summary>
    /// The race day as the club named it, for the top of the report -
    /// "Club Night 14". Null is fine; the report then has no heading of
    /// its own, which is right for a club that files nothing against an
    /// event.
    /// </summary>
    internal Func<string?>? EventName { get; set; }

    /// <summary>
    /// Tonight's "presented by" mentions per sponsor id and moment, for the
    /// report - the announcer's own count (AnnouncementService.SponsorMentions),
    /// wired by the host. Null leaves them out of the report.
    /// </summary>
    internal Func<IReadOnlyDictionary<string, IReadOnlyDictionary<SponsoredMoment, int>>>? MentionsTonight { get; set; }

    private readonly Button _btnReport = new();

    private readonly CheckBox _chkEnabled = new ThemedCheckBox();
    private readonly ListBox _lstSlides = new();
    private readonly PictureBox _picPreview = new();
    private readonly NumericUpDown _numLeaderboardSeconds = new();
    private readonly NumericUpDown _numSlideSeconds = new();
    private readonly CheckBox _chkPauseDuringRace = new ThemedCheckBox();
    private readonly CheckBox _chkPlayVideoSound = new ThemedCheckBox();
    private readonly CheckBox _chkMuteMusicDuringVideo = new ThemedCheckBox();

    // --- The sponsors themselves, and the copy they paid to have read ---

    private readonly CheckBox _chkReadInAdBreaks = new ThemedCheckBox();
    private readonly CheckBox _chkReadInSegments = new ThemedCheckBox();
    private readonly ListBox _lstSponsors = new();
    private readonly TextBox _txtSponsorName = new();
    private readonly TextBox _txtSponsorTagline = new();
    private readonly TextBox _txtSponsorCopy = new();
    private readonly NumericUpDown _numSponsorWeight = new();
    private readonly CheckBox _chkSponsorEnabled = new ThemedCheckBox();

    /// <summary>The moments of the race the selected sponsor presents - one row per SponsoredMoment, in its order. See SponsoredMoments.</summary>
    private readonly CheckedListBox _lstSponsorMoments = new ThemedCheckedListBox();

    /// <summary>Says which moments are ticked for more than one sponsor, and who actually gets them. Empty when none are.</summary>
    private readonly Label _lblMomentClashes = new();

    /// <summary>How long the selected sponsor's copy takes to read, under the copy box.</summary>
    private readonly Label _lblCopyLength = new();

    /// <summary>
    /// Set while the fields are being filled in from the selected sponsor,
    /// so the handlers that save what is typed do not save it straight back
    /// over itself - and, worse, do not write the sponsor that was selected
    /// a moment ago into the one selected now. The pattern every edit-beside
    /// -a-list page in the app needs and the reason this one needed a flag
    /// at all.
    /// </summary>
    private bool _fillingSponsor;

    /// <summary>
    /// Sits over the preview and says what a video slide is, since there is
    /// nothing to decode into the picture box. A child of the picture box
    /// rather than a row of its own, so the card's layout is the same
    /// whichever kind of slide is selected.
    /// </summary>
    private readonly Label _lblVideoNote = new();

    /// <summary>Plays the selected video in a window of its own - enabled only while a video slide is selected. See SponsorVideoPreviewForm.</summary>
    private readonly Button _btnPreviewVideo = new() { Text = "Preview Video" };

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The slides and the preview side by side - the list's column is worked out from its width. See ListColumnWidth.</summary>
    private Beside? _slidesBand;

    /// <summary>The list's column, the narrowest it gets, and the preview's least height, at the normal text size.</summary>
    private const int ListWidth = 320;
    private const int ListLeastWidth = 160;
    private const int PreviewLeastHeight = 160;

    /// <summary>The fewest slides the list shows before the page scrolls instead.</summary>
    private const int ListLeastRows = 4;

    /// <summary>
    /// The sponsor fields' widths and heights at the normal text size -
    /// scaled with it in SizeFields, never set in pixels at the size the
    /// page happens to be running at.
    /// </summary>
    private const int SponsorListWidth = 260;
    private const int SponsorNameWidth = 300;
    /// <summary>
    /// The picture the crowd screen shows while this sponsor's read is
    /// being said (12 Oct 2026, asked for 10 Oct).
    ///
    /// A DROP-DOWN OF WHAT THE CLUB ALREADY HAS, plus an Upload button, so
    /// the two ways a club would want to answer this are one row: point it
    /// at a slide they have already uploaded, or bring in a picture for
    /// the read. Upload puts the file in the same folder the rotation uses
    /// and so it appears here too - which is deliberate, because a picture
    /// stored anywhere else would be one a club could neither see nor
    /// delete.
    ///
    /// "None" is the ordinary answer and is not nothing: the Big Screen
    /// shows the sponsor's NAME for the length of the read. See
    /// SponsorOnAir.
    /// </summary>
    private readonly ComboBox _cmbSponsorPicture = new();

    private readonly Button _btnSponsorPicture = new();

    /// <summary>What "none" reads as in the drop-down - it says what happens, not that nothing does.</summary>
    private const string NoPictureItem = "None - show their name";

    /// <summary>
    /// How wide the picture drop-down is at the normal text size.
    ///
    /// Three hundred, like the name box beside it, and it has to be SET:
    /// left to itself a ComboBox is 121 pixels, and "None - show their
    /// name" needs 202 at the normal text size and 372 at 200% - so the
    /// ordinary answer, the one every sponsor has until somebody uploads
    /// something, was the one the club could not read. Caught by
    /// LayoutTests at every size on 12 Oct 2026.
    ///
    /// Scaled through UiLayout.AtTextSize in SizeFields with every other
    /// field on this page, never set in the pixels the page happens to be
    /// running at.
    /// </summary>
    private const int SponsorPictureWidth = 300;

    private const int SalesCopyWidth = 440;

    /// <summary>How many sponsors the list shows, and how many lines of copy the box shows, before each scrolls.</summary>
    private const int SponsorRows = 5;
    private const int SalesCopyLines = 4;

    public SponsorsControl(SponsorStore store, UndoService undo)
    {
        _store = store;
        _undo = undo;

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize.
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshSlides();
        RefreshSponsors();

        // Undo changes the store, never this page - see OnStoreRestored.
        _store.Restored += OnStoreRestored;
    }

    /// <summary>Re-reads the slides and timing after an undo changed them.</summary>
    private void OnStoreRestored()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        // The boxes save what they show when they change - setting them to
        // what the store already holds writes the same values back, nothing
        // more.
        _chkEnabled.Checked = _store.Current.Enabled;
        _numLeaderboardSeconds.Value = Math.Clamp(_store.Current.LeaderboardSeconds, 5, 600);
        _numSlideSeconds.Value = Math.Clamp(_store.Current.SlideSeconds, 2, 120);
        _chkPauseDuringRace.Checked = _store.Current.PauseDuringRace;
        _chkPlayVideoSound.Checked = _store.Current.PlayVideoSound;
        _chkMuteMusicDuringVideo.Checked = _store.Current.MuteMusicDuringVideo;
        _chkReadInAdBreaks.Checked = _store.Current.ReadDuringAdBreaks;
        _chkReadInSegments.Checked = _store.Current.ReadDuringSegments;
        SyncVideoSoundEnabled();

        RefreshSlides();
        RefreshSponsors();
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _store.Restored -= OnStoreRestored;
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// The intro and the on/off switch, then the slides in a card that takes
    /// the page's height - the list down the left, the selected slide's
    /// preview beside it taking the rest, and the list's buttons under both -
    /// and the rotation's timing in a card under that. On the layout pieces
    /// (LayoutPieces.cs), so nothing is placed by hand.
    ///
    /// It used to be placed in pixels: the list 320x280 and the preview
    /// 360x280 whatever the window, the buttons stepped along from the list's
    /// left edge, and the timing in a group box widened by hand to whatever
    /// its measured row came to.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Show sponsor or promo images and videos in rotation on the Big Screen between leaderboard views - meant for a " +
                   "shop or club venue, so leave it off at home. A video plays to its own end rather than for the slide time below.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _chkEnabled.Text = "Show sponsor slides on the Big Screen";
        _chkEnabled.AutoSize = true;
        _chkEnabled.Checked = _store.Current.Enabled;
        _chkEnabled.CheckedChanged += (_, _) =>
        {
            _store.Current.Enabled = _chkEnabled.Checked;
            _store.Save();
        };

        // --- The slides ---

        _lstSlides.IntegralHeight = false;
        _lstSlides.SelectedIndexChanged += (_, _) => RefreshPreview();
        _lstSlides.DoubleClick += (_, _) => PreviewSelectedVideo();

        _picPreview.BorderStyle = BorderStyle.FixedSingle;
        _picPreview.SizeMode = PictureBoxSizeMode.Zoom;
        _picPreview.BackColor = AppTheme.Surface;

        _lblVideoNote.Dock = DockStyle.Fill;
        _lblVideoNote.TextAlign = ContentAlignment.MiddleCenter;
        _lblVideoNote.ForeColor = AppTheme.TextMuted;
        _lblVideoNote.Visible = false;
        _picPreview.Controls.Add(_lblVideoNote);
        _lblVideoNote.DoubleClick += (_, _) => PreviewSelectedVideo();
        _lblVideoNote.Cursor = Cursors.Hand;

        // Sized to their own captions by the bar they sit in.
        var btnAdd = new Button { Text = "Add Image..." };
        btnAdd.Click += (_, _) => AddSlide();

        var btnAddVideo = new Button { Text = "Add Video..." };
        btnAddVideo.Click += (_, _) => AddVideo();

        _btnPreviewVideo.Enabled = false;
        _btnPreviewVideo.Click += (_, _) => PreviewSelectedVideo();

        var btnRemove = new Button { Text = "Remove" };
        btnRemove.Click += (_, _) => RemoveSelected();

        var btnMoveUp = new Button { Text = "Move Up" };
        btnMoveUp.Click += (_, _) => MoveSelected(-1);

        var btnMoveDown = new Button { Text = "Move Down" };
        btnMoveDown.Click += (_, _) => MoveSelected(1);

        // The list down the left, as wide as it was at the normal text size
        // (see ListColumnWidth), and the preview taking the rest - a slide is
        // a picture made for a TV, so the room goes to seeing it.
        var band = new Beside(ListColumnWidth) { SideOnLeft = true };
        _slidesBand = band;

        band.AddFill(_picPreview, _ => UiLayout.AtTextSize(PreviewLeastHeight));
        band.AddSideFill(_lstSlides, _ => _lstSlides.ItemHeight * ListLeastRows + 4, gapBelow: 0);

        var slidesCard = new Section("Slides (shown in this order)");

        slidesCard.AddFill(band, () => band.LeastHeight(Math.Max(1, band.Width)));
        slidesCard.Add(new ButtonBar().With(btnAdd, btnAddVideo, _btnPreviewVideo, btnRemove, btnMoveUp, btnMoveDown), gapBelow: 0);

        // --- Timing ---

        _numLeaderboardSeconds.Minimum = 5;
        _numLeaderboardSeconds.Maximum = 600;
        _numLeaderboardSeconds.Value = Math.Clamp(_store.Current.LeaderboardSeconds, 5, 600);
        _numLeaderboardSeconds.ValueChanged += (_, _) =>
        {
            _store.Current.LeaderboardSeconds = (int)_numLeaderboardSeconds.Value;
            _store.Save();
        };

        _numSlideSeconds.Minimum = 2;
        _numSlideSeconds.Maximum = 120;
        _numSlideSeconds.Value = Math.Clamp(_store.Current.SlideSeconds, 2, 120);
        _numSlideSeconds.ValueChanged += (_, _) =>
        {
            _store.Current.SlideSeconds = (int)_numSlideSeconds.Value;
            _store.Save();
        };

        _chkPauseDuringRace.Text = "Never interrupt the leaderboard while a race is actually running (recommended)";
        _chkPauseDuringRace.AutoSize = true;
        _chkPauseDuringRace.Checked = _store.Current.PauseDuringRace;
        _chkPauseDuringRace.CheckedChanged += (_, _) =>
        {
            _store.Current.PauseDuringRace = _chkPauseDuringRace.Checked;
            _store.Save();
        };

        var btnTimingDefaults = new Button { Text = "Restore Defaults" };
        btnTimingDefaults.Click += (_, _) =>
        {
            // The three values as they were, for undo - put back on the
            // store, and the page follows through OnStoreRestored.
            var current = _store.Current;
            var leaderboardBefore = current.LeaderboardSeconds;
            var slideBefore = current.SlideSeconds;
            var pauseBefore = current.PauseDuringRace;

            if (leaderboardBefore != 60 || slideBefore != 8 || !pauseBefore)
            {
                var store = _store;
                _undo.Capture("Restore sponsor timing defaults", () =>
                {
                    store.Current.LeaderboardSeconds = leaderboardBefore;
                    store.Current.SlideSeconds = slideBefore;
                    store.Current.PauseDuringRace = pauseBefore;
                    store.Save();
                    store.NotifyRestored();
                });
            }

            // Matches the values a fresh install starts with.
            _numLeaderboardSeconds.Value = 60;
            _numSlideSeconds.Value = 8;
            _chkPauseDuringRace.Checked = true;
        };

        // The two numbers one under the other, their captions in a column of
        // their own. They used to share one row, each placed just after the
        // one before - four controls in a line that ran past its box at a
        // larger text size.
        var timing = new FieldGrid();

        timing.AddRow("Leaderboard shows for (sec):", _numLeaderboardSeconds);
        timing.AddRow("Each slide shows for (sec):", _numSlideSeconds);

        var timingCard = new Section("Rotation timing");

        timingCard.Add(timing);
        timingCard.Add(_chkPauseDuringRace, stretch: false);
        timingCard.Add(new ButtonBar().With(btnTimingDefaults), gapBelow: 0);

        // --- Video sound ---

        _chkPlayVideoSound.Text = "Play a sponsor video's own sound";
        _chkPlayVideoSound.AutoSize = true;
        _chkPlayVideoSound.Checked = _store.Current.PlayVideoSound;
        _chkPlayVideoSound.CheckedChanged += (_, _) =>
        {
            _store.Current.PlayVideoSound = _chkPlayVideoSound.Checked;
            _store.Save();
            SyncVideoSoundEnabled();
        };

        _chkMuteMusicDuringVideo.Text = "Silence the music and the crowd while it plays (recommended)";
        _chkMuteMusicDuringVideo.AutoSize = true;
        _chkMuteMusicDuringVideo.Checked = _store.Current.MuteMusicDuringVideo;
        _chkMuteMusicDuringVideo.CheckedChanged += (_, _) =>
        {
            _store.Current.MuteMusicDuringVideo = _chkMuteMusicDuringVideo.Checked;
            _store.Save();
        };

        var lblVideoSound = new Label
        {
            Text = "A sponsor who paid for a spot was promised nobody talks over it, so the music and crowd can fade out while "
                   + "their video's sound plays. The announcer still speaks - a race call always wins.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        var videoCard = new Section("Video sound");

        videoCard.Add(lblVideoSound, gapBelow: UiLayout.RowGap);
        videoCard.Add(_chkPlayVideoSound, stretch: false);
        videoCard.Add(_chkMuteMusicDuringVideo, stretch: false, gapBelow: 0);

        SyncVideoSoundEnabled();

        var readsCard = BuildSponsorReadsCard(out var readsGrid);

        // The number boxes at the text size in use - see SizeFields.
        _textSize.Keep(SizeFields);

        // --- The page ---

        var page = new FillPage();

        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.Add(_chkEnabled, stretch: false);
        page.AddFill(slidesCard, slidesCard.LeastHeight);

        // The rotation timing and the video's sound are Advanced (1 Nov
        // 2026, Simple mode); the slides and the reads are the sponsors.
        page.Add(SimpleMode.Hide(timingCard));
        page.Add(SimpleMode.Hide(videoCard));
        page.Add(readsCard, gapBelow: 0);
        page.Add(SimpleMode.MoreLine(), stretch: false, gapBelow: 0);

        Controls.Add(page);

        // (22 Oct 2026, page tidy): the timing and the sponsor reads each
        // had a caption column of their own, so their fields started at
        // different x. One column for both now - once the grids are on the
        // page and wear its face, which is what the captions are measured in.
        _textSize.Keep(() => FieldGrid.LineUp(new[] { timing, readsGrid }));
    }

    /// <summary>
    /// The sponsors the announcer reads out, and the copy each of them paid
    /// for.
    ///
    /// A card on this page rather than a window of its own, deliberately. A
    /// club setting their sponsors up does it once, for all of them, at the
    /// same sitting as they load the slides - and three of them in a dialog
    /// each is three times the clicking for no gain. It is also the list
    /// and the fields beside it that every other page in the app uses for
    /// this shape, so nothing new has to be learnt.
    ///
    /// Everything is placed by the layout pieces, in a FieldGrid, so the
    /// whole card follows the text size from 85% to 200% without a single
    /// pixel being worked out here - see SizeFields for the two things that
    /// do need a measurement.
    /// </summary>
    private Section BuildSponsorReadsCard(out FieldGrid grid)
    {
        var lblReadsIntro = new Label
        {
            Text = "A sponsor can have a read - a few sentences written to be spoken aloud - and the announcer reads it as written, "
                   + "in the gaps you allow below. It needs no AI: the words are yours. A race call always comes first, so a read "
                   + "waits for the next gap.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _chkReadInAdBreaks.Text = "Read at the end of an ad break, once the sponsor's video has finished";
        _chkReadInAdBreaks.AutoSize = true;
        _chkReadInAdBreaks.Checked = _store.Current.ReadDuringAdBreaks;
        _chkReadInAdBreaks.CheckedChanged += (_, _) =>
        {
            _store.Current.ReadDuringAdBreaks = _chkReadInAdBreaks.Checked;
            _store.Save();
        };

        // Worded as the extra it is (21 Oct 2026): the commercial break is
        // where reads belong, and this puts them on rotation between races
        // as well. See SponsorSettings.ReadDuringSegments.
        _chkReadInSegments.Text = "Also put reads on rotation between races, outside the commercial break";
        _chkReadInSegments.AutoSize = true;
        _chkReadInSegments.Checked = _store.Current.ReadDuringSegments;
        _chkReadInSegments.CheckedChanged += (_, _) =>
        {
            _store.Current.ReadDuringSegments = _chkReadInSegments.Checked;
            _store.Save();
        };

        _lstSponsors.IntegralHeight = false;
        _lstSponsors.SelectedIndexChanged += (_, _) => FillSponsorFields();

        var btnAddSponsor = new Button { Text = "Add Sponsor" };
        btnAddSponsor.Click += (_, _) => AddSponsor();

        var btnRemoveSponsor = new Button { Text = "Remove" };
        btnRemoveSponsor.Click += (_, _) => RemoveSponsor();

        var btnSponsorUp = new Button { Text = "Move Up" };
        btnSponsorUp.Click += (_, _) => MoveSponsor(-1);

        var btnSponsorDown = new Button { Text = "Move Down" };
        btnSponsorDown.Click += (_, _) => MoveSponsor(1);

        _txtSponsorName.TextChanged += (_, _) => SaveSponsorField(s => s.Name = _txtSponsorName.Text);
        _txtSponsorTagline.TextChanged += (_, _) => SaveSponsorField(s => s.Tagline = _txtSponsorTagline.Text);

        // The list's wording follows the fields when the cursor LEAVES
        // them, not as they are typed. Replacing a row in a list box takes
        // the native control's selection with it, and doing that on every
        // keystroke would put the page through its own selection handler
        // once a letter - as well as flickering the list under somebody
        // typing into the box above it.
        _txtSponsorName.Leave += (_, _) => RefreshSponsorLabels();
        _txtSponsorCopy.Leave += (_, _) => RefreshSponsorLabels();

        // Held to the same number SponsorCopy keeps, so what is typed is
        // what is read: a cap applied silently further down is how a club
        // finds out their last sentence went missing from the sponsor.
        _txtSponsorCopy.MaxLength = SponsorCopy.MaxChars;

        // And the name and tagline, held to what SponsorRotation will say
        // of them - for the same reason.
        _txtSponsorName.MaxLength = SponsorRotation.MaxNameChars;
        _txtSponsorTagline.MaxLength = SponsorRotation.MaxTaglineChars;

        _txtSponsorCopy.Multiline = true;
        _txtSponsorCopy.ScrollBars = ScrollBars.Vertical;
        _txtSponsorCopy.BackColor = AppTheme.Surface;
        _txtSponsorCopy.ForeColor = AppTheme.TextDark;
        _txtSponsorCopy.BorderStyle = BorderStyle.FixedSingle;
        _txtSponsorCopy.TextChanged += (_, _) =>
        {
            SaveSponsorField(s => s.SalesCopy = _txtSponsorCopy.Text);
            ShowCopyLength();
        };

        _numSponsorWeight.Minimum = 1;
        _numSponsorWeight.Maximum = 10;
        _numSponsorWeight.Value = 1;
        _numSponsorWeight.ValueChanged += (_, _) =>
        {
            SaveSponsorField(s => s.Weight = (int)_numSponsorWeight.Value);
            RefreshSponsorLabels();
        };

        _chkSponsorEnabled.Text = "In tonight's rotation";
        _chkSponsorEnabled.AutoSize = true;
        _chkSponsorEnabled.Checked = true;
        _chkSponsorEnabled.CheckedChanged += (_, _) =>
        {
            SaveSponsorField(s => s.Enabled = _chkSponsorEnabled.Checked);
            RefreshSponsorLabels();
        };

        // The list, its buttons and the selected sponsor's fields. The note
        // and the two switches go on the card itself rather than in here,
        // exactly as the video card above does it - a wrapping note belongs
        // to the card's width, not to a grid's field column.
        grid = new FieldGrid();

        grid.AddRow("Sponsors:", _lstSponsors);
        grid.AddField(new ButtonBar().With(btnAddSponsor, btnRemoveSponsor, btnSponsorUp, btnSponsorDown));
        grid.AddRow("Name, as it's said:", _txtSponsorName);
        grid.AddHint("Spell it the way it should sound - \"and co\" rather than the ampersand on their banner.");
        grid.AddRow("One-line tagline:", _txtSponsorTagline);
        grid.AddHint("Said after every credit for a moment they present (below), and once on their first read of the night. "
                     + "Leave it empty if they only bought the full read.");
        grid.AddRow("Their read:", _txtSponsorCopy);
        grid.AddHint(_lblCopyLength);
        _cmbSponsorPicture.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbSponsorPicture.SelectedIndexChanged += (_, _) => PictureChosen();

        _btnSponsorPicture.Text = "Upload...";
        _btnSponsorPicture.Click += (_, _) => UploadSponsorPicture();

        grid.AddRow("Picture for their read:", FieldGrid.Inline(_cmbSponsorPicture, _btnSponsorPicture));
        grid.AddHint(
            "On the Big Screen for the length of their read. Choose a picture already uploaded, or upload one - it "
            + "joins the slides above, where you can see or remove it.");

        grid.AddRow("Share of the reads:", _numSponsorWeight);
        grid.AddHint("Two gets twice the reads of one, however many the night has room for. Everyone ends up within one of their share.");

        // SPONSORED SEGMENTS (18 Oct 2026) - "the fastest lap, presented by".
        // See SponsoredMoments for why this is a different thing from the
        // reads above, and SponsoredMomentGate for the ration.
        _lstSponsorMoments.CheckOnClick = true;
        _lstSponsorMoments.BorderStyle = BorderStyle.None;
        _lstSponsorMoments.IntegralHeight = false;

        foreach (var moment in Enum.GetValues<SponsoredMoment>())
        {
            _lstSponsorMoments.Items.Add(SponsoredMoments.Label(moment));
        }

        _lstSponsorMoments.ItemCheck += (_, e) =>
        {
            if (_fillingSponsor)
            {
                return;
            }

            // ItemCheck comes BEFORE the tick changes, so the new state is
            // read from the event rather than the list.
            var moment = (SponsoredMoment)e.Index;
            var on = e.NewValue == CheckState.Checked;

            SaveSponsorField(s => SetPresents(s, moment, on));
            ShowMomentClashes();
        };

        grid.AddRow("Presents:", _lstSponsorMoments);
        grid.AddHint(
            "A short credit - \"That fastest lap is brought to you by\", their name, then their tagline - on the end of "
            + "the call (the top of their segment on the recap show) and on the stream's graphic. A moment that happens "
            + "often (fastest laps, lead changes, battles) is credited at most once every three minutes; introductions, the qualifying recap "
            + "and the result every time. Every mention goes in tonight's report.");
        grid.AddHint(_lblMomentClashes);
        grid.AddField(_chkSponsorEnabled);

        // WHAT EACH OF THEM GOT (11 Oct 2026). The counting has happened
        // since the paid reads were built and nothing ever collected it
        // into something a club could send - which is the one thing on
        // this page that renews a sponsor. See SponsorReport.
        _btnReport.Text = "Copy report for tonight";
        _btnReport.Click += (_, _) => CopyTheReport();

        grid.AddField(new ButtonBar().With(_btnReport));
        grid.AddHint(
            "Copies a plain-text report of what each sponsor got tonight, ready to paste into an email: their reads, "
            + "the share of the evening that was, and the share they bought. Reads only - the app doesn't know which "
            + "slide belongs to which sponsor.");

        var card = new Section("Sponsor reads");

        card.Add(lblReadsIntro, gapBelow: UiLayout.RowGap);
        card.Add(_chkReadInAdBreaks, stretch: false);
        card.Add(_chkReadInSegments, stretch: false, gapBelow: UiLayout.SectionGap);
        card.Add(grid, gapBelow: 0);

        return card;
    }

    /// <summary>
    /// Builds tonight's sponsor report and puts it on the clipboard.
    ///
    /// THE CLIPBOARD AND NOT A FILE, because the thing a club secretary
    /// actually does with this is paste it into an email. A file would mean
    /// a Save dialog, a folder to remember and an attachment to find - and
    /// a report that needs opening is a report that gets forwarded as a
    /// screenshot. The text is short enough to read in the message that
    /// confirms it went.
    ///
    /// Clipboard.SetText throws often enough to be worth catching - another
    /// application holding the clipboard open is all it takes - and a club
    /// told why can press it again.
    /// </summary>
    private void CopyTheReport()
    {
        try
        {
            var report = SponsorReport.ForTheNight(
                _store.Sponsors,
                ReadsTonight?.Invoke(),
                _store.ReadPlan().Placement,
                EventName?.Invoke(),
                // The race NIGHT, 6am to 6am like every other night-scoped
                // reader - a report copied at twenty past midnight was
                // dated the day after the club raced.
                TonightAtTheTrack.NightOf(DateTime.Now),
                MentionsTonight?.Invoke());

            if (string.IsNullOrWhiteSpace(report))
            {
                return;
            }

            Clipboard.SetText(report);

            AppDialog.Show(
                this,
                "The report is on the clipboard - paste it into an email.\n\n" + report,
                "Sponsor report copied",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppDialog.Show(
                this,
                $"Couldn't copy the report: {ex.Message}\n\n"
                    + "Something else on this PC may be holding the clipboard. Try again.",
                "Report not copied",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// The number boxes: as wide as three digits and their arrows need in the
    /// page's face, and never narrower than a number box at this text size.
    /// Only when it changed - a new width lays the grid out again.
    ///
    /// The sponsor fields are here too, for the same reason: a name box, a
    /// read box of four lines of its own font, and a list of five rows -
    /// each the width it was at the normal text size, at the size in use.
    /// A box left to size itself would be one line tall and a list would be
    /// one row.
    /// </summary>
    private void SizeFields()
    {
        var number = Math.Max(
            UiLayout.AtTextSize(UiLayout.NumericWidth),
            TextRenderer.MeasureText("000", Font).Width + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra);

        foreach (var spinner in new[] { _numLeaderboardSeconds, _numSlideSeconds, _numSponsorWeight })
        {
            if (spinner.Width != number)
            {
                spinner.Width = number;
            }
        }

        Fit(_txtSponsorName, UiLayout.AtTextSize(SponsorNameWidth));
        Fit(_txtSponsorTagline, UiLayout.AtTextSize(SalesCopyWidth));
        Fit(_cmbSponsorPicture, UiLayout.AtTextSize(SponsorPictureWidth));

        // Four lines of the box's own font, and the hint's gap twice over
        // for the room a text box keeps inside its edge - exactly how the
        // Commentary page sizes its teaser box.
        Fit(
            _txtSponsorCopy,
            UiLayout.AtTextSize(SalesCopyWidth),
            Pieces.LineHeight(_txtSponsorCopy.Font) * SalesCopyLines + UiLayout.HintGap * 2);

        Fit(_lstSponsors, UiLayout.AtTextSize(SponsorListWidth), _lstSponsors.ItemHeight * SponsorRows + 4);

        // Every moment on show at once - eight rows of its own font - so a
        // club sees the whole list of what it can sell without scrolling.
        Fit(_lstSponsorMoments, UiLayout.AtTextSize(SponsorListWidth), _lstSponsorMoments.ItemHeight * Enum.GetValues<SponsoredMoment>().Length + 4);

        static void Fit(Control field, int width, int? height = null)
        {
            if (height is { } tall)
            {
                var size = new Size(width, tall);

                if (field.Size != size)
                {
                    field.Size = size;
                }

                return;
            }

            if (field.Width != width)
            {
                field.Width = width;
            }
        }
    }

    /// <summary>
    /// The slides' column: the width the list was at the normal text size, at
    /// the size in use - and never more than a little under half the card,
    /// so the preview beside it keeps the larger share when the window is
    /// narrow or the text large, nor less than a file name's worth.
    /// </summary>
    private int ListColumnWidth()
    {
        var wanted = UiLayout.AtTextSize(ListWidth);
        var room = _slidesBand?.Width ?? 0;

        return room > 0
            ? Math.Min(wanted, Math.Max(UiLayout.AtTextSize(ListLeastWidth), room * 45 / 100))
            : wanted;
    }

    private void RefreshSlides()
    {
        var previous = _lstSlides.SelectedItem as string;

        _lstSlides.Items.Clear();
        foreach (var name in _store.SlideFileNames)
        {
            _lstSlides.Items.Add(name);
        }

        if (previous is not null && _lstSlides.Items.Contains(previous))
        {
            _lstSlides.SelectedItem = previous;
        }
        else if (_lstSlides.Items.Count > 0)
        {
            _lstSlides.SelectedIndex = 0;
        }
        else
        {
            RefreshPreview();
        }
    }

    private void RefreshPreview()
    {
        _picPreview.Image?.Dispose();
        _picPreview.Image = null;
        _lblVideoNote.Visible = false;
        _btnPreviewVideo.Enabled = false;

        if (_lstSlides.SelectedItem is not string fileName)
        {
            return;
        }

        var path = _store.ResolvePath(fileName);
        if (path is null)
        {
            return;
        }

        // A video isn't decoded into the preview. Showing a still from it
        // would mean starting LibVLC just to draw this page, every time the
        // selection moved - so the preview says what it is and how long it
        // will hold the screen, which is what an operator picking slide
        // order actually needs to know.
        if (SponsorMedia.IsVideoName(fileName))
        {
            var size = TryFileLength(path);

            _lblVideoNote.Text = "Video\n\n" + Path.GetFileName(fileName)
                                 + (size is { } bytes ? $"\n{bytes / (1024 * 1024)} MB" : string.Empty)
                                 + "\n\nIt plays to its own end on the Big Screen."
                                 + "\n\nDouble-click, or press Preview Video, to watch it here.";
            _lblVideoNote.Visible = true;
            _btnPreviewVideo.Enabled = true;
            return;
        }

        try
        {
            // Only a slide no bigger than Add Image... takes in, sized from
            // its header before anything is decoded (see
            // SponsorStore.ReadSlide). This used to decode whatever the file
            // held, every time the slide was picked in the list. Anything
            // else leaves the preview blank, as an unreadable image does.
            if (SponsorStore.ReadSlide(path) is not { } bytes)
            {
                return;
            }

            // Copied into a Bitmap of its own before the stream closes - an
            // Image from FromStream keeps reading its stream for life, and
            // this one's is disposed at the end of the block. Orientation is
            // fixed first, as the copy doesn't carry the EXIF tag.
            using var stream = new MemoryStream(bytes);
            using var decoded = Image.FromStream(stream);
            TrackAttachmentStore.ApplyExifOrientation(decoded);
            _picPreview.Image = new Bitmap(decoded);
        }
        catch
        {
            // Unreadable image - just leave the preview blank.
        }
    }

    private void AddSlide()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Add a sponsor slide",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*",
            Multiselect = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var failures = new List<string>();
        foreach (var file in dialog.FileNames)
        {
            if (_store.AddSlide(file, out var error) is null)
            {
                failures.Add($"{Path.GetFileName(file)}: {error}");
            }
        }

        if (failures.Count > 0)
        {
            // Each file's own reason, now that a picture can be turned away
            // for its size as well as fail to copy - this used to give only
            // the count. The first few, so a big selection can't grow the
            // message past the screen.
            const int listed = 3;

            var more = failures.Count > listed
                ? $"\n\n... and {Plural.Of(failures.Count - listed, "more file")}."
                : string.Empty;

            AppDialog.Show(this,
                $"Couldn't add {Plural.Of(failures.Count, "file")} you selected.\n\n{string.Join("\n\n", failures.Take(listed))}{more}",
                "Some files failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        RefreshSlides();
    }

    /// <summary>
    /// Takes a video into the rotation. Multiselect, and one reason per
    /// file that failed, exactly as AddSlide does it - the two differ only
    /// in what they accept and in that a video is copied rather than read
    /// (see SponsorStore.AddVideo).
    /// </summary>
    private void AddVideo()
    {
        // Said before the file dialog rather than after: a club picking a
        // video on a build that can't play one should hear so before it
        // spends a minute copying 200 MB into %AppData%.
        if (!SponsorVideoEngine.NativesPresent)
        {
            AppDialog.Show(this,
                "This copy of HeatCaller can't play videos - the video player's files are missing from beside HeatCaller.exe. "
                + "Reinstall HeatCaller, and sponsor images will keep working in the meantime.",
                "Video unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "Add a sponsor video",
            Filter = SponsorMedia.VideoFilter,
            Multiselect = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var failures = new List<string>();
        foreach (var file in dialog.FileNames)
        {
            if (_store.AddVideo(file, out var error) is null)
            {
                failures.Add($"{Path.GetFileName(file)}: {error}");
            }
        }

        if (failures.Count > 0)
        {
            const int listed = 3;

            var more = failures.Count > listed
                ? $"\n\n... and {Plural.Of(failures.Count - listed, "more file")}."
                : string.Empty;

            AppDialog.Show(this,
                $"Couldn't add {Plural.Of(failures.Count, "file")} you selected.\n\n{string.Join("\n\n", failures.Take(listed))}{more}",
                "Some files failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        RefreshSlides();
    }

    /// <summary>
    /// Plays the selected video slide in a preview window, with its sound,
    /// through the same player the Big Screen uses. Does nothing when the
    /// selected slide is an image.
    /// </summary>
    private void PreviewSelectedVideo()
    {
        if (_lstSlides.SelectedItem is not string fileName || !SponsorMedia.IsVideoName(fileName))
        {
            return;
        }

        if (!SponsorVideoEngine.NativesPresent)
        {
            AppDialog.Show(this,
                "This copy of HeatCaller can't play videos - the video player's files are missing from beside HeatCaller.exe. "
                + "Reinstall HeatCaller.",
                "Video unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_store.ResolvePath(fileName) is not { } path)
        {
            return;
        }

        using var preview = new SponsorVideoPreviewForm(path);
        preview.ShowDialog(FindForm());
    }

    /// <summary>A stored slide's size on disk, or null when it can't be read - the preview says nothing about size rather than failing to draw.</summary>
    private static long? TryFileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Greys the music switch out while video sound is off: with no sound
    /// to talk over there is nothing to silence, and a live-looking tick
    /// that does nothing is worse than one plainly turned off.
    /// </summary>
    private void SyncVideoSoundEnabled() =>
        _chkMuteMusicDuringVideo.Enabled = _chkPlayVideoSound.Checked;

    private void RemoveSelected()
    {
        if (_lstSlides.SelectedItem is not string fileName)
        {
            return;
        }

        if (AppDialog.Show(this, "Remove this slide from the rotation?", "Confirm remove", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        // Release the preview's file handle first - otherwise the move
        // inside RemoveSlide can silently fail on a file we're displaying.
        _picPreview.Image?.Dispose();
        _picPreview.Image = null;

        // Captured BEFORE the removal, with the place the slide holds now, so
        // undo puts it back there rather than on the end. The image is moved
        // to the undo holding folder by RemoveSlide; where it went is filled
        // in below, before the undo can ever run.
        var index = _store.SlideFileNames.ToList().IndexOf(fileName);
        string? held = null;
        var store = _store;
        _undo.Capture($"Remove sponsor slide {fileName}", () =>
        {
            store.RestoreSlide(fileName, index, held);
            store.NotifyRestored();
        });

        held = _store.RemoveSlide(fileName);

        RefreshSlides();
    }

    private void MoveSelected(int direction)
    {
        if (_lstSlides.SelectedItem is not string fileName)
        {
            return;
        }

        // A move changes the order the rotation shows them in, so it can be
        // undone too - by moving the same slide back the other way, which
        // leaves any other reordering since alone.
        var from = _store.SlideFileNames.ToList().IndexOf(fileName);
        var to = from + direction;
        if (from >= 0 && to >= 0 && to < _store.SlideFileNames.Count)
        {
            var store = _store;
            _undo.Capture(direction < 0 ? $"Move sponsor slide {fileName} up" : $"Move sponsor slide {fileName} down", () =>
            {
                store.Move(fileName, -direction);
                store.NotifyRestored();
            });
        }

        _store.Move(fileName, direction);
        RefreshSlides();
        _lstSlides.SelectedItem = fileName;
    }

    // ------------------------------------------------------------------
    // The sponsors the announcer reads
    // ------------------------------------------------------------------

    /// <summary>
    /// A sponsor in the list: the id it really is, and the line the list
    /// shows.
    ///
    /// The id and not the name, because the name is what is being edited:
    /// a list that held names would lose its place on the first keystroke
    /// in the name box, and a club renaming a sponsor would find the
    /// selection jump away mid-word.
    /// </summary>
    private sealed record SponsorRow(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>The id of the sponsor selected in the list, or null when none is.</summary>
    private string? SelectedSponsorId => (_lstSponsors.SelectedItem as SponsorRow)?.Id;

    /// <summary>
    /// Re-reads the sponsors into the list and fills the fields from
    /// whichever is selected, keeping the selection where it was. Called
    /// when the page is built, after an undo, and after anything that adds,
    /// removes or reorders.
    /// </summary>
    private void RefreshSponsors()
    {
        var was = SelectedSponsorId;

        _lstSponsors.Items.Clear();

        foreach (var sponsor in _store.Sponsors)
        {
            _lstSponsors.Items.Add(Row(sponsor));
        }

        var back = _lstSponsors.Items
            .OfType<SponsorRow>()
            .FirstOrDefault(row => string.Equals(row.Id, was, StringComparison.Ordinal));

        if (back is not null)
        {
            _lstSponsors.SelectedItem = back;
        }
        else if (_lstSponsors.Items.Count > 0)
        {
            _lstSponsors.SelectedIndex = 0;
        }
        else
        {
            // Nothing selected raises no SelectedIndexChanged, so the
            // fields are cleared by hand - otherwise the last sponsor
            // removed would still be showing in them.
            FillSponsorFields();
        }
    }

    /// <summary>
    /// The line the list shows for a sponsor: their name, and what they
    /// bought. The share only where it is more than one, and "read" only
    /// where there is copy - a list where every row carries the same two
    /// notes is a list nobody reads.
    /// </summary>
    private static SponsorRow Row(SponsorBooking sponsor)
    {
        var name = string.IsNullOrWhiteSpace(sponsor.Name) ? "(no name yet)" : sponsor.Name.Trim();

        var notes = new List<string>();

        if (!string.IsNullOrWhiteSpace(sponsor.SalesCopy))
        {
            notes.Add("read");
        }

        if (sponsor.Weight > 1)
        {
            notes.Add($"share {sponsor.Weight}");
        }

        if (!sponsor.Enabled)
        {
            notes.Add("off tonight");
        }

        return new SponsorRow(sponsor.Id, notes.Count == 0 ? name : $"{name}  -  {string.Join(", ", notes)}");
    }

    /// <summary>
    /// Re-labels the rows whose wording has changed, so a name typed into
    /// the box above shows in the list as well.
    ///
    /// Row by row rather than cleared and refilled, so a list of eight
    /// sponsors is not rebuilt because one letter changed. Replacing a row
    /// can take the list's selection with it - the native control does not
    /// promise to keep it - so the selection is put back; the fields are
    /// then filled from the store, which already holds what was typed, and
    /// setting a box to what it already says raises nothing.
    /// </summary>
    private void RefreshSponsorLabels()
    {
        var selected = _lstSponsors.SelectedIndex;
        var replaced = false;

        for (var i = 0; i < _lstSponsors.Items.Count; i++)
        {
            if (_lstSponsors.Items[i] is not SponsorRow row || _store.Sponsor(row.Id) is not { } sponsor)
            {
                continue;
            }

            var fresh = Row(sponsor);

            if (string.Equals(fresh.Label, row.Label, StringComparison.Ordinal))
            {
                continue;
            }

            _lstSponsors.Items[i] = fresh;
            replaced = true;
        }

        if (replaced && selected >= 0 && selected < _lstSponsors.Items.Count && _lstSponsors.SelectedIndex != selected)
        {
            _lstSponsors.SelectedIndex = selected;
        }
    }

    /// <summary>
    /// Fills the fields from the selected sponsor, or empties and greys
    /// them when there is none.
    ///
    /// Guarded with _fillingSponsor: every one of these boxes saves what it
    /// holds as it changes, and without the guard selecting a sponsor would
    /// write the one before it over the one just picked.
    /// </summary>
    private void FillSponsorFields()
    {
        var sponsor = _store.Sponsor(SelectedSponsorId);

        _fillingSponsor = true;

        try
        {
            _txtSponsorName.Text = sponsor?.Name ?? string.Empty;
            _txtSponsorTagline.Text = sponsor?.Tagline ?? string.Empty;
            _txtSponsorCopy.Text = sponsor?.SalesCopy ?? string.Empty;
            _numSponsorWeight.Value = Math.Clamp(sponsor?.Weight ?? 1, (int)_numSponsorWeight.Minimum, (int)_numSponsorWeight.Maximum);
            _chkSponsorEnabled.Checked = sponsor?.Enabled ?? true;

            var presents = sponsor?.Presents ?? new List<string>();

            for (var i = 0; i < _lstSponsorMoments.Items.Count; i++)
            {
                var key = ((SponsoredMoment)i).ToString();
                _lstSponsorMoments.SetItemChecked(i, presents.Any(p => string.Equals(p, key, StringComparison.OrdinalIgnoreCase)));
            }

            FillPictureChoices(sponsor?.PictureFileName);
        }
        finally
        {
            _fillingSponsor = false;
        }

        var on = sponsor is not null;

        _txtSponsorName.Enabled = on;
        _txtSponsorTagline.Enabled = on;
        _txtSponsorCopy.Enabled = on;
        _numSponsorWeight.Enabled = on;
        _chkSponsorEnabled.Enabled = on;
        _lstSponsorMoments.Enabled = on;
        _cmbSponsorPicture.Enabled = on;
        _btnSponsorPicture.Enabled = on;

        ShowCopyLength();
        ShowMomentClashes();
    }

    /// <summary>Ticks or unticks one moment on a sponsor's booking - by name, once.</summary>
    private static void SetPresents(SponsorBooking sponsor, SponsoredMoment moment, bool on)
    {
        sponsor.Presents ??= new List<string>();

        var key = moment.ToString();
        sponsor.Presents.RemoveAll(p => string.Equals(p, key, StringComparison.OrdinalIgnoreCase));

        if (on)
        {
            sponsor.Presents.Add(key);
        }
    }

    /// <summary>
    /// Says plainly when one moment is ticked for two sponsors: only the one
    /// higher in the list is named, and a club must not think both are.
    /// </summary>
    private void ShowMomentClashes()
    {
        var clashes = SponsoredMoments.Clashes(_store.Sponsors);

        _lblMomentClashes.ForeColor = clashes.Count > 0 ? AppTheme.Danger : AppTheme.TextMuted;
        _lblMomentClashes.Text = clashes.Count == 0
            ? string.Empty
            : string.Join(" ", clashes.Select(c =>
                $"{SponsoredMoments.Label(c.Moment)} is ticked for {c.Winner} and {string.Join(" and ", c.Others)} - only {c.Winner}, higher in the list, is named."));
    }

    /// <summary>
    /// Fills the picture drop-down with the pictures the club has, and
    /// selects this sponsor's.
    ///
    /// PICTURES ONLY. The slide list holds videos too, and a video under a
    /// spoken read would be two adverts at once - the collision the
    /// commercial break was built to stop. SponsorStore.OnScreenFor
    /// refuses one as well, so a hand-edited file cannot get one in here
    /// either.
    ///
    /// A name that is no longer in the folder is still SHOWN as selected,
    /// so a club can see what it was pointed at rather than finding it
    /// silently back on "none" - and the read falls back to the sponsor's
    /// name in the meantime.
    /// </summary>
    private void FillPictureChoices(string? chosen)
    {
        _cmbSponsorPicture.Items.Clear();
        _cmbSponsorPicture.Items.Add(NoPictureItem);

        foreach (var slide in _store.SlideFileNames)
        {
            if (!SponsorMedia.IsVideoName(slide))
            {
                _cmbSponsorPicture.Items.Add(slide);
            }
        }

        var wanted = chosen ?? string.Empty;

        if (wanted.Length == 0)
        {
            _cmbSponsorPicture.SelectedIndex = 0;

            return;
        }

        // Still named even when the file has gone - see the remarks.
        if (!_cmbSponsorPicture.Items.Contains(wanted))
        {
            _cmbSponsorPicture.Items.Add(wanted);
        }

        _cmbSponsorPicture.SelectedItem = wanted;
    }

    /// <summary>A picture picked from the drop-down. "None" stores nothing, which is what the name fallback reads.</summary>
    private void PictureChosen()
    {
        if (_fillingSponsor)
        {
            return;
        }

        var picked = _cmbSponsorPicture.SelectedIndex <= 0
            ? string.Empty
            : _cmbSponsorPicture.SelectedItem as string ?? string.Empty;

        SaveSponsorField(sponsor => sponsor.PictureFileName = picked);
    }

    /// <summary>
    /// Brings a picture in for this sponsor's read.
    ///
    /// THROUGH AddSlide, which is the one path a picture enters this app
    /// by: it checks the size, copies it into the club's own folder under
    /// a name of its own, and adds it to the rotation. The rotation part
    /// is deliberate rather than incidental - a picture stored outside it
    /// would be one the club could neither see nor delete, and the only
    /// remove button on this page works on that list.
    /// </summary>
    private void UploadSponsorPicture()
    {
        if (_store.Sponsor(SelectedSponsorId) is null)
        {
            return;
        }

        using var picker = new OpenFileDialog
        {
            Title = "Choose a picture for this sponsor's read",
            Filter = "Images (" + string.Join(";", SponsorMedia.ImageExtensions.Select(e => "*" + e)) + ")|"
                + string.Join(";", SponsorMedia.ImageExtensions.Select(e => "*" + e))
                + "|All files (*.*)|*.*",
        };

        if (picker.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        string? stored;
        string? why;

        try
        {
            stored = _store.AddSlide(picker.FileName, out why);
        }
        catch (Exception ex)
        {
            AppDialog.Show(
                this,
                $"That picture couldn't be brought in: {ex.Message}",
                "Couldn't add the picture",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            return;
        }

        if (stored is null)
        {
            AppDialog.Show(
                this,
                why ?? "That picture couldn't be used.",
                "Couldn't add the picture",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        SaveSponsorField(sponsor => sponsor.PictureFileName = stored);

        // The slide list above has a new row, and the drop-down a new
        // entry - both are the same folder.
        RefreshSlides();
        FillPictureChoices(stored);
    }

    /// <summary>
    /// Writes one typed field onto the selected sponsor and saves. Does
    /// nothing while the fields are being filled in - see _fillingSponsor -
    /// and nothing when no sponsor is selected.
    /// </summary>
    private void SaveSponsorField(Action<SponsorBooking> set)
    {
        if (_fillingSponsor || _store.Sponsor(SelectedSponsorId) is not { } sponsor)
        {
            return;
        }

        set(sponsor);
        _store.Save();
    }

    /// <summary>
    /// How long the copy in the box takes to read, and over how many gaps.
    ///
    /// Worth saying plainly, because it is the one thing about sales copy a
    /// club cannot judge by looking at it: four lines of text is nearly a
    /// minute out loud, and copy longer than a gap is read over two or
    /// three of them rather than cut - which is a surprise unless it is on
    /// the screen in front of whoever is pasting it in.
    /// </summary>
    private void ShowCopyLength()
    {
        var copy = SponsorCopy.Tidy(_txtSponsorCopy.Text);

        if (copy.Length == 0)
        {
            _lblCopyLength.Text = "A few sentences, as they should be spoken. Leave it empty and the sponsor gets the one-line mention instead.";
            return;
        }

        var seconds = Math.Max(1, (int)Math.Round(SponsorCopy.SpokenLength(copy).TotalSeconds));
        var parts = 0;
        var at = 0;

        // Counted by walking the copy exactly as a race night would, so the
        // number here is the number the club will actually hear - rather
        // than a division that disagrees with it by one.
        while (parts < 10)
        {
            var fit = SponsorCopy.Fit(copy, at, SponsorCopy.LongestRead);

            if (fit.Text.Length == 0)
            {
                break;
            }

            parts++;

            if (!fit.More)
            {
                break;
            }

            at = fit.ResumeAt;
        }

        var over = parts > 1 ? $", read over {parts} gaps" : string.Empty;

        _lblCopyLength.Text = $"About {Plural.Of(seconds, "second")} to read{over}. "
                              + $"{copy.Length} of {SponsorCopy.MaxChars} characters used.";
    }

    /// <summary>
    /// Adds a sponsor, selects it and puts the cursor in the name box -
    /// which is the only field that has to be filled in for anything to
    /// happen, so it is where the club is sent.
    /// </summary>
    private void AddSponsor()
    {
        var added = _store.AddSponsor();

        var store = _store;
        var id = added.Id;

        _undo.Capture($"Add sponsor {added.Name}", () =>
        {
            store.RemoveSponsor(id);
            store.NotifyRestored();
        });

        RefreshSponsors();

        _lstSponsors.SelectedItem = _lstSponsors.Items
            .OfType<SponsorRow>()
            .FirstOrDefault(row => string.Equals(row.Id, id, StringComparison.Ordinal));

        _txtSponsorName.Focus();
        _txtSponsorName.SelectAll();
    }

    /// <summary>
    /// Takes the selected sponsor off the list, keeping the whole record
    /// for undo.
    ///
    /// The record is captured BEFORE the removal and put back whole, the
    /// same shape as removing a slide: a sponsor's read is a few sentences
    /// somebody wrote and may be the only copy of them anywhere, so an
    /// undo that brought back the name and not the words would be worse
    /// than no undo at all.
    /// </summary>
    private void RemoveSponsor()
    {
        if (_store.Sponsor(SelectedSponsorId) is not { } sponsor)
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(sponsor.Name) ? "this sponsor" : sponsor.Name.Trim();

        if (AppDialog.Show(this,
                $"Remove {name} from the reads? Their slides and videos stay where they are.",
                "Confirm remove", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var store = _store;
        var index = _store.RemoveSponsor(sponsor.Id);

        if (index >= 0)
        {
            _undo.Capture($"Remove sponsor {name}", () =>
            {
                store.RestoreSponsor(sponsor, index);
                store.NotifyRestored();
            });
        }

        RefreshSponsors();
    }

    /// <summary>
    /// Moves the selected sponsor up or down. Undone by moving the same
    /// sponsor back the other way, which leaves any other reordering since
    /// alone - exactly as a slide's move is undone.
    /// </summary>
    private void MoveSponsor(int direction)
    {
        if (_store.Sponsor(SelectedSponsorId) is not { } sponsor)
        {
            return;
        }

        var from = _store.Sponsors.ToList().IndexOf(sponsor);
        var to = from + direction;

        if (from >= 0 && to >= 0 && to < _store.Sponsors.Count)
        {
            var store = _store;
            var id = sponsor.Id;

            _undo.Capture(direction < 0 ? $"Move sponsor {sponsor.Name} up" : $"Move sponsor {sponsor.Name} down", () =>
            {
                store.MoveSponsor(id, -direction);
                store.NotifyRestored();
            });
        }

        _store.MoveSponsor(sponsor.Id, direction);

        RefreshSponsors();
    }
}
