namespace HeatCaller;

/// <summary>What a warning costs the driver, decided by where it falls on the class's ladder.</summary>
public enum WarningAction
{
    /// <summary>Logged, announced and shown - nothing more.</summary>
    Warning,

    /// <summary>Seconds added to the driver's time, exactly like the +5s / +10s buttons.</summary>
    TimePenalty,

    /// <summary>Called in: announced as a black flag, and the saved result is marked DQ.</summary>
    BlackFlag,
}

/// <summary>One rung of a warning ladder: what the Nth warning in a race does.</summary>
public sealed class WarningStep
{
    public WarningAction Action { get; set; }

    /// <summary>Only used by TimePenalty.</summary>
    public int PenaltySeconds { get; set; }

    public WarningStep()
    {
    }

    public WarningStep(WarningAction action, int penaltySeconds = 0)
    {
        Action = action;
        PenaltySeconds = penaltySeconds;
    }

    /// <summary>For settings and menus - "Warning only", "+5s penalty", "Black flag (DQ)".</summary>
    public string Describe() => Action switch
    {
        WarningAction.TimePenalty => $"+{PenaltySeconds}s penalty",
        WarningAction.BlackFlag => "Black flag (DQ)",
        _ => "Warning only",
    };
}

/// <summary>
/// One warning given to a driver during a race. Plain settable properties
/// so it round-trips through the race journal and the saved history as-is.
/// </summary>
public sealed class RaceWarning
{
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>How far into the race it was given, in ms. Zero before the green.</summary>
    public long RaceTimeMs { get; set; }

    /// <summary>1 for the driver's first warning of the race, 2 for the second...</summary>
    public int Number { get; set; }

    public string Reason { get; set; } = string.Empty;

    public WarningAction Action { get; set; }

    /// <summary>The time this warning added, if its step was a penalty. Kept so removing it gives exactly that back.</summary>
    public long PenaltyMs { get; set; }

    /// <summary>
    /// A copy of this warning. WHY (23 Oct 2026, timing layer, P4): with
    /// timing on its own thread a screen is handed a copy rather than the
    /// race's own warning, whose PenaltyMs a Clear Penalty or an undo can
    /// change on the timing thread (see RaceTiming). Every field is a value
    /// or a string, so a member-wise copy is a whole copy.
    /// </summary>
    public RaceWarning Clone() => (RaceWarning)MemberwiseClone();

    /// <summary>"Warning 2 - Blocking (+5s)" - for tooltips, logs and history.</summary>
    public string Summary()
    {
        var reason = string.IsNullOrWhiteSpace(Reason) ? "no reason given" : Reason;
        var cost = Action switch
        {
            WarningAction.TimePenalty => $" (+{PenaltyMs / 1000.0:0.#}s)",
            WarningAction.BlackFlag => " (BLACK FLAG)",
            _ => string.Empty,
        };

        var at = RaceTimeMs > 0 ? $" at {RaceTimeMs / 60000}:{RaceTimeMs / 1000 % 60:00}" : string.Empty;
        return $"Warning {Number} - {reason}{cost}{at}";
    }
}

/// <summary>The preset reasons and the default ladder, shared by every place a warning is given or shown.</summary>
public static class WarningRules
{
    /// <summary>One click each from the menu. Anything else goes through "Other reason...".</summary>
    public static readonly string[] PresetReasons =
    {
        "Rough driving",
        "Track cutting",
        "Blocking",
        "Unsportsmanlike conduct",
        "Ignoring the marshal",
        "Jump start",
        "Not marshaling",
    };

    /// <summary>1st: a warning. 2nd: +5s. 3rd: +10s. 4th: black flag.</summary>
    public static List<WarningStep> DefaultLadder() => new()
    {
        new WarningStep(WarningAction.Warning),
        new WarningStep(WarningAction.TimePenalty, 5),
        new WarningStep(WarningAction.TimePenalty, 10),
        new WarningStep(WarningAction.BlackFlag),
    };

    /// <summary>
    /// What the Nth warning (1-based) does. Past the end of the ladder the
    /// last rung repeats - a fifth warning on a ladder ending +10s is
    /// another +10s, not a free pass.
    /// </summary>
    public static WarningStep StepFor(IReadOnlyList<WarningStep>? ladder, int number)
    {
        var steps = ladder is { Count: > 0 } ? ladder : DefaultLadder();
        return steps[Math.Clamp(number, 1, steps.Count) - 1];
    }

    /// <summary>The mark after a driver's name on every board - empty for a clean driver.</summary>
    public static string Badge(IReadOnlyCollection<RaceWarning> warnings)
    {
        if (warnings.Count == 0)
        {
            return string.Empty;
        }

        return warnings.Any(w => w.Action == WarningAction.BlackFlag)
            ? "  ⚑ BLACK FLAG"
            : $"  ⚠{warnings.Count}";
    }

    /// <summary>"1st", "2nd", "3rd", "4th"...</summary>
    public static string Ordinal(int n) =>
        ((n % 100) is 11 or 12 or 13) ? $"{n}th"
        : (n % 10) switch { 1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th" };
}
