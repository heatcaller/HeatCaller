namespace HeatCaller.Tests;

/// <summary>
/// Saved sessions for the Race Analytics tests: a race or practice on a
/// layout on a day, each driver with their laps in seconds.
/// </summary>
internal static class AnalyticsData
{
    /// <summary>A driver's laps in a session: name, transponder, laps in seconds (a start entry goes in front).</summary>
    public static SessionResultEntry Driver(string name, string transponder, params double[] seconds)
    {
        var entry = new SessionResultEntry { DriverName = name, Transponder = transponder };

        entry.LapTimesMs.Add(3_000);
        entry.LapTimesMs.AddRange(seconds.Select(s => (long)Math.Round(s * 1000)));
        entry.Laps = seconds.Length;
        entry.FastLapMs = seconds.Length == 0 ? 0 : (long)Math.Round(seconds.Min() * 1000);

        return entry;
    }

    /// <summary>A session on <paramref name="layout"/> at 8pm UTC on <paramref name="day"/>, in finishing order.</summary>
    public static SessionRecord Session(string layout, DateTime day, string label, params SessionResultEntry[] results)
    {
        var session = new SessionRecord
        {
            SessionType = RaceSessionType.Race,
            TrackLayoutName = layout,
            Label = label,
            StartedAtUtc = DateTime.SpecifyKind(day.Date.AddHours(20), DateTimeKind.Utc),
        };

        for (var i = 0; i < results.Length; i++)
        {
            results[i].Position = i + 1;
            session.Results.Add(results[i]);
        }

        session.EndedAtUtc = session.StartedAtUtc.AddMinutes(5);

        return session;
    }

    /// <summary>Laps around <paramref name="best"/>: the best, then a little over it, <paramref name="count"/> in all.</summary>
    public static double[] Steady(double best, int count = 8) =>
        Enumerable.Range(0, count).Select(i => best + (i % 3) * 0.04).ToArray();
}

/// <summary>
/// The Overview tab since the Race Engineer rework (13 Nov 2026): the news
/// from the last race night (RaceCoach.Overview) and the control showing it.
/// </summary>
public sealed class RaceAnalyticsOverviewTests
{
    private static readonly DateTime Oct12 = new(2026, 10, 12);
    private static readonly DateTime Nov7 = new(2026, 11, 7);

    private static TrackLayoutStore Layouts()
    {
        var store = TrackLayoutStore.Load();
        store.Upsert(new TrackLayout { Name = "Club Infield", TrackLengthFeet = 600 });
        return store;
    }

    [Fact]
    public void Nothing_raced_says_so_and_has_no_news()
    {
        var club = RaceCoach.Overview(Array.Empty<SessionRecord>(), null, null);

        Assert.Null(club.NightLocal);
        Assert.Empty(club.Items);
        Assert.StartsWith("Nothing raced yet", club.Headline);
    }

    [Fact]
    public void A_record_that_still_stands_says_how_long_and_who_came_closest_in_car_lengths()
    {
        using var data = new TempAppData();

        var sessions = new[]
        {
            AnalyticsData.Session("Club Infield", Oct12, "A-Main",
                AnalyticsData.Driver("Priya Shah", "1", AnalyticsData.Steady(13.842))),
            AnalyticsData.Session("Club Infield", Nov7, "A-Main",
                AnalyticsData.Driver("Sam Okafor", "2", AnalyticsData.Steady(14.105)),
                AnalyticsData.Driver("Priya Shah", "1", AnalyticsData.Steady(14.300))),
        };

        var club = RaceCoach.Overview(sessions, null, Layouts());
        var record = Assert.Single(club.Items, i => i.Kind == RaceCoach.NewsKind.Record);

        Assert.Equal("Priya Shah's Club Infield record has stood three weeks", record.Title);
        Assert.Contains("Closest last night: Sam Okafor, 14.105, 0.263s (8 car lengths) off.", record.Detail);
        Assert.Equal("REC", record.Badge);
    }

    [Fact]
    public void A_record_broken_on_the_night_is_new_and_a_demo_lap_never_is()
    {
        var demo = AnalyticsData.Session("Club Infield", Nov7, "A-Main", AnalyticsData.Driver("Demo Dan", "9", AnalyticsData.Steady(12.000)));
        demo.IsDemo = true;

        var sessions = new[]
        {
            AnalyticsData.Session("Club Infield", Oct12, "A-Main", AnalyticsData.Driver("Priya Shah", "1", AnalyticsData.Steady(13.842))),
            AnalyticsData.Session("Club Infield", Nov7, "A-Main", AnalyticsData.Driver("Sam Okafor", "2", AnalyticsData.Steady(13.800))),
            demo,
        };

        var record = Assert.Single(RaceCoach.Overview(sessions, null, null).Items, i => i.Kind == RaceCoach.NewsKind.Record);

        Assert.Equal("New Club Infield record for Sam Okafor", record.Title);
        Assert.Contains("0.042s under Priya Shah's 13.842", record.Detail);
    }

