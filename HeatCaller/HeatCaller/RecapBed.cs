using System.Collections.Concurrent;
using System.Diagnostics;
using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// The music and the crowd as the room heard them during a race, kept beside
/// the race's recording so the highlight videos can have them under the call
/// (26 Oct 2026, asked for by Asher: "video recaps should include the music
/// that is playing as well.").
///
/// WHY THEY WERE MISSING. The race recording is AudioBus's one mix
/// (RaceAudioRecorder, an AudioBus.IMixListener), and the music (MusicPlayer)
/// and the crowd bed (AmbienceService) never pass through that bus - each
/// opens a WaveOutEvent of its own, and Windows adds the three together
/// where nothing here can read the sum. So every video was the announcer and
/// the effects over silence, however loud the music was in the room.
///
/// WHERE THEY ARE TAKEN FROM: AudioLevels.Tap, the pass-through that already
/// sits last in both chains for the MUSIC and CROWD strips - after the
/// volume and the duck, one step before the 16-bit conversion and the
/// device. So what is kept is what the room heard, at the level it heard it,
/// ducked under the voice where the voice ducked it. Not AudioBus's mix
/// listener: that is one slot, and it is the race recording's.
///
/// WHY A FILE OF ITS OWN, and not mixed into the race's recording. The race
/// mp3 and the drivers' mp3 clips stay the call, clean, as the recorder's
/// own note explains - the request was for the videos. The bed is a mono
/// 16-bit wav at the recording's own rate, on the recording's own clock
/// (sample n is n samples after the recording started, as the recording's
/// is), so a video's stretch of it is the same byte range as the call's and
/// the two are mixed as the video's wav is cut (RaceAudioRecorder.CutWavWithBed).
/// It is deleted with the race's working wav once the videos are made.
///
/// IN STEP WITH THE CALL. Each read is stamped, on the device's thread, with
/// when it will be heard (Stamper - the rules AudioLevels.Feeder uses for the
/// strips), and so is each read of the call: the race recorder tells this,
/// read by read, which of its file's samples is heard when (CallHeard). The
/// writer puts each read of the music at the file sample heard at the same
/// moment, so the two line up to within a buffer whatever the race file's
/// own clock has done (a late first read, drift padding).
///
/// THE DEVICE THREAD'S RULES (AudioBus.IMixListener's): a copy of the read
/// and a queue add, and nothing else - no disk, no lock anything slow stands
/// behind, nothing that throws. Converting to mono, to the recording's rate,
/// summing the two and writing all happen on a writer thread of its own,
/// below normal priority. A full queue drops the read and counts it.
/// </summary>
internal sealed class RecapBed
{
    /// <summary>What a bed file's name ends with - "&lt;race&gt;.bed.wav", beside the race's own wav.</summary>
    public const string Extension = ".bed.wav";

    /// <summary>The race recording's rate, so a stretch of one is the same bytes of the other.</summary>
    public const int SampleRate = RaceAudioRecorder.SampleRate;

    /// <summary>
    /// How far behind now the writer stays before it writes a moment down.
    /// A read is stamped up to half a second ahead of now, and two outputs
    /// write here, so a moment is only final once both have been read past
    /// it; three seconds also rides out the writer being held up a while.
    /// </summary>
    internal const int FlushLagMs = 3000;

    /// <summary>How much of each source the writer holds in memory: well past the lag and the furthest a read is stamped ahead.</summary>
    internal const int RingSeconds = 8;

    private const int RingLength = SampleRate * RingSeconds;

    /// <summary>A read placed this close to where its source's last one ended is joined on to it - stamps are rounded, a click is not.</summary>
    internal const int SnapSamples = 64;

    /// <summary>About twenty seconds of reads from both outputs (a bed output reads ten times a second).</summary>
    private const int MaxQueued = 400;

    private const int FlushEveryMs = 2000;

    /// <summary>The bed being fed, or null. One at a time, like the bus's listener; the newest race wins.</summary>
    private static RecapBed? _current;

    private const int MusicSource = 0;
    private const int CrowdSource = 1;

    /// <summary>One read, or a cut (Samples null): nothing of that source after DueTicks is heard.</summary>
    private sealed record Chunk(int Source, long DueTicks, int Rate, int Channels, float[]? Samples);

    private readonly BlockingCollection<Chunk> _pending = new(MaxQueued);
    private readonly WaveFileWriter _wav;
    private readonly Thread _writer;
    private readonly long _startTicks;
    private readonly long _maxSamples;

