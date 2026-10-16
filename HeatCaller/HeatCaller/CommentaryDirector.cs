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
/// bookends need almost nothing; the history, form, qualifying,
/// championship, halfway-standings and this-day segments each need a real
/// reader behind them, and until one is set they cost nothing and say
/// nothing. That is deliberate - a half-fed segment that invents the other
/// half is worse than one that stays quiet.
///
/// A SEGMENT WITH NO READER AT ALL IS A DIFFERENT FAULT, and three of them
/// shipped that way until 10 Oct 2026: the form guide, the halfway
/// standings and "this day at the club" each had a title, a description, a
/// tier, a budget and a tick box on the Commentary page, and each fell
/// through Candidates' default - so a club that ticked one got silence for
/// the rest of its life and nothing ever said why. A box that does nothing
/// is worse than no box. Every member of SegmentKind now either has a case
/// below or is written somewhere else on purpose, and the default says
/// which.
/// </summary>
public sealed class CommentaryDirector
{
    private readonly CommentaryService _commentary;
    private readonly AppSettings _settings;
    private readonly Func<DateTime> _clock;

    private string _raceKey = string.Empty;
    private string? _className;
    private bool _racing;

    /// <summary>
    /// Whether the gap a paid read may go into is open: from a race's flag
    /// until the next race is on its way (staged, or On Air pressed for it).
    /// Shut until the night's first race has finished. See ReadsBelongHere.
    /// </summary>
    private bool _afterARace;

    /// <summary>BroadcastImaging.OnAirsBegun when the last flag fell - a different count means On Air has been pressed since.</summary>
    private int _onAirsAtFlag;

    /// <summary>CommercialBreak.BreaksBegun when the last flag fell - a different count means a stop set has run since.</summary>
    private int _breaksAtFlag;

    /// <summary>
    /// The last reason each segment was refused, so the refusal is said
    /// once instead of twice a second.
    ///
    /// TryAsync runs on the host's half-second tick for every segment that
    /// has anything to say, and a segment that is out of budget is refused
    /// on every one of them. It used to log every refusal: rehearsal 5
    /// (10 Oct 2026) produced four hundred copies of "Skipped grid history
    /// - 6 lines in the last ten minutes already." in twelve minutes, which
    /// buried the race's own log so thoroughly that the audio fault the
    /// night was recorded to find had to be dug out of it line by line. A
    /// log nobody can read is a log that is not there.
    ///
    /// Keyed on the reason, not just the segment, so a refusal that changes
    /// - the count going up, the night's budget running out on top of the
    /// ten-minute rule - is still said. The same reason twice in a row is
    /// the only thing held back.
    /// </summary>
    private readonly Dictionary<SegmentKind, string> _refusalSaid = new();

