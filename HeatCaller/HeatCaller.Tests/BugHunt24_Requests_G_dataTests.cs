using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the follow-up requests in bundle G_data: the
/// changes first-round fixers needed in files they did not own, so their
/// fixes reach the director - storage notices, where backups really go, the
/// export using Forget's rule, the roll call on an alternate transponder,
/// the garage's size and pack names, juniors on handed-on transponders, desk
/// answers on the relay's clock, the restored standby address, Take Over
/// keeping the standby's own lists, and the copy's age on this PC's clock.
/// Real stores in a throwaway data folder; no window is shown.
/// </summary>
public sealed class BugHunt24_Requests_G_dataTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "HeatCaller" }.Concat(parts).ToArray()));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }

    // ------------------------------------------------------------ SafeFile (F051, F007)

    [Fact]
    public void The_saving_switched_off_line_no_longer_talks_about_restored_files()
    {
        var source = Source("HeatCaller.Timing", "SafeFile.cs");

        Assert.DoesNotContain("so the restored files aren't overwritten", source);
        Assert.Contains("\"[storage] Saving is switched off until the app restarts.\"", source);
    }

    [Fact]
    public void A_caller_that_carries_on_says_what_happens_next_instead_of_restart()
    {
        var path = _data.PathOf("never-there-journal.json");
        var messages = new List<string>();
        void Collect(string message) => messages.Add(message);

        SafeFile.WriteFailed += Collect;
        try
        {
            Assert.True(SafeFile.HoldWritesAfterUnreadable(path, new IOException("locked"), "Any race in it was not offered back."));
        }
        finally
        {
            SafeFile.WriteFailed -= Collect;
        }

        var said = Assert.Single(messages, m => m.Contains("never-there-journal.json", StringComparison.Ordinal));
        Assert.Contains("Any race in it was not offered back.", said);
        Assert.DoesNotContain("restarted", said);
    }

    [Fact]
    public void Without_the_words_the_hold_says_what_it_always_said()
    {
        var path = _data.PathOf("never-there-store.json");
        var messages = new List<string>();
        void Collect(string message) => messages.Add(message);

        SafeFile.WriteFailed += Collect;
        try
        {
            Assert.True(SafeFile.HoldWritesAfterUnreadable(path, new IOException("locked")));
        }
        finally
        {
            SafeFile.WriteFailed -= Collect;
        }

        var said = Assert.Single(messages, m => m.Contains("never-there-store.json", StringComparison.Ordinal));
        Assert.Contains("Nothing will be saved to it until HeatCaller is restarted and can read it", said);
    }

    // ------------------------------------------------------------ Backups page (F058)

    private static readonly string Cloud = Path.Combine(Path.GetTempPath(), "hc-g-data-fake-onedrive");
    private static readonly string Local = Path.Combine(Path.GetTempPath(), "hc-g-data-this-pc");

    private static string? OneDriveOnly(CloudService service) => service == CloudService.OneDrive ? Cloud : null;

    [Fact]
    public void An_unlocked_backup_folder_in_OneDrive_shows_the_folder_on_this_PC_and_says_why()
    {
        var chosen = Path.Combine(Cloud, "Backups");

        var (folder, note) = BackupRestoreControl.WhereBackupsGo(chosen, locked: false, Local, OneDriveOnly);

        Assert.Equal(Local, folder);
        Assert.NotNull(note);
        Assert.Contains("OneDrive", note);
        Assert.Contains("on this PC only", note);
        Assert.Contains("Set a backup password", note);
    }

    [Fact]
    public void A_locked_backup_or_a_folder_outside_the_cloud_shows_the_folder_chosen()
    {
        var inCloud = Path.Combine(Cloud, "Backups");
        var onDisk = Path.Combine(Path.GetTempPath(), "hc-g-data-plain", "Backups");

        Assert.Equal((inCloud, (string?)null), BackupRestoreControl.WhereBackupsGo(inCloud, locked: true, Local, OneDriveOnly));
        Assert.Equal((onDisk, (string?)null), BackupRestoreControl.WhereBackupsGo(onDisk, locked: false, Local, OneDriveOnly));
    }

    [Fact]
    public void Remove_Password_says_backups_will_stay_on_this_PC_when_the_folder_is_in_the_cloud()
    {
        Assert.Contains("OneDrive", BackupRestoreControl.RemovePasswordFolderNote("OneDrive"));
        Assert.Contains("kept on this PC instead", BackupRestoreControl.RemovePasswordFolderNote("OneDrive"));
        Assert.Equal(string.Empty, BackupRestoreControl.RemovePasswordFolderNote(null));
    }

    // ------------------------------------------------------------ Local web page's on deck (#155)

    [Fact]
    public void The_local_web_pages_on_deck_time_is_shown_the_way_the_timetable_shows_it()
    {
        Assert.Contains("DayScheduleControl.ShownTime(next.ScheduledTime)", Source("HeatCaller", "WebServerService.LiveInputs.cs"));
    }

    // ------------------------------------------------------------ Export uses Forget's rule (F119)

    [Fact]
    public void Export_leaves_out_another_drivers_race_on_a_handed_on_transponder_and_keeps_their_own_on_one_given_up()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "1111111", DriverName = "Mia Kent" });
        profiles.Upsert(new DriverProfile { Transponder = "2222222", DriverName = "Sam Roe" });

        var history = SessionHistoryStore.FromSessions(new[]
        {
            new SessionRecord
            {
                Label = "Handed on Q1",
                StartedAtUtc = new DateTime(2026, 3, 1, 19, 0, 0, DateTimeKind.Utc),
                Results = new List<SessionResultEntry> { new() { Position = 1, Transponder = "1111111", DriverName = "Sam Roe", Laps = 20 } },
            },
            new SessionRecord
            {
                Label = "Her old transponder Q2",
                StartedAtUtc = new DateTime(2026, 4, 1, 19, 0, 0, DateTimeKind.Utc),
                Results = new List<SessionResultEntry> { new() { Position = 2, Transponder = "3333333", DriverName = "Mia Kent", Laps = 19 } },
            },
        });

        var html = DriverDataExport.Html(profiles.Get("1111111")!, new DriverDataSources { Profiles = profiles, History = history },
            "Acme RC", new DateTime(2026, 11, 23, 10, 0, 0));

        Assert.DoesNotContain("Handed on Q1", html);
        Assert.Contains("Her old transponder Q2", html);
    }

    // ------------------------------------------------------------ Leader behind on the phone (F153)

    [Fact]
    public void The_phones_board_passes_the_races_flag_state_to_Leader_behind()
    {
        Assert.Contains("LeaderBehind.Approach(car, ordered, startType, staggerClock, _race.HasTakenFlag)", Source("HeatCaller", "WebServerService.cs"));
    }

    // ------------------------------------------------------------ Desk Inbox answer keys (F163)

    [Fact]
    public void A_greyed_out_answer_key_is_kept_by_the_inbox_rather_than_passed_on()
    {
        var notes = ClubNotes.Load();
        notes.NewMeeting("tonight");

        StaThread.Run(() =>
        {
            using var inbox = new DeskInboxForm(null, ProfileStore.Load(), null, null, notes, null, null, null, new AppSettings(), null, null, null);
            var press = typeof(DeskInboxForm).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!;

            // Nothing waiting, so nothing picked and every answer greyed out.
            foreach (var key in new[] { Keys.A, Keys.N, Keys.D, Keys.S, Keys.Delete })
            {
                Assert.True((bool)press.Invoke(inbox, new object[] { new Message(), key })!, $"{key} went past the inbox.");
            }

            return true;
        }, TimeSpan.FromSeconds(30), () => "greyed-out answer keys in the inbox");
    }

    // ------------------------------------------------------------ Roll call on an alternate (F269)

    [Fact]
    public void A_driver_seated_on_their_alternate_transponder_is_heard_on_the_roll_call()
    {
        var watch = new TransponderWatch { AlternateTransponderOwner = id => id == "ALT" ? "MAIN" : null };
        watch.Record(new DirectLapEvent { TransponderId = "ALT", SignalHits = 8, SignalStrength = 100 });

        var entries = RollCall.Build(new[] { "ALT" }, watch, _ => "Mia", DateTime.UtcNow.AddMinutes(-1));

        var entry = Assert.Single(entries);
        Assert.Equal("ALT", entry.Transponder);
        Assert.Equal(RollCall.State.Heard, entry.State);
    }

    // ------------------------------------------------------------ The garage (F278, pack names)

    private static DriverGarage.Sources GarageSources(ProfileStore profiles, BatteryPackStore packs, SessionHistoryStore history, CarStore cars, Func<string, string?> thumb) =>
        new(profiles, packs, history, cars, ChassisTypeStore.Load(), ChassisTemplateStore.Load(), SetupLogStore.Load(), new[] { "Stock" }, thumb);

    [Fact]
    public void A_race_on_a_pack_not_in_the_drivers_list_is_named_by_that_pack()
    {
        var profiles = ProfileStore.Load();
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "100", DriverName = "Sam" }, out _));

        var packs = BatteryPackStore.Load();
        var clubPack = new BatteryPack { Label = "Club Orange" };
        packs.Upsert(clubPack);

        var race = new SessionRecord
        {
            Label = "Stock A Main",
            StartedAtUtc = new DateTime(2026, 11, 20, 19, 0, 0, DateTimeKind.Utc),
            Results = new List<SessionResultEntry> { new() { Position = 1, Transponder = "100", DriverName = "Sam", Laps = 20 } },
        };

        packs.Tag(race.Id, BatteryPackStore.DriverKey(profiles, "100"), clubPack.Id, PackTagSource.RaceControl);

        var json = DriverGarage.Build(profiles.Get("100")!,
            GarageSources(profiles, packs, SessionHistoryStore.FromSessions(new[] { race }), CarStore.Load(), _ => null),
            DateTime.UtcNow);

        using var doc = JsonDocument.Parse(json);
        var row = Assert.Single(doc.RootElement.GetProperty("races").EnumerateArray());

        Assert.Equal(clubPack.Id, row.GetProperty("pack").GetString());
        Assert.Equal("Club Orange", row.GetProperty("packLabel").GetString());
    }

    [Fact]
    public void A_garage_too_big_for_the_relay_drops_photos_but_keeps_the_cover()
    {
        var profiles = ProfileStore.Load();
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "100", DriverName = "Sam" }, out _));

        var cars = CarStore.Load();
        cars.Upsert(new Car { Id = "c1", Name = "Red", OwnerTransponder = "100", PhotoFileNames = { "p1.jpg", "p2.jpg", "p3.jpg", "p4.jpg", "p5.jpg" } });

        var big = "data:image/jpeg;base64," + new string('A', 60_000);
        var sources = GarageSources(profiles, BatteryPackStore.Load(), SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>()), cars, _ => big);

        var json = DriverGarage.Build(profiles.Get("100")!, sources, DateTime.UtcNow, budgetBytes: 200_000);

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(json) <= 200_000);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("trimmed").GetBoolean());

        var car = Assert.Single(doc.RootElement.GetProperty("cars").EnumerateArray());
        var photos = car.GetProperty("photos").EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToList();

        Assert.InRange(photos.Count, 1, 4);
        Assert.Equal("p1.jpg", photos[0]);
        Assert.Equal("p1.jpg", car.GetProperty("cover").GetString());
    }

    [Fact]
    public void A_garage_that_fits_is_left_whole_and_not_marked_trimmed()
    {
        var profiles = ProfileStore.Load();
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "100", DriverName = "Sam" }, out _));

        var cars = CarStore.Load();
        cars.Upsert(new Car { Id = "c1", Name = "Red", OwnerTransponder = "100", PhotoFileNames = { "p1.jpg", "p2.jpg" } });

        var json = DriverGarage.Build(profiles.Get("100")!,
            GarageSources(profiles, BatteryPackStore.Load(), SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>()), cars, _ => "data:image/jpeg;base64,AAAA"),
            DateTime.UtcNow);

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("trimmed", out _));
        Assert.Equal(2, Assert.Single(doc.RootElement.GetProperty("cars").EnumerateArray()).GetProperty("photos").GetArrayLength());
    }

    // ------------------------------------------------------------ Juniors on handed-on transponders (F276)

    private static DriverProfile Mia() => new() { Transponder = "7001", DriverName = "Mia Kent", IsJunior = true };

    private static DriverProfile Joe() => new() { Transponder = "7009", DriverName = "Joe Bloggs" };

    [Fact]
    public void GoesBy_is_true_only_for_the_name_the_transponders_driver_has()
    {
        var register = PublicPrivacy.From(new[] { Mia(), Joe() });

        Assert.True(register.GoesBy("7009", "joe  bloggs"));
        Assert.False(register.GoesBy("7009", "Mia Kent"));
        Assert.False(register.GoesBy("9999", "Joe Bloggs"));
    }

    [Fact]
    public void A_juniors_old_row_on_a_transponder_now_an_adults_is_still_shortened()
    {
        var register = PublicPrivacy.From(new[] { Mia(), Joe() });

        Assert.Equal(new[] { "Mia K" }, RelayPublisher.DisplayNames(new[] { "Mia Kent" }, abbreviate: false, new string?[] { "7009" }, register));

        // The adult's own rows on it keep the adult's full name.
        Assert.Equal(new[] { "Joe Bloggs" }, RelayPublisher.DisplayNames(new[] { "Joe Bloggs" }, abbreviate: false, new string?[] { "7009" }, register));
    }

    [Fact]
    public void A_deleted_juniors_rows_on_a_transponder_an_adult_now_holds_are_still_shortened()
    {
        // Mia's profile is gone (her protection kept), and her transponder is Joe's now.
        var register = PublicPrivacy.From(
            new[] { new DriverProfile { Transponder = "7001", DriverName = "Joe Bloggs" } },
            new[] { new PrivacyRetainedDriver { Transponders = new() { "7001" }, Name = "Mia Kent", Junior = true } });

        Assert.Equal(new[] { "Mia K" }, RelayPublisher.DisplayNames(new[] { "Mia Kent" }, abbreviate: false, new string?[] { "7001" }, register));
        Assert.Equal(new[] { "Joe Bloggs" }, RelayPublisher.DisplayNames(new[] { "Joe Bloggs" }, abbreviate: false, new string?[] { "7001" }, register));
    }

    [Fact]
    public void Where_a_name_is_on_two_transponders_the_juniors_is_the_one_kept()
    {
        var register = PublicPrivacy.From(new[] { Mia(), Joe() });

        var chosen = register.TransponderForEach(new[] { "Mia Kent" },
            new (string?, string?)[] { ("Mia Kent", "7009"), ("Mia Kent", "7001") });

        Assert.Equal("7001", Assert.Single(chosen));
    }

    // ------------------------------------------------------------ Desk answers on the relay's clock (F343)

    [Fact]
    public void An_answer_is_dated_on_the_relays_clock()
    {
        var at = new DateTime(2026, 11, 23, 19, 0, 0, DateTimeKind.Utc);

        Assert.Equal(at.AddMinutes(20), DriverLive.OnRelayClock(at, TimeSpan.FromMinutes(20)));
        Assert.Equal(at.AddMinutes(-5), DriverLive.OnRelayClock(at, TimeSpan.FromMinutes(-5)));
        Assert.Equal(DateTime.MaxValue.Ticks, DriverLive.OnRelayClock(DateTime.MaxValue, TimeSpan.FromDays(1)).Ticks);
    }

    // ------------------------------------------------------------ The restored standby address

    [Fact]
    public void Enter_in_the_Timing_PC_address_box_and_ticking_standby_take_on_a_restored_address()
    {
        var source = Source("HeatCaller", "BackupRestoreControl.Standby.cs");

        Assert.Contains("_txtStandbyPrimary.KeyDown +=", source);
        Assert.Contains("CommitStandbyPrimary(pressedEnter: true)", source);
        Assert.Contains("RestoredSettings.Confirm(RestoredSettings.Part.Standby)", source);
        Assert.Contains("RestoredSettings.StandbyMessage", source);
    }

    // ------------------------------------------------------------ Restore's confirm line (F297/F298)

    [Fact]
    public void The_restore_line_says_the_clubs_words_and_childrens_privacy_choices_are_kept()
    {
        Assert.Contains("the words you wrote for your club website and announcer", SettingsRestore.WhatIsKept);
        Assert.Contains("children's-privacy choices", SettingsRestore.WhatIsKept);
    }

    // ------------------------------------------------------------ Sponsor stream night (F304)

    [Fact]
    public void Tonights_sponsor_stream_counts_have_a_setting_filed_like_the_mentions()
    {
        var settings = new AppSettings();
        Assert.Equal(string.Empty, settings.SponsorStreamNight);

        Assert.Equal(SettingsTable.App[nameof(AppSettings.SponsorMentionsNight)], SettingsTable.App[nameof(AppSettings.SponsorStreamNight)]);
    }

    // ------------------------------------------------------------ Take Over keeps the standby's lists (F324)

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(text);
            }
        }

        return buffer.ToArray();
    }

    [Fact]
    public void Take_over_from_an_older_timing_pcs_copy_keeps_the_standbys_own_music_list()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(_data.PathOf("music.json"), "[\"standby's own\"]");
        File.WriteAllText(_data.PathOf("profiles.json"), "[\"old\"]");

        var copy = Path.Combine(_data.Folder, "older-copy.zip");
        File.WriteAllBytes(copy, Zip(("profiles.json", "[\"new\"]"), ("music.json", "[\"timing pc's\"]")));

        var result = BackupService.RestoreBackup(copy, Path.Combine(_data.Folder, "backups"), null, WarmStandby.MachineOwnFiles);

        Assert.Empty(result.Failed);
        Assert.Equal("[\"new\"]", File.ReadAllText(_data.PathOf("profiles.json")));
        Assert.Equal("[\"standby's own\"]", File.ReadAllText(_data.PathOf("music.json")));
    }

    // ------------------------------------------------------------ The copy's age (F325)

    [Fact]
    public void The_timing_pc_says_how_old_the_copy_is_and_the_standby_reads_it_on_its_own_clock()
    {
        Assert.Contains("context.Response.Headers[WarmStandby.AgeHeader] = WarmStandby.ServedAgeHeaderValue(atUtc, DateTime.UtcNow);",
            Source("HeatCaller", "WebServerService.Standby.cs"));

        var mirror = Source("HeatCaller", "StandbyMirror.cs");
        Assert.Contains("WarmStandby.CopyTakenOnThisClock(receivedUtc, ageHeader, stampUtc)", mirror);
        Assert.Contains("response.Headers.TryGetValues(WarmStandby.AgeHeader, out var ages)", mirror);
    }
}
