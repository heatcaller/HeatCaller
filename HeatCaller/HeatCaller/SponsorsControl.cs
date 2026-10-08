namespace HeatCaller;

/// <summary>Manage sponsor/promo slides shown in rotation on the Big Screen, and how long each stays up.</summary>
public sealed class SponsorsControl : UserControl
{
    private readonly SponsorStore _store;
    private readonly UndoService _undo;

    private readonly CheckBox _chkEnabled = new ThemedCheckBox();
    private readonly ListBox _lstSlides = new();
    private readonly PictureBox _picPreview = new();
    private readonly NumericUpDown _numLeaderboardSeconds = new();
    private readonly NumericUpDown _numSlideSeconds = new();
    private readonly CheckBox _chkPauseDuringRace = new ThemedCheckBox();
    private readonly CheckBox _chkPlayVideoSound = new ThemedCheckBox();
    private readonly CheckBox _chkMuteMusicDuringVideo = new ThemedCheckBox();

    /// <summary>
    /// Sits over the preview and says what a video slide is, since there is
    /// nothing to decode into the picture box. A child of the picture box
    /// rather than a row of its own, so the card's layout is the same
    /// whichever kind of slide is selected.
    /// </summary>
    private readonly Label _lblVideoNote = new();

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The slides and the preview side by side - the list's column is worked out from its width. See ListColumnWidth.</summary>
    private Beside? _slidesBand;

    /// <summary>The list's column, the narrowest it gets, and the preview's least height, at the normal text size.</summary>
    private const int ListWidth = 320;
    private const int ListLeastWidth = 160;
    private const int PreviewLeastHeight = 160;

    /// <summary>The fewest slides the list shows before the page scrolls instead.</summary>
    private const int ListLeastRows = 4;

    public SponsorsControl(SponsorStore store, UndoService undo)
    {
        _store = store;
        _undo = undo;

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize.
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshSlides();

        // Undo changes the store, never this page - see OnStoreRestored.
        _store.Restored += OnStoreRestored;
    }

    /// <summary>Re-reads the slides and timing after an undo changed them.</summary>
    private void OnStoreRestored()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        // The boxes save what they show when they change - setting them to
        // what the store already holds writes the same values back, nothing
        // more.
        _chkEnabled.Checked = _store.Current.Enabled;
        _numLeaderboardSeconds.Value = Math.Clamp(_store.Current.LeaderboardSeconds, 5, 600);
        _numSlideSeconds.Value = Math.Clamp(_store.Current.SlideSeconds, 2, 120);
        _chkPauseDuringRace.Checked = _store.Current.PauseDuringRace;
        _chkPlayVideoSound.Checked = _store.Current.PlayVideoSound;
        _chkMuteMusicDuringVideo.Checked = _store.Current.MuteMusicDuringVideo;
        SyncVideoSoundEnabled();

        RefreshSlides();
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _store.Restored -= OnStoreRestored;
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// The intro and the on/off switch, then the slides in a card that takes
    /// the page's height - the list down the left, the selected slide's
    /// preview beside it taking the rest, and the list's buttons under both -
    /// and the rotation's timing in a card under that. On the layout pieces
    /// (LayoutPieces.cs), so nothing is placed by hand.
    ///
    /// It used to be placed in pixels: the list 320x280 and the preview
    /// 360x280 whatever the window, the buttons stepped along from the list's
    /// left edge, and the timing in a group box widened by hand to whatever
    /// its measured row came to.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Show sponsor or promo images and videos in rotation on the Big Screen between leaderboard views. Leave this off " +
                   "entirely for a home setup - it's meant for a shop or club venue with sponsors to display. A video plays to its " +
                   "own end rather than for the slide time below.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _chkEnabled.Text = "Show sponsor slides on the Big Screen";
        _chkEnabled.AutoSize = true;
        _chkEnabled.Checked = _store.Current.Enabled;
        _chkEnabled.CheckedChanged += (_, _) =>
        {
            _store.Current.Enabled = _chkEnabled.Checked;
            _store.Save();
        };

