namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): the report card writes gaps to the hundredth,
/// so an ideal lap 1-4 ms under the best lap printed "0.00s still to find",
/// and a rival 1-4 ms quicker printed "0.00s off Sam's best" - a line that
/// says there is a gap and gives it as nothing. Under 5 ms the ideal lap is
/// left out and the driver is level with the rival.
/// </summary>
public sealed class BugHunt24_CoachReportTests
{
    private const string Me = "1001";
    private const string Sam = "1002";
    private const string Layout = "Oval";

    private static readonly DateTime Tonight = new(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);

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

    private static SessionRecord Session(DateTime startUtc, params SessionResultEntry[] rows) => new()
    {
        SessionType = RaceSessionType.Race,
        TrackLayoutName = Layout,
        StartedAtUtc = startUtc,
        EndedAtUtc = startUtc.AddMinutes(6),
        SectorCut = "2:1,1;5,5",
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
    public void BugHunt24_IdealLapUnderFiveMsBelowBest_SaysNoGapOfNothing()
    {
        // Best lap 8.912; best sectors 4.397 + 4.512 = 8.909, 3 ms under it.
        var card = Card(Session(At(19), RowWithSectors(Me, new[] { 4400L, 4512L }, new[] { 4397L, 4520L })));

        Assert.DoesNotContain("ideal lap", card[0], StringComparison.Ordinal);
        Assert.StartsWith("Best lap tonight on Oval: 8.912s", card[0], StringComparison.Ordinal);
        Assert.DoesNotContain(card, line => line.Contains("0.00s", StringComparison.Ordinal));
    }

    [Fact]
    public void BugHunt24_IdealLapTenMsBelowBest_StillSaysWhatIsLeftToFind()
    {
        // Best lap 8.912; best sectors 4.390 + 4.512 = 8.902, 10 ms under it.
        var card = Card(Session(At(19), RowWithSectors(Me, new[] { 4400L, 4512L }, new[] { 4390L, 4530L })));

        Assert.Contains("ideal lap of 8.902s - 0.01s still to find.", card[0], StringComparison.Ordinal);
    }

    [Fact]
    public void BugHunt24_RivalUnderFiveMsQuicker_SaysLevelNotZeroOff()
    {
        var card = Card(Session(At(19),
            RowWithSectors(Me, new[] { 4400L, 4515L }),
            RowWithSectors(Sam, new[] { 4400L, 4512L })));

        Assert.Equal("Level with Sam's best of 8.912s, the quickest lap of anyone you raced with tonight.", card[^1]);
        Assert.DoesNotContain(card, line => line.Contains("0.00s", StringComparison.Ordinal));
    }

    [Fact]
    public void BugHunt24_RivalTenMsQuicker_StillGivesTheGap()
    {
        var card = Card(Session(At(19),
            RowWithSectors(Me, new[] { 4400L, 4522L }),
            RowWithSectors(Sam, new[] { 4400L, 4512L })));

        Assert.StartsWith("0.01s off Sam's best of 8.912s, the quickest lap of anyone you raced with tonight", card[^1], StringComparison.Ordinal);
    }
}
