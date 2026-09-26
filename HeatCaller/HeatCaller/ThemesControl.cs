namespace HeatCaller;

/// <summary>
/// Sounds → Soundscapes (on Themes until 0.56): one card per soundscape - its sounds, its
/// announcer's words and delivery - each with Preview, Apply and Customize.
///
/// Called "announcer styles" on screen from 0.48 to 0.53 and "soundscapes"
/// since, when the built-in sound packs arrived. In code and in the stored
/// data they are still Themes (ThemeStore, themes.json, a venue's
/// PreferredThemeName), so nothing a club has saved changes meaning.
///
/// Editing happens on the soundscape itself (SoundscapeEditorForm) rather
/// than by setting the Voice and Sound Effects pages up and saving "current
/// settings" into a style - that route is still here, as "Save current
/// settings as new...", for anyone who built a setup the old way.
/// </summary>
public sealed class ThemesControl : UserControl
{
    private readonly ThemeStore _themeStore;
    private readonly VoiceSettingsStore _voiceStore;
    private readonly SoundEffectsStore _soundStore;
    private readonly UndoService _undo;
    private readonly AmbienceStore? _ambienceStore;
    private readonly MusicStore? _musicStore;

    private readonly FlowLayoutPanel _cards = new();
    private readonly Label _lblStatus = new();
    private readonly CheckBox _chkPreviewVoice = new();

    private CancellationTokenSource? _previewCts;
    private Button? _previewButton;

    /// <summary>
    /// True while RecordVoiceLinesAsync is rendering. The menu can be opened
    /// again while it runs, and a second run would pay ElevenLabs for every
    /// line a second time.
    /// </summary>
    private bool _recordingLines;

    private const int MinCardWidth = 440;
    private const int CardHeight = 214;

    /// <summary>
    /// How wide every card is, measured from the widest button row at the
    /// text size in use. It was a fixed 440, and at a larger text size the
    /// fourth button ran off the card's edge.
    /// </summary>
    private int CardWidth = MinCardWidth;

    /// <summary>Speaks the preview lines. Set by MainForm; previews play sounds only without it.</summary>
    public AnnouncementService? Announcer { get; set; }

    /// <summary>Raised after a soundscape is applied, so the host (MainForm) can rebuild the Voice/Sound tabs' UI to reflect the newly-active settings - those tabs only read from their store once, at construction.</summary>
    public event Action? ThemeApplied;

    /// <summary>
    /// Raised after a soundscape is edited or deleted here (or either is
    /// undone), so the host can bring what reads the applied soundscape -
    /// the Music page's Theme music - back in step. Applying raises
    /// ThemeApplied instead.
    /// </summary>
    public event Action? SoundscapesChanged;

    public ThemesControl(
        ThemeStore themeStore,
        VoiceSettingsStore voiceStore,
        SoundEffectsStore soundStore,
        UndoService undo,
        AmbienceStore? ambienceStore = null,
        MusicStore? musicStore = null)
    {
        _themeStore = themeStore;
        _voiceStore = voiceStore;
        _soundStore = soundStore;
        _undo = undo;
        _ambienceStore = ambienceStore;
        _musicStore = musicStore;

        Dock = DockStyle.Fill;
        Font = AppTheme.BodyFont;

        BuildLayout();
        RefreshCards();

        // Undo works on the stores, never on this page - see OnThemeStoreRestored.
        _themeStore.Restored += OnThemeStoreRestored;
    }

    /// <summary>
    /// Brings the cards (and the host) up to date after an undo changed the
    /// soundscapes, their recorded lines, or - for an Apply undone - the live
    /// settings too, which the host answers exactly as it does an Apply:
    /// rebuilding Voice Settings and Sound Effects, and handing ambience and
    /// Theme music their restored settings.
    /// </summary>
    private void OnThemeStoreRestored(bool liveSettingsRestored)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        RefreshCards();

