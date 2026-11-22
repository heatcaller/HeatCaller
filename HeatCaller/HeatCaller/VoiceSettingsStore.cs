using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Loads/saves the single VoiceProfile to %AppData%\HeatCaller\voice-settings.json.
/// Only one profile for now (this is a POC); Current is updated in place on
/// Save so AnnouncementService picks up changes immediately, no restart needed.
/// </summary>
public sealed class VoiceSettingsStore
{
    public VoiceProfile Current { get; private set; }

    /// <summary>Where the profile lives, so the UI can say so in the log when a save doesn't land.</summary>
    internal static string SettingsPath => FilePath;

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "voice-settings.json");

    private VoiceSettingsStore(VoiceProfile profile)
    {
        Current = profile;
    }

    /// <summary>
    /// Bumped when the built-in template lines change. A settings file
    /// stamped with an older value gets its LINES replaced on next load.
    ///
    /// This exists because the merge below only ever added event types it
    /// had never seen, which meant a new set of default phrasings could
    /// never reach anyone who already had a settings file - and every
    /// existing install has one. Version-stamping is what lets a genuine
    /// content update land without also wiping settings on every launch.
    ///
    /// 4 (10 Nov 2026): every line retagged for Eleven v4 - [shouting] and
    /// [excited] only on the moments that earn them (see AudioTagTiers) -
    /// and every saved voice moved to eleven_v4_turbo.
    ///
    /// 5 (11 Nov 2026): a driver's own bests - personal bests, streaks,
    /// qualifying improvements, achievements - lose [excited] and [shouting]
    /// (AudioTagTiers.Unlifted); their [cheerful] and [awe] stay. A file on 4
    /// only has those two tags taken out of those calls; it is not refreshed.
    /// One older than 4 is, as before.
    /// </summary>
    internal const int CurrentTemplateVersion = 5;

    private static readonly System.Text.RegularExpressions.Regex LoudTag =
        new(@"\[(excited|shouting)\] ?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// Takes [excited] and [shouting] out of the lines of a driver's own
    /// bests (AudioTagTiers.Unlifted), club-written lines included - for
    /// those calls the loud tag is the thing being removed, not the wording.
    /// Any other tag ([cheerful], [awe]) stays. True when anything changed.
    /// </summary>
    internal static bool StripLoudTagsFromUnliftedCalls(VoiceProfile profile)
    {
        var changed = false;

        foreach (var type in AudioTagTiers.Unlifted)
        {
            if (!profile.Events.TryGetValue(type.ToString(), out var config) || config is null)
            {
                continue;
            }

            var plain = config.EffectiveTemplates.Select(line => LoudTag.Replace(line, string.Empty).Trim()).ToList();

            if (!plain.SequenceEqual(config.EffectiveTemplates, StringComparer.Ordinal))
            {
                config.Templates = plain;
                config.Template = string.Empty;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Built-in lines that have been reworded since a club's file was saved
    /// (17 Oct 2026: achievements, not badges; American spelling), old to
    /// new. Only an exact match is changed - a line a club wrote or edited
    /// itself is left as they wrote it.
    /// </summary>
    private static readonly Dictionary<string, string> RewordedLines = new(StringComparer.Ordinal)
    {
        ["[cheerful] Badge for {driver} - {achievement}."] = "[cheerful] Achievement for {driver} - {achievement}.",
        ["[excited] {driver} is dialled in - {streak} straight!"] = "[excited] {driver} is dialed in - {streak} straight!",
        ["[shouting] Chequers are out. Race complete!"] = "[shouting] Checkered flag is out. Race complete!",

        // WHY (22 Nov 2026, the owner): the one track record line that didn't name the driver.
        // A record broken twice in a race sounded like the first driver's, said again.
        ["[awe] That is the fastest lap this track has ever seen!"] = "[awe] {driver}, {time} - that is the fastest lap this track has ever seen!",
    };

    /// <summary>
    /// Brings a loaded file's lines up to the built-in wording without a
    /// full refresh (which would throw away a club's own lines): the
    /// achievement announcement's old {badge} placeholder becomes
    /// {achievement}, and the lines in RewordedLines are swapped for their
    /// new wording. True when anything changed, so the file is saved once
    /// and this finds nothing to do after. Both placeholders are still
    /// filled in when a line is spoken.
    /// </summary>
    internal static bool UpdateOldLines(VoiceProfile profile)
    {
        var changed = false;

        foreach (var (key, config) in profile.Events)
        {
            if (config is null)
            {
                continue;
            }

            var achievement = key == nameof(AnnouncementEventType.AchievementUnlocked);

            string Update(string line)
            {
                var swapped = achievement
                    ? line.Replace("{badge}", "{achievement}", StringComparison.OrdinalIgnoreCase)
                    : line;

                return RewordedLines.TryGetValue(swapped, out var reworded) ? reworded : swapped;
            }

            if (config.Templates is { } lines)
            {
                for (var i = 0; i < lines.Count; i++)
                {
                    if (lines[i] is { } line && Update(line) is var updated && !string.Equals(updated, line, StringComparison.Ordinal))
                    {
                        lines[i] = updated;
                        changed = true;
                    }
                }
            }

            if (!string.IsNullOrEmpty(config.Template) && Update(config.Template) is var single
                && !string.Equals(single, config.Template, StringComparison.Ordinal))
            {
                config.Template = single;
                changed = true;
            }
        }

        return changed;
    }

    public static VoiceSettingsStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = SafeFile.ReadAllTextWithRetry(FilePath);
                var loaded = JsonSerializer.Deserialize<VoiceProfile>(json);
                if (loaded is not null)
                {
                    // Files from before sealing carried the key in plain
                    // text. Taken in here and saved again below, so the
                    // plain copy is gone from disk from this launch on.
                    // Nulls from a hand-edited or restored file are filled
                    // first, so nothing below trips over one.
                    loaded.FillNulls();

                    var hadPlainKey = AdoptPlainTextApiKey(json, loaded);

                    // After the key is taken in, since having one is what
                    // decides it.
                    var engineChosen = ChooseEngineForOlderFile(loaded);

                    // Merge in any event types added since this file was last saved,
                    // so upgrading the app doesn't silently drop new announcement types.
                    //
                    // A null entry (or a null list) from a hand-edited or
                    // restored file is treated as missing too: the refresh
                    // below threw on one, and that threw every voice setting
                    // away as "unreadable".
                    var defaults = VoiceProfile.CreateDefault();
                    foreach (var (key, value) in defaults.Events)
                    {
                        if (!loaded.Events.TryGetValue(key, out var present) || present is null)
                        {
                            loaded.Events[key] = value;
                        }
                    }

                    // Repair weights the Voice Settings page used to reset:
                    // it saved every event without a weight box as a flat 50,
                    // which knocked Provisional Pole out of the per-lap cap
                    // and tied the pair lines. Those weights have no box, so
                    // a 50 on one whose default differs came from that save,
                    // not from the club; the default goes back.
                    foreach (var (key, defaultConfig) in defaults.Events)
                    {
                        if (Enum.TryParse<AnnouncementEventType>(key, out var type)
                            && !VoiceSettingsControl.PerLapWeightedTypes.Contains(type)
                            && loaded.Events.TryGetValue(key, out var existing)
                            && existing is not null
                            && existing.Weight == 50
                            && defaultConfig.Weight != 50)
                        {
                            existing.Weight = defaultConfig.Weight;
                        }
                    }

                    // 17 Oct 2026: achievements, not badges, and American
                    // spelling. The saved lines are moved over here - only the
                    // placeholder and the built-in lines that were reworded -
                    // so a club's own wording is left as they wrote it.
                    var renamed = UpdateOldLines(loaded);

                    if (loaded.TemplateVersion < CurrentTemplateVersion)
                    {
                        // Version 4 (10 Nov 2026) is the move to Eleven v4
                        // Turbo, for every club: the lines are written for
                        // how v4 plays a tag (see AudioTagTiers), so the
                        // model moves with them. The race-call model goes
                        // back to "same as Model" - v4 Turbo is quick enough
                        // for race calls, which is all Flash was there for.
                        //
                        // Version 5 (11 Nov 2026) is NOT a refresh: a file
                        // already on 4 has the v4 lines, so only a driver's
                        // own bests lose [excited] and [shouting] (see
                        // StripLoudTagsFromUnliftedCalls),
                        // and every other line a club wrote stays as it is.
                        var fullRefresh = loaded.TemplateVersion < 4;

                        if (fullRefresh)
                        {
                            loaded.ModelId = VoiceProfile.DefaultModelId;
                            loaded.RaceCallModelId = string.Empty;

                            // Replace ONLY the spoken lines. Enabled, Weight and
                            // every per-event toggle are deliberately untouched -
                            // this is a phrasing refresh, not a settings reset.
                            foreach (var (key, defaultConfig) in defaults.Events)
                            {
                                if (loaded.Events.TryGetValue(key, out var existing))
                                {
                                    existing.Templates = defaultConfig.Templates.ToList();

                                    // Clear the legacy single-template field too,
                                    // or it would shadow the list if that were
                                    // ever emptied later.
                                    existing.Template = string.Empty;
                                }
                            }
                        }
                        else
                        {
                            StripLoudTagsFromUnliftedCalls(loaded);
                        }

                        loaded.TemplateVersion = CurrentTemplateVersion;

                        var store = new VoiceSettingsStore(loaded) { RefreshedLines = fullRefresh };
                        store.Save(loaded);
                        return store;
                    }

                    var result = new VoiceSettingsStore(loaded);
                    if (hadPlainKey || engineChosen || renamed)
                    {
                        result.Save(loaded);
                    }

                    return result;
                }
            }
        }
        catch (Exception ex)
        {
            // Corrupt/unreadable file: start fresh rather than crashing on
            // launch - but, when it could not be read at all or no copy of
            // it could be kept, never save over it this session (see
            // LoadedUnreadable).
            var hold = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
            return new VoiceSettingsStore(VoiceProfile.CreateDefault()) { LoadedUnreadable = hold };
        }

        return new VoiceSettingsStore(VoiceProfile.CreateDefault());
    }

    /// <summary>
    /// True when voice-settings.json was there at startup but could not be
    /// read - see SafeFile.HoldWritesAfterUnreadable. The profile here is
    /// then the defaults rather than the club's, and <see cref="Save"/>
    /// writes nothing this session.
    ///
    /// This store used to fall back to defaults and let the next save write
    /// them over the file, as settings.json once did. The file holds the
    /// only copy on this PC of the sealed ElevenLabs key, the chosen voice
    /// and model, and every announcement line and toggle the club edited -
    /// and a backup, sync or antivirus tool holding it for a second at
    /// launch usually stops it being copied aside too. Nudging the
    /// announcer volume in the Mixer was enough to lose all of it. Secret
    /// recovery also asks this, since defaults that only LOOK unlocked
    /// would have it throw away the backup's copy of the key.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    /// <summary>
    /// True when this launch replaced the saved lines with the built-in ones
    /// (an older CurrentTemplateVersion). Those are the plain defaults; a
    /// club running a soundscape wants that soundscape's own lines back, and
    /// only the ThemeStore knows which one that is - see
    /// ThemeStore.RestoreActiveThemeLines.
    /// </summary>
    public bool RefreshedLines { get; private init; }

    /// <summary>
    /// Takes in an ElevenLabs key stored in plain text by an older version.
    /// True when the file still has one - even a blank one - so the caller
    /// re-saves without it.
    /// </summary>
    private static bool AdoptPlainTextApiKey(string json, VoiceProfile loaded)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(nameof(VoiceProfile.ApiKey), out var legacy)
                || legacy.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var plain = legacy.GetString();
            if (string.IsNullOrEmpty(loaded.ApiKey) && !string.IsNullOrEmpty(plain))
            {
                loaded.ApiKey = plain;
            }

            return true;
        }
        catch (JsonException)
        {
            // Deserialize already succeeded on this text, so this can't fail
            // in practice - and if it did there would be nothing to adopt.
            return false;
        }
    }

    /// <summary>
    /// Gives a file written before there was more than one voice a choice of
    /// engine, once - and moves a file that chose the retired Windows voice
    /// onto Kokoro. True when it changed anything, so the caller saves it
    /// and this never runs for that file again.
    ///
    /// A club with an ElevenLabs key - even one sealed on another PC and
    /// not yet recovered - keeps ElevenLabs; a file with no key at all was
    /// an announcer that could not speak, and now speaks with Kokoro.
    /// </summary>
    private static bool ChooseEngineForOlderFile(VoiceProfile loaded)
    {
        // Every engine this version knows, including the HeatCaller voice:
        // a choice this test did not list would be "inferred" back to
        // ElevenLabs or Kokoro on every single load, and the club would
        // find its voice changed each time the app started.
        if (loaded.Engine is VoiceEngine.ElevenLabs or VoiceEngine.HeatCaller or VoiceEngine.Kokoro)
        {
            return false;
        }

        // A file that chose the free Windows voice. By NUMBER, because no
        // enum member answers to 1 any more and System.Text.Json hands back
        // the integer as it found it - see VoiceEngines.RetiredWindows.
        //
        // Named here rather than left to InferEngine below, which would
        // have handed a club that had BOTH a key and the Windows voice
        // chosen back to ElevenLabs - a voice it had deliberately turned
        // off, and one it pays for by the character. Kokoro is the free
        // voice now, so "the free voice" is what it was and what it gets.
        if ((int?)loaded.Engine == VoiceEngines.RetiredWindows)
        {
            loaded.Engine = VoiceEngine.Kokoro;
            return true;
        }

        loaded.Engine = loaded.InferEngine();
        return true;
    }

    // --- The HeatCaller voice's allowance, remembered between runs ----------
    //
    // Two dates the announcer keeps about the club's monthly HeatCaller
    // allowance: until when the "nine tenths spent" warning has already been
    // said, and until when the allowance is known to be spent. Both used to
    // live only in memory, so the warning the docs promise "once a month"
    // was said again every time the app started, and a laptop restarted
    // mid-night after the allowance ran out played every saved line in the
    // HeatCaller voice again until the next new line was refused - half the
    // night in one voice and half in the other, which is exactly what the
    // spent state is there to prevent.
    //
    // In a file of their own rather than on the VoiceProfile: the Voice
    // Settings page saves a brand-new profile built from its boxes on every
    // edit, and a field it does not know about would be wiped by the next
    // keystroke there. A missing or unreadable file simply means "nothing
    // remembered", which is how every laptop starts.

    private static string AllowanceFilePath => Path.Combine(
        AppPaths.Root,
        "voice-allowance.json");

    /// <summary>What is written to voice-allowance.json. Both null by default, so an older or missing file reads as "nothing remembered".</summary>
    private sealed class HeatCallerAllowanceFile
    {
        public DateTime? WarnedUntilUtc { get; set; }
        public DateTime? SpentUntilUtc { get; set; }
    }

    /// <summary>Guards the allowance file: the announcer writes it from background renders as well as the UI thread.</summary>
    private readonly object _allowanceLock = new();

    /// <summary>The file as last read or written. Read once, on first use.</summary>
    private HeatCallerAllowanceFile? _allowance;

    /// <summary>Until when the "nine tenths spent" warning has been said, or null. The caller decides whether that is still in the future.</summary>
    internal DateTime? HeatCallerWarnedUntilUtc
    {
        get
        {
            lock (_allowanceLock)
            {
                return AllowanceLocked().WarnedUntilUtc;
            }
        }
    }

    /// <summary>Until when the allowance is known to be spent, or null. The caller decides whether that is still in the future.</summary>
    internal DateTime? HeatCallerSpentUntilUtc
    {
        get
        {
            lock (_allowanceLock)
            {
                return AllowanceLocked().SpentUntilUtc;
            }
        }
    }

    /// <summary>Remembers until when the warning has been said. Written only when it actually changes. Never throws.</summary>
    internal void SetHeatCallerWarnedUntil(DateTime? untilUtc)
    {
        lock (_allowanceLock)
        {
            var file = AllowanceLocked();

            if (file.WarnedUntilUtc == untilUtc)
            {
                return;
            }

            file.WarnedUntilUtc = untilUtc;
            WriteAllowanceLocked(file);
        }
    }

    /// <summary>Remembers until when the allowance is spent - null once the relay speaks again. Written only when it actually changes. Never throws.</summary>
    internal void SetHeatCallerSpentUntil(DateTime? untilUtc)
    {
        lock (_allowanceLock)
        {
            var file = AllowanceLocked();

            if (file.SpentUntilUtc == untilUtc)
            {
                return;
            }

            file.SpentUntilUtc = untilUtc;
            WriteAllowanceLocked(file);
        }
    }

    private HeatCallerAllowanceFile AllowanceLocked()
    {
        if (_allowance is not null)
        {
            return _allowance;
        }

        try
        {
            if (File.Exists(AllowanceFilePath))
            {
                _allowance = JsonSerializer.Deserialize<HeatCallerAllowanceFile>(SafeFile.ReadAllTextWithRetry(AllowanceFilePath));
            }
        }
        catch (Exception)
        {
            // Unreadable: nothing remembered. The worst that costs is the
            // warning said once more, or one line asked of the relay.
            _allowance = null;
        }

        _allowance ??= new HeatCallerAllowanceFile();
        return _allowance;
    }

    private static void WriteAllowanceLocked(HeatCallerAllowanceFile file)
    {
        try
        {
            // SafeFile never throws; the serialiser is inside the try only
            // because nothing about this bookkeeping may ever stop a line.
            SafeFile.WriteAllText(AllowanceFilePath, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // Remembered for this run either way.
        }
    }

    /// <summary>
    /// Saves the profile and makes it current. Returns false when the file
    /// could not be written (SafeFile reports that rather than throwing), or
    /// was not written because it could not be read at startup -
    /// the profile is current for this run either way. Most callers can
    /// ignore it; secret recovery cannot (SecretRecovery.Run).
    /// </summary>
    public bool Save(VoiceProfile profile)
    {
        // A key that could not be opened on this PC (sealed on the old one)
        // shows as an empty box on the settings page, and that page saves a
        // brand-new profile built from its boxes. Without this, saving any
        // voice setting before the key was recovered would throw away the
        // only copy of it. A key actually typed in always wins.
        if (!ReferenceEquals(profile, Current)
            && string.IsNullOrEmpty(profile.ApiKey)
            && !profile.ApiKeyLocked
            && Current.ApiKeyLocked)
        {
            profile.CopyApiKeyFrom(Current);
        }

        Current = profile;

        // Not written - and said so, through the answer. Still current for
        // this run, so a change made tonight is heard tonight; see
        // LoadedUnreadable for why it must not reach the file.
        if (LoadedUnreadable)
        {
            return false;
        }

        var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
        return SafeFile.WriteAllText(FilePath, json);
    }

    /// <summary>
    /// Raised after an undo has put settings back in this store, so a page
    /// showing them re-reads it. Not raised by ordinary saves - the page that
    /// made those already shows them.
    /// </summary>
    public event Action? Restored;

    /// <summary>Tells whatever shows this store that an undo just changed it. UI thread.</summary>
    public void NotifyRestored() => Restored?.Invoke();
}