    public CommentaryDirector(CommentaryService commentary, AppSettings settings, Func<DateTime>? clock = null)
    {
        _commentary = commentary;
        _settings = settings;
        _clock = clock ?? (() => DateTime.Now);

        Budget = new CommentaryBudget(_clock);
        Battles = new BattleWatch(_clock);
        Pace = new PaceWatch();
        Horizons = new RaceHorizon(_clock);
        Said = new SaidTonight();
        Sponsors = new SponsorRotation();
        Continuity = new ContinuityVoice();

        // Written down as each line is heard, not only after a paid
        // segment: continuity is what still runs with the AI off. Only its
        // own half - this is the UI thread, mid running order, and the
        // budget is Save's business after a segment.
        Continuity.LineHeard = () =>
        {
            _settings.CommentaryContinuityNight = Continuity.Save();
            _settings.Save();
        };

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

    /// <summary>
    /// Watches what each gap is DOING and how much road is left - the
    /// forward-looking half of the live commentary, which until 15 Oct
    /// 2026 was built and fed by nothing. See RaceHorizon, and the
    /// Horizon segment it feeds.
    /// </summary>
    public RaceHorizon Horizons { get; }

    /// <summary>What has been said tonight, so nothing is said twice.</summary>
    public SaidTonight Said { get; }

    /// <summary>Whose turn it is for a sponsor read. Not on the budget's ladder - see SponsorRotation.</summary>
    public SponsorRotation Sponsors { get; }

    /// <summary>
    /// What the night came to: every segment weighed up, what became of it,
    /// and the facts the last one that went out was built from.
    ///
    /// INTERNAL because NightAccount is - a public property on a public
    /// class may not name an internal type, which is CS0053 and has landed
    /// four times on this project in a week. Everything that reads it is in
    /// this assembly.
    /// </summary>
    internal NightAccount Account { get; } = new();

    /// <summary>
    /// What the broadcast has pointed the room at tonight, so a later
    /// segment can come back to it. See NightPromises, which states at
    /// length what a promise is allowed to be.
    ///
    /// Internal, with the reader below, for the reason Account is: the
    /// type is internal, and a public member may not name one.
    /// </summary>
    internal NightPromises Promises { get; } = new();

    /// <summary>
    /// Tonight's moments, strongest first - what a promise is paid off
    /// with. Wire this to NightMoments.Over over the race day's own
    /// sessions.
    ///
    /// A READER, like every other source in this class, and here that is
    /// the whole of what makes the callback honest: the moments are
    /// arithmetic over the club's saved results, so by the time a promise
    /// is weighed up the reader has seen the race that has since been
    /// filed. Held between passes it would go on offering a callback about
    /// a result the club had since corrected, and would never see the race
    /// that is the whole reason there is anything to come back to.
    ///
    /// Null until wired, and then nothing is ever come back to, which is
    /// how the show read before any of this.
    /// </summary>
    internal Func<IReadOnlyList<NightMoments.Moment>>? MomentsTonight { get; set; }

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

    /// <summary>
    /// True when the host tells this class whether each segment it was
    /// handed actually went out - <see cref="Spoken"/> or
    /// <see cref="Dropped"/> - and the segment is only settled when it was
    /// (14 Oct 2026).
    ///
    /// Off, a segment is settled the moment it is written, which is how
    /// this class always behaved and what a caller that never reports back
    /// needs. On, the budget's spend, the facts, the club note or room vote
    /// marked said, the promises and Repeat's line all wait for Spoken, and
    /// a Dropped segment gives every one of them back - so a birthday
    /// dedication written and then dropped because the next race was called
    /// is still waiting to be said, not quietly spent.
    /// </summary>
    public bool CommitWhenSpoken { get; set; }

    /// <summary>
    /// The host said this segment: settle it - spend, facts, marks,
    /// promises, Repeat. Does nothing for a segment that is not waiting
    /// (already settled, already dropped, or never this class's).
    /// </summary>
    public void Spoken(SpokenSegment? segment)
    {
        if (TakeAwaiting(segment) is not { } waiting)
        {
            return;
        }

        try
        {
            waiting.Commit();
        }
        catch (Exception ex)
        {
            Say($"[brain] Couldn't settle a spoken segment: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// The host dropped this segment unheard: give back its budget and
    /// leave its facts and marks unspent, exactly as a segment dropped
    /// inside TryAsync is. Safe to call for a segment already settled or
    /// dropped - it does nothing.
    /// </summary>
    public void Dropped(SpokenSegment? segment)
    {
        if (TakeAwaiting(segment) is not { } waiting)
        {
            return;
        }

        Budget.Release(waiting.Ticket);
        Account.DroppedAfterWriting();

        // Nobody heard it, so it is not the line Repeat offers.
        _commentary.ForgetLine(segment!.Text);
    }

    /// <summary>A segment written and waiting for the host's word. See CommitWhenSpoken.</summary>
    private sealed record AwaitingWord(Action Commit, long Ticket, IReadOnlyList<string> Keys, DateTime WrittenAt);

    /// <summary>
    /// How long a written segment waits for Spoken or Dropped before it is
    /// taken as dropped. Longer than any show holds the floor (see
    /// NightRecap.LongestHold), so it only ever catches a path that wrote a
    /// line and then lost it without a word - a programme whose colour came
    /// back after it had stopped waiting, say.
    /// </summary>
    private static readonly TimeSpan AwaitingWordCeiling = TimeSpan.FromMinutes(15);

    /// <summary>Guards _awaiting: written on the pass's thread, answered on the host's.</summary>
    private readonly object _awaitingGate = new();

    /// <summary>
    /// Segments handed to the host and not yet answered for. By reference:
    /// SpokenSegment is a record, and two lines can read the same.
    /// </summary>
    private readonly Dictionary<SpokenSegment, AwaitingWord> _awaiting = new(ReferenceEqualityComparer.Instance);

    private AwaitingWord? TakeAwaiting(SpokenSegment? segment)
    {
        if (segment is null)
        {
            return null;
        }

        lock (_awaitingGate)
        {
            return _awaiting.Remove(segment, out var waiting) ? waiting : null;
        }
    }

    /// <summary>The fact keys of every segment still waiting to be said.</summary>
    private HashSet<string> KeysAwaiting()
    {
        lock (_awaitingGate)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var waiting in _awaiting.Values)
            {
                keys.UnionWith(waiting.Keys);
            }

            return keys;
        }
    }

    /// <summary>Drops every segment that has waited past AwaitingWordCeiling.</summary>
    private void DropStale()
    {
        List<SpokenSegment> stale;
        var now = _clock();

        lock (_awaitingGate)
        {
            if (_awaiting.Count == 0)
            {
                return;
            }

            stale = _awaiting
                .Where(pair => now - pair.Value.WrittenAt > AwaitingWordCeiling)
                .Select(pair => pair.Key)
                .ToList();
        }

        foreach (var segment in stale)
        {
            Dropped(segment);
        }
    }

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

    /// <summary>
    /// What the crowd screen should show while one sponsor's read goes
    /// out - wire this to SponsorStore.OnScreenFor.
    ///
    /// Asked by the ONE place a paid read is spoken, at the moment it is
    /// spoken, rather than carried on the read itself: a
    /// <see cref="SponsorRead"/> is planned ahead of the gap it lands in
    /// and a picture chosen minutes earlier could since have been removed.
    /// See SponsorOnAir.
    ///
    /// Null at a club on an older wiring, and then a read goes out over
    /// the standings exactly as it always did.
    /// </summary>
    public Func<string, SponsorOnScreen?>? SponsorOnScreenFor { get; set; }

    /// <summary>The evening's shape, for the cold open.</summary>
    public Func<NightAhead>? NightAhead { get; set; }

    /// <summary>The evening's numbers, for the sign-off.</summary>
    public Func<NightDone>? NightDone { get; set; }

    /// <summary>
    /// THE NIGHT'S THREAD - the one storyline the evening is being told
    /// as. Wire this to CommentaryReaders.NightStory.
    ///
    /// Read fresh on every pass like the rest of them, and for this one
    /// that is not a convenience, it is the feature. A thread is a framing
    /// of what the club's results actually say, so asking again is what
    /// makes a thread the night has contradicted impossible to offer: by
    /// the time the next segment is weighed up the reader has seen the heat
    /// that beat the subject. Held between passes it would be the "can
    /// anyone stop Dave" that keeps asking after Dave went out, which is
    /// the one failure a room notices instantly and laughs at. See
    /// NightThread.
    ///
    /// Null until wired, and then both bookends read exactly as they did
    /// before there was any of this.
    /// </summary>
    public Func<NightThread?>? NightStory { get; set; }

    /// <summary>The grid as the club's history knows them, and the pairs with previous.</summary>
    public Func<(IReadOnlyList<DriverForm> Grid, IReadOnlyList<HeadToHead> Rivalries)>? GridHistory { get; set; }

    /// <summary>
    /// The shape of this grid's last few race NIGHTS in the class - a run
    /// of podium nights, perfect attendance, a clean run of finishes, a
    /// driver back after a while away. Wire this to
    /// CommentaryReaders.RecentWeeks.
    ///
    /// A READER OF ITS OWN rather than something worked out from
    /// <see cref="GridHistory"/>, and that is the point rather than an
    /// oversight. GridHistory counts a driver's recent PLACINGS and the
    /// form guide reads them out in order; this counts NIGHTS, which needs
    /// the dates and the nights a driver was absent from - neither of
    /// which DriverForm carries. See CommentaryWeekFacts for why the unit
    /// is what makes the difference.
    ///
    /// Null until wired, and then the form guide reads exactly as it did
    /// before there was any of this.
    /// </summary>
    public Func<IReadOnlyList<DriverWeeks>>? RecentWeeks { get; set; }

    /// <summary>A class's qualifying, once it is done.</summary>
    public Func<IReadOnlyList<QualifyingLine>>? Qualifying { get; set; }

    /// <summary>
    /// Where the class on track stands partway through its heats. Wire
    /// this to CommentaryReaders.MidMeeting.
    ///
    /// Its own reader rather than something worked out from
    /// <see cref="Qualifying"/>, although both end up asking the same
    /// calculator: the halfway standings also need to know how many of the
    /// class's heats have RUN, which is the programme's business and not
    /// qualifying's, and the whole segment hangs on that figure. Null
    /// until wired, and then the segment stays quiet - see the summary.
    /// </summary>
    public Func<MeetingSoFar?>? MidMeeting { get; set; }

    /// <summary>
    /// What this club did on this date in other years. Wire this to
    /// CommentaryReaders.OnThisDay.
    ///
    /// Empty is the usual answer and the right one: a club in its first
    /// year has no anniversary, and nothing here invents one.
    /// </summary>
    public Func<IReadOnlyList<ClubRaceDay>>? OnThisDay { get; set; }

    /// <summary>
    /// The next thing about the NIGHT the club wants mentioned - a
    /// dedication, a birthday, the raffle - or null, which is the answer
    /// on almost every tick of almost every evening.
    ///
    /// THE ONLY FACT IN HERE NOBODY WORKED OUT. Every other reader on this
    /// class walks the timing, the history or the programme. This one
    /// reads a sentence a human typed because they know something the
    /// decoder cannot, and it is the one the room repeats afterwards.
    ///
    /// Only ever hands over an APPROVED note - that is ClubNotes.ToSay's
    /// promise, and the reason the gate lives in the store rather than
    /// here: there is no route into this director that can carry an
    /// unapproved one.
    ///
    /// Null until wired, and then the segment stays quiet.
    /// </summary>
    /// <remarks>
    /// INTERNAL, where almost every other hook on this class is public,
    /// because <see cref="ClubNote"/> is - a public property carrying an
    /// internal type is CS0053. Everything that sets this is in the app.
    /// </remarks>
    internal Func<ClubNote?>? ClubAside { get; set; }

    /// <summary>
    /// What the room voted would happen in the race just run, and who
    /// actually won it - see <see cref="RoomVote"/>, which answers null
    /// on a thin vote, a split room or a vote about a different race, so
    /// anything arriving here is already worth saying.
    /// </summary>
    /// <remarks>Internal for the same CS0053 reason as <see cref="ClubAside"/>.</remarks>
    internal Func<RoomsVerdict?>? RoomsCall { get; set; }

    /// <summary>
    /// Called with the race whose verdict has just been SPOKEN, so the
    /// vote is marked said and the room is never told twice. The mirror of
    /// <see cref="ClubAsideSaid"/>, and settled at the same instant and
    /// for the same reason.
    /// </summary>
    internal Action<string>? RoomsCallSaid { get; set; }

    /// <summary>
    /// Told that a note has actually gone out, so the store can mark it
    /// said - see <see cref="ClubAsideSaid"/>.
    ///
    /// A delegate rather than the store itself, for the reason every other
    /// reader here is one: this class walks no stores and holds none, and
    /// a director that owned the notes file would be a director that had
    /// to be told about a race day changing.
    /// </summary>
    public Action<string>? ClubAsideSaid { get; set; }

    /// <summary>
    /// Which note went into the facts of the segment being written, so it
    /// can be marked said if and only if that segment survives.
    ///
    /// Cleared on every pass. A note put into a segment that was then
    /// dropped - held, too slow, refused - must stay unsaid: a dedication
    /// marked said and never spoken is one the club promised somebody and
    /// nobody heard, which is worse than one said a minute late.
    /// </summary>
    /// <summary>
    /// What each kind would mark as SAID if its segment survives this
    /// pass - a club note's id, a room vote's race.
    ///
    /// PER KIND, and that is the whole point. A single field could not
    /// work once NextAsync gathered the whole pass up front: every kind's
    /// Candidates ran before any kind was tried, so the last one to record
    /// a mark would have overwritten the rest. Keyed by kind, each
    /// segment reads its own.
    ///
    /// Written during the gather (UI thread), read during the awaited loop
    /// (a pool thread), and cleared only at the top of the next gather -
    /// so it is never mutated while it is being read. Same discipline as
    /// _pass beside it.
    ///
    /// A kind whose segment is dropped simply never reads its mark, and
    /// the next gather clears it. That is the promise the old single field
    /// was written for and could not keep: a dedication that went into a
    /// segment which was then dropped stays unsaid.
    /// </summary>
    private Dictionary<SegmentKind, string> _marks = new();

    /// <summary>
    /// Records what this kind marks said if it survives - see
    /// <see cref="_marks"/>.
    /// </summary>
    private void MarkWhenSpoken(SegmentKind kind, string? what)
    {
        if (what is { Length: > 0 } mark)
        {
            _marks[kind] = mark;
        }
    }

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
        // The nights still to come this month, not the month's whole count -
        // which never counted down, so the last night was handed a quarter
        // of what was left. See CommentaryBudget.NightsLeft.
        Budget.NightsLeftThisMonth = CommentaryBudget.NightsLeft(_settings.CommentaryNightsPerMonth, _clock());
        Budget.KeepEverythingRunning = _settings.CommentaryKeepEverything;

        // RESTORED, NOT RESET (13 Oct 2026). This was NewMeeting, and
        // SaidTonight.Restore - which calls NewMeeting itself, and only
        // picks facts back up when the day matches - was written at the
        // same time as Budget.Restore above and then called by nobody. So
        // the spend survived a restart and the facts did not, and a club
        // that reopened the app between the heats and the mains heard
        // every record and storyline over again.
        Said.Restore(_settings.CommentaryFactsNight, dayId);

        // And the sponsors' reads, for the same reason and with the same
        // miss behind it: SponsorRotation.Restore existed and nobody called
        // it, so a restart wiped the counts on the club's sponsor report.
        // Saved after every read (SalesReadSaid), so laying it back over
        // the night on every tick puts back exactly what is already here.
        Sponsors.Restore(_settings.SponsorRotationNight, dayId);

        // Guards itself on the day actually changing, like the two above.
        Account.NewMeeting(dayId);

        // And the same, for the same reason. Last week's promises were
        // paid off or were not, and either way nothing is coming back to
        // them - see NightPromises.
        Promises.NewMeeting(dayId);

        // Guards itself on the day actually changing, like the two above
        // and for the reason the comment below spells out - this method
        // runs on a half-second tick, and a rotation reset twice a second
        // is a rotation that always hands out whatever a fresh shuffle
        // happens to start with. Restored for the same day, like Said
        // above - see ContinuityVoice.Restore.
        Continuity.Restore(_settings.CommentaryContinuityNight, dayId);

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
            _refusalSaid.Clear();

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
            Horizons.Reset();
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
    /// How big the race in front of the announcer is - see
    /// <see cref="RaceStature"/>. Told on the tick, because the round under
    /// the announcer changes while the app runs.
    ///
    /// Kept here as well as on the budget because the DIRECTOR needs it
    /// too: the budget decides how much may be said, and this decides
    /// whether what is said is worth two voices. See WriteAsync.
    /// </summary>
    public RaceStature Stature
    {
        get => Budget.Stature;
        set => Budget.Stature = value;
    }

    /// <summary>
    /// How much the club wants said - see <see cref="ShowChatter"/>. Held
    /// on the budget, which is what the ceiling is read from, and exposed
    /// here because the director needs it too: the dial decides whether the
    /// second voice comes out below the mains. See WriteAsync.
    /// </summary>
    public string Chatter
    {
        get => Budget.Chatter;
        set => Budget.Chatter = value;
    }

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
        _afterARace = false;

        Battles.Reset();
        Pace.Reset();
        Horizons.Reset();
    }

    /// <summary>The flag is out. Nothing in-race is worth saying from here.</summary>
    public void RaceFinished()
    {
        _racing = false;
        _afterARace = true;
        _onAirsAtFlag = BroadcastImaging.OnAirsBegun;
        _breaksAtFlag = CommercialBreak.BreaksBegun;
    }

    /// <summary>
    /// The next race is on its way - staged or counting down. The gap
    /// after the last race is over, so no paid read goes out until the
    /// next flag. See ReadsBelongHere.
    /// </summary>
    public void RaceComing() => _afterARace = false;

    /// <summary>
    /// Whether this is the gap AFTER a race, the only place a paid read
    /// goes (16 Oct 2026).
    ///
    /// The club heard Ralph's Tavern read out while On Air was getting the
    /// next race's opening ready - the copy landed in the quiet before the
    /// cold open, then the jingle came in on top of the tail of it. The top
    /// of a race is the programme's own: the jingle, the bed and the
    /// opening are what the room is waiting for, and an advert in front of
    /// them is an advert nobody wanted. So a read is owed to the gap after
    /// a flag and nowhere else: not before the night's first race, not once
    /// On Air has been pressed, not while the next race is staged. A read
    /// that does not fit is not lost - it waits for the next race's flag.
    /// </summary>
    private bool ReadsBelongHere => _afterARace && BroadcastImaging.OnAirsBegun == _onAirsAtFlag;

    /// <summary>
    /// Whether a stop set has run since the last flag. When one has, the
    /// sponsor's copy went out INSIDE it (16 Oct 2026 - see NextBreakRead),
    /// so the gap-filling reads stand down until the next race: the club
    /// heard the same copy again at the end of the recap, outside the
    /// break, which is the read it had already had.
    /// </summary>
    private bool StopSetHasRun => CommercialBreak.BreaksBegun != _breaksAtFlag;

    /// <summary>
    /// The paid read for the stop set itself, or null (16 Oct 2026).
    ///
    /// Asked by the commercial break once the video advert has finished
    /// (RaceControlControl.ReadTheSponsorsInTheBreakAsync), so the copy is
    /// read IN the break - after the video, with the sponsor on the screen
    /// - rather than in whatever quiet came after the recap. None of the
    /// show-is-on gates the gap reads have: the break IS the slot, and the
    /// break itself makes sure no video is playing under it. A hold still
    /// stops it, and so does anything other than the gap after a race.
    ///
    /// Settles the ad-break debt as it goes - the read this is is the one
    /// that debt was for.
    /// </summary>
    public SponsorRead? NextBreakRead(TimeSpan gap)
    {
        if (_racing || !ReadsBelongHere || Held)
        {
            return null;
        }

        _adBreaksSeen = AdBreak.BreaksEnded;
        _adBreakOwed = false;

        return SalesRead(SponsorPlacement.AdBreaks, SponsorMoment.AfterTheAdvert, gap)
               ?? SalesRead(SponsorPlacement.Segments, SponsorMoment.BetweenRaces, gap);
    }

    /// <summary>One tick of the running order - every adjacent pair, as the race stands. See BattleWatch.</summary>
    public void Standings(IReadOnlyList<PairGap> pairs) => Standings(pairs, 0, null);

    /// <summary>
    /// One tick of the running order, with what the horizon needs on top:
    /// the leader's lap count and how much road is left.
    ///
    /// THE SAME PAIRS GO TO BOTH, on purpose. BattleWatch and RaceHorizon
    /// read one tick feed so they can never disagree about who is in front
    /// - see RaceHorizon's own note on it.
    /// </summary>
    /// <param name="leaderLaps">The leader's completed laps - the clock the horizon's rate is measured against.</param>
    /// <param name="road">
    /// Laps left and whether that is known or worked out - see
    /// RaceHorizon.LapsLeftIn. Null when the race has neither a lap limit
    /// nor a clock to estimate from, and then the horizon is told there is
    /// no road and says nothing.
    /// </param>
    public void Standings(IReadOnlyList<PairGap> pairs, int leaderLaps, RoadLeft? road)
    {
        if (!_racing)
        {
            return;
        }

        Battles.Observe(pairs);

        Horizons.Observe(pairs, leaderLaps, road?.Laps ?? 0, road?.Estimated ?? false);
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
            // Not over a bigger one still waiting to be said. PaceWatch
            // gives each lap its biggest true thing, but this is a single
            // slot: without the check, somebody's first personal best a
            // lap later replaced the fastest lap of the race and that was
            // never called. A pending note past its deadline is dropped by
            // Candidates anyway, so it does not block a fresh one.
            if (_pending is { } waiting
                && note.Kind < waiting.Kind
                && _clock() - _pendingAt <= CommentaryBudget.Spec(SegmentKind.PaceNote).NeedsBy)
            {
                return;
            }

            _pending = note;
            _pendingAt = _clock();
        }
    }

