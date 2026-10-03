namespace HeatCaller.Tests;

/// <summary>
/// Badges for a home track and a club from one catalogue (Oct 2026): each
/// badge tagged with what it needs, and the tags held to the rules; practice
/// judged at home only; and the home and garage badges - ghosts, practice,
/// setups, packs, cars and days.
/// </summary>
public sealed class AchievementHomeTests : IDisposable
{
    private const string Layout = "Kitchen Oval";

    private static readonly DateTime Day = new(2026, 9, 10, 18, 0, 0, DateTimeKind.Utc);

    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>A session: each row a driver, their laps (the first is the start lap), and their car.</summary>
    private static SessionRecord Session(RaceSessionType type, DateTime start, params (string Who, long[] Laps, string Car)[] rows) => new()
    {
        SessionType = type,
        Label = type == RaceSessionType.Practice ? "Practice" : "Heat",
        TrackLayoutName = Layout,
        StartedAtUtc = start,
        EndedAtUtc = start.AddMinutes(5),
        RecordsIncidents = true,
        Results = rows.Select((r, i) => new SessionResultEntry
        {
            Position = i + 1,
            Transponder = r.Who,
            DriverName = "Driver " + r.Who,
            CarModel = r.Car,
            Laps = r.Laps.Length,
            FastLapMs = r.Laps.Skip(1).DefaultIfEmpty(r.Laps[0]).Min(),
            ElapsedMs = r.Laps.Sum(),
            LapTimesMs = r.Laps.ToList(),
        }).ToList(),
    };

    /// <summary><paramref name="count"/> laps of <paramref name="lapMs"/>, after a start lap a little longer.</summary>
    private static long[] Laps(int count, long lapMs) =>
        new[] { lapMs + 1500 }.Concat(Enumerable.Repeat(lapMs, count - 1)).ToArray();

    private static AchievementContext Home => new() { RacingAt = RacingSetting.Home };

    // ------------------------------------------------------------ the tags

    [Fact]
    public void Racing_alone_never_earns_a_badge_that_needs_somebody_else()
    {
        var store = AchievementStore.Load();
        var history = new List<SessionRecord>();
        var unlocks = new List<AchievementUnlock>();

        // Forty races alone over forty days - quicker, slower, long, short,
        // early and late - enough to earn most of what can be earned alone.
        for (var i = 0; i < 40; i++)
        {
            var lap = 10_000 - (i % 7) * 120 + (i % 3) * 200;
            var race = Session(RaceSessionType.Race, Day.AddDays(i).AddHours(i % 12 - 6), ("A", Laps(12 + i % 25, lap), i % 4 == 0 ? "Red" : "Blue"));

            unlocks.AddRange(AchievementEvaluator.EvaluateFinishedRace(race, history, store, context: Home));
            history.Insert(0, race);
        }

        Assert.NotEmpty(unlocks);
        Assert.All(unlocks, u => Assert.Equal(AchievementNeeds.Alone, u.Badge.Needs));

        // Not a win, and not a podium: nobody else was there.
        Assert.Equal(0, store.For("A").Wins);
        Assert.Equal(0, store.For("A").Podiums);
    }

    [Fact]
    public void Four_friends_racing_for_a_season_never_earn_a_club_sized_badge()
    {
        var store = AchievementStore.Load();
        var history = new List<SessionRecord>();
        var unlocks = new List<AchievementUnlock>();
        var friends = new[] { "A", "B", "C", "D" };

        for (var i = 0; i < 40; i++)
        {
            // Round and round the finishing order, so everybody wins some.
            var order = friends.Skip(i % 4).Concat(friends.Take(i % 4)).ToArray();
            var race = Session(RaceSessionType.Race, Day.AddDays(i),
                order.Select((who, place) => (who, Laps(15 - place, 10_000 + place * 150L), "Car " + who)).ToArray());

            unlocks.AddRange(AchievementEvaluator.EvaluateFinishedRace(race, history, store, context: Home));
            history.Insert(0, race);
        }

        Assert.Contains(unlocks, u => u.Badge.Needs == AchievementNeeds.Rival);
        Assert.DoesNotContain(unlocks, u => u.Badge.Needs == AchievementNeeds.Club);
    }

