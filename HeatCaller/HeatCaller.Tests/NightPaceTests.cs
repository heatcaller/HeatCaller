namespace HeatCaller.Tests;

/// <summary>
/// Race-night build plan, Phase 0 (11 Nov 2026): how the night ran, as
/// numbers - green to green, the longest gaps and what happened in them, and
/// the share of the night on track. See NightPace.
/// </summary>
public sealed class NightPaceTests
{
    private static readonly DateTime Seven = new(2026, 11, 7, 19, 0, 0, DateTimeKind.Utc);

    private static DateTime At(double minutes) => Seven.AddMinutes(minutes);

    /// <summary>A race that went green <paramref name="start"/> minutes after seven and ran <paramref name="length"/> minutes.</summary>
    private static SessionRecord Race(string label, double start, double length = 5, double? stagedMinutesBefore = null) => new()
    {
        SessionType = RaceSessionType.Race,
        Label = label,
        StartedAtUtc = At(start),
        EndedAtUtc = At(start + length),
        StagedAtUtc = stagedMinutesBefore is { } before ? At(start - before) : null,
    };

    [Fact]
    public void Fewer_than_two_races_say_nothing()
    {
        Assert.Null(NightPace.For(Array.Empty<SessionRecord>()));
        Assert.Null(NightPace.For(new[] { Race("Q1 Heat 1", 0) }));
    }

    [Fact]
    public void Green_to_green_is_the_median_so_one_long_gap_does_not_hide_a_quick_night()
    {
        // Starts at 0, 8, 16, 40: gaps between greens 8, 8, 24.
        var pace = NightPace.For(new[] { Race("H1", 0), Race("H2", 8), Race("H3", 16), Race("H4", 40) })!;

        Assert.Equal(4, pace.Races);
        Assert.Equal(TimeSpan.FromMinutes(8), pace.MedianGreenToGreen);

        // Finish to next green: 3, 3, 19.
        Assert.Equal(TimeSpan.FromMinutes(3), pace.MedianGap);
        Assert.Equal(TimeSpan.FromMinutes(19), pace.Longest!.Length);
        Assert.Equal("H3", pace.Longest.Before.Label);
        Assert.Equal("H4", pace.Longest.After.Label);
    }

    [Fact]
    public void On_track_share_is_racing_over_the_night()
    {
        // Two 5-minute races from 0 to 15: 10 of 15 minutes racing.
        var pace = NightPace.For(new[] { Race("H1", 0), Race("H2", 10) })!;

        Assert.Equal(10.0 / 15.0, pace.OnTrackShare!.Value, 6);
        Assert.Equal("67%", NightPace.Percent(pace.OnTrackShare.Value));
    }

    [Fact]
    public void A_break_of_hours_splits_the_night_into_sittings_and_is_not_dead_time()
    {
        // An afternoon pair, then an evening pair four hours later.
        var pace = NightPace.For(new[] { Race("P1", 0), Race("P2", 10), Race("E1", 255), Race("E2", 265) })!;

        // Only the two gaps inside sittings count.
        Assert.Equal(TimeSpan.FromMinutes(5), pace.Longest!.Length);
        Assert.Equal(TimeSpan.FromMinutes(10), pace.MedianGreenToGreen);

        // 20 minutes racing over two 15-minute sittings.
        Assert.Equal(20.0 / 30.0, pace.OnTrackShare!.Value, 6);
    }

    [Fact]
    public void Practice_demo_and_imported_sessions_are_left_out()
    {
        var practice = Race("Practice", 5);
        practice.SessionType = RaceSessionType.Practice;
        var demo = Race("[DEMO] Heat", 6);
        demo.IsDemo = true;
        var imported = Race("LiveRC A-Main", 7);
        imported.ImportedFrom = "LiveRC";

        var pace = NightPace.For(new[] { Race("H1", 0), practice, demo, imported, Race("H2", 20) })!;

        Assert.Equal(2, pace.Races);
        Assert.Equal(TimeSpan.FromMinutes(15), pace.Longest!.Length);
    }

    [Fact]
    public void A_gap_in_which_the_finals_were_built_says_so()
    {
        var before = Race("Q2 Heat 4", 0);
        var after = Race("B-Main", 20);
        var main = new RoundDefinition { Kind = RoundKind.Main, RoundLabel = "B-Main", CreatedAtUtc = At(12) };
        var earlier = new RoundDefinition { Kind = RoundKind.Qualifier, RoundLabel = "Q2 Heat 4", CreatedAtUtc = At(-60) };

        var gap = NightPace.For(new[] { before, after }, new[] { main, earlier })!.Longest!;

        Assert.Equal(new[] { "building the finals" }, gap.Causes);
    }

    [Fact]
    public void Heats_built_in_a_gap_read_as_building_heats()
    {
        var heat = new RoundDefinition { Kind = RoundKind.Qualifier, CreatedAtUtc = At(8) };

        var gap = NightPace.For(new[] { Race("H1", 0), Race("H2", 15) }, new[] { heat })!.Longest!;

        Assert.Equal("building heats", gap.CauseText);
    }