    private PaceNote? _pending;
    private DateTime _pendingAt;

    /// <summary>
    /// The note the pass is writing, so Spent clears that one and not a
    /// newer record that came in during the write - the same reason
    /// BattleWatch keeps _calling.
    /// </summary>
    private PaceNote? _paceCalling;

    /// <summary>
    /// Tonight's spend and tonight's facts, for AppSettings to hold across
    /// a restart.
    ///
    /// AND IT NOW HOLDS BOTH. This comment said "and tonight's facts" for
    /// a month while saving only the spend, which is a worse kind of wrong
    /// than saying nothing - anyone reading it would have stopped looking.
    ///
    /// Called from SaveOn after every segment that was actually SPOKEN,
    /// not on the way out: the restart this exists for is usually not a
    /// planned one, and a club that force-closes a frozen window is
    /// exactly the club that must not hear the evening twice.
    /// </summary>
    public void Save()
    {
        _settings.CommentaryBudgetNight = Budget.Save();
        _settings.CommentaryFactsNight = Said.Save();
        _settings.SponsorRotationNight = Sponsors.Save();
        _settings.CommentaryContinuityNight = Continuity.Save();
        _settings.Save();
    }

    // ------------------------------------------------------------------
    // The pull
    // ------------------------------------------------------------------