        if (liveSettingsRestored)
        {
            ThemeApplied?.Invoke();
        }
        else
        {
            SoundscapesChanged?.Invoke();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _themeStore.Restored -= OnThemeStoreRestored;
            _previewCts?.Cancel();
            _previewCts?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var header = new Panel { Dock = DockStyle.Top };

        var lblIntro = new Label
        {
            Text = "A soundscape is the whole sound of race day: a sound for every moment, and an announcer's words and delivery to match. " +
                   "Preview one to hear a short pretend race, Apply it to use it, or Customize to mix packs and rewrite lines. " +
                   "Applying never touches your API key, which voice speaks, or this PC's volumes. Colours and the club logo are on the Themes page.",
            Location = new Point(12, 12),
            AutoSize = true,
            MaximumSize = new Size(900, 0),
            ForeColor = AppTheme.TextMuted,
        };

        var rowY = UiLayout.Below(lblIntro, UiLayout.RowGap);

        var btnSaveNew = new Button { Text = "Save Current Settings as New..." };
        btnSaveNew.Click += BtnSaveNew_Click;
        var right = AppTheme.LayoutButtonRow(12, rowY, UiLayout.ButtonGap, btnSaveNew);

        _chkPreviewVoice.Text = "Previews include the announcer's voice";
        _chkPreviewVoice.AutoSize = true;
        _chkPreviewVoice.Checked = true;
        _chkPreviewVoice.Location = new Point(right + UiLayout.ButtonGap * 2, rowY + 8);
        // Disposed with the page - a ToolTip left behind keeps the controls
        // it was given alive after the page itself has gone.
        var previewTip = new ToolTip();
        AppTheme.StyleToolTip(previewTip);
        Disposed += (_, _) => previewTip.Dispose();
        previewTip.SetToolTip(_chkPreviewVoice,
            "Lines are spoken by the voice this PC already uses. On your own ElevenLabs key each preview line costs characters, " +
            "so untick this to hear the sounds only.");

        _lblStatus.Location = new Point(12, rowY + UiLayout.ButtonHeight + 8);
        _lblStatus.AutoSize = true;
        _lblStatus.MaximumSize = new Size(900, 0);
        _lblStatus.ForeColor = AppTheme.TextMuted;
        _lblStatus.Text = " ";

        header.Controls.AddRange(new Control[] { lblIntro, btnSaveNew, _chkPreviewVoice, _lblStatus });
        header.Height = _lblStatus.Top + UiLayout.HeightOf(_lblStatus) + 8;

        _cards.Dock = DockStyle.Fill;
        _cards.AutoScroll = true;
        _cards.WrapContents = true;
        _cards.FlowDirection = FlowDirection.LeftToRight;
        _cards.Padding = new Padding(6, 4, 6, 12);

        // Fill first - WinForms docks the last-added control first.
        Controls.Add(_cards);
        Controls.Add(header);
    }

    private void RefreshCards()
    {
        StopPreview();

        _cards.SuspendLayout();

        // Over a copy. Disposing a card takes it out of _cards.Controls, so
        // walking the live collection skipped every other card - those were
        // then only Cleared below, never disposed, and every refresh (each
        // apply, edit, undo and recording) leaked half the cards' window
        // handles.
        foreach (var c in _cards.Controls.Cast<Control>().ToList())
        {
            c.Dispose();
        }

        _cards.Controls.Clear();

        CardWidth = MeasureCardWidth();

        var activeId = _themeStore.ActiveThemeId;
        foreach (var theme in _themeStore.All)
        {
            _cards.Controls.Add(BuildCard(theme, theme.Id == activeId));
        }

        // Room under the last card. A flow panel leaves its own bottom
        // padding out of what it scrolls, so the last row stopped against
        // the bottom edge of the window; the last card's own margin does
        // count.
        if (_cards.Controls.Count > 0)
        {
            var last = _cards.Controls[_cards.Controls.Count - 1];
            last.Margin = new Padding(last.Margin.Left, last.Margin.Top, last.Margin.Right, last.Margin.Bottom + LayoutDoctor.ScrollBottomRoom);
        }

        _cards.ResumeLayout();
        AppTheme.ApplyRecursively(_cards);
    }

