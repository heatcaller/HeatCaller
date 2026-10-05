namespace HeatCaller.Tests;

/// <summary>
/// Results and points (4 Oct bug hunt #4): the Event Report naming results
/// by the transponder's current owner, a race-day delete missing sector
/// records on laps Edit Result merged, turning off a championship's
/// automatic rule emptying its table and dropping exclusions, the Record
/// Holder badge ignoring the live track record, and cars that never turned
/// a lap still counting for Comeback Kid, Knows Everyone and Old Friends.
/// </summary>
public sealed class BugHunt4ResultsPointsTests : IDisposable
{
    private const string Layout = "Club Oval";

    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- results-2: Event Report names ----------------------------------------

    [Fact]
    public void The_event_report_names_the_driver_who_raced_not_the_transponders_new_owner()
    {
        var profiles = Profiles(new DriverProfile { Transponder = "4471", DriverName = "Jo" });
        var day = DateTime.Now.Date.AddHours(12);

        var session = new SessionRecord
        {
            Label = "Heat 1",
            SessionType = RaceSessionType.Race,
            StartedAtUtc = day.ToUniversalTime(),
            EndedAtUtc = day.AddMinutes(5).ToUniversalTime(),
            Results =
            {
                new SessionResultEntry
                {
                    Transponder = "4471", DriverName = "Sam", Position = 1, Laps = 3, ElapsedMs = 30_000,
                    FastLapMs = 9_000, LapTimesMs = { 2_000, 9_000, 9_500 },
                },
            },
            Edits =
            {
                new ResultEdit { Transponder = "4471", DriverName = "Sam", Change = "time penalty 0s -> 5s", Reason = "cut" },
            },
        };

        var html = Report(day, profiles, session);

        Assert.Contains("Fastest lap - Sam", html);
        Assert.Contains("<td>Sam</td>", html);
        Assert.Contains("<strong>Sam</strong>", html);
        Assert.Contains("<li>Sam: ", html);
        Assert.DoesNotContain("Fastest lap - Jo", html);
        Assert.DoesNotContain("<td>Jo</td>", html);
        Assert.DoesNotContain("<strong>Jo</strong>", html);
    }

    [Fact]
    public void Two_drivers_who_shared_a_transponder_on_the_day_each_get_a_fastest_lap_row()
    {
        var profiles = Profiles(new DriverProfile { Transponder = "4471", DriverName = "Jo" });
        var day = DateTime.Now.Date.AddHours(12);

        var html = Report(day, profiles,
            Heat(day, "Heat 1", "4471", "Sam", 9_000),
            Heat(day.AddMinutes(20), "Heat 2", "4471", "Jo", 9_400));

        var records = html[html.IndexOf("Fastest laps of the day", StringComparison.Ordinal)..];

        Assert.Contains("<td>Sam</td><td class=\"num\">9.000s</td>", records);
        Assert.Contains("<td>Jo</td><td class=\"num\">9.400s</td>", records);
    }

    [Fact]
    public void Two_different_drivers_with_the_same_name_each_get_a_fastest_lap_row()
    {
        var day = DateTime.Now.Date.AddHours(12);

        var html = Report(day, Profiles(),
            Heat(day, "Heat 1", "100", "Chris", 9_000),
            Heat(day.AddMinutes(20), "Heat 2", "200", "Chris", 9_400),
            Heat(day.AddMinutes(40), "Heat 3", "100", "chris", 9_200));

        var records = html[html.IndexOf("Fastest laps of the day", StringComparison.Ordinal)..];
        records = records[..records.IndexOf("</table>", StringComparison.Ordinal)];

        // One row per Chris: the one on 100 by his best of two heats.
        Assert.Contains("<td>Chris</td><td class=\"num\">9.000s</td>", records);
        Assert.Contains("<td>Chris</td><td class=\"num\">9.400s</td>", records);
        Assert.DoesNotContain("9.200s", records);
    }

    // --- results-4: sector records on a merged lap ------------------------------

    [Fact]
    public void A_race_day_delete_finds_a_sector_record_on_a_lap_edit_result_merged()
    {
        var record = SplitLoopRace(
            new List<long> { 3_000, 20_000, 20_000, 3_500 },
            new List<long[]?> { null, new long[] { 9_000, 11_000 }, new long[] { 8_000, 12_000 }, null });

        Assert.Equal(1, ResultEditor.Apply(record, new[] { new ResultAdjustment("T1", -1, 0, 0, "") }, "double read", "Laptop"));

        // Lap 2 is now 23.5s, so its 8.0 + 12.0 sectors no longer add up to it.
        Assert.True(RaceDayDeleter.SetIn(new[] { record }, new TrackLayout { Name = "T" }, 2,
            new SectorRecord { Sector = 1, Ms = 8_000, Holder = "Alice" }));
    }

