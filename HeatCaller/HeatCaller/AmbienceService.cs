using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace HeatCaller;

/// <summary>
/// Continuous background atmosphere - crowd and track noise - played
/// UNDERNEATH the existing announcements rather than queued alongside them.
///
/// This deliberately does NOT go through AnnouncementService's playback
/// lock. That lock exists to stop announcements talking over each other,
/// which is right for discrete lines but exactly wrong for ambience: a
/// crowd bed has to run continuously and be audible beneath the announcer,
/// not wait its turn. So this owns its own output device and mixer, and the
/// two streams simply coexist at the hardware level.
///
/// Everything is best-effort. A missing file, an unreadable format, or no
/// audio device at all leaves the race running normally and silent - losing
/// atmosphere must never take a race day down.
/// </summary>
public sealed class AmbienceService : IDisposable
{
    /// <summary>How worked-up the crowd is, driven by what's happening on track.</summary>
    public enum Intensity
    {
        /// <summary>Between races - light murmur.</summary>
        Idle,

        /// <summary>Cars staged, countdown running - anticipation.</summary>
        Anticipation,

        /// <summary>Normal green-flag running.</summary>
        Racing,

        /// <summary>Something happened - close battle, lead change, fast lap.</summary>
        Excited,

        /// <summary>Finish, record, or a big moment.</summary>
        Peak,
    }

    private readonly object _lock = new();

    private IWavePlayer? _output;
    private MixingSampleProvider? _mixer;

    /// <summary>
    /// The duck under the announcer, as its own stage after the mixer with
    /// its own quick glide (see SmoothGainSampleProvider).
    ///
    /// It used to be multiplied into every layer's target and ridden on the
    /// crowd's own fades, so each line the announcer spoke started one of
    /// the club's slow fades (4 seconds down by default) and restarted
    /// whatever ramp was running - a surge's rise, a finish, a settle. The
    /// ramps and fades set on the Ambience tab never got to play out as
    /// set, because the announcer speaks on exactly the moments the crowd
    /// reacts to. Now the layers follow only the race and the club's fade
    /// times, and the duck sits on top.
    /// </summary>
    private SmoothGainSampleProvider? _duckGain;

    /// <summary>Glide times for the duck - quick down so the first word is clear, gentler back up.</summary>
    private const double DuckDownSeconds = 0.25;
    private const double DuckUpSeconds = 0.8;

    /// <summary>Moved on by each fade-out-and-stop and by anything that calls one off.</summary>
    private int _fadeOutGeneration;

    /// <summary>True while fading out to stop - see FadeOutAndStopAsync.</summary>
    private bool _fadingOut;

    // One looping bed per layer, each with its own volume control so they
    // can be faded independently as the race state changes.
    private readonly Dictionary<string, VolumeSampleProvider> _layers = new(StringComparer.OrdinalIgnoreCase);

    // The file readers behind those layers. VolumeSampleProvider doesn't own
    // (or dispose) its source, so these are tracked separately - otherwise
    // every Start/Stop cycle leaked an open file handle per layer, which
    // also kept the audio files locked against being replaced.
    private readonly List<LoopingSampleProvider> _loops = new();

    // The file each loaded layer is actually playing, and its reader, so the
    // layer editor's preview can tell that the path in the box has changed
    // since the layer was loaded and swap the new file in.
    private readonly Dictionary<string, (string FilePath, LoopingSampleProvider Loop)> _layerSources = new(StringComparer.OrdinalIgnoreCase);

    private bool _running;
    private Intensity _current = Intensity.Idle;

    // Where each layer is heading. The provider holds where it actually IS;
    // the fade ticker closes the gap between the two.
    private readonly Dictionary<string, float> _targets = new(StringComparer.OrdinalIgnoreCase);

    private System.Threading.Timer? _fadeTimer;

    /// <summary>
    /// How often volumes are stepped. 50ms is smooth to the ear without
    /// waking a timer more often than a slow fade needs.
    /// </summary>
    /// <summary>
    /// Fade tick. 15ms rather than 50 - at 20 steps a second each volume
    /// change is a small discontinuity in the waveform, and on sustained
    /// crowd noise that is heard as a series of clicks rather than as a
    /// smooth ramp. 66 steps a second puts the step rate well below the
    /// threshold where they read as separate events.
    /// </summary>
    private const int FadeIntervalMs = 15;

    /// <summary>
    /// The distance each layer's current fade set out to cover, so the ramp
    /// moves at a constant rate rather than decelerating toward the target.
    /// </summary>
    private readonly Dictionary<string, float> _fadeSpans = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The target each remembered span was set out for - a new target starts a new span.</summary>
    private readonly Dictionary<string, float> _fadeSpanTargets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How long the current fade-out-and-stop takes.</summary>
    private double _fadeOutSeconds = 4;

    /// <summary>
    /// When the current fade-out-and-stop should reach silence, on the
    /// Environment.TickCount64 clock. Only meaningful while _fadingOut.
    ///
    /// The fade-out is steered toward this moment rather than run as a
    /// fixed-length ramp from wherever it starts. A restart in the middle of
    /// the fade (a soundscape applied, a layer edited - see Restart) opens a
    /// new bed and starts each layer's ramp again from the level it had
    /// reached; spread over the full fade time, that left the crowd about
    /// half up when FadeOutAndStopAsync stopped it at the original time, and
    /// it was heard cut off. Aimed at the deadline, it arrives at silence on
    /// time however the fade was interrupted. It also absorbs a fade timer
    /// that ticks late, which would otherwise run the ramp past the stop.
    /// </summary>
    private long _fadeOutDeadlineMs;

    /// <summary>Below this the remaining difference is inaudible, so it's snapped rather than chased forever.</summary>
    /// <summary>
    /// How close counts as arrived. Dropped from 0.005 - that final snap
    /// was itself a step, small but audible against a quiet bed.
    /// </summary>
    private const float FadeSnapThreshold = 0.0008f;

    /// <summary>Raised with diagnostic detail - surfaced in the console like the other services.</summary>
    public event Action<string>? StatusMessage;

