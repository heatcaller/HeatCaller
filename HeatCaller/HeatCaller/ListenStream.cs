using System.Collections.Concurrent;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace HeatCaller;

/// <summary>A caption inside a stream segment: words cleared for the public page, and how far into the segment they start.</summary>
internal readonly record struct ListenCaption(string Text, double AtSeconds);

/// <summary>
/// About three seconds of the mix, as mp3 frames, for the listen page.
/// </summary>
/// <param name="Mp3">The frames: <paramref name="Lead"/> frames from before this segment, then <paramref name="Frames"/> of its own.</param>
/// <param name="Lead">How many frames at the front are only there to warm the page's decoder up, and are not played.</param>
/// <param name="Frames">How many frames are this segment's own.</param>
/// <param name="SamplesPerFrame">Samples each frame decodes to - 1152 for the mp3 this sends.</param>
/// <param name="SampleRate">The stream's rate.</param>
/// <param name="Run">When this stream started, in UTC ticks - a new run is a new stream, not the next segment of the last one.</param>
/// <param name="Number">This segment's place in its run. The page plays two segments back to back only when they are numbered one after the other.</param>
internal sealed record ListenSegment(
    byte[] Mp3,
    int Lead,
    int Frames,
    int SamplesPerFrame,
    int SampleRate,
    long Run,
    long Number,
    IReadOnlyList<ListenCaption> Captions,
    DateTime AtUtc)
{
    /// <summary>How long this segment plays for.</summary>
    public double Seconds => SampleRate <= 0 ? 0 : (double)Frames * SamplesPerFrame / SampleRate;
}

/// <summary>
/// Everything the room hears, on its way to anyone listening along from
/// home (2 Nov 2026, asked for by Asher: "listen in on the relay should
/// include all audio. not just the voice. imaging, ambiance, music, etc.").
///
/// WHAT CHANGED. Listening along used to be the announcer's lines, each one
/// encoded on its own and played on the page one after another
/// (ListenAlong). Everything between them - the show's music, the crowd,
/// the stingers and beds, the tones - never left the building, so somebody
/// at home heard a voice in a silent room. Now the page is sent the mix:
/// AudioBus's on-air tap (voice, imaging, tones and effects), plus the
/// music and the crowd, which each play through an output of their own and
/// are taken from the same AudioLevels.Tap the highlight videos' bed uses.
///
/// ONE MP3, CUT AT FRAME BOUNDARIES. Encoding a few seconds at a time and
/// playing them back to back would put a click or a gap at every join - the
/// encoder pads the front and back of whatever it is given - which a voice
/// line hides and music does not. So the mix goes through one encoder for
/// as long as listening is on, and its output is cut into segments on mp3
/// frame boundaries. Played in order, the frames are the one continuous
/// stream. Each segment carries the last few frames of the one before it
/// (Lead): the page decodes those as well, so its decoder starts the
/// segment warmed up, and then plays only the segment's own frames - so a
/// segment needs nothing but itself, and a missed one is a gap rather than
/// noise.
///
/// WHAT STAYS IN THE ROOM. Anything played off air (AudioBus.PlayAsync's
/// offAir): an announcer line not cleared for the public page - an
/// initials-only driver, a car said by its number, a penalty call, full
/// names the club shortens online - and a driver's own clip, which is a
/// recording of the call. The bus takes those back out of the mix it hands
/// over, so the music under them carries on and the words do not.
///
/// SILENCE IS NOT SENT. A segment with nothing above the noise floor in it
/// is cut and thrown away, so a quiet hall between races costs nobody an
/// upload. The page simply hears nothing, as it would have from the room.
///
/// THE DEVICE THREADS' RULES (AudioBus.IMixListener): a copy and a queue add,
/// nothing else, nothing that throws. The mixing, the encoding and the
/// cutting happen on threads of this class's own, below normal priority.
///
/// The bus is the clock. It plays silence between sounds and never stops,
/// so each of its reads says how much time has passed; the music and the
/// crowd are held a moment in a buffer each (BedLead) and laid under the
/// bus read by read. Their devices run on the same sound card as the bus
/// almost always, so they keep step; when one gets ahead it is trimmed,
/// and when it runs dry it is silence until it has caught up again.
/// </summary>
internal sealed class ListenStream : IDisposable
{
    /// <summary>The stream's rate - the bus's, so the bus's reads need no resampling.</summary>
    public const int SampleRate = AudioBus.MixSampleRate;

    /// <summary>
    /// About how long a segment is. Short enough that the page is a few
    /// seconds behind the room; long enough that a race night is a request
    /// every few seconds rather than several a second. Forty of them is what
    /// the relay holds (relay/src/types.ts), so two minutes.
    /// </summary>
    internal const double SegmentSeconds = 3.0;

