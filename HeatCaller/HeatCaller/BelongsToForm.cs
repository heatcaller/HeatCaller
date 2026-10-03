namespace HeatCaller;

/// <summary>
/// "Who is on transponder B70E21?" - gives a transponder nobody has to a
/// driver already on file: pick them, say whether it is fitted to one of
/// their cars or moves between cars, and whether it replaces one of theirs
/// (which is then retired, its laps staying theirs).
///
/// Only asks; the caller makes the change (MainForm.GiveTransponderTo), so
/// it can take its undo step.
///
/// On the layout pieces (DialogFit): the question at the top, the search
/// and the drivers taking the height, the choices under them and the
/// buttons at the foot.
/// </summary>
internal sealed class BelongsToForm : Form
{
    /// <summary>The drivers show at least this many before the window scrolls instead.</summary>
    private const int LeastRows = 5;

    private readonly string _transponder;
    private readonly ProfileStore _profiles;
    private readonly CarStore _cars;

    private readonly TextBox _txtFind = new();
    private readonly ListBox _lstDrivers = new();
    private readonly RadioButton _rdoInCar = new ThemedRadioButton();
    private readonly ComboBox _cmbCar = new();
    private readonly RadioButton _rdoMoves = new ThemedRadioButton();
    private readonly ComboBox _cmbReplaces = new();
    private readonly Label _lblOutcome = new() { AutoSize = true };
    private readonly Button _btnGive = new();

    /// <summary>The driver picked - their main transponder, the profile's key. Set on OK.</summary>
    public string DriverKey { get; private set; } = string.Empty;

    /// <summary>The car it is fitted to (Car.Id), or empty when it moves between cars. Set on OK.</summary>
    public string CarId { get; private set; } = string.Empty;

    /// <summary>The driver's transponder it replaces, to be retired, or empty. Set on OK.</summary>
    public string Replaces { get; private set; } = string.Empty;

    public BelongsToForm(string transponder, string whatItDid, ProfileStore profiles, CarStore cars)
    {
        _transponder = transponder;
        _profiles = profiles;
        _cars = cars;

        Text = $"Who is on transponder {transponder}?";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var lblWhat = new Label { Text = whatItDid, AutoSize = true };

        _txtFind.PlaceholderText = "name, number or transponder";
        _txtFind.TextChanged += (_, _) => FillDrivers();

        _lstDrivers.IntegralHeight = false;
        _lstDrivers.SelectedIndexChanged += (_, _) => OnDriverPicked();
        _lstDrivers.DoubleClick += (_, _) => Give();

        _rdoInCar.Text = "In one of their cars:";
        _rdoInCar.AutoSize = true;
        _cmbCar.DropDownStyle = ComboBoxStyle.DropDownList;
        // The two are in different containers (one sits beside its car
        // list), and WinForms only groups radio buttons that share one - so
        // each clears the other itself.
        _rdoInCar.CheckedChanged += (_, _) =>
        {
            _cmbCar.Enabled = _rdoInCar.Checked;

            if (_rdoInCar.Checked)
            {
                _rdoMoves.Checked = false;
            }
        };

        _rdoMoves.Text = "Their own transponder - it moves between cars";
        _rdoMoves.AutoSize = true;
        _rdoMoves.Checked = true;
        _rdoMoves.CheckedChanged += (_, _) =>
        {
            if (_rdoMoves.Checked)
            {
                _rdoInCar.Checked = false;
            }
        };

        _cmbReplaces.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbReplaces.SelectedIndexChanged += (_, _) => UpdateOutcome();

        _btnGive.Text = "Give to Driver";
        _btnGive.Click += (_, _) => Give();

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        var where = new FieldGrid();
        where.AddField(ButtonsBeside(_rdoInCar, _cmbCar));
        where.AddField(_rdoMoves);
        where.AddRow("It replaces:", _cmbReplaces);
        where.AddHint("A replaced transponder is retired: it stays theirs, and so does everything timed on it.");

        var find = new FieldGrid();
        find.AddRow("Find:", _txtFind, stretch: true);

        var page = new FillPage();
        page.Add(lblWhat, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(find, gapBelow: UiLayout.HintGap);
        page.AddFill(_lstDrivers, _ => LeastRows * _lstDrivers.ItemHeight + 4, gapBelow: UiLayout.RowGap);
        page.Add(new Label { Text = "Where is it?", AutoSize = true, Font = AppTheme.BoldFont }, stretch: false, gapBelow: UiLayout.HintGap);
        page.Add(where);
        page.Add(_lblOutcome, stretch: false);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, _btnGive), gapBelow: 0);

        Controls.Add(page);
        CancelButton = btnCancel;
        AcceptButton = _btnGive;

        FillDrivers();

        AppTheme.ApplyRecursively(this);
        _lblOutcome.ForeColor = AppTheme.TextMuted;

        // After the theme pass, in the faces it leaves them in.
        _cmbCar.Width = UiLayout.AtTextSize(220);
        _cmbReplaces.Width = UiLayout.AtTextSize(220);

