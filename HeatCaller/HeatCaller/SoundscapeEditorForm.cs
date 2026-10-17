namespace HeatCaller;

/// <summary>
/// Edits one soundscape in place: its name, the sound for every effect,
/// and the announcer's delivery and lines for the race moments a
/// soundscape gives a personality to.
///
/// Works on a copy handed in by ThemesControl and hands back
/// <see cref="Result"/> only on Save, so Cancel really does leave the
/// soundscape as it was. Custom sound FILES on an effect are kept as they
/// are - this form picks packs; the Sound Effects page is where a file is
/// chosen - and the row says when one is set, since it plays instead.
///
/// On the layout pieces (DialogFit): the name, the description and what
/// plays underneath lined up in a field grid across the top; under them two
/// cards side by side (a Beside) taking the height the window has - the
/// sounds down the left, Set all to: over them and the sounds scrolling on
/// their own, the card as wide as its widest line; and the announcer beside
/// it taking the rest of the width, its delivery along one line, the lines'
/// hint, and the race moments beside the lines of the one picked, the lines
/// taking the height. Under the cards the note about the HeatCaller voice,
/// when this PC speaks with it, and along the foot Preview and what it is
/// doing on the left, and Delete Soundscape, Save and Cancel on the right.
/// It opens at the size it always had, and can be made bigger. It used to
/// be placed in pixels in a fixed 1000x790 window - taller than a 1280x720
/// screen has room for, so Save and Cancel opened under its edge - with two
/// group boxes of fixed sizes and every caption and field at a fixed place,
/// whatever the text size.
/// </summary>
public sealed class SoundscapeEditorForm : Form
{
    /// <summary>
    /// The window's inside at the normal text size - the size it always
    /// opened at. It opens this big at the text size in use, never bigger
    /// than the screen, and can be made bigger, the two cards taking the
    /// room.
    /// </summary>
    private const int DialogWidth = 1000;
    private const int DialogHeight = 790;

    /// <summary>The name box, and the background and music drop-downs, at the normal text size, as they were - a drop-down wider when its longest choice needs it.</summary>
    private const int NameWidth = 360;
    private const int AmbienceWidth = 250;
    private const int MusicWidth = 530;

    /// <summary>A sound's pack drop-down at the normal text size, as it was - wider when its longest pack needs it.</summary>
    private const int PackPickerWidth = 120;

    /// <summary>The list of race moments at the normal text size, as it was - wider when its longest name needs it.</summary>
    private const int MomentsWidth = 170;

    /// <summary>The description box shows this many lines, as it did.</summary>
    private const int DescriptionLines = 2;

    /// <summary>The sounds show at least this many before the window scrolls instead.</summary>
    private const int LeastSoundRows = 3;

    /// <summary>The race moments show at least this many before the window scrolls instead.</summary>
    private const int LeastMomentRows = 4;

    /// <summary>The lines box shows at least this many lines before the window scrolls instead.</summary>
    private const int LeastLines = 3;

    private readonly Theme _working;
    private readonly ThemeStore _store;
    private readonly Func<Theme, CancellationToken, Task> _preview;
    private readonly Action _stopVoice;

    private readonly TextBox _txtName = new();
    private readonly TextBox _txtDescription = new();
    private readonly Dictionary<SoundEffectKind, ComboBox> _sounds = new();
    private readonly ComboBox _cmbAmbience = new();
    private readonly ComboBox _cmbMusic = new();
    private readonly IReadOnlyList<string> _musicLibrary;

    private readonly NumericUpDown _numStability = new();
    private readonly NumericUpDown _numStyle = new();
    private readonly NumericUpDown _numSpeed = new();

    private readonly ListBox _lstEvents = new();
    private readonly CheckBox _chkEventEnabled = new ThemedCheckBox();
    private readonly TextBox _txtLines = new();
    private AnnouncementEventType? _shownEvent;

    private readonly Button _btnPreview = new();

