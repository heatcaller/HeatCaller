namespace HeatCaller;

/// <summary>
/// ElevenLabs' 19 Default voices, which it retires on 31 December 2026, and
/// what each of the cast moves to (24 Nov 2026, Asher: plans/elevenlabs-voice-replacement-plan.md,
/// sections 2 and 4).
/// </summary>
/// <remarks>
/// WHY (24 Nov 2026, Asher: "All our Default voices will expire on December
/// 31, 2026"): a club whose seats were on Charlie, Alice or Bill would have
/// had every line fail from 1 January, each one making a failing request
/// before Kokoro stood in, and its saved lines playing on in the old voice
/// beside the new. This is the one place that knows which voices go and
/// what replaces them, so the Cast tab's notice, the switch on 1 January and
/// the crew voices all give the same answer.
///
/// Two replacements, on purpose:
/// - a speaking SEAT (the announcer, the co-host, the Technical Lead, the Team
///   Principal, the achievements reader and the recap host) moves to the
///   voice HeatCaller picked for that seat, whatever it was on - so the cast
///   never ends up sharing a voice.
/// - anything else (a driver's own crew voice) moves to ElevenLabs' own named
///   replacement for the voice it was on, which is the nearest in sound.
///
/// Callum has no usable named replacement: the id ElevenLabs gives for
/// "Callum - Husky Trickster" (fs2OqxduwXgp9foh2xjK) answered 404
/// voice_not_found on 24 Nov 2026. A seat on Callum moves to its seat's
/// voice; a crew pick on Callum is cleared, so that driver hears the club's
/// own voice for the role (Finley, the obvious stand-in, is usually the
/// announcer's own voice and so never a crew member's - DriverCrew.Reserved).
///
/// Matched by voice id only, never by name: a club's own cloned voice may
/// well be called Charlie.
/// </remarks>
internal static class RetiredVoices
{
    /// <summary>The last day ElevenLabs' Default voices speak. From the next day (local date) they are gone.</summary>
    internal static readonly DateOnly LastDay = new(2026, 12, 31);

    /// <summary>The day as the Cast tab says it.</summary>
    internal const string LastDayWords = "31 December";

    /// <summary>Whether the Default voices have stopped working on this (local) day.</summary>
    internal static bool AreGone(DateOnly today) => today > LastDay;

    /// <summary>An ElevenLabs voice by its id and its full name.</summary>
    internal sealed record Voice(string Id, string Name)
    {
        /// <summary>The first name alone ("Darian" for "Darian - Warm Grounded Storyteller"), for a short line.</summary>
        public string ShortName => Name.Split(" - ", 2)[0].Trim();
    }

    /// <summary>A retired Default voice and ElevenLabs' own named replacement for it - null where that has none that works.</summary>
    internal sealed record Retired(string Id, string Name, Voice? Replacement);

    /// <summary>The speaking seats, each with HeatCaller's own pick (section 4 of the plan).</summary>
    internal enum Seat
    {
        Announcer,
        CoHost,
        TechnicalLead,
        TeamPrincipal,
        Achievements,
        RecapHost,
    }

    private static readonly Voice Finley = new("fnYMz3F5gMEDGMWcH1ex", "Finley - Articulate Anchor");
    private static readonly Voice Alicia = new("BFd5oBc2DDna33pSi4Gf", "Alicia - Polished Global Anchor");
    private static readonly Voice Wyatt = new("FrS6cKLB1wg4WYgPa9GW", "Wyatt - Seasoned Mentor");
    private static readonly Voice Darian = new("gOupLcAkjEnguROwi4oS", "Darian - Warm Grounded Storyteller");
    private static readonly Voice Elowen = new("dvbL7qkNGZY1IqPGZAjM", "Elowen - Upbeat Modern Narrator");
    private static readonly Voice Baxter = new("jSuBIjxMKhqIfb0wCK1F", "Baxter - Dry Calm Aussie");

