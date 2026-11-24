namespace HeatCaller;

/// <summary>
/// Add/edit dialog for a single DriverProfile. Controls are built directly
/// in code rather than a separate .Designer.cs partial - functionally
/// identical, just fewer files for a form this small.
///
/// On the layout pieces (DialogFit): a card across the top saying who the
/// driver is at a glance - their photo or initials, their name, a line of
/// transponder, number, class and car that follows the boxes, the badges
/// they have earned, and the photo's two buttons - then the fields in cards
/// by what they are for, each with a line saying so: who they are, car and
/// class, on the PA, their crew, junior, privacy, contact, the driver card
/// (team and sponsors), records, and notes (18 Nov 2026, dialog jazz-up).
/// Every card's captions share one column, as wide as the longest; the
/// garage and the class are each a drop-down with its buttons beside it
/// (under it, when a long name leaves them no room). All of that is the
/// window's body, which scrolls on a screen too short for it, and Save and
/// Cancel are under it at the foot. It opens at least as wide as it was made
/// at the normal text size, at the size in use, and wider when its columns
/// need more: the garage's drop-down and its two buttons on one line, a
/// drop-down's longest name, the body's scrollbar on a short screen. Before
/// the cards it was one long field grid with the photo, team and sponsors in
/// a strip down the right; before that it was placed in pixels in a 640x792
/// window - taller than a 1280x720 screen has room for,
/// so Save and Cancel were off the bottom of it - with the captions in a
/// 124px column, both drop-downs 200px wide whatever they held, and the
/// badges line laid over the bottom of the notes box.
/// </summary>
public sealed class ProfileForm : Form
{
    /// <summary>
    /// How wide the window is inside its frame at the normal text size, at
    /// the least - about the width it had with the photo strip down the
    /// right, which gives the cards' fields room: the transponders'
    /// drop-down and its three buttons on one line, the ticks on one.
    /// </summary>
    private const int DialogWidth = 780;

    /// <summary>The fields' column at the normal text size, at the least: the 320px the text boxes had.</summary>
    private const int FieldColumnWidth = 320;

    /// <summary>A drop-down's width at the normal text size - wider when its longest name needs it.</summary>
    private const int PickerLeastWidth = 200;

    /// <summary>The photo's side in the top card, at the normal text size.</summary>
    private const int PhotoSide = 112;

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

    // The car's body colour (stream overlay plan 6.5): a swatch, Choose and None.
    private readonly Panel _pnlBodyColour = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly Button _btnBodyColour = new() { Text = "Choose..." };
    private readonly Button _btnBodyColourNone = new() { Text = "None" };
    private string _bodyColour = string.Empty;
    private readonly TextBox _txtHomeTrack = new();
    private readonly TextBox _txtSkillLevel = new();
    private readonly CheckBox _chkNovice = new ThemedCheckBox();
    private readonly TextBox _txtMentor = new();
    private readonly TextBox _txtContact = new();
    private readonly TextBox _txtEmail = new();

    /// <summary>A line in their own words for the AI commentary - see DriverProfile.AboutLine.</summary>
    private readonly TextBox _txtAboutLine = new();
    private readonly TextBox _txtCrewPrincipal = new();
    private readonly TextBox _txtCrewTech = new();

    /// <summary>How their crew's names are said (19 Nov 2026) - see DriverProfile.CrewPrincipalSaidAs.</summary>
    private readonly TextBox _txtCrewPrincipalSay = new();
    private readonly TextBox _txtCrewTechSay = new();

    /// <summary>
    /// Their crew's voices, picked at the desk (19 Nov 2026) from the list a
    /// phone is offered - built only when there is one (drivers' crews on and
    /// the engine's voices listed). See CrewVoiceChoices.
    /// </summary>
    private ComboBox? _cmbCrewPrincipalVoice;
    private ComboBox? _cmbCrewTechVoice;
    private IReadOnlyList<CrewVoiceChoice> _crewPrincipalChoices = Array.Empty<CrewVoiceChoice>();
    private IReadOnlyList<CrewVoiceChoice> _crewTechChoices = Array.Empty<CrewVoiceChoice>();
    private readonly TextBox _txtCrewGoal = new();
    private readonly TextBox _txtCrewThanks = new();
    private readonly TextBox _txtCrewCar = new();
    private readonly CheckBox _chkCrewConsent = new ThemedCheckBox();

    /// <summary>Junior and their age band (15 Nov 2026) - see AddJuniorRow.</summary>
    private readonly CheckBox _chkJunior = new ThemedCheckBox();
    private readonly ComboBox _cmbJuniorAge = new();

    /// <summary>A junior on the stream (15 Nov 2026) - see DriverProfile.JuniorOffStream and StreamConsent.</summary>
    private readonly ComboBox _cmbOnStream = new();
    private readonly CheckBox _chkStreamConsent = new ThemedCheckBox();

    /// <summary>The "On stream:" choices, in the order the drop-down lists them: false is Shortened, true is Not at all.</summary>
    internal static readonly (bool OffStream, string Words)[] OnStreamChoices =
    {
        (false, "Shortened (Mia K)"),
        (true, "Not at all"),
    };

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
    private readonly CheckBox _chkVisitor = new ThemedCheckBox();
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

    /// <summary>The photo's side in the top card at the text size in use - see BuildHeader.</summary>
    private int _photoSide;

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
    /// <param name="crewVoices">
    /// The voices a driver may pick for their crew, as the phone is offered
    /// them (MainForm.CrewVoicesForDesk), or null when there are none to
    /// offer - then the voices are only described, as before (19 Nov 2026).
    /// </param>
    internal ProfileForm(DriverProfile? existing, RaceClassStore classStore, string? fixedTransponder = null, UndoService? undo = null, ProfileStore? profiles = null, Action<string>? speakName = null, IReadOnlyList<CrewVoiceOption>? crewVoices = null)
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

