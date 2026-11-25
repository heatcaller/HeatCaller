using System.Text.RegularExpressions;

namespace HeatCaller.Tests;

/// <summary>
/// Every menu themed (24 Nov 2026, asked for by Asher: "unthemed submenu.
/// sweep the whole app and fix"). The report-card menu on Race History came
/// up as a stock white Windows menu over the dark grid: it, the Drivers
/// roster's and the tray icon's were never passed to StyleContextMenu, and
/// a submenu filled in after its menu was styled missed the theme as well.
/// </summary>
public sealed class ThemedMenusEverywhereTests
{
    [Fact]
    public void A_submenu_nobody_styled_is_drawn_by_the_themed_renderer()
    {
        var before = ToolStripManager.Renderer;

        try
        {
            AppTheme.ThemeEveryMenu();

            using var menu = new ContextMenuStrip();
            var parent = new ToolStripMenuItem("Export");
            menu.Items.Add(parent);

            // Added later, as a menu filled in when it opens does.
            parent.DropDownItems.Add(new ToolStripMenuItem("As a picture"));

            Assert.Equal("ThemedMenuRenderer", menu.Renderer.GetType().Name);
            Assert.Equal("ThemedMenuRenderer", parent.DropDown.Renderer.GetType().Name);
        }
        finally
        {
            ToolStripManager.Renderer = before;
        }
    }

    [Fact]
    public void The_app_makes_the_themed_renderer_the_default_at_start()
    {
        Assert.Contains("AppTheme.ThemeEveryMenu();", Source("Program.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Menus_are_the_only_tool_strips_so_the_default_is_safe()
    {
        // The menu renderer draws a rounded card: a tool bar, a menu bar or
        // a status bar would be drawn as one too. Give any of those its own
        // renderer, then update this test.
        var strips = new Regex(@"new\s+(MenuStrip|ToolStrip|StatusStrip)\s*\(");

        foreach (var file in AppSources())
        {
            Assert.False(strips.IsMatch(File.ReadAllText(file)), $"{Path.GetFileName(file)} makes a tool strip - see ThemeEveryMenu.");
        }
    }

    [Fact]
    public void Every_context_menu_made_in_the_app_is_styled()
    {
        var made = new Regex(@"new\s+ContextMenuStrip\s*\(|ContextMenuStrip\??\s+\w+\s*=\s*new\s*\(\s*\)");

        foreach (var file in AppSources())
        {
            var text = File.ReadAllText(file);
            var menus = made.Matches(text).Count;

            if (menus == 0)
            {
                continue;
            }

            var styled = Regex.Matches(text, @"AppTheme\.StyleContextMenu\(|\bStyleContextMenu\(").Count;

            // A partial class may style its menus in another of its files.
            if (styled < menus)
            {
                var stem = Path.GetFileNameWithoutExtension(file).Split('.')[0];
                styled = Directory.GetFiles(Path.GetDirectoryName(file)!, stem + "*.cs")
                    .Sum(other => Regex.Matches(File.ReadAllText(other), @"\bStyleContextMenu\(").Count);
            }

            Assert.True(styled >= menus, $"{Path.GetFileName(file)} makes {menus} menu(s) but styles {styled} - call AppTheme.StyleContextMenu on each.");
        }
    }

    [Fact]
    public void The_menus_that_were_white_are_styled()
    {
        Assert.Contains("AppTheme.StyleContextMenu(cardMenu);", Source("RaceHistoryControl.cs"), StringComparison.Ordinal);
        Assert.Contains("AppTheme.StyleContextMenu(cardMenu);", Source("ProfileManagerControl.cs"), StringComparison.Ordinal);
        Assert.Contains("AppTheme.StyleContextMenu(menu);", Source("MainForm.Tray.cs"), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------

    private static string AppFolder
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);

            while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "check.cmd")))
            {
                folder = folder.Parent;
            }

            Assert.NotNull(folder);
            return Path.Combine(folder!.FullName, "HeatCaller", "HeatCaller");
        }
    }

    private static IEnumerable<string> AppSources() =>
        Directory.GetFiles(AppFolder, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    private static string Source(string file) => File.ReadAllText(Path.Combine(AppFolder, file));
}