    [Fact]
    public void A_pb_taken_on_the_night_says_by_how_much()
    {
        var sessions = new[]
        {
            AnalyticsData.Session("Club Infield", Oct12, "Heat 1", AnalyticsData.Driver("Sam Okafor", "2", AnalyticsData.Steady(14.180))),
            AnalyticsData.Session("Club Infield", Nov7, "A-Main", AnalyticsData.Driver("Sam Okafor", "2", AnalyticsData.Steady(14.105))),
        };

        var pb = Assert.Single(RaceCoach.Overview(sessions, null, null).Items, i => i.Kind == RaceCoach.NewsKind.PersonalBest);

        Assert.Equal("Sam Okafor took 0.075s off their Club Infield best", pb.Title);
        Assert.StartsWith("14.105 in A-Main, down from 14.180", pb.Detail);
    }

    [Fact]
    public void A_first_visit_is_no_pb_and_a_ghost_is_never_news()
    {
        var sessions = new[]
        {
            AnalyticsData.Session("Club Infield", Oct12, "Heat 1", AnalyticsData.Driver("Ghost", Ghosts.Prefix + "1", AnalyticsData.Steady(15.0))),
            AnalyticsData.Session("Club Infield", Nov7, "A-Main",
                AnalyticsData.Driver("Sam Okafor", "2", AnalyticsData.Steady(14.105)),
                AnalyticsData.Driver("Ghost", Ghosts.Prefix + "1", AnalyticsData.Steady(14.0))),
        };

        Assert.DoesNotContain(RaceCoach.Overview(sessions, null, null).Items, i => i.Kind == RaceCoach.NewsKind.PersonalBest);
    }

    [Fact]
    public void A_driver_who_got_quicker_through_a_race_is_news()
    {
        var sessions = new[]
        {
            AnalyticsData.Session("Club Infield", Nov7, "A-Main",
                AnalyticsData.Driver("Dana Kowalski", "3", 16.0, 15.9, 15.8, 15.2, 15.1, 15.0, 14.4, 14.3, 14.2)),
        };

        var item = Assert.Single(RaceCoach.Overview(sessions, null, null).Items, i => i.Kind == RaceCoach.NewsKind.GotQuicker);

        Assert.Equal("Dana Kowalski got quicker through A-Main", item.Title);
        Assert.StartsWith("The last third was 1.60s a lap faster than the first.", item.Detail);
    }

    [Fact]
    public void The_driver_whose_mistakes_cost_the_most_is_news()
    {
        var sessions = new[]
        {
            AnalyticsData.Session("Club Infield", Nov7, "A-Main",
                AnalyticsData.Driver("Leo Martins", "4", 14.2, 14.25, 14.2, 16.5, 14.22, 14.2, 17.0, 14.25, 14.2),
                AnalyticsData.Driver("Sam Okafor", "2", AnalyticsData.Steady(14.3, 9))),
        };

        var item = Assert.Single(RaceCoach.Overview(sessions, null, Layouts()).Items, i => i.Kind == RaceCoach.NewsKind.Mistakes);

        Assert.Equal("Leo Martins: quick laps, two mistakes", item.Title);
        Assert.Contains("The mistakes cost 5.1s", item.Detail);
        Assert.DoesNotContain("car lengths", item.Detail); // 143 of them: too many to picture, so seconds only.
        Assert.Equal("!", item.Badge);
    }

    [Fact]
    public void The_feed_keeps_to_five_in_the_order_of_their_kinds()
    {
        var before = new[] { "A", "B", "C", "D", "E" }
            .Select((n, i) => AnalyticsData.Session("Club Infield", Oct12, "Heat", AnalyticsData.Driver(n, $"{i}", AnalyticsData.Steady(15.0 + i * 0.1))))
            .ToList();

        var night = AnalyticsData.Session("Club Infield", Nov7, "A-Main",
            AnalyticsData.Driver("A", "0", 16.0, 15.9, 15.8, 15.2, 15.1, 15.0, 14.4, 14.3, 14.2),
            AnalyticsData.Driver("B", "1", 14.5, 14.55, 14.5, 16.9, 14.52, 14.5, 17.3, 14.55, 14.5),
            AnalyticsData.Driver("C", "2", AnalyticsData.Steady(14.9)),
            AnalyticsData.Driver("D", "3", AnalyticsData.Steady(15.0)),
            AnalyticsData.Driver("E", "4", AnalyticsData.Steady(15.1)));

        var items = RaceCoach.Overview(before.Append(night), null, null).Items;

        Assert.InRange(items.Count, 3, RaceCoach.MostNews);
        Assert.Equal(items.OrderBy(i => i.Kind).Select(i => i.Title), items.Select(i => i.Title));
        Assert.Contains(items, i => i.Kind == RaceCoach.NewsKind.GotQuicker);
        Assert.Contains(items, i => i.Kind == RaceCoach.NewsKind.Mistakes);
        Assert.Contains("more PBs on the night", items.Last(i => i.Kind == RaceCoach.NewsKind.PersonalBest).Detail);
    }

