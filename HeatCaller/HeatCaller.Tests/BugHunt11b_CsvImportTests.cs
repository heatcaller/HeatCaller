namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11b (6 Nov 2026): a roster row with a stray opening quote
/// ("Stock with no closing quote) used to swallow every row down to the next
/// line with any quote in it - Bob and Ann vanished into Sam's class without
/// a skipped count or a row problem. A quoted note typed over two lines in
/// Excel must still come back as one row.
/// </summary>
public sealed class BugHunt11b_CsvImportTests
{
    private static List<List<string>> Rows(string text) =>
        CsvHelper.NonBlankLines(text).Select(line => DriverImport.ParseLine(line.Text, ',')).ToList();

    [Fact]
    public void A_stray_opening_quote_does_not_swallow_the_rows_after_it()
    {
        var text = "Transponder,Name,Class\r\n8467BE,Sam,\"Stock\r\n1234AB,Bob,Mod\r\n5555CC,\"Ann\",Stock\r\n";

        var lines = CsvHelper.NonBlankLines(text);

        Assert.Equal(new[] { 1, 2, 3, 4 }, lines.Select(line => line.LineNumber));

        var rows = Rows(text);
        Assert.Equal(new[] { "8467BE", "Sam", "Stock" }, rows[1]);
        Assert.Equal(new[] { "1234AB", "Bob", "Mod" }, rows[2]);
        Assert.Equal(new[] { "5555CC", "Ann", "Stock" }, rows[3]);
    }

    [Fact]
    public void A_stray_quote_closed_later_by_a_quote_mid_field_joins_nothing()
    {
        // The later quote closes a field that goes on ("Ann"x) - that is not
        // how a note over several lines ends, so nothing is joined.
        var text = "TX,Name,Notes\nA1,Sam,\"Fast\nB2,Bob,Slow\nC3,Ann,\"Quick\" one\n";

        Assert.Equal(new[] { 1, 2, 3, 4 }, CsvHelper.NonBlankLines(text).Select(line => line.LineNumber));
    }

    [Fact]
    public void A_quoted_note_over_two_lines_is_still_one_row()
    {
        var text = "TX,Name,Notes,Class\r\n8467BE,Sam,\"Runs late\r\non Fridays\",Stock\r\n1234AB,Bob,,Mod\r\n";

        var lines = CsvHelper.NonBlankLines(text);

        Assert.Equal(new[] { 1, 2, 4 }, lines.Select(line => line.LineNumber));

        var rows = Rows(text);
        Assert.Equal(new[] { "8467BE", "Sam", "Runs late" + Environment.NewLine + "on Fridays", "Stock" }, rows[1]);
        Assert.Equal(new[] { "1234AB", "Bob", "", "Mod" }, rows[2]);
    }

    [Fact]
    public void A_quoted_note_ending_a_row_over_three_lines_is_still_one_row()
    {
        var text = "TX,Name,Notes\n8467BE,Sam,\"Line one\nline \"\"two\"\"\nline three\"\n1234AB,Bob,\n";

        var rows = Rows(text);

        Assert.Equal(3, rows.Count);
        Assert.Equal("Line one" + Environment.NewLine + "line \"two\"" + Environment.NewLine + "line three", rows[1][2]);
        Assert.Equal(new[] { "1234AB", "Bob", "" }, rows[2]);
    }
}
