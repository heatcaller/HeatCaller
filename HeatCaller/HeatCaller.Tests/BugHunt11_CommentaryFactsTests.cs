namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11 (5 Nov 2026): "the last N races on this layout have all
/// gone to Bill" must not be offered once a race on that layout tonight has
/// gone to somebody else. Tonight's races still do not EXTEND a run (see
/// LayoutLegendTests.Tonights_own_races_are_not_the_clubs_history_yet), but
/// they can END one.
/// </summary>
public sealed class BugHunt11_CommentaryFactsTests : IDisposable
{
    // Declared first: every store below loads from wherever it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>Seven in the evening, which NightOf reads as that day's race night.</summary>
    private static readonly DateTime Now = new(2026, 10, 11, 19, 0, 0, DateTimeKind.Local);

    private const string Oval = "Club Oval";

    private static SessionRecord Raced(DateTime endedLocal, params (string Transponder, int Position)[] results) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            SessionType = RaceSessionType.Race,
            TrackLayoutName = Oval,
            StartedAtUtc = endedLocal.ToUniversalTime().AddMinutes(-6),
            EndedAtUtc = endedLocal.ToUniversalTime(),
            Results = results
                .Select(r => new SessionResultEntry
                {
                    Transponder = r.Transponder,
                    DriverName = r.Transponder,
                    Position = r.Position,
                    FastLapMs = 11_500 + (r.Position * 100),
                    Laps = 12,
                })
                .ToList(),
        };

    private static ProfileStore ThreeDrivers()
    {
        var profiles = ProfileStore.Load();

        profiles.Upsert(new DriverProfile { Transponder = "A", DriverName = "Sam" });
        profiles.Upsert(new DriverProfile { Transponder = "B", DriverName = "Bill" });
        profiles.Upsert(new DriverProfile { Transponder = "C", DriverName = "Carl" });

        return profiles;
    }

    /// <summary>Six weekly race nights on the Oval, Bill winning the last four of them, plus tonight's races.</summary>
    private static SessionHistoryStore SixNightsAnd(params SessionRecord[] tonight) =>
        SessionHistoryStore.FromSessions(new[]
        {
            Raced(Now.AddDays(-42), ("A", 1), ("B", 2), ("C", 3)),
            Raced(Now.AddDays(-35), ("C", 1), ("A", 2), ("B", 3)),
            Raced(Now.AddDays(-28), ("B", 1), ("A", 2), ("C", 3)),
            Raced(Now.AddDays(-21), ("B", 1), ("A", 2), ("C", 3)),
            Raced(Now.AddDays(-14), ("B", 1), ("A", 2), ("C", 3)),
            Raced(Now.AddDays(-7), ("B", 1), ("A", 2), ("C", 3)),
        }.Concat(tonight).ToList());

    [Fact]
    public void A_run_somebody_else_ended_tonight_is_not_said_at_a_later_intro()
    {
        // Sam beat Bill on the Oval in tonight's first heat.
        var history = SixNightsAnd(Raced(Now.AddHours(-1), ("A", 1), ("B", 2), ("C", 3)));

        var legend = CommentaryReaders.LayoutStory(history, ThreeDrivers(), Oval, new[] { "A" }, Now);

        Assert.NotNull(legend);
        Assert.True(legend!.RunEndedTonight);

        Assert.Null(CommentaryRaceFacts.WhoWinsHere(legend, "night-1"));
        Assert.DoesNotContain(
            CommentaryRaceFacts.ForTheLayout(legend, "night-1", most: 99),
            b => b.Text.Contains("gone to", StringComparison.Ordinal));
    }

    [Fact]
    public void A_run_still_standing_tonight_is_still_said()
    {
        // No race yet tonight, or one Bill won again (which does not extend
        // the run, but does not end it either), or a two-car race that
        // neither makes nor breaks it.
        foreach (var tonight in new[]
        {
            Array.Empty<SessionRecord>(),
            new[] { Raced(Now.AddHours(-1), ("B", 1), ("A", 2), ("C", 3)) },
            new[] { Raced(Now.AddHours(-1), ("A", 1), ("B", 2)) },
        })
        {
            var legend = CommentaryReaders.LayoutStory(SixNightsAnd(tonight), ThreeDrivers(), Oval, new[] { "A" }, Now);

            Assert.NotNull(legend);
            Assert.False(legend!.RunEndedTonight);
            Assert.Equal(4, legend.WinnerRun);

            var block = CommentaryRaceFacts.WhoWinsHere(legend, "night-1");

            Assert.NotNull(block);
            Assert.Contains("gone to Bill", block!.Text);
        }
    }
}
