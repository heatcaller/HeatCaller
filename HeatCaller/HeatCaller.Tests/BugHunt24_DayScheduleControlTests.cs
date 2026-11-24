namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the Timetable: #154 the menu's Edit and Move act
/// on the highlighted line, not wherever the cursor was left; #155 a stored
/// HH:mm time is shown in the club's clock, so a 12-hour sheet no longer
/// mixes "19:40" with "7:30 PM".
/// </summary>
public sealed class BugHunt24_DayScheduleControlTests
{
    // ------------------------------------------------------------ #154 which line the menu acts on

    [Fact]
    public void One_highlighted_line_wins_over_a_cursor_left_on_another()
    {
        // Ctrl-click A, Ctrl-click B, Ctrl-click B again: A highlighted, cursor on B.
        Assert.Equal("A", DayScheduleControl.LineIdToActOn(new[] { "A" }, "B", currentSelected: false));
    }

    [Fact]
    public void The_cursor_line_is_used_when_it_is_the_highlighted_one()
    {
        Assert.Equal("A", DayScheduleControl.LineIdToActOn(new[] { "A" }, "A", currentSelected: true));
        Assert.Equal("B", DayScheduleControl.LineIdToActOn(new[] { "A", "B" }, "B", currentSelected: true));
    }

    [Fact]
    public void Several_highlighted_with_the_cursor_elsewhere_acts_on_none()
    {
        Assert.Null(DayScheduleControl.LineIdToActOn(new[] { "A", "C" }, "B", currentSelected: false));
    }

    [Fact]
    public void Nothing_highlighted_keeps_the_cursor_line()
    {
        Assert.Equal("B", DayScheduleControl.LineIdToActOn(Array.Empty<string?>(), "B", currentSelected: false));
        Assert.Null(DayScheduleControl.LineIdToActOn(Array.Empty<string?>(), null, currentSelected: false));
    }

    [Fact]
    public void The_page_picks_its_line_through_the_rule_not_CurrentRow_alone()
    {
        var page = Source("DayScheduleControl.cs");

        Assert.Contains("LineIdToActOn(selectedIds, current?.Tag as string, current?.Selected == true)", page);
        Assert.DoesNotContain("if (_grid.CurrentRow?.Tag is not string id)", page);
    }

    // ------------------------------------------------------------ #155 the stored time in the club's clock

    [Fact]
    public void The_timetable_shows_stored_times_through_the_clock_rule()
    {
        var page = Source("DayScheduleControl.cs");

        Assert.Contains("_grid.Rows.Add(entry.IsComplete, ShownTime(entry.ScheduledTime),", page);
        Assert.Contains("parts.Add(ShownTime(entry.ScheduledTime));", page);
    }

    private static string Source(string file)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "check.cmd")))
        {
            folder = folder.Parent;
        }

        Assert.NotNull(folder);
        return File.ReadAllText(Path.Combine(folder!.FullName, "HeatCaller", "HeatCaller", file));
    }
}

/// <summary>Bug hunt 24, #155: the Timetable's stored HH:mm follows the 24-hour tick on Themes.</summary>
[Collection(AppTimeCollection.Name)]
public sealed class BugHunt24_DayScheduleControlTimeTests
{
    [Fact]
    public void A_stored_HHmm_reads_in_the_12_hour_clock()
    {
        var was = AppTime.Use24Hour;

        try
        {
            AppTime.Use24Hour = false;
            Assert.Equal("7:40 PM", DayScheduleControl.ShownTime("19:40"));
            Assert.Equal("9:05 AM", DayScheduleControl.ShownTime("09:05"));
            Assert.Equal("9:05 AM", DayScheduleControl.ShownTime("9:05"));
            Assert.Equal("12:10 AM", DayScheduleControl.ShownTime("00:10"));
        }
        finally
        {
            AppTime.Use24Hour = was;
        }
    }

    [Fact]
    public void A_stored_HHmm_stays_HHmm_in_the_24_hour_clock()
    {
        var was = AppTime.Use24Hour;

        try
        {
            AppTime.Use24Hour = true;
            Assert.Equal("19:40", DayScheduleControl.ShownTime("19:40"));
            Assert.Equal("09:05", DayScheduleControl.ShownTime("9:05"));
        }
        finally
        {
            AppTime.Use24Hour = was;
        }
    }

    [Theory]
    [InlineData("7:30 PM")]
    [InlineData("~10:15")]
    [InlineData("TBD")]
    [InlineData("after lunch")]
    [InlineData("")]
    [InlineData("25:99")]
    [InlineData("19:4")]
    public void Anything_but_an_exact_time_is_shown_as_typed(string stored)
    {
        var was = AppTime.Use24Hour;

        try
        {
            AppTime.Use24Hour = false;
            Assert.Equal(stored, DayScheduleControl.ShownTime(stored));
            AppTime.Use24Hour = true;
            Assert.Equal(stored, DayScheduleControl.ShownTime(stored));
        }
        finally
        {
            AppTime.Use24Hour = was;
        }
    }
}
