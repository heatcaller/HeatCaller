namespace HeatCaller;

/// <summary>
/// Backup and restore for everything the app persists - driver profiles,
/// race classes/rounds, session history, track layouts, and all settings.
/// See BackupService for what a "backup" actually is (a zip of the whole
/// app data folder).
/// </summary>
public sealed class BackupRestoreControl : UserControl
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
    /// SupportInfo - because it needs Driver Profiles, which this page
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

    private readonly Button _btnBackupNow = new();

    private readonly Label _lblLastBackup = new();
    private readonly CheckBox _chkAutoBackupEnabled = new();
    private readonly NumericUpDown _numIntervalDays = new();
    private readonly NumericUpDown _numKeepCount = new();
    private readonly TextBox _txtFolder = new();
    private readonly ListBox _lstBackups = new();
    private readonly Label _lblStatus = new();
    private readonly GroupBox _grpList = new();
    private readonly Button _btnRestoreSelected = new();
    private readonly Button _btnDeleteSelected = new();
    private readonly Dictionary<CloudService, CheckBox> _cloudChecks = new();
    private readonly Label _lblCloudStatus = new();
    private readonly Button _btnChangeFolder = new();
    private int _folderRowMaxRight;
    private readonly TextBox _txtBackupPassword = new();
    private readonly TextBox _txtBackupPasswordConfirm = new();
    private readonly Label _lblPasswordStatus = new();

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
        AutoScroll = true;
        MinimumSize = new Size(560, 560);
        Font = AppTheme.BodyFont;

        BuildLayout();
        RefreshBackupList();
        RefreshLastBackupLabel();
    }

    /// <summary>
    /// The gap between a group box's own edge and the controls inside it.
    /// </summary>
    private const int Inset = 12;

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

    /// <summary>
    /// Builds the page.
    ///
    /// Nothing here carries a hand-picked coordinate. Every position is
    /// derived from the control above or beside it, measured in the font
    /// the control will actually be drawn in, so the page lays itself out
    /// correctly at any font scale instead of being laid out wrong and then
    /// patched by LayoutDoctor on every launch - which is what this page
    /// used to do, and what made the doctor stand down and leave it alone.
    /// </summary>
    private void BuildLayout()
    {
        var width = MinimumSize.Width - UiLayout.PageLeft - UiLayout.PageRight;

        var grpManual = BuildManualGroup(width);
        grpManual.Location = new Point(UiLayout.PageLeft, UiLayout.PageTop);

        var grpAuto = BuildAutoGroup(width);
        grpAuto.Location = new Point(UiLayout.PageLeft, UiLayout.Below(grpManual, UiLayout.RowGap));

        var grpPassword = BuildPasswordGroup(width);
        grpPassword.Location = new Point(UiLayout.PageLeft, UiLayout.Below(grpAuto, UiLayout.RowGap));

        var grpSupport = BuildSupportGroup(width);
        grpSupport.Location = new Point(UiLayout.PageLeft, UiLayout.Below(grpPassword, UiLayout.RowGap));

        // The list stays the last box on the page: it grows and shrinks with
        // the backups it holds (LayoutListGroup), and nothing sits below it
        // to be pushed about.
        var listTop = UiLayout.Below(grpSupport, UiLayout.RowGap);

        var grpList = BuildListGroup(width);

        grpList.Location = new Point(UiLayout.PageLeft, listTop);

        Controls.Add(grpManual);
        Controls.Add(grpAuto);
        Controls.Add(grpPassword);
        Controls.Add(grpSupport);
        Controls.Add(grpList);
    }

    /// <summary>
    /// Save Support Info: one zip of the logs and crash log, with driver
    /// names and emails taken out, for a club to email when something went
    /// wrong. On this page because it is where the app's files are looked
    /// after; the crash dialog and the console's menu point here too.
    /// </summary>
    private GroupBox BuildSupportGroup(int width)
    {
        var group = new GroupBox
        {
            Text = "Getting Help",
            Font = AppTheme.BodyFont,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        var btnSupport = new Button { Text = "Save Support Info..." };
        btnSupport.Click += (_, _) => SupportInfoRequested?.Invoke();

        UiLayout.ButtonRow(Inset, UiLayout.InsideGroup(group), btnSupport);

        var lblHint = new Label
        {
            Text = "Saves one file to attach to an email when something went wrong: the console from the last few runs, the crash log, "
                   + "the version and the settings that matter. Driver names, email addresses and your Windows user name are replaced "
                   + "first, and no keys, tokens, PINs or passwords go in.",
            AutoSize = true,
            MaximumSize = new Size(width - Inset * 2, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
            Location = new Point(Inset, UiLayout.Below(btnSupport, UiLayout.RowGap)),
        };

        group.Controls.AddRange(new Control[] { btnSupport, lblHint });
        group.Size = new Size(width, UiLayout.Below(lblHint, Inset));

        return group;
    }

    private GroupBox BuildManualGroup(int width)
    {
        // "and" rather than an ampersand: a group box treats "&" as a
        // mnemonic prefix, and the themed caption drew "&&" as an
        // underscore.
        var group = new GroupBox
        {
            Text = "Manual Backup and Restore",
            Font = AppTheme.BodyFont,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        // A field, so it can be switched off while its backup is being made.
        var btnBackupNow = _btnBackupNow;
        btnBackupNow.Text = "Back Up Now";
        btnBackupNow.Click += BtnBackupNow_Click;

        var btnRestore = new Button { Text = "Restore from File..." };
        btnRestore.Click += BtnRestore_Click;

        var btnOpenFolder = new Button { Text = "Open Backup Folder" };
        btnOpenFolder.Click += (_, _) => OpenFolderInExplorer(CurrentFolder());

        // Each button sized to its own caption in the bold face the theme
        // will give it, rather than to a width somebody guessed.
        UiLayout.ButtonRow(Inset, UiLayout.InsideGroup(group), btnBackupNow, btnRestore, btnOpenFolder);

        var lblManualHint = new Label
        {
            Text = "Includes driver profiles, race classes, session history, track layouts, and all app settings.",
            AutoSize = true,
            MaximumSize = new Size(width - Inset * 2, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
            Location = new Point(Inset, UiLayout.Below(btnBackupNow, UiLayout.RowGap)),
        };

        group.Controls.AddRange(new Control[] { btnBackupNow, btnRestore, btnOpenFolder, lblManualHint });
        group.Size = new Size(width, UiLayout.Below(lblManualHint, Inset));

        return group;
    }

    private GroupBox BuildAutoGroup(int width)
    {
        var group = new GroupBox
        {
            Text = "Automatic Backups",
            Font = AppTheme.BodyFont,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        // Fonts assigned before anything is measured. A spinner and a text
        // box both take their height from their font, and a row positioned
        // against the height they had BEFORE the font arrived is a row laid
        // out against the wrong numbers.
        _numIntervalDays.Font = AppTheme.BodyFont;
        _numKeepCount.Font = AppTheme.BodyFont;
        _txtFolder.Font = AppTheme.BodyFont;

        // --- Row 1: [x] Automatically back up   every [n] day(s) ---
        var y = UiLayout.InsideGroup(group);

        _chkAutoBackupEnabled.Text = "Automatically back up";
        _chkAutoBackupEnabled.AutoSize = true;
        _chkAutoBackupEnabled.Checked = _settings.AutoBackupEnabled;
        _chkAutoBackupEnabled.CheckedChanged += (_, _) => SaveAutoBackupSettings();

        _numIntervalDays.Width = UiLayout.NumericWidth;
        _numIntervalDays.Minimum = 1;
        _numIntervalDays.Maximum = 30;
        _numIntervalDays.Value = Math.Clamp(_settings.AutoBackupIntervalDays, 1, 30);
        _numIntervalDays.ValueChanged += (_, _) => SaveAutoBackupSettings();

        var fieldHeight = _numIntervalDays.Height;

        var lblEvery = new Label { Text = "every", AutoSize = true };
        // The number lives in the box beside it, so this is the noun on
        // its own - see Plural.Word. Safe to re-measure because it is the
        // last control on the row and nothing is positioned from it.
        var lblDays = new Label { Text = Plural.Word((long)_numIntervalDays.Value, "day"), AutoSize = true };
        _numIntervalDays.ValueChanged += (_, _) =>
            lblDays.Text = Plural.Word((long)_numIntervalDays.Value, "day");

        _chkAutoBackupEnabled.Location = new Point(Inset, UiLayout.CentreOn(_chkAutoBackupEnabled, y, fieldHeight));
        lblEvery.Location = new Point(UiLayout.After(_chkAutoBackupEnabled) + UiLayout.RowGap, UiLayout.CentreOn(lblEvery, y, fieldHeight));
        _numIntervalDays.Location = new Point(UiLayout.After(lblEvery), y);
        lblDays.Location = new Point(UiLayout.After(_numIntervalDays), UiLayout.CentreOn(lblDays, y, fieldHeight));

        // --- Row 2: Keep the last [n] automatic backups (...) ---
        y = UiLayout.Below(_numIntervalDays, UiLayout.RowGap);

        _numKeepCount.Width = UiLayout.NumericWidth;
        _numKeepCount.Minimum = 1;
        _numKeepCount.Maximum = 200;
        _numKeepCount.Value = Math.Clamp(_settings.AutoBackupMaxToKeep, 1, 200);
        _numKeepCount.ValueChanged += (_, _) => SaveAutoBackupSettings();

        var lblKeep = new Label { Text = "Keep the last", AutoSize = true };
        var lblKeepSuffix = new Label { Text = "automatic backups (older ones are deleted automatically)", AutoSize = true };

        lblKeep.Location = new Point(Inset, UiLayout.CentreOn(lblKeep, y, fieldHeight));
        _numKeepCount.Location = new Point(UiLayout.After(lblKeep), y);
        lblKeepSuffix.Location = new Point(UiLayout.After(_numKeepCount), UiLayout.CentreOn(lblKeepSuffix, y, fieldHeight));

        // Capped so a long tail wraps inside the box instead of running out
        // past its right edge and leaving the doctor to drag it back.
        lblKeepSuffix.MaximumSize = new Size(Math.Max(120, width - Inset - lblKeepSuffix.Left), 0);

        // --- Row 3: Folder: [path] [Change...] ---
        y = UiLayout.Below(lblKeepSuffix, UiLayout.RowGap);

        var lblFolder = new Label { Text = "Folder:", AutoSize = true };

        _txtFolder.ReadOnly = true;
        _txtFolder.Text = CurrentFolder();

        var btnChangeFolder = _btnChangeFolder;
        btnChangeFolder.Text = "Change...";
        btnChangeFolder.Click += BtnChangeFolder_Click;
        btnChangeFolder.Size = new Size(UiLayout.WidthFor(btnChangeFolder), UiLayout.ButtonHeight);

        lblFolder.Location = new Point(Inset, UiLayout.CentreOn(lblFolder, y, UiLayout.ButtonHeight));

        _txtFolder.Left = UiLayout.After(lblFolder);
        _txtFolder.Top = UiLayout.CentreOn(_txtFolder, y, UiLayout.ButtonHeight);
        btnChangeFolder.Top = y;

        // The path box is sized to the path and the button sits right after
        // it - not stretched to the group's far edge with the button pinned
        // out there on its own, which on a wide window left it floating.
        _folderRowMaxRight = width - Inset;
        LayOutFolderRow();

        // --- Row 4: Also copy every backup to:
        //            [x] Google Drive  [x] iCloud Drive  [x] OneDrive   [Check Again] ---
        y = UiLayout.Below(btnChangeFolder, UiLayout.RowGap);

        var lblCloud = new Label { Text = "Also copy every backup to:", AutoSize = true };
        lblCloud.Location = new Point(Inset, y + 2);

        // The boxes get a row of their own: beside the caption, with the
        // button, they would not fit the group's width.
        y = UiLayout.Below(lblCloud, 6);
        var x = Inset;
        var cloudRowBottom = y;

        foreach (var service in Enum.GetValues<CloudService>())
        {
            var check = new CheckBox
            {
                Text = CloudBackup.NameOf(service),
                AutoSize = true,
                Checked = CloudBackup.IsEnabled(_settings, service),
                Font = AppTheme.BodyFont,
            };

            var captured = service;
            check.CheckedChanged += (_, _) =>
            {
                CloudBackup.SetEnabled(_settings, captured, check.Checked);
                _settings.Save();
                RefreshCloudStatus();
            };

            check.Location = new Point(x, y);
            x = UiLayout.After(check) + UiLayout.RowGap;
            cloudRowBottom = Math.Max(cloudRowBottom, UiLayout.Below(check, 0));

            _cloudChecks[service] = check;
        }

        var btnCheckCloud = new Button { Text = "Check Again" };
        btnCheckCloud.Click += (_, _) => RefreshCloudStatus();
        btnCheckCloud.Size = new Size(UiLayout.WidthFor(btnCheckCloud), UiLayout.ButtonHeight);
        // Straight after the boxes it re-checks, rather than pinned to the
        // far edge of the group.
        var checkHeight = UiLayout.HeightOf(_cloudChecks.Values.Last());
        btnCheckCloud.Location = new Point(x + UiLayout.RowGap, y - Math.Max(0, (btnCheckCloud.Height - checkHeight) / 2));
        cloudRowBottom = Math.Max(cloudRowBottom, btnCheckCloud.Bottom);

        // What each service's app is doing on this PC - found and where, or
        // what to install - so a copy that can't happen is visible here,
        // not only in the log after a backup.
        // A fixed height with room for every line, wrapped - so the text
        // changing with what is ticked or found never runs into "Last
        // backup" below it.
        _lblCloudStatus.AutoSize = false;
        _lblCloudStatus.Font = AppTheme.SmallFont;
        _lblCloudStatus.Size = new Size(width - Inset * 2, TextRenderer.MeasureText("Ag", AppTheme.SmallFont).Height * 6 + 4);
        _lblCloudStatus.ForeColor = AppTheme.TextMuted;
        _lblCloudStatus.Location = new Point(Inset, Math.Max(cloudRowBottom, btnCheckCloud.Bottom) + 4);
        RefreshCloudStatus();

        // --- Row 5: Last backup: ... ---
        _lblLastBackup.AutoSize = true;
        _lblLastBackup.ForeColor = AppTheme.TextMuted;
        _lblLastBackup.Location = new Point(Inset, UiLayout.Below(_lblCloudStatus, UiLayout.RowGap));

        // Width first, height after.
        //
        // "Change..." is placed at width - Inset - its own width and
        // anchored Right, and an anchor keeps the margin a control had
        // when its parent was laid out. A GroupBox starts 200 wide, so
        // adding it before the group knew its width risked baking in a
        // margin measured against 200 rather than against 528. The height
        // still has to come afterwards, because it depends on where the
        // last row ended up.
        group.Width = width;

        group.Controls.AddRange(new Control[]
        {
            _chkAutoBackupEnabled, lblEvery, _numIntervalDays, lblDays,
            lblKeep, _numKeepCount, lblKeepSuffix,
            lblFolder, _txtFolder, btnChangeFolder,
            lblCloud, btnCheckCloud, _lblCloudStatus,
            _lblLastBackup,
        });

        group.Controls.AddRange(_cloudChecks.Values.ToArray<Control>());

        group.Size = new Size(width, UiLayout.Below(_lblLastBackup, Inset));

        return group;
    }

    /// <summary>
    /// The club's backup password. Keys and tokens are sealed to this PC on
    /// disk, so a backup only brings them to another PC if it carries its
    /// own copy - locked with this.
    /// </summary>
    private GroupBox BuildPasswordGroup(int width)
    {
        var group = new GroupBox
        {
            Text = "Backup Password",
            Font = AppTheme.BodyFont,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        // Fonts first, for the same reason as the automatic group: the
        // boxes take their height from their font.
        _txtBackupPassword.Font = AppTheme.BodyFont;
        _txtBackupPasswordConfirm.Font = AppTheme.BodyFont;
        _txtBackupPassword.UseSystemPasswordChar = true;
        _txtBackupPasswordConfirm.UseSystemPasswordChar = true;
        _txtBackupPassword.Width = UiLayout.FieldWidth;
        _txtBackupPasswordConfirm.Width = UiLayout.FieldWidth;

        var fieldHeight = _txtBackupPassword.Height;

        var lblPassword = new Label { Text = "New password:", AutoSize = true, Font = AppTheme.BodyFont, Left = Inset };
        var lblConfirm = new Label { Text = "Type it again:", AutoSize = true, Font = AppTheme.BodyFont, Left = Inset };

        // Both boxes start at the same x, past the wider of the two captions,
        // measured in the font they are drawn in.
        var fieldLeft = Math.Max(
            UiLayout.After(lblPassword, AppTheme.BodyFont),
            UiLayout.After(lblConfirm, AppTheme.BodyFont));

        // --- Row 1: New password: [.....] ---
        var y = UiLayout.InsideGroup(group);

        lblPassword.Location = new Point(Inset, UiLayout.CentreOn(lblPassword, y, fieldHeight));
        _txtBackupPassword.Location = new Point(fieldLeft, y);

        // --- Row 2: Type it again: [.....] ---
        y = UiLayout.Below(_txtBackupPassword, UiLayout.RowGap);

        lblConfirm.Location = new Point(Inset, UiLayout.CentreOn(lblConfirm, y, fieldHeight));
        _txtBackupPasswordConfirm.Location = new Point(fieldLeft, y);

        // --- Row 3: [Set Password] [Remove Password] ---
        var btnSet = new Button { Text = "Set Password" };
        btnSet.Click += BtnSetBackupPassword_Click;

        var btnRemove = new Button { Text = "Remove Password" };
        btnRemove.Click += BtnRemoveBackupPassword_Click;

        UiLayout.ButtonRow(Inset, UiLayout.Below(_txtBackupPasswordConfirm, UiLayout.RowGap), btnSet, btnRemove);

        // --- Row 4: what the current state means ---
        _lblPasswordStatus.AutoSize = true;
        _lblPasswordStatus.MaximumSize = new Size(width - Inset * 2, 0);
        _lblPasswordStatus.Font = AppTheme.BodyFont;
        _lblPasswordStatus.Location = new Point(Inset, UiLayout.Below(btnSet, UiLayout.RowGap));

        // Two lines' worth reserved, whichever message is showing, so the
        // hint below never has to move when the password is set or removed.
        _lblPasswordStatus.MinimumSize = new Size(0, AppTheme.BodyFont.Height * 2);
        RefreshPasswordStatus();

        var lblHint = new Label
        {
            Text = $"At least {MinPasswordLength} characters. Keep it somewhere safe away from this PC - it can't be recovered, and a backup restored on another PC needs it to bring the keys and the QR check-in key across.",
            AutoSize = true,
            MaximumSize = new Size(width - Inset * 2, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
            Location = new Point(Inset, UiLayout.Below(_lblPasswordStatus, UiLayout.RowGap)),
        };

        group.Controls.AddRange(new Control[]
        {
            lblPassword, _txtBackupPassword,
            lblConfirm, _txtBackupPasswordConfirm,
            btnSet, btnRemove,
            _lblPasswordStatus, lblHint,
        });

        group.Size = new Size(width, UiLayout.Below(lblHint, Inset));

        return group;
    }

    private void RefreshPasswordStatus()
    {
        var isSet = !string.IsNullOrEmpty(_settings.BackupPassword);

        _lblPasswordStatus.Text = isSet
            ? "Keys and tokens travel with backups, locked with this password."
            : "No backup password: backups don't carry keys - after restoring on another PC you'd re-enter them and reprint QR check-ins.";

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

        AppDialog.Show(this,
            "Backup password set. Backups made from now on carry the keys, tokens and QR check-in key, locked with it - backups made before don't.\n\n" +
            "Write it down and keep it away from this PC. Without it, a backup restored on another PC comes back without its keys.",
            "Backup password set", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

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
    /// The backups list, the two buttons that act on a selected one, and
    /// the status line.
    ///
    /// Stretches sideways with the page, like the three boxes above it, but
    /// is NOT anchored to the bottom.
    ///
    /// It used to be: the box and the list inside it were both anchored
    /// top-to-bottom so they would fill the window. On a page that scrolls
    /// that does not mean what it looks like it means - a Bottom anchor
    /// pins a control to the bottom of the SCROLLING area, not to the
    /// bottom of what can be seen. So the box grew past the window's edge,
    /// taking the Restore and Delete buttons under the list with it, and no
    /// margin below could ever show through: there was always more box.
    /// The page scrolls, so the height comes from the contents instead -
    /// see LayoutListGroup.
    /// </summary>
    private GroupBox BuildListGroup(int width)
    {
        _grpList.Text = "Existing Backups";
        _grpList.Font = AppTheme.BodyFont;
        _grpList.Width = width;
        _grpList.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _btnRestoreSelected.Text = "Restore Selected";
        _btnRestoreSelected.Click += BtnRestoreSelected_Click;

        _btnDeleteSelected.Text = "Delete Selected";
        _btnDeleteSelected.Click += BtnDeleteSelected_Click;

        // Monospaced, so the date / size / name columns line up - sized off
        // the body font so it still tracks Ctrl+/Ctrl- like everything else.
        _lstBackups.Font = new Font("Consolas", Math.Max(7.5F, AppTheme.BodyFont.SizeInPoints - 2F));

        // Off, or the list quietly rounds its own height down to a whole
        // number of rows and leaves a gap under it that nothing explains.
        _lstBackups.IntegralHeight = false;

        // Sideways with the box, so a long backup file name has the width
        // of the page to be read in rather than the width the page happened
        // to be built at.
        _lstBackups.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = AppTheme.TextMuted;

        _grpList.Controls.AddRange(new Control[] { _lstBackups, _btnRestoreSelected, _btnDeleteSelected, _lblStatus });

        LayoutListGroup();

        return _grpList;
    }

    /// <summary>
    /// Sizes the list to the backups it is actually holding, puts the
    /// buttons and the status line under it, and trims the box to fit.
    ///
    /// Re-run whenever the list changes. A fixed number of rows meant four
    /// backups sat above six rows of nothing with the buttons stranded at
    /// the bottom of it - and this box is the last thing on the page, so
    /// growing and shrinking it disturbs nothing above.
    /// </summary>
    private void LayoutListGroup()
    {
        var inner = Math.Max(120, _grpList.Width - Inset * 2);

        // ItemHeight is font-derived and answers before the handle exists,
        // but a list that has not measured itself yet can report nothing -
        // hence the font as the floor.
        var rowHeight = Math.Max(_lstBackups.ItemHeight, _lstBackups.Font.Height);
        var rows = Math.Clamp(_lstBackups.Items.Count, MinBackupRows, MaxBackupRows);

        _lstBackups.Location = new Point(Inset, UiLayout.InsideGroup(_grpList));
        _lstBackups.Size = new Size(inner, rowHeight * rows + 4);

        UiLayout.ButtonRow(Inset, UiLayout.Below(_lstBackups, UiLayout.RowGap), _btnRestoreSelected, _btnDeleteSelected);

        _lblStatus.MaximumSize = new Size(inner, 0);
        _lblStatus.Location = new Point(Inset, UiLayout.Below(_btnRestoreSelected, UiLayout.RowGap));

        // Two lines' worth kept for the status line whether or not it is
        // saying anything: it reports file names and error messages, both of
        // which wrap, and a box that changed height as it reported would
        // jump under the pointer.
        _grpList.Height = _lblStatus.Top + AppTheme.BodyFont.Height * 2 + Inset;
    }

    /// <summary>
    /// Looks for each cloud service's folder on this PC again (it can be
    /// installed or signed into while the app is open) and says what was
    /// found. The boxes stay clickable either way - greyed-out text was
    /// unreadable on the dark looks, and the line for each service already
    /// says what it needs.
    /// </summary>
    private void RefreshCloudStatus()
    {
        var lines = new List<string>();

        foreach (var (service, check) in _cloudChecks)
        {
            var root = CloudBackup.FindSyncRoot(service);

            if (root is not null)
            {
                if (check.Checked)
                {
                    lines.Add($"{CloudBackup.NameOf(service)}: copies go to {Path.Combine(root, CloudBackup.FolderName)}");
                }
            }
            else if (check.Checked)
            {
                lines.Add($"{CloudBackup.NameOf(service)}: not found on this PC - install and sign in to {CloudBackup.AppNameOf(service)}, then press Check Again.");
            }
            else
            {
                lines.Add($"{CloudBackup.NameOf(service)}: needs {CloudBackup.AppNameOf(service)} installed and signed in on this PC.");
            }
        }

        lines.Add("Copies upload whenever the laptop is online. Set a backup password below to include your keys, locked.");
        _lblCloudStatus.Text = string.Join(Environment.NewLine, lines);
    }

    /// <summary>Sizes the folder box to its path (within the group) and puts Change... right after it.</summary>
    private void LayOutFolderRow()
    {
        var textWidth = TextRenderer.MeasureText(_txtFolder.Text, _txtFolder.Font).Width + 16;
        var room = _folderRowMaxRight - _btnChangeFolder.Width - UiLayout.LabelGap - _txtFolder.Left;

        _txtFolder.Width = Math.Clamp(textWidth, Math.Min(260, room), Math.Max(120, room));
        _btnChangeFolder.Left = _txtFolder.Right + UiLayout.LabelGap;
    }

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
        LayOutFolderRow();
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

        var results = await Task.Run(() => CloudBackup.CopyToTargets(path, clouds, keep));

        if (!IsDisposed && !Disposing)
        {
            _lblStatus.Text = made + " " + string.Join(" ", results);
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

        var confirm = AppDialog.Show(this,
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

        // Set the moment the restore reaches the point of no return. After
        // that the app restarts whatever happens, success or not.
        var filesTouched = false;

        void BeforeReplacing()
        {
            filesTouched = true;

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
            result = BackupService.RestoreBackup(zipPath, CurrentFolder(), BeforeReplacing);
        }
        catch (Exception ex)
        {
            if (!filesTouched)
            {
                // Refused or couldn't unpack: the data folder is exactly as
                // it was, and the app carries on as if nothing happened.
                AppDialog.Show(this, $"Restore failed: {ex.Message}", "Restore failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Should not happen - the restore reports rather than throws once
            // files are going back - but if it does, restarting is still the
            // only safe thing to do.
            AppDialog.Show(this,
                $"The restore stopped partway through: {ex.Message}\n\nThe app will now restart. If things look wrong afterwards, restore the safety backup in the \"Pre-Restore Safety Backups\" folder inside the backup folder.",
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

        LayoutListGroup();
    }

    private void RefreshLastBackupLabel()
    {
        _lblLastBackup.Text = _settings.LastAutoBackupUtc is { } last
            ? $"Last backup: {last.ToLocalTime():yyyy-MM-dd HH:mm}"
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

    private sealed record BackupListItem(FileInfo File)
    {
        public override string ToString() =>
            $"{File.LastWriteTime:yyyy-MM-dd HH:mm}   {File.Length / 1024.0:0.#} KB   {File.Name}";
    }
}
