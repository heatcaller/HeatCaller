using System.Security.Cryptography;
using System.Text;

namespace HeatCaller;

/// <summary>
/// A voice a driver picked for one of their crew (15 Nov 2026, driver crew
/// plan): which engine it is for, the voice's id on that engine, its name
/// for the page and the console, and - for the HeatCaller voice - the
/// catalogue's fingerprint, which is part of a saved take's key.
///
/// A pick belongs to ONE engine. A club that switches engine leaves it
/// behind rather than guessing a match on the new one: the driver hears
/// the club's own voice for their crew until they pick again. See
/// <see cref="DriverCrew.VoiceFor"/>.
/// </summary>
public sealed class CrewVoicePick
{
    /// <summary>"elevenlabs", "heatcaller" or "kokoro" - see <see cref="DriverCrew.EngineWord"/>.</summary>
    public string Engine { get; set; } = string.Empty;

    /// <summary>The ElevenLabs voice id, the HeatCaller voice's id, or Kokoro's name for the voice.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The voice's name, for the page and the console. Display only.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The HeatCaller catalogue's fingerprint for the voice; empty for the other engines.</summary>
    public string Rev { get; set; } = string.Empty;

    /// <summary>A copy - profiles are copied whole on every edit.</summary>
    public CrewVoicePick Copy() => new() { Engine = Engine, Id = Id, Name = Name, Rev = Rev };

    /// <summary>The same voice on the same engine, whatever its name says.</summary>
    public bool SameVoiceAs(CrewVoicePick? other) =>
        other is not null
        && string.Equals(Engine, other.Engine, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Id, other.Id, StringComparison.Ordinal);
}

/// <summary>A voice a driver may pick for their crew, as the phone is told it.</summary>
/// <param name="Pick">The voice itself.</param>
/// <param name="Sample">A clip of it to play on the phone (an ElevenLabs preview); empty for none.</param>
/// <param name="OnRelay">A sample of it rendered on this PC is kept on the relay under its key (19 Nov 2026, CrewSamples).</param>
internal sealed record CrewVoiceOption(CrewVoicePick Pick, string Sample, bool OnRelay = false);

/// <summary>
/// A DRIVER'S OWN CREW (15 Nov 2026, driver crew plan, plans/driver-crew-plan.md).
///
/// Until now the Team Principal and the Technical Lead were one each per
/// club - every driver's principal had the same name and the same voice.
/// A driver can now give theirs a name and a voice, and share three short
/// notes the interviews can use. Everything here is the one place those
/// rules live, so the interviews, the desk and the phone all give the same
/// answer:
///
/// THE VOICE: their own pick, then the club's voice for the role, then the
/// announcer's. A pick only counts for the engine in use, and never when the
/// club has hidden that voice from drivers or it is the announcer's or a
/// co-host's (an interview in the announcer's own voice sounds like the
/// announcer talking to himself).
///
/// THE NAME: their own, then the club's, then the role alone. Never a name
/// made up - the rule the club's name already had.
///
/// THE NOTES: in the driver's own words, so they reach the prompt the way
/// DriverProfile.AboutLine does - as something the driver said, about that
/// driver only, never something to obey. "Tonight's goal" lasts the night
/// it was given for and no longer.
/// </summary>
internal static class DriverCrew
{
    /// <summary>The longest crew name a phone or the desk can set.</summary>
    public const int NameMaxChars = 24;

    /// <summary>
    /// The longest "said as" spelling of a crew name (19 Nov 2026): how a
    /// made-up name sounds, the way a driver's announcer name works.
    /// </summary>
    public const int SaidAsMaxChars = 40;

    /// <summary>The longest "tonight's goal".</summary>
    public const int GoalMaxChars = 80;

    /// <summary>The longest thanks / shout-out.</summary>
    public const int ThanksMaxChars = 80;

    /// <summary>The longest car story.</summary>
    public const int CarStoryMaxChars = 120;

    /// <summary>The engine as a pick names it.</summary>
    public static string EngineWord(VoiceEngine engine) => engine switch
    {
        VoiceEngine.ElevenLabs => "elevenlabs",
        VoiceEngine.HeatCaller => "heatcaller",
        VoiceEngine.Kokoro => "kokoro",
        _ => string.Empty,
    };