    // NextAsync's own summary used to be here, stranded above this field
    // by an edit that moved the method and left the comment behind - one
    // of the four orphans the 12 Oct bug hunt recorded. A doc comment
    // describing "the next thing worth saying" sitting on a bool is worse
    // than none: a reader takes it for the field's own and stops looking.
    // It has gone down onto NextAsync, which had no summary at all.

    /// <summary>
    /// Whether this pass is being asked for by a PROGRAMME rather than as
    /// filler between races - see ForTheProgrammeAsync, and
    /// CommentaryBudget.ReserveForProgramme for the one gate it lifts.
    /// </summary>
    private bool _forTheProgramme;

    /// <summary>
    /// Held for the length of a pass, so there is only ever one - see
    /// ForTheProgrammeAsync, which waits for it, and NextAsync, which gives
    /// way to it.
    /// </summary>
    private readonly SemaphoreSlim _onePass = new(1, 1);

    /// <summary>
    /// One segment for the post-race programme to speak INSIDE its own
    /// show, rather than as filler between races.
    ///
    /// The same ladder, the same facts and the same budget as
    /// <see cref="NextAsync"/> - the only difference is that the minimum
    /// gap since the last spoken line does not apply, because a programme
    /// is a run of segments back to back by design and its own previous
    /// line has always just closed that gap. Without this every colour
    /// segment was refused mid-programme with "only 2s since the last
    /// line" and then became due once the programme went quiet, which is
    /// how the form guide came to play after the sign-off as a show of its
    /// own. See CommentaryBudget.ReserveForProgramme for what is still
    /// enforced, which is everything else.
    ///
    /// Called on the UI thread like NextAsync, and for the same reason -
    /// the facts are read before the first await.
    /// </summary>
    public async Task<SpokenSegment?> ForTheProgrammeAsync(CancellationToken ct = default)
    {
        // THE RACE IS OVER, AND THIS SAYS SO ITSELF (11 Oct 2026).
        //
        // A programme only ever runs after the flag, so it can state that
        // rather than wait to be told - and waiting was a real bug, not a
        // tidiness point. RaceFinished is otherwise only called from the
        // announcer screen's half-second tick, on the state transition,
        // while the post-race sequence starts within a second of the flag
        // on a thread of its own. Ask before that tick has run and _racing
        // is still true, which makes Order() offer ONLY the in-race kinds
        // (BattleWatch and PaceNote, see InRace) - so the form guide was
        // never a candidate at all, and the segment the programme wanted
        // went unasked for and then played by itself after the sign-off.
        //
        // Idempotent: it sets a bool false. The tick calling it again a
        // moment later costs nothing.
        RaceFinished();

        // AFTER THE TICK'S PASS, NOT BESIDE IT (bug hunt 7). The flag can
        // fall while the announcer's tick is half way through writing a
        // battle call, and the programme asks for its colour straight away.
        // Two passes at once share _pass, _marks, the budget and tonight's
        // facts, none of which are built for it, and _forTheProgramme set
        // here would have let the tick's next kind skip its gap. So this
        // waits its turn - the colour is not wanted until after the
        // honours, and the tick's pass drops its in-race call as soon as
        // the write comes back (see TryAsync). Resumes on the UI thread,
        // which the gather at the top of the pass needs.
        await _onePass.WaitAsync(ct).ConfigureAwait(true);

        _forTheProgramme = true;

        try
        {
            return await PassAsync(ct).ConfigureAwait(true);
        }
        finally
        {
            _forTheProgramme = false;
            _onePass.Release();
        }
    }

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
        // The tick's pass gives way rather than waits: a programme pass is
        // running, and the tick asks again half a second later anyway.
        if (!_onePass.Wait(0))
        {
            return null;
        }