    /// <summary>Frames from the segment before carried at the front of each - see the class summary. Four covers the mp3 bit reservoir and the decoder's own start.</summary>
    internal const int LeadFrames = 4;

    /// <summary>How quiet a stretch has to be to count as silence: -60 dB.</summary>
    internal const float LoudEnough = 0.001f;

    /// <summary>
    /// How far the music and the crowd are held behind their own devices,
    /// so a read that arrives late does not leave a hole. Their outputs hand
    /// over a tenth of a second at a time and the bus a tenth at a time, so
    /// half a second is plenty of slack.
    /// </summary>
    internal const double BedLeadSeconds = 0.5;

    /// <summary>Past this much held, a bed is trimmed back to <see cref="BedLeadSeconds"/>.</summary>
    internal const double BedMostSeconds = 1.2;

    /// <summary>
    /// The most a bed ever holds. Only reached when the bus has stopped
    /// reading (its speaker dropped out), so nothing is laying the bed under
    /// anything: the oldest goes rather than the music piling up, ten
    /// megabytes a minute, until the bus comes back.
    /// </summary>
    internal const double BedStalledSeconds = 5.0;

    /// <summary>How long after a stream fails before another is started.</summary>
    private static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(30);

    /// <summary>
    /// HOW THE STREAM IS DOING, IN THE CONSOLE (11 Nov 2026, asked for by
    /// Asher: Listen Now's music "skips or drops", and maybe the voice).
    /// Counted before anything is changed, so a race night says which of
    /// the suspects it is. Every five minutes, a line saying what went
    /// wrong in that stretch: the music or the crowd running dry or being
    /// trimmed (see Bed), sound dropped on the way to the encoder, and how
    /// long the stretches took to reach the relay. A clean five minutes
    /// says nothing, except once every half an hour so a quiet console is
    /// known to be a clean stream rather than one that is not counting.
    /// </summary>
    internal static readonly TimeSpan HealthEvery = TimeSpan.FromMinutes(5);

    /// <summary>How often a clean stream says so - see <see cref="HealthEvery"/>.</summary>
    internal static readonly TimeSpan CleanEvery = TimeSpan.FromMinutes(30);

    /// <summary>The one running, for <see cref="Offer"/> - the music and crowd taps are static.</summary>
    private static volatile Run? _live;

    private readonly Func<bool> _wanted;
    private readonly Action<ListenSegment> _send;
    private readonly Action<string>? _log;
    private readonly Thread _keeper;
    private readonly ManualResetEventSlim _poke = new(false);
    private volatile bool _disposed;
    private volatile Run? _run;
    private readonly Func<ListenAlong.Delivery?>? _delivery;
    private DateTime _retryAt = DateTime.MinValue;
    private string? _problemSaid;
    private DateTime _healthAt = DateTime.MaxValue;
    private DateTime _cleanSaidAt = DateTime.MinValue;

    /// <param name="wanted">Whether listening along is on - asked every second.</param>
    /// <param name="send">Where each segment goes. Called on the encoder's thread; must not block.</param>
    /// <param name="log">Where a problem is said, once.</param>
    /// <param name="delivery">How the stretches got to the relay since it was last asked - see ListenAlong.TakeDelivery. Null where nothing sends them.</param>
    public ListenStream(Func<bool> wanted, Action<ListenSegment> send, Action<string>? log, Func<ListenAlong.Delivery?>? delivery = null)
    {
        _wanted = wanted;
        _send = send;
        _log = log;
        _delivery = delivery;
        _keeper = new Thread(Keep) { IsBackground = true, Name = "Listen stream", Priority = ThreadPriority.BelowNormal };
        _keeper.Start();
    }

    /// <summary>
    /// Whether listeners are getting the mix right now - the encoder is up
    /// and taking audio. Until it is, and on a PC whose encoder will not
    /// stream, the announcer's lines go line by line as they always did
    /// (AnnouncementService.StreamingToListeners).
    /// </summary>
    public bool Streaming => _run is { Up: true, Finished: false };

    /// <summary>
    /// The words of a line about to be heard on air, for the page to show
    /// as it plays. Any thread; never blocks.
    /// </summary>
    public void Caption(string text)
    {
        if (_run is { } run && !string.IsNullOrWhiteSpace(text))
        {
            run.Caption(text.Trim());
        }
    }

