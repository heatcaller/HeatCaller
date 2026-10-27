using System.Diagnostics;
using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// How loud each of the three sounds is as the room hears it, for the
/// channel strips in the Broadcast window (25 Oct 2026, the radio-board
/// strips Asher asked for: VOICE, MUSIC and CROWD, with smooth VU movement).
///
/// FOUR STRIPS FROM THREE OUTPUTS. The announcer, effects and imaging go
/// out through AudioBus's one mix; the music (MusicPlayer) and the crowd
/// bed (AmbienceService) each open an output of their own and never pass
/// through it. Windows adds the three together where nothing here can read
/// the sum, so each strip is one output - except AudioBus's, which is split
/// in two (26 Oct 2026, asked for by Asher: "one for voice and one for
/// SFX"). Each sound handed to the bus says whether it is the announcer
/// speaking (AudioBus.PlayAsync's voice), and the bus adds each up into its
/// own sub-mix as the mixer reads it - VOICE, and SFX for everything else
/// on the bus: effects, beeps, imaging, recordings played back (a replayed
/// moment, a driver's own clip).
///
/// READ ON THE SOUND DEVICE'S OWN THREAD, so the rules are AudioBus's for
/// its mix listener: no allocation, no lock, no disk, nothing that throws.
/// Each read is cut into 10ms blocks and the average power of each block is
/// kept, left and right - a multiply and an add a sample - in a ring the
/// screen reads from. This is not that listener and never takes its one
/// slot, so a meter can never stop a race recording.
///
/// A VU MEASURES POWER, NOT PEAKS: the strips show how loud the sound is
/// over a fraction of a second, as a radio board's VU does, so each block
/// keeps its mean square rather than its loudest sample.
///
/// IN STEP WITH THE ROOM, NOT AHEAD OF IT. An output reads its sound a few
/// buffers before the speaker plays it - a fifth of a second for the
/// announcer, nearer a third for the music. A meter fed as the sound is read
/// would move before the line was heard. So each block is stamped with when
/// it will be heard, and the screen shows the blocks whose time has come:
///  - A read that refills a buffer happens just as a buffer has finished
///    playing, with the others still queued in front of it - so its sound
///    is heard the queue's length from now ("ahead", given by the output).
///  - The first reads of an output come all at once, filling every buffer;
///    those play one after another from now, so they are stamped one after
///    another, not each a queue's length ahead.
///  - When a stamp has drifted from where the queue says it should be by
///    more than a buffer - the sound card's clock and this PC's are never
///    quite the same, and over a race night they part - it is put back.
///    That also puts a paused-and-resumed output back in step.
/// </summary>
internal static class AudioLevels
{
    /// <summary>The announcer speaking - the lines AudioBus is told are voice (AudioBus.PlayAsync's voice).</summary>
    public static readonly Meter Voice = new("VOICE");

    /// <summary>Everything else on AudioBus: effects, lap beeps, imaging, recordings played back.</summary>
    public static readonly Meter Effects = new("SFX");

    /// <summary>The music - MusicPlayer's own output.</summary>
    public static readonly Meter Music = new("MUSIC");

    /// <summary>The crowd bed - AmbienceService's own output.</summary>
    public static readonly Meter Crowd = new("CROWD");

    /// <summary>The length of a block: one reading. A hundredth of a second.</summary>
    public const int BlockMs = 10;

    /// <summary>
    /// The furthest ahead of now a block is ever stamped: the deepest output
    /// queue there is (the beds', 400ms) with room to spare. A block is
    /// always stamped no earlier than it was written and no later than this
    /// after - which is what lets a reader stop early (Meter.Heard).
    /// </summary>
    internal static readonly long MostAheadTicks = Stopwatch.Frequency / 2;

    /// <summary>A ring of one output's blocks: when each is heard, and its power, left and right.</summary>
    internal sealed class Meter
    {
        /// <summary>
        /// Five seconds of blocks. A block is read a few tenths of a second
        /// after it is written, so it is never written over first; if it
        /// ever were, the cost is one wrong flicker of a bar.
        /// </summary>
        internal const int Capacity = 512;

        private readonly long[] _due = new long[Capacity];
        private readonly float[] _left = new float[Capacity];
        private readonly float[] _right = new float[Capacity];

        /// <summary>How many blocks have ever been written; the next goes at this, round the ring.</summary>
        private long _written;

