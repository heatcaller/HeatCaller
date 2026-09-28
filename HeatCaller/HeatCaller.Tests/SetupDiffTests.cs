namespace HeatCaller.Tests;

/// <summary>A small setup sheet shared by the setup tests: one field of every kind, and a notes field.</summary>
internal static class TestSheet
{
    internal static readonly ChassisTemplate Sheet = new()
    {
        Id = "sheet",
        Name = "Test chassis",
        Sections =
        {
            new SetupSection
            {
                Name = "Car",
                Fields =
                {
                    new SetupField { Id = "pinion", Label = "Pinion", Kind = SetupFieldKind.Number, Unit = "T" },
                    new SetupField { Id = "springs", Label = "Front springs", Kind = SetupFieldKind.Choice },
                    new SetupField { Id = "bearings", Label = "Bearings", Kind = SetupFieldKind.MultiChoice },
                    new SetupField { Id = "swaybar", Label = "Sway bar", Kind = SetupFieldKind.Boolean },
                    new SetupField { Id = "height", Label = "Ride height", Kind = SetupFieldKind.Number, Unit = "mm" },
                    new SetupField { Id = "notes", Label = "Notes", Kind = SetupFieldKind.Text },
                },
            },
        },
    };

    internal static ChassisTemplate? For(string? id) => id == Sheet.Id ? Sheet : null;

    /// <summary>A setup on the test sheet for car-1, logged at <paramref name="at"/>.</summary>
    internal static SetupEntry Setup(DateTime at, params (string Id, string Value)[] values) => new()
    {
        RecordedAtUtc = at,
        DriverName = "Asher",
        CarModel = "Kyosho MR-03",
        CarId = "car-1",
        ChassisTemplateId = Sheet.Id,
        Values = values.ToDictionary(v => v.Id, v => v.Value),
    };
}

/// <summary>
/// SetupDiff: what changed on a car from one logged setup to the next - the
/// Garage's "Changed Since Last" column, and what the mechanic in the
/// recap's interview is told (27 Sep 2026).
/// </summary>
public sealed class SetupDiffTests
{
    private static readonly DateTime T0 = new(2026, 9, 27, 18, 0, 0, DateTimeKind.Utc);

    private static SetupEntry Setup(double hours, params (string Id, string Value)[] values) =>
        TestSheet.Setup(T0.AddHours(hours), values);

    [Fact]
    public void Changes_read_in_the_sheets_order_with_their_units_and_notes_left_out()
    {
        var older = Setup(0, ("pinion", "22"), ("springs", "Soft"), ("notes", "loose on exit"));
        var newer = Setup(1, ("springs", "Medium"), ("pinion", "23"), ("notes", "better"));

        var changes = SetupDiff.Between(older, newer, TestSheet.For);

        Assert.Equal(
            new[] { new SetupDiff.Change("Pinion", "22T", "23T"), new SetupDiff.Change("Front springs", "Soft", "Medium") },
            changes);

        Assert.Equal("Pinion 22T → 23T, Front springs Soft → Medium", SetupDiff.Describe(changes));
        Assert.Equal(new[] { "Pinion changed from 22T to 23T", "Front springs changed from Soft to Medium" }, SetupDiff.Spoken(changes));
    }

    [Fact]
    public void Case_and_spaces_are_not_a_change()
    {
        var changes = SetupDiff.Between(Setup(0, ("springs", "medium ")), Setup(1, ("springs", "Medium")), TestSheet.For);

        Assert.Empty(changes);
        Assert.Equal("no changes", SetupDiff.Describe(changes));
    }

    [Fact]
    public void A_field_set_cleared_or_ticked_says_which()
    {
        var older = Setup(0, ("pinion", "22"));
        var newer = Setup(1, ("springs", "Hard"), ("swaybar", "true"), ("bearings", "Front,Rear"));

        var changes = SetupDiff.Between(older, newer, TestSheet.For);

        Assert.Equal("Pinion 22T → (none), Front springs → Hard, Bearings → Front, Rear and 1 more", SetupDiff.Describe(changes));

        Assert.Equal(
            new[]
            {
                "Pinion no longer set (was 22T)",
                "Front springs set to Hard",
                "Bearings set to Front, Rear",
                "Sway bar changed from off to on",
            },
            SetupDiff.Spoken(changes));
    }

    [Fact]
    public void The_setup_before_is_the_same_cars_newest_older_one()
    {
        var first = Setup(0, ("pinion", "21"));
        var second = Setup(1, ("pinion", "22"));
        var otherCar = Setup(1.5, ("pinion", "30"));
        otherCar.CarId = "car-2";
        var third = Setup(2, ("pinion", "23"));

        var all = new[] { third, otherCar, second, first };

        Assert.Same(second, SetupDiff.Previous(third, all));
        Assert.Null(SetupDiff.Previous(first, all));

        Assert.Equal("(first setup)", SetupDiff.Describe(SetupDiff.SincePrevious(first, all, TestSheet.For)));
        Assert.Equal("Pinion 22T → 23T", SetupDiff.Describe(SetupDiff.SincePrevious(third, all, TestSheet.For)));
    }

