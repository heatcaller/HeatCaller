namespace HeatCaller;

/// <summary>
/// Two cars next to each other in the order, as one tick of the race sees
/// them.
///
/// Ids rather than entries, and seconds rather than milliseconds: the
/// detector has no business knowing what a session entry looks like, and
/// the adapter that feeds it is three lines over RaceGaps.Between.
/// </summary>
/// <param name="AheadId">The transponder in front.</param>
/// <param name="BehindId">The transponder behind.</param>
/// <param name="Position">Where the car ahead is running. 1 is the lead.</param>
/// <param name="GapSeconds">The gap at the line, as RaceGaps measures it. NaN when it cannot be measured this tick.</param>
/// <param name="LapsDown">How many laps behind the car behind is. Anything but zero is not a fight.</param>
/// <param name="BehindLaps">
/// Laps the car behind has completed, or nought from a feed that does not
/// say. The gap at the line only changes when THIS car crosses (RaceGaps),
/// so it is what RaceHorizon keys its samples on (8 Nov 2026) - keyed on
/// the leader's laps, the sample after the leader crossed repeated the old
/// gap under a new lap and the rate came out about a third too low.
/// </param>
/// <param name="AheadPenaltyMs">Penalty the car ahead is carrying - already inside its race time, and so inside the gap.</param>
/// <param name="BehindPenaltyMs">
/// Penalty the car behind is carrying. Both are here so a change in either
/// can end the gap's history (8 Nov 2026): a gap measured across a penalty
/// moves by the penalty, and a one second penalty was read as "going out
/// by a second a lap".
/// </param>
public readonly record struct PairGap(
    string AheadId,
    string BehindId,
    int Position,
    double GapSeconds,
    int LapsDown,
    int BehindLaps = 0,
    long AheadPenaltyMs = 0,
    long BehindPenaltyMs = 0)
{
    /// <summary>The penalty one of the two cars carries, by id - zero for anyone else.</summary>
    internal long PenaltyOf(string id) =>
        string.Equals(id, AheadId, StringComparison.Ordinal) ? AheadPenaltyMs
        : string.Equals(id, BehindId, StringComparison.Ordinal) ? BehindPenaltyMs
        : 0;

    /// <summary>
    /// Whether either car's penalty differs from <paramref name="before"/> -
    /// compared by car, not by place, so a pair that has swapped is still
    /// compared like with like.
    /// </summary>
    internal bool PenaltiesChangedSince(PairGap before) =>
        before.PenaltyOf(AheadId) != AheadPenaltyMs || before.PenaltyOf(BehindId) != BehindPenaltyMs;
}

/// <summary>
/// A fight that has lasted long enough to be worth a word.
/// </summary>
/// <param name="AheadId">Who is in front right now.</param>
/// <param name="BehindId">Who is behind right now.</param>
/// <param name="Position">The position being fought over. 1 is the lead.</param>
/// <param name="Samples">How many ticks they have been locked together for.</param>
/// <param name="For">How long that is in real time.</param>
/// <param name="ClosestSeconds">The smallest gap seen during the fight.</param>
/// <param name="NowSeconds">The gap at the last tick.</param>
/// <param name="LeadChanges">How many times they have swapped places without ever separating.</param>
public sealed record Battle(
    string AheadId,
    string BehindId,
    int Position,
    int Samples,
    TimeSpan For,
    double ClosestSeconds,
    double NowSeconds,
    int LeadChanges);

/// <summary>
/// Watches for two cars that have been locked together long enough to be
/// worth talking about.
///
/// WHY A DETECTOR AND NOT A PROMPT
///
/// The model is never asked "is anything interesting happening" - it would
/// answer yes. A battle is a fact the app works out: a gap under a
/// threshold, held across consecutive ticks, for long enough that it is a
/// fight rather than a moment. The model is then handed that fact and asked
/// to say it well, which is the rule the whole commentary layer runs on.
///
/// WHY IT IS THE SEGMENT WORTH BUILDING FIRST
///
/// It is the only one that makes the broadcast sound live. An intro and a
/// recap bracket a race; a line about two cars that have been inside four
/// tenths for six laps can only have come from watching it happen.
///
/// IT IS ALSO THE MOST PERISHABLE THING HERE
///
/// A line about laps four to nine is worthless by lap twenty however well
/// it reads, so the segment's deadline is inside a single race
/// (CommentaryBudget.Spec) and a late answer is thrown away rather than
/// spoken. Nothing in this class knows about that - it only finds fights -
/// but it is why Pick returns the fight as it stands at the last tick
/// rather than a summary of one that has finished.
///
/// Pure: ids, numbers and an injectable clock. No stores, no UI, no race.
/// </summary>
public sealed class BattleWatch
{
    private sealed class Streak
    {
        public required string AheadId { get; set; }
        public required string BehindId { get; set; }
        public int Position { get; set; }
        public DateTime First { get; init; }
        public DateTime Last { get; set; }
        public int Samples { get; set; }
        public double Closest { get; set; }
        public double Now { get; set; }
        public int LeadChanges { get; set; }

