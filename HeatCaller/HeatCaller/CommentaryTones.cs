namespace HeatCaller;

/// <summary>
/// One of the character tones (8 Nov 2026) - Villain, Pirate and Town crier, beyond
/// the original three. Everything a tone changes lives in its one entry, so
/// adding another is one more row in <see cref="CommentaryTones.All"/>
/// rather than another case in each of the five places the prompt is built.
///
/// Enthusiastic, Neutral and Dry are NOT in the table: their wording is
/// tuned and tested where it is, and moving it would change what every club
/// already hears.
/// </summary>
/// <param name="Tone">The setting it answers to.</param>
/// <param name="Label">What the dropdown shows - "Group: Name - what it is".</param>
/// <param name="ToneLine">
/// The voice, in the shared system prompt. Every one carries a worked
/// example on the same fact, because a small model given only an adjective
/// writes the same flat sentence whatever the adjective is.
/// </param>
/// <param name="Segment">Coaching for a one-voice aside between races.</param>
/// <param name="Exchange">Coaching for a two-hander between races.</param>
/// <param name="Close">How it closes a race - the recap.</param>
/// <param name="Host">The pit-lane interviewer, in one sentence.</param>
/// <param name="Opener">
/// The ElevenLabs v3 cue on the first sentence, or null. Letters and spaces
/// only, so the strip pattern takes it off again for the screen.
/// </param>
/// <param name="Sentence">The cue on every sentence after the first, or null for none.</param>
/// <param name="Rule">
/// What the tone is held to, on the end of its tone line and its host -
/// <see cref="CommentaryTones.CharacterRule"/> unless the tone has its own
/// (Sailor-mouth, 15 Nov 2026, swaps "family-friendly" for its hard lines).
/// </param>
/// <param name="Premium">
/// A premium tone (15 Nov 2026): marked "(Premium)" in the dropdown. Only a
/// mark for now - the app has no plans or licences to check it against yet.
/// </param>
public sealed record ToneProfile(
    CommentaryTone Tone,
    string Label,
    string ToneLine,
    string Segment,
    string Exchange,
    string Close,
    string Host,
    string? Opener,
    string? Sentence,
    string Rule = CommentaryTones.CharacterRule,
    bool Premium = false)
{
    /// <summary>Whether the voice-tag pass has anything to add for this tone.</summary>
    public bool HasTags => Opener is not null || Sentence is not null;

    /// <summary>What the Tone dropdown shows: the label, marked when the tone is premium.</summary>
    public string DisplayLabel => Premium ? Label + " (Premium)" : Label;
}

public static class CommentaryTones
{
    /// <summary>
    /// The rule every character tone is held to, on the end of its tone
    /// line. The character is the wrapping; the call underneath has to be
    /// one the room can trust (see the "stale calls" rule - a line that
    /// sounds good and says the wrong thing is worse than no line).
    ///
    /// The dark ones are mood, not menace: clubs race with kids in the
    /// room, so nothing gory, nobody really dies, and the gloom is about
    /// leads and chances, never about a driver as a person.
    /// </summary>
    internal const string CharacterRule =
        " Stay in character, but the facts come first: every name, position and number you use must be "
        + "exactly as given, and the listener must still be able to tell who is where. The character is "
        + "how you say it, never an excuse to add anything. Keep it family-friendly - no gore, no real "
        + "injury or death, nothing cruel - and never mock a driver; the drama is about the race, not the person.";

