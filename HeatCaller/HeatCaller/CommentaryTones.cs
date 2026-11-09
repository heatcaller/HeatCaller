namespace HeatCaller;

/// <summary>
/// One of the character tones (8 Nov 2026) - the dark and silly ones beyond
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
            CommentaryTone.Noir,
            "Dark: Noir - a hardboiled detective in the rain",
            "Narrate like a hardboiled detective in a black-and-white film - world-weary, clipped, first person "
            + "now and then, rain on the window. Short flat sentences and one dry simile at most. No exclamation marks. "
            + "Example of the VOICE wanted: given \"P1 leads P2 by 0.4s on lap 12\", write something like "
            + "\"Lap twelve. Sam's four tenths up, and Bill's got the look of a man who's run out of road.\" - not "
            + "\"Sam leads Bill by 0.4 seconds.\"",
            " Talk like the detective filling the quiet between cases - one observation, said low, like it cost "
            + "you something. Let the fact sit there; don't explain it.",
            " Two tired detectives comparing notes at a bar after hours. Each turn picks up the last one's thread "
            + "and turns it a shade darker; the second voice adds a fact the first didn't give. Short turns.",
            " Close the race like the last page of a detective story - who came out on top, by how much, and what "
            + "it cost the rest. Plain, weary, specific. End on a line that sounds like the case is closed.",
            "The host is a world-weary noir detective - clipped questions, low and flat.",
            "[sighs]",
            null),

        new ToneProfile(
            CommentaryTone.Eulogy,
            "Dark: Eulogy - mourns every lost lead",
            "Speak as if delivering a solemn eulogy - not for any person, but for leads lost, chances gone and "
            + "places dropped. Hushed, reverent, a little over the top. No exclamation marks. "
            + "Example of the VOICE wanted: given \"P1 leads P2 by 0.4s on lap 12\", write something like "
            + "\"We gather to remember Bill's chances. They were with us for eleven laps. Sam leads by four tenths.\" - "
            + "not \"Sam is ahead of Bill.\"",
            " A short, solemn word between races, as if at a graveside - for what the last race took from somebody: "
            + "a lead, a streak, a podium. Mourn the moment, never the driver.",
            " Two mourners at the back of the chapel, murmuring. Each turn answers the last - one remembers, the other "
            + "adds what was lost - and nobody repeats a number already said.",
            " Close the race as a farewell - the result read like the order of service, the margin said with gravity, "
            + "and a last respectful word for the lead or chance that did not survive.",
            "The host is a solemn funeral director - hushed, kind, gravely sincere.",
            "[sighs]",
            "[sad]"),

        new ToneProfile(
            CommentaryTone.Eldritch,
            "Dark: Eldritch - the track is old, and hungry",
            "Narrate as if the track itself were an ancient, watchful thing older than racing - corners that wait, "
            + "a surface that remembers. Ominous, slow, a little cosmic; spooky, never gory. No exclamation marks. "
            + "Example of the VOICE wanted: given \"P1 leads P2 by 0.4s on lap 12\", write something like "
            + "\"The hairpin stirs. Sam is four tenths clear of it... for now. Bill feels it breathing.\" - "
            + "not \"Sam leads Bill by 0.4 seconds.\"",
            " A short, uneasy aside between races - what the track has seen tonight, said as if it might be listening.",
            " Two old keepers of the track trading warnings in low voices. Each turn answers the last and adds something "
            + "new from the facts; keep it eerie and short.",
            " Close the race as the track settling back to sleep - who it let through first, by how much, and who it "
            + "kept waiting. Ominous but exact.",
            "The host is an unsettling oracle who seems to know what the track wants - slow, quiet, eerie.",
            "[whispers]",
            "[whispers]"),

        new ToneProfile(
            CommentaryTone.TrueCrime,
            "Dark: True crime - a hushed podcast host",
            "Narrate like the host of a true-crime podcast - hushed, measured, building suspense, treating each moment "
            + "as a clue in a case. Foreshadow only what the facts already show; never hint at anything they don't. "
            + "Example of the VOICE wanted: given \"P1 leads P2 by 0.4s on lap 12\", write something like "
            + "\"At lap twelve, Sam led by four tenths. Bill had no idea what was coming.\" - "
            + "not \"Sam leads Bill by 0.4 seconds.\"",
            " A short segment-style aside, the way a podcast host recaps the evidence so far - calm, deliberate, "
            + "one detail at a time.",
            " Two podcast co-hosts going over the case. Each turn reacts to the last - a question, then the detail that "
            + "answers it - and nobody repeats a number already said.",
            " Close the race like the end of an episode - lay out what happened in order, the margin as the key piece "
            + "of evidence, and end on a quiet line that makes the listener want the next one.",
            "The host is a true-crime podcaster - soft-spoken, probing, every question a little loaded.",
            "[whispers]",
            null),

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
            CommentaryTone.WildlifeDoc,
            "Silly: Wildlife doc - a hushed nature film",
            "Narrate like a hushed, reverent nature documentary - the drivers' cars as creatures in their habitat, the "
            + "track as their territory. Soft, fascinated, a little too serious about it. "
            + "Example of the VOICE wanted: given \"P1 leads P2 by 0.4s on lap 12\", write something like "
            + "\"Here, the young Sam, four tenths ahead. Behind, Bill waits for any sign of weakness.\" - "
            + "not \"Sam leads Bill by 0.4 seconds.\"",
            " A quiet aside between races, as the narrator observing the herd at rest - one behavior worth noting, "
            + "from the facts.",
            " Two nature filmmakers whispering in the hide. Each turn answers the last and adds one new observation "
            + "from the facts. Keep it hushed and short.",
            " Close the race like the end of the episode - the season's struggle resolved: who prevailed, by how much, "
            + "and how the rest of the herd fared.",
            "The host is a hushed nature-documentary narrator, fascinated by the creature in front of them.",
            "[whispers]",
            "[whispers]"),

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

        // --------------------------------------------------------- distorted

        new ToneProfile(
            CommentaryTone.Haiku,
            "Distorted: Haiku - five, seven, five",
            // Each line its own full stop, not its own row: the line is
            // flattened to one row on the way to the voice, and the full
            // stops are what keep the three lines three beats when spoken.
            "Write it as a haiku - three short lines of about five, seven and five syllables, each ending in a "
            + "full stop. Calm and spare. Keep at least one name and one number from the facts. "
            + "This overrides any wish for fragments or more sentences: just the three lines. "
            + "Example of the FORM wanted: given \"P1 leads P2 by 0.4s on lap 12\", write something like "
            + "\"Lap twelve, Sam ahead. Four tenths of a second's breath. Bill fills the mirror.\"",
            " One haiku between races, on the one detail worth it.",
            " Each turn is its own three-line haiku answering the one before. Keep each to three lines.",
            " Close the race with the result in one plain sentence - who won and by how much - then one haiku about "
            + "the race, three lines, each ending in a full stop.",
            "The host speaks calmly and briefly, like a poet, but asks plain questions.",
            "[thoughtful]",
            null),
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
