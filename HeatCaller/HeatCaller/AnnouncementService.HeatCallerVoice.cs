using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// The HeatCaller voice (ElevenLabs through the relay, on HeatCaller's
/// account): its allowance, refusals, probe and reset.
///
/// Moved here unchanged from AnnouncementService.cs on 26 Sep 2026, when the
/// file was split into parts. Fields with initializers stayed there.
/// </summary>
public sealed partial class AnnouncementService
{
    // --- The HeatCaller voice ----------------------------------------------
    //
    // ElevenLabs on HeatCaller's own account, asked for through the club's
    // relay (see HeatCallerVoice.cs). The club pays nothing and holds no
    // key, so the rules are different from a club's own key in one way that
    // matters: when the month's characters are gone, EVERY line switches to
    // the free Kokoro voice, saved ones included, until the allowance
    // starts again. A night that is half one voice and half another is what
    // a club hears as "the announcer is broken"; one voice, with the reason
    // in the log and on the Mixer, is honest. Nothing is thrown away - the
    // saved lines are all still there next month.

    /// <summary>Set by the host: how this laptop asks its relay to speak. Null when the app has not wired one (tests, and the first moments of startup).</summary>
    internal HeatCallerVoiceClient? HeatCallerVoice { get; set; }

    private readonly object _heatCallerLock = new();

    /// <summary>Why the HeatCaller voice isn't speaking, or null while it is. Lower case, to sit inside a sentence.</summary>
    private string? _heatCallerReason;

    /// <summary>Nothing is sent to the relay before this. See the rest periods below.</summary>
    private DateTime _heatCallerRetryUtc;

    /// <summary>A probe is on its way. Never more than one.</summary>
    private bool _heatCallerProbeRunning;

    /// <summary>Where the allowance stood when the relay last said. For the Voice Settings page and the log.</summary>
    private HeatCallerAllowance? _heatCallerAllowance;

    /// <summary>Whether the "nearly spent" warning has been said for this month's allowance.</summary>
    private bool _heatCallerAllowanceWarned;

    /// <summary>Which month that warning was said for - the relay's own reset date, which moves exactly once a month.</summary>
    private DateTime? _heatCallerAllowanceResets;

    /// <summary>
    /// Latched while the month's characters are spent, and cleared only by a
    /// line the relay actually RENDERED.
    ///
    /// Its own field rather than a reading of the last refusal: the relay is
    /// asked again every half hour, and a Wi-Fi blip on one of those tries
    /// used to replace "the allowance is gone" with "can't be reached",
    /// which let saved lines play in the HeatCaller voice again for the rest
    /// of the month - half the night in one voice and half in the other,
    /// which is the whole thing this is here to prevent. A line the relay
    /// served from its own cache does not clear it either: that costs
    /// nothing and says nothing about the allowance.
    /// </summary>
    private bool _heatCallerAllowanceSpent;

    /// <summary>
    /// Until when the spent allowance holds - the relay's own reset date when
    /// it said one, otherwise the start of next month. Kept beside the flag
    /// so it can be remembered across a restart (see VoiceSettingsStore):
    /// a laptop restarted mid-night used to forget the allowance was gone
    /// and play every saved line in the HeatCaller voice again.
    /// </summary>
    private DateTime? _heatCallerSpentUntilUtc;

    /// <summary>
    /// Moved on by every refusal that rests the voice, and by a change of
    /// relay or voice on Voice Settings. Under _heatCallerLock.
    ///
    /// Up to two lines are with the relay at once. Line B can come back
    /// "allowance spent" while line A, asked for a moment earlier, is still
    /// rendering - and A then finishing used to clear the spent state B had
    /// just set and say "The HeatCaller voice is back", with the allowance
    /// gone. A request notes this number as it goes out; only an answer
    /// whose number is still current may end a rest. One that was
    /// overtaken by a refusal is still played, and still saved - it is a
    /// perfectly good take - it just says nothing about how things stand
    /// now.
    /// </summary>
    private int _heatCallerRefusalGeneration;

    /// <summary>
    /// Moved on only by ResetHeatCallerVoiceState - a change of relay, club,
    /// token or voice. Under _heatCallerLock.
    ///
    /// The refusal generation above stops an old ANSWER ending a rest; this
    /// stops an old REFUSAL starting one. A line asked of the old relay just
    /// before the club changed it, and refused afterwards - "allowance spent"
    /// from a test relay, say - used to rest the new relay over it, and
    /// remember that across a restart until the month was out.
    /// </summary>
    private int _heatCallerResetGeneration;

    /// <summary>True when the relay or voice has been changed since <paramref name="resetGeneration"/> was read.</summary>
    private bool HeatCallerResetSince(int resetGeneration)
    {
        lock (_heatCallerLock)
        {
            return resetGeneration != _heatCallerResetGeneration;
        }
    }

    /// <summary>
    /// Keeps the remembered spent date in step with the flag when two
    /// threads change it at once: the file is written from whatever the
    /// flag says at the time, under this, rather than from what each thread
    /// thought it said. Never taken inside _heatCallerLock.
    /// </summary>
    private readonly object _heatCallerMemoLock = new();

    private const string HeatCallerAllowanceSpentReason = "this month's HeatCaller voice allowance is used up";

