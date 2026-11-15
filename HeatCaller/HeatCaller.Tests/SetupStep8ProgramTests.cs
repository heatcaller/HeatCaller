namespace HeatCaller.Tests;

/// <summary>
/// 12 Nov 2026, setup plan step 8 (program and results): a protest window
/// after every round and main, finals cuts when stopping early, the desk's
/// skip scored DNS, the club's marshal rule, number-order seeding and a
/// rest gap of N races with warnings.
/// </summary>
public sealed class SetupStep8ProgramTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    private readonly RoundDefinitionStore _rounds = RoundDefinitionStore.Load();
    private readonly ProfileStore _profiles = ProfileStore.Load();
    private readonly List<SessionRecord> _sessions = new();

    public void Dispose() => _data.Dispose();

    private static readonly DateTime T0 = new(2026, 11, 12, 20, 0, 0, DateTimeKind.Utc);

    private static SessionRecord Ran(string id, string roundId, DateTime ended) =>
        new() { Id = id, RoundId = roundId, SessionType = RaceSessionType.Race, StartedAtUtc = ended.AddMinutes(-5), EndedAtUtc = ended };

    private static RoundDefinition Round(string id, string label, RoundKind kind = RoundKind.Qualifier, string? session = null) =>
        new() { Id = id, ClassName = "Stock", RoundLabel = label, Kind = kind, LinkedSessionId = session };

    // ------------------------------------------------------------ 1. protest window

    [Fact]
    public void A_qualifying_round_opens_its_window_once_all_its_heats_are_in()
    {
        var sessions = new Dictionary<string, SessionRecord>
        {
            ["s1"] = Ran("s1", "q1h1", T0.AddMinutes(-10)),
            ["s2"] = Ran("s2", "q1h2", T0),
        };
        var rounds = new List<RoundDefinition>
        {
            Round("q1h1", "Q1 Heat 1", session: "s1"),
            Round("q1h2", "Q1 Heat 2"),
            Round("q2h1", "Q2 Heat 1"),
            Round("q2h2", "Q2 Heat 2"),
        };

        // Heat 2 of Q1 still to run: no window yet.
        Assert.Null(ProtestWindow.ForRound(rounds[0], rounds, sessions.GetValueOrDefault, 5));

        // Filed a moment ago, not linked yet: Q1's window, from its last heat.
        var window = ProtestWindow.ForRound(rounds[1], rounds, sessions.GetValueOrDefault, 5, sessions["s2"]);
        Assert.NotNull(window);
        Assert.Equal("Q1", window!.What);
        Assert.Equal(T0.AddMinutes(5), window.ClosesAtUtc);
        Assert.False(window.LastQualifying);

        // No window at all with it off.
        Assert.Null(ProtestWindow.ForRound(rounds[1], rounds, sessions.GetValueOrDefault, 0, sessions["s2"]));
    }

    [Fact]
    public void A_main_opens_its_own_window_and_the_latest_window_is_the_newest_result()
    {
        var sessions = new Dictionary<string, SessionRecord>
        {
            ["s1"] = Ran("s1", "q1", T0.AddMinutes(-30)),
            ["sb"] = Ran("sb", "b", T0.AddMinutes(-10)),
            ["sa"] = Ran("sa", "a", T0),
        };
        var rounds = new List<RoundDefinition>
        {
            Round("q1", "Heat 1", session: "s1"),
            new() { Id = "b", ClassName = "Stock", RoundLabel = "B-Main", Kind = RoundKind.Main, MainTier = 1, LinkedSessionId = "sb" },
            new() { Id = "a", ClassName = "Stock", RoundLabel = "A-Main", Kind = RoundKind.Main, MainTier = 0, LinkedSessionId = "sa" },
        };

        var main = ProtestWindow.ForRound(rounds[2], rounds, sessions.GetValueOrDefault, 5)!;
        Assert.Equal(RoundKind.Main, main.Kind);
        Assert.Equal("A-Main", main.What);

        Assert.Equal(T0.AddMinutes(5), ProtestWindow.Latest(rounds, sessions.GetValueOrDefault, 5)!.ClosesAtUtc);

        // The qualifying card reads only the qualifying windows: one round, so it is the last.
        var qualifying = ProtestWindow.Latest(rounds, sessions.GetValueOrDefault, 5, RoundKind.Qualifier)!;
        Assert.Equal(T0.AddMinutes(-25), qualifying.ClosesAtUtc);
        Assert.True(qualifying.LastQualifying);
    }

    [Fact]
    public void The_announcer_says_which_round_and_when_qualifying_is_done()
    {
        var q1 = new ProtestWindow.RoundWindow("Q1", T0, RoundKind.Qualifier, false, "QQ1");
        var q3 = new ProtestWindow.RoundWindow("Q3", T0, RoundKind.Qualifier, true, "QQ3");
        var a = new ProtestWindow.RoundWindow("A-Main", T0, RoundKind.Main, false, "#a");
        var single = new ProtestWindow.RoundWindow("qualifying", T0, RoundKind.Qualifier, true, "Q");

        Assert.Equal("That's Q1 done for Stock. Results are provisional for five minutes - any queries to the race desk, please.",
            ProtestWindow.Announcement("Stock", 5, q1));
        Assert.Equal("That's Q3 done for Stock, and that's qualifying. Results are provisional for five minutes - any queries to the race desk, please.",
            ProtestWindow.Announcement("Stock", 5, q3));
        Assert.StartsWith("That's the A-Main done for Stock.", ProtestWindow.Announcement("Stock", 5, a));
        Assert.Equal(ProtestWindow.Announcement("Stock", 5), ProtestWindow.Announcement("Stock", 5, single));
        Assert.StartsWith("PROVISIONAL UNTIL ", ProtestWindow.BoardWords(T0));
    }

    [Fact]
    public void A_query_inside_a_window_goes_to_the_top_of_driver_requests()
    {
        Assert.True(ProtestWindow.AskedInside(T0.AddMinutes(1), T0, T0.AddMinutes(5)));
        Assert.False(ProtestWindow.AskedInside(T0.AddMinutes(6), T0, T0.AddMinutes(5)));
        Assert.False(ProtestWindow.AskedInside(T0.AddMinutes(1), T0, null));

        var order = ProtestWindow.DeskOrder(new[] { "pack", "query-late", "query-in", "details" }, r => r == "query-in");
        Assert.Equal(new[] { "query-in", "pack", "query-late", "details" }, order);
    }

    [Fact]
    public void The_window_is_wired_to_every_round_the_big_screen_and_driver_requests()
    {
        var main = Source("MainForm.ProtestWindow.cs");
        Assert.Contains("_raceProgram.RoundProtestWindow(round, className, record)", main);
        Assert.Contains("ProtestWindow.Announcement(className, minutes, window)", main);
        Assert.Contains("ProtestWindow.BoardWords(closes)", main);

        Assert.Contains("provisional: () => ProvisionalBoard?.Invoke()", Source("RaceControlControl.Layout.cs"));
        Assert.Contains("RaceState.Finished => FinishedWords()", Source("BigScreenForm.cs"));
        Assert.Contains("ProtestWindow.DeskOrder(desk.Store.All.Where(r => r.IsWaiting), desk.InProtestWindow)", Source("DeskInboxForm.Requests.cs"));
        Assert.Contains("(request.Kind == DriverAsk.Query && desk.InProtestWindow(request))", Source("DeskInboxForm.Requests.cs"));
        Assert.Contains("notes.Add(InProtestWindowWords);", Source("DriverRequests.cs"));
        Assert.Contains("QueryInWindow = QueryInProtestWindow,", Source("MainForm.DriverRequests.cs"));

        // Seeding still waits only for the last round's window.
        Assert.Contains("ProtestWindow.FinalAtUtc(DayRoundsForClass(className)", Source("RaceProgramControl.cs"));
    }

    // ------------------------------------------------------------ 2. stop early

    private static NightPlan.Projection PlanWithMains(DateTime? outBy, params (string Class, int Minutes)[] mains)
    {
        var slots = mains
            .Select((m, i) => new NightPlan.Slot(null, "Main", m.Class, RoundKind.Main, TimeSpan.FromMinutes(m.Minutes), T0.AddMinutes(i * 10)))
            .ToList();

        return new NightPlan.Projection(slots, slots[^1].EndUtc, TimeSpan.FromMinutes(3), false, outBy, null, Array.Empty<NightPlan.Fix>());
    }

    [Fact]
    public void Stopping_early_offers_shorter_finals_and_one_main_fewer_with_what_each_saves()
    {
        var plan = PlanWithMains(T0, ("Stock", 6), ("Stock", 6), ("Mod", 4));

        var cuts = NightPlan.FinalsCuts(plan)!;

        // 2 minutes off each main, never below 3 minutes: 2 off each 6-minute
        // Stock main, 1 off the 4-minute Mod main (bug hunt 19).
        Assert.Equal(2, cuts.MinutesShorter);
        Assert.Equal(TimeSpan.FromMinutes(5), cuts.ShorterSaves);

        // Only Stock has two mains: one fewer saves a main and its gap.
        Assert.Equal(TimeSpan.FromMinutes(9), cuts.FewerSaves);
        Assert.Equal(new[] { "Stock" }, cuts.FewerClasses);

        // No out-by time: nothing offered.
        Assert.Null(NightPlan.FinalsCuts(PlanWithMains(null, ("Stock", 6))));
    }

    [Fact]
    public void Stopping_early_leaves_out_mains_already_on_the_program()
    {
        // Stock's finals are already seeded (built, not run); Mod's are still to come.
        var plan = PlanWithMains(T0, ("Stock", 6), ("Stock", 6), ("Mod", 6), ("Mod", 6));
        var built = plan.Slots
            .Select(s => s.ClassName == "Stock"
                ? s with { Round = new RoundDefinition { ClassName = "Stock", RoundLabel = "A-Main", Kind = RoundKind.Main } }
                : s)
            .ToList();
        plan = plan with { Slots = built };

        var cuts = NightPlan.FinalsCuts(plan)!;

        // The cut only reaches mains not built yet, so only Mod's count.
        Assert.Equal(TimeSpan.FromMinutes(4), cuts.ShorterSaves);
        Assert.Equal(TimeSpan.FromMinutes(9), cuts.FewerSaves);
        Assert.Equal(new[] { "Mod" }, cuts.FewerClasses);

        // Every class's finals already built: nothing left to cut.
        Assert.Null(NightPlan.FinalsCuts(plan with { Slots = built.Where(s => s.ClassName == "Stock").ToList() }));
    }

    [Fact]
    public void The_cut_is_applied_when_the_finals_are_built()
    {
        var format = new RaceFormat { TimeLimitMinutes = 6 };

        Assert.Equal(4, NightPlan.Shortened(format, 2)!.TimeLimitMinutes);
        Assert.Equal(3, NightPlan.Shortened(new RaceFormat { TimeLimitMinutes = 4 }, 2)!.TimeLimitMinutes);
        Assert.Null(NightPlan.Shortened(new RaceFormat { TimeLimitMinutes = 3 }, 2));
        Assert.Null(NightPlan.Shortened(format, 0));
        Assert.Equal(6, format.TimeLimitMinutes); // a copy, the class's own untouched

        var seeded = MainsBuilder.PlanSeeding(new[] { "a", "b", "c", "d", "e" }, 2);
        var fewer = NightPlan.OneMainFewer(seeded);
        Assert.Equal(2, fewer.TierCount);
        Assert.Equal(new[] { "a", "b", "c", "d" }, fewer.Tiers.SelectMany(t => t).ToArray());
        Assert.Equal(1, NightPlan.OneMainFewer(MainsBuilder.PlanSeeding(new[] { "a" }, 2)).TierCount);

        var source = Source("RaceProgramControl.cs");
        Assert.Contains("var plan = FinalsPlanTonight(MainsBuilder.PlanSeeding(ranked, mainSize));", source);
        Assert.Contains("round.FormatOverride = shortened.Clone();", source);
        Assert.Contains("NightPlan.FinalsCuts(Plan(), ActiveDay?.FinalsMinutesShorter ?? 0, ActiveDay?.FinalsOneMainFewer ?? false)", source);
        Assert.Contains("alsoUndo: PutCutBack", source);
    }

    [Fact]
    public void The_finals_cut_lives_on_the_race_day_and_is_off_by_default()
    {
        var day = new RaceEvent();

        Assert.Equal(0, day.FinalsMinutesShorter);
        Assert.False(day.FinalsOneMainFewer);
        Assert.Equal(1, day.RacesOffBetween);
    }

    // ------------------------------------------------------------ 3. skip, DNS

    [Fact]
    public void A_skip_takes_the_driver_off_the_grid_scores_dns_and_undoes()
    {
        var race = Round("q2", "Q2 Heat 1");
        race.AssignedTransponders = new List<string> { "a", "b", "c" };

        var undo = SkipDns.Skip(race, "B")!;

        Assert.Equal(new[] { "a", "c" }, race.AssignedTransponders);
        Assert.True(SkipDns.IsSkipped(race, "b"));
        Assert.Null(SkipDns.Skip(race, "zzz"));

        // Filed: on the result as DNS, with no result row.
        var result = new SessionRecord { Results = new List<SessionResultEntry> { new() { Transponder = "a" }, new() { Transponder = "c" } } };
        SkipDns.StampOnResult(result, race);
        Assert.Equal(new[] { "B" }, result.DidNotStart);
        Assert.Equal("DNS (did not start): Bea", SkipDns.Words(result, t => t.Equals("b", StringComparison.OrdinalIgnoreCase) ? "Bea" : t));

        // Undone after the race: the DNS comes off the result too.
        race.LinkedSessionId = "run";
        SkipDns.PutBack(race, "B", undo, result);
        Assert.Null(race.SkippedDns);
        Assert.Null(result.DidNotStart);
    }

    [Fact]
    public void A_skipped_driver_who_raced_after_all_is_not_dns_and_an_unrun_undo_restores_the_grid()
    {
        var race = Round("q2", "Q2 Heat 1");
        race.AssignedTransponders = new List<string> { "a", "b" };
        var undo = SkipDns.Skip(race, "b")!;

        var result = new SessionRecord { Results = new List<SessionResultEntry> { new() { Transponder = "a" }, new() { Transponder = "b" } } };
        SkipDns.StampOnResult(result, race);
        Assert.Null(result.DidNotStart);

        SkipDns.PutBack(race, "b", undo);
        Assert.Equal(new[] { "a", "b" }, race.AssignedTransponders);
    }

    [Fact]
    public void The_desk_and_the_phone_both_skip_as_dns()
    {
        var autoStart = Source("RaceControlControl.AutoStart.cs");
        Assert.Contains("SkipDriverDns(transponder, name, \"car broken (asked from their phone)\")", autoStart);

        var skip = Source("RaceControlControl.SkipDns.cs");
        Assert.Contains("item.Text = $\"Skip {name} in {next!.RoundLabel} (DNS)\";", skip);
        Assert.Contains("_nextUpBar.ContextMenuStrip = menu;", skip);
        Assert.Contains("FillSkipDnsItem(skipDns, transponder, name);", Source("RaceControlControl.Leaderboard.cs"));
        Assert.Contains("SkipDns.StampOnResult(record, round);", Source("RaceFilingService.cs"));
    }

    // ------------------------------------------------------------ 4. marshal rule

    private RoundDefinition RunHeat(string label, DateTime started, params (string Transponder, int Laps, long Ms)[] results)
    {
        var round = new RoundDefinition { ClassName = "Stock", RoundLabel = label, Kind = RoundKind.Qualifier };
        _rounds.Add(round);

        var session = new SessionRecord
        {
            Label = label,
            SessionType = RaceSessionType.Race,
            RoundId = round.Id,
            StartedAtUtc = started,
            EndedAtUtc = started.AddMinutes(5),
            Results = results.Select(r => new SessionResultEntry { Transponder = r.Transponder, DriverName = r.Transponder, Laps = r.Laps, ElapsedMs = r.Ms }).ToList(),
        };

        _sessions.Add(session);
        Assert.True(_rounds.MarkRecorded(round.Id, session.Id));
        return round;
    }

    private void LogNoShow(RoundDefinition round, string transponder, MarshalNoShowRule rule, int seconds = 10)
    {
        round.NoShowMarshals = new List<MarshalNoShow> { MarshalRule.Log($"{transponder} (Turn 1)", transponder, "Stock", rule, seconds, T0) };
        round.MarshalNoShows = new List<string> { $"{transponder} (Turn 1)" };
        _rounds.Update(round);
    }

    private List<QualifyingCalculator.Entry> Standings(RaceClass raceClass) =>
        QualifyingCalculator.Compute("Stock", _rounds, SessionHistoryStore.FromSessions(_sessions), _profiles, raceClass);

    [Fact]
    public void A_marshal_who_does_not_turn_up_loses_their_best_qualifier_and_it_says_why()
    {
        var q1 = RunHeat("Q1 Heat 1", T0, ("A", 14, 300_000), ("B", 13, 300_000));
        RunHeat("Q2 Heat 1", T0.AddMinutes(10), ("A", 12, 300_000), ("B", 13, 301_000));

        var best1 = new RaceClass { Name = "Stock", CountBestRounds = 1 };
        Assert.Equal("A", Standings(best1)[0].Transponder);

        LogNoShow(q1, "A", MarshalNoShowRule.LosesBestQualifier);

        var after = Standings(best1);
        var a = after.Single(e => e.Transponder == "A");

        Assert.Equal("B", after[0].Transponder);
        Assert.Equal(12, a.BestLaps);
        Assert.Equal("Lost Q1 Heat 1 (14 laps) - marshal no-show, Q1 Heat 1 (Turn 1)", a.Forfeit);
        Assert.Equal(string.Empty, after.Single(e => e.Transponder == "B").Forfeit);

        // The rule "Nothing" only logs.
        LogNoShow(q1, "A", MarshalNoShowRule.Nothing);
        Assert.Equal("A", Standings(best1)[0].Transponder);
    }

    [Fact]
    public void Under_points_the_marshal_loses_their_best_round()
    {
        var q1 = RunHeat("Q1 Heat 1", T0, ("A", 14, 300_000), ("B", 13, 300_000));
        RunHeat("Q2 Heat 1", T0.AddMinutes(10), ("A", 14, 300_000), ("B", 13, 301_000));

        var points = new RaceClass { Name = "Stock", QualifyingMethod = QualifyingMethod.RoundPoints, CountBestRounds = 2 };
        LogNoShow(q1, "A", MarshalNoShowRule.LosesBestQualifier);

        var a = Standings(points).Single(e => e.Transponder == "A");

        // Q1 1st (1 pt) is gone: it scores as missed (field 2 + 1 = 3), Q2 still 1.
        Assert.Equal(4, a.Points);
        Assert.Contains("lost (marshal)", a.PointsDetail);
        Assert.StartsWith("Lost Q1 (1st) - marshal no-show", a.Forfeit);
    }

    [Fact]
    public void On_the_ifmar_scale_the_marshals_lost_round_scores_ifmars_missed_round()
    {
        // 12 Nov 2026: the lost round is a round not run, on the class's own
        // scale - IFMAR's 500, not the club's field + 1.
        var q1 = RunHeat("Q1 Heat 1", T0, ("A", 14, 300_000), ("B", 13, 300_000));
        RunHeat("Q2 Heat 1", T0.AddMinutes(10), ("A", 14, 300_000), ("B", 13, 301_000));

        var points = new RaceClass
        {
            Name = "Stock",
            QualifyingMethod = QualifyingMethod.RoundPoints,
            CountBestRounds = 2,
            RoundPointsScale = RoundPointsScale.Ifmar,
        };
        LogNoShow(q1, "A", MarshalNoShowRule.LosesBestQualifier);

        var standings = Standings(points);
        var a = standings.Single(e => e.Transponder == "A");

        // Q1 lost (500), Q2 TQ (0). B keeps the places they earned: 2nd and 2nd, 2 + 2.
        Assert.Equal(500, a.Points);
        Assert.Contains("Q1 lost (marshal) (500 pts)", a.PointsDetail);
        Assert.Equal(4, standings.Single(e => e.Transponder == "B").Points);
        Assert.Equal("B", standings[0].Transponder);
    }

    [Fact]
    public void A_time_penalty_goes_on_their_next_race_and_comes_off_with_the_no_show()
    {
        var marshalled = Round("q1h2", "Q1 Heat 2");
        LogNoShowOn(marshalled, "A");

        var next = new SessionRecord
        {
            Id = "next",
            SessionType = RaceSessionType.Race,
            Results = new List<SessionResultEntry>
            {
                new() { Transponder = "A", DriverName = "A", Laps = 14, ElapsedMs = 300_000, Position = 1 },
                new() { Transponder = "B", DriverName = "B", Laps = 14, ElapsedMs = 305_000, Position = 2 },
            },
        };

        // Not on the round they missed - only a later race carries it.
        Assert.Empty(MarshalRule.PendingPenalties(next.Results, marshalled.Id, new[] { marshalled }, null));

        var pending = MarshalRule.PendingPenalties(next.Results, "q2h1", new[] { marshalled }, null);
        Assert.Single(pending);

        var lines = MarshalRule.ApplyPenalties(next, pending);
        Assert.Single(lines);
        Assert.Equal("next", marshalled.NoShowMarshals![0].AppliedToSessionId);

        var a = next.Results.Single(r => r.Transponder == "A");
        Assert.Equal(10_000, a.TimePenaltyMs);
        Assert.Equal(310_000, a.ElapsedMs);
        Assert.Equal("B", next.Results[0].Transponder); // re-ordered
        Assert.Contains(next.Edits, e => e.Reason.Contains("marshal no-show, Q1 Heat 2 (Turn 1)"));

        // Applied once only.
        Assert.Empty(MarshalRule.PendingPenalties(next.Results, "q3h1", new[] { marshalled }, null));

        Assert.True(MarshalRule.TakePenaltyOff(next, marshalled, marshalled.NoShowMarshals[0]));
        Assert.Equal(0, next.Results.Single(r => r.Transponder == "A").TimePenaltyMs);
    }

    private static void LogNoShowOn(RoundDefinition round, string transponder) =>
        round.NoShowMarshals = new List<MarshalNoShow> { MarshalRule.Log($"{transponder} (Turn 1)", transponder, "Stock", MarshalNoShowRule.TimePenalty, 10, T0) };

    [Fact]
    public void Only_tonights_no_shows_count_and_the_rule_is_a_club_setting_defaulting_to_the_best_qualifier()
    {
        var lastWeek = Round("old", "Q1 Heat 1");
        lastWeek.NoShowMarshals = new List<MarshalNoShow> { MarshalRule.Log("A (Turn 1)", "A", "Stock", MarshalNoShowRule.LosesBestQualifier, 0, T0) };

        Assert.Empty(MarshalRule.ForfeitsFor("Stock", new[] { lastWeek }, new[] { "tonight" }));
        Assert.Single(MarshalRule.ForfeitsFor("Stock", new[] { lastWeek }, null));
        Assert.Empty(MarshalRule.ForfeitsFor("Mod", new[] { lastWeek }, null));

        var settings = new AppSettings();
        Assert.Equal(MarshalNoShowRule.LosesBestQualifier, settings.MarshalNoShowRule);
        Assert.Equal(10, settings.MarshalNoShowPenaltySeconds);
        Assert.Equal("Loses their best qualifying result", MarshalRule.Words(MarshalNoShowRule.LosesBestQualifier));

        Assert.Contains("MarshalRule.PendingPenalties(record.Results, round.Id", Source("RaceFilingService.cs"));
        Assert.Contains("raceControl.MarshalNoShowTakenBack +=", Source("MainForm.NightSetup.cs"));
        Assert.Contains("MarshalRuleCaption", Source("RaceDayEventControl.cs"));
        Assert.Contains("MarshalRule.Consequence(noShow)", Source("EventReportBuilder.cs"));
        Assert.Contains("Marshal rule: ", Source("QualifyingStandingsForm.cs"));
    }

    // ------------------------------------------------------------ 5. number order

    [Fact]
    public void Number_order_deals_by_race_number_numbers_first_then_text_then_none()
    {
        var numbers = new Dictionary<string, string>
        {
            ["Al"] = "",
            ["Bea"] = "10",
            ["Cy"] = "2",
            ["Di"] = "7a",
            ["Ed"] = "",
        };

        var order = FirstRound.ByRaceNumber(new[] { "Al", "Bea", "Cy", "Di", "Ed" }, d => numbers[d]);
        Assert.Equal(new[] { "Cy", "Bea", "Di", "Al", "Ed" }, order);

        var seeding = FirstRound.For(new[] { "Al", "Bea", "Cy" }, FirstRoundSeeding.RaceNumber, new Dictionary<string, long>(), new[] { 2, 1 }, 1, d => numbers[d]);
        Assert.Equal(new[] { "Cy", "Bea", "Al" }, seeding.Order);
        Assert.Null(seeding.FirstRoundHeats); // dealt in turn, as alphabetical is

        Assert.Equal(4, (int)FirstRoundSeeding.RaceNumber);
        Assert.Equal("By race number", FirstRound.Words(FirstRoundSeeding.RaceNumber));
        Assert.Contains("transponder => _profiles.Get(transponder)?.RaceNumber", Source("RaceProgramControl.cs"));
    }

    // ------------------------------------------------------------ 6. rest gap

    private static IReadOnlyCollection<string> Heat(params string[] drivers) => drivers;

    [Fact]
    public void A_gap_of_two_keeps_a_driver_two_races_apart_where_it_can()
    {
        // Sam races heat 0 and heat 1 of the built order; with two off, both other heats go between.
        var heats = new[] { Heat("sam", "a"), Heat("sam", "b"), Heat("c"), Heat("d") };

        var order = RestOrder.Arrange(heats, null, 2);

        Assert.Equal(new[] { 0, 2, 3, 1 }, order);
        Assert.Equal(1, RestOrder.Clashes(heats, null, 2));
        Assert.Equal(0, RestOrder.Clashes(order.Select(i => heats[i]).ToList(), null, 2));

        // 0 keeps the built order; 1 is the old rule.
        Assert.Equal(new[] { 0, 1, 2, 3 }, RestOrder.Arrange(heats, null, 0));
        Assert.Equal(RestOrder.Arrange(heats), RestOrder.Arrange(heats, null, 1));
    }

    [Fact]
    public void A_change_by_hand_that_puts_a_driver_too_close_is_named()
    {
        var before = new List<(string, IReadOnlyCollection<string>)>
        {
            ("Q2 Heat 1", Heat("max", "a")),
            ("Q2 Heat 2", Heat("b")),
            ("Q2 Heat 3", Heat("max", "c")),
        };
        var after = new List<(string, IReadOnlyCollection<string>)>
        {
            ("Q2 Heat 1", Heat("max", "a")),
            ("Q2 Heat 2", Heat("b", "max")),
            ("Q2 Heat 3", Heat("c")),
        };

        Assert.Empty(RestOrder.NewlyTooClose(before, before, 1));

        var tooClose = RestOrder.NewlyTooClose(before, after, 1);
        Assert.Single(tooClose);

        var words = RestOrder.Warning(tooClose, t => t == "max" ? "Max Power" : t, 1);
        Assert.StartsWith("Max Power would race in Q2 Heat 1 and Q2 Heat 2 back to back.", words);

        // With two off, the "before" order is already too close (one race between) - not news.
        Assert.Empty(RestOrder.NewlyTooClose(before, before, 2));
        Assert.Contains("with only 1 race between", RestOrder.Warning(RestOrder.TooClose(before, 2), t => t, 2));
    }

    [Fact]
    public void The_gap_is_a_setup_field_carried_over_and_used_by_the_build_auto_start_and_the_warnings()
    {
        var lastWeek = new RaceEvent { RacesOffBetween = 2 };
        Assert.Equal(2, NightTemplates.From(lastWeek, "Tuesday").RacesOffBetween);

        Assert.Contains("RacesOffCaption", Source("RaceDayEventControl.cs"));
        Assert.Contains("current.RacesOffBetween = old.RacesOffBetween;", Source("RaceDayEventControl.cs"));
        Assert.Contains("raceEvent.RacesOffBetween = lastLikeThis.RacesOffBetween;", Source("EventSpine.cs"));
        Assert.Contains("var justRan = RecentlyRan(next, lastRound);", Source("RaceControlControl.AutoStart.cs"));
        Assert.Contains("restCheck: RosterRestWarning", Source("RaceProgramControl.cs"));
        Assert.Contains("if (!MoveKeepsRest(ids, anchor))", Source("RaceProgramControl.cs"));
        Assert.Contains("_restCheck?.Invoke(changes)", Source("HeatBalanceForm.cs"));
    }

    /// <summary>The app source, for checks on how the pieces are wired in.</summary>
    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }
}
