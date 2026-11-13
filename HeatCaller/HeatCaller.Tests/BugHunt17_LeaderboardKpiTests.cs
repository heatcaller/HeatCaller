using System.Globalization;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), the leaderboard's figures.
///
/// A car credited with the start lap at the green (a jump start) has its
/// real start lap at index 1, and the session keeps that out of best lap -
/// but the pace figures only skipped index 0, so Average, Std Dev, Trend and
/// Time Lost took a grid-to-line dash for a lap. And the columns wrote
/// their seconds in the PC's own region, so a comma-decimal Windows showed
/// "12,345" beside a gap of "+2.345" on the same row.
/// </summary>
public sealed class BugHunt17_LeaderboardKpiTests
{
    private sealed class NoDrivers : IDriverLookup
    {
        public DriverProfile? Get(string transponder) => null;
    }

    private static RacerStanding Car(params long[] laps)
    {
        var car = new RacerStanding { TransponderId = "101", Laps = laps.Length, ElapsedMs = laps.Sum() };
        car.LapTimesMs.AddRange(laps);
        return car;
    }

    private static string Cell(string key, RacerStanding car, long fastestOverall = 0)
    {
        var context = new KpiContext
        {
            Ordered = new[] { car },
            Profiles = new NoDrivers(),
            Tracker = new KpiTracker(),
            FastestOverall = fastestOverall,
        };

        return KpiCatalog.Find(key)!.Compute(car, 0, context).Text;
    }

    [Fact]
    public void The_lap_after_a_credited_start_lap_is_not_a_pace_lap()
    {
        // Parked over the loop at the green: a credited zero, then 15 s from
        // the crossing before the green, then real laps.
        var car = Car(0, 15_000, 10_000, 10_200);
        car.StartLapCredited = true;
        car.FastestLapMs = 10_000;

        Assert.Equal(new long[] { 10_000, 10_200 }, RaceGaps.PaceLaps(car));
        Assert.Equal("10.100", Cell("avg", car));
        Assert.Equal("0.200", Cell("timelost", car));

        // The lap list still has it, so position still means lap number.
        Assert.Equal(new long[] { 15_000, 10_000, 10_200 }, RaceGaps.FlyingLaps(car));
    }

    [Fact]
    public void Without_a_credited_start_lap_the_second_lap_still_counts()
    {
        var car = Car(2_000, 15_000, 10_000, 10_200);

        Assert.Equal(new long[] { 15_000, 10_000, 10_200 }, RaceGaps.PaceLaps(car));
    }

    [Fact]
    public void The_columns_write_a_decimal_point_on_a_comma_decimal_pc()
    {
        var car = Car(2_000, 12_345, 12_100, 12_400, 12_900, 13_100, 13_456);
        car.FastestLapMs = 12_100;
        car.PenaltyMs = 2_500;
        car.SectorsAtLap.AddRange(new long[]?[] { null, null, null, null, null, null, new long[] { 6_120, 7_336 } });

        var was = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");

        try
        {
            Assert.Equal("13.456", Cell("last", car));
            Assert.Equal("12.100", Cell("best", car));
            Assert.Equal("+1.356", Cell("delta", car));
            Assert.Equal("+0.100", Cell("pace", car, fastestOverall: 12_000));
            Assert.Equal("12.716", Cell("avg", car));
            Assert.Equal("3.701", Cell("timelost", car));
            Assert.Equal("+2.5s", Cell("penalty", car));
            Assert.Equal("▼ 0.87", Cell("trend", car));
            Assert.Equal("6.12", Cell("s1", car));
            Assert.Equal("7.34", Cell("s2", car));
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }
}