    /// <summary>
    /// How long the relay rests after it could not be reached before one
    /// line is tried through it again, in the background. The same minute
    /// ElevenLabs rests for, and for the same reason: a club's Wi-Fi with no
    /// internet can hold a request for half a minute, and a lap call that
    /// late is worthless.
    /// </summary>
    private static readonly TimeSpan HeatCallerRestPeriod = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long before a settled refusal - the allowance spent, the voice
    /// not included, a voice that has gone - is tried again. Long, because
    /// none of them mends itself in a minute; not never, because whoever
    /// runs the relay can put any of them right while the club races on.
    /// </summary>
    private static readonly TimeSpan HeatCallerSettledRetry = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The audio for one line with the HeatCaller voice: the saved copy, the
    /// relay, or the Kokoro voice standing in.
    /// </summary>
    /// <param name="standInText">What Kokoro says if it stands in - the line as written, before names were shortened for the relay (22 Oct 2026). Null: the same text.</param>
    private async Task<SynthesisResult> RenderWithHeatCallerAsync(VoiceProfile profile, string text, string? standInText = null)
    {
        var onThisPc = standInText ?? text;

        NoteVoiceRelayOnce();

        bool allowanceSpent;
        bool resting;

        lock (_heatCallerLock)
        {
            // Resting until the relay speaks a line again, not until a clock
            // runs out - the same shape as the ElevenLabs rest above. The
            // clock says when one line may be TRIED in the background; the
            // reason is what says the voice isn't speaking. Tied to the
            // clock, every line went to the relay again the moment it
            // expired, and any of them the relay happened to hold a copy of
            // came back in the HeatCaller voice - half a night in one voice
            // and half in the other, which is what this is here to prevent.
            resting = _heatCallerReason is not null;
            allowanceSpent = _heatCallerAllowanceSpent;
        }

        // The allowance is the one refusal that reaches past new lines: the
        // saved ones are passed over too, so the whole night is one voice.
        // (The same rule as HeatCallerSavedLinesPassedOver, read here in the
        // one look above.) So is a consent that is missing or taken back: a
        // saved take is the person's voice as surely as a new one.
        // Not with Kokoro unable to speak (23 Nov 2026, bug hunt 24) - see
        // PassesOverSavedHeatCaller.
        if (!PassesOverSavedHeatCaller(profile, HeatCallerConsentWithheld(profile), allowanceSpent, KokoroSpeech.IsAvailable)
            && await VoiceLibrary.Shared.TryLoadAsync(profile, text) is { } saved)
        {
            return new SynthesisResult(saved, LineSource.Library);
        }

        if (HeatCallerVoice is not { } client || !profile.HeatCallerReady)
        {
            LogStandInOnce(
                "heatcaller-setup",
                // The picker is at the top of the Cast tab since 22 Oct 2026.
                "[voice] No HeatCaller voice is chosen yet - the free Kokoro voice is speaking. Pick one at the top of the Cast tab.");

            return await RenderWithStandInAsync(profile, onThisPc);
        }

        if (resting)
        {
            // The probe is claimed HERE, where it is actually started - not
            // when the rest was noticed. Claimed earlier, a line that then
            // came back from the library (the common case while resting: a
            // flag call that already has a take) took the claim away with
            // it and never gave it back, and no probe ran again for the
            // rest of the night.
            bool probe;

            lock (_heatCallerLock)
            {
                // One line a rest period is tried through the relay in the
                // background, to notice it coming back - a minute apart for
                // something that may mend itself, half an hour apart for
                // something somebody has to put right. The line itself has
                // already gone to Kokoro, so nothing waits on it.
                probe = !_heatCallerProbeRunning && DateTime.UtcNow >= _heatCallerRetryUtc;

                if (probe)
                {
                    _heatCallerProbeRunning = true;
                }
            }

            if (probe)
            {
                StartHeatCallerProbe(client, profile, text);
            }

            return await RenderWithStandInAsync(profile, onThisPc);
        }

        try
        {
            // RenderCancel: a line rendered ahead that nobody will now hear
            // (22 Oct 2026) - None everywhere else, as it always was.
            var audio = await SpeakWholeLineThroughRelayAsync(client, profile, text, RenderCancel.Value);

            VoiceLibrary.Shared.Save(profile, text, audio);

            return new SynthesisResult(audio, LineSource.HeatCaller);
        }
        catch (HeatCallerVoiceException ex)
        {
            NoteHeatCallerRefusal(ex);
            return await RenderWithStandInAsync(profile, onThisPc);
        }
        catch (SynthesisSlotTimeoutException)
        {
            // This laptop's own queue, not the relay: nothing to rest over.
            LogStandInOnce(
                "heatcaller-slot",
                "[voice] The voice queue was full, so the free Kokoro voice said a line. Said once, not for every line.");

            return await RenderWithStandInAsync(profile, onThisPc);
        }
    }

    /// <summary>Set once the log has been told the voice is pointed at a relay of its own.</summary>
    private int _voiceRelayNoted;

