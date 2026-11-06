namespace HeatCaller;

/// <summary>
/// Add/edit dialog for a single DriverProfile. Controls are built directly
/// in code rather than a separate .Designer.cs partial - functionally
/// identical, just fewer files for a form this small.
///
/// On the layout pieces (DialogFit): the organiser's fields down the left in
/// a field grid - the captions in a column as wide as the longest, the
/// garage and the class each a drop-down with its buttons beside it (under
/// it, when a long name leaves them no room) - and down the right the strip
/// for the crowd: the photo, square and as wide as the strip, its two
/// buttons, then team and sponsors. The badges line, when the driver has
/// earned any, is under the fields. All of that is the window's body, which
/// scrolls on a screen too short for it, and Save and Cancel are under it
/// at the foot. It opens at least as wide as it was made at the normal text
/// size, at the size in use, and wider when its columns need more: the
/// garage's drop-down and its two buttons on one line, a drop-down's longest
/// name, the body's scrollbar on a short screen. It used to be placed in
/// pixels in a 640x792 window - taller than a 1280x720 screen has room for,
/// so Save and Cancel were off the bottom of it - with the captions in a
/// 124px column, both drop-downs 200px wide whatever they held, and the
/// badges line laid over the bottom of the notes box.
/// </summary>
public sealed class ProfileForm : Form
{
    /// <summary>
    /// How wide the window is inside its frame at the normal text size, at
    /// the least: the left column is the admin (everything the organiser
    /// needs), and the strip down the right is the show - photo, team,
    /// sponsors. Made wide rather than tall, because this dialog has more
    /// fields than a small laptop has height for.
    /// </summary>
    private const int DialogWidth = 640;

    /// <summary>The fields' column at the normal text size, at the least: the 320px the text boxes had.</summary>
    private const int FieldColumnWidth = 320;

    /// <summary>A drop-down's width at the normal text size - wider when its longest name needs it.</summary>
    private const int PickerLeastWidth = 200;

    /// <summary>The photo's side at the normal text size. The strip is this wide, or as wide as its buttons need.</summary>
    private const int PhotoSide = 136;

    /// <summary>Lines of text the setup and notes boxes show - about the 90px they had.</summary>
    private const int NotesLines = 4;

    /// <summary>Lines of text the sponsors box shows - about the 76px it had.</summary>
    private const int SponsorsLines = 3;

    private readonly RaceClassStore _classStore;
    private readonly UndoService? _undo;

    /// <summary>The saved profiles, for refusing a transponder that is already somebody else's. Null skips the check.</summary>
    private readonly ProfileStore? _profiles;

    /// <summary>The main transponder of the profile being edited, or null when adding - it may "clash" with itself.</summary>
    private readonly string? _editingTransponder;

    /// <summary>
    /// Reads a line out in the club's own announcer voice - wired to
    /// AnnouncementService.SpeakTest by whoever opened this dialog, so the
    /// engine, the voice and the speed are the ones the club has set rather
    /// than this dialog's idea of them.
    ///
    /// Null leaves Hear It off the dialog altogether. A button that cannot
    /// do anything is worse than no button: it reads as the feature being
    /// broken, which is how several things in this app got reported.
    /// </summary>
    private readonly Action<string>? _speakName;

    private readonly TextBox _txtTransponder = new();
    private readonly TextBox _txtDriverName = new();
    private readonly ComboBox _cmbCars = new();
    private readonly TextBox _txtAnnouncerName = new();

    /// <summary>Their pronouns - one of Pronouns.Common or their own, typed. See DriverProfile.Pronouns.</summary>
    private readonly ComboBox _cmbPronouns = new();
    private readonly TextBox _txtRaceNumber = new();
    private readonly TextBox _txtHomeTrack = new();
    private readonly TextBox _txtSkillLevel = new();
    private readonly TextBox _txtContact = new();
    private readonly TextBox _txtEmail = new();

    /// <summary>A line in their own words for the AI commentary - see DriverProfile.AboutLine.</summary>
    private readonly TextBox _txtAboutLine = new();

    /// <summary>Email them their night - see DriverProfile.EmailMyNight. Only enabled while Email is filled in.</summary>
    private readonly CheckBox _chkEmailMyNight = new ThemedCheckBox();

    /// <summary>The online privacy ticks - see DriverProfile.NoPhotoOnline, NoVideoOnline and InitialsOnline.</summary>
    private readonly CheckBox _chkNoPhotoOnline = new ThemedCheckBox();
    private readonly CheckBox _chkNoVideoOnline = new ThemedCheckBox();
    private readonly CheckBox _chkInitialsOnline = new ThemedCheckBox();

    /// <summary>Keep their photo off the Big Screen - DriverProfile.NoPhotoOnBigScreen.</summary>
    private readonly CheckBox _chkNoPhotoBigScreen = new ThemedCheckBox();
    /// <summary>The driver's other transponders, and the main one's Retire - see TransponderChoice.</summary>
    private readonly ComboBox _cmbTransponders = new();

    /// <summary>
    /// The transponder list as it stands in the dialog: a copy of the
    /// profile's, so Cancel leaves the stored driver exactly as it was.
    /// </summary>
    private readonly List<OwnedTransponder> _transponders;
    private readonly CheckBox _chkRental = new ThemedCheckBox();
    private readonly CheckBox _chkExcludeRecords = new ThemedCheckBox();
    private readonly ComboBox _cmbClass = new();

    /// <summary>The other classes they race, as "Also races" shows them - chosen with Choose... (OtherClassesForm), saved as DriverProfile.Classes.</summary>
    private List<string> _otherClasses = new();

    /// <summary>"Also races": the other classes, or "none".</summary>
    private readonly Label _lblOtherClasses = new() { AutoSize = true };
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

    /// <summary>
    /// How wide the photo strip is: the photo's side at the text size in
    /// use, or as wide as its wider button's caption needs. Worked out once
    /// the theme pass has been (SizeStrip); the strip asks for it whenever
    /// it is placed.
    /// </summary>
    private int _stripWidth;