        /// <summary>See Cut: blocks written before this count, and due after <see cref="_cutAt"/>, will never be heard.</summary>
        private long _cutWritten;
        private long _cutAt;

        internal Meter(string name) => Name = name;

        /// <summary>The strip's caption.</summary>
        public string Name { get; }

        /// <summary>One block, heard at <paramref name="dueTicks"/> (Stopwatch ticks), with its mean square left and right. The device's thread.</summary>
        internal void Write(long dueTicks, float left, float right)
        {
            var written = Volatile.Read(ref _written);
            var slot = (int)(written % Capacity);

            _due[slot] = dueTicks;
            _left[slot] = left;
            _right[slot] = right;

            // Published after the block, so a reader never counts a slot that
            // is not filled in yet.
            Volatile.Write(ref _written, written + 1);
        }

        /// <summary>
        /// The output has stopped or paused: whatever it had queued will not
        /// be heard, so the bars must not go on moving to it for the next
        /// third of a second. Any thread.
        /// </summary>
        internal void Cut(long? nowTicks = null)
        {
            var at = nowTicks ?? Stopwatch.GetTimestamp();

            Volatile.Write(ref _cutAt, at);
            Volatile.Write(ref _cutWritten, Volatile.Read(ref _written));

            // WHY (26 Oct 2026): what was queued is not heard, so it is not
            // under the highlight videos either (RecapBed). A queue add for
            // the music's and crowd's meters, nothing for the others.
            RecapBed.Cut(this, at);
        }

        /// <summary>
        /// How loud it was between <paramref name="sinceTicks"/> and
        /// <paramref name="nowTicks"/>, as the average power of the blocks
        /// heard in that time, left and right - 0 when nothing was heard (an
        /// output stopped, or never opened). Any thread; the screen's.
        /// </summary>
        internal (float Left, float Right, int Blocks) Heard(long sinceTicks, long nowTicks)
        {
            var written = Volatile.Read(ref _written);
            var cutWritten = Volatile.Read(ref _cutWritten);
            var cutAt = Volatile.Read(ref _cutAt);

            // Back from the newest. The ring is in the order blocks were
            // written, which is the order they are heard for one output -
            // but two outputs can briefly write to one meter (AudioBus's
            // open race), so the search goes back past the window by the
            // furthest a block is ever stamped ahead before it stops: no
            // block written after that point can be due inside the window.
            var stopAt = sinceTicks - MostAheadTicks;
            double left = 0;
            double right = 0;
            var blocks = 0;

            for (long i = written - 1, seen = 0; i >= 0 && seen < Capacity; i--, seen++)
            {
                var slot = (int)(i % Capacity);
                var due = _due[slot];

                if (due <= stopAt)
                {
                    break;
                }

                if (due > nowTicks || due <= sinceTicks)
                {
                    continue;
                }

                // Queued before a stop and due after it: never heard.
                if (i < cutWritten && due > cutAt)
                {
                    continue;
                }

                left += _left[slot];
                right += _right[slot];
                blocks++;
            }

            return blocks == 0 ? (0f, 0f, 0) : ((float)(left / blocks), (float)(right / blocks), blocks);
        }

        /// <summary>For the tests: the ring emptied.</summary>
        internal void Clear()
        {
            Volatile.Write(ref _written, 0);
            Volatile.Write(ref _cutWritten, 0);
            Array.Clear(_due, 0, _due.Length);
        }
    }

    /// <summary>
    /// Cuts one output's sound into blocks and stamps each with when it will
    /// be heard. One per output; called only on that output's thread.
    /// </summary>
    internal sealed class Feeder
    {
        private readonly Meter _meter;
        private readonly int _channels;
        private readonly int _framesPerBlock;
        private readonly long _ticksPerBlock;
        private readonly long _aheadTicks;
        private readonly long _bufferTicks;
        private readonly Func<long> _clock;

        // The block being filled, carried across reads.
        private int _framesIn;
        private double _sumLeft;
        private double _sumRight;

        /// <summary>When the next block will start playing, in Stopwatch ticks; 0 before the first.</summary>
        private long _nextDue;

        /// <summary>When the last read came; reads closer together than a quarter of a buffer are the first fill.</summary>
        private long _lastRead;

