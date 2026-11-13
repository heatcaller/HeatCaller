namespace HeatCaller.Tests;

/// <summary>
/// Setting up the night, step 4 (12 Nov 2026, setup plan B1, B2 and D6):
/// start tonight like last time, the heats building themselves as entries
/// close, and late check-ins going into the heats, nearest their pace, with
/// nobody else moving: by themselves before the heats are posted, and asked
/// about after.
/// </summary>
public sealed class NightSetupLateArrivalsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static RoundDefinition Heat(string label, bool run = false, params string[] cars) => new()
    {
        RoundLabel = label,
        ClassName = "Stock",
        Kind = RoundKind.Qualifier,
        LinkedSessionId = run ? Guid.NewGuid().ToString("N") : null,
        AssignedTransponders = cars.ToList(),
    };

    // --- D6: where a late driver goes -------------------------------------------

    [Fact]
    public void A_late_driver_goes_in_the_heat_nearest_their_pace()
    {
        var slow = Heat("Q1 Heat 1", cars: new[] { "s1", "s2", "s3" });
        var fast = Heat("Q1 Heat 2", cars: new[] { "f1", "f2", "f3", "f4" });
        var laps = new Dictionary<string, long>
        {
            ["s1"] = 26000, ["s2"] = 27000, ["s3"] = 28000,
            ["f1"] = 20000, ["f2"] = 20500, ["f3"] = 21000, ["f4"] = 21500,
            ["late"] = 20800,
        };

        var places = LateEntry.Place(new[] { slow, fast }, "late", null, laps, heatSize: 8);

        // The fast heat, though it's the bigger one.
        Assert.Same(fast, Assert.Single(places));
    }

    [Fact]
    public void A_heat_at_the_night_s_size_is_passed_over_while_another_has_room()
    {
        var full = Heat("Q1 Heat 1", cars: new[] { "f1", "f2" });
        var room = Heat("Q1 Heat 2", cars: new[] { "s1" });
        var laps = new Dictionary<string, long> { ["f1"] = 20000, ["f2"] = 20100, ["s1"] = 30000, ["late"] = 20050 };

        Assert.Same(room, Assert.Single(LateEntry.Place(new[] { full, room }, "late", null, laps, heatSize: 2)));
    }

    [Fact]
    public void A_late_driver_with_no_pace_goes_in_the_smallest_heat_as_before()
    {
        var big = Heat("Q1 Heat 1", cars: new[] { "a", "b", "c" });
        var small = Heat("Q1 Heat 2", cars: new[] { "d" });

        Assert.Same(small, Assert.Single(LateEntry.Place(new[] { big, small }, "late", null, new Dictionary<string, long> { ["a"] = 20000 })));
        Assert.Same(small, Assert.Single(LateEntry.Place(new[] { big, small }, "late", null)));
    }

    [Fact]
    public void A_late_driver_goes_in_every_round_still_to_run_and_never_one_run_or_on_track()
    {
        var q1Run = Heat("Q1 Heat 1", run: true, cars: new[] { "a" });
        var q2OnTrack = Heat("Q2 Heat 1", cars: new[] { "a" });
        var q2Next = Heat("Q2 Heat 2", cars: new[] { "b" });
        var q3 = Heat("Q3 Heat 1", cars: new[] { "a", "b" });

        var places = LateEntry.Place(new[] { q1Run, q2OnTrack, q2Next, q3 }, "late", q2OnTrack.Id);

        Assert.Equal(new[] { q2Next, q3 }, places);
    }

    // --- D6: who is a late arrival ----------------------------------------------

    [Fact]
    public void A_late_arrival_is_on_the_roster_but_in_none_of_the_class_s_heats()
    {
        var rounds = new List<RoundDefinition> { Heat("Q1 Heat 1", cars: new[] { "a", "b" }) };
        var classes = new[] { ("Stock", (IReadOnlyList<string>)new[] { "a", "b", "c", "d" }) };

        var found = LateArrivals.Find(classes, _ => rounds, null, new[] { LateArrivals.Key("Stock", "d") });

        // d was declined.
        Assert.Equal(new[] { new LateArrivals.Arrival("Stock", "c") }, found);
    }

    [Fact]
    public void No_late_arrival_before_the_heats_are_built_or_once_they_have_all_run()
    {
        var classes = new[] { ("Stock", (IReadOnlyList<string>)new[] { "a", "late" }) };

        Assert.Empty(LateArrivals.Find(classes, _ => new List<RoundDefinition>(), null, null));
        Assert.Empty(LateArrivals.Find(classes, _ => new List<RoundDefinition> { Heat("Q1 Heat 1", run: true, cars: new[] { "a" }) }, null, null));
    }

    [Fact]
    public void The_headline_names_one_driver_or_counts_several()
    {
        Assert.Equal("Max Power checked in after the heats were posted.", LateArrivals.Headline(new[] { "Max Power" }, clubNight: false));
        Assert.Equal("2 drivers checked in after the races were posted.", LateArrivals.Headline(new[] { "Al", "Hugh" }, clubNight: true));
    }

    // --- The whole flow, on the program page ------------------------------------

    [Fact]
    public void Before_posting_a_late_check_in_goes_straight_in_and_after_posting_it_is_asked_about() => OnUiThread(() =>
    {
        using var night = new Night();
        night.Driver("1", "Max Power");
        night.Driver("2", "Al Gorithim");
        night.Driver("3", "Hugh Turn");

        Assert.True(night.Program.BuildQualifying(string.Empty).Ok);
        Assert.Null(night.Program.AbsorbLateArrivals());

        // Checked in after the build, before posting: in by itself.
        night.Driver("4", "Rhoda Rage");
        Assert.NotNull(night.Program.AbsorbLateArrivals());
        Assert.Contains(night.DayRounds(), r => r.AssignedTransponders.Contains("4"));
        Assert.Equal(3, night.DayRounds().Count(r => r.AssignedTransponders.Contains("1")));

        // Posted: the next one is asked about, not added.
        var day = night.Events.Active!;
        day.HeatsPostedUtc = DateTime.UtcNow;
        night.Events.Upsert(day);

        night.Driver("5", "Axel Grease");
        Assert.Null(night.Program.AbsorbLateArrivals());
        Assert.DoesNotContain(night.DayRounds(), r => r.AssignedTransponders.Contains("5"));
        Assert.Equal(ProgramStage.LateArrivals, night.Program.NextStep().Stage);

        // A club that adds them without asking: in it goes.
        night.Settings.AddLateCheckInsWithoutAsking = true;
        Assert.NotNull(night.Program.AbsorbLateArrivals());
        Assert.Contains(night.DayRounds(), r => r.AssignedTransponders.Contains("5"));
        Assert.NotEqual(ProgramStage.LateArrivals, night.Program.NextStep().Stage);
    });

    [Fact]
    public void A_driver_taken_out_of_the_heats_on_purpose_is_not_put_back() => OnUiThread(() =>
    {
        using var night = new Night();
        night.Driver("1", "Max Power");
        night.Driver("2", "Al Gorithim");

        Assert.True(night.Program.BuildQualifying(string.Empty).Ok);

        foreach (var round in night.DayRounds())
        {
            round.AssignedTransponders.Remove("2");
            night.Rounds.Update(round);
        }

        Assert.Null(night.Program.AbsorbLateArrivals());
        Assert.DoesNotContain(night.DayRounds(), r => r.AssignedTransponders.Contains("2"));
    });

    [Fact]
    public void The_heats_build_themselves_when_entries_close_but_only_once_and_only_when_wanted() => OnUiThread(() =>
    {
        using var night = new Night();
        night.Driver("1", "Max Power");
        night.Driver("2", "Al Gorithim");

        night.Settings.BuildHeatsWhenEntriesClose = false;
        Assert.Null(night.Program.BuildWhenEntriesClose());
        Assert.Empty(night.DayRounds());

        night.Settings.BuildHeatsWhenEntriesClose = true;
        Assert.NotNull(night.Program.BuildWhenEntriesClose());
        Assert.NotEmpty(night.DayRounds());

        // Built already: nothing more to do.
        Assert.Null(night.Program.BuildWhenEntriesClose());
    });

    // --- B1: start tonight like last time ----------------------------------------

    [Fact]
    public void Tonight_starts_like_a_day_from_last_week_but_not_like_tonight_s_own() => OnUiThread(() =>
    {
        using var night = new Night();

        var tonight = night.Events.Active!;
        Assert.Null(night.Program.LastDayToCopy(tonight.StartedUtc.AddHours(1)));
        Assert.Same(tonight, night.Program.LastDayToCopy(tonight.StartedUtc.AddDays(7)));

        tonight.ClassNames.Clear();
        night.Events.Upsert(tonight);
        Assert.Null(night.Program.LastDayToCopy(tonight.StartedUtc.AddDays(7)));
    });

    private static void OnUiThread(Action body) =>
        StaThread.Run(() =>
        {
            body();
            return true;
        }, TimeSpan.FromMinutes(1), () => "setup plan step 4");

    /// <summary>A race day with one class, Stock, and the program page on it.</summary>
    private sealed class Night : IDisposable
    {
        public readonly AppSettings Settings = new();
        public readonly UndoService Undo = new();
        public readonly RaceEventStore Events = RaceEventStore.Load();
        public readonly RaceClassStore Classes = RaceClassStore.Load();
        public readonly ProfileStore Profiles = ProfileStore.Load();
        public readonly RoundDefinitionStore Rounds = RoundDefinitionStore.Load();
        public readonly SessionHistoryStore History = SessionHistoryStore.Load();
        public readonly CheckInStore CheckIns = CheckInStore.Load();
        public readonly TechInspectionStore Tech = TechInspectionStore.Load();
        public readonly DayScheduleStore Schedule = DayScheduleStore.Load();
        public readonly TrackLayoutStore Tracks = TrackLayoutStore.Load();
        public readonly RaceProgramControl Program;

        public Night()
        {
            EventSpine.Bind(Events, CheckIns, Tech, Schedule, Rounds);
            Classes.Upsert(new RaceClass { Name = "Stock" });
            EventSpine.StartEvent(Events, "Tonight", classNames: new[] { "Stock" }, qualifyingRounds: 3);

            Program = new RaceProgramControl(Classes, Profiles, Rounds, Tracks, History, CheckIns, Settings, Undo, Events, Schedule);
        }

        /// <summary>A Stock driver, checked in.</summary>
        public void Driver(string transponder, string name)
        {
            Profiles.Upsert(new DriverProfile { Transponder = transponder, DriverName = name, ClassName = "Stock" });
            CheckIns.SetCheckedIn(transponder, true);
        }

        public List<RoundDefinition> DayRounds()
        {
            var ids = Events.Active!.RoundIds.ToHashSet(StringComparer.Ordinal);
            return Rounds.All.Where(r => ids.Contains(r.Id)).ToList();
        }

        public void Dispose() => Program.Dispose();
    }
}