    /// <summary>The field column's width with the body's scrollbar showing - see FitPicker.</summary>
    private int _fieldColumn;

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
    /// <param name="speakName">
    /// Reads a name out in the club's announcer voice, for the Hear It
    /// beside the Announcer name box - wire it to
    /// AnnouncementService.SpeakTest. Null leaves that button off.
    /// </param>
    public ProfileForm(DriverProfile? existing, RaceClassStore classStore, string? fixedTransponder = null, UndoService? undo = null, ProfileStore? profiles = null, Action<string>? speakName = null)
    {
        _classStore = classStore;
        _undo = undo;
        _profiles = profiles;
        _speakName = speakName;
        _editingTransponder = existing?.Transponder;
        Result = existing ?? new DriverProfile { Transponder = fixedTransponder ?? string.Empty };
        _otherClasses = (Result.Classes ?? new List<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .ToList();
        _transponders = System.Text.Json.JsonSerializer.Deserialize<List<OwnedTransponder>>(
                            System.Text.Json.JsonSerializer.Serialize(Result.Transponders ?? new List<OwnedTransponder>()))
                        ?? new List<OwnedTransponder>();

        Text = existing is null ? "Add Driver Profile" : "Edit Driver Profile";
        Font = AppTheme.BodyFont;

        _photoFileName = Result.PhotoFileName;
        _originalPhotoFileName = Result.PhotoFileName;

        // The organiser's column: a caption and its field on each row, the
        // captions in one column as wide as the longest of them, and every
        // text box running to the column's right-hand edge.
        var fields = new FieldGrid();
        var captions = new List<Label>();

        captions.Add(AddLabeledTextBox(fields, "Transponder:", _txtTransponder, Result.Transponder));
        _txtTransponder.ReadOnly = fixedTransponder is not null;

        captions.Add(AddLabeledTextBox(fields, "Driver name:", _txtDriverName, Result.DriverName));

        // What the announcer says, when it differs. A nickname the crowd
        // knows, or a spelling the voice actually pronounces correctly -
        // this is wired into the announcer, so it takes effect immediately.
        //
        // The box has always done both of those jobs and only the first was
        // ever obvious, so the hint under it now names the second, with
        // examples: a club cannot use a feature it has not been told is
        // there. And the respelling half was unusable anyway - a club could
        // type a guess, but nothing would say whether the guess worked
        // until the name went out over the PA in front of the room - so
        // Hear It reads whatever is in the box, now, through the same name
        // resolution the race calls use (NameAsAnnounced).
        captions.Add(AddLabeledTextBox(fields, "Announcer name:", _txtAnnouncerName, Result.AnnouncerName));
        _txtAnnouncerName.PlaceholderText = "optional - a nickname, or how the name sounds";

        // Not built at all when there is no voice to read through - see
        // _speakName. Hidden would do as much for the eye and leave a dead
        // button in the dialog for anything that walks it.
        Button? btnHearName = null;

        if (_speakName is not null)
        {
            btnHearName = new Button { Text = "Hear It" };
            btnHearName.Click += (_, _) => HearAnnouncerName();

            // In the fields' column under the box, not beside it: the box
            // runs to the column's right-hand edge like every other one
            // here, and narrowing it to make room for a button would single
            // this field out for no reason a club would understand.
            fields.AddField(new ButtonBar().With(btnHearName));
        }

        var announcerHint =
            "Read out instead of their real name: a nickname, or the name spelled the way it SOUNDS to fix one the "
            + "voice gets wrong (\"Jacques\" as \"Zhock\"). Their real name is used everywhere else.";

        if (btnHearName is not null)
        {
            announcerHint += " Hear It plays it as the announcer will say it.";
        }

        fields.AddHint(announcerHint);

        // Their pronouns, under the name they are announced by (21 Oct
        // 2026) - the other half of the announcer getting a driver right.
        // A drop-down that can be typed in: the usual three to pick from,
        // and nobody's own refused. Blank is allowed and changes nothing.
        _cmbPronouns.DropDownStyle = ComboBoxStyle.DropDown;
        _cmbPronouns.MaxLength = Pronouns.MaxChars;

        foreach (var set in Pronouns.Common)
        {
            _cmbPronouns.Items.Add(set);
        }

        _cmbPronouns.Text = Pronouns.Clean(Result.Pronouns);

        captions.Add(fields.AddRow("Pronouns:", _cmbPronouns));
        fields.AddHint("Optional. The AI commentary uses these instead of guessing from their name. Pick one, or type "
                       + "their own (like \"she/they\").");

        // WHY (22 Oct 2026, driver experience): a line about them in their
        // own words, for the AI commentary - beside the pronouns, the other
        // thing on here only the commentary reads. A driver can now ask for
        // it from their own phone, and the desk sees and edits it here.
        // Never shown publicly. Capped where the phone's is (AboutLineMaxChars).
        captions.Add(AddLabeledTextBox(fields, "About them:", _txtAboutLine, Result.AboutLine ?? string.Empty));
        _txtAboutLine.MaxLength = DriverProfile.AboutLineMaxChars;
        _txtAboutLine.PlaceholderText = "optional - for the AI commentary, in their own words";
        fields.AddHint($"For the AI commentary: a line in their own words, up to {DriverProfile.AboutLineMaxChars} characters "
                       + "(like \"racing since I was eight; my dad builds the cars\"). Never shown publicly.");

        // Their garage's cars, the active one showing - what they are
        // announced and recorded in. A name typed and Added is a new car,
        // which the Garage makes when the profile is saved (ProfileStore.
        // CarsAdded); everything else about a car is done in the Garage.
        _cmbCars.DropDownStyle = ComboBoxStyle.DropDown;

        foreach (var car in Result.AllCars)
        {
            _cmbCars.Items.Add(car);
        }

        _cmbCars.Text = Result.CarModel;

        var btnAddCar = new Button { Text = "Add" };
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

            // A longer name than the box was made for widens it, as far as
            // its row goes.
            FitPicker(_cmbCars, keepToRow: true);
        };

        // No Remove: the list is their garage (GarageSync.SyncNames), where
        // a car is retired, deleted or transferred - a name taken off here
        // would only come back from it.
        captions.Add(fields.AddRow("Active car:", ButtonsBeside(_cmbCars, btnAddCar)));
        fields.AddHint("The car they race in now, announced and recorded. Pick one, or type a new one and press Add: it joins their garage when you save. Retire or transfer cars in their garage.");

        captions.Add(AddLabeledTextBox(fields, "Race number:", _txtRaceNumber, Result.RaceNumber));
        captions.Add(AddLabeledTextBox(fields, "Home track:", _txtHomeTrack, Result.HomeTrack));
        captions.Add(AddLabeledTextBox(fields, "Skill level:", _txtSkillLevel, Result.SkillLevel));
        _txtSkillLevel.PlaceholderText = "e.g. Novice / Intermediate / Expert";

        captions.Add(AddLabeledTextBox(fields, "Contact:", _txtContact, Result.Contact));
        _txtContact.PlaceholderText = "organizer use only - never announced";

        // Its own box rather than read out of Contact, which is free text
        // ("Dad, 07700 900123"). Only ever used to send this racer their
        // check-in PIN.
        captions.Add(AddLabeledTextBox(fields, "Email:", _txtEmail, Result.Email));
        _txtEmail.PlaceholderText = "for check-in PINs - never shown publicly";

        // WHY (22 Oct 2026, driver experience): whether they'd like their
        // night emailed to them, which a driver can also ask for from their
        // phone. Under the address it goes to, and only enabled while there
        // is one - a tick with nowhere to send to would promise an email
        // nobody gets. Emptying the box keeps the tick as it was rather than
        // clearing it: the night email checks for an address anyway, and a
        // typo fixed by retyping the address should not lose their wish.
        _chkEmailMyNight.Text = "Email them their night";
        _chkEmailMyNight.AutoSize = true;
        _chkEmailMyNight.Checked = Result.EmailMyNight;
        fields.AddField(_chkEmailMyNight);
        fields.AddHint("When the club sends night emails: their results and report card, to the address above.");

        void UpdateEmailMyNight() => _chkEmailMyNight.Enabled = _txtEmail.Text.Trim().Length > 0;

        _txtEmail.TextChanged += (_, _) => UpdateEmailMyNight();
        UpdateEmailMyNight();

        // Every transponder the driver has besides the main one: a second
        // car's, a spare, a replacement. Type one and press Add. Retire marks
        // one no longer in use without losing what was timed on it; Remove
        // is for one added by mistake.
        _cmbTransponders.DropDownStyle = ComboBoxStyle.DropDown;
        RefillTransponders(null);

        var btnAddTransponder = new Button { Text = "Add" };
        btnAddTransponder.Click += (_, _) => AddTransponder();

        var btnRetireTransponder = new Button { Text = "Retire" };
        btnRetireTransponder.Click += (_, _) => ToggleRetired();

        var btnRemoveTransponder = new Button { Text = "Remove" };
        btnRemoveTransponder.Click += (_, _) => RemoveTransponder();

        // "Retire" or "Bring Back", for whichever is picked; Remove only for
        // an extra one - the main transponder is the driver's key.
        void UpdateTransponderButtons()
        {
            var picked = PickedTransponder();
            btnRetireTransponder.Text = picked is not null && IsRetiredHere(picked.Id) ? "Bring Back" : "Retire";
            btnRetireTransponder.Enabled = picked is not null;
            btnRemoveTransponder.Enabled = picked is { IsMain: false };
        }

        _cmbTransponders.SelectedIndexChanged += (_, _) => UpdateTransponderButtons();

        // A new driver's main transponder is typed after the form opens; the
        // list's first line follows it.
        _txtTransponder.TextChanged += (_, _) =>
            RefillTransponders((_cmbTransponders.SelectedItem as TransponderChoice)?.Id);
        _cmbTransponders.TextChanged += (_, _) => UpdateTransponderButtons();
        UpdateTransponderButtons();

        captions.Add(fields.AddRow("Transponders:", ButtonsBeside(_cmbTransponders, btnAddTransponder, btnRetireTransponder, btnRemoveTransponder)));
        fields.AddHint("Their other transponders - a second car, a spare. A retired one keeps what was timed on it.");

        _cmbClass.DropDownStyle = ComboBoxStyle.DropDownList;

        var btnManageClasses = new Button { Text = "Manage..." };
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

                // The other classes too - a rename made in there must not
                // leave "Also races" naming a class that has gone.
                for (var i = 0; i < _otherClasses.Count; i++)
                {
                    if (string.Equals(_otherClasses[i], oldName, StringComparison.OrdinalIgnoreCase))
                    {
                        _otherClasses[i] = newName;
                    }
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
            ShowOtherClasses();

            // A class added or renamed may be longer than any before it.
            FitPicker(_cmbClass, keepToRow: true);
        };

        RefreshClassCombo(Result.ClassName, keepIfUnlisted: true);
        captions.Add(fields.AddRow("Class:", ButtonsBeside(_cmbClass, btnManageClasses)));

        // Any other classes they race. Wraps rather than running past the
        // edge when there are several.
        _lblOtherClasses.MaximumSize = new Size(UiLayout.AtTextSize(UiLayout.FieldWidth), 0);
        var btnOtherClasses = new Button { Text = "Choose..." };
        btnOtherClasses.Click += (_, _) => ChooseOtherClasses();
        _cmbClass.SelectedIndexChanged += (_, _) => ShowOtherClasses();
        ShowOtherClasses();
        captions.Add(fields.AddRow("Also races:", new ButtonBar(tight: true).With(_lblOtherClasses, btnOtherClasses)));
        fields.AddHint("They go into these classes' heats as well as their main class's.");

        _txtSetupConfiguration.Multiline = true;
        _txtSetupConfiguration.AcceptsReturn = true;
        _txtSetupConfiguration.ScrollBars = ScrollBars.Vertical;
        _txtSetupConfiguration.Text = Result.SetupConfiguration;
        captions.Add(fields.AddRow("Setup configuration:", _txtSetupConfiguration, stretch: true));

        // In the fields' column, under the fields, as they were.
        _chkRental.Text = "Rental / loaner transponder";
        _chkRental.AutoSize = true;
        _chkRental.Checked = Result.IsRental;
        fields.AddField(_chkRental);

        _chkExcludeRecords.Text = "Exclude from track records and points";
        _chkExcludeRecords.AutoSize = true;
        _chkExcludeRecords.Checked = Result.ExcludeFromRecords;
        fields.AddField(_chkExcludeRecords);
        fields.AddHint("Both keep a driver out of all-time records - for guests, staff cars and loaners.");

        _txtNotes.Multiline = true;
        _txtNotes.AcceptsReturn = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;
        _txtNotes.Text = Result.Notes;
        captions.Add(fields.AddRow("Notes:", _txtNotes, stretch: true));

        // ONLINE PRIVACY (22 Oct 2026, driver experience): the three ways a
        // driver can ask to be kept off the internet - see the flags on
        // DriverProfile, which PublicPrivacy enforces for the relay. A phone
        // can only turn these on; turning one off is done here, at the desk.
        // A grid of its own under a heading, lined up with the fields above
        // (FieldGrid.LineUp, after the theme pass). The hint says the room
        // is not affected, because a club will otherwise expect the Big
        // Screen to change too.
        var privacyHeading = new Label
        {
            Text = "PRIVACY",
            AutoSize = true,
            Font = AppTheme.MicroFont,
            ForeColor = AppTheme.Primary,
        };

        var privacy = new FieldGrid();

        _chkNoPhotoOnline.Text = "Leave their photo out";
        _chkNoPhotoOnline.AutoSize = true;
        _chkNoPhotoOnline.Checked = Result.NoPhotoOnline;
        captions.Add(privacy.AddRow("Online:", _chkNoPhotoOnline));

        _chkNoVideoOnline.Text = "No podium videos of them";
        _chkNoVideoOnline.AutoSize = true;
        _chkNoVideoOnline.Checked = Result.NoVideoOnline;
        privacy.AddField(_chkNoVideoOnline);

        _chkInitialsOnline.Text = "Initials only (\"Sam Parker\" shows as \"S.P.\")";
        _chkInitialsOnline.AutoSize = true;
        _chkInitialsOnline.Checked = Result.InitialsOnline;
        privacy.AddField(_chkInitialsOnline);

        // THE ROOM (1 Nov 2026, asked for by Asher: a kid's photo kept off
        // the Big Screen's driver introduction card). A row of its own under
        // the online ones, because it is the one switch here that changes
        // what the room sees. Their name still shows.
        _chkNoPhotoBigScreen.Text = "Keep their photo off it (a young driver, say)";
        _chkNoPhotoBigScreen.AutoSize = true;
        _chkNoPhotoBigScreen.Checked = Result.NoPhotoOnBigScreen;
        captions.Add(privacy.AddRow("Big Screen:", _chkNoPhotoBigScreen));

        // "and podium videos" went from this hint on 28 Oct 2026: the videos
        // stay on this PC now (asked for by Asher), so none goes online.
        // "No podium videos of them" is kept - it is the driver's own ask,
        // they can still make it from their phone, and it is already met.
        privacy.AddHint("Online is only what goes on the internet: the club's relay pages. In the room those "
                        + "change nothing - the Wi-Fi pages, the Big Screen and the announcer still use their name and photo. "
                        + "A driver can ask for them from their phone; only the desk can turn one off. "
                        + "Big Screen keeps their photo off the driver introductions; their name still shows.");

        // The fields on the left and the strip down the right, as tall as
        // the taller of the two. The strip is as wide as SizeStrip works
        // out; until the theme pass, the photo's side.
        _stripWidth = UiLayout.AtTextSize(PhotoSide);

        var band = new Beside(() => _stripWidth)
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        // Badges, read-only, under the rest of the left column. Read here
        // rather than passed in so the form's callers don't all need a store
        // they otherwise have no use for.
        var badgeSummary = DescribeBadges(fixedTransponder ?? _txtTransponder.Text);
        var hasBadges = !string.IsNullOrWhiteSpace(badgeSummary);

        band.Add(fields, gapBelow: UiLayout.SectionGap);
        band.Add(privacyHeading, gapBelow: UiLayout.HeadingGap);
        band.Add(privacy, gapBelow: hasBadges ? UiLayout.RowGap : 0);

        if (hasBadges)
        {
            band.Add(new Label
            {
                Text = badgeSummary,
                AutoSize = true,
                ForeColor = AppTheme.TextMuted,
            }, gapBelow: 0);
        }

        var strip = BuildPhotoStrip(band);

        var btnSave = new Button { Text = "Save" };
        btnSave.Click += BtnSave_Click;
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // Everything but the buttons is the body: this dialog has more
        // fields than a small screen has height for, and on one they scroll
        // while Save and Cancel stay in view.
        var page = new FillPage();
        var body = DialogFit.Body(page);
        body.Add(band, gapBelow: 0);

        // Bottom-right of the dialog, Cancel furthest right, as a dialog's
        // buttons go - and Tab still reaches Save first, as it did: the bar
        // takes them right to left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);
        btnSave.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

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

        // After the theme pass, in the faces it leaves them in: the strip
        // and what is in it, the two note boxes their lines, and each
        // drop-down as wide as its longest name.
        SizeStrip(strip);

        // One caption column down the fields and the privacy grid, measured
        // in the faces the theme pass left the captions in - before the
        // width below, which is worked out from the captions (22 Oct 2026,
        // driver experience).
        FieldGrid.LineUp(new[] { fields, privacy });

        _txtSetupConfiguration.Height = LinesHigh(_txtSetupConfiguration, NotesLines);
        _txtNotes.Height = LinesHigh(_txtNotes, NotesLines);

        var carsWidth = FitPicker(_cmbCars, keepToRow: false);
        var classWidth = FitPicker(_cmbClass, keepToRow: false);
        var pronounsWidth = FitPicker(_cmbPronouns, keepToRow: false);
        FitPicker(_cmbTransponders, keepToRow: false);

        // The fields' column: never narrower than the text boxes were, wide
        // enough for the garage's drop-down and its two buttons on one line
        // when its names are short, as they were, and for each drop-down's
        // longest name and each tick. The buttons beside a longer name go
        // under it rather than widening the window for them. Kept in locals,
        // never read back from the controls - see QuickDriverForm.
        var carsOnOneLine = UiLayout.AtTextSize(PickerLeastWidth)
            + UiLayout.TightButtonSizeFor(btnAddCar).Width
            + 2 * UiLayout.TightButtonGap;

        var fieldColumn = new[]
        {
            UiLayout.AtTextSize(FieldColumnWidth),
            carsOnOneLine,
            carsWidth + UiLayout.TightButtonGap,
            classWidth + UiLayout.TightButtonGap,
            pronounsWidth + _cmbPronouns.Margin.Horizontal,
            _chkRental.GetPreferredSize(Size.Empty).Width,
            _chkExcludeRecords.GetPreferredSize(Size.Empty).Width,
            _chkEmailMyNight.GetPreferredSize(Size.Empty).Width,
            _chkNoPhotoOnline.GetPreferredSize(Size.Empty).Width,
            _chkNoVideoOnline.GetPreferredSize(Size.Empty).Width,
            _chkInitialsOnline.GetPreferredSize(Size.Empty).Width,
            _chkNoPhotoBigScreen.GetPreferredSize(Size.Empty).Width,

            // Hear It, measured as the bar it sits in measures it, and zero
            // when there is none. It is narrower than the boxes at every
            // text size, but the column is worked out from everything in it
            // rather than from the parts somebody remembered - which is how
            // a caption ends up cut off at 200% and nothing notices.
            btnHearName is null ? 0 : UiLayout.ButtonSizeFor(btnHearName).Width,
        }.Max();

        // The caption column as the grid works it out: its widest caption
        // and the gap after it.
        var captionColumn = captions.Max(caption => caption.GetPreferredSize(Size.Empty).Width + caption.Margin.Horizontal);

        // The width it was made at, at the text size in use, or wider when
        // the two columns need it - with room for the body's scrollbar, which
        // takes its width from the fields on a screen too short for them.
        var width = Math.Max(
            UiLayout.AtTextSize(DialogWidth),
            UiLayout.PageLeft + captionColumn + fieldColumn + UiLayout.RowGap + _stripWidth
                + SystemInformation.VerticalScrollBarWidth + UiLayout.PageRight);

        // The field column as it is when the body's scrollbar is showing -
        // the narrowest it gets - for a drop-down widened once the window
        // is up (FitPicker).
        _fieldColumn = width - UiLayout.PageLeft - captionColumn - UiLayout.RowGap - _stripWidth
                       - SystemInformation.VerticalScrollBarWidth - UiLayout.PageRight;

        DialogFit.Keep(this, page, width, body: body, widthMeasured: true);
    }

