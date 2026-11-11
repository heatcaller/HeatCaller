using System.Text.RegularExpressions;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 16 (10 Nov 2026): a text-size change (Ctrl+ / Ctrl-) let the
/// page on screen go parents first, the slow order LayoutFreeze's notes warn
/// about, so every press stalled on the heavy pages (Voice, Commentary) the
/// way opening them did before the 14 Oct fix to ShowPage.
///
/// Read off the source because the stall needs the whole window and a heavy
/// page to show; the order itself is tested in LayoutFreezeTests
/// (PiecesAreLetGoChildrenFirstSoTheirContainerIsLaidOutOnce).
/// </summary>
public sealed class BugHunt16_TextSizeThawTests
{
    private static string AppFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller");
    }

    private static string Source(string file) =>
        File.ReadAllText(Path.Combine(AppFolder(), file)).Replace("\r\n", "\n");

    [Fact]
    public void A_text_size_change_lets_the_page_on_screen_go_children_first()
    {
        var chrome = Source("MainForm.Chrome.cs");
        var start = chrome.IndexOf("private void ApplyFontScale()", StringComparison.Ordinal);

        Assert.True(start > 0, "MainForm.ApplyFontScale has been renamed - this test reads its body.");

        var end = chrome.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        var body = chrome[start..end];

        Assert.Contains("LayoutFreeze.Thaw(shown!, childrenFirst: true);", body);
    }

    [Fact]
    public void Every_page_the_app_lets_go_is_let_go_children_first()
    {
        // Every runtime letting-go is of a whole page whose containers all
        // want laying out, so none of them wants the parents-first order.
        var calls = Directory.GetFiles(AppFolder(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), @"LayoutFreeze\.Thaw\([^;]*;")
                .Select(match => (File: Path.GetFileName(path), Call: match.Value)))
            .ToList();

        Assert.NotEmpty(calls);
        Assert.All(calls, call => Assert.True(
            call.Call.Contains("childrenFirst: true", StringComparison.Ordinal),
            $"{call.File}: {call.Call} lets a page go parents first."));
    }
}
