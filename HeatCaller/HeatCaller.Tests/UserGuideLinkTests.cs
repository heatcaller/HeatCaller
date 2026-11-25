using System.Reflection;
using System.Text.RegularExpressions;

namespace HeatCaller.Tests;

/// <summary>
/// The user guide online at heatcaller.com/docs/, and the ways to it from the
/// app (25 Nov 2026, asked for by Asher: "put the guide online and link the
/// app to it, then find a better place in the app for people to find").
///
/// The old behaviour: the only way to the guide was the About box's
/// "Guides and help: heatcaller.com" link, two clicks from Backup &amp;
/// Restore, which opened the home page - the guide was online nowhere.
/// </summary>
public sealed class UserGuideLinkTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Couldn't find the repo root");
        return dir!.FullName;
    }

    private static string AppSource(string file) => File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", file));

    /// <summary>Each page in mkdocs.yml's nav, as the folder MkDocs builds it into: "race-day/track-designer/", "" for the home.</summary>
    private static HashSet<string> GuideFolders()
    {
        var yml = File.ReadAllText(Path.Combine(RepoRoot(), "website", "mkdocs.yml"));
        var nav = yml[yml.IndexOf("\nnav:", StringComparison.Ordinal)..];

        return Regex.Matches(nav, @":\s*([A-Za-z0-9/_-]+)\.md\s*$", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Select(page => page == "index" ? string.Empty
                : page.EndsWith("/index", StringComparison.Ordinal) ? page[..^"index".Length]
                : page + "/")
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The sidebar's pages, by name, as MainForm.Pages.cs lists them.</summary>
    private static List<string> SidebarPages()
    {
        var pages = AppSource("MainForm.Pages.cs");
        return Regex.Matches(pages, @"new\(""([^""]+)"", page[A-Za-z]+\)").Select(m => m.Groups[1].Value).Distinct().ToList();
    }

    [Fact]
    public void The_guide_is_at_heatcaller_com_docs_and_the_site_is_built_for_that_address()
    {
        Assert.Equal("https://heatcaller.com/docs/", AppSupport.GuideUrl);

        var yml = File.ReadAllText(Path.Combine(RepoRoot(), "website", "mkdocs.yml"));
        Assert.Contains("\nsite_url: https://heatcaller.com/docs/", yml, StringComparison.Ordinal);
        Assert.Contains("font: false", yml, StringComparison.Ordinal);

        // The mark on emails and results is still the plain web address.
        Assert.Equal("heatcaller.com", AppSupport.DocsText);
    }

    [Fact]
    public void Every_sidebar_page_opens_a_page_the_guide_has()
    {
        var pages = SidebarPages();
        var folders = GuideFolders();

        Assert.True(pages.Count >= 20, $"only {pages.Count} sidebar pages found: {string.Join(", ", pages)}");
        Assert.True(folders.Count >= 40, $"only {folders.Count} guide pages found");

        foreach (var page in pages)
        {
            Assert.True(GuidePages.ByPage.ContainsKey(page), $"\"{page}\" has no guide page in GuidePages");
        }

        foreach (var (page, folder) in GuidePages.ByPage)
        {
            Assert.True(folders.Contains(folder), $"\"{page}\" opens {folder}, which isn't a page in mkdocs.yml's nav");
            Assert.Equal(AppSupport.GuideUrl + folder, GuidePages.For(page));
        }

        // A page the guide has no page of its own for - and none - open its start.
        Assert.Equal(AppSupport.GuideUrl, GuidePages.For("Some New Page"));
        Assert.Equal(AppSupport.GuideUrl, GuidePages.For(null));
    }

    [Fact]
    public void Four_footer_buttons_fit_the_open_sidebar_s_row_where_three_were_60_wide()
    {
        Assert.Equal(UiLayout.AtDpi(60), MainForm.WideFooterButtonWidth(3));

        var four = MainForm.WideFooterButtonWidth(4);
        Assert.True(four * 4 + UiLayout.AtDpi(8) * 3 <= UiLayout.AtDpi(196), $"four of {four} don't fit the row");
        Assert.True(four >= UiLayout.AtDpi(42), $"each is still big enough to press: {four}");
    }

    [Fact]
    public void The_User_Guide_button_is_in_the_sidebar_footer_open_or_narrowed_and_opens_the_guide_for_the_page_shown()
    {
        var opened = new List<string>();
        var before = AppSupport.OpenGuide;

        try
        {
            var seen = StaThread.Run(() =>
            {
                using var data = new TempAppData();

                new AppSettings
                {
                    StartMaximized = false,
                    HasCompletedFirstRun = true,
                    HasSeenRaceDaySetup = true,
                    AutoBackupEnabled = false,
                    ConnectDecodersAtStartup = false,
                }.Save();

                AppSupport.OpenGuide = opened.Add;
                var window = new MainForm { LayoutCheckMode = true, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };

                try
                {
                    window.Show();
                    StaThread.Settle();
                    window.SizeContentForLayoutCheck(new Size(1280, 800));
                    StaThread.Settle();

                    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    var guide = (Button)typeof(MainForm).GetField("btnGuide", flags)!.GetValue(window)!;
                    var footer = (Panel)typeof(MainForm).GetField("pnlSidebarFooter", flags)!.GetValue(window)!;
                    var undo = (Button)typeof(MainForm).GetField("btnUndo", flags)!.GetValue(window)!;
                    var tip = (ToolTip)typeof(MainForm).GetField("_footerTooltip", flags)!.GetValue(window)!;

                    bool Shows() => guide.Parent == footer && guide.Visible && footer.ClientRectangle.Contains(guide.Bounds);

                    var wide = Shows() && guide.Top == undo.Top && guide.Left > undo.Right;
                    var tipText = tip.GetToolTip(guide);

                    typeof(MainForm).GetMethod("GoToPage", flags)!.Invoke(window, new object[] { "Sounds" });
                    StaThread.Settle();
                    guide.PerformClick();

                    typeof(MainForm).GetMethod("SetNavIconsOnly", flags)!.Invoke(window, new object[] { true, false, false });
                    StaThread.Settle();
                    var rail = Shows() && guide.Top > undo.Bottom && Math.Abs(guide.Left - undo.Left) <= 1;

                    typeof(MainForm).GetMethod("GoToPage", flags)!.Invoke(window, new object[] { "Race Control" });
                    StaThread.Settle();
                    guide.PerformClick();

                    typeof(MainForm).GetMethod("SetNavIconsOnly", flags)!.Invoke(window, new object[] { false, false, false });
                    StaThread.Settle();

                    return (wide, rail, tipText, guide.AccessibleName);
                }
                finally
                {
                    window.Close();
                    window.Dispose();
                    StaThread.Settle();
                }
            }, TimeSpan.FromMinutes(3), () => "the User Guide button");

            Assert.True(seen.wide, "across the open sidebar, after Undo in the footer's row");
            Assert.True(seen.rail, "narrowed to the rail, under Undo in the column");
            Assert.Equal("User Guide: how this page works", seen.tipText);
            Assert.Equal("User Guide", seen.AccessibleName);
            Assert.Equal(new[] { GuidePages.For("Sounds"), GuidePages.For("Race Control") }, opened);
            Assert.Equal("https://heatcaller.com/docs/announcer/sounds-and-music/", opened[0]);
        }
        finally
        {
            AppSupport.OpenGuide = before;
        }
    }

    [Fact]
    public void The_Getting_Help_card_has_User_Guide_and_the_About_box_links_to_the_guide_itself()
    {
        var backup = AppSource("BackupRestoreControl.cs");
        Assert.Contains("new Button { Text = \"User Guide...\" }", backup, StringComparison.Ordinal);
        Assert.Contains("AppSupport.OpenGuide(AppSupport.GuideUrl)", backup, StringComparison.Ordinal);
        Assert.Contains("With(btnGuide, btnSupport, btnAbout, btnTerms)", backup, StringComparison.Ordinal);

        var about = AppSource("About.cs");
        Assert.Contains("Link(\"User guide: heatcaller.com/docs\", \"heatcaller.com/docs\", AppSupport.GuideUrl)", about, StringComparison.Ordinal);
        Assert.DoesNotContain("AppSupport.DocsUrl)", about, StringComparison.Ordinal);

        // The page search finds it under the words people use for it.
        var navigation = AppSource("MainForm.Navigation.cs");
        foreach (var word in new[] { "\"guide\"", "\"user guide\"", "\"manual\"" })
        {
            Assert.Contains(word, navigation, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_guide_s_build_strips_the_writers_notes_and_stops_on_a_broken_link()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "website", "build-site.ps1"));
        Assert.Contains("--strict", script, StringComparison.Ordinal);
        Assert.Contains("<!--(?!\\[if).*?-->", script, StringComparison.Ordinal);
        Assert.Contains("TODO:|screenshot:", script, StringComparison.Ordinal);
    }
}
