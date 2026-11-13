using System.Security.Cryptography;
using System.Text;

namespace HeatCaller;

/// <summary>
/// MARSHAL SWAPS FROM PHONES (12 Nov 2026, setup plan step 8, gap 4.3).
///
/// WHY: a driver who can't stand on Turn 3 for Q2 Heat 3 - a car to fix, a
/// child to collect from the pits - used to walk to the desk and ask for a
/// swap (Swap... in the Marshals window, 11 Nov 2026). Usually they have
/// already found somebody. Now they ask that somebody from their Me page, the
/// asked driver says yes or no on theirs, and a yes becomes the same swap the
/// desk would have made (RoundDefinition.MarshalCovers) - through the desk's
/// Driver Requests first while "Marshal swaps from phones need the desk's OK"
/// is on, which it is unless the club turns it off.
///
/// WHO CAN BE ASKED: tonight's checked-in drivers who aren't driving in that
/// race and aren't already marshalling it. They are offered by a short key
/// (<see cref="KeyOf"/>), never by transponder or badge code: a badge code is
/// the credential for a driver's own night, so handing the list of them to
/// every marshal would let anyone act as anyone. The key is a hash of the
/// badge code, so only this laptop - which has the codes - can turn it back.
///
/// NAMES: the private live document carries no other driver's name, with this
/// one deliberate exception - the picker has to say who, and the asked driver
/// has to read who asked. Only the short form (<see cref="ShortName"/>, "Max
/// P."), only to a driver it concerns, and only of drivers on tonight's
/// public board already.
///
/// Pure, so it is tested without a form.
/// </summary>
public static class MarshalSwaps
{
    /// <summary>Hex characters in a driver's cover key.</summary>
    public const int KeyLength = 12;

    /// <summary>How long an ask to cover stays open. A race night, with room.</summary>
    public static readonly TimeSpan OffersLast = TimeSpan.FromHours(12);

    /// <summary>The most drivers offered in the "Ask someone to cover" picker.</summary>
    public const int MostChoices = 40;

    /// <summary>The asked driver's key for the phone: the first <see cref="KeyLength"/> hex of SHA-256 of their badge code.</summary>
    public static string KeyOf(string badge)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("heatcaller-marshal-cover|" + (badge ?? string.Empty).Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash).ToLowerInvariant()[..KeyLength];
    }

    /// <summary>"Max Power" to "Max P.", "Cher" to "Cher". Empty stays empty.</summary>
    public static string ShortName(string? name)
    {
        var words = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return words.Length switch
        {
            0 => string.Empty,
            1 => words[0],
            _ => $"{words[0]} {char.ToUpperInvariant(words[^1][0])}.",
        };
    }

    /// <summary>
    /// Who can be asked to cover a post in a round: tonight's drivers (main
    /// transponders), less those driving in it, those already marshalling it
    /// and the marshal asking. In the order given, each once.
    /// </summary>
    public static List<string> Candidates(IEnumerable<string> tonight, IEnumerable<string> driving, IEnumerable<string> marshalling, string asking)
    {
        var no = new HashSet<string>(driving.Concat(marshalling).Append(asking).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return tonight
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Where(t => !no.Contains(t) && seen.Add(t))
            .ToList();
    }

    /// <summary>
    /// Puts <paramref name="cover"/> on <paramref name="marshal"/>'s post for
    /// the round, the way Swap... in the Marshals window does: when the
    /// marshal is covering somebody already, it is that somebody's post that
    /// moves. Null when done, or the reason it can't be.
    /// </summary>
    public static string? Cover(RoundDefinition round, string marshal, string cover)
    {
        if (round.AssignedTransponders.Contains(cover, StringComparer.OrdinalIgnoreCase))
        {
            return "They're driving in that race, so they can't marshal it.";
        }

        var owner = round.MarshalCovers?.FirstOrDefault(kv => string.Equals(kv.Value, marshal, StringComparison.OrdinalIgnoreCase)).Key ?? marshal;
        var covers = round.MarshalCovers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.Equals(cover, owner, StringComparison.OrdinalIgnoreCase))
        {
            covers.Remove(owner);
        }
        else
        {
            covers[owner] = cover;
        }

        round.MarshalCovers = covers.Count == 0 ? null : covers;
        return null;
    }

    /// <summary>A driver's marshal duty in one round, as the desk reads it to file a swap: the race's title and the post.</summary>
    public sealed record Duty(string Title, string Post);

    /// <summary>Where one ask to cover stands: asked, waiting for the desk, accepted or declined.</summary>
    public const string Asked = "asked";
    public const string AtDesk = "desk";
    public const string Accepted = "accepted";
    public const string Declined = "declined";

    /// <summary>
    /// One ask to cover a post: who asked (badge), who was asked (badge), the
    /// round, its title and post as they were when asked, and where it stands.
    /// </summary>
    public sealed record Offer(string Id, string FromBadge, string ToBadge, string RoundId, string Title, string Post, string State, DateTime AskedUtc);

    /// <summary>
    /// Every ask to cover still open, from the requests drivers sent: each
    /// swap the laptop filed (approved, with who was asked noted on it) in the
    /// last <see cref="OffersLast"/>, and where the asked driver's answer has
    /// got to. A newer ask by the same driver for the same race replaces an
    /// older one. Oldest first.
    /// </summary>
    internal static IReadOnlyList<Offer> Offers(IEnumerable<DriverRequest> requests, DateTime nowUtc)
    {
        var all = requests.ToList();

        var swaps = all
            .Where(r => r.Kind == DriverAsk.Swap && r.State == DriverAsk.Approved
                        && r.Text("toBadge").Length > 0 && r.Text("round").Length > 0
                        && nowUtc - r.AskedUtc <= OffersLast)
            .GroupBy(r => (r.Badge, Round: r.Text("round")))
            .Select(g => g.OrderBy(r => r.AskedUtc).Last())
            .OrderBy(r => r.AskedUtc)
            .ToList();

        var offers = new List<Offer>();

        foreach (var swap in swaps)
        {
            var toBadge = swap.Text("toBadge");
            var answer = all
                .Where(r => r.Kind == DriverAsk.Cover && r.Text("offer") == swap.Id && r.Badge == toBadge)
                .OrderBy(r => r.AskedUtc)
                .LastOrDefault();

            var state = answer is null ? Asked
                : answer.Text("answer") != "yes" ? Declined
                : answer.State == DriverAsk.Waiting ? AtDesk
                : answer.State == DriverAsk.Approved ? Accepted
                : Declined;

            offers.Add(new Offer(swap.Id, swap.Badge, toBadge, swap.Text("round"), swap.Text("title"), swap.Text("post"), state, swap.AskedUtc));
        }

        return offers;
    }
}
