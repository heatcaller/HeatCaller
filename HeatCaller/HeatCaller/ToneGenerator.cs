using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Linq;

namespace HeatCaller;

/// <summary>
/// Plays sound effects for lap feedback. Since this app has no way to
/// source real sampled sound effects (like Next Level Timing's own audio
/// assets), tones are generated procedurally as short sine-wave beeps/
/// chimes via NAudio. If the user supplies their own audio file for an
/// effect, that's played instead.
/// </summary>
public static class ToneGenerator
{

    /// <summary>
    /// One of the per-driver lap cues.
    ///
    /// These vary in SHAPE as well as pitch - single, double, rising,
    /// falling, warbling - because pitch alone is hard to tell apart over
    /// track noise and several cars running. Each is kept short: a cue that
    /// outlasts the gap between two cars crossing is worse than none.
    /// </summary>
    public static Task PlayDriverLapAsync(int toneIndex, float volume) => toneIndex switch
    {
        0 => PlayNotesAsync(new[] { (1318.0, 70) }, volume),                      // high blip
        1 => PlayNotesAsync(new[] { (440.0, 80) }, volume),                       // low blip
        2 => PlayNotesAsync(new[] { (1318.0, 55), (1318.0, 55) }, volume),        // double high
        3 => PlayNotesAsync(new[] { (440.0, 60), (440.0, 60) }, volume),          // double low
        4 => PlayNotesAsync(new[] { (698.0, 55), (1046.0, 75) }, volume),         // rising
        5 => PlayNotesAsync(new[] { (1046.0, 55), (698.0, 75) }, volume),         // falling
        6 => PlayNotesAsync(new[] { (988.0, 40), (988.0, 40), (988.0, 40) }, volume), // triple tick
        _ => PlayNotesAsync(new[] { (880.0, 45), (1108.0, 45), (880.0, 45) }, volume), // warble
    };

    /// <summary>
    /// How loud a coaching tone is against the sound effects' own level
    /// (22 Oct 2026): quieter. It is for one driver, who is listening for
    /// it, and it must not turn the whole room's head on every lap.
    /// </summary>
    internal const float CoachToneLevel = 0.6f;

    /// <summary>
    /// A coaching tone (22 Oct 2026, "Tones instead of words"): one short
    /// note, high for quicker than the target, middle for on it, low for
    /// slower - and two rising notes for a new best, a shape no other cue
    /// has. About 120 ms, the length of the existing lap blips: long enough
    /// to place the pitch, short enough never to overlap the next car.
    /// Built on the caller's thread (PlayNotesAsync), never the device's.
    /// </summary>
    internal static Task PlayCoachToneAsync(CoachTone tone, float volume) =>
        PlayNotesAsync(CoachNotes(tone), Math.Clamp(volume, 0f, 1f) * CoachToneLevel);

    /// <summary>The notes of each coaching tone. Internal for the tests.</summary>
    internal static (double Freq, int DurationMs)[] CoachNotes(CoachTone tone) => tone switch
    {
        CoachTone.Faster => new[] { (1046.0, 120) },
        CoachTone.OnTarget => new[] { (784.0, 120) },
        CoachTone.Slower => new[] { (523.0, 120) },
        _ => new[] { (784.0, 70), (1318.0, 90) },
    };

    /// <summary>A short ascending two-note chime for a valid ("good", no crash) lap.</summary>
    public static Task PlayGoodLapAsync(float volume) => PlayGoodLapAsync(volume, CancellationToken.None);

    /// <inheritdoc cref="PlayGoodLapAsync(float)"/>
    public static Task PlayGoodLapAsync(float volume, CancellationToken cancellationToken) =>
        PlayNotesAsync(new[] { (880.0, 90), (1174.0, 110) }, volume, cancellationToken);

    /// <summary>A low double-buzz for an invalid lap.</summary>
    public static Task PlayInvalidLapAsync(float volume) => PlayInvalidLapAsync(volume, CancellationToken.None);

    /// <inheritdoc cref="PlayInvalidLapAsync(float)"/>
    public static Task PlayInvalidLapAsync(float volume, CancellationToken cancellationToken) =>
        PlayNotesAsync(new[] { (220.0, 120), (196.0, 160) }, volume, cancellationToken);