    [Fact]
    public void A_race_day_delete_finds_a_sector_record_on_the_lap_edit_result_folded_away()
    {
        var record = SplitLoopRace(
            new List<long> { 3_000, 20_000, 20_000, 20_500 },
            new List<long[]?> { null, new long[] { 9_000, 11_000 }, new long[] { 8_000, 12_000 }, new long[] { 10_000, 10_500 } });

        Assert.Equal(1, ResultEditor.Apply(record, new[] { new ResultAdjustment("T1", -1, 0, 0, "") }, "double read", "Laptop"));

        Assert.True(RaceDayDeleter.SetIn(new[] { record }, new TrackLayout { Name = "T" }, 2,
            new SectorRecord { Sector = 2, Ms = 10_500, Holder = "Alice" }));
    }

    // --- points-2: turning the championship rule off -----------------------------

    [Fact]
    public void Turning_off_add_new_races_automatically_keeps_the_races_already_counted()
    {
        var (history, rounds, races) = ThreeRaces();
        var season = new Season { Name = "Summer", AutoInclude = true, MainsOnly = false };
        season.ExcludedSessionIds.Add(races[1].Id);

        var before = Ids(EventSpine.SessionsFor(season, null, rounds, history));
        Assert.Equal(new[] { races[0].Id, races[2].Id }, before);

        SeasonsControl.ApplyRules(season, s => s.AutoInclude = false, null, rounds, history);

        Assert.False(season.AutoInclude);
        Assert.Equal(before, Ids(EventSpine.SessionsFor(season, null, rounds, history)));

        // The rained-off round stays out when the rule is turned back on.
        season.AutoInclude = true;
        Assert.Equal(before, Ids(EventSpine.SessionsFor(season, null, rounds, history)));
    }

    [Fact]
    public void A_race_taken_out_by_hand_stays_out_after_a_tick_with_the_rule_off()
    {
        var (history, rounds, races) = ThreeRaces();
        var season = new Season { Name = "Summer", AutoInclude = false, MainsOnly = false };
        season.IncludedSessionIds.AddRange(new[] { races[0].Id, races[2].Id });
        season.ExcludedSessionIds.Add(races[1].Id);

        // The director ticks nothing new - a points edit saves the ticks as they stand.
        var (forcedIn, forcedOut) = SeasonsControl.OverridesFor(season,
            new[] { (races[0], true), (races[1], false), (races[2], true) }, rounds, null, history);

        Assert.Equal(new[] { races[0].Id, races[2].Id }, forcedIn);
        Assert.Equal(new[] { races[1].Id }, forcedOut);
    }

    [Fact]
    public void A_race_taken_out_by_hand_stays_out_when_the_rule_no_longer_takes_it()
    {
        var (history, rounds, races) = ThreeRaces();

        // Narrowed to a class none of these races are in.
        var season = new Season { Name = "Summer", AutoInclude = true, MainsOnly = false, ClassName = "Stock" };
        season.ExcludedSessionIds.Add(races[1].Id);

        var (_, forcedOut) = SeasonsControl.OverridesFor(season,
            new[] { (races[0], false), (races[1], false), (races[2], false) }, rounds, null, history);

        Assert.Equal(new[] { races[1].Id }, forcedOut);
    }

    [Fact]
    public void Ticking_a_race_taken_out_by_hand_puts_it_back_in()
    {
        var (history, rounds, races) = ThreeRaces();
        var season = new Season { Name = "Summer", AutoInclude = true, MainsOnly = false };
        season.ExcludedSessionIds.Add(races[1].Id);

        var (forcedIn, forcedOut) = SeasonsControl.OverridesFor(season,
            new[] { (races[0], true), (races[1], true), (races[2], true) }, rounds, null, history);

        Assert.Empty(forcedIn);
        Assert.Empty(forcedOut);
    }

