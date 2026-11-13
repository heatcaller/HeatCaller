namespace HeatCaller.Tests;

/// <summary>
/// Fixes from the first night on step 8 (12 Nov 2026): a driver's "Need 5
/// minutes" approved before their race is next is kept and given when it is
/// (MoreTimeOwed, RaceControlControl.ApplyOwedMoreTime); the night plan runs
/// Race Control's next race first even out of program order
/// (NightPlan.InRunningOrder); and the Day Schedule's lines still to come
/// are expected later by the next race's push-back (ScheduleExpected).
/// </summary>
public sealed class MoreTimeAndRunningOrderTests
{
    private static readonly DateTime T0 = new(2026, 11, 12, 20, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Five = TimeSpan.FromMinutes(5);

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }

    private static RoundDefinition Heat(string label, params string[] transponders)
    {
        var round = new RoundDefinition { Kind = RoundKind.Qualifier, RoundLabel = label };
        round.AssignedTransponders.AddRange(transponders);
        return round;
    }

    // --- 1. More time kept for a race not next yet ---

    [Fact]
    public void Two_yeses_for_the_same_race_add_up_and_are_given_once()
    {
        var owed = new MoreTimeOwed();

        Assert.Equal(5, owed.Owe("q3h2", "111", "Ann", RaceControlControl.LateMinutes));
        Assert.Equal(10, owed.Owe("q3h2", "222", "Bob", RaceControlControl.LateMinutes));
        Assert.Equal(10, owed.MinutesFor("q3h2"));
        Assert.Equal(0, owed.MinutesFor("other"));

        var given = owed.Take("q3h2");
        Assert.Equal(10, given.Sum(a => a.Minutes));
        Assert.Equal(new[] { "Ann", "Bob" }, given.Select(a => a.Name));

        Assert.Empty(owed.Take("q3h2"));
        Assert.False(owed.Any);
        Assert.Empty(owed.Take(null));
    }

    [Fact]
    public void A_race_that_ran_or_was_deleted_drops_what_it_was_owed()
    {
        var owed = new MoreTimeOwed();
        var stillToRun = Heat("Q3 Heat 2", "111");
        owed.Owe(stillToRun.Id, "111", "Ann", 5);
        owed.Owe("ran", "111", "Ann", 5);
        owed.Owe("deleted", "111", "Ann", 5);

        owed.Keep(id => id == stillToRun.Id ? stillToRun : null);

        Assert.Equal(5, owed.MinutesFor(stillToRun.Id));
        Assert.Equal(0, owed.MinutesFor("ran"));
        Assert.Equal(0, owed.MinutesFor("deleted"));
    }

    [Fact]
    public void A_driver_who_leaves_their_race_takes_their_minutes_with_them()
    {
        var owed = new MoreTimeOwed();
        var race = Heat("Q3 Heat 2", "111", "222");
        owed.Owe(race.Id, "111", "Ann", 5);
        owed.Owe(race.Id, "222", "Bob", 5);

        race.AssignedTransponders.Remove("222");
        owed.Keep(id => id == race.Id ? race : null);
        Assert.Equal(5, owed.MinutesFor(race.Id));

        race.AssignedTransponders.Remove("111");
        owed.Keep(id => id == race.Id ? race : null);
        Assert.False(owed.Any);
    }

    [Fact]
    public void A_yes_before_the_race_is_next_is_kept_and_the_driver_told_so()
    {
        var source = Source("RaceControlControl.AutoStart.cs");

        Assert.Contains("_moreTimeOwed.Owe(theirs.Id, transponder, name, LateMinutes)", source);
        Assert.Contains("will go back {Plural.Of(owed, \"minute\")} for you when it's next.", source);
        Assert.DoesNotContain("Ask again when it's next.", source);
    }

    [Fact]
    public void The_gap_gives_what_is_owed_when_that_race_is_next_before_auto_start_reads_it()
    {
        var tick = Source("RaceControlControl.BetweenRaces.cs");
        var apply = Source("RaceControlControl.AutoStart.cs");

        var nextRace = tick.IndexOf("Gap.NextRace(facts.NextId);", StringComparison.Ordinal);
        var owed = tick.IndexOf("ApplyOwedMoreTime(now, facts.NextId)", StringComparison.Ordinal);
        var autoStart = tick.IndexOf("AutoStartTick(now, facts, busy);", StringComparison.Ordinal);
        Assert.True(nextRace >= 0 && nextRace < owed && owed < autoStart);

        // The same push-back, gap note and console line as a yes given while it was next.
        Assert.Contains("Gap.PushBack(now, minutes);", apply);
        Assert.Contains("NoteGap(next.Id, GapNoteKind.DriverAskedForTime, now, TimeSpan.FromMinutes(ask.Minutes));", apply);
        Assert.Contains("_moreTimeOwed.Keep(", apply);
    }

    // --- 2. The plan follows Race Control's running order ---

    private static (RoundDefinition Run, RoundDefinition H2, RoundDefinition H3, RoundDefinition H4) Night() => (
        new RoundDefinition { Kind = RoundKind.Qualifier, RoundLabel = "Q1 Heat 1", LinkedSessionId = "s1" },
        Heat("Q1 Heat 2"),
        Heat("Q1 Heat 3"),
        Heat("Q1 Heat 4"));

