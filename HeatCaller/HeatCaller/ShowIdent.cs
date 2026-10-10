namespace HeatCaller;

/// <summary>
/// The club's voice-ID: one line, in their own voice, saying whose station
/// this is. "You're listening to Thunder Valley Raceway."
///
/// WHY THIS IS WORTH A FILE
///
/// It is the cheapest thing in the whole show treatment. A club already has
/// an opener, a bed and an announcer; what they do not have is the one
/// sentence every real station says and no app ever does. Typed once, kept
/// as a file, and played at the top of the night it is the difference
/// between a PA with music on it and a station - and unlike every other
/// segment it needs no model, no budget and no internet, because the words
/// never change.
///
/// NAMING. "Voice ID" could not be used: VoiceId in this app is ElevenLabs'
/// identifier for a voice (VoiceProfile.VoiceId) and has been for a year.
/// "Ident" is what a broadcaster calls this and collides with nothing.
///
/// WHERE IT SITS IN A SHOW
///
/// As the FIRST ITEM of the block, which means it lands over the opener's
/// ring-out - because that is where a show's first words already land
/// (ShowTreatment.VoiceEntersAfter brings the voice in at the point
/// ShowShape measured the piece stops hitting and starts ringing out).
///
/// That answers a real objection. Laying the voice-ID over the tail AS WELL
/// would put two voices across a second and a bit of ring-out, and the
/// shorter the opener's fade the worse it gets - the ident would be
/// finishing as the first line started, or the line would be pushed past
/// the end of the music into silence. Making the voice-ID the first words
/// instead of putting it beside them is not a dodge: the room gets the
/// theme, then the station name over its tail, then the programme. Which is
/// the order a broadcast does it in.
///
/// WHY A BRIDGE AND NOT A SEGMENT
///
/// ShowItem.Bridge, so the fitting treats it as continuity - and continuity
/// is the first thing a block drops when the gap is tight (ShowBlock.Fit).
/// That is exactly the behaviour a race night needs: on an evening running
/// to time the station introduces itself, and on an evening running late
/// the two seconds come off the station and not off the race. It also keeps
/// the voice-ID out of the first-segment overrun exemption, so it can never
/// be the reason a block overruns its gap.
///
/// WHAT MAKES IT DEGRADE
///
/// Nothing here renders, plays or opens anything. The line is handed to the
/// announcer like any other line the show says, and the announcer decides
/// how it comes out: a recorded take if the club has one
/// (AnnouncementService.RenderLineAsync asks StingerLibrary first, before
/// any key, relay or network), and otherwise the free Kokoro voice, live,
/// on this PC, with nothing billed. So a club with no ElevenLabs key, no
/// relay and no recording still has a voice-ID - theirs, in the house
/// voice, every night. The only state with nothing to say is a club who has
/// not typed a line, and then there is simply no item and the show is the
/// show it always was.
/// </summary>
internal static class ShowIdent
{
    /// <summary>
    /// The longest a voice-ID may be, in characters.
    ///
    /// A hundred and twenty is a comfortable sentence - "You're listening to
    /// Thunder Valley Raceway, club racing every Tuesday night" is seventy -
    /// and about seven seconds spoken. The cap is not a storage limit; it is
    /// what stops the feature turning into a second announcement. Anything
    /// longer is a read, reads over the opener's tail into silence, and on a
    /// tight night pushes a race later. The box on the Commentary page takes
    /// this as its MaxLength, so a club meets the limit while typing rather
    /// than by their voice-ID silently not playing.
    /// </summary>
    internal const int LongestLine = 120;

