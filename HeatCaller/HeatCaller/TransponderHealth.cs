using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// How well each transponder has been read, night by night - so one that is
/// fading is noticed while it still works, not when it drops out of a main.
///
/// The EasyLap reports a signal strength and a hit count with every pass
/// (EasyLapFramer). TransponderWatch already uses them, but only for the
/// last twelve minutes: it can say a transponder is weak NOW. A battery
/// going flat, a cracked case or a transponder knocked loose in its mount
/// shows first as a slow slide over a few nights - each night still "fine"
/// on its own - and only a record across nights can see that.
///
/// One small file (transponder-health.json): for each transponder, a line a
/// night of how many readings there were and their totals - nothing per pass.
/// A night runs from 6am to 6am, so a club night that goes past midnight is
/// one night. Kept to the last <see cref="NightsKept"/> nights each.
///
/// Per physical transponder, not per driver: it is the transponder that is
/// fading, and a driver's alternate is a different one. Only passes that
/// carried a reading count - an I-Lap gives none (SignalHits 0 = unknown),
/// so its transponders are never called fading for want of data.
///
/// Recorded on the decoder's own thread (MainForm.OnDecoderLapEvent calls it
/// from the UI thread today, but nothing here relies on that); saved at most
/// once a minute while something has changed (<see cref="SaveIfDue"/>, from
/// the race tick) and when the app closes.
/// </summary>
public sealed class TransponderHealthStore
{
    /// <summary>How many nights each transponder keeps.</summary>
    public const int NightsKept = 20;

    /// <summary>A night needs this many readings to say anything - a car put down for one lap says little.</summary>
    public const int LeastReadings = 5;

    /// <summary>How many earlier nights there must be before a transponder can be called fading.</summary>
    public const int LeastEarlierNights = 3;

    /// <summary>A night this much weaker than the transponder's usual is fading.</summary>
    public const double FadingDrop = 0.25;

    /// <summary>Or a night when this share of passes were barely caught, from a transponder that is usually read cleanly.</summary>
    public const double FadingWeakShare = 0.30;

    /// <summary>When the hour the night turns over is - 6am, so a night past midnight stays one night.</summary>
    private static readonly TimeSpan NightTurnsAt = TimeSpan.FromHours(6);

    private static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(1);

    /// <summary>One transponder's readings for one night.</summary>
    public sealed class Night
    {
        /// <summary>The night, as yyyy-MM-dd - the date it started on.</summary>
        public string Date { get; set; } = string.Empty;

        public int Readings { get; set; }
        public long TotalStrength { get; set; }
        public long TotalHits { get; set; }

        /// <summary>Passes with fewer hits than TransponderWatch.WeakHits - only just caught.</summary>
        public int WeakPasses { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public double AverageStrength => Readings == 0 ? 0 : TotalStrength / (double)Readings;
        [System.Text.Json.Serialization.JsonIgnore]
        public double WeakShare => Readings == 0 ? 0 : WeakPasses / (double)Readings;
    }

    public enum Verdict
    {
        /// <summary>Not enough nights, or not enough readings tonight, to say.</summary>
        NotEnoughData,
        Good,
        Fading,
    }

    /// <summary>What the record says about one transponder.</summary>
    /// <param name="Latest">Its most recent night with enough readings.</param>
    /// <param name="UsualStrength">The median of its earlier nights' average strength.</param>
    /// <param name="Drop">How much weaker the latest night is than usual, 0.32 for 32% - negative when stronger.</param>
    public sealed record Health(string Transponder, Verdict Verdict, Night? Latest, double UsualStrength, double Drop, int NightsOnRecord)
    {
        /// <summary>One short line for a person: "32% weaker than usual" and the like.</summary>
        public string Summary => Verdict switch
        {
            Verdict.Fading when Drop >= FadingDrop => $"reading {Drop:P0} weaker than usual",
            Verdict.Fading => $"barely caught on {Latest!.WeakShare:P0} of passes",
            Verdict.Good => "reading as usual",
            _ => "not enough nights yet",
        };
    }

