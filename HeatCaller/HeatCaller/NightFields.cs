namespace HeatCaller;

/// <summary>
/// A night's settings, as one set of fields (14 Nov 2026): the kind of night,
/// venue, layout and championship; the times and breaks; the classes, heats,
/// seeding and running order; the fees; and the between-race talk - every
/// setting a night template keeps.
///
/// WHY ONE SET. Setup's Tonight tab edits them on a race day and the Edit
/// Template window edits them on a template, and the two used to build their
/// own copies of the same fields - so a new night setting had to be added
/// twice, and the day's and the template's could quietly disagree about what
/// a field did. Now both put these same fields on their cards, load them from
/// a race day and write them back to one (a template is edited on a scratch
/// day - NightTemplates.ApplyTo, then NightTemplates.From). A field added here
/// is on both.
///
/// What stays the host's: a day's name and notes, the phones' next night,
/// starting the next race by itself, the club's ticks - and how changes are
/// saved. Tonight saves as you go with one undo entry for a run of edits; the
/// template window saves on Save. The fields only say that something changed
/// (Typed, LeftTyping, Changed), and the Breaks... and Order... buttons call
/// back to the host, which owns what they change.
/// </summary>
internal sealed class NightFields
{
    // --- Captions, measured for the caption column (FieldGrid.LineUp) ---

    /// <summary>The rounds row on a race day.</summary>
    public const string HeatsPerDriverCaption = "Heats each driver gets:";

    /// <summary>The same row on a club night. Both are in the column's measurement, so switching never moves the fields.</summary>
    public const string RacesPerDriverCaption = "Races each driver gets:";

    public const string ShapeCaption = "What kind of night:";
    public const string FirstRaceCaption = "First race at:";
    public const string OutByCaption = "Out of the venue by:";
    public const string EntriesCloseCaption = "Entries close at:";

    /// <summary>The rest gap's row (setup plan step 8) - RaceEvent.RacesOffBetween.</summary>
    public const string RacesOffCaption = "Races off between a driver's races:";

    private const string RaceDayShapeHint =
        "Qualifying heats, then finals built from them. Heats are built from drivers who have checked in.";

    private const string ClubNightShapeHint =
        "A flat list of races - no finals, no seeding, no bump-ups. Everyone in a racing class is in them.";

    private const string RaceDayClassHint =
        "Tick the classes running today. Next Steps builds all their heats in one press.";

    private const string ClubNightClassHint =
        "Tick the classes running today. Next Steps builds all their races in one press.";

    /// <summary>The widths at the normal text size, before any field is measured.</summary>
    private const int FieldWidth = 320;
    private const int NumberWidth = 70;

    // --- The fields ---

    public ComboBox Shape { get; } = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    /// <summary>Editable: the venue list is a suggestion, not a gatekeeper - a club night at a friend's garage needs no venue profile.</summary>
    public ComboBox Venue { get; } = new() { DropDownStyle = ComboBoxStyle.DropDown };

    public ComboBox Layout { get; } = new() { DropDownStyle = ComboBoxStyle.DropDown };

    public ComboBox Championship { get; } = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    public NumericUpDown Rounds { get; } = new() { Minimum = 1, Maximum = 10, Value = 3 };

    public CheckedListBox Classes { get; } = new ThemedCheckedListBox { CheckOnClick = true, BorderStyle = BorderStyle.None, IntegralHeight = false };

    public NumericUpDown HeatSize { get; } = new() { Minimum = 2, Maximum = 20, Value = 8 };

    /// <summary>How the first round is seeded (RaceEvent.FirstRoundSeeding, 11 Nov 2026).</summary>
    public ComboBox FirstRound { get; } = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    /// <summary>Small classes share a heat under this many drivers (11 Nov 2026, Phase 4.1). 0 is off.</summary>
    public NumericUpDown ShareUnder { get; } = new() { Minimum = 0, Maximum = 19, Value = 0 };

