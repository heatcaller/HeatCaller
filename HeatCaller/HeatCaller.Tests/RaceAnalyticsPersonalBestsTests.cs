namespace HeatCaller.Tests;

/// <summary>
/// The Personal Bests tab since the Race Engineer rework (13 Nov 2026): how
/// real each PB is (RaceCoach.ReadPb), the headline and "Try this" made from
/// them, and the control showing it all with its resets still in place.
/// </summary>
public sealed class RaceAnalyticsPersonalBestsTests
{
    private static readonly DateTime Start = new(2026, 9, 3, 20, 0, 0, DateTimeKind.Utc);

    /// <summary>Races a week apart from <see cref="Start"/>, each with this best lap in seconds.</summary>
    private static List<RaceCoach.PbRace> Races(params double[] bests) =>
        bests.Select((b, i) => new RaceCoach.PbRace(Start.AddDays(7 * i), (long)Math.Round(b * 1000))).ToList();

    [Fact]
    public void Too_few_races_is_fresh()
    {
        var read = RaceCoach.ReadPb(14_105, Start, Races(14.105, 14.4));

        Assert.Equal(RaceCoach.PbReality.Fresh, read.Reality);
        Assert.Equal("Fresh", read.Label);
        Assert.Equal("Only two races here, too early to say.", read.Meaning);
    }

    [Fact]
    public void Near_it_in_most_recent_races_is_solid()
    {
        var read = RaceCoach.ReadPb(14_105, Start.AddDays(14), Races(14.5, 14.3, 14.105, 14.15, 14.19, 14.4, 14.17));

        Assert.Equal(RaceCoach.PbReality.Solid, read.Reality);
        Assert.Equal(4, read.Near);
        Assert.Equal(5, read.Window);
        Assert.Equal("Within 0.10s of it in 4 of your last 5 races. This is your real pace.", read.Meaning);
    }

    [Fact]
    public void Never_close_since_it_was_set_is_a_one_off_with_a_real_pace()
    {
        var read = RaceCoach.ReadPb(17_510, Start, Races(17.51, 17.9, 17.85, 17.82));

        Assert.Equal(RaceCoach.PbReality.OneOff, read.Reality);
        Assert.Equal(17_850, read.RealPaceMs);
        Assert.StartsWith("Never within 0.30s of it since", read.Meaning);
        Assert.EndsWith("Treat 17.850 as your real pace here.", read.Meaning);
    }

    [Fact]
    public void Close_now_and_then_is_in_reach()
    {
        var read = RaceCoach.ReadPb(14_105, Start, Races(14.105, 14.35, 14.3, 14.5, 14.6));

        Assert.Equal(RaceCoach.PbReality.InReach, read.Reality);
        Assert.Equal("In reach", read.Label);
    }

    [Fact]
    public void The_headline_names_the_real_pb_and_the_soft_one_and_the_drill_aims_between()
    {
        var lines = new[]
        {
            new RaceCoach.PbLine("Club Infield", 14_105, Start.AddDays(14),
                RaceCoach.ReadPb(14_105, Start.AddDays(14), Races(14.5, 14.3, 14.105, 14.15, 14.19))),
            new RaceCoach.PbLine("Outer Loop", 17_510, Start,
                RaceCoach.ReadPb(17_510, Start, Races(17.51, 17.9, 17.85, 17.82))),
        };

        var headline = RaceCoach.PbHeadline(lines);

        Assert.StartsWith("Your Club Infield PB is the real thing. Your Outer Loop PB is a one-off you haven't been near since", headline);
        Assert.EndsWith("so that is where the easiest time is.", headline);
        Assert.Equal(
            "Next Outer Loop night, aim for 17.68 every lap in practice rather than chasing the 17.51. A repeatable 17.68 beats one 17.51.",
            RaceCoach.PbDrill(lines));
        Assert.DoesNotContain("—", headline);
    }

    [Fact]
    public void All_fresh_says_it_is_too_early()
    {
        var lines = new[] { new RaceCoach.PbLine("Club Infield", 14_105, Start, RaceCoach.ReadPb(14_105, Start, Races(14.105))) };

        Assert.StartsWith("Too few races on any track", RaceCoach.PbHeadline(lines));
        Assert.StartsWith("Race the same track a few nights running.", RaceCoach.PbDrill(lines));
    }