    /// <summary>
    /// The right-hand strip: photo on top, then team and sponsors - the
    /// three fields that exist for the crowd rather than the organiser,
    /// grouped away from the paperwork on the left. Down the side of
    /// <paramref name="band"/>, one under another and each as wide as the
    /// strip, the captions above their boxes as they always were. The strip
    /// gives its parts its width but not their heights: the photo, the
    /// captions, the note and the sponsors box are given theirs once the
    /// theme pass is done (SizeStrip), and this returns what that needs.
    /// </summary>
    private PhotoStrip BuildPhotoStrip(Beside band)
    {
        _photoPanel.Paint += (_, e) =>
        {
            var who = _txtDriverName.Text.Trim().Length > 0 ? _txtDriverName.Text.Trim() : Result.Transponder;
            DriverPhotos.DrawAvatar(e.Graphics, _photoPanel.ClientRectangle, who, _photoFileName);
        };
        band.AddSide(_photoPanel);

        // The initials avatar follows the name as it is typed.
        _txtDriverName.TextChanged += (_, _) => _photoPanel.Invalidate();

        var btnChoose = new Button { Text = "Choose Photo..." };
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
        band.AddSide(btnChoose, gapBelow: UiLayout.ButtonGap);

        var btnRemove = new Button { Text = "Remove Photo" };
        btnRemove.Click += (_, _) =>
        {
            _photoFileName = string.Empty;
            _photoPanel.Invalidate();
        };
        band.AddSide(btnRemove, gapBelow: UiLayout.RowGap + UiLayout.HintGap);

        var lblTeam = band.AddSide(new Label { Text = "Team / club:" }, gapBelow: UiLayout.HintGap);
        _txtTeam.Text = Result.Team;
        band.AddSide(_txtTeam);

        var lblSponsors = band.AddSide(new Label { Text = "Sponsors:" }, gapBelow: UiLayout.HintGap);
        _txtSponsors.Multiline = true;
        _txtSponsors.ScrollBars = ScrollBars.Vertical;
        _txtSponsors.Text = Result.Sponsors;
        band.AddSide(_txtSponsors, gapBelow: UiLayout.HintGap);

        var lblSponsorsHint = band.AddSide(new Label
        {
            Text = "Shown on the driver card - the works-driver treatment.",
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        }, gapBelow: 0);

        return new PhotoStrip(btnChoose, btnRemove, new[] { lblTeam, lblSponsors, lblSponsorsHint });
    }

