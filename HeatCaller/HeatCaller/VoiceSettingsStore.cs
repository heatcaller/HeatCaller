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
    /// </summary>
    internal const int CurrentTemplateVersion = 3;

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

                    if (loaded.TemplateVersion < CurrentTemplateVersion)
                    {
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

                        loaded.TemplateVersion = CurrentTemplateVersion;

                        var store = new VoiceSettingsStore(loaded);
                        store.Save(loaded);
                        return store;
                    }

                    var result = new VoiceSettingsStore(loaded);
                    if (hadPlainKey || engineChosen)
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
    /// Gives a file written before the free Windows voice existed a choice
    /// of engine, once. True when it had none (or one this version doesn't
    /// know), so the caller saves it and this never runs for it again.
    ///
    /// A club with an ElevenLabs key - even one sealed on another PC and
    /// not yet recovered - keeps ElevenLabs; a file with no key at all was
    /// an announcer that could not speak, and now speaks with Windows.
    /// </summary>
    private static bool ChooseEngineForOlderFile(VoiceProfile loaded)
    {
        // Every engine this version knows, including the HeatCaller voice:
        // a choice this test did not list would be "inferred" back to
        // ElevenLabs or Windows on every single load, and the club would
        // find its voice changed each time the app started.
        if (loaded.Engine is VoiceEngine.ElevenLabs or VoiceEngine.Windows or VoiceEngine.HeatCaller or VoiceEngine.Kokoro)
        {
            return false;
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
