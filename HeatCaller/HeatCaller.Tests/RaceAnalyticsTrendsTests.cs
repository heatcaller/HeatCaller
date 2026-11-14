namespace HeatCaller.Tests;

/// <summary>
/// The Trends tab of Race Analytics after the Race Engineer rework (13 Nov
/// 2026): the reading of a driver's sessions on one track (RaceCoach.Trends)
/// - still improving and how fast now against earlier, the curve, and where
/// on it they are - and the page that shows it.
/// </summary>
public sealed class RaceAnalyticsTrendsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly DateTime Start = new(2026, 8, 15, 19, 0, 0);

    /// <summary>A session a week, from 15 Aug, at these best laps.</summary>
    private static List<RaceCoach.TrendSample> Weekly(params long[] laps) =>
        laps.Select((ms, i) => new RaceCoach.TrendSample(Start.AddDays(7 * i), ms)).ToList();

    [Fact]
    public void Big_early_drops_then_small_ones_read_as_still_improving_but_slower()
    {
        var reading = RaceCoach.ReadTrend(Weekly(14900, 14760, 14650, 14560, 14490, 14440, 14400, 14370, 14330, 14300, 14270, 14240));

        Assert.Equal(12, reading.Sessions);
        Assert.True(reading.EarlyRate > reading.RecentRate);
        Assert.True(reading.RecentRate > 0);
        Assert.Equal(RaceCoach.CurveStage.Refining, reading.Stage);
        Assert.StartsWith("Still improving, but slower: 0.", RaceCoach.TrendHeadline(reading));
        Assert.Contains("in August", RaceCoach.TrendHeadline(reading));
        Assert.Contains("a week now", RaceCoach.TrendHeadline(reading));
        Assert.DoesNotContain("—", RaceCoach.TrendHeadline(reading));
        Assert.Contains("setup change", RaceCoach.TrendDrill(reading));
    }

    [Fact]
    public void A_steady_tenth_a_week_is_learning()
    {
        var reading = RaceCoach.ReadTrend(Weekly(15000, 14900, 14800, 14700, 14600, 14500, 14400, 14300));

        Assert.Equal(RaceCoach.CurveStage.Learning, reading.Stage);
        Assert.Equal(0.1, reading.RecentRate!.Value, 3);
        Assert.Equal("Still improving at a steady 0.10 s a week. Laps are still the quickest way to find time.", RaceCoach.TrendHeadline(reading));
    }

    [Fact]
    public void Flat_with_no_new_best_for_three_sessions_is_a_plateau()
    {
        var reading = RaceCoach.ReadTrend(Weekly(14900, 14700, 14500, 14320, 14300, 14330, 14310, 14320));

        Assert.Equal(3, reading.SessionsSinceBest);
        Assert.Equal(RaceCoach.CurveStage.Plateau, reading.Stage);
        Assert.StartsWith("Levelled off:", RaceCoach.TrendHeadline(reading));
        Assert.Contains("No new best in 3 sessions.", RaceCoach.TrendHeadline(reading));
    }

    [Fact]
    public void Flat_but_with_a_new_best_just_now_is_still_refining()
    {
        var reading = RaceCoach.ReadTrend(Weekly(14900, 14700, 14500, 14320, 14330, 14320, 14325, 14310));

        Assert.Equal(0, reading.SessionsSinceBest);
        Assert.Equal(RaceCoach.CurveStage.Refining, reading.Stage);
    }

    [Fact]
    public void Getting_slower_says_so_and_points_at_the_car()
    {
        var reading = RaceCoach.ReadTrend(Weekly(14500, 14400, 14300, 14350, 14450, 14550));

        Assert.StartsWith("Slower lately:", RaceCoach.TrendHeadline(reading));
        Assert.StartsWith("Check the car", RaceCoach.TrendDrill(reading));
    }

    [Fact]
    public void Too_few_sessions_are_not_called_a_trend()
    {
        var reading = RaceCoach.ReadTrend(Weekly(14900, 14700, 14600));

        Assert.Null(reading.Stage);
        Assert.Null(reading.Fit);
        Assert.Equal("Only 3 sessions here so far. It takes 4 to see which way you're heading.", RaceCoach.TrendHeadline(reading));
        Assert.StartsWith("Race this track a few more nights", RaceCoach.TrendDrill(reading));
    }

    [Fact]
    public void Sessions_all_in_one_week_have_no_weekly_rate()
    {
        var samples = Enumerable.Range(0, 5).Select(i => new RaceCoach.TrendSample(Start.AddDays(i), 14500 - i * 20)).ToList();
        var reading = RaceCoach.ReadTrend(samples);

        Assert.Null(reading.RecentRate);
        Assert.Null(reading.Stage);
        Assert.StartsWith("These sessions are all within a week", RaceCoach.TrendHeadline(reading));
    }

    [Fact]
    public void The_curve_follows_the_points_and_flattens()
    {
        var samples = Weekly(14900, 14760, 14650, 14560, 14490, 14440, 14410, 14390, 14370, 14350, 14335, 14320);
        var fit = RaceCoach.FitCurve(samples)!;

        // Within a tenth of every point, and falling less each week.
        Assert.All(samples, p => Assert.InRange(fit.At(p.When), p.BestLapMs - 100, p.BestLapMs + 100));

        var firstWeek = fit.At(Start) - fit.At(Start.AddDays(7));
        var lastWeek = fit.At(Start.AddDays(70)) - fit.At(Start.AddDays(77));

        Assert.True(firstWeek > lastWeek && lastWeek > 0);
    }

    [Fact]
    public void Order_does_not_matter_and_empty_laps_are_left_out()
    {
        var samples = Weekly(14900, 14760, 14650, 14560, 14490, 14440);
        var shuffled = samples.AsEnumerable().Reverse().Append(new RaceCoach.TrendSample(Start.AddDays(3), 0)).ToList();

        Assert.Equal(RaceCoach.ReadTrend(samples), RaceCoach.ReadTrend(shuffled) with { Fit = RaceCoach.ReadTrend(samples).Fit });
    }

    [Fact]
    public void The_page_reads_one_track_and_says_the_gap_to_the_record_in_car_lengths() => StaThread.Run(() =>
    {
        var layouts = RaceAnalyticsSamples.Layouts();
        using var trend = new TrendChartControl(RaceAnalyticsSamples.History(), new AppSettings()) { Layouts = layouts };

        trend.Pick("Sam Okafor", "Club Infield");

        Assert.StartsWith("Still improving", trend.Headline);
        Assert.NotNull(trend.Stage);
        Assert.Contains("Track record 13.842 by Priya Shah", trend.RecordLine);
        Assert.Contains("car lengths a lap", trend.RecordLine);
        Assert.NotEqual(string.Empty, trend.Advice);

        // Without a length for the layout, the gap in seconds alone.
        trend.Layouts = null;
        trend.Pick("Sam Okafor", "Outer Loop");
        trend.Pick("Sam Okafor", "Club Infield");

        Assert.DoesNotContain("car length", trend.RecordLine);
        Assert.Contains("s off it.", trend.RecordLine);
        return true;
    }, TimeSpan.FromMinutes(1), () => "building the Trends page");

    [Fact]
    public void All_tracks_is_not_read_as_a_trend() => StaThread.Run(() =>
    {
        using var trend = new TrendChartControl(RaceAnalyticsSamples.History(), new AppSettings());

        trend.Pick("Sam Okafor", null);

        Assert.StartsWith("Across all tracks", trend.Headline);
        Assert.Null(trend.Stage);
        Assert.Equal(string.Empty, trend.RecordLine);
        Assert.StartsWith("Pick the track", trend.Advice);

        // And nobody picked asks for somebody.
        trend.Pick("(select a driver)", null);

        Assert.StartsWith("Pick a driver", trend.Headline);
        Assert.Equal(string.Empty, trend.Advice);
        return true;
    }, TimeSpan.FromMinutes(1), () => "building the Trends page");

    [Fact]
    public void A_track_with_few_sessions_says_so_honestly() => StaThread.Run(() =>
    {
        using var trend = new TrendChartControl(RaceAnalyticsSamples.History(), new AppSettings());

        trend.Pick("Sam Okafor", "Outer Loop");

        Assert.StartsWith("Only 2 sessions here so far.", trend.Headline);
        Assert.Null(trend.Stage);
        return true;
    }, TimeSpan.FromMinutes(1), () => "building the Trends page");
}

