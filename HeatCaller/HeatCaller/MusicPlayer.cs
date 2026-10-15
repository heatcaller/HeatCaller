using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace HeatCaller;

/// <summary>
/// Plays the club's music library between races.
///
/// Its own output device, like ambience - music has to coexist with the
/// announcer and the crowd bed rather than queue behind them. Everything
/// here is best-effort: a missing file or absent audio device leaves the
/// race running normally and silent.
/// </summary>
public sealed class MusicPlayer : IDisposable
{
    private readonly object _lock = new();

    private IWavePlayer? _output;
    private AudioFileReader? _reader;
    private VolumeSampleProvider? _volume;
    private SmoothGainSampleProvider? _duckGain;

    /// <summary>Output buffering for the music and the crowd bed - see where the music's output is made.</summary>
    internal const int BedLatencyMs = 400;

    /// <inheritdoc cref="BedLatencyMs"/>
    internal const int BedBuffers = 4;

    private readonly List<MusicTrack> _queue = new();
    private int _queueIndex = -1;
    private bool _running;
    private bool _ducked;

    /// <summary>
    /// Set while the player itself is swapping tracks, so the natural
    /// end-of-track event isn't mistaken for a user-initiated stop.
    /// </summary>
    private bool _advancing;

    /// <summary>
    /// Bumped by everything that starts, stops, skips or pauses the music
    /// on purpose: Play, Stop, Next, Previous, a track picked from the list,
    /// and Pause or Resume in the gap between two tracks. A move to another
    /// track that runs a moment after it was set going - the next track at
    /// the natural end of one, and the retry after a file that would not
    /// play, both on the thread pool - carries the number it was set going
    /// under, and does nothing if the number has moved on by the time it
    /// runs. See AdvanceTrack.
    /// </summary>
    private int _generation;

    /// <summary>
    /// True from a track failing to play until the retry that tries the
    /// next one runs. Nothing is loaded in between, but the playlist is
    /// still on: IsPlaying says so, which is what lets Stop, Pause, the
    /// mute and the end of the broadcast reach it and call the retry off.
    /// </summary>
    private bool _retryPending;

    /// <summary>
    /// True while a track's file and sound device are being opened, which
    /// now happens with the lock let go (see AdvanceTrack). Nothing is
    /// loaded in between, exactly as for _retryPending, but the playlist is
    /// on: IsPlaying says so, which is what lets Stop, Pause, the mute and
    /// the end of the broadcast reach it - and the open, finding what it was
    /// set going under has moved on, lets go of everything it built instead
    /// of starting it.
    /// </summary>
    private bool _opening;

    /// <summary>
    /// Bumped by each move that commits to opening a track. The file and the
    /// device are opened with the lock let go, so two moves can be opening a
    /// device at the same time; only the one still holding this number when
    /// it finishes puts its output in the fields, and the other stops and
    /// disposes what it built. Without it both could publish, and the club
    /// would hear two tracks at once for the rest of the evening.
    ///
    /// Separate from _generation, which is about intent: a move can still be
    /// the current intent and yet have been overtaken by a later move of the
    /// same generation - two PlaybackStopped events for one device, say, or
    /// one of them alongside the hand-off at the end of AdvanceTrack for a
    /// device that had already stopped. The generation check alone lets both
    /// of those through.
    /// </summary>
    private int _loadToken;

    /// <summary>
    /// Paused in that gap, when there was no track loaded to pause. The
    /// retry has been called off; Resume moves on to the next track.
    /// </summary>
    private bool _pausedBetweenTracks;

    /// <summary>When the current track started (Environment.TickCount64), to tell a file with no sound in it from a track that played to its end.</summary>
    private long _trackStartedMs;

    /// <summary>
    /// A track that stops sooner than this after starting never really
    /// played: a file with no sound in it, or one that fails straight away.
    /// Far shorter than any music, far longer than such a file lasts.
    /// </summary>
    private const long ShortestRealPlayMs = 500;

    private readonly Random _shuffle = new();

    public event Action<string>? StatusMessage;

    /// <summary>Raised when the playing track changes, so the UI can show what's on.</summary>
    public event Action<MusicTrack?>? NowPlayingChanged;

    public MusicSettings Settings { get; set; } = new();

    /// <summary>
    /// The applied soundscape's music, for when Settings.PlayThemeMusic is
    /// on. Set by the main window at start-up and whenever a soundscape is
    /// applied; null when none has been.
    /// </summary>
    public ThemeMusicSource? Theme { get; set; }

    /// <summary>
    /// True when the next queue will be the soundscape's tracks: Theme music
    /// is chosen AND the soundscape has tracks this PC can play. When it has
    /// none (Pit Lane has no music of its own), the club's own music plays
    /// instead - a room falling silent because the soundscape happens to
    /// have no tracks is the worse surprise - and the Music page says so.
    /// </summary>
    public bool ThemeMusicInUse =>
        Settings.PlayThemeMusic == true && Theme is { } theme && theme.TracksIn(Settings.Tracks).Count > 0;

