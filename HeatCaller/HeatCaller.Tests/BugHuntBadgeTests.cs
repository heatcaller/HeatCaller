namespace HeatCaller.Tests;

/// <summary>
/// Badge and storage bugs found before the club beta: an empty store file
/// that nobody was told about, cars that never turned a lap making up the
/// field for "not alone" badges, and badges announced after a write that
/// never happened.
/// </summary>
public sealed class BugHuntBadgeTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static SessionResultEntry Result(int position, string transponder, int laps, long fastMs = 0) => new()
    {
        Position = position,
        Transponder = transponder,
        DriverName = "Driver " + transponder,
        Laps = laps,
        FastLapMs = fastMs,
        LapTimesMs = Enumerable.Repeat(fastMs > 0 ? fastMs : 10_000L, laps).ToList(),
    };

    private static SessionRecord Race(params SessionResultEntry[] results) => new()
    {
        SessionType = RaceSessionType.Race,
        StartedAtUtc = DateTime.UtcNow,
        Results = results.ToList(),
    };

    // --- An empty store file is reported -----------------------------------------------

    [Fact]
    public void An_empty_race_history_file_is_reported_rather_than_loading_silently_as_a_fresh_install()
    {
        File.WriteAllText(_data.PathOf("race-history.json"), string.Empty);

        var messages = new List<string>();
        void Collect(string message) => messages.Add(message);

        SafeFile.WriteFailed += Collect;
        try
        {
            var store = SessionHistoryStore.Load();

            // Nothing in it to protect, so writes still go ahead as before.
            Assert.False(store.LoadedUnreadable);
        }
        finally
        {
            SafeFile.WriteFailed -= Collect;
        }

        Assert.Contains(messages, m => m.Contains("race-history.json") && m.Contains("empty"));
    }

    [Fact]
    public void A_store_with_no_file_at_all_still_starts_quietly()
    {
        var messages = new List<string>();
        void Collect(string message) => messages.Add(message);

        SafeFile.WriteFailed += Collect;
        try
        {
            SafeFile.PreserveUnreadable(_data.PathOf("never-there.json"), new FileNotFoundException());
        }
        finally
        {
            SafeFile.WriteFailed -= Collect;
        }

        Assert.DoesNotContain(messages, m => m.Contains("never-there.json"));
    }

    // --- Non-starters don't make up the field ------------------------------------------

    [Fact]
    public void One_car_lapping_a_hand_released_grid_alone_is_not_a_win()
    {
        var store = AchievementStore.Load();
        var race = Race(
            Result(1, "100", laps: 8, fastMs: 9_000),
            Result(2, "200", laps: 0),
            Result(3, "300", laps: 0),
            Result(4, "400", laps: 0),
            Result(5, "500", laps: 0),
            Result(6, "600", laps: 0));

        var unlocks = AchievementEvaluator.EvaluateFinishedRace(race, Array.Empty<SessionRecord>(), store);

        var driver = store.For("100");
        Assert.Equal(1, driver.RacesFinished);
        Assert.Equal(0, driver.Wins);
        Assert.Equal(0, driver.Podiums);
        Assert.DoesNotContain(unlocks, u => u.Badge.Id is AchievementCatalogue.FirstWin
            or AchievementCatalogue.FirstPodium
            or AchievementCatalogue.FastestLap
            or AchievementCatalogue.LapClear
            or AchievementCatalogue.FullHouse);
    }

    [Fact]
    public void A_real_two_car_race_is_still_a_win()
    {
        var store = AchievementStore.Load();
        var race = Race(
            Result(1, "100", laps: 8, fastMs: 9_000),
            Result(2, "200", laps: 7, fastMs: 9_500),
            Result(3, "300", laps: 0));

        AchievementEvaluator.EvaluateFinishedRace(race, Array.Empty<SessionRecord>(), store);

        Assert.Equal(1, store.For("100").Wins);
        Assert.True(store.For("100").Has(AchievementCatalogue.FirstWin));
    }

    // --- A failed write announces nothing ----------------------------------------------

    [Fact]
    public void Badges_are_not_returned_for_announcing_when_the_store_could_not_be_saved()
    {
        // A folder where the file should be: it loads as empty, and every
        // write to it fails.
        Directory.CreateDirectory(_data.PathOf("achievements.json"));
        var store = AchievementStore.Load();

        var unlocks = AchievementEvaluator.EvaluateFinishedRace(
            Race(Result(1, "100", laps: 8, fastMs: 9_000), Result(2, "200", laps: 7, fastMs: 9_500)),
            Array.Empty<SessionRecord>(),
            store);

        Assert.False(store.Save());
        Assert.Empty(unlocks);
    }
}
