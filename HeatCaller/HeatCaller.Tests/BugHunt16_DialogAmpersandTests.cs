using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 16: the themed message box took an '&' in its title or message
/// for a shortcut key - "Delete venue "Smith & Sons"?" showed as "Smith
/// _Sons" with the S underlined - where Windows' own message box shows it as
/// typed. The Best lap line on the Analytics overview did the same to a
/// driver's or a layout's name.
/// </summary>
public sealed class BugHunt16_DialogAmpersandTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void The_message_box_draws_an_ampersand_in_its_title_and_message_as_text()
    {
        var seen = StaThread.Run(() =>
        {
            using var form = AppDialog.BuildForLayoutCheck(
                "Delete venue \"Smith & Sons Raceway\"?",
                "Pit & Paddock",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            return Labels(form)
                .Where(l => l.Text.Contains('&'))
                .Select(l => (l.Text, l.UseMnemonic))
                .ToList();
        }, TimeSpan.FromSeconds(30), () => "building a message box with '&' in it");

        Assert.Contains(seen, l => l.Text == "Delete venue \"Smith & Sons Raceway\"?");
        Assert.Contains(seen, l => l.Text == "Pit & Paddock");
        Assert.All(seen, l => Assert.False(l.UseMnemonic, $"\"{l.Text}\" takes '&' for a shortcut key"));
    }

    [Fact]
    public void The_best_lap_line_draws_an_ampersand_in_a_name_as_text()
    {
        var mnemonic = StaThread.Run(() =>
        {
            using var page = new AnalyticsOverviewControl(SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>()));

            var label = (Label)typeof(AnalyticsOverviewControl)
                .GetField("_lblBestWho", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(page)!;

            return label.UseMnemonic;
        }, TimeSpan.FromSeconds(30), () => "building the Analytics overview");

        Assert.False(mnemonic);
    }

    private static IEnumerable<Label> Labels(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is Label label)
            {
                yield return label;
            }

            foreach (var inner in Labels(child))
            {
                yield return inner;
            }
        }
    }
}
