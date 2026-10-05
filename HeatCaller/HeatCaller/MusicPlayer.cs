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
    /// </summary>
    public bool IsPlaying
    {
        get { lock (_lock) { return IsOnLocked; } }
    }

    /// <summary>Whether the playlist is on - playing, paused, or between two tracks. Read under the lock.</summary>
    private bool IsOnLocked => _running || _retryPending || _pausedBetweenTracks;

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
            else if (_retryPending || (_running && _output?.PlaybackState == PlaybackState.Stopped))
            {
                // Between two tracks, with nothing playing to pause: a track
                // that would not play and its retry, or one that has just
                // come to its end, with the move to the next one on its way.
                // That move is called off - it used to start the next track
                // straight after the pause, or after the mute, which pauses
                // the music - and Resume moves on to the next track instead.
                _generation++;
                _retryPending = false;
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
        _ducked && Settings.DuckUnderAnnouncements
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

                DisposePlaybackLocked();
                NowPlaying = null;
                endedBecause ??= "no enabled track has a readable file";
            }
            else
            {
                _advancing = true;
                DisposePlaybackLocked();
                _advancing = false;

                try
                {
                    _reader = new AudioFileReader(track.FilePath);
                    _volume = new VolumeSampleProvider(_reader);
                    _duckGain = new SmoothGainSampleProvider(_volume, DuckFactorLocked());
                    ApplyVolumeLocked();

                    // A deep buffer. The music only needs to keep flowing -
                    // nothing about it has to be quick - and at 200ms (two
                    // 100ms buffers) it ran dry whenever the announcer
                    // opened its own output to speak, which on some sound
                    // drivers stalls the others for a moment. It then
                    // stuttered like a scratched CD for as long as the line
                    // lasted. Four buffers ride that out.
                    _output = new WaveOutEvent { DesiredLatency = BedLatencyMs, NumberOfBuffers = BedBuffers };
                    _output.Init(_duckGain);
                    _output.PlaybackStopped += OnPlaybackStopped;
                    _output.Play();
                    _running = true;
                    _trackStartedMs = Environment.TickCount64;

                    // Under the lock, so a Stop straight after cannot be
                    // followed by this track's name going back up.
                    NowPlaying = track;
                }
                catch (Exception ex)
                {
                    StatusMessage?.Invoke($"[music] Couldn't play \"{track.DisplayName}\": {ex.Message}");
                    DisposePlaybackLocked();

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
                    track = null;
                }
            }
        }

        if (track is null)
        {
            NowPlayingChanged?.Invoke(null);
            StatusMessage?.Invoke($"[music] Stopped - {endedBecause}.");
            return;
        }

        NowPlayingChanged?.Invoke(track);
        StatusMessage?.Invoke($"[music] Now playing: {track.DisplayName}");
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

    private void DisposePlaybackLocked()
    {
        // Each in its own guard. They were in one, so a Stop that threw on a
        // device already gone skipped both Disposes - the track's file stayed
        // open until the next garbage collection, long enough to stop it
        // being moved on the Music page or a backup being restored over it.
        if (_output is not null)
        {
            try
            {
                _output.PlaybackStopped -= OnPlaybackStopped;
                _output.Stop();
            }
            catch
            {
                // Stopping a device that's already gone is not worth reporting.
            }

            try
            {
                _output.Dispose();
            }
            catch
            {
                // Nor is disposing one.
            }
        }

        try
        {
            _reader?.Dispose();
        }
        catch
        {
            // The file is let go of either way.
        }

        _output = null;
        _reader = null;
        _volume = null;
        _duckGain = null;
        _running = false;
    }

    public void Dispose() => Stop();
}
