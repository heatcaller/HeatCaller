using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeatCaller;

/// <summary>
/// Which ambience layers are allowed to play.
///
/// Music has the same idea as a pair - theme music or my music, one or the
/// other - because a playlist REPLACES a playlist. Ambience layers mix, so
/// a soundscape's crowd bed and a club's own track hum are meant to be
/// heard together, and forcing a choice between them would take away the
/// thing layers are for. Hence three, not two.
///
/// Both is 0 so that a settings file written before this existed reads back
/// as Both, which is exactly how it behaved.
/// </summary>
public enum AmbienceSource
{
    /// <summary>Everything set up plays - the soundscape's beds and the club's own layers together.</summary>
    Both = 0,

    /// <summary>Only the beds a soundscape supplied. The club's own layers stay configured but silent.</summary>
    Soundscape = 1,

    /// <summary>Only the club's own layers. The soundscape's beds stay configured but silent.</summary>
    Mine = 2,
}

/// <summary>
/// One continuous audio bed - crowd, track noise, pit lane - with a volume
/// per race intensity.
///
/// Storing a volume per intensity rather than a single level with a
/// multiplier is what lets layers behave differently from each other: track
/// noise should be loud while cars are running and near-silent between
/// races, whereas a crowd bed does roughly the opposite around a finish.
/// </summary>
public sealed class AmbienceLayer
{
    /// <summary>Stable identifier used to match a layer to its mixer input. Not shown to the user.</summary>
    public string Key { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// True for a bed a soundscape put here, false for one the club added.
    /// </summary>
    /// <remarks>
    /// Read off the key rather than stored, and deliberately so. The key
    /// prefix is ALREADY the authority on this everywhere else - it is how
    /// ThemeStore.ApplyBackground finds the old soundscape's beds to replace
    /// without touching the club's own layers, and how the crowd-roar
    /// migration found what to re-level. A stored flag beside it would be a
    /// second answer to the same question, which is one more than a question
    /// should have, and every settings file already written would need
    /// migrating to agree with the key it already has.
    ///
    /// Not serialised: it is derived, and writing it out would invite
    /// exactly the disagreement this avoids.
    /// </remarks>
    [JsonIgnore]
    public bool FromSoundscape =>
        Key.StartsWith(SoundPacks.BedLayerKeyPrefix, StringComparison.Ordinal);

    /// <summary>Whether this layer is allowed to play under the chosen source.</summary>
    public bool PlaysUnder(AmbienceSource source) => source switch
    {
        AmbienceSource.Soundscape => FromSoundscape,
        AmbienceSource.Mine => !FromSoundscape,
        _ => true,
    };

    /// <summary>What the layer list calls where this came from.</summary>
    [JsonIgnore]
    public string SourceLabel => FromSoundscape ? "Soundscape" : "Yours";

    /// <summary>
    /// Plays at a constant level regardless of what the race is doing, and
    /// keeps playing between races.
    ///
    /// For a background theme or a club's own uploaded track - content
    /// that shouldn't swell with a fast lap or drop away at the checkered
    /// flag. Reactive layers answer to race state; these deliberately
    /// don't, so the per-intensity volumes below are ignored in favour of
    /// ConstantVolume.
    /// </summary>
    public bool AlwaysOn { get; set; }

    /// <summary>The fixed level used when AlwaysOn - the per-state volumes don't apply.</summary>
    public float ConstantVolume { get; set; } = 0.35f;

    // Volumes per intensity, 0-1. Defaults give a usable mix out of the box
    // for a crowd-style bed; track-noise layers will want editing.
    public float IdleVolume { get; set; } = 0.15f;
    public float AnticipationVolume { get; set; } = 0.30f;
    public float RacingVolume { get; set; } = 0.45f;
    public float ExcitedVolume { get; set; } = 0.70f;
    public float PeakVolume { get; set; } = 1.0f;

    public float VolumeFor(AmbienceService.Intensity intensity) =>
        AlwaysOn ? ConstantVolume : VolumeForReactive(intensity);

    private float VolumeForReactive(AmbienceService.Intensity intensity) => intensity switch
    {
        AmbienceService.Intensity.Anticipation => AnticipationVolume,
        AmbienceService.Intensity.Racing => RacingVolume,
        AmbienceService.Intensity.Excited => ExcitedVolume,
        AmbienceService.Intensity.Peak => PeakVolume,
        _ => IdleVolume,
    };

    public void SetVolumeFor(AmbienceService.Intensity intensity, float value)
    {
        value = Math.Clamp(value, 0f, 1f);

        switch (intensity)
        {
            case AmbienceService.Intensity.Anticipation: AnticipationVolume = value; break;
            case AmbienceService.Intensity.Racing: RacingVolume = value; break;
            case AmbienceService.Intensity.Excited: ExcitedVolume = value; break;
            case AmbienceService.Intensity.Peak: PeakVolume = value; break;
            default: IdleVolume = value; break;
        }
    }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "(unnamed layer)" : Name;

    /// <summary>
    /// A separate copy, Key included, for an editor to change freely - so
    /// Cancel really does leave the saved layer untouched. Every property is
    /// a value or a string, so a member-by-member copy is a complete one.
    /// </summary>
    public AmbienceLayer Clone() => (AmbienceLayer)MemberwiseClone();
}

/// <summary>Ambience configuration, persisted alongside the other audio settings.</summary>
public sealed class AmbienceSettings
{
    /// <summary>
    /// Off by default - this needs audio files the user has to supply, and
    /// an app that silently wants something it doesn't have is worse than
    /// one that plainly says the feature is disabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Scales every layer, so the whole bed can be pulled down without re-tuning each one.</summary>
    public float MasterVolume { get; set; } = 0.6f;