        // Built with its layout held, and let go once it is all measured -
        // see LayoutFreeze.HoldWhileBuilding, and the Setup Sheet and the
        // template editor, which do the same (18 Nov 2026). Built live, its
        // forty-odd rows laid themselves out again for every row added, for
        // every face the theme pass changed, and for every size set after
        // it - each pass seconds on a busy PC - and the dialog took 15 to 30
        // seconds to open, which timed out the tests that open it.
        using var building = LayoutFreeze.HoldWhileBuilding();

        // The cards (18 Nov 2026, dialog jazz-up): a caption and its field
        // on each row of each card, the captions of every card in one column
        // as wide as the longest of them (FieldGrid.LineUp, after the theme
        // pass), and every text box running to the card's right-hand edge.
        // One grid per card, filled in the order the fields were always
        // built in; the cards' order down the window is set at the end.
        var who = new FieldGrid();
        var voice = new FieldGrid();
        var crew = new FieldGrid();
        var junior = new FieldGrid();
        var carGrid = new FieldGrid();
        var contact = new FieldGrid();
        var records = new FieldGrid();
        var crowd = new FieldGrid();
        var captions = new List<Label>();

        captions.Add(AddLabeledTextBox(who, "Transponder:", _txtTransponder, Result.Transponder));
        _txtTransponder.ReadOnly = fixedTransponder is not null;

