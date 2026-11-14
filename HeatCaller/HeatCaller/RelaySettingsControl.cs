namespace HeatCaller;

/// <summary>
/// The Club Website page: where a club points this laptop at its relay, decides
/// what may be published, and sees whether it is actually getting through.
///
/// A page of its own rather than a section on Local Screens, for the same
/// reason Classes became one: this is the thing somebody goes looking for,
/// and it should not be four steps inside a page about something else. The
/// Local Screens page carries a one-line status and a button through to
/// here, so there are two doors rather than one.
/// </summary>
public sealed class RelaySettingsControl : UserControl
{
    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>
    /// The grids whose captions share one column down the page - the
    /// relay's, the welcome line's, the links', the personal PINs' and the
    /// mail's - so their boxes line up from card to card. Filled as the
    /// cards are built; lined up in BuildLayout.
    /// </summary>
    private readonly List<FieldGrid> _linedUp = new();

    /// <summary>The drop-downs' widths as last measured - see Pieces.PickerWidth. Their choices are put in once, as the page is built.</summary>
    private (Font Font, int Count, int Width)? _rotationWidth;
    private (Font Font, int Count, int Width)? _presetWidth;

    /// <summary>
    /// The fields' widths at the normal text size - the widths they have
    /// always had - put at the text size in use by SizeFields.
    /// </summary>
    private const int AddressWidth = 460;
    private const int ClubWidth = 260;
    private const int StaffPinWidth = 120;
    private const int DropDownWidth = 200;
    private const int MailWidth = 420;
    private const int ShortMailWidth = 280;

    /// <summary>The digits in the highest port there is, 65535 - what the port box is as wide as.</summary>
    private const int PortDigits = 5;

    /// <summary>The lines of its face each status line keeps whatever it says - see KeepLines.</summary>
    private const int StatusLines = 2;

    private readonly RelayPublisher _publisher;
    private readonly AppSettings _settings;
    private readonly ProfileStore _profiles;

    /// <summary>The app's undo, for New PINs and Reissue All Badges. Null when the host didn't pass one.</summary>
    private readonly UndoService? _undo;

    /// <summary>
    /// True while the controls are being filled in from settings.
    ///
    /// Setting a CheckBox's Checked property raises CheckedChanged, and
    /// this page saves on that event - so loading used to write the boxes
    /// back to settings BEFORE they had been filled in, storing empty
    /// strings over a perfectly good address, club and token.
    ///
    /// It only bit once publishing had been switched on, because
    /// CheckedChanged fires on a CHANGE: while the setting was false,
    /// loading false changed nothing and raised nothing. The first launch
    /// after enabling it wiped the lot, silently, and the publisher then
    /// dropped everything for want of a configuration.
    /// </summary>
    private bool _loading;

    private readonly CheckBox _chkEnabled = new ThemedCheckBox();
    private readonly TextBox _txtBaseUrl = new();
    private readonly TextBox _txtClub = new();
    private readonly TextBox _txtToken = new();
    private readonly CheckBox _chkShowToken = new ThemedCheckBox();
    private readonly CheckBox _chkRoster = new ThemedCheckBox();
    private readonly CheckBox _chkDriverNights = new ThemedCheckBox();
    private readonly CheckBox _chkAbbreviate = new ThemedCheckBox();
    private readonly CheckBox _chkFullNamesAloud = new ThemedCheckBox();

    // --- Driver experience (22 Oct 2026) ---------------------------------
    //
    // WHY (22 Oct 2026, driver experience): the drivers' own requests from
    // their phones, the announcer's words as captions on the live page, and
    // each driver's night by email. All off until the club ticks them, like
    // everything else on this page that puts something new online or in
    // somebody's inbox.
    private readonly CheckBox _chkDriverRequests = new ThemedCheckBox();
    private readonly CheckBox _chkResultQueries = new ThemedCheckBox();
    private readonly NumericUpDown _numProtestMinutes = new();
    private FieldGrid _protestGrid = null!;
    private Label _lblProtestHint = null!;
    private readonly CheckBox _chkRelayCaptions = new ThemedCheckBox();
    private readonly CheckBox _chkNightEmail = new ThemedCheckBox();
    private readonly CheckBox _chkNightEmailAuto = new ThemedCheckBox();
    private readonly Label _lblNightEmailState = new();
    private readonly Button _btnEmailNights = new();

    /// <summary>What the Email Tonight's Nights button's send is up to, for its status line. Null when it is not sending.</summary>
    private string? _nightProgress;

    /// <summary>
    /// How this page reaches the night emails MainForm gathers and sends -
    /// set by MainForm.WireNightEmail before the page is built, the way
    /// QrBadgeRenderer.OnlineBadgeUrl is. Null (no MainForm behind the page,
    /// as in the layout tests) and Email Tonight's Nights stays greyed out.
    /// (22 Oct 2026, driver experience.)
    /// </summary>
    internal static NightEmailDesk? NightEmails { get; set; }
    private readonly Button _btnTest = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnRestore = new();
    private readonly Label _lblLastGood = new();
    private readonly Button _btnDelete = new();
    private readonly Label _lblTestResult = new();
    private readonly Label _lblStatus = new();
    private readonly Label _lblDetail = new();

    /// <summary>Whether the Status card is naming a restored backup's relay address, as it does in Simple mode where the address row is hidden.</summary>
    private bool _restoredAddressNamed;
    private readonly TextBox _txtTagline = new();
    private readonly TextBox _txtClubAlias = new();
    private readonly TextBox _txtShortName = new();
    private readonly TextBox _txtNewcomerWhere = new();
    private readonly TextBox _txtNewcomerWhen = new();
    private readonly TextBox _txtNewcomerJoin = new();

