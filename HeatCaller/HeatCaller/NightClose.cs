namespace HeatCaller;

/// <summary>
/// What is still to do once the racing is over (11 Nov 2026, race-night
/// build plan, Phase 2, "close the night").
///
/// WHY: the end of the night is when things get forgotten. Everybody is
/// packing up, the director is tired, and the last main's result is the
/// only thing anybody is thinking about. The rental transponder that went
/// home in someone's pocket, the results that never reached the website,
/// the night emails that were never sent and the backup that was never
/// taken all come from this hour. The Next Steps card already says "The
/// program is complete." This adds the list of what is left before lights
/// out, from things the app already knows.
///
/// Pure: the facts are gathered by whoever can see the stores (MainForm)
/// and handed in.
/// </summary>
public static class NightClose
{
    /// <summary>What the app knows at the end of the night. Null parts are things it can't tell.</summary>
    /// <param name="RentalsOut">Who still has a rental transponder (one name per unit out).</param>
    /// <param name="NightEmailsToSend">Night emails still to go out; null when night emails are off or can't be planned.</param>
    /// <param name="ResultsNotOnline">Tonight's results the club website hasn't got yet; null when publishing is off.</param>
    /// <param name="LastBackupUtc">When the last backup was taken; null when there has never been one.</param>
    /// <param name="DayOpen">Whether the race day is still open.</param>
    /// <param name="Owing">Drivers who raced and still owe their entry (11 Nov 2026, Phase 4); null when the day has no fee.</param>
    public sealed record Facts(
        IReadOnlyList<string> RentalsOut,
        int? NightEmailsToSend,
        int? ResultsNotOnline,
        DateTime? LastBackupUtc,
        bool DayOpen,
        IReadOnlyList<string>? Owing = null);

    /// <summary>
    /// The list, as the Next Steps card says it, starting with a space:
    /// " Before lights out: 2 rentals still out (Sam, Jo) · 14 night emails
    /// to send · back up tonight's results · Close Day." With nothing left:
    /// " All wrapped up - lights out."
    /// </summary>
    /// <param name="facts">What the app knows.</param>
    /// <param name="lastRaceEndedUtc">When tonight's last race finished; a backup from before it is missing results.</param>
    public static string Note(Facts facts, DateTime? lastRaceEndedUtc)
    {
        var left = Left(facts, lastRaceEndedUtc);

        return left.Count == 0
            ? " All wrapped up - lights out."
            : $" Before lights out: {string.Join(" · ", left)}.";
    }

    /// <summary>Each thing still to do, in the order to do it.</summary>
    public static List<string> Left(Facts facts, DateTime? lastRaceEndedUtc)
    {
        var left = new List<string>();

        if (facts.RentalsOut.Count > 0)
        {
            var names = facts.RentalsOut.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var who = names.Count == 0 ? string.Empty : $" ({string.Join(", ", names.Take(3))}{(names.Count > 3 ? ", ..." : string.Empty)})";
            left.Add($"{Plural.Of(facts.RentalsOut.Count, "rental")} still out{who}");
        }

        if (facts.Owing is { Count: > 0 } owing)
        {
            var names = string.Join(", ", owing.Take(3)) + (owing.Count > 3 ? ", ..." : string.Empty);
            left.Add($"{Plural.Of(owing.Count, "driver")} still {(owing.Count == 1 ? "owes" : "owe")} their entry ({names})");
        }

        if (facts.ResultsNotOnline is > 0 and var owed)
        {
            left.Add($"{Plural.Of(owed, "result")} not on the club website yet");
        }

        if (facts.NightEmailsToSend is > 0 and var emails)
        {
            left.Add($"{Plural.Of(emails, "night email")} to send");
        }

        // A backup taken before the last race finished hasn't got it.
        var backedUp = facts.LastBackupUtc is { } backup
                       && (lastRaceEndedUtc is not { } ended || backup >= ended);

        if (!backedUp)
        {
            left.Add("back up tonight's results");
        }

        if (facts.DayOpen)
        {
            left.Add("Close Day");
        }

        return left;
    }
}
