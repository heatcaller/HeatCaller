namespace HeatCaller;

/// <summary>
/// The two cars a battle line is about, as the line describes them:
/// <paramref name="AheadId"/> in front, <paramref name="BehindId"/> chasing,
/// fighting for <paramref name="Position"/>. Transponders, the key the
/// announcer's standings are kept by. See CommentaryFacts.Battle.
/// </summary>
public sealed record BattlePair(string AheadId, string BehindId, int Position)
{
    public static BattlePair Of(Battle battle) => new(battle.AheadId, battle.BehindId, battle.Position);
}

/// <summary>
/// One battle line in the voice queue, tagged with the pair it describes so
/// the board can overrule it. <see cref="Overtaken"/> is set, once, when the
/// car it called "behind" gets ahead.
/// </summary>
internal sealed class BattleLine
{
    private int _overtaken;

    public BattleLine(BattlePair pair) => Pair = pair;

    public BattlePair Pair { get; }

    /// <summary>
    /// The flag and countdown generations the voice loop took it off the
    /// queue under - see AnnouncementService.CutSinceTaken. Null until then.
    /// </summary>
    public int? TakenUnderInterrupt { get; set; }

    public int? TakenUnderCountdown { get; set; }

    public bool Overtaken => Volatile.Read(ref _overtaken) == 1;

    /// <summary>True the first time only, so one pass is called once.</summary>
    public bool MarkOvertaken() => Interlocked.Exchange(ref _overtaken, 1) == 0;
}

/// <summary>What the board did to the battle lines in the air - see <see cref="AnnouncementService.JudgeBattleLines"/>.</summary>
/// <param name="Cut">The line being said when the pass happened - cut off, with the pass called in its place. Null when none.</param>
/// <param name="Stale">Lines still waiting their turn about a pair that has swapped - binned unheard.</param>
internal sealed record BattleVerdict(BattleLine? Cut, IReadOnlyList<BattleLine> Stale);

/// <summary>
/// A battle line overtaken by the battle (1 Nov 2026, asked for by Asher:
/// "during a AI battle call out, sometimes the person behind gets ahead of
/// the other person but the ai script finishes betraying what's on the
/// board").
///
/// The Battle watch segment is written about the order a few seconds ago
/// and takes ten more to say, and a close fight can swap in that time - so
/// the room heard "Sam is all over the back of Bill" with Sam already in
/// front on the board. Now the line carries its pair (BattlePair, from the
/// director through AboutBattle) and every board update asks whether the
/// car it called "behind" has got ahead. If it has, the line stops
/// mid-sentence and a short pass call goes next in the queue.
///
/// ONLY THESE TWO CARS. A third car going past both of them, or any other
/// pair swapping, leaves the line alone: the line is still true. And ONCE:
/// the line is marked overtaken the first time, so the pass is called once
/// however many crossings follow.
///
/// FOR THE LEAD the pass call IS the lead change call - it uses the Leader
/// change call's own "takes the lead" wording, and the ordinary lead change
/// call for that same crossing is not queued as well (HandleStandingUpdated).
/// </summary>
public sealed partial class AnnouncementService
{
    /// <summary>
    /// The pass call for a fight for the lead - the first line of the
    /// shipped Leader change call, so the cut-in sounds like the call it
    /// stands in for.
    /// </summary>
    internal const string BattlePassForTheLead = "[excited] And {driver} takes the lead!";

    /// <summary>The pass call for any other place. {place} is the spoken ordinal - "third".</summary>
    internal const string BattlePassForAPlace = "[excited] And {driver} is through, up into {place}!";

    /// <summary>The pair the line being queued right now is about - see <see cref="AboutBattle"/>.</summary>
    private readonly AsyncLocal<BattlePair?> _queueingAbout = new();

    /// <summary>The battle line the voice loop has taken off the queue and is saying, or null. Under _voiceQueueLock.</summary>
    private BattleLine? _battleOnAir;

    /// <summary>The battle line a voice-queue line is, flowed into its work the way the generations are.</summary>
    private readonly AsyncLocal<BattleLine?> _dequeuedBattle = new();

