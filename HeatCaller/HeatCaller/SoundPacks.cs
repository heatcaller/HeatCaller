namespace HeatCaller;

/// <summary>
/// The sound packs that ship with the app: one recorded file for every
/// SoundEffectKind, in five styles.
///
/// WHERE. Assets\SoundPacks\&lt;Pack&gt;\&lt;Kind&gt;.wav beside the executable,
/// copied there by the build (see the csproj). They are played from there
/// rather than copied into %AppData%: nobody edits them, and a new version
/// of the app replacing them is exactly what should happen.
///
/// HOW A SOUND IS CHOSEN. Per effect, in this order:
///   1. the club's own file, if one is set and still there;
///   2. the pack the effect is set to, if that file is there;
///   3. the generated tone ("Generated tones") - always available, so a
///      missing or damaged Assets folder costs the sound's style, never
///      the sound itself.
/// </summary>
public static class SoundPacks
{
    public sealed record Pack(string Key, string Name, string Description);

    /// <summary>The key a config uses for the generated tones. Empty, so every settings file written before packs existed means exactly that.</summary>
    public const string ClassicKey = "";

    public static readonly IReadOnlyList<Pack> All = new[]
    {
        new Pack("Podium", "Podium", "Polished bells and chimes - premium, clean, never harsh."),
        new Pack("Arcade", "Arcade", "8-bit chiptune - playful, punchy, cuts through motor whine."),
        new Pack("Broadcast", "Broadcast", "TV sports graphics - whooshes, brass stabs, hits, big hall."),
        // Key "Synthwave", shown as "Classic" (17 Oct 2026): Classic took
        // this pack over whole on 16 Oct, and the pickers, the crowd layer
        // and the soundscape cards still said Synthwave. The key stays -
        // every saved soundscape, sound setting and crowd layer names it.
        new Pack("Synthwave", "Classic", "80s supersaws, arps and gated reverb."),
        new Pack("PitLane", "Pit Lane", "Realistic trackside - decoder beeps, air horns, lap bell, crowd."),
    };

    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Assets", "SoundPacks");

