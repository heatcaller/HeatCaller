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
    private readonly TextBox _txtName = new();
    private readonly ComboBox _cmbConnectionType = new();
    private readonly ComboBox _cmbTrackLayout = new();
    private readonly ComboBox _cmbTheme = new();
    private readonly NumericUpDown _numLapLimit = new();
    private readonly NumericUpDown _numTimeLimitMinutes = new();
    private readonly NumericUpDown _numMinLapTimeMs = new();
    private readonly NumericUpDown _numStreakSlowMultiplier = new();
    private readonly Label _lblStatus = new();
    private readonly Button _btnApply = new();

    /// <summary>The venues' card and its buttons, which the column's width is measured from - see VenuesWidth.</summary>
    private Section? _venuesCard;
    private ButtonBar? _listButtons;

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The pickers' widths as last measured - see Pieces.PickerWidth. Forgotten when their lists are refilled.</summary>
    private (Font Font, int Count, int Width)? _connectionWidth;
    private (Font Font, int Count, int Width)? _trackWidth;
    private (Font Font, int Count, int Width)? _themeWidth;

    /// <summary>The venues' column and the settings' fields at the normal text size.</summary>
    private const int ListWidth = 240;
    private const int FieldWidth = 280;

    /// <summary>The fewest venues the list shows before the page scrolls instead.</summary>
    private const int ListLeastRows = 4;

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

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize.
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshDropdownSources();
        RefreshList();
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
    /// The intro, then the venues down the left in a card of their own, the
    /// list taking the page's height and its buttons under it - and beside
    /// them the selected venue's settings: its own details, then its race
    /// defaults, each group with a caption column of its own, and Apply
    /// This Venue under them. On the layout pieces (LayoutPieces.cs), so
    /// nothing is placed by hand.
    ///
    /// It used to be placed in pixels: the list 220x360 whatever the window,
    /// the settings in a group box sized once at build, and the fields 280px
    /// wide at every text size.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Save everything specific to one physical track - its decoder, preferred track layout, soundscape, and race defaults - " +
                   "and apply them all together with one click instead of reconfiguring each piece by hand.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        // --- The venues ---

        _list.IntegralHeight = false;
        _list.SelectedIndexChanged += (_, _) => LoadSelectedIntoUi();

        var btnNew = new Button { Text = "New Venue" };
        btnNew.Click += BtnNew_Click;

        var btnDelete = new Button { Text = "Delete" };
        btnDelete.Click += BtnDelete_Click;

        _listButtons = new ButtonBar(tight: true).With(btnNew, btnDelete);

        _venuesCard = new Section("Venues");

        _venuesCard.AddFill(_list, () => _list.ItemHeight * ListLeastRows + 4);
        _venuesCard.Add(_listButtons, gapBelow: 0);

        // --- The selected venue ---

        _txtName.Leave += (_, _) => { if (!_suppressEvents) SaveCurrentFromUi(); };

        _cmbConnectionType.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbConnectionType.Items.AddRange(DecoderConnection.Items());
        _cmbConnectionType.SelectedIndexChanged += (_, _) => { if (!_suppressEvents) SaveCurrentFromUi(); };

        _cmbTrackLayout.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTrackLayout.SelectedIndexChanged += (_, _) => { if (!_suppressEvents) SaveCurrentFromUi(); };

        _cmbTheme.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTheme.SelectedIndexChanged += (_, _) => { if (!_suppressEvents) SaveCurrentFromUi(); };

        _numLapLimit.Maximum = 9999;
        _numLapLimit.ValueChanged += (_, _) => { if (!_suppressEvents) SaveCurrentFromUi(); };

        _numTimeLimitMinutes.Maximum = 180;
        _numTimeLimitMinutes.ValueChanged += (_, _) => { if (!_suppressEvents) SaveCurrentFromUi(); };

        _numMinLapTimeMs.Maximum = 30000;
        _numMinLapTimeMs.Increment = 100;
        _numMinLapTimeMs.Value = 3000;
        _numMinLapTimeMs.ValueChanged += (_, _) => { if (!_suppressEvents) SaveCurrentFromUi(); };

        _numStreakSlowMultiplier.DecimalPlaces = 2;
        _numStreakSlowMultiplier.Increment = 0.1m;
        _numStreakSlowMultiplier.Minimum = 1.0m;
        _numStreakSlowMultiplier.Maximum = 5.0m;
        _numStreakSlowMultiplier.Value = 1.5m;
        _numStreakSlowMultiplier.ValueChanged += (_, _) => { if (!_suppressEvents) SaveCurrentFromUi(); };

        _btnApply.Text = "Apply This Venue";
        _btnApply.Click += BtnApply_Click;

        // The fields at the text size in use - see SizeFields.
        _textSize.Keep(SizeFields);

        // TWO caption columns, not one. "Streak slow-lap multiplier:" is more
        // than three times the width of "Name:", so one column for the whole
        // card left the short rows with a gutter you could park a bus in.
        // The venue's own details and its race defaults do not need to line
        // up with each other, so each group is a grid of its own, measured
        // against its own captions.
        var details = new FieldGrid();

        details.AddRow("Name:", _txtName);
        details.AddRow("Connect via:", _cmbConnectionType);
        details.AddRow("Preferred track:", _cmbTrackLayout);
        details.AddRow("Preferred soundscape:", _cmbTheme);

        var lblDefaults = new Label { Text = "Race defaults for this venue:", AutoSize = true };
        _textSize.Wear(lblDefaults, () => AppTheme.BoldFont);

        var defaults = new FieldGrid();

        defaults.AddRow("Lap limit (0 = none):", _numLapLimit);
        defaults.AddRow("Time limit, min (0 = none):", _numTimeLimitMinutes);
        defaults.AddRow("Minimum lap time (ms):", _numMinLapTimeMs);
        defaults.AddRow("Streak slow-lap multiplier:", _numStreakSlowMultiplier);

        // What the last change did.
        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = AppTheme.TextMuted;

        var settings = new Section("Venue settings");

        settings.Add(details);
        settings.Add(lblDefaults, stretch: false);
        settings.Add(defaults);
        settings.Add(new ButtonBar().With(_btnApply));
        settings.Add(_lblStatus, gapBelow: 0);

        // --- The page ---

        var band = new Beside(VenuesWidth) { SideOnLeft = true };

        band.Add(settings, gapBelow: 0);
        band.AddSideFill(_venuesCard, _venuesCard.LeastHeight, gapBelow: 0);

        var page = new FillPage();

        page.Add(lblIntro);
        page.AddFill(band, band.LeastHeight, gapBelow: 0);

        Controls.Add(page);

        SetEditEnabled(false);
    }

    /// <summary>
    /// The fields' sizes at the text size in use. The name box is the width
    /// it was at the normal text size, at this size - capped, not stretched:
    /// a venue's name is a few words, and a box across the page for it just
    /// looks like a mistake. The pickers are that wide or as wide as their
    /// longest choice, and the number boxes as wide as six digits and their
    /// arrows need.
    ///
    /// Run again when the text size changes (TextSize.Keep) and when the
    /// pickers' lists are refilled (RefillDropdowns).
    /// </summary>
    private void SizeFields()
    {
        var field = UiLayout.AtTextSize(FieldWidth);

        Fit(_txtName, field);
        Fit(_cmbConnectionType, Math.Max(field, Pieces.PickerWidth(_cmbConnectionType, ref _connectionWidth)));
        Fit(_cmbTrackLayout, Math.Max(field, Pieces.PickerWidth(_cmbTrackLayout, ref _trackWidth)));
        Fit(_cmbTheme, Math.Max(field, Pieces.PickerWidth(_cmbTheme, ref _themeWidth)));

        var number = TextRenderer.MeasureText("000000", Font).Width + 34;

        foreach (var spinner in new NumericUpDown[] { _numLapLimit, _numTimeLimitMinutes, _numMinLapTimeMs, _numStreakSlowMultiplier })
        {
            Fit(spinner, number);
        }

        // Only when it changed: a new width lays the grid out again.
        static void Fit(Control control, int width)
        {
            if (control.Width != width)
            {
                control.Width = width;
            }
        }
    }

    /// <summary>
    /// The venues' column: the width the list was at the normal text size,
    /// at the size in use - and never narrower than its caption or its two
    /// buttons on one line need.
    /// </summary>
    private int VenuesWidth() =>
        Math.Max(
            UiLayout.AtTextSize(ListWidth),
            Math.Max(_venuesCard?.CaptionWidth ?? 0, _listButtons?.OneLineWidth ?? 0) + Section.Inset.Horizontal);

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

        // The longest choice may have come or gone: measured again.
        RefitPickers();
    }

    /// <summary>
    /// The pickers measured again after their lists changed - refilled, or
    /// given a "(missing: ...)" entry - which can change the longest choice
    /// without changing how many there are.
    /// </summary>
    private void RefitPickers()
    {
        _trackWidth = _themeWidth = null;
        SizeFields();
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
        //
        // Named rather than found: the fields used to be the children of
        // one group box, and are now in two grids inside a card.
        _txtName.ReadOnly = !enabled;

        foreach (var spinner in new[] { _numLapLimit, _numTimeLimitMinutes, _numMinLapTimeMs, _numStreakSlowMultiplier })
        {
            spinner.ReadOnly = !enabled;
        }

        _btnApply.Enabled = enabled;
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

        // A saved choice that has gone shows as "(missing: ...)", which may
        // be longer than anything else in the list.
        RefitPickers();
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
            $"Apply \"{venue.Name}\"? This sets its decoder on the Connection page, applies its preferred soundscape, and on Race Control selects its preferred track and updates the race defaults.",
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
