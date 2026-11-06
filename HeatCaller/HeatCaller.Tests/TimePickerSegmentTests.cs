using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// The time picker's hour and minute, each on its own (5 Nov 2026, Asher:
/// "add ability to click on hours or minutes and type in a number. or click
/// to select minutes or hours and use the up/down arrows to change just"
/// that one). Driven through the picker's own mouse and key handlers, in a
/// shown window so it can hold the focus.
/// </summary>
public sealed class TimePickerSegmentTests
{
    [Fact]
    public void Clicking_the_minutes_then_Up_and_Down_changes_only_the_minutes() => OnUi(picker =>
    {
        Click(picker, MinutePoint(picker));
        Key(picker, Keys.Up);
        Key(picker, Keys.Up);
        Key(picker, Keys.Down);

        Assert.Equal(new TimeSpan(10, 56, 0), picker.Value);
    });

    [Fact]
    public void Clicking_the_hours_then_Up_changes_only_the_hours() => OnUi(picker =>
    {
        Click(picker, HourPoint(picker));
        Key(picker, Keys.Up);

        Assert.Equal(new TimeSpan(11, 55, 0), picker.Value);
    });

    [Fact]
    public void Clicking_the_hours_and_typing_a_number_sets_the_hours() => OnUi(picker =>
    {
        Click(picker, HourPoint(picker));
        Type(picker, '7');

        Assert.Equal(new TimeSpan(7, 55, 0), picker.Value);
    });

    [Fact]
    public void Clicking_the_minutes_and_typing_two_digits_sets_the_minutes() => OnUi(picker =>
    {
        Click(picker, MinutePoint(picker));
        Type(picker, '3');
        Type(picker, '0');

        Assert.Equal(new TimeSpan(10, 30, 0), picker.Value);
    });

    private static Point HourPoint(TimePicker picker) => Centre(picker, "_hourRect");

    private static Point MinutePoint(TimePicker picker) => Centre(picker, "_minuteRect");

    private static Point Centre(TimePicker picker, string field)
    {
        var rect = (Rectangle)typeof(TimePicker).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(picker)!;
        return new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    }

    private static void Click(TimePicker picker, Point at) =>
        Call(picker, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, at.X, at.Y, 0));

    private static void Key(TimePicker picker, Keys key) => Call(picker, "OnKeyDown", new KeyEventArgs(key));

    private static void Type(TimePicker picker, char c) => Call(picker, "OnKeyPress", new KeyPressEventArgs(c));

    private static void Call(TimePicker picker, string method, EventArgs args) =>
        typeof(Control).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(picker, new object[] { args });

    private static void OnUi(Action<TimePicker> test) => StaThread.Run(() =>
    {
        using var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000) };
        var picker = new TimePicker { Size = new Size(160, 30), Value = new TimeSpan(10, 55, 0) };
        form.Controls.Add(picker);
        form.Show();
        Application.DoEvents();

        test(picker);
        return 0;
    }, TimeSpan.FromSeconds(30), () => "the time picker test");
}
