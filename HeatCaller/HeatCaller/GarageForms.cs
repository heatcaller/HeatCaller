namespace HeatCaller;

/// <summary>
/// One car: what it is, and whose it is.
///
/// The owner picker carries "The club" as a real choice rather than
/// leaving it as an empty selection, because a club car is a deliberate
/// thing and not a car somebody forgot to assign - see Car.OwnerTransponder.
/// </summary>
internal sealed class CarForm : Form
{
    private readonly Car _car;

    private readonly TextBox _txtName = new();
    private readonly ComboBox _cmbOwner = new();
    private readonly ComboBox _cmbClass = new();
    private readonly ComboBox _cmbChassis = new();
    private readonly TextBox _txtShell = new();
    private readonly TextBox _txtNumber = new();
    private readonly TextBox _txtNotes = new();

    /// <summary>The car as edited. The instance handed in, written back into - the caller kept its own copy for the undo.</summary>
    public Car Result => _car;

    private sealed record OwnerChoice(string Transponder, string Caption)
    {
        public override string ToString() => Caption;
    }

    private sealed record ChassisChoice(string Id, string Caption)
    {
        public override string ToString() => Caption;
    }

    public CarForm(Car car, ChassisTypeStore chassisTypes, ProfileStore profiles, RaceClassStore raceClasses, bool isNew)
    {
        _car = car;

        Text = isNew ? "Add a Car" : "Edit Car";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = AppTheme.BodyFont;

        const int fieldWidth = 320;
        var right = UiLayout.FieldLeft + fieldWidth;

        var y = UiLayout.PageTop;

        Label Row(string caption, Control field, int width)
        {
            // Parented before it is measured. A label measures itself in
            // the font it currently has, and until it is added to this form
            // it has the WinForms default rather than this one - so a
            // position worked out first is a position for a control that is
            // about to change size.
            var label = new Label { Text = caption, AutoSize = true, Font = AppTheme.BodyFont };

            Controls.Add(label);
            label.Location = new Point(UiLayout.PageLeft, UiLayout.CentreOn(label, y, UiLayout.RowHeight));

            field.Location = new Point(UiLayout.FieldLeft, y);
            field.Width = width;

            Controls.Add(field);

            y += UiLayout.RowStep;

            return label;
        }

        _txtName.Text = car.Name;
        _txtName.MaxLength = 60;
        Row("Name:", _txtName, fieldWidth);

        var lblNameHint = new Label
        {
            Text = "What people call it - \"the red one\", \"Dave's Audi\". Left blank, the shell is used instead.",
            Location = new Point(UiLayout.FieldLeft, y - UiLayout.RowStep + UiLayout.RowHeight + UiLayout.HintGap),
            AutoSize = true,
            MaximumSize = new Size(fieldWidth, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        Controls.Add(lblNameHint);
        y = UiLayout.Below(lblNameHint, UiLayout.RowGap);

        _cmbOwner.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbOwner.Items.Add(new OwnerChoice(string.Empty, "The club"));

        foreach (var profile in profiles.All.OrderBy(p => p.DriverName, StringComparer.CurrentCultureIgnoreCase))
        {
            var name = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName;
            _cmbOwner.Items.Add(new OwnerChoice(profile.Transponder, name));
        }

        _cmbOwner.SelectedItem = _cmbOwner.Items
            .Cast<OwnerChoice>()
            .FirstOrDefault(o => string.Equals(o.Transponder, car.OwnerTransponder, StringComparison.OrdinalIgnoreCase))
            ?? _cmbOwner.Items[0];

        Row("Owner:", _cmbOwner, fieldWidth);

        // Editable rather than a fixed list: a club can put a car in a
        // class it has not set up yet, and the Garage groups on the name
        // either way. Refusing here would mean the class had to be created
        // before the car could be described, which is the wrong order.
        _cmbClass.DropDownStyle = ComboBoxStyle.DropDown;
        _cmbClass.Items.Add(string.Empty);

        foreach (var raceClass in raceClasses.All)
        {
            _cmbClass.Items.Add(raceClass.Name);
        }

        _cmbClass.Text = car.ClassName;

        Row("Class:", _cmbClass, fieldWidth);

        _cmbChassis.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbChassis.Items.Add(new ChassisChoice(string.Empty, "(not said)"));

        foreach (var type in chassisTypes.InUse)
        {
            _cmbChassis.Items.Add(new ChassisChoice(type.Id, type.DisplayName));
        }

        // A chassis that has since been retired is still this car's, so it
        // is added back rather than silently becoming "(not said)".
        if (!string.IsNullOrWhiteSpace(car.ChassisTypeId)
            && chassisTypes.Get(car.ChassisTypeId) is { Retired: true } retired)
        {
            _cmbChassis.Items.Add(new ChassisChoice(retired.Id, retired.DisplayName + "  (retired)"));
        }

        _cmbChassis.SelectedItem = _cmbChassis.Items
            .Cast<ChassisChoice>()
            .FirstOrDefault(c => string.Equals(c.Id, car.ChassisTypeId, StringComparison.Ordinal))
            ?? _cmbChassis.Items[0];

        Row("Chassis:", _cmbChassis, fieldWidth);

        _txtShell.Text = car.Bodyshell;
        _txtShell.MaxLength = 60;
        _txtShell.PlaceholderText = "e.g. Audi R8";
        Row("Shell:", _txtShell, fieldWidth);

        _txtNumber.Text = car.RaceNumber;
        _txtNumber.MaxLength = 8;
        Row("Race number:", _txtNumber, UiLayout.NumericWidth);

        _txtNotes.Text = car.Notes;
        _txtNotes.Multiline = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;
        _txtNotes.Height = AppTheme.BodyFont.Height * 3 + 8;

        var lblNotes = new Label
        {
            Text = "Notes:",
            Location = new Point(UiLayout.PageLeft, y),
            AutoSize = true,
        };

        _txtNotes.Location = new Point(UiLayout.FieldLeft, y);
        _txtNotes.Width = fieldWidth;

        Controls.Add(lblNotes);
        Controls.Add(_txtNotes);

        y = UiLayout.Below(_txtNotes, UiLayout.SectionGap);

        var btnOk = new Button { Text = "Save", Font = AppTheme.BodyFont, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", Font = AppTheme.BodyFont, DialogResult = DialogResult.Cancel };

        btnOk.Size = new Size(UiLayout.WidthFor(btnOk), UiLayout.ButtonHeight);
        btnCancel.Size = new Size(UiLayout.WidthFor(btnCancel), UiLayout.ButtonHeight);

        btnCancel.Location = new Point(right - btnCancel.Width, y);
        btnOk.Location = new Point(btnCancel.Left - UiLayout.ButtonGap - btnOk.Width, y);

        Controls.Add(btnOk);
        Controls.Add(btnCancel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        // Read back here rather than in a Click handler, so closing with
        // Enter and closing with the button do exactly the same thing.
        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_txtName.Text) && string.IsNullOrWhiteSpace(_txtShell.Text))
            {
                AppDialog.Show(this,
                    "Give the car a name or a shell, so it can be told apart from the others in the list.",
                    "It needs something to call it", MessageBoxButtons.OK, MessageBoxIcon.Information);

                e.Cancel = true;
                return;
            }

            _car.Name = _txtName.Text.Trim();
            _car.OwnerTransponder = (_cmbOwner.SelectedItem as OwnerChoice)?.Transponder ?? string.Empty;
            _car.ClassName = _cmbClass.Text.Trim();
            _car.ChassisTypeId = (_cmbChassis.SelectedItem as ChassisChoice)?.Id ?? string.Empty;
            _car.Bodyshell = _txtShell.Text.Trim();
            _car.RaceNumber = _txtNumber.Text.Trim();
            _car.Notes = _txtNotes.Text.Trim();

            // A car that is no longer anybody's cannot be out on loan to
            // somebody. Cleared here rather than left to puzzle over later.
            if (_car.IsClubCar && string.IsNullOrWhiteSpace(_car.LoanedToTransponder))
            {
                _car.LoanedForEventId = string.Empty;
            }
        };

        AppTheme.ApplyRecursively(this);

        ClientSize = new Size(right + UiLayout.PageLeft, UiLayout.Below(btnOk, UiLayout.PageTop));
    }
}

/// <summary>
/// A car's photographs: add, remove, and say which one leads.
///
/// Pictures are copied into the app's own folder at import (see
/// PhotoStore) rather than referenced where they sit, so a car keeps its
/// photos the day somebody tidies their Downloads folder.
///
/// Removing one here takes it off the CAR but leaves the file alone. That
/// is deliberate: the caller captures an undo entry that puts the names
/// back, and a name whose file had been deleted would put back a broken
/// picture. A few kilobytes is the cheaper mistake.
/// </summary>
internal sealed class CarPhotosForm : Form
{
    private readonly Car _car;
    private readonly ListBox _list = new();
    private readonly PictureBox _preview = new();
    private readonly Button _btnRemove = new();
    private readonly Button _btnFirst = new();

