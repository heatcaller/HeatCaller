namespace HeatCaller;

public enum RoundKind
{
    Qualifier,
    Main,
    Practice,
}

/// <summary>
/// One scheduled slot in a race program - a heat, a main, or a practice
/// session - for a specific class, with an assigned roster of transponders.
/// Next Level Timing still does the actual timing; this just organizes the
/// event and links each slot to the real SessionRecord once it's run (see
/// LinkedSessionId, set by MainForm when a session tagged with this round
/// finishes).
/// </summary>
public sealed class RoundDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Order { get; set; }
    public string ClassName { get; set; } = string.Empty;
    public string RoundLabel { get; set; } = string.Empty; // "Heat 1", "A-Main", etc.
    public RoundKind Kind { get; set; } = RoundKind.Qualifier;
    public string TrackLayoutName { get; set; } = string.Empty;
    public List<string> AssignedTransponders { get; set; } = new();
    public string? LinkedSessionId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Which tier this main is: 0 = A-main, 1 = B-main, 2 = C-main and so
    /// on. -1 for anything that isn't a main, and for mains created before
    /// tiers existed - their labels were only cosmetic, so there's nothing
    /// to infer a tier from.
    ///
    /// Tiers are what make bump-ups possible: the app needs to know that
    /// the B-main feeds the A-main, which a label alone can't tell it.
    /// </summary>
    public int MainTier { get; set; } = -1;

    /// <summary>
    /// How many finishers from the tier BELOW this one bump up into it.
    /// Zero means no bump-up, which is correct for the lowest main and for
    /// clubs that don't run them.
    /// </summary>
    public int BumpUpCount { get; set; }

    /// <summary>True once bump-ups have been applied, so it can't be done twice.</summary>
    public bool BumpUpsApplied { get; set; }

    /// <summary>
    /// A format just for this round, or null to use the class's (which may
    /// itself fall through to the app default). The top of the precedence
    /// chain: an A-main that runs longer than the heats is a round-level
    /// fact, not a class-level one.
    /// </summary>
    public RaceFormat? FormatOverride { get; set; }

    /// <summary>
    /// Marshal swaps for this round (11 Nov 2026, race-night build plan,
    /// Phase 4): the driver whose post it was, to the driver covering it.
    /// Null or empty - the usual case - is the heat before marshalling as
    /// always. See MarshalAssigner.Assign.
    /// </summary>
    public Dictionary<string, string>? MarshalCovers { get; set; }

    /// <summary>
    /// Marshals who didn't turn up to their post for this round (12 Nov 2026,
    /// setup plan D4), as "Name (Post)", logged from Race Control's
    /// Someone's missing... for the club's marshal rule. Null for none.
    /// </summary>
    public List<string>? MarshalNoShows { get; set; }

    /// <summary>
    /// Which leg of a multi-leg A-main this is (11 Nov 2026, race-night build
    /// plan, Phase 5.3): 1, 2 or 3, or 0 for every other round - including an
    /// A-main run as one race, the usual case. The legs are scored together
    /// by AMainLegs; this is how that, the program and the results find them
    /// without reading the label.
    /// </summary>
    public int MainLeg { get; set; }

    /// <summary>How many legs this main's A-main has, beside <see cref="MainLeg"/>. 0 when not a leg.</summary>
    public int MainLegCount { get; set; }

    /// <summary>
    /// The OTHER classes sharing this heat with <see cref="ClassName"/>
    /// (11 Nov 2026, race-night build plan, Phase 4.1: mixed-class heats) -
    /// "Mod" on a Stock heat that 3 Stock and 4 Mod run together.
    ///
    /// Null or empty, which is every round saved before this and every round
    /// built without the option, is a one-class round and behaves exactly as
    /// it always did. ClassName stays the round's MAIN class, so everything
    /// that only knows one class still has a right answer; MixedHeats is
    /// where a shared round is told apart and scored per class. Optional and
    /// migration-free on purpose: an older build reading a newer file simply
    /// ignores it and sees a Stock heat.
    /// </summary>
    public List<string>? SharedClasses { get; set; }

    /// <summary>
    /// Which class each driver in a shared heat runs in, by transponder
    /// (11 Nov 2026, Phase 4.1). Written when the heat is built or put
    /// together, because that is when it is certain. A driver with no entry
    /// is placed by their profile - see MixedHeats.ClassOfDriver. Null for a
    /// one-class round.
    /// </summary>
    public Dictionary<string, string>? SharedClassOf { get; set; }

    /// <summary>
    /// The start drivers were given for this race (12 Nov 2026, setup plan
    /// step 8): the "about 20:42" the phones and the heat sheets showed at
    /// the moment the heats were posted - or, on a night that doesn't post,
    /// when the first race went out. Null for a race nobody was given a time
    /// for (built later, or a night with no plan).
    ///
    /// WHY ON THE ROUND: the promise is about this heat, and has to survive
    /// a restart of the laptop mid-night - the plan itself moves every
    /// minute, so it can't be worked out again later. Auto start never
    /// starts the race more than StartPromise.Early before it (a club
    /// setting, on by default), because drivers are in the pits working on
    /// cars and were told a time. See StartPromise.
    /// </summary>
    public DateTime? PromisedStartUtc { get; set; }

    /// <summary>
    /// When this race was first called up to the grid (12 Nov 2026, setup
    /// plan step 8, build plan 0.1): the call-up line going out, by itself
    /// or from Call Up. Null when it never was. With <see cref="SavedAtUtc"/>
    /// it cuts the gap before this race into its parts for NightPace.
    ///
    /// WHY ON THE ROUND AND NOT THE SESSION: a race is called up before there
    /// is any session to write it on, and the round is what both the
    /// call-up and the result already point at.
    /// </summary>
    public DateTime? CalledAtUtc { get; set; }

    /// <summary>
    /// When this race's result was saved - the round linked to its session
    /// (RoundDefinitionStore.MarkRecorded, 12 Nov 2026, setup plan step 8).
    /// The time from the checkered flag to here is the filing part of the
    /// gap that follows the race.
    /// </summary>
    public DateTime? SavedAtUtc { get; set; }

    /// <summary>
    /// What the desk did in the gap before this race (12 Nov 2026, setup plan
    /// step 8, build plan 0.2): Hold and how long, +1 min, Start now, a
    /// driver's "Need 5 minutes", a wait for the promised time. NightPace
    /// names them as the gap's causes, so the report can say "held 3 min at
    /// the desk" instead of leaving the gap unexplained. Null for none.
    /// </summary>
    public List<GapNote>? GapNotes { get; set; }

    public bool IsRecorded => !string.IsNullOrEmpty(LinkedSessionId);

    /// <summary>A, B, C... derived from the tier. Falls back to the stored label for untiered mains.</summary>
    public string TierLetter => MainTier >= 0 && MainTier < 26
        ? ((char)('A' + MainTier)).ToString()
        : string.Empty;
}

/// <summary>What the desk did in a gap between races (12 Nov 2026, setup plan step 8). See RoundDefinition.GapNotes.</summary>
public enum GapNoteKind
{
    /// <summary>Auto start held; <see cref="GapNote.Seconds"/> is how long.</summary>
    Held,

    /// <summary>+1 min (Call Up's push back); <see cref="GapNote.Seconds"/> is 60.</summary>
    PushedBack,

    /// <summary>A driver's "Need 5 minutes", said yes to at the desk.</summary>
    DriverAskedForTime,

    /// <summary>Start now pressed - the race went before the gap was up.</summary>
    StartedNow,

    /// <summary>Auto start waited for the start drivers were given; <see cref="GapNote.Seconds"/> is how long.</summary>
    WaitedForPromise,
}

/// <summary>One thing the desk did in a gap, and when. See RoundDefinition.GapNotes.</summary>
public sealed class GapNote
{
    public GapNoteKind Kind { get; set; }

    public DateTime AtUtc { get; set; }

    /// <summary>How long it put the race back, in seconds; 0 when it didn't (Start now).</summary>
    public int Seconds { get; set; }
}
