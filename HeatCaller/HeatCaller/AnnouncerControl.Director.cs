namespace HeatCaller;

/// <summary>
/// The announcer screen's side of the broadcast segments: feeding the
/// director what the race is doing, and speaking what it hands back.
///
/// WHY HERE
///
/// This control already holds everything the job needs - the session, the
/// profiles, the announcer and a tick that runs twice a second - and it is
/// already where the in-race commentary lives. Putting it anywhere else
/// would mean threading four references through MainForm to reach the same
/// place.
///
/// WHY IT READS RATHER THAN SUBSCRIBES
///
/// The session raises LapProcessed as laps arrive, which is off this
/// thread. The director is a pile of plain dictionaries and was written to
/// be driven from one thread, so instead of subscribing, each tick reads
/// what has changed since the last one - the same thing the rest of this
/// page does with the same data, on the same thread. A lap that arrives
/// mid-tick is picked up by the next one, half a second later, which is
/// far inside any deadline a segment has.
///
/// WHAT IS NOT WIRED FROM HERE
///
/// The programme, the club's sponsors and the history readers belong to
/// stores this control does not hold. They are properties on the director
/// for MainForm to set, and until it does those segments say nothing and
/// cost nothing - see CommentaryDirector.
///
/// That now includes the continuity readers - director.Upcoming and
/// director.JustRan - and they are the two that decide whether an evening
/// sounds like a programme. Unwired, every bridge in this file is silent
/// and every running order falls back to the one segment it always was:
/// correct, and quieter than it should be. See ContinuityShow.
///
/// AND WHAT A SHOW IS NOW
///
/// A show used to wrap one segment. It is a BLOCK - open once, item, mark,
/// item, ending, with the bed under all of it - so this page builds the
/// running order rather than handing over a single line, and holds its
/// one-at-a-time guard for as long as the block runs rather than only for
/// as long as the writing takes. See SpeakNextSegmentAsync and ShowBlock.
/// </summary>
public partial class AnnouncerControl
{
    private CommentaryDirector? _director;

    /// <summary>Laps already handed over, per transponder, so each is fed once.</summary>
    private readonly Dictionary<string, int> _lapsFed = new(StringComparer.OrdinalIgnoreCase);

    private int _directorGeneration = -1;
    private bool _segmentInFlight;
    private string _directorDay = string.Empty;

    /// <summary>
    /// The hand-over this page has already made, named by the two rounds it
    /// sat between - see <see cref="HandOverIfItIsTimeAsync"/>. Empty before
    /// the first one.
    /// </summary>
    private string _handedOver = string.Empty;

    /// <summary>
    /// Which race day is open, as MainForm reads it. Null until wired,
    /// and then the whole commentary layer is off: without a day there is
    /// nothing to carry a night's spend against, and the club's own
    /// segment switches are loaded with it.
    /// </summary>
    public Func<string>? RaceDayId { get; set; }

    /// <summary>
    /// The class on track, as MainForm reads it from the active round.
    ///
    /// Scopes the per-class caps in the budget, and tells the history
    /// facts which class their numbers were counted over - without it
    /// they describe the club as a whole and say so.
    /// </summary>
    public Func<string?>? RaceClassName { get; set; }

    /// <summary>
    /// The broadcast segments' console lines - every refusal, with its
    /// reason. MainForm sends these to the developer console; a race night
    /// that went quiet is otherwise impossible to account for.
    /// </summary>
    public event Action<string>? StatusMessage;

    /// <summary>
    /// The director, built on first use.
    ///
    /// Null until there is a commentary service and settings to build it
    /// with - this control takes both as optional, and the pages that
    /// build it for a preview pass neither.
    /// </summary>
    public CommentaryDirector? Director
    {
        get
        {
            if (_director is not null || _commentary is null || _settings is null)
            {
                return _director;
            }

            _director = new CommentaryDirector(_commentary, _settings);
            _director.NameFor = NameFor;
            _director.Status += line => StatusMessage?.Invoke($"{AppTime.LogStampNow()}  {line}");

            // THE NIGHT'S LEDGER, HANDED TO THE TWO PROMPT BUILDERS THAT DO
            // NOT COME THROUGH THE DIRECTOR (10 Oct 2026).
            //
            // The race intro and the race recap have prompt builders of
            // their own in CommentaryService, and both now carry the club's
            // own history. They carried it with no idea what the night had
            // already said, because SaidTonight lives on the director - so
            // a club running six heats with the same ten entries heard
            // "Billy is making their first ever Box Stock start" in all six
            // intros, which is a sharper version of the very complaint the
            // history was added to answer.
            //
            // FILTER ONLY from the service's side: it reads this to leave
            // out what has been said and never marks it. Marking happens
            // where the line is actually queued to the announcer, because a
            // recap written and then dropped must cost the night nothing -
            // see CommentaryService.HistorySaid for the whole argument.
            //
            // Here because this is the one place in the app that holds both
            // the service and the director, and here rather than in
            // MainForm's wiring because the director is built on first use
            // and may not exist when anything else is hooked up.
            _commentary.HistorySaid = _director.Said;

            return _director;
        }
    }

