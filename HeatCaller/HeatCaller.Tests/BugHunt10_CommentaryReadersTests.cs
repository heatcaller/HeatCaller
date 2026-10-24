namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10, CommentaryReaders. What the hunt found:
///
///  - The grid's head-to-heads kept the three most one-sided pairs BEFORE
///    the builder's five-meeting bar was applied, so a newcomer's 2-0s
///    filled every slot and the club's real 9-1 rivalry was never said.
///  - The qualifying report announced pole from the provisional order
///    after the first heat, because nothing checked the class's heats were
///    all run. The wiring in MainForm is a window to drive; what is pinned
///    here is the reader it now calls.
/// </summary>
public sealed class BugHunt10_CommentaryReadersTests : IDisposable
{
    // Declared first: every store below loads from wherever it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly DateTime Now = new(2026, 10, 11, 19, 0, 0, DateTimeKind.Local);

    private const string Stock = "Box Stock";

    private static SessionResultEntry Row(string transponder, int position) => new()
    {
        Transponder = transponder,
        DriverName = transponder,
        Position = position,
        Laps = 12,
    };

    private static SessionRecord Race(DateTime endedLocal, params SessionResultEntry[] results) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        SessionType = RaceSessionType.Race,
        StartedAtUtc = endedLocal.ToUniversalTime().AddMinutes(-6),
        EndedAtUtc = endedLocal.ToUniversalTime(),
        Results = results.ToList(),
    };

    // ------------------------------------------------------------------
    // The grid's rivalries: the builder's bar before the cap
    // ------------------------------------------------------------------

    [Fact]
    public void A_newcomers_small_perfect_records_do_not_push_the_clubs_real_rivalry_out()
    {
        var profiles = ProfileStore.Load();

        foreach (var (t, name) in new[] { ("B", "Bill"), ("S", "Sam"), ("A", "Ann"), ("D", "Dan") })
        {
            profiles.Upsert(new DriverProfile { Transponder = t, DriverName = name });
        }

        var races = new List<SessionRecord>();

        // Eight races of just Bill and Sam: Bill ahead in seven.
        for (var week = 1; week <= 8; week++)
        {
            races.Add(week == 4
                ? Race(Now.AddDays(-7 * week), Row("S", 1), Row("B", 2))
                : Race(Now.AddDays(-7 * week), Row("B", 1), Row("S", 2)));
        }

        // Ann and Dan's two races, behind Bill and Sam both times: every
        // pair involving them is a perfect 2-0.
        races.Add(Race(Now.AddDays(-1), Row("B", 1), Row("S", 2), Row("A", 3), Row("D", 4)));
        races.Add(Race(Now.AddDays(-2), Row("B", 1), Row("S", 2), Row("A", 3), Row("D", 4)));

        var (grid, rivalries) = CommentaryReaders.GridHistory(
            new[] { "B", "S", "A", "D" }, null, profiles, SessionHistoryStore.FromSessions(races), RoundDefinitionStore.Load(), DateTime.UtcNow);

        var pair = Assert.Single(rivalries);
        Assert.Equal(new[] { "B", "S" }, new[] { pair.AId, pair.BId }.OrderBy(x => x).ToArray());
        Assert.Equal(10, pair.AAhead + pair.BAhead);

        var facts = CommentaryHistoryFacts.ForIntro(grid, rivalries, "race-1");

        Assert.Contains(facts, f => f.Text.Contains("Bill has finished ahead of Sam 9-1"));
    }

    [Theory]
    [InlineData(2, 0, false)]  // too few meetings, however one-sided
    [InlineData(4, 0, false)]
    [InlineData(9, 1, true)]
    [InlineData(7, 2, true)]   // 0.78
    [InlineData(4, 3, false)]  // neither one-sided nor level
    [InlineData(6, 6, true)]   // dead level over plenty
    [InlineData(2, 2, false)]  // level, but only four meetings
    public void The_pre_filter_matches_the_builders_own_bar(int aAhead, int bAhead, bool expected)
    {
        Assert.Equal(expected, CommentaryReaders.IsRivalry(aAhead, bAhead));

        var built = CommentaryHistoryFacts.Rivalry(new HeadToHead("a", "b", aAhead, bAhead), id => id, "race-1");

        Assert.Equal(expected, built is not null);
    }

    // ------------------------------------------------------------------
    // The qualifying report: nothing until the heats are all run
    // ------------------------------------------------------------------

    [Fact]
    public void The_qualifying_report_names_no_pole_until_every_heat_is_recorded()
    {
        var rounds = RoundDefinitionStore.Load();
        var profiles = ProfileStore.Load();
        var sessions = new List<SessionRecord>();

        var heats = Enumerable.Range(1, 4)
            .Select(n => new RoundDefinition { ClassName = Stock, RoundLabel = $"Q{n}", Kind = RoundKind.Qualifier })
            .ToList();

        foreach (var heat in heats)
        {
            rounds.Add(heat);
        }

        void Run(RoundDefinition heat, string transponder)
        {
            var session = new SessionRecord
            {
                Label = heat.RoundLabel,
                SessionType = RaceSessionType.Race,
                RoundId = heat.Id,
                StartedAtUtc = DateTime.UtcNow,
                Results = new List<SessionResultEntry>
                {
                    new() { Transponder = transponder, DriverName = transponder, Laps = 10, ElapsedMs = 300_000 },
                },
            };

            sessions.Add(session);
            Assert.True(rounds.MarkRecorded(heat.Id, session.Id));
        }

        Run(heats[0], "Sam");

        Assert.False(CommentaryReaders.QualifyingFinished(Stock, rounds));
        Assert.Empty(CommentaryReaders.FinishedQualifying(Stock, rounds, SessionHistoryStore.FromSessions(sessions), profiles));

        // The ungated reader still has the provisional order - MidMeeting
        // depends on that.
        Assert.NotEmpty(CommentaryReaders.Qualifying(Stock, rounds, SessionHistoryStore.FromSessions(sessions), profiles));

        Run(heats[1], "Sam");
        Run(heats[2], "Sam");
        Run(heats[3], "Bill");

        Assert.True(CommentaryReaders.QualifyingFinished(Stock, rounds));
        Assert.NotEmpty(CommentaryReaders.FinishedQualifying(Stock, rounds, SessionHistoryStore.FromSessions(sessions), profiles));
    }

    [Fact]
    public void A_class_with_no_heats_or_no_name_is_never_finished()
    {
        var rounds = RoundDefinitionStore.Load();

        Assert.False(CommentaryReaders.QualifyingFinished(Stock, rounds));
        Assert.False(CommentaryReaders.QualifyingFinished("  ", rounds));
        Assert.False(CommentaryReaders.QualifyingFinished(null, rounds));
    }
}