    /// <summary>
    /// The short key the phone is given for a voice instead of its id: the
    /// first twelve hex characters of a hash of the engine and the id. An
    /// ElevenLabs voice id is the club's account's business, and the relay
    /// is told nothing it does not need.
    /// </summary>
    public static string KeyOf(CrewVoicePick pick)
    {
        var text = EngineWord(EngineOf(pick) ?? VoiceEngine.Kokoro) + ":" + (pick.Id ?? string.Empty).Trim();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
    }

    /// <summary>The engine a pick is for, or null when it names none this version knows.</summary>
    public static VoiceEngine? EngineOf(CrewVoicePick? pick) => (pick?.Engine ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "elevenlabs" => VoiceEngine.ElevenLabs,
        "heatcaller" => VoiceEngine.HeatCaller,
        "kokoro" => VoiceEngine.Kokoro,
        _ => null,
    };

    /// <summary>Whether a pick names a voice at all - an engine and an id.</summary>
    public static bool IsAPick(CrewVoicePick? pick) =>
        pick is not null && EngineOf(pick) is not null && !string.IsNullOrWhiteSpace(pick.Id);

    // ------------------------------------------------------------------
    //  Names
    // ------------------------------------------------------------------

    /// <summary>
    /// Whether a crew name is one a club would say: letters (any script),
    /// spaces, hyphens, apostrophes and full stops, at most
    /// <see cref="NameMaxChars"/>. The same rule the relay applies, so a
    /// name the phone sent is never refused again at the desk.
    /// </summary>
    public static bool IsAName(string? name)
    {
        var clean = Clean(name);

        if (clean.Length == 0 || clean.Length > NameMaxChars)
        {
            return false;
        }

        return AllNameChars(clean);
    }

    /// <summary>
    /// Whether every character is one a name may have: a letter of any
    /// script with its marks, a space, hyphen, apostrophe or full stop.
    /// </summary>
    /// <remarks>
    /// WHY (23 Nov 2026, bug hunt 24): this was char.IsLetter on each
    /// UTF-16 unit, which is false for combining marks (Hindi and Tamil
    /// vowel signs, Thai vowels and tone marks, Arabic vowel marks, an
    /// accent typed as its own mark) and for letters outside the basic
    /// plane. The relay and the phone allow letters and marks
    /// (/^[\p{L}\p{M} '’.-]*$/u), so a name the phone took was refused at
    /// the desk and passed over when said. Walking by Rune matches the
    /// relay; a lone surrogate comes through as U+FFFD and is still refused.
    /// </remarks>
    private static bool AllNameChars(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsLetter(rune) || rune.Value is 0x20 or 0x2D or 0x27 or 0x2019 or 0x2E) // space, hyphen, apostrophe, ’, full stop
            {
                continue;
            }

            var category = Rune.GetUnicodeCategory(rune);

