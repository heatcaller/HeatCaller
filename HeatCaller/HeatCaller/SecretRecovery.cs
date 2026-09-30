using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Gets the keys, tokens and PINs back after a backup is restored onto a
/// different PC.
///
/// On disk every secret is sealed to the Windows account that saved it
/// (see SecretBox), so on a new laptop the restored settings open with
/// their secrets locked. A backup made while a backup password was set also
/// carries secrets.hgk - the same secrets locked with that password - and
/// a restore leaves it in the data folder. On the next launch this asks
/// for the password, puts the secrets back, re-seals them to this PC and
/// deletes the file.
///
/// Without that file (or the password) the secrets are gone for good, and
/// the most useful thing left to do is say plainly what needs entering
/// again - including that badges printed on the old PC won't scan, since
/// the badge key was one of them.
/// </summary>
internal static class SecretRecovery
{
    private const int MaxAttempts = 3;

    // Whether the secrets were locked when the stores were loaded. Anything
    // that makes a new secret before RunIfNeeded gets its turn - a fresh
    // badge key, a new set of racer PINs - re-seals the settings and so
    // clears SecretsLocked, which would make a restore onto a new PC look
    // like a restore onto this one. Remembered here so that can't happen.
    private static bool _settingsLockedAtStart;
    private static bool _voiceLockedAtStart;

    /// <summary>
    /// Notes whether the secrets were locked as loaded. Call straight after
    /// AppSettings.Load and VoiceSettingsStore.Load, before anything else
    /// runs. Optional - RunIfNeeded also checks the state as it stands then
    /// - but it closes the gap described above.
    /// </summary>
    public static void RememberStartupState(AppSettings settings, VoiceSettingsStore voice)
    {
        _settingsLockedAtStart = settings.SecretsLocked;
        _voiceLockedAtStart = voice.Current.ApiKeyLocked;
        _lockedAtStart = _settingsLockedAtStart || _voiceLockedAtStart;
    }

    private static bool _lockedAtStart;
    private static bool _resolved;

    /// <summary>
    /// True from a locked startup until RunIfNeeded has finished - dialog
    /// included. Anything that would MAKE a secret in that window (a new
    /// set of racer PINs, emailed out) waits, or it would hand out values
    /// the recovery is about to replace.
    /// </summary>
    public static bool Pending => _lockedAtStart && !_resolved;

    /// <summary>
    /// Deals with locked secrets and any secrets.hgk left by a restore.
    /// Shows a dialog only when there is something to recover or to tell.
    /// Never throws: nothing here is worth stopping the app opening.
    ///
    /// True when secrets were put back. Every page was built from the
    /// settings as they were at startup - with the secrets blank - and some
    /// save their boxes back (Connect writes the API key box, the relay and
    /// web server pages their token and PINs), which would blank the
    /// recovered values again. The caller restarts the app on true, as it
    /// does after a restore; everything is already saved by then.
    /// </summary>
    public static bool RunIfNeeded(IWin32Window owner, AppSettings settings, VoiceSettingsStore voice)
    {
        try
        {
            return Run(owner, settings, voice);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[secrets] Recovery failed: {ex}");
            return false;
        }
        finally
        {
            _resolved = true;
        }
    }

