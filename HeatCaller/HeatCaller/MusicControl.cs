namespace HeatCaller;

/// <summary>
/// The music library and player - a between-race soundtrack.
///
/// Separate from Ambience on purpose: ambience reacts to the race, music is
/// a playlist someone puts on. Keeping them apart means a track never
/// swells at a fast lap.
/// </summary>
public sealed class MusicControl : UserControl
{
    private readonly MusicStore _store;
    private readonly MusicPlayer _player;
    private readonly UndoService _undo;

    private readonly CheckBox _chkEnabled = new ThemedCheckBox();
    private readonly NumericUpDown _numVolume = new();
    private readonly CheckBox _chkShuffle = new ThemedCheckBox();
    private readonly CheckBox _chkPauseDuringRaces = new ThemedCheckBox();
    private readonly CheckBox _chkDuck = new ThemedCheckBox();
    private readonly NumericUpDown _numDuckTo = new();

    private readonly Label _lblNowPlaying = new();
    private readonly Button _btnPlayStop = new();
    private readonly Button _btnSkip = new();
    private readonly Button _btnPrevious = new();
    private readonly Button _btnStop = new();
    private readonly SliderControl _seek = new();
    private readonly Label _lblTime = new();
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 500 };

    /// <summary>True while the seek bar is being dragged, so the timer leaves it alone.</summary>
    private bool _seeking;

    private readonly DataGridView _grid = new();
    private readonly Label _lblStatus = new();

    // "Play: (o) Theme music  ( ) My music" - see MusicSettings.PlayThemeMusic.
    private readonly RadioButton _rdoThemeMusic = new ThemedRadioButton();
    private readonly RadioButton _rdoMyMusic = new ThemedRadioButton();

    /// <summary>
    /// The sentence under the Theme music / My music choice - see
    /// RefreshSource. As tall as the longer of the two things it can say for
    /// the soundscape applied now, so switching between them never moves the
    /// player under it.
    /// </summary>
    private readonly SteadyLabel _lblSource = new();

    /// <summary>Set while the page seeds or re-syncs its own controls, so that isn't taken as an edit.</summary>
    private bool _syncing;

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The clock's column width as last measured, and the face it was measured in - see TimeWidth.</summary>
    private (Font Font, int Width)? _timeWidth;

    /// <summary>The seek bar's height, and the clock's beside it, at the normal text size.</summary>
    private const int SeekHeight = 30;

    /// <summary>
    /// The least room what is playing takes at the end of the transport
    /// buttons' line, at the normal text size, before it moves onto a line of
    /// its own - see BuildLayout.
    /// </summary>
    private const int NowPlayingLeastWidth = 240;

    /// <summary>The fewest tracks the library shows under its headings before the page scrolls instead.</summary>
    private const int GridLeastRows = 4;

    public MusicControl(MusicStore store, MusicPlayer player, UndoService undo)
    {
        _store = store;
        _player = player;
        _undo = undo;

        Dock = DockStyle.Fill;
        ForeColor = AppTheme.TextDark;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize.
        //
        // No design size and no MinimumSize: the 900x616 floor was honoured
        // over the size the page was given, so a smaller window cut the page
        // off rather than scrolling it. A short window scrolls the page
        // instead (FillPage), which also does away with the page's own
        // scrolling.
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshGrid();
        RefreshNowPlaying(_player.NowPlaying);

        _player.NowPlayingChanged += OnNowPlayingChanged;

        // An undo changes the library from outside this page - see
        // OnStoreRestored.
        _store.Restored += OnStoreRestored;

        _tick.Tick += (_, _) => TickPosition();
        _tick.Start();
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
    /// Re-reads the library after an undo put tracks back or took them out.
    /// The undo itself only touches the store and the player, never this
    /// page, which may have been rebuilt by the time it runs.
    /// </summary>
    private void OnStoreRestored()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        RefreshGrid();
    }

    private void OnNowPlayingChanged(MusicTrack? track)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        // No handle yet - the Music page has not been shown this session, or
        // it was just rebuilt. InvokeRequired answers false then even on the
        // playback thread (there is no handle to ask), so the page - the
        // list's rows included, which MarkPlayingRow restyles - used to be
        // updated straight from the thread pool. Skipped instead: the page
        // catches up when its handle arrives (OnHandleCreated).
        if (!IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            // Raised from the playback thread when a track ends naturally.
            // The handle can still go between the check above and this - the
            // page closing as a track ends - and that is nothing to report.
            try
            {
                BeginInvoke(() => RefreshNowPlaying(track));
            }
            catch (InvalidOperationException)
            {
                // ObjectDisposedException included - it derives from this.
            }

            return;
        }

        RefreshNowPlaying(track);
    }

    /// <summary>Brings Now Playing up to date for whatever changed while the page had no handle - see OnNowPlayingChanged.</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RefreshNowPlaying(_player.NowPlaying);
    }

    /// <summary>
    /// What the page is for, then the switches along one line - Enable
    /// music, the volume, and Shuffle with what it does - and under them the
    /// choice of Theme music or My music, with the sentence saying what that
    /// choice plays. Then three cards: Player - the transport buttons with
    /// what is playing at the end of their line, and the seek bar with the
    /// clock beside it; Library - the tracks, taking the height that is
    /// left, and their buttons; Behaviour - when the music stops, and how far
    /// it ducks. Then what the last change did. On the layout pieces
    /// (LayoutPieces.cs), so nothing is placed by hand.
    ///
    /// It used to be placed in pixels against a 980x696 page: every row from
    /// the Player box down moved 76px by hand when the Theme music / My music
    /// line went in above it, the Player box 944px wide with the clock at
    /// x=844 whatever the text size, the list 198px tall however tall the
    /// window, and a 900x616 floor the page kept however small its window.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "A soundtrack for between races. Eleven tracks are included - add your own with Add Track, "
                 + "and they're copied into the app's music folder so they keep working if you move the original.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _chkEnabled.Text = "Enable music";
        _chkEnabled.AutoSize = true;
        _textSize.Wear(_chkEnabled, () => AppTheme.BoldFont);
        _chkEnabled.Checked = _store.Current.Enabled;
        _chkEnabled.CheckedChanged += (_, _) =>
        {
            _store.Current.Enabled = _chkEnabled.Checked;
            _store.Save();
            _player.Settings = _store.Current;

            if (!_chkEnabled.Checked)
            {
                _player.Stop();
            }

            UpdateEnabledState();
        };

        _numVolume.Minimum = 0;
        _numVolume.Maximum = 100;
        _numVolume.Increment = 5;
        _numVolume.Value = (decimal)Math.Clamp(_store.Current.Volume * 100f, 0, 100);
        _numVolume.ValueChanged += (_, _) =>
        {
            _store.Current.Volume = (float)_numVolume.Value / 100f;
            _store.Save();
            _player.Settings = _store.Current;

            // Applied live so it can be set by ear while playing.
            _player.RefreshVolume();
        };

        var lblVolumeSuffix = new Label { Text = "%", ForeColor = AppTheme.TextMuted };

        _chkShuffle.Text = "Shuffle";
        _chkShuffle.AutoSize = true;
        _chkShuffle.Checked = _store.Current.Shuffle;
        _chkShuffle.CheckedChanged += (_, _) =>
        {
            _store.Current.Shuffle = _chkShuffle.Checked;
            _store.Save();
            _player.Settings = _store.Current;
        };

        var lblShuffleHint = new Label
        {
            Text = "plays every track once before repeating any",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblShuffleHint, () => AppTheme.SmallFont);

        var volume = FieldGrid.Inline(_numVolume, lblVolumeSuffix);

        // Along one line, and onto the next when there is not room for them
        // all (FieldRow). Shuffle and what it does are one pair, so a line
        // that wraps never parts them.
        var switches = new FieldRow();

        switches.Add(null, _chkEnabled);
        switches.Add(new Label { Text = "Volume:" }, volume);
        switches.Add(null, FieldGrid.Inline(_chkShuffle, lblShuffleHint));

        // --- What plays ---
        //
        // Theme music or the club's own, as a choice of its own. Applying a
        // soundscape used to BE this choice, made silently: it switched
        // every library track off except the soundscape's, so the club's
        // playlist was overwritten and the theme's one track looked as
        // though it had taken the place of their music. Now the soundscape
        // only says what "Theme music" is, and this says which plays.

        var lblPlay = new Label
        {
            Text = "Play:",
            AutoSize = true,
            ForeColor = AppTheme.TextDark,
        };

        _textSize.Wear(lblPlay, () => AppTheme.BoldFont);

        _rdoThemeMusic.Text = "Theme music";
        _rdoThemeMusic.AutoSize = true;

        _rdoMyMusic.Text = "My music";
        _rdoMyMusic.AutoSize = true;

        _syncing = true;
        _rdoThemeMusic.Checked = _store.Current.PlayThemeMusic == true;
        _rdoMyMusic.Checked = _store.Current.PlayThemeMusic != true;
        _syncing = false;

        // Both radios raise CheckedChanged on a switch - one going off, one
        // coming on - so only the one coming on acts.
        _rdoThemeMusic.CheckedChanged += (_, _) =>
        {
            if (_rdoThemeMusic.Checked)
            {
                SetPlayThemeMusic(true);
            }
        };
        _rdoMyMusic.CheckedChanged += (_, _) =>
        {
            if (_rdoMyMusic.Checked)
            {
                SetPlayThemeMusic(false);
            }
        };

        // Both radios in the one row, which makes them one group - picking
        // one clears the other, as it did when both sat on the page itself.
        var source = new FieldRow();

        source.Add(lblPlay, _rdoThemeMusic);
        source.Add(null, _rdoMyMusic);

        _lblSource.ForeColor = AppTheme.TextMuted;
        _textSize.Wear(_lblSource, () => AppTheme.SmallFont);

        // --- Player ---

        // Words rather than media glyphs. U+23EE/23F9/23ED aren't in
        // Bahnschrift, so Windows either drew a .notdef box or substituted
        // another font mid-row at a different weight and size - which is
        // what made these look wrong. Words always render, match every
        // other button in the app, and lose nothing: the row already reads
        // left to right in the order every player uses.
        _btnPrevious.Text = "Previous";
        _btnPrevious.Click += (_, _) => { _player.Previous(); RefreshNowPlaying(_player.NowPlaying); };

        _btnPlayStop.Text = "Play";
        _btnPlayStop.Click += (_, _) => TogglePlay();

        _btnStop.Text = "Stop";
        _btnStop.Click += (_, _) => { _player.Stop(); RefreshNowPlaying(null); };

        _btnSkip.Text = "Next";
        _btnSkip.Click += (_, _) => { _player.Skip(); RefreshNowPlaying(_player.NowPlaying); };

        // One width for all four, because a transport row wants to look like
        // a transport row: each as wide as the widest of them needs
        // (ButtonBar, sameWidth). The widest caption is "Previous" - wider in
        // the bold face than "Resume" and "Pause" too, which Play/Stop swaps
        // in mid-song - so the row keeps its width as they come and go. Were
        // one of them ever the wider, the bar would fit all four to it.
        var transport = new ButtonBar(sameWidth: true).With(_btnPrevious, _btnPlayStop, _btnStop, _btnSkip);

        // What is playing, at the end of the buttons' line, cut short with
        // "..." when a long title runs out of room there - or on a line of
        // its own when the line leaves it less than a few words' room
        // (FieldRow). That least is the same whatever it says, so starting
        // and stopping the music never moves the seek bar under it.
        _lblNowPlaying.AutoSize = false;
        _lblNowPlaying.AutoEllipsis = true;
        _textSize.Wear(_lblNowPlaying, () => AppTheme.BoldFont);

        var nowPlaying = new FieldRow();

        nowPlaying.Add(null, transport);
        nowPlaying.AddLast(null, _lblNowPlaying, () => UiLayout.AtTextSize(NowPlayingLeastWidth));

        // Seek bar. Dragging is tracked separately from playback position,
        // or the timer would fight the user's mouse and snap the thumb back
        // every tick while they're still holding it.
        _seek.Minimum = 0;
        _seek.Maximum = 1000;
        _seek.MouseDown += (_, _) => _seeking = true;
        _seek.MouseUp += (_, _) =>
        {
            var (_, duration) = _player.CurrentPosition;
            if (duration > TimeSpan.Zero)
            {
                _player.Seek(duration * (_seek.Value / 1000.0));
            }

            _seeking = false;
        };

        // The clock against the card's right-hand edge, in a column as wide
        // as the longest time it can show (TimeWidth), and the seek bar
        // across the rest - so the bar never moves as the time counts up.
        _lblTime.ForeColor = AppTheme.TextMuted;
        _lblTime.TextAlign = ContentAlignment.MiddleRight;
        _lblTime.Text = "0:00 / 0:00";

        var seekRow = new Beside(TimeWidth)
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        seekRow.Add(_seek, gapBelow: 0);
        seekRow.AddSide(_lblTime, gapBelow: 0);

        var playerCard = new Section("Player");

        playerCard.Add(nowPlaying);
        playerCard.Add(seekRow, gapBelow: 0);

        // --- Library ---

        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        // Multi-select: pruning several tracks from a library is far more
        // common than removing exactly one.
        _grid.MultiSelect = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "title", HeaderText = "Track", FillWeight = 110 },
            new DataGridViewTextBoxColumn { Name = "source", HeaderText = "Source", FillWeight = 40 },
            // "My music" rather than "In playlist": with Theme music on, the
            // playlist is the soundscape's, and these ticks wait for the
            // switch back.
            new DataGridViewTextBoxColumn { Name = "state", HeaderText = "My music", FillWeight = 40 },
            new DataGridViewTextBoxColumn { Name = "file", HeaderText = "File", FillWeight = 90 }
        );

        // Every heading whole, at the text size in use - see Pieces.KeepHeadingsWhole.
        _textSize.Keep(() => Pieces.KeepHeadingsWhole(_grid));

        // Double-click plays that track. Toggling inclusion moved to its
        // own button - in a music library, double-click means "play this",
        // and anything else is a surprise.
        _grid.CellDoubleClick += (_, args) =>
        {
            if (args.RowIndex < 0 || Selected() is not { } track)
            {
                return;
            }

            if (!File.Exists(track.FilePath))
            {
                AppDialog.Show(this, $"That file is missing:\r\n\r\n{track.FilePath}",
                    "Can't play", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _player.Settings = _store.Current;
            _player.PlayTrack(track);
            RefreshNowPlaying(_player.NowPlaying);

            if (!InPlayingList(track))
            {
                // Playing an excluded track directly is reasonable, but it
                // shouldn't silently re-add it to the rotation: it plays once
                // and the playlist carries on (MusicPlayer.PlayTrack). Said
                // once it IS playing - this used to be shown while nothing
                // played at all. A file that won't open is reported in the
                // console, and the playlist is tried instead.
                var started = _player.NowPlaying?.Id == track.Id;

                _lblStatus.Text = !started
                    ? $"Couldn't play \"{track.DisplayName}\" - the console says why."
                    : _player.ThemeMusicInUse
                        ? $"\"{track.DisplayName}\" isn't part of the theme music - playing it once, then back to the theme."
                        : $"\"{track.DisplayName}\" is excluded from My music - playing it once.";
                _lblStatus.ForeColor = started ? AppTheme.Accent : AppTheme.Danger;
            }
        };

        var btnAdd = new Button { Text = "Add Track..." };
        btnAdd.Click += (_, _) => AddTracks();

        var btnToggle = new Button { Text = "Include / Exclude" };
        btnToggle.Click += (_, _) => ToggleSelectedEnabled();

        var btnRemove = new Button { Text = "Remove" };
        btnRemove.Click += (_, _) => RemoveSelected();

        var btnFolder = new Button { Text = "Open Music Folder" };
        btnFolder.Click += (_, _) => OpenMusicFolder();

        var btnRestore = new Button { Text = "Restore Included Tracks" };
        btnRestore.Click += (_, _) => RestoreIncluded();

        var btnClear = new Button { Text = "Clear Library" };
        btnClear.Click += (_, _) => ClearLibrary();

        var library = new Section("Library");

        library.AddFill(_grid, () => _grid.ColumnHeadersHeight + _grid.RowTemplate.Height * GridLeastRows + 2);
        library.Add(new ButtonBar().With(btnAdd, btnToggle, btnRemove, btnFolder, btnRestore, btnClear), gapBelow: 0);

        // --- Behaviour ---

        _chkPauseDuringRaces.Text = "Stop while a race is running";
        _chkPauseDuringRaces.AutoSize = true;
        _chkPauseDuringRaces.Checked = _store.Current.PauseDuringRaces;
        _chkPauseDuringRaces.CheckedChanged += (_, _) =>
        {
            _store.Current.PauseDuringRaces = _chkPauseDuringRaces.Checked;
            _store.Save();
            _player.Settings = _store.Current;
        };

        // Under the box it is about, wrapping to the card's width, rather
        // than beside it: a line this long beside the box would be cut short
        // in a narrow window or at a large text size.
        var lblPauseHint = new Label
        {
            Text = "ignored if the Mixer is set to start music when a session begins - that's the more specific instruction",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblPauseHint, () => AppTheme.SmallFont);

        _chkDuck.Text = "Duck under announcements";
        _chkDuck.AutoSize = true;
        _chkDuck.Checked = _store.Current.DuckUnderAnnouncements;
        _chkDuck.CheckedChanged += (_, _) =>
        {
            _store.Current.DuckUnderAnnouncements = _chkDuck.Checked;
            _store.Save();
            _player.Settings = _store.Current;
            UpdateEnabledState();
        };

        _numDuckTo.Minimum = 5;
        _numDuckTo.Maximum = 100;
        _numDuckTo.Increment = 5;
        _numDuckTo.Value = (decimal)Math.Clamp(_store.Current.DuckToFraction * 100f, 5, 100);
        _numDuckTo.ValueChanged += (_, _) =>
        {
            _store.Current.DuckToFraction = (float)_numDuckTo.Value / 100f;
            _store.Save();
            _player.Settings = _store.Current;
        };

        var lblDuckSuffix = new Label { Text = "%", ForeColor = AppTheme.TextMuted };

        var duckTo = FieldGrid.Inline(_numDuckTo, lblDuckSuffix);

        // How far it ducks after the box that turns it on, and under it when
        // the line is too narrow for both (FieldRow).
        var duck = new FieldRow();

        duck.Add(null, _chkDuck);
        duck.Add(new Label { Text = "down to:" }, duckTo);

        var behaviour = new Section("Behaviour");

        behaviour.Add(_chkPauseDuringRaces, stretch: false, gapBelow: UiLayout.HintGap);
        behaviour.Add(lblPauseHint);
        behaviour.Add(duck, gapBelow: 0);

        // What the library comes to, and what the last change did - wrapping
        // rather than cut short. It always says something (RefreshGrid), and
        // it is the last thing on the page, so nothing under it moves when a
        // long message takes a second line.
        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = AppTheme.TextMuted;

        // The number boxes, the seek bar and the clock at the text size in
        // use - see SizeFields.
        _textSize.Keep(SizeFields);

        // --- The page ---

        var page = new FillPage();

        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.Add(switches, gapBelow: UiLayout.RowGap);
        page.Add(source, gapBelow: UiLayout.HintGap);
        page.Add(_lblSource);
        page.Add(playerCard);
        page.AddFill(library, library.LeastHeight);
        page.Add(behaviour, gapBelow: UiLayout.RowGap);
        page.Add(_lblStatus, gapBelow: 0);

        Controls.Add(page);

        UpdateEnabledState();
        RefreshSource();
    }

    /// <summary>
    /// The sizes that follow the text size: the number boxes as wide as
    /// their largest value and their arrows need (NumberWidth), and the seek
    /// bar and the clock beside it as tall as the bar was at the normal text
    /// size - never shorter than a line of the clock's face. Run again when
    /// the text size changes (TextSize.Keep).
    /// </summary>
    private void SizeFields()
    {
        Fit(_numVolume, NumberWidth(_numVolume));
        Fit(_numDuckTo, NumberWidth(_numDuckTo));

        // The clock wears the page's face.
        var seek = Math.Max(UiLayout.AtTextSize(SeekHeight), Font.Height + 4);

        if (_seek.Height != seek)
        {
            _seek.Height = seek;
        }

        if (_lblTime.Height != seek)
        {
            _lblTime.Height = seek;
        }

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
    /// A number box as wide as its largest value and its arrows need, in the
    /// page's face - which the box wears - and never narrower than a number
    /// box was at the normal text size.
    /// </summary>
    private int NumberWidth(NumericUpDown box)
    {
        var digits = decimal.Truncate(box.Maximum).ToString(System.Globalization.CultureInfo.InvariantCulture).Length
            + (box.DecimalPlaces > 0 ? box.DecimalPlaces + 1 : 0);

        return Math.Max(
            UiLayout.AtTextSize(UiLayout.NumericWidth),
            TextRenderer.MeasureText(new string('0', digits), Font).Width + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra);
    }

    /// <summary>
    /// The clock's column: as wide as the longest time it can show - three
    /// figures of minutes a side, every figure the widest the face has, since
    /// a 1 is narrower than a 0 in it - so the seek bar never moves as the
    /// time counts up. Kept until the clock's face changes (a new text size),
    /// since the column is asked for its width whenever it is placed.
    /// </summary>
    private int TimeWidth()
    {
        var font = _lblTime.Font;

        if (_timeWidth is { } kept && ReferenceEquals(kept.Font, font))
        {
            return kept.Width;
        }

        var widest = '0';
        var widestWidth = 0;

        foreach (var figure in "0123456789")
        {
            var width = TextRenderer.MeasureText(figure.ToString(), font, Size.Empty, TextFormatFlags.NoPadding).Width;

            if (width > widestWidth)
            {
                widest = figure;
                widestWidth = width;
            }
        }

        var minutes = new string(widest, 3);
        var seconds = new string(widest, 2);
        var needed = TextRenderer.MeasureText($"{minutes}:{seconds} / {minutes}:{seconds}", font).Width + _lblTime.Padding.Horizontal;

        _timeWidth = (font, needed);
        return needed;
    }

    /// <summary>
    /// Brings the page up to date after something else changed the music
    /// settings or the applied soundscape - Soundscapes' Apply, a venue.
    /// </summary>
    public void RefreshAll()
    {
        _syncing = true;
        try
        {
            _rdoThemeMusic.Checked = _store.Current.PlayThemeMusic == true;
            _rdoMyMusic.Checked = _store.Current.PlayThemeMusic != true;
        }
        finally
        {
            _syncing = false;
        }

        RefreshSource();
        RefreshGrid();
        RefreshNowPlaying(_player.NowPlaying);
    }

    /// <summary>
    /// Switches between the soundscape's music and the club's own. Music
    /// that is playing moves over straight away - the switch is heard, not
    /// just saved - and paused music is stopped, so Play starts the new
    /// choice rather than resuming the old one.
    /// </summary>
    private void SetPlayThemeMusic(bool theme)
    {
        if (_syncing || _store.Current.PlayThemeMusic == theme)
        {
            return;
        }

        _store.Current.PlayThemeMusic = theme;
        _store.Save();
        _player.Settings = _store.Current;

        if (_player.IsPaused)
        {
            _player.Stop();
        }
        else if (_player.IsPlaying)
        {
            // The queue is built when play starts.
            _player.Stop();
            _player.Play();
        }

        RefreshSource();
        RefreshGrid();
        RefreshNowPlaying(_player.NowPlaying);
    }

    /// <summary>
    /// The sentence under the Theme music / My music choice, saying what it
    /// plays - and, when Theme music has nothing to play, that the club's
    /// own music plays instead and why. Falling back to the club's music is
    /// the friendlier of the two answers: a room going quiet because the
    /// soundscape happens to have no tracks is the kind of surprise nobody
    /// traces back to this page.
    ///
    /// What each choice would say is worked out whichever is chosen, and the
    /// line is kept as tall as the longer of the two (SteadyLabel), so
    /// switching between them never moves the player under it.
    /// </summary>
    private void RefreshSource()
    {
        var theme = _player.Theme;
        var themeTracks = theme?.TracksIn(_store.Current.Tracks) ?? new List<MusicTrack>();
        var fallback = false;

        string themeText;

        if (theme is null)
        {
            themeText = "No soundscape has been applied yet, so there is no theme music - your music plays instead. "
                        + "Apply one on Sounds \u2192 Soundscapes.";
            fallback = true;
        }
        else if (theme.Titles.Count == 0)
        {
            themeText = $"The {theme.SoundscapeName} soundscape has no music of its own - your music plays instead.";
            fallback = true;
        }
        else if (themeTracks.Count == 0)
        {
            themeText = $"The {theme.SoundscapeName} soundscape's music isn't in your library (or its file is missing) - "
                        + "your music plays instead. Restore Included Tracks puts the shipped tunes back.";
            fallback = true;
        }
        else
        {
            themeText = (themeTracks.Count == 1
                            ? $"Theme music: \"{themeTracks[0].DisplayName}\" from the {theme.SoundscapeName} soundscape."
                            : $"Theme music: {themeTracks.Count} tracks from the {theme.SoundscapeName} soundscape.")
                        + " Your own ticks under My music are kept for when you switch back.";
        }

        var myText = "My music: the tracks ticked Yes under My music below - your own uploads and the included tunes.";

        if (theme is not null && themeTracks.Count > 0)
        {
            myText += $" The {theme.SoundscapeName} soundscape's music is under Theme music.";
        }

        var playTheme = _store.Current.PlayThemeMusic == true;
        var wordings = new[] { themeText, myText };
        var reworded = !_lblSource.Wordings.SequenceEqual(wordings);

        _lblSource.Wordings = wordings;
        _lblSource.Text = playTheme ? themeText : myText;
        _lblSource.ForeColor = playTheme && fallback ? AppTheme.Warning : AppTheme.TextMuted;

        // A new wording for the choice NOT made - another soundscape applied
        // while My music plays - can change how tall the line is kept without
        // changing what it says, and only a change to what it says tells the
        // page. So the page is told.
        if (reworded)
        {
            _lblSource.Parent?.PerformLayout(_lblSource, nameof(PreferredSize));
        }
    }

    /// <summary>Whether a track is in what plays now: the theme's tracks with Theme music in use, the ticked ones otherwise.</summary>
    private bool InPlayingList(MusicTrack track) => InPlayingList(track, _player.ThemeMusicInUse);

    /// <summary>The same, with ThemeMusicInUse worked out once by a caller going through every row.</summary>
    private bool InPlayingList(MusicTrack track, bool themeInUse) =>
        themeInUse ? IsThemeTrack(track) : track.Enabled;

    private bool IsThemeTrack(MusicTrack track) =>
        _player.Theme is { } theme
        && theme.Titles.Contains(track.DisplayName.Trim(), StringComparer.OrdinalIgnoreCase);

    private void UpdateEnabledState()
    {
        var on = _chkEnabled.Checked;

        _numVolume.Enabled = on;
        _chkShuffle.Enabled = on;
        _btnPlayStop.Enabled = on;
        _btnSkip.Enabled = on && _player.IsPlaying;
        _chkPauseDuringRaces.Enabled = on;
        _chkDuck.Enabled = on;
        _numDuckTo.Enabled = on && _chkDuck.Checked;

        // The library stays usable when music is off - the same deadlock
        // the ambience page had, where you couldn't set up the content
        // without first enabling the feature that needs it.
        _grid.Enabled = true;
    }

    private void TogglePlay()
    {
        if (_player.IsPaused)
        {
            _player.Resume();
        }
        else if (_player.IsPlaying && _player.NowPlaying is not null)
        {
            // Pause rather than stop: Stop has its own button now, and
            // losing your place in a track to a mis-click is annoying.
            //
            // Only with a track showing - the same test that gives the
            // button its "Pause" caption (RefreshNowPlaying). The player
            // also counts as on in the moment after a file fails to play,
            // while the next one is tried; if nothing has played yet the
            // button still reads "Play" then, and must not pause instead.
            // Play does nothing while the player is on, and the next track
            // follows.
            _player.Pause();
        }
        else
        {
            _player.Settings = _store.Current;
            _player.Play();
        }

        RefreshNowPlaying(_player.NowPlaying);
    }

    /// <summary>Moves the seek bar and clock while a track plays.</summary>
    private void TickPosition()
    {
        var (position, duration) = _player.CurrentPosition;

        if (duration <= TimeSpan.Zero)
        {
            _seek.Enabled = false;
            _lblTime.Text = "0:00 / 0:00";
            return;
        }

        _seek.Enabled = true;
        _lblTime.Text = $"{Format(position)} / {Format(duration)}";

        // Skipped while dragging, or the thumb would snap back under the
        // user's mouse twice a second.
        if (!_seeking)
        {
            var value = (int)Math.Clamp(position.TotalSeconds / duration.TotalSeconds * 1000, 0, 1000);
            if (value != _seek.Value)
            {
                _seek.Value = value;
            }
        }

        static string Format(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }

    private void RefreshNowPlaying(MusicTrack? track)
    {
        var playing = _player.IsPlaying && track is not null;

        _btnPlayStop.Text = _player.IsPaused ? "Resume" : playing ? "Pause" : "Play";

        var active = _chkEnabled.Checked && playing;
        _btnSkip.Enabled = active;
        _btnPrevious.Enabled = active;

        // Whenever the player is on, track or no track: after a file that
        // would not play, with the next one still being tried, Stop is the
        // one button that has to work - it used to be greyed out then.
        _btnStop.Enabled = _chkEnabled.Checked && _player.IsPlaying;
        _seek.Enabled = active;

        // Says where the music comes from when it is the soundscape's, so
        // a theme track playing is never mistaken for one of the club's own.
        _lblNowPlaying.Text = !playing
            ? "Not playing."
            : _player.ThemeMusicInUse && IsThemeTrack(track!) && _player.Theme is { } theme
                // Kept short: this line shares the transport buttons' line
                // and ends in "..." when it runs out of room there, and the
                // sentence above the box already names the soundscape in full.
                ? $"Now playing:  {track!.DisplayName}  (theme music, {theme.SoundscapeName})"
                : $"Now playing:  {track!.DisplayName}";
        _lblNowPlaying.ForeColor = playing ? AppTheme.Success : AppTheme.TextMuted;

        MarkPlayingRow();
    }

    /// <summary>
    /// Marks the playing track's row - bold, in the "playing" colour, with
    /// "(now playing)" after its name - and puts every other row back.
    ///
    /// Done on the rows already there rather than by rebuilding the list,
    /// which would lose a multi-row selection and the scroll position every
    /// time a track ended.
    /// </summary>
    private void MarkPlayingRow()
    {
        var playingId = _player.IsPlaying ? _player.NowPlaying?.Id : null;
        var themeInUse = _player.ThemeMusicInUse;

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is not string id || _store.Current.Tracks.FirstOrDefault(t => t.Id == id) is not { } track)
            {
                continue;
            }

            StyleRow(row, track, File.Exists(track.FilePath), id == playingId, themeInUse);
        }
    }

    private void StyleRow(DataGridViewRow row, MusicTrack track, bool exists, bool playing, bool themeInUse)
    {
        row.Cells[0].Value = playing ? $"{track.DisplayName}  (now playing)" : track.DisplayName;
        row.DefaultCellStyle.Font = playing ? AppTheme.BoldFont : null;
        row.DefaultCellStyle.ForeColor = playing
            ? AppTheme.Success
            : exists && !InPlayingList(track, themeInUse) ? AppTheme.TextMuted : Color.Empty;
    }

    /// <summary>Every selected track, falling back to the current row.</summary>
    private List<MusicTrack> SelectedMany()
    {
        var ids = _grid.SelectedRows
            .Cast<DataGridViewRow>()
            .OrderBy(r => r.Index)
            .Select(r => r.Tag as string)
            .Where(id => id is not null)
            .ToList();

        if (ids.Count == 0 && _grid.CurrentRow?.Tag as string is { } currentId)
        {
            ids.Add(currentId);
        }

        return ids
            .Select(id => _store.Current.Tracks.FirstOrDefault(t => t.Id == id))
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();
    }

    private MusicTrack? Selected() =>
        _grid.CurrentRow?.Tag is string id
            ? _store.Current.Tracks.FirstOrDefault(t => t.Id == id)
            : null;

    private void RefreshGrid()
    {
        var selectedId = _grid.CurrentRow?.Tag as string;
        var themeInUse = _player.ThemeMusicInUse;
        _grid.Rows.Clear();

        foreach (var track in _store.Current.Tracks)
        {
            var exists = File.Exists(track.FilePath);

            // The applied soundscape's tracks say so, whichever music is
            // chosen - it is how the list shows which ones "Theme music"
            // means without moving or re-ticking anything in it.
            var rowIndex = _grid.Rows.Add(
                track.DisplayName,
                (track.IsBuiltIn ? "Included" : "Yours") + (IsThemeTrack(track) ? "  \u2022  theme" : string.Empty),
                track.Enabled ? "Yes" : "No",
                exists ? Path.GetFileName(track.FilePath) : $"MISSING: {Path.GetFileName(track.FilePath)}");

            _grid.Rows[rowIndex].Tag = track.Id;

            if (!exists)
            {
                _grid.Rows[rowIndex].Cells[3].Style.ForeColor = AppTheme.Danger;
            }

            StyleRow(_grid.Rows[rowIndex], track, exists, _player.IsPlaying && _player.NowPlaying?.Id == track.Id, themeInUse);

            if (selectedId == track.Id)
            {
                _grid.Rows[rowIndex].Selected = true;
                _grid.CurrentCell = _grid.Rows[rowIndex].Cells[0];
            }
        }

        var playable = _store.Current.Tracks.Count(t => t.Enabled && File.Exists(t.FilePath));
        _lblStatus.Text = _store.Current.Tracks.Count == 0
            ? "No tracks yet - press Add Track to bring in your own."
            : themeInUse
                ? $"Theme music is chosen. {playable} of {Plural.Of(_store.Current.Tracks.Count, "track")} ticked for My music."
                : $"{playable} of {Plural.Of(_store.Current.Tracks.Count, "track")} in My music.";
        _lblStatus.ForeColor = !themeInUse && playable == 0 && _store.Current.Tracks.Count > 0 ? AppTheme.Danger : AppTheme.TextMuted;

        // Rebuilding the list, or ticking a track in or out, can change what
        // "Theme music" finds to play - so the sentence saying so follows.
        RefreshSource();
    }

    private void AddTracks()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Add music tracks",
            Filter = "Audio files (*.wav;*.mp3)|*.wav;*.mp3|All files (*.*)|*.*",
            Multiselect = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var addedIds = new HashSet<string>();
        var copiedIn = new List<string>();
        var failed = 0;

        foreach (var path in dialog.FileNames)
        {
            if (_store.AddTrack(path) is { } track)
            {
                addedIds.Add(track.Id);

                // AddTrack copies the file into the music folder unless it
                // was picked from there already (the same test it makes).
                // Only a copy it made is the undo's to take away again.
                if (!string.Equals(track.FilePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    copiedIn.Add(track.FilePath);
                }
            }
            else
            {
                failed++;
            }
        }

        var added = addedIds.Count;

        if (added > 0)
        {
            // Only the tracks this added come out again - a whole-library
            // snapshot would also undo every tick, volume or removal made
            // since. Store and player only; the page follows via Restored.
            var store = _store;
            var player = _player;
            _undo.Capture(added == 1 ? "Add music track" : $"Add {added} music tracks", () =>
            {
                if (player.NowPlaying is { } playing && addedIds.Contains(playing.Id))
                {
                    player.Stop();
                }

                store.Current.Tracks.RemoveAll(t => addedIds.Contains(t.Id));
                store.Save();

                // The copies go too - set aside, not deleted. Left in the
                // music folder, the start-up check lists every audio file it
                // finds there, so the tracks came back at the next launch.
                // Only a file still inside the music folder that no track
                // points at now.
                string? holdIn = null;
                foreach (var copy in copiedIn)
                {
                    if (PlainFileName.IsInside(MusicStore.MusicDirectory, copy)
                        && !store.Current.Tracks.Any(t => string.Equals(t.FilePath, copy, StringComparison.OrdinalIgnoreCase)))
                    {
                        holdIn ??= UndoHoldingFolder.NewFolder();
                        UndoHoldingFolder.MoveIn(copy, holdIn);
                    }
                }

                player.Settings = store.Current;
                store.NotifyRestored();
            });
        }

        _player.Settings = _store.Current;
        RefreshGrid();

        if (failed > 0)
        {
            AppDialog.Show(this,
                $"{Plural.Of(failed, "file")} couldn't be copied into the music folder. They may be in use or read-only.",
                "Some tracks not added", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ToggleSelectedEnabled()
    {
        if (Selected() is not { } track)
        {
            return;
        }

        track.Enabled = !track.Enabled;
        _store.Save();
        _player.Settings = _store.Current;
        RefreshGrid();
    }

    private void RemoveSelected()
    {
        var selected = SelectedMany();

        if (selected.Count == 0)
        {
            return;
        }

        var track = selected[0];
        var isPlaying = selected.Any(t => _player.NowPlaying?.Id == t.Id);
        var anyBuiltIn = selected.Any(t => t.IsBuiltIn);

        var what = selected.Count == 1
            ? $"\"{track.DisplayName}\""
            : $"these {selected.Count} tracks";

        var message = anyBuiltIn
            ? $"Remove {what} from the library?\r\n\r\n"
              + "Included tracks will come back next time the app starts unless you "
              + "delete the file from the music folder."
            : $"Remove {what} from the library?\r\n\r\n"
              + "The files stay in the music folder - use Open Music Folder to delete them for good.";

        if (AppDialog.Show(this, message, "Remove track", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var undoLabel = selected.Count == 1
            ? $"Remove music track {track.DisplayName}"
            : $"Remove {selected.Count} music tracks";

        var removeIds = selected.Select(t => t.Id).ToHashSet();

        // Each removed track with the place it held, so undo puts it back
        // there and leaves everything changed since alone. The files were
        // never touched, so the entries are all there is to restore.
        var removed = _store.Current.Tracks
            .Select((t, i) => (Index: i, Track: t))
            .Where(r => removeIds.Contains(r.Track.Id))
            .ToList();

        var store = _store;
        var player = _player;
        _undo.Capture(undoLabel, () =>
        {
            store.PutBack(removed);
            player.Settings = store.Current;
            store.NotifyRestored();
        });
        _store.Current.Tracks.RemoveAll(t => removeIds.Contains(t.Id));
        _store.Save();
        _player.Settings = _store.Current;

        // Skip off a track that was just removed, rather than letting it
        // finish playing from a library it's no longer in.
        if (isPlaying)
        {
            _player.Skip();
        }

        RefreshGrid();
    }

    /// <summary>
    /// Re-runs the bundled-track install. The tracks ship with the app and
    /// are copied in on first run; if that didn't happen - a build that
    /// flattened them, a settings file saved before they arrived - this is
    /// the way back without hunting through folders.
    /// </summary>
    private void RestoreIncluded()
    {
        var before = _store.Current.Tracks.Count;

        _store.ReinstallBundledTracks();
        _player.Settings = _store.Current;
        RefreshGrid();

        var added = _store.Current.Tracks.Count - before;

        if (added > 0)
        {
            _lblStatus.Text = $"Added {Plural.Of(added, "included track")}.";
            _lblStatus.ForeColor = AppTheme.Success;
        }
        else
        {
            // Shows exactly where it looked and what was there. "Nothing
            // found" on its own is a dead end - this distinguishes a build
            // that didn't include the files from one where they're present
            // but already listed.
            // If the library already has the shipped set, say so plainly -
            // reporting "nothing found" while eleven of them sit visible in
            // the list behind the dialog is just confusing.
            var alreadyHave = _store.Current.Tracks.Count(t => MusicStore.IsBundledTitle(t.DisplayName));

            if (alreadyHave > 0)
            {
                AppDialog.Show(this,
                    $"Nothing to add - {Plural.Of(alreadyHave, "included track")} {(alreadyHave == 1 ? "is" : "are")} already in your library.",
                    "Already up to date", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            AppDialog.Show(this,
                "No included tracks were added.\r\n\r\n"
                + "Here's where the app looked:\r\n\r\n"
                + _store.DescribeBundledSearch()
                + "\r\n\r\nIf every location shows NOT FOUND, the audio files didn't reach your build - "
                + "check that Assets\\Music exists in the project folder. You can always add your own with Add Track.",
                "Nothing to restore", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    /// <summary>
    /// Empties the library listing, optionally deleting the audio files
    /// too.
    ///
    /// Exists because a library can accumulate duplicates - the same
    /// soundtrack added twice in different formats, say - and removing
    /// twenty-odd entries one at a time to start clean is no way to spend
    /// an evening. The file deletion is opt-in and asked about separately,
    /// since losing someone's own uploaded music would be unrecoverable.
    /// </summary>
    private void ClearLibrary()
    {
        if (_store.Current.Tracks.Count == 0)
        {
            AppDialog.Show(this, "The library is already empty.", "Nothing to clear",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var count = _store.Current.Tracks.Count;

        if (AppDialog.Show(this,
                $"Remove all {Plural.Of(count, "track")} from the library?\r\n\r\n"
                + "The audio files stay in the music folder unless you choose to delete them next.",
                "Clear library", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var alsoDelete = AppDialog.Show(this,
            "Also delete the audio files from the music folder?\r\n\r\n"
            + "Choose No to keep them - you can re-add them later with Add Track. "
            + $"Either way Undo brings the library back; deleted files are held for {UndoHoldingFolder.KeepDays} days so Undo can return them too, "
            + "then they are gone for good. A file that can't be held (locked, say) is deleted outright and won't come back. "
            + "Files outside the music folder, or on another drive, are left where they are.",
            "Delete files too?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;

        // Every entry with its place, and - when the files go too - where
        // each file was held rather than deleted. Undo used to be offered
        // only when the files stayed; they are now moved to the undo
        // holding folder instead of deleted, so both cases come back.
        var removed = _store.Current.Tracks.Select((t, i) => (Index: i, Track: t)).ToList();
        var heldFiles = new List<(string Held, string Original)>();
        var holdIn = alsoDelete ? UndoHoldingFolder.NewFolder() : null;

        var store = _store;
        var player = _player;
        _undo.Capture(count == 1 ? "Clear music library (1 track)" : $"Clear music library ({count} tracks)", () =>
        {
            // Files first, so the entries come back pointing at something.
            // One the club has put back by hand since is kept as it is.
            foreach (var (held, original) in heldFiles)
            {
                UndoHoldingFolder.MoveBack(held, original);
            }

            store.PutBack(removed);
            player.Settings = store.Current;
            store.NotifyRestored();
        });

        // Stop first - moving the file out from under a playing track leaves
        // the player holding a handle to something that no longer exists.
        _player.Stop();

        var leftAlone = 0;
        if (alsoDelete && holdIn is not null)
        {
            foreach (var track in _store.Current.Tracks)
            {
                // Only files in the music folder. A track's path comes from
                // music.json, which a restored backup or a hand edit can
                // point anywhere - "Delete files too" must never reach a
                // file in Documents, or ask a server on the network whether
                // a file exists (which hands it this account's password
                // hash). IsInside comes first so nothing outside is touched,
                // and a network path can only pass it when the music folder
                // itself is on the network (a redirected %AppData%).
                if (string.IsNullOrEmpty(track.FilePath)
                    || !PlainFileName.IsInside(MusicStore.MusicDirectory, track.FilePath))
                {
                    leftAlone++;
                    continue;
                }

                // Held only when that is a rename, as for a deleted backup
                // (BackupRestoreControl): the holding folder is beside the
                // app's data, and a file on another drive would be copied
                // across in full, here on the UI thread, freezing the page
                // for as long as a big library takes. The music folder is
                // beside the holding folder, so this only turns away a path
                // that names another drive - that file is left alone rather
                // than deleted without an undo, as the question said.
                if (!string.Equals(
                        Path.GetPathRoot(Path.GetFullPath(track.FilePath)),
                        Path.GetPathRoot(Path.GetFullPath(UndoHoldingFolder.RoamingRoot)),
                        StringComparison.OrdinalIgnoreCase))
                {
                    leftAlone++;
                    continue;
                }

                // Held for undo rather than deleted outright. Only when it
                // can't be moved is it deleted as asked - that one file then
                // won't come back with an undo, which the question says.
                if (UndoHoldingFolder.MoveIn(track.FilePath, holdIn) is { } held)
                {
                    heldFiles.Add((held, track.FilePath));
                    continue;
                }

                try
                {
                    if (File.Exists(track.FilePath))
                    {
                        File.Delete(track.FilePath);
                    }
                }
                catch
                {
                    // A locked or read-only file shouldn't abort the rest.
                }
            }
        }

        _store.Current.Tracks.Clear();
        _store.Save();
        _player.Settings = _store.Current;
        RefreshGrid();

        _lblStatus.Text = !alsoDelete
            ? $"Cleared {Plural.Of(count, "track")} - files kept in the music folder."
            : leftAlone > 0
                ? $"Cleared {Plural.Of(count, "track")} and deleted their files - {Plural.Of(leftAlone, "file")} outside the music folder or on another drive left alone."
                : $"Cleared {Plural.Of(count, "track")} and deleted their files.";
        _lblStatus.ForeColor = AppTheme.Success;
    }

    private void OpenMusicFolder()
    {
        try
        {
            Directory.CreateDirectory(MusicStore.MusicDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = MusicStore.MusicDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't open the folder:\r\n\r\n{ex.Message}",
                "Open music folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _player.NowPlayingChanged -= OnNowPlayingChanged;
            _store.Restored -= OnStoreRestored;
            _tick.Stop();
            _tick.Dispose();
        }

        base.Dispose(disposing);
    }
}