    [Fact]
    public void Turning_the_rule_off_and_on_again_lets_a_later_class_change_drop_the_old_class()
    {
        var rounds = RoundDefinitionStore.Load();
        var stockMain = new RoundDefinition { ClassName = "Stock", RoundLabel = "A-Main", Kind = RoundKind.Main };
        var modMain = new RoundDefinition { ClassName = "Mod", RoundLabel = "A-Main", Kind = RoundKind.Main };
        rounds.Add(stockMain);
        rounds.Add(modMain);

        var stock = Race(DateTime.UtcNow.AddDays(-14), Entry("A", 1, 9_000));
        stock.RoundId = stockMain.Id;
        var mod = Race(DateTime.UtcNow.AddDays(-13), Entry("B", 1, 9_000));
        mod.RoundId = modMain.Id;

        // A freeform race no rule takes, ticked in by hand.
        var freeform = Race(DateTime.UtcNow.AddDays(-12), Entry("C", 1, 9_000));
        var history = SessionHistoryStore.FromSessions(new[] { stock, mod, freeform });

        var season = new Season { Name = "Summer", AutoInclude = true, MainsOnly = false, ClassName = "Stock" };
        season.IncludedSessionIds.Add(freeform.Id);

        SeasonsControl.ApplyRules(season, s => s.AutoInclude = false, null, rounds, history);
        SeasonsControl.ApplyRules(season, s => s.AutoInclude = true, null, rounds, history);

        // Back on, the rule takes the Stock race again, so only the hand tick is stored.
        Assert.Equal(new[] { freeform.Id }, season.IncludedSessionIds);
        Assert.Equal(new[] { stock.Id, freeform.Id }, Ids(EventSpine.SessionsFor(season, null, rounds, history)));

        SeasonsControl.ApplyRules(season, s => s.ClassName = "Mod", null, rounds, history);

        Assert.Equal(new[] { mod.Id, freeform.Id }, Ids(EventSpine.SessionsFor(season, null, rounds, history)));
    }

    // --- points-3: Record Holder follows the live record --------------------------

    [Fact]
    public void A_phantom_lap_race_control_refused_does_not_earn_record_holder()
    {
        var now = DateTime.UtcNow;
        var tonight = Race(now, Entry("200", position: 1, fast: 3_100), Entry("ALICE", position: 2, fast: 10_200));

        var unlocks = Evaluate(tonight, Live(10_000, now.AddDays(-7)), OldRace(10_000));

        Assert.DoesNotContain(unlocks, u => u.Badge.Id == AchievementCatalogue.TrackRecordHolder);
    }

    [Fact]
    public void The_real_record_holder_still_gets_record_holder_when_a_phantom_was_refused_in_the_same_race()
    {
        var now = DateTime.UtcNow;
        var tonight = Race(now, Entry("200", position: 1, fast: 3_100), Entry("ALICE", position: 2, fast: 9_800));

        var unlocks = Evaluate(tonight, Live(9_800, now.AddMinutes(-3)), OldRace(10_000));

        Assert.DoesNotContain(unlocks, u => u.Transponder == "200" && u.Badge.Id == AchievementCatalogue.TrackRecordHolder);
        Assert.Contains(unlocks, u => u.Transponder == "ALICE" && u.Badge.Id == AchievementCatalogue.TrackRecordHolder);
    }

    [Fact]
    public void Record_holder_is_awarded_again_after_the_record_is_reset()
    {
        var now = DateTime.UtcNow;
        var tonight = Race(now, Entry("ALICE", position: 1, fast: 12_000), Entry("BOB", position: 2, fast: 12_400));

        // Reset Record on the re-laid track: the old layout's 10.000 is still in history.
        var unlocks = Evaluate(tonight, Live(12_000, now.AddMinutes(-3)), OldRace(10_000));

        Assert.Contains(unlocks, u => u.Transponder == "ALICE" && u.Badge.Id == AchievementCatalogue.TrackRecordHolder);
        Assert.DoesNotContain(unlocks, u => u.Transponder == "BOB" && u.Badge.Id == AchievementCatalogue.TrackRecordHolder);
    }

    [Fact]
    public void Record_holder_is_not_awarded_for_a_layout_that_is_not_on_track_layouts()
    {
        var tonight = Race(DateTime.UtcNow, Entry("ALICE", position: 1, fast: 9_800), Entry("BOB", position: 2, fast: 9_900));

        var unlocks = Evaluate(tonight, _ => null, OldRace(10_000));

        Assert.DoesNotContain(unlocks, u => u.Badge.Id == AchievementCatalogue.TrackRecordHolder);
    }