    [Fact]
    public void Race_control_s_next_race_goes_first_the_rest_in_program_order()
    {
        var (run, h2, h3, h4) = Night();

        var ordered = NightPlan.InRunningOrder(new[] { run, h2, h3, h4 }, new RaceControlControl.NextStartFact(h3.Id, null, 0));

        Assert.Equal(new[] { run, h3, h2, h4 }, ordered);
    }

    [Fact]
    public void The_order_is_left_alone_when_there_is_nothing_to_honour()
    {
        var (run, h2, h3, h4) = Night();
        var day = new[] { run, h2, h3, h4 };

        Assert.Same(day, NightPlan.InRunningOrder(day, null));
        Assert.Same(day, NightPlan.InRunningOrder(day, new RaceControlControl.NextStartFact(h2.Id, null, 5)));
        Assert.Same(day, NightPlan.InRunningOrder(day, new RaceControlControl.NextStartFact(run.Id, null, 5)));
        Assert.Same(day, NightPlan.InRunningOrder(day, new RaceControlControl.NextStartFact("gone", null, 5)));
    }

    [Fact]
    public void A_race_run_out_of_order_takes_the_push_back_and_the_program_s_next_follows_it()
    {
        var (run, h2, h3, h4) = Night();
        var day = new[] { run, h2, h3, h4 };
        var startsAt = T0.AddMinutes(9);
        var fact = new RaceControlControl.NextStartFact(h3.Id, startsAt, PushedBackMinutes: 5);

        var about = NightPlan.AboutOf(NightPlan.For(NightPlan.InRunningOrder(day, fact), _ => Five, null, null, T0.AddMinutes(-1), null, null, T0, nextStart: fact));

        Assert.Equal(NightPlan.About(startsAt), about(h3.Id));
        Assert.Equal(NightPlan.About(startsAt + Five + NightPlan.DefaultGap), about(h2.Id));
        Assert.Equal(NightPlan.About(startsAt + 2 * (Five + NightPlan.DefaultGap)), about(h4.Id));
    }

    [Fact]
    public void The_program_plans_in_running_order()
    {
        var source = Source("RaceProgramControl.cs");

        Assert.Contains("NightPlan.InRunningOrder(day, nextStart)", source);
    }

    // --- 3. The Day Schedule's lines still to come ---

    private static ScheduleEntry Line(string time, string roundId = "") => new() { ScheduledTime = time, RoundId = roundId };

    [Fact]
    public void The_next_race_s_push_back_moves_its_line_and_the_ones_after_it()
    {
        var done = Line("19:50", "h1");
        done.ActualStartUtc = T0;
        var before = Line("20:00", "h2");
        var next = Line("20:05", "h3");
        var after = Line("20:10", "h4");
        var sheet = new[] { done, before, next, after };
        var fact = new RaceControlControl.NextStartFact("h3", null, PushedBackMinutes: 5);

        Assert.Equal(2, ScheduleExpected.MinutesLate(sheet, before, 2, fact));
        Assert.Equal(7, ScheduleExpected.MinutesLate(sheet, next, 2, fact));
        Assert.Equal(7, ScheduleExpected.MinutesLate(sheet, after, 2, fact));

        // Nothing started yet, and nothing put back: nobody can say.
        Assert.Null(ScheduleExpected.MinutesLate(sheet, after, null, null));
        Assert.Equal(5, ScheduleExpected.MinutesLate(sheet, after, null, fact));

        // The next race with no line of its own: every line waits for it.
        Assert.Equal(5, ScheduleExpected.MinutesLate(sheet, before, 0, fact with { RoundId = "not-on-the-sheet" }));
    }

    [Fact]
    public void A_line_still_to_come_says_when_it_is_expected_without_touching_its_time()
    {
        var nowLocal = DateTime.SpecifyKind(DateTime.Today.AddHours(19).AddMinutes(50), DateTimeKind.Local);
        var nowUtc = nowLocal.ToUniversalTime();
        var line = Line("20:05", "h3");
        var sheet = new[] { line };
        var fact = new RaceControlControl.NextStartFact("h3", null, PushedBackMinutes: 5);

        var words = ScheduleExpected.Words(sheet, line, 2, fact, nowUtc);

        Assert.Equal($"expected {AppTime.TimeOfDay(DateTime.Today.AddHours(20).AddMinutes(12))} · 7 min late", words);
        Assert.Equal("20:05", line.ScheduledTime);

        // On time, a started line and a time that doesn't read say nothing.
        Assert.Equal(string.Empty, ScheduleExpected.Words(sheet, line, 1, null, nowUtc));
        Assert.Equal(string.Empty, ScheduleExpected.Words(new[] { Line("TBD", "h3") }, Line("TBD", "h3"), 9, fact, nowUtc));
        line.ActualStartUtc = nowUtc;
        Assert.Equal(string.Empty, ScheduleExpected.Words(sheet, line, 2, fact, nowUtc));
    }

    [Fact]
    public void The_day_schedule_reads_race_control_s_next_start_and_redraws_when_it_moves()
    {
        var page = Source("DayScheduleControl.cs");
        var main = Source("MainForm.Pages.cs");

        Assert.Contains("ScheduleExpected.Words(entries, entry, dayLate, next, now)", page);
        Assert.Contains("daySchedule.NextStart = () => _raceControlControl?.NextStartForPlan;", main);
        Assert.Contains("_raceControlControl.NextStartMoved += daySchedule.NextStartMoved;", main);
    }
}
