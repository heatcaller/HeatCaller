using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace HeatCaller;

/// <summary>
/// RESTORE DEFAULT SETTINGS (1 Nov 2026, Simple and Advanced mode, round 4):
/// every setting Simple mode hides put back to how HeatCaller ships, with a
/// copy of the settings saved first - and those copies listed, so any of
/// them can be put back.
///
/// It works on the settings FILES, not on the settings the running app
/// holds: the pages read those once and save what they show, so a reset
/// made under them would be saved straight back over. The host writes the
/// files, freezes every further write (SafeFile.Freeze) and restarts, as a
/// backup restore does. Pure apart from the files, so it is tested on a
/// folder of its own.
///
/// What it never touches is SettingsTable's KeptOnRestore and Memory - the
/// club's data, keys, accounts, PINs, the decoder's port, backups, first-run
/// flags - and the few hidden settings in <see cref="LeftAlone"/>.
/// </summary>
internal static class SettingsRestore
{
    /// <summary>One settings file: its name in the data folder, its type, its table, and how it is written.</summary>
    internal sealed record SettingsFile(string Name, string Label, Type Type, IReadOnlyDictionary<string, SettingGroup> Table, Func<object> Defaults, JsonSerializerOptions Json);

    private static readonly JsonSerializerOptions Plain = new() { WriteIndented = true };

    private static readonly JsonSerializerOptions WithEnumNames = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The five files the settings table covers, as each is written by its own store.</summary>
    internal static readonly IReadOnlyList<SettingsFile> Files = new[]
    {
        new SettingsFile("settings.json", "App", typeof(AppSettings), SettingsTable.App, () => new AppSettings(), Plain),
        new SettingsFile("voice-settings.json", "Voice", typeof(VoiceProfile), SettingsTable.Voice, () => VoiceProfile.CreateDefault(), Plain),
        new SettingsFile("music.json", "Music", typeof(MusicSettings), SettingsTable.Music, () => new MusicSettings(), Plain),
        new SettingsFile("ambience.json", "Ambience", typeof(AmbienceSettings), SettingsTable.Ambience, () => new AmbienceSettings(), Plain),
        new SettingsFile("imaging.json", "Imaging", typeof(ImagingSettings), SettingsTable.Imaging, () => new ImagingSettings(), WithEnumNames),
    };

    /// <summary>
    /// Hidden in Simple mode, but left as they are by a restore, as "File:Name".
    /// The relay addresses ship blank - put back, the club's website and the
    /// HeatCaller voice would lose their relay. The crowd's layers, its source
    /// and its switch are what the soundscape in use set - put back, the crowd
    /// would fall silent until a soundscape was applied again.
    /// </summary>
    internal static readonly IReadOnlySet<string> LeftAlone = new HashSet<string>(StringComparer.Ordinal)
    {
        $"App:{nameof(AppSettings.RelayBaseUrl)}",
        $"App:{nameof(AppSettings.VoiceRelayBaseUrl)}",
        $"Ambience:{nameof(AmbienceSettings.Layers)}",
        $"Ambience:{nameof(AmbienceSettings.Source)}",
        $"Ambience:{nameof(AmbienceSettings.Enabled)}",
    };

    /// <summary>Whether a restore puts a setting back: hidden in Simple (and, asked, shown too), and not left alone.</summary>
    internal static bool Resets(SettingsFile file, string name, SettingGroup group, bool alsoShown) =>
        !LeftAlone.Contains($"{file.Label}:{name}")
        && (group == SettingGroup.HiddenInSimple || (alsoShown && group == SettingGroup.ShownInSimple));

    /// <summary>
    /// One file's JSON with what a restore puts back set to its default, and
    /// settings.json's mode back to Simple. Everything else in the file - the
    /// sealed secrets included - is carried over as it was. Hands back the
    /// new JSON and the names it changed.
    /// </summary>
    internal static (string Json, IReadOnlyList<string> Changed) ResetJson(SettingsFile file, string json, bool alsoShown)
    {
        var node = JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        var defaults = file.Defaults();
        var changed = new List<string>();

        foreach (var property in file.Type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!property.CanRead
                || property.GetIndexParameters().Length > 0
                || !file.Table.TryGetValue(property.Name, out var group)
                || !Resets(file, property.Name, group, alsoShown))
            {
                continue;
            }

            var fresh = JsonSerializer.SerializeToNode(property.GetValue(defaults), property.PropertyType, file.Json);

            if (!JsonNode.DeepEquals(node[property.Name], fresh))
            {
                node[property.Name] = fresh;
                changed.Add(property.Name);
            }
        }

        if (file.Type == typeof(AppSettings))
        {
            node[nameof(AppSettings.SettingsMode)] = JsonSerializer.SerializeToNode(SettingsMode.Simple, file.Json);
        }