    /// <summary>The widest row a card can have (a built-in's), plus room either side.</summary>
    private static int MeasureCardWidth()
    {
        var probe = new[]
        {
            new Button { Text = "Preview" }, new Button { Text = "Apply" },
            new Button { Text = "Customize..." }, new Button { Text = "Voice Lines..." },
        };

        try
        {
            // The right edge of the row, plus slack for the theming pass,
            // which can widen a button a little after it is measured here.
            var right = AppTheme.LayoutButtonRow(20, 0, 6, probe);
            return Math.Max(MinCardWidth, right + 20 + probe.Length * 6);
        }
        finally
        {
            foreach (var button in probe)
            {
                button.Dispose();
            }
        }
    }

    /// <summary>The strip colour down a card's left edge - one per pack, so a style is recognisable at a glance.</summary>
    private static Color StripColour(Theme theme)
    {
        if (!theme.IsBuiltIn)
        {
            return AppTheme.Accent;
        }

        return theme.SoundPack switch
        {
            "Podium" => Color.FromArgb(212, 160, 23),
            "Arcade" => Color.FromArgb(61, 220, 132),
            "Broadcast" => Color.FromArgb(59, 130, 246),
            "Synthwave" => Color.FromArgb(255, 79, 163),
            "PitLane" => Color.FromArgb(249, 115, 22),
            _ => AppTheme.TextMuted,
        };
    }

    private Control BuildCard(Theme theme, bool active)
    {
        var card = new Panel
        {
            Size = new Size(CardWidth, CardHeight),
            Margin = new Padding(6),
            BackColor = AppTheme.Surface,
        };

        var strip = StripColour(theme);
        card.Paint += (_, e) =>
        {
            using var border = new Pen(active ? AppTheme.Accent : AppTheme.Border, active ? 2 : 1);
            e.Graphics.DrawRectangle(border, 0, 0, card.Width - 1, card.Height - 1);
            using var brush = new SolidBrush(strip);
            e.Graphics.FillRectangle(brush, 0, 0, 6, card.Height);
        };

        var lblName = new Label
        {
            Text = theme.Name,
            Font = AppTheme.HeadingFont,
            ForeColor = AppTheme.TextDark,
            AutoSize = true,
            Location = new Point(20, 12),
            BackColor = Color.Transparent,
        };

        var packs = theme.SoundEffects.Effects.Values
            .Select(c => string.IsNullOrWhiteSpace(c.CustomFilePath) ? SoundPacks.DisplayName(c.BuiltInSound) : "own files")
            .Distinct()
            .ToList();

        var recordable = StingerLibrary.RecordableLines(theme.Voice);
        var recorded = StingerLibrary.Shared.CountRecorded(recordable);
        var recordedNote = recorded == 0 ? string.Empty : $"  \u2022  {recorded} of {recordable.Count} voice lines recorded";
        var backgroundNote = theme.Background is { } bg
            ? "  \u2022  " + string.Join(" + ", new[]
                {
                    SoundPacks.Find(bg.AmbienceBed) is not null ? "crowd" : null,
                    bg.MusicPlaylist is { Count: > 0 } ? "music" : null,
                }.Where(x => x is not null))
            : string.Empty;
        if (backgroundNote.Trim() == "\u2022")
        {
            backgroundNote = string.Empty;
        }

        var lblMeta = new Label
        {
            Text = $"{(theme.IsBuiltIn ? "Built-in" : "Custom")}  \u2022  Sounds: {string.Join(", ", packs)}{backgroundNote}{recordedNote}",
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
            AutoSize = true,
            MaximumSize = new Size(CardWidth - 40, 0),
            Location = new Point(20, UiLayout.Below(lblName, 2)),
            BackColor = Color.Transparent,
        };

        var lblDescription = new Label
        {
            Text = theme.Description,
            ForeColor = AppTheme.TextDark,
            AutoSize = false,
            Location = new Point(20, UiLayout.Below(lblMeta, 6)),
            BackColor = Color.Transparent,
        };

        var buttonsY = CardHeight - UiLayout.ButtonHeight - 12;
        lblDescription.Size = new Size(CardWidth - 36, Math.Max(20, buttonsY - lblDescription.Top - 6));

        card.Controls.Add(lblName);
        card.Controls.Add(lblMeta);
        card.Controls.Add(lblDescription);

        if (active)
        {
            var lblActive = new Label
            {
                Text = "LAST APPLIED",
                Font = AppTheme.MicroFont,
                ForeColor = AppTheme.Accent,
                AutoSize = true,
                BackColor = Color.Transparent,
            };
            lblActive.Location = new Point(CardWidth - TextRenderer.MeasureText(lblActive.Text, lblActive.Font).Width - 14, 14);
            card.Controls.Add(lblActive);
        }

        var btnPreview = new Button { Text = "Preview" };
        btnPreview.Click += async (_, _) => await TogglePreviewAsync(theme, btnPreview);

        var btnApply = new Button { Text = "Apply" };
        btnApply.Click += (_, _) => ApplyTheme(theme);

        var btnEdit = new Button { Text = theme.IsBuiltIn ? "Customize..." : "Edit..." };
        btnEdit.Click += (_, _) => EditTheme(theme);

        var btnLines = new Button { Text = "Voice Lines..." };
        btnLines.Click += (_, _) => ShowVoiceLinesMenu(theme, btnLines, recordable, recorded);

        var buttons = new List<Button> { btnPreview, btnApply, btnEdit, btnLines };

        AppTheme.LayoutButtonRow(20, buttonsY, 6, buttons.ToArray());
        card.Controls.AddRange(buttons.ToArray<Control>());

        return card;
    }

