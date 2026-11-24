namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), Race Control: a closed race day's leftover
/// heats, lap windows over an abandoned race, Auto start's race just run,
/// ghost cars on the Next Up bar and in Redo Remaining Heats, introductions
/// written ahead after a name is corrected, and the black flag kept when a
/// race is put back.
/// </summary>
public sealed class BugHunt24_RaceControlControl_2Tests
{
    private static RoundDefinition Heat(string label, string className = "Stock", bool run = false, params string[] cars) => new()
    {
        RoundLabel = label,
        ClassName = className,
        Kind = RoundKind.Qualifier,
        LinkedSessionId = run ? Guid.NewGuid().ToString("N") : null,
        AssignedTransponders = cars.ToList(),
    };

    // --- F228: a closed race day picked to look at --------------------------

    private static RaceEvent Day(bool closed, params RoundDefinition[] rounds) => new()
    {
        Name = "Last week",
        FinishedUtc = closed ? new DateTime(2026, 11, 16, 23, 0, 0, DateTimeKind.Utc) : null,
        RoundIds = rounds.Select(r => r.Id).ToList(),
    };

    [Fact]
    public void BugHunt24_A_closed_day_offers_none_of_its_leftover_heats_as_next()
    {
        var aMain = Heat("A-Main", run: true);
        var cMain = Heat("C-Main");
        var other = Heat("Tonight's Q1 Heat 1");
        var all = new[] { cMain, aMain, other };

        var program = RaceControlControl.ProgramOfDay(Day(closed: true, aMain, cMain), all, keepRoundId: null);

        Assert.DoesNotContain(cMain, program);
        Assert.DoesNotContain(other, program);
        Assert.Contains(aMain, program);
        Assert.Null(program.FirstOrDefault(r => !r.IsRecorded));
        Assert.NotNull(RaceControlControl.ClosedDayNextText(Day(closed: true, aMain, cMain)));
    }

    [Fact]
    public void BugHunt24_The_leftover_heat_is_next_again_once_the_day_is_reopened()
    {
        var aMain = Heat("A-Main", run: true);
        var cMain = Heat("C-Main");

        var program = RaceControlControl.ProgramOfDay(Day(closed: false, aMain, cMain), new[] { cMain, aMain }, keepRoundId: null);

        Assert.Equal(cMain, program.First(r => !r.IsRecorded));
        Assert.Null(RaceControlControl.ClosedDayNextText(Day(closed: false, aMain, cMain)));
    }

    [Fact]
    public void BugHunt24_A_race_already_under_way_on_a_closed_day_keeps_its_round()
    {
        var cMain = Heat("C-Main");
        var bMain = Heat("B-Main");

        var program = RaceControlControl.ProgramOfDay(Day(closed: true, cMain, bMain), new[] { cMain, bMain }, keepRoundId: cMain.Id);

        Assert.Contains(cMain, program);
        Assert.DoesNotContain(bMain, program);
    }

    [Fact]
    public void BugHunt24_The_legacy_holder_is_still_the_whole_program()
    {
        var heat = Heat("Heat 1");
        var legacy = Day(closed: true, heat);
        legacy.IsLegacy = true;

        Assert.Contains(heat, RaceControlControl.ProgramOfDay(legacy, new[] { heat }, keepRoundId: null));
        Assert.Contains(heat, RaceControlControl.ProgramOfDay(null, new[] { heat }, keepRoundId: null));
        Assert.Null(RaceControlControl.ClosedDayNextText(legacy));
    }

    // --- F223: lap windows over an abandoned race --------------------------

    [Theory]
    [InlineData(RaceState.Racing, RaceState.Staging)]
    [InlineData(RaceState.Racing, RaceState.Countdown)]
    public void BugHunt24_Stage_or_Countdown_over_a_running_race_closes_the_lap_windows(RaceState previous, RaceState state)
    {
        Assert.True(RaceControlControl.LapWindowsCloseFor(previous, state, greenStartedTheRace: false, restoring: false));
        Assert.False(RaceControlControl.LapWindowsCloseFor(previous, state, greenStartedTheRace: false, restoring: true));
    }

