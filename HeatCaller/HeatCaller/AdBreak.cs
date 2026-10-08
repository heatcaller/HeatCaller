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
    private static readonly object Gate = new();
    private static int _playing;

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
    /// marshals for itself.
    /// </summary>
    internal static event Action<bool>? PlayingChanged;

    /// <summary>A sponsor video with sound has started. Pair every call with <see cref="End"/>.</summary>
    internal static void Begin()
    {
        bool changed;

        lock (Gate)
        {
            changed = _playing++ == 0;
        }

        if (changed)
        {
            PlayingChanged?.Invoke(true);
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
        bool changed;

        lock (Gate)
        {
            if (_playing == 0)
            {
                return;
            }

            changed = --_playing == 0;
        }

        if (changed)
        {
            PlayingChanged?.Invoke(false);
        }
    }

    /// <summary>
    /// Back to nothing playing, for the test run. Static state outlives a
    /// test, and a test that began a break without ending one would mute the
    /// music in every test that followed.
    /// </summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            _playing = 0;
        }
    }
}