    /// <summary>The first-night guide, as the club words it (4 Nov 2026) - see BuildGuideCard.</summary>
    private readonly TextBox _txtGuide = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, AcceptsReturn = true };
    private readonly Button _btnSendGuide = new();
    private readonly Button _btnStandardGuide = new();
    private readonly Label _lblGuideState = new();

    /// <summary>Club updates (4 Nov 2026) - see BuildUpdatesCard.</summary>
    private readonly TextBox _txtUpdates = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, AcceptsReturn = true };
    private readonly Button _btnSendUpdates = new();
    private readonly Button _btnClearUpdates = new();
    private readonly Label _lblUpdatesState = new();

    /// <summary>One box per RelaySocial platform, keyed by its key.</summary>
    private readonly Dictionary<string, TextBox> _socialBoxes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Says what is wrong with a link that was just typed, or nothing.</summary>
    private readonly Label _lblSocialProblem = new();
    private readonly Button _btnOpenLooks = new();
    private readonly CheckBox _chkCheckIn = new ThemedCheckBox();
    private readonly TextBox _txtCheckInPin = new();
    private readonly Label _lblCheckInState = new();
    private readonly Button _btnOpenCheckIn = new();
    private readonly Button _btnReissueBadges = new();

    /// <summary>
    /// Whether Reissue All Badges is on show. Kept here rather than read
    /// back from the button's Visible, which answers false whenever this
    /// page is not the one on screen. See RefreshCheckInState.
    /// </summary>
    private bool _reissueOffered = true;

    private readonly CheckBox _chkCheckInPhotos = new ThemedCheckBox();
    private readonly CheckBox _chkEmailSignInLink = new ThemedCheckBox();
    private readonly CheckBox _chkRacerPins = new ThemedCheckBox();
    private readonly ComboBox _cmbPinRotation = new();
    private readonly Label _lblPinsState = new();
    private readonly Button _btnShowPins = new();
    private readonly Button _btnNewPins = new();
    private readonly Button _btnEmailPins = new();
    private readonly CheckBox _chkEmailPinsAuto = new ThemedCheckBox();
    private readonly ComboBox _cmbMailPreset = new();
    private readonly TextBox _txtSmtpHost = new();
    private readonly NumericUpDown _numSmtpPort = new();
    private readonly TextBox _txtSmtpUser = new();
    private readonly TextBox _txtSmtpPassword = new();
    private readonly CheckBox _chkShowSmtpPassword = new ThemedCheckBox();
    private readonly TextBox _txtSmtpFromName = new();
    private readonly Button _btnTestMail = new();
    private readonly Label _lblMailResult = new();
    private readonly CheckBox _chkManager = new ThemedCheckBox();
    private readonly Label _lblManagerState = new();
    private readonly Button _btnManagerPin = new();
    private readonly Button _btnOpenManage = new();

    /// <summary>Stops an email send in progress when the page goes away.</summary>
    private readonly CancellationTokenSource _mailCancel = new();

    /// <summary>What the Email PINs button's send is up to, for the status line. Null when it is not sending.</summary>
    private string? _pinProgress;

    /// <summary>
    /// Why the staff PIN just typed was not saved, shown in the check-in
    /// line until it is fixed. Held here rather than written to the label
    /// once, because that line is redrawn every second and would wipe it.
    /// </summary>
    private string? _pinRejected;
    private readonly LinkLabel _lnkClubPage = new();
    private readonly Button _btnOpenPage = new();
    private readonly Button _btnCopyPage = new();
    private readonly Button _btnPoster = new();
    private readonly System.Windows.Forms.Timer _tick = new();

    /// <summary>
    /// Raised when the personal PINs have been changed here - a new set, or
    /// switched on or off - so the check-in list goes to the relay at once
    /// rather than on the next pass.
    /// </summary>
    public event Action? RacerPinsChanged;

    /// <summary>
    /// Raised by "Open Themes": the club pages' colours and logo are set on
    /// Themes → Looks, shared with the app and the other screens, and the
    /// host (MainForm) takes the user there.
    /// </summary>
    public event Action? OpenLooksRequested;

    /// <summary>
    /// Raised by the manager card's "Set Manager PIN...": the PIN itself
    /// lives on Local Screens, shared by the Wi-Fi and relay pages alike
    /// (see AppSettings.ManagerPin), so this page only ever switches the
    /// relay path on or off and sends the host there rather than keeping a
    /// second box for the same PIN.
    /// </summary>
    public event Action? OpenPhoneWebRequested;

    /// <summary>
    /// Raised when Reissue All Badges has made and saved a new badge key.
    /// Every driver's online id has changed with it, so the host (MainForm)
    /// works them all out again and sends the check-in list - which carries
    /// them - straight away, rather than on its next pass.
    /// </summary>
    public event Action? BadgesReissued;

    /// <summary>Console output from this page: what was done here that the log should show.</summary>
    public event Action<string>? StatusMessage;

    /// <summary>
    /// The relay's address, club or token was changed and saved while the
    /// HeatCaller voice speaks through this same relay (the voice relay
    /// boxes on Voice Settings are blank). A rest the voice was taking over
    /// the old relay - a spent allowance, a token it turned down - says
    /// nothing about the new one, so the announcer is told to ask again.
    /// </summary>
    public event Action? VoiceRelayChanged;

    /// <summary>
    /// Asked by Email PINs... before it sends: true while the host (MainForm)
    /// is still making the new PINs' hashes and has not yet sent the
    /// check-in list that carries them. Until it has, the relay holds the
    /// old PINs, so a racer emailed now could tap their new PIN at once and
    /// be refused. Null (nothing wired) means never.
    /// </summary>
    public Func<bool>? CheckInListBeingPrepared { get; set; }

    /// <summary>
    /// Raised on the UI thread just before Email PINs... starts sending, and
    /// PinEmailFinished once what it sent has been recorded (RacerPinsEmailed)
    /// - or the page has gone. Between the two the host does not email any
    /// PIN of its own: in the moment after the sending ends and before this
    /// page has recorded it, a racer it emailed still looks unemailed.
    /// </summary>
    public event Action? PinEmailStarted;

    /// <summary>See PinEmailStarted. Raised exactly once for each PinEmailStarted.</summary>
    public event Action? PinEmailFinished;

    /// <summary>
    /// The Room & Web card: the club's notes, the room's vote and listening
    /// along, moved here from the Commentary page on 17 Oct 2026 - see
    /// RoomAndWebCard. Its three "closed on the relay" events are joined up
    /// by the host.
    /// </summary>
    private readonly RoomAndWebCard _roomAndWeb;

    /// <summary>The Room & Web card, for the host to wire its events.</summary>
    internal RoomAndWebCard RoomAndWeb => _roomAndWeb;

    public RelaySettingsControl(RelayPublisher publisher, AppSettings settings, ProfileStore profiles, UndoService? undo = null)
    {
        _publisher = publisher;
        _settings = settings;
        _profiles = profiles;
        _undo = undo;

        // Before the page is built, which puts its card on the page.
        _roomAndWeb = new RoomAndWebCard(settings, _textSize);

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize.
        //
        // No MinimumSize, and no scrolling of its own: nine cards are taller
        // than a laptop screen, and a page whose last buttons are below the
        // window with no way down to them is a page with fewer buttons - so
        // the cards are in a PageStack, which scrolls, down only.
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        LoadFromSettings();
        RefreshStatus();
        RefreshLastGood();

        _tick.Interval = 1000;
        _tick.Tick += (_, _) => RefreshStatus();
        _tick.Start();
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

    /// <summary>Called when the page is navigated to, so the boxes match what settings actually hold.</summary>
    public void RefreshAll()
    {
        LoadFromSettings();
        _roomAndWeb.RefreshAll();
        RefreshStatus();
        RefreshLastGood();
    }

    /// <summary>
    /// Builds the page on the layout pieces (LayoutPieces.cs): the intro,
    /// then ten cards down the page, each as wide as the page and as tall
    /// as what is in it -
    ///
    ///   Relay                   publishing on or off; the address, the club
    ///                           id and the token; Save, Test Connection and
    ///                           Restore Last Working; what the last test said
    ///                           and what last worked.
    ///   What gets published     what always goes, and the roster and
    ///                           short-name switches with what each means;
    ///                           since 22 Oct 2026 also the drivers' own
    ///                           requests (a step in under their nights),
    ///                           their night by email with its button, and
    ///                           the live page's captions.
    ///   Look                    the welcome line, the club's links, and the
    ///                           way to Themes → Looks.
    ///   Online check-in         the switch, the staff PIN, photos, how it is
    ///                           doing, and its buttons.
    ///   Personal PINs           the switch, how often they change, how they
    ///                           are doing, their buttons, and emailing them.
    ///   Email (for racer PINs)  the club's mailbox and its test.
    ///   Race-day manager        the switch, how it is doing, its buttons.
    ///   Share                   the club page's address and what to do with it.
    ///   Room & Web              club notes, the room's vote and listening
    ///                           along (RoomAndWebCard, 17 Oct 2026).
    ///   Status                  whether anything is getting through, and
    ///                           Delete Everything Published.
    ///
    /// The relay's, the look's, the staff PIN's, the personal PINs' and the
    /// mail's captions share one column (FieldGrid.LineUp), so their boxes
    /// line up down the page (the staff PIN's too since 22 Oct 2026, when
    /// its caption was shortened to fit). Nothing is placed
    /// by hand, and the page scrolls, down only, when it is taller than the
    /// window (PageStack).
    ///
    /// It used to be placed in pixels: every group box 780px wide whatever
    /// the window, each one's height added up by hand from the rows in it,
    /// the boxes the same width at every text size, the rows under each
    /// switch indented by hand, each status line a fixed box two lines of
    /// the small face tall - cut short with "...", or on check-in not even
    /// that - and an 820x640 floor the page kept however small its window.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Publishes results, standings and the live race to a relay, so they can be reached off the "
                 + "venue's Wi-Fi and stay up when this laptop is closed. The laptop only ever sends, and nothing "
                 + "on race day waits for the network.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        var page = new PageStack();

        // Simple mode (1 Nov 2026) keeps the Relay card (club and token),
        // Online check-in, Share and Status; what gets published, the look,
        // personal PINs, email, the race-day manager and Room & Web are
        // Advanced. See SimpleMode.
        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.Add(BuildRelayCard());
        page.Add(SimpleMode.Hide(BuildPublishCard()));
        page.Add(SimpleMode.Hide(BuildLookCard()));
        page.Add(BuildGuideCard());
        page.Add(BuildUpdatesCard());
        page.Add(BuildCheckInCard());
        page.Add(SimpleMode.Hide(BuildPinsCard()));
        page.Add(SimpleMode.Hide(BuildMailCard()));
        page.Add(SimpleMode.Hide(BuildManagerCard()));
        page.Add(BuildShareCard());

        // ROOM & WEB (17 Oct 2026): what the room and the people at home can
        // send in or hear through the club's pages - from the Commentary
        // page. After Share, which is the address they use to get there.
        page.Add(SimpleMode.Hide(_roomAndWeb.Card));
        page.Add(BuildStatusCard(), gapBelow: 0);
        page.Add(SimpleMode.MoreLine(), stretch: false, gapBelow: 0);

        Controls.Add(page);

        // Once the grids are on the page and wear its face - what their
        // captions and fields are measured in.
        _textSize.Keep(() => FieldGrid.LineUp(_linedUp));
        _textSize.Keep(SizeFields);
    }

    /// <summary>
    /// The fields' sizes at the text size in use. The boxes are the widths
    /// they always had at the normal text size, at this size - capped, not
    /// stretched: an address, a token or a link is a long line, but a box
    /// across the whole page for one reads as a mistake. The drop-downs are
    /// that wide or as wide as their longest choice, the port as wide as
    /// five digits and its arrows need, and the club page's link one line
    /// of its face tall.
    ///
    /// Run again when the text size changes (TextSize.Keep).
    /// </summary>
    private void SizeFields()
    {
        var address = UiLayout.AtTextSize(AddressWidth);

        Fit(_txtBaseUrl, address);
        Fit(_txtClub, UiLayout.AtTextSize(ClubWidth));
        Fit(_txtToken, address);
        Fit(_txtTagline, address);
        Fit(_txtClubAlias, UiLayout.AtTextSize(ClubWidth));
        Fit(_txtShortName, UiLayout.AtTextSize(ClubWidth));
        Fit(_txtNewcomerWhere, address);
        Fit(_txtNewcomerWhen, address);
        Fit(_txtNewcomerJoin, address);

        // Room for a few parts of the guide at a time; it scrolls past that.
        var guide = Pieces.LineHeight(_txtGuide.Font) * 14 + UiLayout.HintGap * 2;

        if (_txtGuide.Height != guide)
        {
            _txtGuide.Height = guide;
        }

        var updates = Pieces.LineHeight(_txtUpdates.Font) * 8 + UiLayout.HintGap * 2;

        if (_txtUpdates.Height != updates)
        {
            _txtUpdates.Height = updates;
        }

        foreach (var box in _socialBoxes.Values)
        {
            Fit(box, address);
        }

        Fit(_txtCheckInPin, UiLayout.AtTextSize(StaffPinWidth));
        Fit(_cmbPinRotation, Math.Max(UiLayout.AtTextSize(DropDownWidth), Pieces.PickerWidth(_cmbPinRotation, ref _rotationWidth)));
        Fit(_cmbMailPreset, Math.Max(UiLayout.AtTextSize(DropDownWidth), Pieces.PickerWidth(_cmbMailPreset, ref _presetWidth)));

        var mail = UiLayout.AtTextSize(MailWidth);
        var shortMail = UiLayout.AtTextSize(ShortMailWidth);

        Fit(_txtSmtpHost, shortMail);
        Fit(_numProtestMinutes, UiLayout.AtTextSize(UiLayout.NumericWidth));
        Fit(_numSmtpPort, Math.Max(
            UiLayout.AtTextSize(UiLayout.NumericWidth),
            TextRenderer.MeasureText(new string('0', PortDigits), _numSmtpPort.Font).Width + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra));
        Fit(_txtSmtpUser, mail);
        Fit(_txtSmtpPassword, shortMail);
        Fit(_txtSmtpFromName, mail);

        var link = _lnkClubPage.Font.Height + 4;

        if (_lnkClubPage.Height != link)
        {
            _lnkClubPage.Height = link;
        }

        // Only when it changed: a new width lays the grid out again.
        static void Fit(Control field, int width)
        {
            if (field.Width != width)
            {
                field.Width = width;
            }
        }
    }

    /// <summary>
    /// A status line in the small face that keeps two lines of it whatever
    /// it says, so what is under it does not hop as it is rewritten - and
    /// that grows past them for a longer message rather than cutting it
    /// short. These used to be fixed boxes two lines tall: a longer message
    /// ended in "...", and on check-in's, which had no "...", its third line
    /// was simply not drawn.
    /// </summary>
    private void KeepLines(Label label)
    {
        label.AutoSize = true;
        _textSize.Wear(label, () => AppTheme.SmallFont);
        _textSize.ReserveLines(label, StatusLines);
    }

    /// <summary>A line of help in the small muted type the app's hints use, wrapping to the room it has, in that face at every text size.</summary>
    private Label HintLabel(string text) =>
        _textSize.Wear(
            new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = AppTheme.TextMuted,
            },
            () => AppTheme.SmallFont);

    /// <summary>
    /// Publishing on or off; the relay's address, the club id and the
    /// publish token, with Show the token under it; Save, Test Connection
    /// and Restore Last Working; and under them what the last test said
    /// and what last worked.
    /// </summary>
    private Section BuildRelayCard()
    {
        _chkEnabled.Text = "Publish to the relay";
        _chkEnabled.AutoSize = true;
        _textSize.Wear(_chkEnabled, () => AppTheme.BoldFont);
        _chkEnabled.CheckedChanged += (_, _) =>
        {
            // Saved together with the fields: turning this on while the
            // token sat unsaved in its box would switch on a publisher
            // with nothing to publish with. The tick alone does not take a
            // restored backup's address as checked, though - with publishing
            // off the page had not yet said it was held (Bug hunt 17).
            SaveFields(confirmRestored: false);
        };

        _txtBaseUrl.PlaceholderText = "http://localhost:8787";

        _txtClub.PlaceholderText = "your-club";

        _txtToken.UseSystemPasswordChar = true;
        _txtToken.PlaceholderText = "hg_...";

        _chkShowToken.Text = "Show the token";
        _chkShowToken.AutoSize = true;
        _textSize.Wear(_chkShowToken, () => AppTheme.SmallFont);
        _chkShowToken.CheckedChanged += (_, _) => _txtToken.UseSystemPasswordChar = !_chkShowToken.Checked;

        // One caption column, so the three boxes line up instead of each
        // starting wherever its own caption ended - and the same column as
        // the other cards' boxes further down (BuildLayout).
        var fields = new FieldGrid();

        fields.AddRow("Address:", _txtBaseUrl);
        fields.AddRow("Club id:", _txtClub);
        fields.AddRow("Publish token:", _txtToken);
        fields.AddField(_chkShowToken);

        _linedUp.Add(fields);

        _btnTest.Text = "Test Connection";
        _btnTest.Click += async (_, _) => await TestAsync();

        _btnSave.Text = "Save";
        _btnSave.Click += (_, _) =>
        {
            if (SaveFields())
            {
                ShowTestResult(new RelayProbe(true, "Saved."));
            }
        };

        _btnRestore.Text = "Restore Last Working";
        _btnRestore.Click += (_, _) => RestoreLastGood();

        // Live, so the Restore button knows whether what is in the boxes
        // has drifted from what last worked.
        _txtBaseUrl.TextChanged += (_, _) => RefreshLastGood();
        _txtClub.TextChanged += (_, _) => RefreshLastGood();
        _txtToken.TextChanged += (_, _) => RefreshLastGood();

        // Both of these say different things at different lengths, one
        // under the other - so each keeps two lines whatever it says
        // (KeepLines), and the card does not change height as they do.
        _lblTestResult.ForeColor = AppTheme.TextMuted;
        _lblTestResult.Text = "Test Connection checks the address, club and token without publishing anything.";
        KeepLines(_lblTestResult);

        _lblLastGood.ForeColor = AppTheme.TextMuted;
        KeepLines(_lblLastGood);

        var card = new Section("Relay");

        // The relay's address is Advanced (1 Nov 2026, Simple mode); the club
        // and token are not.
        SimpleMode.HideRow(fields, _txtBaseUrl);

        card.Add(_chkEnabled, stretch: false);
        card.Add(fields);
        card.Add(new ButtonBar().With(_btnSave, _btnTest, _btnRestore), gapBelow: UiLayout.HintGap);
        card.Add(_lblTestResult, gapBelow: UiLayout.HintGap);
        card.Add(_lblLastGood, gapBelow: 0);

        return card;
    }

    /// <summary>What always goes to the relay, then the roster and short-name switches, each with what it means under it.</summary>
    private Section BuildPublishCard()
    {
        var lblAlways = new Label
        {
            Text = "The live race while one is running, each session's results as it is saved, championship "
                 + "standings, today's schedule from the Timetable, the achievements drivers have earned, and "
                 + "every track layout with its lap record and best laps.",
            AutoSize = true,
        };

        _chkRoster.Text = "Also publish the roster and heat line-ups (who is entered, and who is in which heat)";
        _chkRoster.AutoSize = true;
        _chkRoster.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.RelayPublishRoster = _chkRoster.Checked;
            _settings.Save();
            _publisher.SettingsChanged();
        };

        var lblRosterHint = HintLabel(
            "Off by default: it puts driver names, some likely children's, on an address anyone can open. The "
            + "relay also withholds the roster unless the club is set up to allow it. With this off, the schedule "
            + "shows the heats but not who is in them.");

        _chkDriverNights.Text = "Give each driver their own night at a private address";
        _chkDriverNights.AutoSize = true;
        _chkDriverNights.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.PublishDriverNightsToTheRelay = _chkDriverNights.Checked;
            _settings.Save();

            // UNTICKING IS SENT, not left to be inferred from nothing being
            // published. The relay cannot tell a club that has withdrawn
            // this from a club that has not raced this week, so it is told -
            // and being told is what makes it throw away every night it
            // holds. See RelayPublisher.CloseDriverNightsOnTheRelay.
            if (!_chkDriverNights.Checked)
            {
                _publisher.CloseDriverNightsOnTheRelay();

                // Drivers' requests are part of their night, so they close
                // with it - and the relay throws away any it still holds
                // (RelayPublisher.Requests.AsksWanted; 22 Oct 2026, driver
                // experience). Ticked again, they reopen by themselves.
                _publisher.CloseDriverAsksOnTheRelay();
            }

            _publisher.SettingsChanged();
        };

        var lblNightsHint = HintLabel(
            "Off by default. Each driver who raced gets a page of their evening - races, finishes, best laps "
            + "and moments - at an address built from the code on their check-in badge, which nobody without "
            + "it can find or open. Needs online check-in set up. Unticking takes every night off the relay, "
            + "with any driver requests still waiting there.");

        var (lblRequestsHint, lblQueriesHint, lblNightEmailHint, lblNightEmailAutoHint, lblCaptionsHint) = BuildDriverExtras();

        _chkAbbreviate.Text = "Shorten published names to a first name and an initial";
        _chkAbbreviate.AutoSize = true;
        _chkAbbreviate.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.RelayAbbreviateNames = _chkAbbreviate.Checked;
            _settings.Save();
            _publisher.SettingsChanged();
            _chkFullNamesAloud.Enabled = _chkAbbreviate.Checked;
        };

        var lblNameHint = HintLabel(
            "\"Sam K\" rather than the full name, everywhere the relay shows one. A single-word name is left "
            + "alone. Affects what is sent from now on, not what is already on the relay. While it is ticked the "
            + "HeatCaller and ElevenLabs voices say the short name too, unless the box below is ticked.");

        // WHY (24 Oct 2026): the announcer had started calling drivers "Sam K"
        // with the online voices, because the box above reached them too (22
        // Oct). A club that wants full names in the room and short ones online
        // ticks this. See AnnouncementService.SayFullNamesWithCloudVoices.
        _chkFullNamesAloud.Text = "Say full names aloud with the online voices";
        _chkFullNamesAloud.AutoSize = true;
        _chkFullNamesAloud.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.SayFullNamesWithOnlineVoices = _chkFullNamesAloud.Checked;
            _settings.Save();
        };

        var lblFullNamesHint = HintLabel(
            "The announcer says each driver's full name with the HeatCaller voice and ElevenLabs, as the Kokoro "
            + "voice always does. Those voices render on the internet, so the full names are sent there. The "
            + "club's pages and captions still show the short name, and a line that says a full name is not "
            + "sent to people listening along at home.");

        var (btnBoardColumns, lblBoardColumns) = BuildBoardColumns();

        var card = new Section("What gets published");

        card.Add(lblAlways);
        card.Add(new ButtonBar().With(btnBoardColumns), gapBelow: UiLayout.HintGap);
        card.Add(lblBoardColumns);
        card.Add(_chkRoster, stretch: false, gapBelow: UiLayout.HintGap);
        card.Add(lblRosterHint);
        card.Add(_chkDriverNights, stretch: false, gapBelow: UiLayout.HintGap);
        card.Add(lblNightsHint);

        // A step in, under the nights tick they need (22 Oct 2026, driver
        // experience) - see StepIn.
        StepIn(card.Add(_chkDriverRequests, stretch: false, gapBelow: UiLayout.HintGap));
        StepIn(card.Add(lblRequestsHint));
        StepIn(card.Add(_chkResultQueries, stretch: false, gapBelow: UiLayout.HintGap));
        StepIn(card.Add(lblQueriesHint));
        StepIn(card.Add(_protestGrid, gapBelow: UiLayout.HintGap));
        StepIn(card.Add(_lblProtestHint));

        card.Add(_chkNightEmail, stretch: false, gapBelow: UiLayout.HintGap);
        card.Add(lblNightEmailHint);
        StepIn(card.Add(_chkNightEmailAuto, stretch: false, gapBelow: UiLayout.HintGap));
        StepIn(card.Add(lblNightEmailAutoHint));
        card.Add(_lblNightEmailState, gapBelow: UiLayout.HintGap);
        card.Add(new ButtonBar().With(_btnEmailNights));

        card.Add(_chkRelayCaptions, stretch: false, gapBelow: UiLayout.HintGap);
        card.Add(lblCaptionsHint);

        card.Add(_chkAbbreviate, stretch: false, gapBelow: UiLayout.HintGap);
        card.Add(lblNameHint);
        StepIn(card.Add(_chkFullNamesAloud, stretch: false, gapBelow: UiLayout.HintGap));
        StepIn(card.Add(lblFullNamesHint, gapBelow: 0));

        return card;
    }

    /// <summary>
    /// The live board's columns on the club's website, and the line under
    /// the button saying which they are (5 Nov 2026 - see RelayColumns).
    ///
    /// A pick that comes out the same as the Live Results page's columns is
    /// saved as no pick at all, so the board goes on following that page -
    /// which is also what the picker's Restore Defaults gives.
    /// </summary>
    private (Button Button, Label Hint) BuildBoardColumns()
    {
        var btn = new Button { Text = "Choose Live Board Columns..." };
        var hint = HintLabel(string.Empty);

        void ShowColumns()
        {
            var labels = RelayColumns.Resolve(_settings.WebKpis, _settings.RelayKpis, forRelay: true)
                .Select(k => k.Label);
            var whose = _settings.RelayKpis.Count == 0
                ? "The same columns as the OBS overlay"
                : "Its own columns";

            hint.Text = $"The live race on the club's website. {whose}: {string.Join(", ", labels)}. "
                + "The transponder number is never shown online.";
        }

        btn.Click += (_, _) =>
        {
            var following = RelayColumns.Current(_settings.WebKpis, Array.Empty<string>());

            using var form = new KpiPickerForm("Club website live board",
                RelayColumns.Current(_settings.WebKpis, _settings.RelayKpis),
                following,
                RelayColumns.LeftOut);

            if (form.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var picked = form.SelectedKeys.SequenceEqual(following, StringComparer.OrdinalIgnoreCase)
                ? new List<string>()
                : form.SelectedKeys.ToList();

            var before = _settings.RelayKpis.ToList();

            if (before.SequenceEqual(picked))
            {
                return;
            }

            _undo?.Capture("Change the website's live board columns", () =>
            {
                _settings.RelayKpis = before.ToList();
                _settings.Save();
                ShowColumns();
            });

            _settings.RelayKpis = picked;
            _settings.Save();
            ShowColumns();
        };

        // The Live Results page's columns change on Local Screens; read again
        // whenever this page comes back on screen, so the line is never stale.
        hint.VisibleChanged += (_, _) =>
        {
            if (hint.Visible)
            {
                ShowColumns();
            }
        };

        ShowColumns();
        return (btn, hint);
    }

    /// <summary>
    /// The driver experience ticks (22 Oct 2026) - the drivers' own requests,
    /// the night email and the live page's captions - and what each means,
    /// for the "What gets published" card to place. Returned rather than
    /// kept: only the ticks and the night email's status line and button are
    /// spoken to again.
    /// </summary>
    private (Label RequestsHint, Label QueriesHint, Label NightEmailHint, Label NightEmailAutoHint, Label CaptionsHint) BuildDriverExtras()
    {
        _chkDriverRequests.Text = "Let drivers send changes from their phone";
        _chkDriverRequests.AutoSize = true;
        _chkDriverRequests.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.RelayDriverRequests = _chkDriverRequests.Checked;
            _settings.Save();

            // UNTICKING IS SENT, the way the nights box's is: the relay holds
            // the requests drivers have sent until this laptop collects them,
            // so it is told to stop taking them and throw away what it holds.
            // See RelayPublisher.CloseDriverAsksOnTheRelay. (22 Oct 2026,
            // driver experience.)
            if (!_chkDriverRequests.Checked)
            {
                _publisher.CloseDriverAsksOnTheRelay();
            }

            RefreshDriverExtras();
            _publisher.SettingsChanged();
        };

        var lblRequestsHint = HintLabel(
            "Off by default. From their night page a driver can ask to change their details or battery pack, ask "
            + "to be kept more private online, or query a result. Nothing changes until the desk approves it. Needs "
            + "online check-in and the box above. Unticking throws away any requests still on the relay.");

        // Result queries on a switch of their own (5 Nov 2026, Asher: "can
        // you turn off the something wrong with a result from the app? if
        // not add it to the club website tab"). Sent with the requests
        // switch, so the relay hides "Something wrong?" and refuses a query
        // within one publish pass; the other requests carry on.
        _chkResultQueries.Text = "Let drivers query a result";
        _chkResultQueries.AutoSize = true;
        _chkResultQueries.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.RelayResultQueries = _chkResultQueries.Checked;
            _settings.Save();
            _publisher.SettingsChanged();
        };

        var lblQueriesHint = HintLabel(
            "On by default. Shows \"Something wrong?\" under each race on a driver's night page. Untick it and "
            + "drivers ask about a result at the desk instead; their other requests still work.");

        // The protest window, moved here from Race Day > Setup (11 Nov
        // 2026) so it sits beside the queries it limits - the one place it
        // is set (AppSettings.ProtestMinutes). Not greyed with the queries:
        // the desk's "provisional until" and the announcer use it either way.
        _numProtestMinutes.Minimum = 0;
        _numProtestMinutes.Maximum = ProtestWindow.MostMinutes;
        _numProtestMinutes.ValueChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.ProtestMinutes = (int)_numProtestMinutes.Value;
            _settings.Save();
            _publisher.SettingsChanged();
        };

        _protestGrid = new FieldGrid();
        _protestGrid.AddRow("Protest window (min):", _numProtestMinutes);
        _linedUp.Add(_protestGrid);

        _lblProtestHint = HintLabel(
            $"{ProtestWindow.DefaultMinutes} minutes unless you change it; 0 turns it off. Once each qualifying round "
            + "of a class is in, and each main, its results show \"Provisional until\" a time on the phones and the "
            + "Big Screen, the announcer says queries go to the desk, and when the time is up \"Something wrong?\" goes "
            + "from those races.");

        _chkNightEmail.Text = "Offer drivers their night by email";
        _chkNightEmail.AutoSize = true;
        _chkNightEmail.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.NightEmailOffered = _chkNightEmail.Checked;
            _settings.Save();
            RefreshNightEmailState();
        };

        var lblNightEmailHint = HintLabel(
            "Off by default. Drivers who asked for it on their profile are emailed their races, their report card "
            + "and a link to their night page. Sent from this laptop through the Email card below, and never "
            + "twice to anybody for one night.");

        _chkNightEmailAuto.Text = "Send automatically when the race day closes";
        _chkNightEmailAuto.AutoSize = true;
        _chkNightEmailAuto.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.NightEmailAutomatic = _chkNightEmailAuto.Checked;
            _settings.Save();
        };

        var lblNightEmailAutoHint = HintLabel(
            "When tonight's race day is closed on Race Day. Otherwise, press Email Tonight's Nights.");

        // Rewritten while it sends, and every second - see KeepLines.
        KeepLines(_lblNightEmailState);

        _btnEmailNights.Text = "Email Tonight's Nights...";
        _btnEmailNights.Click += async (_, _) => await EmailNightsAsync();

        _chkRelayCaptions.Text = "Captions on the club's live page";
        _chkRelayCaptions.AutoSize = true;
        _chkRelayCaptions.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            // Saving is all it takes here: the captions are published, or
            // emptied when this goes off, by MainForm.Captions on its own
            // pass. (22 Oct 2026, driver experience.)
            _settings.RelayCaptions = _chkRelayCaptions.Checked;
            _settings.Save();
            _publisher.SettingsChanged();
        };

        var lblCaptionsHint = HintLabel(
            "Off by default. What the announcer says, as text on the live page, for anyone who can't hear the PA "
            + "- no sound needed. Lines about drivers who asked to be shown by initials are left out.");

        return (lblRequestsHint, lblQueriesHint, lblNightEmailHint, lblNightEmailAutoHint, lblCaptionsHint);
    }

    /// <summary>
    /// A part a step in, under the tick it belongs to: the app's usual step,
    /// or as far in as the tick's caption starts once the box has grown past
    /// that with the text (TickBox.CaptionIndent). Set after the card places
    /// it, which gives every part a margin of its own, and again whenever the
    /// text size changes. The card measures a part inside its margins, so a
    /// hint a step in wraps to the room it has. (22 Oct 2026, driver
    /// experience.)
    /// </summary>
    private void StepIn(Control part)
    {
        _textSize.Keep(() =>
        {
            var left = TickBox.CaptionIndent;

            if (part.Margin.Left != left)
            {
                part.Margin = new Padding(left, part.Margin.Top, part.Margin.Right, part.Margin.Bottom);
            }
        });
    }

    /// <summary>
    /// The driver experience ticks greyed out while what they need is off,
    /// the way "Email automatically when PINs change" is under its own
    /// switch: driver requests need online check-in and drivers' nights,
    /// and sending automatically needs the night email offered. The
    /// setting is kept either way. Then the night email's status line.
    /// </summary>
    private void RefreshDriverExtras()
    {
        _chkDriverRequests.Enabled = _settings.PublishDriverNightsToTheRelay && _settings.RelayCheckInEnabled;
        _chkResultQueries.Enabled = _chkDriverRequests.Enabled && _settings.RelayDriverRequests;
        _chkNightEmailAuto.Enabled = _settings.NightEmailOffered;

        RefreshNightEmailState();
    }

    /// <summary>
    /// The night emails at a glance: how many drivers want theirs, how many
    /// have had it tonight - or, while sending, how far it has got.
    /// </summary>
    private void RefreshNightEmailState()
    {
        var desk = NightEmails;
        var sending = _nightProgress is not null || PinMailer.IsSending || desk?.IsSending() == true;

        string text;
        var colour = AppTheme.TextMuted;

        if (_nightProgress is { } progress)
        {
            text = progress;
            colour = AppTheme.Accent;
        }
        else if (!_settings.NightEmailOffered)
        {
            text = "Off. Nobody is emailed their night.";
        }
        else if (desk?.IsSending() == true || PinMailer.IsSending)
        {
            text = "Sending email in the background...";
            colour = AppTheme.Accent;
        }
        else if (!PinMailer.IsConfigured(_settings))
        {
            text = "Set up the Email card below to send drivers their nights.";
            colour = AppTheme.Primary;
        }
        else
        {
            var asked = _profiles.All.Count(p => p.EmailMyNight && !p.IsRental && NightEmail.AddressOf(p.Email) is not null);
            var tonight = NightEmail.NightKey(TonightAtTheTrack.NightOf(DateTime.Now));
            var emailed = string.Equals(_settings.NightEmailSentFor, tonight, StringComparison.Ordinal)
                ? _settings.NightEmailSentTo.Count
                : 0;

            text = asked == 0
                ? "Nobody has asked for their night by email yet - tick it on a driver's profile."
                : $"{Plural.Of(asked, "driver")} {(asked == 1 ? "wants" : "want")} their night by email \u00B7 {emailed} emailed tonight";
            colour = asked == 0 ? AppTheme.TextMuted : AppTheme.Accent;
        }

        if (_lblNightEmailState.Text != text)
        {
            _lblNightEmailState.Text = text;
        }

        _lblNightEmailState.ForeColor = colour;
        _btnEmailNights.Enabled = _settings.NightEmailOffered && desk is not null && !sending;
    }

    /// <summary>
    /// Email Tonight's Nights...: who would be emailed is worked out first,
    /// the count asked about, and only then sent - one at a time, through
    /// MainForm (NightEmails), which records who got theirs so nobody gets
    /// it twice. (22 Oct 2026, driver experience, item 11.)
    /// </summary>
    private async Task EmailNightsAsync()
    {
        var desk = NightEmails;

        if (desk is null)
        {
            return;
        }

        var plan = desk.Plan();

        if (plan.Problem is { } problem)
        {
            AppDialog.Show(this, problem, "Can't email nights", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var count = plan.Messages.Count;

        if (count == 0)
        {
            var why = "Nobody to email tonight.";

            if (plan.AlreadySent > 0)
            {
                why += $"\n\n{Plural.Of(plan.AlreadySent, "driver")} who asked {(plan.AlreadySent == 1 ? "has" : "have")} already been sent tonight's.";
            }

            if (plan.NoAddress > 0)
            {
                why += $"\n\n{Plural.Of(plan.NoAddress, "driver")} who asked {(plan.NoAddress == 1 ? "has" : "have")} no usable address in the Email box on their driver profile.";
            }

            if (plan.AlreadySent == 0 && plan.NoAddress == 0)
            {
                why += "\n\nOnly drivers who raced tonight and asked for their night by email (on their driver profile) are sent one.";
            }

            AppDialog.Show(this, why, "Nobody to email", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // About a second and a half apart, as the PINs go.
        var minutes = Math.Max(1, (int)Math.Ceiling(count * 2 / 60.0));

        var question = $"Email {Plural.Of(count, "driver")} their night?";

        if (plan.AlreadySent > 0)
        {
            question += $"\n\n{Plural.Of(plan.AlreadySent, "driver")} already had tonight's and won't be sent it again.";
        }

        if (plan.NoAddress > 0)
        {
            question += $"\n\n{Plural.Of(plan.NoAddress, "driver")} asked but {(plan.NoAddress == 1 ? "has" : "have")} no usable email address, and won't get one.";
        }

        question += $"\n\nThey go one at a time - about {Plural.Of(minutes, "minute")}. Everything else keeps working meanwhile. "
                    + "An email can't be taken back once it has gone - Undo won't recall it.";

        if (AppDialog.Show(this, question, "Email Tonight's Nights", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _nightProgress = "Starting to email nights...";
        RefreshNightEmailState();

        // Created here, on the UI thread, so each report lands back on it.
        var progress = new Progress<string>(report =>
        {
            if (_nightProgress is not null && !IsDisposed)
            {
                _nightProgress = report;
                RefreshNightEmailState();
            }
        });

        MailResult result;

        try
        {
            result = await desk.SendAsync(progress, _mailCancel.Token);
        }
        catch (Exception ex)
        {
            result = new MailResult { Ok = false, Message = ex.Message };
        }
        finally
        {
            _nightProgress = null;
        }

        if (IsDisposed)
        {
            return;
        }

        RefreshNightEmailState();

        AppDialog.Show(this, DescribeMailResult(result), result.Ok ? "Nights emailed" : "Nights not all emailed",
            MessageBoxButtons.OK,
            result.Ok && result.Failures.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    /// <summary>
    /// How the club's pages greet people: the welcome line, live - the site
    /// document is rebuilt every two seconds and only sent when it differs -
    /// so there is no Save. Then the club's links, one box each, and what
    /// is wrong with one just typed.
    ///
    /// The colours and the logo used to be chosen here too. Since 0.48 they
    /// are one set shared by the app, the Big Screen, the Wi-Fi pages and
    /// these pages, chosen on Themes → Looks, and this card points there
    /// rather than keeping a second copy that could disagree.
    /// </summary>
    private Section BuildLookCard()
    {
        _txtTagline.MaxLength = 160;
        _txtTagline.PlaceholderText = "Racing every Tuesday from 6pm - all welcome";

        // Held in memory as it is typed, so the next publish carries it;
        // written to disk when the box is left rather than per keystroke.
        _txtTagline.TextChanged += (_, _) =>
        {
            if (!_loading)
            {
                _settings.RelayTagline = _txtTagline.Text.Trim();
            }
        };
        _txtTagline.Leave += (_, _) => _settings.Save();

        // The welcome line and every link below it in one column of boxes -
        // the page's (BuildLayout).
        var tagline = new FieldGrid();

        tagline.AddRow("Welcome line:", _txtTagline);

        // A short address and the name under the icon (14 Nov 2026, driver
        // phone plan item 5). Live like the welcome line.
        _txtClubAlias.MaxLength = 32;
        _txtClubAlias.PlaceholderText = "upstateminiz";
        _txtClubAlias.TextChanged += (_, _) =>
        {
            if (!_loading)
            {
                _settings.RelayClubAlias = _txtClubAlias.Text.Trim().ToLowerInvariant();
            }
        };
        _txtClubAlias.Leave += (_, _) => _settings.Save();
        tagline.AddRow("Short address:", _txtClubAlias);

        _txtShortName.MaxLength = 12;
        _txtShortName.PlaceholderText = "Upstate MZ";
        _txtShortName.TextChanged += (_, _) =>
        {
            if (!_loading)
            {
                _settings.RelayShortName = _txtShortName.Text.Trim();
            }
        };
        _txtShortName.Leave += (_, _) => _settings.Save();
        tagline.AddRow("Name under the icon:", _txtShortName);

        _linedUp.Add(tagline);

        // ------------------------------------------------ "New here?"
        //
        // 30 Oct 2026, relay navigation round 6 (Asher: "build it with
        // newcomer"): three lines for a card on the club's home page, for
        // somebody who has found the club but never raced there. Live like
        // the welcome line - held as typed, saved when the box is left.

        var lblNewcomer = _textSize.Wear(new Label { Text = "New here? card", AutoSize = true }, () => AppTheme.BoldFont);

        var lblNewcomerHint = HintLabel(
            "Shown on the club's home page to visitors, above the club's tracks. Leave all three empty for no card.");

        var newcomer = new FieldGrid();

        NewcomerBox(newcomer, _txtNewcomerWhere, "Where we race:", "Kent Hobbies, 12 Main St", 160,
            text => _settings.RelayNewcomerWhere = text);
        NewcomerBox(newcomer, _txtNewcomerWhen, "When we race:", "Tuesdays from 6pm", 160,
            text => _settings.RelayNewcomerWhen = text);
        NewcomerBox(newcomer, _txtNewcomerJoin, "How to join:", "Bring your car and a transponder - the first night is free", 300,
            text => _settings.RelayNewcomerJoin = text);

        _linedUp.Add(newcomer);

        // ------------------------------------------------ the club's links

        var lblSocial = _textSize.Wear(new Label { Text = "Club links", AutoSize = true }, () => AppTheme.BoldFont);

        var lblSocialHint = HintLabel(
            "Shown on the club's home page and at the foot of every page. Paste the full address, or just "
            + "@yourclub for Instagram, YouTube, TikTok and X. Leave a box empty to leave that one off.");

        var links = new FieldGrid();

        foreach (var platform in RelaySocial.Platforms)
        {
            var box = new TextBox
            {
                MaxLength = RelaySocial.MaxUrlLength,
                PlaceholderText = platform.Placeholder,
            };

            // Same as the welcome line: held in memory as typed (the site
            // document is rebuilt every couple of seconds, and ForSite skips
            // anything half-typed), tidied and saved when the box is left.
            box.TextChanged += (_, _) =>
            {
                if (!_loading)
                {
                    _settings.RelaySocialLinks[platform.Key] = box.Text.Trim();
                }
            };

            box.Leave += (_, _) => CommitSocialLink(platform, box);

            _socialBoxes[platform.Key] = box;

            links.AddRow(platform.Label + ":", box);
        }

        // Under the last box, in the boxes' column, in the danger colour -
        // and only while there is a link that can't be used
        // (RefreshSocialProblem). Hidden, it takes no room; shown, what is
        // under it moves down to make room rather than being written over.
        links.AddHint(_lblSocialProblem);
        _lblSocialProblem.ForeColor = AppTheme.Danger;
        _lblSocialProblem.Text = string.Empty;
        _lblSocialProblem.Visible = false;

        _linedUp.Add(links);

        var lblLooksNote = new Label
        {
            Text = "Colors and club logo are set in the Looks card on the Themes page (shared with the app, Big Screen and Wi-Fi pages).",
            AutoSize = true,
        };

        _btnOpenLooks.Text = "Open Themes";
        _btnOpenLooks.Click += (_, _) => OpenLooksRequested?.Invoke();

        var lblLookHint = HintLabel("Changes reach the club's pages within a few seconds.");

        var card = new Section("Look");

        card.Add(tagline, gapBelow: UiLayout.RowGap + UiLayout.HintGap);
        card.Add(lblNewcomer, stretch: false, gapBelow: UiLayout.HintGap);
        card.Add(lblNewcomerHint);
        card.Add(newcomer, gapBelow: UiLayout.SectionGap);
        card.Add(lblSocial, stretch: false, gapBelow: UiLayout.HintGap);
        card.Add(lblSocialHint);
        card.Add(links, gapBelow: UiLayout.SectionGap);
        card.Add(lblLooksNote);
        card.Add(new ButtonBar().With(_btnOpenLooks), gapBelow: UiLayout.HintGap);
        card.Add(lblLookHint, gapBelow: 0);

        return card;
    }

    /// <summary>
    /// "Your first night" (4 Nov 2026, Asher: "the guide should be able to be
    /// edited on laptop under the website nav", and "club should be able to
    /// push newly updated card from the laptop to the relay"): the guide a
    /// driver on their first night sees on their phone, in a box to reword.
    ///
    /// Unlike the welcome line it is NOT sent as it is typed - the site
    /// document goes every couple of seconds, and a half-written guide would
    /// reach drivers' phones. It goes when Send is pressed, and every driver
    /// who hid the old one sees the new one, since a guide that has changed
    /// is shown again (relay/src/pages.ts, GUIDE_KEY).
    /// </summary>
    private Section BuildGuideCard()
    {
        var hint = HintLabel(
            "Shown on the phone of a driver racing here for the first time, on their check-in and night pages. "
            + "One part per paragraph, a blank line between them, each starting with its heading and a colon - "
            + "\"Parking: round the back, by the bins.\"");

        _txtGuide.MaxLength = RelayDocuments.MaxGuideParts * (RelayDocuments.MaxGuideHeading + RelayDocuments.MaxGuideWords + 8);
        _txtGuide.TextChanged += (_, _) => RefreshGuideState();

        _btnSendGuide.Text = "Send to the club's pages";
        _btnSendGuide.Click += (_, _) => SendGuide(_txtGuide.Text);

        _btnStandardGuide.Text = "Put back the standard guide";
        _btnStandardGuide.Click += (_, _) =>
        {
            ShowGuide(string.Empty);
            SendGuide(string.Empty);
        };

        _textSize.Wear(_lblGuideState, () => AppTheme.SmallFont);
        _lblGuideState.AutoSize = true;
        _lblGuideState.ForeColor = AppTheme.TextMuted;

        var card = new Section("Your first night guide");

        card.Add(hint);
        card.Add(_txtGuide, gapBelow: UiLayout.HintGap);
        card.Add(new ButtonBar().With(_btnSendGuide, _btnStandardGuide), gapBelow: UiLayout.HintGap);
        card.Add(_lblGuideState, gapBelow: 0);

        return card;
    }

    /// <summary>The guide in the box: the club's own, or the standard one for a club that has none.</summary>
    private void ShowGuide(string saved)
    {
        // The old standard words, saved unchanged, show as today's (6 Nov 2026).
        _txtGuide.Text = string.IsNullOrWhiteSpace(saved) || RelayDocuments.IsStandardGuide(saved) ? RelayDocuments.StandardGuideText() : saved;
        RefreshGuideState();
    }

    /// <summary>
    /// The box's guide into the settings, which the next pass sends to the
    /// relay within a couple of seconds. The standard words, or nothing at
    /// all, are kept as no guide of the club's own.
    /// </summary>
    private void SendGuide(string text)
    {
        var parts = RelayDocuments.GuideParts(text);
        _settings.RelayFirstNightGuide = parts.Count == 0 || RelayDocuments.IsStandardGuide(text) ? string.Empty : text.Trim();
        _settings.Save();

        if (parts.Count == 0)
        {
            ShowGuide(string.Empty);
        }

        RefreshGuideState();
    }

    /// <summary>Whether what is in the box has gone to the club's pages, and what they show.</summary>
    private void RefreshGuideState()
    {
        var sent = _settings.RelayFirstNightGuide;
        var box = _txtGuide.Text;
        var same = string.IsNullOrWhiteSpace(sent)
            ? RelayDocuments.IsStandardGuide(box)
            : RelayDocuments.GuideParts(box).SequenceEqual(RelayDocuments.GuideParts(sent));

        _btnSendGuide.Enabled = !same;
        _btnStandardGuide.Enabled = !string.IsNullOrWhiteSpace(sent) || !RelayDocuments.IsStandardGuide(box);

        var parts = RelayDocuments.GuideParts(box).Count;

        _lblGuideState.Text = !same
            ? $"Changed here, not sent yet - {Plural.Of(parts, "part")}. Press Send to put it on drivers' phones."
            : string.IsNullOrWhiteSpace(sent)
                ? "Drivers see the standard guide."
                : "Sent. Drivers see this guide within a few seconds, including any who had hidden the old one.";
    }

    /// <summary>
    /// Club updates (4 Nov 2026, Asher: "a separate optional card to push to
    /// the relay for Club Updates. same motion"): the club's news, for a card
    /// on every driver's night page under the first-night guide. Optional -
    /// an empty box is no card - and sent the guide's way: held in the box
    /// until Send, and shown again to a driver who hid the last lot.
    /// </summary>
    private Section BuildUpdatesCard()
    {
        var hint = HintLabel(
            "Optional. News for every driver, on their night page under the first-night guide - a change of night, "
            + "a new track layout, a race coming up. One item per paragraph, a blank line between them; start one "
            + "with a heading and a colon to give it a heading. Leave it empty for no card.");

        _txtUpdates.MaxLength = RelayDocuments.MaxUpdates * (RelayDocuments.MaxGuideHeading + RelayDocuments.MaxUpdateWords + 8);
        _txtUpdates.PlaceholderText = "Track night moved: we race Thursday this week, doors at 6.";
        _txtUpdates.TextChanged += (_, _) => RefreshUpdatesState();

        _btnSendUpdates.Text = "Send to the club's pages";
        _btnSendUpdates.Click += (_, _) => SendUpdates(_txtUpdates.Text);

        _btnClearUpdates.Text = "Take the card down";
        _btnClearUpdates.Click += (_, _) =>
        {
            _txtUpdates.Text = string.Empty;
            SendUpdates(string.Empty);
        };

        _textSize.Wear(_lblUpdatesState, () => AppTheme.SmallFont);
        _lblUpdatesState.AutoSize = true;
        _lblUpdatesState.ForeColor = AppTheme.TextMuted;

        var card = new Section("Club updates");

        card.Add(hint);
        card.Add(_txtUpdates, gapBelow: UiLayout.HintGap);
        card.Add(new ButtonBar().With(_btnSendUpdates, _btnClearUpdates), gapBelow: UiLayout.HintGap);
        card.Add(_lblUpdatesState, gapBelow: 0);

        return card;
    }

    /// <summary>The box's updates into the settings, which the next pass sends to the relay within a couple of seconds.</summary>
    private void SendUpdates(string text)
    {
        _settings.RelayClubUpdates = RelayDocuments.UpdateParts(text).Count == 0 ? string.Empty : text.Trim();
        _settings.Save();
        RefreshUpdatesState();
    }

    /// <summary>Whether what is in the updates box has gone to the club's pages.</summary>
    private void RefreshUpdatesState()
    {
        var sent = RelayDocuments.UpdateParts(_settings.RelayClubUpdates);
        var box = RelayDocuments.UpdateParts(_txtUpdates.Text);
        var same = box.SequenceEqual(sent);

        _btnSendUpdates.Enabled = !same;
        _btnClearUpdates.Enabled = sent.Count > 0 || box.Count > 0;

        _lblUpdatesState.Text = !same
            ? box.Count == 0
                ? "Emptied here, not sent yet. Press Send to take the card off drivers' pages."
                : $"Changed here, not sent yet - {Plural.Of(box.Count, "item")}. Press Send to put it on drivers' phones."
            : sent.Count == 0
                ? "No club updates card on drivers' pages."
                : "Sent. Drivers see these within a few seconds, including any who had hidden the last ones.";
    }

    /// <summary>
    /// One of the "New here?" card's boxes, wired like the welcome line: the
    /// setting follows the box as it is typed in, and is saved when the box
    /// is left.
    /// </summary>
    private void NewcomerBox(FieldGrid grid, TextBox box, string label, string placeholder, int maxLength, Action<string> keep)
    {
        box.MaxLength = maxLength;
        box.PlaceholderText = placeholder;

        box.TextChanged += (_, _) =>
        {
            if (!_loading)
            {
                keep(box.Text.Trim());
            }
        };

        box.Leave += (_, _) => _settings.Save();

        grid.AddRow(label, box);
    }

    /// <summary>
    /// A link box was left: tidy what was typed into the address the relay
    /// will be sent ("yourclub.com" to https://yourclub.com/, "@yourclub"
    /// to the profile), or say why it can't be used. An unusable entry is
    /// kept in the box - and in the settings, so it isn't lost - but never
    /// published; RelaySocial.ForSite leaves it off.
    /// </summary>
    private void CommitSocialLink(RelaySocial.Platform platform, TextBox box)
    {
        if (_loading)
        {
            return;
        }

        var url = RelaySocial.Normalize(platform, box.Text, out var problem);

        if (url is null)
        {
            _settings.RelaySocialLinks[platform.Key] = box.Text.Trim();
            _lblSocialProblem.Text = problem + " It won't be shown until it's fixed.";
            _lblSocialProblem.Visible = true;
        }
        else
        {
            if (url.Length == 0)
            {
                _settings.RelaySocialLinks.Remove(platform.Key);
            }
            else
            {
                _settings.RelaySocialLinks[platform.Key] = url;
            }

            if (box.Text != url)
            {
                _loading = true;

                try
                {
                    box.Text = url;
                }
                finally
                {
                    _loading = false;
                }
            }

            RefreshSocialProblem();
        }

        _settings.Save();
    }

    /// <summary>Shows the first link that still can't be used, or nothing.</summary>
    private void RefreshSocialProblem()
    {
        foreach (var platform in RelaySocial.Platforms)
        {
            if (_socialBoxes.TryGetValue(platform.Key, out var box)
                && RelaySocial.Normalize(platform, box.Text, out var problem) is null)
            {
                _lblSocialProblem.Text = problem + " It won't be shown until it's fixed.";
                _lblSocialProblem.Visible = true;
                return;
            }
        }

        _lblSocialProblem.Text = string.Empty;
        _lblSocialProblem.Visible = false;
    }

    /// <summary>
    /// Drivers checking themselves in from their own phones, through the
    /// relay - which means from mobile data, from the car park, from a
    /// guest Wi-Fi that keeps phones apart, none of which the LAN check-in
    /// page can reach.
    /// </summary>
    private Section BuildCheckInCard()
    {
        _chkCheckIn.Text = "Let drivers check in from their own phones";
        _chkCheckIn.AutoSize = true;
        _textSize.Wear(_chkCheckIn, () => AppTheme.BoldFont);
        _chkCheckIn.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.RelayCheckInEnabled = _chkCheckIn.Checked;

            if (_chkCheckIn.Checked)
            {
                // Made now rather than at the first badge print, so the ids
                // the relay learns and the ids the badges carry come from
                // the same key from the very first publish.
                RelayCheckIn.EnsureSecret(_settings);
            }

            _settings.Save();
            RefreshCheckInState();
        };

        _txtCheckInPin.MaxLength = 8;
        _txtCheckInPin.PlaceholderText = "e.g. 246813";

        // Digits only: the check-in page asks for it with a phone's number
        // pad, and a PIN with a letter in it could not be typed there.
        // ASCII digits, not char.IsDigit - that also lets through Arabic,
        // Devanagari and full-width digits, which no number pad types.
        _txtCheckInPin.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) && !char.IsAsciiDigit(e.KeyChar))
            {
                e.Handled = true;
            }
        };
        _txtCheckInPin.TextChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            // Pasted text gets the same rule as typed text.
            var digits = new string(_txtCheckInPin.Text.Where(char.IsAsciiDigit).ToArray());

            if (digits != _txtCheckInPin.Text)
            {
                _txtCheckInPin.Text = digits;
                _txtCheckInPin.SelectionStart = digits.Length;
            }
        };

        // Taken into settings when the box is left, not per keystroke. The
        // check-in list is republished every two seconds, so typing "2468"
        // used to publish the hash of "2", then "24", then "246" - each of
        // them, for a moment, the PIN that opened every driver's name.
        //
        // A new or changed PIN has to be six to eight digits (see
        // RelayCheckIn.MinNewPinLength). The PIN already saved is left
        // alone even if it is shorter - it still works, and gets a warning
        // below rather than being thrown out. Clearing the box is always
        // allowed: no staff PIN just keeps the full list closed.
        _txtCheckInPin.Leave += (_, _) =>
        {
            var digits = new string(_txtCheckInPin.Text.Where(char.IsAsciiDigit).ToArray());

            if (digits == _settings.RelayCheckInPin)
            {
                _pinRejected = null;
                RefreshCheckInState();
                return;
            }

            if (digits.Length > 0 && !RelayCheckIn.IsStrongPin(digits))
            {
                _pinRejected = RelayCheckIn.IsUsablePin(_settings.RelayCheckInPin)
                    ? $"Not saved - a new staff PIN must be {RelayCheckIn.MinNewPinLength} to 8 digits. The old staff PIN still works until it is changed."
                    : $"Not saved - a new staff PIN must be {RelayCheckIn.MinNewPinLength} to 8 digits.";
                RefreshCheckInState();
                return;
            }

            // Already some racer's personal PIN (RacerPins.IsHeldByRacer):
            // the racer may have it in an email, and it would open everybody's
            // list. Said without saying whose.
            if (RacerPins.IsHeldByRacer(_settings, digits))
            {
                _pinRejected = RelayCheckIn.IsUsablePin(_settings.RelayCheckInPin)
                    ? "Not saved - that PIN is already in use elsewhere in HeatCaller. Choose another. The old staff PIN still works until it is changed."
                    : "Not saved - that PIN is already in use elsewhere in HeatCaller. Choose another.";
                RefreshCheckInState();
                return;
            }

            _pinRejected = null;
            _settings.RelayCheckInPin = digits;
            _settings.Save();
            RefreshCheckInState();
        };

        // (22 Oct 2026, page tidy): this grid kept a caption column of its
        // own because its caption, "Staff PIN (opens the full list):", was
        // too long to share - so its box started at a different x from every
        // other card's. The bracket said what the hint under it says, so the
        // caption is now "Staff PIN:" and the grid lines up with the rest.
        var pin = new FieldGrid();

        pin.AddRow("Staff PIN:", _txtCheckInPin);

        _linedUp.Add(pin);

        var lblHint = HintLabel(
            "The staff PIN opens everybody's list on the club's check-in page, for a tablet on the desk. A "
            + "driver's QR check-in (print them on Race Day > Who's Racing while this is on) or their own PIN "
            + "(Personal PINs, below) opens just that driver, no staff PIN needed. Nobody is checked in until "
            + "they tap Check In, and taps reach this laptop within a few seconds.");

        _chkCheckInPhotos.Text = "Include photos (drivers can be children - off unless you're sure)";
        _chkCheckInPhotos.AutoSize = true;
        _chkCheckInPhotos.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            // Saving is all it takes: the check-in list is rebuilt every
            // two seconds and sent whenever it differs, so the photos go
            // on - or come off - the relay with the next pass.
            _settings.RelayCheckInPhotos = _chkCheckInPhotos.Checked;
            _settings.Save();
        };

        // EMAIL ME A LINK (14 Nov 2026, driver phone plan item 17, Asher: "yes
        // to email me a sign in link. make it optional"). Off by default: it
        // sends from the club's own mailbox. The relay offers it on Me only
        // while this is on and the mail is set up; this laptop sends each
        // link to an address it knows, and answers nobody it doesn't.
        _chkEmailSignInLink.Text = "Let drivers email themselves a link to their page (uses the club's email, below)";
        _chkEmailSignInLink.AutoSize = true;
        _chkEmailSignInLink.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.RelayEmailSignInLink = _chkEmailSignInLink.Checked;
            _settings.Save();
        };

        // Refreshed every second, and says things of different lengths -
        // see KeepLines.
        KeepLines(_lblCheckInState);

        _btnOpenCheckIn.Text = "Open Check-in Page";
        _btnOpenCheckIn.Click += (_, _) =>
        {
            var page = ClubPageUrl();

            if (page.Length > 0)
            {
                OpenUrl(page + "/checkin");
            }
        };

        // Beside Open Check-in Page rather than on a row of its own, so the
        // card is no taller for the club that never needs it - and the row
        // is never left empty, Open Check-in Page being always there. On
        // show only once there is a badge key to replace - see
        // RefreshCheckInState.
        _btnReissueBadges.Text = "Reissue All QR Check-ins...";
        _btnReissueBadges.Click += (_, _) => ReissueAllBadges();

        var card = new Section("Online check-in");

        card.Add(_chkCheckIn, stretch: false);
        card.Add(pin, gapBelow: UiLayout.HintGap);
        card.Add(lblHint);
        card.Add(SimpleMode.Hide(_chkCheckInPhotos), stretch: false);
        card.Add(SimpleMode.Hide(_chkEmailSignInLink), stretch: false);
        card.Add(_lblCheckInState, gapBelow: UiLayout.HintGap);
        card.Add(new ButtonBar().With(_btnOpenCheckIn, _btnReissueBadges), gapBelow: 0);

        return card;
    }

    /// <summary>One line on whether check-in is working, and if not, the thing to do about it.</summary>
    private void RefreshCheckInState()
    {
        string text;
        var colour = AppTheme.TextMuted;

        if (_pinRejected is not null)
        {
            // First, whatever else is going on: it is about what was just
            // typed, and the box still shows a PIN that is not in use.
            text = _pinRejected;
            colour = AppTheme.Danger;
        }
        else if (!_settings.RelayCheckInEnabled)
        {
            text = "Off. The check-in page says it is closed, and QR check-ins print in the Wi-Fi-only form.";
        }
        else if (!_settings.RelayEnabled)
        {
            text = "Switched on, but publishing to the relay is off - nothing reaches the check-in page until it is on.";
            colour = AppTheme.Primary;
        }
        else if (_publisher.CheckInProblem is { } problem)
        {
            text = problem;
            colour = AppTheme.Danger;
        }
        else if (_publisher.RelayPinLockedUntilUtc is DateTime lockedUntil)
        {
            // The relay has stopped taking PINs - racers' own as well as
            // the staff one - for this club. Publishing a different staff
            // PIN is what reopens it; badges keep working meanwhile. Ahead
            // of the PIN checks below because it is the thing stopping
            // sign-in right now, whatever they would say. The lock grows
            // each time, so past today the day is given too.
            var local = lockedUntil.ToLocalTime();
            var when = local.Date == DateTime.Today ? AppTime.TimeOfDay(local) : AppTime.DateAndTime(local);
            var fix = _settings.RelayCheckInPin.Length == 0 ? "set a staff PIN" : "change the staff PIN";
            text = $"PIN sign-in is locked after many wrong guesses until {when} - {fix} to reopen it.";
            colour = AppTheme.Danger;
        }
        else if (_settings.RelayCheckInPin.Length == 0)
        {
            text = "Open for QR check-ins and racers' own PINs. No staff PIN is set, so the full list stays closed.";
            colour = AppTheme.Accent;
        }
        else if (!RelayCheckIn.IsUsablePin(_settings.RelayCheckInPin))
        {
            // Not published at all until it is long enough - see
            // RelayCheckIn.IsUsablePin - so this is an error, not advice.
            // Says six, not four: four and five only still work for a PIN
            // set before the six-digit rule, and a new one needs six.
            text = "The staff PIN must be 6 to 8 digits. Until it is, the full list stays closed and only QR check-ins and racers' own PINs work.";
            colour = AppTheme.Danger;
        }
        else if (RelayCheckIn.IsWeakPin(_settings.RelayCheckInPin))
        {
            text = "Works, but a 4- or 5-digit staff PIN can be guessed - change it to 6 or more digits.";
            colour = AppTheme.Primary;
        }
        else
        {
            text = "Open - QR check-ins, racers' own PINs and the staff PIN list all work.";
            colour = AppTheme.Accent;
        }

        if (_lblCheckInState.Text != text)
        {
            _lblCheckInState.Text = text;
        }

        _lblCheckInState.ForeColor = colour;
        _btnOpenCheckIn.Enabled = _settings.RelayCheckInEnabled && ClubPageUrl().Length > 0;

        // Offered once there is a badge key, whether check-in is on or off
        // today: badges printed while it was on carry ids made from it, and
        // the desk's scanner still reads them. Before there is a key, no
        // badge carries an online id and there is nothing to reissue.
        var offerReissue = RelayCheckIn.CanReissue(_settings);

        if (offerReissue != _reissueOffered)
        {
            _reissueOffered = offerReissue;
            _btnReissueBadges.Visible = offerReissue;
        }
    }

    /// <summary>
    /// Reissue All Badges: a new badge key, so every driver's online id
    /// changes and every one handed out so far - on a printed badge, or
    /// kept from the staff list by somebody who once had the staff PIN -
    /// stops working. See RelayCheckIn.Reissue.
    ///
    /// Undo puts the old key back - for a reissue pressed by mistake, before
    /// any badge has been printed with the new one. Nothing here can see
    /// whether one has, so it is still asked plainly, with No as the answer
    /// a stray Enter gives, and the dialog says what Undo would cost.
    /// </summary>
    private void ReissueAllBadges()
    {
        if (!RelayCheckIn.CanReissue(_settings))
        {
            return;
        }

        var answer = AppDialog.Show(
            this,
            "Reissue all QR check-ins?\n\n"
            + "Every QR check-in printed so far that says \"Scan with your phone camera to check in\" stops working - on "
            + "phones and at the desk scanner - and has to be printed again. Check-in links anybody has kept stop "
            + "working too.\n\n"
            + "Racers' own PINs and the staff PIN keep working. Undo puts the old QR check-ins back, but only until "
            + "new QR check-ins have been printed - those would then stop working instead.",
            "Reissue All QR Check-ins",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        // Asked again after the answer: nothing should have changed while
        // the question was up, but a key made from nothing is not a reissue.
        if (answer != DialogResult.Yes || !RelayCheckIn.CanReissue(_settings))
        {
            return;
        }

        // The key itself, not the page: the undo works on settings and
        // tells the host through the same event the reissue does, so the
        // ids are worked out again and the check-in list sent.
        // Held sealed (DPAPI), as settings keep it on disk, rather than as
        // plain text in memory for as long as the step is on the list - a
        // reissue is often done because the old key got out. Sealed here,
        // before Reissue replaces the key: this is the "before" Undo puts
        // back.
        string? sealedOldSecret = null;
        try
        {
            sealedOldSecret = _undo is null ? null : SecretBox.Seal(_settings.RelayCheckInSecret);
        }
        catch
        {
            // Can't seal it: no undo, rather than one holding it in the clear.
        }

        // The new key could not be written to settings.json, so Reissue has
        // put the old one back: nothing changed, and nothing here may say it
        // did. No undo step (there is nothing to undo), no BadgesReissued
        // (every id is the same), no "Reissued" in the console, and above
        // all no "print new QR check-ins" - those would carry the same key,
        // and the lost badge the reissue was meant to stop would still work.
        if (!RelayCheckIn.Reissue(_settings))
        {
            RefreshCheckInState();

            AppDialog.Show(
                this,
                "The new QR check-in key couldn't be saved to settings.json, so nothing was reissued. The QR check-ins "
                + "already handed out still work.\n\nClose anything that might have settings.json open and try again.",
                "Not reissued",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        // Pushed only now the new key is on disk - an undo step for a
        // reissue that never happened would "put back" the key already in
        // use. Pushing it after the change rather than before is safe: the
        // old key was sealed above, before Reissue touched it.
        if (sealedOldSecret is not null)
        {
            const string undoName = "Reissue all QR check-ins";
            Action? undoReissue = null;

            undoReissue = () =>
            {
                var newer = _settings.RelayCheckInSecret;
                _settings.RelayCheckInSecret = SecretBox.Open(sealedOldSecret);

                // The old key back only in memory would make the old badges
                // work tonight and stop again at the next start, when the
                // new key loads from disk - while the undo said it was done.
                // So, unsaved, the new key stays, the step goes back on the
                // list to try again, and the director is told.
                if (!_settings.Save())
                {
                    _settings.RelayCheckInSecret = newer;
                    _undo?.Capture(undoName, undoReissue!);
                    throw new InvalidOperationException(
                        "The old QR check-in key couldn't be saved to settings.json, so the reissue was not undone - "
                        + "the QR check-ins printed since still work. Close anything that might have settings.json open and press Undo again.");
                }

                BadgesReissued?.Invoke();
            };

            _undo?.Capture(undoName, undoReissue);
        }

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [relay] Reissued all QR check-ins: a new QR check-in key was made, so every QR check-in and link handed out before now has stopped working.");

        try
        {
            BadgesReissued?.Invoke();
        }
        catch (Exception ex)
        {
            // The new key is saved whatever happens here, and the host works
            // the ids out again and sends the check-in list on its own
            // two-second pass anyway - so this is a delay, not a failure, and
            // must not turn into a crash after the key has already changed.
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [relay] Couldn't send the check-in list with the new QR check-in ids straight away ({ex.Message}). It goes on the next pass.");
        }

        RefreshCheckInState();

        AppDialog.Show(
            this,
            "Done. Print new QR check-ins with Print QR Check-ins on Race Day \u2192 Who's Racing, and hand them out - the old ones no longer work.",
            "Reissue All QR Check-ins",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    /// <summary>One entry in the "Change them" list.</summary>
    private sealed record RotationChoice(PinRotation Value, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>One entry in the mail provider list. Other has no server of its own.</summary>
    private sealed record PresetChoice(string Name, string Host, int Port)
    {
        public bool IsOther => Host.Length == 0;

        public override string ToString() => Name;
    }

    /// <summary>
    /// A PIN of every racer's own: typed on the check-in page, it checks in
    /// that racer and nobody else. For the racer who left their badge at
    /// home, and for the club that would rather not print badges at all.
    /// </summary>
    private Section BuildPinsCard()
    {
        _chkRacerPins.Text = "Give every racer their own PIN";
        _chkRacerPins.AutoSize = true;
        _textSize.Wear(_chkRacerPins, () => AppTheme.BoldFont);
        _chkRacerPins.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.RelayRacerPinsEnabled = _chkRacerPins.Checked;
            _settings.Save();
            RefreshPinsState();
            RacerPinsChanged?.Invoke();
        };

        var lblPinsHint = HintLabel(
            "Six digits each, never shared. A racer types theirs on the club's check-in page and it checks in "
            + "only them - it cannot open the list. The relay only ever gets a scrambled form of each PIN. The "
            + "staff PIN above still opens everything.");

        _cmbPinRotation.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbPinRotation.Items.AddRange(new object[]
        {
            new RotationChoice(PinRotation.Never, "Never"),
            new RotationChoice(PinRotation.Daily, "Every race day"),
            new RotationChoice(PinRotation.Weekly, "Weekly"),
            new RotationChoice(PinRotation.Monthly, "Monthly"),
        });

        _cmbPinRotation.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _cmbPinRotation.SelectedItem is not RotationChoice choice)
            {
                return;
            }

            // Only the schedule. The set itself is replaced when it next
            // falls due, the first time the app runs on that day - never
            // from here, halfway through somebody reading their email.
            _settings.RacerPinRotation = choice.Value;
            _settings.Save();
            RefreshPinsState();
        };

        var rotation = new FieldGrid();

        rotation.AddRow("Change them:", _cmbPinRotation);

        _linedUp.Add(rotation);

        // Two lines kept, like check-in's state line above it - see KeepLines.
        KeepLines(_lblPinsState);

        _btnShowPins.Text = "Show PINs...";
        _btnShowPins.Click += (_, _) =>
        {
            using var form = new RacerPinsForm(_settings, _profiles);
            form.ShowDialog(this);
        };

        _btnNewPins.Text = "New PINs Now";
        _btnNewPins.Click += (_, _) => MakeNewPins();

        _btnEmailPins.Text = "Email PINs...";
        _btnEmailPins.Click += async (_, _) => await EmailPinsAsync();

        _chkEmailPinsAuto.Text = "Email automatically when PINs change";
        _chkEmailPinsAuto.AutoSize = true;
        _chkEmailPinsAuto.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.RacerPinsEmailAutomatically = _chkEmailPinsAuto.Checked;
            _settings.Save();
        };

        var lblAutoHint = HintLabel(
            "Racers are sent their new PIN when a new set is made, and a newcomer, or anyone not yet sent "
            + "theirs, within ten minutes. Needs an email address on the driver profile and the Email card "
            + "below set up.");

        var card = new Section("Personal PINs");

        card.Add(_chkRacerPins, stretch: false, gapBelow: UiLayout.HintGap);
        card.Add(lblPinsHint);
        card.Add(rotation);
        card.Add(_lblPinsState, gapBelow: UiLayout.HintGap);
        card.Add(new ButtonBar().With(_btnShowPins, _btnNewPins, _btnEmailPins));
        card.Add(_chkEmailPinsAuto, stretch: false, gapBelow: UiLayout.HintGap);
        card.Add(lblAutoHint, gapBelow: 0);

        return card;
    }

    private void SelectRotation(PinRotation rotation)
    {
        var index = _cmbPinRotation.Items.Cast<RotationChoice>().ToList().FindIndex(c => c.Value == rotation);
        _cmbPinRotation.SelectedIndex = index < 0 ? 0 : index;
    }

    /// <summary>
    /// The personal PINs at a glance: how many, when they were made, when
    /// they change, how many have been emailed - or, while emailing, how
    /// far it has got.
    /// </summary>
    private void RefreshPinsState()
    {
        var racers = _profiles.All;
        var withPin = racers.Count(p => _settings.RacerPins.ContainsKey(p.Transponder));
        var sending = _pinProgress is not null || PinMailer.IsSending;

        string text;
        var colour = AppTheme.TextMuted;

        if (_pinProgress is { } progress)
        {
            text = progress;
            colour = AppTheme.Accent;
        }
        else if (PinMailer.IsSending)
        {
            text = "Sending email in the background...";
            colour = AppTheme.Accent;
        }
        else if (!_settings.RelayRacerPinsEnabled)
        {
            text = "Off. Racers check in with their QR check-in, or somebody with the staff PIN checks them in.";
        }
        else if (!_settings.RelayCheckInEnabled)
        {
            text = "Online check-in is off above. PINs are made, and work, once it is on.";
            colour = AppTheme.Primary;
        }
        else if (withPin == 0)
        {
            text = racers.Count == 0
                ? "No racers yet. Each one gets a PIN within a few seconds of being added."
                : "Making PINs - they appear within a few seconds.";
        }
        else
        {
            var emailed = racers.Count(p => _settings.RacerPins.ContainsKey(p.Transponder)
                                            && RacerPins.IsEmailed(_settings, p.Transponder));

            var parts = new List<string> { withPin == 1 ? "1 racer has a PIN" : $"{withPin} racers have PINs" };

            if (_settings.RacerPinsIssuedUtc is { } issued)
            {
                parts.Add($"made {issued.ToLocalTime():d MMM}");
            }

            if (RacerPins.NextChangeLocal(_settings) is { } next)
            {
                parts.Add(next <= DateTime.Today ? "changing today" : $"change next {next:d MMM}");
            }

            parts.Add($"{emailed} emailed");

            text = string.Join(" \u00B7 ", parts);
            colour = AppTheme.Accent;

            if (!PinMailer.IsConfigured(_settings))
            {
                text += Environment.NewLine + "Set up the Email group below to send racers their PINs.";
            }
        }

        if (_lblPinsState.Text != text)
        {
            _lblPinsState.Text = text;
        }

        _lblPinsState.ForeColor = colour;

        var on = _settings.RelayRacerPinsEnabled;

        _cmbPinRotation.Enabled = on;
        _chkEmailPinsAuto.Enabled = on;
        _btnShowPins.Enabled = withPin > 0;
        _btnNewPins.Enabled = on && withPin > 0 && !sending;
        _btnEmailPins.Enabled = on && withPin > 0 && !sending;
    }

    private void MakeNewPins()
    {
        var answer = AppDialog.Show(
            this,
            "Make a new PIN for every racer now?\n\n"
            + "Every racer's old PIN stops working straight away."
            + (_settings.RacerPinsEmailAutomatically
                ? " Each racer with an email address is sent their new one - Undo brings the old PINs back, but an email can't be recalled."
                : " Nobody is told - use Email PINs afterward, or hand them out from Show PINs."),
            "New PINs",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (answer != DialogResult.Yes)
        {
            return;
        }

        // The whole old set, with its date and who was emailed it, so Undo
        // makes those PINs work again. It can't take back an email: anybody
        // already sent a new PIN has one that stops working again.
        var oldPins = RacerPins.CaptureSet(_settings);
        bool saved;

        try
        {
            RacerPins.RegenerateAll(_settings, _profiles);
            saved = _settings.Save();
        }
        catch (Exception ex)
        {
            // Back to the old set in memory too, so nothing later - the
            // ten-minute pass, an automatic email - picks up a set that
            // never reached disk. Exactly as it was: RestoreSet would also
            // hand out PINs to anyone added since, and those would not be on
            // disk either (KeepRacerPinsInStep gives them theirs once it can
            // save).
            RacerPins.PutBackExactly(_settings, oldPins);
            RefreshPinsState();
            AppDialog.Show(this, $"Couldn't make new PINs: {ex.Message}", "New PINs", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Save doesn't throw when settings.json can't be written (locked by
        // another program, a read-only folder, a full disk): it reports the
        // failure and returns false. The PINs live nowhere but settings.json,
        // so a set that wasn't written must not be published or emailed -
        // RacerPinsChanged would do both, and after the next restart the old
        // set would load from disk and every PIN just emailed would stop
        // working. So the old set goes back in memory, which is still what
        // is on disk and on the relay, and nobody is told anything new.
        if (!saved)
        {
            RacerPins.PutBackExactly(_settings, oldPins);
            RefreshPinsState();
            AppDialog.Show(
                this,
                "The new PINs couldn't be saved, so they haven't been sent anywhere and nobody was emailed. "
                + "Every racer's PIN is unchanged.\n\nClose anything that might have settings.json open and try again.",
                "New PINs",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        // Captured only once the new set is safely on disk: a step put on
        // the list before a write that then failed would "undo" a change
        // that never happened, and UndoService has no way to take one back
        // off. oldPins was taken before RegenerateAll, so it is still the
        // set to go back to.
        const string undoName = "New PINs for every racer (emailed PINs can't be recalled)";

        Action? undoNewPins = null;
        undoNewPins = () =>
        {
            var newer = RacerPins.CaptureSet(_settings);
            RacerPins.RestoreSet(_settings, _profiles, oldPins);

            // The old set back only once it is on disk too - otherwise it
            // would be published and emailed from memory, and the next
            // restart would bring the new set back from settings.json. The
            // step goes back on the list first, so Undo can be pressed again
            // once the file can be written (UndoService has already taken it
            // off).
            if (!_settings.Save())
            {
                RacerPins.PutBackExactly(_settings, newer);

                if (!IsDisposed)
                {
                    RefreshPinsState();
                }

                _undo?.Capture(undoName, undoNewPins!);
                throw new InvalidOperationException(
                    "the old PINs couldn't be saved to settings.json, so the new PINs are still in use. "
                    + "Close anything that might have settings.json open and press Undo again.");
            }

            if (!IsDisposed)
            {
                RefreshPinsState();
            }

            RacerPinsChanged?.Invoke();
        };

        _undo?.Capture(undoName, undoNewPins);

        RefreshPinsState();
        RacerPinsChanged?.Invoke();
    }

    private async Task EmailPinsAsync()
    {
        if (PinMailer.ConfigurationProblem(_settings) is { } problem)
        {
            AppDialog.Show(this, problem + "\n\nSet up the Email group on this page first.", "Can't email PINs",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var withPin = _profiles.All.Where(p => _settings.RacerPins.ContainsKey(p.Transponder)).ToList();
        var reachable = withPin.Count(p => RacerPins.EmailFor(p) is not null);
        var unreachable = withPin.Count - reachable;

        if (reachable == 0)
        {
            AppDialog.Show(this,
                "Nobody with a PIN has an email address yet.\n\n"
                + "Add one in the Email box on each racer's driver profile, or import an Email column from a CSV on the Driver Hub page.",
                "Nobody to email", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // About a second and a half apart, so the provider does not take a
        // burst of a hundred for spam.
        var minutes = Math.Max(1, (int)Math.Ceiling(reachable * 2 / 60.0));

        var question = $"Email {Plural.Of(reachable, "racer")} their PIN?";

        if (unreachable > 0)
        {
            question += $"\n\n{Plural.Of(unreachable, "racer")} {(unreachable == 1 ? "has" : "have")} no email address and won't get one.";
        }

        question += $"\n\nThey go one at a time - about {Plural.Of(minutes, "minute")}. Everything else keeps working meanwhile. "
                    + "An email can't be taken back once it has gone - Undo won't recall it.";

        if (AppDialog.Show(this, question, "Email PINs", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        // Asked after the question, not before: "New PINs now" a moment ago
        // means the host is still making their hashes in the background, and
        // the relay holds the old PINs until it has sent the list that
        // carries the new ones. Emailed now, a racer could open the email,
        // tap their new PIN straight away and be refused. It takes seconds,
        // so the director is simply asked to wait.
        if (CheckInListBeingPrepared?.Invoke() == true)
        {
            AppDialog.Show(this,
                "The new PINs are still being sent to the club website. Try again in a few seconds, so nobody is "
                + "emailed a PIN the website doesn't know yet.",
                "Not ready yet", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _pinProgress = "Starting to email PINs...";
        RefreshPinsState();

        // Created here, on the UI thread, so each report lands back on it.
        var progress = new Progress<string>(report =>
        {
            if (_pinProgress is not null && !IsDisposed)
            {
                _pinProgress = report;
                RefreshPinsState();
            }
        });

        MailResult result;

        // Raised here, on the UI thread, and matched by exactly one
        // PinEmailFinished below - every path from here on reaches it.
        PinEmailStarted?.Invoke();

        try
        {
            try
            {
                result = await PinMailer.SendPinsAsync(_settings, _profiles, null, progress, _mailCancel.Token);
            }
            catch (Exception ex)
            {
                result = new MailResult { Ok = false, Message = ex.Message };
            }
            finally
            {
                _pinProgress = null;
            }

            if (IsDisposed)
            {
                return;
            }

            // Back on the UI thread: the only place settings are changed.
            if (RacerPins.MarkEmailed(_settings, result.Sent) > 0)
            {
                _settings.Save();
            }
        }
        finally
        {
            // Before the result dialog, not after it: a racer approved from
            // an online sign-up while this ran is waiting on this send to end
            // for their own PIN email, and should not also wait for the
            // director to dismiss a dialog.
            PinEmailFinished?.Invoke();
        }

        RefreshPinsState();

        AppDialog.Show(this, DescribeMailResult(result), result.Ok ? "PINs emailed" : "PINs not all emailed",
            MessageBoxButtons.OK,
            result.Ok && result.Failures.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    /// <summary>The result dialog's text: what went, what didn't and why, and who has no address.</summary>
    private static string DescribeMailResult(MailResult result)
    {
        var text = result.Message;

        if (!result.Ok && result.Sent.Count > 0)
        {
            text += $"\n\n{Plural.Of(result.Sent.Count, "racer")} did get theirs.";
        }

        if (result.Failures.Count > 0)
        {
            text += $"\n\nCouldn't send to {result.Failures.Count}:\n" + string.Join("\n", result.Failures.Take(10).Select(f => "   " + f));

            if (result.Failures.Count > 10)
            {
                text += $"\n   ...and {result.Failures.Count - 10} more.";
            }
        }

        if (result.NoEmail.Count > 0)
        {
            text += $"\n\nNo email address for {result.NoEmail.Count}:\n   " + string.Join(", ", result.NoEmail.Take(20));

            if (result.NoEmail.Count > 20)
            {
                text += $" and {result.NoEmail.Count - 20} more";
            }

            text += "\nAdd one in the Email box on their driver profile.";
        }

        return text;
    }

    /// <summary>
    /// The club's own mailbox, for sending racers their PINs. Straight from
    /// this laptop to the provider - Gmail, iCloud, Outlook - with nothing
    /// in between. Saved as it is typed, like the look settings above.
    /// </summary>
    private Section BuildMailCard()
    {
        _cmbMailPreset.DropDownStyle = ComboBoxStyle.DropDownList;

        foreach (var preset in PinMailer.Presets)
        {
            _cmbMailPreset.Items.Add(new PresetChoice(preset.Name, preset.Host, preset.Port));
        }

        _cmbMailPreset.Items.Add(new PresetChoice("Other", string.Empty, 587));

        _cmbMailPreset.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _cmbMailPreset.SelectedItem is not PresetChoice choice)
            {
                return;
            }

            if (!choice.IsOther)
            {
                _settings.SmtpHost = choice.Host;
                _settings.SmtpPort = choice.Port;
                _settings.Save();
                ConfirmRestoredMail();

                // Shown without their own handlers saving it all again.
                _loading = true;

                try
                {
                    _txtSmtpHost.Text = choice.Host;
                    _numSmtpPort.Value = choice.Port;
                }
                finally
                {
                    _loading = false;
                }
            }

            ShowPresetEditable(choice.IsOther);
            RefreshMailProblem();
        };

        _txtSmtpHost.PlaceholderText = "smtp.example.com";

        _txtSmtpHost.TextChanged += (_, _) =>
        {
            if (!_loading)
            {
                _settings.SmtpHost = _txtSmtpHost.Text.Trim();

                // Typed here, so it is the club's choice - see RestoredSettings.
                ConfirmRestoredMail();
            }
        };
        _txtSmtpHost.Leave += (_, _) => _settings.Save();

        _numSmtpPort.Minimum = 1;
        _numSmtpPort.Maximum = 65535;
        _numSmtpPort.ValueChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.SmtpPort = (int)_numSmtpPort.Value;
            _settings.Save();
            RefreshMailProblem();
        };

        _txtSmtpUser.PlaceholderText = "club@gmail.com";
        _txtSmtpUser.TextChanged += (_, _) =>
        {
            if (!_loading)
            {
                _settings.SmtpUser = _txtSmtpUser.Text.Trim();
            }
        };
        _txtSmtpUser.Leave += (_, _) =>
        {
            _settings.Save();
            RefreshMailProblem();
        };

        _txtSmtpPassword.UseSystemPasswordChar = true;
        _txtSmtpPassword.PlaceholderText = "abcd efgh ijkl mnop";
        _txtSmtpPassword.TextChanged += (_, _) =>
        {
            if (!_loading)
            {
                // Spaces out: Google shows an app password in groups of
                // four, and a pasted copy brings the spaces along. No
                // provider's app password contains one.
                _settings.SmtpPassword = new string(_txtSmtpPassword.Text.Where(c => !char.IsWhiteSpace(c)).ToArray());
            }
        };
        _txtSmtpPassword.Leave += (_, _) =>
        {
            _settings.Save();
            RefreshMailProblem();
        };

        _chkShowSmtpPassword.Text = "Show";
        _chkShowSmtpPassword.AutoSize = true;
        _textSize.Wear(_chkShowSmtpPassword, () => AppTheme.SmallFont);
        _chkShowSmtpPassword.CheckedChanged += (_, _) => _txtSmtpPassword.UseSystemPasswordChar = !_chkShowSmtpPassword.Checked;

        _txtSmtpFromName.MaxLength = 80;
        _txtSmtpFromName.PlaceholderText = "e.g. Acme RC Club - blank uses the club id";
        _txtSmtpFromName.TextChanged += (_, _) =>
        {
            if (!_loading)
            {
                _settings.SmtpFromName = _txtSmtpFromName.Text.Trim();
            }
        };
        _txtSmtpFromName.Leave += (_, _) => _settings.Save();

        _btnTestMail.Text = "Send Test Email";
        _btnTestMail.Click += async (_, _) => await SendTestMailAsync();

        // The server and its port on one line, wrapping the port under the
        // server when the line is too narrow for both (FieldGrid.Inline).
        // The test's button and what it said under the boxes, in their
        // column, as they always were.
        var fields = new FieldGrid();

        fields.AddRow("Provider:", _cmbMailPreset);
        fields.AddRow("Server:", FieldGrid.Inline(_txtSmtpHost, new Label { Text = "Port:" }, _numSmtpPort));
        fields.AddRow("Club email address:", _txtSmtpUser);
        fields.AddRow("App password:", _txtSmtpPassword);
        fields.AddField(_chkShowSmtpPassword);
        fields.AddRow("From name:", _txtSmtpFromName);
        fields.AddField(FieldGrid.Inline(_btnTestMail));
        fields.AddHint(_lblMailResult);

        _linedUp.Add(fields);

        // What the test said, or what is wrong with the settings - two
        // lines kept whatever it says (KeepLines).
        _lblMailResult.Text = MailTestHint;
        KeepLines(_lblMailResult);

        var lblMailHint = HintLabel(
            "Gmail/iCloud need an app password (Google Account \u2192 Security \u2192 App passwords). "
            + "Nothing goes through a cloud service - mail is sent from this laptop.");

        var card = new Section("Email (for racer PINs)");

        card.Add(fields, gapBelow: UiLayout.HintGap);
        card.Add(lblMailHint, gapBelow: 0);

        return card;
    }

    /// <summary>Picks the provider matching the saved server and port, or Other.</summary>
    private void SelectPreset()
    {
        var choices = _cmbMailPreset.Items.Cast<PresetChoice>().ToList();
        var index = choices.FindIndex(c => !c.IsOther
                                           && string.Equals(c.Host, _settings.SmtpHost.Trim(), StringComparison.OrdinalIgnoreCase)
                                           && c.Port == _settings.SmtpPort);

        if (index < 0)
        {
            index = choices.FindIndex(c => c.IsOther);
        }

        _cmbMailPreset.SelectedIndex = index;
        ShowPresetEditable(index >= 0 && choices[index].IsOther);
    }

    /// <summary>A named provider fills in its own server and port; only Other lets them be typed.</summary>
    private void ShowPresetEditable(bool editable)
    {
        _txtSmtpHost.ReadOnly = !editable;
        _numSmtpPort.Enabled = editable;
    }

    private const string MailTestHint = "Send Test Email sends one message to the club's own address.";

    /// <summary>
    /// A mail server a restored backup brought in is the club's from now on
    /// (RestoredSettings): somebody has chosen it, typed it, or had a test
    /// email go through it, on this page with its name in front of them.
    /// </summary>
    private void ConfirmRestoredMail()
    {
        if (!RestoredSettings.IsHeld(RestoredSettings.Part.Mail))
        {
            return;
        }

        RestoredSettings.Confirm(RestoredSettings.Part.Mail);
        ShowMailResult(MailTestHint, ok: true);
    }

    /// <summary>
    /// Says so straight away when the port is one this can't use, rather
    /// than at the first send - or when the server came from a restored
    /// backup and nothing will be emailed until it is checked.
    /// </summary>
    private void RefreshMailProblem()
    {
        if (_settings.SmtpPort == 465 || RestoredSettings.IsHeld(RestoredSettings.Part.Mail))
        {
            ShowMailResult(PinMailer.ConfigurationProblem(_settings) ?? string.Empty, ok: false);
            _mailProblemShown = true;
        }
        else if (_mailProblemShown)
        {
            // Put right - the port changed, a provider picked - so the line
            // stops saying it is wrong (Bug hunt 17). Only a problem this
            // put there: what a test email said is left alone.
            ShowMailResult(MailTestHint, ok: true);
        }
    }

    /// <summary>Whether the result line is showing a problem with the settings (RefreshMailProblem) rather than the hint or what a test said.</summary>
    private bool _mailProblemShown;

    private void ShowMailResult(string message, bool ok)
    {
        _mailProblemShown = false;
        _lblMailResult.Text = message;
        _lblMailResult.ForeColor = ok ? AppTheme.TextDark : AppTheme.Primary;
    }

    private async Task SendTestMailAsync()
    {
        // Whatever is in the boxes, saved first: the test is of what will
        // actually be used.
        _settings.Save();

        _btnTestMail.Enabled = false;
        ShowMailResult("Sending a test email...", ok: true);

        try
        {
            var result = await PinMailer.SendTestAsync(_settings, _mailCancel.Token);

            // Through this server, by hand, with its name on the page: it is
            // the club's (RestoredSettings). Whether or not the page is still up.
            if (result.Ok)
            {
                RestoredSettings.Confirm(RestoredSettings.Part.Mail);
            }

            if (!IsDisposed)
            {
                ShowMailResult(result.Message, result.Ok);
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                ShowMailResult(ex.Message, ok: false);
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                _btnTestMail.Enabled = true;
            }
        }
    }

    /// <summary>
    /// Lets the race-day manager - normally reached over the venue's own
    /// Wi-Fi, from Local Screens - also answer through the relay, for
    /// someone setting up entries or posting a result from off the
    /// venue's network. One Manager PIN either way (AppSettings.ManagerPin,
    /// [JsonIgnore]d, so it is not shown or set here): duplicating it on
    /// this page would risk the two drifting apart, so this card only
    /// ever flips the relay path on or off and points at Local Screens
    /// for the PIN itself, the same way the Look card above points at
    /// Themes → Looks rather than keeping a second copy of the colours.
    /// </summary>
    private Section BuildManagerCard()
    {
        _chkManager.Text = "Let the race-day manager be reached through the relay";
        _chkManager.AutoSize = true;
        _textSize.Wear(_chkManager, () => AppTheme.BoldFont);
        _chkManager.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.ManagerViaRelay = _chkManager.Checked;
            _settings.Save();
            _publisher.SettingsChanged();
            RefreshManagerState();
        };

        // 17 Oct 2026, Bug Hunt 8, A17-3: it said the manager never starts a
        // round or throws a flag on the venue's Wi-Fi either - but there its
        // Race tab can (RaceDayService refuses race control only off Wi-Fi).
        var lblHint = HintLabel(
            "Local Screens' race-day manager (the day, entries, check-in, program and results), reachable off "
            + "the venue's Wi-Fi too. Through the relay it never starts a round or throws a flag, but on the "
            + "venue's Wi-Fi its Race tab can, so give the Manager PIN only to people you'd let run Race Control. "
            + "Needs a Manager PIN set on Local Screens.");

        // Two lines kept, like check-in's and personal PINs' own state lines
        // above - see KeepLines.
        KeepLines(_lblManagerState);

        _btnManagerPin.Text = "Set Manager PIN...";
        _btnManagerPin.Click += (_, _) => OpenPhoneWebRequested?.Invoke();

        _btnOpenManage.Text = "Open Manager Page";
        _btnOpenManage.Click += (_, _) =>
        {
            var page = ClubPageUrl();

            if (page.Length > 0)
            {
                OpenUrl(page + "/manage");
            }
        };

        // No "Manager Phones..." any more (26 Sep): a phone signs in with the
        // Manager PIN alone, and signs every request with a key made from it,
        // which this laptop checks - see RelayManagerDevices.VerifyPinSigned.
        // Nothing to enrol at the desk.

        var card = new Section("Race-day manager");

        card.Add(_chkManager, stretch: false, gapBelow: UiLayout.HintGap);
        card.Add(lblHint);
        card.Add(_lblManagerState, gapBelow: UiLayout.HintGap);
        card.Add(new ButtonBar().With(_btnManagerPin, _btnOpenManage), gapBelow: 0);

        return card;
    }

    /// <summary>One line on whether the manager is reachable through the relay, and if not, why.</summary>
    private void RefreshManagerState()
    {
        string text;
        var colour = AppTheme.TextMuted;

        if (!_settings.ManagerViaRelay)
        {
            text = "Off. The manager still works from Local Screens, over the venue's own Wi-Fi.";
        }
        else if (!_settings.RelayEnabled)
        {
            text = "Switched on, but publishing to the relay (above) is off - nothing reaches the manager page until it is on.";
            colour = AppTheme.Primary;
        }
        else if (!RaceDayService.IsUsableManagerPin(_settings.ManagerPin))
        {
            text = "No usable Manager PIN is set on Local Screens - the manager page refuses every request until one is.";
            colour = AppTheme.Danger;
        }
        else if (_publisher.ManagerProblem is { } problem)
        {
            text = problem;
            colour = AppTheme.Danger;
        }
        else if (_publisher.RelayManagerPinLockedUntilUtc is DateTime lockedUntil)
        {
            // The relay has stopped taking manager PIN guesses for this
            // club - publishing a changed one is what reopens it, the same
            // as check-in's own lock above.
            var local = lockedUntil.ToLocalTime();
            var when = local.Date == DateTime.Today ? AppTime.TimeOfDay(local) : AppTime.DateAndTime(local);
            text = $"Locked after many wrong guesses until {when} - change the Manager PIN on Local Screens to reopen it.";
            colour = AppTheme.Danger;
        }
        else
        {
            text = "Open with the Manager PIN. Every request is signed with it, and this laptop checks the signature "
                 + "before running anything.";
            colour = AppTheme.Accent;
        }

        if (_lblManagerState.Text != text)
        {
            _lblManagerState.Text = text;
        }

        _lblManagerState.ForeColor = colour;
        _btnOpenManage.Enabled = _settings.ManagerViaRelay && ClubPageUrl().Length > 0;
    }

    /// <summary>
    /// The address to hand out, and the three things people do with it.
    ///
    /// The club page rather than any one page on it: it is the one that
    /// says what is on now and links to everything else, so it is the one
    /// worth putting on a poster that stays up all season.
    /// </summary>
    private Section BuildShareCard()
    {
        // One line across the card, cut short with "..." when the address is
        // longer than that - an address has no spaces to wrap at. One line
        // of its face tall at the text size in use (SizeFields).
        _lnkClubPage.AutoSize = false;
        _lnkClubPage.AutoEllipsis = true;
        _lnkClubPage.LinkColor = AppTheme.Accent;
        _lnkClubPage.ActiveLinkColor = AppTheme.Accent;
        _lnkClubPage.VisitedLinkColor = AppTheme.Accent;
        _lnkClubPage.LinkClicked += (_, _) => OpenClubPage();

        _btnOpenPage.Text = "Open";
        _btnOpenPage.Click += (_, _) => OpenClubPage();

        _btnCopyPage.Text = "Copy Link";
        _btnCopyPage.Click += (_, _) => CopyClubPage();

        _btnPoster.Text = "Show Poster";
        _btnPoster.Click += (_, _) => ShowClubPoster();

        var lblShareHint = HintLabel(
            "The club's home page: what's on now, the schedule, results, standings, achievements and tracks, "
            + "from any network. The poster puts it in a QR code for the pits wall.");

        var card = new Section("Share");

        card.Add(_lnkClubPage);
        card.Add(new ButtonBar().With(_btnOpenPage, _btnCopyPage, _btnPoster), gapBelow: UiLayout.HintGap);
        card.Add(lblShareHint, gapBelow: 0);

        RefreshShare();

        return card;
    }

    /// <summary>The club page for what is SAVED, or empty. Never what is merely typed in the boxes - that may not work yet.</summary>
    private string ClubPageUrl()
    {
        var root = RelayPublisher.NormaliseBaseUrl(_settings.RelayBaseUrl);
        var club = _settings.RelayClub.Trim();

        return root.Length == 0 || club.Length == 0 ? string.Empty : $"{root}/c/{Uri.EscapeDataString(club)}";
    }

    private void RefreshShare()
    {
        var url = ClubPageUrl();
        var text = url.Length == 0 ? "Save a relay address and club id to get the club's page." : url;

        // Called every second from the status tick; only touched when it
        // actually changes, so the link is not repainted for nothing.
        if (_lnkClubPage.Text != text)
        {
            _lnkClubPage.Text = text;
            _lnkClubPage.LinkArea = url.Length == 0 ? new LinkArea(0, 0) : new LinkArea(0, text.Length);
        }

        _btnOpenPage.Enabled = url.Length > 0;
        _btnCopyPage.Enabled = url.Length > 0;
        _btnPoster.Enabled = url.Length > 0;
    }

    private void OpenClubPage()
    {
        var url = ClubPageUrl();

        if (url.Length > 0)
        {
            OpenUrl(url);
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowTestResult(new RelayProbe(false, $"Could not open a browser: {ex.Message}"));
        }
    }

    private void CopyClubPage()
    {
        var url = ClubPageUrl();

        if (url.Length == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(url);
            ShowTestResult(new RelayProbe(true, "Club page address copied."));
        }
        catch (Exception ex)
        {
            // Another program holding the clipboard open is enough to make
            // this throw, and it is no reason to lose the page.
            ShowTestResult(new RelayProbe(false, $"Could not copy: {ex.Message}"));
        }
    }

    private void ShowClubPoster()
    {
        var url = ClubPageUrl();

        if (url.Length == 0)
        {
            return;
        }

        // Non-modal, like the Wi-Fi posters: it is meant to sit open on a
        // second screen while the meeting carries on behind it.
        var poster = new JoinPosterForm(
            url,
            "FOLLOW THE RACING",
            "Scan with your phone camera. Live timing, schedule and results - and your own races under Me.");

        poster.Show(this);
    }

    /// <summary>
    /// Whether anything is getting through - a word or two in the bold face,
    /// then the detail - and the one way to take it all back off the relay.
    /// </summary>
    private Section BuildStatusCard()
    {
        _lblStatus.AutoSize = true;
        _textSize.Wear(_lblStatus, () => AppTheme.BoldFont);

        // Rewritten every second while publishing ("Last published 5 seconds
        // ago"), and it runs to a second line with a problem in it - so two
        // lines are kept (KeepLines), and Delete Everything Published under
        // it stays where it is while somebody reaches for it.
        _lblDetail.ForeColor = AppTheme.TextMuted;
        KeepLines(_lblDetail);

        _btnDelete.Text = "Delete Everything Published";
        _btnDelete.Click += async (_, _) => await DeleteAsync();

        var lblDeleteHint = HintLabel(
            "Clears everything this club has published from the relay - results, standings, the live race, the "
            + "schedule, achievements, tracks, the look and the check-in list. The laptop keeps its own copy. It "
            + "also turns \"Publish to the relay\" off; ticking it again sends everything again, including the "
            + "last day and a half of results.");

        var card = new Section("Status");

        card.Add(_lblStatus, gapBelow: UiLayout.HintGap);
        card.Add(_lblDetail);
        card.Add(new ButtonBar().With(_btnDelete), gapBelow: UiLayout.HintGap);
        card.Add(lblDeleteHint, gapBelow: 0);

        return card;
    }

    private void LoadFromSettings()
    {
        _loading = true;

        try
        {
            // Fields first, then the switches. The flag above makes the
            // order irrelevant, and the order makes the flag unnecessary -
            // belt and braces, because this one cost a silent evening of
            // nothing being published.
            _txtBaseUrl.Text = _settings.RelayBaseUrl;
            _txtClub.Text = _settings.RelayClub;
            _txtToken.Text = _settings.RelayToken;

            _chkEnabled.Checked = _settings.RelayEnabled;
            _chkRoster.Checked = _settings.RelayPublishRoster;
            _chkDriverNights.Checked = _settings.PublishDriverNightsToTheRelay;
            _chkAbbreviate.Checked = _settings.RelayAbbreviateNames;
            _chkFullNamesAloud.Checked = _settings.SayFullNamesWithOnlineVoices;
            _chkFullNamesAloud.Enabled = _settings.RelayAbbreviateNames;
            _chkDriverRequests.Checked = _settings.RelayDriverRequests;
            _chkResultQueries.Checked = _settings.RelayResultQueries;
            _numProtestMinutes.Value = Math.Clamp(_settings.ProtestMinutesFor(null), (int)_numProtestMinutes.Minimum, (int)_numProtestMinutes.Maximum);
            _chkNightEmail.Checked = _settings.NightEmailOffered;
            _chkNightEmailAuto.Checked = _settings.NightEmailAutomatic;
            _chkRelayCaptions.Checked = _settings.RelayCaptions;

            _txtTagline.Text = _settings.RelayTagline;
            _txtClubAlias.Text = _settings.RelayClubAlias;
            _txtShortName.Text = _settings.RelayShortName;
            _txtNewcomerWhere.Text = _settings.RelayNewcomerWhere;
            _txtNewcomerWhen.Text = _settings.RelayNewcomerWhen;
            _txtNewcomerJoin.Text = _settings.RelayNewcomerJoin;
            ShowGuide(_settings.RelayFirstNightGuide);
            _txtUpdates.Text = _settings.RelayClubUpdates;
            RefreshUpdatesState();

            foreach (var (key, box) in _socialBoxes)
            {
                box.Text = _settings.RelaySocialLinks.TryGetValue(key, out var link) ? link : string.Empty;
            }

            RefreshSocialProblem();
            _chkCheckIn.Checked = _settings.RelayCheckInEnabled;
            _txtCheckInPin.Text = _settings.RelayCheckInPin;
            _pinRejected = null;
            _chkCheckInPhotos.Checked = _settings.RelayCheckInPhotos;
            _chkEmailSignInLink.Checked = _settings.RelayEmailSignInLink;

            _chkRacerPins.Checked = _settings.RelayRacerPinsEnabled;
            SelectRotation(_settings.RacerPinRotation);
            _chkEmailPinsAuto.Checked = _settings.RacerPinsEmailAutomatically;

            _txtSmtpHost.Text = _settings.SmtpHost;
            _numSmtpPort.Value = Math.Clamp(_settings.SmtpPort, (int)_numSmtpPort.Minimum, (int)_numSmtpPort.Maximum);
            _txtSmtpUser.Text = _settings.SmtpUser;
            _txtSmtpPassword.Text = _settings.SmtpPassword;
            _txtSmtpFromName.Text = _settings.SmtpFromName;
            SelectPreset();

            // A server held back after a restore says so as the page opens,
            // not only at the first send.
            RefreshMailProblem();

            _chkManager.Checked = _settings.ManagerViaRelay;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Writes the boxes to settings. False when the club id is one the relay could never hold - saved anyway, and said so.</summary>
    private bool SaveFields(bool confirmRestored = true)
    {
        if (_loading)
        {
            return false;
        }

        // Whether this save takes a restored backup's address as checked
        // (RestoredSettings): only with the address in front of whoever
        // pressed - its box, or in Simple mode, which hides the box, the
        // Status card naming it (Bug hunt 17).
        var confirming = confirmRestored
                         && RestoredSettings.IsHeld(RestoredSettings.Part.PublishRelay)
                         && (SimpleMode.ShowAll || _restoredAddressNamed);

        // Lower case, as the relay keeps it: "Acme-RC" typed here is
        // plainly acme-rc, and the relay answers anything else with a 404.
        var club = _txtClub.Text.Trim().ToLowerInvariant();

        var oldBaseUrl = _settings.RelayBaseUrl;
        var oldClub = _settings.RelayClub;
        var oldToken = _settings.RelayToken;

        _settings.RelayEnabled = _chkEnabled.Checked;
        _settings.RelayBaseUrl = RelayPublisher.NormaliseBaseUrl(_txtBaseUrl.Text);
        _settings.RelayClub = club;
        _settings.RelayToken = _txtToken.Text.Trim();

        // The set "that last tested clean" came out of the same backup. Only
        // the address being saved has been checked, so a last-good set for
        // any other address is forgotten rather than offered afterwards as
        // this laptop's own (Bug hunt 17). The next clean test remembers anew.
        if (confirming
            && !string.Equals(RelayPublisher.NormaliseBaseUrl(_settings.RelayLastGoodBaseUrl), _settings.RelayBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            _settings.RelayLastGoodBaseUrl = string.Empty;
            _settings.RelayLastGoodClub = string.Empty;
            _settings.RelayLastGoodToken = string.Empty;
            _settings.RelayLastGoodUtc = null;
        }

        _settings.Save();

        // Show the tidied address and club back, so "localhost:8787"
        // visibly becomes what will actually be called.
        _txtBaseUrl.Text = _settings.RelayBaseUrl;

        if (_txtClub.Text != club)
        {
            _txtClub.Text = club;
        }

        // Somebody at this page pressed Save or Test Connection with the
        // address in front of them (see "confirming" above) - so an address
        // a restored backup brought in is theirs from now on
        // (RestoredSettings). Before SettingsChanged, which is what reads
        // it again.
        if (confirming)
        {
            RestoredSettings.Confirm(RestoredSettings.Part.PublishRelay);
        }

        _publisher.SettingsChanged();
        RefreshStatus();
        RefreshLastGood();

        // Only while the voice has no relay of its own: with the voice relay
        // boxes filled in, the voice is speaking somewhere else and this
        // relay's details are not what it was resting over.
        var relayChanged = !string.Equals(oldBaseUrl, _settings.RelayBaseUrl, StringComparison.Ordinal)
                           || !string.Equals(oldClub, _settings.RelayClub, StringComparison.Ordinal)
                           || !string.Equals(oldToken, _settings.RelayToken, StringComparison.Ordinal);

        // ...except where a voice relay of its own still borrows from these
        // boxes: a blank voice club falls back to this club, and a blank
        // voice token to this token (see HeatCallerVoiceClient.Target), so
        // changing either here changes what the voice is asking with.
        var voiceBorrows = RelayPublisher.NormaliseBaseUrl(_settings.VoiceRelayBaseUrl).Length == 0
                           || string.IsNullOrWhiteSpace(_settings.VoiceRelayClub)
                           || string.IsNullOrWhiteSpace(_settings.VoiceRelayToken);

        if (relayChanged && voiceBorrows)
        {
            VoiceRelayChanged?.Invoke();
        }

        // Kept rather than refused, so the box and settings agree - but the
        // publisher treats it as not configured, and the reason is shown
        // where the operator is looking.
        if (club.Length > 0 && !RelayPublisher.IsValidClubId(club))
        {
            ShowTestResult(new RelayProbe(false, RelayPublisher.InvalidClubMessage));
            return false;
        }

        return true;
    }

    /// <summary>
    /// Records the settings a test has just proved.
    ///
    /// Kept as a separate set rather than by trusting the live ones,
    /// because the live ones are what publishing uses and may be halfway
    /// through being edited. This is the copy that is known to have
    /// worked, and the only one worth going back to.
    /// </summary>
    private void RememberLastGood()
    {
        _settings.RelayLastGoodBaseUrl = _settings.RelayBaseUrl;
        _settings.RelayLastGoodClub = _settings.RelayClub;
        _settings.RelayLastGoodToken = _settings.RelayToken;
        _settings.RelayLastGoodUtc = DateTime.UtcNow;
        _settings.Save();

        RefreshLastGood();
    }

    private void RestoreLastGood()
    {
        // Not while a restored backup's settings are unchecked: the set that
        // "last worked" came out of the same file (Bug hunt 17).
        if (!HasLastGood() || RestoredSettings.IsHeld(RestoredSettings.Part.PublishRelay))
        {
            return;
        }

        _txtBaseUrl.Text = _settings.RelayLastGoodBaseUrl;
        _txtClub.Text = _settings.RelayLastGoodClub;
        _txtToken.Text = _settings.RelayLastGoodToken;

        SaveFields();
        ShowTestResult(new RelayProbe(true, "Put back the settings that last worked."));
    }

    private bool HasLastGood() =>
        _settings.RelayLastGoodBaseUrl.Length > 0
        && _settings.RelayLastGoodClub.Length > 0
        && _settings.RelayLastGoodToken.Length > 0;

    /// <summary>Whether what is in the boxes right now IS the set that last worked.</summary>
    private bool MatchesLastGood() =>
        HasLastGood()
        && RelayPublisher.NormaliseBaseUrl(_txtBaseUrl.Text) == _settings.RelayLastGoodBaseUrl
        && _txtClub.Text.Trim() == _settings.RelayLastGoodClub
        && _txtToken.Text.Trim() == _settings.RelayLastGoodToken;

    private void RefreshLastGood()
    {
        if (!HasLastGood())
        {
            _lblLastGood.Text = "Nothing has tested clean yet. A successful test remembers what it used.";
            _btnRestore.Enabled = false;
            return;
        }

        // Held after a restore: the last-good set is from the backup too, so
        // it is not vouched for or offered until the settings are checked.
        if (RestoredSettings.IsHeld(RestoredSettings.Part.PublishRelay))
        {
            _lblLastGood.Text = RestoredSettings.LastGoodHeldMessage;
            _btnRestore.Enabled = false;
            return;
        }

        var when = _settings.RelayLastGoodUtc?.ToLocalTime();
        var stamp = when is null ? string.Empty : $" ({AppTime.DateAndTime(when.Value)})";

        if (MatchesLastGood())
        {
            _lblLastGood.Text = $"These are the settings that last tested clean{stamp}.";
            _btnRestore.Enabled = false;
            return;
        }

        _lblLastGood.Text = $"Changed since the last clean test{stamp}: "
                          + $"{_settings.RelayLastGoodClub} at {_settings.RelayLastGoodBaseUrl}.";

        _btnRestore.Enabled = true;
    }

    private async Task TestAsync()
    {
        _btnTest.Enabled = false;
        ShowTestResult(new RelayProbe(true, "Checking..."));

        // What is being tested, taken before the wait. The test takes a few
        // seconds, and anything typed in the boxes meanwhile is untested -
        // saving it as "proved" was how a half-typed token became the one
        // on record as last working.
        var testedUrl = _txtBaseUrl.Text;
        var testedClub = _txtClub.Text;
        var testedToken = _txtToken.Text;

        try
        {
            var probe = await _publisher.TestAsync(testedUrl, testedClub, testedToken);

            var unchanged = _txtBaseUrl.Text == testedUrl
                            && _txtClub.Text == testedClub
                            && _txtToken.Text == testedToken;

            if (probe.Ok && !unchanged)
            {
                ShowTestResult(new RelayProbe(false, probe.Message + " Not saved - the boxes were changed during the test. Test again."));
            }
            else if (probe.Ok)
            {
                // The box ends up showing what was PROVED, not what was
                // pasted. A live-page link is taken back to its relay by
                // NormaliseBaseUrl before the test; leaving the page's
                // address sitting in the field afterwards would leave the
                // operator believing the page address is the setting.
                var proved = RelayPublisher.NormaliseBaseUrl(_txtBaseUrl.Text);

                if (proved.Length > 0 && _txtBaseUrl.Text.Trim() != proved)
                {
                    _txtBaseUrl.Text = proved;
                }

                // Saved the moment they are proved, rather than waiting
                // for Save to be pressed. Losing a token that has just
                // been shown to work, because the page was navigated away
                // from, would mean minting another one for no reason.
                if (SaveFields())
                {
                    RememberLastGood();
                    ShowTestResult(new RelayProbe(true, probe.Message + " Saved."));
                }
            }
            else
            {
                ShowTestResult(probe);
            }
        }
        catch (Exception ex)
        {
            ShowTestResult(new RelayProbe(false, ex.Message));
        }
        finally
        {
            _btnTest.Enabled = true;
        }
    }

    private async Task DeleteAsync()
    {
        var answer = AppDialog.Show(
            this,
            "Delete everything this club has published to the relay?\n\n"
            + "Results, standings, the live race, the schedule and everything else will disappear from "
            + "every public page, and online check-in stops. Nothing on this laptop is touched.\n\n"
            + "Publishing is switched off so none of it comes back on its own. Tick \"Publish to the "
            + "relay\" again when you want it back - that sends everything again. The Undo button can't "
            + "reverse this: the relay deletes it straight away.",
            "Delete published data",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.Yes)
        {
            return;
        }

        _btnDelete.Enabled = false;

        // Publishing goes off BEFORE the delete, not after. The laptop
        // keeps publishing on its own - the schedule every few seconds,
        // the look, badges, tracks and check-in list again every ten
        // minutes so a copy the relay lost is put back - and with the
        // switch still on, that refresh filled the relay straight back up
        // after "Delete everything", against what this page promised.
        // Off first also means nothing new is queued while the delete is
        // on its way. Turning it back on is the one way everything is
        // sent again, which is a choice the club makes, and a new
        // publishing round starts from scratch: the change caches on the
        // main window are thrown away when the switch changes (see
        // RelayPublisher.Generation), so an old copy cannot slip back.
        var wasPublishing = _settings.RelayEnabled;
        SetPublishing(false);

        var deleted = false;

        try
        {
            // And anything already on its way stopped and waited out: a live
            // frame that left just before the switch went off, landing just
            // after the delete, put the live page straight back.
            await _publisher.QuiesceAsync();

            var probe = await _publisher.DeletePublishedAsync();
            deleted = probe.Ok;

            ShowTestResult(probe.Ok
                ? new RelayProbe(true, probe.Message + " Publishing is now off - tick \"Publish to the relay\" to start again.")
                : probe);
        }
        catch (Exception ex)
        {
            ShowTestResult(new RelayProbe(false, ex.Message));
        }
        finally
        {
            // Nothing was deleted, so there is nothing to keep off the
            // relay: put the switch back the way it was.
            if (!deleted && wasPublishing)
            {
                SetPublishing(true);
            }

            _btnDelete.Enabled = true;
            RefreshStatus();
        }
    }

    /// <summary>
    /// Turns publishing on or off without going through the tick box's own
    /// handler, which saves every field on the page - including anything
    /// typed into the address, club or token boxes and not yet tested.
    /// Only the switch changes here.
    /// </summary>
    private void SetPublishing(bool on)
    {
        _settings.RelayEnabled = on;
        _settings.Save();
        _publisher.SettingsChanged();

        _loading = true;

        try
        {
            _chkEnabled.Checked = on;
        }
        finally
        {
            _loading = false;
        }
    }

    private void ShowTestResult(RelayProbe probe)
    {
        _lblTestResult.Text = probe.Message;
        _lblTestResult.ForeColor = probe.Ok ? AppTheme.TextDark : AppTheme.Primary;
    }

    private void RefreshStatus()
    {
        // Whether anybody can listen along depends on the club website being
        // switched on, which is the Relay card's first box.
        _roomAndWeb.DescribeListening();

        RefreshShare();
        RefreshCheckInState();
        RefreshPinsState();
        RefreshDriverExtras();
        RefreshManagerState();

        var status = _publisher.Status;

        // Before "Not configured", which is what the publisher reports while
        // it holds back - and which would send somebody looking for a
        // missing token that is sitting right there. And before "Not
        // publishing" (Bug hunt 17): with publishing off the hold was never
        // said, and Save or the tick then took the address as checked.
        if (RestoredSettings.IsHeld(RestoredSettings.Part.PublishRelay))
        {
            _lblStatus.Text = "Check these settings";
            _lblStatus.ForeColor = AppTheme.Primary;

            // Simple mode hides the address row, so there is no "address
            // above": the address is named here instead, and Save only
            // takes it as checked once it has been (SaveFields).
            var address = RelayPublisher.NormaliseBaseUrl(_txtBaseUrl.Text);

            _lblDetail.Text = SimpleMode.ShowAll
                ? RestoredSettings.PublishRelayMessage
                : RestoredSettings.PublishRelayMessageNaming(address);

            _restoredAddressNamed = !SimpleMode.ShowAll;
            return;
        }

        _restoredAddressNamed = false;

        if (!status.Enabled)
        {
            _lblStatus.Text = "Not publishing";
            _lblStatus.ForeColor = AppTheme.TextMuted;
            _lblDetail.Text = "Tick \"Publish to the relay\" above once the address, club and token are set.";
            return;
        }

        if (!status.Configured)
        {
            _lblStatus.Text = "Not configured";
            _lblStatus.ForeColor = AppTheme.Primary;
            _lblDetail.Text = "An address, a club id and a publish token are all needed before anything can be sent.";
            return;
        }

        var signupProblem = _publisher.SignupProblem;

        if (status.Online && signupProblem is not null)
        {
            // Results are getting through but sign-up is not - a green
            // "Publishing" here is how invites went out to a sign-up page
            // that had never heard of their codes. Shown in the colour the
            // page uses for anything that needs doing.
            _lblStatus.Text = "Publishing - sign-up needs attention";
            _lblStatus.ForeColor = AppTheme.Primary;
        }
        else if (status.Online)
        {
            _lblStatus.Text = "Publishing";
            _lblStatus.ForeColor = AppTheme.Accent;
        }
        else
        {
            _lblStatus.Text = status.LastSuccessUtc is null ? "Waiting for the first publish" : "Offline";
            _lblStatus.ForeColor = AppTheme.Primary;
        }

        var detail = DescribeStatus(status);

        // A refused signup document is already in the status's own error;
        // a problem collecting sign-ups is not, so it is added here.
        if (signupProblem is not null && !detail.Contains(signupProblem, StringComparison.Ordinal))
        {
            detail += "  " + signupProblem;
        }

        _lblDetail.Text = detail;
    }

    /// <summary>The one sentence a race director actually needs: is it getting through, and how far behind is it.</summary>
    public static string DescribeStatus(RelayStatus status)
    {
        var parts = new List<string>();

        parts.Add(status.LastSuccessUtc is DateTime last
            ? $"Last published {Ago(DateTime.UtcNow - last)}."
            : "Nothing has been published yet.");

        if (status.Queued > 0)
        {
            parts.Add(status.Queued == 1 ? "1 waiting to go." : $"{status.Queued} waiting to go.");
        }

        if (status.Dropped > 0)
        {
            parts.Add(status.Dropped == 1 ? "1 could not be published." : $"{status.Dropped} could not be published.");
        }

        if (status.LastError is not null)
        {
            parts.Add(status.LastError);
        }

        return string.Join("  ", parts);
    }

    private static string Ago(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        if (span.TotalSeconds < 5)
        {
            return "just now";
        }

        // Each tier runs to 90 of its unit, so the tier above starts at 1
        // of its own - "1 minutes ago" for anything from 90 to 119
        // seconds, and "1 hours ago" from 90 to 119 minutes. The "just
        // now" guard hid it at the bottom tier only.
        if (span.TotalSeconds < 90)
        {
            return Plural.Of((int)span.TotalSeconds, "second") + " ago";
        }

        if (span.TotalMinutes < 90)
        {
            return Plural.Of((int)span.TotalMinutes, "minute") + " ago";
        }

        return Plural.Of((int)span.TotalHours, "hour") + " ago";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tick.Stop();
            _tick.Dispose();

            // An email send still running stops at its current message;
            // what it had sent is simply not marked, and goes again. Not
            // disposed as well: the send may still be holding its token,
            // and a source with no timer owns nothing that needs freeing.
            _mailCancel.Cancel();
        }

        base.Dispose(disposing);
    }
}
