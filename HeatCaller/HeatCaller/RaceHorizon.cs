namespace HeatCaller;

/// <summary>
/// What a gap is DOING, which is the half a fight's existence does not
/// tell you.
/// </summary>
public enum HorizonKind
{
    /// <summary>Steady, or not a fight at all. BattleWatch's story, not this one's.</summary>
    None = 0,

    /// <summary>The gap is coming down, but the laps left would not cover it at this rate.</summary>
    Closing = 1,

    /// <summary>The gap is going out.</summary>
    SlippingAway = 2,

    /// <summary>The gap is coming down, and the laps left at this rate would cover it.</summary>
    WithinReach = 3,
}

/// <summary>
/// A gap, what it is doing, and how much road is left - the arithmetic of
/// something still to be decided.
/// </summary>
/// <param name="AheadId">Who is in front right now.</param>
/// <param name="BehindId">Who is behind right now.</param>
/// <param name="Position">The position being fought over. 1 is the lead.</param>
/// <param name="GapSeconds">The gap at the last sample.</param>
/// <param name="PerLap">
/// How much of it is coming down each lap. Positive is closing, negative
/// is slipping away - the sign is the kind, and it is worked out over
/// whole laps rather than ticks so that a figure which gets SAID is a
/// figure a listener can check against the next lap.
/// </param>
/// <param name="LapsLeft">How much road is left.</param>
/// <param name="Buys">
/// What this rate over those laps would recover. Stated rather than
/// compared to the gap in words, so that the sentence can give the room
/// the subtraction and let the room do it - see <see cref="RaceHorizon"/>
/// on why it must never do the subtraction out loud as a forecast.
/// </param>
/// <param name="Kind">What the gap is doing.</param>
/// <param name="Samples">How many samples the rate was taken over.</param>
/// <param name="Estimated">
/// The laps left were WORKED OUT from the clock rather than known - a
/// timed race. See <see cref="RaceHorizon.LapsLeftIn"/>, and Say, which
/// softens the wording to "about four laps" so the sentence still claims
/// nothing it cannot stand behind.
/// </param>
public sealed record Horizon(
    string AheadId,
    string BehindId,
    int Position,
    double GapSeconds,
    double PerLap,
    int LapsLeft,
    double Buys,
    HorizonKind Kind,
    int Samples,
    bool Estimated = false);

/// <summary>
/// How much road is left in a race, and whether that figure is known or
/// worked out - see <see cref="RaceHorizon.LapsLeftIn"/>.
/// </summary>
/// <param name="Laps">Laps left for the leader, counting the one they are on.</param>
/// <param name="Estimated">True when it came off the clock rather than a lap limit.</param>
public readonly record struct RoadLeft(int Laps, bool Estimated);

/// <summary>
/// Watches a gap's RATE and what is left of the race, so the announcer can
/// point at something before it happens instead of only reporting it after.
///
/// WHY THIS IS NOT BattleWatch (14 Oct 2026)
///
/// BattleWatch answers "are these two having a fight" - a gap under a
/// threshold, held long enough to be a fight rather than a moment. That is
/// a fact about the PAST and it is the right fact: it is what makes a
/// segment sound live.
///
/// What it cannot answer is the question a room actually has while it
/// watches two cars, which is whether the one behind is going to get
/// there. Nothing in the app knew the gap's direction, its rate, or how
/// many laps were left to spend - so every live line the announcer had was
/// in the past tense. "They have been within four tenths for six laps" is
/// good. "Four tenths, coming down two tenths a lap, three to go" is a
/// broadcast.
///
/// It takes the same tick feed, PairGap, on purpose. Two detectors reading
/// the same thing two ways is how they end up disagreeing about which car
/// is in front.
///
/// THE RULE IT SHARES WITH NightPromises AND RecordWatch
///
/// IT NEVER PREDICTS. There is no field on <see cref="Horizon"/> for who
/// is going to win and no method that returns one.
/// <see cref="HorizonKind.WithinReach"/> is the strongest thing it will
/// say and it means exactly what it is defined as - this rate, over these
/// laps, adds up to this gap - which is a sum, not a claim. If the next
/// lap is a spin, every number in the record was still true when it was
/// said.
///
/// That distinction is the whole reason a forward-looking line is allowed
/// at all. Read NightPromises' opening: the SHAPE of anticipation is
/// invented, the content is arithmetic. <see cref="Say"/> carries it into
/// the wording, which is where it would otherwise be lost - "he'll catch
/// him" is one careless template away from a club's announcer being wrong
/// out loud in front of the people it was wrong about.
///
/// THE RATE IS PER LAP AND NOT PER TICK, and that is not a detail. The
/// ticks are a half-second UI timer; a rate per tick is a number nobody
/// can check and that changes if the timer does. Per lap is the unit the
/// room is already counting in, so a listener hears "two tenths a lap",
/// watches the next lap, and finds it was true.
///
/// Pure: ids, numbers and an injectable clock. No stores, no UI, no race.
/// </summary>
public sealed class RaceHorizon
{
    private sealed class Trail
    {
        public required string AheadId { get; set; }

