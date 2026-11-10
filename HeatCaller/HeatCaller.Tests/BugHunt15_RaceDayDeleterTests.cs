namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15 (9 Nov 2026), B18: a race day deleted with its races kept
/// takes its rounds with it, and with the round gone the thrown-away first
/// run of a heat run again stopped reading as superseded - an every-race
/// championship scored the heat twice.
/// </summary>
public sealed class BugHunt15_RaceDayDeleterTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static SessionRecord Race(string? roundId, DateTime startedUtc, params string[] order) => new()
    {
        SessionType = RaceSessionType.Race,
        RoundId = roundId,
        StartedAtUtc = startedUtc,
        Results = order.Select((name, i) => new SessionResultEntry
        {
            Position = i + 1,
            Transponder = "T-" + name,
            DriverName = name,
            Laps = 10,
        }).ToList(),
    };

    [Fact]
    public void A_heat_run_again_still_counts_once_after_its_round_is_deleted()
    {
        var rounds = RoundDefinitionStore.Load();
        var heat = new RoundDefinition { ClassName = "Stock", RoundLabel = "Heat 2", Kind = RoundKind.Qualifier };
        rounds.Add(heat);

        // A club championship that counts every race - no class, mains or day filter.
        var season = new Season { Name = "Club Points", AutoInclude = true, MainsOnly = false };

        var first = Race(heat.Id, DateTime.UtcNow.AddHours(-1), "Alex", "Sam");
        var again = Race(heat.Id, DateTime.UtcNow, "Sam", "Alex");
        rounds.MarkRecorded(heat.Id, again.Id);

        var history = SessionHistoryStore.FromSessions(new[] { first, again });

        Assert.Equal(again.Id, Assert.Single(EventSpine.SessionsFor(season, null, rounds, history)).Id);

        // The race day deleted, "Delete saved races" unticked: the rounds go,
        // both runs stay in Race History.
        rounds.Delete(heat.Id);
        Assert.Null(rounds.Get(heat.Id));

        Assert.Equal(again.Id, Assert.Single(EventSpine.SessionsFor(season, null, rounds, history)).Id);
        Assert.True(EventSpine.SupersededByRerun(first, rounds, history));
        Assert.False(EventSpine.SupersededByRerun(again, rounds, history));
        Assert.False(EventSpine.RuleTakes(season, first, rounds, null, history));

        // Ticked in by hand, it still counts.
        season.IncludedSessionIds.Add(first.Id);
        Assert.Equal(2, EventSpine.SessionsFor(season, null, rounds, history).Count);
    }

    [Fact]
    public void A_single_run_of_a_deleted_round_still_counts()
    {
        var rounds = RoundDefinitionStore.Load();
        var season = new Season { Name = "Club Points", AutoInclude = true, MainsOnly = false };

        // Its round long gone, and a demo of the same round later - never a re-run.
        var only = Race("gone-round", DateTime.UtcNow.AddHours(-1), "Alex", "Sam");
        var demo = Race("gone-round", DateTime.UtcNow, "Sam", "Alex");
        demo.IsDemo = true;

        var history = SessionHistoryStore.FromSessions(new[] { only, demo });

        Assert.False(EventSpine.SupersededByRerun(only, rounds, history));
        Assert.Equal(only.Id, Assert.Single(EventSpine.SessionsFor(season, null, rounds, history)).Id);
    }
}
