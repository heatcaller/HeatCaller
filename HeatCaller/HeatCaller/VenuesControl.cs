namespace HeatCaller;

/// <summary>
/// Manage named venue profiles and apply one with a single click - sets
/// the decoder, selects the preferred track layout,
/// applies the preferred theme, and pushes race-control defaults (lap
/// limit, time limit, min lap time, streak tolerance) all at once.
/// </summary>
public sealed class VenuesControl : UserControl
{
    private readonly VenueStore _venues;
    private readonly UndoService _undo;
    private readonly TrackLayoutStore _trackLayouts;
    private readonly ThemeStore _themes;

    private readonly ListBox _list = new();
    private readonly GroupBox _grpEdit = new();
    private readonly TextBox _txtName = new();
    private readonly ComboBox _cmbConnectionType = new();
    private readonly ComboBox _cmbTrackLayout = new();
    private readonly ComboBox _cmbTheme = new();
    private readonly NumericUpDown _numLapLimit = new();
    private readonly NumericUpDown _numTimeLimitMinutes = new();
    private readonly NumericUpDown _numMinLapTimeMs = new();
    private readonly NumericUpDown _numStreakSlowMultiplier = new();
    private readonly Label _lblStatus = new();

    private bool _suppressEvents;

    /// <summary>Raised when "Apply This Venue" is clicked, so the host (MainForm) can push the settings into the Connection page, apply the theme, and update Race Control's defaults.</summary>
    public event Action<VenueProfile>? VenueApplied;

