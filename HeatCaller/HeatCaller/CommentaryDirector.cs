namespace HeatCaller;

/// <summary>
/// A line the director has had written and wants said.
/// </summary>
/// <param name="Kind">Which segment it is, for the console and for the Announcer screen.</param>
/// <param name="Text">The whole thing as one piece of text - what the screen shows and "repeat that" reads back.</param>
/// <param name="Turns">The two-voice split, or empty when it is one voice.</param>
public sealed record SpokenSegment(SegmentKind Kind, string Text, IReadOnlyList<ExchangeTurn> Turns)
{
    /// <summary>True when this should be spoken in two voices. See CommentaryExchange.</summary>
    public bool IsExchange => Turns.Count > 0;
}

/// <summary>
/// The one place the commentary layer's pieces meet.
///
/// WHY THERE IS A DIRECTOR AT ALL
///
/// By itself every piece here is simple: a budget that says yes or no, a
/// ledger of what has been said, two detectors, a dozen fact builders.
/// What is NOT simple is the order they have to be used in, and getting
/// it wrong is silent - a line that cost the club its allowance and was
/// never spoken, or the best fact of the night burned on a request that
/// timed out. Scattering that order through the race loop would mean
/// getting it right in a dozen places and then again for every segment
/// added later.
///
/// So the order lives here, once:
///
///   1. Build the candidate facts.          Free.
///   2. Pack them, dropping what has        Free.
///      already been said. Empty -> stop.
///   3. Reserve from the budget.            Free. Refused -> stop.
///   4. Write the line.                     This is the only step that costs.
///   5. Written and still worth saying:     Settle the budget, mark the facts.
///   6. Failed, or came back too late:      Release the budget, mark nothing.
///
/// WHAT IT DOES NOT DO
///
/// It never touches a store, a clock it does not own, or the voice. The
/// host feeds it what the race is doing and speaks what comes back, which
/// is what keeps a race night's worth of behaviour inside a table test.
///
/// WHAT IS NOT FED YET
///
/// Every fact source is a function the host sets, and one left null simply
/// means that segment never fires. The three live ones and the show's own
/// bookends need almost nothing; the history, qualifying and championship
/// segments each need a real reader behind them, and until one is set they
/// cost nothing and say nothing. That is deliberate - a half-fed segment
/// that invents the other half is worse than one that stays quiet.
/// </summary>
public sealed class CommentaryDirector
{
    private readonly CommentaryService _commentary;
    private readonly AppSettings _settings;
    private readonly Func<DateTime> _clock;

    private string _raceKey = string.Empty;
    private string? _className;
    private bool _racing;

    public CommentaryDirector(CommentaryService commentary, AppSettings settings, Func<DateTime>? clock = null)
    {
        _commentary = commentary;
        _settings = settings;
        _clock = clock ?? (() => DateTime.Now);

        Budget = new CommentaryBudget(_clock);
        Battles = new BattleWatch(_clock);
        Pace = new PaceWatch();
        Said = new SaidTonight();
        Sponsors = new SponsorRotation();
    }

    /// <summary>What may be spent and when. The club's switches are applied from settings in NewMeeting.</summary>
    public CommentaryBudget Budget { get; }

    /// <summary>Watches the running order for a fight worth calling.</summary>
    public BattleWatch Battles { get; }

    /// <summary>Watches the laps for a record falling.</summary>
    public PaceWatch Pace { get; }

    /// <summary>What has been said tonight, so nothing is said twice.</summary>
    public SaidTonight Said { get; }

    /// <summary>Whose turn it is for a sponsor read. Not on the budget's ladder - see SponsorRotation.</summary>
    public SponsorRotation Sponsors { get; }

    /// <summary>Console lines - every refusal says why, or a quiet night is unexplainable.</summary>
    public event Action<string>? Status;

    /// <summary>
    /// True while a line is being written - the two to five seconds
    /// before the announcer starts talking.
    ///
    /// This is the one piece of state a race director actually needs. A
    /// segment that fires without warning is a segment that fires over
    /// the top of whatever they were about to say into the mic, and no
    /// amount of console logging helps with that at the time.
    /// </summary>
    public bool Writing { get; private set; }

    /// <summary>The last thing said, for the strip on Race Control and for "what did it just cover".</summary>
    public SpokenSegment? LastSaid { get; private set; }

    /// <summary>
    /// Nothing is written or said while this is on.
    ///
    /// The race director's own hold. Knowing a line is coming is only
    /// worth anything if there is something to do about it - a prize
    /// giving, a crash to talk through, a word with a driver - so the
    /// warning and the switch belong together.
    ///
    /// Nothing is lost by holding: a fight is still a fight when it comes
    /// off, and a fact that went unsaid is unspent.
    /// </summary>
    public bool Held { get; set; }

