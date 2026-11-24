namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24: when two of a driver's transponders were timed as separate
/// cars in one session, Trends took the first row with their name (the
/// higher finisher) rather than their quickest lap.
/// </summary>
public class BugHunt24_TrendChartControlTests
{
    [Fact]
    public void Trends_TwoRowsUnderOneDriver_TakesTheQuickerLap()
    {
        var session = new SessionRecord
        {
            Results =
            {
                new SessionResultEntry { Transponder = "100", DriverName = "Sam", Position = 1, Laps = 12, FastLapMs = 21_000 },
                new SessionResultEntry { Transponder = "200", DriverName = "Sam", Position = 2, Laps = 11, FastLapMs = 20_000 },
            },
        };

        Assert.Equal(20_000, TrendChartControl.SessionBest(session, "Sam"));
    }

    [Fact]
    public void Trends_FirstRowWithoutALap_DoesNotDropTheSession()
    {
        var session = new SessionRecord
        {
            Results =
            {
                new SessionResultEntry { Transponder = "100", DriverName = "Sam", Position = 1, Laps = 0, FastLapMs = 0 },
                new SessionResultEntry { Transponder = "200", DriverName = "sam", Position = 2, Laps = 5, FastLapMs = 20_000 },
            },
        };

        Assert.Equal(20_000, TrendChartControl.SessionBest(session, "Sam"));
    }

    [Fact]
    public void Trends_GhostNamedAfterTheDriver_IsStillIgnored()
    {
        var session = new SessionRecord
        {
            Results =
            {
                new SessionResultEntry { Transponder = "GHOST-1", DriverName = "Sam", Position = 1, Laps = 12, FastLapMs = 15_000 },
                new SessionResultEntry { Transponder = "100", DriverName = "Sam", Position = 2, Laps = 11, FastLapMs = 21_000 },
                new SessionResultEntry { Transponder = "300", DriverName = "Alex", Position = 3, Laps = 11, FastLapMs = 19_000 },
            },
        };

        Assert.Equal(21_000, TrendChartControl.SessionBest(session, "Sam"));
        Assert.Null(TrendChartControl.SessionBest(session, "Jo"));
    }
}
