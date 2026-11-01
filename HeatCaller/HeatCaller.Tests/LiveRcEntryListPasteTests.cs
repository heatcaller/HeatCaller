namespace HeatCaller.Tests;

/// <summary>
/// Paste a Table and a LiveRC entry list (1 Nov 2026, asked for by Asher):
/// a club switching over copies its own track's entry list off LiveRC and
/// pastes it on Driver Profiles. The page is one table per class, headed
/// "10th Scale Entries: 16", with "#", "Driver" and "Transponder #"
/// columns, and every name in it twice ("BLACK, KALEB" and "KALEB BLACK"
/// in one cell). The samples here are laid out the way the page reads.
/// </summary>
public class LiveRcEntryListPasteTests
{
    private static string ClipboardHtml(string fragment) =>
        "Version:0.9\r\nStartHTML:0000000105\r\nEndHTML:0000000999\r\n"
        + "StartFragment:0000000141\r\nEndFragment:0000000900\r\nSourceURL:https://example.liverc.com/results/\r\n"
        + "<html><body>\r\n<!--StartFragment-->" + fragment + "<!--EndFragment-->\r\n</body></html>";

    private static string ClassTable(string className, int entries, params (string Last, string First, string Transponder)[] drivers)
    {
        var rows = string.Concat(drivers.Select((d, i) =>
            $"<tr><td>{i + 1}</td>"
            + $"<td><span class=\"hidden-xs\">{d.Last}, {d.First}</span><span class=\"visible-xs\">{d.First} {d.Last}</span></td>"
            + $"<td>{d.Transponder}</td></tr>"));

        return "<table class=\"table table-striped entry_list_data\">"
            + $"<thead><tr><th colspan=\"3\">{className}<br>Entries: {entries}</th></tr>"
            + "<tr><th>#</th><th>Driver</th><th>Transponder #</th></tr></thead>"
            + "<tbody><tr><th>#</th><th>Driver</th><th>Transponder #</th></tr>"
            + rows
            + "</tbody></table>";
    }

    private static string EntryListPage() => ClipboardHtml(
        "<ul class=\"nav nav-tabs\"><li><a href=\"#c1\">10th Scale</a></li><li><a href=\"#c2\">1-8th Novice</a></li></ul>"
        + ClassTable("10th Scale", 3,
            ("BARTHOLOMEW", "JOSH", "9877106"),
            ("BLACK", "KALEB", "3745120"),
            ("DILLON", "BLAKE", "2217666"))
        + ClassTable("1-8th Novice", 2,
            ("BALIBAN", "ANDREW", "9912057"),
            ("BLACK", "KALEB", "3745120")));

    [Fact]
    public void Every_class_table_on_the_page_is_read_not_just_the_biggest()
    {
        var table = PastedTable.Read(EntryListPage(), null)!;

        Assert.Equal(5, table.Rows.Count);
    }

    [Fact]
    public void The_class_comes_from_each_tables_title_without_its_entry_count()
    {
        var table = PastedTable.Read(EntryListPage(), null)!;
        var classColumn = table.Headers.ToList().IndexOf(PastedTable.ClassHeading);

        Assert.True(classColumn >= 0);
        Assert.Equal(
            new[] { "10th Scale", "10th Scale", "10th Scale", "1-8th Novice", "1-8th Novice" },
            table.Rows.Select(r => r[classColumn]));
    }

    [Fact]
    public void A_doubled_name_cell_comes_in_once_in_ordinary_capitals()
    {
        var table = PastedTable.Read(EntryListPage(), null)!;

        Assert.Equal(
            new[] { "Josh Bartholomew", "Kaleb Black", "Blake Dillon", "Andrew Baliban", "Kaleb Black" },
            table.Rows.Select(r => r[1]));
    }

    [Fact]
    public void The_columns_are_guessed_as_transponder_driver_and_class_and_the_row_count_is_ignored()
    {
        var table = PastedTable.Read(EntryListPage(), null)!;
        var guesses = DriverImport.GuessAll(table.Headers);

        Assert.Equal(PastedTable.RowCountHeading, table.Headers[0]);
        Assert.Equal(ImportColumn.Ignore, guesses[0]);
        Assert.Equal(ImportColumn.DriverName, guesses[1]);
        Assert.Equal(ImportColumn.Transponder, guesses[2]);
        Assert.Equal(ImportColumn.ClassName, guesses[3]);
        Assert.Equal("3745120", table.Rows[1][2]);
    }

