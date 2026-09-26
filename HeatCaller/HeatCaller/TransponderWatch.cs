namespace HeatCaller;

/// <summary>
/// Remembers which transponders the decoder has heard lately, and how well.
///
/// The decoder reports a pass and the app counts a lap; nothing has ever
/// kept the passes that did not belong to a race. That is a shame, because
/// those passes are the only evidence there is that a car's transponder is
/// alive - and the moment it matters is the five minutes BEFORE a race,
/// when there is still time to change a battery.
///
/// Without this, a flat transponder is discovered the way it has always
/// been discovered: the race runs, and afterwards somebody is missing from
/// the results and wants their heat re-run. This is the same information,
/// half an hour earlier, when it is still worth something.
///
/// Held in memory only. It is about what is on the track right now, and a
/// reading from last Tuesday would be worse than none.
/// </summary>
public sealed class TransponderWatch
{
    /// <summary>
    /// How long a transponder counts as "seen".
    ///
    /// Long enough to cover a driver who put their car down, did a couple
    /// of laps and picked it up again while the heat before theirs
    /// finished; short enough that it is still a statement about this
    /// race rather than about the morning.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(12);

    /// <summary>
    /// Below this, a transponder is reporting but not reliably.
    ///
    /// The decoder counts how many times it saw the transponder during a
    /// single pass. One or two hits is a pass it only just caught, and a
    /// car that only just gets caught at walking pace in the pit lane is a
    /// car that will be missed at racing speed.
    /// </summary>
    public const int WeakHits = 3;

    public sealed class Sighting
    {
        public required string TransponderId { get; init; }
        public DateTime LastSeenUtc { get; set; }
        public int Passes { get; set; }
        public int WeakPasses { get; set; }
        public int BestHits { get; set; }
        public long TotalStrength { get; set; }

        /// <summary>
        /// Passes that carried a signal reading. A decoder that reports no
        /// hit count (SignalHits 0 = unknown, e.g. I-Lap) contributes passes
        /// but no readings, so its cars are neither called weak nor averaged
        /// down to a meaningless "signal 0".
        /// </summary>
        public int SignalReadings { get; set; }

        /// <summary>False when nothing heard so far reported signal quality - show it as unknown, not as 0.</summary>
        public bool SignalKnown => SignalReadings > 0;

        public int AverageStrength => SignalReadings == 0 ? 0 : (int)(TotalStrength / SignalReadings);

        /// <summary>True when most of what was heard was marginal.</summary>
        public bool Weak => Passes > 0 && WeakPasses * 2 >= Passes;

        public TimeSpan Ago => DateTime.UtcNow - LastSeenUtc;
    }

    private readonly Dictionary<string, Sighting> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    /// <summary>
    /// Given a transponder id, the main transponder of the driver it is the
    /// ALTERNATE transponder of - or null when it is nobody's alternate.
    /// Wired to ProfileStore.PrimaryForAlternate.
    ///
    /// The pre-race check asks about the roster, and the roster lists
    /// drivers by their main transponder. A driver out in their second car
    /// used to show as "not heard at all" AND as a stranger on track - two
    /// warnings about one car that is fine. Filing the pass under the
    /// driver's main id answers the question actually being asked: is this
    /// driver's car being heard. Left null, every pass is filed under its
    /// own id as before.
    /// </summary>
    public Func<string, string?>? AlternateTransponderOwner { get; set; }

    /// <summary>Records a pass. Ghost cars are not evidence of anything and are ignored.</summary>
    public void Record(DirectLapEvent lap)
    {
        if (lap.IsSynthetic || string.IsNullOrWhiteSpace(lap.TransponderId))
        {
            return;
        }

        // Looked up outside the lock - it reads the profile store, and has
        // no business holding this one while it does.
        var owner = AlternateTransponderOwner?.Invoke(lap.TransponderId);
        var key = string.IsNullOrWhiteSpace(owner) ? lap.TransponderId : owner;

        lock (_gate)
        {
            if (!_seen.TryGetValue(key, out var sighting))
            {
                sighting = new Sighting { TransponderId = key };
                _seen[key] = sighting;
            }

            sighting.LastSeenUtc = DateTime.UtcNow;
            sighting.Passes++;

            // SignalHits 0 means the decoder gave no signal information for
            // this pass (unknown), not a pass with no signal - it must not
            // count as weak or drag the average strength down.
            if (lap.SignalHits > 0)
            {
                sighting.SignalReadings++;
                sighting.TotalStrength += lap.SignalStrength;
                sighting.BestHits = Math.Max(sighting.BestHits, lap.SignalHits);

                if (lap.SignalHits < WeakHits)
                {
                    sighting.WeakPasses++;
                }
            }
        }
    }

    /// <summary>Everything heard inside the window, most recent first.</summary>
    public IReadOnlyList<Sighting> Recent()
    {
        lock (_gate)
        {
            var cutoff = DateTime.UtcNow - Window;

            return _seen.Values
                .Where(s => s.LastSeenUtc >= cutoff)
                .OrderByDescending(s => s.LastSeenUtc)
                .ToList();
        }
    }

    /// <summary>What is known about one transponder, or null if it has not been heard lately.</summary>
    public Sighting? Of(string transponderId)
    {
        lock (_gate)
        {
            if (!_seen.TryGetValue(transponderId, out var sighting))
            {
                return null;
            }

            return sighting.LastSeenUtc >= DateTime.UtcNow - Window ? sighting : null;
        }
    }

    /// <summary>Forgets everything - for a new event, or a new day.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _seen.Clear();
        }
    }
}