    /// <summary>
    /// The show's music, built on first use beside the director and for
    /// the same reasons. Null until there are settings and an announcer
    /// to build it with.
    /// </summary>
    private ShowTreatment? Treatment
    {
        get
        {
            if (_treatment is not null || _settings is null)
            {
                return _treatment;
            }

            _treatment = new ShowTreatment(_settings, _announcer);
            _treatment.Status += line => StatusMessage?.Invoke($"{AppTime.LogStampNow()}  {line}");

            return _treatment;
        }
    }

    private ShowTreatment? _treatment;

    /// <summary>
    /// Forgets the loaded show music, so the next show reads the files
    /// again. Called from the Commentary page when a club chooses
    /// different music - the page has a ShowMusic of its own, so the two
    /// caches have to be told separately.
    /// </summary>
    public void ReloadShowMusic() => ShowMusic.Reload();

    /// <summary>
    /// The show's music, for Race Control's recap sequence - the one show
    /// this control does not run itself, because the recap is a sequence
    /// of its own with its own waits and its own cancellation. Null until
    /// there is a commentary service and settings to build it with.
    /// </summary>
    internal ShowTreatment? ShowMusicTreatment => Treatment;

    /// <summary>A line for the Commentary page's Test button - the kind of thing a cold open says.</summary>
    private const string ShowTestLine =
        "Good evening and welcome to the club, where the track is clean, the batteries are charged, "
        + "and we have got a full programme ahead of us tonight.";

    /// <summary>
    /// Plays a show open with a test line over it, exactly as a real
    /// segment would - the only way to set the levels by ear without
    /// running a race night. Returns what is wrong, or null if it ran.
    ///
    /// Deliberately the real path (SpeakReminder through the queue) rather
    /// than the announcer's test line: a test line bypasses the queue and
    /// so never raises SpeakingChanged, which means the bed would not duck
    /// - and the duck is half of what there is to listen for. The cost is
    /// that a muted announcer says nothing, which is said plainly below
    /// rather than left as silence.
    /// </summary>
    public async Task<string?> TestShowMusicAsync()
    {
        if (Treatment is not { } treatment)
        {
            return "The commentary isn't set up on this PC yet.";
        }

        if (!treatment.Music.Ready(out var problem))
        {
            return problem;
        }

        if (_announcer.Muted)
        {
            return "The announcer is muted, so you would hear the music with nobody over it. "
                + "Unmute it on the Mixer and try again.";
        }

        await treatment.RunAsync(() => _announcer.SpeakReminder(ShowTestLine, showOnScreens: false)).ConfigureAwait(true);

        return null;
    }

