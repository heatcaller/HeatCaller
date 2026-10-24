namespace HeatCaller.Tests;

/// <summary>
/// Paste a Table (22 Oct 2026): a driver list copied off a web page or out
/// of a spreadsheet, read into headings and rows for the same column preview
/// and import as a CSV file (PastedTable).
/// </summary>
public class RoadmapBPasteTableTests
{
    // What Windows puts on the clipboard for a copied table: the CF_HTML
    // header, then the page's markup around the fragment.
    private static string ClipboardHtml(string fragment) =>
        "Version:0.9\r\nStartHTML:0000000105\r\nEndHTML:0000000999\r\n"
        + "StartFragment:0000000141\r\nEndFragment:0000000900\r\nSourceURL:https://example.com/results\r\n"
        + "<html><body>\r\n<!--StartFragment-->" + fragment + "<!--EndFragment-->\r\n</body></html>";

    [Fact]
    public void A_results_table_off_a_web_page_reads_as_headings_and_drivers()
    {
        var html = ClipboardHtml(
            "<table class=\"results\">"
            + "<thead><tr><th>Pos</th><th>Driver</th><th>Transponder</th><th>Laps</th></tr></thead>"
            + "<tbody>"
            + "<tr><td colspan=\"4\"><b>Mini-Z Stock - A-Main</b></td></tr>"
            + "<tr><td>1</td><td><a href=\"/driver/12\">Billy Smith</a></td><td>1234567</td><td>22</td></tr>"
            + "<tr><td>2</td><td>Sam &amp; Jo&nbsp;Jones</td><td>7654321</td><td>21</td></tr>"
            + "</tbody></table>");

        var table = PastedTable.Read(html, "ignored when the HTML has a table");

        Assert.NotNull(table);
        Assert.True(table!.FromWebPage);
        Assert.Equal(new[] { "Pos", "Driver", "Transponder", "Laps" }, table.Headers);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(new[] { "1", "Billy Smith", "1234567", "22" }, table.Rows[0]);
        Assert.Equal("Sam & Jo Jones", table.Rows[1][1]);

        // The guesses the preview starts from.
        var guesses = DriverImport.GuessAll(table.Headers);
        Assert.Equal(ImportColumn.DriverName, guesses[1]);
        Assert.Equal(ImportColumn.Transponder, guesses[2]);
    }

    [Fact]
    public void Headings_repeated_for_each_class_and_class_titles_are_left_out()
    {
        var html = ClipboardHtml(
            "<table>"
            + "<tr><td colspan=3>Stock</td></tr>"
            + "<tr><th>Name</th><th>Transponder</th><th>Class</th></tr>"
            + "<tr><td>Billy Smith</td><td>111</td><td>Stock</td></tr>"
            + "<tr><td></td><td></td><td></td></tr>"
            + "<tr><td colspan=3>Modified</td></tr>"
            + "<tr><th>Name</th><th>Transponder</th><th>Class</th></tr>"
            + "<tr><td>Ann Lee</td><td>222</td><td>Modified</td></tr>"
            + "</table>");

        var table = PastedTable.Read(html, null)!;

        Assert.Equal(new[] { "Name", "Transponder", "Class" }, table.Headers);
        Assert.Equal(new[] { "Billy Smith", "Ann Lee" }, table.Rows.Select(row => row[0]));

        // Each row keeps its place in the table as pasted, for "Row 7".
        Assert.Equal(new[] { 3, 7 }, table.RowNumbers);
    }

    [Fact]
    public void A_cell_across_two_columns_keeps_the_columns_after_it_under_their_headings()
    {
        var html = ClipboardHtml(
            "<table><tr><td>Name</td><td>Transponder</td><td>Car</td><td>Team</td></tr>"
            + "<tr><td>Billy Smith</td><td>111</td><td colspan=\"2\">-</td></tr>"
            + "<tr><td>Ann Lee</td><td>222</td><td>MR-03</td><td>Hornets</td></tr></table>");

        var table = PastedTable.Read(html, null)!;

        Assert.Equal(4, table.Headers.Count);
        Assert.Equal(new[] { "Billy Smith", "111", "-", string.Empty }, table.Rows[0]);
        Assert.Equal("Hornets", table.Rows[1][3]);
    }

