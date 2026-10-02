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

    /// <summary>
    /// Opens the output now rather than on the first sound, so that first
    /// sound doesn't pay for the wake-up - and nor does the music. Never
    /// throws: a PC with no sound device just stays quiet.
    /// </summary>
    public static void Warm()
    {
        try
        {
            lock (Gate)
            {
                EnsureRunningLocked();
            }
        }
        catch
        {
            // Tried again on the first sound.
        }
    }

    /// <summary>
    /// Plays a sound and completes when it has been handed to the device -
    /// cut short, and completed straight away, if the token is cancelled.
    /// Throws InvalidOperationException when there is no usable output.
    /// </summary>
    ///
    /// With <paramref name="allowBoost"/> the gain may go above unity, up to
    /// <see cref="MaxBoost"/>, and the clip is passed through a soft limiter
    /// (SoftLimiter) so the boosted peaks bend rather than hard-clip in the
    /// 16-bit output. Only the announcer asks for it - the "boost voice over
    /// loud ambience" setting, which a 0..1 clamp here silently cancelled at
    /// the default 100% announcer level. Everything else stays at or below
    /// unity, exactly as before.
    public static Task PlayAsync(ISampleProvider source, float volume, CancellationToken cancellationToken = default, bool allowBoost = false)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        var gain = float.IsNaN(volume) ? 0f : Math.Clamp(volume, 0f, allowBoost ? MaxBoost : 1f);
        ISampleProvider gained = new VolumeSampleProvider(ToMixFormat(source)) { Volume = gain };

        if (gain > 1f)
        {
            gained = new SoftLimiter(gained);
        }

        var clip = new Clip(gained);

        MixingSampleProvider mixer;

        lock (Gate)
        {
            try
            {
                mixer = EnsureRunningLocked();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"No usable audio output device: {ex.Message}", ex);
            }

            Playing.Add(clip);
        }

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

    private static MixingSampleProvider EnsureRunningLocked()
    {
        if (_output is not null && _mixer is not null)
        {
            return _mixer;
        }

        // ReadFully: the mixer never runs dry, so the device plays silence
        // between sounds instead of stopping.
        var mixer = new MixingSampleProvider(MixFormat) { ReadFully = true };

        // Not the shortest buffer possible: some drivers play nothing at
        // all from a very short one (see ToneGenerator), and a continuous
        // stream that runs dry glitches. About a third of a second.
        var output = new WaveOutEvent { DesiredLatency = 300, NumberOfBuffers = 3 };

        try
        {
            output.Init(new SampleToWaveProvider16(mixer));
            output.PlaybackStopped += OnPlaybackStopped;
            output.Play();
        }
        catch
        {
            output.Dispose();
            throw;
        }

        _output = output;
        _mixer = mixer;
        return mixer;
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

        // Continuations off the audio thread: whoever awaits this goes on to
        // do real work, and the device's own thread must not wait for it.
        private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private volatile bool _finished;

        public Clip(ISampleProvider source) => _source = source;

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