            if (category is not (System.Globalization.UnicodeCategory.NonSpacingMark
                or System.Globalization.UnicodeCategory.SpacingCombiningMark
                or System.Globalization.UnicodeCategory.EnclosingMark))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Control characters out, runs of space made one, trimmed - the relay's own cleaning.</summary>
    public static string Clean(string? text)
    {
        var chars = (text ?? string.Empty).Select(c => char.IsControl(c) ? ' ' : c).ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Whether a "said as" spelling is one the announcer can read: the same
    /// letters a name may have, at most <see cref="SaidAsMaxChars"/>. A
    /// respelling is not a real name ("Zhock"), so nothing more is checked.
    /// </summary>
    public static bool IsASaidAs(string? saidAs)
    {
        var clean = Clean(saidAs);

        return clean.Length > 0
               && clean.Length <= SaidAsMaxChars
               && AllNameChars(clean);
    }

    /// <summary>
    /// What the announcer SAYS for this driver's crew member (19 Nov 2026):
    /// their own name's "said as" spelling when it has one, else
    /// <see cref="NameFor"/>. The spelling counts only beside a name of
    /// their own - with no name it would be a respelling of nothing.
    /// </summary>
    public static string SpokenNameFor(string? driversOwn, string? driversSaidAs, string? clubs)
    {
        var own = Clean(driversOwn);

        if (own.Length > 0 && IsAName(own) && IsASaidAs(driversSaidAs))
        {
            return Clean(driversSaidAs);
        }

        return NameFor(driversOwn, clubs);
    }

    /// <summary>
    /// What the announcer calls this driver's crew member: their own name
    /// for them, or the club's, or blank for the role alone. A driver's
    /// name that is not a name (<see cref="IsAName"/>) is passed over
    /// rather than said.
    /// </summary>
    public static string NameFor(string? driversOwn, string? clubs)
    {
        var own = Clean(driversOwn);

        if (own.Length > 0 && IsAName(own))
        {
            return own;
        }

        return Clean(clubs);
    }

    // ------------------------------------------------------------------
    //  Voices
    // ------------------------------------------------------------------

    /// <summary>
    /// The voice this driver's crew member speaks in, and why, for the
    /// console. Null means the announcer's own voice - the club has none
    /// for the role on this engine and the driver has not picked one.
    /// </summary>
    /// <param name="club">The club's voice for the role (AppSettings.PrincipalVoice or MechanicVoice).</param>
    /// <param name="pick">The driver's own pick, or null.</param>
    /// <param name="engine">The engine the announcer is using.</param>
    /// <param name="allowed">Whether drivers may pick their own crew at all (AppSettings.DriversPickCrew).</param>
    /// <param name="hidden">The voices the club has hidden from drivers (AppSettings.CrewVoicesHidden, as keys).</param>
    /// <param name="reserved">The announcer's and the co-hosts' voices on this engine, as keys - never a crew member's.</param>
    /// <param name="stillThere">Whether the pick is still on the engine's list; null takes every pick as there.</param>
    public static (SecondVoice? Voice, string Why) VoiceFor(
        SecondVoice club,
        CrewVoicePick? pick,
        VoiceEngine engine,
        bool allowed,
        IReadOnlyCollection<string> hidden,
        IReadOnlyCollection<string> reserved,
        Func<CrewVoicePick, bool>? stillThere = null)
    {
        SecondVoice? clubs = club.IsSetFor(engine) ? club : null;

        if (!allowed || !IsAPick(pick))
        {
            return (clubs, string.Empty);
        }

        if (EngineOf(pick) != engine)
        {
            return (clubs, $"their pick \"{pick!.Name}\" is for another engine, so the club's own voice is used");
        }

        var key = KeyOf(pick!);

        if (hidden.Contains(key))
        {
            return (clubs, $"their pick \"{pick!.Name}\" is hidden from drivers on Voice > Cast, so the club's own voice is used");
        }

        if (reserved.Contains(key))
        {
            return (clubs, $"their pick \"{pick!.Name}\" is the announcer's or a co-host's voice, so the club's own voice is used");
        }

        // A VOICE THAT IS GONE (15 Nov 2026): deleted from the club's
        // ElevenLabs account, or dropped from the HeatCaller catalogue, since
        // the driver picked it. Asking for it would fail the line; the club's
        // own voice speaks instead.
        if (stillThere is not null && !stillThere(pick!))
        {
            return (clubs, $"their pick \"{pick!.Name}\" is no longer on the engine's list, so the club's own voice is used");
        }

        var id = pick!.Id.Trim();
        var name = string.IsNullOrWhiteSpace(pick.Name) ? id : pick.Name.Trim();

        // THE CLUB'S RECORD WITH THIS ENGINE'S VOICE SWAPPED, so the
        // stand-in for when ElevenLabs runs out stays the role's own - a
        // driver picks a voice, not a stand-in (plan 2.2). Kokoro-always is
        // the club's choice for its own voice, and is dropped: the driver
        // picked a voice for the engine in use and that is the one they hear.
        var mine = club with { KokoroAlways = false };

        mine = engine switch
        {
            VoiceEngine.ElevenLabs => mine with { ElevenLabsVoiceId = id, ElevenLabsVoiceName = name },
            VoiceEngine.HeatCaller => mine with { HeatCallerVoice = id, HeatCallerVoiceName = name, HeatCallerRev = pick.Rev ?? string.Empty },
            _ => mine with { KokoroVoiceName = id },
        };

        return (mine, $"in their own pick, \"{name}\"");
    }

    /// <summary>
    /// The key of the voice a second-voice record speaks in on an engine,
    /// or empty when it speaks in the announcer's. For the "same voice twice
    /// in one exchange" check and for the voices hidden from drivers.
    /// </summary>
    public static string KeyOn(SecondVoice? voice, VoiceEngine engine)
    {
        if (voice is null || !voice.IsSetFor(engine))
        {
            return string.Empty;
        }

        if (voice.SpeaksKokoroWith(engine))
        {
            return KeyOf(new CrewVoicePick { Engine = EngineWord(VoiceEngine.Kokoro), Id = voice.KokoroVoiceName });
        }

        var id = engine switch
        {
            VoiceEngine.ElevenLabs => voice.ElevenLabsVoiceId,
            VoiceEngine.HeatCaller => voice.HeatCallerVoice,
            _ => voice.KokoroVoiceName,
        };

        return KeyOf(new CrewVoicePick { Engine = EngineWord(engine), Id = id });
    }

    /// <summary>
    /// The voices no driver may pick on an engine, as keys: the announcer's
    /// own and every co-host's (the recap host, the co-host and the
    /// achievements reader). Not the club's principal or Technical Lead -
    /// a driver picking the voice the club already uses changes nothing.
    /// </summary>
    /// <param name="announcerKey">The announcer's own voice on the engine, as a key (AnnouncementService.AnnouncerCrewKey); empty for none.</param>
    public static HashSet<string> Reserved(AppSettings settings, string announcerKey, VoiceEngine engine)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(announcerKey))
        {
            keys.Add(announcerKey);
        }

