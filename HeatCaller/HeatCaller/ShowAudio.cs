using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// The show's own music: the opener that lands in front of the voice, and
/// the bed that runs underneath it.
///
/// WHY IN MEMORY
///
/// Both pieces are loaded whole into a float array the first time they are
/// used and kept. A bed has to loop seamlessly for as long as the show
/// runs, and looping a file off disk means seeking a reader back to the
/// loop point - which on an mp3 is approximate, and which stalls for as
/// long as the decoder takes. A sample array loops exactly, crossfades
/// without arithmetic on a reader's position, and cannot hiccup because
/// the laptop chose that moment to write a backup. An 88-second stereo bed
/// is about 34 MB held, which is a fair price for the one piece of audio
/// in the app that absolutely must not stutter.
///
/// WHY NOT A NEW OUTPUT DEVICE
///
/// Both play through AudioBus, which is already open and warm - see the
/// long note at the top of that class. Opening an output device mid-race
/// stalls everything else playing for a moment, and the top of the show is
/// the worst moment in the evening to make the music skip.
///
/// NAMING. "Stinger" is already taken in this app: StingerLibrary holds
/// recorded VOICE lines for the fixed race calls. The piece of music in
/// front of the voice is the opener here, and never a stinger.
/// </summary>
internal sealed class ShowClip
{
    /// <summary>
    /// The longest piece either part may be. A bed is looped, so a club
    /// pointing this at an hour-long album track is a mistake worth
    /// telling them about rather than quietly holding 1.4 GB.
    /// </summary>
    public const double MostSeconds = 300;

    private ShowClip(float[] samples, WaveFormat format, ShowShape shape)
    {
        Samples = samples;
        Format = format;
        Shape = shape;
    }

    /// <summary>Every sample, interleaved, at the file's own rate and channel count.</summary>
    public float[] Samples { get; }

    public WaveFormat Format { get; }

    /// <summary>Where the body of the piece starts and ends - see ShowShape.</summary>
    public ShowShape Shape { get; }

    public int Channels => Math.Max(1, Format.Channels);

    public int SampleRate => Math.Max(1, Format.SampleRate);

    /// <summary>Length in frames (one frame is one sample per channel).</summary>
    public int Frames => Samples.Length / Channels;

    public TimeSpan Length => TimeSpan.FromSeconds((double)Frames / SampleRate);

    /// <summary>
    /// Loads a piece of audio, or returns null and says why in plain words
    /// - a message fit to put in front of whoever chose the file.
    ///
    /// Never throws: a show whose music will not load is a show read
    /// without music, which is what the announcer did before any of this.
    /// </summary>
    public static ShowClip? Load(string? path, out string? problem)
    {
        problem = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            problem = "No file chosen.";
            return null;
        }

        if (!File.Exists(path))
        {
            problem = $"{Path.GetFileName(path)} isn't there any more.";
            return null;
        }

