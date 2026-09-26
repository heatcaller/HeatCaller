namespace HeatCaller;

/// <summary>
/// Crowd and track ambience settings.
///
/// Ambience is continuous background audio that plays UNDERNEATH the
/// announcements rather than queued with them, so the page leads with the
/// fact that files have to be supplied - there's nothing built in, and a
/// page that looks configured but makes no sound is the confusing outcome
/// to avoid.
/// </summary>
public sealed class AmbienceControl : UserControl
{
    private readonly AmbienceStore _store;
    private readonly AmbienceService _ambience;
    private readonly UndoService _undo;

    private readonly CheckBox _chkEnabled = new();
    private readonly NumericUpDown _numMasterVolume = new();

    // Which layers play. Music has the same idea as a pair; this is three,
    // because ambience layers mix rather than replace one another. See
    // AmbienceSource.
    private readonly RadioButton _rdoSourceBoth = new();
    private readonly RadioButton _rdoSourceSoundscape = new();
    private readonly RadioButton _rdoSourceMine = new();
    private readonly Label _lblSource = new();

    /// <summary>Set while the radios are being brought into line with the store, so seeding them isn't read as a click.</summary>
    private bool _syncingSource;

    private readonly DataGridView _grid = new();
    private readonly CheckBox _chkReact = new();
    private readonly NumericUpDown _numSurgeSeconds = new();
    private readonly NumericUpDown _numFinishSeconds = new();
    private readonly CheckBox _chkDuck = new();
    private readonly NumericUpDown _numDuckTo = new();
    private readonly CheckBox _chkBoostVoice = new();
    private readonly NumericUpDown _numVoiceBoost = new();
    private readonly CheckBox _chkFastLap = new();
    private readonly CheckBox _chkLeadChange = new();
    private readonly CheckBox _chkTrackRecord = new();
    private readonly CheckBox _chkJumpStart = new();
    private readonly CheckBox _chkFinish = new();
    private readonly CheckBox _chkLastLap = new();
    private readonly CheckBox _chkCloseBattle = new();
    private readonly CheckBox _chkStreak = new();
    private readonly CheckBox _chkPersonalBest = new();
    private readonly CheckBox _chkLapped = new();
    private readonly NumericUpDown _numFadeUp = new();
    private readonly NumericUpDown _numFadeDown = new();
    private readonly Label _lblStatus = new();
    private readonly Label _lblReactionsState = new();
    // Plain Panel plus a manual double-buffer style: Panel.DoubleBuffered
    // is protected, so it can't be set from an object initializer.
    private readonly Panel _monitor = new();
    private readonly System.Windows.Forms.Timer _monitorTimer = new() { Interval = 100 };

    public AmbienceControl(AmbienceStore store, AmbienceService ambience, UndoService undo)
    {
        _store = store;
        _ambience = ambience;
        _undo = undo;

        Dock = DockStyle.Fill;
        AutoScroll = true;
        Font = AppTheme.BodyFont;
        ForeColor = AppTheme.TextDark;

        // Taller by the Play row and the sentence under it, so the page is
        // not left one row short and scrolling for it.
        Size = new Size(980, 900);
        MinimumSize = new Size(900, 560);

        BuildLayout();
        RefreshGrid();

        // An undo changes the layers from outside this page - see
        // OnStoreRestored.
        _store.Restored += OnStoreRestored;

        // 100ms keeps the meters moving smoothly during a fade without
        // repainting more often than the eye resolves.
        _monitorTimer.Tick += (_, _) => _monitor.Invalidate();
        _monitorTimer.Start();
    }

