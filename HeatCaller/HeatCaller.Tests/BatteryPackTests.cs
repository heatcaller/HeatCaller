namespace HeatCaller.Tests;

/// <summary>
/// Battery packs that belong to drivers, with a pack recorded per driver
/// per session (BatteryPackStore): the pack in the car recorded at filing
/// and never over one picked by hand, the tags from before they were per
/// driver still counting and converted where certain, pace on both, and
/// transfer, retire, delete and the pack in the car - each with its undo
/// (BatteryActions).
/// </summary>
public sealed class BatteryPackTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static SessionRecord Session(string id, params (string Transponder, int Laps, long FastLapMs)[] results) => new()
    {
        Id = id,
        Label = "Heat " + id,
        StartedAtUtc = DateTime.UtcNow.AddMinutes(-int.Parse(id)),
        Results = results.Select(r => new SessionResultEntry { Transponder = r.Transponder, Laps = r.Laps, FastLapMs = r.FastLapMs }).ToList(),
    };

    private sealed class World
    {
        public ProfileStore Profiles { get; } = ProfileStore.Load();

        public BatteryPackStore Packs { get; } = BatteryPackStore.Load();

        public UndoService Undo { get; } = new();

        public World()
        {
            var maya = new DriverProfile { Transponder = "100", DriverName = "Maya" };
            maya.Transponders.Add(new OwnedTransponder { Id = "200" });
            Assert.True(Profiles.TryAdd(maya, out _));
            Assert.True(Profiles.TryAdd(new DriverProfile { Transponder = "500", DriverName = "Dev" }, out _));
        }

        public BatteryPack Add(string id, string owner, bool retired = false)
        {
            var pack = new BatteryPack { Id = id, Label = id, OwnerTransponder = owner, Retired = retired };
            Packs.Upsert(pack);
            return pack;
        }
    }

    [Fact]
    public void Each_driver_in_a_race_is_tagged_with_the_pack_in_their_car_when_it_is_filed()
    {
        var w = new World();
        w.Add("m1", "100");
        w.Add("d1", "500");
        w.Packs.SetCurrent("100", "m1");
        w.Packs.SetCurrent("500", "d1");

        // Maya's row is under her second transponder: still her.
        var heat = Session("1", ("200", 10, 12_000), ("500", 10, 12_500), ("999", 3, 20_000));
        Assert.Equal(2, w.Packs.AutoTag(heat, w.Profiles));

        Assert.Equal("m1", w.Packs.PackFor("1", "100")?.Id);
        Assert.Equal("d1", w.Packs.PackFor("1", "500")?.Id);
        Assert.Null(w.Packs.PackFor("1", "999"));
        Assert.Equal(PackTagSource.InTheCar, w.Packs.TagFor("1", "100")!.Source);

        // A demo race's laps are invented: nothing recorded on it.
        var demo = Session("2", ("100", 10, 12_000));
        demo.IsDemo = true;
        Assert.Equal(0, w.Packs.AutoTag(demo, w.Profiles));
    }

    [Fact]
    public void A_pack_picked_by_hand_is_never_written_over_by_the_one_in_the_car()
    {
        var w = new World();
        w.Add("m1", "100");
        w.Add("m2", "100");
        w.Packs.SetCurrent("100", "m1");

        // Picked on Race Control during the race, before it was filed.
        w.Packs.Tag("1", "100", "m2", PackTagSource.RaceControl);
        Assert.Equal(0, w.Packs.AutoTag(Session("1", ("100", 10, 12_000)), w.Profiles));
        Assert.Equal("m2", w.Packs.PackFor("1", "100")?.Id);

        // A retired pack in the car records nothing; retiring takes it out.
        w.Packs.Upsert(new BatteryPack { Id = "m1", Label = "m1", OwnerTransponder = "100", Retired = true });
        Assert.Null(w.Packs.CurrentPackId("100"));
    }

    [Fact]
    public void A_tag_from_before_counts_for_everyone_and_is_made_a_drivers_own_where_certain()
    {
        // A file from before tags were per driver: "old" only ever on
        // sessions Maya ran alone, "heat" on a heat with Dev, and "mixed" on
        // one of each.
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(_data.PathOf("battery-packs.json"),
            """
            {
              "Packs": [ { "Id": "old", "Label": "Old pack" }, { "Id": "heat", "Label": "Heat pack" }, { "Id": "mixed", "Label": "Mixed" } ],
              "SessionPacks": { "1": "old", "2": "old", "3": "heat", "4": "mixed", "5": "mixed" }
            }
            """);

        var w = new World();
        var history = SessionHistoryStore.FromSessions(new[]
        {
            Session("1", ("200", 10, 12_000), ("999", 0, 0)),
            Session("2", ("100", 10, 12_300)),
            Session("3", ("100", 10, 11_900), ("500", 10, 12_600)),
            Session("4", ("100", 10, 12_800)),
            Session("5", ("100", 10, 12_600), ("500", 10, 12_900)),
        });

        Assert.True(w.Packs.Get("old")!.IsClubPack);
        Assert.Equal("old", w.Packs.PackFor("1", "100")?.Id);
        Assert.Equal("heat", w.Packs.PackFor("3", "500")?.Id);

        Assert.Equal((3, 1), w.Packs.ConvertOnce(history, w.Profiles));

        // Sessions one driver ran alone are hers now; the pack only ever
        // tagged for her is hers too. The heats stay session-wide, and still
        // count for both of them - and a pack still on one was shared, so it
        // stays the club's.
        Assert.Equal(PackTagSource.Converted, w.Packs.TagFor("1", "100")!.Source);
        Assert.Equal("100", w.Packs.Get("old")!.OwnerTransponder);
        Assert.True(w.Packs.Get("heat")!.IsClubPack);
        Assert.True(w.Packs.Get("mixed")!.IsClubPack);
        Assert.Equal(PackTagSource.Converted, w.Packs.TagFor("4", "100")!.Source);
        Assert.True(w.Packs.IsSessionWideTag("5", "500"));
        Assert.True(w.Packs.IsSessionWideTag("3", "500"));
        Assert.Equal("heat", w.Packs.PackFor("3", "500")?.Id);
        Assert.Equal("heat", w.Packs.PackFor("3", "100")?.Id);

        // "No pack recorded" picked by hand hides the session-wide one.
        w.Packs.Tag("3", "500", null, PackTagSource.DriverHub);
        Assert.Null(w.Packs.PackFor("3", "500"));

        // Once only, and kept in the file.
        Assert.Equal((0, 0), w.Packs.ConvertOnce(history, w.Profiles));
        Assert.Equal((0, 0), BatteryPackStore.Load().ConvertOnce(history, w.Profiles));

        // Pace: each session's best lap, on whichever kind of tag.
        var pace = w.Packs.ComparePace("100", history, 1, w.Profiles);
        Assert.Equal(new[] { "heat", "old", "mixed" }, pace.Select(p => p.Pack.Id));
        Assert.Equal(12.7, pace[2].AverageBestLapSeconds);
        Assert.Equal(2, pace[1].Sessions);
        Assert.Equal(12.15, pace[1].AverageBestLapSeconds);
        Assert.Equal(0.25, pace[1].GapToBestPackSeconds);
    }

    [Fact]
    public void A_transfer_moves_the_pack_out_of_the_old_owners_car_and_undo_puts_it_back()
    {
        var w = new World();
        w.Add("m1", "100");
        w.Packs.SetCurrent("100", "m1");
        w.Packs.Tag("1", "100", "m1", PackTagSource.InTheCar);

        Assert.Null(w.Packs.Transfer("m1", "500"));

        var moved = w.Packs.Get("m1")!;
        Assert.Equal("500", moved.OwnerTransponder);
        Assert.Equal("100", Assert.Single(moved.Transfers).FromTransponder);
        Assert.Null(w.Packs.CurrentPackId("100"));
        Assert.Equal("m1", w.Packs.PackFor("1", "100")?.Id);
        Assert.Empty(w.Packs.OwnedBy(w.Profiles.Get("100")));
        Assert.Single(w.Packs.OwnedBy(w.Profiles.Get("500")));

        // Already theirs: refused, nothing changed.
        Assert.NotNull(w.Packs.Transfer("m1", "500"));

        // Undo, by way of the memo the desk takes before a transfer.
        w.Add("p", "100");
        w.Packs.SetCurrent("100", "p");
        var before = w.Packs.Remember("p");
        Assert.Null(w.Packs.Transfer("p", string.Empty));
        Assert.True(w.Packs.Get("p")!.IsClubPack);
        Assert.Null(w.Packs.CurrentPackId("100"));
        Assert.True(w.Packs.PutBack(before));
        Assert.Equal("100", w.Packs.Get("p")!.OwnerTransponder);
        Assert.Empty(w.Packs.Get("p")!.Transfers);
        Assert.Equal("p", w.Packs.CurrentPackId("100"));
    }

    [Fact]
    public void Deleting_a_pack_takes_its_tags_and_undo_brings_them_back_without_writing_over_newer_ones()
    {
        var w = new World();
        w.Add("m1", "100");
        w.Add("m2", "100");
        w.Packs.SetCurrent("100", "m1");
        w.Packs.Tag("1", "100", "m1", PackTagSource.InTheCar);
        w.Packs.Tag("2", "100", "m1", PackTagSource.InTheCar);

        var before = w.Packs.Remember("m1");
        w.Packs.Delete("m1");

        Assert.Null(w.Packs.Get("m1"));
        Assert.Null(w.Packs.TagFor("1", "100"));
        Assert.Null(w.Packs.CurrentPackId("100"));

        // Session 2 given another pack since: it keeps that.
        w.Packs.Tag("2", "100", "m2", PackTagSource.DriverHub);

        Assert.True(w.Packs.PutBack(before, wasDeleted: true));
        Assert.Equal("m1", w.Packs.PackFor("1", "100")?.Id);
        Assert.Equal("m2", w.Packs.PackFor("2", "100")?.Id);
        Assert.Equal("m1", w.Packs.CurrentPackId("100"));

        // Back already: a second undo has nothing to do.
        Assert.False(w.Packs.PutBack(before, wasDeleted: true));
    }

    [Fact]
    public void The_pack_in_the_car_and_a_race_control_pick_each_undo_as_one_step()
    {
        var w = new World();
        w.Add("m1", "100");
        w.Add("m2", "100");

        Assert.True(BatteryActions.PutInCar(w.Packs, w.Undo, "100", "Maya", "m1"));
        Assert.False(BatteryActions.PutInCar(w.Packs, w.Undo, "100", "Maya", "m1"));

        // Picked on Race Control after the flag: the race, and the car.
        w.Packs.Tag("1", "100", "m1", PackTagSource.InTheCar);
        BatteryActions.TagSession(w.Packs, w.Undo, "1", "Heat 1", "100", "Maya", "m2", PackTagSource.RaceControl, intoCar: true);

        Assert.Equal("m2", w.Packs.PackFor("1", "100")?.Id);
        Assert.Equal("m2", w.Packs.CurrentPackId("100"));

        Assert.NotNull(w.Undo.Undo());
        Assert.Equal("m1", w.Packs.PackFor("1", "100")?.Id);
        Assert.Equal(PackTagSource.InTheCar, w.Packs.TagFor("1", "100")!.Source);
        Assert.Equal("m1", w.Packs.CurrentPackId("100"));

        // The car changed again since: undoing the first pick is refused.
        w.Packs.SetCurrent("100", "m2");
        Assert.Null(w.Undo.Undo());
        Assert.Equal("m2", w.Packs.CurrentPackId("100"));
    }

    [Fact]
    public void A_driver_can_run_their_own_packs_and_the_clubs_but_not_someone_elses()
    {
        var w = new World();
        w.Add("m1", "100");
        w.Add("m-old", "100", retired: true);
        w.Add("club", string.Empty);
        w.Add("d1", "500");

        var choices = w.Packs.ChoicesFor(w.Profiles.Get("100")).Select(p => p.Id);
        Assert.Equal(new[] { "m1", "club" }, choices);

        // A pack owned under her second transponder is hers too.
        w.Add("m-alt", "200");
        Assert.Contains("m-alt", w.Packs.ChoicesFor(w.Profiles.Get("100")).Select(p => p.Id));

        Assert.Equal("100", BatteryPackStore.DriverKey(w.Profiles, " 200 "));
        Assert.Equal("777", BatteryPackStore.DriverKey(w.Profiles, "777"));

        // Saved and read back whole.
        w.Packs.SetCurrent("500", "club");
        var again = BatteryPackStore.Load();
        Assert.Equal("club", again.CurrentPackId("500"));
        Assert.Equal("100", again.Get("m1")!.OwnerTransponder);
        Assert.Equal(new[] { "500" }, again.InCarsOf("club"));
    }
}