    /// <summary>
    /// One tick: tell the director where the race is, then ask whether
    /// anything is worth saying.
    ///
    /// Deliberately called before this page's own visibility check. The
    /// segments are spoken over the PA, not shown here, so a club that
    /// never opens the announcer screen must still get them.
    /// </summary>
    private void FeedTheDirector()
    {
        if (Director is not { } director || _settings?.CommentaryEnabled != true || _commentary?.IsAvailable != true)
        {
            return;
        }

        var day = RaceDayId?.Invoke() ?? string.Empty;

        if (day.Length == 0)
        {
            // No race day, no night to budget against. Practice and a
            // club night with nothing filed get the announcements they
            // always did and none of the segments.
            return;
        }

        // Cheap and idempotent - it returns at once for the day it is
        // already on, and starts a fresh night when somebody opens a new
        // one halfway through an evening.
        director.NewMeeting(day);

        if (!string.Equals(_directorDay, day, StringComparison.Ordinal))
        {
            _directorDay = day;
            _directorGeneration = -1;
            _lapsFed.Clear();
            _handedOver = string.Empty;

            // A fresh evening for the show's running order too, so the first
            // show of the night gets the whole opener. Without this it is
            // inferred from a twenty-minute quiet spell, which gets it wrong
            // for a club that set their levels with Play It ten minutes
            // before the first race - they would hear a rejoin as their cold
            // open. Here rather than in NewMeeting because this is the one
            // place that already knows the day actually changed.
            Treatment?.NewNight();
        }

        var racing = _race.State == RaceState.Racing;

        // A new session number means a new race, whatever happened to the
        // one before it - finished, reset, or abandoned halfway.
        if (racing && _race.SessionGeneration != _directorGeneration)
        {
            _directorGeneration = _race.SessionGeneration;
            _lapsFed.Clear();

            director.RaceStarted(_race.SessionGeneration.ToString(), RaceClassName?.Invoke());
        }
        else if (!racing && _directorGeneration >= 0)
        {
            _directorGeneration = -1;

            director.RaceFinished();
        }

        if (racing)
        {
            var ordered = _race.GetOrderedStandings();

            director.Standings(PairsFrom(ordered));
            FeedLaps(ordered);
        }

        // NOT while a race is being called to the grid.
        //
        // Staging is not "between races" - it is seconds before a start.
        // A between-races segment started here is written while the grid
        // forms, and the countdown bins it the moment it arrives: the
        // voice queue says "dropped - the countdown started while it was
        // being synthesised". The club paid the relay for that line and
        // nobody heard it, once per staged race, all night.
        //
        // The budget cannot catch this. It is asked whether a line is
        // affordable, not whether the next ten seconds are about to be a
        // countdown, and by the time the line comes back it has already
        // been spent.
        //
        // Essentials are unaffected: the race intro is spoken from Race
        // Control's own staging chain, not from here.
        if (_race.State is RaceState.Staging or RaceState.Countdown)
        {
            return;
        }

        _ = SpeakNextSegmentAsync(director);
    }

    /// <summary>
    /// Every adjacent pair in the running order, as BattleWatch wants
    /// them.
    ///
    /// Gaps at the line, from RaceGaps, never by comparing lap counts -
    /// those differ for everybody the moment the leader crosses. A pair
    /// whose gap cannot be measured is handed over as one that is miles
    /// apart, so the detector drops it rather than treating "unknown" as
    /// "nose to tail".
    /// </summary>
    private static IReadOnlyList<PairGap> PairsFrom(IReadOnlyList<RacerStanding> ordered)
    {
        if (ordered.Count < 2)
        {
            return Array.Empty<PairGap>();
        }

        var pairs = new List<PairGap>(ordered.Count - 1);

        for (var i = 0; i < ordered.Count - 1; i++)
        {
            var gap = RaceGaps.Between(ordered[i + 1], ordered[i]);

            pairs.Add(new PairGap(
                ordered[i].TransponderId,
                ordered[i + 1].TransponderId,
                i + 1,
                gap.GapMs is { } ms ? ms / 1000.0 : double.MaxValue,
                gap.LapsDown));
        }

        return pairs;
    }

    /// <summary>
    /// Hands over the laps that have come in since the last tick.
    ///
    /// Counted per transponder rather than read from an event, so a lap
    /// is fed exactly once however many ticks it sits there for, and a
    /// race that is reset starts the count again (see _lapsFed, cleared
    /// with the session number).
    /// </summary>
    private void FeedLaps(IReadOnlyList<RacerStanding> ordered)
    {
        if (Director is not { } director)
        {
            return;
        }

        foreach (var standing in ordered)
        {
            // Snapshotted: the decoder's thread appends to this list, and
            // a count read once is a count that cannot grow underneath
            // the loop below.
            var laps = standing.LapTimesMs;
            var now = laps.Count;
            var fed = _lapsFed.GetValueOrDefault(standing.TransponderId);

            if (now <= fed)
            {
                continue;
            }

            for (var i = fed; i < now && i < laps.Count; i++)
            {
                director.Lap(standing.TransponderId, laps[i] / 1000.0);
            }

            _lapsFed[standing.TransponderId] = now;
        }
    }