    /// <summary>
    /// True from Play until Stop, paused included - and in the moment
    /// between a track that would not play and the next one being tried,
    /// when nothing is loaded. It used to read false there, so Stop, the
    /// mute and "Music after the recap" all found nothing playing and did
    /// nothing, and the retry then started the next track regardless.
    ///
    /// True while a track's file and device are being opened too (see
    /// _opening). That is now a moment with nothing loaded as well, and the
    /// same callers have to be able to reach it.
    /// </summary>
    public bool IsPlaying
    {
        get { lock (_lock) { return IsOnLocked; } }
    }

    /// <summary>Whether the playlist is on - playing, paused, opening a track, or between two tracks. Read under the lock.</summary>
    private bool IsOnLocked => _running || _opening || _retryPending || _pausedBetweenTracks;

    public MusicTrack? NowPlaying { get; private set; }

    /// <summary>
    /// Starts playback from the top of a freshly built queue. Safe to call
    /// when already playing, or with nothing playable configured.
    /// </summary>
    public void Play()
    {
        int generation;

        lock (_lock)
        {
            if (IsOnLocked)
            {
                return;
            }

            BuildQueueLocked();

            if (_queue.Count == 0)
            {
                StatusMessage?.Invoke("[music] Nothing to play - no enabled track has a readable file.");
                return;
            }

            _queueIndex = -1;
            generation = ++_generation;
        }

        AdvanceTrack(generation);
    }

    /// <summary>
    /// Fades the music down over <paramref name="seconds"/> and then stops.
    ///
    /// A hard cut at the end of a race is jarring - it reads as something
    /// breaking rather than as the session closing. A fade is what a
    /// broadcast does, and it costs nothing but a timer.
    /// </summary>
    public async Task FadeOutAsync(double seconds)
    {
        if (!IsPlaying)
        {
            return;
        }

        var steps = Math.Max(1, (int)(Math.Clamp(seconds, 0.5, 30) * 20));
        var startVolume = Settings.Volume;

        // What the music was doing when the fade began. Once it has been
        // started, stopped or skipped by something else - Play or Next
        // pressed - the fade is no longer about the music that is on, and
        // leaves it alone. It used to carry on, and stop the new music at
        // the end. A new session wanting the music kept calls it off too
        // (CancelFade), as does a later fade, which takes over.
        int generation;
        int fade;

        lock (_lock)
        {
            generation = _generation;
            fade = ++_fadeToken;
        }

        try
        {
            for (var i = 1; i <= steps; i++)
            {
                lock (_lock)
                {
                    if (_generation != generation || _fadeToken != fade)
                    {
                        return;
                    }

                    // Nothing loaded: a track that would not play, with the
                    // next one about to be tried. Stopped below, which calls
                    // that retry off. The fade used to give up here without
                    // stopping, and the retry then started the next track
                    // after the broadcast had finished.
                    if (!_running || _volume is null)
                    {
                        break;
                    }

                    // Eased rather than linear - a straight ramp sounds like
                    // it drops away early, because loudness isn't linear.
                    var progress = (double)i / steps;
                    var eased = 1 - progress * progress;

                    _volume.Volume = (float)Math.Clamp(startVolume * eased, 0f, 1f);
                }

                await Task.Delay(50);
            }
        }
        catch
        {
            // A failed fade should still leave the music stopped.
        }

        // Called off in the last step's wait: the music stays on.
        lock (_lock)
        {
            if (_generation != generation || _fadeToken != fade)
            {
                return;
            }
        }

        Stop();

        // Restore the configured level, or the next Play would start silent.
        lock (_lock)
        {
            ApplyVolumeLocked();
        }
    }

    /// <summary>
    /// Taken by each FadeOutAsync as it starts, and moved on by CancelFade: a
    /// fade whose number has moved on stops where it is and leaves the music
    /// playing. Separate from _generation, which calls off the move to the
    /// next track - calling off a fade must not do that.
    /// </summary>
    private int _fadeToken;

    /// <summary>
    /// Calls off a fade that is under way and puts the music back at its
    /// set level, playing. For a new session that wants the music on while
    /// the last one's end is still fading it out: the fade used to carry on
    /// and stop the music at its end, a few seconds into the new heat.
    /// </summary>
    public void CancelFade()
    {
        lock (_lock)
        {
            _fadeToken++;
            ApplyVolumeLocked();
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            // Calls off whatever was set going to run a moment later - the
            // next track at the end of one, the retry after a file that
            // would not play - whether or not anything is loaded now. Stop
            // used to return straight away when nothing was, which is just
            // the state a failed track leaves while its retry waits: Stop
            // did nothing, and the retry then started the next track.
            _generation++;

            if (!IsOnLocked && _output is null)
            {
                return;
            }

            _retryPending = false;
            _pausedBetweenTracks = false;
            DisposePlaybackLocked();

            // Under the lock, so a track started on the thread pool a moment
            // before cannot put its name back after this.
            NowPlaying = null;
        }

        NowPlayingChanged?.Invoke(null);
        StatusMessage?.Invoke("[music] Stopped.");
    }

