namespace HeatCaller.Tests;

/// <summary>
/// Race-night build plan, Phase 3 (11 Nov 2026): the first round seeded by
/// pace, the protest window, the mains preview and the gentle late entry.
/// </summary>
public sealed class FairResultsTests
{
    private static readonly IReadOnlyList<string> Field = new[] { "a", "b", "c", "d", "e", "f" };

    [Fact]
    public void By_pace_puts_similar_pace_together_fastest_last_and_no_lap_with_the_slowest()
    {
        var pace = new Dictionary<string, long> { ["a"] = 9000, ["b"] = 8000, ["c"] = 7000, ["d"] = 10000, ["e"] = 7500 };

        var seeding = FirstRound.For(Field, FirstRoundSeeding.Pace, pace, new[] { 3, 3 }, 1);

        Assert.NotNull(seeding.FirstRoundHeats);
        Assert.Equal(new[] { "c", "e", "b" }, seeding.FirstRoundHeats![1]);
        Assert.Equal(new[] { "a", "d", "f" }, seeding.FirstRoundHeats[0]);
        Assert.Equal(new[] { "c", "e", "b", "a", "d", "f" }, seeding.Order);
    }

    [Fact]
    public void By_pace_with_nobody_timed_keeps_the_old_deal()
    {
        var seeding = FirstRound.For(Field, FirstRoundSeeding.Pace, new Dictionary<string, long>(), new[] { 3, 3 }, 1);

        Assert.Null(seeding.FirstRoundHeats);
        Assert.Equal(Field, seeding.Order);
    }

    [Fact]
    public void Alphabetical_is_the_old_deal()
    {
        var pace = new Dictionary<string, long> { ["a"] = 9000 };

        var seeding = FirstRound.For(Field, FirstRoundSeeding.Alphabetical, pace, new[] { 3, 3 }, 1);

        Assert.Null(seeding.FirstRoundHeats);
        Assert.Equal(Field, seeding.Order);
    }

    [Fact]
    public void Random_is_the_same_every_time_for_the_same_day_and_class()
    {
        var seed = FirstRound.StableSeed("day1|Stock");

        var once = FirstRound.For(Field, FirstRoundSeeding.Random, new Dictionary<string, long>(), new[] { 3, 3 }, seed).Order;
        var again = FirstRound.For(Field, FirstRoundSeeding.Random, new Dictionary<string, long>(), new[] { 3, 3 }, FirstRound.StableSeed("day1|Stock")).Order;

        Assert.Equal(once, again);
        Assert.Equal(Field.OrderBy(x => x), once.OrderBy(x => x));
        Assert.NotEqual(FirstRound.StableSeed("day1|Stock"), FirstRound.StableSeed("day1|Mod"));
    }

    [Fact]
    public void A_new_race_day_seeds_by_pace_with_a_five_minute_protest_window()
    {
        var day = new RaceEvent();

        Assert.Equal(FirstRoundSeeding.Pace, day.FirstRoundSeeding);
        Assert.Equal(5, day.ProtestMinutes);
    }

    private static readonly DateTime T0 = new(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Results_are_provisional_for_the_window_after_the_last_qualifier()
    {
        var s1 = new SessionRecord { Id = "s1", SessionType = RaceSessionType.Race, StartedAtUtc = T0.AddMinutes(-20), EndedAtUtc = T0.AddMinutes(-15) };
        var s2 = new SessionRecord { Id = "s2", SessionType = RaceSessionType.Race, StartedAtUtc = T0.AddMinutes(-6), EndedAtUtc = T0.AddMinutes(-1) };
        var rounds = new[]
        {
            new RoundDefinition { Kind = RoundKind.Qualifier, LinkedSessionId = "s1" },
            new RoundDefinition { Kind = RoundKind.Qualifier, LinkedSessionId = "s2" },
            new RoundDefinition { Kind = RoundKind.Qualifier },
        };
        var sessions = new[] { s1, s2 }.ToDictionary(s => s.Id);

        var finalAt = ProtestWindow.FinalAtUtc(rounds, id => sessions.GetValueOrDefault(id), 5);

        Assert.Equal(T0.AddMinutes(4), finalAt);
        Assert.True(ProtestWindow.IsProvisional(finalAt, T0));
        Assert.False(ProtestWindow.IsProvisional(finalAt, T0.AddMinutes(4)));
        Assert.Null(ProtestWindow.FinalAtUtc(rounds, id => sessions.GetValueOrDefault(id), 0));
        Assert.Null(ProtestWindow.FinalAtUtc(new[] { new RoundDefinition() }, _ => null, 5));
    }

    [Fact]
    public void The_mains_preview_names_each_driver_with_why_and_marks_the_bump_up_places()
    {
        var plan = MainsBuilder.PlanSeeding(new[] { "a", "b", "c", "d" }, 2);
        var entries = new Dictionary<string, QualifyingCalculator.Entry>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = new("a", "Sam", "", 14, 302_300, 20_000, 2, SourceRoundLabel: "Q2 Heat 1"),
            ["b"] = new("b", "Jo", "", 14, 305_100, 20_100, 2, SourceRoundLabel: "Q1 Heat 2"),
            ["c"] = new("c", "Al", "", 13, 301_000, 21_000, 2, SourceRoundLabel: "Q2 Heat 2"),
            ["d"] = new("d", "Bea", "", 12, 300_000, 22_000, 2, SourceRoundLabel: "Q1 Heat 1"),
        };

        var text = MainsPreview.Text(plan, entries, QualifyingMethod.TotalLaps, 3, bumpUp: 1);

        Assert.Equal(
            "A-Main (plus the top 1 of the B-Main):\r\n"
            + "   1. Sam - 14 laps, 5:02.3 (Q2 Heat 1)\r\n"
            + "   2. Jo - 14 laps, 5:05.1 (Q1 Heat 2)\r\n"
            + "B-Main:\r\n"
            + "   3. Al - 13 laps, 5:01.0 (Q2 Heat 2)  [bump-up place]\r\n"
            + "   4. Bea - 12 laps, 5:00.0 (Q1 Heat 1)",
            text);
    }

