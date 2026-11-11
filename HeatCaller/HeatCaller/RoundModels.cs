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

    public bool IsRecorded => !string.IsNullOrEmpty(LinkedSessionId);

    /// <summary>A, B, C... derived from the tier. Falls back to the stored label for untiered mains.</summary>
    public string TierLetter => MainTier >= 0 && MainTier < 26
        ? ((char)('A' + MainTier)).ToString()
        : string.Empty;
}