    /// <summary>
    /// Says, once per run, that the HeatCaller voice is speaking through a
    /// relay of its own rather than the club's.
    ///
    /// Not a stand-in reason, so it does not go through LogStandInOnce:
    /// that set is emptied whenever the voice recovers, and this is not
    /// something to be told again every time it does. It is a note about how
    /// the laptop is set up, and the night's log should carry it exactly
    /// once - so that a recording that sounds wrong can be explained without
    /// anyone having to guess which relay spoke.
    /// </summary>
    private void NoteVoiceRelayOnce()
    {
        if (HeatCallerVoice is not { UsingVoiceRelay: true })
        {
            return;
        }

        if (Interlocked.Exchange(ref _voiceRelayNoted, 1) != 0)
        {
            return;
        }

        StatusMessage?.Invoke(
            "[voice] The HeatCaller voice is speaking through the voice relay set on the Voice tab, not the one on the Club Website page. "
            + "Results, check-in and the race-day manager are unaffected.");
    }

    /// <summary>
    /// One line through the relay, in the same queue and under the same time
    /// limit as an ElevenLabs line - the announcer must never have more than
    /// a couple of renders outstanding, whoever is doing the rendering.
    /// </summary>
    private async Task<byte[]> SpeakThroughRelayAsync(
        HeatCallerVoiceClient client,
        VoiceProfile profile,
        string text,
        CancellationToken cancel,
        bool? shared = null)
    {
        // Also here, not only in RenderWithHeatCallerAsync: Prepare Lines
        // comes straight to this method, and a whole grid prepared through
        // a test relay should say so in the log too.
        NoteVoiceRelayOnce();

        // Which relay and voice this is asked of - see _heatCallerResetGeneration.
        // Read before the wait for a slot: the client was chosen before it,
        // and a change made while waiting must still mark this one's refusal.
        int resetGeneration;

        lock (_heatCallerLock)
        {
            resetGeneration = _heatCallerResetGeneration;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(SynthesisTimeout);

        // WHY (20 Oct 2026, Bug Hunt 8): the wait for a slot and the relay
        // request share the one 30 s limit, so a line that got its slot late
        // was cut off and reported as the relay not answering - which rests
        // the relay though only this laptop's own queue was slow. Timed the
        // same way as the ElevenLabs paths (see SlotWaitTookMostOfLimit).
        var gateClock = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await SynthesisGate.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new SynthesisSlotTimeoutException($"No synthesis slot came free within {SynthesisTimeout.TotalSeconds:0} s.");
        }

        var waitedForSlot = gateClock.Elapsed;

        try
        {
            // Noted as the request goes out, not as it comes back: see
            // _heatCallerRefusalGeneration.
            int generation;

            lock (_heatCallerLock)
            {
                generation = _heatCallerRefusalGeneration;
            }

            var (audio, allowance, cached) = await client.SpeakAsync(
                profile.HeatCallerVoice,
                text,
                shared ?? IsFixedLine(text),
                timeout.Token);

            NoteHeatCallerAnswered(allowance, cached, generation);

            return audio;
        }
        catch (HeatCallerVoiceException ex) when (!ex.AskedBeforeReset && HeatCallerResetSince(resetGeneration))
        {
            // Refused by the relay or for the voice as they were before the
            // club changed them: marked, so it rests nothing (see
            // NoteHeatCallerRefusal).
            throw new HeatCallerVoiceException(ex.Refusal, ex.Message, ex.Allowance) { AskedBeforeReset = true };
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            if (SlotWaitTookMostOfLimit(waitedForSlot))
            {
                throw new SynthesisSlotTimeoutException(
                    $"Waited {waitedForSlot.TotalSeconds:0} s for a synthesis slot, which left the request too little of the {SynthesisTimeout.TotalSeconds:0} s limit.");
            }

            throw new HeatCallerVoiceException(
                HeatCallerRefusal.Unreachable,
                $"The relay didn't answer within {SynthesisTimeout.TotalSeconds:0} s.")
            {
                AskedBeforeReset = HeatCallerResetSince(resetGeneration),
            };
        }
        finally
        {
            SynthesisGate.Release();
        }
    }

    /// <summary>
    /// The quiet put between two pieces of a line the relay was asked for
    /// in parts - about the pause a voice leaves between two sentences, so
    /// the join is heard as one, not as a gap.
    /// </summary>
    private const double HeatCallerPieceGapSeconds = 0.2;

