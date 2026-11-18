using Xunit;

namespace HeatCaller.Tests;

/// <summary>
/// Race Day's run sheet (17 Nov 2026, plans/race-day-mockups.html, A+B):
/// the words and judgements the sheet, the late strip and the "Get back on
/// time" card are drawn from. See RunSheet.
/// </summary>
public sealed class RunSheetTests
{
    [Theory]
    [InlineData(null, "OnTime")]
    [InlineData(0, "OnTime")]
    [InlineData(9, "OnTime")]
    [InlineData(10, "Watch")]
    [InlineData(29, "Watch")]
    [InlineData(30, "Problem")]
    [InlineData(242, "Problem")]
    public void The_strip_shows_amber_at_ten_minutes_late_and_red_at_thirty(int? minutesLate, string expected)
    {
        TimeSpan? over = minutesLate is { } m ? TimeSpan.FromMinutes(m) : null;

        Assert.Equal(Enum.Parse<RunSheet.Pace>(expected), RunSheet.PaceOf(over));
    }

    [Fact]
    public void The_strip_says_how_late_in_hours_and_minutes()
    {
        Assert.Equal("4 h 02 min late", RunSheet.LateWords(TimeSpan.FromMinutes(242)));
        Assert.Equal("12 min late", RunSheet.LateWords(TimeSpan.FromMinutes(12)));
    }

    [Theory]
    [InlineData("Stock", "Q1 Heat 1", "Stock · Q1 Heat 1")]
    [InlineData("Stock + Mod", "Q2 Heat 1", "Stock + Mod · Q2 Heat 1")]
    [InlineData("", "A-Main", "A-Main")]
    [InlineData(null, "A-Main", "A-Main")]
    [InlineData("Stock", "", "Stock")]
    public void A_race_is_named_with_its_class_first(string? classLabel, string roundLabel, string expected)
    {
        Assert.Equal(expected, RunSheet.RaceName(classLabel, roundLabel));
    }

    [Fact]
    public void A_race_already_run_is_recorded_whoever_is_here()
    {
        var status = RunSheet.StatusOf(recorded: true, drivers: 5, here: 0, countHere: true);

        Assert.Equal(RunSheet.Pill.Recorded, status.Pill);
        Assert.Equal(RunSheet.Mark.Tick, status.Mark);
    }

    [Fact]
    public void A_race_with_someone_not_checked_in_says_how_many_are_missing()
    {
        var status = RunSheet.StatusOf(recorded: false, drivers: 5, here: 3, countHere: true);

        Assert.Equal(RunSheet.Pill.Missing, status.Pill);
        Assert.Equal("2 missing", status.Words);
        Assert.Equal(RunSheet.Mark.Triangle, status.Mark);
    }

    [Fact]
    public void A_race_is_ready_when_everyone_is_here_or_nobody_is_checking_in()
    {
        Assert.Equal(RunSheet.Pill.Ready, RunSheet.StatusOf(false, 5, 5, countHere: true).Pill);

        // A club that doesn't check drivers in would otherwise see every
        // race as "5 missing".
        Assert.Equal(RunSheet.Pill.Ready, RunSheet.StatusOf(false, 5, 0, countHere: false).Pill);
    }

    [Fact]
    public void A_race_with_nobody_in_it_is_flagged()
    {
        var status = RunSheet.StatusOf(false, 0, 0, countHere: true);

        Assert.Equal(RunSheet.Pill.Empty, status.Pill);
        Assert.Equal(RunSheet.Mark.Triangle, status.Mark);
    }

    [Theory]
    [InlineData(false, 5, 4, true, "4 of 5 here")]
    [InlineData(true, 5, 4, true, "5 drivers")]
    [InlineData(false, 5, 0, false, "5 drivers")]
    [InlineData(false, 1, 0, false, "1 driver")]
    [InlineData(false, 5, 9, true, "5 of 5 here")]
    public void The_drivers_column_says_who_is_here_for_a_race_to_come(bool recorded, int drivers, int here, bool countHere, string expected)
    {
        Assert.Equal(expected, RunSheet.DriversWords(recorded, drivers, here, countHere));
    }