    /// <summary>
    /// What the preview is doing, beside Preview. As wide as the longest
    /// thing it says, whatever it says now (SteadyLabel) - so the foot's
    /// line never changes as it is rewritten.
    /// </summary>
    private readonly SteadyLabel _lblStatus = new()
    {
        Wordings = new[] { "Playing a short pretend race...", "Preview finished.", "Preview couldn't play.", "Preview stopped." },
    };

    private CancellationTokenSource? _previewCts;

    /// <summary>Set all to:, and the sounds under it - two grids with one caption column (FieldGrid.LineUp), so every pack drop-down lines up.</summary>
    private readonly FieldGrid _setAllGrid = new();
    private readonly FieldGrid _soundGrid = new();

    /// <summary>The captions of both, which that column is as wide as the widest of.</summary>
    private readonly List<Label> _soundCaptions = new();

    /// <summary>Every pack drop-down - Set all to:'s and each sound's - all one width (SizeFields).</summary>
    private readonly List<ComboBox> _packPickers = new();

    /// <summary>The most a sound's line holds beside its drop-down - its Play button, and "own file" when it has one - with the gaps between them.</summary>
    private int _soundExtrasWidth;

    /// <summary>
    /// The sounds card's width and the race moments list's, worked out in
    /// SizeFields and read whenever the two columns are placed.
    /// </summary>
    private int _soundsColumnWidth;
    private int _momentsColumnWidth;

    /// <summary>The edited soundscape - set when the form closes with Save.</summary>
    public Theme Result { get; private set; }

    /// <summary>The moments a soundscape's personality covers, in race order, with the names shown for them.</summary>
    private static readonly (AnnouncementEventType Type, string Name)[] EditableEvents =
    {
        (AnnouncementEventType.RaceStaging, "Race staging"),
        (AnnouncementEventType.CountdownStarted, "Countdown"),
        (AnnouncementEventType.RaceStarted, "Race start"),
        (AnnouncementEventType.JumpStart, "Jump start"),
        (AnnouncementEventType.PersonalBestLap, "Personal best"),
        (AnnouncementEventType.FastLap, "Fastest lap"),
        (AnnouncementEventType.TrackRecord, "Track record"),
        (AnnouncementEventType.LeaderChange, "Leader change"),
        (AnnouncementEventType.Streak, "Streak"),
        (AnnouncementEventType.YellowFlag, "Yellow flag"),
        (AnnouncementEventType.RedFlag, "Red flag"),
        (AnnouncementEventType.GreenFlagResumed, "Green (restart)"),
        (AnnouncementEventType.RaceTimeOver, "Last lap"),
        (AnnouncementEventType.RaceCompleted, "Checkered flag"),
    };

    private static readonly Dictionary<SoundEffectKind, string> SoundNames = new()
    {
        [SoundEffectKind.GoodLap] = "Good lap",
        [SoundEffectKind.InvalidLap] = "Invalid lap",
        [SoundEffectKind.PersonalBest] = "Personal best",
        [SoundEffectKind.FastLap] = "Fast lap",
        [SoundEffectKind.TrackRecord] = "Track record",
        [SoundEffectKind.Streak] = "Streak",
        [SoundEffectKind.LeaderChange] = "Leader change",
        [SoundEffectKind.RaceStaging] = "Race staging",
        [SoundEffectKind.CountdownStarted] = "Countdown",
        [SoundEffectKind.CountdownTicks] = "Countdown ticks",
        [SoundEffectKind.JumpStart] = "Jump start",
        [SoundEffectKind.RaceStarted] = "Race start",
        [SoundEffectKind.YellowFlag] = "Yellow flag",
        [SoundEffectKind.RedFlag] = "Red flag",
        [SoundEffectKind.GreenFlagResumed] = "Green (restart)",
        [SoundEffectKind.Warning] = "Warning",
        [SoundEffectKind.RaceTimeOver] = "Last lap",
        [SoundEffectKind.RaceCompleted] = "Checkered flag",
    };

