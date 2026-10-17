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
    /// How big the race in front of the announcer is - see
    /// <see cref="RaceStature"/>. Supplied by whoever knows the programme,
    /// which is MainForm; null here means the flat cap the app has always
    /// had, which is what a club with no programme keeps.
    /// </summary>
    public Func<RaceStature>? RaceSize { get; set; }

    /// <summary>
    /// Tonight's saved races, oldest first, as MainForm reads them -
    /// everything a driver's own piece is written from. Null until wired,
    /// and then there is no piece to write.
    ///
    /// HANDED IN RATHER THAN WORKED OUT HERE, because what counts as
    /// "tonight" is TonightAtTheTrack's question and it needs the round
    /// store to answer it. This control has the history and not the rounds,
    /// and a second answer to "which races were tonight" is the sort of
    /// thing that agrees all season and then disagrees about a race that
    /// started at five to midnight.
    /// </summary>
    public Func<IReadOnlyList<SessionRecord>>? RacesTonight { get; set; }

    /// <summary>
    /// The club's track layouts, for the one fact in a driver's piece that
    /// needs a store rather than a result: a lap record is only a record
    /// because TrackLayout.RecordLapMs says so. Null means no record is
    /// claimed, rather than a guess at one - see NightMoments.ForRace.
    /// </summary>
    public Func<TrackLayoutStore?>? Layouts { get; set; }

    /// <summary>
    /// What the write-ahead did tonight - intros written during the quiet
    /// between races and whether they were used. Null when nothing is
    /// wired, and then nothing is said about it.
    ///
    /// A hook rather than this page reaching for Race Control: the ledger
    /// lives there, with the thing that writes the intros, and an
    /// announcer page that knew how to find Race Control would be a second
    /// path between two pages that already have one. See
    /// WrittenAhead.Report.
    /// </summary>
    public Func<string?>? IntrosWrittenAhead { get; set; }

    /// <summary>
    /// What went out to anyone listening from home - lines sent, and the
    /// ones that did not make it in time. Null when nothing is wired.
    ///
    /// Same reasoning: the queue lives on the relay publisher, which this
    /// page has no business knowing about. See ListenAlong.Report.
    /// </summary>
    public Func<string?>? ListenedAlong { get; set; }

    /// <summary>
    /// True while the gap after the recap show belongs to On Air (17 Oct
    /// 2026): the recap has just been on air and the next race will open
    /// through On Air - see RaceControlControl.OnAirFollowsTheRecap. Null
    /// when nothing is wired, and then nothing changes.
    /// </summary>
    internal Func<bool>? OnAirOpensTheNextRace { get; set; }

    /// <summary>Whether the console has been told the director is standing down for On Air, so it is said once per gap rather than twice a second.</summary>
    private bool _saidOnAirHasTheGap;

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

            // THE PACE DETECTOR'S HISTORY, WHICH WAS NEVER WIRED
            // (15 Oct 2026). PaceWatch takes its history as three
            // callbacks so the detector is a table test, and all three
            // were assigned only in PaceWatchTests. So in the field it
            // could report "fastest of the race so far" and nothing else,
            // and the commentary could never say what a lap beat or who
            // held it - the thing PaceWatch's own class comment calls the
            // entire point of it.
            //
            // Both read on demand, like everything else this control hands
            // over: a club changes the selected track between races, and
            // tonight's races grow as the evening runs.
            //
            // The layout is the one Race Control has selected, by name -
            // which is also what scopes the night's best, see
            // PaceHistory.NightBestFor for why a lap on another layout is
            // not a figure to compare against.
            _director.Pace.LayoutRecord = () =>
                PaceHistory.LayoutRecordFor(Layouts?.Invoke()?.All, _settings.LastRaceControlTrackName);

            _director.Pace.NightBest = () =>
                PaceHistory.NightBestFor(RacesTonight?.Invoke(), _settings.LastRaceControlTrackName);

            // PersonalBestBefore IS DELIBERATELY LEFT UNSET, and that is a
            // known gap rather than an oversight - see the ROADMAP's open
            // issues.
            //
            // It wants a driver's best lap on this layout before tonight,
            // and the only thing that knows is the whole session history
            // (DriverStats walks it to build PersonalBests per layout).
            // This callback is asked once per driver per lap, so on a
            // ten-car track that is ten walks of every race ever run,
            // every lap, on a page a club has already reported as slow.
            //
            // Unset, PaceWatch falls back to the driver's own best EARLIER
            // IN THIS RACE, which its own comment allows for a driver with
            // no history and which overclaims slightly for one with. That
            // is the behaviour the app has always had; making it right
            // needs a cheap per-driver index, not a callback.
            _director.Status += line => StatusMessage?.Invoke($"{AppTime.LogStampNow()}  {line}");

            // A SEGMENT IS SETTLED WHEN IT IS SAID, NOT WHEN IT IS WRITTEN
            // (14 Oct 2026). This screen can still drop a written line - the
            // next race called to the grid, the floor taken, the hold - and
            // until now the director had already marked it said: a club
            // note spent unheard, the caps used, Repeat offering it. Every
            // way out of SpeakNextSegmentAsync, and Race Control's colour,
            // now tells it Spoken or Dropped.
            _director.CommitWhenSpoken = true;

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

            // And saved when an intro or a recap marks it, not only when a
            // segment does - otherwise a restart brought back every fact
            // those had said. Save writes AppSettings, which belongs to the
            // UI thread, and the recap marks from the post-race task.
            _commentary.HistoryMarked = () => RunOnUiThread(() => _director?.Save());

            // AND TONIGHT'S MOMENTS, which is what a promise is paid off
            // with - see NightPromises.
            //
            // Built from the two readers this control already has for a
            // driver's own piece rather than from two more of its own: a
            // second answer to "which races were tonight" is the sort of
            // thing that agrees all season and then disagrees about a race
            // that started at five to midnight.
            //
            // THE READERS ARE READ INSIDE THE LAMBDA, not captured. The
            // director is built on first use, which can be before MainForm
            // has set either of them; reading them here would have wired
            // the callback to two nulls for the life of the app, and the
            // only symptom would have been a feature that silently never
            // fired. Null at call time is simply a night with no moments.
            //
            // EVERY MOMENT, NOT THE NIGHT'S TOP TWELVE. The only reader is
            // the promise pay-off, which skips anything from a race that
            // started before the promise. Capped, the strongest moments of
            // the early races filled all twelve places by mid-evening, and a
            // promise's pay-off in the next race - a lead change, a fastest
            // lap - never made the list, so the promise lapsed in silence.
            // Still strongest first, which is the order PayOff wants.
            _director.MomentsTonight = () =>
                NightMoments.Over(RacesTonight?.Invoke(), Layouts?.Invoke(), most: int.MaxValue);

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
    /// Writes tonight's recap, plays it out once with the show round it,
    /// and keeps it as a file - see <see cref="NightRecap"/>.
    ///
    /// Answers null when it went out, and otherwise why not, in words a
    /// club can act on. The same contract as TestShowMusicAsync above, and
    /// for the same reason: the caller is a button on a settings page that
    /// has to say something either way.
    ///
    /// THE ORDER HERE IS THE DESIGN. The floor is claimed BEFORE the model
    /// is asked, because writing takes the best part of a minute and a
    /// segment that started in the middle of it would be talking over the
    /// recap's own opener by the time it arrived. The recorder is started
    /// AFTER the writing, because a recorder running through a minute of
    /// silence is a minute of silence in the file.
    /// </summary>
    public async Task<string?> PlayTonightsRecapAsync()
    {
        if (Director is not { } director || _commentary is not { IsAvailable: true })
        {
            return "The commentary isn't set up on this PC yet, so there is nothing to write the recap with.";
        }

        if (Treatment is not { } treatment)
        {
            return "The commentary isn't set up on this PC yet.";
        }

        if (_announcer.Muted)
        {
            return "The announcer is muted, so nobody would hear it and the recording would be silent. "
                + "Unmute it on the Mixer and try again.";
        }

        // AND NOT WHILE A RACE IS ON (13 Oct 2026). The mute and the floor
        // were checked and the track was not, so this could be pressed
        // during a race and would take the broadcast for four minutes of
        // "tonight's racing" over a heat that was still running - talking
        // about results as settled while cars were on the track deciding
        // them.
        //
        // Staging and the countdown count as on: both are seconds from a
        // green, and a piece that long cannot be let go of once it starts.
        // Finished does NOT - the flag is out, and after the last race is
        // exactly when this is for.
        if (_race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
        {
            return "A race is on. Wait for the flag and try again - this is for between races and after the last one.";
        }

        if (ShowFloor.Busy)
        {
            return $"{ShowFloor.HeldBy ?? "Something else"} is on air. Wait for it to finish and try again.";
        }

        // The director's own segments never claim the floor, so a block
        // between races - opener, bed and all - leaves it free. Opened
        // over one, this would be two openers and two beds at once.
        if (_segmentInFlight || ShowOnAir.Playing)
        {
            return "A commentary segment is on air. Wait for it to finish and try again.";
        }

        var facts = director.NightFacts();

        if (facts.Empty)
        {
            return "There is nothing filed for tonight yet, so there is no night to recap. "
                + "Run the races first - this is for after the last one.";
        }

        // For as long as the recap can hold it, not the floor's ordinary
        // six minutes - see NightRecap.LongestHold.
        if (!ShowFloor.TryClaim(TheNightRecap, NightRecap.LongestHold))
        {
            return $"{ShowFloor.HeldBy ?? "Something else"} has the broadcast. Wait for it to finish and try again.";
        }

        BookendRecording? recording = null;

        // CALLED OFF WHEN THE NEXT RACE IS (14 Oct 2026). The checks above
        // are all made before the model is asked, and the writing takes up
        // to three minutes - long enough for the next race to be called to
        // the grid. Cancelled the moment it is, so the writing stops, the
        // show is cut short rather than playing its ending over the
        // countdown, and the read is not waited out. See NextRaceCalled.
        using var nextRace = new CancellationTokenSource();
        Action<RaceState> onNextRace = state => NextRaceCalled(state, nextRace);

        _race.StateChanged += onNextRace;

        try
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [show] Writing tonight's recap...");

            // AND WHAT THE NIGHT CAME TO, said here because this is the
            // moment a club asks for the night to be summed up - so it is
            // the moment they will read it.
            //
            // The commonest way this feature disappoints is silence nobody
            // can explain, and until 12 Oct 2026 the two commonest reasons
            // for it were written down nowhere: a segment with nothing to
            // build from and a segment whose facts were all spent both
            // returned quietly. A club reading the log saw a handful of
            // refusals and could not tell whether the quiet was three
            // segments or thirty. See NightAccount.Report, which also names
            // the setting that would release each.
            if (director.Account.Report() is { Length: > 0 } account)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [brain] {account}");
            }

            // AND WHAT THE NIGHT PROMISED, on a line of its own rather than
            // folded into the account above. The two count different
            // things: the account counts segments the budget weighed, and
            // this counts subjects the broadcast pointed the room at, which
            // is a figure a club reading "said 14" cannot work out from it.
            // See NightPromises.Report.
            if (director.Promises.Report() is { Length: > 0 } promised)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [brain] {promised}");
            }

            // AND WHAT WAS WRITTEN IN ADVANCE. The write-ahead is the one
            // part of this the club paid the model for and never saw: an
            // intro written in the quiet between races is indistinguishable
            // from one written on the spot, which is the point of it - and
            // means a club with a bad night of it has no way of telling
            // that half the spend went on intros that were thrown away.
            // See WrittenAhead.Report, which names each reason.
            if (IntrosWrittenAhead?.Invoke() is { Length: > 0 } ahead)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [brain] {ahead}");
            }

            // AND WHAT THE PEOPLE AT HOME HEARD, for the same reason: a
            // club cannot see the other end of this at all. A listener who
            // heard gaps all evening and a club whose log says every line
            // went are two people who cannot have the same conversation
            // without this line. See ListenAlong.Report.
            if (ListenedAlong?.Invoke() is { Length: > 0 } heard)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [show] {heard}");
            }

            var script = await _commentary.BuildNightRecapAsync(facts, nextRace.Token).ConfigureAwait(true);

            // ASKED AGAIN NOW THE WRITING IS BACK (14 Oct 2026). Everything
            // checked before the model was asked can have changed in the
            // minutes it took: a race on track, the floor broken or taken,
            // a segment already on air. Refused and said, rather than read
            // over the top of any of them.
            if (WhyNotAfterWriting(TheNightRecap, nextRace) is { } whyNot)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [show] Tonight's recap was written but not played - {whyNot}");

                return $"Tonight's recap was written but not played - {whyNot}";
            }

            // AND THE HOST'S OWN SIGN-OFF ON THE END (14 Oct 2026), added
            // here rather than written by the model - see
            // NightRecap.SignOff. Chunks takes it as its own last piece,
            // past the backstop that drops an overrunning recap's tail, so
            // the show ends with the host naming himself and the show on
            // every night including the ones where the model wrote too
            // much or stopped early.
            var chunks = NightRecap.Chunks(script, _commentary.NightRecapSignOff());

            if (chunks.Count == 0)
            {
                return "The model did not write anything usable. Try again, or check the Connection card above.";
            }

            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [show] Tonight's recap: {Plural.Of(chunks.Count, "piece")} to read, "
                + $"about {SponsorCopy.SpokenLength(script!).TotalSeconds:0} seconds. Playing it now.");

            // Started once there is something to record. See the note above.
            recording = BookendRecording.StartForTheNight(line => StatusMessage?.Invoke(line));

            // THE SIGN-OFF'S OWN TREATMENT, because that is what this is
            // shaped like: the opener in front, the bed under it and the
            // playout at the end. Treats() is asked first, so a club that
            // has unticked the sign-off's music gets the recap read plainly
            // rather than the one piece of music it said it did not want.
            //
            // RunLongAsync rather than RunAsync: a show winds itself up
            // after four minutes and a recap at its limit is five, so the
            // ordinary ceiling brought the music up under the last of it
            // and closed the recording's file there. See
            // NightRecap.LongestRead.
            // THE BAND SAYS WHAT THIS IS, not what it borrowed (13 Oct
            // 2026). The treatment is the sign-off's on purpose; the NAME
            // was coming along with it, so the Big Screen read "Sign-Off"
            // for four minutes of the night being recapped. A crowd reads
            // that band for a plain statement of what is happening.
            if (treatment.Treats(SegmentKind.SignOffNumbers)
                && await treatment.RunLongAsync(
                    () => SayTheRecap(chunks),
                    SegmentKind.SignOffNumbers,
                    NightRecap.LongestRead,
                    caption: NightRecap.OnTheBand,
                    ct: nextRace.Token).ConfigureAwait(true))
            {
                return null;
            }

            // RunLongAsync answers false when there was no music to put
            // round it - and in that case it has NOT spoken, so this must.
            SayTheRecap(chunks);

            // AND THEN WAITED FOR, which this path did not do and the one
            // above always has (12 Oct 2026).
            //
            // SpeakReminder queues and returns. So on a club with no music
            // files the two lines above said "play the recap" and fell
            // straight through to the finally below, which finished the
            // recording and released the broadcast floor before a word had
            // been spoken: a nought-byte mp3 reported as "no sound came out
            // of HeatCaller at all", and the floor handed to whatever asked
            // for it next while the recap was still being read. The music
            // path was right because RunLongAsync waits for quiet inside
            // itself; the two were one line apart and behaved completely
            // differently.
            await treatment.WaitForTheReadAsync(NightRecap.LongestRead, nextRace.Token).ConfigureAwait(true);

            return null;
        }
        catch (OperationCanceledException) when (nextRace.IsCancellationRequested)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [show] Tonight's recap was cut off - the next race was called to the grid.");

            return "The next race was called to the grid, so tonight's recap was cut off. Play it again after the flag.";
        }
        catch (Exception ex)
        {
            return $"Couldn't play tonight's recap: {ex.Message}";
        }
        finally
        {
            _race.StateChanged -= onNextRace;

            recording?.Finish(line => StatusMessage?.Invoke(line));

            ShowFloor.Release(TheNightRecap);
        }
    }

    /// <summary>What the night's recap calls itself while it holds the broadcast floor - see ShowFloor.</summary>
    private const string TheNightRecap = "tonight's recap";

    /// <summary>What a driver's own piece calls itself while it holds the floor.</summary>
    private const string ADriversNight = "a driver's own piece";

    /// <summary>
    /// ONE DRIVER'S NIGHT, written and played (12 Oct 2026) - see
    /// <see cref="DriverCut"/> for what it is.
    ///
    /// Answers null when it went out, and otherwise why not, in words a
    /// club can act on - the same contract PlayTonightsRecapAsync holds to
    /// and for the same reason: the caller is a button that has to say
    /// something either way.
    ///
    /// ONE DRIVER AT A TIME, ON DEMAND, and that is a decision rather than
    /// a limitation of the loop. Twenty drivers at ninety seconds each is
    /// half an hour of a hall listening to pieces that are not about them.
    /// The only path from a written line to audio in this app is the voice
    /// queue with a recorder round it - the roadmap records that choice and
    /// why the silent render was not built - so a piece gets made by being
    /// PLAYED. Until something can render one without the room hearing it,
    /// a club plays the ones it wants: at a presentation, or while the
    /// place is being packed up.
    ///
    /// THE ORDER IS THE RECAP'S ORDER, for its reasons. The floor is
    /// claimed before the model is asked, because writing takes the best
    /// part of a minute and a segment that started inside it would be
    /// talking over this piece's own opener. The recorder starts after the
    /// writing, because a recorder running through that minute puts a
    /// minute of silence at the top of the file.
    /// </summary>
    /// <param name="transponder">The driver, as their results key on.</param>
    /// <param name="driverName">Their name, as it should be said and as the file is named.</param>
    public async Task<string?> PlayADriversNightAsync(string? transponder, string? driverName)
    {
        if (_commentary is not { IsAvailable: true })
        {
            return "The commentary isn't set up on this PC yet, so there is nothing to write the piece with.";
        }

        if (Treatment is not { } treatment)
        {
            return "The commentary isn't set up on this PC yet.";
        }

        var named = (driverName ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(transponder) || named.Length == 0)
        {
            return "Pick a driver first.";
        }

        if (_announcer.Muted)
        {
            return "The announcer is muted, so nobody would hear it and the recording would be silent. "
                + "Unmute it on the Mixer and try again.";
        }

        if (RacesTonight?.Invoke() is not { Count: > 0 } tonight)
        {
            return "There is nothing filed for tonight yet, so there is no night to write about. "
                + "Run the races first.";
        }

        var facts = DriverCut.FactsFor(transponder, named, tonight, Layouts?.Invoke());

        if (facts.Empty)
        {
            return $"{named} has no races filed for tonight, so there is nothing to write about.";
        }

        // AND NOT WHILE A RACE IS ON (13 Oct 2026). The mute and the floor
        // were checked and the track was not, so this could be pressed
        // during a race and would take the broadcast for four minutes of
        // "tonight's racing" over a heat that was still running - talking
        // about results as settled while cars were on the track deciding
        // them.
        //
        // Staging and the countdown count as on: both are seconds from a
        // green, and a piece that long cannot be let go of once it starts.
        // Finished does NOT - the flag is out, and after the last race is
        // exactly when this is for.
        if (_race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
        {
            return "A race is on. Wait for the flag and try again - this is for between races and after the last one.";
        }

        if (ShowFloor.Busy)
        {
            return $"{ShowFloor.HeldBy ?? "Something else"} is on air. Wait for it to finish and try again.";
        }

        // The director's own segments never claim the floor, so a block
        // between races - opener, bed and all - leaves it free. Opened
        // over one, this would be two openers and two beds at once.
        if (_segmentInFlight || ShowOnAir.Playing)
        {
            return "A commentary segment is on air. Wait for it to finish and try again.";
        }

        // As long as the recap's own hold, being read the same way - see
        // NightRecap.LongestHold.
        if (!ShowFloor.TryClaim(ADriversNight, NightRecap.LongestHold))
        {
            return $"{ShowFloor.HeldBy ?? "Something else"} has the broadcast. Wait for it to finish and try again.";
        }

        BookendRecording? recording = null;

        // Called off when the next race is, for the reason the recap's own
        // path gives - see NextRaceCalled.
        using var nextRace = new CancellationTokenSource();
        Action<RaceState> onNextRace = state => NextRaceCalled(state, nextRace);

        _race.StateChanged += onNextRace;

        try
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [show] Writing {named}'s night...");

            var script = await _commentary.BuildDriverCutAsync(facts, nextRace.Token).ConfigureAwait(true);

            // Asked again now the writing is back - see the recap's path.
            if (WhyNotAfterWriting(ADriversNight, nextRace) is { } whyNot)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [show] {named}'s night was written but not played - {whyNot}");

                return $"{named}'s night was written but not played - {whyNot}";
            }

            var chunks = DriverCut.Chunks(script);

            if (chunks.Count == 0)
            {
                return "The model did not write anything usable. Try again, or check the Connection card above.";
            }

            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [show] {named}'s night: {Plural.Of(chunks.Count, "piece")} to read, "
                + $"about {SponsorCopy.SpokenLength(script!).TotalSeconds:0} seconds. Playing it now.");

            recording = BookendRecording.StartForADriver(named, line => StatusMessage?.Invoke(line));

            // THEIR OWN NIGHT, AS IT SOUNDED (13 Oct 2026). A cold open:
            // a line of handoff, then a few seconds of the race audio this
            // driver was actually called over, and then the opener and the
            // written piece.
            //
            // IN FRONT RATHER THAN INSIDE, which is a constraint and not a
            // preference. RunLongAsync below opens the music, speaks and
            // plays out in one call and returns true when it has; there is
            // no addressable moment inside it, and SayTheRecap queues every
            // chunk without awaiting any of them, so "between the second
            // and third chunk" is not a place this code can stand. In front
            // of all of it is a place, and it is also the right one: a cold
            // open before the title music is what this shape of thing has
            // always been.
            //
            // AFTER THE RECORDER STARTS, so the clip is in the saved file.
            // That is the whole reason it goes through AudioBus rather than
            // being stitched on - see DriversOwnClip.
            await PlayTheirOwnClipAsync(treatment, named).ConfigureAwait(true);

            // THE SIGN-OFF'S TREATMENT, like the night's recap: this is the
            // same shape of thing, an opener in front and a playout at the
            // end. Treats() is asked first so a club that has unticked the
            // sign-off's music gets the piece read plainly rather than the
            // one piece of music it said it did not want.
            // And the band says whose night it is rather than "Sign-Off",
            // for the reason the recap's own path gives. This one was the
            // worse of the two: a piece about one driver, with their name
            // in every sentence of it, under a band reading "Sign-Off".
            if (treatment.Treats(SegmentKind.SignOffNumbers)
                && await treatment.RunLongAsync(
                    () => SayTheRecap(chunks),
                    SegmentKind.SignOffNumbers,
                    NightRecap.LongestRead,
                    caption: DriverCut.OnTheBand(driverName),
                    ct: nextRace.Token).ConfigureAwait(true))
            {
                return null;
            }

            SayTheRecap(chunks);

            // AND WAITED FOR, for the reason the recap's own path states at
            // length: SpeakReminder queues and returns, so without this the
            // finally below would close the file and free the floor before a
            // word had been said.
            await treatment.WaitForTheReadAsync(NightRecap.LongestRead, nextRace.Token).ConfigureAwait(true);

            return null;
        }
        catch (OperationCanceledException) when (nextRace.IsCancellationRequested)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [show] {named}'s night was cut off - the next race was called to the grid.");

            return $"The next race was called to the grid, so {named}'s night was cut off. Play it again after the flag.";
        }
        catch (Exception ex)
        {
            return $"Couldn't play {named}'s night: {ex.Message}";
        }
        finally
        {
            _race.StateChanged -= onNextRace;

            recording?.Finish(line => StatusMessage?.Invoke(line));

            ShowFloor.Release(ADriversNight);
        }
    }

    /// <summary>
    /// Calls a long piece off when the race moves towards the next green -
    /// staged, counted down or started. Raised on whatever thread the race
    /// session raises it on; cancelling is safe from any of them.
    /// </summary>
    private static void NextRaceCalled(RaceState state, CancellationTokenSource nextRace)
    {
        if (state is not (RaceState.Staging or RaceState.Countdown or RaceState.Racing))
        {
            return;
        }

        try
        {
            nextRace.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The piece finished between the race moving and this - nothing
            // left to call off.
        }
    }

    /// <summary>
    /// Why a long piece that has just come back from the model must not be
    /// played now, or null when it may - the checks made before the model
    /// was asked, made again (14 Oct 2026). The writing takes minutes, and
    /// a recap that reads four minutes of "tonight's racing" over a heat
    /// that went green while it was written is the thing those checks were
    /// there to stop.
    /// </summary>
    /// <param name="who">What the piece calls itself on the broadcast floor.</param>
    /// <param name="nextRace">Cancelled when the next race was called while it was written.</param>
    private string? WhyNotAfterWriting(string who, CancellationTokenSource nextRace)
    {
        if (nextRace.IsCancellationRequested
            || _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
        {
            return "a race was called while it was being written. Wait for the flag and try again.";
        }

        // Still ours. Asking HeldBy also breaks a claim that has gone
        // stale, which is the honest answer: if it went stale, something
        // else may already have started on the floor.
        if (!string.Equals(ShowFloor.HeldBy, who, StringComparison.Ordinal))
        {
            return "the broadcast was taken by something else while it was being written. Wait for it to finish and try again.";
        }

        if (_segmentInFlight || ShowOnAir.Playing)
        {
            return "a commentary segment went on air while it was being written. Wait for it to finish and try again.";
        }

        return null;
    }

    /// <summary>
    /// The cold open of a driver's own piece: a line of handoff and then a
    /// few seconds of the race audio they were called over.
    ///
    /// NOTHING HERE CAN TAKE THE PIECE DOWN, which is why the whole of it
    /// sits in one catch. A clip is a courtesy. Every way it can fail - no
    /// recording tonight, the folder gone, a file half-written, a disk that
    /// will not read, no output device at all - leaves the piece exactly as
    /// it was before this existed: written, read and recorded, without the
    /// twenty seconds in front of it.
    ///
    /// THE CLUB'S OWN SWITCH is read, and only the one that is about this:
    /// DriverHighlights, "cut a clip for each driver". A club that has
    /// unticked that is saying it does not want driver clips, so this does
    /// not go looking for last hour's. The RECORDING switch is deliberately
    /// NOT read: a club that unticks recording while packing up has not said
    /// anything about the clip that already exists, and reading it would
    /// have cost them this.
    ///
    /// THE LINE FIRST, because without it a hall hears twenty seconds of its
    /// own race audio out of nowhere and reads it as a fault. It goes
    /// through SpeakReminder like every other line, so it is on the screens
    /// and in the recording with the rest.
    ///
    /// AND WAITED FOR. SpeakReminder queues and returns, so without the wait
    /// the clip would start over the top of the line introducing it.
    /// </summary>
    private async Task PlayTheirOwnClipAsync(ShowTreatment treatment, string named)
    {
        try
        {
            var store = RaceAudioStore.Load();

            if (!store.DriverHighlights)
            {
                return;
            }

            // Tonight's folder, by the night it is rather than the calendar
            // date - a meeting that runs past midnight is one night, and
            // FolderForNight is the one place that decision lives.
            if (DriversOwnClip.FindFor(store.FolderForNight(DateTime.Now), named) is not { } path)
            {
                return;
            }

            var clip = ShowClip.Load(path, out var problem);

            if (clip is null)
            {
                // Said rather than swallowed: a club that has the clips
                // switched on and never hears one should be able to find out
                // why from the log.
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [show] {named}'s clip couldn't be read ({problem}) - "
                    + "the piece is being read without it.");

                return;
            }

            var seconds = Math.Min(DriversOwnClip.MostSeconds, clip.Length.TotalSeconds);

            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [show] Opening {named}'s piece with {seconds:0} seconds of "
                + $"{Path.GetFileName(path)}.");

            _announcer.SpeakReminder(
                $"First, a few seconds of {named}'s night as it sounded trackside.",
                showOnScreens: true);

            await treatment.WaitForTheReadAsync().ConfigureAwait(true);

            await AudioBus.PlayAsync(new DriversOwnClip.Excerpt(clip), DriversOwnClip.Level).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [show] Couldn't play {named}'s own clip ({ex.Message}) - "
                + "the piece is being read without it.");
        }
    }

    /// <summary>
    /// Why the announcer said what it last said, for the club that wants to
    /// check it - see <see cref="NightAccount.Proof"/>.
    ///
    /// A sentence either way, because the caller is a button.
    /// </summary>
    public string TheProofOfTheLastLine()
    {
        if (Director is not { } director)
        {
            return "The commentary isn't set up on this PC yet.";
        }

        if (director.Account.Proof() is not { Length: > 0 } proof)
        {
            return "The announcer has not said one of its own segments yet tonight, so there is nothing to show.";
        }

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [brain] {proof}");

        // The promises with it, because the one fact in a pack a club is
        // most likely to query is the callback - "how does it know we
        // mentioned him earlier" - and the ledger's own count is the
        // answer. See NightPromises.
        if (director.Promises.Report() is { Length: > 0 } promised)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [brain] {promised}");
        }

        return "Written out in the developer console - every fact it was given, and nothing else.";
    }

    /// <summary>
    /// Reads the recap out, a piece at a time.
    ///
    /// Each piece is queued like any other spoken line, so a flag call can
    /// still jump in front of whatever of it is unspoken - which on a
    /// night that turns out not to be quite over matters more than the
    /// recap does.
    ///
    /// Every piece but the last is marked as having another hard behind
    /// it, so the pauses inside the recap are the pauses inside a
    /// paragraph rather than the full stop the voice queue puts after a
    /// line on its own. See AnnouncementService.SpeechTail.
    /// </summary>
    private void SayTheRecap(IReadOnlyList<string> chunks)
    {
        for (var at = 0; at < chunks.Count; at++)
        {
            var run = CommentaryExchange.ExchangeRun.At(at, chunks.Count);

            _announcer.SpeakReminder(
                chunks[at],
                showOnScreens: true,
                tail: run.AnotherFollows
                    ? AnnouncementService.SpeechTail.BackToBack
                    : AnnouncementService.SpeechTail.Normal,
                lead: run.FollowsAnother
                    ? AnnouncementService.SpeechLead.FollowsOn
                    : AnnouncementService.SpeechLead.Normal,
                // Every piece, not just the first: a race director reading
                // the strip two minutes in has to be told it is still the
                // recap. The Big Screen's own band says the same thing -
                // see NightRecap.OnTheBand, which is deliberately this and
                // not the recording's file name.
                onAir: NightRecap.OnTheBand);
        }
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
        if (Director is not { } director || _settings is null)
        {
            return;
        }

        // The paid reads go out with the AI layer off as well - they are
        // typed by a person and need no model, and the Sponsors page says
        // so. They were behind this gate with everything else, so a club
        // without the commentary switched on never got one read all night.
        if (!_settings.CommentaryEnabled || _commentary?.IsAvailable != true)
        {
            FeedTheSponsorsOnly(director);
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

        // HOW BIG THIS RACE IS, on every tick rather than at the green.
        //
        // On the tick because it has to be right BEFORE the race as well
        // as during it: the intro, the grid history and the form guide all
        // go out while the grid is forming, and they are part of what the
        // pacing is about. And on every tick rather than once, because the
        // round under the announcer changes while the app runs - a club
        // picks a different one, seeds the mains mid-evening, or drops back
        // to a freeform race.
        // The club's own dial, read on the tick like everything else here,
        // so turning it on the Commentary page while a race is on takes
        // effect at the next segment rather than at the next race.
        director.Chatter = _settings.CommentaryChatter ?? string.Empty;

        if (RaceSize?.Invoke() is { } size && director.Stature != size)
        {
            director.Stature = size;

            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [brain] The next race is {size.Describe} - up to "
                + $"{Plural.Of(ShowChatter.Scaled(size.AirtimeCap, director.Chatter), "spoken segment")} in ten minutes"
                + $" at {ShowChatter.For(director.Chatter).Name}"
                + (size.WorthTwoVoices || ShowChatter.TwoVoicesEverywhere(director.Chatter)
                    ? ", two voices where a segment suits them."
                    : ", one voice."));
        }

        // A new session number means a new race, whatever happened to the
        // one before it - finished, reset, or abandoned halfway.
        if (racing && _race.SessionGeneration != _directorGeneration)
        {
            _directorGeneration = _race.SessionGeneration;
            _lapsFed.Clear();

            // The session number alone repeats after a restart - it starts
            // at 0 in every launch - and tonight's spent facts are restored
            // across one, so the mains would find "pole:5" already said by
            // a heat. The time of the green makes it this race's own.
            director.RaceStarted(
                $"{_race.SessionGeneration}@{DateTime.Now.Ticks}",
                RaceClassName?.Invoke());
        }
        else if (!racing && _directorGeneration >= 0)
        {
            _directorGeneration = -1;

            director.RaceFinished();
        }

        // The next race on its way closes the gap after the last one, so no
        // paid read lands on the grid. See CommentaryDirector.ReadsBelongHere.
        if (_race.State is RaceState.Staging or RaceState.Countdown)
        {
            director.RaceComing();
        }

        if (racing)
        {
            var ordered = RacingField(_race.GetOrderedStandings());

            // With what the horizon needs on top of the pairs: the leader's
            // laps and how much road is left - exact under a lap limit,
            // worked out from the clock in a timed race. See
            // RaceHorizon.LapsLeftIn and RoadFor below.
            director.Standings(PairsFrom(ordered), ordered.Count > 0 ? ordered[0].Laps : 0, RoadFor(ordered));
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

        // AND NOT WHILE THE PROGRAMME HAS THE FLOOR (11 Oct 2026).
        //
        // Race Control's post-flag sequence - teaser, recap, honours, the
        // mechanic, the sign-off - runs for minutes as one fire-and-forget
        // task, and a race being Finished is not "between races" while it
        // is still going. The guard below is _segmentInFlight, which only
        // ever knew about THIS chain, so the tick cheerfully started a
        // second show over the top of the recap's: on 11 Oct it opened the
        // night's sign-off nine seconds into the recap's bed, and the hall
        // heard two openers and two beds at once.
        //
        // Nothing is lost by standing down. A segment not said is unspent,
        // and the gap after a recap is long - see ShowFloor, which explains
        // why this is a claim rather than a lock.
        if (ShowFloor.Busy)
        {
            return;
        }

        // AND NOT IN THE GAP ON AIR IS ABOUT TO FILL (17 Oct 2026).
        //
        // Reported as too many hand-overs a race: the recap show signed off
        // into its bumper, then this put "still to come, the A-main" or a
        // Coming Up into the quiet, and then On Air's opening said it all
        // again. Three goodbyes-and-hellos between two races.
        //
        // Stood down for the whole pass rather than for the two segments
        // alone, which is the narrowest SAFE version: a Coming Up refused
        // after it was written is given back unspent and asked for again on
        // the next tick - a model write twice a second for nothing. And only
        // while it is true: the recap was put on air for the race still on
        // the board, that race is finished, and Start races On Air is
        // ticked. A reset, a Stage or unticking the box ends it, and a
        // hand-over refused here is not marked as given, so it still goes
        // out if the box is unticked. See RaceControlControl.OnAirFollowsTheRecap.
        if (OnAirOpensTheNextRace?.Invoke() == true)
        {
            if (!_saidOnAirHasTheGap)
            {
                _saidOnAirHasTheGap = true;

                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [brain] Standing down until the next race - the recap has just been on air and On Air "
                    + "opens the next one, so no hand-over or Coming Up in between.");
            }

            return;
        }

        _saidOnAirHasTheGap = false;

        _ = SpeakNextSegmentAsync(director);
    }

    /// <summary>
    /// The sponsors' paid reads on their own, for a night with no AI
    /// commentary - the same reads, in the same gaps, asked the same way
    /// SpeakNextSegmentAsync asks for them when the ladder has nothing.
    ///
    /// The race state is checked here rather than left to the director,
    /// because nothing feeds the director the green and the flag on this
    /// path - its own idea of whether a race is on is whatever it was when
    /// the commentary was last on.
    /// </summary>
    private void FeedTheSponsorsOnly(CommentaryDirector director)
    {
        var day = RaceDayId?.Invoke() ?? string.Empty;

        if (day.Length == 0 || _segmentInFlight || ShowFloor.Busy)
        {
            return;
        }

        if (_race.State is not (RaceState.Idle or RaceState.Finished))
        {
            return;
        }

        director.NewMeeting(day);

        // Switched off mid-race, the director was told the green and never
        // the flag, and would refuse every read for the rest of the night.
        if (_directorGeneration >= 0)
        {
            _directorGeneration = -1;

            director.RaceFinished();
        }

        // Caught as SpeakNextSegmentAsync catches it: this runs on the
        // page's timer, and nothing about an advert is worth the window.
        try
        {
            SayTheSponsorsReadIfThereIsOne(director);
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [sponsor] {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Every adjacent pair in the running order, as BattleWatch wants
    /// them.
    ///
    /// Gaps at the line, from RaceGaps, never by comparing lap counts -
    /// those differ for everybody the moment the leader crosses. A pair
    /// whose gap cannot be measured is handed over as NaN, never as a
    /// number: the detector neither starts a fight on it nor ends one.
    ///
    /// It used to be handed over as miles apart, and that ended the very
    /// fights this segment is for. The moment one car passes another and
    /// crosses the line first, the pair is unmeasurable until the other
    /// crosses too (RaceGaps finds a negative gap and returns none), so a
    /// real pass wiped out the battle it should have been the highlight
    /// of and its lead change was never counted.
    /// </summary>
    internal static IReadOnlyList<PairGap> PairsFrom(IReadOnlyList<RacerStanding> ordered)
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
                gap.GapMs is { } ms ? ms / 1000.0 : double.NaN,
                gap.LapsDown));
        }

        return pairs;
    }

    /// <summary>
    /// How much road the race in front of the announcer has left, for the
    /// horizon - or null when neither a lap limit nor a clock says.
    ///
    /// The leader's pace is the middle of their last three PACE laps, the
    /// same laps the session counts for best lap (IsPaceLap), so a start
    /// lap or a lap through a crash recovery cannot shrink the estimate.
    /// </summary>
    private RoadLeft? RoadFor(IReadOnlyList<RacerStanding> ordered)
    {
        if (ordered.Count == 0)
        {
            return null;
        }

        var leader = ordered[0];

        // Snapshotted, as FeedLaps does: the decoder's thread appends to it.
        var laps = leader.LapTimesMs;
        var count = laps.Count;
        var pace = new List<double>(3);

        for (var i = count - 1; i >= 0 && pace.Count < 3; i--)
        {
            if (i < laps.Count && IsPaceLap(leader, i))
            {
                pace.Insert(0, laps[i] / 1000.0);
            }
        }

        // How far into the lap in progress the leader is: race time now,
        // less race time at their last crossing (ElapsedMs carries their
        // penalties, which are not track time).
        var elapsed = _race.ElapsedRaceTime;
        var crossedAt = TimeSpan.FromMilliseconds(Math.Max(0, leader.ElapsedMs - leader.PenaltyMs));
        var since = elapsed > crossedAt ? elapsed - crossedAt : TimeSpan.Zero;

        return RaceHorizon.LapsLeftIn(
            _race.LapLimit,
            leader.Laps,
            _race.TimeLimit,
            elapsed,
            RaceHorizon.RecentPace(pace),
            since);
    }

    /// <summary>
    /// The running order as the commentary should see it: every ghost left
    /// out while a real car is racing, as RaceLeader and the race's own
    /// fastest lap leave them out. A field of nothing but ghosts is still
    /// talked about.
    ///
    /// Without this a PB ghost was half of "a fight for the lead", pushed
    /// the real leader down to second, and had its replayed laps called the
    /// fastest of the race.
    /// </summary>
    internal static IReadOnlyList<RacerStanding> RacingField(IReadOnlyList<RacerStanding> ordered)
    {
        if (!ordered.Any(s => !Ghosts.IsGhost(s.TransponderId)))
        {
            return ordered;
        }

        return ordered.Where(s => !Ghosts.IsGhost(s.TransponderId)).ToList();
    }

    /// <summary>
    /// Whether the lap at this index says anything about pace - the same
    /// laps the session lets compete for best lap.
    ///
    /// Not the start lap, which runs from the green to the first crossing:
    /// a 1.4 s dash from a grid just behind the loop became the session
    /// best, no real lap could beat it, and the record-pace calls stopped
    /// for the rest of the race. Nor index 1 for a car credited its start
    /// lap at the green (see RacerStanding.StartLapCredited), nor a lap
    /// that ran through a crash recovery.
    /// </summary>
    internal static bool IsPaceLap(RacerStanding standing, int index) =>
        index > 0
        && !(standing.StartLapCredited && index == 1)
        && !standing.IsRecoveryLap(index);

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
                if (!IsPaceLap(standing, i))
                {
                    continue;
                }

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

        // Outside the try so the finally can answer for it - see below.
        SpokenSegment? said = null;

        try
        {
            said = await director.NextAsync().ConfigureAwait(true);

            // THE GAP MAY HAVE GONE WHILE IT WAS WRITTEN (bug hunt 7).
            // FeedTheDirector turns away staging and a claimed floor only
            // before the pass starts, and a write takes seconds - long
            // enough for the next race to be called to the grid or for the
            // programme to take the floor. Asked again here, and before the
            // sponsor's read and the hand-over below, which would otherwise
            // go out into the same gap.
            if (!IsDisposed && WhyNotNow(said) is { } why)
            {
                if (said is not null)
                {
                    StatusMessage?.Invoke(
                        $"{AppTime.LogStampNow()}  [brain] Written, then dropped - {why}.");
                }

                return;
            }

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

            // It went out. A block the race overtook before a word of it was
            // queued has already said Dropped (RunAsShowAsync), and then
            // this does nothing.
            director.Spoken(said);
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [brain] {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            // EVERY OTHER WAY OUT IS A DROP (14 Oct 2026): the gap went while
            // it was written, the hold went on, the screen went away, or the
            // staging threw. The director settles a segment only on Spoken -
            // see CommitWhenSpoken - so this gives the rest back. Does
            // nothing for one already answered for, or for none at all.
            director.Dropped(said);

            _segmentInFlight = false;
        }
    }

    /// <summary>
    /// Why a segment that has just been written must not go out now, or
    /// null when it may. The same rules as FeedTheDirector's, asked again
    /// once the write is back, plus the one the director applies to its
    /// own order: an in-race segment only while a race is on, and a
    /// between-races one only while none is. The director drops those
    /// itself once it is told the race has changed; this covers the half
    /// second before the tick tells it.
    /// </summary>
    private string? WhyNotNow(SpokenSegment? said)
    {
        if (_race.State is RaceState.Staging or RaceState.Countdown)
        {
            return "the next race was called to the grid while it was being written";
        }

        if (ShowFloor.Busy)
        {
            return $"{ShowFloor.HeldBy ?? "the programme"} took the floor while it was being written";
        }

        if (said is not null
            && CommentaryDirector.InRace(said.Kind) != (_race.State == RaceState.Racing))
        {
            return _race.State == RaceState.Racing
                ? "the race went green while it was being written"
                : "the race finished while it was being written";
        }

        return null;
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

        // Nor over the programme - the same reason as FeedTheDirector's own
        // check, which says it at length. BEFORE _handedOver is marked
        // below: a hand-over refused because the recap is still running is
        // one that should still happen when it finishes, where a hand-over
        // whose gap was too short is not. See ShowFloor.
        if (ShowFloor.Busy)
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

        // No block round it, so the turns go out back to back - but they are
        // still synthesised one at a time unless they are rendered first,
        // and the hand-over is where that lands. See RenderTurnsAsync.
        return said.IsExchange ? SpeakRenderedAsync(said) : SpeakNow(said);

        Task SpeakNow(SpokenSegment one)
        {
            SpeakPlainly(one);
            return Task.CompletedTask;
        }

        async Task SpeakRenderedAsync(SpokenSegment one)
        {
            _renderedTurns = await RenderTurnsAsync(one).ConfigureAwait(true);
            SpeakPlainly(one);
        }
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
        // EVERY TURN RENDERED BEFORE THE BLOCK STARTS. The staging below
        // waits for each turn to be heard before queueing the next, so a
        // turn rendered late puts its network round trip in the hand-over -
        // see RenderTurnsAsync.
        if (said.IsExchange)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();

            _renderedTurns = await RenderTurnsAsync(said).ConfigureAwait(true);

            var ready = 0;

            foreach (var one in _renderedTurns)
            {
                if (one is not null)
                {
                    ready++;
                }
            }

            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [brain] {ready} of {said.Turns.Count} turns rendered ahead in "
                + $"{clock.ElapsedMilliseconds}ms - the hand-overs have nothing to wait for."
                + (ready == said.Turns.Count
                    ? string.Empty
                    : " The rest are rendered as they are reached, which leaves a gap where the voice changes."));
        }

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

        void SpeakTurn(ExchangeTurn turn, CommentaryExchange.ExchangeRun run)
        {
            spoken = true;

            // Found by reference rather than by value: two turns of an
            // exchange can legitimately read the same, and the audio is not
            // interchangeable - they are in different voices.
            var at = -1;

            for (var i = 0; i < said.Turns.Count; i++)
            {
                if (ReferenceEquals(said.Turns[i], turn))
                {
                    at = i;
                    break;
                }
            }

            // Which turn this is, so only the opening one can take a lift -
            // a co-host answering mid-conversation should not be shouting.
            SpeakTurnPlainly(
                turn,
                run,
                said.Kind,
                opening: at == 0,
                prepared: at >= 0 && at < _renderedTurns.Count ? _renderedTurns[at] : null);
        }

        // A race moved on and took the block with it. The segment is then
        // NOT said plainly afterwards: the room's attention is on the
        // track, a race call is already in front of it on the voice queue,
        // and a recap of the last race arriving over a countdown is the
        // exact thing ShowTreatment.AbandonBlock exists to stop.
        var abandoned = false;

        // RECORDING THE NIGHT'S BOOKENDS (11 Oct 2026). Every race is
        // already recorded through its own broadcast, so a recap, the
        // honours and the interview are all in that race's mp3. The cold
        // open and the sign-off were in no file at all - and they are the
        // two set pieces about the NIGHT rather than about one heat, which
        // makes them the two a club would actually post.
        //
        // Null for every segment that is not one of those two, and for a
        // club with recording switched off. See BookendRecording, which
        // also explains why it refuses to start while anything else is
        // recording.
        var taping = BookendRecording.StartFor(said.Kind, line => StatusMessage?.Invoke(line));

        // THE WHOLE OF IT INSIDE THE RECORDER'S FINALLY (12 Oct 2026), and
        // the nesting is the fix rather than tidiness.
        //
        // The plain fallback used to sit AFTER the finally that closes the
        // bookend's file - so on a club with no music files the cold open
        // and the sign-off were recorded, finished and reported before the
        // words were even queued, and every one of those mp3s was nought
        // bytes with "no sound came out of HeatCaller at all" beside it in
        // the log. Both set pieces, every night, for every club that had
        // not dropped the music in: the two files a club would actually
        // post. The music path was right all along because RunAsync waits
        // for quiet inside itself.
        //
        // The inner try keeps what the outer one cannot: a block that
        // throws is logged AND still falls through to the fallback, which
        // is what it did before.
        try
        {
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

            // A race moved on and took the block with it: said out loud in
            // the console rather than left as silence. The club paid a
            // write for this line and nobody heard it, and a quiet night
            // with no reason in the log is the thing this whole layer is
            // logged to death to avoid. NOT said plainly afterwards - the
            // room's attention is on the track and a race call is already
            // in front of it on the voice queue.
            if (abandoned && !spoken)
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [show] Written, then dropped - the race moved on before the block reached it.");

                // And the director told so, so the line's facts, marks and
                // budget are given back rather than spent unheard - see
                // CommentaryDirector.CommitWhenSpoken.
                director.Dropped(said);
            }

            // No music to play, or it failed before the lines went in, or
            // the gap had no room for a block at all - said the ordinary
            // way. The words are the part that matters, and this is exactly
            // what happened before any of the staging existed.
            if (!spoken && !abandoned)
            {
                SpeakOnce();

                // AND WAITED FOR, but only where there is a file open to
                // wait for. Every other way into this method is a
                // half-second tick that must not be held up for the length
                // of a read, and a segment with no recorder has nothing
                // that closing early could spoil.
                if (taping is not null)
                {
                    await treatment.WaitForTheReadAsync().ConfigureAwait(true);
                }
            }
        }
        finally
        {
            // In the FINALLY, so a block that threw or was abandoned still
            // closes its file off: half a broadcast on disk is worth
            // having, and a recorder left listening would hold the tap for
            // the rest of the evening and silently displace the next
            // race's own recording. Finish never throws and never waits.
            taping?.Finish(line => StatusMessage?.Invoke(line));
        }
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
        // Not while the announcer is muted. The read is counted before it
        // is queued (below) and SpeakReminder drops it while muted, so a
        // twenty-minute track repair on mute billed every sponsor a read
        // every ninety seconds that nobody heard. Asked before the director
        // so an ad break's read stays owed rather than being spent.
        if (_announcer.Muted)
        {
            return false;
        }

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

        // AND THE SPONSOR ON THE CROWD SCREEN for the length of it (12 Oct
        // 2026). The read was the one part of the rotation with nothing to
        // look at: a slide is a picture, a video is a picture that moves,
        // and the part that costs the announcer airtime left the screen on
        // the standings.
        //
        // Asked for HERE rather than read off the plan, because a read is
        // planned ahead of the gap it lands in and a picture chosen
        // minutes earlier could since have been removed. Before the words
        // are queued, so the picture is up as the voice starts.
        if (director.SponsorOnScreenFor?.Invoke(paid.SponsorId) is { } onScreen)
        {
            SponsorOnAir.Begin(
                onScreen.PictureFileName,
                onScreen.Name,
                SponsorCopy.SpokenLength(paid.Text));
        }

        _announcer.SpeakReminder(paid.Text, showOnScreens: true);

        return true;
    }

    /// <summary>Says it with no music in front of it - what every segment did before the show treatment.</summary>
    /// <summary>
    /// The audio for the turns of the exchange being spoken, index for
    /// index with them, or empty when nothing was rendered ahead.
    ///
    /// A field rather than an argument because the show block speaks its
    /// turns through a callback it owns the shape of (ShowTreatment), so
    /// there is nowhere to thread this through. Only ever one exchange is
    /// in the air at a time - the director writes one segment at a time and
    /// waits for it - so one slot is enough.
    /// </summary>
    private IReadOnlyList<AnnouncementService.PreparedSpeech?> _renderedTurns =
        Array.Empty<AnnouncementService.PreparedSpeech?>();

    private void SpeakPlainly(SpokenSegment said)
    {
        if (_settings is null)
        {
            return;
        }

        if (!said.IsExchange)
        {
            // Delivery tags go on here, at the last moment before the voice,
            // exactly where the recap puts them on
            // (RaceControlControl.AfterRace). The service decides whether
            // any are wanted - club setting, v3 voice, Enthusiastic tone -
            // and hands the line straight back when they are not.
            var spoken = _commentary?.PrepareSegmentForSpeech(said.Kind, said.Text) ?? said.Text;

            // Named as it goes out, so the strip says "Form Guide" rather
            // than the kind of call this happens to be queued as. See
            // AnnouncementService.OnAirNow.
            _announcer.SpeakReminder(spoken, showOnScreens: true, onAir: ShowBlock.NameFor(said.Kind));
            return;
        }

        for (var at = 0; at < said.Turns.Count; at++)
        {
            SpeakTurnPlainly(
                said.Turns[at],
                CommentaryExchange.ExchangeRun.At(at, said.Turns.Count),
                said.Kind,
                opening: at == 0,
                prepared: at < _renderedTurns.Count ? _renderedTurns[at] : null);
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
    /// <param name="run">
    /// Where this turn sits in the exchange, which is what trims the
    /// silence either side of it - see CommentaryExchange.ExchangeRun and
    /// AnnouncementService.SpeechLead for the second-and-a-half of dead air
    /// this took out of a two-hander.
    /// </param>
    /// <param name="kind">Which segment this turn belongs to - only the cold open and the sign-off open on a lift.</param>
    /// <param name="opening">True for the first turn of the exchange. See CommentaryService.PrepareSegmentForSpeech.</param>
    /// <summary>
    /// Renders every turn of a two-hander before any of them is spoken,
    /// each in the voice that says it.
    ///
    /// WHY AHEAD, AND NOT AS EACH TURN COMES UP (14 Oct 2026). SpeakReminder
    /// synthesises at speak time, and a show block waits for each turn to
    /// have been heard before it queues the next - so the network round trip
    /// for turn two did not even START until turn one had finished speaking,
    /// and it landed squarely in the hand-over. The beat between voices was
    /// tuned to 180ms and the pads either side were trimmed to nothing, and
    /// none of that mattered against a render sitting in the gap: the room
    /// heard two people who could not quite keep up with each other.
    ///
    /// The whole exchange is written in ONE model call, so every turn's
    /// words exist before the first is spoken - there is nothing to wait
    /// for. This is the same thing RaceControlControl.RenderHonoursAsync
    /// does for the honours hand-over, and for the same reason, which is
    /// why PrepareReminderAsync exists at all.
    ///
    /// Index for index with the turns. A null entry is a render that failed
    /// or a voice that is not configured - PrepareReminderAsync returns null
    /// for both - and that turn is spoken the old way instead, which is the
    /// fallback its own documentation asks callers to make.
    /// </summary>
    private async Task<IReadOnlyList<AnnouncementService.PreparedSpeech?>> RenderTurnsAsync(SpokenSegment said)
    {
        var rendered = new List<AnnouncementService.PreparedSpeech?>(said.Turns.Count);

        for (var at = 0; at < said.Turns.Count; at++)
        {
            var turn = said.Turns[at];

            var line = _commentary?.PrepareSegmentForSpeech(said.Kind, turn.Line, at == 0) ?? turn.Line;
            var voice = turn.FromCoHost ? _settings?.CoHostVoice : null;

            try
            {
                rendered.Add(await _announcer.PrepareReminderAsync(line, null, voice).ConfigureAwait(true));
            }
            catch (Exception ex)
            {
                // One turn that would not render is not a reason to lose the
                // segment: it is spoken the old way and the rest still flow.
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [brain] A turn of the two-hander could not be rendered ahead "
                    + $"({ex.GetType().Name}) - it will be spoken as it is reached.");

                rendered.Add(null);
            }
        }

        return rendered;
    }

    /// <param name="prepared">
    /// This turn's audio, rendered ahead by RenderTurnsAsync. Null falls
    /// through to synthesising as it is spoken, which is what used to put a
    /// network round trip in the middle of the conversation.
    /// </param>
    private void SpeakTurnPlainly(
        ExchangeTurn turn,
        CommentaryExchange.ExchangeRun run,
        SegmentKind kind,
        bool opening,
        AnnouncementService.PreparedSpeech? prepared = null)
    {
        if (_settings is null)
        {
            return;
        }

        // As in SpeakPlainly: tagged at the last moment, and only when the
        // service says tags are wanted at all.
        turn = turn with { Line = _commentary?.PrepareSegmentForSpeech(kind, turn.Line, opening) ?? turn.Line };

        var tail = run.AnotherFollows
            ? AnnouncementService.SpeechTail.BackToBack
            : AnnouncementService.SpeechTail.Normal;

        var lead = run.FollowsAnother
            ? AnnouncementService.SpeechLead.FollowsOn
            : AnnouncementService.SpeechLead.Normal;

        // The segment's name, not the speaker's: a strip that said
        // "Co-host" would be naming a voice where the useful thing is which
        // part of the programme is on. See AnnouncementService.OnAirNow.
        var onAir = ShowBlock.NameFor(kind);

        if (prepared is not null)
        {
            _announcer.SpeakPrepared(prepared, tail, lead, onAir);
            return;
        }

        // Null for the announcer's own voice. A club that has not
        // picked a co-host voice for the engine in use hears both
        // halves in one voice, which still reads as a conversation.
        _announcer.SpeakReminder(
            turn.Line,
            null,
            turn.FromCoHost ? _settings.CoHostVoice : null,
            showOnScreens: true,
            tail: tail,
            lead: lead,
            onAir: onAir);
    }
}