    /// <summary>
    /// A triumphant rising arpeggio for a consistency streak. Pitch climbs
    /// slightly with streak length (capped) so longer streaks sound a little
    /// more exciting without needing real sampled audio.
    /// </summary>
    public static Task PlayStreakAsync(int streakCount, float volume) =>
        PlayStreakAsync(streakCount, volume, CancellationToken.None);

    /// <inheritdoc cref="PlayStreakAsync(int, float)"/>
    public static Task PlayStreakAsync(int streakCount, float volume, CancellationToken cancellationToken)
    {
        var baseFreq = 523.0 + Math.Min(streakCount, 15) * 12.0; // C5-ish, climbing with streak length
        var notes = new[]
        {
            (baseFreq, 90),
            (baseFreq * 1.25, 90),
            (baseFreq * 1.5, 140),
        };
        return PlayNotesAsync(notes, volume, cancellationToken);
    }

    /// <summary>A bright ascending three-note "ta-da" for a new fast lap.</summary>
    public static Task PlayFastLapAsync(float volume) => PlayFastLapAsync(volume, CancellationToken.None);

    /// <inheritdoc cref="PlayFastLapAsync(float)"/>
    public static Task PlayFastLapAsync(float volume, CancellationToken cancellationToken) =>
        PlayNotesAsync(new[] { (1046.5, 80), (1318.5, 80), (1568.0, 140) }, volume, cancellationToken);

    /// <summary>A bigger, longer five-note ascending fanfare for an all-time track record - deliberately grander than the regular fast-lap chime since this is rarer.</summary>
    public static Task PlayTrackRecordAsync(float volume) => PlayTrackRecordAsync(volume, CancellationToken.None);

    /// <inheritdoc cref="PlayTrackRecordAsync(float)"/>
    public static Task PlayTrackRecordAsync(float volume, CancellationToken cancellationToken) =>
        PlayNotesAsync(new[] { (784.0, 70), (987.8, 70), (1174.7, 70), (1568.0, 90), (2093.0, 220) }, volume, cancellationToken);

    /// <summary>A quick rising "whoosh-ding" for an overtake / leader change.</summary>
    public static Task PlayLeaderChangeAsync(float volume) => PlayLeaderChangeAsync(volume, CancellationToken.None);

    /// <inheritdoc cref="PlayLeaderChangeAsync(float)"/>
    public static Task PlayLeaderChangeAsync(float volume, CancellationToken cancellationToken) =>
        PlayNotesAsync(new[] { (700.0, 60), (900.0, 60), (1100.0, 100) }, volume, cancellationToken);

    /// <summary>Two calm low beeps calling racers to the grid.</summary>
    public static Task PlayRaceStagingAsync(float volume) => PlayRaceStagingAsync(volume, CancellationToken.None);

    /// <inheritdoc cref="PlayRaceStagingAsync(float)"/>
    public static Task PlayRaceStagingAsync(float volume, CancellationToken cancellationToken) =>
        PlayNotesAsync(new[] { (440.0, 150), (440.0, 150) }, volume, cancellationToken);

    /// <summary>Three short even beeps marking the countdown beginning.</summary>
    public static Task PlayCountdownStartedAsync(float volume) => PlayCountdownStartedAsync(volume, CancellationToken.None);

    /// <inheritdoc cref="PlayCountdownStartedAsync(float)"/>
    public static Task PlayCountdownStartedAsync(float volume, CancellationToken cancellationToken) =>
        PlayNotesAsync(new[] { (500.0, 90), (500.0, 90), (500.0, 90) }, volume, cancellationToken);

    /// <summary>A two-tone "air horn" for the green flag.</summary>
    public static Task PlayRaceStartedAsync(float volume) => PlayRaceStartedAsync(volume, CancellationToken.None);

    /// <inheritdoc cref="PlayRaceStartedAsync(float)"/>
    public static Task PlayRaceStartedAsync(float volume, CancellationToken cancellationToken) =>
        PlayNotesAsync(new[] { (392.0, 220), (523.25, 320) }, volume, cancellationToken);

    /// <summary>An urgent alternating high-low buzzer for the last-lap warning.</summary>
    public static Task PlayRaceTimeOverAsync(float volume) => PlayRaceTimeOverAsync(volume, CancellationToken.None);