    /// <summary>A sentence's end: the punctuation stays with the sentence, the space after it is where a line may be split.</summary>
    private static readonly System.Text.RegularExpressions.Regex HeatCallerSentenceEnd =
        new(@"(?<=[.!?])\s+", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The audio tags at the start of a sentence, such as "[excited] ".</summary>
    private static readonly System.Text.RegularExpressions.Regex HeatCallerLeadingTags =
        new(@"^(?:\[[a-zA-Z ]{1,24}\]\s*)+", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// One line through the relay, however long it is. A line the relay
    /// takes at once goes as it is; a longer one - a race recap with the
    /// broadcast opening and closing, or a raised word limit, every sentence
    /// tagged [excited] and every time spelled out - goes in pieces cut at
    /// sentence ends, and the pieces are joined back into one take.
    ///
    /// Without this the relay refused the line as too long, and the recap -
    /// the line of the night a club most wants in the HeatCaller voice - was
    /// read by the free voice while every other line used the HeatCaller
    /// one, with the reason logged once a session and never again.
    ///
    /// The length is counted with the audio tags in, which is how the relay
    /// counts it for a v3 voice (the tags are sent on to it). For a voice
    /// that can't use them the relay takes them out and counts less, so a
    /// piece measured this way fits either way.
    /// </summary>
    private async Task<byte[]> SpeakWholeLineThroughRelayAsync(
        HeatCallerVoiceClient client,
        VoiceProfile profile,
        string text,
        CancellationToken cancel)
    {
        var pieces = SplitForHeatCaller(text, HeatCallerVoiceClient.MaxCharacters);

        if (pieces.Count <= 1)
        {
            return await SpeakThroughRelayAsync(client, profile, text, cancel);
        }

        WaveFormat? format = null;
        using var joined = new MemoryStream();

        for (var i = 0; i < pieces.Count; i++)
        {
            // Never shared: only a whole line with nothing filled into it
            // may be kept for every club, and a piece of a long line is part
            // of one with names in it. Asking IsFixedLine of a piece could
            // answer yes for a sentence that happens to match one.
            var audio = await SpeakThroughRelayAsync(client, profile, pieces[i], cancel, shared: false);

            byte[] pcm;
            WaveFormat pieceFormat;

            try
            {
                (pcm, pieceFormat) = DecodeMp3(audio);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // This one line's problem, like a line too long: the Kokoro
                // voice says it, and nothing rests over it.
                throw new HeatCallerVoiceException(HeatCallerRefusal.Text, $"A piece of a long line came back unreadable ({ex.Message}).");
            }

            if (format is null)
            {
                format = pieceFormat;
            }
            else if (!format.Equals(pieceFormat))
            {
                // Two pieces of one voice from one relay should never differ,
                // but joined raw they would play at the wrong speed or as
                // noise. Better the Kokoro voice than that.
                throw new HeatCallerVoiceException(HeatCallerRefusal.Text, "The pieces of a long line came back in different audio formats.");
            }
            else
            {
                var gap = (int)(format.AverageBytesPerSecond * HeatCallerPieceGapSeconds);
                gap -= gap % Math.Max(1, format.BlockAlign);
                joined.Write(new byte[gap], 0, gap);
            }

            joined.Write(pcm, 0, pcm.Length);
        }

        // A WAV, like a release call cut out of a batch: the player and the
        // saved-voice library both take one, and it is played as it is, with
        // nothing more skipped from its start - the start of each piece was
        // already trimmed by DecodeMp3 above.
        return EncodeWav(joined.ToArray(), format!);
    }

    /// <summary>
    /// Cuts a line into pieces no longer than <paramref name="maxCharacters"/>,
    /// counted with any audio tags in. Pieces end at sentence ends where they
    /// can, as many whole sentences in each as fit. A single sentence longer
    /// than that is cut after a comma or between words, and its continuation
    /// starts with the sentence's own tags again so the delivery holds.
    /// A line that already fits comes back as itself, trimmed, in one piece.
    /// </summary>
    internal static IReadOnlyList<string> SplitForHeatCaller(string text, int maxCharacters)
    {
        var whole = (text ?? string.Empty).Trim();

        if (whole.Length <= maxCharacters || maxCharacters < 1)
        {
            return [whole];
        }

        var pieces = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var sentence in HeatCallerSentenceEnd.Split(whole))
        {
            var trimmed = sentence.Trim();

            if (trimmed.Length == 0)
            {
                continue;
            }

            foreach (var part in FitSentence(trimmed, maxCharacters))
            {
                if (current.Length == 0)
                {
                    current.Append(part);
                }
                else if (current.Length + 1 + part.Length <= maxCharacters)
                {
                    current.Append(' ').Append(part);
                }
                else
                {
                    pieces.Add(current.ToString());
                    current.Clear().Append(part);
                }
            }
        }

        if (current.Length > 0)
        {
            pieces.Add(current.ToString());
        }

        return pieces;
    }

    /// <summary>One sentence as pieces that each fit; just the sentence when it already does.</summary>
    private static List<string> FitSentence(string sentence, int maxCharacters)
    {
        var parts = new List<string>();

        if (sentence.Length <= maxCharacters)
        {
            parts.Add(sentence);
            return parts;
        }

        // Carried onto each continuation only while it leaves most of the
        // room for words - a tag that ate the piece would never finish it.
        var tags = HeatCallerLeadingTags.Match(sentence).Value;
        var carry = tags.Length > 0 && tags.Length * 4 <= maxCharacters ? tags : string.Empty;

        var rest = sentence;
        var first = true;

        while (rest.Length > 0)
        {
            // The first piece already starts with the tags.
            var prefix = first ? string.Empty : carry;

            if (prefix.Length + rest.Length <= maxCharacters)
            {
                parts.Add(prefix + rest);
                break;
            }

            var cut = BestCut(rest, maxCharacters - prefix.Length);
            var piece = (prefix + rest[..cut]).TrimEnd();

            if (piece.Length > 0)
            {
                parts.Add(piece);
            }

            rest = rest[cut..].TrimStart();
            first = false;
        }

        return parts;
    }

    /// <summary>
    /// Where to end a piece of an over-long sentence, as a length of
    /// <paramref name="rest"/> no more than <paramref name="room"/> and never
    /// zero: after a comma or semicolon in its later half, else at the last
    /// space, else - one unbroken run of letters - right at the limit.
    /// </summary>
    private static int BestCut(string rest, int room)
    {
        room = Math.Max(1, room);

        for (var i = Math.Min(rest.Length, room) - 1; i >= room / 2; i--)
        {
            if (rest[i] is ',' or ';')
            {
                return i + 1;
            }
        }

        // room itself is looked at: a space just past the limit still means
        // everything before it fits.
        for (var i = Math.Min(rest.Length - 1, room); i > 0; i--)
        {
            if (char.IsWhiteSpace(rest[i]))
            {
                return i;
            }
        }

        return Math.Min(rest.Length, room);
    }

    /// <summary>
    /// Tries one line through the relay in the background while it rests.
    /// The line itself has already gone to Kokoro; this is to
    /// find out whether the relay is back - and if it is, the take is saved
    /// like any other, so that line plays in the HeatCaller voice next time.
    /// </summary>
    private void StartHeatCallerProbe(HeatCallerVoiceClient client, VoiceProfile profile, string text)
    {
        _ = Task.Run(async () =>
        {
            // Its own life: not stopped with the line that happened to start it (see RenderCancel).
            RenderCancel.Value = default;

            try
            {
                // Not once the club has chosen another voice: this would be
                // a request for a voice nobody is using.
                if (_voiceStore.Current.EffectiveEngine != VoiceEngine.HeatCaller)
                {
                    return;
                }

                // With the allowance spent, the line itself is no test. The
                // relay answers the lines it already holds - every flag
                // call, "Green flag!" - from its own cache, free, spent or
                // not, and a cached answer rightly doesn't end the rest.
                // Those are exactly the lines most likely to be the one
                // tried, so after a top-up or a new month every try came
                // back cached and the voice stayed on Kokoro, half an hour
                // at a time, all night. The allowance is asked for directly
                // instead - the same catalogue read the Voice Settings page
                // makes, which costs nothing.
                if (!await HeatCallerAllowanceBackAsync(client))
                {
                    return;
                }

                var audio = await SpeakWholeLineThroughRelayAsync(client, profile, text, CancellationToken.None);

                VoiceLibrary.Shared.Save(profile, text, audio);
            }
            catch (HeatCallerVoiceException ex)
            {
                NoteHeatCallerRefusal(ex);
            }
            catch (Exception)
            {
                // A probe is a courtesy. It never surfaces as an error.
                lock (_heatCallerLock)
                {
                    _heatCallerRetryUtc = DateTime.UtcNow + HeatCallerRestPeriod;
                }
            }
            finally
            {
                lock (_heatCallerLock)
                {
                    _heatCallerProbeRunning = false;
                }
            }
        });
    }

    /// <summary>
    /// For the background try: false while the allowance is still spent (and
    /// nothing should be sent), true when it isn't spent or has room again -
    /// in which case the spent state is ended here, and the line that is
    /// then tried says whether the relay is speaking.
    ///
    /// "Room" is at least one of the longest lines the relay takes, not
    /// merely a count below the allowance: characters are reserved before a
    /// render, so a club a few characters short of the end is refused for a
    /// line longer than what is left while the count still reads below it.
    /// Ending the rest on that would bounce the night between the two voices
    /// every half hour - exactly what the spent state is there to stop.
    /// </summary>
    private async Task<bool> HeatCallerAllowanceBackAsync(HeatCallerVoiceClient client)
    {
        int generation;
        int resetGeneration;

        lock (_heatCallerLock)
        {
            if (!_heatCallerAllowanceSpent)
            {
                return true;
            }

            generation = _heatCallerRefusalGeneration;
            resetGeneration = _heatCallerResetGeneration;
        }

        // A refusal here is handled by the probe's own catch, as any other -
        // marked when it came from a relay the club has since changed, as a
        // spoken line's is (see SpeakThroughRelayAsync).
        HeatCallerCatalogue catalogue;

        try
        {
            catalogue = await client.CatalogueAsync();
        }
        catch (HeatCallerVoiceException ex) when (!ex.AskedBeforeReset && HeatCallerResetSince(resetGeneration))
        {
            throw new HeatCallerVoiceException(ex.Refusal, ex.Message, ex.Allowance) { AskedBeforeReset = true };
        }

        var allowance = catalogue.Allowance;
        // An unknown figure (20 Oct 2026) is no proof of room: the relay
        // could not read the club's usage, so the month stays spent.
        var room = catalogue.Enabled
                   && allowance.Known
                   && allowance.Allowance > 0
                   && allowance.Allowance - allowance.Used >= HeatCallerVoiceClient.MaxCharacters;

        var ended = false;

        lock (_heatCallerLock)
        {
            if (allowance.Known)
            {
                _heatCallerAllowance = allowance;
            }

            if (!room)
            {
                // Still spent: the next look is the usual half hour away.
                _heatCallerRetryUtc = DateTime.UtcNow + HeatCallerSettledRetry;
            }
            else if (generation != _heatCallerRefusalGeneration)
            {
                // Something was refused, or the relay changed, while this
                // was being asked. That is newer than this answer, so the
                // state it left is kept, and nothing is tried this time.
            }
            else
            {
                // The reason stays until the relay speaks the line tried
                // next: that is what says "back", in the usual place.
                _heatCallerAllowanceSpent = false;
                _heatCallerSpentUntilUtc = null;
                ended = true;
            }
        }

        if (ended)
        {
            RememberHeatCallerSpent();
        }
        else if (!room)
        {
            // A restart since the allowance ran out would otherwise stand in
            // with nothing in the log to say why.
            LogStandInOnce(
                "heatcaller-still-spent",
                $"[voice] {Capitalised(HeatCallerAllowanceSpentReason)} - the free Kokoro voice speaks every line until it starts again. "
                + "Nothing is lost: every line already saved is kept and plays again then.");
        }

        return ended;
    }

    /// <summary>
    /// Writes the spent allowance's date - or its absence - to the voice
    /// settings store, from whatever the flag says now. See _heatCallerMemoLock.
    /// </summary>
    private void RememberHeatCallerSpent()
    {
        lock (_heatCallerMemoLock)
        {
            DateTime? until;

            lock (_heatCallerLock)
            {
                until = _heatCallerAllowanceSpent ? _heatCallerSpentUntilUtc : null;
            }

            _voiceStore.SetHeatCallerSpentUntil(until);
        }
    }

    /// <summary>The start of next calendar month, in UTC - the relay's own month when it didn't say its reset date.</summary>
    private static DateTime NextMonthStartUtc(DateTime nowUtc) =>
        new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);