    private Image? _shown;

    public Car Result => _car;

    public CarPhotosForm(Car car)
    {
        _car = car;

        Text = $"Photos - {car.DisplayName}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = AppTheme.BodyFont;

        const int width = 620;

        var lblIntro = new Label
        {
            Text = "Pictures are copied into the app, so they stay with the car. The first one is the picture the car is "
                 + "shown by.",
            Location = new Point(UiLayout.PageLeft, UiLayout.PageTop),
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        Controls.Add(lblIntro);

        var y = UiLayout.Below(lblIntro, UiLayout.RowGap);

        _list.Location = new Point(UiLayout.PageLeft, y);
        _list.Size = new Size(220, 260);
        _list.SelectedIndexChanged += (_, _) => ShowSelected();

        _preview.Location = new Point(_list.Right + UiLayout.ButtonGap, y);
        _preview.Size = new Size(width - 220 - UiLayout.ButtonGap, 260);
        _preview.SizeMode = PictureBoxSizeMode.Zoom;
        _preview.BackColor = AppTheme.Background;

        Controls.Add(_list);
        Controls.Add(_preview);

        y = UiLayout.Below(_list, UiLayout.RowGap);

        var btnAdd = new Button { Text = "Add a Photo...", Font = AppTheme.BodyFont };
        btnAdd.Click += (_, _) => AddPhoto();

        _btnRemove.Text = "Remove";
        _btnRemove.Font = AppTheme.BodyFont;
        _btnRemove.Click += (_, _) => RemoveSelected();

        _btnFirst.Text = "Make it First";
        _btnFirst.Font = AppTheme.BodyFont;
        _btnFirst.Click += (_, _) => MakeFirst();

        var btnDone = new Button { Text = "Done", Font = AppTheme.BodyFont, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", Font = AppTheme.BodyFont, DialogResult = DialogResult.Cancel };

        foreach (var button in new[] { btnAdd, _btnRemove, _btnFirst, btnDone, btnCancel })
        {
            button.Size = new Size(UiLayout.WidthFor(button), UiLayout.ButtonHeight);
        }

        btnAdd.Location = new Point(UiLayout.PageLeft, y);
        _btnRemove.Location = new Point(btnAdd.Right + UiLayout.ButtonGap, y);
        _btnFirst.Location = new Point(_btnRemove.Right + UiLayout.ButtonGap, y);

        var right = UiLayout.PageLeft + width;

        btnCancel.Location = new Point(right - btnCancel.Width, y);
        btnDone.Location = new Point(btnCancel.Left - UiLayout.ButtonGap - btnDone.Width, y);

        Controls.Add(btnAdd);
        Controls.Add(_btnRemove);
        Controls.Add(_btnFirst);
        Controls.Add(btnDone);
        Controls.Add(btnCancel);

        AcceptButton = btnDone;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        // After the theme pass - the preview stays neutral whatever the app
        // is wearing, so a photo is judged on its own.
        _preview.BackColor = AppTheme.Background;

        ClientSize = new Size(right + UiLayout.PageLeft, UiLayout.Below(btnDone, UiLayout.PageTop));

        Reload();
    }

    private void Reload()
    {
        var selected = _list.SelectedIndex;

        _list.BeginUpdate();
        _list.Items.Clear();

        for (var i = 0; i < _car.PhotoFileNames.Count; i++)
        {
            _list.Items.Add(i == 0 ? "Photo 1  (shown first)" : $"Photo {i + 1}");
        }

        _list.EndUpdate();

        if (_list.Items.Count > 0)
        {
            _list.SelectedIndex = Math.Clamp(selected, 0, _list.Items.Count - 1);
        }
        else
        {
            ShowSelected();
        }

        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var have = _list.SelectedIndex >= 0;

        _btnRemove.Enabled = have;
        _btnFirst.Enabled = have && _list.SelectedIndex > 0;
    }

    private void ShowSelected()
    {
        // Disposed before the next one is loaded: these are full-size
        // images rather than cached thumbnails, and a dialog left open
        // while somebody clicks down a list would otherwise hold every
        // one of them.
        _shown?.Dispose();
        _shown = null;

        var index = _list.SelectedIndex;

        if (index >= 0 && index < _car.PhotoFileNames.Count)
        {
            _shown = PhotoStore.Cars.Full(_car.PhotoFileNames[index]);
        }

        _preview.Image = _shown;
        UpdateButtons();
    }

    private void AddPhoto()
    {
        using var open = new OpenFileDialog
        {
            Title = "Choose a photo of the car",
            Filter = PhotoStore.PickerFilter,
            CheckFileExists = true,
        };

        if (open.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var stored = PhotoStore.Cars.Import(open.FileName, "use as a car photo", out var error);

        if (stored is null)
        {
            AppDialog.Show(this,
                error ?? "That picture could not be read.",
                "Could not add the photo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _car.PhotoFileNames.Add(stored);
        Reload();
        _list.SelectedIndex = _car.PhotoFileNames.Count - 1;
    }

    private void RemoveSelected()
    {
        var index = _list.SelectedIndex;

        if (index < 0 || index >= _car.PhotoFileNames.Count)
        {
            return;
        }

        // The file is left where it is - see the class note.
        _car.PhotoFileNames.RemoveAt(index);
        Reload();
    }

    private void MakeFirst()
    {
        var index = _list.SelectedIndex;

        if (index <= 0 || index >= _car.PhotoFileNames.Count)
        {
            return;
        }

        var name = _car.PhotoFileNames[index];
        _car.PhotoFileNames.RemoveAt(index);
        _car.PhotoFileNames.Insert(0, name);

        Reload();
        _list.SelectedIndex = 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _preview.Image = null;
            _shown?.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Lending a car for one race day.
///
/// One night, never open-ended: the loan carries the race day it is for,
/// so it ends when the night does and there is no state anybody has to
/// remember to clear. See Car.DriverFor.
/// </summary>
internal sealed class LendCarForm : Form
{
    private readonly ComboBox _cmbTo = new();

    /// <summary>Who is taking it, or empty to hand it back.</summary>
    public string ToTransponder { get; private set; } = string.Empty;

    private sealed record DriverChoice(string Transponder, string Caption)
    {
        public override string ToString() => Caption;
    }

    public LendCarForm(Car car, ProfileStore profiles, string dayName, string ownerName)
    {
        Text = "Lend a Car";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = AppTheme.BodyFont;

        const int width = 420;

        var owner = car.IsClubCar
            ? "This is a club car."
            : $"This is {(string.IsNullOrWhiteSpace(ownerName) ? "someone's" : ownerName + "'s")} car.";

        var lblIntro = new Label
        {
            Text = $"{owner} Lending it applies to \"{dayName}\" only - it goes back to its owner when the day ends.",
            Location = new Point(UiLayout.PageLeft, UiLayout.PageTop),
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        Controls.Add(lblIntro);

        var y = UiLayout.Below(lblIntro, UiLayout.RowGap);

        // Parented before it is measured - see the note in CarForm's Row.
        var lblTo = new Label { Text = "Lend it to:", AutoSize = true, Font = AppTheme.BodyFont };

        Controls.Add(lblTo);
        lblTo.Location = new Point(UiLayout.PageLeft, UiLayout.CentreOn(lblTo, y, UiLayout.RowHeight));

        _cmbTo.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTo.Location = new Point(UiLayout.FieldLeft, y);
        _cmbTo.Width = width - (UiLayout.FieldLeft - UiLayout.PageLeft);

        _cmbTo.Items.Add(new DriverChoice(string.Empty, "Nobody - it stays with its owner"));

        // AllRaceable, not All: a car is a thing that physically exists and
        // a ghost cannot be lent one. ProfileStore says so itself.
        foreach (var profile in profiles.All
                     .Where(p => !string.Equals(p.Transponder, car.OwnerTransponder, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(p => p.DriverName, StringComparer.CurrentCultureIgnoreCase))
        {
            var name = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName;
            _cmbTo.Items.Add(new DriverChoice(profile.Transponder, name));
        }

        _cmbTo.SelectedItem = _cmbTo.Items
            .Cast<DriverChoice>()
            .FirstOrDefault(d => string.Equals(d.Transponder, car.LoanedToTransponder, StringComparison.OrdinalIgnoreCase))
            ?? _cmbTo.Items[0];

        Controls.Add(_cmbTo);

        y = UiLayout.Below(_cmbTo, UiLayout.SectionGap);

        var btnOk = new Button { Text = "Save", Font = AppTheme.BodyFont, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", Font = AppTheme.BodyFont, DialogResult = DialogResult.Cancel };

        btnOk.Size = new Size(UiLayout.WidthFor(btnOk), UiLayout.ButtonHeight);
        btnCancel.Size = new Size(UiLayout.WidthFor(btnCancel), UiLayout.ButtonHeight);

        var right = UiLayout.PageLeft + width;

        btnCancel.Location = new Point(right - btnCancel.Width, y);
        btnOk.Location = new Point(btnCancel.Left - UiLayout.ButtonGap - btnOk.Width, y);

        Controls.Add(btnOk);
        Controls.Add(btnCancel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        FormClosing += (_, _) =>
        {
            if (DialogResult == DialogResult.OK)
            {
                ToTransponder = (_cmbTo.SelectedItem as DriverChoice)?.Transponder ?? string.Empty;
            }
        };

        AppTheme.ApplyRecursively(this);

        ClientSize = new Size(right + UiLayout.PageLeft, UiLayout.Below(btnOk, UiLayout.PageTop));
    }
}

/// <summary>
/// The club's chassis list.
///
/// Small on purpose. A chassis is a name, a maker and - optionally - the
/// setup sheet a car on it should reach for, which is the one place the
/// physical car and the setup-sheet schema meet. Retiring rather than
/// deleting is offered first, because a chassis nobody runs any more is
/// still what last season's cars were.
/// </summary>
internal sealed class ChassisTypesForm : Form
{
    private readonly ChassisTypeStore _types;
    private readonly ChassisTemplateStore _sheets;
    private readonly CarStore _cars;

    private readonly ListBox _list = new();
    private readonly TextBox _txtName = new();
    private readonly TextBox _txtMaker = new();
    private readonly ComboBox _cmbSheet = new();
    private readonly Button _btnRetire = new();
    private readonly Button _btnDelete = new();

    private sealed record SheetChoice(string Id, string Caption)
    {
        public override string ToString() => Caption;
    }

    public ChassisTypesForm(ChassisTypeStore types, ChassisTemplateStore sheets, CarStore cars)
    {
        _types = types;
        _sheets = sheets;
        _cars = cars;

        Text = "Chassis Types";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = AppTheme.BodyFont;

        const int width = 600;

        var lblIntro = new Label
        {
            Text = "The chassis a car can be. This list is the club's own - the ones here to begin with are a starting "
                 + "point, not a rulebook.",
            Location = new Point(UiLayout.PageLeft, UiLayout.PageTop),
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        Controls.Add(lblIntro);

        var y = UiLayout.Below(lblIntro, UiLayout.RowGap);

        _list.Location = new Point(UiLayout.PageLeft, y);
        _list.Size = new Size(240, 230);
        _list.SelectedIndexChanged += (_, _) => LoadSelected();

        Controls.Add(_list);

        var fieldsLeft = _list.Right + UiLayout.SectionGap;
        var fieldWidth = width - (fieldsLeft - UiLayout.PageLeft);
        var fieldY = y;

        void Field(string caption, Control field)
        {
            var label = new Label
            {
                Text = caption,
                Location = new Point(fieldsLeft, fieldY),
                AutoSize = true,
                Font = AppTheme.SmallFont,
                ForeColor = AppTheme.TextMuted,
            };

            Controls.Add(label);

            field.Location = new Point(fieldsLeft, UiLayout.Below(label, UiLayout.HintGap));
            field.Width = fieldWidth;

            Controls.Add(field);

            fieldY = UiLayout.Below(field, UiLayout.RowGap);
        }

        _txtName.MaxLength = 40;
        Field("Name", _txtName);

        _txtMaker.MaxLength = 40;
        Field("Maker", _txtMaker);

        _cmbSheet.DropDownStyle = ComboBoxStyle.DropDownList;
        Field("Setup sheet to use", _cmbSheet);

        var lblSheetHint = new Label
        {
            Text = "Logging a setup for a car on this chassis opens on that sheet.",
            Location = new Point(fieldsLeft, fieldY),
            AutoSize = true,
            MaximumSize = new Size(fieldWidth, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        Controls.Add(lblSheetHint);

        y = Math.Max(UiLayout.Below(_list, UiLayout.RowGap), UiLayout.Below(lblSheetHint, UiLayout.RowGap));

        var btnAdd = new Button { Text = "Add", Font = AppTheme.BodyFont };
        btnAdd.Click += (_, _) => AddType();

        var btnSave = new Button { Text = "Save Changes", Font = AppTheme.BodyFont };
        btnSave.Click += (_, _) => SaveSelected();

        _btnRetire.Text = "Retire";
        _btnRetire.Font = AppTheme.BodyFont;
        _btnRetire.Click += (_, _) => RetireSelected();

        _btnDelete.Text = "Delete";
        _btnDelete.Font = AppTheme.BodyFont;
        _btnDelete.Click += (_, _) => DeleteSelected();

        var btnClose = new Button { Text = "Close", Font = AppTheme.BodyFont, DialogResult = DialogResult.OK };

        foreach (var button in new[] { btnAdd, btnSave, _btnRetire, _btnDelete, btnClose })
        {
            button.Size = new Size(UiLayout.WidthFor(button), UiLayout.ButtonHeight);
        }

        btnAdd.Location = new Point(UiLayout.PageLeft, y);
        btnSave.Location = new Point(btnAdd.Right + UiLayout.ButtonGap, y);
        _btnRetire.Location = new Point(btnSave.Right + UiLayout.ButtonGap, y);
        _btnDelete.Location = new Point(_btnRetire.Right + UiLayout.ButtonGap, y);

        var right = UiLayout.PageLeft + width;

        btnClose.Location = new Point(right - btnClose.Width, y);

        Controls.Add(btnAdd);
        Controls.Add(btnSave);
        Controls.Add(_btnRetire);
        Controls.Add(_btnDelete);
        Controls.Add(btnClose);

        AcceptButton = btnClose;
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);

        ClientSize = new Size(right + UiLayout.PageLeft, UiLayout.Below(btnClose, UiLayout.PageTop));

        Reload();
    }

    private void Reload()
    {
        var selectedId = Selected()?.Id;

        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var type in _types.All)
        {
            _list.Items.Add(type.Retired ? type.DisplayName + "  (retired)" : type.DisplayName);
        }

        _list.EndUpdate();

        var index = _types.All.ToList().FindIndex(t => string.Equals(t.Id, selectedId, StringComparison.Ordinal));

        if (_list.Items.Count > 0)
        {
            _list.SelectedIndex = index >= 0 ? index : 0;
        }
        else
        {
            LoadSelected();
        }
    }

    private ChassisType? Selected() =>
        _list.SelectedIndex >= 0 ? _types.All.ElementAtOrDefault(_list.SelectedIndex) : null;

    private void LoadSelected()
    {
        var type = Selected();

        _txtName.Text = type?.Name ?? string.Empty;
        _txtMaker.Text = type?.Maker ?? string.Empty;

        _cmbSheet.Items.Clear();
        _cmbSheet.Items.Add(new SheetChoice(string.Empty, "(none)"));

        foreach (var sheet in _sheets.All)
        {
            _cmbSheet.Items.Add(new SheetChoice(sheet.Id, sheet.Name));
        }

        _cmbSheet.SelectedItem = _cmbSheet.Items
            .Cast<SheetChoice>()
            .FirstOrDefault(s => string.Equals(s.Id, type?.SetupSheetId, StringComparison.Ordinal))
            ?? _cmbSheet.Items[0];

        var have = type is not null;

        _txtName.Enabled = have;
        _txtMaker.Enabled = have;
        _cmbSheet.Enabled = have;
        _btnDelete.Enabled = have;
        _btnRetire.Enabled = have;
        _btnRetire.Text = type is { Retired: true } ? "Put Back" : "Retire";
    }

    private void AddType()
    {
        var created = new ChassisType { Name = "New chassis" };

        _types.Upsert(created);
        Reload();

        var index = _types.All.ToList().FindIndex(t => string.Equals(t.Id, created.Id, StringComparison.Ordinal));

        if (index >= 0)
        {
            _list.SelectedIndex = index;
        }

        _txtName.Focus();
        _txtName.SelectAll();
    }

    private void SaveSelected()
    {
        if (Selected() is not { } type)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_txtName.Text))
        {
            AppDialog.Show(this, "A chassis needs a name.", "Name it", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        type.Name = _txtName.Text.Trim();
        type.Maker = _txtMaker.Text.Trim();
        type.SetupSheetId = (_cmbSheet.SelectedItem as SheetChoice)?.Id ?? string.Empty;

        _types.Upsert(type);
        Reload();
    }

    private void RetireSelected()
    {
        if (Selected() is not { } type)
        {
            return;
        }

        type.Retired = !type.Retired;
        _types.Upsert(type);
        Reload();
    }

    private void DeleteSelected()
    {
        if (Selected() is not { } type)
        {
            return;
        }

        var inUse = _cars.All.Count(c => string.Equals(c.ChassisTypeId, type.Id, StringComparison.Ordinal));

        if (inUse > 0)
        {
            AppDialog.Show(this,
                $"{Plural.Of(inUse, "car")} in the garage {(inUse == 1 ? "is" : "are")} on a {type.DisplayName}. "
                + "Retire it instead - it stays on those cars and stops being offered for new ones.",
                "Still in use", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (AppDialog.Show(this,
                $"Delete \"{type.DisplayName}\"?",
                "Delete chassis", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _types.Delete(type.Id);
        Reload();
    }
}

/// <summary>
/// One car, opened from the wall: its photographs big, what it is, and
/// every setup ever logged for it.
///
/// A window rather than a panel on the page, because the page is now a
/// wall of pictures and a detail pane underneath it would leave both
/// halves too short to be worth looking at. This is also where the old
/// Tuning page ended up - a setup belongs to a car, so it lives on the
/// car rather than on a separate page pointed at the same row.
///
/// Every change is written through the stores before this closes, so the
/// wall behind it just re-reads. Nothing is handed back.
/// </summary>
internal sealed class CarDetailForm : Form
{
    private readonly Car _car;
    private readonly CarStore _cars;
    private readonly ChassisTypeStore _chassisTypes;
    private readonly ProfileStore _profiles;
    private readonly RaceClassStore _raceClasses;
    private readonly SetupLogStore _setups;
    private readonly SessionHistoryStore _history;
    private readonly TrackLayoutStore _trackLayouts;
    private readonly ChassisTemplateStore _chassisTemplates;
    private readonly UndoService _undo;
    private readonly RaceEventStore? _events;

    private readonly PictureBox _photo = new();
    private readonly Label _lblCount = new();
    private readonly Button _btnPrev = new();
    private readonly Button _btnNext = new();

    private readonly Label _lblHeading = new();
    private readonly Label _lblFacts = new();
    private readonly Label _lblNotes = new();

    private readonly Button _btnRetire = new();
    private readonly Button _btnLend = new();

    private readonly DataGridView _setupGrid = new();
    private readonly Button _btnEditSetup = new();
    private readonly Button _btnDuplicateSetup = new();
    private readonly Button _btnDeleteSetup = new();

    private Image? _shown;
    private int _index;

    public CarDetailForm(
        Car car,
        CarStore cars,
        ChassisTypeStore chassisTypes,
        ProfileStore profiles,
        RaceClassStore raceClasses,
        SetupLogStore setups,
        SessionHistoryStore history,
        TrackLayoutStore trackLayouts,
        ChassisTemplateStore chassisTemplates,
        UndoService undo,
        RaceEventStore? events)
    {
        _car = car;
        _cars = cars;
        _chassisTypes = chassisTypes;
        _profiles = profiles;
        _raceClasses = raceClasses;
        _setups = setups;
        _history = history;
        _trackLayouts = trackLayouts;
        _chassisTemplates = chassisTemplates;
        _undo = undo;
        _events = events;

        Text = car.DisplayName;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = AppTheme.BodyFont;
        MinimumSize = new Size(1000, 720);

        BuildLayout();
        AppTheme.ApplyRecursively(this);

        // After the theme pass: the picture sits on a neutral field so a
        // car is judged on its own colours, not the app's.
        _photo.BackColor = AppTheme.Background;

        RefreshAll();
    }

    private string? CurrentEventId => _events?.Active is { IsLegacy: false } day ? day.Id : null;

    private void BuildLayout()
    {
        const int photoWidth = 460;
        const int photoHeight = 300;

        _photo.Location = new Point(UiLayout.PageLeft, UiLayout.PageTop);
        _photo.Size = new Size(photoWidth, photoHeight);
        _photo.SizeMode = PictureBoxSizeMode.Zoom;

        Controls.Add(_photo);

        _btnPrev.Text = "◀";
        _btnPrev.Size = new Size(44, UiLayout.ButtonHeight);
        _btnPrev.Location = new Point(UiLayout.PageLeft, UiLayout.Below(_photo, UiLayout.HintGap));
        _btnPrev.Click += (_, _) => Step(-1);

        _btnNext.Text = "▶";
        _btnNext.Size = new Size(44, UiLayout.ButtonHeight);
        _btnNext.Location = new Point(_btnPrev.Right + UiLayout.ButtonGap, _btnPrev.Top);
        _btnNext.Click += (_, _) => Step(1);

        _lblCount.AutoSize = true;
        _lblCount.ForeColor = AppTheme.TextMuted;
        _lblCount.Font = AppTheme.SmallFont;
        _lblCount.Location = new Point(_btnNext.Right + UiLayout.ButtonGap, _btnPrev.Top + 9);

        var btnPhotos = new Button { Text = "Photos..." };
        btnPhotos.Size = new Size(UiLayout.WidthFor(btnPhotos), UiLayout.ButtonHeight);
        btnPhotos.Location = new Point(UiLayout.PageLeft + photoWidth - btnPhotos.Width, _btnPrev.Top);
        btnPhotos.Click += (_, _) => EditPhotos();

        Controls.Add(_btnPrev);
        Controls.Add(_btnNext);
        Controls.Add(_lblCount);
        Controls.Add(btnPhotos);

        // --- the facts, beside the picture ---

        var factsLeft = UiLayout.PageLeft + photoWidth + UiLayout.SectionGap;

        // The car's own buttons, built first so the column can be as wide as
        // they need. It was a fixed 460, four standard buttons are about 620,
        // and Delete ran off the side of the window.
        var btnEdit = new Button { Text = "Edit..." };
        btnEdit.Click += (_, _) => EditCar();

        _btnLend.Text = "Lend Out...";
        _btnLend.Click += (_, _) => LendCar();

        _btnRetire.Text = "Retire";
        _btnRetire.Click += (_, _) => RetireCar();

        var btnDelete = new Button { Text = "Delete" };
        btnDelete.Click += (_, _) => DeleteCar();

        var carButtons = new[] { btnEdit, _btnLend, _btnRetire, btnDelete };
        var carButtonsWidth = carButtons.Sum(UiLayout.WidthFor) + UiLayout.ButtonGap * (carButtons.Length - 1);

        var factsWidth = Math.Max(460, carButtonsWidth);

        _lblHeading.Location = new Point(factsLeft, UiLayout.PageTop);
        _lblHeading.AutoSize = true;
        _lblHeading.MaximumSize = new Size(factsWidth, 0);
        _lblHeading.Font = AppTheme.HeadingFont;

        Controls.Add(_lblHeading);

        _lblFacts.Location = new Point(factsLeft, UiLayout.Below(_lblHeading, UiLayout.RowGap));
        _lblFacts.AutoSize = true;
        _lblFacts.MaximumSize = new Size(factsWidth, 0);

        Controls.Add(_lblFacts);

        _lblNotes.Location = new Point(factsLeft, UiLayout.Below(_lblFacts, UiLayout.RowGap));
        _lblNotes.AutoSize = true;
        _lblNotes.MaximumSize = new Size(factsWidth, 0);
        _lblNotes.ForeColor = AppTheme.TextMuted;
        _lblNotes.Font = AppTheme.SmallFont;

        Controls.Add(_lblNotes);

        // On the same line as the photo's own row - arrows, count, Photos -
        // so the two columns end in one toolbar rather than two rows a
        // button apart. It was pinned to the photo's bottom edge instead,
        // which put it just above Photos and never level with it.
        UiLayout.ButtonRow(factsLeft, _btnPrev.Top, carButtons);

        Controls.Add(btnEdit);
        Controls.Add(_btnLend);
        Controls.Add(_btnRetire);
        Controls.Add(btnDelete);

        // --- its setups ---

        var setupsTop = UiLayout.Below(_btnPrev, UiLayout.SectionGap);

        var lblSetups = new Label
        {
            Text = "Setups logged for this car",
            Location = new Point(UiLayout.PageLeft, setupsTop),
            AutoSize = true,
            Font = AppTheme.BoldFont,
        };

        Controls.Add(lblSetups);

        _setupGrid.Location = new Point(UiLayout.PageLeft, UiLayout.Below(lblSetups, UiLayout.RowGap));
        _setupGrid.Size = new Size(factsLeft + factsWidth - UiLayout.PageLeft, 220);
        _setupGrid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _setupGrid.ReadOnly = true;
        _setupGrid.AllowUserToAddRows = false;
        _setupGrid.AllowUserToDeleteRows = false;
        _setupGrid.AllowUserToResizeRows = false;
        _setupGrid.RowHeadersVisible = false;
        _setupGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _setupGrid.MultiSelect = true;
        _setupGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _setupGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "date", HeaderText = "Date", FillWeight = 40 },
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 50 },
            new DataGridViewTextBoxColumn { Name = "track", HeaderText = "Track", FillWeight = 45 },
            new DataGridViewTextBoxColumn { Name = "sheet", HeaderText = "Chassis Sheet", FillWeight = 55 },
            new DataGridViewTextBoxColumn { Name = "filled", HeaderText = "Filled", FillWeight = 26 },
            new DataGridViewTextBoxColumn { Name = "bestlap", HeaderText = "Best Lap", FillWeight = 42 },
            new DataGridViewTextBoxColumn { Name = "summary", HeaderText = "Setup", FillWeight = 160 }
        );
        _setupGrid.SelectionChanged += (_, _) => UpdateButtons();
        _setupGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditSetup(); };

        Controls.Add(_setupGrid);

        var btnLog = new Button { Text = "Log a Setup..." };
        btnLog.Click += (_, _) => AddSetup();

        _btnEditSetup.Text = "Edit...";
        _btnEditSetup.Click += (_, _) => EditSetup();

        _btnDuplicateSetup.Text = "Duplicate";
        _btnDuplicateSetup.Click += (_, _) => DuplicateSetup();

        _btnDeleteSetup.Text = "Delete";
        _btnDeleteSetup.Click += (_, _) => DeleteSetup();

        var btnClose = new Button { Text = "Close", DialogResult = DialogResult.OK };

        var setupButtonsY = _setupGrid.Bottom + UiLayout.RowGap;

        UiLayout.ButtonRow(UiLayout.PageLeft, setupButtonsY, btnLog, _btnEditSetup, _btnDuplicateSetup, _btnDeleteSetup);

        btnClose.Size = new Size(UiLayout.WidthFor(btnClose), UiLayout.ButtonHeight);
        btnClose.Location = new Point(_setupGrid.Right - btnClose.Width, setupButtonsY);

        foreach (var button in new[] { btnLog, _btnEditSetup, _btnDuplicateSetup, _btnDeleteSetup, btnClose })
        {
            button.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        }

        btnClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

        Controls.Add(btnLog);
        Controls.Add(_btnEditSetup);
        Controls.Add(_btnDuplicateSetup);
        Controls.Add(_btnDeleteSetup);
        Controls.Add(btnClose);

        AcceptButton = btnClose;
        CancelButton = btnClose;

        ClientSize = new Size(
            _setupGrid.Right + UiLayout.PageLeft,
            UiLayout.Below(btnClose, UiLayout.PageTop));
    }

    private void RefreshAll()
    {
        Text = _car.DisplayName;
        _lblHeading.Text = _car.DisplayName + (_car.Retired ? "   (retired)" : string.Empty);

        var lines = new List<string>();

        var owner = _car.IsClubCar ? "The club" : NameOf(_car.OwnerTransponder);
        lines.Add($"Owner:  {(owner.Length == 0 ? "—" : owner)}");

        lines.Add($"Class:  {(string.IsNullOrWhiteSpace(_car.ClassName) ? "—" : _car.ClassName)}");

        var chassis = _chassisTypes.NameOf(_car.ChassisTypeId);
        lines.Add($"Chassis:  {(chassis.Length == 0 ? "—" : chassis)}");

        if (!string.IsNullOrWhiteSpace(_car.Bodyshell))
        {
            lines.Add($"Shell:  {_car.Bodyshell}");
        }

        if (!string.IsNullOrWhiteSpace(_car.RaceNumber))
        {
            lines.Add($"Number:  {_car.RaceNumber}");
        }

        var eventId = CurrentEventId;
        var driver = _car.DriverFor(eventId);

        if (eventId is not null
            && !string.IsNullOrWhiteSpace(driver)
            && !string.Equals(driver, _car.OwnerTransponder, StringComparison.OrdinalIgnoreCase))
        {
            lines.Add($"On loan to:  {NameOf(driver)}");
        }

        _lblFacts.Text = string.Join("\r\n", lines);

        _lblNotes.Text = _car.Notes;
        _lblNotes.Visible = !string.IsNullOrWhiteSpace(_car.Notes);
        _lblNotes.Location = new Point(_lblNotes.Left, UiLayout.Below(_lblFacts, UiLayout.RowGap));

        _btnRetire.Text = _car.Retired ? "Put Back" : "Retire";
        _btnLend.Enabled = eventId is not null;

        _index = Math.Clamp(_index, 0, Math.Max(0, _car.PhotoFileNames.Count - 1));

        ShowPhoto();
        RefreshSetups();
    }

    private string NameOf(string transponder)
    {
        if (string.IsNullOrWhiteSpace(transponder))
        {
            return string.Empty;
        }

        var profile = _profiles.Get(transponder);

        return profile is null || string.IsNullOrWhiteSpace(profile.DriverName)
            ? transponder
            : profile.DriverName;
    }

    private void Step(int by)
    {
        if (_car.PhotoFileNames.Count == 0)
        {
            return;
        }

        _index = (_index + by + _car.PhotoFileNames.Count) % _car.PhotoFileNames.Count;
        ShowPhoto();
    }

    private void ShowPhoto()
    {
        // Disposed before the next is loaded. These are full-size pictures
        // rather than cached thumbnails, and stepping through a filmstrip
        // would otherwise hold every one of them.
        _photo.Image = null;
        _shown?.Dispose();
        _shown = null;

        if (_index >= 0 && _index < _car.PhotoFileNames.Count)
        {
            _shown = PhotoStore.Cars.Full(_car.PhotoFileNames[_index]);
        }

        _photo.Image = _shown;

        var count = _car.PhotoFileNames.Count;

        _lblCount.Text = count switch
        {
            0 => "No photographs yet",
            1 => "1 photograph",
            _ => $"{_index + 1} of {count}",
        };

        _btnPrev.Enabled = count > 1;
        _btnNext.Enabled = count > 1;
    }

    // ------------------------------------------------------------- car

    private void EditCar()
    {
        var before = _car.CloneForUndo();

        using var form = new CarForm(_car.CloneForUndo(), _chassisTypes, _profiles, _raceClasses, isNew: false);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _undo.Capture($"Edit {before.DisplayName}", () => _cars.Upsert(before));

        Apply(form.Result);
    }

    private void EditPhotos()
    {
        var before = _car.CloneForUndo();

        using var form = new CarPhotosForm(_car.CloneForUndo());

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // Pictures dropped from the car are left on disk - the undo puts
        // the NAMES back, and a name whose file had been deleted would put
        // back a broken picture.
        _undo.Capture($"Change {before.DisplayName}'s photos", () => _cars.Upsert(before));

        Apply(form.Result);
    }

    private void LendCar()
    {
        if (CurrentEventId is not { } eventId)
        {
            return;
        }

        var dayName = _events?.Get(eventId)?.DisplayName ?? "tonight";

        using var form = new LendCarForm(_car, _profiles, dayName, NameOf(_car.OwnerTransponder));

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var before = _car.CloneForUndo();

        _undo.Capture($"Lend {before.DisplayName}", () => _cars.Upsert(before));

        _cars.Lend(_car.Id, form.ToTransponder, eventId);

        if (_cars.Get(_car.Id) is { } live)
        {
            Apply(live);
        }
    }

    private void RetireCar()
    {
        var before = _car.CloneForUndo();

        _undo.Capture(before.Retired ? $"Put {before.DisplayName} back" : $"Retire {before.DisplayName}",
            () => _cars.Upsert(before));

        var after = _car.CloneForUndo();
        after.Retired = !after.Retired;

        Apply(after);
    }

    private void DeleteCar()
    {
        var setups = SetupsForThisCar().Count;

        var warning = setups == 0
            ? string.Empty
            : $"\r\n\r\n{Plural.Of(setups, "setup")} logged against it stay in the log - they lose the car, not the numbers.";

        if (AppDialog.Show(this,
                $"Delete \"{_car.DisplayName}\"?{warning}\r\n\r\nRetiring it instead keeps it out of the way without losing it.",
                "Delete car", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var before = _car.CloneForUndo();

        _undo.Capture($"Delete {before.DisplayName}", () => _cars.Upsert(before));

        // The photographs are left on disk: an undo has to put the car back
        // whole, and a deleted file cannot be un-deleted.
        _cars.Delete(_car.Id);

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>Writes an edited copy back to the store and onto the car this window is showing, so both agree without a re-read.</summary>
    private void Apply(Car edited)
    {
        _cars.Upsert(edited);

        _car.Name = edited.Name;
        _car.OwnerTransponder = edited.OwnerTransponder;
        _car.ClassName = edited.ClassName;
        _car.ChassisTypeId = edited.ChassisTypeId;
        _car.Bodyshell = edited.Bodyshell;
        _car.RaceNumber = edited.RaceNumber;
        _car.Notes = edited.Notes;
        _car.PhotoFileNames = new List<string>(edited.PhotoFileNames);
        _car.LoanedToTransponder = edited.LoanedToTransponder;
        _car.LoanedForEventId = edited.LoanedForEventId;
        _car.Retired = edited.Retired;

        RefreshAll();
    }

    // ---------------------------------------------------------- setups

    /// <summary>
    /// This car's setups: by id where an entry has one, and otherwise by
    /// the owner's name and the typed car string - which is all an entry
    /// written before the Garage existed has. That fallback is why nothing
    /// had to be rewritten on upgrade.
    /// </summary>
    private IReadOnlyList<SetupEntry> SetupsForThisCar()
    {
        var ownerName = NameOf(_car.OwnerTransponder);

        return _setups.All
            .Where(e =>
                string.Equals(e.CarId, _car.Id, StringComparison.Ordinal)
                || (string.IsNullOrEmpty(e.CarId)
                    && ownerName.Length > 0
                    && string.Equals(e.DriverName.Trim(), ownerName, StringComparison.OrdinalIgnoreCase)
                    && MatchesCarWords(e.CarModel)))
            .ToList();
    }

    /// <summary>
    /// Whether an old entry's typed car string looks like this car.
    /// Deliberately loose: a wrong guess lists a setup under the wrong car
    /// of the same driver, and being strict shows an empty list to everyone
    /// who upgraded.
    /// </summary>
    private bool MatchesCarWords(string typed)
    {
        var text = typed.Trim();

        if (text.Length == 0)
        {
            return false;
        }

        return string.Equals(text, _car.Name.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, _car.Bodyshell.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshSetups()
    {
        _setupGrid.Rows.Clear();

        foreach (var entry in SetupsForThisCar())
        {
            var template = entry.ChassisTemplateId is null ? null : _chassisTemplates.Get(entry.ChassisTemplateId);

            var index = _setupGrid.Rows.Add(
                AppTime.DateAndTime(entry.RecordedAtUtc.ToLocalTime()),
                entry.DriverName,
                entry.TrackLayoutName,
                template?.Name ?? (entry.IsLegacy ? "(older entry)" : string.Empty),
                entry.IsLegacy ? string.Empty : entry.FilledCount.ToString(),
                BestLapFor(entry),
                entry.Summarise(template));

            _setupGrid.Rows[index].Tag = entry.Id;
        }

        UpdateButtons();
    }

    /// <summary>The best lap of the session an entry was linked to, or empty - the reason for linking one at all.</summary>
    private string BestLapFor(SetupEntry entry)
    {
        if (string.IsNullOrEmpty(entry.LinkedSessionId) || _history.Get(entry.LinkedSessionId) is not { } session)
        {
            return string.Empty;
        }

        var mine = session.Results.FirstOrDefault(r =>
            string.Equals(r.DriverName.Trim(), entry.DriverName.Trim(), StringComparison.OrdinalIgnoreCase));

        return mine is { FastLapMs: > 0 } ? $"{mine.FastLapMs / 1000.0:0.000}s" : string.Empty;
    }

    private void UpdateButtons()
    {
        _btnEditSetup.Enabled = _setupGrid.SelectedRows.Count == 1;
        _btnDuplicateSetup.Enabled = _setupGrid.SelectedRows.Count == 1;
        _btnDeleteSetup.Enabled = _setupGrid.SelectedRows.Count >= 1;
    }

    private SetupEntry? SelectedSetup() =>
        _setupGrid.SelectedRows.Count >= 1 && _setupGrid.SelectedRows[0].Tag is string id ? _setups.Get(id) : null;

    private void AddSetup()
    {
        var entry = new SetupEntry
        {
            CarId = _car.Id,
            DriverName = NameOf(_car.OwnerTransponder),
            CarModel = _car.DisplayName,

            // The chassis can say which sheet to reach for - that is what
            // ChassisType.SetupSheetId is for.
            ChassisTemplateId = SheetForChassis(),
        };

        EditSetupEntry(entry, isNew: true);
    }

    private string? SheetForChassis()
    {
        var sheetId = _chassisTypes.Get(_car.ChassisTypeId)?.SetupSheetId;

        return string.IsNullOrWhiteSpace(sheetId) || _chassisTemplates.Get(sheetId) is null ? null : sheetId;
    }

    private void EditSetup()
    {
        if (SelectedSetup() is { } entry)
        {
            EditSetupEntry(entry, isNew: false);
        }
    }

    private void EditSetupEntry(SetupEntry entry, bool isNew)
    {
        var before = isNew ? null : _setups.Get(entry.Id);
        var beforeCopy = before is null ? null : SetupLogStore.CopyOf(before);

        using var form = new SetupEntryForm(entry, _chassisTemplates, _history, _profiles, _trackLayouts);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var saved = form.Result;

        // The car it belongs to, stamped on save rather than on upgrade -
        // see SetupEntry.CarId.
        if (string.IsNullOrEmpty(saved.CarId))
        {
            saved.CarId = _car.Id;
        }

        var id = saved.Id;

        _undo.Capture(isNew ? "Log a setup" : "Edit a setup", () => _setups.UndoUpsert(id, beforeCopy));

        _setups.Upsert(saved);
        RefreshSetups();
    }

    private void DuplicateSetup()
    {
        if (SelectedSetup() is { } entry)
        {
            EditSetupEntry(entry.CloneForNewEntry(), isNew: true);
        }
    }

    private void DeleteSetup()
    {
        var chosen = _setupGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(r => r.Tag as string)
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => _setups.Get(id!))
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();

        if (chosen.Count == 0)
        {
            return;
        }

        if (AppDialog.Show(this,
                chosen.Count == 1 ? "Delete this setup?" : $"Delete {Plural.Of(chosen.Count, "setup")}?",
                "Delete setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var removed = chosen.Select(SetupLogStore.CopyOf).ToList();

        _undo.Capture(chosen.Count == 1 ? "Delete a setup" : $"Delete {chosen.Count} setups",
            () => _setups.RestoreDeleted(removed));

        foreach (var entry in chosen)
        {
            _setups.Delete(entry.Id);
        }

        RefreshSetups();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _photo.Image = null;
            _shown?.Dispose();
        }

        base.Dispose(disposing);
    }
}