    public SoundscapeEditorForm(
        Theme working,
        bool isNew,
        ThemeStore store,
        VoiceProfile currentVoice,
        Func<Theme, CancellationToken, Task> preview,
        Action stopVoice,
        IReadOnlyList<string>? musicLibrary = null)
    {
        _musicLibrary = musicLibrary ?? Array.Empty<string>();
        _working = working;
        _store = store;
        _preview = preview;
        _stopVoice = stopVoice;
        Result = working;

        Text = isNew ? "New soundscape" : $"Edit soundscape - {working.Name}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var page = BuildLayout(currentVoice, canDelete: !isNew);

        AppTheme.ApplyRecursively(this);

        SizeFields();

        DialogFit.Keep(this, page, width: DialogWidth, height: DialogHeight);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        StopPreview();
        base.OnFormClosing(e);
    }

    /// <summary>
    /// The window's page - see the class notes. The two cards' columns are
    /// sized from the faces the theme pass leaves, so their widths are
    /// worked out after it (SizeFields) and read as the columns are placed.
    /// </summary>
    private FillPage BuildLayout(VoiceProfile currentVoice, bool canDelete)
    {
        // ---- name, description and what plays underneath
        _txtName.Width = UiLayout.AtTextSize(NameWidth);
        _txtName.Text = _working.Name;

        _txtDescription.Multiline = true;
        _txtDescription.Text = _working.Description;

        var fields = new FieldGrid();
        fields.AddRow("Name:", _txtName);
        fields.AddRow("Description:", _txtDescription, stretch: true);
        fields.AddRow("Background:", BuildBackgroundRow());

        // ---- sounds
        var tips = new ToolTip();
        AppTheme.StyleToolTip(tips);

        // Disposed with the form. A ToolTip keeps every control it was given,
        // so one left behind kept each closed editor alive with it.
        Disposed += (_, _) => tips.Dispose();

        var cmbSetAll = NewSoundCombo(null);
        cmbSetAll.SelectedIndex = -1;
        cmbSetAll.SelectionChangeCommitted += (_, _) =>
        {
            if (cmbSetAll.SelectedItem is SoundChoice choice)
            {
                foreach (var combo in _sounds.Values)
                {
                    SelectSound(combo, choice.Key);
                }
            }
        };

        _packPickers.Add(cmbSetAll);
        _soundCaptions.Add(_setAllGrid.AddRow("Set all to:", cmbSetAll));

        // The same wake-up pad the race uses on this PC's output, or a short
        // test sound can vanish on a wireless speaker.
        var leadIn = AnnouncementService.LeadingSilenceFor(currentVoice.WirelessSpeaker);

        // A line for each sound: its caption, its pack, a button that plays
        // it, and - when it plays a file of its own instead - a note saying
        // so. The first line is kept for the least the sounds can do with.
        ButtonBar? firstLine = null;

        foreach (var kind in Enum.GetValues<SoundEffectKind>())
        {
            var config = _working.SoundEffects.Effects.GetValueOrDefault(kind.ToString());

            var combo = NewSoundCombo(config?.BuiltInSound);

            var btnTest = new Button { Text = "▶" };
            tips.SetToolTip(btnTest, "Play this sound");
            var testKind = kind;
            btnTest.Click += async (_, _) =>
            {
                var current = _working.SoundEffects.Effects.GetValueOrDefault(testKind.ToString());
                var probe = new SoundEffectConfig { BuiltInSound = SoundKeyOf(combo), CustomFilePath = current?.CustomFilePath ?? string.Empty };
                await SoundPacks.PlayAsync(probe, testKind, 0.9f, 6, 3, leadIn);
            };

            var parts = new List<Control> { combo, btnTest };

            // As the tight row sizes the button, and the gap after it.
            var extras = UiLayout.TightButtonSizeFor(btnTest).Width + UiLayout.TightButtonGap;

            if (!string.IsNullOrWhiteSpace(config?.CustomFilePath))
            {
                var lblOwn = new Label
                {
                    Text = "own file",
                    AutoSize = true,
                    Font = AppTheme.SmallFont,
                    ForeColor = AppTheme.Accent,
                };
                tips.SetToolTip(lblOwn, $"Plays {config!.CustomFilePath} instead of the pack. Change or clear it on Sounds → Effects.");
                parts.Add(lblOwn);

                extras += lblOwn.GetPreferredSize(Size.Empty).Width + UiLayout.TightButtonGap;
            }

            _soundExtrasWidth = Math.Max(_soundExtrasWidth, extras);

            var line = SoundLine(parts.ToArray());
            firstLine ??= line;

            _soundCaptions.Add(_soundGrid.AddRow($"{SoundNames.GetValueOrDefault(kind, kind.ToString())}:", line));
            _packPickers.Add(combo);
            _sounds[kind] = combo;
        }

        // The sounds scroll on their own, under a Set all to: that stays
        // put - seventeen of them are taller than the card at the height the
        // window opens at. No margins of their own - the card's inset is
        // round them - and no room under the last line, which the theme
        // pass gives a panel that scrolls (see ScrollTheming.NoRoomBelow).
        var soundRows = new PageStack { Padding = Padding.Empty };
        ScrollTheming.NoRoomBelow(soundRows);
        soundRows.Add(_soundGrid, gapBelow: 0);

        var sounds = new Section("Sounds");
        sounds.Add(_setAllGrid);
        sounds.AddFill(
            soundRows,
            () => LeastSoundRows * ((firstLine?.GetPreferredSize(Size.Empty).Height ?? 0) + UiLayout.RowGap),
            gapBelow: 0);

        // ---- announcer
        var tuning = new FieldRow();

        foreach (var (caption, box, min, max, value) in new[]
                 {
                     ("Stability", _numStability, 0m, 1m, _working.Voice.Stability),
                     ("Style", _numStyle, 0m, 1m, _working.Voice.Style),
                     ("Speed", _numSpeed, 0.7m, 1.2m, _working.Voice.Speed),
                 })
        {
            box.DecimalPlaces = 2;
            box.Increment = 0.05m;
            box.Minimum = min;
            box.Maximum = max;
            box.Value = Math.Clamp((decimal)value, min, max);

            // Room for its largest value, "1.20", at every text size.
            box.Width = UiLayout.AtTextSize(UiLayout.NumericWidth);

            tuning.Add(new Label { Text = caption + ":" }, box);
        }

        tips.SetToolTip(_numStability, "Lower is livelier and less even; higher is calmer and steadier. ElevenLabs voices only.");
        tips.SetToolTip(_numStyle, "How much character the delivery gets. ElevenLabs voices only.");
        tips.SetToolTip(_numSpeed, "Speaking speed.");

        // Given the height the card has, not snapped to whole rows.
        _lstEvents.IntegralHeight = false;
        foreach (var (type, name) in EditableEvents)
        {
            _lstEvents.Items.Add(new EventItem(type, name));
        }

        _lstEvents.SelectedIndexChanged += (_, _) => ShowEvent();

        _chkEventEnabled.Text = "Announce this moment";
        _chkEventEnabled.AutoSize = true;

        // Across the card, over the moments and their lines, wrapping to the
        // card's width.
        var lblLines = new Label
        {
            Text = "Lines - one per line, picked at random. {driver}, {time}, {lap} and {streak} are filled in; " +
                   "tags like [excited] or [shouting] steer ElevenLabs delivery and are dropped for other voices.",
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        };

        _txtLines.Multiline = true;
        _txtLines.ScrollBars = ScrollBars.Vertical;
        _txtLines.WordWrap = false;
        _txtLines.AcceptsReturn = true;

        // The moments down the left, as wide as the longest; the tick for the
        // one picked and its lines beside them, the lines taking the height.
        var moments = new Beside(() => _momentsColumnWidth) { SideOnLeft = true };
        moments.AddSideFill(_lstEvents, _ => LeastMomentRows * _lstEvents.ItemHeight + 4, gapBelow: 0);
        moments.Add(_chkEventEnabled, stretch: false);
        moments.AddFill(_txtLines, _ => LeastLines * Pieces.LineHeight(_txtLines.Font) + UiLayout.HintGap * 2);

        // Tab reaches the moments before their lines, as it did: the column
        // on the left is added after the parts beside it, so it is put first.
        FirstInTabOrder(moments, _lstEvents);

        var announcer = new Section("Announcer");
        announcer.Add(tuning);
        announcer.Add(lblLines);

        // Nothing in the moments' band wraps - the list, the tick on one
        // line, the lines box - so its least is the same at any width it
        // can have; the card asks without one (Section.AddFill).
        announcer.AddFill(moments, () => moments.LeastHeight(UiLayout.AtTextSize(DialogWidth)), gapBelow: 0);

        // The sounds down the left at the width their lines need, the
        // announcer taking the rest. Both take the height the window has.
        var band = new Beside(() => _soundsColumnWidth) { SideOnLeft = true };
        band.AddSideFill(sounds, sounds.LeastHeight, gapBelow: 0);
        band.AddFill(announcer, announcer.LeastHeight);

        // The sounds before the announcer, as they were - see above.
        FirstInTabOrder(band, sounds);

        Label? lblHeatCaller = null;

        if (currentVoice.EffectiveEngine == VoiceEngine.HeatCaller)
        {
            lblHeatCaller = new Label
            {
                Text = "This PC speaks with the HeatCaller voice, which always uses the standard lines. These lines are saved and apply once you use your own ElevenLabs key or the Kokoro voice.",
                AutoSize = true,
                Font = AppTheme.SmallFont,
                ForeColor = AppTheme.Accent,
            };
        }

        // ---- the foot
        _btnPreview.Text = "Preview";
        _btnPreview.Click += async (_, _) => await TogglePreviewAsync();

        _lblStatus.ForeColor = AppTheme.TextMuted;

        // Centred up and down on Preview, as FieldGrid.Inline centres a label.
        _lblStatus.Anchor = AnchorStyles.Left;

        var btnSave = new Button { Text = "Save" };
        btnSave.Click += (_, _) => Save();

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // Save and Cancel on the right, Cancel furthest right - and Tab still
        // reaches Save first: the bar takes them right to left, in the order
        // they were added.
        var closing = new ButtonBar(alignRight: true).With(btnCancel, btnSave);
        btnSave.TabIndex = 0;
        btnCancel.TabIndex = 1;

        if (canDelete)
        {
            // Abort tells ThemesControl to delete it - it owns the undo and
            // the confirmation.
            var btnDelete = new Button { Text = "Delete Soundscape", DialogResult = DialogResult.Abort };
            closing.With(btnDelete);
            btnDelete.TabIndex = 2;

            // Three gaps from Save rather than one, as it always was, so it is
            // not pressed for Save: the bar keeps one on Save's side of it,
            // and the other two go on this side.
            btnDelete.Margin = new Padding(
                btnDelete.Margin.Left,
                btnDelete.Margin.Top,
                btnDelete.Margin.Right + UiLayout.ButtonGap * 2,
                btnDelete.Margin.Bottom);
        }

        // Preview and what it is doing on the left.
        var footer = new SplitBar(new ButtonBar().With(_btnPreview, _lblStatus), closing);

        var page = new FillPage();
        page.Add(fields);
        page.AddFill(band, band.LeastHeight, gapBelow: lblHeatCaller is null ? UiLayout.SectionGap : UiLayout.RowGap);

        if (lblHeatCaller is not null)
        {
            page.Add(lblHeatCaller);
        }

        page.Add(footer, gapBelow: 0);

        Controls.Add(page);

        CancelButton = btnCancel;

        _lstEvents.SelectedIndex = 0;

        return page;
    }

