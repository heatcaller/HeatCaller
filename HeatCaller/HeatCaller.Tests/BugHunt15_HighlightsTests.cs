namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15, highlights: a warning given after a red flag lands in the
/// recording where it was called, not early by the length of the stoppage.
/// The race clock (RaceWarning.RaceTimeMs) stands still under a red; the lap
/// crossings and the recording do not, so the highlight mark uses the time
/// since the green with the red included.
/// </summary>
public sealed class BugHunt15_HighlightsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_warning_after_a_red_flag_keeps_the_time_since_the_green_with_the_red_in_it()
    {
        long nowMs = 1_000_000;
        var utc0 = new DateTime(2026, 11, 9, 19, 0, 0, DateTimeKind.Utc);
        var race = new RaceControlSession
        {
            NowMsSource = () => nowMs,
            UtcNowSource = () => utc0.AddMilliseconds(nowMs),
        };

        race.Stage();
        race.GreenFlag(0);

        nowMs += 5_000;
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = "A",
            DeviceTimeMs = 5_000,
            SignalHits = 1,
            SignalStrength = 100,
            IsSynthetic = true,
        });

        // Red at 3:00 for two minutes, then green again.
        nowMs += 175_000;
        race.RedFlag();
        nowMs += 120_000;
        race.GreenFlag(300_000);

        // Called at 6:00 on the wall, 4:00 on the race clock.
        nowMs += 60_000;
        var warning = race.IssueWarning("A", "contact")!;

        Assert.Equal(240_000L, warning.RaceTimeMs);
        Assert.Equal(360_000L, warning.SinceGreenMs);
    }

    [Fact]
    public void A_warning_mark_is_placed_on_the_same_clock_as_the_crossings()
    {
        var record = new SessionRecord
        {
            SessionType = RaceSessionType.Race,
            Label = "A Main",
            StartedAtUtc = new DateTime(2026, 11, 9, 19, 0, 0, DateTimeKind.Utc),
            EndedAtUtc = new DateTime(2026, 11, 9, 19, 10, 0, DateTimeKind.Utc),
            Results = new List<SessionResultEntry>
            {
                new()
                {
                    Position = 1,
                    Transponder = "7",
                    DriverName = "Seven",
                    Laps = 2,
                    LapTimesMs = new List<long> { 300_000, 100_000 },
                },
            },
        };

        record.Results[0].Warnings.Add(new RaceWarning
        {
            RaceTimeMs = 240_000,
            SinceGreenMs = 360_000,
            Number = 1,
            Reason = "Blocking",
        });

        var mark = Assert.Single(RaceHighlights.MarksFrom(record), m => m.What == RaceHighlights.Moment.Warning);

        Assert.Equal(360_000L, mark.RaceTimeMs);
    }

    [Fact]
    public void A_warning_saved_before_the_time_since_the_green_was_kept_still_uses_its_race_time()
    {
        var record = new SessionRecord
        {
            SessionType = RaceSessionType.Race,
            Label = "A Main",
            StartedAtUtc = new DateTime(2026, 11, 9, 19, 0, 0, DateTimeKind.Utc),
            EndedAtUtc = new DateTime(2026, 11, 9, 19, 10, 0, DateTimeKind.Utc),
            Results = new List<SessionResultEntry>
            {
                new()
                {
                    Position = 1,
                    Transponder = "7",
                    DriverName = "Seven",
                    Laps = 1,
                    LapTimesMs = new List<long> { 30_000 },
                },
            },
        };

        record.Results[0].Warnings.Add(new RaceWarning { RaceTimeMs = 14_500, Number = 1, Reason = "Blocking" });

        var mark = Assert.Single(RaceHighlights.MarksFrom(record), m => m.What == RaceHighlights.Moment.Warning);

        Assert.Equal(14_500L, mark.RaceTimeMs);
    }
}