    /// <summary>
    /// One read of the music's or the crowd's output, from AudioLevels.Tap -
    /// THE DEVICE'S OWN THREAD. A copy and a queue add; never throws.
    /// </summary>
    internal static void Offer(AudioLevels.Meter meter, float[] buffer, int offset, int count, int sampleRate, int channels)
    {
        if (_live is not { } run)
        {
            return;
        }

        var source = ReferenceEquals(meter, AudioLevels.Music) ? Run.MusicSource
            : ReferenceEquals(meter, AudioLevels.Crowd) ? Run.CrowdSource
            : -1;

        if (source >= 0)
        {
            run.Take(source, buffer, offset, count, sampleRate, channels);
        }
    }

    private void Keep()
    {
        while (!_disposed)
        {
            try
            {
                var want = _wanted();
                var run = _run;

                if (run is not null && DateTime.UtcNow >= _healthAt)
                {
                    SayHealth(run, DateTime.UtcNow);
                }

                if (want && (run is null || run.Finished))
                {
                    if (run is { Finished: true })
                    {
                        Say(run.Problem);
                        SayHealth(run, DateTime.UtcNow);
                        Stop(run);
                        _run = null;
                        _retryAt = DateTime.UtcNow + RetryEvery;
                    }
                    else if (DateTime.UtcNow >= _retryAt)
                    {
                        _run = Begin();
                    }
                }
                else if (!want && run is not null)
                {
                    SayHealth(run, DateTime.UtcNow);
                    Stop(run);
                    _run = null;
                    _retryAt = DateTime.MinValue;
                }
                else if (want && run is { Finished: false })
                {
                    // The bus is the clock, and a speaker dropping out closes
                    // it until the next announcer line or chime. The music
                    // finds the new speaker by itself, so without this the
                    // room had music and the listeners had silence. Returns
                    // at once while the bus is open.
                    AudioBus.Warm();
                }
            }
            catch (Exception ex)
            {
                Say($"Listening along stopped sending the room's sound: {ex.Message}");
            }

            _poke.Wait(1000);
            _poke.Reset();
        }

        if (_run is { } last)
        {
            Stop(last);
            _run = null;
        }
    }

    private Run Begin()
    {
        var run = new Run(_send);
        _live = run;
        AudioBus.ListenOnAir(run);
        run.Start();

        // Counted from here, and the relay's count started afresh with it.
        _delivery?.Invoke();
        _healthAt = DateTime.UtcNow + HealthEvery;
        _cleanSaidAt = DateTime.UtcNow;

        // The bus is the clock (see the class summary), so it has to be
        // running for the music to go out before the announcer's first word.
        // Opens it if nothing has yet; blocks this thread only.
        AudioBus.Warm();
        return run;
    }

    private static void Stop(Run run)
    {
        AudioBus.StopListeningOnAir(run);

        if (ReferenceEquals(_live, run))
        {
            _live = null;
        }

        run.Stop();
    }

    /// <summary>The last few minutes' line for the console, or nothing for a clean stretch - see <see cref="HealthEvery"/>.</summary>
    private void SayHealth(Run run, DateTime nowUtc)
    {
        _healthAt = nowUtc + HealthEvery;

        try
        {
            var line = HealthLine(run.TakeHealth(), _delivery?.Invoke(), nowUtc - _cleanSaidAt >= CleanEvery);

            if (line is not null)
            {
                _cleanSaidAt = nowUtc;
                _log?.Invoke($"[relay] Listening along, last few minutes: {line}");
            }
        }
        catch
        {
            // A count is never worth the stream.
        }
    }

    /// <summary>
    /// The console's words for a stretch of the stream: what went wrong in
    /// it, or "clean" when nothing did and it is time to say so, or null.
    /// </summary>
    internal static string? HealthLine(Health health, ListenAlong.Delivery? delivery, bool sayClean)
    {
        var trouble = new List<string>();
        var shape = new List<string>();

        foreach (var (name, bed) in new[] { ("music", health.Music), ("crowd", health.Crowd) })
        {
            if (bed.Heard == 0)
            {
                continue;
            }

            if (bed.Drops > 0)
            {
                trouble.Add($"{name} ran dry {Plural.Of(bed.Drops, "time")} ({Seconds(bed.DropSeconds)} silent, longest {Seconds(bed.LongestDropSeconds)})");
            }

            if (bed.Skips > 0)
            {
                trouble.Add($"{name} trimmed {Plural.Of(bed.Skips, "time")} ({Seconds(bed.SkipSeconds)} jumped)");
            }

            var rate = bed.SourceRate == SampleRate ? "" : $", resampled from {bed.SourceRate} Hz";
            shape.Add($"{name} held {Seconds(bed.LeastHeldSeconds)} to {Seconds(bed.MostHeldSeconds)}{rate}");
        }

        if (health.Dropped > 0)
        {
            trouble.Add($"{Plural.Of(health.Dropped, "read")} of sound dropped before the encoder");
        }

        var late = delivery is { Late: > 0 } or { Missed: > 0 };

        if (trouble.Count == 0 && !late && !sayClean)
        {
            return null;
        }

        var parts = new List<string> { trouble.Count == 0 ? "clean" : string.Join("; ", trouble) };
        parts.AddRange(shape);

        if (delivery is { } sent)
        {
            parts.Add(sent.Words());
        }

        return string.Join(". ", parts) + ".";
    }