    [Theory]
    [InlineData(RaceState.Idle, RaceState.Staging, false, true)]
    [InlineData(RaceState.Finished, RaceState.Countdown, false, true)]
    [InlineData(RaceState.Idle, RaceState.Racing, true, true)]
    [InlineData(RaceState.Idle, RaceState.Racing, false, false)]
    [InlineData(RaceState.Staging, RaceState.Countdown, false, false)]
    [InlineData(RaceState.Countdown, RaceState.Racing, true, false)]
    [InlineData(RaceState.Racing, RaceState.Finished, false, false)]
    [InlineData(RaceState.Racing, RaceState.Idle, false, false)]
    [InlineData(RaceState.Finished, RaceState.Idle, false, false)]
    public void BugHunt24_Moving_on_within_a_race_still_leaves_the_lap_windows_open(RaceState previous, RaceState state, bool green, bool closes)
    {
        Assert.Equal(closes, RaceControlControl.LapWindowsCloseFor(previous, state, green, restoring: false));
    }

    // --- F219: Auto start's race just run -----------------------------------

    [Fact]
    public void BugHunt24_Auto_start_takes_the_race_just_run_from_the_filing_not_the_emptied_Round_box()
    {
        var q2 = Heat("Q2 Heat 4", run: true, cars: new[] { "101", "102" });
        var q3 = Heat("Q3 Heat 1", cars: new[] { "102", "103" });
        RoundDefinition? Lookup(string id) => id == q2.Id ? q2 : null;

        // The Round box is back to freeform once the race is filed.
        var last = RaceControlControl.AutoStartLastRound((q2.Id, 7), 7, boxRound: null, Lookup);

        Assert.Same(q2, last);

        // So the planned break after Q2 is counted, as it never was.
        var breaks = new[] { new PlannedBreak { AfterRound = 2, Minutes = 10, What = "water the track" } };
        Assert.Single(NightBreaks.Between(last, q3, breaks));
        Assert.Empty(NightBreaks.Between(null, q3, breaks));
    }

    [Fact]
    public void BugHunt24_Auto_start_falls_back_to_the_Round_box_for_another_race()
    {
        var q2 = Heat("Q2 Heat 4", run: true);
        var box = Heat("Q2 Heat 3");
        RoundDefinition? Lookup(string id) => id == q2.Id ? q2 : null;

        // Filed for an earlier race (another generation), or for no round.
        Assert.Same(box, RaceControlControl.AutoStartLastRound((q2.Id, 6), 7, box, Lookup));
        Assert.Same(box, RaceControlControl.AutoStartLastRound((null, 7), 7, box, Lookup));
        Assert.Same(box, RaceControlControl.AutoStartLastRound(null, 7, box, Lookup));
        Assert.Null(RaceControlControl.AutoStartLastRound(("gone", 7), 7, null, Lookup));
    }

    // --- F229: ghost cars on the Next Up bar and in Redo --------------------

    private const string Ghost = Ghosts.Prefix + "1";

    [Fact]
    public void BugHunt24_A_ghost_on_the_next_heat_is_not_counted_as_not_checked_in()
    {
        var here = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "101" };

