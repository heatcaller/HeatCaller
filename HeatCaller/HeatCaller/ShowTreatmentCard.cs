namespace HeatCaller;

/// <summary>
/// The "Show treatment" card on Sounds > Imaging (17 Oct 2026): the club's
/// own opener, bed and sting, their levels, where the voice comes in, the
/// running order, Play It, and which segments get the music.
///
/// MOVED FROM THE COMMENTARY PAGE, where it was "The show's music" card -
/// the Voice + Sounds regroup put everything you hear except the voice on
/// Sounds. Same settings, same behaviour: nothing a club has saved changes,
/// only where it is shown.
///
/// A card rather than a page of its own. Section is sealed and the Imaging
/// page is a FillPage, so this builds the card and its host adds it - the
/// way the page's own cards are added - and the host's TextSize keeps it in
/// step with the text size. Every hook it needs is set by MainForm, the way
/// CommentaryControl's were (MainForm.Commentary.cs, WireShowMusic).
///
/// It still greys out with AI commentary switched off: the show's music
/// dresses the commentary's segments, and with no commentary there is no
/// segment for it to dress (see CommentaryOn).
/// </summary>
internal sealed class ShowTreatmentCard
{
    private readonly AppSettings _settings;
    private readonly TextSize _textSize;

    /// <summary>
    /// The club's show music, for choosing and describing the files. The
    /// announcer screen keeps one of its own and is told when this one
    /// changes anything - see ShowMusicChanged.
    /// </summary>
    private readonly ShowMusic _showMusic;

    // --- The show's music (8 Oct 2026) ---
    //
    // An opener in front of the voice and a bed under it, for the segments
    // that open and close the evening. The levels are the part that needs
    // an ear rather than a number, so there is a Test button that runs a
    // real show rather than a preview of one.
    private readonly CheckBox _chkShowMusic = new ThemedCheckBox();

    // The shipped library, one list per part (11 Oct 2026) - see
    // ShowMusicLibrary. Modelled on the Sounds page's rows: a drop-down of
    // what ships, beside a Choose... for the club's own file, and the
    // club's own file wins wherever one is set.
    private readonly ComboBox _cmbOpenerPiece = new();
    private readonly ComboBox _cmbBedPiece = new();
    private readonly ComboBox _cmbStingPiece = new();

    private readonly NumericUpDown _numOpenerLevel = new();

    /// <summary>AppSettings.ShowStingLevel, which had no box (17 Oct 2026) - built like the opener's.</summary>
    private readonly NumericUpDown _numStingLevel = new();
    private readonly NumericUpDown _numBedLevel = new();
    private readonly NumericUpDown _numBedDuck = new();
    private readonly NumericUpDown _numVoiceEnters = new();
    private readonly Button _btnOpenerPick = new();
    private readonly Button _btnOpenerClear = new();
    private readonly Button _btnBedPick = new();
    private readonly Button _btnBedClear = new();
    private readonly Button _btnTestShow = new();
    private readonly Label _lblOpenerFile = new();
    private readonly Label _lblBedFile = new();
    private readonly Label _lblShowResult = new();

    // --- The sting (10 Oct 2026) ---
    //
    // The third piece, and the only one that is not music to listen to: it
    // is the mark BETWEEN two items of a block - see ShowBlock. Beside the
    // opener and the bed because it is chosen the same way and lives in the
    // same folder, and below them because it is the one a club can leave
    // out without losing anything: with no sting a swell of the bed does
    // the same job, and the line beside the row says so.
    private readonly Button _btnStingPick = new();
    private readonly Button _btnStingClear = new();
    private readonly Label _lblStingFile = new();

    // --- The running order (10 Oct 2026) ---
    //
    // A show is no longer the same show every time: the first of the night
    // plays the opener whole and the ones after it come in on its ring-out
    // - see ShowRunningOrder. Where a later show comes in is MEASURED off
    // the club's own file, so the line under the box says what their file
    // is going to do, the way the two Describe lines above it do.
    private readonly CheckBox _chkShowRejoin = new ThemedCheckBox();
    private readonly Label _lblRunningOrder = new();

