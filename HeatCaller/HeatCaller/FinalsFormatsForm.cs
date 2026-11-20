namespace HeatCaller;

/// <summary>
/// A class's two extra finals formats (19 Nov 2026, race-night build plan
/// row 14b), opened from Edit Class's Finals Formats... button: the Pole
/// Shootout and the Invitational (Reedy-style format). Both are off until
/// ticked, and everything under a tick is greyed while it is off.
///
/// On the layout pieces (DialogFit), in the redesigned dialog idiom of Edit
/// Class (18 Nov 2026): a line saying what the window is for, then a card
/// for each format with its line on what it does, the settings in a field
/// grid - captions in one column down both cards (FieldGrid.LineUp) - and
/// a note on how it runs. That is the body, which scrolls on a screen too
/// short for it; Cancel and OK at the foot, OK in the app's colour.
///
/// Writes onto the class only on OK, like the other sub-editors; the class
/// itself is saved by Edit Class's Save.
/// </summary>
public sealed class FinalsFormatsForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size - Edit Class's width, so it reads as part of it.</summary>
    private const int DialogWidth = 640;

    private readonly RaceClass _raceClass;
    private readonly Func<string, IReadOnlyList<(string Transponder, string Name)>>? _driversOf;

    private readonly CheckBox _chkShootout = new ThemedCheckBox();
    private readonly NumericUpDown _numShootoutDrivers = new();
    private readonly NumericUpDown _numShootoutLaps = new();
    private readonly Label _lblShootoutDrivers;
    private readonly Label _lblShootoutLaps;

    private readonly CheckBox _chkInvitational = new ThemedCheckBox();
    private readonly NumericUpDown _numMains = new();
    private readonly NumericUpDown _numCounted = new();
    private readonly NumericUpDown _numField = new();
    private readonly ComboBox _cmbScoring = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _chkInvert = new ThemedCheckBox();
    private readonly CheckBox _chkLowerLegs = new ThemedCheckBox();
    private readonly Button _btnSeed = new() { Text = "Seed Order..." };
    private readonly Label _lblSeed = new() { AutoSize = true };
    private readonly Label _lblMains;
    private readonly Label _lblCounted;
    private readonly Label _lblField;
    private readonly Label _lblScoring;
    private readonly Label _lblSeedCaption;

    /// <summary>The seed order as it is being edited - written onto the class on OK.</summary>
    private List<string>? _seed;

    private static readonly (InvitationalScoring Scoring, string Words)[] ScoringChoices =
    {
        (InvitationalScoring.Reedy, "1st = 1 point (Reedy Race scale)"),
        (InvitationalScoring.ClassScale, "The class's qualifying points scale"),
    };

    /// <param name="raceClass">The class being edited - written onto on OK.</param>
    /// <param name="className">Its name as typed, for the title.</param>
    /// <param name="driversOf">The class's drivers by name, for the seed order. Null leaves Seed Order... with only the order already set.</param>
    public FinalsFormatsForm(RaceClass raceClass, string className, Func<string, IReadOnlyList<(string Transponder, string Name)>>? driversOf = null)
    {
        using var building = LayoutFreeze.HoldWhileBuilding();

        _raceClass = raceClass;
        _driversOf = driversOf;
        _seed = raceClass.InvitationalSeed?.ToList();

        var name = className.Trim() is { Length: > 0 } typed ? typed : "class";
        Text = $"Finals Formats - {name}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        // --- Pole Shootout ---
        _chkShootout.Text = $"Run a {PoleShootout.Name} after qualifying";
        _chkShootout.AutoSize = true;
        _chkShootout.Checked = raceClass.PoleShootout;

        _numShootoutDrivers.Minimum = PoleShootout.MinDrivers;
        _numShootoutDrivers.Maximum = PoleShootout.MaxDrivers;
        _numShootoutDrivers.Value = Math.Clamp(raceClass.PoleShootoutDrivers, PoleShootout.MinDrivers, PoleShootout.MaxDrivers);

        _numShootoutLaps.Minimum = 1;
        _numShootoutLaps.Maximum = PoleShootout.MaxTimedLaps;
        _numShootoutLaps.Value = Math.Clamp(raceClass.PoleShootoutLaps, 1, PoleShootout.MaxTimedLaps);

        var shootout = new FieldGrid();
        shootout.AddWide(_chkShootout, stretch: false);
        _lblShootoutDrivers = shootout.AddRow("Drivers:", FieldGrid.Inline(_numShootoutDrivers, Unit("top qualifiers")));
        _lblShootoutLaps = shootout.AddRow("Timed laps:", FieldGrid.Inline(_numShootoutLaps, Unit("after the out lap (best counts)")));
        shootout.AddWide(Hint(
            "Each driver runs alone, one car at a time, slowest of them first. The fastest run takes pole, and the "
            + "shootout sets the front of the A-Main grid; everyone else keeps their qualifying place. A run with no "
            + "time keeps the driver's qualifying place among them. The runs are built with the finals."));

        // --- Invitational ---
        _chkInvitational.Text = "Run the finals as an Invitational";
        _chkInvitational.AutoSize = true;
        _chkInvitational.Checked = raceClass.Invitational;

        _numMains.Minimum = Invitational.MinMains;
        _numMains.Maximum = Invitational.MaxMains;
        _numMains.Value = Math.Clamp(raceClass.InvitationalMains, Invitational.MinMains, Invitational.MaxMains);

        _numCounted.Minimum = 1;
        _numCounted.Maximum = Invitational.MaxMains;
        _numCounted.Value = Math.Clamp(raceClass.InvitationalCounted, 1, Invitational.MaxMains);

        _numField.Minimum = 0;
        _numField.Maximum = 60;
        _numField.Value = Math.Clamp(raceClass.InvitationalFieldLimit, 0, 60);

        foreach (var choice in ScoringChoices)
        {
            _cmbScoring.Items.Add(choice.Words);
        }

        var scoring = Array.FindIndex(ScoringChoices, c => c.Scoring == raceClass.InvitationalScoring);
        _cmbScoring.SelectedIndex = scoring >= 0 ? scoring : 0;

        _chkInvert.Text = "Invert the grid (points leader starts at the back)";
        _chkInvert.AutoSize = true;
        _chkInvert.Checked = raceClass.InvitationalInvertGrid;

        _btnSeed.Click += (_, _) => EditSeed(name);

        var invitational = new FieldGrid();
        invitational.AddWide(_chkInvitational, stretch: false);
        _lblMains = invitational.AddRow("Mains:", _numMains);
        _lblCounted = invitational.AddRow("Mains that count:", _numCounted);
        _lblField = invitational.AddRow("Field:", FieldGrid.Inline(_numField, Unit("drivers (0 = everyone entered)")));
        _lblScoring = invitational.AddRow("Points:", _cmbScoring);
        invitational.AddField(_chkInvert);
        _lblSeedCaption = invitational.AddRow("Seed order:", FieldGrid.Inline(_btnSeed, _lblSeed));
        invitational.AddWide(Hint(
            "The whole field races every main. Main 1 is gridded by qualifying - or by the seed order where there is "
            + "none - and each main after it by the points so far. Fewest points wins; ties go to the best single "
            + "finish, then the last main. With this on, the class's A/B/C mains, bump-ups and A-main legs are not used."));

        _chkShootout.CheckedChanged += (_, _) => UpdateEnabled();
        _chkInvitational.CheckedChanged += (_, _) => UpdateEnabled();
        _numMains.ValueChanged += (_, _) => _numCounted.Value = Math.Min(_numCounted.Value, _numMains.Value);
        _numCounted.ValueChanged += (_, _) =>
        {
            if (_numCounted.Value > _numMains.Value)
            {
                _numCounted.Value = _numMains.Value;
            }
        };

        var shootoutCard = new Section(PoleShootout.Name);
        shootoutCard.Add(Hint("The top qualifiers run alone against the clock for pole."));
        shootoutCard.Add(shootout, gapBelow: 0);

        // --- Lower mains in legs (19 Nov 2026) ---
        _chkLowerLegs.Text = "Run the lower mains in legs too";
        _chkLowerLegs.AutoSize = true;
        _chkLowerLegs.Checked = raceClass.LowerMainsInLegs;

        var lowerLegs = new FieldGrid();
        lowerLegs.AddWide(_chkLowerLegs, stretch: false);
        lowerLegs.AddWide(Hint(
            "Each lower main runs as many legs as the A-Main, with the same number counting - set by A-main: in the "
            + "class settings, so it does nothing with a single A-main. A main's bump-ups go up from its overall "
            + "result once all its legs are in, into every leg of the main above. The lower legs run first."));

        var lowerLegsCard = new Section("Lower mains in legs");
        lowerLegsCard.Add(Hint("The B, C and lower mains run in legs like the A-Main."));
        lowerLegsCard.Add(lowerLegs, gapBelow: 0);

        var invitationalCard = new Section(Invitational.Name);
        invitationalCard.Add(Hint("A series of mains for the whole field, scored on points, best ones counting."));
        invitationalCard.Add(invitational, gapBelow: 0);

        var lblIntro = new Label
        {
            Text = "More ways to run this class's finals beyond the usual mains. Each is off until ticked, and they can run together.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        var ok = new Button { Text = "OK" };
        ok.Click += (_, _) => Save();
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        var page = new FillPage();
        var body = DialogFit.Body(page);
        body.Add(lblIntro, gapBelow: UiLayout.RowGap);
        body.Add(shootoutCard);
        body.Add(invitationalCard);
        body.Add(lowerLegsCard, gapBelow: 0);

        page.Add(new ButtonBar(alignRight: true).With(cancel, ok), gapBelow: 0);
        ok.TabIndex = 0;
        cancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = ok;
        CancelButton = cancel;

        ShowSeed();
        UpdateEnabled();
        AppTheme.ApplyRecursively(this);
        AppTheme.StyleButton(ok, AppTheme.ButtonKind.Primary);

        FieldGrid.LineUp(new[] { shootout, invitational, lowerLegs });
        SizeFields();

        DialogFit.Keep(this, page, width: DialogWidth, body: body);
    }

    /// <summary>The number boxes one width, for the largest value any of them takes; the points picker as wide as its longer choice.</summary>
    private void SizeFields()
    {
        var numbers = new[] { _numShootoutDrivers, _numShootoutLaps, _numMains, _numCounted, _numField };

        var width = Math.Max(
            UiLayout.AtTextSize(UiLayout.NumericWidth),
            TextRenderer.MeasureText("00", _numMains.Font).Width + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra);

        foreach (var number in numbers)
        {
            number.Width = width;
        }

        (Font Font, int Count, int Width)? scoringWidth = null;
        _cmbScoring.Width = Math.Max(UiLayout.AtTextSize(UiLayout.FieldWidth), Pieces.PickerWidth(_cmbScoring, ref scoringWidth));
    }

    private static Label Unit(string text) => new() { Text = text, AutoSize = true };

    private static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = AppTheme.TextMuted,
        Font = AppTheme.SmallFont,
    };

    /// <summary>Everything under a tick is greyed while it is off, rather than hidden - the window keeps its shape.</summary>
    private void UpdateEnabled()
    {
        var shootout = _chkShootout.Checked;
        _numShootoutDrivers.Enabled = shootout;
        _numShootoutLaps.Enabled = shootout;
        _lblShootoutDrivers.ForeColor = _lblShootoutLaps.ForeColor = shootout ? AppTheme.TextDark : AppTheme.TextMuted;

        var invitational = _chkInvitational.Checked;
        _numMains.Enabled = _numCounted.Enabled = _numField.Enabled = invitational;
        _cmbScoring.Enabled = _chkInvert.Enabled = _btnSeed.Enabled = invitational;
        _lblMains.ForeColor = _lblCounted.ForeColor = _lblField.ForeColor = _lblScoring.ForeColor = _lblSeedCaption.ForeColor
            = invitational ? AppTheme.TextDark : AppTheme.TextMuted;
    }

    /// <summary>Beside the button: whose order the field is seeded in when there is no qualifying.</summary>
    private void ShowSeed() =>
        _lblSeed.Text = _seed is { Count: > 0 } ? $"Set ({Plural.Of(_seed.Count, "driver")})" : "Roster order";

    private void EditSeed(string className)
    {
        var drivers = (_driversOf?.Invoke(_raceClass.Name) ?? Array.Empty<(string, string)>()).ToList();

        using var form = new SeedOrderForm(className, InvitationalSeedRows(drivers, _seed));

        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _seed = form.Order.Count > 0 ? form.Order.ToList() : null;
            ShowSeed();
        }
    }

    /// <summary>
    /// The seed order's rows: the order already set first (a driver no
    /// longer in the class kept by transponder), then the class's other
    /// drivers in roster order.
    /// </summary>
    internal static List<(string Transponder, string Name)> InvitationalSeedRows(
        IReadOnlyList<(string Transponder, string Name)> drivers, IReadOnlyList<string>? seed)
    {
        var byTransponder = drivers
            .GroupBy(d => d.Transponder, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

        var rows = new List<(string, string)>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in (seed ?? Array.Empty<string>()).Concat(drivers.Select(d => d.Transponder)))
        {
            if (!string.IsNullOrWhiteSpace(t) && taken.Add(t))
            {
                rows.Add((t, byTransponder.TryGetValue(t, out var n) && n.Length > 0 ? n : t));
            }
        }

        return rows;
    }

    private void Save()
    {
        _raceClass.PoleShootout = _chkShootout.Checked;
        _raceClass.PoleShootoutDrivers = (int)_numShootoutDrivers.Value;
        _raceClass.PoleShootoutLaps = (int)_numShootoutLaps.Value;

        _raceClass.Invitational = _chkInvitational.Checked;
        _raceClass.InvitationalMains = (int)_numMains.Value;
        _raceClass.InvitationalCounted = (int)Math.Min(_numCounted.Value, _numMains.Value);
        _raceClass.InvitationalFieldLimit = (int)_numField.Value;
        _raceClass.InvitationalScoring = ScoringChoices[Math.Clamp(_cmbScoring.SelectedIndex, 0, ScoringChoices.Length - 1)].Scoring;
        _raceClass.InvitationalInvertGrid = _chkInvert.Checked;
        _raceClass.InvitationalSeed = _seed;
        _raceClass.LowerMainsInLegs = _chkLowerLegs.Checked;

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// The Invitational's seed order (Seed Order...): the class's drivers in
    /// the order the field is seeded when they have no qualifying result,
    /// moved up and down - the Tech Checklist's shape, a list taking the
    /// height with its buttons in a column beside it (Beside), OK and Cancel
    /// at the foot. Internal, so the layout test can build it without a click.
    /// </summary>
    internal sealed class SeedOrderForm : Form
    {
        private const int LeastRows = 6;

        private readonly ListBox _list = new();
        private readonly List<string> _transponders = new();

        public SeedOrderForm(string className, IReadOnlyList<(string Transponder, string Name)> rows)
        {
            using var building = LayoutFreeze.HoldWhileBuilding();

            Text = $"Seed Order - {(className.Trim() is { Length: > 0 } n ? n : "class")}";
            Font = AppTheme.BodyFont;
            BackColor = AppTheme.Background;
            ForeColor = AppTheme.TextDark;

            _list.BackColor = AppTheme.Surface;
            _list.IntegralHeight = false;

            foreach (var (transponder, name) in rows)
            {
                _transponders.Add(transponder);
                _list.Items.Add(name);
            }

            if (_list.Items.Count > 0)
            {
                _list.SelectedIndex = 0;
            }

            var lblHint = new Label
            {
                Text = "The order the Invitational is seeded in for drivers with no qualifying result - and for the whole "
                     + "field when the class has not qualified. Top of the list starts at the front of main 1."
                     + (_list.Items.Count == 0 ? " No drivers are in this class yet." : string.Empty),
                AutoSize = true,
                ForeColor = AppTheme.TextMuted,
                Font = AppTheme.SmallFont,
            };

            var btnTop = new Button { Text = "To the Top" };
            btnTop.Click += (_, _) => Move(-_list.SelectedIndex);
            var btnUp = new Button { Text = "Move Up" };
            btnUp.Click += (_, _) => Move(-1);
            var btnDown = new Button { Text = "Move Down" };
            btnDown.Click += (_, _) => Move(1);
            var btnClear = new Button { Text = "Roster Order" };
            btnClear.Click += (_, _) =>
            {
                Cleared = true;
                DialogResult = DialogResult.OK;
                Close();
            };

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

            var order = new Beside();
            order.AddFill(_list, _ => LeastRows * _list.ItemHeight + 4);
            order.AddSide(btnTop, gapBelow: UiLayout.ButtonGap);
            order.AddSide(btnUp, gapBelow: UiLayout.ButtonGap);
            order.AddSide(btnDown, gapBelow: UiLayout.SectionGap);
            order.AddSide(btnClear, gapBelow: 0);

            var card = new Section("Seed order");
            card.Add(lblHint, gapBelow: UiLayout.RowGap);
            card.AddFill(order, () => order.LeastHeight(Math.Max(1, order.Width)), gapBelow: 0);

            var page = new FillPage();
            page.AddFill(card, card.LeastHeight);
            page.Add(new ButtonBar(alignRight: true).With(cancel, ok), gapBelow: 0);
            ok.TabIndex = 0;
            cancel.TabIndex = 1;

            Controls.Add(page);

            AcceptButton = ok;
            CancelButton = cancel;

            AppTheme.ApplyRecursively(this);
            AppTheme.StyleButton(ok, AppTheme.ButtonKind.Primary);

            DialogFit.Keep(this, page, width: 492, height: 480);
        }

        /// <summary>Roster Order was pressed: the class goes back to its roster order.</summary>
        public bool Cleared { get; private set; }

        /// <summary>The order, by transponder - empty after Roster Order. Only meaningful after DialogResult.OK.</summary>
        public IReadOnlyList<string> Order => Cleared ? Array.Empty<string>() : _transponders;

        private void Move(int by)
        {
            var index = _list.SelectedIndex;
            var target = index + by;

            if (index < 0 || by == 0 || target < 0 || target >= _list.Items.Count)
            {
                return;
            }

            var item = _list.Items[index];
            var transponder = _transponders[index];

            _list.Items.RemoveAt(index);
            _transponders.RemoveAt(index);
            _list.Items.Insert(target, item);
            _transponders.Insert(target, transponder);
            _list.SelectedIndex = target;
        }
    }
}
