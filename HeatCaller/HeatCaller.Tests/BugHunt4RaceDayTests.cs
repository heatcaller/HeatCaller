using System.Reflection;
using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 4, race day: the race-day manager and Set Up Tonight.
///
///  - Answers sent through the online relay carried transponder numbers in
///    full in their free text: the undo list on Today, the bump-up
///    question's names for a car nobody had named, and "already one of the
///    profile for 7654321's transponders".
///  - New Race Day from the phone made a bare race day - no classes, venue,
///    track or championship - which the next New Race Day on the laptop then
///    copied its blanks from.
///  - Set Up Tonight's check-in ticks weren't timed, so a slower online tap
///    made before them was applied over them.
///  - Set Up Tonight opened on a race day whatever the club ran, and kept
///    the name "Race Day ..." when the kind was changed to a club night.
///  - Set Up Tonight's Classes step took ticks and numbers with no race day
///    open, and threw them away without a word.
///
/// The ones marked Windows-only build real windows or the laptop's Program
/// tab, which need a single-threaded apartment and Windows itself.
/// </summary>
public sealed class BugHunt4RaceDayTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- Transponder numbers through the relay -------------------------------------------

    [Fact]
    public void A_clash_told_to_the_relay_shows_only_the_end_of_an_unnamed_owners_transponder()
    {
        var profiles = ProfileStore.Load();
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "7654321", AlternateTransponder = "555" }, out _));
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "4440001", DriverName = "Bea" }, out _));

        // Bea's alt set to the unnamed driver's alt, from the phone.
        var setAlt = RaceDayService.ClashForPhone(
            profiles,
            new DriverProfile { Transponder = "4440001", AlternateTransponder = "555" },
            editing: "4440001",
            RaceDayChannel.Relay,
            "555");

        Assert.NotNull(setAlt);
        Assert.DoesNotContain("7654321", setAlt);
        Assert.Contains("...321", setAlt);

        // What the phone typed is said back as it was typed.
        Assert.Contains("555", setAlt);

        // A new driver on that same alt, from the phone.
        var add = RaceDayService.ClashForPhone(
            profiles,
            new DriverProfile { Transponder = "555" },
            editing: null,
            RaceDayChannel.Relay,
            "555", string.Empty);

        Assert.NotNull(add);
        Assert.DoesNotContain("7654321", add);
    }

    [Fact]
    public void A_clash_told_at_the_desk_or_on_the_wifi_still_names_the_owners_whole_transponder()
    {
        var profiles = ProfileStore.Load();
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "7654321", AlternateTransponder = "555" }, out _));

        var candidate = new DriverProfile { Transponder = "555" };

        Assert.Contains("the profile for 7654321", profiles.DescribeClash(candidate, editing: null));
        Assert.Contains("the profile for 7654321", RaceDayService.ClashForPhone(profiles, candidate, null, RaceDayChannel.WiFi, "555"));
    }

    [Fact]
    public void A_bump_up_name_that_is_only_a_transponder_shows_its_last_three_over_the_relay()
    {
        var profiles = ProfileStore.Load();
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "7654321", AlternateTransponder = "8889990" }, out _));

        // Filed under the main transponder, under the alt (the spare car),
        // and a car with no profile at all - each named by its number.
        var onMain = new MainsBuilder.PlannedPromotion("7654321", 1, "7654321");
        var onAlt = new MainsBuilder.PlannedPromotion("7654321", 1, "8889990");
        var noProfile = new MainsBuilder.PlannedPromotion("1112223", 2, "1112223");

        Assert.Equal("...321", RaceDayService.ShownPromotionName(onMain, profiles, RaceDayChannel.Relay));
        Assert.Equal("...321", RaceDayService.ShownPromotionName(onAlt, profiles, RaceDayChannel.Relay));
        Assert.Equal("...223", RaceDayService.ShownPromotionName(noProfile, profiles, RaceDayChannel.Relay));
    }

    [Fact]
    public void A_bump_up_name_is_left_alone_on_the_wifi_and_for_a_driver_with_a_name()
    {
        var profiles = ProfileStore.Load();
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "7654321" }, out _));
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "4440001", DriverName = "Bea" }, out _));

        Assert.Equal("7654321", RaceDayService.ShownPromotionName(new MainsBuilder.PlannedPromotion("7654321", 1, "7654321"), profiles, RaceDayChannel.WiFi));
        Assert.Equal("Bea", RaceDayService.ShownPromotionName(new MainsBuilder.PlannedPromotion("4440001", 1, "Bea"), profiles, RaceDayChannel.Relay));
    }

    [Fact]
    public void The_relay_is_told_how_many_changes_can_be_undone_but_not_what_they_were()
    {
        var pending = new[] { "Transfer transponder 1234567 to Alex", "Phone: Add driver '7654321'" };

        // One line with only the count, for a relay page from before undoCount,
        // which shows the list as it comes and would otherwise say "Nothing to undo."
        Assert.Equal(new[] { "2 changes - undo them on the laptop" }, RaceDayService.UndoListFor(pending, RaceDayChannel.Relay));
        Assert.Equal(new[] { "1 change - undo it on the laptop" }, RaceDayService.UndoListFor(pending[..1], RaceDayChannel.Relay));
        Assert.Empty(RaceDayService.UndoListFor(Array.Empty<string>(), RaceDayChannel.Relay));

        Assert.Equal(pending, RaceDayService.UndoListFor(pending, RaceDayChannel.WiFi));
    }

    /// <summary>Windows-only: the laptop's Program tab is a real control.</summary>
    [Fact]
    public void Over_the_relay_the_undo_list_and_the_clash_messages_carry_no_whole_transponder() => OnUiThread(() =>
    {
        using var laptop = new Laptop();

        Assert.True(laptop.Profiles.TryAdd(new DriverProfile { Transponder = "7654321", AlternateTransponder = "555" }, out _));
        Assert.True(laptop.Profiles.TryAdd(new DriverProfile { Transponder = "4440001", DriverName = "Bea" }, out _));

        laptop.Undo.Capture("Transfer transponder 1234567 to Sam", () => { });

        var overRelay = laptop.Send("today.summary", null, RaceDayChannel.Relay);
        Assert.DoesNotContain("1234567", overRelay.GetRawText());
        Assert.Equal(1, overRelay.GetProperty("data").GetProperty("undoCount").GetInt32());
        Assert.Equal("1 change - undo it on the laptop", Assert.Single(overRelay.GetProperty("data").GetProperty("undo").EnumerateArray()).GetString());

        // The desk's own Wi-Fi still lists them.
        Assert.Contains("1234567", laptop.Send("today.summary", null, RaceDayChannel.WiFi).GetRawText());

        var setAlt = laptop.Send("driver.setAltTransponder", new { driverId = laptop.Phone.DriverIdFor("4440001"), altTransponder = "555" }, RaceDayChannel.Relay);
        Assert.False(setAlt.GetProperty("ok").GetBoolean());
        Assert.DoesNotContain("7654321", setAlt.GetProperty("error").GetString());

        var add = laptop.Send("driver.add", new { transponder = "555", name = "Cal" }, RaceDayChannel.Relay);
        Assert.False(add.GetProperty("ok").GetBoolean());
        Assert.DoesNotContain("7654321", add.GetProperty("error").GetString());
    });

    /// <summary>Windows-only: the laptop's Program tab is a real control.</summary>
    [Fact]
    public void Over_the_relay_the_bump_up_question_names_an_unnamed_car_by_its_last_three() => OnUiThread(() =>
    {
        using var laptop = new Laptop();

        laptop.Classes.Upsert(new RaceClass { Name = "Stock" });
        Assert.True(laptop.Profiles.TryAdd(new DriverProfile { Transponder = "7654321", ClassName = "Stock" }, out _));
        Assert.True(laptop.Profiles.TryAdd(new DriverProfile { Transponder = "S01", DriverName = "Ava", ClassName = "Stock" }, out _));

        // The B-main has run, won by the car nobody named; the A-main takes one.
        var feeder = new SessionRecord
        {
            SessionType = RaceSessionType.Race,
            Label = "Stock B-Main",
            Results = new List<SessionResultEntry>
            {
                new() { Position = 1, Transponder = "7654321", DriverName = "7654321", Laps = 5 },
            },
        };

        Assert.True(laptop.History.Add(feeder));

        laptop.Rounds.Add(new RoundDefinition
        {
            ClassName = "Stock", RoundLabel = "B-Main", Kind = RoundKind.Main, MainTier = 1,
            AssignedTransponders = new List<string> { "7654321" }, LinkedSessionId = feeder.Id,
        });

        laptop.Rounds.Add(new RoundDefinition
        {
            ClassName = "Stock", RoundLabel = "A-Main", Kind = RoundKind.Main, MainTier = 0, BumpUpCount = 1,
            AssignedTransponders = new List<string> { "S01" },
        });

        var relay = laptop.Send("program.bumpUps", new { className = "Stock" }, RaceDayChannel.Relay);
        Assert.True(relay.GetProperty("needsConfirm").GetBoolean(), relay.GetRawText());
        Assert.DoesNotContain("7654321", relay.GetProperty("message").GetString());
        Assert.Contains("...321", relay.GetProperty("message").GetString());

        // The same question on the Wi-Fi, as the desk asks it.
        var wifi = laptop.Send("program.bumpUps", new { className = "Stock" }, RaceDayChannel.WiFi);
        Assert.Contains("7654321", wifi.GetProperty("message").GetString());
    });

    // --- New Race Day from the phone -----------------------------------------------------

    [Fact]
    public void A_phone_day_carries_the_last_days_kind_classes_venue_track_championship_and_heats()
    {
        var events = RaceEventStore.Load();
        var classes = RaceClassStore.Load();
        classes.Upsert(new RaceClass { Name = "Stock" });
        classes.Upsert(new RaceClass { Name = "Mod" });

        var last = EventSpine.StartEvent(events, "Old", "Hall", "Oval", "S1", RaceDayShape.ClubNight, new[] { "Stock" }, 2, 6);
        events.Close(last.Id);

        var day = RaceDayService.StartDayLike(events, classes, "Tuesday", last);

        Assert.Equal(day.Id, events.Active?.Id);
        Assert.Equal("Tuesday", day.Name);
        Assert.Equal(RaceDayShape.ClubNight, day.Shape);
        Assert.False(day.CheckedInDriversOnly);
        Assert.Equal(new[] { "Stock" }, day.ClassNames);
        Assert.Equal(2, day.QualifyingRounds);
        Assert.Equal(6, day.DriversPerHeat);
        Assert.Equal("Hall", day.VenueName);
        Assert.Equal("Oval", day.TrackLayoutName);
        Assert.Equal("S1", day.ChampionshipId);
    }

    [Fact]
    public void A_phone_day_drops_a_class_deleted_since_and_takes_every_class_when_none_is_left()
    {
        var events = RaceEventStore.Load();
        var classes = RaceClassStore.Load();
        classes.Upsert(new RaceClass { Name = "Stock" });
        classes.Upsert(new RaceClass { Name = "Mod" });

        var mixed = EventSpine.StartEvent(events, "Old", classNames: new[] { "stock", "Retired" });
        events.Close(mixed.Id);

        // As the class spells itself, and only while it is still on file.
        Assert.Equal(new[] { "Stock" }, RaceDayService.StartDayLike(events, classes, "Next", mixed).ClassNames);

        // A club's first day, from the phone: every class, as New Race Day ticks them all.
        var first = RaceDayService.StartDayLike(events, classes, "First", previous: null);
        Assert.Equal(new[] { "Mod", "Stock" }, first.ClassNames.OrderBy(n => n));
        Assert.Equal(RaceDayShape.RaceDay, first.Shape);
        Assert.True(first.CheckedInDriversOnly);
    }

    [Fact]
    public void A_repeated_new_race_day_finds_a_club_night_by_its_default_name()
    {
        var started = DateTime.UtcNow;
        var local = started.ToLocalTime();

        var clubNight = new RaceEvent { Name = EventSpine.DefaultDayName(local, RaceDayShape.ClubNight), Shape = RaceDayShape.ClubNight, StartedUtc = started };

        Assert.True(RaceDayService.NamedAsAsked(clubNight, string.Empty));

        // Switched to a race day at the desk since, keeping its name: still the same day.
        clubNight.Shape = RaceDayShape.RaceDay;
        Assert.True(RaceDayService.NamedAsAsked(clubNight, string.Empty));

        var renamed = new RaceEvent { Name = "Winter Series 3", StartedUtc = started };
        Assert.False(RaceDayService.NamedAsAsked(renamed, string.Empty));
        Assert.True(RaceDayService.NamedAsAsked(renamed, "Winter Series 3"));
    }

    /// <summary>Windows-only: the laptop's Program tab is a real control.</summary>
    [Fact]
    public void Day_create_from_the_phone_starts_a_club_night_like_the_last_and_a_repeat_changes_nothing() => OnUiThread(() =>
    {
        using var laptop = new Laptop();

        laptop.Classes.Upsert(new RaceClass { Name = "Stock" });
        laptop.Classes.Upsert(new RaceClass { Name = "Mod" });

        var last = EventSpine.StartEvent(laptop.Events, "Old", "Hall", "Oval", "S1", RaceDayShape.ClubNight, new[] { "Stock" }, 2, 0);
        laptop.Events.Close(last.Id);

        var first = laptop.Send("day.create", new { }, RaceDayChannel.WiFi);
        Assert.True(first.GetProperty("ok").GetBoolean(), first.GetRawText());
        Assert.True(first.GetProperty("data").GetProperty("changed").GetBoolean());

        var day = laptop.Events.Active!;
        Assert.Equal("S1", day.ChampionshipId);
        Assert.Equal(RaceDayShape.ClubNight, day.Shape);
        Assert.Equal(new[] { "Stock" }, day.ClassNames);
        Assert.Equal(2, day.QualifyingRounds);
        Assert.Equal("Hall", day.VenueName);
        Assert.Equal("Oval", day.TrackLayoutName);
        Assert.False(day.CheckedInDriversOnly);
        Assert.StartsWith("Club Night", day.Name);

        var again = laptop.Send("day.create", new { }, RaceDayChannel.WiFi);
        Assert.False(again.GetProperty("data").GetProperty("changed").GetBoolean());
        Assert.Equal(day.Id, laptop.Events.Active!.Id);
    });

    // --- Set Up Tonight ------------------------------------------------------------------

    [Fact]
    public void Switching_to_a_club_night_rewords_the_name_set_up_tonight_suggested()
    {
        var date = new DateTime(2026, 10, 4, 18, 0, 0);
        var suggested = EventSpine.DefaultDayName(date, RaceDayShape.RaceDay);

        var renamed = RaceDaySetupWizard.SuggestedNameAfterShapeChange(suggested, suggested, RaceDayShape.ClubNight, date);

        Assert.Equal(EventSpine.DefaultDayName(date, RaceDayShape.ClubNight), renamed);
        Assert.StartsWith("Club Night", renamed);

        // And back again.
        Assert.Equal(suggested, RaceDaySetupWizard.SuggestedNameAfterShapeChange(renamed!, renamed, RaceDayShape.RaceDay, date));
    }

    [Fact]
    public void A_name_typed_in_set_up_tonight_is_kept_when_the_kind_of_night_changes()
    {
        var date = new DateTime(2026, 10, 4, 18, 0, 0);
        var suggested = EventSpine.DefaultDayName(date, RaceDayShape.RaceDay);

        Assert.Null(RaceDaySetupWizard.SuggestedNameAfterShapeChange("Winter Series 3", suggested, RaceDayShape.ClubNight, date));

        // Nothing suggested: a day is open, and its name is its own.
        Assert.Null(RaceDaySetupWizard.SuggestedNameAfterShapeChange(suggested, null, RaceDayShape.ClubNight, date));
    }

    /// <summary>Windows-only: builds the wizard's window.</summary>
    [Fact]
    public void Set_Up_Tonight_opens_on_last_times_kind_of_night_and_names_it_to_match() => OnUiThread(() =>
    {
        var events = RaceEventStore.Load();
        var last = EventSpine.StartEvent(events, "Last week", shape: RaceDayShape.ClubNight);
        events.Close(last.Id);
        events.SetActive(null);

        using var wizard = Wizard(events, RaceDaySetupWizard.StepDay, out _);

        var shape = Field<ComboBox>(wizard, "_cmbShape");
        var name = Field<TextBox>(wizard, "_txtDayName");

        Assert.Equal(1, shape.SelectedIndex);
        Assert.StartsWith("Club Night", name.Text);

        // Picked by hand: the suggestion follows.
        shape.SelectedIndex = 0;
        Assert.StartsWith("Race Day", name.Text);

        // Typed: kept.
        name.Text = "Winter Series 3";
        shape.SelectedIndex = 1;
        Assert.Equal("Winter Series 3", name.Text);
    });

    /// <summary>Windows-only: builds the wizard's window.</summary>
    [Fact]
    public void Clear_All_in_set_up_tonight_goes_through_the_desks_timed_check_in() => OnUiThread(() =>
    {
        var events = RaceEventStore.Load();
        var day = EventSpine.StartEvent(events, "Tonight");

        var checkIns = CheckInStore.Load();
        checkIns.CurrentEventId = day.Id;

        var profiles = ProfileStore.Load();
        profiles.UpsertMany(new[]
        {
            new DriverProfile { Transponder = "S01", DriverName = "Ava" },
            new DriverProfile { Transponder = "S02", DriverName = "Ben" },
        });

        checkIns.CheckInAll(new[] { "S01", "S02" });

        using var wizard = Wizard(events, RaceDaySetupWizard.StepDrivers, out var timed);

        typeof(RaceDaySetupWizard).GetMethod("SetAllShown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(wizard, new object[] { false });

        Assert.Contains(("S01", false), timed);
        Assert.Contains(("S02", false), timed);
    });

    /// <summary>Windows-only: builds the wizard's window.</summary>
    [Fact]
    public void With_no_day_open_the_classes_step_takes_no_ticks_or_numbers() => OnUiThread(() =>
    {
        var events = RaceEventStore.Load();
        RaceClassStore.Load().Upsert(new RaceClass { Name = "Stock" });
        events.SetActive(null);

        using (var closed = Wizard(events, RaceDaySetupWizard.StepClasses, out _))
        {
            Assert.False(Field<CheckedListBox>(closed, "_lstClasses").Enabled);
            Assert.False(Field<NumericUpDown>(closed, "_numRounds").Enabled);
            Assert.False(Field<NumericUpDown>(closed, "_numHeatSize").Enabled);
            Assert.False(Field<CheckBox>(closed, "_chkFirstRace").Enabled);

            // Adding a class is the class store's, and still works.
            Assert.True(Field<Button>(closed, "_btnAddClass").Enabled);
        }

        EventSpine.StartEvent(events, "Tonight");

        using var open = Wizard(events, RaceDaySetupWizard.StepClasses, out _);
        Assert.True(Field<CheckedListBox>(open, "_lstClasses").Enabled);
        Assert.True(Field<NumericUpDown>(open, "_numRounds").Enabled);
    });

    // --- Helpers -------------------------------------------------------------------------

    /// <summary>Set Up Tonight at a step, over the stores in the test's folder. Starts and changes nothing; check-ins it times are listed.</summary>
    private static RaceDaySetupWizard Wizard(RaceEventStore events, int step, out List<(string Transponder, bool Here)> timed)
    {
        var calls = new List<(string Transponder, bool Here)>();
        timed = calls;

        var checkIns = CheckInStore.Load();
        checkIns.CurrentEventId = events.CurrentId;

        return new RaceDaySetupWizard(
            AppSettings.Load(), RaceClassStore.Load(), ProfileStore.Load(), TrackLayoutStore.Load(), VenueStore.Load(), events, checkIns,
            new RaceDaySetupHost(
                StartDay: (_, _) => false,
                ChangeDay: _ => { },
                SetClassesRacing: _ => { },
                PlanBuild: () => new ProgramAnswer(false, string.Empty, string.Empty),
                Build: () => new ProgramAnswer(false, string.Empty, string.Empty),
                SetCheckedIn: (transponder, here) =>
                {
                    calls.Add((transponder, here));
                    checkIns.SetCheckedIn(transponder, here);
                }),
            step);
    }

    // --- A driver seated under his alternate transponder ---------------------------------

    /// <summary>
    /// Windows-only: the laptop's Program tab is a real control. A heat can
    /// hold a driver's alternate transponder; program.heat sends him by his
    /// main one's id, and program.moveDriver now finds him by it
    /// (2026-11-06: "Bob isn't in Heat 1", and he was offered as available).
    /// </summary>
    [Fact]
    public void A_driver_seated_under_his_alternate_can_be_taken_out_of_the_heat_from_the_phone() => OnUiThread(() =>
    {
        using var laptop = new Laptop();

        laptop.Classes.Upsert(new RaceClass { Name = "Stock" });
        Assert.True(laptop.Profiles.TryAdd(new DriverProfile { Transponder = "MAIN123", AlternateTransponder = "ALT555", DriverName = "Bob", ClassName = "Stock" }, out _));
        Assert.True(laptop.Profiles.TryAdd(new DriverProfile { Transponder = "S01", DriverName = "Ava", ClassName = "Stock" }, out _));

        var heat = new RoundDefinition
        {
            ClassName = "Stock", RoundLabel = "Heat 1",
            AssignedTransponders = new List<string> { "ALT555", "S01" },
        };

        laptop.Rounds.Add(heat);

        var shown = laptop.Send("program.heat", new { roundId = heat.Id }, RaceDayChannel.WiFi);
        Assert.True(shown.GetProperty("ok").GetBoolean(), shown.GetRawText());

        var data = shown.GetProperty("data");
        var bob = data.GetProperty("drivers").EnumerateArray().Single(d => d.GetProperty("name").GetString() == "Bob");
        var bobId = bob.GetProperty("id").GetString();

        Assert.DoesNotContain(data.GetProperty("available").EnumerateArray(), d => d.GetProperty("id").GetString() == bobId);

        var moved = laptop.Send("program.moveDriver", new { fromRoundId = heat.Id, driverId = bobId }, RaceDayChannel.WiFi);
        Assert.True(moved.GetProperty("ok").GetBoolean(), moved.GetRawText());

        Assert.Equal(new[] { "S01" }, laptop.Rounds.All.Single(r => r.Id == heat.Id).AssignedTransponders);
    });

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static void OnUiThread(Action body) =>
        StaThread.Run(() =>
        {
            body();
            return true;
        }, TimeSpan.FromMinutes(1), () => "bug hunt 4, race day");

    /// <summary>
    /// The race-day manager on the laptop's own stores, put together the way
    /// RaceNightTests' laptop is (and MainForm.WireRaceDayService) - with the
    /// channel each request comes in on chosen by the test.
    /// </summary>
    private sealed class Laptop : IDisposable
    {
        public readonly AppSettings Settings = new() { AllowResultEditing = true };
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
        public readonly RaceControlSession Race = new();

        public readonly RaceProgramControl Program;
        public readonly RaceDayService Phone;

        private readonly Control _window = new();

        public Laptop()
        {
            EventSpine.Bind(Events, CheckIns, Tech, Schedule, Rounds);

            _ = _window.Handle;

            Program = new RaceProgramControl(Classes, Profiles, Rounds, Tracks, History, CheckIns, Settings, Undo, Events, Schedule);

            Phone = new RaceDayService(new RaceDayServiceHost
            {
                UiThread = _window,
                Settings = Settings,
                Undo = Undo,
                Race = new RaceTiming(Race),
                Events = Events,
                Classes = Classes,
                Profiles = Profiles,
                Rounds = Rounds,
                History = History,
                CheckIns = CheckIns,
                Tech = Tech,
                Schedule = Schedule,
                Program = Program,
                RaceControl = null!,
                ActiveRound = () => null,
                StartRound = (_, _, _) => "Not from here.",
                SetCheckedIn = (transponder, wanted) =>
                {
                    if (CheckIns.IsCheckedIn(transponder) == wanted)
                    {
                        return false;
                    }

                    CheckIns.SetCheckedIn(transponder, wanted);
                    return true;
                },
                RepublishSession = _ => { },
                RefreshDesk = () => Program.RefreshAll(),
                Log = _ => { },
            });
        }

        public void Dispose()
        {
            Program.Dispose();
            _window.Dispose();
        }

        /// <summary>One request on a channel, on the window's own thread, and its answer - unconfirmed.</summary>
        public JsonElement Send(string op, object? args, RaceDayChannel channel)
        {
            var request = new Dictionary<string, object?> { ["op"] = op, ["args"] = args ?? new { } };

            var json = Phone.HandleJsonAsync(JsonSerializer.Serialize(request), channel, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
    }
}
