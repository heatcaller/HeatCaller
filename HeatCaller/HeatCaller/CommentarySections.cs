namespace HeatCaller;

/// <summary>The six section switches of AI Commentary's Simple view, in the order the night hears them.</summary>
internal enum CommentarySection
{
    BeforeTheRace,
    DuringTheRace,
    BetweenRaces,
    AfterTheFlag,
    RecapShow,
    OpeningAndClosing,
}

/// <summary>Who a piece needs from the Cast tab to be heard at all.</summary>
internal enum CastNeed
{
    None,
    TechnicalLead,
    TeamPrincipal,
    AchievementsReader,
}

/// <summary>
/// AI COMMENTARY'S SIMPLE VIEW (1 Nov 2026, Simple and Advanced mode, round
/// 3): six section switches over the settings the tab already has, each
/// named after one of its groups. Pure - the switches' state, and what
/// turning one on or off does to the settings - so it is tested without a
/// window; CommentaryControl draws them.
///
/// What a switch does:
///   - OFF turns every piece in its section off, and remembers which were
///     on (AppSettings.CommentarySectionsRemembered) - the way Lap Times
///     Only puts the sound back.
///   - ON puts back what it remembered. With nothing remembered, it turns
///     the section's pieces on as HeatCaller ships them.
///   - A piece that needs a voice from a Cast card Simple hides (the
///     Technical Lead, the Team Principal, the Achievements reader) is only
///     turned on when that voice is set - so a switch never turns on
///     something that would stay silent.
///   - A section set piece by piece in Advanced reads as ON with "custom"
///     beside it; off and on again puts that custom set back.
/// </summary>
internal static class CommentarySections
{
    /// <summary>One piece a switch turns on or off: an AppSettings tick, or a segment (on while it is not in CommentarySegmentsOff).</summary>
    internal sealed record Piece(string Key, CommentarySection Section, CastNeed Needs, Func<AppSettings, bool> Get, Action<AppSettings, bool> Set);

    /// <summary>Every piece, by section, as the plan maps them.</summary>
    internal static readonly IReadOnlyList<Piece> All = Build();

    /// <summary>The words on each switch.</summary>
    internal static string Title(CommentarySection section) => section switch
    {
        CommentarySection.BeforeTheRace => "Before the race",
        CommentarySection.DuringTheRace => "During the race",
        CommentarySection.BetweenRaces => "Between races",
        CommentarySection.AfterTheFlag => "After the flag",
        CommentarySection.RecapShow => "Recap show",
        _ => "Opening and closing the night",
    };

    /// <summary>The line under each switch: what it covers, in the tab's own names.</summary>
    internal static string What(CommentarySection section) => section switch
    {
        CommentarySection.BeforeTheRace => "Race intros, driver introductions, what qualifying decided, and the throw to pit lane.",
        CommentarySection.DuringTheRace => "Battles, record laps, what's still to decide, coming up next, and the championship.",
        CommentarySection.BetweenRaces => "Form guide, qualifying report, grid history, club notes, and the room's verdict.",
        CommentarySection.AfterTheFlag => "The unofficial result, achievements, the Technical Lead, teasers, and sponsors as a break.",
        CommentarySection.RecapShow => "Race recaps with the fastest lap, the TV-style open and sign-off, and the booth interview.",
        _ => "The cold open, this day at the club, halfway standings, and the sign-off numbers.",
    };

    /// <summary>The pieces of one section.</summary>
    internal static IEnumerable<Piece> In(CommentarySection section) => All.Where(piece => piece.Section == section);

    /// <summary>On while any piece in the section is on.</summary>
    internal static bool IsOn(AppSettings settings, CommentarySection section) =>
        In(section).Any(piece => piece.Get(settings));

    /// <summary>
    /// On, but not as HeatCaller ships it - set piece by piece in Advanced
    /// mode, or put back from a custom set. Shown as "custom" beside the
    /// switch.
    /// </summary>
    internal static bool IsCustom(AppSettings settings, CommentarySection section, Func<CastNeed, bool> castSet) =>
        IsOn(settings, section)
        && In(section).Any(piece => piece.Get(settings) != Shipped(piece, castSet));

    /// <summary>After the flag is on but needs recaps, which Recap show has off - the rule the tab already says in words.</summary>
    internal static bool AfterTheFlagNeedsRecaps(AppSettings settings) =>
        IsOn(settings, CommentarySection.AfterTheFlag) && !settings.CommentaryRaceSummaries;

    /// <summary>Turns a section on or off - see the class summary. Does not save.</summary>
    internal static void Set(AppSettings settings, CommentarySection section, bool on, Func<CastNeed, bool> castSet)
    {
        var key = section.ToString();
        var pieces = In(section).ToList();

        if (!on)
        {
            var wereOn = pieces.Where(piece => piece.Get(settings)).Select(piece => piece.Key).ToList();

            // An off switch turned off again must not forget what it had.
            if (wereOn.Count > 0)
            {
                settings.CommentarySectionsRemembered[key] = wereOn;
            }

            foreach (var piece in pieces)
            {
                piece.Set(settings, false);
            }

            return;
        }

        if (settings.CommentarySectionsRemembered.TryGetValue(key, out var remembered) && remembered.Count > 0)
        {
            foreach (var piece in pieces)
            {
                piece.Set(settings, remembered.Contains(piece.Key));
            }
        }
        else
        {
            foreach (var piece in pieces)
            {
                // One that needs a voice nobody has set stays as it is: on
                // without a voice it would stay silent.
                if (piece.Needs != CastNeed.None && !castSet(piece.Needs))
                {
                    continue;
                }

                piece.Set(settings, Shipped(piece, castSet));
            }
        }

        settings.CommentarySectionsRemembered.Remove(key);
    }

