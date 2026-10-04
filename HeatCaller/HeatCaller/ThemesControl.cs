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

    /// <summary>For whether the setup wizards ask about the sound, and lap times only (LapTimesOnly). Null in a host without them - the tick is not shown.</summary>
    private readonly AppSettings? _settings;

    private readonly CardWall _cards;
    private readonly Label _lblStatus = new();
    private readonly CheckBox _chkPreviewVoice = new ThemedCheckBox();

    /// <summary>Whether the setup wizards ask about the sound - turned off there by "Don't ask about the sound again", and back on here.</summary>
    private readonly CheckBox _chkAskInWizards = new ThemedCheckBox();

    private CancellationTokenSource? _previewCts;
    private Button? _previewButton;

    /// <summary>
    /// True while RecordVoiceLinesAsync is rendering. The menu can be opened
    /// again while it runs, and a second run would pay ElevenLabs for every
    /// line a second time.
    /// </summary>
    private bool _recordingLines;

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>
    /// The cards' labels that wear a face of their own, and the face - kept
    /// at the text size in use by FaceCards. Forgotten, with the cards, each
    /// time the cards are built again, so a text-size change never reaches
    /// for a label that has gone.
    /// </summary>
    private readonly List<(Control Control, Func<Font> Face)> _cardFaces = new();

    /// <summary>The cards' rows of buttons, which the least a card can do with is measured from - see CardLeastWidth. Forgotten with the cards.</summary>
    private readonly List<ButtonBar> _cardButtons = new();

    /// <summary>
    /// The narrowest a card gets at the normal text size, whatever its
    /// buttons need: room for its description to read as a line of text
    /// rather than a column of words. See CardLeastWidth.
    /// </summary>
    private const int CardReadingWidth = 360;

    /// <summary>The pack's colour down a card's left edge - see PaintCardMarks.</summary>
    private const int StripWidth = 6;

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
        MusicStore? musicStore = null,
        AppSettings? settings = null)
    {
        _themeStore = themeStore;
        _voiceStore = voiceStore;
        _soundStore = soundStore;
        _undo = undo;
        _ambienceStore = ambienceStore;
        _musicStore = musicStore;
        _settings = settings;

        _cards = new CardWall(CardLeastWidth);

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize.
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshCards();

        // Undo works on the stores, never on this page - see OnThemeStoreRestored.
        _themeStore.Restored += OnThemeStoreRestored;
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

    /// <summary>
    /// Marks the soundscape applied somewhere else - a venue, or a setup
    /// wizard - on its card, or none for lap times only.
    /// </summary>
    public void ShowApplied()
    {
        if (!IsDisposed && !Disposing)
        {
            RefreshCards();
        }
    }

    /// <summary>
    /// The wizards' tick as settings have it - "Don't ask about the sound
    /// again" changes it from a wizard, which can be opened over this page
    /// (Ctrl+Shift+S) without the page ever being hidden. Called by the host
    /// when a wizard closes, and whenever the page is shown.
    /// </summary>
    public void ShowAskTick()
    {
        if (_settings is not null && _chkAskInWizards.Checked != _settings.AskAboutSoundInWizards)
        {
            _chkAskInWizards.Checked = _settings.AskAboutSoundInWizards;
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (Visible)
        {
            ShowAskTick();
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

    /// <summary>
    /// The intro; Save Current Settings as New with the preview-voice box
    /// beside it (under it when the line is too narrow); the status line;
    /// then the cards, as many to a row as fit (CardWall), taking the height
    /// that is left and scrolling inside it - so the status line, which a
    /// preview reports on step by step, stays in view whichever card is
    /// playing. On the layout pieces (LayoutPieces.cs), so nothing is placed
    /// by hand.
    ///
    /// It used to be placed in pixels: a header panel of fixed height, its
    /// intro and status capped at 900px, docked over a flow panel of cards
    /// 214px tall whatever their text needed - a description longer than
    /// that was cut off - and at least 440px wide at every text size.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "A soundscape is the whole sound of race day: a sound for every moment, and an announcer's words and delivery to match. " +
                   "Preview one to hear a short pretend race, Apply it to use it, or Customize to mix packs and rewrite lines. " +
                   "Applying never touches your API key, which voice speaks, or this PC's volumes. Colours and the club logo are on the Themes page.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        var btnSaveNew = new Button { Text = "Save Current Settings as New..." };
        btnSaveNew.Click += BtnSaveNew_Click;

        _chkPreviewVoice.Text = "Previews include the announcer's voice";
        _chkPreviewVoice.AutoSize = true;
        _chkPreviewVoice.Checked = true;
        // Disposed with the page - a ToolTip left behind keeps the controls
        // it was given alive after the page itself has gone.
        var previewTip = new ToolTip();
        AppTheme.StyleToolTip(previewTip);
        Disposed += (_, _) => previewTip.Dispose();
        previewTip.SetToolTip(_chkPreviewVoice,
            "Lines are spoken by the voice this PC already uses. On your own ElevenLabs key each preview line costs characters, " +
            "so untick this to hear the sounds only.");

        // A line kept for it whatever it says - it starts blank - so the
        // cards under it do not hop the first time a preview reports. Its
        // own face, so the line is measured in the face it is drawn in (see
        // TextSize.ReserveLines).
        _chkAskInWizards.Text = "Ask which sound to use in Set Up Tonight";
        _chkAskInWizards.AutoSize = true;
        _chkAskInWizards.Visible = _settings is not null;
        _chkAskInWizards.Checked = _settings?.AskAboutSoundInWizards ?? true;
        _chkAskInWizards.CheckedChanged += (_, _) =>
        {
            if (_settings is not null && _settings.AskAboutSoundInWizards != _chkAskInWizards.Checked)
            {
                _settings.AskAboutSoundInWizards = _chkAskInWizards.Checked;
                _settings.Save();
            }
        };
        previewTip.SetToolTip(_chkAskInWizards,
            "Set Up Tonight, and the setup the app opens with the first time, offer a soundscape or lap times only. "
            + "Untick to leave the sound as it is set here.");

        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = AppTheme.TextMuted;
        _lblStatus.Text = " ";
        _textSize.Wear(_lblStatus, () => AppTheme.BodyFont);
        _textSize.ReserveLines(_lblStatus, 1);

        // The cards scroll inside the rest of the page, under everything
        // above them, as they did in their flow panel. A page of their own,
        // without the page's margins - the page they are on has those - and
        // with the theme's room under the last row (AutoScrollMargin).
        var scroller = new PageStack { Padding = Padding.Empty };

        scroller.Add(_cards, gapBelow: 0);

        // The cards' own faces at the text size in use - see FaceCards.
        _textSize.Keep(FaceCards);

        var page = new FillPage();

        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.Add(FieldGrid.Inline(btnSaveNew, _chkPreviewVoice), gapBelow: _settings is null ? UiLayout.RowGap : UiLayout.HintGap);

        if (_settings is not null)
        {
            page.Add(_chkAskInWizards, stretch: false, gapBelow: UiLayout.RowGap);
        }
        page.Add(_lblStatus, gapBelow: UiLayout.RowGap);

        // At least a row of cards - below that the page scrolls.
        page.AddFill(scroller, _cards.LeastHeight, gapBelow: 0);

        Controls.Add(page);
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

        // The old cards' labels and buttons went with them.
        _cardFaces.Clear();
        _cardButtons.Clear();

        var activeId = _themeStore.ActiveThemeId;
        foreach (var theme in _themeStore.All)
        {
            _cards.AddCard(BuildCard(theme, theme.Id == activeId));
        }

        // No room kept under the last card here: the cards' page keeps the
        // theme's room under its last row itself (AutoScrollMargin), which a
        // flow panel left out of what it scrolled.
        _cards.ResumeLayout();
        AppTheme.ApplyRecursively(_cards);
    }

    /// <summary>
    /// The least a card can do with across: its row of buttons on one line
    /// - the widest of them, a built-in's - and the card's inset, and never
    /// narrower than CardReadingWidth at the text size in use.
    ///
    /// It was 440px whatever the text size, and wider when the buttons were:
    /// at 150% two cards to a row that three would have fitted, and at 200%
    /// a description of two words to a line.
    /// </summary>
    private int CardLeastWidth() =>
        Math.Max(
            UiLayout.AtTextSize(CardReadingWidth),
            _cardButtons.Select(bar => bar.OneLineWidth).DefaultIfEmpty(0).Max() + Section.Inset.Horizontal);

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

    /// <summary>
    /// One soundscape's card: its name - with LAST APPLIED against the
    /// right-hand edge on the one applied last - what it is made of, its
    /// description, and its buttons along the bottom. The card is as tall
    /// as the tallest in its row (CardWall), and the room that leaves goes
    /// under the description, so the buttons of a row line up.
    /// </summary>
    private Section BuildCard(Theme theme, bool active)
    {
        var card = new Section();

        var strip = StripColour(theme);
        card.Paint += (_, e) => PaintCardMarks(e.Graphics, card.ClientSize, strip, active);

        var lblName = CardFace(new Label
        {
            Text = theme.Name,
            ForeColor = AppTheme.TextDark,
            AutoSize = true,
        }, () => AppTheme.HeadingFont);

        if (active)
        {
            var lblActive = CardFace(new Label
            {
                Text = "LAST APPLIED",
                ForeColor = AppTheme.Accent,
                AutoSize = true,
            }, () => AppTheme.MicroFont);

            // The name on the left and the marker against the right-hand
            // edge, on one line when they fit - the marker under the name,
            // still on the right, when they do not (SplitBar).
            card.Add(new SplitBar(new ButtonBar().With(lblName), new ButtonBar(alignRight: true).With(lblActive)), gapBelow: UiLayout.HintGap);
        }
        else
        {
            card.Add(lblName, gapBelow: UiLayout.HintGap);
        }

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

        var lblMeta = CardFace(new Label
        {
            Text = $"{(theme.IsBuiltIn ? "Built-in" : "Custom")}  \u2022  Sounds: {string.Join(", ", packs)}{backgroundNote}{recordedNote}",
            ForeColor = AppTheme.TextMuted,
            AutoSize = true,
        }, () => AppTheme.SmallFont);

        card.Add(lblMeta);

        // Wrapped to the card's width, however long - it used to be a box
        // of fixed size, and a longer description was cut off at its foot.
        var lblDescription = new Label
        {
            Text = theme.Description,
            ForeColor = AppTheme.TextDark,
            AutoSize = true,
        };

        card.Add(lblDescription);

        // The description's row takes whatever height the card has over what
        // its parts need - the card is as tall as the tallest in its row - so
        // the buttons sit along the bottom of every card in a row, as they
        // did when the cards were all one fixed height.
        var descriptionRow = card.RowStyles[card.GetRow(lblDescription)];
        descriptionRow.SizeType = SizeType.Percent;
        descriptionRow.Height = 100f;

        var btnPreview = new Button { Text = "Preview" };
        btnPreview.Click += async (_, _) => await TogglePreviewAsync(theme, btnPreview);

        var btnApply = new Button { Text = "Apply" };
        btnApply.Click += (_, _) => ApplyTheme(theme);

        var btnEdit = new Button { Text = theme.IsBuiltIn ? "Customize..." : "Edit..." };
        btnEdit.Click += (_, _) => EditTheme(theme);

        var btnLines = new Button { Text = "Voice Lines..." };
        btnLines.Click += (_, _) => ShowVoiceLinesMenu(theme, btnLines, recordable, recorded);

        // One row of buttons, each only as wide as its caption and a little
        // room, so the four stay on one line in a card of readable width.
        var buttons = new ButtonBar(tight: true).With(btnPreview, btnApply, btnEdit, btnLines);

        _cardButtons.Add(buttons);
        card.Add(buttons, gapBelow: 0);

        return card;
    }

    /// <summary>
    /// What a card has on top of the theme's own: the pack's colour down its
    /// left edge, clipped to the card's rounded corners, and - on the
    /// soundscape applied last - an outline in the accent. Both inside the
    /// card's inset, where nothing else is drawn.
    /// </summary>
    private static void PaintCardMarks(Graphics g, Size size, Color strip, bool active)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // The card's own outline, as AppTheme.DrawCard draws it.
        var outline = RectangleF.Inflate(new RectangleF(0, 0, size.Width, size.Height), -0.5f, -0.5f);

        using (var edge = AppTheme.RoundedRect(outline, AppTheme.CornerRadius))
        {
            var saved = g.Save();
            g.SetClip(edge);

            using (var brush = new SolidBrush(strip))
            {
                g.FillRectangle(brush, outline.Left, outline.Top, StripWidth, outline.Height);
            }

            g.Restore(saved);
        }

        if (active)
        {
            // Two pixels wide, inside the card's edge rather than across it.
            var inside = RectangleF.Inflate(new RectangleF(0, 0, size.Width, size.Height), -1f, -1f);

            using var ring = AppTheme.RoundedRect(inside, AppTheme.CornerRadius);
            using var pen = new Pen(AppTheme.Accent, 2f);

            g.DrawPath(pen, ring);
        }
    }

    /// <summary>Gives a card's label a face of its own, and keeps it at the text size in use - see _cardFaces.</summary>
    private T CardFace<T>(T control, Func<Font> face) where T : Control
    {
        _cardFaces.Add((control, face));
        control.Font = face();

        return control;
    }

    /// <summary>The cards' labels in their faces at the text size in use. Run again when it changes (TextSize.Keep).</summary>
    private void FaceCards()
    {
        foreach (var (control, face) in _cardFaces)
        {
            var font = face();

            if (!control.IsDisposed && !Equals(control.Font, font))
            {
                control.Font = font;
            }
        }
    }

    /// <summary>
    /// The soundscapes' cards side by side in columns of one width - as
    /// many to a row as fit at the least a card can do with, wrapping onto
    /// the next row - every card in a row as tall as the tallest, so the
    /// buttons along their bottoms line up.
    ///
    /// Placed by hand rather than by a table or a flow panel: how many cards
    /// go on a row comes from the width, and the cards then share it, which
    /// neither does. Sizes itself - put it in a PageStack, which gives it
    /// the width.
    /// </summary>
    private sealed class CardWall : Panel
    {
        /// <summary>Between two cards, across and down.</summary>
        private const int Gap = UiLayout.RowGap;

        private readonly Func<int> _leastCardWidth;

        /// <param name="leastCardWidth">The least a card can do with across, asked whenever the wall is measured - so it can follow the text size and the buttons.</param>
        public CardWall(Func<int> leastCardWidth)
        {
            _leastCardWidth = leastCardWidth;

            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            Tag = AppTheme.KeepBackColor;
        }

        /// <summary>
        /// Adds a card after the last. Sized here, never by the layout: a
        /// card that sized itself would be resized by the panel to its width
        /// on one line (see Beside.Side).
        ///
        /// The wall is placed again whenever a card lays itself out - a
        /// button's caption changing ("Preview" to "Stop") is something only
        /// the card sees. Told as the card's layout starts, before its rows
        /// are placed - for the reason Beside.AddSideCard gives.
        /// </summary>
        public void AddCard(Section card)
        {
            card.AutoSize = false;
            card.Margin = Padding.Empty;
            card.Layout += (_, _) => PerformLayout();

            Controls.Add(card);
        }

        /// <summary>The least the wall can do with at <paramref name="width"/> before it scrolls: its first row.</summary>
        public int LeastHeight(int width) => Arrange(Math.Max(1, width), place: false, rows: 1);

        /// <summary>How tall the rows come to at the width proposed. With none, the cards on one row at their least.</summary>
        public override Size GetPreferredSize(Size proposedSize)
        {
            var width = proposedSize.Width is > 0 and < int.MaxValue / 4
                ? proposedSize.Width
                : OneRowWidth();

            return new Size(width, Arrange(width, place: false));
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);

            Arrange(Math.Max(1, ClientSize.Width), place: true);
        }

        /// <summary>Coming into view: while it was hidden, a card hidden in its own right could not be told from the rest.</summary>
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);

            if (Visible)
            {
                PerformLayout();
            }
        }

        /// <summary>The cards that take a place - hidden in its own right leaves one out, askable only while the wall is on screen (see Pieces.TrailingGap).</summary>
        private List<Control> Cards() =>
            Controls.Cast<Control>().Where(card => (!Visible || card.Visible) && card is not ThinScrollBar).ToList();

        /// <summary>Every card on one row, at its least.</summary>
        private int OneRowWidth()
        {
            var count = Cards().Count;

            return count == 0 ? 0 : count * Math.Max(1, _leastCardWidth()) + (count - 1) * Gap;
        }

        /// <summary>
        /// Works the rows out <paramref name="width"/> wide - the first
        /// <paramref name="rows"/> of them, placing the cards when
        /// <paramref name="place"/> is set - and returns how tall they come
        /// to. The columns share the width to the pixel, the last one ending
        /// on the edge; a last row that is short keeps the columns' widths.
        /// </summary>
        private int Arrange(int width, bool place, int rows = int.MaxValue)
        {
            var cards = Cards();

            if (cards.Count == 0)
            {
                return 0;
            }

            var columns = Math.Clamp((width + Gap) / (Math.Max(1, _leastCardWidth()) + Gap), 1, cards.Count);
            var room = Math.Max(0, width - Gap * (columns - 1));
            var y = 0;

            for (var first = 0; first < cards.Count && first / columns < rows; first += columns)
            {
                var row = cards.Skip(first).Take(columns).ToList();
                var height = 0;

                for (var i = 0; i < row.Count; i++)
                {
                    height = Math.Max(height, row[i].GetPreferredSize(new Size(ColumnWidth(i), 0)).Height);
                }

                if (place)
                {
                    for (var i = 0; i < row.Count; i++)
                    {
                        var bounds = new Rectangle(ColumnLeft(i), y, ColumnWidth(i), height);

                        if (row[i].Bounds != bounds)
                        {
                            row[i].Bounds = bounds;
                        }
                    }
                }

                y += height + Gap;
            }

            return y - Gap;

            int ColumnLeft(int column) => column * (room / columns) + column * Gap;

            int ColumnWidth(int column) =>
                Math.Max(1, column == columns - 1 ? width - ColumnLeft(column) : room / columns);
        }
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

        // The announcer's queue is deliberately left alone. This used to call
        // StopAllPendingAudio, to stop a preview line already handed to the
        // voice queue from playing on after Stop. It never did that: the
        // preview speaks through SpeakReminder, and Reminder is one of the
        // priority types StopAllPendingAudio keeps. What it DID clear was the
        // live race's - the countdown ticks, every queued lap time and fast
        // lap, and the pending lap batches - so stopping a preview, or
        // Apply / Edit / Delete while one played, during a heat silently
        // dropped calls for the drivers on track. The preview's own sounds
        // stop with the token above, and it queues at most one line at a
        // time (it waits for each to be spoken), so at worst that one line
        // finishes.
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
        // so a soundscape applied under it changes the sounds and leaves the
        // wording alone (see ThemeStore.Apply). Its tuning is saved too, but
        // that voice is rendered by the relay in its own delivery - only the
        // voice and the text go with a line (HeatCallerVoice) - so the
        // message does not say the tuning applies (docs check, 30 Sep). The
        // most announcements per lap does: the announcer counts them on
        // every voice.
        // The wireless speaker tick is hidden on Voice Settings (it changes
        // nothing now), so it is not named either.
        var onHeatCaller = _voiceStore.Current.EffectiveEngine == VoiceEngine.HeatCaller;

        var notAffected = "Not affected: your ElevenLabs API key"
            + (hasVoice ? string.Empty : ", voice and model")
            + ", the voice and sound volumes, the output format"
            + (hasLanguage ? string.Empty : ", the language")
            + ", and which voice speaks - ElevenLabs, the HeatCaller voice or the Windows voice."
            + (onHeatCaller
                ? "\n\nThe HeatCaller voice speaks the standard announcer lines in its own delivery, so this soundscape's own lines "
                  + "are NOT applied, and its stability, similarity, style and speed are saved but make no difference until another "
                  + "voice speaks. Its sounds, and its Max spoken announcements per lap, are applied. To use its lines, switch to your own "
                  + "ElevenLabs key or the free Windows voice on Voice Settings."
                : string.Empty);

        var background = ThemeStore.DescribeBackground(theme, _musicStore);

        // On the HeatCaller voice the lines stay as they are (above), so the
        // question does not say they are replaced.
        var replaces = onHeatCaller
            ? "your sound effect settings"
            : "your current announcer lines and sound effect settings";

        // Lap times only is left as a soundscape is applied (LapTimesOnly.Leave).
        var leavesLapTimesOnly = _settings is not null && LapTimesOnly.IsOn(_settings)
            ? "The sound is lap times only now. Music, ambience and the spoken race intros and recaps come back on as they "
              + "were before, with this soundscape's announcements and sounds.\n\n"
            : string.Empty;

        var confirm = AppDialog.Show(this,
            $"Apply \"{theme.Name}\"? This replaces {replaces}.\n\n"
            + leavesLapTimesOnly
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

        // After the capture above - see LapTimesOnly.Leave.
        var putBackLapTimesOnly = _settings is null ? null : LapTimesOnly.Leave(_settings, _voiceStore);

        var themeStore = _themeStore;
        _undo.Capture($"Apply the {theme.Name} soundscape", () =>
        {
            putBack();
            putBackLapTimesOnly?.Invoke();
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

    /// <summary>
    /// The editor's "stop the voice" hook when its preview stops. Nothing to
    /// do: it used to clear the announcer's queue, which never removed the
    /// preview's own line (a Reminder, which is kept) but did throw away a
    /// live race's countdown ticks and lap calls - see StopPreview. The
    /// editor cancels its own preview token, which stops the sounds.
    /// </summary>
    private void StopPreviewFromEditor()
    {
    }

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

        if (voice.EffectiveEngine is VoiceEngine.Windows or VoiceEngine.Kokoro)
        {
            AppDialog.Show(this,
                "Voice lines are recorded with ElevenLabs - your own key, or the HeatCaller voice through your relay. "
                + $"This PC speaks with the {(voice.EffectiveEngine == VoiceEngine.Kokoro ? "Kokoro" : "Windows")} voice, which already says these lines for free. "
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

    /// <summary>
    /// Asks for one line of text over this page - the new soundscape's name,
    /// then its description (BtnSaveNew_Click). Null when cancelled; the
    /// answer as typed otherwise.
    ///
    /// In the app's shared prompt (TextPromptForm): the same window this
    /// method used to build for itself - the question, the box under it, OK
    /// and Cancel, in the body face - on the layout pieces, and a class the
    /// layout test can build (WindowCatalog). The one built here was placed
    /// in pixels: a 420x176 window, the box a fixed 380px wide and the two
    /// buttons a fixed 80px at fixed places under it.
    /// </summary>
    private string? PromptForText(string title, string prompt, string defaultValue)
    {
        using var dialog = new TextPromptForm(title, prompt, defaultValue);

        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Answer : null;
    }
}