    [Fact]
    public void The_first_race_on_a_new_layout_still_earns_no_record_holder()
    {
        var now = DateTime.UtcNow;
        var tonight = Race(now, Entry("ALICE", position: 1, fast: 9_800), Entry("BOB", position: 2, fast: 9_900));

        var unlocks = Evaluate(tonight, Live(9_800, now.AddMinutes(-3)));

        Assert.DoesNotContain(unlocks, u => u.Badge.Id == AchievementCatalogue.TrackRecordHolder);
    }

    // --- points-4: cars that never turned a lap -----------------------------------

    [Fact]
    public void A_no_show_gridded_behind_does_not_stop_comeback_kid()
    {
        var tonight = Race(DateTime.UtcNow,
            Entry("A", position: 1, fast: 9_000, start: 1),
            Entry("B", position: 2, fast: 9_000, start: 2),
            Entry("F", position: 3, fast: 9_000, start: 6),
            Entry("C", position: 4, fast: 9_000, start: 3),
            Entry("D", position: 5, fast: 9_000, start: 4),
            Entry("E", position: 6, fast: 9_000, start: 5),
            Entry("G", position: 7, fast: 0, start: 7, laps: 0));

        var unlocks = Evaluate(tonight, Profiles());

        Assert.Contains(unlocks, u => u.Transponder == "F" && u.Badge.Id == AchievementCatalogue.ComebackKid && u.Detail == "P6 to P3");
    }

    [Fact]
    public void Cars_that_never_turned_a_lap_are_not_opponents_for_knows_everyone()
    {
        // Eleven others on the roster, only two of them ever lapping.
        var old = Race(DateTime.UtcNow.AddDays(-7),
            new[] { Entry("100", 1, 9_000), Entry("201", 2, 9_000), Entry("202", 3, 9_000) }
                .Concat(Enumerable.Range(203, 9).Select((t, i) => Entry(t.ToString(), 4 + i, 0, laps: 0)))
                .ToArray());

        var tonight = Race(DateTime.UtcNow, Entry("100", 1, 9_000), Entry("201", 2, 9_000), Entry("202", 3, 9_000));

        var unlocks = Evaluate(tonight, Profiles(), old);

        Assert.DoesNotContain(unlocks, u => u.Transponder == "100" && u.Badge.Id == AchievementCatalogue.RacedEveryone);
    }

    [Fact]
    public void A_race_a_driver_never_turned_a_lap_in_does_not_count_for_knows_everyone()
    {
        var old = Race(DateTime.UtcNow.AddDays(-7),
            Enumerable.Range(201, 11).Select((t, i) => Entry(t.ToString(), 1 + i, 9_000))
                .Append(Entry("100", 12, 0, laps: 0))
                .ToArray());

        var tonight = Race(DateTime.UtcNow, Entry("100", 1, 9_000), Entry("201", 2, 9_000));

        var unlocks = Evaluate(tonight, Profiles(), old);

        Assert.DoesNotContain(unlocks, u => u.Transponder == "100" && u.Badge.Id == AchievementCatalogue.RacedEveryone);
    }

    [Fact]
    public void A_friend_who_never_turned_a_lap_in_earlier_races_does_not_count_for_old_friends()
    {
        var earlier = Enumerable.Range(1, 19)
            .Select(d => Race(DateTime.UtcNow.AddDays(-d), Entry("100", 1, 9_000), Entry("200", 2, 0, laps: 0)))
            .ToArray();

        var tonight = Race(DateTime.UtcNow, Entry("100", 1, 9_000), Entry("200", 2, 9_000));

        var unlocks = Evaluate(tonight, Profiles(), earlier);

        Assert.DoesNotContain(unlocks, u => u.Badge.Id == AchievementCatalogue.OldFriends);
    }

    [Fact]
    public void A_friend_who_never_turned_a_lap_tonight_does_not_count_for_old_friends()
    {
        var earlier = Enumerable.Range(1, 19)
            .Select(d => Race(DateTime.UtcNow.AddDays(-d), Entry("100", 1, 9_000), Entry("200", 2, 9_000)))
            .ToArray();

        var tonight = Race(DateTime.UtcNow, Entry("100", 1, 9_000), Entry("200", 2, 0, laps: 0));

        var unlocks = Evaluate(tonight, Profiles(), earlier);

        Assert.DoesNotContain(unlocks, u => u.Transponder == "100" && u.Badge.Id == AchievementCatalogue.OldFriends);
    }

    // --- Helpers ---------------------------------------------------------------

    private static ProfileStore Profiles(params DriverProfile[] drivers)
    {
        var store = ProfileStore.Load();
        foreach (var driver in drivers)
        {
            Assert.True(store.TryAdd(driver, out _));
        }

        return store;
    }

