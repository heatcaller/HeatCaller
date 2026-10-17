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

    private readonly CheckBox _chkEnabled = new ThemedCheckBox();

    // Which layers play. Music has the same idea as a pair; this is three,
    // because ambience layers mix rather than replace one another. See
    // AmbienceSource.
    private readonly RadioButton _rdoSourceBoth = new ThemedRadioButton();
    private readonly RadioButton _rdoSourceSoundscape = new ThemedRadioButton();
    private readonly RadioButton _rdoSourceMine = new ThemedRadioButton();
    private readonly SteadyLabel _lblSource = new();

    /// <summary>Set while the radios are being brought into line with the store, so seeding them isn't read as a click.</summary>
    private bool _syncingSource;

    private readonly DataGridView _grid = new();
    private readonly CheckBox _chkReact = new ThemedCheckBox();
    private readonly NumericUpDown _numSurgeSeconds = new();
    private readonly NumericUpDown _numFinishSeconds = new();
    private readonly CheckBox _chkFastLap = new ThemedCheckBox();
    private readonly CheckBox _chkLeadChange = new ThemedCheckBox();
    private readonly CheckBox _chkTrackRecord = new ThemedCheckBox();
    private readonly CheckBox _chkJumpStart = new ThemedCheckBox();
    private readonly CheckBox _chkFinish = new ThemedCheckBox();
    private readonly CheckBox _chkLastLap = new ThemedCheckBox();
    private readonly CheckBox _chkCloseBattle = new ThemedCheckBox();
    private readonly CheckBox _chkStreak = new ThemedCheckBox();
    private readonly CheckBox _chkPersonalBest = new ThemedCheckBox();
    private readonly CheckBox _chkLapped = new ThemedCheckBox();
    private readonly NumericUpDown _numFadeUp = new();
    private readonly NumericUpDown _numFadeDown = new();
    private readonly SteadyLabel _lblStatus = new();
    private readonly SteadyLabel _lblReactionsState = new();
    // Plain Panel plus a manual double-buffer style: Panel.DoubleBuffered
    // is protected, so it can't be set from an object initializer.
    private readonly Panel _monitor = new();
    private readonly System.Windows.Forms.Timer _monitorTimer = new() { Interval = 100 };

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The live monitor's height at the normal text size - what Monitor_Paint draws into.</summary>
    private const int MonitorHeight = 78;

    /// <summary>The fewest layers the list shows under its headings before the page scrolls instead.</summary>
    private const int GridLeastRows = 5;

    /// <summary>The most surge triggers along one line: two rows of five, or fewer to a line when the card is too narrow for five.</summary>
    private const int TriggerColumns = 5;

    /// <summary>
    /// A check box's box and the gap after it, on top of its caption - the
    /// room LayoutAudit counts for one that does not size itself: 26 at the
    /// normal text size, and the app's tick box grows with the text
    /// (TickBox.Side). See CheckBoxSize.
    /// </summary>
    private static int CheckBoxChrome => 26 - 13 + TickBox.Side;

    // What the line above the React switch says - see UpdateEnabledState. Both
    // are measured for the room it keeps (SteadyLabel). A sentence of its own
    // now it is the card's first line; it began with a dash when it carried
    // on from the heading beside it.
    private const string ReactionsNeedReact = "Turn on \"React to what happens in the race\" below to use these.";
    private const string ReactionsNeedEnable = "Grayed out because \"Enable ambience\" at the top of this page is off.";

    // What the line under the Play choice says - see RefreshSource. All of
    // them are measured for the room it keeps (SteadyLabel).
    private const string SoundscapeNothingPlaying =
        "No soundscape beds are set up, so nothing is playing. Apply a soundscape with a crowd, or switch to "
        + "My ambience.";

    private const string SoundscapeOnly =
        "Only the beds a soundscape supplied. Your own layers stay listed and keep their settings - they are "
        + "just not playing.";

    private const string MineNothingPlaying =
        "You haven't added a layer of your own yet, so nothing is playing. Add one below, or switch to "
        + "Soundscape ambience.";

    private const string MineOnly =
        "Only the layers you added. A soundscape's crowd beds stay listed, and applying another soundscape "
        + "still replaces them - they are just not playing.";

    private const string NothingSetUp = "Nothing is set up yet. Add a layer below, or apply a soundscape with a crowd.";

    private const string EverythingPlays =
        "Everything set up plays together - a soundscape's crowd under your own layers, which is what layers "
        + "are for.";

    /// <summary>What the line under the list says with no layers at all - see RefreshGrid.</summary>
    private const string NoLayersStatus = "No layers yet - add one and point it at a looping audio file.";

    /// <summary>
    /// The longest things the line under the list can say, counts of two
    /// digits standing in for the real ones, so it keeps room for the
    /// longest and rewording it never moves the list above it or the cards
    /// below (SteadyLabel). The wordings themselves are RefreshGrid's.
    /// </summary>
    private static readonly string[] StatusWordings =
    {
        NoLayersStatus,
        "0 of 99 layers playing - everything set up is on the other side of the Play choice above.",
        "0 of 99 layers ready - 99 have no file set or the file is missing. Select a layer and press Edit to point it at an audio file.",
        "0 of 99 layers ready - every layer is switched off. Edit a layer to enable it.",
        "99 of 99 layers ready to play.",
    };

    public AmbienceControl(AmbienceStore store, AmbienceService ambience, UndoService undo)
    {
        _store = store;
        _ambience = ambience;
        _undo = undo;

        Dock = DockStyle.Fill;
        ForeColor = AppTheme.TextDark;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize. In its tab, the page would otherwise wear the
        // tab row's bold face.
        //
        // No design size and no MinimumSize: the 900x560 floor was honoured
        // over the size the page was given. A short window scrolls the page
        // instead (FillPage), which also does away with the page's own
        // scrolling.
        _textSize.Wear(this, () => AppTheme.BodyFont);

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
            // Restart rather than Stop and Start, so an undo pressed while
            // the crowd is fading out at the end of a session doesn't bring
            // it back with nothing left to stop it (see Restart).
            ambience.Restart(ignoreEnabledSetting: true);
        }

        store.NotifyRestored();
    }

    /// <summary>
    /// Builds the page: what ambience is; the Enable switch; the live monitor
    /// across the page; then two cards (the overall level and the ducking
    /// moved to the Mixer on 17 Oct 2026).
    /// Layers: which layers play and what that means, the list of layers
    /// taking whatever height the page has left, its buttons, and a line
    /// saying what will be heard. Reactions: why they may be greyed out, the
    /// React switch, how long a surge, the finish celebration and the fades
    /// take, the ten triggers in columns, and the once-per-race note. On the
    /// layout pieces (LayoutPieces.cs), so nothing is placed by hand.
    ///
    /// It used to be placed in pixels against a 980px page: every row at a y
    /// worked out from the one before, each level box after a caption
    /// measured once at build, the triggers in four columns a quarter of the
    /// page wide, the list 170px and the monitor 78px tall at every text
    /// size, and a 900x560 floor the page kept however small its window.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Continuous crowd and track noise that keeps running under the announcer. "
                 + "For your own layers, short looping clips of 10-30 seconds work best.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        // --- Master ------------------------------------------------------

        _chkEnabled.Text = "Enable ambience";
        _chkEnabled.AutoSize = true;
        _textSize.Wear(_chkEnabled, () => AppTheme.BoldFont);
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

            // Switching the master switch off is the one place the page
            // stops the crowd outright, whatever started it - that is what
            // the operator just asked for. Every other edit only refreshes a
            // bed that is already playing. See ApplyRunState.
            if (_chkEnabled.Checked)
            {
                ApplyRunState(startIfEnabled: true);
            }
            else
            {
                _ambience.Settings = _store.Current;
                _ambience.Stop();
            }

            UpdateEnabledState();
        };

        // The switch on its own line (17 Oct 2026). The overall level that
        // sat beside it is the Mixer's Ambience fader - the one place every
        // level is set now - and the same stored value.
        var master = new FieldRow();

        master.Add(null, _chkEnabled);

        // --- Live monitor -------------------------------------------------
        //
        // Across the page, and as tall as it was at the normal text size at
        // the size in use (SizeFields). What is drawn in it is Monitor_Paint's.

        _monitor.Paint += Monitor_Paint;

        typeof(Panel).GetProperty("DoubleBuffered",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(_monitor, true);

        // --- Layers -------------------------------------------------------

        var lblLayersHint = new Label
        {
            Text = "Each plays continuously at a volume that follows what's happening on track.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblLayersHint, () => AppTheme.SmallFont);

        // --- Which layers play ---
        //
        // The same row Music has, with one more choice on it. Music's is a
        // pair because a playlist replaces a playlist; these mix, so Both
        // is a real answer and it is the default - a club that never comes
        // here hears exactly what it heard before.
        _rdoSourceBoth.Text = "Both";
        _rdoSourceSoundscape.Text = "Soundscape ambience";
        _rdoSourceMine.Text = "My ambience";

        foreach (var radio in new[] { _rdoSourceBoth, _rdoSourceSoundscape, _rdoSourceMine })
        {
            radio.AutoSize = true;

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

        // What the choice means for what is set up, under it - as tall as
        // the longest thing it says, so switching the choice never moves the
        // list under it (SteadyLabel).
        _lblSource.Wordings = new[]
        {
            SoundscapeNothingPlaying, SoundscapeOnly, MineNothingPlaying, MineOnly, NothingSetUp, EverythingPlays,
        };

        // The three radios in one row of their own, which also makes them
        // one group: a radio clears the others in the same container.
        var play = new FieldGrid();

        play.AddRow("Play:", FieldGrid.Inline(_rdoSourceBoth, _rdoSourceSoundscape, _rdoSourceMine));
        play.AddHint(_lblSource);

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

        // Every heading whole, at the text size in use - see Pieces.KeepHeadingsWhole.
        _textSize.Keep(() => Pieces.KeepHeadingsWhole(_grid));

        var btnAdd = new Button { Text = "Add Layer..." };
        btnAdd.Click += (_, _) => EditLayer(null);

        var btnEdit = new Button { Text = "Edit..." };
        btnEdit.Click += (_, _) => EditLayer(Selected());

        var btnDelete = new Button { Text = "Delete" };
        btnDelete.Click += (_, _) => DeleteLayer();

        var btnPreview = new Button { Text = "Preview" };
        btnPreview.Click += (_, _) => TogglePreview(btnPreview);

        // What will actually be heard, under the list it counts - wrapping
        // rather than cut short, and as tall as the longest thing it says.
        _lblStatus.ForeColor = AppTheme.TextMuted;
        _lblStatus.Wordings = StatusWordings;

        var layers = new Section("Layers");

        layers.Add(lblLayersHint);
        layers.Add(play);
        layers.AddFill(_grid, () => _grid.ColumnHeadersHeight + GridLeastRows * _grid.RowTemplate.Height + 2);
        layers.Add(new ButtonBar().With(btnAdd, btnEdit, btnDelete, btnPreview));
        layers.Add(_lblStatus, gapBelow: 0);

        // --- Reactions ----------------------------------------------------

        // Why the card is greyed out, when it is. It sat beside the heading;
        // it is the card's first line now, and keeps the room for the longer
        // of its two wordings even when it says nothing - so ticking React,
        // which empties it, never moves the box being ticked (SteadyLabel).
        _lblReactionsState.Wordings = new[] { ReactionsNeedReact, ReactionsNeedEnable };
        _textSize.Wear(_lblReactionsState, () => AppTheme.SmallFont);

        _chkReact.Text = "React to what happens in the race";
        _chkReact.AutoSize = true;
        _chkReact.Checked = _store.Current.ReactToEvents;
        _chkReact.CheckedChanged += (_, _) =>
        {
            _store.Current.ReactToEvents = _chkReact.Checked;
            _store.Save();
            _ambience.Settings = _store.Current;
            UpdateEnabledState();
        };

        // The timings: surge, finish celebration and fades together, since
        // they're all "how long does something take". One caption column, so
        // the three boxes line up, each with its unit after it.
        _numSurgeSeconds.Minimum = 1;
        _numSurgeSeconds.Maximum = 30;
        _numSurgeSeconds.Value = Math.Clamp(_store.Current.SurgeSeconds, 1, 30);
        _numSurgeSeconds.ValueChanged += (_, _) => { _store.Current.SurgeSeconds = (int)_numSurgeSeconds.Value; _store.Save(); _ambience.Settings = _store.Current; };

        _numFinishSeconds.Minimum = 0;
        _numFinishSeconds.Maximum = 300;
        _numFinishSeconds.Increment = 5;
        _numFinishSeconds.Value = Math.Clamp(_store.Current.FinishCelebrationSeconds, 0, 300);
        _numFinishSeconds.ValueChanged += (_, _) => { _store.Current.FinishCelebrationSeconds = (int)_numFinishSeconds.Value; _store.Save(); _ambience.Settings = _store.Current; };

        var lblSurgeSuffix = new Label { Text = "s  - then it settles back to idle", ForeColor = AppTheme.TextMuted };
        _textSize.Wear(lblSurgeSuffix, () => AppTheme.SmallFont);

        _numFadeUp.DecimalPlaces = 1;
        _numFadeUp.Increment = 0.5m;
        _numFadeUp.Minimum = 0.1m;
        _numFadeUp.Maximum = 30m;
        _numFadeUp.Value = (decimal)Math.Clamp(_store.Current.FadeUpSeconds, 0.1, 30);
        _numFadeUp.ValueChanged += (_, _) => { _store.Current.FadeUpSeconds = (double)_numFadeUp.Value; _store.Save(); _ambience.Settings = _store.Current; };

        _numFadeDown.DecimalPlaces = 1;
        _numFadeDown.Increment = 0.5m;
        _numFadeDown.Minimum = 0.1m;
        _numFadeDown.Maximum = 30m;
        _numFadeDown.Value = (decimal)Math.Clamp(_store.Current.FadeDownSeconds, 0.1, 30);
        _numFadeDown.ValueChanged += (_, _) => { _store.Current.FadeDownSeconds = (double)_numFadeDown.Value; _store.Save(); _ambience.Settings = _store.Current; };

        var lblFadeHint = new Label { Text = "seconds - a slower fall than rise sounds most natural", ForeColor = AppTheme.TextMuted };
        _textSize.Wear(lblFadeHint, () => AppTheme.SmallFont);

        var timing = new FieldGrid();

        timing.AddRow("Surge lasts:", FieldGrid.Inline(_numSurgeSeconds, new Label { Text = "s" }));
        timing.AddRow("Finish celebration:", FieldGrid.Inline(_numFinishSeconds, lblSurgeSuffix));
        timing.AddRow("Fade up / down:", FieldGrid.Inline(_numFadeUp, _numFadeDown, lblFadeHint));

        // The triggers in columns: every box as wide as the widest needs
        // (SizeTriggers), so the row wraps into columns that line up - five
        // to a line where the card has room, fewer where it has not.
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

        var triggerBar = new ButtonBar();

        for (var i = 0; i < triggers.Length; i++)
        {
            var (box, text, get, set) = triggers[i];

            box.Text = text;

            // Sized by SizeTriggers, all alike.
            box.AutoSize = false;
            box.Checked = get();

            var setter = set;
            var source = box;
            box.CheckedChanged += (_, _) => { setter(source.Checked); _store.Save(); _ambience.Settings = _store.Current; };

            triggerBar.With(box);
        }

        var lblOnceHint = new Label
        {
            Text = "Close battle and lapping fire once per race - a long scrap would otherwise keep the level pinned high.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblOnceHint, () => AppTheme.SmallFont);

        var reactions = new Section("Reactions");

        reactions.Add(_lblReactionsState, gapBelow: UiLayout.HintGap);
        reactions.Add(_chkReact, stretch: false);
        reactions.Add(timing);
        reactions.Add(triggerBar, stretch: false, gapBelow: UiLayout.HintGap);
        reactions.Add(lblOnceHint, gapBelow: 0);

        // --- Ducking ------------------------------------------------------
        //
        // On the Mixer since 17 Oct 2026, with every other duck: whether the
        // crowd ducks under the announcer and how far, and the voice lift
        // over a loud crowd. The same stored settings.

        // --- The page -----------------------------------------------------

        var page = new FillPage();

        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.Add(master, gapBelow: UiLayout.RowGap);
        page.Add(_monitor);
        page.AddFill(layers, layers.LeastHeight);
        page.Add(reactions, gapBelow: 0);

        Controls.Add(page);

        // Measured in the fonts they are drawn in, so not until now: until
        // they were on the page, the boxes and switches had WinForms' own
        // default font rather than the body face. Again whenever the text
        // size changes.
        _textSize.Keep(SizeFields);
        _textSize.Keep(() => SizeTriggers(triggerBar));

        UpdateEnabledState();
    }

    /// <summary>
    /// The number boxes as wide as their largest value and arrows need, and
    /// never narrower than a number box was at the normal text size; and the
    /// live monitor as tall as it was then, at the size in use. Run again
    /// when the text size changes (TextSize.Keep).
    /// </summary>
    private void SizeFields()
    {
        var numbers = new[] { _numSurgeSeconds, _numFinishSeconds, _numFadeUp, _numFadeDown };

        foreach (var number in numbers)
        {
            var width = NumberWidth(number);

            // Only when it changed: a new width lays the row out again.
            if (number.Width != width)
            {
                number.Width = width;
            }
        }

        var monitor = UiLayout.AtTextSize(MonitorHeight);

        if (_monitor.Height != monitor)
        {
            _monitor.Height = monitor;
        }
    }

    /// <summary>A number box's width: every character of its largest value as a zero, its arrows and a little room - or a number box's width at the normal text size, at the size in use, if that is more.</summary>
    private static int NumberWidth(NumericUpDown number)
    {
        var largest = number.Maximum.ToString("F" + number.DecimalPlaces, System.Globalization.CultureInfo.CurrentCulture);

        var needed = TextRenderer.MeasureText(new string('0', largest.Length), number.Font).Width
            + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra;

        return Math.Max(UiLayout.AtTextSize(UiLayout.NumericWidth), needed);
    }

    /// <summary>
    /// The surge triggers, all one width - the widest caption's, and a
    /// section gap after it - so their row wraps into columns that line up;
    /// and the row no wider than five of them, so a wide card holds two rows
    /// of five rather than eight and two. A card too narrow for five gets
    /// fewer to a line, wrapping as any row of buttons does.
    /// </summary>
    private static void SizeTriggers(ButtonBar bar)
    {
        var boxes = bar.Controls.OfType<CheckBox>().ToList();

        if (boxes.Count == 0)
        {
            return;
        }

        var size = SizeAlike(boxes, UiLayout.SectionGap);

        // Each box with its margin, which is the bar's gap: the flow counts
        // it when it decides whether a box fits on the line (see
        // ButtonBar.OneLineWidth), so five exactly fit.
        var most = new Size(TriggerColumns * (size.Width + boxes[0].Margin.Horizontal), 0);

        if (bar.MaximumSize != most)
        {
            bar.MaximumSize = most;
        }
    }

    /// <summary>
    /// Gives check boxes one size: as wide as the widest of them needs for
    /// its caption with <paramref name="room"/> after it, and as tall as the
    /// tallest - for boxes that line up, the triggers in columns. The boxes do not size
    /// themselves (AutoSize is off), or each would be only as wide as its
    /// own caption. Returns the size.
    /// </summary>
    private static Size SizeAlike(IReadOnlyList<CheckBox> boxes, int room)
    {
        var sizes = boxes.Select(CheckBoxSize).ToList();
        var size = new Size(sizes.Max(s => s.Width) + room, sizes.Max(s => s.Height));

        foreach (var box in boxes)
        {
            // Only when it changed: a new size lays the row out again.
            if (box.Size != size)
            {
                box.Size = size;
            }
        }

        return size;
    }

    /// <summary>
    /// What a check box needs for its caption in the font it has: what it
    /// measures itself at, and never less than the caption on one line and
    /// the box's chrome as LayoutAudit counts them for a box that does not
    /// size itself, nor less tall than a line of its caption.
    /// </summary>
    private static Size CheckBoxSize(CheckBox box)
    {
        var own = box.GetPreferredSize(Size.Empty);

        var caption = TextRenderer.MeasureText(box.Text, box.Font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        var line = TextRenderer.MeasureText(box.Text, box.Font);

        return new Size(Math.Max(own.Width, caption.Width + CheckBoxChrome), Math.Max(own.Height, line.Height));
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

        // Everything in here is laid out at the text size in use, as the
        // panel's own height is (SizeFields): the stage labels, the surge
        // line and the meters' names are in the small face, and at fixed
        // pixels the labels overran their segments and the names ran into
        // the meters under them at 200%. Worked out on every paint, and the
        // timer repaints ten times a second, so a new text size is picked
        // up at once.
        var inset = UiLayout.AtTextSize(10);

        if (!_ambience.IsRunning)
        {
            using var offBrush = new SolidBrush(AppTheme.TextMuted);

            // Given the panel's width, inside its inset, so a long line
            // wraps - and ellipsizes on the last line the panel holds -
            // rather than running off the right-hand edge. Only whole lines
            // are drawn (LineLimit), so none is sliced through by the foot.
            using var offFormat = new StringFormat
            {
                Trimming = StringTrimming.EllipsisWord,
                FormatFlags = StringFormatFlags.LineLimit,
            };

            var offRect = new RectangleF(
                inset, inset,
                Math.Max(1, _monitor.ClientSize.Width - inset * 2),
                Math.Max(1, _monitor.ClientSize.Height - inset * 2));

            g.DrawString("Ambience is not playing - enable it above, or press Preview to audition your layers.",
                AppTheme.BodyFont, offBrush, offRect, offFormat);
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
        // rather than looking like the race state itself changed. The
        // segments and the gap between them at the text size in use: at a
        // fixed 92x22, "STAGING" and "EXCITED" were cut off at 200%.
        var segW = UiLayout.AtTextSize(92);
        var segH = UiLayout.AtTextSize(22);
        var segGap = UiLayout.AtTextSize(4);
        var x = inset;

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
            var rect = new Rectangle(x, inset, segW, segH);
            var isCurrent = intensity == effective;

            g.FillRectangle(isCurrent ? litBrush : dimBrush, rect);

            if (intensity == baseLevel && !isCurrent)
            {
                g.DrawRectangle(basePen, rect);
            }

            g.DrawString(label, AppTheme.SmallFont, isCurrent ? litText : dimText, rect, centered);
            x += segW + segGap;
        }

        // The line after the ladder, at the text size in use, and given the
        // rest of the panel's width on one line: the ladder grows with the
        // text, so a long surge reason ellipsizes at the panel's edge rather
        // than running through its border. A line that fits is drawn exactly
        // where it was.
        var noteLeft = x + UiLayout.AtTextSize(8);
        var noteTop = UiLayout.AtTextSize(15);
        var noteRect = new RectangleF(
            noteLeft, noteTop,
            Math.Max(1, _monitor.ClientSize.Width - inset - noteLeft),
            Math.Max(1, _monitor.ClientSize.Height - noteTop));

        using var noteFormat = new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };

        if (_ambience.IsSurging)
        {
            using var surgeBrush = new SolidBrush(AppTheme.Accent);
            // What set it off, so a surge can be traced to the moment that
            // caused it - and the time left from the surge itself, which for
            // the finish celebration is not the Surge lasts setting.
            var reason = string.IsNullOrWhiteSpace(_ambience.SurgeReason) ? "Surge" : _ambience.SurgeReason;
            g.DrawString($"SURGE: {reason} ({_ambience.SurgePeak}) - settling back in {_ambience.SurgeSecondsLeft:0.0}s",
                AppTheme.SmallFont, surgeBrush, noteRect, noteFormat);
        }
        else if (baseLevel != effective)
        {
            using var hintBrush = new SolidBrush(AppTheme.TextMuted);
            g.DrawString("(outline = base level)", AppTheme.SmallFont, hintBrush, noteRect, noteFormat);
        }
        else if (!string.IsNullOrWhiteSpace(_ambience.SurgeReason) && _ambience.LastSurgeUtc > DateTime.MinValue)
        {
            // Settled - still worth knowing what last lifted the crowd.
            var ago = DateTime.UtcNow - _ambience.LastSurgeUtc;
            var when = ago.TotalMinutes >= 1 ? $"{(int)ago.TotalMinutes}m ago" : $"{(int)ago.TotalSeconds}s ago";
            using var lastBrush = new SolidBrush(AppTheme.TextMuted);
            g.DrawString($"Last surge: {_ambience.SurgeReason}, {when}", AppTheme.SmallFont, lastBrush, noteRect, noteFormat);
        }

        // Per-layer meters showing real current volume, mid-fade included.
        // Their names over them and their levels beside them are in the
        // small face, so the meters, their rows and the room between them
        // are at the text size in use too - the panel's height is.
        var volumes = _ambience.CurrentLayerVolumes();
        var meterX = inset;
        var meterW = UiLayout.AtTextSize(150);
        var meterH = UiLayout.AtTextSize(8);
        var nameTop = UiLayout.AtTextSize(44);
        var meterTop = UiLayout.AtTextSize(60);
        var levelLeft = UiLayout.AtTextSize(6);
        var levelTop = UiLayout.AtTextSize(56);
        var meterStep = meterW + UiLayout.AtTextSize(60);

        foreach (var layer in _store.Current.Layers.Where(l => l.Enabled))
        {
            if (!volumes.TryGetValue(layer.Key, out var volume))
            {
                continue;
            }

            var name = layer.DisplayName.Length > 18 ? layer.DisplayName[..18] : layer.DisplayName;
            g.DrawString(name, AppTheme.SmallFont, dimText, meterX, nameTop);

            var track = new Rectangle(meterX, meterTop, meterW, meterH);
            g.FillRectangle(dimBrush, track);

            var filled = (int)(meterW * Math.Clamp(volume, 0f, 1f));
            if (filled > 0)
            {
                g.FillRectangle(litBrush, new Rectangle(meterX, meterTop, filled, meterH));
            }

            g.DrawRectangle(border, track);
            g.DrawString($"{volume * 100:0}%", AppTheme.SmallFont, dimText, meterX + meterW + levelLeft, levelTop);

            meterX += meterStep;

            // Only as many meters as fit - a long layer list shouldn't spill
            // out of the panel.
            if (meterX + meterW > _monitor.Width - inset)
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

        var reacting = on && _chkReact.Checked;

        _lblReactionsState.Text = on
            ? reacting ? string.Empty : ReactionsNeedReact
            : ReactionsNeedEnable;
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
            _lblStatus.Text = NoLayersStatus;
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

        // The wordings are named at the top of the class, where the line
        // keeps room for the longest of them (BuildLayout).
        var (text, warn) = current.Source switch
        {
            AmbienceSource.Soundscape when !soundscapeReady => (SoundscapeNothingPlaying, true),

            AmbienceSource.Soundscape => (SoundscapeOnly, false),

            AmbienceSource.Mine when !mineReady => (MineNothingPlaying, true),

            AmbienceSource.Mine => (MineOnly, false),

            _ when !soundscapeReady && !mineReady => (NothingSetUp, false),

            _ => (EverythingPlays, false),
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

    /// <summary>True while the page's Preview button is previewing - see TogglePreview.</summary>
    private bool _pagePreviewing;

    /// <summary>True when the Preview button started the ambience itself, so stopping the preview stops only that.</summary>
    private bool _pagePreviewStartedAmbience;

    /// <summary>The crowd's mood before the preview set it to Racing, put back when the preview stops.</summary>
    private AmbienceService.Intensity _pagePreviewMoodBefore;

    /// <summary>
    /// Plays the ambience so it can be tuned by ear. Deliberately runs at
    /// "Racing" level - the middle of the range, and what you'll hear for
    /// most of an actual race.
    /// </summary>
    private void TogglePreview(Button btnPreview)
    {
        // Decided by whether THIS button is previewing, not by whether the
        // crowd is playing. Going by IsRunning, Preview pressed mid-race -
        // the button still said "Preview" - stopped the session crowd for
        // the rest of the race.
        if (_pagePreviewing)
        {
            _pagePreviewing = false;
            btnPreview.Text = "Preview";

            // Put the mood back first, as the Mixer's audition does: the
            // preview set race level, and a stopped crowd kept it, so the
            // next start between races came up at race level. Left alone if
            // something has since moved the mood off Racing (a finish, a
            // caution).
            if (_ambience.CurrentIntensity == AmbienceService.Intensity.Racing)
            {
                _ambience.SetIntensity(_pagePreviewMoodBefore);
            }

            // Only what this button started is stopped.
            if (_pagePreviewStartedAmbience)
            {
                _ambience.Stop();
            }

            _pagePreviewStartedAmbience = false;
            return;
        }

        _pagePreviewMoodBefore = _ambience.CurrentIntensity;
        _pagePreviewStartedAmbience = !_ambience.IsRunning;

        if (_pagePreviewStartedAmbience)
        {
            _ambience.Settings = _store.Current;

            // Bypasses the enabled check so files can be auditioned before
            // committing to switching ambience on.
            _ambience.Start(ignoreEnabledSetting: true);
        }

        if (_ambience.IsRunning)
        {
            _pagePreviewing = true;
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

    /// <summary>Hands the player the edited settings, restarting it if it is playing, and starts it if asked and ambience is enabled.</summary>
    /// <param name="startIfEnabled">
    /// Whether this should actually begin playback, or only refresh a
    /// session that's already running.
    ///
    /// Editing a layer shouldn't START the ambience - someone adjusting a
    /// volume while nothing is playing doesn't expect sound to begin. But
    /// if it IS playing, the change needs picking up, since mixer inputs
    /// are built at Start.
    ///
    /// A bed that is already playing is restarted past the master switch,
    /// the way AfterUndo and the layer editor's preview restart it: whatever
    /// let it play before still applies. This used to stop the ambience
    /// outright whenever the switch was off - but session auto-start brings
    /// the crowd up past that switch on purpose (StartSessionAudio, and the
    /// switch is off by default), so saving a layer, deleting one or picking
    /// another Source mid-race silenced the crowd for the rest of the race.
    /// Turning the switch itself off is handled where the switch is, and
    /// still stops it.
    /// </param>
    private void ApplyRunState(bool startIfEnabled = false)
    {
        _ambience.Settings = _store.Current;

        if (_ambience.IsRunning)
        {
            // Restart so newly added or edited layers are picked up. Restart
            // rather than Stop and Start, so an edit made while the crowd is
            // fading out at the end of a session keeps that fade going.
            _ambience.Restart(ignoreEnabledSetting: true);
        }
        else if (startIfEnabled && _store.Current.Enabled)
        {
            _ambience.Start();
        }
    }
}

/// <summary>
/// Adds or edits one ambience layer.
///
/// On the layout pieces (DialogFit): the name and the file lined up in a
/// field grid, Browse... beside the file and its hint under it; the layer's
/// switches and its always-on level along one line (a FieldRow), their hint
/// under them; the volumes' heading and hint, and Play while I drag with
/// what the preview is doing beside it; then a line for each race state -
/// its caption in one column, the slider across the room left, and its
/// value and what the state means in columns of their own on its right -
/// and Save and Cancel at the foot. Everything but the buttons is the
/// window's body, so on a screen too short for all of it, it scrolls and
/// Save and Cancel stay in view. It used to be placed by hand in a 672px
/// window as tall as its rows came to - each row under the measured bottom
/// of the one before and each column at an x worked out from the others -
/// which at a larger text size ran off the bottom of a small screen,
/// buttons and all.
/// </summary>
public sealed class AmbienceLayerForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size: the 640px it laid its rows out in, and its margins.</summary>
    private const int DialogWidth = 672;

    /// <summary>
    /// The file box at the normal text size - enough to show a path's end
    /// with Browse... beside it on one line at every text size, the body's
    /// scrollbar or not.
    /// </summary>
    private const int FileBoxWidth = 360;

    /// <summary>A race state's slider at the normal text size - its lines the 46px apart they always were.</summary>
    private const int SliderHeight = 36;

    private readonly TextBox _txtName = new();
    private readonly TextBox _txtFile = new();
    private readonly CheckBox _chkEnabled = new ThemedCheckBox();

    private readonly Dictionary<AmbienceService.Intensity, SliderControl> _sliders = new();
    private readonly Dictionary<AmbienceService.Intensity, Label> _valueLabels = new();
    private readonly Dictionary<AmbienceService.Intensity, int> _shownValues = new();

    /// <summary>
    /// Keeps each percentage label agreeing with its slider even when the
    /// trackbar's own events don't arrive - which is what was happening:
    /// thumbs dragged, numbers frozen at where they started.
    /// </summary>
    private readonly System.Windows.Forms.Timer _sliderSync = new() { Interval = 150 };
    private readonly CheckBox _chkLivePreview = new ThemedCheckBox();
    private readonly CheckBox _chkAlwaysOn = new ThemedCheckBox();
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
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        // Stopped when the dialog closes (OnFormClosed), which is how the
        // page always ends it. A dialog put away without being closed - the
        // layout test builds each window and disposes it - would otherwise
        // leave the timer ticking over a window that is gone, and holding it.
        Disposed += (_, _) => _sliderSync.Dispose();

        var (page, body) = BuildLayout();

        AppTheme.ApplyRecursively(this);

        DialogFit.Keep(this, page, width: DialogWidth, body: body);
    }

    /// <summary>The window's page, and its body - see the class notes.</summary>
    private (FillPage Page, PageStack Body) BuildLayout()
    {
        _txtName.Text = Result.Name;
        _txtName.PlaceholderText = "Crowd, Track hum, Pit lane...";

        var btnBrowse = new Button { Text = "Browse..." };
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

        _txtFile.Width = UiLayout.AtTextSize(FileBoxWidth);
        _txtFile.Text = Result.FilePath;

        var fields = new FieldGrid();
        fields.AddRow("Name:", _txtName, stretch: true);
        fields.AddRow("Audio file:", FieldGrid.Inline(_txtFile, btnBrowse));
        fields.AddHint("A short clip that loops cleanly works best - it repeats continuously while the ambience is running.");

        _chkEnabled.Text = "Layer enabled";
        _chkEnabled.AutoSize = true;
        _chkEnabled.Checked = Result.Enabled;

        _chkAlwaysOn.Text = "Always on - ignore race state";
        _chkAlwaysOn.AutoSize = true;
        _chkAlwaysOn.Checked = Result.AlwaysOn;
        _chkAlwaysOn.CheckedChanged += (_, _) => UpdateAlwaysOnState();

        // Room for "100", its largest value, at every text size.
        _numConstantVolume.Width = UiLayout.AtTextSize(UiLayout.NumericWidth);
        _numConstantVolume.Minimum = 0;
        _numConstantVolume.Maximum = 100;
        _numConstantVolume.Increment = 5;
        _numConstantVolume.Value = (decimal)Math.Clamp(Result.ConstantVolume * 100f, 0, 100);

        // The two switches, and the level an always-on layer plays at after
        // them - going onto a second line, rather than off the edge, when the
        // window is too narrow for all three.
        var switches = new FieldRow();
        switches.Add(null, _chkEnabled);
        switches.Add(null, _chkAlwaysOn);
        switches.Add(new Label { Text = "Level:" }, _numConstantVolume);

        var lblAlwaysOnHint = new Label
        {
            Text = "For a background theme or your own uploaded track - plays at one level throughout, "
                 + "including between races. The per-state volumes below don't apply to it.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        var lblVolumes = new Label
        {
            Text = "Volume at each race state",
            AutoSize = true,
            Font = AppTheme.BoldFont,
        };

        var lblVolumeHint = new Label
        {
            Text = "This is what makes a layer react. A crowd might be quiet at idle and loud at peak; "
                 + "track noise might be the opposite, loud while racing and silent between.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        _chkLivePreview.Text = "Play while I drag";
        _chkLivePreview.AutoSize = true;
        _chkLivePreview.Checked = true;
        _chkLivePreview.CheckedChanged += (_, _) =>
        {
            if (!_chkLivePreview.Checked)
            {
                EndPreview();
                _lblPreviewState.Text = string.Empty;
            }
        };

        // What the preview is doing, at the end of the tick's line - cut
        // short with "..." rather than wrapped, or on a line of its own when
        // the line leaves it less than the longest thing it says (FieldRow),
        // so saying it never moves the sliders under it.
        _lblPreviewState.AutoSize = false;
        _lblPreviewState.AutoEllipsis = true;
        _lblPreviewState.Font = AppTheme.SmallFont;
        _lblPreviewState.ForeColor = AppTheme.TextMuted;

        var previewWidth = new[]
            {
                "Set a file above to hear these.",
                "Couldn't play that file.",
                $"Playing \u2013 {AmbienceService.Intensity.Anticipation} at 100%",
            }
            .Max(wording => TextRenderer.MeasureText(wording, AppTheme.SmallFont).Width);

        var preview = new FieldRow();
        preview.Add(null, _chkLivePreview);
        preview.AddLast(null, _lblPreviewState, () => previewWidth);

        var rows = new (AmbienceService.Intensity Intensity, string Label, string Hint)[]
        {
            (AmbienceService.Intensity.Idle, "Idle", "between races"),
            (AmbienceService.Intensity.Anticipation, "Staging", "staged and counting down"),
            (AmbienceService.Intensity.Racing, "Racing", "normal green-flag running"),
            (AmbienceService.Intensity.Excited, "Excited", "close battle, lead change"),
            (AmbienceService.Intensity.Peak, "Peak", "finish, record, big moment"),
        };

        // Every line one height - the slider's, or a line of the value's face
        // and a little room, when that is more - with the value and the hint
        // drawn level with the middle of it. The value's column is wide
        // enough for "100%" so it can never clip, and the hint's for the
        // longest of them on one line, with a few pixels spare: a hint that
        // wrapped would be two lines in one line's height at 150% and 200%.
        var lineHeight = Math.Max(SliderHeight, Pieces.LineHeight(AppTheme.BoldFont) + UiLayout.HintGap * 2);
        var valueWidth = TextRenderer.MeasureText("100%", AppTheme.BoldFont).Width + 6;
        var hintWidth = rows.Max(row => TextRenderer.MeasureText(row.Hint, AppTheme.SmallFont).Width) + UiLayout.HintGap;

        var volumes = new FieldGrid();

        foreach (var (intensity, label, hint) in rows)
        {
            // SliderControl, not TrackBar: a skinned TrackBar drew its handle
            // somewhere other than where it could be grabbed, so dragging
            // took several tries. See SliderControl.
            var slider = new SliderControl
            {
                Height = lineHeight,
                Minimum = 0,
                Maximum = 100,
                SmallChange = 1,
                LargeChange = 10,
                Value = (int)Math.Clamp(Result.VolumeFor(intensity) * 100f, 0, 100),
            };

            // Sized by the line rather than by their text, so a new value
            // never lays anything out again.
            var lblValue = new Label
            {
                Text = $"{slider.Value}%",
                AutoSize = false,
                Height = lineHeight,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = AppTheme.BoldFont,
            };

            var lblHint = new Label
            {
                Text = hint,
                AutoSize = false,
                Height = lineHeight,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = AppTheme.TextMuted,
                Font = AppTheme.SmallFont,
            };

            var captured = intensity;

            // Every change - drag, click, wheel, keyboard - raises
            // ValueChanged; the idle sync timer stays as a backstop.
            slider.ValueChanged += (_, _) => SyncSliderRow(captured, preview: true);

            _sliders[intensity] = slider;
            _valueLabels[intensity] = lblValue;
            _shownValues[intensity] = slider.Value;

            volumes.AddRow($"{label}:", VolumeLine(slider, lblValue, valueWidth, lblHint, hintWidth), stretch: true);
        }

        var btnSave = new Button { Text = "Save" };
        btnSave.Click += BtnSave_Click;

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // A row's gap above the buttons rather than a section's - about what
        // it always had - which is the difference between all of it and a
        // few pixels short of it on a 1280x720 screen.
        var page = new FillPage();
        var body = DialogFit.Body(page, gapBelow: UiLayout.RowGap);

        body.Add(fields, gapBelow: UiLayout.RowGap);
        body.Add(switches, gapBelow: UiLayout.HintGap);
        body.Add(lblAlwaysOnHint);
        body.Add(lblVolumes, gapBelow: UiLayout.HintGap);
        body.Add(lblVolumeHint, gapBelow: UiLayout.RowGap);
        body.Add(preview, gapBelow: UiLayout.RowGap);
        body.Add(volumes, gapBelow: 0);

        // Bottom-right, Cancel outermost - same as the app's other dialogs -
        // and Tab still reaches Save first: the bar takes them right to
        // left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);
        btnSave.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

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

        return (page, body);
    }

    /// <summary>
    /// A race state's line: the slider across the room it has, and its value
    /// and the state's hint in columns of their own down the right - a
    /// Beside inside a Beside, each as tall as the line.
    /// </summary>
    private static Beside VolumeLine(SliderControl slider, Label value, int valueWidth, Label hint, int hintWidth)
    {
        var withValue = new Beside(valueWidth)
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        withValue.Add(slider, gapBelow: 0);
        withValue.AddSide(value, gapBelow: 0);

        var line = new Beside(hintWidth)
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        line.Add(withValue, gapBelow: 0);
        line.AddSide(hint, gapBelow: 0);

        return line;
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
            // play before the dialog still applies. Restart rather than Stop
            // and Start, so closing the dialog while the crowd is fading out
            // at the end of a session keeps that fade going. The saved
            // settings are handed over first - Restart reopens the layers
            // from whatever settings it has.
            if (_store is not null)
            {
                _ambience.Settings = _store.Current;
            }

            _ambience.Restart(ignoreEnabledSetting: true);
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