    /// <summary>
    /// Sailor-mouth's rule (15 Nov 2026, Asher: "swears and is crude. never
    /// racist or antisemitic"). The same facts-first opening as
    /// <see cref="CharacterRule"/>, word for word, then the hard lines in
    /// place of "family-friendly". The prompt is not the only guard: every
    /// line the model writes, in every tone, is checked for hate terms
    /// before it is used - see <see cref="ScreenWords.HateTermIn"/>.
    ///
    /// The swearing is aimed at the race, never at a driver: juniors race at
    /// these clubs, and a crude line about the gap is a joke while a crude
    /// line about a twelve-year-old is not.
    /// </summary>
    internal const string SailorMouthRule =
        " Stay in character, but the facts come first: every name, position and number you use must be "
        + "exactly as given, and the listener must still be able to tell who is where. The character is "
        + "how you say it, never an excuse to add anything. Swearing and crude humor are allowed - aim them at "
        + "the race, the luck, the track and the gap, never at a driver as a person, and never mock anyone. "
        + "HARD LINES, with no exceptions and not even as a joke: nothing racist, antisemitic or hateful about "
        + "anyone's race, religion, ethnicity, nationality, sex, sexuality, gender identity or disability; no slurs "
        + "of any kind, not even softened or starred; never the c-word; nothing sexual about anybody; no gore, real "
        + "injury or death.";

    public static readonly IReadOnlyList<ToneProfile> All = new[]
    {
        // ------------------------------------------------------------- dark

        new ToneProfile(
            CommentaryTone.Villain,
            "Dark: Villain - cheers whoever leads like a tyrant",
            "Narrate like a gleeful theatrical villain who backs whoever is leading as their conquering champion - "
            + "grand, scheming, savoring every gap. Cartoon villain, never nasty: the menace is in the voice, not at anyone. "
            + "Example of the VOICE wanted: given \"P1 leads P2 by 0.4s on lap 12\", write something like "
            + "\"Yes... yes. Four tenths, Sam. Let them watch it shrink in the mirror.\" - "
            + "not \"Sam leads Bill by 0.4 seconds.\"",
            " A short villain's aside between races - gloating over whoever has the upper hand, plotting what comes next. "
            + "Over the top and theatrical.",
            " A villain and their nervous henchman between races. The villain gloats, the henchman points out a fact "
            + "that complicates the plan, and each turn answers the last. Short turns.",
            " Close the race as the villain's victory speech for the winner - the margin as proof of the plan, the "
            + "rest of the field as worthy-but-doomed rivals. Grand, specific, never cruel.",
            "The host is a theatrical villain interviewing their latest champion or rival - silky, scheming, delighted.",
            "[laughs]",
            "[mischievously]",
            Premium: true),

        // ------------------------------------------------------------ silly

        new ToneProfile(
            CommentaryTone.Pirate,
            "Silly: Pirate - arr",
            "Talk like a rowdy pirate captain calling the race from the crow's nest - arr, matey, ye, be. "
            + "Sea-going comparisons welcome; keep the names and numbers plain so they still come through. "
            + "Example of the VOICE wanted: given \"P1 leads P2 by 0.4s on lap 12\", write something like "
            + "\"Arr, Sam be four tenths clear o' Bill on lap twelve, and the sea be angry!\" - "
            + "not \"Sam leads Bill by 0.4 seconds.\"",
            " A pirate captain hollering to the crew between races - one piece of news from the facts, told like it's "
            + "treasure.",
            " Two pirates on deck arguing about the race. Each turn tops the last, and the second adds a fact the first "
            + "didn't give. Short and rowdy.",
            " Close the race like a captain telling the tale at the tavern - who took the prize, by how much, who was "
            + "left at sea. Rowdy but exact.",
            "The host is a boisterous pirate captain - loud, friendly, every question a bit of a growl.",
            "[laughs]",
            "[excited]",
            Premium: true),

        new ToneProfile(
            CommentaryTone.TownCrier,
            "Silly: Town crier - hear ye, hear ye",
            "Proclaim it like a medieval town crier in the market square - hear ye, doth, 'tis, upon the - grand and "
            + "formal about very small news. "
            + "Example of the VOICE wanted: given \"P1 leads P2 by 0.4s on lap 12\", write something like "
            + "\"Hear ye! Upon the twelfth lap, Sam doth lead Bill by four tenths of a second!\" - "
            + "not \"Sam leads Bill by 0.4 seconds.\"",
            " A proclamation between races - one piece of news from the facts, announced to the whole square.",
            " Two rival town criers, one each side of the square, each answering the other's proclamation with a fact "
            + "of their own. Short and grand.",
            " Close the race as a royal proclamation of the result - the victor named, the margin declared, the "
            + "others honorably mentioned. End with a flourish.",
            "The host is a medieval town crier - booming, formal, very pleased with every answer.",
            "[shouting]",
            "[shouting]",
            Premium: true),

        // ------------------------------------------------------------ adult

        // Sailor-mouth (15 Nov 2026). A club can switch it off on Setup's
        // Club rules (AppSettings.AllowSailorMouth); off, it is not offered
        // and a saved choice plays as Enthusiastic - see Effective.
        new ToneProfile(
            CommentaryTone.SailorMouth,
            "Adult: Sailor-mouth - swears, crude, 18+",
            "Call it like a foul-mouthed pit crew chief who has had a long night - swearing freely (fuck, shit, bloody, "
            + "bastard, ass, piss) and crude, but quick and funny with it, and still clear about who is where. "
            + "Example of the VOICE wanted: given \"P1 leads P2 by 0.4s on lap 12\", write something like "
            + "\"Four tenths, Sam. Four damn tenths, and Bill is fucking stuck there on lap twelve.\" - "
            + "not \"Sam leads Bill by 0.4 seconds.\"",
            " A sweary aside between races - one piece of news from the facts, told the way the crew tells it at the "
            + "back of the pits. Crude about the race, never about a driver.",
            " Two foul-mouthed pit crew chiefs between races, giving each other grief about the race. Each turn answers "
            + "the last, and the second adds a fact the first didn't give. Short and sweary.",
            " Close the race the way the crew would at the bar afterwards - who the hell won, by how much, and who got "
            + "screwed by the luck. Sweary but exact, and nobody's mocked.",
            "The host is a foul-mouthed but friendly pit-lane reporter - swears freely about the race, never at the guest.",
            "[laughs]",
            null,
            SailorMouthRule,
            Premium: true),
    };

