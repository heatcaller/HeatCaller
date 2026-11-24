namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the drivers' meeting sheet (TextSheetRenderer).
/// A new Bitmap takes the screen's DPI, so under --dpi-aware at 150% the
/// point-size text drew half as big again against the sheet's fixed pixel
/// layout. A test can't change the process DPI, so it hands DrawPage a bitmap
/// already at 144 DPI - what the screen would have given - and checks the
/// text comes out the same size as at 96.
/// </summary>
public sealed class BugHunt24_ReadyToRaceTests
{
    private const int PageWidth = 1275;
    private const int PageHeight = 1650;
    private const int BodyTop = 70 + 34;
    private const int BodyBottom = BodyTop + 34;

    private static (int Right, int Bottom) BodyLineInk(float dpi)
    {
        using var bitmap = new Bitmap(PageWidth, PageHeight);
        bitmap.SetResolution(dpi, dpi);

        TextSheetRenderer.DrawPage(bitmap, new[] { "Drivers' meeting", new string('M', 40) }, 0, 10);

        var right = -1;
        var bottom = -1;

        for (var y = BodyTop; y < BodyBottom + 20; y++)
        {
            for (var x = 0; x < PageWidth; x++)
            {
                var c = bitmap.GetPixel(x, y);

                if (c.R < 128 && c.G < 128 && c.B < 128)
                {
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        return (right, bottom);
    }

    [Fact]
    public void BugHunt24_The_drivers_meeting_sheet_draws_the_same_size_on_a_scaled_screen()
    {
        var at96 = BodyLineInk(96);
        var at144 = BodyLineInk(144);

        Assert.True(at96.Right > 0, "the body line should leave ink");
        Assert.InRange(at144.Right, at96.Right - 1, at96.Right + 1);
        Assert.InRange(at144.Bottom, at96.Bottom - 1, at96.Bottom + 1);
    }

    [Fact]
    public void BugHunt24_The_drivers_meeting_pages_are_still_labelled_150_dpi_for_printing()
    {
        var pages = TextSheetRenderer.RenderPages("Drivers' meeting\nBe on the stand when your heat is called.");

        try
        {
            Assert.Single(pages);
            Assert.Equal(150f, pages[0].HorizontalResolution, precision: 1);
            Assert.Equal(PageWidth, pages[0].Width);
        }
        finally
        {
            foreach (var page in pages)
            {
                page.Dispose();
            }
        }
    }
}