    private static string Seconds(double seconds) =>
        seconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s";

    /// <summary>One stretch of the stream's counts - see <see cref="HealthEvery"/>.</summary>
    internal readonly record struct Health(BedHealth Music, BedHealth Crowd, int Dropped);

    /// <summary>
    /// What one bed did in a stretch. Heard is how many of the bus's reads
    /// had this bed under them; a bed that played nothing is left out of
    /// the line.
    /// </summary>
    internal readonly record struct BedHealth(
        int Heard,
        int Drops,
        double DropSeconds,
        double LongestDropSeconds,
        int Skips,
        double SkipSeconds,
        double LeastHeldSeconds,
        double MostHeldSeconds,
        int SourceRate);

    private void Say(string? problem)
    {
        if (problem is { Length: > 0 } && !string.Equals(problem, _problemSaid, StringComparison.Ordinal))
        {
            _problemSaid = problem;
            _log?.Invoke($"[relay] Listening along: {problem}");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _poke.Set();
        _keeper.Join(TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// One stream, from listening being switched on to it being switched
    /// off (or failing). A new one is a new encoder and a new numbering.
    /// </summary>
    internal sealed class Run : AudioBus.IMixListener
    {
        internal const int BusSource = 0;
        internal const int MusicSource = 1;
        internal const int CrowdSource = 2;

        /// <summary>The block the noise floor is judged in - one mp3 frame of the mix.</summary>
        private const int Block = 1152;

        /// <summary>How many blocks the loudness log remembers - about a minute and a half.</summary>
        private const int LoudRing = 4096;

        private readonly Action<ListenSegment> _send;
        private readonly BlockingCollection<Chunk> _in = new(boundedCapacity: 128);
        private readonly BlockingCollection<short[]> _pcm = new(boundedCapacity: 256);
        private readonly Bed[] _beds = { new(), new() };
        private readonly long[] _loud = new long[LoudRing];
        private readonly ConcurrentQueue<(long Sample, string Text)> _captions = new();
        private readonly long _runTicks = DateTime.UtcNow.Ticks;
        private Thread? _mixer;
        private Thread? _encoder;

        /// <summary>Mono samples handed to the encoder so far - the stream's clock.</summary>
        private long _fed;

        /// <summary>Device reads, or mixed buffers, thrown away because a queue was full - each one a hole in the stream. See <see cref="HealthEvery"/>.</summary>
        private int _dropped;

        private volatile bool _up;
        private volatile bool _finished;
        private volatile bool _stopping;
        private volatile string? _problem;

        public Run(Action<ListenSegment> send)
        {
            _send = send;
            Array.Fill(_loud, -1L);
        }

        public bool Up => _up;

        public bool Finished => _finished;

        public string? Problem => _problem;

        public void Start()
        {
            _mixer = new Thread(Mix) { IsBackground = true, Name = "Listen stream mixer", Priority = ThreadPriority.BelowNormal };
            _encoder = new Thread(Encode) { IsBackground = true, Name = "Listen stream encoder", Priority = ThreadPriority.BelowNormal };
            _mixer.Start();
            _encoder.Start();
        }

        public void Stop()
        {
            _stopping = true;
            _in.CompleteAdding();
            _mixer?.Join(TimeSpan.FromSeconds(2));
            _pcm.CompleteAdding();
            _encoder?.Join(TimeSpan.FromSeconds(3));
        }

        public void Caption(string text) => _captions.Enqueue((Interlocked.Read(ref _fed), text));

        /// <summary>The counts since the last time this was asked, which starts them again. Any thread.</summary>
        public Health TakeHealth() =>
            new(_beds[MusicSource - 1].TakeHealth(), _beds[CrowdSource - 1].TakeHealth(), Interlocked.Exchange(ref _dropped, 0));

        /// <summary>The bus's on-air mix - THE DEVICE'S OWN THREAD. A copy and a queue add.</summary>
        public void Mixed(float[] buffer, int offset, int count) =>
            Take(BusSource, buffer, offset, count, AudioBus.MixSampleRate, AudioBus.MixChannels);

        /// <summary>A copy of a device's read, queued for the mixer. Dropped, not waited for, when the queue is full.</summary>
        public void Take(int source, float[] buffer, int offset, int count, int sampleRate, int channels)
        {
            try
            {
                if (_stopping || count <= 0 || offset < 0 || offset + count > buffer.Length)
                {
                    return;
                }

                var copy = new float[count];
                Array.Copy(buffer, offset, copy, 0, count);

                if (!_in.TryAdd(new Chunk(source, copy, sampleRate, Math.Max(1, channels))))
                {
                    Interlocked.Increment(ref _dropped);
                }
            }
            catch
            {
                // Closed a moment ago, or anything else: the room plays on.
            }
        }

        private void Mix()
        {
            try
            {
                foreach (var chunk in _in.GetConsumingEnumerable())
                {
                    var mono = RecapBed.ToMonoAtRate(chunk.Samples, chunk.Samples.Length, chunk.Channels, chunk.Rate, SampleRate);

                    if (chunk.Source != BusSource)
                    {
                        _beds[chunk.Source - 1].Add(mono, chunk.Rate);
                        continue;
                    }

                    foreach (var bed in _beds)
                    {
                        bed.Under(mono);
                    }

                    var start = Interlocked.Read(ref _fed);
                    var pcm = new short[mono.Length];

                    for (var i = 0; i < mono.Length; i++)
                    {
                        var level = mono[i];

                        if (Math.Abs(level) > LoudEnough)
                        {
                            var block = (start + i) / Block;
                            _loud[block % LoudRing] = block;
                        }

                        pcm[i] = RecapBed.ToPcm16(level);
                    }

                    Interlocked.Add(ref _fed, mono.Length);

                    // Waits rather than drops: the encoder keeps up with real
                    // time many times over, and a dropped buffer is a hole
                    // in the music.
                    if (!_pcm.TryAdd(pcm, 1000))
                    {
                        Interlocked.Increment(ref _dropped);
                    }
                }
            }
            catch (Exception ex)
            {
                Fail($"the room's sound could not be mixed ({ex.Message}).");
            }
        }

        private void Encode()
        {
            try
            {
                MediaFoundationApi.Startup();
            }
            catch (Exception ex)
            {
                Fail($"Windows Media Foundation is not available ({ex.Message}), so only the announcer's lines are sent.");
                return;
            }

            var cutter = new Cutter(this);
            string? why = null;

            // Lowest that still carries music first. Not every Windows build
            // offers mono at this rate - see RaceAudioRecorder.TryEncode -
            // so stereo is the last resort.
            foreach (var (channels, bitRate) in new[] { (1, 96_000), (1, 128_000), (2, 128_000) })
            {
                try
                {
                    MediaFoundationEncoder.EncodeToMp3(new Feed(_pcm, channels), cutter, bitRate);

                    if (_stopping)
                    {
                        _finished = true;
                        return;
                    }

                    why = "the encoder stopped on its own.";
                    break;
                }
                catch (Exception ex)
                {
                    why = ex.Message;

                    // Started and then broke: not a format problem, so the
                    // next format would not help. The keeper starts again.
                    if (cutter.Started || _stopping)
                    {
                        break;
                    }
                }
            }

            Fail(_stopping ? null : $"the room's sound could not be encoded ({why}), so only the announcer's lines are sent for now.");
        }

        private void Fail(string? problem)
        {
            _problem ??= problem;
            _up = false;
            _finished = true;
        }

        /// <summary>Whether anything above the noise floor was mixed between these two samples.</summary>
        private bool LoudBetween(long fromSample, long toSample)
        {
            for (var block = Math.Max(0, fromSample / Block); block <= toSample / Block; block++)
            {
                if (_loud[block % LoudRing] == block)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A segment is cut: captions attached, silence thrown away, the rest sent.</summary>
        private void Cut(byte[] mp3, int lead, int frames, int samplesPerFrame, int sampleRate, long number, long firstFrame)
        {
            _up = true;

            var from = firstFrame * samplesPerFrame;
            var to = from + ((long)frames * samplesPerFrame);
            var captions = new List<ListenCaption>();

            while (_captions.TryPeek(out var next) && next.Sample < to)
            {
                _captions.TryDequeue(out _);
                captions.Add(new ListenCaption(next.Text, Math.Max(0, (next.Sample - from) / (double)sampleRate)));
            }

            // Two frames either side for the encoder's own delay.
            if (captions.Count == 0 && !LoudBetween(from - (2L * samplesPerFrame), to + samplesPerFrame))
            {
                return;
            }

            _send(new ListenSegment(mp3, lead, frames, samplesPerFrame, sampleRate, _runTicks, number, captions, DateTime.UtcNow));
        }

        private readonly record struct Chunk(int Source, float[] Samples, int Rate, int Channels);

        /// <summary>
        /// The music or the crowd, held <see cref="BedLeadSeconds"/> behind its device and laid under the bus.
        ///
        /// Counted as it goes (11 Nov 2026 - see <see cref="HealthEvery"/>),
        /// the two things it does to the sound that the room never hears:
        /// running dry, which is silence until half a second has built up
        /// again, and being trimmed, which jumps the music forward. A dry
        /// spell counts as a drop only when the music was sounding as it
        /// ran out and came back within <see cref="LongestDrop"/> - a track
        /// fading out, or the music being stopped, is not one. Add and Under
        /// are the mixer thread's; TakeHealth is anybody's.
        /// </summary>
        internal sealed class Bed
        {
            /// <summary>A dry spell longer than this is the music having stopped, not a drop.</summary>
            internal const double LongestDrop = 5.0;

            private readonly Queue<float[]> _parts = new();
            private readonly object _tally = new();
            private int _head;
            private int _held;
            private bool _primed;

            // The dry spell under way: how much silence it has made so far,
            // and whether it cut the sound off rather than following a fade.
            private bool _dry;
            private long _drySamples;
            private bool _dryCutSound;

            // The loudest of the last read that laid anything under the bus.
            private float _lastPeak;

            // Since the last TakeHealth, under _tally.
            private int _heard;
            private int _drops;
            private long _dropSamples;
            private long _longestDropSamples;
            private int _skips;
            private long _skipSamples;
            private int _leastHeld = int.MaxValue;
            private int _mostHeld;
            private int _sourceRate = SampleRate;

            /// <param name="sourceRate">The device's own rate, before it was brought to the stream's - for the console only.</param>
            public void Add(float[] mono, int sourceRate = SampleRate)
            {
                if (mono.Length == 0)
                {
                    return;
                }

                _parts.Enqueue(mono);
                _held += mono.Length;

                // Nothing is taking it (see BedStalledSeconds). Not counted
                // as a trim here: Under counts the jump when the bus is back.
                var most = (int)(BedStalledSeconds * SampleRate);

                if (_held > most)
                {
                    Skip(_held - most);
                }

                if (sourceRate > 0)
                {
                    Volatile.Write(ref _sourceRate, sourceRate);
                }
            }

            /// <summary>Adds the next stretch of this bed into <paramref name="mix"/>, as long as the mix.</summary>
            public void Under(float[] mix)
            {
                var lead = (int)(BedLeadSeconds * SampleRate);

                if (!_primed)
                {
                    if (_held < lead)
                    {
                        if (_dry)
                        {
                            _drySamples += mix.Length;
                        }

                        return;
                    }

                    _primed = true;

                    if (_dry)
                    {
                        EndDry();
                    }
                }

                if (_held > BedMostSeconds * SampleRate)
                {
                    var trimmed = _held - lead;
                    Skip(trimmed);

                    lock (_tally)
                    {
                        _skips++;
                        _skipSamples += trimmed;
                    }
                }

                lock (_tally)
                {
                    _heard++;
                    _leastHeld = Math.Min(_leastHeld, _held);
                    _mostHeld = Math.Max(_mostHeld, _held);
                }

                var at = 0;
                var peak = 0f;

                while (at < mix.Length && _parts.Count > 0)
                {
                    var part = _parts.Peek();
                    var n = Math.Min(mix.Length - at, part.Length - _head);

                    for (var i = 0; i < n; i++)
                    {
                        var level = part[_head + i];
                        mix[at + i] += level;
                        peak = Math.Max(peak, Math.Abs(level));
                    }

                    at += n;
                    Used(part, n);
                }

                if (at > 0)
                {
                    _lastPeak = peak;
                }

                // Ran dry: silence until it has caught up again, rather than
                // a stutter of whatever arrives.
                if (at < mix.Length)
                {
                    _primed = false;
                    _dry = true;
                    _drySamples = mix.Length - at;
                    _dryCutSound = _lastPeak > LoudEnough;
                }
            }

            /// <summary>The counts since the last time this was asked, which starts them again.</summary>
            public BedHealth TakeHealth()
            {
                lock (_tally)
                {
                    var health = new BedHealth(
                        _heard,
                        _drops,
                        (double)_dropSamples / SampleRate,
                        (double)_longestDropSamples / SampleRate,
                        _skips,
                        (double)_skipSamples / SampleRate,
                        _heard == 0 ? 0 : (double)_leastHeld / SampleRate,
                        (double)_mostHeld / SampleRate,
                        Volatile.Read(ref _sourceRate));

                    _heard = 0;
                    _drops = 0;
                    _dropSamples = 0;
                    _longestDropSamples = 0;
                    _skips = 0;
                    _skipSamples = 0;
                    _leastHeld = int.MaxValue;
                    _mostHeld = 0;
                    return health;
                }
            }

            private void EndDry()
            {
                _dry = false;

                if (!_dryCutSound || _drySamples > LongestDrop * SampleRate)
                {
                    return;
                }

                lock (_tally)
                {
                    _drops++;
                    _dropSamples += _drySamples;
                    _longestDropSamples = Math.Max(_longestDropSamples, _drySamples);
                }
            }

            private void Skip(int samples)
            {
                while (samples > 0 && _parts.Count > 0)
                {
                    var part = _parts.Peek();
                    var n = Math.Min(samples, part.Length - _head);
                    samples -= n;
                    Used(part, n);
                }
            }

            private void Used(float[] part, int n)
            {
                _head += n;
                _held -= n;

                if (_head >= part.Length)
                {
                    _parts.Dequeue();
                    _head = 0;
                }
            }
        }

        /// <summary>
        /// The mixed audio as the encoder reads it: blocks until there is
        /// some, and ends when the run is stopped.
        /// </summary>
        private sealed class Feed : IWaveProvider
        {
            private readonly BlockingCollection<short[]> _pcm;
            private readonly int _channels;
            private short[]? _current;
            private int _at;

            public Feed(BlockingCollection<short[]> pcm, int channels)
            {
                _pcm = pcm;
                _channels = channels;
                WaveFormat = new WaveFormat(SampleRate, 16, channels);
            }

            public WaveFormat WaveFormat { get; }

            public int Read(byte[] buffer, int offset, int count)
            {
                var frameBytes = 2 * _channels;
                var written = 0;

                while (written + frameBytes <= count)
                {
                    if (_current is null || _at >= _current.Length)
                    {
                        // Hand over what there is rather than wait for more:
                        // the encoder's next read comes straight back.
                        if (written > 0 && _pcm.Count == 0)
                        {
                            break;
                        }

                        if (!_pcm.TryTake(out _current, Timeout.Infinite))
                        {
                            break;
                        }

                        _at = 0;
                    }

                    var sample = _current[_at++];

                    for (var c = 0; c < _channels; c++)
                    {
                        buffer[offset + written] = (byte)sample;
                        buffer[offset + written + 1] = (byte)(sample >> 8);
                        written += 2;
                    }
                }

                return written;
            }
        }

        /// <summary>
        /// Where the encoder writes: a stream that finds the mp3 frames in
        /// what it is given and cuts them into segments. The tag the encoder
        /// writes first is skipped, and so is the header it goes back to
        /// rewrite when it is closed - the frames already went out.
        /// </summary>
        private sealed class Cutter : Stream
        {
            private readonly Run _run;
            private readonly List<byte[]> _frames = new();
            private readonly Queue<byte[]> _lead = new();
            private byte[] _pending = new byte[16 * 1024];
            private int _count;
            private long _length;
            private long _position;
            private bool _tagChecked;
            private long _skip;
            private long _framesCut;
            private long _number;
            private int _samplesPerFrame = 1152;
            private int _rate = SampleRate;

            public Cutter(Run run) => _run = run;

            /// <summary>Whether any of the stream has been written - so a failure after it is not the format's fault.</summary>
            public bool Started => _length > 0;

            public override bool CanRead => false;

            public override bool CanSeek => true;

            public override bool CanWrite => true;

            public override long Length => _length;

            public override long Position
            {
                get => _position;
                set => _position = value;
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin)
            {
                _position = origin switch
                {
                    SeekOrigin.Begin => offset,
                    SeekOrigin.End => _length + offset,
                    _ => _position + offset,
                };

                return _position;
            }

            public override void SetLength(long value)
            {
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                // Anything but the end is the encoder rewriting its header
                // as it closes - nothing a listener needs.
                if (_position != _length)
                {
                    _position += count;
                    return;
                }

                _position += count;
                _length += count;

                if (_count + count > _pending.Length)
                {
                    Array.Resize(ref _pending, Math.Max(_pending.Length * 2, _count + count));
                }

                Buffer.BlockCopy(buffer, offset, _pending, _count, count);
                _count += count;
                Parse();
            }

            private void Parse()
            {
                var at = 0;

                while (true)
                {
                    if (!_tagChecked)
                    {
                        if (_count - at < 10)
                        {
                            break;
                        }

                        _tagChecked = true;
                        _skip = Mp3Frames.TagLength(_pending, at);
                    }

                    if (_skip > 0)
                    {
                        var n = (int)Math.Min(_skip, _count - at);
                        _skip -= n;
                        at += n;

                        if (_skip > 0)
                        {
                            break;
                        }

                        continue;
                    }

                    if (_count - at < 4)
                    {
                        break;
                    }

                    if (!Mp3Frames.TryHeader(_pending, at, out var length, out var samples, out var rate))
                    {
                        at++;
                        continue;
                    }

                    if (_count - at < length)
                    {
                        break;
                    }

                    var frame = new byte[length];
                    Buffer.BlockCopy(_pending, at, frame, 0, length);
                    at += length;
                    _samplesPerFrame = samples;
                    _rate = rate;
                    Add(frame);
                }

                if (at > 0)
                {
                    Buffer.BlockCopy(_pending, at, _pending, 0, _count - at);
                    _count -= at;
                }
            }

            private void Add(byte[] frame)
            {
                _frames.Add(frame);

                var wanted = (int)Math.Ceiling(SegmentSeconds * _rate / _samplesPerFrame);

                if (_frames.Count < wanted)
                {
                    return;
                }

                var lead = _lead.ToArray();
                var bytes = new byte[lead.Sum(f => f.Length) + _frames.Sum(f => f.Length)];
                var at = 0;

                foreach (var one in lead.Concat(_frames))
                {
                    Buffer.BlockCopy(one, 0, bytes, at, one.Length);
                    at += one.Length;
                }

                var first = _framesCut;
                _framesCut += _frames.Count;

                try
                {
                    _run.Cut(bytes, lead.Length, _frames.Count, _samplesPerFrame, _rate, _number++, first);
                }
                catch
                {
                    // Sending is the publisher's business; a segment it
                    // could not take is a gap, and the stream goes on.
                }

                foreach (var one in _frames.Skip(Math.Max(0, _frames.Count - LeadFrames)))
                {
                    _lead.Enqueue(one);
                }

                while (_lead.Count > LeadFrames)
                {
                    _lead.Dequeue();
                }

                _frames.Clear();
            }
        }
    }
}

/// <summary>Just enough of the mp3 format to find its frames - see ListenStream.</summary>
internal static class Mp3Frames
{
    private static readonly int[] Mpeg1Layer3Kbps = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 };
    private static readonly int[] Mpeg2Layer3Kbps = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 };
    private static readonly int[] Mpeg1Rates = { 44100, 48000, 32000 };

    /// <summary>How long the ID3v2 tag at <paramref name="at"/> is, header and all - or 0 when there is none. Needs ten bytes.</summary>
    internal static long TagLength(byte[] bytes, int at)
    {
        if (bytes.Length - at < 10 || bytes[at] != (byte)'I' || bytes[at + 1] != (byte)'D' || bytes[at + 2] != (byte)'3')
        {
            return 0;
        }

        var size = ((long)(bytes[at + 6] & 0x7F) << 21) | ((long)(bytes[at + 7] & 0x7F) << 14)
                   | ((long)(bytes[at + 8] & 0x7F) << 7) | (long)(bytes[at + 9] & 0x7F);
        var footer = (bytes[at + 5] & 0x10) != 0 ? 10 : 0;

        return 10 + size + footer;
    }

    /// <summary>Whether a Layer III frame header starts at <paramref name="at"/>, and if so how long the frame is and what it decodes to.</summary>
    internal static bool TryHeader(byte[] bytes, int at, out int length, out int samples, out int rate)
    {
        length = 0;
        samples = 0;
        rate = 0;

        if (bytes.Length - at < 4 || bytes[at] != 0xFF || (bytes[at + 1] & 0xE0) != 0xE0)
        {
            return false;
        }

        var version = (bytes[at + 1] >> 3) & 3;
        var layer = (bytes[at + 1] >> 1) & 3;
        var bitIndex = bytes[at + 2] >> 4;
        var rateIndex = (bytes[at + 2] >> 2) & 3;
        var padding = (bytes[at + 2] >> 1) & 1;

        // Layer III only, and not the reserved or free values.
        if (version == 1 || layer != 1 || bitIndex is 0 or 15 || rateIndex == 3)
        {
            return false;
        }

        var mpeg1 = version == 3;
        var kbps = mpeg1 ? Mpeg1Layer3Kbps[bitIndex] : Mpeg2Layer3Kbps[bitIndex];

        rate = Mpeg1Rates[rateIndex] / (mpeg1 ? 1 : version == 2 ? 2 : 4);
        samples = mpeg1 ? 1152 : 576;
        length = ((mpeg1 ? 144_000 : 72_000) * kbps / rate) + padding;

        return length > 4;
    }
}