/// <summary>
/// A club's season for the Race Analytics tests: Sam Okafor's twelve
/// sessions on Club Infield over three months, two on Outer Loop, and the
/// A-Main of 7 Nov - twenty laps, five drivers, Sam with a crash on lap 9
/// and a fade at the end. Club Infield is 600 feet round.
/// </summary>
internal static class RaceAnalyticsSamples
{
    public const string Track = "Club Infield";

    private static readonly DateTime First = new(2026, 8, 15, 9, 0, 0, DateTimeKind.Utc);

    public static TrackLayoutStore Layouts()
    {
        var layouts = TrackLayoutStore.Load();

        layouts.Upsert(new TrackLayout { Name = Track, TrackLengthFeet = 600 });
        return layouts;
    }

    public static SessionHistoryStore History()
    {
        var sessions = new List<SessionRecord>();

        for (var i = 0; i < 12; i++)
        {
            // Quick at first, then a little at a time, with a night's noise.
            var best = (long)(14860 - 140 * Math.Log(1 + i * 1.8)) + (i % 3 == 1 ? 30 : 0);

            sessions.Add(Session(First.AddDays(i * 7 + (i > 6 ? 1 : 0)), Track, RaceSessionType.Race,
                Entry(1, "1001", "Sam Okafor", best),
                Entry(2, "1002", "Leo Martins", best + 120)));
        }

        // The record, set by Priya on 12 Oct.
        sessions.Add(Session(new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc), Track, RaceSessionType.Race,
            Entry(1, "1003", "Priya Shah", 13842)));