    /// <summary>
    /// The strip's width and the heights of what is in it, in the faces the
    /// theme pass left them in. The width: the photo's side at the text size
    /// in use, or what the wider button's caption needs if that is more -
    /// measured as the strip measures its buttons (UiLayout.ButtonSizeFor),
    /// so the strip comes out exactly this wide. The photo square in it; the
    /// captions and the note each as tall as their text wrapped to it; the
    /// sponsors box its lines. Once: the dialog is built at the text size in
    /// use and is not kept in step with a change made while it is up (see
    /// DialogFit).
    /// </summary>
    private void SizeStrip(PhotoStrip strip)
    {
        _stripWidth = Math.Max(
            UiLayout.AtTextSize(PhotoSide),
            Math.Max(UiLayout.ButtonSizeFor(strip.Choose).Width, UiLayout.ButtonSizeFor(strip.Remove).Width));

        _photoPanel.Height = _stripWidth;

        foreach (var label in strip.Labels)
        {
            label.Height = label.GetPreferredSize(new Size(_stripWidth, 0)).Height;
        }

        _txtSponsors.Height = LinesHigh(_txtSponsors, SponsorsLines);
    }

    /// <summary>What the strip hands back to be sized after the theme pass: its two buttons, whose captions can widen it, and its captions and note.</summary>
    private sealed record PhotoStrip(Button Choose, Button Remove, IReadOnlyList<Label> Labels);