    public static Pack? Find(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// "Generated tones" for the generated sounds, otherwise the pack's
    /// name. They were "Classic tones" until 17 Oct 2026, which read as the
    /// Classic soundscape's sounds once Classic became the old Synthwave.
    /// </summary>
    public static string DisplayName(string? key) => Find(key)?.Name ?? "Generated tones";

    /// <summary>The pack's file for this effect, or null if it isn't on disk.</summary>
    public static string? FileFor(string? packKey, SoundEffectKind kind)
    {
        var pack = Find(packKey);
        if (pack is null)
        {
            return null;
        }

        try
        {
            var path = Path.Combine(Folder, pack.Key, kind + ".wav");
            if (File.Exists(path))
            {
                return path;
            }

            // No pack has a warning sound of its own: it plays the pack's
            // invalid-lap buzzer, as it did before it was a sound of its own.
            return kind == SoundEffectKind.Warning ? FileFor(packKey, SoundEffectKind.InvalidLap) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Ambience layers a soundscape adds are keyed with this, so applying
    /// another soundscape can find and replace them without touching the
    /// club's own layers.
    /// </summary>
    public const string BedLayerKeyPrefix = "soundscape-bed-";

    /// <summary>The pack's looping venue bed (Assets\SoundPacks\&lt;Pack&gt;\Ambience.wav), or null if it isn't on disk.</summary>
    public static string? AmbienceFileFor(string? packKey) => PackFile(packKey, "Ambience.wav");

    /// <summary>
    /// The pack's crowd cheering (Assets\SoundPacks\&lt;Pack&gt;\CrowdRoar.wav),
    /// or null if it isn't on disk. The same crowd as the bed, only on its
    /// feet - it sits near-silent under the bed and is faded in on the
    /// moments that deserve it.
    /// </summary>
    public static string? CrowdRoarFileFor(string? packKey) => PackFile(packKey, "CrowdRoar.wav");

    private static string? PackFile(string? packKey, string fileName)
    {
        var pack = Find(packKey);
        if (pack is null)
        {
            return null;
        }

        try
        {
            var path = Path.Combine(Folder, pack.Key, fileName);
            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Added to a pack's bed key for its crowd-roar layer, so the two can be
    /// told apart - and both still start with <see cref="BedLayerKeyPrefix"/>,
    /// which is what lets applying another soundscape replace the pair.
    /// </summary>
    public const string RoarLayerKeySuffix = "-roar";

    public static string BedLayerKey(Pack pack) => BedLayerKeyPrefix + pack.Key;

    public static string RoarLayerKey(Pack pack) => BedLayerKeyPrefix + pack.Key + RoarLayerKeySuffix;

    /// <summary>
    /// How loud each pack's bed sits at each race intensity - idle,
    /// anticipation, racing, excited, peak.
    ///
    /// Low, and it barely moves. The bed plays all night with a crowd
    /// murmur in it, and it used to be the thing that swelled at a fast lap
    /// too - up to full level - which meant a bed loud enough to cheer was
    /// far too loud to sit under a quiet heat. The cheering is its own layer
    /// now (see RoarVolumes), so the bed only has to be a room.
    ///
    /// Arcade and Synthwave have crowds in their beds now as well, so they
    /// sit with the others rather than being the loud flat ones.
    /// </summary>
    public static (float Idle, float Anticipation, float Racing, float Excited, float Peak) BedVolumes(string? packKey) =>
        Find(packKey)?.Key switch
        {
            "Podium" => (0.20f, 0.24f, 0.26f, 0.30f, 0.34f),
            "PitLane" => (0.24f, 0.28f, 0.30f, 0.34f, 0.38f),
            _ => (0.22f, 0.26f, 0.28f, 0.32f, 0.36f),
        };

    /// <summary>
    /// How loud each pack's crowd roar sits at each race intensity.
    ///
    /// Silent between races and barely there while cars run, so the room
    /// never sounds like a stadium during a quiet heat - then most of the
    /// way up at an excited moment (a fast lap, a lead change, the last
    /// lap, a close battle) and full at a peak (the finish, a track
    /// record). The Ambience engine's own fades do the ramping.
    /// </summary>
    public static (float Idle, float Anticipation, float Racing, float Excited, float Peak) RoarVolumes(string? packKey) =>
        Find(packKey)?.Key switch
        {
            "Podium" => (0.0f, 0.06f, 0.10f, 0.50f, 0.90f),
            "PitLane" => (0.0f, 0.08f, 0.12f, 0.60f, 1.00f),
            "Arcade" or "Synthwave" => (0.0f, 0.06f, 0.10f, 0.50f, 0.85f),
            _ => (0.0f, 0.08f, 0.12f, 0.55f, 0.95f),
        };

    /// <summary>
    /// How loud the beds were before the crowd roar was split out (0.57 and
    /// earlier) - only so a bed added back then, and never tuned since, can
    /// be recognised and brought down to the new level. See
    /// AmbienceStore.Load.
    /// </summary>
    public static (float Idle, float Anticipation, float Racing, float Excited, float Peak) LegacyBedVolumes(string? packKey) =>
        Find(packKey)?.Key switch
        {
            "Arcade" => (0.35f, 0.40f, 0.45f, 0.55f, 0.70f),
            "Synthwave" => (0.40f, 0.45f, 0.50f, 0.60f, 0.75f),
            "Podium" => (0.25f, 0.35f, 0.40f, 0.65f, 0.95f),
            _ => (0.25f, 0.40f, 0.50f, 0.75f, 1.00f),
        };

    /// <summary>The name a soundscape's bed layer is given in Ambience.</summary>
    public static string BedLayerName(Pack pack) => $"{pack.Name} crowd (from the soundscape)";

    /// <summary>The name a soundscape's crowd-roar layer is given in Ambience.</summary>
    public static string RoarLayerName(Pack pack) => $"{pack.Name} crowd roar (from the soundscape)";

    /// <summary>
    /// The pack's bed as an Ambience layer, or null when its file isn't on
    /// disk.
    /// </summary>
    public static AmbienceLayer? BedLayer(string? packKey)
    {
        if (Find(packKey) is not { } pack || AmbienceFileFor(pack.Key) is not { } file)
        {
            return null;
        }

        var v = BedVolumes(pack.Key);
        return new AmbienceLayer
        {
            Key = BedLayerKey(pack),
            Name = BedLayerName(pack),
            FilePath = file,
            Enabled = true,
            IdleVolume = v.Idle,
            AnticipationVolume = v.Anticipation,
            RacingVolume = v.Racing,
            ExcitedVolume = v.Excited,
            PeakVolume = v.Peak,
        };
    }

    /// <summary>
    /// The pack's crowd roar as an Ambience layer, or null when its file
    /// isn't on disk.
    /// </summary>
    public static AmbienceLayer? RoarLayer(string? packKey)
    {
        if (Find(packKey) is not { } pack || CrowdRoarFileFor(pack.Key) is not { } file)
        {
            return null;
        }

        var v = RoarVolumes(pack.Key);
        return new AmbienceLayer
        {
            Key = RoarLayerKey(pack),
            Name = RoarLayerName(pack),
            FilePath = file,
            Enabled = true,
            IdleVolume = v.Idle,
            AnticipationVolume = v.Anticipation,
            RacingVolume = v.Racing,
            ExcitedVolume = v.Excited,
            PeakVolume = v.Peak,
        };
    }

    /// <summary>
    /// The music that ships with each pack, by track title - installed into
    /// the music library like the rest of the bundled soundtrack (see
    /// MusicStore). Synthwave - Classic's pack - also takes the synthwave
    /// tracks the app has always shipped with. Every pack has music of its
    /// own now; Pit Lane's four songs came on 16 Oct 2026.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> PackMusic =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            // Podium's own four songs (17 Oct 2026), in place of Victory
            // Lane - which still ships and stays in the library.
            ["Podium"] = new[] { "Full Throttle", "Top Step", "Burnout", "Champagne Spray" },
            ["Arcade"] = new[] { "Insert Coin" },
            // The club's own four songs since 16 Oct 2026, in place of
            // Race Day Live - which still ships and stays in the library.
            // Named since 17 Oct 2026 - they shipped as Broadcast Song 1 to 4
            // (see MusicStore.NamedSongs).
            ["Broadcast"] = new[] { "Prime Time", "Highlight Reel", "Under the Lights", "Post-Race Show" },
            // Pit Lane's own four songs (16 Oct 2026) - it had no music of its own before.
            ["PitLane"] = new[] { "Gravel and Gasoline", "Dirt Road Dash", "Pit Stop Two-Step", "Back Forty Straight" },
            ["Synthwave"] = new[]
            {
                "Neon Grid", "Horizon Line", "Chasing Sunset", "Grid Runner", "Afterglow", "Cooldown Lap",
                "Vanishing Point", "Last Lap Lights", "Redline Memory", "One More Run", "Back to the Grid", "Long Way Home",

                // Classic's own four songs (16 Oct 2026), added to the
                // soundtrack rather than in place of it.
                "Chrome Apex", "Midnight Pit Wall", "Starlight Straight", "Afterburn",

                // Victory Lane and Race Day Live (17 Oct 2026): Podium and
                // Broadcast swapped them for songs of their own, so nothing
                // played them any more. They still ship, and Classic keeps
                // them in the rotation.
                "Victory Lane", "Race Day Live",
            },
        };

    /// <summary>
    /// The file this effect should play, or null for the generated tone.
    /// The club's own file first, then the pack - see the class summary.
    /// </summary>
    public static string? ResolveFile(SoundEffectConfig? config, SoundEffectKind kind)
    {
        if (config is null)
        {
            return null;
        }

        var custom = config.CustomFilePath;
        if (!string.IsNullOrWhiteSpace(custom) && !PlainFileName.IsNetworkPath(custom))
        {
            try
            {
                if (File.Exists(custom))
                {
                    return custom;
                }
            }
            catch
            {
                // Unreadable path - fall through to the pack.
            }
        }

        return FileFor(config.BuiltInSound, kind);
    }

    /// <summary>
    /// How far the streak sound is raised for a streak this long.
    ///
    /// The generated streak tone always climbed with the streak, which made a
    /// long run sound like it was building. A recorded file can't be re-pitched
    /// note by note, so the whole file is lifted instead - half a semitone per
    /// lap past the threshold, capped at five so a very long run doesn't turn
    /// into a chipmunk.
    /// </summary>
    public static double StreakSemitones(int streakCount, int threshold) =>
        Math.Clamp((streakCount - Math.Max(2, threshold)) * 0.5, 0, 5);

    /// <summary>
    /// Plays an effect exactly as a race would pick it - own file, pack file,
    /// or generated tone - for the Test buttons and the Mixer's level check.
    /// </summary>
    public static Task PlayAsync(
        SoundEffectConfig? config,
        SoundEffectKind kind,
        float volume,
        int streakCount = 3,
        int streakThreshold = 3,
        double leadInSeconds = 0.9,
        CancellationToken cancellationToken = default)
    {
        var file = ResolveFile(config, kind);
        if (file is not null)
        {
            var semitones = kind == SoundEffectKind.Streak ? StreakSemitones(streakCount, streakThreshold) : 0;
            return ToneGenerator.PlayFileAsync(file, volume, cancellationToken, leadInSeconds, semitones);
        }

        return ToneGenerator.PlayGeneratedAsync(kind, volume, streakCount, cancellationToken);
    }
}