    public AmbienceSettings Settings { get; set; } = new();

    public bool IsRunning
    {
        get { lock (_lock) { return _running; } }
    }

    /// <summary>The underlying race-state mood, ignoring any momentary surge.</summary>
    public Intensity CurrentIntensity => _current;

    /// <summary>
    /// The level actually being played right now - the surge peak while one
    /// is active, otherwise the base mood. Separate from CurrentIntensity
    /// because a surge deliberately does NOT overwrite the base, so that
    /// settling back afterwards returns to the right place.
    /// </summary>
    public Intensity EffectiveIntensity => _surgeUntilUtc > DateTime.UtcNow ? _surgePeak : _current;

    /// <summary>What set off the current (or last) surge - shown on the Ambience tab.</summary>
    public string SurgeReason { get; private set; } = string.Empty;

    /// <summary>When the last surge started - for "last surge ... ago" once it has settled.</summary>
    public DateTime LastSurgeUtc { get; private set; } = DateTime.MinValue;

    /// <summary>The level the current surge lifted the crowd to.</summary>
    public Intensity SurgePeak => _surgePeak;

    /// <summary>Seconds until the current surge settles back. Zero when none is running.</summary>
    public double SurgeSecondsLeft => Math.Max(0, (_surgeUntilUtc - DateTime.UtcNow).TotalSeconds);

    /// <summary>True while a momentary surge is still running.</summary>
    public bool IsSurging => _surgeUntilUtc > DateTime.UtcNow;

    /// <summary>How far through the current surge, 0-1. Zero when none is active.</summary>
    public double SurgeProgress
    {
        get
        {
            var remaining = (_surgeUntilUtc - DateTime.UtcNow).TotalSeconds;
            if (remaining <= 0 || _surgeTotalSeconds <= 0)
            {
                return 0;
            }

            return Math.Clamp(1 - remaining / _surgeTotalSeconds, 0, 1);
        }
    }