    /// <summary>
    /// A drop-down with its buttons after it, in one field, wrapping under it
    /// when a long name leaves them no room. The buttons only as wide as
    /// their captions: at a standard button's width the garage's two would
    /// not fit beside its drop-down, where the small buttons they replace
    /// did. The drop-down is centred up and down on the taller buttons, as
    /// FieldGrid.Inline does it.
    /// </summary>
    private static ButtonBar ButtonsBeside(ComboBox picker, params Button[] buttons)
    {
        picker.Anchor = AnchorStyles.Left;

        return new ButtonBar(tight: true).With(picker).With(buttons);
    }

    /// <summary>
    /// Sizes a drop-down to the longest name it holds, and the one it shows,
    /// never narrower than it was made at the normal text size, at the size
    /// in use - measured afresh, as its list may have changed without
    /// changing its length. Once the window is up
    /// (<paramref name="keepToRow"/>: a car added, the classes managed) never
    /// wider than the field column either - the window has its width by
    /// then, and a name longer than that is better cut short than run past
    /// the edge. The column as it is with the body's scrollbar showing, not
    /// as the row reads now: a drop-down made as wide as the row wraps its
    /// buttons under it, the body grows a scrollbar, and the row shrinks by
    /// its width under the drop-down. Returns the width.
    /// </summary>
    private int FitPicker(ComboBox picker, bool keepToRow)
    {
        (Font Font, int Count, int Width)? measured = null;

        var width = Math.Max(UiLayout.AtTextSize(PickerLeastWidth), Pieces.PickerWidth(picker, ref measured, picker.Text));

        if (keepToRow && _fieldColumn > 0)
        {
            width = Math.Min(width, Math.Max(1, _fieldColumn - picker.Margin.Horizontal));
        }

        if (picker.Width != width)
        {
            picker.Width = width;
        }

        return width;
    }

