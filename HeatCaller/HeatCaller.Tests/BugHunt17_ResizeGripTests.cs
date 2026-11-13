using System.Reflection;
using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: the grip raised Finished for a plain click, and for the
/// second click of a double-click - after Reset. On Race Day > Today that
/// saved the split's height as "dragged" straight after the double-click had
/// forgotten it, so the top half never fitted itself again.
/// </summary>
public sealed class BugHunt17_ResizeGripTests
{
    [Fact]
    public void A_click_that_never_moves_is_not_a_drag_to_save()
    {
        using var grip = new ResizeGrip { CurrentHeight = () => 200 };
        var finished = 0;
        grip.Finished += () => finished++;

        Mouse(grip, "OnMouseDown", clicks: 1);
        Mouse(grip, "OnMouseUp", clicks: 1);

        Assert.Equal(0, finished);
    }

    [Fact]
    public void A_double_click_resets_and_saves_nothing_after_it()
    {
        using var grip = new ResizeGrip { CurrentHeight = () => 200 };
        var said = new List<string>();
        grip.Finished += () => said.Add("finished");
        grip.Reset += () => said.Add("reset");
        grip.Dragged += _ => said.Add("dragged");

        // As Windows sends it: down, up, then the second press with Clicks
        // = 2, the double-click, and its release.
        Mouse(grip, "OnMouseDown", clicks: 1);
        Mouse(grip, "OnMouseUp", clicks: 1);
        Mouse(grip, "OnMouseDown", clicks: 2);
        Invoke(grip, "OnDoubleClick", EventArgs.Empty);
        Mouse(grip, "OnMouseMove", clicks: 0);
        Mouse(grip, "OnMouseUp", clicks: 1);

        Assert.Equal(new[] { "reset" }, said);
    }

    private static void Mouse(ResizeGrip grip, string method, int clicks) =>
        Invoke(grip, method, new MouseEventArgs(MouseButtons.Left, clicks, 5, 5, 0));

    private static void Invoke(ResizeGrip grip, string method, EventArgs e) =>
        typeof(ResizeGrip).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(grip, new object[] { e });
}
