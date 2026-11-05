namespace HeatCaller.Tests;

/// <summary>
/// A driver's packs compared (PackAnalytics): clean laps as raced, the fade
/// across a race and its profile in tenths, the gap to the best pack among
/// those with enough races, the layout filter and the layouts offered, and
/// what never counts - demo and imported races, crash laps, other drivers.
/// </summary>
public sealed class PackAnalyticsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private sealed class World
    {
        public ProfileStore Profiles { get; } = ProfileStore.Load();

        public BatteryPackStore Packs { get; } = BatteryPackStore.Load();

        public List<SessionRecord> Sessions { get; } = new();

        public World()
        {
            var maya = new DriverProfile { Transponder = "100", DriverName = "Maya" };
            maya.Transponders.Add(new OwnedTransponder { Id = "200" });
            Assert.True(Profiles.TryAdd(maya, out _));
            Assert.True(Profiles.TryAdd(new DriverProfile { Transponder = "500", DriverName = "Dev" }, out _));
        }

        public void Pack(string id) => Packs.Upsert(new BatteryPack { Id = id, Label = id, OwnerTransponder = "100" });

        /// <summary>A race <paramref name="hoursAgo"/> on <paramref name="pack"/>; the laps in seconds, a start lap of 3s put in front.</summary>
        public SessionRecord Race(string pack, int hoursAgo, params double[] laps) => Race(pack, hoursAgo, "Club", "100", laps);

        public SessionRecord Race(string pack, int hoursAgo, string layout, string transponder, params double[] laps)
        {
            var times = new List<long> { 3_000 };
            times.AddRange(laps.Select(l => (long)Math.Round(l * 1000)));

            var session = new SessionRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Label = "Heat",
                TrackLayoutName = layout,
                StartedAtUtc = DateTime.UtcNow.AddHours(-hoursAgo),
                Results = new List<SessionResultEntry>
                {
                    new() { Transponder = transponder, Laps = laps.Length, FastLapMs = laps.Length == 0 ? 0 : times.Skip(1).Min(), LapTimesMs = times },
                    new() { Transponder = "500", Laps = 5, FastLapMs = 9_000, LapTimesMs = new List<long> { 3_000, 9_000, 9_000, 9_000, 9_000, 9_000 } },
                },
            };

            Sessions.Add(session);
            Packs.Tag(session.Id, "100", pack, PackTagSource.DriverHub);
            return session;
        }

        public PackReport Report(PackFilter? filter = null) =>
            PackAnalytics.For("100", Packs, SessionHistoryStore.FromSessions(Sessions), Profiles, filter);
    }

    private static double[] Same(double lap, int count) => Enumerable.Repeat(lap, count).ToArray();

    [Fact]
    public void A_race_that_slows_down_has_a_positive_fade_and_a_rising_profile()
    {
        var w = new World();
        w.Pack("a");
        w.Race("a", 1, 10.0, 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7, 10.8, 10.9, 11.0, 11.1);

        var pack = Assert.Single(w.Report().Packs);
        var run = Assert.Single(pack.Runs);

        // Thirds of four laps: median 10.95 at the end against 10.15 at the start.
        Assert.Equal(0.8, run.Fade, 3);
        Assert.Equal(0.8, pack.Fade, 3);
        Assert.Equal(10.55, pack.MedianLap, 3);
        Assert.Equal(12, pack.Laps);
        Assert.Equal(PackAnalytics.ProfileBuckets, pack.Profile.Count);
        Assert.True(pack.Profile[0] < 0 && pack.Profile[^1] > 0);
        Assert.True(pack.Profile.Zip(pack.Profile.Skip(1)).All(p => p.Second >= p.First));
    }

    [Fact]
    public void A_crash_lap_is_left_out_but_the_start_lap_never_counts()
    {
        var w = new World();
        w.Pack("a");
        w.Race("a", 1, 10.0, 10.0, 10.0, 30.0, 10.0, 10.0, 10.0, 10.0, 0);

        var pack = Assert.Single(w.Report().Packs);

        Assert.Equal(7, pack.Laps);
        Assert.Equal(10.0, pack.BestLap, 3);
        Assert.Equal(10.0, pack.MedianLap, 3);
        Assert.Equal(0, pack.Spread, 3);
        Assert.Equal(0, pack.Fade, 3);
    }

    [Fact]
    public void Demo_and_imported_races_never_count()
    {
        var w = new World();
        w.Pack("a");
        w.Race("a", 1, Same(10.0, 6));
        w.Race("a", 2, Same(9.0, 6)).IsDemo = true;
        w.Race("a", 3, Same(9.0, 6)).ImportedFrom = "LiveRC";

        var pack = Assert.Single(w.Report().Packs);

        Assert.Equal(1, pack.Sessions);
        Assert.Equal(10.0, pack.BestLap, 3);
        Assert.False(pack.Enough);
    }

    [Fact]
    public void The_gap_is_to_the_best_pack_with_enough_races()
    {
        var w = new World();
        w.Pack("a");
        w.Pack("b");
        w.Pack("c");

        for (var i = 0; i < 3; i++)
        {
            w.Race("a", 10 + i, Same(10.0, 6));
            w.Race("b", 20 + i, Same(10.2, 6));
        }

        // Quicker, but on one race only: not the yardstick yet.
        w.Race("c", 1, Same(9.5, 6));

        var packs = w.Report().Packs;

        Assert.Equal(new[] { "c", "a", "b" }, packs.Select(p => p.Pack.Id));
        Assert.Equal(new[] { false, true, true }, packs.Select(p => p.Enough));
        Assert.Equal(new[] { -0.5, 0, 0.2 }, packs.Select(p => p.GapToBestPack));
        Assert.Equal(3, packs[1].Runs.Count);
        Assert.True(packs[1].Runs[0].StartedUtc < packs[1].Runs[2].StartedUtc);
    }

    [Fact]
    public void With_no_pack_trusted_yet_the_gap_is_to_the_quickest_of_them_all()
    {
        var one = new World();
        one.Pack("a");
        one.Pack("b");
        one.Race("a", 1, Same(10.0, 6));
        one.Race("b", 2, Same(10.3, 6));

        Assert.Equal(new[] { 0, 0.3 }, one.Report().Packs.Select(p => p.GapToBestPack));
    }

    [Fact]
    public void The_layout_filter_narrows_the_races_but_every_layout_is_still_offered_newest_first()
    {
        var w = new World();
        w.Pack("a");
        w.Race("a", 30, "Club", "100", Same(10.0, 6));
        w.Race("a", 20, "Outdoor", "100", Same(12.0, 6));
        w.Race("a", 10, "club", "100", Same(10.2, 6));
        w.Race("a", 5, "", "100", Same(11.0, 6));

        var all = w.Report();
        Assert.Equal(new[] { "club", "Outdoor" }, all.Layouts);
        Assert.Equal(4, all.Packs[0].Sessions);

        var club = w.Report(new PackFilter(Layout: "CLUB"));
        Assert.Equal(all.Layouts, club.Layouts);
        Assert.Equal(2, club.Packs[0].Sessions);
        Assert.Equal(10.1, club.Packs[0].MedianLap, 3);

        Assert.Empty(w.Report(new PackFilter(SessionType: "Main")).Packs);
        Assert.Equal(4, w.Report(new PackFilter(SessionType: "practice")).Packs[0].Sessions);
        Assert.Equal(2, w.Report(new PackFilter(SinceUtc: DateTime.UtcNow.AddHours(-15))).Packs[0].Sessions);
    }

    [Fact]
    public void A_short_race_fills_every_tenth_and_one_too_short_counts_only_for_its_best_lap()
    {
        var w = new World();
        w.Pack("a");
        w.Race("a", 1, 10.0, 10.2, 10.4, 10.6, 10.8);
        w.Race("a", 2, 9.8, 10.0);

        var pack = Assert.Single(w.Report().Packs);

        Assert.Equal(2, pack.Sessions);
        Assert.Equal(9.8, pack.BestLap, 3);
        Assert.Equal(5, pack.Laps);
        Assert.Equal(10.4, pack.MedianLap, 3);

        var shortRun = pack.Runs[0];
        Assert.Empty(shortRun.Profile);
        Assert.Equal(0, shortRun.MedianLap);

        var run = pack.Runs[1];
        Assert.Equal(PackAnalytics.ProfileBuckets, run.Profile.Count);
        Assert.Equal(PackAnalytics.ProfileBuckets, pack.Profile.Count);
        Assert.Equal(-0.4, run.Profile[0], 3);
        Assert.Equal(0.4, run.Profile[^1], 3);
    }

    [Fact]
    public void A_race_under_the_drivers_second_transponder_is_theirs()
    {
        var w = new World();
        w.Pack("a");
        w.Race("a", 1, "Club", "200", Same(10.0, 6));
        w.Race("a", 2, "Club", "100", Same(10.4, 6));

        var pack = Assert.Single(w.Report().Packs);

        Assert.Equal(2, pack.Sessions);
        Assert.Equal(10.0, pack.BestLap, 3);

        // Asked for under the second transponder, the same driver.
        var again = PackAnalytics.For("200", w.Packs, SessionHistoryStore.FromSessions(w.Sessions), w.Profiles);
        Assert.Equal(2, Assert.Single(again.Packs).Sessions);
    }

    [Fact]
    public void An_empty_history_or_a_deleted_pack_gives_nothing()
    {
        var w = new World();
        var empty = w.Report();

        Assert.Empty(empty.Packs);
        Assert.Empty(empty.Layouts);

        w.Pack("a");
        w.Race("a", 1, Same(10.0, 6));
        w.Packs.Delete("a");

        Assert.Empty(w.Report().Packs);
        Assert.Empty(PackAnalytics.For("", w.Packs, SessionHistoryStore.FromSessions(w.Sessions), w.Profiles).Packs);
    }
}