    /// <summary>
    /// How HeatCaller ships a piece: its default in a new settings file. A
    /// piece that needs a Cast voice ships on once that voice is set - off
    /// by default only because a new club has nobody cast in it.
    /// </summary>
    private static bool Shipped(Piece piece, Func<CastNeed, bool> castSet) =>
        piece.Needs != CastNeed.None ? castSet(piece.Needs) : piece.Get(Defaults);

    private static readonly AppSettings Defaults = new();

    private static IReadOnlyList<Piece> Build()
    {
        var pieces = new List<Piece>();

        void Tick(CommentarySection section, string key, Func<AppSettings, bool> get, Action<AppSettings, bool> set, CastNeed needs = CastNeed.None) =>
            pieces.Add(new Piece(key, section, needs, get, set));

        void Segments(CommentarySection section, params SegmentKind[] kinds)
        {
            foreach (var kind in kinds)
            {
                pieces.Add(new Piece(kind.ToString(), section, CastNeed.None, s => SegmentOn(s, kind), (s, on) => SetSegment(s, kind, on)));
            }
        }

        Tick(CommentarySection.BeforeTheRace, nameof(AppSettings.CommentaryRaceIntros), s => s.CommentaryRaceIntros, (s, v) => s.CommentaryRaceIntros = v);
        Tick(CommentarySection.BeforeTheRace, nameof(AppSettings.CommentaryDriverIntros), s => s.CommentaryDriverIntros, (s, v) => s.CommentaryDriverIntros = v);
        Tick(CommentarySection.BeforeTheRace, nameof(AppSettings.CommentaryQualifyingRecap), s => s.CommentaryQualifyingRecap, (s, v) => s.CommentaryQualifyingRecap = v);
        Tick(CommentarySection.BeforeTheRace, nameof(AppSettings.PrincipalBeforeRace), s => s.PrincipalBeforeRace, (s, v) => s.PrincipalBeforeRace = v, CastNeed.TeamPrincipal);

        Segments(CommentarySection.DuringTheRace,
            SegmentKind.BattleWatch, SegmentKind.PaceNote, SegmentKind.RecordWatch, SegmentKind.Horizon, SegmentKind.PitLaneLive, SegmentKind.ComingUpNext, SegmentKind.ChampionshipCheck);

        Segments(CommentarySection.BetweenRaces,
            SegmentKind.FormGuide, SegmentKind.QualifyingReport, SegmentKind.GridHistory, SegmentKind.ClubAside, SegmentKind.RoomVerdict);

        Tick(CommentarySection.AfterTheFlag, nameof(AppSettings.FlagResultRead), s => s.FlagResultRead, (s, v) => s.FlagResultRead = v);
        Tick(CommentarySection.AfterTheFlag, nameof(AppSettings.RecapBadgeHonours), s => s.RecapBadgeHonours, (s, v) => s.RecapBadgeHonours = v, CastNeed.AchievementsReader);
        Tick(CommentarySection.AfterTheFlag, nameof(AppSettings.RecapMechanicInterview), s => s.RecapMechanicInterview, (s, v) => s.RecapMechanicInterview = v, CastNeed.TechnicalLead);
        Tick(CommentarySection.AfterTheFlag, nameof(AppSettings.SummaryTeaserEnabled), s => s.SummaryTeaserEnabled, (s, v) => s.SummaryTeaserEnabled = v);
        Tick(CommentarySection.AfterTheFlag, nameof(AppSettings.RecapAdBreak), s => s.RecapAdBreak, (s, v) => s.RecapAdBreak = v);

        Tick(CommentarySection.RecapShow, nameof(AppSettings.CommentaryRaceSummaries), s => s.CommentaryRaceSummaries, (s, v) => s.CommentaryRaceSummaries = v);
        Tick(CommentarySection.RecapShow, nameof(AppSettings.CommentarySummaryMentionFastestLap), s => s.CommentarySummaryMentionFastestLap, (s, v) => s.CommentarySummaryMentionFastestLap = v);
        Tick(CommentarySection.RecapShow, nameof(AppSettings.RecapBroadcastOpening), s => s.RecapBroadcastOpening, (s, v) => s.RecapBroadcastOpening = v);
        Tick(CommentarySection.RecapShow, nameof(AppSettings.PrincipalAfterRace), s => s.PrincipalAfterRace, (s, v) => s.PrincipalAfterRace = v, CastNeed.TeamPrincipal);
        Tick(CommentarySection.RecapShow, nameof(AppSettings.RecapBroadcastClosing), s => s.RecapBroadcastClosing, (s, v) => s.RecapBroadcastClosing = v);

        Segments(CommentarySection.OpeningAndClosing,
            SegmentKind.ColdOpen, SegmentKind.ThisDayInHistory, SegmentKind.SignOffNumbers, SegmentKind.MidMeetingStandings);

        return pieces;
    }

    private static bool SegmentOn(AppSettings settings, SegmentKind kind) =>
        !CommentaryBudget.ParseSwitches(settings.CommentarySegmentsOff).Contains(kind);

    private static void SetSegment(AppSettings settings, SegmentKind kind, bool on)
    {
        var off = new HashSet<SegmentKind>(CommentaryBudget.ParseSwitches(settings.CommentarySegmentsOff));

        if (on ? off.Remove(kind) : off.Add(kind))
        {
            settings.CommentarySegmentsOff = CommentaryBudget.SaveSwitches(off);
        }
    }
}
