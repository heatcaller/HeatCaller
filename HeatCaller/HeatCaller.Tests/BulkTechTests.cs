namespace HeatCaller.Tests;

/// <summary>
/// Several drivers at once on Who's Racing (1 Nov 2026): which rows a
/// double-click acts on, which of today's inspections the desk edits, and the
/// inspections one bulk log files.
/// </summary>
public sealed class BulkTechTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 20, 0, 0, DateTimeKind.Utc);

    private static DriverProfile Driver(string transponder, string name, string className = "Stock", string car = "") =>
        new() { Transponder = transponder, DriverName = name, ClassName = className, CarModel = car };

    private static IReadOnlyList<string> Checklist(string className) => className switch
    {
        "Stock" => new[] { "Body secure", "Motor legal", "Tyres legal" },
        "Modified" => new[] { "Weight", "Ride height" },
        _ => Array.Empty<string>(),
    };

    // --- Which rows a double-click means ---

    private static readonly int[] None = Array.Empty<int>();

    [Fact]
    public void A_double_click_on_one_of_several_picked_rows_means_all_of_them_in_list_order()
    {
        // Picked 2, 4 and 7 - not next to each other - and double-clicked 4.
        // Its first click has narrowed the list's selection to 4 by now.
        var rows = BulkTech.RowsToActOn(new[] { 7, 2, 4 }, selectedNow: new[] { 4 }, clickedRow: 4);

        Assert.Equal(new[] { 2, 4, 7 }, rows);
    }

    [Fact]
    public void A_ctrl_double_click_on_one_of_the_picked_rows_still_means_all_of_them()
    {
        // Ctrl's first click took 4 out of the selection.
        var rows = BulkTech.RowsToActOn(new[] { 2, 4, 7 }, selectedNow: new[] { 2, 7 }, clickedRow: 4, ctrl: true);

        Assert.Equal(new[] { 2, 4, 7 }, rows);
    }

    [Fact]
    public void A_ctrl_double_click_on_a_row_not_picked_yet_adds_it_to_the_picked_ones()
    {
        // Ctrl-picked 2 and 7, then Ctrl+double-clicked 9. That used to
        // inspect 9 alone.
        var rows = BulkTech.RowsToActOn(new[] { 2, 7 }, selectedNow: new[] { 2, 7, 9 }, clickedRow: 9, ctrl: true);

        Assert.Equal(new[] { 2, 7, 9 }, rows);
    }

    [Fact]
    public void A_shift_double_click_means_the_run_it_picks()
    {
        var rows = BulkTech.RowsToActOn(new[] { 1 }, selectedNow: new[] { 3, 4, 5 }, clickedRow: 5, shift: true);

        Assert.Equal(new[] { 3, 4, 5 }, rows);
    }

    [Fact]
    public void A_double_click_on_a_row_outside_the_picked_ones_means_just_that_row()
    {
        Assert.Equal(new[] { 9 }, BulkTech.RowsToActOn(new[] { 2, 4 }, new[] { 9 }, clickedRow: 9));
    }

    [Fact]
    public void A_double_click_with_one_row_picked_means_just_that_row_as_it_always_did()
    {
        Assert.Equal(new[] { 3 }, BulkTech.RowsToActOn(new[] { 3 }, new[] { 3 }, clickedRow: 3));
        Assert.Equal(new[] { 3 }, BulkTech.RowsToActOn(None, new[] { 3 }, clickedRow: 3));
        Assert.Equal(new[] { 3 }, BulkTech.RowsToActOn(None, new[] { 3 }, clickedRow: 3, ctrl: true));
    }

    [Fact]
    public void A_double_click_on_the_headings_means_no_rows()
    {
        Assert.Empty(BulkTech.RowsToActOn(new[] { 1, 2 }, new[] { 1, 2 }, clickedRow: -1));
        Assert.Empty(BulkTech.RowsToActOn(new[] { 1, 2 }, new[] { 1, 2 }, clickedRow: -1, ctrl: true));
    }

    // --- Telling a double-click's second press from a new click ---

    private static readonly Size DoubleClickSize = new(4, 4);

    [Fact]
    public void A_press_counted_as_a_double_click_is_its_second_press()
    {
        Assert.True(BulkTech.IsSecondPress(2, msSinceLastPress: 5000, new Point(10, 10), new Point(300, 300), 500, DoubleClickSize));
    }

    [Fact]
    public void A_quick_press_in_the_same_place_is_the_second_press_even_when_it_says_one_click()
    {
        Assert.True(BulkTech.IsSecondPress(1, msSinceLastPress: 200, new Point(11, 10), new Point(10, 11), 500, DoubleClickSize));
    }

    [Fact]
    public void A_slow_press_or_one_somewhere_else_is_a_new_click()
    {
        Assert.False(BulkTech.IsSecondPress(1, msSinceLastPress: 900, new Point(10, 10), new Point(10, 10), 500, DoubleClickSize));
        Assert.False(BulkTech.IsSecondPress(1, msSinceLastPress: 200, new Point(10, 60), new Point(10, 10), 500, DoubleClickSize));
    }

    // --- Which of today's inspections the desk edits ---

    [Fact]
    public void An_inspection_from_the_last_few_hours_is_edited_rather_than_filed_again()
    {
        var latest = new TechInspection { Transponder = "T1", InspectedAtUtc = Now.AddHours(-2) };

        Assert.Same(latest, BulkTech.EditableToday(latest, Now));
    }

    [Fact]
    public void Yesterdays_inspection_at_a_weekend_meeting_is_left_alone()
    {
        var latest = new TechInspection { Transponder = "T1", InspectedAtUtc = Now.AddDays(-1).AddHours(-2) };

        Assert.Null(BulkTech.EditableToday(latest, Now));
        Assert.Null(BulkTech.EditableToday(null, Now));
    }

    // --- The inspections a bulk log files ---

    [Fact]
    public void A_bulk_pass_files_one_inspection_per_car_with_every_check_on_its_own_class_list_ok()
    {
        var cars = new[]
        {
            new BulkTech.Car(Driver("T1", "Ana", car: "MR-03"), "Stock", null),
            new BulkTech.Car(Driver("T2", "Ben", "Modified"), "Modified", null),
        };

        var built = BulkTech.Build(cars, TechResult.Passed, " Sam ", "Waved through at the bench", Checklist, Now);

        Assert.Equal(2, built.Count);

        Assert.Equal("T1", built[0].Transponder);
        Assert.Equal("Ana", built[0].DriverName);
        Assert.Equal("Stock", built[0].ClassName);
        Assert.Equal("MR-03", built[0].CarModel);
        Assert.Equal(Checklist("Stock"), built[0].ItemsPassed);
        Assert.Empty(built[0].ItemsFailed);

        Assert.Equal("Modified", built[1].ClassName);
        Assert.Equal(Checklist("Modified"), built[1].ItemsPassed);

        Assert.All(built, inspection =>
        {
            Assert.Equal(TechResult.Passed, inspection.Result);
            Assert.True(inspection.IsCleared);
            Assert.Equal("Sam", inspection.Inspector);
            Assert.Equal("Waved through at the bench", inspection.Notes);
            Assert.Equal(Now, inspection.InspectedAtUtc);

            // Left for the store to stamp with the race day open now.
            Assert.Equal(string.Empty, inspection.EventId);
        });

        Assert.NotEqual(built[0].Id, built[1].Id);
    }

    [Fact]
    public void A_bulk_failure_marks_no_check_and_does_not_clear_the_cars()
    {
        var cars = new[]
        {
            new BulkTech.Car(Driver("T1", "Ana"), "Stock", null),
            new BulkTech.Car(Driver("T2", "Ben"), "Stock", null),
        };

        var built = BulkTech.Build(cars, TechResult.Failed, "Sam", "Wrong motor", Checklist, Now);

        Assert.All(built, inspection =>
        {
            Assert.Equal(TechResult.Failed, inspection.Result);
            Assert.False(inspection.IsCleared);
            Assert.Empty(inspection.ItemsPassed);
            Assert.Empty(inspection.ItemsFailed);
        });
    }

    [Fact]
    public void A_pass_with_a_note_still_clears_the_cars_and_marks_no_check()
    {
        var built = BulkTech.Build(
            new[] { new BulkTech.Car(Driver("T1", "Ana"), "Stock", null) },
            TechResult.PassedWithNote, "Sam", "Fix the bumper before the main", Checklist, Now);

        Assert.True(built[0].IsCleared);
        Assert.Empty(built[0].ItemsPassed);
    }

    [Fact]
    public void A_car_already_inspected_today_has_that_inspection_updated_not_a_second_one_filed()
    {
        var today = new TechInspection
        {
            Transponder = "T1",
            DriverName = "Ana",
            ClassName = "Stock",
            CarModel = "MR-03 EVO",
            Result = TechResult.Failed,
            ItemsFailed = new List<string> { "Motor legal" },
            Inspector = "Jo",
            Notes = "Motor swapped at the bench",
            InspectedAtUtc = Now.AddHours(-1),
            EventId = "day-1",
        };

        var built = BulkTech.Build(
            new[] { new BulkTech.Car(Driver("T1", "Ana", car: "Old car"), "Stock", today) },
            TechResult.Passed, inspector: "  ", notes: "", Checklist, Now);

        var updated = Assert.Single(built);

        Assert.Equal(today.Id, updated.Id);
        Assert.Equal(TechResult.Passed, updated.Result);
        Assert.Empty(updated.ItemsFailed);
        Assert.Equal(Checklist("Stock"), updated.ItemsPassed);

        // Blank inspector and notes keep what the bench wrote; the car on
        // the inspection stays the one that was looked at.
        Assert.Equal("Jo", updated.Inspector);
        Assert.Equal("Motor swapped at the bench", updated.Notes);
        Assert.Equal("MR-03 EVO", updated.CarModel);

        // Re-inspected now, so filed under the race day open now.
        Assert.Equal(Now, updated.InspectedAtUtc);
        Assert.Equal(string.Empty, updated.EventId);

        // A copy: the stored record is never written to here, or an undo
        // snapshot taken after would already hold the change.
        Assert.Equal(TechResult.Failed, today.Result);
        Assert.NotSame(today, updated);
    }

    [Fact]
    public void Inspector_and_notes_typed_in_replace_what_was_there()
    {
        var today = new TechInspection { Transponder = "T1", ClassName = "Stock", Inspector = "Jo", Notes = "old", InspectedAtUtc = Now };

        var built = BulkTech.Build(
            new[] { new BulkTech.Car(Driver("T1", "Ana"), "Stock", today) },
            TechResult.Passed, "Sam", "new", Checklist, Now);

        Assert.Equal("Sam", built[0].Inspector);
        Assert.Equal("new", built[0].Notes);
    }

    [Fact]
    public void The_same_car_picked_twice_is_filed_once()
    {
        var ana = Driver("T1", "Ana");

        var built = BulkTech.Build(
            new[] { new BulkTech.Car(ana, "Stock", null), new BulkTech.Car(Driver("t1", "Ana"), "STOCK", null) },
            TechResult.Passed, "Sam", string.Empty, Checklist, Now);

        Assert.Single(built);
    }

    [Fact]
    public void Filed_through_the_store_each_car_is_cleared_in_its_own_class()
    {
        using var data = new TempAppData();
        var store = TechInspectionStore.Load();
        store.CurrentEventId = "day-1";

        var built = BulkTech.Build(
            new[]
            {
                new BulkTech.Car(Driver("T1", "Ana"), "Stock", null),
                new BulkTech.Car(Driver("T2", "Ben", "Modified"), "Modified", null),
            },
            TechResult.Passed, "Sam", string.Empty, Checklist, DateTime.UtcNow);

        foreach (var inspection in built)
        {
            store.Upsert(inspection);
        }

        Assert.NotNull(store.Cleared("T1", "Stock"));
        Assert.NotNull(store.Cleared("T2", "Modified"));
        Assert.Null(store.Cleared("T2", "Stock"));
        Assert.All(store.All, inspection => Assert.Equal("day-1", inspection.EventId));
    }

    // --- Who it covers, in one line ---

    [Fact]
    public void The_names_line_lists_a_few_and_counts_the_rest()
    {
        Assert.Equal("Ana", BulkTech.NamesLine(new[] { "Ana" }));
        Assert.Equal("Ana and Ben", BulkTech.NamesLine(new[] { "Ana", "Ben" }));
        Assert.Equal("Ana, Ben and Cy", BulkTech.NamesLine(new[] { "Ana", "Ben", "Cy" }));
        Assert.Equal("A, B and 3 more", BulkTech.NamesLine(new[] { "A", "B", "C", "D", "E" }, shown: 2));
        Assert.Equal(string.Empty, BulkTech.NamesLine(Array.Empty<string>()));
    }
}
