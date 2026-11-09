namespace HeatCaller.Tests;

/// <summary>
/// The short-track warning (9 Nov 2026): under 8 seconds a lap, the
/// announcer can't keep up, and the Session card and the Track Layouts
/// editor say so. See ShortTrack.
/// </summary>
public sealed class ShortTrackTests
{
    [Theory]
    [InlineData(5.8, true)]
    [InlineData(7.99, true)]
    [InlineData(8.0, false)]
    [InlineData(12.0, false)]
    [InlineData(0.0, false)]
    [InlineData(null, false)]
    public void Warns_under_eight_seconds_only(double? lap, bool warns)
    {
        Assert.Equal(warns, ShortTrack.Warning(lap) is not null);
    }

    [Fact]
    public void The_warning_names_the_lap_and_says_timing_is_fine()
    {
        var warning = ShortTrack.Warning(5.8)!;

        Assert.Contains("5.8s", warning, StringComparison.Ordinal);
        Assert.Contains("Timing and results aren't affected", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void The_average_typed_is_used_when_nothing_has_been_raced()
    {
        var track = new TrackLayout { Name = "Kitchen", AverageLapSeconds = 6.0 };

        Assert.Equal(6.0, ShortTrack.LapFor(track, Array.Empty<SessionRecord>()));
    }

    [Fact]
    public void Real_laps_raced_beat_the_average_typed()
    {
        var track = new TrackLayout { Name = "Kitchen", AverageLapSeconds = 12.0 };
        var race = new SessionRecord
        {
            TrackLayoutName = "Kitchen",
            StartedAtUtc = DateTime.UtcNow,
            Results = new List<SessionResultEntry>
            {
                new() { LapTimesMs = Enumerable.Repeat(6000L, 12).ToList() },
            },
        };

        Assert.Equal(6.0, ShortTrack.LapFor(track, new[] { race }));
        Assert.NotNull(ShortTrack.Warning(ShortTrack.LapFor(track, new[] { race })));
    }

    [Fact]
    public void No_track_and_no_lap_give_no_warning()
    {
        Assert.Null(ShortTrack.LapFor(null, null));
        Assert.Null(ShortTrack.LapFor(new TrackLayout { Name = "Bare" }, null));
    }
}
