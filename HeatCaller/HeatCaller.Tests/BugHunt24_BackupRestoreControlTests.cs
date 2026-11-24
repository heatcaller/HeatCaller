namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, the Backup &amp; Restore page: a refused cloud tick that wiped
/// the red "Copy failed" light, a locked-backup restore whose password prompt
/// sat after the last race/backup check, and a too-short standby PIN whose
/// refusal was overwritten two seconds later.
/// </summary>
public sealed class BugHunt24_BackupRestoreControlTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- F055: a refused cloud tick keeps the recorded problem -------------

    [Fact]
    public void Refused_cloud_tick_unticking_itself_keeps_the_copy_failed_problem()
    {
        var at = new DateTime(2026, 11, 22, 20, 0, 0, DateTimeKind.Utc);
        var settings = new AppSettings
        {
            CloudBackupOneDrive = true,
            CloudBackupGoogleDrive = false,
            LastCloudBackupUtc = at,
            LastCloudBackupProblem = "Not copied to OneDrive: set a backup password first.",
        };

        // The box's own untick after the refusal: Google Drive was never on.
        var changed = BackupRestoreControl.ApplyCloudTick(settings, CloudService.GoogleDrive, nowChecked: false);

        Assert.False(changed);
        Assert.False(settings.CloudBackupGoogleDrive);
        Assert.Equal("Not copied to OneDrive: set a backup password first.", settings.LastCloudBackupProblem);
        Assert.Equal(at, settings.LastCloudBackupUtc);
    }

    [Fact]
    public void Really_unticking_a_service_still_clears_the_problem()
    {
        var settings = new AppSettings
        {
            CloudBackupOneDrive = true,
            LastCloudBackupUtc = new DateTime(2026, 11, 22, 20, 0, 0, DateTimeKind.Utc),
            LastCloudBackupProblem = "Not copied to OneDrive: nope.",
        };

        var changed = BackupRestoreControl.ApplyCloudTick(settings, CloudService.OneDrive, nowChecked: false);

        Assert.True(changed);
        Assert.False(settings.CloudBackupOneDrive);
        Assert.Equal(string.Empty, settings.LastCloudBackupProblem);
        Assert.Null(settings.LastCloudBackupUtc);
    }

    // --- F056: checked again after the password prompt ---------------------

    [Fact]
    public void Locked_restore_is_refused_when_a_backup_starts_while_the_password_is_asked()
    {
        var prompted = false;
        var restored = 0;

        var finished = BackupRestoreControl.FinishRestore(
            isLocked: true,
            savedPasswordThatOpens: () => null,
            askForPassword: () =>
            {
                prompted = true;
                return "their password";
            },
            // The automatic backup starts while the prompt is open.
            canRestoreNow: () => !prompted,
            runRestore: _ => restored++);

        Assert.False(finished);
        Assert.Equal(0, restored);
    }

    [Fact]
    public void Locked_restore_goes_ahead_with_the_typed_password_when_nothing_started()
    {
        string? used = null;

        var finished = BackupRestoreControl.FinishRestore(
            isLocked: true,
            savedPasswordThatOpens: () => null,
            askForPassword: () => "their password",
            canRestoreNow: () => true,
            runRestore: p => used = p);

        Assert.True(finished);
        Assert.Equal("their password", used);
    }

    [Fact]
    public void Locked_restore_opened_by_the_saved_password_asks_nothing()
    {
        var asked = false;
        var restores = 0;

        var finished = BackupRestoreControl.FinishRestore(
            isLocked: true,
            savedPasswordThatOpens: () => "this laptop",
            askForPassword: () =>
            {
                asked = true;
                return null;
            },
            canRestoreNow: () => true,
            runRestore: _ => restores++);

        Assert.True(finished);
        Assert.False(asked);
        Assert.Equal(1, restores);
    }

    [Fact]
    public void Locked_restore_with_the_prompt_cancelled_does_not_restore()
    {
        var restores = 0;

        var finished = BackupRestoreControl.FinishRestore(
            isLocked: true,
            savedPasswordThatOpens: () => null,
            askForPassword: () => null,
            canRestoreNow: () => true,
            runRestore: _ => restores++);

        Assert.False(finished);
        Assert.Equal(0, restores);
    }

    // --- F057: the standby PIN refusal stays said --------------------------

    [Fact]
    public void Too_short_standby_pin_left_in_the_box_is_still_said_to_be_refused()
    {
        var line = BackupRestoreControl.StandbyPinRefusal("abc", "abcd1234", typing: false);

        Assert.NotNull(line);
        Assert.Contains("not saved", line);
        Assert.Contains("old PIN is still the one in use", line);
    }

    [Fact]
    public void Standby_pin_refusal_is_quiet_when_there_is_nothing_wrong()
    {
        Assert.Null(BackupRestoreControl.StandbyPinRefusal("abcd1234", "abcd1234", typing: false));
        Assert.Null(BackupRestoreControl.StandbyPinRefusal("efgh5678", "abcd1234", typing: false));
        Assert.Null(BackupRestoreControl.StandbyPinRefusal(string.Empty, "abcd1234", typing: false));
        // Half typed: not nagged until the box is left.
        Assert.Null(BackupRestoreControl.StandbyPinRefusal("abc", "abcd1234", typing: true));
    }

    [Fact]
    public void Standby_pin_with_a_symbol_and_no_saved_pin_is_refused_without_an_old_one()
    {
        var line = BackupRestoreControl.StandbyPinRefusal("abcd-1234", string.Empty, typing: false);

        Assert.NotNull(line);
        Assert.Contains("not saved", line);
        Assert.DoesNotContain("old PIN", line);
    }
}