        /// <summary>The pair as last measured, for its penalties - see the penalty note in Observe.</summary>
        public PairGap LastPair { get; set; }
    }

    private readonly Func<DateTime> _clock;
    private readonly Dictionary<string, Streak> _streaks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _toldAbout = new(StringComparer.Ordinal);

    public BattleWatch(Func<DateTime>? clock = null) => _clock = clock ?? (() => DateTime.Now);

    /// <summary>
    /// Inside this and they are fighting; outside it and they are merely
    /// near each other.
    ///
    /// Three quarters of a second is about a car and a half at Mini-Z
    /// pace. Wider than this and half a heat qualifies as a battle, which
    /// is the same as none of it qualifying.
    /// </summary>
    public double CloseSeconds { get; set; } = 0.75;

    /// <summary>How many consecutive ticks before a gap is a fight rather than a moment.</summary>
    public int MinSamples { get; set; } = 4;

    /// <summary>And how long in real time, so a fast polling loop cannot turn two seconds into a battle.</summary>
    public TimeSpan MinDuration { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long a fight is held, untouched, while its gap cannot be
    /// measured. That happens for the moment between one car passing and
    /// both having crossed the line, which is never longer than the new gap
    /// itself, so a couple of seconds covers it at any tick rate. Bounded so
    /// a gap that stays unmeasurable - a lap added by hand - cannot keep a
    /// fight alive on numbers that are going stale.
    /// </summary>
    public TimeSpan UnmeasuredHold { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>How long the same pair is left alone after being talked about.</summary>
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How many fights are being followed right now. For the console and the tests.</summary>
    public int Watching => _streaks.Count;

    /// <summary>
    /// One tick of the race: every adjacent pair in the running order, as
    /// it stands.
    ///
    /// A pair that is not in this tick's list has stopped being adjacent -
    /// somebody came between them, or one of them stopped - and its streak
    /// ends. That is deliberate: a fight that a third car has joined is a
    /// different story, and the one being tracked is over.
    /// </summary>
    public void Observe(IReadOnlyList<PairGap> pairs)
    {
        var now = _clock();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var pair in pairs)
        {
            if (string.IsNullOrWhiteSpace(pair.AheadId) || string.IsNullOrWhiteSpace(pair.BehindId))
            {
                continue;
            }

            var key = CommentaryFacts.Pair(pair.AheadId, pair.BehindId);

            // NO GAP THIS TICK IS NOT THE SAME AS APART. Straight after a
            // pass the car now ahead has crossed and the other has not, and
            // until it does there is no gap to measure. Ending the fight
            // there threw away the very swap that makes it worth a line, so
            // it is held as it stands and the next measured tick sees the
            // new order and counts the lead change. Never started on one,
            // and a lapped car is still dropped below.
            if (double.IsNaN(pair.GapSeconds) && pair.LapsDown == 0)
            {
                if (_streaks.TryGetValue(key, out var held) && now - held.Last <= UnmeasuredHold)
                {
                    seen.Add(key);
                }
                else
                {
                    _streaks.Remove(key);
                }

                continue;
            }

            // A lapped car is not racing the car it happens to be behind.
            // Without this the leader catching a backmarker reads as the
            // fight of the night, which is the most embarrassing thing an
            // announcer can say.
            if (pair.LapsDown != 0 || double.IsNaN(pair.GapSeconds) || pair.GapSeconds < 0 || pair.GapSeconds > CloseSeconds)
            {
                _streaks.Remove(key);
                continue;
            }

            seen.Add(key);

            // A PENALTY IS NOT RACING (8 Nov 2026). The gap is race time and
            // race time carries the penalties, so a penalty given mid-fight
            // moved the gap by the penalty - enough to drop the penalised
            // car behind and count a "lead change" nobody saw on track, and
            // to keep a closest gap from before it in a fight measured
            // after. The fight starts again from this tick, as it now stands.
            if (_streaks.TryGetValue(key, out var before) && pair.PenaltiesChangedSince(before.LastPair))
            {
                _streaks.Remove(key);
            }

            if (!_streaks.TryGetValue(key, out var streak))
            {
                _streaks[key] = new Streak
                {
                    AheadId = pair.AheadId,
                    BehindId = pair.BehindId,
                    Position = pair.Position,
                    First = now,
                    Last = now,
                    Samples = 1,
                    Closest = pair.GapSeconds,
                    Now = pair.GapSeconds,
                    LastPair = pair,
                };

                continue;
            }

            // They have swapped without ever separating - which is the
            // best thing that can happen to this segment, and the reason
            // the streak is keyed on the pair rather than on who leads it.
            if (!string.Equals(streak.AheadId, pair.AheadId, StringComparison.Ordinal))
            {
                streak.LeadChanges++;
                streak.AheadId = pair.AheadId;
                streak.BehindId = pair.BehindId;
            }

            streak.Position = pair.Position;
            streak.LastPair = pair;
            streak.Last = now;
            streak.Samples++;
            streak.Now = pair.GapSeconds;
            streak.Closest = Math.Min(streak.Closest, pair.GapSeconds);
        }

        foreach (var key in _streaks.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _streaks.Remove(key);
        }
    }

    /// <summary>
    /// The fight most worth a line right now, or null when there isn't
    /// one.
    ///
    /// Ranked by what makes a story, not by what is closest: places
    /// actually changing hands beats a gap that has sat at two tenths for
    /// a minute, and a fight for the lead beats the same fight for
    /// seventh. Takes nothing and changes nothing - call Said when the
    /// line has actually been written.
    ///
    /// <paramref name="skip"/> passes over a fight the caller cannot use,
    /// so the next one down is offered instead (13 Nov 2026, bug hunt 17) -
    /// see CommentaryDirector's BattleWatch candidates for the one it has.
    /// </summary>
    public Battle? Pick(Func<Battle, bool>? skip = null)
    {
        var now = _clock();

        return _streaks.Values
            .Where(s => s.Samples >= MinSamples)
            .Where(s => s.Last - s.First >= MinDuration)
            .Where(s => !RecentlyTold(s, now))
            .OrderByDescending(s => s.LeadChanges)
            .ThenBy(s => s.Position)
            .ThenByDescending(s => s.Last - s.First)
            .Select(s => new Battle(
                s.AheadId,
                s.BehindId,
                s.Position,
                s.Samples,
                s.Last - s.First,
                s.Closest,
                s.Now,
                s.LeadChanges))
            .FirstOrDefault(battle => skip?.Invoke(battle) != true);
    }

    /// <summary>
    /// This fight has been talked about. Left alone for the cooldown, so
    /// the same two cars circulating nose to tail for a whole heat are one
    /// story and not twenty.
    /// </summary>
    public void Said(Battle battle)
    {
        lock (_toldGate)
        {
            _toldAbout[CommentaryFacts.Pair(battle.AheadId, battle.BehindId)] = _clock();
        }
    }

    /// <summary>
    /// The line about this pair was thrown away before a word of it was
    /// heard, so its cooldown is lifted and the fight can be called again.
    /// Either order of the two ids.
    /// </summary>
    /// <remarks>
    /// WHY (23 Nov 2026, bug hunt 24): a battle watch binned unheard still
    /// left the pair alone for two minutes, serving a cooldown for a line
    /// nobody in the room heard. Locked, with the cooldown's other readers
    /// and writers, because the line is dropped on the voice's thread while
    /// the director picks on its own.
    /// </remarks>
    public void Unsaid(string aheadId, string behindId)
    {
        if (string.IsNullOrWhiteSpace(aheadId) || string.IsNullOrWhiteSpace(behindId))
        {
            return;
        }

        lock (_toldGate)
        {
            _toldAbout.Remove(CommentaryFacts.Pair(aheadId, behindId));
        }
    }

    /// <summary>
    /// A new race. Every fight is over and every cooldown with it - two
    /// drivers who fought in their heat are a fresh story in the main.
    /// </summary>
    public void Reset()
    {
        _streaks.Clear();

        lock (_toldGate)
        {
            _toldAbout.Clear();
        }
    }

    private bool RecentlyTold(Streak streak, DateTime now)
    {
        lock (_toldGate)
        {
            return _toldAbout.TryGetValue(CommentaryFacts.Pair(streak.AheadId, streak.BehindId), out var when)
                   && now - when < Cooldown;
        }
    }

    /// <summary>Guards <see cref="_toldAbout"/> - see <see cref="Unsaid"/>.</summary>
    private readonly object _toldGate = new();
}