    /// <summary>Races off between a driver's races (12 Nov 2026, setup plan step 8).</summary>
    public NumericUpDown RacesOff { get; } = new() { Minimum = 0, Maximum = RestOrder.MostGap, Value = RestOrder.DefaultGap };

    public NumericUpDown EntryFee { get; } = new() { DecimalPlaces = 2, Maximum = 1000, Increment = 1 };

    public NumericUpDown ExtraFee { get; } = new() { DecimalPlaces = 2, Maximum = 1000, Increment = 1 };

    public CheckBox CheckedInOnly { get; } = new ThemedCheckBox { Text = "Only include drivers who have checked in", AutoSize = true };

    /// <summary>
    /// When the first race goes out, and whether it is set: two controls
    /// because the property is nullable and a time picker can't say "not
    /// set". TimePicker, not WinForms' DateTimePicker, which ignores the
    /// theme's colours and clips its own text.
    /// </summary>
    public TimePicker FirstRace { get; } = new() { AutoSize = true, Value = TimeSpan.FromHours(10) };

    public CheckBox FirstRaceSet { get; } = new ThemedCheckBox { Text = "Set", AutoSize = true };

    /// <summary>When the club has to be out of the venue (11 Nov 2026), and whether it's set.</summary>
    public TimePicker OutBy { get; } = new() { AutoSize = true, Value = TimeSpan.FromHours(22) };

    public CheckBox OutBySet { get; } = new ThemedCheckBox { Text = "Set", AutoSize = true };

    /// <summary>When entries close (12 Nov 2026, setup plan D1), and whether it's set.</summary>
    public TimePicker EntriesClose { get; } = new() { AutoSize = true, Value = TimeSpan.FromHours(18.5) };

    public CheckBox EntriesCloseSet { get; } = new ThemedCheckBox { Text = "Set", AutoSize = true };

    /// <summary>The night's planned breaks on one line (setup plan D5), and the button that changes them.</summary>
    public Label BreaksLine { get; } = new() { AutoSize = true, Margin = new Padding(0, 6, 8, 0), Text = "None" };

    public Button BreaksButton { get; } = new() { Text = "Breaks...", AutoSize = true };

    /// <summary>The class running order on one line (setup plan D7), and the button that changes it.</summary>
    public Label ClassOrderLine { get; } = new() { AutoSize = true, Margin = new Padding(0, 6, 8, 0), Text = "As on the Classes page" };

    public Button ClassOrderButton { get; } = new() { Text = "Order...", AutoSize = true };

    /// <summary>Between-race talk for the night: the club's Between races settings, or a preset over them (31 Oct 2026).</summary>
    public ComboBox GapPreset { get; } = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    /// <summary>The hint under the kind of night - as tall as the longer wording, so nothing below moves as it's changed.</summary>
    private readonly SteadyLabel _lblShapeHint = new() { Wordings = new[] { RaceDayShapeHint, ClubNightShapeHint } };

    /// <summary>The hint under the classes, worded from the kind of night, as tall as the longer wording.</summary>
    private readonly SteadyLabel _lblClassHint = new() { Wordings = new[] { RaceDayClassHint, ClubNightClassHint } };

    /// <summary>The rounds row's caption, held because its wording comes from the kind of night.</summary>
    private Label? _lblRounds;

    /// <summary>The club's classes, in the Classes page's order - for the running order's line.</summary>
    private List<string> _allClasses = new();

    private bool _editable = true;

    /// <summary>The drop-downs' measured widths - see Pieces.PickerWidth. Forgotten when the lists are rebuilt.</summary>
    private (Font Font, int Count, int Width)? _shapeWidth, _venueWidth, _layoutWidth, _championshipWidth, _firstRoundWidth, _gapPresetWidth;

    /// <summary>Set while the fields are being filled, so filling them isn't a change.</summary>
    public bool Loading { get; private set; }

