namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: the audio rendered ahead for a two-hander's turns sat in one
/// field of the Announcer page. On Air's Tonight Open can start while a
/// director block is still pacing its turns, and it then overwrote the slot,
/// so the block's remaining turns were said with the Tonight Open's audio.
/// Each segment now keeps its own renders and hands them to what speaks them.
/// The wiring is page code no test can drive, so the shape is pinned here.
/// </summary>
public sealed class BugHunt17_AnnouncerControl_DirectorTests
{
    private static string Source()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", "AnnouncerControl.Director.cs")).Replace("\r\n", "\n");
    }

    [Fact]
    public void The_rendered_turns_are_kept_by_the_segment_not_by_the_page()
    {
        var source = Source();

        Assert.DoesNotContain("_renderedTurns", source);

        // The show block's turns and the plain path both speak from the
        // renders made for THEIR segment.
        Assert.Contains("rendered = await RenderTurnsAsync(said).ConfigureAwait(true);", source);
        Assert.Contains("prepared: at >= 0 && at < rendered.Count ? rendered[at] : null,", source);
        Assert.Contains("var rendered = await RenderTurnsAsync(one).ConfigureAwait(true);", source);
        Assert.Contains("SpeakPlainly(one, rendered);", source);
        Assert.Contains("SpeakPlainly(said, rendered);", source);
    }
}
