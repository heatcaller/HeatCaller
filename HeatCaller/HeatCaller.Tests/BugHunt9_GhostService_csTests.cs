namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 9 (22 Oct 2026), GhostService: an Undo of "Bring Sam's best up
/// to date" used to re-read the driver's best again and leave a running
/// ghost at the refreshed pace. Going back to a revision it already ran at
/// now puts that pace back.
/// </summary>
public sealed class BugHunt9_GhostService_csTests
{
    private const string Sam = "101";
    private const string Bill = "202";

    [Fact]
    public void Undoing_a_refresh_puts_a_running_ghost_back_to_the_pace_it_ran_at_before()
    {
        var settings = new GhostSettings();
        var (started, _) = OwnBestGhost.ChaseFor(settings, Sam, "Sam", null);

        var ghosts = new GhostService(new RaceTiming(new RaceControlSession()), ProfileStore.Load(),
            SessionHistoryStore.FromSessions(new[]
            {
                Session(Sam, DateTime.UtcNow.AddMinutes(-3), 9_500),
                Session(Bill, DateTime.UtcNow.AddMinutes(-2), 9_200),
            }))
        {
            Settings = settings,
        };

        var said = new List<string>();
        ghosts.StatusMessage += said.Add;

        try
        {
            ghosts.Start();
            Assert.True(ghosts.IsRunning);
            Assert.Contains(said, line => line.Contains("on track at 9.500s", StringComparison.Ordinal));

            // Revision moved on, now reading a quicker best (9.2s). Bill's
            // session stands in for "Sam set a quicker lap since the green".
            ghosts.Settings = Copy(started, started.PaceRevision + 1, Bill);
            Assert.Equal(1, ghosts.RefreshChangedPaces());
            Assert.Contains(said, line => line.Contains("brought up to 9.200s", StringComparison.Ordinal));

            // Undo: the revision goes back, while a lookup of the best would
            // still say 9.2s. The ghost must go back to the 9.5s it ran at.
            said.Clear();
            ghosts.Settings = Copy(started, started.PaceRevision, Bill);
            Assert.Equal(1, ghosts.RefreshChangedPaces());
            Assert.Contains(said, line => line.Contains("put back to 9.500s", StringComparison.Ordinal));
            Assert.DoesNotContain(said, line => line.Contains("brought up", StringComparison.Ordinal));

            // Nothing further asked for: nothing moves.
            Assert.Equal(0, ghosts.RefreshChangedPaces());
        }
        finally
        {
            ghosts.Stop();
        }
    }

    private static GhostSettings Copy(GhostCar started, int revision, string source) => new()
    {
        Enabled = true,
        Ghosts =
        {
            new GhostCar
            {
                Id = started.Id,
                Transponder = started.Transponder,
                Name = started.Name,
                PaceSource = GhostPaceSource.OwnBestTonight,
                SourceTransponder = source,
                PaceRevision = revision,
            },
        },
    };

    private static SessionRecord Session(string transponder, DateTime startedUtc, long fastLapMs) => new()
    {
        SessionType = RaceSessionType.Practice,
        StartedAtUtc = startedUtc,
        Results =
        {
            new SessionResultEntry
            {
                Transponder = transponder,
                Laps = 5,
                FastLapMs = fastLapMs,
                LapTimesMs = Enumerable.Repeat(fastLapMs, 5).ToList(),
                ElapsedMs = fastLapMs * 5,
            },
        },
    };
}