    // The writer thread's alone.
    private readonly float[][] _rings = { new float[RingLength], new float[RingLength] };
    private readonly long[] _nextPos = new long[2];
    private readonly long[] _furthest = new long[2];
    private readonly byte[] _scratch = new byte[32 * 1024];
    private long _flushed;

    /// <summary>
    /// Where the race file's samples are against the Stopwatch, as the race
    /// recorder last saw them: the file's sample index at _startTicks. See
    /// CallHeard. 0 until the first read of the call - the wall clock since
    /// the start, which is what the recorder's drift padding keeps it near.
    /// </summary>
    private long _callOffset;

    private int _closed;
    private int _stopped;
    private int _dropped;
    private volatile string? _problem;

    private RecapBed(WaveFileWriter wav, string path, DateTime startedUtc, long maxSamples)
    {
        _wav = wav;
        Path = path;
        _maxSamples = maxSamples;

        // The recording's clock zeroes at startedUtc, which may be a moment
        // ago; this is the same instant on the Stopwatch the reads are
        // stamped by.
        _startTicks = Stopwatch.GetTimestamp()
            - (long)((DateTime.UtcNow - startedUtc).TotalSeconds * Stopwatch.Frequency);

        _writer = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "HeatCaller recap bed",
            Priority = ThreadPriority.BelowNormal,
        };
    }

    /// <summary>The bed's file.</summary>
    public string Path { get; }

    /// <summary>What went wrong, or null.</summary>
    public string? Problem => _problem;

    /// <summary>Reads the writer could not keep up with.</summary>
    public int Dropped => Volatile.Read(ref _dropped);

    /// <summary>
    /// Starts keeping the music and crowd for the race recording that starts
    /// at <paramref name="startedUtc"/>, or answers null - a bed that cannot
    /// start costs the videos their music, never the race its recording.
    /// </summary>
    internal static RecapBed? StartFor(string folder, string baseName, DateTime startedUtc, long maxSamples, Action<string> log)
    {
        try
        {
            var path = RaceHighlights.UnusedPath(folder, baseName, Extension);
            var wav = new WaveFileWriter(path, new WaveFormat(SampleRate, 16, 1));
            var bed = new RecapBed(wav, path, startedUtc, maxSamples);

            bed._writer.Start();

            // Last, so no read is queued before the writer that drains it runs.
            Volatile.Write(ref _current, bed);
            return bed;
        }
        catch (Exception ex)
        {
            log($"{AppTime.LogStampNow()}  [audio] The highlight videos of this race will have the announcer but not "
                + $"the music or crowd ({ex.Message}). The race recording is unaffected.");
            return null;
        }
    }

    /// <summary>Which bed source a level meter's output is, or -1 for one that is not kept (the bus's own).</summary>
    private static int SourceOf(AudioLevels.Meter meter) =>
        ReferenceEquals(meter, AudioLevels.Music) ? MusicSource
        : ReferenceEquals(meter, AudioLevels.Crowd) ? CrowdSource
        : -1;

    /// <summary>
    /// One read of the music's or the crowd's output, from AudioLevels.Tap -
    /// THE DEVICE'S OWN THREAD. Stamped whether or not a bed is running, so
    /// a race that starts mid-track still knows when that track's reads are
    /// heard. Never throws.
    /// </summary>
    internal static void Offer(AudioLevels.Meter meter, Stamper stamper, float[] buffer, int offset, int count, int sampleRate, int channels)
    {
        try
        {
            var source = SourceOf(meter);

            if (source < 0 || count <= 0 || offset < 0 || offset + count > buffer.Length)
            {
                return;
            }

            channels = Math.Max(1, channels);
            var frames = count / channels;

            if (frames <= 0)
            {
                return;
            }

            var due = stamper.Next(frames, sampleRate);

            if (Volatile.Read(ref _current) is not { } bed || Volatile.Read(ref bed._closed) == 1)
            {
                return;
            }

            // The buffer is the output's own and is reused the moment this
            // returns, so it is copied - the one allocation, as the race
            // recorder's tap makes one.
            var copy = new float[frames * channels];
            Array.Copy(buffer, offset, copy, 0, copy.Length);

            if (!bed._pending.TryAdd(new Chunk(source, due, sampleRate, channels, copy)))
            {
                Interlocked.Increment(ref bed._dropped);
            }
        }
        catch
        {
            // The device's thread: the queue closed a moment ago, or anything
            // else - the music plays on and the video loses a tenth of it.
        }
    }

    /// <summary>
    /// The music's or the crowd's output has stopped or paused (AudioLevels.Meter.Cut):
    /// what it had queued is not heard, so it must not be under the video
    /// either. Any thread; never throws.
    /// </summary>
    internal static void Cut(AudioLevels.Meter meter, long nowTicks)
    {
        try
        {
            var source = SourceOf(meter);

            if (source < 0 || Volatile.Read(ref _current) is not { } bed || Volatile.Read(ref bed._closed) == 1)
            {
                return;
            }

            bed._pending.TryAdd(new Chunk(source, nowTicks, 0, 0, null));
        }
        catch
        {
            // Nothing worth more than the music it would have trimmed.
        }
    }

    /// <summary>Stops taking reads in at once, without waiting. The writer finishes what is queued.</summary>
    public void BeginStop()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1)
        {
            return;
        }

        Interlocked.CompareExchange(ref _current, null, this);

        try
        {
            _pending.CompleteAdding();
        }
        catch
        {
            // Already completed.
        }
    }

    /// <summary>Drains what is queued and closes the file. Not for the UI thread: it waits. Safe to call twice.</summary>
    public void Stop()
    {
        BeginStop();

        if (Interlocked.Exchange(ref _stopped, 1) == 1)
        {
            return;
        }

        try
        {
            if (_writer.IsAlive && !_writer.Join(TimeSpan.FromSeconds(15)))
            {
                _problem ??= "the last few seconds could not be written";

                // Still inside a write on a disk that has stalled. The file
                // is the writer's to close when it comes back (see WriteLoop):
                // a WaveFileWriter is not thread-safe, and closing it here
                // rewrites its header underneath that write.
                return;
            }
        }
        catch
        {
            // A thread that never started.
        }

        try
        {
            _wav.Dispose();
        }
        catch
        {
            // The header was flushed as it went; the file still reads.
        }
    }

    private void WriteLoop()
    {
        var lastFlush = Environment.TickCount64;

        try
        {
            while (!_pending.IsCompleted)
            {
                if (_pending.TryTake(out var chunk, 250))
                {
                    Place(chunk);

                    // Everything that is waiting, before anything is written
                    // down: a writer held up for a while must not write a
                    // moment as silence with its music still in the queue.
                    while (_pending.TryTake(out var more))
                    {
                        Place(more);
                    }
                }

                FlushTo(PositionAt(Stopwatch.GetTimestamp()) - (long)FlushLagMs * SampleRate / 1000);

                if (Environment.TickCount64 - lastFlush >= FlushEveryMs)
                {
                    lastFlush = Environment.TickCount64;
                    _wav.Flush();
                }
            }

            // The end: everything placed goes down.
            FlushTo(Math.Max(_furthest[MusicSource], _furthest[CrowdSource]));
            _wav.Flush();
        }
        catch (Exception ex)
        {
            // A disk gone away, a file deleted underneath this. The bed stops;
            // the race and its recording do not notice, and the videos get
            // whatever was written.
            _problem ??= ex.Message;
            Interlocked.Exchange(ref _closed, 1);
            Interlocked.CompareExchange(ref _current, null, this);
        }
        finally
        {
            // Closed by the thread that writes it, so a Stop that gave up
            // waiting never has to close it under a write. Stop closes it
            // again when it did not give up, which does nothing.
            try
            {
                _wav.Dispose();
            }
            catch
            {
                // The header was flushed as it went; the file still reads.
            }
        }
    }

    /// <summary>
    /// The race recorder's read of the call, from the bus's device thread:
    /// the race file's sample <paramref name="fileIndex"/> is heard at
    /// <paramref name="heardTicks"/>. A subtraction and a write.
    /// </summary>
    // WHY (26 Oct 2026): the race file is not on the wall clock to the
    // sample. Its first read comes whenever the bus next refills - heard a
    // fifth of a second later when the bus was already playing - and the
    // drift padding moves it on when the device goes away. Placing the music
    // by the call's own reads puts it under the call where the room heard it,
    // whichever of those happened; the wall clock is only the fallback.
    internal void CallHeard(long fileIndex, long heardTicks) =>
        Volatile.Write(ref _callOffset, OffsetFor(fileIndex, heardTicks, _startTicks, SampleRate, Stopwatch.Frequency));

    /// <summary>Where in the file a moment on the Stopwatch falls, in samples.</summary>
    private long PositionAt(long ticks) =>
        Volatile.Read(ref _callOffset) + PositionFor(ticks, _startTicks, SampleRate, Stopwatch.Frequency);

    /// <summary>
    /// The samples between the recording's start and <paramref name="heardTicks"/>.
    /// Its own method because it is the arithmetic the sync stands on.
    /// </summary>
    internal static long PositionFor(long heardTicks, long startTicks, int sampleRate, long ticksPerSecond) =>
        ticksPerSecond <= 0 ? 0 : (heardTicks - startTicks) * sampleRate / ticksPerSecond;

    /// <summary>
    /// How far the race file's sample index is from the wall clock: what has
    /// to be added to <see cref="PositionFor"/> so a moment lands on the file
    /// sample heard then. Positive when the file is ahead of the clock.
    /// </summary>
    internal static long OffsetFor(long fileIndex, long heardTicks, long startTicks, int sampleRate, long ticksPerSecond) =>
        fileIndex - PositionFor(heardTicks, startTicks, sampleRate, ticksPerSecond);

    private void Place(Chunk chunk)
    {
        var s = chunk.Source;
        var ring = _rings[s];

        if (chunk.Samples is null)
        {
            // A cut: what was queued after this moment is not heard.
            var from = Math.Max(_flushed, PositionAt(chunk.DueTicks));

            for (var p = from; p < _furthest[s] && p < _flushed + RingLength; p++)
            {
                ring[p % RingLength] = 0f;
            }

            _furthest[s] = Math.Min(_furthest[s], Math.Max(from, _flushed));
            _nextPos[s] = _furthest[s];
            return;
        }

        var mono = ToMonoAtRate(chunk.Samples, chunk.Samples.Length, chunk.Channels, chunk.Rate, SampleRate);
        var pos = PositionAt(chunk.DueTicks);

        if (Math.Abs(pos - _nextPos[s]) <= SnapSamples)
        {
            pos = _nextPos[s];
        }

        _nextPos[s] = pos + mono.Length;

        for (var i = 0; i < mono.Length; i++)
        {
            var p = pos + i;

            // Too late to be written (the moment is already on disk), or
            // further ahead than the ring holds - neither happens with the
            // writer keeping up; both are a dropped sliver, not a fault.
            if (p < _flushed)
            {
                continue;
            }

            if (p >= _flushed + RingLength)
            {
                break;
            }

            ring[p % RingLength] = mono[i];
        }

        _furthest[s] = Math.Max(_furthest[s], Math.Min(pos + mono.Length, _flushed + RingLength));
    }

    /// <summary>Writes every moment before <paramref name="target"/>: the music and the crowd added together, as the room added them.</summary>
    private void FlushTo(long target)
    {
        // Never longer than the race recording may be.
        target = Math.Min(target, _maxSamples);

        if (target <= _flushed)
        {
            return;
        }

        var music = _rings[MusicSource];
        var crowd = _rings[CrowdSource];

        while (_flushed < target)
        {
            var count = (int)Math.Min(target - _flushed, _scratch.Length / 2);
            var at = 0;

            for (var k = 0; k < count; k++)
            {
                var slot = (int)((_flushed + k) % RingLength);
                var sample = ToPcm16(music[slot] + crowd[slot]);

                music[slot] = 0f;
                crowd[slot] = 0f;

                _scratch[at++] = (byte)(sample & 0xFF);
                _scratch[at++] = (byte)((sample >> 8) & 0xFF);
            }

            _wav.Write(_scratch, 0, at);
            _flushed += count;
        }

        if (_flushed >= _maxSamples)
        {
            BeginStop();
        }
    }

    /// <summary>
    /// One read as mono at <paramref name="toRate"/>: the channels averaged,
    /// as the race recording averages the bus's two, and stretched to the
    /// rate by straight-line interpolation - plenty for music under a voice,
    /// and nothing that can go subtly wrong. A music file at 48kHz is the
    /// usual reason the rates differ; the crowd is always 44.1kHz. Not a
    /// number counts as silence.
    /// </summary>
    internal static float[] ToMonoAtRate(float[] interleaved, int count, int channels, int fromRate, int toRate)
    {
        channels = Math.Max(1, channels);
        var frames = Math.Min(count, interleaved.Length) / channels;

        if (frames <= 0 || fromRate <= 0 || toRate <= 0)
        {
            return Array.Empty<float>();
        }

        float Frame(int f)
        {
            var sum = 0f;
            var at = f * channels;

            for (var c = 0; c < channels; c++)
            {
                sum += interleaved[at + c];
            }

            var mono = sum / channels;
            return float.IsNaN(mono) ? 0f : mono;
        }

        if (fromRate == toRate)
        {
            var same = new float[frames];

            for (var f = 0; f < frames; f++)
            {
                same[f] = Frame(f);
            }

            return same;
        }

        var length = (int)((long)frames * toRate / fromRate);
        var output = new float[length];
        var step = (double)fromRate / toRate;

        for (var i = 0; i < length; i++)
        {
            var source = i * step;
            var before = (int)source;
            var after = Math.Min(before + 1, frames - 1);
            var fraction = (float)(source - before);
            var a = Frame(before);

            output[i] = a + ((Frame(after) - a) * fraction);
        }

        return output;
    }

    /// <summary>
    /// A level to 16 bits, through a soft knee above 0.8 rather than a hard
    /// clip: two beds at full volume - or a bed under a loud call - add up
    /// past full scale, and the knee keeps the sum inside it without the
    /// crackle a clip makes.
    /// </summary>
    internal static short ToPcm16(float level)
    {
        var limited = SoftClip(level);
        return (short)Math.Clamp((int)MathF.Round(limited * short.MaxValue), short.MinValue, short.MaxValue);
    }

    /// <summary>Unchanged up to 0.8 either way; above it, eased towards 1 and never past it.</summary>
    internal static float SoftClip(float level)
    {
        const float Knee = 0.8f;

        if (float.IsNaN(level))
        {
            return 0f;
        }

        var size = Math.Abs(level);

        if (size <= Knee)
        {
            return level;
        }

        var eased = Knee + ((1f - Knee) * MathF.Tanh((size - Knee) / (1f - Knee)));
        return level < 0 ? -eased : eased;
    }

    /// <summary>
    /// The bed's 16-bit samples added into the call's, in place - a video's
    /// soundtrack. Where the bed is silent the call is left exactly as it
    /// was recorded; past the bed's end (a bed that stopped early) only the
    /// call plays. Both little-endian 16-bit mono, as both files are.
    /// </summary>
    internal static void MixPcm16(byte[] call, int callCount, byte[] bed, int bedCount)
    {
        var count = Math.Min(Math.Min(callCount, call.Length), Math.Min(bedCount, bed.Length)) & ~1;

        for (var i = 0; i < count; i += 2)
        {
            var b = (short)(bed[i] | (bed[i + 1] << 8));

            if (b == 0)
            {
                continue;
            }

            var c = (short)(call[i] | (call[i + 1] << 8));
            var mixed = ToPcm16((c + b) / (float)short.MaxValue);

            call[i] = (byte)(mixed & 0xFF);
            call[i + 1] = (byte)((mixed >> 8) & 0xFF);
        }
    }

    /// <summary>
    /// When each read of one output will be heard - AudioLevels.Feeder's
    /// stamping, kept for the bed. One per output (a Tap), used only on that
    /// output's thread.
    ///  - A refill is heard the queue's length after it is read.
    ///  - The first reads of an output come all at once, filling every
    ///    buffer, and play one after another from now.
    ///  - A stamp more than a buffer from where the queue says it should be
    ///    is put back (clock drift, a pause and resume).
    /// </summary>
    internal sealed class Stamper
    {
        private readonly long _aheadTicks;
        private readonly long _bufferTicks;
        private readonly Func<long> _clock;
        private long _next;
        private long _lastRead;

        /// <param name="latencyMs">The output's DesiredLatency.</param>
        /// <param name="buffers">The output's NumberOfBuffers.</param>
        /// <param name="clock">Stopwatch ticks; given by the tests, else the Stopwatch.</param>
        internal Stamper(int latencyMs, int buffers, Func<long>? clock = null)
        {
            var bufferMs = Math.Max(1, latencyMs / Math.Max(1, buffers));
            _bufferTicks = Stopwatch.Frequency * bufferMs / 1000;
            _aheadTicks = Math.Min(AudioLevels.MostAheadTicks, Stopwatch.Frequency * Math.Max(0, latencyMs - bufferMs) / 1000);
            _clock = clock ?? Stopwatch.GetTimestamp;
        }

        /// <summary>When the first sample of this read will be heard, in Stopwatch ticks.</summary>
        internal long Next(int frames, int sampleRate)
        {
            var now = _clock();
            var firstFill = _lastRead != 0 && now - _lastRead < _bufferTicks / 4;
            _lastRead = now;

            var expected = now + _aheadTicks;

            if (!firstFill && Math.Abs(_next - expected) > _bufferTicks)
            {
                _next = _next == 0 ? now : expected;
            }
            else if (_next < now)
            {
                _next = now;
            }

            var due = _next;
            _next += Math.Max(0, frames) * Stopwatch.Frequency / Math.Max(1, sampleRate);
            return due;
        }
    }
}