    /// <summary>What the night has spent, in a line. See CommentaryBudget.Describe.</summary>
    public string Describe() => Budget.Describe();

    // ------------------------------------------------------------------
    // What the host has to supply. A null one means that segment is quiet.
    // ------------------------------------------------------------------

    /// <summary>A transponder or driver id to the name to say. Needed by everything in a race.</summary>
    public Func<string, string?>? NameFor { get; set; }

    /// <summary>The next few rounds on tonight's programme, and roughly how long until the next one.</summary>
    public Func<(IReadOnlyList<UpcomingRound> Rounds, TimeSpan? InAbout)>? Upcoming { get; set; }

    /// <summary>The club's sponsors and the share each bought.</summary>
    public Func<IReadOnlyList<SponsorSlot>>? SponsorList { get; set; }

    /// <summary>The evening's shape, for the cold open.</summary>
    public Func<NightAhead>? NightAhead { get; set; }

    /// <summary>The evening's numbers, for the sign-off.</summary>
    public Func<NightDone>? NightDone { get; set; }

    /// <summary>The grid as the club's history knows them, and the pairs with previous.</summary>
    public Func<(IReadOnlyList<DriverForm> Grid, IReadOnlyList<HeadToHead> Rivalries)>? GridHistory { get; set; }

    /// <summary>A class's qualifying, once it is done.</summary>
    public Func<IReadOnlyList<QualifyingLine>>? Qualifying { get; set; }

    /// <summary>A class's championship, after its last main.</summary>
    public Func<IReadOnlyList<ChampionshipStanding>>? Championship { get; set; }

    // ------------------------------------------------------------------
    // The night
    // ------------------------------------------------------------------

    /// <summary>
    /// A race day has been opened. Picks tonight's spend and tonight's
    /// facts back up when it is the same day as before - a laptop gets
    /// closed between the heats and the mains - and starts fresh when it
    /// is not.
    /// </summary>
    public void NewMeeting(string dayId)
    {
        Budget.Restore(_settings.CommentaryBudgetNight, dayId);
        Budget.RestoreSwitches(_settings.CommentarySegmentsOff);
        Budget.NightsLeftThisMonth = _settings.CommentaryNightsPerMonth;
        Budget.KeepEverythingRunning = _settings.CommentaryKeepEverything;

        Said.NewMeeting(dayId);
        Sponsors.NewMeeting(dayId);

        _dayKey = dayId ?? string.Empty;

        Battles.Reset();
        Pace.Reset();

        // Asked before anything is paid for: a segment whose every fact
        // has already been used tonight is dropped for free.
        Budget.NothingNewToSay = (kind, cls) => Said.NothingLeftIn(Candidates(kind, cls));
    }

    /// <summary>How much of tonight's programme has run. Called whenever the programme is refreshed.</summary>
    public void NightShape(int roundsRun, int roundsTotal) => Budget.NightShape(roundsRun, roundsTotal);

    /// <summary>The club's month, as the relay reported it on its last answer.</summary>
    public void MonthlyUsage(int used, int allowance) => Budget.MonthlyUsage(used, allowance);

    /// <summary>
    /// A race has gone green. The detectors start again, because a fight
    /// in a heat is a fresh story in the main and a record "of the race"
    /// means this race.
    /// </summary>
    public void RaceStarted(string raceKey, string? className)
    {
        _raceKey = string.IsNullOrWhiteSpace(raceKey) ? _clock().Ticks.ToString() : raceKey;
        _className = className;
        _racing = true;

        Battles.Reset();
        Pace.Reset();
    }

    /// <summary>The flag is out. Nothing in-race is worth saying from here.</summary>
    public void RaceFinished() => _racing = false;

    /// <summary>One tick of the running order - every adjacent pair, as the race stands. See BattleWatch.</summary>
    public void Standings(IReadOnlyList<PairGap> pairs)
    {
        if (_racing)
        {
            Battles.Observe(pairs);
        }
    }

    /// <summary>A lap has come in. See PaceWatch.</summary>
    public void Lap(string driverId, double seconds)
    {
        if (!_racing)
        {
            return;
        }

        if (Pace.Saw(new LapSeen(driverId, seconds)) is { } note)
        {
            _pending = note;
            _pendingAt = _clock();
        }
    }

    private PaceNote? _pending;
    private DateTime _pendingAt;

