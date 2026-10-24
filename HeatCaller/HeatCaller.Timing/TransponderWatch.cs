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

        // WHY (23 Oct 2026, bug hunt 10): Weak is a ratio over the window, so a
        // remounted transponder kept reading weak on "Check my transponder".
        // The last pass is kept on its own so that check describes the pass
        // just made; Weak stays the verdict for readiness and Mission Control.
        /// <summary>Hits on the most recent pass; 0 = that pass reported no signal (unknown).</summary>
        public int LastHits { get; set; }

        /// <summary>True when the most recent pass carried a signal reading.</summary>
        public bool LastSignalKnown => LastHits > 0;

        /// <summary>True when the most recent pass was heard, but only just.</summary>
        public bool LastPassWeak => LastHits > 0 && LastHits < WeakHits;

        public int AverageStrength => SignalReadings == 0 ? 0 : (int)(TotalStrength / SignalReadings);

        /// <summary>True when most of what was heard was marginal.</summary>
        public bool Weak => Passes > 0 && WeakPasses * 2 >= Passes;

        public TimeSpan Ago => DateTime.UtcNow - LastSeenUtc;

        // WHY (23 Oct 2026, bug hunt 10): the counters above used to add up
        // every pass since the app started, so a car with a clean practice
        // and heats was still called fine when its battery faded on the
        // warm-up before the main (10 weak passes against 120 good ones),
        // and a car that read weakly in the morning stayed WEAK after a new
        // battery. The passes inside the Window are kept here, and Record
        // recomputes the counters from them alone, so the verdict is about
        // NOW. Only touched by Record, Recent and Of, under the watch's lock.
        internal readonly Queue<(DateTime At, int Hits, int Strength)> RecentReadings = new();

        /// <summary>Drops passes older than the window and recomputes the counters from what is left.</summary>
        internal void Recount(DateTime nowUtc)
        {
            var cutoff = nowUtc - Window;

            while (RecentReadings.Count > 0 && RecentReadings.Peek().At < cutoff)
            {
                RecentReadings.Dequeue();
            }

            var passes = 0;
            var readings = 0;
            var weak = 0;
            var best = 0;
            long total = 0;

            foreach (var reading in RecentReadings)
            {
                passes++;

                // SignalHits 0 means the decoder gave no signal information for
                // this pass (unknown), not a pass with no signal - it must not
                // count as weak or drag the average strength down.
                if (reading.Hits > 0)
                {
                    readings++;
                    total += reading.Strength;
                    best = Math.Max(best, reading.Hits);

                    if (reading.Hits < WeakHits)
                    {
                        weak++;
                    }
                }
            }

            Passes = passes;
            SignalReadings = readings;
            WeakPasses = weak;
            BestHits = best;
            TotalStrength = total;
        }
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
    public void Record(DirectLapEvent lap) => Record(lap, DateTime.UtcNow);

    /// <summary>Records a pass heard at <paramref name="nowUtc"/> - the clock is a parameter so tests can replay a night.</summary>
    internal void Record(DirectLapEvent lap, DateTime nowUtc)
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

            sighting.LastSeenUtc = nowUtc;

            // WHY (23 Oct 2026, bug hunt 10): the Check-my-transponder answer must describe the pass just made, not the window's running ratio.
            sighting.LastHits = Math.Max(0, lap.SignalHits);

            // Every pass is queued, SignalHits 0 included, so Passes stays a
            // true count; Recount decides what each one says about signal.
            sighting.RecentReadings.Enqueue((nowUtc, lap.SignalHits, lap.SignalStrength));
            sighting.Recount(nowUtc);
        }
    }

    /// <summary>Everything heard inside the window, most recent first.</summary>
    public IReadOnlyList<Sighting> Recent()
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            var cutoff = now - Window;

            var recent = _seen.Values
                .Where(s => s.LastSeenUtc >= cutoff)
                .OrderByDescending(s => s.LastSeenUtc)
                .ToList();

            // WHY (23 Oct 2026, bug hunt 10 review): the counters are only
            // recounted when a pass comes in, so a car that went quiet kept
            // passes older than the window in its verdict. Recounted on read
            // too, so what is shown is the last twelve minutes as of now.
            foreach (var sighting in recent)
            {
                sighting.Recount(now);
            }

            return recent;
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

            var now = DateTime.UtcNow;

            if (sighting.LastSeenUtc < now - Window)
            {
                return null;
            }

            // Recounted on read as well as on a pass - see Recent.
            sighting.Recount(now);
            return sighting;
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
