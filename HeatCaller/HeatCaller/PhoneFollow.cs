namespace HeatCaller;

/// <summary>A race still to run, as the phone pages need it: its title, its cars in grid order, and whether it is on track now.</summary>
/// <param name="RoundId">The round's id, when the caller knows it. Null by default so older callers are unchanged.</param>
/// <param name="Staged">The race on track is staged - its drivers should be at the driver stand now.</param>
/// <param name="EtaUtc">When the race is expected to start (RaceEta), or null when nobody can say.</param>
/// <remarks>
/// RoundId, Staged and EtaUtc added 22 Oct 2026 (driver experience), with
/// defaults so every existing caller still compiles and means what it did.
/// </remarks>
public sealed record UpcomingRace(
    string Title, IReadOnlyList<string> Grid, bool OnTrack,
    string? RoundId = null, bool Staged = false, DateTime? EtaUtc = null);

/// <summary>
/// Where one driver's next race is: its title, how many races run before it
/// (0 = theirs is next), their grid slot (1 = pole) and whether it is on
/// track already.
/// </summary>
/// <remarks>
/// Staged and EtaUtc added 22 Oct 2026 (driver experience): copied from the
/// race, so a phone can buzz again when the race is staged and say "about
/// 7 min". Defaulted, so existing callers are unchanged.
/// </remarks>
public sealed record CallUp(string Race, int Away, int Slot, bool OnTrack, bool Staged = false, DateTime? EtaUtc = null);

/// <summary>One driver's race at a glance, for a phone following them: position, laps, last lap and the gaps either side.</summary>
public sealed record FollowCard(int Position, int Laps, long LastLapMs, string Ahead, string Behind);

/// <summary>
/// The "follow a driver" view on the Live Results page (WebServerService),
/// worked out here so it can be tested without a browser.
///
/// The board is unreadable on a phone held at arm's length beside the
/// track: twelve columns of small type. A driver - or a parent - wants one
/// thing: where am I, how far to the car ahead, how far back is the one
/// behind. And before that, the thing that runs a race day late: when is my
/// next race, and where do I grid. The phone that follows a driver says so,
/// and buzzes when their race is next (the page does the buzzing).
///
/// Only in the copy served on the club's Wi-Fi and, per badge, in the relay's
/// private driverlive (22 Oct 2026, driver experience): the online live page
/// (the relay's) is public and keyed by nothing a stranger should be able to
/// follow, but driverlive is reachable only with that driver's own badge.
/// </summary>
internal static class PhoneFollow
{
    /// <summary>What a staged call-up says (22 Oct 2026, driver experience).</summary>
    public const string StagedWords = "Staged - go to the driver stand";

    /// <summary>
    /// Each driver's next race among <paramref name="upcoming"/> (in running
    /// order): the first they are in. A driver in two - a heat and the next
    /// round's - is called for the first.
    /// </summary>
    public static IReadOnlyDictionary<string, CallUp> CallUps(IReadOnlyList<UpcomingRace> upcoming)
    {
        var callUps = new Dictionary<string, CallUp>(StringComparer.OrdinalIgnoreCase);
        var before = 0;

        foreach (var race in upcoming)
        {
            for (var slot = 0; slot < race.Grid.Count; slot++)
            {
                var id = race.Grid[slot];

                if (!string.IsNullOrWhiteSpace(id) && !callUps.ContainsKey(id))
                {
                    callUps[id] = new CallUp(
                        race.Title, race.OnTrack ? 0 : before, slot + 1, race.OnTrack, race.Staged, race.EtaUtc);
                }
            }

            // The race on track now is not one to wait for.
            if (!race.OnTrack)
            {
                before++;
            }
        }

        return callUps;
    }

    /// <summary>The call-up in words, for the page: "You're up next", "2 races before yours", "On track now".</summary>
    /// <remarks>
    /// Staged comes first (22 Oct 2026, driver experience): a staged race is
    /// also the one on track, and "On track now" told a driver nothing about
    /// where to be.
    /// </remarks>
    public static string Words(CallUp callUp) =>
        callUp.Staged ? StagedWords
        : callUp.OnTrack ? "On track now"
        : callUp.Away == 0 ? "You're up next"
        : $"{Plural.Of(callUp.Away, "race")} before yours";

    /// <summary>Every car on the board's card, by transponder.</summary>
    public static IReadOnlyDictionary<string, FollowCard> Cards(IReadOnlyList<RacerStanding> ordered)
    {
        var cards = new Dictionary<string, FollowCard>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < ordered.Count; i++)
        {
            var car = ordered[i];

            var ahead = i == 0 ? "Leading" : RaceGaps.Format(RaceGaps.Between(car, ordered[i - 1]));
            var behind = i == ordered.Count - 1 ? "-" : RaceGaps.Format(RaceGaps.Between(ordered[i + 1], car));
            var last = car.LapTimesMs.Count > 0 ? car.LapTimesMs[^1] : 0;

            cards[car.TransponderId] = new FollowCard(i + 1, car.Laps, last, ahead, behind);
        }

        return cards;
    }
}