    /// <inheritdoc cref="PlayRaceTimeOverAsync(float)"/>
    public static Task PlayRaceTimeOverAsync(float volume, CancellationToken cancellationToken) =>
        PlayNotesAsync(new[] { (800.0, 120), (400.0, 120), (800.0, 120), (400.0, 120) }, volume, cancellationToken);

    /// <summary>A four-note victory fanfare for the checkered flag.</summary>
    public static Task PlayRaceCompletedAsync(float volume) => PlayRaceCompletedAsync(volume, CancellationToken.None);

    /// <inheritdoc cref="PlayRaceCompletedAsync(float)"/>
    public static Task PlayRaceCompletedAsync(float volume, CancellationToken cancellationToken) =>
        PlayNotesAsync(new[] { (523.25, 110), (659.25, 110), (784.0, 110), (1046.5, 260) }, volume, cancellationToken);

    /// <summary>
    /// A bright two-note "ding-ding" for a personal best - a step up from
    /// the good-lap chime, a step down from the fast-lap "ta-da".
    /// </summary>
    public static Task PlayPersonalBestAsync(float volume, CancellationToken cancellationToken) =>
        PlayNotesAsync(new[] { (1046.5, 90), (1568.0, 160) }, volume, cancellationToken);

    /// <summary>
    /// Five short beeps a second apart, then a second of quiet - so when it
    /// ends, the green is due. Exactly <see cref="AnnouncementService.CountdownTicksLength"/>
    /// long; a frequency of zero is silence.
    /// </summary>
    public static Task PlayCountdownTicksAsync(float volume, CancellationToken cancellationToken) =>
        PlayNotesAsync(new[]
        {
            (880.0, 120), (0.0, 880),
            (880.0, 120), (0.0, 880),
            (880.0, 120), (0.0, 880),
            (880.0, 120), (0.0, 880),
            (880.0, 120), (0.0, 880),
        }, volume, cancellationToken);

    /// <summary>A long, low double buzz for a jump start - harsher and longer than the invalid-lap one.</summary>
    public static Task PlayJumpStartAsync(float volume, CancellationToken cancellationToken) =>
        PlayNotesAsync(new[] { (185.0, 260), (0.0, 70), (185.0, 380) }, volume, cancellationToken);

    /// <summary>Three even pulses in the middle of the range - "caution", not alarm.</summary>
    public static Task PlayYellowFlagAsync(float volume, CancellationToken cancellationToken) =>
        PlayNotesAsync(new[] { (660.0, 180), (0.0, 80), (660.0, 180), (0.0, 80), (660.0, 180) }, volume, cancellationToken);

    /// <summary>
    /// A slow high-low siren for a red flag - stop. Slower and longer than
    /// the time-up buzzer's alternation, so the two can't be mistaken.
    /// </summary>
    public static Task PlayRedFlagAsync(float volume, CancellationToken cancellationToken) =>
        PlayNotesAsync(new[] { (960.0, 240), (620.0, 240), (960.0, 240), (620.0, 240), (960.0, 240), (620.0, 320) }, volume, cancellationToken);

    /// <summary>A rising two-note call for going green again - brighter and shorter than the start horn.</summary>
    public static Task PlayGreenFlagResumedAsync(float volume, CancellationToken cancellationToken) =>
        PlayNotesAsync(new[] { (523.25, 140), (784.0, 280) }, volume, cancellationToken);