    /// <summary>
    /// The club's voice-ID as it will actually be said, or null when there
    /// is nothing sayable.
    ///
    /// WHAT IS TAKEN OUT, AND WHY EACH
    ///
    /// Line breaks and tabs become spaces and runs of space collapse: a
    /// voice-ID is one line, and the text is the KEY a recording is stored
    /// under (StingerLibrary.KeyFor), so a stray newline from a paste would
    /// be a different line from the one that was recorded - a club would
    /// have paid for a take that never played again.
    ///
    /// Braces come out altogether. A brace is this app's mark for something
    /// filled in at race time, and StingerLibrary.RecordableLines refuses
    /// any line containing one - so a club who typed "You're listening to
    /// {club}" would have had a voice-ID that could never be recorded and no
    /// way to find out why. There is nothing to fill in here: the line is
    /// typed, not templated.
    ///
    /// Audio tags are LEFT IN. "[excited] You're listening to..." is a club
    /// using the same tags the race lines use, the key ignores them
    /// (KeyFor strips them) and the Kokoro voice reads straight past them.
    ///
    /// A line with no letter or digit in it at all - "..." , "---" - is
    /// nothing sayable and comes back null rather than becoming a second of
    /// the announcer clearing its throat.
    /// </summary>
    internal static string? Line(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
        {
            return null;
        }

        var builder = new System.Text.StringBuilder(typed.Length);

        foreach (var ch in typed)
        {
            if (ch == '{' || ch == '}')
            {
                continue;
            }

            builder.Append(char.IsControl(ch) ? ' ' : ch);
        }

        var collapsed = string.Join(
            ' ',
            builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (collapsed.Length == 0 || collapsed.Length > LongestLine)
        {
            return null;
        }

        return collapsed.Any(char.IsLetterOrDigit) ? collapsed : null;
    }

    /// <summary>
    /// Whether a line was typed but is longer than a voice-ID may be - so
    /// the Commentary page can say which of the two reasons nothing is going
    /// to be said. <see cref="Line"/> cannot: both come back null, which is
    /// right for the staging and useless to a club.
    /// </summary>
    internal static bool TooLong(string? typed) =>
        Line(typed) is null
        && !string.IsNullOrWhiteSpace(typed)
        && Line(typed[..Math.Min(typed.Length, LongestLine)]) is not null;

    /// <summary>
    /// The voice-ID as an item for the head of a show's block, or null when
    /// the club has not given us one to say.
    ///
    /// <paramref name="say"/> queues the line with the announcer and must
    /// not wait for it to be spoken - the same contract every other item in
    /// a block has (see ShowUtterance.Say). AnnouncementService.SpeakReminder
    /// is the one to hand it to: it is what every other deliberate line of a
    /// show goes out as, so the voice-ID inherits the ducking, the ordering
    /// and above all the priority that already keeps a race call in front of
    /// anything the show wanted to say.
    ///
    /// <paramref name="went"/> is called straight after the words are
    /// handed over, and is how the running order learns that the voice-ID
    /// actually went out - see ShowRunningOrder.IdentWent for why that
    /// cannot be done when the item is built. It is called AFTER the say and
    /// not before: an item whose say threw never reached the announcer, and
    /// a club should not lose the night's voice-ID to a voice queue that
    /// fell over.
    /// </summary>
    /// <param name="typed">What the club typed - <see cref="Line"/> cleans it.</param>
    /// <param name="say">Queues the line with the announcer.</param>
    /// <param name="went">Told once the line has been handed over. Optional.</param>
    internal static ShowItem? Item(string? typed, Action<string> say, Action? went = null)
    {
        if (Line(typed) is not { } line)
        {
            return null;
        }

        return ShowItem.Bridge(line, () =>
        {
            say(line);
            went?.Invoke();
        });
    }

    /// <summary>
    /// What is recorded for the club's voice-ID - which voice, which
    /// soundscape, when - or null when nothing is and it will be spoken
    /// live.
    /// </summary>
    internal static StingerLibrary.Entry? Recorded(string? typed) =>
        Recorded(typed, StingerLibrary.Shared);

    /// <inheritdoc cref="Recorded(string?)"/>
    /// <param name="typed">What the club typed.</param>
    /// <param name="library">
    /// Which library to ask. The overload above asks the club's own
    /// (StingerLibrary.Shared), which settles its folder once for the life
    /// of the process - so a test, which points the app at a folder of its
    /// own per test, has to be able to hand in one of those.
    /// </param>
    internal static StingerLibrary.Entry? Recorded(string? typed, StingerLibrary library) =>
        Line(typed) is { } line ? library.Recorded(line) : null;

    /// <summary>
    /// Whether the voice-ID on disk was recorded in a voice this PC has
    /// since stopped using.
    ///
    /// THE FAILURE MODE THIS IS FOR. A club records their voice-ID in March,
    /// changes their announcer voice in April, and from then on every night
    /// opens with one sentence in the old voice and the rest of the show in
    /// the new one - and nothing anywhere tells them why.
    ///
    /// WHAT IS NOT DONE ABOUT IT. The recording still plays. That is
    /// StingerLibrary's whole contract, stated on the class: a line that was
    /// recorded plays as recorded whatever voice is chosen, including the
    /// free one and with no internet. Holding this ONE line back would be a
    /// second, contradictory policy for the same store - and it would mean a
    /// club who changed voice lost their voice-ID silently, which is a worse
    /// version of the same complaint. It is also not obviously wrong on the
    /// air: a voice-over nobody on the station ever does in person is what
    /// an ident IS, most places.
    ///
    /// So the answer is to SAY so - see <see cref="Reading"/> - and let the
    /// club spend one press on recording it again, which the Soundscapes
    /// page's "Re-record all" already does and already has an undo for.
    /// </summary>
    internal static bool InAnotherVoice(string? typed, VoiceProfile voice) =>
        InAnotherVoice(typed, voice, StingerLibrary.Shared);

    /// <inheritdoc cref="InAnotherVoice(string?, VoiceProfile)"/>
    internal static bool InAnotherVoice(string? typed, VoiceProfile voice, StingerLibrary library) =>
        Recorded(typed, library) is { } entry
        && !StingerLibrary.SameVoice(entry.VoiceName, StingerLibrary.VoiceNameFor(voice));

    /// <summary>
    /// The club's voice-ID in one line, for the Commentary page and the
    /// developer console.
    ///
    /// THE ONLY TABLE OF THESE SENTENCES - five states, side by side, in the
    /// order a club meets them. Written here and not on the page for the
    /// reason ShowBlock.CaptionFor gives: the same question gets asked from
    /// the console and from a settings page, and two tables of the same
    /// answers disagreed on seven of eleven entries the last time this app
    /// had two.
    ///
    /// NO MENTION OF THE WINDOWS VOICE anywhere in here, and there must
    /// never be one: it was retired on 10 Oct 2026 and Kokoro is both the
    /// free voice a club can choose and the voice that speaks when the
    /// chosen one cannot.
    /// </summary>
    internal static string Reading(string? typed, VoiceProfile voice) =>
        Reading(typed, voice, StingerLibrary.Shared);

    /// <inheritdoc cref="Reading(string?, VoiceProfile)"/>
    internal static string Reading(string? typed, VoiceProfile voice, StingerLibrary library)
    {
        if (Line(typed) is not { } line)
        {
            return TooLong(typed)
                ? $"That is longer than a voice-ID can be - keep it to one sentence of {LongestLine} characters or fewer."
                : "No voice-ID. Type one line - \"You're listening to Thunder Valley Raceway\" - and the show says it at the top of the night.";
        }

        var now = StingerLibrary.VoiceNameFor(voice);

        if (Recorded(line, library) is not { } entry)
        {
            // Nothing recorded, which is not a fault and for most clubs
            // never will be: the line is said live, free, by the voice that
            // says everything else.
            return voice.EffectiveEngine == VoiceEngine.Kokoro
                ? $"“{line}” - said live in {now}, free, every time. Record it with an ElevenLabs voice to keep one take."
                : $"“{line}” - said live for now. Record voice lines on Soundscapes to keep one take in {now}.";
        }

        return StingerLibrary.SameVoice(entry.VoiceName, now)
            ? $"“{line}” - recorded in {entry.VoiceName}."
            : $"“{line}” - recorded in {entry.VoiceName}, and this PC now speaks with {now}. "
                + "It plays as recorded; record voice lines again on Soundscapes to have it match.";
    }
}