    /// <summary>
    /// The 19 Default voices, with ElevenLabs' named replacement for each
    /// (its help centre, 24 Nov 2026). The Default ids are ElevenLabs
    /// premade voice ids, each checked against its name in the ElevenLabs
    /// workspace voice list on 24 Nov 2026.
    /// </summary>
    internal static readonly IReadOnlyList<Retired> All = new Retired[]
    {
        new("onwK4e9ZLuTAKqWW03F9", "Daniel", Finley),
        new("IKne3meq5aSn9XLyUdCD", "Charlie", Baxter),
        new("Xb7hH8MSUJpSbSDYk0k2", "Alice", Alicia),
        new("pqHfZKP75CvOlQylNhV4", "Bill", Wyatt),
        new("cgSgspJ2msm6clMCkdW9", "Jessica", new("g7LVvkPWALzPxOQbF6OE", "Jade - Upbeat and Natural")),
        new("bIHbv24MWmeRgasZH58o", "Will", new("7QN34D2r3hCNwbOYIeK0", "Warren - Effortless and Cool")),
        new("TX3LPaxmHKxFdv7VOQHJ", "Liam", new("ktkP7Nsj67dw2zcplQYt", "Lawrence - Bright and Informative")),
        new("SOYHLrjzK2X1ezoPC6cr", "Harry", new("10NkTYmU7tSz3Kkl3Lex", "Kaelen - Amateur Warrior")),
        new("JBFqnCBsd6RMkjVDRZzb", "George", new("6WwXjDDEMyNmFG95zycZ", "Eldrin - Crisp British Baritone")),
        new("CwhRBWXzGAHq8TQ4Fs17", "Roger", Darian),
        new("EXAVITQu4vr4xnSDxMaL", "Sarah", new("OZ0L6eISlOejga3XjDFt", "Talia - Warm Soft Guide")),
        new("FGY2WhTYpPnrIDTdsKH5", "Laura", new("WQP7cQUF5aAS6Axh5yaa", "Elara - Crisp Pro Narrator")),
        new("N2lVS1w4EtoT3dr4eOWO", "Callum", null),
        new("SAz9YHcvj6GT2YYXdXww", "River", Elowen),
        new("XrExE9yKIg1WjnnlVkGX", "Matilda", new("QtY3JBOUKEB5xzrRfOKc", "Maisie - Friendly Casual Neighbor")),
        new("cjVigY5qzO86Huf0OWal", "Eric", new("l7kNoIfnJKPg7779LI2t", "Eddie - Natural and Helpful")),
        new("iP95p4xoKVk53GoZ742B", "Chris", new("AaOhDHYJ1XLZk74lXhdE", "Caleb - Trusted Guide")),
        new("nPczCjzI2devNBz1zQrb", "Brian", new("8dEUmyPMdDdK91vboYih", "Sawyer - Midnight Storyteller")),
        new("pFZP5JQG7iQjIQuC4Bku", "Lily", new("22N9cF8z0o7y23njdyaY", "Florence - Atmospheric Storyteller")),
    };

    /// <summary>HeatCaller's voice for each seat, in the order the Cast tab shows them.</summary>
    internal static readonly IReadOnlyList<(Seat Seat, Voice Voice)> Suggested = new (Seat, Voice)[]
    {
        (Seat.Announcer, Finley),
        (Seat.CoHost, Alicia),
        (Seat.TechnicalLead, Wyatt),
        (Seat.TeamPrincipal, Darian),
        (Seat.Achievements, Elowen),
        (Seat.RecapHost, Baxter),
    };

    /// <summary>What a list says in front of a suggested voice, so nobody takes it for one saved on their own account.</summary>
    internal const string SuggestedMark = "Suggested by HeatCaller";

    /// <summary>A suggested voice as a list shows it: "Suggested by HeatCaller: Finley - Articulate Anchor".</summary>
    internal static string SuggestedLabel(Voice voice) => $"{SuggestedMark}: {voice.Name}";

    /// <summary>HeatCaller's voice for a seat.</summary>
    internal static Voice SeatDefault(Seat seat) => Suggested.First(s => s.Seat == seat).Voice;

