using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>
/// The Lap Times tab as the race engineer (13 Nov 2026, the Race Analytics
/// rework, option C): where a fade starts, the run at the driver's limit,
/// the headline and the three things in the race (RaceCoach.LapTimes.cs),
/// and the page showing them for the focused driver with the chart marking
/// the best lap, the mistakes and the fade.
/// </summary>
public sealed class RaceAnalyticsLapTimesTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>
    /// Sam's A-Main: quick through the middle, a crash on the ninth counted
    /// lap (lap 10 on the chart, which counts the start pass as lap 1), and
    /// slower by about two tenths a lap from the sixteenth.
    /// </summary>
    internal static readonly double[] Sam =
    {
        14.62, 14.40, 14.25, 14.18, 14.15, 14.12, 14.105, 14.13, 15.85, 14.30,
        14.20, 14.18, 14.22, 14.25, 14.20, 14.38, 14.42, 14.45, 14.47, 14.50,
    };

    private static SessionResultEntry Entry(string name, int position, IEnumerable<double> seconds, long start = 3_000)
    {
        var entry = new SessionResultEntry { Transponder = (100 + position).ToString(), DriverName = name, Position = position };
        entry.LapTimesMs.Add(start);
        entry.LapTimesMs.AddRange(seconds.Select(s => (long)Math.Round(s * 1000)));
        entry.Laps = entry.LapTimesMs.Count;
        return entry;
    }

    private static List<long> Ms(params double[] seconds) => seconds.Select(s => (long)Math.Round(s * 1000)).ToList();

    // --- The rules ---

    [Fact]
    public void A_fade_starts_where_the_pace_drops_and_stays_down()
    {
        var laps = Ms(14.10, 14.12, 14.08, 14.11, 14.09, 14.10, 14.13, 14.10, 14.40, 14.42, 14.45, 14.41, 14.44);

        var fade = RaceCoach.FadeOf(laps);

        Assert.NotNull(fade);
        Assert.Equal(8, fade!.Start);
        Assert.InRange(fade.PerLapMs, 300, 350);
        Assert.InRange(fade.LostMs, 1500, 1750);
    }

    [Fact]
    public void An_even_race_and_one_that_gets_quicker_have_no_fade()
    {
        Assert.Null(RaceCoach.FadeOf(Ms(14.10, 14.12, 14.08, 14.11, 14.09, 14.10, 14.13, 14.10, 14.12, 14.09)));
        Assert.Null(RaceCoach.FadeOf(Ms(14.60, 14.50, 14.45, 14.40, 14.30, 14.20, 14.15, 14.10, 14.10, 14.08)));
    }

    [Fact]
    public void One_crash_late_on_is_a_mistake_not_a_fade()
    {
        var laps = Ms(14.10, 14.12, 14.08, 14.11, 14.09, 14.10, 14.13, 14.10, 14.12, 16.40, 14.11);

        Assert.Equal(new[] { 9 }, RaceCoach.MistakeLaps(laps));
        Assert.Null(RaceCoach.FadeOf(laps));
    }

    [Fact]
    public void Too_few_laps_have_no_fade() =>
        Assert.Null(RaceCoach.FadeOf(Ms(14.10, 14.12, 14.40, 14.45, 14.50)));

    [Fact]
    public void The_limit_run_is_the_longest_stretch_within_one_and_a_half_percent_of_the_best()
    {
        // Best 14.00, so the limit is 14.21: laps 1-3 and 5-8 (zero-based), the longer wins.
        var laps = Ms(14.50, 14.10, 14.00, 14.20, 14.40, 14.05, 14.15, 14.10, 14.20, 14.60);

        Assert.Equal((5, 8), RaceCoach.LimitRun(laps));
        Assert.Null(RaceCoach.LimitRun(Ms(14.00, 14.50, 14.10, 14.60, 14.05)));
    }

    [Fact]
    public void The_best_three_in_a_row_is_found_by_their_average()
    {
        var three = RaceCoach.BestThree(Ms(14.50, 14.10, 14.00, 14.20, 14.40, 14.05));

        Assert.Equal((1, 14_100L), three);
    }

    [Fact]
    public void Lap_numbers_are_the_chart_s_and_skip_a_credited_start()
    {
        var plain = Entry("Sam", 1, new[] { 14.1, 14.2 });
        Assert.Equal(new[] { 2, 3 }, RaceCoach.LapNumbers(plain));

        var credited = Entry("Sam", 1, new[] { 2.5, 14.1, 14.2 }, start: 0);
        credited.StartLapCredited = true;
        Assert.Equal(new[] { 3, 4 }, RaceCoach.LapNumbers(credited));
        Assert.Equal(new long[] { 14_100, 14_200 }, RaceCoach.Laps(credited));
    }

    [Fact]
    public void The_headline_says_where_they_were_quick_the_mistake_and_the_fade()
    {
        var entry = Entry("Sam", 2, Sam);

        Assert.Equal(
            "Quick through the middle, one big mistake, then you faded by about two tenths a lap.",
            RaceCoach.LapTimesHeadline(PaceAnalysis.For(entry), RaceCoach.Laps(entry)));
    }

    [Fact]
    public void A_clean_even_race_says_so()
    {
        var entry = Entry("Priya", 1, new[] { 14.00, 13.95, 13.90, 13.92, 13.94, 13.91, 13.93, 13.95, 13.92, 13.96 });

        Assert.Equal(
            "Quick through the middle, no big mistakes, and you held your pace to the end.",
            RaceCoach.LapTimesHeadline(PaceAnalysis.For(entry), RaceCoach.Laps(entry)));
    }

    [Fact]
    public void Three_things_are_the_limit_run_the_mistake_against_the_fade_and_the_last_laps()
    {
        var entry = Entry("Sam", 2, Sam);
        var things = RaceCoach.ThreeThings(RaceCoach.Laps(entry), RaceCoach.LapNumbers(entry), 400);

        Assert.Equal(3, things.Count);

        Assert.Equal("Laps 4 to 9 were all at your limit", things[0].Line);
        Assert.StartsWith("6 laps in a row within 1.5% of your best.", things[0].Detail);
        Assert.EndsWith("Best three in a row: 14.118.", things[0].Detail);

        // 15.850 - 14.105 = 1.745 s; of a 14.105 s lap 400 cars long (600 ft of 1/10), 49.5 lengths.
        Assert.Equal("Lap 10 alone cost 1.75 s, 49 car lengths", things[1].Line);
        Assert.Contains("the fade lost over laps 17 to 21", things[1].Detail);

        Assert.Equal("Your last 5 laps averaged 14.444", things[2].Line);
        Assert.Contains("a lap slower than the laps before them", things[2].Detail);
        Assert.Contains("car length", things[2].Detail);
    }

    [Fact]
    public void Without_a_layout_length_there_are_no_car_lengths()
    {
        var entry = Entry("Sam", 2, Sam);
        var things = RaceCoach.ThreeThings(RaceCoach.Laps(entry), RaceCoach.LapNumbers(entry), null);

        Assert.Equal("Lap 10 alone cost 1.75 s", things[1].Line);
        Assert.DoesNotContain(things, t => t.Line.Contains("car length") || t.Detail.Contains("car length"));
    }

    [Fact]
    public void A_race_too_short_to_read_has_no_findings()
    {
        var entry = Entry("Sam", 1, new[] { 14.1, 14.2 });

        Assert.Empty(RaceCoach.ThreeThings(RaceCoach.Laps(entry), RaceCoach.LapNumbers(entry), 400));
    }

    [Fact]
    public void Nothing_the_reading_says_has_an_em_dash()
    {
        var entry = Entry("Sam", 2, Sam);
        var laps = RaceCoach.Laps(entry);
        var said = RaceCoach.ThreeThings(laps, RaceCoach.LapNumbers(entry), 400)
            .SelectMany(t => new[] { t.Line, t.Detail })
            .Append(RaceCoach.LapTimesHeadline(PaceAnalysis.For(entry), laps));

        Assert.DoesNotContain(said, s => s.Contains('—') || s.Contains('–'));
    }

    // --- The page ---

    internal static SessionRecord Race() => new()
    {
        Id = "a-main",
        SessionType = RaceSessionType.Race,
        StartedAtUtc = new DateTime(2026, 11, 7, 20, 0, 0, DateTimeKind.Utc),
        EndedAtUtc = new DateTime(2026, 11, 7, 20, 6, 0, DateTimeKind.Utc),
        TrackLayoutName = "Club Infield",
        Label = "A-Main",
        Results =
        {
            Entry("Priya Shah", 1, new[] { 14.10, 13.98, 13.92, 13.90, 13.88, 13.86, 13.89, 13.91, 13.87, 13.90, 13.92, 13.89, 13.90, 13.95, 13.93, 13.91, 13.94, 13.96, 13.92, 13.95 }),
            Entry("Sam Okafor", 2, Sam),
            Entry("Leo Martins", 3, new[] { 14.30, 14.15, 14.90, 14.12, 14.70, 14.20, 14.18, 14.85, 14.25, 14.16, 14.75, 14.22, 14.19, 14.80, 14.24, 14.21, 14.30, 14.26, 14.95 }),
            Entry("Dana Kowalski", 4, new[] { 14.90, 14.80, 14.75, 14.70, 14.66, 14.62, 14.60, 14.58, 14.55, 14.55, 14.52, 14.50, 14.48, 14.50, 14.46, 14.45, 14.44, 14.42 }),
            Entry("Jo Park", 5, new[] { 15.20, 15.05, 14.98, 15.10, 14.95, 15.00, 14.92, 15.30, 14.96, 14.99, 15.02, 14.94, 15.05, 14.97, 15.01, 15.08, 15.00 }),
        },
    };

    internal static TrackLayoutStore Layouts()
    {
        var store = TrackLayoutStore.Load();
        store.Upsert(new TrackLayout { Name = "Club Infield", TrackLengthFeet = 600 });
        return store;
    }

    [Fact]
    public void The_page_reads_the_race_of_the_driver_it_focuses_on()
    {
        var history = SessionHistoryStore.FromSessions(new[] { Race() });
        var layouts = Layouts();

        var (first, focused) = StaThread.Run(() =>
        {
            using var page = new LapChartControl(history, new AppSettings()) { Layouts = layouts };
            _ = page.Handle;
            StaThread.Settle();

            var first = (page.HeadlineText, page.ThingsShown.Count, page.DrillText);

            page.FocusOn("Sam Okafor");
            StaThread.Settle();

            return (first, (page.HeadlineText, page.ThingsShown, page.DrillText));
        }, TimeSpan.FromSeconds(60), () => "Lap Times reading a race");

        // Priya, the winner, is first ticked and so read first.
        Assert.Equal("Quick through the middle, no big mistakes, and you held your pace to the end.", first.HeadlineText);
        Assert.Equal(3, first.Count);
        Assert.NotEmpty(first.DrillText);

        Assert.Equal("Quick through the middle, one big mistake, then you faded by about two tenths a lap.", focused.HeadlineText);
        Assert.Equal("2. Lap 10 alone cost 1.75 s, 49 car lengths", focused.ThingsShown[1].Line);
        Assert.StartsWith("1. ", focused.ThingsShown[0].Line);
        Assert.Equal(RaceCoach.Drill(PaceAnalysis.For(Race().Results[1]), RaceCoach.Laps(Race().Results[1])), focused.DrillText);
    }

    [Fact]
    public void The_chart_marks_the_best_lap_the_mistake_and_the_fade_with_labels_that_fit_and_do_not_overlap()
    {
        var history = SessionHistoryStore.FromSessions(new[] { Race() });
        var layouts = Layouts();
        var bounds = new Rectangle(0, 0, 640, 320);

        var marks = StaThread.Run(() =>
        {
            using var page = new LapChartControl(history, new AppSettings()) { Layouts = layouts };
            _ = page.Handle;
            StaThread.Settle();
            page.FocusOn("Sam Okafor");
            StaThread.Settle();

            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (var g = Graphics.FromImage(bitmap))
            {
                page.DrawChartForTest(g, bounds);
            }

            return page.MarksDrawn.ToList();
        }, TimeSpan.FromSeconds(60), () => "Lap Times chart marks");

        Assert.Contains(marks, m => m.Text == "Best 14.105");
        Assert.Contains(marks, m => m.Text == "+1.75 s, 49 car lengths");
        Assert.Contains(marks, m => m.Text == "Fade starts");

        // Every one found room, inside the chart, and none overlaps another.
        Assert.All(marks, m => Assert.NotNull(m.Label));

        var labels = marks.Select(m => m.Label!.Value).ToList();

        Assert.All(labels, l => Assert.True(bounds.Contains(l), $"{l} leaves the chart"));

        for (var i = 0; i < labels.Count; i++)
        {
            for (var j = i + 1; j < labels.Count; j++)
            {
                Assert.False(labels[i].IntersectsWith(labels[j]), $"{marks[i].Text} overlaps {marks[j].Text}");
            }
        }
    }

    [Fact]
    public void With_no_one_ticked_the_page_says_nothing_about_a_race()
    {
        var history = SessionHistoryStore.FromSessions(new[] { Race() });

        var (headline, things, drill) = StaThread.Run(() =>
        {
            using var page = new LapChartControl(history, new AppSettings());
            _ = page.Handle;
            StaThread.Settle();

            var list = (CheckedListBox)typeof(LapChartControl)
                .GetField("_clbDrivers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(page)!;

            for (var i = 0; i < list.Items.Count; i++)
            {
                list.SetItemChecked(i, false);
            }

            StaThread.Settle();

            return (page.HeadlineText, page.ThingsShown.Count, page.DrillText);
        }, TimeSpan.FromSeconds(60), () => "Lap Times with no one ticked");

        Assert.Equal(string.Empty, headline);
        Assert.Equal(0, things);
        Assert.Equal(string.Empty, drill);
    }
}
