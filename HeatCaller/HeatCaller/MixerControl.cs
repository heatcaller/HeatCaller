namespace HeatCaller;

/// <summary>
/// One place to balance everything the app plays.
///
/// The individual levels live on their own tabs next to the settings they
/// belong with, which is right for configuring them but wrong for MIXING -
/// getting a blend right means hearing the sources against each other and
/// adjusting in seconds, not clicking between four tabs. This page is a
/// view onto those same stored values rather than a separate set: a change
/// here is the same change made there.
/// </summary>
public sealed class MixerControl : UserControl
{
    private readonly VoiceSettingsStore _voiceStore;
    private readonly SoundEffectsStore _soundStore;
    private readonly AmbienceStore _ambienceStore;
    private readonly AmbienceService _ambience;
    private readonly MusicStore _musicStore;
    private readonly MusicPlayer _music;
    private readonly AnnouncementService _announcer;
    private readonly AppSettings _settings;

    /// <summary>For Reset to Defaults. Optional so an older host still builds; the reset then isn't undoable.</summary>
    private readonly UndoService? _undo;

    private readonly List<Channel> _channels = new();
    private readonly Label _lblStatus = new();
    private readonly Label _lblVoiceWarning = new();
    private readonly Button _btnAuditionAll = new();
    private readonly NumericUpDown _numAnnounceEvery = new();
    private readonly Button _btnSpeakNow = new();
    private readonly CheckBox _chkStartRaces = new ThemedCheckBox();
    private readonly CheckBox _chkStartPractice = new ThemedCheckBox();
    private readonly ComboBox _cmbStartAt = new();
    private readonly CheckBox _chkStartMusic = new ThemedCheckBox();
    private readonly CheckBox _chkStartAmbience = new ThemedCheckBox();
    private readonly CheckBox _chkUnmute = new ThemedCheckBox();
    private readonly CheckBox _chkStopAmbience = new ThemedCheckBox();
    private readonly CheckBox _chkStopMusic = new ThemedCheckBox();
    private readonly ComboBox _cmbMusicEnd = new();
    private readonly NumericUpDown _numMusicFade = new();
    private readonly CheckBox _chkDuckAmbience = new ThemedCheckBox();
    private readonly CheckBox _chkDuckMusic = new ThemedCheckBox();

    /// <summary>Repeats the sample announcement while auditioning, so there's always something to balance against.</summary>
    private readonly System.Windows.Forms.Timer _auditionVoice = new() { Interval = 9000 };

    private bool _auditioning;

    /// <summary>What was playing before the audition, so stopping restores rather than silences.</summary>
    private bool _ambienceWasRunning;
    private AmbienceService.Intensity _ambienceMoodBefore;
    private bool _musicWasPlaying;
    private readonly System.Windows.Forms.Timer _meterTimer = new() { Interval = 120 };

    /// <summary>
    /// Writes the fader levels to disk once the fader stops moving. A drag
    /// or a held arrow key is dozens of steps, and every one used to write
    /// the whole store file on the UI thread. The level itself is set on
    /// the store and the player straight away, so the change is heard at
    /// once and anything else that saves the store carries it; only the
    /// file write waits. See SoundEffectsControl for the same pattern.
    /// </summary>
    private readonly System.Windows.Forms.Timer _saveDebounce = new() { Interval = 400 };

    // Which stores have a fader change not yet written - see _saveDebounce.
    private bool _voiceLevelUnsaved;
    private bool _soundLevelUnsaved;
    private bool _ambienceLevelUnsaved;
    private bool _musicLevelUnsaved;

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The strips' Test buttons, which the least a strip can do with is measured from - see StripLeastWidth.</summary>
    private readonly List<Button> _testButtons = new();

    /// <summary>The drop-downs' widths as last measured - see Pieces.PickerWidth. Their choices never change, so only a new text size measures them again.</summary>
    private (Font Font, int Count, int Width)? _startAtWidth;
    private (Font Font, int Count, int Width)? _musicEndWidth;

    /// <summary>A fader's width and height at the normal text size. FaderControl draws it; it is sized here, at the text size in use.</summary>
    private const int FaderWidth = 48;
    private const int FaderHeight = 200;

    /// <summary>
    /// The narrowest the column beside a fader gets at the normal text size -
    /// the level, and what the source is doing, which wraps. Test sets it
    /// wider on the strips that have one. Any wider and the four strips
    /// would go two by two at 200%.
    /// </summary>
    private const int StateLeastWidth = 120;

    /// <summary>The drop-downs' widths at the normal text size - never narrower, and as wide as their longest choice.</summary>
    private const int StartAtPickerWidth = 150;
    private const int MusicEndPickerWidth = 170;

    /// <summary>
    /// The four strips' names and descriptions, in order. Every strip keeps
    /// room for the longest of them, so the four faders start level - see
    /// AddChannel.
    /// </summary>
    private static readonly string[] StripNames = { "Announcements", "Sound effects", "Ambience", "Music" };

    private static readonly string[] StripDescriptions =
    {
        "Spoken lines from the announcer.",
        "Per-lap chimes and race cues.",
        "Crowd and track atmosphere.",
        "Between-race soundtrack.",
    };

    /// <summary>Set while sliders are being written programmatically, so that doesn't count as a user edit.</summary>
    private bool _loading;

    /// <summary>
    /// The sidebar's own mute, supplied by the main window. Muting or
    /// unmuting from this page goes through it when set, so the choice is
    /// saved and the sidebar's record of what its mute stopped stays true.
    ///
    /// Setting the announcer's Muted flag directly, as this page used to,
    /// left the saved setting behind: unmute here, restart the app, and it
    /// came back muted - or mute here and the sidebar's unmute had no idea
    /// the crowd and music had been stopped. Falls back to the flag alone
    /// when nothing is wired up.
    /// </summary>
    public Action<bool>? SetAllSoundMutedRequested { get; set; }

    /// <summary>Mutes or unmutes through the sidebar's path when there is one - see SetAllSoundMutedRequested.</summary>
    private void SetAnnouncerMuted(bool muted)
    {
        if (SetAllSoundMutedRequested is { } setAllSoundMuted)
        {
            setAllSoundMuted(muted);
        }
        else
        {
            _announcer.Muted = muted;
        }
    }