    /// <summary>
    /// Renders an entire multi-note effect (e.g. the two notes of "good
    /// lap", or the three of "streak") into ONE continuous PCM buffer and
    /// plays it through a SINGLE WaveOutEvent.
    ///
    /// Two separate problems were found and fixed here across testing:
    /// (1) the previous version opened a fresh WaveOutEvent per NOTE, and
    /// with notes only 60-140ms long, most or all of a note's audible
    /// content could be lost to WaveOutEvent's device startup overhead -
    /// fixed by rendering the whole effect into one buffer and paying that
    /// cost once. (2) that render used 32-bit IEEE float PCM, which some
    /// audio drivers handle poorly - producing no error, just no sound.
    /// Standard 16-bit integer PCM (the same kind voice announcements
    /// already use successfully, since MP3 decodes to it) is about as
    /// universally supported as audio gets, so tones now use that too.
    /// </summary>
    private static async Task PlayNotesAsync((double Freq, int DurationMs)[] notes, float volume, CancellationToken cancellationToken = default)
    {
        const int sampleRate = 44100;
        var format = new WaveFormat(sampleRate, 16, 1); // 16-bit PCM, mono

        // Leading padding needs to comfortably outlast this system's actual
        // device startup latency. Measured directly by testing every
        // effect's total duration against the default WaveOutEvent buffer
        // warm-up: everything under ~720ms was silently swallowed entirely,
        // everything over ~880ms played fine. That's a much longer warm-up
        // than NAudio's own ~300ms default would suggest - likely a
        // Bluetooth or similar wireless audio path, which often has far
        // higher latency than a standard wired output. Padding well past
        // the observed threshold here rather than creeping up again.
        //
        // Since the shared output (AudioBus) the device never goes to
        // sleep between sounds, so there is nothing left to wake: the 0.9s
        // that was here only made every effect late.
        const double leadingPaddingSeconds = AudioBus.LeadInSeconds;
        const double trailingPaddingSeconds = 0.05;
        var leadingPaddingSamples = (int)(sampleRate * leadingPaddingSeconds);
        var trailingPaddingSamples = (int)(sampleRate * trailingPaddingSeconds);

        var floatSamples = new List<float>(leadingPaddingSamples + trailingPaddingSamples + notes.Sum(n => sampleRate * n.DurationMs / 1000));
        floatSamples.AddRange(new float[leadingPaddingSamples]);

        foreach (var (freq, durationMs) in notes)
        {
            var generator = new SignalGenerator(sampleRate, 1)
            {
                Type = SignalGeneratorType.Sin,
                Frequency = freq,
                Gain = 0.3,
            };

            var noteSamples = new float[sampleRate * durationMs / 1000];
            generator.Read(noteSamples, 0, noteSamples.Length);
            floatSamples.AddRange(noteSamples);
        }

        floatSamples.AddRange(new float[trailingPaddingSamples]);

        // Convert [-1, 1] float samples to 16-bit signed PCM.
        var pcmBytes = new byte[floatSamples.Count * sizeof(short)];
        for (var i = 0; i < floatSamples.Count; i++)
        {
            var clamped = Math.Clamp(floatSamples[i], -1f, 1f);
            var intSample = (short)(clamped * short.MaxValue);
            pcmBytes[i * 2] = (byte)(intSample & 0xFF);
            pcmBytes[i * 2 + 1] = (byte)((intSample >> 8) & 0xFF);
        }

        using var pcmStream = new MemoryStream(pcmBytes);
        using var rawReader = new RawSourceWaveStream(pcmStream, format);

        // Through the shared output (see AudioBus): one opened per effect
        // stalled the music for a moment before every chime. Guarded
        // because a machine may have no usable audio output at all -
        // headless, RDP, or a disabled device - and several callers fire
        // these with a bare "_ =", so an exception here would surface on a
        // background thread with nothing catching it and take the process
        // down. No sound is a degraded race day, a crash is a lost one.
        try
        {
            await AudioBus.PlayAsync(rawReader.ToSampleProvider(), Math.Clamp(volume, 0f, 1f), cancellationToken);

            if (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(60);
            }
        }
        catch
        {
            // Swallowed deliberately - see above.
        }
    }

    /// <summary>Plays an arbitrary local audio file (wav, mp3, etc.) via NAudio's format-sniffing reader.</summary>
    public static Task PlayFileAsync(string path, float volume) =>
        PlayFileAsync(path, volume, CancellationToken.None);