    /// <summary>
    /// Tags every line queued inside the scope as being about
    /// <paramref name="pair"/>, so the board can cut it off - see the class
    /// remarks. Wrap the SpeakReminder or SpeakPrepared call that says a
    /// battle line. A null pair tags nothing.
    /// </summary>
    public IDisposable AboutBattle(BattlePair? pair)
    {
        var before = _queueingAbout.Value;
        _queueingAbout.Value = pair;

        return new Scope(() => _queueingAbout.Value = before);
    }

    private sealed class Scope(Action end) : IDisposable
    {
        private Action? _end = end;

        public void Dispose() => Interlocked.Exchange(ref _end, null)?.Invoke();
    }

    /// <summary>
    /// True when <paramref name="pair"/>'s chaser is now ahead of the car it
    /// was chasing, by the leaderboard's own rule: more laps, then the
    /// earlier crossing. False while either car is unknown.
    /// </summary>
    internal static bool HasPassed(BattlePair pair, Func<string, RaceStanding?> standingOf)
    {
        if (standingOf(pair.AheadId) is not { } ahead || standingOf(pair.BehindId) is not { } behind)
        {
            return false;
        }

        return behind.Laps > ahead.Laps
            || (behind.Laps == ahead.Laps && behind.ElapsedMs < ahead.ElapsedMs);
    }

    /// <summary>
    /// Which battle lines the board has just overruled: the one on air, and
    /// any still queued, whose chaser has got ahead. Each is marked
    /// overtaken here, so a line is only ever judged overtaken once.
    /// </summary>
    internal static BattleVerdict JudgeBattleLines(BattleLine? onAir, IEnumerable<BattleLine> queued, Func<string, RaceStanding?> standingOf)
    {
        var cut = onAir is { Overtaken: false } line && HasPassed(line.Pair, standingOf) && line.MarkOvertaken()
            ? line
            : null;

        var stale = queued
            .Where(waiting => !waiting.Overtaken && HasPassed(waiting.Pair, standingOf))
            .Distinct()
            .Where(waiting => waiting.MarkOvertaken())
            .ToList();

        return new BattleVerdict(cut, stale);
    }

    /// <summary>
    /// The words of the pass call: who got through, and into which place.
    /// <paramref name="place"/> is where the chaser now runs.
    /// </summary>
    internal static string BattlePassWords(string driver, int place) =>
        place <= 1
            ? BattlePassForTheLead.Replace("{driver}", driver, StringComparison.Ordinal)
            : BattlePassForAPlace
                .Replace("{driver}", driver, StringComparison.Ordinal)
                .Replace("{place}", LeadTracker.SpokenOrdinal(place), StringComparison.Ordinal);