        public required string BehindId { get; set; }

        public int Position { get; set; }

        /// <summary>The samples kept, oldest first, each with the lap it was taken on.</summary>
        public List<(int Lap, double Gap)> Seen { get; } = new();
    }

    private readonly Dictionary<string, Trail> _trails = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _saidAt = new(StringComparer.Ordinal);
    private readonly Func<DateTime> _clock;

    private int _lapsLeft;

    private bool _estimated;

    public RaceHorizon(Func<DateTime>? clock = null) => _clock = clock ?? (() => DateTime.Now);

    /// <summary>
    /// How many samples of a pair's gap are kept. Four, so the rate is
    /// taken over a few laps rather than off the last two crossings - a
    /// rate off two samples is one lap's noise reported as a trend.
    /// </summary>
    public int Window { get; set; } = 4;

    /// <summary>
    /// The smallest per-lap change that counts as a trend rather than
    /// noise, in seconds.
    ///
    /// Two hundredths a lap. Below that the gap is steady and the thing to
    /// say about it is that these two are locked together, which is
    /// BattleWatch's line and not this one's.
    /// </summary>
    public double MeaningfulRate { get; set; } = 0.02;

    /// <summary>
    /// The widest gap worth a horizon, in seconds.
    ///
    /// Two seconds. Beyond that a gap coming down is a car making its way
    /// forward over a whole race rather than something about to be
    /// decided, and "about to be decided" is the only thing this class is
    /// for.
    /// </summary>
    public double WatchGap { get; set; } = 2.0;

    /// <summary>The fewest laps that can still be counted as road left.</summary>
    public int LeastLapsLeft { get; set; } = 1;

    /// <summary>How long after a pair has been talked about before they can be again.</summary>
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How many pairs are being followed. For the console and the tests.</summary>
    public int Watching => _trails.Count;