    /// <summary>
    /// Forgets why the HeatCaller voice was resting - the reason, a spent
    /// allowance, and when it may next be tried - so the next line is asked
    /// of the relay straight away.
    ///
    /// For Voice Settings, when the voice relay address, club or token is
    /// changed or a different HeatCaller voice is chosen. Each of those is a
    /// different question: a spent allowance on a test relay says nothing
    /// about the club's own, and a token or voice the relay turned down may
    /// be exactly what was just put right. Resting on over it kept the
    /// Kokoro voice speaking for up to half an hour - or, for the
    /// allowance, the rest of the month - after the club had fixed it. The
    /// same thing a new ElevenLabs key does for a rest over the old one.
    /// </summary>
    /// <param name="why">Lower case, to sit inside a sentence in the log.</param>
    internal void ResetHeatCallerVoiceState(string why)
    {
        bool wasResting;

        lock (_heatCallerLock)
        {
            wasResting = _heatCallerReason is not null || _heatCallerAllowanceSpent;

            _heatCallerReason = null;
            _heatCallerAllowanceSpent = false;
            _heatCallerSpentUntilUtc = null;
            _heatCallerRetryUtc = DateTime.MinValue;

            // Where some other relay's allowance stood.
            _heatCallerAllowance = null;

            // Anything already on its way was asked of the old relay, or for
            // the old voice: its answer says nothing about this one.
            _heatCallerRefusalGeneration++;

            // Nor does its refusal - see _heatCallerResetGeneration.
            _heatCallerResetGeneration++;
        }

        RememberHeatCallerSpent();

        // Under its own lock, never inside the one above - see
        // NoteHeatCallerAnswered.
        lock (_voiceHealthLock)
        {
            _standInLogged.Clear();
        }

        if (wasResting)
        {
            StatusMessage?.Invoke($"[voice] {Capitalised(why)} - the HeatCaller voice is tried again from the next line.");
        }
    }

