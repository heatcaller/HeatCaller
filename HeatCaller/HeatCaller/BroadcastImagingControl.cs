namespace HeatCaller;

/// <summary>
/// Sounds -> Broadcast Imaging (16 Oct 2026): the jingles, sweepers,
/// bumpers, stingers and music beds each soundscape plays to make race night
/// sound like an FM station, and the run of show that decides where each one
/// fires.
///
/// Every soundscape has its own pieces - the ones it ships with, until the
/// club changes them - and Restore Defaults puts them back, one element or
/// the whole soundscape. The run of show is one for the club, whichever
/// soundscape is in use. Where several files could play at a point, one is
/// picked at random and none repeats until all have played - see
/// BroadcastImaging.
///
/// THE SOUNDS REGROUP (17 Oct 2026): the pieces' and beds' levels moved to
/// the Mixer, where every level and duck now lives - Post timing stays
/// here, beside the marks it nudges. And the club's own show music moved
/// here from the Commentary page as the Show treatment card
/// (ShowTreatmentCard), since it is what plays where the soundscape has no
/// imaging. The ON/OFF tick now greys the imaging part of the page, not
/// only its own caption.
/// </summary>
public sealed class BroadcastImagingControl : UserControl
{
    private readonly ThemeStore _themes;

    private readonly CheckBox _chkEnabled = new ThemedCheckBox();
    private readonly ComboBox _cmbSoundscape = new();
    private readonly Label _lblSoundscape = new();
    private readonly DataGridView _grid = new();
    private readonly ComboBox _cmbElement = new();
    private readonly Label _lblStatus = new();

    /// <summary>The club's own show music, the last card on the page - see ShowTreatmentCard.</summary>
    private readonly ShowTreatmentCard _showTreatment;

    /// <summary>The imaging cards the ON/OFF tick greys - the soundscape, its pieces, the run of show and the post timing.</summary>
    private readonly List<Control> _imagingCards = new();
    /// <summary>The selected piece's OUT - the mark the app called its post until 17 Oct 2026. See TalkUp.</summary>
    private readonly NumericUpDown _numPost = new();
    private readonly Label _lblPostSource = new();

    /// <summary>The selected piece's POST - where its own voice comes in (17 Oct 2026). See TalkUp.</summary>
    private readonly NumericUpDown _numTalkUp = new();
    private readonly Label _lblTalkUpRoom = new();

    /// <summary>The nudge on every post, in seconds - see BroadcastImaging.PostNudge.</summary>
    private readonly NumericUpDown _numNudge = new();

    /// <summary>
    /// Where the preview playing now started in the piece, and when - so
    /// Mark Post and Mark Out can read off where it has got to.
    /// </summary>
    private (string Path, double From, DateTime StartedUtc)? _previewAt;

    /// <summary>
    /// Taken off a mark made by pressing a button while listening - about
    /// how long it takes to hear the moment and press. Marks a touch late
    /// otherwise, which on a post is the piece's voice landing on the last
    /// word instead of after it.
    /// </summary>
    private const double ReactionSeconds = 0.15;

    /// <summary>One picker per point in the run of show.</summary>
    private readonly Dictionary<ImagingCue, ComboBox> _cuePickers = new();

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    private (Font Font, int Count, int Width)? _soundscapeWidth;
    private (Font Font, int Count, int Width)? _elementWidth;
    private readonly Dictionary<ImagingCue, (Font Font, int Count, int Width)?> _cueWidths = new();

    /// <summary>The file the Play button is playing, so Stop - or leaving the page - can stop it.</summary>
    private CancellationTokenSource? _preview;

    /// <summary>Set while the page fills its own controls, so filling them isn't taken for a change.</summary>
    private bool _filling;

    /// <summary>The fewest pieces the list shows before the page scrolls instead.</summary>
    private const int GridLeastRows = 6;

    /// <summary>A soundscape in the picker, marked when it is the one in use.</summary>
    private sealed record SoundscapeItem(Theme Theme, bool InUse)
    {
        public override string ToString() => InUse ? $"{Theme.Name}   (in use)" : Theme.Name;
    }

    /// <summary>An element in the Add picker.</summary>
    private sealed record ElementItem(ImagingElement Element)
    {
        public override string ToString() => BroadcastImaging.Describe(Element);
    }

    /// <summary>A choice in a run-of-show picker.</summary>
    private sealed record ChoiceItem(ImagingChoice Choice)
    {
        public override string ToString() => BroadcastImaging.Describe(Choice);
    }

