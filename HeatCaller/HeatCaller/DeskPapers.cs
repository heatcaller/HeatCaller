namespace HeatCaller;

/// <summary>
/// A driver's papers at the desk (12 Nov 2026, setup plan D9): a signed
/// waiver (a parent or guardian's for a junior) and a membership in date.
///
/// WHY: many clubs, and every sanctioned meeting, need both before a driver
/// races: the waiver for the club's insurance, the membership for the
/// national body's. Today the desk keeps a clipboard beside the laptop and
/// checks it by eye. Now Who's Racing says "Waiver missing" or "Membership
/// expired 3 Oct" beside the driver as they check in, and Post the heats
/// lists anyone still not in order. Recorded by hand, like money: HeatCaller
/// never takes a signature or checks anybody's records. Both checks are off
/// until a club turns them on.
/// </summary>
public static class DeskPapers
{
    /// <summary>How long a signed waiver lasts when the club says nothing else.</summary>
    public const int DefaultWaiverMonths = 12;

    /// <summary>The desk's warning for a junior with no age band (15 Nov 2026).</summary>
    public const string NoAgeBand = "Set their age band - under 13s get the strictest rules";

    /// <summary>
    /// What's not in order for this driver tonight, in plain words, or an
    /// empty list when nothing is (or the club checks neither).
    /// </summary>
    /// <param name="waivers">The club asks for a signed waiver.</param>
    /// <param name="waiverMonths">How many months a waiver lasts; 0 for for ever.</param>
    /// <param name="memberships">The club checks memberships.</param>
    /// <param name="today">Today, as a local date.</param>
    public static List<string> Problems(DriverProfile profile, bool waivers, int waiverMonths, bool memberships, DateTime today)
    {
        var problems = new List<string>();
        today = today.Date;

        if (waivers)
        {
            if (profile.WaiverSignedOn is not { } signed)
            {
                problems.Add(profile.IsJunior ? "Guardian's waiver missing" : "Waiver missing");
            }
            else if (waiverMonths > 0 && signed.Date.AddMonths(waiverMonths) <= today)
            {
                problems.Add($"Waiver ran out {Day(signed.Date.AddMonths(waiverMonths))}");
            }
            else if (profile.IsJunior && string.IsNullOrWhiteSpace(profile.WaiverGuardian))
            {
                problems.Add("Junior: no guardian named on the waiver");
            }

            // A junior with no age band (15 Nov 2026, children's privacy plan
            // 2.1) gets the under-13 rules until the desk picks one. Beside
            // the Junior tick in the papers window, so asked for here too.
            if (profile.IsJunior && profile.JuniorAge == JuniorAge.NotSet)
            {
                problems.Add(NoAgeBand);
            }
        }

        if (memberships)
        {
            if (string.IsNullOrWhiteSpace(profile.MembershipNumber) && profile.MembershipExpires is null)
            {
                problems.Add("Membership missing");
            }
            else if (profile.MembershipExpires is { } expires && expires.Date < today)
            {
                problems.Add($"Membership expired {Day(expires.Date)}");
            }
        }

        return problems;
    }

    /// <summary>The Papers cell: "OK", or the problems joined ("Waiver missing · Membership expired 3 Oct").</summary>
    public static string Cell(IReadOnlyList<string> problems) =>
        problems.Count == 0 ? "OK" : string.Join(" · ", problems);

    /// <summary>
    /// The Post the heats line: "2 drivers' papers aren't in order: Al
    /// Gorithim (Waiver missing) and Hugh Turn (Membership expired 3 Oct)."
    /// Empty when everyone's are.
    /// </summary>
    public static string PostingWords(IReadOnlyList<(string Name, IReadOnlyList<string> Problems)> drivers)
    {
        var listed = drivers.Where(d => d.Problems.Count > 0).ToList();

        if (listed.Count == 0)
        {
            return string.Empty;
        }

        var each = listed.Take(4).Select(d => $"{d.Name} ({string.Join(", ", d.Problems)})").ToList();
        var more = listed.Count - each.Count;
        var list = more > 0
            ? $"{string.Join(", ", each)} and {more} more"
            : each.Count == 1 ? each[0] : $"{string.Join(", ", each.Take(each.Count - 1))} and {each[^1]}";

        return listed.Count == 1
            ? $"1 driver's papers aren't in order: {list}."
            : $"{listed.Count} drivers' papers aren't in order: {list}.";
    }

    /// <summary>A date as the desk says it: "3 Oct", or "3 Oct 2025" when it isn't this year.</summary>
    internal static string Day(DateTime date) =>
        date.Year == DateTime.Today.Year ? date.ToString("d MMM") : date.ToString("d MMM yyyy");
}
