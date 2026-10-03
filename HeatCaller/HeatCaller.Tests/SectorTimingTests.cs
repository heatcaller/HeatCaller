namespace HeatCaller.Tests;

/// <summary>
/// Sector times in the race (RaceControlSession.ProcessSplitPass and the
/// lap that closes them): a lap cut at the split loop into sectors that
/// always add up to it, a missed split left blank rather than guessed, a
/// split read arriving just after the line, a lap the line may have missed,
/// and the readings built on them (Sectors).
/// </summary>
public sealed class SectorTimingTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static RaceControlSession Race(int splits = 1)
    {
        // Demo mode leaves the start reference alone: these crossings come
        // far faster than the wall clock.
        var race = new RaceControlSession { IsDemoMode = true, SplitCount = splits };
        race.Stage();
        race.GreenFlag(0);
        return race;
    }

    private static void Cross(RaceControlSession race, string car, uint at) =>
        race.ProcessLapEvent(new DirectLapEvent { TransponderId = car, DeviceTimeMs = at, SignalHits = 5, SignalStrength = 100 });

    private static RacerStanding Of(RaceControlSession race, string car) =>
        race.GetOrderedStandings().Single(s => s.TransponderId == car);

    [Fact]
    public void A_lap_is_cut_at_the_split_into_sectors_that_add_up_to_it()
    {
        var race = Race();
        var seen = new List<(int Lap, long[] Sectors)>();
        race.SectorsRecorded += (_, lap, sectors) => seen.Add((lap, sectors));

        // The start lap, then the split 4s into the first flying lap.
        Cross(race, "P1", 5_000);
        Assert.True(race.ProcessSplitPass("P1", TimingPoint.Split1, 9_000));
        Cross(race, "P1", 15_000);

        var car = Of(race, "P1");

        Assert.Null(car.SectorsOf(0));
        Assert.Equal(new long[] { 4_000, 6_000 }, car.SectorsOf(1));
        Assert.Equal(1, Assert.Single(seen).Lap);
    }

    [Fact]
    public void A_split_before_the_first_crossing_is_not_used()
    {
        var race = Race();

        Assert.False(race.ProcessSplitPass("P1", TimingPoint.Split1, 2_000));

        Cross(race, "P1", 5_000);
        Assert.Null(Of(race, "P1").SectorsOf(0));
    }

    [Fact]
    public void A_missed_split_leaves_the_lap_without_sectors_and_the_next_lap_has_them()
    {
        var race = Race();

        Cross(race, "P1", 5_000);
        Cross(race, "P1", 15_000);

        Assert.True(race.ProcessSplitPass("P1", TimingPoint.Split1, 19_500));
        Cross(race, "P1", 25_000);

        var car = Of(race, "P1");
        Assert.Null(car.SectorsOf(1));
        Assert.Equal(new long[] { 4_500, 5_500 }, car.SectorsOf(2));
    }

    [Fact]
    public void A_split_read_arriving_just_after_the_line_still_completes_the_lap()
    {
        var race = Race();

        Cross(race, "P1", 5_000);
        Cross(race, "P1", 15_000);

        // Read at 14.0s, but heard after the line counted the lap at 15.0s.
        Assert.True(race.ProcessSplitPass("P1", TimingPoint.Split1, 14_000));

        Assert.Equal(new long[] { 9_000, 1_000 }, Of(race, "P1").SectorsOf(1));
    }

    [Fact]
    public void Two_crossings_of_the_split_without_the_line_flag_a_possible_missed_lap()
    {
        var race = Race();
        var flagged = 0;
        race.PossibleMissedLap += (_, _, _) => flagged++;

        Cross(race, "P1", 5_000);
        Assert.True(race.ProcessSplitPass("P1", TimingPoint.Split1, 9_000));

        // A repeat read of the same crossing is just ignored...
        Assert.False(race.ProcessSplitPass("P1", TimingPoint.Split1, 9_400));

        // ...a lap's worth later is another crossing - but nothing is said
        // until the line proves it saw nothing in between.
        Assert.True(race.ProcessSplitPass("P1", TimingPoint.Split1, 19_000));
        Assert.Equal(0, flagged);

        // The lap that finally crosses spans two real ones: flagged, no
        // sectors, and the lap itself untouched.
        Cross(race, "P1", 25_000);

        var car = Of(race, "P1");
        Assert.Equal(1, flagged);
        Assert.Equal(20_000, car.LapTimesMs[1]);
        Assert.Null(car.SectorsOf(1));
    }

    [Fact]
    public void A_car_parked_on_the_split_loop_is_one_crossing()
    {
        var race = Race();
        var flagged = 0;
        race.PossibleMissedLap += (_, _, _) => flagged++;

        Cross(race, "P1", 5_000);

        // Read every two seconds while it sits there.
        foreach (var at in new long[] { 9_000, 11_000, 13_000, 15_000, 17_000 })
        {
            race.ProcessSplitPass("P1", TimingPoint.Split1, at);
        }

        Cross(race, "P1", 20_000);

        Assert.Equal(0, flagged);
        Assert.Equal(new long[] { 4_000, 11_000 }, Of(race, "P1").SectorsOf(1));
    }

    [Fact]
    public void A_split_read_just_after_the_line_that_arrives_first_goes_to_the_next_lap()
    {
        var race = Race();
        var flagged = 0;
        race.PossibleMissedLap += (_, _, _) => flagged++;

        Cross(race, "P1", 5_000);
        race.ProcessSplitPass("P1", TimingPoint.Split1, 9_000);

        // The split loop is just past the line: its read at 15.5s is heard
        // before the line's crossing at 15.0s.
        race.ProcessSplitPass("P1", TimingPoint.Split1, 15_500);
        Cross(race, "P1", 15_000);
        Cross(race, "P1", 25_000);

        var car = Of(race, "P1");
        Assert.Equal(0, flagged);
        Assert.Equal(new long[] { 4_000, 6_000 }, car.SectorsOf(1));
        Assert.Equal(new long[] { 500, 9_500 }, car.SectorsOf(2));
    }

    [Fact]
    public void Nothing_is_read_at_the_split_under_a_red_flag()
    {
        var race = Race();

        Cross(race, "P1", 5_000);
        race.RedFlag();

        Assert.False(race.ProcessSplitPass("P1", TimingPoint.Split1, 9_000));
    }

    [Fact]
    public void A_lap_merged_by_hand_no_longer_shows_its_sectors()
    {
        var race = Race();

        Cross(race, "P1", 5_000);
        race.ProcessSplitPass("P1", TimingPoint.Split1, 9_000);
        Cross(race, "P1", 15_000);
        race.ProcessSplitPass("P1", TimingPoint.Split1, 19_000);
        Cross(race, "P1", 25_000);

        Assert.True(race.AdjustLaps("P1", -1));

        var car = Of(race, "P1");
        Assert.Equal(2, car.LapTimesMs.Count);
        Assert.Null(car.SectorsOf(1));
    }

    [Fact]
    public void Without_a_split_loop_nothing_is_cut()
    {
        var race = Race(splits: 0);

        Cross(race, "P1", 5_000);
        Assert.False(race.ProcessSplitPass("P1", TimingPoint.Split1, 9_000));
        Cross(race, "P1", 15_000);

        Assert.Null(Of(race, "P1").SectorsOf(1));
    }

    [Fact]
    public void Bests_the_ideal_lap_and_the_live_gap_at_the_split()
    {
        var race = Race();

        Cross(race, "P1", 5_000);
        race.ProcessSplitPass("P1", TimingPoint.Split1, 9_000);   // 4.0
        Cross(race, "P1", 15_000);                               // 6.0 -> 10.0
        race.ProcessSplitPass("P1", TimingPoint.Split1, 18_500); // 3.5
        Cross(race, "P1", 25_000);                               // 6.5 -> 10.0

        var car = Of(race, "P1");

        Assert.Equal(new long[] { 3_500, 6_000 }, Sectors.Bests(car));
        Assert.Equal(9_500, Sectors.TheoreticalBest(car));

        // On the next lap, at the split in 3.8s: their best lap (the first
        // 10.0, which reached the split in 4.0) - 0.2s up.
        race.ProcessSplitPass("P1", TimingPoint.Split1, 28_800);

        var live = Sectors.LiveDelta(car);
        Assert.NotNull(live);
        Assert.Equal(1, live.Value.Split);
        Assert.Equal(-200, live.Value.DeltaMs);
        Assert.Equal("-0.20", Sectors.FormatDelta(live.Value.DeltaMs));
    }

    [Fact]
    public void Sectors_are_only_trusted_while_they_add_up_to_their_lap()
    {
        Assert.NotNull(Sectors.Checked(new long[] { 4_000, 6_000 }, 10_000));
        Assert.Null(Sectors.Checked(new long[] { 4_000, 6_000 }, 10_001));
        Assert.Null(Sectors.Checked(new long[] { 10_000 }, 10_000));
        Assert.Null(Sectors.Checked(new long[] { -1, 10_001 }, 10_000));
        Assert.Null(Sectors.Checked(null, 10_000));
    }
}