    public MixerControl(
        VoiceSettingsStore voiceStore,
        SoundEffectsStore soundStore,
        AmbienceStore ambienceStore,
        AmbienceService ambience,
        MusicStore musicStore,
        MusicPlayer music,
        AnnouncementService announcer,
        AppSettings settings,
        UndoService? undo = null)
    {
        _settings = settings;
        _undo = undo;
        _voiceStore = voiceStore;
        _soundStore = soundStore;
        _ambienceStore = ambienceStore;
        _ambience = ambience;
        _musicStore = musicStore;
        _music = music;
        _announcer = announcer;

        Dock = DockStyle.Fill;
        ForeColor = AppTheme.TextDark;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize. No design size and no MinimumSize: the 900x500
        // floor was honoured over the size the page was given. The page
        // scrolls when it is taller than the window, and down only - nothing
        // on it is wider than the page (PageStack).
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();

        _meterTimer.Tick += (_, _) => RefreshMeters();
        _meterTimer.Start();

        _saveDebounce.Tick += (_, _) => SaveLevelsNow();

        // Before the handle goes as well as on Dispose: a window closing
        // straight after a fader move must not lose that last level.
        HandleDestroyed += (_, _) => SaveLevelsNow();

        // Repeats the sample line so there's always something to balance
        // against, rather than one announcement then silence.
        _auditionVoice.Tick += (_, _) => SpeakAuditionLine();
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

    /// <summary>One mixer strip: a fader plus the live state of that source.</summary>
    private sealed class Channel
    {
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required FaderControl Slider { get; init; }
        public required Label ValueLabel { get; init; }
        public required Label StateLabel { get; init; }
        public required Func<float> Read { get; init; }
        public required Action<float> Write { get; init; }

        /// <summary>Live status text - what this source is doing right now.</summary>
        public required Func<string> State { get; init; }

        /// <summary>Optional audition, for sources that can demo themselves.</summary>
        public Action? Test { get; init; }
    }

    /// <summary>
    /// The intro and the line kept for the voice banner; then the four
    /// strips side by side - four across, two by two, or one under another,
    /// as the width allows (StripBank). Under them the settings, in cards:
    /// whether the beds duck under announcements, what comes up when a
    /// session starts, and what stops when it ends - and last a card with
    /// the audition's interval, the buttons and the status line. The four
    /// cards' captions share one column (FieldGrid.LineUp), so every choice
    /// starts at the same x down the page. On the layout pieces
    /// (LayoutPieces.cs), so nothing is placed by hand; the page scrolls
    /// when it is taller than the window (PageStack).
    ///
    /// It used to be placed in pixels on a 980x664 page with a 900x500
    /// floor: the strips 224px wide and their faders 48x200 at every text
    /// size, the intro and the banner capped at the floor's width, the
    /// faders' shared top worked out by hand from the descriptions' wrapped
    /// heights, and every row under the strips at an x worked out from the
    /// widest caption. The two groups of settings were bold headings over
    /// the rows ("WHEN A SESSION STARTS", "WHEN IT ENDS"); they are the
    /// cards' captions now.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Balance every sound the app makes, in one place. These are the same levels as on the other tabs - "
                 + "changing one here changes it there. Adjustments apply immediately, so you can set the blend by ear.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        // Under the intro, on a line of its own that the strips start below,
        // kept whether the banner shows or not - so the faders never move
        // when it comes and goes. It used to sit at a fixed y=38, which is
        // the middle of the intro's second line - so NO VOICE was printed
        // across the intro whenever it showed. It shows more often now (the
        // Windows voice standing in has a notice here too), so it gets the
        // room. One line, cut short rather than wrapped, so it can never
        // reach down into the strips - see RefreshMeters for what it says.
        _lblVoiceWarning.AutoSize = false;
        _lblVoiceWarning.AutoEllipsis = true;
        _lblVoiceWarning.Dock = DockStyle.Fill;
        _lblVoiceWarning.Visible = false;
        _textSize.Wear(_lblVoiceWarning, () => AppTheme.BoldFont);

        var warningLine = new Panel { Tag = AppTheme.KeepBackColor };
        warningLine.Controls.Add(_lblVoiceWarning);

        // One line of the bold face and a little room, at the text size in
        // use.
        _textSize.Keep(() =>
        {
            var height = AppTheme.BoldFont.Height + 4;

            if (warningLine.Height != height)
            {
                warningLine.Height = height;
            }
        });

        // --- The strips ---

        var strips = new StripBank(StripLeastWidth);

        strips.AddStrip(AddChannel(
            StripNames[0],
            StripDescriptions[0],
            () => (float)_voiceStore.Current.Volume,
            v =>
            {
                _voiceStore.Current.Volume = v;
                _voiceLevelUnsaved = true;
                QueueLevelSave();
            },
            // Which voice is speaking, since there are two now - and whether
            // the Windows one is only standing in for ElevenLabs.
            () => _announcer.DescribeVoiceBlocker() is { } blocked
                ? $"Can't speak - {blocked}"
                : _announcer.DescribeStandIn() is not null
                    ? "Ready - Windows voice standing in"
                    : _voiceStore.Current.EffectiveEngine switch
                    {
                        VoiceEngine.Windows => "Ready - Windows voice",
                        VoiceEngine.HeatCaller => "Ready - HeatCaller voice",
                        VoiceEngine.Kokoro => "Ready - Kokoro voice",
                        _ => "Ready",
                    },
            () =>
            {
                // Checked BEFORE queueing. Speech happens asynchronously on
                // the voice queue, so a failure surfaces long after the
                // button returns - pressing Test and getting silence with
                // no explanation is exactly what happened here.
                if (_announcer.DescribeVoiceBlocker() is { } blocker)
                {
                    _lblStatus.Text = $"No voice - {blocker}.";
                    _lblStatus.ForeColor = AppTheme.Danger;
                    return;
                }

                _announcer.SpeakReminder("Announcement level check.");
                _lblStatus.Text = "Speaking a test line...";
                _lblStatus.ForeColor = AppTheme.TextMuted;
            }));

        strips.AddStrip(AddChannel(
            StripNames[1],
            StripDescriptions[1],
            () => (float)_soundStore.Current.Volume,
            v =>
            {
                _soundStore.Current.Volume = v;
                _soundLevelUnsaved = true;
                QueueLevelSave();
            },
            () => _announcer.Muted ? "Muted" : "Ready",
            () => _ = SoundPacks.PlayAsync(_soundStore.Current.Effects.GetValueOrDefault(nameof(SoundEffectKind.GoodLap)), SoundEffectKind.GoodLap, (float)_soundStore.Current.Volume)));

        strips.AddStrip(AddChannel(
            StripNames[2],
            StripDescriptions[2],
            () => _ambienceStore.Current.MasterVolume,
            v =>
            {
                _ambienceStore.Current.MasterVolume = v;
                _ambienceLevelUnsaved = true;
                QueueLevelSave();
                _ambience.Settings = _ambienceStore.Current;

                // Re-apply so the change is audible straight away rather
                // than at the next intensity change - at the crowd's CURRENT
                // mood. This used to ask for Racing, which did nothing
                // mid-race and turned the crowd up to race level between
                // races.
                _ambience.RefreshLevels();
            },
            // What is playing first, the master switch second. A session's
            // auto-start and this page's own Play Everything both bring the
            // crowd up past the switch, which is off on a fresh install, so
            // testing the switch first read "Off" for a crowd that could be
            // heard - on the page meant for working out what is playing.
            () => _ambience.IsRunning ? $"Playing - {_ambience.EffectiveIntensity}"
                : !_ambienceStore.Current.Enabled ? "Off"
                : "Idle"));

        strips.AddStrip(AddChannel(
            StripNames[3],
            StripDescriptions[3],
            () => _musicStore.Current.Volume,
            v =>
            {
                _musicStore.Current.Volume = v;
                _musicLevelUnsaved = true;
                QueueLevelSave();
                _music.Settings = _musicStore.Current;
                _music.RefreshVolume();
            },
            // Live state first, as for the crowd above: auto-start and Play
            // Everything play music whatever the master switch says.
            () => _music.NowPlaying is { } t ? $"Playing - {t.DisplayName}"
                : !_musicStore.Current.Enabled ? "Off"
                : "Stopped"));

        // --- One caption column for everything below the strips ---
        //
        // A caption on the left, its choices to the right of it, one
        // setting per row. These rows were each laid out on their own, left
        // to right, so the choices started wherever their caption happened
        // to end and nothing lined up from one row to the next - and each
        // tick box was spaced by its default 104-pixel width rather than by
        // its text, which spread "Races" and "Practice" a third of the page
        // apart. Every caption shares one column now, across the four cards
        // (LineUp, below), and every choice starts at the same x.
        //
        // Hints go on their own line under the row they explain rather than
        // beside it. A line of prose next to a row of controls only ever
        // fits at one font size: "Music after the recap" once had its hint
        // at the same y as its drop-down and spinner, drawn straight
        // through both.

        // --- Duck toggles ---
        //
        // Surfaced here as well as on their own tabs: this is the page
        // you're on when the ducking is bothering you, and hunting two
        // other tabs to switch it off is friction at exactly the wrong
        // moment. Same stored setting, so the two stay in step.
        _chkDuckAmbience.Text = "Ambience";
        _chkDuckAmbience.AutoSize = true;
        _chkDuckAmbience.Checked = _ambienceStore.Current.DuckUnderAnnouncements;
        _chkDuckAmbience.CheckedChanged += (_, _) =>
        {
            _ambienceStore.Current.DuckUnderAnnouncements = _chkDuckAmbience.Checked;
            _ambienceStore.Save();
            _ambience.Settings = _ambienceStore.Current;

            // Release any duck currently applied, or turning it off
            // mid-announcement would leave the bed stuck quiet.
            if (!_chkDuckAmbience.Checked)
            {
                _ambience.SetDucked(false);
            }
        };

        _chkDuckMusic.Text = "Music";
        _chkDuckMusic.AutoSize = true;
        _chkDuckMusic.Checked = _musicStore.Current.DuckUnderAnnouncements;
        _chkDuckMusic.CheckedChanged += (_, _) =>
        {
            _musicStore.Current.DuckUnderAnnouncements = _chkDuckMusic.Checked;
            _musicStore.Save();
            _music.Settings = _musicStore.Current;

            if (!_chkDuckMusic.Checked)
            {
                _music.SetDucked(false);
            }
        };

        var duckGrid = new FieldGrid();

        duckGrid.AddRow("Duck under announcements:", Choices(_chkDuckAmbience, _chkDuckMusic), stretch: true);
        duckGrid.AddHint("off means the bed stays at full level while the announcer talks - set how far it drops on each source's own tab");

        var duckCard = new Section();

        duckCard.Add(duckGrid, gapBelow: 0);

        // --- When a session starts ---
        //
        // Auto-start lives here rather than on any one source's tab: it
        // governs music, ambience AND the announcer together, so putting it
        // under Music (say) would hide two thirds of what it does.
        _chkStartRaces.Text = "Races";
        _chkStartRaces.AutoSize = true;
        _chkStartRaces.Checked = _settings.AudioStartWithRaces;
        _chkStartRaces.CheckedChanged += (_, _) =>
        {
            _settings.AudioStartWithRaces = _chkStartRaces.Checked;
            _settings.Save();
            UpdateAutoStartState();
        };

        _chkStartPractice.Text = "Practice";
        _chkStartPractice.AutoSize = true;
        _chkStartPractice.Checked = _settings.AudioStartWithPractice;
        _chkStartPractice.CheckedChanged += (_, _) =>
        {
            _settings.AudioStartWithPractice = _chkStartPractice.Checked;
            _settings.Save();
            UpdateAutoStartState();
        };

        _cmbStartAt.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbStartAt.Items.AddRange(new object[] { "Staging", "Green flag" });
        _cmbStartAt.SelectedIndex = _settings.AudioStartAtStaging ? 0 : 1;
        _cmbStartAt.SelectedIndexChanged += (_, _) =>
        {
            _settings.AudioStartAtStaging = _cmbStartAt.SelectedIndex == 0;
            _settings.Save();
        };

        _chkStartMusic.Text = "Music";
        _chkStartMusic.AutoSize = true;
        _chkStartMusic.Checked = _settings.AudioStartMusic;
        _chkStartMusic.CheckedChanged += (_, _) => { _settings.AudioStartMusic = _chkStartMusic.Checked; _settings.Save(); };

        _chkStartAmbience.Text = "Ambience";
        _chkStartAmbience.AutoSize = true;
        _chkStartAmbience.Checked = _settings.AudioStartAmbience;
        _chkStartAmbience.CheckedChanged += (_, _) => { _settings.AudioStartAmbience = _chkStartAmbience.Checked; _settings.Save(); };

        _chkUnmute.Text = "Unmute announcer";
        _chkUnmute.AutoSize = true;
        _chkUnmute.Checked = _settings.AudioUnmuteAnnouncer;
        _chkUnmute.CheckedChanged += (_, _) => { _settings.AudioUnmuteAnnouncer = _chkUnmute.Checked; _settings.Save(); };

        var startGrid = new FieldGrid();

        startGrid.AddRow("Start for:", Choices(_chkStartRaces, _chkStartPractice), stretch: true);
        startGrid.AddRow("Starting at:", _cmbStartAt);
        startGrid.AddRow("Bring up:", Choices(_chkStartMusic, _chkStartAmbience, _chkUnmute), stretch: true);

        var startCard = new Section("When a session starts");

        startCard.Add(startGrid, gapBelow: 0);

        // --- When it ends ---

        // Music straight away at the checkered flag, for a club that wants
        // the track quiet the moment racing stops. Takes the place of the
        // choice after the recap, which is greyed while this is ticked.
        _chkStopMusic.Text = "Music";
        _chkStopMusic.AutoSize = true;
        _chkStopMusic.Checked = _settings.AudioStopMusicAfterSession;
        _chkStopMusic.CheckedChanged += (_, _) =>
        {
            _settings.AudioStopMusicAfterSession = _chkStopMusic.Checked;
            _settings.Save();
            UpdateAutoStartState();
        };

        _chkStopAmbience.Text = "Ambience";
        _chkStopAmbience.AutoSize = true;
        _chkStopAmbience.Checked = _settings.AudioStopAmbienceAfterSession;
        _chkStopAmbience.CheckedChanged += (_, _) =>
        {
            _settings.AudioStopAmbienceAfterSession = _chkStopAmbience.Checked;
            _settings.Save();
        };

        // Music gets a three-way choice rather than a tick, and it happens
        // after the RECAP rather than at the flag - fading across the
        // summary is the opposite of what a broadcast does.
        _cmbMusicEnd.DropDownStyle = ComboBoxStyle.DropDownList;
        // Rows carry their value rather than standing for it by position.
        // The chosen behaviour is saved in settings; three literal strings
        // welded by order to an enum in another file meant that adding a
        // fourth choice anywhere but the end changed what every saved
        // setting meant.
        _cmbMusicEnd.Items.Add(new MusicEndItem(MusicEndBehaviour.KeepPlaying, "Keep playing"));
        _cmbMusicEnd.Items.Add(new MusicEndItem(MusicEndBehaviour.FadeOut, "Fade out"));
        _cmbMusicEnd.Items.Add(new MusicEndItem(MusicEndBehaviour.StopImmediately, "Stop"));
        SelectMusicEnd(_settings.MusicAfterSummary);
        _cmbMusicEnd.SelectedIndexChanged += (_, _) =>
        {
            _settings.MusicAfterSummary = SelectedMusicEnd();
            _settings.Save();
            UpdateAutoStartState();
        };

        var lblFadeOver = new Label { Text = "over" };

        _numMusicFade.DecimalPlaces = 1;
        _numMusicFade.Increment = 0.5m;
        _numMusicFade.Minimum = 0.5m;
        _numMusicFade.Maximum = 30m;
        _numMusicFade.Value = (decimal)Math.Clamp(_settings.MusicFadeOutSeconds, 0.5, 30);
        _numMusicFade.ValueChanged += (_, _) =>
        {
            _settings.MusicFadeOutSeconds = (double)_numMusicFade.Value;
            _settings.Save();
        };

        var lblFadeSuffix = new Label { Text = "s", ForeColor = AppTheme.TextMuted };

        var endGrid = new FieldGrid();

        endGrid.AddRow("At the flag, stop:", Choices(_chkStopMusic, _chkStopAmbience), stretch: true);
        endGrid.AddRow("Music after the recap:", FieldGrid.Inline(_cmbMusicEnd, lblFadeOver, _numMusicFade, lblFadeSuffix));
        endGrid.AddHint("Only stops what was started automatically - anything you started by hand keeps going. "
                      + "Ambience waits for the finish celebration to play out first. "
                      + "With Music ticked above, the music stops at the flag and the choice after the recap doesn't apply.");

        var endCard = new Section("When it ends");

        endCard.Add(endGrid, gapBelow: 0);

        // --- Audition cadence ---

        _numAnnounceEvery.Minimum = 5;
        _numAnnounceEvery.Maximum = 120;
        _numAnnounceEvery.Increment = 5;
        _numAnnounceEvery.Value = 20;
        _numAnnounceEvery.ValueChanged += (_, _) =>
        {
            // Applied live so the gap can be widened mid-audition without
            // stopping and starting again.
            _auditionVoice.Interval = Math.Max(1, (int)_numAnnounceEvery.Value) * 1000;
        };

        var auditionGrid = new FieldGrid();

        auditionGrid.AddRow("Announce every:", FieldGrid.Inline(_numAnnounceEvery, new Label { Text = "seconds" }));
        auditionGrid.AddHint("The bed plays clean first, so you can hear the blend before anything ducks it. "
                           + "Widen this to listen longer between announcements.");

        // --- Buttons ---
        //
        // Sized to their captions by the row they are in (ButtonBar) rather
        // than given a height of 28. At the current font a 28-pixel button
        // clips its own caption, which is what had happened to "Play
        // Everything".

        // Balancing by ear needs everything audible at once - checking one
        // source at a time tells you nothing about how they sit together,
        // which is the whole question a mixer answers.
        _btnAuditionAll.Text = "Play Everything";
        _btnAuditionAll.Click += (_, _) => ToggleAudition();

        // On-demand line, so the ducked and un-ducked states can be compared
        // back to back instead of waiting for the timer to come round.
        _btnSpeakNow.Text = "Announce Now";
        _btnSpeakNow.Click += (_, _) =>
        {
            if (_announcer.Muted)
            {
                SetAnnouncerMuted(false);
            }

            SpeakAuditionLine();
        };

        var btnMuteAll = new Button { Text = "Mute Everything" };
        btnMuteAll.Click += (_, _) =>
        {
            // Ends an audition properly rather than just silencing it -
            // otherwise the button still reads "Stop Everything" and the
            // repeat timer keeps firing announcements into a mute.
            if (_auditioning)
            {
                _auditioning = false;
                _auditionVoice.Stop();
                _btnAuditionAll.Text = "Play Everything";
            }

            // Through the sidebar's mute whenever it is wired up - already
            // muted included. A repeat mute there ADDS to the record of what
            // the first one stopped (a crowd or music started since from
            // their own tabs), so nothing is forgotten; stopping them here
            // directly instead left the sidebar's unmute resuming music this
            // had stopped outright and not bringing the crowd back.
            if (SetAllSoundMutedRequested is not null)
            {
                // The sidebar's mute: it stops the crowd and pauses the music
                // itself, and remembers that it did, so the sidebar's unmute
                // brings back exactly what this silenced.
                SetAnnouncerMuted(true);
                _lblStatus.Text = "Everything muted. Unmute from the sidebar to bring it all back.";
            }
            else
            {
                _announcer.Muted = true;
                _ambience.Stop();
                _music.Stop();
                _lblStatus.Text = "Everything muted. Unmute the announcer from the sidebar; ambience and music restart from their own tabs.";
            }

            _lblStatus.ForeColor = AppTheme.Accent;
            RefreshMeters();
        };

        var btnReset = new Button { Text = "Reset to Defaults" };
        btnReset.Click += (_, _) => ResetLevels();

        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = AppTheme.TextMuted;
        _lblStatus.Text = "Drag a fader to set its level - changes save as you go.";

        var auditionCard = new Section();

        auditionCard.Add(auditionGrid, gapBelow: UiLayout.SectionGap);
        auditionCard.Add(new ButtonBar().With(_btnAuditionAll, _btnSpeakNow, btnMuteAll, btnReset));
        auditionCard.Add(_lblStatus, gapBelow: 0);

        // --- The page ---

        var page = new PageStack();

        page.Add(lblIntro, gapBelow: UiLayout.HintGap);
        page.Add(warningLine, gapBelow: UiLayout.RowGap);
        page.Add(strips);
        page.Add(duckCard);
        page.Add(startCard);
        page.Add(endCard);
        page.Add(auditionCard, gapBelow: 0);

        Controls.Add(page);

        // Measured in the face the captions and fields inherit from this
        // page, so only once they are on it - before that they wear the
        // WinForms default. Then again whenever the text size changes.
        //
        // The four cards' captions in one column.
        _textSize.Keep(() => FieldGrid.LineUp(new[] { duckGrid, startGrid, endGrid, auditionGrid }));

        // The drop-downs and the number boxes at the text size in use - see
        // SizeFields.
        _textSize.Keep(SizeFields);

        RefreshMeters();
        UpdateAutoStartState();
    }

