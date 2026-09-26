namespace HeatCaller;

/// <summary>Manage sponsor/promo slides shown in rotation on the Big Screen, and how long each stays up.</summary>
public sealed class SponsorsControl : UserControl
{
    private readonly SponsorStore _store;
    private readonly UndoService _undo;

    private readonly CheckBox _chkEnabled = new();
    private readonly ListBox _lstSlides = new();
    private readonly PictureBox _picPreview = new();
    private readonly NumericUpDown _numLeaderboardSeconds = new();
    private readonly NumericUpDown _numSlideSeconds = new();
    private readonly CheckBox _chkPauseDuringRace = new();

    public SponsorsControl(SponsorStore store, UndoService undo)
    {
        _store = store;
        _undo = undo;

        Dock = DockStyle.Fill;
        MinimumSize = new Size(880, 560);
        Font = AppTheme.BodyFont;

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

        RefreshSlides();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _store.Restored -= OnStoreRestored;
        }

        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Show sponsor or promo images in rotation on the Big Screen between leaderboard views. Leave this off entirely " +
                   "for a home setup - it's meant for a shop or club venue with sponsors to display.",
            Location = new Point(12, 12),
            AutoSize = true,
            MaximumSize = new Size(840, 0),
            ForeColor = AppTheme.TextMuted,
        };

        _chkEnabled.Text = "Show sponsor slides on the Big Screen";
        _chkEnabled.Location = new Point(12, 56);
        _chkEnabled.AutoSize = true;
        _chkEnabled.Checked = _store.Current.Enabled;
        _chkEnabled.CheckedChanged += (_, _) =>
        {
            _store.Current.Enabled = _chkEnabled.Checked;
            _store.Save();
        };

        var lblSlides = new Label { Text = "Slides (shown in this order):", Location = new Point(12, 92), AutoSize = true, Font = AppTheme.BoldFont };

        _lstSlides.Location = new Point(12, 116);
        _lstSlides.Size = new Size(320, 280);
        _lstSlides.SelectedIndexChanged += (_, _) => RefreshPreview();

        _picPreview.Location = new Point(348, 116);
        _picPreview.Size = new Size(360, 280);
        _picPreview.BorderStyle = BorderStyle.FixedSingle;
        _picPreview.SizeMode = PictureBoxSizeMode.Zoom;
        _picPreview.BackColor = AppTheme.Surface;

        // Sized to their own captions rather than to 120 / 90 / 90 / 100,
        // which were measured against a smaller font and left "Move Down"
        // two pixels short of its own text.
        var btnAdd = new Button { Text = "Add Image...", Location = new Point(12, 406) };
        btnAdd.Click += (_, _) => AddSlide();

        var btnRemove = new Button { Text = "Remove", Location = new Point(0, 406) };
        btnRemove.Click += (_, _) => RemoveSelected();

        var btnMoveUp = new Button { Text = "Move Up", Location = new Point(0, 406) };
        btnMoveUp.Click += (_, _) => MoveSelected(-1);

        var btnMoveDown = new Button { Text = "Move Down", Location = new Point(0, 406) };
        btnMoveDown.Click += (_, _) => MoveSelected(1);

        foreach (var button in new[] { btnAdd, btnRemove, btnMoveUp, btnMoveDown })
        {
            button.Width = UiLayout.WidthFor(button);
        }

        btnRemove.Left = btnAdd.Right + UiLayout.ButtonGap;
        btnMoveUp.Left = btnRemove.Right + UiLayout.ButtonGap;
        btnMoveDown.Left = btnMoveUp.Right + UiLayout.ButtonGap;

        // --- Timing ---
        var grpTiming = new GroupBox { Text = "Rotation Timing", Location = new Point(12, 448), Size = new Size(700, 132) };

        // Measured across, not stepped 196 / 300 / 476. Those numbers were
        // spaced for a shorter font: at the current one the first caption
        // runs past 196, so the spinner sat inside it, the second caption
        // inside the spinner and the second spinner inside that caption -
        // four shunts every startup, in a row only four controls long.
        var lblLeaderboardSecs = new Label
        {
            Text = "Leaderboard shows for (sec):",
            Location = new Point(12, 28),
            AutoSize = true,
            Font = AppTheme.BodyFont,
        };

        _numLeaderboardSeconds.Width = 70;
        _numLeaderboardSeconds.Location = new Point(UiLayout.After(lblLeaderboardSecs), 25);
        _numLeaderboardSeconds.Minimum = 5;
        _numLeaderboardSeconds.Maximum = 600;
        _numLeaderboardSeconds.Value = Math.Clamp(_store.Current.LeaderboardSeconds, 5, 600);
        _numLeaderboardSeconds.ValueChanged += (_, _) =>
        {
            _store.Current.LeaderboardSeconds = (int)_numLeaderboardSeconds.Value;
            _store.Save();
        };

        var lblSlideSecs = new Label
        {
            Text = "Each slide shows for (sec):",
            Location = new Point(_numLeaderboardSeconds.Right + UiLayout.SectionGap, 28),
            AutoSize = true,
            Font = AppTheme.BodyFont,
        };

        _numSlideSeconds.Width = 70;
        _numSlideSeconds.Location = new Point(UiLayout.After(lblSlideSecs), 25);
        _numSlideSeconds.Minimum = 2;
        _numSlideSeconds.Maximum = 120;
        _numSlideSeconds.Value = Math.Clamp(_store.Current.SlideSeconds, 2, 120);
        _numSlideSeconds.ValueChanged += (_, _) =>
        {
            _store.Current.SlideSeconds = (int)_numSlideSeconds.Value;
            _store.Save();
        };

        _chkPauseDuringRace.Text = "Never interrupt the leaderboard while a race is actually running (recommended)";
        _chkPauseDuringRace.Location = new Point(12, 60);
        _chkPauseDuringRace.AutoSize = true;
        _chkPauseDuringRace.Checked = _store.Current.PauseDuringRace;
        _chkPauseDuringRace.CheckedChanged += (_, _) =>
        {
            _store.Current.PauseDuringRace = _chkPauseDuringRace.Checked;
            _store.Save();
        };

                // Its own row under the checkbox, left-aligned. It used to share the
        // checkbox's row at x=566, and that checkbox's caption auto-sizes
        // well past there at this font, so it sat in front of the button and
        // clipped it to "Restore De".
        var btnTimingDefaults = new Button { Text = "Restore Defaults", Location = new Point(12, 92) };
        btnTimingDefaults.Size = new Size(UiLayout.WidthFor(btnTimingDefaults), UiLayout.ButtonHeight);
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

        // Wide and tall enough for whatever the measured rows inside it
        // came to, rather than the flat 700 x 132 they now run past.
        grpTiming.Width = Math.Max(
            grpTiming.Width,
            Math.Max(_numSlideSeconds.Right, btnTimingDefaults.Right) + UiLayout.PageLeft);

        grpTiming.Height = Math.Max(grpTiming.Height, UiLayout.GroupHeightFor(btnTimingDefaults.Bottom));

        grpTiming.Controls.AddRange(new Control[]
        {
            lblLeaderboardSecs, _numLeaderboardSeconds, lblSlideSecs, _numSlideSeconds, _chkPauseDuringRace, btnTimingDefaults,
        });

        Controls.Add(lblIntro);
        Controls.Add(_chkEnabled);
        Controls.Add(lblSlides);
        Controls.Add(_lstSlides);
        Controls.Add(_picPreview);
        Controls.Add(btnAdd);
        Controls.Add(btnRemove);
        Controls.Add(btnMoveUp);
        Controls.Add(btnMoveDown);
        Controls.Add(grpTiming);
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

        if (_lstSlides.SelectedItem is not string fileName)
        {
            return;
        }

        var path = _store.ResolvePath(fileName);
        if (path is null)
        {
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