        try
        {
            using var reader = new AudioFileReader(path);

            var channels = Math.Max(1, reader.WaveFormat.Channels);
            var rate = Math.Max(1, reader.WaveFormat.SampleRate);
            var most = (long)(MostSeconds * rate) * channels;

            // The show's output (AudioBus) plays mono or stereo and nothing
            // else. Refused here, where the club is told, rather than
            // accepted and then silent at the show with the playlist muted.
            if (channels > 2)
            {
                problem = $"{Path.GetFileName(path)} has {channels} channels - use a mono or stereo file.";
                return null;
            }

            if (reader.Length > 0)
            {
                // Length is in bytes of the reader's own 32-bit float output.
                var samples = reader.Length / 4;
                if (samples > most)
                {
                    problem = $"{Path.GetFileName(path)} is {reader.TotalTime.TotalMinutes:0.#} minutes long. "
                        + $"Use something under {MostSeconds / 60:0} minutes - the bed is looped, so it doesn't need to be long.";
                    return null;
                }
            }

            var read = new List<float>(capacity: 1 << 20);
            var buffer = new float[1 << 16];

            while (true)
            {
                var got = reader.Read(buffer, 0, buffer.Length);
                if (got <= 0)
                {
                    break;
                }

                for (var i = 0; i < got; i++)
                {
                    read.Add(buffer[i]);
                }

                // A file whose header lied about its length, or a stream
                // that does not end. Stops rather than filling memory.
                if (read.Count > most)
                {
                    problem = $"{Path.GetFileName(path)} is longer than {MostSeconds / 60:0} minutes.";
                    return null;
                }
            }

            // Whole frames only, so nothing downstream has to cope with
            // half of one at the end.
            var whole = read.Count - read.Count % channels;

            if (whole <= 0)
            {
                problem = $"{Path.GetFileName(path)} has no sound in it.";
                return null;
            }

            var samplesArray = new float[whole];
            read.CopyTo(0, samplesArray, 0, whole);

            var shape = ShowShape.Find(samplesArray, channels, rate);

            return new ShowClip(samplesArray, reader.WaveFormat, shape);
        }
        catch (Exception ex)
        {
            problem = $"{Path.GetFileName(path)} couldn't be read: {ex.Message}";
            return null;
        }
    }
}

/// <summary>
/// Where the body of a piece of music starts and ends, in frames - that
/// is, with any fade-in at the front and any fade-out at the back found
/// and set aside.
///
/// WHY THIS IS MEASURED RATHER THAN TYPED IN
///
/// A bed almost always has its own fade-out on the end, because it was
/// written as a piece of music rather than as a loop. Looping the whole
/// file would fade down to nothing and snap back to full every time round.
/// So the body is what loops, and the fade on the end is kept back and
/// played once, as the show's own ending - which is a better ending than
/// anything the app could fade for itself.
///
/// Finding it by measuring means it works for whatever a club drops in,
/// rather than for the two files this was built against.
///
/// HOW THE END IS FOUND, IN TWO PASSES
///
/// First by level: anything at the end more than FadeBelowDb under the
/// body's own level is certainly not music any more. That alone is not
/// enough, because it finds the MIDDLE of a fade rather than its top - on
/// a straight fade, -6 dB is halfway down - and a loop carrying the first
/// half of a fade droops audibly every time round.
///
/// So the second pass walks back up the slope: while the music was
/// meaningfully louder half a second earlier, it was still fading, so the
/// fade started earlier than that. A body at a steady level stops the walk
/// at once, because half a second earlier it was no louder. That finds the
/// top of a fade whether it takes one second or ten.
///
/// The start is found the same way in reverse: while the music was
/// meaningfully quieter half a second earlier, it was still coming up.
///
/// If tightening both ends would leave too little to loop - a piece that
/// is all one long swell, or one so quiet throughout that the measurement
/// finds nothing - the looser pair is used, and failing that the whole
/// piece. There was no ending to keep back in that case, which is the
/// honest answer rather than a loop of four seconds.
/// </summary>
internal sealed record ShowShape(int LoopStart, int LoopEnd)
{
    /// <summary>How far under the body's level counts as a fade rather than a quiet passage.</summary>
    private const double FadeBelowDb = -6.0;

    /// <summary>
    /// How far back the second pass looks, in windows - about half a
    /// second. Comparing neighbouring windows instead would mistake the
    /// hiss between two beats for a fade, and would give up on a slow fade
    /// whose neighbouring windows barely differ.
    /// </summary>
    private const int SlopeWindows = 10;

    /// <summary>
    /// How much louder it has to have been SlopeWindows earlier to count
    /// as still coming down. A steady body varies by less than this, which
    /// is what stops the walk; any real fade exceeds it.
    /// </summary>
    private const double SlopeDrop = 1.04;

