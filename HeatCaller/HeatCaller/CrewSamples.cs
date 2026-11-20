using NAudio.MediaFoundation;
using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// SAMPLES OF THE HEATCALLER AND KOKORO VOICES FOR DRIVERS' PHONES (19 Nov
/// 2026, driver crew plan 2.1: "Kokoro and HeatCaller samples are rendered
/// once on the club PC and stored on the relay").
///
/// An ElevenLabs voice comes with its own preview clip from the account's
/// voice list, so its Play button already worked. The other two engines
/// have none a phone can reach: a HeatCaller voice's free sample is on the
/// relay under the voice's own id, which the phone is never told, and a
/// Kokoro voice exists only on this PC. So each is got once - the
/// HeatCaller voice's free sample fetched (nothing charged), the Kokoro
/// voice rendered here - kept on this PC, and sent to the relay once under
/// the voice's crew key (DriverCrew.KeyOf), which is all the phone knows
/// it by. A voice already on the relay is not sent again.
/// </summary>
internal static class CrewSamples
{
    /// <summary>What a Kokoro voice says for its sample. No names, nothing about anybody.</summary>
    public const string Line = "Hi, I'm on your crew tonight. Great drive out there - let's go and get them in the main.";

    /// <summary>The relay's own limit on one sample (CREW_SAMPLE_MAX_BYTES).</summary>
    public const int MaxBytes = 512 * 1024;

    /// <summary>The most samples sent in one go, so a long list is spread over a few listings.</summary>
    public const int MostPerPass = 40;

    /// <summary>Where they are kept on this PC: derived audio, so the local folder, not the backed-up one.</summary>
    public static string Folder => Path.Combine(AppPaths.LocalRoot, "crew-samples");

    /// <summary>Whether samples for this engine's voices are made here - not ElevenLabs, whose come with the account.</summary>
    public static bool MadeHere(VoiceEngine? engine) => engine is VoiceEngine.HeatCaller or VoiceEngine.Kokoro;

    /// <summary>The voices whose sample is still to go to the relay: made here, no sample of their own, not there yet.</summary>
    public static IReadOnlyList<CrewVoiceOption> StillToSend(IEnumerable<CrewVoiceOption> voices, IReadOnlySet<string> onRelay) =>
        voices
            .Where(v => DriverCrew.IsAPick(v.Pick) && MadeHere(DriverCrew.EngineOf(v.Pick)))
            .Where(v => string.IsNullOrWhiteSpace(v.Sample) && !onRelay.Contains(DriverCrew.KeyOf(v.Pick)))
            .ToList();

    /// <summary>The voices as the phone is told them: each one whose sample is on the relay marked so.</summary>
    public static IReadOnlyList<CrewVoiceOption> Marked(IReadOnlyList<CrewVoiceOption> voices, IReadOnlySet<string>? onRelay) =>
        onRelay is null || onRelay.Count == 0
            ? voices
            : voices
                .Select(v => string.IsNullOrWhiteSpace(v.Sample) && onRelay.Contains(DriverCrew.KeyOf(v.Pick)) ? v with { OnRelay = true } : v)
                .ToList();

    /// <summary>Whether a key is a crew voice key: twelve lower-case hex characters.</summary>
    public static bool IsAKey(string? key) => key is { Length: 12 } && key.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string FileOf(string key) =>
        IsAKey(key) ? Path.Combine(Folder, key + ".mp3") : throw new ArgumentException("Not a crew voice key.", nameof(key));

    /// <summary>A sample kept on this PC from before, or null.</summary>
    public static byte[]? Kept(string key)
    {
        try
        {
            var path = FileOf(key);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Keeps a sample on this PC, so it is never rendered twice. A failure only means it is made again.</summary>
    public static void Keep(string key, byte[] mp3)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var path = FileOf(key);
            var temp = path + ".part";
            File.WriteAllBytes(temp, mp3);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Made again next time.
        }
    }

    /// <summary>
    /// A voice's sample, as MP3 bytes: kept from before, or made now and kept.
    /// </summary>
    /// <param name="heatCallerSample">The relay's free sample of a HeatCaller voice, by its id (HeatCallerVoice.SampleAsync).</param>
    /// <param name="kokoroRender">A line rendered in a Kokoro voice as a WAV (KokoroSpeech.RenderAsync), by the voice's name.</param>
    public static async Task<byte[]> SampleOfAsync(
        CrewVoicePick pick,
        Func<string, CancellationToken, Task<byte[]>> heatCallerSample,
        Func<string, string, CancellationToken, Task<byte[]>> kokoroRender,
        CancellationToken cancel = default)
    {
        var key = DriverCrew.KeyOf(pick);

        if (Kept(key) is { Length: > 0 } kept)
        {
            return kept;
        }

        byte[] mp3 = DriverCrew.EngineOf(pick) switch
        {
            VoiceEngine.HeatCaller => await heatCallerSample(pick.Id.Trim(), cancel).ConfigureAwait(false),
            VoiceEngine.Kokoro => WavToMp3(await kokoroRender(pick.Id.Trim(), Line, cancel).ConfigureAwait(false)),
            _ => throw new InvalidOperationException("ElevenLabs voices have their own sample."),
        };

        if (mp3.Length == 0 || mp3.Length > MaxBytes)
        {
            throw new InvalidOperationException(mp3.Length == 0 ? "the sample came out empty" : "the sample came out too big");
        }

        Keep(key, mp3);
        return mp3;
    }

    /// <summary>A WAV as MP3 bytes, through Windows Media Foundation as the race recordings are.</summary>
    public static byte[] WavToMp3(byte[] wav)
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".tmp.mp3");

        try
        {
            MediaFoundationApi.Startup();

            using (var reader = new WaveFileReader(new MemoryStream(wav)))
            {
                MediaFoundationEncoder.EncodeToMp3(reader, path, 64_000);
            }

            return File.ReadAllBytes(path);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A stray temp file; the next pass's is named afresh.
            }
        }
    }
}
