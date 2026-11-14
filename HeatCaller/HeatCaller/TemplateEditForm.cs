namespace HeatCaller;

/// <summary>
/// A night template, edited where it is kept (14 Nov 2026, Setup's Templates
/// tab). A template used to be made only by saving a race day as one, and
/// changed only by starting a night from it, changing the night and saving
/// it over the old one. This is its fields, in the cards Tonight has them in,
/// with what the night would come to along the bottom.
///
/// Works on a copy: <see cref="Result"/> is the template as saved, and the
/// one handed in is never changed. Its nights (Weekdays, Dates) are carried
/// over untouched - When... on the card's menu sets those.
/// </summary>
internal sealed class TemplateEditForm : Form
{
    private readonly NightTemplate _original;
    private readonly IReadOnlyCollection<string> _takenNames;
    private readonly Func<NightTemplate, (string Line, bool Late)?>? _forecast;

    private readonly TextBox _txtName = new();
    private readonly ComboBox _cmbShape = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _cmbVenue = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly ComboBox _cmbLayout = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly ComboBox _cmbChampionship = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckedListBox _lstClasses = new() { CheckOnClick = true, BorderStyle = BorderStyle.None, IntegralHeight = false };
    private readonly NumericUpDown _numRounds = new() { Minimum = 1, Maximum = 10 };
    private readonly NumericUpDown _numHeatSize = new() { Minimum = 2, Maximum = 20 };
    private readonly ComboBox _cmbFirstRound = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _numShareUnder = new() { Minimum = 0, Maximum = 19 };
    private readonly NumericUpDown _numRacesOff = new() { Minimum = 0, Maximum = RestOrder.MostGap };
    private readonly CheckBox _chkCheckedInOnly = new ThemedCheckBox { Text = "Only include drivers who have checked in", AutoSize = true };
    private readonly TimePicker _dtpEntriesClose = new() { AutoSize = true };
    private readonly CheckBox _chkEntriesClose = new ThemedCheckBox { Text = "Set", AutoSize = true };
    private readonly TimePicker _dtpFirstRace = new() { AutoSize = true };
    private readonly CheckBox _chkFirstRace = new ThemedCheckBox { Text = "Set", AutoSize = true };
    private readonly TimePicker _dtpOutBy = new() { AutoSize = true };
    private readonly CheckBox _chkOutBy = new ThemedCheckBox { Text = "Set", AutoSize = true };
    private readonly Label _lblBreaks = new() { AutoSize = true, Margin = new Padding(0, 6, 8, 0) };
    private readonly NumericUpDown _numEntryFee = new() { Minimum = 0, Maximum = 10000, DecimalPlaces = 2 };
    private readonly NumericUpDown _numExtraFee = new() { Minimum = 0, Maximum = 10000, DecimalPlaces = 2 };
    private readonly ComboBox _cmbGapPreset = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _lblForecast = new() { AutoSize = true, UseMnemonic = false };
    private readonly Label _lblProblem = new() { AutoSize = true, UseMnemonic = false, Visible = false };

    private List<PlannedBreak> _breaks;

    /// <summary>The template as saved. Null until Save.</summary>
    public NightTemplate? Result { get; private set; }

    private sealed record Choice(string Key, string Text)
    {
        public override string ToString() => Text;
    }