    [Fact]
    public void A_correction_made_in_the_gap_is_named()
    {
        var corrected = Race("H1", 0);
        corrected.Edits.Add(new ResultEdit { AtUtc = At(7), Change = "laps 11 -> 12", Reason = "missed pass" });

        var gap = NightPace.For(new[] { corrected, Race("H2", 15) })!.Longest!;

        Assert.Contains("correcting a result", gap.Causes);
    }

    [Fact]
    public void A_long_wait_at_the_grid_is_named_with_its_length()
    {
        // Staged 6 minutes before its green.
        var gap = NightPace.For(new[] { Race("H1", 0), Race("H2", 15, stagedMinutesBefore: 6) })!.Longest!;

        Assert.Equal("waiting at the grid for 6:00", gap.CauseText);
    }

    [Fact]
    public void A_short_call_to_the_grid_is_not_worth_naming()
    {
        // A club whose set gap is 8 minutes: ten minutes isn't idle (12 Nov
        // 2026 - see NightPaceStep8Tests for "idle director").
        var gap = NightPace.For(new[] { Race("H1", 0), Race("H2", 15, stagedMinutesBefore: 1.5) }, setGap: TimeSpan.FromMinutes(8))!.Longest!;

        Assert.Empty(gap.Causes);
        Assert.Equal("nothing recorded", gap.CauseText);
    }

    [Fact]
    public void Races_run_are_counted_against_the_program_leaving_practice_out()
    {
        var run1 = new RoundDefinition { Id = "a", Kind = RoundKind.Qualifier, LinkedSessionId = "s1" };
        var run2 = new RoundDefinition { Id = "b", Kind = RoundKind.Qualifier, LinkedSessionId = "s2" };
        var unrun = new RoundDefinition { Id = "c", Kind = RoundKind.Main };
        var practice = new RoundDefinition { Id = "p", Kind = RoundKind.Practice };

        var pace = NightPace.For(new[] { Race("H1", 0), Race("H2", 10) }, new[] { run1, run2, unrun, practice }, new[] { "a", "b", "c", "p" })!;

        Assert.Equal(2, pace.RoundsRun);
        Assert.Equal(3, pace.RoundsPlanned);
    }

    [Fact]
    public void The_longest_gaps_are_listed_longest_first_and_at_most_three()
    {
        // Gaps of 3, 10, 1, 7, 5 minutes.
        var pace = NightPace.For(new[]
        {
            Race("A", 0), Race("B", 8), Race("C", 23), Race("D", 29), Race("E", 41), Race("F", 51),
        })!;

        Assert.Equal(new[] { 10.0, 7.0, 5.0 }, pace.LongestGaps.Select(g => g.Length.TotalMinutes));
    }

    [Fact]
    public void A_stage_is_kept_only_when_it_falls_shortly_before_the_green()
    {
        Assert.Equal(At(-2), NightPace.StageFor(At(-2), At(0)));
        Assert.Null(NightPace.StageFor(null, At(0)));
        Assert.Null(NightPace.StageFor(At(1), At(0)));
        Assert.Null(NightPace.StageFor(At(-61), At(0)));
    }

    [Fact]
    public void The_race_remembers_when_it_was_first_called_to_the_grid()
    {
        long nowMs = 1_000_000;
        var utc0 = new DateTime(2026, 11, 11, 19, 0, 0, DateTimeKind.Utc);
        var race = new RaceControlSession
        {
            NowMsSource = () => nowMs,
            UtcNowSource = () => utc0.AddMilliseconds(nowMs),
        };

        Assert.Null(race.StagedAtUtc);

        race.Stage();
        var staged = race.StagedAtUtc;
        Assert.NotNull(staged);
        Assert.True(Math.Abs((staged!.Value - utc0.AddMilliseconds(1_000_000)).TotalSeconds) < 1);

        // The countdown two minutes later keeps the stage's time.
        nowMs += 120_000;
        race.StartCountdown();
        Assert.Equal(staged, race.StagedAtUtc);

        race.GreenFlag(0);
        Assert.Equal(staged, race.StagedAtUtc);
    }

    [Fact]
    public void A_race_sent_straight_to_green_was_never_staged()
    {
        long nowMs = 1_000_000;
        var utc0 = new DateTime(2026, 11, 11, 19, 0, 0, DateTimeKind.Utc);
        var race = new RaceControlSession
        {
            NowMsSource = () => nowMs,
            UtcNowSource = () => utc0.AddMilliseconds(nowMs),
        };

        race.Stage();
        race.Reset();
        race.GreenFlag(0);

        Assert.Null(race.StagedAtUtc);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(292, "4:52")]
    [InlineData(3599, "59:59")]
    [InlineData(3780, "1 h 03 min")]
    public void Lengths_read_the_way_a_race_director_reads_them(int seconds, string expected)
    {
        Assert.Equal(expected, NightPace.Clock(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void The_one_line_summary_names_every_part_it_has()
    {
        var main = new RoundDefinition { Id = "m", Kind = RoundKind.Main, CreatedAtUtc = At(12), LinkedSessionId = "x" };

        var pace = NightPace.For(new[] { Race("H1", 0), Race("A-Main", 20) }, new[] { main }, new[] { "m" })!;

        Assert.Equal("Green to green 20:00 · longest gap 15:00 (building the finals) · 40% on track · 1 of 1 races run",
            NightPace.Line(pace));
    }
}