    [Fact]
    public void The_headline_compares_the_club_s_month_with_the_one_before()
    {
        var lastMonth = new DateTime(2026, 9, 25);
        var drivers = new[] { "A", "B", "C" };

        var sessions = drivers.Select((n, i) => AnalyticsData.Session("Club Infield", lastMonth, "Heat", AnalyticsData.Driver(n, $"{i}", AnalyticsData.Steady(14.5 + i * 0.1))))
            .Concat(drivers.Select((n, i) => AnalyticsData.Session("Club Infield", Nov7, "Heat", AnalyticsData.Driver(n, $"{i}", AnalyticsData.Steady(14.42 + i * 0.1)))));

        Assert.Equal(
            "The club got quicker this month: the typical best lap on Club Infield is down 0.080s on the month before.",
            RaceCoach.Overview(sessions, null, null).Headline);
    }

    [Fact]
    public void Without_a_month_to_compare_the_headline_tells_the_night()
    {
        var sessions = new[]
        {
            AnalyticsData.Session("Club Infield", Oct12, "Heat 1", AnalyticsData.Driver("Sam Okafor", "2", AnalyticsData.Steady(14.180))),
            AnalyticsData.Session("Club Infield", Nov7, "A-Main", AnalyticsData.Driver("Sam Okafor", "2", AnalyticsData.Steady(14.105))),
        };

        var headline = RaceCoach.Overview(sessions, null, null).Headline;

        Assert.EndsWith("one driver ran one session, with a new track record and one PB.", headline);
        Assert.DoesNotContain("—", headline);
    }

    [Theory]
    [InlineData(3, "3 days")]
    [InlineData(26, "3 weeks")]
    [InlineData(95, "3 months")]
    public void How_long_a_record_has_stood_is_said_plainly(int days, string said) =>
        Assert.Equal(said.Replace("3", "three"), RaceCoach.HowLong(Nov7.AddDays(-days), Nov7));

    [Fact]
    public void The_overview_shows_the_headline_the_news_and_the_gap_to_the_record()
    {
        var shown = StaThread.Run(() =>
        {
            using var data = new TempAppData();

            var history = SessionHistoryStore.FromSessions(new[]
            {
                AnalyticsData.Session("Club Infield", Oct12, "A-Main",
                    AnalyticsData.Driver("Priya Shah", "1", AnalyticsData.Steady(13.842))),
                AnalyticsData.Session("Club Infield", Nov7, "A-Main",
                    AnalyticsData.Driver("Sam Okafor", "2", AnalyticsData.Steady(14.105)),
                    AnalyticsData.Driver("Priya Shah", "1", AnalyticsData.Steady(14.300))),
            });

            using var page = new AnalyticsOverviewControl(history) { Layouts = Layouts() };
            page.RefreshAll();

            var row = page.RecordsGrid.Rows[0];

            return (page.HeadlineText, page.News.Count, Next: row.Cells["next"].Value?.ToString(), Holder: row.Cells["holder"].Value?.ToString());
        }, TimeSpan.FromSeconds(30), () => "building the Analytics overview");

        Assert.False(string.IsNullOrWhiteSpace(shown.HeadlineText));
        Assert.True(shown.Count >= 1);
        Assert.Equal("Priya Shah", shown.Holder);
        Assert.Equal("Sam Okafor, +0.263s (8 car lengths)", shown.Next);
    }

    [Fact]
    public void The_overview_with_nothing_raced_hides_the_news_and_says_so()
    {
        var (headline, count) = StaThread.Run(() =>
        {
            using var page = new AnalyticsOverviewControl(SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>()));
            return (page.HeadlineText, page.News.Count);
        }, TimeSpan.FromSeconds(30), () => "building an empty Analytics overview");

        Assert.StartsWith("Nothing raced yet", headline);
        Assert.Equal(0, count);
    }
}
