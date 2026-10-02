using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// Reading a driver CSV for import (bug hunt, 2 Oct 2026): a file Excel
/// saved in the Windows code page keeps its accented names, and each row
/// keeps its real line number when blank lines are dropped.
/// </summary>
public sealed class BugHuntCsvImportTests
{
    // "Zoë Müller" as Excel's plain "CSV (Comma delimited)" writes it:
    // code page 1252, no byte order mark. 0xEB and 0xFC are not valid UTF-8.
    private static readonly byte[] Cp1252Name =
        { (byte)'Z', (byte)'o', 0xEB, (byte)' ', (byte)'M', 0xFC, (byte)'l', (byte)'l', (byte)'e', (byte)'r' };

    [Fact]
    public void A_code_page_file_without_a_mark_is_read_as_the_fallback()
    {
        Assert.Equal("Zoë Müller", CsvHelper.DecodeText(Cp1252Name, Encoding.Latin1));
    }

    [Fact]
    public void A_code_page_file_keeps_its_accents_with_the_local_code_page()
    {
        var text = CsvHelper.DecodeText(Cp1252Name);

        Assert.DoesNotContain('�', text);
        Assert.StartsWith("Zo", text);
    }

    [Fact]
    public void A_utf8_file_without_a_mark_is_read_as_utf8()
    {
        var bytes = new UTF8Encoding(false).GetBytes("Zoë Müller");

        Assert.Equal("Zoë Müller", CsvHelper.DecodeText(bytes, Encoding.Latin1));
    }

    [Fact]
    public void A_byte_order_mark_decides_and_is_not_part_of_the_text()
    {
        var utf8 = CsvHelper.FileEncoding.GetPreamble().Concat(Encoding.UTF8.GetBytes("Zoë")).ToArray();
        var utf16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("Zoë")).ToArray();

        Assert.Equal("Zoë", CsvHelper.DecodeText(utf8, Encoding.Latin1));
        Assert.Equal("Zoë", CsvHelper.DecodeText(utf16, Encoding.Latin1));
    }

    [Fact]
    public void Blank_lines_are_dropped_but_the_line_numbers_are_kept()
    {
        var lines = CsvHelper.NonBlankLines("Name,TX\r\nA,1\r\n\r\n   \nB,\rC,3\r\n");

        Assert.Equal(
            new[] { ("Name,TX", 1), ("A,1", 2), ("B,", 5), ("C,3", 6) },
            lines.ToArray());
    }
}
