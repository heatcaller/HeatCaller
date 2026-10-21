using System.Collections.Concurrent;
using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace HeatCaller;

/// <summary>What one finished recording left on disk, and what went wrong with it if anything did.</summary>
/// <param name="WavPath">The file that was being written. Still there, and still playable, whatever else failed.</param>
/// <param name="BaseName">The name every file from this race is built on - see RaceHighlights.BaseNameFor.</param>
/// <param name="GreenAtMs">How far into the file the green dropped, so the race's own clock can be laid over it.</param>
/// <param name="LengthMs">How much audio really landed in the file.</param>
/// <param name="DroppedChunks">How many buffers the writer could not keep up with. Not zero means a gap in the audio and a warning worth printing.</param>
internal sealed record RaceAudioRecording(
    string WavPath,
    string Folder,
    string BaseName,
    DateTime StartedLocal,
    long GreenAtMs,
    long LengthMs,
    int DroppedChunks,
    string? Problem);

/// <summary>
/// Records the night as it goes out, and afterwards writes it as an mp3 with
/// a clip for each driver.
///
/// WHAT IS RECORDED is the shared bus, AudioBus - everything that comes out
/// of it, as one mix. That is the announcer's lines, the tones and the lap
/// beeps, the stingers, the sponsor reads and the show treatment: the
/// programme as the room heard it. Deliberately NOT only the announcer:
/// pulling the voice out on its own would mean every line being pushed to
/// two places instead of one, which is work in the audio path, and work in
/// the audio path is exactly what AudioBus exists to prevent (its own note:
/// one output, because every line opening its own made the music skip). Here
/// the samples are already assembled in one buffer one step before the
/// speaker, and the tap copies them. It cannot make the mixer do more work
/// than it was already doing.
///
/// The music and the crowd bed are NOT in it, and that falls out of the same
/// fact rather than being a choice made here: MusicPlayer and
/// AmbienceService each keep their own output (see their "new WaveOutEvent"),
/// so they never pass through this bus at all. The upshot is the right one
/// for a club - a driver's clip is the call, clean, with no music under it to
/// argue about - so there is one switch (on or off) and not a second one
/// nobody could explain.
///
/// HOW IT CANNOT HURT A RACE NIGHT:
///  - The tap runs on the sound device's own thread. It copies, converts and
///    hands the buffer to a bounded queue, and never touches the disk,
///    never takes a lock the disk is behind, and never throws. Past the
///    queue's end, buffers are DROPPED and counted - the same bargain
///    SessionLog makes for the console - because a stalled disk must not be
///    allowed to stall the audio.
///  - Stored as mono rather than stereo. The bus is stereo but the content
///    is speech, and mono halves both the disk and the work in the tap.
///  - Capped in length (RaceAudioStore.MaxMinutes) and stopped when the disk
///    gets low (RaceAudioStore.MinFreeMb), because the race's own files -
///    the crash journal and the race history - need that disk far more than
///    this does.
///  - Everything after the flag - the mp3, the clips - happens on a
///    background thread, after the broadcast has ended, never on the UI
///    thread and never during a race.
///  - Every failure is caught, said once in the race log, and leaves the
///    race alone.
///
/// WHY IT IS A WAV WHILE IT RUNS. mp3 has to be encoded, and nothing should
/// be encoding while a race is on. So the audio goes down as plain 16-bit
/// PCM, flushed every few seconds - and WaveFileWriter.Flush rewrites the
/// RIFF lengths in the header, which is the whole trick: the file on disk is
/// a complete, playable wav at every moment, so a crash, a power cut or the
/// app being force-closed mid-race leaves a file that opens and plays up to
/// a few seconds before it went down. It is named ".recording.wav" while it
/// is being written, and the next recording sweeps any left behind and turns
/// them into mp3s (see <see cref="SweepOrphans"/>), so nothing is silently
/// lost and nothing is silently kept as a 250MB wav either.
/// </summary>
internal sealed class RaceAudioRecorder : AudioBus.IMixListener, IDisposable
{
    /// <summary>The bus's rate, because the samples come straight off it and resampling them would be work in the audio path.</summary>
    public const int SampleRate = AudioBus.MixSampleRate;

    /// <summary>Mono - see the note on this class.</summary>
    public const int Channels = 1;

    /// <summary>16-bit, the one depth every player and every encoder takes.</summary>
    public const int BytesPerSample = 2;

    /// <summary>The marker a recording in progress carries, so a file left behind by a crash can be told from a finished one.</summary>
    public const string InProgressExtension = ".recording.wav";

