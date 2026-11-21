using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// Audio output: the one line playing at a time, silence before and after a
/// line, decoding MP3 and WAV, and playing PCM.
///
/// Moved here unchanged from AnnouncementService.cs on 26 Sep 2026, when the
/// file was split into parts. Fields with initializers stayed there.
/// </summary>
public sealed partial class AnnouncementService
{
    /// <summary>
    /// The line currently playing, so a flag call can stop it.
    ///
    /// Static alongside PlayAsync itself, which every path here goes
    /// through. Cancelling takes the line out of the shared mix (see
    /// AudioBus) and completes the awaiting task exactly as a natural end
    /// would.
    /// </summary>
    private static CancellationTokenSource? _activeLine;
    private static readonly object ActiveOutputLock = new();

    /// <summary>Whether a spoken line is coming out of the speaker now - for a lap chime, which plays under it (see PlaySound).</summary>
    private static bool VoiceLineIsPlaying
    {
        get
        {
            lock (ActiveOutputLock)
            {
                return _activeLine is not null;
            }
        }
    }

    /// <summary>
    /// How loud a lap chime is under a spoken line, as a share of its own
    /// level (10 Nov 2026). Enough to mark the lap, not enough to cover the
    /// name being said - the chime is the room's tick, the voice its news.
    /// </summary>
    internal const float ChimeUnderVoiceGain = 0.55f;

    /// <summary>Stops whatever is playing. Returns true if there was something to stop.</summary>
    public static bool StopCurrentPlayback()
    {
        CancellationTokenSource? line;

        lock (ActiveOutputLock)
        {
            line = _activeLine;
        }

        if (line is null)
        {
            return false;
        }

        try
        {
            line.Cancel();
            return true;
        }
        catch
        {
            // Already finished and disposed - nothing to cut off.
            return false;
        }
    }

    /// <summary>When the playing line's words began (TimingClock), past its lead-in silence. Under ActiveOutputLock.</summary>
    private static long _activeLineWordsAtMs;

    /// <summary>How much of a cut-off line's words are heard at least - see StopCurrentPlaybackAfterABeat.</summary>
    internal const long CutLineHeardAtLeastMs = 1_800;

    /// <summary>And the beat left before the cut, however much has been heard.</summary>
    internal const long CutBeatMs = 500;

    /// <summary>
    /// How long to wait before cutting a line whose words began at
    /// <paramref name="wordsAtMs"/>, at <paramref name="nowMs"/>: until
    /// CutLineHeardAtLeastMs of its words have been heard, and never less
    /// than CutBeatMs.
    /// </summary>
    internal static long CutDelayMs(long wordsAtMs, long nowMs) =>
        Math.Max(CutBeatMs, wordsAtMs + CutLineHeardAtLeastMs - nowMs);

    /// <summary>
    /// Stops the line playing now, but after a beat (8 Nov 2026, Asher: "the
    /// first call would stop playing so quick you don't know what was about
    /// to be said. it should wait a beat or so for some info to drop before
    /// the correction cuts it off"). A battle line overtaken by a pass, or a
    /// ruling, was cut the instant the board changed - often in its first
    /// word, so the room heard a syllable and then the pass call. Now at
    /// least CutLineHeardAtLeastMs of its words go out, and a beat more
    /// whatever has been heard, and then the cut. Only that line: one that
    /// ends by itself first is not followed by the stop, and nor is the
    /// next one. The pass call queued behind it plays straight after.
    /// Returns true when there was a line to cut.
    /// </summary>
    /// <param name="orTheLineAboutToPlay">
    /// When nothing is registered as playing yet, cut the next line that
    /// registers instead, after the same beat (21 Nov 2026). For a position
    /// report, which is put on air a moment before its audio is registered
    /// (both under the playback lock, so the next line to register IS the
    /// report): a change of order in that moment used to find nothing to
    /// stop, and the report played in full with its correction after it.
    /// Forgotten when the report goes off air (ForgetPendingCut), so it can
    /// never reach another line.
    /// </param>
    public static bool StopCurrentPlaybackAfterABeat(bool orTheLineAboutToPlay = false)
    {
        CancellationTokenSource? line;
        long wordsAt;

        lock (ActiveOutputLock)
        {
            line = _activeLine;
            wordsAt = _activeLineWordsAtMs;

            if (line is null && orTheLineAboutToPlay)
            {
                _cutTheLineAboutToPlay = true;
            }
        }

        if (line is null)
        {
            return false;
        }

        CutAfterABeat(line, wordsAt);
        return true;
    }