    [Fact]
    public void A_driver_s_races_at_a_track_follow_the_personal_bests_rules()
    {
        var demo = AnalyticsData.Session("Club Infield", Start.AddDays(21), "Demo", AnalyticsData.Driver("Sam", "2", 12.0, 12.0, 12.0));
        demo.IsDemo = true;

        var sessions = new[]
        {
            AnalyticsData.Session("Club Infield", Start, "Heat", AnalyticsData.Driver("Sam", "2", AnalyticsData.Steady(14.5))),
            AnalyticsData.Session("club infield", Start.AddDays(7), "Heat", AnalyticsData.Driver("SAM", "2", AnalyticsData.Steady(14.3))),
            AnalyticsData.Session("Club Infield", Start.AddDays(14), "Heat", AnalyticsData.Driver("Sam", Ghosts.Prefix + "2", AnalyticsData.Steady(13.0))),
            AnalyticsData.Session("Outer Loop", Start.AddDays(14), "Heat", AnalyticsData.Driver("Sam", "2", AnalyticsData.Steady(17.0))),
            demo,
        };

        Assert.Equal(new long[] { 14_500, 14_300 }, RaceCoach.RacesAt(sessions, "sam", "Club Infield", null).Select(r => r.BestMs));

        // A reset's cutoff leaves out the races at or before it.
        Assert.Equal(new long[] { 14_300 }, RaceCoach.RacesAt(sessions, "Sam", "Club Infield", Start.AddHours(1)).Select(r => r.BestMs));
    }

    private static SessionHistoryStore History() => SessionHistoryStore.FromSessions(new[]
    {
        AnalyticsData.Session("Club Infield", Start, "Heat", AnalyticsData.Driver("Priya", "1", AnalyticsData.Steady(13.842)),
            AnalyticsData.Driver("Sam", "2", AnalyticsData.Steady(14.5))),
        AnalyticsData.Session("Club Infield", Start.AddDays(7), "Heat", AnalyticsData.Driver("Sam", "2", AnalyticsData.Steady(14.105))),
        AnalyticsData.Session("Club Infield", Start.AddDays(14), "Heat", AnalyticsData.Driver("Sam", "2", AnalyticsData.Steady(14.15))),
        AnalyticsData.Session("Club Infield", Start.AddDays(21), "Heat", AnalyticsData.Driver("Sam", "2", AnalyticsData.Steady(14.18))),
        AnalyticsData.Session("Outer Loop", Start, "Heat", AnalyticsData.Driver("Sam", "2", AnalyticsData.Steady(17.51))),
        AnalyticsData.Session("Outer Loop", Start.AddDays(7), "Heat", AnalyticsData.Driver("Sam", "2", AnalyticsData.Steady(17.9))),
        AnalyticsData.Session("Outer Loop", Start.AddDays(14), "Heat", AnalyticsData.Driver("Sam", "2", AnalyticsData.Steady(17.85))),
    });

    [Fact]
    public void The_page_shows_each_pb_s_reading_the_gap_to_the_record_and_a_drill()
    {
        var shown = StaThread.Run(() =>
        {
            using var data = new TempAppData();

            var layouts = TrackLayoutStore.Load();
            layouts.Upsert(new TrackLayout { Name = "Club Infield", TrackLengthFeet = 600 });

            using var page = new PersonalBestsControl(History(), PersonalBestResetStore.Load(), new UndoService()) { Layouts = layouts };
            page.SelectDriver("Sam");
            page.RefreshAll();

            var rows = page.Grid.Rows.Cast<DataGridViewRow>()
                .Select(r => (Track: r.Cells["track"].Value?.ToString(), Real: r.Cells["real"].Value?.ToString(),
                    Gap: r.Cells["gap"].Value?.ToString(), Means: r.Cells["means"].Value?.ToString()))
                .ToList();

            return (page.HeadlineText, page.DrillText, Rows: rows);
        }, TimeSpan.FromSeconds(30), () => "building Personal Bests");

        var infield = Assert.Single(shown.Rows, r => r.Track == "Club Infield");
        var loop = Assert.Single(shown.Rows, r => r.Track == "Outer Loop");

        Assert.Equal("Solid", infield.Real);
        Assert.Equal("+0.263s (8 car lengths)", infield.Gap);
        Assert.Equal("One-off", loop.Real);
        Assert.Equal("You hold the record", loop.Gap);
        Assert.StartsWith("Your Club Infield PB is the real thing. Your Outer Loop PB is a one-off", shown.HeadlineText);
        Assert.StartsWith("Next Outer Loop night, aim for", shown.DrillText);
    }