    [Fact]
    public void A_home_track_is_offered_everything_but_the_club_sized_badges_and_a_club_everything_but_the_home_ones()
    {
        var home = AchievementCatalogue.All.Where(b => AchievementCatalogue.IsOffered(b, RacingSetting.Home)).ToList();
        var club = AchievementCatalogue.All.Where(b => AchievementCatalogue.IsOffered(b, RacingSetting.Club)).ToList();

        Assert.DoesNotContain(home, b => b.Needs == AchievementNeeds.Club);
        Assert.Contains(home, b => b.Id == AchievementCatalogue.PracticeMakesPerfect);
        Assert.Contains(home, b => b.Id == AchievementCatalogue.FirstWin);

        Assert.DoesNotContain(club, b => b.Needs == AchievementNeeds.Home);
        Assert.Contains(club, b => b.Id == AchievementCatalogue.FullHouse);

        // Every id once, and every badge drawn by name.
        Assert.Equal(AchievementCatalogue.All.Count, AchievementCatalogue.All.Select(b => b.Id).Distinct().Count());
    }

    // ------------------------------------------------------------ practice

    [Fact]
    public void Practice_at_home_earns_what_can_be_earned_alone_and_never_a_race_or_a_record()
    {
        var store = AchievementStore.Load();
        // Too short a race for a clean run of ten, so the practice can earn it.
        var race = Session(RaceSessionType.Race, Day, ("A", Laps(9, 10_000), "Red"));
        AchievementEvaluator.EvaluateFinishedRace(race, Array.Empty<SessionRecord>(), store, context: Home);

        var practice = Session(RaceSessionType.Practice, Day.AddDays(1), ("A", Laps(14, 9_700), "Red"));
        var unlocks = AchievementEvaluator.EvaluatePractice(practice, new[] { race }, store, Home);

        Assert.Contains(unlocks, u => u.Badge.Id == AchievementCatalogue.CleanTen);
        Assert.Contains(unlocks, u => u.Badge.Id == AchievementCatalogue.PersonalBest);
        Assert.Contains(unlocks, u => u.Badge.Id == AchievementCatalogue.BigStep);
        Assert.DoesNotContain(unlocks, u => u.Badge.Id is AchievementCatalogue.FirstRace or AchievementCatalogue.TrackRecordHolder);
        Assert.Equal(1, store.For("A").RacesFinished);
        Assert.Equal(23, store.For("A").TotalLaps);

        // And a race after it measures its best against the practice too.
        var later = Session(RaceSessionType.Race, Day.AddDays(2), ("A", Laps(12, 9_800), "Red"));
        Assert.DoesNotContain(
            AchievementEvaluator.EvaluateFinishedRace(later, new[] { practice, race }, store, context: Home),
            u => u.Badge.Id == AchievementCatalogue.PersonalBest);
    }

    [Fact]
    public void Practice_at_a_club_earns_nothing()
    {
        var store = AchievementStore.Load();
        var practice = Session(RaceSessionType.Practice, Day, ("A", Laps(120, 9_700), "Red"));

        Assert.Empty(AchievementEvaluator.EvaluatePractice(practice, Array.Empty<SessionRecord>(), store, new AchievementContext()));
        Assert.Equal(0, store.For("A").TotalLaps);
    }

    [Fact]
    public void A_hundred_practice_laps_and_ten_practice_days()
    {
        var store = AchievementStore.Load();
        var history = new List<SessionRecord>();

        for (var i = 0; i < 9; i++)
        {
            var day = Session(RaceSessionType.Practice, Day.AddDays(i), ("A", Laps(6, 10_000 + i), "Red"));
            Assert.DoesNotContain(AchievementEvaluator.EvaluatePractice(day, history, store, Home),
                u => u.Badge.Id == AchievementCatalogue.RegularAtTheTrack);
            history.Insert(0, day);
        }

        var tenth = Session(RaceSessionType.Practice, Day.AddDays(9), ("A", Laps(100, 10_100), "Red"));
        var unlocks = AchievementEvaluator.EvaluatePractice(tenth, history, store, Home);

        Assert.Contains(unlocks, u => u.Badge.Id == AchievementCatalogue.RegularAtTheTrack);
        Assert.Contains(unlocks, u => u.Badge.Id == AchievementCatalogue.PracticeMakesPerfect);
    }

