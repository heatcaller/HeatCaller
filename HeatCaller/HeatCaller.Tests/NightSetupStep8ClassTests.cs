namespace HeatCaller.Tests;

/// <summary>
/// Setup plan step 8 (12 Nov 2026), the classes half: a new class is asked
/// how long its races are and how they finish, and a class can have its own
/// fee, which the desk, the closing list and the event report all use.
/// </summary>
public sealed class NightSetupStep8ClassTests
{
    // --- How a class's race finishes -----------------------------------------

    [Fact]
    public void A_new_class_window_suggests_five_minutes_then_finish_the_lap_on_a_new_install()
    {
        var newInstall = new RaceFormat();

        Assert.Equal(RaceFormat.NewClassMinutes, RaceFormat.SuggestedMinutes(newInstall));
        Assert.True(RaceFormat.SuggestedFinishTheLap(newInstall));
        Assert.True(RaceFormat.SuggestedFinishTheLap(null));
    }

    [Fact]
    public void The_suggestion_follows_an_app_default_that_has_a_time_and_a_finish_rule()
    {
        var onRoad = new RaceFormat { TimeLimitMinutes = 8, FinishOnNextCrossing = false };

        Assert.Equal(8, RaceFormat.SuggestedMinutes(onRoad));
        Assert.False(RaceFormat.SuggestedFinishTheLap(onRoad));
    }

    [Fact]
    public void Left_as_suggested_a_new_class_gets_exactly_what_it_always_did()
    {
        // No limit on the default: the 5-minute class, as ForNewClass gives.
        var format = RaceFormat.ForNewClass(new RaceFormat(), RaceFormat.NewClassMinutes, finishTheLap: true);
        Assert.Equal(RaceFormat.NewClassMinutes, format!.TimeLimitMinutes);
        Assert.True(format.FinishOnNextCrossing);

        // A default with a limit - including a lap count - is followed.
        Assert.Null(RaceFormat.ForNewClass(new RaceFormat { TimeLimitMinutes = 6 }, 6, finishTheLap: true));
        Assert.Null(RaceFormat.ForNewClass(new RaceFormat { LapLimit = 20 }, RaceFormat.NewClassMinutes, finishTheLap: true));
    }

    [Fact]
    public void Stop_when_times_up_gives_the_class_its_own_format_that_stops_at_the_time()
    {
        var appDefault = new RaceFormat { TimeLimitMinutes = 6, StagingSeconds = 20 };

        var format = RaceFormat.ForNewClass(appDefault, 6, finishTheLap: false);

        Assert.NotNull(format);
        Assert.False(format!.FinishOnNextCrossing);
        Assert.Equal(6, format.TimeLimitMinutes);
        Assert.Equal(20, format.StagingSeconds);

        // The app's default is left alone.
        Assert.True(appDefault.FinishOnNextCrossing);
    }

    [Fact]
    public void A_different_length_is_raced_to_that_time_with_no_lap_limit()
    {
        var format = RaceFormat.ForNewClass(new RaceFormat { LapLimit = 20 }, 8, finishTheLap: true);

        Assert.Equal(8, format!.TimeLimitMinutes);
        Assert.Equal(0, format.LapLimit);
        Assert.True(format.FinishOnNextCrossing);
    }

    [Fact]
    public void The_finish_rule_is_said_in_plain_words()
    {
        Assert.Equal("Time's up, then finish the lap", RaceFormat.FinishWords(true));
        Assert.Equal("Stop when time's up", RaceFormat.FinishWords(false));
        Assert.Equal("  ·  then finish the lap", RaceClassesControl.FinishNote(new RaceFormat { TimeLimitMinutes = 5 }));
        Assert.Equal("  ·  stop at the time", RaceClassesControl.FinishNote(new RaceFormat { TimeLimitMinutes = 5, FinishOnNextCrossing = false }));
        Assert.Equal(string.Empty, RaceClassesControl.FinishNote(new RaceFormat { LapLimit = 20 }));
    }

    [Fact]
    public void A_club_racing_to_laps_is_told_what_leaving_the_length_alone_does()
    {
        Assert.Contains("20 laps", ClassLengthPicker.HintFor(new RaceFormat { LapLimit = 20 }));
        Assert.DoesNotContain("laps;", ClassLengthPicker.HintFor(new RaceFormat()));
    }

    // --- A fee per class ------------------------------------------------------

    [Fact]
    public void A_class_without_its_own_fee_charges_the_race_day_s()
    {
        var fee = DeskMoney.FeeOf(new RaceClass { Name = "Stock" }, 15m, 10m);

        Assert.Equal(new DeskMoney.ClassFee(15m, 10m), fee);
        Assert.Equal(new DeskMoney.ClassFee(15m, 10m), DeskMoney.FeeOf(null, 15m, 10m));
    }

    [Fact]
    public void A_cheap_class_is_never_dearer_as_an_extra_than_on_its_own()
    {
        var juniors = new RaceClass { Name = "Juniors", EntryFee = 5m };

        Assert.Equal(new DeskMoney.ClassFee(5m, 5m), DeskMoney.FeeOf(juniors, 15m, 10m));

        // Its own extra fee wins outright.
        juniors.ExtraClassFee = 3m;
        Assert.Equal(new DeskMoney.ClassFee(5m, 3m), DeskMoney.FeeOf(juniors, 15m, 10m));
    }

