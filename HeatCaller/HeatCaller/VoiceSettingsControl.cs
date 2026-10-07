namespace HeatCaller;

/// <summary>
/// Voice settings: which voice speaks, how it is tuned, and the library of
/// lines it has already rendered. Controls are built in code (no separate
/// .Designer.cs) - same approach as ProfileForm.
/// </summary>
/// <remarks>
/// The per-announcement rows used to be the fifth group at the bottom of
/// this page. They are now a page of their own, built by the other half of
/// this partial class in VoiceSettingsControl.Announcements.cs and handed
/// to the host through AnnouncementsPage for a tab of its own. The class
/// stays whole because the rows are part of the same VoiceProfile this page
/// saves; that file's remarks explain the choice.
/// </remarks>
public sealed partial class VoiceSettingsControl : UserControl
{
    private readonly ElevenLabsClient _elevenLabs = new();
    private readonly VoiceProfile _original;

    /// <summary>
    /// The live announcer, when the host wires it in. Test playback then
    /// goes through its output lock, so a test pressed during a session
    /// waits for the current call instead of playing on top of it. Optional:
    /// without it tests still play, just unsynchronised (the old behaviour).
    /// Synthesis goes through the shared concurrency gate either way.
    /// </summary>
    public AnnouncementService? Announcer { get; set; }

    private Task PlayTestAudioAsync(byte[] audio, double volume) =>
        Announcer is { } announcer
            ? announcer.PlayExclusiveAsync(audio, volume)
            : AnnouncementService.PlayAsync(audio, volume);
    private readonly Label _lblSaveStatus = new();

    // --- Speak With (see BuildEngineCard) ---
    private readonly RadioButton _rdoHeatCaller = new ThemedRadioButton();
    private readonly ComboBox _cmbHeatCallerVoice = new();
    private readonly Button _btnHeatCallerRefresh = new();
    private readonly Label _lblHeatCallerStatus = new();

    // A relay for the voice alone (advanced) - see the Voice relay row in
    // BuildEngineCard. Blank in every ordinary club.
    private readonly TextBox _txtVoiceRelayUrl = new();
    private readonly TextBox _txtVoiceRelayClub = new();
    private readonly TextBox _txtVoiceRelayToken = new();
    private readonly RadioButton _rdoElevenLabs = new ThemedRadioButton();
    private readonly RadioButton _rdoWindows = new ThemedRadioButton();
    private readonly CheckBox _chkWindowsStandsIn = new ThemedCheckBox();
    private readonly ComboBox _cmbWindowsVoice = new();
    private readonly Button _btnTestWindowsVoice = new();
    private readonly Label _lblWindowsVoiceStatus = new();

    /// <summary>While the Windows voice test is speaking, so a change of engine doesn't re-enable its button part-way.</summary>
    private bool _windowsTestRunning;

    // --- Kokoro: free and natural, on this PC (see KokoroSpeech) ---
    private readonly RadioButton _rdoKokoro = new ThemedRadioButton();
    private readonly ComboBox _cmbKokoroVoice = new();
    private readonly Button _btnTestKokoroVoice = new();
    private readonly Button _btnDownloadKokoro = new();
    private readonly Label _lblKokoroStatus = new();

    /// <summary>While the Kokoro test is speaking - as _windowsTestRunning.</summary>
    private bool _kokoroTestRunning;

    /// <summary>The model download this page is showing, while it runs. KokoroSpeech holds it; its button cancels it.</summary>
    private Task? _kokoroDownload;

    /// <summary>How this page hears the download's progress - taken off KokoroSpeech when the page goes.</summary>
    private Action<double>? _kokoroProgress;

    /// <summary>What the Prepare row says while a voice on this PC is chosen - and how the page knows it said it.</summary>
    private const string PrepareNotNeededText = "Not needed with the Windows or Kokoro voice - they speak each line on this PC as it's wanted.";

    private readonly TextBox _txtApiKey = new();
    private readonly CheckBox _chkShowApiKey = new ThemedCheckBox();
    private readonly ComboBox _cmbVoice = new();

    /// <summary>
    /// The entry Load Voices adds when the voice that was chosen is not in
    /// the list the key's account sends back, and the voice's real name
    /// without the "(not in this account)" note. Kept so the page goes on
    /// showing the voice that is saved rather than a blank box, and so
    /// BuildProfileFromControls saves the real name, not the note.
    /// </summary>
    private ElevenLabsClient.VoiceInfo? _voiceNotInAccount;
    private string _voiceNotInAccountName = string.Empty;

    private readonly Button _btnLoadVoices = new();
    private readonly ComboBox _cmbModel = new();

    private readonly NumericUpDown _numStability = new();
    private readonly NumericUpDown _numSimilarity = new();
    private readonly NumericUpDown _numStyle = new();
    private readonly NumericUpDown _numSpeed = new();
    private readonly CheckBox _chkSpeakerBoost = new ThemedCheckBox();
    private readonly ComboBox _cmbTextNormalization = new();
    private readonly TextBox _txtLanguageCode = new();
    private readonly TextBox _txtOutputFormat = new();
    private readonly NumericUpDown _numVolumePercent = new();
    private readonly CheckBox _chkWirelessSpeaker = new ThemedCheckBox();
    private readonly NumericUpDown _numMaxPerLap = new();
    private readonly NumericUpDown _numStreakEveryN = new();
    private readonly CheckBox _chkOnlyLongestStreak = new ThemedCheckBox();
    private readonly CheckBox _chkMentionStreakChaser = new ThemedCheckBox();
    private readonly NumericUpDown _numPositionEveryLaps = new();
    private readonly NumericUpDown _numPositionDepth = new();
    private readonly NumericUpDown _numLapTimeEveryN = new();
    private readonly Dictionary<AnnouncementEventType, NumericUpDown> _eventWeights = new();
    private readonly Dictionary<AnnouncementEventType, NumericUpDown> _eventSpeeds = new();
    private readonly Dictionary<AnnouncementEventType, CheckBox> _eventCaution = new();
    private readonly Label _lblAudioTags = new();
    private readonly Button _btnTestVoice = new();
    private readonly Label _lblTestStatus = new();

    // --- Saved voice lines (see VoiceLibrary) ---
    private readonly Label _lblLibraryStatus = new();
    private readonly Button _btnPrepareLines = new();
    private readonly Button _btnStopPreparing = new();
    private readonly Label _lblPrepareProgress = new();
    private readonly ComboBox _cmbSavedLines = new();
    private readonly Button _btnForgetLine = new();
    private readonly Button _btnForgetAll = new();

    /// <summary>Set while a Prepare run is going; Stop cancels it.</summary>
    private CancellationTokenSource? _prepareCancel;

    /// <summary>
    /// Coalesces refreshes of the library's count and list. A race saves a
    /// line every few seconds, and the folder does not need reading for each.
    /// </summary>
    private readonly System.Windows.Forms.Timer _libraryRefreshTimer = new() { Interval = 800 };
    private bool _libraryRefreshRunning;

    /// <summary>A refresh was asked for while one was still reading; it runs again when that one finishes.</summary>
    private bool _libraryRefreshAgain;

    /// <summary>How many of the most recently played lines the list offers to forget.</summary>
    private const int RecentLinesShown = 40;

    /// <summary>
    /// One entry per announcement row on the Announcements page.
    ///
    /// Two tick boxes, not one. There used to be a single Enabled box and an
    /// "Editing: Race / Practice" combo that swapped what it meant, with the
    /// other context's value parked in a dictionary - so the page had a mode,
    /// the mode was invisible unless you read the combo, and a rebuild while
    /// editing practice showed race values. Both contexts are now on the row,
    /// which is how Sound Effects has always done it.
    /// </summary>
    private readonly Dictionary<AnnouncementEventType, (CheckBox Race, CheckBox Practice, TextBox Template)> _eventRows = new();

    /// <summary>
    /// Every per-announcement Test button. Kept so the HeatCaller voice can
    /// take them away: each press is a line rendered on HeatCaller's
    /// ElevenLabs account, and a page of thirty of them is an invitation to
    /// spend somebody else's money. See UpdateEngineControls.
    /// </summary>
    private readonly List<Button> _eventTestButtons = new();

    /// <summary>The inline Restore Default Lines button, so it can be switched off with the wording it restores.</summary>
    private Button? _btnRestoreLines;
    private readonly CheckBox _chkIncludeLapTime = new ThemedCheckBox();
    private readonly CheckBox _chkOnlyBestLap = new ThemedCheckBox();
    private readonly CheckBox _chkSayLapTimeLabel = new ThemedCheckBox();
    private readonly CheckBox _chkIncludeDriverNameInLapTime = new ThemedCheckBox();
    private readonly CheckBox _chkFastLapIncludeTime = new ThemedCheckBox();

    private readonly VoiceSettingsStore _store;

    /// <summary>
    /// The app's own settings, for the three voice-relay boxes only.
    ///
    /// Everything else on this page belongs to the voice profile and is
    /// saved through _store. Where the voice relay is does not: it sits
    /// beside the publishing relay's address in AppSettings, because it is
    /// the same kind of thing and because the token has to go through the
    /// same sealing on the way to disk. Passed in rather than set
    /// afterwards, so it cannot be null while the constructor is filling
    /// those boxes in.
    /// </summary>
    private readonly AppSettings _appSettings;

    /// <summary>
    /// The app's undo stack, for Restore Default Lines and forgetting saved
    /// lines. Optional so an older host that doesn't pass one still builds;
    /// those actions then simply aren't undoable.
    /// </summary>
    private readonly UndoService? _undo;

    /// <summary>What this page keeps in step with the text size - see TextSize. The Announcements page keeps its own (AnnouncementsHost).</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The pickers' widths as last measured - see Pieces.PickerWidth. Forgotten when their lists are refilled (RefitPickers).</summary>
    private (Font Font, int Count, int Width)? _heatCallerVoiceWidth;
    private (Font Font, int Count, int Width)? _windowsVoiceWidth;
    private (Font Font, int Count, int Width)? _kokoroVoiceWidth;
    private (Font Font, int Count, int Width)? _voiceWidth;
    private (Font Font, int Count, int Width)? _modelWidth;
    private (Font Font, int Count, int Width)? _normalizationWidth;
    private (Font Font, int Count, int Width)? _savedLinesWidth;

    /// <summary>
    /// The widest the three voice pickers get, at the normal text size. A
    /// picker is as wide as its longest voice up to this, so a voice with a
    /// long description can't push its row past the card at 1280x720 or at
    /// the largest text size; its open list is as wide as the longest voice
    /// whatever this is (SizeFields).
    /// </summary>
    private const int PickerMostWidth = 640;

    /// <summary>Fields sized for what goes in them, at the normal text size - see SizeFields.</summary>
    private const int RelayClubWidth = 150;
    private const int UnitSpinnerWidth = 80;
    private const int NormalizationPickerWidth = 84;
    private const int LanguageBoxWidth = 64;
    private const int OutputFormatBoxWidth = 200;

    /// <summary>The least room Prepare's progress keeps beside its buttons, at the normal text size, before it takes a line of its own.</summary>
    private const int PrepareProgressLeastWidth = 240;

    public VoiceSettingsControl(VoiceSettingsStore store, AppSettings settings, UndoService? undo = null)
    {
        _loadingSettings = true;
        _store = store;
        _appSettings = settings;
        _undo = undo;

        // Which relay a HeatCaller line was rendered on is part of its saved
        // key, and these are the settings that say. Set here because this
        // page is built once at startup and is the one place that already
        // holds them; the same object every time, so a page rebuilt on a
        // theme switch changes nothing.
        VoiceLibrary.VoiceRelaySettings = settings;

        var current = store.Current;
        _original = current;

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize. The page sits in a tab whose row is drawn in
        // the bold face, which it would otherwise take.
        //
        // No MinimumSize: the 660x720 floor was honoured over the size the
        // page was given, and a small window lost the bottom of the page. A
        // short window scrolls it instead (PageStack).
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout(current);

        // No Save button. Every other settings page here saves as you go,
        // and this one didn't - so a model or template change could look
        // applied while races kept using the old saved profile. That is
        // exactly the test-sounds-right-but-races-don't problem.
        //
        // This call MUST come after the cards are added to this control.
        // It walks the tree from here down, and it used to run while
        // Controls was still empty - so it subscribed to nothing, and the
        // page never saved anything at all. The API key, the voice, the
        // model and every event toggle stayed on screen and never reached
        // the profile races read, which is exactly the bug that removing
        // the Save button was meant to prevent.
        //
        // Guarded during any later rebuild - otherwise filling a box counts
        // as an edit and the page saves dozens of times while it loads.
        //
        // The saved-lines list is not a setting: picking a line in it must
        // not save the profile. Marked as already wired so the walk below
        // passes over it.
        _autoSaveWired.Add(_cmbSavedLines);

        // Nor are the three voice-relay boxes: they are AppSettings, saved
        // by VoiceRelayBoxLeft when the box is left. Left to the walk below
        // they would save the VOICE PROFILE on every keystroke - harmless in
        // itself, but it would also stamp "Saved" over the page's status
        // line for a change that had not been written anywhere yet.
        _autoSaveWired.Add(_txtVoiceRelayUrl);
        _autoSaveWired.Add(_txtVoiceRelayClub);
        _autoSaveWired.Add(_txtVoiceRelayToken);

        // Nor the ElevenLabs boxes typed into - the API key, model, language
        // and output format. Saving makes the profile current at once, and
        // every live race line reads it, so saved per keystroke a lap or flag
        // call made mid-edit went to ElevenLabs with "eleven_v" or "mp3_44"
        // and was refused - spoken by the stand-in or skipped - and each
        // half-typed key earned a key-refusal rest. Like the relay boxes,
        // they are taken into use when left or on Enter; a model picked from
        // the list is whole, so that still saves at once.
        _autoSaveWired.Add(_txtApiKey);
        _autoSaveWired.Add(_cmbModel);
        _autoSaveWired.Add(_txtLanguageCode);
        _autoSaveWired.Add(_txtOutputFormat);

        _cmbModel.SelectedIndexChanged += (_, _) => AutoSave();

        foreach (var box in new Control[] { _txtApiKey, _cmbModel, _txtLanguageCode, _txtOutputFormat })
        {
            box.Leave += (_, _) => ElevenLabsBoxLeft();
            box.KeyDown += (_, e) =>
            {
                // An open model list is left to take Enter as its own pick.
                if (e.KeyCode == Keys.Enter && box is not ComboBox { DroppedDown: true })
                {
                    // No ding: a single-line box has nothing to do with Enter itself.
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    ElevenLabsBoxLeft();
                }
            };
        }

        WireAutoSave(this);

        // The Announcements page is not a child of this control - it lives
        // in a tab of its own - so the walk above never reaches it and its
        // rows have to be wired by name. Without this every edit on that
        // page would be silently unsaved.
        WireAutoSave(_announcementsPage);

        // Its Test button reports through the status line beside the global
        // one, which is now a different tab. See MirrorTestStatus.
        MirrorTestStatus();

        // Once every card exists: it reaches into the Saved Voice Lines
        // card as well as its own.
        UpdateEngineControls();

        // The Speak With status line mentions an ElevenLabs set up below
        // while the Windows voice is chosen - kept in step as it is typed.
        // Only when that answer actually changes: the same line shows the
        // last Windows voice test's result ("Done - that was Microsoft
        // Zira"), and rewriting it on every keystroke wiped it.
        _txtApiKey.TextChanged += (_, _) => ElevenLabsSetUpChanged();
        _cmbVoice.SelectedIndexChanged += (_, _) => ElevenLabsSetUpChanged();
        _loadingSettings = false;

        _libraryRefreshTimer.Tick += (_, _) => RefreshLibraryNow();

        // An undo puts lines back in the store, not on this page - see
        // OnStoreRestored.
        _store.Restored += OnStoreRestored;

        Status($"Voice Settings opened - saved profile: {Describe(current)}. File: {VoiceSettingsStore.SettingsPath}");
    }

    /// <summary>The page's own face changed - handed the body face at a new text size - so everything else it keeps in step follows. See TextSize.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>A new text size, reaching this page through its tab's bold face - the only way it does, since the page wears a face of its own. See TextSize.</summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>
    /// Builds the page on the layout pieces (LayoutPieces.cs), so nothing is
    /// placed by hand: four cards down a page that scrolls when it is taller
    /// than the window (PageStack), and under them, outside the scrolling,
    /// a strip that says what the last change did.
    ///
    ///   Speak with - the three voices, each with what belongs to it
    ///     indented under it: the HeatCaller voice (Announcer voice: and
    ///     Refresh, its status, the voice relay's three boxes and their
    ///     hint), ElevenLabs (the Windows voice standing in), the Windows
    ///     voice (Windows voice: and its Test, its status), then the hint.
    ///     The fields of all three share one caption column (LineUp).
    ///   ElevenLabs connection - API Key:, Voice:, Model:, and the hints.
    ///   Voice parameters - the ElevenLabs tuning in one caption column,
    ///     playback (volume, the most per lap) in another, then Test Voice
    ///     and what it said.
    ///   Saved voice lines - the count, Prepare and Stop with the progress
    ///     beside them, Recently played: with Forget Line and Forget All.
    ///
    /// The announcement rows are built here as well, as a page of their own
    /// that the host gives a tab (BuildAnnouncementsPage).
    ///
    /// It used to be placed in pixels: four group boxes 840px wide whatever
    /// the window, every row's x worked out from the widths of the captions
    /// before it, each box's height set once at build, the fields at the
    /// widths they had at the normal text size, the status lines cut short
    /// to stay inside their boxes, and a 660x720 floor the page kept however
    /// small its window.
    /// </summary>
    private void BuildLayout(VoiceProfile current)
    {
        // First, because it decides what everything below it is for.
        var (speak, speakGrids) = BuildEngineCard(current);

        var connection = BuildConnectionCard(current);
        var parameters = BuildParametersCard(current);

        // Between the voice and the lines it speaks: it is about both.
        var library = BuildLibraryCard();

        // The announcement rows used to be a fifth group here, below the
        // library - a long way below the fold on a page that already
        // scrolled. They are a page of their own now; the host asks for it
        // through AnnouncementsPage and gives it a tab.
        BuildAnnouncementsPage(current);

        var page = new PageStack();

        page.Add(speak);
        page.Add(connection);
        page.Add(parameters);
        page.Add(library, gapBelow: 0);

        // The save line lives in its own docked strip rather than at the
        // bottom of the cards, so it is on screen however far down the page
        // the change was made. One line, cut short with "..." rather than
        // wrapped: a save that failed says why in the console as well
        // (Status), in full.
        _lblSaveStatus.AutoSize = false;
        _lblSaveStatus.AutoEllipsis = true;
        _lblSaveStatus.Dock = DockStyle.Fill;
        _lblSaveStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblSaveStatus.ForeColor = AppTheme.TextMuted;

        var statusBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Padding = new Padding(UiLayout.PageLeft, 0, UiLayout.PageRight, 0),
        };

