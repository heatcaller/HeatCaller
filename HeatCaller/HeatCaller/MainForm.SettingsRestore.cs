namespace HeatCaller;

/// <summary>
/// MainForm's side of Restore Default Settings and Put Back Earlier
/// Settings (1 Nov 2026, round 4), from the Default settings card on Backup
/// &amp; Restore. Simple and Advanced mode, which these came with, were taken
/// out on 18 Nov 2026: every setting shows, and Desk View is how a laptop
/// handed to a helper is kept away from them.
/// </summary>
public partial class MainForm
{
    /// <summary>A race, or practice, under way - when a restore, which restarts, is not offered.</summary>
    private bool RaceUnderWayForRestore() =>
        _race.IsPracticeMode || _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing;

    /// <summary>
    /// A restore's dialog shown and answered, then the race checked again
    /// (10 Nov 2026, bug hunt 16): the dialog greys its button only for a
    /// race running when it opens, and practice can start at the gate while
    /// it is on screen. Restoring then restarted over it - frozen first, so
    /// the practice and its laps could not be saved on the way out. Now it is
    /// said and refused, as the line-up window does. True only when the
    /// dialog was confirmed and nothing has started since.
    /// </summary>
    internal static bool ConfirmedWithNothingStarted(Func<bool> showAndConfirm, Func<bool> raceUnderWay, Action sayStarted)
    {
        if (!showAndConfirm())
        {
            return false;
        }

        if (raceUnderWay())
        {
            sayStarted();
            return false;
        }

        return true;
    }

    /// <summary>What a restore says when a practice or race started while its dialog was open.</summary>
    internal const string StartedWhileOpenLine =
        "A practice or race started while the window was open, so nothing was changed. "
        + "Try again once it has finished - this restarts HeatCaller, which would end it.";