        DialogFit.Keep(this, page, width: 520, height: 560);
    }

    /// <summary>One driver in the list.</summary>
    private sealed record DriverChoice(DriverProfile Profile, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>One line of a drop-down with a value behind it.</summary>
    private sealed record Item(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    private static string NameOf(DriverProfile profile) =>
        string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();

    /// <summary>The drivers matching what is typed - by name, announcer name, number or any of their transponders.</summary>
    private void FillDrivers()
    {
        var find = _txtFind.Text.Trim();
        var picked = (_lstDrivers.SelectedItem as DriverChoice)?.Profile.Transponder;

        bool Has(string? value) => !string.IsNullOrEmpty(value) && value.Contains(find, StringComparison.OrdinalIgnoreCase);

        var matches = _profiles.All
            .Where(p => find.Length == 0
                        || Has(p.DriverName) || Has(p.AnnouncerName) || Has(p.RaceNumber)
                        || p.AllTransponderIds.Any(Has))
            .OrderBy(NameOf, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _lstDrivers.BeginUpdate();
        _lstDrivers.Items.Clear();

        foreach (var profile in matches)
        {
            var number = string.IsNullOrWhiteSpace(profile.RaceNumber) ? string.Empty : $"  #{profile.RaceNumber.Trim()}";
            var cars = _cars.OwnedBy(profile).Count(c => !c.Retired);
            var carText = cars == 1 ? "1 car" : $"{cars} cars";
            _lstDrivers.Items.Add(new DriverChoice(profile, $"{NameOf(profile)}{number}  ·  {profile.Transponder}  ·  {carText}"));
        }

        _lstDrivers.EndUpdate();

        var again = _lstDrivers.Items.Cast<DriverChoice>()
            .FirstOrDefault(c => string.Equals(c.Profile.Transponder, picked, StringComparison.OrdinalIgnoreCase));

        if (again is not null)
        {
            _lstDrivers.SelectedItem = again;
        }
        else if (_lstDrivers.Items.Count == 1)
        {
            _lstDrivers.SelectedIndex = 0;
        }

        OnDriverPicked();
    }

    /// <summary>Fills the car and "replaces" lists for the driver picked.</summary>
    private void OnDriverPicked()
    {
        var profile = (_lstDrivers.SelectedItem as DriverChoice)?.Profile;

        _cmbCar.Items.Clear();
        _cmbReplaces.Items.Clear();
        _cmbReplaces.Items.Add(new Item(string.Empty, "Nothing - it's an extra one"));

        if (profile is not null)
        {
            foreach (var car in _cars.OwnedBy(profile).Where(c => !c.Retired))
            {
                _cmbCar.Items.Add(new Item(car.Id, car.DisplayName));
            }

            foreach (var id in profile.AllTransponderIds.Where(id => !profile.IsRetired(id)))
            {
                _cmbReplaces.Items.Add(new Item(id, id));
            }
        }

        _rdoInCar.Enabled = _cmbCar.Items.Count > 0;

        if (_cmbCar.Items.Count > 0)
        {
            _cmbCar.SelectedIndex = 0;
        }
        else
        {
            _rdoMoves.Checked = true;
        }

        _cmbCar.Enabled = _rdoInCar.Checked && _cmbCar.Items.Count > 0;
        _cmbReplaces.SelectedIndex = 0;

        _btnGive.Enabled = profile is not null;
        _btnGive.Text = profile is null ? "Give to Driver" : $"Give {_transponder} to {NameOf(profile)}";

        UpdateOutcome();
    }

    /// <summary>The line saying what pressing the button will do.</summary>
    private void UpdateOutcome()
    {
        var profile = (_lstDrivers.SelectedItem as DriverChoice)?.Profile;

        if (profile is null)
        {
            _lblOutcome.Text = "Pick the driver it belongs to.";
            return;
        }

        var replaces = (_cmbReplaces.SelectedItem as Item)?.Value ?? string.Empty;
        var retire = replaces.Length > 0 ? $" {replaces} is retired." : string.Empty;

        _lblOutcome.Text = $"Laps timed on {_transponder} will count for {NameOf(profile)}.{retire}";
    }

    private void Give()
    {
        if (_lstDrivers.SelectedItem is not DriverChoice choice)
        {
            return;
        }

        DriverKey = choice.Profile.Transponder;
        CarId = _rdoInCar.Checked ? (_cmbCar.SelectedItem as Item)?.Value ?? string.Empty : string.Empty;
        Replaces = (_cmbReplaces.SelectedItem as Item)?.Value ?? string.Empty;

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>A control and a drop-down beside it, the drop-down centred up and down on it.</summary>
    private static ButtonBar ButtonsBeside(Control first, ComboBox picker)
    {
        picker.Anchor = AnchorStyles.Left;
        first.Anchor = AnchorStyles.Left;
        return new ButtonBar(tight: true).With(first, picker);
    }
}