    /// <summary>
    /// Tick boxes along a line, a section gap apart - each spaced by its own
    /// text and box - and onto a second line when the column is too narrow
    /// for them all (FieldRow).
    /// </summary>
    private static FieldRow Choices(params CheckBox[] boxes)
    {
        var row = new FieldRow();

        foreach (var box in boxes)
        {
            row.Add(null, box);
        }

        return row;
    }

    /// <summary>
    /// The fields' sizes at the text size in use: the drop-downs the width
    /// they were at the normal text size, at this size, or as wide as their
    /// longest choice if that is more; the number boxes as wide as their
    /// largest value and their arrows need. Run again when the text size
    /// changes (TextSize.Keep).
    /// </summary>
    private void SizeFields()
    {
        Fit(_cmbStartAt, Math.Max(UiLayout.AtTextSize(StartAtPickerWidth), Pieces.PickerWidth(_cmbStartAt, ref _startAtWidth)));
        Fit(_cmbMusicEnd, Math.Max(UiLayout.AtTextSize(MusicEndPickerWidth), Pieces.PickerWidth(_cmbMusicEnd, ref _musicEndWidth)));
        Fit(_numMusicFade, NumberWidth(_numMusicFade));
        Fit(_numAnnounceEvery, NumberWidth(_numAnnounceEvery));

        // Only when it changed: a new width lays the row out again.
        static void Fit(Control field, int width)
        {
            if (field.Width != width)
            {
                field.Width = width;
            }
        }
    }

