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
    private readonly CheckBox _chkStartRaces = new();
    private readonly CheckBox _chkStartPractice = new();
    private readonly ComboBox _cmbStartAt = new();
    private readonly CheckBox _chkStartMusic = new();
    private readonly CheckBox _chkStartAmbience = new();
    private readonly CheckBox _chkUnmute = new();
    private readonly CheckBox _chkStopAmbience = new();
    private readonly CheckBox _chkStopMusic = new();
    private readonly ComboBox _cmbMusicEnd = new();
    private readonly NumericUpDown _numMusicFade = new();
    private readonly CheckBox _chkDuckAmbience = new();
    private readonly CheckBox _chkDuckMusic = new();

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

    /// <summary>The four strip cards, kept so they can be levelled to one height once all of them exist.</summary>
    private readonly List<Panel> _cards = new();

    /// <summary>Where the fader starts on every card - see SharedSliderTop.</summary>
    private int _sharedSliderTop;

    /// <summary>Where the four cards start: under the voice banner's line - see BuildLayout.</summary>
    private int _cardsTop = 58;

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

        // Scrolls down only. Everything here fits the width, but the moment
        // the vertical bar appears it takes its width from the page, and
        // that brought a horizontal bar with it for a few pixels. Switched
        // off before AutoScroll goes on, or AutoScroll brings it back.
        HorizontalScroll.Maximum = 0;
        HorizontalScroll.Enabled = false;
        HorizontalScroll.Visible = false;
        AutoScroll = true;
        Font = AppTheme.BodyFont;
        ForeColor = AppTheme.TextDark;

        Size = new Size(980, 664);
        MinimumSize = new Size(900, 500);

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

    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Balance every sound the app makes, in one place. These are the same levels as on the other tabs - "
                 + "changing one here changes it there. Adjustments apply immediately, so you can set the blend by ear.",
            Location = new Point(12, 12),
            AutoSize = true,
            MaximumSize = new Size(876, 0),  // capped to the page's own minimum width, not a wider guess

            // Measured below before it is parented, so it has to carry the
            // font it will be drawn in.
            Font = AppTheme.BodyFont,
            ForeColor = AppTheme.TextMuted,
        };

        // Under the intro, on a line of its own that the cards start below.
        // It used to sit at a fixed y=38, which is the middle of the intro's
        // second line - so NO VOICE was printed across the intro whenever it
        // showed. It shows more often now (the Windows voice standing in has
        // a notice here too), so it gets the room. One line, cut short
        // rather than wrapped, so it can never reach down into the cards.
        _lblVoiceWarning.Location = new Point(16, UiLayout.Below(lblIntro, UiLayout.HintGap));
        _lblVoiceWarning.AutoSize = false;
        _lblVoiceWarning.AutoEllipsis = true;
        _lblVoiceWarning.Font = AppTheme.BoldFont;
        _lblVoiceWarning.Size = new Size(868, AppTheme.BoldFont.Height + 4);  // capped to the page's own minimum width, not a wider guess
        _lblVoiceWarning.Visible = false;

        _cardsTop = UiLayout.Below(_lblVoiceWarning, UiLayout.RowGap);

        var x = UiLayout.PageLeft;
        const int stripWidth = 224;

        // Worked out once, from all four captions, before any card is
        // built - see SharedSliderTop.
        _sharedSliderTop = SharedSliderTop(
            stripWidth,
            "Spoken lines from the announcer.",
            "Per-lap chimes and race cues.",
            "Crowd and track atmosphere.",
            "Between-race soundtrack.");

        AddChannel(ref x, stripWidth,
            "Announcements",
            "Spoken lines from the announcer.",
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
            });

        AddChannel(ref x, stripWidth,
            "Sound effects",
            "Per-lap chimes and race cues.",
            () => (float)_soundStore.Current.Volume,
            v =>
            {
                _soundStore.Current.Volume = v;
                _soundLevelUnsaved = true;
                QueueLevelSave();
            },
            () => _announcer.Muted ? "Muted" : "Ready",
            () => _ = SoundPacks.PlayAsync(_soundStore.Current.Effects.GetValueOrDefault(nameof(SoundEffectKind.GoodLap)), SoundEffectKind.GoodLap, (float)_soundStore.Current.Volume));

        AddChannel(ref x, stripWidth,
            "Ambience",
            "Crowd and track atmosphere.",
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
            () => !_ambienceStore.Current.Enabled ? "Off"
                : _ambience.IsRunning ? $"Playing - {_ambience.EffectiveIntensity}"
                : "Idle");

        AddChannel(ref x, stripWidth,
            "Music",
            "Between-race soundtrack.",
            () => _musicStore.Current.Volume,
            v =>
            {
                _musicStore.Current.Volume = v;
                _musicLevelUnsaved = true;
                QueueLevelSave();
                _music.Settings = _musicStore.Current;
                _music.RefreshVolume();
            },
            () => !_musicStore.Current.Enabled ? "Off"
                : _music.NowPlaying is { } t ? $"Playing - {t.DisplayName}"
                : "Stopped");

        // Every card the same height, now that all four exist.
        LevelCards();

        var cardsRight = x - 10;

        // Duck toggles surfaced here as well as on their own tabs: this is
        // the page you're on when the ducking is bothering you, and hunting
        // two other tabs to switch it off is friction at exactly the wrong
        // moment. Same stored setting, so the two stay in step.
        // Everything from here down is measured off the control above or
        // beside it.
        //
        // It was written as literal coordinates - (280, 394), (352, 397),
        // (330, 488) - chosen against the old body font, and at the current
        // one they had stopped agreeing with each other. The worst of it was
        // the "Music after the recap" row: a hint label placed at x=330 on
        // the same y as a dropdown at x=384 and a spinner at x=582, so the
        // paragraph was drawn straight through both controls.
        //
        // Hints now go on their own line under the row they explain rather
        // than beside it. A line of prose next to a row of controls only
        // ever fits at one font size.
        var content = cardsRight - UiLayout.PageLeft;

        var y = UiLayout.Below(_cards[0], UiLayout.SectionGap);

        // --- One grid for everything below the cards ---
        //
        // A caption on the left, its choices to the right of it, one
        // setting per row. These rows were each laid out on their own,
        // left to right, so the choices started wherever their caption
        // happened to end and nothing lined up from one row to the next -
        // and each tick box was spaced by its default 104-pixel width
        // rather than by its text, which spread "Races" and "Practice" a
        // third of the page apart. Now every caption shares one column and
        // every choice starts at the same x.
        var captionFont = AppTheme.BodyFont;
        var captionTexts = new[]
        {
            "Duck under announcements:", "Start for:", "Starting at:", "Bring up:",
            "At the flag, stop:", "Music after the recap:",
        };
        var fieldX = UiLayout.PageLeft
            + captionTexts.Max(t => TextRenderer.MeasureText(t, captionFont).Width)
            + UiLayout.SectionGap;
        var fieldWidth = Math.Max(200, content - (fieldX - UiLayout.PageLeft));

        // A tick box's width by its text and box, not its default size.
        static int WidthOf(Control control) => control switch
        {
            CheckBox box => TextRenderer.MeasureText(box.Text, AppTheme.BodyFont).Width + 26,
            Label { AutoSize: true } label => TextRenderer.MeasureText(label.Text, label.Font).Width,
            _ => control.Width,
        };

        void PlaceRow(Label caption, int rowY, int rowHeight, params Control[] fields)
        {
            caption.Font = captionFont;
            caption.Location = new Point(UiLayout.PageLeft, UiLayout.CentreOn(caption, rowY, rowHeight));

            var x = fieldX;
            foreach (var field in fields)
            {
                if (field is CheckBox)
                {
                    field.Font = captionFont;
                }

                var top = field is CheckBox or Label ? UiLayout.CentreOn(field, rowY, rowHeight) : rowY;
                field.Location = new Point(x, top);

                x += WidthOf(field) + field switch
                {
                    CheckBox => UiLayout.SectionGap,
                    ComboBox => UiLayout.RowGap + 2,
                    _ => UiLayout.LabelGap,
                };
            }
        }

        // --- Duck toggles ---
        //
        // Surfaced here as well as on their own tabs: this is the page
        // you're on when the ducking is bothering you, and hunting two
        // other tabs to switch it off is friction at exactly the wrong
        // moment. Same stored setting, so the two stay in step.
        var lblDuckToggle = new Label { Text = "Duck under announcements:", AutoSize = true };

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

        var rowHeight = Math.Max(UiLayout.HeightOf(_chkDuckAmbience), AppTheme.BodyFont.Height);

        PlaceRow(lblDuckToggle, y, rowHeight, _chkDuckAmbience, _chkDuckMusic);

        var lblDuckNote = new Label
        {
            Text = "off means the bed stays at full level while the announcer talks - set how far it drops on each source's own tab",
            Location = new Point(fieldX, y + rowHeight + UiLayout.HintGap),
            AutoSize = true,
            MaximumSize = new Size(fieldWidth, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        // --- When a session starts ---
        //
        // Auto-start lives here rather than on any one source's tab: it
        // governs music, ambience AND the announcer together, so putting it
        // under Music (say) would hide two thirds of what it does.
        y = UiLayout.Below(lblDuckNote, UiLayout.SectionGap);

        var lblAutoStart = new Label
        {
            Text = "WHEN A SESSION STARTS",
            Location = new Point(UiLayout.PageLeft, y),
            AutoSize = true,
            Font = AppTheme.BoldFont,
            ForeColor = AppTheme.Primary,
        };

        y = UiLayout.Below(lblAutoStart, UiLayout.HeadingGap);

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

        var lblStartFor = new Label { Text = "Start for:", AutoSize = true };
        var lblStartAt = new Label { Text = "Starting at:", AutoSize = true };

        _cmbStartAt.Font = AppTheme.BodyFont;
        _cmbStartAt.Width = 150;
        _cmbStartAt.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbStartAt.Items.AddRange(new object[] { "Staging", "Green flag" });
        _cmbStartAt.SelectedIndex = _settings.AudioStartAtStaging ? 0 : 1;
        _cmbStartAt.SelectedIndexChanged += (_, _) =>
        {
            _settings.AudioStartAtStaging = _cmbStartAt.SelectedIndex == 0;
            _settings.Save();
        };

        var lblStartWhat = new Label { Text = "Bring up:", AutoSize = true };

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

        var startRow = _cmbStartAt.Height;

        PlaceRow(lblStartFor, y, startRow, _chkStartRaces, _chkStartPractice);
        y += startRow + UiLayout.RowGap;
        PlaceRow(lblStartAt, y, startRow, _cmbStartAt);
        y += startRow + UiLayout.RowGap;
        PlaceRow(lblStartWhat, y, startRow, _chkStartMusic, _chkStartAmbience, _chkUnmute);
        y += startRow + UiLayout.SectionGap;

        // --- When it ends ---
        var lblAutoEnd = new Label
        {
            Text = "WHEN IT ENDS",
            Location = new Point(UiLayout.PageLeft, y),
            AutoSize = true,
            Font = AppTheme.BoldFont,
            ForeColor = AppTheme.Primary,
        };

        y = UiLayout.Below(lblAutoEnd, UiLayout.HeadingGap);

        var lblStopAfter = new Label { Text = "At the flag, stop:", AutoSize = true };

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
        var lblMusicEnd = new Label { Text = "Music after the recap:", AutoSize = true };

        _cmbMusicEnd.Font = AppTheme.BodyFont;
        _cmbMusicEnd.Width = 170;
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

        var lblFadeOver = new Label { Text = "over", AutoSize = true };

        _numMusicFade.Font = AppTheme.BodyFont;
        _numMusicFade.Width = UiLayout.NumericWidth;
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

        var lblFadeSuffix = new Label { Text = "s", AutoSize = true, ForeColor = AppTheme.TextMuted };

        var endRow = Math.Max(_cmbMusicEnd.Height, _numMusicFade.Height);

        lblFadeOver.Font = captionFont;
        lblFadeSuffix.Font = captionFont;

        PlaceRow(lblStopAfter, y, endRow, _chkStopMusic, _chkStopAmbience);
        y += endRow + UiLayout.RowGap;
        PlaceRow(lblMusicEnd, y, endRow, _cmbMusicEnd, lblFadeOver, _numMusicFade, lblFadeSuffix);

        var lblContinueHint = new Label
        {
            Text = "Only stops what was started automatically - anything you started by hand keeps going. "
                 + "Ambience waits for the finish celebration to play out first. "
                 + "With Music ticked above, the music stops at the flag and the choice after the recap doesn't apply.",
            Location = new Point(fieldX, y + endRow + UiLayout.HintGap),
            AutoSize = true,
            MaximumSize = new Size(fieldWidth, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        // --- Audition cadence ---
        y = UiLayout.Below(lblContinueHint, UiLayout.RowGap);

        var lblAnnounceEvery = new Label { Text = "Announce every:", AutoSize = true };

        _numAnnounceEvery.Font = AppTheme.BodyFont;
        _numAnnounceEvery.Width = UiLayout.NumericWidth;
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

        var lblSeconds = new Label { Text = "seconds", AutoSize = true };

        lblAnnounceEvery.Location = new Point(UiLayout.PageLeft, UiLayout.CentreOn(lblAnnounceEvery, y, _numAnnounceEvery.Height));
        _numAnnounceEvery.Location = new Point(UiLayout.After(lblAnnounceEvery), y);
        lblSeconds.Location = new Point(UiLayout.After(_numAnnounceEvery), UiLayout.CentreOn(lblSeconds, y, _numAnnounceEvery.Height));

        var lblAuditionHint = new Label
        {
            Text = "The bed plays clean first, so you can hear the blend before anything ducks it. "
                 + "Widen this to listen longer between announcements.",
            Location = new Point(UiLayout.PageLeft + UiLayout.Indent, UiLayout.Below(_numAnnounceEvery, UiLayout.HintGap)),
            AutoSize = true,
            MaximumSize = new Size(content - UiLayout.Indent, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        // --- Buttons ---
        //
        // Sized by the row helper rather than given a height of 28. At the
        // current font a 28-pixel button clips its own caption, which is
        // what had happened to "Play Everything".
        y = UiLayout.Below(lblAuditionHint, UiLayout.SectionGap);

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

        y = UiLayout.FlowButtons(
            UiLayout.PageLeft, y, UiLayout.PageLeft + content,
            _btnAuditionAll, _btnSpeakNow, btnMuteAll, btnReset);

        _lblStatus.Location = new Point(UiLayout.PageLeft, y + UiLayout.RowGap);
        _lblStatus.AutoSize = true;
        _lblStatus.MaximumSize = new Size(content, 0);
        _lblStatus.ForeColor = AppTheme.TextMuted;
        _lblStatus.Text = "Drag a fader to set its level - changes save as you go.";

        Controls.Add(lblIntro);
        Controls.Add(_lblVoiceWarning);
        Controls.Add(lblDuckToggle);
        Controls.Add(_chkDuckAmbience);
        Controls.Add(_chkDuckMusic);
        Controls.Add(lblDuckNote);

        // The auto-start block. These were created and positioned but never
        // registered, so they existed in code and rendered nowhere - the
        // section simply appeared to be missing.
        Controls.Add(lblAutoStart);
        Controls.Add(_chkStartRaces);
        Controls.Add(_chkStartPractice);
        Controls.Add(lblStartFor);
        Controls.Add(lblStartAt);
        Controls.Add(_cmbStartAt);
        Controls.Add(lblStartWhat);
        Controls.Add(_chkStartMusic);
        Controls.Add(_chkStartAmbience);
        Controls.Add(_chkUnmute);
        Controls.Add(lblAutoEnd);
        Controls.Add(lblStopAfter);
        Controls.Add(_chkStopMusic);
        Controls.Add(_chkStopAmbience);
        Controls.Add(lblMusicEnd);
        Controls.Add(_cmbMusicEnd);
        Controls.Add(lblFadeOver);
        Controls.Add(_numMusicFade);
        Controls.Add(lblFadeSuffix);
        Controls.Add(lblContinueHint);

        Controls.Add(_btnAuditionAll);
        Controls.Add(_btnSpeakNow);
        Controls.Add(lblAnnounceEvery);
        Controls.Add(_numAnnounceEvery);
        Controls.Add(lblSeconds);
        Controls.Add(lblAuditionHint);
        Controls.Add(btnMuteAll);
        Controls.Add(btnReset);
        Controls.Add(_lblStatus);

        RefreshMeters();
        UpdateAutoStartState();
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
    /// The y every fader starts at, measured from the TALLEST of the four
    /// card descriptions rather than from each card's own.
    ///
    /// One number for the bank is the whole point: it is what makes four
    /// strips read as a mixer instead of as four separate sliders that
    /// happen to be side by side.
    /// </summary>
    private static int SharedSliderTop(int width, params string[] descriptions)
    {
        const int pad = 12;

        var inner = Math.Max(40, width - pad * 2);

        var nameHeight = TextRenderer.MeasureText("Xg", AppTheme.BoldFont).Height;

        var tallest = descriptions.Max(text => TextRenderer.MeasureText(
            text,
            AppTheme.SmallFont,
            new Size(inner, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height);

        return pad + nameHeight + 6 + tallest + 14;
    }

    /// <summary>
    /// Gives every card the height of the tallest, so the bank has one
    /// straight bottom edge.
    /// </summary>
    private void LevelCards()
    {
        if (_cards.Count == 0)
        {
            return;
        }

        var tallest = _cards.Max(card => card.Height);

        foreach (var card in _cards)
        {
            card.Height = tallest;
        }
    }

    private void AddChannel(
        ref int x,
        int width,
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
        const int pad = 12;

        var panel = new Panel
        {
            Location = new Point(x, _cardsTop),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = AppTheme.Surface,
        };

        // ApplyRecursively repaints every Panel in the app with the page
        // background, which would flatten these cards into the page behind
        // them. Tagged so it leaves this one alone.
        panel.Tag = AppTheme.KeepBackColor;

        var lblName = new Label
        {
            Text = name,
            Location = new Point(pad, pad),
            AutoSize = true,
            MaximumSize = new Size(width - pad * 2, 0),
            Font = AppTheme.BoldFont,
            ForeColor = AppTheme.Primary,
        };

        var lblDescription = new Label
        {
            Text = description,
            Location = new Point(pad, Below(lblName, 6)),
            AutoSize = true,
            MaximumSize = new Size(width - pad * 2, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        // Where the fader starts is decided for the whole bank, not by this
        // card's own caption.
        //
        // "Spoken lines from the announcer." wraps to two lines at this
        // width where the other three fit on one. Letting each card place
        // its fader under its own description put the Announcements fader a
        // line lower than its neighbours, pushed its Test button down with
        // it, and left that card taller than the other three - four strips
        // that should read as one bank, each starting somewhere different.
        var sliderTop = _sharedSliderTop;

        // A FaderControl, not a TrackBar.
        //
        // The fader you can see and the fader you can grab are now the same
        // object - see FaderControl for why that took four goes. Percent is
        // the real number here, 100 at the top, so nothing on this page
        // flips anything any more.
        var slider = new FaderControl
        {
            Location = new Point(pad - 6, sliderTop),
            Size = new Size(48, 200),
            Percent = (int)Math.Clamp(read() * 100f, 0, 100),
            SmallChange = 1,
            LargeChange = 10,
        };

        var valueLabel = new Label
        {
            Text = $"{slider.Percent}%",
            Location = new Point(slider.Right + 10, sliderTop),
            AutoSize = true,
            Font = AppTheme.BoldFont,
        };

        var stateLabel = new Label
        {
            Text = string.Empty,
            Location = new Point(valueLabel.Left, Below(valueLabel, 8)),
            AutoSize = true,
            MaximumSize = new Size(width - valueLabel.Left - pad, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

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

        panel.Controls.Add(lblName);
        panel.Controls.Add(lblDescription);
        panel.Controls.Add(slider);
        panel.Controls.Add(valueLabel);
        panel.Controls.Add(stateLabel);

        // The fader goes to the front, whatever the measurements said.
        //
        // In WinForms the control at index 0 is the TOP of the z-order, and
        // Controls.Add appends - so the two labels added above this are in
        // front of the fader, and the descriptions are wide enough to cross
        // it. If a measurement is ever wrong again, the worst case should
        // be a caption that looks untidy, not a control that cannot be
        // touched. A label has nothing to do with the mouse; the fader has
        // nothing but.
        slider.BringToFront();

        if (test is not null)
        {
            var btnTest = new Button
            {
                Text = "Test",
                // Under the fader, not beside it: the state line above can
                // wrap to two or three lines depending on what it is
                // reporting, and anything placed at a fixed height beside
                // the fader is one long message away from being sat on.
                // Level with the bottom of the fader, on every card. The
                // state line above it reports live text - "Playing - track
                // name" - so hanging this off THAT would move the button
                // around during a race.
                Location = new Point(valueLabel.Left, slider.Bottom - UiLayout.ButtonHeight),
                Width = Math.Max(80, width - valueLabel.Left - pad),
                Height = UiLayout.ButtonHeight,
            };
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
            panel.Controls.Add(btnTest);
        }

        // The card is as tall as what ended up in it. Fixing its height
        // instead is how the contents came to be clipped at the bottom: the
        // card was 332 high whatever the font, so the moment the wrapped
        // text above it needed another line, the test button went under the
        // border.
        var contentBottom = pad;

        foreach (Control child in panel.Controls)
        {
            contentBottom = Math.Max(contentBottom, child.Bottom);
        }

        panel.Size = new Size(width, contentBottom + pad);

        _cards.Add(panel);
        Controls.Add(panel);

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

        x += width + 10;
    }

    /// <summary>
    /// The top edge for something that goes under a control, with a gap.
    ///
    /// Hands off to UiLayout, which measures a WRAPPED label properly. The
    /// version that lived here used Label.PreferredHeight, which reports
    /// the height of one line whatever MaximumSize says - so a three-line
    /// channel description measured as one and everything below it was
    /// placed two lines too high. That was a real bug and this is the real
    /// fix for it.
    ///
    /// It was not, however, why the faders could not be dragged, which is
    /// what the comment here used to claim. See FaderControl.
    /// </summary>
    private static int Below(Control control, int gap) => UiLayout.Below(control, gap);

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

            var stored = (int)Math.Clamp(channel.Read() * 100f, 0, 100);

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
                _channels[i].Slider.Percent = (int)(defaults[i] * 100f);
                _channels[i].ValueLabel.Text = $"{(int)(defaults[i] * 100f)}%";
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
