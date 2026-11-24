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
    /// Whether a background warm-up has been started - see
    /// <see cref="WarmUp"/>. 0 until the first ask, 1 from then on.
    ///
    /// WHY (23 Nov 2026, bug hunt 24): this was a bool read and written
    /// under <see cref="Gate"/>, the lock the load itself holds from start
    /// to finish. It is now only ever touched with Interlocked, so asking
    /// "is a warm-up already running" never waits for the one that is.
    /// </summary>
    private static int _warmAsked;

    /// <summary>
    /// Whether the one attempt at loading the engine has FINISHED, either
    /// way. Not the same thing as <see cref="_tried"/>, which goes true at
    /// the START of the attempt: between the two there is a thread inside
    /// the lock holding libvlc.dll open, and anything that asks
    /// <see cref="TryGet"/> in that window waits for it.
    ///
    /// Volatile and outside the lock on purpose: the whole reason it exists
    /// is to be readable by a UI thread that must NOT take the lock. See
    /// the note on <see cref="Settled"/>.
    /// </summary>
    private static volatile bool _settled;

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
            finally
            {
                // EITHER WAY, and only now that the attempt is over - a
                // failure is as settled as a success, and both let a UI
                // thread stop waiting. See Settled.
                _settled = true;
            }
        }
    }

    /// <summary>
    /// Whether the one attempt at loading the engine is OVER - it loaded,
    /// or it failed and <see cref="Problem"/> says why.
    ///
    /// WHY ANYTHING ASKS THIS RATHER THAN JUST CALLING TryGet (11 Oct 2026).
    /// Loading LibVLC means loading two native DLLs and scanning a plugin
    /// folder of a few hundred files, and on the club's PC that took
    /// FORTY-THREE SECONDS the first time. It had always been done on the
    /// UI thread at the moment the first advert of the night came up, which
    /// is the moment a commercial break starts - so the window in front of
    /// the crowd froze solid for three quarters of a minute, the freeze
    /// watchdog wrote a 1.2 GB dump of the app mid-break, the recap that
    /// was being written behind it was dropped for taking too long, and the
    /// advert finally appeared twelve seconds into the programme that
    /// replaced it.
    ///
    /// So the engine is warmed on a background thread (see
    /// <see cref="WarmUp"/>) and nothing on the UI thread ever calls
    /// TryGet without asking this first: taking the lock while the warm-up
    /// holds it is the same freeze by another route.
    /// </summary>
    internal static bool Settled => _settled;

    /// <summary>
    /// Starts loading the engine on a background thread, once, and reports
    /// how it went. Does nothing when it is already loaded, already
    /// loading, or already known to be broken.
    ///
    /// SAFE ON A BACKGROUND THREAD. Nothing here touches a control: making
    /// the engine is pure native loading, and the two things that ARE
    /// UI-bound - the MediaPlayer and the VideoView - are made by
    /// <see cref="SponsorVideoSurface"/> on the UI thread afterwards, which
    /// costs nothing once the engine exists.
    ///
    /// <paramref name="say"/> is called from the background thread, so a
    /// caller that writes to a window marshals it.
    /// </summary>
    internal static void WarmUp(Action<string>? say = null)
    {
        // WHY (23 Nov 2026, bug hunt 24): this used to open with lock (Gate)
        // to read "already tried / already warming". The warm-up it had
        // started holds that same lock for the whole of the load, so a
        // SECOND ask from the UI thread - another video slide selected,
        // Preview Video pressed, or the Big Screen reaching a video while
        // the Sponsors page's warm-up was still running - waited there
        // until the player had finished loading, and the whole window
        // froze with it (43 seconds on the club's PC). Every caller checks
        // Settled first, which is false for exactly that stretch, so the
        // check sent them straight into the lock. Now nothing here takes
        // it. An engine that something else already loaded or failed to
        // load is Settled; one being loaded right now without a warm-up
        // just makes the task below wait its turn on a pool thread.
        if (!ClaimWarmUp(ref _warmAsked, _settled))
        {
            return;
        }

        Task.Run(() =>
        {
            var started = Environment.TickCount64;

            try
            {
                var vlc = TryGet(out var problem);
                var took = (Environment.TickCount64 - started) / 1000.0;

                say?.Invoke(vlc is not null
                    ? $"{AppTime.LogStampNow()}  [sponsor] The video player is ready ({took:0.#}s to start). "
                      + "Video adverts will play the moment a commercial break asks for one."
                    : $"{AppTime.LogStampNow()}  [sponsor] The video player won't start on this PC, so video adverts "
                      + $"are skipped: {problem ?? "no reason given."}");
            }
            catch (Exception ex)
            {
                // TryGet does not throw. This is here because a warm-up
                // that threw on a pool thread would take the app down with
                // it, and the whole point of this is that a club's evening
                // does not depend on the video player.
                say?.Invoke($"{AppTime.LogStampNow()}  [sponsor] Couldn't start the video player: "
                            + $"{ex.GetType().Name}: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Whether this ask is the one that should start the background load:
    /// true once only, and never once the attempt is over. Takes no lock,
    /// so it cannot wait - see the note in <see cref="WarmUp"/>.
    /// (23 Nov 2026, bug hunt 24.)
    /// </summary>
    internal static bool ClaimWarmUp(ref int asked, bool settled) =>
        !settled && Interlocked.CompareExchange(ref asked, 1, 0) == 0;

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
    /// Its place in NowPlaying (25 Oct 2026) while a video with its own sound
    /// is playing, so Race Control can name it; 0 when none is.
    /// </summary>
    private int _playingId;

    /// <summary>Stops naming the video in Now Playing. Safe from any thread, and twice.</summary>
    private void ForgetPlaying() => NowPlaying.Ended(Interlocked.Exchange(ref _playingId, 0));

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
            _player.EndReached += (_, _) =>
            {
                // Touches nothing of LibVLC's - see above.
                ForgetPlaying();
                Finished?.Invoke();
            };
            // Failed first, then Finished (22 Oct 2026, bug hunt): the Big
            // Screen only needs to know it is over; the preview needs to
            // know it broke, rather than telling the club it played fine.
            _player.EncounteredError += (_, _) =>
            {
                ForgetPlaying();
                Failed?.Invoke();
                Finished?.Invoke();
            };
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

    /// <summary>Whether a video is playing now - the preview asks, to try once more when the first start didn't take. UI thread only.</summary>
    internal bool IsPlaying
    {
        get
        {
            try
            {
                return _player is not null && !_disposed && _player.IsPlaying;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// The video reached its end, or failed part way through. Raised on a
    /// LibVLC thread - a handler that touches a form must marshal.
    /// </summary>
    internal event Action? Finished;

    /// <summary>
    /// The video failed part way through - raised just before Finished, on
    /// a LibVLC thread (22 Oct 2026, bug hunt).
    /// </summary>
    internal event Action? Failed;

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

            // Race Control's Now Playing (25 Oct 2026): a video the room can
            // hear, by its file's name. A silent one is not named - nothing
            // is going out.
            ForgetPlaying();

            if (started && withSound)
            {
                Interlocked.Exchange(ref _playingId,
                    NowPlaying.Started(NowPlayingKind.SponsorVideo, NowPlaying.Joined("Sponsor video", Path.GetFileNameWithoutExtension(path))));
            }

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
    /// Pauses a playing video, or carries on with a paused one. Used by the
    /// Sponsors page's preview window - the Big Screen never pauses an ad.
    /// Returns true when the video is now paused. Called from the UI thread
    /// only, never from inside a LibVLC event (see the note on Finished).
    /// </summary>
    internal bool TogglePause()
    {
        if (_player is null || _disposed)
        {
            return false;
        }

        try
        {
            var pausing = _player.IsPlaying;
            _player.SetPause(pausing);
            return pausing;
        }
        catch
        {
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

        ForgetPlaying();

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
        ForgetPlaying();

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