    /// <summary>
    /// Plays a local audio file that can be cut off part-way.
    ///
    /// A custom start or finish sound can be any length - a whole victory
    /// song - and the announcer's flag calls must be able to stop it rather
    /// than wait for it to end.
    ///
    /// <paramref name="leadInSeconds"/> is silence played BEFORE the file,
    /// for the same reason the generated tones carry 0.9s of it (see
    /// PlayNotesAsync): a wireless speaker wakes up on the first fraction of
    /// a second it is sent and swallows it. Files used to get none, so a
    /// short chime from a file could vanish entirely where the same chime
    /// generated here played fine.
    ///
    /// <paramref name="semitones"/> raises (or lowers) the whole file - the
    /// streak sound climbs with the streak this way.
    /// </summary>
    public static async Task PlayFileAsync(string path, float volume, CancellationToken cancellationToken, double leadInSeconds = 0, double semitones = 0)
    {
        // Same guard as above, plus the file itself: a custom sound whose
        // path has gone (moved drive, cleared folder) would otherwise throw
        // from a fire-and-forget call.
        if (!File.Exists(path))
        {
            return;
        }

        AudioFileReader reader;
        try
        {
            reader = new AudioFileReader(path) { Volume = Math.Clamp(volume, 0f, 1f) };
        }
        catch
        {
            // Not audio, or not a format this PC can decode.
            return;
        }

        using var readerScope = reader;

        ISampleProvider chain = reader;

        if (Math.Abs(semitones) > 0.01)
        {
            // Told it was recorded at a higher rate, then resampled back to
            // the real one: the same samples, played faster - higher.
            var rate = reader.WaveFormat.SampleRate;
            var retagged = (int)Math.Round(rate * Math.Pow(2, semitones / 12.0));
            chain = new WdlResamplingSampleProvider(new RetaggedRate(chain, retagged), rate);
        }

        // The wake-up silence callers ask for is no longer needed - the
        // shared output never sleeps - so no more than a hair of it.
        leadInSeconds = Math.Min(leadInSeconds, AudioBus.LeadInSeconds);

        if (leadInSeconds > 0)
        {
            chain = new OffsetSampleProvider(chain) { DelayBy = TimeSpan.FromSeconds(leadInSeconds) };
        }

        // Through the shared output, like the tones - see AudioBus. Every
        // caller is fire-and-forget, and a speaker can go away mid-race.
        try
        {
            await AudioBus.PlayAsync(chain, 1f, cancellationToken);

            if (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(120);
            }
        }
        catch
        {
            // Swallowed deliberately - no sound beats no app.
        }
    }

    /// <summary>
    /// The generated tone for an effect - "Classic tones". One place for the
    /// switch the Sound Effects page, the Mixer and the soundscape previews
    /// all need.
    /// </summary>
    public static Task PlayGeneratedAsync(SoundEffectKind kind, float volume, int streakCount = 3, CancellationToken cancellationToken = default) => kind switch
    {
        SoundEffectKind.GoodLap => PlayGoodLapAsync(volume, cancellationToken),
        SoundEffectKind.InvalidLap => PlayInvalidLapAsync(volume, cancellationToken),
        SoundEffectKind.FastLap => PlayFastLapAsync(volume, cancellationToken),
        SoundEffectKind.TrackRecord => PlayTrackRecordAsync(volume, cancellationToken),
        SoundEffectKind.Streak => PlayStreakAsync(streakCount, volume, cancellationToken),
        SoundEffectKind.LeaderChange => PlayLeaderChangeAsync(volume, cancellationToken),
        SoundEffectKind.RaceStaging => PlayRaceStagingAsync(volume, cancellationToken),
        SoundEffectKind.CountdownStarted => PlayCountdownStartedAsync(volume, cancellationToken),
        SoundEffectKind.RaceStarted => PlayRaceStartedAsync(volume, cancellationToken),
        SoundEffectKind.RaceTimeOver => PlayRaceTimeOverAsync(volume, cancellationToken),
        SoundEffectKind.RaceCompleted => PlayRaceCompletedAsync(volume, cancellationToken),
        SoundEffectKind.PersonalBest => PlayPersonalBestAsync(volume, cancellationToken),
        SoundEffectKind.CountdownTicks => PlayCountdownTicksAsync(volume, cancellationToken),
        SoundEffectKind.JumpStart => PlayJumpStartAsync(volume, cancellationToken),
        SoundEffectKind.YellowFlag => PlayYellowFlagAsync(volume, cancellationToken),
        SoundEffectKind.RedFlag => PlayRedFlagAsync(volume, cancellationToken),
        SoundEffectKind.GreenFlagResumed => PlayGreenFlagResumedAsync(volume, cancellationToken),

        // The invalid-lap buzzer, which a warning played before it was a sound of its own.
        SoundEffectKind.Warning => PlayInvalidLapAsync(volume, cancellationToken),
        _ => Task.CompletedTask,
    };

    /// <summary>Passes samples through untouched while claiming a different sample rate - half of a pitch shift.</summary>
    private sealed class RetaggedRate : ISampleProvider
    {
        private readonly ISampleProvider _source;

        public RetaggedRate(ISampleProvider source, int sampleRate)
        {
            _source = source;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, source.WaveFormat.Channels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count) => _source.Read(buffer, offset, count);
    }
}