    /// <summary>
    /// A tick of the race. Only pairs on the same lap are followed - the
    /// same rule BattleWatch goes by, because a car a lap down is not in a
    /// fight with the car in front of it however small the gap at the line.
    /// </summary>
    /// <param name="pairs">Adjacent pairs as this tick sees them.</param>
    /// <param name="lapsRun">
    /// The leader's lap count. The clock the rate is measured against - see
    /// the per-lap note on the class.
    /// </param>
    /// <param name="lapsLeft">
    /// How many laps are left to run, as the race knows it. Nought or
    /// fewer means nothing is left to be decided and nothing is offered.
    /// </param>
    /// <param name="estimated">
    /// The laps left came off the clock - a timed race - rather than a lap
    /// limit. Carried onto every Horizon so the wording can say "about".
    /// See <see cref="LapsLeftIn"/>.
    /// </param>
    public void Observe(IReadOnlyList<PairGap> pairs, int lapsRun, int lapsLeft, bool estimated = false)
    {
        _lapsLeft = lapsLeft;
        _estimated = estimated;

        if (pairs is null)
        {
            return;
        }

        var seenThisTick = new HashSet<string>(StringComparer.Ordinal);

        foreach (var pair in pairs)
        {
            if (pair.LapsDown != 0
                || string.IsNullOrEmpty(pair.AheadId)
                || string.IsNullOrEmpty(pair.BehindId)
                || double.IsNaN(pair.GapSeconds)
                || pair.GapSeconds < 0)
            {
                continue;
            }

            var key = Key(pair.AheadId, pair.BehindId);

            seenThisTick.Add(key);

            if (!_trails.TryGetValue(key, out var trail))
            {
                trail = new Trail { AheadId = pair.AheadId, BehindId = pair.BehindId };
                _trails[key] = trail;
            }

            // KEPT IN THE ORDER THE TICK SEES THEM, so a pair that swaps
            // places is still the same trail. The key is order-independent
            // for that reason - two cars trading a position four times are
            // one story, and a key built from "who is ahead" would start a
            // new trail on every swap and never build a rate at all.
            trail.AheadId = pair.AheadId;
            trail.BehindId = pair.BehindId;
            trail.Position = pair.Position;

            // ONE SAMPLE PER LAP. The ticks are a half-second timer, so
            // without this the window would hold two seconds of history
            // and the rate would be taken over no laps at all.
            if (trail.Seen.Count > 0 && trail.Seen[^1].Lap == lapsRun)
            {
                trail.Seen[^1] = (lapsRun, pair.GapSeconds);
            }
            else
            {
                trail.Seen.Add((lapsRun, pair.GapSeconds));
            }

            while (trail.Seen.Count > Math.Max(2, Window))
            {
                trail.Seen.RemoveAt(0);
            }
        }

        // A pair that is no longer adjacent is no longer a pair. Dropped
        // rather than aged out, because the gap between two cars that now
        // have somebody between them is not a gap either of them is racing.
        foreach (var gone in _trails.Keys.Where(k => !seenThisTick.Contains(k)).ToList())
        {
            _trails.Remove(gone);
        }
    }

    /// <summary>
    /// The one horizon worth a word, or null - which is most ticks.
    ///
    /// THE LEAD FIRST, then the strongest kind, then the smallest gap.
    /// Position is the first sort for the reason RaceStature exists at all:
    /// a room that hears the announcer spend its one forward-looking line
    /// on the fight for seventh has been told that the fight for the lead
    /// is worth the same, which is not true and is how a broadcast stops
    /// meaning anything.
    /// </summary>
    public Horizon? Pick()
    {
        if (_lapsLeft < Math.Max(1, LeastLapsLeft))
        {
            return null;
        }

        Horizon? best = null;

        foreach (var (key, trail) in _trails)
        {
            var horizon = Read(trail);

            if (horizon is null || horizon.Kind == HorizonKind.None)
            {
                continue;
            }

            if (_saidAt.TryGetValue(key, out var said) && _clock() - said < Cooldown)
            {
                continue;
            }

            if (best is null || Better(horizon, best))
            {
                best = horizon;
            }
        }

        return best;
    }

    private static bool Better(Horizon one, Horizon than)
    {
        // The lead, then a kind that is more worth saying, then the
        // tighter gap. Position 1 beats everything; after that a pair
        // within reach beats one merely closing.
        var leadOne = one.Position == 1;
        var leadThan = than.Position == 1;

        if (leadOne != leadThan)
        {
            return leadOne;
        }

        if (one.Kind != than.Kind)
        {
            return one.Kind > than.Kind;
        }

        return one.GapSeconds < than.GapSeconds;
    }

