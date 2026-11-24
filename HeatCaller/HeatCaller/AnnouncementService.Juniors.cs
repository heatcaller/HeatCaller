namespace HeatCaller;

/// <summary>
/// "Offline voices for juniors" (15 Nov 2026, children's privacy, plan 2.3):
/// with the club's switch on, a spoken line that names a junior is said by
/// Kokoro on this PC - never sent to ElevenLabs or the HeatCaller voice.
/// </summary>
/// <remarks>
/// HOW A LINE IS KNOWN TO NAME A JUNIOR. The same way listen-along knows a
/// line names a driver shown by initials (CaptionIfCleared): the words are
/// searched for every name and each word of a name of every driver ticked
/// Junior, through PublicPrivacy.MentionsJunior. It errs on the private side
/// - an adult sharing a junior's first name or surname is kept here too.
///
/// WHERE IT IS ASKED. Everywhere a line could leave this PC for a cloud
/// voice: RenderLineAsync (every race and announcer line, release calls one
/// by one, the recap), WillStream (a streamed call is never one of them),
/// TryRenderReleaseBatchAsync (a grid naming a junior goes line by line) and
/// PrepareLinesAsync (a line naming a junior is not prepared in a cloud
/// voice). A take already saved or recorded is played from disk as before:
/// nothing is sent for it.
///
/// KOKORO OR NOTHING. With the switch on and Kokoro unable to speak, a line
/// naming a junior is lost rather than sent to the cloud anyway, and the
/// console says so once - the club chose this, and a stand-in that quietly
/// went back to the cloud would undo it.
/// </remarks>
public sealed partial class AnnouncementService
{
    /// <summary>AppSettings.OfflineVoicesForJuniors, read live - wired by AnnouncerControl. Null (tests, older callers) is off.</summary>
    public Func<bool>? JuniorsStayOnThisPc { get; set; }

    /// <summary>Who the juniors are. PublicPrivacy.Current unless a test hands in its own.</summary>
    internal Func<PublicPrivacy>? JuniorRegister { get; set; }

    /// <summary>For ElevenLabsClient.Notice - a console line said from outside this class.</summary>
    internal void SayToConsole(string line) => StatusMessage?.Invoke(line);

    /// <summary>Whether this line names a junior and the club has asked for those to stay on this PC.</summary>
    internal bool KeepsJuniorLineHere(string? text)
    {
        try
        {
            return JuniorsStayOnThisPc?.Invoke() == true
                   && (JuniorRegister?.Invoke() ?? PublicPrivacy.Current).MentionsJunior(text);
        }
        catch
        {
            // A register that can't be read: with the switch on, the private
            // side - kept here - is the safer guess.
            return JuniorsStayOnThisPc?.Invoke() == true;
        }
    }

    /// <summary>
    /// A line naming a junior, in the Kokoro voice on this PC. Throws, like
    /// RenderWithKokoroAsync, when Kokoro can't speak - never falls back to a
    /// cloud voice. See the remarks above.
    /// </summary>
    private async Task<SynthesisResult> RenderJuniorLineAsync(VoiceProfile profile, string text)
    {
        NoteJuniorLine(KokoroSpeech.UnavailableReason);

        if (KokoroSpeech.UnavailableReason is { } unavailable)
        {
            throw new InvalidOperationException($"The Kokoro voice can't speak - {unavailable}. {GetKokoroAdvice}");
        }

        return new SynthesisResult(await KokoroSpeech.RenderAsync(profile, text, renderingAhead: RenderingAhead.Value), LineSource.Kokoro);
    }

    /// <summary>The junior notes already in the console, by reason. Locked on itself; never emptied.</summary>
    private readonly HashSet<string> _juniorNotesSaid = new(StringComparer.Ordinal);

    /// <summary>
    /// The console's note that a line naming a junior is said on this PC -
    /// or, when Kokoro can't speak (<paramref name="unavailable"/> says why),
    /// that it wasn't said at all. Each once: the first once a run, the
    /// second once for each reason.
    /// </summary>
    /// <remarks>
    /// WHY (23 Nov 2026, bug hunt 24): these went through LogStandInOnce,
    /// whose list is emptied every time a cloud voice answers a line - right
    /// for a stand-in reason, which is news again once the voice has come
    /// back, but this is how the club has set things up, not a fault that
    /// ends. So "Said once." was said again after nearly every line the
    /// cloud voice made, dozens of times a night, pushing real warnings up
    /// the console. They keep their own list now, as NoteVoiceRelayOnce
    /// keeps its own flag for the same reason.
    /// </remarks>
    internal void NoteJuniorLine(string? unavailable)
    {
        bool first;

        lock (_juniorNotesSaid)
        {
            first = _juniorNotesSaid.Add(unavailable is null ? "said-here" : "lost:" + unavailable);
        }

        if (!first)
        {
            return;
        }

        StatusMessage?.Invoke(unavailable is null
            ? "[voice] Lines that name a junior are said by the Kokoro voice on this PC (\"Offline voices for juniors\" is on). Said once."
            : $"[voice] A line naming a junior wasn't said: \"Offline voices for juniors\" is on and the Kokoro voice can't speak ({unavailable}). "
              + "It isn't sent to a cloud voice instead. Said once for this reason.");
    }

    /// <summary>What a line answered by ElevenLabs does to the console's said-once notes, for tests - which have no cloud voice to answer one.</summary>
    internal void CloudVoiceAnsweredForTest() => NoteElevenLabsAnswered();
}
