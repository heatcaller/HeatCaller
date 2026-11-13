namespace HeatCaller;

/// <summary>
/// Settings dialog for all sound effects: one row per SoundEffectKind, each
/// toggleable with an optional custom audio file, plus the most sounds per
/// lap (the streak thresholds are on Voice > Race Calls and the volume on the
/// Mixer, since 17 Oct 2026). Mimics Next Level Timing's own audio feedback for
/// laps and race lifecycle moments; see AnnouncementService/ToneGenerator
/// for how these get triggered and played.
/// </summary>
public sealed class SoundEffectsControl : UserControl
{
    /// <summary>
    /// One row per sound: a tick for races, a tick for practice, and the
    /// file it plays.
    ///
    /// Both ticks are on screen at once, on purpose. This page used to show
    /// ONE column and a dropdown that chose which of the two it was editing
    /// - and the trouble with a mode is that it is invisible the moment you
    /// stop thinking about it. Tick a row believing you are setting up your
    /// races when the dropdown says practice, and the setting goes somewhere
    /// real, saves without complaint, and does nothing on race night. There
    /// is no warning to add that fixes that; the fix is to stop hiding half
    /// the answer.
    /// </summary>
    private readonly Dictionary<SoundEffectKind, EffectRow> _rows = new();
    private readonly Dictionary<SoundEffectKind, CheckBox> _caution = new();
    private readonly Label _lblContextHint = new();

    // The column headings over the list - see BuildColumnHeadings.
    private readonly Label _lblColumnRace = new();
    private readonly Label _lblColumnPractice = new();
    private readonly Label _lblColumnSound = new();
    private readonly Label _lblColumnPack = new();
    private readonly Label _lblColumnFile = new();
    private readonly Label _lblColumnWeight = new();

    /// <summary>Under the Race and Practice headings: ticks or clears that whole column (ColumnTickBox). Made with the headings.</summary>
    private ColumnTickBox? _allRace;
    private ColumnTickBox? _allPractice;

    /// <summary>
    /// One sound's controls, and the own file it plays.
    ///
    /// The file box shows just the file's NAME - a full path in a box this
    /// narrow only ever showed "C:\Users\..." - with the full path in its
    /// tooltip, and swaps to the full path while it has the focus so a path
    /// can still be typed or pasted. So the path lives here rather than in
    /// the box's text.
    ///
    /// Every control on the row is kept here, not only the ones the page
    /// reads back, because the row is placed again whenever the text size
    /// changes - see PlaceRows.
    /// </summary>
    private sealed class EffectRow
    {
        public EffectRow(
            CheckBox race,
            CheckBox practice,
            Label nameLabel,
            Label? detailLabel,
            ComboBox sound,
            TextBox fileBox,
            Button browse,
            Button remove,
            Button test,
            string filePath)
        {
            Race = race;
            Practice = practice;
            NameLabel = nameLabel;
            DetailLabel = detailLabel;
            Sound = sound;
            FileBox = fileBox;
            Browse = browse;
            Remove = remove;
            Test = test;
            FilePath = filePath;
        }

        public CheckBox Race { get; }
        public CheckBox Practice { get; }

        /// <summary>The sound's name, and the explanation under it when it has one ("all-time best on this track").</summary>
        public Label NameLabel { get; }
        public Label? DetailLabel { get; }

        public ComboBox Sound { get; }
        public TextBox FileBox { get; }
        public Button Browse { get; }
        public Button Remove { get; }
        public Button Test { get; }

        /// <summary>The hairline under the row. None under the last row - a rule under the final row is a line under nothing.</summary>
        public Panel? Rule { get; set; }

        /// <summary>The row's middle, down from the top of the list - what everything on it is centred on. See PlaceRow.</summary>
        public int Middle { get; set; }

        /// <summary>The own file, as a full path; empty for none.</summary>
        public string FilePath { get; set; }

        /// <summary>Set while the page itself rewrites the box, so that isn't read back as typing.</summary>
        public bool Updating { get; set; }
    }

    // The streak thresholds moved to Voice > Race Calls' How often card on
    // 17 Oct 2026: they decide the voice's streak call as much as this
    // page's streak sound. Same store fields (StreakThreshold,
    // StreakThresholdInPractice); BuildProfile carries them through.
    private readonly Dictionary<SoundEffectKind, NumericUpDown> _weights = new();

    /// <summary>"Set every sound to:" - shows the pack every row shares, or "Mixed".</summary>
    private readonly ComboBox _cmbSetAll = NewSoundCombo(null);

    private readonly ToolTip _tips = new();

    /// <summary>Set while the page is being built, so seeding controls doesn't write the file eleven times.</summary>
    private bool _loading = true;

    private readonly System.Windows.Forms.Timer _saveDebounce = new() { Interval = 400 };

    private readonly NumericUpDown _numMaxPerLap = new();
    private readonly Label _lblStatus = new();

    private readonly SoundEffectsStore _store;

    /// <summary>
    /// The app's undo stack, for Remove, Enable All / None and "Set every
    /// sound to:". Optional so an older host that doesn't pass one still
    /// builds; those actions then simply aren't undoable.
    /// </summary>
    private readonly UndoService? _undo;

    /// <summary>
    /// The store's profile as of this page's last load or save. Something
    /// else saving a new one (a soundscape being applied) replaces it - see
    /// Dispose, which must not then write this page's older rows back.
    /// </summary>
    private SoundEffectsProfile _profileShown;

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>
    /// The rows, which scroll down - and sideways, when the page is narrower
    /// than a row - inside the list's frame (StickyHeadings).
    /// </summary>
    private readonly Panel _rowsPanel = new()
    {
        Dock = DockStyle.Fill,
        Padding = new Padding(2),
        AutoScroll = true,

        // Room under the last row, so it is never cut by the panel's edge
        // or sat under the horizontal scrollbar.
        AutoScrollMargin = new Size(RowPad, RowPad + 14),
    };

    /// <summary>The strip the column headings sit on, above the rows - see StickyHeadings.</summary>
    private readonly Panel _track = new();

    /// <summary>The list's frame - the rows and the headings over them - which the page gives the height left over.</summary>
    private Panel? _listFrame;

    /// <summary>The pack pickers' widths as last measured - see Pieces.PickerWidth. Their lists never change, only the text size.</summary>
    private (Font Font, int Count, int Width)? _packWidth;
    private (Font Font, int Count, int Width)? _setAllWidth;

    /// <summary>Set while PlaceRows is placing the rows, so the sizes it gives the fields are not taken for fields resizing themselves.</summary>
    private bool _placing;

    // --- The list's columns ---------------------------------------------
    //
    // Measured in the fonts the page is drawn in, and measured again every
    // time the text size changes (PlaceRows). The list used to be placed at
    // remembered x values (20, 58, 88, 258, 384, 540, 614, 678, 732) chosen
    // against a smaller font: names wrapped onto three lines, "Browse" was
    // cut to "Brows", "Practice" had to be written "Prac", and the rows came
    // out whatever height their name happened to need. Every column below
    // is as wide as the widest thing that goes in it, so each row is the
    // same height and nothing is cut.
    //
    // The rows are the one part of the page not placed by the layout pieces:
    // their columns have to line up with the headings on a strip outside
    // the rows (StickyHeadings), which a table of its own could not do. The
    // list scrolls sideways inside its frame when a row is wider than the
    // page - at the larger text sizes and on a small screen - and nothing
    // else on the page does.

    /// <summary>Space between two columns on a row.</summary>
    private const int ColumnGap = 12;

    /// <summary>Space between the file box and its Browse and Remove, which belong to it and sit close to it and to each other.</summary>
    private const int CloseGap = 6;

    /// <summary>Space above and below a row's controls - the gap between rows is twice this - and in from the list's edges.</summary>
    private const int RowPad = 8;

    /// <summary>Between a sound's name and the explanation under it.</summary>
    private const int NameDetailGap = 2;

    /// <summary>The names' column at its narrowest and widest, at the normal text size - see MeasureColumns.</summary>
    private const int NameLeastWidth = 140;
    private const int NameMostWidth = 300;

