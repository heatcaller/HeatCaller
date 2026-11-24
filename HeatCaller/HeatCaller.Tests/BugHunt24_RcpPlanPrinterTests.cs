namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): a plan printed to scale over several sheets
/// left every tile on a sheet join without its piece mark, because a tile
/// was marked only when its whole square was on one sheet.
/// </summary>
public sealed class BugHunt24_RcpPlanPrinterTests
{
    /// <summary>A 12 x 8 plan at 1:10 with 50 cm tiles on Letter landscape: 3 x 3 sheets whose edges miss the grid lines.</summary>
    private static IReadOnlyList<RcpPrintSheet> Sheets()
    {
        var area = new RectangleF(-0.3f, -0.3f, 12.6f, 8.6f);
        var box = RcpPlanPrinter.DrawingBoxMm(new SizeF(215.9f, 279.4f), landscape: true, RcpPlanPrinter.MarginMm);

        return RcpPrintLayout.Sheets(area, RcpPrintLayout.MmPerTile(500, 10), box);
    }

    [Fact]
    public void Every_tile_is_marked_on_exactly_one_sheet_when_the_plan_takes_several()
    {
        var sheets = Sheets();
        Assert.True(sheets.Count > 1);

        for (var col = 0; col < 12; col++)
        {
            for (var row = 0; row < 8; row++)
            {
                Assert.Equal(1, sheets.Count(s => RcpPlanPrinter.MarksTileOnSheet(col, row, s.Tiles)));
            }
        }
    }

    [Fact]
    public void A_tile_cut_by_a_sheet_join_is_marked_on_the_part_its_sheet_shows()
    {
        var sheets = Sheets();

        for (var col = 0; col < 12; col++)
        {
            for (var row = 0; row < 8; row++)
            {
                var sheet = sheets.Single(s => RcpPlanPrinter.MarksTileOnSheet(col, row, s.Tiles));
                var corner = RcpPlanPrinter.MarkCorner(col, row, sheet.Tiles);

                Assert.InRange(corner.X, sheet.Tiles.Left, sheet.Tiles.Right);
                Assert.InRange(corner.Y, sheet.Tiles.Top, sheet.Tiles.Bottom);
                // At least half the tile is left on the sheet past the mark.
                Assert.True(sheet.Tiles.Right - corner.X >= 0.5f - 1e-4f);
                Assert.True(sheet.Tiles.Bottom - corner.Y >= 0.5f - 1e-4f);
            }
        }
    }
}