    /// <summary>A cut asked for before the line it was meant for registered - see StopCurrentPlaybackAfterABeat. Under ActiveOutputLock.</summary>
    private static bool _cutTheLineAboutToPlay;

    /// <summary>
    /// The playing line's words begin <paramref name="leadMs"/> plus the
    /// device's buffer after now - the moment the output first pulled it.
    /// Only for the line still registered, and only ever later than the
    /// estimate made at registration. Any thread (the audio thread).
    /// </summary>
    private static void NoteWordsStartFromFirstSample(CancellationTokenSource line, long leadMs)
    {
        var wordsAt = TimingClock.NowMs + leadMs + VoiceLineTiming.DeviceAheadMs;

        lock (ActiveOutputLock)
        {
            if (ReferenceEquals(_activeLine, line) && wordsAt > _activeLineWordsAtMs)
            {
                _activeLineWordsAtMs = wordsAt;
            }
        }
    }

    /// <summary>Forgets a cut still waiting for its line - the position report it was for has gone off air.</summary>
    internal static void ForgetPendingCut()
    {
        lock (ActiveOutputLock)
        {
            _cutTheLineAboutToPlay = false;
        }
    }

    /// <summary>Cuts <paramref name="line"/> once enough of its words have been heard - see StopCurrentPlaybackAfterABeat.</summary>
    private static void CutAfterABeat(CancellationTokenSource line, long wordsAt) =>
        CutAfter(line, CutDelayMs(wordsAt, TimingClock.NowMs));

