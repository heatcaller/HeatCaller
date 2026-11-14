namespace HeatCaller.Tests;

/// <summary>
/// The Pace tab of Race Analytics after the Race Engineer rework (13 Nov
/// 2026): whose race is read, its headline and result, the three gauges with
/// their zones, everyone's verdict, and the full table kept underneath - on
/// the tab, and in the window opened on one race (PaceAnalysisForm).
/// </summary>
public sealed class RaceAnalyticsPaceTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static T OnSta<T>(Func<T> work) => StaThread.Run(work, TimeSpan.FromMinutes(1), () => "building the Pace page");

    [Fact]
    public void The_winners_race_is_read_first() => OnSta(() =>
    {
        using var pace = new PaceAnalysisControl(RaceAnalyticsSamples.History());

        Assert.Equal("Priya Shah", pace.DriverShowing);
        Assert.Contains("Won it,", pace.Headline);
        Assert.Contains("s clear of", pace.Headline);
        Assert.Equal(5, pace.TableRows);
        Assert.Equal(5, pace.VerdictDrivers.Count);
        Assert.NotEqual(string.Empty, pace.Advice);
        return true;
    });

    [Fact]
    public void A_crash_lap_is_the_story_and_the_gauges_read_the_race() => OnSta(() =>
    {
        using var pace = new PaceAnalysisControl(RaceAnalyticsSamples.History()) { Layouts = RaceAnalyticsSamples.Layouts() };

        pace.RefreshAll();
        pace.PickDriver("Sam Okafor");

        Assert.Contains("mistake (lap 10)", pace.Headline); // the chart and leaderboard number: the start pass is lap 1
        Assert.Contains("P3 of 5", pace.Headline);
        Assert.Contains("behind Priya Shah", pace.Headline);

        var (consistency, table, fade) = (pace.Gauges[0], pace.Gauges[1], pace.Gauges[2]);

        Assert.Equal("Consistency", consistency.Caption);
        Assert.NotNull(consistency.Value);
        Assert.Contains("Lap 9 pushed it up", consistency.Reading);

        // The layout has a length, so a typical lap's loss is in car lengths.
        Assert.Contains("car length", table.Reading);
        Assert.EndsWith("s", table.Figure);

        Assert.StartsWith("+", fade.Figure);
        Assert.Contains("slower in the last third", fade.Reading);

        Assert.Equal(RaceCoach.Drill(PaceAnalysis.For(Sam()), RaceCoach.Laps(Sam())), pace.Advice);
        return true;
    });

    [Fact]
    public void Everyones_verdict_is_by_the_pace_they_could_hold() => OnSta(() =>
    {
        using var pace = new PaceAnalysisControl(RaceAnalyticsSamples.History());

        var rows = PaceAnalysis.For(RaceAnalyticsSamples.Race())
            .OrderBy(r => r.BestConsecutiveAverageMs)
            .Select(r => r.DriverName)
            .ToList();

        Assert.Equal(rows, pace.VerdictDrivers);
        return true;
    });

    [Fact]
    public void Without_a_layout_length_there_are_no_car_lengths() => OnSta(() =>
    {
        using var pace = new PaceAnalysisControl(RaceAnalyticsSamples.History());

        pace.PickDriver("Sam Okafor");

        Assert.DoesNotContain("car length", pace.Gauges[1].Reading);
        return true;
    });

    [Fact]
    public void The_window_on_one_race_still_reads_it() => OnSta(() =>
    {
        using var form = new PaceAnalysisForm(RaceAnalyticsSamples.Race());
        var pace = form.Controls.OfType<FillPage>().Single().Controls.Cast<Control>()
            .SelectMany(Descendants).OfType<PaceAnalysisControl>().Single();

        Assert.Equal("Priya Shah", pace.DriverShowing);
        Assert.Contains("Won it", pace.Headline);
        Assert.Equal(5, pace.TableRows);
        return true;
    });

    [Fact]
    public void A_session_with_no_lap_times_says_why() => OnSta(() =>
    {
        var race = RaceAnalyticsSamples.Race();

        foreach (var entry in race.Results)
        {
            entry.LapTimesMs.Clear();
        }

        using var pace = new PaceAnalysisControl(race);

        Assert.StartsWith("This session was recorded before lap-by-lap times were kept", pace.Headline);
        Assert.Equal("-", pace.Gauges[0].Figure);
        Assert.Null(pace.Gauges[0].Value);
        Assert.Equal(string.Empty, pace.Advice);
        return true;
    });

    [Theory]
    [InlineData(1.0, "at your limit")]
    [InlineData(2.6, "normal")]
    [InlineData(5.0, "scrappy")]
    [InlineData(9.0, "scrappy")]
    public void A_figure_falls_in_its_zone(double value, string zone) => OnSta(() =>
    {
        using var gauge = new ZoneGauge();

        gauge.Configure("Consistency", PaceAnalysisControl.ConsistencyZones);
        gauge.Show(value, $"{value}%", string.Empty);

        Assert.Equal(zone, gauge.Zone!.Label);
        return true;
    });

    [Fact]
    public void Each_zone_takes_an_equal_share_of_the_bar()
    {
        var zones = PaceAnalysisControl.ConsistencyZones;

        Assert.Equal(0f, ZoneGauge.Position(0, zones));
        Assert.Equal(1 / 6f, ZoneGauge.Position(0.75, zones), 3);
        Assert.Equal(1 / 3f, ZoneGauge.Position(1.5, zones), 3);
        Assert.Equal(0.5f, ZoneGauge.Position(2.75, zones), 3);
        Assert.Equal(1f, ZoneGauge.Position(20, zones));
    }

    [Fact]
    public void The_readings_say_the_zone_in_plain_words()
    {
        var tidy = PaceAnalysis.For(Driver(14000, 14050, 14020, 14010, 14030, 14040));

        Assert.StartsWith("At your limit.", PaceAnalysisControl.ConsistencyReading(tidy, RaceCoach.Laps(Driver(14000, 14050, 14020, 14010, 14030, 14040))));
        Assert.StartsWith("Tidy.", PaceAnalysisControl.TableReading(tidy, null));
        Assert.Equal("Held pace from start to finish.", PaceAnalysisControl.FadeReading(tidy));
        Assert.Contains("under a car length", PaceAnalysisControl.TableReading(tidy, 600));

        var all = new[] { "At your limit.", "Normal.", "Scrappy.", "Tidy.", "Some to find.", "Lots." };
        Assert.All(all, s => Assert.DoesNotContain("—", s));
    }

    [Fact]
    public void The_result_clause_counts_laps_down()
    {
        var race = RaceAnalyticsSamples.Race();
        var last = race.Results.Single(r => r.Position == 5);

        last.Laps = 18;

        Assert.Equal("P5 of 5, 2 laps down on Priya Shah.", PaceAnalysisControl.ResultClause(race, last));

        race.SessionType = RaceSessionType.Practice;
        Assert.Equal(string.Empty, PaceAnalysisControl.ResultClause(race, last));
    }

    private static SessionResultEntry Sam() => RaceAnalyticsSamples.Race().Results.Single(r => r.DriverName == "Sam Okafor");

    private static SessionResultEntry Driver(params long[] laps) => new()
    {
        DriverName = "Alex",
        Transponder = "2001",
        Position = 1,
        Laps = laps.Length,
        LapTimesMs = new List<long> { 2000 }.Concat(laps).ToList(),
    };

    private static IEnumerable<Control> Descendants(Control control) =>
        new[] { control }.Concat(control.Controls.Cast<Control>().SelectMany(Descendants));
}