    /// <summary>
    /// Something a rest may have been over was just put right on this PC -
    /// a consent recorded - so the next line tries the HeatCaller voice in
    /// the background at once instead of waiting out the half hour a
    /// settled refusal rests for. The usual probe, so the rest ends the
    /// usual way: when the relay speaks a line.
    ///
    /// Not ResetHeatCallerVoiceState: that forgets a spent allowance too,
    /// and recording a consent says nothing about the allowance.
    /// </summary>
    /// <param name="why">Lower case, to sit inside a sentence in the log.</param>
    internal void RetryHeatCallerVoiceSoon(string why)
    {
        bool resting;

        lock (_heatCallerLock)
        {
            resting = _heatCallerReason is not null;

            if (resting)
            {
                _heatCallerRetryUtc = DateTime.MinValue;
            }
        }

        if (resting)
        {
            StatusMessage?.Invoke($"[voice] {Capitalised(why)} - the HeatCaller voice is tried again with the next line.");
        }
    }

    /// <summary>
    /// Whether saved HeatCaller takes are passed over for this profile right
    /// now: the HeatCaller voice chosen, and its month's allowance spent.
    /// The one rule behind the saved-line check in RenderWithHeatCallerAsync,
    /// the release calls read straight from the library, and the prepared
    /// flag and release takes - so that none of them plays in the HeatCaller
    /// voice on a night the rest is in the Kokoro voice.
    ///
    /// Of the relay's refusals, only the allowance. While the relay merely can't be reached, saved
    /// lines DO still play in the HeatCaller voice - that is what the docs
    /// promise, and what RenderWithHeatCallerAsync has always done.
    ///
    /// And the consent: a cloned voice with no consent on record, or one
    /// taken back, passes its saved and prepared takes over too. Those were
    /// rendered in the person's voice before, and the gate in SpeakAsync
    /// only sees lines on their way to the relay - so without this, Take It
    /// Back stopped new lines while every saved one went on playing.
    /// </summary>
    private bool HeatCallerSavedLinesPassedOver(VoiceProfile profile)
    {
        if (profile.EffectiveEngine != VoiceEngine.HeatCaller)
        {
            return false;
        }

        if (HeatCallerConsentWithheld(profile))
        {
            return true;
        }

        bool allowanceSpent;

        lock (_heatCallerLock)
        {
            allowanceSpent = _heatCallerAllowanceSpent;
        }

        return PassesOverSavedHeatCaller(profile, consentWithheld: false, allowanceSpent, KokoroSpeech.IsAvailable);
    }