        Assert.Equal(0, RaceControlControl.NotCheckedInCount(new[] { "101", Ghost }, here.Contains));
        Assert.Equal(1, RaceControlControl.NotCheckedInCount(new[] { "101", "102", Ghost }, here.Contains));
    }

    [Fact]
    public void BugHunt24_Redo_Remaining_Heats_keeps_a_ghost_that_was_on_a_heat()
    {
        var heat1 = Heat("Q2 Heat 1", cars: new[] { "S1", Ghost });
        var heat2 = Heat("Q2 Heat 2", cars: new[] { "S2" });
        var here = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "S1", "S2" };

        RaceControlControl.RedoRosters Redo(Func<string, bool> isCheckedIn) => RaceControlControl.RedealAcrossClasses(
            new[] { heat1, heat2 },
            _ => new List<string> { "S1", "S2" },
            isCheckedIn,
            (round, transponder) => MixedHeats.ClassOfDriver(round, transponder));

        // The old rule, a plain check-in lookup, dropped it.
        Assert.DoesNotContain(Ghost, Redo(here.Contains).Rosters.Values.SelectMany(r => r));

        var redo = Redo(RaceControlControl.PresentForRedo(here.Contains));
        var dealt = redo.Rosters.Values.SelectMany(r => r).ToList();

        Assert.Contains(Ghost, dealt);
        Assert.Contains("S1", dealt);
        Assert.Contains("S2", dealt);
    }

    [Fact]
    public void BugHunt24_A_class_with_only_a_ghost_left_is_still_nobody_checked_in()
    {
        var heat = Heat("Q2 Heat 1", cars: new[] { "S1", Ghost });

        var redo = RaceControlControl.RedealAcrossClasses(
            new[] { heat },
            _ => new List<string>(),
            RaceControlControl.PresentForRedo(_ => false),
            (round, transponder) => MixedHeats.ClassOfDriver(round, transponder));

        Assert.Equal(new[] { "Stock" }, redo.NobodyIn);
        Assert.False(redo.Rosters.ContainsKey(heat.Id));
    }

    // --- F222: introductions written ahead after a correction ---------------

    private static IntroDriver Driver(string transponder, int slot, string spoken, string screen, string number = "7", string home = "Riverside", string stat = "", string career = "") =>
        new(transponder, slot, spoken, screen, number, home, stat, career);

    [Fact]
    public void BugHunt24_Held_introductions_are_not_used_once_a_name_or_spelling_is_corrected()
    {
        var before = new[] { Driver("101", 1, "Jon", "Jon Smith"), Driver("102", 2, "Kai", "Kai Lee") };
        var held = RaceControlControl.IntroductionsFingerprint(before);

        Assert.NotEqual(held, RaceControlControl.IntroductionsFingerprint(new[] { Driver("101", 1, "John", "Jon Smith"), before[1] }));
        Assert.NotEqual(held, RaceControlControl.IntroductionsFingerprint(new[] { Driver("101", 1, "Jon", "John Smith"), before[1] }));
        Assert.NotEqual(held, RaceControlControl.IntroductionsFingerprint(new[] { Driver("101", 1, "Jon", "Jon Smith", number: "17"), before[1] }));
        Assert.NotEqual(held, RaceControlControl.IntroductionsFingerprint(new[] { Driver("101", 1, "Jon", "Jon Smith", home: "Hilltop"), before[1] }));
        Assert.NotEqual(held, RaceControlControl.IntroductionsFingerprint(new[] { before[1], before[0] }));
    }

    [Fact]
    public void BugHunt24_Held_introductions_are_still_used_when_nothing_they_say_has_changed()
    {
        var before = new[] { Driver("101", 1, "Jon", "Jon Smith", stat: "won last week"), Driver("102", 2, "Kai", "Kai Lee") };
        var again = new[] { Driver("101", 1, "Jon", "Jon Smith", stat: "something else"), Driver("102", 2, "Kai", "Kai Lee", career: "ten nights here") };

        Assert.Equal(RaceControlControl.IntroductionsFingerprint(before), RaceControlControl.IntroductionsFingerprint(again));
    }

    // --- F232: the black flag kept when a race is put back ------------------

    [Fact]
    public void BugHunt24_A_race_put_back_keeps_who_was_black_flagged()
    {
        var flagged = new RacerStanding { TransponderId = "101", Laps = 12 };
        flagged.Warnings.Add(new RaceWarning { Action = WarningAction.BlackFlag, Reason = "rough driving" });
        var clean = new RacerStanding { TransponderId = "102", Laps = 11 };

        var seededFlagged = RaceControlControl.SeededStandingFor(flagged, "Sam");
        var seededClean = RaceControlControl.SeededStandingFor(clean, "Bill");

        Assert.True(seededFlagged.BlackFlagged);
        Assert.False(seededClean.BlackFlagged);
        Assert.Equal("Sam", seededFlagged.Name);
        Assert.Equal(12, seededFlagged.Laps);
        Assert.Equal("active", seededFlagged.Status);
    }
}
