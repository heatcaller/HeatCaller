namespace HeatCaller;

/// <summary>
/// Backup and restore for everything the app persists - driver profiles,
/// race classes/rounds, session history, track layouts, and all settings.
/// See BackupService for what a "backup" actually is (a zip of the whole
/// app data folder).
/// </summary>
public sealed partial class BackupRestoreControl : UserControl
{
    private readonly AppSettings _settings;

    /// <summary>
    /// Raised once a restore has put files back - all of them, or only some
    /// - so the host (MainForm) can restart the app immediately. There's no
    /// "closing the dialog" moment to hang this off now that this lives in
    /// a tab. Raised for a partial restore too: by then the files on disk
    /// no longer match what this process holds, and carrying on would save
    /// the old data over whatever did get restored.
    /// </summary>
    public event Action? RestoreCompleted;

    /// <summary>
    /// Raised just before a restore starts replacing files, on the UI
    /// thread, for the host to close every file in the data folder it holds
    /// open: the music track playing, the ambience loops, the decoder's raw
    /// capture log. Windows won't replace a file that is open, so without
    /// this the restore stopped partway through. Only raised once the
    /// backup has been checked and unpacked - a restore refused before that
    /// leaves the music playing - and the app always restarts afterwards,
    /// so nothing needs starting again.
    /// </summary>
    public event Action? ReleaseFilesForRestore;

    /// <summary>
    /// Raised by Save Support Info. The host (MainForm) does the work - see
    /// SupportInfo - because it needs Driver Hub, which this page
    /// doesn't have, to know whose names to take out.
    /// </summary>
    public event Action? SupportInfoRequested;

    /// <summary>
    /// Asked before a restore starts: true while a race (or practice) is
    /// under way. Set by the host (MainForm), which knows the race state;
    /// null means "never", which is what this page assumed before.
    ///
    /// A restore ends by restarting the app, which would end the race - and
    /// its safety backup zips the whole data folder on this thread first,
    /// which stalls timing while it runs.
    /// </summary>
    public Func<bool>? IsRaceUnderWay { get; set; }

    /// <summary>
    /// Raised by the Settings mode card's button (1 Nov 2026): the host
    /// (MainForm) turns Advanced mode on behind its warning, or goes back to
    /// Simple, and calls <see cref="ShowMode"/> with the result.
    /// </summary>
    public event Action? SettingsModeToggleRequested;

    /// <summary>Raised by Restore Default Settings (round 4): the host asks, saves a copy, resets and restarts - see MainForm.RestoreDefaultSettings.</summary>
    public event Action? RestoreDefaultsRequested;

    /// <summary>Raised by Put Back Earlier Settings (round 4): the host lists the copies - see MainForm.PutBackEarlierSettings.</summary>
    public event Action? PutBackSettingsRequested;

    private readonly Label _lblMode = new();
    private readonly Button _btnMode = new();

    /// <summary>"Ask for the manager PIN to turn on Advanced mode" (round 5) - AppSettings.AdvancedNeedsManagerPin.</summary>
    private readonly CheckBox _chkAdvancedPin = new ThemedCheckBox();

    private readonly Label _lblAdvancedPinHint = new();

    private readonly Button _btnBackupNow = new();

    private readonly Label _lblLastBackup = new();
    private readonly CheckBox _chkAutoBackupEnabled = new ThemedCheckBox();
    private readonly NumericUpDown _numIntervalDays = new();
    private readonly NumericUpDown _numKeepCount = new();
    private readonly TextBox _txtFolder = new();
    private readonly ListBox _lstBackups = new();
    private readonly Label _lblStatus = new();
    private readonly Button _btnRestoreSelected = new();
    private readonly Button _btnDeleteSelected = new();
    private readonly Dictionary<CloudService, CheckBox> _cloudChecks = new();

    /// <summary>The cloud backup light and the two or three words beside it - see RefreshCloudStatus.</summary>
    private readonly Panel _pnlCloudLight = new();

    private readonly Label _lblCloudLight = new();

    /// <summary>What is wrong and what to do about it, under the light.</summary>
    private readonly Label _lblCloudDetail = new();

    private readonly Label _lblCloudStatus = new();
    private readonly Button _btnChangeFolder = new();
    private readonly TextBox _txtBackupPassword = new();
    private readonly TextBox _txtBackupPasswordConfirm = new();

    /// <summary>
    /// What the password as set now means. As tall as the longer of the two
    /// things it says (SteadyLabel), so the hint under it never has to move
    /// when the password is set or removed.
    /// </summary>
    private readonly SteadyLabel _lblPasswordStatus = new() { Wordings = new[] { PasswordSetText, NoPasswordText } };

    private const string PasswordSetText = "Keys and tokens travel with backups, locked with this password.";

    private const string NoPasswordText =
        "No backup password: backups don't carry keys - after restoring on another PC you'd re-enter them and reprint QR check-ins.";

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The backups list's monospaced face, made for the text size in use.</summary>
    private Font? _listFont;

    /// <summary>
    /// The shortest backup password accepted. It is the only thing between
    /// a backup file and every key in it, and anyone holding the file can
    /// try passwords at leisure.
    /// </summary>
    private const int MinPasswordLength = 8;

    /// <summary>The app's undo, for the password and deleting a backup. Null when the host didn't pass one.</summary>
    private readonly UndoService? _undo;

    public BackupRestoreControl(AppSettings settings, UndoService? undo = null)
    {
        _settings = settings;
        _undo = undo;

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize.
        //
        // No MinimumSize, and no scrolling of its own: the 560x560 floor was
        // honoured over the size the page was given. A short window scrolls
        // the page instead (PageStack).
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshBackupList();
        RefreshLastBackupLabel();
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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        // After the list that wears it is gone.
        if (disposing)
        {
            _listFont?.Dispose();
            _listFont = null;
        }
    }

    /// <summary>
    /// The fewest rows the backups list ever shows, so an empty folder
    /// still reads as a list rather than as a missing control.
    /// </summary>
    private const int MinBackupRows = 4;

    /// <summary>
    /// The most rows it shows before scrolling within itself.
    ///
    /// Counts of rows rather than heights in pixels, so the list shows the
    /// same number of backups at every text size.
    /// </summary>
    private const int MaxBackupRows = 12;

    /// <summary>The narrowest and the widest the folder box gets, at the normal text size - see SizeFolderBox.</summary>
    private const int FolderBoxLeastWidth = 260;
    private const int FolderBoxMostWidth = 560;