    private readonly Dictionary<string, List<Night>> _nights;
    private readonly object _gate = new();
    private readonly string? _path;

    /// <summary>
    /// True when the file was there but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. A file locked by a backup or sync
    /// tool as HeatCaller started used to load empty, and the first save
    /// replaced up to twenty nights of history with tonight's.
    /// </summary>
    private readonly bool _holdWrites;

    private bool _dirty;
    private DateTime _lastSaveUtc = DateTime.MinValue;

    private TransponderHealthStore(Dictionary<string, List<Night>> nights, string? path, bool holdWrites = false)
    {
        _nights = nights;
        _path = path;
        _holdWrites = holdWrites;
    }

    private static string FilePath => Path.Combine(AppPaths.Root, "transponder-health.json");

    /// <summary>A store that is never written to disk - for tests, and anything that only wants the arithmetic.</summary>
    public static TransponderHealthStore InMemory() => new(new Dictionary<string, List<Night>>(StringComparer.OrdinalIgnoreCase), null);

    public static TransponderHealthStore Load()
    {
        var path = FilePath;
        var hold = false;

        try
        {
            if (File.Exists(path))
            {
                var json = SafeFile.ReadAllTextWithRetry(path);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, List<Night>>>(json);

                if (loaded is not null)
                {
                    return new TransponderHealthStore(
                        new Dictionary<string, List<Night>>(loaded, StringComparer.OrdinalIgnoreCase), path);
                }
            }
        }
        catch (Exception ex)
        {
            // A damaged record is set aside, not trusted: it is advice, and
            // starting it again costs a few nights of history, nothing more.
            // But one that could not be read at all (locked) or kept aside is
            // never saved over this session - see _holdWrites.
            hold = SafeFile.HoldWritesAfterUnreadable(path, ex);
        }

        return new TransponderHealthStore(new Dictionary<string, List<Night>>(StringComparer.OrdinalIgnoreCase), path, hold);
    }

