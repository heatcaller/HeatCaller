using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// A spoken line played while it is still arriving from ElevenLabs (10 Nov
/// 2026, voice speed plan, phase 3) - the audio bus's source for it.
/// </summary>
/// <remarks>
/// WHY. A whole-clip render is about 0.8 s on eleven_v4_turbo, and the room
/// hears nothing until all of it is in. ElevenLabs' stream endpoint sends the
/// first audio sooner; played as it comes, a call starts that much earlier.
///
/// HOW IT ENDS. The bus takes a source that reads SHORT of what was asked as
/// finished (AudioBus.Clip). So until the download is complete this always
/// reads in full - what has arrived, then silence for the rest - and only once
/// it is complete and drained does it read short and leave the mix. A gap
/// while waiting is heard as a pause, and counted (Underruns), so a slow
/// connection shows in the console rather than as a mystery.
///
/// Raw 16-bit mono PCM in (ElevenLabs' pcm_22050), floats out, with the
/// line's gain applied as it is added - the whole-clip levelling
/// (VoiceLevel) needs the whole clip, which is the one thing a stream does
/// not have; see VoiceLevelMemory.
/// </remarks>
internal sealed class StreamingVoiceLine : ISampleProvider
{
    /// <summary>The format asked of ElevenLabs, and what it sends: 22.05 kHz, 16-bit, mono.</summary>
    internal const string ElevenLabsFormat = "pcm_22050";

    internal static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(22_050, 1);

    private readonly object _gate = new();
    private readonly Queue<float> _samples = new();
    private readonly float _gain;

    private bool _complete;
    private bool _gotAudio;
    private int _oddByte = -1;
    private long _samplesAdded;

    /// <param name="gain">The line's level, as a multiplier - see VoiceLevelMemory.</param>
    /// <param name="leadingSilenceSeconds">Silence in front of the words, as every line has.</param>
    internal StreamingVoiceLine(float gain, double leadingSilenceSeconds)
    {
        _gain = gain;

        for (var i = 0; i < (int)(Format.SampleRate * Math.Max(0, leadingSilenceSeconds)); i++)
        {
            _samples.Enqueue(0f);
        }
    }

    public WaveFormat WaveFormat => Format;

    /// <summary>Times the bus read past what had arrived, before the end - each one a gap heard as silence.</summary>
    internal int Underruns { get; private set; }

    /// <summary>The words' length so far, from the audio that has arrived.</summary>
    internal TimeSpan SpokenFor
    {
        get
        {
            lock (_gate)
            {
                return TimeSpan.FromSeconds(_samplesAdded / (double)Format.SampleRate);
            }
        }
    }

    /// <summary>
    /// Raw 16-bit little-endian PCM as it arrives - in pieces of any size, a
    /// sample split across two pieces included.
    /// </summary>
    internal void Add(ReadOnlySpan<byte> pcm)
    {
        lock (_gate)
        {
            var at = 0;

            if (_oddByte >= 0 && pcm.Length > 0)
            {
                Push((short)(_oddByte | (pcm[0] << 8)));
                _oddByte = -1;
                at = 1;
            }

            for (; at + 1 < pcm.Length; at += 2)
            {
                Push((short)(pcm[at] | (pcm[at + 1] << 8)));
            }

            if (at < pcm.Length)
            {
                _oddByte = pcm[at];
            }

            if (pcm.Length > 0)
            {
                _gotAudio = true;
            }
        }
    }

    private void Push(short sample)
    {
        _samples.Enqueue(Math.Clamp(sample / 32768f * _gain, -1f, 1f));
        _samplesAdded++;
    }

    /// <summary>The download is over - all of it, or cut short. Adds the line's tail of silence; from here it reads short once drained.</summary>
    internal void Complete(double trailingSilenceSeconds)
    {
        lock (_gate)
        {
            if (_complete)
            {
                return;
            }

            for (var i = 0; i < (int)(Format.SampleRate * Math.Max(0, trailingSilenceSeconds)); i++)
            {
                _samples.Enqueue(0f);
            }

            _complete = true;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (_gate)
        {
            var read = 0;

            while (read < count && _samples.Count > 0)
            {
                buffer[offset + read++] = _samples.Dequeue();
            }

            if (read == count || _complete)
            {
                return read;
            }

            // Still arriving: silence for the rest, so the bus keeps the line.
            if (_gotAudio)
            {
                Underruns++;
            }

            Array.Clear(buffer, offset + read, count - read);

            return count;
        }
    }
}

/// <summary>
/// The level each voice's lines come in at, learned from its whole clips,
/// for the lines that are streamed (10 Nov 2026).
/// </summary>
/// <remarks>
/// A whole clip is measured and brought to VoiceLevel.TargetDb before it is
/// played. A stream can't be - its end hasn't arrived - so it is given the
/// gain the same voice's last whole clips needed. A voice that has not had
/// one yet is not streamed: its first lines play whole, as always, and teach
/// this its level.
/// </remarks>
internal static class VoiceLevelMemory
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, double> LevelDb = new(StringComparer.Ordinal);

    /// <summary>How much the remembered level follows each new clip - an average over the last few, so one odd line does not move it far.</summary>
    internal const double Follow = 0.3;

    /// <summary>The most a stream is made louder or quieter.</summary>
    internal const double MostGainDb = 9;

    internal static string KeyFor(VoiceProfile profile) => $"{profile.VoiceId}\n{profile.ModelId}";

    /// <summary>A whole clip of this voice measured at <paramref name="speechDb"/>.</summary>
    internal static void Learn(VoiceProfile profile, double speechDb)
    {
        var key = KeyFor(profile);

        lock (Gate)
        {
            LevelDb[key] = LevelDb.TryGetValue(key, out var was) ? was + Follow * (speechDb - was) : speechDb;
        }
    }

    /// <summary>The gain to bring this voice's stream to the target, or null when it has no whole clip to go by yet.</summary>
    internal static float? GainFor(VoiceProfile profile)
    {
        lock (Gate)
        {
            if (!LevelDb.TryGetValue(KeyFor(profile), out var level))
            {
                return null;
            }

            var db = Math.Clamp(VoiceLevel.TargetDb - level, -MostGainDb, MostGainDb);

            return (float)Math.Pow(10, db / 20);
        }
    }

    /// <summary>For the tests.</summary>
    internal static void Forget()
    {
        lock (Gate)
        {
            LevelDb.Clear();
        }
    }
}
