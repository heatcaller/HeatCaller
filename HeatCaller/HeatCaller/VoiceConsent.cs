namespace HeatCaller;

/// <summary>
/// Whether the club may speak in a cloned voice - the consent behind a
/// real person's likeness, and the one thing that must be true before
/// HeatCaller says a word in it.
///
/// WHY THIS EXISTS AT ALL
///
/// A club's own announcer can record half an hour once, and from then on
/// the app sounds like the person the room already knows. It is the single
/// most memorable thing about the product and the answer to the one
/// objection that loses the sale - it does not replace the announcer, it
/// IS them, on the nights they are racing instead of talking.
///
/// It is also a person's voice. The honest shape, and the one the plan
/// settled on before a line of this was written, is: **the club holds the
/// recording and the consent, HeatCaller holds nothing but a voice id, and
/// one button takes it back.** The relay side of that is built - a voice
/// marked as a clone is kept out of the cross-club cache and out of the
/// public sample list, and taking the id off a club's allow-list is the
/// revoke. This is the other half: the club's own record that somebody
/// agreed, and the gate that reads it.
///
/// THERE WAS NO CONSENT MACHINERY IN THIS APP BEFORE THIS (12 Oct 2026)
///
/// Nothing anywhere recorded that a named person agreed to something, with
/// a date. The nearest shape was ClubNote - a `From`, an `AddedAtUtc`, an
/// `Approved` flag, and nothing said unless the flag is true - and that is
/// what this is modelled on, plus the one thing it does not need: WHO
/// agreed, as opposed to who sent it.
///
/// A RULE, NOT A STORE, AND DELIBERATELY SO
///
/// Everything here is static and takes what it needs as arguments. Where
/// the record is kept, and what the page that writes it looks like, are
/// decisions of their own - and the thing worth getting right first, and
/// worth being able to prove, is WHEN SPEAKING IS ALLOWED. The same order
/// NightMoments, WrittenAhead and NightPromises were built in.
///
/// THE GATE FAILS CLOSED, which is the whole of its value. Every answer
/// that is not an unambiguous yes is a no, and a no is not a silence: the
/// free Kokoro voice says the line instead, exactly as it does for every
/// other refusal on the HeatCaller voice path, and the console says which
/// it was. A club whose consent record has gone missing gets a night in a
/// stock voice and a line telling them why - never a night in somebody's
/// voice that nobody can show an agreement for.
/// </summary>
internal static class VoiceConsent
{
    /// <summary>
    /// A club's record that one named person agreed their voice could be
    /// used.
    ///
    /// NO RECORDING AND NO AUDIO, which is the point of it being this
    /// small. The club holds the recording and whatever they signed;
    /// HeatCaller holds the fact that they did, the name, the day, and
    /// which voice it was for. There is nothing here that is any use to
    /// anybody who takes a copy of it.
    /// </summary>
    /// <param name="VoiceId">
    /// WHICH VOICE IT IS FOR, and this field is doing real work. A consent
    /// is for one person's voice, not for "whatever voice is currently
    /// set" - so a club that clones a second announcer, or switches the
    /// one it uses, does not carry the first person's agreement over to
    /// the second. See MaySpeakWith.
    /// </param>
    /// <param name="Who">
    /// The person who agreed, as the club wrote their name. Blank is not a
    /// consent: an agreement with nobody's name on it is a tick box, and
    /// the whole reason to record this is to be able to say whose voice it
    /// is and who said yes.
    /// </param>
    /// <param name="AtUtc">The day they agreed.</param>
    /// <param name="RevokedAtUtc">
    /// When it was taken back, or null.
    ///
    /// KEPT RATHER THAN DELETED. A revoked consent is not the same as one
    /// that never existed, and a club asked "did you ever have permission
    /// for that recording you posted in March" needs to be able to answer
    /// yes, until the ninth of June. Deleting the row would lose exactly
    /// the fact that makes the club's own position defensible.
    ///
    /// It does not have to be the person who revokes it, and nothing here
    /// assumes it was - a club may take a voice out of use for its own
    /// reasons, and that is the same effect.
    /// </param>
    internal sealed record Record(string VoiceId, string Who, DateTime AtUtc, DateTime? RevokedAtUtc = null);

    /// <summary>Why the club may or may not speak in this voice. Typed, so the reason can be counted and shown rather than matched on its wording - the decision BudgetRefusal records.</summary>
    internal enum Verdict
    {
        /// <summary>Go ahead.</summary>
        MaySpeak,

        /// <summary>No voice is set, so there is nothing to ask about.</summary>
        NoVoiceChosen,

        /// <summary>This voice is not a clone of anybody, so no consent is needed.</summary>
        NotACloneAtAll,

        /// <summary>A cloned voice with no consent recorded against it.</summary>
        NothingRecorded,

        /// <summary>A consent exists, but for a different voice.</summary>
        ForAnotherVoice,

        /// <summary>The consent names nobody.</summary>
        NobodyNamed,

        /// <summary>It was taken back.</summary>
        TakenBack,
    }

    /// <summary>True for the two verdicts that let a line go out in this voice.</summary>
    internal static bool Allows(Verdict verdict) =>
        verdict is Verdict.MaySpeak or Verdict.NotACloneAtAll;