    [Fact]
    public void Entries_from_before_setup_sheets_compare_their_five_fields_by_driver_and_car()
    {
        var older = new SetupEntry { RecordedAtUtc = T0, DriverName = "Asher", CarModel = "MR-03", Gearing = "22T", Tires = "Sweep 30" };
        var newer = new SetupEntry { RecordedAtUtc = T0.AddHours(1), DriverName = "asher ", CarModel = "MR-03", Gearing = "23T", Tires = "Sweep 30", Notes = "faster" };

        Assert.Equal("Gearing 22T → 23T", SetupDiff.Describe(SetupDiff.SincePrevious(newer, new[] { newer, older }, TestSheet.For)));
    }

    [Fact]
    public void A_sheet_setup_is_not_compared_with_one_from_before_sheets()
    {
        // They share no fields - every one would read as a change.
        var legacy = new SetupEntry { RecordedAtUtc = T0, DriverName = "Asher", CarModel = "Kyosho MR-03", CarId = "car-1", Gearing = "22T" };
        var sheet = Setup(1, ("pinion", "22"));

        Assert.Null(SetupDiff.SincePrevious(sheet, new[] { sheet, legacy }, TestSheet.For));
    }

    [Fact]
    public void A_first_setup_lists_what_was_filled_in_and_nothing_that_was_not()
    {
        var entry = Setup(0, ("pinion", "23"), ("swaybar", ""), ("springs", "Soft"), ("notes", "new car"), ("height", "4.5"));

        Assert.Equal(new[] { "Pinion 23T", "Front springs Soft", "Ride height 4.5mm" }, SetupDiff.Filled(entry, TestSheet.For));
    }

    [Fact]
    public void A_field_on_no_sheet_still_counts_under_its_own_id()
    {
        // A field taken off the sheet since - the value is still the car's.
        var changes = SetupDiff.Between(Setup(0, ("diff", "3000")), Setup(1, ("diff", "5000")), TestSheet.For);

        Assert.Equal(new[] { new SetupDiff.Change("diff", "3000", "5000", OnSheet: false) }, changes);
    }

    [Fact]
    public void A_unit_typed_in_with_the_number_isnt_doubled()
    {
        var changes = SetupDiff.Between(Setup(0, ("pinion", "22")), Setup(1, ("pinion", "23T")), TestSheet.For);

        Assert.Equal("Pinion 22T → 23T", SetupDiff.Describe(changes));
    }

    [Fact]
    public void The_same_boxes_ticked_in_another_order_are_not_a_change()
    {
        Assert.Empty(SetupDiff.Between(Setup(0, ("bearings", "Rear,Front")), Setup(1, ("bearings", "Front, Rear")), TestSheet.For));
    }
}

/// <summary>
/// MechanicBrief: what the mechanic in the recap's interview may say about
/// the winning car - the setup logged for it on the day, and what changed
/// (27 Sep 2026).
/// </summary>
public sealed class MechanicBriefTests
{
    private static readonly DateTime RaceEnd = new(2026, 9, 27, 20, 0, 0, DateTimeKind.Utc);

    private static Car MakeCar(string id, string owner = "T1", string className = "Mini-Z") =>
        new() { Id = id, Name = id, OwnerTransponder = owner, ClassName = className };

    private static SetupEntry Setup(double hoursBeforeEnd, string carId, params (string Id, string Value)[] values)
    {
        var entry = TestSheet.Setup(RaceEnd.AddHours(-hoursBeforeEnd), values);
        entry.CarId = carId;
        return entry;
    }

    private static IReadOnlyList<string> Lines(IEnumerable<Car> cars, IEnumerable<SetupEntry> setups, string? className = "Mini-Z", string? eventId = null) =>
        MechanicBrief.SetupLines("T1", "Asher", className, eventId, RaceEnd, cars, setups.OrderByDescending(s => s.RecordedAtUtc).ToList(), TestSheet.For);

    [Fact]
    public void The_changes_logged_on_the_day_are_listed()
    {
        var lines = Lines(
            new[] { MakeCar("car-1") },
            new[]
            {
                Setup(3, "car-1", ("pinion", "22"), ("springs", "Soft")),
                Setup(1, "car-1", ("pinion", "23"), ("springs", "Medium")),
            });

        Assert.Equal(
            new[]
            {
                "Setup changes logged for this race day:",
                "- Pinion changed from 22T to 23T",
                "- Front springs changed from Soft to Medium",
            },
            lines);
    }