    /// <summary>
    /// The rule behind HeatCallerSavedLinesPassedOver, with what it reads
    /// handed in so it can be tested: the HeatCaller voice chosen, and either
    /// its consent withheld, or its allowance spent with Kokoro able to speak.
    /// </summary>
    /// <remarks>
    /// WHY (23 Nov 2026, bug hunt 24): the spent allowance passed saved
    /// takes over even when Kokoro could not speak on this PC, so every line
    /// failed - yellow, red and the finish included - though their takes were
    /// saved and cost nothing to play. With nothing to say a line instead, a
    /// saved take is better than silence, as ElevenLabs' rule has it
    /// (PassesOverSavedElevenLabs). A withheld consent still passes them over:
    /// a saved take is the person's voice as surely as a new one.
    /// </remarks>
    internal static bool PassesOverSavedHeatCaller(VoiceProfile profile, bool consentWithheld, bool allowanceSpent, bool kokoroReady) =>
        profile.EffectiveEngine == VoiceEngine.HeatCaller
        && (consentWithheld || (allowanceSpent && kokoroReady));

    /// <summary>
    /// True when the consent gate would refuse this profile's HeatCaller
    /// voice. With no client there are no settings to read and no relay
    /// either, so nothing is passed over on its account - the app always
    /// has one (MainForm sets it as it starts).
    /// </summary>
    private bool HeatCallerConsentWithheld(VoiceProfile profile) =>
        HeatCallerVoice is { } client && !client.ConsentAllows(profile.HeatCallerVoice);

    /// <summary>The relay spoke a line: whatever it last refused for is over, and the allowance is as it just said.</summary>
    /// <param name="cached">
    /// True when the relay served a copy it already had. Free, and no proof
    /// that the club has any allowance left - so it does not end a rest over
    /// a spent one.
    /// </param>
    /// <param name="generation">
    /// _heatCallerRefusalGeneration as the request went out. When a refusal
    /// has come in since, this answer ends nothing - see that field.
    /// </param>
    private void NoteHeatCallerAnswered(HeatCallerAllowance? allowance, bool cached, int generation)
    {
        bool back;
        var warn = false;
        var spentEnded = false;
        var overtaken = false;
        DateTime? warnedUntil = null;

        lock (_heatCallerLock)
        {
            if (generation != _heatCallerRefusalGeneration)
            {
                // Asked before the latest refusal (or before the relay or
                // voice was changed) and answered after it. The take is
                // good; what it would say about the state is out of date.
                overtaken = true;
                back = false;
            }
            else if (cached && _heatCallerAllowanceSpent)
            {
                // A line the relay had a copy of. Free, and served to a club
                // with nothing left just the same - so it is no proof the
                // allowance is back, and the rest goes on. Only the next try
                // is put back to its usual distance.
                _heatCallerRetryUtc = DateTime.UtcNow + HeatCallerSettledRetry;
                back = false;
            }
            else
            {
                back = _heatCallerReason is not null;
                _heatCallerReason = null;
                _heatCallerRetryUtc = DateTime.MinValue;

                if (!cached && _heatCallerAllowanceSpent)
                {
                    // Characters were really spent on this one, so there was
                    // room for them.
                    _heatCallerAllowanceSpent = false;
                    _heatCallerSpentUntilUtc = null;
                    spentEnded = true;
                }
            }

            // Not an unknown figure (20 Oct 2026): a stand-in "0 used" must
            // not reset the month or stand as where the month is.
            if (allowance is { Known: true })
            {
                // The month the relay says it is counting, which changes
                // exactly once a month - unlike the count itself, which also
                // goes down when a failed render gives its characters back.
                if (allowance.ResetsUtc is { } resets && _heatCallerAllowanceResets != resets)
                {
                    _heatCallerAllowanceResets = resets;
                    _heatCallerAllowanceWarned = false;
                }

                // Nine tenths spent, said once a month: the first sign of a
                // spent allowance is otherwise a whole race in the Kokoro
                // voice, on the night, with nothing having warned anybody.
                warn = !_heatCallerAllowanceWarned
                       && allowance.Allowance > 0
                       && allowance.Used * 10 >= allowance.Allowance * 9;

                if (warn)
                {
                    _heatCallerAllowanceWarned = true;

                    // Remembered until the month it was said for is over, so
                    // a restart doesn't say it again (see the constructor).
                    warnedUntil = allowance.ResetsUtc ?? NextMonthStartUtc(DateTime.UtcNow);
                }

                _heatCallerAllowance = allowance;
            }
        }

        if (spentEnded)
        {
            RememberHeatCallerSpent();
        }

        if (warnedUntil is { } until)
        {
            _voiceStore.SetHeatCallerWarnedUntil(until);
        }

        if (warn && allowance is not null)
        {
            StatusMessage?.Invoke(
                $"[voice] The HeatCaller voice has used {allowance.Used:N0} of {allowance.Allowance:N0} characters this month. "
                + "When it's gone, the free Kokoro voice speaks every line until it starts again"
                + (allowance.ResetsUtc is { } resets ? $" on {resets.ToLocalTime():d MMM}." : ".")
                + " Lines already saved are kept either way.");
        }

        // Not for an answer that was overtaken: the refusal that overtook it
        // has just been said, and forgetting that would say it again on the
        // very next line.
        if (overtaken)
        {
            return;
        }

        // Under its own lock, and never inside the one above: what has been
        // said is _voiceHealthLock's, and taking two locks in one place is
        // how a deadlock gets written.
        lock (_voiceHealthLock)
        {
            _standInLogged.Clear();
        }

        if (back)
        {
            StatusMessage?.Invoke(
                "[voice] The HeatCaller voice is back - it speaks from the next line, and the Kokoro voice stands down.");
        }
    }