        captions.Add(AddLabeledTextBox(who, "Driver name:", _txtDriverName, Result.DriverName));

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
        captions.Add(AddLabeledTextBox(voice, "Announcer name:", _txtAnnouncerName, Result.AnnouncerName));
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
            voice.AddField(new ButtonBar().With(btnHearName));
        }

        var announcerHint =
            "Read out instead of their real name: a nickname, or the name spelled the way it SOUNDS to fix one the "
            + "voice gets wrong (\"Jacques\" as \"Zhock\"). Their real name is used everywhere else.";

        if (btnHearName is not null)
        {
            announcerHint += " Hear It plays it as the announcer will say it.";
        }

        voice.AddHint(announcerHint);

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

        captions.Add(voice.AddRow("Pronouns:", _cmbPronouns));
        voice.AddHint("Optional. The AI commentary uses these instead of guessing from their name. Pick one, or type "
                       + "their own (like \"she/they\").");

        // WHY (22 Oct 2026, driver experience): a line about them in their
        // own words, for the AI commentary - beside the pronouns, the other
        // thing on here only the commentary reads. A driver can now ask for
        // it from their own phone, and the desk sees and edits it here.
        // Never shown publicly. Capped where the phone's is (AboutLineMaxChars).
        captions.Add(AddLabeledTextBox(voice, "About them:", _txtAboutLine, Result.AboutLine ?? string.Empty));
        _txtAboutLine.MaxLength = DriverProfile.AboutLineMaxChars;
        _txtAboutLine.PlaceholderText = "optional - for the AI commentary, in their own words";
        voice.AddHint($"For the AI commentary: a line in their own words, up to {DriverProfile.AboutLineMaxChars} characters "
                       + "(like \"racing since I was eight; my dad builds the cars\"). Never shown publicly.");

        // THEIR OWN CREW AND NOTES (15 Nov 2026, driver crew plan): beside
        // the about line, the other words of theirs only the commentary
        // reads. A phone can send these too; the desk sees and edits them
        // here. The voices they picked are kept as they are - a phone picks
        // those from the club's whole list (DriverCrew).
        captions.Add(AddLabeledTextBox(crew, "Their principal:", _txtCrewPrincipal, Result.CrewPrincipalName ?? string.Empty));
        _txtCrewPrincipal.MaxLength = DriverCrew.NameMaxChars;
        _txtCrewPrincipal.PlaceholderText = "optional - blank for the club's Team Principal";
        captions.Add(AddLabeledTextBox(crew, "Said as:", _txtCrewPrincipalSay, Result.CrewPrincipalSaidAs ?? string.Empty));
        _txtCrewPrincipalSay.MaxLength = DriverCrew.SaidAsMaxChars;
        _txtCrewPrincipalSay.PlaceholderText = "optional - how the name sounds";
        _cmbCrewPrincipalVoice = AddCrewVoiceRow(crew, captions, crewVoices, Result.CrewPrincipalVoice, out _crewPrincipalChoices);
        captions.Add(AddLabeledTextBox(crew, "Their tech lead:", _txtCrewTech, Result.CrewTechName ?? string.Empty));
        _txtCrewTech.MaxLength = DriverCrew.NameMaxChars;
        _txtCrewTech.PlaceholderText = "optional - blank for the club's Technical Lead";
        captions.Add(AddLabeledTextBox(crew, "Said as:", _txtCrewTechSay, Result.CrewTechSaidAs ?? string.Empty));
        _txtCrewTechSay.MaxLength = DriverCrew.SaidAsMaxChars;
        _txtCrewTechSay.PlaceholderText = "optional - how the name sounds";
        _cmbCrewTechVoice = AddCrewVoiceRow(crew, captions, crewVoices, Result.CrewTechVoice, out _crewTechChoices);
        crew.AddHint("Made-up names for their own Team Principal and Technical Lead. Letters, spaces, hyphens and "
                       + "apostrophes. \"Said as\" is the name spelled the way it sounds, as for an announcer name. "
                       + (crewVoices is null ? CrewVoicesHint(Result) : "A driver can pick the voices from their phone too."));

        captions.Add(AddLabeledTextBox(crew, "Tonight's goal:", _txtCrewGoal, DriverCrew.GoalTonight(Result, DateTime.Now)));
        _txtCrewGoal.MaxLength = DriverCrew.GoalMaxChars;
        _txtCrewGoal.PlaceholderText = "optional - said before their race tonight only";
        captions.Add(AddLabeledTextBox(crew, "Thanks:", _txtCrewThanks, Result.CrewThanks ?? string.Empty));
        _txtCrewThanks.MaxLength = DriverCrew.ThanksMaxChars;
        _txtCrewThanks.PlaceholderText = "optional - who they'd like to thank";
        captions.Add(AddLabeledTextBox(crew, "Car's story:", _txtCrewCar, Result.CrewCarStory ?? string.Empty));
        _txtCrewCar.MaxLength = DriverCrew.CarStoryMaxChars;
        _txtCrewCar.PlaceholderText = "optional - for the Technical Lead";
        crew.AddHint("In their own words, for the principal and the Technical Lead. Spoken over the PA and the stream; "
                       + "never shown online.");

        AddJuniorRow(junior, captions);

        // A junior's notes and about line are said only with a parent's
        // consent recorded here (15 Nov 2026) - see DriverCrew.OwnWordsMayBeSaid.
        _chkCrewConsent.Text = "A parent or guardian agreed these notes and the about line can be said on the PA and the stream";
        _chkCrewConsent.AutoSize = true;
        _chkCrewConsent.Checked = Result.CrewNotesParentConsent;
        junior.AddRow(string.Empty, _chkCrewConsent);
        junior.AddHint("For a junior. Without it their notes and about line are kept but never said. Ask the parent at the desk, "
                       + "as for the waiver.");

        AddOnStreamRows(junior, captions);

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
        captions.Add(carGrid.AddRow("Active car:", ButtonsBeside(_cmbCars, btnAddCar)));
        carGrid.AddHint("The car they race in now, announced and recorded. Pick one, or type a new one and press Add: it joins their garage when you save. Retire or transfer cars in their garage.");

        captions.Add(AddLabeledTextBox(who, "Race number:", _txtRaceNumber, Result.RaceNumber));

        _bodyColour = CarColour.Clean(Result.BodyColour);
        _pnlBodyColour.Size = new Size(UiLayout.AtTextSize(44), UiLayout.AtTextSize(24));
        _btnBodyColour.Click += (_, _) => ChooseBodyColour();
        _btnBodyColourNone.Click += (_, _) => { _bodyColour = string.Empty; ShowBodyColour(); };
        ShowBodyColour();
        captions.Add(carGrid.AddRow("Car colour:", FieldGrid.Inline(_pnlBodyColour, _btnBodyColour, _btnBodyColourNone)));
        carGrid.AddHint("The main colour of their car's body. It goes beside their name on the stream, so viewers can tell the cars apart.");
        captions.Add(AddLabeledTextBox(who, "Home track:", _txtHomeTrack, Result.HomeTrack));
        captions.Add(AddLabeledTextBox(who, "Skill level:", _txtSkillLevel, Result.SkillLevel));
        _txtSkillLevel.PlaceholderText = "e.g. Novice / Intermediate / Expert";

        // New to racing (12 Nov 2026, setup plan D12).
        _chkNovice.Text = "New to racing (the desk shows it, and the announcer welcomes them)";
        _chkNovice.AutoSize = true;
        _chkNovice.Checked = Result.IsNovice;
        who.AddField(_chkNovice);
        captions.Add(AddLabeledTextBox(who, "Mentor:", _txtMentor, Result.Mentor ?? string.Empty));
        _txtMentor.PlaceholderText = "who's looking after them tonight";

        captions.Add(AddLabeledTextBox(contact, "Contact:", _txtContact, Result.Contact));
        _txtContact.PlaceholderText = "organizer use only - never announced";

        // Its own box rather than read out of Contact, which is free text
        // ("Dad, 07700 900123"). Only ever used to send this racer their
        // check-in PIN.
        captions.Add(AddLabeledTextBox(contact, "Email:", _txtEmail, Result.Email));
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
        contact.AddField(_chkEmailMyNight);
        contact.AddHint("When the club sends night emails: their results and report card, to the address above.");

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
        // WHY (23 Nov 2026, bug hunt 24): an extra transponder typed in the
        // drop-down but not yet Added is put back after the refill. This
        // used to refill with only the picked line, so the first keystroke
        // in the main box blanked the typed one and Save, which counts a
        // typed-but-not-Added transponder, saved the driver without it.
        _txtTransponder.TextChanged += (_, _) =>
        {
            var typed = TypedTransponder();
            RefillTransponders(typed.Length == 0 ? (_cmbTransponders.SelectedItem as TransponderChoice)?.Id : null);

            if (typed.Length > 0)
            {
                _cmbTransponders.Text = typed;
            }
        };
        _cmbTransponders.TextChanged += (_, _) => UpdateTransponderButtons();
        UpdateTransponderButtons();

        captions.Add(carGrid.AddRow("Transponders:", ButtonsBeside(_cmbTransponders, btnAddTransponder, btnRetireTransponder, btnRemoveTransponder)));
        carGrid.AddHint("Their other transponders - a second car, a spare. A retired one keeps what was timed on it.");

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
        captions.Add(carGrid.AddRow("Class:", ButtonsBeside(_cmbClass, btnManageClasses)));

        // Any other classes they race. Wraps rather than running past the
        // edge when there are several.
        _lblOtherClasses.MaximumSize = new Size(UiLayout.AtTextSize(UiLayout.FieldWidth), 0);
        var btnOtherClasses = new Button { Text = "Choose..." };
        btnOtherClasses.Click += (_, _) => ChooseOtherClasses();
        _cmbClass.SelectedIndexChanged += (_, _) => ShowOtherClasses();
        ShowOtherClasses();
        captions.Add(carGrid.AddRow("Also races:", new ButtonBar(tight: true).With(_lblOtherClasses, btnOtherClasses)));
        carGrid.AddHint("They go into these classes' heats as well as their main class's.");

        _txtSetupConfiguration.Multiline = true;
        _txtSetupConfiguration.AcceptsReturn = true;
        _txtSetupConfiguration.ScrollBars = ScrollBars.Vertical;
        _txtSetupConfiguration.Text = Result.SetupConfiguration;
        captions.Add(carGrid.AddRow("Setup configuration:", _txtSetupConfiguration, stretch: true));

        // In the fields' column, under the fields, as they were.
        _chkRental.Text = "Rental / loaner transponder";
        _chkRental.AutoSize = true;
        _chkRental.Checked = Result.IsRental;
        records.AddField(_chkRental);

        _chkExcludeRecords.Text = "Exclude from track records and points";
        _chkExcludeRecords.AutoSize = true;
        _chkExcludeRecords.Checked = Result.ExcludeFromRecords;
        records.AddField(_chkExcludeRecords);
        records.AddHint("Both keep a driver out of all-time records - for guests, staff cars and loaners.");

        _chkVisitor.Text = "Visiting from another club";
        _chkVisitor.AutoSize = true;
        _chkVisitor.Checked = Result.Visitor;
        records.AddField(_chkVisitor);
        records.AddHint("The announcer's opening counts these: \"six of them visiting\".");

        _txtNotes.Multiline = true;
        _txtNotes.AcceptsReturn = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;
        _txtNotes.Text = Result.Notes;

        // ONLINE PRIVACY (22 Oct 2026, driver experience): the three ways a
        // driver can ask to be kept off the internet - see the flags on
        // DriverProfile, which PublicPrivacy enforces for the relay. A phone
        // can only turn these on; turning one off is done here, at the desk.
        // A card of its own, lined up with the other cards' fields
        // (FieldGrid.LineUp, after the theme pass). The hint says the room
        // is not affected, because a club will otherwise expect the Big
        // Screen to change too.
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

        // Team and sponsors: the two fields for the crowd rather than the
        // organiser, in a card of their own - see BuildCrowdRows.
        BuildCrowdRows(crowd, captions);

        // Badges, read-only, in the card at the top. Read here rather than
        // passed in so the form's callers don't all need a store they
        // otherwise have no use for.
        var badgeSummary = DescribeBadges(fixedTransponder ?? _txtTransponder.Text);

        var header = BuildHeader(badgeSummary, adding: existing is null);

        var btnSave = new Button { Text = "Save" };
        btnSave.Click += BtnSave_Click;
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // THE CARDS (18 Nov 2026, dialog jazz-up): who the driver is at a
        // glance across the top - their photo, name and number - then the
        // fields in cards by what they are for, each with a line saying so.
        // It was one long grid of thirty-odd rows, with the photo, team and
        // sponsors in a strip down the right. The junior card keeps its rows
        // and words exactly as they were, and sits straight under the crew
        // card its notes consent is about.
        var cards = new[]
        {
            Card(new Section("Who they are"), "Their name and number, as the desk and the results show them.", who),
            Card(new Section("Car and class"), "What they race, and which heats they go in.", carGrid),
            Card(new Section("On the PA"), "How the announcer and the AI commentary talk about them.", voice),
            Card(new Section("Their crew"), "Their own made-up pit crew and a few words for the race calls. All optional.", crew),
            Card(new Section("Junior"), "For a driver under 18, and what a parent or guardian agreed to.", junior),
            Card(new Section("Privacy"), "What they asked to keep off the internet and the Big Screen.", privacy),
            Card(new Section("Contact"), "For the desk only. Never announced.", contact),
            Card(new Section("Driver card"), "Shown on their driver card - the works-driver treatment.", crowd),
            Card(new Section("Records"), "Who counts in the club's records, and in the visitor count.", records),
        };

        var notesCard = new Section("Notes");
        notesCard.Add(CardHint("Anything else the desk should know about them."), gapBelow: UiLayout.RowGap);
        notesCard.Add(_txtNotes, gapBelow: 0);

        // Everything but the buttons is the body: this dialog has more
        // fields than a small screen has height for, and on one they scroll
        // while Save and Cancel stay in view.
        var page = new FillPage();
        var body = DialogFit.Body(page);
        body.Add(header.Card, gapBelow: UiLayout.SectionGap);

        foreach (var card in cards)
        {
            body.Add(card, gapBelow: UiLayout.RowGap);
        }

        body.Add(notesCard, gapBelow: 0);

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

        AppTheme.StyleButton(btnSave, AppTheme.ButtonKind.Primary);

        // After the theme pass, in the faces it leaves them in: the top
        // card's photo and words, the three note boxes their lines, and
        // each drop-down as wide as its longest name.
        SizeHeader(header);

        // One caption column down every card, measured in the faces the
        // theme pass left the captions in - before the width below, which
        // is worked out from the captions (22 Oct 2026, driver experience).
        FieldGrid.LineUp(new[] { who, carGrid, voice, crew, junior, privacy, contact, crowd, records });

        _txtSetupConfiguration.Height = LinesHigh(_txtSetupConfiguration, NotesLines);
        _txtNotes.Height = LinesHigh(_txtNotes, NotesLines);
        _txtSponsors.Height = LinesHigh(_txtSponsors, SponsorsLines);

        var carsWidth = FitPicker(_cmbCars, keepToRow: false);
        var classWidth = FitPicker(_cmbClass, keepToRow: false);
        var pronounsWidth = FitPicker(_cmbPronouns, keepToRow: false);
        FitPicker(_cmbTransponders, keepToRow: false);
        FitPicker(_cmbOnStream, keepToRow: false);
        FitPicker(_cmbJuniorAge, keepToRow: false);

        // The crew voice drop-downs (19 Nov 2026): the column is made wide
        // enough for their longest voice name, as for the pronouns.
        var crewVoicesWidth = 0;

        foreach (var picker in new[] { _cmbCrewPrincipalVoice, _cmbCrewTechVoice }.OfType<ComboBox>())
        {
            crewVoicesWidth = Math.Max(crewVoicesWidth, FitPicker(picker, keepToRow: false) + picker.Margin.Horizontal);
        }

        // One width for the two, one under the other in the same card: a
        // voice kept from before ("not on the list now") is on only one
        // list, and made that one alone wider.
        foreach (var picker in new[] { _cmbCrewPrincipalVoice, _cmbCrewTechVoice }.OfType<ComboBox>())
        {
            var voiceWidth = crewVoicesWidth - picker.Margin.Horizontal;

            if (picker.Width != voiceWidth)
            {
                picker.Width = voiceWidth;
            }
        }

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
            crewVoicesWidth,
            _chkRental.GetPreferredSize(Size.Empty).Width,
            _chkExcludeRecords.GetPreferredSize(Size.Empty).Width,
            _chkVisitor.GetPreferredSize(Size.Empty).Width,
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

        // The cards' own room: their inset either side, and the widest of
        // their captions on one line (Section.CaptionWidth), and the top
        // card's photo beside its buttons.
        var insets = Section.Inset.Horizontal;
        var cardCaptions = cards.Append(notesCard).Max(card => card.CaptionWidth) + insets;
        var headerNeeds = insets + _photoSide + UiLayout.RowGap + header.Buttons.OneLineWidth;

        // The width it was made at, at the text size in use, or wider when
        // the columns need it - with room for the body's scrollbar, which
        // takes its width from the fields on a screen too short for them.
        var width = new[]
        {
            UiLayout.AtTextSize(DialogWidth),
            UiLayout.PageLeft + insets + captionColumn + fieldColumn
                + SystemInformation.VerticalScrollBarWidth + UiLayout.PageRight,
            UiLayout.PageLeft + Math.Max(cardCaptions, headerNeeds)
                + SystemInformation.VerticalScrollBarWidth + UiLayout.PageRight,
        }.Max();

        // The field column as it is when the body's scrollbar is showing -
        // the narrowest it gets - for a drop-down widened once the window
        // is up (FitPicker).
        _fieldColumn = width - UiLayout.PageLeft - insets - captionColumn
                       - SystemInformation.VerticalScrollBarWidth - UiLayout.PageRight;

        // Let go, children first, and laid out once: everything above is
        // worked out from what the parts want (their preferred sizes), never
        // from where a layout put them, so none of it needed the dialog laid
        // out - and each change made live, from the strip's heights to each
        // drop-down's width, laid out all of it again.
        building.Dispose();

        DialogFit.Keep(this, page, width, body: body, widthMeasured: true);
    }

    /// <summary>
    /// The card across the top (18 Nov 2026, dialog jazz-up): who this
    /// driver is at a glance. Their photo down the left - or the initials
    /// avatar, which follows the name as it is typed - and beside it their
    /// name in the heading face, a line of transponder, number, class and
    /// car that follows the boxes below, the badges they have earned when
    /// there are any, and Choose Photo and Remove Photo. The photo is given
    /// its height once the theme pass is done (SizeHeader).
    /// </summary>
    private HeaderCard BuildHeader(string badgeSummary, bool adding)
    {
        _photoSide = UiLayout.AtTextSize(PhotoSide);

        _photoPanel.Paint += (_, e) =>
        {
            var who = _txtDriverName.Text.Trim().Length > 0 ? _txtDriverName.Text.Trim() : Result.Transponder;
            DriverPhotos.DrawAvatar(e.Graphics, _photoPanel.ClientRectangle, who, _photoFileName);
        };

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

        var btnRemove = new Button { Text = "Remove Photo" };
        btnRemove.Click += (_, _) =>
        {
            _photoFileName = string.Empty;
            _photoPanel.Invalidate();
        };

        var lblName = new Label
        {
            AutoSize = true,
            Font = AppTheme.HeadingFont,
            ForeColor = AppTheme.TextDark,
            UseMnemonic = false,
        };

        var lblLine = new Label
        {
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            UseMnemonic = false,
        };

        void ShowWho()
        {
            lblName.Text = WhoHeading(_txtDriverName.Text, adding);
            lblLine.Text = WhoLine(_txtTransponder.Text, _txtRaceNumber.Text, MainClassShown(), _cmbCars.Text);
        }

        _txtDriverName.TextChanged += (_, _) => ShowWho();
        _txtTransponder.TextChanged += (_, _) => ShowWho();
        _txtRaceNumber.TextChanged += (_, _) => ShowWho();
        _cmbClass.SelectedIndexChanged += (_, _) => ShowWho();
        _cmbCars.TextChanged += (_, _) => ShowWho();
        ShowWho();

        var buttons = new ButtonBar().With(btnChoose, btnRemove);

        var band = new Beside(() => _photoSide)
        {
            SideOnLeft = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        band.AddSide(_photoPanel, gapBelow: 0);
        band.Add(lblName, gapBelow: UiLayout.HintGap);
        band.Add(lblLine, gapBelow: UiLayout.RowGap);

        if (!string.IsNullOrWhiteSpace(badgeSummary))
        {
            band.Add(new Label
            {
                Text = badgeSummary,
                AutoSize = true,
                ForeColor = AppTheme.Primary,
                Font = AppTheme.SmallFont,
                UseMnemonic = false,
            }, gapBelow: UiLayout.RowGap);
        }

        band.Add(buttons, stretch: false, gapBelow: 0);

        var card = new Section();
        card.Add(band, gapBelow: 0);

        return new HeaderCard(card, buttons);
    }

    /// <summary>The top card and its photo buttons, whose width the window's is worked out from.</summary>
    private sealed record HeaderCard(Section Card, ButtonBar Buttons);

    /// <summary>
    /// The photo square, at the text size in use, in the face the theme pass
    /// left - once: the dialog is built at the text size in use and is not
    /// kept in step with a change made while it is up (see DialogFit).
    /// </summary>
    private void SizeHeader(HeaderCard header)
    {
        _photoPanel.Height = _photoSide;
        header.Card.PerformLayout();
    }

    /// <summary>The top card's name: the name as typed, or what it is until there is one.</summary>
    internal static string WhoHeading(string? typedName, bool adding)
    {
        var name = (typedName ?? string.Empty).Trim();

        return name.Length > 0 ? name : adding ? "New driver" : "No name yet";
    }

    /// <summary>The top card's line: "Transponder 4001644 · #117 · Stock Buggy · Mini-Z", the parts there are.</summary>
    internal static string WhoLine(string? transponder, string? raceNumber, string? className, string? carName)
    {
        var parts = new List<string>();
        var tx = (transponder ?? string.Empty).Trim();

        parts.Add(tx.Length > 0 ? "Transponder " + tx : "No transponder yet");

        if (!string.IsNullOrWhiteSpace(raceNumber))
        {
            parts.Add("#" + raceNumber.Trim().TrimStart('#'));
        }

        if (!string.IsNullOrWhiteSpace(className))
        {
            parts.Add(className.Trim());
        }

        if (!string.IsNullOrWhiteSpace(carName))
        {
            parts.Add(carName.Trim());
        }

        return string.Join("  ·  ", parts);
    }

    /// <summary>
    /// Team and sponsors, the fields that exist for the crowd rather than
    /// the organiser, as rows of their own card. They used to sit down the
    /// photo strip with their captions above the boxes.
    /// </summary>
    private void BuildCrowdRows(FieldGrid crowd, List<Label> captions)
    {
        _txtTeam.Text = Result.Team;
        captions.Add(crowd.AddRow("Team / club:", _txtTeam, stretch: true));

        _txtSponsors.Multiline = true;
        _txtSponsors.ScrollBars = ScrollBars.Vertical;
        _txtSponsors.Text = Result.Sponsors;
        captions.Add(crowd.AddRow("Sponsors:", _txtSponsors, stretch: true));
    }

    /// <summary>A card filled in: a line saying what it is for, and its fields under it.</summary>
    private static Section Card(Section card, string hint, FieldGrid grid)
    {
        card.Add(CardHint(hint), gapBelow: UiLayout.RowGap);
        card.Add(grid, gapBelow: 0);

        return card;
    }

    /// <summary>A card's line saying what it is for, in the small muted face.</summary>
    private static Label CardHint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = AppTheme.TextMuted,
        Font = AppTheme.SmallFont,
    };

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

    private void ChooseBodyColour()
    {
        using var dialog = new ColorDialog { FullOpen = true, AnyColor = true };

        if (CarColour.ToColour(_bodyColour) is { } current)
        {
            dialog.Color = current;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _bodyColour = CarColour.FromColour(dialog.Color);
            ShowBodyColour();
        }
    }

    /// <summary>The swatch shows the colour, or a plain box with "None" greyed out when there is none.</summary>
    private void ShowBodyColour()
    {
        var colour = CarColour.ToColour(_bodyColour);
        _pnlBodyColour.BackColor = colour ?? AppTheme.Surface;
        _btnBodyColourNone.Enabled = colour is not null;
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

        // WHY (23 Nov 2026, bug hunt 24): a crew name or "said as" the
        // announcer can't say is refused here, as the two crew approval
        // paths refuse it. Save used to keep "Coach D2" without a word, and
        // on race night the club's own name was said instead.
        foreach (var (box, had, saidAs) in new[]
                 {
                     (_txtCrewPrincipal, Result.CrewPrincipalName, false),
                     (_txtCrewPrincipalSay, Result.CrewPrincipalSaidAs, true),
                     (_txtCrewTech, Result.CrewTechName, false),
                     (_txtCrewTechSay, Result.CrewTechSaidAs, true),
                 })
        {
            if (CrewWordProblem(box.Text, had, saidAs) is { } problem)
            {
                AppDialog.Show(this, problem, "Crew name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                box.Focus();
                return;
            }
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
            BodyColour = _bodyColour,
            HomeTrack = _txtHomeTrack.Text.Trim(),
            SkillLevel = _txtSkillLevel.Text.Trim(),

            // New to racing (12 Nov 2026, setup plan D12).
            IsNovice = _chkNovice.Checked,
            Mentor = _txtMentor.Text.Trim(),
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
            Visitor = _chkVisitor.Checked,

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

            // Papers (12 Nov 2026, setup plan D9): recorded on Who's Racing,
            // not on this dialog, so kept exactly as they were - a Driver Hub
            // edit must never wipe a signed waiver or a membership.
            WaiverSignedOn = Result.WaiverSignedOn,
            // Junior and the age band ARE on this dialog now (15 Nov 2026,
            // AddJuniorRow), so any club can set them, waivers or not.
            IsJunior = _chkJunior.Checked,
            JuniorAge = JuniorAgeChosen,
            WaiverGuardian = Result.WaiverGuardian ?? string.Empty,
            MembershipNumber = Result.MembershipNumber ?? string.Empty,
            MembershipExpires = Result.MembershipExpires,

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

            // Their crew and notes (15 Nov 2026). Unchanged boxes keep the
            // profile's own text exactly, as the about line does, so opening
            // and saving a driver is never a change. The voices are not on
            // this dialog and are kept as they were.
            CrewPrincipalName = Kept(_txtCrewPrincipal.Text, Result.CrewPrincipalName, DriverCrew.NameMaxChars),
            CrewTechName = Kept(_txtCrewTech.Text, Result.CrewTechName, DriverCrew.NameMaxChars),
            CrewPrincipalSaidAs = Kept(_txtCrewPrincipalSay.Text, Result.CrewPrincipalSaidAs, DriverCrew.SaidAsMaxChars),
            CrewTechSaidAs = Kept(_txtCrewTechSay.Text, Result.CrewTechSaidAs, DriverCrew.SaidAsMaxChars),

            // Picked here since 19 Nov 2026 when the voices are listed; kept
            // as they were otherwise, and when the pick shown is the one they had.
            CrewPrincipalVoice = CrewVoiceKept(_cmbCrewPrincipalVoice, _crewPrincipalChoices, Result.CrewPrincipalVoice),
            CrewTechVoice = CrewVoiceKept(_cmbCrewTechVoice, _crewTechChoices, Result.CrewTechVoice),
            CrewThanks = Kept(_txtCrewThanks.Text, Result.CrewThanks, DriverCrew.ThanksMaxChars),
            CrewCarStory = Kept(_txtCrewCar.Text, Result.CrewCarStory, DriverCrew.CarStoryMaxChars),
            CrewGoal = GoalKept(_txtCrewGoal.Text, Result).Goal,
            CrewGoalNight = GoalKept(_txtCrewGoal.Text, Result).Night,
            CrewGoalHeld = GoalKept(_txtCrewGoal.Text, Result).Held,
            CrewNotesParentConsent = _chkCrewConsent.Checked,

            // A junior on the stream (15 Nov 2026), as it stands in the dialog.
            JuniorOffStream = OnStreamOffChosen,
            StreamConsent = _chkStreamConsent.Checked,
        };
    }

    /// <summary>
    /// Why a crew name or "said as" box can't be saved as typed, in plain
    /// words - or null when it can. Blank is fine (the club's own is used),
    /// and so is an unchanged box, so a name saved before this check never
    /// blocks an edit to something else (bug hunt 24).
    /// </summary>
    internal static string? CrewWordProblem(string? typed, string? had, bool saidAs)
    {
        if (string.Equals(typed ?? string.Empty, had ?? string.Empty, StringComparison.Ordinal))
        {
            return null;
        }

        var text = DriverCrew.Clean(typed);

        if (text.Length == 0)
        {
            return null;
        }

        if (saidAs)
        {
            return DriverCrew.IsASaidAs(text)
                ? null
                : $"\"{text}\" can't be read out - letters, spaces, hyphens and apostrophes only. Fix it, or leave it blank.";
        }

        return DriverCrew.IsAName(text)
            ? null
            : $"\"{text}\" isn't a name the announcer can say - letters, spaces, hyphens and apostrophes only. "
              + "Fix it, or leave it blank for the club's own.";
    }

    /// <summary>A crew or notes box as it is kept: unchanged keeps the profile's own text; changed is cleaned onto one line and capped.</summary>
    internal static string Kept(string? typed, string? had, int most)
    {
        if (string.Equals(typed ?? string.Empty, had ?? string.Empty, StringComparison.Ordinal))
        {
            return had ?? string.Empty;
        }

        var text = DriverCrew.Clean(typed);
        return text.Length > most ? text[..most].TrimEnd() : text;
    }

    /// <summary>
    /// A JUNIOR ON THE STREAM (15 Nov 2026, children's privacy plan 4.2):
    /// how they appear on the stream - shortened, or not at all - and the
    /// parent's say-so, recorded at the desk beside the notes consent. Its
    /// own rows and its own method, so nothing about the notes consent
    /// above changes. See DriverProfile.JuniorOffStream and StreamConsent.
    /// </summary>
    private void AddOnStreamRows(FieldGrid fields, List<Label> captions)
    {
        _cmbOnStream.DropDownStyle = ComboBoxStyle.DropDownList;

        foreach (var (_, words) in OnStreamChoices)
        {
            _cmbOnStream.Items.Add(words);
        }

        _cmbOnStream.SelectedIndex = Math.Max(0, Array.FindIndex(OnStreamChoices, c => c.OffStream == Result.JuniorOffStream));
        captions.Add(fields.AddRow("On stream:", _cmbOnStream));

        _chkStreamConsent.Text = "A parent or guardian agreed to them appearing on the stream";
        _chkStreamConsent.AutoSize = true;
        _chkStreamConsent.Checked = Result.StreamConsent;
        fields.AddRow(string.Empty, _chkStreamConsent);
        fields.AddHint("For a junior. Online their name is always shortened (\"Mia K\"), consent or not. \"Not at all\" keeps "
                       + "their name off the stream's overlays and the club's captions, keeps lines naming them from listeners "
                       + "at home, and leaves them out of the night-before preview. The PA in the room still says their name.");
    }

    /// <summary>The "Age:" choices for a junior, in the order the drop-down lists them.</summary>
    private static readonly (JuniorAge Age, string Words)[] JuniorAgeChoices =
    {
        (JuniorAge.NotSet, "Age not set"),
        (JuniorAge.Under13, "Under 13"),
        (JuniorAge.ThirteenToSeventeen, "13-17"),
    };

    /// <summary>
    /// JUNIOR AND AGE BAND ON THE PROFILE (15 Nov 2026, children's privacy
    /// review). Until now the Junior tick lived only in the Papers window,
    /// which a club sees only when it checks waivers or memberships - so a
    /// club that checks neither could not mark a junior at all, and every
    /// junior protection (shortened names, desk-only notes, no following by
    /// name, fixed desk replies, clean Sailor-mouth) passed them by. The same
    /// two fields, here too, so any club can set them; the Papers window
    /// still edits them, and both write the same profile.
    /// </summary>
    private void AddJuniorRow(FieldGrid fields, List<Label> captions)
    {
        _chkJunior.Text = "Under 18";
        _chkJunior.AutoSize = true;
        _chkJunior.Checked = Result.IsJunior;

        _cmbJuniorAge.DropDownStyle = ComboBoxStyle.DropDownList;

        foreach (var (_, words) in JuniorAgeChoices)
        {
            _cmbJuniorAge.Items.Add(words);
        }

        _cmbJuniorAge.SelectedIndex = Math.Max(0, Array.FindIndex(JuniorAgeChoices, c => c.Age == Result.JuniorAge));
        _cmbJuniorAge.Enabled = _chkJunior.Checked;
        _chkJunior.CheckedChanged += (_, _) => _cmbJuniorAge.Enabled = _chkJunior.Checked;

        captions.Add(fields.AddRow("Junior:", FieldGrid.Inline(_chkJunior, _cmbJuniorAge)));
        fields.AddHint("Juniors' names are shortened online and their own words are taken only at the desk. Under 13s, and juniors with no age set, get "
                       + "the strictest rules. Set the age band so 13-17s can send crew names from their phone.");
    }

    /// <summary>The age band as saved: NotSet unless Junior is ticked.</summary>
    private JuniorAge JuniorAgeChosen =>
        !_chkJunior.Checked || _cmbJuniorAge.SelectedIndex < 0 || _cmbJuniorAge.SelectedIndex >= JuniorAgeChoices.Length
            ? JuniorAge.NotSet
            : JuniorAgeChoices[_cmbJuniorAge.SelectedIndex].Age;

    /// <summary>The "On stream:" choice as saved: true for "Not at all".</summary>
    private bool OnStreamOffChosen =>
        _cmbOnStream.SelectedIndex >= 0 && _cmbOnStream.SelectedIndex < OnStreamChoices.Length && OnStreamChoices[_cmbOnStream.SelectedIndex].OffStream;

    /// <summary>
    /// Tonight's goal as it is kept. The box shows tonight's goal only, so
    /// an unchanged box keeps whatever the profile had (a goal for another
    /// night included); a changed one is for tonight, and a cleared one
    /// clears it.
    /// </summary>
    internal static (string Goal, DateTime? Night, bool Held) GoalKept(string? typed, DriverProfile had)
    {
        if (string.Equals(typed ?? string.Empty, DriverCrew.GoalTonight(had, DateTime.Now), StringComparison.Ordinal))
        {
            return (had.CrewGoal ?? string.Empty, had.CrewGoalNight, had.CrewGoalHeld);
        }

        var goal = Kept(typed, null, DriverCrew.GoalMaxChars);
        return goal.Length == 0 ? (string.Empty, null, false) : (goal, TonightAtTheTrack.NightOf(DateTime.Now), false);
    }

    /// <summary>
    /// One line of a crew voice drop-down (19 Nov 2026): the club's own
    /// (Pick null, Kept false), a voice on the list, or the pick they have
    /// now when it is not on the list (Kept true) - another engine's, or one
    /// gone - so opening and saving never takes it away.
    /// </summary>
    internal sealed record CrewVoiceChoice(string Words, CrewVoicePick? Pick, bool Kept)
    {
        public override string ToString() => Words;
    }

    /// <summary>The drop-down's lines, and which of them is their pick now.</summary>
    internal static (IReadOnlyList<CrewVoiceChoice> Choices, int Selected) CrewVoiceChoices(
        IReadOnlyList<CrewVoiceOption> offered, CrewVoicePick? current)
    {
        var choices = new List<CrewVoiceChoice> { new("The club's own voice", null, false) };
        var selected = 0;

        foreach (var voice in offered.Where(v => DriverCrew.IsAPick(v.Pick)))
        {
            var name = DriverCrew.Clean(string.IsNullOrWhiteSpace(voice.Pick.Name) ? voice.Pick.Id : voice.Pick.Name);

            if (voice.Pick.SameVoiceAs(current))
            {
                selected = choices.Count;
            }

            choices.Add(new CrewVoiceChoice(name, voice.Pick.Copy(), false));
        }

        if (selected == 0 && DriverCrew.IsAPick(current))
        {
            selected = choices.Count;
            choices.Add(new CrewVoiceChoice($"{DriverCrew.Clean(current!.Name)} (not on the list now)", current.Copy(), true));
        }

        return (choices, selected);
    }

    /// <summary>A crew voice drop-down, when there are voices to pick from; null otherwise.</summary>
    private static ComboBox? AddCrewVoiceRow(
        FieldGrid grid, List<Label> captions, IReadOnlyList<CrewVoiceOption>? offered, CrewVoicePick? current, out IReadOnlyList<CrewVoiceChoice> choices)
    {
        choices = Array.Empty<CrewVoiceChoice>();

        if (offered is null)
        {
            return null;
        }

        var (list, selected) = CrewVoiceChoices(offered, current);
        choices = list;

        var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };

        foreach (var choice in list)
        {
            picker.Items.Add(choice);
        }

        picker.SelectedIndex = selected;
        captions.Add(grid.AddRow("Voice:", picker));
        return picker;
    }

    /// <summary>The voice saved for a role: the one picked, or - with no drop-down, or the old pick still shown - the one they had.</summary>
    internal static CrewVoicePick? CrewVoiceKept(ComboBox? picker, IReadOnlyList<CrewVoiceChoice> choices, CrewVoicePick? had)
    {
        if (picker is null || picker.SelectedIndex < 0 || picker.SelectedIndex >= choices.Count)
        {
            return had?.Copy();
        }

        var choice = choices[picker.SelectedIndex];

        if (choice.Kept || choice.Pick?.SameVoiceAs(had) == true)
        {
            return had?.Copy();
        }

        return choice.Pick?.Copy();
    }

    /// <summary>Which voices their crew speaks in, for the hint under the names.</summary>
    private static string CrewVoicesHint(DriverProfile profile)
    {
        var principal = DriverCrew.IsAPick(profile.CrewPrincipalVoice) ? profile.CrewPrincipalVoice!.Name : string.Empty;
        var tech = DriverCrew.IsAPick(profile.CrewTechVoice) ? profile.CrewTechVoice!.Name : string.Empty;

        if (principal.Length == 0 && tech.Length == 0)
        {
            return "They speak in the club's voices on Voice > Cast; a driver can pick their own from their phone.";
        }

        return "Voices picked on their phone: "
               + (principal.Length > 0 ? $"principal \"{principal}\"" : "principal the club's")
               + ", " + (tech.Length > 0 ? $"tech lead \"{tech}\"" : "tech lead the club's") + ".";
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
