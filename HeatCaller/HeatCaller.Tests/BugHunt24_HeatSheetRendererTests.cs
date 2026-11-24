namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24: a heat sheet printed after midnight was dated by the calendar
/// day, so a reprint at 00:20 said the next day while the earlier sheets for
/// the same meeting said the race night's day. A race night runs 6am to 6am.
/// </summary>
public sealed class BugHunt24_HeatSheetRendererTests
{
    // Formatted the same way the renderer formats, so the test holds in any
    // culture the test machine runs under.
    private static string Day(int year, int month, int day) =>
        $"{new DateTime(year, month, day):dddd, MMM d yyyy}";

    [Fact]
    public void A_heat_sheet_printed_after_midnight_carries_the_race_nights_date()
    {
        var line = HeatSheetRenderer.HeadingLine(new DateTime(2026, 11, 22, 0, 20, 0), 1, 1);

        Assert.Equal($"Heat sheet  -  {Day(2026, 11, 21)}", line);
    }

    [Fact]
    public void A_heat_sheet_turns_to_the_next_night_at_6am_not_before()
    {
        Assert.Contains(Day(2026, 11, 21), HeatSheetRenderer.HeadingLine(new DateTime(2026, 11, 22, 5, 59, 0), 1, 1));
        Assert.Contains(Day(2026, 11, 22), HeatSheetRenderer.HeadingLine(new DateTime(2026, 11, 22, 6, 0, 0), 1, 1));
        Assert.Contains(Day(2026, 11, 21), HeatSheetRenderer.HeadingLine(new DateTime(2026, 11, 21, 19, 0, 0), 1, 1));
    }

    [Fact]
    public void A_heat_sheet_date_line_still_carries_the_page_count_on_a_long_class()
    {
        var line = HeatSheetRenderer.HeadingLine(new DateTime(2026, 11, 22, 0, 20, 0), 2, 3);

        Assert.Equal($"Heat sheet  -  {Day(2026, 11, 21)}  -  page 2 of 3", line);
        Assert.DoesNotContain("page", HeatSheetRenderer.HeadingLine(new DateTime(2026, 11, 21, 19, 0, 0), 1, 1));
    }
}