    /// <summary>The standing for a car, or null - for HasPassed off the timing thread, where _standings may be mid-write.</summary>
    private RaceStanding? StandingOrNull(string transponder)
    {
        try
        {
            return _standings.TryGetValue(transponder, out var standing) ? standing : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// The battle line a line being queued now is, or null when it is not
    /// one. A line whose pair has ALREADY swapped - in the seconds it took
    /// to write - comes back overtaken, and EnqueueVoiceWork drops it.
    /// </summary>
    private BattleLine? BattleLineBeingQueued()
    {
        if (_queueingAbout.Value is not { } pair)
        {
            return null;
        }

        var line = new BattleLine(pair);

        if (HasPassed(pair, StandingOrNull))
        {
            line.MarkOvertaken();
        }

        return line;
    }

    /// <summary>
    /// What the voice loop does when it takes a battle line off the queue -
    /// for tests, which cannot hold a line in the speaker while the board
    /// moves under it.
    /// </summary>
    internal BattleLine PutBattleLineOnAir(BattlePair pair)
    {
        var line = new BattleLine(pair);

        lock (_voiceQueueLock)
        {
            line.TakenUnderInterrupt = Volatile.Read(ref _interruptGeneration);
            line.TakenUnderCountdown = Volatile.Read(ref _countdownCutGeneration);
            _battleOnAir = line;
        }

        return line;
    }

    /// <summary>True when a flag, the countdown or Reset has cut everything since the voice loop took this line.</summary>
    private bool CutSinceTaken(BattleLine line) =>
        (line.TakenUnderInterrupt is int interrupt && interrupt != Volatile.Read(ref _interruptGeneration))
        || (line.TakenUnderCountdown is int countdown && countdown != Volatile.Read(ref _countdownCutGeneration));

    /// <summary>True when the line being produced is a battle line the board has overruled since it was queued.</summary>
    private bool IsOvertakenOnTrack() => _dequeuedBattle.Value is { Overtaken: true };

    /// <summary>
    /// Asked on every board update, on the timing thread, before the lead
    /// change call. Cuts off a battle line the battle has overtaken, bins
    /// any still waiting, and queues the pass call next. Returns true when
    /// a pass for the LEAD was called, so the lead change call for the same
    /// crossing is not queued behind it.
    /// </summary>
    /// <param name="ordered">The real field in board order, ghosts left out while they are not announced.</param>
    private bool CallTheBattlePass(IReadOnlyList<RaceStanding> ordered)
    {
        BattleVerdict verdict;
        List<Action>? giveBack = null;

        lock (_voiceQueueLock)
        {
            if (_battleOnAir is null && !_voiceQueue.Any(item => item.Battle is not null))
            {
                return false;
            }

            // A line a flag, the countdown or Reset has already overtaken is
            // not in the speaker any more - and something else may be, a
            // flag call played outside the queue, which must not be cut.
            var onAir = _battleOnAir is { } line && !CutSinceTaken(line) ? line : null;

            verdict = JudgeBattleLines(
                onAir,
                _voiceQueue.Select(item => item.Battle).OfType<BattleLine>(),
                transponder => _standings.TryGetValue(transponder, out var standing) ? standing : null);

            if (verdict.Cut is null && verdict.Stale.Count == 0)
            {
                return false;
            }

            // The rest of a two-hander about the same fight goes with it.
            var cutPair = verdict.Cut?.Pair;
            BinQueuedLocked(item => item.Battle is { } line
                && (verdict.Stale.Contains(line) || (cutPair is not null && line.Pair == cutPair)), ref giveBack);
        }

        GiveBackLost(giveBack);

        if (verdict.Cut is not { } cut)
        {
            StatusMessage?.Invoke($"[voice] Dropped {Plural.Of(verdict.Stale.Count, "waiting battle line")} - the two cars swapped before it was said.");
            return false;
        }

        var place = 1 + ordered.ToList().FindIndex(standing => standing.Transponder == cut.Pair.BehindId);

        if (place < 1)
        {
            place = cut.Pair.Position;
        }

        var wasSpeaking = StopCurrentPlayback();
        var driver = DisplayNameFor(cut.Pair.BehindId);

        StatusMessage?.Invoke(
            $"[voice] Battle line {(wasSpeaking ? "cut off mid-sentence" : "dropped before it spoke")} - "
            + $"{driver} got past {DisplayNameFor(cut.Pair.AheadId)}.");

        if (Muted || _postRaceQuiet)
        {
            return false;
        }

        var words = BattlePassWords(driver, place);
        var type = place <= 1 ? AnnouncementEventType.LeaderChange : AnnouncementEventType.CloseBattle;

        RaiseAnnouncementText(words, cut.Pair.BehindId);

        lock (_voiceQueueLock)
        {
            // Next - behind only an instruction or a race moment already
            // waiting, the slot a kept call gets (HeldOverSlot). Straight
            // in, past the pacing gate: the battle line it replaces was
            // already given the air, and this is the end of that line.
            var at = HeldOverSlot(_voiceQueue.Select(waiting => waiting.Type).ToList());

            _voiceQueue.Insert(at, new QueuedVoiceItem(type, cut.Pair.BehindId, OnAir: "Battle", Work: async () =>
            {
                try
                {
                    await SynthesizeAndPlayAsync(words, "[voice] Battle pass");
                }
                catch (Exception ex)
                {
                    StatusMessage?.Invoke($"[voice] Battle pass failed: {ex.Message}");
                }
            }));
        }

        _voiceQueueSignal.Release();

        return place <= 1;
    }
}