    /// <summary>
    /// Which layers play: the soundscape's beds, the club's own, or both.
    ///
    /// A way to silence one side without DELETING it. Before this, muting a
    /// soundscape's crowd meant unticking each of its beds one at a time -
    /// and applying any soundscape afterwards put them back ticked, because
    /// ApplyBackground replaces that pair wholesale. Going the other way was
    /// worse: to hear only the soundscape you had to untick layers you had
    /// built yourself, and there was nothing to tell you which ones those
    /// were.
    /// </summary>
    public AmbienceSource Source { get; set; } = AmbienceSource.Both;

    /// <summary>The layers that would play as things stand - enabled, and allowed by <see cref="Source"/>.</summary>
    [JsonIgnore]
    public IEnumerable<AmbienceLayer> PlayableLayers =>
        Layers.Where(l => l.Enabled && l.PlaysUnder(Source));

    /// <summary>
    /// Whether race events cause brief surges. With this off the ambience
    /// still follows the broad race state (idle / racing / finished) but
    /// stops reacting to individual moments - quieter, for clubs that find
    /// constant reaction distracting.
    /// </summary>
    public bool ReactToEvents { get; set; } = true;

    /// <summary>How long a rise takes. Quick - a crowd swells fast.</summary>
    public double FadeUpSeconds { get; set; } = 1.5;

    /// <summary>
    /// How long a fall takes. Deliberately slower than the rise by default:
    /// a crowd settling down is a gradual thing, and matching that asymmetry
    /// is most of what makes the transition sound natural.
    /// </summary>
    public double FadeDownSeconds { get; set; } = 4.0;

    /// <summary>How long a surge lasts before settling back.</summary>
    public int SurgeSeconds { get; set; } = 6;

    /// <summary>
    /// How long the crowd stays up after the checkered flag before settling
    /// to idle.
    ///
    /// Separate from SurgeSeconds because a finish is not a momentary
    /// spike - the celebration genuinely runs longer than a reaction to a
    /// fast lap, and it ends by dropping to IDLE rather than returning to
    /// whatever was playing before.
    /// </summary>
    public int FinishCelebrationSeconds { get; set; } = 20;

    /// <summary>
    /// Whether ambience drops while an announcement is speaking, so the
    /// voice stays intelligible over a bed of crowd noise.
    ///
    /// This is the standard broadcast approach and is far more effective
    /// than simply raising the voice: the two compete in the same
    /// frequency range, so pushing the voice up mostly makes everything
    /// louder rather than clearer.
    /// </summary>
    public bool DuckUnderAnnouncements { get; set; } = true;

    /// <summary>How far ambience drops while speaking, as a fraction of its current level. 0.35 = down to 35%.</summary>
    public float DuckToFraction { get; set; } = 0.35f;

    /// <summary>
    /// Extra gain applied to spoken announcements while ambience is
    /// playing loudly. Applied on top of ducking rather than instead of it.
    /// </summary>
    public bool BoostVoiceOverAmbience { get; set; } = true;

    /// <summary>Multiplier for the announcement volume during loud ambience. 1.0 = no change.</summary>
    public float VoiceBoostMultiplier { get; set; } = 1.3f;

