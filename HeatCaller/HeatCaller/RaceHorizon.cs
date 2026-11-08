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
/// The CLOCK ends this race, not a lap count - a timed race, or a race
/// with both limits where the clock comes first. The laps left were then
/// worked out from the clock rather than known, and they are for the sum
/// only: a timed race is never told how many laps it has left (1 Nov 2026,
/// see <see cref="RaceHorizon.Remaining"/>). It is told the time.
/// </param>
/// <param name="TimeLeft">
/// The time left on the clock when <paramref name="Estimated"/> - nought
/// once it has run out and the leader is on the lap that takes the flag.
/// Null in a race a lap count ends.
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
    bool Estimated = false,
    TimeSpan? TimeLeft = null);

/// <summary>
/// How much road is left in a race, and whether that figure is known or
/// worked out - see <see cref="RaceHorizon.LapsLeftIn"/>.
/// </summary>
/// <param name="Laps">Laps left for the leader, counting the one they are on.</param>
/// <param name="Estimated">True when it came off the clock rather than a lap limit - the clock ends this race.</param>
/// <param name="TimeLeft">
/// When the clock ends the race, the time left on it - nought once it has
/// run out. Null when a lap count ends it. What a timed race's lines are
/// said in: see <see cref="RaceHorizon.Remaining"/>.
/// </param>
public readonly record struct RoadLeft(int Laps, bool Estimated, TimeSpan? TimeLeft = null);

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

        /// <summary>
        /// The samples kept, oldest first, each with the lap it was taken on -
        /// the CHASING car's lap where the feed says it, see Observe.
        /// </summary>
        public List<(int Lap, double Gap)> Seen { get; } = new();

        /// <summary>The pair as last seen, for its penalties - see the penalty note in Observe.</summary>
        public PairGap LastPair { get; set; }
    }

    private readonly Dictionary<string, Trail> _trails = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _saidAt = new(StringComparer.Ordinal);
    private readonly Func<DateTime> _clock;

    private int _lapsLeft;

    private bool _estimated;

    private TimeSpan? _timeLeft;

    public RaceHorizon(Func<DateTime>? clock = null) => _clock = clock ?? (() => DateTime.Now);

    /// <summary>
    /// How many samples of a pair's gap are kept. Four, so the rate is
    /// taken over a few laps rather than off the last two crossings - a
    /// rate off two samples is one lap's noise reported as a trend.
    /// </summary>
    public int Window { get; set; } = 4;

    /// <summary>
    /// The fewest samples a rate is taken over - and they must span at least
    /// <see cref="LeastLapsSpanned"/> laps.
    ///
    /// THE WINDOW'S OWN RULE, NOW KEPT (8 Nov 2026). Read gave a rate off
    /// two samples a lap apart, which is the "one lap's noise reported as a
    /// trend" the window note warns about: one bobble came out as "the gap
    /// is going out by 0.70s a lap". Three readings over two laps at least
    /// is a direction, not a moment.
    /// </summary>
    public const int LeastSamples = 3;

    /// <summary>The fewest laps a rate is taken over. See <see cref="LeastSamples"/>.</summary>
    public const int LeastLapsSpanned = 2;

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
    /// The leader's lap count. The clock the rate is measured against when
    /// a pair does not carry <see cref="PairGap.BehindLaps"/> - see the
    /// per-lap note on the class, and Observe's note on whose lap it is.
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
    /// <param name="timeLeft">
    /// The time left on the clock when it is the clock that ends the race -
    /// what an estimated road is SAID in. See <see cref="Remaining"/>.
    /// </param>
    public void Observe(IReadOnlyList<PairGap> pairs, int lapsRun, int lapsLeft, bool estimated = false, TimeSpan? timeLeft = null)
    {
        _lapsLeft = lapsLeft;
        _estimated = estimated;
        _timeLeft = estimated && timeLeft is { } left ? (left < TimeSpan.Zero ? TimeSpan.Zero : left) : null;

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
                trail = new Trail { AheadId = pair.AheadId, BehindId = pair.BehindId, LastPair = pair };
                _trails[key] = trail;
            }

            // A PENALTY IS NOT PACE (8 Nov 2026). The gap is race time, which
            // carries the penalties, so a sample before a penalty and one
            // after differ by the penalty: a one second penalty read as "the
            // gap is going out by a second a lap". The rate starts again from
            // the race as it now stands; the trail and its cooldown are kept.
            if (pair.PenaltiesChangedSince(trail.LastPair))
            {
                trail.Seen.Clear();
            }

            trail.LastPair = pair;

            // KEPT IN THE ORDER THE TICK SEES THEM, so a pair that swaps
            // places is still the same trail. The key is order-independent
            // for that reason - two cars trading a position four times are
            // one story, and a key built from "who is ahead" would start a
            // new trail on every swap and never build a rate at all.
            //
            // THE RATE restarts on a swap, though (17 Oct 2026, Bug Hunt 8,
            // A07-2). Each sample is the unsigned gap to whoever was ahead
            // at the time, so a rate taken across a pass mixed gaps measured
            // against different cars - "closing at three tenths a lap" for a
            // car that had just lost the place. The trail, and with it the
            // one story and the cooldown key, is kept.
            if (!string.Equals(trail.AheadId, pair.AheadId, StringComparison.Ordinal))
            {
                trail.Seen.Clear();
            }

            trail.AheadId = pair.AheadId;
            trail.BehindId = pair.BehindId;
            trail.Position = pair.Position;

            // ONE SAMPLE PER LAP. The ticks are a half-second timer, so
            // without this the window would hold two seconds of history
            // and the rate would be taken over no laps at all.
            //
            // THE CHASING CAR'S LAP, NOT THE LEADER'S (8 Nov 2026). The gap
            // at the line only changes when the car behind crosses
            // (RaceGaps), so keyed on the leader's laps the sample taken
            // just after the leader crossed was the previous gap again under
            // a new lap number - the rate was divided by a lap that had not
            // been measured, about a third too low over four samples. Keyed
            // on the car behind, each sample is a new reading. The leader's
            // count is the fallback for a feed that does not say.
            var lap = pair.BehindLaps > 0 ? pair.BehindLaps : lapsRun;

            if (trail.Seen.Count > 0 && trail.Seen[^1].Lap == lap)
            {
                trail.Seen[^1] = (lap, pair.GapSeconds);
            }
            else
            {
                trail.Seen.Add((lap, pair.GapSeconds));
            }

            while (trail.Seen.Count > Math.Max(LeastSamples, Window))
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
        // Three readings over two laps at least - see LeastSamples.
        if (trail.Seen.Count < LeastSamples)
        {
            return null;
        }

        var oldest = trail.Seen[0];
        var newest = trail.Seen[^1];
        var laps = newest.Lap - oldest.Lap;

        // A rate needs laps to be a rate - see the per-lap note on the
        // class. Several samples taken on the same lap are one sample.
        if (laps < LeastLapsSpanned)
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
            _estimated,
            _timeLeft);
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
        _timeLeft = null;
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
        var timed = timeLimit is { } limitSet && limitSet > TimeSpan.Zero;

        if (timed && leaderLapSeconds is > 0 and var pace)
        {
            var time = timeLimit!.Value;
            var left = time - elapsed;
            var remaining = left.TotalSeconds;
            var fromTheLine = remaining + Math.Max(0, sinceLeaderCrossed.TotalSeconds);

            byClock = remaining <= 0
                ? new RoadLeft(1, true, TimeSpan.Zero)
                : new RoadLeft(Math.Max(1, (int)Math.Ceiling(fromTheLine / pace)), true, left);
        }

        if (byLaps is { } laps && byClock is { } clock)
        {
            // The nearer end. On a tie the lap limit, which is exact.
            return clock.Laps < laps.Laps ? clock : laps;
        }

        // A CLOCK AND NO PACE YET (1 Nov 2026). With both limits set and the
        // leader's pace not known, nobody can say which end comes first - a
        // club that sets "5 minutes, 99 laps" as a ceiling would otherwise
        // have heard "ninety-seven laps left" in what is a timed race. No
        // road, no line, until there is a lap to go on.
        if (timed && byClock is null)
        {
            return null;
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
    /// How much of the race is left, in the terms that END it (1 Nov 2026).
    ///
    /// WHY THIS EXISTS. A timed race was called as "about four laps left",
    /// because the road was counted in laps whatever ended the race, and
    /// the built-in commentary turned that into "four circuits to run" in
    /// a race nobody had set a lap count for. The room hears the clock, so a
    /// race the clock ends is spoken in time - "about a minute and a half
    /// left" - and only a race a lap count ends is spoken in laps. The laps
    /// a clock leaves are still worked out, for the sum behind
    /// <see cref="HorizonKind.WithinReach"/>, but they are never said.
    /// </summary>
    /// <returns>
    /// The amount alone, ready to follow "with" or "has": "three laps",
    /// "about a minute and a half", or, once the clock has run out, "the
    /// final lap". A timed race whose time left is not known comes back as
    /// "what is left of the clock" - still never a count of laps.
    /// </returns>
    internal static string Remaining(Horizon horizon)
    {
        if (!horizon.Estimated)
        {
            return $"{RaceNumbers.Words(horizon.LapsLeft)} {Plural.Word(horizon.LapsLeft, "lap")}";
        }

        if (horizon.TimeLeft is not { } left)
        {
            return "what is left of the clock";
        }

        return left <= TimeSpan.Zero ? "the final lap" : $"about {RaceNumbers.TimeLeftSpoken(left)}";
    }

    /// <summary>
    /// Whether the clock is what ends this race, for the time-left calls
    /// (1 Nov 2026). True for a timed race, and for a race with both limits
    /// unless the lap limit is known to come first - "two minutes to go" in
    /// a race that will be over in three laps is the announcer getting the
    /// race wrong. When the leader's pace is not known yet, nobody can say,
    /// and the clock - which is set, and is running - is given the benefit.
    /// </summary>
    public static bool ClockEndsIt(int? lapLimit, int leaderLaps, TimeSpan? timeLimit, TimeSpan elapsed, double? leaderLapSeconds, TimeSpan sinceLeaderCrossed = default)
    {
        if (timeLimit is not { } time || time <= TimeSpan.Zero)
        {
            return false;
        }

        if (lapLimit is not > 0)
        {
            return true;
        }

        var road = LapsLeftIn(lapLimit, leaderLaps, timeLimit, elapsed, leaderLapSeconds, sinceLeaderCrossed);

        return road is not { Estimated: false };
    }

    /// <summary>
    /// The line itself, kept here so the wording cannot drift - the
    /// argument DriverCut.Describe makes for itself.
    ///
    /// READ EVERY ONE OF THESE AGAINST THE NO-PREDICTION RULE at the top of
    /// this class. Each gives the room the gap, the rate and what is left,
    /// and stops. None of them says who gets there. "He has three laps to
    /// find half a second, and he is taking two tenths a lap out of it" is a
    /// sentence that is still true if the car spins on the next corner;
    /// "he is going to catch him" is not, and is the sentence this app
    /// must never say about somebody standing in the room.
    ///
    /// WHAT IS LEFT IS SAID IN THE RACE'S OWN TERMS (1 Nov 2026) - laps
    /// for a race a lap count ends, time for one the clock ends. See
    /// <see cref="Remaining"/>.
    /// </summary>
    internal static string Say(Horizon horizon, string aheadNamed, string behindNamed)
    {
        // MarginSpoken, not GapSpoken (17 Oct 2026, Bug Hunt 8, A07-5): a
        // rate from MeaningfulRate (two hundredths) up to a twentieth was
        // "closing at nothing a lap". From a tenth up the two read the same.
        var gap = RaceNumbers.MarginSpoken(horizon.GapSeconds);
        var rate = RaceNumbers.MarginSpoken(Math.Abs(horizon.PerLap));
        var left = Remaining(horizon);
        var finalLap = horizon.Estimated && horizon.TimeLeft is { } clock && clock <= TimeSpan.Zero;
        var place = horizon.Position == 1 ? "the lead" : RaceNumbers.PositionSpoken(horizon.Position);

        return horizon.Kind switch
        {
            HorizonKind.WithinReach =>
                $"{behindNamed} has {left} to find {gap} on {aheadNamed}, and is taking {rate} a lap out of "
                + $"it - that puts {place} on the line.",

            HorizonKind.Closing => finalLap
                ? $"{behindNamed} is {gap} behind {aheadNamed} and closing at {rate} a lap, on the final lap."
                : $"{behindNamed} is {gap} behind {aheadNamed} and closing at {rate} a lap, with {left} left.",

            HorizonKind.SlippingAway =>
                $"{aheadNamed} is pulling away from {behindNamed} - {gap} now, and going out {rate} a lap.",

            _ => string.Empty,
        };
    }
}
