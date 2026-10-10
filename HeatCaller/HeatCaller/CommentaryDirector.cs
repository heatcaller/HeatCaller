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
        Continuity = new ContinuityVoice();

        // Whatever ad breaks have already run tonight are not this
        // director's to read for. One is built the first time the
        // announcer screen's tick asks for it, which can be well after the
        // Big Screen has started its rotation - and a director born owing
        // four sponsors a read would fire four of them into the first gap
        // it saw. See NextAdBreakRead.
        _adBreaksSeen = AdBreak.BreaksEnded;
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

    /// <summary>
    /// Whose turn each phrasing of a continuity line is, and what the room
    /// has already heard tonight - see ContinuityVoice.
    ///
    /// Not on the budget's ladder either, and for a stronger reason than a
    /// sponsor read: a bridge is built by concatenating facts this laptop
    /// already holds, so no model is asked and nothing is reserved. It is
    /// the one part of the broadcast that is still there when the month's
    /// allowance has gone, which is the whole argument for templating it.
    /// </summary>
    public ContinuityVoice Continuity { get; }

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

    /// <summary>
    /// The next few rounds on tonight's programme, and roughly how long
    /// until the next one. Wire this to CommentaryReaders.Upcoming.
    ///
    /// It feeds two different things now, and the second one is the reason
    /// it matters more than it did. The "coming up next" segment reads the
    /// rounds out; the continuity bridges name the first of them, and the
    /// InAbout is what a show BLOCK is fitted to - it is the gap, so
    /// without it a running order between two heats is reckoned against
    /// ShowBlock.NoLimit and leans on the race-state watcher to cut it
    /// short. See ContinuityShow.
    /// </summary>
    public Func<(IReadOnlyList<UpcomingRound> Rounds, TimeSpan? InAbout)>? Upcoming { get; set; }

    /// <summary>
    /// The round whose race has just been run, for the look-back half of a
    /// continuity line - "That was the B-main for Stock". Wire this to
    /// CommentaryReaders.JustRan.
    ///
    /// Its own reader and not worked out from <see cref="Upcoming"/>,
    /// because the two are different questions: what is left on the
    /// programme is the programme's business, and what has just happened is
    /// the filed history's. A club runs races out of order all evening, so
    /// "the one before the next one" is not an answer.
    ///
    /// Null until wired, and then the look-back is simply not said - every
    /// running order that would have used it is one item shorter. That is
    /// the house rule: a fact the app does not hold is not said.
    /// </summary>
    public Func<RoundJustRun?>? JustRan { get; set; }

    /// <summary>
    /// The club's sponsors and the share each bought - the older reader,
    /// which says nothing about where a read is allowed. Still what
    /// <see cref="NextSponsorRead"/> asks, and what the paid reads fall
    /// back to when <see cref="SponsorReads"/> is not set.
    /// </summary>
    public Func<IReadOnlyList<SponsorSlot>>? SponsorList { get; set; }

    /// <summary>
    /// Who is sold, their paid copy, and where the club lets a read be
    /// heard - wire this to SponsorStore.ReadPlan.
    ///
    /// One reader for all of it rather than three, because all of it can
    /// change in the middle of an evening: a sponsor sold at the sign-up
    /// desk, copy corrected after the first read of it, a tick box turned
    /// off because the club decided the gaps were getting crowded. Read
    /// fresh every time a read is considered; it is a list the store
    /// already holds, so it costs nothing to ask.
    ///
    /// Null means the club has not been asked, and then the paid reads fall
    /// back to <see cref="SponsorList"/> and are allowed everywhere.
    /// </summary>
    public Func<SponsorReadPlan>? SponsorReads { get; set; }

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

    /// <summary>
    /// How much of tonight's programme has run, and how much there is -
    /// what the budget releases the night's allotment against.
    ///
    /// A reader rather than something pushed in, like the rest of them: the
    /// programme belongs to stores this class cannot reach, and a reader is
    /// one line for the host to set rather than a tick for it to remember
    /// to call. Until it is set the budget sits at its opening float all
    /// night, which starves the later segments - the whole point of the
    /// ladder is that it loosens as the programme is got through.
    /// </summary>
    public Func<(int Run, int Total)>? NightProgress { get; set; }

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

        // Guards itself on the day actually changing, like the two above
        // and for the reason the comment below spells out - this method
        // runs on a half-second tick, and a rotation reset twice a second
        // is a rotation that always hands out whatever a fresh shuffle
        // happens to start with.
        Continuity.NewMeeting(dayId);

        // The paid reads' own state, guarded on the day ACTUALLY changing.
        //
        // This method is called from the host's half-second tick rather
        // than once a night (see AnnouncerControl.FeedTheDirector), so
        // anything cleared here unguarded is cleared twice a second. The
        // pieces above each make this guard for themselves; these three
        // are fields of this class, so it is made here.
        if (!string.Equals(_dayKey, dayId ?? string.Empty, StringComparison.Ordinal))
        {
            _lastSalesRead = DateTime.MinValue;
            _adBreakOwed = false;
            _adBreaksSeen = AdBreak.BreaksEnded;

            // Inside the guard, and this is a FIX, not a tidy-up.
            //
            // These two were unconditional here, and this method runs on the
            // host's half-second tick (AnnouncerControl.FeedTheDirector,
            // whose comment believes this "returns at once for the day it is
            // already on" - the pieces above guard themselves, these did
            // not). So the two detectors were cleared twice a second:
            // BattleWatch needs about thirty seconds of a gap holding before
            // it will call a fight, and PaceWatch holds a record lap until
            // there is room to say it. Neither could ever reach that in the
            // running app - only in the tests, which call NewMeeting once.
            // Close battle and record-pace calls were therefore dead in the
            // field while passing their own tests.
            //
            // A race is still the right granularity for both: RaceStarted
            // resets them per race, which is what "a battle in a heat is a
            // fresh story in the main" means. This call only has to cover a
            // new night.
            Battles.Reset();
            Pace.Reset();
        }

        _dayKey = dayId ?? string.Empty;

        // Asked before anything is paid for: a segment whose every fact
        // has already been used tonight is dropped for free. Answered from
        // the pass's own facts where there is a pass, so this question
        // does not walk the stores again from a thread they do not belong
        // to - see NextAsync.
        Budget.NothingNewToSay = (kind, cls) => Said.NothingLeftIn(CandidatesForPass(kind, cls));
    }

    /// <summary>How much of tonight's programme has run. Called whenever the programme is refreshed.</summary>
    public void NightShape(int roundsRun, int roundsTotal) => Budget.NightShape(roundsRun, roundsTotal);

    /// <summary>When the programme was last counted - see ReadNightProgress.</summary>
    private DateTime _progressReadAt = DateTime.MinValue;

    /// <summary>How often the programme is counted. It changes once a race, so this is far more often than it needs.</summary>
    private static readonly TimeSpan ProgressEvery = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Tells the budget how far through the programme the night is.
    ///
    /// Throttled because NextAsync runs twice a second and counting the
    /// rounds walks the round store, where what it finds can only change
    /// when a race is filed - minutes apart. A reading that arrives late
    /// cannot cost anything either: the entitlement only ever ratchets up
    /// (see CommentaryBudget's high-water mark).
    /// </summary>
    private void ReadNightProgress()
    {
        if (NightProgress is null || _clock() - _progressReadAt < ProgressEvery)
        {
            return;
        }

        _progressReadAt = _clock();

        try
        {
            var (run, total) = NightProgress.Invoke();

            Budget.NightShape(run, total);
        }
        catch (Exception ex)
        {
            // A reader that throws must not stop the segments - it only
            // means the budget paces on what it last knew.
            Status?.Invoke($"[brain] Couldn't read the programme's progress: {ex.Message}");
        }
    }

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

        ReadNightProgress();

        var order = Order();

        // Every segment's facts are worked out here, before anything is
        // awaited, because this is the last point in the pass that is
        // certainly still on the thread the host called from. Every store
        // the readers walk belongs to the UI thread (RaceDayService says
        // so), and the loop below awaits - so from the second segment
        // onwards this was asking the session history for tonight's
        // qualifying on a thread-pool thread.
        //
        // It costs nothing in the usual case: a pass where nothing turns
        // out to be worth saying asked every one of these anyway.
        var pass = new Dictionary<SegmentKind, IReadOnlyList<FactBlock>>();

        foreach (var kind in order)
        {
            pass[kind] = Gather(kind);
        }

        _pass = pass;

        // Where the night's spend is written, for the same reason - see
        // SaveOn.
        var ui = SynchronizationContext.Current;

        try
        {
            foreach (var kind in order)
            {
                var said = await TryAsync(kind, ui, ct).ConfigureAwait(false);

                if (said is not null)
                {
                    return said;
                }

                if (ct.IsCancellationRequested)
                {
                    return null;
                }

                // The hold went on while that one was being written.
                // Nothing further is written until it comes off; TryAsync
                // has already given the line and its facts back.
                if (Held)
                {
                    return null;
                }
            }
        }
        finally
        {
            _pass = null;
        }

        return null;
    }

    /// <summary>
    /// The facts this pass is working from, gathered before the first
    /// await. Null between passes.
    /// </summary>
    private Dictionary<SegmentKind, IReadOnlyList<FactBlock>>? _pass;

    /// <summary>
    /// A segment's candidate facts, with a reader that throws treated as
    /// nothing to say.
    ///
    /// The rule ReadNightProgress already follows, and it matters more
    /// here: now that the whole pass's facts are gathered together, one
    /// reader catching a store mid-save would otherwise silence every
    /// other segment in the pass as well.
    /// </summary>
    private IReadOnlyList<FactBlock> Gather(SegmentKind kind)
    {
        try
        {
            return Candidates(kind, _className);
        }
        catch (Exception ex)
        {
            Say($"[brain] Couldn't work out what to say about {CommentaryBudget.Spec(kind).Title.ToLowerInvariant()}: {ex.Message}");

            return Array.Empty<FactBlock>();
        }
    }

    /// <summary>
    /// This pass's facts for a segment, or freshly worked-out ones when
    /// there is no pass in hand - which is the budget asking its own
    /// question from somewhere outside NextAsync.
    /// </summary>
    private IReadOnlyList<FactBlock> CandidatesForPass(SegmentKind kind, string? className)
    {
        if (_pass is { } pass
            && string.Equals(className, _className, StringComparison.Ordinal)
            && pass.TryGetValue(kind, out var gathered))
        {
            return gathered;
        }

        return Candidates(kind, className);
    }

    /// <summary>
    /// Writes the night's spend on the thread the pass started on.
    ///
    /// AppSettings belongs to the UI thread like every other store here,
    /// and from the second segment of a pass onwards this code is on a
    /// thread-pool thread - where the write was a race against whatever
    /// else was saving settings, and the spend it was recording could be
    /// lost without a word. No context (a test, or a host that has none)
    /// means there is nowhere to post to, so it is written here.
    /// </summary>
    private void SaveOn(SynchronizationContext? ui)
    {
        if (ui is null || ui == SynchronizationContext.Current)
        {
            Save();
            return;
        }

        ui.Post(_ => Save(), null);
    }

    // ------------------------------------------------------------------
    // Continuity
    //
    // WHY THE FACTS ARE GATHERED HERE AND NOT WHERE THE BLOCK IS BUILT
    //
    // Both readers walk a store that belongs to the UI thread, both can be
    // null, and either can throw if it catches a store mid-save. That is
    // exactly the shape Gather and ReadNightProgress already deal with in
    // this class, and the rule they follow - a reader that throws is
    // treated as nothing to say - is one worth having in ONE place rather
    // than copied onto the announcer screen. Costs nothing: it is
    // arithmetic over stores this laptop already has open.
    //
    // NOTHING HERE IS WRITTEN, RESERVED OR SPENT. No model is asked and no
    // ticket is taken, because a continuity line is built by concatenating
    // facts and that is the entire point of it - see CommentaryContinuity.
    // ------------------------------------------------------------------

    /// <summary>
    /// What the night around a block looks like right now: what has just
    /// run, what is still to come, and roughly how long until it.
    ///
    /// Asked on the host's own thread, before anything is awaited, and
    /// handed to the running order - see ContinuityFacts. An unwired
    /// reader, or one that throws, comes back as "nothing known", and then
    /// the bridges that would have used it are not said.
    /// </summary>
    internal ContinuityFacts ContinuityNow()
    {
        RoundJustRun? ran = null;
        IReadOnlyList<UpcomingRound> rounds = Array.Empty<UpcomingRound>();
        TimeSpan? inAbout = null;

        try
        {
            ran = JustRan?.Invoke();
        }
        catch (Exception ex)
        {
            Say($"[brain] Couldn't work out what has just run: {ex.Message}");
        }

        try
        {
            if (Upcoming?.Invoke() is { } next)
            {
                inAbout = next.InAbout;

                // A property pattern rather than a null check, so a host
                // that hands back no list at all - or an empty one - is a
                // programme with nothing on it rather than a throw over
                // the PA. The tuple says the list cannot be null; a Func
                // set by somebody else is not bound by that.
                if (next.Rounds is { Count: > 0 })
                {
                    rounds = next.Rounds;
                }
            }
        }
        catch (Exception ex)
        {
            Say($"[brain] Couldn't read what is still to come: {ex.Message}");
        }

        return new ContinuityFacts(ran, rounds, inAbout);
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

    // ------------------------------------------------------------------
    // The paid reads
    //
    // WHERE A SALES READ GOES, AND WHY THERE
    //
    // A sales read is a few sentences a sponsor paid to have spoken. It is
    // the single most interruptible thing in the app: a sponsor would far
    // rather be read at the next gap than over the top of a flag call, and
    // unlike a battle call it does not go off - the copy is just as true in
    // ten minutes.
    //
    // So there are exactly two places it is offered, and both are places
    // where nothing else wants the air:
    //
    //   1. THE TAIL OF AN AD BREAK. The sponsor's own video has just
    //      stopped and the screen is going back to the standings. Reading
    //      over the video would be two voices at once - its voiceover and
    //      ours - which is the one thing the ad break was built to prevent
    //      (see AdBreak). Reading before it starts means talking over the
    //      opening seconds of what the club was paid for. The tail is the
    //      only part of a break with nothing in it, and "that was Clarke
    //      and Sons" is the one moment of the evening the crowd has
    //      already got the name in front of them.
    //
    //   2. THE BETWEEN-RACES GAP, AFTER THE SEGMENT LADDER HAS DECLINED.
    //      NextAsync is asked first and these second, so a paid read never
    //      pushes a race recap or a what's-coming-next out of the way - it
    //      fills the quiet those leave. That is also why it does not need
    //      to be on the budget's ladder at all: by the time it is asked,
    //      the ladder has already had its turn and said no.
    //
    // NOTHING HERE WRITES ANYTHING. No model is asked, no budget is
    // reserved, nothing is settled or released. The copy was written by a
    // person and is read as typed, so all of this works with the AI layer
    // switched off entirely - which is deliberate, because a club that
    // sold a read has to get it whatever the month's allowance is doing.
    // ------------------------------------------------------------------

    /// <summary>
    /// How long after a paid read before another one may go out.
    ///
    /// Not a budget - it is airtime. A club with one sponsor has a gap
    /// between every race and would otherwise hear the same advert in all
    /// of them, which is the fastest way to make a crowd stop hearing it.
    /// Ninety seconds is longer than the longest read and shorter than a
    /// heat, so a sponsor gets one bite per gap.
    /// </summary>
    private static readonly TimeSpan QuietAfterRead = TimeSpan.FromSeconds(90);

    /// <summary>Breaks this director has already noticed ending - see NextAdBreakRead.</summary>
    private int _adBreaksSeen;

    /// <summary>True when a break has ended and its read has not gone out yet.</summary>
    private bool _adBreakOwed;

    /// <summary>When the last paid read went out. See QuietAfterRead.</summary>
    private DateTime _lastSalesRead = DateTime.MinValue;

    /// <summary>
    /// The paid read an ad break has just earned, or null - which is almost
    /// always.
    ///
    /// Asked once a tick. The break is noticed by comparing AdBreak's count
    /// of finished breaks rather than by subscribing to it, for the reasons
    /// given there.
    ///
    /// A break that ends while a race is running does NOT lose its read:
    /// the debt is kept and paid at the next real gap. That is the whole
    /// point of a sales read - it is the one thing in here worth deferring
    /// rather than dropping. Anything else that stops it - the club does
    /// not want ad-break reads, nobody is sold, no copy would fit - settles
    /// the debt, so a club that switches the reads on an hour later does
    /// not suddenly hear one for a break that ran before tea.
    /// </summary>
    public SponsorRead? NextAdBreakRead(TimeSpan gap)
    {
        var ended = AdBreak.BreaksEnded;

        if (ended != _adBreaksSeen)
        {
            _adBreaksSeen = ended;
            _adBreakOwed = true;
        }

        if (!_adBreakOwed)
        {
            return null;
        }

        // A race call always wins, and a line being written is about to be
        // one. The read waits.
        if (_racing || Held || Writing)
        {
            return null;
        }

        _adBreakOwed = false;

        return SalesRead(SponsorPlacement.AdBreaks, SponsorMoment.AfterTheAdvert, gap);
    }

    /// <summary>
    /// The paid read this between-races gap has room for, or null.
    ///
    /// Asked AFTER NextAsync has come back with nothing, which is what
    /// makes this the quiet the segments left rather than something
    /// competing with them.
    /// </summary>
    public SponsorRead? NextSegmentRead(TimeSpan gap)
    {
        if (_racing || Held || Writing)
        {
            return null;
        }

        return SalesRead(SponsorPlacement.Segments, SponsorMoment.BetweenRaces, gap);
    }

    /// <summary>
    /// A paid read has actually been spoken.
    ///
    /// Must be called after the line has gone to the announcer and never
    /// before, for the reason SponsorRotation gives: the count is what the
    /// club shows the sponsor at the end of the night, and where a long
    /// copy got to is only known from the read that really went out.
    /// </summary>
    public void SalesReadSaid(SponsorRead? read)
    {
        if (read is null)
        {
            return;
        }

        Sponsors.Said(read);

        _lastSalesRead = _clock();

        Say(read.MoreToCome
            ? $"[sponsor] Read for {read.SponsorId} - more of their copy to come in the next gap."
            : $"[sponsor] Read for {read.SponsorId}.");
    }

    /// <summary>
    /// Whose turn it is and what to say, where the club allows a read at
    /// all and the night is not still ringing with the last one.
    /// </summary>
    private SponsorRead? SalesRead(SponsorPlacement where, SponsorMoment moment, TimeSpan gap)
    {
        if (Plan() is not { } plan || (plan.Placement & where) == 0)
        {
            return null;
        }

        if (_clock() - _lastSalesRead < QuietAfterRead)
        {
            return null;
        }

        return Sponsors.Next(plan.Sponsors, moment, gap);
    }

    /// <summary>
    /// The club's sponsors and where they may be heard, or null when
    /// nobody is sold.
    ///
    /// A reader that throws is treated as nobody sold, the rule
    /// ReadNightProgress and Gather already follow: a store caught
    /// mid-save must not take the announcer down, and it certainly must
    /// not do it over a paid read.
    /// </summary>
    private SponsorReadPlan? Plan()
    {
        try
        {
            if (SponsorReads?.Invoke() is { } plan)
            {
                return plan.Sponsors.Count == 0 ? null : plan;
            }

            var list = SponsorList?.Invoke();

            // The older reader says nothing about placement, so a host that
            // only wired that one gets reads wherever they fit - which is
            // what it meant before there was anywhere to choose.
            return list is null or { Count: 0 }
                ? null
                : new SponsorReadPlan(list, SponsorPlacement.Everywhere);
        }
        catch (Exception ex)
        {
            Say($"[sponsor] Couldn't read the club's sponsors: {ex.Message}");

            return null;
        }
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

    private async Task<SpokenSegment?> TryAsync(SegmentKind kind, SynchronizationContext? ui, CancellationToken ct)
    {
        var spec = CommentaryBudget.Spec(kind);
        var blocks = CandidatesForPass(kind, _className);

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

            // Held while it was being written. Nothing is lost by
            // holding, which is what Held promises: the line is dropped,
            // the budget goes back, and the facts are NOT marked - before
            // this they were, so a hold turned on mid-write took that
            // fight, or the cold open, off the night for good.
            if (Held)
            {
                Budget.Release(verdict.Ticket);
                Say($"[brain] Dropped {spec.Title.ToLowerInvariant()} - the segments were held while it was being written.");
                return null;
            }

            Budget.Settle(verdict.Ticket);
            Said.Mark(pack.Keys);
            Spent(kind);
            SaveOn(ui);

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
                // Kept, because Spent has to cool THIS fight. It used to
                // ask Pick() again afterwards, and with two fights live
                // the second answer can be the other one - so the
                // cooldown landed on a fight nobody had mentioned while
                // the one that was just called stayed free to be called
                // again.
                _calling = Battles.Pick();

                return _calling is { } battle
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

    /// <summary>The fight this pass's battle facts were built from. See Candidates.</summary>
    private Battle? _calling;

    /// <summary>What a segment has to do once its line has actually been said.</summary>
    private void Spent(SegmentKind kind)
    {
        switch (kind)
        {
            case SegmentKind.BattleWatch:
            {
                // Left alone for the cooldown, so two cars nose to tail
                // for a whole heat are one story and not twenty. The one
                // that was actually announced, not whatever Pick would
                // answer now.
                if (_calling is { } battle)
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
