namespace HeatCaller;

/// <summary>
/// Sounds > Recording (17 Oct 2026): recording each race and cutting each
/// driver their clip, where the files go and how many nights are kept, and
/// the pieces written from the night - tonight's recap, a driver's own
/// night, the proof of a line and the night-before preview.
///
/// MOVED FROM THE COMMENTARY PAGE, where it was the "Recording the night"
/// card - the Voice + Sounds regroup put everything you hear except the
/// voice on Sounds. Same settings and the same behaviour: the record switch
/// is not gated on the announcer, and the four pieces written from the
/// night still need AI commentary on, because the announcer's model writes
/// them. Every hook is set by MainForm, the way CommentaryControl's were
/// (MainForm.Commentary.cs).
/// </summary>
public sealed class RecordingControl : UserControl
{
    private readonly AppSettings _settings;

    // --- Recording the night (11 Oct 2026) ---
    //
    // The recording and the per-driver clips have been written after every
    // race since they were built, and no page had a single control for any
    // of it: a club could not turn it off, could not find the files, and
    // was never told that a season of broadcast was accumulating in
    // Documents. See RaceAudioStore.
    //
    // The store is this page's own rather than the one Race Control holds.
    // Both load the same small file and neither caches a decision from it -
    // Race Control reads its switches at each green - so a change made
    // here is in force at the next race without anything being told.
    private readonly RaceAudioStore _audio = RaceAudioStore.Load();
    private readonly CheckBox _chkRecord = new ThemedCheckBox();
    private readonly CheckBox _chkClips = new ThemedCheckBox();

    /// <summary>Which drivers get a highlight video (21 Oct 2026) - see HighlightVideo.</summary>
    private readonly ComboBox _cmbVideos = new();

    /// <summary>The club's handle for the end of each video.</summary>
    private readonly TextBox _txtVideoHandle = new();

    /// <summary>The podium's videos on the club's page, with a QR code on the Big Screen (21 Oct 2026).</summary>
    private readonly CheckBox _chkShareVideos = new ThemedCheckBox();

    /// <summary>Each driver's own mp3 clip on their private night page on the club's website (22 Oct 2026, driver experience).</summary>
    private readonly CheckBox _chkShareClips = new ThemedCheckBox();
    private readonly TextBox _txtAudioFolder = new();
    private readonly Button _btnAudioFolder = new();
    private readonly Button _btnAudioOpen = new();
    private readonly NumericUpDown _numKeepNights = new();
    private readonly Label _lblOnDisk = new();

    /// <summary>
    /// Writes and plays the night's own recap - see NightRecap. On the
    /// recording page rather than with the show's music, because what a
    /// club wants out of it is the FILE: the piece plays once in a hall that
    /// is packing up, and then sits in the night's folder for the people who
    /// were not there.
    /// </summary>
    private readonly Button _btnNightRecap = new();

    /// <summary>What happened when it was pressed, in the club's words.</summary>
    private readonly Label _lblNightRecap = new();

    // --- A driver's own night, and the proof of a line (12 Oct 2026) ---

    /// <summary>
    /// Which driver's piece to write - tonight's drivers, filled when the
    /// box is opened so it is never a list from an hour ago.
    /// </summary>
    private readonly ComboBox _cmbDriverCut = new();

    private readonly Button _btnDriverCut = new();
    private readonly Label _lblDriverCut = new();

    /// <summary>
    /// Tonight's drivers in the order the box lists them, so the box's own
    /// index gives back a transponder.
    ///
    /// A LIST BESIDE THE BOX rather than objects in it. A ComboBox holding
    /// a record needs DisplayMember and a ToString nobody can see, and the
    /// box here is redrawn by the theme pass; an index into a list the page
    /// owns is the shape every other picker uses.
    /// </summary>
    private readonly List<(string Transponder, string Name)> _driversTonight = new();

    private readonly Button _btnProof = new();
    private readonly Label _lblProof = new();
    private readonly Button _btnNightBefore = new();
    private readonly Label _lblNightBefore = new();
    private readonly CheckBox _chkNightBeforeRelay = new ThemedCheckBox();

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    // True while the night recap, a driver's night or the preview is
    // running. Their buttons are held down for the length of the run, and
    // UpdateEnabledState would otherwise put them back up mid-run and let a
    // second one start over the first.
    private bool _nightRecapRunning;
    private bool _driverCutRunning;
    private bool _nightBeforeRunning;