    private static string Report(DateTime day, ProfileStore profiles, params SessionRecord[] sessions) =>
        EventReportBuilder.Build(
            new EventReportBuilder.Options(day.Date, "Club night", IncludeQualifying: false, IncludeLapDetail: true,
                IncludeTech: false, IncludeRentals: false),
            SessionHistoryStore.FromSessions(sessions),
            RoundDefinitionStore.Load(),
            RaceClassStore.Load(),
            profiles,
            TrackLayoutStore.Load(),
            TechInspectionStore.Load(),
            RentalTransponderStore.Load());

    private static SessionRecord Heat(DateTime localStart, string label, string transponder, string name, long fast) => new()
    {
        Label = label,
        SessionType = RaceSessionType.Race,
        StartedAtUtc = localStart.ToUniversalTime(),
        EndedAtUtc = localStart.AddMinutes(5).ToUniversalTime(),
        Results =
        {
            new SessionResultEntry
            {
                Transponder = transponder, DriverName = name, Position = 1, Laps = 3, ElapsedMs = 30_000,
                FastLapMs = fast, LapTimesMs = { 2_000, fast, fast + 500 },
            },
        },
    };

    private static SessionRecord SplitLoopRace(List<long> laps, List<long[]?> sectors) => new()
    {
        Label = "Heat 1",
        SessionType = RaceSessionType.Race,
        TrackLayoutName = string.Empty,
        StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
        EndedAtUtc = DateTime.UtcNow,
        Results =
        {
            new SessionResultEntry
            {
                Transponder = "T1",
                DriverName = "Alice",
                Position = 1,
                Laps = laps.Count - 1,
                ElapsedMs = laps.Sum(),
                FastLapMs = laps.Skip(1).Min(),
                LapTimesMs = laps,
                SectorTimesMs = sectors,
            },
        },
    };

    private static (SessionHistoryStore History, RoundDefinitionStore Rounds, List<SessionRecord> Races) ThreeRaces()
    {
        var races = Enumerable.Range(1, 3)
            .Select(i => Race(DateTime.UtcNow.AddDays(-21 + i * 7), Entry("A", 1, 9_000), Entry("B", 2, 9_100)))
            .ToList();

        return (SessionHistoryStore.FromSessions(races), RoundDefinitionStore.Load(), races);
    }

    private static List<string> Ids(IEnumerable<SessionRecord> sessions) => sessions.Select(s => s.Id).ToList();

    private static Func<string, TrackLayout?> Live(long recordMs, DateTime setUtc) =>
        name => name == Layout
            ? new TrackLayout { Name = Layout, RecordLapMs = recordMs, RecordHolderName = "someone", RecordSetUtc = setUtc }
            : null;

    private static SessionRecord OldRace(long best) => Race(DateTime.UtcNow.AddDays(-7),
        Entry("DAVE", position: 1, fast: best),
        Entry("EVE", position: 2, fast: best + 400));

    private static SessionRecord Race(DateTime endedUtc, params SessionResultEntry[] results) => new()
    {
        Label = "A-Main",
        SessionType = RaceSessionType.Race,
        TrackLayoutName = Layout,
        StartedAtUtc = endedUtc.AddMinutes(-6),
        EndedAtUtc = endedUtc,
        Results = results.ToList(),
    };

    private static SessionResultEntry Entry(string transponder, int position, long fast, int start = 0, int laps = 12) => new()
    {
        Transponder = transponder,
        DriverName = transponder,
        Position = position,
        StartPosition = start == 0 ? position : start,
        Laps = laps,
        ElapsedMs = laps == 0 ? 0 : 300_000 + position * 1_000,
        FastLapMs = laps == 0 ? 0 : fast,
        LapTimesMs = laps == 0 ? new List<long>() : Enumerable.Repeat(fast + 500, laps - 1).Append(fast).Prepend(3_000L).ToList(),
    };

    private static List<AchievementUnlock> Evaluate(SessionRecord record, Func<string, TrackLayout?> layouts, params SessionRecord[] history) =>
        AchievementEvaluator.EvaluateFinishedRace(record, history, AchievementStore.Load(), profiles: Profiles(),
            context: new AchievementContext { TrackLayoutNamed = layouts });

    private static List<AchievementUnlock> Evaluate(SessionRecord record, ProfileStore profiles, params SessionRecord[] history) =>
        AchievementEvaluator.EvaluateFinishedRace(record, history, AchievementStore.Load(), profiles: profiles);
}