    /// <summary>
    /// How many converted buffers may wait for the writer.
    ///
    /// The bus hands over about ten buffers a second (WaveOutEvent at 300ms
    /// over three buffers), each a tenth of a second of mono audio, so this
    /// is roughly twelve seconds and about a megabyte. Big enough that an
    /// ordinary disk hiccup - an antivirus pass, a backup tool - costs
    /// nothing; small enough that a disk which has stopped answering can
    /// never grow this into real memory.
    /// </summary>
    private const int MaxQueued = 120;

    /// <summary>How often the header is rewritten and the disk re-checked. The most a crash can cost.</summary>
    private const int FlushEveryMs = 2000;

    /// <summary>
    /// How far behind the wall clock the file may drift before silence is
    /// put in to catch it up.
    ///
    /// The file's own clock is its sample count, and the marks laid over it
    /// are in race time, so the two have to agree. They can come apart: if
    /// the sound device goes away mid-race - a Bluetooth speaker switched
    /// off - AudioBus drops its output and opens a new one on the next
    /// sound, and for that gap no samples arrive at all. Left alone, every
    /// highlight after the gap would be cut early by however long it lasted.
    /// </summary>
    private const long DriftSlackMs = 400;

    /// <summary>The most silence one catch-up may put in, so a mistake here can never write a file full of nothing.</summary>
    private const long MaxPadMs = 30_000;

    /// <summary>Shorter than this and there is no call in it worth keeping.</summary>
    private const long MinUsefulMs = 5000;

    private readonly BlockingCollection<byte[]> _pending = new(MaxQueued);
    private readonly WaveFileWriter _wav;
    private readonly Thread _writer;
    private readonly string _wavPath;
    private readonly string _folder;
    private readonly string _baseName;
    private readonly long _maxSamples;
    private readonly long _minFreeBytes;

    /// <summary>Written by the writer thread, read by anyone - so the length is read through Interlocked, never torn.</summary>
    private long _samplesWritten;

    /// <summary>
    /// Every sample the file has taken on: queued for the writer, written,
    /// or put in as padding. Read through Interlocked like _samplesWritten.
    /// </summary>
    // WHY (20 Oct 2026): the drift check used to compare the clock with
    // _samplesWritten alone, so audio still waiting in _pending during a
    // disk or CPU stall looked missing. Silence went in, the queued audio
    // came after it, and every highlight after that was out of step. The
    // drift check now counts this instead, so only audio that never reached
    // the queue (the device gone, a dropped buffer) gets padded.
    private long _samplesAccepted;

    private long _paddedMs;
    private int _dropped;

    /// <summary>1 once nothing more is to be taken in: the cap reached, the disk low, or the race over.</summary>
    private int _closed;

    private int _stopped;
    private RaceAudioRecording? _result;
    private volatile string? _problem;

