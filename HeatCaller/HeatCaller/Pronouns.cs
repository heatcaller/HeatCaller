namespace HeatCaller;

/// <summary>
/// A driver's pronouns, as their profile has them (DriverProfile.Pronouns)
/// - and the few words the app itself writes about a driver.
///
/// WHY THIS EXISTS (21 Oct 2026)
///
/// The AI commentary guessed. Handed "Sam" and a lap time, a model picks
/// "he" or "she" from the name, and a guess said over the PA about
/// somebody standing in the room is wrong out loud. A club could fix a
/// name the voice got wrong (the Announcer name box) but had no way at
/// all to fix this. So the profile now has a Pronouns box under the
/// Announcer name, and every prompt that names the driver carries them -
/// see CommentaryService.WithPronouns.
///
/// FREE TEXT, NOT A LIST. The drop-down offers the three a club will
/// usually want, but people use others ("she/they", "xe/xem"), and a box
/// that refused somebody's own pronouns would be worse than no box. What
/// is typed is cleaned (<see cref="Clean"/>) because it goes into a model's
/// prompt, and kept short because it can only ever be a pronoun.
///
/// BLANK CHANGES NOTHING. The few sentences the app writes about a driver
/// itself (the team principal's facts, "right on his tail" in a position
/// report) read exactly as they always did for a driver with nothing set -
/// see <see cref="For"/>. Only a driver whose pronouns have been filled in
/// is described differently.
/// </summary>
public static class Pronouns
{
    /// <summary>The longest a pronoun entry can be - "they/them/theirs" with room to spare, and too short to be anything else.</summary>
    public const int MaxChars = 24;

    /// <summary>What the profile's drop-down offers. Anything else can be typed.</summary>
    public static readonly IReadOnlyList<string> Common = new[] { "he/him", "she/her", "they/them" };

    /// <summary>
    /// A pronoun entry made safe to keep and to put in a prompt: letters,
    /// slashes, hyphens and apostrophes only, one space between words, no
    /// spaces around a slash ("she / her" is "she/her"), and no longer than
    /// <see cref="MaxChars"/>. Empty for nothing usable.
    /// </summary>
    public static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var kept = new System.Text.StringBuilder(raw.Length);

        foreach (var ch in raw)
        {
            if (char.IsLetter(ch) || ch is '/' or '-' or '\'' or '’')
            {
                kept.Append(ch);
            }
            else if (char.IsWhiteSpace(ch) || ch == ',')
            {
                kept.Append(' ');
            }
        }

        var text = System.Text.RegularExpressions.Regex.Replace(kept.ToString(), @"\s+", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s*/\s*", "/");
        text = System.Text.RegularExpressions.Regex.Replace(text, "/{2,}", "/").Trim(' ', '/');

        if (text.Length > MaxChars)
        {
            text = text[..MaxChars].TrimEnd(' ', '/', '-');
        }

        return text;
    }

    /// <summary>
    /// The words the app uses itself when it writes a sentence about a
    /// driver.
    ///
    /// Read from the FIRST pronoun given, which is the one a person with
    /// two ("she/they") puts first. Blank is <see cref="He"/> - exactly what
    /// those sentences always said, so a driver with nothing filled in is
    /// described as before. A set this does not know ("xe/xem") gets the
    /// neutral <see cref="They"/>: the sentence stays grammatical, and the
    /// AI commentary, which is handed their pronouns as typed, uses them.
    /// </summary>
    public static PronounSet For(string? raw)
    {
        var first = Clean(raw).Split('/', ' ').FirstOrDefault(word => word.Length > 0)?.ToLowerInvariant();

        return first switch
        {
            null or "" => He,
            "he" or "him" or "his" => He,
            "she" or "her" or "hers" => She,
            _ => They,
        };
    }

    /// <summary>"he", "him", "his", "himself".</summary>
    public static readonly PronounSet He = new("he", "him", "his", "himself");

    /// <summary>"she", "her", "her", "herself".</summary>
    public static readonly PronounSet She = new("she", "her", "her", "herself");

    /// <summary>"they", "them", "their", "themselves".</summary>
    public static readonly PronounSet They = new("they", "them", "their", "themselves");
}

/// <summary>One set of pronouns, by the job each word does in a sentence. See <see cref="Pronouns.For"/>.</summary>
public sealed record PronounSet(string Subject, string Object, string Possessive, string Reflexive);
