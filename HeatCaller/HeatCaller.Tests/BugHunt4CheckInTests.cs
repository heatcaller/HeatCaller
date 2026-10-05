using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 4, the check-in desk and the timetable.
///
/// The run sheet ticking off recorded heats (it only did so while somebody
/// had the Timetable open, so ON DECK stayed on the first heat all night);
/// which class Inspect Selected Car opens, and a checklist not carried over
/// to another class; the desk and the phone judging tech only in the classes
/// racing tonight, and only in the class picked in Class:; and a timetable
/// line's time typed as text ("TBD") kept through an edit that left it alone.
/// </summary>
public sealed class BugHunt4CheckInTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- The run sheet ticks off recorded heats -----------------------------------------

    [Fact]
    public void Recording_a_heat_ticks_its_line_off_so_on_deck_and_the_online_schedule_move_on()
    {
        var events = RaceEventStore.Load();
        var rounds = RoundDefinitionStore.Load();
        var schedule = DayScheduleStore.Load();
        EventSpine.Bind(events, CheckInStore.Load(), TechInspectionStore.Load(), schedule, rounds);

        var day = new RaceEvent { Name = "Tonight" };
        events.Upsert(day);
        events.SetActive(day.Id);

        var heat1 = new RoundDefinition { ClassName = "Stock", RoundLabel = "Q1 Heat 1", Kind = RoundKind.Qualifier };
        var heat2 = new RoundDefinition { ClassName = "Stock", RoundLabel = "Q1 Heat 2", Kind = RoundKind.Qualifier };
        rounds.Add(heat1);
        rounds.Add(heat2);
        events.AddRoundsToActive(new[] { heat1.Id, heat2.Id });

        // Building the heats puts them on the timetable by itself - nobody
        // has opened the Timetable at all.
        schedule.SyncToProgram(EventSpine.Programme(events, rounds)
            .Select(r => new DayScheduleStore.ProgramLine(r.Id, r.ClassName, r.RoundLabel))
            .ToList());

        // Heat 1 is started and recorded, as Race Control does it.
        schedule.MarkStarted(heat1.Id, DateTime.UtcNow);
        rounds.MarkRecorded(heat1.Id, "session-1");

        // What MainForm's SessionSaved handler calls when a race is saved,
        // whether or not the Timetable page has ever been opened. (That the
        // handler is hooked up at all needs the window, so it isn't run here.)
        Assert.Equal(1, MainForm.TickOffRecordedHeats(schedule, rounds));

        Assert.Equal(heat2.Id, schedule.NextUp?.RoundId);
        Assert.True(DayScheduleStore.Load().Everything.Single(e => e.RoundId == heat1.Id).IsComplete);

        // The club's online schedule reads the same lines.
        using var doc = JsonDocument.Parse(RelayDocuments.Programme(schedule, rounds, events, ProfileStore.Load(), lineups: false, abbreviate: false));
        var status = doc.RootElement.GetProperty("lines").EnumerateArray()
            .ToDictionary(l => l.GetProperty("description").GetString()!, l => l.GetProperty("status").GetString());

        Assert.Equal("done", status["Q1 Heat 1"]);
        Assert.Equal("next", status["Q1 Heat 2"]);
    }

    // --- Tech for tonight's classes, and the class Inspect opens ------------------------

    [Fact]
    public void The_desk_and_the_phone_judge_tech_only_in_the_classes_racing_tonight()
    {
        var tech = TechWithStockPass(out var tonight);
        var ann = Ann();
        var classes = Checklisted("Stock", "Mod");

        var judged = tech.LatestForDriver(ann, name => RaceControlControl.DeskJudgesTechFor(name, classes, tech, tonight));

        Assert.True(judged is { IsCleared: true });

        // A race day that names no classes is "whatever turns up", so every
        // class with a checklist still counts - and Mod has no inspection.
        var practice = new RaceEvent { Name = "Practice" };

        Assert.Null(tech.LatestForDriver(ann, name => RaceControlControl.DeskJudgesTechFor(name, classes, tech, practice)));
        Assert.Null(tech.LatestForDriver(ann, name => RaceControlControl.DeskJudgesTechFor(name, classes, tech, null)));
    }

    [Fact]
    public void Picking_a_class_at_the_desk_judges_tech_in_that_class_alone()
    {
        var tech = TechWithStockPass(out _);
        var ann = Ann();
        var classes = Checklisted("Stock", "Mod");

        var stock = tech.LatestForDriver(ann, name => RaceControlControl.DeskJudgesTechFor(name, classes, tech, null, classFilter: "stock"));
        var mod = tech.LatestForDriver(ann, name => RaceControlControl.DeskJudgesTechFor(name, classes, tech, null, classFilter: "Mod"));

        Assert.True(stock is { IsCleared: true });
        Assert.Null(mod);
    }

    [Fact]
    public void Inspect_Selected_Car_opens_the_class_the_driver_still_needs_inspecting_in()
    {
        var tech = TechWithStockPass(out var tonight);
        var ann = Ann();
        var classes = Checklisted("Stock", "Mod");

        Assert.Equal("Mod", tech.TechClassForDriver(ann, name => RaceControlControl.DeskJudgesTechFor(name, classes, tech, null)));

        // Only Stock racing tonight: Stock is the class the desk is showing.
        Assert.Equal("Stock", tech.TechClassForDriver(ann, name => RaceControlControl.DeskJudgesTechFor(name, classes, tech, tonight)));
    }

    [Fact]
    public void Inspect_Selected_Car_opens_the_main_class_when_no_class_runs_tech()
    {
        var tech = TechInspectionStore.Load();
        tech.CurrentEventId = "day-1";
        var classes = new List<RaceClass> { new() { Name = "Stock" }, new() { Name = "Mod" } };

        Assert.Equal("Stock", tech.TechClassForDriver(Ann(), name => RaceControlControl.DeskJudgesTechFor(name, classes, tech, null)));
    }

    [Fact]
    public void Cleared_in_every_class_Inspect_Selected_Car_opens_the_pass_the_desk_shows()
    {
        var tech = TechWithStockPass(out _);
        tech.Upsert(new TechInspection { Transponder = "T1", ClassName = "Mod", InspectedAtUtc = DateTime.UtcNow.AddMinutes(10) });
        var ann = Ann();
        var classes = Checklisted("Stock", "Mod");

        bool Judged(string name) => RaceControlControl.DeskJudgesTechFor(name, classes, tech, null);

        Assert.Equal("Mod", tech.LatestForDriver(ann, Judged)?.ClassName);
        Assert.Equal("Mod", tech.TechClassForDriver(ann, Judged));
    }

    [Fact]
    public void Switching_an_inspection_to_another_class_starts_its_checklist_fresh()
    {
        var stockPass = new TechInspection
        {
            Transponder = "T1",
            ClassName = "Stock",
            ItemsPassed = new List<string> { "Width", "Weight" },
            ItemsFailed = new List<string> { "Tires" },
        };

        // Its own class: the checks as they were recorded.
        Assert.Equal("OK", TechInspectionEditForm.StartingState(stockPass, "Stock", "Width"));
        Assert.Equal("FAIL", TechInspectionEditForm.StartingState(stockPass, "stock", "Tires"));

        // Another class with the same check names: nobody has looked at that car.
        Assert.Equal("not checked", TechInspectionEditForm.StartingState(stockPass, "Mod", "Width"));
        Assert.Equal("not checked", TechInspectionEditForm.StartingState(stockPass, "Mod", "Tires"));
        Assert.Equal("not checked", TechInspectionEditForm.StartingState(stockPass, null, "Width"));
    }

    [Fact]
    public void Saving_an_inspection_on_file_under_another_class_asks_first_because_it_moves_it()
    {
        var stockPass = new TechInspection { Transponder = "T1", ClassName = "Stock", Result = TechResult.Passed };

        Assert.True(TechInspectionEditForm.MovesToAnotherClass(stockPass, "Mod"));

        // Same class (whatever the case), a new inspection, or one that had no class: nothing moves.
        Assert.False(TechInspectionEditForm.MovesToAnotherClass(stockPass, "stock"));
        Assert.False(TechInspectionEditForm.MovesToAnotherClass(null, "Mod"));
        Assert.False(TechInspectionEditForm.MovesToAnotherClass(new TechInspection { Transponder = "T1" }, "Mod"));
        Assert.False(TechInspectionEditForm.MovesToAnotherClass(stockPass, null));
    }

    // --- A time typed as text survives an edit -------------------------------------------

    [Fact]
    public void Editing_a_timetable_line_keeps_a_time_typed_as_text_unless_the_time_is_changed()
    {
        // Left alone: kept, so Fill In the Times keeps it too.
        Assert.Equal("TBD", ScheduleEntryForm.TimeToSave("TBD", noTimeTicked: true, noTimeToggled: false, "10:00 AM"));

        // No set time unticked and ticked again: cleared on purpose.
        Assert.Equal(string.Empty, ScheduleEntryForm.TimeToSave("TBD", noTimeTicked: true, noTimeToggled: true, "10:00 AM"));

        // A time picked instead.
        Assert.Equal("10:00 AM", ScheduleEntryForm.TimeToSave("TBD", noTimeTicked: false, noTimeToggled: true, "10:00 AM"));

        // A line with a real time behaves as it always did.
        Assert.Equal("7:00 PM", ScheduleEntryForm.TimeToSave("19:00", noTimeTicked: false, noTimeToggled: false, "7:00 PM"));
        Assert.Equal(string.Empty, ScheduleEntryForm.TimeToSave("19:00", noTimeTicked: true, noTimeToggled: true, "7:00 PM"));
        Assert.Equal(string.Empty, ScheduleEntryForm.TimeToSave(string.Empty, noTimeTicked: true, noTimeToggled: false, "7:00 PM"));
    }

    // --- Helpers ----------------------------------------------------------------------------

    /// <summary>Ann races Stock, and Mod on the Also races line.</summary>
    private static DriverProfile Ann() => new()
    {
        Transponder = "T1",
        DriverName = "Ann",
        ClassName = "Stock",
        Classes = new List<string> { "Mod" },
    };

    /// <summary>Classes sharing Add Common's checks.</summary>
    private static List<RaceClass> Checklisted(params string[] names) =>
        names.Select(n => new RaceClass { Name = n, TechCheckItems = new List<string> { "Width", "Weight", "Tires" } }).ToList();

    /// <summary>A tech store on tonight's race day (Stock only racing), with Ann's Stock car through.</summary>
    private static TechInspectionStore TechWithStockPass(out RaceEvent tonight)
    {
        tonight = new RaceEvent { Name = "Tonight", ClassNames = new List<string> { "Stock" } };

        var tech = TechInspectionStore.Load();
        tech.CurrentEventId = tonight.Id;
        tech.Upsert(new TechInspection { Transponder = "T1", DriverName = "Ann", ClassName = "Stock", Result = TechResult.Passed });

        return tech;
    }
}