    /// <summary>A box of several lines' height for <paramref name="lines"/> lines, as the box draws them (Pieces.LineHeight).</summary>
    private static int LinesHigh(TextBox box, int lines) =>
        lines * Pieces.LineHeight(box.Font) + UiLayout.HintGap * 2;

    /// <summary>
    /// The class this profile was saved with when the form opened, when it
    /// matches none of the classes in the class store - or empty. See
    /// RefreshClassCombo.
    /// </summary>
    private string _unlistedClass = string.Empty;

    /// <summary>
    /// Fills the class drop-down and selects <paramref name="preferredSelection"/>.
    ///
    /// The selection is matched ignoring case and surrounding spaces, the
    /// way every other part of the app matches class names (a class rename,
    /// the class rosters, the CSV import's championship lookup). It used to
    /// be an exact match, and anything it missed showed as "(none)" - which
    /// Save then wrote, so a driver imported from a sheet as "stock buggy"
    /// lost their "Stock Buggy" class the first time anybody opened them to
    /// change a race number, and dropped out of that class's heats and
    /// standings without a word.
    ///
    /// For the same reason a class the profile already has that is not in
    /// the class store at all (a sheet can name a class nobody has set up
    /// yet) is kept as an item of its own and selected, rather than being
    /// swapped for "(none)". Only the profile's own class is kept like that;
    /// any other name that matches nothing still falls back to "(none)", as
    /// before. Once a class of that name is added to the store the extra
    /// item goes, and the store's spelling is the one selected.
    /// </summary>
    private void RefreshClassCombo(string? preferredSelection, bool keepIfUnlisted = false)
    {
        var wanted = preferredSelection?.Trim() ?? string.Empty;

        _cmbClass.Items.Clear();
        _cmbClass.Items.Add("(none)");
        foreach (var c in _classStore.All)
        {
            _cmbClass.Items.Add(c.Name);
        }

        if (keepIfUnlisted && wanted.Length > 0 && IndexOfClass(wanted) < 0)
        {
            _unlistedClass = wanted;
        }

        if (_unlistedClass.Length > 0 && IndexOfClass(_unlistedClass) < 0)
        {
            _cmbClass.Items.Add(_unlistedClass);
        }

        var idx = wanted.Length > 0 ? IndexOfClass(wanted) : -1;
        _cmbClass.SelectedIndex = idx >= 0 ? idx : 0;
    }

    /// <summary>The class in the Class box now, or empty for "(none)".</summary>
    private string MainClassShown() =>
        _cmbClass.SelectedItem as string is { } shown && shown != "(none)" ? shown.Trim() : string.Empty;

