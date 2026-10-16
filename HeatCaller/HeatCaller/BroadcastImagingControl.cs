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
    private readonly NumericUpDown _numLevel = new();
    private readonly NumericUpDown _numPost = new();
    private readonly Label _lblPostSource = new();

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

    public BroadcastImagingControl(ThemeStore themes)
    {
        _themes = themes;

        Dock = DockStyle.Fill;
        ForeColor = AppTheme.TextDark;

        _textSize.Wear(this, () => AppTheme.BodyFont);

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

    /// <summary>A preview left playing when the page is put away is stopped with it.</summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (!Visible)
        {
            StopPreview();
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
    }

    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "BROADCAST IMAGING. Makes race night sound like an FM station: every soundscape has its own jingles, sweepers, bumpers, "
                 + "stingers and music beds, and the run of show below says where each one plays. Where there is more than "
                 + "one of a kind, one is picked at random each time and none plays twice until they all have.",
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
            Text = "Off and nothing on this page plays - the race night sounds exactly as it did without it. "
                 + "On Air, in front of Stage on Race Control, plays the top of the race; the rest follows the race on its own.",
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
        soundscapeGrid.AddRow("Soundscape:", FieldGrid.Inline(_cmbSoundscape, _lblSoundscape));
        soundscapeGrid.AddHint(
            "Each soundscape only ever plays its own pieces. The one in use on the Soundscapes tab is what the race "
            + "night hears; any of them can be set up here.");

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

        // THE POST (16 Oct 2026): where the voice - or the next piece -
        // comes in. On a jingle, the instant the sung part stops.
        _numPost.DecimalPlaces = 2;
        _numPost.Increment = 0.05m;
        _numPost.Minimum = 0;
        _numPost.Maximum = (decimal)ShowClip.MostSeconds;

        _lblPostSource.AutoSize = true;
        _lblPostSource.ForeColor = AppTheme.TextMuted;
        _textSize.Wear(_lblPostSource, () => AppTheme.SmallFont);

        var btnSetPost = new Button { Text = "Set Post" };
        btnSetPost.Click += (_, _) => SetPost();

        var btnAutoPost = new Button { Text = "Auto Post" };
        btnAutoPost.Click += (_, _) => ClearPost();

        var btnPlayPost = new Button { Text = "Play Into Post" };
        btnPlayPost.Click += (_, _) => PlaySelected(intoPost: true);

        var postGrid = new FieldGrid();
        postGrid.AddRow("Post at:", FieldGrid.Inline(_numPost, new Label { Text = "s" }, btnSetPost, btnAutoPost, btnPlayPost, _lblPostSource));
        postGrid.AddHint(
            "The post is where the next thing comes in over a piece - on a jingle, the instant the sung part stops, so the "
            + "voice hits it. Whatever plays after it crossfades into the bed or the next piece; nothing is ever cut short. "
            + "Play Into Post plays the three seconds up to it. Auto Post goes back to the shipped mark, or one measured "
            + "from the piece.");

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
            Text = "Add Files copies the files into the club's data as the element picked beside it, named for the "
                 + "soundscape and the element - \"Classic Jingle 5\" - so moving the originals afterwards loses nothing. "
                 + "Restore Element Defaults puts the element picked beside Add Files back to what the soundscape ships with.",
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

            runGrid.AddRow(BroadcastImaging.Describe(cue) + ":", FieldGrid.Inline(picker, when));
        }

        var btnRestoreRun = new Button { Text = "Restore Run of Show Defaults" };
        btnRestoreRun.Click += (_, _) =>
        {
            BroadcastImaging.RestoreRunOfShow();
            RefreshRunOfShow();
        };

        var lblRunHint = new Label
        {
            Text = "The night runs: jingle or sweeper  ->  opening, hitting the jingle's post over the opening bed  ->  bumper  "
                 + "->  staging call over the bed  ...  race over  ->  bumper  ->  stop set  ->  jingle or sweeper  ->  race recap "
                 + "open  ->  the recap host hits its post as it crossfades into the recap bed  ->  stinger between parts  ->  "
                 + "bumper over the bed's crossfade. The first part is On Air's, on Race Control. Every piece plays whole: the "
                 + "next thing starts at its post and the rest crossfades under it, so nothing is cut and nothing drops to "
                 + "silence. A point whose soundscape has none of what it is set to simply plays nothing.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblRunHint, () => AppTheme.SmallFont);

        var run = new Section("Run of show");
        run.Add(lblRunHint);
        run.Add(runGrid);
        run.Add(new ButtonBar().With(btnRestoreRun), gapBelow: 0);

        // --- Levels ------------------------------------------------------

        _numLevel.Minimum = 0;
        _numLevel.Maximum = 100;
        _numLevel.Increment = 5;
        _numLevel.ValueChanged += (_, _) =>
        {
            if (!_filling)
            {
                BroadcastImaging.Level = (int)_numLevel.Value;
            }
        };

        var lblLevelSuffix = new Label { Text = "%  - the bed plays at the show bed level on the Commentary page", AutoSize = true, ForeColor = AppTheme.TextMuted };
        _textSize.Wear(lblLevelSuffix, () => AppTheme.SmallFont);

        var levelGrid = new FieldGrid();
        levelGrid.AddRow("Level:", FieldGrid.Inline(_numLevel, lblLevelSuffix));

        var levels = new Section("Level");
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
        page.Add(levels, gapBelow: 0);

        Controls.Add(page);

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

        Fit(_numLevel, NumberWidth(_numLevel));
        Fit(_numPost, NumberWidth(_numPost));

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
            _numLevel.Value = Math.Clamp(BroadcastImaging.Level, (int)_numLevel.Minimum, (int)_numLevel.Maximum);

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
    }

    private Theme? SelectedSoundscape => (_cmbSoundscape.SelectedItem as SoundscapeItem)?.Theme;

    private ImagingElement SelectedElement => (_cmbElement.SelectedItem as ElementItem)?.Element ?? ImagingElement.Jingle;

    private PieceRow? SelectedPiece => _grid.CurrentRow?.Tag as PieceRow;

    private void UpdateEnabledLook()
    {
        _chkEnabled.Text = BroadcastImaging.Enabled ? "Broadcast imaging ON" : "Broadcast imaging OFF";
        _chkEnabled.ForeColor = BroadcastImaging.Enabled ? AppTheme.Success : AppTheme.TextMuted;
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

                var row = _grid.Rows.Add(
                    BroadcastImaging.Describe(element),
                    Path.GetFileName(path),
                    marked is { } post ? $"{post:0.00}s" : "auto",
                    !there ? "missing" : isShipped ? "ships with it" : "added");

                _grid.Rows[row].Tag = new PieceRow(element, path);

                if (!there)
                {
                    _grid.Rows[row].DefaultCellStyle.ForeColor = AppTheme.Accent;
                }
                else if (!isShipped)
                {
                    _grid.Rows[row].Cells[3].Style.ForeColor = AppTheme.Success;
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

        _preview = BroadcastImaging.Preview(piece.Path, out var problem, from);

        if (_preview is null)
        {
            AppDialog.Show(this, problem ?? "That piece couldn't play.", "Couldn't play it", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
                return;
            }

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