    /// <summary>Typing in a field that waits for a pause (the venue, the layout).</summary>
    public event Action? Typed;

    /// <summary>Focus left a field that waits for a pause - write now.</summary>
    public event Action? LeftTyping;

    /// <summary>A field with a discrete value changed: a tick, a number, a choice. Never raised while Loading.</summary>
    public event Action? Changed;

    /// <summary>What Breaks... does - the host's, since it owns what the breaks are saved on.</summary>
    public Action? EditBreaks { get; set; }

    /// <summary>What Order... does - the host's, as Breaks....</summary>
    public Action? EditClassOrder { get; set; }

    private sealed record ShapeChoice(RaceDayShape Value, string Caption)
    {
        public override string ToString() => Caption;
    }

    private sealed record SeasonChoice(Season Season)
    {
        public override string ToString() => Season.Name;
    }

    private sealed record GapPresetChoice(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    /// <param name="textSize">The host's - the time pickers wear the body face at the text size in use.</param>
    public NightFields(TextSize textSize)
    {
        Shape.Items.Add(new ShapeChoice(RaceDayShape.RaceDay, "Race day - heats, then finals"));
        Shape.Items.Add(new ShapeChoice(RaceDayShape.ClubNight, "Club night - just races"));
        Shape.SelectedIndex = 0;

        foreach (var mode in Enum.GetValues<FirstRoundSeeding>())
        {
            FirstRound.Items.Add(HeatCaller.FirstRound.Words(mode));
        }

        FirstRound.SelectedIndex = 0;

        GapPreset.Items.Add(new GapPresetChoice(string.Empty, "The club's Between races settings"));

        foreach (var preset in HeatCaller.GapPreset.All)
        {
            GapPreset.Items.Add(new GapPresetChoice(preset.Key, preset.Name));
        }

        GapPreset.SelectedIndex = 0;

        foreach (var picker in new[] { FirstRace, OutBy, EntriesClose })
        {
            textSize.Wear(picker, () => AppTheme.BodyFont);
        }

        BreaksButton.Click += (_, _) => EditBreaks?.Invoke();
        ClassOrderButton.Click += (_, _) => EditClassOrder?.Invoke();

        Wire();
        ApplyShapeWording();
    }

    // ------------------------------------------------------------------
    //  The rows, on the host's cards
    // ------------------------------------------------------------------

    /// <summary>The night's card: the kind of night with its hint, the venue, the layout, the championship.</summary>
    public void AddNightRows(FieldGrid grid)
    {
        grid.AddRow(ShapeCaption, Shape);
        grid.AddHint(_lblShapeHint);
        grid.AddRow("Venue:", Venue);
        grid.AddRow("Track layout:", Layout);
        grid.AddRow("Championship:", Championship);
    }

    /// <summary>The times card: entries close (first, as in the night), the first race, out by, and the breaks.</summary>
    public void AddTimesRows(FieldGrid grid)
    {
        grid.AddRow(EntriesCloseCaption, FieldGrid.Inline(EntriesClose, EntriesCloseSet));
        grid.AddHint("After this, a driver who checks in is a late entry. The announcer calls it ten minutes before and as it passes, and Next Steps counts down to it.");
        grid.AddRow(FirstRaceCaption, FieldGrid.Inline(FirstRace, FirstRaceSet));
        grid.AddRow(OutByCaption, FieldGrid.Inline(OutBy, OutBySet));
        grid.AddRow("Breaks:", FieldGrid.Inline(BreaksLine, BreaksButton));
        grid.AddHint("Watering the track, dinner, track repairs. The finish time, every estimated start and the gap before the next race allow for them, and the announcer tells the room.");
    }

    /// <summary>The heats card: rounds, classes, heat size, seeding, sharing, the rest gap, the check-in rule, the running order.</summary>
    public void AddHeatsRows(FieldGrid grid)
    {
        _lblRounds = grid.AddRow(HeatsPerDriverCaption, Rounds);
        grid.AddRow("Classes racing:", Classes);
        grid.AddHint(_lblClassHint);
        grid.AddRow("Drivers per heat:", HeatSize);
        grid.AddRow("First round seeded:", FirstRound);
        grid.AddRow("Share a heat under (drivers):", ShareUnder);
        grid.AddRow(RacesOffCaption, RacesOff);
        grid.AddField(CheckedInOnly);
        grid.AddRow("Class running order:", FieldGrid.Inline(ClassOrderLine, ClassOrderButton));
        grid.AddHint("Which class goes first in each round, and whether the order turns round each round. It decides who marshals for whom and when a class finishes. Used when the heats are built.");

        ApplyShapeWording();
    }

    /// <summary>The money card: the entry fee and each extra class.</summary>
    public void AddMoneyRows(FieldGrid grid)
    {
        grid.AddRow("Entry fee:", EntryFee);
        grid.AddRow("Each extra class:", ExtraFee);
    }

    /// <summary>Running the night: the between-race talk. (Starting the next race by itself is the race day's, not the template's.)</summary>
    public void AddRunRows(FieldGrid grid)
    {
        grid.AddRow("Between-race talk:", GapPreset);
        grid.AddHint("How much the announcer says between races tonight. A preset here is for this night only - the club's own settings are on the AI Commentary tab.");
    }

    // ------------------------------------------------------------------
    //  Lists, loading and writing
    // ------------------------------------------------------------------

    /// <summary>The lists rebuilt from what exists now: the club's classes, venues, layouts and championships.</summary>
    public void FillLists(IEnumerable<string> classes, IEnumerable<string> venues, IEnumerable<string> layouts, IEnumerable<Season> seasons)
    {
        Loading = true;

        try
        {
            _allClasses = classes.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();

            Classes.Items.Clear();

            foreach (var name in _allClasses)
            {
                Classes.Items.Add(name);
            }

            Venue.Items.Clear();

            foreach (var venue in venues)
            {
                Venue.Items.Add(venue);
            }

            Layout.Items.Clear();

            foreach (var layout in layouts)
            {
                Layout.Items.Add(layout);
            }

            Championship.Items.Clear();
            Championship.Items.Add("(standalone - no championship)");

            foreach (var season in seasons)
            {
                Championship.Items.Add(new SeasonChoice(season));
            }

            if (Championship.SelectedIndex < 0)
            {
                Championship.SelectedIndex = 0;
            }
        }
        finally
        {
            Loading = false;
        }

        _venueWidth = _layoutWidth = _championshipWidth = null;
    }

    /// <summary>
    /// The fields filled from a race day. <paramref name="heatSizeShown"/> is
    /// the heat size to show for a day that never set its own
    /// (RaceEventStore.HeatSizeFor) - shown, not written. Raises nothing.
    /// </summary>
    public void Load(RaceEvent day, int heatSizeShown)
    {
        Loading = true;

        try
        {
            for (var i = 0; i < Shape.Items.Count; i++)
            {
                if (Shape.Items[i] is ShapeChoice choice && choice.Value == day.Shape)
                {
                    Shape.SelectedIndex = i;
                    break;
                }
            }

            ApplyShapeWording();

            Venue.Text = day.VenueName;
            Layout.Text = day.TrackLayoutName;
            Rounds.Value = Math.Clamp(day.QualifyingRounds, 1, 10);

            // Ten o'clock shown, not written, when the day has no time - the
            // time becomes real only when the tick goes on.
            FirstRaceSet.Checked = day.FirstRaceLocalTime is not null;
            FirstRace.Value = day.FirstRaceLocalTime ?? TimeSpan.FromHours(10);
            OutBySet.Checked = day.OutByLocalTime is not null;
            OutBy.Value = day.OutByLocalTime ?? TimeSpan.FromHours(22);
            EntriesCloseSet.Checked = day.EntriesCloseLocalTime is not null;
            EntriesClose.Value = day.EntriesCloseLocalTime ?? TimeSpan.FromHours(18.5);

            ShowBreaks(day);
            ShowClassOrder(day);

            HeatSize.Value = Math.Clamp(heatSizeShown, (int)HeatSize.Minimum, (int)HeatSize.Maximum);
            FirstRound.SelectedIndex = Math.Clamp((int)day.FirstRoundSeeding, 0, FirstRound.Items.Count - 1);
            ShareUnder.Value = Math.Clamp(day.ShareHeatsUnder, (int)ShareUnder.Minimum, (int)ShareUnder.Maximum);
            RacesOff.Value = Math.Clamp(day.RacesOffBetween, (int)RacesOff.Minimum, (int)RacesOff.Maximum);
            EntryFee.Value = Math.Clamp(day.EntryFee, EntryFee.Minimum, EntryFee.Maximum);
            ExtraFee.Value = Math.Clamp(day.ExtraClassFee, ExtraFee.Minimum, ExtraFee.Maximum);
            CheckedInOnly.Checked = day.CheckedInDriversOnly;
            ShowGapPreset(day.BetweenRacesPreset);

            // The club's classes, and after them any class the day names that
            // the club no longer has - listed, ticked, so writing the day back
            // doesn't drop it without a word. Rebuilt for each day, so one
            // day's missing class isn't left on the list for the next.
            Classes.Items.Clear();

            foreach (var name in _allClasses.Concat((day.ClassNames ?? new List<string>())
                         .Where(n => !_allClasses.Contains(n, StringComparer.OrdinalIgnoreCase))))
            {
                Classes.Items.Add(name);
            }

            for (var i = 0; i < Classes.Items.Count; i++)
            {
                Classes.SetItemChecked(i, (day.ClassNames ?? new List<string>()).Contains(Classes.Items[i] as string ?? string.Empty, StringComparer.OrdinalIgnoreCase));
            }

            var championship = 0;

            for (var i = 1; i < Championship.Items.Count; i++)
            {
                if (Championship.Items[i] is SeasonChoice season && string.Equals(season.Season.Id, day.ChampionshipId, StringComparison.Ordinal))
                {
                    championship = i;
                    break;
                }
            }

            Championship.SelectedIndex = championship;

            FollowTicks();
        }
        finally
        {
            Loading = false;
        }
    }

    /// <summary>Every field written onto <paramref name="day"/> - the kind of night, the times, the classes and the rest. Its name, rounds and results are left alone.</summary>
    public void WriteTo(RaceEvent day)
    {
        day.VenueName = Venue.Text.Trim();
        day.TrackLayoutName = Layout.Text.Trim();
        day.Shape = SelectedShape;
        day.QualifyingRounds = (int)Rounds.Value;
        day.FirstRaceLocalTime = FirstRaceSet.Checked ? FirstRace.Value : null;
        day.OutByLocalTime = OutBySet.Checked ? OutBy.Value : null;
        day.EntriesCloseLocalTime = EntriesCloseSet.Checked ? EntriesClose.Value : null;
        day.DriversPerHeat = (int)HeatSize.Value;
        day.FirstRoundSeeding = (FirstRoundSeeding)Math.Max(0, FirstRound.SelectedIndex);
        day.ShareHeatsUnder = (int)ShareUnder.Value;
        day.RacesOffBetween = (int)RacesOff.Value;
        day.EntryFee = EntryFee.Value;
        day.ExtraClassFee = ExtraFee.Value;

        // Deliberately not re-derived from the kind of night: it is set from
        // that once, when the day is started; after that it is the director's.
        day.CheckedInDriversOnly = CheckedInOnly.Checked;
        day.BetweenRacesPreset = GapPreset.SelectedItem is GapPresetChoice gap ? gap.Key : string.Empty;
        day.ClassNames = CheckedClasses;
        day.ChampionshipId = Championship.SelectedItem is SeasonChoice choice ? choice.Season.Id : string.Empty;
    }

    /// <summary>The classes ticked now, in the list's order.</summary>
    public List<string> CheckedClasses =>
        Classes.CheckedItems.Cast<object>().Select(item => item as string ?? string.Empty).Where(name => name.Length > 0).ToList();

    /// <summary>The kind of night picked. Never null - the drop-down always has a choice.</summary>
    public RaceDayShape SelectedShape => Shape.SelectedItem is ShapeChoice choice ? choice.Value : RaceDayShape.RaceDay;

    /// <summary>Whether the fields can be changed - never on the legacy holder, which is a filing cabinet, not a race day.</summary>
    public void SetEditable(bool editable)
    {
        _editable = editable;

        foreach (var field in new Control[]
                 {
                     Shape, Venue, Layout, Championship, Rounds, Classes, HeatSize, FirstRound, ShareUnder, RacesOff,
                     EntryFee, ExtraFee, CheckedInOnly, GapPreset, FirstRaceSet, OutBySet, EntriesCloseSet, BreaksButton, ClassOrderButton,
                 })
        {
            field.Enabled = editable;
        }

        FollowTicks();
    }

    /// <summary>The breaks line, from a day's breaks.</summary>
    public void ShowBreaks(RaceEvent day) => BreaksLine.Text = NightBreaks.Summary(day.Breaks, day.IsClubNight);

    /// <summary>The running order line, from a day's classes and order.</summary>
    public void ShowClassOrder(RaceEvent day) =>
        ClassOrderLine.Text = ClassRunningOrder.Summary(ClassesOn(day), day.ClassRunningOrder, day.RotateClassOrder);

    /// <summary>A day's classes in the Classes page's order: those picked, or every class when none are.</summary>
    public List<string> ClassesOn(RaceEvent day) =>
        (day.ClassNames ?? new List<string>()).Count == 0
            ? _allClasses.ToList()
            : _allClasses.Where(n => day.ClassNames!.Contains(n, StringComparer.OrdinalIgnoreCase))
                .Concat(day.ClassNames!.Where(n => !_allClasses.Contains(n, StringComparer.OrdinalIgnoreCase)))
                .ToList();

    /// <summary>
    /// The fields' sizes at the text size in use: the drop-downs as wide as
    /// their longest choice and never narrower than at the normal size, the
    /// boxes the width they were then, the class list five lines tall.
    /// </summary>
    public void SizeFields()
    {
        var wide = UiLayout.AtTextSize(FieldWidth);
        var number = UiLayout.AtTextSize(NumberWidth);

        Fit(Shape, Math.Max(wide, Pieces.PickerWidth(Shape, ref _shapeWidth)));
        Fit(Venue, Math.Max(wide, Pieces.PickerWidth(Venue, ref _venueWidth)));
        Fit(Layout, Math.Max(wide, Pieces.PickerWidth(Layout, ref _layoutWidth)));
        Fit(Championship, Math.Max(wide, Pieces.PickerWidth(Championship, ref _championshipWidth)));
        Fit(Rounds, number);
        Fit(Classes, wide, Classes.Font.Height * 5 + 12);
        Fit(HeatSize, number);
        Fit(FirstRound, Math.Max(UiLayout.AtTextSize(160), Pieces.PickerWidth(FirstRound, ref _firstRoundWidth)));
        Fit(ShareUnder, number);
        Fit(RacesOff, number);
        Fit(EntryFee, number);
        Fit(ExtraFee, number);
        Fit(GapPreset, Math.Max(wide, Pieces.PickerWidth(GapPreset, ref _gapPresetWidth)));

        // Only when it changed: a new size lays the grid out again.
        static void Fit(Control field, int width, int? height = null)
        {
            var size = new Size(width, height ?? field.Height);

            if (field.Size != size)
            {
                field.Size = size;
            }
        }
    }

    /// <summary>The drop-downs measured again - after their lists were rebuilt, which can change their longest choice.</summary>
    public void RefitFields()
    {
        _shapeWidth = _venueWidth = _layoutWidth = _championshipWidth = _firstRoundWidth = _gapPresetWidth = null;
        SizeFields();
    }

    private void ShowGapPreset(string? key)
    {
        for (var i = 0; i < GapPreset.Items.Count; i++)
        {
            if (GapPreset.Items[i] is GapPresetChoice choice && string.Equals(choice.Key, key ?? string.Empty, StringComparison.Ordinal))
            {
                GapPreset.SelectedIndex = i;
                return;
            }
        }

        GapPreset.SelectedIndex = 0;
    }

    /// <summary>
    /// The captions and hints worded from the kind of night. Only wording: no
    /// field moves, since both captions are in the column's measurement and
    /// both hints are as tall as their longer wording.
    /// </summary>
    private void ApplyShapeWording()
    {
        var clubNight = SelectedShape == RaceDayShape.ClubNight;

        if (_lblRounds is not null)
        {
            _lblRounds.Text = clubNight ? RacesPerDriverCaption : HeatsPerDriverCaption;
        }

        _lblShapeHint.Text = clubNight ? ClubNightShapeHint : RaceDayShapeHint;
        _lblClassHint.Text = clubNight ? ClubNightClassHint : RaceDayClassHint;
    }

    /// <summary>A time's picker is grayed rather than hidden while its tick is off - a picker that vanished would move the rows under it.</summary>
    private void FollowTicks()
    {
        FirstRace.Enabled = _editable && FirstRaceSet.Checked;
        OutBy.Enabled = _editable && OutBySet.Checked;
        EntriesClose.Enabled = _editable && EntriesCloseSet.Checked;
    }

    private void Wire()
    {
        void Change()
        {
            if (!Loading)
            {
                Changed?.Invoke();
            }
        }

        Venue.TextChanged += (_, _) =>
        {
            if (!Loading)
            {
                Typed?.Invoke();
            }
        };
        Layout.TextChanged += (_, _) =>
        {
            if (!Loading)
            {
                Typed?.Invoke();
            }
        };
        Venue.Leave += (_, _) => LeftTyping?.Invoke();
        Layout.Leave += (_, _) => LeftTyping?.Invoke();

        // The wording first, then the change - wording happens even while
        // loading, which is how picking another day gets the right captions.
        Shape.SelectedIndexChanged += (_, _) =>
        {
            ApplyShapeWording();
            Change();
        };

        foreach (var tick in new[] { FirstRaceSet, OutBySet, EntriesCloseSet })
        {
            tick.CheckedChanged += (_, _) =>
            {
                FollowTicks();
                Change();
            };
        }

        foreach (var picker in new[] { FirstRace, OutBy, EntriesClose })
        {
            picker.ValueChanged += Change;
        }

        foreach (var number in new[] { Rounds, HeatSize, ShareUnder, RacesOff, EntryFee, ExtraFee })
        {
            number.ValueChanged += (_, _) => Change();
        }

        foreach (var combo in new[] { Championship, FirstRound, GapPreset })
        {
            combo.SelectedIndexChanged += (_, _) => Change();
        }

        CheckedInOnly.CheckedChanged += (_, _) => Change();

        // ItemCheck comes BEFORE the tick is applied, so the change is posted
        // to land after it. A handle first: BeginInvoke throws without one,
        // and the list is filled before it is ever shown.
        Classes.ItemCheck += (_, _) =>
        {
            if (!Loading && Classes.IsHandleCreated && !Classes.IsDisposed)
            {
                Classes.BeginInvoke(new Action(Change));
            }
        };
    }
}