    /// <param name="template">The template to edit; a fresh one for New Template.</param>
    /// <param name="isNew">Whether it's a new template - the window's caption and the name check say so.</param>
    /// <param name="takenNames">The other templates' names, so a rename can't land on one of them.</param>
    /// <param name="classes">The club's classes, in the Classes page's order.</param>
    /// <param name="venues">Venues to suggest.</param>
    /// <param name="layouts">Track layouts to suggest.</param>
    /// <param name="seasons">Championships.</param>
    /// <param name="forecast">What a night from the template as it stands would come to, or null when there's nothing to go by.</param>
    public TemplateEditForm(
        NightTemplate template,
        bool isNew,
        IReadOnlyCollection<string> takenNames,
        IReadOnlyList<string> classes,
        IEnumerable<string> venues,
        IEnumerable<string> layouts,
        IEnumerable<Season> seasons,
        Func<NightTemplate, (string Line, bool Late)?>? forecast)
    {
        _original = template;
        _takenNames = takenNames;
        _forecast = forecast;
        _breaks = NightBreaks.Copy(template.Breaks);

        Text = isNew ? "New Template" : $"Edit Template - {template.Name}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        // --- The night ---
        _txtName.Text = template.Name;
        _cmbShape.Items.Add("Race day - heats, then finals");
        _cmbShape.Items.Add("Club night - just races");
        _cmbShape.SelectedIndex = template.Shape == RaceDayShape.ClubNight ? 1 : 0;

        foreach (var venue in venues)
        {
            _cmbVenue.Items.Add(venue);
        }

        foreach (var layout in layouts)
        {
            _cmbLayout.Items.Add(layout);
        }

        _cmbVenue.Text = template.VenueName;
        _cmbLayout.Text = template.TrackLayoutName;

        _cmbChampionship.Items.Add(new Choice(string.Empty, "(standalone - no championship)"));

        foreach (var season in seasons)
        {
            _cmbChampionship.Items.Add(new Choice(season.Id, season.Name));
        }

        _cmbChampionship.SelectedIndex = Math.Max(0, _cmbChampionship.Items.Cast<Choice>().ToList().FindIndex(c => c.Key == (template.ChampionshipId ?? string.Empty)));

        var nightGrid = new FieldGrid();
        nightGrid.AddRow("Name:", _txtName);
        nightGrid.AddRow("What kind of night:", _cmbShape);
        nightGrid.AddRow("Venue:", _cmbVenue);
        nightGrid.AddRow("Track layout:", _cmbLayout);
        nightGrid.AddRow("Championship:", _cmbChampionship);

        // --- Times ---
        Time(_dtpEntriesClose, _chkEntriesClose, template.EntriesCloseLocalTime, TimeSpan.FromHours(18.5));
        Time(_dtpFirstRace, _chkFirstRace, template.FirstRaceLocalTime, TimeSpan.FromHours(10));
        Time(_dtpOutBy, _chkOutBy, template.OutByLocalTime, TimeSpan.FromHours(22));

        var btnBreaks = new Button { Text = "Breaks...", AutoSize = true };
        btnBreaks.Click += (_, _) => EditBreaks();
        ShowBreaks();

        var timesGrid = new FieldGrid();
        timesGrid.AddRow("Entries close at:", FieldGrid.Inline(_dtpEntriesClose, _chkEntriesClose));
        timesGrid.AddRow("First race at:", FieldGrid.Inline(_dtpFirstRace, _chkFirstRace));
        timesGrid.AddRow("Out of the venue by:", FieldGrid.Inline(_dtpOutBy, _chkOutBy));
        timesGrid.AddRow("Breaks:", FieldGrid.Inline(_lblBreaks, btnBreaks));

        // --- Heats ---
        foreach (var name in classes)
        {
            _lstClasses.Items.Add(name, template.ClassNames.Contains(name, StringComparer.OrdinalIgnoreCase));
        }

        // A class the template names that the club no longer has stays
        // listed, ticked, so saving doesn't drop it without saying.
        foreach (var name in template.ClassNames.Where(n => !classes.Contains(n, StringComparer.OrdinalIgnoreCase)))
        {
            _lstClasses.Items.Add(name, true);
        }

        _numRounds.Value = Math.Clamp(template.QualifyingRounds > 0 ? template.QualifyingRounds : 3, 1, 10);
        _numHeatSize.Value = Math.Clamp(template.DriversPerHeat > 0 ? template.DriversPerHeat : 8, 2, 20);

        foreach (var mode in Enum.GetValues<FirstRoundSeeding>())
        {
            _cmbFirstRound.Items.Add(FirstRound.Words(mode));
        }

        _cmbFirstRound.SelectedIndex = Math.Clamp((int)template.FirstRoundSeeding, 0, _cmbFirstRound.Items.Count - 1);
        _numShareUnder.Value = Math.Clamp(template.ShareHeatsUnder, 0, 19);
        _numRacesOff.Value = Math.Clamp(template.RacesOffBetween, 0, RestOrder.MostGap);
        _chkCheckedInOnly.Checked = template.CheckedInDriversOnly;

        var heatsGrid = new FieldGrid();
        var lblRounds = heatsGrid.AddRow("Heats each driver gets:", _numRounds);
        heatsGrid.AddRow("Classes racing:", _lstClasses);
        heatsGrid.AddRow("Drivers per heat:", _numHeatSize);
        heatsGrid.AddRow("First round seeded:", _cmbFirstRound);
        heatsGrid.AddRow("Share a heat under (drivers):", _numShareUnder);
        heatsGrid.AddRow(RaceDayEventControl.RacesOffCaption, _numRacesOff);
        heatsGrid.AddField(_chkCheckedInOnly);
        heatsGrid.AddHint("The running order is kept from the night the template was saved from; change it on Tonight once a night is started.");

        // --- Money ---
        _numEntryFee.Value = Math.Clamp(template.EntryFee, 0, 10000);
        _numExtraFee.Value = Math.Clamp(template.ExtraClassFee, 0, 10000);

        var moneyGrid = new FieldGrid();
        moneyGrid.AddRow("Entry fee:", _numEntryFee);
        moneyGrid.AddRow("Each extra class:", _numExtraFee);

        // --- Running the night ---
        _cmbGapPreset.Items.Add(new Choice(string.Empty, "The club's Between races settings"));

        foreach (var preset in GapPreset.All)
        {
            _cmbGapPreset.Items.Add(new Choice(preset.Key, preset.Name));
        }

        _cmbGapPreset.SelectedIndex = Math.Max(0, _cmbGapPreset.Items.Cast<Choice>().ToList().FindIndex(c => c.Key == (template.BetweenRacesPreset ?? string.Empty)));

        var runGrid = new FieldGrid();
        runGrid.AddRow("Between-race talk:", _cmbGapPreset);

        var body = new PageStack();
        body.Add(Card("The night", nightGrid));
        body.Add(Card("Times", timesGrid));
        body.Add(Card("Heats", heatsGrid));
        body.Add(Card("Money", moneyGrid));
        body.Add(Card("Running the night", runGrid), gapBelow: 0);

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var btnSave = new Button { Text = "Save", AutoSize = true };
        btnSave.Click += (_, _) => Save();

        _lblForecast.ForeColor = AppTheme.TextMuted;
        _lblProblem.ForeColor = AppTheme.Danger;

        var page = new FillPage();
        page.AddFill(body, 200, gapBelow: UiLayout.RowGap);
        page.Add(_lblForecast, stretch: true, gapBelow: UiLayout.HintGap);
        page.Add(_lblProblem, stretch: true, gapBelow: UiLayout.HintGap);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);