    [Fact]
    public void A_length_is_whole_minutes_and_never_nothing()
    {
        Assert.Equal("5 min", RunSheet.LengthWords(TimeSpan.FromSeconds(5 * 60 + 25)));
        Assert.Equal("1 min", RunSheet.LengthWords(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void The_end_of_racing_is_late_in_red_on_time_in_green_or_nothing_without_an_out_by()
    {
        var late = RunSheet.EndOf(TimeSpan.FromMinutes(210), hasOutBy: true);
        Assert.Equal(RunSheet.Pill.Late, late.Pill);
        Assert.Equal("+3 h 30", late.Words);
        Assert.Equal(RunSheet.Mark.Square, late.Mark);

        Assert.Equal("+25 min", RunSheet.EndOf(TimeSpan.FromMinutes(25), true).Words);
        Assert.Equal(RunSheet.Pill.OnTime, RunSheet.EndOf(null, hasOutBy: true).Pill);
        Assert.Equal(RunSheet.Pill.None, RunSheet.EndOf(null, hasOutBy: false).Pill);
    }

    [Theory]
    [InlineData("4:58 PM", "4:58")]
    [InlineData("10:12 am", "10:12")]
    [InlineData("16:58", "16:58")]
    [InlineData("", "")]
    public void A_short_clock_drops_am_and_pm(string clock, string expected)
    {
        Assert.Equal(expected, RunSheet.ShortClock(clock));
    }

    [Fact]
    public void A_night_on_time_offers_no_fixes()
    {
        Assert.Empty(RunSheet.ChoicesFor(null));
        Assert.Empty(RunSheet.ChoicesFor(Plan(over: null)));
    }

    [Fact]
    public void The_fixes_keep_the_plans_minutes_and_say_which_can_be_ticked()
    {
        var choices = RunSheet.ChoicesFor(Plan(over: TimeSpan.FromMinutes(242)));

        Assert.Equal(new[] { "Drop Q3 (4 races)", "Qualifiers 1 min shorter", "One main fewer in Stock", "Run Stock and Mod together (Run Together on the program)" },
            choices.Select(c => c.Label));
        Assert.Equal(new[] { true, true, true, false }, choices.Select(c => c.CanTick));
        Assert.Equal(3, choices[0].Round);
        Assert.Equal("−31 min", choices[0].SavesWords);
    }

    [Fact]
    public void The_ticked_fixes_add_up_without_the_advice()
    {
        var choices = RunSheet.ChoicesFor(Plan(over: TimeSpan.FromMinutes(242)));

        Assert.Equal(TimeSpan.FromMinutes(31 + 8 + 7), RunSheet.Saving(choices));
        Assert.Equal(TimeSpan.FromMinutes(31), RunSheet.Saving(choices.Take(1)));
    }

    private static NightPlan.Projection Plan(TimeSpan? over) =>
        new(
            Array.Empty<NightPlan.Slot>(),
            new DateTime(2026, 11, 11, 22, 0, 0, DateTimeKind.Utc),
            TimeSpan.FromMinutes(3),
            GapIsTonights: true,
            OutByUtc: new DateTime(2026, 11, 11, 17, 0, 0, DateTimeKind.Utc),
            Over: over,
            Fixes: new[]
            {
                new NightPlan.Fix("drop Q3 (4 races) to save 31 min", TimeSpan.FromMinutes(31), 3, NightPlan.FixKind.DropRound),
                new NightPlan.Fix("run qualifiers a minute shorter to save 8 min", TimeSpan.FromMinutes(8), Kind: NightPlan.FixKind.ShorterQualifiers),
                new NightPlan.Fix("run one main fewer in Stock to save 7 min", TimeSpan.FromMinutes(7), Kind: NightPlan.FixKind.OneMainFewer),
                new NightPlan.Fix("run Stock and Mod together (Run Together on the program) to save 6 min", TimeSpan.FromMinutes(6)),
            });
}
