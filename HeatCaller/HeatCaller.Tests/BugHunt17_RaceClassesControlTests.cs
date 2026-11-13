using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), the Classes page: undoing a rename is refused
/// when a different class has the new name now (it used to drag that class's
/// drivers back to the old name), and the sanctioning preset's question says
/// it saves straight away from the door that does.
/// </summary>
public sealed class BugHunt17_RaceClassesControlTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Undoing_a_rename_is_refused_when_a_new_class_has_taken_the_new_name()
    {
        var profiles = ProfileStore.Load();
        var classes = RaceClassStore.Load();
        var cascade = new ClassRenameCascade(profiles, RoundDefinitionStore.Load(), SeasonStore.Load());

        var a = new RaceClass { Name = "A" };
        classes.Upsert(a);

        var undo = new UndoService();
        RenameInEditor(undo, classes, cascade, a, "B");

        // The renamed class is deleted, and a new class is added as "B".
        classes.Delete(a.Id);
        var newB = new RaceClass { Name = "B" };
        classes.Upsert(newB);
        profiles.Upsert(new DriverProfile { Transponder = "1", DriverName = "Jo", ClassName = "B" });

        Assert.Null(undo.Undo());

        // Nothing moved: Jo is still in the new class, and "A" is not back.
        Assert.Equal("B", ProfileStore.Load().Get("1")!.ClassName);
        Assert.Equal(new[] { "B" }, classes.All.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void Undoing_a_rename_still_works_while_the_class_itself_has_the_new_name()
    {
        var profiles = ProfileStore.Load();
        var classes = RaceClassStore.Load();
        var cascade = new ClassRenameCascade(profiles, RoundDefinitionStore.Load(), SeasonStore.Load());

        var a = new RaceClass { Name = "A" };
        classes.Upsert(a);
        profiles.Upsert(new DriverProfile { Transponder = "1", DriverName = "Jo", ClassName = "A" });

        var undo = new UndoService();
        RenameInEditor(undo, classes, cascade, a, "B");

        Assert.NotNull(undo.Undo());

        Assert.Equal("A", ProfileStore.Load().Get("1")!.ClassName);
        Assert.Equal("A", classes.Get(a.Id)!.Name);
    }

    [Fact]
    public void The_preset_question_says_when_yes_is_the_save()
    {
        // The class editor: its own Save commits.
        Assert.Contains("not saved until you press Save", SanctioningPresetForm.SavingNote(savedAtOnce: false, canUndo: true), StringComparison.Ordinal);

        // The Classes page's button: Yes writes the class.
        var atOnce = SanctioningPresetForm.SavingNote(savedAtOnce: true, canUndo: true);
        Assert.Contains("saved straight away", atOnce, StringComparison.Ordinal);
        Assert.Contains("Undo", atOnce, StringComparison.Ordinal);
        Assert.DoesNotContain("press Save", atOnce, StringComparison.Ordinal);

        Assert.DoesNotContain("Undo", SanctioningPresetForm.SavingNote(savedAtOnce: true, canUndo: false), StringComparison.Ordinal);
    }

    /// <summary>A rename as the Classes page's Edit makes it (RaceClassesControl.EditSelected), with its undo step.</summary>
    private static void RenameInEditor(UndoService undo, RaceClassStore classes, ClassRenameCascade cascade, RaceClass live, string newName)
    {
        var oldName = live.Name;
        var before = JsonSerializer.Deserialize<RaceClass>(JsonSerializer.Serialize(live));

        // The dialog writes onto the stored class itself.
        live.Name = newName;
        var afterJson = JsonSerializer.Serialize(live);
        ClassRenameReceipt? receipt = null;

        undo.Capture($"Rename class '{oldName}' to '{newName}'", () =>
        {
            RaceClassesControl.RefuseUndoIfNameReused(classes, live.Id, newName);
            classes.UndoRename(live.Id, oldName, before, afterJson, cascade, receipt);
        });

        receipt = cascade.Rename(oldName, newName);
        classes.Upsert(live);
    }
}