    // ------------------------------------------------------------ preview

    private async Task TogglePreviewAsync(Theme theme, Button button)
    {
        // A second press on the playing card stops it; a press on another
        // card stops the first and starts that one.
        var wasThis = _previewButton == button;
        StopPreview();
        if (wasThis)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _previewCts = cts;
        _previewButton = button;
        button.Text = "Stop";

        try
        {
            await PlayPreviewAsync(theme, cts.Token);
            if (!cts.IsCancellationRequested)
            {
                _lblStatus.Text = $"Previewed \"{theme.Name}\".";
            }
        }
        catch (Exception ex)
        {
            // Called from a button's async handler, so anything that got out
            // of here would take the whole app down. Say so and carry on.
            System.Diagnostics.Debug.WriteLine($"[soundscape preview] {ex}");
            SetStatus($"Couldn't preview \"{theme.Name}\": {ex.Message}");
        }
        finally
        {
            if (_previewCts == cts)
            {
                _previewCts = null;
                _previewButton = null;
                if (!button.IsDisposed)
                {
                    button.Text = "Preview";
                }
            }

            cts.Dispose();
        }
    }

    /// <summary>Plays a soundscape's pretend race, reporting each moment on the status line. Shared with the editor.</summary>
    internal Task PlayPreviewAsync(Theme theme, CancellationToken token) =>
        SoundscapePreview.RunAsync(
            theme,
            _voiceStore.Current,
            (float)_soundStore.Current.Volume,
            AnnouncementService.LeadingSilenceFor(_voiceStore.Current.WirelessSpeaker),
            Announcer,
            _chkPreviewVoice.Checked,
            step => SetStatus($"Previewing \"{theme.Name}\": {step}"),
            token);