        /// <param name="latencyMs">The output's DesiredLatency.</param>
        /// <param name="buffers">The output's NumberOfBuffers.</param>
        /// <param name="clock">Stopwatch ticks; given by the tests, else the Stopwatch.</param>
        internal Feeder(Meter meter, int sampleRate, int channels, int latencyMs, int buffers, Func<long>? clock = null)
        {
            _meter = meter;
            _channels = Math.Max(1, channels);
            _framesPerBlock = Math.Max(1, sampleRate * BlockMs / 1000);
            _ticksPerBlock = Stopwatch.Frequency * BlockMs / 1000;

            var bufferMs = Math.Max(1, latencyMs / Math.Max(1, buffers));
            _bufferTicks = Stopwatch.Frequency * bufferMs / 1000;
            _aheadTicks = Math.Min(MostAheadTicks, Stopwatch.Frequency * Math.Max(0, latencyMs - bufferMs) / 1000);
            _clock = clock ?? Stopwatch.GetTimestamp;
        }

        /// <summary>
        /// One read of the output's sound, interleaved, as it goes to the
        /// device. Never throws: a meter must not cost the room its sound.
        /// </summary>
        internal void Feed(float[] buffer, int offset, int count)
        {
            try
            {
                if (count <= 0 || offset < 0 || offset + count > buffer.Length)
                {
                    return;
                }

                var now = _clock();
                var firstFill = _lastRead != 0 && now - _lastRead < _bufferTicks / 4;
                _lastRead = now;

                // Where the queue says this read's sound will be heard. Back
                // there when the stamps have strayed from it by more than a
                // buffer - not in the first fill, whose reads come together
                // and play one after another.
                var expected = now + _aheadTicks;

                if (!firstFill && Math.Abs(_nextDue - expected) > _bufferTicks)
                {
                    // The very first read of an output plays now, not a queue
                    // from now: nothing is queued in front of it yet.
                    _nextDue = _nextDue == 0 ? now : expected;
                }
                else if (_nextDue < now)
                {
                    _nextDue = now;
                }

                var frames = count / _channels;
                var at = offset;

                for (var frame = 0; frame < frames; frame++)
                {
                    var l = buffer[at];
                    var r = _channels > 1 ? buffer[at + 1] : l;
                    at += _channels;

                    // Not a number counts as silence; anything absurdly loud
                    // as "very loud", so one bad sample cannot pin the bar.
                    if (!(l * l < 16f))
                    {
                        l = float.IsNaN(l) ? 0f : 4f;
                    }

                    if (!(r * r < 16f))
                    {
                        r = float.IsNaN(r) ? 0f : 4f;
                    }

                    _sumLeft += l * l;
                    _sumRight += r * r;

                    if (++_framesIn == _framesPerBlock)
                    {
                        _meter.Write(_nextDue, (float)(_sumLeft / _framesPerBlock), (float)(_sumRight / _framesPerBlock));
                        _nextDue += _ticksPerBlock;
                        _framesIn = 0;
                        _sumLeft = 0;
                        _sumRight = 0;
                    }
                }
            }
            catch
            {
                // Nothing here can fail as written; if it ever does, the
                // meter misses a read and the sound goes on.
            }
        }
    }

    /// <summary>
    /// A pass-through for an output that has its own chain (MusicPlayer,
    /// AmbienceService): hands each read on unchanged, and to a Feeder.
    /// </summary>
    internal sealed class Tap : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly Feeder _feeder;
        private readonly Meter _meter;
        private readonly RecapBed.Stamper _heardAt;
        private readonly int _sampleRate;
        private readonly int _channels;

        /// <param name="latencyMs">The output's DesiredLatency.</param>
        /// <param name="buffers">The output's NumberOfBuffers.</param>
        internal Tap(ISampleProvider source, Meter meter, int latencyMs, int buffers)
        {
            _source = source;
            _feeder = new Feeder(meter, source.WaveFormat.SampleRate, source.WaveFormat.Channels, latencyMs, buffers);
            _meter = meter;
            _heardAt = new RecapBed.Stamper(latencyMs, buffers);
            _sampleRate = source.WaveFormat.SampleRate;
            _channels = source.WaveFormat.Channels;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);

            if (read > 0)
            {
                _feeder.Feed(buffer, offset, read);

                // WHY (26 Oct 2026, asked for by Asher: "video recaps should
                // include the music that is playing as well."): the same read
                // to the race's recap bed, for the highlight videos - the music
                // and crowd as heard, after the volume and the duck. A copy
                // and a queue add, and it never throws; see RecapBed.
                RecapBed.Offer(_meter, _heardAt, buffer, offset, read, _sampleRate, _channels);
            }

            return read;
        }
    }
}
