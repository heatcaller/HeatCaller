namespace HeatCaller;

/// <summary>
/// Says when a sponsor video is on screen with its own sound, so the music
/// and the crowd bed can get out of its way.
///
/// Static, and an event rather than a parameter, for the same reason
/// Looks.DisplaysChanged is: the Big Screen is built by Race Control and by
/// Practice Mode (two call sites, each already passing a dozen optional
/// arguments), and the music player belongs to MainForm, three levels up.
/// Threading an "ad started" callback down that chain would mean changing
/// both call sites and both constructors to carry something neither window
/// has any opinion about. Here the Big Screen says what is happening and
/// MainForm, which owns the audio, decides what to do about it.
///
/// A sponsor video's own audio does NOT go through AudioBus or the music
/// player - LibVLC opens its own output. So nothing here can mix the two;
/// all it can do is quiet what HeatCaller is playing, which is what muting
/// the music means.
/// </summary>
internal static class AdBreak
{
    /// <summary>
    /// Serialises the count and the event together.
    ///
    /// The event used to be raised OUTSIDE this, which let a Begin on one
    /// thread and an End on another be counted in one order and reported in
    /// the other - leaving MainForm holding the wrong final value, and with
    /// the wrong value the music and the crowd bed stay muted for the rest
    /// of the evening. Two Big Screens each running their own rotation is
    /// exactly how two threads come to count at once. The same bug
    /// AnnouncementService's _speakingLock was written for; this had not
    /// copied it.
    ///
    /// So the handler now runs while this is held: it must not block, and
    /// it must not wait on anything that ends a break. What is on it today
    /// only sets a flag under the music player's and the ambience's own
    /// locks, and nothing under those ever calls back in here. Calling
    /// Begin or End straight back in is safe - a lock is re-entrant on the
    /// same thread - though the nested change is then reported before the
    /// outer one.
    /// </summary>
    private static readonly object Gate = new();

    private static int _playing;

    private static int _ended;

    /// <summary>
    /// How many ad breaks have finished since the app started.
    ///
    /// Here because a paid sales read goes out on the TAIL of a break -
    /// once the sponsor's own voiceover has stopped - and the thing that
    /// wants to know runs on its own half-second tick (see
    /// CommentaryDirector.NextAdBreakRead). A count to compare against is
    /// what that wants, for exactly the reason the laps are counted rather
    /// than subscribed to in AnnouncerControl.FeedLaps: a break is then
    /// acted on once, however many ticks it sits there for.
    ///
    /// Deliberately NOT a second event. A subscriber would be a static
    /// event holding a director that belongs to one screen for the life of
    /// the process, and it would be called inside <see cref="Gate"/> -
    /// where the one rule is that nothing may block. Nothing reads this
    /// from in there.
    ///
    /// Counted inside <see cref="Gate"/> beside Raise(false), so a reader
    /// can never see a break counted that the music has not been told
    /// about - and it does NOTHING to <see cref="_playing"/>, which is the
    /// count that decides the mute. Nothing added here can leave the club's
    /// music down for the night, which is the one failure this file exists
    /// to prevent.
    /// </summary>
    internal static int BreaksEnded
    {
        get
        {
            lock (Gate)
            {
                return _ended;
            }
        }
    }

    /// <summary>
    /// Whether a sponsor video with sound is on screen right now.
    ///
    /// A count rather than a flag: a second Big Screen window (Practice Mode
    /// can open one while Race Control's is already up) runs its own
    /// rotation, and whichever video ends first must not un-mute the music
    /// under the other one.
    /// </summary>
    internal static bool Playing
    {
        get
        {
            lock (Gate)
            {
                return _playing > 0;
            }
        }
    }

    /// <summary>
    /// Raised when the answer to <see cref="Playing"/> changes, with the new
    /// value. Raised on whatever thread began or ended the break - the Big
    /// Screen's UI thread in practice - so a handler that touches a form
    /// marshals for itself - and with the count held, so it must not block.
    /// See <see cref="Gate"/>.
    /// </summary>
    internal static event Action<bool>? PlayingChanged;

    /// <summary>A sponsor video with sound has started. Pair every call with <see cref="End"/>.</summary>
    internal static void Begin()
    {
        lock (Gate)
        {
            if (_playing++ == 0)
            {
                Raise(true);
            }
        }
    }

    /// <summary>
    /// The video has finished, been skipped, or its window has closed.
    /// Called from a finally or a Dispose as well as at the natural end, so
    /// a crash mid-ad cannot leave the music muted for the rest of the
    /// night - and so it never counts below zero.
    /// </summary>
    internal static void End()
    {
        lock (Gate)
        {
            if (_playing == 0)
            {
                return;
            }

            if (--_playing == 0)
            {
                // Before the handler, so anything the handler sets off can
                // already see the break as finished - and inside the same
                // lock, so the count and the mute can never disagree about
                // whether a break is over. See BreaksEnded.
                _ended++;

                Raise(false);
            }
        }
    }

    /// <summary>
    /// A sponsor video has finished that held no break open - one played
    /// silent, or with "silence the music" unticked. Counted as a finished
    /// break for <see cref="BreaksEnded"/> and nothing else: the ad-break
    /// read is owed for the advert, whatever the club chose about the
    /// music, and before this a club with either box off never got one.
    /// It does not touch <see cref="_playing"/> and raises nothing, so it
    /// cannot move the music.
    /// </summary>
    internal static void QuietVideoEnded()
    {
        lock (Gate)
        {
            _ended++;
        }
    }

    /// <summary>
    /// Tells the handler, inside <see cref="Gate"/> - see the note there. A
    /// subscriber that throws is swallowed, exactly as AnnouncementService
    /// swallows its own: it must not take down the window that was starting
    /// or ending the break, and it must certainly not leave the count and
    /// what the handler was told disagreeing.
    /// </summary>
    private static void Raise(bool playing)
    {
        try
        {
            PlayingChanged?.Invoke(playing);
        }
        catch
        {
            // Nothing useful to do here, and leaving the music muted
            // because a handler failed is the worst outcome in this area.
        }
    }

    /// <summary>
    /// Back to nothing playing and no breaks counted, for the test run.
    /// Static state outlives a test, and a test that began a break without
    /// ending one would mute the music in every test that followed - while
    /// one that ended a break would leave the next test's director owing a
    /// sponsor a read nobody had earned.
    /// </summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            _playing = 0;
            _ended = 0;
        }
    }
}
