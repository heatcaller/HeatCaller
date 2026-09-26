namespace HeatCaller;

/// <summary>
/// Add/edit dialog for a single DriverProfile. Controls are built directly
/// in code rather than a separate .Designer.cs partial - functionally
/// identical, just fewer files for a form this small.
/// </summary>
public sealed class ProfileForm : Form
{
    private readonly RaceClassStore _classStore;
    private readonly UndoService? _undo;

    /// <summary>The saved profiles, for refusing a transponder that is already somebody else's. Null skips the check.</summary>
    private readonly ProfileStore? _profiles;

    /// <summary>The main transponder of the profile being edited, or null when adding - it may "clash" with itself.</summary>
    private readonly string? _editingTransponder;
    private readonly TextBox _txtTransponder = new();
    private readonly TextBox _txtDriverName = new();
    private readonly ComboBox _cmbCars = new();
    private readonly TextBox _txtAnnouncerName = new();
    private readonly TextBox _txtRaceNumber = new();
    private readonly TextBox _txtHomeTrack = new();
    private readonly TextBox _txtSkillLevel = new();
    private readonly TextBox _txtContact = new();
    private readonly TextBox _txtEmail = new();
    private readonly TextBox _txtAltTransponder = new();
    private readonly CheckBox _chkRental = new();
    private readonly CheckBox _chkExcludeRecords = new();
    private readonly ComboBox _cmbClass = new();
    private readonly TextBox _txtSetupConfiguration = new();
    private readonly TextBox _txtNotes = new();
    private readonly TextBox _txtTeam = new();
    private readonly TextBox _txtSponsors = new();
    private readonly Panel _photoPanel = new();

    /// <summary>The photo as it stands in the dialog - not saved until Save is.</summary>
    private string _photoFileName;

    /// <summary>What the profile arrived with, so Save knows which old file it replaced.</summary>
    private readonly string _originalPhotoFileName;

    /// <summary>Files imported while this dialog was open. All but the one that gets saved are deleted on close.</summary>
    private readonly List<string> _importedThisSession = new();

    public DriverProfile Result { get; private set; }