    /// <summary>
    /// Whether a line may be spoken in this voice.
    ///
    /// FAILS CLOSED. Read the order of the checks below as the rule: a
    /// cloned voice speaks only when there is a record, it is for THIS
    /// voice, it names somebody, and it has not been taken back. Anything
    /// else is a no.
    ///
    /// A VOICE THAT IS NOT A CLONE NEEDS NOTHING, and that is not a
    /// loophole - the stock voices are nobody's likeness, which is the
    /// whole reason a catalogue of them exists. The flag comes from the
    /// relay rather than from the laptop, deliberately: the laptop is
    /// never told which voices are clones of whom, so it cannot be the
    /// thing that decides, and an app running an old build cannot answer
    /// "not a clone" about one.
    /// </summary>
    /// <param name="voiceId">The voice about to speak.</param>
    /// <param name="cloned">Whether the relay's catalogue says that voice is a clone of a real person.</param>
    /// <param name="consent">What the club has recorded, or null.</param>
    internal static Verdict MaySpeakWith(string? voiceId, bool cloned, Record? consent)
    {
        if (voiceId is not { Length: > 0 } wanted || wanted.Trim().Length == 0)
        {
            return Verdict.NoVoiceChosen;
        }

        if (!cloned)
        {
            return Verdict.NotACloneAtAll;
        }

        if (consent is null)
        {
            return Verdict.NothingRecorded;
        }

        // THE VOICE IT WAS FOR. Checked before the name and before the
        // revocation, because a consent for another voice tells us nothing
        // about this one either way - and a club that has cloned a second
        // announcer must not find the first one's agreement standing in
        // for theirs.
        if (!string.Equals(consent.VoiceId.Trim(), wanted.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Verdict.ForAnotherVoice;
        }

        if (consent.Who.Trim().Length == 0)
        {
            return Verdict.NobodyNamed;
        }

        if (consent.RevokedAtUtc is not null)
        {
            return Verdict.TakenBack;
        }

        return Verdict.MaySpeak;
    }

    /// <summary>
    /// What the console says, in a club's words rather than a developer's,
    /// and always with what to do about it.
    ///
    /// EVERY REFUSAL NAMES ITS FIX. A club hearing a stock voice all
    /// evening when it expected its own needs to know which of these it
    /// was, and none of them is something they would guess.
    /// </summary>
    internal static string Because(Verdict verdict) => verdict switch
    {
        Verdict.MaySpeak => "the club's own voice is set and its consent is on record",
        Verdict.NoVoiceChosen => "no HeatCaller voice is chosen",
        Verdict.NotACloneAtAll => "this voice is one of the stock ones, which is nobody's own",
        Verdict.NothingRecorded =>
            "this is the club's own announcer's voice and nothing on this PC records that they agreed to it. "
            + "Add who agreed, and when, on the Voice Settings page - until then the free voice reads every line.",
        Verdict.ForAnotherVoice =>
            "the consent on this PC is for a different voice than the one that is set. A consent is for one "
            + "person's voice, so the voice that is set needs its own.",
        Verdict.NobodyNamed =>
            "the consent on this PC does not say who agreed. A name is the whole point of recording it, so it "
            + "is not treated as a consent until there is one.",
        Verdict.TakenBack =>
            "the consent for this voice has been taken back, so nothing is spoken in it. The record of it is "
            + "kept rather than deleted - a club asked whether it ever had permission needs to be able to say so.",
        _ => "of something this version does not have a word for",
    };

    /// <summary>
    /// A consent taken back, keeping everything about the original.
    ///
    /// Idempotent on purpose: taking back something already taken back
    /// keeps the FIRST date, because that is the day it stopped being
    /// allowed and a second press of a button is not a second revocation.
    /// </summary>
    internal static Record TakeBack(Record consent, DateTime atUtc) =>
        consent.RevokedAtUtc is null ? consent with { RevokedAtUtc = atUtc } : consent;

    /// <summary>
    /// How a club's own page says where it stands, in one line.
    ///
    /// Said whether or not anything is wrong, which is the lesson the
    /// colour segment cost three rounds of testing to learn: "it found
    /// nothing" and "it never ran" look identical unless one of them says
    /// so.
    /// </summary>
    internal static string Describe(Record? consent)
    {
        if (consent is null)
        {
            return "No consent is recorded on this PC. A cloned voice will not be used until one is.";
        }

        var who = consent.Who.Trim();
        var named = who.Length > 0 ? who : "somebody not named";

        if (consent.RevokedAtUtc is { } revoked)
        {
            return $"{named} agreed on {Day(consent.AtUtc)}, and it was taken back on {Day(revoked)}. "
                + "Nothing is spoken in that voice.";
        }

        return $"{named} agreed on {Day(consent.AtUtc)}. That is the only record of it - the recording and "
            + "whatever they signed are the club's to keep.";
    }

    /// <summary>
    /// The same line, said against the voice that is set now. A consent
    /// recorded for one voice and then another voice chosen read exactly as
    /// if it covered the new one, while the gate refused it (ForAnotherVoice)
    /// and the free voice read every line with nothing on the page saying
    /// why.
    /// </summary>
    /// <param name="consent">What the club has recorded, or null.</param>
    /// <param name="voiceNow">The HeatCaller voice that is set, or blank.</param>
    /// <param name="nowCloned">Whether that voice is a clone - only then does a consent for another voice keep it quiet.</param>
    internal static string Describe(Record? consent, string? voiceNow, bool nowCloned)
    {
        var line = Describe(consent);
        var now = voiceNow?.Trim() ?? string.Empty;

        if (consent is not { RevokedAtUtc: null }
            || now.Length == 0
            || string.Equals(consent.VoiceId.Trim(), now, StringComparison.OrdinalIgnoreCase))
        {
            return line;
        }

        line += $" It is for the voice {consent.VoiceId.Trim()}, not the one now chosen";

        return nowCloned
            ? line + ", so the free voice reads every line until this one has a consent of its own."
            : line + ".";
    }

    /// <summary>A day as a club writes it, in their own region's order.</summary>
    private static string Day(DateTime utc) =>
        utc.ToLocalTime().ToString("d MMMM yyyy", System.Globalization.CultureInfo.CurrentCulture);
}
