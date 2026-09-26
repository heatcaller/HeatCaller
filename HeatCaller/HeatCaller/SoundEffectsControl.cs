namespace HeatCaller;

/// <summary>
/// Settings dialog for all sound effects: one row per SoundEffectKind, each
/// toggleable with an optional custom audio file, plus the streak threshold
/// and an overall volume. Mimics Next Level Timing's own audio feedback for
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
    private readonly Label _lblColumnRace = new();
    private readonly Label _lblColumnPractice = new();

    /// <summary>
    /// One sound's controls, and the own file it plays.
    ///
    /// The file box shows just the file's NAME - a full path in a box this
    /// narrow only ever showed "C:\Users\..." - with the full path in its
    /// tooltip, and swaps to the full path while it has the focus so a path
    /// can still be typed or pasted. So the path lives here rather than in
    /// the box's text.
    /// </summary>
    private sealed class EffectRow
    {
        public EffectRow(CheckBox race, CheckBox practice, ComboBox sound, TextBox fileBox, Button remove, string filePath)
        {
            Race = race;
            Practice = practice;
            Sound = sound;
            FileBox = fileBox;
            Remove = remove;
            FilePath = filePath;
        }

        public CheckBox Race { get; }
        public CheckBox Practice { get; }
        public ComboBox Sound { get; }
        public TextBox FileBox { get; }
        public Button Remove { get; }

        /// <summary>The own file, as a full path; empty for none.</summary>
        public string FilePath { get; set; }

        /// <summary>Set while the page itself rewrites the box, so that isn't read back as typing.</summary>
        public bool Updating { get; set; }
    }

    // Streak threshold per context - a single box on screen, two values.
    private readonly Label _lblThreshold = new();
    private readonly Dictionary<SoundEffectKind, NumericUpDown> _weights = new();
    private readonly NumericUpDown _numThreshold = new();

    /// <summary>"Set every sound to:" - shows the pack every row shares, or "Mixed".</summary>
    private readonly ComboBox _cmbSetAll;

    private readonly ToolTip _tips = new();

    /// <summary>Set while the page is being built, so seeding controls doesn't write the file eleven times.</summary>
    private bool _loading = true;

    private readonly System.Windows.Forms.Timer _saveDebounce = new() { Interval = 400 };

    /// <summary>
    /// How wide a spin box has to be to show its own value.
    ///
    /// 60 was chosen against a smaller font and clipped "100" to "00" -
    /// a volume that read as zero when it was set to full.
    /// </summary>
    private const int NumberWidth = 82;
    private readonly Label _lblPracticeThreshold = new();
    private readonly NumericUpDown _numPracticeThreshold = new();
    private readonly NumericUpDown _numVolumePercent = new();
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

    // --- The list's columns ---------------------------------------------
    //
    // Measured once, from the fonts the page is drawn in, before any row is
    // built. The list used to be placed at remembered x values (20, 58, 88,
    // 258, 384, 540, 614, 678, 732) chosen against a smaller font: names
    // wrapped onto three lines, "Browse" was cut to "Brows", "Practice" had
    // to be written "Prac", and the rows came out whatever height their name
    // happened to need. Every column below is as wide as the widest thing
    // that goes in it, so each row is the same height and nothing is cut.

    /// <summary>Space between two controls on a row.</summary>
    private const int ColumnGap = 12;

    /// <summary>Space above and below a row's controls - the gap between rows is twice this.</summary>
    private const int RowPad = 8;

    /// <summary>Space either side of a row button's caption.</summary>
    private const int RowButtonPadX = 14;

    /// <summary>Where the pack name ends and the long explanation starts: "Track record (all-time best on this track)".</summary>
    private static (string Name, string? Detail) SplitDescription(SoundEffectKind kind)
    {
        var text = SoundEffectsProfile.Describe(kind);
        var open = text.IndexOf(" (", StringComparison.Ordinal);

        return open > 0 && text.EndsWith(")", StringComparison.Ordinal)
            ? (text[..open], text[(open + 2)..^1])
            : (text, null);
    }

    private static int TextWidth(string text, Font font) =>
        TextRenderer.MeasureText(
            text,
            font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;

    /// <summary>A row button just wide enough for its caption in the bold face buttons are drawn in.</summary>
    private static int RowButtonWidth(string caption) => TextWidth(caption, AppTheme.BoldFont) + RowButtonPadX * 2;

    /// <summary>Every x on a row, worked out once. See the note above ColumnGap.</summary>
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

        /// <summary>A button's height - the theme's own minimum, so it never resizes one after layout.</summary>
        public int ControlHeight;

        /// <summary>Every row's height, the tallest row's contents plus RowPad above and below.</summary>
        public int RowHeight;
    }

    private readonly Columns _col = new();

    private const string BrowseCaption = "Browse";
    private const string RemoveCaption = "Remove";
    private const string TestCaption = "Test";

    private void MeasureColumns()
    {
        var c = _col;

        // A tick box with no caption is only its glyph.
        const int tickWidth = 18;

        c.Race = UiLayout.PageLeft;
        c.RaceWidth = Math.Max(tickWidth, TextWidth("Race", AppTheme.MicroFont));
        c.Practice = c.Race + c.RaceWidth + ColumnGap;
        c.PracticeWidth = Math.Max(tickWidth, TextWidth("Practice", AppTheme.MicroFont));
        c.Name = c.Practice + c.PracticeWidth + ColumnGap + 4;

        // As wide as the widest name or explanation, within reason. A name
        // wider than the cap wraps rather than pushing everything off the
        // right-hand side.
        var nameWidth = TextWidth("Sound", AppTheme.MicroFont);
        foreach (var kind in Enum.GetValues<SoundEffectKind>())
        {
            var (name, detail) = SplitDescription(kind);
            nameWidth = Math.Max(nameWidth, TextWidth(name, AppTheme.BoldFont) + 4);
            if (detail is not null)
            {
                nameWidth = Math.Max(nameWidth, TextWidth(detail, AppTheme.SmallFont) + 4);
            }
        }

        c.NameWidth = Math.Clamp(nameWidth, 140, 300);

        // The pack dropdown: its longest name, plus room for the chevron.
        var soundWidth = TextWidth(SoundPacks.DisplayName(null), AppTheme.BodyFont);
        foreach (var pack in SoundPacks.All)
        {
            soundWidth = Math.Max(soundWidth, TextWidth(pack.Name, AppTheme.BodyFont));
        }

        c.Sound = c.Name + c.NameWidth + ColumnGap;
        c.SoundWidth = Math.Max(120, soundWidth + 48);

        // Room for a file NAME (the path is in the tooltip) and the prompt.
        c.File = c.Sound + c.SoundWidth + ColumnGap;
        c.FileWidth = Math.Max(170, TextWidth("(optional own file)", AppTheme.BodyFont) + 20);

        // Browse and Remove belong to the file box, so they sit close to it
        // and to each other; Test is its own thing and gets the full gap.
        c.Browse = c.File + c.FileWidth + 6;
        c.BrowseWidth = RowButtonWidth(BrowseCaption);
        c.Remove = c.Browse + c.BrowseWidth + 6;
        c.RemoveWidth = RowButtonWidth(RemoveCaption);
        c.Test = c.Remove + c.RemoveWidth + ColumnGap;
        c.TestWidth = Math.Max(64, RowButtonWidth(TestCaption));

        // The weight box has to show "100" beside its own spin buttons.
        c.Weight = c.Test + c.TestWidth + ColumnGap;
        c.WeightWidth = Math.Max(Math.Max(64, TextWidth("Weight", AppTheme.MicroFont)), TextWidth("100", AppTheme.BodyFont) + 44);
        c.Caution = c.Weight + c.WeightWidth + ColumnGap;

        // The theme raises any button shorter than this (StyleButton), so a
        // row sized for less would have its buttons grow into the next one.
        c.ControlHeight = Math.Max(30, AppTheme.BoldFont.Height + 12);

        // Every row as tall as the tallest: a name with its explanation
        // under it, or a button - whichever is taller.
        var nameBlock = AppTheme.BoldFont.Height + 2 + AppTheme.SmallFont.Height;
        c.RowHeight = Math.Max(c.ControlHeight, nameBlock) + RowPad * 2;
    }

    public SoundEffectsControl(SoundEffectsStore store, UndoService? undo = null)
    {
        _store = store;
        _undo = undo;
        var current = store.Current;
        _profileShown = current;

        Dock = DockStyle.Fill;
        AutoScroll = true;
        MinimumSize = new Size(640, 692);
        Font = AppTheme.BodyFont;

        AppTheme.StyleToolTip(_tips);
        MeasureColumns();

        // Every caption on this page carries its font from the start.
        //
        // They used to inherit it, which only happens once they are added
        // to the page - so each one was MEASURED in the WinForms default
        // font (smaller) and then DRAWN in the page's. Every field placed
        // "just after" its caption landed under the end of it, and the
        // caption, painted on top, hid what was in the field: the races
        // streak threshold showed an empty box while practice's shorter
        // caption only just missed its number, and "Set every sound to:"
        // sat over its own dropdown.
        var lblIntro = new Label
        {
            Text = "Mimics Next Level Timing's audio cues: per-lap feedback plus sounds for race moments.\n" +
                   "Pick a sound for each row - Classic tones or one of the built-in packs - or point it at your own file, which wins over both.\n" +
                   "\"Weight\" decides which per-lap sounds win when several fire on the same lap - see \"Max sounds per lap\" below.",
            Location = new Point(12, 12),
            AutoSize = true,
            Font = AppTheme.BodyFont,
            ForeColor = AppTheme.TextMuted,
        };

        // Everything above the list is stacked by measurement from here
        // down. It was placed at remembered y values - 63, 66, 92, 98, 132 -
        // chosen when the intro was shorter and the font smaller, so the
        // lines grew into each other and the page turned into a pile.
        var headerY = UiLayout.Below(lblIntro, 12);

        _lblContextHint.Location = new Point(12, headerY);
        _lblContextHint.AutoSize = true;
        _lblContextHint.MaximumSize = new Size(600, 0);
        _lblContextHint.Font = AppTheme.SmallFont;
        _lblContextHint.ForeColor = AppTheme.TextMuted;
        _lblContextHint.Text =
            "Two ticks per sound: the first is for RACES, the second for PRACTICE. "
            + "A sound with only the practice box ticked stays silent on race night.";

        headerY += UiLayout.HeightOf(_lblContextHint) + 10;

        var btnEnableAll = new Button { Text = "Enable All" };
        btnEnableAll.Click += (_, _) => SetAllEnabled(true);

        var btnEnableNone = new Button { Text = "Enable None" };
        btnEnableNone.Click += (_, _) => SetAllEnabled(false);

        var btnTestAll = new Button { Text = "Test All Enabled" };
        btnTestAll.Click += async (_, _) => await TestAllAsync(btnTestAll);

        UiLayout.ButtonRow(12, headerY, btnEnableAll, btnEnableNone, btnTestAll);

        // One pack for every row at once - the quickest way to hear a whole
        // style, and to put the page back after mixing and matching.
        //
        // It used to show nothing at all: it was deliberately left with no
        // selection, and an empty dropdown reads as a broken one. It now
        // shows the pack every row shares, or "Mixed" when they differ.
        var lblSetAll = new Label { Text = "Set every sound to:", AutoSize = true, Font = AppTheme.BodyFont, ForeColor = AppTheme.TextMuted };
        _cmbSetAll = NewSoundCombo(null);
        _cmbSetAll.Items.Insert(0, new SoundChoice(MixedKey, "Mixed - pick one"));
        _cmbSetAll.Font = AppTheme.BodyFont;
        _cmbSetAll.Width = _col.SoundWidth + 30;
        lblSetAll.Location = new Point(btnTestAll.Right + UiLayout.ButtonGap * 3, UiLayout.CentreOn(lblSetAll, headerY, UiLayout.ButtonHeight));
        _cmbSetAll.Location = new Point(
            UiLayout.After(lblSetAll),
            headerY + Math.Max(0, (UiLayout.ButtonHeight - Math.Max(_cmbSetAll.Height, _cmbSetAll.PreferredSize.Height)) / 2));
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

        headerY += UiLayout.ButtonHeight + 8;

        var lblBulkHint = new Label
        {
            Text = "Enable All / None ticks or clears every sound below - files and volumes are left alone.",
            Location = new Point(12, headerY),
            AutoSize = true,
            MaximumSize = new Size(600, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        headerY += UiLayout.HeightOf(lblBulkHint) + 10;

        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(2),
            AutoScroll = true,

            // Room under the last row, so it is never cut by the panel's
            // edge or sat under the horizontal scrollbar.
            AutoScrollMargin = new Size(UiLayout.PageRight, RowPad + 14),
        };

        // Column headings in a strip ABOVE the list, so they stay on screen
        // as it scrolls down, and follow it when it scrolls sideways. They
        // used to be the list's first row and went off the top with it. The
        // frame carries the border the list used to. See StickyHeadings.
        var headings = new Panel();
        headings.Height = BuildColumnHeadings(headings, 8) + 4;

        var list = StickyHeadings.Wrap(scroll, headings);

        var y = 6;

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
            BuildRow(scroll, kinds[i], config, ref y);

            if (i < kinds.Length - 1)
            {
                scroll.Controls.Add(new Panel
                {
                    BackColor = AppTheme.Border,
                    Location = new Point(UiLayout.PageLeft, y),
                    Size = new Size(Math.Max(1, _col.Caution + 120 - UiLayout.PageLeft), 1),
                    Tag = AppTheme.KeepBackColor,
                });
            }
        }

        // The four page-level settings get a strip of their own, laid out
        // left to right by measurement.
        //
        // They were at y = 557, 585, 613 while the list above them was
        // anchored to grow - so the list's bottom edge landed on top of
        // them and the threshold row was sliced in half by the panel
        // border. Nothing on this page is placed by remembered pixels any
        // more: the header is a strip, the list fills what is left, and
        // these sit between it and the Save bar.
        var settings = new Panel { Dock = DockStyle.Bottom, Padding = new Padding(12, 10, 12, 6) };

        var settingsY = 10;

        // One step for a caption-and-box line, tall enough that a spin box
        // and its caption never touch the line above or below.
        var rowStep = Math.Max(UiLayout.RowHeight, AppTheme.BodyFont.Height + 14) + 10;

        _numThreshold.Font = AppTheme.BodyFont;
        _numPracticeThreshold.Font = AppTheme.BodyFont;
        _numVolumePercent.Font = AppTheme.BodyFont;
        _numMaxPerLap.Font = AppTheme.BodyFont;

        _lblThreshold.Text = "Streak threshold - races:";
        _lblThreshold.AutoSize = true;
        _lblThreshold.Font = AppTheme.BodyFont;
        _lblThreshold.ForeColor = AppTheme.TextDark;
        _lblThreshold.Location = new Point(12, UiLayout.CentreOn(_lblThreshold, settingsY, _numThreshold.PreferredHeight));

        _numThreshold.Location = new Point(UiLayout.After(_lblThreshold), settingsY);
        _numThreshold.Width = NumberWidth;
        // Two is the floor, not one - a run of one lap is a lap. See
        // SoundEffectsProfile.StreakThresholdFor.
        _numThreshold.Minimum = 2;
        _numThreshold.Maximum = 50;
        _numThreshold.Value = Math.Clamp(current.StreakThreshold, 2, 50);

        _lblPracticeThreshold.Text = "practice:";
        _lblPracticeThreshold.AutoSize = true;
        _lblPracticeThreshold.Font = AppTheme.BodyFont;
        _lblPracticeThreshold.ForeColor = AppTheme.TextMuted;
        _lblPracticeThreshold.Location = new Point(
            _numThreshold.Right + UiLayout.ButtonGap * 3,
            UiLayout.CentreOn(_lblPracticeThreshold, settingsY, _numPracticeThreshold.PreferredHeight));

        _numPracticeThreshold.Location = new Point(UiLayout.After(_lblPracticeThreshold), settingsY);
        _numPracticeThreshold.Width = NumberWidth;
        _numPracticeThreshold.Minimum = 2;
        _numPracticeThreshold.Maximum = 50;
        _numPracticeThreshold.Value =
            Math.Clamp(current.StreakThresholdInPractice ?? current.StreakThreshold, 2, 50);

        settingsY += rowStep;

        // Volume gets its own line rather than being crammed on the end of
        // the thresholds - three caption-and-box pairs on one row is how
        // that line came out unreadable.
        var lblVolume = new Label { Text = "Sound effects volume (%):", AutoSize = true, Font = AppTheme.BodyFont, ForeColor = AppTheme.TextDark };
        lblVolume.Location = new Point(12, UiLayout.CentreOn(lblVolume, settingsY, _numVolumePercent.PreferredHeight));

        _numVolumePercent.Location = new Point(UiLayout.After(lblVolume), settingsY);
        _numVolumePercent.Width = NumberWidth;
        _numVolumePercent.Minimum = 0;
        _numVolumePercent.Maximum = 100;
        _numVolumePercent.Increment = 5;
        _numVolumePercent.Value = (decimal)Math.Clamp(current.Volume * 100.0, 0, 100);
        _volumeShown = _numVolumePercent.Value;

        settingsY += rowStep;

        var lblMaxPerLap = new Label
        {
            Text = "Max sounds per lap (good / invalid / fast / streak / leader change):",
            AutoSize = true,
            Font = AppTheme.BodyFont,
            ForeColor = AppTheme.TextDark,
        };
        lblMaxPerLap.Location = new Point(12, UiLayout.CentreOn(lblMaxPerLap, settingsY, _numMaxPerLap.PreferredHeight));

        _numMaxPerLap.Location = new Point(UiLayout.After(lblMaxPerLap), settingsY);
        _numMaxPerLap.Width = NumberWidth;
        _numMaxPerLap.Minimum = 1;
        _numMaxPerLap.Maximum = 5;
        _numMaxPerLap.Value = Math.Clamp(current.MaxEffectsPerLap, 1, 5);

        settings.Controls.AddRange(new Control[]
        {
            _lblThreshold, _numThreshold, _lblPracticeThreshold, _numPracticeThreshold,
            lblVolume, _numVolumePercent, lblMaxPerLap, _numMaxPerLap,
        });

        settings.Height = settingsY + rowStep + 6;

        // The status line lives in a strip pinned to the bottom of the page,
        // always on screen.
        //
        // There used to be a "Save now" button beside it. It is gone: this
        // page has saved itself on a short debounce for a long time, it
        // flushes anything still pending on the way out, and every other
        // settings page in the app saves as you go without offering a
        // button. A button that is never the thing that saved your work only
        // raises the question of whether the rest of the page did.
        //
        // The strip itself stays, and so does the line in it. The button it
        // used to hold sat at y=652 on a page around 560 tall, below the
        // fold behind a scrolling list that swallows the wheel - so the page
        // looked as though ticking a box was the whole job. The sentence
        // that says otherwise has to be somewhere it can be read.
        var footer = new Panel { Dock = DockStyle.Bottom, Height = UiLayout.ButtonHeight + 20 };

        _lblStatus.Location = new Point(12, 16);
        _lblStatus.AutoSize = true;
        _lblStatus.Font = AppTheme.BodyFont;
        _lblStatus.ForeColor = AppTheme.TextMuted;
        _lblStatus.MaximumSize = new Size(480, 0);
        _lblStatus.Text = "Changes save themselves as you make them.";

        footer.Controls.Add(_lblStatus);

        var header = new Panel { Dock = DockStyle.Top, Height = headerY + 4 };

        header.Controls.AddRange(new Control[]
        {
            lblIntro, _lblContextHint, btnEnableAll, btnEnableNone, btnTestAll, lblSetAll, _cmbSetAll,
            lblBulkHint,
        });

        // Fill first, edges after: WinForms lays docked children out from
        // the LAST one added to the first, so the Fill has to be earliest
        // in the list to be given whatever the strips did not take.
        Controls.Add(list);
        Controls.Add(header);
        Controls.Add(settings);
        Controls.Add(footer);

        // Themed like the vertical one the theme pass gives every scrolling
        // panel: the list is wider than a narrow window, and the native
        // horizontal bar is the white system one.
        ThinScrollBar.AttachHorizontal(scroll);

        _numThreshold.ValueChanged += (_, _) => QueueSave();
        _numPracticeThreshold.ValueChanged += (_, _) => QueueSave();
        _numVolumePercent.ValueChanged += (_, _) => QueueSave();
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // A pending edit must not be lost because the window closed
            // half a second after it was made.
            //
            // But only onto the profile this page was showing. This page is
            // also disposed to be REBUILT, straight after a soundscape (or a
            // venue's soundscape) has saved a whole new profile - and the
            // flush then wrote the old page's rows back over it, undoing the
            // soundscape's sounds. A different profile in the store means
            // someone else has saved since, and theirs is the newer one.
            if (_saveDebounce.Enabled && ReferenceEquals(_store.Current, _profileShown))
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

    /// <summary>The headings over the list's columns. Returns the y the first row starts at.</summary>
    private int BuildColumnHeadings(Control parent, int y)
    {
        var c = _col;

        Label Heading(string text, int x, int width, bool accent = false)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                Font = AppTheme.MicroFont,
                ForeColor = accent ? AppTheme.Accent : AppTheme.TextMuted,
            };

            // Centred over a narrow column (the ticks), left-aligned over a wide one.
            var textWidth = TextWidth(text, AppTheme.MicroFont);
            label.Location = new Point(width < 60 ? x + Math.Max(0, (width - textWidth) / 2) - 1 : x, y);
            parent.Controls.Add(label);
            return label;
        }

        _lblColumnRace.Text = "Race";
        _lblColumnRace.AutoSize = true;
        _lblColumnRace.Font = AppTheme.MicroFont;
        _lblColumnRace.ForeColor = AppTheme.Accent;
        _lblColumnRace.Location = new Point(c.Race + Math.Max(0, (c.RaceWidth - TextWidth("Race", AppTheme.MicroFont)) / 2), y);
        parent.Controls.Add(_lblColumnRace);

        _lblColumnPractice.Text = "Practice";
        _lblColumnPractice.AutoSize = true;
        _lblColumnPractice.Font = AppTheme.MicroFont;
        _lblColumnPractice.ForeColor = AppTheme.TextMuted;
        _lblColumnPractice.Location = new Point(c.Practice + Math.Max(0, (c.PracticeWidth - TextWidth("Practice", AppTheme.MicroFont)) / 2), y);
        parent.Controls.Add(_lblColumnPractice);

        _tips.SetToolTip(_lblColumnRace, "Ticked sounds play during races.");
        _tips.SetToolTip(_lblColumnPractice, "Ticked sounds play during practice.");

        Heading("Sound", c.Name, c.NameWidth);
        Heading("Pack", c.Sound, c.SoundWidth);
        Heading("Your own file (optional)", c.File, c.FileWidth);
        Heading("Weight", c.Weight, c.WeightWidth);

        return y + UiLayout.HeightOf(_lblColumnRace) + 4;
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


    private float CurrentVolume() => (float)(_numVolumePercent.Value / 100m);

    private void BuildRow(Control parent, SoundEffectKind kind, SoundEffectConfig config, ref int y)
    {
        var c = _col;

        // Every row the same height, with every control centred on the
        // row's middle. Rows used to be as tall as their own name, which
        // wrapped to two or three lines at the old width - so the list was a
        // stack of uneven rows with the controls stuck at different heights.
        var middle = y + c.RowHeight / 2;

        int CentreTop(int height) => middle - height / 2;

        // The name on one line, and the explanation that used to wrap it -
        // "(all-time best on this track)" - on a smaller line underneath.
        var (name, detail) = SplitDescription(kind);

        var lbl = new Label
        {
            Text = name,
            AutoSize = true,
            MaximumSize = new Size(c.NameWidth, 0),
            Font = AppTheme.BoldFont,
            ForeColor = AppTheme.TextDark,
        };

        Label? lblDetail = detail is null
            ? null
            : new Label
            {
                Text = detail,
                AutoSize = true,
                MaximumSize = new Size(c.NameWidth, 0),
                Font = AppTheme.SmallFont,
                ForeColor = AppTheme.TextMuted,
            };

        var nameBlock = UiLayout.HeightOf(lbl) + (lblDetail is null ? 0 : 2 + UiLayout.HeightOf(lblDetail));
        lbl.Location = new Point(c.Name, CentreTop(nameBlock));
        if (lblDetail is not null)
        {
            lblDetail.Location = new Point(c.Name, lbl.Top + UiLayout.HeightOf(lbl) + 2);
        }

        // No pop-up on the name: it only repeated the name and detail
        // already written on the row.

        var chkRace = new CheckBox { AutoSize = true, Checked = config.Enabled };
        chkRace.Location = new Point(c.Race + Math.Max(0, (c.RaceWidth - chkRace.PreferredSize.Width) / 2), CentreTop(chkRace.PreferredSize.Height));

        var chkPractice = new CheckBox { AutoSize = true, Checked = config.EnabledInPractice ?? config.Enabled };
        chkPractice.Location = new Point(c.Practice + Math.Max(0, (c.PracticeWidth - chkPractice.PreferredSize.Width) / 2), CentreTop(chkPractice.PreferredSize.Height));

        _tips.SetToolTip(chkRace, $"Play \"{name}\" during races.");
        _tips.SetToolTip(chkPractice, $"Play \"{name}\" during practice.");

        var cmbSound = NewSoundCombo(config.BuiltInSound);
        cmbSound.Font = AppTheme.BodyFont;
        cmbSound.Width = c.SoundWidth;
        cmbSound.Location = new Point(c.Sound, CentreTop(Math.Max(cmbSound.Height, cmbSound.PreferredSize.Height)));
        _tips.SetToolTip(cmbSound, "The built-in sound this row plays - Classic tones or one of the packs. Your own file, if set, plays instead.");

        var fileBox = new TextBox
        {
            Font = AppTheme.BodyFont,
            Width = c.FileWidth,
            PlaceholderText = "(optional own file)",
            BackColor = AppTheme.Surface,
            ForeColor = AppTheme.TextDark,
        };
        fileBox.Location = new Point(c.File, CentreTop(fileBox.PreferredHeight));
        fileBox.HandleCreated += (_, _) =>
        {
            fileBox.BackColor = AppTheme.Surface;
            fileBox.ForeColor = AppTheme.TextDark;
        };

        var btnBrowse = new Button
        {
            Text = BrowseCaption,
            Size = new Size(c.BrowseWidth, c.ControlHeight),
            Location = new Point(c.Browse, CentreTop(c.ControlHeight)),
        };

        // Takes the row back to its pack sound. Only the setting is
        // cleared: the file was always the club's own, wherever it lives -
        // this page points at it and never copies it anywhere - so there is
        // no copy of the app's to tidy away, and nothing of theirs is
        // deleted.
        var btnRemove = new Button
        {
            Text = RemoveCaption,
            Size = new Size(c.RemoveWidth, c.ControlHeight),
            Location = new Point(c.Remove, CentreTop(c.ControlHeight)),
        };

        var row = new EffectRow(chkRace, chkPractice, cmbSound, fileBox, btnRemove, config.CustomFilePath?.Trim() ?? string.Empty);
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

        var btnTest = new Button
        {
            Text = TestCaption,
            Size = new Size(c.TestWidth, c.ControlHeight),
            Location = new Point(c.Test, CentreTop(c.ControlHeight)),
        };
        btnTest.Click += async (_, _) =>
        {
            btnTest.Enabled = false;
            _lblStatus.Text = $"Playing {SoundEffectsProfile.Describe(kind).ToLowerInvariant()}... (volume={CurrentVolume():0.00})";
            try
            {
                await PlayRowAsync(kind, cmbSound, row.FilePath);
                _lblStatus.Text = "Done.";
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

        if (SoundEffectsProfile.PerLapKinds.Contains(kind))
        {
            var numWeight = new NumericUpDown
            {
                Font = AppTheme.BodyFont,
                Width = c.WeightWidth,
                Minimum = 0,
                Maximum = 100,
                Value = Math.Clamp(config.Weight, 0, 100),
            };
            numWeight.Location = new Point(c.Weight, CentreTop(numWeight.PreferredHeight));
            numWeight.ValueChanged += (_, _) => QueueSave();
            _tips.SetToolTip(numWeight, "When several per-lap sounds fire on the same lap, the higher weights win - see \"Max sounds per lap\" below.");
            parent.Controls.Add(numWeight);
            _weights[kind] = numWeight;

            // Per-lap sounds are held while a flag is out, so chimes don't
            // play over a caution. Tick this for one that should carry on.
            var chkCaution = new CheckBox
            {
                Text = "Caution",
                AutoSize = true,
                Checked = config.AllowUnderCaution,
                Font = AppTheme.SmallFont,
                ForeColor = AppTheme.TextMuted,
            };
            chkCaution.Location = new Point(c.Caution, CentreTop(chkCaution.PreferredSize.Height));
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

        y += c.RowHeight;
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

    /// <summary>One entry in a sound dropdown: "Classic tones" or a pack.</summary>
    private sealed record SoundChoice(string Key, string Name)
    {
        public override string ToString() => Name;
    }

    private static ComboBox NewSoundCombo(string? selectedKey)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        combo.Items.Add(new SoundChoice(SoundPacks.ClassicKey, SoundPacks.DisplayName(null)));
        foreach (var pack in SoundPacks.All)
        {
            combo.Items.Add(new SoundChoice(pack.Key, pack.Name));
        }

        SelectSound(combo, selectedKey);
        return combo;
    }

    /// <summary>Selects the entry for a pack key; anything unknown shows as Classic tones, which is what it would play.</summary>
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
        var threshold = (int)_numThreshold.Value;

        // Test plays the streak as it sounds a couple of laps past the
        // threshold, so the climb is audible on a pack sound.
        return SoundPacks.PlayAsync(config, kind, CurrentVolume(), threshold + 2, threshold);
    }

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
    /// The volume box's value as last loaded from, or saved to, the store.
    /// If the box still shows this, nobody has changed it on THIS page.
    /// </summary>
    private decimal _volumeShown;

    /// <summary>
    /// Brings the volume box up to date with the saved level.
    ///
    /// The Mixer page writes the same stored volume, but this page read it
    /// once when it was built - so the next tick or file change here saved
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

        var wasLoading = _loading;
        _loading = true;

        try
        {
            _numVolumePercent.Value = stored;
        }
        finally
        {
            _loading = wasLoading;
        }

        _volumeShown = stored;
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        // Coming back to this page after using the Mixer should show the
        // level the Mixer set, not the one from when the page was built.
        if (Visible)
        {
            SyncVolumeFromStore();
        }
    }

    private void SaveNow()
    {
        _saveDebounce.Stop();

        // Before building the profile, so a save triggered by some other
        // control carries the CURRENT stored volume rather than a stale one.
        SyncVolumeFromStore();

        var profile = BuildProfile();

        _store.Save(profile);
        _profileShown = _store.Current;
        _volumeShown = _numVolumePercent.Value;

        var races = profile.Effects.Values.Count(c => c.Enabled);
        var practice = profile.Effects.Values.Count(c => c.EnabledInPractice ?? c.Enabled);

        _lblStatus.Text = $"Saved {DateTime.Now:HH:mm:ss} - {Plural.Of(races, "sound")} on for races, {practice} for practice.";
        _lblStatus.ForeColor = AppTheme.TextMuted;
    }

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
            SyncVolumeFromStore();
            return;
        }

        ShowProfile(_store.Current);
        _lblStatus.Text = $"Undone at {DateTime.Now:HH:mm:ss} - the sounds below are as they were.";
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

            _numThreshold.Value = Math.Clamp(profile.StreakThreshold, 2, 50);
            _numPracticeThreshold.Value = Math.Clamp(profile.StreakThresholdInPractice ?? profile.StreakThreshold, 2, 50);
            _numVolumePercent.Value = (decimal)Math.Clamp(profile.Volume * 100.0, 0, 100);
            _numMaxPerLap.Value = Math.Clamp(profile.MaxEffectsPerLap, 1, 5);
        }
        finally
        {
            _loading = wasLoading;
        }

        _volumeShown = _numVolumePercent.Value;
        _profileShown = profile;
        UpdateSetAll();
    }

    /// <summary>The page, as a profile - read straight off the controls.</summary>
    private SoundEffectsProfile BuildProfile()
    {
        var profile = new SoundEffectsProfile
        {
            StreakThreshold = (int)_numThreshold.Value,
            StreakThresholdInPractice = (int)_numPracticeThreshold.Value,
            Volume = (double)(_numVolumePercent.Value / 100m),
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

            for (var i = 0; i < enabled.Count; i++)
            {
                var kind = enabled[i].Key;
                var row = enabled[i].Value;
                _lblStatus.Text = $"Testing {i + 1} of {enabled.Count}: {SoundEffectsProfile.Describe(kind)}...";

                try
                {
                    await PlayRowAsync(kind, row.Sound, row.FilePath);
                }
                catch (Exception ex)
                {
                    _lblStatus.Text = $"Failed on {SoundEffectsProfile.Describe(kind)}: {ex.Message}";
                    return;
                }

                await Task.Delay(350);
            }

            _lblStatus.Text = $"Tested all {enabled.Count} enabled sounds - everything played.";
        }
        finally
        {
            trigger.Enabled = true;
        }
    }
}
