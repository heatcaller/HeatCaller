namespace HeatCaller.Tests;

/// <summary>
/// The scale helpers every drawn part and page size goes through
/// (UiLayout.AtTextSize, AtDpi, PointsForPixels, DpiScale) and the switch that
/// starts the app DPI-aware (Program's --dpi-aware).
///
/// What matters most is what they do NOT do: in a run that wasn't started with
/// the switch - the default, and every test - the screen's DPI changes nothing,
/// so every size is exactly what it was before the helpers existed. The layout
/// test's numbers depend on that.
/// </summary>
public class ScaleTests
{
    [Fact]
    public void WithoutTheSwitchTheScreensDpiChangesNothing()
    {
        Assert.False(Program.StartedDpiAware);
        Assert.Equal(1f, UiLayout.DpiScale);

        foreach (var pixels in new[] { 0, 1, 7, 26, 44, 118, 220, 1280 })
        {
            Assert.Equal(pixels, UiLayout.AtDpi(pixels));
            Assert.Equal((int)Math.Round(pixels * AppTheme.FontScale), UiLayout.AtTextSize(pixels));
        }
    }

    [Fact]
    public void AFontSizedFromPixelsIsUnchangedAt96Dpi()
    {
        using var bitmap = new Bitmap(4, 4);
        bitmap.SetResolution(96, 96);
        using var g = Graphics.FromImage(bitmap);

        foreach (var size in new[] { 6.5f, 3.64f, 16f, 104f * 0.52f, 260f * 0.40f })
        {
            Assert.Equal(size, UiLayout.PointsForPixels(size, g));
        }
    }

    [Fact]
    public void AFontSizedFromPixelsKeepsItsPixelSizeAtAHigherDpi()
    {
        using var bitmap = new Bitmap(4, 4);
        bitmap.SetResolution(144, 144);
        using var g = Graphics.FromImage(bitmap);

        // 30 "points" worked out from pixels, drawn at 144 DPI, would be half
        // as big again as at 96; given back as 20 they come out the same size.
        Assert.Equal(20f, UiLayout.PointsForPixels(30f, g), precision: 4);
    }

    [Theory]
    [InlineData(new[] { "HeatCaller.exe", "--dpi-aware" }, true)]
    [InlineData(new[] { "HeatCaller.exe", " --DPI-Aware " }, true)]
    [InlineData(new[] { "HeatCaller.exe", "--rehearsal", "--dpi-aware" }, true)]
    [InlineData(new[] { "HeatCaller.exe" }, false)]
    [InlineData(new[] { "HeatCaller.exe", "--rehearsal" }, false)]
    [InlineData(new[] { "--dpi-aware" }, false)]
    public void TheSwitchIsReadFromTheCommandLine(string[] commandLine, bool wanted)
    {
        // The first entry is the program itself, as Environment.GetCommandLineArgs gives it.
        Assert.Equal(wanted, Program.DpiAwareWanted(commandLine));
    }
}
