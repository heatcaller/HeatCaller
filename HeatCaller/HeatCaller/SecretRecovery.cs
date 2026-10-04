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

    // Set when Run leaves the locked settings and secrets.hgk for the next
    // launch rather than deciding anything (see Run). Pending then stays
    // true for the rest of the session.
    private static bool _postponed;

    /// <summary>
    /// True from a locked startup until RunIfNeeded has finished - dialog
    /// included - and for the whole session when it put recovery off to the
    /// next launch. Anything that would MAKE a secret in that window (a new
    /// set of racer PINs, emailed out) waits, or it would hand out values
    /// the recovery is about to replace - and, while the settings are still
    /// locked, re-seal them with only that one value in them.
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
            SessionLog.Write($"[secrets] Recovery failed: {ex.Message}");

            // Failed part-way with the settings still locked: treated as put
            // off, for the same reason as the postponed paths in Run.
            _postponed |= settings.SecretsLocked;
            return false;
        }
        finally
        {
            // Only resolved when something was actually decided. This used to
            // be set on every return, including the two that deliberately
            // leave the settings locked and secrets.hgk on disk for the next
            // launch. Pending then went false, the two-second roster tick
            // minted a racer PIN for everybody, and the save re-sealed the
            // settings with ONLY those PINs in them - the relay token, every
            // PIN, the email and backup passwords and the QR check-in key
            // gone, the lock cleared, and secrets.hgk deleted as no longer
            // needed on the following launch, without a word to the club.
            _resolved = !_postponed;
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

        // settings.json could not be read this launch, so the settings here
        // are defaults that only LOOK unlocked, and nothing can be saved to
        // the file anyway - see AppSettings.LoadedUnreadable. Everything
        // below would act on that guess: delete secrets.hgk as no longer
        // needed, or tell the club its keys are gone. Left for the next
        // launch that can read the file.
        //
        // The same goes for voice-settings.json (VoiceSettingsStore.
        // LoadedUnreadable): its defaults carry no locked ElevenLabs key, so
        // this went on to recover only the app's keys - or, with
        // settings.json readable and unlocked, found nothing locked at all -
        // and then deleted secrets.hgk, the only copy of the ElevenLabs key
        // this PC could ever open. Nothing is lost by waiting: settings that
        // are still locked keep their sealed text on every save - but only
        // while nothing in them has been filled in, which is why Pending is
        // kept true for the rest of the session (see RunIfNeeded) - and the
        // file is asked about again on the next launch.
        if (settings.LoadedUnreadable || voice.LoadedUnreadable)
        {
            SessionLog.Write("[secrets] Recovery put off to the next launch: settings.json or voice-settings.json could not be read.");
            _postponed = true;
            return false;
        }

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

        // Read with retries, and a read that still fails is NOT a reason to
        // give up. This runs on the launch straight after a restore, seconds
        // after the restore wrote the file - just when an antivirus scan or
        // a sync tool may have it open. It used to go to GiveUp on the first
        // failed read, which deleted secrets.hgk, cleared the settings' lock
        // and saved them without their sealed keys, and told the club every
        // key, PIN and the QR check-in key needed entering again - all over
        // a lock that would have gone a moment later.
        //
        // So the file and the locked state are both left exactly as they
        // are, and the next launch asks again. Nothing is lost by waiting:
        // settings that are still locked keep their sealed text on every
        // save (the same reasoning as LoadedUnreadable above). That holds
        // only while nothing makes a secret, so recovery is NOT marked
        // resolved here: Pending stays true for this session and the racer
        // PINs, badge key and auto backup keep waiting. This used to let them
        // go, and the first racer PIN minted re-sealed the settings without
        // every other key.
        string envelope;
        try
        {
            envelope = SafeFile.ReadAllTextWithRetry(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppDialog.Show(owner,
                $"The backup's locked copy of the keys couldn't be opened just now ({ex.Message}).\n\n"
                + "Nothing has been lost: it has been left where it is, and HeatCaller will ask for the backup password "
                + "again the next time it starts. Until then the keys, tokens and PINs from the backup are not in use - leave them "
                + "blank rather than typing new ones in, or HeatCaller will take that as starting afresh and delete the backup's copy.\n\n"
                + "If this keeps happening, close anything that might have the HeatCaller data folder open (a backup or "
                + "sync tool is the usual cause) and restart HeatCaller.",
                "Keys not recovered yet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            SessionLog.Write($"[secrets] Recovery put off to the next launch: secrets.hgk could not be read ({ex.Message}).");
            _postponed = true;
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

        var voiceSaved = true;

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

            voiceSaved = voice.Save(profile);
        }

        // secrets.hgk is the only other copy of what was just put back, so
        // it goes only once settings.json actually holds the keys again.
        // Save reports a failed write (a sync or antivirus tool holding the
        // file, a full disk) as false rather than throwing, and this used to
        // delete the file regardless: the caller then restarts, the restarted
        // app finds settings.json still sealed to the old PC and no
        // secrets.hgk, and tells the club the keys cannot be recovered. A
        // brief lock is the usual cause, so it is tried again a few times
        // first.
        //
        // The voice key counts the same way: when only the voice key was
        // locked, the settings could save while the voice file did not, and
        // deleting secrets.hgk then lost the ElevenLabs key for good.
        if (!voiceSaved || !SaveWithRetry(settings))
        {
            // Kept, so the next launch asks for the password again. Still
            // true, so the caller restarts: every page was built with the
            // keys blank, and carrying on here would let them save those
            // blanks back once the file could be written - leaving
            // settings.json unsealed but empty, after which secrets.hgk is
            // thrown away as no longer needed.
            AppDialog.Show(
                owner,
                "The backup password was right, but the keys couldn't be saved to this PC - something may have the "
                + "settings file open, or the disk may be full.\n\nNothing has been lost: the backup's locked copy of the "
                + "keys has been kept. HeatCaller will now restart and ask for the backup password again.",
                "Keys not saved yet",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return true;
        }

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

            // Only a club that pointed the voice at a relay of its own has
            // a token there to lose; the rest borrow the one above. It was
            // left off this list (docs check, 30 Sep).
            if (!string.IsNullOrWhiteSpace(settings.VoiceRelayBaseUrl))
            {
                toEnter.Add("the voice relay's token, if it had its own (Voice Settings, under Voice relay)");
            }
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

    /// <summary>
    /// Saves the settings, trying again a few times over about a second when
    /// the write fails - a file held for a moment by a backup, sync or
    /// antivirus tool is the usual cause, and it is usually let go quickly.
    /// True once a save reached the disk.
    /// </summary>
    private static bool SaveWithRetry(AppSettings settings)
    {
        const int attempts = 4;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            if (settings.Save())
            {
                return true;
            }

            if (attempt < attempts)
            {
                Thread.Sleep(300);
            }
        }

        return false;
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
