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
    private static readonly object Gate = new();
    private static int _running;

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
    /// handler that touches a form marshals for itself.
    /// </summary>
    internal static event Action<bool>? PlayingChanged;

    /// <summary>The show's music has started. Pair every call with <see cref="End"/>.</summary>
    internal static void Begin()
    {
        bool changed;

        lock (Gate)
        {
            changed = _running++ == 0;
        }

        if (changed)
        {
            PlayingChanged?.Invoke(true);
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
        bool changed;

        lock (Gate)
        {
            if (_running == 0)
            {
                return;
            }

            changed = --_running == 0;
        }

        if (changed)
        {
            PlayingChanged?.Invoke(false);
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
        }
    }
}
