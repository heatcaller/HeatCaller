namespace HeatCaller.Tests;

/// <summary>
/// Badges that read the grid (25 Sep audit): the stored start slot is the
/// roster's, ghosts included, while places are counted among the real cars -
/// so a ghost at the top of the roster cost the real polesitter Pole
/// Converted and handed somebody else a Charge they never made.
/// </summary>
public sealed class AchievementGridTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_ghost_at_the_top_of_the_roster_does_not_take_pole_off_the_real_front_row()
    {
        var unlocks = Evaluate(GhostOnPole());

        Assert.Contains(unlocks, u => u.Transponder == "ALICE" && u.Badge.Id == AchievementCatalogue.PoleConverted);
    }

    [Fact]
    public void Places_made_up_are_counted_from_the_real_grid()
    {
        // Dave: sixth in the roster, fifth among the real cars, second at
        // the flag - three places, not four.
        var unlocks = Evaluate(GhostOnPole());

        Assert.DoesNotContain(unlocks, u => u.Transponder == "DAVE" && u.Badge.Id == AchievementCatalogue.Charge);
    }

    [Fact]
    public void Without_a_ghost_the_same_race_scores_as_it_always_did()
    {
        // The control: the same field with no ghost at all. Alice from the
        // front still converts, and Dave from fifth to second is three.
        var record = GhostOnPole();
        record.Results.RemoveAll(r => GhostService.IsGhost(r.Transponder));
        foreach (var r in record.Results)
        {
            r.StartPosition--;
        }

        var unlocks = Evaluate(record);

        Assert.Contains(unlocks, u => u.Transponder == "ALICE" && u.Badge.Id == AchievementCatalogue.PoleConverted);
        Assert.DoesNotContain(unlocks, u => u.Transponder == "DAVE" && u.Badge.Id == AchievementCatalogue.Charge);
    }

    [Fact]
    public void A_real_charge_from_the_back_still_counts()
    {
        var record = GhostOnPole();

        // Eve: last of the real cars (real slot 5), wins it - four places.
        foreach (var r in record.Results.Where(r => r.Transponder != "EVE"))
        {
            r.Position++;
        }

        Result(record, "EVE").Position = 1;
        Result(record, "EVE").StartPosition = 6;
        Result(record, "DAVE").StartPosition = 5;

        var unlocks = Evaluate(record);

        Assert.Contains(unlocks, u => u.Transponder == "EVE" && u.Badge.Id == AchievementCatalogue.Charge);
    }

    // --- Helpers ---------------------------------------------------------------

    /// <summary>
    /// A ghost on the top roster slot, five real cars behind it. Alice starts
    /// first among the real cars and wins; Dave starts last-but-one and is
    /// second.
    /// </summary>
    private static SessionRecord GhostOnPole() => new()
    {
        Label = "A-Main",
        SessionType = RaceSessionType.Race,
        StartedAtUtc = DateTime.UtcNow.AddMinutes(-6),
        EndedAtUtc = DateTime.UtcNow,
        RecordsIncidents = true,
        Results = new List<SessionResultEntry>
        {
            Entry("ALICE", position: 1, start: 2, laps: 12, fast: 20_000),
            Entry("DAVE", position: 2, start: 6, laps: 12, fast: 20_500),
            Entry("GHOST-PACE", position: 3, start: 1, laps: 12, fast: 19_000),
            Entry("BOB", position: 4, start: 3, laps: 11, fast: 21_000),
            Entry("CAROL", position: 5, start: 4, laps: 11, fast: 21_500),
            Entry("EVE", position: 6, start: 5, laps: 10, fast: 22_000),
        },
    };

    private static SessionResultEntry Entry(string transponder, int position, int start, int laps, long fast) => new()
    {
        Transponder = transponder,
        DriverName = transponder,
        Position = position,
        StartPosition = start,
        Laps = laps,
        ElapsedMs = 300_000 + position * 1_000,
        FastLapMs = fast,
        LapTimesMs = Enumerable.Repeat(fast + 500, laps).Prepend(3_000L).ToList(),
    };

    private static SessionResultEntry Result(SessionRecord record, string transponder) =>
        record.Results.Single(r => r.Transponder == transponder);

    private static List<AchievementUnlock> Evaluate(SessionRecord record) =>
        AchievementEvaluator.EvaluateFinishedRace(record, Array.Empty<SessionRecord>(), AchievementStore.Load());
}