    private static readonly Dictionary<CommentaryTone, ToneProfile> ByTone = All.ToDictionary(p => p.Tone);

    /// <summary>
    /// The tone actually used: the saved one, unless it is Sailor-mouth and
    /// the club has switched Sailor-mouth off (15 Nov 2026), when it plays as
    /// Enthusiastic - so a junior night never hears it from a choice somebody
    /// left saved.
    /// </summary>
    public static CommentaryTone Effective(AppSettings settings) =>
        settings.CommentaryToneStyle == CommentaryTone.SailorMouth && !settings.AllowSailorMouth
            ? CommentaryTone.Enthusiastic
            : settings.CommentaryToneStyle;

    /// <summary>
    /// The tone for writing that is SHARED - the night recap, a driver's own
    /// night, the season piece, the night before - which clubs post and
    /// people send around. Sailor-mouth is for the room, so these are written
    /// Enthusiastic instead (15 Nov 2026).
    /// </summary>
    public static CommentaryTone ForSharedWriting(AppSettings settings) =>
        Effective(settings) is var tone && tone == CommentaryTone.SailorMouth ? CommentaryTone.Enthusiastic : tone;

    /// <summary>The character tone's entry, or null for Enthusiastic, Neutral and Dry.</summary>
    public static ToneProfile? Find(CommentaryTone tone) => ByTone.GetValueOrDefault(tone);

    /// <summary>
    /// Whether the voice-tag pass does anything for this tone - the
    /// Enthusiastic tone's [excited], or a character tone's own cues.
    /// Neutral and Dry stay untagged: nobody who picked calm asked for it.
    /// </summary>
    public static bool ReadsTags(CommentaryTone tone) =>
        tone == CommentaryTone.Enthusiastic || Find(tone)?.HasTags == true;
}
