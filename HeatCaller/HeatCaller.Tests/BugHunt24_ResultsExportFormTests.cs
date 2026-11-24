namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): Export Results lists each CSV's columns under
/// the format box. The Results CSV list left out ClassPosition and the Lap
/// CSV list never mentioned the sector columns, so the words on screen were
/// not true of the file. These hold the words to the header the file has.
/// </summary>
public sealed class BugHunt24_ResultsExportFormTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static string[] HeaderOf(ResultsExporter.Format format) =>
        ResultsExporter.Build(format, Array.Empty<SessionRecord>(), null, ProfileStore.Load())
            .TrimStart('﻿')
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[0]
            .Split(',');

    private static string[] ListedColumns(ResultsExporter.Format format)
    {
        var words = ResultsExportForm.ShapeOf(format);
        Assert.StartsWith("Columns: ", words);
        return words["Columns: ".Length..]
            .Split(',')
            .Select(c => c.Trim())
            .ToArray();
    }

    [Fact]
    public void ResultsCsv_words_list_every_column_of_the_file_in_order()
    {
        Assert.Equal(HeaderOf(ResultsExporter.Format.ResultsCsv), ListedColumns(ResultsExporter.Format.ResultsCsv));
        Assert.Contains("ClassPosition", ResultsExportForm.ShapeOf(ResultsExporter.Format.ResultsCsv));
    }

    [Fact]
    public void LapCsv_words_list_the_file_columns_in_order_and_say_sector_columns_follow()
    {
        var header = HeaderOf(ResultsExporter.Format.LapCsv);
        var listed = ListedColumns(ResultsExporter.Format.LapCsv);

        // The fixed columns, exactly as the file has them with no sectors.
        Assert.Equal(header, listed.Take(header.Length).ToArray());

        var words = ResultsExportForm.ShapeOf(ResultsExporter.Format.LapCsv);
        Assert.Contains("S1Seconds", words);
        Assert.Contains("S2Seconds", words);
    }
}