    private Horizon? Read(Trail trail)
    {
        if (trail.Seen.Count < 2)
        {
            return null;
        }

        var oldest = trail.Seen[0];
        var newest = trail.Seen[^1];
        var laps = newest.Lap - oldest.Lap;

        // A rate needs laps to be a rate - see the per-lap note on the
        // class. Several samples taken on the same lap are one sample.
        if (laps <= 0)
        {
            return null;
        }

        var gap = newest.Gap;

        if (gap > WatchGap)
        {
            return null;
        }

        var perLap = (oldest.Gap - newest.Gap) / laps;
        var buys = perLap * _lapsLeft;

        var kind = perLap >= MeaningfulRate
            ? (buys >= gap ? HorizonKind.WithinReach : HorizonKind.Closing)
            : perLap <= -MeaningfulRate
                ? HorizonKind.SlippingAway
                : HorizonKind.None;

        return new Horizon(
            trail.AheadId,
            trail.BehindId,
            trail.Position,
            gap,
            perLap,
            _lapsLeft,
            buys,
            kind,
            trail.Seen.Count,
            _estimated);
    }

    /// <summary>This pair has been talked about - start their cooldown.</summary>
    public void Said(Horizon horizon)
    {
        if (horizon is not null)
        {
            _saidAt[Key(horizon.AheadId, horizon.BehindId)] = _clock();
        }
    }

    /// <summary>A new race. Every trail and every cooldown goes.</summary>
    public void Reset()
    {
        _trails.Clear();
        _saidAt.Clear();
        _lapsLeft = 0;
        _estimated = false;
    }

    /// <summary>
    /// The leader's PACE for <see cref="LapsLeftIn"/> - the middle of their
    /// last three laps, or null with none to go on.
    ///
    /// The middle rather than the mean, and the last three rather than the
    /// best: one crash-recovery lap in a mean of three turns "about four
    /// laps left" into "about three", and the best lap is the pace they
    /// managed once, not the pace the clock will see. Pass only the laps
    /// that say anything about pace (AnnouncerControl.IsPaceLap) - a start
    /// lap is not one.
    /// </summary>
    public static double? RecentPace(IReadOnlyList<double> lapSeconds)
    {
        if (lapSeconds is null || lapSeconds.Count == 0)
        {
            return null;
        }

        var last = lapSeconds.Skip(Math.Max(0, lapSeconds.Count - 3)).Where(one => one > 0).OrderBy(one => one).ToList();

        return last.Count == 0 ? null : last[last.Count / 2];
    }

    /// <summary>
    /// How much road is left, worked out from the race's own limits - or
    /// null when nothing says, which is no horizon at all.
    ///
    /// THE QUESTION THE 14 OCT ENTRY LEFT OPEN, AND THE ANSWER (15 Oct
    /// 2026). Laps left is exact for a lap-limited race and can only be
    /// estimated for a timed one, from the clock and the leader's recent
    /// pace - and most club races are timed. Running the horizon on
    /// lap-limited races only would have switched it off for most of the
    /// evening, so it runs on both, and an estimate is CARRIED rather than
    /// hidden: <see cref="Horizon.Estimated"/> goes onto the record, and
    /// Say softens "three laps" to "about three laps". The sum is still a
    /// sum - "about four laps to find half a second, taking two tenths a
    /// lap" is true whatever the clock does next - and the "about" is what
    /// stops it pretending to be more.
    ///
    /// THE LAP IN PROGRESS COUNTS. When the clock runs out the leader takes
    /// the flag at their next crossing (RaceControlSession.
    /// FinishOnNextCrossing), so the count is taken from the leader's LAST
    /// CROSSING rather than from now - the clock left plus how far into
    /// this lap they already are - and rounded UP. Twelve seconds left, a
    /// five second lap and three seconds since they crossed is fifteen
    /// seconds of laps from the line: three crossings to go. Once the clock
    /// has run out the leader still has the lap they are on, so it is one
    /// and never nought while the race runs - the same answer a
    /// lap-limited race gives on its last lap. A lap-limited race on lap 8
    /// of 10 has two left.
    ///
    /// BOTH LIMITS SET, the nearer one wins - whichever ends the race first
    /// is the road there is. A lap limit reached first is exact, and so is
    /// the answer; a clock reached first is an estimate.
    /// </summary>
    /// <param name="lapLimit">The race's lap limit, or null for none.</param>
    /// <param name="leaderLaps">Laps the leader has completed.</param>
    /// <param name="timeLimit">The race's time limit, or null for none.</param>
    /// <param name="elapsed">Race time so far, with red flags taken out (RaceControlSession.ElapsedRaceTime).</param>
    /// <param name="leaderLapSeconds">The leader's recent lap, in seconds - their pace, not their best. Null or nought when unknown.</param>
    /// <param name="sinceLeaderCrossed">How long ago the leader last crossed the line - how far into the lap in progress they are.</param>
    public static RoadLeft? LapsLeftIn(int? lapLimit, int leaderLaps, TimeSpan? timeLimit, TimeSpan elapsed, double? leaderLapSeconds, TimeSpan sinceLeaderCrossed = default)
    {
        RoadLeft? byLaps = lapLimit is > 0 and var limit
            ? new RoadLeft(Math.Max(0, limit - leaderLaps), false)
            : null;

        RoadLeft? byClock = null;

        if (timeLimit is { } time && time > TimeSpan.Zero && leaderLapSeconds is > 0 and var pace)
        {
            var remaining = (time - elapsed).TotalSeconds;
            var fromTheLine = remaining + Math.Max(0, sinceLeaderCrossed.TotalSeconds);

            byClock = remaining <= 0
                ? new RoadLeft(1, true)
                : new RoadLeft(Math.Max(1, (int)Math.Ceiling(fromTheLine / pace)), true);
        }

        if (byLaps is { } laps && byClock is { } clock)
        {
            // The nearer end. On a tie the lap limit, which is exact.
            return clock.Laps < laps.Laps ? clock : laps;
        }

        return byLaps ?? byClock;
    }

