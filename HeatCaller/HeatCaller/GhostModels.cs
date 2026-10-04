namespace HeatCaller;

/// <summary>Where a ghost's pace comes from.</summary>
public enum GhostPaceSource
{
    /// <summary>A lap time you type in. The simplest, and the one to reach for when chasing a target.</summary>
    FixedLapTime,

    /// <summary>Your own personal best on this track - racing the best version of yourself.</summary>
    YourPersonalBest,

    /// <summary>A named driver's personal best, so you can chase someone who isn't there.</summary>
    DriverPersonalBest,

    /// <summary>Your average pace from your last session, which is a fairer fight than your best ever.</summary>
    YourRecentAverage,

    // Everything below is APPENDED. These persist as integers, so inserting
    // anywhere above would silently reassign the saved ghosts' sources.

    /// <summary>The all-time record for the selected track layout, whoever set it - the house benchmark.</summary>
    TrackRecord,

    /// <summary>
    /// The driver's own pace in the session that is running, re-read every
    /// lap: their best lap so far, or their rolling recent average.
    ///
    /// Unlike every source above, this one MOVES. Improve, and the ghost
    /// improves with you, so it stays the same small distance ahead instead
    /// of being beaten once and left behind.
    /// </summary>
    LiveOwnPace,

    /// <summary>Whatever the current race leader is running, re-read every lap.</summary>
    LiveLeaderPace,

    /// <summary>The driver's best lap within a chosen window of dates - racing a past version of yourself.</summary>
    PersonalBestInRange,
}

/// <summary>Which live number a LiveOwnPace ghost follows.</summary>
public enum GhostLiveBasis
{
    /// <summary>Their quickest lap so far this session - the ghost you have to beat at your best.</summary>
    BestLapSoFar,

    /// <summary>The mean of their recent laps - a fairer, less punishing target.</summary>
    RecentAverage,
}

/// <summary>
/// How steady a ghost is, lap to lap.
///
/// A metronome is useful for a target but makes a poor opponent - real
/// racing has a rhythm of good laps and scrappy ones, and chasing something
/// that never varies teaches you less than chasing something that does.
/// </summary>
public enum GhostConsistency
{
    /// <summary>Identical every lap. Best as a pace target rather than a rival.</summary>
    Metronome,

    /// <summary>Small variation - a quick, experienced driver on a good run.</summary>
    Steady,

    /// <summary>Noticeable variation, the odd mistake. The most race-like.</summary>
    Human,

    /// <summary>Wild swings - fast laps and bad ones. Good for practising recovery.</summary>
    Erratic,
}

/// <summary>One virtual opponent.</summary>
public sealed class GhostCar
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Transponder ID used for this ghost's lap events.
    ///
    /// Prefixed so it can never collide with real hardware, and so anything
    /// reading standings can tell a ghost from a car on track.
    /// </summary>
    public string Transponder { get; set; } = $"{Ghosts.Prefix}{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

    public string Name { get; set; } = "Ghost";
    public bool Enabled { get; set; } = true;

    public GhostPaceSource PaceSource { get; set; } = GhostPaceSource.FixedLapTime;
    public GhostConsistency Consistency { get; set; } = GhostConsistency.Human;

    /// <summary>Lap time in milliseconds, for FixedLapTime.</summary>
    public int FixedLapMs { get; set; } = 7000;

    /// <summary>Whose best to chase, for DriverPersonalBest.</summary>
    public string SourceTransponder { get; set; } = string.Empty;

    /// <summary>
    /// Pace adjustment as a percentage. 100 = exactly the source pace,
    /// 102 = two percent slower, 98 = two percent quicker.
    ///
    /// The point of the feature: set a ghost slightly quicker than your
    /// best and you have something to chase rather than something to
    /// comfortably beat.
    /// </summary>
    public int PacePercent { get; set; } = 100;

    /// <summary>A car name, so the announcer has something to say about it.</summary>
    public string CarModel { get; set; } = string.Empty;

    /// <summary>For PersonalBestInRange: the earliest session date to consider. Null means no lower bound.</summary>
    public DateTime? RangeFromUtc { get; set; }

    /// <summary>For PersonalBestInRange: the latest session date to consider. Null means up to now.</summary>
    public DateTime? RangeToUtc { get; set; }

    /// <summary>For LiveOwnPace: which live number to follow.</summary>
    public GhostLiveBasis LiveBasis { get; set; } = GhostLiveBasis.BestLapSoFar;

    /// <summary>
    /// How many recent laps a LiveOwnPace ghost averages over, when it is
    /// following an average rather than a best lap.
    /// </summary>
    public int LiveAverageLaps { get; set; } = 5;

    /// <summary>Whether this ghost's pace changes during the session rather than being fixed at the green.</summary>
    public bool IsLivePace => PaceSource is GhostPaceSource.LiveOwnPace or GhostPaceSource.LiveLeaderPace;

    /// <summary>The variation band for this ghost's consistency setting.</summary>
    public double VariationFraction => Consistency switch
    {
        GhostConsistency.Metronome => 0.000,
        GhostConsistency.Steady => 0.012,
        GhostConsistency.Human => 0.035,
        GhostConsistency.Erratic => 0.090,
        _ => 0.035,
    };
}

/// <summary>Ghost racing settings.</summary>
public sealed class GhostSettings
{
    /// <summary>
    /// Off by default - ghosts appear in the standings as if they were real
    /// cars, which would be startling if it happened unasked.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Announce ghosts the same way as real drivers.
    ///
    /// On by default: an opponent you can't hear being called isn't much of
    /// an opponent, and being told you've been passed is the whole point.
    /// </summary>
    public bool AnnounceGhosts { get; set; } = true;

    /// <summary>
    /// Start ghosts only when a race actually starts, rather than the
    /// moment they're switched on.
    /// </summary>
    public bool StartWithRace { get; set; } = true;

    public List<GhostCar> Ghosts { get; set; } = new();
}


/// <summary>Loads and saves the ghost configuration.</summary>
public sealed class GhostStore
{
    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "ghosts.json");

    public GhostSettings Current { get; private set; } = new();

    public static GhostStore Load()
    {
        var store = new GhostStore();

        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = System.Text.Json.JsonSerializer.Deserialize<GhostSettings>(SafeFile.ReadAllTextWithRetry(FilePath));
                if (loaded is not null)
                {
                    store.Current = loaded;
                }
            }
        }
        catch (Exception ex)
        {
            // Unreadable file: start fresh rather than refusing to open,
            // keeping a copy first so the next save doesn't lose it.
            SafeFile.PreserveUnreadable(FilePath, ex);
        }

        return store;
    }

    /// <summary>
    /// Raised after saving, so driver profiles can be kept in step.
    ///
    /// Ghosts need a DriverProfile to be selectable in rosters, demo mode
    /// and everywhere else drivers are picked. Creating those only when a
    /// race started was a chicken and egg: you couldn't enter a ghost into
    /// a session until you'd already raced it.
    /// </summary>
    public event Action<GhostSettings>? Saved;

    public void Save()
    {
        SafeFile.WriteAllText(FilePath, System.Text.Json.JsonSerializer.Serialize(
            Current, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        Saved?.Invoke(Current);
    }

    public string Snapshot() => System.Text.Json.JsonSerializer.Serialize(Current);

    public void Restore(string snapshot)
    {
        try
        {
            var restored = System.Text.Json.JsonSerializer.Deserialize<GhostSettings>(snapshot);
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
