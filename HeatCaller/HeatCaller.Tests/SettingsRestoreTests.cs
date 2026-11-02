using System.Text.Json;
using System.Text.Json.Nodes;

namespace HeatCaller.Tests;

/// <summary>
/// Restore default settings (1 Nov 2026, Simple and Advanced mode, round 4):
/// what Simple hides goes back to how HeatCaller ships, in the files, with
/// everything else carried over as it was - the sealed secrets included -
/// and a copy saved first that can be put back.
/// </summary>
public sealed class SettingsRestoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "HeatCallerRestoreTests", Guid.NewGuid().ToString("N"));

    public SettingsRestoreTests() => Directory.CreateDirectory(_folder);

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

    private static SettingsRestore.SettingsFile File(string label) => SettingsRestore.Files.Single(f => f.Label == label);

    private static string Json<T>(T value, JsonSerializerOptions? options = null) =>
        JsonSerializer.Serialize(value, options ?? new JsonSerializerOptions { WriteIndented = true });

    // --- settings.json ---------------------------------------------------------------

    [Fact]
    public void A_hidden_setting_goes_back_and_the_rest_stay()
    {
        var shipped = new AppSettings();
        var settings = new AppSettings
        {
            RaceMinLapTimeMs = shipped.RaceMinLapTimeMs + 900,        // hidden
            RaceLapLimit = shipped.RaceLapLimit + 7,                  // shown
            LastTrackLayoutName = "Hairpin",                          // memory
            HasCompletedFirstRun = true,                              // kept
            SettingsMode = SettingsMode.Advanced,
            SettingsModeDecided = true,
        };

        var (json, changed) = SettingsRestore.ResetJson(File("App"), Json(settings), alsoShown: false);
        var back = JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.Equal(shipped.RaceMinLapTimeMs, back.RaceMinLapTimeMs);
        Assert.Equal(settings.RaceLapLimit, back.RaceLapLimit);
        Assert.Equal("Hairpin", back.LastTrackLayoutName);
        Assert.True(back.HasCompletedFirstRun);
        Assert.True(back.SettingsModeDecided);
        Assert.Equal(SettingsMode.Simple, back.SettingsMode);
        Assert.Contains(nameof(AppSettings.RaceMinLapTimeMs), changed);
        Assert.DoesNotContain(nameof(AppSettings.RaceLapLimit), changed);
    }

    [Fact]
    public void What_simple_shows_goes_back_only_when_asked()
    {
        var shipped = new AppSettings();
        var json = Json(new AppSettings { RaceLapLimit = shipped.RaceLapLimit + 7 });

        Assert.Equal(shipped.RaceLapLimit + 7,
            JsonSerializer.Deserialize<AppSettings>(SettingsRestore.ResetJson(File("App"), json, alsoShown: false).Json)!.RaceLapLimit);
        Assert.Equal(shipped.RaceLapLimit,
            JsonSerializer.Deserialize<AppSettings>(SettingsRestore.ResetJson(File("App"), json, alsoShown: true).Json)!.RaceLapLimit);
    }

    [Fact]
    public void The_relay_addresses_are_left_alone()
    {
        var json = Json(new AppSettings { RelayBaseUrl = "https://relay.club.invalid", VoiceRelayBaseUrl = "https://voice.club.invalid" });

        var back = JsonSerializer.Deserialize<AppSettings>(SettingsRestore.ResetJson(File("App"), json, alsoShown: true).Json)!;

        Assert.Equal("https://relay.club.invalid", back.RelayBaseUrl);
        Assert.Equal("https://voice.club.invalid", back.VoiceRelayBaseUrl);
    }

    [Fact]
    public void Anything_in_the_file_the_table_does_not_name_is_carried_over()
    {
        // The sealed secrets, and anything a later version wrote.
        var node = JsonNode.Parse(Json(new AppSettings()))!.AsObject();
        node["Secrets"] = "sealed-blob";
        node["SomethingNewer"] = 42;

        var back = JsonNode.Parse(SettingsRestore.ResetJson(File("App"), node.ToJsonString(), alsoShown: true).Json)!;

        Assert.Equal("sealed-blob", (string?)back["Secrets"]);
        Assert.Equal(42, (int?)back["SomethingNewer"]);
    }

    [Fact]
    public void The_commentary_pieces_and_the_sidebar_go_back()
    {
        var json = Json(new AppSettings
        {
            CommentaryRaceIntros = false,
            CommentarySegmentsOff = "BattleWatch,FormGuide",
            NavOrder = new List<string> { "Venues", "Race Day" },
            HiddenTabs = new List<string> { "Garage" },
        });

        var back = JsonSerializer.Deserialize<AppSettings>(SettingsRestore.ResetJson(File("App"), json, alsoShown: false).Json)!;
        var shipped = new AppSettings();

        Assert.Equal(shipped.CommentaryRaceIntros, back.CommentaryRaceIntros);
        Assert.Equal(shipped.CommentarySegmentsOff, back.CommentarySegmentsOff);
        Assert.Empty(back.NavOrder);
        Assert.Empty(back.HiddenTabs);
    }

    // --- The other files ------------------------------------------------------------

    [Fact]
    public void The_voices_tuning_goes_back_and_its_key_and_voice_stay()
    {
        var profile = VoiceProfile.CreateDefault();
        profile.Stability = 0.95;
        profile.VoiceId = "club-voice";
        profile.RaceCallsInRaces = false;

        var node = JsonNode.Parse(Json(profile))!.AsObject();
        node["ApiKeySealed"] = "sealed-key";

        var json = SettingsRestore.ResetJson(File("Voice"), node.ToJsonString(), alsoShown: false).Json;
        var back = JsonSerializer.Deserialize<VoiceProfile>(json)!;

        Assert.Equal(VoiceProfile.CreateDefault().Stability, back.Stability);
        Assert.Equal("club-voice", back.VoiceId);
        Assert.False(back.RaceCallsInRaces);
        Assert.Equal("sealed-key", (string?)JsonNode.Parse(json)!["ApiKeySealed"]);
    }

    [Fact]
    public void The_crowd_the_soundscape_set_is_left_alone()
    {
        var ambience = new AmbienceSettings { Enabled = true, FadeUpSeconds = 9 };
        ambience.Layers.Add(new AmbienceLayer());

        var back = JsonSerializer.Deserialize<AmbienceSettings>(SettingsRestore.ResetJson(File("Ambience"), Json(ambience), alsoShown: false).Json)!;

        Assert.True(back.Enabled);
        Assert.Single(back.Layers);
        Assert.Equal(new AmbienceSettings().FadeUpSeconds, back.FadeUpSeconds);
    }

    [Fact]
    public void Imaging_is_written_the_way_its_own_store_reads_it()
    {
        var file = File("Imaging");
        var json = SettingsRestore.ResetJson(file, Json(new ImagingSettings { Level = 12 }, file.Json), alsoShown: false).Json;

        var back = JsonSerializer.Deserialize<ImagingSettings>(json, file.Json)!;

        Assert.Equal(new ImagingSettings().Level, back.Level);
    }

    [Fact]
    public void Every_settings_file_in_the_table_is_restored()
    {
        Assert.Equal(new[] { "App", "Voice", "Music", "Ambience", "Imaging" }, SettingsRestore.Files.Select(f => f.Label));
    }

    [Fact]
    public void Every_setting_left_alone_is_a_real_hidden_one()
    {
        foreach (var entry in SettingsRestore.LeftAlone)
        {
            var (label, name) = (entry.Split(':')[0], entry.Split(':')[1]);

            Assert.Equal(SettingGroup.HiddenInSimple, File(label).Table[name]);
        }
    }

    // --- The files, the copies, putting one back ------------------------------------

    [Fact]
    public void A_restore_rewrites_the_files_there_are_and_makes_none_that_are_not()
    {
        var shipped = new AppSettings();
        System.IO.File.WriteAllText(Path.Combine(_folder, "settings.json"), Json(new AppSettings { RaceMinLapTimeMs = shipped.RaceMinLapTimeMs + 500 }));

        var changed = SettingsRestore.ResetFiles(_folder, alsoShown: false);

        Assert.Contains("App: RaceMinLapTimeMs", changed);
        Assert.Equal(shipped.RaceMinLapTimeMs,
            JsonSerializer.Deserialize<AppSettings>(System.IO.File.ReadAllText(Path.Combine(_folder, "settings.json")))!.RaceMinLapTimeMs);
        Assert.False(System.IO.File.Exists(Path.Combine(_folder, "music.json")));
    }

    [Fact]
    public void A_copy_holds_the_settings_files_and_puts_them_back()
    {
        var settings = Path.Combine(_folder, "settings.json");
        System.IO.File.WriteAllText(settings, "{\"RaceLapLimit\":12}");
        System.IO.File.WriteAllText(Path.Combine(_folder, "music.json"), "{\"Volume\":0.3}");
        System.IO.File.WriteAllText(Path.Combine(_folder, "profiles.json"), "[]");

        var zip = SettingsRestore.SaveCopy(_folder, new DateTime(2026, 11, 1, 21, 0, 0));

        System.IO.File.WriteAllText(settings, "{\"RaceLapLimit\":99}");
        SettingsRestore.PutBack(_folder, zip);

        Assert.Equal("{\"RaceLapLimit\":12}", System.IO.File.ReadAllText(settings));

        using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
        Assert.Equal(new[] { "music.json", "settings.json" }, archive.Entries.Select(e => e.FullName).OrderBy(n => n));
    }

    [Fact]
    public void Ten_copies_are_kept_newest_first()
    {
        System.IO.File.WriteAllText(Path.Combine(_folder, "settings.json"), "{}");

        for (var i = 0; i < SettingsRestore.CopiesKept + 3; i++)
        {
            var zip = SettingsRestore.SaveCopy(_folder, new DateTime(2026, 10, 1).AddDays(i));
            System.IO.File.SetLastWriteTime(zip, new DateTime(2026, 10, 1).AddDays(i));
        }

        var copies = SettingsRestore.Copies(_folder);

        Assert.True(copies.Count <= SettingsRestore.CopiesKept + 1, $"{copies.Count} copies kept");
        Assert.True(copies.Zip(copies.Skip(1)).All(pair => pair.First.When >= pair.Second.When));
    }

    [Fact]
    public void Two_copies_in_one_second_are_both_kept()
    {
        System.IO.File.WriteAllText(Path.Combine(_folder, "settings.json"), "{}");
        var when = new DateTime(2026, 11, 1, 21, 0, 0);

        var first = SettingsRestore.SaveCopy(_folder, when);
        var second = SettingsRestore.SaveCopy(_folder, when);

        Assert.NotEqual(first, second);
        Assert.True(System.IO.File.Exists(first));
        Assert.True(System.IO.File.Exists(second));
    }
}