    /// <summary>The usual captions of the buttons that say so while they work - see Steady.</summary>
    private const string NightRecapCaption = "Write And Play It";
    private const string DriverCutCaption = "Write And Play It";
    private const string ProofCaption = "Why It Said That";
    private const string NightBeforeCaption = "Write The Preview";

    /// <summary>
    /// How wide the driver picker is at the normal text size.
    ///
    /// Two hundred and sixty, and it has to be SET: left to itself a
    /// ComboBox is 121 pixels and would cut most club members' names in
    /// half. FIXED RATHER THAN GROWN TO ITS LONGEST ITEM: this list is the
    /// club's own members, so growing to fit would let one driver with a
    /// long name widen the card - and at 200% text that is how a page stops
    /// fitting on a 1280 screen. A name past this is clipped in the box and
    /// is still right in the piece and in the file name.
    /// </summary>
    private const int DriverPickerWidth = 260;

    /// <summary>
    /// Whether AI commentary is switched on - the master switch on Voice >
    /// Commentary. The four pieces written from the night need it. Set by
    /// the host; unset, the saved setting is read.
    /// </summary>
    internal Func<bool>? CommentaryOn { get; set; }

    /// <summary>
    /// Writes tonight's recap, plays it out once and keeps it as a file -
    /// AnnouncerControl.PlayTonightsRecapAsync. Null until wired, and then
    /// the button does nothing.
    /// </summary>
    internal Func<Task<string?>>? PlayTonightsRecap { get; set; }

    /// <summary>
    /// Tonight's drivers, for the picker - whoever has a race filed for the
    /// night, as MainForm reads them. Null until wired, and then the picker
    /// stays empty and says so.
    /// </summary>
    internal Func<IReadOnlyList<(string Transponder, string Name)>>? DriversTonight { get; set; }

    /// <summary>
    /// Writes one driver's night, plays it out once and keeps it as a file -
    /// AnnouncerControl.PlayADriversNightAsync. Takes their transponder and
    /// their name. Null until wired, and then the button does nothing.
    /// </summary>
    internal Func<string, string, Task<string?>>? PlayADriversNight { get; set; }

    /// <summary>
    /// Why the announcer said what it last said, written to the developer
    /// console - AnnouncerControl.TheProofOfTheLastLine. Hands back a
    /// sentence for the page either way.
    /// </summary>
    internal Func<string>? ProofOfTheLastLine { get; set; }

    /// <summary>
    /// Writes a preview of the next race day and saves it where the club
    /// can get at it - see <see cref="NightBefore"/>. Hands back a sentence
    /// either way. The host owns this because the preview is built from the
    /// programme, the seasons and the club's own history.
    /// </summary>
    internal Func<Task<string?>>? WriteTheNightBefore { get; set; }

    /// <summary>
    /// Raised when the club unticks putting the night-before preview on its
    /// page, so a preview already published comes down then rather than
    /// standing until the first race is filed. A setting saved is not a
    /// page changed.
    /// </summary>
    internal event Action? NightBeforeClosedOnTheRelay;

    public RecordingControl(AppSettings settings)
    {
        _settings = settings;

        Dock = DockStyle.Fill;
        ForeColor = AppTheme.TextDark;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize.
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
    }

