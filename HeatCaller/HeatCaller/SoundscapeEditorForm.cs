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
/// </summary>
public sealed class SoundscapeEditorForm : Form
{
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
    private readonly CheckBox _chkEventEnabled = new();
    private readonly TextBox _txtLines = new();
    private AnnouncementEventType? _shownEvent;

    private readonly Button _btnPreview = new();
    private readonly Label _lblStatus = new();
    private CancellationTokenSource? _previewCts;

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
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1000, 790);
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        BuildLayout(currentVoice, canDelete: !isNew);

        AppTheme.ApplyRecursively(this);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        StopPreview();
        base.OnFormClosing(e);
    }

    private void BuildLayout(VoiceProfile currentVoice, bool canDelete)
    {
        // ---- name and description
        var lblName = new Label { Text = "Name:", AutoSize = true, Location = new Point(16, 20) };
        _txtName.Location = new Point(120, 16);
        _txtName.Width = 360;
        _txtName.Text = _working.Name;

        var lblDescription = new Label { Text = "Description:", AutoSize = true, Location = new Point(16, 56) };
        _txtDescription.Location = new Point(120, 52);
        _txtDescription.Size = new Size(860, 46);
        _txtDescription.Multiline = true;
        _txtDescription.Text = _working.Description;

        Controls.AddRange(new Control[] { lblName, _txtName, lblDescription, _txtDescription });

        BuildBackgroundRow();

        // ---- sounds
        var grpSounds = new GroupBox { Text = "Sounds", Location = new Point(16, 156), Size = new Size(420, 530) };

        var y = UiLayout.InsideGroup(grpSounds) + 4;

        var lblSetAll = new Label { Text = "Set all to:", AutoSize = true, Location = new Point(12, y + 5) };
        var cmbSetAll = NewSoundCombo(null);
        cmbSetAll.SelectedIndex = -1;
        cmbSetAll.Location = new Point(150, y);
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
        grpSounds.Controls.Add(lblSetAll);
        grpSounds.Controls.Add(cmbSetAll);

        y += 40;

        var tips = new ToolTip();
        AppTheme.StyleToolTip(tips);

        // Disposed with the form. A ToolTip keeps every control it was given,
        // so one left behind kept each closed editor alive with it.
        Disposed += (_, _) => tips.Dispose();

        // The same wake-up pad the race uses on this PC's output, or a short
        // test sound can vanish on a wireless speaker.
        var leadIn = AnnouncementService.LeadingSilenceFor(currentVoice.WirelessSpeaker);

        // The rows scroll, under a Set All that stays put. Seventeen of them
        // at 38 apart is taller than the group, which was sized for eleven -
        // and making the group taller would push the whole editor off a
        // laptop screen.
        var rows = new Panel
        {
            Location = new Point(4, y),
            Size = new Size(grpSounds.Width - 8, grpSounds.Height - y - 8),
            AutoScroll = true,
        };
        grpSounds.Controls.Add(rows);

        // Positions inside the scrolling panel, which starts 4px in - so the
        // columns sit where they always have.
        const int inset = 4;
        y = 4;

        foreach (var kind in Enum.GetValues<SoundEffectKind>())
        {
            var config = _working.SoundEffects.Effects.GetValueOrDefault(kind.ToString());

            var lbl = new Label { Text = SoundNames.GetValueOrDefault(kind, kind.ToString()), AutoSize = true, Location = new Point(12 - inset, y + 5) };
            var combo = NewSoundCombo(config?.BuiltInSound);
            combo.Location = new Point(150 - inset, y);

            var btnTest = new Button { Text = "▶", Location = new Point(278 - inset, y - 1), Size = new Size(36, 28) };
            tips.SetToolTip(btnTest, "Play this sound");
            var testKind = kind;
            btnTest.Click += async (_, _) =>
            {
                var current = _working.SoundEffects.Effects.GetValueOrDefault(testKind.ToString());
                var probe = new SoundEffectConfig { BuiltInSound = SoundKeyOf(combo), CustomFilePath = current?.CustomFilePath ?? string.Empty };
                await SoundPacks.PlayAsync(probe, testKind, 0.9f, 6, 3, leadIn);
            };

            rows.Controls.Add(lbl);
            rows.Controls.Add(combo);
            rows.Controls.Add(btnTest);

            if (!string.IsNullOrWhiteSpace(config?.CustomFilePath))
            {
                var lblOwn = new Label
                {
                    Text = "own file",
                    AutoSize = true,
                    Font = AppTheme.SmallFont,
                    ForeColor = AppTheme.Accent,
                    Location = new Point(322 - inset, y + 6),
                };
                tips.SetToolTip(lblOwn, $"Plays {config!.CustomFilePath} instead of the pack. Change or clear it on Sounds → Effects.");
                rows.Controls.Add(lblOwn);
            }

            _sounds[kind] = combo;
            y += 38;
        }

        Controls.Add(grpSounds);

        // ---- announcer
        var grpVoice = new GroupBox { Text = "Announcer", Location = new Point(452, 156), Size = new Size(532, 530) };
        var vy = UiLayout.InsideGroup(grpVoice) + 4;

        var x = 12;
        foreach (var (caption, box, min, max, value) in new[]
                 {
                     ("Stability", _numStability, 0m, 1m, _working.Voice.Stability),
                     ("Style", _numStyle, 0m, 1m, _working.Voice.Style),
                     ("Speed", _numSpeed, 0.7m, 1.2m, _working.Voice.Speed),
                 })
        {
            var lbl = new Label { Text = caption + ":", AutoSize = true, Location = new Point(x, vy + 5) };
            box.DecimalPlaces = 2;
            box.Increment = 0.05m;
            box.Minimum = min;
            box.Maximum = max;
            box.Value = Math.Clamp((decimal)value, min, max);
            box.Width = 74;
            box.Location = new Point(UiLayout.After(lbl), vy);
            grpVoice.Controls.Add(lbl);
            grpVoice.Controls.Add(box);
            x = box.Right + 18;
        }

        tips.SetToolTip(_numStability, "Lower is livelier and less even; higher is calmer and steadier. ElevenLabs voices only.");
        tips.SetToolTip(_numStyle, "How much character the delivery gets. ElevenLabs voices only.");
        tips.SetToolTip(_numSpeed, "Speaking speed.");

        vy += 42;

        _lstEvents.Location = new Point(12, vy);
        _lstEvents.Size = new Size(170, 420);
        _lstEvents.IntegralHeight = false;
        foreach (var (type, name) in EditableEvents)
        {
            _lstEvents.Items.Add(new EventItem(type, name));
        }

        _lstEvents.SelectedIndexChanged += (_, _) => ShowEvent();

        _chkEventEnabled.Text = "Announce this moment";
        _chkEventEnabled.AutoSize = true;
        _chkEventEnabled.Location = new Point(196, vy);

        var lblLines = new Label
        {
            Text = "Lines - one per line, picked at random. {driver}, {time}, {lap} and {streak} are filled in; " +
                   "tags like [excited] or [shouting] steer ElevenLabs delivery and are dropped for other voices.",
            AutoSize = true,
            MaximumSize = new Size(320, 0),
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
            Location = new Point(196, vy + 28),
        };

        var linesTop = UiLayout.Below(lblLines, 6);
        _txtLines.Location = new Point(196, linesTop);
        _txtLines.Size = new Size(322, vy + 420 - linesTop);
        _txtLines.Multiline = true;
        _txtLines.ScrollBars = ScrollBars.Vertical;
        _txtLines.WordWrap = false;
        _txtLines.AcceptsReturn = true;

        grpVoice.Controls.AddRange(new Control[] { _lstEvents, _chkEventEnabled, lblLines, _txtLines });
        Controls.Add(grpVoice);

        if (currentVoice.EffectiveEngine == VoiceEngine.HeatCaller)
        {
            var lblHeatCaller = new Label
            {
                Text = "This PC speaks with the HeatCaller voice, which always uses the standard lines - these lines are saved, and apply once you use your own ElevenLabs key or the Windows voice.",
                AutoSize = true,
                MaximumSize = new Size(960, 0),
                Font = AppTheme.SmallFont,
                ForeColor = AppTheme.Accent,
                Location = new Point(16, 694),
            };
            Controls.Add(lblHeatCaller);
        }

        // ---- bottom bar
        var barY = ClientSize.Height - UiLayout.ButtonHeight - 14;

        _btnPreview.Text = "Preview";
        _btnPreview.Click += async (_, _) => await TogglePreviewAsync();

        var btnSave = new Button { Text = "Save" };
        btnSave.Click += (_, _) => Save();

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        AppTheme.LayoutButtonRow(16, barY, UiLayout.ButtonGap, _btnPreview);

        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = AppTheme.TextMuted;
        _lblStatus.Location = new Point(_btnPreview.Right + 12, barY + 9);
        _lblStatus.MaximumSize = new Size(520, 0);

        // Save and Cancel on the right.
        AppTheme.LayoutButtonRow(0, barY, UiLayout.ButtonGap, btnSave, btnCancel);
        btnCancel.Left = ClientSize.Width - 16 - btnCancel.Width;
        btnSave.Left = btnCancel.Left - UiLayout.ButtonGap - btnSave.Width;

        Controls.AddRange(new Control[] { _btnPreview, _lblStatus, btnSave, btnCancel });

        if (canDelete)
        {
            // Abort tells ThemesControl to delete it - it owns the undo and
            // the confirmation.
            var btnDelete = new Button { Text = "Delete Soundscape", DialogResult = DialogResult.Abort };
            AppTheme.LayoutButtonRow(0, barY, UiLayout.ButtonGap, btnDelete);
            btnDelete.Left = btnSave.Left - UiLayout.ButtonGap * 3 - btnDelete.Width;
            Controls.Add(btnDelete);
            _lblStatus.MaximumSize = new Size(Math.Max(120, btnDelete.Left - _lblStatus.Left - 12), 0);
        }
        CancelButton = btnCancel;

        _lstEvents.SelectedIndex = 0;
    }

    // ------------------------------------------------------------ background

    private sealed record Choice<T>(string Label, T Value)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Ambience bed and music playlist - "leave as it is" for either, so a
    /// soundscape can change the sounds and voice without touching what the
    /// club already has playing underneath.
    /// </summary>
    private void BuildBackgroundRow()
    {
        var bg = _working.Background;

        var lblAmbience = new Label { Text = "Background:", AutoSize = true, Location = new Point(16, 118) };
        _cmbAmbience.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbAmbience.Location = new Point(120, 114);
        _cmbAmbience.Width = 250;
        _cmbAmbience.Items.Add(new Choice<string?>("Leave Ambience as it is", null));
        _cmbAmbience.Items.Add(new Choice<string?>("No soundscape crowd", string.Empty));
        foreach (var pack in SoundPacks.All)
        {
            _cmbAmbience.Items.Add(new Choice<string?>($"{pack.Name} crowd", pack.Key));
        }

        var wantedBed = bg?.AmbienceBed is null ? null : (SoundPacks.Find(bg.AmbienceBed)?.Key ?? string.Empty);
        _cmbAmbience.SelectedIndex = Math.Max(0, _cmbAmbience.Items.Cast<Choice<string?>>().ToList().FindIndex(c => c.Value == wantedBed));

        var lblMusic = new Label { Text = "Music:", AutoSize = true, Location = new Point(392, 118) };
        _cmbMusic.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbMusic.Location = new Point(450, 114);
        _cmbMusic.Width = 530;
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
            "What Sounds \u2192 Music plays when it is set to Theme music. Your own playlist ticks are never changed. " +
            "With no music of its own, Theme music plays your music instead. " +
            "Whether music is on at all stays as you have it on Sounds \u2192 Music.");

        Controls.AddRange(new Control[] { lblAmbience, _cmbAmbience, lblMusic, _cmbMusic });
    }

    // ------------------------------------------------------------ sounds

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
