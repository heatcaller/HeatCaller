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

        // 2026-11-05, Bug hunt 11: every file read and reset first, and only
        // then any written. Read and written one at a time, a damaged
        // imaging.json (last in Files, and never rewritten by the pre-restore
        // save) threw JsonException after the other four were already reset
        // on disk - past the caller's catch, with no restart, so the running
        // app saved its old values back over them. A locked file part-way
        // did the same under "nothing was changed".
        var writes = new List<(string Path, string Json)>();

        foreach (var file in Files)
        {
            var path = Path.Combine(folder, file.Name);

            if (!File.Exists(path))
            {
                continue;
            }

            string json;
            IReadOnlyList<string> names;

            try
            {
                (json, names) = ResetJson(file, File.ReadAllText(path), alsoShown);
            }
            catch (JsonException)
            {
                // Damaged: left as it is. Its store already runs on the
                // defaults and keeps a copy of it aside, and the zip saved
                // first holds it too.
                continue;
            }

            writes.Add((path, json));
            changed.AddRange(names.Select(name => $"{file.Label}: {name}"));
        }

        WriteAll(writes);

        return changed;
    }

    /// <summary>
    /// 5 Nov 2026, Bug hunt 11 review: thrown by Restore Default Settings
    /// and Put Back only once at least one settings file has been written
    /// and a later one could not be. The caller then knows the files are
    /// mixed - it says so and restarts, or the running app would save its
    /// old values back over the files already written. Any other failure
    /// came before the first write, so nothing was changed. Not an
    /// IOException, so a catch of those never takes it for one.
    /// </summary>
    internal sealed class PartlyWrittenException(string message, Exception inner) : Exception(message, inner);

    /// <summary>
    /// The worked-out files written in turn. 5 Nov 2026, Bug hunt 11 review:
    /// SafeFile.WriteAllText reports a failed write by handing back false,
    /// not by throwing, so a locked or read-only file was skipped without a
    /// word and the caller said every setting was put back. A failure on
    /// the first file throws IOException (nothing written); on a later one,
    /// <see cref="PartlyWrittenException"/>. The rest are not tried.
    /// </summary>
    private static void WriteAll(IReadOnlyList<(string Path, string Json)> writes)
    {
        var written = 0;

        foreach (var (path, json) in writes)
        {
            Exception failed;

            try
            {
                if (SafeFile.WriteAllText(path, json))
                {
                    written++;
                    continue;
                }

                failed = new IOException($"HeatCaller couldn't write {Path.GetFileName(path)}.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed = ex;
            }

            if (written == 0)
            {
                throw failed as IOException ?? new IOException(failed.Message, failed);
            }

            throw new PartlyWrittenException(
                $"{written} of {writes.Count} settings files were written before {Path.GetFileName(path)} could not be. {failed.Message}", failed);
        }
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
    internal static string SaveCopy(string root, DateTime when, string? keep = null)
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

        // 2026-11-05, Bug hunt 11: never the copy being put back. With ten
        // already there, Put Back's own copy pruned the oldest - and when
        // that was the one picked, it was deleted before it was read.
        foreach (var old in Copies(root).Skip(CopiesKept)
            .Where(old => keep is null || !string.Equals(Path.GetFullPath(old.Path), Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase)))
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
    internal static void PutBack(string root, string zip) => WriteBack(root, ReadCopy(zip));

    /// <summary>
    /// Put Back Earlier Settings' whole job on the files: the picked copy
    /// read, the settings as they are now saved as a copy of their own, then
    /// the picked one put back. Hands back the new copy's path.
    /// </summary>
    internal static string PutBackWithCopy(string root, string zip, DateTime when)
    {
        // 2026-11-05, Bug hunt 11: read before the new copy is saved - that
        // save prunes to CopiesKept, and the oldest copy was the one picked.
        var files = ReadCopy(zip);
        var saved = SaveCopy(root, when, keep: zip);

        WriteBack(root, files);
        return saved;
    }

    /// <summary>A copy's settings files, by name - only the five, whatever else the zip holds.</summary>
    private static IReadOnlyList<(SettingsFile File, string Json)> ReadCopy(string zip)
    {
        var read = new List<(SettingsFile, string)>();

        using var archive = ZipFile.OpenRead(zip);

        foreach (var entry in archive.Entries)
        {
            if (Files.FirstOrDefault(file => string.Equals(file.Name, entry.FullName, StringComparison.OrdinalIgnoreCase)) is not { } file)
            {
                continue;
            }

            using var reader = new StreamReader(entry.Open());
            read.Add((file, reader.ReadToEnd()));
        }

        return read;
    }

    /// <summary>
    /// 2026-11-05, Bug hunt 11: a copy is put back the way a restore resets -
    /// only what a restore would put back (ShownInSimple and HiddenInSimple,
    /// less <see cref="LeftAlone"/>) comes from the copy; the live file's
    /// KeptOnRestore and Memory stay as they are. Written whole, a copy
    /// brought back PINs and racer PINs rotated since (with the sealed
    /// Secrets that still open here), a revoked consent, the relay
    /// bookkeeping, Advanced mode past the manager-PIN lock (or the lock
    /// itself off), and - from a copy planted in a restored backup - relay
    /// and mail addresses no RestoredSettings check ever saw. Every file is
    /// worked out before any is written.
    /// </summary>
    private static void WriteBack(string root, IReadOnlyList<(SettingsFile File, string Json)> copy)
    {
        var writes = new List<(string Path, string Json)>();
        var hold = RestoredSettings.Part.None;

        foreach (var (file, copyJson) in copy)
        {
            var path = Path.Combine(root, file.Name);
            var liveJson = File.Exists(path) ? File.ReadAllText(path) : null;
            var json = MergeJson(file, liveJson, copyJson);

            if (file.Type == typeof(AppSettings))
            {
                // Belt and braces: an address new to this laptop - possible
                // only when the live file could not be read - is held as a
                // restored backup's would be.
                hold |= RestoredSettings.WhatNeedsConfirming(liveJson, json);
            }

            writes.Add((path, json));
        }

        RestoredSettings.Hold(hold);

        WriteAll(writes);
    }

    /// <summary>
    /// One file as Put Back writes it: the live JSON with what a restore
    /// would reset taken from the copy. A live file that is missing or
    /// cannot be read leaves the copy as it is, its mode Simple. A copy
    /// that comes out the same is handed back word for word.
    /// </summary>
    internal static string MergeJson(SettingsFile file, string? liveJson, string copyJson)
    {
        JsonObject copyNode;

        try
        {
            copyNode = JsonNode.Parse(copyJson) as JsonObject
                ?? throw new InvalidDataException($"The copy's {file.Name} isn't settings HeatCaller can read.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The copy's {file.Name} can't be read: {ex.Message}", ex);
        }

        JsonObject? live = null;

        try
        {
            live = string.IsNullOrWhiteSpace(liveJson) ? null : JsonNode.Parse(liveJson) as JsonObject;
        }
        catch (JsonException)
        {
        }

        JsonObject merged;

        if (live is null)
        {
            merged = (JsonObject)copyNode.DeepClone();

            if (file.Type == typeof(AppSettings))
            {
                merged[nameof(AppSettings.SettingsMode)] = JsonSerializer.SerializeToNode(SettingsMode.Simple, file.Json);
            }
        }
        else
        {
            merged = live;

            foreach (var property in file.Type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (!file.Table.TryGetValue(property.Name, out var group)
                    || !Resets(file, property.Name, group, alsoShown: true)
                    || !copyNode.TryGetPropertyValue(property.Name, out var value))
                {
                    continue;
                }

                merged[property.Name] = value?.DeepClone();
            }
        }

        return JsonNode.DeepEquals(merged, copyNode) ? copyJson : merged.ToJsonString(file.Json);
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
