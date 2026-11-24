namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, the Standings page: a championship's rounds counted by
/// calendar date for races on no race day, and a points edit wiping the
/// hand-ticked races when the race history could not be read at launch.
/// </summary>
public sealed class BugHunt24_SeasonsControlTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- #299 (F294): a night past midnight is one round ----------------------

    [Fact]
    public void A_night_that_runs_past_midnight_is_one_round_for_races_on_no_race_day()
    {
        var night = DateTime.Now.Date.AddDays(-10);
        var races = new List<SessionRecord>
        {
            Race(night.AddHours(23).AddMinutes(40)),
            Race(night.AddDays(1).AddMinutes(20)),
        };
        var season = new Season { Name = "Summer", PlannedRounds = 2 };

        var (line, _, finished, rounds) = SeasonsControl.ProgressOf(season, races, null);

        Assert.Equal(1, rounds);
        Assert.False(finished);
        Assert.EndsWith("round 1 of 2", line);
    }

    [Fact]
    public void Two_evenings_are_still_two_rounds_for_races_on_no_race_day()
    {
        var night = DateTime.Now.Date.AddDays(-10);
        var races = new List<SessionRecord>
        {
            Race(night.AddHours(23).AddMinutes(40)),
            Race(night.AddDays(1).AddHours(19)),
        };
        var season = new Season { Name = "Summer", PlannedRounds = 2 };

        var (_, _, finished, rounds) = SeasonsControl.ProgressOf(season, races, null);

        Assert.Equal(2, rounds);
        Assert.True(finished);
    }

    // --- #50 (F295): hand ticks survive an unreadable history -----------------

    [Fact]
    public void A_points_edit_keeps_hand_ticked_races_when_the_history_could_not_be_read()
    {
        File.WriteAllText(_data.PathOf("race-history.json"), "null");
        var history = SessionHistoryStore.Load();
        Assert.True(history.LoadedUnreadable);

        var season = new Season { Name = "Summer", AutoInclude = false };
        season.IncludedSessionIds.AddRange(new[] { "a", "b" });
        season.ExcludedSessionIds.Add("c");

        var (forcedIn, forcedOut) = SeasonsControl.OverridesFor(season,
            Array.Empty<(SessionRecord, bool)>(), RoundDefinitionStore.Load(), null, history);

        Assert.Equal(new[] { "a", "b" }, forcedIn);
        Assert.Equal(new[] { "c" }, forcedOut);
    }

    [Fact]
    public void A_tick_made_tonight_still_counts_while_the_history_is_unreadable()
    {
        File.WriteAllText(_data.PathOf("race-history.json"), "null");
        var history = SessionHistoryStore.Load();
        Assert.True(history.LoadedUnreadable);

        var tonight = Race(DateTime.Now.AddHours(-1));
        var season = new Season { Name = "Summer", AutoInclude = false };
        season.IncludedSessionIds.Add("a");

        var (forcedIn, _) = SeasonsControl.OverridesFor(season,
            new[] { (tonight, true) }, RoundDefinitionStore.Load(), null, history);

        Assert.Equal(new[] { "a", tonight.Id }, forcedIn);
    }

    [Fact]
    public void A_race_gone_from_a_readable_history_still_drops_its_tick()
    {
        var kept = Race(DateTime.Now.AddDays(-7));
        var history = SessionHistoryStore.FromSessions(new[] { kept });
        Assert.False(history.LoadedUnreadable);

        var season = new Season { Name = "Summer", AutoInclude = false };
        season.IncludedSessionIds.AddRange(new[] { "gone", kept.Id });

        var (forcedIn, _) = SeasonsControl.OverridesFor(season,
            new[] { (kept, true) }, RoundDefinitionStore.Load(), null, history);

        Assert.Equal(new[] { kept.Id }, forcedIn);
    }

    private static SessionRecord Race(DateTime startedLocal) => new()
    {
        Label = "A-Main",
        SessionType = RaceSessionType.Race,
        StartedAtUtc = startedLocal.ToUniversalTime(),
        EndedAtUtc = startedLocal.AddMinutes(6).ToUniversalTime(),
    };
}