    /// <summary>Measured over windows this long - short enough to find the knee of a fade, long enough to ignore one quiet beat.</summary>
    private const double WindowSeconds = 0.05;

    /// <summary>
    /// A body shorter than this is not believed, and the whole piece is
    /// used instead. Guards against a piece that is all fade (a swell), or
    /// one so quiet throughout that the measurement finds nothing.
    /// </summary>
    private const double ShortestBodySeconds = 4.0;

    public int BodyFrames => Math.Max(0, LoopEnd - LoopStart);

    /// <summary>Finds the body of a piece - see the type summary.</summary>
    public static ShowShape Find(float[] samples, int channels, int sampleRate)
    {
        channels = Math.Max(1, channels);
        sampleRate = Math.Max(1, sampleRate);

        var frames = samples.Length / channels;
        var whole = new ShowShape(0, frames);

        var window = Math.Max(1, (int)(WindowSeconds * sampleRate));
        var count = frames / window;

        if (count < 8)
        {
            // Too short to measure - a second or two of audio is all body.
            return whole;
        }

        var level = new double[count];

        for (var w = 0; w < count; w++)
        {
            var sum = 0.0;
            var from = w * window * channels;
            var to = from + window * channels;

            for (var i = from; i < to && i < samples.Length; i++)
            {
                sum += (double)samples[i] * samples[i];
            }

            level[w] = Math.Sqrt(sum / (window * channels));
        }

        // The body's own level, as the middle of the piece plays it. The
        // median of the middle stretch rather than the mean of everything:
        // a long fade would drag a mean down and then be measured against
        // itself.
        var middle = level.Skip(count / 5).Take(Math.Max(1, count - 2 * (count / 5))).OrderBy(v => v).ToArray();
        var body = middle[middle.Length / 2];

        if (body <= 0)
        {
            return whole;
        }

        var floor = body * Math.Pow(10, FadeBelowDb / 20.0);

        var first = 0;
        while (first < count && level[first] < floor)
        {
            first++;
        }

        var last = count - 1;
        while (last > first && level[last] < floor)
        {
            last--;
        }

        if (first >= count)
        {
            return whole;
        }

        var least = (int)(ShortestBodySeconds * sampleRate);

        // Up the slope to the top of the fade at the end, and forward over
        // any fade-in at the start - see the type summary.
        var fadeTop = last;

        while (fadeTop - SlopeWindows > first && level[fadeTop - SlopeWindows] > level[fadeTop] * SlopeDrop)
        {
            fadeTop -= SlopeWindows;
        }

        var riseTop = first;

        while (riseTop + SlopeWindows < fadeTop && level[riseTop + SlopeWindows] > level[riseTop] * SlopeDrop)
        {
            riseTop += SlopeWindows;
        }

        // The END of the last window still at level: the fade begins after
        // it, not inside it. The start is the beginning of its window.
        var tight = new ShowShape(riseTop * window, Math.Min(frames, fadeTop * window));
        var loose = new ShowShape(first * window, Math.Min(frames, (last + 1) * window));

        if (tight.BodyFrames >= least)
        {
            return tight;
        }

        return loose.BodyFrames >= least ? loose : whole;
    }
}

/// <summary>
/// The music bed: the body of a piece looped for as long as the show runs,
/// and then the piece's own ending played once to finish.
///
/// THE SEAM
///
/// Looping jumps from the end of the body back to its start, and unless
/// the two happen to match it clicks. Rather than asking a club to find a
/// musically perfect loop point, the last SeamSeconds of the body are
/// mixed over the first SeamSeconds of it, so every time round is
/// identical and the join is a crossfade rather than an edge. The first
/// time through is played straight - there is nothing behind it yet to
/// fade up from.
///
/// THE ENDING
///
/// WindUp crossfades from wherever the loop has got to into the fade the
/// piece came with, and lets it play out; the provider then runs short,
/// which is how AudioBus knows the clip is over. A piece with no fade of
/// its own is faded out here instead, over FadeOutSeconds.
///
/// THREADS. WindUp is called from the UI thread; everything else happens
/// inside Read on the audio thread, so the only thing shared is one
/// volatile flag.
/// </summary>
internal sealed class ShowBed : ISampleProvider
{
    /// <summary>How long the loop's crossfade is. Long enough to hide a join, short enough not to smear two bars together.</summary>
    public const double SeamSeconds = 0.25;