    /// <summary>
    /// A stored level (0 to 1) as the whole percent its fader shows.
    /// </summary>
    /// <remarks>
    /// This rounds; it must not simply cut off the fraction. A fader stores
    /// percent / 100f, and in single precision some of those do not come
    /// back whole when multiplied by 100 again: 0.53f * 100f is 52.999996
    /// and 0.59f * 100f is 58.999996. Cut off, they read back as 52 and 58,
    /// and the meter refresh - which runs every 120 ms and puts the fader
    /// back to whatever the store reads as - would snap a fader set to 53%
    /// or 59% one step down, so Up or the wheel could never get past 52 or
    /// 58, and a fader dragged there would show one percent less than the
    /// level actually playing. Rounding gives back the exact percent that
    /// was written, for every value from 0 to 100.
    /// </remarks>
    internal static int LevelToPercent(float level)
    {
        // A NaN would clamp to NaN and convert to nothing sensible; treat a
        // level that is not a number as silence rather than guess.
        if (float.IsNaN(level))
        {
            return 0;
        }

        return (int)MathF.Round(Math.Clamp(level * 100f, 0f, 100f), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// A number box as wide as its largest value - every digit of its
    /// Maximum, and its decimals - and its arrows need, in the face it is
    /// drawn in; never narrower than a number box at the normal text size,
    /// at this size.
    /// </summary>
    private static int NumberWidth(NumericUpDown box)
    {
        var whole = new string('0', ((long)Math.Truncate(box.Maximum)).ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        var widest = box.DecimalPlaces > 0 ? whole + "." + new string('0', box.DecimalPlaces) : whole;
        var needed = TextRenderer.MeasureText(widest, box.Font).Width + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra;

        return Math.Max(UiLayout.AtTextSize(UiLayout.NumericWidth), needed);
    }

    /// <summary>
    /// The least a strip can do with across: the fader at the text size in
    /// use, the gap beside it, and a column there as wide as the longest of
    /// Test, the level at its widest and the least the state line wraps
    /// in - or its name on one line, if that is more - and the card's inset.
    /// </summary>
    private int StripLeastWidth()
    {
        var beside = Math.Max(
            UiLayout.AtTextSize(StateLeastWidth),
            Math.Max(
                _testButtons.Select(button => button.Width).DefaultIfEmpty(0).Max(),
                TextRenderer.MeasureText("100%", AppTheme.BoldFont).Width));

        var name = StripNames.Max(text => TextRenderer.MeasureText(text, AppTheme.BoldFont).Width);

        return Math.Max(name, UiLayout.AtTextSize(FaderWidth) + UiLayout.RowGap + beside) + Section.Inset.Horizontal;
    }

    /// <summary>
    /// Greys the "bring up" choices when neither races nor practice are
    /// selected - there's nothing for them to act on, and leaving them live
    /// suggests otherwise.
    /// </summary>
    private void UpdateAutoStartState()
    {
        var on = _chkStartRaces.Checked || _chkStartPractice.Checked;

        _cmbStartAt.Enabled = on;
        _chkStartMusic.Enabled = on;
        _chkStartAmbience.Enabled = on;
        _chkUnmute.Enabled = on;
        _chkStopAmbience.Enabled = on;
        _chkStopMusic.Enabled = on;

        // Music stopped at the flag has nothing left to do after the recap.
        var afterRecap = on && !_chkStopMusic.Checked;
        _cmbMusicEnd.Enabled = afterRecap;

        // The fade length only means anything when fading.
        _numMusicFade.Enabled = afterRecap && SelectedMusicEnd() == MusicEndBehaviour.FadeOut;
    }

    /// <summary>
    /// Starts or stops an audition of every source at once.
    ///
    /// Deliberately bypasses each source's own enabled setting - someone
    /// balancing levels wants to hear the thing they're balancing, and
    /// having to switch four features on and back off again to set a mix
    /// is exactly the friction this button exists to remove.
    /// </summary>
    private void ToggleAudition()
    {
        if (_auditioning)
        {
            StopAudition();
            return;
        }

        // Remembered so stopping restores the previous state rather than
        // leaving everything silent - the ambience may well have been
        // running before this was pressed.
        _ambienceWasRunning = _ambience.IsRunning;
        _ambienceMoodBefore = _ambience.CurrentIntensity;
        _musicWasPlaying = _music.IsPlaying;

        _auditioning = true;
        _btnAuditionAll.Text = "Stop Everything";

        var started = new List<string>();

        // Ambience, forced past its enabled setting.
        _ambience.Settings = _ambienceStore.Current;
        if (!_ambience.IsRunning)
        {
            _ambience.Start(ignoreEnabledSetting: true);
        }

        if (_ambience.IsRunning)
        {
            // Racing is the level you'll hear for most of an actual race,
            // so it's the honest one to balance against.
            _ambience.SetIntensity(AmbienceService.Intensity.Racing);
            started.Add("ambience");
        }

        // Music.
        _music.Settings = _musicStore.Current;
        if (!_music.IsPlaying)
        {
            _music.Play();
        }

        if (_music.IsPlaying)
        {
            started.Add("music");
        }

        // Announcements need unmuting, or the whole point is lost.
        if (_announcer.Muted)
        {
            SetAnnouncerMuted(false);
        }

        // Deliberately NOT speaking yet. The first thing you need to hear
        // is music and ambience undisturbed - ducking starts the moment an
        // announcement plays, and you can't judge the resting blend once
        // something is pulling it down.
        _auditionVoice.Interval = Math.Max(1, (int)_numAnnounceEvery.Value) * 1000;
        _auditionVoice.Start();
        started.Add($"announcements every {(int)_numAnnounceEvery.Value}s");

        _lblStatus.Text = started.Count > 1
            ? $"Playing {string.Join(", ", started)} together - drag the faders to balance."
            : "Nothing could be started - check each source has content configured on its own tab.";
        _lblStatus.ForeColor = started.Count > 1 ? AppTheme.Success : AppTheme.Danger;

        RefreshMeters();
    }

    private void StopAudition()
    {
        _auditioning = false;
        _auditionVoice.Stop();
        _btnAuditionAll.Text = "Play Everything";

        // Restore, don't just stop: whatever was running before the
        // audition should still be running after it.
        if (!_ambienceWasRunning)
        {
            _ambience.Stop();
        }
        else if (_ambience.CurrentIntensity == AmbienceService.Intensity.Racing)
        {
            // The audition set the crowd to race level to balance against.
            // Put the mood back, or a between-races crowd stays at race
            // level after the audition. Left alone if something has since
            // moved the mood off Racing (a finish, a caution).
            _ambience.SetIntensity(_ambienceMoodBefore);
        }

        if (!_musicWasPlaying)
        {
            _music.Stop();
        }

        _lblStatus.Text = "Audition stopped.";
        _lblStatus.ForeColor = AppTheme.TextMuted;

        RefreshMeters();
    }

    /// <summary>
    /// A line long enough to hear over the bed, and worded so it's obvious
    /// this is a level check rather than a real race call.
    /// </summary>
    private void SpeakAuditionLine()
    {
        _announcer.SpeakReminder(
            "Level check. This is how an announcement sounds over the music and the crowd.");

        // A sound effect alongside it, since that's a fourth level to set
        // and it's the one most easily lost under everything else.
        _ = SoundPacks.PlayAsync(_soundStore.Current.Effects.GetValueOrDefault(nameof(SoundEffectKind.GoodLap)), SoundEffectKind.GoodLap, (float)_soundStore.Current.Volume);
    }

    /// <summary>
    /// One strip, as a card: its name and what it is, then the fader, and
    /// beside the fader its level, what the source is doing and - for a
    /// source that can demo itself - Test, level with the fader's foot.
    ///
    /// Where the fader starts is decided for the whole bank, not by this
    /// strip's own words: the name and the description each keep room for
    /// the longest of the four (SteadyLabel). "Spoken lines from the
    /// announcer." wraps to two lines where the other three fit on one, and
    /// letting each strip place its fader under its own description put the
    /// Announcements fader a line lower than its neighbours, pushed its Test
    /// button down with it, and left that strip taller than the other three
    /// - four strips that should read as one bank, each starting somewhere
    /// different.
    /// </summary>
    private Section AddChannel(
        string name,
        string description,
        Func<float> read,
        Action<float> write,
        Func<string> state,
        Action? test = null)
    {
        // Everything below is measured and stacked rather than placed at
        // remembered pixel offsets.
        //
        // It was written the other way, and the font going up broke it: the
        // description label wraps, so a bigger font made it taller and it
        // grew down over the top of the fader. Worth keeping measured for
        // its own sake - a hand-placed layout is only ever right for the
        // font it was written against.
        //
        // This was ALSO once offered as the reason the faders could not be
        // dragged. It was not. Nor was the label measurement, nor the order
        // of the initializer, nor the axis the thumb was drawn on. The
        // fader was unusable because a skinned native trackbar has two
        // different ideas of where its handle is, and no amount of
        // rearranging what sat around it was ever going to help. See
        // FaderControl. Four wrong answers, all of them plausible, all of
        // them written down here as though settled - so treat a confident
        // explanation in this file with suspicion unless it says how it was
        // tested.
        var lblName = new SteadyLabel
        {
            Text = name,
            ForeColor = AppTheme.Primary,
            Wordings = StripNames,
        };

        _textSize.Wear(lblName, () => AppTheme.BoldFont);

        var lblDescription = new SteadyLabel
        {
            Text = description,
            ForeColor = AppTheme.TextMuted,
            Wordings = StripDescriptions,
        };

        _textSize.Wear(lblDescription, () => AppTheme.SmallFont);

        // A FaderControl, not a TrackBar.
        //
        // The fader you can see and the fader you can grab are now the same
        // object - see FaderControl for why that took four goes. Percent is
        // the real number here, 100 at the top, so nothing on this page
        // flips anything any more.
        var slider = new FaderControl
        {
            Percent = LevelToPercent(read()),
            SmallChange = 1,
            LargeChange = 10,
        };

        // As tall as it was at the normal text size, at the size in use; the
        // column it stands in gives it its width (Beside, below). What it
        // draws inside - the track and the handle - is the size it always
        // was, for now.
        _textSize.Keep(() =>
        {
            var height = UiLayout.AtTextSize(FaderHeight);

            if (slider.Height != height)
            {
                slider.Height = height;
            }
        });

        var valueLabel = new Label
        {
            Text = $"{slider.Percent}%",
            AutoSize = true,
        };

        _textSize.Wear(valueLabel, () => AppTheme.BoldFont);

        var stateLabel = new Label
        {
            Text = string.Empty,
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(stateLabel, () => AppTheme.SmallFont);

        slider.PercentChanged += (_, _) =>
        {
            var percent = slider.Percent;

            valueLabel.Text = $"{percent}%";

            // Guarded so ResetLevels writing the sliders doesn't count as
            // four separate user edits and save four times over.
            if (_loading)
            {
                return;
            }

            write(percent / 100f);

            _lblStatus.Text = $"{name} set to {percent}%.";
            _lblStatus.ForeColor = AppTheme.TextMuted;
        };

        // The fader down the left, its top level with the level beside it,
        // and the state line under the level, wrapping to the room it has.
        var band = new Beside(() => UiLayout.AtTextSize(FaderWidth))
        {
            SideOnLeft = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        band.AddSide(slider, gapBelow: 0);
        band.Add(valueLabel);
        band.Add(stateLabel, gapBelow: 0);

        if (test is not null)
        {
            // Under the state line, not beside the fader at a fixed height:
            // the state line wraps to two or three lines depending on what it
            // is reporting, and anything placed at a fixed height beside the
            // fader is one long message away from being sat on. Level with
            // the bottom of the fader, on every strip: the room between the
            // two is taken up here, so the state line - live text, "Playing -
            // track name" - never moves the button around during a race, and
            // a message longer than the fader is tall makes the strip taller
            // rather than running into it.
            band.AddFill(new Panel { Tag = AppTheme.KeepBackColor }, _ => UiLayout.RowGap, gapBelow: 0);

            var btnTest = new Button { Text = "Test" };
            btnTest.Click += (_, _) =>
            {
                try
                {
                    test();
                }
                catch
                {
                    // An audition failing is not worth interrupting a mix for.
                }
            };

            _testButtons.Add(btnTest);

            band.Add(new ButtonBar().With(btnTest), gapBelow: 0);
        }

        // The fader goes to the front, whatever the measurements said.
        //
        // In WinForms the control at index 0 is the TOP of the z-order, and
        // Controls.Add appends - so the column of labels beside the fader,
        // added first, is in front of it. The two are side by side and should
        // never meet; if a measurement is ever wrong again, the worst case
        // should be a caption that looks untidy, not a control that cannot be
        // touched. A label has nothing to do with the mouse; the fader has
        // nothing but.
        slider.BringToFront();

        // The card sizes itself to what is in it, and the bank gives every
        // strip in a row the height of the tallest (StripBank). Fixing its
        // height instead is how the contents came to be clipped at the
        // bottom: the card was 332 high whatever the font, so the moment the
        // wrapped text above it needed another line, the test button went
        // under the border.
        var card = new Section();

        card.Add(lblName, gapBelow: UiLayout.HintGap);
        card.Add(lblDescription, gapBelow: UiLayout.RowGap);
        card.Add(band, gapBelow: 0);

        _channels.Add(new Channel
        {
            Name = name,
            Description = description,
            Slider = slider,
            ValueLabel = valueLabel,
            StateLabel = stateLabel,
            Read = read,
            Write = write,
            State = state,
            Test = test,
        });

        return card;
    }

    /// <summary>
    /// The four strips side by side, one width each, as a mixing desk has
    /// them - or two by two, or one under another, when the page is too
    /// narrow for four at the least a strip can do with; never three and one
    /// left over. Every strip in a row is as tall as the tallest, so the
    /// bank has one straight bottom edge.
    ///
    /// Placed by hand rather than by a table, for the reason Beside's column
    /// is: how many strips go on a row comes from the width, and a table's
    /// columns are fixed. Sizes itself - put it in a PageStack, which gives
    /// it the width.
    /// </summary>
    private sealed class StripBank : Panel
    {
        /// <summary>Between two strips, across and down.</summary>
        private const int Gap = UiLayout.RowGap;

        private readonly Func<int> _leastStripWidth;

        /// <param name="leastStripWidth">The least a strip can do with across, asked whenever the bank is measured - so it can follow the text size.</param>
        public StripBank(Func<int> leastStripWidth)
        {
            _leastStripWidth = leastStripWidth;

            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            Tag = AppTheme.KeepBackColor;
        }

        /// <summary>
        /// Adds a strip after the last. Sized here, never by the layout: a
        /// card that sized itself would be resized by the panel to its width
        /// on one line (see Beside.Side).
        ///
        /// The bank is placed again whenever a strip lays itself out: a state
        /// line wrapping onto a line more than the fader is tall is something
        /// only the strip sees. Told as the strip's layout starts, before its
        /// rows are placed - for the reason Beside.AddSideCard gives.
        /// </summary>
        public void AddStrip(Section strip)
        {
            strip.AutoSize = false;
            strip.Margin = Padding.Empty;
            strip.Layout += (_, _) => PerformLayout();

            Controls.Add(strip);
        }

        /// <summary>How tall the rows come to at the width proposed. With none, the strips on one row at their least.</summary>
        public override Size GetPreferredSize(Size proposedSize)
        {
            var width = proposedSize.Width is > 0 and < int.MaxValue / 4
                ? proposedSize.Width
                : OneRowWidth();

            return new Size(width, Arrange(width, place: false));
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);

            Arrange(Math.Max(1, ClientSize.Width), place: true);
        }

        /// <summary>Coming into view: while it was hidden, a strip hidden in its own right could not be told from the rest.</summary>
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);

            if (Visible)
            {
                PerformLayout();
            }
        }

        /// <summary>The strips that take a place - hidden in its own right leaves one out, askable only while the bank is on screen (see Pieces.TrailingGap).</summary>
        private List<Control> Strips() =>
            Controls.Cast<Control>().Where(strip => (!Visible || strip.Visible) && strip is not ThinScrollBar).ToList();

        /// <summary>Every strip on one row, at its least.</summary>
        private int OneRowWidth()
        {
            var count = Strips().Count;

            return count == 0 ? 0 : count * Math.Max(1, _leastStripWidth()) + (count - 1) * Gap;
        }

        /// <summary>
        /// Works the rows out <paramref name="width"/> wide - placing the
        /// strips when <paramref name="place"/> is set - and returns how tall
        /// they come to. The columns share the width to the pixel, the last
        /// one ending on the edge.
        /// </summary>
        private int Arrange(int width, bool place)
        {
            var strips = Strips();

            if (strips.Count == 0)
            {
                return 0;
            }

            var fit = Math.Max(1, (width + Gap) / (Math.Max(1, _leastStripWidth()) + Gap));

            // As many to a row as fit, but rows of one length: four, two or
            // one, never three and one on its own underneath.
            var columns = strips.Count;

            while (columns > 1 && (columns > fit || strips.Count % columns != 0))
            {
                columns--;
            }

            var room = Math.Max(0, width - Gap * (columns - 1));
            var y = 0;

            for (var first = 0; first < strips.Count; first += columns)
            {
                var row = strips.Skip(first).Take(columns).ToList();
                var height = 0;

                for (var i = 0; i < row.Count; i++)
                {
                    height = Math.Max(height, row[i].GetPreferredSize(new Size(ColumnWidth(i), 0)).Height);
                }

                if (place)
                {
                    for (var i = 0; i < row.Count; i++)
                    {
                        var bounds = new Rectangle(ColumnLeft(i), y, ColumnWidth(i), height);

                        if (row[i].Bounds != bounds)
                        {
                            row[i].Bounds = bounds;
                        }
                    }
                }

                y += height + Gap;
            }

            return y - Gap;

            int ColumnLeft(int column) => column * (room / columns) + column * Gap;

            int ColumnWidth(int column) =>
                Math.Max(1, column == columns - 1 ? width - ColumnLeft(column) : room / columns);
        }
    }

    /// <summary>
    /// Refreshes the live state line under each fader, and re-reads levels
    /// in case they were changed on another tab while this one was open.
    /// </summary>
    private void RefreshMeters()
    {
        // A standing banner rather than a log line. When voice is blocked
        // the log says so once, at the moment it happens, buried among
        // everything else - by the time someone notices there's no sound,
        // that line is long gone. This states it plainly and keeps stating
        // it until it's fixed.
        if (_announcer.DescribeVoiceBlocker() is { } blocker)
        {
            _lblVoiceWarning.Text = $"NO VOICE:  {blocker}."
                + (blocker.Contains("quota", StringComparison.OrdinalIgnoreCase)
                    ? "  Sound effects, music and ambience are unaffected."
                    : string.Empty);
            _lblVoiceWarning.ForeColor = AppTheme.Danger;
            _lblVoiceWarning.Visible = true;
        }
        else if (_announcer.DescribeStandIn() is { } standIn)
        {
            // Not red: the announcer CAN speak - in the free Windows voice,
            // until ElevenLabs can again. Worth saying all the same, since
            // the voice people hear has changed.
            _lblVoiceWarning.Text = standIn.Length == 0
                ? standIn
                : $"{char.ToUpperInvariant(standIn[0])}{standIn[1..]}.";
            _lblVoiceWarning.ForeColor = AppTheme.Warning;
            _lblVoiceWarning.Visible = true;
        }
        else
        {
            _lblVoiceWarning.Visible = false;
        }

        foreach (var channel in _channels)
        {
            channel.StateLabel.Text = channel.State();

            var stored = LevelToPercent(channel.Read());

            if (stored != channel.Slider.Percent)
            {
                _loading = true;
                try
                {
                    channel.Slider.Percent = stored;
                }
                finally
                {
                    _loading = false;
                }
            }
        }
    }

    public void RefreshAll() => RefreshMeters();

    /// <summary>
    /// "Bring up:" as settings now have it - changed from outside this page
    /// by lap times only, and by leaving it (see LapTimesOnly). Only a tick
    /// that differs is set, so nothing is saved for nothing.
    /// </summary>
    public void ShowStartTicks()
    {
        if (_chkStartMusic.Checked != _settings.AudioStartMusic)
        {
            _chkStartMusic.Checked = _settings.AudioStartMusic;
        }

        if (_chkStartAmbience.Checked != _settings.AudioStartAmbience)
        {
            _chkStartAmbience.Checked = _settings.AudioStartAmbience;
        }
    }

    /// <summary>Starts (or restarts) the wait before fader levels are written - see _saveDebounce.</summary>
    private void QueueLevelSave()
    {
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    /// <summary>Writes every store with a fader change not yet on disk.</summary>
    private void SaveLevelsNow()
    {
        _saveDebounce.Stop();

        // Each flag is cleared before its save, so a store that throws
        // doesn't have the others retried over and over.
        try
        {
            if (_voiceLevelUnsaved)
            {
                _voiceLevelUnsaved = false;
                _voiceStore.Save(_voiceStore.Current);
            }

            if (_soundLevelUnsaved)
            {
                _soundLevelUnsaved = false;
                _soundStore.Save(_soundStore.Current);
            }

            if (_ambienceLevelUnsaved)
            {
                _ambienceLevelUnsaved = false;
                _ambienceStore.Save();
            }

            if (_musicLevelUnsaved)
            {
                _musicLevelUnsaved = false;
                _musicStore.Save();
            }
        }
        catch (Exception ex)
        {
            // Same as a failed save anywhere else on this page would be -
            // the level is still in use, it just isn't on disk.
            System.Diagnostics.Debug.WriteLine($"[mixer] Couldn't save levels: {ex.Message}");
        }
    }

    private void ResetLevels()
    {
        if (AppDialog.Show(this,
                "Set every level back to its default?\r\n\r\n"
                + "Announcements 100%, sound effects 100%, ambience 60%, music 40%.",
                "Reset levels", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var defaults = new[] { 1.0f, 1.0f, 0.6f, 0.4f };

        // The four levels as they were, put back by undo straight into the
        // stores and players - not through this page's faders, which may be
        // gone by then. The meter timer moves the faders to match.
        if (_undo is not null)
        {
            var voiceStore = _voiceStore;
            var soundStore = _soundStore;
            var ambienceStore = _ambienceStore;
            var ambience = _ambience;
            var musicStore = _musicStore;
            var music = _music;

            var voiceBefore = voiceStore.Current.Volume;
            var soundBefore = soundStore.Current.Volume;
            var ambienceBefore = ambienceStore.Current.MasterVolume;
            var musicBefore = musicStore.Current.Volume;

            _undo.Capture("Reset mixer levels to defaults", () =>
            {
                voiceStore.Current.Volume = voiceBefore;
                voiceStore.Save(voiceStore.Current);

                soundStore.Current.Volume = soundBefore;
                soundStore.Save(soundStore.Current);

                ambienceStore.Current.MasterVolume = ambienceBefore;
                ambienceStore.Save();
                ambience.Settings = ambienceStore.Current;
                ambience.RefreshLevels();

                musicStore.Current.Volume = musicBefore;
                musicStore.Save();
                music.Settings = musicStore.Current;
                music.RefreshVolume();
            });
        }

        for (var i = 0; i < _channels.Count && i < defaults.Length; i++)
        {
            _channels[i].Write(defaults[i]);

            _loading = true;
            try
            {
                _channels[i].Slider.Percent = LevelToPercent(defaults[i]);
                _channels[i].ValueLabel.Text = $"{LevelToPercent(defaults[i])}%";
            }
            finally
            {
                _loading = false;
            }
        }

        _lblStatus.Text = "Levels reset to defaults.";
        _lblStatus.ForeColor = AppTheme.Success;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _meterTimer.Stop();
            _meterTimer.Dispose();

            // A fader level still waiting to be written goes now, rather
            // than being lost because the page closed straight after it.
            SaveLevelsNow();
            _saveDebounce.Dispose();

            // Leaving an audition running after the page closes would keep
            // music and ambience going with no way to stop them from here.
            if (_auditioning)
            {
                StopAudition();
            }

            _auditionVoice.Stop();
            _auditionVoice.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>One row of the "music after the recap" list, and the behaviour it stands for.</summary>
    private sealed record MusicEndItem(MusicEndBehaviour Behaviour, string Label)
    {
        public override string ToString() => Label;
    }

    private MusicEndBehaviour SelectedMusicEnd() =>
        (_cmbMusicEnd.SelectedItem as MusicEndItem)?.Behaviour ?? MusicEndBehaviour.KeepPlaying;

    private void SelectMusicEnd(MusicEndBehaviour behaviour)
    {
        for (var i = 0; i < _cmbMusicEnd.Items.Count; i++)
        {
            if (_cmbMusicEnd.Items[i] is MusicEndItem item && item.Behaviour == behaviour)
            {
                _cmbMusicEnd.SelectedIndex = i;
                return;
            }
        }

        if (_cmbMusicEnd.Items.Count > 0)
        {
            _cmbMusicEnd.SelectedIndex = 0;
        }
    }
}
