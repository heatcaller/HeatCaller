using System.Globalization;

namespace HeatCaller.Tests;

/// <summary>
/// Driver experience P0 (22 Oct 2026): PenaltyWords - why a result carries
/// a penalty, in the driver's own night and on the Wi-Fi follow view.
/// </summary>
public sealed class DriverExp_P0_PenaltyWordsTests
{
    private static SessionResultEntry Row(string transponder = "T1") =>
        new() { Transponder = transponder, DriverName = "Sam Parker" };

    private static RaceWarning Warn(int number, WarningAction action, string reason, long penaltyMs = 0) =>
        new() { Number = number, Action = action, Reason = reason, PenaltyMs = penaltyMs };

    [Fact]
    public void A_clean_result_says_nothing()
    {
        Assert.Empty(PenaltyWords.For(Row(), new SessionRecord()));
    }

    [Fact]
    public void Each_warning_action_has_its_own_words()
    {
        var row = Row();
        row.Warnings.Add(Warn(1, WarningAction.Warning, "Blocking"));
        row.Warnings.Add(Warn(2, WarningAction.TimePenalty, "Blocking", 5000));
        row.Warnings.Add(Warn(3, WarningAction.BlackFlag, "Rough driving"));
        row.RacePenaltyMs = 5000;

        Assert.Equal(
            new[] { "Warning 1 - Blocking", "+5 s - Blocking (warning 2)", "Black flag - Rough driving (warning 3)" },
            PenaltyWords.For(row, new SessionRecord()));
    }

    [Fact]
    public void A_jump_start_with_time_says_so()
    {
        var row = Row();
        row.JumpStarted = true;
        row.RacePenaltyMs = 5000;

        Assert.Equal(new[] { "+5 s - jump start" }, PenaltyWords.For(row, new SessionRecord()));
    }

    [Fact]
    public void A_jump_start_with_no_time_added_still_says_so()
    {
        var row = Row();
        row.JumpStarted = true;

        Assert.Equal(new[] { "Jump start (no time added)" }, PenaltyWords.For(row, new SessionRecord()));
    }

    [Fact]
    public void A_jump_start_given_as_a_warning_is_not_said_twice()
    {
        var row = Row();
        row.JumpStarted = true;
        row.Warnings.Add(Warn(1, WarningAction.Warning, "Jump start"));

        Assert.Equal(new[] { "Warning 1 - Jump start" }, PenaltyWords.For(row, new SessionRecord()));
    }

    [Fact]
    public void Time_no_warning_explains_is_from_race_control()
    {
        var row = Row();
        row.Warnings.Add(Warn(1, WarningAction.TimePenalty, "Track cutting", 5000));
        row.RacePenaltyMs = 15000;

        Assert.Equal(
            new[] { "+5 s - Track cutting (warning 1)", "+10 s - from race control" },
            PenaltyWords.For(row, new SessionRecord()));
    }

    [Fact]
    public void Post_race_edits_for_this_driver_only_are_said_with_their_reason()
    {
        var session = new SessionRecord();
        session.Edits.Add(new ResultEdit { Transponder = "t1", Change = "time penalty 0s -> 5s", Reason = "Cut the chicane", AtUtc = new DateTime(2026, 10, 22, 19, 0, 0, DateTimeKind.Utc) });
        session.Edits.Add(new ResultEdit { Transponder = "T2", Change = "lap penalty 0 -> 1", Reason = "Someone else" });
        session.Edits.Add(new ResultEdit { Transponder = "T1", Change = "finished -> DQ", Reason = "  ", AtUtc = new DateTime(2026, 10, 22, 19, 5, 0, DateTimeKind.Utc) });

        var row = Row();
        row.TimePenaltyMs = 5000;
        row.Outcome = ResultEditor.Dq;

        Assert.Equal(
            new[] { "time penalty 0s -> 5s - Cut the chicane", "finished -> DQ - no reason given" },
            PenaltyWords.For(row, session));
    }

    [Fact]
    public void Without_an_edit_record_the_totals_are_said()
    {
        var row = Row();
        row.TimePenaltyMs = 2500;
        row.LapPenalty = 2;
        row.Outcome = ResultEditor.Dq;

        Assert.Equal(
            new[] { "+2.5 s added after the race", "-2 laps after the race", "Disqualified" },
            PenaltyWords.For(row, new SessionRecord()));
    }

    [Fact]
    public void One_lap_and_a_DNF_read_right()
    {
        var row = Row();
        row.LapPenalty = 1;
        row.Outcome = ResultEditor.Dnf;

        Assert.Equal(new[] { "-1 lap after the race", "Did not finish" }, PenaltyWords.For(row, new SessionRecord()));
    }

    [Fact]
    public void An_empty_warning_reason_reads_no_reason_given()
    {
        var row = Row();
        row.Warnings.Add(Warn(1, WarningAction.Warning, ""));

        Assert.Equal(new[] { "Warning 1 - no reason given" }, PenaltyWords.For(row, new SessionRecord()));
    }

    [Fact]
    public void A_long_reason_is_cut_to_eighty_characters_on_one_line()
    {
        var row = Row();
        row.Warnings.Add(Warn(1, WarningAction.Warning, "Line one\r\nthen " + new string('x', 200)));

        var line = Assert.Single(PenaltyWords.For(row, new SessionRecord()));
        var reason = line["Warning 1 - ".Length..];

        Assert.Equal(PenaltyWords.ReasonMaxChars, reason.Length);
        Assert.StartsWith("Line one then x", reason);
        Assert.EndsWith("...", reason);
    }

    [Fact]
    public void Seconds_are_the_same_in_every_culture()
    {
        var was = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var row = Row();
            row.TimePenaltyMs = 2500;

            Assert.Equal(new[] { "+2.5 s added after the race" }, PenaltyWords.For(row, new SessionRecord()));
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }

    [Fact]
    public void Filing_marks_only_the_cars_race_control_saw_jump()
    {
        var racers = new[]
        {
            new RacerStanding { TransponderId = "A", Laps = 3 },
            new RacerStanding { TransponderId = "B", Laps = 3 },
        };

        var rows = RaceFiling.ResultsFrom(racers, ProfileStore.Load(), id => id == "B");

        Assert.False(rows.Single(r => r.Transponder == "A").JumpStarted);
        Assert.True(rows.Single(r => r.Transponder == "B").JumpStarted);

        // And every other caller, which passes nothing, marks nobody.
        Assert.All(RaceFiling.ResultsFrom(racers, ProfileStore.Load()), r => Assert.False(r.JumpStarted));
    }
}
