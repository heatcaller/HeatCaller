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

            Speak(said);
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
    /// Says it - in two voices where the director wrote a two-hander.
    ///
    /// Queued a turn at a time in order rather than waited on one by one:
    /// the announcer's own queue keeps them in order, and anything more
    /// urgent - a staging call, a flag - goes in front of what has not
    /// been spoken yet, which is exactly what should happen to a remark
    /// about a fight when the next race is being called to the grid.
    /// </summary>
    private void Speak(SpokenSegment said)
    {
        if (_settings is null)
        {
            return;
        }

        // The show segments get an opener in front of them and a bed
        // underneath - see ShowTreatment. Fire and forget: the treatment
        // waits out the opener, queues the lines itself, and then waits
        // for the read to finish so it can play the music out. Nothing on
        // screen waits on any of that.
        //
        // RunAsync returns false when there was no music to play, having
        // deliberately NOT spoken - so the line is said the ordinary way
        // rather than lost to a club that never dropped the files in.
        if (Treatment is { } treatment && treatment.Treats(said.Kind))
        {
            _ = RunAsShowAsync(treatment, said);
            return;
        }

        SpeakPlainly(said);
    }

    /// <summary>
    /// Runs a segment as a show, falling back to saying it plainly when
    /// there turns out to be no music.
    /// </summary>
    private async Task RunAsShowAsync(ShowTreatment treatment, SpokenSegment said)
    {
        // Whether the treatment got as far as queueing the lines. A
        // segment said twice is far worse than one said without music, so
        // the fallback below only runs when nothing has been said yet.
        var spoken = false;

        void SpeakOnce()
        {
            spoken = true;
            SpeakPlainly(said);
        }

        try
        {
            await treatment.RunAsync(SpeakOnce).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [show] {ex.GetType().Name}: {ex.Message}");
        }

        if (!spoken)
        {
            // No music to play, or it failed before the lines went in -
            // said the ordinary way. The words are the part that matters.
            SpeakOnce();
        }
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

        var coHost = _settings.CoHostVoice;

        foreach (var turn in said.Turns)
        {
            // Null for the announcer's own voice. A club that has not
            // picked a co-host voice for the engine in use hears both
            // halves in one voice, which still reads as a conversation.
            _announcer.SpeakReminder(turn.Line, null, turn.FromCoHost ? coHost : null, showOnScreens: true);
        }
    }
}
