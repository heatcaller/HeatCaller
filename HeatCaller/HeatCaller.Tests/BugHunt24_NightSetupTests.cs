namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), NightSetup.cs: the printed marshal list.
/// </summary>
public class BugHunt24_NightSetupTests
{
    // The screen's DPI can't be changed in a test (the test run is 96 DPI), so
    // the rule is pinned in the source: the page is set to 96 DPI before
    // anything is drawn on it, as every other printed sheet is. The old code
    // drew on a bitmap at the screen's DPI, so under --dpi-aware the
    // point-size text came out 1.5x-2x too big on a fixed line pitch.
    [Fact]
    public void MarshalSheet_PageIsSetTo96DpiBeforeAnythingIsDrawn()
    {
        var source = Source("NightSetup.cs");
        var start = source.IndexOf("internal static class MarshalSheetRenderer", StringComparison.Ordinal);
        Assert.True(start >= 0, "MarshalSheetRenderer not found in NightSetup.cs");

        var renderer = source[start..];
        var newBitmap = renderer.IndexOf("new Bitmap(PageWidth, PageHeight)", StringComparison.Ordinal);
        var at96 = renderer.IndexOf("SetResolution(96, 96)", StringComparison.Ordinal);
        var fromImage = renderer.IndexOf("Graphics.FromImage(bitmap)", StringComparison.Ordinal);

        Assert.True(newBitmap >= 0, "the page bitmap was not found");
        Assert.True(fromImage > newBitmap, "Graphics.FromImage was not found after the page bitmap");
        Assert.True(at96 > newBitmap && at96 < fromImage, "the page must be set to 96 DPI before drawing starts");
    }

    // The printed size is unchanged: the pages still carry the 150 DPI label
    // set after drawing, at the heat sheet's page size.
    [Fact]
    public void MarshalSheet_PagesStillPrintAt150DpiOnTheHeatSheetPageSize()
    {
        var pages = MarshalSheetRenderer.RenderPages(new[]
        {
            ("Stock Buggy", "CORNER MARSHALS - Stock Buggy\nHeat 1: Alex on corner 1\nHeat 2: Sam on corner 2"),
        });

        try
        {
            Assert.Single(pages);
            Assert.Equal(1275, pages[0].Width);
            Assert.Equal(1650, pages[0].Height);
            Assert.Equal(150f, pages[0].HorizontalResolution);
            Assert.Equal(150f, pages[0].VerticalResolution);
        }
        finally
        {
            foreach (var page in pages)
            {
                page.Dispose();
            }
        }
    }

    private static string Source(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", file));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Couldn't find the repo root (check.cmd).");
    }
}
