using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace HeatCaller;

/// <summary>
/// Handing the announcer's voice to anyone listening from home.
///
/// WHERE THIS SITS (13 Oct 2026)
///
/// One call, from the one place in the app where a line is definitely
/// about to be heard: the moment before
/// <see cref="SynthesizeAndPlayAsync"/> hands bytes to the speaker. Not
/// when the line was queued - half of those are dropped by a flag, a
/// countdown or a mute - and not when it was rendered, because a rendered
/// line can still be thrown away. A listener at home hears what the room
/// hears, and nothing the room did not.
///
/// NOTHING SLOW HAPPENS ON THE WAY TO THE SPEAKER
///
/// The hand-off is a Task.Run and a return. Everything expensive - the
/// sniff, the encode, the base64, the network - happens on a pool thread
/// afterwards. A feature that made the announcer a beat late would be a
/// feature every club switched off, and the lap call being on time is the
/// entire job of this application.
///
/// WHY THERE IS AN ENCODER HERE AT ALL
///
/// ElevenLabs sends mp3 and that can go as it is. Kokoro - the free voice,
/// which is what a club with no API key uses, which is most of them -
/// renders wav. A wav of a three-second line is about 260 KB and the relay
/// takes 256 KB for one document, so sending it raw would have failed for
/// exactly the clubs most likely to want this. So it is encoded, through
/// the same Media Foundation route the race recordings use
/// (RaceAudioRecorder.TryEncode), on a thread where taking a moment costs
/// nobody anything.
/// </summary>
public sealed partial class AnnouncementService
{
    /// <summary>
    /// Whether anybody may listen - checked before a line is encoded, so a
    /// club that has this switched off pays nothing for it.
    ///
    /// Null until MainForm wires it, and then nothing is offered, which is
    /// the right way round for a feature that puts a club's voice on a
    /// public page.
    /// </summary>
    public Func<bool>? ListenersWanted { get; set; }

    /// <summary>
    /// Where a line goes once it is an mp3 - the relay publisher's queue.
    ///
    /// Takes what was said and the audio. Must not block: it is called on
    /// a pool thread, but a thread that blocks here is one that is not
    /// encoding the next line.
    /// </summary>
    public Action<string, byte[]>? SendToListeners { get; set; }

    /// <summary>Said once per reason rather than per line, the way the voice stand-in notices are.</summary>
    private int _listenEncodeComplained;

    /// <summary>
    /// Offers a line to listeners at home, and returns at once.
    ///
    /// Called from the audio path. See the class summary for why this does
    /// nothing but hand the work to a pool thread.
    /// </summary>
    private void OfferLineToListeners(string text, byte[]? audio)
    {
        if (SendToListeners is null || audio is null || audio.Length == 0)
        {
            return;
        }

        // The cheapest check first, and before the Task.Run rather than
        // inside it: a club with this off should not even be scheduling
        // work, and most clubs have it off.
        if (ListenersWanted?.Invoke() != true)
        {
            return;
        }

        // Without the [excited]-style tags a v3 voice keeps in the text it
        // is given: they are directions to the voice, and the page shows
        // these words as the caption.
        var words = VoiceProfile.StripAudioTags(text);
        var bytes = audio;

        // Fire and forget, on purpose, with its own catch: this is a
        // courtesy to people who are not at the track, and nothing it can
        // do is allowed to reach the race. Faulted and never observed
        // would be a process-level unhandled exception on finalisation.
        _ = Task.Run(() =>
        {
            try
            {
                var mp3 = AsMp3(bytes);

                if (mp3 is not null)
                {
                    SendToListeners?.Invoke(words, mp3);

                    // A line got through, so a later failure is news again
                    // (17 Oct 2026, Bug Hunt 8, A06-11): one early hiccup
                    // used to silence every complaint for the rest of the run.
                    Volatile.Write(ref _listenEncodeComplained, 0);
                }
            }
            catch (Exception ex)
            {
                ComplainAboutListenAudioOnce(ex.Message);
            }
        });
    }

    /// <summary>
    /// Whatever a synthesis handed back, as an mp3 - or null when it
    /// cannot be made into one.
    ///
    /// An mp3 is passed straight through. Anything else is decoded and
    /// re-encoded. Null means this line does not go out and the next one
    /// will be tried as if nothing happened: a club whose PC has no mp3
    /// encoder loses listening along and loses nothing else.
    /// </summary>
    private byte[]? AsMp3(byte[] audio)
    {
        if (VoiceLibrary.SniffAudio(audio) == VoiceLibrary.SavedAudioKind.Mp3)
        {
            return audio;
        }

        // Written to disk and read back, because MediaFoundationEncoder
        // writes to a path. Both files go in the temp folder and both are
        // deleted in the finally - on a race night this runs a few hundred
        // times, and a few hundred small files left behind would be this
        // feature's legacy.
        var wavPath = Path.Combine(Path.GetTempPath(), "heatcaller-listen-" + Guid.NewGuid().ToString("N") + ".wav");
        var mp3Path = Path.ChangeExtension(wavPath, ".mp3");

        try
        {
            if (VoiceLibrary.SniffAudio(audio) == VoiceLibrary.SavedAudioKind.Wav)
            {
                File.WriteAllBytes(wavPath, audio);
            }
            else
            {
                // Neither a wav nor an mp3. Decoded through the same
                // routine playback uses, so whatever it is, it is the same
                // audio the room heard.
                var (pcm, format) = DecodeAudio(audio);

                using var writer = new WaveFileWriter(wavPath, format);
                writer.Write(pcm, 0, pcm.Length);
            }

            if (!TryEncodeForListeners(wavPath, mp3Path, out var why))
            {
                ComplainAboutListenAudioOnce(why);
                return null;
            }

            var mp3 = File.ReadAllBytes(mp3Path);

            // Judged here as well as in ListenAlong.Offer, so the reason a
            // long line did not go out is said in words rather than
            // counted. The recap is the line this catches.
            if (mp3.Length > ListenAlong.MostBytes)
            {
                StatusMessage?.Invoke(
                    $"[listen] A {mp3.Length / 1024} KB line was too long to send to listeners - "
                    + ListenAlong.Because(ListenAlong.Verdict.TooLong) + ".");

                return null;
            }

            return mp3;
        }
        finally
        {
            TryDeleteQuietly(wavPath);
            TryDeleteQuietly(mp3Path);
        }
    }

