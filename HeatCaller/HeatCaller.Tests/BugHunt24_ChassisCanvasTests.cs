using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24: in the chassis diagram editor a label grabbed anywhere but
/// dead centre jumped under the mouse on the first move of a drag, because
/// the drag put the label's middle where the mouse was.
/// </summary>
public sealed class BugHunt24_ChassisCanvasTests
{
    [Fact]
    public void LabelDragOffset_GrabbedLeftOfMiddle_MovesTheMiddleByTheMouseMoveOnly()
    {
        // A 400x300 diagram at (100, 50); the point sits in its middle and
        // its label's middle is kept 80 px right of it.
        var bounds = new Rectangle(100, 50, 400, 300);
        const float pointX = 0.5f, pointY = 0.5f;
        var middleX = 100 + 400 * 0.5f + 80;
        var middleY = 50 + 300 * 0.5f;

        // Pressed 40 px left of the label's middle, then moved 1 px right.
        var grab = new PointF(-40, 0);
        var (ox, oy) = ChassisCanvas.LabelDragOffset(middleX - 40 + 1, middleY, grab, bounds, pointX, pointY);

        var newMiddleX = bounds.Left + bounds.Width * (pointX + ox);
        var newMiddleY = bounds.Top + bounds.Height * (pointY + oy);

        Assert.Equal(middleX + 1, newMiddleX, 2);
        Assert.Equal(middleY, newMiddleY, 2);
    }

    [Fact]
    public void LabelDragOffset_GrabbedDeadCentre_PutsTheMiddleUnderTheMouseAsBefore()
    {
        var bounds = new Rectangle(0, 0, 200, 100);
        var (ox, oy) = ChassisCanvas.LabelDragOffset(150, 25, PointF.Empty, bounds, 0.5f, 0.5f);

        Assert.Equal(0.25f, ox, 4);
        Assert.Equal(-0.25f, oy, 4);
    }

    [Fact]
    public void LabelDragOffset_StillClampedToTheDiagram()
    {
        var bounds = new Rectangle(0, 0, 100, 100);
        var (ox, oy) = ChassisCanvas.LabelDragOffset(5000, -5000, PointF.Empty, bounds, 0.5f, 0.5f);

        Assert.Equal(1f, ox);
        Assert.Equal(-1f, oy);
    }

    [Fact]
    public void LongLabelGrabbedByItsEnd_DoesNotJumpOnTheFirstMove()
    {
        var field = new SetupField
        {
            Id = "shaft",
            Label = "Upper suspension shaft",
            OnDiagram = true,
            X = 0.5f,
            Y = 0.5f,
            LabelOffsetX = 0.20f,
            LabelOffsetY = -0.06f,
        };
        var template = new ChassisTemplate
        {
            Sections = { new SetupSection { Fields = { field } } },
        };

        using var canvas = new ChassisCanvas { Size = new Size(900, 600), EditMode = true };
        canvas.SetTemplate(template, null);

        var before = Boxes(canvas)["shaft"];
        var pressX = before.Left + 3;
        var pressY = before.Top + before.Height / 2;

        Mouse(canvas, "OnMouseDown", pressX, pressY);
        var pressed = Boxes(canvas)["shaft"];

        Mouse(canvas, "OnMouseMove", pressX + 1, pressY);
        var moved = Boxes(canvas)["shaft"];

        Mouse(canvas, "OnMouseUp", pressX + 1, pressY);

        // One pixel of mouse is about one pixel of label - not half its width.
        Assert.InRange(moved.X - pressed.X, 0, 2);
        Assert.InRange(moved.Y - pressed.Y, -1, 1);
    }

    private static Dictionary<string, Rectangle> Boxes(ChassisCanvas canvas) =>
        (Dictionary<string, Rectangle>)typeof(ChassisCanvas)
            .GetMethod("LabelBoxes", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(canvas, null)!;

    private static void Mouse(ChassisCanvas canvas, string handler, int x, int y) =>
        typeof(ChassisCanvas)
            .GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(MouseEventArgs) }, null)!
            .Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, x, y, 0) });
}