    /// <summary>
    /// A pair's key, independent of which of them is in front - see the
    /// note in <see cref="Observe"/> on why that matters.
    /// </summary>
    private static string Key(string a, string b) =>
        string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";

    /// <summary>
    /// The line itself, kept here so the wording cannot drift - the
    /// argument DriverCut.Describe makes for itself.
    ///
    /// READ EVERY ONE OF THESE AGAINST THE NO-PREDICTION RULE at the top of
    /// this class. Each gives the room the gap, the rate and the laps, and
    /// stops. None of them says who gets there. "He has three laps to find
    /// half a second, and he is taking two tenths a lap out of it" is a
    /// sentence that is still true if the car spins on the next corner;
    /// "he is going to catch him" is not, and is the sentence this app
    /// must never say about somebody standing in the room.
    /// </summary>
    internal static string Say(Horizon horizon, string aheadNamed, string behindNamed)
    {
        var gap = RaceNumbers.GapSpoken(horizon.GapSeconds);
        var rate = RaceNumbers.GapSpoken(Math.Abs(horizon.PerLap));

        // In words, like every other figure the voice is handed (see
        // RaceNumbers) - and with "about" in front when the laps came off
        // the clock, which is the whole of what makes an estimate safe to
        // say. See LapsLeftIn.
        var laps = $"{(horizon.Estimated ? "about " : string.Empty)}{RaceNumbers.Words(horizon.LapsLeft)} "
            + Plural.Word(horizon.LapsLeft, "lap");
        var place = horizon.Position == 1 ? "the lead" : RaceNumbers.PositionSpoken(horizon.Position);

        return horizon.Kind switch
        {
            HorizonKind.WithinReach =>
                $"{behindNamed} has {laps} to find {gap} on {aheadNamed}, and is taking {rate} a lap out of "
                + $"it - that puts {place} on the line.",

            HorizonKind.Closing =>
                $"{behindNamed} is {gap} behind {aheadNamed} and closing at {rate} a lap, with {laps} left.",

            HorizonKind.SlippingAway =>
                $"{aheadNamed} is pulling away from {behindNamed} - {gap} now, and going out {rate} a lap.",

            _ => string.Empty,
        };
    }
}