    [Fact]
    public void Line_breaks_links_and_entities_in_a_cell_are_read_as_its_words()
    {
        var html = ClipboardHtml(
            "<table><tr><th>Driver</th><th>Transponder</th><th>Team</th></tr>"
            + "<tr><td>Jos&eacute;<br>Garc&#237;a</td><td> 999 </td><td><span><a href=x>Fast &amp; Furious</a></span></td></tr>"
            + "</table>");

        var row = PastedTable.Read(html, null)!.Rows.Single();

        Assert.Equal("José García", row[0]);
        Assert.Equal("999", row[1]);
        Assert.Equal("Fast & Furious", row[2]);
    }

    [Fact]
    public void The_biggest_table_is_the_one_read_when_the_copy_took_in_more()
    {
        var html = ClipboardHtml(
            "<table><tr><td>Menu</td></tr></table>"
            + "<table><tr><th>Name</th><th>Transponder</th></tr>"
            + "<tr><td>Billy Smith</td><td>111</td></tr><tr><td>Ann Lee</td><td>222</td></tr></table>"
            + "<script>var x = '<table><tr><td>not this</td></tr></table>';</script>");

        var table = PastedTable.Read(html, null)!;

        Assert.Equal(new[] { "Name", "Transponder" }, table.Headers);
        Assert.Equal(2, table.Rows.Count);
    }

    [Fact]
    public void A_row_label_th_does_not_make_a_driver_row_into_headings()
    {
        var html = ClipboardHtml(
            "<table><tr><th>Pos</th><th>Driver</th><th>Transponder</th></tr>"
            + "<tr><th>1</th><td>Billy Smith</td><td>111</td></tr></table>");

        var table = PastedTable.Read(html, null)!;

        Assert.Equal(new[] { "1", "Billy Smith", "111" }, table.Rows.Single());
    }

    [Fact]
    public void Tab_separated_text_from_a_spreadsheet_is_read_when_there_is_no_html_table()
    {
        var text = "Name\tTransponder\tClass\r\nBilly Smith\t111\tStock\r\n\r\nAnn Lee\t222\tModified\r\n";

        var table = PastedTable.Read("<html><body><p>no table here</p></body></html>", text)!;

        Assert.False(table.FromWebPage);
        Assert.Equal(new[] { "Name", "Transponder", "Class" }, table.Headers);
        Assert.Equal(new[] { "Billy Smith", "Ann Lee" }, table.Rows.Select(row => row[0]));
    }

    [Fact]
    public void A_list_with_no_headings_gets_numbered_columns_and_loses_no_driver()
    {
        var table = PastedTable.Read(null, "Billy Smith\t1234567\nAnn Lee\t7654321\n")!;

        Assert.Equal(new[] { "Column 1", "Column 2" }, table.Headers);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("Billy Smith", table.Rows[0][0]);
    }

    [Fact]
    public void Nothing_to_import_is_null_rather_than_an_empty_table()
    {
        Assert.Null(PastedTable.Read(null, null));
        Assert.Null(PastedTable.Read(ClipboardHtml("<p>just words</p>"), "   "));
        Assert.Null(PastedTable.Read(ClipboardHtml("<table><tr><th>Name</th><th>Transponder</th></tr></table>"), null));
    }

    [Fact]
    public void Clipboard_html_read_in_the_wrong_code_page_is_put_back()
    {
        Assert.Equal("<td>José</td>", PastedTable.RepairUtf8("<td>JosÃ©</td>"));
        Assert.Equal("<td>Plain</td>", PastedTable.RepairUtf8("<td>Plain</td>"));
    }

    [Theory]
    [InlineData(new[] { "Driver", "Transponder" }, true)]
    [InlineData(new[] { "Pilote", "Puce" }, true)]
    [InlineData(new[] { "Billy Smith", "1234567" }, false)]
    public void A_first_row_is_headings_when_it_reads_like_them(string[] cells, bool expected)
    {
        Assert.Equal(expected, PastedTable.LooksLikeHeadings(cells));
    }
}