    /// <summary>
    /// A wav to an mp3, for listeners.
    ///
    /// The same shape as RaceAudioRecorder.TryEncode and for the same
    /// reason: MediaFoundationEncoder only takes an output format the
    /// installed encoder actually offers at exactly that rate and channel
    /// count, and "mono 44.1kHz mp3" is not offered on every Windows
    /// build. Not shared with that one because the bit rates wanted are
    /// different - this is paying for a 256 KB document and goes LOW
    /// first, where a kept recording goes high first.
    /// </summary>
    private static bool TryEncodeForListeners(string wavPath, string mp3Path, out string why)
    {
        why = string.Empty;

        try
        {
            MediaFoundationApi.Startup();
        }
        catch (Exception ex)
        {
            why = $"Windows Media Foundation is not available ({ex.Message}), so the announcer cannot be sent to listeners";
            return false;
        }

        // Lowest first. A listener on a phone is not judging the fidelity
        // of a lap call, and every kilobyte saved is more of a long line
        // that fits.
        foreach (var bitRate in new[] { 48_000, 64_000, 96_000 })
        {
            try
            {
                using var reader = new WaveFileReader(wavPath);
                MediaFoundationEncoder.EncodeToMp3(AtAnMp3Rate(reader), mp3Path, bitRate);
                return true;
            }
            catch (Exception ex)
            {
                why = ex.Message;
                TryDeleteQuietly(mp3Path);
            }
        }

        try
        {
            using var reader = new WaveFileReader(wavPath);
            var stereo = new MonoToStereoSampleProvider(AtAnMp3Rate(reader.ToSampleProvider()));

            MediaFoundationEncoder.EncodeToMp3(new SampleToWaveProvider16(stereo), mp3Path, 64_000);

            return true;
        }
        catch (Exception ex)
        {
            why = ex.Message;
            TryDeleteQuietly(mp3Path);
        }

        return false;
    }

    /// <summary>
    /// The sample rates the Windows mp3 encoder takes. It is an MPEG-1
    /// Layer III encoder and offers nothing else, so a wav at any other
    /// rate fails every attempt with "no suitable encoders".
    /// </summary>
    internal static bool IsAnMp3Rate(int sampleRate) => sampleRate is 32_000 or 44_100 or 48_000;

    /// <summary>
    /// The wav as it is when the encoder takes its rate, and otherwise
    /// resampled to 48 kHz.
    ///
    /// KOKORO IS THE ONE THIS IS FOR. It renders at 24 kHz, which the
    /// encoder refuses, so without this every line of the free voice -
    /// the voice most clubs have - went nowhere. A wav already at an mp3
    /// rate is passed through untouched, so the ElevenLabs path is
    /// exactly what it was.
    /// </summary>
    private static IWaveProvider AtAnMp3Rate(WaveFileReader reader)
    {
        if (IsAnMp3Rate(reader.WaveFormat.SampleRate))
        {
            return reader;
        }

        return new SampleToWaveProvider16(AtAnMp3Rate(reader.ToSampleProvider()));
    }

    /// <summary>The same, for samples - see the overload above.</summary>
    internal static ISampleProvider AtAnMp3Rate(ISampleProvider samples)
    {
        return IsAnMp3Rate(samples.WaveFormat.SampleRate)
            ? samples
            : new WdlResamplingSampleProvider(samples, 48_000);
    }

    /// <summary>
    /// Says it once.
    ///
    /// A PC with no mp3 encoder fails on every line of the evening, and a
    /// console with four hundred copies of the same sentence in it is a
    /// console nobody reads - the same reason LogStandInOnce exists for
    /// the voice itself.
    /// </summary>
    private void ComplainAboutListenAudioOnce(string why)
    {
        if (Interlocked.Exchange(ref _listenEncodeComplained, 1) != 0)
        {
            return;
        }

        StatusMessage?.Invoke(
            "[listen] Nothing is being sent to anyone listening along: " + why + ". "
            + "Everything else about the night is unaffected - untick \"Let people listen along\" "
            + "on the Club Website page to stop it trying.");
    }

    /// <summary>A temp file that is already gone, or locked, is not worth a word.</summary>
    private static void TryDeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temp. Windows will have it.
        }
    }
}
