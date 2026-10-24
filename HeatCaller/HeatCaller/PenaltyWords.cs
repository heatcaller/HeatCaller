using System.Globalization;

namespace HeatCaller;

/// <summary>
/// Why a driver's result carries a penalty, in a line per reason, for the
/// driver themselves: "+5 s - jump start", "Warning 1 - Blocking",
/// "time penalty 0s -> 5s - Cut the chicane".
/// </summary>
/// <remarks>
/// 22 Oct 2026 (driver experience, item 1). A driver who dropped a place
/// used to be able to see only that their time was longer. Every number a
/// penalty changes was already kept on the result - the warnings, the
/// in-race penalty, the corrections log - so this only says them.
///
/// THE REASONS ARE PRIVATE. They are free text a race director typed about
/// a named person, who may be a child. These lines go only to the driver's
/// own night document (badge-only) and to the Wi-Fi follow view in the
/// room; the public results document carries the numbers alone.
/// </remarks>
internal static class PenaltyWords
{
    /// <summary>The longest reason shown. A reason is a note, not an essay, and the phone has one line for it.</summary>
    public const int ReasonMaxChars = 80;

    /// <summary>What an empty reason reads as.</summary>
    public const string NoReason = "no reason given";

    /// <summary>
    /// Every line for one driver's result in <paramref name="session"/>,
    /// oldest first: the warnings in race order, then whatever race control
    /// added that the warnings don't explain, then the corrections made after
    /// the race. Empty for a clean result.
    /// </summary>
    public static IReadOnlyList<string> For(SessionResultEntry entry, SessionRecord session)
    {
        var lines = new List<string>();
        var warnings = (entry.Warnings ?? new List<RaceWarning>())
            .Where(w => w is not null)
            .OrderBy(w => w.Number)
            .ThenBy(w => w.AtUtc)
            .ToList();

        // 1. The warnings, each as the ladder decided it.
        foreach (var warning in warnings)
        {
            var reason = Reason(warning.Reason);

            lines.Add(warning.Action switch
            {
                WarningAction.TimePenalty => $"{Seconds(warning.PenaltyMs)} - {reason} (warning {warning.Number})",
                WarningAction.BlackFlag => $"Black flag - {reason} (warning {warning.Number})",
                _ => $"Warning {warning.Number} - {reason}",
            });
        }

        // 2. The rest of the in-race penalty: what race control added that
        // no warning accounts for - a jump start, or the +5s buttons.
        var fromWarnings = warnings.Sum(w => Math.Max(0, w.PenaltyMs));
        var rest = Math.Max(0, entry.RacePenaltyMs) - fromWarnings;

        if (rest > 0)
        {
            lines.Add(entry.JumpStarted
                ? $"{Seconds(rest)} - jump start"
                : $"{Seconds(rest)} - from race control");
        }
        else if (entry.JumpStarted && !warnings.Any(w => IsJumpStartReason(w.Reason)))
        {
            // Flagged, then forgiven (Clear Penalty) or on a class that
            // charges nothing: still worth knowing it was seen. Not said a
            // second time when it was given as a warning.
            lines.Add("Jump start (no time added)");
        }

        // 3. After the race: the corrections log says what changed and why.
        var edits = (session?.Edits ?? new List<ResultEdit>())
            .Where(e => e is not null && string.Equals(e.Transponder?.Trim(), entry.Transponder?.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.AtUtc)
            .ToList();

        if (edits.Count > 0)
        {
            foreach (var edit in edits)
            {
                var change = OneLine(edit.Change);

                if (change.Length > 0)
                {
                    lines.Add($"{change} - {Reason(edit.Reason)}");
                }
            }
        }
        else
        {
            // A correction without its log line - a file edited by hand, or
            // a log lost to an undo. The totals are still on the result.
            if (entry.TimePenaltyMs > 0)
            {
                lines.Add($"{Seconds(entry.TimePenaltyMs)} added after the race");
            }

            if (entry.LapPenalty > 0)
            {
                lines.Add($"-{Plural.Of(entry.LapPenalty, "lap")} after the race");
            }

            if (string.Equals(entry.Outcome, ResultEditor.Dq, StringComparison.OrdinalIgnoreCase))
            {
                lines.Add("Disqualified");
            }
            else if (string.Equals(entry.Outcome, ResultEditor.Dnf, StringComparison.OrdinalIgnoreCase))
            {
                lines.Add("Did not finish");
            }
        }

        return lines;
    }

    /// <summary>"+5 s", "+2.5 s" - one decimal at most, the same in every culture.</summary>
    internal static string Seconds(long ms) =>
        "+" + (Math.Max(0, ms) / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " s";

    /// <summary>A reason on one line, at most <see cref="ReasonMaxChars"/> characters, or <see cref="NoReason"/>.</summary>
    internal static string Reason(string? reason)
    {
        var text = OneLine(reason);

        if (text.Length == 0)
        {
            return NoReason;
        }

        return text.Length <= ReasonMaxChars
            ? text
            : text[..(ReasonMaxChars - 3)].TrimEnd() + "...";
    }

    private static bool IsJumpStartReason(string? reason) =>
        OneLine(reason).Contains("jump start", StringComparison.OrdinalIgnoreCase);

    /// <summary>Line breaks and runs of spaces as single spaces, trimmed.</summary>
    private static string OneLine(string? text) =>
        string.Join(" ", (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