    /// <summary>
    /// Five cards down the page: making and restoring a backup by hand; the
    /// automatic backups - how often, how many are kept, the folder, the
    /// cloud folders they are copied to and what each of those is doing, and
    /// when the last was made; the backup password; getting help; and the
    /// backups there are, the list as tall as the backups in it, with the
    /// buttons that act on the one selected and what the last of them did.
    /// On the layout pieces (LayoutPieces.cs), so nothing is placed by hand;
    /// the page scrolls when it is taller than the window (PageStack).
    ///
    /// It used to be placed in pixels: every group box 528px wide whatever
    /// the window, the rows in each measured from the one before in the
    /// font of the day and the box sized to them once, at build, the cloud
    /// status given six lines of that width, and a 560x560 floor the page
    /// kept however small its window. Every font was set once, so nothing
    /// followed the text size.
    /// </summary>
    private void BuildLayout()
    {
        var page = new PageStack();

        page.Add(BuildManualCard());
        page.Add(BuildAutoCard());
        page.Add(BuildPasswordCard());
        page.Add(BuildStandbyCard());
        page.Add(BuildModeCard());
        page.Add(BuildSupportCard());
        page.Add(BuildUpdatesCard());

        // The list stays the last card on the page: it grows and shrinks
        // with the backups it holds (SizeList), and nothing sits below it
        // to be pushed about.
        page.Add(BuildListCard(), gapBelow: 0);

        Controls.Add(page);

        // Once the fields are on the page and wear its face - what they are
        // measured in.
        _textSize.Keep(SizeFields);
    }

