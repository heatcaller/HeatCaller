namespace HeatCaller.Tests;

/// <summary>
/// The printable sheets are a Letter page at 150 DPI in pixels, and the
/// saved PNG has to say so. They used to carry the 96 DPI the page is drawn
/// at, which described a 13.3 x 17.2 inch page, so printing at the image's
/// own size cut the sheet off.
/// </summary>
public class BugHuntSheetsTests
{
    [Fact]
    public void RunSheetPagesDescribeALetterPage()
    {
        var pages = RunSheetRenderer.RenderPages("Club night", Array.Empty<ScheduleEntry>());

        try
        {
            var page = Assert.Single(pages);

            Assert.Equal(150f, page.HorizontalResolution);
            Assert.Equal(150f, page.VerticalResolution);

            // 1275 x 1650 pixels at 150 DPI is 8.5 x 11 inches.
            Assert.Equal(8.5f, page.Width / page.HorizontalResolution, 3);
            Assert.Equal(11f, page.Height / page.VerticalResolution, 3);
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