    /// <summary>
    /// A sound's line: its pack, the button that plays it, and the note
    /// that it has a file of its own - each only as wide as it needs (a
    /// tight row, so the button is only as wide as its arrow), everything
    /// but the button centred up and down on it, as FieldGrid.Inline does.
    /// </summary>
    private static ButtonBar SoundLine(params Control[] parts)
    {
        foreach (var part in parts)
        {
            if (part is not Button)
            {
                part.Anchor = AnchorStyles.Left;
            }
        }

        return new ButtonBar(tight: true).With(parts);
    }

    /// <summary>
    /// Puts a Beside's column first in the tab order. The column on the left
    /// (SideOnLeft) is added after the parts beside it, so Tab would reach it
    /// last; given the first tab index and the first place among its
    /// siblings - which settles a tie of tab indexes - it comes first, as it
    /// is first on screen.
    /// </summary>
    private static void FirstInTabOrder(Beside band, Control column)
    {
        column.TabIndex = 0;
        band.Controls.SetChildIndex(column, 0);
    }

    /// <summary>
    /// What is sized from the faces the theme pass leaves: the drop-downs as
    /// wide as they were at the normal text size, at the size in use, or as
    /// their longest choice needs (Pieces.PickerWidth) - every pack drop-down
    /// one width; the description its lines of its face (Pieces.LineHeight);
    /// the sounds' captions in one column with Set all to:'s. Then the two
    /// columns: the sounds card as wide as its widest line - caption, pack,
    /// Play and "own file" - with the card's inset either side and room for
    /// the scrollbar the sounds take, since they scroll at the height the
    /// window opens at; and the race moments as wide as the longest of them,
    /// with room for their list's scrollbar.
    /// </summary>
    private void SizeFields()
    {
        (Font Font, int Count, int Width)? measured = null;
        _cmbAmbience.Width = Math.Max(UiLayout.AtTextSize(AmbienceWidth), Pieces.PickerWidth(_cmbAmbience, ref measured));

        measured = null;
        _cmbMusic.Width = Math.Max(UiLayout.AtTextSize(MusicWidth), Pieces.PickerWidth(_cmbMusic, ref measured));

        // Every pack drop-down holds the same packs, in the same face.
        measured = null;
        var packWidth = Math.Max(UiLayout.AtTextSize(PackPickerWidth), Pieces.PickerWidth(_packPickers[0], ref measured));

        foreach (var picker in _packPickers)
        {
            if (picker.Width != packWidth)
            {
                picker.Width = packWidth;
            }
        }

        _txtDescription.Height = DescriptionLines * Pieces.LineHeight(_txtDescription.Font) + UiLayout.HintGap * 2;

        FieldGrid.LineUp(new[] { _setAllGrid, _soundGrid });

        // The caption column as the grids work it out: the widest caption,
        // measured as a label measures itself, and the gap after it.
        var captionColumn = _soundCaptions.Max(caption => TextRenderer.MeasureText(caption.Text, caption.Font).Width) + UiLayout.LabelGap;

        _soundsColumnWidth = Section.Inset.Horizontal
            + captionColumn
            + packWidth + UiLayout.TightButtonGap
            + _soundExtrasWidth
            + SystemInformation.VerticalScrollBarWidth;

        var widestMoment = EditableEvents.Max(moment => TextRenderer.MeasureText(moment.Name, _lstEvents.Font).Width);

        _momentsColumnWidth = Math.Max(
            UiLayout.AtTextSize(MomentsWidth),
            widestMoment + SystemInformation.VerticalScrollBarWidth + UiLayout.LabelGap);
    }