    // Which events trigger a surge. Separated so a club can keep the big
    // moments and drop the frequent ones - a fast lap can fire many times
    // in a race, a track record almost never.
    public bool SurgeOnFastLap { get; set; } = true;
    public bool SurgeOnLeadChange { get; set; } = true;
    public bool SurgeOnTrackRecord { get; set; } = true;
    public bool SurgeOnJumpStart { get; set; }

    // Off by default - fires for every driver who beats their own time, so
    // on a full grid it surges constantly.
    public bool SurgeOnPersonalBest { get; set; }

    /// <summary>A consistency streak reaching its threshold.</summary>
    public bool SurgeOnStreak { get; set; }

    /// <summary>The last lap - the crowd knows it's nearly over.</summary>
    public bool SurgeOnLastLap { get; set; } = true;

    /// <summary>The checkered flag. Peak rather than a normal surge.</summary>
    public bool SurgeOnFinish { get; set; } = true;

    /// <summary>
    /// A close battle developing - two cars on the same lap within a
    /// second of each other. Fires at most once per race so a sustained
    /// scrap doesn't surge on every single lap.
    /// </summary>
    public bool SurgeOnCloseBattle { get; set; } = true;

    /// <summary>A car being lapped - the field spreading out.</summary>
    public bool SurgeOnLapped { get; set; }

    public List<AmbienceLayer> Layers { get; set; } = new();

    /// <summary>
    /// Which one-off update to the soundscape crowd layers this file has
    /// had - see AmbienceStore.UpdateSoundscapeCrowd. 0 in a file from
    /// before the crowd roar existed.
    /// </summary>
    public int SoundscapeCrowdVersion { get; set; }
}

/// <summary>Loads/saves ambience settings.</summary>
public sealed class AmbienceStore
{
    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "ambience.json");

    public AmbienceSettings Current { get; private set; } = new();

