namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), Race Control at the flag: the sectors line
/// crediting a disqualified driver, and the unofficial result giving the
/// winner the fastest lap because another driver shares their spoken name.
/// </summary>
public sealed class BugHunt24_RaceControlControl_4Tests
{
    // --- F217: a DQ is not quickest through a sector -------------------------

    private static SessionResultEntry Row(string transponder, int position, string outcome, params long[][] sectorsPerLap)
    {
        var flying = sectorsPerLap.Select(s => s.Sum()).ToArray();

        return new SessionResultEntry
        {
            Transponder = transponder,
            DriverName = "Driver " + transponder,
            Position = position,
            Outcome = outcome,
            LapTimesMs = new[] { 20_000L }.Concat(flying).ToList(),
            Laps = flying.Length,
            FastLapMs = flying.Min(),
            SectorTimesMs = new List<long[]?> { null }.Concat(sectorsPerLap.Select(s => (long[]?)s)).ToList(),
        };
    }

    private static SessionRecord Race(params SessionResultEntry[] rows) => new()
    {
        SessionType = RaceSessionType.Race,
        Results = rows.ToList(),
    };

    [Fact]
    public void BugHunt24_A_disqualified_driver_is_not_named_quickest_through_a_sector()
    {
        var record = Race(
            Row("1001", 1, string.Empty, new long[] { 4_100, 5_000 }),
            Row("1002", 2, string.Empty, new long[] { 4_200, 5_100 }),
            Row("1003", 3, ResultEditor.Dq, new long[] { 3_900, 4_800 }));

        var (field, cut) = RaceControlControl.SectorsAtTheFlagField(record, _ => false);

        Assert.Equal(2, cut);
        Assert.DoesNotContain(field, x => x.Result.Transponder == "1003");

        var quickest = RaceControlControl.QuickestThroughSectors(field.Select(x => (x.Result.Position, x.Bests)).ToList(), cut);

        Assert.Equal(new[] { "1001", "1001" }, quickest.Select(i => field[i].Result.Transponder).ToArray());
    }

    [Fact]
    public void BugHunt24_A_DNF_keeps_its_sectors_at_the_flag()
    {
        var record = Race(
            Row("1001", 1, string.Empty, new long[] { 4_100, 5_000 }),
            Row("1003", 2, ResultEditor.Dnf, new long[] { 3_900, 4_800 }));

        var (field, cut) = RaceControlControl.SectorsAtTheFlagField(record, _ => false);
        var quickest = RaceControlControl.QuickestThroughSectors(field.Select(x => (x.Result.Position, x.Bests)).ToList(), cut);

        Assert.Equal(new[] { "1003", "1003" }, quickest.Select(i => field[i].Result.Transponder).ToArray());
    }

    [Fact]
    public void BugHunt24_Drivers_kept_out_of_records_are_still_left_out_of_the_sectors_at_the_flag()
    {
        var record = Race(
            Row("1001", 1, string.Empty, new long[] { 4_100, 5_000 }),
            Row("1003", 2, string.Empty, new long[] { 3_900, 4_800 }));

        var (field, _) = RaceControlControl.SectorsAtTheFlagField(record, t => t == "1003");

        Assert.Single(field);
        Assert.Equal("1001", field[0].Result.Transponder);
    }

    [Fact]
    public void BugHunt24_A_race_where_only_the_disqualified_driver_had_sectors_has_no_sectors_line()
    {
        var record = Race(
            new SessionResultEntry { Transponder = "1001", Position = 1, Laps = 3, FastLapMs = 9_000, LapTimesMs = new List<long> { 20_000, 9_000, 9_100 } },
            Row("1003", 2, ResultEditor.Dq, new long[] { 3_900, 4_800 }));

        var (field, _) = RaceControlControl.SectorsAtTheFlagField(record, _ => false);

        Assert.Empty(field);
    }

    // --- F218: two drivers with the same spoken name -------------------------

    [Fact]
    public void BugHunt24_The_fastest_lap_is_not_given_to_a_winner_who_only_shares_its_setter_s_name()
    {
        var line = RaceControlControl.FlagResult(new[] { "Dave", "Sam" }, "Dave", fastestIsWinner: false);

        Assert.NotNull(line);
        Assert.Contains("The fastest lap of the race went to Dave.", line, StringComparison.Ordinal);
        Assert.DoesNotContain("too", line, StringComparison.Ordinal);
    }

    [Fact]
    public void BugHunt24_The_winner_who_set_the_fastest_lap_still_hears_too()
    {
        var line = RaceControlControl.FlagResult(new[] { "Dave", "Sam" }, "Dave", fastestIsWinner: true);

        Assert.Equal("Unofficially, that's Dave taking the win, ahead of Sam. And Dave had the fastest lap of the race too.", line);
    }

    [Fact]
    public void BugHunt24_Whether_the_winner_set_the_fastest_lap_is_decided_on_the_car()
    {
        var winner = new SessionResultEntry { Transponder = "1001", DriverName = "Dave", Position = 1 };
        var otherDave = new SessionResultEntry { Transponder = "1002", DriverName = "Dave", Position = 2 };
        var sameCar = new SessionResultEntry { Transponder = " 1001 ", DriverName = "Dave", Position = 1 };

        Assert.False(RaceControlControl.FastestIsWinner(winner, otherDave));
        Assert.True(RaceControlControl.FastestIsWinner(winner, winner));
        Assert.True(RaceControlControl.FastestIsWinner(winner, sameCar));
        Assert.False(RaceControlControl.FastestIsWinner(winner, null));
    }

    [Fact]
    public void BugHunt24_The_two_Daves_race_reads_the_fastest_lap_as_the_other_Dave_s()
    {
        var record = Race(
            new SessionResultEntry { Transponder = "1001", DriverName = "Dave", Position = 1, Laps = 10, FastLapMs = 9_500 },
            new SessionResultEntry { Transponder = "1002", DriverName = "Sam", Position = 2, Laps = 10, FastLapMs = 9_600 },
            new SessionResultEntry { Transponder = "1003", DriverName = "Dave", Position = 3, Laps = 9, FastLapMs = 9_000 });

        var (podium, fastest) = RaceControlControl.FlagResultRows(record);
        var line = RaceControlControl.FlagResult(
            podium.Select(r => r.DriverName).ToList(),
            fastest?.DriverName,
            RaceControlControl.FastestIsWinner(podium[0], fastest));

        Assert.Equal(
            "Unofficially, that's Dave taking the win, ahead of Sam and Dave. The fastest lap of the race went to Dave.",
            line);
    }
}