        Controls.Add(page);
        AcceptButton = btnSave;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);
        AppTheme.StyleButton(btnSave, AppTheme.ButtonKind.Primary);

        FieldGrid.LineUp(new[] { nightGrid, timesGrid, heatsGrid, moneyGrid, runGrid }, RaceDayEventControl.RacesOffCaption);

        var wide = UiLayout.AtTextSize(300);
        _txtName.Width = wide;

        // Each drop-down as wide as its longest choice, never narrower than
        // the name box - measured, as Tonight's are (Pieces.PickerWidth).
        foreach (var combo in new[] { _cmbShape, _cmbVenue, _cmbLayout, _cmbChampionship, _cmbFirstRound, _cmbGapPreset })
        {
            (Font Font, int Count, int Width)? measured = null;
            combo.Width = Math.Max(wide, Pieces.PickerWidth(combo, ref measured));
        }

        _lstClasses.Size = new Size(wide, _lstClasses.Font.Height * 6 + 12);

        foreach (var number in new Control[] { _numRounds, _numHeatSize, _numShareUnder, _numRacesOff, _numEntryFee, _numExtraFee })
        {
            number.Width = UiLayout.AtTextSize(90);
        }

        // The rounds caption follows the kind of night, as Tonight's does.
        void Worded() => lblRounds.Text = _cmbShape.SelectedIndex == 1 ? "Races each driver gets:" : "Heats each driver gets:";
        _cmbShape.SelectedIndexChanged += (_, _) => { Worded(); ShowForecast(); };
        Worded();

        // The forecast follows every change that moves it.
        _lstClasses.ItemCheck += (_, _) => BeginInvoke(ShowForecast);
        foreach (var number in new[] { _numRounds, _numHeatSize })
        {
            number.ValueChanged += (_, _) => ShowForecast();
        }

        foreach (var picker in new[] { _dtpFirstRace, _dtpOutBy })
        {
            picker.ValueChanged += ShowForecast;
        }

        foreach (var tick in new[] { _chkFirstRace, _chkOutBy, _chkEntriesClose })
        {
            tick.CheckedChanged += (_, _) => { FollowTicks(); ShowForecast(); };
        }

        FollowTicks();
        ShowForecast();

        DialogFit.Keep(this, page, width: 760, height: 720, body: body);
    }

    private static Section Card(string caption, FieldGrid grid)
    {
        var card = new Section(caption);
        card.Add(grid, gapBelow: 0);
        return card;
    }

    private static void Time(TimePicker picker, CheckBox tick, TimeSpan? value, TimeSpan shown)
    {
        tick.Checked = value is not null;
        picker.Value = value ?? shown;
    }

    private void FollowTicks()
    {
        _dtpEntriesClose.Enabled = _chkEntriesClose.Checked;
        _dtpFirstRace.Enabled = _chkFirstRace.Checked;
        _dtpOutBy.Enabled = _chkOutBy.Checked;
    }

    private void ShowBreaks() => _lblBreaks.Text = NightBreaks.Summary(_breaks, _cmbShape.SelectedIndex == 1);

    private void EditBreaks()
    {
        using var form = new BreaksForm(_breaks, (int)_numRounds.Value, _cmbShape.SelectedIndex == 1);

        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _breaks = form.Breaks;
            ShowBreaks();
            ShowForecast();
        }
    }

    private void ShowForecast()
    {
        if (IsDisposed || _forecast is null)
        {
            _lblForecast.Visible = false;
            return;
        }

        var said = _forecast(Current(_txtName.Text.Trim()));

        _lblForecast.Visible = said is not null;
        _lblForecast.Text = said?.Line ?? string.Empty;
        _lblForecast.ForeColor = said is { Late: true } ? AppTheme.Warning : AppTheme.TextMuted;
    }

    /// <summary>The template as the window has it now, under <paramref name="name"/>.</summary>
    private NightTemplate Current(string name)
    {
        var ticked = _lstClasses.CheckedItems.Cast<string>().ToList();

        return new NightTemplate
        {
            Name = name,
            Shape = _cmbShape.SelectedIndex == 1 ? RaceDayShape.ClubNight : RaceDayShape.RaceDay,
            VenueName = _cmbVenue.Text.Trim(),
            TrackLayoutName = _cmbLayout.Text.Trim(),
            ChampionshipId = (_cmbChampionship.SelectedItem as Choice)?.Key ?? string.Empty,
            ClassNames = ticked,

            // The running order keeps the classes still ticked, in the order they had.
            ClassRunningOrder = (_original.ClassRunningOrder ?? new List<string>())
                .Where(c => ticked.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList(),
            RotateClassOrder = _original.RotateClassOrder,
            QualifyingRounds = (int)_numRounds.Value,
            DriversPerHeat = (int)_numHeatSize.Value,
            FirstRoundSeeding = (FirstRoundSeeding)Math.Max(0, _cmbFirstRound.SelectedIndex),
            ShareHeatsUnder = (int)_numShareUnder.Value,
            RacesOffBetween = (int)_numRacesOff.Value,
            EntryFee = _numEntryFee.Value,
            ExtraClassFee = _numExtraFee.Value,
            BetweenRacesPreset = (_cmbGapPreset.SelectedItem as Choice)?.Key ?? string.Empty,
            CheckedInDriversOnly = _chkCheckedInOnly.Checked,
            EntriesCloseLocalTime = _chkEntriesClose.Checked ? _dtpEntriesClose.Value : null,
            FirstRaceLocalTime = _chkFirstRace.Checked ? _dtpFirstRace.Value : null,
            OutByLocalTime = _chkOutBy.Checked ? _dtpOutBy.Value : null,
            Breaks = NightBreaks.Copy(_breaks),
            Weekdays = (_original.Weekdays ?? new List<DayOfWeek>()).ToList(),
            Dates = (_original.Dates ?? new List<DateTime>()).ToList(),
        };
    }

    /// <summary>Why the template as it stands can't be saved, or null when it can. Pure, for the tests.</summary>
    internal static string? Problem(string name, int classesTicked, IReadOnlyCollection<string> takenNames)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Give the template a name - \"Tuesday club night\", \"Points round\".";
        }

        if (takenNames.Any(t => string.Equals(t.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return $"There's already a template called \"{name.Trim()}\". Pick another name.";
        }

        return classesTicked == 0 ? "Tick at least one class - a night with none races nobody." : null;
    }

    private void Save()
    {
        var name = _txtName.Text.Trim();

        if (Problem(name, _lstClasses.CheckedItems.Count, _takenNames) is { } problem)
        {
            _lblProblem.Text = problem;
            _lblProblem.Visible = true;
            return;
        }

        Result = Current(name);
        DialogResult = DialogResult.OK;
        Close();
    }
}
