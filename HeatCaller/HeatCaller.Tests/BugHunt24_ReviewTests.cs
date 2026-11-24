namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 review (24 Nov 2026): five of bug hunt 24's fixes went too
/// far or not far enough.
///
/// 1. An automatic backup kept off the cloud was pruned to 14 whatever the
///    club keeps, before the club's own prune ran - a club keeping 30 had 14.
/// 2. Make This a Standby Again ticks the standby box itself, and the tick
///    took on a restored timing-PC address that nobody had looked at.
/// 3. "Not while finishing" silenced a true last-lap call for a staggered
///    start car whose own window was still open.
/// 4. Forgetting Sam Parker sent "Sam P" to the relay, which keys followers
///    by the name shown - so Sam Pemberton, still racing, lost his.
/// 5. A row on the driver's transponder already under an earlier Forget's
///    placeholder was taken as theirs, merging two forgotten people.
/// </summary>
public sealed class BugHunt24_ReviewTests : IDisposable
{
    private readonly TempAppData _data = new();

    public BugHunt24_ReviewTests()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(_data.PathOf("profiles.json"), "[{\"DriverName\":\"Mia Kent\"}]");
        File.WriteAllText(_data.PathOf("settings.json"), "{}");
    }

    public void Dispose()
    {
        BackupService.SyncRootOverride = null;
        _data.Dispose();
    }

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "HeatCaller" }.Concat(parts).ToArray()));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Couldn't find the repo root.");
    }

    // ------------------------------------------------------------ 1. The club's keep

    [Fact]
    public void An_automatic_backup_kept_off_the_cloud_keeps_as_many_as_the_club_asked()
    {
        var oneDrive = Path.Combine(_data.Folder, "OneDrive");
        BackupService.SyncRootOverride = s => s == CloudService.OneDrive ? oneDrive : null;
        var cloudFolder = Path.Combine(oneDrive, "Documents", "HeatCaller Backups");

        var local = BackupService.LocalOnlyBackupFolder;
        Directory.CreateDirectory(local);

        for (var day = 1; day <= 31; day++)
        {
            File.WriteAllText(Path.Combine(local, $"HeatCaller-AutoBackup-2026-10-{day:00}_20-00-00.zip"), "old");
        }

        for (var day = 1; day <= 9; day++)
        {
            File.WriteAllText(Path.Combine(local, $"HeatCaller-AutoBackup-2026-09-{day:00}_20-00-00.zip"), "older");
        }

        var path = BackupService.CreateBackup(cloudFolder, automatic: true, secrets: null, keepAutomatic: 30);

        var kept = BackupService.ListBackups(local).Where(f => f.Name.StartsWith("HeatCaller-AutoBackup-", StringComparison.Ordinal)).ToList();
        Assert.Equal(30, kept.Count);
        Assert.Contains(kept, f => string.Equals(f.FullName, path, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_scheduled_backup_hands_the_clubs_keep_to_the_backup_itself()
    {
        Assert.Contains("BackupService.CreateBackup(folder, true, secrets, keep)", Source("HeatCaller", "MainForm.cs"));
    }

    // ------------------------------------------------------------ 2. Standby ticked in code

    [Fact]
    public void Only_a_tick_by_hand_takes_on_a_restored_timing_PC_address()
    {
        Assert.True(BackupRestoreControl.TickConfirmsRestoredAddress(ticked: true, tickedByHand: true));
        Assert.False(BackupRestoreControl.TickConfirmsRestoredAddress(ticked: true, tickedByHand: false));
        Assert.False(BackupRestoreControl.TickConfirmsRestoredAddress(ticked: false, tickedByHand: true));
    }

    [Fact]
    public void Make_this_a_standby_again_marks_its_tick_as_made_in_code()
    {
        var source = Source("HeatCaller", "BackupRestoreControl.Standby.cs");
        var method = source[source.IndexOf("private void MakeStandbyAgain()", StringComparison.Ordinal)..];

        var marked = method.IndexOf("_tickingStandbyInCode = true;", StringComparison.Ordinal);
        var ticked = method.IndexOf("_chkStandbyMode.Checked = true;", StringComparison.Ordinal);

        Assert.True(marked > 0, "MakeStandbyAgain no longer marks its tick as made in code.");
        Assert.True(marked < ticked);
        Assert.Contains("TickConfirmsRestoredAddress(_chkStandbyMode.Checked, tickedByHand: !_tickingStandbyInCode)", source);
    }

    // ------------------------------------------------------------ 3. Last lap on a staggered start

    private long _nowMs = 1_000_000;

    private void Cross(RaceControlSession race, string car, uint raceTimeMs)
    {
        _nowMs = 1_000_000 + raceTimeMs;
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = car,
            DeviceTimeMs = raceTimeMs,
            SignalHits = 1,
            SignalStrength = 100,
            IsSynthetic = true,
        });
    }

    [Fact]
    public void Last_lap_is_still_called_for_a_staggered_car_whose_own_window_is_open()
    {
        var utc0 = new DateTime(2026, 11, 24, 19, 0, 0, DateTimeKind.Utc);
        var race = new RaceControlSession
        {
            LapLimit = 3,
            NowMsSource = () => _nowMs,
            UtcNowSource = () => utc0.AddMilliseconds(_nowMs),
            StartType = RaceStartType.StaggeredGrid,
            StaggerClockMode = StaggerClock.OwnRelease,
            FinishOnNextCrossing = true,
            TimeLimit = TimeSpan.FromSeconds(30),
        };
        race.Stage();
        race.SetReleaseSchedule(new Dictionary<string, long> { ["A"] = 0, ["B"] = 20_000 });
        race.GreenFlag(0);

        var lastLap = new List<string>();
        race.LastLapStarted += s => lastLap.Add(s.TransponderId);

        Cross(race, "A", 12_000);

        // A's 30 s are up: checkered out. B, released 20 s later, has 20 s still.
        _nowMs = 1_000_000 + 30_000;
        race.Tick();
        Assert.True(race.IsFinishing);

        Cross(race, "B", 32_000);
        Cross(race, "A", 35_000); // A takes the flag on lap 2 - no last lap for it
        Assert.True(race.HasTakenFlag("A"));
        Assert.Empty(lastLap);

        // B onto its lap 2 of 3 inside its own window: a true last lap.
        Cross(race, "B", 42_000);
        Assert.False(race.HasTakenFlag("B"));
        Assert.Equal(new[] { "B" }, lastLap);
    }

    [Fact]
    public void Last_lap_is_still_called_for_a_pass_that_arrived_before_the_checkered_but_was_handled_after()
    {
        const long baseMs = 1_000_000;
        var utc0 = new DateTime(2026, 11, 24, 19, 0, 0, DateTimeKind.Utc);
        var race = new RaceControlSession
        {
            LapLimit = 5,
            TimeLimit = TimeSpan.FromSeconds(30),
            NowMsSource = () => _nowMs,
            UtcNowSource = () => utc0.AddMilliseconds(_nowMs),
        };
        race.Stage();
        race.GreenFlag(0);

        var lastLap = new List<string>();
        race.LastLapStarted += s => lastLap.Add(s.TransponderId);

        Cross(race, "A", 5_000);
        Cross(race, "A", 15_000);
        Cross(race, "A", 25_000);

        _nowMs = baseMs + 30_000;
        race.Tick();
        Assert.True(race.IsFinishing);

        // Crossed - and reached the app - at 29.5 s, but the window was busy
        // and only got to it at 31 s. Onto lap 4 of 5 before the checkered.
        _nowMs = baseMs + 31_000;
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = "A",
            DeviceTimeMs = 29_500,
            ReceivedMs = baseMs + 29_500,
            SignalHits = 1,
            SignalStrength = 100,
            IsSynthetic = true,
        });

        Assert.False(race.HasTakenFlag("A"));
        Assert.Equal(new[] { "A" }, lastLap);
    }

    // ------------------------------------------------------------ 4. Shortened names another driver goes by

    [Fact]
    public void Forgetting_a_driver_does_not_send_a_short_name_another_current_driver_is_shown_by()
    {
        var names = MainForm.RelayForgetNames(new[] { "Sam Parker" }, "Sam Parker");

        var sent = MainForm.SparingOtherDrivers(names, new[] { "Sam Parker" }, new[] { "Sam Pemberton", "Joe Bloggs" });

        Assert.DoesNotContain("Sam P", sent);
        Assert.Contains("Sam Parker", sent);
        Assert.Contains("Sam Pa", sent);
        Assert.Contains("Sam Par", sent);
    }

    [Fact]
    public void With_no_other_driver_sharing_a_short_name_every_form_still_goes()
    {
        var names = MainForm.RelayForgetNames(new[] { "Sam Parker" }, "S.P.");

        Assert.Equal(names, MainForm.SparingOtherDrivers(names, new[] { "Sam Parker" }, new[] { "Joe Bloggs", null, "" }));
    }

    [Fact]
    public void Their_full_name_and_initials_always_go_even_when_another_driver_has_the_same_name()
    {
        var names = MainForm.RelayForgetNames(new[] { "Sam Parker" }, "S.P.");

        var sent = MainForm.SparingOtherDrivers(names, new[] { "Sam Parker" }, new[] { "Sam Parker" });

        Assert.Contains("Sam Parker", sent);
        Assert.Contains("S.P.", sent);
        Assert.DoesNotContain("Sam P", sent);
    }

    [Fact]
    public void The_forget_spares_the_other_current_drivers_names()
    {
        var source = Source("HeatCaller", "MainForm.Forget.cs");
        var made = source.IndexOf("var names = RelayForgetNames(outcome.Names, shown);", StringComparison.Ordinal);
        var spared = source.IndexOf("names = SparingOtherDrivers(names, outcome.Names,", StringComparison.Ordinal);
        var owed = source.IndexOf("_settings.RelayForgetsOwed.Add(", StringComparison.Ordinal);

        Assert.True(made > 0 && spared > made && owed > spared);
    }

    // ------------------------------------------------------------ 5. An earlier Forget's placeholder

    [Fact]
    public void A_placeholder_name_is_known_as_one()
    {
        Assert.True(DriverForget.IsPlaceholderName(DriverForget.Placeholder));
        Assert.True(DriverForget.IsPlaceholderName("removed  driver 3"));
        Assert.False(DriverForget.IsPlaceholderName("Removed driver 1"));
        Assert.False(DriverForget.IsPlaceholderName("Removed drivers"));
        Assert.False(DriverForget.IsPlaceholderName("Mia Kent"));
        Assert.False(DriverForget.IsPlaceholderName(null));
    }

    [Fact]
    public void A_row_on_their_transponder_under_an_earlier_forgets_placeholder_is_not_theirs()
    {
        var ids = new[] { "1234567" };
        var names = new[] { "Mia Kent" };

        Assert.False(DriverForget.IsTheirResult("1234567", "Removed driver", ids, "Mia Kent", names, Array.Empty<string>()));
        Assert.False(DriverForget.IsTheirResult("1234567", "Removed driver 2", ids, "Mia Kent", names, null));

        // Their own rows, and a renamed profile's old name, are still theirs.
        Assert.True(DriverForget.IsTheirResult("1234567", "Mia Kent", ids, "Mia Kent", names, Array.Empty<string>()));
        Assert.True(DriverForget.IsTheirResult("1234567", "Mia Smith", ids, "Mia Kent", names, Array.Empty<string>()));
    }
}