    /// <summary>
    /// SOUNDSCAPE BEFORE CLUB (17 Oct 2026). The club's own opener, bed and
    /// sting rows, folded away while the soundscape in use has imaging -
    /// its pieces play then, at the Mixer's imaging levels - and back on
    /// their own on a soundscape with none. See RefreshImagingFold.
    /// </summary>
    private readonly FieldGrid _clubPiecesGrid = new();

    /// <summary>What stands in for the folded rows, and the button that opens them anyway.</summary>
    private readonly Label _lblImagingCovers;
    private readonly Button _btnShowClubPieces = new();
    private bool _clubPiecesOpened;
    private readonly Control _clubPiecesBar;

    /// <summary>The boxes for which segments get the music, by segment - see ShowSegments.Candidates.</summary>
    private readonly Dictionary<SegmentKind, CheckBox> _showBoxes = new();

    /// <summary>
    /// True while Play It is running. Its button is held down for the
    /// length of the show, and almost anything else on the card calls
    /// UpdateEnabledState, which would otherwise put it back up mid-run.
    /// </summary>
    private bool _testShowRunning;

    /// <summary>The usual caption of the button that says so while it works - see Steady.</summary>
    private const string TestShowCaption = "Play It";

    /// <summary>The library drop-downs. Wide enough for "Good Morning America 2" and "Opening Drive (132 BPM)" without cutting either off.</summary>
    private const int ShowPieceWidth = 220;

    /// <summary>
    /// What a part's drop-down says when the club is on nothing.
    ///
    /// Not blank, and not "Default": blank reads as a list that failed to
    /// load, and "Default" is wrong because none IS a choice here - a club
    /// who wants no opener picks this on purpose, and the page has to be
    /// able to show them that is what they have.
    /// </summary>
    private const string NoPiece = "None";

    private const string ShowClubPiecesCaption = "Show the club's own pieces";
    private const string HideClubPiecesCaption = "Hide the club's own pieces";

    /// <summary>
    /// Whether AI commentary is switched on - the master switch on Voice >
    /// Commentary. The card greys out without it, as it did on that page.
    /// Set by the host; unset, the saved setting is read.
    /// </summary>
    internal Func<bool>? CommentaryOn { get; set; }

    /// <summary>
    /// Plays a show open with a test line over it, as a real segment
    /// would, and hands back what was wrong or null. Set by the host,
    /// which has the announcer.
    /// </summary>
    internal Func<Task<string?>>? TestShowMusic { get; set; }

    /// <summary>
    /// Tells the announcer screen that the show's music has changed, so it
    /// reads the files again rather than playing the take it loaded at the
    /// start of the evening. Set by the host.
    /// </summary>
    internal Action? ShowMusicChanged { get; set; }

    /// <summary>The card itself, for the host to put on its page.</summary>
    public Section Card { get; }

    /// <summary>
    /// The card's grid, for the host's one caption column (FieldGrid.LineUp)
    /// - (22 Oct 2026, page tidy): it was in none, so its fields started at
    /// a different x from the rest of the Imaging page's.
    /// </summary>
    internal FieldGrid Grid => _clubPiecesGrid;

    public ShowTreatmentCard(AppSettings settings, TextSize textSize)
    {
        _settings = settings;
        _textSize = textSize;
        _showMusic = new ShowMusic(settings);

        Card = new Section("Show treatment");

        _lblImagingCovers = Hint(
            // Said truly (22 Oct 2026, bug hunt): the imaging only covers the
            // race recap's open and bed, and the stings - the club's own
            // pieces still play on the cold open, the sign-off and every
            // other set piece ticked below.
            "This soundscape's imaging plays the race recap's opener, bed and stings (Pieces card above; levels on "
            + "the Mixer). The club's own pieces below still play on the cold open, the sign-off and the other set "
            + "pieces ticked below.");

        _clubPiecesBar = new ButtonBar().With(Steady(_btnShowClubPieces, HideClubPiecesCaption));

        Build();
    }

    private bool IsCommentaryOn => CommentaryOn?.Invoke() ?? _settings.CommentaryEnabled;

