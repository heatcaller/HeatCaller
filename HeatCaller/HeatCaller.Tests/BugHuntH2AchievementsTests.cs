namespace HeatCaller.Tests;

/// <summary>
/// Track Record badge (2 Oct bug hunt, second pass): the badge ignored the
/// rule the live record follows. A guest marked "Exclude from track records"
/// (or a rental) with the quickest lap took Record Holder off the club driver
/// who really set the record, and a ghost's or excluded guest's lap in an
/// older saved race set a bar nobody could ever beat for the badge.
/// </summary>
public sealed class BugHuntH2AchievementsTests : IDisposable
{
    private const string Layout = "Club Oval";

    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void An_excluded_guest_does_not_take_the_record_badge_off_the_club_driver()
    {
        var profiles = Profiles(new DriverProfile { Transponder = "GUEST", DriverName = "Guest", ExcludeFromRecords = true });

        var tonight = Race(DateTime.UtcNow,
            Entry("GUEST", position: 1, fast: 9_700),
            Entry("ALICE", position: 2, fast: 9_800),
            Entry("BOB", position: 3, fast: 9_900));

        var unlocks = Evaluate(tonight, profiles, OldRecord());

        Assert.DoesNotContain(unlocks, u => u.Transponder == "GUEST" && u.Badge.Id == AchievementCatalogue.TrackRecordHolder);
        Assert.Contains(unlocks, u => u.Transponder == "ALICE" && u.Badge.Id == AchievementCatalogue.TrackRecordHolder);
        Assert.DoesNotContain(unlocks, u => u.Transponder == "BOB" && u.Badge.Id == AchievementCatalogue.TrackRecordHolder);
    }

    [Fact]
    public void A_rental_does_not_take_the_record_badge()
    {
        var profiles = Profiles(new DriverProfile { Transponder = "RENTAL1", DriverName = "Rental 1", IsRental = true });

        var tonight = Race(DateTime.UtcNow,
            Entry("RENTAL1", position: 1, fast: 9_700),
            Entry("ALICE", position: 2, fast: 9_800));

        var unlocks = Evaluate(tonight, profiles, OldRecord());

        Assert.DoesNotContain(unlocks, u => u.Transponder == "RENTAL1" && u.Badge.Id == AchievementCatalogue.TrackRecordHolder);
        Assert.Contains(unlocks, u => u.Transponder == "ALICE" && u.Badge.Id == AchievementCatalogue.TrackRecordHolder);
    }

    [Fact]
    public void A_ghost_lap_in_an_older_race_is_not_the_bar_to_beat()
    {
        // The old race carries a ghost row quicker than anyone real ever
        // ran; the real record there is Dave's 10.000.
        var old = Race(DateTime.UtcNow.AddDays(-7),
            Entry("GHOST-PACE", position: 1, fast: 9_000),
            Entry("DAVE", position: 2, fast: 10_000));

        var tonight = Race(DateTime.UtcNow,
            Entry("ALICE", position: 1, fast: 9_800),
            Entry("BOB", position: 2, fast: 9_900));

        var unlocks = Evaluate(tonight, Profiles(), old);

        Assert.Contains(unlocks, u => u.Transponder == "ALICE" && u.Badge.Id == AchievementCatalogue.TrackRecordHolder);
    }

    [Fact]
    public void An_excluded_guest_lap_in_an_older_race_is_not_the_bar_to_beat()
    {
        var profiles = Profiles(new DriverProfile { Transponder = "GUEST", DriverName = "Guest", ExcludeFromRecords = true });

        var old = Race(DateTime.UtcNow.AddDays(-7),
            Entry("GUEST", position: 1, fast: 9_000),
            Entry("DAVE", position: 2, fast: 10_000));

        var tonight = Race(DateTime.UtcNow,
            Entry("ALICE", position: 1, fast: 9_800),
            Entry("BOB", position: 2, fast: 9_900));

        var unlocks = Evaluate(tonight, profiles, old);

        Assert.Contains(unlocks, u => u.Transponder == "ALICE" && u.Badge.Id == AchievementCatalogue.TrackRecordHolder);
    }

    // --- Helpers ---------------------------------------------------------------

    private static ProfileStore Profiles(params DriverProfile[] drivers)
    {
        var store = ProfileStore.Load();
        foreach (var driver in drivers)
        {
            Assert.True(store.TryAdd(driver, out _));
        }

        return store;
    }

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

    private static List<AchievementUnlock> Evaluate(SessionRecord record, ProfileStore profiles, params SessionRecord[] history) =>
        AchievementEvaluator.EvaluateFinishedRace(record, history, AchievementStore.Load(), profiles: profiles);
}
