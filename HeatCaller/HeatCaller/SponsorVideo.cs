using LibVLCSharp.Shared;

namespace HeatCaller;

/// <summary>
/// The one LibVLC engine the app uses, made on first ask and kept for the
/// run.
///
/// Making it is not free - it loads libvlc.dll, libvlccore.dll and scans
/// the plugin folder beside the exe - so it is made once, lazily: a club
/// with no sponsor videos never pays for it, and the first video of the
/// night pays only once.
///
/// Nothing here throws. A publish with no libvlc\ folder beside the exe, a
/// 32-bit DLL where a 64-bit one should be, a plugin folder a virus
/// scanner has eaten - all of it comes back as a reason in plain English
/// (see <see cref="Problem"/>) and the Big Screen skips video slides
/// instead of falling over mid-event. That is the whole point of the
/// try/catch around Core.Initialize: it throws a DllNotFoundException or a
/// VLCException, and it throws it on the UI thread of a window that is on
/// a wall in front of a crowd.
/// </summary>
internal static class SponsorVideoEngine
{
    private static readonly object Gate = new();
    private static bool _tried;
    private static LibVLC? _vlc;
    private static string? _problem;

    /// <summary>
    /// Where the native LibVLC files sit, relative to the exe. This is
    /// VlcWindowsX64TargetDir in HeatCaller.csproj, which is the package's
    /// own default - the two have to agree, and PublishLayoutTests holds
    /// them together.
    ///
    /// Only win-x64 ships: the app publishes -r win-x64 (publish.cmd), and
    /// the csproj switches the package's x86 and arm64 copies off. An ARM64
    /// Windows PC runs the x64 build, and this x64 LibVLC with it, under
    /// the same emulation.
    /// </summary>
    internal const string NativeFolderName = @"libvlc\win-x64";

    /// <summary>Why video can't play, or null when it can. Only meaningful after <see cref="TryGet"/> has been asked once.</summary>
    internal static string? Problem
    {
        get
        {
            lock (Gate)
            {
                return _problem;
            }
        }
    }

    /// <summary>
    /// The engine, or null with the reason in <paramref name="problem"/>.
    /// Tried once: a failure is remembered, so a rotation with a video in
    /// it doesn't try to load the natives every time the slide comes round.
    /// </summary>
    internal static LibVLC? TryGet(out string? problem)
    {
        lock (Gate)
        {
            if (_tried)
            {
                problem = _problem;
                return _vlc;
            }

            _tried = true;

            try
            {
                var natives = Path.Combine(AppContext.BaseDirectory, NativeFolderName);

                if (!Directory.Exists(natives))
                {
                    _problem = "The video player's files are missing from beside HeatCaller.exe (" + NativeFolderName
                               + "). Reinstall HeatCaller.";
                    problem = _problem;
                    return null;
                }

                // Told exactly where to look rather than left to search.
                // LibVLCSharp's own search works from the process
                // architecture, which in a DPI-unaware WinForms app under
                // emulation is not always what landed in the folder.
                Core.Initialize(natives);

                _vlc = new LibVLC(
                    // No "VLC media player" banner drawn over the first
                    // seconds of every ad.
                    "--no-video-title-show",

                    // No on-screen display at all - a club's screen is not
                    // a media player and there is nothing to show on it.
                    "--no-osd",

                    // Nothing on stdout. A WinExe has no console, and
                    // LibVLC's logging is not something a club can act on.
                    "--quiet",

                    // One ad at a time, started by the rotation. Without
                    // this LibVLC will happily follow a container's own
                    // playlist.
                    "--no-sub-autodetect-file");

                problem = null;
                return _vlc;
            }
            catch (Exception ex)
            {
                // DllNotFoundException when the natives aren't loadable,
                // VLCException when they are the wrong architecture, and
                // anything a virus scanner does to a plugin folder.
                _problem = "HeatCaller couldn't start the video player: " + ex.Message;
                problem = _problem;
                return null;
            }
        }
    }

    /// <summary>
    /// Whether this build could play a video at all, without making the
    /// engine. Used by the Sponsors page to say so before a club adds one -
    /// asking TryGet there would load 40 MB of natives just to draw a page.
    /// </summary>
    internal static bool NativesPresent =>
        Directory.Exists(Path.Combine(AppContext.BaseDirectory, NativeFolderName));
}

/// <summary>
/// One video surface: the control the Big Screen puts on the wall, and the
/// player behind it.
///
/// The Big Screen owns one of these for its whole life and plays each video
/// slide through it, rather than making a player per slide - opening a
/// LibVLC output is the same kind of expense as opening a sound device
/// (see AudioBus) and doing it per ad would stall the screen.
///
/// Every public member is safe to call when video is unavailable: Play
/// returns false and the rotation moves on. A Big Screen on a PC where
/// LibVLC won't load behaves exactly as it did before videos existed.
/// </summary>
internal sealed class SponsorVideoSurface : IDisposable
{
    private readonly LibVLC? _vlc;
    private readonly MediaPlayer? _player;
    private readonly string? _problem;
    private bool _disposed;