        // --- The slides ---

        _lstSlides.IntegralHeight = false;
        _lstSlides.SelectedIndexChanged += (_, _) => RefreshPreview();

        _picPreview.BorderStyle = BorderStyle.FixedSingle;
        _picPreview.SizeMode = PictureBoxSizeMode.Zoom;
        _picPreview.BackColor = AppTheme.Surface;

        _lblVideoNote.Dock = DockStyle.Fill;
        _lblVideoNote.TextAlign = ContentAlignment.MiddleCenter;
        _lblVideoNote.ForeColor = AppTheme.TextMuted;
        _lblVideoNote.Visible = false;
        _picPreview.Controls.Add(_lblVideoNote);

        // Sized to their own captions by the bar they sit in.
        var btnAdd = new Button { Text = "Add Image..." };
        btnAdd.Click += (_, _) => AddSlide();

        var btnAddVideo = new Button { Text = "Add Video..." };
        btnAddVideo.Click += (_, _) => AddVideo();

        var btnRemove = new Button { Text = "Remove" };
        btnRemove.Click += (_, _) => RemoveSelected();

        var btnMoveUp = new Button { Text = "Move Up" };
        btnMoveUp.Click += (_, _) => MoveSelected(-1);

        var btnMoveDown = new Button { Text = "Move Down" };
        btnMoveDown.Click += (_, _) => MoveSelected(1);

        // The list down the left, as wide as it was at the normal text size
        // (see ListColumnWidth), and the preview taking the rest - a slide is
        // a picture made for a TV, so the room goes to seeing it.
        var band = new Beside(ListColumnWidth) { SideOnLeft = true };
        _slidesBand = band;

        band.AddFill(_picPreview, _ => UiLayout.AtTextSize(PreviewLeastHeight));
        band.AddSideFill(_lstSlides, _ => _lstSlides.ItemHeight * ListLeastRows + 4, gapBelow: 0);

        var slidesCard = new Section("Slides (shown in this order)");

        slidesCard.AddFill(band, () => band.LeastHeight(Math.Max(1, band.Width)));
        slidesCard.Add(new ButtonBar().With(btnAdd, btnAddVideo, btnRemove, btnMoveUp, btnMoveDown), gapBelow: 0);

        // --- Timing ---

        _numLeaderboardSeconds.Minimum = 5;
        _numLeaderboardSeconds.Maximum = 600;
        _numLeaderboardSeconds.Value = Math.Clamp(_store.Current.LeaderboardSeconds, 5, 600);
        _numLeaderboardSeconds.ValueChanged += (_, _) =>
        {
            _store.Current.LeaderboardSeconds = (int)_numLeaderboardSeconds.Value;
            _store.Save();
        };

        _numSlideSeconds.Minimum = 2;
        _numSlideSeconds.Maximum = 120;
        _numSlideSeconds.Value = Math.Clamp(_store.Current.SlideSeconds, 2, 120);
        _numSlideSeconds.ValueChanged += (_, _) =>
        {
            _store.Current.SlideSeconds = (int)_numSlideSeconds.Value;
            _store.Save();
        };

        _chkPauseDuringRace.Text = "Never interrupt the leaderboard while a race is actually running (recommended)";
        _chkPauseDuringRace.AutoSize = true;
        _chkPauseDuringRace.Checked = _store.Current.PauseDuringRace;
        _chkPauseDuringRace.CheckedChanged += (_, _) =>
        {
            _store.Current.PauseDuringRace = _chkPauseDuringRace.Checked;
            _store.Save();
        };

