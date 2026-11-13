namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, the tech inspection window: Enter in the Notes box starts a
/// new line. It used to press Save - the window's default button - and file
/// the inspection part-way through a note.
/// </summary>
public sealed class BugHunt17_TechInspectionEditFormTests
{
    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file)).Replace("\r\n", "\n");
    }

    // Read from the source: the window needs the club's drivers and classes
    // to open, and the key goes to a real, focused window to be tested.
    [Fact]
    public void The_notes_box_takes_Enter_as_a_new_line()
    {
        Assert.Contains("_txtNotes.AcceptsReturn = true;", Source("TechInspectionEditForm.cs"));
    }
}
