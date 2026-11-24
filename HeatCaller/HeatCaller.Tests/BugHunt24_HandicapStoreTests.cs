namespace HeatCaller.Tests;

/// <summary>
/// Bug Hunt 24 (23 Nov 2026), HandicapStore: F142 suggested handicaps use
/// only races on the newest layout, so a layout change is not mistaken for
/// a pace gap.
/// </summary>
public sealed class BugHunt24_HandicapStoreTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly DriverProfile[] Drivers =
    {
        new() { Transponder = "A", DriverName = "Alice" },
        new() { Transponder = "B", DriverName = "Bob" },
        new() { Transponder = "C", DriverName = "Cara" },
    };

    [Fact]
    public void Suggestions_use_only_races_on_the_newest_layout()
    {
        var start = DateTime.UtcNow.AddDays(-20);
        var sessions = new List<SessionRecord>();

        // Five old-layout nights: A and B, both around 14 s.
        for (var i = 0; i < 5; i++)
        {
            sessions.Add(Race("Old", start.AddDays(i), ("A", 14_000), ("B", 14_200)));
        }

        // Five newer nights on a quicker layout: A and C only.
        for (var i = 0; i < 5; i++)
        {
            sessions.Add(Race("New", start.AddDays(10 + i), ("A", 10_500), ("C", 10_800)));
        }

        var history = SessionHistoryStore.FromSessions(sessions);
        var suggestions = HandicapStore.Load().Suggest(Drivers, history, 5, 30);

        // Bob only raced the old layout, so he gets no suggestion rather
        // than a 3.5 s-a-lap credit that is really the layout change.
        Assert.DoesNotContain(suggestions, s => s.Transponder == "B");

        var a = suggestions.Single(s => s.Transponder == "A");
        var c = suggestions.Single(s => s.Transponder == "C");
        Assert.Equal(10.5, a.AverageBestLapSeconds);
        Assert.Equal(0.0, a.SuggestedSeconds);
        Assert.Equal(9.0, c.SuggestedSeconds);
    }

    [Fact]
    public void Suggestions_without_layout_names_still_use_every_race()
    {
        var start = DateTime.UtcNow.AddDays(-10);
        var history = SessionHistoryStore.FromSessions(new[]
        {
            Race("", start, ("A", 14_000), ("B", 15_000)),
            Race("", start.AddDays(1), ("A", 10_000), ("B", 11_000)),
        });

        var suggestions = HandicapStore.Load().Suggest(Drivers, history, 5, 10);

        Assert.Equal(2, suggestions.Single(s => s.Transponder == "A").SessionsUsed);
        Assert.Equal(2, suggestions.Single(s => s.Transponder == "B").SessionsUsed);
        Assert.Equal(10.0, suggestions.Single(s => s.Transponder == "B").SuggestedSeconds);
    }

    [Fact]
    public void On_newest_layout_matches_names_ignoring_case_and_spaces()
    {
        var start = DateTime.UtcNow.AddDays(-10);
        var older = Race(" club oval ", start, ("A", 14_000));
        var other = Race("Infield", start.AddDays(1), ("A", 12_000));
        var newest = Race("Club Oval", start.AddDays(2), ("A", 13_000));

        var kept = HandicapStore.OnNewestLayout(new[] { newest, other, older }).Select(s => s.Id).ToList();

        Assert.Equal(new[] { newest.Id, older.Id }, kept);
    }

    private static SessionRecord Race(string layout, DateTime startedUtc, params (string Transponder, long FastLapMs)[] results) => new()
    {
        Label = "Heat",
        SessionType = RaceSessionType.Race,
        TrackLayoutName = layout,
        StartedAtUtc = startedUtc,
        EndedAtUtc = startedUtc.AddMinutes(5),
        Results = results.Select((r, i) => new SessionResultEntry
        {
            Transponder = r.Transponder,
            DriverName = r.Transponder,
            Position = i + 1,
            Laps = 10,
            ElapsedMs = r.FastLapMs * 10,
            FastLapMs = r.FastLapMs,
        }).ToList(),
    };
}