        var btnTimingDefaults = new Button { Text = "Restore Defaults" };
        btnTimingDefaults.Click += (_, _) =>
        {
            // The three values as they were, for undo - put back on the
            // store, and the page follows through OnStoreRestored.
            var current = _store.Current;
            var leaderboardBefore = current.LeaderboardSeconds;
            var slideBefore = current.SlideSeconds;
            var pauseBefore = current.PauseDuringRace;

            if (leaderboardBefore != 60 || slideBefore != 8 || !pauseBefore)
            {
                var store = _store;
                _undo.Capture("Restore sponsor timing defaults", () =>
                {
                    store.Current.LeaderboardSeconds = leaderboardBefore;
                    store.Current.SlideSeconds = slideBefore;
                    store.Current.PauseDuringRace = pauseBefore;
                    store.Save();
                    store.NotifyRestored();
                });
            }

            // Matches the values a fresh install starts with.
            _numLeaderboardSeconds.Value = 60;
            _numSlideSeconds.Value = 8;
            _chkPauseDuringRace.Checked = true;
        };

        // The two numbers one under the other, their captions in a column of
        // their own. They used to share one row, each placed just after the
        // one before - four controls in a line that ran past its box at a
        // larger text size.
        var timing = new FieldGrid();

        timing.AddRow("Leaderboard shows for (sec):", _numLeaderboardSeconds);
        timing.AddRow("Each slide shows for (sec):", _numSlideSeconds);

        var timingCard = new Section("Rotation timing");

        timingCard.Add(timing);
        timingCard.Add(_chkPauseDuringRace, stretch: false);
        timingCard.Add(new ButtonBar().With(btnTimingDefaults), gapBelow: 0);

        // --- Video sound ---

        _chkPlayVideoSound.Text = "Play a sponsor video's own sound";
        _chkPlayVideoSound.AutoSize = true;
        _chkPlayVideoSound.Checked = _store.Current.PlayVideoSound;
        _chkPlayVideoSound.CheckedChanged += (_, _) =>
        {
            _store.Current.PlayVideoSound = _chkPlayVideoSound.Checked;
            _store.Save();
            SyncVideoSoundEnabled();
        };

        _chkMuteMusicDuringVideo.Text = "Silence the music and the crowd while it plays (recommended)";
        _chkMuteMusicDuringVideo.AutoSize = true;
        _chkMuteMusicDuringVideo.Checked = _store.Current.MuteMusicDuringVideo;
        _chkMuteMusicDuringVideo.CheckedChanged += (_, _) =>
        {
            _store.Current.MuteMusicDuringVideo = _chkMuteMusicDuringVideo.Checked;
            _store.Save();
        };