    /// <summary>What a row of the list is.</summary>
    private sealed record PieceRow(ImagingElement Element, string Path);

    public BroadcastImagingControl(ThemeStore themes, AppSettings settings)
    {
        _themes = themes;

        Dock = DockStyle.Fill;
        ForeColor = AppTheme.TextDark;

        _textSize.Wear(this, () => AppTheme.BodyFont);

        _showTreatment = new ShowTreatmentCard(settings, _textSize);

        BuildLayout();
        RefreshAll();

        BroadcastImaging.Changed += OnImagingChanged;
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
    }

    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>
    /// The Show treatment card, for the host to hook up to the announcer -
    /// see MainForm.Commentary.cs, WireShowMusic.
    /// </summary>
    internal ShowTreatmentCard ShowTreatment => _showTreatment;

    /// <summary>
    /// A preview left playing when the page is put away is stopped with it.
    /// Shown, the Show treatment card is set out again: the commentary
    /// switch and the soundscape in use are changed on other pages.
    /// </summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (!Visible)
        {
            StopPreview();
        }
        else
        {
            _showTreatment.RefreshAll();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            BroadcastImaging.Changed -= OnImagingChanged;
            StopPreview();
        }

        base.Dispose(disposing);
    }

    private void OnImagingChanged()
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(OnImagingChanged));
            }
            catch (InvalidOperationException)
            {
                // The page is closing.
            }

            return;
        }

        RefreshPieces();
        RefreshRunOfShow();

        // Switching imaging on or off, or a soundscape gaining or losing
        // its pieces, decides whether the club's own pieces are folded away.
        _showTreatment.RefreshImagingFold();
    }

    private void BuildLayout()
    {
        // (22 Oct 2026, page tidy): one caption column down the page
        // (FieldGrid.LineUp, at the end) - the five grids each had their own,
        // so no two cards' fields started at the same x. The Show treatment
        // card's captions are a step in under its switch, so these keep the
        // step on their right instead, as CommentaryControl's Flush does.
        static Label Flush(Label caption)
        {
            caption.Margin = new Padding(caption.Margin.Left, caption.Margin.Top, caption.Margin.Right + UiLayout.Indent, caption.Margin.Bottom);
            return caption;
        }

        var lblIntro = new Label
        {
            Text = "BROADCAST IMAGING. Jingles, sweepers, bumpers, stingers and music beds that make race night sound like a radio station. "
                 + "Each soundscape has its own (several of a kind take turns at random); Run of show below says where each one plays.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _chkEnabled.AutoSize = true;
        _textSize.Wear(_chkEnabled, () => AppTheme.BoldFont);
        _chkEnabled.CheckedChanged += (_, _) =>
        {
            if (_filling)
            {
                return;
            }

            BroadcastImaging.Enabled = _chkEnabled.Checked;
            UpdateEnabledLook();
        };

        var lblEnabledHint = new Label
        {
            Text = "Off: no imaging plays, and the club's own show music below plays where it is set up. "
                 + "On: On Air on Race Control starts the show; the rest follows the race on its own.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblEnabledHint, () => AppTheme.SmallFont);

        // --- The soundscape ----------------------------------------------

        _cmbSoundscape.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbSoundscape.SelectedIndexChanged += (_, _) =>
        {
            if (!_filling)
            {
                StopPreview();
                RefreshPieces();
            }
        };

        _lblSoundscape.AutoSize = true;
        _lblSoundscape.ForeColor = AppTheme.TextMuted;
        _textSize.Wear(_lblSoundscape, () => AppTheme.SmallFont);

        var soundscapeGrid = new FieldGrid();
        Flush(soundscapeGrid.AddRow("Soundscape:", FieldGrid.Inline(_cmbSoundscape, _lblSoundscape)));
        soundscapeGrid.AddHint(
            "Race night plays the soundscape in use on the Soundscapes tab; any of them can be set up here.");

        // --- The pieces --------------------------------------------------

        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "element", HeaderText = "Element", FillWeight = 40 },
            new DataGridViewTextBoxColumn { Name = "file", HeaderText = "File", FillWeight = 120 },
            new DataGridViewTextBoxColumn { Name = "post", HeaderText = "Post", FillWeight = 40 },
            new DataGridViewTextBoxColumn { Name = "out", HeaderText = "Out", FillWeight = 40 },
            new DataGridViewTextBoxColumn { Name = "from", HeaderText = "From", FillWeight = 40 });
        _grid.SelectionChanged += (_, _) => ShowPost();
        _grid.CellDoubleClick += (_, args) =>
        {
            if (args.RowIndex >= 0)
            {
                PlaySelected();
            }
        };

        _textSize.Keep(() => Pieces.KeepHeadingsWhole(_grid));

        _cmbElement.DropDownStyle = ComboBoxStyle.DropDownList;

        foreach (var element in Enum.GetValues<ImagingElement>())
        {
            _cmbElement.Items.Add(new ElementItem(element));
        }

        _cmbElement.SelectedIndex = 0;

        var btnAdd = new Button { Text = "Add Files..." };
        btnAdd.Click += (_, _) => AddFiles();

        var btnPlay = new Button { Text = "Play" };
        btnPlay.Click += (_, _) => PlaySelected();

        var btnStop = new Button { Text = "Stop" };
        btnStop.Click += (_, _) => StopPreview();

        // TWO MARKS PER PIECE (17 Oct 2026). The POST is where the piece's
        // own voice or singing comes in: the announcer talks over the intro
        // in front of it and stops right on it - hitting the post. The OUT
        // is where the piece's own voice stops: the next line starts on it.
        // Until today the page called the out "the post". See TalkUp.
        _numTalkUp.DecimalPlaces = 2;
        _numTalkUp.Increment = 0.05m;
        _numTalkUp.Minimum = 0;
        _numTalkUp.Maximum = (decimal)ShowClip.MostSeconds;

        _lblTalkUpRoom.AutoSize = true;
        _lblTalkUpRoom.ForeColor = AppTheme.TextMuted;
        _textSize.Wear(_lblTalkUpRoom, () => AppTheme.SmallFont);

        var btnMarkTalkUp = new Button { Text = "Mark Post" };
        btnMarkTalkUp.Click += (_, _) => MarkWhilePlaying(post: true);

        // "Set Post" / "Set Out" and one "Reset" for both (17 Oct 2026):
        // two buttons both called Set, and a Clear beside an Auto that did
        // the same kind of job, read as four different things.
        var btnSetTalkUp = new Button { Text = "Set Post" };
        btnSetTalkUp.Click += (_, _) => SetTalkUp();

        var btnClearTalkUp = new Button { Text = "Reset" };
        btnClearTalkUp.Click += (_, _) => ClearTalkUp();

        var btnPlayToTalkUp = new Button { Text = "Play to Post" };
        btnPlayToTalkUp.Click += (_, _) => PlayToTalkUp();

        _numPost.DecimalPlaces = 2;
        _numPost.Increment = 0.05m;
        _numPost.Minimum = 0;
        _numPost.Maximum = (decimal)ShowClip.MostSeconds;

        _lblPostSource.AutoSize = true;
        _lblPostSource.ForeColor = AppTheme.TextMuted;
        _textSize.Wear(_lblPostSource, () => AppTheme.SmallFont);

        var btnMarkOut = new Button { Text = "Mark Out" };
        btnMarkOut.Click += (_, _) => MarkWhilePlaying(post: false);

        var btnSetPost = new Button { Text = "Set Out" };
        btnSetPost.Click += (_, _) => SetPost();

        var btnAutoPost = new Button { Text = "Reset" };
        btnAutoPost.Click += (_, _) => ClearPost();

        var btnPlayPost = new Button { Text = "Play from Out" };
        btnPlayPost.Click += (_, _) => PlayFromOut();

        var postGrid = new FieldGrid();
        Flush(postGrid.AddRow("Post at:", FieldGrid.Inline(_numTalkUp, new Label { Text = "s" }, btnMarkTalkUp, btnSetTalkUp, btnClearTalkUp, btnPlayToTalkUp, _lblTalkUpRoom)));
        postGrid.AddHint(
            "POST: where the piece's own voice comes in - the announcer talks over the intro and stops right on it. "
            + "Press Play, then Mark Post when you hear the voice; Reset means no intro (right for a stinger).");
        Flush(postGrid.AddRow("Out at:", FieldGrid.Inline(_numPost, new Label { Text = "s" }, btnMarkOut, btnSetPost, btnAutoPost, btnPlayPost, _lblPostSource)));
        postGrid.AddHint(
            "OUT: where the piece's voice ends and only music is left - the next line starts there as the music fades. "
            + "Mark it the same way; Reset goes back to the piece's own mark.");

        var btnRemove = new Button { Text = "Remove" };
        btnRemove.Click += (_, _) => RemoveSelected();

        var btnRestoreElement = new Button { Text = "Restore Element Defaults" };
        btnRestoreElement.Click += (_, _) => RestoreElement();

        var btnRestoreSoundscape = new Button { Text = "Restore Soundscape Defaults" };
        btnRestoreSoundscape.Click += (_, _) => RestoreSoundscape();

        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = AppTheme.TextMuted;

        var lblAddHint = new Label
        {
            Text = "Add Files copies the files into the club's data as the element picked beside it. "
                 + "Restore Element Defaults puts that element back to what the soundscape ships with.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblAddHint, () => AppTheme.SmallFont);

        var pieces = new Section("Pieces");

        pieces.AddFill(_grid, () => _grid.ColumnHeadersHeight + GridLeastRows * _grid.RowTemplate.Height + 2);
        pieces.Add(new ButtonBar().With(_cmbElement, btnAdd, btnPlay, btnStop, btnRemove, btnRestoreElement, btnRestoreSoundscape));
        pieces.Add(postGrid);
        pieces.Add(lblAddHint);
        pieces.Add(_lblStatus, gapBelow: 0);

        // --- The run of show ---------------------------------------------

        var runGrid = new FieldGrid();

        foreach (var cue in BroadcastImaging.Cues)
        {
            var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };

            foreach (var choice in BroadcastImaging.ChoicesFor(cue))
            {
                picker.Items.Add(new ChoiceItem(choice));
            }

            var at = cue;
            picker.SelectedIndexChanged += (_, _) =>
            {
                if (!_filling && picker.SelectedItem is ChoiceItem item)
                {
                    BroadcastImaging.SetChoice(at, item.Choice);
                }
            };

            _cuePickers[cue] = picker;
            _cueWidths[cue] = null;

            var when = new Label { Text = BroadcastImaging.When(cue), AutoSize = true, ForeColor = AppTheme.TextMuted };
            _textSize.Wear(when, () => AppTheme.SmallFont);

            Flush(runGrid.AddRow(BroadcastImaging.Describe(cue) + ":", FieldGrid.Inline(picker, when)));
        }

        var btnRestoreRun = new Button { Text = "Restore Run of Show Defaults" };
        btnRestoreRun.Click += (_, _) =>
        {
            BroadcastImaging.RestoreRunOfShow();
            RefreshRunOfShow();
        };

        var lblRunHint = new Label
        {
            Text = "Before the race (On Air on Race Control): jingle or sweeper  ->  the opening, over the opening bed  ->  throw to pit lane  ->  "
                 + "driver introductions  ->  bumper  ->  staging call. "
                 + "After the flag: the finish call  ->  over the after-the-flag bed, the result, the pit lane and the teaser  ->  bumper  ->  stop set (sponsors)  ->  jingle or sweeper  ->  recap open  ->  "
                 + "the recap host over the recap bed, a stinger between parts  ->  closing bumper. "
                 + "A point set to something the soundscape doesn't have plays nothing.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblRunHint, () => AppTheme.SmallFont);

        var run = new Section("Run of show");
        run.Add(lblRunHint);
        run.Add(runGrid);
        run.Add(new ButtonBar().With(btnRestoreRun), gapBelow: 0);

        // THE NUDGE (17 Oct 2026) - one setting for every post, for taste:
        // some like the piece's voice right on the last word, some a hair
        // under it.
        _numNudge.DecimalPlaces = 2;
        _numNudge.Increment = 0.05m;
        _numNudge.Minimum = (decimal)-BroadcastImaging.MostNudge;
        _numNudge.Maximum = (decimal)BroadcastImaging.MostNudge;
        _numNudge.ValueChanged += (_, _) =>
        {
            if (!_filling)
            {
                BroadcastImaging.PostNudge = (double)_numNudge.Value;
            }
        };

        var lblNudgeSuffix = new Label
        {
            Text = "s  - below zero the piece's voice comes in a touch early, above zero a touch late",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };
        _textSize.Wear(lblNudgeSuffix, () => AppTheme.SmallFont);

        // THE LEVELS ARE THE MIXER'S (17 Oct 2026): the pieces' level, the
        // beds' level and how far the beds drop under the voice moved there
        // with every other level and duck. The timing stays, beside the
        // marks it nudges.
        var levelGrid = new FieldGrid();
        Flush(levelGrid.AddRow("Post timing:", FieldGrid.Inline(_numNudge, lblNudgeSuffix)));
        levelGrid.AddHint("The pieces' and beds' levels are on the Mixer tab.");

        var levels = new Section("Timing");
        levels.Add(levelGrid, gapBelow: 0);

        // --- The page ----------------------------------------------------

        var top = new Section("Soundscape");
        top.Add(soundscapeGrid, gapBelow: 0);

        var page = new FillPage();

        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.Add(_chkEnabled, stretch: false, gapBelow: UiLayout.HintGap);
        page.Add(lblEnabledHint);
        page.Add(top);
        page.AddFill(pieces, pieces.LeastHeight);
        page.Add(run);
        page.Add(levels);
        page.Add(_showTreatment.Card, gapBelow: 0);

        _imagingCards.AddRange(new Control[] { top, pieces, run, levels });

        Controls.Add(page);

        // Every grid on the page, the Show treatment card's too.
        _textSize.Keep(() => FieldGrid.LineUp(new[] { soundscapeGrid, postGrid, runGrid, levelGrid, _showTreatment.Grid }));

        _textSize.Keep(SizeFields);
    }

    /// <summary>The pickers as wide as their longest choice, the number boxes as wide as their largest value - see TextSize.Keep.</summary>
    private void SizeFields()
    {
        Fit(_cmbSoundscape, Math.Max(UiLayout.AtTextSize(180), Pieces.PickerWidth(_cmbSoundscape, ref _soundscapeWidth)));
        Fit(_cmbElement, Math.Max(UiLayout.AtTextSize(120), Pieces.PickerWidth(_cmbElement, ref _elementWidth)));

        foreach (var (cue, picker) in _cuePickers)
        {
            var kept = _cueWidths.TryGetValue(cue, out var known) ? known : null;
            Fit(picker, Math.Max(UiLayout.AtTextSize(160), Pieces.PickerWidth(picker, ref kept)));
            _cueWidths[cue] = kept;
        }

        Fit(_numPost, NumberWidth(_numPost));
        Fit(_numTalkUp, NumberWidth(_numTalkUp));
        Fit(_numNudge, NumberWidth(_numNudge));

        static void Fit(Control field, int width)
        {
            if (field.Width != width)
            {
                field.Width = width;
            }
        }
    }

    /// <summary>A number box as wide as its largest value and its arrows need - the same sum the Music page does.</summary>
    private int NumberWidth(NumericUpDown box)
    {
        var digits = decimal.Truncate(box.Maximum).ToString(System.Globalization.CultureInfo.InvariantCulture).Length
            + (box.DecimalPlaces > 0 ? box.DecimalPlaces + 1 : 0);

        return Math.Max(
            UiLayout.AtTextSize(UiLayout.NumericWidth),
            TextRenderer.MeasureText(new string('0', digits), Font).Width + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra);
    }

    /// <summary>Everything read again - the soundscapes may have been added, renamed or applied since.</summary>
    public void RefreshAll()
    {
        _filling = true;

        try
        {
            _chkEnabled.Checked = BroadcastImaging.Enabled;
            _numNudge.Value = Math.Clamp((decimal)BroadcastImaging.PostNudge, _numNudge.Minimum, _numNudge.Maximum);

            var keep = (SelectedSoundscape ?? BroadcastImaging.Active)?.Id;
            var activeId = _themes.ActiveThemeId;

            _cmbSoundscape.Items.Clear();

            foreach (var theme in _themes.All)
            {
                _cmbSoundscape.Items.Add(new SoundscapeItem(theme, string.Equals(theme.Id, activeId, StringComparison.Ordinal)));
            }

            var index = -1;

            for (var i = 0; i < _cmbSoundscape.Items.Count; i++)
            {
                if (_cmbSoundscape.Items[i] is SoundscapeItem item && string.Equals(item.Theme.Id, keep, StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }

            _cmbSoundscape.SelectedIndex = index >= 0 ? index : (_cmbSoundscape.Items.Count > 0 ? 0 : -1);
            _soundscapeWidth = null;
        }
        finally
        {
            _filling = false;
        }

        UpdateEnabledLook();
        RefreshPieces();
        RefreshRunOfShow();
        SizeFields();
        _showTreatment.RefreshAll();
    }

    private Theme? SelectedSoundscape => (_cmbSoundscape.SelectedItem as SoundscapeItem)?.Theme;

    private ImagingElement SelectedElement => (_cmbElement.SelectedItem as ElementItem)?.Element ?? ImagingElement.Jingle;

    private PieceRow? SelectedPiece => _grid.CurrentRow?.Tag as PieceRow;

    private void UpdateEnabledLook()
    {
        var on = BroadcastImaging.Enabled;

        _chkEnabled.Text = on ? "Broadcast imaging ON" : "Broadcast imaging OFF";
        _chkEnabled.ForeColor = on ? AppTheme.Success : AppTheme.TextMuted;

        // AND THE PAGE SAYS SO (17 Oct 2026). Only the caption used to
        // change, and everything under it stayed live for a feature that
        // was off. The imaging cards grey; the Show treatment card does not
        // - it is the club's own music, and plays where imaging does not.
        // A preview playing is stopped with them, as nothing here plays
        // while imaging is off.
        if (!on)
        {
            StopPreview();
        }

        foreach (var card in _imagingCards)
        {
            if (card.Enabled != on)
            {
                card.Enabled = on;
            }
        }
    }

    private void RefreshPieces()
    {
        var theme = SelectedSoundscape;
        var selected = SelectedPiece?.Path;

        _grid.Rows.Clear();

        if (theme is null)
        {
            _lblSoundscape.Text = string.Empty;
            _lblStatus.Text = "No soundscapes to set up.";
            return;
        }

        _lblSoundscape.Text = BroadcastImaging.IsCustom(theme)
            ? "the club has changed some of its pieces"
            : "the pieces it ships with";

        var counts = new List<string>();

        foreach (var element in Enum.GetValues<ImagingElement>())
        {
            var files = BroadcastImaging.FilesFor(theme, element);
            var custom = BroadcastImaging.IsCustom(theme, element);
            var shipped = BroadcastImaging.DefaultFiles(theme, element);

            foreach (var path in files)
            {
                var isShipped = shipped.Contains(path, StringComparer.OrdinalIgnoreCase);
                var there = File.Exists(path);

                var marked = BroadcastImaging.ClubPost(path) ?? BroadcastImaging.ShippedPost(path);
                var talkUp = BroadcastImaging.ClubTalkUp(path);

                var row = _grid.Rows.Add(
                    BroadcastImaging.Describe(element),
                    Path.GetFileName(path),
                    talkUp is { } post ? $"{post:0.00}s" : "top",
                    marked is { } at ? $"{at:0.00}s" : "auto",
                    !there ? "missing" : isShipped ? "ships with it" : "added");

                _grid.Rows[row].Tag = new PieceRow(element, path);

                if (!there)
                {
                    _grid.Rows[row].DefaultCellStyle.ForeColor = AppTheme.Accent;
                }
                else if (!isShipped)
                {
                    _grid.Rows[row].Cells[4].Style.ForeColor = AppTheme.Success;
                }

                if (selected is not null && string.Equals(selected, path, StringComparison.OrdinalIgnoreCase))
                {
                    _grid.Rows[row].Selected = true;
                    _grid.CurrentCell = _grid.Rows[row].Cells[0];
                }
            }

            if (files.Count > 0 || custom)
            {
                counts.Add($"{Plural.Of(files.Count, BroadcastImaging.Describe(element).ToLowerInvariant())}{(custom ? " (changed)" : string.Empty)}");
            }
        }

        _lblStatus.Text = counts.Count == 0
            ? $"{theme.Name} has no imaging yet - add some with Add Files."
            : $"{theme.Name}: {string.Join(", ", counts)}.";
    }

    private void RefreshRunOfShow()
    {
        _filling = true;

        try
        {
            foreach (var (cue, picker) in _cuePickers)
            {
                var choice = BroadcastImaging.ChoiceFor(cue);

                for (var i = 0; i < picker.Items.Count; i++)
                {
                    if (picker.Items[i] is ChoiceItem item && item.Choice == choice)
                    {
                        picker.SelectedIndex = i;
                        break;
                    }
                }
            }
        }
        finally
        {
            _filling = false;
        }
    }

    private void AddFiles()
    {
        if (SelectedSoundscape is not { } theme)
        {
            return;
        }

        var element = SelectedElement;
        var patterns = string.Join(";", BroadcastImaging.Extensions.Select(e => "*" + e));

        using var dialog = new OpenFileDialog
        {
            Title = $"Choose {BroadcastImaging.Describe(element).ToLowerInvariant()} files for {theme.Name}",
            Filter = $"Audio ({patterns})|{patterns}|All files (*.*)|*.*",
            Multiselect = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var problems = new List<string>();

        foreach (var file in dialog.FileNames)
        {
            if (!BroadcastImaging.Add(theme, element, file, out var problem))
            {
                problems.Add(problem ?? $"{Path.GetFileName(file)} couldn't be added.");
            }
        }

        RefreshPieces();

        if (problems.Count > 0)
        {
            AppDialog.Show(
                this,
                string.Join(Environment.NewLine + Environment.NewLine, problems),
                problems.Count == 1 ? "Couldn't add that file" : "Couldn't add some files",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void PlaySelected(bool intoPost = false)
    {
        if (SelectedPiece is not { } piece)
        {
            AppDialog.Show(this, "Pick a piece in the list first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        StopPreview();

        // Into the post: the three seconds up to it, then a beat past - the
        // part a club listens to when it is placing the mark.
        var from = intoPost && PostOfSelected(piece) is { } post ? Math.Max(0, post - 3) : 0;

        StartPreview(piece, from);
    }

    /// <summary>Plays the selected piece from a point in it, noting where and when for Mark Post and Mark Out.</summary>
    private CancellationTokenSource? StartPreview(PieceRow piece, double from)
    {
        _preview = BroadcastImaging.Preview(piece.Path, out var problem, from);

        if (_preview is null)
        {
            _previewAt = null;
            AppDialog.Show(this, problem ?? "That piece couldn't play.", "Couldn't play it", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        _previewAt = (piece.Path, from, DateTime.UtcNow);

        return _preview;
    }

    /// <summary>
    /// Plays the intro and stops on the post - so a club hears exactly the
    /// stretch the announcer talks over, and the piece's voice cut off as it
    /// starts. Plays from the top when the intro is short, and from four
    /// seconds before the post when it is long.
    /// </summary>
    private void PlayToTalkUp()
    {
        if (SelectedPiece is not { } piece)
        {
            AppDialog.Show(this, "Pick a piece in the list first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var post = BroadcastImaging.ClubTalkUp(piece.Path) ?? 0;

        if (post <= 0)
        {
            AppDialog.Show(
                this,
                "This piece has no post marked yet, so its voice is taken to be at its very top. Press Play, then Mark Post the "
                + "moment you hear its voice come in.",
                "No post yet",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        StopPreview();

        var from = Math.Max(0, post - 4);

        if (StartPreview(piece, from) is { } playing)
        {
            StopLater(playing, TimeSpan.FromSeconds(post - from));
        }
    }

    /// <summary>Plays from just before the out, to hear the piece's voice stop and the music carry on.</summary>
    private void PlayFromOut()
    {
        if (SelectedPiece is not { } piece)
        {
            AppDialog.Show(this, "Pick a piece in the list first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        StopPreview();

        var from = PostOfSelected(piece) is { } at ? Math.Max(0, at - 3) : 0;

        StartPreview(piece, from);
    }

    /// <summary>Stops a preview after a while, if it is still the one playing.</summary>
    private async void StopLater(CancellationTokenSource playing, TimeSpan after)
    {
        try
        {
            await Task.Delay(after);
        }
        catch
        {
            return;
        }

        if (!IsDisposed && ReferenceEquals(_preview, playing))
        {
            StopPreview();
        }
    }

    /// <summary>
    /// Marks the post - or the out - where the preview has got to: press
    /// Play, and press this the instant you hear the moment. Saved straight
    /// away, and the number box shows it for fine-tuning.
    /// </summary>
    private void MarkWhilePlaying(bool post)
    {
        if (SelectedPiece is not { } piece)
        {
            AppDialog.Show(this, "Pick a piece in the list first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var length = BroadcastImaging.Load(piece.Path, out _)?.Length.TotalSeconds ?? 0;

        if (_preview is null
            || _previewAt is not { } at
            || !string.Equals(at.Path, piece.Path, StringComparison.OrdinalIgnoreCase))
        {
            AppDialog.Show(
                this,
                $"Press Play first, then Mark {(post ? "Post" : "Out")} the instant you hear the piece's voice "
                + $"{(post ? "come in" : "stop")}.",
                "Nothing playing",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var heard = at.From + (DateTime.UtcNow - at.StartedUtc).TotalSeconds - ReactionSeconds;

        if (length > 0 && heard > length)
        {
            AppDialog.Show(this, "The piece has already finished. Press Play and mark it while it plays.", "Already finished", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // To the box's own step, so what is saved is what the box shows.
        var mark = Math.Round(Math.Max(0, heard) * 20, MidpointRounding.AwayFromZero) / 20;

        if (post)
        {
            BroadcastImaging.SetTalkUp(piece.Path, mark);
        }
        else
        {
            BroadcastImaging.SetPost(piece.Path, mark);
        }

        RefreshPieces();
        ShowPost();
    }

    private void SetTalkUp()
    {
        if (SelectedPiece is not { } piece)
        {
            AppDialog.Show(this, "Pick a piece in the list first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        BroadcastImaging.SetTalkUp(piece.Path, (double)_numTalkUp.Value);
        RefreshPieces();
        ShowPost();
    }

    private void ClearTalkUp()
    {
        if (SelectedPiece is not { } piece)
        {
            return;
        }

        BroadcastImaging.SetTalkUp(piece.Path, null);
        RefreshPieces();
        ShowPost();
    }

    /// <summary>The selected piece's post in seconds - the mark when there is one, otherwise measured from the piece.</summary>
    private static double? PostOfSelected(PieceRow piece)
    {
        if ((BroadcastImaging.ClubPost(piece.Path) ?? BroadcastImaging.ShippedPost(piece.Path)) is { } marked)
        {
            return marked;
        }

        return BroadcastImaging.Load(piece.Path, out _) is { } clip
            ? BroadcastImaging.PostOf(piece.Path, clip).TotalSeconds
            : null;
    }

    /// <summary>Shows the selected piece's post in the box under the list.</summary>
    private void ShowPost()
    {
        _filling = true;

        try
        {
            if (SelectedPiece is not { } piece)
            {
                _numPost.Value = 0;
                _lblPostSource.Text = string.Empty;
                _numTalkUp.Value = 0;
                _lblTalkUpRoom.Text = string.Empty;
                return;
            }

            var talkUp = BroadcastImaging.ClubTalkUp(piece.Path);

            _numTalkUp.Value = Math.Clamp((decimal)(talkUp ?? 0), _numTalkUp.Minimum, _numTalkUp.Maximum);
            _lblTalkUpRoom.Text = talkUp is { } room && room > 0
                ? $"{room:0.00}s of intro to talk over"
                : "voice at the top - no intro to talk over";

            var marked = BroadcastImaging.ClubPost(piece.Path) ?? BroadcastImaging.ShippedPost(piece.Path);

            _numPost.Value = Math.Clamp((decimal)(marked ?? 0), _numPost.Minimum, _numPost.Maximum);
            _lblPostSource.Text = BroadcastImaging.PostSource(piece.Path) switch
            {
                "set" => "set by the club",
                "shipped" => "as it ships",
                _ => "measured from the piece when it plays",
            };
        }
        finally
        {
            _filling = false;
        }
    }

    private void SetPost()
    {
        if (SelectedPiece is not { } piece)
        {
            AppDialog.Show(this, "Pick a piece in the list first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        BroadcastImaging.SetPost(piece.Path, (double)_numPost.Value);
        RefreshPieces();
        ShowPost();
    }

    private void ClearPost()
    {
        if (SelectedPiece is not { } piece)
        {
            return;
        }

        BroadcastImaging.SetPost(piece.Path, null);
        RefreshPieces();
        ShowPost();
    }

    private void StopPreview()
    {
        BroadcastImaging.StopPreview(_preview);
        _preview = null;
        _previewAt = null;
    }

    private void RemoveSelected()
    {
        if (SelectedSoundscape is not { } theme || SelectedPiece is not { } piece)
        {
            AppDialog.Show(this, "Pick a piece in the list first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        StopPreview();
        BroadcastImaging.Remove(theme, piece.Element, piece.Path);
        RefreshPieces();
    }

    private void RestoreElement()
    {
        if (SelectedSoundscape is not { } theme)
        {
            return;
        }

        var element = SelectedElement;

        if (BroadcastImaging.IsCustom(theme, element)
            && AppDialog.Show(
                this,
                $"Put {theme.Name}'s {BroadcastImaging.Describe(element).ToLowerInvariant()} pieces back to the ones it ships with? "
                + "Any the club added are taken off the list (the files stay in the club's data).",
                "Restore element defaults",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        StopPreview();
        BroadcastImaging.RestoreDefaults(theme, element);
        RefreshPieces();
    }

    private void RestoreSoundscape()
    {
        if (SelectedSoundscape is not { } theme)
        {
            return;
        }

        if (BroadcastImaging.IsCustom(theme)
            && AppDialog.Show(
                this,
                $"Put every one of {theme.Name}'s pieces back to the ones it ships with? "
                + "Any the club added are taken off the list (the files stay in the club's data).",
                "Restore soundscape defaults",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        StopPreview();
        BroadcastImaging.RestoreDefaults(theme);
        RefreshPieces();
    }
}