    /// <summary>Skips to the next track, rebuilding the queue if it's exhausted.</summary>
    public void Skip()
    {
        int generation;

        lock (_lock)
        {
            if (!IsOnLocked)
            {
                return;
            }

            generation = ++_generation;
        }

        AdvanceTrack(generation);
    }

    /// <summary>
    /// Restarts the current track, or goes to the previous one if we're
    /// already near the start.
    ///
    /// Matches how every music player behaves: pressing Previous a few
    /// seconds in means "I missed the beginning", not "skip backwards".
    /// </summary>
    public void Previous()
    {
        int generation;

        lock (_lock)
        {
            if (!IsOnLocked)
            {
                return;
            }

            if (_reader is not null && _reader.CurrentTime > TimeSpan.FromSeconds(3))
            {
                _reader.CurrentTime = TimeSpan.Zero;
                return;
            }

            // Step back two, because AdvanceTrack moves forward one.
            _queueIndex = Math.Max(-1, _queueIndex - 2);
            generation = ++_generation;
        }

        AdvanceTrack(generation);
    }

    /// <summary>Pauses without tearing down the track, so Resume picks up where it left off.</summary>
    public void Pause()
    {
        lock (_lock)
        {
            if (_output?.PlaybackState == PlaybackState.Playing)
            {
                // Guarded as the track start and DisposePlaybackLocked are: a
                // device that has just gone (a Bluetooth PA switched off) makes
                // waveOutPause throw, and it used to escape to the sidebar's
                // Mute - a crash dialog in front of the club, voice muted and
                // the music still playing. The device is let go instead, which
                // stops the music as the pause meant to, and Resume moves on to
                // the next track on whatever device there is then.
                try
                {
                    _output.Pause();
                }
                catch (Exception ex)
                {
                    StatusMessage?.Invoke($"[music] The sound device wouldn't pause ({ex.Message}) - the music is stopped instead, and carries on with the next track when it is resumed.");
                    DisposePlaybackLocked();
                    _generation++;
                    _retryPending = false;
                    _pausedBetweenTracks = true;
                }
            }
            else if (_retryPending || _opening || (_running && _output?.PlaybackState == PlaybackState.Stopped))
            {
                // Between two tracks, with nothing playing to pause: a track
                // that would not play and its retry, or one that has just
                // come to its end, with the move to the next one on its way.
                // That move is called off - it used to start the next track
                // straight after the pause, or after the mute, which pauses
                // the music - and Resume moves on to the next track instead.
                //
                // A track whose file and device are being opened counts as
                // between two tracks too. That open happens with the lock
                // let go (see _opening), so it is a moment a pause can land
                // in, and moving the generation on is what stops the track
                // coming up underneath the pause a fraction later.
                _generation++;
                _retryPending = false;
                _opening = false;
                _pausedBetweenTracks = true;
            }
        }

        NowPlayingChanged?.Invoke(NowPlaying);
    }

    public void Resume()
    {
        int? nextUnder = null;

        lock (_lock)
        {
            if (_output?.PlaybackState == PlaybackState.Paused)
            {
                // A device gone while paused: on to the next track, which opens
                // whatever device there is now, rather than an exception on the
                // UI thread (see Pause).
                try
                {
                    _output.Play();
                }
                catch (Exception ex)
                {
                    StatusMessage?.Invoke($"[music] The sound device wouldn't resume ({ex.Message}) - moving on to the next track.");
                    DisposePlaybackLocked();
                    nextUnder = ++_generation;
                }
            }
            else if (_pausedBetweenTracks)
            {
                // Paused between two tracks (see Pause): on to the next one.
                nextUnder = ++_generation;
            }
        }

        if (nextUnder is { } generation)
        {
            AdvanceTrack(generation);
            return;
        }

        NowPlayingChanged?.Invoke(NowPlaying);
    }

    public bool IsPaused
    {
        get { lock (_lock) { return _output?.PlaybackState == PlaybackState.Paused || _pausedBetweenTracks; } }
    }

    /// <summary>Where the current track is up to, and how long it runs. Zero when nothing is loaded.</summary>
    public (TimeSpan Position, TimeSpan Duration) CurrentPosition
    {
        get
        {
            lock (_lock)
            {
                return _reader is null
                    ? (TimeSpan.Zero, TimeSpan.Zero)
                    : (_reader.CurrentTime, _reader.TotalTime);
            }
        }
    }