    // ------------------------------------------------------------ ghosts

    private static GhostCar Ghost(string id, GhostPaceSource source, string of = "", int percent = 100) => new()
    {
        Transponder = id,
        PaceSource = source,
        SourceTransponder = of,
        PacePercent = percent,
    };

    [Fact]
    public void Beating_a_ghost_of_your_own_best_at_full_pace_is_Ghost_Buster()
    {
        var store = AchievementStore.Load();
        var mine = Ghost("GHOST-PB", GhostPaceSource.YourPersonalBest, of: "A");
        var easy = Ghost("GHOST-EASY", GhostPaceSource.YourPersonalBest, of: "B", percent: 102);

        // In practice by best lap: A quicker than both, B quicker than his
        // slowed-down one only.
        var board = new List<(string, int, long)> { ("A", 12, 9_800), ("GHOST-PB", 12, 9_900), ("B", 12, 9_950), ("GHOST-EASY", 12, 10_100) };
        var beaten = AchievementEvaluator.GhostsBeatenBy(board, new[] { mine, easy }, byBestLap: true);

        var practice = Session(RaceSessionType.Practice, Day, ("A", Laps(12, 9_800), "Red"), ("B", Laps(12, 9_950), "Blue"));
        var unlocks = AchievementEvaluator.EvaluatePractice(practice, Array.Empty<SessionRecord>(), store, Home with { GhostsBeaten = beaten });

        Assert.Contains(unlocks, u => u.Transponder == "A" && u.Badge.Id == AchievementCatalogue.GhostBuster);
        Assert.DoesNotContain(unlocks, u => u.Transponder == "B" && u.Badge.Id == AchievementCatalogue.GhostBuster);
    }

    [Fact]
    public void Finishing_a_race_ahead_of_a_live_pace_ghost_is_Shadow_Boxer_and_a_few_laps_ahead_is_not()
    {
        var store = AchievementStore.Load();
        var live = Ghost("GHOST-LIVE", GhostPaceSource.LiveLeaderPace);

        var beaten = AchievementEvaluator.GhostsBeatenBy(
            new List<(string, int, long)> { ("A", 12, 9_800), ("GHOST-LIVE", 11, 9_900) }, new[] { live }, byBestLap: false);

        var race = Session(RaceSessionType.Race, Day, ("A", Laps(12, 9_800), "Red"));
        Assert.Contains(AchievementEvaluator.EvaluateFinishedRace(race, Array.Empty<SessionRecord>(), store, context: Home with { GhostsBeaten = beaten }),
            u => u.Badge.Id == AchievementCatalogue.ShadowBoxer);

        // Three laps in: not enough to count as beating anything.
        Assert.Empty(AchievementEvaluator.GhostsBeatenBy(
            new List<(string, int, long)> { ("A", 3, 9_800), ("GHOST-LIVE", 3, 9_900) }, new[] { live }, byBestLap: false));
    }

    // ------------------------------------------------------------ bests

    [Fact]
    public void Personal_bests_in_three_cars_and_on_two_days_running()
    {
        var store = AchievementStore.Load();
        var history = new List<SessionRecord>
        {
            Session(RaceSessionType.Practice, Day.AddDays(2), ("A", Laps(12, 9_800), "Yellow")),
            Session(RaceSessionType.Practice, Day.AddDays(1), ("A", Laps(12, 9_900), "Blue")),
            Session(RaceSessionType.Practice, Day, ("A", Laps(12, 10_000), "Red")),
        };

        var today = Session(RaceSessionType.Practice, Day.AddDays(3), ("A", Laps(12, 9_700), "Green"));
        var unlocks = AchievementEvaluator.EvaluatePractice(today, history, store, Home);

        // Bests in Blue, Yellow and Green - Red only set the first mark.
        Assert.Contains(unlocks, u => u.Badge.Id == AchievementCatalogue.GarageTour);
        Assert.Contains(unlocks, u => u.Badge.Id == AchievementCatalogue.BackToBack);
    }