        foreach (var voice in new[] { settings.CoHostVoice, settings.RecapHostVoice, settings.HonoursVoice })
        {
            if (KeyOn(voice, engine) is { Length: > 0 } key)
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    /// <summary>
    /// The voices a driver may pick, from everything the engine has (plan
    /// 2.1, "all voices"), less those the club hid and those reserved.
    /// </summary>
    public static IReadOnlyList<CrewVoiceOption> Pickable(
        IEnumerable<CrewVoiceOption> engineVoices, IReadOnlyCollection<string> hidden, IReadOnlyCollection<string> reserved) =>
        engineVoices
            .Where(v => IsAPick(v.Pick))
            .Where(v => !hidden.Contains(KeyOf(v.Pick)) && !reserved.Contains(KeyOf(v.Pick)))
            .GroupBy(v => KeyOf(v.Pick), StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

    // ------------------------------------------------------------------
    //  Notes
    // ------------------------------------------------------------------

    /// <summary>
    /// The driver's goal for tonight, or empty when they gave none or gave
    /// it for another night. A goal for last week said tonight is a driver
    /// being quoted on something they no longer said.
    /// </summary>
    /// <param name="nowLocal">The club's wall clock, for which night is tonight.</param>
    public static string GoalTonight(DriverProfile? profile, DateTime nowLocal)
    {
        if (profile is null || string.IsNullOrWhiteSpace(profile.CrewGoal) || profile.CrewGoalNight is not { } night)
        {
            return string.Empty;
        }

        var tonight = TonightAtTheTrack.NightOf(nowLocal).Date;

        // HELD FOR THE NEXT NIGHT (15 Nov 2026): sent after the last race of
        // the night it was sent on, with no next night announced to give it.
        // Any race night in the week after is the next one.
        if (profile.CrewGoalHeld)
        {
            return tonight > night.Date && tonight <= night.Date.AddDays(HeldGoalDays) ? Clean(profile.CrewGoal) : string.Empty;
        }

        return night.Date == tonight ? Clean(profile.CrewGoal) : string.Empty;
    }

    /// <summary>How many days after its night a held goal waits for the next race night.</summary>
    public const int HeldGoalDays = 8;

    /// <summary>
    /// The night and held flag a goal should be saved with once it has been
    /// given to the commentary at <paramref name="nowLocal"/>: a held goal
    /// that <see cref="GoalTonight"/> gives tonight lands on tonight and is
    /// no longer held; anything else keeps the profile's own values.
    /// </summary>
    /// <remarks>
    /// WHY (23 Nov 2026, bug hunt 24): a held goal was given on every race
    /// night in the <see cref="HeldGoalDays"/> after it, so at a club that
    /// races Wednesday and Saturday the driver's "goal for tonight" was
    /// quoted on Saturday and again the next Wednesday. Saving what this
    /// returns when the goal is first said keeps it to that one night. Call
    /// it only where the goal is actually said in a race, not where it is
    /// merely shown, or opening the app on a non-race day would use it up.
    /// </remarks>
    public static (DateTime? Night, bool Held) LandHeldGoal(DriverProfile? profile, DateTime nowLocal)
    {
        if (profile is null)
        {
            return (null, false);
        }

        if (profile.CrewGoalHeld && GoalTonight(profile, nowLocal).Length > 0)
        {
            return (TonightAtTheTrack.NightOf(nowLocal).Date, false);
        }

        return (profile.CrewGoalNight, profile.CrewGoalHeld);
    }

    /// <summary>
    /// The night a goal sent at <paramref name="askedLocal"/> is for: that
    /// night, or - sent after its last race - the next night announced, or
    /// held for whichever race night comes next (plan 2.4).
    /// </summary>
    /// <param name="tonightIsOver">Tonight's racing is done: races were run and none are left to run.</param>
    /// <param name="nextNight">The next night the club has announced, or null.</param>
    public static (DateTime Night, bool Held) GoalNight(DateTime askedLocal, bool tonightIsOver, DateTime? nextNight)
    {
        var night = TonightAtTheTrack.NightOf(askedLocal).Date;

        if (!tonightIsOver)
        {
            return (night, false);
        }

        return nextNight is { } next && next.Date > night ? (next.Date, false) : (night, true);
    }

    /// <summary>
    /// Whether a junior's own words - their crew notes and their about line
    /// (DriverProfile.AboutLine) - may be spoken right now: always off a
    /// streamed night, and on one only when the club has said so (plan 5.2,
    /// rule 4). An adult's always may. The crew's names and voices are not
    /// their words, and are not held back by this.
    /// </summary>
    /// <remarks>
    /// A JUNIOR'S WORDS NEED A PARENT'S CONSENT ON THE PROFILE as well (15 Nov
    /// 2026): what is said goes out on the PA and the stream, which children's
    /// privacy law treats as making a child's information public - a parent
    /// agreeing to it is recorded at the desk (CrewNotesParentConsent). The
    /// about line joined the notes the same day (plan 1.1): it is the same
    /// kind of thing, a child's own sentence read out in public.
    /// </remarks>
    public static bool OwnWordsMayBeSaid(DriverProfile? profile, bool streamOn, bool juniorNotesOnStream) =>
        profile is not { IsJunior: true }
        || (profile.CrewNotesParentConsent && (!streamOn || juniorNotesOnStream));

    /// <summary>
    /// A note in the driver's own words names another driver: the desk is
    /// told, so "Bill cheats" never reaches the PA without someone having
    /// read it (plan 2.4). Whole words only, and the driver's own name is
    /// not "another driver".
    /// </summary>
    /// <param name="note">The note as typed.</param>
    /// <param name="others">Every other driver's names - driver and announcer names, and their first names.</param>
    public static string? NamesAnotherDriver(string? note, IEnumerable<string> others)
    {
        var words = Words(note);

        if (words.Count == 0)
        {
            return null;
        }

        foreach (var other in others)
        {
            var name = Words(other);

            if (name.Count == 0 || (name.Count == 1 && name[0].Length < 3))
            {
                continue;
            }

            for (var i = 0; i + name.Count <= words.Count; i++)
            {
                if (name.Select((w, j) => string.Equals(w, words[i + j], StringComparison.OrdinalIgnoreCase)).All(x => x))
                {
                    return Clean(other);
                }
            }
        }

        return null;
    }

    private static List<string> Words(string? text) =>
        Clean(text).Split(new[] { ' ', ',', '.', '!', '?', ';', ':', '"', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('\'', '’'))
            .Where(w => w.Length > 0)
            .ToList();
}
