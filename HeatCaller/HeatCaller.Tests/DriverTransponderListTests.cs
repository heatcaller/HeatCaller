using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// A driver owning any number of transponders (DriverProfile.Transponders):
/// older files' single alt transponder carried over, every one of them
/// finding the driver, retired ones included, a transponder never shared
/// between two drivers, and the lap pipeline keeping two of one driver's
/// cars on track apart.
/// </summary>
public sealed class DriverTransponderListTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void An_older_file_with_an_alt_transponder_reads_as_one_entry_and_writes_it_back()
    {
        var old = """[{"Transponder":"100","DriverName":"Maya","AlternateTransponder":"200"}]""";
        var profile = JsonSerializer.Deserialize<List<DriverProfile>>(old)![0];

        Assert.Equal(new[] { "100", "200" }, profile.AllTransponderIds);
        Assert.Equal("200", profile.AlternateTransponder);

        // Written back with both, read again with one entry, not two.
        var again = JsonSerializer.Deserialize<DriverProfile>(JsonSerializer.Serialize(profile))!;
        Assert.Single(again.Transponders);
        Assert.Equal("200", again.AlternateTransponder);
    }

    [Fact]
    public void Setting_the_alt_transponder_only_ever_adds_and_puts_it_first()
    {
        var profile = new DriverProfile { Transponder = "100" };
        profile.Transponders.Add(new OwnedTransponder { Id = "200" });
        profile.Transponders.Add(new OwnedTransponder { Id = "300", Retired = true });

        profile.AlternateTransponder = "400";
        Assert.Equal("400", profile.AlternateTransponder);
        Assert.Equal(new[] { "100", "400", "200", "300" }, profile.AllTransponderIds);

        // Empty, and the main one, change nothing.
        profile.AlternateTransponder = string.Empty;
        profile.AlternateTransponder = "100";
        Assert.Equal(new[] { "100", "400", "200", "300" }, profile.AllTransponderIds);

        // One of theirs already, retired: back in use, at the front, once.
        profile.AlternateTransponder = "300";
        Assert.Equal("300", profile.AlternateTransponder);
        Assert.False(profile.IsRetired("300"));
        Assert.Equal(new[] { "100", "300", "400", "200" }, profile.AllTransponderIds);
    }

    [Fact]
    public void Every_transponder_finds_the_driver_retired_ones_too()
    {
        var store = ProfileStore.Load();
        var maya = new DriverProfile { Transponder = "100", DriverName = "Maya" };
        maya.Transponders.Add(new OwnedTransponder { Id = "200" });
        maya.Transponders.Add(new OwnedTransponder { Id = "300", Retired = true });
        Assert.True(store.TryAdd(maya, out _));

        Assert.Equal("Maya", store.Get("200")?.DriverName);
        Assert.Equal("Maya", store.Get("300")?.DriverName);
        Assert.Equal("100", store.PrimaryForAlternate("200"));
        Assert.Equal("100", store.PrimaryForAlternate("300"));
        Assert.Null(store.PrimaryForAlternate("100"));
        Assert.Same(maya, store.OwnerOf("300"));

        // And after a restart.
        var reloaded = ProfileStore.Load();
        Assert.Equal("100", reloaded.PrimaryForAlternate("300"));
        Assert.True(reloaded.Get("100")!.IsRetired("300"));
    }

    [Fact]
    public void A_transponder_cannot_belong_to_two_drivers()
    {
        var store = ProfileStore.Load();
        var maya = new DriverProfile { Transponder = "100", DriverName = "Maya" };
        maya.Transponders.Add(new OwnedTransponder { Id = "200" });
        maya.Transponders.Add(new OwnedTransponder { Id = "300" });
        Assert.True(store.TryAdd(maya, out _));

        // Dev's extra is one of Maya's extras.
        var dev = new DriverProfile { Transponder = "500", DriverName = "Dev" };
        dev.Transponders.Add(new OwnedTransponder { Id = "300" });
        Assert.False(store.TryAdd(dev, out var refusal));
        Assert.Contains("Maya", refusal);

        // Dev's main is one of Maya's extras.
        Assert.NotNull(store.DescribeClash(new DriverProfile { Transponder = "200" }, editing: null));

        // Maya can be saved with her own.
        Assert.Null(store.DescribeClash(maya, editing: "100"));
    }

    [Fact]
    public void Add_driver_refuses_an_extra_two_drivers_already_list()
    {
        // An older file: 5521 is an extra on both Alex and Ben, so the
        // timing index leaves it out and Get finds nobody.
        var store = ProfileStore.Load();
        var alex = new DriverProfile { Transponder = "100", DriverName = "Alex" };
        alex.Transponders.Add(new OwnedTransponder { Id = "5521" });
        var ben = new DriverProfile { Transponder = "200", DriverName = "Ben" };
        ben.Transponders.Add(new OwnedTransponder { Id = "5521" });
        store.Upsert(alex);
        store.Upsert(ben);
        Assert.Null(store.Get("5521"));

        // The Add Driver window's check still names a claimant ...
        var ownerName = QuickDriverForm.OwnerNameIn(store);
        Assert.Contains(ownerName("5521"), new[] { "Alex", "Ben" });
        Assert.Null(ownerName("999"));

        // ... and the save behind it refuses rather than making it Chris's main.
        Assert.False(store.TryAdd(new DriverProfile { Transponder = "5521", DriverName = "Chris" }, out var refusal));
        Assert.NotEmpty(refusal);
        Assert.NotEqual("Chris", store.OwnerOf("5521")?.DriverName);
    }

    [Fact]
    public void A_retired_main_transponder_stays_the_drivers_key()
    {
        var store = ProfileStore.Load();
        var maya = new DriverProfile { Transponder = "100", DriverName = "Maya" };
        maya.Transponders.Add(new OwnedTransponder { Id = "200" });
        maya.EntryToChange("100").Retired = true;
        Assert.True(store.TryAdd(maya, out _));

        var reloaded = ProfileStore.Load().Get("100")!;
        Assert.True(reloaded.IsRetired("100"));
        Assert.False(reloaded.IsRetired("200"));
        Assert.Equal("200", reloaded.AlternateTransponder);
        Assert.Equal(new[] { "100", "200" }, reloaded.AllTransponderIds);
    }

    [Fact]
    public void A_drivers_cars_include_ones_owned_under_another_of_their_transponders()
    {
        var cars = CarStore.Load();
        cars.Upsert(new Car { Id = "a", Name = "Main car", OwnerTransponder = "100" });
        cars.Upsert(new Car { Id = "b", Name = "Spare", OwnerTransponder = "200" });
        cars.Upsert(new Car { Id = "c", Name = "Old", OwnerTransponder = "300", Retired = true });
        cars.Upsert(new Car { Id = "d", Name = "Dev's", OwnerTransponder = "500" });

        var maya = new DriverProfile { Transponder = "100", DriverName = "Maya" };
        maya.Transponders.Add(new OwnedTransponder { Id = "200" });
        maya.Transponders.Add(new OwnedTransponder { Id = "300", Retired = true });

        Assert.Equal(new[] { "a", "b", "c" }, cars.OwnedBy(maya).Select(c => c.Id).OrderBy(id => id));
    }

    private static void Pass(RaceControlSession race, string car, uint deviceTimeMs) =>
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = car,
            DeviceTimeMs = deviceTimeMs,
            SignalHits = 60,
            SignalStrength = 247,
        });

    private static RaceControlSession RaceWithOwners(Func<string, string?> owner)
    {
        var race = new RaceControlSession { AlternateTransponderOwner = owner };
        race.Stage();
        race.StartCountdown();
        race.GreenFlag(100_000);
        return race;
    }

    [Fact]
    public void Two_of_one_drivers_transponders_out_together_are_timed_as_two_cars()
    {
        // 200 and 300 are both Maya's (main 100); 100 itself is not out.
        var race = RaceWithOwners(id => id is "200" or "300" ? "100" : null);

        Pass(race, "200", 101_000);
        Pass(race, "300", 101_500);

        var rows = race.GetOrderedStandings().Select(s => s.TransponderId).ToList();

        // The first is filed under her main one; the second is a car of its own.
        Assert.Contains("100", rows);
        Assert.Contains("300", rows);
        Assert.DoesNotContain("200", rows);
    }

    [Fact]
    public void A_transponder_given_a_driver_mid_race_keeps_its_own_row()
    {
        var owners = new Dictionary<string, string>();
        var race = RaceWithOwners(id => owners.TryGetValue(id, out var main) ? main : null);

        // Unknown at first: timed under its own id.
        Pass(race, "900", 101_000);

        // Then given to Maya from the board's menu.
        owners["900"] = "100";
        Pass(race, "900", 108_000);

        var rows = race.GetOrderedStandings().Select(s => s.TransponderId).ToList();
        Assert.Contains("900", rows);
        Assert.DoesNotContain("100", rows);
    }

    [Fact]
    public void The_unknown_list_holds_each_transponder_once_and_later_lasts_until_a_new_one()
    {
        var unknowns = new UnknownTransponders();
        var changes = 0;
        unknowns.Changed += () => changes++;

        unknowns.Note("B70E21", "Heat 2");
        unknowns.Note("b70e21", "Heat 3");
        Assert.Single(unknowns.Pending);

        unknowns.SetLaps("B70E21", 14, 7905);
        Assert.Equal("14 laps in Heat 2 · best 7.905", UnknownTransponders.Describe(unknowns.Pending[0]));

        unknowns.Snooze();
        Assert.True(unknowns.Snoozed);

        unknowns.Note("0D4C93", "Practice");
        Assert.False(unknowns.Snoozed);
        Assert.Equal(2, unknowns.Pending.Count);

        unknowns.DropKnown(id => id == "0D4C93");
        unknowns.Resolve("B70E21");
        Assert.Empty(unknowns.Pending);
        Assert.True(changes >= 6);
    }
}