    /// <summary>
    /// Asks the director for a line and speaks it.
    ///
    /// One at a time: this runs twice a second and a written line takes
    /// seconds, so without the guard the requests would pile up far faster
    /// than they complete - the same reason the talking point has one.
    /// Fire and forget, because nothing on screen waits on it.
    ///
    /// THE GUARD NOW COVERS THE WHOLE SHOW, not just the writing.
    ///
    /// It used to let go the moment the words were handed to the staging,
    /// which was fine when a show was one line over an opener and over in
    /// seconds. A BLOCK holds two or three items and PACES them - it waits
    /// for each to have been heard before queueing the next, which is the
    /// only reason a mark between two of them is audible - so it owns the
    /// gap for as long as it runs. Let go early, the tick half a second
    /// later would ask the director for another segment and start a second
    /// block on top of the first, and the tick after that would offer a
    /// sponsor's paid read into the same gap. "Never two voices at once"
    /// has to be true of a programme as well as of a line.
    /// </summary>
    private async Task SpeakNextSegmentAsync(CommentaryDirector director)
    {
        if (_segmentInFlight)
        {
            return;
        }

        _segmentInFlight = true;

        try
        {
            var said = await director.NextAsync().ConfigureAwait(true);

            if (said is null || IsDisposed)
            {
                // Nothing from the segment ladder. THAT is the quiet a
                // sponsor's paid read is for, and it is asked second on
                // purpose: a recap or a "coming up next" always outranks
                // somebody's advert. See CommentaryDirector.NextSegmentRead.
                if (said is null && !IsDisposed)
                {
                    // And if nobody has bought the quiet either, the
                    // programme can still say where it is. A hand-over is
                    // asked LAST of the three because it is the only one
                    // that costs nothing: the ladder's line was paid for
                    // and the sponsor's was sold, and neither should be
                    // pushed out of a gap by two sentences this laptop
                    // wrote for free. See HandOverIfItIsTimeAsync.
                    if (!SayTheSponsorsReadIfThereIsOne(director))
                    {
                        await HandOverIfItIsTimeAsync(director).ConfigureAwait(true);
                    }
                }

                return;
            }

            // Held while this one was being written. It was asked for and
            // paid for, and it is not going to be said over whatever the
            // race director picked the mic up for - which is the whole
            // point of the button. Said plainly, because a line that cost
            // the club a write and never aired should not vanish quietly.
            if (director.Held)
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [brain] Written, then dropped - the segments were held before it could be said.");

                return;
            }

            // A hand-over's whole job is to say what has just gone and
            // what is next, and a segment block says both of those round
            // the segment itself. Marked here so the two cannot both fire
            // into one gap and say the same thing twice in different
            // words, which is the single most noticeable thing an
            // announcer can get wrong.
            _handedOver = HandOverKey(director.ContinuityNow());

            await SpeakAsync(said, director).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [brain] {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _segmentInFlight = false;
        }
    }

