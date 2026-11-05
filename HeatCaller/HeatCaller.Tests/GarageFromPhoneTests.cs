using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// THE DRIVER'S GARAGE FROM THEIR PHONE (5 Nov 2026, asked for by Asher:
/// "what they input on the relay should update 1:1 on the battery section of
/// the app ... They should be able to add new battery packs and
/// remove/retire old battery packs from the relay ... build all of it").
/// Every change is applied by itself (DriverRequestDesk.FileWaitingWrites),
/// only to the driver's own packs and cars, with an Undo step - and the
/// garage document the phone reads is built from the same stores.
/// </summary>
public sealed class GarageFromPhoneTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string SamBadge = "0123456789abcdef0123";
    private const string DevBadge = "fedcba9876543210fedc";
    private const string PackId = "0123456789abcdef0123456789abcdef";
    private const string CarId = "33333333333333333333333333333333";

    private sealed class World
    {
        private int _next;

        public ProfileStore Profiles { get; } = ProfileStore.Load();
        public DriverRequestStore Store { get; } = DriverRequestStore.Load();
        public BatteryPackStore Packs { get; } = BatteryPackStore.Load();
        public SetupLogStore Setups { get; } = SetupLogStore.Load();
        public CarStore Cars { get; } = CarStore.Load();
        public ChassisTypeStore Chassis { get; } = ChassisTypeStore.Load();
        public ChassisTemplateStore Sheets { get; } = ChassisTemplateStore.Load();
        public UndoService Undo { get; } = new();
        public SessionHistoryStore History { get; } = SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>());
        public DriverRequestDesk Desk { get; }

        public World()
        {
            Assert.True(Profiles.TryAdd(new DriverProfile { Transponder = "100", DriverName = "Sam Parker", ClassName = "Stock" }, out _));
            Assert.True(Profiles.TryAdd(new DriverProfile { Transponder = "500", DriverName = "Dev Shah", ClassName = "Stock" }, out _));
            var badges = new Dictionary<string, string> { [SamBadge] = "100", [DevBadge] = "500" };

            Desk = new DriverRequestDesk(Store, new AppSettings(), Profiles, Packs, History, Undo, _ => { },
                badge => badges.TryGetValue(badge, out var t) ? t : null)
            {
                Setups = Setups,
                Cars = Cars,
                ChassisTypes = Chassis,
                Sheets = Sheets,
                TrackName = () => "Club Oval",
                CarPhotos = new PhotoStore("CarPhotos"),
            };
        }

        public DriverRequest Ask(string kind, Dictionary<string, string> texts, string badge = SamBadge, DateTime? at = null)
        {
            var ask = new DriverRequest
            {
                Id = (++_next).ToString("x24"),
                Badge = badge,
                Kind = kind,
                AskedUtc = at ?? DateTime.UtcNow,
                Proof = DriverAsk.ByPin,
            };

            foreach (var (k, v) in texts)
            {
                ask.Texts[k] = v;
            }

            Store.FileAll(new[] { ask }, DateTime.UtcNow, out _);
            Desk.FileWaitingWrites();
            return Store.Get(ask.Id)!;
        }

        public SessionRecord Race(string label, string layout = "Club Oval")
        {
            var session = new SessionRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Label = label,
                TrackLayoutName = layout,
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-30),
                Results = new List<SessionResultEntry>
                {
                    new() { Transponder = "100", Laps = 4, FastLapMs = 10_000, LapTimesMs = new List<long> { 3_000, 10_000, 10_100, 10_200, 10_300 } },
                },
            };

            Assert.True(History.Add(session));
            return session;
        }
    }

    // ------------------------------------------------------------- packs

    [Fact]
    public void A_pack_added_on_the_phone_is_theirs_goes_in_the_car_and_undoes()
    {
        var w = new World();
        var ask = w.Ask(DriverAsk.PackEdit, new() { ["op"] = "add", ["pack"] = PackId, ["label"] = "Blue 2", ["chem"] = "LiPo" });

        Assert.Equal(DriverAsk.Approved, ask.State);
        var pack = w.Packs.Get(PackId)!;
        Assert.Equal("Blue 2", pack.Label);
        Assert.Equal("LiPo", pack.Chemistry);
        Assert.Equal("100", pack.OwnerTransponder);
        Assert.Equal(PackId, w.Packs.CurrentPackId("100"));

        w.Undo.Undo();
        Assert.Null(w.Packs.Get(PackId));
    }

    [Fact]
    public void A_pack_with_no_races_is_removed_and_one_with_races_is_retired_instead()
    {
        var w = new World();
        w.Packs.Upsert(new BatteryPack { Id = PackId, Label = "Old", OwnerTransponder = "100" });
        w.Packs.Upsert(new BatteryPack { Id = new string('b', 32), Label = "Used", OwnerTransponder = "100" });
        w.Packs.Tag(w.Race("Heat 1").Id, "100", new string('b', 32), PackTagSource.InTheCar);

        Assert.Equal(DriverAsk.Approved, w.Ask(DriverAsk.PackEdit, new() { ["op"] = "remove", ["pack"] = PackId }).State);
        Assert.Null(w.Packs.Get(PackId));

        var used = w.Ask(DriverAsk.PackEdit, new() { ["op"] = "remove", ["pack"] = new string('b', 32) });
        Assert.Equal(DriverAsk.Approved, used.State);
        Assert.Contains("retired instead", used.Said, StringComparison.Ordinal);
        Assert.True(w.Packs.Get(new string('b', 32))!.Retired);
    }

    [Fact]
    public void Somebody_elses_pack_is_never_changed()
    {
        var w = new World();
        w.Packs.Upsert(new BatteryPack { Id = PackId, Label = "Dev's", OwnerTransponder = "500" });

        var ask = w.Ask(DriverAsk.PackEdit, new() { ["op"] = "retire", ["pack"] = PackId });

        Assert.Equal(DriverAsk.Declined, ask.State);
        Assert.False(w.Packs.Get(PackId)!.Retired);
    }

    [Fact]
    public void The_pack_they_ran_a_race_on_is_recorded_from_the_phone_but_never_over_the_desks_later_pick()
    {
        var w = new World();
        w.Packs.Upsert(new BatteryPack { Id = PackId, Label = "Blue", OwnerTransponder = "100" });
        var race = w.Race("Heat 2");

        var ask = w.Ask(DriverAsk.Pack, new() { ["pack"] = PackId, ["race"] = race.Id });

        Assert.Equal(DriverAsk.Approved, ask.State);
        Assert.Equal(PackId, w.Packs.PackIdFor(race.Id, "100"));
        Assert.Equal(PackTagSource.Phone, w.Packs.TagFor(race.Id, "100")!.Source);
        Assert.Null(w.Packs.CurrentPackId("100"));

        // The desk sets it afterwards; a phone ask from before that is refused.
        w.Packs.Tag(race.Id, "100", null, PackTagSource.RaceControl);
        var late = w.Ask(DriverAsk.Pack, new() { ["pack"] = PackId, ["race"] = race.Id }, at: DateTime.UtcNow.AddMinutes(-5));

        Assert.Equal(DriverAsk.Declined, late.State);
        Assert.Null(w.Packs.PackIdFor(race.Id, "100"));
    }

    [Fact]
    public void The_pack_in_the_car_changes_by_itself_now()
    {
        var w = new World();
        w.Packs.Upsert(new BatteryPack { Id = PackId, Label = "Blue", OwnerTransponder = "100" });

        Assert.Equal(DriverAsk.Approved, w.Ask(DriverAsk.Pack, new() { ["pack"] = PackId }).State);
        Assert.Equal(PackId, w.Packs.CurrentPackId("100"));
    }

    // -------------------------------------------------------------- cars

    [Fact]
    public void A_car_added_and_edited_on_the_phone_has_every_field_and_racing_tonight_sets_the_active_car()
    {
        var w = new World();
        var chassis = new ChassisType { Name = "MR-03", Maker = "Kyosho" };
        w.Chassis.Upsert(chassis);

        Assert.Equal(DriverAsk.Approved, w.Ask(DriverAsk.Car, new()
        {
            ["op"] = "add", ["car"] = CarId, ["name"] = "Red Rocket", ["chassis"] = chassis.Id,
            ["cls"] = "Stock", ["body"] = "Porsche 911", ["number"] = "27", ["notes"] = "Fresh tyres",
        }).State);

        var car = w.Cars.Get(CarId)!;
        Assert.Equal("Red Rocket", car.Name);
        Assert.Equal("100", car.OwnerTransponder);
        Assert.Equal(chassis.Id, car.ChassisTypeId);
        Assert.Equal("Porsche 911", car.Bodyshell);
        Assert.Equal("27", car.RaceNumber);
        Assert.Equal("Fresh tyres", car.Notes);

        Assert.Equal(DriverAsk.Approved, w.Ask(DriverAsk.Car, new() { ["op"] = "edit", ["car"] = CarId, ["body"] = "Audi R8" }).State);
        Assert.Equal("Audi R8", w.Cars.Get(CarId)!.Bodyshell);
        Assert.Equal("Red Rocket", w.Cars.Get(CarId)!.Name);

        Assert.Equal(DriverAsk.Approved, w.Ask(DriverAsk.Car, new() { ["op"] = "tonight", ["car"] = CarId }).State);
        Assert.Equal("Red Rocket", w.Profiles.Get("100")!.CarModel);
    }

    [Fact]
    public void A_race_number_another_driver_has_in_the_class_is_refused()
    {
        var w = new World();
        w.Cars.Upsert(new Car { Name = "Dev's", OwnerTransponder = "500", ClassName = "Stock", RaceNumber = "7" });

        var ask = w.Ask(DriverAsk.Car, new() { ["op"] = "add", ["car"] = CarId, ["name"] = "Mine", ["cls"] = "Stock", ["number"] = "7" });

        Assert.Equal(DriverAsk.Declined, ask.State);
        Assert.Contains("already taken", ask.Said, StringComparison.Ordinal);
        Assert.Null(w.Cars.Get(CarId));
    }

    [Fact]
    public void A_chassis_not_on_the_list_is_kept_in_the_cars_notes_and_a_retired_car_comes_back()
    {
        var w = new World();
        w.Ask(DriverAsk.Car, new() { ["op"] = "add", ["car"] = CarId, ["name"] = "Mine", ["chassisOther"] = "Atomic AMZ" });
        Assert.Contains("Atomic AMZ", w.Cars.Get(CarId)!.Notes, StringComparison.Ordinal);

        w.Ask(DriverAsk.Car, new() { ["op"] = "retire", ["car"] = CarId });
        Assert.True(w.Cars.Get(CarId)!.Retired);

        w.Ask(DriverAsk.Car, new() { ["op"] = "unretire", ["car"] = CarId });
        Assert.False(w.Cars.Get(CarId)!.Retired);
    }

    [Fact]
    public void A_photo_from_the_phone_is_added_to_their_car_and_can_be_made_the_cover_or_removed()
    {
        var w = new World();
        w.Cars.Upsert(new Car { Id = CarId, Name = "Mine", OwnerTransponder = "100", PhotoFileNames = new List<string> { "aaaaaaaaaaaa.jpg" } });

        var id = new string('c', 24);
        Assert.True(PhonePhotos.Keep(id, TinyJpeg()));

        var ask = new DriverRequest { Id = id, Badge = SamBadge, Kind = DriverAsk.Photo, AskedUtc = DateTime.UtcNow };
        ask.Texts["car"] = CarId;
        w.Store.FileAll(new[] { ask }, DateTime.UtcNow, out _);
        w.Desk.FileWaitingWrites();

        Assert.Equal(DriverAsk.Approved, w.Store.Get(id)!.State);
        var photos = w.Cars.Get(CarId)!.PhotoFileNames;
        Assert.Equal(2, photos.Count);
        Assert.False(File.Exists(PhonePhotos.PathOf(id)), "the waiting copy is gone once it is on the car");

        w.Ask(DriverAsk.Car, new() { ["op"] = "cover", ["car"] = CarId, ["photo"] = photos[1] });
        Assert.Equal(photos[1], w.Cars.Get(CarId)!.PhotoFileNames[0]);

        w.Ask(DriverAsk.Car, new() { ["op"] = "unphoto", ["car"] = CarId, ["photo"] = "aaaaaaaaaaaa.jpg" });
        Assert.Single(w.Cars.Get(CarId)!.PhotoFileNames);
    }

    // ------------------------------------------------------------ setups

    [Fact]
    public void A_whole_sheet_from_the_phone_is_stored_as_the_setup_sheet_form_would_store_it()
    {
        var w = new World();
        var sheet = new ChassisTemplate { Name = "Club sheet" };
        sheet.Sections.Add(new SetupSection
        {
            Name = "Suspension",
            Fields =
            {
                new SetupField { Id = "spring", Label = "Front spring", Kind = SetupFieldKind.Choice, Options = { "Soft(Red)", "Hard(Yellow)" } },
                new SetupField { Id = "extras", Label = "Extras", Kind = SetupFieldKind.MultiChoice, Options = { "Ball diff", "Bearings" } },
                new SetupField { Id = "wheelbase", Label = "Wheelbase", Kind = SetupFieldKind.Number, Unit = "mm" },
                new SetupField { Id = "gyro", Label = "Gyro", Kind = SetupFieldKind.Boolean },
            },
        });
        w.Sheets.Upsert(sheet);

        var chassis = new ChassisType { Name = "MR-03", SetupSheetId = sheet.Id };
        w.Chassis.Upsert(chassis);
        w.Cars.Upsert(new Car { Id = CarId, Name = "Mine", OwnerTransponder = "100", ChassisTypeId = chassis.Id });
        var race = w.Race("Heat 3", "Winter");

        var ask = w.Ask(DriverAsk.Setup, new()
        {
            ["car"] = CarId, ["sheet"] = sheet.Id, ["race"] = race.Id, ["notes"] = "Loose on exit",
            ["v.spring"] = "soft(red)", ["v.extras"] = "bearings, Ball diff", ["v.wheelbase"] = "94", ["v.gyro"] = "Yes", ["v.nope"] = "x",
        });

        Assert.Equal(DriverAsk.Approved, ask.State);
        Assert.Contains("1 value", ask.Said, StringComparison.Ordinal);

        var entry = Assert.Single(w.Setups.All);
        Assert.Equal(CarId, entry.CarId);
        Assert.Equal(sheet.Id, entry.ChassisTemplateId);
        Assert.Equal(race.Id, entry.LinkedSessionId);
        Assert.Equal("Winter", entry.TrackLayoutName);
        Assert.Equal("Soft(Red)", entry.Values["spring"]);
        Assert.Equal("Bearings, Ball diff", entry.Values["extras"]);
        Assert.Equal("94", entry.Values["wheelbase"]);
        Assert.Equal("true", entry.Values["gyro"]);
        Assert.Equal("Loose on exit", entry.Notes);

        // Fixed from the phone the same night: the entry itself changes.
        w.Ask(DriverAsk.Setup, new() { ["car"] = CarId, ["sheet"] = sheet.Id, ["entry"] = entry.Id, ["v.spring"] = "Hard(Yellow)" });
        Assert.Equal("Hard(Yellow)", Assert.Single(w.Setups.All).Values["spring"]);
    }

    [Fact]
    public void The_short_log_lands_on_the_car_the_phone_named()
    {
        var w = new World();
        w.Cars.Upsert(new Car { Id = CarId, Name = "Mine", OwnerTransponder = "100" });

        Assert.Equal(DriverAsk.Approved, w.Ask(DriverAsk.Setup, new() { ["car"] = CarId, ["tires"] = "Sweep 32" }).State);

        var entry = Assert.Single(w.Setups.All);
        Assert.Equal(CarId, entry.CarId);
        Assert.Equal("Sweep 32", entry.Tires);
    }

    [Fact]
    public void The_inbox_keeps_the_garage_fields_and_sheet_values()
    {
        var json = "{\"items\":[{\"id\":\"" + new string('1', 24) + "\",\"b\":\"" + SamBadge + "\",\"k\":\"setup\",\"v\":\"pin\",\"at\":\"2026-11-05T19:00:00Z\","
                   + "\"f\":{\"car\":\"" + CarId + "\",\"sheet\":\"" + PackId + "\",\"v.front-spring\":\"Soft\",\"v.bad id\":\"x\"}},"
                   + "{\"id\":\"" + new string('2', 24) + "\",\"b\":\"" + SamBadge + "\",\"k\":\"packedit\",\"v\":\"pin\",\"at\":\"2026-11-05T19:00:00Z\","
                   + "\"f\":{\"op\":\"add\",\"pack\":\"" + PackId + "\",\"label\":\"Blue\",\"chem\":\"LiPo\"}}]}";

        var found = DriverRequestStore.ParseInbox(json, DateTime.UtcNow, out var unusable);

        Assert.Empty(unusable);
        Assert.Equal(2, found.Count);
        Assert.Equal(CarId, found[0].Text("car"));
        Assert.Equal("Soft", found[0].Text("v.front-spring"));
        Assert.Equal(string.Empty, found[0].Text("v.bad id"));
        Assert.Equal(DriverAsk.PackEdit, found[1].Kind);
        Assert.Equal("Blue", found[1].Text("label"));
    }

    // ---------------------------------------------------- the garage doc

    [Fact]
    public void The_garage_the_phone_reads_has_their_packs_races_cars_and_sheets()
    {
        var w = new World();
        var sheet = new ChassisTemplate { Name = "Club sheet" };
        sheet.Sections.Add(new SetupSection { Name = "Gearing", Fields = { new SetupField { Id = "pinion", Label = "Pinion", Kind = SetupFieldKind.Number, Unit = "T" } } });
        w.Sheets.Upsert(sheet);
        var chassis = new ChassisType { Name = "MR-03", SetupSheetId = sheet.Id };
        w.Chassis.Upsert(chassis);

        w.Packs.Upsert(new BatteryPack { Id = PackId, Label = "Blue", OwnerTransponder = "100" });
        w.Packs.Upsert(new BatteryPack { Label = "Dev's", OwnerTransponder = "500" });
        w.Packs.SetCurrent("100", PackId);
        var race = w.Race("Heat 1");
        w.Packs.Tag(race.Id, "100", PackId, PackTagSource.Phone);
        w.Cars.Upsert(new Car { Id = CarId, Name = "Mine", OwnerTransponder = "100", ChassisTypeId = chassis.Id, RaceNumber = "27" });

        var sources = new DriverGarage.Sources(w.Profiles, w.Packs, w.History, w.Cars, w.Chassis, w.Sheets, w.Setups,
            new[] { "Stock", "GT" }, _ => "data:image/jpeg;base64,AAAA");

        using var doc = JsonDocument.Parse(DriverGarage.Build(w.Profiles.Get("100")!, sources, DateTime.UtcNow));
        var root = doc.RootElement;

        var pack = Assert.Single(root.GetProperty("packs").EnumerateArray());
        Assert.Equal("Blue", pack.GetProperty("label").GetString());
        Assert.True(pack.GetProperty("inCar").GetBoolean());

        var raced = Assert.Single(root.GetProperty("races").EnumerateArray());
        Assert.Equal(PackId, raced.GetProperty("pack").GetString());
        Assert.Equal("phone", raced.GetProperty("by").GetString());

        var car = Assert.Single(root.GetProperty("cars").EnumerateArray());
        Assert.Equal("27", car.GetProperty("number").GetString());
        Assert.Equal(sheet.Id, car.GetProperty("sheet").GetString());
        Assert.Equal("number", root.GetProperty("sheets").GetProperty(sheet.Id).GetProperty("sections")[0].GetProperty("fields")[0].GetProperty("kind").GetString());
        Assert.Equal(2, root.GetProperty("classes").GetArrayLength());
        Assert.Equal(PhonePhotos.MostPerCar, root.GetProperty("limits").GetProperty("photosPerCar").GetInt32());
        Assert.True(root.GetProperty("stats").GetProperty("groups").GetArrayLength() >= 1);
    }

    private static byte[] TinyJpeg()
    {
        using var bitmap = new Bitmap(40, 30);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Red);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Jpeg);
        return stream.ToArray();
    }
}