    /// <summary>How long the bed takes to fade out when the piece has no ending of its own.</summary>
    public const double FadeOutSeconds = 2.5;

    /// <summary>
    /// A tail shorter than this is not an ending.
    ///
    /// A piece that simply stops still leaves a window or two past the
    /// body, because the measurement works in windows. Handing over to
    /// fifty milliseconds of audio and stopping is a cut, which is the one
    /// way a show must not end - so a tail this short is treated as no
    /// ending at all and the bed fades itself out instead.
    /// </summary>
    public const double ShortestEndingSeconds = 0.5;

    /// <summary>
    /// How long a bed takes to get out of the way when it is cut short
    /// rather than ended.
    ///
    /// A race intro's show has to be gone the moment the countdown starts,
    /// and the piece's own ending - a three second fade, say - would play
    /// straight over the beeps and the horn. Short enough to be clear
    /// before a countdown is under way, long enough not to be a click.
    /// </summary>
    public const double CutShortSeconds = 1.0;

    private enum Part
    {
        /// <summary>First time through the head of the body - played straight, nothing to fade up from.</summary>
        Opening,

        /// <summary>Round and round, with the seam crossfaded.</summary>
        Looping,

        /// <summary>The piece's own ending, or a fade out where it has none.</summary>
        Ending,

        Done,
    }

    private readonly float[] _samples;
    private readonly int _channels;
    private readonly int _loopStart;
    private readonly int _loopEnd;

    /// <summary>Frames of crossfade at the seam, and of the handover into the ending.</summary>
    private readonly int _seam;

    /// <summary>Frames in one time round: the body less the seam, which is overlapped.</summary>
    private readonly int _period;

    /// <summary>Frames of the piece's own ending, after the body. Zero when it has none.</summary>
    private readonly int _endingFrames;

    /// <summary>Frames to fade out over when there is no ending of its own.</summary>
    private readonly int _fadeFrames;

    /// <summary>Frames to fade out over when the show is cut short - see CutShort.</summary>
    private readonly int _cutFrames;

    private Part _part = Part.Opening;

    /// <summary>
    /// True once the body has been round at least once, so there is a tail
    /// to crossfade its head against. Kept apart from _part because the
    /// loop goes on playing underneath the hand-over into the ending, and
    /// the seam has to stay crossfaded while it does.
    /// </summary>
    private bool _beenRound;

    /// <summary>Where we are inside the loop's period.</summary>
    private int _at;

    /// <summary>Where we are inside the ending.</summary>
    private int _ending;

    /// <summary>Frames of handover left between the loop and the ending.</summary>
    private int _handover;

    private volatile bool _windUpAsked;
    private volatile bool _cutShortAsked;

    // Settled once, at the moment the ending starts, so a cut asked for
    // midway through an ending cannot change what is already playing.
    private int _endingPlaying;
    private int _fadePlaying;

    public ShowBed(ShowClip clip)
    {
        _samples = clip.Samples;
        _channels = clip.Channels;

        var frames = clip.Frames;
        var shape = clip.Shape;

        _loopStart = Math.Clamp(shape.LoopStart, 0, Math.Max(0, frames - 1));
        _loopEnd = Math.Clamp(shape.LoopEnd, _loopStart + 1, frames);

        var body = _loopEnd - _loopStart;

        // Never more than a third of the body, so a short bed still has a
        // recognisable time round rather than being one long crossfade.
        _seam = Math.Clamp((int)(SeamSeconds * clip.SampleRate), 1, Math.Max(1, body / 3));
        _period = Math.Max(1, body - _seam);

        var tail = Math.Max(0, frames - _loopEnd);

        _endingFrames = tail >= ShortestEndingSeconds * clip.SampleRate ? tail : 0;
        _fadeFrames = Math.Max(1, (int)(FadeOutSeconds * clip.SampleRate));
        _cutFrames = Math.Max(1, (int)(CutShortSeconds * clip.SampleRate));

        WaveFormat = clip.Format;
    }

