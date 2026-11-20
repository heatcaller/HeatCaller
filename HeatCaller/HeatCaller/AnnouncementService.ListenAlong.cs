using System.Collections.Concurrent;
using System.Text.RegularExpressions;
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
/// VOICE ONLY, NEVER MUSIC (15 Nov 2026, IP plan 3.4)
///
/// Only the announcer's own lines go out here - never the music bed, the
/// show music or a club's own songs. HeatCaller's music is its own, but a
/// club's songs are licensed (if at all) for its PA, and relaying them to
/// listeners at home would make HeatCaller the one transmitting them to the
/// public. The Terms (Part 1, 7.5) promise listen-along is voice only; keep
/// it that way, or the music licensing question lands on HeatCaller.
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

    /// <summary>
    /// Whether listeners are being sent the whole mix rather than the
    /// announcer's lines one at a time (2 Nov 2026 - see ListenStream). While
    /// it is, a line is not encoded and sent on its own: it is cleared or
    /// kept off air as it plays, and only its caption goes to
    /// <see cref="CaptionToListeners"/>. False, or unwired, is the line-by-line
    /// route, which a PC without an mp3 encoder for the stream still has.
    /// </summary>
    public Func<bool>? StreamingToListeners { get; set; }

    /// <summary>
    /// Where the words of a line cleared for the stream go, for the listen
    /// page to show as it plays. Must not block: it is called on the way to
    /// the speaker.
    /// </summary>
    public Action<string>? CaptionToListeners { get; set; }

    /// <summary>Whether a line's audio reaches listeners through the stream - see <see cref="StreamingToListeners"/>.</summary>
    private bool StreamingTheMix => ListenersWanted?.Invoke() == true && StreamingToListeners?.Invoke() == true;

    /// <summary>
    /// Raised with a line's words (no [audio tags]) the moment it is about to
    /// be heard - every voice, every kind of line. For the highlight videos'
    /// captions. On whatever thread the line plays from: a handler must be
    /// quick and must not block.
    /// </summary>
    public event Action<string>? LineHeard;

    /// <summary>Said once per reason rather than per line, the way the voice stand-in notices are.</summary>
    private int _listenEncodeComplained;

    /// <summary>
    /// Every transponder the announcer has said AS a name this run - a car
    /// with no profile, or a profile with no name (23 Oct 2026, bug hunt 10).
    /// Made on first use rather than with an initializer, because this is a
    /// part file. Static, like PublicPrivacy.Current, so the public captions
    /// document and the highlight videos can ask it too. Only ever grows: a
    /// car named later is said by its name, so its number in the set costs
    /// nothing.
    /// </summary>
    private static ConcurrentDictionary<string, byte>? _carsSpokenByNumber;

    /// <summary>
    /// Notes that <paramref name="spoken"/>, the name about to be said for
    /// <paramref name="transponder"/>, is the transponder itself.
    /// </summary>
    /// <remarks>
    /// WHY (23 Oct 2026, bug hunt 10): the room hears an unnamed car's
    /// number, which helps the desk find it - but the same line went to the
    /// public listen-along page, the public captions and the podium videos,
    /// and a transponder number must never go online. Nothing public could
    /// tell, because the privacy register never sees a car with no profile.
    /// Every place that falls back to the transponder for a name calls this,
    /// and the public gates ask <see cref="NamesACarByNumber"/>.
    /// </remarks>
    internal static void NoteSpokenAsNumber(string? transponder, string? spoken)
    {
        var id = (transponder ?? string.Empty).Trim();

        if (id.Length == 0 || !string.Equals((spoken ?? string.Empty).Trim(), id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        LazyInitializer.EnsureInitialized(
                ref _carsSpokenByNumber,
                () => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase))
            .TryAdd(id, 0);
    }

    /// <summary>
    /// Swaps the cars said by their number for <paramref name="with"/> and
    /// hands back the ones there were - for the tests, which share one run.
    /// </summary>
    /// <remarks>
    /// WHY (19 Nov 2026): tests that race a car whose transponder and name
    /// are both "Sam" noted "Sam" here for the rest of the run, so a later
    /// test's "Sam Parker takes the lead!" was withheld from listen-along -
    /// it passed alone and failed in the full suite. A test that checks
    /// what reaches listeners starts from an empty set and puts the old one
    /// back after.
    /// </remarks>
    internal static ConcurrentDictionary<string, byte>? SwapCarsSpokenByNumberForTests(ConcurrentDictionary<string, byte>? with) =>
        Interlocked.Exchange(ref _carsSpokenByNumber, with);

    /// <summary>
    /// Whether these words name a car by its transponder number - one the
    /// announcer has said as a name (<see cref="NoteSpokenAsNumber"/>). Such
    /// a line stays in the room: see the remarks there.
    /// </summary>
    internal static bool NamesACarByNumber(string? words) =>
        Volatile.Read(ref _carsSpokenByNumber) is { IsEmpty: false } cars
        && MentionsAnyId(words, cars.Keys);

    /// <summary>
    /// Whether any of <paramref name="ids"/> is in the words as a whole token
    /// ("4471234 sets the fastest lap", not "44712345"), whatever its case.
    /// Its own method so the rule can be tested.
    /// </summary>
    internal static bool MentionsAnyId(string? words, IEnumerable<string>? ids)
    {
        if (string.IsNullOrWhiteSpace(words) || ids is null)
        {
            return false;
        }

        foreach (var raw in ids)
        {
            var id = (raw ?? string.Empty).Trim();

            if (id.Length == 0)
            {
                continue;
            }

            var numeric = id.All(char.IsAsciiDigit);

            foreach (Match match in Regex.Matches(
                         words,
                         @"(?<![\p{L}\p{N}])" + Regex.Escape(id) + @"(?![\p{L}\p{N}])",
                         RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                // WHY (26 Oct 2026, asked for by Asher: "can't hear the heat
                // reminders that fire between races by listening through the
                // relay"): an id that is only digits was found in ANY number
                // in the line. A decoder whose ids are a few digits (an I-Lap
                // id loses its leading zeros: "0004567" is "4567") turned
                // one unnamed car said by its number into every later line
                // with that number in it kept off listen-along, the public
                // captions and the podium videos for the rest of the run -
                // "5 minutes to Stock Q1 Heat 2.", "Heat 3 to the drivers
                // stand", "Lap 5". Such a line has the number where no name
                // ever goes - straight after "heat", "round" and the like,
                // before "minutes" or "point", or inside a time - so those
                // are passed over and every other place still counts. Not
                // after "lap": "about to lap {rival}" puts a name there.
                if (numeric && NumberIsACount(words, match.Index, match.Length))
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>The words a number follows when it labels a race or a place in one, never a car: "Heat 3", "Round 2".</summary>
    private static readonly HashSet<string> NumberLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "heat", "race", "round", "main", "final", "qualifier", "qualifying", "group", "session", "position",
    };

    /// <summary>The words a number goes before when it is an amount, never a car: "5 minutes", "6 point 9".</summary>
    private static readonly HashSet<string> NumberUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "point", "minute", "minutes", "seconds",
    };

    /// <summary>
    /// Whether the number at <paramref name="index"/> in the words is a
    /// count or a label rather than a car's name - see the WHY in
    /// <see cref="MentionsAnyId"/>. Its own method so the rule can be tested.
    /// </summary>
    internal static bool NumberIsACount(string words, int index, int length)
    {
        var end = index + length;

        // Part of a time or a decimal: "7:30", "6.94".
        if ((index >= 2 && (words[index - 1] is '.' or ':') && char.IsDigit(words[index - 2]))
            || (end + 1 < words.Length && (words[end] is '.' or ':') && char.IsDigit(words[end + 1])))
        {
            return true;
        }

        var before = words[..index].TrimEnd();
        var wordBefore = before[(before.LastIndexOf(' ') + 1)..];

        if (wordBefore.Length > 0 && NumberLabels.Contains(wordBefore))
        {
            return true;
        }

        var after = words[end..].TrimStart().Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
        var next = after.Length > 0 ? after[0].TrimEnd('.', ',', '!', '?', ';', ':') : string.Empty;

        if (NumberUnits.Contains(next))
        {
            return true;
        }

        // "3 laps to go", "1 lap to go".
        return next is "lap" or "laps" or "Lap" or "Laps"
               && after.Length >= 3
               && string.Equals(after[1], "to", StringComparison.OrdinalIgnoreCase)
               && after[2].TrimEnd('.', ',', '!', '?', ';', ':').Equals("go", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether the audio a synthesis handed back was rendered from the line
    /// with names SHORTENED (AnnouncementService.ShortNames) - the HeatCaller
    /// voice, ElevenLabs, or a take saved under the shortened words. Not
    /// Kokoro, not a Kokoro stand-in (both given the line as written), and not
    /// a soundscape recording, which says what was recorded. For
    /// <see cref="OfferLineToListeners"/>'s audioShortened (23 Oct 2026, bug
    /// hunt 10).
    /// </summary>
    ///
    /// WHY (24 Oct 2026): not while "Say full names aloud with the online
    /// voices" is on - those voices were then sent the names in full, so their
    /// audio says them, and the line is held back as a Kokoro one is. Read as
    /// the line is offered; a switch flipped while a line is being rendered
    /// can misjudge that one line.
    private bool AudioFromShortenedText(LineSource source, string text) =>
        ShortensForCloudVoice()
        && (source is LineSource.HeatCaller or LineSource.ElevenLabs
            || (source == LineSource.Library && !StingerLibrary.Shared.Has(text)));

    /// <summary>
    /// Offers a line to listeners at home, and returns at once.
    ///
    /// Called from the audio path. See the class summary for why this does
    /// nothing but hand the work to a pool thread.
    /// </summary>
    /// <param name="audioShortened">
    /// Whether <paramref name="audio"/> says the names shortened, as
    /// <see cref="AudioFromShortenedText"/> works out; null when the caller
    /// cannot tell. Only matters while the club shortens published names -
    /// see OfferToListenersOnly.
    /// </param>
    /// <returns>Whether the line is heard on the listen stream - PlayPcmAsync's onAir. See <see cref="OfferToListenersOnly"/>.</returns>
    private bool OfferLineToListeners(string text, byte[]? audio, bool? audioShortened = null)
    {
        // THE CAPTIONS FIRST (21 Oct 2026), and whether or not anyone is
        // listening from home: this is the one point where a line is
        // definitely about to be heard, which is exactly when a highlight
        // video's caption should start. See RaceAudioRecorder.NoteLine.
        NoteHeard(text);

        return OfferToListenersOnly(text, audio, audioShortened);
    }

    /// <summary>
    /// Raises <see cref="LineHeard"/> for a line about to be heard - on its
    /// own (22 Oct 2026, bug hunt), so a path that only offers a line to
    /// listeners when someone is listening (the driver release calls) still
    /// gives the videos their caption.
    /// </summary>
    private void NoteHeard(string text)
    {
        if (LineHeard is { } heard)
        {
            try
            {
                var shown = Starred(VoiceProfile.StripAudioTags(text));

                if (!string.IsNullOrWhiteSpace(shown))
                {
                    heard(shown);
                }
            }
            catch
            {
                // A caption is never worth a line of the broadcast.
            }
        }
    }

    /// <summary>The listeners' half of <see cref="OfferLineToListeners"/>, without the caption.</summary>
    /// <returns>
    /// Whether the line's audio is heard on the listen stream (2 Nov 2026 -
    /// see ListenStream). False whenever the whole mix is not being streamed,
    /// as nothing is heard on a stream that is not running; the line-by-line
    /// route sends the line itself.
    /// </returns>
    private bool OfferToListenersOnly(string text, byte[]? audio, bool? audioShortened = null)
    {
        // The cheapest check first, and before the Task.Run rather than
        // inside it: a club with this off should not even be scheduling
        // work, and most clubs have it off.
        if (ListenersWanted?.Invoke() != true)
        {
            return false;
        }

        // Without the [excited]-style tags a v3 voice keeps in the text it
        // is given: they are directions to the voice, and the page shows
        // these words as the caption.
        var words = Starred(VoiceProfile.StripAudioTags(text));

        // The privacy register as it is the moment the line is heard - read
        // here, a volatile field read, so a register swapped while this one
        // is encoding can't let it through half-checked (22 Oct 2026, driver
        // experience, item 12).
        var privacy = PublicPrivacy.Current;

        if (StreamingToListeners?.Invoke() == true)
        {
            // WHY HERE AND NOT ON A POOL THREAD (2 Nov 2026): the audio
            // goes out as the room hears it, so whether it is muted from
            // the stream has to be known before the speaker starts. The
            // same checks as below - a few regex passes over one line,
            // where the slow part the pool thread exists for is the encode,
            // which the stream does elsewhere. Off air if anything fails:
            // it errs on the private side, as the rule does.
            try
            {
                if (CaptionIfCleared(words, audioShortened, privacy) is not { } cleared)
                {
                    return false;
                }

                if (cleared.Length > 0)
                {
                    CaptionToListeners?.Invoke(cleared);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        if (SendToListeners is null || audio is null || audio.Length == 0)
        {
            return false;
        }

        var bytes = audio;

        // Fire and forget, on purpose, with its own catch: this is a
        // courtesy to people who are not at the track, and nothing it can
        // do is allowed to reach the race. Faulted and never observed
        // would be a process-level unhandled exception on finalisation.
        _ = Task.Run(() =>
        {
            try
            {
                // Checked on this pool thread, not before Task.Run, because
                // it is a regex pass and nothing slow happens on the way to
                // the speaker.
                if (CaptionIfCleared(words, audioShortened, privacy) is not { } caption)
                {
                    return;
                }

                var mp3 = AsMp3(bytes);

                if (mp3 is not null)
                {
                    SendToListeners?.Invoke(caption, mp3);

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

        return false;
    }

    /// <summary>
    /// What listeners at home may be shown for a line - or null when the line
    /// must not reach them at all, words or audio. The one rule for both the
    /// line-by-line route and the stream.
    /// </summary>
    internal string? CaptionIfCleared(string words, bool? audioShortened, PublicPrivacy privacy)
    {
        // WITHHELD, NOT REDACTED (22 Oct 2026, driver experience, item 12).
        // A driver who asked to be initials-only online can't be bleeped out
        // of audio, so a line that mentions them - by any name, or any word
        // of one - is not sent to listeners at home at all. The room still
        // hears it; the public captions leave it out too. It errs on the
        // private side: another driver who shares that first name is
        // withheld too. Race control's warning and black-flag calls are
        // withheld as well (22 Oct 2026, security review): they say the
        // penalty's reason aloud, and reasons stay in the room and the
        // driver's own night.
        if (WithheldFromListeners(words, privacy))
        {
            return null;
        }

        // WHY (23 Oct 2026, bug hunt 10): a line naming a car by its
        // transponder number - a car nobody named - went to the public page
        // as words and audio. Withheld, not rewritten, because the audio says
        // the number. See NoteSpokenAsNumber.
        if (NamesACarByNumber(words))
        {
            return null;
        }

        // WHY (23 Oct 2026, bug hunt 10): "Shorten published names" (on by
        // default) never reached this page - the caption was the line as
        // written, full names and all, and with Kokoro the audio said them
        // too. The caption is now the shortened line, and a line whose names
        // it shortens goes out only when its audio was rendered from the
        // shortened words; otherwise it is withheld, the way an initials-only
        // line is.
        //
        // And a junior's name (15 Nov 2026, children's privacy plan 1.3):
        // shortened in the caption whatever the club's setting, by the same
        // rule, so with "Shorten published names" off a line saying a
        // junior's full name is withheld rather than heard at home in full.
        var caption = (privacy ?? PublicPrivacy.Empty).Redact(TextForPublicPages(words));

        if (!string.Equals(caption, words, StringComparison.Ordinal) && audioShortened != true)
        {
            return null;
        }

        return caption;
    }

    /// <summary>
    /// Whether a line must not go to listeners at home because it mentions
    /// a driver shown online by initials only (22 Oct 2026, driver
    /// experience, item 12), or is one of race control's warning or
    /// black-flag calls (22 Oct 2026, security review). Its own method so
    /// the rule can be tested without a voice.
    /// </summary>
    internal static bool WithheldFromListeners(string? words, PublicPrivacy? privacy) =>
        IsRaceControlCall(words)
        || (privacy ?? PublicPrivacy.Empty).MentionsWithheld(words);

    /// <summary>
    /// Whether a line is one of race control's warning or black-flag calls,
    /// which say the penalty's reason aloud - "Warning, Sam. Blocking."
    /// (RaceControlControl.OnWarningIssued, spoken through NotifyWarning).
    /// Kept off everything public - the captions document and listen-along -
    /// while the room still hears and sees it (22 Oct 2026, security review).
    /// One rule, here, so the two can't drift apart.
    /// </summary>
    internal static bool IsRaceControlCall(string? text)
    {
        var line = (text ?? string.Empty).TrimStart();

        return line.StartsWith("Warning,", StringComparison.OrdinalIgnoreCase)
               || line.StartsWith("Black flag,", StringComparison.OrdinalIgnoreCase);
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