    [Fact]
    public void Best_consecutive_qualifying_is_shown_as_its_run()
    {
        var entry = new QualifyingCalculator.Entry("a", "Sam", "", 14, 302_300, 20_000, 2, BestConsecutiveMs: 36_912, SourceRoundLabel: "Q1 Heat 2");

        Assert.Equal("best 3 in a row 36.912 (Q1 Heat 2)", MainsPreview.ResultWords(entry, QualifyingMethod.BestConsecutiveLaps, 3));
    }

    private static RoundDefinition Heat(string label, params string[] drivers) =>
        new() { RoundLabel = label, Kind = RoundKind.Qualifier, AssignedTransponders = drivers.ToList() };

    [Fact]
    public void A_late_entry_goes_in_the_smallest_heat_of_each_round_still_to_run()
    {
        var q1h1 = Heat("Q1 Heat 1", "a", "b");
        q1h1.LinkedSessionId = "run";
        var q1h2 = Heat("Q1 Heat 2", "c", "d");
        q1h2.LinkedSessionId = "run2";
        var q2h1 = Heat("Q2 Heat 1", "a", "b", "c");
        var q2h2 = Heat("Q2 Heat 2", "d");
        var q3h1 = Heat("Q3 Heat 1", "a", "b");
        var q3h2 = Heat("Q3 Heat 2", "c", "d");

        var places = LateEntry.Place(new[] { q1h1, q1h2, q2h1, q2h2, q3h1, q3h2 }, "late", onTrackRoundId: null);

        // Q1 has run; Q2's smallest is Heat 2; Q3's are equal, so its first.
        Assert.Equal(new[] { q2h2, q3h1 }, places);
    }

    [Fact]
    public void A_late_entry_skips_a_round_they_are_in_and_a_heat_on_track()
    {
        var q2h1 = Heat("Q2 Heat 1", "a");
        var q2h2 = Heat("Q2 Heat 2", "b", "c");
        var q3h1 = Heat("Q3 Heat 1", "late");

        var places = LateEntry.Place(new[] { q2h1, q2h2, q3h1 }, "LATE", onTrackRoundId: q2h1.Id);

        Assert.Equal(new[] { q2h2 }, places);
    }

    /// <summary>The app source, for checks on how the pieces are wired in.</summary>
    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }

    [Fact]
    public void The_build_seeds_the_first_round_and_the_finals_question_shows_the_preview_and_the_window()
    {
        var source = Source("RaceProgramControl.cs");

        // 11 Nov 2026, Phase 5.2: heads-up builds its own heats first, and
        // every other night falls through to the seeding as before.
        Assert.Contains("var heats = HeadsUpHeatsFor(", source);
        Assert.Contains("?? (round == 0 && seeding.FirstRoundHeats is { } seeded", source);
        Assert.Contains("var summary = MainsPreview.Text(plan, entries,", source);
        Assert.Contains("ProtestWindow.Words(finalAt!.Value)", source);
        Assert.Contains("new ToolStripMenuItem(\"Add a Late Entry...\"", source);
    }
}