    /// <summary>
    /// Continuity on its own: "That was the B-main for Stock. Still to come,
    /// the A-main for Stock." No segment, no model, no allowance.
    ///
    /// WHY THIS IS THE ONE THAT MATTERS
    ///
    /// Everything else on this page needs the relay and a month's
    /// allowance behind it. This needs neither - the lines are built by
    /// concatenating the programme and what has been filed - so it is what
    /// a club hears on the evening the allowance has run out, on the
    /// evening they never switched the AI on at all, and on the evening the
    /// relay is down. Two sentences and a beat between them is still a
    /// programme where silence over the top of the playlist is not. See
    /// CommentaryContinuity.
    ///
    /// ONCE PER HAND-OVER. This is reached from a tick that runs twice a
    /// second, so without the key below the club would get a bridge, then
    /// the same thing in different words, then again, until the rotation
    /// ran out of ways to say it - sixty lines a minute, and about twenty
    /// seconds of airtime before it went quiet for the rest of the night.
    /// The key is the two rounds the hand-over sits between, so it re-arms
    /// itself when the programme moves on: once a race, which is what a
    /// hand-over is.
    /// </summary>
    private async Task HandOverIfItIsTimeAsync(CommentaryDirector director)
    {
        // Held means the race director has the mic and Writing means a line
        // is about to arrive; the director checks both before it offers a
        // paid read and this is in exactly the same position.
        if (Treatment is not { } treatment || director.Held || director.Writing || IsDisposed)
        {
            return;
        }

        // Not over a race. Staging and the countdown are already turned
        // away before this is reached (FeedTheDirector, which explains at
        // length why), so a race on track is the one left - and "still to
        // come" said over one is the thing ShowBlock was written about.
        if (_race.State == RaceState.Racing)
        {
            return;
        }

        var facts = director.ContinuityNow();
        var key = HandOverKey(facts);

        if (key.Length == 0 || string.Equals(_handedOver, key, StringComparison.Ordinal))
        {
            return;
        }

        // Marked BEFORE the block runs, not after. The tick does not wait
        // for this method, so a block that takes twenty seconds would
        // otherwise be started again by every tick inside it. Marked even
        // when nothing goes out: a gap too short for a block now is a gap
        // too short for one half a second later, and retrying it forty
        // times would only fill the console.
        _handedOver = key;

        if (ContinuityShow.HandOver(facts, director.Continuity, SayContinuityLine) is not { } block)
        {
            return;
        }

        try
        {
            await treatment.RunBlockAsync(block).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [show] {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Which hand-over this is: the round that has just run and the round
    /// that is next, which together name the one moment a hand-over
    /// belongs in.
    ///
    /// Empty when either end is unknown, which is also the bar
    /// ContinuityShow.HandOver sets for saying anything at all - a
    /// hand-over with one end missing is not a hand-over.
    /// </summary>
    private static string HandOverKey(ContinuityFacts facts)
    {
        if (facts.JustRan is not { } ran || facts.Upcoming.Count == 0)
        {
            return string.Empty;
        }

        var next = facts.Upcoming[0];

        return $"{ran.Label}/{ran.ClassName}>{next.Label}/{next.ClassName}";
    }

    /// <summary>
    /// Says it: as a show where the club wants one round this segment, and
    /// plainly where they do not.
    ///
    /// Returns a task the caller AWAITS, which is the one change here worth
    /// knowing about. It used to be fired and forgotten; a block owns the
    /// gap while it paces its items, so the caller's one-at-a-time guard
    /// has to stay on for as long as it runs. See SpeakNextSegmentAsync.
    /// </summary>
    private Task SpeakAsync(SpokenSegment said, CommentaryDirector director)
    {
        if (_settings is null)
        {
            return Task.CompletedTask;
        }

        // The show segments get an opener in front of them and a bed
        // underneath - see ShowTreatment - and, where the night around
        // them gives anything to bridge to, continuity either side.
        //
        // Still gated on the club's own tick list. A block IS a show: it
        // plays the opener and silences the playlist exactly as one line
        // over staging does, so running one for a segment the club did not
        // tick would be this feature overruling the Commentary page.
        if (Treatment is { } treatment && treatment.Treats(said.Kind))
        {
            return RunAsShowAsync(treatment, said, director);
        }

        SpeakPlainly(said);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs a segment as a show - as a running order with continuity round
    /// it where there is anything to bridge to, and as the one line it
    /// always was where there is not - falling back to saying it plainly
    /// when there turns out to be no music.
    ///
    /// THE FALLBACK IS THE DELICATE PART, AND A BLOCK CHANGES IT
    ///
    /// RunAsync hands the words BACK when there is no music to play, having
    /// deliberately not spoken, and the line is then said the ordinary way
    /// rather than lost to a club that never dropped the files in.
    /// RunBlockAsync does the opposite: it speaks the items itself either
    /// way, because the programme is the items and their order and the
    /// music is only how it is dressed.
    ///
    /// So the flag below is what both paths are judged on, and it is set
    /// inside the delegates that queue the words - which is the only thing
    /// that knows. A segment said twice is far worse than one said without
    /// music.
    /// </summary>
    private async Task RunAsShowAsync(ShowTreatment treatment, SpokenSegment said, CommentaryDirector director)
    {
        // Whether the staging got as far as queueing the words. See the
        // remarks: this is the ONE signal, and it is deliberately not
        // "did the block report an item" - an exchange that got one turn
        // out has been spoken, however the block then ended.
        var spoken = false;

        void SpeakOnce()
        {
            spoken = true;
            SpeakPlainly(said);
        }

        void SpeakTurn(ExchangeTurn turn)
        {
            spoken = true;
            SpeakTurnPlainly(turn);
        }

        // A race moved on and took the block with it. The segment is then
        // NOT said plainly afterwards: the room's attention is on the
        // track, a race call is already in front of it on the voice queue,
        // and a recap of the last race arriving over a countdown is the
        // exact thing ShowTreatment.AbandonBlock exists to stop.
        var abandoned = false;

        try
        {
            // The facts are read here, on the thread this came back on,
            // and handed over - the rule CommentaryDirector.NextAsync
            // follows for the same reason. Null when the night gives
            // nothing to bridge to: no programme entered, nothing filed
            // yet, the last race of the evening already run.
            var order = ContinuityShow.Around(
                said,
                director.ContinuityNow(),
                director.Continuity,
                SpeakOnce,
                SpeakTurn,
                SayContinuityLine);

            if (order is { } block)
            {
                var result = await treatment.RunBlockAsync(block).ConfigureAwait(true);

                abandoned = result.Abandoned;
            }
            else
            {
                // said.Kind, so the running order can tell a sign-off from an
                // ordinary segment. It is the one cue that cannot be worked out
                // from the clock - "last show of the night" is only knowable from
                // what is being said - and without it the sign-off never gets its
                // playout. See ShowRunningOrder.
                await treatment.RunAsync(SpeakOnce, said.Kind).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [show] {ex.GetType().Name}: {ex.Message}");
        }

        if (spoken)
        {
            return;
        }

        if (abandoned)
        {
            // Said out loud in the console rather than left as silence: the
            // club paid a write for this line and nobody heard it, and a
            // quiet night with no reason in the log is the thing this whole
            // layer is logged to death to avoid.
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [show] Written, then dropped - the race moved on before the block reached it.");

            return;
        }

        // No music to play, or it failed before the lines went in, or the
        // gap had no room for a block at all - said the ordinary way. The
        // words are the part that matters, and this is exactly what
        // happened before any of the staging existed.
        SpeakOnce();
    }

    /// <summary>
    /// A continuity line, queued like every other spoken line so a flag
    /// call can jump in front of whatever of it is unspoken.
    ///
    /// On the screens, like a segment: "Still to come, the A-main" is for
    /// the room, and the room is looking at the Big Screen.
    /// </summary>
    private void SayContinuityLine(string line) => _announcer.SpeakReminder(line, showOnScreens: true);

    /// <summary>
    /// A sponsor's own words, read in the gap the segments left empty.
    ///
    /// Written by a human and read as written, so it needs no model and no
    /// budget - which is the point of it: a club with the AI commentary
    /// switched off entirely still reads the advert somebody paid for.
    ///
    /// Through SpeakReminder, like every segment, so the line goes on the
    /// voice queue and a flag call can jump in front of whatever of it is
    /// still unspoken. The director refuses while a race is on track, while
    /// a hold is on and while it is writing, so there is nothing to check
    /// again here.
    ///
    /// Returns whether a read actually went out, so the caller knows
    /// whether the gap is still empty. A hand-over goes into the quiet
    /// AFTER this has had its turn and never over the top of it: the
    /// sponsor's words were sold and the hand-over's were free.
    /// </summary>
    private bool SayTheSponsorsReadIfThereIsOne(CommentaryDirector director)
    {
        // The real gap where the host knows it, so copy longer than the gap
        // is split at a sentence rather than run into the next race; no
        // limit otherwise, where the read's own 25-second cap still applies.
        var gap = director.Upcoming?.Invoke().InAbout ?? SponsorCopy.NoLimit;

        var paid = director.NextAdBreakRead(gap) ?? director.NextSegmentRead(gap);

        if (paid is null)
        {
            return false;
        }

        // Counted before it is said: SpeakReminder is fire and forget, and a
        // read that went out but was never counted would be read again in the
        // next gap, which is the one thing a paying sponsor would notice.
        director.SalesReadSaid(paid);
        _announcer.SpeakReminder(paid.Text, showOnScreens: true);

        return true;
    }

    /// <summary>Says it with no music in front of it - what every segment did before the show treatment.</summary>
    private void SpeakPlainly(SpokenSegment said)
    {
        if (_settings is null)
        {
            return;
        }

        if (!said.IsExchange)
        {
            _announcer.SpeakReminder(said.Text, showOnScreens: true);
            return;
        }

        foreach (var turn in said.Turns)
        {
            SpeakTurnPlainly(turn);
        }
    }

    /// <summary>
    /// One turn of a two-hander, in whichever voice it belongs to.
    ///
    /// Split out of SpeakPlainly because a BLOCK says an exchange a turn at
    /// a time: it waits for each to have been heard before queueing the
    /// next, and puts a beat and a nudge of the bed at the change of voice
    /// (ShowItem.Exchange, CommentaryExchange.HandoffBeat). That is the
    /// difference between two people talking and one voice reading out a
    /// transcript of two people talking - and without a block round it the
    /// turns still go out back to back exactly as they always did, which is
    /// what the loop above does.
    /// </summary>
    private void SpeakTurnPlainly(ExchangeTurn turn)
    {
        if (_settings is null)
        {
            return;
        }

        // Null for the announcer's own voice. A club that has not
        // picked a co-host voice for the engine in use hears both
        // halves in one voice, which still reads as a conversation.
        _announcer.SpeakReminder(
            turn.Line,
            null,
            turn.FromCoHost ? _settings.CoHostVoice : null,
            showOnScreens: true);
    }
}