        return (node.ToJsonString(file.Json), changed);
    }

    /// <summary>
    /// Puts back every file in <paramref name="folder"/> (the data folder):
    /// each one there is reset, one that is not is left for its store to make
    /// at the next start, with the defaults. Hands back what changed, as
    /// "Label: Name". The caller has saved a copy first (SaveCopy).
    /// </summary>
    internal static IReadOnlyList<string> ResetFiles(string folder, bool alsoShown)
    {
        var changed = new List<string>();

        foreach (var file in Files)
        {
            var path = Path.Combine(folder, file.Name);

            if (!File.Exists(path))
            {
                continue;
            }

            var (json, names) = ResetJson(file, File.ReadAllText(path), alsoShown);

            SafeFile.WriteAllText(path, json);
            changed.AddRange(names.Select(name => $"{file.Label}: {name}"));
        }

        return changed;
    }

    // ==================== The copies ====================

    /// <summary>The copies' folder, in the data folder.</summary>
    internal static string CopiesFolder(string root) => Path.Combine(root, "Settings Backups");

    /// <summary>How many copies are kept: the oldest go once there are more.</summary>
    internal const int CopiesKept = 10;

    /// <summary>
    /// The settings files as they are now, zipped into the copies folder and
    /// named for <paramref name="when"/> - a restore's first step, so it can
    /// be put back. The oldest past <see cref="CopiesKept"/> are deleted.
    /// Hands back the zip's path.
    /// </summary>
    internal static string SaveCopy(string root, DateTime when)
    {
        var folder = CopiesFolder(root);
        Directory.CreateDirectory(folder);

        var zip = Path.Combine(folder, $"settings {when:yyyy-MM-dd HH-mm-ss}.zip");

        // Two in one second: the second gets a number rather than replacing the first.
        for (var n = 2; File.Exists(zip); n++)
        {
            zip = Path.Combine(folder, $"settings {when:yyyy-MM-dd HH-mm-ss} ({n}).zip");
        }

        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (var file in Files)
            {
                var path = Path.Combine(root, file.Name);

                if (File.Exists(path))
                {
                    archive.CreateEntryFromFile(path, file.Name);
                }
            }
        }

        foreach (var old in Copies(root).Skip(CopiesKept))
        {
            try
            {
                File.Delete(old.Path);
            }
            catch (IOException)
            {
                // Kept one more time; the next restore tries again.
            }
        }

        return zip;
    }

    /// <summary>The copies there are, newest first.</summary>
    internal static IReadOnlyList<(string Path, DateTime When)> Copies(string root)
    {
        var folder = CopiesFolder(root);

        return !Directory.Exists(folder)
            ? Array.Empty<(string, DateTime)>()
            : Directory.GetFiles(folder, "settings *.zip")
                .Select(path => (Path: path, When: File.GetLastWriteTime(path)))
                .OrderByDescending(copy => copy.When)
                .ToList();
    }

    /// <summary>
    /// Puts a copy's files back in the data folder, over what is there. Only
    /// the five settings files, whatever else a zip holds. The caller freezes
    /// writes and restarts after.
    /// </summary>
    internal static void PutBack(string root, string zip)
    {
        var names = Files.Select(file => file.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        using var archive = ZipFile.OpenRead(zip);

        foreach (var entry in archive.Entries.Where(entry => names.Contains(entry.FullName)))
        {
            using var reader = new StreamReader(entry.Open());
            SafeFile.WriteAllText(Path.Combine(root, entry.FullName), reader.ReadToEnd());
        }
    }

    // ==================== Words ====================

    internal const string ConfirmTitle = "Restore default settings?";

    /// <summary>What goes back, in plain words - the areas the hidden settings are in.</summary>
    internal static readonly IReadOnlyList<string> WhatGoesBack = new[]
    {
        "Sound tuning: ducking, imaging levels, what starts and stops with a session, the ambience and music behavior, imaging and effects",
        "Voice tuning: the ElevenLabs model and voice parameters, how much the announcer says, and every race call's own settings",
        "AI commentary: how it's written, who writes it, and every piece behind its section switches",
        "Race format tuning: minimum lap, streaks, stagger, warm-up, stage and countdown times, finishing",
        "The cast beyond the announcer and co-host",
        "The sidebar's order and hidden pages, and the columns on Race Control, Practice, the Big Screen and the web",
        "Club website extras: what gets published, its look, personal PINs, emails, the race-day manager, Room & Web",
        "Connection extras: the timing thread, pass recording and the split loop",
    };

    /// <summary>What a restore keeps, in plain words.</summary>
    internal const string WhatIsKept =
        "Kept: your drivers, classes, tracks, race days and results, sponsors, venues and garage; your keys, the club website's "
        + "club and token and address, PINs and email sign-in; the decoder's connection and port; backups; and the soundscape's crowd.";

    internal const string AlsoShownTick = "Also reset what Simple mode shows (track, lap count, voice, volumes, look, commentary switches)";

    internal const string CopyLine = "Your current settings are saved first. Put Back Earlier Settings puts them back.";

    internal const string RaceLine = "Not while a race or practice is running - finish it first.";

    internal const string RestartLine = "HeatCaller restarts to use them.";
}
