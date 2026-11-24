namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): a pasted driver list took any row with one
/// cell filled in for a title and left it out without a word, so a new racer
/// with only a name (no transponder or car yet) vanished from the paste that
/// the same sheet opened as a CSV file kept.
/// </summary>
public class BugHunt24_PastedTableTests
{
    private static string ClipboardHtml(string fragment) =>
        "Version:0.9\r\nStartHTML:0000000105\r\nEndHTML:0000000999\r\n"
        + "StartFragment:0000000141\r\nEndFragment:0000000900\r\nSourceURL:https://example.com/drivers\r\n"
        + "<html><body>\r\n<!--StartFragment-->" + fragment + "<!--EndFragment-->\r\n</body></html>";

    [Fact]
    public void A_driver_with_only_a_name_is_kept_not_taken_for_a_title()
    {
        var table = PastedTable.Read(null, "Name\tTransponder\tCar\nBilly Smith\t111\tMR-03\nNew Kid\t\t\nAnn Lee\t222\tMR-03\n")!;

        Assert.Equal(new[] { "Billy Smith", "New Kid", "Ann Lee" }, table.Rows.Select(row => row[0]));
        Assert.Equal(new[] { 2, 3, 4 }, table.RowNumbers);
    }

    [Fact]
    public void A_driver_with_only_a_name_is_kept_from_a_web_table_too()
    {
        var html = ClipboardHtml(
            "<table><tr><th>Driver</th><th>Transponder</th><th>Car</th></tr>"
            + "<tr><td>Billy Smith</td><td>111</td><td>MR-03</td></tr>"
            + "<tr><td>New Kid</td><td></td><td></td></tr>"
            + "<tr><td>Ann Lee</td><td>222</td><td>MR-03</td></tr></table>");

        var table = PastedTable.Read(html, null)!;

        Assert.Equal(new[] { "Billy Smith", "New Kid", "Ann Lee" }, table.Rows.Select(row => row[0]));
    }

    [Fact]
    public void A_driver_with_only_a_name_on_the_last_row_is_kept()
    {
        var table = PastedTable.Read(null, "Name\tTransponder\tCar\nBilly Smith\t111\tMR-03\nNew Kid\t\t\n")!;

        Assert.Equal(new[] { "Billy Smith", "New Kid" }, table.Rows.Select(row => row[0]));
    }

    [Fact]
    public void A_name_only_driver_in_a_class_list_keeps_the_class_for_the_rows_after()
    {
        var html = ClipboardHtml(
            "<table><thead><tr><th colspan=\"3\">Stock Entries: 3</th></tr>"
            + "<tr><th>#</th><th>Driver</th><th>Transponder #</th></tr></thead><tbody>"
            + "<tr><td>1</td><td>Billy Smith</td><td>111</td></tr>"
            + "<tr><td></td><td>New Kid</td><td></td></tr>"
            + "<tr><td>3</td><td>Ann Lee</td><td>222</td></tr></tbody></table>");

        var table = PastedTable.Read(html, null)!;
        var classColumn = table.Headers.ToList().IndexOf(PastedTable.ClassHeading);

        Assert.Equal(new[] { "Billy Smith", "New Kid", "Ann Lee" }, table.Rows.Select(row => row[1]));
        Assert.True(classColumn >= 0);
        Assert.All(table.Rows, row => Assert.Equal("Stock", row[classColumn]));
    }

    [Fact]
    public void A_class_title_over_repeated_headings_is_still_left_out()
    {
        var text = "Name\tTransponder\tClass\nBilly Smith\t111\tStock\nModified\t\t\nName\tTransponder\tClass\nAnn Lee\t222\tModified\n";

        var table = PastedTable.Read(null, text)!;

        Assert.Equal(new[] { "Billy Smith", "Ann Lee" }, table.Rows.Select(row => row[0]));
    }

    [Fact]
    public void A_lone_title_under_a_column_that_is_not_the_name_is_still_left_out()
    {
        var text = "Pos\tDriver\tLaps\n1\tSam Lee\t20\nB-Main\t\t\n1\tAlex Kim\t19\n";

        var table = PastedTable.Read(null, text)!;

        Assert.Equal(new[] { "Sam Lee", "Alex Kim" }, table.Rows.Select(row => row[1]));
    }

    [Fact]
    public void A_lone_class_with_its_count_under_the_name_heading_is_still_a_title()
    {
        var text = "Driver\tTransponder\tCar\nBilly Smith\t111\tMR-03\nModified Entries: 1\t\t\nAnn Lee\t222\tMR-03\n";

        var table = PastedTable.Read(null, text)!;

        Assert.Equal(new[] { "Billy Smith", "Ann Lee" }, table.Rows.Select(row => row[0]));
    }
}
