namespace HeatCaller;

/// <summary>
/// Says when the show's own music is playing, so the club's music can get
/// out of its way.
///
/// Static, and an event rather than a parameter, for exactly the reason
/// AdBreak is: the show is run by the announcer screen's director, and the
/// music player belongs to MainForm several levels up. Threading a "the
/// show is on" callback down that chain would mean changing constructors
/// that have no opinion about music. Here the show says what is happening
/// and MainForm, which owns the audio, decides what to do about it.
///
/// A counter rather than a flag, like AdBreak's: nothing today runs two
/// shows at once, but a second one starting while the first was winding up
/// would otherwise let the first one's ending bring the club's music back
/// underneath it.
///
/// WHAT THIS DOES NOT TOUCH. The crowd bed. The club's music is silenced
/// because two pieces of music at once is a mess; crowd noise under a show
/// open is atmosphere, and the whole point of the segment. It ducks under
/// the voice as it always has.
/// </summary>
internal static class ShowOnAir
{
    /// <summary>
    /// Serialises the count and the event together.
    ///
    /// The event used to be raised OUTSIDE this, which let a Begin on one
    /// thread and an End on another be counted in one order and reported in
    /// the other - leaving MainForm holding the wrong final value. When the
    /// wrong value is "playing", the club's music stays silenced for the
    /// rest of the evening, which is the worst thing that can happen here.
    /// The same bug AnnouncementService's _speakingLock was written for;
    /// this had not copied it.
    ///
    /// So the handler now runs while this is held: it must not block, and
    /// it must not wait on anything that ends a show. What is on it today
    /// only sets a flag under the music player's own lock, and nothing
    /// under that lock ever calls back in here. Calling Begin or End
    /// straight back in is safe - a lock is re-entrant on the same thread -
    /// though the nested change is then reported before the outer one.
    /// </summary>
    private static readonly object Gate = new();

    private static int _running;

    /// <summary>
    /// What is on air, in words a crowd can read - "Race Recap", "Cold
    /// Open". Null when nothing is on.
    ///
    /// Here rather than threaded down to the Big Screen for the same reason
    /// the count is here: the show is run by the announcer screen's
    /// director and the Big Screen is a separate window several levels
    /// away, so the show says what is happening and whoever is interested
    /// reads it. The Big Screen's on-air treatment is the only thing that
    /// reads it today.
    ///
    /// The OUTERMOST show's caption wins, not the newest. Nothing runs two
    /// shows at once, but if a second began while the first was winding up,
    /// the first is the one the room is still hearing - and a caption that
    /// changed under a crowd mid-show would read as a fault.
    ///
    /// A NAMELESS OUTER PIECE GIVES WAY (17 Oct 2026, Bug Hunt 8, A05-1).
    /// An imaging jingle or bed counts here with no caption, and the back-
    /// from-break jingle was still winding down when the recap's show began
    /// - so the recap never got its name and the band read "THE SHOW" all
    /// through it. The first named show now takes a caption the outer piece
    /// never had; a named outer show is still never replaced.
    /// </summary>
    private static string? _caption;

    /// <summary>Whether the show's music is playing right now.</summary>
    internal static bool Playing
    {
        get
        {
            lock (Gate)
            {
                return _running > 0;
            }
        }
    }

    /// <summary>
    /// Raised when the answer to <see cref="Playing"/> changes, with the
    /// new value. Raised on whatever thread started or ended the show, so a
    /// handler that touches a form marshals for itself - and with the count
    /// held, so it must not block. See <see cref="Gate"/>.
    /// </summary>
    internal static event Action<bool>? PlayingChanged;

    /// <summary>
    /// What is on air right now, or null when nothing is - see
    /// <see cref="_caption"/>. Read it when <see cref="PlayingChanged"/>
    /// says true; it is already set by then, because both are settled
    /// inside <see cref="Gate"/> before the handler runs.
    /// </summary>
    internal static string? Caption
    {
        get
        {
            lock (Gate)
            {
                return _running > 0 ? _caption : null;
            }
        }
    }

    /// <summary>
    /// The show's music has started. Pair every call with <see cref="End"/>.
    ///
    /// caption is what the Big Screen puts on the crowd's screen while this
    /// show is on. Optional, because the count is what this class is for and
    /// a show with nothing to call itself is still a show.
    /// </summary>
    internal static void Begin(string? caption = null)
    {
        lock (Gate)
        {
            var cleaned = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();

            if (_running++ == 0)
            {
                // Set before Raise, so a handler reading Caption on the way
                // in sees this show's own name rather than the last one's.
                _caption = cleaned;

                Raise(true);
            }
            else if (_caption is null && cleaned is not null)
            {
                // See _caption: a nameless piece gives way to the first
                // named show. No event - the Big Screen reads Caption on
                // its tick.
                _caption = cleaned;
            }
        }
    }

    /// <summary>
    /// The show has finished, or failed partway through. Called from a
    /// finally as well as at the natural end, so a show that throws cannot
    /// leave the club's music silenced for the rest of the evening - and so
    /// it never counts below zero.
    /// </summary>
    internal static void End()
    {
        lock (Gate)
        {
            if (_running == 0)
            {
                return;
            }

            if (--_running == 0)
            {
                // Cleared before Raise, for the mirror of Begin's reason: a
                // handler asking what is on air while being told nothing is
                // must not be handed the show that has just ended.
                _caption = null;

                Raise(false);
            }
        }
    }

    /// <summary>
    /// Tells the handler, inside <see cref="Gate"/> - see the note there. A
    /// subscriber that throws is swallowed, exactly as AnnouncementService
    /// swallows its own: it must not take down whatever was starting or
    /// ending the show, and it must certainly not leave the count and what
    /// the handler was told disagreeing.
    /// </summary>
    private static void Raise(bool playing)
    {
        try
        {
            PlayingChanged?.Invoke(playing);
        }
        catch
        {
            // Nothing useful to do here, and leaving the club's music
            // silenced because a handler failed is the worst outcome in
            // this area.
        }
    }

    /// <summary>
    /// Back to nothing playing, for the test run. Static state outlives a
    /// test, and a test that began a show without ending one would silence
    /// the music in every test that followed.
    /// </summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            _running = 0;
            _caption = null;
        }
    }
}