    // A line of help in the small muted type the app's hints use,
    // wrapping to the room it is given.
    private Label Hint(string text) => _textSize.Wear(
        new Label { Text = text, AutoSize = true, ForeColor = AppTheme.TextMuted },
        () => AppTheme.SmallFont);

    // What follows a field on its line - "%", "ms". Muted and in the small
    // type when it explains the field; in the body face when it finishes
    // the sentence the caption starts.
    private Label Suffix(string text, bool muted = true)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = muted ? AppTheme.TextMuted : AppTheme.TextDark,
        };

        if (muted)
        {
            _textSize.Wear(label, () => AppTheme.SmallFont);
        }

        return label;
    }

    private static CheckBox Check(CheckBox box, string text)
    {
        box.Text = text;
        box.AutoSize = true;
        return box;
    }

    // The caption of a setting that belongs to the switch above it sits a
    // step in, as it did on the Commentary page.
    private static Label Stepped(Label caption)
    {
        caption.Margin = new Padding(UiLayout.Indent, caption.Margin.Top, caption.Margin.Right, caption.Margin.Bottom);
        return caption;
    }

    private static FlowLayoutPanel Line(params Control[] controls) => FieldGrid.Inline(controls);

    // A button whose caption is swapped while it works ("Playing..."), in a
    // panel of its own that is always as big as its usual caption needs -
    // see CommentaryControl.BuildLayout's Steady for why.
    private Panel Steady(Button button, string caption)
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

    private void Build()
    {
        Check(_chkShowMusic, "Play an opener and a music bed under the show");
        _textSize.Wear(_chkShowMusic, () => AppTheme.BoldFont);
        _chkShowMusic.Checked = _settings.ShowThemeEnabled;
        _chkShowMusic.CheckedChanged += (_, _) =>
        {
            _settings.ShowThemeEnabled = _chkShowMusic.Checked;
            _settings.Save();
            ShowMusicChanged?.Invoke();
            UpdateEnabledState();
        };

        // THE SHIPPED PIECES, one drop-down per part. Filled from the
        // library rather than written down - adding an mp3 to
        // Assets\ShowMusic adds a choice with no code change. Each one
        // attaches its own handler after it is filled, so filling it does
        // not read as the club choosing something - see FillLibrary.
        FillLibrary(_cmbOpenerPiece, ShowPart.Opener);
        FillLibrary(_cmbBedPiece, ShowPart.Bed);
        FillLibrary(_cmbStingPiece, ShowPart.Sting);

        _btnOpenerPick.Text = "Choose...";
        _btnOpenerPick.Click += (_, _) => PickShowMusic(ShowPart.Opener);

        _btnOpenerClear.Text = "Remove";
        _btnOpenerClear.Click += (_, _) => ClearShowMusic(ShowPart.Opener);

        _btnBedPick.Text = "Choose...";
        _btnBedPick.Click += (_, _) => PickShowMusic(ShowPart.Bed);

        _btnBedClear.Text = "Remove";
        _btnBedClear.Click += (_, _) => ClearShowMusic(ShowPart.Bed);

        _btnStingPick.Text = "Choose...";
        _btnStingPick.Click += (_, _) => PickShowMusic(ShowPart.Sting);

        _btnStingClear.Text = "Remove";
        _btnStingClear.Click += (_, _) => ClearShowMusic(ShowPart.Sting);

        foreach (var label in new[] { _lblOpenerFile, _lblBedFile, _lblStingFile, _lblShowResult })
        {
            label.AutoSize = true;
            label.ForeColor = AppTheme.TextMuted;
            _textSize.Wear(label, () => AppTheme.SmallFont);
        }

        // A floor of 10%: a show whose opener is at 2% reads as the feature
        // being broken rather than as a quiet opener.
        _numOpenerLevel.Minimum = 10;
        _numOpenerLevel.Maximum = 100;
        _numOpenerLevel.Value = Math.Clamp(_settings.ShowOpenerLevel, 10, 100);
        _numOpenerLevel.ValueChanged += (_, _) =>
        {
            _settings.ShowOpenerLevel = (int)_numOpenerLevel.Value;
            _settings.Save();
        };

        // The same floor and ceiling as the opener's, for the same reason.
        _numStingLevel.Minimum = 10;
        _numStingLevel.Maximum = 100;
        _numStingLevel.Value = Math.Clamp(_settings.ShowStingLevel, 10, 100);
        _numStingLevel.ValueChanged += (_, _) =>
        {
            _settings.ShowStingLevel = (int)_numStingLevel.Value;
            _settings.Save();
        };

        _numBedLevel.Minimum = 2;
        _numBedLevel.Maximum = 100;
        _numBedLevel.Value = Math.Clamp(_settings.ShowBedLevel, 2, 100);
        _numBedLevel.ValueChanged += (_, _) =>
        {
            _settings.ShowBedLevel = (int)_numBedLevel.Value;
            _settings.Save();
        };

        _numBedDuck.Minimum = 5;
        _numBedDuck.Maximum = 100;
        _numBedDuck.Value = Math.Clamp(_settings.ShowBedDuckPercent, 5, 100);
        _numBedDuck.ValueChanged += (_, _) =>
        {
            _settings.ShowBedDuckPercent = (int)_numBedDuck.Value;
            _settings.Save();
        };

        _numVoiceEnters.Minimum = 0;
        _numVoiceEnters.Maximum = 15000;
        _numVoiceEnters.Increment = 100;
        _numVoiceEnters.Value = Math.Clamp(_settings.ShowVoiceEntersMs, 0, 15000);
        _numVoiceEnters.ValueChanged += (_, _) =>
        {
            _settings.ShowVoiceEntersMs = (int)_numVoiceEnters.Value;
            _settings.Save();
        };

        Check(_chkShowRejoin, "Shorten the opener on later shows");
        _chkShowRejoin.Checked = _settings.ShowRejoinEnabled;
        _chkShowRejoin.CheckedChanged += (_, _) =>
        {
            _settings.ShowRejoinEnabled = _chkShowRejoin.Checked;
            _settings.Save();
            RefreshShowMusic();
        };

        _btnTestShow.Text = TestShowCaption;
        _btnTestShow.Click += async (_, _) => await TestShowMusicAsync();

        var showMusicGrid = _clubPiecesGrid;

        // The drop-down FIRST on each row, because it is what most clubs
        // will ever touch: the shipped piece is what they are already on,
        // and Choose... is for the club with a piece of their own.
        Stepped(showMusicGrid.AddRow("Opener:", Line(_cmbOpenerPiece, _btnOpenerPick, _btnOpenerClear, _lblOpenerFile)));
        Stepped(showMusicGrid.AddRow("Music bed:", Line(_cmbBedPiece, _btnBedPick, _btnBedClear, _lblBedFile)));
        Stepped(showMusicGrid.AddRow("Club's sting:", Line(_cmbStingPiece, _btnStingPick, _btnStingClear, _lblStingFile)));
        showMusicGrid.AddHint(
            "Your own files are copied into the club's data, so a backup carries them. The bed loops (half a minute is "
            + "plenty); the sting is a second or two between parts - with none, the bed swells instead.");

        Stepped(showMusicGrid.AddRow("Opener level:", Line(_numOpenerLevel, Suffix("%", muted: false))));
        Stepped(showMusicGrid.AddRow("Sting level:", Line(_numStingLevel, Suffix("%", muted: false))));
        Stepped(showMusicGrid.AddRow("Bed level:", Line(_numBedLevel, Suffix("% - low on purpose, a bed sits under the voice"))));
        Stepped(showMusicGrid.AddRow("and drops to:", Line(_numBedDuck, Suffix("% of that while the announcer speaks"))));
        Stepped(showMusicGrid.AddRow("Voice comes in:", Line(_numVoiceEnters, Suffix("ms into the opener - 0 finds where it starts ringing out"))));

        // The running order. In the field column with no caption of its
        // own, because it belongs to the four rows above it rather than
        // naming a field - FieldGrid.AddField.
        showMusicGrid.AddField(_chkShowRejoin);
        showMusicGrid.AddHint(
            "The first show of the night plays the opener whole; later ones come in on its ring-out, and the sign-off "
            + "plays out on the bed.");
        showMusicGrid.AddHint(_lblRunningOrder);

        Stepped(showMusicGrid.AddRow("Hear it:", Line(Steady(_btnTestShow, TestShowCaption), _lblShowResult)));

        // ------------------------------------------------------------------
        // Which segments get it
        //
        // The set pieces only - see ShowSegments.Candidates for why the
        // race intro and the recap are not on this list.
        // ------------------------------------------------------------------

        var musicOn = new HashSet<SegmentKind>(ShowSegments.Parse(_settings.ShowSegments));

        var show = Card;

        show.Add(Hint(
            "For the AI commentary's set pieces, so they sound like going on air. Needs AI commentary on. Where each "
            + "piece plays: Run of show, above."));

        // THE FALLBACK (17 Oct 2026). Imaging owns the show's music now, per
        // soundscape; this card is what plays where the soundscape has no
        // piece, and its levels are the club's own pieces' only.
        show.Add(_chkShowMusic, stretch: false, gapBelow: UiLayout.HintGap);

        _btnShowClubPieces.Text = ShowClubPiecesCaption;
        _btnShowClubPieces.Click += (_, _) =>
        {
            _clubPiecesOpened = !_clubPiecesOpened;
            RefreshImagingFold();
        };

        show.Add(_lblImagingCovers, gapBelow: UiLayout.HintGap);
        show.Add(_clubPiecesBar, stretch: false, gapBelow: UiLayout.HintGap);
        show.Add(showMusicGrid);

        var musicHeading = _textSize.Wear(
            new Label { Text = "Which segments get it", AutoSize = true, ForeColor = AppTheme.TextDark },
            () => AppTheme.BoldFont);

        show.Add(musicHeading, stretch: false, gapBelow: 0);

        // The two that sit beside a race ARE on this list, and have been
        // since each got its own handling - see ShowSegments.Candidates for
        // what is actually offered and why.
        show.Add(Hint(
            "The cold open and the sign-off come ticked. Achievements earned plays inside the recap - tick it to give it "
            + "a show on a night with no recap."),
            gapBelow: UiLayout.HintGap);

        // Not the hidden ones (17 Oct 2026) - see CommentaryControl.NotOffered.
        var musicCandidates = ShowSegments.Candidates.Where(kind => !CommentaryControl.NotOffered.Contains(kind)).ToList();

        for (var i = 0; i < musicCandidates.Count; i++)
        {
            var kind = musicCandidates[i];
            var box = new ThemedCheckBox();

            Check(box, CommentaryBudget.Spec(kind).Title);
            box.Checked = musicOn.Contains(kind);
            box.CheckedChanged += (_, _) => SaveShowSegments();

            _showBoxes[kind] = box;

            // Nothing under the last one: the section's own room follows it.
            var last = i == musicCandidates.Count - 1;

            show.Add(box, stretch: false, gapBelow: last ? 0 : UiLayout.RowGap);
        }

        _textSize.Keep(SizeFields);

        RefreshImagingFold();
        RefreshShowMusic();
    }

    /// <summary>
    /// Everything read again and greyed by the rules - for the host when its
    /// page is shown, since the commentary switch and the soundscape in use
    /// are changed on other pages.
    /// </summary>
    public void RefreshAll()
    {
        RefreshImagingFold();
        UpdateEnabledState();
    }

    /// <summary>The library drop-downs at their width at the normal text size, at this size, and the number boxes as wide as their largest value.</summary>
    private void SizeFields()
    {
        Fit(_cmbOpenerPiece, UiLayout.AtTextSize(ShowPieceWidth));
        Fit(_cmbBedPiece, UiLayout.AtTextSize(ShowPieceWidth));
        Fit(_cmbStingPiece, UiLayout.AtTextSize(ShowPieceWidth));

        foreach (var number in new[] { _numOpenerLevel, _numStingLevel, _numBedLevel, _numBedDuck, _numVoiceEnters })
        {
            Fit(number, NumberWidth(number));
        }

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
    /// A number box as wide as its largest value - every digit a nought, with
    /// its decimals - and its arrows need in its own font, and never narrower
    /// than a number box was at the normal text size, at this size.
    /// </summary>
    private static int NumberWidth(NumericUpDown number)
    {
        var digits = decimal.Truncate(number.Maximum).ToString(System.Globalization.CultureInfo.InvariantCulture).Length;

        var widest = new string('0', digits)
            + (number.DecimalPlaces > 0 ? "." + new string('0', number.DecimalPlaces) : string.Empty);

        return Math.Max(
            UiLayout.AtTextSize(UiLayout.NumericWidth),
            TextRenderer.MeasureText(widest, number.Font).Width + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra);
    }

    /// <summary>
    /// Which segments get the music, saved by name. An empty choice is
    /// saved as "none" rather than blank - see ShowSegments.Save.
    /// </summary>
    private void SaveShowSegments()
    {
        _settings.ShowSegments = ShowSegments.Save(
            _showBoxes.Where(pair => pair.Value.Checked).Select(pair => pair.Key));

        _settings.Save();
    }

    /// <summary>
    /// Takes a file as the opener or the bed. The file is checked before
    /// it is copied, so a piece that is no use as a loop is turned away
    /// here rather than on a race night - see ShowMusic.Install.
    /// </summary>
    private void PickShowMusic(ShowPart part)
    {
        var what = WhatItIs(part);

        using var dialog = new OpenFileDialog
        {
            Title = $"Choose the {what}",
            Filter = $"Audio ({string.Join(";", ShowMusic.Extensions.Select(e => "*" + e))})"
                + $"|{string.Join(";", ShowMusic.Extensions.Select(e => "*" + e))}|All files (*.*)|*.*",

            // The folder the files end up in, so a club swapping one out
            // can see what is already there. Blank - not null - for the
            // usual Windows choice when it isn't there yet.
            InitialDirectory = Directory.Exists(ShowMusic.Folder) ? ShowMusic.Folder : string.Empty,
        };

        if (dialog.ShowDialog(Card) != DialogResult.OK)
        {
            return;
        }

        if (!_showMusic.Install(dialog.FileName, part, out var problem))
        {
            AppDialog.Show(
                Card,
                problem ?? $"That file couldn't be used as the {what}.",
                $"Couldn't use that {what}",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        _lblShowResult.Text = string.Empty;

        ShowMusicChanged?.Invoke();
        RefreshShowMusic();
    }

    /// <summary>Takes one of the three pieces away, so the show runs without it.</summary>
    private void ClearShowMusic(ShowPart part)
    {
        var what = WhatItIs(part);

        // Remove only ever takes away the club's OWN file (17 Oct 2026, Bug
        // Hunt 8, A13-1): the show then goes back to the piece chosen in
        // the list, so "read without it" was untrue whenever one was. None
        // in the list is what plays nothing - and for the sting, nothing
        // means the bed swells to mark the breaks.
        var warning = part == ShowPart.Sting
            ? $"Remove your own {what}? The show goes back to the sting chosen in the list (choose {NoPiece} there "
              + "and the breaks are marked by the bed swelling instead)."
            : $"Remove your own {what}? The show goes back to the piece chosen in the list (choose {NoPiece} there "
              + "to play nothing).";

        if (AppDialog.Show(
                Card,
                warning,
                $"Remove the {what}",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _showMusic.Remove(part);
        _lblShowResult.Text = string.Empty;

        ShowMusicChanged?.Invoke();
        RefreshShowMusic();
    }

    /// <summary>What a piece is called in front of a club - the pickers, the warnings and the titles all use this one.</summary>
    private static string WhatItIs(ShowPart part) => part switch
    {
        ShowPart.Bed => "music bed",
        ShowPart.Sting => "sting",
        _ => "opener",
    };

    /// <summary>
    /// Fills one part's drop-down from the shipped library and selects
    /// whatever the club is on.
    ///
    /// THE HANDLER GOES ON LAST, after the items and the selection. A
    /// SelectedIndexChanged attached first fires while this is filling -
    /// once as the first item lands and again as the selection is set -
    /// and each of those would be written to the settings and counted as
    /// the club having chosen, which is exactly what
    /// AppSettings.ShowMusicStarted must not be told by accident.
    ///
    /// An empty library leaves the row with None in it and nothing else,
    /// which is the honest state of a damaged install - the club's own
    /// Choose... still works.
    /// </summary>
    private void FillLibrary(ComboBox box, ShowPart part)
    {
        box.DropDownStyle = ComboBoxStyle.DropDownList;
        box.Items.Clear();
        box.Items.Add(NoPiece);

        var pieces = ShowMusicLibrary.For(part);

        foreach (var piece in pieces)
        {
            box.Items.Add(piece.Name);
        }

        var on = (part switch
        {
            ShowPart.Bed => _settings.ShowBedBuiltIn,
            ShowPart.Sting => _settings.ShowStingBuiltIn,
            _ => _settings.ShowOpenerBuiltIn,
        } ?? string.Empty).Trim();

        // A name the library no longer has is ADDED to the list and
        // selected, rather than silently falling back to None: the club
        // chose it, the file has gone, and Describe says so beside the
        // row. Dropping it from the list would make the page claim they
        // had chosen nothing.
        if (on.Length > 0 && !ShowMusicLibrary.Has(part, on))
        {
            box.Items.Add(on);
        }

        box.SelectedItem = on.Length > 0 ? on : NoPiece;

        if (box.SelectedIndex < 0)
        {
            box.SelectedIndex = 0;
        }

        box.SelectedIndexChanged += (_, _) =>
        {
            var chosen = box.SelectedItem as string;

            _showMusic.ChooseFromLibrary(part, string.Equals(chosen, NoPiece, StringComparison.Ordinal) ? null : chosen);

            // Both, exactly as PickShowMusic and ClearShowMusic do: the
            // announcer screen holds a ShowMusic of its own over the same
            // static clip cache, and this card's labels are stale until
            // they are redrawn.
            ShowMusicChanged?.Invoke();
            RefreshShowMusic();
        };
    }

    /// <summary>
    /// Whether the soundscape in use has imaging for the show - its opener,
    /// recap bed or stinger. The same question ShowTreatment asks when it
    /// picks what to play.
    /// </summary>
    private static bool SoundscapeHasImaging() =>
        BroadcastImaging.Has(ImagingCue.RecapOpen)
        || BroadcastImaging.Has(ImagingCue.RecapBed)
        || BroadcastImaging.Has(ImagingCue.BetweenRecapParts);

    /// <summary>
    /// Folds the club's own opener, bed and sting rows away while the
    /// soundscape covers them, and shows them on a soundscape that does
    /// not. The club's choices are kept either way - nothing is cleared.
    /// </summary>
    public void RefreshImagingFold()
    {
        if (Card.IsDisposed)
        {
            return;
        }

        var covered = SoundscapeHasImaging();

        Card.SuspendLayout();

        try
        {
            // The club's rows stay on show (22 Oct 2026, bug hunt): they are
            // still heard outside the recap, so folding them away hid the
            // controls for music that plays.
            _lblImagingCovers.Visible = covered;
            _clubPiecesBar.Visible = false;
            _btnShowClubPieces.Text = _clubPiecesOpened ? HideClubPiecesCaption : ShowClubPiecesCaption;
            _clubPiecesGrid.Visible = true;
        }
        finally
        {
            Card.ResumeLayout(true);
        }
    }

    /// <summary>
    /// What the three files are, and what the measurement made of them -
    /// how much of the bed loops and whether it has an ending of its own.
    /// Measured rather than set, so this is the only way a club can see
    /// what their file is going to do.
    /// </summary>
    private void RefreshShowMusic()
    {
        _lblOpenerFile.Text = _showMusic.Describe(ShowPart.Opener);
        _lblBedFile.Text = _showMusic.Describe(ShowPart.Bed);
        _lblStingFile.Text = _showMusic.Describe(ShowPart.Sting);

        // And what the running order is going to do with their opener,
        // which is measured off the file in the same way and so is just as
        // impossible to guess from the outside. Only worth saying while
        // the shortening is on: unticked, every show plays it whole and
        // there is nothing to report.
        _lblRunningOrder.Text = _chkShowRejoin.Checked
            ? _showMusic.DescribeRunningOrder()
            : "every show plays the opener whole.";

        UpdateEnabledState();
    }

    /// <summary>
    /// Plays a show open with a test line over it - a real one, through
    /// the announcer, because the levels are the whole point and they can
    /// only be judged against an actual voice.
    /// </summary>
    private async Task TestShowMusicAsync()
    {
        if (TestShowMusic is null)
        {
            return;
        }

        // The button stays down for the length of the show, so nobody
        // starts a second one over the first.
        _testShowRunning = true;
        _btnTestShow.Enabled = false;
        _btnTestShow.Text = "Playing...";
        _lblShowResult.Text = string.Empty;
        _lblShowResult.ForeColor = AppTheme.TextMuted;

        try
        {
            var problem = await TestShowMusic.Invoke();

            if (Card.IsDisposed)
            {
                return;
            }

            // The press itself counts as a show of the evening, so a
            // second one is a rejoin and sounds different on purpose -
            // said here, because otherwise it reads as the test having
            // half failed the second time.
            _lblShowResult.Text = problem ?? "That is how it will sound. Press it again to hear a later show in the night.";
            _lblShowResult.ForeColor = problem is null ? AppTheme.TextMuted : AppTheme.Accent;
        }
        catch (Exception ex)
        {
            _lblShowResult.Text = ex.Message;
            _lblShowResult.ForeColor = AppTheme.Accent;
        }
        finally
        {
            _testShowRunning = false;

            if (!Card.IsDisposed)
            {
                _btnTestShow.Text = TestShowCaption;
                UpdateEnabledState();
            }
        }
    }

    /// <summary>
    /// The card's greying. The show's music dresses the commentary's
    /// segments, so it goes dark with commentary itself. Remove is dark when
    /// there is nothing there to remove, which is also how the card says a
    /// file never arrived.
    /// </summary>
    public void UpdateEnabledState()
    {
        var on = IsCommentaryOn;

        _chkShowMusic.Enabled = on;

        var musicOn = on && _chkShowMusic.Checked;

        _btnOpenerPick.Enabled = musicOn;
        // Remove is for the club's own file only (17 Oct 2026, Bug Hunt 8,
        // A13-1). The paths also resolve to a shipped library piece, which
        // Remove never touches - it was live for one, said it was gone,
        // and the piece played on. The list is how a shipped piece goes.
        _btnOpenerClear.Enabled = musicOn && _showMusic.IsTheirOwn(ShowPart.Opener);
        _btnBedPick.Enabled = musicOn;
        _btnBedClear.Enabled = musicOn && _showMusic.IsTheirOwn(ShowPart.Bed);
        _btnStingPick.Enabled = musicOn;
        _btnStingClear.Enabled = musicOn && _showMusic.IsTheirOwn(ShowPart.Sting);

        // The library lists go dark with the rest of the card, and also
        // while the club is on a file of their OWN for that part: the
        // shipped piece is not what is playing then, so a list that still
        // looked live would be claiming otherwise. Remove puts them back.
        _cmbOpenerPiece.Enabled = musicOn && !_showMusic.IsTheirOwn(ShowPart.Opener);
        _cmbBedPiece.Enabled = musicOn && !_showMusic.IsTheirOwn(ShowPart.Bed);
        _cmbStingPiece.Enabled = musicOn && !_showMusic.IsTheirOwn(ShowPart.Sting);
        _numOpenerLevel.Enabled = musicOn;
        _numStingLevel.Enabled = musicOn;
        _numBedLevel.Enabled = musicOn;
        _numBedDuck.Enabled = musicOn;
        _numVoiceEnters.Enabled = musicOn;

        // The running order shortens the OPENER, so it has nothing to do
        // for a club who have only ever put a bed in.
        _chkShowRejoin.Enabled = musicOn && _showMusic.OpenerPath is not null;

        // Nothing to play: a Test button that does nothing reads as the
        // feature being broken rather than as a club with no files in yet.
        _btnTestShow.Enabled = !_testShowRunning && musicOn && (_showMusic.OpenerPath is not null || _showMusic.BedPath is not null);

        foreach (var box in _showBoxes.Values)
        {
            box.Enabled = musicOn;
        }
    }
}
