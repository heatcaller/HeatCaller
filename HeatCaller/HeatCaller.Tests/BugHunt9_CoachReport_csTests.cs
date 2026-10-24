namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 9 (22 Oct 2026): the report card's costliest sector and its
/// "mostly in sector N" against the quickest rival only checked that two sets
/// of sectors had the same NUMBER of sectors. After the split loop moved on
/// the same layout, sector one meant a different stretch of track in each
/// heat, and the card could blame the wrong part of the circuit. They now
/// also compare the cut, as the best sectors already did.
/// </summary>
public sealed class BugHunt9_CoachReport_csTests
{
    private const string Me = "1001";
    private const string Sam = "1002";
    private const string Layout = "Oval";

    private static readonly DateTime Tonight = new(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime At(int hour) => Tonight.AddHours(hour);

    private static SessionResultEntry RowWithSectors(string who, params long[][] sectorsPerLap)
    {
        var flying = sectorsPerLap.Select(s => s.Sum()).ToArray();

        return new SessionResultEntry
        {
            Transponder = who,
            DriverName = who,
            LapTimesMs = new[] { 20_000L }.Concat(flying).ToList(),
            Laps = flying.Length,
            FastLapMs = flying.Min(),
            SectorTimesMs = new List<long[]?> { null }.Concat(sectorsPerLap.Select(s => (long[]?)s)).ToList(),
        };
    }

    private static SessionRecord Session(DateTime startUtc, string cut, params SessionResultEntry[] rows) => new()
    {
        SessionType = RaceSessionType.Race,
        TrackLayoutName = Layout,
        StartedAtUtc = startUtc,
        EndedAtUtc = startUtc.AddMinutes(6),
        SectorCut = cut,
        Results = rows.ToList(),
    };

    private static IReadOnlyList<string> Card(params SessionRecord[] history) =>
        CoachReport.Lines(new CoachReport.Input(
            Me,
            history,
            Tonight,
            utc => utc.Date,
            t => t == Sam ? "Sam" : string.Empty,
            null,
            _ => "7:40 pm"));

    [Fact]
    public void A_rival_from_a_heat_cut_differently_gets_no_sector_blame()
    {
        // The rival is only ever looked for in heats the driver ran too, so
        // they race Sam in the heat before the loop moved, then run again after.
        var card = Card(
            Session(At(19), "2:1,1;5,5", RowWithSectors(Me, new[] { 4400L, 4700L }), RowWithSectors(Sam, new[] { 4000L, 4600L })),
            Session(At(20), "2:1,1;7,2", RowWithSectors(Me, new[] { 4300L, 4600L })));

        Assert.Equal("0.30s off Sam's best of 8.600s, the quickest lap of anyone you raced with tonight.", card[^1]);
    }

    [Fact]
    public void A_rival_from_a_heat_cut_the_same_way_still_gets_the_sector()
    {
        var card = Card(
            Session(At(19), "2:1,1;5,5", RowWithSectors(Me, new[] { 4400L, 4700L }), RowWithSectors(Sam, new[] { 4000L, 4600L })),
            Session(At(20), "2:1,1;5,5", RowWithSectors(Me, new[] { 4300L, 4600L })));

        Assert.Equal("0.30s off Sam's best of 8.600s, the quickest lap of anyone you raced with tonight - mostly in sector one.", card[^1]);
    }

    [Fact]
    public void Laps_cut_differently_from_the_best_sectors_do_not_count_toward_the_costliest_sector()
    {
        // The old cut's laps lose 0.30s in sector two against the new cut's
        // bests; measured together they would single it out. Only the new
        // cut's laps count, and they lose nothing.
        var card = Card(
            Session(At(19), "2:1,1;5,5", RowWithSectors(Me, new[] { 4000L, 5300L }, new[] { 4000L, 5300L }, new[] { 4000L, 5300L })),
            Session(At(20), "2:1,1;7,2", RowWithSectors(Me, new[] { 4000L, 5000L }, new[] { 4000L, 5000L }, new[] { 4000L, 5000L })));

        Assert.DoesNotContain(card, line => line.StartsWith("Sector", StringComparison.Ordinal));
    }
}