    /// <param name="existing">Pass an existing profile to edit it, or null to create a new one.</param>
    /// <param name="classStore">Backing store for the Class dropdown and its "Manage Classes..." shortcut.</param>
    /// <param name="fixedTransponder">
    /// When set (e.g. opened by double-clicking a live leaderboard row), the
    /// transponder field is locked to this value since it came from the
    /// timing hardware and shouldn't be hand-edited into a mismatch.
    /// </param>
    /// <param name="profiles">
    /// The saved profiles. When given, Save refuses a transponder or alt
    /// transponder that already belongs to another driver, naming them,
    /// instead of letting the caller overwrite that driver's profile.
    /// </param>
    public ProfileForm(DriverProfile? existing, RaceClassStore classStore, string? fixedTransponder = null, UndoService? undo = null, ProfileStore? profiles = null)
    {
        _classStore = classStore;
        _undo = undo;
        _profiles = profiles;
        _editingTransponder = existing?.Transponder;
        Result = existing ?? new DriverProfile { Transponder = fixedTransponder ?? string.Empty };

        Text = existing is null ? "Add Driver Profile" : "Edit Driver Profile";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        // 640 wide: the left column is the admin (everything the organiser
        // needs), and the new right strip is the show - photo, team,
        // sponsors. Widened rather than made taller, because at 760 this
        // dialog already fills a small laptop top to bottom - the one row
        // added since (Email) cost it 32px, which could not be found
        // anywhere else without squeezing the notes.
        ClientSize = new Size(640, 792);
        Font = AppTheme.BodyFont;

        _photoFileName = Result.PhotoFileName;
        _originalPhotoFileName = Result.PhotoFileName;

        var y = 16;
        AddLabeledTextBox("Transponder:", _txtTransponder, ref y, Result.Transponder);
        _txtTransponder.ReadOnly = fixedTransponder is not null;

        AddLabeledTextBox("Driver name:", _txtDriverName, ref y, Result.DriverName);

        // What the announcer says, when it differs. A nickname the crowd
        // knows, or a spelling the voice actually pronounces correctly -
        // this is wired into the announcer, so it takes effect immediately.
        AddLabeledTextBox("Announcer name:", _txtAnnouncerName, ref y, Result.AnnouncerName);
        _txtAnnouncerName.PlaceholderText = "optional - blank uses the driver name";

        // Garage rather than a single box. The combo shows the active car
        // and lists the rest; Add and Remove manage the garage. A driver
        // with a stock and a modified car shouldn't need two profiles -
        // that splits their lap history and personal bests in two.
        var lblCars = new Label { Text = "Cars:", Location = new Point(16, y + 3), AutoSize = true };

        _cmbCars.Location = new Point(140, y);
        _cmbCars.Width = 200;
        _cmbCars.DropDownStyle = ComboBoxStyle.DropDown;

        foreach (var car in Result.AllCars)
        {
            _cmbCars.Items.Add(car);
        }

        _cmbCars.Text = Result.CarModel;

        var btnAddCar = new Button { Text = "Add", Location = new Point(348, y - 1), Width = 52, Height = 24 };
        btnAddCar.Click += (_, _) =>
        {
            var car = _cmbCars.Text.Trim();

            if (car.Length == 0)
            {
                return;
            }

            if (!_cmbCars.Items.Cast<string>().Any(c => string.Equals(c, car, StringComparison.OrdinalIgnoreCase)))
            {
                _cmbCars.Items.Add(car);
            }

            _cmbCars.Text = car;
        };

        var btnRemoveCar = new Button { Text = "Remove", Location = new Point(404, y - 1), Width = 62, Height = 24 };
        btnRemoveCar.Click += (_, _) =>
        {
            if (_cmbCars.SelectedItem is not string car)
            {
                return;
            }

            _cmbCars.Items.Remove(car);

            // Fall back to whatever is left, so the active car is never a
            // name that's no longer in the garage.
            _cmbCars.Text = _cmbCars.Items.Count > 0 ? (string)_cmbCars.Items[0]! : string.Empty;
        };

        Controls.Add(lblCars);
        Controls.Add(_cmbCars);
        Controls.Add(btnAddCar);
        Controls.Add(btnRemoveCar);
        y += 28;

        var lblCarsHint = new Label
        {
            Text = "Type a car and press Add. The one shown is the active car - what gets announced and recorded.",
            Location = new Point(140, y),
            AutoSize = true,
            MaximumSize = new Size(326, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };
        Controls.Add(lblCarsHint);
        y += 34;
        AddLabeledTextBox("Race number:", _txtRaceNumber, ref y, Result.RaceNumber);
        AddLabeledTextBox("Home track:", _txtHomeTrack, ref y, Result.HomeTrack);
        AddLabeledTextBox("Skill level:", _txtSkillLevel, ref y, Result.SkillLevel);
        _txtSkillLevel.PlaceholderText = "e.g. Novice / Intermediate / Expert";

        AddLabeledTextBox("Contact:", _txtContact, ref y, Result.Contact);
        _txtContact.PlaceholderText = "organiser use only - never announced";

        // Its own box rather than read out of Contact, which is free text
        // ("Dad, 07700 900123"). Only ever used to send this racer their
        // check-in PIN.
        AddLabeledTextBox("Email:", _txtEmail, ref y, Result.Email);
        _txtEmail.PlaceholderText = "for check-in PINs - never shown publicly";

        AddLabeledTextBox("Alt transponder:", _txtAltTransponder, ref y, Result.AlternateTransponder);
        _txtAltTransponder.PlaceholderText = "a second car they also run";

        var lblClass = new Label { Text = "Class:", Location = new Point(16, y + 3), AutoSize = true };
        _cmbClass.Location = new Point(140, y);
        _cmbClass.Width = 200;
        _cmbClass.DropDownStyle = ComboBoxStyle.DropDownList;
        // Positioned after the combo (140-340), not on top of it - at x=324
        // the button overlapped by 16px, which is what clipped it. Also
        // widened: "Manage Classes..." never fit in 80px, hence the 7pt
        // font that was papering over it.
        var btnManageClasses = new Button
        {
            Text = "Manage...",
            Location = new Point(348, y - 1),
            Width = 118,
            Height = 24,
        };
        btnManageClasses.Click += (_, _) =>
        {
            // The class this box shows now - a change not yet saved
            // included - carried through any rename made in the dialog.
            // It used to go back to the class the profile had when it was
            // opened, and a class renamed in there then matched nothing and
            // showed as "(none)", so saving the driver dropped their class.
            var selected = _cmbClass.SelectedItem as string is { } shown && shown != "(none)" ? shown : string.Empty;

            void FollowRename(string oldName, string newName)
            {
                if (selected.Length > 0 && string.Equals(selected, oldName, StringComparison.OrdinalIgnoreCase))
                {
                    selected = newName;
                }
            }

            var cascade = _classStore.RenameCascade;

            if (cascade is not null)
            {
                cascade.Renamed += FollowRename;
            }

            try
            {
                using var form = new RaceClassManagerForm(_classStore, _undo);
                form.ShowDialog(this);
            }
            finally
            {
                if (cascade is not null)
                {
                    cascade.Renamed -= FollowRename;
                }
            }

            RefreshClassCombo(selected);
        };
        Controls.Add(lblClass);
        Controls.Add(_cmbClass);
        Controls.Add(btnManageClasses);
        y += 32;

        RefreshClassCombo(Result.ClassName);

        y += 4;
        var lblSetup = new Label { Text = "Setup configuration:", Location = new Point(16, y), AutoSize = true };
        Controls.Add(lblSetup);
        y += 20;
        _txtSetupConfiguration.Multiline = true;
        _txtSetupConfiguration.AcceptsReturn = true;
        _txtSetupConfiguration.ScrollBars = ScrollBars.Vertical;
        _txtSetupConfiguration.Location = new Point(16, y);
        _txtSetupConfiguration.Size = new Size(388, 90);
        _txtSetupConfiguration.Text = Result.SetupConfiguration;
        Controls.Add(_txtSetupConfiguration);
        y += 98;

        _chkRental.Text = "Rental / loaner transponder";
        _chkRental.Location = new Point(140, y);
        _chkRental.AutoSize = true;
        _chkRental.Checked = Result.IsRental;
        Controls.Add(_chkRental);
        y += 26;

        _chkExcludeRecords.Text = "Exclude from track records and points";
        _chkExcludeRecords.Location = new Point(140, y);
        _chkExcludeRecords.AutoSize = true;
        _chkExcludeRecords.Checked = Result.ExcludeFromRecords;
        Controls.Add(_chkExcludeRecords);
        y += 26;

        var lblExcludeHint = new Label
        {
            Text = "Both keep a driver out of all-time records - for guests, staff cars and loaners.",
            Location = new Point(140, y),
            AutoSize = true,
            MaximumSize = new Size(264, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };
        Controls.Add(lblExcludeHint);
        y += 32;

        var lblNotes = new Label { Text = "Notes:", Location = new Point(16, y), AutoSize = true };
        Controls.Add(lblNotes);
        y += 20;
        _txtNotes.Multiline = true;
        _txtNotes.AcceptsReturn = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;
        _txtNotes.Location = new Point(16, y);
        _txtNotes.Size = new Size(388, 90);
        _txtNotes.Text = Result.Notes;
        Controls.Add(_txtNotes);
        y += 98;

        // Bottom-right of the DIALOG, now that it is wider than its left
        // column - dialog buttons that stop two thirds of the way across
        // read as a mistake.
        var btnSave = new Button { Text = "Save", Location = new Point(448, y), Width = 84 };
        btnSave.Click += BtnSave_Click;
        var btnCancel = new Button { Text = "Cancel", Location = new Point(540, y), Width = 84, DialogResult = DialogResult.Cancel };
        // Badges, read-only, under the rest. Read here rather than passed in
        // so the form's callers don't all need a store they otherwise have
        // no use for. Left column, full width - at its old x it was
        // half off the dialog's right edge.
        var badgeSummary = DescribeBadges(fixedTransponder ?? _txtTransponder.Text);

        if (!string.IsNullOrWhiteSpace(badgeSummary))
        {
            var lblBadges = new Label
            {
                Text = badgeSummary,
                Location = new Point(16, btnSave.Top - 46),
                AutoSize = false,
                Size = new Size(424, 40),
                ForeColor = AppTheme.TextMuted,
            };

            Controls.Add(lblBadges);
        }

        Controls.Add(btnSave);
        Controls.Add(btnCancel);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        BuildPhotoStrip();

        BackColor = AppTheme.Background;
        AppTheme.ApplyRecursively(this);

        // Whatever was imported and NOT saved is deleted on the way out -
        // a photo chosen and then cancelled away must not pile up in the
        // app's folder, and neither must the second choice made before a
        // third. The one the profile keeps survives, of course.
        FormClosed += (_, _) =>
        {
            var kept = DialogResult == DialogResult.OK ? Result.PhotoFileName : _originalPhotoFileName;

            foreach (var imported in _importedThisSession.Where(f => !string.Equals(f, kept, StringComparison.OrdinalIgnoreCase)))
            {
                DriverPhotos.Delete(imported);
            }

            if (DialogResult == DialogResult.OK
                && _originalPhotoFileName.Length > 0
                && !string.Equals(_originalPhotoFileName, Result.PhotoFileName, StringComparison.OrdinalIgnoreCase))
            {
                // Moved aside rather than deleted when the caller can undo:
                // undoing the edit puts the old profile back, and it should
                // come back with its face, not a missing file.
                if (_undo is not null)
                {
                    DriverPhotos.Retire(_originalPhotoFileName);
                }
                else
                {
                    DriverPhotos.Delete(_originalPhotoFileName);
                }
            }
        };
    }

    /// <summary>
    /// The right-hand strip: photo on top, then team and sponsors - the
    /// three fields that exist for the crowd rather than the organiser,
    /// grouped away from the paperwork on the left.
    /// </summary>
    private void BuildPhotoStrip()
    {
        const int left = 488;
        const int width = 136;

        _photoPanel.Location = new Point(left, 16);
        _photoPanel.Size = new Size(width, width);
        _photoPanel.Paint += (_, e) =>
        {
            var who = _txtDriverName.Text.Trim().Length > 0 ? _txtDriverName.Text.Trim() : Result.Transponder;
            DriverPhotos.DrawAvatar(e.Graphics, _photoPanel.ClientRectangle, who, _photoFileName);
        };
        Controls.Add(_photoPanel);

        // The initials avatar follows the name as it is typed.
        _txtDriverName.TextChanged += (_, _) => _photoPanel.Invalidate();

        var btnChoose = new Button { Text = "Choose Photo...", Location = new Point(left, 158), Width = width, Height = 26 };
        btnChoose.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Choose a driver photo",
                Filter = "Images (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All files (*.*)|*.*",
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var imported = DriverPhotos.Import(dialog.FileName, out var error);

            if (imported is null)
            {
                AppDialog.Show(this, $"Couldn't use that picture: {error}", "Photo not added",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _importedThisSession.Add(imported);
            _photoFileName = imported;
            _photoPanel.Invalidate();
        };
        Controls.Add(btnChoose);

        var btnRemove = new Button { Text = "Remove Photo", Location = new Point(left, 188), Width = width, Height = 26 };
        btnRemove.Click += (_, _) =>
        {
            _photoFileName = string.Empty;
            _photoPanel.Invalidate();
        };
        Controls.Add(btnRemove);

        var lblTeam = new Label { Text = "Team / club:", Location = new Point(left, 228), AutoSize = true };
        _txtTeam.Location = new Point(left, 248);
        _txtTeam.Width = width;
        _txtTeam.Text = Result.Team;
        Controls.Add(lblTeam);
        Controls.Add(_txtTeam);

        var lblSponsors = new Label { Text = "Sponsors:", Location = new Point(left, 282), AutoSize = true };
        _txtSponsors.Multiline = true;
        _txtSponsors.ScrollBars = ScrollBars.Vertical;
        _txtSponsors.Location = new Point(left, 302);
        _txtSponsors.Size = new Size(width, 76);
        _txtSponsors.Text = Result.Sponsors;
        Controls.Add(lblSponsors);
        Controls.Add(_txtSponsors);

        var lblSponsorsHint = new Label
        {
            Text = "Shown on the driver card - the works-driver treatment.",
            Location = new Point(left, 382),
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };
        Controls.Add(lblSponsorsHint);
    }

    private void RefreshClassCombo(string? preferredSelection)
    {
        _cmbClass.Items.Clear();
        _cmbClass.Items.Add("(none)");
        foreach (var c in _classStore.All)
        {
            _cmbClass.Items.Add(c.Name);
        }

        var idx = !string.IsNullOrWhiteSpace(preferredSelection) ? _cmbClass.Items.IndexOf(preferredSelection) : -1;
        _cmbClass.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void AddLabeledTextBox(string label, TextBox box, ref int y, string initialValue)
    {
        var lbl = new Label { Text = label, Location = new Point(16, y + 3), AutoSize = true };
        box.Location = new Point(140, y);
        box.Width = 320;
        box.Text = initialValue;
        Controls.Add(lbl);
        Controls.Add(box);
        y += 32;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        var transponder = _txtTransponder.Text.Trim();
        if (string.IsNullOrWhiteSpace(transponder))
        {
            AppDialog.Show(this, "Transponder is required.", "Missing transponder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var saved = new DriverProfile
        {
            Transponder = transponder,
            DriverName = _txtDriverName.Text.Trim(),
            CarModel = _cmbCars.Text.Trim(),

            // Includes whatever is typed but not yet Added - forgetting to
            // press Add shouldn't silently lose the car you just entered.
            Cars = _cmbCars.Items.Cast<string>()
                .Concat(new[] { _cmbCars.Text.Trim() })
                .Where(car => !string.IsNullOrWhiteSpace(car))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ClassName = _cmbClass.SelectedItem as string == "(none)" ? string.Empty : (string)(_cmbClass.SelectedItem ?? string.Empty),
            SetupConfiguration = _txtSetupConfiguration.Text,
            Notes = _txtNotes.Text,

            AnnouncerName = _txtAnnouncerName.Text.Trim(),
            RaceNumber = _txtRaceNumber.Text.Trim(),
            HomeTrack = _txtHomeTrack.Text.Trim(),
            SkillLevel = _txtSkillLevel.Text.Trim(),
            Contact = _txtContact.Text.Trim(),
            Email = _txtEmail.Text.Trim(),
            AlternateTransponder = _txtAltTransponder.Text.Trim(),
            IsRental = _chkRental.Checked,
            ExcludeFromRecords = _chkExcludeRecords.Checked,

            PhotoFileName = _photoFileName,
            Team = _txtTeam.Text.Trim(),
            Sponsors = _txtSponsors.Text.Trim(),

            // Preserved on edit, stamped on create - so "new this season"
            // reporting has something to work from.
            FirstSeenUtc = Result.FirstSeenUtc ?? DateTime.UtcNow,
        };

        // Checked here, with the dialog still open, so the fix is one edit
        // away rather than a lost form. Saving over another driver's
        // transponder used to replace their whole profile without a word.
        if (_profiles?.DescribeClash(saved, _editingTransponder) is { } clash)
        {
            AppDialog.Show(this, clash, "Transponder already in use", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Result = saved;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// A one-line summary of what this driver has earned, or nothing at all
    /// when they have earned nothing - an empty "Badges:" heading is worse
    /// than no heading.
    /// </summary>
    private static string DescribeBadges(string? transponder)
    {
        if (string.IsNullOrWhiteSpace(transponder))
        {
            return string.Empty;
        }

        try
        {
            var driver = AchievementStore.Load().For(transponder);

            var earned = driver.Unlocked
                .Select(u => AchievementCatalogue.Get(u.Id))
                .Where(b => b is not null)
                .Select(b => b!.Name)
                .ToList();

            if (earned.Count == 0)
            {
                return string.Empty;
            }

            var shown = string.Join(", ", earned.Take(4));
            var more = earned.Count > 4 ? $" +{earned.Count - 4} more" : string.Empty;

            return $"Badges ({earned.Count}):  {shown}{more}";
        }
        catch
        {
            // Decoration. Never the reason a driver can't be edited.
            return string.Empty;
        }
    }
}