    /// <summary>The relay would not speak: rest it for a while, and say why once.</summary>
    private void NoteHeatCallerRefusal(HeatCallerVoiceException ex)
    {
        // Asked of the relay or voice the club has since changed: the line
        // went to Kokoro, and that is all. Resting the new relay
        // over the old one's answer is what the reset was there to stop.
        if (ex.AskedBeforeReset)
        {
            return;
        }

        // One line's own problem, not a state: too long, or nothing to say.
        // Nothing rests over it - the next line may be perfectly speakable.
        if (ex.Refusal == HeatCallerRefusal.Text)
        {
            LogStandInOnce("heatcaller-text", $"[voice] The HeatCaller voice couldn't speak a line ({ex.Message}) - the free Kokoro voice said it.");
            return;
        }

        var settled = ex.Refusal
            is HeatCallerRefusal.Allowance
            or HeatCallerRefusal.NotIncluded
            or HeatCallerRefusal.Off
            or HeatCallerRefusal.NotAllowed
            or HeatCallerRefusal.UnknownVoice

            // Settled, because nothing about it changes until somebody
            // records the consent: every line for the rest of the night
            // would otherwise say the same thing again. The club is told
            // once, with what to do about it.
            or HeatCallerRefusal.NoConsent;

        var reason = ex.Refusal switch
        {
            HeatCallerRefusal.Allowance => HeatCallerAllowanceSpentReason,
            HeatCallerRefusal.NotIncluded => "the HeatCaller voice isn't included for this club",
            HeatCallerRefusal.Off => "the HeatCaller voice isn't switched on for this relay",
            HeatCallerRefusal.NotAllowed => "the relay didn't accept this club's token",
            HeatCallerRefusal.UnknownVoice => "that HeatCaller voice isn't available any more",

            // The message carries VoiceConsent's own reason, which names
            // the fix - so it is used as written rather than summarised
            // into something a club cannot act on.
            HeatCallerRefusal.NoConsent => ex.Message,
            HeatCallerRefusal.Busy => "the HeatCaller voice is busy",
            _ => "the HeatCaller relay can't be reached",
        };

        bool first;
        var spentBegan = false;

        lock (_heatCallerLock)
        {
            first = _heatCallerReason != reason;
            _heatCallerReason = reason;

            // Any answer still on its way was asked for before this, and
            // may no longer end the rest - see _heatCallerRefusalGeneration.
            _heatCallerRefusalGeneration++;

            if (ex.Allowance is { Known: true })
            {
                _heatCallerAllowance = ex.Allowance;
            }

            if (ex.Refusal == HeatCallerRefusal.Allowance && !_heatCallerAllowanceSpent)
            {
                _heatCallerAllowanceSpent = true;

                // Until the month the relay is counting is over - its own
                // date when it has said one, the calendar's otherwise. A
                // date already past is last month's figure, kept from an
                // earlier answer, and would be forgotten on the next start.
                var nowUtc = DateTime.UtcNow;

                _heatCallerSpentUntilUtc = _heatCallerAllowance?.ResetsUtc is { } resets && resets > nowUtc
                    ? resets
                    : NextMonthStartUtc(nowUtc);

                spentBegan = true;
            }

            _heatCallerRetryUtc = DateTime.UtcNow + (settled ? HeatCallerSettledRetry : HeatCallerRestPeriod);
        }

        // Remembered across a restart, so a laptop restarted mid-night
        // stays in one voice (see the constructor).
        if (spentBegan)
        {
            RememberHeatCallerSpent();
        }

        if (!first)
        {
            return;
        }

        StatusMessage?.Invoke(ex.Refusal == HeatCallerRefusal.Allowance
            ? $"[voice] {Capitalised(reason)} - the free Kokoro voice speaks every line until it starts again. "
              + "Nothing is lost: every line already saved is kept and plays again then. " + ex.Message
            : $"[voice] {Capitalised(reason)} ({ex.Message}) - the free Kokoro voice is standing in. "
              + (settled
                  ? "It is tried again about every half hour."
                  : "It is tried again in the background about once a minute."));
    }
}