    /// <summary>The retired voice with this id, or null for any other.</summary>
    internal static Retired? Find(string? voiceId)
    {
        var id = (voiceId ?? string.Empty).Trim();
        return id.Length == 0 ? null : All.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));
    }

    /// <summary>Whether this is one of the 19 Default voices ElevenLabs retires.</summary>
    internal static bool IsRetired(string? voiceId) => Find(voiceId) is not null;

    /// <summary>What a seat saved on this voice moves to: the seat's own HeatCaller voice, or null when the voice isn't retired.</summary>
    internal static Voice? ForSeat(string? voiceId, Seat seat) => IsRetired(voiceId) ? SeatDefault(seat) : null;

    /// <summary>
    /// What anything else saved on this voice - a driver's crew voice - moves
    /// to: ElevenLabs' named replacement. Null when the voice isn't retired,
    /// or (Callum) has no replacement that works - see <see cref="Moves"/>.
    /// </summary>
    internal static Voice? ForOther(string? voiceId) => Find(voiceId)?.Replacement;

    /// <summary>
    /// Every voice HeatCaller may move something to or suggest: the six seat
    /// voices and ElevenLabs' named replacements. Library voices, so the
    /// account's own list (GET v1/voices) never has them unless the club saved
    /// them - and a crew pick moved to one must not read as "no longer on the
    /// engine's list" (MainForm.Crew's still-there check).
    /// </summary>
    internal static IReadOnlySet<string> Offered { get; } =
        All.Where(r => r.Replacement is not null).Select(r => r.Replacement!.Id)
            .Concat(Suggested.Select(s => s.Voice.Id))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The one line a seat on a retired voice shows on the Cast tab:
    /// "ElevenLabs retires Charlie on 31 December. Switch to Darian?" -
    /// or null when the seat's voice isn't retired.
    /// </summary>
    internal static string? SeatNotice(string? voiceId, Seat seat, DateOnly today)
    {
        if (Find(voiceId) is not { } retired)
        {
            return null;
        }

        var to = SeatDefault(seat).ShortName;

        return AreGone(today)
            ? $"ElevenLabs retired {retired.Name} on {LastDayWords}. Switch to {to}?"
            : $"ElevenLabs retires {retired.Name} on {LastDayWords}. Switch to {to}?";
    }

    // ------------------------------------------------------------------
    //  The switch on 1 January
    // ------------------------------------------------------------------

    /// <summary>
    /// One seat on a retired voice, moved: which seat, from and to - said once
    /// in the console.
    /// </summary>
    internal sealed record Move(string Who, string From, Voice To);

    /// <summary>The console line for a move.</summary>
    internal static string Words(Move move) =>
        $"[voice] ElevenLabs retired {move.From} on {LastDayWords}, so the {move.Who} now speaks as {move.To.ShortName} "
        + $"({move.To.Name}). Pick another on Voice > Cast.";

    /// <summary>What each seat is called in a console line.</summary>
    internal static string SeatWords(Seat seat) => seat switch
    {
        Seat.Announcer => "announcer",
        Seat.CoHost => "co-host",
        Seat.TechnicalLead => "Technical Lead",
        Seat.TeamPrincipal => "Team Principal",
        Seat.Achievements => "achievements reader",
        _ => "recap host",
    };

    /// <summary>
    /// From the day after <see cref="LastDay"/>, every seat still saved on a
    /// retired voice moves to its HeatCaller voice - the announcer's
    /// (<paramref name="announcer"/>) and the five in <paramref name="settings"/>.
    /// Nothing happens before then. Changes them in place; the caller saves
    /// when anything moved and says each move once.
    /// </summary>
    /// <remarks>
    /// The saved ElevenLabs voice is moved whatever engine is in use: it is
    /// the voice the seat speaks in the day the club goes back to ElevenLabs.
    /// Saved lines are kept by voice id (VoiceLibrary.KeyFor), so a moved seat
    /// renders its lines afresh in the new voice and none of the old voice's
    /// saved lines plays beside them.
    /// </remarks>
    internal static IReadOnlyList<Move> SwitchSeats(AppSettings settings, VoiceProfile announcer, DateOnly today)
    {
        var moves = new List<Move>();

        if (!AreGone(today))
        {
            return moves;
        }

        if (ForSeat(announcer.VoiceId, Seat.Announcer) is { } forAnnouncer)
        {
            moves.Add(new Move(SeatWords(Seat.Announcer), Find(announcer.VoiceId)!.Name, forAnnouncer));
            announcer.VoiceId = forAnnouncer.Id;
            announcer.VoiceName = forAnnouncer.Name;
        }

        void Check(Seat seat, Func<string> id, Action<Voice> set)
        {
            if (ForSeat(id(), seat) is { } to)
            {
                moves.Add(new Move(SeatWords(seat), Find(id())!.Name, to));
                set(to);
            }
        }

        Check(Seat.CoHost, () => settings.CoHostElevenLabsVoiceId, v => (settings.CoHostElevenLabsVoiceId, settings.CoHostElevenLabsVoiceName) = (v.Id, v.Name));
        Check(Seat.TechnicalLead, () => settings.MechanicElevenLabsVoiceId, v => (settings.MechanicElevenLabsVoiceId, settings.MechanicElevenLabsVoiceName) = (v.Id, v.Name));
        Check(Seat.TeamPrincipal, () => settings.PrincipalElevenLabsVoiceId, v => (settings.PrincipalElevenLabsVoiceId, settings.PrincipalElevenLabsVoiceName) = (v.Id, v.Name));
        Check(Seat.Achievements, () => settings.HonoursElevenLabsVoiceId, v => (settings.HonoursElevenLabsVoiceId, settings.HonoursElevenLabsVoiceName) = (v.Id, v.Name));
        Check(Seat.RecapHost, () => settings.RecapHostElevenLabsVoiceId, v => (settings.RecapHostElevenLabsVoiceId, settings.RecapHostElevenLabsVoiceName) = (v.Id, v.Name));

        return moves;
    }

    /// <summary>
    /// A driver's crew voice from the day after <see cref="LastDay"/>: an
    /// ElevenLabs pick on a retired voice becomes ElevenLabs' named
    /// replacement, or is cleared (null) when there is none that works
    /// (Callum). Any other pick - another engine, a voice not retired, any
    /// pick before then - comes back as it was: a driver's pick stays theirs.
    /// </summary>
    /// <returns>The pick to keep, and whether it changed.</returns>
    internal static (CrewVoicePick? Pick, bool Changed) SwitchCrewPick(CrewVoicePick? pick, DateOnly today)
    {
        if (!AreGone(today)
            || pick is null
            || DriverCrew.EngineOf(pick) != VoiceEngine.ElevenLabs
            || Find(pick.Id) is not { } retired)
        {
            return (pick, false);
        }

        if (retired.Replacement is not { } to)
        {
            return (null, true);
        }

        var moved = pick.Copy();
        moved.Id = to.Id;
        moved.Name = to.Name;
        return (moved, true);
    }

    /// <summary>
    /// Every driver whose crew voice moved, as copies to save - see
    /// <see cref="SwitchCrewPick"/>. The profiles handed in are not changed.
    /// </summary>
    internal static IReadOnlyList<DriverProfile> SwitchCrew(IEnumerable<DriverProfile> drivers, DateOnly today)
    {
        var changed = new List<DriverProfile>();

        if (!AreGone(today))
        {
            return changed;
        }

        foreach (var driver in drivers)
        {
            var principal = SwitchCrewPick(driver.CrewPrincipalVoice, today);
            var tech = SwitchCrewPick(driver.CrewTechVoice, today);

            if (!principal.Changed && !tech.Changed)
            {
                continue;
            }

            var copy = ProfileStore.Snapshot(driver);
            copy.CrewPrincipalVoice = principal.Pick;
            copy.CrewTechVoice = tech.Pick;
            changed.Add(copy);
        }

        return changed;
    }
}
