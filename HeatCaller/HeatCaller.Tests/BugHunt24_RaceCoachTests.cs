namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), Race Analytics' coaching words (RaceCoach):
/// #233 Head to Head no longer drops a regular for one race on a rental;
/// #234 a mid-race slump with a quick finish is not called a fade; #235 the
/// Overview's last night is the 6am-to-6am night, not the last 12 hours;
/// #236 a driver getting slower is not placed as "still finding time"; and
/// #237 "Your best was 1 session ago", not "1 sessions".
/// </summary>
public sealed class BugHunt24_RaceCoachTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // ------------------------------------------------------------ #233 Head to Head

    private static SessionRecord Race(DateTime whenLocal, params (string Name, string Transponder, int Position)[] results) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        SessionType = RaceSessionType.Race,
        TrackLayoutName = "Club Infield",
        StartedAtUtc = whenLocal.ToUniversalTime(),
        EndedAtUtc = whenLocal.ToUniversalTime().AddMinutes(5),
        Results = results.Select(r => new SessionResultEntry
        {
            Transponder = r.Transponder,
            DriverName = r.Name,
            Position = r.Position,
            FastLapMs = 14_000 + r.Position * 100,
            Laps = 12,
            LapTimesMs = new List<long> { 3_000 }.Concat(Enumerable.Repeat(14_000L + r.Position * 100, 11)).ToList(),
        }).ToList(),
    };

    [Fact]
    public void A_regular_who_once_raced_on_a_rental_is_still_in_the_grid()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "R4", DriverName = "Rental R4", IsRental = true });

        var start = new DateTime(2026, 8, 7, 19, 0, 0, DateTimeKind.Local);

        var fields = RaceCoach.Fields(new[]
        {
            // One night on the club's rental, under his own name.
            Race(start, ("Sam", "R4", 1), ("Leo", "T-Leo", 2), ("Kit", "T-Kit", 3)),
            Race(start.AddDays(7), ("Sam", "T-Sam", 1), ("Leo", "T-Leo", 2), ("Kit", "T-Kit", 3)),
            Race(start.AddDays(14), ("Leo", "T-Leo", 1), ("Sam", "T-Sam", 2), ("Kit", "T-Kit", 3)),
            Race(start.AddDays(21), ("Sam", "T-Sam", 1), ("Kit", "T-Kit", 2), ("Leo", "T-Leo", 3)),
        });

        var m = RaceCoach.BuildMatrix(fields, RaceCoach.Beat.FinishedAhead, profiles);

        Assert.Contains("Sam", m.Drivers);

        // The rental race still does not count for him.
        var sam = m.IndexOf("Sam");
        Assert.Equal(3, m[sam, m.IndexOf("Leo")].Races);
        Assert.Equal(new RaceCoach.Cell(2, 1), m[sam, m.IndexOf("Leo")]);
    }

    // ------------------------------------------------------------ #234 Lap Times fade

    private static SessionResultEntry LapEntry(IEnumerable<long> laps)
    {
        var entry = new SessionResultEntry { Transponder = "101", DriverName = "Sam", Position = 1 };
        entry.LapTimesMs.Add(3_000);
        entry.LapTimesMs.AddRange(laps);
        entry.Laps = entry.LapTimesMs.Count;
        return entry;
    }

    private static List<long> SlumpThenQuickFinish() =>
        Enumerable.Repeat(10_000L, 4)
            .Concat(Enumerable.Repeat(10_500L, 10))
            .Concat(Enumerable.Repeat(9_900L, 3))
            .ToList();

    [Fact]
    public void A_slump_in_the_middle_with_the_quickest_laps_at_the_end_is_not_a_fade()
    {
        var laps = SlumpThenQuickFinish();

        Assert.Empty(RaceCoach.MistakeLaps(laps));
        Assert.Null(RaceCoach.FadeOf(laps));
    }

    [Fact]
    public void A_slump_with_a_quick_finish_reads_as_held_to_the_flag_and_finishing_strong()
    {
        var entry = LapEntry(SlumpThenQuickFinish());
        var laps = RaceCoach.Laps(entry);

        var headline = RaceCoach.LapTimesHeadline(PaceAnalysis.For(entry), laps);
        Assert.DoesNotContain("faded", headline);
        Assert.Contains("quickest at the end", headline);
        Assert.Contains("the pace held to the flag", headline);

        var things = RaceCoach.ThreeThings(laps, RaceCoach.LapNumbers(entry), null);
        Assert.EndsWith("You finished strong.", things[^1].Detail);
    }

    [Fact]
    public void A_fade_that_lasts_to_the_flag_is_still_a_fade()
    {
        var laps = new List<long> { 14_100, 14_120, 14_080, 14_110, 14_090, 14_100, 14_130, 14_100, 14_400, 14_420, 14_450, 14_410, 14_440 };

        var fade = RaceCoach.FadeOf(laps);

        Assert.NotNull(fade);
        Assert.Equal(8, fade!.Start);
    }

    // ------------------------------------------------------------ #235 Overview's last night

    private static SessionRecord At(DateTime local, string label, params SessionResultEntry[] results)
    {
        var session = AnalyticsData.Session("Club Infield", local.Date, label, results);
        session.StartedAtUtc = DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
        session.EndedAtUtc = session.StartedAtUtc.AddMinutes(5);
        return session;
    }

    [Fact]
    public void An_all_day_meeting_is_one_night_and_its_morning_is_not_an_old_best_or_record()
    {
        // A Saturday in October, well away from a clock change.
        var day = new DateTime(2026, 10, 17);
        var sessions = new[]
        {
            At(day.AddHours(9), "Practice", AnalyticsData.Driver("Sam", "T-Sam", AnalyticsData.Steady(14.5)), AnalyticsData.Driver("Leo", "T-Leo", AnalyticsData.Steady(14.6))),
            At(day.AddHours(14), "Heat 1", AnalyticsData.Driver("Sam", "T-Sam", AnalyticsData.Steady(14.4)), AnalyticsData.Driver("Leo", "T-Leo", AnalyticsData.Steady(14.5))),
            At(day.AddHours(21.5), "A-Main", AnalyticsData.Driver("Sam", "T-Sam", AnalyticsData.Steady(14.2)), AnalyticsData.Driver("Leo", "T-Leo", AnalyticsData.Steady(14.3))),
        };

        Assert.Equal(3, RaceCoach.LastNight(sessions).Count);

        var club = RaceCoach.Overview(sessions, null, null);

        // A first visit sets no PB, and a layout's first day is no record story.
        Assert.DoesNotContain(club.Items, i => i.Kind == RaceCoach.NewsKind.PersonalBest);
        Assert.DoesNotContain(club.Items, i => i.Kind == RaceCoach.NewsKind.Record);
    }

    [Fact]
    public void A_night_that_runs_past_midnight_is_one_night_and_the_evening_before_is_not_in_it()
    {
        var sessions = new[]
        {
            At(new DateTime(2026, 10, 16, 21, 0, 0), "Night before", AnalyticsData.Driver("Sam", "T-Sam", AnalyticsData.Steady(14.5))),
            At(new DateTime(2026, 10, 17, 19, 0, 0), "Heat 1", AnalyticsData.Driver("Sam", "T-Sam", AnalyticsData.Steady(14.4))),
            At(new DateTime(2026, 10, 18, 1, 30, 0), "A-Main", AnalyticsData.Driver("Sam", "T-Sam", AnalyticsData.Steady(14.3))),
        };

        var night = RaceCoach.LastNight(sessions);

        Assert.Equal(new[] { "Heat 1", "A-Main" }, night.Select(s => s.Label));
    }

    // ------------------------------------------------------------ #236 and #237 Trends

    private static readonly DateTime TrendStart = new(2026, 8, 15, 19, 0, 0);

    private static List<RaceCoach.TrendSample> Weekly(params long[] laps) =>
        laps.Select((ms, i) => new RaceCoach.TrendSample(TrendStart.AddDays(7 * i), ms)).ToList();

    [Fact]
    public void A_driver_getting_slower_with_a_recent_best_is_not_placed_as_still_finding_time()
    {
        var reading = RaceCoach.ReadTrend(Weekly(14500, 14400, 14300, 14200, 14000, 13500, 14200, 14300));

        Assert.True(reading.RecentRate < -RaceCoach.PlateauRate);
        Assert.Equal(2, reading.SessionsSinceBest);
        Assert.Null(reading.Stage);
        Assert.StartsWith("Slower lately:", RaceCoach.TrendHeadline(reading));

        var where = RaceCoach.WhereNote(reading);
        Assert.DoesNotContain("It takes", where);
        Assert.StartsWith("Slower over your last 4 sessions here", where);
    }

    [Fact]
    public void Slower_with_no_best_for_three_sessions_is_still_a_plateau_and_flat_with_a_new_best_still_refining()
    {
        Assert.Equal(RaceCoach.CurveStage.Plateau, RaceCoach.StageOf(-0.1, RaceCoach.PlateauDrought));
        Assert.Equal(RaceCoach.CurveStage.Refining, RaceCoach.StageOf(-0.01, 0));
        Assert.Null(RaceCoach.StageOf(-0.1, 2));
    }

    [Fact]
    public void The_where_line_is_unchanged_when_there_is_a_stage_or_too_little_to_read()
    {
        var placed = RaceCoach.ReadTrend(Weekly(15000, 14900, 14800, 14700, 14600, 14500, 14400, 14300));
        Assert.NotNull(placed.Stage);
        Assert.StartsWith("From your last ", RaceCoach.WhereNote(placed));

        var few = RaceCoach.ReadTrend(Weekly(14900, 14700, 14600));
        Assert.Equal("It takes 4 sessions over more than a week to place you.", RaceCoach.WhereNote(few));
    }

    [Fact]
    public void Slower_lately_with_the_best_one_session_back_says_one_session_ago()
    {
        var reading = RaceCoach.ReadTrend(Weekly(20000, 20000, 19900, 20400));

        Assert.Equal(1, reading.SessionsSinceBest);

        var headline = RaceCoach.TrendHeadline(reading);
        Assert.StartsWith("Slower lately:", headline);
        Assert.Contains("Your best was 1 session ago.", headline);
        Assert.DoesNotContain("1 sessions", headline);
    }

    [Fact]
    public void Slower_lately_with_the_best_two_sessions_back_still_says_sessions()
    {
        var reading = RaceCoach.ReadTrend(Weekly(20000, 19900, 20000, 20400));

        Assert.Equal(2, reading.SessionsSinceBest);
        Assert.Contains("Your best was 2 sessions ago.", RaceCoach.TrendHeadline(reading));
    }
}