    /// <summary>
    /// Restore Default Settings (round 4): asks (RestoreDefaultsForm), saves
    /// what every store holds so the copy is current, zips the settings files
    /// into Settings Backups, puts back the tuning settings in the files
    /// themselves (SettingsRestore) and restarts - frozen first, so nothing
    /// the running app holds is saved back over it on the way out. UI thread.
    /// </summary>
    internal void RestoreDefaultSettings()
    {
        if (_settings.LoadedUnreadable)
        {
            AppDialog.Show(this, "HeatCaller couldn't read its settings file this time, so it won't change it. Restart HeatCaller first.",
                SettingsRestore.ConfirmTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        bool alsoShown = false;

        if (!ConfirmedWithNothingStarted(
            () =>
            {
                using var form = new RestoreDefaultsForm(RaceUnderWayForRestore());

                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return false;
                }

                alsoShown = form.AlsoShown;
                return true;
            },
            RaceUnderWayForRestore,
            () => AppDialog.Show(this, StartedWhileOpenLine,
                SettingsRestore.ConfirmTitle, MessageBoxButtons.OK, MessageBoxIcon.Information)))
        {
            return;
        }

        SaveEverySettingsStore();

        string copy;

        try
        {
            copy = SettingsRestore.SaveCopy(AppPaths.Root, DateTime.Now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AppDialog.Show(this, "HeatCaller couldn't save a copy of the settings, so nothing was changed.\r\n\r\n" + ex.Message,
                SettingsRestore.ConfirmTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // 2026-11-05, Bug hunt 11: the reset caught on its own, JsonException
        // too. "Nothing was changed" is true only of the copy failing; once
        // ResetFiles has begun writing, some files may be reset, so HeatCaller
        // says so and restarts anyway - running on, it would save its old
        // values back over the files that were.
        // 5 Nov 2026, Bug hunt 11 review: ResetFiles reads and resets every
        // file before writing any, so a failure there (a locked file) comes
        // before anything is written - "nothing was changed", and no restart.
        // Only PartlyWrittenException, thrown once a file has been written,
        // means the files are mixed.
        try
        {
            var changed = SettingsRestore.ResetFiles(AppPaths.Root, alsoShown);

            SessionLog.Write($"[settings] Restored default settings{(alsoShown ? ", with the everyday settings too" : string.Empty)}: "
                + $"{changed.Count} changed. Copy saved as {Path.GetFileName(copy)}.");
        }
        catch (SettingsRestore.PartlyWrittenException ex)
        {
            SessionLog.Write($"[settings] Restore default settings stopped part-way: {ex.Message} Copy saved as {Path.GetFileName(copy)}.");
            AppDialog.Show(this, "HeatCaller couldn't put back every setting, so some may be reset and some not. "
                + "Your settings were saved first: Put Back Earlier Settings puts them back. HeatCaller restarts now.\r\n\r\n" + ex.Message,
                SettingsRestore.ConfirmTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            SessionLog.Write($"[settings] Restore default settings changed nothing: {ex.Message} Copy saved as {Path.GetFileName(copy)}.");
            AppDialog.Show(this, "HeatCaller couldn't restore the default settings, so nothing was changed.\r\n\r\n" + ex.Message,
                SettingsRestore.ConfirmTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        RestartForRestoredSettings();
    }

    /// <summary>
    /// Put Back Earlier Settings (round 4): the copies, newest first; the one
    /// picked goes back after the settings as they are now are saved as a
    /// copy too, and HeatCaller restarts. UI thread.
    /// </summary>
    internal void PutBackEarlierSettings()
    {
        string picked = string.Empty;

        if (!ConfirmedWithNothingStarted(
            () =>
            {
                using var form = new PutBackSettingsForm(SettingsRestore.Copies(AppPaths.Root), RaceUnderWayForRestore());

                if (form.ShowDialog(this) != DialogResult.OK || form.Picked is not { } path)
                {
                    return false;
                }

                picked = path;
                return true;
            },
            RaceUnderWayForRestore,
            () => AppDialog.Show(this, StartedWhileOpenLine,
                "Put back earlier settings", MessageBoxButtons.OK, MessageBoxIcon.Information)))
        {
            return;
        }

        SaveEverySettingsStore();

        try
        {
            // The settings as they are now, first - this is reversible too.
            // 2026-11-05, Bug hunt 11: one call that reads the picked copy
            // before that save can prune it (it was the oldest of ten).
            SettingsRestore.PutBackWithCopy(AppPaths.Root, picked, DateTime.Now);

            SessionLog.Write($"[settings] Put back the settings saved in {Path.GetFileName(picked)}.");
        }
        catch (SettingsRestore.PartlyWrittenException ex)
        {
            // 5 Nov 2026, Bug hunt 11 review: the files are written one at a
            // time. Once one is written, "couldn't put those settings back"
            // with no restart left the running app to save its old values
            // over the files already put back - mixed settings, and the club
            // told nothing changed. Said as it is, and restarted, as Restore
            // Default Settings does; the copy saved first undoes it.
            SessionLog.Write($"[settings] Put back of {Path.GetFileName(picked)} stopped part-way: {ex.Message}");
            AppDialog.Show(this, "HeatCaller couldn't put back every setting, so some may be put back and some not. "
                + "Your settings were saved first, as the newest copy: Put Back Earlier Settings puts them back. HeatCaller restarts now.\r\n\r\n" + ex.Message,
                "Put back earlier settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AppDialog.Show(this, "HeatCaller couldn't put those settings back.\r\n\r\n" + ex.Message,
                "Put back earlier settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        RestartForRestoredSettings();
    }

    /// <summary>Every store's settings written as they stand, so a copy taken now is the settings in use.</summary>
    private void SaveEverySettingsStore()
    {
        _settings.Save();
        _voiceStore.Save(_voiceStore.Current);
        _musicStore.Save();
        _ambienceStore.Save();
    }

    /// <summary>
    /// The files are written: freeze every write the closing app would make
    /// over them, and start again - as a backup restore does
    /// (BackupRestoreControl.RestoreCompleted).
    /// </summary>
    private void RestartForRestoredSettings()
    {
        SafeFile.Freeze();
        Application.Restart();
        Environment.Exit(0);
    }
}
