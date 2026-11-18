namespace HeatCaller.Tests;

/// <summary>
/// The drag handle under Race Calls and over Tonight's program (11 Nov
/// 2026): a drag never asks for less than the part can be or more than the
/// page has, and each grip says what it sizes.
/// </summary>
public sealed class ResizeGripTests
{
    [Theory]
    [InlineData(300, 25, 500, 300)]
    [InlineData(10, 25, 500, 25)]
    [InlineData(900, 25, 500, 500)]
    [InlineData(-40, 25, 500, 25)]
    public void A_drag_is_kept_between_the_least_and_the_most(int asked, int least, int most, int expected)
    {
        Assert.Equal(expected, ResizeGrip.Clamp(asked, least, most));
    }

    [Fact]
    public void With_no_room_for_even_the_least_a_drag_asks_for_the_least()
    {
        // A window too short for both halves: Math.Clamp would throw here.
        Assert.Equal(25, ResizeGrip.Clamp(300, 25, 10));
    }

    [Fact]
    public void Each_grip_says_what_it_sizes()
    {
        using var raceCalls = new ResizeGrip();
        Assert.Equal(ResizeGrip.DefaultTip, raceCalls.TipText);

        using var program = new ResizeGrip { TipText = "Drag to make Tonight's program taller or shorter." };
        Assert.Equal("Drag to make Tonight's program taller or shorter.", program.TipText);
    }

    /// <summary>
    /// Race Day's Next Steps had a grip to drag between its card and the
    /// program grid. Since the run sheet (17 Nov 2026) the program IS the
    /// page, with the next race's card on it, so there is no line to drag:
    /// one page, the program in it, and no splitter left behind.
    /// </summary>
    [Fact]
    public void Race_Day_is_one_page_with_the_run_sheet_in_it_not_a_split()
    {
        var pages = File.ReadAllText(Path.Combine(SourceDir(), "MainForm.Pages.cs"));
        var today = File.ReadAllText(Path.Combine(SourceDir(), "TodayControl.cs"));

        Assert.Contains("subPageToday.Controls.Add(today);", pages, StringComparison.Ordinal);
        Assert.DoesNotContain("var todaySplit = new SplitContainer", pages, StringComparison.Ordinal);
        Assert.Contains("middle.AddFill(_program,", today, StringComparison.Ordinal);
        Assert.Contains("_program.HostNowCard(_card);", today, StringComparison.Ordinal);
    }

    private static string SourceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HeatCaller.sln")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("HeatCaller.sln"), "HeatCaller");
    }
}