    public WaveFormat WaveFormat { get; }

    /// <summary>True once the ending has played out and the bed is finished.</summary>
    public bool Finished => _part == Part.Done;

    /// <summary>
    /// Finish: hand over to the piece's own ending and play it out. Safe to
    /// call from any thread, and safe to call twice.
    /// </summary>
    public void WindUp() => _windUpAsked = true;

    /// <summary>
    /// Get out of the way now: a quick fade instead of the piece's own
    /// ending - see CutShortSeconds. For a show whose moment has been
    /// overtaken, which in practice means a race intro's bed when the
    /// countdown starts.
    ///
    /// Asking for this after WindUp has already begun an ending leaves
    /// that ending playing: it is a second or two from done, and cutting
    /// across it would sound worse than letting it finish.
    /// </summary>
    public void CutShort()
    {
        _cutShortAsked = true;
        _windUpAsked = true;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        // Whole frames. Every mixer in NAudio asks in whole frames (it
        // reads into its own buffer sized in them), and a frame is the
        // unit everything below works in - a half-written frame would put
        // the channels out of step for the rest of the night.
        var want = count / _channels;
        var done = 0;

        while (done < want && _part != Part.Done)
        {
            if (_windUpAsked && _part != Part.Ending)
            {
                // What the ending is, settled here and not read again: a
                // cut asked for while one is already playing is ignored.
                _endingPlaying = _cutShortAsked ? 0 : _endingFrames;
                _fadePlaying = _cutShortAsked ? _cutFrames : _fadeFrames;

                _part = Part.Ending;
                _ending = 0;
                _handover = _endingPlaying > 0 ? _seam : 0;
            }

            var into = offset + done * _channels;

            if (_part == Part.Ending)
            {
                WriteEndingFrame(buffer, into);
            }
            else
            {
                WriteLoopFrame(buffer, into);
            }

            done++;
        }

        // Short of what was asked for: AudioBus takes this out of the mix
        // and completes the clip, which is how the show knows the ending
        // has played.
        return done * _channels;
    }

    private void WriteLoopFrame(float[] buffer, int into)
    {
        var head = (_loopStart + _at) * _channels;

        if (_beenRound && _at < _seam)
        {
            // The seam: the body's tail fading out over its head fading in.
            var tail = (_loopStart + _period + _at) * _channels;
            var up = (float)_at / _seam;

            for (var c = 0; c < _channels; c++)
            {
                buffer[into + c] = Sample(head + c) * up + Sample(tail + c) * (1f - up);
            }
        }
        else
        {
            for (var c = 0; c < _channels; c++)
            {
                buffer[into + c] = Sample(head + c);
            }
        }

        if (++_at >= _period)
        {
            _at = 0;
            _beenRound = true;

            if (_part == Part.Opening)
            {
                _part = Part.Looping;
            }
        }
    }

