using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11 (5 Nov 2026), Settings: Restore Default Settings and Put Back
/// Earlier Settings on the files - the copy picked no longer pruned before
/// it is read, a damaged file no longer stops a reset half-way, and a copy
/// put back only what a restore resets, keeping the live keys, PINs, mode,
/// lock and addresses.
/// </summary>
public sealed class BugHunt11_SettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "HeatCallerBugHunt11Settings", Guid.NewGuid().ToString("N"));

    public BugHunt11_SettingsTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string SettingsPath => Path.Combine(_folder, "settings.json");

    private JsonObject ReadSettings() => (JsonObject)JsonNode.Parse(File.ReadAllText(SettingsPath))!;

    /// <summary>A copy in Settings Backups holding the given files, dated so it sorts where wanted.</summary>
    private string MakeCopy(DateTime when, params (string Name, string Json)[] files)
    {
        var folder = SettingsRestore.CopiesFolder(_folder);
        Directory.CreateDirectory(folder);
        var zip = Path.Combine(folder, $"settings {when:yyyy-MM-dd HH-mm-ss}.zip");

        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (var (name, json) in files)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(json);
            }
        }

        File.SetLastWriteTime(zip, when);
        return zip;
    }

    // --- Put Back: the oldest of ten ---------------------------------------------------

    [Fact]
    public void Putting_back_the_oldest_of_ten_copies_puts_it_back_and_keeps_it()
    {
        File.WriteAllText(SettingsPath, "{\"RaceLapLimit\":99}");

        var oldest = MakeCopy(new DateTime(2026, 10, 1), ("settings.json", "{\"RaceLapLimit\":12}"));

        for (var i = 1; i < SettingsRestore.CopiesKept; i++)
        {
            MakeCopy(new DateTime(2026, 10, 1).AddDays(i), ("settings.json", "{\"RaceLapLimit\":50}"));
        }

        Assert.Equal(SettingsRestore.CopiesKept, SettingsRestore.Copies(_folder).Count);
        Assert.Equal(oldest, SettingsRestore.Copies(_folder)[^1].Path);

        SettingsRestore.PutBackWithCopy(_folder, oldest, new DateTime(2026, 11, 5, 20, 0, 0));

        Assert.Equal(12, ReadSettings()["RaceLapLimit"]!.GetValue<int>());
        Assert.True(File.Exists(oldest), "the copy picked was pruned");
    }

    [Fact]
    public void Saving_a_copy_never_prunes_the_one_kept()
    {
        File.WriteAllText(SettingsPath, "{}");

        var oldest = MakeCopy(new DateTime(2026, 10, 1), ("settings.json", "{}"));

        for (var i = 1; i < SettingsRestore.CopiesKept; i++)
        {
            MakeCopy(new DateTime(2026, 10, 1).AddDays(i), ("settings.json", "{}"));
        }

        SettingsRestore.SaveCopy(_folder, new DateTime(2026, 11, 5, 20, 0, 0), keep: oldest);

        Assert.True(File.Exists(oldest));
    }

    // --- Restore Defaults: a damaged file ----------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("{\"Enabled\": tr")]
    public void A_damaged_imaging_file_is_left_alone_and_the_rest_are_reset(string damaged)
    {
        var shipped = new AppSettings();
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new AppSettings
        {
            RaceMinLapTimeMs = shipped.RaceMinLapTimeMs + 900,
            SettingsMode = SettingsMode.Advanced,
        }));
        var imaging = Path.Combine(_folder, "imaging.json");
        File.WriteAllText(imaging, damaged);

        var changed = SettingsRestore.ResetFiles(_folder, alsoShown: false);

        var back = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath))!;
        Assert.Equal(shipped.RaceMinLapTimeMs, back.RaceMinLapTimeMs);
        Assert.Equal(SettingsMode.Simple, back.SettingsMode);
        Assert.Contains("App: RaceMinLapTimeMs", changed);
        Assert.Equal(damaged, File.ReadAllText(imaging));
    }

    [Fact]
    public void A_file_that_cannot_be_read_stops_the_reset_before_anything_is_written()
    {
        var shipped = new AppSettings();
        var before = JsonSerializer.Serialize(new AppSettings { RaceMinLapTimeMs = shipped.RaceMinLapTimeMs + 900 });
        File.WriteAllText(SettingsPath, before);
        var ambience = Path.Combine(_folder, "ambience.json");
        File.WriteAllText(ambience, "{}");

        using (new FileStream(ambience, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => SettingsRestore.ResetFiles(_folder, alsoShown: false));
        }

        Assert.Equal(before, File.ReadAllText(SettingsPath));
    }

    // --- Put Back: only what a restore resets ------------------------------------------

    [Fact]
    public void Putting_back_keeps_the_live_pins_keys_and_bookkeeping()
    {
        File.WriteAllText(SettingsPath, """
            {
              "RaceLapLimit": 99,
              "RaceMinLapTimeMs": 3000,
              "WebServerPin": "2222",
              "Secrets": "sealed-new",
              "RacerPins": { "Ann": "4321" },
              "RacerPinsEmailed": [ "Ann" ],
              "RelayToken": "token-new",
              "PrivacyRepublishOwed": [ ],
              "HeatCallerConsentRevokedUtc": "2026-11-04T10:00:00Z",
              "LastTrackLayoutName": "Hairpin"
            }
            """);

        var zip = MakeCopy(new DateTime(2026, 11, 1), ("settings.json", """
            {
              "RaceLapLimit": 12,
              "RaceMinLapTimeMs": 4500,
              "WebServerPin": "1111",
              "Secrets": "sealed-old",
              "RacerPins": { "Ann": "1234" },
              "RacerPinsEmailed": [ ],
              "RelayToken": "token-old",
              "PrivacyRepublishOwed": [ "Bob" ],
              "HeatCallerConsentRevokedUtc": null,
              "LastTrackLayoutName": "Oval"
            }
            """));

        SettingsRestore.PutBack(_folder, zip);
        var back = ReadSettings();

        // The tuning comes back...
        Assert.Equal(12, back["RaceLapLimit"]!.GetValue<int>());
        Assert.Equal(4500, back["RaceMinLapTimeMs"]!.GetValue<int>());

        // ...the club's own does not.
        Assert.Equal("2222", back["WebServerPin"]!.GetValue<string>());
        Assert.Equal("sealed-new", back["Secrets"]!.GetValue<string>());
        Assert.Equal("4321", back["RacerPins"]!["Ann"]!.GetValue<string>());
        Assert.Equal("Ann", Assert.Single(back["RacerPinsEmailed"]!.AsArray())!.GetValue<string>());
        Assert.Equal("token-new", back["RelayToken"]!.GetValue<string>());
        Assert.Empty(back["PrivacyRepublishOwed"]!.AsArray());
        Assert.NotNull(back["HeatCallerConsentRevokedUtc"]);
        Assert.Equal("Hairpin", back["LastTrackLayoutName"]!.GetValue<string>());
    }

    [Fact]
    public void Putting_back_keeps_simple_mode_and_the_manager_pin_lock()
    {
        File.WriteAllText(SettingsPath, $$"""
            { "SettingsMode": {{(int)SettingsMode.Simple}}, "AdvancedNeedsManagerPin": true, "ManagerPin": "9876" }
            """);

        var zip = MakeCopy(new DateTime(2026, 11, 1), ("settings.json", $$"""
            { "SettingsMode": {{(int)SettingsMode.Advanced}}, "AdvancedNeedsManagerPin": false, "ManagerPin": "" }
            """));

        SettingsRestore.PutBack(_folder, zip);
        var back = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath))!;

        Assert.Equal(SettingsMode.Simple, back.SettingsMode);
        Assert.True(back.AdvancedNeedsManagerPin);
        Assert.Equal("9876", ReadSettings()["ManagerPin"]!.GetValue<string>());
    }

    [Fact]
    public void A_planted_copy_cannot_change_where_the_secrets_go()
    {
        File.WriteAllText(SettingsPath, """
            {
              "RelayBaseUrl": "https://relay.club.invalid",
              "VoiceRelayBaseUrl": "https://voice.club.invalid",
              "SmtpHost": "smtp.club.invalid",
              "RaceLapLimit": 99
            }
            """);

        var zip = MakeCopy(DateTime.Now, ("settings.json", """
            {
              "RelayBaseUrl": "https://relay.attacker.invalid",
              "VoiceRelayBaseUrl": "https://voice.attacker.invalid",
              "SmtpHost": "smtp.attacker.invalid",
              "RaceLapLimit": 12
            }
            """));

        SettingsRestore.PutBack(_folder, zip);
        var back = ReadSettings();

        Assert.Equal("https://relay.club.invalid", back["RelayBaseUrl"]!.GetValue<string>());
        Assert.Equal("https://voice.club.invalid", back["VoiceRelayBaseUrl"]!.GetValue<string>());
        Assert.Equal("smtp.club.invalid", back["SmtpHost"]!.GetValue<string>());
        Assert.Equal(12, back["RaceLapLimit"]!.GetValue<int>());
    }

    // --- Review (5 Nov 2026): a write that fails part-way --------------------------------
    //
    // SafeFile.WriteAllText hands back false rather than throwing, so a file
    // that could not be written was skipped and the caller told every
    // setting went back. Now a failure before the first write is an
    // IOException (nothing changed, no restart) and one after it is
    // PartlyWrittenException (the caller says so and restarts). A file held
    // open for reading only still reads, but cannot be swapped over.

    private static FileStream HoldForReadingOnly(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    [Fact]
    public void A_reset_that_cannot_write_a_later_file_says_it_was_partly_written()
    {
        var shipped = new AppSettings();
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new AppSettings { RaceMinLapTimeMs = shipped.RaceMinLapTimeMs + 900 }));
        var voice = Path.Combine(_folder, "voice-settings.json");
        File.WriteAllText(voice, "{}");

        using (HoldForReadingOnly(voice))
        {
            var ex = Assert.Throws<SettingsRestore.PartlyWrittenException>(() => SettingsRestore.ResetFiles(_folder, alsoShown: false));
            Assert.Contains("voice-settings.json", ex.Message);
        }

        // settings.json, first in Files, was written before voice-settings.json failed.
        Assert.Equal(shipped.RaceMinLapTimeMs, JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath))!.RaceMinLapTimeMs);
    }

    [Fact]
    public void A_reset_that_cannot_write_the_first_file_changed_nothing()
    {
        var shipped = new AppSettings();
        var before = JsonSerializer.Serialize(new AppSettings { RaceMinLapTimeMs = shipped.RaceMinLapTimeMs + 900 });
        File.WriteAllText(SettingsPath, before);
        var voice = Path.Combine(_folder, "voice-settings.json");
        File.WriteAllText(voice, "{}");

        using (HoldForReadingOnly(SettingsPath))
        {
            var ex = Assert.ThrowsAny<Exception>(() => SettingsRestore.ResetFiles(_folder, alsoShown: false));
            Assert.IsAssignableFrom<IOException>(ex);
        }

        Assert.Equal(before, File.ReadAllText(SettingsPath));
        Assert.Equal("{}", File.ReadAllText(voice));
    }

    [Fact]
    public void A_reset_stopped_by_a_locked_file_is_not_taken_for_a_partly_written_one()
    {
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new AppSettings()));
        var ambience = Path.Combine(_folder, "ambience.json");
        File.WriteAllText(ambience, "{}");

        using (new FileStream(ambience, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var ex = Assert.ThrowsAny<Exception>(() => SettingsRestore.ResetFiles(_folder, alsoShown: false));
            Assert.IsNotType<SettingsRestore.PartlyWrittenException>(ex);
            Assert.IsAssignableFrom<IOException>(ex);
        }
    }

    [Fact]
    public void A_put_back_that_cannot_write_a_later_file_says_it_was_partly_written()
    {
        File.WriteAllText(SettingsPath, """{ "RaceLapLimit": 99 }""");
        var voice = Path.Combine(_folder, "voice-settings.json");
        File.WriteAllText(voice, "{}");

        var zip = MakeCopy(new DateTime(2026, 11, 1),
            ("settings.json", """{ "RaceLapLimit": 12 }"""),
            ("voice-settings.json", "{}"));

        using (HoldForReadingOnly(voice))
        {
            Assert.Throws<SettingsRestore.PartlyWrittenException>(() => SettingsRestore.PutBack(_folder, zip));
        }

        Assert.Equal(12, ReadSettings()["RaceLapLimit"]!.GetValue<int>());
    }

    [Fact]
    public void A_put_back_that_cannot_write_the_first_file_changed_nothing()
    {
        var before = """{ "RaceLapLimit": 99 }""";
        File.WriteAllText(SettingsPath, before);

        var zip = MakeCopy(new DateTime(2026, 11, 1), ("settings.json", """{ "RaceLapLimit": 12 }"""));

        using (HoldForReadingOnly(SettingsPath))
        {
            var ex = Assert.ThrowsAny<Exception>(() => SettingsRestore.PutBack(_folder, zip));
            Assert.IsAssignableFrom<IOException>(ex);
        }

        Assert.Equal(before, File.ReadAllText(SettingsPath));
    }
}
