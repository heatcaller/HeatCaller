namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 21 (15 Nov 2026), analytics and history: a season is not
/// "finished" while its last race day is still running; a handicap race's
/// Winner tile names the handicap winner; a credited start lap's dash is
/// never a best lap; "No big mistakes" only claims every lap was within 4%
/// when it was; a driver who equals a track record is not told they hold
/// it; and Head to Head's grid and score do not count practice as races.
/// </summary>
public sealed class BugHunt21_AnalyticsHistoryTests
{
    // ------------------------------------------------------------ #28 season finished

    [Fact]
    public void A_season_is_not_finished_while_its_last_race_day_is_still_open()
    {
        // Every class counts, two mains a night, two rounds planned. The
        // last night's first main is in; its second has not run.
        var events = RaceEventStore.Load();
        var races = new List<SessionRecord>();
        // The last night is tonight: an open day from an earlier night counts as done.
        var start = DateTime.UtcNow.AddDays(-14).AddMinutes(-1);
        var days = new List<RaceEvent>();

        for (var night = 0; night < 2; night++)
        {
            var day = new RaceEvent { Name = $"Round {night + 1}" };
            var classes = night == 0 ? new[] { "Stock", "Mod" } : new[] { "Stock" };

            foreach (var cls in classes)
            {
                var round = new RoundDefinition { ClassName = cls, Kind = RoundKind.Main, RoundLabel = "A-Main" };
                day.RoundIds.Add(round.Id);
                races.Add(new SessionRecord
                {
                    SessionType = RaceSessionType.Race,
                    RoundId = round.Id,
                    StartedAtUtc = start.AddDays(14 * night),
                    Results = new List<SessionResultEntry> { new() { Position = 1, DriverName = cls + " Al", Transponder = cls + "1", Laps = 20 } },
                });
            }

            days.Add(day);
        }

        days[0].FinishedUtc = start.AddHours(4);
        days[1].RoundIds.Add(new RoundDefinition { ClassName = "Mod", Kind = RoundKind.Main, RoundLabel = "A-Main" }.Id);

        foreach (var day in days)
        {
            events.Upsert(day);
        }

        var season = new Season { Name = "Winter Series", PlannedRounds = 2 };

        var (line, _, finished, rounds) = SeasonsControl.ProgressOf(season, races, events);

        Assert.False(finished);
        Assert.Equal(2, rounds);
        Assert.Contains("round 2 of 2", line);

        // Closed off, the night is done and so is the season.
        days[1].FinishedUtc = start.AddDays(14).AddHours(4);
        events.Upsert(days[1]);

        var (closedLine, _, closedFinished, _) = SeasonsControl.ProgressOf(season, races, events);

        Assert.True(closedFinished);
        Assert.Contains("finished", closedLine);
    }

    // ------------------------------------------------------------ #29 handicap winner tile

    [Fact]
    public void A_handicap_race_names_the_handicap_winner_on_the_winner_tile()
    {
        var al = new SessionResultEntry { Position = 1, DriverName = "Al", Transponder = "A", Laps = 20 };
        var bo = new SessionResultEntry { Position = 2, DriverName = "Bo", Transponder = "B", Laps = 19 };

        var adjusted = new List<HandicapCalculator.Adjusted>
        {
            new(al, 1, 2, 0, 0),
            new(bo, 2, 1, 0, 2),
        };

        var (value, caption) = RaceHistoryControl.WinnerTile(new[] { al, bo }, adjusted);

        Assert.Equal("Bo", value);
        Assert.StartsWith("Handicap winner", caption);

        // No handicap: the as-raced winner, as before.
        Assert.Equal(("Al", "Winner · 20 laps"), RaceHistoryControl.WinnerTile(new[] { bo, al }, null));
        Assert.Equal(("-", "Winner"), RaceHistoryControl.WinnerTile(Array.Empty<SessionResultEntry>(), null));
    }

    // ------------------------------------------------------------ #30 credited start lap

    [Fact]
    public void A_credited_start_laps_dash_is_not_a_flying_lap()
    {
        var entry = new SessionResultEntry
        {
            Transponder = "A",
            DriverName = "Al",
            StartLapCredited = true,
            LapTimesMs = new List<long> { 0, 3000, 12000, 12100 },
        };

        Assert.Equal(new long[] { 12000, 12100 }, CoachReport.FlyingLaps(entry));
        Assert.Equal(PaceAnalysis.CountedLapsOf(entry), CoachReport.FlyingLaps(entry).ToList());

        // Not credited: only the run to the first crossing goes.
        entry.StartLapCredited = false;
        entry.LapTimesMs = new List<long> { 1500, 12000, 12100 };
        Assert.Equal(new long[] { 12000, 12100 }, CoachReport.FlyingLaps(entry));
    }