        sessions.Add(Session(First.AddDays(3), "Outer Loop", RaceSessionType.Practice, Entry(1, "1001", "Sam Okafor", 17900)));
        sessions.Add(Session(First.AddDays(40), "Outer Loop", RaceSessionType.Practice, Entry(1, "1001", "Sam Okafor", 17510)));

        sessions.Add(Race());

        // Newest first, as the history keeps them.
        return SessionHistoryStore.FromSessions(sessions.OrderByDescending(s => s.StartedAtUtc));
    }

    /// <summary>The A-Main of 7 Nov: twenty laps each after the start, finishing order by total time.</summary>
    public static SessionRecord Race()
    {
        long Noise(int lap, int driver) => ((lap * 37 + driver * 11) % 9 - 4) * 20;

        var drivers = new (string Name, string Transponder, Func<int, long> Lap)[]
        {
            ("Priya Shah", "1003", i => 14200 + Noise(i, 1)),
            ("Sam Okafor", "1001", i => 14390 + Math.Abs(Noise(i, 2)) + (i == 8 ? 1740 : 0) + (i >= 14 ? 100 * (i - 13) / 2 : 0)),
            ("Dana Kowalski", "1004", i => 14700 - 15 * i + Noise(i, 3) / 2),
            ("Leo Martins", "1002", i => 14400 + Noise(i, 4) + (i is 3 or 6 or 10 or 13 or 16 or 18 ? 600 : 0)),
            ("Kim Lee", "1005", i => 15000 + Noise(i, 5) * 10),
        };

        var entries = drivers.Select(d =>
        {
            var laps = new List<long> { 2600 };
            laps.AddRange(Enumerable.Range(0, 20).Select(d.Lap));

            return new SessionResultEntry
            {
                DriverName = d.Name,
                Transponder = d.Transponder,
                Laps = 20,
                LapTimesMs = laps,
                FastLapMs = laps.Skip(1).Min(),
                ElapsedMs = laps.Sum(),
            };
        }).OrderBy(e => e.ElapsedMs).ToList();

        for (var i = 0; i < entries.Count; i++)
        {
            entries[i].Position = i + 1;
        }

        return new SessionRecord
        {
            SessionType = RaceSessionType.Race,
            Label = "A-Main",
            TrackLayoutName = Track,
            StartedAtUtc = new DateTime(2026, 11, 7, 20, 0, 0, DateTimeKind.Utc),
            Results = entries,
        };
    }

    private static SessionRecord Session(DateTime when, string track, RaceSessionType type, params SessionResultEntry[] results) => new()
    {
        SessionType = type,
        Label = type == RaceSessionType.Race ? "A-Main" : "Practice",
        TrackLayoutName = track,
        StartedAtUtc = when,
        Results = results.ToList(),
    };

    private static SessionResultEntry Entry(int position, string transponder, string name, long best) => new()
    {
        Position = position,
        Transponder = transponder,
        DriverName = name,
        Laps = 10,
        FastLapMs = best,
        LapTimesMs = new List<long> { 2000 }.Concat(Enumerable.Range(0, 10).Select(i => best + (i % 4) * 90)).ToList(),
    };
}
