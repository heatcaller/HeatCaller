using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Loads/saves the SoundEffectsProfile to %AppData%\HeatCaller\sound-effects.json.
/// Same load/save-in-place pattern as VoiceSettingsStore.
/// </summary>
public sealed class SoundEffectsStore
{
    public SoundEffectsProfile Current { get; private set; }

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "sound-effects.json");

    private SoundEffectsStore(SoundEffectsProfile profile)
    {
        Current = profile;
    }

    public static SoundEffectsStore Load()
    {
        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var json = SafeFile.ReadAllTextWithRetry(FilePath);
                var loaded = JsonSerializer.Deserialize<SoundEffectsProfile>(json);
                if (loaded is not null)
                {
                    // Merge in any sound kinds added since this file was last saved
                    // (also handles the old fixed-property schema transparently,
                    // since Effects will just be empty and get fully populated).
                    FillMissing(loaded);
                    ClearNetworkPaths(loaded);
                    return new SoundEffectsStore(loaded);
                }
            }
        }
        catch (Exception ex)
        {
            // Corrupt/unreadable file: start fresh rather than crashing on launch.
            // But, when it could not be read at all or no copy of it could be
            // kept, never save over it this session (see LoadedUnreadable).
            unreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
        }

        return new SoundEffectsStore(SoundEffectsProfile.CreateDefault()) { LoadedUnreadable = unreadable };
    }

    /// <summary>
    /// True when the file was there but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. A briefly locked file used to load as the defaults,
    /// and the next save replaced every sound the club had chosen.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    /// <summary>
    /// Gives a profile read from a file an entry for every kind of sound,
    /// with no nulls in it. A kind added since the file was saved gets its
    /// default (an older fixed-property file has no Effects at all, and gets
    /// every one). So does an entry the file names as null, or a null list:
    /// a hand-edited or restored file - or a soundscape saved from one - can
    /// say "GoodLap": null, and the lap sounds then threw reading it. Used
    /// for this store's file and for the copies soundscapes hold.
    /// </summary>
    internal static void FillMissing(SoundEffectsProfile? profile)
    {
        if (profile is null)
        {
            return;
        }

        profile.Effects ??= new Dictionary<string, SoundEffectConfig>();

        // A kind the file has never heard of takes the pack most of the
        // club's other sounds are on, so a club running Pit Lane doesn't get
        // one Classic beep among them. Read before anything is added. A file
        // with no sounds in it at all gets Classic, as before.
        var pack = profile.Effects.Values
            .Where(v => v is not null)
            .GroupBy(v => v.BuiltInSound ?? string.Empty)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault() ?? string.Empty;

        // A jump start played the invalid-lap buzzer before it had a sound of
        // its own, so a club with that buzzer on heard one at every jump. It
        // keeps hearing one - its own new sound, switched on where the
        // buzzer was.
        var jumpStartIsNew = !profile.Effects.TryGetValue(nameof(SoundEffectKind.JumpStart), out var oldJump) || oldJump is null;
        var warningIsNew = !profile.Effects.TryGetValue(nameof(SoundEffectKind.Warning), out var oldWarning) || oldWarning is null;
        profile.Effects.TryGetValue(nameof(SoundEffectKind.InvalidLap), out var invalidLap);

        foreach (var (key, value) in SoundEffectsProfile.CreateDefault().Effects)
        {
            if (!profile.Effects.TryGetValue(key, out var existing) || existing is null)
            {
                value.BuiltInSound = pack;
                profile.Effects[key] = value;
            }
        }

        if (jumpStartIsNew && invalidLap is not null)
        {
            var jump = profile.Effects[nameof(SoundEffectKind.JumpStart)];
            jump.Enabled = invalidLap.Enabled;
            jump.EnabledInPractice = invalidLap.EnabledInPractice;
        }

        // The same for a warning or black flag, which played that buzzer
        // too - but on rather than off by default, so it is set from the
        // buzzer even when the file had none (and so the default, off). And
        // the same buzzer, the club's own file or pack included, so nothing
        // about how a warning sounds changes until the club changes it.
        if (warningIsNew)
        {
            var buzzer = profile.Effects[nameof(SoundEffectKind.InvalidLap)];
            var warning = profile.Effects[nameof(SoundEffectKind.Warning)];
            warning.Enabled = buzzer.Enabled;
            warning.EnabledInPractice = buzzer.EnabledInPractice;
            warning.CustomFilePath = buzzer.CustomFilePath;
            warning.BuiltInSound = buzzer.BuiltInSound;
        }

        // Nulls under a key this version doesn't know are dropped, and blank
        // text fields are made empty, so nothing that walks the list trips.
        foreach (var key in profile.Effects.Where(e => e.Value is null).Select(e => e.Key).ToList())
        {
            profile.Effects.Remove(key);
        }

        foreach (var effect in profile.Effects.Values)
        {
            effect.CustomFilePath ??= string.Empty;
            effect.BuiltInSound ??= string.Empty;
        }
    }

    /// <summary>
    /// Drops any custom sound that points at another machine, leaving that
    /// effect on its built-in sound. Even checking such a path exists makes
    /// Windows log on to the server (see PlainFileName.IsNetworkPath), and
    /// these profiles come from files a restored backup writes - this one,
    /// and the copies held in themes.
    /// </summary>
    internal static void ClearNetworkPaths(SoundEffectsProfile? profile)
    {
        if (profile?.Effects is null)
        {
            return;
        }

        foreach (var effect in profile.Effects.Values)
        {
            if (effect is not null && PlainFileName.IsNetworkPath(effect.CustomFilePath))
            {
                effect.CustomFilePath = string.Empty;
            }
        }
    }

    /// <summary>
    /// Keeps <paramref name="profile"/> as the current sounds and writes it to
    /// the file. True when it reached the file; false when it was only kept in
    /// memory (writes held after an unreadable load, writes frozen for a
    /// restore, or the write itself failed).
    /// </summary>
    // WHY (23 Nov 2026, bug hunt 24): Save was void and threw away the answer
    // SafeFile.WriteAllText gives, so a write that failed (disk full, the file
    // read-only or held by a sync tool, writes frozen for a restore) looked
    // exactly like one that worked - the Sound Effects page said "Saved" and
    // the sounds were back as they were at the next start. Same pattern as
    // VoiceSettingsStore.Save, which already returns its result.
    public bool Save(SoundEffectsProfile profile)
    {
        Current = profile;

        // Not written: the real file is almost certainly fine, and this
        // profile was built on the defaults it loaded as. Kept in memory so
        // the change still plays this session. The console has already said why.
        if (LoadedUnreadable)
        {
            return false;
        }

        var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
        return SafeFile.WriteAllText(FilePath, json);
    }

    /// <summary>
    /// A separate copy of a profile, for an undo to hold or to change and
    /// save. Saved as a NEW object on purpose: the Sound Effects page tells
    /// "someone else saved" from its own saves by the profile no longer being
    /// the one it last wrote.
    /// </summary>
    public static SoundEffectsProfile Copy(SoundEffectsProfile profile) =>
        JsonSerializer.Deserialize<SoundEffectsProfile>(JsonSerializer.Serialize(profile)) ?? SoundEffectsProfile.CreateDefault();

    /// <summary>
    /// Raised after an undo has put settings back in this store, so a page
    /// showing them re-reads it. Not raised by ordinary saves - the page that
    /// made those already shows them.
    /// </summary>
    public event Action? Restored;

    /// <summary>Tells whatever shows this store that an undo just changed it. UI thread.</summary>
    public void NotifyRestored() => Restored?.Invoke();
}
