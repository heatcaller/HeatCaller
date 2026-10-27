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
    /// How much of the decoded start is thrown away, and therefore how far
    /// the decoded audio runs AHEAD of any timings the API reported for it.
    /// Anything cutting that audio by time has to allow for this.
    /// </summary>
    public const double DecodeSkipSeconds = 0.08;

    /// <summary>
    /// Decodes an MP3 to raw PCM.
    ///
    /// Split out of PlayAsync so one render covering several lines can be
    /// decoded once and cut into pieces, each played on its own.
    /// </summary>
    public static (byte[] Pcm, WaveFormat Format) DecodeMp3(byte[] mp3Bytes)
    {
        using var mp3Stream = new MemoryStream(mp3Bytes);
        using var mp3Reader = new Mp3FileReader(mp3Stream);
        var format = mp3Reader.WaveFormat;

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
        // Read until that much has gone, or the line has: one Read may hand
        // back less than was asked for (CA2022), and what it left would be
        // heard.
        var skip = (int)(format.AverageBytesPerSecond * DecodeSkipSeconds);
        var skipBuffer = new byte[Math.Max(1, skip)];
        var skipped = 0;

        while (skipped < skip)
        {
            var read = mp3Reader.Read(skipBuffer, 0, skip - skipped);

            if (read <= 0)
            {
                break;
            }

            skipped += read;
        }

        using var pcm = new MemoryStream();
        var buffer = new byte[format.AverageBytesPerSecond];
        int bytesRead;

        while ((bytesRead = mp3Reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            pcm.Write(buffer, 0, bytesRead);
        }

        return (pcm.ToArray(), format);
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

        return (pcm.ToArray(), format);
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
    public static Task PlayAsync(
        byte[] mp3Bytes,
        double volume,
        double trailingSilenceSeconds = DefaultTrailingSilenceSeconds,
        double leadingSilenceSeconds = DefaultLeadingSilenceSeconds,
        Func<bool>? stillWanted = null)
    {
        var (pcm, format) = DecodeAudio(mp3Bytes);
        return PlayPcmAsync(pcm, format, volume, trailingSilenceSeconds, leadingSilenceSeconds, stillWanted: stillWanted);
    }

    /// <summary>
    /// The checks every announcer line makes before it speaks - a flag since
    /// it was taken off the queue, a countdown since then, and (when the line
    /// honours it) the mute - as one test, for <see cref="PlayPcmAsync"/>'s
    /// stillWanted. Reads the generations stamped at dequeue, which are
    /// AsyncLocal and so flow into the awaited playback call.
    /// </summary>
    private Func<bool> StillWantedUnlessOvertaken(bool honourMute) =>
        () => !IsSupersededByInterrupt() && !IsCutByCountdown() && !(honourMute && Muted);

    /// <summary>Plays already-decoded audio, padded the way every line is.</summary>
    /// <param name="stillWanted">
    /// Asked once more AFTER the line is registered as the one playing, and
    /// the line is dropped unheard if it says no. See the WHY at the check.
    /// </param>
    public static async Task PlayPcmAsync(
        byte[] pcmBytes,
        WaveFormat format,
        double volume,
        double trailingSilenceSeconds = DefaultTrailingSilenceSeconds,
        double leadingSilenceSeconds = DefaultLeadingSilenceSeconds,
        Action? started = null,
        Func<bool>? stillWanted = null)
    {
        byte[] paddedPcm;

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
        }

        using var pcmStream = new MemoryStream(paddedPcm);
        using var rawReader = new RawSourceWaveStream(pcmStream, format);

        // Through the shared output (see AudioBus) rather than one opened
        // for this line: opening an output per line stalled the music for a
        // moment every time the announcer spoke. Still throws when there is
        // no usable output device, which the voice queue already reports
        // and carries on from - a dead announcer is the outcome to avoid.
        using var stop = new CancellationTokenSource();

        lock (ActiveOutputLock)
        {
            _activeLine = stop;
        }

        // Whether the line played to its end, for the captions below.
        var heardToTheEnd = false;

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
            ISampleProvider source = started is null
                ? rawReader.ToSampleProvider()
                : new FirstReadSampleProvider(rawReader.ToSampleProvider(), started);

            // voice: on the VOICE level meter, not SFX (26 Oct 2026 - see AudioLevels).
            await AudioBus.PlayAsync(source, (float)Math.Clamp(volume, 0.0, AudioBus.MaxBoost), stop.Token, allowBoost: true, voice: true);

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
        if (trailingSilenceSeconds > BackToBackTrailingSilenceSeconds)
        {
            await Task.Delay(350);
        }
    }
}