    /// <summary>The page's own face changed - handed the body face at a new text size - so everything else it keeps in step follows. See TextSize.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>A new text size, reaching this page through its parent's font. See TextSize.</summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>The commentary switch is on another page, so the greying is set out again whenever this one is shown.</summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (Visible)
        {
            UpdateEnabledState();
        }
    }

    private bool IsCommentaryOn => CommentaryOn?.Invoke() ?? _settings.CommentaryEnabled;

    /// <summary>
    /// Two cards on the layout pieces (LayoutPieces.cs): recording the
    /// night - the switch, the clips, how many nights and the folder - and
    /// what is written from the night. One caption column down both
    /// (FieldGrid.LineUp); the page scrolls when it is taller than the
    /// window (PageStack).
    /// </summary>
    private void BuildLayout()
    {
        Label Hint(string text) => _textSize.Wear(
            new Label { Text = text, AutoSize = true, ForeColor = AppTheme.TextMuted },
            () => AppTheme.SmallFont);

        Label Suffix(string text)
        {
            var label = new Label { Text = text, AutoSize = true, ForeColor = AppTheme.TextMuted };
            _textSize.Wear(label, () => AppTheme.SmallFont);
            return label;
        }

        static CheckBox Check(CheckBox box, string text)
        {
            box.Text = text;
            box.AutoSize = true;
            return box;
        }

        // A setting that belongs to the switch above it has its caption a
        // step in, as it did on the Commentary page.
        static Label Stepped(Label caption)
        {
            caption.Margin = new Padding(UiLayout.Indent, caption.Margin.Top, caption.Margin.Right, caption.Margin.Bottom);
            return caption;
        }

        // A caption that is not a step in keeps the step on its right, so
        // the fields still line up down the page.
        static Label Flush(Label caption)
        {
            caption.Margin = new Padding(caption.Margin.Left, caption.Margin.Top, caption.Margin.Right + UiLayout.Indent, caption.Margin.Bottom);
            return caption;
        }

        static FlowLayoutPanel Line(params Control[] controls) => FieldGrid.Inline(controls);

        // A button whose caption is swapped while it works ("Writing..."),
        // in a panel of its own that is always as big as its usual caption
        // needs - see CommentaryControl.BuildLayout's Steady.
        Panel Steady(Button button, string caption)
        {
            var holder = new Panel
            {
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Tag = AppTheme.KeepBackColor,
            };

            button.Dock = DockStyle.Fill;
            holder.Controls.Add(button);

            _textSize.Keep(() =>
            {
                var size = UiLayout.ButtonSizeFor(caption);

                if (holder.Size != size)
                {
                    holder.Size = size;
                }
            });

            return holder;
        }

        foreach (var label in new[] { _lblNightRecap, _lblDriverCut, _lblProof, _lblNightBefore, _lblOnDisk })
        {
            label.AutoSize = true;
            label.ForeColor = AppTheme.TextMuted;
            _textSize.Wear(label, () => AppTheme.SmallFont);
        }

        // ------------------------------------------------------------------
        // Recording the night (11 Oct 2026)
        // ------------------------------------------------------------------

        Check(_chkRecord, "Record each race, and cut each driver their own clip");
        _textSize.Wear(_chkRecord, () => AppTheme.BoldFont);
        _chkRecord.Checked = _audio.Enabled;
        _chkRecord.CheckedChanged += (_, _) =>
        {
            _audio.Enabled = _chkRecord.Checked;
            UpdateEnabledState();
        };

        Check(_chkClips, "A clip for each driver as well as the race's own recording");
        _chkClips.Checked = _audio.DriverHighlights;
        _chkClips.CheckedChanged += (_, _) =>
        {
            _audio.DriverHighlights = _chkClips.Checked;

            // The videos come from the clips (22 Oct 2026, bug hunt).
            UpdateEnabledState();
        };

        // THE SHAREABLE VIDEO (21 Oct 2026): an upright mp4 for a driver's
        // phone, made from their clip after the race - see HighlightVideo.
        _cmbVideos.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbVideos.Items.AddRange(new object[] { "Off", "The winner", "The podium", "Every driver" });
        _cmbVideos.SelectedIndex = Math.Clamp((int)_audio.HighlightVideos, 0, 3);
        _cmbVideos.SelectedIndexChanged += (_, _) => _audio.HighlightVideos = (HighlightVideo.Who)Math.Max(0, _cmbVideos.SelectedIndex);

        _txtVideoHandle.PlaceholderText = "@yourclub";
        _txtVideoHandle.Text = _audio.VideoHandle;
        _txtVideoHandle.TextChanged += (_, _) => _audio.VideoHandle = _txtVideoHandle.Text;

        Check(_chkShareVideos, "Put the podium's videos on the club's page, with a QR code on the Big Screen to download them");
        _chkShareVideos.Checked = _audio.SharePodiumVideos;
        _chkShareVideos.CheckedChanged += (_, _) => _audio.SharePodiumVideos = _chkShareVideos.Checked;

        // A DRIVER'S OWN CLIP ON THEIR NIGHT (22 Oct 2026, driver
        // experience): a recording of a named person, so off until the
        // club ticks it - see ClipShare and RelayPublisher.Clips.
        Check(_chkShareClips, "Put each driver's clip on their own night on the club's page");
        _chkShareClips.Checked = _audio.ShareDriverClips;
        _chkShareClips.CheckedChanged += (_, _) => _audio.ShareDriverClips = _chkShareClips.Checked;

        _txtAudioFolder.ReadOnly = true;
        _txtAudioFolder.Text = _audio.Folder;

        _btnAudioFolder.Text = "Choose...";
        _btnAudioFolder.Click += (_, _) => PickAudioFolder();

        _btnAudioOpen.Text = "Open folder";
        _btnAudioOpen.Click += (_, _) => OpenAudioFolder();

        _numKeepNights.Minimum = 1;
        _numKeepNights.Maximum = 104;
        _numKeepNights.Value = Math.Clamp(_audio.KeepNights, 1, 104);
        _numKeepNights.ValueChanged += (_, _) =>
        {
            _audio.KeepNights = (int)_numKeepNights.Value;
            ShowWhatIsOnDisk();
        };

        var audioGrid = new FieldGrid();

        Stepped(audioGrid.AddRow("Highlight videos:", Line(_cmbVideos, Suffix("an upright video with captions, for a driver's phone"))));
        Stepped(audioGrid.AddRow("Club handle:", Line(_txtVideoHandle, Suffix("shown at the end of each video"))));
        Stepped(audioGrid.AddRow("Keep the last:", Line(_numKeepNights, Suffix("race nights - older nights' folders are removed"))));
        Stepped(audioGrid.AddRow("Folder:", Line(_txtAudioFolder, _btnAudioFolder, _btnAudioOpen)));
        audioGrid.AddHint(_lblOnDisk);

        var audio = new Section("Recording the night");

        audio.Add(Hint(
            "Records the announcer and effects (not the music or crowd). A driver's clip is an mp3 of the moments they were "
            + "called. One folder per night, with a Highlights folder inside."));
        audio.Add(_chkRecord, stretch: false);
        audio.Add(_chkClips, stretch: false, gapBelow: UiLayout.HintGap);
        audio.Add(Hint(
            "Recordings are NOT in backups (a night is a few hundred MB). Copy a night's folder elsewhere to keep it."));
        audio.Add(audioGrid);
        audio.Add(_chkShareVideos, stretch: false, gapBelow: UiLayout.HintGap);
        audio.Add(Hint(
            "Only 1st to 3rd, and only those with a video. Needs the club's page on (Club Website); kept online for a week."));
        audio.Add(_chkShareClips, stretch: false, gapBelow: UiLayout.HintGap);
        audio.Add(Hint(
            "Only for that driver: their clip is listed on their own night, which only their badge code opens - never on a "
            + "public page. Needs drivers' nights on (Club Website); kept online for a week."),
            gapBelow: 0);

        // ------------------------------------------------------------------
        // Written from the night - each needs the announcer's model, so
        // AI commentary on (see UpdateEnabledState).
        // ------------------------------------------------------------------

        // THE NIGHT IN FOUR MINUTES, here because what a club wants out of
        // it is the file - it lands in the same night's folder as the races
        // and the bookends. See NightRecap.
        _btnNightRecap.Text = NightRecapCaption;
        _btnNightRecap.Click += async (_, _) => await PlayTonightsRecapAsync();

        var recapGrid = new FieldGrid();

        recapGrid.AddHint(
            "After the last race: the evening as a short piece, played once with your opener and bed and saved in "
            + "tonight's folder as \"The Night\".");
        Flush(recapGrid.AddRow("Tonight's recap:", Line(Steady(_btnNightRecap, NightRecapCaption), _lblNightRecap)));

        // ONE DRIVER'S NIGHT, here for the recap's reason: what a club wants
        // out of it is the file, and it lands in the same night's folder.
        // See DriverCut.
        _cmbDriverCut.DropDownStyle = ComboBoxStyle.DropDownList;

        // FILLED WHEN IT IS OPENED, so it is never a list from an hour
        // ago - a club runs races between pressing this and pressing it
        // again. Filled once below as well, so the box is not empty the
        // first time somebody looks at the card.
        _cmbDriverCut.DropDown += (_, _) => FillDriversTonight();

        _btnDriverCut.Text = DriverCutCaption;
        _btnDriverCut.Click += async (_, _) => await PlayADriversNightAsync();

        var cutGrid = new FieldGrid();

        cutGrid.AddHint(
            "About ninety seconds on one driver's races and pace, played once and saved in tonight's folder under "
            + "their name.");

        Flush(cutGrid.AddRow(
            "A driver's night:",
            Line(_cmbDriverCut, Steady(_btnDriverCut, DriverCutCaption), _lblDriverCut)));

        // AND WHY IT SAID THAT. Every claim the announcer makes is worked
        // out from the club's own results. See NightAccount.Proof.
        _btnProof.Text = ProofCaption;
        _btnProof.Click += (_, _) => ShowTheProof();

        var proofGrid = new FieldGrid();

        proofGrid.AddHint(
            "Lists the facts from the club's results that the last segment was built from, in the developer console "
            + "(under Help).");

        Flush(proofGrid.AddRow("Check a line:", Line(Steady(_btnProof, ProofCaption), _lblProof)));

        // THE NIGHT BEFORE. The one thing this app writes on a day it is
        // otherwise shut, and the only one whose job is getting people to
        // the track rather than entertaining the people already at it.
        // See NightBefore.
        _btnNightBefore.Text = NightBeforeCaption;
        _btnNightBefore.Click += async (_, _) => await WriteTheNightBeforeAsync();

        var beforeGrid = new FieldGrid();

        beforeGrid.AddHint(
            "A short preview of the next race day from the program and entry list, saved as a text file to paste "
            + "into the club's page or group. It never predicts winners or grids.");

        Flush(beforeGrid.AddRow("The night before:", Line(Steady(_btnNightBefore, NightBeforeCaption), _lblNightBefore)));

        // PUBLISHING IS A SEPARATE YES. The button has saved a file since
        // this shipped, and a club that has been pressing it all season
        // for something to paste into its own group must not find that an
        // update started putting the same words on a public address. See
        // AppSettings.PublishNightBeforeToTheRelay.
        Check(_chkNightBeforeRelay, "Also put it on the club's own web page");
        _chkNightBeforeRelay.Checked = _settings.PublishNightBeforeToTheRelay;
        _chkNightBeforeRelay.CheckedChanged += (_, _) =>
        {
            _settings.PublishNightBeforeToTheRelay = _chkNightBeforeRelay.Checked;
            _settings.Save();

            // TAKEN DOWN when it goes off, like the notes, the vote and
            // listening along. Otherwise a preview the club has just said
            // it does not want public stays on its page.
            if (!_chkNightBeforeRelay.Checked)
            {
                NightBeforeClosedOnTheRelay?.Invoke();
            }
        };

        beforeGrid.AddHint(
            "The file is saved either way. On the page it shows at the top and comes down by itself a few hours "
            + "after the first race, even with this PC off.");

        Flush(beforeGrid.AddRow(string.Empty, _chkNightBeforeRelay));

        var written = new Section("Written from the night");

        written.Add(Hint(
            "Needs AI commentary on (Voice > AI Commentary)."),
            gapBelow: UiLayout.RowGap);
        written.Add(recapGrid, gapBelow: UiLayout.SectionGap);
        written.Add(cutGrid, gapBelow: UiLayout.SectionGap);
        written.Add(proofGrid, gapBelow: UiLayout.SectionGap);
        written.Add(beforeGrid, gapBelow: 0);

        FillDriversTonight();

        ShowWhatIsOnDisk();

        // ------------------------------------------------------------------
        // The page
        // ------------------------------------------------------------------

        var page = new PageStack();

        page.Add(audio);
        page.Add(written, gapBelow: 0);

        Controls.Add(page);

        // One caption column down both cards. Every grid on the page - a
        // grid left out of this still looks correct on its own.
        _textSize.Keep(() => FieldGrid.LineUp(new[] { audioGrid, recapGrid, cutGrid, proofGrid, beforeGrid }));

        _textSize.Keep(SizeFields);

        UpdateEnabledState();
    }

    /// <summary>The driver picker and the number box at the text size in use.</summary>
    private void SizeFields()
    {
        Fit(_cmbDriverCut, UiLayout.AtTextSize(DriverPickerWidth));
        Fit(_cmbVideos, UiLayout.AtTextSize(DriverPickerWidth));
        Fit(_txtVideoHandle, UiLayout.AtTextSize(DriverPickerWidth));
        Fit(_numKeepNights, NumberWidth(_numKeepNights));

        // Only when it changed: a new size lays the grid or the line out again.
        static void Fit(Control field, int width)
        {
            if (field.Width != width)
            {
                field.Width = width;
            }
        }
    }

    /// <summary>
    /// A number box as wide as its largest value and its arrows need in its
    /// own font, and never narrower than a number box was at the normal text
    /// size, at this size.
    /// </summary>
    private static int NumberWidth(NumericUpDown number)
    {
        var digits = decimal.Truncate(number.Maximum).ToString(System.Globalization.CultureInfo.InvariantCulture).Length;

        return Math.Max(
            UiLayout.AtTextSize(UiLayout.NumericWidth),
            TextRenderer.MeasureText(new string('0', digits), number.Font).Width + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra);
    }

    private void UpdateEnabledState()
    {
        // RECORDING IS NOT GATED ON THE ANNOUNCER (11 Oct 2026). A club
        // that runs no commentary at all still has music, effects, flag
        // calls and lap times going out of this PC, and a recording of that
        // is a recording of their night.
        //
        // Open folder stays live whatever the switches say: somebody
        // turning recording off is quite likely to want the files they
        // already have.
        var recording = _chkRecord.Checked;

        _chkClips.Enabled = recording;
        // The videos are made from the drivers' clips, so with clips off
        // these do nothing (22 Oct 2026, bug hunt).
        var clips = recording && _chkClips.Checked;
        _cmbVideos.Enabled = clips;
        _txtVideoHandle.Enabled = clips;
        _chkShareVideos.Enabled = clips;
        _chkShareClips.Enabled = clips;
        _txtAudioFolder.Enabled = recording;
        _btnAudioFolder.Enabled = recording;
        _numKeepNights.Enabled = recording;

        var on = IsCommentaryOn;

        // THE RECAP NEEDS THE ANNOUNCER, not the music. With no commentary
        // at all there is nothing to write it with - and a button that
        // answers "the commentary isn't set up" every time is a button that
        // should have been greyed out.
        _btnNightRecap.Enabled = !_nightRecapRunning && on;

        // THE SAME RULE for the two beside it: both need the announcer and
        // neither needs the music. A driver with no race filed and a night
        // with no segment said yet are both answered in words by the
        // handlers rather than by a greyed-out button.
        _btnDriverCut.Enabled = !_driverCutRunning && on;
        _cmbDriverCut.Enabled = !_driverCutRunning && on;
        _btnProof.Enabled = on;

        // THE SAME RULE for the preview. It is never spoken, but it is
        // written by the announcer's model, and with commentary unticked
        // there is no model to write it with.
        _btnNightBefore.Enabled = !_nightBeforeRunning && on;
    }

    /// <summary>
    /// Where the recordings go. A folder the club picks, or the default
    /// under Documents.
    ///
    /// The box is READ-ONLY and this is the only way to change it, which is
    /// deliberate: a typed path can be a network share, and the store's own
    /// note explains at length why merely asking Windows whether one exists
    /// hands this account's credentials to whoever runs that server. The
    /// store refuses one anyway; this means a club never gets as far as
    /// typing it.
    /// </summary>
    private void PickAudioFolder()
    {
        try
        {
            using var dialog = new FolderBrowserDialog
            {
                SelectedPath = _audio.Folder,
                Description = "Choose a folder for the race recordings",
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            _audio.FolderSetting = dialog.SelectedPath;
            _txtAudioFolder.Text = _audio.Folder;

            ShowWhatIsOnDisk();
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't change the recordings folder: {ex.Message}",
                "Folder not changed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Opens the recordings folder, making it first if no race has been
    /// recorded yet - which is the usual state of it the first time
    /// somebody presses this, and an error about a missing folder would
    /// read as the feature being broken.
    /// </summary>
    private void OpenAudioFolder()
    {
        var folder = _audio.Folder;

        try
        {
            Directory.CreateDirectory(folder);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't open {folder}: {ex.Message}",
                "Folder not opened", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// What is in the recordings folder now.
    ///
    /// A club cannot decide how many nights to keep without knowing what a
    /// night costs. Asked when the page is built and when the count changes -
    /// never on a timer: it walks the folder, and a settings page that
    /// measured a few gigabytes twice a second would be the reason a club
    /// PC was busy.
    /// </summary>
    private void ShowWhatIsOnDisk()
    {
        var (nights, megabytes) = _audio.OnDisk();

        _lblOnDisk.Text = nights == 0
            ? "Nothing recorded yet. A folder appears here after the first race of a night."
            : $"On disk now: {Plural.Of(nights, "night")}, about {megabytes:n0} MB.";
    }

    /// <summary>
    /// Presses it: writes the recap, plays it, and says what happened.
    ///
    /// The button stays down for the whole thing - the writing AND the
    /// playing, which together is a couple of minutes - so nobody starts
    /// a second recap over the first.
    /// </summary>
    private async Task PlayTonightsRecapAsync()
    {
        if (PlayTonightsRecap is null)
        {
            return;
        }

        _nightRecapRunning = true;
        _btnNightRecap.Enabled = false;
        _btnNightRecap.Text = "Writing...";
        _lblNightRecap.Text = string.Empty;
        _lblNightRecap.ForeColor = AppTheme.TextMuted;

        try
        {
            var problem = await PlayTonightsRecap.Invoke();

            if (IsDisposed)
            {
                return;
            }

            _lblNightRecap.Text = problem
                ?? "Played, and saved in tonight's folder as \"The Night\". Open folder above to find it.";
            _lblNightRecap.ForeColor = problem is null ? AppTheme.TextMuted : AppTheme.Accent;
        }
        catch (Exception ex)
        {
            _lblNightRecap.Text = ex.Message;
            _lblNightRecap.ForeColor = AppTheme.Accent;
        }
        finally
        {
            _nightRecapRunning = false;

            if (!IsDisposed)
            {
                // The switch, not simply back on: commentary may have been
                // turned off while it ran.
                _btnNightRecap.Enabled = IsCommentaryOn;
                _btnNightRecap.Text = NightRecapCaption;
            }
        }
    }

    /// <summary>
    /// Fills the driver picker with tonight's drivers, keeping whoever was
    /// picked where they are still in the list.
    ///
    /// KEEPS THE SELECTION, because this runs every time the box is opened:
    /// a club that picked Bill, read the hint and opened the box again must
    /// not find it back on the first name. The list beside the box is
    /// rebuilt with it, so an index never points at the wrong driver.
    /// </summary>
    private void FillDriversTonight()
    {
        var wanted = _cmbDriverCut.SelectedIndex >= 0 && _cmbDriverCut.SelectedIndex < _driversTonight.Count
            ? _driversTonight[_cmbDriverCut.SelectedIndex].Transponder
            : string.Empty;

        var tonight = DriversTonight?.Invoke() ?? Array.Empty<(string Transponder, string Name)>();

        var fresh = tonight
            .Where(one => !string.IsNullOrWhiteSpace(one.Transponder) && !string.IsNullOrWhiteSpace(one.Name))
            .ToList();

        // Only when it has actually changed: refilling a ComboBox closes
        // its own drop-down, and this runs from the event that opens it.
        if (fresh.Count == _driversTonight.Count
            && fresh.Select(one => one.Transponder).SequenceEqual(
                _driversTonight.Select(one => one.Transponder), StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _driversTonight.Clear();
        _driversTonight.AddRange(fresh);

        _cmbDriverCut.Items.Clear();

        foreach (var one in _driversTonight)
        {
            _cmbDriverCut.Items.Add(one.Name.Trim());
        }

        if (_cmbDriverCut.Items.Count == 0)
        {
            return;
        }

        var back = wanted.Length == 0
            ? -1
            : _driversTonight.FindIndex(one =>
                string.Equals(one.Transponder, wanted, StringComparison.OrdinalIgnoreCase));

        _cmbDriverCut.SelectedIndex = back >= 0 ? back : 0;
    }

    /// <summary>
    /// Writes and plays the picked driver's own night - see
    /// <see cref="DriverCut"/>.
    ///
    /// The same shape as the recap's handler above: the button says what it
    /// is doing while it does it, and the label says what happened either
    /// way, because a button that goes quiet is a button a club presses
    /// again.
    /// </summary>
    private async Task PlayADriversNightAsync()
    {
        if (PlayADriversNight is null)
        {
            return;
        }

        if (_cmbDriverCut.SelectedIndex < 0 || _cmbDriverCut.SelectedIndex >= _driversTonight.Count)
        {
            _lblDriverCut.Text = _driversTonight.Count == 0
                ? "Nobody has a race filed for tonight yet."
                : "Pick a driver first.";
            _lblDriverCut.ForeColor = AppTheme.Accent;

            return;
        }

        var (transponder, name) = _driversTonight[_cmbDriverCut.SelectedIndex];

        _driverCutRunning = true;
        _btnDriverCut.Enabled = false;
        _cmbDriverCut.Enabled = false;
        _btnDriverCut.Text = "Writing...";
        _lblDriverCut.Text = string.Empty;
        _lblDriverCut.ForeColor = AppTheme.TextMuted;

        try
        {
            var problem = await PlayADriversNight.Invoke(transponder, name);

            if (IsDisposed)
            {
                return;
            }

            _lblDriverCut.Text = problem
                ?? $"Played, and saved in tonight's folder as \"{DriverCut.LabelFor(name)}\". "
                   + "Open folder above to find it.";
            _lblDriverCut.ForeColor = problem is null ? AppTheme.TextMuted : AppTheme.Accent;
        }
        catch (Exception ex)
        {
            _lblDriverCut.Text = ex.Message;
            _lblDriverCut.ForeColor = AppTheme.Accent;
        }
        finally
        {
            _driverCutRunning = false;

            if (!IsDisposed)
            {
                var on = IsCommentaryOn;

                _btnDriverCut.Enabled = on;
                _cmbDriverCut.Enabled = on;
                _btnDriverCut.Text = DriverCutCaption;
            }
        }
    }

    /// <summary>
    /// Writes the last segment's facts out to the developer console - see
    /// <see cref="NightAccount.Proof"/>.
    ///
    /// NOT INTO THE LABEL, which is one line beside a button: the proof is
    /// several facts and the console is where this app puts anything a club
    /// reads a few lines of. The label says where it went.
    /// </summary>
    private void ShowTheProof()
    {
        if (ProofOfTheLastLine is null)
        {
            return;
        }

        try
        {
            _lblProof.Text = ProofOfTheLastLine.Invoke();
            _lblProof.ForeColor = AppTheme.TextMuted;
        }
        catch (Exception ex)
        {
            _lblProof.Text = ex.Message;
            _lblProof.ForeColor = AppTheme.Accent;
        }
    }

    /// <summary>
    /// Writes the next race day's preview and says where it went.
    ///
    /// The button is held at its own width while the caption says
    /// "Writing..." - see Steady - because a button that shrinks under the
    /// pointer re-lays the row it is in.
    /// </summary>
    private async Task WriteTheNightBeforeAsync()
    {
        if (WriteTheNightBefore is null)
        {
            return;
        }

        _nightBeforeRunning = true;
        _btnNightBefore.Enabled = false;
        _btnNightBefore.Text = "Writing...";
        _lblNightBefore.Text = string.Empty;

        try
        {
            var said = await WriteTheNightBefore.Invoke();

            _lblNightBefore.Text = said ?? "The preview was written.";
            _lblNightBefore.ForeColor = AppTheme.TextMuted;
        }
        catch (Exception ex)
        {
            _lblNightBefore.Text = ex.Message;
            _lblNightBefore.ForeColor = AppTheme.Accent;
        }
        finally
        {
            _nightBeforeRunning = false;
            _btnNightBefore.Text = NightBeforeCaption;

            // Back to what UpdateEnabledState says rather than to true, so
            // commentary unticked while it was writing leaves it greyed.
            _btnNightBefore.Enabled = IsCommentaryOn;
        }
    }
}