    [Fact]
    public void A_best_soon_after_a_setup_on_that_car_is_Dialled_In_and_four_sessions_later_is_not()
    {
        var store = AchievementStore.Load();
        var setups = SetupLogStore.Load();
        setups.Upsert(new SetupEntry { DriverName = "Driver A", CarModel = "Red", RecordedAtUtc = Day.AddDays(1).AddHours(-1) });

        var before = Session(RaceSessionType.Practice, Day, ("A", Laps(12, 10_000), "Red"));
        var soon = Session(RaceSessionType.Practice, Day.AddDays(1), ("A", Laps(12, 9_900), "Red"));

        var context = Home with { Setups = setups };
        Assert.Contains(AchievementEvaluator.EvaluatePractice(soon, new[] { before }, store, context),
            u => u.Badge.Id == AchievementCatalogue.DialledIn);

        var history = new List<SessionRecord> { before };

        for (var i = 1; i <= 3; i++)
        {
            history.Insert(0, Session(RaceSessionType.Practice, Day.AddDays(1).AddHours(i), ("A", Laps(12, 10_050), "Red")));
        }

        var late = Session(RaceSessionType.Practice, Day.AddDays(2), ("A", Laps(12, 9_900), "Red"));
        // A fresh store, so an earlier award cannot hide this one.
        var unlocks = AchievementEvaluator.EvaluatePractice(late, history, new FreshStore().Store, context);

        Assert.Contains(unlocks, u => u.Badge.Id == AchievementCatalogue.PersonalBest);
        Assert.DoesNotContain(unlocks, u => u.Badge.Id == AchievementCatalogue.DialledIn);
    }

    /// <summary>An achievement store with nothing in it, apart from the test's own.</summary>
    private sealed class FreshStore
    {
        public AchievementStore Store { get; }

        public FreshStore()
        {
            File.Delete(Path.Combine(AppPaths.Root, "achievements.json"));
            Store = AchievementStore.Load();
        }
    }

    [Fact]
    public void Three_of_your_own_packs_in_a_day_and_a_best_on_a_pack_you_dont_usually_run()
    {
        var store = AchievementStore.Load();
        var profiles = ProfileStore.Load();
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "A", DriverName = "Ann" }, out _));

        var packs = BatteryPackStore.Load();

        foreach (var id in new[] { "p1", "p2", "p3" })
        {
            packs.Upsert(new BatteryPack { Id = id, Label = id, OwnerTransponder = "A" });
        }

        // Noon UTC, and minutes apart: the same local day wherever the test runs.
        var noonUtc = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
        var history = new List<SessionRecord>();

        for (var i = 0; i < 3; i++)
        {
            var usual = Session(RaceSessionType.Practice, noonUtc.AddDays(-3 + i), ("A", Laps(8, 10_000), "Red"));
            packs.Tag(usual.Id, "A", "p1", PackTagSource.DriverHub);
            history.Insert(0, usual);
        }

        var morning = Session(RaceSessionType.Practice, noonUtc, ("A", Laps(8, 10_050), "Red"));
        var noon = Session(RaceSessionType.Practice, noonUtc.AddMinutes(10), ("A", Laps(8, 10_050), "Red"));
        packs.Tag(morning.Id, "A", "p1", PackTagSource.DriverHub);
        packs.Tag(noon.Id, "A", "p2", PackTagSource.DriverHub);
        history.Insert(0, morning);
        history.Insert(0, noon);

        var evening = Session(RaceSessionType.Practice, noonUtc.AddMinutes(20), ("A", Laps(8, 9_900), "Red"));
        packs.Tag(evening.Id, "A", "p3", PackTagSource.DriverHub);

        var unlocks = AchievementEvaluator.EvaluatePractice(evening, history, store, Home with { Profiles = profiles, Packs = packs });

        Assert.Contains(unlocks, u => u.Badge.Id == AchievementCatalogue.PackRotation);
        Assert.Contains(unlocks, u => u.Badge.Id == AchievementCatalogue.FreshLegs);
    }
}
