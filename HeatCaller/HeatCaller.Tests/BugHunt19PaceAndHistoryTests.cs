namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 19, #24, #45 and #48.
///
/// #24: the Pace tab's result clause printed the raw position, which on an
/// older saved race still counts its ghosts - "P3 of 2" - and called nobody
/// the winner when a ghost took P1. It now places drivers among the real
/// cars only.
///
/// #45: in a shared (mixed-class) heat the clause placed every driver in the
/// whole heat and measured them against another class's winner, so a class
/// winner was told they lost. It now places them in their own class.
///
/// #48: Race History grouped sessions by calendar date, so a night whose
/// mains ran past midnight was split into two days. It now uses the race
/// night, 6am to 6am, like the rest of the app.
/// </summary>
public sealed class BugHunt19PaceAndHistoryTests
{
    private static SessionResultEntry Row(int position, string transponder, string name, int laps, long elapsedMs, string? className = null) => new()
    {
        Position = position,
        Transponder = transponder,
        DriverName = name,
        Laps = laps,
        ElapsedMs = elapsedMs,
        ClassName = className,
    };

    private static SessionRecord Race(params SessionResultEntry[] rows) => new()
    {
        SessionType = RaceSessionType.Race,
        Label = "A-Main",
        StartedAtUtc = new DateTime(2026, 11, 7, 20, 0, 0, DateTimeKind.Utc),
        Results = rows.ToList(),
    };

    [Fact]
    public void A_ghost_ahead_is_not_counted_in_the_place_or_as_the_winner()
    {
        var ghost = Row(1, Ghosts.Prefix + "1", "Ghost", 21, 290_000);
        var x = Row(2, "1001", "Sam Okafor", 20, 300_000);
        var y = Row(3, "1002", "Leo Martins", 20, 302_500);
        var race = Race(ghost, x, y);

        Assert.Equal("Won it, 2.5 s clear of Leo Martins.", PaceAnalysisControl.ResultClause(race, x));
        Assert.Equal("P2 of 2, 2.5 s behind Sam Okafor.", PaceAnalysisControl.ResultClause(race, y));
    }

    [Fact]
    public void A_shared_heat_places_a_driver_in_their_own_class()
    {
        var modA = Row(1, "2001", "Mod A", 22, 300_000, "Mod");
        var stockC = Row(2, "3001", "Stock C", 21, 301_000, "Stock");
        var modB = Row(3, "2002", "Mod B", 21, 303_000, "Mod");
        var stockD = Row(4, "3002", "Stock D", 20, 299_000, "Stock");
        var race = Race(modA, stockC, modB, stockD);

        Assert.Equal("Won Stock by 1 lap.", PaceAnalysisControl.ResultClause(race, stockC));
        Assert.Equal("Won Mod by 1 lap.", PaceAnalysisControl.ResultClause(race, modA));
        Assert.Equal("P2 of 2 in Mod, 1 lap down on Mod A.", PaceAnalysisControl.ResultClause(race, modB));
        Assert.Equal("P2 of 2 in Stock, 1 lap down on Stock C.", PaceAnalysisControl.ResultClause(race, stockD));
    }

    [Fact]
    public void A_one_class_race_reads_as_before()
    {
        var a = Row(1, "1001", "Sam Okafor", 20, 300_000, "Stock");
        var b = Row(2, "1002", "Leo Martins", 19, 301_000, "Stock");
        var race = Race(a, b);

        Assert.Equal("Won it by 1 lap.", PaceAnalysisControl.ResultClause(race, a));
        Assert.Equal("P2 of 2, 1 lap down on Sam Okafor.", PaceAnalysisControl.ResultClause(race, b));
    }

    [Fact]
    public void Race_history_keeps_mains_after_midnight_on_the_same_night()
    {
        SessionRecord At(DateTime local) => new() { StartedAtUtc = local.ToUniversalTime() };

        var heat = At(new DateTime(2026, 11, 13, 23, 50, 0, DateTimeKind.Local));
        var main = At(new DateTime(2026, 11, 14, 0, 5, 0, DateTimeKind.Local));
        var nextMorning = At(new DateTime(2026, 11, 14, 9, 0, 0, DateTimeKind.Local));

        Assert.Equal(new DateTime(2026, 11, 13), RaceHistoryControl.NightOf(heat));
        Assert.Equal(new DateTime(2026, 11, 13), RaceHistoryControl.NightOf(main));
        Assert.Equal(new DateTime(2026, 11, 14), RaceHistoryControl.NightOf(nextMorning));
    }

    [Fact]
    public void Race_history_lists_a_session_with_no_start_time_under_its_own_day()
    {
        Assert.Equal(DateTime.MinValue.Date, RaceHistoryControl.NightOf(new SessionRecord()));
    }
}