    // ------------------------------------------------------------ #31 no big mistakes

    [Fact]
    public void No_big_mistakes_does_not_claim_every_lap_was_within_four_percent_after_a_slow_warm_up()
    {
        // Three warm-up laps well over 4% off the best, each close to the
        // next, so none of them is a mistake.
        var laps = new long[] { 15000, 14800, 14600, 14200, 14000, 14000, 14000, 14000, 14000, 14000, 14000, 14000 };
        var numbers = Enumerable.Range(1, laps.Length).ToList();

        Assert.Empty(RaceCoach.MistakeLaps(laps));

        var said = RaceCoach.ThreeThings(laps, numbers, null).Single(f => f.Line == "No big mistakes");

        Assert.DoesNotContain("Every lap was within 4%", said.Detail);
        Assert.Contains("over 4% off your best, but none stood out", said.Detail);

        // A clean race still says it.
        var clean = Enumerable.Repeat(14000L, 12).ToArray();
        Assert.Equal("Every lap was within 4% of your best.",
            RaceCoach.ThreeThings(clean, numbers, null).Single(f => f.Line == "No big mistakes").Detail);
    }

    // ------------------------------------------------------------ #32 equal record

    [Fact]
    public void A_driver_who_equals_the_track_record_is_not_told_they_hold_it()
    {
        const string track = "Club Oval";
        var sessions = new[]
        {
            new SessionRecord
            {
                SessionType = RaceSessionType.Race,
                TrackLayoutName = track,
                StartedAtUtc = new DateTime(2026, 11, 1, 20, 0, 0, DateTimeKind.Utc),
                Results = new List<SessionResultEntry> { new() { Transponder = "SAM", DriverName = "Sam", FastLapMs = 12300 } },
            },
            new SessionRecord
            {
                SessionType = RaceSessionType.Race,
                TrackLayoutName = track,
                StartedAtUtc = new DateTime(2026, 11, 8, 20, 0, 0, DateTimeKind.Utc),
                Results = new List<SessionResultEntry> { new() { Transponder = "PAT", DriverName = "Pat", FastLapMs = 12300 } },
            },
        };

        var pat = TrendChartControl.RecordText(sessions, track, "Pat", 12300, null, null);
        Assert.DoesNotContain("You hold", pat);
        Assert.StartsWith("You equal the track record here", pat);
        Assert.Contains("by Sam", pat);

        Assert.StartsWith("You hold the track record here", TrendChartControl.RecordText(sessions, track, "Sam", 12300, null, null));
    }

    // ------------------------------------------------------------ #33 head to head practice

    [Fact]
    public void Head_to_heads_grid_and_score_do_not_count_practice_as_races()
    {
        static SessionRecord Run(RaceSessionType type, int day, string first, string second) => new()
        {
            SessionType = type,
            TrackLayoutName = "Club Oval",
            StartedAtUtc = new DateTime(2026, 11, day, 20, 0, 0, DateTimeKind.Utc),
            Results = new List<SessionResultEntry>
            {
                new() { Position = 1, DriverName = first, Transponder = first, Laps = 10, FastLapMs = 12000 },
                new() { Position = 2, DriverName = second, Transponder = second, Laps = 10, FastLapMs = 12500 },
            },
        };

        var fields = RaceCoach.Fields(new[]
        {
            Run(RaceSessionType.Practice, 1, "Al", "Bo"),
            Run(RaceSessionType.Practice, 2, "Al", "Bo"),
            Run(RaceSessionType.Race, 3, "Bo", "Al"),
        });

        // The career table still sees the practice sessions.
        Assert.Equal(3, fields.Count);

        var pair = RaceCoach.Pair(RaceCoach.RacesOnly(fields), "Al", "Bo");

        Assert.Equal(1, pair.Shared);
        Assert.Equal(0, pair.AheadA);
        Assert.Equal(1, pair.AheadB);

        Assert.Contains("RaceCoach.Pair(RaceCoach.RacesOnly(_fields)", Source("HeadToHeadControl.cs"));
        Assert.Contains("RaceCoach.BuildMatrix(RaceCoach.RacesOnly(_fields)", Source("HeadToHeadControl.cs"));
    }

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }
}