    [Fact]
    public void More_changes_than_the_mechanic_is_told_by_name_are_counted()
    {
        var lines = Lines(
            new[] { MakeCar("car-1") },
            new[]
            {
                Setup(3, "car-1", ("pinion", "22"), ("springs", "Soft"), ("height", "4")),
                Setup(1, "car-1", ("pinion", "23"), ("springs", "Medium"), ("height", "5"), ("swaybar", "true"), ("bearings", "Front")),
            });

        Assert.Equal(1 + SetupDiff.SpokenMax + 1, lines.Count);
        Assert.Equal("- and 1 more change besides", lines[^1]);
    }

    [Fact]
    public void A_setup_from_an_earlier_day_is_not_called_a_change_for_this_race()
    {
        var lines = Lines(new[] { MakeCar("car-1") }, new[] { Setup(80, "car-1", ("pinion", "22")), Setup(72, "car-1", ("pinion", "23")) });

        Assert.Equal(new[] { "No setup changes were logged for this race day - the car ran the setup it already had." }, lines);
    }

    [Fact]
    public void A_setup_logged_after_the_race_is_not_what_the_car_ran()
    {
        var lines = Lines(
            new[] { MakeCar("car-1") },
            new[] { Setup(1, "car-1", ("pinion", "22")), Setup(-0.2, "car-1", ("pinion", "24")) });

        Assert.Equal(
            new[] { "Setup logged for this race day, the first one logged for this car, so there is nothing to compare it with: Pinion 22T" },
            lines);
    }

    [Fact]
    public void Only_the_winners_car_in_the_races_class_counts()
    {
        var lines = Lines(
            new[] { MakeCar("car-1"), MakeCar("car-2", className: "Touring") },
            new[]
            {
                Setup(4, "car-1", ("pinion", "22")),
                Setup(2, "car-1", ("pinion", "23")),
                Setup(0.5, "car-2", ("pinion", "40")),
            });

        Assert.Equal("- Pinion changed from 22T to 23T", Assert.Single(lines.Skip(1)));
    }

    [Fact]
    public void Somebody_elses_car_does_not_count_unless_it_was_lent_for_the_day()
    {
        var borrowed = MakeCar("car-9", owner: "T9");
        borrowed.LoanedToTransponder = "T1";
        borrowed.LoanedForEventId = "day-1";

        var setups = new[] { Setup(3, "car-9", ("pinion", "20")), Setup(1, "car-9", ("pinion", "21")) };

        Assert.Empty(Lines(new[] { borrowed }, setups, className: null, eventId: "day-2"));
        Assert.Equal("- Pinion changed from 20T to 21T", Assert.Single(Lines(new[] { borrowed }, setups, className: null, eventId: "day-1").Skip(1)));
    }

    [Fact]
    public void An_entry_from_before_the_Garage_is_matched_by_the_drivers_name()
    {
        var older = Setup(3, "car-1", ("pinion", "22"));
        var newer = Setup(1, "car-1", ("pinion", "22"));
        older.CarId = null;
        newer.CarId = null;
        newer.DriverName = " asher";

        Assert.Equal(new[] { "Setup logged for this race day: no changes from the setup before it." }, Lines(Array.Empty<Car>(), new[] { older, newer }));
    }

    [Fact]
    public void The_first_setup_on_a_sheet_after_older_entries_says_what_was_filled_in()
    {
        var legacy = new SetupEntry { RecordedAtUtc = RaceEnd.AddHours(-5), DriverName = "Asher", CarModel = "Kyosho MR-03", CarId = "car-1", Gearing = "22T" };
        var sheet = Setup(1, "car-1", ("pinion", "23"), ("springs", "Soft"));

        Assert.Equal(
            new[] { "Setup logged for this race day, the first on a setup sheet for this car, so there is nothing to compare it with: Pinion 23T, Front springs Soft" },
            Lines(new[] { MakeCar("car-1") }, new[] { legacy, sheet }));
    }

    [Fact]
    public void A_value_whose_field_is_off_the_sheet_isnt_told_to_the_mechanic()
    {
        // Its only name is the field's stored id - not something to say out loud.
        var lines = Lines(
            new[] { MakeCar("car-1") },
            new[]
            {
                Setup(3, "car-1", ("pinion", "22"), ("a1b2c3d4", "3000")),
                Setup(1, "car-1", ("pinion", "23"), ("a1b2c3d4", "5000")),
            });

        Assert.Equal(new[] { "Setup changes logged for this race day:", "- Pinion changed from 22T to 23T" }, lines);
    }

    [Fact]
    public void Nothing_logged_means_nothing_to_say()
    {
        Assert.Empty(Lines(new[] { MakeCar("car-1") }, Array.Empty<SetupEntry>()));
        Assert.Empty(MechanicBrief.SetupLines(string.Empty, "Asher", null, null, RaceEnd, Array.Empty<Car>(), Array.Empty<SetupEntry>(), TestSheet.For));
    }
}