    [Fact]
    public void A_copy_of_one_class_tab_is_read_the_same_way()
    {
        var html = ClipboardHtml(ClassTable("Mini-Z Stock", 2, ("O'BRIEN", "MARY ANN", "1111111"), ("MCDONALD", "SAM", "2222222")));

        var table = PastedTable.Read(html, null)!;

        Assert.Equal(new[] { "Mary Ann O'Brien", "Sam McDonald" }, table.Rows.Select(r => r[1]));
        Assert.All(table.Rows, r => Assert.Equal("Mini-Z Stock", r[3]));
    }

    [Fact]
    public void Tab_separated_text_with_the_count_on_its_own_line_still_gets_its_class()
    {
        var text = "10th Scale\r\nEntries: 2\r\n#\tDriver\tTransponder #\r\n"
            + "1\tBLACK, KALEBKALEB BLACK\t3745120\r\n"
            + "2\tDILLON, BLAKE BLAKE DILLON\t2217666\r\n";

        var table = PastedTable.Read(null, text)!;

        Assert.Equal(new[] { "Kaleb Black", "Blake Dillon" }, table.Rows.Select(r => r[1]));
        Assert.All(table.Rows, r => Assert.Equal("10th Scale", r[^1]));
    }

    [Theory]
    [InlineData("BLACK, KALEBKALEB BLACK", "Kaleb Black")]
    [InlineData("BLACK, KALEB KALEB BLACK", "Kaleb Black")]
    [InlineData("SMITH-JONES, MARY ANNMARY ANN SMITH-JONES", "Mary Ann Smith-Jones")]
    [InlineData("Garcia, JoséJosé Garcia", "José Garcia")]
    [InlineData("SMITH III, JOHNJOHN SMITH III", "John Smith III")]
    public void A_doubled_name_is_put_back_to_one(string cell, string expected)
    {
        Assert.Equal(expected, PastedTable.UndoubledName(cell));
    }

    [Theory]
    [InlineData("Smith, Jr.")]
    [InlineData("Billy Smith")]
    [InlineData("BLACK, KALEB")]
    [InlineData("BLACK, KALEB JONES")]
    [InlineData("")]
    public void Anything_else_is_left_as_it_is(string cell)
    {
        Assert.Null(PastedTable.UndoubledName(cell));
    }

    [Theory]
    [InlineData("10th Scale Entries: 16", "10th Scale")]
    [InlineData("Stock (12 entries)", "Stock")]
    [InlineData("Modified - 8 drivers", "Modified")]
    [InlineData("Mini-Z Stock - A-Main", null)]
    [InlineData("Entries: 16", null)]
    public void A_class_title_is_read_only_when_it_carries_an_entry_count(string title, string? expected)
    {
        Assert.Equal(expected, PastedTable.ClassFromTitle(new[] { title, "", "" }));
    }

    [Fact]
    public void A_table_with_its_own_class_column_keeps_it_and_gets_no_second_one()
    {
        var html = ClipboardHtml(
            "<table><tr><th colspan=3>Club night Entries: 2</th></tr>"
            + "<tr><th>Name</th><th>Transponder</th><th>Class</th></tr>"
            + "<tr><td>Billy Smith</td><td>111</td><td>Stock</td></tr>"
            + "<tr><td>Ann Lee</td><td>222</td><td>Modified</td></tr></table>");

        var table = PastedTable.Read(html, null)!;

        Assert.Equal(new[] { "Name", "Transponder", "Class" }, table.Headers);
        Assert.Equal("Modified", table.Rows[1][2]);
    }

    [Fact]
    public void A_real_number_column_is_still_taken_for_race_numbers()
    {
        var html = ClipboardHtml(
            "<table><tr><th colspan=3>Stock Entries: 2</th></tr>"
            + "<tr><th>#</th><th>Driver</th><th>Transponder</th></tr>"
            + "<tr><td>14</td><td>Billy Smith</td><td>111</td></tr>"
            + "<tr><td>7</td><td>Ann Lee</td><td>222</td></tr></table>");

        var table = PastedTable.Read(html, null)!;

        Assert.Equal("#", table.Headers[0]);
        Assert.Equal(ImportColumn.RaceNumber, DriverImport.GuessAll(table.Headers)[0]);
    }

    [Fact]
    public void A_menu_table_beside_the_class_tables_is_still_left_out()
    {
        var html = ClipboardHtml(
            "<table><tr><th>Track</th><th>Phone</th></tr><tr><td>Home</td><td>555</td></tr></table>"
            + ClassTable("Stock", 1, ("LEE", "ANN", "222")));

        var table = PastedTable.Read(html, null)!;

        Assert.Equal("Ann Lee", table.Rows.Single()[1]);
    }
}
