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

    [Fact]
    public void Race_Day_sizes_Tonights_program_with_the_grip_not_the_splitter()
    {
        var pages = File.ReadAllText(Path.Combine(SourceDir(), "MainForm.Pages.cs"));

        Assert.Contains("IsSplitterFixed = true", pages, StringComparison.Ordinal);
        Assert.Contains("var programGrip = new ResizeGrip", pages, StringComparison.Ordinal);
        Assert.Contains("ResizeGrip.PaintNow(todaySplit)", pages, StringComparison.Ordinal);
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