    // ------------------------------------------------------------ background

    private sealed record Choice<T>(string Label, T Value)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Ambience bed and music playlist - "leave as it is" for either, so a
    /// soundscape can change the sounds and voice without touching what the
    /// club already has playing underneath. One line, the music after the
    /// ambience, going onto a second when the window is too narrow for both.
    /// </summary>
    private FlowLayoutPanel BuildBackgroundRow()
    {
        var bg = _working.Background;

        _cmbAmbience.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbAmbience.Items.Add(new Choice<string?>("Leave Ambience as it is", null));
        _cmbAmbience.Items.Add(new Choice<string?>("No soundscape crowd", string.Empty));
        foreach (var pack in SoundPacks.All)
        {
            _cmbAmbience.Items.Add(new Choice<string?>($"{pack.Name} crowd", pack.Key));
        }

        var wantedBed = bg?.AmbienceBed is null ? null : (SoundPacks.Find(bg.AmbienceBed)?.Key ?? string.Empty);
        _cmbAmbience.SelectedIndex = Math.Max(0, _cmbAmbience.Items.Cast<Choice<string?>>().ToList().FindIndex(c => c.Value == wantedBed));

        var lblMusic = new Label { Text = "Music:" };
        _cmbMusic.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbMusic.Items.Add(new Choice<List<string>>("No music of its own", new List<string>()));
        foreach (var pack in SoundPacks.All)
        {
            if (SoundPacks.PackMusic.TryGetValue(pack.Key, out var titles))
            {
                var have = titles.Count(t => _musicLibrary.Contains(t, StringComparer.OrdinalIgnoreCase));
                var label = titles.Count == 1
                    ? $"{pack.Name} music - \"{titles[0]}\""
                    : $"{pack.Name} music - {titles.Count} tracks";
                if (have == 0)
                {
                    label += " (not in your library yet)";
                }

                _cmbMusic.Items.Add(new Choice<List<string>>(label, titles.ToList()));
            }
        }

        // A playlist that isn't one of the packs' - kept exactly as it was.
        var current = bg?.MusicPlaylist ?? new List<string>();
        var match = _cmbMusic.Items.Cast<Choice<List<string>>>().ToList()
            .FindIndex(c => c.Value.Count == current.Count && c.Value.All(t => current.Contains(t, StringComparer.OrdinalIgnoreCase)));
        if (match < 0)
        {
            _cmbMusic.Items.Add(new Choice<List<string>>($"Its own playlist - {Plural.Of(current.Count, "track")}", current.ToList()));
            match = _cmbMusic.Items.Count - 1;
        }

        _cmbMusic.SelectedIndex = match;

        // Disposed with the form - see the other ToolTip in BuildLayout.
        var musicTip = new ToolTip();
        AppTheme.StyleToolTip(musicTip);
        Disposed += (_, _) => musicTip.Dispose();
        musicTip.SetToolTip(_cmbMusic,
            "What Sounds \u2192 Music plays when it is set to Theme music (with none, your own music plays). " +
            "Your playlist ticks, and whether music is on at all, stay as you have them.");

        return FieldGrid.Inline(_cmbAmbience, lblMusic, _cmbMusic);
    }

