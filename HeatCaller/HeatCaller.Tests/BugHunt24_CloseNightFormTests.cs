using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), finding 131 (F078): Close the Night opened
/// without its "how the night went" line. The constructor read the pace line
/// before the caller's object initializer had set PaceLine, so the line only
/// appeared at the first two-second refresh. The window now reads it again as
/// it loads, before it is sized and shown.
/// </summary>
public sealed class BugHunt24_CloseNightFormTests
{
    private const string Pace = "Night pace: 4 min between races";

    private static string PaceTextAfterLoad(bool ready) => StaThread.Run(() =>
    {
        using var form = new CloseNightForm(
            () => new List<CloseNightChecklist.Item>(),
            (_, _) => { },
            _ => { },
            () => new List<string>(),
            _ => { },
            ready)
        {
            PaceLine = () => Pace,
        };

        // What the window does as it opens, without putting it on screen.
        typeof(Form)
            .GetMethod("OnLoad", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, new object[] { EventArgs.Empty });

        var label = (Label)typeof(CloseNightForm)
            .GetField("_lblPace", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;

        return label.Text;
    }, TimeSpan.FromSeconds(30), () => "opening Close the Night");

    [Fact]
    public void Pace_line_set_in_the_initializer_is_there_as_the_window_opens()
    {
        Assert.Equal(Pace, PaceTextAfterLoad(ready: false));
    }

    [Fact]
    public void Ready_to_race_still_shows_no_pace_line()
    {
        Assert.Equal(string.Empty, PaceTextAfterLoad(ready: true));
    }
}