    /// <summary>The status line, from whichever thread the preview reports on.</summary>
    private void SetStatus(string text)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke((MethodInvoker)(() => _lblStatus.Text = text));
        }
        else
        {
            _lblStatus.Text = text;
        }
    }

    private void StopPreview()
    {
        if (_previewCts is null)
        {
            return;
        }

        _previewCts.Cancel();

        // A line already handed to the voice queue would otherwise play on
        // after Stop. Race-priority lines are kept - nothing here queued any.
        Announcer?.StopAllPendingAudio();

        if (_previewButton is { IsDisposed: false })
        {
            _previewButton.Text = "Preview";
        }

        _previewCts = null;
        _previewButton = null;
        _lblStatus.Text = "Preview stopped.";
    }

    // ------------------------------------------------------------ apply

    private void ApplyTheme(Theme theme)
    {
        StopPreview();

        // Says exactly what ThemeStore.Apply leaves alone.
        var hasVoice = !string.IsNullOrWhiteSpace(theme.Voice.VoiceId);
        var hasLanguage = !string.IsNullOrWhiteSpace(theme.Voice.LanguageCode);

        var switches = (hasVoice
                ? $"This soundscape also switches the ElevenLabs voice to \"{(string.IsNullOrWhiteSpace(theme.Voice.VoiceName) ? theme.Voice.VoiceId : theme.Voice.VoiceName)}\" "
                  + "and the model it was saved with"
                : string.Empty)
            + (hasLanguage
                ? $"{(hasVoice ? ", and" : "This soundscape also sets")} the language to \"{theme.Voice.LanguageCode}\""
                : string.Empty);

        // The HeatCaller voice fixes the announcer's lines while it speaks,
        // so a soundscape applied under it changes the sounds and the tuning
        // and leaves the wording alone (see ThemeStore.Apply).
        var onHeatCaller = _voiceStore.Current.EffectiveEngine == VoiceEngine.HeatCaller;

        var notAffected = "Not affected: your ElevenLabs API key"
            + (hasVoice ? string.Empty : ", voice and model")
            + ", the voice and sound volumes, the wireless speaker setting, the output format"
            + (hasLanguage ? string.Empty : ", the language")
            + ", and which voice speaks - ElevenLabs, the HeatCaller voice or the Windows voice."
            + (onHeatCaller
                ? "\n\nThe HeatCaller voice speaks the standard announcer lines, so this soundscape's own lines are NOT applied. "
                  + "Its sounds and delivery are. To use its lines, switch to your own ElevenLabs key or the free Windows voice on Voice Settings."
                : string.Empty);

        var background = ThemeStore.DescribeBackground(theme, _musicStore);

        var confirm = AppDialog.Show(this,
            $"Apply \"{theme.Name}\"? This replaces your current announcer lines and sound effect settings.\n\n"
            + (switches.Length > 0 ? $"{switches}.\n\n" : string.Empty)
            + (background.Length > 0 ? $"{background}\n\n" : string.Empty)
            + notAffected,
            "Apply soundscape", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        // Everything Apply overwrites - lines, sounds, the crowd in Ambience,
        // the last-applied marker - taken now, so one undo puts it all back.
        var putBack = ThemeStore.CaptureForUndo(theme, _voiceStore, _soundStore, _ambienceStore);
        var themeStore = _themeStore;
        _undo.Capture($"Apply the {theme.Name} soundscape", () =>
        {
            putBack();
            themeStore.NotifyRestored(liveSettingsRestored: true);
        });

        ThemeStore.Apply(theme, _voiceStore, _soundStore, _ambienceStore, _musicStore);
        _themeStore.MarkApplied(theme);
        RefreshCards();
        _lblStatus.Text = $"Applied \"{theme.Name}\".";
        ThemeApplied?.Invoke();
    }

    // ------------------------------------------------------------ edit

    private void EditTheme(Theme theme)
    {
        StopPreview();

        var working = Clone(theme);
        var isNew = theme.IsBuiltIn;

        if (isNew)
        {
            // A built-in is never edited in place - the next version of the
            // app replaces it. Customizing starts a custom copy.
            working.Id = Guid.NewGuid().ToString("N");
            working.IsBuiltIn = false;
            working.BuiltInVersion = 0;
            working.Name = UniqueName($"{theme.Name} (my version)");
        }

        var library = _musicStore?.Current.Tracks.Select(t => t.DisplayName.Trim()).ToList() ?? new List<string>();
        using var editor = new SoundscapeEditorForm(working, isNew, _themeStore, _voiceStore.Current, PlayPreviewAsync, StopPreviewFromEditor, library);
        var answer = editor.ShowDialog(this);

        // Delete lives in the editor, to keep the cards to one row of buttons.
        if (answer == DialogResult.Abort && !isNew)
        {
            DeleteTheme(theme);
            return;
        }

        if (answer != DialogResult.OK)
        {
            return;
        }

        var edited = editor.Result;
        var themeStore = _themeStore;

        // Store only - the cards follow through OnThemeStoreRestored, so the
        // undo never reaches into this page.
        if (!isNew)
        {
            var before = Clone(theme);
            _undo.Capture($"Edit soundscape '{theme.Name}'", () =>
            {
                themeStore.Upsert(Clone(before));
                themeStore.NotifyRestored(liveSettingsRestored: false);
            });
        }
        else
        {
            var newId = edited.Id;
            _undo.Capture($"New soundscape '{edited.Name}'", () =>
            {
                if (themeStore.Get(newId) is not null)
                {
                    themeStore.Delete(newId);
                }

                themeStore.NotifyRestored(liveSettingsRestored: false);
            });
        }

        _themeStore.Upsert(edited);
        RefreshCards();
        SoundscapesChanged?.Invoke();

        _lblStatus.Text = isNew
            ? $"Saved \"{edited.Name}\" as a new soundscape. Apply it to use it."
            : $"Saved changes to \"{edited.Name}\"."
              + (edited.Id == _themeStore.ActiveThemeId ? " Apply it again to use the changes." : string.Empty);
    }

    private void StopPreviewFromEditor() => Announcer?.StopAllPendingAudio();

    // ------------------------------------------------------------ voice lines

    private void ShowVoiceLinesMenu(Theme theme, Button anchor, IReadOnlyList<string> lines, int recorded)
    {
        StopPreview();

        if (lines.Count == 0)
        {
            AppDialog.Show(this,
                $"\"{theme.Name}\" has no fixed lines to record. Voice lines are the staging, countdown, start, flag, last-lap and checkered-flag lines - "
                + "only for moments that are switched on, and only lines without a driver's name or a time in them.",
                "Voice lines", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var menu = new ContextMenuStrip();
        var missing = lines.Count - recorded;

        var missingCharacters = lines
            .Where(l => !StingerLibrary.Shared.Has(l))
            .Sum(l => VoiceProfile.StripAudioTags(l).Length);

        var recordItem = menu.Items.Add(
            missing == lines.Count
                ? $"Record {lines.Count} voice lines (about {missingCharacters:N0} characters)..."
                : $"Record the {missing} missing voice lines (about {missingCharacters:N0} characters)...",
            null,
            async (_, _) => await RecordVoiceLinesAsync(theme, lines, rerecordAll: false));
        recordItem.Enabled = missing > 0 && !_recordingLines;

        if (recorded > 0)
        {
            var rerecordItem = menu.Items.Add($"Re-record all {lines.Count}...", null, async (_, _) => await RecordVoiceLinesAsync(theme, lines, rerecordAll: true));
            rerecordItem.Enabled = !_recordingLines;
            var deleteItem = menu.Items.Add($"Delete the {recorded} recorded lines", null, (_, _) =>
            {
                if (_recordingLines)
                {
                    return;
                }

                if (AppDialog.Show(this,
                        $"Delete the recorded voice lines for \"{theme.Name}\"? Those lines go back to being spoken live by whichever voice this PC uses. "
                        + "Undo brings them back; recording them again would cost characters again.",
                        "Delete voice lines", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }

                // The recordings are moved aside rather than deleted, so the
                // undo can put them back - see StingerLibrary.DeleteForUndo.
                // A line recorded again since is kept as the newer take.
                IReadOnlyList<StingerLibrary.Held> held = Array.Empty<StingerLibrary.Held>();
                var themeStore = _themeStore;
                _undo.Capture($"Delete recorded voice lines for '{theme.Name}'", () =>
                {
                    StingerLibrary.Shared.PutBack(held, replaceNewer: false);
                    themeStore.NotifyRestored(liveSettingsRestored: false);
                });

                held = StingerLibrary.Shared.DeleteForUndo(lines);
                RefreshCards();
                _lblStatus.Text = $"Deleted {held.Count} recorded voice lines for \"{theme.Name}\".";
            });

            // Not while lines are being recorded - it would delete them
            // out from under the recording.
            deleteItem.Enabled = !_recordingLines;
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("What are voice lines?", null, (_, _) => AppDialog.Show(this,
            "The fixed race calls - staging, countdown, the start, flags, last lap and the checkered flag - recorded once in this soundscape's delivery, "
            + "with your ElevenLabs voice or the HeatCaller voice.\n\n"
            + "After that they play from this PC every race, whatever voice is chosen - even the free Windows voice, even with no internet - "
            + "so the big moments always sound the part. Lines with a driver's name or a time in them are always spoken live.",
            "Voice lines", MessageBoxButtons.OK, MessageBoxIcon.Information));

        // Styled after every item is in, so each item gets the theme's font
        // and padding - an unstyled menu is stock grey Windows chrome.
        AppTheme.StyleContextMenu(menu);
        menu.Closed += (_, _) => BeginInvoke((MethodInvoker)menu.Dispose);
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    private async Task RecordVoiceLinesAsync(Theme theme, IReadOnlyList<string> lines, bool rerecordAll)
    {
        var voice = _voiceStore.Current;

        if (Announcer is null)
        {
            return;
        }

        if (_recordingLines)
        {
            // Already recording (from this card or another) - see _recordingLines.
            _lblStatus.Text = "Voice lines are already being recorded - wait for that to finish.";
            return;
        }

        if (voice.EffectiveEngine == VoiceEngine.Windows)
        {
            AppDialog.Show(this,
                "Voice lines are recorded with ElevenLabs - your own key, or the HeatCaller voice through your relay. "
                + "This PC speaks with the Windows voice, which already says these lines for free. "
                + "Switch the voice on Voice Settings to record them, then switch back if you like: the recordings keep playing.",
                "Voice lines", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var toRecord = rerecordAll ? lines.ToList() : lines.Where(l => !StingerLibrary.Shared.Has(l)).ToList();
        if (toRecord.Count == 0)
        {
            return;
        }

        var characters = toRecord.Sum(l => VoiceProfile.StripAudioTags(l).Length);
        var voiceName = voice.EffectiveEngine == VoiceEngine.HeatCaller
            ? (string.IsNullOrWhiteSpace(voice.HeatCallerVoiceName) ? "the HeatCaller voice" : $"the HeatCaller voice ({voice.HeatCallerVoiceName})")
            : (string.IsNullOrWhiteSpace(voice.VoiceName) ? "your ElevenLabs voice" : voice.VoiceName);

        var heatCallerNote = voice.EffectiveEngine == VoiceEngine.HeatCaller
            ? "\n\nWhile this PC uses the HeatCaller voice the announcer says the standard lines, so these recordings play once you switch to your own key or the Windows voice."
            : string.Empty;

        if (AppDialog.Show(this,
                $"Record {toRecord.Count} lines for \"{theme.Name}\" with {voiceName}? That's about {characters:N0} characters, once.{heatCallerNote}",
                "Record voice lines", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        // Checked again: the question above is modal, but it is the last
        // point before characters are spent.
        if (_recordingLines)
        {
            return;
        }

        _recordingLines = true;

        // For an undo of a re-record pressed while it is still recording:
        // putting the old takes back then would have the rest of the run
        // record straight over them, destroying takes that were paid for.
        // Instead the run stops at the next line, and the old takes go back
        // once it has (see finally).
        IReadOnlyList<StingerLibrary.Held> previousTakes = Array.Empty<StingerLibrary.Held>();
        var recordingDone = false;
        var undoneWhileRecording = false;

        try
        {
            if (rerecordAll)
            {
                // Out of the way first - a recorded line would otherwise be
                // handed straight back instead of rendered again.
                //
                // Moved aside rather than deleted, so a re-record that comes
                // out worse can be undone: the old takes go back over the new
                // ones. The characters spent on the new takes are not
                // refunded by an undo - that part is ElevenLabs'.
                var themeStore = _themeStore;
                _undo.Capture($"Re-record voice lines for '{theme.Name}'", () =>
                {
                    if (!recordingDone)
                    {
                        undoneWhileRecording = true;
                        return;
                    }

                    StingerLibrary.Shared.PutBack(previousTakes, replaceNewer: true);
                    themeStore.NotifyRestored(liveSettingsRestored: false);
                });

                previousTakes = StingerLibrary.Shared.DeleteForUndo(toRecord);
            }

            var ok = 0;
            for (var i = 0; i < toRecord.Count && !undoneWhileRecording; i++)
            {
                _lblStatus.Text = $"Recording voice line {i + 1} of {toRecord.Count} for \"{theme.Name}\"...";
                var audio = await Announcer.RecordVoiceLineAsync(toRecord[i], theme.Voice.Stability, theme.Voice.Style);
                if (audio is not null && !undoneWhileRecording)
                {
                    StingerLibrary.Shared.Save(toRecord[i], audio, voiceName, theme.Name);
                    ok++;
                }
            }

            RefreshCards();
            _lblStatus.Text = ok == toRecord.Count
                ? $"Recorded {ok} voice lines for \"{theme.Name}\"."
                : $"Recorded {ok} of {toRecord.Count} voice lines for \"{theme.Name}\" - the rest couldn't be rendered with ElevenLabs. Check Voice Settings, then record the missing ones.";
        }
        catch (Exception ex)
        {
            // Run from a menu item's async handler - an exception getting out
            // would take the app down.
            System.Diagnostics.Debug.WriteLine($"[voice lines] {ex}");
            if (!IsDisposed)
            {
                RefreshCards();
                _lblStatus.Text = $"Recording voice lines for \"{theme.Name}\" stopped: {ex.Message}";
            }
        }
        finally
        {
            _recordingLines = false;
            recordingDone = true;

            if (undoneWhileRecording)
            {
                // The undo pressed mid-run, done now that nothing more will
                // be recorded over the old takes. Store only, as any undo.
                // Caught: this runs under a menu item's async handler, where
                // an exception would take the app down.
                try
                {
                    StingerLibrary.Shared.PutBack(previousTakes, replaceNewer: true);
                    _themeStore.NotifyRestored(liveSettingsRestored: false);

                    if (!IsDisposed)
                    {
                        _lblStatus.Text = $"Re-recording \"{theme.Name}\" stopped by Undo - the previous takes are back.";
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[voice lines] undo: {ex}");
                }
            }
        }
    }

    private void DeleteTheme(Theme theme)
    {
        StopPreview();

        if (AppDialog.Show(this, $"Delete soundscape \"{theme.Name}\"? This can be reversed with Undo.", "Confirm delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        // The soundscape bundles a full voice and sound configuration, so
        // the whole object goes into the capture - a copy, so nothing that
        // still holds the original can change what comes back. Upsert goes
        // by id, so an undo can never list it twice.
        var removed = Clone(theme);
        var themeStore = _themeStore;
        _undo.Capture($"Delete soundscape '{theme.Name}'", () =>
        {
            themeStore.Upsert(Clone(removed));
            themeStore.NotifyRestored(liveSettingsRestored: false);
        });

        _themeStore.Delete(theme.Id);
        RefreshCards();
        SoundscapesChanged?.Invoke();
        _lblStatus.Text = $"Deleted \"{theme.Name}\".";
    }

    private void BtnSaveNew_Click(object? sender, EventArgs e)
    {
        var name = PromptForText("Save Current Settings as New Soundscape", "Name:", string.Empty);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var description = PromptForText("Save Current Settings as New Soundscape", "Short description (optional):", string.Empty) ?? string.Empty;

        var theme = ThemeStore.SnapshotCurrent(_voiceStore, _soundStore, UniqueName(name.Trim()), description.Trim());
        _themeStore.Upsert(theme);
        RefreshCards();
        _lblStatus.Text = $"Saved your current voice and sound settings as \"{theme.Name}\".";
    }

    /// <summary>Adds " 2", " 3"... until no other soundscape has the name.</summary>
    private string UniqueName(string name)
    {
        var taken = _themeStore.All.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name))
        {
            return name;
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{name} {i}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    internal static Theme Clone(Theme theme) =>
        System.Text.Json.JsonSerializer.Deserialize<Theme>(System.Text.Json.JsonSerializer.Serialize(theme))!;

    private string? PromptForText(string title, string prompt, string defaultValue)
    {
        using var dialog = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(420, 176),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Font = Font,
        };

        var lbl = new Label { Text = prompt, Location = new Point(12, 16), AutoSize = true };
        var txt = new TextBox { Location = new Point(12, 40), Width = 380, Text = defaultValue };
        var btnOk = new Button { Text = "OK", Location = new Point(228, 78), Width = 80, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", Location = new Point(312, 78), Width = 80, DialogResult = DialogResult.Cancel };

        dialog.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel });
        dialog.AcceptButton = btnOk;
        dialog.CancelButton = btnCancel;
        dialog.BackColor = AppTheme.Background;
        AppTheme.ApplyRecursively(dialog);

        return dialog.ShowDialog(this) == DialogResult.OK ? txt.Text : null;
    }
}
