using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11 (5 Nov 2026), the Garage. A phone's change to a car or a
/// setup lands on the roster tick while the desk has that car's window (or
/// the setup sheet) open; the desk's next Retire, Edit or Photos, or its
/// Save, wrote back the copy it opened on whole and undid the phone's change
/// without a word. And the phone's garage document wrote its times in the
/// PC's culture, which a Finnish or Thai PC turned into something the
/// phone could not read.
/// </summary>
public sealed class BugHunt11_GarageTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>What ApplyPhoto and ApplyCar do: a changed clone of the stored car, upserted.</summary>
    private static void PhoneChanges(CarStore cars, string id, Action<Car> change)
    {
        var changed = cars.Get(id)!.CloneForUndo();
        change(changed);
        cars.Upsert(changed);
    }

    private static CarDetailForm Window(Car car, CarStore cars) => new(
        car,
        cars,
        ChassisTypeStore.Load(),
        ProfileStore.Load(),
        RaceClassStore.Load(),
        SetupLogStore.Load(),
        SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>()),
        TrackLayoutStore.Load(),
        ChassisTemplateStore.Load(),
        new UndoService(),
        events: null);

    private static void Press(CarDetailForm form, string method) =>
        typeof(CarDetailForm).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);

    // ------------------------------------------------------------------
    //  1: the car window wrote a stale car back whole
    // ------------------------------------------------------------------

    [Fact]
    public void Putting_a_car_back_keeps_the_photo_and_notes_a_phone_sent_while_its_window_was_open()
    {
        var cars = CarStore.Load();
        cars.Upsert(new Car { Id = "c1", Name = "Red", Notes = "old notes", Retired = true, PhotoFileNames = { "desk.jpg" } });

        StaThread.Run(() =>
        {
            using var form = Window(cars.Get("c1")!, cars);

            PhoneChanges(cars, "c1", c =>
            {
                c.PhotoFileNames.Add("phone.jpg");
                c.Notes = "new diff oil";
            });

            Press(form, "RetireCar");
            return true;
        }, TimeSpan.FromSeconds(30), () => "putting a car back");

        var stored = cars.Get("c1")!;
        Assert.False(stored.Retired);
        Assert.Equal(new[] { "desk.jpg", "phone.jpg" }, stored.PhotoFileNames);
        Assert.Equal("new diff oil", stored.Notes);
    }

    [Fact]
    public void A_window_opened_from_a_card_built_before_a_phone_change_opens_on_the_car_as_stored()
    {
        var cars = CarStore.Load();
        cars.Upsert(new Car { Id = "c1", Name = "Red", Retired = true });

        // The wall's card: the store's object when the wall was built.
        var card = cars.Get("c1")!;
        PhoneChanges(cars, "c1", c => c.Name = "Red Rocket");

        var title = StaThread.Run(() =>
        {
            using var form = Window(card, cars);
            Press(form, "RetireCar");
            return form.Text;
        }, TimeSpan.FromSeconds(30), () => "opening a stale card");

        Assert.Equal("Red Rocket", title);
        Assert.Equal("Red Rocket", cars.Get("c1")!.Name);
        Assert.False(cars.Get("c1")!.Retired);
    }

    [Fact]
    public void An_edit_keeps_what_a_phone_changed_while_the_dialog_was_open_and_the_desk_did_not_touch()
    {
        var opened = new Car { Id = "c1", Name = "Red", Notes = "old", RaceNumber = "5", ChassisTypeId = "a" };

        var desk = opened.CloneForUndo();
        desk.RaceNumber = "27";

        var live = opened.CloneForUndo();
        live.Notes = "phone notes";
        live.ChassisTypeId = "b";
        live.PhotoFileNames.Add("phone.jpg");

        var merged = CarDetailForm.MergeEdit(opened, desk, live);

        Assert.Equal("27", merged.RaceNumber);
        Assert.Equal("phone notes", merged.Notes);
        Assert.Equal("b", merged.ChassisTypeId);
        Assert.Equal(new[] { "phone.jpg" }, merged.PhotoFileNames);
    }

    [Fact]
    public void A_photo_edit_keeps_a_photo_the_phone_added_while_it_was_open()
    {
        var opened = new Car { Id = "c1", PhotoFileNames = { "a.jpg", "b.jpg", "c.jpg" } };

        // The desk took b off and put c first.
        var desk = opened.CloneForUndo();
        desk.PhotoFileNames = new List<string> { "c.jpg", "a.jpg" };

        // Meanwhile the phone added one and took a off.
        var live = opened.CloneForUndo();
        live.PhotoFileNames = new List<string> { "b.jpg", "c.jpg", "phone.jpg" };

        var merged = CarDetailForm.MergeEdit(opened, desk, live);

        Assert.Equal(new[] { "c.jpg", "phone.jpg" }, merged.PhotoFileNames);
    }

    // ------------------------------------------------------------------
    //  2: a desk Setup Sheet save over a phone's fix
    // ------------------------------------------------------------------

    [Fact]
    public void A_desk_save_keeps_the_values_a_phone_fixed_while_the_sheet_was_open()
    {
        var opened = new SetupEntry
        {
            DriverName = "Sam",
            ChassisTemplateId = "sheet",
            Values = new() { ["pinion"] = "20", ["oil"] = "30wt" },
            Notes = "loose",
        };

        // The desk links the race and changes nothing else.
        var desk = SetupLogStore.CopyOf(opened);
        desk.LinkedSessionId = "heat-1";

        // The phone's fix, applied under the dialog.
        var now = SetupLogStore.CopyOf(opened);
        now.Values = new() { ["pinion"] = "21", ["oil"] = "35wt" };
        now.Notes = "better";

        var merged = SetupLogStore.MergeEdit(opened, desk, now);

        Assert.Equal("heat-1", merged.LinkedSessionId);
        Assert.Equal("21", merged.Values["pinion"]);
        Assert.Equal("35wt", merged.Values["oil"]);
        Assert.Equal("better", merged.Notes);
        Assert.Equal(opened.Id, merged.Id);
    }

    [Fact]
    public void A_value_the_desk_did_change_still_wins()
    {
        var opened = new SetupEntry { ChassisTemplateId = "sheet", Values = new() { ["pinion"] = "20", ["oil"] = "30wt" } };

        var desk = SetupLogStore.CopyOf(opened);
        desk.Values["pinion"] = "22";

        var now = SetupLogStore.CopyOf(opened);
        now.Values["oil"] = "35wt";

        var merged = SetupLogStore.MergeEdit(opened, desk, now);

        Assert.Equal("22", merged.Values["pinion"]);
        Assert.Equal("35wt", merged.Values["oil"]);
    }

    // ------------------------------------------------------------------
    //  3: the phone's garage times in the PC's culture
    // ------------------------------------------------------------------

    private static CultureInfo FinnishStyleTimes()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
        culture.DateTimeFormat.TimeSeparator = ".";
        return culture;
    }

    private static CultureInfo ThaiBuddhistYears()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("th-TH").Clone();
        culture.DateTimeFormat.Calendar = new ThaiBuddhistCalendar();
        return culture;
    }

    public static TheoryData<string> Cultures => new() { "finnish-times", "thai-years" };

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_garage_document_writes_its_times_the_same_way_whatever_the_PCs_culture(string which)
    {
        var profiles = ProfileStore.Load();
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "100", DriverName = "Sam" }, out _));

        var packs = BatteryPackStore.Load();
        var addedUtc = new DateTime(2026, 11, 5, 19, 30, 0, DateTimeKind.Utc);
        packs.Upsert(new BatteryPack { Label = "Blue", OwnerTransponder = "100", AddedUtc = addedUtc });

        var sources = new DriverGarage.Sources(profiles, packs, SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>()),
            CarStore.Load(), ChassisTypeStore.Load(), ChassisTemplateStore.Load(), SetupLogStore.Load(),
            new[] { "Stock" }, _ => null);

        var was = CultureInfo.CurrentCulture;
        string json;

        try
        {
            CultureInfo.CurrentCulture = which == "thai-years" ? ThaiBuddhistYears() : FinnishStyleTimes();
            json = DriverGarage.Build(profiles.Get("100")!, sources, addedUtc);
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("2026-11-05T19:30:00Z", root.GetProperty("at").GetString());
        Assert.Equal("2026-11-05T19:30:00Z", Assert.Single(root.GetProperty("packs").EnumerateArray()).GetProperty("added").GetString());
    }
}
