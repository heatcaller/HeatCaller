using System.Text.RegularExpressions;

namespace HeatCaller.Tests;

/// <summary>
/// The sidebar narrowed to a rail of icons (Ctrl+B, or the chevron in the
/// page-search row - see MainForm.SetNavIconsOnly).
///
/// What the rail takes away is everything that carries a word: the page
/// names, the group headings, the page search and the app title. So the
/// icons become the whole of the navigation, and the thing that can quietly
/// break it is a new page added to MainForm's _navItems with no shape of its
/// own in AppTheme.DrawNavIcon - whose fallback is a plain dot, and a rail
/// with two identical dots in it is a rail with two pages nobody can tell
/// apart. That is the first test here, and it is the one worth having.
///
/// Most of the rest lives inside a window, where the only way in is a press
/// on a button, so it is checked by reading the source the way InstallerTests
/// checks the order of things in Main - a worse test than a real one and much
/// better than none. What IS driven for real is the list itself: a
/// SidebarNavList at the rail's width, with its groups, painted at every text
/// size in both states, which is where the arithmetic is.
/// </summary>
public sealed class NavRailTests : IDisposable
{
    /// <summary>A folder of its own for the test that reads and writes settings.json - see TempAppData.</summary>
    private readonly TempAppData _data = new();

    private string _now = "starting";

    public void Dispose() => _data.Dispose();

