using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// A per-driver lap cue.
///
/// The point is that a driver hears THEIR lap without looking up. On a small
/// track with several cars circulating, a single shared chime tells you a lap
/// happened but not whose - which is no use to someone driving.
/// </summary>
public sealed class DriverLapBeep
{
    public required string Transponder { get; set; }

    /// <summary>Which of the built-in cues this driver gets. Index into <see cref="LapBeepStore.ToneNames"/>.</summary>
    public int ToneIndex { get; set; }

    public bool Enabled { get; set; } = true;

    public override string ToString() => Transponder;
}

/// <summary>
/// Per-driver lap beep settings.
///
/// Kept in its own store rather than on DriverProfile: this is a preference
/// about how the app behaves during a race, not a fact about the driver, and
/// most drivers will never set one.
/// </summary>
public sealed class LapBeepStore
{
    private readonly List<DriverLapBeep> _beeps;
    private bool _enabled;

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "lap-beeps.json");

    /// <summary>
    /// The available cues, in the order they're offered.
    ///
    /// Chosen to be told apart by ear rather than to sound pleasant - that's
    /// the whole job. They differ in pitch AND shape (single, double, rising,
    /// falling), because pitch alone is hard to distinguish over track noise.
    /// </summary>
    public static readonly string[] ToneNames =
    {
        "High blip",
        "Low blip",
        "Double high",
        "Double low",
        "Rising",
        "Falling",
        "Triple tick",
        "Warble",
    };

    private LapBeepStore(List<DriverLapBeep> beeps, bool enabled)
    {
        _beeps = beeps;
        _enabled = enabled;
    }

    private sealed class BeepFile
    {
        public List<DriverLapBeep> Beeps { get; set; } = new();

        /// <summary>Master switch. Off by default - a beep per lap per driver is a lot of noise if you didn't ask for it.</summary>
        public bool Enabled { get; set; }
    }

    public static LapBeepStore Load()
    {
        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var file = JsonSerializer.Deserialize<BeepFile>(SafeFile.ReadAllTextWithRetry(FilePath));
                if (file is not null)
                {
                    // "Beeps": null, or a null entry, from a hand-edited or
                    // restored file. Every lap looks a driver up in this list
                    // (ToneFor), and a null threw there - mid-race, on every
                    // crossing.
                    var beeps = (file.Beeps ?? new List<DriverLapBeep>())
                        .Where(b => b is not null && !string.IsNullOrEmpty(b.Transponder))
                        .ToList();

                    return new LapBeepStore(beeps, file.Enabled);
                }
            }
        }
        catch (Exception ex)
        {
            // Unreadable file: start empty rather than refusing to open. But,
            // when it could not be read at all or no copy of it could be
            // kept, never save over it this session (see LoadedUnreadable).
            unreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
        }

        return new LapBeepStore(new List<DriverLapBeep>(), false) { LoadedUnreadable = unreadable };
    }

    /// <summary>
    /// True when the file was there but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. The file holds every driver's
    /// chosen cue, and a file locked for a moment at launch used to load
    /// empty and then be replaced whole by the first cue anyone set.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    public void Save()
    {
        // Not written: the real file is almost certainly fine, and saving
        // this near-empty store would replace it. The console has already
        // said why.
        if (LoadedUnreadable)
        {
            return;
        }

        SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(
            new BeepFile { Beeps = _beeps, Enabled = _enabled },
            new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Master switch for per-driver beeps.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            Save();
        }
    }

    public IReadOnlyList<DriverLapBeep> All => _beeps.ToList();

    public DriverLapBeep? Get(string transponder) =>
        _beeps.FirstOrDefault(b => string.Equals(b.Transponder, transponder, StringComparison.OrdinalIgnoreCase));

    public void Upsert(DriverLapBeep beep)
    {
        var index = _beeps.FindIndex(b =>
            string.Equals(b.Transponder, beep.Transponder, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            _beeps[index] = beep;
        }
        else
        {
            _beeps.Add(beep);
        }

        Save();
    }

    public void Delete(string transponder)
    {
        _beeps.RemoveAll(b => string.Equals(b.Transponder, transponder, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    /// <summary>The tone index for a driver, or null when they shouldn't get a beep.</summary>
    public int? ToneFor(string transponder)
    {
        if (!_enabled)
        {
            return null;
        }

        var beep = Get(transponder);
        return beep is { Enabled: true } ? Math.Clamp(beep.ToneIndex, 0, ToneNames.Length - 1) : null;
    }

    /// <summary>
    /// Assigns a different tone to each driver in a list, cycling if there
    /// are more drivers than tones. Saves setting eight of them by hand,
    /// which is the difference between the feature being used and not.
    /// </summary>
    public void AutoAssign(IEnumerable<string> transponders)
    {
        var index = 0;

        foreach (var transponder in transponders)
        {
            var existing = Get(transponder) ?? new DriverLapBeep { Transponder = transponder };
            existing.ToneIndex = index % ToneNames.Length;
            existing.Enabled = true;
            Upsert(existing);
            index++;
        }
    }

    /// <summary>A separate copy of a driver's cue, for an undo to hold - the stored one is changed in place.</summary>
    public static DriverLapBeep Copy(DriverLapBeep beep) =>
        new() { Transponder = beep.Transponder, ToneIndex = beep.ToneIndex, Enabled = beep.Enabled };

    /// <summary>
    /// Puts drivers' cues back as they were, for Undo: a copy for a driver
    /// who had one, null for a driver who had none (their cue is removed).
    /// Only the drivers named are touched, so undoing one change never
    /// reverts cues set on other drivers since. Saves.
    /// </summary>
    public void PutBack(IReadOnlyDictionary<string, DriverLapBeep?> before)
    {
        foreach (var (transponder, beep) in before)
        {
            _beeps.RemoveAll(b => string.Equals(b.Transponder, transponder, StringComparison.OrdinalIgnoreCase));

            if (beep is not null)
            {
                _beeps.Add(Copy(beep));
            }
        }

        Save();
    }

    /// <summary>
    /// Raised after an undo has put cues back, so the Lap Beeps page re-reads
    /// the store. Not raised by ordinary saves.
    /// </summary>
    public event Action? Restored;

    /// <summary>Tells whatever shows this store that an undo just changed it. UI thread.</summary>
    public void NotifyRestored() => Restored?.Invoke();

    public string Snapshot() =>
        JsonSerializer.Serialize(new BeepFile { Beeps = _beeps, Enabled = _enabled });

    public void Restore(string snapshot)
    {
        try
        {
            var file = JsonSerializer.Deserialize<BeepFile>(snapshot);
            if (file is null)
            {
                return;
            }

            _beeps.Clear();
            _beeps.AddRange(file.Beeps);
            _enabled = file.Enabled;
            Save();
        }
        catch
        {
            // A failed restore leaves the current state alone.
        }
    }
}