    /// <summary>The pack pickers' least width, and the file box's, at the normal text size. Room for a file NAME - the path is in the tooltip.</summary>
    private const int PackPickerWidth = 120;
    private const int FileBoxWidth = 170;

    /// <summary>What an empty file box says.</summary>
    private const string FilePrompt = "(optional own file)";

    /// <summary>The widest number any box on this page holds: a weight or the volume, 100.</summary>
    private const int NumberDigits = 3;

    /// <summary>The fewest rows the list shows before the page scrolls instead.</summary>
    private const int ListLeastRows = 3;

    /// <summary>Where the pack name ends and the long explanation starts: "Track record (all-time best on this track)".</summary>
    private static (string Name, string? Detail) SplitDescription(SoundEffectKind kind)
    {
        var text = SoundEffectsProfile.Describe(kind);
        var open = text.IndexOf(" (", StringComparison.Ordinal);

        return open > 0 && text.EndsWith(")", StringComparison.Ordinal)
            ? (text[..open], text[(open + 2)..^1])
            : (text, null);
    }

    /// <summary>Every x on a row, and every row's height, at the text size in use. See the note above ColumnGap.</summary>
    private sealed class Columns
    {
        public int Race;
        public int RaceWidth;
        public int Practice;
        public int PracticeWidth;
        public int Name;
        public int NameWidth;
        public int Sound;
        public int SoundWidth;
        public int File;
        public int FileWidth;
        public int Browse;
        public int BrowseWidth;
        public int Remove;
        public int RemoveWidth;
        public int Test;
        public int TestWidth;
        public int Weight;
        public int WeightWidth;
        public int Caution;
        public int CautionWidth;

        /// <summary>A row button's height - the theme's own rule for it, so it never resizes one after it is placed.</summary>
        public int ButtonHeight;

        /// <summary>Every row's height: the tallest thing on any row, plus RowPad above and below. 0 until the rows are first placed.</summary>
        public int RowHeight;

        /// <summary>Where a row ends, for the hairline under it and the headings' strip.</summary>
        public int Right => Caution + CautionWidth;
    }

    private readonly Columns _col = new();

    private const string BrowseCaption = "Browse";
    private const string RemoveCaption = "Remove";
    private const string TestCaption = "Test";