    /// <summary>The night a moment belongs to, as yyyy-MM-dd: before 6am it is still last night.</summary>
    internal static string NightOf(DateTime local) => (local - NightTurnsAt).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Records a pass. Ghosts, and passes that carried no reading (an I-Lap's,
    /// SignalHits 0), are not evidence of anything here and are ignored.
    /// </summary>
    public void Record(DirectLapEvent lap) => Record(lap, DateTime.Now);

    internal void Record(DirectLapEvent lap, DateTime local)
    {
        if (lap.IsSynthetic || lap.SignalHits <= 0 || string.IsNullOrWhiteSpace(lap.TransponderId))
        {
            return;
        }

        var date = NightOf(local);

        lock (_gate)
        {
            if (!_nights.TryGetValue(lap.TransponderId, out var nights))
            {
                nights = new List<Night>();
                _nights[lap.TransponderId] = nights;
            }

            var night = nights.Count > 0 && nights[^1].Date == date ? nights[^1] : null;

            if (night is null)
            {
                night = new Night { Date = date };
                nights.Add(night);

                if (nights.Count > NightsKept)
                {
                    nights.RemoveRange(0, nights.Count - NightsKept);
                }
            }

            night.Readings++;
            night.TotalStrength += lap.SignalStrength;
            night.TotalHits += lap.SignalHits;

            if (lap.SignalHits < TransponderWatch.WeakHits)
            {
                night.WeakPasses++;
            }

            _dirty = true;
        }
    }

    /// <summary>
    /// Whether a transponder is reading as it usually does. Its latest night
    /// with enough readings is compared with the median of the nights before
    /// it (up to eight of them) - the median, so one odd night (a wet track,
    /// a different car) does not set the bar.
    /// </summary>
    public Health Assess(string transponder)
    {
        List<Night> nights;

        lock (_gate)
        {
            nights = _nights.TryGetValue(transponder, out var kept)
                ? kept.Where(n => n.Readings >= LeastReadings).Select(Copy).ToList()
                : new List<Night>();
        }

        return Assess(transponder, nights);
    }

    internal static Health Assess(string transponder, IReadOnlyList<Night> nights)
    {
        var usable = nights.Where(n => n.Readings >= LeastReadings).OrderBy(n => n.Date, StringComparer.Ordinal).ToList();

        if (usable.Count == 0)
        {
            return new Health(transponder, Verdict.NotEnoughData, null, 0, 0, 0);
        }

        var latest = usable[^1];
        var earlier = usable.Take(usable.Count - 1).TakeLast(8).ToList();

        if (earlier.Count < LeastEarlierNights)
        {
            return new Health(transponder, Verdict.NotEnoughData, latest, 0, 0, usable.Count);
        }

        var usual = Median(earlier.Select(n => n.AverageStrength));
        var usualWeak = Median(earlier.Select(n => n.WeakShare));
        var drop = usual <= 0 ? 0 : 1 - latest.AverageStrength / usual;

        var fading = drop >= FadingDrop
                     || (latest.WeakShare >= FadingWeakShare && usualWeak < FadingWeakShare / 2);

        return new Health(transponder, fading ? Verdict.Fading : Verdict.Good, latest, usual, drop, usable.Count);
    }

    /// <summary>
    /// Every transponder fading on its latest night, if that night is within
    /// <paramref name="within"/> of today - a transponder last raced in the
    /// spring is not news. Most fallen first.
    /// </summary>
    public IReadOnlyList<Health> Fading(TimeSpan within) => Fading(within, DateTime.Now);

    internal IReadOnlyList<Health> Fading(TimeSpan within, DateTime local)
    {
        List<string> ids;

        lock (_gate)
        {
            ids = _nights.Keys.ToList();
        }

        var since = NightOf(local - within);

        return ids
            .Select(id => Assess(id))
            .Where(h => h.Verdict == Verdict.Fading && string.CompareOrdinal(h.Latest!.Date, since) >= 0)
            .OrderByDescending(h => h.Drop)
            .ToList();
    }

    /// <summary>Saves when something has changed and the last save was a minute ago or more. Cheap to call every tick.</summary>
    public void SaveIfDue()
    {
        if (_dirty && DateTime.UtcNow - _lastSaveUtc >= SaveEvery)
        {
            Save();
        }
    }

    /// <summary>Saves now if anything has changed - when the app closes.</summary>
    public void Save()
    {
        // Not written: the real file is almost certainly fine, and this
        // store holds only tonight. The console has already said why.
        if (_holdWrites)
        {
            return;
        }

        string json;

        lock (_gate)
        {
            if (!_dirty)
            {
                return;
            }

            json = JsonSerializer.Serialize(_nights, new JsonSerializerOptions { WriteIndented = true });
            _dirty = false;
            _lastSaveUtc = DateTime.UtcNow;
        }

        // 17 Oct 2026, Bug Hunt 8, A02-4: a write that failed (a sync tool
        // holding the file, a full disk) marks the store dirty again, so
        // SaveIfDue tries again a minute later and the close-time Save does
        // too - the flag used to be cleared before the write and the result
        // ignored, losing those readings. Not after a restore froze the
        // store: that freeze is for good, and retrying it is pointless.
        if (_path is not null && !SafeFile.WriteAllText(_path, json) && !SafeFile.IsFrozen)
        {
            lock (_gate)
            {
                _dirty = true;
            }
        }
    }

    private static Night Copy(Night n) => new()
    {
        Date = n.Date,
        Readings = n.Readings,
        TotalStrength = n.TotalStrength,
        TotalHits = n.TotalHits,
        WeakPasses = n.WeakPasses,
    };

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();

        if (sorted.Count == 0)
        {
            return 0;
        }

        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}
