using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace HeatCaller;

/// <summary>
/// One output, kept open for the whole run, that every announcer line and
/// sound effect plays through.
///
/// Each line and each effect used to open a sound output of its own and
/// close it when done. Opening one is not free: the sound system - and a
/// Bluetooth speaker most of all - reconfigures itself for the new stream,
/// and for that moment everything else playing stalls. The music, on its
/// own output, skipped like a scratched CD just before every effect and
/// every line, which is exactly when the new output was being opened. The
/// same wake-up is why the generated tones carried nearly a second of
/// silence in front: a speaker coming out of standby swallowed their start.
///
/// Here the output opens once and plays silence between sounds, so the
/// device never stops and never has to wake; a sound is just mixed into a
/// stream that is already running. Music and the crowd keep their own
/// outputs - those open once per track or session, not once per sound.
///
/// Everything is converted to one format on the way in (44.1 kHz stereo),
/// and sent to the device as 16-bit, which every driver plays - some play
/// 32-bit float as silence without complaint.
/// </summary>
internal static class AudioBus
{
    private const int SampleRate = 44100;

    /// <summary>
    /// The rate the mix runs at, for anything that takes the mix as it is
    /// rather than handing a sound in - see <see cref="IMixListener"/>.
    /// Named so a listener never has to guess it, and so it can never need
    /// a resampler: resampling the mix would be work on the device's own
    /// thread, which is the one thing this class exists to keep clear of.
    /// </summary>
    public const int MixSampleRate = SampleRate;

    /// <summary>See <see cref="MixSampleRate"/>. The mix is interleaved stereo.</summary>
    public const int MixChannels = 2;

    /// <summary>
    /// Silence still worth putting in front of a sound. The device is
    /// already awake, so this only has to cover nothing - kept as a hair
    /// of space so a sound doesn't begin on a hard edge.
    /// </summary>
    public const double LeadInSeconds = 0.05;