    private static string RepoRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);

            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            throw new DirectoryNotFoundException("No folder with check.cmd in it above " + AppContext.BaseDirectory);
        }
    }

    private static string AppSource(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", file));

    /// <summary>
    /// The sidebar's pages, read out of MainForm's _navItems - the list the
    /// sidebar is actually built from, rather than a copy of it kept here
    /// that would go stale the first time a page was added.
    /// </summary>
    private static List<string> NavPages()
    {
        var source = AppSource("MainForm.Pages.cs");
        var start = source.IndexOf("_navItems = new List<NavItem>", StringComparison.Ordinal);

        Assert.True(start > 0, "MainForm.Pages.cs no longer builds _navItems the way this test reads it.");

        var end = source.IndexOf("};", start, StringComparison.Ordinal);
        Assert.True(end > start, "No end to the _navItems list in MainForm.Pages.cs.");

        var names = Regex.Matches(source[start..end], @"new\(""([^""]+)"",")
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.True(names.Count >= 20, $"Only {names.Count} pages read out of _navItems - the list is read wrongly, not short.");

        return names;
    }

    /// <summary>
    /// The page names AppTheme.DrawNavIcon has a shape for, read out of its
    /// switch - bounded at both ends, so this is that one switch and not
    /// every case label in a 150KB file.
    /// </summary>
    private static List<string> IconCases()
    {
        var theme = AppSource("AppTheme.cs");
        var start = theme.IndexOf("public static void DrawNavIcon", StringComparison.Ordinal);

        Assert.True(start > 0, "AppTheme.DrawNavIcon has been renamed - this test reads its switch.");

        var end = theme.IndexOf("private static System.Drawing.Drawing2D.GraphicsPath BuildStarPath", start, StringComparison.Ordinal);

        Assert.True(end > start, "DrawNavIcon's switch no longer ends where this test expects.");

        return Regex.Matches(theme[start..end], @"case ""([^""]+)"":")
            .Select(m => m.Groups[1].Value)
            .ToList();
    }

    // --- The icons are the whole of the rail --------------------------------

    [Fact]
    public void Every_page_in_the_sidebar_has_an_icon_of_its_own()
    {
        var cases = IconCases().ToHashSet(StringComparer.Ordinal);
        var missing = NavPages().Where(page => !cases.Contains(page)).ToList();

        Assert.True(missing.Count == 0,
            "These sidebar pages fall through to DrawNavIcon's default dot, so in the rail they are "
            + "rows nobody can tell apart: " + string.Join(", ", missing));
    }

    [Fact]
    public void The_switch_this_is_read_from_is_the_one_switch()
    {
        // A case label written twice is a compile error in C#, so a duplicate
        // here can only mean the read above has run past the end of
        // DrawNavIcon and is taking in some other switch's labels - which
        // would make the test above pass on icons that are not nav icons.
        var duplicates = IconCases()
            .GroupBy(label => label, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.True(duplicates.Count == 0,
            "DrawNavIcon's switch is being read past its end - these labels came back twice: "
            + string.Join(", ", duplicates));
    }

    // --- The rail is wide enough for what goes in it ------------------------

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void The_rail_holds_the_biggest_icon_the_text_size_can_ask_for(float scale) => AtTextSize(scale, () =>
    {
        // The icon is drawn at AtTextSize(18) inside the row's plate, and the
        // plate is the list's width less PlateNarrowerBy. That constant is
        // private to SidebarNavList, so it is read back from the source here
        // rather than guessed at: if it changes, this fails and says so
        // instead of the rail quietly clipping its icons.
        var source = AppSource("SidebarNavList.cs");
        var narrower = Regex.Match(source, @"PlateNarrowerBy => UiLayout\.AtDpi\((\d+)\)");

        Assert.True(narrower.Success, "SidebarNavList.PlateNarrowerBy is no longer written the way this test reads it.");

        var plate = MainForm.SidebarNarrowWidth - UiLayout.AtDpi(int.Parse(narrower.Groups[1].Value));
        var icon = UiLayout.AtTextSize(18);

        Assert.True(plate >= icon + 2,
            $"At {scale * 100:0}% the rail's plate is {plate}px across and the icon in it {icon}px - it would be clipped.");
    });

    [Fact]
    public void The_rails_footer_buttons_fit_inside_it()
    {
        // The three footer buttons go from a row to a column in the rail
        // (MainForm.PlaceRailFooter). A column is only worth it if it fits.
        Assert.True(UiLayout.AtDpi(MainForm.RailFooterButtonWidth) <= MainForm.SidebarNarrowWidth,
            $"A {MainForm.RailFooterButtonWidth}px footer button does not fit a {MainForm.SidebarNarrowWidth}px rail.");

        // And the chevron with them, which shares the page-search row.
        Assert.True(UiLayout.AtDpi(MainForm.NavChevronSize) <= MainForm.SidebarNarrowWidth,
            "The chevron does not fit the rail it has to be centred in.");
    }

    [Fact]
    public void The_rail_gives_the_page_beside_it_most_of_the_sidebar_back()
    {
        // The whole point. If this ever stops being true the feature is not
        // worth the chrome it costs.
        Assert.True(MainForm.SidebarNarrowWidth * 2 < MainForm.SidebarWidth,
            $"The rail is {MainForm.SidebarNarrowWidth}px against the sidebar's {MainForm.SidebarWidth}px - barely narrower.");
    }

    // --- The list itself, driven for real -----------------------------------

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void The_list_paints_at_the_rails_width_in_both_states(float scale) => AtTextSize(scale, () =>
    {
        // Shorter than its own rows on purpose, so the scroll thumb is drawn
        // too and the rows are placed against an offset rather than only from
        // the top of the list.
        using var host = Host(UiLayout.AtTextSize(140), out var list);

        list.SetItems(NavPages());
        list.SelectedItem = "Race Control";
        list.SetBadge("Rentals", 3);

        // The list really is at the rail's width, so what is painted below
        // really is painted at it.
        Assert.Equal(MainForm.SidebarNarrowWidth, list.Width);

        // Both states, and back: the headings change height with IconsOnly,
        // so the rows move both ways and the scroll range with them.
        Paint(list, "the names showing");

        list.IconsOnly = true;
        Paint(list, "the rail");

        list.IconsOnly = false;
        Paint(list, "the names showing again");
    });

    [Fact]
    public void A_rail_with_one_page_in_it_still_paints() => AtTextSize(1f, () =>
    {
        // The narrow end of every Math.Max in the painting: one row, no
        // second group, and a list taller than its content.
        using var host = Host(UiLayout.AtTextSize(400), out var list);

        list.IconsOnly = true;
        list.SetItems(new[] { "Race Control" });

        Assert.Equal(MainForm.SidebarNarrowWidth, list.Width);

        Paint(list, "one page");
    });

    /// <summary>
    /// The nav list in a window of its own, at the rail's width and off the
    /// side of the screen.
    ///
    /// A window, rather than a bare control, because DrawToBitmap only
    /// really runs a control's painting once it is in a shown one - and the
    /// painting is the whole point of the two tests above. LayoutTests shows
    /// its windows at -20000 for the same reason.
    ///
    /// The list is given its width outright rather than docked to fill the
    /// window, because Windows will not make a window as narrow as the rail.
    /// Even a borderless form is held to the system's minimum tracking width
    /// (SM_CXMINTRACK - 136px on an ordinary Windows), so a docked list came
    /// out at 136 and the one thing these tests are for, painting at 60, was
    /// not being tested at all. The window is therefore wider than the rail
    /// and the list's own width is what is asserted and what is painted.
    /// </summary>
    private static Form Host(int height, out SidebarNavList list)
    {
        var railHeight = Math.Max(40, height);

        list = new SidebarNavList
        {
            Location = Point.Empty,
            Width = MainForm.SidebarNarrowWidth,
            Height = railHeight,
            SectionOf = NavTiers.SectionOf,
        };

        var host = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-20000, -20000),
            ClientSize = new Size(Math.Max(MainForm.SidebarNarrowWidth, 400), railHeight),
        };

        host.Controls.Add(list);
        host.Show();
        StaThread.Settle();

        return host;
    }

    /// <summary>
    /// Paints the list, then draws it into a bitmap. Nothing is checked about
    /// what comes out - a rail of icons is not something a test can read -
    /// but everything inside OnPaint runs, which is where a rectangle gone
    /// negative at a width this small would throw.
    /// </summary>
    private void Paint(SidebarNavList list, string what)
    {
        _now = "painting " + what;

        list.Refresh();
        StaThread.Settle();

        using var bitmap = new Bitmap(Math.Max(1, list.Width), Math.Max(1, list.Height));

        list.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
    }

    // --- The setting ---------------------------------------------------------

    [Fact]
    public void A_new_install_shows_the_page_names() => Assert.False(AppSettings.Load().NavIconsOnly);

    [Fact]
    public void The_sidebar_comes_back_the_way_it_was_left()
    {
        var settings = AppSettings.Load();
        settings.NavIconsOnly = true;

        Assert.True(settings.Save());
        Assert.True(AppSettings.Load().NavIconsOnly);
    }

    // --- The wiring ----------------------------------------------------------

    [Fact]
    public void Ctrl_B_narrows_the_sidebar_and_is_left_alone_in_Race_Mode()
    {
        var keyboard = AppSource("MainForm.Keyboard.cs");

        Assert.Contains("keyData == (Keys.Control | Keys.B) && !_raceMode", keyboard);
        Assert.Contains("ToggleNavIconsOnly()", keyboard);
    }

    [Fact]
    public void Ctrl_F_opens_the_sidebar_before_it_looks_for_the_search_box()
    {
        // The box is hidden in the rail. Focusing it there would put the
        // caret somewhere nobody can see and swallow everything typed next.
        var keyboard = AppSource("MainForm.Keyboard.cs");
        var ctrlF = keyboard.IndexOf("keyData == (Keys.Control | Keys.F)", StringComparison.Ordinal);
        var focus = keyboard.IndexOf("txtNavSearch.Focus()", ctrlF, StringComparison.Ordinal);
        var open = keyboard.IndexOf("SetNavIconsOnly(false)", ctrlF, StringComparison.Ordinal);

        Assert.True(open > 0 && open < focus, "Ctrl+F focuses the page search without opening the sidebar first.");
    }

    [Fact]
    public void Narrowing_the_sidebar_is_remembered_and_is_not_an_undo_step()
    {
        var chrome = AppSource("MainForm.Chrome.cs");
        var start = chrome.IndexOf("private void SetNavIconsOnly", StringComparison.Ordinal);

        Assert.True(start > 0, "MainForm.SetNavIconsOnly has been renamed.");

        var end = chrome.IndexOf("private void ToggleNavIconsOnly", start, StringComparison.Ordinal);
        Assert.True(end > start, "SetNavIconsOnly no longer sits above ToggleNavIconsOnly.");

        var body = chrome[start..end];

        Assert.Contains("_settings.NavIconsOnly = iconsOnly;", body);
        Assert.Contains("_settings.Save();", body);

        // A view is not a change to the club's data. On the undo stack, a
        // Ctrl+Z after narrowing the sidebar would take back whatever the
        // club really did last instead.
        Assert.DoesNotContain("_undo", body);
    }

    [Fact]
    public void The_chevrons_tooltip_names_the_shortcut()
    {
        // The chevron is the only thing on screen that says the rail can be
        // opened again, so its tooltip is where Ctrl+B is discovered.
        var chrome = AppSource("MainForm.Chrome.cs");

        Assert.Contains("Show the page names (Ctrl+B)", chrome);
        Assert.Contains("Just the icons (Ctrl+B)", chrome);
    }

    private void AtTextSize(float scale, Action check) =>
        StaThread.Run(() =>
        {
            _now = $"text {scale * 100:0}%";
            AppTheme.SetFontScale(scale);

            try
            {
                check();
            }
            finally
            {
                AppTheme.SetFontScale(1f);
            }

            return 0;
        }, TimeSpan.FromMinutes(2), () => _now);
}