    /// <summary>
    /// SETTINGS MODE (1 Nov 2026, Simple and Advanced mode, round 1): where
    /// Advanced mode is turned on - here rather than a switch in the
    /// sidebar's footer, which Asher found too much for something a
    /// volunteer should not stumble on. What mode this PC is in, and one
    /// button for the other. The host does the asking (SettingsModeToggleRequested).
    /// </summary>
    private Section BuildModeCard()
    {
        _lblMode.AutoSize = true;

        _btnMode.Click += (_, _) => SettingsModeToggleRequested?.Invoke();

        var lblHint = new Label
        {
            Text = SettingsModes.CardHint,
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblHint, () => AppTheme.SmallFont);

        // RESTORE DEFAULT SETTINGS (round 4), in both modes: the way a Simple
        // user undoes settings changed in Advanced, and the copies it saves.
        var btnRestore = new Button { Text = "Restore Default Settings..." };
        btnRestore.Click += (_, _) => RestoreDefaultsRequested?.Invoke();

        var btnPutBack = new Button { Text = "Put Back Earlier Settings..." };
        btnPutBack.Click += (_, _) => PutBackSettingsRequested?.Invoke();

        var lblRestoreHint = new Label
        {
            Text = "Restore Default Settings puts every setting Simple mode hides back to how HeatCaller ships, after saving a "
                   + "copy. Your drivers, classes, tracks, results and keys are never touched.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblRestoreHint, () => AppTheme.SmallFont);

        // ASKING FOR THE MANAGER PIN (round 5), for a PC volunteers run: in
        // Advanced mode only, so nobody in Simple can untick it - and greyed
        // until the club has a manager PIN to ask for.
        _chkAdvancedPin.Text = "Ask for the manager PIN to turn on Advanced mode";
        _chkAdvancedPin.AutoSize = true;
        _chkAdvancedPin.Checked = _settings.AdvancedNeedsManagerPin;
        _chkAdvancedPin.CheckedChanged += (_, _) =>
        {
            if (_settings.AdvancedNeedsManagerPin != _chkAdvancedPin.Checked)
            {
                _settings.AdvancedNeedsManagerPin = _chkAdvancedPin.Checked;
                _settings.Save();
            }
        };

        _lblAdvancedPinHint.AutoSize = true;
        _lblAdvancedPinHint.ForeColor = AppTheme.TextMuted;
        _textSize.Wear(_lblAdvancedPinHint, () => AppTheme.SmallFont);

        var card = new Section("Settings mode");

        card.Add(_lblMode, gapBelow: UiLayout.LabelGap);
        card.Add(new ButtonBar().With(_btnMode));
        card.Add(lblHint, gapBelow: UiLayout.RowGap);
        card.Add(SimpleMode.Hide(_chkAdvancedPin), stretch: false, gapBelow: 0);
        card.Add(SimpleMode.Hide(_lblAdvancedPinHint), gapBelow: UiLayout.SectionGap);

        // The manager PIN is set on Local Screens, so it is read again each
        // time this page is shown.
        VisibleChanged += (_, _) =>
        {
            if (Visible)
            {
                ShowAdvancedPin();
            }
        };
        card.Add(new ButtonBar().With(btnRestore, btnPutBack));
        card.Add(lblRestoreHint, gapBelow: 0);

        ShowMode(_settings.SettingsMode);

        return card;
    }

    /// <summary>The Settings mode card brought up to date with the mode this PC is in. UI thread.</summary>
    public void ShowMode(SettingsMode mode)
    {
        // The bar sizes the button to its new words by itself.
        _lblMode.Text = SettingsModes.CardStatus(mode);
        _btnMode.Text = SettingsModes.CardButton(mode);

        ShowAdvancedPin();
    }

    /// <summary>The manager PIN tick: greyed, and the hint says why, until the club has a manager PIN to ask for.</summary>
    private void ShowAdvancedPin()
    {
        var hasPin = RaceDayService.IsUsableManagerPin(_settings.ManagerPin);

        _chkAdvancedPin.Enabled = hasPin;
        _lblAdvancedPinHint.Text = hasPin
            ? "For a PC volunteers run: Advanced mode then needs the PIN the race-day manager's phone page asks for."
            : "Set a manager PIN first, on Local Screens in the Race-day manager card (six digits or more).";
    }

    /// <summary>
    /// Save Support Info: one zip of the logs and crash log, with driver
    /// names and emails taken out, for a club to email when something went
    /// wrong. On this page because it is where the app's files are looked
    /// after; the crash dialog and the console's menu point here too.
    /// </summary>
    private Section BuildSupportCard()
    {
        var btnSupport = new Button { Text = "Save Support Info..." };
        btnSupport.Click += (_, _) => SupportInfoRequested?.Invoke();

        var btnAbout = new Button { Text = "About HeatCaller..." };
        btnAbout.Click += (_, _) => AboutForm.ShowFor(FindForm());

        // The license and privacy notice the club accepted before
        // HeatCaller first opened, to read again - see TermsForm.
        var btnTerms = new Button { Text = "License and Privacy..." };
        btnTerms.Click += (_, _) => TermsForm.ShowFor(FindForm());

        // The address the support file is for. A link rather than painted
        // text so it opens the club's email program, with Copy Address
        // beside it because a track PC often has no email program set up at
        // all and the address is then wanted on a phone - the same pairing
        // the club page gets on Club Website.
        var lnkEmail = new LinkLabel
        {
            Text = AppSupport.EmailSentence,
            AutoSize = true,
            LinkArea = new LinkArea(
                AppSupport.EmailSentence.IndexOf(AppSupport.Email, StringComparison.Ordinal),
                AppSupport.Email.Length),
            LinkColor = AppTheme.Accent,
            ActiveLinkColor = AppTheme.Accent,
            VisitedLinkColor = AppTheme.Accent,
        };

        lnkEmail.LinkClicked += (_, _) => AppSupport.Open(AppSupport.MailTo);

        var btnCopyEmail = new Button { Text = "Copy Address" };
        btnCopyEmail.Click += (_, _) => AppSupport.CopyEmail();

        var lblHint = new Label
        {
            Text = "Saves one file to attach to an email when something went wrong: the console from the last few runs, the crash log, "
                   + "the version and the settings that matter. Driver names, email addresses and your Windows user name are replaced "
                   + "first, and no keys, tokens, PINs or passwords go in.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblHint, () => AppTheme.SmallFont);

        var card = new Section("Getting help");

        card.Add(new ButtonBar().With(btnSupport, btnAbout, btnTerms));
        card.Add(lblHint);
        card.Add(FieldGrid.Inline(lnkEmail, btnCopyEmail), gapBelow: 0);

        return card;
    }

    /// <summary>
    /// UPDATES (15 Nov 2026): the version this PC is on, Check for Updates,
    /// what the last check found, and the switch for the quiet checks
    /// (AppSettings.UpdateChecksOn, which had no switch on screen before).
    /// The checking, downloading and asking are AppUpdates' - the same
    /// question, the same never-mid-meeting rule and the same backup first
    /// as the quiet check; the button only makes it happen now and say what
    /// it found.
    /// </summary>
    private Section BuildUpdatesCard()
    {
        var lblVersion = new Label
        {
            Text = $"This PC is on HeatCaller {AppVersion.Number}.",
            AutoSize = true,
        };

        var btnCheck = new Button { Text = "Check for Updates" };

        var lblResult = new Label
        {
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblResult, () => AppTheme.SmallFont);

        btnCheck.Click += async (_, _) =>
        {
            btnCheck.Enabled = false;
            lblResult.Text = "Checking...";

            string result;

            try
            {
                result = await AppUpdates.CheckNowAsync(_settings);
            }
            catch (Exception ex)
            {
                result = $"The check didn't finish ({ex.GetType().Name}). Try again in a minute.";
            }

            // The page can have gone while the check was out - closing, or a
            // restore restarting the app.
            if (IsDisposed || btnCheck.IsDisposed)
            {
                return;
            }

            lblResult.Text = result;
            btnCheck.Enabled = true;
        };

        var chkAuto = new ThemedCheckBox
        {
            Text = "Look for updates by itself every few hours",
            AutoSize = true,
            Checked = _settings.UpdateChecksOn,
        };

        chkAuto.CheckedChanged += (_, _) =>
        {
            if (_settings.UpdateChecksOn != chkAuto.Checked)
            {
                _settings.UpdateChecksOn = chkAuto.Checked;
                _settings.Save();
            }
        };

        var lblHint = new Label
        {
            Text = "An update is only ever offered when no race day is open and no race is on, a backup is taken first, "
                   + "and \"After tonight\" installs it the next time HeatCaller is opened.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblHint, () => AppTheme.SmallFont);

        var card = new Section("Updates");

        card.Add(lblVersion, gapBelow: UiLayout.LabelGap);
        card.Add(new ButtonBar().With(btnCheck));
        card.Add(lblResult, gapBelow: UiLayout.RowGap);
        card.Add(chkAuto, stretch: false, gapBelow: 0);
        card.Add(lblHint, gapBelow: 0);

        return card;
    }

    /// <summary>Back Up Now, Restore from File and Open Backup Folder, each as wide as its caption, and what a backup holds.</summary>
    private Section BuildManualCard()
    {
        // A field, so it can be switched off while its backup is being made.
        _btnBackupNow.Text = "Back Up Now";
        _btnBackupNow.Click += BtnBackupNow_Click;

        var btnRestore = new Button { Text = "Restore from File..." };
        btnRestore.Click += BtnRestore_Click;

        var btnOpenFolder = new Button { Text = "Open Backup Folder" };
        btnOpenFolder.Click += (_, _) => OpenFolderInExplorer(CurrentFolder());

        var lblManualHint = new Label
        {
            Text = "Includes driver profiles, race classes, session history, track layouts, and all app settings.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblManualHint, () => AppTheme.SmallFont);

        // "and" rather than an ampersand, from when this was a group box: a
        // group box treats "&" as a mnemonic prefix, and its themed caption
        // drew "&&" as an underscore.
        var card = new Section("Manual backup and restore");

        card.Add(new ButtonBar().With(_btnBackupNow, btnRestore, btnOpenFolder));
        card.Add(lblManualHint, gapBelow: 0);

        return card;
    }

    /// <summary>
    /// Whether and how often a backup is made by itself, how many are kept,
    /// the folder they go to, the cloud folders they are copied to and what
    /// each of those is doing, and when the last one was made. Each line
    /// reads as a sentence with its boxes where the words put them, and
    /// wraps rather than running off the card when the card is too narrow
    /// for it (FieldGrid.Inline).
    /// </summary>
    private Section BuildAutoCard()
    {
        // --- [x] Automatically back up   every [n] day(s) ---
        _chkAutoBackupEnabled.Text = "Automatically back up";
        _chkAutoBackupEnabled.AutoSize = true;
        _chkAutoBackupEnabled.Checked = _settings.AutoBackupEnabled;
        _chkAutoBackupEnabled.CheckedChanged += (_, _) => SaveAutoBackupSettings();

        _numIntervalDays.Minimum = 1;
        _numIntervalDays.Maximum = 30;
        _numIntervalDays.Value = Math.Clamp(_settings.AutoBackupIntervalDays, 1, 30);
        _numIntervalDays.ValueChanged += (_, _) => SaveAutoBackupSettings();

        var lblEvery = new Label { Text = "every", AutoSize = true };

        // The number lives in the box beside it, so this is the noun on
        // its own - see Plural.Word. The last thing on its line, so "day"
        // becoming "days" moves nothing else.
        var lblDays = new Label { Text = Plural.Word((long)_numIntervalDays.Value, "day"), AutoSize = true };
        _numIntervalDays.ValueChanged += (_, _) =>
            lblDays.Text = Plural.Word((long)_numIntervalDays.Value, "day");

        // --- Keep the last [n] automatic backups (...) ---
        _numKeepCount.Minimum = 1;
        _numKeepCount.Maximum = 200;
        _numKeepCount.Value = Math.Clamp(_settings.AutoBackupMaxToKeep, 1, 200);
        _numKeepCount.ValueChanged += (_, _) => SaveAutoBackupSettings();

        var lblKeep = new Label { Text = "Keep the last", AutoSize = true };
        var lblKeepSuffix = new Label { Text = "automatic backups (older ones are deleted)", AutoSize = true };

        // --- Folder: [path] [Change...] ---
        var lblFolder = new Label { Text = "Folder:", AutoSize = true };

        _txtFolder.ReadOnly = true;
        _txtFolder.Text = CurrentFolder();

        _btnChangeFolder.Text = "Change...";
        _btnChangeFolder.Click += BtnChangeFolder_Click;

        // The path box is sized to the path and the button sits right after
        // it - not stretched to the card's far edge with the button pinned
        // out there on its own, which on a wide window left it floating. On
        // a line too narrow for both, the button goes under the box
        // (FieldGrid.Inline). See SizeFolderBox.
        var folderRow = FieldGrid.Inline(lblFolder, _txtFolder, _btnChangeFolder);

        // --- Also copy every backup to:
        //     [x] Google Drive  [x] iCloud Drive  [x] OneDrive  [Check Again] ---
        var lblCloud = new Label { Text = "Also copy every backup to:", AutoSize = true };

        // The boxes get a line of their own, under the caption, with the
        // button that looks for their folders again straight after them
        // rather than pinned to the far edge of the card.
        var cloudRow = new List<Control>();

        foreach (var service in Enum.GetValues<CloudService>())
        {
            var check = new ThemedCheckBox
            {
                Text = CloudBackup.NameOf(service),
                AutoSize = true,
                Checked = CloudBackup.IsEnabled(_settings, service),
            };

            var captured = service;
            check.CheckedChanged += (_, _) =>
            {
                CloudBackup.SetEnabled(_settings, captured, check.Checked);
                CloudBackupStatus.ServiceSwitched(_settings, check.Checked);
                _settings.Save();
                RefreshCloudStatus();
            };

            _cloudChecks[service] = check;
            cloudRow.Add(check);
        }

        var btnCheckCloud = new Button { Text = "Check Again" };
        btnCheckCloud.Click += (_, _) => RefreshCloudStatus();
        cloudRow.Add(btnCheckCloud);

        // Whether the backups are actually reaching the cloud: a green,
        // amber or red light and, beside and under it, what is wrong and
        // what to do. It used to be only the muted grey lines below - which
        // said what each service needed in the same colour as everything
        // else, so a club whose OneDrive folder had been unlinked read right
        // past it. See CloudBackupStatus, which Mission Control's tile
        // reads too.
        var cloudLight = new StatusLine(_pnlCloudLight, _lblCloudLight);

        _lblCloudDetail.AutoSize = true;
        _textSize.Wear(_lblCloudDetail, () => AppTheme.SmallFont);

        // Two lines kept for the detail, so the wording changing with what
        // is ticked or found does not move everything under it; it still
        // grows for a third when it needs one.
        _textSize.ReserveLines(_lblCloudDetail, 2);

        // Where each service's copies go on this PC. The light above says
        // whether they are getting there; this says where, which is what a
        // club checking its own OneDrive folder wants. A line kept for each
        // service and one for the note after them, for the same reason.
        _lblCloudStatus.AutoSize = true;
        _lblCloudStatus.ForeColor = AppTheme.TextMuted;
        _textSize.Wear(_lblCloudStatus, () => AppTheme.SmallFont);
        _textSize.ReserveLines(_lblCloudStatus, Enum.GetValues<CloudService>().Length + 1);
        RefreshCloudStatus();

        // --- Last backup: ... ---
        _lblLastBackup.AutoSize = true;
        _lblLastBackup.ForeColor = AppTheme.TextMuted;

        var card = new Section("Automatic backups");

        card.Add(FieldGrid.Inline(_chkAutoBackupEnabled, lblEvery, _numIntervalDays, lblDays));
        card.Add(FieldGrid.Inline(lblKeep, _numKeepCount, lblKeepSuffix));
        card.Add(folderRow);
        card.Add(lblCloud, gapBelow: UiLayout.HintGap);
        card.Add(FieldGrid.Inline(cloudRow.ToArray()), gapBelow: UiLayout.HintGap);
        card.Add(cloudLight, gapBelow: UiLayout.HintGap);
        card.Add(_lblCloudDetail, gapBelow: UiLayout.HintGap);
        card.Add(_lblCloudStatus);
        card.Add(_lblLastBackup, gapBelow: 0);

        return card;
    }

    /// <summary>
    /// The club's backup password. Keys and tokens are sealed to this PC on
    /// disk, so a backup only brings them to another PC if it carries its
    /// own copy - locked with this.
    /// </summary>
    private Section BuildPasswordCard()
    {
        _txtBackupPassword.UseSystemPasswordChar = true;
        _txtBackupPasswordConfirm.UseSystemPasswordChar = true;

        // Both boxes in one column, past the wider of the two captions.
        var grid = new FieldGrid();

        grid.AddRow("New password:", _txtBackupPassword);
        grid.AddRow("Type it again:", _txtBackupPasswordConfirm);

        var btnSet = new Button { Text = "Set Password" };
        btnSet.Click += BtnSetBackupPassword_Click;

        var btnRemove = new Button { Text = "Remove Password" };
        btnRemove.Click += BtnRemoveBackupPassword_Click;

        // What the current state means - see _lblPasswordStatus.
        RefreshPasswordStatus();

        var lblHint = new Label
        {
            Text = $"At least {MinPasswordLength} characters. Keep it somewhere safe away from this PC - it can't be recovered, and a backup restored on another PC needs it to bring the keys and the QR check-in key across.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblHint, () => AppTheme.SmallFont);

        var card = new Section("Backup password");

        card.Add(grid);
        card.Add(new ButtonBar().With(btnSet, btnRemove));
        card.Add(_lblPasswordStatus);
        card.Add(lblHint, gapBelow: 0);

        return card;
    }

    /// <summary>
    /// The backups list, the two buttons that act on a selected one, and
    /// the status line.
    ///
    /// The list is as tall as the backups it holds, between four rows and
    /// twelve (SizeList), rather than taking whatever height the window has
    /// left - which on a tall window would stand four backups over rows of
    /// nothing, with Restore and Delete stranded at the bottom of it. The
    /// page scrolls instead when the window is too short for it.
    /// </summary>
    private Section BuildListCard()
    {
        _btnRestoreSelected.Text = "Restore Selected";
        _btnRestoreSelected.Click += BtnRestoreSelected_Click;

        _btnDeleteSelected.Text = "Delete Selected";
        _btnDeleteSelected.Click += BtnDeleteSelected_Click;

        // Monospaced, so the date / size / name columns line up, a size
        // under the body face - made again for a new text size, which it
        // never was when it was set once here, and the old one let go.
        _textSize.Keep(() =>
        {
            var size = Math.Max(7.5F, AppTheme.BodyFont.SizeInPoints - 2F);

            if (_listFont is not null && Math.Abs(_listFont.SizeInPoints - size) < 0.01F)
            {
                return;
            }

            var old = _listFont;

            _listFont = new Font("Consolas", size);
            _lstBackups.Font = _listFont;
            old?.Dispose();
        });

        // Off, or the list quietly rounds its own height down to a whole
        // number of rows and leaves a gap under it that nothing explains.
        _lstBackups.IntegralHeight = false;

        // Two lines kept for the status line whether or not it is saying
        // anything: it reports file names and error messages, both of which
        // wrap, and a card that changed height as it reported would jump
        // under the pointer. It still grows for more.
        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = AppTheme.TextMuted;
        _textSize.ReserveLines(_lblStatus, 2);

        var card = new Section("Existing backups");

        card.Add(_lstBackups);
        card.Add(new ButtonBar().With(_btnRestoreSelected, _btnDeleteSelected));
        card.Add(_lblStatus, gapBelow: 0);

        return card;
    }

    /// <summary>
    /// The fields' sizes at the text size in use: the number boxes as wide
    /// as their largest value and their arrows need, and never narrower than
    /// a number box was at the normal text size; the password boxes the
    /// width a field was then, at this size; the folder box to its path
    /// (SizeFolderBox); and the list to its backups, in its face at this
    /// size (SizeList).
    ///
    /// Run again when the text size changes (TextSize.Keep).
    /// </summary>
    private void SizeFields()
    {
        Fit(_numIntervalDays, NumberWidth(_numIntervalDays));
        Fit(_numKeepCount, NumberWidth(_numKeepCount));

        var password = UiLayout.AtTextSize(UiLayout.FieldWidth);

        Fit(_txtBackupPassword, password);
        Fit(_txtBackupPasswordConfirm, password);

        SizeFolderBox();
        SizeList();

        // Only when it changed: a new width lays the line out again.
        static void Fit(Control field, int width)
        {
            if (field.Width != width)
            {
                field.Width = width;
            }
        }
    }

    /// <summary>A number box as wide as its largest value and its arrows need, and never narrower than one was at the normal text size.</summary>
    private int NumberWidth(NumericUpDown box) =>
        Math.Max(
            UiLayout.AtTextSize(UiLayout.NumericWidth),
            TextRenderer.MeasureText(new string('0', ((int)box.Maximum).ToString().Length), Font).Width
                + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra);

    /// <summary>
    /// The folder box: as wide as the path in it, between 260px and 560px at
    /// the normal text size, at the size in use - so the line of caption,
    /// box and Change... fits the card on the narrowest window at every text
    /// size, and on a narrower one Change... goes under the box. A longer
    /// path scrolls inside the box.
    ///
    /// From the path and the text size only, never from the line's width.
    /// It used to be worked out from the line as it was laid out - its width,
    /// its caption's, its button's - and set from their SizeChanged, which
    /// fire in the middle of the line's own layout; the line then kept a
    /// layout made for another width of box, Change... three lines down in a
    /// line one high (the layout test, 200%, 29 Sep). Run when the text size
    /// changes (SizeFields) and when the folder does.
    /// </summary>
    private void SizeFolderBox()
    {
        var width = Math.Clamp(
            TextRenderer.MeasureText(_txtFolder.Text, _txtFolder.Font).Width + 16,
            UiLayout.AtTextSize(FolderBoxLeastWidth),
            UiLayout.AtTextSize(FolderBoxMostWidth));

        // Only when it changed: a new width lays the line out again.
        if (_txtFolder.Width != width)
        {
            _txtFolder.Width = width;
        }
    }

    /// <summary>
    /// Sizes the list to the backups it is actually holding - between four
    /// rows and twelve, of the face it is drawn in.
    ///
    /// Re-run whenever the list changes, and when the text size does. A
    /// fixed number of rows meant four backups sat above six rows of nothing
    /// with the buttons stranded at the bottom of it - and this card is the
    /// last thing on the page, so growing and shrinking it disturbs nothing
    /// above.
    /// </summary>
    private void SizeList()
    {
        // ItemHeight is font-derived and answers before the handle exists,
        // but a list that has not measured itself yet can report nothing -
        // hence the font as the floor.
        var rowHeight = Math.Max(_lstBackups.ItemHeight, _lstBackups.Font.Height);
        var rows = Math.Clamp(_lstBackups.Items.Count, MinBackupRows, MaxBackupRows);
        var height = rowHeight * rows + 4;

        // Only when it changed: a new height lays the card out again.
        if (_lstBackups.Height != height)
        {
            _lstBackups.Height = height;
        }
    }

    private void RefreshPasswordStatus()
    {
        var isSet = !string.IsNullOrEmpty(_settings.BackupPassword);

        _lblPasswordStatus.Text = isSet ? PasswordSetText : NoPasswordText;

        _lblPasswordStatus.ForeColor = isSet ? AppTheme.TextDark : AppTheme.Warning;
    }

    private void BtnSetBackupPassword_Click(object? sender, EventArgs e)
    {
        // Not trimmed: a space is as good a character as any, and whatever
        // is typed here is exactly what has to be typed on the new PC.
        var password = _txtBackupPassword.Text;

        if (password.Length < MinPasswordLength)
        {
            AppDialog.Show(this, $"The backup password needs at least {MinPasswordLength} characters.", "Password too short", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtBackupPassword.Focus();
            return;
        }

        if (!string.Equals(password, _txtBackupPasswordConfirm.Text, StringComparison.Ordinal))
        {
            AppDialog.Show(this, "The two passwords don't match. Type the same password in both boxes.", "Passwords don't match", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtBackupPasswordConfirm.Focus();
            _txtBackupPasswordConfirm.SelectAll();
            return;
        }

        // A password set over another one loses the old one, which backups
        // already made still need - so Undo can put it back.
        CaptureBackupPasswordUndo(string.IsNullOrEmpty(_settings.BackupPassword) ? "Set the backup password" : "Change the backup password");

        _settings.BackupPassword = password;
        _settings.Save();

        // Out of the boxes as soon as it is saved, rather than left sitting
        // on screen behind a row of dots.
        _txtBackupPassword.Clear();
        _txtBackupPasswordConfirm.Clear();
        RefreshPasswordStatus();

        if (SessionOnlyPasswordMessage(_settings) is { } sessionOnly)
        {
            AppDialog.Show(this, sessionOnly, "Backup password set for now", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        AppDialog.Show(this,
            "Backup password set. Backups made from now on carry the keys, tokens and QR check-in key, locked with it - backups made before don't.\n\n" +
            "Write it down and keep it away from this PC. Without it, a backup restored on another PC comes back without its keys.",
            "Backup password set", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// What Set Password says instead while a restored backup's keys wait
    /// for the next start (SecretRecovery's PutOff): every save then writes
    /// the locked keys back unchanged, so the password lasts only until
    /// HeatCaller closes. It used to say "Backup password set" and that
    /// backups now carried the keys, neither of which held. Null otherwise.
    /// </summary>
    internal static string? SessionOnlyPasswordMessage(AppSettings settings) =>
        settings.SecretsLocked && settings.HoldLockedSecrets
            ? "The backup password is set for this session only - it isn't saved.\n\n"
              + "HeatCaller is still waiting to get the restored backup's keys back, and tries again the next time it starts. "
              + "Until then nothing typed in for the keys is saved, this password included. Set it again once the keys are back.\n\n"
              + "A backup made in the meantime won't carry the restored backup's keys."
            : null;

    private void BtnRemoveBackupPassword_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_settings.BackupPassword))
        {
            AppDialog.Show(this, "There's no backup password set.", "No backup password", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = AppDialog.Show(this,
            "Remove the backup password?\n\n" +
            "Backups made from now on won't carry the keys and tokens. Restored on another PC, they'd need entering again and QR check-ins reprinting. Backups already made keep the password they were made with.",
            "Remove backup password", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        CaptureBackupPasswordUndo("Remove the backup password");

        _settings.BackupPassword = string.Empty;
        _settings.Save();
        RefreshPasswordStatus();
    }

    /// <summary>
    /// Puts the backup password back as it is now. Settings first; the
    /// status line is this page's, which lives as long as the app, and is
    /// only touched while it is still there.
    /// </summary>
    private void CaptureBackupPasswordUndo(string description)
    {
        if (_undo is null)
        {
            return;
        }

        // Held sealed (DPAPI, this Windows account only), the same way
        // settings keep it on disk - not as plain text sitting in memory
        // for as long as the step is on the undo list, which is exactly
        // what a password changed because it leaked must not do.
        string sealedBefore;
        try
        {
            sealedBefore = SecretBox.Seal(_settings.BackupPassword);
        }
        catch
        {
            // Can't seal it: no undo, rather than one holding it in the clear.
            return;
        }

        _undo.Capture(description, () =>
        {
            _settings.BackupPassword = SecretBox.Open(sealedBefore);
            _settings.Save();

            if (!IsDisposed)
            {
                RefreshPasswordStatus();
            }
        });
    }

    /// <summary>
    /// Looks for each cloud service's folder on this PC again (it can be
    /// installed or signed into while the app is open) and says how the
    /// cloud copies stand: the light, what to do about it, and where each
    /// service's copies go. The boxes stay clickable either way - greyed-out
    /// text was unreadable on the dark looks, and the light already says
    /// what is needed.
    /// </summary>
    private void RefreshCloudStatus()
    {
        // Judged in one place, so this page and Mission Control's tile can
        // never disagree about whether the backups are reaching the cloud.
        var state = CloudBackupStatus.Assess(CloudBackupStatus.Read(_settings, DateTime.UtcNow));

        _pnlCloudLight.BackColor = LightColor(state.Level);
        _lblCloudLight.ForeColor = LightColor(state.Level);
        _lblCloudLight.Text = state.Headline;

        // The detail stays in the ordinary muted ink: the light and the few
        // words beside it carry the colour, and two sentences of red is
        // harder to read than it is alarming.
        _lblCloudDetail.ForeColor = AppTheme.TextMuted;
        _lblCloudDetail.Text = state.Detail;

        var lines = new List<string>();

        foreach (var (service, check) in _cloudChecks)
        {
            var root = CloudBackup.FindSyncRoot(service);

            if (root is not null)
            {
                lines.Add(check.Checked
                    ? $"{CloudBackup.NameOf(service)}: copies go to {Path.Combine(root, CloudBackup.FolderName)}"
                    : $"{CloudBackup.NameOf(service)}: found on this PC, but not ticked.");
            }
            else
            {
                lines.Add($"{CloudBackup.NameOf(service)}: needs {CloudBackup.AppNameOf(service)} installed and signed in on this PC.");
            }
        }

        lines.Add("Copies upload whenever the laptop is online. Set a backup password below to include your keys, locked.");
        _lblCloudStatus.Text = string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// The light's colour for a level - the same green, amber and red
    /// Mission Control's tiles use (MissionControlForm), through the theme,
    /// so it follows every look and never comes out unreadable on a dark one.
    /// </summary>
    private static Color LightColor(MissionControl.Level level) => level switch
    {
        MissionControl.Level.Problem => AppTheme.Danger,
        MissionControl.Level.Watch => AppTheme.Warning,
        MissionControl.Level.Good => AppTheme.Success,
        _ => AppTheme.TextMuted,
    };

    private string CurrentFolder() =>
        // The guarded answer, so the page shows where backups will REALLY
        // go rather than echoing a setting that is about to be overruled.
        BackupService.SafeBackupFolder(_settings.AutoBackupFolder);

    private void SaveAutoBackupSettings()
    {
        _settings.AutoBackupEnabled = _chkAutoBackupEnabled.Checked;
        _settings.AutoBackupIntervalDays = (int)_numIntervalDays.Value;
        _settings.AutoBackupMaxToKeep = (int)_numKeepCount.Value;
        _settings.Save();
    }

    private void BtnChangeFolder_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = CurrentFolder(), Description = "Choose a folder for backups" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _settings.AutoBackupFolder = dialog.SelectedPath;
        _settings.Save();
        _txtFolder.Text = CurrentFolder();
        SizeFolderBox();
        RefreshBackupList();
    }

    /// <summary>
    /// Makes a backup in the background, as the automatic one is made.
    ///
    /// It used to be made right here on the UI thread, and zipping the data
    /// folder - music library included - froze the whole app, timing and
    /// all, for as long as that took. Everything read from the settings
    /// (the folder, the secrets) is read here first, on the UI thread;
    /// only the zipping goes to the background.
    /// </summary>
    private async void BtnBackupNow_Click(object? sender, EventArgs e)
    {
        // One at a time: the automatic backup may be part way through, and
        // two zips of the same folder at once only make both slower.
        if (BackupService.IsBackupRunning)
        {
            _lblStatus.Text = "A backup is already being written - try again in a moment.";
            return;
        }

        string folder;
        BackupSecrets? secrets;
        IReadOnlyList<CloudBackup.Target> clouds;
        int keep;

        try
        {
            folder = CurrentFolder();
            secrets = BackupService.CaptureSecrets();
            clouds = CloudBackup.EnabledTargets(_settings);
            keep = _settings.AutoBackupMaxToKeep;
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, ex.Message, "Backup failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _btnBackupNow.Enabled = false;
        _lblStatus.Text = "Backing up...";

        string path;

        try
        {
            path = await Task.Run(() => BackupService.CreateBackup(folder, false, secrets));
        }
        catch (Exception ex)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            _btnBackupNow.Enabled = true;
            _lblStatus.Text = string.Empty;
            AppDialog.Show(this, ex.Message, "Backup failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Back on the UI thread (the await returns here). The page can have
        // been closed meanwhile, and a restore can have frozen the files -
        // a save now would put pre-restore settings back.
        if (IsDisposed || Disposing || SafeFile.IsFrozen)
        {
            return;
        }

        _btnBackupNow.Enabled = true;
        _lblStatus.Text = $"Backup created: {Path.GetFileName(path)}";

        try
        {
            _settings.LastAutoBackupUtc = DateTime.UtcNow;
            _settings.Save();
            RefreshLastBackupLabel();
            RefreshBackupList();
        }
        catch (Exception ex)
        {
            // The backup itself is made; only the page's refresh failed.
            AppDialog.Show(this, ex.Message, "Backup made, but the list could not be refreshed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        if (clouds.Count == 0)
        {
            return;
        }

        var made = $"Backup created: {Path.GetFileName(path)}.";
        _lblStatus.Text = made + " Copying to the cloud...";

        var results = await Task.Run(() => CloudBackup.Copy(path, clouds, keep));

        if (IsDisposed || Disposing || SafeFile.IsFrozen)
        {
            return;
        }

        _lblStatus.Text = made + " " + string.Join(" ", results.Select(r => r.Line));

        // What the copies did, written down for the light - see
        // CloudBackupStatus. Not from the rehearsal copy, which copies
        // nothing on purpose: its "not copied" lines are not a fault.
        if (AppPaths.IsRehearsal)
        {
            return;
        }

        try
        {
            CloudBackupStatus.Record(_settings, results, DateTime.UtcNow);
            _settings.Save();
            RefreshCloudStatus();
        }
        catch (Exception ex)
        {
            // The backup and its copies are made; only the note about them
            // failed to save. Said on the line, not in a dialog over it.
            _lblStatus.Text = made + $" Couldn't note how the cloud copies went: {ex.Message}";
        }
    }

    private void BtnRestore_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Backup files (*.zip)|*.zip|All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(CurrentFolder()) ? CurrentFolder() : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            RestoreFrom(dialog.FileName);
        }
    }

    private void BtnRestoreSelected_Click(object? sender, EventArgs e)
    {
        if (_lstBackups.SelectedItem is not BackupListItem item)
        {
            AppDialog.Show(this, "Select a backup from the list first.", "No backup selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        RestoreFrom(item.File.FullName);
    }

    /// <summary>
    /// Whether a restore may start now, saying why not when it may not.
    ///
    /// A restore restarts the app, which would end a race, and its safety
    /// backup holds this thread while it zips the data folder. A backup
    /// still being written (the automatic one runs in the background) reads
    /// the very files a restore replaces.
    /// </summary>
    private bool CanRestoreNow()
    {
        if (IsRaceUnderWay?.Invoke() == true)
        {
            AppDialog.Show(this,
                "A race or practice is running. Restore a backup once it has finished - restoring restarts the app, which would end it.",
                "Race running", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        if (BackupService.IsBackupRunning)
        {
            AppDialog.Show(this,
                "A backup is being written - try again in a moment.",
                "Backup in progress", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        return true;
    }

    private void RestoreFrom(string zipPath)
    {
        // Checked before asking, so nobody confirms a restore only to be
        // told no - and again after, below.
        if (!CanRestoreNow())
        {
            return;
        }

        // Said before the restore, not after: once it has run, the badge key
        // in memory is the pre-restore one and the settings can no longer be
        // saved, so there is nothing left to compare the restored key with.
        // A backup made before Reissue All Badges carries the badge key as it
        // was then, and putting that back stops every badge printed since the
        // reissue working - silently, until someone scans one on race night.
        // Only for a club that has online badges at all.
        var badgeNote = string.IsNullOrWhiteSpace(_settings.RelayCheckInSecret)
            ? string.Empty
            : "QR check-ins: the backup's QR check-in key comes back with it. QR check-ins printed with the key in this backup work; "
              + "if you have used Reissue All QR Check-ins since it was made, QR check-ins printed after that stop working - "
              + "print them again, or reissue again on the Club Website page.\n\n";

        // Which backup, and when it was taken, said in the question itself.
        // It used to name neither, so the only check on picking last March's
        // backup instead of last week's was a date column that could be
        // wrong (see BackupListItem) - and Delete, which is far less harm,
        // named its file when this did not.
        var backupFile = new FileInfo(zipPath);
        var whichBackup = $"Backup: {backupFile.Name}\nTaken: {BackupService.BackupTime(backupFile):yyyy-MM-dd} {AppTime.TimeOfDay(BackupService.BackupTime(backupFile))}\n\n";

        var confirm = AppDialog.Show(this,
            whichBackup +
            "Restoring will overwrite your current driver profiles, race classes, session history, track layouts, and settings with the contents of this backup.\n\n" +
            badgeNote +
            "A safety backup of your CURRENT data will be taken first - the Undo button can't reverse a restore, but restoring that safety backup can. The app will need to restart afterward for the restored data to take effect.\n\n" +
            "Continue?",
            "Confirm restore", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        // Again: the app kept running while the question was on screen, so
        // the automatic backup or a race can have started meanwhile. From
        // here to the restart everything runs on this thread, so nothing
        // else can start before the restore is done.
        if (!CanRestoreNow())
        {
            return;
        }

        RunRestore(zipPath, keepAsTheyAre: null, refused: null);
    }

    /// <summary>
    /// The restore itself, once asked and allowed: shared by Restore and by
    /// the standby PC's Take Over (11 Nov 2026, Phase 6.2 - see
    /// BackupRestoreControl.Standby), so Take Over gets every protection a
    /// restore has - the safety backup, the files let go, saving frozen at
    /// the point of no return, the restart. <paramref name="keepAsTheyAre"/>
    /// goes to BackupService.RestoreBackup; <paramref name="refused"/> runs
    /// when the restore was refused with nothing touched, for Take Over to
    /// put back what it changed first. <paramref name="atPointOfNoReturn"/>
    /// runs once the safety backup is made and the backup has unpacked, just
    /// before the first file is replaced and while saving still works - for
    /// Take Over to leave its marker, which must not be in the safety backup.
    /// </summary>
    private void RunRestore(string zipPath, IReadOnlyCollection<string>? keepAsTheyAre, Action? refused, Action? atPointOfNoReturn = null)
    {
        // Set the moment the restore reaches the point of no return. After
        // that the app restarts whatever happens, success or not.
        var filesTouched = false;

        void BeforeReplacing()
        {
            filesTouched = true;

            try
            {
                atPointOfNoReturn?.Invoke();
            }
            catch
            {
                // The restore goes on: it is past stopping cleanly.
            }

            // Files closed first, so anything closing them saves lands
            // before the restored files go in rather than after.
            try
            {
                ReleaseFilesForRestore?.Invoke();
            }
            catch
            {
                // A file still open is retried, then reported, by the restore.
            }

            // From here on nothing this process holds may be saved. Every
            // store and the settings in memory are the PRE-restore ones, and
            // the restart closes the main window - which saves the settings
            // - before the new process starts. Frozen BEFORE the first file
            // is replaced, not after the last: a timer's save landing in
            // between would put one old file back among the restored ones,
            // and a restore that stopped partway used to leave saving
            // switched on, so the old data went straight back over the
            // half that had been restored.
            SafeFile.Freeze();
        }

        RestoreResult result;

        try
        {
            result = BackupService.RestoreBackup(zipPath, CurrentFolder(), BeforeReplacing, keepAsTheyAre);
        }
        catch (Exception ex)
        {
            if (!filesTouched)
            {
                try
                {
                    refused?.Invoke();
                }
                catch
                {
                    // Putting the caller's own changes back is a help; the
                    // refusal below is what matters.
                }

                // Refused or couldn't unpack: the data folder is exactly as
                // it was, and the app carries on as if nothing happened.
                AppDialog.Show(this, $"Restore failed: {ex.Message}", "Restore failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Should not happen - the restore reports rather than throws once
            // files are going back - but if it does, restarting is still the
            // only safe thing to do.
            AppDialog.Show(this,
                $"The restore stopped partway through: {ex.Message}\n\nThe app will now restart. If things look wrong afterward, restore the safety backup in the \"Pre-Restore Safety Backups\" folder inside the backup folder.",
                "Restore incomplete", MessageBoxButtons.OK, MessageBoxIcon.Error);
            RestoreCompleted?.Invoke();
            return;
        }

        var safetyNote = string.IsNullOrEmpty(result.SafetyBackupPath)
            ? string.Empty
            : $"\n\nYour previous data was saved to:\n{result.SafetyBackupPath}";

        var skippedNote = result.Skipped.Count == 0
            ? string.Empty
            : $"\n\n{Plural.Of(result.Skipped.Count, "music, audio or log file")} {(result.Skipped.Count == 1 ? "was" : "were")} in use and kept as {(result.Skipped.Count == 1 ? "it was" : "they were")}:\n{ListForDialog(result.Skipped)}";

        if (result.IsComplete)
        {
            AppDialog.Show(this,
                $"Restore complete.{safetyNote}{skippedNote}\n\nThe app will now restart to load the restored data.",
                "Restore complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            var undo = string.IsNullOrEmpty(result.SafetyBackupPath)
                ? string.Empty
                : " To go back to how things were before, restore the safety backup named above.";

            AppDialog.Show(this,
                $"The restore only partly worked. These files couldn't be put back, so they still hold the data from before the restore:\n{ListForDialog(result.Failed)}\n\n" +
                $"Close anything that might have them open and restore the same backup again.{undo}{safetyNote}{skippedNote}\n\nThe app will now restart.",
                "Restore incomplete", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        RestoreCompleted?.Invoke();
    }

    /// <summary>A list of file names for a message box, cut short so a long one can't push the OK button off the screen.</summary>
    private static string ListForDialog(IReadOnlyList<string> items)
    {
        const int shown = 8;

        var lines = items.Take(shown).Select(item => "   " + item).ToList();
        if (items.Count > shown)
        {
            lines.Add($"   ...and {items.Count - shown} more");
        }

        return string.Join("\n", lines);
    }

    private void BtnDeleteSelected_Click(object? sender, EventArgs e)
    {
        if (_lstBackups.SelectedItem is not BackupListItem item)
        {
            AppDialog.Show(this, "Select a backup from the list first.", "No backup selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Held for Undo only when that is a rename: the holding folder is
        // beside the app's data, and a backup on another drive (a USB stick)
        // would be copied across in full, on this thread, to be deleted a
        // week later anyway.
        var original = item.File.FullName;
        var canHold = _undo is not null
                      && string.Equals(Path.GetPathRoot(Path.GetFullPath(original)), Path.GetPathRoot(Path.GetFullPath(UndoHoldingFolder.RoamingRoot)), StringComparison.OrdinalIgnoreCase);

        var question = canHold
            ? $"Delete {item.File.Name}? Undo can bring it back for the next week."
            : $"Delete {item.File.Name}? It's on another drive, so it is deleted outright - this can't be undone.";

        if (AppDialog.Show(this, question, "Confirm delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var held = canHold ? UndoHoldingFolder.MoveIn(original, UndoHoldingFolder.NewFolder()) : null;

            if (held is null && canHold)
            {
                // 17 Oct 2026, Bug Hunt 8, A23-5: the operator was just told
                // "Undo can bring it back", so a failed move to the holding
                // folder must not fall through to deleting it outright.
                AppDialog.Show(this, $"Couldn't set {item.File.Name} aside for Undo, so it has not been deleted.", "Delete failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                return;
            }

            if (held is null)
            {
                item.File.Delete();
            }
            else
            {
                // Captured once the move has worked rather than before it: a
                // move that failed has changed nothing, and an undo entry
                // for it would only undo nothing.
                _undo?.Capture($"Delete backup {item.File.Name}", () =>
                {
                    var back = UndoHoldingFolder.MoveBack(held, original);

                    if (!IsDisposed)
                    {
                        RefreshBackupList();
                    }

                    // Said out loud rather than logged as undone: a file of
                    // that name made since is left alone, and a held file
                    // cleared by the week's pruning is gone.
                    if (!back)
                    {
                        throw new InvalidOperationException($"{Path.GetFileName(original)} couldn't be put back - there is a file of that name there now, or it was cleared from the holding folder.");
                    }
                });
            }

            RefreshBackupList();
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't delete that file: {ex.Message}", "Delete failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RefreshBackupList()
    {
        _lstBackups.Items.Clear();

        // Guarded: this runs in the constructor, as the main window is
        // built, and a backup folder that exists but can't be listed (a
        // network share gone read-only, a folder whose permissions changed)
        // threw out of it and stopped the app opening at all.
        IReadOnlyList<FileInfo> backups;

        try
        {
            backups = BackupService.ListBackups(CurrentFolder());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _lblStatus.Text = $"Couldn't list the backups in that folder: {ex.Message}";
            return;
        }

        foreach (var file in backups)
        {
            _lstBackups.Items.Add(new BackupListItem(file));
        }

        SizeList();
    }

    private void RefreshLastBackupLabel()
    {
        _lblLastBackup.Text = _settings.LastAutoBackupUtc is { } last
            ? $"Last backup: {last.ToLocalTime():yyyy-MM-dd} {AppTime.TimeOfDay(last.ToLocalTime())}"
            : "Last backup: never";
    }

    private void OpenFolderInExplorer(string folder)
    {
        // Said in a dialog rather than thrown. The backup folder can be a USB
        // stick that has since been pulled out, or a drive that is no longer
        // there, and an exception here went to the app's crash dialog.
        try
        {
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't open the backup folder: {ex.Message}", "Folder not opened", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// One row of the list. The date is when the backup was taken, from its
    /// name - the time the list is sorted by. It used to be the file's own
    /// modified time, which changes whenever the zip is copied or synced: on
    /// a new PC every OneDrive backup showed the day it was downloaded, and a
    /// March backup brought back on a USB stick showed today's date while
    /// sorting below October's.
    /// </summary>
    private sealed record BackupListItem(FileInfo File)
    {
        public override string ToString() =>
            $"{BackupService.BackupTime(File):yyyy-MM-dd} {AppTime.TimeOfDay(BackupService.BackupTime(File))}   {File.Length / 1024.0:0.#} KB   {File.Name}";
    }
}