    /// <summary>
    /// Re-reads the settings after an undo changed them. The undo itself
    /// only touches the store and the player, never this page.
    /// </summary>
    private void OnStoreRestored()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        RefreshAll();
    }

    /// <summary>
    /// Hands the player the settings an undo just put back, restarting it
    /// if it is playing - layers are opened when it starts. Works on the
    /// store and the player only, so it is safe from an undo that runs after
    /// this page has gone.
    /// </summary>
    private static void AfterUndo(AmbienceStore store, AmbienceService ambience)
    {
        store.Save();
        ambience.Settings = store.Current;

        if (ambience.IsRunning)
        {
            ambience.Stop();
            ambience.Start(ignoreEnabledSetting: true);
        }

        store.NotifyRestored();
    }

    private void BuildLayout()
    {
        // Laid out against Layout's shared constants rather than hand-picked
        // coordinates. This page had 31 different left edges across 40 rows -
        // nothing lined up with anything, which is what made it read as a
        // patchwork however tidy each individual row looked.
        var y = UiLayout.PageTop;
        var contentWidth = UiLayout.ContentWidth(Width);

        // --- Intro -------------------------------------------------------

        var lblIntro = new Label
        {
            Text = "Continuous crowd and track noise, mixed underneath the announcements rather than queued with them, "
                 + "so the ambience keeps running while the announcer talks over it. You supply the audio files - "
                 + "short looping clips of 10-30 seconds work best.",
            Location = new Point(UiLayout.PageLeft, y),
            AutoSize = true,
            MaximumSize = new Size(contentWidth, 0),
            ForeColor = AppTheme.TextMuted,
        };
        y += 44;

        // --- Master ------------------------------------------------------

        _chkEnabled.Text = "Enable ambience";
        _chkEnabled.AutoSize = true;
        _chkEnabled.Font = AppTheme.BoldFont;
        _chkEnabled.Checked = _store.Current.Enabled;
        _chkEnabled.CheckedChanged += (_, _) =>
        {
            // Brought into line with the settings by RefreshAll - not a
            // click, so nothing is saved or started. See RefreshAll.
            if (_syncingEnabled)
            {
                return;
            }

            _store.Current.Enabled = _chkEnabled.Checked;
            _store.Save();
            ApplyRunState(startIfEnabled: _chkEnabled.Checked);
            UpdateEnabledState();
        };

        var lblMaster = new Label { Text = "Overall level:", AutoSize = true };
        _numMasterVolume.Width = UiLayout.NumericWidth;
        _numMasterVolume.Minimum = 0;
        _numMasterVolume.Maximum = 100;
        _numMasterVolume.Increment = 5;
        _numMasterVolume.Value = (decimal)Math.Clamp(_store.Current.MasterVolume * 100f, 0, 100);
        _numMasterVolume.ValueChanged += (_, _) =>
        {
            _store.Current.MasterVolume = (float)_numMasterVolume.Value / 100f;
            _store.Save();
            _ambience.Settings = _store.Current;

            // Heard straight away, at the crowd's CURRENT mood. This asked
            // for Racing, which moved a between-races crowd up to race level
            // (and kept it there) the moment the level was touched - the
            // same fault the Mixer's fader had. See RefreshLevels.
            _ambience.RefreshLevels();
        };

        var lblMasterSuffix = new Label { Text = "%  - scales every layer at once", AutoSize = true, ForeColor = AppTheme.TextMuted, Font = AppTheme.SmallFont };

        _chkEnabled.Location = new Point(UiLayout.PageLeft, UiLayout.LabelY(y));
        lblMaster.Location = new Point(
            Math.Max(220, UiLayout.After(_chkEnabled) + UiLayout.SectionGap),
            UiLayout.LabelY(y));

        _numMasterVolume.Location = new Point(UiLayout.After(lblMaster), y);
        lblMasterSuffix.Location = new Point(_numMasterVolume.Right + UiLayout.HintGap, UiLayout.LabelY(y));
        y = UiLayout.NextRow(y);

        // --- Live monitor -------------------------------------------------

        _monitor.Location = new Point(UiLayout.PageLeft, y);
        _monitor.Size = new Size(contentWidth, 78);
        _monitor.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _monitor.Paint += Monitor_Paint;

        typeof(Panel).GetProperty("DoubleBuffered",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(_monitor, true);

        y += 78 + UiLayout.RowGap;

        // --- Layers -------------------------------------------------------

        var lblLayers = new Label { Text = "LAYERS" };
        y = UiLayout.SectionHeading(UiLayout.SectionAfter(y - UiLayout.RowGap), lblLayers);

        var lblLayersHint = new Label
        {
            Text = "Each plays continuously at a volume that follows what's happening on track.",
            Location = new Point(UiLayout.PageLeft, y),
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };
        y += UiLayout.HintHeight + UiLayout.HintGap;

        // --- Which layers play ---
        //
        // The same row Music has, with one more choice on it. Music's is a
        // pair because a playlist replaces a playlist; these mix, so Both
        // is a real answer and it is the default - a club that never comes
        // here hears exactly what it heard before.
        var lblPlay = new Label { Text = "Play:", AutoSize = true, Font = AppTheme.BodyFont };

        _rdoSourceBoth.Text = "Both";
        _rdoSourceSoundscape.Text = "Soundscape ambience";
        _rdoSourceMine.Text = "My ambience";

        var sourceLeft = UiLayout.PageLeft;
        lblPlay.Location = new Point(sourceLeft, UiLayout.CentreOn(lblPlay, y, UiLayout.RowHeight));
        sourceLeft = UiLayout.After(lblPlay);

        foreach (var radio in new[] { _rdoSourceBoth, _rdoSourceSoundscape, _rdoSourceMine })
        {
            radio.AutoSize = true;
            radio.Font = AppTheme.BodyFont;
            radio.Location = new Point(sourceLeft, UiLayout.CentreOn(radio, y, UiLayout.RowHeight));
            sourceLeft = UiLayout.After(radio) + UiLayout.RowGap;

            // Only on the one being switched ON: a radio group raises this
            // twice per change, and acting on both would save the losing
            // choice a moment before the winning one.
            radio.CheckedChanged += (_, _) =>
            {
                if (_syncingSource || !radio.Checked)
                {
                    return;
                }

                SetSource(
                    ReferenceEquals(radio, _rdoSourceSoundscape) ? AmbienceSource.Soundscape
                    : ReferenceEquals(radio, _rdoSourceMine) ? AmbienceSource.Mine
                    : AmbienceSource.Both);
            };
        }

        y += UiLayout.RowHeight + UiLayout.HintGap;

        _lblSource.Location = new Point(UiLayout.PageLeft, y);
        _lblSource.AutoSize = true;
        _lblSource.MaximumSize = new Size(contentWidth, 0);
        _lblSource.ForeColor = AppTheme.TextMuted;
        _lblSource.Font = AppTheme.SmallFont;

        y += UiLayout.HintHeight + UiLayout.RowGap;

        _grid.Location = new Point(UiLayout.PageLeft, y);
        _grid.Size = new Size(contentWidth, 170);
        _grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        // No pop-up for a cut-off cell. The grid's built-in one is Windows'
        // pale yellow, unreachable by any theme, and it says nothing except
        // the text already under the pointer - a layer called "Synthwave
        // crowd (from the..." pops up "Synthwave crowd (from the pack)".
        // AppTheme.StyleGrid turns this off for every grid it themes; set
        // here as well because this one is evidently not getting that pass.
        _grid.ShowCellToolTips = false;
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "name", HeaderText = "Layer", FillWeight = 70 },

            // Where the layer came from. Until now the only cue was the
            // name a soundscape happened to give its bed ending in "(from
            // the soundscape)" - which is a convention, not an answer, and
            // said nothing at all about the club's own layers.
            new DataGridViewTextBoxColumn { Name = "source", HeaderText = "Source", FillWeight = 34 },
            new DataGridViewTextBoxColumn { Name = "file", HeaderText = "File", FillWeight = 96 },
            new DataGridViewTextBoxColumn { Name = "idle", HeaderText = "Idle", FillWeight = 32 },
            new DataGridViewTextBoxColumn { Name = "anticipation", HeaderText = "Staging", FillWeight = 40 },
            new DataGridViewTextBoxColumn { Name = "racing", HeaderText = "Racing", FillWeight = 38 },
            new DataGridViewTextBoxColumn { Name = "excited", HeaderText = "Excited", FillWeight = 40 },
            new DataGridViewTextBoxColumn { Name = "peak", HeaderText = "Peak", FillWeight = 34 }
        );
        _grid.CellDoubleClick += (_, args) => { if (args.RowIndex >= 0) { EditLayer(Selected()); } };
        y += 170 + UiLayout.RowGap;

        var btnAdd = new Button { Text = "Add Layer...", Width = UiLayout.ButtonWidth, Height = UiLayout.ButtonHeight };
        btnAdd.Click += (_, _) => EditLayer(null);

        var btnEdit = new Button { Text = "Edit...", Width = 90, Height = UiLayout.ButtonHeight };
        btnEdit.Click += (_, _) => EditLayer(Selected());

        var btnDelete = new Button { Text = "Delete", Width = 90, Height = UiLayout.ButtonHeight };
        btnDelete.Click += (_, _) => DeleteLayer();

        var btnPreview = new Button { Text = "Preview", Width = 100, Height = UiLayout.ButtonHeight };
        btnPreview.Click += (_, _) => TogglePreview(btnPreview);

        y = UiLayout.Row(y, btnAdd, btnEdit, btnDelete, btnPreview);

        // --- Reactions ----------------------------------------------------

        var lblReactions = new Label { Text = "REACTIONS" };
        var reactionsY = UiLayout.SectionAfter(y - UiLayout.RowGap);
        y = UiLayout.SectionHeading(reactionsY, lblReactions);

        _lblReactionsState.Location = new Point(140, reactionsY + 2);
        _lblReactionsState.AutoSize = true;
        _lblReactionsState.MaximumSize = new Size(contentWidth - 140, 0);
        _lblReactionsState.Font = AppTheme.SmallFont;

        _chkReact.Text = "React to what happens in the race";
        _chkReact.Location = new Point(UiLayout.PageLeft, UiLayout.LabelY(y));
        _chkReact.AutoSize = true;
        _chkReact.Checked = _store.Current.ReactToEvents;
        _chkReact.CheckedChanged += (_, _) =>
        {
            _store.Current.ReactToEvents = _chkReact.Checked;
            _store.Save();
            _ambience.Settings = _store.Current;
            UpdateEnabledState();
        };
        y = UiLayout.NextRow(y);

        // Timing row: surge, fades and the finish celebration together,
        // since they're all "how long does something take".
        // These two rows used fixed-width labels (96 / 150 / 110) at fixed x
        // (136 / 208 / 360 / 432, then 150 / 222 / 294), every number of
        // them measured against a smaller font. At the current one each
        // caption runs past its own box and each field lands inside the
        // caption before it - which is the seventeen adjustments this page
        // reported, the most of any page in the app. Labels now size
        // themselves and every control is placed after the one before it.
        var lblSurge = new Label { Text = "Surge lasts:", AutoSize = true };
        _numSurgeSeconds.Width = UiLayout.NumericWidth;
        _numSurgeSeconds.Minimum = 1;
        _numSurgeSeconds.Maximum = 30;
        _numSurgeSeconds.Value = Math.Clamp(_store.Current.SurgeSeconds, 1, 30);
        _numSurgeSeconds.ValueChanged += (_, _) => { _store.Current.SurgeSeconds = (int)_numSurgeSeconds.Value; _store.Save(); _ambience.Settings = _store.Current; };

        var lblFinish = new Label { Text = "s      Finish celebration:", AutoSize = true };
        _numFinishSeconds.Width = UiLayout.NumericWidth;
        _numFinishSeconds.Minimum = 0;
        _numFinishSeconds.Maximum = 300;
        _numFinishSeconds.Increment = 5;
        _numFinishSeconds.Value = Math.Clamp(_store.Current.FinishCelebrationSeconds, 0, 300);
        _numFinishSeconds.ValueChanged += (_, _) => { _store.Current.FinishCelebrationSeconds = (int)_numFinishSeconds.Value; _store.Save(); _ambience.Settings = _store.Current; };

        var lblSurgeSuffix = new Label { Text = "s  - then it settles back to idle", AutoSize = true, ForeColor = AppTheme.TextMuted, Font = AppTheme.SmallFont };

        lblSurge.Location = new Point(UiLayout.Indent + UiLayout.PageLeft, UiLayout.LabelY(y));
        _numSurgeSeconds.Location = new Point(UiLayout.After(lblSurge), y);
        lblFinish.Location = new Point(_numSurgeSeconds.Right + UiLayout.HintGap, UiLayout.LabelY(y));
        _numFinishSeconds.Location = new Point(UiLayout.After(lblFinish), y);
        lblSurgeSuffix.Location = new Point(_numFinishSeconds.Right + UiLayout.HintGap, UiLayout.LabelY(y));
        y = UiLayout.NextRow(y);

        var lblFadeUp = new Label { Text = "Fade up / down:", AutoSize = true };
        _numFadeUp.Width = UiLayout.NumericWidth;
        _numFadeUp.DecimalPlaces = 1;
        _numFadeUp.Increment = 0.5m;
        _numFadeUp.Minimum = 0.1m;
        _numFadeUp.Maximum = 30m;
        _numFadeUp.Value = (decimal)Math.Clamp(_store.Current.FadeUpSeconds, 0.1, 30);
        _numFadeUp.ValueChanged += (_, _) => { _store.Current.FadeUpSeconds = (double)_numFadeUp.Value; _store.Save(); _ambience.Settings = _store.Current; };

        _numFadeDown.Width = UiLayout.NumericWidth;
        _numFadeDown.DecimalPlaces = 1;
        _numFadeDown.Increment = 0.5m;
        _numFadeDown.Minimum = 0.1m;
        _numFadeDown.Maximum = 30m;
        _numFadeDown.Value = (decimal)Math.Clamp(_store.Current.FadeDownSeconds, 0.1, 30);
        _numFadeDown.ValueChanged += (_, _) => { _store.Current.FadeDownSeconds = (double)_numFadeDown.Value; _store.Save(); _ambience.Settings = _store.Current; };

        var lblFadeHint = new Label { Text = "seconds - a slower fall than rise sounds most natural", AutoSize = true, ForeColor = AppTheme.TextMuted, Font = AppTheme.SmallFont };

        lblFadeUp.Location = new Point(UiLayout.Indent + UiLayout.PageLeft, UiLayout.LabelY(y));
        _numFadeUp.Location = new Point(UiLayout.After(lblFadeUp), y);
        _numFadeDown.Location = new Point(_numFadeUp.Right + UiLayout.HintGap, y);
        lblFadeHint.Location = new Point(_numFadeDown.Right + UiLayout.LabelGap, UiLayout.LabelY(y));
        y = UiLayout.NextRow(y);

        // Triggers on a tidy two-row grid of four columns.
        var triggers = new (CheckBox Box, string Text, Func<bool> Get, Action<bool> Set)[]
        {
            (_chkFastLap,      "Fastest lap",      () => _store.Current.SurgeOnFastLap,      v => _store.Current.SurgeOnFastLap = v),
            (_chkLeadChange,   "Lead change",      () => _store.Current.SurgeOnLeadChange,   v => _store.Current.SurgeOnLeadChange = v),
            (_chkTrackRecord,  "Track record",     () => _store.Current.SurgeOnTrackRecord,  v => _store.Current.SurgeOnTrackRecord = v),
            (_chkFinish,       "Checkered flag",   () => _store.Current.SurgeOnFinish,       v => _store.Current.SurgeOnFinish = v),
            (_chkLastLap,      "Last lap",         () => _store.Current.SurgeOnLastLap,      v => _store.Current.SurgeOnLastLap = v),
            (_chkCloseBattle,  "Close battle",     () => _store.Current.SurgeOnCloseBattle,  v => _store.Current.SurgeOnCloseBattle = v),
            (_chkStreak,       "Consistency streak", () => _store.Current.SurgeOnStreak,     v => _store.Current.SurgeOnStreak = v),
            (_chkJumpStart,    "Jump start",       () => _store.Current.SurgeOnJumpStart,    v => _store.Current.SurgeOnJumpStart = v),
            (_chkPersonalBest, "Personal best",    () => _store.Current.SurgeOnPersonalBest, v => _store.Current.SurgeOnPersonalBest = v),
            (_chkLapped,       "Car gets lapped",  () => _store.Current.SurgeOnLapped,       v => _store.Current.SurgeOnLapped = v),
        };

        const int triggerColumns = 4;
        var columnWidth = (contentWidth - UiLayout.Indent) / triggerColumns;

        for (var i = 0; i < triggers.Length; i++)
        {
            var (box, text, get, set) = triggers[i];
            var column = i % triggerColumns;
            var row = i / triggerColumns;

            box.Text = text;
            box.AutoSize = true;
            box.Checked = get();
            box.Location = new Point(
                UiLayout.PageLeft + UiLayout.Indent + column * columnWidth,
                y + row * UiLayout.RowStep);

            var setter = set;
            var source = box;
            box.CheckedChanged += (_, _) => { setter(source.Checked); _store.Save(); _ambience.Settings = _store.Current; };
        }

        y += ((triggers.Length + triggerColumns - 1) / triggerColumns) * UiLayout.RowStep;

        var lblOnceHint = new Label
        {
            Text = "Close battle and lapping fire once per race - a long scrap would otherwise keep the level pinned high.",
            Location = new Point(UiLayout.PageLeft + UiLayout.Indent, y),
            AutoSize = true,
            MaximumSize = new Size(contentWidth - UiLayout.Indent, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };
        y += UiLayout.HintHeight + UiLayout.RowGap;

        // --- Ducking ------------------------------------------------------

        var lblDucking = new Label { Text = "DUCKING" };
        y = UiLayout.SectionHeading(UiLayout.SectionAfter(y - UiLayout.RowGap), lblDucking);

        _chkDuck.Text = "Duck ambience while the announcer speaks";
        _chkDuck.Location = new Point(UiLayout.PageLeft, UiLayout.LabelY(y));
        _chkDuck.AutoSize = true;
        _chkDuck.Checked = _store.Current.DuckUnderAnnouncements;
        _chkDuck.CheckedChanged += (_, _) =>
        {
            _store.Current.DuckUnderAnnouncements = _chkDuck.Checked;
            _store.Save();
            _ambience.Settings = _store.Current;
            UpdateEnabledState();
        };

        var lblDuckTo = new Label { Text = "down to:", AutoSize = true };
        _numDuckTo.Width = UiLayout.NumericWidth;
        _numDuckTo.Minimum = 5;
        _numDuckTo.Maximum = 100;
        _numDuckTo.Increment = 5;
        _numDuckTo.Value = (decimal)Math.Clamp(_store.Current.DuckToFraction * 100f, 5, 100);
        _numDuckTo.ValueChanged += (_, _) => { _store.Current.DuckToFraction = (float)_numDuckTo.Value / 100f; _store.Save(); _ambience.Settings = _store.Current; };

        var lblDuckHint = new Label { Text = "%  - pulling the bed down reads clearer than pushing the voice up", AutoSize = true, ForeColor = AppTheme.TextMuted, Font = AppTheme.SmallFont };

        // Beside the check box it belongs to, measured from it - 330 was
        // clear of that caption at the old font and inside it at this one.
        lblDuckTo.Location = new Point(
            Math.Max(330, UiLayout.After(_chkDuck) + UiLayout.SectionGap),
            UiLayout.LabelY(y));

        _numDuckTo.Location = new Point(UiLayout.After(lblDuckTo), y);
        lblDuckHint.Location = new Point(_numDuckTo.Right + UiLayout.HintGap, UiLayout.LabelY(y));
        y = UiLayout.NextRow(y);

        _chkBoostVoice.Text = "Also lift the voice when the bed is loud";
        _chkBoostVoice.Location = new Point(UiLayout.PageLeft, UiLayout.LabelY(y));
        _chkBoostVoice.AutoSize = true;
        _chkBoostVoice.Checked = _store.Current.BoostVoiceOverAmbience;
        _chkBoostVoice.CheckedChanged += (_, _) =>
        {
            _store.Current.BoostVoiceOverAmbience = _chkBoostVoice.Checked;
            _store.Save();
            _ambience.Settings = _store.Current;
            UpdateEnabledState();
        };

        var lblBoost = new Label { Text = "up to x", AutoSize = true };
        _numVoiceBoost.Width = UiLayout.NumericWidth;
        _numVoiceBoost.DecimalPlaces = 1;
        _numVoiceBoost.Increment = 0.1m;
        _numVoiceBoost.Minimum = 1.0m;
        _numVoiceBoost.Maximum = 2.0m;
        _numVoiceBoost.Value = (decimal)Math.Clamp(_store.Current.VoiceBoostMultiplier, 1.0f, 2.0f);
        _numVoiceBoost.ValueChanged += (_, _) => { _store.Current.VoiceBoostMultiplier = (float)_numVoiceBoost.Value; _store.Save(); _ambience.Settings = _store.Current; };

        var lblBoostHint = new Label { Text = "scaled by how loud the bed actually is - no boost over a quiet one", AutoSize = true, ForeColor = AppTheme.TextMuted, Font = AppTheme.SmallFont };

        lblBoost.Location = new Point(
            Math.Max(330, UiLayout.After(_chkBoostVoice) + UiLayout.SectionGap),
            UiLayout.LabelY(y));

        _numVoiceBoost.Location = new Point(UiLayout.After(lblBoost), y);
        lblBoostHint.Location = new Point(_numVoiceBoost.Right + UiLayout.HintGap, UiLayout.LabelY(y));
        y = UiLayout.NextRow(y);

        // --- Status -------------------------------------------------------

        _lblStatus.Location = new Point(UiLayout.PageLeft, y + UiLayout.RowGap);
        _lblStatus.AutoSize = true;
        _lblStatus.MaximumSize = new Size(contentWidth, 0);
        _lblStatus.ForeColor = AppTheme.TextMuted;

        Size = new Size(980, y + 60);

        Controls.AddRange(new Control[]
        {
            lblIntro,
            _chkEnabled, lblMaster, _numMasterVolume, lblMasterSuffix,
            _monitor,
            lblLayers, lblLayersHint,
            lblPlay, _rdoSourceBoth, _rdoSourceSoundscape, _rdoSourceMine, _lblSource,
            _grid, btnAdd, btnEdit, btnDelete, btnPreview,
            lblReactions, _lblReactionsState, _chkReact,
            lblSurge, _numSurgeSeconds, lblFinish, _numFinishSeconds, lblSurgeSuffix,
            lblFadeUp, _numFadeUp, _numFadeDown, lblFadeHint,
            _chkFastLap, _chkLeadChange, _chkTrackRecord, _chkFinish,
            _chkLastLap, _chkCloseBattle, _chkStreak, _chkJumpStart,
            _chkPersonalBest, _chkLapped, lblOnceHint,
            lblDucking, _chkDuck, lblDuckTo, _numDuckTo, lblDuckHint,
            _chkBoostVoice, lblBoost, _numVoiceBoost, lblBoostHint,
            _lblStatus,
        });

        UpdateEnabledState();
    }


    /// <summary>
    /// Draws the live state: which intensity stage the ambience is at, a
    /// surge indicator, and a meter per layer showing its actual volume.
    ///
    /// Reads the service directly each paint rather than caching - the
    /// whole point is showing the real current value, including mid-fade.
    /// </summary>
    private void Monitor_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var bg = new SolidBrush(AppTheme.Surface);
        g.FillRectangle(bg, _monitor.ClientRectangle);

        using var border = new Pen(AppTheme.Border);
        g.DrawRectangle(border, 0, 0, _monitor.Width - 1, _monitor.Height - 1);

        if (!_ambience.IsRunning)
        {
            using var offBrush = new SolidBrush(AppTheme.TextMuted);
            g.DrawString("Ambience is not playing - enable it above, or press Preview to audition your layers.",
                AppTheme.BodyFont, offBrush, 10, 10);
            return;
        }

        var stages = new[]
        {
            (AmbienceService.Intensity.Idle, "IDLE"),
            (AmbienceService.Intensity.Anticipation, "STAGING"),
            (AmbienceService.Intensity.Racing, "RACING"),
            (AmbienceService.Intensity.Excited, "EXCITED"),
            (AmbienceService.Intensity.Peak, "PEAK"),
        };

        var effective = _ambience.EffectiveIntensity;
        var baseLevel = _ambience.CurrentIntensity;

        // Intensity ladder - the lit segment is where it is NOW, with the
        // base mood outlined separately so a surge reads as temporary
        // rather than looking like the race state itself changed.
        const int segW = 92;
        const int segH = 22;
        var x = 10;

        using var litBrush = new SolidBrush(AppTheme.Primary);
        using var dimBrush = new SolidBrush(AppTheme.Background);
        using var litText = new SolidBrush(AppTheme.TextDark);
        using var dimText = new SolidBrush(AppTheme.TextMuted);
        using var basePen = new Pen(AppTheme.Accent, 2f);
        using var centered = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        foreach (var (intensity, label) in stages)
        {
            var rect = new Rectangle(x, 10, segW, segH);
            var isCurrent = intensity == effective;

            g.FillRectangle(isCurrent ? litBrush : dimBrush, rect);

            if (intensity == baseLevel && !isCurrent)
            {
                g.DrawRectangle(basePen, rect);
            }

            g.DrawString(label, AppTheme.SmallFont, isCurrent ? litText : dimText, rect, centered);
            x += segW + 4;
        }

        if (_ambience.IsSurging)
        {
            using var surgeBrush = new SolidBrush(AppTheme.Accent);
            // What set it off, so a surge can be traced to the moment that
            // caused it - and the time left from the surge itself, which for
            // the finish celebration is not the Surge lasts setting.
            var reason = string.IsNullOrWhiteSpace(_ambience.SurgeReason) ? "Surge" : _ambience.SurgeReason;
            g.DrawString($"SURGE: {reason} ({_ambience.SurgePeak}) - settling back in {_ambience.SurgeSecondsLeft:0.0}s",
                AppTheme.SmallFont, surgeBrush, x + 8, 15);
        }
        else if (baseLevel != effective)
        {
            using var hintBrush = new SolidBrush(AppTheme.TextMuted);
            g.DrawString("(outline = base level)", AppTheme.SmallFont, hintBrush, x + 8, 15);
        }
        else if (!string.IsNullOrWhiteSpace(_ambience.SurgeReason) && _ambience.LastSurgeUtc > DateTime.MinValue)
        {
            // Settled - still worth knowing what last lifted the crowd.
            var ago = DateTime.UtcNow - _ambience.LastSurgeUtc;
            var when = ago.TotalMinutes >= 1 ? $"{(int)ago.TotalMinutes}m ago" : $"{(int)ago.TotalSeconds}s ago";
            using var lastBrush = new SolidBrush(AppTheme.TextMuted);
            g.DrawString($"Last surge: {_ambience.SurgeReason}, {when}", AppTheme.SmallFont, lastBrush, x + 8, 15);
        }

        // Per-layer meters showing real current volume, mid-fade included.
        var volumes = _ambience.CurrentLayerVolumes();
        var meterX = 10;
        const int meterW = 150;

        foreach (var layer in _store.Current.Layers.Where(l => l.Enabled))
        {
            if (!volumes.TryGetValue(layer.Key, out var volume))
            {
                continue;
            }

            var name = layer.DisplayName.Length > 18 ? layer.DisplayName[..18] : layer.DisplayName;
            g.DrawString(name, AppTheme.SmallFont, dimText, meterX, 44);

            var track = new Rectangle(meterX, 60, meterW, 8);
            g.FillRectangle(dimBrush, track);

            var filled = (int)(meterW * Math.Clamp(volume, 0f, 1f));
            if (filled > 0)
            {
                g.FillRectangle(litBrush, new Rectangle(meterX, 60, filled, 8));
            }

            g.DrawRectangle(border, track);
            g.DrawString($"{volume * 100:0}%", AppTheme.SmallFont, dimText, meterX + meterW + 6, 56);

            meterX += meterW + 60;

            // Only as many meters as fit - a long layer list shouldn't spill
            // out of the panel.
            if (meterX + meterW > _monitor.Width - 10)
            {
                break;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _store.Restored -= OnStoreRestored;
            _monitorTimer.Stop();
            _monitorTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private AmbienceLayer? Selected() =>
        _grid.CurrentRow?.Tag is string key
            ? _store.Current.Layers.FirstOrDefault(l => l.Key == key)
            : null;

    /// <summary>Set while RefreshAll moves the Enable tick to match the settings.</summary>
    private bool _syncingEnabled;

    /// <summary>
    /// Brings the page up to date after something else changed the
    /// settings - applying a soundscape adds its crowd layers and switches
    /// Ambience on.
    ///
    /// The Enable tick is moved quietly: ticking it by hand also STARTS the
    /// ambience, and a soundscape applied at a venue must not start noise in
    /// the room (see SoundscapeBackground). It plays when a session or this
    /// page brings it up, as it would after a restart with it switched on.
    /// </summary>
    public void RefreshAll()
    {
        if (_chkEnabled.Checked != _store.Current.Enabled)
        {
            _syncingEnabled = true;
            try
            {
                _chkEnabled.Checked = _store.Current.Enabled;
            }
            finally
            {
                _syncingEnabled = false;
            }

            UpdateEnabledState();
        }

        RefreshGrid();
    }

    private void UpdateEnabledState()
    {
        var on = _chkEnabled.Checked;
        _numMasterVolume.Enabled = on;
        _chkReact.Enabled = on;

        // The grid stays usable even when ambience is OFF. Disabling it
        // created a deadlock: you can't enable ambience without layers that
        // have real files, but you couldn't select a layer to add a file to
        // while ambience was off. Setting layers up before switching it on
        // is the normal order of operations.
        _grid.Enabled = true;

        // Surge settings only mean something when reactions are on at all.
        // Fades apply to ordinary state changes too, so they follow the
        // master switch rather than the reactions toggle.
        _numFadeUp.Enabled = on;
        _numFadeDown.Enabled = on;

        // Ducking applies to any announcement, not just reactions, so it
        // follows the master switch rather than the reactions toggle.
        _chkDuck.Enabled = on;
        _numDuckTo.Enabled = on && _chkDuck.Checked;
        _chkBoostVoice.Enabled = on;
        _numVoiceBoost.Enabled = on && _chkBoostVoice.Checked;

        var reacting = on && _chkReact.Checked;

        _lblReactionsState.Text = on
            ? reacting ? string.Empty : "- turn on \"React to what happens in the race\" below to use these"
            : "- greyed out because \"Enable ambience\" at the top of this page is off";
        _lblReactionsState.ForeColor = AppTheme.Accent;
        _numSurgeSeconds.Enabled = reacting;
        _numFinishSeconds.Enabled = reacting;
        _chkFastLap.Enabled = reacting;
        _chkLeadChange.Enabled = reacting;
        _chkTrackRecord.Enabled = reacting;
        _chkJumpStart.Enabled = reacting;
        _chkFinish.Enabled = reacting;
        _chkLastLap.Enabled = reacting;
        _chkCloseBattle.Enabled = reacting;
        _chkStreak.Enabled = reacting;
        _chkPersonalBest.Enabled = reacting;
        _chkLapped.Enabled = reacting;
    }

    private void RefreshGrid()
    {
        var selectedKey = _grid.CurrentRow?.Tag as string;
        _grid.Rows.Clear();

        foreach (var layer in _store.Current.Layers)
        {
            var fileText = string.IsNullOrWhiteSpace(layer.FilePath)
                ? "(no file set)"
                : File.Exists(layer.FilePath)
                    ? Path.GetFileName(layer.FilePath)
                    : $"MISSING: {Path.GetFileName(layer.FilePath)}";

            // Two different kinds of "not playing", and they are not the
            // same thing to fix: switched off is the layer's own setting,
            // silenced is the Play choice above the list. Saying which
            // saves someone editing a layer that was never the problem.
            var silenced = layer.Enabled && !layer.PlaysUnder(_store.Current.Source);

            var nameText = layer.DisplayName
                + (!layer.Enabled ? "  (off)" : silenced ? "  (not playing)" : string.Empty);

            var rowIndex = _grid.Rows.Add(
                nameText,
                layer.SourceLabel,
                fileText,
                $"{layer.IdleVolume * 100:0}%",
                $"{layer.AnticipationVolume * 100:0}%",
                $"{layer.RacingVolume * 100:0}%",
                $"{layer.ExcitedVolume * 100:0}%",
                $"{layer.PeakVolume * 100:0}%");

            _grid.Rows[rowIndex].Tag = layer.Key;

            // A missing file is the single most likely reason ambience is
            // silent, so it's called out rather than left to be discovered.
            if (!string.IsNullOrWhiteSpace(layer.FilePath) && !File.Exists(layer.FilePath))
            {
                _grid.Rows[rowIndex].Cells[2].Style.ForeColor = AppTheme.Danger;
            }
            else if (!layer.Enabled || silenced)
            {
                _grid.Rows[rowIndex].DefaultCellStyle.ForeColor = AppTheme.TextMuted;
            }

            if (selectedKey == layer.Key)
            {
                _grid.Rows[rowIndex].Selected = true;
                _grid.CurrentCell = _grid.Rows[rowIndex].Cells[0];
            }
        }

        RefreshSource();

        // Counts what would actually be HEARD, which is what the line under
        // the list is asked to report. Before the Play choice existed those
        // were the same set; now a layer can be ticked, have its file, and
        // still be on the side that isn't playing.
        var usable = _store.Current.PlayableLayers.Count(l => File.Exists(l.FilePath));
        var total = _store.Current.Layers.Count;

        // Says what's WRONG, not just a count. "0 of 3 ready" leaves someone
        // guessing whether the files are missing, the layers are switched
        // off, or the feature is broken.
        if (total == 0)
        {
            _lblStatus.Text = "No layers yet - add one and point it at a looping audio file.";
            _lblStatus.ForeColor = AppTheme.TextMuted;
        }
        else if (usable == 0)
        {
            var missingFiles = _store.Current.Layers.Count(l => !File.Exists(l.FilePath));

            // Checked before the other two: if the Play choice is what
            // silenced them, telling somebody their files are missing sends
            // them hunting for a file that is sitting right there.
            var silencedBySource = _store.Current.Source != AmbienceSource.Both
                && _store.Current.Layers.Any(l => l.Enabled && File.Exists(l.FilePath));

            _lblStatus.Text = silencedBySource
                ? $"0 of {Plural.Of(total, "layer")} playing - everything set up is on the other side of the Play choice above."
                : missingFiles > 0
                ? $"0 of {Plural.Of(total, "layer")} ready - {missingFiles} {(missingFiles == 1 ? "has" : "have")} no file set or the file is missing. "
                  + "Select a layer and press Edit to point it at an audio file."
                : $"0 of {Plural.Of(total, "layer")} ready - every layer is switched off. Edit a layer to enable it.";
            _lblStatus.ForeColor = AppTheme.Danger;
        }
        else
        {
            _lblStatus.Text = $"{usable} of {Plural.Of(total, "layer")} ready to play.";
            _lblStatus.ForeColor = usable < total ? AppTheme.Accent : AppTheme.Success;
        }
    }

    private void EditLayer(AmbienceLayer? existing)
    {
        // Taken BEFORE the dialog opens, so Undo has the layer as it was.
        // The dialog used to edit the stored layer itself and the snapshot
        // was taken after it closed - so it already held the edit, and Undo
        // put back exactly what it was meant to take away.
        //
        // Just this layer now, not the whole ambience file: a snapshot of
        // everything also undid every other layer and setting changed since.
        var before = existing?.Clone();

        // The dialog works on a copy (see AmbienceLayerForm), so Cancel
        // leaves the stored layer exactly as it was.
        using var form = new AmbienceLayerForm(existing, _ambience, _store);
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var key = form.Result.Key;
        var store = _store;
        var ambience = _ambience;
        _undo.Capture(existing is null ? $"Add ambience layer {form.Result.DisplayName}" : $"Edit ambience layer {form.Result.DisplayName}", () =>
        {
            var layers = store.Current.Layers;
            var at = layers.FindIndex(l => l.Key == key);

            if (before is null)
            {
                // An added layer comes out again.
                if (at >= 0)
                {
                    layers.RemoveAt(at);
                }
            }
            else if (at >= 0)
            {
                layers[at] = before.Clone();
            }
            else
            {
                // Deleted since: the edit's undo still brings the layer back
                // as it was before the edit.
                layers.Add(before.Clone());
            }

            AfterUndo(store, ambience);
        });

        var index = existing is null ? -1 : _store.Current.Layers.IndexOf(existing);

        if (index >= 0)
        {
            _store.Current.Layers[index] = form.Result;
        }
        else
        {
            // A new layer - or one whose original went while the dialog was
            // open, which is better kept than silently lost.
            _store.Current.Layers.Add(form.Result);
        }

        _store.Save();
        _ambience.Settings = _store.Current;
        ApplyRunState();
        RefreshGrid();
    }

    /// <summary>
    /// Switches which layers play, and restarts the bed if one is running so
    /// the change is heard rather than waited for.
    /// </summary>
    private void SetSource(AmbienceSource source)
    {
        if (_store.Current.Source == source)
        {
            return;
        }

        var before = _store.Current.Source;
        var store = _store;
        var ambience = _ambience;

        _undo.Capture($"Play {DescribeSource(before).ToLowerInvariant()} ambience", () =>
        {
            store.Current.Source = before;
            store.Save();
            ambience.Settings = store.Current;
            AfterUndo(store, ambience);
        });

        _store.Current.Source = source;
        _store.Save();

        // ApplyRunState restarts a running bed, which is what actually swaps
        // the layers over: they are opened when ambience starts, so a
        // silenced one has to be closed and the other side opened.
        ApplyRunState();
        RefreshGrid();
    }

    /// <summary>The Play choice in the words the explanation line uses.</summary>
    private static string DescribeSource(AmbienceSource source) => source switch
    {
        AmbienceSource.Soundscape => "Soundscape",
        AmbienceSource.Mine => "My",
        _ => "Both",
    };

    /// <summary>
    /// Brings the Play radios into line with the store and explains, in a
    /// sentence, what the current choice means for what is set up.
    ///
    /// The sentence matters more than it looks. "Soundscape ambience" with
    /// no soundscape beds present is silence, and a radio alone would not
    /// say so - the club would be left wondering whether the feature had
    /// broken. Music has the same four-way explanation for the same reason.
    /// </summary>
    private void RefreshSource()
    {
        var current = _store.Current;

        _syncingSource = true;
        try
        {
            _rdoSourceBoth.Checked = current.Source == AmbienceSource.Both;
            _rdoSourceSoundscape.Checked = current.Source == AmbienceSource.Soundscape;
            _rdoSourceMine.Checked = current.Source == AmbienceSource.Mine;
        }
        finally
        {
            _syncingSource = false;
        }

        bool Ready(Func<AmbienceLayer, bool> which) =>
            current.Layers.Any(l => which(l) && l.Enabled && File.Exists(l.FilePath));

        var soundscapeReady = Ready(l => l.FromSoundscape);
        var mineReady = Ready(l => !l.FromSoundscape);

        var (text, warn) = current.Source switch
        {
            AmbienceSource.Soundscape when !soundscapeReady =>
                ("No soundscape beds are set up, so nothing is playing. Apply a soundscape with a crowd, or switch to "
                 + "My ambience.", true),

            AmbienceSource.Soundscape =>
                ("Only the beds a soundscape supplied. Your own layers stay listed and keep their settings - they are "
                 + "just not playing.", false),

            AmbienceSource.Mine when !mineReady =>
                ("You haven't added a layer of your own yet, so nothing is playing. Add one below, or switch to "
                 + "Soundscape ambience.", true),

            AmbienceSource.Mine =>
                ("Only the layers you added. A soundscape's crowd beds stay listed, and applying another soundscape "
                 + "still replaces them - they are just not playing.", false),

            _ when !soundscapeReady && !mineReady =>
                ("Nothing is set up yet. Add a layer below, or apply a soundscape with a crowd.", false),

            _ =>
                ("Everything set up plays together - a soundscape's crowd under your own layers, which is what layers "
                 + "are for.", false),
        };

        _lblSource.Text = text;
        _lblSource.ForeColor = warn ? AppTheme.Warning : AppTheme.TextMuted;
    }

    private void DeleteLayer()
    {
        if (Selected() is not { } layer)
        {
            return;
        }

        // A soundscape's bed is not the club's to lose by accident: the only
        // way to get one back is to apply that soundscape again, which also
        // replaces the other one in the pair and undoes any levelling done
        // by hand. Said plainly rather than refused - it is still their
        // setup - but said before, not after. Built-in music tracks get the
        // same treatment on the Music page.
        var question = layer.FromSoundscape
            ? $"Delete the \"{layer.DisplayName}\" layer?\n\nThis bed came from a soundscape. Applying that soundscape "
              + "again is the only way to bring it back, and doing so also replaces the crowd roar that came with it.\n\n"
              + "To silence it without deleting it, set Play to \"My ambience\" instead."
            : $"Delete the \"{layer.DisplayName}\" layer?";

        if (AppDialog.Show(this, question, "Delete layer",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        // The layer and where it sat, so undo puts back just this one - not a
        // snapshot of the whole ambience file, which also undid every change
        // made since. Its sound file is never touched by a delete.
        var index = _store.Current.Layers.FindIndex(l => l.Key == layer.Key);
        var removed = layer.Clone();
        var store = _store;
        var ambience = _ambience;
        _undo.Capture($"Delete ambience layer {layer.DisplayName}", () =>
        {
            var layers = store.Current.Layers;

            // Already back (by key) - nothing to do but say so to the page.
            if (!layers.Any(l => l.Key == removed.Key))
            {
                layers.Insert(Math.Clamp(index, 0, layers.Count), removed.Clone());
            }

            AfterUndo(store, ambience);
        });

        _store.Current.Layers.RemoveAll(l => l.Key == layer.Key);
        _store.Save();
        _ambience.Settings = _store.Current;
        ApplyRunState();
        RefreshGrid();
    }

    /// <summary>
    /// Plays the ambience so it can be tuned by ear. Deliberately runs at
    /// "Racing" level - the middle of the range, and what you'll hear for
    /// most of an actual race.
    /// </summary>
    private void TogglePreview(Button btnPreview)
    {
        if (_ambience.IsRunning)
        {
            _ambience.Stop();
            btnPreview.Text = "Preview";
            return;
        }

        _ambience.Settings = _store.Current;

        // Bypasses the enabled check so files can be auditioned before
        // committing to switching ambience on.
        _ambience.Start(ignoreEnabledSetting: true);

        if (_ambience.IsRunning)
        {
            _ambience.SetIntensity(AmbienceService.Intensity.Racing);
            btnPreview.Text = "Stop Preview";
        }
        else
        {
            AppDialog.Show(this,
                "Nothing played. Check that at least one layer is enabled and its file exists.",
                "Nothing to preview", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    /// <summary>Starts or stops the service to match the enabled setting.</summary>
    /// <param name="startIfEnabled">
    /// Whether this should actually begin playback, or only refresh a
    /// session that's already running.
    ///
    /// Editing a layer shouldn't START the ambience - someone adjusting a
    /// volume while nothing is playing doesn't expect sound to begin. But
    /// if it IS playing, the change needs picking up, since mixer inputs
    /// are built at Start.
    /// </param>
    private void ApplyRunState(bool startIfEnabled = false)
    {
        _ambience.Settings = _store.Current;

        if (!_store.Current.Enabled)
        {
            _ambience.Stop();
            return;
        }

        var wasRunning = _ambience.IsRunning;

        if (wasRunning || startIfEnabled)
        {
            // Restart so newly added or edited layers are picked up.
            _ambience.Stop();
            _ambience.Start();
        }
    }
}

/// <summary>Adds or edits one ambience layer.</summary>
public sealed class AmbienceLayerForm : Form
{
    private readonly TextBox _txtName = new();
    private readonly TextBox _txtFile = new();
    private readonly CheckBox _chkEnabled = new();

    private readonly Dictionary<AmbienceService.Intensity, SliderControl> _sliders = new();
    private readonly Dictionary<AmbienceService.Intensity, Label> _valueLabels = new();
    private readonly Dictionary<AmbienceService.Intensity, int> _shownValues = new();

    /// <summary>
    /// Keeps each percentage label agreeing with its slider even when the
    /// trackbar's own events don't arrive - which is what was happening:
    /// thumbs dragged, numbers frozen at where they started.
    /// </summary>
    private readonly System.Windows.Forms.Timer _sliderSync = new() { Interval = 150 };
    private readonly CheckBox _chkLivePreview = new();
    private readonly CheckBox _chkAlwaysOn = new();
    private readonly NumericUpDown _numConstantVolume = new();
    private readonly Label _lblPreviewState = new();

    // Optional: supplied so sliders can be auditioned live. The form still
    // works without it, just silently - it's an editor first.
    private readonly AmbienceService? _ambience;
    private readonly AmbienceStore? _store;

    /// <summary>
    /// The edited layer. Always a COPY of the one passed in - the caller puts
    /// it in place of the original on OK. Editing the stored layer directly
    /// meant a Cancel after a preview still kept the previewed file path.
    /// </summary>
    public AmbienceLayer Result { get; }

    /// <summary>Whether the ambience was already playing when the dialog opened, so closing it can put things back.</summary>
    private readonly bool _ambienceWasRunning;

    /// <summary>True when the dialog's own preview started the ambience, so only that gets stopped on close.</summary>
    private bool _previewStartedAmbience;

    /// <summary>True once a preview has soloed this layer - the other layers are muted until the mix is restored.</summary>
    private bool _previewTouched;

    public AmbienceLayerForm(AmbienceLayer? existing, AmbienceService? ambience = null, AmbienceStore? store = null)
    {
        Result = existing?.Clone() ?? new AmbienceLayer();
        _ambience = ambience;
        _store = store;
        _ambienceWasRunning = ambience?.IsRunning ?? false;

        Text = existing is null ? "Add Ambience Layer" : $"Edit {Result.DisplayName}";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        BuildLayout();
        AppTheme.ApplyRecursively(this);
    }

    private void BuildLayout()
    {
        // Measured, not hand-picked pixels. This dialog predates the app's
        // font scale, and its original fixed grid showed it: the file hint
        // wrapped down over the checkbox row, the Level box fell off the
        // right edge, and the percentage labels were 42px boxes that
        // clipped "100%". Every row now sits below the measured bottom of
        // the one before it, the way RaceFormatForm lays itself out.
        var left = UiLayout.PageLeft;
        const int contentWidth = 640;
        var right = left + contentWidth;

        var lblName = new Label { Text = "Name:", AutoSize = true, Font = AppTheme.BodyFont };
        var lblFile = new Label { Text = "Audio file:", AutoSize = true, Font = AppTheme.BodyFont };

        // Both captions in this column, so the two fields line up.
        var fieldLeft = left + Math.Max(lblName.PreferredWidth, lblFile.PreferredWidth) + UiLayout.LabelGap;

        var y = UiLayout.PageTop;

        _txtName.Font = AppTheme.BodyFont;
        _txtName.Location = new Point(fieldLeft, y);
        _txtName.Width = right - fieldLeft;
        _txtName.Text = Result.Name;
        _txtName.PlaceholderText = "Crowd, Track hum, Pit lane...";
        lblName.Location = new Point(left, UiLayout.CentreOn(lblName, y, _txtName.Height));

        y = UiLayout.Below(_txtName, UiLayout.RowGap);

        var btnBrowse = new Button { Text = "Browse..." };
        btnBrowse.Width = UiLayout.WidthFor(btnBrowse);
        btnBrowse.Height = UiLayout.ButtonHeight;

        _txtFile.Font = AppTheme.BodyFont;
        _txtFile.Location = new Point(fieldLeft, y);
        _txtFile.Width = right - fieldLeft - btnBrowse.Width - UiLayout.ButtonGap;
        _txtFile.Text = Result.FilePath;
        lblFile.Location = new Point(left, UiLayout.CentreOn(lblFile, y, _txtFile.Height));
        btnBrowse.Location = new Point(right - btnBrowse.Width, y + (_txtFile.Height - btnBrowse.Height) / 2);
        btnBrowse.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Choose an ambience loop",
                Filter = "Audio files (*.wav;*.mp3)|*.wav;*.mp3|All files (*.*)|*.*",
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _txtFile.Text = dialog.FileName;
            }
        };

        y = Math.Max(UiLayout.Below(_txtFile, UiLayout.HintGap), UiLayout.Below(btnBrowse, UiLayout.HintGap));

        var lblFileHint = new Label
        {
            Text = "A short clip that loops cleanly works best - it repeats continuously while the ambience is running.",
            AutoSize = true,
            MaximumSize = new Size(right - fieldLeft, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
            Location = new Point(fieldLeft, y),
        };

        y = UiLayout.Below(lblFileHint, UiLayout.RowGap + 4);

        _chkEnabled.Text = "Layer enabled";
        _chkEnabled.AutoSize = true;
        _chkEnabled.Font = AppTheme.BodyFont;
        _chkEnabled.Checked = Result.Enabled;
        _chkEnabled.Location = new Point(left, y);

        _chkAlwaysOn.Text = "Always on - ignore race state";
        _chkAlwaysOn.AutoSize = true;
        _chkAlwaysOn.Font = AppTheme.BodyFont;
        _chkAlwaysOn.Checked = Result.AlwaysOn;
        _chkAlwaysOn.CheckedChanged += (_, _) => UpdateAlwaysOnState();
        _chkAlwaysOn.Location = new Point(left + _chkEnabled.PreferredSize.Width + UiLayout.SectionGap, y);

        _numConstantVolume.Font = AppTheme.BodyFont;
        _numConstantVolume.Width = UiLayout.NumericWidth;
        _numConstantVolume.Minimum = 0;
        _numConstantVolume.Maximum = 100;
        _numConstantVolume.Increment = 5;
        _numConstantVolume.Value = (decimal)Math.Clamp(Result.ConstantVolume * 100f, 0, 100);
        _numConstantVolume.Location = new Point(right - UiLayout.NumericWidth, y - 2);

        var lblConstant = new Label { Text = "Level:", AutoSize = true, Font = AppTheme.BodyFont };
        lblConstant.Location = new Point(_numConstantVolume.Left - lblConstant.PreferredWidth - UiLayout.LabelGap, y);

        y = Math.Max(UiLayout.Below(_chkAlwaysOn, UiLayout.HintGap), UiLayout.Below(_numConstantVolume, UiLayout.HintGap));

        var lblAlwaysOnHint = new Label
        {
            Text = "For a background theme or your own uploaded track - plays at one level throughout, "
                 + "including between races. The per-state volumes below don't apply to it.",
            AutoSize = true,
            MaximumSize = new Size(contentWidth, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
            Location = new Point(left, y),
        };

        y = UiLayout.SectionAfter(UiLayout.Below(lblAlwaysOnHint, 0));

        var lblVolumes = new Label
        {
            Text = "Volume at each race state",
            AutoSize = true,
            Font = AppTheme.BoldFont,
            Location = new Point(left, y),
        };

        y = UiLayout.Below(lblVolumes, UiLayout.HintGap);

        var lblVolumeHint = new Label
        {
            Text = "This is what makes a layer react. A crowd might be quiet at idle and loud at peak; "
                 + "track noise might be the opposite, loud while racing and silent between.",
            AutoSize = true,
            MaximumSize = new Size(contentWidth, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
            Location = new Point(left, y),
        };

        y = UiLayout.Below(lblVolumeHint, UiLayout.RowGap);

        _chkLivePreview.Text = "Play while I drag";
        _chkLivePreview.AutoSize = true;
        _chkLivePreview.Font = AppTheme.BodyFont;
        _chkLivePreview.Checked = true;
        _chkLivePreview.Location = new Point(left, y);
        _chkLivePreview.CheckedChanged += (_, _) =>
        {
            if (!_chkLivePreview.Checked)
            {
                EndPreview();
                _lblPreviewState.Text = string.Empty;
            }
        };

        _lblPreviewState.AutoSize = true;
        _lblPreviewState.Font = AppTheme.SmallFont;
        _lblPreviewState.ForeColor = AppTheme.TextMuted;
        _lblPreviewState.Location = new Point(left + _chkLivePreview.PreferredSize.Width + UiLayout.SectionGap, y + 2);
        _lblPreviewState.MaximumSize = new Size(Math.Max(120, right - _lblPreviewState.Left), 0);

        y = UiLayout.Below(_chkLivePreview, UiLayout.RowGap + 2);

        var rows = new (AmbienceService.Intensity Intensity, string Label, string Hint)[]
        {
            (AmbienceService.Intensity.Idle, "Idle", "between races"),
            (AmbienceService.Intensity.Anticipation, "Staging", "staged and counting down"),
            (AmbienceService.Intensity.Racing, "Racing", "normal green-flag running"),
            (AmbienceService.Intensity.Excited, "Excited", "close battle, lead change"),
            (AmbienceService.Intensity.Peak, "Peak", "finish, record, big moment"),
        };

        // Three measured columns: captions to the widest of them, the
        // value sized to "100%" so it can never clip, and the hint taking
        // the right end.
        var captionWidth = 0;
        foreach (var (_, label, _) in rows)
        {
            captionWidth = Math.Max(captionWidth, TextRenderer.MeasureText($"{label}:", AppTheme.BodyFont).Width);
        }

        var valueWidth = TextRenderer.MeasureText("100%", AppTheme.BoldFont).Width + 6;
        var hintWidth = Math.Min(230, contentWidth / 3);
        var hintLeft = right - hintWidth;
        var valueLeft = hintLeft - valueWidth - UiLayout.LabelGap;
        var sliderLeft = left + captionWidth + UiLayout.LabelGap;

        foreach (var (intensity, label, hint) in rows)
        {
            var caption = new Label
            {
                Text = $"{label}:",
                AutoSize = true,
                Font = AppTheme.BodyFont,
                Location = new Point(left, y + 8),
            };

            // SliderControl, not TrackBar: a skinned TrackBar drew its handle
            // somewhere other than where it could be grabbed, so dragging
            // took several tries. See SliderControl.
            var slider = new SliderControl
            {
                Location = new Point(sliderLeft, y),
                Height = 40,
                Width = Math.Max(140, valueLeft - UiLayout.LabelGap - sliderLeft),
                Minimum = 0,
                Maximum = 100,
                SmallChange = 1,
                LargeChange = 10,
                Value = (int)Math.Clamp(Result.VolumeFor(intensity) * 100f, 0, 100),
            };

            var lblValue = new Label
            {
                Text = $"{slider.Value}%",
                AutoSize = true,
                Font = AppTheme.BoldFont,
                Location = new Point(valueLeft, y + 8),
            };

            var lblHint = new Label
            {
                Text = hint,
                AutoSize = true,
                MaximumSize = new Size(hintWidth, 0),
                ForeColor = AppTheme.TextMuted,
                Font = AppTheme.SmallFont,
                Location = new Point(hintLeft, y + 8),
            };

            var captured = intensity;

            // Every change - drag, click, wheel, keyboard - raises
            // ValueChanged; the idle sync timer stays as a backstop.
            slider.ValueChanged += (_, _) => SyncSliderRow(captured, preview: true);

            _sliders[intensity] = slider;
            _valueLabels[intensity] = lblValue;
            _shownValues[intensity] = slider.Value;

            Controls.Add(caption);
            Controls.Add(slider);
            Controls.Add(lblValue);
            Controls.Add(lblHint);

            y += Math.Max(UiLayout.HeightOf(slider), UiLayout.HeightOf(lblHint) + 8) + 6;
        }

        var btnSave = new Button { Text = "Save" };
        btnSave.Click += BtnSave_Click;

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // Bottom-right, Cancel outermost - same as the app's other dialogs.
        // ButtonRow returns the x past the trailing gap, so the gap is
        // taken back off to land Cancel flush on the right margin.
        var buttonsRight = UiLayout.ButtonRow(0, y + UiLayout.RowGap, btnSave, btnCancel) - UiLayout.ButtonGap;
        var shift = right - buttonsRight;
        btnSave.Left += shift;
        btnCancel.Left += shift;

        Controls.AddRange(new Control[]
        {
            lblName, _txtName, lblFile, _txtFile, btnBrowse, lblFileHint, _chkEnabled,
            _chkAlwaysOn, lblConstant, _numConstantVolume, lblAlwaysOnHint,
            lblVolumes, lblVolumeHint, _chkLivePreview, _lblPreviewState, btnSave, btnCancel,
        });

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        ClientSize = new Size(right + UiLayout.PageRight, UiLayout.Below(btnSave, UiLayout.PageTop));

        _sliderSync.Tick += (_, _) =>
        {
            foreach (var intensity in _sliders.Keys)
            {
                SyncSliderRow(intensity, preview: true);
            }
        };
        _sliderSync.Start();

        // After the sliders exist, or there would be nothing to grey.
        UpdateAlwaysOnState();
    }

    /// <summary>
    /// Puts one slider's current value in its label and, when the value
    /// actually moved, auditions it. All the paths that can change a
    /// slider funnel through here, so a drag, a click, an arrow key and
    /// the sync timer all agree on what the number says.
    /// </summary>
    private void SyncSliderRow(AmbienceService.Intensity intensity, bool preview)
    {
        var slider = _sliders[intensity];

        if (_shownValues.TryGetValue(intensity, out var shown) && shown == slider.Value)
        {
            return;
        }

        _shownValues[intensity] = slider.Value;
        _valueLabels[intensity].Text = $"{slider.Value}%";

        if (preview)
        {
            PreviewStage(intensity, slider.Value / 100f);
        }
    }


    /// <summary>
    /// Plays this layer alone at the dragged volume so the value can be set
    /// by ear. Best-effort: if ambience isn't playable right now the slider
    /// still works, it's just silent.
    /// </summary>

    /// <summary>
    /// Greys the per-state sliders when the layer is always-on - they have
    /// no effect in that mode, and leaving them live would imply otherwise.
    /// </summary>
    private void UpdateAlwaysOnState()
    {
        var alwaysOn = _chkAlwaysOn.Checked;
        _numConstantVolume.Enabled = alwaysOn;

        foreach (var slider in _sliders.Values)
        {
            slider.Enabled = !alwaysOn;
        }
    }

    private void PreviewStage(AmbienceService.Intensity intensity, float volume)
    {
        if (_ambience is null || _store is null || !_chkLivePreview.Checked)
        {
            return;
        }

        // Capture the edits so far, so the preview uses the file currently
        // typed in the box rather than whatever was saved last.
        Result.FilePath = _txtFile.Text.Trim();

        if (string.IsNullOrWhiteSpace(Result.FilePath) || !File.Exists(Result.FilePath))
        {
            _lblPreviewState.Text = "Set a file above to hear these.";
            _lblPreviewState.ForeColor = AppTheme.TextMuted;
            return;
        }

        if (!_ambience.IsRunning)
        {
            _ambience.Settings = _store.Current;
            _ambience.Start(ignoreEnabledSetting: true);

            // Nothing saved would play - the first layer a club ever adds,
            // or every other layer switched off or missing its file. Start
            // then refuses (it only opens the saved layers), and this layer
            // could never be heard while it was being set up: "Couldn't
            // play that file" for a file that plays fine. Started on this
            // layer alone instead, then handed the saved settings back, so
            // the fades and the duck still go by what is saved.
            if (!_ambience.IsRunning)
            {
                var solo = Result.Clone();
                solo.Enabled = true;

                _ambience.Settings = new AmbienceSettings
                {
                    MasterVolume = _store.Current.MasterVolume,
                    Layers = new List<AmbienceLayer> { solo },
                };

                _ambience.Start(ignoreEnabledSetting: true);
                _ambience.Settings = _store.Current;
            }

            // Remembered so closing stops only what this dialog started.
            _previewStartedAmbience = _ambience.IsRunning;
        }

        // Reloaded when the file in the box differs from the one already
        // playing for this layer - otherwise picking a new file kept
        // previewing the old one.
        if (!_ambience.EnsureLayerLoaded(Result, reloadIfFileChanged: true))
        {
            _lblPreviewState.Text = "Couldn't play that file.";
            _lblPreviewState.ForeColor = AppTheme.Danger;
            return;
        }

        _ambience.PreviewLayerVolume(Result.Key, volume);
        _previewTouched = true;

        _lblPreviewState.Text = $"Playing \u2013 {intensity} at {volume * 100:0}%";
        _lblPreviewState.ForeColor = AppTheme.Success;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _sliderSync.Stop();
        _sliderSync.Dispose();

        // End the solo preview so the edit doesn't leave one layer blaring
        // and the rest muted once the dialog is gone.
        EndPreview();

        base.OnFormClosed(e);
    }

    /// <summary>
    /// Undoes whatever the live preview did to the ambience, and nothing
    /// more.
    ///
    /// This used to stop the ambience outright whenever it was playing -
    /// including when it had been playing before the dialog opened, mid
    /// race or from the page's own Preview button - and never started it
    /// again. Now: if the preview started it, it stops; if it was already
    /// playing and the preview soloed this layer, it is restarted from the
    /// saved settings so every layer comes back at its proper level; if the
    /// preview never played, it is left alone.
    /// </summary>
    private void EndPreview()
    {
        if (_ambience is null)
        {
            return;
        }

        if (_previewStartedAmbience)
        {
            _ambience.Stop();
        }
        else if (_previewTouched && _ambienceWasRunning && _ambience.IsRunning)
        {
            // A restart rather than a level refresh: the preview may have
            // loaded a layer (or a file) that isn't in the saved settings,
            // and only rebuilding the mix takes that back out. Started past
            // the enabled setting because it WAS playing - whatever let it
            // play before the dialog still applies.
            _ambience.Stop();

            if (_store is not null)
            {
                _ambience.Settings = _store.Current;
            }

            _ambience.Start(ignoreEnabledSetting: true);
        }

        _previewStartedAmbience = false;
        _previewTouched = false;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtName.Text))
        {
            AppDialog.Show(this, "Give the layer a name.", "Missing name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var path = _txtFile.Text.Trim();
        if (!string.IsNullOrWhiteSpace(path) && !File.Exists(path))
        {
            // Warn rather than block - the file might be on a drive that
            // isn't connected right now, and the grid flags it anyway.
            if (AppDialog.Show(this,
                    $"That file doesn't exist right now:\r\n\r\n{path}\r\n\r\nSave the layer anyway?",
                    "File not found", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
        }

        Result.Name = _txtName.Text.Trim();
        Result.FilePath = path;
        Result.Enabled = _chkEnabled.Checked;
        Result.AlwaysOn = _chkAlwaysOn.Checked;
        Result.ConstantVolume = (float)_numConstantVolume.Value / 100f;

        foreach (var (intensity, slider) in _sliders)
        {
            Result.SetVolumeFor(intensity, slider.Value / 100f);
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