        try
        {
            return await PassAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _onePass.Release();
        }
    }

    /// <summary>
    /// One pass down the ladder. Only ever run by NextAsync or
    /// ForTheProgrammeAsync, and only while holding _onePass.
    /// </summary>
    private async Task<SpokenSegment?> PassAsync(CancellationToken ct)
    {
        // A line the host never answered for is given back once it is
        // older than any show could hold it - see AwaitingWordCeiling.
        DropStale();

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

        // Last pass's marks go before this pass records any - see _marks.
        // A segment that was dropped must not be marked said by the next
        // pass's segment of the same kind.
        _marks = new Dictionary<SegmentKind, string>();

        // TONIGHT'S MOMENTS, ONCE FOR THE WHOLE PASS, and before the
        // gather loop that needs them.
        //
        // Once because NightMoments.Over walks every saved race of the
        // evening and the loop below asks for twenty segments' facts: read
        // per kind, a twenty-race night would re-derive four hundred races
        // worth of moments on a half-second tick. And here rather than
        // inside Gather for the reason the gather loop itself is here -
        // this is the last point in the pass certainly still on the thread
        // the host called from, and the session history belongs to it.
        _momentsThisPass = MomentsNow();

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

                // The race started or finished while that one was being
                // written. The rest of this order belongs to the phase that
                // has gone, so nothing further is written for it.
                if (InRace(kind) != _racing)
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
    /// Tonight's moments as this pass sees them. Empty between passes and
    /// on a night nothing has happened in yet.
    ///
    /// Not cleared in the finally beside _pass, deliberately: it is read
    /// once per kind in the gather loop AND once more by the pay-off in
    /// Gather, and both of those run inside the pass. It is overwritten at
    /// the top of the next pass, which is the only place it is written.
    /// </summary>
    private IReadOnlyList<NightMoments.Moment> _momentsThisPass = Array.Empty<NightMoments.Moment>();

    /// <summary>
    /// Tonight's moments, with a reader that throws treated as a night
    /// nothing has happened in - the rule Gather and ReadNightProgress
    /// already follow, and it matters as much here: this one runs before
    /// the gather loop, so a session history caught mid-save would
    /// otherwise take the whole pass down rather than costing one
    /// callback.
    /// </summary>
    private IReadOnlyList<NightMoments.Moment> MomentsNow()
    {
        if (MomentsTonight is null)
        {
            return Array.Empty<NightMoments.Moment>();
        }

        try
        {
            return MomentsTonight() ?? Array.Empty<NightMoments.Moment>();
        }
        catch (Exception ex)
        {
            Say($"[brain] Couldn't work out tonight's moments: {ex.Message}");

            return Array.Empty<NightMoments.Moment>();
        }
    }

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
        IReadOnlyList<FactBlock> own;

        try
        {
            own = Candidates(kind, _className);
        }
        catch (Exception ex)
        {
            Say($"[brain] Couldn't work out what to say about {CommentaryBudget.Spec(kind).Title.ToLowerInvariant()}: {ex.Message}");

            return Array.Empty<FactBlock>();
        }

        // THE CALLBACK RIDES ALONG, in whichever segment turns out to go
        // out, exactly as CommentaryShowFacts.ThreadSoFar does.
        //
        // HERE RATHER THAN IN A CASE ARM OF Candidates, and rather than as
        // a segment of its own, and both of those are decisions:
        //
        //   A segment of its own would be a segment ABOUT THE BROADCAST.
        //   "Earlier we mentioned Carl, and Carl has just taken the lead"
        //   is worth five seconds inside a race recap and is an odd thing
        //   to stop an evening for.
        //
        //   One case arm would mean picking which segment gets to carry a
        //   callback, and the honest answer is whichever one the budget
        //   lets through next - which is not knowable from inside a case
        //   arm. This is the one funnel every kind comes through.
        //
        // AND IT IS LAST IN THE LIST. FactPack keeps the order it is given
        // and drops the tail when the room runs short, so a callback never
        // costs a segment the fact it is actually about. A reminder of
        // something the room already heard is the first thing that should
        // go.
        //
        // Never on an Essential. A race intro and a race recap are the two
        // segments a club gets whatever else is switched off, they are
        // written by CommentaryService rather than from here, and a result
        // read-out interrupted by "as we were saying earlier" is the one
        // place this would land badly. They never reach Gather anyway -
        // Order() skips them - and the guard is here so that stays true if
        // Order ever changes.
        if (kind is SegmentKind.RaceIntro or SegmentKind.RaceRecap or SegmentKind.BadgesEarned)
        {
            return own;
        }

        // AND NEVER ON ITS OWN. A callback is colour inside a segment that
        // has something to say; a segment with nothing of its own is
        // supposed to be dropped before it costs anything, and the budget
        // asks that by looking at this list - so a lone callback here would
        // turn "nothing to say about the form guide" into a form guide
        // whose entire content is a reminder about a driver. The club would
        // get a night of the announcer referring back to itself, and
        // NightAccount's own count of segments that had nothing to say
        // would quietly stop being true.
        if (own.Count == 0)
        {
            return own;
        }

        if (Promises.PayOff(_momentsThisPass, _clock()) is not { } callback)
        {
            return own;
        }

        var with = new List<FactBlock>(own.Count + 1);
        with.AddRange(own);
        with.Add(callback);

        return with;
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
        //
        // AND A SHOW WINS, which is the one added on 10 Oct 2026. The
        // commercial break hands the crowd's screen back as it ends, and if
        // its last item was a sponsor VIDEO that hand-back ticks
        // AdBreak.BreaksEnded - so the very next half-second tick had a
        // read owed at the exact moment the recap's opener was coming up
        // under it, and the advert went out between the recap and the
        // honours. A paid read over the top of the night's own programme is
        // worth less to the sponsor than the same read in a real gap, and
        // it is the programme the room is listening to.
        //
        // The debt is KEPT here, not settled - the same as for a race -
        // because that is what this whole method is for: the read goes out
        // in the next real quiet instead, which after a recap is the gap
        // before the next race is called. Nothing is lost and nobody is
        // billed for a read the room talked over.
        //
        // AND AN ADVERT ON THE CROWD'S SCREEN WINS TOO (11 Oct 2026). A
        // sponsor's video has its own soundtrack, and on 11 Oct the club's
        // written copy went out seven seconds into one: two adverts for two
        // different sponsors over each other, which is worth nothing to
        // either of them. The video's own hand-back is what ticks
        // BreaksEnded in the first place, so the owed read is almost always
        // sitting there waiting exactly while one is playing - and a video
        // that starts slowly, or a break that ended under it, puts the two
        // in the same few seconds. See CommercialBreak.VideoOnScreen.
        if (_racing || !ReadsBelongHere || StopSetHasRun || Held || Writing || ShowOnAir.Playing || ShowFloor.Busy || CommercialBreak.VideoOnScreen)
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
        // ShowOnAir for the same reason as in NextAdBreakRead: a gap with
        // the night's own programme playing in it is not a gap. Nothing is
        // deferred and nothing is lost - this is asked again on the next
        // tick, and the quiet after the sign-off is a real one.
        //
        // ShowFloor beside it because ShowOnAir only knows about the BED:
        // the recap sequence has quiet stretches between its lines with no
        // music under them, and a paid read dropped into one of those is
        // still a read over the programme. See ShowFloor.
        //
        // And VideoOnScreen for the reason NextAdBreakRead gives at length:
        // a gap with a sponsor's video advert playing in it is not a gap
        // either, and two adverts over each other sell neither.
        //
        // And only after a race - see ReadsBelongHere.
        if (_racing || !ReadsBelongHere || StopSetHasRun || Held || Writing || ShowOnAir.Playing || ShowFloor.Busy || CommercialBreak.VideoOnScreen)
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

        // Kept at once rather than on the way out, for the reason Save
        // gives: the restart this is for is usually a force-closed window.
        _settings.SponsorRotationNight = Sponsors.Save();
        _settings.Save();

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
            // THE HORIZON GOES AFTER THE OTHER LIVE CALLS (15 Oct 2026),
            // whatever its deadline says. It has the shortest life of the
            // three, so it sorted first and took the slot every time it had
            // something - and a record lap waiting behind it could run past
            // its own deadline and be thrown away. A record falling or a
            // fight being called is the thing happening; the horizon is the
            // thing about to, and it waits for them.
            .ThenBy(spec => spec.Kind == SegmentKind.Horizon)
            .ThenBy(spec => spec.NeedsBy)
            .Select(spec => spec.Kind);

        return kinds.ToList();
    }

    /// <summary>True for the segments that only make sense while cars are on track.</summary>
    internal static bool InRace(SegmentKind kind) =>
        kind is SegmentKind.BattleWatch or SegmentKind.PaceNote or SegmentKind.Horizon;

    private async Task<SpokenSegment?> TryAsync(SegmentKind kind, SynchronizationContext? ui, CancellationToken ct)
    {
        var spec = CommentaryBudget.Spec(kind);

        // NOT CLEARED HERE ANY MORE, and the reason is worth keeping.
        // This used to read "cleared before the candidates are gathered,
        // every pass", which was true until NextAsync was changed to
        // gather the WHOLE pass up front - after which Candidates ran
        // during the gather, these two lines wiped what it recorded, and
        // CandidatesForPass then returned the cached blocks without
        // re-running it. So the marks were never in hand when a segment
        // survived: ClubAsideSaid and RoomsCallSaid could not fire at all.
        //
        // A club note was spoken, its fact key was spent, and the store
        // was never told - so ToSay() offered the same note again, its key
        // was already spent, the pack came back empty, and the club notes
        // segment went silent for the rest of the night after note one.
        //
        // The marks are now recorded PER KIND at gather time (see
        // MarkWhenSpoken) and read by the kind being tried, so caching
        // cannot lose them and a dropped segment still marks nothing.

        // COUNTED HERE, before anything can refuse it, so the night's
        // account has an honest denominator: "eleven of nineteen" means
        // nineteen were weighed up rather than nineteen nearly said. See
        // NightAccount.
        Account.Weighed(kind);

        var blocks = CandidatesForPass(kind, _className);

        if (blocks.Count == 0)
        {
            // The commonest answer of all, and until 12 Oct 2026 it was
            // written down nowhere: a club reading the log saw a handful of
            // refusals and could not tell whether the quiet was three
            // segments or thirty.
            Account.NothingToSay();

            return null;
        }

        // Facts in a line written and still waiting to be said are not
        // offered again - they are not marked said until it is (see
        // CommitWhenSpoken), and a programme writes its colour segments
        // before it says any of them, so without this the second could be
        // built from the first one's facts.
        if (KeysAwaiting() is { Count: > 0 } awaitingKeys)
        {
            blocks = blocks.Where(block => !awaitingKeys.Contains(block.Key)).ToList();
        }

        var pack = FactPack.Build(blocks, Said);

        if (pack.Empty)
        {
            // Facts existed and every one was already spent, or had just
            // been talked about. Also silent until now, and a different
            // thing from having nothing to say - a club that hears this all
            // evening has a variety rule biting rather than thin results.
            Account.EveryFactSpent();

            return null;
        }

        var verdict = _forTheProgramme
            ? Budget.ReserveForProgramme(kind, _className)
            : Budget.Reserve(kind, _className);

        if (!verdict.Allowed)
        {
            // Once per reason - see _refusalSaid.
            if (!_refusalSaid.TryGetValue(kind, out var saidBefore)
                || !string.Equals(saidBefore, verdict.Reason, StringComparison.Ordinal))
            {
                _refusalSaid[kind] = verdict.Reason;
                Say($"[brain] Skipped {spec.Title.ToLowerInvariant()} - {verdict.Reason}.");
            }

            // EVERY TIME, where the line above is once per reason: the
            // console must not repeat itself and a tally that counted one in
            // ten would be a tally of nothing. By the typed reason rather
            // than by the sentence - see BudgetRefusal.
            Account.Refused(verdict.Why);

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

            // The verdict's fallback, obeyed. Colour and flourish segments
            // say "relay or nothing", and nothing read it, so when the relay
            // refused one Ollama wrote it anyway. Only once the relay has
            // told us the club's month: before that the relay is not this
            // club's brain yet, and the local model is all it has.
            FallbackCommentaryBrain.RelayOnly = verdict.Fallback == BrainFallback.None && Budget.AllowanceKnown;

            var written = await WriteAsync(kind, spec, pack, ct).ConfigureAwait(false);

            if (written is null)
            {
                Budget.Release(verdict.Ticket);
                return null;
            }

            if (_clock() - asked > spec.NeedsBy)
            {
                Budget.Release(verdict.Ticket);
                Account.DroppedAfterWriting();

                // Nobody heard it, so it is not the line Repeat offers.
                _commentary.ForgetLine(written.Text);

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
                Account.DroppedAfterWriting();
                _commentary.ForgetLine(written.Text);

                Say($"[brain] Dropped {spec.Title.ToLowerInvariant()} - the segments were held while it was being written.");
                return null;
            }

            // The race went green or the flag fell while it was being
            // written. A battle call that comes back after the chequered
            // flag is inside its deadline but describes a race that is
            // over, and would be said into the recap; a "coming up next"
            // that comes back after the green would be said over the race.
            // Given back like a hold, so nothing is marked said.
            if (InRace(kind) != _racing)
            {
                Budget.Release(verdict.Ticket);
                Account.DroppedAfterWriting();

                // Nobody heard this one either - see the two drops above.
                _commentary.ForgetLine(written.Text);

                Say($"[brain] Dropped {spec.Title.ToLowerInvariant()} - the race "
                    + (_racing ? "went green" : "finished")
                    + " while it was being written.");
                return null;
            }

            // WHAT IS SETTLED WHEN THE LINE IS SAID, kept together so it can
            // wait for the host (14 Oct 2026). Everything below used to run
            // here, the instant the line came back - and the host can still
            // drop a line after that: the next race called to the grid, the
            // floor taken, the hold pressed. A dropped club note was then
            // marked said and never heard, the segment's caps were spent and
            // Repeat offered a line nobody had heard. With CommitWhenSpoken
            // on, this waits for Spoken, and Dropped gives it all back.
            //
            // What it needs is taken NOW, while it is still this pass's:
            // the next pass clears the marks and moves the fight and the
            // pace note being called on.
            var ticket = verdict.Ticket;
            var calling = _calling;
            var paceCalling = _paceCalling;
            var horizonCalling = _horizonCalling;
            _marks.TryGetValue(kind, out var mark);
            SpokenSegment spokenLine = written;

            void Commit()
            {
                Budget.Settle(ticket);

                // IT WENT OUT, with the facts it was built from, at the same
                // instant as everything else that is settled here - so a
                // segment written and then dropped is not counted as said and
                // its facts are not offered as the proof of a line nobody
                // heard. See NightAccount.Proof.
                Account.Spoke(kind, pack);

                // THE MARK FOR THIS KIND, at the same instant as the facts
                // and for the same reason: every path out above gives the
                // ticket back and marks nothing, so a dedication whose segment
                // was dropped is still waiting to be said rather than quietly
                // spent. See _marks.
                if (mark is { Length: > 0 })
                {
                    if (kind == SegmentKind.ClubAside)
                    {
                        ClubAsideSaid?.Invoke(mark);
                    }
                    else if (kind == SegmentKind.RoomVerdict)
                    {
                        RoomsCallSaid?.Invoke(mark);
                    }
                }

                // THE PROMISE LEDGER, both halves of it, at the same instant
                // and under the same rule as everything else settled here: a
                // segment written and then dropped pointed the room at nobody
                // and came back to nothing. See NightPromises.
                //
                // Retired first, recorded second, and the order is deliberate.
                // A callback is itself a segment about a driver, so recording
                // comes back round and files a FRESH promise about the same
                // person - which is right, because the room has just been
                // reminded about Carl and there is something to come back to
                // again. It cannot loop: the race that paid the old promise
                // off had to have gone green before this line went out, so it
                // is already too early to pay the new one. Coming back to Carl
                // twice needs two races, which is the right answer.
                foreach (var key in pack.Keys)
                {
                    if (Promises.IsPayOffKey(key))
                    {
                        Promises.MarkPaid(key);
                    }
                }

                Promises.Note(kind, pack, NameFor, spokenLine.IsExchange, _clock());

                // The pack, not just its keys: the themes have to be marked
                // with them or the variety rule never fires and nothing says
                // so. See SaidTonight.Mark(PackedFacts) - and note that every
                // path out above gives the ticket back and marks NOTHING, so a
                // segment written and then dropped costs the night neither its
                // facts nor its variety.
                Said.Mark(pack);
                Spent(kind, calling, paceCalling, horizonCalling);
                SaveOn(ui);

                LastSaid = spokenLine;
            }

            if (!CommitWhenSpoken)
            {
                Commit();

                return written;
            }

            // Held for the host's word, still refundable - see Written.
            Budget.Written(ticket);

            lock (_awaitingGate)
            {
                _awaiting[written] = new AwaitingWord(Commit, ticket, pack.Keys, _clock());
            }

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
            FallbackCommentaryBrain.RelayOnly = false;
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
        // TWO VOICES ARE SAVED FOR THE RACES THAT EARN THEM (11 Oct 2026).
        //
        // The second voice is the loudest signal this broadcast has, and
        // spending it on every heat is how it stops meaning anything. A
        // room that hears the second voice only in the mains learns,
        // without ever being told, that two voices means this one counts -
        // which is worth more than the same segment being a two-hander in
        // heat three.
        //
        // A club with no programme keeps every bit of the show it already
        // had: RaceStature.Unknown is worth two voices. See
        // RaceStature.WorthTwoVoices.
        // AND THE CLUB'S DIAL CAN LET THEM OUT ANYWAY (12 Oct 2026). A club
        // that asks for a chatty evening means the two voices as much as it
        // means the count - holding the co-host back through the heats of a
        // big meeting with a crowd in is the pacing being clever at the
        // club's expense. See ShowChatter.TwoVoicesEverywhere.
        // AND THE CLUB CAN JUST ASK (14 Oct 2026). The chattiness dial
        // already let the two voices out everywhere at its top settings,
        // but only by making the whole evening chattier - a club that wants
        // the co-host in the heats and the same number of segments had no
        // way to say so. This is that, on its own.
        var twoVoices = _settings.CommentaryCoHost
                        && (Stature.WorthTwoVoices
                            || _settings.CommentaryTwoVoicesInHeats
                            || ShowChatter.TwoVoicesEverywhere(Chatter));

        if (!twoVoices && _settings.CommentaryCoHost && spec.CanBeExchange)
        {
            Say($"[brain] {spec.Title} in one voice - {Stature.Describe}, and the two-hander is held for the mains. "
                + "Tick \"two voices in the heats too\" on the Commentary page, or turn the chattiness up past "
                + $"{ShowChatter.Standard.Name}, to hear them in the heats.");
        }

        if (twoVoices && spec.CanBeExchange)
        {
            // THE LEAD COMMENTATOR HAS A NAME NOW (11 Oct 2026), where the
            // club has typed one on the Commentary page. This passed null
            // for him while naming the co-host, which left the model
            // writing a two-hander in which one of the two people had a
            // name and the other did not - and a model asked to write
            // dialogue between a named person and an unnamed one picks a
            // name for the unnamed one. On 11 Oct the co-host called him
            // Laura, on air, twice. See AppSettings.RecapAnnouncerName and
            // CommentaryExchange.Brief, which has always taken both.
            var host = CommentaryService.Field(_settings.RecapAnnouncerName);

            // HOW LONG A CONVERSATION RUNS IS THE CLUB'S CALL (14 Oct
            // 2026). This asked for the default two - a remark and a reply -
            // which is a call-and-response rather than two people talking.
            // BuildExchangeAsync clamps, so a stored value out of range
            // cannot ask for something CommentaryExchange will not write.
            var turns = await _commentary
                .BuildExchangeAsync(
                    kind, pack, host, _settings.CommentaryCoHostName,
                    _settings.CommentaryExchangeTurns, ct)
                .ConfigureAwait(false);

            if (turns.Count > 0)
            {
                // SAID WHEN IT WORKS, not only when it doesn't (14 Oct
                // 2026). Every other outcome here has a line - held for the
                // mains, came back in one piece - and a two-hander that
                // actually ran had none, so a night with no co-host in it
                // looked exactly like a night where one was written and
                // something later swallowed it. Three evenings were spent
                // guessing between those two.
                Say($"[brain] {spec.Title} written as a two-hander - {turns.Count} turns.");

                return new SpokenSegment(
                    kind,
                    CommentaryExchange.Flatten(turns, host, _settings.CommentaryCoHostName),
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

            case SegmentKind.Horizon:
            {
                // Kept, for BattleWatch's reason: Spent has to cool THIS
                // pair, not whatever Pick would answer by the time the line
                // has been heard.
                _horizonCalling = Horizons.Pick();

                return _horizonCalling is { } horizon
                    ? new[] { CommentaryFacts.Horizon(horizon, names, _raceKey) }
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

                _paceCalling = note;

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
                //
                // AND THE NIGHT'S KEY, NOT JUST THE RACE'S (10 Oct 2026).
                // Every fact ForIntro builds is true for the whole evening
                // - a debut is made once, a seven-two record is seven-two
                // all night - and since the race INTRO now carries this
                // same history for every race, a race-scoped key let the
                // same line go out from the intro and then again from here.
                // Handing both the race day means SaidTonight sees them as
                // one fact, which is what it is. See
                // CommentaryHistoryFacts.ForIntro's remarks.
                return GridHistory?.Invoke() is { } history
                    ? CommentaryHistoryFacts.ForIntro(
                        history.Grid, history.Rivalries, _raceKey, className, nightKey: _dayKey)
                    : Array.Empty<FactBlock>();
            }

            case SegmentKind.FormGuide:
            {
                // THE SAME READER AS THE GRID HISTORY, and that is the
                // point rather than a shortcut. A form guide is "how the
                // drivers in a main have been going lately", which is the
                // recent finishes CommentaryReaders.GridHistory already
                // counts for this grid in this class - so a second reader
                // would walk the same history on the same tick to reach the
                // same numbers, and the two could disagree after a result
                // was corrected mid-evening.
                //
                // What makes the two segments different is what they SAY:
                // the grid history picks the one superlative worth a
                // sentence, and the form guide reads the placings out. The
                // theme on each block is what stops them landing on the
                // same driver inside their separate budgets - see
                // FactBlock.Theme.
                //
                // The RACE's key and not the night's, unlike the grid
                // history above: a heat gets filed between races, so the
                // window these placings are read from really does change.
                // AND THE SHAPE OF THE LAST FEW NIGHTS (11 Oct 2026), which
                // is the half of a form guide the app did not have. The
                // placings below are a results sheet read aloud - "fourth,
                // second, first, sixth"; this is the thing a regular says
                // to another regular in the pits: "he has been on the
                // podium three weeks running". Same segment, because they
                // answer the same question and a club does not want two
                // form guides - and the per-driver themes are what stop
                // both landing on the same person. See CommentaryWeekFacts.
                //
                // THE NIGHT'S KEY for these and the RACE's for the placings
                // below, which is not an inconsistency. A heat gets filed
                // between races, so the window those placings are read from
                // really does change; a run of three NIGHTS is still three
                // until the club races again, so a race-scoped key would
                // have a club running six heats hear the same sentence in
                // all six.
                var weeks = RecentWeeks?.Invoke() is { } shape
                    ? CommentaryWeekFacts.ForTheWeeks(shape, className, _dayKey)
                    : Array.Empty<FactBlock>();

                var placings = GridHistory?.Invoke() is { } form
                    ? CommentaryHistoryFacts.FormGuide(form.Grid, _raceKey, className)
                    : Array.Empty<FactBlock>();

                if (weeks.Count == 0)
                {
                    return placings;
                }

                // The weeks FIRST. A run of podium nights is the stronger
                // claim and the one a room reacts to, and the pack's cap
                // keeps the order it is given.
                return weeks.Concat(placings).ToList();
            }

            case SegmentKind.MidMeetingStandings:
            {
                if (MidMeeting?.Invoke() is not { } soFar)
                {
                    return Array.Empty<FactBlock>();
                }

                // The key carries how many heats have run, so a second
                // check later in the block is a fresh fact rather than the
                // same one refused - which is what the spec's two-a-class
                // allowance is for. The class is in it too: two classes
                // running alternate heats each get their own.
                return CommentaryReportFacts.MidMeetingStandings(
                    soFar,
                    $"{_dayKey}:{soFar.ClassName}:{soFar.RoundsRun}");
            }

            case SegmentKind.ClubAside:
            {
                // KEYED ON THE NOTE'S OWN ID, not on the night or the
                // race. Each note is its own fact and is said once; a
                // night-scoped key would let the first dedication spend
                // the key and silence the other five.
                if (ClubAside?.Invoke() is not { } note)
                {
                    return Array.Empty<FactBlock>();
                }

                if (CommentaryShowFacts.ClubAside(note, note.Id) is not { } aside)
                {
                    return Array.Empty<FactBlock>();
                }

                MarkWhenSpoken(SegmentKind.ClubAside, note.Id);

                return new[] { aside };
            }

            case SegmentKind.RoomVerdict:
            {
                // KEYED ON THE RACE, not on the night: the vote is opened
                // per race, so a night-scoped key would let the first
                // verdict spend it and silence every one after.
                if (RoomsCall?.Invoke() is not { } asked)
                {
                    return Array.Empty<FactBlock>();
                }

                if (CommentaryShowFacts.RoomVerdict(asked.Call, asked.WinnerName, _raceKey) is not { } verdict)
                {
                    return Array.Empty<FactBlock>();
                }

                MarkWhenSpoken(SegmentKind.RoomVerdict, asked.RaceKey);

                return new[] { verdict };
            }

            case SegmentKind.ThisDayInHistory:
            {
                // Tonight's key, so the same anniversary is news again next
                // year and never twice in one evening.
                return OnThisDay?.Invoke() is { } days
                    ? CommentaryHistoryFacts.ThisDayAtTheClub(days, _dayKey)
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
                var opening = new List<FactBlock>();

                if (NightAhead?.Invoke() is { } ahead
                    && CommentaryShowFacts.ColdOpen(ahead, _dayKey) is { } block)
                {
                    opening.Add(block);
                }

                // AND THE NIGHT'S THREAD IS SET HERE (11 Oct 2026), second
                // because the shape of the evening orients a room and the
                // storyline hooks it - "forty-one drivers, thirty races",
                // then "can anybody stop that run tonight". Either can be
                // absent and the other still reads.
                //
                // Both go out in ONE pack, so both are spent together when
                // the line is actually spoken and neither can come back
                // later in the evening on its own.
                if (NightStory?.Invoke() is { } story
                    && CommentaryShowFacts.ThreadAhead(story, _dayKey) is { } thread)
                {
                    opening.Add(thread);
                }

                return opening;
            }

            case SegmentKind.SignOffNumbers:
            {
                var closing = new List<FactBlock>();

                // THE THREAD RESOLVED, AND FIRST. FactPack keeps the order
                // it is given and drops the tail when the room runs short,
                // so this is the one line that has to survive: the numbers
                // nobody was counting are the sign-off's own argument, but
                // the storyline is what the room has been waiting on since
                // the cold open, and a bracket is noticed mostly when it is
                // missing.
                if (NightStory?.Invoke() is { } story
                    && CommentaryShowFacts.ThreadDone(story, _dayKey) is { } thread)
                {
                    closing.Add(thread);
                }

                if (NightDone?.Invoke() is { } done)
                {
                    closing.AddRange(CommentaryShowFacts.SignOff(done, _dayKey));
                }

                return closing;
            }

            // What is left here is written somewhere other than by the
            // model, and reaching this point for one of them is correct
            // rather than a gap:
            //
            //   BadgesEarned and Honours are built by HeatCaller out of
            //   the badges themselves - see RaceHonours. Order() skips the
            //   Essentials, so BadgesEarned never arrives here at all.
            //
            //   MechanicInterview has a path of its own, written beside
            //   the recap from the Garage's setup log.
            //
            //   RaceIntro and RaceRecap are Essentials with their own
            //   prompt builders in CommentaryService, and are likewise
            //   never asked for here.
            //
            // Anything ADDED to SegmentKind without a case lands here and
            // is refused for ever in silence, which is the fault the three
            // segments fixed on 10 Oct 2026 had. CommentaryDepthTests holds
            // every member to having a case or being on that list.
            default:
                return Array.Empty<FactBlock>();
        }
    }

    /// <summary>
    /// Everything the night's own facts amount to, packed for a piece
    /// rather than for a line - see <see cref="NightRecap"/>.
    ///
    /// THE SAME FACTS THE SIGN-OFF IS BUILT FROM, and deliberately WITHOUT
    /// the "already said" filter: a recap for somebody who was not there
    /// wants the result the room heard an hour ago. Passing Said here, as
    /// every segment does, would hand a club that had a good sign-off an
    /// empty recap - which is exactly backwards.
    ///
    /// SPENDS NOTHING. No budget is reserved, nothing is marked said, and
    /// no theme is cooled: this is asked once, by somebody pressing a
    /// button, and what it costs is the one model call the caller makes
    /// with it.
    ///
    /// Empty on a night with nothing filed, and then there is no recap to
    /// write - which the caller says in those words.
    /// </summary>
    public PackedFacts NightFacts() =>
        FactPack.Build(Gather(SegmentKind.SignOffNumbers), said: null);

    /// <summary>The fight this pass's battle facts were built from. See Candidates.</summary>
    private Battle? _calling;

    /// <summary>The horizon this pass's facts were built from - see _calling, for the same reason.</summary>
    private Horizon? _horizonCalling;

    /// <summary>
    /// What a segment has to do once its line has actually been said - for
    /// the fight and the pace note it was written about, taken when it was
    /// written, because by the time a held line is said the next pass may
    /// be calling on others. See TryAsync.
    /// </summary>
    private void Spent(SegmentKind kind, Battle? calling, PaceNote? paceCalling, Horizon? horizonCalling = null)
    {
        switch (kind)
        {
            case SegmentKind.Horizon:
            {
                // Left alone for RaceHorizon's cooldown, so a gap coming
                // down over six laps is pointed at once and then watched -
                // the pay-off is the race itself, not five more lines.
                if (horizonCalling is { } horizon)
                {
                    Horizons.Said(horizon);
                }

                break;
            }

            case SegmentKind.BattleWatch:
            {
                // Left alone for the cooldown, so two cars nose to tail
                // for a whole heat are one story and not twenty. The one
                // that was actually announced, not whatever Pick would
                // answer now.
                if (calling is { } battle)
                {
                    Battles.Said(battle);
                }

                break;
            }

            case SegmentKind.PaceNote:
                // Only if it is still the one that was said. A faster lap
                // seen while it was being written is a new note, and
                // PaceWatch will not raise it again. Exchanged rather than
                // compared and set, because Lap writes this from the UI
                // thread while this runs wherever the write came back.
                if (paceCalling is { } said)
                {
                    Interlocked.CompareExchange(ref _pending, null, said);
                }

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
