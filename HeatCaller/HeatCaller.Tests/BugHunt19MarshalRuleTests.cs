namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 19 (14 Nov 2026), the marshal rule: a time penalty lands on
/// the driver's next race whichever of their transponders it is on (#4)
/// and is still owed when the race it was marked on never reached the
/// history (#47); a lost qualifier follows a class rename (#20); and under
/// points the lost heat is no longer their Best Heat or a tie-break (#25).
/// </summary>
public sealed class BugHunt19MarshalRuleTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    private readonly RoundDefinitionStore _rounds = RoundDefinitionStore.Load();
    private readonly ProfileStore _profiles = ProfileStore.Load();
    private readonly List<SessionRecord> _sessions = new();

    public void Dispose() => _data.Dispose();

    private static readonly DateTime T0 = new(2026, 11, 14, 20, 0, 0, DateTimeKind.Utc);

    private static RoundDefinition Missed(string transponder, MarshalNoShowRule rule, string className = "Stock") => new()
    {
        Id = "q1h2",
        ClassName = "Mod",
        RoundLabel = "Q1 Heat 2",
        Kind = RoundKind.Qualifier,
        NoShowMarshals = new List<MarshalNoShow> { MarshalRule.Log("Ann (Turn 1)", transponder, className, rule, 10, T0) },
    };

    private static SessionRecord NextRace(string id, string transponder) => new()
    {
        Id = id,
        SessionType = RaceSessionType.Race,
        Results = new List<SessionResultEntry>
        {
            new() { Transponder = transponder, DriverName = "Ann", Laps = 14, ElapsedMs = 300_000, Position = 1 },
            new() { Transponder = "B", DriverName = "B", Laps = 14, ElapsedMs = 305_000, Position = 2 },
        },
    };

    // ------------------------------------------------------------ #4

    [Fact]
    public void A_time_penalty_logged_on_the_second_cars_transponder_goes_on_the_next_race_on_the_first_car()
    {
        Assert.True(_profiles.TryAdd(new DriverProfile { Transponder = "T1", AlternateTransponder = "T2", DriverName = "Ann" }, out _));

        // The post was held under T2 (their Mod car); the next race is Stock, on T1.
        var missed = Missed("T2", MarshalNoShowRule.TimePenalty);
        var next = NextRace("next", "T1");

        var pending = MarshalRule.PendingPenalties(next.Results, "q2h1", new[] { missed }, null, _profiles);
        Assert.Single(pending);

        Assert.Single(MarshalRule.ApplyPenalties(next, pending, _profiles));
        Assert.Equal("next", missed.NoShowMarshals![0].AppliedToSessionId);
        Assert.Equal(10_000, next.Results.Single(r => r.Transponder == "T1").TimePenaltyMs);

        // And the undo finds it there too.
        Assert.True(MarshalRule.TakePenaltyOff(next, missed, missed.NoShowMarshals[0], _profiles));
        Assert.Equal(0, next.Results.Single(r => r.Transponder == "T1").TimePenaltyMs);

        Assert.Contains("_profiles, raceIsFiled)", Source("RaceFilingService.cs"));
        Assert.Contains("MarshalRule.ApplyPenalties(record, pending, _profiles)", Source("RaceFilingService.cs"));
        Assert.Contains("MarshalRule.TakePenaltyOff(race, saved, noShow, _profiles)", Source("MainForm.SetupStep8.cs"));
    }

    // ------------------------------------------------------------ #47

    [Fact]
    public void A_penalty_marked_on_a_race_that_never_reached_the_history_is_still_owed()
    {
        var missed = Missed("A", MarshalNoShowRule.TimePenalty);
        missed.NoShowMarshals![0].AppliedToSessionId = "never-saved";
        var recovered = NextRace("recovered", "A");

        // Its race is not in the history (the save failed, then the app closed): owed.
        Assert.Single(MarshalRule.PendingPenalties(recovered.Results, "q2h1", new[] { missed }, null, raceIsFiled: _ => false));

        // Its race is in the history (or waiting to be filed again): served.
        Assert.Empty(MarshalRule.PendingPenalties(recovered.Results, "q2h1", new[] { missed }, null, raceIsFiled: _ => true));

        Assert.Contains("_sessionHistory.LoadedUnreadable", Source("RaceFilingService.cs"));
    }

    // ------------------------------------------------------------ #20

    [Fact]
    public void A_lost_qualifier_follows_its_class_through_a_rename_and_its_undo()
    {
        var missed = Missed("A", MarshalNoShowRule.LosesBestQualifier);
        _rounds.Add(missed);

        var leaveAlone = _rounds.WithClassName("Stock 2WD");
        _rounds.RenameClass("Stock", "Stock 2WD");

        Assert.Single(MarshalRule.ForfeitsFor("Stock 2WD", _rounds.All, null));
        Assert.Empty(MarshalRule.ForfeitsFor("Stock", _rounds.All, null));

        // Saved with it.
        Assert.Single(MarshalRule.ForfeitsFor("Stock 2WD", RoundDefinitionStore.Load().All, null));

        _rounds.UndoRenameClass("Stock 2WD", "Stock", leaveAlone);
        Assert.Single(MarshalRule.ForfeitsFor("Stock", _rounds.All, null));
    }

    // ------------------------------------------------------------ #25

    private RoundDefinition RunHeat(string label, DateTime started, params (string Transponder, int Laps, long Ms)[] results)
    {
        var round = new RoundDefinition { ClassName = "Stock", RoundLabel = label, Kind = RoundKind.Qualifier };
        _rounds.Add(round);

        var session = new SessionRecord
        {
            Label = label,
            SessionType = RaceSessionType.Race,
            RoundId = round.Id,
            StartedAtUtc = started,
            EndedAtUtc = started.AddMinutes(5),
            Results = results.Select(r => new SessionResultEntry { Transponder = r.Transponder, DriverName = r.Transponder, Laps = r.Laps, ElapsedMs = r.Ms }).ToList(),
        };

        _sessions.Add(session);
        Assert.True(_rounds.MarkRecorded(round.Id, session.Id));
        return round;
    }

    [Fact]
    public void Under_points_the_heat_lost_to_the_marshal_rule_is_not_their_best_heat_or_a_tie_break()
    {
        var q1 = RunHeat("Q1 Heat 1", T0, ("A", 20, 300_000), ("B", 19, 300_000));
        RunHeat("Q2 Heat 1", T0.AddMinutes(10), ("A", 15, 300_000), ("B", 16, 300_000));
        RunHeat("Q3 Heat 1", T0.AddMinutes(20), ("A", 15, 300_000), ("B", 14, 300_000));

        q1.NoShowMarshals = new List<MarshalNoShow> { MarshalRule.Log("A (Turn 1)", "A", "Stock", MarshalNoShowRule.LosesBestQualifier, 0, T0) };
        _rounds.Update(q1);

        var points = new RaceClass { Name = "Stock", QualifyingMethod = QualifyingMethod.RoundPoints, CountBestRounds = 2 };
        var standings = QualifyingCalculator.Compute("Stock", _rounds, SessionHistoryStore.FromSessions(_sessions), _profiles, points);

        var a = standings.Single(e => e.Transponder == "A");
        var b = standings.Single(e => e.Transponder == "B");

        // Level: A Q2 2nd + Q3 1st, B Q2 1st + Q1 2nd - 3 points and a win each.
        Assert.Equal(3, a.Points);
        Assert.Equal(3, b.Points);

        // A's 20 laps were in the Q1 they lost: their best heat left is 15.
        Assert.Equal(15, a.BestLaps);
        Assert.Equal("B", standings[0].Transponder);
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
