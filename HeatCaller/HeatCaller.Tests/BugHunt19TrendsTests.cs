namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 19, two Trends tab faults.
///
/// #7: the trend was read from the earlier and later halves of the
/// sessions, and a half inside one week has no weekly rate. Eight heats over
/// two nights a week apart put all of last night in the recent half, so the
/// driver was told the sessions were "all within a week". A half is now
/// widened until it spans a week.
///
/// #34: the record line said "You hold the track record here" whenever the
/// chart's best was at or under the record, so a rental or excluded guest
/// quicker than the record was told they held it, and a driver who set the
/// record in practice, with practice unticked, was told they were off their
/// own record. Who holds it now comes from the record laps themselves.
/// </summary>
public sealed class BugHunt19TrendsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string Track = "Club Oval";

    private static readonly DateTime NightOne = new(2026, 10, 31, 19, 0, 0);

    [Fact]
    public void Two_nights_a_week_apart_with_four_heats_each_read_as_a_trend()
    {
        var samples = new List<RaceCoach.TrendSample>();
        for (var heat = 0; heat < 4; heat++)
        {
            samples.Add(new RaceCoach.TrendSample(NightOne.AddMinutes(30 * heat), 14800 - heat * 10));
            samples.Add(new RaceCoach.TrendSample(NightOne.AddDays(7).AddMinutes(30 * heat), 14600 - heat * 10));
        }

        var reading = RaceCoach.ReadTrend(samples);

        Assert.Equal(8, reading.Sessions);
        Assert.NotNull(reading.RecentRate);
        Assert.NotNull(reading.EarlyRate);
        Assert.NotNull(reading.Stage);
        Assert.True(reading.RecentSessions > 4);
        Assert.DoesNotContain("within a week", RaceCoach.TrendHeadline(reading));
        Assert.DoesNotContain("Four sessions is enough", RaceCoach.TrendDrill(reading));
    }

    [Fact]
    public void Sessions_really_all_in_one_week_still_have_no_rate()
    {
        var samples = Enumerable.Range(0, 6).Select(i => new RaceCoach.TrendSample(NightOne.AddDays(i), 14500 - i * 20)).ToList();
        var reading = RaceCoach.ReadTrend(samples);

        Assert.Null(reading.RecentRate);
        Assert.StartsWith("These sessions are all within a week", RaceCoach.TrendHeadline(reading));
    }

    private static SessionResultEntry Row(string transponder, string name, long best) => new()
    {
        Transponder = transponder,
        DriverName = name,
        FastLapMs = best,
    };

    private static SessionRecord Session(RaceSessionType type, params SessionResultEntry[] results) => new()
    {
        SessionType = type,
        Label = "Heat 1",
        TrackLayoutName = Track,
        StartedAtUtc = new DateTime(2026, 11, 7, 20, 0, 0, DateTimeKind.Utc),
        Results = results.ToList(),
    };

    private static ProfileStore Profiles()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "SAM", DriverName = "Sam" });
        profiles.Upsert(new DriverProfile { Transponder = "RENT", DriverName = "Rental One", IsRental = true });
        profiles.Upsert(new DriverProfile { Transponder = "GUEST", DriverName = "Guest", ExcludeFromRecords = true });
        return profiles;
    }

    [Theory]
    [InlineData("RENT", "Rental One")]
    [InlineData("GUEST", "Guest")]
    public void A_driver_whose_laps_cant_be_records_is_not_told_they_hold_it(string transponder, string name)
    {
        var sessions = new[] { Session(RaceSessionType.Race, Row("SAM", "Sam", 12300), Row(transponder, name, 11800)) };

        var line = TrendChartControl.RecordText(sessions, Track, name, 11800, Profiles(), null);

        Assert.DoesNotContain("You hold", line);
        Assert.Contains("by Sam", line);
        Assert.Contains("doesn't count", line);
    }

    [Fact]
    public void A_record_set_in_practice_is_still_the_drivers_own_with_practice_unticked()
    {
        var sessions = new[]
        {
            Session(RaceSessionType.Practice, Row("SAM", "Sam", 12100)),
            Session(RaceSessionType.Race, Row("SAM", "Sam", 12300)),
        };

        // With practice unticked the chart's best is the race's 12.300.
        var line = TrendChartControl.RecordText(sessions, Track, "Sam", 12300, Profiles(), null);

        Assert.StartsWith("You hold the track record here", line);
    }

    [Fact]
    public void A_driver_off_someone_elses_record_is_told_the_gap()
    {
        var sessions = new[] { Session(RaceSessionType.Race, Row("SAM", "Sam", 12300), Row("PAT", "Pat", 12500)) };

        var line = TrendChartControl.RecordText(sessions, Track, "Pat", 12500, Profiles(), null);

        Assert.Contains("by Sam", line);
        Assert.Contains("0.200 s off it", line);
    }
}
