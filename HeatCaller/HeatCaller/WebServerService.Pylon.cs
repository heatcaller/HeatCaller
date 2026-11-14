using System.Text.Json.Nodes;

namespace HeatCaller;

/// <summary>
/// What the broadcast graphics (/broadcast) need beyond the board itself:
/// the pylon's extras - car numbers, laps led, the gap to the leader and the
/// lead change count - the sponsors who present a moment, the banner for
/// the latest moment, and the driver ratings at the flag.
///
/// LAN copy only, like the follow cards it sits beside: everything here is
/// keyed by the roster's opaque id (RosterId), never a transponder, for the
/// reason AddIdsAndGrid gives - /api/state is served to any phone on the
/// club Wi-Fi with no PIN.
/// </summary>
public sealed partial class WebServerService
{
    /// <summary>
    /// How many times the lead has changed hands this race and among how many
    /// drivers - the announcer's count (AnnouncementService.Leads), so the
    /// tower and the voice can never give different numbers. Null before the
    /// host wires it, and the tower then simply has no count to show.
    /// </summary>
    public Func<(int Changes, int Leaders)>? LeadChanges { get; set; }

    /// <summary>The latest broadcast moment - a lead change, a milestone - for the banner. See BroadcastMomentFeed.</summary>
    public BroadcastMomentFeed Moments { get; } = new();

    /// <summary>
    /// Adds node["pylon"], node["presented"], node["moment"] and
    /// node["ratings"] to the LAN copy of the live state.
    /// </summary>
    /// <remarks>
    /// WHY (24 Oct 2026, timing layer, P8): the presenting sponsors come from
    /// the window's side the build read (ui - see WebServerService.
    /// LiveInputs), the names from Names, and the race's state from the same
    /// moment as the board (view, threaded; null inline, where it is read
    /// here as always), so this can run off the UI thread.
    /// </remarks>
    private void AddBroadcast(JsonObject node, IReadOnlyList<RacerStanding> ordered, byte[] rosterKey, UiSide ui, LiveRaceView? view)
    {
        var (changes, leaders) = (0, 0);

        try
        {
            (changes, leaders) = LeadChanges?.Invoke() ?? (0, 0);
        }
        catch
        {
            // The tower without the count, rather than no tower.
        }

        var cars = new JsonObject();
        var front = ordered.Count > 0 ? ordered[0] : null;

        foreach (var standing in ordered)
        {
            var id = RosterId(rosterKey, standing.TransponderId);

            if (cars.ContainsKey(id))
            {
                continue;
            }

            cars[id] = new JsonObject
            {
                // The racing number from the driver's profile - empty for a
                // driver without one, and the tower then leaves the box off.
                ["num"] = (Names.Get(standing.TransponderId)?.RaceNumber ?? string.Empty).Trim(),
                ["led"] = standing.LapsLed,

                // To the car at the top of the board, in the same words the
                // interval to the car ahead is in (RaceGaps.Format), so the
                // two read alike when the tower cycles between them.
                ["lead"] = front is null || ReferenceEquals(front, standing)
                    ? string.Empty
                    : RaceGaps.Format(RaceGaps.Between(standing, front)),
            };
        }

        node["pylon"] = new JsonObject
        {
            ["changes"] = changes,
            ["leaders"] = leaders,
            ["cars"] = cars,
        };

        // Who presents the moments the graphics show, by moment name. A
        // caption costs the room nothing, so it is shown every time - unlike
        // the voice, which rations its "presented by" (SponsoredMomentGate).
        //
        // Worked out with the rest of the window's side (UiSideNow), in the
        // same order. Null there when the sponsors could not be read, which
        // leaves the rest of the broadcast extras out, as a failed read here
        // always did.
        var presented = new JsonObject();
        var presentedBy = ui.Presented ?? throw new InvalidOperationException("The sponsors could not be read.");

        foreach (var (moment, sponsor) in presentedBy)
        {
            presented[moment] = sponsor;
        }

        node["presented"] = presented;

        node["moment"] = Moments.Current() is { } current
            ? new JsonObject
            {
                ["seq"] = current.Seq,
                ["kind"] = current.Moment.Kind,
                ["title"] = current.Moment.Title,
                ["line"] = current.Moment.Line,
                ["sponsor"] = current.Moment.Sponsor ?? string.Empty,
            }
            : null;

        // The driver ratings, once the race is over - for the podium. Not
        // during it: a rating halfway through rewards whoever happens to
        // be leading on the lap the page was read.
        if ((view?.State ?? _race.State) == RaceState.Finished)
        {
            var ratings = new JsonObject();

            foreach (var (transponder, rating) in DriverRating.ForStandings(ordered))
            {
                var id = RosterId(rosterKey, transponder);

                if (!ratings.ContainsKey(id))
                {
                    ratings[id] = rating;
                }
            }

            node["ratings"] = ratings;
        }
        else
        {
            node["ratings"] = null;
        }
    }
}