    /// <summary>"Also races": the other classes, less the main one, or "none".</summary>
    private void ShowOtherClasses()
    {
        var main = MainClassShown();
        var others = _otherClasses
            .Where(c => !string.Equals(c, main, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _lblOtherClasses.Text = others.Count == 0 ? "none" : string.Join(", ", others);
    }

    /// <summary>Choose... beside "Also races".</summary>
    private void ChooseOtherClasses()
    {
        using var form = new OtherClassesForm(
            MainClassShown(),
            _classStore.All.Select(c => c.Name).ToList(),
            _otherClasses);

        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _otherClasses = form.Result.ToList();
            ShowOtherClasses();
        }
    }

    /// <summary>Where a class of this name is in the drop-down, ignoring case and spaces, or -1. "(none)" is never a match.</summary>
    private int IndexOfClass(string name)
    {
        for (var i = 1; i < _cmbClass.Items.Count; i++)
        {
            if (_cmbClass.Items[i] is string item
                && string.Equals(item.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>One line in the transponder drop-down: the main one or an extra, marked when retired.</summary>
    private sealed record TransponderChoice(string Id, bool IsMain, bool Retired)
    {
        public override string ToString() =>
            Id + (IsMain ? " (main)" : string.Empty) + (Retired ? " (retired)" : string.Empty);
    }

    /// <summary>The main transponder as typed in the Transponder box.</summary>
    private string MainHere => _txtTransponder.Text.Trim();

    private OwnedTransponder? EntryHere(string id) =>
        _transponders.FirstOrDefault(t => string.Equals(t.Id?.Trim(), id, StringComparison.OrdinalIgnoreCase));

    private bool IsRetiredHere(string id) => EntryHere(id)?.Retired == true;

    /// <summary>Fills the transponder drop-down - the main one first - and picks <paramref name="select"/> when given.</summary>
    private void RefillTransponders(string? select)
    {
        _cmbTransponders.Items.Clear();

        if (MainHere.Length > 0)
        {
            _cmbTransponders.Items.Add(new TransponderChoice(MainHere, true, IsRetiredHere(MainHere)));
        }

        foreach (var entry in _transponders)
        {
            var id = entry.Id?.Trim() ?? string.Empty;

            if (id.Length > 0 && !string.Equals(id, MainHere, StringComparison.OrdinalIgnoreCase))
            {
                _cmbTransponders.Items.Add(new TransponderChoice(id, false, entry.Retired));
            }
        }

        var match = _cmbTransponders.Items.Cast<TransponderChoice>()
            .FirstOrDefault(c => string.Equals(c.Id, select, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            _cmbTransponders.SelectedItem = match;
        }
        else
        {
            _cmbTransponders.Text = string.Empty;
        }
    }

    /// <summary>
    /// What is typed in the transponder drop-down, when it is not just a
    /// listed line picked - or empty. Compared with the picked line's text
    /// rather than read from SelectedItem alone: an editable drop-down can
    /// keep its pick while something else is typed over it.
    /// </summary>
    private string TypedTransponder()
    {
        var text = _cmbTransponders.Text.Trim();

        return _cmbTransponders.SelectedItem is TransponderChoice picked
               && string.Equals(picked.ToString(), _cmbTransponders.Text, StringComparison.Ordinal)
            ? string.Empty
            : text;
    }

    /// <summary>The listed line picked, when nothing else is typed over it.</summary>
    private TransponderChoice? PickedTransponder() =>
        TypedTransponder().Length == 0 ? _cmbTransponders.SelectedItem as TransponderChoice : null;

    /// <summary>
    /// Adds the transponder typed in the drop-down to the driver's list.
    /// False when it was refused (and the reason shown); true when it was
    /// added, or there was nothing new to add.
    /// </summary>
    private bool AddTransponder()
    {
        var id = TypedTransponder();

        if (id.Length == 0)
        {
            return true;
        }

        if (id.Length > 40)
        {
            AppDialog.Show(this, "That transponder number is too long.", "Transponder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (string.Equals(id, MainHere, StringComparison.OrdinalIgnoreCase) || EntryHere(id) is not null)
        {
            RefillTransponders(id);
            return true;
        }

        // Somebody else's already? Said now rather than at Save.
        if (_profiles?.OwnerOf(id) is { } owner
            && !string.Equals(owner.Transponder, _editingTransponder, StringComparison.OrdinalIgnoreCase))
        {
            var who = string.IsNullOrWhiteSpace(owner.DriverName) ? $"the profile for {owner.Transponder}" : owner.DriverName.Trim();
            AppDialog.Show(this, $"Transponder {id} already belongs to {who}. A transponder can only belong to one driver.",
                "Transponder already in use", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        _transponders.Add(new OwnedTransponder { Id = id, AddedUtc = DateTime.UtcNow });
        RefillTransponders(id);
        FitPicker(_cmbTransponders, keepToRow: true);
        return true;
    }

    /// <summary>Retires the picked transponder, or brings a retired one back into use.</summary>
    private void ToggleRetired()
    {
        if (PickedTransponder() is not { } picked)
        {
            return;
        }

        var entry = EntryHere(picked.Id);

        if (entry is not { Retired: true } && !RetireConfirm.Ask(this, $"transponder {picked.Id}"))
        {
            return;
        }

        if (entry is null)
        {
            // The main transponder has an entry only once it is retired or
            // fitted to a car.
            entry = new OwnedTransponder { Id = picked.Id };
            _transponders.Add(entry);
        }

        entry.Retired = !entry.Retired;
        entry.RetiredUtc = entry.Retired ? DateTime.UtcNow : null;
        RefillTransponders(picked.Id);
    }

    /// <summary>Takes an extra transponder off the driver - for one added by mistake.</summary>
    private void RemoveTransponder()
    {
        if (PickedTransponder() is not { IsMain: false } picked)
        {
            return;
        }

        if (AppDialog.Show(this,
                $"Remove transponder {picked.Id} from this driver?\n\nLaps already timed on it will no longer count as theirs. "
                + "If it is just no longer used, Retire it instead - that keeps them.",
                "Remove transponder", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _transponders.RemoveAll(t => string.Equals(t.Id?.Trim(), picked.Id, StringComparison.OrdinalIgnoreCase));
        RefillTransponders(null);
        FitPicker(_cmbTransponders, keepToRow: true);
    }

    /// <summary>A caption and its text box, filled in, the box running to the grid's right-hand edge. Returns the caption.</summary>
    private static Label AddLabeledTextBox(FieldGrid fields, string label, TextBox box, string initialValue)
    {
        box.Text = initialValue;
        return fields.AddRow(label, box, stretch: true);
    }

    /// <summary>
    /// The name as the announcer would read it NOW - from the boxes as they
    /// stand, not from the saved profile, because the whole point is to try
    /// a spelling before committing it.
    ///
    /// Built as a DriverProfile and read back through its SpokenName rather
    /// than picking the first non-blank box here. SpokenName is what the
    /// voice queue reads when it fills {driver}
    /// (AnnouncementService.VoiceQueue.cs), so going through it is the only
    /// thing that guarantees this says what race night will say. The same
    /// three-step ladder written out a second time is a second thing to keep
    /// in step, and it would be the wrong one the week somebody changed the
    /// first.
    /// </summary>
    internal string NameAsAnnounced() => new DriverProfile
    {
        Transponder = _txtTransponder.Text.Trim(),
        DriverName = _txtDriverName.Text.Trim(),
        AnnouncerName = _txtAnnouncerName.Text.Trim(),
    }.SpokenName;

    /// <summary>
    /// Reads the announcer name out loud, exactly as the announcer will.
    ///
    /// Through SpeakTest, which speaks even while the announcer is muted and
    /// jumps the drop-while-speaking rule - a test that silently does
    /// nothing because a race call happened to be going out reads as the
    /// button being broken.
    /// </summary>
    private void HearAnnouncerName()
    {
        var name = NameAsAnnounced();

        if (name.Length == 0)
        {
            AppDialog.Show(this,
                "Fill in a driver name or an announcer name first, and this reads it out exactly as the announcer will.",
                "Nothing to read", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _speakName?.Invoke(name);
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        var transponder = _txtTransponder.Text.Trim();
        if (string.IsNullOrWhiteSpace(transponder))
        {
            AppDialog.Show(this, "Transponder is required.", "Missing transponder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // One typed but not yet Added counts, as a typed car does - and the
        // old Alt transponder box saved whatever was in it. Refused (too
        // long, somebody else's), the form stays open.
        if (!AddTransponder())
        {
            return;
        }

        var saved = ProfileAsShown();

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
    /// The profile as the dialog shows it now - what Save keeps. A brand-new
    /// DriverProfile, so every field the profile has must be copied here,
    /// from its box or, for one this dialog does not show, from the profile
    /// it was opened on: a field left out is wiped by every desk edit.
    /// Taken out of Save on 22 Oct 2026 (driver experience) so the tests can
    /// check exactly that.
    /// </summary>
    internal DriverProfile ProfileAsShown()
    {
        var transponder = _txtTransponder.Text.Trim();
        var mainClass = _cmbClass.SelectedItem as string == "(none)" ? string.Empty : (string)(_cmbClass.SelectedItem ?? string.Empty);

        return new DriverProfile
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
            ClassName = mainClass,

            // The other classes, less the main one if it was ticked there too.
            Classes = _otherClasses
                .Where(c => !string.IsNullOrWhiteSpace(c) && !string.Equals(c.Trim(), mainClass.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SetupConfiguration = _txtSetupConfiguration.Text,
            Notes = _txtNotes.Text,

            AnnouncerName = _txtAnnouncerName.Text.Trim(),
            Pronouns = Pronouns.Clean(_cmbPronouns.Text),
            RaceNumber = _txtRaceNumber.Text.Trim(),
            HomeTrack = _txtHomeTrack.Text.Trim(),
            SkillLevel = _txtSkillLevel.Text.Trim(),
            Contact = _txtContact.Text.Trim(),
            Email = _txtEmail.Text.Trim(),
            Transponders = _transponders.ToList(),

            // Not on this form - the driver's Transponders tab has them -
            // so they are kept as they were rather than lost on an edit.
            SplitTransponders = System.Text.Json.JsonSerializer.Deserialize<List<OwnedTransponder>>(
                                    System.Text.Json.JsonSerializer.Serialize(Result.SplitTransponders ?? new List<OwnedTransponder>()))
                                ?? new List<OwnedTransponder>(),
            IsRental = _chkRental.Checked,
            ExcludeFromRecords = _chkExcludeRecords.Checked,

            PhotoFileName = _photoFileName,
            Team = _txtTeam.Text.Trim(),
            Sponsors = _txtSponsors.Text.Trim(),

            // Preserved on edit, stamped on create - so "new this season"
            // reporting has something to work from. An edit keeps exactly
            // what the profile had, even when that is nothing: a driver saved
            // before this date was recorded has none, and ProfileStore
            // deliberately leaves it that way rather than back-dating a
            // long-time regular to today (see StampFirstSeen). Filling it in
            // here would undo that on the first edit, and would also make an
            // unchanged Save look like a change, recording a needless
            // "Edit driver" undo entry. _editingTransponder is set only when
            // the form was opened on an existing profile.
            FirstSeenUtc = _editingTransponder is null
                ? Result.FirstSeenUtc ?? DateTime.UtcNow
                : Result.FirstSeenUtc,

            // WHY (22 Oct 2026, driver experience): the driver's own choices,
            // which a phone can set too. All five are on this dialog and are
            // saved as they stand in it - the privacy ticks included, so a
            // desk edit can turn one off (the only place that can) and never
            // turns one off by leaving it out. Save used to build this
            // profile from the boxes alone, so a field it did not copy came
            // back empty after any edit at all.
            NoPhotoOnline = _chkNoPhotoOnline.Checked,
            NoVideoOnline = _chkNoVideoOnline.Checked,
            InitialsOnline = _chkInitialsOnline.Checked,
            NoPhotoOnBigScreen = _chkNoPhotoBigScreen.Checked,
            EmailMyNight = _chkEmailMyNight.Checked,
            AboutLine = string.Equals(_txtAboutLine.Text, Result.AboutLine ?? string.Empty, StringComparison.Ordinal)
                ? Result.AboutLine ?? string.Empty
                : AboutLineFrom(_txtAboutLine.Text),
        };
    }

    /// <summary>
    /// The About them box as it is kept: on one line, trimmed, and no longer
    /// than DriverProfile.AboutLineMaxChars - the box stops typing there,
    /// but a paste can carry line breaks. An unchanged box keeps the
    /// profile's own text exactly (ProfileAsShown), so opening a driver and
    /// saving them unchanged is never a change (22 Oct 2026, driver
    /// experience).
    /// </summary>
    internal static string AboutLineFrom(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
        {
            return string.Empty;
        }

        var text = System.Text.RegularExpressions.Regex.Replace(typed, @"\s+", " ").Trim();

        if (text.Length > DriverProfile.AboutLineMaxChars)
        {
            var cut = DriverProfile.AboutLineMaxChars;

            // Never split a surrogate pair - half an emoji is invalid text.
            if (char.IsHighSurrogate(text[cut - 1]))
            {
                cut--;
            }

            text = text[..cut].TrimEnd();
        }

        return text;
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

            return $"Achievements ({earned.Count}):  {shown}{more}";
        }
        catch
        {
            // Decoration. Never the reason a driver can't be edited.
            return string.Empty;
        }
    }
}