    public VenuesControl(VenueStore venues, TrackLayoutStore trackLayouts, ThemeStore themes, UndoService undo)
    {
        _venues = venues;
        _undo = undo;
        _trackLayouts = trackLayouts;
        _themes = themes;

        Dock = DockStyle.Fill;
        Font = AppTheme.BodyFont;

        BuildLayout();
        RefreshDropdownSources();
        RefreshList();
    }

    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Save everything specific to one physical track - its decoder, preferred track layout, announcer style, and race defaults - " +
                   "and apply them all together with one click instead of reconfiguring each piece by hand.",
            Location = new Point(12, 12),
            AutoSize = true,
            MaximumSize = new Size(800, 0),
            ForeColor = AppTheme.TextMuted,
        };

        var lblListCaption = new Label { Text = "Venues:", Location = new Point(12, 60), AutoSize = true, Font = AppTheme.BoldFont };

        _list.Location = new Point(12, 82);
        _list.Size = new Size(220, 360);
        _list.Font = AppTheme.BodyFont;
        _list.SelectedIndexChanged += (_, _) => LoadSelectedIntoUi();

        var btnNew = new Button { Text = "New Venue" };
        btnNew.Click += BtnNew_Click;

        var btnDelete = new Button { Text = "Delete" };
        btnDelete.Click += BtnDelete_Click;

        // Under the list, measured - not at a remembered 450 that assumed
        // the list was exactly 360 tall.
        UiLayout.TightButtonRow(12, UiLayout.Below(_list, UiLayout.RowGap), btnNew, btnDelete);

        _grpEdit.Text = "Venue Settings";
        _grpEdit.Location = new Point(248, 60);
        _grpEdit.Size = new Size(520, 440);

        var lblName = new Label { Text = "Name:", AutoSize = true };
        _txtName.Leave += (_, _) => { if (!_suppressEvents) SaveCurrentFromUi(); };

        var lblConnection = new Label { Text = "Connect via:", AutoSize = true };
        _cmbConnectionType.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbConnectionType.Items.AddRange(DecoderConnection.Items());
        _cmbConnectionType.SelectedIndexChanged += (_, _) => { if (!_suppressEvents) SaveCurrentFromUi(); };

        var lblTrack = new Label { Text = "Preferred track:", AutoSize = true };
        _cmbTrackLayout.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTrackLayout.SelectedIndexChanged += (_, _) => { if (!_suppressEvents) SaveCurrentFromUi(); };

        var lblTheme = new Label { Text = "Preferred soundscape:", AutoSize = true };
        _cmbTheme.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTheme.SelectedIndexChanged += (_, _) => { if (!_suppressEvents) SaveCurrentFromUi(); };

        var lblDefaultsCaption = new Label { Text = "Race defaults for this venue:", AutoSize = true, Font = AppTheme.BoldFont };

        var lblLapLimit = new Label { Text = "Lap limit (0 = none):", AutoSize = true };
        _numLapLimit.Width = 70;
        _numLapLimit.Maximum = 9999;
        _numLapLimit.ValueChanged += (_, _) => { if (!_suppressEvents) SaveCurrentFromUi(); };

        var lblTimeLimit = new Label { Text = "Time limit, min (0 = none):", AutoSize = true };
        _numTimeLimitMinutes.Width = 70;
        _numTimeLimitMinutes.Maximum = 180;
        _numTimeLimitMinutes.ValueChanged += (_, _) => { if (!_suppressEvents) SaveCurrentFromUi(); };

        var lblMinLap = new Label { Text = "Minimum lap time (ms):", AutoSize = true };
        _numMinLapTimeMs.Width = 80;
        _numMinLapTimeMs.Maximum = 30000;
        _numMinLapTimeMs.Increment = 100;
        _numMinLapTimeMs.Value = 3000;
        _numMinLapTimeMs.ValueChanged += (_, _) => { if (!_suppressEvents) SaveCurrentFromUi(); };

        var lblStreakMult = new Label { Text = "Streak slow-lap multiplier:", AutoSize = true };
        _numStreakSlowMultiplier.Width = 80;
        _numStreakSlowMultiplier.DecimalPlaces = 2;
        _numStreakSlowMultiplier.Increment = 0.1m;
        _numStreakSlowMultiplier.Minimum = 1.0m;
        _numStreakSlowMultiplier.Maximum = 5.0m;
        _numStreakSlowMultiplier.Value = 1.5m;
        _numStreakSlowMultiplier.ValueChanged += (_, _) => { if (!_suppressEvents) SaveCurrentFromUi(); };

        var btnApply = new Button { Text = "Apply This Venue" };
        btnApply.Click += BtnApply_Click;

        // --- TWO measured field columns, not one ---
        //
        // One column for the whole card was the obvious thing to do and it
        // was wrong: "Streak slow-lap multiplier:" is more than three times
        // the width of "Name:", so aligning everything to the widest
        // caption left the short rows with a gutter you could park a bus
        // in, and pushed the card out to match.
        //
        // The card has two groups of rows that do not need to line up with
        // each other - the venue's own details and the race defaults - so
        // each gets its own column, measured against its own captions.
        var topCaptions = new[] { lblName, lblConnection, lblTrack, lblTheme };
        var defaultCaptions = new[] { lblLapLimit, lblTimeLimit, lblMinLap, lblStreakMult };

        foreach (var caption in topCaptions.Concat(defaultCaptions).Append(lblDefaultsCaption))
        {
            caption.Font = caption == lblDefaultsCaption ? AppTheme.BoldFont : AppTheme.BodyFont;
        }

        // Measured against the font they are drawn in. None of these has
        // been parented yet, so PreferredWidth would report a size they
        // are never drawn at.
        int CaptionWidth(Label caption) =>
            TextRenderer.MeasureText(caption.Text, caption.Font).Width;

        var topLeft = 12 + UiLayout.LabelGap + topCaptions.Max(CaptionWidth);
        var defaultsLeft = 12 + UiLayout.LabelGap + defaultCaptions.Max(CaptionWidth);

        // Wide enough for its digits and its spinner, rather than a
        // remembered 70 that the repair pass then had to stretch.
        var numericWidth = TextRenderer.MeasureText("000000", AppTheme.BodyFont).Width + 34;

        foreach (var spinner in new NumericUpDown[] { _numLapLimit, _numTimeLimitMinutes, _numMinLapTimeMs, _numStreakSlowMultiplier })
        {
            spinner.Font = AppTheme.BodyFont;
            spinner.Width = numericWidth;
        }

        // Capped, not stretched. A venue name is a few words; a 600px box
        // for it just looks like a mistake.
        const int fieldWidth = 280;

        _txtName.Font = AppTheme.BodyFont;
        _txtName.Width = fieldWidth;
        _cmbConnectionType.Font = AppTheme.BodyFont;
        _cmbConnectionType.Width = fieldWidth;
        _cmbTrackLayout.Font = AppTheme.BodyFont;
        _cmbTrackLayout.Width = fieldWidth;
        _cmbTheme.Font = AppTheme.BodyFont;
        _cmbTheme.Width = fieldWidth;

        var y = UiLayout.InsideGroup(_grpEdit);

        int Row(Label caption, Control field, int fieldLeft, int top)
        {
            caption.Location = new Point(12, UiLayout.CentreOn(caption, top, field.Height));
            field.Location = new Point(fieldLeft, top);

            return UiLayout.Below(field, UiLayout.RowGap);
        }

        y = Row(lblName, _txtName, topLeft, y);
        y = Row(lblConnection, _cmbConnectionType, topLeft, y);
        y = Row(lblTrack, _cmbTrackLayout, topLeft, y);
        y = Row(lblTheme, _cmbTheme, topLeft, y);

        y += UiLayout.RowGap;
        lblDefaultsCaption.Location = new Point(12, y);
        y = UiLayout.Below(lblDefaultsCaption, UiLayout.RowGap);

        y = Row(lblLapLimit, _numLapLimit, defaultsLeft, y);
        y = Row(lblTimeLimit, _numTimeLimitMinutes, defaultsLeft, y);
        y = Row(lblMinLap, _numMinLapTimeMs, defaultsLeft, y);
        y = Row(lblStreakMult, _numStreakSlowMultiplier, defaultsLeft, y);

        // Directly after the last row. It used to be pinned at y=380 in a
        // group 440 tall, which left sixty pixels of nothing under it.
        y += UiLayout.RowGap;

        btnApply.Size = new Size(UiLayout.WidthFor(btnApply), UiLayout.ButtonHeight);
        btnApply.Location = new Point(12, y);

        // As wide as the widest row actually needs, rather than a column
        // width plus a guess.
        _grpEdit.Width = Math.Max(topLeft + fieldWidth, defaultsLeft + numericWidth) + 12;

        _grpEdit.Height = UiLayout.Below(btnApply, 16);

        // Every child in here is placed by the code above, so the repair
        // pass keeps out - it was the thing stretching the spinners.
        LayoutDoctor.LeaveAlone(_grpEdit);

        _grpEdit.Controls.AddRange(new Control[]
        {
            lblName, _txtName, lblConnection, _cmbConnectionType,
            lblTrack, _cmbTrackLayout, lblTheme, _cmbTheme, lblDefaultsCaption,
            lblLapLimit, _numLapLimit, lblTimeLimit, _numTimeLimitMinutes,
            lblMinLap, _numMinLapTimeMs, lblStreakMult, _numStreakSlowMultiplier, btnApply,
        });

        _lblStatus.Location = new Point(248, UiLayout.Below(_grpEdit, UiLayout.RowGap));
        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = AppTheme.TextMuted;

        Controls.Add(lblIntro);
        Controls.Add(lblListCaption);
        Controls.Add(_list);
        Controls.Add(btnNew);
        Controls.Add(btnDelete);
        Controls.Add(_grpEdit);
        Controls.Add(_lblStatus);

        SetEditEnabled(false);
    }

    /// <summary>Call whenever this tab becomes visible - track layouts and themes can change elsewhere while it's not active.</summary>
    public void RefreshDropdownSources()
    {
        // Refilling the lists changes their selection; that must not be
        // taken for the user picking something and saved over the venue.
        var wasSuppressed = _suppressEvents;
        _suppressEvents = true;
        try
        {
            RefillDropdowns();

            // And the list itself, when the saved venues no longer match it -
            // an undo (a deleted venue back) changes the store behind this
            // page, and this is what the host calls afterwards. Only then, so
            // an ordinary visit doesn't reload the venue being edited.
            var shown = _list.Items.OfType<VenueListItem>().Select(i => i.Venue.Id).ToList();
            var saved = _venues.All.Select(v => v.Id).ToList();
            if (!shown.SequenceEqual(saved))
            {
                RefreshList(SelectedVenue()?.Id);
            }
        }
        finally
        {
            _suppressEvents = wasSuppressed;
        }
    }

    private void RefillDropdowns()
    {
        var previousTrack = _cmbTrackLayout.SelectedItem as string;
        _cmbTrackLayout.Items.Clear();
        _cmbTrackLayout.Items.Add("(none)");
        foreach (var t in _trackLayouts.All)
        {
            _cmbTrackLayout.Items.Add(t.Name);
        }
        if (previousTrack is not null && _cmbTrackLayout.Items.Contains(previousTrack))
        {
            _cmbTrackLayout.SelectedItem = previousTrack;
        }

        var previousTheme = _cmbTheme.SelectedItem as string;
        _cmbTheme.Items.Clear();
        _cmbTheme.Items.Add("(none)");
        foreach (var t in _themes.All)
        {
            _cmbTheme.Items.Add(t.Name);
        }
        if (previousTheme is not null && _cmbTheme.Items.Contains(previousTheme))
        {
            _cmbTheme.SelectedItem = previousTheme;
        }

        // The venue on show goes back to what it has saved - including a
        // "(missing: ...)" entry, which the refill above threw away.
        var venue = SelectedVenue();
        if (venue is not null)
        {
            SelectStoredName(_cmbTrackLayout, venue.PreferredTrackLayoutName);
            SelectStoredName(_cmbTheme, ThemeNameToShow(venue.PreferredThemeName));
        }
    }

    /// <summary>A built-in renamed since the venue was saved shows under its new name.</summary>
    private static string ThemeNameToShow(string? themeName)
    {
        if (!string.IsNullOrWhiteSpace(themeName) && Theme.RenamedBuiltIns.TryGetValue(themeName, out var renamedTheme))
        {
            return renamedTheme;
        }

        return themeName ?? string.Empty;
    }

    /// <summary>
    /// Selects a venue's saved name in one of the pickers. A name that isn't
    /// in the list any more (a track layout or soundscape deleted or renamed
    /// since) gets a "(missing: X)" entry of its own - setting SelectedItem
    /// to something not in the list does nothing at all, which left the
    /// PREVIOUS venue's choice showing, and the next save wrote it into
    /// this venue.
    /// </summary>
    private static void SelectStoredName(ComboBox combo, string? storedName)
    {
        for (var i = combo.Items.Count - 1; i >= 0; i--)
        {
            if (combo.Items[i] is MissingName)
            {
                combo.Items.RemoveAt(i);
            }
        }

        if (string.IsNullOrWhiteSpace(storedName))
        {
            combo.SelectedItem = "(none)";
        }
        else if (combo.Items.Contains(storedName))
        {
            combo.SelectedItem = storedName;
        }
        else
        {
            var missing = new MissingName(storedName);
            combo.Items.Add(missing);
            combo.SelectedItem = missing;
        }
    }

    /// <summary>
    /// What to save from a picker: the choice, "" for "(none)", or - while it
    /// still shows the "(missing: X)" entry or nothing - the name the venue
    /// already had, so opening a venue never quietly changes it.
    /// </summary>
    private static string NameToSave(ComboBox combo, string existing) => combo.SelectedItem switch
    {
        MissingName or null => existing,
        string name when combo.SelectedIndex > 0 => name,
        _ => string.Empty,
    };

    private VenueProfile? SelectedVenue() => (_list.SelectedItem as VenueListItem)?.Venue;

    private void RefreshList(string? selectId = null)
    {
        _list.Items.Clear();
        foreach (var venue in _venues.All)
        {
            _list.Items.Add(new VenueListItem(venue));
        }

        if (selectId is not null)
        {
            for (var i = 0; i < _list.Items.Count; i++)
            {
                if (((VenueListItem)_list.Items[i]).Venue.Id == selectId)
                {
                    _list.SelectedIndex = i;
                    return;
                }
            }
        }

        if (_list.Items.Count > 0 && _list.SelectedIndex < 0)
        {
            _list.SelectedIndex = 0;
        }
        else if (_list.Items.Count == 0)
        {
            SetEditEnabled(false);
        }
    }

    private void SetEditEnabled(bool enabled)
    {
        // Control.Enabled = false triggers WinForms' native "disabled"
        // rendering, which uses hardcoded OS theme colors and completely
        // ignores any custom BackColor/ForeColor - no matter what the rest
        // of the app's theme says, a disabled TextBox/NumericUpDown/
        // ComboBox always renders with the OS's own grey/white palette.
        // ReadOnly achieves the same "can't edit this yet" behavior for
        // TextBox and NumericUpDown while still respecting the theme's
        // colors. ComboBox has no equivalent - it's left interactive even
        // with nothing selected, which is a minor UX loosening but far
        // less jarring than a bright white box in an otherwise dark app.
        // Buttons are unaffected here; their disabled state is already
        // themed correctly (see AppTheme.StyleButton's EnabledChanged
        // handler), so toggling Enabled on them is fine as-is.
        foreach (Control c in _grpEdit.Controls)
        {
            switch (c)
            {
                case TextBox textBox:
                    textBox.ReadOnly = !enabled;
                    break;
                case NumericUpDown numericUpDown:
                    numericUpDown.ReadOnly = !enabled;
                    break;
                case Button button:
                    button.Enabled = enabled;
                    break;
            }
        }
    }

    private void LoadSelectedIntoUi()
    {
        var venue = SelectedVenue();
        if (venue is null)
        {
            SetEditEnabled(false);
            return;
        }

        _suppressEvents = true;
        try
        {
            SetEditEnabled(true);

            _txtName.Text = venue.Name;
            _cmbConnectionType.SelectedIndex = DecoderConnection.IndexOf(venue.ConnectionType);
            SelectStoredName(_cmbTrackLayout, venue.PreferredTrackLayoutName);
            SelectStoredName(_cmbTheme, ThemeNameToShow(venue.PreferredThemeName));
            _numLapLimit.Value = Math.Clamp(venue.RaceLapLimit, 0, (int)_numLapLimit.Maximum);
            _numTimeLimitMinutes.Value = Math.Clamp(venue.RaceTimeLimitMinutes, 0, (int)_numTimeLimitMinutes.Maximum);
            _numMinLapTimeMs.Value = Math.Clamp(venue.RaceMinLapTimeMs, 0, (int)_numMinLapTimeMs.Maximum);
            _numStreakSlowMultiplier.Value = (decimal)Math.Clamp(venue.RaceStreakSlowMultiplier, (double)_numStreakSlowMultiplier.Minimum, (double)_numStreakSlowMultiplier.Maximum);
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void SaveCurrentFromUi()
    {
        var venue = SelectedVenue();
        if (venue is null)
        {
            return;
        }

        venue.Name = _txtName.Text.Trim();
        // The saved number, not the picker position - see DecoderConnection.
        // Host and Port are left as they were: they were Next Level
        // Timing's address, and nothing reads them now.
        venue.ConnectionType = DecoderConnection.ValueAt(_cmbConnectionType.SelectedIndex);
        venue.PreferredTrackLayoutName = NameToSave(_cmbTrackLayout, venue.PreferredTrackLayoutName);
        venue.PreferredThemeName = NameToSave(_cmbTheme, venue.PreferredThemeName);
        venue.RaceLapLimit = (int)_numLapLimit.Value;
        venue.RaceTimeLimitMinutes = (int)_numTimeLimitMinutes.Value;
        venue.RaceMinLapTimeMs = (int)_numMinLapTimeMs.Value;
        venue.RaceStreakSlowMultiplier = (double)_numStreakSlowMultiplier.Value;

        _venues.Upsert(venue);

        var index = _list.SelectedIndex;
        if (index >= 0)
        {
            _list.Items[index] = new VenueListItem(venue);
            _list.SelectedIndex = index;
        }

        _lblStatus.Text = "Saved.";
    }

    private void BtnNew_Click(object? sender, EventArgs e)
    {
        var venue = new VenueProfile { Name = "New Venue" };
        _venues.Upsert(venue);
        RefreshList(venue.Id);
    }

    private void BtnDelete_Click(object? sender, EventArgs e)
    {
        var venue = SelectedVenue();
        if (venue is null)
        {
            AppDialog.Show(this, "Select a venue first.", "No venue selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (AppDialog.Show(this, $"Delete venue \"{venue.Name}\"?", "Confirm delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        // A copy, put back by id (so never listed twice). Store only: the
        // host refreshes this page straight after an undo, and
        // RefreshDropdownSources picks the venue back up into the list.
        var removed = System.Text.Json.JsonSerializer.Deserialize<VenueProfile>(System.Text.Json.JsonSerializer.Serialize(venue))!;
        var venues = _venues;
        _undo.Capture($"Delete venue '{venue.Name}'", () =>
        {
            if (venues.Get(removed.Id) is null)
            {
                venues.Upsert(removed);
            }
        });

        _venues.Delete(venue.Id);
        RefreshList();
    }

    private void BtnApply_Click(object? sender, EventArgs e)
    {
        var venue = SelectedVenue();
        if (venue is null)
        {
            AppDialog.Show(this, "Select a venue first.", "No venue selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = AppDialog.Show(this,
            $"Apply \"{venue.Name}\"? This sets its decoder and selects its preferred track on the Connection page, applies its preferred announcer style, and updates the race defaults on Race Control.",
            "Confirm apply venue", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        VenueApplied?.Invoke(venue);
        _lblStatus.Text = $"Applied \"{venue.Name}\".";
    }

    /// <summary>A saved name that isn't in a picker's list - see SelectStoredName.</summary>
    private sealed record MissingName(string Name)
    {
        public override string ToString() => $"(missing: {Name})";
    }

    private sealed record VenueListItem(VenueProfile Venue)
    {
        public override string ToString() => Venue.Name;
    }
}