    private RaceAudioRecorder(
        WaveFileWriter wav,
        string wavPath,
        string folder,
        string baseName,
        DateTime startedUtc,
        long maxSamples,
        long minFreeBytes)
    {
        _wav = wav;
        _wavPath = wavPath;
        _folder = folder;
        _baseName = baseName;
        _maxSamples = maxSamples;
        _minFreeBytes = minFreeBytes;
        StartedUtc = startedUtc;

        _writer = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "HeatCaller race audio",

            // Below the UI and well below whatever the audio device's own
            // thread runs at. This thread only ever writes to a file; it
            // must never be the reason a lap, a call or a frame is late.
            Priority = ThreadPriority.BelowNormal,
        };
    }

    /// <summary>When this recording started, which is where its clock zeroes.</summary>
    public DateTime StartedUtc { get; }

    /// <summary>How far into the recording the green dropped, or null until the race has started.</summary>
    public long? GreenAtMs { get; private set; }

    /// <summary>What went wrong, or null while it is working. Said once by the caller.</summary>
    public string? Problem => _problem;

    /// <summary>How much audio has landed in the file, in milliseconds.</summary>
    public long LengthMs => Interlocked.Read(ref _samplesWritten) * 1000 / SampleRate;

    /// <summary>
    /// Starts recording, or answers null when it cannot or should not.
    ///
    /// Null is not a failure the caller has to handle beyond carrying on:
    /// recording off, no room on the disk, a folder that cannot be made. The
    /// reason is logged here, once.
    /// </summary>
    public static RaceAudioRecorder? StartFor(RaceAudioStore store, string? raceLabel, DateTime startedUtc, Action<string> log)
    {
        if (!store.Enabled)
        {
            return null;
        }

        try
        {
            var local = startedUtc.ToLocalTime();
            var folder = store.FolderForNight(local);

            // ONLY A FOLDER THIS MAKES IS MARKED (17 Oct 2026, Bug Hunt 8,
            // A14-1). The marker is what lets the prune delete a night
            // outside the default folder, and marking whatever was already
            // there handed it a club's own Pictures/2026-10-17, photos and
            // all. A folder that is missing or empty is ours; one that
            // already holds the marker (an earlier race tonight) keeps it.
            // A night left unmarked is only ever kept longer.
            var ours = !Directory.Exists(folder) || !Directory.EnumerateFileSystemEntries(folder).Any();

            Directory.CreateDirectory(folder);

            if (ours)
            {
                RaceAudioStore.MarkAsNight(folder);
            }

            var minFreeBytes = (long)store.MinFreeMb * 1024 * 1024;

            if (!HasRoom(folder, minFreeBytes))
            {
                log($"{AppTime.LogStampNow()}  [audio] This race is NOT being recorded: less than {store.MinFreeMb}MB "
                    + $"free where the recordings go ({folder}). The race itself is unaffected - clear some space "
                    + "before the next one if you want the audio.");
                return null;
            }

            var baseName = RaceHighlights.BaseNameFor(local, raceLabel);
            var wavPath = RaceHighlights.UnusedPath(folder, baseName, InProgressExtension);

            // A plain PCM wav, not a stream through an encoder: see the note
            // on this class for why nothing encodes while a race is on.
            var wav = new WaveFileWriter(wavPath, new WaveFormat(SampleRate, 8 * BytesPerSample, Channels));

            var maxSamples = (long)store.MaxMinutes * 60 * SampleRate;

            var recorder = new RaceAudioRecorder(wav, wavPath, folder, baseName, startedUtc, maxSamples, minFreeBytes);
            recorder._writer.Start();

            // SOMETHING WAS ALREADY RECORDING, and this takes the tap off
            // it: AudioBus holds ONE listener and Listen replaces it.
            //
            // THE RACE WINS, which is the right way round and is what
            // BookendRecording's own refusal says in the other direction -
            // a race's mp3 is the one a club cannot record again. But the
            // thing displaced ends wherever this starts, and the one
            // unforgivable version of that is the silent one: a cold open
            // or the night's recap that stops mid-sentence with nothing
            // anywhere to say why. AudioBus.Listening was added on 11 Oct
            // 2026 for exactly this and this caller never asked it
            // (12 Oct 2026).
            //
            // Said rather than refused, and said here rather than above, so
            // it only goes in the log when the takeover actually happens -
            // a race that gets no further than "no room on the disk" has
            // displaced nothing.
            if (AudioBus.Listening)
            {
                log($"{AppTime.LogStampNow()}  [audio] Something else was already recording - this race takes the "
                    + "feed, and whatever it was ends here. Its own file is still written, just short.");
            }

            // Last, so the tap can never be handed a buffer before the
            // writer thread that drains the queue is running.
            AudioBus.Listen(recorder);

            log($"{AppTime.LogStampNow()}  [audio] Recording this race to {wavPath} - it becomes an mp3, with a clip "
                + "for each driver, once the broadcast ends.");

            return recorder;
        }
        catch (Exception ex)
        {
            // Never the thing that stops a race starting.
            log($"{AppTime.LogStampNow()}  [audio] Could not start recording this race ({ex.Message}). The race is "
                + "running as normal.");
            return null;
        }
    }

    /// <summary>
    /// Notes where the green is, once. The caller has it on the UI thread
    /// the moment the race starts; nothing else needs to know.
    ///
    /// Worked out from the wall clock rather than from the file's length,
    /// because the two can differ by a buffer or two and this is the one
    /// number every highlight is measured from. Negative when recording
    /// began after the green - a race restored after a crash, or already
    /// running when Race Control was opened - so a mark's race time still
    /// lands on the right part of the file (RaceHighlights.WindowFor clamps
    /// to the file).
    /// </summary>
    public void NoteGreen(DateTime greenUtc)
    {
        if (GreenAtMs.HasValue)
        {
            return;
        }

        // Not clamped at zero any more (17 Oct 2026, Bug Hunt 8, A14-3): a
        // zero here put every clip of a race recorded late by however long
        // it had already run.
        GreenAtMs = (long)(greenUtc - StartedUtc).TotalMilliseconds;
    }

    /// <summary>
    /// The sound device's own thread, with the finished mix. Everything here
    /// is a copy, a loop and a queue - no disk, no lock, no allocation worth
    /// the name, and no exception that can reach the device.
    /// </summary>
    public void Mixed(float[] buffer, int offset, int count)
    {
        if (Volatile.Read(ref _closed) == 1)
        {
            return;
        }

        try
        {
            var frames = count / AudioBus.MixChannels;

            if (frames <= 0)
            {
                return;
            }

            var bytes = new byte[frames * BytesPerSample];
            var at = 0;

            for (var frame = 0; frame < frames; frame++)
            {
                var left = buffer[offset + (frame * AudioBus.MixChannels)];
                var right = buffer[offset + (frame * AudioBus.MixChannels) + 1];

                // The average of the two, not their sum: the bus's own soft
                // limiter has already kept each channel inside 1.0, and
                // adding them would put a centred voice at 2.0 and clip it
                // on the way into 16 bits.
                var mono = (left + right) * 0.5f;

                if (float.IsNaN(mono))
                {
                    mono = 0f;
                }

                var sample = (short)Math.Clamp((int)MathF.Round(mono * short.MaxValue), short.MinValue, short.MaxValue);

                bytes[at++] = (byte)(sample & 0xFF);
                bytes[at++] = (byte)((sample >> 8) & 0xFF);
            }

            // WHY (20 Oct 2026): counted before TryAdd, so the writer can
            // never write this chunk before it is counted, and taken back off
            // if the queue refuses it - a dropped chunk is a real gap and the
            // drift padding must still fill it. The finally also takes it
            // back off when TryAdd throws (the queue closed by Latch or
            // BeginStop a moment after the _closed check above), so a chunk
            // that never reached the queue can never hide a real gap.
            Interlocked.Add(ref _samplesAccepted, frames);
            var queued = false;

            try
            {
                queued = _pending.TryAdd(bytes);
            }
            finally
            {
                if (!queued)
                {
                    Interlocked.Add(ref _samplesAccepted, -frames);
                }
            }

            if (!queued)
            {
                // The writer is behind - see MaxQueued. A gap in the file is
                // the price; a stalled race night is not.
                Interlocked.Increment(ref _dropped);
            }
        }
        catch
        {
            // This is the device's thread. Nothing from here may escape:
            // AudioBus would have to drop the tap, and every sound after it
            // would be at risk for the sake of a recording.
        }
    }

    private void WriteLoop()
    {
        var lastFlush = Environment.TickCount64;

        try
        {
            foreach (var chunk in _pending.GetConsumingEnumerable())
            {
                _wav.Write(chunk, 0, chunk.Length);
                Interlocked.Add(ref _samplesWritten, chunk.Length / BytesPerSample);

                if (Environment.TickCount64 - lastFlush < FlushEveryMs)
                {
                    continue;
                }

                lastFlush = Environment.TickCount64;
                FlushAndCheck();
            }
        }
        catch (Exception ex)
        {
            // A disk that has gone away, a file somebody deleted underneath
            // this, the queue disposed from under the enumerator. The
            // recording stops; the race does not notice.
            Latch($"writing stopped ({ex.Message})");
        }
    }

    /// <summary>
    /// Rewrites the header so the file on disk is playable as it stands,
    /// catches the timeline up if the device went away, and gives up the
    /// recording if the cap or the disk says so.
    /// </summary>
    private void FlushAndCheck()
    {
        PadForDrift();

        _wav.Flush();

        if (Interlocked.Read(ref _samplesWritten) >= _maxSamples)
        {
            Latch($"the {_maxSamples / 60 / SampleRate}-minute limit was reached");
            return;
        }

        if (!HasRoom(_folder, _minFreeBytes))
        {
            Latch("the disk was running out of room");
        }
    }

    /// <summary>
    /// Puts silence in when the file has fallen behind the wall clock - see
    /// <see cref="DriftSlackMs"/>. Only once some real audio has arrived: a
    /// PC with no sound device at all never feeds the tap, and that must
    /// give an empty recording, not an hour of silence.
    /// </summary>
    private void PadForDrift()
    {
        var written = Interlocked.Read(ref _samplesWritten);

        if (written <= 0 || _paddedMs >= MaxPadMs)
        {
            return;
        }

        // WHY (20 Oct 2026): measured against what the file has accepted
        // (queued as well as written), not only what is written - a writer
        // backlog is late audio, not missing audio. See _samplesAccepted.
        var accepted = Math.Max(written, Interlocked.Read(ref _samplesAccepted));
        var elapsedMs = (long)(DateTime.UtcNow - StartedUtc).TotalMilliseconds;
        var gap = SilenceSamplesNeeded(elapsedMs, accepted, SampleRate, DriftSlackMs);

        if (gap <= 0)
        {
            return;
        }

        var allowed = (MaxPadMs - _paddedMs) * SampleRate / 1000;
        gap = Math.Min(gap, allowed);

        var silence = new byte[Math.Min(gap, SampleRate) * BytesPerSample];

        while (gap > 0)
        {
            var now = (int)Math.Min(gap, silence.Length / BytesPerSample);
            _wav.Write(silence, 0, now * BytesPerSample);
            Interlocked.Add(ref _samplesWritten, now);
            Interlocked.Add(ref _samplesAccepted, now);
            gap -= now;
            _paddedMs += now * 1000L / SampleRate;
        }
    }

    /// <summary>
    /// How many samples of silence the file needs to catch up with the wall
    /// clock, or zero while it is within <paramref name="slackMs"/>.
    /// Its own method because this is the arithmetic the highlights stand
    /// on, and arithmetic can be tested where a sound device cannot.
    /// </summary>
    public static long SilenceSamplesNeeded(long elapsedMs, long samplesWritten, int sampleRate, long slackMs)
    {
        if (elapsedMs <= 0 || sampleRate <= 0)
        {
            return 0;
        }

        var expected = elapsedMs * sampleRate / 1000;
        var behind = expected - samplesWritten;
        var slack = Math.Max(0, slackMs) * sampleRate / 1000;

        return behind > slack ? behind : 0;
    }

    /// <summary>Whether the disk holding <paramref name="folder"/> has room to spare. A disk that cannot be asked is taken as having room - a recording must not be refused because Windows would not answer.</summary>
    public static bool HasRoom(string folder, long minFreeBytes)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder));

            if (string.IsNullOrEmpty(root))
            {
                return true;
            }

            return new DriveInfo(root).AvailableFreeSpace >= minFreeBytes;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Stops taking audio in and remembers why. The file keeps whatever it already has.</summary>
    private void Latch(string reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1)
        {
            return;
        }

        _problem = reason;

        try
        {
            AudioBus.StopListening(this);
        }
        catch
        {
            // Nothing left to do about it.
        }

        try
        {
            _pending.CompleteAdding();
        }
        catch
        {
            // Already completed, or disposed.
        }
    }

    /// <summary>
    /// Takes the tap off the bus at once, without waiting for anything.
    ///
    /// Split from <see cref="Stop"/> because Stop waits for the writer
    /// thread to drain, and the caller is on the UI thread between races.
    /// This part is instant and is what must happen immediately; the waiting
    /// is done on a background thread.
    /// </summary>
    public void BeginStop()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1)
        {
            return;
        }

        try
        {
            AudioBus.StopListening(this);
        }
        catch
        {
            // Nothing left to do about it.
        }

        try
        {
            _pending.CompleteAdding();
        }
        catch
        {
            // Already completed.
        }
    }

    /// <summary>
    /// Drains what is queued, closes the file and says what is on disk.
    /// Safe to call twice - the second call gives the same answer. Not for
    /// the UI thread: it waits.
    /// </summary>
    public RaceAudioRecording Stop()
    {
        BeginStop();

        if (Interlocked.Exchange(ref _stopped, 1) == 1)
        {
            return _result ?? Describe();
        }

        try
        {
            // Generous, and bounded. The queue holds about twelve seconds of
            // audio and about a megabyte, so a working disk drains it in
            // milliseconds; this is only here so a disk that has stopped
            // answering cannot hold a thread for the rest of the night.
            if (_writer.IsAlive && !_writer.Join(TimeSpan.FromSeconds(15)))
            {
                _problem ??= "the last few seconds could not be written";
            }
        }
        catch
        {
            // A thread that never started.
        }

        try
        {
            _wav.Flush();
        }
        catch (Exception ex)
        {
            _problem ??= $"the recording could not be finished off ({ex.Message})";
        }

        try
        {
            _wav.Dispose();
        }
        catch
        {
            // The header was already flushed, so the file is playable even
            // if closing it complained.
        }

        try
        {
            _pending.Dispose();
        }
        catch
        {
            // Nothing left to do about it.
        }

        _result = Describe();
        return _result;
    }

    private RaceAudioRecording Describe()
    {
        var dropped = Volatile.Read(ref _dropped);

        return new RaceAudioRecording(
            _wavPath,
            _folder,
            _baseName,
            StartedUtc.ToLocalTime(),
            GreenAtMs ?? 0,
            LengthMs,
            dropped,
            _problem);
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Everything after the flag: the race's mp3, a clip for each driver,
    /// and the wav tidied away. Runs on a background thread, after the
    /// broadcast has ended.
    /// </summary>
    /// <param name="record">The result as it was filed, for the drivers' names and finishing positions. Null when the race was never saved - then only the race's own mp3 is written.</param>
    /// <param name="releaseOffsetsMs">Each driver's release offset off the board at the flag - zero in an ordinary race. See RaceHighlights.OffsetOf.</param>
    public static void Publish(
        RaceAudioRecording recording,
        RaceAudioStore store,
        SessionRecord? record,
        IReadOnlyDictionary<string, long>? releaseOffsetsMs,
        Action<string> log)
    {
        try
        {
            if (recording.DroppedChunks > 0)
            {
                log($"{AppTime.LogStampNow()}  [audio] The recording of \"{recording.BaseName}\" has gaps in it: "
                    + $"{Plural.Of(recording.DroppedChunks, "buffer")} could not be written in time. The usual cause "
                    + "is something else on this PC hammering the disk - a backup or a sync tool.");
            }

            if (recording.Problem is { } problem)
            {
                log($"{AppTime.LogStampNow()}  [audio] The recording of \"{recording.BaseName}\" stopped early: "
                    + $"{problem}. What was recorded up to that point is kept.");
            }

            if (recording.LengthMs < MinUsefulMs)
            {
                log($"{AppTime.LogStampNow()}  [audio] Nothing worth keeping was recorded for "
                    + $"\"{recording.BaseName}\" ({recording.LengthMs}ms). "
                    + (recording.LengthMs == 0
                        ? "No sound came out of HeatCaller at all - check the speakers and Sounds > Mixer."
                        : "The file has been removed."));
                TryDelete(recording.WavPath, log);
                SweepOrphans(store, recording.WavPath, log);

                // A new night's folder may have been made for it all the
                // same, so the ceiling still applies.
                store.PruneOldNights(log);
                return;
            }

            // The race's own name as it was finally filed, which is the one
            // the club will look for in Race History. The wav was named at
            // the green, from whatever was typed in the race-name box, which
            // is before the round has been picked or guessed.
            var baseName = record is not null && !string.IsNullOrWhiteSpace(record.Label)
                ? RaceHighlights.BaseNameFor(recording.StartedLocal, record.Label)
                : recording.BaseName;

            var mp3Path = RaceHighlights.UnusedPath(recording.Folder, baseName, ".mp3");

            // The race's mp3 first, and the clips after: it is the thing
            // asked for, and if anything goes wrong from here it is already
            // on disk.
            if (TryEncode(recording.WavPath, mp3Path, out var why))
            {
                log($"{AppTime.LogStampNow()}  [audio] Saved {mp3Path} ({recording.LengthMs / 60000}m"
                    + $"{recording.LengthMs / 1000 % 60:00}s).");
            }
            else
            {
                // Renamed off the in-progress name so the sweep leaves it
                // alone from now on: an encoder that has refused this file
                // once will refuse it every race night, and a club does not
                // need to be told so twenty times an evening.
                var kept = KeepAsPlainWav(recording.WavPath, log);

                log($"{AppTime.LogStampNow()}  [audio] Could not make an mp3 of \"{baseName}\" ({why}). The "
                    + $"recording is kept as a wav instead and plays as it is: {kept}");

                // THE CLIPS STILL GET CUT (17 Oct 2026, Bug Hunt 8, A14-4).
                // WriteClips keeps a clip the encoder refuses as a wav, and
                // DriversOwnClip plays wav clips for exactly this PC - but
                // this branch returned before any were cut. From the kept
                // wav, which is the renamed file or the original if the
                // rename failed; either reads the same.
                if (store.DriverHighlights && record is not null)
                {
                    WriteClips(recording with { WavPath = kept }, record, releaseOffsetsMs, baseName, log);
                }

                // The ceiling matters most here: a PC with no mp3 encoder
                // fails like this every race, with five times the file.
                store.PruneOldNights(log);
                return;
            }

            if (store.DriverHighlights && record is not null)
            {
                WriteClips(recording, record, releaseOffsetsMs, baseName, log);
            }

            // Only now, and only because the mp3 landed.
            TryDelete(recording.WavPath, log);

            SweepOrphans(store, recording.WavPath, log);

            // AND THE CEILING (11 Oct 2026). Nothing ever deleted any of
            // this: a club running a season would have put twenty
            // gigabytes of broadcast into Documents without being told it
            // was happening. Here because this is the only moment the
            // folder definitely grew, and because nothing is waiting on
            // this thread. See RaceAudioStore.PruneOldNights.
            store.PruneOldNights(log);
        }
        catch (Exception ex)
        {
            // The race is long since filed. Nothing here is worth more than
            // a line in the log.
            log($"{AppTime.LogStampNow()}  [audio] Finishing the recording of \"{recording.BaseName}\" failed "
                + $"({ex.Message}). Anything already written is where it was.");
        }
    }

    /// <summary>One mp3 per driver, holding the stretches of the recording where they were called.</summary>
    private static void WriteClips(
        RaceAudioRecording recording,
        SessionRecord record,
        IReadOnlyDictionary<string, long>? releaseOffsetsMs,
        string baseName,
        Action<string> log)
    {
        var marks = RaceHighlights.MarksFrom(record, releaseOffsetsMs);
        var clips = RaceHighlights.ClipsFor(record, marks, recording.GreenAtMs, recording.LengthMs);

        if (clips.Count == 0)
        {
            return;
        }

        var folder = Path.Combine(recording.Folder, "Highlights");
        Directory.CreateDirectory(folder);

        var written = 0;

        // One line for every refused clip, not one per driver (17 Oct
        // 2026, Bug Hunt 8, A14-4): on a PC whose encoder refuses the race
        // it refuses every clip too, now that those clips get cut.
        var keptAsWav = new List<string>();
        string? refusedWhy = null;

        foreach (var clip in clips)
        {
            var name = RaceHighlights.ClipNameFor(baseName, clip.Position, clip.DriverName);

            // A plain ".wav", NOT the in-progress name: a clip the encoder
            // refused is left where it is and plays as a wav, and the sweep
            // must not pick it up every race night afterwards and try the
            // encoder it has already refused.
            var temp = RaceHighlights.UnusedPath(folder, name, ".wav");

            try
            {
                CutWav(recording.WavPath, temp, clip.Windows);

                var clipPath = RaceHighlights.UnusedPath(folder, name, ".mp3");

                if (TryEncode(temp, clipPath, out var why))
                {
                    written++;
                }
                else
                {
                    keptAsWav.Add(clip.DriverName);
                    refusedWhy ??= why;
                    continue;
                }
            }
            catch (Exception ex)
            {
                // One driver's clip failing must not cost the other eleven
                // theirs.
                log($"{AppTime.LogStampNow()}  [audio] Could not cut {clip.DriverName}'s clip ({ex.Message}).");
            }

            TryDelete(temp, log);
        }

        if (written > 0)
        {
            log($"{AppTime.LogStampNow()}  [audio] {Plural.Of(written, "driver clip")} saved in {folder}.");
        }

        if (keptAsWav.Count > 0)
        {
            log($"{AppTime.LogStampNow()}  [audio] Could not make {Plural.Of(keptAsWav.Count, "driver clip")} an mp3 "
                + $"({refusedWhy}) - kept as wavs in {folder}: {string.Join(", ", keptAsWav)}.");
        }
    }

    /// <summary>
    /// Copies the given stretches of one wav into a new one, in order, as
    /// raw samples.
    ///
    /// Byte copying rather than anything clever: the source is plain 16-bit
    /// PCM that this class wrote itself, so a stretch is a byte range, and a
    /// byte range is something that cannot go subtly wrong at four in the
    /// afternoon on a club's laptop. The offsets come from
    /// RaceHighlights.ByteOffsetFor, which keeps them on whole samples.
    /// </summary>
    private static void CutWav(string sourcePath, string targetPath, IReadOnlyList<RaceHighlights.Window> windows)
    {
        using var reader = new WaveFileReader(sourcePath);
        using var writer = new WaveFileWriter(targetPath, reader.WaveFormat);

        var bytesPerSample = reader.WaveFormat.BitsPerSample / 8 * reader.WaveFormat.Channels;
        var buffer = new byte[64 * 1024];

        // A breath between two stretches, so two calls cut from different
        // parts of the race do not run into each other as one sentence.
        var seam = new byte[reader.WaveFormat.AverageBytesPerSecond / 2];
        var first = true;

        foreach (var window in windows)
        {
            var from = RaceHighlights.ByteOffsetFor(window.StartMs, reader.WaveFormat.SampleRate, bytesPerSample);
            var to = RaceHighlights.ByteOffsetFor(window.EndMs, reader.WaveFormat.SampleRate, bytesPerSample);

            from = Math.Clamp(from, 0, reader.Length);
            to = Math.Clamp(to, from, reader.Length);

            if (to <= from)
            {
                continue;
            }

            if (!first)
            {
                writer.Write(seam, 0, seam.Length);
            }

            first = false;
            reader.Position = from;

            var left = to - from;

            while (left > 0)
            {
                var read = reader.Read(buffer, 0, (int)Math.Min(left, buffer.Length));

                if (read <= 0)
                {
                    break;
                }

                writer.Write(buffer, 0, read);
                left -= read;
            }
        }

        writer.Flush();
    }

    /// <summary>
    /// A wav to an mp3, through Media Foundation - which ships with Windows
    /// and with NAudio, so this adds no dependency.
    ///
    /// Three tries at a bit rate and then one as stereo, because
    /// MediaFoundationEncoder only takes an output format the installed
    /// encoder actually offers at exactly this rate and channel count, and
    /// "mono 44.1kHz mp3" is not offered on every Windows build. The stereo
    /// pass doubles a speech file's size and is the last resort rather than
    /// the first for that reason. False means the wav stays, and a wav plays.
    /// </summary>
    private static bool TryEncode(string wavPath, string mp3Path, out string why)
    {
        why = string.Empty;

        try
        {
            // EncodeToMp3 starts Media Foundation itself; doing it here as
            // well is free (it counts references) and means a machine where
            // Media Foundation is missing altogether says so plainly.
            MediaFoundationApi.Startup();
        }
        catch (Exception ex)
        {
            why = $"Windows Media Foundation is not available ({ex.Message})";
            return false;
        }

        foreach (var bitRate in new[] { 96_000, 128_000, 64_000 })
        {
            try
            {
                using var reader = new WaveFileReader(wavPath);
                MediaFoundationEncoder.EncodeToMp3(reader, mp3Path, bitRate);
                return true;
            }
            catch (Exception ex)
            {
                why = ex.Message;
                TryDelete(mp3Path, null);
            }
        }

        try
        {
            using var reader = new WaveFileReader(wavPath);
            var stereo = new MonoToStereoSampleProvider(new Pcm16BitToSampleProvider(reader));
            MediaFoundationEncoder.EncodeToMp3(new SampleToWaveProvider16(stereo), mp3Path, 128_000);
            return true;
        }
        catch (Exception ex)
        {
            why = ex.Message;
            TryDelete(mp3Path, null);
        }

        return false;
    }

    /// <summary>
    /// Turns any recording left behind by a crash or a close into an mp3.
    ///
    /// A file still called ".recording.wav" is one nothing finished: the app
    /// went down mid-race, or was closed with a race on. It is playable as
    /// it stands - the header is rewritten every couple of seconds - so
    /// nothing has been lost; this is what makes it an mp3 like the rest, so
    /// a club is never left with a quarter-gigabyte wav it has to know
    /// about. No highlights for one: the race it belongs to was never
    /// filed, so there are no names, no positions and no lap times to cut
    /// against.
    ///
    /// Run after a race has finished and published, never during one, and
    /// only over nights that are not tonight's own live file.
    /// </summary>
    private static void SweepOrphans(RaceAudioStore store, string? skipPath, Action<string> log)
    {
        try
        {
            var root = store.Folder;

            if (!Directory.Exists(root))
            {
                return;
            }

            foreach (var orphan in Directory.EnumerateFiles(root, "*" + InProgressExtension, SearchOption.AllDirectories))
            {
                // EnumerateFiles' pattern is Windows', which is looser than
                // it looks with several dots in it, so the name is checked
                // again rather than trusted.
                if (!orphan.EndsWith(InProgressExtension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (skipPath is not null
                    && string.Equals(Path.GetFullPath(orphan), Path.GetFullPath(skipPath), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Written in the last couple of minutes: something still has
                // it. Nothing should, but encoding a file another recording
                // is writing would make a mess of both, so it waits for the
                // next sweep.
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(orphan) < TimeSpan.FromMinutes(2))
                {
                    continue;
                }

                var name = Path.GetFileName(orphan);
                name = name[..(name.Length - InProgressExtension.Length)];

                var folder = Path.GetDirectoryName(orphan) ?? root;
                var mp3Path = RaceHighlights.UnusedPath(folder, name, ".mp3");

                if (TryEncode(orphan, mp3Path, out var why))
                {
                    log($"{AppTime.LogStampNow()}  [audio] A recording left behind by an earlier close has been "
                        + $"saved as {mp3Path}.");
                    TryDelete(orphan, log);
                }
                else
                {
                    // Off the in-progress name, so it is not tried again
                    // every race for the rest of the season.
                    var kept = KeepAsPlainWav(orphan, log);

                    log($"{AppTime.LogStampNow()}  [audio] A recording left behind by an earlier close could not be "
                        + $"made into an mp3 ({why}). It is still there and still plays: {kept}");
                }
            }
        }
        catch (Exception ex)
        {
            log($"{AppTime.LogStampNow()}  [audio] Could not tidy up earlier recordings ({ex.Message}).");
        }
    }

    /// <summary>
    /// Takes the ".recording.wav" marker off a file that is going to stay a
    /// wav, so the sweep stops offering it to an encoder that has already
    /// refused it. Answers wherever the file ended up - the name it had, if
    /// the rename could not be made.
    /// </summary>
    private static string KeepAsPlainWav(string path, Action<string> log)
    {
        try
        {
            if (!path.EndsWith(InProgressExtension, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            var folder = Path.GetDirectoryName(path) ?? string.Empty;
            var name = Path.GetFileName(path);
            name = name[..(name.Length - InProgressExtension.Length)];

            var plain = RaceHighlights.UnusedPath(folder, name, ".wav");
            File.Move(path, plain);
            return plain;
        }
        catch (Exception ex)
        {
            log($"{AppTime.LogStampNow()}  [audio] Could not rename {path} ({ex.Message}).");
            return path;
        }
    }

    private static void TryDelete(string path, Action<string>? log)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            // A file left behind is untidy, not a problem - and saying so
            // once is better than a dialog about it.
            log?.Invoke($"{AppTime.LogStampNow()}  [audio] Could not remove {path} ({ex.Message}).");
        }
    }
}
