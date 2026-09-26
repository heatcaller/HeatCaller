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

    public bool IsRecorded => !string.IsNullOrEmpty(LinkedSessionId);

    /// <summary>A, B, C... derived from the tier. Falls back to the stored label for untiered mains.</summary>
    public string TierLetter => MainTier >= 0 && MainTier < 26
        ? ((char)('A' + MainTier)).ToString()
        : string.Empty;
}
