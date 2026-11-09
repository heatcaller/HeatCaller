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
public sealed record ToneProfile(
    CommentaryTone Tone,
    string Label,
    string ToneLine,
    string Segment,
    string Exchange,
    string Close,
    string Host,
    string? Opener,
    string? Sentence)
{
    /// <summary>Whether the voice-tag pass has anything to add for this tone.</summary>
    public bool HasTags => Opener is not null || Sentence is not null;
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
            "[mischievously]"),

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
            "[excited]"),

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
            "[shouting]"),
    };

    private static readonly Dictionary<CommentaryTone, ToneProfile> ByTone = All.ToDictionary(p => p.Tone);

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