    private void WriteEndingFrame(float[] buffer, int into)
    {
        if (_endingPlaying == 0)
        {
            // No ending of its own, or cut short: keep looping and fade
            // out. _ending counts the fade here.
            var level = 1f - (float)_ending / _fadePlaying;

            WriteLoopFrame(buffer, into);

            for (var c = 0; c < _channels; c++)
            {
                buffer[into + c] *= Math.Max(0f, level);
            }

            if (++_ending >= _fadePlaying)
            {
                _part = Part.Done;
            }

            return;
        }

        var tail = (_loopEnd + _ending) * _channels;

        if (_handover > 0)
        {
            // Crossfade from wherever the loop had got to into the start of
            // the ending, so finishing is a hand-over rather than a cut.
            var up = 1f - (float)_handover / _seam;

            // The loop's own next frame, written first and then mixed under.
            WriteLoopFrame(buffer, into);

            for (var c = 0; c < _channels; c++)
            {
                buffer[into + c] = buffer[into + c] * (1f - up) + Sample(tail + c) * up;
            }

            _handover--;
        }
        else
        {
            for (var c = 0; c < _channels; c++)
            {
                buffer[into + c] = Sample(tail + c);
            }
        }

        if (++_ending >= _endingPlaying)
        {
            _part = Part.Done;
        }
    }

    /// <summary>
    /// One sample, or silence past the end. The indices above are all
    /// inside by construction; this is the backstop that keeps a rounding
    /// error from throwing on the audio thread, where an exception would
    /// take the clip - and the show's music - out mid-sentence.
    /// </summary>
    private float Sample(int index) =>
        index >= 0 && index < _samples.Length ? _samples[index] : 0f;
}

/// <summary>
/// A piece of audio played once from memory, for the opener. Reads its
/// frames out and then runs short, exactly as a file reader would.
///
/// IT CAN START PARTWAY IN, AND IT CAN WAIT A BEAT FIRST
///
/// Both are the show's billing asking for them - see ShowBilling.
///
/// A show in the body of the evening comes in on the opener's ring-out
/// rather than playing the theme from the top for the eleventh time, which
/// is <paramref name="fromFrame"/>. A seek, in other words - except there
/// is nothing to seek, because the piece is already a sample array, which
/// is half of why it is held as one (see ShowClip).
///
/// The beat of silence in front is <paramref name="airFrames"/>. The
/// club's music is silenced the instant a show opens, so without it the
/// playlist stops dead and the opener lands in the same sample - which
/// sounds like the music cutting out, not like a station going live. It is
/// carried here rather than as a Task.Delay in the staging for two
/// reasons: a timer is good to about fifteen milliseconds and this is good
/// to one sample, and an await in the staging is an await that the
/// cancellation and lifetime machinery around a show has to be right
/// about. Silence at the head of a provider is none of those things.
/// </summary>
internal sealed class ShowOneShot : ISampleProvider
{
    private readonly float[] _samples;

    /// <summary>Samples of silence still to write in front of the piece.</summary>
    private int _air;

    private int _at;

    public ShowOneShot(ShowClip clip, int fromFrame = 0, int airFrames = 0)
    {
        _samples = clip.Samples;
        WaveFormat = clip.Format;

        // Multiplied up from FRAMES, never taken as a sample count: a
        // start or a lead-in that landed half a frame in would put the
        // channels out of step for the rest of the piece.
        var channels = clip.Channels;

        _at = Math.Clamp(fromFrame, 0, Math.Max(0, clip.Frames)) * channels;
        _air = Math.Max(0, airFrames) * channels;
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var done = 0;

        if (_air > 0)
        {
            // Whole frames, as everywhere below: every mixer in NAudio asks
            // in them, and both counts above are frame multiples, so the
            // smaller of the two is as well.
            var quiet = Math.Min(_air, count);

            Array.Clear(buffer, offset, quiet);

            _air -= quiet;
            done += quiet;
        }

        var left = _samples.Length - _at;
        var take = Math.Min(count - done, Math.Max(0, left));

        if (take > 0)
        {
            Array.Copy(_samples, _at, buffer, offset + done, take);
            _at += take;
            done += take;
        }

        // Short of what was asked for is the end, as it always was - which
        // is how AudioBus knows the opener has played out. The air counts
        // towards it, so a buffer that is all air still reads as full.
        return done;
    }
}