        statusBar.Controls.Add(_lblSaveStatus);

        // Fill first: docking resolves from the last child backwards, so
        // whatever is added first is left with the space the others did not
        // take.
        Controls.Add(page);
        Controls.Add(statusBar);

        // Once everything is on the page and wears its face - what it is
        // measured in.
        //
        // One caption column for the three voices' fields, so the voice
        // pickers and the relay's boxes line up down the card.
        _textSize.Keep(() => FieldGrid.LineUp(speakGrids));
        _textSize.Keep(SizeFields);

        // Room kept for what the status lines say, so a new message does not
        // move what is under it: two lines for each voice's status (a reason
        // it can't speak runs to two), one for the others. Each still grows
        // past that for more - they wrap now, where they used to be cut off
        // at the edge of a box of fixed height.
        _textSize.ReserveLines(_lblHeatCallerStatus, 2);
        _textSize.ReserveLines(_lblWindowsVoiceStatus, 2);
        _textSize.ReserveLines(_lblLibraryStatus, 1);
        _textSize.ReserveLines(_lblTestStatus, 1);

        // The strip: a line of the body face, a row gap above and below it.
        _textSize.Keep(() =>
        {
            var height = _lblSaveStatus.Font.Height + UiLayout.RowGap * 2;

            if (statusBar.Height != height)
            {
                statusBar.Height = height;
            }
        });
    }

    /// <summary>
    /// The fields' sizes at the text size in use. The pickers are as wide as
    /// their longest choice and never narrower than a field was at the normal
    /// text size - the three voice pickers no wider than PickerMostWidth, with
    /// their open lists as wide as the longest voice; the boxes are the widths
    /// they were at the normal text size, at this size; each number box is as
    /// wide as its largest value and its arrows need (NumberBoxWidth). The
    /// saved lines' open list is as wide as the longest line, so one can be
    /// read in full before it is forgotten.
    ///
    /// Run again when the text size changes (TextSize.Keep) and when a list
    /// is refilled (RefitPickers).
    /// </summary>
    private void SizeFields()
    {
        var field = UiLayout.AtTextSize(UiLayout.FieldWidth);
        var most = UiLayout.AtTextSize(PickerMostWidth);

        FitVoicePicker(_cmbHeatCallerVoice, ref _heatCallerVoiceWidth);
        FitVoicePicker(_cmbWindowsVoice, ref _windowsVoiceWidth);
        FitVoicePicker(_cmbKokoroVoice, ref _kokoroVoiceWidth);
        FitVoicePicker(_cmbVoice, ref _voiceWidth);

        Fit(_cmbModel, Math.Max(field, Pieces.PickerWidth(_cmbModel, ref _modelWidth)));
        Fit(_cmbTextNormalization, Math.Max(UiLayout.AtTextSize(NormalizationPickerWidth), Pieces.PickerWidth(_cmbTextNormalization, ref _normalizationWidth)));

        Fit(_txtVoiceRelayUrl, UiLayout.AtTextSize(UiLayout.WideFieldWidth));
        Fit(_txtVoiceRelayClub, UiLayout.AtTextSize(RelayClubWidth));
        Fit(_txtVoiceRelayToken, UiLayout.AtTextSize(UiLayout.WideFieldWidth));
        Fit(_txtApiKey, field);
        Fit(_txtLanguageCode, UiLayout.AtTextSize(LanguageBoxWidth));
        Fit(_txtOutputFormat, UiLayout.AtTextSize(OutputFormatBoxWidth));

        foreach (var spinner in new[] { _numStability, _numSimilarity, _numStyle, _numSpeed })
        {
            Fit(spinner, NumberBoxWidth(spinner, UnitSpinnerWidth));
        }

        Fit(_numVolumePercent, NumberBoxWidth(_numVolumePercent, UiLayout.NumericWidth));
        Fit(_numMaxPerLap, NumberBoxWidth(_numMaxPerLap, UiLayout.NumericWidth));

        OpenTo(_cmbSavedLines, Pieces.PickerWidth(_cmbSavedLines, ref _savedLinesWidth));

        void FitVoicePicker(ComboBox picker, ref (Font Font, int Count, int Width)? kept)
        {
            var wants = Pieces.PickerWidth(picker, ref kept);

            Fit(picker, Math.Min(most, Math.Max(field, wants)));
            OpenTo(picker, Math.Max(picker.Width, wants));
        }

        // Only when it changed: a new width lays the row out again.
        static void Fit(Control control, int width)
        {
            if (control.Width != width)
            {
                control.Width = width;
            }
        }

        static void OpenTo(ComboBox picker, int width)
        {
            var open = Math.Max(1, width);

            if (picker.DropDownWidth != open)
            {
                picker.DropDownWidth = open;
            }
        }
    }

    /// <summary>
    /// The pickers measured again after their lists changed - refilled from
    /// the relay, from ElevenLabs or from the saved lines - which can change
    /// the longest choice without changing how many there are.
    /// </summary>
    private void RefitPickers()
    {
        _heatCallerVoiceWidth = _windowsVoiceWidth = _kokoroVoiceWidth = _voiceWidth = _savedLinesWidth = null;
        SizeFields();
    }

    /// <summary>
    /// A number box wide enough to show its largest value beside its arrows,
    /// in the face it is drawn in - or the width it was at the normal text
    /// size, <paramref name="normalWidth"/>, at the size in use, if that is
    /// more. Every digit of the largest value is measured as a nought.
    /// </summary>
    private static int NumberBoxWidth(NumericUpDown box, int normalWidth)
    {
        var largest = box.Maximum.ToString("F" + box.DecimalPlaces);
        var noughts = new string(largest.Select(ch => char.IsDigit(ch) ? '0' : ch).ToArray());

        return Math.Max(
            UiLayout.AtTextSize(normalWidth),
            TextRenderer.MeasureText(noughts, box.Font).Width + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra);
    }

    /// <summary>
    /// Moves a part added to a card in, under the choice it belongs to - the
    /// voice picker under the HeatCaller voice, the stand-in under
    /// ElevenLabs - by TickBox.CaptionIndent: the usual indent, or as far in
    /// as the radio button's caption starts once its dot has grown past that
    /// with the text. Again whenever the text size changes. The card
    /// measures its parts inside their margins, so the part is narrower by
    /// the indent and wraps to that.
    /// </summary>
    private T Indented<T>(T part) where T : Control
    {
        _textSize.Keep(() =>
        {
            var indent = TickBox.CaptionIndent;

            // Only when it changed: a new margin lays the card out again.
            if (part.Margin.Left != indent)
            {
                part.Margin = new Padding(indent, part.Margin.Top, part.Margin.Right, part.Margin.Bottom);
            }
        });

        return part;
    }

    /// <summary>
    /// Shows what the store holds after an undo changed it behind this page -
    /// the announcements' lines, and the volume. Without this the next edit
    /// here would build a profile from the boxes and save the undone lines
    /// straight back.
    /// </summary>
    private void OnStoreRestored()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        SyncVolumeFromStore();
        RebuildAnnouncementRows();
    }

    /// <summary>
    /// Console output from this page, so a save (or a failed one) is visible
    /// in the log rather than only in a label at the bottom of the page.
    /// </summary>
    public event Action<string>? StatusMessage;

    private void Status(string message) => StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [voice] {message}");

    private static string Describe(VoiceProfile profile) =>
        $"speaking with {profile.EffectiveEngine switch
        {
            VoiceEngine.Windows => "the Windows voice",
            VoiceEngine.Kokoro => $"the Kokoro voice ({KokoroSpeech.Describe(string.IsNullOrWhiteSpace(profile.KokoroVoiceName) ? KokoroSpeech.DefaultVoice : profile.KokoroVoiceName)})",
            VoiceEngine.HeatCaller => $"the HeatCaller voice ({(profile.HeatCallerReady ? profile.HeatCallerVoice : "none chosen")})",
            _ => "ElevenLabs",
        }}"
        + (profile.EffectiveEngine == VoiceEngine.ElevenLabs
            ? $" (Windows voice stands in: {(profile.WindowsVoiceStandsIn ? "yes" : "no")})"
            : string.Empty)
        + $", Windows voice {(string.IsNullOrWhiteSpace(profile.WindowsVoiceName) ? "Windows default" : profile.WindowsVoiceName)}, "
        + $"API key {(string.IsNullOrWhiteSpace(profile.ApiKey) ? "MISSING" : "set")}, "
        + $"voice {(string.IsNullOrWhiteSpace(profile.VoiceId) ? "NONE" : profile.VoiceName + " (" + profile.VoiceId + ")")}, "
        + $"model {profile.ModelId}, "
        + $"{profile.Events.Values.Count(e => e.Enabled)}/{profile.Events.Count} announcements on for races "
        + $"(lines v{profile.TemplateVersion})";

    /// <summary>
    /// Replaces every event's spoken lines with the current built-in
    /// defaults, leaving enable/weight/volume settings alone.
    ///
    /// Scoped to the LINES only on purpose: someone restoring phrasings
    /// almost certainly doesn't also want their carefully tuned weights and
    /// per-lap caps reset, and bundling the two would make this button too
    /// risky to press.
    /// </summary>
    private void BtnRestoreLines_Click(object? sender, EventArgs e)
    {
        var defaults = VoiceProfile.CreateDefault();

        // The profile as last SAVED, not the one this page was built from.
        // Saving that construction-time copy put back every setting as it
        // stood at startup - each auto-saved edit since, and the volume the
        // Mixer page set, silently undone by restoring some phrasings.
        var latest = _store.Current;

        var changed = defaults.Events.Count(kv =>
            latest.Events.TryGetValue(kv.Key, out var mine)
            && !mine.Templates.SequenceEqual(kv.Value.Templates));

        if (changed == 0)
        {
            AppDialog.Show(this, "Your spoken lines already match the built-in defaults.",
                "Nothing to restore", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (AppDialog.Show(this,
                $"Replace the spoken lines for {Plural.Of(changed, "event type")} with the built-in defaults?\r\n\r\n"
                + "Any lines you've written or edited yourself will be lost. Enable/disable settings, "
                + "weights and volumes are NOT affected.",
                "Restore default lines", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        RestoreDefaultLines(null);

        // A message box rather than the status label at the bottom of the
        // page - on a narrower window that label is below the fold, so a
        // status update there would look like nothing happened.
        AppDialog.Show(this,
            $"Restored the built-in lines for {Plural.Of(changed, "event type")}.",
            "Restored", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// Puts every announcement's lines back to the built-in ones and
    /// rebuilds the rows. Shared by the Restore Default Lines button and by
    /// choosing the HeatCaller voice, which only speaks those lines.
    /// </summary>
    /// <param name="why">Said in the console when this wasn't a button press, or null for the button.</param>
    private void RestoreDefaultLines(string? why)
    {
        var defaults = VoiceProfile.CreateDefault();
        var latest = _store.Current;

        // Every line this is about to replace, so undo gives the club its
        // own wording back. Only the lines: whatever else is changed on the
        // page afterwards (ticks, weights, the voice) stays as it is then.
        if (_undo is not null)
        {
            var linesBefore = defaults.Events.Keys
                .Where(key => latest.Events.TryGetValue(key, out var config) && config is not null)
                .ToDictionary(key => key, key => (
                    Templates: (latest.Events[key].Templates ?? new List<string>()).ToList(),
                    Template: latest.Events[key].Template ?? string.Empty));

            var store = _store;
            _undo.Capture(why is null ? "Restore default announcer lines" : "Standard announcer lines for the HeatCaller voice", () =>
            {
                var profile = store.Current;

                // The HeatCaller voice only ever speaks the standard lines -
                // the account behind it is HeatCaller's (see HeatCallerChosen).
                // While it is the voice in use, the club's own wording must
                // not come back through an undo; after switching to another
                // voice it can.
                if (profile.EffectiveEngine == VoiceEngine.HeatCaller)
                {
                    throw new InvalidOperationException(
                        "Your own announcer lines can't come back while the HeatCaller voice is in use - it only speaks the standard lines.");
                }

                foreach (var (key, lines) in linesBefore)
                {
                    if (profile.Events.TryGetValue(key, out var config) && config is not null)
                    {
                        config.Templates = lines.Templates.ToList();
                        config.Template = lines.Template;
                    }
                }

                store.Save(profile);
                store.NotifyRestored();
            });
        }

        foreach (var (key, defaultConfig) in defaults.Events)
        {
            if (latest.Events.TryGetValue(key, out var existing))
            {
                // Replace the list rather than the whole config, so Enabled,
                // Weight and the per-event toggles survive untouched.
                existing.Templates = defaultConfig.Templates.ToList();

                // Clear the legacy single-template field as well. It's only
                // consulted when Templates is empty, but leaving stale text
                // there means it would silently resurface if the list were
                // ever emptied later.
                existing.Template = string.Empty;
            }
            else
            {
                latest.Events[key] = defaultConfig;
            }
        }

        _store.Save(latest);

        // Rebuild the event rows in place. This control is constructed once
        // at startup and only ever replaced on a theme switch - navigating
        // away and back does NOT reconstruct it, so telling the user to
        // "reopen the page" would have been advice that couldn't work.
        RebuildAnnouncementRows();

        if (why is not null)
        {
            Status($"The standard announcer lines are back - {why}.");
        }
    }

    /// <summary>
    /// Speak With: the HeatCaller voice, ElevenLabs or the free Windows
    /// voice, which voice of each, and whether the Windows voice stands in
    /// when ElevenLabs can't speak a line - each choice with what belongs to
    /// it indented under it. Returns the card, and the three caption-and-field
    /// grids in it, which BuildLayout lines up once they wear the page's face.
    ///
    /// The three choices go straight in the card, one row each, rather than
    /// into grids or rows of their own: radio buttons switch each other off
    /// only among the controls that share their parent.
    /// </summary>
    private (Section Card, FieldGrid[] Grids) BuildEngineCard(VoiceProfile current)
    {
        // The list as it stands, with a fresh read asked for in the
        // background - so a voice installed since the app started is offered
        // the next time this page is built, and building it never waits on
        // Windows speech.
        var voices = WindowsVoice.InstalledVoices(refresh: true);

        // --- The HeatCaller voice: included, and nothing to set up ---
        //
        // First because it is the one most clubs want: a natural voice with
        // no account, no key and no bill. Everything that costs a character
        // is fixed while it is chosen - the wording, the voice's settings,
        // the Test buttons - because the account behind it is HeatCaller's,
        // not the club's. A club that wants its own words uses its own key
        // or the free Windows voice, where both are theirs to spend.
        _rdoHeatCaller.Text = "HeatCaller voice - included with your HeatCaller setup. Nothing to sign up for, no key, no bill";
        _rdoHeatCaller.AutoSize = true;

        _cmbHeatCallerVoice.DropDownStyle = ComboBoxStyle.DropDownList;

        _btnHeatCallerRefresh.Text = "Refresh";
        _btnHeatCallerRefresh.Click += async (_, _) => await LoadHeatCallerCatalogueAsync(byHand: true);

        // The picker and Refresh side by side, Refresh under it when the row
        // is too narrow for both.
        var heatCaller = new FieldGrid();

        heatCaller.AddRow("Announcer voice:", FieldGrid.Inline(_cmbHeatCallerVoice, _btnHeatCallerRefresh));

        // The allowance, and why it can't be used when it can't - which can
        // run to a few lines, so the label wraps to the card rather than
        // being cut short.
        _lblHeatCallerStatus.AutoSize = true;
        _lblHeatCallerStatus.ForeColor = AppTheme.TextMuted;
        _lblHeatCallerStatus.Text = "Checking with the relay...";

        // --- A relay of your own, for the voice only (advanced) ---
        //
        // Left blank by every club: the voice then speaks through the relay
        // on the Club Website page, with the same club and token. Filled in, it
        // points the VOICE somewhere else - a relay running on this PC,
        // typically - while results, check-in and the race-day manager carry
        // on going where they always did. That separation is the point:
        // trying a relay out must not quietly move a club's live pages onto
        // somebody's laptop.
        _txtVoiceRelayUrl.PlaceholderText = "blank - use the relay on the Club Website page";
        _txtVoiceRelayClub.PlaceholderText = "same club";

        // Masked like every other token on this page: it is a credential,
        // and a settings page is often on a screen a room can see.
        _txtVoiceRelayToken.UseSystemPasswordChar = true;
        _txtVoiceRelayToken.PlaceholderText = "same token";

        var relay = new FieldGrid();

        relay.AddRow("Voice relay:", _txtVoiceRelayUrl);
        relay.AddRow("Club:", _txtVoiceRelayClub);
        relay.AddRow("Token:", _txtVoiceRelayToken);
        relay.AddHint(
            "Advanced - leave all three blank unless you are trying a relay out. Only the voice uses them; "
            + "results, check-in and the race-day manager keep going to the relay on the Club Website page. "
            + "A blank club means the Club Website page's own. A blank token borrows the Club Website page's only when this "
            + "is that same relay or a relay on this PC (localhost); any other address needs its token typed in.");

        // These three are AppSettings, not the voice profile - so they are
        // filled from, and saved to, a different place than everything else
        // on this page. They are also marked as already auto-save-wired in
        // the constructor, so the page's profile save never sees them.
        _txtVoiceRelayUrl.Text = _appSettings.VoiceRelayBaseUrl;
        _txtVoiceRelayClub.Text = _appSettings.VoiceRelayClub;
        _txtVoiceRelayToken.Text = _appSettings.VoiceRelayToken;
        _voiceRelaySaved = VoiceRelayShown();

        // Taken into use only when a box is left, or Enter is pressed in it -
        // never per keystroke. The voice client reads these on every line,
        // so a box applied as it was typed sent live race lines to every
        // half-typed host on the way to the real one ("h", "ht", ...
        // "relay.exam") and, with the token box, offered each of them a
        // half-typed token.
        //
        // The address decides which relay is asked, so the list and the
        // allowance are read again as soon as one is left - not on the next
        // page build. Leaving is also when the address is tidied into the
        // form the client will use, so what the box shows is what is asked.
        _txtVoiceRelayUrl.Leave += (_, _) => VoiceRelayBoxLeft(tidyAddress: true);
        _txtVoiceRelayClub.Leave += (_, _) => VoiceRelayBoxLeft(tidyAddress: false);
        _txtVoiceRelayToken.Leave += (_, _) => VoiceRelayBoxLeft(tidyAddress: false);

        _txtVoiceRelayUrl.KeyDown += (_, e) => VoiceRelayBoxKeyDown(e, tidyAddress: true);
        _txtVoiceRelayClub.KeyDown += (_, e) => VoiceRelayBoxKeyDown(e, tidyAddress: false);
        _txtVoiceRelayToken.KeyDown += (_, e) => VoiceRelayBoxKeyDown(e, tidyAddress: false);

        // --- ElevenLabs, and the stand-in that belongs to it ---
        _rdoElevenLabs.Text = "ElevenLabs - natural voices from your own ElevenLabs account (your API key and credits)";
        _rdoElevenLabs.AutoSize = true;

        // One line, like every check box: a check box is as wide as its
        // caption and never wraps it. The card is wide enough for it on one
        // line at the largest text size.
        _chkWindowsStandsIn.Text = "If ElevenLabs can't speak a line - no key, no internet, or the quota is used up - the Windows voice says it instead";
        _chkWindowsStandsIn.AutoSize = true;
        _chkWindowsStandsIn.Checked = current.WindowsVoiceStandsIn;

        // --- Windows, and which of its voices ---
        _rdoWindows.Text = "Windows voice - free, built into Windows, no account or internet needed";
        _rdoWindows.AutoSize = true;

        _cmbWindowsVoice.DropDownStyle = ComboBoxStyle.DropDownList;

        _btnTestWindowsVoice.Text = "Test Windows Voice";
        _btnTestWindowsVoice.Click += BtnTestWindowsVoice_Click;

        // Wide enough for "Microsoft Zira Desktop (English (United States),
        // female)" - see SizeFields - with the Test button beside it, or
        // under it when the row is too narrow for both.
        FillWindowsVoices(voices, current.WindowsVoiceName);

        var windows = new FieldGrid();

        windows.AddRow("Windows voice:", FieldGrid.Inline(_cmbWindowsVoice, _btnTestWindowsVoice));

        // --- Kokoro: free and natural, on this PC ---
        //
        // Between ElevenLabs and Windows on purpose: it sounds far closer to
        // the first and costs what the second does - nothing. The one catch
        // is a download, once, which the button beside the voice does.
        _rdoKokoro.Text = "Kokoro voice - free, natural-sounding, runs on this PC. One download, then no account or internet needed";
        _rdoKokoro.AutoSize = true;

        _cmbKokoroVoice.DropDownStyle = ComboBoxStyle.DropDownList;
        FillKokoroVoices(current.KokoroVoiceName);

        _btnTestKokoroVoice.Text = "Test Kokoro Voice";
        _btnTestKokoroVoice.Click += BtnTestKokoroVoice_Click;

        _btnDownloadKokoro.Click += BtnDownloadKokoro_Click;
        ShowKokoroDownloadButton();

        var kokoro = new FieldGrid();

        kokoro.AddRow("Kokoro voice:", FieldGrid.Inline(_cmbKokoroVoice, _btnTestKokoroVoice, _btnDownloadKokoro));

        _lblKokoroStatus.AutoSize = true;
        _lblKokoroStatus.ForeColor = AppTheme.TextMuted;

        // What the Windows voice can do on this PC, and the last test. A
        // reason or a test failure can run long, so it wraps to the card;
        // two lines are kept for it (BuildLayout) so a longer one does not
        // move the hint under it.
        _lblWindowsVoiceStatus.AutoSize = true;
        _lblWindowsVoiceStatus.ForeColor = AppTheme.TextMuted;

        var lblHint = _textSize.Wear(
            new Label
            {
                Text = "The Windows and Kokoro voices use the Speed and Playback volume below, and each announcement's own speed. "
                     + "Audio tags like [excited] are left out for them, and what they say is never kept with the saved ElevenLabs lines. "
                     + "While Kokoro isn't downloaded, or can't say a line, the Windows voice says it. "
                     + "The ElevenLabs settings below are kept whichever voice is chosen. "
                     + "With the HeatCaller voice the wording and the voice settings are fixed, and the free Windows voice says anything it can't - "
                     + "a flag call is never silent.",
                AutoSize = true,
                ForeColor = AppTheme.TextMuted,
            },
            () => AppTheme.SmallFont);

        // All three set, not just the one that is on, and before they are
        // put in the card: with no parent yet, none switches the others off.
        _rdoHeatCaller.Checked = current.EffectiveEngine == VoiceEngine.HeatCaller;
        _rdoElevenLabs.Checked = current.EffectiveEngine == VoiceEngine.ElevenLabs;
        _rdoWindows.Checked = current.EffectiveEngine == VoiceEngine.Windows;
        _rdoKokoro.Checked = current.EffectiveEngine == VoiceEngine.Kokoro;

        // Saving is wired by WireAutoSave, with everything else on the page.
        // These only keep the rest of the page in step with the choice.
        _rdoElevenLabs.CheckedChanged += (_, _) => UpdateEngineControls();
        _rdoWindows.CheckedChanged += (_, _) => UpdateEngineControls();
        _chkWindowsStandsIn.CheckedChanged += (_, _) => UpdateEngineControls();

        // Kokoro chosen by hand: the model is loaded now, in the background,
        // so the first line of the night doesn't wait for it.
        _rdoKokoro.CheckedChanged += (_, _) =>
        {
            UpdateEngineControls();

            if (_rdoKokoro.Checked && !_loadingSettings)
            {
                KokoroSpeech.WarmUp();
            }
        };

        // Chosen, rather than merely kept in step: moving to the HeatCaller
        // voice puts the standard lines back, after asking, because those
        // are the only lines it speaks. See HeatCallerChosen.
        _rdoHeatCaller.CheckedChanged += (_, _) =>
        {
            UpdateEngineControls();

            if (_rdoHeatCaller.Checked && !_loadingSettings)
            {
                HeatCallerChosen();
            }
        };

        // The three choices a section gap apart, so each reads as one choice
        // with its own settings under it.
        var card = new Section("Speak with");

        card.Add(_rdoHeatCaller, stretch: false, gapBelow: UiLayout.HintGap);
        Indented(card.Add(heatCaller));
        card.Add(_lblHeatCallerStatus);
        Indented(card.Add(relay, gapBelow: UiLayout.SectionGap));

        card.Add(_rdoElevenLabs, stretch: false, gapBelow: UiLayout.HintGap);
        Indented(card.Add(_chkWindowsStandsIn, stretch: false, gapBelow: UiLayout.SectionGap));

        card.Add(_rdoKokoro, stretch: false, gapBelow: UiLayout.HintGap);
        Indented(card.Add(kokoro));
        card.Add(_lblKokoroStatus, gapBelow: UiLayout.SectionGap);

        card.Add(_rdoWindows, stretch: false, gapBelow: UiLayout.HintGap);
        Indented(card.Add(windows));
        card.Add(_lblWindowsVoiceStatus, gapBelow: UiLayout.HintGap);
        card.Add(lblHint, gapBelow: 0);

        return (card, new[] { heatCaller, relay, kokoro, windows });
    }

    /// <summary>
    /// The Windows voice list: Windows' default first, then every voice that
    /// can speak here. A saved voice this PC doesn't have - a settings file
    /// from another laptop - stays in the list, and chosen, rather than being
    /// quietly replaced by the default the next time anything on the page is
    /// saved. Windows' default speaks for it meanwhile.
    /// </summary>
    private void FillWindowsVoices(IReadOnlyList<WindowsVoiceChoice> voices, string? savedName)
    {
        var saved = savedName?.Trim() ?? string.Empty;

        _cmbWindowsVoice.Items.Clear();
        _cmbWindowsVoice.Items.Add(new WindowsVoiceChoice(string.Empty, "(Windows default)"));

        var selected = 0;

        foreach (var voice in voices)
        {
            var index = _cmbWindowsVoice.Items.Add(voice);

            if (string.Equals(voice.Name, saved, StringComparison.OrdinalIgnoreCase))
            {
                selected = index;
            }
        }

        if (saved.Length > 0 && selected == 0)
        {
            selected = _cmbWindowsVoice.Items.Add(new WindowsVoiceChoice(saved, $"{saved} (not on this PC - Windows' default speaks instead)"));
        }

        _cmbWindowsVoice.SelectedIndex = selected;

        // The picker and its open list are sized to the voices in it
        // (SizeFields), measured afresh the next time the fields are sized.
        _windowsVoiceWidth = null;
    }

    // --- The Kokoro voice ---------------------------------------------------

    /// <summary>
    /// The Kokoro voice list: every English voice KokoroSharp ships, with the
    /// saved one chosen - or the default announcer voice when none is. A saved
    /// voice this PC doesn't have stays in the list, and chosen, like the
    /// Windows list; the default speaks for it meanwhile.
    /// </summary>
    private void FillKokoroVoices(string? savedName)
    {
        var saved = string.IsNullOrWhiteSpace(savedName) ? KokoroSpeech.DefaultVoice : savedName.Trim();

        _cmbKokoroVoice.Items.Clear();

        var selected = -1;

        foreach (var voice in KokoroSpeech.InstalledVoices())
        {
            var index = _cmbKokoroVoice.Items.Add(voice);

            if (string.Equals(voice.Name, saved, StringComparison.OrdinalIgnoreCase))
            {
                selected = index;
            }
        }

        if (selected < 0)
        {
            selected = _cmbKokoroVoice.Items.Add(new KokoroVoiceChoice(saved, $"{saved} (not on this PC - {KokoroSpeech.Describe(KokoroSpeech.DefaultVoice)} speaks instead)"));
        }

        _cmbKokoroVoice.SelectedIndex = selected;
        _kokoroVoiceWidth = null;
    }

    /// <summary>The download button: what pressing it would do now, and hidden once there's nothing to download.</summary>
    private void ShowKokoroDownloadButton()
    {
        var downloading = KokoroSpeech.IsDownloading;

        // "Download Again" for a model that wouldn't load (see ShouldOfferDownload).
        _btnDownloadKokoro.Text = downloading ? "Cancel Download"
            : KokoroSpeech.IsModelDownloaded ? $"Download Again ({KokoroSpeech.DownloadSizeText})"
            : $"Download ({KokoroSpeech.DownloadSizeText})";
        _btnDownloadKokoro.Visible = downloading || KokoroSpeech.ShouldOfferDownload;
    }

    /// <summary>
    /// Keeps the Kokoro row in step: Test once there is a model to test, the
    /// download button while there isn't, and a line saying where it stands.
    /// Part of UpdateEngineControls. Changes nothing that is saved.
    /// </summary>
    private void UpdateKokoroControls(bool kokoroChosen)
    {
        var downloading = KokoroSpeech.IsDownloading;

        // IsModelAvailable, not IsModelDownloaded: since the model ships in
        // the installer, "is there one to test" and "did this club download
        // one" are two different questions, and this row is asking the first.
        // A bundled build has nothing downloaded and is perfectly ready.
        var available = KokoroSpeech.IsModelAvailable;

        _btnTestKokoroVoice.Enabled = available && !downloading && !_kokoroTestRunning;
        ShowKokoroDownloadButton();

        // A test or a download owns the line while it runs.
        if (_kokoroTestRunning || downloading)
        {
            return;
        }

        if (!available)
        {
            _lblKokoroStatus.Text = kokoroChosen
                ? $"Kokoro isn't downloaded on this PC yet - press Download ({KokoroSpeech.DownloadSizeText}, once). Until then the Windows voice speaks."
                : $"Needs a one-off download ({KokoroSpeech.DownloadSizeText}). After that it works with no internet at all.";
            _lblKokoroStatus.ForeColor = kokoroChosen ? AppTheme.Warning : AppTheme.TextMuted;
        }
        else if (KokoroSpeech.LoadFailure is { } reason)
        {
            _lblKokoroStatus.Text = $"Kokoro can't speak - {reason}.";
            _lblKokoroStatus.ForeColor = kokoroChosen ? AppTheme.Danger : AppTheme.Warning;
        }
        else
        {
            // "Ready", not "downloaded and ready": on a build that shipped
            // with the model nobody downloaded anything, and saying so
            // invites a club to go looking for a download it never made.
            _lblKokoroStatus.Text = kokoroChosen
                ? "Speaking with Kokoro - ready on this PC. Nothing is billed, however much the announcer talks."
                : "Ready on this PC.";
            _lblKokoroStatus.ForeColor = AppTheme.TextMuted;
        }
    }

    /// <summary>
    /// Fetches the Kokoro model once, after asking - it is a big download
    /// for a track PC on a phone hotspot. Pressed again while it runs, it
    /// cancels.
    /// </summary>
    private async void BtnDownloadKokoro_Click(object? sender, EventArgs e)
    {
        if (KokoroSpeech.IsDownloading)
        {
            KokoroSpeech.CancelDownload();
            return;
        }

        var answer = AppDialog.Show(this,
            $"Download the Kokoro voice now? It's {KokoroSpeech.DownloadSizeText}, downloaded once and kept on this PC only.\n\n"
            + "After that Kokoro speaks with no internet and no account, and nothing is ever billed. "
            + "It runs on this PC's processor - an ordinary laptop says a race line in well under a second.",
            "Kokoro voice", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (answer != DialogResult.Yes || Gone)
        {
            return;
        }

        await FollowKokoroDownloadAsync(KokoroSpeech.DownloadModelAsync());
    }

    /// <summary>
    /// Shows the model download on this page while it runs, then how it
    /// ended. Never throws.
    ///
    /// The download is KokoroSpeech's, not the page's. It used to be the
    /// page's, and the page is rebuilt when a soundscape, venue or Lap times
    /// only is applied: the new page knew nothing of the download still going
    /// behind it, so it couldn't show or cancel it, and pressing Download
    /// again failed on the file the first one still held. A page built while
    /// one runs follows it from OnHandleCreated.
    /// </summary>
    private async Task FollowKokoroDownloadAsync(Task download)
    {
        if (ReferenceEquals(_kokoroDownload, download))
        {
            return;
        }

        _kokoroDownload = download;
        UpdateEngineControls();

        _lblKokoroStatus.Text = "Downloading Kokoro...";
        _lblKokoroStatus.ForeColor = AppTheme.TextMuted;

        // Made here, on the UI thread, so each report comes back to it.
        var progress = new Progress<double>(fraction =>
        {
            if (!Gone && ReferenceEquals(_kokoroDownload, download))
            {
                _lblKokoroStatus.Text = $"Downloading Kokoro... {fraction:P0}";
            }
        });

        Action<double> report = ((IProgress<double>)progress).Report;
        _kokoroProgress = report;
        KokoroSpeech.DownloadProgressed += report;

        string? outcome = null;
        Color outcomeColour = AppTheme.Success;

        try
        {
            await download;

            // Said by the page showing it now, once - not also by a page
            // rebuilt away while it ran.
            if (!Gone)
            {
                Status("Kokoro voice downloaded.");
                outcome = _rdoKokoro.Checked
                    ? "Downloaded - Kokoro is speaking from now on."
                    : "Downloaded. Choose Kokoro above to speak with it.";

                if (_rdoKokoro.Checked)
                {
                    KokoroSpeech.WarmUp();
                }
            }
        }
        catch (OperationCanceledException)
        {
            outcome = "Download canceled - nothing was kept. Press Download to start again.";
            outcomeColour = AppTheme.TextMuted;
        }
        catch (Exception ex)
        {
            // Followed from an async void handler: anything escaping it takes the app down.
            if (!Gone)
            {
                Status($"Kokoro download failed - {ex.GetType().Name}: {ex.Message}");
            }

            outcome = $"Download failed: {ex.Message}";
            outcomeColour = AppTheme.Danger;
        }
        finally
        {
            KokoroSpeech.DownloadProgressed -= report;
            _kokoroProgress = null;
            _kokoroDownload = null;
        }

        if (Gone)
        {
            return;
        }

        UpdateEngineControls();
        _lblKokoroStatus.Text = outcome;
        _lblKokoroStatus.ForeColor = outcomeColour;
    }

    /// <summary>
    /// Speaks a sample in the Kokoro voice picked above, at the Speed and
    /// Playback volume on screen - saved or not, like every test here. Kokoro
    /// itself, never the Windows stand-in: a test shows what Kokoro says, or
    /// why it can't.
    /// </summary>
    private async void BtnTestKokoroVoice_Click(object? sender, EventArgs e)
    {
        var profile = BuildProfileFromControls();
        var voice = _cmbKokoroVoice.SelectedItem as KokoroVoiceChoice;

        _kokoroTestRunning = true;
        _btnTestKokoroVoice.Enabled = false;
        _lblKokoroStatus.Text = "Speaking a sample with Kokoro... (the first one after starting takes a few seconds to load)";
        _lblKokoroStatus.ForeColor = AppTheme.TextMuted;

        try
        {
            var audio = await KokoroSpeech.RenderAsync(profile, "This is HeatCaller, testing the Kokoro voice. Green flag, green flag!");
            if (Gone) { return; }

            await PlayTestAudioAsync(audio, profile.Volume);
            if (Gone) { return; }

            _lblKokoroStatus.Text = $"Done - that was {voice?.Display ?? KokoroSpeech.Describe(KokoroSpeech.DefaultVoice)}.";
            _lblKokoroStatus.ForeColor = AppTheme.Success;
        }
        catch (Exception ex)
        {
            // An async void handler: anything escaping it takes the app down.
            Status($"Kokoro voice test failed - {ex.GetType().Name}: {ex.Message}");

            if (!Gone)
            {
                _lblKokoroStatus.Text = $"Test failed: {ex.Message}";
                _lblKokoroStatus.ForeColor = AppTheme.Danger;
            }
        }
        finally
        {
            _kokoroTestRunning = false;

            if (!Gone)
            {
                _btnTestKokoroVoice.Enabled = KokoroSpeech.IsModelAvailable && !KokoroSpeech.IsDownloading;
            }
        }
    }

    // --- The HeatCaller voice -----------------------------------------------

    /// <summary>How this page asks the relay which voices the club may use, and where its allowance stands. Set by the host.</summary>
    internal HeatCallerVoiceClient? HeatCallerVoice { get; set; }

    /// <summary>The catalogue as the relay last gave it, so the combo box and the save path agree about revisions.</summary>
    private IReadOnlyList<HeatCallerVoiceChoice> _heatCallerVoices = Array.Empty<HeatCallerVoiceChoice>();

    /// <summary>Whether the relay said this club's plan includes the voice. False until it has answered.</summary>
    private bool _heatCallerEnabled;

    /// <summary>True while the catalogue is being read, so two presses of Refresh don't race.</summary>
    private bool _heatCallerLoading;

    /// <summary>
    /// The catalogue was asked for again while one was still being read, so
    /// it is read once more when that one finishes.
    ///
    /// Leaving each of the three voice-relay boxes asks again, and tabbing
    /// through them is three asks in a second. Dropping the later ones left
    /// the page showing what the half-filled address answered, and the club
    /// had to press Refresh to see the truth.
    /// </summary>
    private bool _heatCallerLoadAgain;

    /// <summary>Whether any of the asks folded into _heatCallerLoadAgain was a club's own edit rather than the page's quiet first look.</summary>
    private bool _heatCallerLoadAgainByHand;

    /// <summary>
    /// Puts a line on the HeatCaller status label, marked when the voice is
    /// pointed at a relay of its own rather than the club's.
    ///
    /// Hearing lines from a relay nobody else is using should never be a
    /// surprise, and this label is the only place that says so.
    /// </summary>
    private void HeatCallerStatus(string text, Color colour)
    {
        _lblHeatCallerStatus.Text = HeatCallerVoice?.UsingVoiceRelay == true
            ? "Test relay - " + text
            : text;

        _lblHeatCallerStatus.ForeColor = colour;
    }

    /// <summary>
    /// Writes the three voice-relay boxes to the settings file and asks
    /// whichever relay they now name what it offers.
    ///
    /// Called as each box is LEFT, or Enter is pressed in it - not as it is
    /// typed: a settings file per keystroke is bad enough, and a request to
    /// a half-typed address is worse. This is also the moment the boxes are
    /// put into AppSettings, which the voice client reads on every line - so
    /// until a box is left, the announcer goes on speaking through whatever
    /// relay was set before.
    ///
    /// Leaving the address box also tidies it into the form the client
    /// actually uses, so "localhost:8787" shows as "http://localhost:8787"
    /// and nobody has to wonder which one is being asked.
    /// </summary>
    private void VoiceRelayBoxLeft(bool tidyAddress)
    {
        if (_loadingSettings)
        {
            return;
        }

        // Tabbing through the page leaves all three of these without
        // touching any of them, and that is not a reason to write the
        // settings file or to make the relay answer the same question
        // again - let alone to put "Asking the relay..." over whatever the
        // status line was saying.
        if (VoiceRelayShown() == _voiceRelaySaved)
        {
            return;
        }

        if (tidyAddress)
        {
            var tidy = RelayPublisher.NormaliseBaseUrl(_txtVoiceRelayUrl.Text);

            if (!string.Equals(tidy, _txtVoiceRelayUrl.Text, StringComparison.Ordinal))
            {
                // Guarded, or putting the tidied text back counts as typing
                // and writes the raw value over it again.
                _loadingSettings = true;

                try
                {
                    _txtVoiceRelayUrl.Text = tidy;
                }
                finally
                {
                    _loadingSettings = false;
                }
            }

            _appSettings.VoiceRelayBaseUrl = tidy;
        }
        else
        {
            // Left as typed, as it always was when another box is left: the
            // address box tidies itself when IT is left, which is always
            // before focus reaches one of the other two.
            _appSettings.VoiceRelayBaseUrl = _txtVoiceRelayUrl.Text.Trim();
        }

        // All three together, so a club id and token belonging to one relay
        // are never in use alongside an address that belongs to another.
        _appSettings.VoiceRelayClub = _txtVoiceRelayClub.Text.Trim();
        _appSettings.VoiceRelayToken = _txtVoiceRelayToken.Text.Trim();

        // Changed by hand here, so whatever a restored backup put in these
        // boxes has been looked at (RestoredSettings).
        RestoredSettings.Confirm(RestoredSettings.Part.VoiceRelay);

        // After the tidy, so leaving the address box a second time without
        // touching it is seen as the no-change it is.
        _voiceRelaySaved = VoiceRelayShown();

        _appSettings.Save();

        // A different relay, club or token is a different answer: whatever
        // the old one refused for - its allowance spent, a token it would
        // not take - says nothing about this one, and without this the
        // announcer went on resting over it for up to half an hour (or,
        // with the allowance, for the rest of the month) after the club had
        // put it right. The same as a new ElevenLabs key ending a rest.
        Announcer?.ResetHeatCallerVoiceState("the voice relay was changed on Voice Settings");

        _ = LoadHeatCallerCatalogueAsync(byHand: true);
    }

    /// <summary>
    /// Enter in one of the voice-relay boxes applies it there and then, as
    /// leaving it would - so a club that types an address and presses Enter
    /// is not left wondering why nothing was asked.
    /// </summary>
    private void VoiceRelayBoxKeyDown(KeyEventArgs e, bool tidyAddress)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        // No ding: a single-line box has nothing to do with Enter itself.
        e.SuppressKeyPress = true;
        e.Handled = true;

        // Enter in a box left as it was is also how a voice relay address
        // that came from a restored backup is taken on - somebody pressing a
        // key in it, with it in front of them. See RestoredSettings.
        if (RestoredSettings.IsHeld(RestoredSettings.Part.VoiceRelay) && VoiceRelayShown() == _voiceRelaySaved)
        {
            RestoredSettings.Confirm(RestoredSettings.Part.VoiceRelay);
            Announcer?.ResetHeatCallerVoiceState("the voice relay from a restored backup was checked on Voice Settings");
            _ = LoadHeatCallerCatalogueAsync(byHand: true);
            return;
        }

        VoiceRelayBoxLeft(tidyAddress);
    }

    /// <summary>
    /// One of the ElevenLabs boxes (key, model, language, output format) was
    /// left, or Enter pressed in it: saved now, if it differs from what the
    /// races are using - see the constructor. Tabbing past them changes
    /// nothing, so it writes nothing.
    /// </summary>
    private void ElevenLabsBoxLeft()
    {
        if (_loadingSettings)
        {
            return;
        }

        var saved = _store.Current;
        var model = string.IsNullOrWhiteSpace(_cmbModel.Text) ? "eleven_multilingual_v2" : _cmbModel.Text.Trim();

        if (string.Equals(_txtApiKey.Text.Trim(), saved.ApiKey ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(model, saved.ModelId ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(_txtLanguageCode.Text.Trim(), saved.LanguageCode ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(_txtOutputFormat.Text.Trim(), saved.OutputFormat ?? string.Empty, StringComparison.Ordinal))
        {
            return;
        }

        AutoSave();
    }

    /// <summary>The three voice-relay boxes as one string, for telling a real edit from tabbing past them.</summary>
    private string VoiceRelayShown() =>
        $"{_txtVoiceRelayUrl.Text}\n{_txtVoiceRelayClub.Text}\n{_txtVoiceRelayToken.Text}";

    /// <summary>What VoiceRelayShown gave when the boxes were last saved - or seeded.</summary>
    private string _voiceRelaySaved = string.Empty;

    /// <summary>
    /// Asks the relay which HeatCaller voices this club may use and where
    /// its allowance stands, and fills the list with them.
    ///
    /// Never blocks the page: it is called as the page opens and from the
    /// Refresh button, and everything it touches is on the UI thread after
    /// the await. A relay that cannot be reached leaves the list as it was
    /// and says so - the club's saved choice keeps working, because the
    /// announcer asks the relay by that choice's id whatever this page knows.
    /// </summary>
    private async Task LoadHeatCallerCatalogueAsync(bool byHand)
    {
        if (_heatCallerLoading)
        {
            _heatCallerLoadAgain = true;

            // Kept, not overwritten: the run in flight may be the quiet one
            // the page starts with, and the re-run is the club's own edit.
            // Losing the flag lost the "Asking the relay..." line and the
            // console note that go with an edit made by hand.
            _heatCallerLoadAgainByHand |= byHand;
            return;
        }

        if (HeatCallerVoice is not { } client)
        {
            _heatCallerEnabled = false;
            HeatCallerStatus("The HeatCaller voice isn't available in this window.", AppTheme.TextMuted);
            UpdateEngineControls();
            return;
        }

        if (!client.RelayConfigured)
        {
            _heatCallerEnabled = false;

            // Two quite different situations, and saying the wrong one sends
            // the club to the wrong page. A voice relay of its own that
            // can't be used is almost always a missing token or a plain
            // http address somewhere out on the internet, which the client
            // refuses to send a token to.
            HeatCallerStatus(
                client.UsingVoiceRelay
                    ? "This voice relay can't be used yet. It needs a club id and a token. A blank club is the Club Website page's; "
                      + "a blank token is the Club Website page's only for this club's own relay or a relay on this PC (localhost) - "
                      + "anywhere else, type the token in. A plain http address is only accepted for this PC or your network."
                    : "Set the club up on the Club Website page first - the HeatCaller voice speaks through the same relay, "
                      + "with the same token.",
                AppTheme.Warning);

            UpdateEngineControls();
            return;
        }

        _heatCallerLoading = true;
        _btnHeatCallerRefresh.Enabled = false;

        if (byHand)
        {
            HeatCallerStatus("Asking the relay...", AppTheme.TextMuted);
        }

        try
        {
            var catalogue = await client.CatalogueAsync();

            if (Gone)
            {
                return;
            }

            _heatCallerEnabled = catalogue.Enabled;
            _heatCallerVoices = catalogue.Voices;

            var missing = FillHeatCallerVoices(_store.Current.HeatCallerVoice);

            if (!catalogue.Enabled)
            {
                HeatCallerStatus(
                    "The HeatCaller voice isn't included for this club yet. Your own ElevenLabs key and the free Windows voice both work as normal.",
                    AppTheme.Warning);
            }
            else if (catalogue.Voices.Count == 0)
            {
                HeatCallerStatus("The relay has no voices to offer yet.", AppTheme.Warning);
            }
            else if (missing)
            {
                // Said first, before the allowance: the allowance is fine
                // and the voice still will not speak, and nothing else on
                // the page says why.
                HeatCallerStatus(
                    $"This relay doesn't offer \"{_store.Current.HeatCallerVoice}\", the voice chosen here, so the free Windows voice "
                    + "would speak every line. Pick one from the list above.",
                    AppTheme.Warning);
            }
            else
            {
                HeatCallerStatus(
                    $"Included - {catalogue.Allowance.Describe()} Saved lines cost nothing. "
                    + "When the allowance is gone the free Windows voice speaks every line until it starts again, and your saved lines are kept.",
                    catalogue.Allowance.Spent ? AppTheme.Warning : AppTheme.TextMuted);
            }

            if (byHand)
            {
                Status($"HeatCaller voice: {(catalogue.Enabled ? "included" : "not included")}, "
                       + $"{Plural.Of(catalogue.Voices.Count, "voice")}, {catalogue.Allowance.Describe()}");
            }
        }
        catch (HeatCallerVoiceException ex)
        {
            if (Gone)
            {
                return;
            }

            // Left as it was rather than emptied: the club's saved voice is
            // still what the announcer will ask for, and a relay that is
            // asleep for a moment must not look like a setting that is gone.
            HeatCallerStatus($"Couldn't ask the relay: {ex.Message}", AppTheme.Warning);

            if (byHand)
            {
                Status($"HeatCaller voice: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            // An async void handler's worth of care, even here: anything
            // escaping this on the UI thread takes the app down.
            if (!Gone)
            {
                HeatCallerStatus($"Couldn't ask the relay: {ex.Message}", AppTheme.Warning);
            }
        }
        finally
        {
            _heatCallerLoading = false;

            if (Gone)
            {
                _heatCallerLoadAgain = false;
                _heatCallerLoadAgainByHand = false;
            }
            else
            {
                _btnHeatCallerRefresh.Enabled = true;
                UpdateEngineControls();

                // Something changed the address, club or token while this
                // one was in flight, so what it just showed is out of date.
                // Both flags are cleared before the call, so this cannot
                // loop: only another edit sets them again.
                if (_heatCallerLoadAgain)
                {
                    var again = _heatCallerLoadAgainByHand;

                    _heatCallerLoadAgain = false;
                    _heatCallerLoadAgainByHand = false;

                    _ = LoadHeatCallerCatalogueAsync(again);
                }
            }
        }
    }

    /// <summary>
    /// Fills the HeatCaller voice list, keeping the club's saved choice
    /// selected. A saved voice the relay no longer offers stays in the list,
    /// and chosen, rather than being silently swapped for another one: the
    /// club should be told, not re-voiced.
    /// </summary>
    private bool FillHeatCallerVoices(string? savedId)
    {
        var saved = savedId?.Trim() ?? string.Empty;
        var reloading = _loadingSettings;
        var missing = false;

        _loadingSettings = true;

        try
        {
            _cmbHeatCallerVoice.Items.Clear();

            var selected = -1;

            foreach (var voice in _heatCallerVoices)
            {
                var index = _cmbHeatCallerVoice.Items.Add(voice);

                if (string.Equals(voice.Id, saved, StringComparison.OrdinalIgnoreCase))
                {
                    selected = index;
                }
            }

            if (selected < 0 && saved.Length > 0)
            {
                // Reported back so the status line can say it. Left only in
                // the drop-down, this reads as "Included - 0 of 20,000
                // characters" on a page that will not speak a word: the
                // announcer asks for a voice this relay has never heard of
                // and the Windows voice says everything. The likeliest way
                // to get here is a voice chosen while a test relay was set,
                // kept after the boxes were emptied.
                missing = true;

                // The entry carries the rev saved with this voice, so picking
                // another voice and then this one again saves this voice with
                // ITS rev. With no rev on it, the save fell back to whatever
                // rev the store held by then - the other voice's - and every
                // prepared line of this voice (keyed by voice and rev) missed.
                // Only when the saved rev belongs to this voice: this method
                // is handed the saved voice today, but a rev must never be
                // paired with a voice it was not saved with. It also cannot
                // trigger the re-tune save below: its rev IS the saved one.
                var savedRev = string.Equals(_store.Current.HeatCallerVoice?.Trim(), saved, StringComparison.OrdinalIgnoreCase)
                    ? _store.Current.HeatCallerRev ?? string.Empty
                    : string.Empty;

                selected = _cmbHeatCallerVoice.Items.Add(
                    new HeatCallerVoiceChoice(saved, saved, "not offered by the relay any more", savedRev));
            }

            if (selected < 0 && _cmbHeatCallerVoice.Items.Count > 0)
            {
                // Nothing chosen yet: the first voice the relay offers, so a
                // club that ticks HeatCaller has a working announcer without
                // having to make another decision.
                selected = 0;
            }

            _cmbHeatCallerVoice.SelectedIndex = selected;
        }
        finally
        {
            _loadingSettings = reloading;
        }

        // The longest voice may have come or gone - see SizeFields.
        RefitPickers();

        // Outside the guard: this IS a change worth saving - the page picked
        // a voice for a club that had none, or the relay has re-tuned the
        // one it already had. The revision matters as much as the id: it is
        // part of a saved line's key, and a voice re-tuned on the relay
        // keeps its id, so comparing ids alone left every laptop playing
        // takes that no longer matched what the relay now speaks.
        if (!reloading && _cmbHeatCallerVoice.SelectedItem is HeatCallerVoiceChoice chosen
            && (!string.Equals(chosen.Id, saved, StringComparison.Ordinal)
                || (chosen.Rev.Length > 0 && !string.Equals(chosen.Rev, _store.Current.HeatCallerRev, StringComparison.Ordinal))))
        {
            AutoSave();
        }

        return missing;
    }

    /// <summary>
    /// The rev to save with the HeatCaller voice in the list. The chosen
    /// entry's own rev when it has one. Otherwise (a "not offered by the
    /// relay any more" entry for a voice saved before revs were kept, or
    /// nothing chosen) the rev last SAVED - not the one the page opened
    /// with, which could undo a rev saved since - but only if that saved rev
    /// belongs to the same voice. A rev is part of a prepared line's key, so
    /// pairing one voice with another voice's rev made every one of its
    /// prepared lines miss; an empty rev is the honest answer instead.
    /// </summary>
    private string HeatCallerRevToSave()
    {
        if (_cmbHeatCallerVoice.SelectedItem is not HeatCallerVoiceChoice picked)
        {
            // Nothing chosen: the voice that will be saved is the opening
            // one (see HeatCallerVoice in BuildProfileFromControls).
            return string.Equals(_store.Current.HeatCallerVoice?.Trim(), _original.HeatCallerVoice?.Trim(), StringComparison.OrdinalIgnoreCase)
                ? _store.Current.HeatCallerRev ?? string.Empty
                : _original.HeatCallerRev ?? string.Empty;
        }

        if (picked.Rev.Length > 0)
        {
            return picked.Rev;
        }

        return string.Equals(_store.Current.HeatCallerVoice?.Trim(), picked.Id?.Trim(), StringComparison.OrdinalIgnoreCase)
            ? _store.Current.HeatCallerRev ?? string.Empty
            : string.Empty;
    }

    /// <summary>
    /// The club has just chosen the HeatCaller voice. Its lines are fixed,
    /// so any wording of the club's own is put back to the standard lines -
    /// after asking, and only when there is something to put back.
    ///
    /// Asked rather than done quietly because the club may have spent an
    /// evening on those lines: the way to keep them is to use an ElevenLabs
    /// key of their own, or the free Windows voice, and the question says so.
    /// </summary>
    private void HeatCallerChosen()
    {
        _ = LoadHeatCallerCatalogueAsync(byHand: false);

        if (!HasOwnWording())
        {
            return;
        }

        var answer = AppDialog.Show(this,
            "The HeatCaller voice speaks the standard announcer lines, so the wording on this page will go back to them.\n\n"
            + "Your own wording is kept if you use your own ElevenLabs key, or the free Windows voice, instead.\n\n"
            + "Use the HeatCaller voice and the standard lines?",
            "HeatCaller voice", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.Yes)
        {
            // Back to whatever was in use before. Setting another radio is
            // what unchecks this one - and a club that was already ON this
            // voice stays on it, rather than being moved to Windows by a
            // question it only reached because a save failed.
            switch (_store.Current.EffectiveEngine)
            {
                case VoiceEngine.ElevenLabs:
                    _rdoElevenLabs.Checked = true;
                    break;

                case VoiceEngine.Windows:
                    _rdoWindows.Checked = true;
                    break;

                case VoiceEngine.Kokoro:
                    _rdoKokoro.Checked = true;
                    break;
            }

            return;
        }

        RestoreDefaultLines("the HeatCaller voice speaks the standard lines");
    }

    /// <summary>
    /// Whether the lines on this page are the club's own words rather than
    /// HeatCaller's.
    ///
    /// An announcer style counts as HeatCaller's: every style ships with the
    /// app, so its lines are the same words in every club in the country -
    /// which is exactly what lets the relay render each of them once and
    /// serve them to everyone. Only wording somebody typed is the club's,
    /// and only that has to go back before the HeatCaller voice speaks.
    /// </summary>
    private bool HasOwnWording()
    {
        var onPage = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var (type, row) in _eventRows)
        {
            onPage[type.ToString()] = row.Template.Text
                .Split('\n')
                .Select(line => line.Trim('\r', ' '))
                .Where(line => line.Length > 0)
                .ToList();
        }

        if (Matches(onPage, VoiceProfile.CreateDefault()))
        {
            return false;
        }

        foreach (var theme in Theme.CreateBuiltInDefaults())
        {
            if (Matches(onPage, theme.Voice))
            {
                return false;
            }
        }

        return true;

        // A style only sets the lines it cares about; the rest stay the
        // default ones, which is what applying it leaves on the page.
        static bool Matches(Dictionary<string, List<string>> page, VoiceProfile known)
        {
            var defaults = VoiceProfile.CreateDefault();

            foreach (var (key, lines) in page)
            {
                var expected = known.Events.TryGetValue(key, out var fromStyle) && fromStyle.EffectiveTemplates.Count > 0
                    ? fromStyle.EffectiveTemplates
                    : defaults.Events.TryGetValue(key, out var standard)
                        ? standard.EffectiveTemplates
                        : Array.Empty<string>();

                if (!lines.SequenceEqual(expected, StringComparer.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Whether ElevenLabs is set up on this page - a key and a voice - as the
    /// Speak With status line last said it was. Null until that line has said
    /// anything about it.
    /// </summary>
    private bool? _elevenLabsSetUpShown;

    /// <summary>Whether a key and a voice are both filled in on this page, saved or not.</summary>
    private bool ElevenLabsSetUpOnPage() => _txtApiKey.Text.Trim().Length > 0 && _cmbVoice.SelectedItem is not null;

    /// <summary>
    /// Refreshes the Speak With status line when the key or voice below
    /// changes what it would say - and only then, so a Windows voice test's
    /// result stays on screen through a key being pasted in. (A key filled in
    /// while a test is speaking leaves the line as it is - the test owns it -
    /// and it catches up at the next change.)
    /// </summary>
    private void ElevenLabsSetUpChanged()
    {
        if (_rdoWindows.Checked && ElevenLabsSetUpOnPage() != _elevenLabsSetUpShown)
        {
            UpdateEngineControls();
        }
    }

    /// <summary>
    /// Keeps the page in step with the voice chosen at the top: the stand-in
    /// box only means something with ElevenLabs chosen (disabled, not hidden,
    /// so it can be seen and set beforehand), the Windows controls need a
    /// Windows voice to exist, and Prepare Lines is for ElevenLabs only.
    /// Changes nothing that is saved.
    /// </summary>
    private void UpdateEngineControls()
    {
        var windowsChosen = _rdoWindows.Checked;
        var heatCallerChosen = _rdoHeatCaller.Checked;
        var kokoroChosen = _rdoKokoro.Checked;
        var reason = WindowsVoice.UnavailableReason;
        var available = reason is null;

        // Can't be picked with no Windows voice to speak - unless it already
        // is, so the choice can still be seen and changed.
        _rdoWindows.Enabled = available || windowsChosen;
        _chkWindowsStandsIn.Enabled = available && !windowsChosen && !heatCallerChosen && !kokoroChosen;

        UpdateKokoroControls(kokoroChosen);
        _cmbWindowsVoice.Enabled = available;
        _btnTestWindowsVoice.Enabled = available && !_windowsTestRunning;

        // The HeatCaller voice can be chosen once the relay says the club
        // has it - and stays choosable while it already is, so a club is
        // never stuck in a mode the page won't let it leave or re-enter.
        _rdoHeatCaller.Enabled = _heatCallerEnabled || heatCallerChosen;
        _cmbHeatCallerVoice.Enabled = _heatCallerEnabled && heatCallerChosen && !_heatCallerLoading;

        ApplyHeatCallerLock(heatCallerChosen);

        if (!_windowsTestRunning)
        {
            if (!available)
            {
                // With the Windows voice chosen nothing can speak, so the way
                // out is what the line says - not that ElevenLabs is fine.
                _lblWindowsVoiceStatus.Text = windowsChosen
                    ? $"No Windows voice can speak on this PC - {reason}. Choose ElevenLabs above to speak with it instead."
                    : kokoroChosen
                        ? $"No Windows voice can speak on this PC - {reason}. Kokoro works as normal, but nothing can stand in for a line it can't say."
                        : $"No Windows voice can speak on this PC - {reason}. ElevenLabs works as normal.";

                // Red only when nothing can speak because of it.
                _lblWindowsVoiceStatus.ForeColor = windowsChosen ? AppTheme.Danger : AppTheme.Warning;
            }
            else
            {
                var count = Plural.Of(WindowsVoice.InstalledVoices().Count, "Windows voice");

                // A key and a voice entered below don't switch over by
                // themselves - the choice above does - so the line says so
                // rather than leave somebody wondering why it isn't ElevenLabs.
                var elevenLabsSetUp = ElevenLabsSetUpOnPage();
                _elevenLabsSetUpShown = elevenLabsSetUp;

                _lblWindowsVoiceStatus.Text = windowsChosen
                    ? elevenLabsSetUp
                        ? $"Speaking with the Windows voice - {count} on this PC. ElevenLabs is set up below too: choose it above to use it."
                        : $"Speaking with the Windows voice - {count} on this PC."
                    : kokoroChosen
                        ? $"Speaking with Kokoro, with the Windows voice standing by for any line it can't say - {count} on this PC."
                    : _chkWindowsStandsIn.Checked
                        ? $"Speaking with ElevenLabs, with the Windows voice standing by - {count} on this PC."
                        : $"Speaking with ElevenLabs. A line it can't speak is skipped - {count} on this PC.";

                _lblWindowsVoiceStatus.ForeColor = AppTheme.TextMuted;
            }
        }

        // Prepare Lines renders through whichever voice is chosen - it is
        // the cheapest thing a club on the HeatCaller voice can do, because
        // the relay keeps one copy of every fixed line for every club - so
        // only the Windows and Kokoro voices have nothing for it to do. Left
        // alone while a run is going: that run stops itself before its next
        // line.
        if (windowsChosen || kokoroChosen)
        {
            if (_prepareCancel is null)
            {
                _btnPrepareLines.Enabled = false;
                _lblPrepareProgress.Text = PrepareNotNeededText;
            }
        }
        else if (_lblPrepareProgress.Text == PrepareNotNeededText)
        {
            _btnPrepareLines.Enabled = true;
            _lblPrepareProgress.Text = string.Empty;
        }
    }

    /// <summary>
    /// What the HeatCaller voice takes away, and gives back.
    ///
    /// Everything here either spends a character on HeatCaller's ElevenLabs
    /// account or changes what would be spent: the API key and its Test
    /// buttons, Load Voices, the model, the voice settings, the per-line
    /// Test buttons, and the lines themselves. A club on the HeatCaller
    /// voice is not the one paying, so it gets the announcer exactly as it
    /// was designed - and its own key or the free Windows voice the moment
    /// it wants to change any of that.
    ///
    /// Disabled and read-only rather than hidden: a club can still see what
    /// its announcer says, and what it would be able to change with a key
    /// of its own. Hiding the boxes would only look like they had been lost.
    /// </summary>
    private void ApplyHeatCallerLock(bool locked)
    {
        // The ElevenLabs credentials and everything that shapes a request.
        foreach (var control in new Control[]
                 {
                     _txtApiKey, _chkShowApiKey, _cmbVoice, _btnLoadVoices, _cmbModel,
                     _numStability, _numSimilarity, _numStyle, _numSpeed, _chkSpeakerBoost,
                     _cmbTextNormalization, _txtLanguageCode, _txtOutputFormat, _btnTestVoice,
                 })
        {
            control.Enabled = !locked;
        }

        // The wording. Read-only rather than disabled so it can still be
        // read and copied, and the caret still shows where a line is. The
        // colour is the theme's own for a text box either way - a read-only
        // box in a dark theme repainted by Windows is unreadable.
        foreach (var (_, row) in _eventRows)
        {
            row.Template.ReadOnly = locked;
        }

        foreach (var button in _eventTestButtons)
        {
            // Hidden, not merely disabled: a row of thirty greyed-out Test
            // buttons reads as something broken, and there is nothing a club
            // can do to bring them back other than change voice.
            button.Visible = !locked;
        }

        // Each announcement's own speed multiplies the voice's - and the
        // HeatCaller voice's speed is the relay's to set, not the club's.
        foreach (var (_, speed) in _eventSpeeds)
        {
            speed.Enabled = !locked;
        }

        if (_btnRestoreLines is not null)
        {
            _btnRestoreLines.Enabled = !locked;
        }
    }

    /// <summary>
    /// Puts Prepare Lines back after a run or a cancelled question - but not
    /// while the Windows voice is chosen: it used to be re-enabled
    /// unconditionally, and the voice can be switched while it waits.
    /// </summary>
    private void RestorePrepareButton()
    {
        if (_rdoWindows.Checked || _rdoKokoro.Checked)
        {
            _btnPrepareLines.Enabled = false;
            _lblPrepareProgress.Text = PrepareNotNeededText;
        }
        else
        {
            _btnPrepareLines.Enabled = true;
        }
    }

    /// <summary>
    /// Speaks a sample in the Windows voice picked above, at the Speed and
    /// Playback volume on screen - saved or not, like every test here. No
    /// ElevenLabs request, no key needed, nothing saved.
    /// </summary>
    private async void BtnTestWindowsVoice_Click(object? sender, EventArgs e)
    {
        var profile = BuildProfileFromControls();
        var voice = _cmbWindowsVoice.SelectedItem as WindowsVoiceChoice;

        _windowsTestRunning = true;
        _btnTestWindowsVoice.Enabled = false;
        _lblWindowsVoiceStatus.Text = "Speaking a sample with the Windows voice...";
        _lblWindowsVoiceStatus.ForeColor = AppTheme.TextMuted;

        try
        {
            var audio = await WindowsVoice.RenderAsync(profile, "This is HeatCaller, testing the Windows voice.");
            if (Gone) { return; }

            await PlayTestAudioAsync(audio, profile.Volume);
            if (Gone) { return; }

            _lblWindowsVoiceStatus.Text = $"Done - that was {(voice is null || voice.Name.Length == 0 ? "Windows' default voice" : voice.Name)}.";
            _lblWindowsVoiceStatus.ForeColor = AppTheme.Success;
        }
        catch (Exception ex)
        {
            // An async void handler: anything escaping it takes the app down.
            Status($"Windows voice test failed - {ex.GetType().Name}: {ex.Message}");

            if (!Gone)
            {
                _lblWindowsVoiceStatus.Text = $"Test failed: {ex.Message}";
                _lblWindowsVoiceStatus.ForeColor = AppTheme.Danger;
            }
        }
        finally
        {
            _windowsTestRunning = false;

            if (!Gone)
            {
                _btnTestWindowsVoice.Enabled = WindowsVoice.IsAvailable;
            }
        }
    }

    /// <summary>
    /// ElevenLabs connection: when it is used, then API Key: with Show,
    /// Voice: with Load Voices, and Model:, in one caption column, and what
    /// the models can do under them.
    /// </summary>
    private Section BuildConnectionCard(VoiceProfile current)
    {
        // Says when this card matters, now that ElevenLabs isn't the only
        // voice. Everything in it stays usable whichever voice is chosen, so
        // a club can set ElevenLabs up before switching over to it.
        var lblUsedWhen = _textSize.Wear(
            new Label
            {
                Text = "Used when ElevenLabs is chosen above, and for the saved voice lines - it can be set up whichever voice is speaking.",
                AutoSize = true,
                ForeColor = AppTheme.TextMuted,
            },
            () => AppTheme.SmallFont);

        // --- API key ---
        _txtApiKey.UseSystemPasswordChar = true;
        _txtApiKey.Text = current.ApiKey;

        // Themed explicitly, same as the template boxes on the Announcements
        // page - consistent rather than depending on the recursive pass
        // reaching it.
        _txtApiKey.BackColor = AppTheme.Surface;
        _txtApiKey.ForeColor = AppTheme.TextDark;
        _txtApiKey.BorderStyle = BorderStyle.FixedSingle;

        _chkShowApiKey.Text = "Show";
        _chkShowApiKey.AutoSize = true;
        _chkShowApiKey.CheckedChanged += (_, _) => _txtApiKey.UseSystemPasswordChar = !_chkShowApiKey.Checked;

        // --- Voice ---
        _cmbVoice.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbVoice.DisplayMember = nameof(ElevenLabsClient.VoiceInfo.Name);

        if (!string.IsNullOrWhiteSpace(current.VoiceId))
        {
            _cmbVoice.Items.Add(new ElevenLabsClient.VoiceInfo(current.VoiceId, string.IsNullOrWhiteSpace(current.VoiceName) ? current.VoiceId : current.VoiceName));
            _cmbVoice.SelectedIndex = 0;
        }

        _btnLoadVoices.Text = "Load Voices";
        _btnLoadVoices.Click += BtnLoadVoices_Click;

        // --- Model ---
        _cmbModel.DropDownStyle = ComboBoxStyle.DropDown;
        _cmbModel.Items.AddRange(new object[] { "eleven_v3", "eleven_multilingual_v2", "eleven_turbo_v2_5", "eleven_flash_v2_5", "eleven_monolingual_v1" });
        _cmbModel.Text = string.IsNullOrWhiteSpace(current.ModelId) ? "eleven_multilingual_v2" : current.ModelId;

        var fields = new FieldGrid();

        fields.AddRow("API Key:", FieldGrid.Inline(_txtApiKey, _chkShowApiKey));
        fields.AddRow("Voice:", FieldGrid.Inline(_cmbVoice, _btnLoadVoices));
        fields.AddRow("Model:", _cmbModel);

        var lblModelHint = new Label
        {
            Text = "Pick a preset or type any ElevenLabs model id. Style exaggeration has a real ceiling on v2 - it varies "
                 + "energy but won't give you a radio personality however far you push it, and past about 0.6 it starts "
                 + "distorting rather than exciting. eleven_v3 is the expressive model if you want genuine performance.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        var card = new Section("ElevenLabs connection");

        card.Add(lblUsedWhen);
        card.Add(fields);
        card.Add(lblModelHint, gapBelow: 0);

        return card;
    }

    /// <summary>
    /// Voice parameters: the ElevenLabs tuning in one caption column - the
    /// four voice settings, then the three root-level ones with the note on
    /// output formats, then Speaker boost - and playback in another, since
    /// it applies to every voice: the volume and the most spoken per lap.
    /// Test Voice, and what it said, under both.
    /// </summary>
    private Section BuildParametersCard(VoiceProfile current)
    {
        SetupUnitSlider(_numStability, current.Stability, 0, 1, 0.05m);
        SetupUnitSlider(_numSimilarity, current.SimilarityBoost, 0, 1, 0.05m);
        SetupUnitSlider(_numStyle, current.Style, 0, 1, 0.05m);
        SetupUnitSlider(_numSpeed, current.Speed, 0.7m, 1.2m, 0.05m);

        // Root-level parameters, not part of voice_settings - see the
        // comment in ElevenLabsClient.SynthesizeAsync. Short values most
        // clubs never touch.
        _cmbTextNormalization.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTextNormalization.Items.AddRange(new object[] { "auto", "on", "off" });
        _cmbTextNormalization.SelectedItem = string.IsNullOrWhiteSpace(current.TextNormalization) ? "auto" : current.TextNormalization;

        _txtLanguageCode.Text = current.LanguageCode;
        _txtLanguageCode.PlaceholderText = "auto";

        _txtOutputFormat.Text = current.OutputFormat;
        _txtOutputFormat.PlaceholderText = "default for your plan";

        // --- The two switches ---
        _chkSpeakerBoost.Text = "Speaker boost";
        _chkSpeakerBoost.AutoSize = true;
        _chkSpeakerBoost.Checked = current.SpeakerBoost;

        _chkWirelessSpeaker.Text = "Wireless speaker (adds wake-up delay)";
        _chkWirelessSpeaker.AutoSize = true;
        _chkWirelessSpeaker.Checked = current.WirelessSpeaker;

        // Kept, so the setting still saves as it was, but hidden: every line
        // now plays through one output that never sleeps (see AudioBus), so
        // there is no wake-up to wait for and the tick changed nothing.
        _chkWirelessSpeaker.Visible = false;

        var wirelessTip = new ToolTip();
        AppTheme.StyleToolTip(wirelessTip);
        wirelessTip.SetToolTip(_chkWirelessSpeaker,
            "A Bluetooth speaker powers down between sounds and swallows the start of whatever plays next, "
            + "so every line gets nearly a second of silence in front of it to give the speaker time to wake. "
            + "Turn this OFF for a wired speaker or audio going into OBS and that comes off the front of every "
            + "announcement - about three quarters of a second quicker, every time.");

        // --- Playback ---
        _numVolumePercent.Minimum = 0;
        _numVolumePercent.Maximum = 100;
        _numVolumePercent.Increment = 5;
        _numVolumePercent.Value = (decimal)Math.Clamp(current.Volume * 100.0, 0, 100);
        _volumeShown = _numVolumePercent.Value;

        _numMaxPerLap.Minimum = 1;
        _numMaxPerLap.Maximum = 5;
        _numMaxPerLap.Value = Math.Clamp(current.MaxAnnouncementsPerLap, 1, 5);

        // --- Test ---
        _btnTestVoice.Text = "Test Voice";
        _btnTestVoice.Click += BtnTestVoice_Click;

        // A raw ElevenLabs error can be a long JSON blob. SummariseApiError
        // keeps the readable part of it, and what is left wraps to the card,
        // which grows with it - it used to run past the group box it was
        // reporting from, over the Announcements section below.
        _lblTestStatus.AutoSize = true;
        _lblTestStatus.ForeColor = AppTheme.TextMuted;

        // Two caption columns, not one: "Max spoken announcements per lap:"
        // is more than twice the width of "Stability:", and one column for
        // the whole card left the short captions a long way from their
        // boxes.
        var tuning = new FieldGrid();

        tuning.AddRow("Stability:", _numStability);
        tuning.AddRow("Similarity boost:", _numSimilarity);
        tuning.AddRow("Style:", _numStyle);
        tuning.AddRow("Speed:", _numSpeed);
        tuning.AddRow("Text normalize:", _cmbTextNormalization);
        tuning.AddRow("Language:", _txtLanguageCode);
        tuning.AddRow("Output format:", _txtOutputFormat);
        tuning.AddHint("Higher-quality output formats need a paid ElevenLabs plan - leave blank unless you know you have one.");
        tuning.AddField(_chkSpeakerBoost);
        tuning.AddField(_chkWirelessSpeaker);

        var playback = new FieldGrid();

        playback.AddRow("Playback volume (%):", _numVolumePercent);
        playback.AddRow("Max spoken announcements per lap:", _numMaxPerLap);

        var card = new Section("Voice parameters");

        card.Add(tuning, gapBelow: UiLayout.SectionGap);
        card.Add(playback);
        card.Add(new ButtonBar().With(_btnTestVoice), gapBelow: UiLayout.HintGap);
        card.Add(_lblTestStatus, gapBelow: 0);

        return card;
    }

    /// <summary>
    /// The saved-voice library: how much is kept, Prepare to render every
    /// line that can be known in advance, and a way to forget a line that
    /// came out wrong. See VoiceLibrary for how lines are matched.
    /// </summary>
    private Section BuildLibraryCard()
    {
        var lblIntro = new Label
        {
            Text = "Every line ElevenLabs speaks is kept on this PC and played from here the next time the same words are "
                 + "wanted in the same voice and settings - so each line is paid for once, and restarting the app loses "
                 + "nothing. Change the voice, a setting or a line's words and only the lines that changed are rendered again.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        // Wraps to the card, a line kept for it (BuildLayout); anything long
        // goes to the console in full as well.
        _lblLibraryStatus.AutoSize = true;
        _lblLibraryStatus.ForeColor = AppTheme.TextDark;
        _lblLibraryStatus.Text = "Counting saved lines...";

        // --- Prepare / Stop, with its progress beside them ---
        _btnPrepareLines.Text = "Prepare Lines...";
        _btnStopPreparing.Text = "Stop";
        _btnStopPreparing.Enabled = false;

        _btnPrepareLines.Click += BtnPrepareLines_Click;
        _btnStopPreparing.Click += (_, _) => _prepareCancel?.Cancel();

        // The rest of the buttons' line, cut short with "..." rather than
        // wrapped, and on a line of its own when there is not the least room
        // for it there (FieldRow). The full summary of a run goes to the
        // console.
        _lblPrepareProgress.AutoSize = false;
        _lblPrepareProgress.AutoEllipsis = true;
        _lblPrepareProgress.ForeColor = AppTheme.TextMuted;
        _lblPrepareProgress.TextAlign = ContentAlignment.MiddleLeft;

        var prepare = new FieldRow();

        prepare.Add(null, new ButtonBar().With(_btnPrepareLines, _btnStopPreparing));
        prepare.AddLast(null, _lblPrepareProgress, () => UiLayout.AtTextSize(PrepareProgressLeastWidth));

        var lblPrepareHint = _textSize.Wear(
            new Label
            {
                Text = "Prepare renders, in one go, every line whose words are already known: each switched-on announcement's "
                     + "lines that have no {placeholder} - the flags among them - and the release call for every driver checked in "
                     + "for the current event. Lines built from live values (lap times, positions, streaks) can't be made in advance; "
                     + "they're saved the first time they're spoken. It uses your ElevenLabs quota, so it asks first and says how much.",
                AutoSize = true,
                ForeColor = AppTheme.TextMuted,
            },
            () => AppTheme.SmallFont);

        // --- Recently played, and forgetting ---
        _cmbSavedLines.DropDownStyle = ComboBoxStyle.DropDownList;

        _btnForgetLine.Text = "Forget Line";
        _btnForgetLine.Enabled = false;
        _btnForgetLine.Click += BtnForgetLine_Click;

        _btnForgetAll.Text = "Forget All...";
        _btnForgetAll.Enabled = false;
        _btnForgetAll.Click += BtnForgetAll_Click;

        // The list across the whole field column - a saved line is a
        // sentence, and the more of it shows the easier it is to pick the
        // right one to forget. Its buttons and hint under it start at the
        // card's left edge, as Prepare Lines and its hint do, rather than
        // under the list with an empty gap below the caption.
        var recent = new FieldGrid();

        recent.AddRow("Recently played:", _cmbSavedLines, stretch: true);

        var forget = new ButtonBar().With(_btnForgetLine, _btnForgetAll);

        var lblForgetHint = _textSize.Wear(
            new Label
            {
                Text = "A line that came out wrong: forget it and it's rendered afresh the next time it's wanted. For a flag, or any "
                     + "line with no {placeholder}, pressing Test on its announcement below does the same - a test always records a "
                     + "new take, and the race plays the one heard last. "
                     + $"Kept in {VoiceLibrary.Shared.Folder} (not part of backups - it can always be made again), up to "
                     + $"{VoiceLibrary.MaxBytes / (1024 * 1024)} MB; past that, the lines played least recently make way.",
                AutoSize = true,
                ForeColor = AppTheme.TextMuted,
            },
            () => AppTheme.SmallFont);

        var card = new Section("Saved voice lines");

        card.Add(lblIntro);
        card.Add(_lblLibraryStatus);
        card.Add(prepare, gapBelow: UiLayout.HintGap);
        card.Add(lblPrepareHint);
        card.Add(recent);
        card.Add(forget, gapBelow: UiLayout.HintGap);
        card.Add(lblForgetHint, gapBelow: 0);

        return card;
    }

    /// <summary>One entry in the Recently played list.</summary>
    private sealed record SavedLineItem(VoiceLibrary.SavedLine Line)
    {
        public override string ToString()
        {
            var words = Line.Text.Length <= 90 ? Line.Text : Line.Text[..87] + "...";
            return $"{words}   ({Line.LastUsedUtc.ToLocalTime():g})";
        }
    }

    /// <summary>Asks for the count and the list to be read again, shortly - see _libraryRefreshTimer.</summary>
    private void RequestLibraryRefresh()
    {
        if (Gone)
        {
            return;
        }

        _libraryRefreshTimer.Stop();
        _libraryRefreshTimer.Start();
    }

    /// <summary>Reads the library off the UI thread and shows what it holds.</summary>
    private async void RefreshLibraryNow()
    {
        _libraryRefreshTimer.Stop();

        // Not while hidden: showing the page asks again (OnVisibleChanged).
        if (Gone || !Visible)
        {
            return;
        }

        // One read at a time - but a change that arrived during it is not
        // lost: it is read again as soon as this one is done.
        if (_libraryRefreshRunning)
        {
            _libraryRefreshAgain = true;
            return;
        }

        _libraryRefreshRunning = true;

        try
        {
            var (stats, recent) = await Task.Run(() =>
                (VoiceLibrary.Shared.GetStats(), VoiceLibrary.Shared.Recent(RecentLinesShown)));

            if (Gone)
            {
                return;
            }

            _lblLibraryStatus.Text = stats.Lines == 0
                ? "Nothing saved yet - lines are kept as they're spoken, or all at once with Prepare."
                : $"{Plural.Of(stats.Lines, "line")} saved, {stats.Bytes / (1024.0 * 1024.0):0.0} MB.";

            var selectedKey = (_cmbSavedLines.SelectedItem as SavedLineItem)?.Line.Key;

            _cmbSavedLines.BeginUpdate();
            try
            {
                _cmbSavedLines.Items.Clear();

                foreach (var line in recent)
                {
                    _cmbSavedLines.Items.Add(new SavedLineItem(line));
                }

                var reselect = -1;
                for (var i = 0; i < _cmbSavedLines.Items.Count && selectedKey is not null; i++)
                {
                    if (((SavedLineItem)_cmbSavedLines.Items[i]!).Line.Key == selectedKey)
                    {
                        reselect = i;
                        break;
                    }
                }

                _cmbSavedLines.SelectedIndex = reselect >= 0 ? reselect : (_cmbSavedLines.Items.Count > 0 ? 0 : -1);
            }
            finally
            {
                _cmbSavedLines.EndUpdate();
            }

            // The open list as wide as the longest line now in it - see SizeFields.
            RefitPickers();

            _btnForgetLine.Enabled = _cmbSavedLines.Items.Count > 0;
            _btnForgetAll.Enabled = stats.Lines > 0;
        }
        catch (Exception ex)
        {
            if (!Gone)
            {
                _lblLibraryStatus.Text = $"Couldn't read the saved lines: {ex.Message}";
            }
        }
        finally
        {
            _libraryRefreshRunning = false;

            if (_libraryRefreshAgain)
            {
                _libraryRefreshAgain = false;
                RequestLibraryRefresh();
            }
        }
    }

    /// <summary>The library changed - raised on whatever thread saved or forgot the line.</summary>
    private void OnLibraryChanged()
    {
        try
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(new Action(RequestLibraryRefresh));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // The page went away between the check and the post.
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        VoiceLibrary.Shared.Changed += OnLibraryChanged;
        RequestLibraryRefresh();

        // The HeatCaller voices are asked for HERE and not in the
        // constructor: the host hands this page its relay client in an
        // object initialiser, which runs after the constructor body, so a
        // load started there found no client and left the page saying the
        // voice wasn't available - with the choice greyed out and no way
        // back but Refresh. The handle is also what makes it safe to touch
        // controls after an await (see Gone).
        _ = LoadHeatCallerCatalogueAsync(byHand: false);

        // A Kokoro download started on the page this one replaced.
        if (KokoroSpeech.RunningDownload is { } download)
        {
            _ = FollowKokoroDownloadAsync(download);
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        // The library is shared and lives as long as the app; this page is
        // rebuilt on a theme change, and must not stay subscribed after.
        VoiceLibrary.Shared.Changed -= OnLibraryChanged;
        base.OnHandleDestroyed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            VoiceLibrary.Shared.Changed -= OnLibraryChanged;
            _store.Restored -= OnStoreRestored;
            _prepareCancel?.Cancel();

            // Not canceled: a Kokoro download carries on, and the page built
            // next follows it.
            if (_kokoroProgress is { } report)
            {
                KokoroSpeech.DownloadProgressed -= report;
            }

            _libraryRefreshTimer.Stop();
            _libraryRefreshTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private async void BtnPrepareLines_Click(object? sender, EventArgs e)
    {
        if (Announcer is not { } announcer)
        {
            AppDialog.Show(this, "The announcer isn't running yet - try again in a moment.",
                "Not ready", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // The SAVED profile - it is what races read, and what Prepare has to
        // match. This page saves every change as it is made.
        var saved = _store.Current;

        // The button is disabled with the Windows voice chosen; this is for
        // the moment between choosing it and the page catching up.
        if (saved.EffectiveEngine is VoiceEngine.Windows or VoiceEngine.Kokoro)
        {
            AppDialog.Show(this,
                "Prepare Lines renders lines in your ElevenLabs voice. With the "
                + (saved.EffectiveEngine == VoiceEngine.Kokoro ? "Kokoro" : "Windows") + " voice chosen at the top of the page, "
                + "every line is spoken as it's needed, so there is nothing to prepare.",
                "Not needed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Each engine needs something different before it can render. The
        // HeatCaller voice is rendered by the relay with HeatCaller's own key,
        // so it needs only a voice chosen from the relay's list - never the
        // club's ElevenLabs key or voice, which a club on this voice usually
        // doesn't have (and can't type in: the key box is locked while the
        // HeatCaller voice is chosen). This is the same test
        // PlanLinesToPrepare makes, so the two can't disagree about whether
        // there is a voice to render with.
        if (saved.EffectiveEngine == VoiceEngine.HeatCaller)
        {
            if (!saved.HeatCallerReady)
            {
                AppDialog.Show(this, "Choose a HeatCaller voice first.", "Missing settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }
        else if (string.IsNullOrWhiteSpace(saved.ApiKey) || string.IsNullOrWhiteSpace(saved.VoiceId))
        {
            AppDialog.Show(this, "Enter an API key and select a voice first.", "Missing settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        IReadOnlyList<AnnouncementService.PlannedLine> plan;
        try
        {
            plan = announcer.PlanLinesToPrepare();
        }
        catch (Exception ex)
        {
            // An async void handler: anything escaping it takes the app down.
            _lblPrepareProgress.Text = $"Couldn't work out which lines to prepare: {ex.Message}";
            Status($"Prepare lines - couldn't build the list: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        if (plan.Count == 0)
        {
            AppDialog.Show(this,
                "There is nothing to prepare: none of the announcements that are switched on has a line without a {placeholder}, "
                + "and nobody is checked in for a release call. Lines with placeholders are filled in live and saved the first time they're spoken.",
                "Nothing to prepare", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _btnPrepareLines.Enabled = false;
        _lblPrepareProgress.Text = "Checking what's already saved...";

        List<AnnouncementService.PlannedLine> toRender;
        try
        {
            toRender = await Task.Run(() => plan.Where(line => !VoiceLibrary.Shared.Contains(line.Profile, line.Text)).ToList());
        }
        catch (Exception ex)
        {
            if (!Gone)
            {
                _lblPrepareProgress.Text = $"Couldn't check the saved lines: {ex.Message}";
                RestorePrepareButton();
            }

            return;
        }

        if (Gone)
        {
            return;
        }

        var alreadySaved = plan.Count - toRender.Count;

        if (toRender.Count == 0)
        {
            _lblPrepareProgress.Text = plan.Count == 1
                ? "The one line to prepare is already saved - nothing to render."
                : $"All {plan.Count} lines are already saved - nothing to render.";
            RestorePrepareButton();
            return;
        }

        var characters = toRender.Sum(line => (long)line.Text.Length);

        var release = saved.Events.GetValueOrDefault(nameof(AnnouncementEventType.DriverRelease));
        var releaseOn = release is not null
                        && (release.IsEnabledFor(false) || release.IsEnabledFor(true))
                        && release.EffectiveTemplates.Count > 0;
        var drivers = plan.Where(line => line.Transponder is not null)
            .Select(line => line.Transponder!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var releaseNote = !releaseOn
            ? string.Empty
            : drivers == 0
                ? "Nobody is checked in for the current event yet, so release calls aren't included - check drivers in first, "
                  + "or each driver's is saved the first time it's called.\r\n\r\n"
                : $"That includes the release calls for the {Plural.Of(drivers, "driver")} checked in.\r\n\r\n";

        var question =
            $"Render {Plural.Of(toRender.Count, "line")} now?\r\n\r\n"
            + (alreadySaved > 0 ? $"{Plural.Of(alreadySaved, "more line")} {(alreadySaved == 1 ? "is" : "are")} already saved and will be skipped.\r\n\r\n" : string.Empty)
            + releaseNote
            + (_rdoHeatCaller.Checked
                ? $"That's about {characters:N0} characters of this club's monthly HeatCaller allowance at most - each line is then played from this PC, "
                  + "and the lines with no driver's name in them are rendered once for every club, so most of them cost nothing at all.\r\n\r\n"
                : $"That's about {characters:N0} characters of your ElevenLabs quota, used once - each line is then played from this PC.\r\n\r\n")
            + "It works through them one at a time and steps aside whenever the announcer is speaking. "
            + "You can stop it at any point, and everything done by then is kept.";

        if (AppDialog.Show(this, question, "Prepare lines", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            _lblPrepareProgress.Text = string.Empty;
            RestorePrepareButton();
            return;
        }

        var cancel = new CancellationTokenSource();
        _prepareCancel = cancel;
        _btnStopPreparing.Enabled = true;

        // Reported on this thread - Progress captures it.
        var progress = new Progress<(int Done, int Total)>(p =>
        {
            if (!Gone)
            {
                _lblPrepareProgress.Text = $"Preparing {Math.Min(p.Done + 1, p.Total)} of {p.Total}...";
            }
        });

        Status($"Preparing {Plural.Of(toRender.Count, "line")} (about {characters:N0} characters).");

        try
        {
            var outcome = await announcer.PrepareLinesAsync(toRender, progress, cancel.Token);

            var parts = new List<string> { $"{Plural.Of(outcome.Rendered, "line")} rendered and saved" };

            if (outcome.AlreadySaved > 0)
            {
                parts.Add($"{outcome.AlreadySaved} already saved");
            }

            if (outcome.Failed > 0)
            {
                parts.Add($"{outcome.Failed} failed - the console says why");
            }

            var summary = string.Join(", ", parts) + ".";
            summary = outcome.StoppedBecause is null ? $"Done: {summary}" : $"Stopped because {outcome.StoppedBecause}. {summary}";

            Status($"Prepare lines - {summary}");

            if (!Gone)
            {
                _lblPrepareProgress.Text = summary;
            }
        }
        catch (Exception ex)
        {
            Status($"Prepare lines failed: {ex.Message}");

            if (!Gone)
            {
                _lblPrepareProgress.Text = $"Prepare failed: {SummariseApiError(ex.Message)}";
            }
        }
        finally
        {
            if (ReferenceEquals(_prepareCancel, cancel))
            {
                _prepareCancel = null;
            }

            cancel.Dispose();

            if (!Gone)
            {
                // Not back on if the Windows voice was chosen during the run
                // (which is what stopped it) - the summary went to the log.
                RestorePrepareButton();
                _btnStopPreparing.Enabled = false;
                RequestLibraryRefresh();
            }
        }
    }

    private void BtnForgetLine_Click(object? sender, EventArgs e)
    {
        if (_cmbSavedLines.SelectedItem is not SavedLineItem item)
        {
            return;
        }

        // Held rather than deleted, so undo can put the take back - a line
        // forgotten by mistake would otherwise be paid for again.
        string? holdIn = null;
        if (_undo is not null)
        {
            var held = UndoHoldingFolder.NewFolder(local: true);
            holdIn = held;
            _undo.Capture($"Forget saved voice line \"{(item.Line.Text.Length > 40 ? item.Line.Text[..37] + "..." : item.Line.Text)}\"",
                () => VoiceLibrary.Shared.PutBackHeld(held));
        }

        if (VoiceLibrary.Shared.Forget(item.Line.Key, holdIn))
        {
            // Whatever the announcer is holding in memory goes too, or the
            // forgotten take would play once more from there.
            Announcer?.ForgetPreparedAudio();
            Status($"Forgot a saved line - it will be rendered afresh the next time it's wanted: \"{item.Line.Text}\"");
        }

        RequestLibraryRefresh();
    }

    private async void BtnForgetAll_Click(object? sender, EventArgs e)
    {
        if (AppDialog.Show(this,
                "Forget every saved voice line?\r\n\r\n"
                + "Nothing is lost that can't be made again, but every line will be rendered - and paid for - afresh the next time "
                + "it's needed. Worth doing after changing something this app can't see, such as editing a cloned voice in your "
                + "ElevenLabs account.",
                "Forget all saved lines", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        _btnForgetAll.Enabled = false;

        // Moved into a holding folder rather than deleted - a rename on the
        // same drive, so no audio is held in memory however big the library -
        // and the undo moves them back on a background thread for the same
        // reason the forgetting runs on one. A line rendered again since
        // keeps its newer take.
        //
        // The undo waits for the forgetting to finish first. Pressed while
        // it was still moving files, it would put back only what had gone so
        // far, and everything moved afterwards would stay in the holding
        // folder with nothing left to bring it back.
        string? holdIn = null;
        Task<int>? forgetting = null;
        if (_undo is not null)
        {
            var held = UndoHoldingFolder.NewFolder(local: true);
            holdIn = held;
            _undo.Capture("Forget all saved voice lines", () =>
            {
                var pending = forgetting;
                _ = Task.Run(async () =>
                {
                    if (pending is not null)
                    {
                        try
                        {
                            await pending.ConfigureAwait(false);
                        }
                        catch
                        {
                            // Whatever did get moved still comes back.
                        }
                    }

                    VoiceLibrary.Shared.PutBackHeld(held);
                });
            });
        }

        try
        {
            forgetting = Task.Run(() => VoiceLibrary.Shared.ForgetAll(holdIn));
            var removed = await forgetting;
            Announcer?.ForgetPreparedAudio();
            Status($"Forgot {Plural.Of(removed, "saved line")}.");
        }
        catch (Exception ex)
        {
            Status($"Couldn't forget the saved lines: {ex.Message}");
        }
        finally
        {
            RequestLibraryRefresh();
        }
    }

    private static void SetupUnitSlider(NumericUpDown control, double value, decimal min, decimal max, decimal increment)
    {
        control.Minimum = min;
        control.Maximum = max;
        control.Increment = increment;
        control.DecimalPlaces = 2;
        control.Value = Math.Clamp((decimal)value, min, max);
    }

    // Internal for VoiceSettingsStore.Load, which repairs the weights of the
    // events NOT listed here (see WeightWithoutBox).
    internal static readonly HashSet<AnnouncementEventType> PerLapWeightedTypes = new()
    {
        AnnouncementEventType.FastLap,
        AnnouncementEventType.PersonalBestLap,
        AnnouncementEventType.JumpStart,
        AnnouncementEventType.LeaderChange,
        AnnouncementEventType.LapCompleted,
        AnnouncementEventType.LapTime,
        AnnouncementEventType.Streak,
    };

    /// <summary>
    /// True once this page is going away. Checked after every await in the
    /// async handlers: the user can switch tabs mid-request, and touching a
    /// control on a disposed page from the continuation throws.
    /// </summary>
    private bool Gone => IsDisposed || Disposing || !IsHandleCreated;

    private async void BtnLoadVoices_Click(object? sender, EventArgs e)
    {
        var apiKey = _txtApiKey.Text.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            AppDialog.Show(this, "Enter your ElevenLabs API key first.", "Missing API key", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _btnLoadVoices.Enabled = false;
        _lblTestStatus.Text = "Loading voices...";
        try
        {
            var voices = await _elevenLabs.GetVoicesAsync(apiKey);
            if (Gone) { return; }
            var previousItem = _cmbVoice.SelectedItem as ElevenLabsClient.VoiceInfo;
            var previouslySelected = previousItem?.VoiceId;

            // The real name, not the "(not in this account)" note, when the
            // voice was already missing from the last list loaded.
            var previousName = previousItem is null ? string.Empty
                : ReferenceEquals(previousItem, _voiceNotInAccount) ? _voiceNotInAccountName
                : previousItem.Name;

            _cmbVoice.Items.Clear();
            _voiceNotInAccount = null;
            _voiceNotInAccountName = string.Empty;

            foreach (var v in voices)
            {
                _cmbVoice.Items.Add(v);
            }

            if (previouslySelected is not null)
            {
                var found = false;
                for (var i = 0; i < _cmbVoice.Items.Count; i++)
                {
                    if (((ElevenLabsClient.VoiceInfo)_cmbVoice.Items[i]!).VoiceId == previouslySelected)
                    {
                        _cmbVoice.SelectedIndex = i;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    // The voice that was chosen is not in this account's list -
                    // the usual way here is pasting another account's key. It
                    // is kept, chosen, with a note, the way the HeatCaller
                    // voice list keeps a voice the relay stopped offering.
                    // Left blank, the box said "no voice" while every save
                    // went on writing the old voice's id behind it, so races
                    // asked the new account for a voice it does not have and
                    // Test Voice got past its own "select a voice" check only
                    // to fail. Dropping the voice instead would throw away a
                    // choice that may still be wanted (a key pasted by
                    // mistake, to be put back), so the page shows exactly
                    // what is saved and the club picks again from the list.
                    _voiceNotInAccountName = previousName;
                    _voiceNotInAccount = new ElevenLabsClient.VoiceInfo(
                        previouslySelected,
                        $"{(string.IsNullOrWhiteSpace(previousName) ? previouslySelected : previousName)} (not in this account)");
                    _cmbVoice.SelectedIndex = _cmbVoice.Items.Add(_voiceNotInAccount);
                }
            }
            else if (_cmbVoice.Items.Count > 0)
            {
                _cmbVoice.SelectedIndex = 0;
            }

            // The longest voice may have come or gone - see SizeFields.
            RefitPickers();

            _lblTestStatus.Text = $"Loaded {Plural.Of(voices.Count, "voice")}.";
        }
        catch (Exception ex)
        {
            _lblTestStatus.Text = $"Failed to load voices: {ex.Message}";
        }
        finally
        {
            if (!Gone)
            {
                _btnLoadVoices.Enabled = true;
            }
        }
    }

    private async void BtnTestVoice_Click(object? sender, EventArgs e)
    {
        var profile = BuildProfileFromControls();
        if (string.IsNullOrWhiteSpace(profile.ApiKey) || string.IsNullOrWhiteSpace(profile.VoiceId))
        {
            AppDialog.Show(this, "Enter an API key and select a voice first.", "Missing settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _btnTestVoice.Enabled = false;
        _lblTestStatus.Text = "Synthesizing test phrase...";
        try
        {
            // Always a fresh render - a test is for hearing the settings as
            // they are now, not a copy saved under the same ones earlier.
            var audio = await AnnouncementService.SynthesizeThroughGateAsync(_elevenLabs, profile, "This is HeatCaller testing your announcer voice.", reuseSaved: false);
            if (Gone) { return; }
            _lblTestStatus.Text = "Playing...";
            await PlayTestAudioAsync(audio, profile.Volume);
            if (Gone) { return; }
            _lblTestStatus.Text = "Done.";
        }
        catch (Exception ex)
        {
            // Trimmed and de-JSONed. The raw message is a wall of escaped
            // JSON that tells most people nothing and wrapped to enough
            // lines to break the layout - the useful part is the status
            // code and the human-readable message inside it.
            _lblTestStatus.Text = $"Test failed: {SummariseApiError(ex.Message)}";
        }
        finally
        {
            if (!Gone)
            {
                _btnTestVoice.Enabled = true;
            }
        }
    }

    private VoiceProfile BuildProfileFromControls()
    {
        var selectedVoice = _cmbVoice.SelectedItem as ElevenLabsClient.VoiceInfo;

        var profile = new VoiceProfile
        {
            // Stamped, or the saved file reads as version 0 - which the
            // store treats as "written before the current built-in lines
            // existed" and answers by replacing every event's lines with
            // the defaults on the next launch. So any phrasing edited here
            // came back as the stock wording after a restart, every time.
            TemplateVersion = VoiceSettingsStore.CurrentTemplateVersion,

            ApiKey = _txtApiKey.Text.Trim(),
            // With nothing chosen (the moment Load Voices empties the list
            // to refill it), the voice last SAVED - not the one the page
            // opened with, which would quietly undo a voice picked and saved
            // since. The "not in this account" entry saves the voice's real
            // name, not the note shown beside it.
            VoiceId = selectedVoice?.VoiceId ?? _store.Current.VoiceId,
            VoiceName = selectedVoice is null ? _store.Current.VoiceName
                : ReferenceEquals(selectedVoice, _voiceNotInAccount) ? _voiceNotInAccountName
                : selectedVoice.Name,
            ModelId = string.IsNullOrWhiteSpace(_cmbModel.Text) ? "eleven_multilingual_v2" : _cmbModel.Text.Trim(),
            Stability = (double)_numStability.Value,
            SimilarityBoost = (double)_numSimilarity.Value,
            Style = (double)_numStyle.Value,
            Speed = (double)_numSpeed.Value,
            SpeakerBoost = _chkSpeakerBoost.Checked,
            TextNormalization = _cmbTextNormalization.SelectedItem as string ?? "auto",
            LanguageCode = _txtLanguageCode.Text.Trim(),
            OutputFormat = _txtOutputFormat.Text.Trim(),
            Volume = (double)(_numVolumePercent.Value / 100m),
            WirelessSpeaker = _chkWirelessSpeaker.Checked,
            MaxAnnouncementsPerLap = (int)_numMaxPerLap.Value,

            // The Speak With card. Built field by field like everything
            // here, so leaving one out would reset it on the next save.
            Engine = _rdoHeatCaller.Checked ? VoiceEngine.HeatCaller
                : _rdoWindows.Checked ? VoiceEngine.Windows
                : _rdoKokoro.Checked ? VoiceEngine.Kokoro
                : VoiceEngine.ElevenLabs,
            WindowsVoiceName = (_cmbWindowsVoice.SelectedItem as WindowsVoiceChoice)?.Name ?? _original.WindowsVoiceName,
            KokoroVoiceName = (_cmbKokoroVoice.SelectedItem as KokoroVoiceChoice)?.Name ?? _original.KokoroVoiceName,
            WindowsVoiceStandsIn = _chkWindowsStandsIn.Checked,

            // The HeatCaller voice: the club's choice, and the relay's
            // fingerprint for it as the catalogue last gave it. Both are
            // kept even while another voice is chosen, so switching back
            // does not mean choosing again - and the rev is what makes a
            // voice re-tuned on the relay re-render rather than play a
            // saved take that no longer matches.
            HeatCallerVoice = (_cmbHeatCallerVoice.SelectedItem as HeatCallerVoiceChoice)?.Id ?? _original.HeatCallerVoice,
            HeatCallerVoiceName = (_cmbHeatCallerVoice.SelectedItem as HeatCallerVoiceChoice)?.Name ?? _original.HeatCallerVoiceName,
            HeatCallerRev = HeatCallerRevToSave(),
        };

        foreach (var (type, (chkRace, chkPractice, txt)) in _eventRows)
        {
            var templates = txt.Text
                .Split('\n')
                .Select(line => line.Trim('\r', ' '))
                .Where(line => line.Length > 0)
                .ToList();

            profile.Events[type.ToString()] = new AnnouncementEventConfig
            {
                // Both ticks are on the row now, so both are simply read.
                // This used to save the visible box for whichever context
                // was being edited and take the other out of a remembered
                // dictionary - which is what let a rebuild at the wrong
                // moment write over the set that was not on screen.
                Enabled = chkRace.Checked,
                EnabledInPractice = chkPractice.Checked,
                Templates = templates,
                IncludeLapTime = (type == AnnouncementEventType.LapCompleted && _chkIncludeLapTime.Checked)
                    || (type == AnnouncementEventType.FastLap && _chkFastLapIncludeTime.Checked),
                OnlyBestLap = type == AnnouncementEventType.LapCompleted && _chkOnlyBestLap.Checked,
                SayLapTimeLabel = type != AnnouncementEventType.LapTime || _chkSayLapTimeLabel.Checked,
                IncludeDriverNameInLapTime = type == AnnouncementEventType.LapTime && _chkIncludeDriverNameInLapTime.Checked,
                SpeedMultiplier = _eventSpeeds.TryGetValue(type, out var speedBox)
                    ? (double)speedBox.Value
                    : 1.0,
                AllowUnderCaution = _eventCaution.TryGetValue(type, out var chkCaution) && chkCaution.Checked,
                OnlyLongestStreak = type == AnnouncementEventType.Streak && _chkOnlyLongestStreak.Checked,
                MentionStreakChaser = type == AnnouncementEventType.Streak && _chkMentionStreakChaser.Checked,
                StreakAnnounceEveryN = type == AnnouncementEventType.Streak
                    ? (int)_numStreakEveryN.Value
                    : 1,
                StreakAnnounceEveryNInPractice = type == AnnouncementEventType.Streak
                    ? (int)_numStreakEveryNPractice.Value
                    : null,
                LapTimeEveryNLaps = type == AnnouncementEventType.LapTime ? (int)_numLapTimeEveryN.Value : 1,
                PositionReportEveryLaps = type == AnnouncementEventType.PositionReport ? (int)_numPositionEveryLaps.Value : 5,
                PositionReportDepth = type == AnnouncementEventType.PositionReport ? (int)_numPositionDepth.Value : 3,
                Weight = _eventWeights.TryGetValue(type, out var numWeight) ? (int)numWeight.Value : WeightWithoutBox(type),
            };
        }

        return profile;
    }

    /// <summary>
    /// The weight saved for an event with no weight box on this page - the
    /// one it already has. This used to save a flat 50, so any change here
    /// knocked Provisional Pole (92) out of the per-lap cap, put Streak Lead
    /// behind the Streak line it was weighted to beat, and tied the pair
    /// lines, with no box to set them back.
    /// </summary>
    private int WeightWithoutBox(AnnouncementEventType type)
    {
        if (_store.Current.Events.TryGetValue(type.ToString(), out var saved) && saved is not null)
        {
            return saved.Weight;
        }

        return VoiceProfile.CreateDefault().Events.TryGetValue(type.ToString(), out var defaults) && defaults is not null
            ? defaults.Weight
            : 50;
    }

    /// <summary>
    /// Pulls the readable part out of an ElevenLabs error.
    ///
    /// Their failures arrive as escaped JSON - useful in a log, unreadable
    /// in a status line, and long enough to wrap past the group box. This
    /// keeps the status code and the "message" field, which is the bit that
    /// actually says what went wrong.
    /// </summary>
    private static string SummariseApiError(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "unknown error";
        }

        // The human-readable part ElevenLabs puts in "message".
        var match = System.Text.RegularExpressions.Regex.Match(raw, "\"message\"\\s*:\\s*\"([^\"]+)\"");
        if (match.Success)
        {
            var code = System.Text.RegularExpressions.Regex.Match(raw, @"\((\d{3})\)");
            return code.Success
                ? $"{code.Groups[1].Value} - {match.Groups[1].Value}"
                : match.Groups[1].Value;
        }

        // No JSON message - just keep it short enough to fit.
        return raw.Length <= 160 ? raw : raw[..157] + "...";
    }

    /// <summary>
    /// Speaks one line from an event's template box, as the race would.
    ///
    /// Uses the settings currently ON SCREEN, not what's saved - so a
    /// phrasing, a speed or a voice change can be tried before committing
    /// to it. Placeholders are filled with sample values; an unresolved
    /// {driver} would tell you nothing about how the line sounds.
    /// </summary>
    private async void TestEventLine(AnnouncementEventType type, TextBox box, NumericUpDown speed, Button button)
    {
        var lines = box.Text
            .Split('\n')
            .Select(l => l.Trim('\r', ' '))
            .Where(l => l.Length > 0)
            .ToList();

        if (lines.Count == 0)
        {
            _lblTestStatus.Text = "Nothing to test - that event has no lines.";
            _lblTestStatus.ForeColor = AppTheme.Danger;
            return;
        }

        // Built from the controls, so unsaved edits are what gets spoken -
        // the voice chosen at the top of the page included.
        var profile = BuildProfileFromControls();
        var kokoro = profile.EffectiveEngine == VoiceEngine.Kokoro;

        // "On this PC" for everything below that isn't ElevenLabs: Kokoro's
        // test is Kokoro itself, never the stand-in, so a test shows what
        // Kokoro says - or why it can't.
        var windows = profile.EffectiveEngine == VoiceEngine.Windows || kokoro;

        if (kokoro)
        {
            if (KokoroSpeech.UnavailableReason is { } unavailable)
            {
                _lblTestStatus.Text = $"Kokoro can't speak yet - {unavailable}.";
                _lblTestStatus.ForeColor = AppTheme.Danger;
                return;
            }
        }
        else if (windows)
        {
            if (WindowsVoice.UnavailableReason is { } unavailable)
            {
                _lblTestStatus.Text = $"No Windows voice can speak on this PC - {unavailable}.";
                _lblTestStatus.ForeColor = AppTheme.Danger;
                return;
            }
        }
        else if (string.IsNullOrWhiteSpace(profile.ApiKey) || string.IsNullOrWhiteSpace(profile.VoiceId))
        {
            _lblTestStatus.Text = "Enter an API key and select a voice first.";
            _lblTestStatus.ForeColor = AppTheme.Danger;
            return;
        }

        var line = lines[Random.Shared.Next(lines.Count)];

        var spoken = line
            .Replace("{driver}", "Sam", StringComparison.OrdinalIgnoreCase)
            .Replace("{rival}", "Bill", StringComparison.OrdinalIgnoreCase)
            .Replace("{car}", "Mini-Z MR03", StringComparison.OrdinalIgnoreCase)
            .Replace("{lap}", "7", StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", "6.92 seconds", StringComparison.OrdinalIgnoreCase)
            .Replace("{laptime}", "6.92 seconds", StringComparison.OrdinalIgnoreCase)
            .Replace("{gate}", "1", StringComparison.OrdinalIgnoreCase)
            .Replace("{positions}", "P1 Sam, P2 Bill, P3 Frank", StringComparison.OrdinalIgnoreCase)
            .Replace("{round}", "Heat 2", StringComparison.OrdinalIgnoreCase)
            .Replace("{class}", "Stock Buggy", StringComparison.OrdinalIgnoreCase)
            .Replace("{marshals}", "Sam, Bill and Frank", StringComparison.OrdinalIgnoreCase)
            .Replace("{sector}", "1", StringComparison.OrdinalIgnoreCase)
            .Replace("{sectortime}", "3.41 seconds", StringComparison.OrdinalIgnoreCase)
            .Replace("{split}", "Split 1", StringComparison.OrdinalIgnoreCase)
            .Replace("{gap}", "point two seconds", StringComparison.OrdinalIgnoreCase)

            // Tidied as the race tidies every line, so a fixed line tested
            // here is word for word the one the race asks for.
            .TidyTemplate();

        // Spoken at this event's own rate, so a slowed-down position report
        // is tested at the speed it will actually be read at.
        var testProfile = profile.CloneForDelivery(
            profile.Stability,
            profile.Style,
            Math.Clamp(profile.Speed * (double)speed.Value, 0.7, 1.2));

        // Tags stripped for models that can't use them, exactly as the real
        // announcement path does - otherwise a v2 test would read the
        // brackets aloud and look like a bug.
        spoken = testProfile.SanitiseForModel(spoken);

        // Disabling the button that was just clicked makes WinForms hand
        // focus to the next control in the tab order, and the scrolling
        // panel this page lives in dutifully scrolls THAT into view - which,
        // from a Test button most of the way down a long list of events,
        // threw the page back to the top. Keeping the position across the
        // whole test is the fix; the same applies when it's re-enabled.
        PreservingScroll(() =>
        {
            button.Enabled = false;
            _lblTestStatus.Text = $"Testing {type}...";
            _lblTestStatus.ForeColor = AppTheme.TextMuted;
        });

        try
        {
            byte[] audio;

            if (kokoro)
            {
                // Kokoro, as a race would use it - and, like the Windows
                // voice, nothing saved and nothing held for the race let go.
                audio = await KokoroSpeech.RenderAsync(testProfile, spoken);
            }
            else if (windows)
            {
                // The Windows voice, as a race would use it. Nothing is saved
                // and nothing held for the race is let go - there is no
                // ElevenLabs take involved.
                audio = await WindowsVoice.RenderAsync(testProfile, spoken);
            }
            else
            {
                // A fresh take every time, never the saved copy - and the
                // take replaces the saved one. For a line with no
                // {placeholder} this is the exact request the race makes, so
                // testing a flag or a fixed line until it sounds right is how
                // a bad take is fixed: the race plays what was heard last.
                //
                // ElevenLabs only, never the stand-in: a test has to show
                // what ElevenLabs said, error and all.
                audio = await AnnouncementService.SynthesizeThroughGateAsync(_elevenLabs, testProfile, spoken, reuseSaved: false);

                // Anything already rendered and held for the race - the next
                // flag line, the grid's release calls - is let go, so it is
                // read back from the library, with this take in it, when
                // next wanted.
                Announcer?.ForgetPreparedAudio();
            }

            if (Gone) { return; }
            await PlayTestAudioAsync(audio, testProfile.Volume);

            // Says plainly when a test and a race would differ. Tests use
            // these on-screen settings; races use the saved profile. There
            // is no Save button - every change here saves the moment it is
            // made - so the two only part company when that save failed
            // (the strip at the bottom of the page then says so), or when
            // the model was changed again while this test was playing.
            // Either way the fix is the model box, not a button.
            // The model only matters to ElevenLabs.
            var savedModel = _store.Current.ModelId;
            var unsaved = !windows && !string.Equals(savedModel, testProfile.ModelId, StringComparison.OrdinalIgnoreCase);

            // Kept short on the label, which wraps into the section below if
            // it runs long; the full explanation goes to the log.
            if (unsaved)
            {
                Status($"Test used {testProfile.ModelId}, but the saved profile races read has {savedModel}. "
                       + "This page saves every change on its own - if the line at the bottom of the page says a save failed, "
                       + "fix that and choose the model again; if it shows a recent save, the model was changed after the test started.");
            }

            _lblTestStatus.Text = unsaved
                ? $"Tested with {testProfile.ModelId}; races use {savedModel} (last saved) - see the note at the bottom of the page."
                : $"{type}: \"{(spoken.Length > 70 ? spoken[..67] + "..." : spoken)}\"";

            _lblTestStatus.ForeColor = unsaved ? AppTheme.Accent : AppTheme.Success;
        }
        catch (Exception ex)
        {
            _lblTestStatus.Text = $"Test failed: {SummariseApiError(ex.Message)}";
            _lblTestStatus.ForeColor = AppTheme.Danger;
        }
        finally
        {
            // Guarded: an early return from inside the try still runs this,
            // and the page may be gone by then.
            if (!Gone)
            {
                PreservingScroll(() => button.Enabled = true);
            }
        }
    }

    /// <summary>
    /// Runs something that will move focus, and puts the page back where it
    /// was afterwards.
    ///
    /// An AutoScroll panel scrolls whichever control takes focus into view.
    /// That is right when the user tabs around and wrong when focus moves
    /// because a button disabled itself - the user didn't ask to go
    /// anywhere, and on this page they end up at the top having lost their
    /// place in a long list.
    /// </summary>
    private void PreservingScroll(Action action)
    {
        var host = ScrollHost();

        // AutoScrollPosition reads back NEGATIVE and is assigned POSITIVE.
        // Assigning what it returned would scroll to the other end.
        var before = host?.AutoScrollPosition ?? Point.Empty;

        action();

        if (host is not null)
        {
            host.AutoScrollPosition = new Point(Math.Abs(before.X), Math.Abs(before.Y));
        }
    }

    /// <summary>
    /// The scrolling panel the buttons this is used for sit in: the
    /// announcement rows' band, where every per-announcement Test button is.
    ///
    /// It used to be found by walking down from this page for the first
    /// panel that scrolled, which was the right one while the rows were a
    /// group at the bottom of this page. They have had a tab of their own
    /// since, and the walk found this page's own scrolling instead - so a
    /// Test pressed on the Announcements tab kept the wrong list where it
    /// was.
    /// </summary>
    private ScrollableControl? ScrollHost() => _annRows;

    /// <summary>
    /// Saves the whole profile whenever anything on the page changes.
    ///
    /// Walks the control tree once and subscribes to every input, so a
    /// control added later is covered without anyone remembering to wire
    /// it up - the class of bug this replaces was precisely a setting that
    /// looked applied but had never been saved.
    /// </summary>
    /// <summary>
    /// Every control that has already been wired for auto-save.
    ///
    /// The announcements group is rebuilt whenever the default lines are
    /// restored or the context is switched, and WireAutoSave is run over
    /// the new one - but a dozen of its controls are FIELDS that survive
    /// the rebuild, so each pass hung ANOTHER handler on the same control.
    /// After three rebuilds, one tick of a checkbox wrote the settings file
    /// four times.
    ///
    /// Reference equality, which is what a Control gives by default and
    /// exactly what is wanted: the question is whether this instance has
    /// been wired, not whether something equal to it has.
    /// </summary>
    private readonly HashSet<Control> _autoSaveWired = new();

    private void WireAutoSave(Control parent)
    {
        // Controls from a rebuilt group are gone but would otherwise be
        // held here for the rest of the session.
        _autoSaveWired.RemoveWhere(control => control.IsDisposed);

        foreach (Control child in parent.Controls)
        {
            if (_autoSaveWired.Add(child))
            {
                switch (child)
                {
                    case TextBox box:
                        box.TextChanged += (_, _) => AutoSave();
                        break;
                    case ComboBox combo:
                        combo.SelectedIndexChanged += (_, _) => AutoSave();
                        combo.TextChanged += (_, _) => AutoSave();
                        break;
                    case NumericUpDown num:
                        num.ValueChanged += (_, _) => AutoSave();
                        break;
                    case CheckBox chk:
                        chk.CheckedChanged += (_, _) => AutoSave();
                        break;

                    // Not a kind of CheckBox, so the case above never saw one
                    // and the choice of voice would never have been saved.
                    // Only the button being switched ON saves: moving between
                    // two radio buttons raises CheckedChanged on both.
                    case RadioButton radio:
                        radio.CheckedChanged += (_, _) =>
                        {
                            if (radio.Checked)
                            {
                                AutoSave();
                            }
                        };
                        break;
                }
            }

            if (child.HasChildren)
            {
                WireAutoSave(child);
            }
        }
    }

    /// <summary>True while controls are being populated, so loading doesn't count as editing.</summary>
    private bool _loadingSettings;

    /// <summary>
    /// The volume box's value as last loaded from, or saved to, the store.
    /// If the box still shows this, nobody has changed it on THIS page.
    /// </summary>
    private decimal _volumeShown;

    /// <summary>
    /// Brings the volume box up to date with the saved level.
    ///
    /// The Mixer page writes the same stored volume, but this page read it
    /// once when it was built - so the next change to anything here saved
    /// the old number back and silently undid the Mixer. Only taken when
    /// the box hasn't been changed here since it was last in step, so an
    /// edit made on this page is never overwritten.
    /// </summary>
    private void SyncVolumeFromStore()
    {
        var stored = (decimal)Math.Clamp(_store.Current.Volume * 100.0, 0, 100);

        if (_numVolumePercent.Value != _volumeShown || stored == _volumeShown)
        {
            return;
        }

        var wasLoading = _loadingSettings;
        _loadingSettings = true;

        try
        {
            _numVolumePercent.Value = stored;
        }
        finally
        {
            _loadingSettings = wasLoading;
        }

        _volumeShown = stored;
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        // Coming back to this page after using the Mixer should show the
        // level the Mixer set, not the one from when the page was built.
        // And the saved-lines count, which a race moves on while it's hidden.
        if (Visible)
        {
            SyncVolumeFromStore();
            RequestLibraryRefresh();
        }
    }

    private void AutoSave()
    {
        if (_loadingSettings)
        {
            return;
        }

        try
        {
            // Before building the profile, so a save triggered by some other
            // box carries the CURRENT stored volume rather than a stale one.
            SyncVolumeFromStore();

            var voiceBefore = _store.Current.HeatCallerVoice;

            var profile = BuildProfileFromControls();

            // Save never throws. SafeFile reports a locked or unwritable file
            // (antivirus, OneDrive, a full disk) by returning false, and it
            // refuses every write after a restore until the app restarts.
            // The profile is current for this run either way, so the
            // announcer keeps using what was just typed. But the file still
            // holds the old settings and they come back on the next launch,
            // so "Saved automatically" would be untrue and the club would
            // only find out on race night. The strip below says so instead,
            // and the next change on the page tries the write again.
            var saved = _store.Save(profile);
            _volumeShown = _numVolumePercent.Value;

            // A different HeatCaller voice: whatever the relay refused the
            // old one for - "that voice isn't available any more", most
            // likely - is no reason to keep the new one resting. Tried again
            // from the next line, as a new ElevenLabs key is.
            if (!string.Equals(voiceBefore, profile.HeatCallerVoice, StringComparison.OrdinalIgnoreCase))
            {
                Announcer?.ResetHeatCallerVoiceState("a different HeatCaller voice was chosen");
            }

            if (!saved)
            {
                var reason = SafeFile.IsFrozen
                    ? "saving is switched off until the app restarts after a restore"
                    : "the settings file couldn't be written (the console says why)";
                _lblSaveStatus.Text = $"Could not save at {DateTime.Now:HH:mm:ss}: {reason}. Changes last only until the app closes.";
                _lblSaveStatus.ForeColor = AppTheme.Danger;
                Status($"Settings NOT saved - {reason}.");
                return;
            }

            _lblSaveStatus.Text = $"Saved automatically at {DateTime.Now:HH:mm:ss}.";
            _lblSaveStatus.ForeColor = AppTheme.TextMuted;
            Status($"Settings saved - {Describe(profile)}");
        }
        catch (Exception ex)
        {
            // This runs from a TextChanged handler, so an exception here
            // would surface as an app crash while someone typed their API
            // key - and the reason the key never saved would be invisible.
            _lblSaveStatus.Text = $"Could not save: {ex.Message}";
            _lblSaveStatus.ForeColor = AppTheme.Danger;
            Status($"Settings NOT saved - {ex.GetType().Name}: {ex.Message}");
        }
    }
}
