namespace HeatCaller.Tests;

/// <summary>
/// Setting up the night, step 2 (12 Nov 2026, setup plan A1-A5): a new race
/// day keeps last week's settings, a driver's classes change at the desk, a
/// new class has a race length, and a new install's empty day doesn't stop
/// the wizard being offered.
/// </summary>
public sealed class NightSetupQuickWinsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- A1: a new day keeps last week's settings ----------------------------

    [Fact]
    public void A_new_day_keeps_last_week_s_times_fees_seeding_and_talk()
    {
        var store = RaceEventStore.Load();
        var lastWeek = EventSpine.StartEvent(store, "Last week", shape: RaceDayShape.RaceDay);
        lastWeek.FirstRaceLocalTime = TimeSpan.FromHours(19);
        lastWeek.EntryFee = 15m;
        lastWeek.ExtraClassFee = 5m;
        lastWeek.FirstRoundSeeding = FirstRoundSeeding.Random;
        lastWeek.ShareHeatsUnder = 4;
        lastWeek.BetweenRacesPreset = "big";
        lastWeek.CheckedInDriversOnly = false;
        store.Upsert(lastWeek);

        var tonight = EventSpine.StartEvent(store, "Tonight", shape: RaceDayShape.RaceDay);

        Assert.Equal(TimeSpan.FromHours(19), tonight.FirstRaceLocalTime);
        Assert.Equal(15m, tonight.EntryFee);
        Assert.Equal(5m, tonight.ExtraClassFee);
        Assert.Equal(FirstRoundSeeding.Random, tonight.FirstRoundSeeding);
        Assert.Equal(4, tonight.ShareHeatsUnder);
        Assert.Equal("big", tonight.BetweenRacesPreset);

        // The club's own choice, not the kind of night's.
        Assert.False(tonight.CheckedInDriversOnly);
    }

    [Fact]
    public void A_club_s_first_day_of_a_kind_gets_the_defaults_but_keeps_the_fees()
    {
        var store = RaceEventStore.Load();
        var raceDay = EventSpine.StartEvent(store, "Race day", shape: RaceDayShape.RaceDay);
        raceDay.FirstRaceLocalTime = TimeSpan.FromHours(10);
        raceDay.EntryFee = 20m;
        raceDay.FirstRoundSeeding = FirstRoundSeeding.Random;
        store.Upsert(raceDay);

        var clubNight = EventSpine.StartEvent(store, "Club night", shape: RaceDayShape.ClubNight);

        Assert.Null(clubNight.FirstRaceLocalTime);
        Assert.Equal(FirstRoundSeeding.Pace, clubNight.FirstRoundSeeding);
        Assert.False(clubNight.CheckedInDriversOnly);
        Assert.Equal(20m, clubNight.EntryFee);
    }

    [Fact]
    public void The_very_first_day_has_the_defaults()
    {
        var store = RaceEventStore.Load();

        var first = EventSpine.StartEvent(store, "First", shape: RaceDayShape.RaceDay);

        Assert.True(first.CheckedInDriversOnly);
        Assert.Null(first.FirstRaceLocalTime);
        Assert.Equal(0m, first.EntryFee);
    }

    // --- A3: a driver's classes at the desk ----------------------------------

    [Fact]
    public void Ticking_a_class_adds_it_beside_the_main_one()
    {
        var driver = new DriverProfile { Transponder = "1", DriverName = "Max Power", ClassName = "Stock" };

        CheckInControl.SetRunsClass(driver, "Mod", runs: true);

        Assert.Equal("Stock", driver.ClassName);
        Assert.Equal(new[] { "Stock", "Mod" }, driver.AllClasses);
        Assert.Equal("Stock + Mod", CheckInControl.ClassCellText(driver));

        // Twice is still once.
        CheckInControl.SetRunsClass(driver, "mod", runs: true);
        Assert.Equal(2, driver.AllClasses.Count);
    }

    [Fact]
    public void A_driver_with_no_class_gets_it_as_their_main_class()
    {
        var driver = new DriverProfile { Transponder = "2" };

        CheckInControl.SetRunsClass(driver, "Stock", runs: true);

        Assert.Equal("Stock", driver.ClassName);
        Assert.Empty(driver.Classes);
    }

    [Fact]
    public void Taking_off_the_main_class_makes_the_next_one_the_main()
    {
        var driver = new DriverProfile { Transponder = "3", ClassName = "Stock", Classes = { "Mod" } };

        CheckInControl.SetRunsClass(driver, "Stock", runs: false);

        Assert.Equal("Mod", driver.ClassName);
        Assert.Empty(driver.Classes);

        CheckInControl.SetRunsClass(driver, "Mod", runs: false);
        Assert.Equal(string.Empty, driver.ClassName);
        Assert.Empty(driver.AllClasses);
    }

    [Fact]
    public void The_menu_offers_tonight_s_classes_and_the_driver_s_own()
    {
        var driver = new DriverProfile { Transponder = "4", ClassName = "Truck" };

        Assert.Equal(new[] { "Stock", "Mod", "Truck" },
            CheckInControl.ClassChoices(driver, new[] { "Stock", "Mod" }, new[] { "Stock", "Mod", "Truck", "Buggy" }));

        // A day with none picked offers every class on file.
        Assert.Equal(new[] { "Stock", "Mod", "Truck", "Buggy" },
            CheckInControl.ClassChoices(driver, Array.Empty<string>(), new[] { "Stock", "Mod", "Truck", "Buggy" }));
    }

    // --- A5: a new class has a race length -----------------------------------

    [Fact]
    public void A_new_class_gets_five_minutes_while_the_app_default_has_no_limit()
    {
        var noLimit = new RaceFormat { StagingSeconds = 20 };

        var format = RaceFormat.ForNewClass(noLimit);

        Assert.NotNull(format);
        Assert.Equal(RaceFormat.NewClassMinutes, format!.TimeLimitMinutes);
        Assert.Equal(20, format.StagingSeconds);

        // A copy: the app's default is left as it was.
        Assert.Equal(0, noLimit.TimeLimitMinutes);

        Assert.Equal(RaceFormat.NewClassMinutes, RaceFormat.ForNewClass(null)!.TimeLimitMinutes);
    }

    [Fact]
    public void A_new_class_follows_an_app_default_that_has_a_limit()
    {
        Assert.Null(RaceFormat.ForNewClass(new RaceFormat { TimeLimitMinutes = 6 }));
        Assert.Null(RaceFormat.ForNewClass(new RaceFormat { LapLimit = 20 }));
    }

    // --- A4: the empty day a new install opens --------------------------------

    [Fact]
    public void A_day_with_no_classes_and_no_rounds_is_empty()
    {
        Assert.True(MainForm.IsEmptyDay(new RaceEvent { Name = "Race Day 1 Nov" }));
        Assert.False(MainForm.IsEmptyDay(new RaceEvent { Name = "Tonight", ClassNames = { "Stock" } }));
        Assert.False(MainForm.IsEmptyDay(new RaceEvent { Name = "Tonight", RoundIds = { "r1" } }));
    }
}
