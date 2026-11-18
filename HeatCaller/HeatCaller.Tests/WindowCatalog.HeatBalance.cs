namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// Move drivers between heats (HeatBalanceForm), three ways: a class of
    /// 26 in three heats of eight with two sitting out - the four columns the
    /// window opens wide enough for; three qualifying rounds of three heats,
    /// nine heats that scroll sideways, with a late arrival put in the first
    /// round only (listed as not in the other two); and a night where the
    /// first heat has been recorded and the second is out on track, both
    /// locked. Every fourth driver has the catalog's longest name. Each has
    /// the Group by pace and Spread by pace buttons.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> HeatBalanceWindows() => new (string, Func<Form>)[]
    {
        ("Move drivers between heats", () => HeatBalanceBoard(qualifyingRounds: 1)),
        ("Move drivers between heats (9 heats)", () => HeatBalanceBoard(qualifyingRounds: 3, lateArrival: true)),
        ("Move drivers between heats (recorded and on track)", () => HeatBalanceBoard(qualifyingRounds: 1, locked: true)),

        // Posting the heats (PostHeatsForm, RaceProgramControl.Posting): with
        // cars the loop hasn't heard and papers not in order, as long as the
        // warning gets; with every car heard; and on a club night of races.
        ("Post the Heats", () => new PostHeatsForm(clubNight: false,
            HeatPosting.NotHeardWords(Drivers(16).Select(d => d.DriverName).ToList(), listed: 12)
            + " 2 drivers' papers aren't in order: Alexandria Montgomery-Fitzwilliam 4 (no waiver) and Driver 7 (membership ran out).")),
        ("Post the Heats (all heard)", () => new PostHeatsForm(clubNight: false, string.Empty)),
        ("Post the Races", () => new PostHeatsForm(clubNight: true, HeatPosting.NotHeardWords(new[] { "Driver 3" }))),
    };

    /// <summary>
    /// A class of 26 dealt into three heats of eight in each qualifying
    /// round, the last two drivers in none. Each round deals them out from
    /// a different place, as a reseed does. One round is called "Heat 1" and
    /// so on, as the program builder names a one-round night; several are
    /// "Q1 Heat 1" and so on.
    /// </summary>
    private static HeatBalanceForm HeatBalanceBoard(int qualifyingRounds, bool lateArrival = false, bool locked = false)
    {
        const int heatsPerRound = 3;
        const int perHeat = 8;
        const int inHeats = heatsPerRound * perHeat;

        var roster = Drivers(inHeats + 2);
        var heats = new List<RoundDefinition>();

        for (var round = 1; round <= qualifyingRounds; round++)
        {
            for (var heat = 1; heat <= heatsPerRound; heat++)
            {
                var drivers = Enumerable.Range(0, perHeat)
                    .Select(i => roster[((heat - 1) * perHeat + i + (round - 1) * 3) % inHeats].Transponder)
                    .ToList();

                heats.Add(new RoundDefinition
                {
                    ClassName = "Stock Buggy",
                    RoundLabel = qualifyingRounds > 1 ? $"Q{round} Heat {heat}" : $"Heat {heat}",
                    Kind = RoundKind.Qualifier,
                    Order = heats.Count,
                    AssignedTransponders = drivers,
                });
            }
        }

        // Turned up after the first round was drawn and put in its last
        // heat by hand - still to be found a heat in the other two.
        if (lateArrival)
        {
            heats[heatsPerRound - 1].AssignedTransponders.Add(roster[inHeats].Transponder);
        }

        string? onTrack = null;

        if (locked)
        {
            heats[0].LinkedSessionId = "recorded-heat-1";
            onTrack = heats[1].Id;
        }

        return new HeatBalanceForm(
            "Stock Buggy",
            heats,
            roster,
            ProfileStore.Load(),
            Color.FromArgb(0xE0, 0x4E, 0x39),
            roundsAreOneRaceDay: true,
            onTrackRoundId: onTrack,

            // A recent best lap for all but the last four, so Group by pace
            // and Spread by pace are on the form as a club with history sees it.
            pace: roster.Take(roster.Count - 4)
                .Select((d, i) => (d.Transponder, Ms: 18_000L + i * 137))
                .ToDictionary(x => x.Transponder, x => x.Ms, StringComparer.OrdinalIgnoreCase));
    }
}
