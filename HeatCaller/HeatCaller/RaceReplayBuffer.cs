using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// The last half-minute of what the room heard, kept in memory so a moment
/// can be played again (22 Oct 2026 - roadmap "Item 6: replaying a call").
///
/// WHY MEMORY AND NOT THE RECORDING FILE. RaceAudioRecorder streams the mix
/// to a WAV on a thread of its own and rewrites the header as it goes;
/// reading the part-written file while that thread holds it is a
/// file-sharing problem and a mid-flush race, for no gain. Thirty seconds
/// of mono 16-bit at the bus's rate is about 2.6 MB, allocated once.
///
/// WHY HERE AND NOT ON THE BUS. AudioBus keeps ONE mix listener and the
/// recorder is it, so this hangs off RaceAudioRecorder.Mixed - which is the
/// tap - and is written on the SOUND DEVICE'S OWN THREAD. That is the whole
/// design constraint: <see cref="Put"/> and <see cref="Commit"/> take no
/// lock, allocate nothing and cannot throw, because a glitch there is a
/// glitch in the club's own race recording.
///
/// ONE WRITER, ANY NUMBER OF READERS. The device thread is the only writer
/// (the bus has one listener and calls it from one thread). A reader copies
/// what it wants and then checks, from the count the writer publishes,
/// whether the writer could have lapped into the part it copied; anything
/// it might have overwritten is dropped from the FRONT of the copy, so a
/// snapshot is always a run of samples the room heard, in order, never a
/// splice of two moments.
/// </summary>
internal sealed class RaceReplayBuffer
{
    /// <summary>Samples a second - the bus's mix rate, mono as the recording is.</summary>
    public const int SampleRate = AudioBus.MixSampleRate;

    /// <summary>How much is kept: the design's "last half-minute".</summary>
    public const int Seconds = 30;

    /// <summary>
    /// The most one device callback can put in before it publishes - a
    /// second, against the bus's tenth-of-a-second buffers. A reader treats
    /// this much in front of the published count as possibly being written
    /// over, which is what keeps a copy from catching a half-written slot.
    /// </summary>
    internal const int MostInOneCallback = SampleRate;

    /// <summary>The one buffer the app's recordings share - one race records at a time.</summary>
    public static RaceReplayBuffer Shared { get; } = new(SampleRate * Seconds);

    private readonly short[] _ring;

    /// <summary>Samples published to readers, ever. Written by the writer only, through Volatile.</summary>
    private long _written;

    /// <summary>Samples put since the last Commit - the writer's own count, nobody else reads it.</summary>
    private int _pending;

    /// <summary>Where this race's audio starts, in the same count - see <see cref="Restart"/>.</summary>
    private long _from;

    /// <summary>Environment.TickCount64 at the last Commit, so a reader can tell "now" from "the end of the race".</summary>
    private long _lastCommitTicks;

    internal RaceReplayBuffer(int capacity)
    {
        _ring = new short[Math.Max(MostInOneCallback * 2, capacity)];
    }

    /// <summary>How many samples the ring holds.</summary>
    public int Capacity => _ring.Length;

    /// <summary>
    /// One sample from the device thread. Not seen by a reader until
    /// <see cref="Commit"/>. No lock, no allocation, nothing that throws.
    /// </summary>
    public void Put(short sample)
    {
        var at = (_written + _pending) % _ring.Length;
        _ring[at] = sample;
        _pending++;
    }

    /// <summary>Publishes what <see cref="Put"/> has written - once per device callback.</summary>
    public void Commit()
    {
        if (_pending == 0)
        {
            return;
        }

        Volatile.Write(ref _lastCommitTicks, Environment.TickCount64);
        Volatile.Write(ref _written, _written + _pending);
        _pending = 0;
    }

    /// <summary>
    /// A new race: what came before it is not this race's to replay.
    /// Nothing is cleared - the writer may be mid-callback - the start is
    /// simply moved up to where the writer has got to.
    /// </summary>
    public void Restart() => Volatile.Write(ref _from, Volatile.Read(ref _written));

    /// <summary>Seconds of this race's audio held now, up to <see cref="Seconds"/>.</summary>
    public double HeldSeconds
    {
        get
        {
            var held = Volatile.Read(ref _written) - Volatile.Read(ref _from);
            return Math.Min(held, _ring.Length - MostInOneCallback) / (double)SampleRate;
        }
    }

    /// <summary>How long since the last audio came in - near nought while a race is recording.</summary>
    public TimeSpan SinceLastAudio
    {
        get
        {
            var last = Volatile.Read(ref _lastCommitTicks);
            return last == 0 ? TimeSpan.MaxValue : TimeSpan.FromMilliseconds(Math.Max(0, Environment.TickCount64 - last));
        }
    }

    /// <summary>
    /// The last <paramref name="seconds"/> of this race's audio, oldest
    /// first - shorter when less is held, empty when nothing is. Safe from
    /// any thread while the device thread writes.
    /// </summary>
    public short[] Last(double seconds)
    {
        var wanted = (long)Math.Max(0, Math.Round(seconds * SampleRate));

        var end = Volatile.Read(ref _written);
        var start = Math.Max(Volatile.Read(ref _from), end - wanted);

        // Never reach back into what the writer may already be overwriting.
        start = Math.Max(start, end - (_ring.Length - MostInOneCallback));

        if (end <= start)
        {
            return Array.Empty<short>();
        }

        var copy = new short[end - start];

        for (long i = start; i < end; i++)
        {
            copy[i - start] = _ring[i % _ring.Length];
        }

        // Nothing copied above may be read later than the count below.
        Interlocked.MemoryBarrier();

        // Did the writer lap into what was copied while it was copied? The
        // oldest sample still safe is MostInOneCallback in front of where
        // the writer now publishes, a whole ring back.
        var after = Volatile.Read(ref _written);
        var safeFrom = after + MostInOneCallback - _ring.Length;

        if (safeFrom <= start)
        {
            return copy;
        }

        var drop = safeFrom - start;

        return drop >= copy.Length ? Array.Empty<short>() : copy[(int)drop..];
    }

    /// <summary>
    /// A snapshot as something the bus can play: mono floats at the mix
    /// rate, faded in and out over a few hundredths of a second so a cut
    /// taken mid-word does not click on the way in or out.
    /// </summary>
    public static ISampleProvider ToSound(short[] samples)
    {
        var floats = new float[samples.Length];

        for (var i = 0; i < samples.Length; i++)
        {
            floats[i] = samples[i] / 32768f;
        }

        var fade = Math.Min(samples.Length / 2, SampleRate / 25);

        for (var i = 0; i < fade; i++)
        {
            var g = i / (float)fade;
            floats[i] *= g;
            floats[floats.Length - 1 - i] *= g;
        }

        return new Snapshot(floats);
    }

    private sealed class Snapshot : ISampleProvider
    {
        private readonly float[] _samples;
        private int _at;

        public Snapshot(float[] samples) => _samples = samples;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            var n = Math.Min(count, _samples.Length - _at);

            if (n <= 0)
            {
                return 0;
            }

            Array.Copy(_samples, _at, buffer, offset, n);
            _at += n;
            return n;
        }
    }
}