    [Fact]
    public void A_driver_pays_their_dearest_class_then_each_other_as_an_extra()
    {
        var fees = new[] { new DeskMoney.ClassFee(15m, 10m), new DeskMoney.ClassFee(20m, 10m), new DeskMoney.ClassFee(5m, 5m) };

        // Mod $20 is the entry; Stock and Juniors are extras at $10 and $5.
        Assert.Equal(35m, DeskMoney.FeeFor(fees));
        Assert.Equal(0m, DeskMoney.FeeFor(Array.Empty<DeskMoney.ClassFee>()));
    }

    [Fact]
    public void With_no_class_fees_it_is_the_old_rule_exactly()
    {
        foreach (var classes in new[] { 1, 2, 3 })
        {
            var same = Enumerable.Repeat(new DeskMoney.ClassFee(15m, 10m), classes);
            Assert.Equal(DeskMoney.FeeFor(classes, 15m, 10m), DeskMoney.FeeFor(same));
        }

        Assert.Equal(0m, DeskMoney.FeeFor(new[] { new DeskMoney.ClassFee(0m, 10m), new DeskMoney.ClassFee(0m, 10m) }));
    }

    [Fact]
    public void The_desk_charges_each_class_its_own_fee()
    {
        var classes = DeskMoney.ClassLookup(new[]
        {
            new RaceClass { Name = "Juniors", EntryFee = 5m },
            new RaceClass { Name = "Mod", EntryFee = 20m },
            new RaceClass { Name = "Stock" },
        });

        var day = new RaceEvent { EntryFee = 15m, ExtraClassFee = 10m, ClassNames = new List<string> { "Juniors", "Mod", "Stock" } };

        var junior = new DriverProfile { Transponder = "T1", ClassName = "Juniors" };
        var twoClasses = new DriverProfile { Transponder = "T2", ClassName = "Stock", Classes = new List<string> { "Mod" } };
        var stock = new DriverProfile { Transponder = "T3", ClassName = "Stock" };

        Assert.Equal(5m, CheckInControl.FeeFor(junior, day, classes));
        Assert.Equal(30m, CheckInControl.FeeFor(twoClasses, day, classes));
        Assert.Equal(15m, CheckInControl.FeeFor(stock, day, classes));

        // Without the classes, the day's fees alone - as before.
        Assert.Equal(15m, CheckInControl.FeeFor(junior, day));
    }

    [Fact]
    public void With_no_classes_ticked_the_driver_pays_for_their_main_class()
    {
        var classes = DeskMoney.ClassLookup(new[] { new RaceClass { Name = "Juniors", EntryFee = 5m } });
        var day = new RaceEvent { EntryFee = 15m, ExtraClassFee = 10m };

        Assert.Equal(5m, CheckInControl.FeeFor(new DriverProfile { Transponder = "T1", ClassName = "Juniors" }, day, classes));
        Assert.Equal(15m, CheckInControl.FeeFor(new DriverProfile { Transponder = "T2", ClassName = "Stock" }, day, classes));
    }

    [Fact]
    public void A_night_that_prices_only_its_classes_still_charges()
    {
        var classes = DeskMoney.ClassLookup(new[] { new RaceClass { Name = "Mod", EntryFee = 20m }, new RaceClass { Name = "Stock" } });

        Assert.True(DeskMoney.Charges(new RaceEvent { ClassNames = new List<string> { "Mod" } }, classes));
        Assert.False(DeskMoney.Charges(new RaceEvent { ClassNames = new List<string> { "Stock" } }, classes));
        Assert.True(DeskMoney.Charges(new RaceEvent { EntryFee = 10m }, classes));
        Assert.False(DeskMoney.Charges(null, classes));
    }

    [Fact]
    public void The_report_line_says_what_each_class_charged()
    {
        var classes = DeskMoney.ClassLookup(new[]
        {
            new RaceClass { Name = "Juniors", EntryFee = 5m },
            new RaceClass { Name = "Mod", EntryFee = 20m, ExtraClassFee = 12m },
            new RaceClass { Name = "Stock" },
        });

        var day = new RaceEvent { EntryFee = 15m, ExtraClassFee = 10m, ClassNames = new List<string> { "Juniors", "Mod", "Stock" } };
        var line = DeskMoney.FeesLine(day, classes)!;

        Assert.Contains($"Juniors {DeskMoney.Money(5m)}", line);
        Assert.Contains($"Mod {DeskMoney.Money(20m)}, {DeskMoney.Money(12m)} as an extra class", line);
        Assert.Contains($"the rest {DeskMoney.Money(15m)}", line);

        Assert.Null(DeskMoney.FeesLine(new RaceEvent { EntryFee = 15m, ClassNames = new List<string> { "Stock" } }, classes));
    }

    [Fact]
    public void A_class_s_fees_survive_a_save_and_load()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new RaceClass { Name = "Juniors", EntryFee = 5m });
        var back = System.Text.Json.JsonSerializer.Deserialize<RaceClass>(json)!;

        Assert.Equal(5m, back.EntryFee);
        Assert.Null(back.ExtraClassFee);

        // A class saved before this existed has neither, so it charges the day's.
        var old = System.Text.Json.JsonSerializer.Deserialize<RaceClass>("{\"Name\":\"Stock\"}")!;
        Assert.Null(old.EntryFee);
        Assert.Equal(string.Empty, DeskMoney.ClassFeeWords(old));
    }
}