        var lblVideoSound = new Label
        {
            Text = "A sponsor who paid for a spot has been promised nobody talks over it, so the music and the crowd bed fade out "
                   + "while a video with sound is on screen and fade back when it ends. The announcer still speaks - a race call "
                   + "always wins.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        var videoCard = new Section("Video sound");

        videoCard.Add(lblVideoSound, gapBelow: UiLayout.RowGap);
        videoCard.Add(_chkPlayVideoSound, stretch: false);
        videoCard.Add(_chkMuteMusicDuringVideo, stretch: false, gapBelow: 0);

        SyncVideoSoundEnabled();

        // The number boxes at the text size in use - see SizeFields.
        _textSize.Keep(SizeFields);

        // --- The page ---

        var page = new FillPage();

        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.Add(_chkEnabled, stretch: false);
        page.AddFill(slidesCard, slidesCard.LeastHeight);
        page.Add(timingCard);
        page.Add(videoCard, gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>
    /// The number boxes: as wide as three digits and their arrows need in the
    /// page's face, and never narrower than a number box at this text size.
    /// Only when it changed - a new width lays the grid out again.
    /// </summary>
    private void SizeFields()
    {
        var number = Math.Max(
            UiLayout.AtTextSize(UiLayout.NumericWidth),
            TextRenderer.MeasureText("000", Font).Width + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra);

        foreach (var spinner in new[] { _numLeaderboardSeconds, _numSlideSeconds })
        {
            if (spinner.Width != number)
            {
                spinner.Width = number;
            }
        }
    }

    /// <summary>
    /// The slides' column: the width the list was at the normal text size, at
    /// the size in use - and never more than a little under half the card,
    /// so the preview beside it keeps the larger share when the window is
    /// narrow or the text large, nor less than a file name's worth.
    /// </summary>
    private int ListColumnWidth()
    {
        var wanted = UiLayout.AtTextSize(ListWidth);
        var room = _slidesBand?.Width ?? 0;

        return room > 0
            ? Math.Min(wanted, Math.Max(UiLayout.AtTextSize(ListLeastWidth), room * 45 / 100))
            : wanted;
    }

    private void RefreshSlides()
    {
        var previous = _lstSlides.SelectedItem as string;

        _lstSlides.Items.Clear();
        foreach (var name in _store.SlideFileNames)
        {
            _lstSlides.Items.Add(name);
        }

        if (previous is not null && _lstSlides.Items.Contains(previous))
        {
            _lstSlides.SelectedItem = previous;
        }
        else if (_lstSlides.Items.Count > 0)
        {
            _lstSlides.SelectedIndex = 0;
        }
        else
        {
            RefreshPreview();
        }
    }

    private void RefreshPreview()
    {
        _picPreview.Image?.Dispose();
        _picPreview.Image = null;
        _lblVideoNote.Visible = false;

        if (_lstSlides.SelectedItem is not string fileName)
        {
            return;
        }

        var path = _store.ResolvePath(fileName);
        if (path is null)
        {
            return;
        }

        // A video isn't decoded into the preview. Showing a still from it
        // would mean starting LibVLC just to draw this page, every time the
        // selection moved - so the preview says what it is and how long it
        // will hold the screen, which is what an operator picking slide
        // order actually needs to know.
        if (SponsorMedia.IsVideoName(fileName))
        {
            var size = TryFileLength(path);

            _lblVideoNote.Text = "Video\n\n" + Path.GetFileName(fileName)
                                 + (size is { } bytes ? $"\n{bytes / (1024 * 1024)} MB" : string.Empty)
                                 + "\n\nIt plays to its own end on the Big Screen.";
            _lblVideoNote.Visible = true;
            return;
        }

        try
        {
            // Only a slide no bigger than Add Image... takes in, sized from
            // its header before anything is decoded (see
            // SponsorStore.ReadSlide). This used to decode whatever the file
            // held, every time the slide was picked in the list. Anything
            // else leaves the preview blank, as an unreadable image does.
            if (SponsorStore.ReadSlide(path) is not { } bytes)
            {
                return;
            }

            // Copied into a Bitmap of its own before the stream closes - an
            // Image from FromStream keeps reading its stream for life, and
            // this one's is disposed at the end of the block. Orientation is
            // fixed first, as the copy doesn't carry the EXIF tag.
            using var stream = new MemoryStream(bytes);
            using var decoded = Image.FromStream(stream);
            TrackAttachmentStore.ApplyExifOrientation(decoded);
            _picPreview.Image = new Bitmap(decoded);
        }
        catch
        {
            // Unreadable image - just leave the preview blank.
        }
    }

    private void AddSlide()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Add a sponsor slide",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*",
            Multiselect = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var failures = new List<string>();
        foreach (var file in dialog.FileNames)
        {
            if (_store.AddSlide(file, out var error) is null)
            {
                failures.Add($"{Path.GetFileName(file)}: {error}");
            }
        }

        if (failures.Count > 0)
        {
            // Each file's own reason, now that a picture can be turned away
            // for its size as well as fail to copy - this used to give only
            // the count. The first few, so a big selection can't grow the
            // message past the screen.
            const int listed = 3;

            var more = failures.Count > listed
                ? $"\n\n... and {Plural.Of(failures.Count - listed, "more file")}."
                : string.Empty;

            AppDialog.Show(this,
                $"Couldn't add {Plural.Of(failures.Count, "file")} you selected.\n\n{string.Join("\n\n", failures.Take(listed))}{more}",
                "Some files failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        RefreshSlides();
    }

    /// <summary>
    /// Takes a video into the rotation. Multiselect, and one reason per
    /// file that failed, exactly as AddSlide does it - the two differ only
    /// in what they accept and in that a video is copied rather than read
    /// (see SponsorStore.AddVideo).
    /// </summary>
    private void AddVideo()
    {
        // Said before the file dialog rather than after: a club picking a
        // video on a build that can't play one should hear so before it
        // spends a minute copying 200 MB into %AppData%.
        if (!SponsorVideoEngine.NativesPresent)
        {
            AppDialog.Show(this,
                "This copy of HeatCaller can't play videos - the video player's files are missing from beside HeatCaller.exe. "
                + "Reinstall HeatCaller, and sponsor images will keep working in the meantime.",
                "Video unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "Add a sponsor video",
            Filter = SponsorMedia.VideoFilter,
            Multiselect = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var failures = new List<string>();
        foreach (var file in dialog.FileNames)
        {
            if (_store.AddVideo(file, out var error) is null)
            {
                failures.Add($"{Path.GetFileName(file)}: {error}");
            }
        }

        if (failures.Count > 0)
        {
            const int listed = 3;

            var more = failures.Count > listed
                ? $"\n\n... and {Plural.Of(failures.Count - listed, "more file")}."
                : string.Empty;

            AppDialog.Show(this,
                $"Couldn't add {Plural.Of(failures.Count, "file")} you selected.\n\n{string.Join("\n\n", failures.Take(listed))}{more}",
                "Some files failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        RefreshSlides();
    }

    /// <summary>A stored slide's size on disk, or null when it can't be read - the preview says nothing about size rather than failing to draw.</summary>
    private static long? TryFileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Greys the music switch out while video sound is off: with no sound
    /// to talk over there is nothing to silence, and a live-looking tick
    /// that does nothing is worse than one plainly turned off.
    /// </summary>
    private void SyncVideoSoundEnabled() =>
        _chkMuteMusicDuringVideo.Enabled = _chkPlayVideoSound.Checked;

    private void RemoveSelected()
    {
        if (_lstSlides.SelectedItem is not string fileName)
        {
            return;
        }

        if (AppDialog.Show(this, "Remove this slide from the rotation?", "Confirm remove", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        // Release the preview's file handle first - otherwise the move
        // inside RemoveSlide can silently fail on a file we're displaying.
        _picPreview.Image?.Dispose();
        _picPreview.Image = null;

        // Captured BEFORE the removal, with the place the slide holds now, so
        // undo puts it back there rather than on the end. The image is moved
        // to the undo holding folder by RemoveSlide; where it went is filled
        // in below, before the undo can ever run.
        var index = _store.SlideFileNames.ToList().IndexOf(fileName);
        string? held = null;
        var store = _store;
        _undo.Capture($"Remove sponsor slide {fileName}", () =>
        {
            store.RestoreSlide(fileName, index, held);
            store.NotifyRestored();
        });

        held = _store.RemoveSlide(fileName);

        RefreshSlides();
    }

    private void MoveSelected(int direction)
    {
        if (_lstSlides.SelectedItem is not string fileName)
        {
            return;
        }

        // A move changes the order the rotation shows them in, so it can be
        // undone too - by moving the same slide back the other way, which
        // leaves any other reordering since alone.
        var from = _store.SlideFileNames.ToList().IndexOf(fileName);
        var to = from + direction;
        if (from >= 0 && to >= 0 && to < _store.SlideFileNames.Count)
        {
            var store = _store;
            _undo.Capture(direction < 0 ? $"Move sponsor slide {fileName} up" : $"Move sponsor slide {fileName} down", () =>
            {
                store.Move(fileName, -direction);
                store.NotifyRestored();
            });
        }

        _store.Move(fileName, direction);
        RefreshSlides();
        _lstSlides.SelectedItem = fileName;
    }
}