    public static AmbienceStore Load()
    {
        var store = new AmbienceStore();

        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AmbienceSettings>(SafeFile.ReadAllTextWithRetry(FilePath));
                if (loaded is not null)
                {
                    loaded.Layers = (loaded.Layers ?? new List<AmbienceLayer>())
                        .Where(l => l is not null)
                        .ToList();

                    // A layer's sound on another machine is dropped, keeping
                    // the layer and its volumes. Even checking such a path
                    // exists makes Windows log on to that server - see
                    // PlainFileName.IsNetworkPath - and this file can come
                    // from a restored backup. Picking the file again from
                    // this PC's own drive puts it back.
                    foreach (var layer in loaded.Layers)
                    {
                        // A hand-edited or damaged file can say "Key": null.
                        // Everything that finds a soundscape's crowd layers
                        // reads the key with StartsWith - applying any
                        // soundscape then crashed on it - and the player keys
                        // its mixer inputs by it. A fresh key only costs the
                        // layer its link to a mixer input it never had.
                        if (string.IsNullOrEmpty(layer.Key))
                        {
                            layer.Key = Guid.NewGuid().ToString("N");
                        }

                        layer.Name ??= string.Empty;
                        layer.FilePath ??= string.Empty;

                        if (PlainFileName.IsNetworkPath(layer.FilePath))
                        {
                            layer.FilePath = string.Empty;
                        }
                    }

                    store.Current = loaded;

                    // Its own guard: a one-off tidy-up that trips over an
                    // odd layer must not send a readable file down the
                    // "unreadable" path below and reset the club's ambience.
                    try
                    {
                        if (UpdateSoundscapeCrowd(loaded))
                        {
                            store.Save();
                        }
                    }
                    catch
                    {
                        // Left as loaded; tried again next start.
                    }
                }
            }
            else
            {
                // A new file has no old soundscape crowd to bring up to date -
                // and must not have one it gets later "updated" (a roar the
                // club deleted put back) on the next start.
                store.Current.SoundscapeCrowdVersion = CurrentSoundscapeCrowdVersion;
            }
        }
        catch (Exception ex)
        {
            // Unreadable file: start with defaults rather than refusing to open.
            // But, when it could not be read at all or no copy of it could be
            // kept, never save over it this session (see LoadedUnreadable).
            store.LoadedUnreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
        }

        return store;
    }

    /// <summary>
    /// True when ambience.json was there but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. A file locked for a moment at
    /// launch used to load with no layers, and the first fader moved or
    /// soundscape applied replaced the club's own layers and settings.
    /// </summary>
    public bool LoadedUnreadable { get; private set; }

    /// <summary>
    /// Brings a soundscape crowd added by an older version up to date: the
    /// crowd roar that now goes with every bed is added beside it, and a bed
    /// still at the old levels - up to full volume, back when the bed had
    /// to do the cheering itself - comes down to the new, low ones.
    ///
    /// Only what a soundscape put there, and only what nobody has changed
    /// since: a bed whose levels were tuned by hand keeps them, a roar the
    /// club deleted is added back only by applying the soundscape again
    /// (there is no telling "deleted" from "never had one", so this adds it
    /// once, when the bed is found without it), and a missing roar file adds
    /// nothing. True when anything changed, so the caller saves.
    ///
    /// Runs once per file (SoundscapeCrowdVersion), so a roar the club
    /// deletes afterwards stays deleted - except when the roar file isn't
    /// installed yet, in which case the check stays due and runs again next
    /// time, once the file has arrived with an update.
    /// </summary>
    /// <summary>The SoundscapeCrowdVersion a file is at once UpdateSoundscapeCrowd has nothing left to do.</summary>
    public const int CurrentSoundscapeCrowdVersion = 1;

    private static bool UpdateSoundscapeCrowd(AmbienceSettings settings)
    {
        const int current = CurrentSoundscapeCrowdVersion;

        if (settings.SoundscapeCrowdVersion >= current)
        {
            return false;
        }

        var changed = false;
        var waitingForFile = false;

        var beds = settings.Layers
            .Where(l => l.Key.StartsWith(SoundPacks.BedLayerKeyPrefix, StringComparison.Ordinal)
                        && !l.Key.EndsWith(SoundPacks.RoarLayerKeySuffix, StringComparison.Ordinal))
            .ToList();

        foreach (var bed in beds)
        {
            if (SoundPacks.Find(bed.Key[SoundPacks.BedLayerKeyPrefix.Length..]) is not { } pack)
            {
                continue;
            }

            var legacy = SoundPacks.LegacyBedVolumes(pack.Key);
            if (Near(bed.IdleVolume, legacy.Idle)
                && Near(bed.AnticipationVolume, legacy.Anticipation)
                && Near(bed.RacingVolume, legacy.Racing)
                && Near(bed.ExcitedVolume, legacy.Excited)
                && Near(bed.PeakVolume, legacy.Peak))
            {
                var v = SoundPacks.BedVolumes(pack.Key);
                bed.IdleVolume = v.Idle;
                bed.AnticipationVolume = v.Anticipation;
                bed.RacingVolume = v.Racing;
                bed.ExcitedVolume = v.Excited;
                bed.PeakVolume = v.Peak;
                changed = true;
            }

            // The old name, as ThemeStore wrote it - a renamed layer is the club's.
            if (string.Equals(bed.Name, $"{pack.Name} background (from the soundscape)", StringComparison.Ordinal))
            {
                bed.Name = SoundPacks.BedLayerName(pack);
                changed = true;
            }

            var roarKey = SoundPacks.RoarLayerKey(pack);
            if (settings.Layers.Any(l => string.Equals(l.Key, roarKey, StringComparison.Ordinal)))
            {
                continue;
            }

            if (SoundPacks.RoarLayer(pack.Key) is not { } roar)
            {
                waitingForFile = true;
                continue;
            }

            // Follows the bed: a bed switched off stays a silent pair.
            roar.Enabled = bed.Enabled;
            settings.Layers.Insert(settings.Layers.IndexOf(bed) + 1, roar);
            changed = true;
        }

        if (!waitingForFile)
        {
            settings.SoundscapeCrowdVersion = current;
            changed = true;
        }

        return changed;

        static bool Near(float a, float b) => Math.Abs(a - b) < 0.001f;
    }

    public void Save()
    {
        // Not written: the real file is almost certainly fine, and these
        // settings were built on the defaults it loaded as. The change still
        // applies this session. The console has already said why.
        if (LoadedUnreadable)
        {
            return;
        }

        try
        {
            // SafeFile, like the other stores: an atomic swap, and it
            // honours the freeze after a restore.
            SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Best-effort, consistent with the other stores.
        }
    }

    /// <summary>
    /// Raised after an undo has put layers or settings back in this store, so
    /// a page showing them re-reads it. Not raised by ordinary saves.
    /// </summary>
    public event Action? Restored;

    /// <summary>Tells whatever shows this store that an undo just changed it. UI thread.</summary>
    public void NotifyRestored() => Restored?.Invoke();

    public string Snapshot() => JsonSerializer.Serialize(Current);

    public void Restore(string snapshot)
    {
        try
        {
            var restored = JsonSerializer.Deserialize<AmbienceSettings>(snapshot);
            if (restored is not null)
            {
                Current = restored;
                Save();
            }
        }
        catch
        {
            // A failed restore leaves the current state alone.
        }
    }
}