    private static bool Run(IWin32Window owner, AppSettings settings, VoiceSettingsStore voice)
    {
        var path = Path.Combine(BackupService.DataDirectory, BackupService.SecretsFileName);
        var hasFile = File.Exists(path);

        var settingsLocked = settings.SecretsLocked || _settingsLockedAtStart;
        var voiceLocked = voice.Current.ApiKeyLocked || _voiceLockedAtStart;

        // Once only - a second call must not act on the first call's state.
        _settingsLockedAtStart = false;
        _voiceLockedAtStart = false;

        if (!settingsLocked && !voiceLocked)
        {
            // Restored onto the PC that made the backup: everything opened
            // as it was, and the password-locked copy isn't needed.
            if (hasFile)
            {
                TryDelete(path);
            }

            return false;
        }

        if (!hasFile)
        {
            GiveUp(owner, settings, voice, settingsLocked, voiceLocked,
                "These settings were saved on another PC or Windows account, and no copy of the keys came with them - no backup password was set when the backup was made.");
            return false;
        }

        string envelope;
        try
        {
            envelope = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            GiveUp(owner, settings, voice, settingsLocked, voiceLocked,
                $"The backup's locked copy of the keys couldn't be read ({ex.Message}).");
            return false;
        }

        BackupSecrets? bundle = null;
        var typedPassword = string.Empty;
        var damaged = false;

        using (var dialog = new PasswordPromptForm(password =>
        {
            var plain = SecretBox.PasswordOpen(envelope, password);
            if (plain is null)
            {
                return false;
            }

            try
            {
                bundle = JsonSerializer.Deserialize<BackupSecrets>(plain);
            }
            catch (JsonException)
            {
                // The password was right (the text opened) but what's inside
                // isn't what a backup writes. Retrying won't help.
                damaged = true;
                return true;
            }

            damaged = bundle is null;
            typedPassword = password;
            return true;
        }))
        {
            var answer = dialog.ShowDialog(owner);

            if (answer != DialogResult.OK)
            {
                var why = answer == DialogResult.Abort
                    ? $"The backup password was entered wrongly {MaxAttempts} times."
                    : "The backup password was skipped.";

                GiveUp(owner, settings, voice, settingsLocked, voiceLocked,
                    why + " To try again, restore the same backup again and enter the password when asked.");
                return false;
            }
        }

        if (damaged || bundle is null)
        {
            GiveUp(owner, settings, voice, settingsLocked, voiceLocked,
                "The backup's locked copy of the keys opened but is damaged.");
            return false;
        }

        var notes = new List<string>();

        if (settingsLocked)
        {
            if (bundle.App is not null)
            {
                // Everything but the backup password from the file: the one
                // just typed is the one that is known to work.
                settings.ApplySecrets(bundle.App, includeBackupPassword: false);
                settings.BackupPassword = typedPassword;

                // The badge key is now the one the backup was made with. This
                // used to say "Printed badges still work." whatever happened,
                // which stopped being true once Reissue All Badges could make a
                // new key: restoring undoes a reissue made after the backup, so
                // badges printed since carry a key that is gone, and the ones
                // the reissue stopped scan again. Said first among the notes,
                // and only when the backup had a badge key - none is made until
                // online check-in is first switched on, and without one no badge
                // carries an online id.
                if (!string.IsNullOrWhiteSpace(settings.RelayCheckInSecret))
                {
                    notes.Add(
                        "QR check-ins work as they did when the backup was made. If Reissue All QR Check-ins was used after that, restoring "
                        + "has undone it: QR check-ins printed since won't scan, and the ones it stopped work again. Print the newer "
                        + "ones again, or reissue again and print every QR check-in.");
                }
            }
            else
            {
                notes.Add("The app's own keys weren't in the backup - enter the relay token, email password and PINs again, and reprint QR check-ins.");
                ClearSettingsLock(settings);
            }
        }
        else if (string.IsNullOrEmpty(settings.BackupPassword))
        {
            // Clearly the club's password; keep backups carrying the keys.
            settings.BackupPassword = typedPassword;
        }

        if (voiceLocked)
        {
            var profile = voice.Current;

            if (!string.IsNullOrEmpty(bundle.ElevenLabsKey))
            {
                // The old PC's sealed copy dropped first, or clearing the
                // key later would bring it back as a locked key.
                profile.ForgetApiKey();
                profile.ApiKey = bundle.ElevenLabsKey;
            }
            else
            {
                notes.Add("The ElevenLabs key wasn't in the backup - enter it again in Voice Settings.");
                profile.ForgetApiKey();
            }

            voice.Save(profile);
        }

        settings.Save();
        TryDelete(path);

        var message = "The keys, tokens and PINs from the backup are back, locked to this PC.\n\nHeatCaller will now restart to load them.";
        if (notes.Count > 0)
        {
            message += "\n\n" + string.Join("\n\n", notes);
        }

        AppDialog.Show(owner, message, "Keys recovered", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return true;
    }

    /// <summary>
    /// The secrets can't be had back. Says what to enter again, then clears
    /// the locked state so this is said once rather than on every launch -
    /// settings.json as it arrived was already kept aside by
    /// AppSettings.Load, and the backup itself still has everything.
    /// </summary>
    private static void GiveUp(
        IWin32Window owner,
        AppSettings settings,
        VoiceSettingsStore voice,
        bool settingsLocked,
        bool voiceLocked,
        string reason)
    {
        var toEnter = new List<string>();

        if (settingsLocked)
        {
            toEnter.Add("the relay token (online results and check-in)");
        }

        if (voiceLocked)
        {
            toEnter.Add("the ElevenLabs key (Voice Settings)");
        }

        if (settingsLocked)
        {
            toEnter.Add("the email app password (racer PINs)");
            toEnter.Add("the web server and check-in PINs");
            toEnter.Add("racers' personal check-in PINs (make a new set)");
        }

        var message = reason
            + "\n\nKeys and tokens are locked to the PC that saved them, so these need entering again:\n\n  - "
            + string.Join("\n  - ", toEnter);

        if (settingsLocked)
        {
            message += "\n\nThe QR check-in key couldn't be recovered either, so QR check-ins printed on the old PC won't scan - print new ones.";
        }

        message += "\n\nTo have keys travel with backups from now on, set a backup password on the Backup && Restore page.";

        // Past the dialog either way, so a failure to show it can't leave
        // the file behind to be asked about again.
        TryDelete(Path.Combine(BackupService.DataDirectory, BackupService.SecretsFileName));

        if (settingsLocked && settings.SecretsLocked)
        {
            ClearSettingsLock(settings);
            settings.Save();
        }

        if (voiceLocked && voice.Current.ApiKeyLocked)
        {
            var profile = voice.Current;
            profile.ForgetApiKey();
            voice.Save(profile);
        }

        AppDialog.Show(owner, message, "Keys need entering again", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>
    /// Drops the settings' unopenable secrets. Only ever called while they
    /// are still locked, when every secret in memory is empty anyway.
    /// </summary>
    private static void ClearSettingsLock(AppSettings settings)
    {
        if (settings.SecretsLocked)
        {
            settings.ApplySecrets(new AppSettings.SecretFields(), includeBackupPassword: true);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[secrets] Couldn't delete {path}: {ex.Message}");
        }
    }

    /// <summary>
    /// Asks for the backup password. The check runs inside the dialog, so a
    /// wrong password can be said so right there and typed again, up to
    /// <see cref="MaxAttempts"/> times. OK when it opened, Abort when the
    /// tries ran out, Cancel when skipped.
    ///
    /// Internal rather than private so the layout test can build it
    /// (WindowCatalog); only RunIfNeeded shows it.
    ///
    /// On the layout pieces (DialogFit): the heading, what the password is
    /// for, the box to type it in and two lines kept under the box for what
    /// went wrong, one under another across the window, and Unlock and Skip
    /// at the foot against the right-hand edge; as tall as that. It used to
    /// be placed in pixels: each line a fixed distance under the one above,
    /// the text wrapped at a fixed 440px, and the window sized to where the
    /// last of them ended up.
    /// </summary>
    internal sealed class PasswordPromptForm : Form
    {
        /// <summary>How wide the window is inside its frame at the normal text size - the 440px the text had, and 20px either side.</summary>
        private const int DialogWidth = 480;

        /// <summary>Lines kept for what went wrong, so a message appearing here never moves the buttons under the pointer.</summary>
        private const int ErrorLines = 2;

        private readonly Func<string, bool> _tryUnlock;
        private readonly TextBox _txtPassword = new();
        private readonly Label _lblError = new();
        private int _attempts;

        public PasswordPromptForm(Func<string, bool> tryUnlock)
        {
            _tryUnlock = tryUnlock;

            Text = "Backup password";
            Font = AppTheme.BodyFont;
            BackColor = AppTheme.Background;

            var lblCaption = new Label
            {
                Text = "Unlock the keys from this backup",
                AutoSize = true,
                Font = AppTheme.BoldFont,
            };

            var lblMessage = new Label
            {
                Text = "This backup came from another PC. Its API keys, tokens, PINs and QR check-in key are locked with the club's backup password.\n\n"
                    + "Enter it to bring them across. Skip to carry on without them - they would then need entering again, and QR check-ins reprinting.",
                AutoSize = true,
                Font = AppTheme.BodyFont,
            };

            _txtPassword.Font = AppTheme.BodyFont;
            _txtPassword.UseSystemPasswordChar = true;

            // Wraps to the width the page gives it; two lines kept for it
            // once the theme pass has left it in its face (see below).
            _lblError.AutoSize = true;
            _lblError.Font = AppTheme.BodyFont;

            var btnUnlock = new Button { Text = "Unlock" };
            btnUnlock.Click += (_, _) => TryUnlock();

            // DialogResult on the button closes the dialog on its own.
            var btnSkip = new Button { Text = "Skip", DialogResult = DialogResult.Cancel };

            var page = new FillPage();
            page.Add(lblCaption, gapBelow: UiLayout.RowGap);
            page.Add(lblMessage, gapBelow: UiLayout.RowGap);

            // Across the page, as the box was across the text above it; what
            // went wrong a hint's gap under it.
            page.Add(_txtPassword, gapBelow: UiLayout.HintGap);

            // A pixel either side of the box for the ring the theme draws round
            // it, outside it, on what holds it (Pieces.RingRoom): stretched
            // across the page, the box's sides would be the page's edges, and
            // the ring's sides would be drawn past them, where nothing shows.
            _txtPassword.Margin = new Padding(1, 0, 1, UiLayout.HintGap);

            // Its height is the box's own, and changes when the theme takes its
            // border off - see Pieces.KeepHeightTold.
            Pieces.KeepHeightTold(_txtPassword);

            page.Add(_lblError, gapBelow: UiLayout.RowGap);

            // Against the right-hand edge, Skip furthest right, as they were -
            // and Tab still reaches Unlock first: the bar takes them right to
            // left, in the order they were added.
            page.Add(new ButtonBar(alignRight: true).With(btnSkip, btnUnlock), gapBelow: 0);
            btnUnlock.TabIndex = 0;
            btnSkip.TabIndex = 1;

            Controls.Add(page);

            AcceptButton = btnUnlock;
            CancelButton = btnSkip;

            AppTheme.ApplyRecursively(this);

            // After the theming pass, which gives every label the ordinary
            // text colour.
            lblCaption.ForeColor = AppTheme.Primary;
            _lblError.ForeColor = AppTheme.Danger;

            // The window's own button in the app's colour, as Save and OK are
            // in every other window - the theme goes by the caption, and
            // "Unlock" is not one it knows, so it came out grey beside a grey
            // Skip.
            AppTheme.StyleButton(btnUnlock, AppTheme.ButtonKind.Primary);

            // In the face it wears, as the label draws its lines
            // (Pieces.LineHeight) - see ErrorLines.
            _lblError.MinimumSize = new Size(0, ErrorLines * Pieces.LineHeight(_lblError.Font) + _lblError.Padding.Vertical);

            DialogFit.Keep(this, page, width: DialogWidth);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _txtPassword.Focus();
        }

        private void TryUnlock()
        {
            var password = _txtPassword.Text;
            if (password.Length == 0)
            {
                _lblError.Text = "Type the backup password, or press Skip.";
                _txtPassword.Focus();
                return;
            }

            bool opened;
            var previous = Cursor.Current;
            try
            {
                // The password is stretched on purpose (to make guessing
                // slow), so this takes a noticeable moment.
                Cursor.Current = Cursors.WaitCursor;
                opened = _tryUnlock(password);
            }
            finally
            {
                Cursor.Current = previous;
            }

            if (opened)
            {
                DialogResult = DialogResult.OK;
                return;
            }

            _attempts++;
            if (_attempts >= MaxAttempts)
            {
                DialogResult = DialogResult.Abort;
                return;
            }

            var left = MaxAttempts - _attempts;
            _lblError.Text = $"That password doesn't open this backup. {Plural.Of(left, "try", "tries")} left.";
            _txtPassword.SelectAll();
            _txtPassword.Focus();
        }
    }
}