    /// <summary>Jumps to a point in the current track.</summary>
    public void Seek(TimeSpan position)
    {
        lock (_lock)
        {
            if (_reader is null)
            {
                return;
            }

            // Clamped, and kept just short of the end - seeking exactly to
            // TotalTime fires PlaybackStopped and skips the track, which is
            // not what dragging to the end of the bar should do.
            var limit = _reader.TotalTime - TimeSpan.FromMilliseconds(250);

            // A seek the file or device refuses is said, not thrown at the
            // Music page's position bar.
            try
            {
                _reader.CurrentTime = position < TimeSpan.Zero
                    ? TimeSpan.Zero
                    : position > limit ? limit : position;
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"[music] Couldn't move within the track: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Plays a specific track immediately, rather than waiting for it to come up.
    ///
    /// A track excluded from the playlist plays once, and then the playlist
    /// carries on from the top of a fresh pass - reshuffled, with shuffle on.
    /// It used to play nothing at all, though the Music page said it was
    /// "playing it once": only the playlist was looked in. It never joins
    /// the queue, so once it has played - or failed to - nothing brings it
    /// round again: not the track after it, not Previous, not a reshuffle,
    /// not the retry.
    /// </summary>
    public void PlayTrack(MusicTrack track)
    {
        int generation;
        MusicTrack? once = null;

        lock (_lock)
        {
            BuildQueueLocked();

            var index = _queue.FindIndex(t => t.Id == track.Id);

            if (index >= 0)
            {
                // One before, since AdvanceTrack steps forward.
                _queueIndex = index - 1;
            }
            // Any track not in this queue: one excluded from "My music", or
            // - with Theme music playing - any track that isn't the
            // theme's. Only excluded tracks were let through here before,
            // which with Theme music on would have made double-clicking an
            // ordinary ticked track do nothing at all.
            else if (Settings.Tracks.FirstOrDefault(t => t.Id == track.Id) is { } excluded
                     && File.Exists(excluded.FilePath))
            {
                // Played instead of the queue's next; the queue waits at its
                // start, so its first track follows.
                once = excluded;
                _queueIndex = -1;
            }
            else
            {
                // Not in the library any more, or its file is gone.
                return;
            }

            generation = ++_generation;
        }

        AdvanceTrack(generation, playOnce: once);
    }

    /// <summary>
    /// Ducks under announcements, exactly as ambience does - applied as a
    /// multiplier over the configured volume so releasing it restores the
    /// right level rather than a remembered one.
    /// </summary>
    /// <summary>Ducks to a specific depth, overriding the configured fraction. Null restores normal.</summary>
    public void SetDuckDepthOverride(float? fraction)
    {
        lock (_lock)
        {
            _duckOverride = fraction is { } f ? Math.Clamp(f, 0.02f, 1f) : null;
            ApplyDuckLocked();
        }
    }

    private float? _duckOverride;

    /// <summary>
    /// Silences the music outright while a sponsor video with its own sound
    /// is on the Big Screen - see AdBreak.
    ///
    /// Not a duck. A duck leaves the music audible under a voice, which is
    /// right for an announcement and wrong for an ad: the ad has its own
    /// soundtrack, and a club that sold the spot has promised the sponsor
    /// that nothing plays over it. It also deliberately ignores
    /// DuckUnderAnnouncements, which a club may well have switched off -
    /// that setting is about the announcer, and it should not decide
    /// whether a paid ad is talked over.
    ///
    /// The glide is the same one the duck uses (SmoothGainSampleProvider),
    /// so the music fades out in about a tenth of a second rather than
    /// cutting, and comes back over half a second when the ad ends.
    /// </summary>
    public void SetAdBreakMuted(bool muted)
    {
        lock (_lock)
        {
            if (_adBreakMuted == muted)
            {
                return;
            }

            _adBreakMuted = muted;
            ApplyDuckLocked();
        }
    }

    private bool _adBreakMuted;

    /// <summary>
    /// Silences the club's music while the show's own opener and bed are
    /// playing - see ShowTreatment and ShowOnAir.
    ///
    /// Not a duck, for the same reason an ad break isn't: two pieces of
    /// music at once is a mess whichever is louder, and the show's bed is
    /// the one the voice was written over. Like an ad break it ignores
    /// DuckUnderAnnouncements, which is a setting about the announcer and
    /// should not decide whether the club's playlist plays over its own
    /// broadcast open.
    ///
    /// Held apart from the ad break's flag rather than sharing one: a
    /// sponsor spot during a show is entirely possible, and whichever ends
    /// first must not bring the playlist back up underneath the other.
    /// </summary>
    public void SetShowMuted(bool muted)
    {
        lock (_lock)
        {
            if (_showMuted == muted)
            {
                return;
            }

            _showMuted = muted;
            ApplyDuckLocked();
        }
    }

    private bool _showMuted;

    public void SetDucked(bool ducked)
    {
        lock (_lock)
        {
            if (_ducked == ducked)
            {
                return;
            }

            _ducked = ducked;
            ApplyDuckLocked();
        }
    }

    /// <summary>Re-applies the configured volume - call after changing Settings.Volume.</summary>
    public void RefreshVolume()
    {
        lock (_lock)
        {
            ApplyVolumeLocked();
        }
    }

    /// <summary>
    /// The configured level. The duck is a separate stage after it (see
    /// ApplyDuckLocked), so the two never fight: a line spoken during the
    /// end-of-race fade used to set the volume straight back to full
    /// between two steps of the fade.
    /// </summary>
    private void ApplyVolumeLocked()
    {
        if (_volume is not null)
        {
            _volume.Volume = Math.Clamp(Settings.Volume, 0f, 1f);
        }

        ApplyDuckLocked();
    }

    /// <summary>Points the duck's glide at the level it should be at now.</summary>
    private void ApplyDuckLocked()
    {
        if (_duckGain is not null)
        {
            _duckGain.Target = DuckFactorLocked();
        }
    }

    private float DuckFactorLocked() =>
        // An ad break or the show's own music wins over everything:
        // silence, not a duck, and not subject to
        // DuckUnderAnnouncements. See SetAdBreakMuted and SetShowMuted.
        _adBreakMuted || _showMuted ? 0f
        : _ducked && Settings.DuckUnderAnnouncements
            // Override wins - the recap ducks deeper than an announcement.
            ? Math.Clamp(_duckOverride ?? Settings.DuckToFraction, 0.02f, 1f)
            : 1f;

    private void BuildQueueLocked()
    {
        _queue.Clear();

        // A copy of the library taken in one go, not the live list walked.
        // This runs on the thread pool at the natural end of a track, while
        // the Music page can be adding or removing tracks on the UI thread -
        // walking the live list then threw "collection was modified", which
        // killed the move to the next track without a word and left the
        // player saying it was on with nothing playing. A copy that fails
        // outright just means nothing to play this time round.
        MusicTrack[] library;
        try
        {
            library = Settings.Tracks.ToArray();
        }
        catch
        {
            library = Array.Empty<MusicTrack>();
        }

        // Theme music is the soundscape's tracks whatever their "My music"
        // ticks say - those ticks are the club's playlist, and the theme
        // no longer touches them. Nothing of the theme's to play falls back
        // to the club's own music (see ThemeMusicInUse).
        var themeTracks = Settings.PlayThemeMusic == true && Theme is { } theme
            ? theme.TracksIn(library)
            : new List<MusicTrack>();

        _queue.AddRange(themeTracks.Count > 0
            ? themeTracks
            : library.Where(t => t.Enabled && File.Exists(t.FilePath)));

        if (Settings.Shuffle && _queue.Count > 1)
        {
            // Fisher-Yates over the whole list, so every track is played
            // once before any repeats - picking at random each time would
            // let the same track come up twice in a row, which is what
            // people actually notice and dislike about "random".
            for (var i = _queue.Count - 1; i > 0; i--)
            {
                var j = _shuffle.Next(i + 1);
                (_queue[i], _queue[j]) = (_queue[j], _queue[i]);
            }
        }
    }

    /// <param name="generation">
    /// The _generation this move belongs to: that of the Play, Next,
    /// Previous or pick from the list that asked for it - or, for the next
    /// track at the natural end of one and the retry after a file that
    /// would not play, both of which run a moment later on the thread pool,
    /// the one current when they were set going. Nothing happens if it has
    /// moved on since.
    ///
    /// The natural end used to be guarded only by whether the music was
    /// still running, which caught a Stop in that moment - the Stop button,
    /// or a backup restore closing every file the app holds open - but not
    /// a Next or a new Play, which the late move then skipped straight past.
    /// The retry was not guarded at all, and a failed track leaves nothing
    /// running, so a Stop in its gap had nothing to stop: the retry started
    /// the next track after Stop, after "Stop while a race is running", the
    /// mute, "Music after the recap" and even the app closing its player.
    /// </param>
    /// <param name="stoppedEarly">
    /// Set when the track that has just stopped never really played - an
    /// error part-way through, or no sound in it at all - saying what went
    /// wrong. It is dealt with as a file that would not open is: taken out
    /// of this pass of the queue, and the next one tried. Taken for the
    /// natural end of a track, a folder of such files went round and round
    /// for ever, opening the output device for every one of them as fast as
    /// the machine could.
    /// </param>
    /// <param name="playOnce">
    /// A track excluded from the playlist, picked from the list to play once
    /// (PlayTrack): played now instead of the queue's next. Only that first
    /// move is given it - the natural end of the track, Next, and the retry
    /// when it won't play all carry on with the queue, which it is never in.
    /// </param>
    private void AdvanceTrack(int generation, string? stoppedEarly = null, MusicTrack? playOnce = null)
    {
        MusicTrack? track;

        // Why the playlist has come to a stop by itself, when it has. Said
        // once the lock is let go, with Now Playing cleared: giving up used
        // to be silent, and the page went on showing the last track, and a
        // Pause button, with nothing playing.
        string? endedBecause = null;

        // The track being let go of, this move's claim on the player, and the
        // levels the new track starts at: all settled under the lock below
        // and then used with it released. The file and the sound device used
        // to be opened under the lock - see where they are opened now for
        // what that cost.
        IWavePlayer? retiredOutput = null;
        AudioFileReader? retiredReader = null;
        var load = 0;
        var startVolume = 0f;
        var startDuck = 1f;

        lock (_lock)
        {
            if (generation != _generation)
            {
                return;
            }

            _retryPending = false;
            _pausedBetweenTracks = false;

            if (stoppedEarly is not null)
            {
                StatusMessage?.Invoke($"[music] Couldn't play \"{NowPlaying?.DisplayName}\": {stoppedEarly}");

                if (!DropFailedTrackLocked(NowPlaying))
                {
                    endedBecause = "none of the tracks in the playlist would play";
                }
            }

            track = endedBecause is null ? (playOnce ?? NextInQueueLocked()) : null;

            if (track is null)
            {
                // Nothing (more) to play. A track still going - Next pressed
                // with nothing else left to play - carries on, as it always
                // did; otherwise the playlist has come to an end.
                if (_output is { PlaybackState: PlaybackState.Playing or PlaybackState.Paused })
                {
                    return;
                }

                TakePlaybackLocked(out retiredOutput, out retiredReader);
                NowPlaying = null;
                endedBecause ??= "no enabled track has a readable file";
            }
            else
            {
                // Still set, though stopping the old device has moved out of
                // the lock along with the rest of the teardown: _running and
                // _output now go in the same instant, under this lock, so
                // OnPlaybackStopped's own two guards already cover the swap.
                _advancing = true;
                TakePlaybackLocked(out retiredOutput, out retiredReader);
                _advancing = false;

                // This move's claim on the player, and the playlist still
                // reading as on while the open runs - see _loadToken and
                // _opening.
                load = ++_loadToken;
                _opening = true;

                // Read here, where the volume and the duck's flags are under
                // the lock, and applied to the new stages before the device
                // is started: it must not play one whole buffer at full
                // volume first. ApplyVolumeLocked runs again once the output
                // is published, to pick up anything that changed meanwhile.
                startVolume = Math.Clamp(Settings.Volume, 0f, 1f);
                startDuck = DuckFactorLocked();
            }
        }

        // With the lock let go. waveOutReset and waveOutClose take as long as
        // the device makes them take, and the UI thread reads IsPlaying
        // several times a second - from the Music page and from the end of
        // every broadcast. See TakePlaybackLocked.
        DisposeRetired(retiredOutput, retiredReader);

        if (track is null)
        {
            NowPlayingChanged?.Invoke(null);
            StatusMessage?.Invoke($"[music] Stopped - {endedBecause}.");
            return;
        }

        // All four stay local until they are published under the lock at the
        // end, so no other thread can reach a half-built track.
        AudioFileReader? reader = null;
        VolumeSampleProvider? volume = null;
        SmoothGainSampleProvider? duckGain = null;
        IWavePlayer? output = null;

        try
        {
            // Opened with the lock let go, which is the point of all of this.
            // AudioFileReader reads a file header; Init is waveOutOpen, and
            // that is the slow one - the sound system reconfigures itself for
            // the new stream, and a Bluetooth speaker may have to be woken
            // first. AudioBus exists because of how long that takes. Under
            // the lock, every IsPlaying on the UI thread waited for all of
            // it, which is a frozen window on the night it happens.
            reader = new AudioFileReader(track.FilePath);
            volume = new VolumeSampleProvider(reader) { Volume = startVolume };
            duckGain = new SmoothGainSampleProvider(volume, startDuck);

            // A deep buffer. The music only needs to keep flowing -
            // nothing about it has to be quick - and at 200ms (two
            // 100ms buffers) it ran dry whenever the announcer
            // opened its own output to speak, which on some sound
            // drivers stalls the others for a moment. It then
            // stuttered like a scratched CD for as long as the line
            // lasted. Four buffers ride that out.
            output = new WaveOutEvent { DesiredLatency = BedLatencyMs, NumberOfBuffers = BedBuffers };
            // Sent to the device as 16-bit, exactly as AudioBus does: some
            // drivers play 32-bit float as silence without complaint. The
            // volume and the duck are both held at or below 1, so nothing
            // here is pushed into clipping by the conversion.
            output.Init(new SampleToWaveProvider16(duckGain));
            output.PlaybackStopped += OnPlaybackStopped;
            output.Play();
        }
        catch (Exception ex)
        {
            // None of this was ever reachable from another thread, so it is
            // let go of before the lock is taken again.
            DisposeRetired(output, reader);

            lock (_lock)
            {
                if (!StillOursLocked(generation, load))
                {
                    ReleaseOpeningLocked(load);
                    return;
                }

                _opening = false;
                StatusMessage?.Invoke($"[music] Couldn't play \"{track.DisplayName}\": {ex.Message}");

                // Skip a bad file rather than stalling the whole playlist,
                // but only if there's something else to try. The retry
                // belongs to this move's generation, so anything pressed
                // before it runs calls it off (see the parameter above).
                if (DropFailedTrackLocked(track))
                {
                    _retryPending = true;
                    _ = Task.Run(() => AdvanceTrack(generation));
                    return;
                }

                NowPlaying = null;
                endedBecause = "none of the tracks in the playlist would play";
            }

            NowPlayingChanged?.Invoke(null);
            StatusMessage?.Invoke($"[music] Stopped - {endedBecause}.");
            return;
        }

        bool published;

        lock (_lock)
        {
            published = StillOursLocked(generation, load);

            if (published)
            {
                _reader = reader;
                _volume = volume;
                _duckGain = duckGain;
                _output = output;
                _running = true;
                _opening = false;
                _trackStartedMs = Environment.TickCount64;

                // Again now the output is in the fields, so anything that
                // moved while the device was opening is applied to it: the
                // Music fader, a duck, an ad break, the show's own bed.
                ApplyVolumeLocked();

                // Under the lock, so a Stop straight after cannot be
                // followed by this track's name going back up.
                NowPlaying = track;
            }
            else
            {
                ReleaseOpeningLocked(load);
            }
        }

        if (!published)
        {
            // A Stop, a Pause, Next or a new Play landed while the device was
            // opening, or a later move claimed the player. This track is not
            // what the player is doing any more, so it is stopped and let go
            // of rather than left playing out of an output nothing points at:
            // that is how two tracks at once would start.
            DisposeRetired(output, reader);
            return;
        }

        NowPlayingChanged?.Invoke(track);
        StatusMessage?.Invoke($"[music] Now playing: {track.DisplayName}");

        // A device that stopped between Play and the publish above - a file
        // with no sound in it ends inside the first buffer - raised its event
        // while the fields were still empty, and OnPlaybackStopped put it
        // down as a stranger's. So the move to the next track is handed to it
        // here instead of being waited for for ever. Harmless if the real
        // event turns up as well: both moves carry this generation, and the
        // claim (_loadToken) is what keeps the second one from publishing a
        // second output.
        if (output is { PlaybackState: PlaybackState.Stopped })
        {
            OnPlaybackStopped(output, new StoppedEventArgs());
        }
    }

    /// <summary>
    /// Whether a move that has just opened a track is still the one the
    /// player is doing: nothing has been pressed since (_generation), and no
    /// later move has claimed the player (_loadToken). Read under the lock.
    /// </summary>
    private bool StillOursLocked(int generation, int load) =>
        generation == _generation && load == _loadToken;

    /// <summary>
    /// Puts the "opening a track" flag down for a move that has given up,
    /// but only if nothing newer has claimed the player - a later move owns
    /// the flag then, and will put it down itself. Clearing it regardless
    /// would have IsPlaying read false with a track on its way up.
    /// </summary>
    private void ReleaseOpeningLocked(int load)
    {
        if (load == _loadToken)
        {
            _opening = false;
        }
    }

    /// <summary>
    /// Moves on to the next track in the queue, rebuilding the queue when it
    /// is empty or used up. Null when nothing is playable.
    /// </summary>
    private MusicTrack? NextInQueueLocked()
    {
        if (_queue.Count == 0)
        {
            BuildQueueLocked();
            _queueIndex = -1;

            if (_queue.Count == 0)
            {
                return null;
            }
        }

        // The queue is built once a pass, but the library can change during
        // it: a track excluded or removed on the Music page kept coming up
        // until the pass ran out - an hour later, maybe - while the page
        // showed it as "No" or gone. So each move goes past any entry the
        // queue would no longer be built with.
        var playable = PlayableIdsLocked();

        while (true)
        {
            _queueIndex++;

            if (_queueIndex >= _queue.Count)
            {
                // End of the queue - reshuffle and go round again, so a
                // long event doesn't fall silent after one pass. Built from
                // the library as it is now, so nothing in it needs skipping.
                BuildQueueLocked();
                _queueIndex = 0;

                return _queue.Count == 0 ? null : _queue[0];
            }

            if (playable.Contains(_queue[_queueIndex].Id))
            {
                return _queue[_queueIndex];
            }
        }
    }

    /// <summary>
    /// The ids of the tracks a queue built now would hold - the same choice
    /// BuildQueueLocked makes (theme tracks, or the library's enabled ones),
    /// for NextInQueueLocked to tell a still-wanted entry from one excluded
    /// or removed since the queue was built.
    /// </summary>
    private HashSet<string> PlayableIdsLocked()
    {
        // A copy, as in BuildQueueLocked: the Music page can change the
        // list on the UI thread while this runs on the thread pool.
        MusicTrack[] library;
        try
        {
            library = Settings.Tracks.ToArray();
        }
        catch
        {
            library = Array.Empty<MusicTrack>();
        }

        var themeTracks = Settings.PlayThemeMusic == true && Theme is { } theme
            ? theme.TracksIn(library)
            : new List<MusicTrack>();

        return (themeTracks.Count > 0 ? themeTracks : library.Where(t => t.Enabled))
            .Select(t => t.Id)
            .ToHashSet();
    }

    /// <summary>
    /// Takes a track that would not play out of this pass of the queue, so
    /// the next move goes past it rather than back to it - which is what
    /// makes a folder of bad files come to an end instead of going round.
    /// False when it is the only track left: there is nothing else to try.
    /// </summary>
    private bool DropFailedTrackLocked(MusicTrack? failed)
    {
        if (failed is null)
        {
            return true;
        }

        var failedId = failed.Id;
        var index = _queue.FindIndex(t => t.Id == failedId);

        // Not in the queue - an excluded track played once from the list,
        // say. Nothing to take out: the next move goes on with the queue,
        // and never comes back to it.
        if (index < 0)
        {
            return true;
        }

        if (_queue.Count == 1)
        {
            return false;
        }

        _queue.RemoveAt(index);

        if (index <= _queueIndex)
        {
            _queueIndex--;
        }

        return true;
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        int generation;
        string? stoppedEarly;

        lock (_lock)
        {
            // A stop we caused ourselves while swapping tracks, a real
            // Stop(), or a late event from a device already let go - which
            // would otherwise skip the track that has just replaced it.
            //
            // Nor once paused between tracks (see Pause): the end of this
            // track can be heard only after the pause that caught it, and
            // moving on then would start the next track under the pause.
            if (_advancing || !_running || _pausedBetweenTracks || !ReferenceEquals(sender, _output))
            {
                return;
            }

            generation = _generation;

            // An error part-way through, or a "track" over the moment it
            // began, never really played - see AdvanceTrack's stoppedEarly.
            stoppedEarly = e.Exception is { } error
                ? $"it stopped part-way ({error.Message})"
                : Environment.TickCount64 - _trackStartedMs < ShortestRealPlayMs
                    ? "it stopped as soon as it started - the file may have no sound in it"
                    : null;
        }

        // Natural end of track - move on. Off the callback thread, since
        // this disposes the very device that raised the event.
        _ = Task.Run(() => AdvanceTrack(generation, stoppedEarly));
    }

    /// <summary>
    /// Takes the loaded track out of the fields and hands back the two parts
    /// that have to be let go of, for the caller to pass to DisposeRetired
    /// with the lock released.
    ///
    /// The device used to be stopped and disposed here, under the lock:
    /// waveOutReset and waveOutClose, which take as long as the sound system
    /// makes them take - a Bluetooth speaker reconfigures itself, and one
    /// that has wandered off takes seconds. The UI thread reads IsPlaying
    /// several times a second and so waited for every bit of that, on a
    /// thread pool move it had nothing to do with.
    ///
    /// Nothing can reach the output once this has returned: it is out of the
    /// field, and OnPlaybackStopped ignores an event whose sender is not the
    /// current _output and ignores anything at all while _running is false.
    /// Both of those go in this one locked step, so letting the device go
    /// outside the lock is safe.
    /// </summary>
    private void TakePlaybackLocked(out IWavePlayer? output, out AudioFileReader? reader)
    {
        output = _output;
        reader = _reader;

        _output = null;
        _reader = null;
        _volume = null;
        _duckGain = null;
        _running = false;
        _opening = false;
    }

    /// <summary>
    /// Stops and lets go of a track taken out by TakePlaybackLocked, or of
    /// one this move opened and then found it was no longer the move being
    /// played. Call it with the lock released.
    /// </summary>
    private void DisposeRetired(IWavePlayer? output, AudioFileReader? reader)
    {
        // Each in its own guard. They were in one, so a Stop that threw on a
        // device already gone skipped both Disposes - the track's file stayed
        // open until the next garbage collection, long enough to stop it
        // being moved on the Music page or a backup being restored over it.
        if (output is not null)
        {
            try
            {
                output.PlaybackStopped -= OnPlaybackStopped;
                output.Stop();
            }
            catch
            {
                // Stopping a device that's already gone is not worth reporting.
            }

            try
            {
                output.Dispose();
            }
            catch
            {
                // Nor is disposing one.
            }
        }

        try
        {
            reader?.Dispose();
        }
        catch
        {
            // The file is let go of either way.
        }
    }

    /// <summary>
    /// Takes the track out and lets it go in one step, with the lock held.
    ///
    /// Left as it was for Stop, Pause and Resume, which are all on the UI
    /// thread: the thread doing the disposing there is the one that would
    /// have been waiting anyway, so splitting it buys nothing - and keeping
    /// it in one step means a Stop is finished with the old device before it
    /// returns, so a Play straight after it cannot open the next one while
    /// the last is still closing. AdvanceTrack, which runs on the thread
    /// pool, is the one that had to be split.
    /// </summary>
    private void DisposePlaybackLocked()
    {
        TakePlaybackLocked(out var output, out var reader);
        DisposeRetired(output, reader);
    }

    public void Dispose() => Stop();
}