    /// <summary>
    /// The volume each layer is currently at, keyed by layer. Read live for
    /// the settings page's meters - reflects mid-fade values rather than
    /// the target, which is the point of showing them.
    /// </summary>
    public IReadOnlyDictionary<string, float> CurrentLayerVolumes()
    {
        lock (_lock)
        {
            return _layers.ToDictionary(kv => kv.Key, kv => kv.Value.Volume, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Plays ONE layer's file at a given volume, muting the others, so a
    /// stage level can be auditioned while dragging its slider.
    ///
    /// Sets the provider volume directly rather than going through the fade
    /// engine - when someone is dragging a slider they want to hear the
    /// value under their finger immediately, not a 1.5-second ramp toward
    /// where they were a moment ago.
    /// </summary>
    public void PreviewLayerVolume(string layerKey, float volume, bool soloLayer = true)
    {
        lock (_lock)
        {
            if (!_running)
            {
                return;
            }

            foreach (var (key, provider) in _layers)
            {
                if (string.Equals(key, layerKey, StringComparison.OrdinalIgnoreCase))
                {
                    provider.Volume = Math.Clamp(volume * Settings.MasterVolume, 0f, 1f);

                    // Keep the fade engine from pulling it back toward an
                    // old target the moment the slider stops moving.
                    _targets[key] = provider.Volume;
                }
                else if (soloLayer)
                {
                    provider.Volume = 0f;
                    _targets[key] = 0f;
                }
            }

            // A preview is a manual override, so stop any fade in flight
            // rather than letting it fight the slider.
            StopFadeLocked();
        }
    }

    /// <summary>Ensures a layer exists in the mixer, adding it if the file was set after Start. Returns false if it can't be played.</summary>
    /// <param name="reloadIfFileChanged">
    /// For the layer editor's preview: if the layer is already loaded but
    /// from a different file than the one it names now, take the old file
    /// out and load the new one. Without it the preview kept playing the
    /// OLD file after a new one was picked, because the layer's key - which
    /// is all this used to check - hadn't changed.
    /// </param>
    public bool EnsureLayerLoaded(AmbienceLayer layer, bool reloadIfFileChanged = false)
    {
        lock (_lock)
        {
            if (!_running || _mixer is null)
            {
                return false;
            }

            if (_layers.TryGetValue(layer.Key, out var loaded))
            {
                if (!reloadIfFileChanged
                    || !_layerSources.TryGetValue(layer.Key, out var source)
                    || string.Equals(source.FilePath, layer.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Only swapped once the new file is known to exist - a path
                // half-typed into the box shouldn't silence the one playing.
                if (string.IsNullOrWhiteSpace(layer.FilePath) || !File.Exists(layer.FilePath))
                {
                    return false;
                }

                _mixer.RemoveMixerInput(loaded);
                _layers.Remove(layer.Key);
                _targets.Remove(layer.Key);
                _fadeSpans.Remove(layer.Key);
                _fadeSpanTargets.Remove(layer.Key);
                _layerSources.Remove(layer.Key);
                _loops.Remove(source.Loop);

                // After it is out of the mixer, so nothing is still reading it.
                source.Loop.Dispose();
            }

            if (string.IsNullOrWhiteSpace(layer.FilePath) || !File.Exists(layer.FilePath))
            {
                return false;
            }

            LoopingSampleProvider? loop = null;
            try
            {
                var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
                loop = new LoopingSampleProvider(layer.FilePath, format);
                var volume = new VolumeSampleProvider(loop) { Volume = 0f };

                // Into the mixer FIRST: it can reject the input (format), and
                // a layer recorded before that would be one that never plays.
                _mixer.AddMixerInput(volume);
                _layers[layer.Key] = volume;
                _targets[layer.Key] = 0f;
                _loops.Add(loop);
                _layerSources[layer.Key] = (layer.FilePath, loop);
                return true;
            }
            catch (Exception ex)
            {
                loop?.Dispose();
                StatusMessage?.Invoke($"[ambience] Couldn't load \"{layer.DisplayName}\": {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Ducks every layer while an announcement speaks, and releases
    /// afterwards.
    ///
    /// Applied as a MULTIPLIER over whatever the fade engine is already
    /// targeting, rather than a fixed level - so a duck during a peak surge
    /// still sits above a duck at idle, and the underlying intensity is
    /// untouched when it releases.
    /// </summary>
    /// <summary>
    /// Ducks to a specific depth, overriding the configured fraction.
    ///
    /// Used for the AI recap, which wants the bed further down than a
    /// one-line announcement does. Passing null restores the normal depth.
    /// </summary>
    public void SetDuckDepthOverride(float? fraction)
    {
        lock (_lock)
        {
            _duckOverride = fraction is { } f ? Math.Clamp(f, 0.02f, 1f) : null;

            if (_ducked)
            {
                ApplyDuckLocked();
            }
        }
    }

    private float? _duckOverride;

    /// <summary>
    /// Silences the crowd bed while a sponsor video with its own sound is
    /// on the Big Screen - see AdBreak, and MusicPlayer.SetAdBreakMuted,
    /// which says why this is a mute rather than a duck and why it ignores
    /// DuckUnderAnnouncements.
    ///
    /// The bed is included with the music because an ad talked over by a
    /// crowd is as broken as one talked over by a song, and a club that
    /// leaves the ambience running all night would otherwise have no way to
    /// get a clean spot.
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

            if (!_running)
            {
                return;
            }

            ApplyDuckLocked();
        }
    }

    private bool _adBreakMuted;

    public void SetDucked(bool ducked)
    {
        lock (_lock)
        {
            if (_ducked == ducked)
            {
                return;
            }

            // Recorded even while stopped. This used to return early when
            // not running, so a line that started before the crowd was
            // stopped left _ducked true, its "finished" was ignored, and the
            // next Start came back stuck at ducked level until another line
            // happened to speak.
            _ducked = ducked;

            if (!_running)
            {
                return;
            }
        }

        // Only the duck stage moves; the crowd's own level, and any ramp
        // it is part-way through, carry on untouched underneath.
        lock (_lock)
        {
            ApplyDuckLocked();
        }
    }

    private void ApplyDuckLocked()
    {
        if (_duckGain is not null)
        {
            _duckGain.Target = DuckFactor;
        }
    }

    private bool _ducked;

    /// <summary>
    /// Opened and running, but held at silence until <see cref="Release"/> -
    /// see <see cref="StartHeld"/>. Read and written under the lock.
    /// </summary>
    private bool _held;

    /// <summary>
    /// Opens the crowd's sound output now and keeps it silent (17 Oct 2026),
    /// for <see cref="Release"/> to bring up later.
    ///
    /// WHY. Opening a sound output makes Windows reconfigure the sound for a
    /// moment, and everything else playing stalls - the music used to skip
    /// like a scratched CD for exactly this reason (see AudioBus). The crowd
    /// opened its output the instant a race was staged, which with On Air
    /// is the instant the bumper and the staging call are playing - so the
    /// broadcast skipped right at the hand-over. On Air opens it here
    /// instead, before anything is on air, and staging only has to bring
    /// the level up.
    ///
    /// Does nothing to a crowd already running - holding that would silence
    /// it. Returns whether a held crowd is now open.
    /// </summary>
    public bool StartHeld(bool ignoreEnabledSetting = false)
    {
        lock (_lock)
        {
            if (_running)
            {
                return false;
            }

            _held = true;
        }

        StartBeds(ignoreEnabledSetting, keepFade: null);

        lock (_lock)
        {
            if (!_running)
            {
                _held = false;
                return false;
            }
        }

        StatusMessage?.Invoke("[ambience] Opened ahead of the race and held silent until it is staged.");
        return true;
    }

    /// <summary>Whether the crowd is open but held silent - see <see cref="StartHeld"/>.</summary>
    public bool IsHeld
    {
        get { lock (_lock) { return _held && _running; } }
    }

    /// <summary>Brings a held crowd up to where it should be - see <see cref="StartHeld"/>. Nothing when it isn't held.</summary>
    public void Release()
    {
        lock (_lock)
        {
            if (!_held)
            {
                return;
            }

            _held = false;
            ApplyDuckLocked();
        }

        StatusMessage?.Invoke("[ambience] Brought up for the race - its output was already open, so nothing else skipped.");
    }

    /// <summary>The factor applied to every layer while ducked. 1.0 when not.</summary>
    private float DuckFactor =>
        // An ad break wins over everything: silence, not a duck, and not
        // subject to DuckUnderAnnouncements. See SetAdBreakMuted. So does
        // a crowd held silent ahead of its race (see StartHeld).
        _adBreakMuted || _held ? 0f
        : _ducked && Settings.DuckUnderAnnouncements
            // The override wins when set - the recap ducks deeper than a
            // routine announcement does.
            ? Math.Clamp(_duckOverride ?? Settings.DuckToFraction, 0.02f, 1f)
            : 1f;

    /// <summary>
    /// How loud the ambience currently is overall, 0-1 - the loudest layer
    /// playing. Used to decide whether a spoken line needs a boost to cut
    /// through, so a quiet bed doesn't trigger one unnecessarily.
    /// </summary>
    public float CurrentLoudness()
    {
        lock (_lock)
        {
            return !_running || _layers.Count == 0 ? 0f : _layers.Values.Max(v => v.Volume);
        }
    }

    private DateTime _surgeUntilUtc = DateTime.MinValue;
    private Intensity _surgePeak = Intensity.Excited;
    private double _surgeTotalSeconds;

    /// <summary>
    /// Starts the ambience beds. Safe to call when already running, or when
    /// nothing is configured - both are no-ops rather than errors.
    /// </summary>
    /// <param name="ignoreEnabledSetting">
    /// Lets the settings page preview layers while ambience is switched
    /// OFF. Without it there's a deadlock: you'd have to enable ambience to
    /// hear whether your files work, but the sensible order is auditioning
    /// them first and enabling once they sound right.
    /// </param>
    public void Start(bool ignoreEnabledSetting = false)
    {
        // Still fading out from the last session: that fade is called off
        // and the crowd comes back, rather than the new session's start
        // being ignored and the bed then stopping under it.
        if (CancelFadeOut())
        {
            ApplyIntensity(EffectiveIntensity, force: false);
            return;
        }

        StartBeds(ignoreEnabledSetting, keepFade: null);
    }

    /// <summary>
    /// Stops the beds and opens them again, to pick up new settings - a
    /// soundscape applied, a layer edited, an undo. Layers are opened when
    /// the crowd starts, so a change to them needs this.
    ///
    /// Not simply Stop() then Start(), because of the fade at the end of a
    /// session. Stop() clears the fade-out, Start() then opened a fresh bed
    /// at the full mood, and when FadeOutAndStopAsync woke it found no fade
    /// in progress and left the crowd alone - so a setting changed in the
    /// last seconds of a session's fade brought the crowd back at idle with
    /// nothing left to stop it, and it played on through the gap between
    /// heats. Here a fade that was running carries on in the new bed, from
    /// the level each layer had reached, still aimed at silence by the same
    /// moment (_fadeOutDeadlineMs, which the restart leaves alone), and the
    /// pending fade still stops it when it is due.
    ///
    /// With no fade running this is exactly Stop() then Start(), and on a
    /// bed that isn't playing it is just Start().
    ///
    /// The whole thing runs under the lock (it is re-entrant, and Stop and
    /// StartBeds take it themselves). Otherwise the pending fade's wait
    /// could end in the gap between the stop and the new bed opening - it
    /// would find no fade, leave things alone, and the fade put back a
    /// moment later would then have nothing to stop it.
    /// </summary>
    /// <param name="ignoreEnabledSetting">As for Start.</param>
    public void Restart(bool ignoreEnabledSetting = false)
    {
        lock (_lock)
        {
            (int Generation, Dictionary<string, float> Levels)? keepFade = null;

            if (_fadingOut)
            {
                keepFade = (_fadeOutGeneration, _layers.ToDictionary(kv => kv.Key, kv => kv.Value.Volume, StringComparer.OrdinalIgnoreCase));
            }

            // A HELD CROWD STAYS HELD (17 Oct 2026, Bug Hunt 8, A12-2).
            // Stop clears the hold, so a soundscape or layer edit between
            // On Air and staging opened the new bed audible, over the
            // show's opener - and staging, finding it running and not
            // held, left it there. The new bed opens at the held level.
            var wasHeld = _held;

            Stop();

            _held = wasHeld;

            if (keepFade is null)
            {
                Start(ignoreEnabledSetting);

                if (!_running)
                {
                    _held = false;
                }

                return;
            }

            StartBeds(ignoreEnabledSetting, keepFade);

            if (!_running)
            {
                _held = false;
            }
        }
    }

    /// <summary>
    /// Opens the beds and starts playing. With <paramref name="keepFade"/>,
    /// the new bed carries on a fade-out the old one was in the middle of
    /// (see Restart) rather than coming up at the mood's level.
    /// </summary>
    private void StartBeds(bool ignoreEnabledSetting, (int Generation, Dictionary<string, float> Levels)? keepFade)
    {
        var fadeKept = false;

        lock (_lock)
        {
            if (_running || (!Settings.Enabled && !ignoreEnabledSetting))
            {
                return;
            }

            // PlayableLayers rather than every enabled one: a layer silenced
            // by the chosen source is never opened at all, rather than
            // opened and held at zero, so picking one side costs nothing to
            // run and releases the file handle.
            var configured = Settings.PlayableLayers.Where(l => File.Exists(l.FilePath)).ToList();
            if (configured.Count == 0)
            {
                // Said differently when it is the SOURCE that silenced them.
                // "No layer has a readable file set" would be a plain lie
                // about a club that has several and has just chosen the
                // other side, and would send them looking for a missing
                // file that isn't missing.
                var otherSide = Settings.Source != AmbienceSource.Both
                    && Settings.Layers.Any(l => l.Enabled && File.Exists(l.FilePath));

                StatusMessage?.Invoke(otherSide
                    ? $"[ambience] Nothing to play - every layer that's set up is on the other side of \"{Settings.Source}\"."
                    : "[ambience] Nothing to play - no layer has a readable file set.");
                return;
            }

            try
            {
                // 44.1kHz stereo: the format everything gets resampled into,
                // so files of mixed sample rates can share one mixer.
                var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
                _mixer = new MixingSampleProvider(format) { ReadFully = true };

                foreach (var layer in configured)
                {
                    LoopingSampleProvider? loop = null;
                    try
                    {
                        loop = new LoopingSampleProvider(layer.FilePath, format);
                        var volume = new VolumeSampleProvider(loop) { Volume = 0f };

                        // Mixer first - see EnsureLayerLoaded.
                        _mixer.AddMixerInput(volume);
                        _layers[layer.Key] = volume;
                        _loops.Add(loop);
                        _layerSources[layer.Key] = (layer.FilePath, loop);
                    }
                    catch (Exception ex)
                    {
                        // One bad file shouldn't silence the others - but its
                        // reader, if it got that far, must not stay open.
                        loop?.Dispose();
                        StatusMessage?.Invoke($"[ambience] Skipped \"{layer.Key}\": {ex.Message}");
                    }
                }

                if (_layers.Count == 0)
                {
                    _mixer = null;
                    return;
                }

                // Buffered as deeply as the music, for the same reason: it
                // ran dry while the announcer's output opened (see MusicPlayer).
                _output = new WaveOutEvent { DesiredLatency = MusicPlayer.BedLatencyMs, NumberOfBuffers = MusicPlayer.BedBuffers };
                _duckGain = new SmoothGainSampleProvider(_mixer, DuckFactor, DuckDownSeconds, DuckUpSeconds);
                // Sent to the device as 16-bit, exactly as AudioBus does: some
                // drivers play 32-bit float as silence without complaint. Every
                // gain in front of it is already held at or below 1.
                _output.Init(new SampleToWaveProvider16(_duckGain));

                // Watched like AudioBus and MusicPlayer watch theirs. Without
                // it, a device that went away (a Bluetooth speaker switched
                // off, a USB interface unplugged, a driver reset) or a layer
                // whose file could no longer be read stopped the output while
                // this went on reporting itself running - so the next race's
                // auto-start skipped it and the crowd stayed silent for good.
                _output.PlaybackStopped += OnOutputStopped;

                // A restart in the middle of a fade-out: the fade is put back,
                // under the generation the pending FadeOutAndStopAsync is
                // waiting with, so it still stops the crowd when it is due.
                // Each layer starts where the old one had faded to - not at
                // the mood's level, which would bring the crowd back up, and
                // not at silence, which would cut it off. A layer that wasn't
                // playing before stays at zero. Only if nothing has called
                // the fade off since (that moves the generation on).
                if (keepFade is { } fade && fade.Generation == _fadeOutGeneration)
                {
                    _fadingOut = true;
                    fadeKept = true;

                    foreach (var (key, volume) in _layers)
                    {
                        volume.Volume = fade.Levels.TryGetValue(key, out var level) ? level : 0f;
                    }
                }

                _output.Play();
                _running = true;

                StatusMessage?.Invoke($"[ambience] Started with {Plural.Of(_layers.Count, "layer")}.");
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"[ambience] Couldn't start: {ex.GetType().Name}: {ex.Message}");
                DisposeAudioLocked();
                fadeKept = false;
            }
        }

        if (fadeKept)
        {
            // Targets go to zero (ApplyIntensity does that while fading out)
            // and the ticker carries the layers down from where they are.
            ApplyIntensity(EffectiveIntensity, force: false);
            return;
        }

        ApplyIntensity(_current, force: true);
    }

    public void Stop()
    {
        IWavePlayer? retiredOutput = null;
        List<LoopingSampleProvider> retiredLoops = new();

        lock (_lock)
        {
            // A stopped crowd is never still held - the next start is a
            // normal one unless StartHeld says otherwise.
            _held = false;

            if (!_running)
            {
                return;
            }

            TakeAudioLocked(out retiredOutput, out retiredLoops);
            StatusMessage?.Invoke("[ambience] Stopped.");
        }

        // With the lock let go: the device is stopped and closed here, and
        // that is the slow part. This runs on the thread pool at the end of a
        // session's fade (FadeOutAndStopAsync), and the UI thread reads
        // IsRunning on every change of race state - it used to wait for the
        // whole of waveOutReset and waveOutClose for it. See TakeAudioLocked.
        //
        // What this does allow is a Start on the UI thread opening the next
        // bed while the last one is still closing. That is the same trade
        // AudioBus and OnOutputStopped already make, and it is inaudible
        // where it matters: the fade that leads to this Stop has already
        // taken the old bed to silence.
        DisposeRetired(retiredOutput, retiredLoops);
    }

    /// <summary>
    /// Fades every layer out over <paramref name="seconds"/>, then stops.
    ///
    /// Stopping at the end of a session used to cut the crowd off dead,
    /// often with the finish celebration still fading down - which sounded
    /// like the ambience broke rather than settled. Called off if anything
    /// brings the crowd back meanwhile: the next session starting, or a new
    /// mood or surge.
    /// </summary>
    public async Task FadeOutAndStopAsync(double seconds)
    {
        int generation;
        double fadeSeconds;

        lock (_lock)
        {
            if (!_running)
            {
                return;
            }

            generation = ++_fadeOutGeneration;
            _fadingOut = true;
            _fadeOutSeconds = Math.Clamp(seconds, 0.2, 30);
            fadeSeconds = _fadeOutSeconds;

            // The moment the crowd should be silent - the fade ticker aims
            // every layer at it (see _fadeOutDeadlineMs).
            _fadeOutDeadlineMs = Environment.TickCount64 + (long)(fadeSeconds * 1000);
        }

        ApplyIntensity(EffectiveIntensity, force: false);
        StatusMessage?.Invoke($"[ambience] Fading out over {fadeSeconds:0.#}s.");

        try
        {
            // The fade, plus a moment for the last of it to leave the buffer.
            // Kept in a local: another fade-out starting meanwhile changes
            // _fadeOutSeconds, and this one's wait should not move with it.
            await Task.Delay(TimeSpan.FromSeconds(fadeSeconds + 0.5));
        }
        catch
        {
        }

        lock (_lock)
        {
            if (!_fadingOut || _fadeOutGeneration != generation)
            {
                return;
            }
        }

        Stop();
    }

    /// <summary>Calls off a fade-out in progress, so the crowd comes back up. True if there was one.</summary>
    private bool CancelFadeOut()
    {
        lock (_lock)
        {
            if (!_fadingOut)
            {
                return false;
            }

            _fadingOut = false;
            _fadeOutGeneration++;
            return true;
        }
    }

    /// <summary>
    /// Sets how excited the crowd is. Called from race events - the actual
    /// per-layer volumes come from the settings, so a club can decide what
    /// "excited" sounds like rather than having it hardcoded.
    /// </summary>
    public void SetIntensity(Intensity intensity)
    {
        // A race mood brings a fading-out crowd back - the next session
        // staging. Idle doesn't: that is what a session ending (or being
        // reset) asks for, and it shouldn't call off the fade that ends it.
        var resumed = intensity != Intensity.Idle && CancelFadeOut();

        if (_current == intensity && !resumed)
        {
            return;
        }

        // A real mood change ends any surge still running. Left live, the
        // surge's peak was still what EffectiveIntensity reported, and its
        // settle timer gave up (the mood had moved) without clearing it - so
        // a fader drag in the Mixer (RefreshLevels) put the crowd back up to
        // Excited during a caution or while staging.
        if (_current != intensity)
        {
            _surgeUntilUtc = DateTime.MinValue;
        }

        _current = intensity;
        ApplyIntensity(intensity, force: false);
    }

    /// <summary>
    /// Re-applies the level the crowd is at right now - the mood, or the
    /// surge if one is running - using the current settings.
    ///
    /// For a volume change, which should be heard straight away without
    /// changing how worked-up the crowd is. SetIntensity can't do this: it
    /// does nothing when asked for the mood already set, and asking for any
    /// other mood to force a refresh moves the crowd to that mood - which is
    /// how the Mixer's fader put a race-level crowd on between races.
    /// </summary>
    public void RefreshLevels() => ApplyIntensity(EffectiveIntensity, force: false);

    /// <summary>
    /// A brief surge above the current level - for a single notable moment
    /// like a fast lap, that shouldn't permanently change the mood.
    /// </summary>
    public void Surge(Intensity peak = Intensity.Excited, string reason = "A race moment")
    {
        if (!IsRunning || !Settings.ReactToEvents)
        {
            return;
        }

        var restoreTo = _current;

        // Never a surge BELOW what is already playing. The crowd roar layer
        // is near-silent until Excited and full at Peak, so this is heard:
        // a fast lap straight after the checkered flag (base mood Peak) used
        // to pull the celebration down to Excited, and a lead change during
        // a record's Peak surge cut it to Excited for the rest of it.
        if (_current >= peak)
        {
            return;
        }

        if (IsSurging && _surgePeak > peak)
        {
            peak = _surgePeak;
        }

        var surgeSeconds = Math.Clamp(Settings.SurgeSeconds, 1, 30);
        var until = DateTime.UtcNow.AddSeconds(surgeSeconds);

        _surgePeak = peak;
        _surgeTotalSeconds = surgeSeconds;
        _surgeUntilUtc = until;
        SurgeReason = reason;
        LastSurgeUtc = DateTime.UtcNow;
        StatusMessage?.Invoke($"[ambience] Surge: {reason} - up to {peak} for {surgeSeconds}s.");

        // Faded, not forced. A surge used to jump the volume in a single
        // sample - a step discontinuity, which is exactly what a pop is.
        // It fires on every fast lap and lead change, so it was the most
        // audible of the two problems.
        ApplyIntensity(peak, force: false);

        // Fire-and-forget: nothing waits on the crowd settling back down.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(surgeSeconds));

                // Only restore if nothing else changed the mood meanwhile -
                // otherwise a surge would stomp a real state change that
                // happened while it was running.
                //
                // Nor if a later surge has taken over (it moved the end
                // time on): that one settles the crowd when IT ends. Two
                // moments a few seconds apart - a fast lap, then a lead
                // change - used to have the first one's timer drop the crowd
                // back mid-way through the second.
                if (_current == restoreTo && _surgeUntilUtc == until)
                {
                    _surgeUntilUtc = DateTime.MinValue;

                    // Fades back rather than snapping - this is the tail of
                    // a surge, exactly where a hard cut is most audible.
                    //
                    // The quick surge fade is cleared first: coming DOWN
                    // from a peak should take the normal, slower fade, or
                    // the crowd drops away unnaturally fast.
                    ApplyIntensity(restoreTo, force: false);
                }
            }
            catch
            {
                // Ambience is decoration - never surface a failure here.
            }
        });
    }

    /// <summary>
    /// Sets the TARGET volume for each layer. The actual volume is eased
    /// toward it by the fade ticker rather than jumped to - an instant
    /// change sounds like someone yanking a fader, which is exactly what
    /// ambience shouldn't draw attention to.
    /// </summary>
    /// <summary>
    /// Holds at peak for the celebration window, then fades to idle.
    ///
    /// Not a Surge: a surge returns to whatever the base mood was, and at a
    /// finish that base IS peak - which is exactly why the crowd used to
    /// roar forever after the checkered flag. This sets the base to idle so
    /// the fade engine genuinely brings it down, and it cannot be stomped
    /// by the next race because starting one sets Racing explicitly.
    /// </summary>
    public void CelebrateFinishThenSettle(int celebrationSeconds)
    {
        if (!IsRunning)
        {
            return;
        }

        var seconds = Math.Clamp(celebrationSeconds, 0, 300);

        _current = Intensity.Peak;
        _surgePeak = Intensity.Peak;
        _surgeTotalSeconds = seconds;
        _surgeUntilUtc = DateTime.UtcNow.AddSeconds(seconds);
        SurgeReason = "Checkered flag - finish celebration";
        LastSurgeUtc = DateTime.UtcNow;
        StatusMessage?.Invoke($"[ambience] Surge: {SurgeReason} - up to Peak for {seconds}s.");

        // Faded for the same reason as a surge.
        ApplyIntensity(Intensity.Peak, force: false);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds));

                // Only settle if the race is still finished - if another
                // race has since started, that set its own intensity and
                // dropping to idle now would mute the new race.
                if (_current == Intensity.Peak)
                {
                    _surgeUntilUtc = DateTime.MinValue;
                    _current = Intensity.Idle;

                    // Faded, not forced - this is the long wind-down after a
                    // race, the most audible place a hard cut could land.
                    ApplyIntensity(Intensity.Idle, force: false);
                }
            }
            catch
            {
                // Ambience is decoration - never surface a failure here.
            }
        });
    }

    private void ApplyIntensity(Intensity intensity, bool force)
    {
        lock (_lock)
        {
            if (!_running)
            {
                return;
            }

            foreach (var layer in Settings.Layers)
            {
                if (!_layers.TryGetValue(layer.Key, out var provider))
                {
                    continue;
                }

                // The duck is not in here - it is its own stage (_duckGain).
                var target = _fadingOut
                    ? 0f
                    : Math.Clamp(layer.VolumeFor(intensity) * Settings.MasterVolume, 0f, 1f);
                _targets[layer.Key] = target;

                // force is used when starting up or when the user is
                // tuning levels by ear on the settings page - both want the
                // change NOW rather than a fade they'd have to wait out.
                if (force)
                {
                    provider.Volume = target;
                }
            }

            if (!force)
            {
                StartFadeLocked();
            }
        }
    }

    /// <summary>
    /// Starts the fade ticker if it isn't already running. One shared timer
    /// eases every layer at once, rather than a task per layer that would
    /// fight each other when intensities change mid-fade.
    /// </summary>
    private void StartFadeLocked()
    {
        if (_fadeTimer is not null)
        {
            return;
        }

        _fadeTimer = new System.Threading.Timer(_ => FadeTick(), null, FadeIntervalMs, FadeIntervalMs);
    }

    private void FadeTick()
    {
        lock (_lock)
        {
            if (!_running)
            {
                StopFadeLocked();
                return;
            }

            var allSettled = true;

            foreach (var (key, provider) in _layers)
            {
                if (!_targets.TryGetValue(key, out var target))
                {
                    continue;
                }

                var current = provider.Volume;
                var delta = target - current;

                // Close enough - snap the last sliver so a layer can't sit
                // forever approaching its target without arriving.
                if (Math.Abs(delta) <= FadeSnapThreshold)
                {
                    provider.Volume = target;

                    // Span dropped on arrival, not just when every layer
                    // settles - otherwise a layer that finishes early keeps
                    // a stale span and its next fade runs at the wrong rate.
                    _fadeSpans.Remove(key);
                    _fadeSpanTargets.Remove(key);
                    continue;
                }

                allSettled = false;

                // Fading out to stop: each step covers this tick's share of
                // what is left before the deadline, so every layer is a
                // straight line from wherever it is now to silence at the
                // moment FadeOutAndStopAsync stops the output - even a layer
                // whose ramp was started again partway through by a restart
                // (see _fadeOutDeadlineMs). Within the last tick it is
                // simply put at the target. Spans aren't used for this, so
                // any left over are dropped and the next ordinary fade
                // starts its own.
                if (_fadingOut)
                {
                    _fadeSpans.Remove(key);
                    _fadeSpanTargets.Remove(key);

                    var remainingMs = _fadeOutDeadlineMs - Environment.TickCount64;
                    provider.Volume = remainingMs <= FadeIntervalMs
                        ? target
                        : Math.Clamp(current + delta * (FadeIntervalMs / (float)remainingMs), 0f, 1f);
                    continue;
                }

                // Fades DOWN slower than up. A crowd swelling is quick; a
                // crowd settling takes a moment, and matching that makes
                // the transition read as natural rather than mechanical.
                // A surge uses its own quick fade rather than the normal
                // one. 1.5 seconds would rob a fast-lap reaction of its
                // impact, but an instant jump clicks - ~180ms is fast
                // enough to feel immediate and long enough to be silent.
                // The club's own ramp and fade, for everything - surges and
                // the finish included. A surge used to rise in a fixed 0.18s
                // whatever Fade up was set to, so the ramp set on the
                // Ambience tab never applied to the moments it is heard on.
                // (A fade-out to stop is handled above, against its deadline.)
                var seconds = delta > 0 ? Settings.FadeUpSeconds : Settings.FadeDownSeconds;
                // A LINEAR step toward the target, not a proportional one.
                //
                // The old version moved a fraction of the REMAINING distance
                // each tick, which is an exponential approach: quick at
                // first, then slower and slower, never quite arriving -
                // hence the snap threshold that finished it off with a jump.
                // A fixed step per tick arrives cleanly and sounds even.
                var totalTicks = Math.Max(1f, (float)(Math.Clamp(seconds, 0.05, 30) * 1000 / FadeIntervalMs));
                var stepSize = 1f / totalTicks;

                // The full span this fade covers, remembered when it starts,
                // so the rate stays constant instead of shrinking as the gap
                // closes.
                //
                // Started afresh whenever the target moves, not only when the
                // direction flips: a new, higher peak arriving mid-rise kept
                // the old, smaller span, so the rest of the rise ran at the
                // wrong rate and took longer or shorter than the setting.
                if (!_fadeSpans.TryGetValue(key, out var span)
                    || !_fadeSpanTargets.TryGetValue(key, out var spanTarget)
                    || Math.Abs(spanTarget - target) > FadeSnapThreshold
                    || Math.Sign(span) != Math.Sign(delta))
                {
                    span = delta;
                    _fadeSpans[key] = span;
                    _fadeSpanTargets[key] = target;
                }

                var move = Math.Abs(span) * stepSize;

                provider.Volume = delta > 0
                    ? Math.Clamp(Math.Min(current + move, target), 0f, 1f)
                    : Math.Clamp(Math.Max(current - move, target), 0f, 1f);
            }

            // Nothing left to move - stop ticking rather than spin a timer
            // for the rest of the race day.
            if (allSettled)
            {
                _fadeSpans.Clear();
                _fadeSpanTargets.Clear();
                StopFadeLocked();
            }
        }
    }

    private void StopFadeLocked()
    {
        _fadeTimer?.Dispose();
        _fadeTimer = null;
    }

    /// <summary>
    /// The output stopped without being asked to - the device went away, or
    /// reading one of the layers threw on the playback thread. Everything is
    /// torn down so the service honestly reports itself stopped: the Mixer
    /// stops saying "Playing", and the next Start - the auto-start when the
    /// next race stages, or the club pressing Start - opens whatever output
    /// there is now.
    ///
    /// It deliberately does not reopen straight away. A file that has
    /// become unreadable (on a USB stick or network share that dropped out)
    /// would fail again at once, and an immediate restart would loop on it;
    /// waiting for the next Start costs at most the rest of one race.
    /// </summary>
    private void OnOutputStopped(object? sender, StoppedEventArgs e)
    {
        IWavePlayer? dead;

        lock (_lock)
        {
            // Only the output currently in use. An earlier one's event can
            // arrive late (it is posted to the UI thread when Start ran
            // there), after a Stop and Start have already opened a new one.
            if (_output is null || !ReferenceEquals(sender, _output))
            {
                return;
            }

            dead = _output;
            dead.PlaybackStopped -= OnOutputStopped;

            // Taken out before the teardown so DisposeAudioLocked doesn't
            // stop and dispose the device from inside its own event - that
            // is done on another thread below, as AudioBus does.
            _output = null;
            DisposeAudioLocked();
        }

        _ = Task.Run(() =>
        {
            try
            {
                dead?.Dispose();
            }
            catch
            {
                // Disposing a device that's already gone is not worth reporting.
            }
        });

        StatusMessage?.Invoke(e.Exception is { } ex
            ? $"[ambience] Playback stopped: {ex.GetType().Name}: {ex.Message} - it will start again with the next race, or press Start."
            : "[ambience] Playback stopped unexpectedly - it will start again with the next race, or press Start.");
    }

    /// <summary>
    /// Takes the bed out of the fields and hands back the parts that have to
    /// be let go of, for the caller to pass to DisposeRetired with the lock
    /// released.
    ///
    /// The device used to be stopped and disposed in here, under the lock:
    /// waveOutReset and waveOutClose, which take as long as the sound system
    /// makes them take - a Bluetooth speaker reconfigures itself, and one
    /// that has wandered off takes seconds. Stop runs on the thread pool at
    /// the end of a session's fade (FadeOutAndStopAsync is awaited from a
    /// Task.Run in MainForm.Audio), and the UI thread reads IsRunning on
    /// every change of race state, so it waited for all of it.
    ///
    /// Nothing can reach the output once this has returned: it is out of the
    /// field with its PlaybackStopped unhooked, and OnOutputStopped ignores
    /// an event whose sender is not the current one anyway. The loops go
    /// with it rather than being disposed here, because they must not be
    /// closed until the device that is reading them has stopped.
    /// </summary>
    private void TakeAudioLocked(out IWavePlayer? output, out List<LoopingSampleProvider> loops)
    {
        output = _output;

        // Unhooked first: this is a stop we asked for, and its
        // PlaybackStopped must not be read as the device going away.
        if (output is not null)
        {
            output.PlaybackStopped -= OnOutputStopped;
        }

        loops = _loops.ToList();
        _loops.Clear();

        StopFadeLocked();

        _output = null;
        _mixer = null;
        _duckGain = null;
        _fadingOut = false;
        _layers.Clear();
        _layerSources.Clear();
        _targets.Clear();

        // With the fade spans too: one left from a fade the Stop cut short
        // set the rate of the same layer's first fade after the next Start.
        _fadeSpans.Clear();
        _fadeSpanTargets.Clear();
        _running = false;

        // A crowd that is not open is not held either (17 Oct 2026, Bug
        // Hunt 8, A12-1). The device going away came through here and left
        // the hold set, so IsHeld read false, staging never released it,
        // and the next Start opened its bed at the held level of zero for
        // the whole race. Restart carries a hold over on purpose.
        _held = false;
    }

    /// <summary>
    /// Stops and lets go of a bed taken out by TakeAudioLocked. Call it with
    /// the lock released.
    /// </summary>
    private static void DisposeRetired(IWavePlayer? output, List<LoopingSampleProvider> loops)
    {
        try
        {
            output?.Stop();
            output?.Dispose();
        }
        catch
        {
            // Disposing a device that's already gone is not worth reporting.
        }

        // After the output is stopped, so nothing is still pulling samples
        // from these (and Read is guarded against a late call regardless).
        foreach (var loop in loops)
        {
            loop.Dispose();
        }
    }

    /// <summary>
    /// Takes the bed out and lets it go in one step, with the lock held.
    ///
    /// Left as it was for the two places that are already on the thread that
    /// would have been doing the waiting: StartBeds, when opening the output
    /// throws (it runs on the UI thread, and is called from Restart with the
    /// lock deliberately held across the whole restart), and OnOutputStopped,
    /// which has taken the dead device out itself already and disposes it on
    /// another thread. Stop is the one that had to be split.
    /// </summary>
    private void DisposeAudioLocked()
    {
        TakeAudioLocked(out var output, out var loops);
        DisposeRetired(output, loops);
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Reads a file on repeat, forever, resampled into the mixer's format.
    ///
    /// Ambience files are typically short loops, so rewinding at the end is
    /// what makes a ten-second crowd recording usable as a continuous bed.
    /// </summary>
    private sealed class LoopingSampleProvider : ISampleProvider, IDisposable
    {
        private readonly AudioFileReader _reader;
        private readonly ISampleProvider _resampled;

        // Read runs on the output device's thread, Dispose on whichever
        // thread stops the ambience - this keeps a late Read from touching
        // a reader that has just been closed.
        private readonly object _sync = new();
        private bool _disposed;

        public LoopingSampleProvider(string path, WaveFormat targetFormat)
        {
            _reader = new AudioFileReader(path);

            try
            {
                ISampleProvider source = _reader;

                // Mono sources get widened to stereo, or the mixer rejects them.
                if (source.WaveFormat.Channels == 1 && targetFormat.Channels == 2)
                {
                    source = new MonoToStereoSampleProvider(source);
                }

                _resampled = source.WaveFormat.SampleRate != targetFormat.SampleRate
                    ? new WdlResamplingSampleProvider(source, targetFormat.SampleRate)
                    : source;
            }
            catch
            {
                // The caller never gets an instance to dispose, so close the file here.
                _reader.Dispose();
                throw;
            }
        }

        public WaveFormat WaveFormat => _resampled.WaveFormat;

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _reader.Dispose();
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            lock (_sync)
            {
                // Silence rather than an exception on the audio thread.
                return _disposed ? 0 : ReadLocked(buffer, offset, count);
            }
        }

        private int ReadLocked(float[] buffer, int offset, int count)
        {
            var written = 0;

            while (written < count)
            {
                var read = _resampled.Read(buffer, offset + written, count - written);

                if (read == 0)
                {
                    // End of file - rewind and keep going. If the file is
                    // empty this would spin forever, so bail instead.
                    if (_reader.Position == 0)
                    {
                        break;
                    }

                    _reader.Position = 0;
                    continue;
                }

                written += read;
            }

            return written;
        }
    }
}
