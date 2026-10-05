using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 4, the data folder: photo-finish frames copied into every
/// backup, keys typed while secret recovery waits for the next launch, a
/// failed swap that took the old file away before its temp file was thrown
/// away too, and half-written backups that piled up after a backup was cut
/// off. Every backup here goes to a folder of the test's own, never
/// Documents.
/// </summary>
public sealed class BugHunt4DataTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private string Backups => Path.Combine(_data.Folder, "backups");

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static List<string> EntriesOf(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        return archive.Entries.Select(e => e.FullName).ToList();
    }

    // --- Photo-finish frames and backups (data-1) ------------------------

    private string Shot => Path.Combine(AppPaths.Root, PhotoFinish.FolderName, "2026-10-03", "0001");

    [Fact]
    public void Photo_finish_frames_are_left_out_of_every_backup()
    {
        Assert.True(new AppSettings { RaceWarmUpSeconds = 30 }.Save());
        Write(Path.Combine(Shot, "shot.json"), "{\"Id\":1}");
        Write(Path.Combine(Shot, "+0000.jpg"), "frame");
        Directory.CreateDirectory(Path.Combine(AppPaths.Root, PhotoFinish.FolderName, "2026-10-04"));

        var zip = BackupService.CreateBackup(Backups, false, null);

        var entries = EntriesOf(zip);
        Assert.Contains("settings.json", entries);
        Assert.DoesNotContain(entries, e => e.StartsWith("photo-finish", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_backup_made_before_frames_were_left_out_still_restores_them()
    {
        var zip = Path.Combine(_data.Folder, "older-backup.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (var (name, text) in new[]
                     {
                         ("settings.json", "{}"),
                         ("photo-finish/2026-09-26/0004/shot.json", "{\"Id\":4}"),
                         ("photo-finish/2026-09-26/0004/+0000.jpg", "old frame"),
                     })
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(text);
            }
        }

        // Tonight's frames on this laptop, which that backup never had.
        Write(Path.Combine(Shot, "+0000.jpg"), "tonight");

        var result = BackupService.RestoreBackup(zip, Backups);

        Assert.Empty(result.Failed);
        Assert.Equal("old frame", File.ReadAllText(Path.Combine(AppPaths.Root, "photo-finish", "2026-09-26", "0004", "+0000.jpg")));
        Assert.Equal("tonight", File.ReadAllText(Path.Combine(Shot, "+0000.jpg")));
    }

    // --- Keys typed while recovery waits (data-2) ------------------------

    private static string SealedTextIn(string settingsJson) =>
        JsonDocument.Parse(settingsJson).RootElement.GetProperty(nameof(AppSettings.Secrets)).GetString()!;

    [Fact]
    public void Keys_typed_while_recovery_is_held_do_not_replace_the_locked_ones()
    {
        // Sealed somewhere else: it cannot be opened here, so it is locked.
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"Secrets\":\"sealed-on-the-old-pc\"}")!;
        Assert.True(settings.SecretsLocked);

        settings.HoldLockedSecrets = true;
        settings.BackupPassword = "longpassword1";
        settings.WebServerCheckInPin = "1234";

        Assert.Equal("sealed-on-the-old-pc", SealedTextIn(JsonSerializer.Serialize(settings)));
        Assert.True(settings.SecretsLocked);

        // Still there in memory for this session.
        Assert.Equal("1234", settings.WebServerCheckInPin);
    }

    /// <summary>
    /// A backup restored onto this laptop - settings sealed on the old PC,
    /// and the backup's password-locked copy of the keys beside them - with
    /// voice-settings.json held by a sync tool at the first launch, so
    /// recovery is put off to the next one. The message that now says so
    /// throws rather than waiting for a click nobody can give here (see
    /// AppDialog.ThrowInsteadOfShowing), and recovery never lets that out.
    /// </summary>
    private AppSettings RecoveryPutOffForAnUnreadableVoiceFile()
    {
        File.WriteAllText(_data.PathOf("settings.json"), "{\"Secrets\":\"sealed-on-the-old-pc\"}");
        File.WriteAllText(_data.PathOf(BackupService.SecretsFileName), "{\"locked\":\"with the backup password\"}");
        File.WriteAllText(_data.PathOf("voice-settings.json"), "{}");

        var settings = AppSettings.Load();
        Assert.True(settings.SecretsLocked);

        VoiceSettingsStore voice;
        using (new FileStream(_data.PathOf("voice-settings.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            voice = VoiceSettingsStore.Load();
        }

        Assert.True(voice.LoadedUnreadable);

        AppDialog.ThrowInsteadOfShowing = true;
        try
        {
            Assert.False(SecretRecovery.RunIfNeeded(null!, settings, voice));
        }
        finally
        {
            AppDialog.ThrowInsteadOfShowing = false;
        }

        return settings;
    }

    [Fact]
    public void Recovery_put_off_for_an_unreadable_voice_file_keeps_the_locked_keys_through_a_save()
    {
        var settings = RecoveryPutOffForAnUnreadableVoiceFile();

        // What the Backup page's Set Password does that session.
        settings.BackupPassword = "longpassword1";
        Assert.True(settings.Save());

        Assert.Equal("sealed-on-the-old-pc", SealedTextIn(File.ReadAllText(_data.PathOf("settings.json"))));
        Assert.True(File.Exists(_data.PathOf(BackupService.SecretsFileName)));

        // So the next launch still finds them locked and asks for the password.
        Assert.True(AppSettings.Load().SecretsLocked);
    }

    [Fact]
    public void Recovery_put_off_for_an_unreadable_voice_file_says_that_keys_typed_meanwhile_are_not_kept()
    {
        var settings = RecoveryPutOffForAnUnreadableVoiceFile();

        // The "Keys not recovered yet" message, which only an unreadable
        // secrets.hgk used to show.
        Assert.Contains("isn't saved", SecretRecovery.PutOffNotice(settings));

        // And the Backup page's Set Password no longer says the password is set for good.
        Assert.Contains("this session only", BackupRestoreControl.SessionOnlyPasswordMessage(settings));
    }

    [Fact]
    public void Settings_that_opened_here_keep_what_is_typed_and_say_nothing_about_it()
    {
        // Held, as an unreadable file still puts recovery off, but not locked.
        var settings = new AppSettings { HoldLockedSecrets = true };

        Assert.Null(SecretRecovery.PutOffNotice(settings));
        Assert.Null(BackupRestoreControl.SessionOnlyPasswordMessage(settings));
    }

    [Fact]
    public void Keys_recovered_claims_only_what_came_back_when_the_backup_had_none_of_the_app_s_keys()
    {
        var withoutAppKeys = new BackupSecrets { App = null, ElevenLabsKey = "el-key" };

        var voiceBack = SecretRecovery.RecoveredOpening(settingsLocked: true, voiceLocked: true, withoutAppKeys);
        Assert.DoesNotContain("tokens and PINs", voiceBack);
        Assert.Contains("ElevenLabs key from the backup is back", voiceBack);

        // The voice settings opened here, so the backup's ElevenLabs key wasn't put back either.
        var nothingBack = SecretRecovery.RecoveredOpening(settingsLocked: true, voiceLocked: false, withoutAppKeys);
        Assert.DoesNotContain("is back", nothingBack);
        Assert.DoesNotContain("are back", nothingBack);

        // A backup that had them still says so.
        Assert.Contains(
            "keys, tokens and PINs from the backup are back",
            SecretRecovery.RecoveredOpening(settingsLocked: true, voiceLocked: false, new BackupSecrets { App = new AppSettings.SecretFields() }));
    }

    // --- A swap that took the old file away (data-4) ---------------------

    /// <summary>
    /// What Windows' ReplaceFile does when it fails with
    /// ERROR_UNABLE_TO_MOVE_REPLACEMENT: the file being replaced is already
    /// gone, and the new text is only in the temp file.
    /// </summary>
    private static void ReplaceTakesTheOldFileAndFails(string temp, string path)
    {
        File.Delete(path);
        throw new IOException("Unable to move the replacement file to the file to be replaced.");
    }

    private static void WithReplace(Action<string, string> replace, Action test)
    {
        var real = SafeFile.ReplaceFile;
        SafeFile.ReplaceFile = replace;

        try
        {
            test();
        }
        finally
        {
            SafeFile.ReplaceFile = real;
        }
    }

    private static string[] TempFilesIn(string folder) => Directory.GetFiles(folder, "*.tmp");

    [Fact]
    public void A_save_whose_swap_took_the_old_file_away_still_lands()
    {
        var path = _data.PathOf("race-history.json");
        Assert.True(SafeFile.WriteAllText(path, "old"));

        WithReplace(ReplaceTakesTheOldFileAndFails, () => Assert.True(SafeFile.WriteAllText(path, "new")));

        Assert.Equal("new", File.ReadAllText(path));
        Assert.Empty(TempFilesIn(AppPaths.Root));
    }

    [Fact]
    public void A_swap_that_failed_with_the_old_file_still_there_leaves_it_as_it_was()
    {
        var path = _data.PathOf("race-history.json");
        Assert.True(SafeFile.WriteAllText(path, "old"));

        WithReplace(
            (_, _) => throw new IOException("Unable to remove the file to be replaced."),
            () => Assert.False(SafeFile.WriteAllText(path, "new")));

        Assert.Equal("old", File.ReadAllText(path));
        Assert.Empty(TempFilesIn(AppPaths.Root));
    }

    [Fact]
    public void The_race_journal_s_swap_moves_the_new_file_in_when_the_old_one_was_taken_away()
    {
        // RaceJournal's quiet retry swaps its temp file in through SwapIn.
        var path = _data.PathOf("race-in-progress.json");
        File.WriteAllText(path, "old");
        var temp = path + ".retry.tmp";
        File.WriteAllText(temp, "new");

        WithReplace(ReplaceTakesTheOldFileAndFails, () => SafeFile.SwapIn(temp, path));

        Assert.Equal("new", File.ReadAllText(path));
        Assert.False(File.Exists(temp));
    }

    /// <summary>
    /// As ReplaceTakesTheOldFileAndFails, and the empty place can't be moved
    /// into either: a folder of the same name stands in for the temp file
    /// still being held, and File.Exists still says there is no file.
    /// </summary>
    private static void ReplaceTakesTheOldFileAndTheMoveInFailsToo(string temp, string path)
    {
        File.Delete(path);
        Directory.CreateDirectory(path);
        throw new IOException("Unable to move the replacement file to the file to be replaced.");
    }

    [Fact]
    public void A_save_that_lost_the_old_file_and_could_not_move_in_keeps_the_new_text_under_the_temp_name()
    {
        var path = _data.PathOf("race-history.json");
        Assert.True(SafeFile.WriteAllText(path, "old"));

        WithReplace(ReplaceTakesTheOldFileAndTheMoveInFailsToo, () => Assert.False(SafeFile.WriteAllText(path, "new")));

        // The only copy of either: it used to be deleted here.
        Assert.False(File.Exists(path));
        var temp = Assert.Single(TempFilesIn(AppPaths.Root));
        Assert.Equal("new", File.ReadAllText(temp));
    }

    [Fact]
    public void The_race_journal_s_quiet_retry_keeps_the_new_journal_when_it_could_not_move_in()
    {
        var path = _data.PathOf("race-in-progress.json");
        File.WriteAllText(path, "old");

        var tryWriteQuietly = typeof(RaceJournal).GetMethod("TryWriteQuietly", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? written = null;
        WithReplace(ReplaceTakesTheOldFileAndTheMoveInFailsToo, () => written = tryWriteQuietly.Invoke(null, new object[] { path, "new" }));

        Assert.Equal(false, written);
        var temp = Assert.Single(TempFilesIn(AppPaths.Root));
        Assert.Equal("new", File.ReadAllText(temp));
    }

    // --- Half-written backups left behind (data-5) -----------------------

    private static string Leftover(string folder, string name, DateTime lastWriteUtc)
    {
        var path = Path.Combine(folder, name);
        Write(path, "half a backup");
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    [Fact]
    public void A_half_written_backup_left_by_an_earlier_run_is_cleared_away()
    {
        Assert.True(new AppSettings().Save());
        var stale = Leftover(Backups, "HeatCaller-AutoBackup-2020-01-01_00-00-00.zip.tmp", DateTime.UtcNow.AddDays(-2));
        var staleManual = Leftover(Backups, "HeatCaller-Backup-2020-01-02_00-00-00.zip.tmp", DateTime.UtcNow.AddDays(-2));

        // Not old enough to be sure nothing is still writing it, and not one of ours.
        var recent = Leftover(Backups, "HeatCaller-AutoBackup-2020-01-03_00-00-00.zip.tmp", DateTime.UtcNow);
        var someoneElses = Leftover(Backups, "Holiday-Photos.zip.tmp", DateTime.UtcNow.AddDays(-2));

        var zip = BackupService.CreateBackup(Backups, true, null);

        Assert.True(File.Exists(zip));
        Assert.False(File.Exists(stale));
        Assert.False(File.Exists(staleManual));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(someoneElses));
    }

    [Fact]
    public void A_half_copied_cloud_backup_left_by_an_earlier_run_is_cleared_away()
    {
        var cloud = Path.Combine(_data.Folder, "cloud", CloudBackup.FolderName);
        var stale = Leftover(cloud, "HeatCaller-AutoBackup-2020-01-01_00-00-00.zip.partial", DateTime.UtcNow.AddDays(-2));
        var recent = Leftover(cloud, "HeatCaller-AutoBackup-2020-01-03_00-00-00.zip.partial", DateTime.UtcNow);

        var backup = Path.Combine(_data.Folder, "local", "HeatCaller-AutoBackup-2026-10-04_20-00-00.zip");
        Write(backup, "a backup");

        var results = CloudBackup.CopyToTargets(
            backup,
            new[] { new CloudBackup.Target(CloudService.OneDrive, "OneDrive", cloud) },
            keepAutomatic: 14,
            isRehearsal: false);

        Assert.Equal(new[] { "Copied to OneDrive." }, results);
        Assert.True(File.Exists(Path.Combine(cloud, Path.GetFileName(backup))));
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(recent));
    }
}