    /// <summary>
    /// What to put in the window: the real video view, or an empty control
    /// standing in for it so the Big Screen's layout is the same either way
    /// and nothing has to check for null while placing controls.
    /// </summary>
    internal Control View { get; }

    internal SponsorVideoSurface()
    {
        _vlc = SponsorVideoEngine.TryGet(out _problem);

        if (_vlc is null)
        {
            View = new Control { Visible = false };
            return;
        }

        try
        {
            _player = new MediaPlayer(_vlc)
            {
                // The Big Screen's own keys (Escape to close, M for the
                // track map) reach the form through KeyPreview. LibVLC's
                // video window grabs the keyboard and the mouse by
                // default, so Escape over a playing ad would have gone to
                // VLC - which treats it as "leave fullscreen" - and never
                // reached the form.
                EnableKeyInput = false,
                EnableMouseInput = false,
            };

            var view = new LibVLCSharp.WinForms.VideoView
            {
                MediaPlayer = _player,
                Visible = false,
            };

            View = view;

            // Both of these arrive on a LibVLC thread, not the UI thread,
            // and LibVLC documents that its own methods must not be called
            // from inside its event handlers - Stop() from in here
            // deadlocks. So the only thing either does is hand the fact
            // over, and the Big Screen marshals it (see Finished).
            _player.EndReached += (_, _) => Finished?.Invoke();
            _player.EncounteredError += (_, _) => Finished?.Invoke();
        }
        catch (Exception ex)
        {
            // A VideoView that can't make its window, most likely. Same
            // outcome as no engine at all.
            _problem = "HeatCaller couldn't open the video player: " + ex.Message;
            _player?.Dispose();
            _player = null;
            View = new Control { Visible = false };
        }
    }

    /// <summary>Whether this surface can actually play something.</summary>
    internal bool Available => _player is not null && !_disposed;

    /// <summary>Why it can't, or null when it can.</summary>
    internal string? Problem => _problem;

    /// <summary>
    /// The video reached its end, or failed part way through. Raised on a
    /// LibVLC thread - a handler that touches a form must marshal.
    /// </summary>
    internal event Action? Finished;

    /// <summary>
    /// Starts a video, with or without its own sound. False when it
    /// couldn't start at all, in which case the caller shows something else
    /// - nothing is left on screen.
    /// </summary>
    internal bool Play(string path, bool withSound)
    {
        if (_player is null || _vlc is null || _disposed)
        {
            return false;
        }

        try
        {
            // Disposed as soon as Play has taken it: the player holds its
            // own reference to the media for as long as it needs it, and a
            // Media kept per slide would leak one per rotation all night.
            using var media = new Media(_vlc, path, FromType.FromPath);

            if (!withSound)
            {
                // On the MEDIA, not the player. MediaPlayer.Mute set before
                // anything is playing has no audio output to act on yet and
                // is silently forgotten, so a silent ad would have spoken
                // until the first frame arrived and then gone quiet. This
                // option is read as the media is opened, so there is no such
                // moment - it is also what LibVLCSharp's own documentation
                // gives as the example for exactly this.
                media.AddOption(":no-audio");
            }

            var started = _player.Play(media);

            if (started && withSound)
            {
                // Full scale here; what a club actually hears is Windows'
                // own volume for the app. Set after Play for the same reason
                // the mute is not: there is no output to set a level on
                // until something is playing.
                _player.Volume = 100;
            }

            return started;
        }
        catch
        {
            // An unreadable file, or a container LibVLC turned away. The
            // rotation moves to the next slide.
            return false;
        }
    }

    /// <summary>
    /// Stops whatever is playing. Safe to call when nothing is, and safe to
    /// call twice - the rotation stops the video both when it ends on its
    /// own and when the window closes under it.
    /// </summary>
    internal void Stop()
    {
        if (_player is null || _disposed)
        {
            return;
        }

        try
        {
            _player.Stop();
        }
        catch
        {
            // Nothing useful to do about a player that won't stop; the
            // window is going away regardless.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            // Detached first: a VideoView still pointed at a disposed
            // player paints from freed memory.
            if (View is LibVLCSharp.WinForms.VideoView view)
            {
                view.MediaPlayer = null;
            }

            _player?.Stop();
            _player?.Dispose();
        }
        catch
        {
            // Shutting down; nothing to report to.
        }
    }
}