    [Fact]
    public void Only_the_record_s_holder_is_told_they_hold_it()
    {
        // Bug hunt 20: a rental's quicker lap and a lap that only tied an
        // earlier record were both told "You hold the record".
        var gaps = StaThread.Run(() =>
        {
            using var data = new TempAppData();

            var profiles = ProfileStore.Load();
            profiles.Upsert(new DriverProfile { Transponder = "RENT", DriverName = "Guest", IsRental = true });

            var history = SessionHistoryStore.FromSessions(new[]
            {
                AnalyticsData.Session("Club Infield", Start, "Heat", AnalyticsData.Driver("Priya", "1", AnalyticsData.Steady(15.1)),
                    AnalyticsData.Driver("Guest", "RENT", AnalyticsData.Steady(14.8))),
                AnalyticsData.Session("Club Infield", Start.AddDays(7), "Heat", AnalyticsData.Driver("Sam", "2", AnalyticsData.Steady(15.1))),
            });

            using var page = new PersonalBestsControl(history, PersonalBestResetStore.Load(), new UndoService()) { Profiles = profiles };

            string? GapFor(string driver)
            {
                page.SelectDriver(driver);
                page.RefreshAll();
                return Assert.Single(page.Grid.Rows.Cast<DataGridViewRow>()).Cells["gap"].Value?.ToString();
            }

            return (Priya: GapFor("Priya"), Guest: GapFor("Guest"), Sam: GapFor("Sam"));
        }, TimeSpan.FromSeconds(30), () => "building Personal Bests with a rental");

        Assert.Equal("You hold the record", gaps.Priya);
        Assert.Equal("-0.300s (not counted for the record)", gaps.Guest);
        Assert.Equal("Equals the record", gaps.Sam);
    }

    [Fact]
    public void Sorting_by_a_heading_keeps_each_pill_on_its_own_track()
    {
        // Bug hunt 18: the pills were kept by row index, so a header-click
        // sort painted one track's pill and dots on another track's row.
        var shown = StaThread.Run(() =>
        {
            using var data = new TempAppData();

            using var page = new PersonalBestsControl(History(), PersonalBestResetStore.Load(), new UndoService());
            page.SelectDriver("Sam");
            page.RefreshAll();

            page.Grid.Sort(page.Grid.Columns["track"]!, System.ComponentModel.ListSortDirection.Descending);

            return page.Grid.Rows.Cast<DataGridViewRow>()
                .Select(r => (Track: r.Cells["track"].Value?.ToString(), Real: r.Cells["real"].Value?.ToString(),
                    Painted: page.ReadingAt(r.Index)?.Label))
                .ToList();
        }, TimeSpan.FromSeconds(30), () => "sorting Personal Bests");

        Assert.Equal(new[] { "Outer Loop", "Club Infield" }, shown.Select(r => r.Track));
        Assert.All(shown, r => Assert.Equal(r.Real, r.Painted));
    }

    [Fact]
    public void A_reset_still_leaves_a_row_to_clear_it_from()
    {
        var means = StaThread.Run(() =>
        {
            using var data = new TempAppData();

            var resets = PersonalBestResetStore.Load();
            resets.SetCutoff("Sam", string.Empty, Start.AddDays(30));

            using var page = new PersonalBestsControl(History(), resets, new UndoService());
            page.SelectDriver("Sam");

            return page.Grid.Rows.Cast<DataGridViewRow>()
                .Select(r => (Driver: r.Cells["driver"].Value?.ToString(), Means: r.Cells["means"].Value?.ToString()))
                .ToList();
        }, TimeSpan.FromSeconds(30), () => "building Personal Bests after a reset");

        Assert.Equal(2, means.Count);
        Assert.All(means, m => Assert.Equal("Sam", m.Driver));
        Assert.All(means, m => Assert.StartsWith("No laps here since the reset on", m.Means));
    }
}