    public SoundEffectsControl(SoundEffectsStore store, UndoService? undo = null)
    {
        _store = store;
        _undo = undo;
        var current = store.Current;
        _profileShown = current;

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize.
        //
        // No MinimumSize and no scrolling of its own: the 640x692 floor was
        // kept however small the window was, and scrolled as a whole. A short
        // window scrolls the page instead (FillPage), and a narrow one only
        // the list, sideways inside its frame.
        _textSize.Wear(this, () => AppTheme.BodyFont);

        AppTheme.StyleToolTip(_tips);

        BuildLayout(current);

        _numMaxPerLap.ValueChanged += (_, _) => QueueSave();

        _saveDebounce.Tick += (_, _) => SaveNow();

        // An undo puts settings back in the store, not on this page - see
        // OnStoreRestored.
        _store.Restored += OnStoreRestored;

        UpdateSetAll();

        // Everything above seeded its controls from the stored profile,
        // which fires the change events. The guard comes off now, so the
        // first write is a real edit rather than the page loading itself.
        _loading = false;
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
    /// Builds the page, top to bottom: the intro and the note on the two
    /// ticks; the bulk buttons with "Set every sound to:" beside them (under
    /// them when the line is too narrow) and their note; the list of sounds,
    /// taking the height that is left, in a frame with its column headings
    /// held above the rows (StickyHeadings); then the page-level settings, a
    /// line each - the most sounds per lap (the volume is the Mixer's, and
    /// the streak thresholds are on Voice > Race Calls) - and the status line. On the layout pieces (LayoutPieces.cs),
    /// except the list's rows, whose columns are measured in the fonts in use
    /// and placed again at every text size (PlaceRows); the list scrolls
    /// sideways inside its frame when a row is wider than the page.
    ///
    /// It used to be placed in pixels: a header strip and a settings strip
    /// docked round the list, each stacked by hand from measurements taken
    /// once at build, the hints capped at 600px and the status at 480px, a
    /// 640x692 floor the page kept however small its window - and the list's
    /// columns measured once, in the fonts of the day, so at any other text
    /// size the rows kept the old widths.
    /// </summary>
    private void BuildLayout(SoundEffectsProfile current)
    {
        var lblIntro = new Label
        {
            Text = "Mimics Next Level Timing's audio cues: per-lap feedback plus sounds for race moments.\n" +
                   "Pick a sound for each row - a generated tone or a built-in pack - or your own file, which wins over both.\n" +
                   "\"Weight\" decides which per-lap sounds win when several fire on the same lap - see \"Max sounds per lap\" below.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _lblContextHint.AutoSize = true;
        _lblContextHint.ForeColor = AppTheme.TextMuted;
        _lblContextHint.Text =
            "Two ticks per sound: the first is for RACES, the second for PRACTICE. "
            + "A sound with only the practice box ticked stays silent on race night.";
        _textSize.Wear(_lblContextHint, () => AppTheme.SmallFont);

        // --- The bulk buttons, and one pack for every row ---

        var btnEnableAll = new Button { Text = "Enable All" };
        btnEnableAll.Click += (_, _) => SetAllEnabled(true);

        var btnEnableNone = new Button { Text = "Enable None" };
        btnEnableNone.Click += (_, _) => SetAllEnabled(false);

        var btnTestAll = new Button { Text = "Test All Enabled" };
        btnTestAll.Click += async (_, _) => await TestAllAsync(btnTestAll);

        // One pack for every row at once - the quickest way to hear a whole
        // style, and to put the page back after mixing and matching.
        //
        // It used to show nothing at all: it was deliberately left with no
        // selection, and an empty dropdown reads as a broken one. It now
        // shows the pack every row shares, or "Mixed" when they differ.
        var lblSetAll = new Label { Text = "Set every sound to:", ForeColor = AppTheme.TextMuted };
        _cmbSetAll.Items.Insert(0, new SoundChoice(MixedKey, "Mixed - pick one"));
        _cmbSetAll.SelectionChangeCommitted += (_, _) =>
        {
            if (_cmbSetAll.SelectedItem is not SoundChoice choice || choice.Key == MixedKey)
            {
                return;
            }

            // Each row's pack as it was, so undo gives every row back its
            // own rather than one pack for all.
            var packsBefore = _rows.ToDictionary(r => r.Key.ToString(), r => SoundKeyOf(r.Value.Sound));
            if (packsBefore.Values.Any(k => !string.Equals(k, choice.Key, StringComparison.OrdinalIgnoreCase)))
            {
                CaptureUndo($"Set every sound to {choice}", profile =>
                {
                    foreach (var (key, pack) in packsBefore)
                    {
                        if (profile.Effects.TryGetValue(key, out var config) && config is not null)
                        {
                            config.BuiltInSound = pack;
                        }
                    }
                });
            }

            foreach (var (_, row) in _rows)
            {
                SelectSound(row.Sound, choice.Key);
            }

            _lblStatus.Text = $"Every sound set to {choice}. Your own files, where set, still play instead.";
            _lblStatus.ForeColor = AppTheme.TextMuted;
        };
        _tips.SetToolTip(_cmbSetAll, "Sets the pack on every row at once. Rows with your own file keep playing it.");

        // The buttons, and "Set every sound to:" beside them - or under them
        // when the line is too narrow for both (FieldRow).
        var bulk = new FieldRow();

        bulk.Add(null, new ButtonBar().With(btnEnableAll, btnEnableNone, btnTestAll));
        bulk.Add(lblSetAll, _cmbSetAll, SetAllPickerWidth);

        var lblBulkHint = new Label
        {
            Text = "Enable All / None ticks or clears every sound below - files and volumes are left alone.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblBulkHint, () => AppTheme.SmallFont);

        // --- The list ---

        // Column headings in a strip ABOVE the list, so they stay on screen
        // as it scrolls down, and follow it when it scrolls sideways. They
        // used to be the list's first row and went off the top with it. The
        // frame carries the border the list used to. See StickyHeadings.
        BuildColumnHeadings();

        var list = StickyHeadings.Wrap(_rowsPanel, _track);

        // A hairline between rows, so a row reads as one line across a list
        // this wide.
        //
        // A ONE-PIXEL PANEL EACH, not lines drawn in the panel's Paint.
        //
        // Drawing them looked tidier and was wrong. A scrolling WinForms
        // panel does not repaint when it scrolls - it BLITS what it already
        // has and invalidates only the strip newly come into view. A line
        // drawn at "row bottom plus the scroll offset" is redrawn at its new
        // place while the blitted copy stays where it was, so every few
        // pixels of scrolling leaves another line behind and the panel ends
        // up ruled like a notepad. It showed up first on the taller
        // Announcements list, but the fault was here, in the page that one
        // was modelled on.
        //
        // Invalidating on scroll would fix the scrollbar and the wheel and
        // still miss ScrollControlIntoView, which tabbing between fields
        // calls and which raises no scroll event at all.
        //
        // A Panel is a child like any other: blitted with the row it belongs
        // to, never in the wrong place, never needing a repaint. Tagged
        // KeepBackColor, or the theme pass paints it the page background.
        var kinds = Enum.GetValues<SoundEffectKind>();

        for (var i = 0; i < kinds.Length; i++)
        {
            var config = current.Effects.GetValueOrDefault(kinds[i].ToString()) ?? new SoundEffectConfig();
            var row = BuildRow(kinds[i], config);

            if (i < kinds.Length - 1)
            {
                var rule = new Panel
                {
                    BackColor = AppTheme.Border,
                    Tag = AppTheme.KeepBackColor,
                };

                _rowsPanel.Controls.Add(rule);
                row.Rule = rule;
            }

            _allRace?.Follow(row.Race);
            _allPractice?.Follow(row.Practice);
        }

        _allRace?.Sync();
        _allPractice?.Sync();

        // --- The page-level settings, a line each ---
        //
        // They were at y = 557, 585, 613 while the list above them was
        // anchored to grow - so the list's bottom edge landed on top of them
        // and the threshold row was sliced in half by the panel border. They
        // are the page's own parts now, under the list, each caption with its
        // box beside it (FieldRow).

        // The streak thresholds were the first line here until 17 Oct 2026.
        // They are on Voice > Race Calls' How often card now, beside the
        // voice's own streak call that they also decide.

        // No volume box here any more (17 Oct 2026): the effects' level is
        // the Mixer's Sound effects fader, the one place it is set.
        // BuildProfile carries the stored level through.

        var lblMaxPerLap = new Label
        {
            Text = "Max sounds per lap (good / invalid / fast / streak / leader change):",
            ForeColor = AppTheme.TextDark,
        };

        _numMaxPerLap.Minimum = 1;
        _numMaxPerLap.Maximum = 5;
        _numMaxPerLap.Value = Math.Clamp(current.MaxEffectsPerLap, 1, 5);

        var maxPerLap = new FieldRow();

        maxPerLap.Add(lblMaxPerLap, _numMaxPerLap, NumberBoxWidth);

        // --- The status line ---
        //
        // At the foot of the page, wrapping rather than cut short.
        //
        // There used to be a "Save now" button beside it. It is gone: this
        // page has saved itself on a short debounce for a long time, it
        // flushes anything still pending on the way out, and every other
        // settings page in the app saves as you go without offering a
        // button. A button that is never the thing that saved your work only
        // raises the question of whether the rest of the page did.
        //
        // The line itself stays. The button it used to sit beside was below
        // the fold behind a scrolling list that swallows the wheel - so the
        // page looked as though ticking a box was the whole job. The
        // sentence that says otherwise has to be somewhere it can be read.
        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = AppTheme.TextMuted;
        _lblStatus.Text = "Changes save themselves as you make them.";

        // --- The page ---

        var page = new FillPage();

        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.Add(_lblContextHint, gapBelow: UiLayout.RowGap);
        page.Add(bulk, gapBelow: UiLayout.HintGap);
        page.Add(lblBulkHint, gapBelow: UiLayout.RowGap);
        _listFrame = page.AddFill(list, _ => ListLeastHeight());
        page.Add(maxPerLap, gapBelow: UiLayout.RowGap);
        page.Add(_lblStatus, gapBelow: 0);

        Controls.Add(page);

        // Themed like the vertical one the theme pass gives every scrolling
        // panel: the list is wider than a narrow window, and the native
        // horizontal bar is the white system one.
        ThinScrollBar.AttachHorizontal(_rowsPanel);

        // The list's columns and rows, measured in the faces worn above and
        // placed - now, and again at every text size. Kept last, so every
        // face is on before anything is measured in it, and after the page
        // is in this control, so the pickers and boxes measured wear the
        // body face they inherit from it.
        _textSize.Keep(PlaceRows);
    }

    /// <summary>
    /// A number box on this page, a threshold, the volume or a weight: wide
    /// enough for the widest number any of them holds beside its own arrows,
    /// in the face in use, and never narrower than a number box is at the
    /// normal text size, at this size. One width for all of them, so they
    /// line up down the page.
    ///
    /// The old fixed 60 was chosen against a smaller font and clipped "100"
    /// to "00" - a volume that read as zero when it was set to full.
    /// </summary>
    private static int NumberBoxWidth() =>
        Math.Max(
            UiLayout.AtTextSize(UiLayout.NumericWidth),
            TextRenderer.MeasureText(new string('0', NumberDigits), AppTheme.BodyFont).Width + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra);

    /// <summary>"Set every sound to:" - as wide as its longest choice ("Mixed - pick one"), and never narrower than a pack picker at the normal text size.</summary>
    private int SetAllPickerWidth() =>
        Math.Max(UiLayout.AtTextSize(PackPickerWidth), Pieces.PickerWidth(_cmbSetAll, ref _setAllWidth));

    /// <summary>
    /// A row's pack picker: as wide as its longest pack, and never narrower
    /// than it was at the normal text size. Every row's picker holds the
    /// same packs in the same face, so one of them is measured for all.
    /// </summary>
    private int PackPickerWidthNow()
    {
        var sample = _rows.Values.FirstOrDefault()?.Sound;
        var measured = sample is null ? 0 : Pieces.PickerWidth(sample, ref _packWidth);

        return Math.Max(UiLayout.AtTextSize(PackPickerWidth), measured);
    }

    /// <summary>
    /// The least the list can do with: its frame, the headings and the rule
    /// under them, a few rows, and the sideways bar it has when the page is
    /// narrower than a row.
    /// </summary>
    private int ListLeastHeight()
    {
        var frame = _listFrame is null ? 0 : _listFrame.Height - _listFrame.ClientSize.Height + _listFrame.Padding.Vertical;
        var headings = _track.Parent?.Height ?? _track.Height;

        return frame + headings + ListLeastRows * _col.RowHeight + SystemInformation.HorizontalScrollBarHeight;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // A pending edit must not be lost because the window closed
            // half a second after it was made.
            //
            // This page is also disposed to be REBUILT, straight after a
            // soundscape (or a venue's soundscape) has saved a whole new
            // profile - and the flush then wrote the old page's rows back
            // over it, undoing the soundscape's sounds. That rebuild drops
            // the pending edit first (DropPendingEdit), so a timer still
            // running here is an edit to keep.
            //
            // No longer "only while the store holds the profile this page
            // showed" (6 Nov 2026): the Voice tab's streak thresholds save a
            // new profile object too, and that test then lost a tick made
            // just before the window closed. BuildProfile already takes the
            // thresholds and volume from the store, so nothing is undone.
            if (_saveDebounce.Enabled)
            {
                SaveNow();
            }

            _store.Restored -= OnStoreRestored;
            _saveDebounce.Dispose();
            _tips.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>The "Set every sound to:" entry shown when the rows use different packs. Never a pack key.</summary>
    private const string MixedKey = "*mixed*";

    /// <summary>Shows the pack every row shares in "Set every sound to:", or "Mixed" when they differ.</summary>
    private void UpdateSetAll()
    {
        var keys = _rows.Values.Select(r => SoundKeyOf(r.Sound)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (keys.Count == 1)
        {
            SelectSound(_cmbSetAll, keys[0]);
        }
        else
        {
            _cmbSetAll.SelectedIndex = 0;
        }
    }

    /// <summary>
    /// The headings over the list's columns, on the strip above the rows.
    /// Placed by PlaceHeadings, over the columns they name, once the columns
    /// have been measured.
    /// </summary>
    private void BuildColumnHeadings()
    {
        void Heading(Label label, string text, Color color)
        {
            label.Text = text;
            label.AutoSize = true;
            label.ForeColor = color;
            _textSize.Wear(label, () => AppTheme.MicroFont);
            _track.Controls.Add(label);
        }

        Heading(_lblColumnRace, "Race", AppTheme.Accent);
        Heading(_lblColumnPractice, "Practice", AppTheme.TextMuted);
        Heading(_lblColumnSound, "Sound", AppTheme.TextMuted);
        Heading(_lblColumnPack, "Pack", AppTheme.TextMuted);
        Heading(_lblColumnFile, "Your own file (optional)", AppTheme.TextMuted);
        Heading(_lblColumnWeight, "Weight", AppTheme.TextMuted);

        _tips.SetToolTip(_lblColumnRace, "Ticked sounds play during races.");
        _tips.SetToolTip(_lblColumnPractice, "Ticked sounds play during practice.");

        // Under each tick column's heading, a box that ticks or clears the
        // whole column. The ticks queue the save themselves; this adds the
        // one Undo, as Enable All does.
        ColumnTickBox Column(Func<EffectRow, CheckBox> box, bool race)
        {
            var forWhat = race ? "races" : "practice";

            var all = new ColumnTickBox(() => _rows.Values.Select(box), change =>
            {
                var before = _rows.ToDictionary(r => r.Key, r => box(r.Value).Checked);

                change();

                if (before.All(b => box(_rows[b.Key]).Checked == b.Value))
                {
                    return;
                }

                var ticked = _rows.Values.All(row => box(row).Checked);

                // Undo goes back to the saved profile - the row's ticks
                // follow it when the page reloads (CaptureUndo).
                _lblStatus.Text = ticked ? $"Every sound ticked for {forWhat}." : $"Every sound unticked for {forWhat}.";
                _lblStatus.ForeColor = AppTheme.TextMuted;

                CaptureUndo(ticked ? $"Tick every sound for {forWhat}" : $"Untick every sound for {forWhat}", profile =>
                {
                    foreach (var (key, was) in before)
                    {
                        if (profile.Effects.TryGetValue(key.ToString(), out var config) && config is not null)
                        {
                            if (race)
                            {
                                config.Enabled = was;
                            }
                            else
                            {
                                config.EnabledInPractice = was;
                            }
                        }
                    }
                });
            })
            {
                AccessibleName = $"Tick or clear every sound for {forWhat}",
            };

            _tips.SetToolTip(all, $"Tick every sound for {forWhat} - or, when they're all ticked, clear them.");
            _track.Controls.Add(all);
            return all;
        }

        _allRace = Column(row => row.Race, race: true);
        _allPractice = Column(row => row.Practice, race: false);
    }

    /// <summary>
    /// How big a label's text comes to wrapped to <paramref name="width"/>,
    /// measured the way the label draws it - word breaks and TextRenderer's
    /// padding - which is also how LayoutAudit checks it. A width of
    /// int.MaxValue measures it on one line.
    /// </summary>
    private static Size TextBlock(Label label, int width)
    {
        var flags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;

        if (!label.UseMnemonic)
        {
            flags |= TextFormatFlags.NoPrefix;
        }

        return TextRenderer.MeasureText(label.Text, label.Font, new Size(Math.Max(1, width), int.MaxValue), flags);
    }

    /// <summary>A heading's width on one line, as it sizes itself.</summary>
    private static int HeadingWidth(Label heading) => heading.GetPreferredSize(Size.Empty).Width;

    /// <summary>A row's name and the explanation under it, one block, wrapped to the names' column.</summary>
    private static int NameBlockHeight(EffectRow row, int width) =>
        TextBlock(row.NameLabel, width).Height
        + (row.DetailLabel is { } detail ? NameDetailGap + TextBlock(detail, width).Height : 0);

    /// <summary>
    /// Every column's x and width, and the rows' height, in the fonts in use
    /// now: each column as wide as the widest thing in it - its heading, a
    /// name, a picker's longest pack, a button's caption in the bold face
    /// buttons are drawn in - and the fields' least widths as they were at
    /// the normal text size, at this size.
    /// </summary>
    private void MeasureColumns()
    {
        var c = _col;
        var rows = _rows.Values.ToList();

        // A tick box with no caption is only its box.
        var tick = rows
            .SelectMany(r => new[] { r.Race, r.Practice })
            .Select(box => box.GetPreferredSize(Size.Empty).Width)
            .DefaultIfEmpty(0)
            .Max();

        c.Race = RowPad;
        c.RaceWidth = Math.Max(tick, HeadingWidth(_lblColumnRace));
        c.Practice = c.Race + c.RaceWidth + ColumnGap;
        c.PracticeWidth = Math.Max(tick, HeadingWidth(_lblColumnPractice));

        // A little more room before the names, where the row's words start.
        c.Name = c.Practice + c.PracticeWidth + ColumnGap + 4;

        // As wide as the widest name or explanation, within reason. A name
        // wider than the cap wraps inside the column rather than pushing
        // everything off the right-hand side - and every row is as tall as
        // that takes (below).
        var names = rows
            .SelectMany(r => new[] { r.NameLabel, r.DetailLabel })
            .OfType<Label>()
            .Select(label => TextBlock(label, int.MaxValue).Width)
            .DefaultIfEmpty(0)
            .Max();

        c.NameWidth = Math.Clamp(
            Math.Max(names, HeadingWidth(_lblColumnSound)),
            UiLayout.AtTextSize(NameLeastWidth),
            UiLayout.AtTextSize(NameMostWidth));

        c.Sound = c.Name + c.NameWidth + ColumnGap;
        c.SoundWidth = Math.Max(HeadingWidth(_lblColumnPack), PackPickerWidthNow());

        // Room for a file NAME (the path is in the tooltip) and the prompt,
        // with room round it for the box's own edge.
        c.File = c.Sound + c.SoundWidth + ColumnGap;
        c.FileWidth = Math.Max(
            Math.Max(UiLayout.AtTextSize(FileBoxWidth), HeadingWidth(_lblColumnFile)),
            TextRenderer.MeasureText(FilePrompt, AppTheme.BodyFont).Width + 20);

        // Browse and Remove belong to the file box, so they sit close to it
        // and to each other; Test is its own thing and gets the full gap.
        // Each as wide as its caption in a dense row (UiLayout.TightButtonSizeFor).
        c.Browse = c.File + c.FileWidth + CloseGap;
        c.BrowseWidth = rows.Select(r => UiLayout.TightButtonSizeFor(r.Browse).Width).DefaultIfEmpty(0).Max();
        c.Remove = c.Browse + c.BrowseWidth + CloseGap;
        c.RemoveWidth = rows.Select(r => UiLayout.TightButtonSizeFor(r.Remove).Width).DefaultIfEmpty(0).Max();
        c.Test = c.Remove + c.RemoveWidth + ColumnGap;
        c.TestWidth = rows.Select(r => UiLayout.TightButtonSizeFor(r.Test).Width).DefaultIfEmpty(0).Max();

        // The weight box has to show "100" beside its own spin buttons.
        c.Weight = c.Test + c.TestWidth + ColumnGap;
        c.WeightWidth = Math.Max(HeadingWidth(_lblColumnWeight), NumberBoxWidth());

        c.Caution = c.Weight + c.WeightWidth + ColumnGap;
        c.CautionWidth = _caution.Values.Select(box => box.GetPreferredSize(Size.Empty).Width).DefaultIfEmpty(0).Max();

        // As tall as the theme makes a button for its caption (StyleButton
        // raises any shorter one, which would then grow into the next row).
        c.ButtonHeight = rows
            .SelectMany(r => new[] { r.Browse, r.Remove, r.Test })
            .Select(button => UiLayout.TightButtonSizeFor(button).Height)
            .DefaultIfEmpty(0)
            .Max();

        // Every row as tall as the tallest thing on any of them - a name
        // with its explanation under it, a button, a field, a tick box -
        // with RowPad above and below. Rows used to be as tall as their own
        // name, which wrapped to two or three lines at the old width - so the
        // list was a stack of uneven rows with the controls stuck at
        // different heights.
        var fields = rows
            .SelectMany(r => new Control[] { r.Sound, r.FileBox })
            .Concat(_weights.Values)
            .Select(field => field.Height);

        var boxes = rows
            .SelectMany(r => new[] { r.Race, r.Practice })
            .Concat(_caution.Values)
            .Select(box => box.GetPreferredSize(Size.Empty).Height);

        var blocks = rows.Select(r => NameBlockHeight(r, c.NameWidth));

        c.RowHeight = fields.Concat(boxes).Concat(blocks).Append(c.ButtonHeight).Max() + RowPad * 2;
    }

    /// <summary>
    /// Measures the list's columns in the fonts in use, and puts the
    /// headings, every row and the hairlines between them where they go -
    /// now, and again whenever the text size changes (TextSize.Keep) or a
    /// field on a row grows taller than its row (FieldResized).
    ///
    /// The rows stay where they were in the list however far it has been
    /// scrolled: a child of a scrolled panel sits that far up and to the
    /// left of its place, so each goes to its place plus the scroll position.
    /// </summary>
    private void PlaceRows()
    {
        if (_placing)
        {
            return;
        }

        _placing = true;
        _rowsPanel.SuspendLayout();

        try
        {
            MeasureColumns();
            PlaceHeadings();

            var c = _col;
            var origin = _rowsPanel.AutoScrollPosition;

            // The headings are not in here - they sit in a strip above the
            // rows so they stay on screen. The first row starts at the top.
            var y = 0;

            foreach (var kind in Enum.GetValues<SoundEffectKind>())
            {
                if (!_rows.TryGetValue(kind, out var row))
                {
                    continue;
                }

                PlaceRow(kind, row, y, origin);

                y += c.RowHeight;

                row.Rule?.SetBounds(c.Race + origin.X, y + origin.Y, Math.Max(1, c.Right - c.Race), 1);
            }
        }
        finally
        {
            _rowsPanel.ResumeLayout(true);
            _placing = false;
        }

        // The least the list can do with follows its rows' height and its
        // headings', which the page does not see change.
        Pieces.FillPageOf(_rowsPanel)?.LeastHeightMayHaveChanged();
    }

    /// <summary>
    /// The headings over the columns they name - the ticks' centred over
    /// them, the rest at their column's left - and the strip they sit on as
    /// tall as they are with RowPad above and below, and as wide as a row.
    /// </summary>
    private void PlaceHeadings()
    {
        var c = _col;

        void Over(Label heading, int left, int width, bool centred)
        {
            var size = heading.GetPreferredSize(Size.Empty);
            var x = centred ? left + Math.Max(0, (width - size.Width) / 2) : left;

            heading.SetBounds(x, RowPad, size.Width, size.Height);
        }

        Over(_lblColumnRace, c.Race, c.RaceWidth, centred: true);
        Over(_lblColumnPractice, c.Practice, c.PracticeWidth, centred: true);
        Over(_lblColumnSound, c.Name, c.NameWidth, centred: false);
        Over(_lblColumnPack, c.Sound, c.SoundWidth, centred: false);
        Over(_lblColumnFile, c.File, c.FileWidth, centred: false);
        Over(_lblColumnWeight, c.Weight, c.WeightWidth, centred: false);

        var tallest = new[] { _lblColumnRace, _lblColumnPractice, _lblColumnSound, _lblColumnPack, _lblColumnFile, _lblColumnWeight }
            .Max(heading => heading.GetPreferredSize(Size.Empty).Height);

        // The column tick boxes under their headings, centred in the column
        // as the rows' ticks are.
        var tickTop = RowPad + tallest + UiLayout.HintGap;
        var tickHeight = 0;

        foreach (var (all, left, width) in new[] { (_allRace, c.Race, c.RaceWidth), (_allPractice, c.Practice, c.PracticeWidth) })
        {
            if (all is null)
            {
                continue;
            }

            var size = all.GetPreferredSize(Size.Empty);
            all.SetBounds(left + Math.Max(0, (width - size.Width) / 2), tickTop, size.Width, size.Height);
            tickHeight = Math.Max(tickHeight, size.Height);
        }

        var trackSize = new Size(c.Right + RowPad, RowPad + tallest + (tickHeight > 0 ? UiLayout.HintGap + tickHeight : 0) + RowPad);

        // The strip StickyHeadings put the track in is as tall as the track
        // and the rule under it, worked out once, when the list was wrapped -
        // so it is told how much the track has grown or shrunk. The track's
        // Left is the rows' sideways scroll, which StickyHeadings keeps.
        var grow = trackSize.Height - _track.Height;

        if (_track.Size != trackSize)
        {
            _track.Size = trackSize;
        }

        if (grow != 0 && _track.Parent is { } strip)
        {
            strip.Height += grow;
        }
    }

    /// <summary>
    /// One row, <paramref name="top"/> down the list, every control on it
    /// centred on the row's middle and in its own column - so nothing on
    /// one row reaches another row, or the next column.
    /// </summary>
    private void PlaceRow(SoundEffectKind kind, EffectRow row, int top, Point origin)
    {
        var c = _col;

        row.Middle = top + c.RowHeight / 2;

        // The row's middle where it is now, however far the list is scrolled.
        var middle = row.Middle + origin.Y;

        int CentreTop(int height) => middle - height / 2;

        // A tick box sizes itself; it is given that size, centred in its column.
        void PlaceTick(CheckBox box, int left, int width)
        {
            var size = box.GetPreferredSize(Size.Empty);
            box.SetBounds(origin.X + left + Math.Max(0, (width - size.Width) / 2), CentreTop(size.Height), size.Width, size.Height);
        }

        // A drop-down's or a box's height is its own - see FieldResized.
        void PlaceField(Control field, int left, int width) =>
            field.SetBounds(origin.X + left, CentreTop(field.Height), width, 0, BoundsSpecified.Location | BoundsSpecified.Width);

        void PlaceButton(Button button, int left, int width) =>
            button.SetBounds(origin.X + left, CentreTop(c.ButtonHeight), width, c.ButtonHeight);

        PlaceTick(row.Race, c.Race, c.RaceWidth);
        PlaceTick(row.Practice, c.Practice, c.PracticeWidth);

        // The name on its line, and the explanation that used to wrap it on
        // a smaller line underneath - the two centred as one block.
        var nameHeight = TextBlock(row.NameLabel, c.NameWidth).Height;
        var nameTop = CentreTop(NameBlockHeight(row, c.NameWidth));

        row.NameLabel.SetBounds(origin.X + c.Name, nameTop, c.NameWidth, nameHeight);

        if (row.DetailLabel is { } detail)
        {
            detail.SetBounds(origin.X + c.Name, nameTop + nameHeight + NameDetailGap, c.NameWidth, TextBlock(detail, c.NameWidth).Height);
        }

        PlaceField(row.Sound, c.Sound, c.SoundWidth);
        PlaceField(row.FileBox, c.File, c.FileWidth);
        PlaceButton(row.Browse, c.Browse, c.BrowseWidth);
        PlaceButton(row.Remove, c.Remove, c.RemoveWidth);
        PlaceButton(row.Test, c.Test, c.TestWidth);

        if (_weights.TryGetValue(kind, out var numWeight))
        {
            PlaceField(numWeight, c.Weight, c.WeightWidth);
        }

        if (_caution.TryGetValue(kind, out var chkCaution))
        {
            var caution = chkCaution.GetPreferredSize(Size.Empty);
            chkCaution.SetBounds(origin.X + c.Caution, CentreTop(caution.Height), caution.Width, caution.Height);
        }
    }

    /// <summary>
    /// A field on a row has settled on a height of its own after it was
    /// placed - a drop-down once the theme draws it, a box once its border
    /// goes (Pieces.KeepHeightTold is the same thing for the layout pieces).
    /// It is centred on its row again; and should it no longer fit in one,
    /// every row is placed again, taller. Not before the rows are first
    /// placed, nor while they are being.
    /// </summary>
    private void FieldResized(Control field, EffectRow row)
    {
        if (_placing || _col.RowHeight == 0 || field.IsDisposed)
        {
            return;
        }

        if (field.Height > _col.RowHeight - RowPad * 2)
        {
            PlaceRows();
            return;
        }

        var top = row.Middle + _rowsPanel.AutoScrollPosition.Y - field.Height / 2;

        if (field.Top != top)
        {
            field.Top = top;
        }
    }

    /// <summary>
    /// Ticks or clears every sound's enable box.
    ///
    /// Only touches the checkboxes - file paths, weights and volumes are
    /// left as they are, so switching everything off and back on doesn't
    /// cost someone the custom files they'd set up. Nothing is saved until
    /// Save is pressed, matching how every other edit on this page behaves.
    /// </summary>
    private void SetAllEnabled(bool enabled)
    {
        // Every row's two ticks as they were, so undo puts back the mix
        // someone had set up rather than the opposite blanket choice.
        var ticksBefore = _rows.ToDictionary(r => r.Key.ToString(), r => (Race: r.Value.Race.Checked, Practice: r.Value.Practice.Checked));
        if (ticksBefore.Values.Any(t => t.Race != enabled || t.Practice != enabled))
        {
            CaptureUndo(enabled ? "Enable all sound effects" : "Switch off all sound effects", profile =>
            {
                foreach (var (key, ticks) in ticksBefore)
                {
                    if (profile.Effects.TryGetValue(key, out var config) && config is not null)
                    {
                        config.Enabled = ticks.Race;
                        config.EnabledInPractice = ticks.Practice;
                    }
                }
            });
        }

        foreach (var (_, row) in _rows)
        {
            row.Race.Checked = enabled;
            row.Practice.Checked = enabled;
        }

        // The ticks above have already queued the write - nothing here has
        // to be applied by hand any more.
        _lblStatus.Text = enabled
            ? $"All {_rows.Count} sounds ticked for races and practice."
            : "All sounds unticked for races and practice.";
        _lblStatus.ForeColor = AppTheme.TextMuted;
    }

    private float CurrentVolume() => (float)_store.Current.Volume;

    /// <summary>
    /// Makes one sound's row - its controls, what they do, and the file it
    /// plays - and adds it to the list. Nothing is placed here: PlaceRows
    /// does that, once every row is made, and again at every text size.
    /// </summary>
    private EffectRow BuildRow(SoundEffectKind kind, SoundEffectConfig config)
    {
        var parent = _rowsPanel;

        // The name on one line, and the explanation that used to wrap it -
        // "(all-time best on this track)" - on a smaller line underneath.
        // Sized by PlaceRows to the names' column: a label that sized itself
        // would keep to one line however narrow the column.
        var (name, detail) = SplitDescription(kind);

        var lbl = new Label
        {
            Text = name,
            AutoSize = false,
            ForeColor = AppTheme.TextDark,
        };

        _textSize.Wear(lbl, () => AppTheme.BoldFont);

        Label? lblDetail = null;

        if (detail is not null)
        {
            lblDetail = new Label
            {
                Text = detail,
                AutoSize = false,
                ForeColor = AppTheme.TextMuted,
            };

            _textSize.Wear(lblDetail, () => AppTheme.SmallFont);
        }

        // No pop-up on the name: it only repeated the name and detail
        // already written on the row.

        var chkRace = new ThemedCheckBox { AutoSize = true, Checked = config.Enabled };
        var chkPractice = new ThemedCheckBox { AutoSize = true, Checked = config.EnabledInPractice ?? config.Enabled };

        _tips.SetToolTip(chkRace, $"Play \"{name}\" during races.");
        _tips.SetToolTip(chkPractice, $"Play \"{name}\" during practice.");

        var cmbSound = NewSoundCombo(config.BuiltInSound);
        _tips.SetToolTip(cmbSound, "The built-in sound this row plays - the generated tones or one of the packs. Your own file, if set, plays instead.");

        var fileBox = new TextBox
        {
            PlaceholderText = FilePrompt,
            BackColor = AppTheme.Surface,
            ForeColor = AppTheme.TextDark,
        };
        fileBox.HandleCreated += (_, _) =>
        {
            fileBox.BackColor = AppTheme.Surface;
            fileBox.ForeColor = AppTheme.TextDark;
        };

        var btnBrowse = new Button { Text = BrowseCaption };

        // Takes the row back to its pack sound. Only the setting is
        // cleared: the file was always the club's own, wherever it lives -
        // this page points at it and never copies it anywhere - so there is
        // no copy of the app's to tidy away, and nothing of theirs is
        // deleted.
        var btnRemove = new Button { Text = RemoveCaption };

        var btnTest = new Button { Text = TestCaption };

        var row = new EffectRow(
            chkRace,
            chkPractice,
            lbl,
            lblDetail,
            cmbSound,
            fileBox,
            btnBrowse,
            btnRemove,
            btnTest,
            config.CustomFilePath?.Trim() ?? string.Empty);

        ShowFile(row, editing: false);

        btnBrowse.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "Audio files (*.wav;*.mp3)|*.wav;*.mp3|All files (*.*)|*.*" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                row.FilePath = dialog.FileName;
                ShowFile(row, editing: fileBox.Focused);
                QueueSave();
            }
        };
        _tips.SetToolTip(btnBrowse, "Pick your own sound file for this row. It plays instead of the pack sound.");

        btnRemove.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(row.FilePath))
            {
                return;
            }

            var removed = row.FilePath;

            // Only the setting is cleared (see above), so the path is all
            // undo needs to put this row back on its own file.
            var effectKey = kind.ToString();
            CaptureUndo($"Remove own sound file from {name}", profile =>
            {
                if (profile.Effects.TryGetValue(effectKey, out var config) && config is not null)
                {
                    config.CustomFilePath = removed;
                }
            });

            row.FilePath = string.Empty;
            ShowFile(row, editing: false);
            QueueSave();

            _lblStatus.Text = $"{name} is back to the {SoundPacks.DisplayName(SoundKeyOf(cmbSound))} sound. "
                              + $"\"{DisplayNameOf(removed)}\" is no longer used - the file itself is untouched.";
            _lblStatus.ForeColor = AppTheme.TextMuted;
        };
        _tips.SetToolTip(btnRemove, "Stop using your own file for this row - it goes back to the pack sound. The file itself is not deleted.");

        // Full path while the box is being edited, so one can be typed or
        // pasted; just the name the rest of the time.
        fileBox.Enter += (_, _) => ShowFile(row, editing: true);
        fileBox.Leave += (_, _) => ShowFile(row, editing: false);
        fileBox.TextChanged += (_, _) =>
        {
            if (row.Updating)
            {
                return;
            }

            row.FilePath = fileBox.Text.Trim();
            row.Remove.Enabled = row.FilePath.Length > 0;
            _tips.SetToolTip(fileBox, FileTip(row.FilePath));
            QueueSave();
        };

        btnTest.Click += async (_, _) =>
        {
            btnTest.Enabled = false;
            _lblStatus.Text = $"Playing {SoundEffectsProfile.Describe(kind).ToLowerInvariant()}... (volume={CurrentVolume():0.00})";
            _lblStatus.ForeColor = AppTheme.TextMuted;
            try
            {
                // Looked at before it plays: what the row is about to play.
                var passedOver = OwnFilePassedOver(kind, cmbSound, row.FilePath);

                await PlayRowAsync(kind, cmbSound, row.FilePath);

                if (passedOver is null)
                {
                    _lblStatus.Text = "Done.";
                }
                else
                {
                    _lblStatus.Text = OwnFileNotPlayedStatus(SoundEffectsProfile.Describe(kind), passedOver);
                    _lblStatus.ForeColor = AppTheme.Danger;
                }
            }
            catch (Exception ex)
            {
                _lblStatus.Text = $"Test failed: {ex.Message}";
            }
            finally
            {
                btnTest.Enabled = true;
            }
        };
        _tips.SetToolTip(btnTest, "Play this row as a race would - your own file, else the pack sound.");

        parent.Controls.Add(chkRace);
        parent.Controls.Add(chkPractice);
        parent.Controls.Add(lbl);
        if (lblDetail is not null)
        {
            parent.Controls.Add(lblDetail);
        }

        parent.Controls.Add(cmbSound);
        parent.Controls.Add(fileBox);
        parent.Controls.Add(btnBrowse);
        parent.Controls.Add(btnRemove);
        parent.Controls.Add(btnTest);

        // A field that settles on a height of its own after it is placed is
        // centred on its row again - see FieldResized.
        cmbSound.SizeChanged += (_, _) => FieldResized(cmbSound, row);
        fileBox.SizeChanged += (_, _) => FieldResized(fileBox, row);

        if (SoundEffectsProfile.PerLapKinds.Contains(kind))
        {
            var numWeight = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 100,
                Value = Math.Clamp(config.Weight, 0, 100),
            };
            numWeight.ValueChanged += (_, _) => QueueSave();
            numWeight.SizeChanged += (_, _) => FieldResized(numWeight, row);
            _tips.SetToolTip(numWeight, "When several per-lap sounds fire on the same lap, the higher weights win - see \"Max sounds per lap\" below.");
            parent.Controls.Add(numWeight);
            _weights[kind] = numWeight;

            // Per-lap sounds are held while a flag is out, so chimes don't
            // play over a caution. Tick this for one that should carry on.
            var chkCaution = new ThemedCheckBox
            {
                Text = "Caution",
                AutoSize = true,
                Checked = config.AllowUnderCaution,
                ForeColor = AppTheme.TextMuted,
            };

            _textSize.Wear(chkCaution, () => AppTheme.SmallFont);
            _tips.SetToolTip(chkCaution, "Keep playing this sound while a yellow or red flag is out. Off, it waits for green.");

            chkCaution.CheckedChanged += (_, _) => QueueSave();
            parent.Controls.Add(chkCaution);
            _caution[kind] = chkCaution;
        }

        chkRace.CheckedChanged += (_, _) => QueueSave();
        chkPractice.CheckedChanged += (_, _) => QueueSave();
        cmbSound.SelectedIndexChanged += (_, _) =>
        {
            QueueSave();

            // So "Set every sound to:" says what the rows now share.
            if (!_loading)
            {
                UpdateSetAll();
            }
        };

        _rows[kind] = row;

        return row;
    }