    private static readonly WaveFormat MixFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);

    private static readonly object Gate = new();
    private static WaveOutEvent? _output;
    private static MixingSampleProvider? _mixer;
    private static readonly List<Clip> Playing = new();

    // WHY (20 Oct 2026): opening the output (waveOutOpen) is the slow call -
    // a Bluetooth speaker reconfiguring, or one that has wandered off, takes
    // seconds. It used to run inside PlayAsync, under Gate, on whatever
    // thread asked for the sound - and after a speaker dropped out that was
    // the UI thread, so the first chime after it froze Race Control and the
    // Big Screen. Now the open runs on the pool, outside Gate, and every
    // caller that arrives while it is going on shares this one open.
    // Guarded by Gate; cleared when the open finishes, either way, so a
    // failed open is tried again on the next sound, as before.
    private static Task<MixingSampleProvider>? _opening;

    // The output being opened, not yet published - so that if it stops
    // before it is published (its PlaybackStopped would otherwise be thrown
    // away as "not ours"), the open is treated as failed rather than
    // publishing an output that is already dead. Both guarded by Gate.
    private static WaveOutEvent? _pending;
    private static bool _pendingDied;

    /// <summary>
    /// Opens the output now rather than on the first sound, so that first
    /// sound doesn't pay for the wake-up - and nor does the music. Never
    /// throws: a PC with no sound device just stays quiet. Blocks until the
    /// open is done, so call it off the UI thread (MainForm uses Task.Run).
    /// </summary>
    public static void Warm()
    {
        try
        {
            Task<MixingSampleProvider> opening;

            lock (Gate)
            {
                if (_output is not null && _mixer is not null)
                {
                    return;
                }

                opening = OpenLocked();
            }

            opening.Wait();
        }
        catch
        {
            // Tried again on the first sound.
        }
    }

    /// <summary>
    /// Opens no audio device and plays nothing, for the test run.
    ///
    /// Set once by TestSetup before any test runs, and never by the app.
    /// Without it the suite is audible: the tests that exercise the
    /// announcer end to end really do fetch a voice and really do play it,
    /// several at once and over each other, out of a laptop that is
    /// supposed to be running tests. Worse than the noise, it is a test
    /// run whose timing depends on a sound card.
    ///
    /// Here rather than in each test, and here rather than muting the
    /// announcer: muting is a behaviour those tests are checking (a muted
    /// announcer returns before it logs what it would have played), so a
    /// test that muted to stay quiet would no longer test anything. This
    /// is the last step before the speaker, below everything any test has
    /// an opinion about.
    /// </summary>
    public static bool Silent { get; set; }

    /// <summary>
    /// Plays a sound and completes when it has been handed to the device -
    /// cut short, and completed straight away, if the token is cancelled.
    /// When there is no usable output the returned task faults with
    /// InvalidOperationException (since 20 Oct 2026 - it used to be thrown
    /// from this call, but the open now happens off the caller's thread).
    /// </summary>
    ///
    /// With <paramref name="allowBoost"/> the gain may go above unity, up to
    /// <see cref="MaxBoost"/>, and the clip is passed through a soft limiter
    /// (SoftLimiter) so the boosted peaks bend rather than hard-clip in the
    /// 16-bit output. Only the announcer asks for it - the "boost voice over
    /// loud ambience" setting, which a 0..1 clamp here silently cancelled at
    /// the default 100% announcer level. Everything else stays at or below
    /// unity, exactly as before.
    ///
    /// With <paramref name="voice"/> the sound is the announcer speaking, and
    /// shows on the VOICE level meter rather than SFX (26 Oct 2026 - see
    /// AudioLevels). Only the announcer's lines (AnnouncementService's
    /// PlayPcmAsync) say so; anything that does not is an effect, which is
    /// what nearly everything else here is - recordings played back (a
    /// replayed moment, a driver's own clip) included, as they carry the
    /// room's sound with the call.
    public static Task PlayAsync(ISampleProvider source, float volume, CancellationToken cancellationToken = default, bool allowBoost = false, bool voice = false)
    {
        if (Silent || cancellationToken.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        var gain = float.IsNaN(volume) ? 0f : Math.Clamp(volume, 0f, allowBoost ? MaxBoost : 1f);
        ISampleProvider gained = new VolumeSampleProvider(ToMixFormat(source)) { Volume = gain };

        if (gain > 1f)
        {
            gained = new SoftLimiter(gained);
        }

        var clip = new Clip(gained, voice);

        MixingSampleProvider? mixer = null;
        Task<MixingSampleProvider>? opening = null;

        lock (Gate)
        {
            if (_output is not null && _mixer is not null)
            {
                mixer = _mixer;
                Playing.Add(clip);
            }
            else
            {
                opening = OpenLocked();
            }
        }

        // WHY (20 Oct 2026): with the output running - nearly always - this
        // is exactly as before. With it closed (first sound, or after the
        // speaker dropped out) the open happens on the pool and the sound
        // starts when it is done, so the caller's thread never waits on
        // the device. "No usable output" now arrives as a faulted task
        // rather than a throw from this call; every caller already handles
        // a faulted task, since a speaker can go away mid-sound.
        if (mixer is null)
        {
            return PlayWhenOpenAsync(opening!, clip, cancellationToken);
        }

        return Start(mixer, clip, cancellationToken);
    }

    /// <summary>The rest of PlayAsync for a sound that had to wait for the output to open.</summary>
    private static async Task PlayWhenOpenAsync(Task<MixingSampleProvider> opening, Clip clip, CancellationToken cancellationToken)
    {
        MixingSampleProvider mixer;

        try
        {
            mixer = await opening.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cut short before it started: completed straight away, as
            // PlayAsync promises. The open carries on for the next sound.
            return;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"No usable audio output device: {ex.Message}", ex);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        lock (Gate)
        {
            // The output could have stopped between being published and
            // here. Adding to a dead mixer would leave the sound waiting
            // forever, since nothing would ever read or orphan it.
            if (!ReferenceEquals(mixer, _mixer))
            {
                throw new InvalidOperationException("The audio output stopped.");
            }

            Playing.Add(clip);
        }

        await Start(mixer, clip, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Hands a clip, already in <see cref="Playing"/>, to the running mixer.</summary>
    private static Task Start(MixingSampleProvider mixer, Clip clip, CancellationToken cancellationToken)
    {
        mixer.AddMixerInput(clip);

        if (cancellationToken.CanBeCanceled)
        {
            var registration = cancellationToken.Register(() =>
            {
                mixer.RemoveMixerInput(clip);
                clip.Finish(null);
            });

            _ = clip.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
        }

        return clip.Task;
    }

    /// <summary>
    /// The open in progress, or a new one started on the pool. Call under
    /// Gate, and only when the output is not running.
    /// </summary>
    private static Task<MixingSampleProvider> OpenLocked()
    {
        if (_opening is null)
        {
            _opening = Task.Run(OpenAndPublish);

            // WHY (20 Oct 2026): a failed open is always observed here. Its
            // only waiter can be a sound that was cut short before the open
            // finished, which returns without looking - and an unobserved
            // fault is written to the crash log as an "Unobserved background
            // error" (Program.cs) for what is only a missing speaker.
            _ = _opening.ContinueWith(
                done => _ = done.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        return _opening;
    }

    /// <summary>
    /// Opens the output - outside Gate, on the pool - and publishes it under
    /// Gate. Runs as the one shared <see cref="_opening"/>.
    /// </summary>
    private static MixingSampleProvider OpenAndPublish()
    {
        try
        {
            // ReadFully: the mixer never runs dry, so the device plays silence
            // between sounds instead of stopping.
            var mixer = new MixingSampleProvider(MixFormat) { ReadFully = true };
            var output = OpenOutput(mixer);

            lock (Gate)
            {
                var died = _pendingDied;
                _pending = null;
                _pendingDied = false;

                if (died || (_output is not null && _mixer is not null))
                {
                    // Not from inside anything of its own, and never under Gate.
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            output.Dispose();
                        }
                        catch
                        {
                        }
                    });

                    if (died)
                    {
                        throw new InvalidOperationException("The audio output stopped as soon as it opened.");
                    }

                    return _mixer!;
                }

                _output = output;
                _mixer = mixer;
                return mixer;
            }
        }
        finally
        {
            lock (Gate)
            {
                _opening = null;
                _pending = null;
                _pendingDied = false;
            }
        }
    }

    /// <summary>The output's buffering: about a third of a second over three buffers (see OpenOutput). Named for the VOICE meter, which needs to know when what it reads is heard (AudioLevels).</summary>
    private const int OutputLatencyMs = 300;

    /// <inheritdoc cref="OutputLatencyMs"/>
    private const int OutputBuffers = 3;

    /// <summary>The slow part: opens the device and starts it playing the mixer. Touches no shared state but <see cref="_pending"/>.</summary>
    private static WaveOutEvent OpenOutput(MixingSampleProvider mixer)
    {
        // Not the shortest buffer possible: some drivers play nothing at
        // all from a very short one (see ToneGenerator), and a continuous
        // stream that runs dry glitches. About a third of a second.
        var output = new WaveOutEvent { DesiredLatency = OutputLatencyMs, NumberOfBuffers = OutputBuffers };

        lock (Gate)
        {
            _pending = output;
            _pendingDied = false;
        }

        try
        {
            // Through Tapped, always, whether anything is listening or not:
            // with no listener it is one null check per buffer, and building
            // the chain the same way every time means a recording started
            // mid-evening never has to touch an output that is already
            // running - which would mean stopping it, and stopping it is
            // the stall this whole class exists to avoid.
            output.Init(new SampleToWaveProvider16(new Tapped(mixer)));
            output.PlaybackStopped += OnPlaybackStopped;
            output.Play();
        }
        catch
        {
            output.Dispose();
            throw;
        }

        return output;
    }

    /// <summary>
    /// The device went away - unplugged, or a Bluetooth speaker switched
    /// off. Everything playing is finished with the error, and the next
    /// sound opens whatever output there is now.
    /// </summary>
    private static void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        List<Clip> orphaned;

        lock (Gate)
        {
            if (!ReferenceEquals(sender, _output))
            {
                // Stopped while still being opened, before it was published:
                // OpenAndPublish sees this and fails the open instead.
                if (sender is not null && ReferenceEquals(sender, _pending))
                {
                    _pendingDied = true;
                }

                return;
            }

            var dead = _output;
            _output = null;
            _mixer = null;

            orphaned = Playing.ToList();
            Playing.Clear();

            // Not from inside its own event.
            _ = Task.Run(() =>
            {
                try
                {
                    dead?.Dispose();
                }
                catch
                {
                }
            });
        }

        var error = e.Exception ?? new InvalidOperationException("The audio output stopped.");

        foreach (var clip in orphaned)
        {
            clip.Finish(error);
        }
    }

    /// <summary>Any mono or stereo source, as 44.1 kHz stereo float.</summary>
    private static ISampleProvider ToMixFormat(ISampleProvider source)
    {
        var shaped = source;

        if (shaped.WaveFormat.Channels == 1)
        {
            shaped = new MonoToStereoSampleProvider(shaped);
        }
        else if (shaped.WaveFormat.Channels != 2)
        {
            throw new NotSupportedException($"{shaped.WaveFormat.Channels}-channel audio isn't supported.");
        }

        if (shaped.WaveFormat.SampleRate != SampleRate)
        {
            shaped = new WdlResamplingSampleProvider(shaped, SampleRate);
        }

        return shaped;
    }

    /// <summary>One sound in the mix. Finishes when its source runs out, or when it is cut off.</summary>
    /// <summary>The most a boosted clip is raised by - see PlayAsync.</summary>
    public const float MaxBoost = 2f;

    /// <summary>
    /// Something that wants the finished mix as it goes to the speaker -
    /// the race recorder, and nothing else so far.
    ///
    /// <see cref="Mixed"/> is called ON THE SOUND DEVICE'S OWN THREAD, which
    /// makes the contract short and absolute: copy what you need and
    /// return. No disk, no network, no lock that anything slow stands
    /// behind, no allocation worth the name, and NOTHING THAT THROWS - an
    /// exception from here would come out of the device's read, which is
    /// how the music used to skip. A listener that does throw is dropped
    /// rather than tried again.
    ///
    /// The buffer is the mixer's own and is reused the moment this returns,
    /// so it must be copied, never kept.
    /// </summary>
    public interface IMixListener
    {
        /// <param name="buffer">Interleaved stereo floats at <see cref="MixSampleRate"/> - see <see cref="MixChannels"/>.</param>
        /// <param name="offset">Where this read's samples start in the buffer.</param>
        /// <param name="count">How many samples (not frames) are this read's.</param>
        void Mixed(float[] buffer, int offset, int count);
    }

    /// <summary>
    /// The one listener, or null. One rather than a list on purpose: a list
    /// would be walked on the device's thread, and the only thing that wants
    /// the mix is a recording, of which there is one at a time.
    /// </summary>
    private static volatile IMixListener? _listener;

    /// <summary>
    /// Starts giving <paramref name="listener"/> the mix. Replaces whatever
    /// was listening - a second recording never quietly joins a first.
    /// Opens nothing and stops nothing, so it is safe mid-race.
    /// </summary>
    public static void Listen(IMixListener listener) => _listener = listener;

    /// <summary>
    /// Whether anything is taking a copy of the mix right now.
    ///
    /// ADDED 11 Oct 2026 for one reason, and it is worth stating because
    /// the field above is a SINGLE listener on purpose. Starting a second
    /// recorder silently displaces the first, so the thing at risk is a
    /// club's recording of a race - which would simply end wherever the
    /// second one started, with nothing anywhere to say why. Anything that
    /// wants to start recording asks this first. See BookendRecording.
    /// </summary>
    public static bool Listening => _listener is not null;

    /// <summary>Stops giving this listener the mix. Does nothing when something else is listening by now.</summary>
    public static void StopListening(IMixListener listener)
    {
        if (ReferenceEquals(_listener, listener))
        {
            _listener = null;
        }
    }

    /// <summary>
    /// Hands each read of the mix to the listener on its way to the device -
    /// see <see cref="IMixListener"/>. With nobody listening it is a null
    /// check and a return.
    /// </summary>
    private sealed class Tapped : ISampleProvider
    {
        private readonly ISampleProvider _source;

        /// <summary>
        /// The VOICE and SFX strips' levels (25-26 Oct 2026) - see
        /// AudioLevels. Not the listener above: a meter must never take a
        /// recording's one slot. One pair per output, as the blocks in
        /// progress belong to this stream.
        /// </summary>
        private readonly AudioLevels.Feeder _voiceLevels = new(AudioLevels.Voice, MixSampleRate, MixChannels, OutputLatencyMs, OutputBuffers);
        private readonly AudioLevels.Feeder _effectLevels = new(AudioLevels.Effects, MixSampleRate, MixChannels, OutputLatencyMs, OutputBuffers);

        // The two sub-mixes of this read: each clip adds itself to one as
        // the mixer reads it (Clip.Read -> Heard). Grown once, to the
        // output's buffer, then reused - no allocation per read.
        private float[] _voiceMix = Array.Empty<float>();
        private float[] _effectMix = Array.Empty<float>();

        /// <summary>
        /// The output whose mixer is reading on this thread right now, so a
        /// clip can find the sub-mix to add itself to. Per thread: each
        /// output reads on its own device thread, and this is set only for
        /// the length of its mixer's read.
        /// </summary>
        [ThreadStatic]
        private static Tapped? _reading;

        public Tapped(ISampleProvider source) => _source = source;

        /// <summary>
        /// A clip's samples for this read, added to the sub-mix it belongs
        /// to. The mixer reads every clip into the start of its own buffer
        /// for the same count it was asked for, and adds sample n into
        /// output n - so the clip's sample n is the sub-mix's sample n too.
        /// Called on the device's thread from inside the mixer's read; never
        /// throws.
        /// </summary>
        public static void Heard(bool voice, float[] buffer, int offset, int count)
        {
            if (_reading is not { } tapped || count <= 0 || offset < 0 || offset + count > buffer.Length)
            {
                return;
            }

            var mix = voice ? tapped._voiceMix : tapped._effectMix;
            var n = Math.Min(count, mix.Length);

            for (var i = 0; i < n; i++)
            {
                mix[i] += buffer[offset + i];
            }
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            if (_voiceMix.Length < count)
            {
                _voiceMix = new float[count];
                _effectMix = new float[count];
            }
            else
            {
                Array.Clear(_voiceMix, 0, count);
                Array.Clear(_effectMix, 0, count);
            }

            int read;
            _reading = this;

            try
            {
                read = _source.Read(buffer, offset, count);
            }
            finally
            {
                _reading = null;
            }

            // Never throws (AudioLevels.Feeder.Feed).
            if (read > 0)
            {
                _voiceLevels.Feed(_voiceMix, 0, read);
                _effectLevels.Feed(_effectMix, 0, read);
            }

            if (read > 0 && _listener is { } listener)
            {
                try
                {
                    listener.Mixed(buffer, offset, read);
                }
                catch
                {
                    // A listener is a nice-to-have and this is the device's
                    // thread. One that throws is taken off rather than
                    // given another chance to do it on the next buffer.
                    StopListening(listener);
                }
            }

            return read;
        }
    }

    /// <summary>
    /// Leaves a sample alone up to the knee, and bends anything above it
    /// smoothly towards full scale (a tanh curve), so a boosted voice line
    /// never exceeds 1.0 - and so never hard-clips when the mixer's floats
    /// are turned into 16-bit samples. ElevenLabs lines already peak near
    /// full scale, so without this a 1.3x boost crackled.
    /// </summary>
    private sealed class SoftLimiter : ISampleProvider
    {
        private const float Knee = 0.9f;
        private const float Room = 1f - Knee;

        private readonly ISampleProvider _source;

        public SoftLimiter(ISampleProvider source) => _source = source;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);

            for (var i = offset; i < offset + read; i++)
            {
                var sample = buffer[i];
                var size = Math.Abs(sample);

                if (size > Knee)
                {
                    var bent = Knee + Room * (float)Math.Tanh((size - Knee) / Room);
                    buffer[i] = sample < 0 ? -bent : bent;
                }
            }

            return read;
        }
    }

    private sealed class Clip : ISampleProvider
    {
        private readonly ISampleProvider _source;

        /// <summary>The announcer speaking, for the VOICE meter; else SFX (see PlayAsync).</summary>
        private readonly bool _voice;

        // Continuations off the audio thread: whoever awaits this goes on to
        // do real work, and the device's own thread must not wait for it.
        private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private volatile bool _finished;

        public Clip(ISampleProvider source, bool voice)
        {
            _source = source;
            _voice = voice;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public Task Task => _done.Task;

        public int Read(float[] buffer, int offset, int count)
        {
            if (_finished)
            {
                return 0;
            }

            int read;

            try
            {
                read = _source.Read(buffer, offset, count);
            }
            catch (Exception ex)
            {
                // A sound that fails to read must not take the whole bus
                // down - it drops out of the mix and reports why.
                Finish(ex);
                return 0;
            }

            // Onto its level meter's sub-mix (Tapped.Heard).
            Tapped.Heard(_voice, buffer, offset, read);

            // Short of what was asked for is the end: the mixer takes this
            // clip out of the mix on its own.
            if (read < count)
            {
                Finish(null);
            }

            return read;
        }

        public void Finish(Exception? error)
        {
            _finished = true;

            lock (Gate)
            {
                Playing.Remove(this);
            }

            if (error is null)
            {
                _done.TrySetResult();
            }
            else
            {
                _done.TrySetException(error);
            }
        }
    }
}