    // ------------------------------------------------------------ sounds

    private sealed record SoundChoice(string Key, string Name)
    {
        public override string ToString() => Name;
    }

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

    // ------------------------------------------------------------ lines

    private sealed record EventItem(AnnouncementEventType Type, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>The event's settings in the working copy, created from the defaults if the soundscape has never had any.</summary>
    private AnnouncementEventConfig ConfigFor(AnnouncementEventType type)
    {
        var key = type.ToString();
        if (!_working.Voice.Events.TryGetValue(key, out var config) || config is null)
        {
            config = VoiceProfile.CreateDefault().Events.GetValueOrDefault(key) ?? new AnnouncementEventConfig();
            _working.Voice.Events[key] = config;
        }

        return config;
    }

    /// <summary>Writes the lines box back into the event it was showing.</summary>
    private void CommitShownEvent()
    {
        if (_shownEvent is not { } type)
        {
            return;
        }

        var config = ConfigFor(type);
        config.Enabled = _chkEventEnabled.Checked;
        config.Templates = _txtLines.Lines
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }

    private void ShowEvent()
    {
        CommitShownEvent();

        if (_lstEvents.SelectedItem is not EventItem item)
        {
            _shownEvent = null;
            return;
        }

        var config = ConfigFor(item.Type);
        _chkEventEnabled.Checked = config.Enabled;
        _txtLines.Lines = (config.Templates ?? new List<string>()).ToArray();
        _shownEvent = item.Type;
    }

    // ------------------------------------------------------------ result

    /// <summary>Everything on the form, written into the working copy.</summary>
    private void Collect()
    {
        CommitShownEvent();

        _working.Name = _txtName.Text.Trim();
        _working.Description = _txtDescription.Text.Trim();
        _working.Voice.Stability = (double)_numStability.Value;
        _working.Voice.Style = (double)_numStyle.Value;
        _working.Voice.Speed = (double)_numSpeed.Value;

        foreach (var (kind, combo) in _sounds)
        {
            var key = kind.ToString();
            if (!_working.SoundEffects.Effects.TryGetValue(key, out var config) || config is null)
            {
                config = SoundEffectsProfile.CreateDefault().Effects.GetValueOrDefault(key) ?? new SoundEffectConfig();
                _working.SoundEffects.Effects[key] = config;
            }

            config.BuiltInSound = SoundKeyOf(combo);
        }

        var bed = (_cmbAmbience.SelectedItem as Choice<string?>)?.Value;
        var playlist = (_cmbMusic.SelectedItem as Choice<List<string>>)?.Value ?? new List<string>();
        _working.Background = bed is null && playlist.Count == 0
            ? null
            : new SoundscapeBackground { AmbienceBed = bed, MusicPlaylist = playlist.ToList() };

        // The pack most of the sounds use, for the card's colour and label.
        _working.SoundPack = _working.SoundEffects.Effects.Values
            .GroupBy(c => c.BuiltInSound ?? string.Empty)
            .OrderByDescending(g => g.Count())
            .First().Key;
    }

    private void Save()
    {
        Collect();

        if (string.IsNullOrWhiteSpace(_working.Name))
        {
            AppDialog.Show(this, "Give the soundscape a name.", "Name needed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _txtName.Focus();
            return;
        }

        var clash = _store.All.FirstOrDefault(t => t.Id != _working.Id && string.Equals(t.Name, _working.Name, StringComparison.OrdinalIgnoreCase));
        if (clash is not null)
        {
            AppDialog.Show(this, $"There's already a soundscape called \"{clash.Name}\" - pick another name.", "Name taken", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _txtName.Focus();
            return;
        }

        Result = _working;
        DialogResult = DialogResult.OK;
        Close();
    }

    // ------------------------------------------------------------ preview

    private async Task TogglePreviewAsync()
    {
        if (_previewCts is not null)
        {
            StopPreview();
            return;
        }

        Collect();

        var cts = new CancellationTokenSource();
        _previewCts = cts;
        _btnPreview.Text = "Stop";
        _lblStatus.Text = "Playing a short pretend race...";

        try
        {
            // A snapshot, so edits made while it plays don't change it midway.
            await _preview(ThemesControl.Clone(_working), cts.Token);
            if (!cts.IsCancellationRequested)
            {
                _lblStatus.Text = "Preview finished.";
            }
        }
        catch
        {
            _lblStatus.Text = "Preview couldn't play.";
        }
        finally
        {
            if (_previewCts == cts)
            {
                _previewCts = null;
                if (!_btnPreview.IsDisposed)
                {
                    _btnPreview.Text = "Preview";
                }
            }

            cts.Dispose();
        }
    }

    private void StopPreview()
    {
        if (_previewCts is null)
        {
            return;
        }

        _previewCts.Cancel();
        _previewCts = null;
        _stopVoice();

        if (!_btnPreview.IsDisposed)
        {
            _btnPreview.Text = "Preview";
        }

        _lblStatus.Text = "Preview stopped.";
    }
}