    /// <summary>
    /// Puts a row's own file in its box - the full path while the box is
    /// being edited, just the file's name otherwise - and brings the
    /// tooltip and the Remove button into line with it.
    /// </summary>
    private void ShowFile(EffectRow row, bool editing)
    {
        row.Updating = true;
        try
        {
            row.FileBox.Text = editing ? row.FilePath : DisplayNameOf(row.FilePath);
        }
        finally
        {
            row.Updating = false;
        }

        // Re-asserted after the text changes - a TextBox with PlaceholderText
        // set can silently drop back to the OS default background the moment
        // real text replaces the placeholder, rather than keeping whatever
        // was set at construction time.
        row.FileBox.BackColor = AppTheme.Surface;
        row.FileBox.ForeColor = AppTheme.TextDark;

        row.Remove.Enabled = row.FilePath.Length > 0;
        _tips.SetToolTip(row.FileBox, FileTip(row.FilePath));
    }

    /// <summary>A path's file name, for a box too narrow to show the path. Blank for no file.</summary>
    private static string DisplayNameOf(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            var name = Path.GetFileName(path.Trim());
            return string.IsNullOrEmpty(name) ? path : name;
        }
        catch
        {
            return path;
        }
    }

    private static string FileTip(string path) =>
        string.IsNullOrWhiteSpace(path)
            ? "Your own sound file for this row (optional). Browse to pick one - it plays instead of the pack sound."
            : path;

    /// <summary>One entry in a sound dropdown: "Generated tones" or a pack (Synthwave shown as Classic - see SoundPacks.All).</summary>
    private sealed record SoundChoice(string Key, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>A pack picker. Its width is given where it is placed: a row's by PlaceRow, "Set every sound to:" by its line (FieldRow).</summary>
    private static ComboBox NewSoundCombo(string? selectedKey)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.Add(new SoundChoice(SoundPacks.ClassicKey, SoundPacks.DisplayName(null)));
        foreach (var pack in SoundPacks.All)
        {
            combo.Items.Add(new SoundChoice(pack.Key, pack.Name));
        }

        SelectSound(combo, selectedKey);
        return combo;
    }

    /// <summary>Selects the entry for a pack key; anything unknown shows as Generated tones, which is what it would play.</summary>
    private static void SelectSound(ComboBox combo, string? key)
    {
        var match = SoundPacks.Find(key)?.Key ?? SoundPacks.ClassicKey;
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is SoundChoice c && c.Key == match)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }

    private static string SoundKeyOf(ComboBox combo) => (combo.SelectedItem as SoundChoice)?.Key ?? SoundPacks.ClassicKey;

    /// <summary>Plays a row as a race would pick it: own file, then the chosen pack, then the generated tone.</summary>
    private Task PlayRowAsync(SoundEffectKind kind, ComboBox sound, string filePath)
    {
        var config = new SoundEffectConfig { BuiltInSound = SoundKeyOf(sound), CustomFilePath = filePath.Trim() };
        // The race threshold, as the store holds it - the box is on Voice >
        // Race Calls now.
        var threshold = _store.Current.StreakThresholdFor(practice: false);

        // Test plays the streak as it sounds a couple of laps past the
        // threshold, so the climb is audible on a pack sound.
        return SoundPacks.PlayAsync(config, kind, CurrentVolume(), threshold + 2, threshold);
    }

    /// <summary>
    /// The row's own file when PlayRowAsync would not play it - there, but
    /// not something this PC can decode, or gone - else null.
    ///
    /// WHY (bug hunt 17): such a file falls back to the pack sound or the
    /// tone, and Test said "Done." and Test All "everything played" over a
    /// sound that was not the club's. Before the fallback, silence was the
    /// clue; now the status line says so.
    /// </summary>
    private static string? OwnFilePassedOver(SoundEffectKind kind, ComboBox sound, string filePath) =>
        SoundPacks.OwnFilePassedOver(
            new SoundEffectConfig { BuiltInSound = SoundKeyOf(sound), CustomFilePath = filePath.Trim() }, kind);

    /// <summary>What Test says when the row's own file was passed over and the pack sound or tone played in its place.</summary>
    internal static string OwnFileNotPlayedStatus(string effect, string filePath) =>
        $"{effect}: your own file ({DisplayNameOf(filePath)}) could not be played - it is missing, or not a sound this PC can read. "
        + "The pack sound or tone played instead.";

    /// <summary>What Test All says at the end: every sound played as set, or which rows' own files were passed over.</summary>
    internal static string TestedAllStatus(int tested, IReadOnlyList<string> passedOver) =>
        passedOver.Count == 0
            ? $"Tested all {tested} enabled sounds - everything played."
            : $"Tested all {tested} enabled sounds, but {(passedOver.Count == 1 ? "one of your own files" : $"{passedOver.Count} of your own files")} "
              + $"could not be played - the pack sound or tone played instead: {string.Join(", ", passedOver)}.";

    /// <summary>
    /// Writes the page out as soon as anything on it changes.
    ///
    /// This page used to wait for a Save button, and that button cost two
    /// race nights' worth of sound effects. It sat below the fold on a page
    /// taller than the window, behind a list that swallows the mouse wheel,
    /// so ticking a box looked like the whole job - the ticks were real, the
    /// page looked right, and nothing was ever written. The settings file
    /// went untouched for a day while it was being configured twice.
    ///
    /// Nothing about a louder button or a sterner label fixes that, because
    /// the fault was asking for a second step at all. Every other setting in
    /// this app saves the moment it is changed; this one now does too.
    ///
    /// Debounced, because the file-path boxes would otherwise write once per
    /// keystroke.
    /// </summary>
    private void QueueSave()
    {
        if (_loading)
        {
            return;
        }

        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    /// <summary>
    /// Forgets an edit still waiting to save, so disposing the page doesn't
    /// write it. For the rebuild after a soundscape has saved a whole new
    /// profile - the old page's rows would undo the soundscape's sounds.
    /// </summary>
    internal void DropPendingEdit() => _saveDebounce.Stop();

    private void SaveNow()
    {
        _saveDebounce.Stop();

        // The profile carries the CURRENT stored volume (the Mixer's
        // level) - see BuildProfile.
        var profile = BuildProfile();

        _store.Save(profile);
        _profileShown = _store.Current;

        var races = profile.Effects.Values.Count(c => c.Enabled);
        var practice = profile.Effects.Values.Count(c => c.EnabledInPractice ?? c.Enabled);

        _lblStatus.Text = SavedStatus(_store.LoadedUnreadable, AppTime.TimeWithSeconds(DateTime.Now), races, practice);
        _lblStatus.ForeColor = _store.LoadedUnreadable ? AppTheme.Danger : AppTheme.TextMuted;
    }

    /// <summary>
    /// The status line after a save. 13 Nov 2026, Bug hunt 17: it said
    /// "Saved" even while the store was holding its writes (the file could
    /// not be read at startup - SoundEffectsStore.LoadedUnreadable), so an
    /// evening's set-up looked saved and was gone at the next start.
    /// </summary>
    internal static string SavedStatus(bool notWritten, string time, int races, int practice) =>
        notWritten
            ? "Not saved - the sounds file (sound-effects.json) couldn't be read when HeatCaller started, so it is left as it is. "
              + "These changes play until HeatCaller closes. Restart HeatCaller to save changes."
            : $"Saved {time} - {Plural.Of(races, "sound")} on for races, {practice} for practice.";

    /// <summary>
    /// Puts one of this page's bulk or destructive changes on the undo
    /// stack. Call BEFORE making the change.
    ///
    /// The undo is TARGETED: it copies whatever the store holds when Undo is
    /// pressed and puts back only what <paramref name="putBack"/> names - so
    /// undoing "Enable None" gives the ticks back without also undoing a
    /// pack or a file chosen afterwards. It works on the store, never on this
    /// page (which a soundscape rebuilds), and the page follows through
    /// OnStoreRestored.
    /// </summary>
    private void CaptureUndo(string description, Action<SoundEffectsProfile> putBack)
    {
        if (_undo is null)
        {
            return;
        }

        // Anything still waiting in the save timer goes to the store first,
        // so the undo is built on top of it rather than losing it.
        if (_saveDebounce.Enabled)
        {
            SaveNow();
        }

        var store = _store;
        _undo.Capture(description, () =>
        {
            // A new object, not the stored one changed in place - see
            // SoundEffectsStore.Copy for why that matters to this page.
            var profile = SoundEffectsStore.Copy(store.Current);
            putBack(profile);
            store.Save(profile);
            store.NotifyRestored();
        });
    }

    /// <summary>
    /// Shows what the store now holds after an undo put some of it back.
    /// Only when the store holds a profile this page didn't write - its own
    /// saves are already on screen.
    /// </summary>
    private void OnStoreRestored()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (ReferenceEquals(_store.Current, _profileShown))
        {
            return;
        }

        ShowProfile(_store.Current);
        _lblStatus.Text = $"Undone at {AppTime.TimeWithSeconds(DateTime.Now)} - the sounds below are as they were.";
        _lblStatus.ForeColor = AppTheme.TextMuted;
    }

    /// <summary>
    /// Puts a whole profile on the page without it counting as an edit - the
    /// same guard the constructor fills the page under.
    /// </summary>
    private void ShowProfile(SoundEffectsProfile profile)
    {
        // Whatever was waiting to be written is older than what the store
        // now holds.
        _saveDebounce.Stop();

        var wasLoading = _loading;
        _loading = true;

        try
        {
            foreach (var (kind, row) in _rows)
            {
                var config = profile.Effects.GetValueOrDefault(kind.ToString()) ?? new SoundEffectConfig();

                row.Race.Checked = config.Enabled;
                row.Practice.Checked = config.EnabledInPractice ?? config.Enabled;
                SelectSound(row.Sound, config.BuiltInSound);
                row.FilePath = config.CustomFilePath?.Trim() ?? string.Empty;
                ShowFile(row, editing: row.FileBox.Focused);

                if (_weights.TryGetValue(kind, out var numWeight))
                {
                    numWeight.Value = Math.Clamp(config.Weight, 0, 100);
                }

                if (_caution.TryGetValue(kind, out var chkCaution))
                {
                    chkCaution.Checked = config.AllowUnderCaution;
                }
            }

            _numMaxPerLap.Value = Math.Clamp(profile.MaxEffectsPerLap, 1, 5);
        }
        finally
        {
            _loading = wasLoading;
        }

        _profileShown = profile;
        UpdateSetAll();
    }

    /// <summary>The page, as a profile - read straight off the controls.</summary>
    private SoundEffectsProfile BuildProfile()
    {
        var profile = new SoundEffectsProfile
        {
            // Both as the store holds them: they are set on Voice > Race
            // Calls (17 Oct 2026), as the volume is on the Mixer, so this
            // page carries them through rather than writing over them.
            StreakThreshold = _store.Current.StreakThreshold,
            StreakThresholdInPractice = _store.Current.StreakThresholdInPractice,
            Volume = _store.Current.Volume,
            MaxEffectsPerLap = (int)_numMaxPerLap.Value,
        };

        foreach (var (kind, row) in _rows)
        {
            profile.Effects[kind.ToString()] = new SoundEffectConfig
            {
                // Both straight off the page. Nothing is remembered in a
                // dictionary on the side any more, so there is no way for
                // what gets saved to disagree with what is shown.
                Enabled = row.Race.Checked,
                EnabledInPractice = row.Practice.Checked,
                CustomFilePath = row.FilePath.Trim(),
                BuiltInSound = SoundKeyOf(row.Sound),
                Weight = _weights.TryGetValue(kind, out var numWeight) ? (int)numWeight.Value : 50,
                AllowUnderCaution = _caution.TryGetValue(kind, out var chkCaution) && chkCaution.Checked,
            };
        }

        return profile;
    }

    /// <summary>
    /// Plays every enabled sound in sequence so a whole audio setup can be
    /// verified before an event, rather than clicking Test on each row and
    /// hoping nothing was missed. Runs one at a time with a short gap so
    /// they're distinguishable rather than overlapping into noise.
    /// </summary>
    private async Task TestAllAsync(Button trigger)
    {
        trigger.Enabled = false;
        try
        {
            // Anything ticked for either races or practice - the point is
            // to hear that the files play, and a sound set up for practice
            // is just as worth checking as one set up for races.
            var enabled = _rows.Where(r => r.Value.Race.Checked || r.Value.Practice.Checked).ToList();

            if (enabled.Count == 0)
            {
                _lblStatus.Text = "No sounds are ticked for races or practice - tick some rows first.";
                return;
            }

            // The rows whose own file was passed over for the pack or tone.
            var passedOver = new List<string>();
            _lblStatus.ForeColor = AppTheme.TextMuted;

            for (var i = 0; i < enabled.Count; i++)
            {
                var kind = enabled[i].Key;
                var row = enabled[i].Value;
                _lblStatus.Text = $"Testing {i + 1} of {enabled.Count}: {SoundEffectsProfile.Describe(kind)}...";

                try
                {
                    if (OwnFilePassedOver(kind, row.Sound, row.FilePath) is not null)
                    {
                        passedOver.Add(SoundEffectsProfile.Describe(kind));
                    }

                    await PlayRowAsync(kind, row.Sound, row.FilePath);
                }
                catch (Exception ex)
                {
                    _lblStatus.Text = $"Failed on {SoundEffectsProfile.Describe(kind)}: {ex.Message}";
                    return;
                }

                await Task.Delay(350);
            }

            _lblStatus.Text = TestedAllStatus(enabled.Count, passedOver);
            _lblStatus.ForeColor = passedOver.Count == 0 ? AppTheme.TextMuted : AppTheme.Danger;
        }
        finally
        {
            trigger.Enabled = true;
        }
    }
}
