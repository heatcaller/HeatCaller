namespace HeatCaller.Tests;

/// <summary>
/// Track Record (2 Oct bug hunt): every car that went under the old record
/// in the same race was named the record holder, and each had the layout
/// counted towards Three Track Records - not just the quickest of them.
/// </summary>
public sealed class BugHuntAchievementsTests : IDisposable
{
    private const string Layout = "Club Oval";

    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Only_the_quickest_car_under_the_old_record_takes_it()
    {
        // The record is 10.000. Alice runs 9.800 and Bob 9.900 - both
        // under it, but only Alice holds it after tonight.
        var tonight = Race(DateTime.UtcNow,
            Entry("ALICE", position: 1, fast: 9_800),
            Entry("BOB", position: 2, fast: 9_900),
            Entry("CAROL", position: 3, fast: 10_500));

        var unlocks = Evaluate(tonight, OldRecord());

        Assert.Contains(unlocks, u => u.Transponder == "ALICE" && u.Badge.Id == AchievementCatalogue.TrackRecordHolder);
        Assert.DoesNotContain(unlocks, u => u.Transponder == "BOB" && u.Badge.Id == AchievementCatalogue.TrackRecordHolder);
        Assert.DoesNotContain(unlocks, u => u.Transponder == "CAROL" && u.Badge.Id == AchievementCatalogue.TrackRecordHolder);
    }

    [Fact]
    public void A_ghost_quicker_than_everyone_does_not_take_the_record_off_a_driver()
    {
        var tonight = Race(DateTime.UtcNow,
            Entry("GHOST-PACE", position: 1, fast: 9_000),
            Entry("ALICE", position: 2, fast: 9_800),
            Entry("BOB", position: 3, fast: 9_900));

        var unlocks = Evaluate(tonight, OldRecord());

        Assert.Contains(unlocks, u => u.Transponder == "ALICE" && u.Badge.Id == AchievementCatalogue.TrackRecordHolder);
        Assert.DoesNotContain(unlocks, u => u.Transponder == "BOB" && u.Badge.Id == AchievementCatalogue.TrackRecordHolder);
    }

    // --- Helpers ---------------------------------------------------------------

    private static SessionRecord OldRecord() => Race(DateTime.UtcNow.AddDays(-7),
        Entry("DAVE", position: 1, fast: 10_000),
        Entry("EVE", position: 2, fast: 10_400));

    private static SessionRecord Race(DateTime startedUtc, params SessionResultEntry[] results) => new()
    {
        Label = "A-Main",
        SessionType = RaceSessionType.Race,
        TrackLayoutName = Layout,
        StartedAtUtc = startedUtc.AddMinutes(-6),
        EndedAtUtc = startedUtc,
        Results = results.ToList(),
    };

    private static SessionResultEntry Entry(string transponder, int position, long fast) => new()
    {
        Transponder = transponder,
        DriverName = transponder,
        Position = position,
        StartPosition = position,
        Laps = 12,
        ElapsedMs = 300_000 + position * 1_000,
        FastLapMs = fast,
        LapTimesMs = Enumerable.Repeat(fast + 500, 11).Append(fast).Prepend(3_000L).ToList(),
    };

    private static List<AchievementUnlock> Evaluate(SessionRecord record, params SessionRecord[] history) =>
        AchievementEvaluator.EvaluateFinishedRace(record, history, AchievementStore.Load());
}