    /// <summary>Tonight's spend and tonight's facts, for AppSettings to hold across a restart.</summary>
    public void Save()
    {
        _settings.CommentaryBudgetNight = Budget.Save();
        _settings.Save();
    }

    // ------------------------------------------------------------------
    // The pull
    // ------------------------------------------------------------------

    /// <summary>
    /// The next thing worth saying, or null - which is most of the time.
    ///
    /// Called on the host's own timer rather than pushing: the host knows
    /// whether the announcer is mid-sentence, whether a race is being
    /// staged, and whether anybody is listening, and none of that is this
    /// class's business.
    /// </summary>
    public async Task<SpokenSegment?> NextAsync(CancellationToken ct = default)
    {
        if (Held)
        {
            return null;
        }

        foreach (var kind in Order())
        {
            var said = await TryAsync(kind, ct).ConfigureAwait(false);

            if (said is not null)
            {
                return said;
            }

            if (ct.IsCancellationRequested)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// A sponsor read, which is not a segment and never goes through the
    /// budget - see SponsorRotation. Returns null when the club has sold
    /// nothing.
    /// </summary>
    public SponsorRead? NextSponsorRead(SponsorMoment moment)
    {
        var sponsors = SponsorList?.Invoke();

        return sponsors is null or { Count: 0 } ? null : Sponsors.Next(sponsors, moment);
    }

    /// <summary>
    /// Which segments to try, in the order that matters most right now.
    ///
    /// The live ones first while a race is running, because they are the
    /// only ones that stop being true; between races the order is simply
    /// the tier, then how perishable each one is.
    /// </summary>
    private IEnumerable<SegmentKind> Order()
    {
        var kinds = CommentaryBudget.AllSpecs
            .Where(spec => spec.Tier != SegmentTier.Essential)
            .Where(spec => InRace(spec.Kind) == _racing)
            .OrderBy(spec => spec.Tier)
            .ThenBy(spec => spec.NeedsBy)
            .Select(spec => spec.Kind);

        return kinds.ToList();
    }

    /// <summary>True for the segments that only make sense while cars are on track.</summary>
    private static bool InRace(SegmentKind kind) =>
        kind is SegmentKind.BattleWatch or SegmentKind.PaceNote;

    private async Task<SpokenSegment?> TryAsync(SegmentKind kind, CancellationToken ct)
    {
        var spec = CommentaryBudget.Spec(kind);
        var blocks = Candidates(kind, _className);

        if (blocks.Count == 0)
        {
            return null;
        }

        var pack = FactPack.Build(blocks, Said);

        if (pack.Empty)
        {
            return null;
        }

        var verdict = Budget.Reserve(kind, _className);

        if (!verdict.Allowed)
        {
            Say($"[brain] Skipped {spec.Title.ToLowerInvariant()} - {verdict.Reason}.");
            return null;
        }

        // From here on something has been taken and must be given back on
        // every path out. The deadline starts now, not when the answer
        // arrives: a battle call that took twenty seconds to write is
        // describing a race that has moved on.
        var asked = _clock();

        try
        {
            Writing = true;

            var written = await WriteAsync(kind, spec, pack, ct).ConfigureAwait(false);

            if (written is null)
            {
                Budget.Release(verdict.Ticket);
                return null;
            }

            if (_clock() - asked > spec.NeedsBy)
            {
                Budget.Release(verdict.Ticket);
                Say($"[brain] Dropped {spec.Title.ToLowerInvariant()} - it came back after the moment had gone.");
                return null;
            }

            Budget.Settle(verdict.Ticket);
            Said.Mark(pack.Keys);
            Spent(kind);
            Save();

            LastSaid = written;

            return written;
        }
        catch (Exception ex)
        {
            Budget.Release(verdict.Ticket);
            Say($"[brain] {spec.Title} failed: {ex.GetType().Name}: {ex.Message}");

            return null;
        }
        finally
        {
            Writing = false;
        }
    }

    /// <summary>
    /// Writes the line, as a two-hander where the club has a co-host and
    /// the segment suits one.
    ///
    /// An exchange that cannot be split falls through to the single voice
    /// rather than being lost. That costs a second request, which is why
    /// it is worth saying plainly: it only happens when the model ignored
    /// the format, and silence would cost more.
    /// </summary>
    private async Task<SpokenSegment?> WriteAsync(SegmentKind kind, SegmentSpec spec, PackedFacts pack, CancellationToken ct)
    {
        if (_settings.CommentaryCoHost && spec.CanBeExchange)
        {
            var turns = await _commentary
                .BuildExchangeAsync(kind, pack, announcer: null, _settings.CommentaryCoHostName, ct: ct)
                .ConfigureAwait(false);

            if (turns.Count > 0)
            {
                return new SpokenSegment(
                    kind,
                    CommentaryExchange.Flatten(turns, announcer: null, _settings.CommentaryCoHostName),
                    turns);
            }

            Say($"[brain] {spec.Title} came back in one piece rather than two - saying it in one voice.");
        }

        var line = await _commentary.BuildSegmentAsync(kind, pack, ct).ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(line)
            ? null
            : new SpokenSegment(kind, line, Array.Empty<ExchangeTurn>());
    }

    /// <summary>
    /// What the detector or the store has to say about this segment right
    /// now. Costs nothing - it is arithmetic over things this laptop
    /// already holds - which is what lets it be asked before the budget.
    /// </summary>
    private IReadOnlyList<FactBlock> Candidates(SegmentKind kind, string? className)
    {
        var names = NameFor ?? (_ => null);

        switch (kind)
        {
            case SegmentKind.BattleWatch:
            {
                return Battles.Pick() is { } battle
                    ? new[] { CommentaryFacts.Battle(battle, names, _raceKey) }
                    : Array.Empty<FactBlock>();
            }

            case SegmentKind.PaceNote:
            {
                if (_pending is not { } note)
                {
                    return Array.Empty<FactBlock>();
                }

                // A record lap goes off as fast as a battle call does.
                // One seen on lap three and finally affordable on lap
                // thirty would be announced as though it had just
                // happened.
                if (_clock() - _pendingAt > CommentaryBudget.Spec(SegmentKind.PaceNote).NeedsBy)
                {
                    _pending = null;

                    return Array.Empty<FactBlock>();
                }

                return new[] { CommentaryFacts.Pace(note, names, _raceKey) };
            }

            case SegmentKind.ComingUpNext:
            {
                if (Upcoming?.Invoke() is not { } next)
                {
                    return Array.Empty<FactBlock>();
                }

                return CommentaryFacts.ComingUpNext(next.Rounds, next.InAbout, _raceKey) is { } block
                    ? new[] { block }
                    : Array.Empty<FactBlock>();
            }

            case SegmentKind.GridHistory:
            {
                // The class goes with it: the reader counted these over
                // one class's races, and the lines have to say which.
                return GridHistory?.Invoke() is { } history
                    ? CommentaryHistoryFacts.ForIntro(history.Grid, history.Rivalries, _raceKey, className)
                    : Array.Empty<FactBlock>();
            }

            case SegmentKind.QualifyingReport:
            {
                return Qualifying?.Invoke() is { } order
                    ? CommentaryReportFacts.QualifyingReport(order, className ?? string.Empty, _raceKey)
                    : Array.Empty<FactBlock>();
            }

            case SegmentKind.ChampionshipCheck:
            {
                return Championship?.Invoke() is { } table
                    ? CommentaryReportFacts.ChampionshipCheck(table, className ?? string.Empty, _raceKey)
                    : Array.Empty<FactBlock>();
            }

            case SegmentKind.ColdOpen:
            {
                if (NightAhead?.Invoke() is not { } ahead)
                {
                    return Array.Empty<FactBlock>();
                }

                return CommentaryShowFacts.ColdOpen(ahead, _dayKey) is { } block
                    ? new[] { block }
                    : Array.Empty<FactBlock>();
            }

            case SegmentKind.SignOffNumbers:
            {
                return NightDone?.Invoke() is { } done
                    ? CommentaryShowFacts.SignOff(done, _dayKey)
                    : Array.Empty<FactBlock>();
            }

            // Everything else is waiting on a reader of its own. Nothing
            // to say is the honest answer until one is wired, and it
            // costs nothing to give.
            default:
                return Array.Empty<FactBlock>();
        }
    }

    /// <summary>What a segment has to do once its line has actually been said.</summary>
    private void Spent(SegmentKind kind)
    {
        switch (kind)
        {
            case SegmentKind.BattleWatch:
            {
                // Left alone for the cooldown, so two cars nose to tail
                // for a whole heat are one story and not twenty.
                if (Battles.Pick() is { } battle)
                {
                    Battles.Said(battle);
                }

                break;
            }

            case SegmentKind.PaceNote:
                _pending = null;
                break;
        }
    }

    private string _dayKey = string.Empty;

    /// <summary>Which race day the show's own bookends belong to. Set by NewMeeting.</summary>
    public string DayKey
    {
        get => _dayKey;
        set => _dayKey = value ?? string.Empty;
    }

    private void Say(string line)
    {
        try
        {
            Status?.Invoke(line);
        }
        catch
        {
            // A listener's failure must not become a commentary failure.
        }
    }
}
