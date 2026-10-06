namespace HeatCaller.Tests;

/// <summary>
/// The cloud backup light (CloudBackupStatus): every state a club's cloud
/// copies can be in, and which colour it gets. The judgement is handed the
/// settings and what the last copy did, so each case is pinned here without
/// a cloud account, a window or a backup.
/// </summary>
public sealed class CloudBackupStatusTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 20, 0, 0, DateTimeKind.Utc);

    private static CloudBackupStatus.Service Service(string name, bool folderFound = true) =>
        new(name, name, folderFound);

    private static CloudBackupStatus.State Assess(
        IReadOnlyList<CloudBackupStatus.Service> services,
        DateTime? lastSuccessUtc = null,
        string lastProblem = "") =>
        CloudBackupStatus.Assess(new CloudBackupStatus.Facts(services, lastSuccessUtc, lastProblem, Now));

    // --- Nothing switched on ----------------------------------------------

    [Fact]
    public void No_cloud_service_at_all_is_not_a_fault_and_says_how_to_turn_one_on()
    {
        var state = Assess(Array.Empty<CloudBackupStatus.Service>());

        Assert.Equal(MissionControl.Level.Off, state.Level);
        Assert.Equal("Off", state.Headline);
        Assert.Contains("stay on this PC only", state.Detail);
        Assert.Contains("Backup & Restore", state.Detail);
    }

    // --- Green -------------------------------------------------------------

    [Fact]
    public void A_copy_that_reached_the_cloud_recently_is_green_and_says_when()
    {
        var state = Assess(new[] { Service("OneDrive") }, lastSuccessUtc: Now.AddDays(-1));

        Assert.Equal(MissionControl.Level.Good, state.Level);
        Assert.Equal("Up to date", state.Headline);
        Assert.StartsWith("The last backup reached OneDrive on ", state.Detail);
    }

    [Fact]
    public void A_copy_the_day_before_it_counts_as_old_is_still_green()
    {
        var state = Assess(
            new[] { Service("OneDrive") },
            lastSuccessUtc: Now - CloudBackupStatus.OutOfDateAfter + TimeSpan.FromHours(1));

        Assert.Equal(MissionControl.Level.Good, state.Level);
    }

    [Fact]
    public void Every_service_switched_on_is_named_in_the_green_line()
    {
        var state = Assess(
            new[] { Service("Google Drive"), Service("iCloud Drive"), Service("OneDrive") },
            lastSuccessUtc: Now.AddHours(-2));

        Assert.Equal(MissionControl.Level.Good, state.Level);
        Assert.Contains("Google Drive, iCloud Drive and OneDrive", state.Detail);
    }

    // --- Red ---------------------------------------------------------------

    [Fact]
    public void A_service_whose_folder_has_gone_is_red_and_says_what_to_install()
    {
        var state = Assess(new[] { Service("OneDrive", folderFound: false) }, lastSuccessUtc: Now.AddDays(-1));

        Assert.Equal(MissionControl.Level.Problem, state.Level);
        Assert.Equal("Can't copy", state.Headline);
        Assert.Contains("No backup is reaching the cloud", state.Detail);
        Assert.Contains("OneDrive isn't installed or signed in on this PC", state.Detail);
        Assert.Contains("press Check Again", state.Detail);
    }

    [Fact]
    public void A_folder_that_has_gone_is_called_out_even_though_the_last_copy_worked()
    {
        // The last copy did work - back when the folder was still linked.
        // The folder being gone is the thing to fix, so it is judged first.
        var fine = Assess(new[] { Service("OneDrive") }, lastSuccessUtc: Now.AddHours(-1));
        var gone = Assess(new[] { Service("OneDrive", folderFound: false) }, lastSuccessUtc: Now.AddHours(-1));

        Assert.Equal(MissionControl.Level.Good, fine.Level);
        Assert.Equal(MissionControl.Level.Problem, gone.Level);
    }

    [Fact]
    public void A_copy_that_failed_is_red_and_repeats_why()
    {
        var state = Assess(
            new[] { Service("OneDrive") },
            lastSuccessUtc: Now.AddDays(-3),
            lastProblem: "Not copied to OneDrive: There is not enough space on the disk.");

        Assert.Equal(MissionControl.Level.Problem, state.Level);
        Assert.Equal("Copy failed", state.Headline);
        Assert.Contains("There is not enough space on the disk.", state.Detail);
        Assert.Contains("Press Back Up Now", state.Detail);
    }

    // --- Amber -------------------------------------------------------------

    [Fact]
    public void One_service_of_two_missing_is_amber_because_a_copy_still_leaves_this_pc()
    {
        var state = Assess(
            new[] { Service("Google Drive"), Service("OneDrive", folderFound: false) },
            lastSuccessUtc: Now.AddDays(-1));

        Assert.Equal(MissionControl.Level.Watch, state.Level);
        Assert.Equal("Partly working", state.Headline);
        Assert.Contains("Copies still reach Google Drive", state.Detail);
        Assert.Contains("OneDrive isn't installed or signed in", state.Detail);
    }

    [Fact]
    public void Switched_on_but_never_copied_yet_is_amber_not_red()
    {
        var state = Assess(new[] { Service("OneDrive") });

        Assert.Equal(MissionControl.Level.Watch, state.Level);
        Assert.Equal("Not yet", state.Headline);
        Assert.Contains("No backup has reached OneDrive yet", state.Detail);
        Assert.Contains("press Back Up Now", state.Detail);
    }

    [Fact]
    public void A_copy_that_reached_the_cloud_a_long_time_ago_is_amber_and_says_how_long()
    {
        var state = Assess(new[] { Service("OneDrive") }, lastSuccessUtc: Now.AddDays(-20));

        Assert.Equal(MissionControl.Level.Watch, state.Level);
        Assert.Equal("Out of date", state.Headline);
        Assert.Contains("20 days ago", state.Detail);
    }

    [Fact]
    public void Exactly_as_old_as_the_limit_already_counts_as_out_of_date()
    {
        var state = Assess(new[] { Service("OneDrive") }, lastSuccessUtc: Now - CloudBackupStatus.OutOfDateAfter);

        Assert.Equal(MissionControl.Level.Watch, state.Level);
    }

    // --- Reading the settings, and writing down what a copy did ------------

    [Fact]
    public void Only_the_services_that_are_ticked_are_judged()
    {
        var settings = new AppSettings
        {
            CloudBackupOneDrive = true,
            CloudBackupGoogleDrive = false,
            CloudBackupICloudDrive = false,
            LastCloudBackupUtc = Now.AddDays(-2),
            LastCloudBackupProblem = string.Empty,
        };

        var facts = CloudBackupStatus.Read(settings, Now);

        Assert.Equal(new[] { "OneDrive" }, facts.Services.Select(s => s.Name));
        Assert.Equal(Now.AddDays(-2), facts.LastSuccessUtc);
        Assert.Equal(string.Empty, facts.LastProblem);
    }

    [Fact]
    public void A_round_of_copies_that_all_worked_is_written_down_as_a_success()
    {
        var settings = new AppSettings { LastCloudBackupProblem = "Not copied to OneDrive: nope." };

        CloudBackupStatus.Record(settings, new[]
        {
            new CloudBackup.CopyOutcome(CloudService.OneDrive, true, "Copied to OneDrive."),
            new CloudBackup.CopyOutcome(CloudService.GoogleDrive, true, "Copied to Google Drive."),
        }, Now);

        Assert.Equal(Now, settings.LastCloudBackupUtc);
        Assert.Equal(string.Empty, settings.LastCloudBackupProblem);
    }

    [Fact]
    public void A_copy_that_failed_keeps_only_its_own_line_and_does_not_count_as_a_success()
    {
        var settings = new AppSettings { LastCloudBackupUtc = Now.AddDays(-30) };

        CloudBackupStatus.Record(settings, new[]
        {
            new CloudBackup.CopyOutcome(CloudService.GoogleDrive, true, "Copied to Google Drive."),
            new CloudBackup.CopyOutcome(CloudService.OneDrive, false, "Not copied to OneDrive: Access to the path is denied."),
        }, Now);

        // The time of the last fully good copy is left alone, and "Copied to
        // Google Drive." is not what a red light should be saying.
        Assert.Equal(Now.AddDays(-30), settings.LastCloudBackupUtc);
        Assert.Equal("Not copied to OneDrive: Access to the path is denied.", settings.LastCloudBackupProblem);
    }

    [Fact]
    public void A_backup_with_no_cloud_service_to_copy_to_changes_nothing()
    {
        var settings = new AppSettings { LastCloudBackupUtc = Now.AddDays(-1), LastCloudBackupProblem = string.Empty };

        CloudBackupStatus.Record(settings, Array.Empty<CloudBackup.CopyOutcome>(), Now);

        Assert.Equal(Now.AddDays(-1), settings.LastCloudBackupUtc);
        Assert.Equal(string.Empty, settings.LastCloudBackupProblem);
    }

    // --- Mission Control's tile --------------------------------------------

    [Fact]
    public void Mission_control_shows_the_same_answer_as_its_own_tile()
    {
        var red = Assess(new[] { Service("OneDrive", folderFound: false) });
        var tile = MissionControl.Assess(Inputs(red)).Single(t => t.Key == "cloud");

        Assert.Equal(MissionControl.Level.Problem, tile.Level);
        Assert.Equal("Cloud backup", tile.Title);
        Assert.Equal(red.Headline, tile.Value);
        Assert.Equal(red.Detail, tile.Detail);
    }

    [Fact]
    public void A_night_with_the_cloud_backup_not_looked_at_has_a_dimmed_tile()
    {
        var tile = MissionControl.Assess(Inputs(null)).Single(t => t.Key == "cloud");

        Assert.Equal(MissionControl.Level.Off, tile.Level);
    }

    /// <summary>Everything else well, so only the cloud backup tile can be anything but green.</summary>
    private static MissionControl.Inputs Inputs(CloudBackupStatus.State? cloud) => new(
        LineDecoder: "EasyLap",
        LineState: DecoderConnectionState.Connected,
        HeartbeatAge: TimeSpan.FromSeconds(1),
        LastPassAge: TimeSpan.FromSeconds(5),
        SplitState: null,
        CarsHeard: 6,
        WeakCars: Array.Empty<string>(),
        Roster: null,
        RosterMissing: Array.Empty<string>(),
        TimingProblem: null,
        Fading: Array.Empty<string>(),
        Relay: null,
        VoiceMuted: false,
        CloudBackup: cloud);
}
