namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): the printed run sheet's date line and Time
/// column.
/// </summary>
[Collection(AppTimeCollection.Name)]
public sealed class BugHunt24_RunSheetRendererTests
{
    [Fact]
    public void A_sheet_saved_after_midnight_is_dated_by_the_race_night()
    {
        var line = RunSheetRenderer.DateLine(new DateTime(2026, 11, 22, 0, 30, 0), 1, 1);

        Assert.Contains(new DateTime(2026, 11, 21).ToString("dddd, MMM d yyyy"), line);
        Assert.DoesNotContain(new DateTime(2026, 11, 22).ToString("dddd"), line);
        Assert.DoesNotContain("page", line);
    }

    [Fact]
    public void A_sheet_saved_after_six_in_the_morning_is_the_new_day()
    {
        var line = RunSheetRenderer.DateLine(new DateTime(2026, 11, 22, 6, 0, 0), 2, 3);

        Assert.Contains(new DateTime(2026, 11, 22).ToString("dddd, MMM d yyyy"), line);
        Assert.EndsWith("page 2 of 3", line);
    }

    [Fact]
    public void The_time_column_follows_the_club_s_clock()
    {
        var was = AppTime.Use24Hour;
        try
        {
            AppTime.Use24Hour = false;
            Assert.Equal(AppTime.TimeOfDay(new TimeSpan(19, 40, 0)), RunSheetRenderer.TimeText("19:40"));
            Assert.NotEqual("19:40", RunSheetRenderer.TimeText("19:40"));

            AppTime.Use24Hour = true;
            Assert.Equal("19:40", RunSheetRenderer.TimeText("19:40"));

            // Times typed by hand are printed as typed.
            Assert.Equal("TBD", RunSheetRenderer.TimeText("TBD"));
            Assert.Equal("7:30 PM", RunSheetRenderer.TimeText("7:30 PM"));
            Assert.Equal(string.Empty, RunSheetRenderer.TimeText(string.Empty));
        }
        finally
        {
            AppTime.Use24Hour = was;
        }
    }
}