    /// <summary>
    /// Cuts <paramref name="line"/> after <paramref name="delayMs"/> - or
    /// later, when its words turned out to start later than thought (the
    /// output first pulled it after the cut was asked for: see
    /// NoteWordsStartFromFirstSample), so it is still heard for
    /// CutLineHeardAtLeastMs.
    /// </summary>
    private static void CutAfter(CancellationTokenSource line, long delayMs)
    {
        _ = Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, delayMs))).ContinueWith(
            _ =>
            {
                long stillToHear;

                lock (ActiveOutputLock)
                {
                    // Ended by itself meanwhile - nothing of ours to stop.
                    if (!ReferenceEquals(_activeLine, line))
                    {
                        return;
                    }

                    stillToHear = _activeLineWordsAtMs + CutLineHeardAtLeastMs - TimingClock.NowMs;
                }

                if (stillToHear > 50)
                {
                    CutAfter(line, stillToHear);
                    return;
                }

                try
                {
                    line.Cancel();
                }
                catch
                {
                    // Finished and disposed in between.
                }
            },
            TaskScheduler.Default);
    }

    /// <summary>
    /// The silence wrapped around every line. The LEADING pad is the fix for
    /// the device-startup clipping bug and is not safe to shorten. The
    /// TRAILING pad only stops the tail being cut when the device stops, so
    /// a caller that knows another line follows immediately can ask for less
    /// of it - 0.9s of trailing silence plus 0.9s of leading silence is
    /// nearly two seconds of dead air between two lines meant to run
    /// back to back.
    /// </summary>
    public const double DefaultLeadingSilenceSeconds = 0.9;
    // TIGHTENED BY 0.3s (18 Oct 2026, asked for from the floor: "bring
    // everything .3 closer together") - see BroadcastImaging.Tighten.
    public const double DefaultTrailingSilenceSeconds = 0.6;

    /// <summary>
    /// The leading pad for an output that doesn't sleep - a wired speaker,
    /// or audio going into OBS.
    ///
    /// The 0.9s above is a WAKE-UP allowance: a Bluetooth speaker powers its
    /// amplifier down between sounds and swallows the first fraction of a
    /// second of whatever plays next. An output that stays awake needs only
    /// enough to clear the device's own startup. This comes off the front of
    /// EVERY line, so on a wired setup it is three quarters of a second back
    /// on every single announcement.
    /// </summary>
    public const double WiredLeadingSilenceSeconds = 0.15;

    /// <summary>
    /// The leading pad the given output needs.
    ///
    /// The short one either way now. Every line plays through the shared
    /// output (see AudioBus), which never stops between sounds, so a
    /// wireless speaker is never asleep when a line arrives and there is
    /// nothing for the long pad to cover - it only put three quarters of a
    /// second of silence in front of every announcement.
    /// </summary>
    public static double LeadingSilenceFor(bool wirelessSpeaker) => WiredLeadingSilenceSeconds;

    /// <summary>The leading pad for whatever the saved profile says is plugged in.</summary>
    private double CurrentLeadingSilence => LeadingSilenceFor(_voiceStore.Current.WirelessSpeaker);

    /// <summary>Enough to stop the tail clipping, short enough not to be heard as a pause.</summary>
    public const double BackToBackTrailingSilenceSeconds = 0.25;

    /// <summary>
    /// For a long line - the race recap especially - where the last words
    /// were being clipped. A long line is a long single buffer chain, and
    /// the device has more of it still queued when the reader runs dry, so
    /// it needs more slack behind it than a one-sentence call does.
    /// </summary>
    public const double LongLineTrailingSilenceSeconds = 1.3;

    /// <summary>How much tail silence a line gets.</summary>
    public enum SpeechTail
    {
        /// <summary>The standard 0.9s.</summary>
        Normal,

        /// <summary>Trimmed, because another line follows immediately.</summary>
        BackToBack,

        /// <summary>Extended, for a long line whose ending was being clipped.</summary>
        Long,
    }

    private static double TailSecondsFor(SpeechTail tail) => tail switch
    {
        SpeechTail.BackToBack => BackToBackTrailingSilenceSeconds,
        SpeechTail.Long => LongLineTrailingSilenceSeconds,
        _ => DefaultTrailingSilenceSeconds,
    };

    /// <summary>
    /// How much silence a line gets in FRONT of it.
    ///
    /// The other end of <see cref="SpeechTail"/>, added 11 Oct 2026 for the
    /// same complaint: the gap when the second voice came in was killing a
    /// two-hander. Both pads were being charged in full between two turns
    /// of one exchange - 0.9s behind the first and 0.15s in front of the
    /// second - and on top of them sat the deliberate beat at the change of
    /// voice. A measured 1.5s of dead air inside what is meant to sound
    /// like two people talking.
    /// </summary>
    public enum SpeechLead
    {
        /// <summary>The standard pad - enough to clear the output device's own startup.</summary>
        Normal,

        /// <summary>
        /// None, because this line follows another immediately.
        ///
        /// The pad exists to cover the output device starting up, and
        /// <see cref="LeadingSilenceFor"/> already records that every line
        /// now goes through the shared output, which never stops between
        /// sounds. A line queued straight behind another therefore has
        /// nothing for it to cover: the device is already running and the
        /// line before it has just finished through the same mixer.
        /// </summary>
        FollowsOn,
    }

    /// <summary>
    /// How much lead silence a line gets. An instance member, unlike
    /// <see cref="TailSecondsFor"/>, because the standard pad is read from
    /// the saved profile - see <see cref="CurrentLeadingSilence"/>.
    /// </summary>
    private double LeadSecondsFor(SpeechLead lead) =>
        lead == SpeechLead.FollowsOn ? 0.0 : CurrentLeadingSilence;

    /// <summary>
    /// The MOST of the decoded start that is thrown away. Less goes when the
    /// words begin sooner - see <see cref="QuietStartToSkip"/> - so anything
    /// cutting the audio by the API's timings takes the amount actually
    /// skipped from <see cref="DecodeMp3Trimmed"/>, not this.
    /// </summary>
    public const double DecodeSkipSeconds = 0.08;

    /// <summary>
    /// How loud a 16-bit sample has to be to count as the words starting,
    /// for <see cref="QuietStartToSkip"/> - about -30 dBFS. The decoder's
    /// own run-in is digital silence and a breath before a line peaks in the
    /// low hundreds; the first consonant of a word is well over this.
    /// </summary>
    public const int SpeechOnsetLevel = 1000;

    /// <summary>
    /// How much is kept in front of the first sample that loud: the very
    /// start of a consonant ("Q", "K") builds up below the level for a few
    /// milliseconds, and cutting on the loud sample itself would still clip it.
    /// </summary>
    public const double SpeechOnsetGuardSeconds = 0.02;

    /// <summary>
    /// Decodes an MP3 to raw PCM.
    ///
    /// Split out of PlayAsync so one render covering several lines can be
    /// decoded once and cut into pieces, each played on its own.
    /// </summary>
    public static (byte[] Pcm, WaveFormat Format) DecodeMp3(byte[] mp3Bytes)
    {
        var (pcm, format, _) = DecodeMp3Trimmed(mp3Bytes);
        return (pcm, format);
    }

    /// <summary>
    /// <see cref="DecodeMp3"/>, with how much of the start it threw away -
    /// for a caller lining the audio up against timings the API reported
    /// for the untrimmed render (the batched release calls).
    /// </summary>
    public static (byte[] Pcm, WaveFormat Format, double SkippedSeconds) DecodeMp3Trimmed(byte[] mp3Bytes)
    {
        using var mp3Stream = new MemoryStream(mp3Bytes);
        using var mp3Reader = new Mp3FileReader(mp3Stream);
        var format = mp3Reader.WaveFormat;

        using var pcm = new MemoryStream();
        var buffer = new byte[format.AverageBytesPerSecond];
        int bytesRead;

        while ((bytesRead = mp3Reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            pcm.Write(buffer, 0, bytesRead);
        }

        var whole = pcm.ToArray();

        // Many MP3 encoders prepend a LAME/Xing "header frame" that isn't
        // real audio - it's encoder metadata shaped like an MP3 frame for
        // compatibility. Decoded naively, it can produce a burst of static
        // right at the start. Increasing the leading silence padding made
        // this MORE audible rather than less, because padding alone doesn't
        // remove it - it was previously being hidden by coincidence, cut off
        // by the device-startup clipping bug this whole feature was built to
        // fix. Discarding a small slice of decoded audio here - not just
        // padding around it - actually removes it from the stream.
        //
        // WHY (1 Nov 2026, Asher: "when the after race call ups fire the very
        // beginning of the audio sounds clipped"): the slice was a flat 80 ms
        // whatever was in it. Most lines don't start speaking until 85-120 ms
        // into the decode, so the cut only took silence. A line that opens on
        // a hard word starts much sooner: "Q2 Heat 1 for Stock is next" is
        // speaking from 35 ms and "A-Main for Stock is next" from 50 ms (the
        // saved ElevenLabs takes in the voice library), so the cut took the
        // start of the first word. Now it stops where the words begin.
        var skip = QuietStartToSkip(whole, format);

        // Levelled AFTER the skip is found (8 Nov 2026): the onset test is
        // against the level the words arrived at. Gain changes no length,
        // so the skip still lines up with the API's timings. See VoiceLevel.
        return (VoiceLevel.Level(whole[skip..], format), format, skip / (double)Math.Max(1, format.AverageBytesPerSecond));
    }

    /// <summary>
    /// How many bytes to throw away from the front of decoded audio: up to
    /// <see cref="DecodeSkipSeconds"/>, but never into the words - stopping
    /// <see cref="SpeechOnsetGuardSeconds"/> short of the first sample at
    /// <see cref="SpeechOnsetLevel"/>. Whole sample frames, so what is left
    /// still starts on a sample.
    /// </summary>
    public static int QuietStartToSkip(ReadOnlySpan<byte> pcm, WaveFormat format)
    {
        var block = Math.Max(1, format.BlockAlign);
        var most = (int)AlignDown(Math.Min(pcm.Length, (long)(format.AverageBytesPerSecond * DecodeSkipSeconds)), block);

        // An MP3 always decodes to 16-bit PCM; anything else is skipped as before.
        if (format.Encoding != WaveFormatEncoding.Pcm || format.BitsPerSample != 16)
        {
            return most;
        }

        for (var i = 0; i + 1 < most; i += 2)
        {
            if (Math.Abs((int)BitConverter.ToInt16(pcm[i..])) >= SpeechOnsetLevel)
            {
                var guard = (long)(format.AverageBytesPerSecond * SpeechOnsetGuardSeconds);
                return (int)AlignDown(Math.Max(0, i - guard), block);
            }
        }

        return most;
    }

    /// <summary>
    /// Decodes whatever a synthesis handed back.
    ///
    /// ElevenLabs sends MP3, but the saved-voice library can also hand back
    /// WAV: a release call cut out of a batched render is saved as the PCM
    /// it was cut to (see TryRenderReleaseBatchAsync), already trimmed, so
    /// nothing is skipped from its start the way an MP3's is.
    /// </summary>
    public static (byte[] Pcm, WaveFormat Format) DecodeAudio(byte[] audio)
    {
        if (VoiceLibrary.SniffAudio(audio) != VoiceLibrary.SavedAudioKind.Wav)
        {
            return DecodeMp3(audio);
        }

        using var stream = new MemoryStream(audio);
        using var reader = new WaveFileReader(stream);
        var format = reader.WaveFormat;

        using var pcm = new MemoryStream();
        var buffer = new byte[Math.Max(4096, format.AverageBytesPerSecond)];
        int bytesRead;

        while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            pcm.Write(buffer, 0, bytesRead);
        }

        // A saved cut is levelled as the MP3 it came from is - see VoiceLevel.
        return (VoiceLevel.Level(pcm.ToArray(), format), format);
    }

    /// <summary>
    /// How long a line rendered ahead takes to say, from its decoded audio -
    /// or null when it cannot be told (26 Oct 2026, review). For a long read
    /// fed in piece by piece: while a made piece plays, the one behind it can
    /// be waited for this long before it has to go as text. Decodes, so call
    /// it off the UI thread.
    /// </summary>
    public static TimeSpan? LengthOf(PreparedSpeech? prepared)
    {
        if (prepared is null || prepared.Audio.Length == 0)
        {
            return null;
        }

        try
        {
            var (pcm, format) = DecodeAudio(prepared.Audio);

            return format.AverageBytesPerSecond > 0
                ? TimeSpan.FromSeconds((double)pcm.Length / format.AverageBytesPerSecond)
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Wraps decoded PCM as a WAV file, for the saved-voice library.</summary>
    private static byte[] EncodeWav(byte[] pcm, WaveFormat format)
    {
        // ToArray works after the writer has closed the stream, and closing
        // it is what makes the writer fill in the header's lengths.
        var memory = new MemoryStream();

        using (var writer = new WaveFileWriter(memory, format))
        {
            writer.Write(pcm, 0, pcm.Length);
        }

        return memory.ToArray();
    }

    /// <summary>
    /// Plays audio under the same output lock the announcer's own lines use,
    /// so a Voice Settings test during a session waits its turn instead of
    /// starting a second device on top of a live call (see _audioPlaybackLock).
    /// </summary>
    public async Task PlayExclusiveAsync(byte[] mp3Bytes, double volume)
    {
        await _audioPlaybackLock.WaitAsync();
        try
        {
            await PlayAsync(mp3Bytes, volume);
        }
        finally
        {
            _audioPlaybackLock.Release();
        }
    }

    /// <summary>
    /// Plays raw MP3 bytes (as returned by ElevenLabs) and waits for playback
    /// to finish, including a brief tail grace period.
    ///
    /// Streaming compressed audio straight into WaveOutEvent (the original
    /// approach here) could clip a fraction of a second off the start and
    /// end of every announcement. The real cause, confirmed by testing:
    /// WaveOutEvent defaults to ~300ms of internal device buffering before
    /// it's fully warmed up, so the very start of playback can be lost to
    /// startup latency before real audio ever reaches the speaker, and
    /// PlaybackStopped can fire slightly before the last buffer has
    /// actually finished draining. Fully decoding to PCM up front (rather
    /// than decoding in real time during playback), explicitly shrinking
    /// that device latency, and padding both ends with a touch of silence
    /// together remove the timing dependency that caused it.
    /// </summary>
    /// <param name="stillWanted">See <see cref="PlayPcmAsync"/>.</param>
    /// <param name="onAir">See <see cref="PlayPcmAsync"/>.</param>
    /// <param name="timing">
    /// The queued line's timing (10 Nov 2026), given its lengths and the
    /// moment the output first reads it. Null for anything not timed.
    /// </param>
    internal static Task PlayAsync(
        byte[] mp3Bytes,
        double volume,
        double trailingSilenceSeconds = DefaultTrailingSilenceSeconds,
        double leadingSilenceSeconds = DefaultLeadingSilenceSeconds,
        Func<bool>? stillWanted = null,
        bool onAir = false,
        VoiceLineTiming? timing = null)
    {
        var (pcm, format) = DecodeAudio(mp3Bytes);
        Action? started = null;

        if (timing is not null)
        {
            NoteLengths(timing, pcm.Length, format, leadingSilenceSeconds, trailingSilenceSeconds);
            started = () => timing.FirstPulled(TimingClock.NowMs);
        }

        return PlayPcmAsync(pcm, format, volume, trailingSilenceSeconds, leadingSilenceSeconds, started: started, stillWanted: stillWanted, onAir: onAir);
    }

    /// <summary>A timed line's lengths as PlayPcmAsync pads it - the same clamps. See VoiceLineTiming.</summary>
    internal static void NoteLengths(VoiceLineTiming timing, int pcmBytes, WaveFormat format, double leadingSilenceSeconds, double trailingSilenceSeconds)
    {
        var bytesPerSecond = Math.Max(1, format.AverageBytesPerSecond);
        timing.SpeechMs = (long)(1000.0 * pcmBytes / bytesPerSecond);
        timing.LeadMs = (long)(1000.0 * Math.Clamp(leadingSilenceSeconds, 0.0, DefaultLeadingSilenceSeconds));
        timing.TailMs = (long)(1000.0 * Math.Clamp(trailingSilenceSeconds, 0.0, LongLineTrailingSilenceSeconds));
    }

    /// <summary>
    /// The checks every announcer line makes before it speaks - a flag since
    /// it was taken off the queue, a countdown since then, and (when the line
    /// honours it) the mute - as one test, for <see cref="PlayPcmAsync"/>'s
    /// stillWanted. Reads the generations stamped at dequeue, which are
    /// AsyncLocal and so flow into the awaited playback call.
    /// </summary>
    /// <remarks>
    /// A line it turns down is taken back off the screens too (21 Nov 2026):
    /// it will never be heard now.
    /// </remarks>
    private Func<bool> StillWantedUnlessOvertaken(bool honourMute) =>
        () =>
        {
            var wanted = !IsSupersededByInterrupt() && !IsCutByCountdown() && !IsOvertakenOnTrack() && !(honourMute && Muted);

            if (!wanted)
            {
                WithdrawAnnouncementText(_dequeuedFreshness.Value.Shown);
            }

            return wanted;
        };

    /// <summary>Plays already-decoded audio, padded the way every line is.</summary>
    /// <param name="stillWanted">
    /// Asked once more AFTER the line is registered as the one playing, and
    /// the line is dropped unheard if it says no. See the WHY at the check.
    /// </param>
    /// <param name="onAir">
    /// Whether anyone listening along from home hears this line too (2 Nov
    /// 2026 - see ListenStream). Off unless a caller has cleared the line
    /// for the public page (OfferLineToListeners), so a test pressed by hand
    /// or a line nobody checked stays in the room.
    /// </param>
    public static async Task PlayPcmAsync(
        byte[] pcmBytes,
        WaveFormat format,
        double volume,
        double trailingSilenceSeconds = DefaultTrailingSilenceSeconds,
        double leadingSilenceSeconds = DefaultLeadingSilenceSeconds,
        Action? started = null,
        Func<bool>? stillWanted = null,
        bool onAir = false)
    {
        byte[] paddedPcm;

        // The line's lead-in silence and its voice, as lengths, for the Big
        // Screen's driver cards (IntroOnAir) - known exactly here and nowhere
        // else.
        TimeSpan leadFor;
        TimeSpan spokenFor;

        {
            // Whole sample frames only. A pad that isn't a multiple of the
            // frame size (2 bytes for 16-bit mono) shifts every sample after
            // it by one byte, and the line plays as loud static. 16-bit mono
            // at 22.05 kHz is 44100 bytes a second, so the wired 0.15 s lead
            // (6615 bytes) and the 0.25 s back-to-back tail (11025) were both odd.
            var block = Math.Max(1, format.BlockAlign);
            var leadingBytes = new byte[(int)AlignDown((long)(format.AverageBytesPerSecond
                * Math.Clamp(leadingSilenceSeconds, 0.0, DefaultLeadingSilenceSeconds)), block)];
            var trailingBytes = new byte[(int)AlignDown((long)(format.AverageBytesPerSecond
                * Math.Clamp(trailingSilenceSeconds, 0.0, LongLineTrailingSilenceSeconds)), block)];

            using var pcm = new MemoryStream();
            pcm.Write(leadingBytes, 0, leadingBytes.Length);
            pcm.Write(pcmBytes, 0, pcmBytes.Length);
            pcm.Write(trailingBytes, 0, trailingBytes.Length);
            paddedPcm = pcm.ToArray();

            var bytesPerSecond = Math.Max(1, format.AverageBytesPerSecond);
            leadFor = TimeSpan.FromSeconds(leadingBytes.Length / (double)bytesPerSecond);
            spokenFor = TimeSpan.FromSeconds(pcmBytes.Length / (double)bytesPerSecond);
        }

        using var pcmStream = new MemoryStream(paddedPcm);
        using var rawReader = new RawSourceWaveStream(pcmStream, format);

        // Through the shared output (see AudioBus) rather than one opened
        // for this line: opening an output per line stalled the music for a
        // moment every time the announcer spoke. Still throws when there is
        // no usable output device, which the voice queue already reports
        // and carries on from - a dead announcer is the outcome to avoid.
        using var stop = new CancellationTokenSource();

        bool cutAsSoonAsHeard;

        lock (ActiveOutputLock)
        {
            _activeLine = stop;

            // When its words begin, past the lead-in silence - see
            // StopCurrentPlaybackAfterABeat.
            _activeLineWordsAtMs = TimingClock.NowMs + (long)leadFor.TotalMilliseconds;

            // A cut asked for in the moment before this line registered.
            cutAsSoonAsHeard = _cutTheLineAboutToPlay;
            _cutTheLineAboutToPlay = false;
        }

        if (cutAsSoonAsHeard)
        {
            CutAfterABeat(stop, _activeLineWordsAtMs);
        }

        // Whether the line played to its end, for the captions below.
        var heardToTheEnd = false;

        // The driver introductions' cards, when this line is them - 0 for
        // any other line. See IntroOnAir.
        var introCards = 0;

        try
        {
            // WHY (23 Oct 2026, bug hunt 10): every caller checks for a flag,
            // the countdown and the mute BEFORE it calls here, but the line
            // was only registered above after it had been decoded and padded
            // - for a 90 s recap MP3 a noticeable moment. A flag, countdown
            // or mute landing in that gap found nothing to stop, and the line
            // then played in full over it. Asked again now that it IS
            // registered: the canceller sets its flag and then takes
            // ActiveOutputLock to stop us, we registered under that same lock
            // and read its flag after - so either it cancelled this line (and
            // the bus returns at once) or this check sees it and drops it.
            if (stillWanted is not null && !stillWanted())
            {
                return;
            }

            // Up to AudioBus.MaxBoost: the announcer's level times the
            // "boost voice over loud ambience" gain (AnnouncementGain), which
            // a 0..1 clamp here used to cancel. The bus soft-limits a boosted
            // line so it bends rather than clips.
            //
            // started: run when the output first pulls the line - the moment
            // it is heard, which after a speaker dropout is later than now
            // (22 Oct 2026, known issues; see FirstReadSampleProvider).
            //
            // And when the output first pulls it, its words' start is set
            // again from that moment (21 Nov 2026): the first pulled sample,
            // its lead-in silence and the device's buffer ahead of the
            // speaker (VoiceLineTiming.DeviceAheadMs) - nearer the moment the
            // room hears the first word than the registration above, so a
            // cut-off line is heard for its full CutLineHeardAtLeastMs.
            var leadMs = (long)leadFor.TotalMilliseconds;
            ISampleProvider source = new FirstReadSampleProvider(rawReader.ToSampleProvider(), () =>
            {
                NoteWordsStartFromFirstSample(stop, leadMs);
                started?.Invoke();
            });

            // WHY (1 Nov 2026, asked for by Asher): the Big Screen puts each
            // driver's name and photo up as the introductions reach them, and
            // this is the one place the line's real length is known - so the
            // cards are timed by the audio rather than by a guess at how fast
            // the voice reads. Nothing here can throw into the voice.
            try
            {
                introCards = IntroOnAir.Shared.Playing(leadFor, spokenFor);
            }
            catch
            {
                // A card is never worth a line of the broadcast.
            }

            // voice: on the VOICE level meter, not SFX (26 Oct 2026 - see AudioLevels).
            await AudioBus.PlayAsync(source, (float)Math.Clamp(volume, 0.0, AudioBus.MaxBoost), stop.Token, allowBoost: true, voice: true, offAir: !onAir);

            // Cancelling completes the bus's task exactly as a natural end
            // does (see _activeLine), so a line cut off is told apart here.
            heardToTheEnd = !stop.IsCancellationRequested;
        }
        finally
        {
            lock (ActiveOutputLock)
            {
                if (ReferenceEquals(_activeLine, stop))
                {
                    _activeLine = null;
                }
            }

            // WHY (26 Oct 2026, asked for by Asher: "Big screen doesn't
            // reliably show live captions"): a long line is captioned a
            // piece at a time, timed by an estimate of how fast the voice
            // speaks (CaptionLog). The end of the line is the one moment
            // that is known rather than estimated: played to the end, what
            // is left of its caption goes up now; cut off by a flag, the
            // countdown or the mute, or dropped before it started, the rest
            // is never shown, because the room never heard it. Lines are
            // played one at a time (the playback lock), so what is waiting
            // is always this line's. Nothing here can throw.
            if (heardToTheEnd)
            {
                CaptionLog.Shared.SaidInFull();
            }
            else
            {
                CaptionLog.Shared.Unsaid();
            }

            // The driver cards the same way: the last one held a moment once
            // the line is said, all of them down at once when it is cut off.
            if (introCards != 0)
            {
                try
                {
                    IntroOnAir.Shared.Stopped(introCards, heardToTheEnd);
                }
                catch
                {
                    // As above.
                }
            }
        }

        // Grace period before the objects above get disposed, so the tail
        // end of the last buffer has time to actually finish playing
        // through the audio hardware rather than being cut off.
        //
        // The bus reports a line done when the last of it has been MIXED,
        // which is still a buffer or so ahead of what is being heard.
        //
        // NOT FOR A LINE WITH ANOTHER HARD BEHIND IT (19 Oct 2026). The
        // next line goes through the same bus, which is still running, so
        // its first sample is mixed straight after this one's last - it
        // cannot cut this one off, and it cannot overlap it. Waiting here
        // only held the speaker for a third of a second more, which was a
        // third of a second of dead air at every change of voice in a
        // two-hander, on top of the trimmed tail that already gives the
        // hand-over its breath.
        //
        // AND NOT FOR A TAIL THAT ALREADY COVERS IT (10 Nov 2026, voice speed
        // plan, phase 1). The grace is for the device's buffer, which is
        // AudioBus.OutputLatencyMs - and a line's own trailing silence is
        // mixed after its last word, so once the bus has mixed a 0.6 s tail
        // the last word left the speaker long ago. Only the part of the
        // buffer the tail does not cover is waited for now: none, for every
        // standard line. The flat 350 ms was dead air after every call - a
        // third of a second of the 2.5 s the baseline race lost per call.
        if (trailingSilenceSeconds > BackToBackTrailingSilenceSeconds
            && GraceAfterLineMs(trailingSilenceSeconds) is var grace and > 0)
        {
            await Task.Delay(grace);
        }
    }

    /// <summary>
    /// Plays a line while it is still arriving (10 Nov 2026, voice speed
    /// plan, phase 3) - registered as the playing line exactly as
    /// PlayPcmAsync registers one, so a flag, the countdown or the mute cuts
    /// it off the same way. Its silences are already in it (StreamingVoiceLine).
    /// </summary>
    internal static async Task PlayStreamAsync(
        StreamingVoiceLine line, double volume, double leadingSilenceSeconds,
        Func<bool>? stillWanted = null, bool onAir = false, VoiceLineTiming? timing = null)
    {
        using var stop = new CancellationTokenSource();
        var heardToTheEnd = false;

        bool cutAsSoonAsHeard;

        lock (ActiveOutputLock)
        {
            _activeLine = stop;
            _activeLineWordsAtMs = TimingClock.NowMs + (long)(leadingSilenceSeconds * 1000);

            // See PlayPcmAsync: a cut asked for in the moment before this registered.
            cutAsSoonAsHeard = _cutTheLineAboutToPlay;
            _cutTheLineAboutToPlay = false;
        }

        if (cutAsSoonAsHeard)
        {
            CutAfterABeat(stop, _activeLineWordsAtMs);
        }

        try
        {
            // See PlayPcmAsync: asked again once registered.
            if (stillWanted is not null && !stillWanted())
            {
                return;
            }

            // Its words' start set again from the first pulled sample (21 Nov
            // 2026) - see PlayPcmAsync.
            var leadMs = (long)(leadingSilenceSeconds * 1000);
            ISampleProvider source = new FirstReadSampleProvider(line, () =>
            {
                NoteWordsStartFromFirstSample(stop, leadMs);
                timing?.FirstPulled(TimingClock.NowMs);
            });

            await AudioBus.PlayAsync(source, (float)Math.Clamp(volume, 0.0, AudioBus.MaxBoost), stop.Token, allowBoost: true, voice: true, offAir: !onAir);

            heardToTheEnd = !stop.IsCancellationRequested;
        }
        finally
        {
            lock (ActiveOutputLock)
            {
                if (ReferenceEquals(_activeLine, stop))
                {
                    _activeLine = null;
                }
            }

            if (heardToTheEnd)
            {
                CaptionLog.Shared.SaidInFull();
            }
            else
            {
                CaptionLog.Shared.Unsaid();
            }
        }
    }

    /// <summary>How long after the bus has mixed a line's tail the device is still playing its last word - the buffer less the tail. See PlayPcmAsync.</summary>
    internal static int GraceAfterLineMs(double trailingSilenceSeconds) =>
        Math.Max(0, AudioBus.OutputLatencyMs - (int)(trailingSilenceSeconds * 1000));

    /// <summary>
    /// The tail a line gets now: a standard line followed by another one
    /// waiting in the queue gets the back-to-back tail (10 Nov 2026, voice
    /// speed plan, phase 1). The 0.6 s was there to stop the device cutting
    /// a line's end when it stopped; the shared output never stops now (see
    /// AudioBus), so with another call ready it is only a pause - and on a
    /// busy track it was the next call's lap going stale. An empty queue
    /// keeps the full breath.
    /// </summary>
    private double TailSecondsNow(SpeechTail tail)
    {
        if (tail != SpeechTail.Normal)
        {
            return TailSecondsFor(tail);
        }

        lock (_voiceQueueLock)
        {
            return _voiceQueue.Count > 0 ? BackToBackTrailingSilenceSeconds : DefaultTrailingSilenceSeconds;
        }
    }
}
