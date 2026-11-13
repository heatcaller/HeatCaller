namespace HeatCaller.Tests;

/// <summary>
/// 12 Nov 2026, setup plan step 8: the ROAR, EFRA, NZRCA and Reedy Race
/// presets. Each one must set EXACTLY the figures its rulebook states and
/// leave every other setting on the class as the club had it - the rule the
/// whole of SanctioningPreset.cs is built on. So every test starts from a
/// class whose settings are all deliberately odd values no rulebook would
/// give, and checks the untouched ones are still those odd values after.
/// </summary>
public sealed class SanctioningPresetTests
{
    /// <summary>A club's class with a format of its own, every setting a value no preset sets.</summary>
    private static RaceClass ClubClass() => new()
    {
        Name = "Stock Buggy",
        QualifyingMethod = QualifyingMethod.BestConsecutiveLaps,
        ConsecutiveLapCount = 5,
        CountBestRounds = 3,
        MainSize = 7,
        BumpUpCount = 4,
        AMainLegs = 1,
        AMainLegsCounted = 0,
        Format = new RaceFormat
        {
            TimeLimitMinutes = 11,
            LapLimit = 40,
            MinLapTimeMs = 4321,
            StreakWindowPercent = 17,
            StartType = RaceStartType.HandReleased,
            StaggerGapSeconds = 7,
            StaggerClockMode = StaggerClock.SharedGreen,
            WarmUpSeconds = 90,
        },
    };

    private static SanctioningPreset Preset(string key) =>
        SanctioningPresets.Find(key) ?? throw new InvalidOperationException($"No preset {key}");

    private static RaceClass Applied(string key)
    {
        var raceClass = ClubClass();
        Preset(key).ApplyTo(raceClass, new RaceFormat());
        return raceClass;
    }

    /// <summary>What no preset may touch on a format: the club's own lap filter, streak window and warm-up.</summary>
    private static void AssertClubFormatKept(RaceFormat format)
    {
        Assert.Equal(4321, format.MinLapTimeMs);
        Assert.Equal(17, format.StreakWindowPercent);
        Assert.Equal(90, format.WarmUpSeconds);
        Assert.Equal(7, format.StaggerGapSeconds);
    }

    [Theory]
    [InlineData("roar-electric-10", 5)]
    [InlineData("roar-electric-12", 8)]
    public void Roar_sets_the_length_full_field_mains_and_single_best_heat_and_nothing_else(string key, int minutes)
    {
        var raceClass = Applied(key);

        // 8.1.4: the length, qualifiers and mains alike.
        Assert.Equal(minutes, raceClass.QualifierFormat!.TimeLimitMinutes);
        Assert.Equal(0, raceClass.QualifierFormat.LapLimit);
        Assert.Equal(minutes, raceClass.Format!.TimeLimitMinutes);
        Assert.Equal(0, raceClass.Format.LapLimit);

        // 8.9.1: mains full-field; the qualifier start is either, so the club's stays.
        Assert.Equal(RaceStartType.Standard, raceClass.Format.StartType);
        Assert.Equal(RaceStartType.HandReleased, raceClass.QualifierFormat.StartType);

        // 8.8.3 / 8.8.4: laps then time, single fastest heat.
        Assert.Equal(QualifyingMethod.TotalLaps, raceClass.QualifyingMethod);
        Assert.Equal(1, raceClass.CountBestRounds);

        // Not in the rulebook for Level 1-4 electric.
        Assert.Equal(5, raceClass.ConsecutiveLapCount);
        Assert.Equal(7, raceClass.MainSize);
        Assert.Equal(4, raceClass.BumpUpCount);
        Assert.Equal(1, raceClass.AMainLegs);
        Assert.Equal(0, raceClass.AMainLegsCounted);
        Assert.Equal(StaggerClock.SharedGreen, raceClass.Format.StaggerClockMode);
        AssertClubFormatKept(raceClass.Format);
        AssertClubFormatKept(raceClass.QualifierFormat);
    }

    [Theory]
    [InlineData("efra-electric-10", 5)]
    [InlineData("efra-electric-12", 8)]
    public void Efra_sets_lengths_grid_finals_of_ten_and_three_legs_but_not_its_points(string key, int minutes)
    {
        var raceClass = Applied(key);

        Assert.Equal(minutes, raceClass.QualifierFormat!.TimeLimitMinutes);
        Assert.Equal(minutes, raceClass.Format!.TimeLimitMinutes);
        Assert.Equal(RaceStartType.Standard, raceClass.Format.StartType);
        Assert.Equal(10, raceClass.MainSize);
        Assert.Equal(3, raceClass.AMainLegs);
        Assert.Equal(2, raceClass.AMainLegsCounted);

        // Round by Round gives the TQ zero points - not the app's scale - so
        // qualifying stays the club's, and so do the qualifier start and bump-ups.
        Assert.Equal(QualifyingMethod.BestConsecutiveLaps, raceClass.QualifyingMethod);
        Assert.Equal(3, raceClass.CountBestRounds);
        Assert.Equal(5, raceClass.ConsecutiveLapCount);
        Assert.Equal(RaceStartType.HandReleased, raceClass.QualifierFormat.StartType);
        Assert.Equal(4, raceClass.BumpUpCount);
        AssertClubFormatKept(raceClass.Format);
        AssertClubFormatKept(raceClass.QualifierFormat);
    }

    [Theory]
    [InlineData("nzrca-ep-offroad-10")]
    [InlineData("nzrca-ep-onroad-5min")]
    public void Nzrca_sets_own_clock_staggered_qualifiers_grid_finals_of_twelve_and_three_legs(string key)
    {
        var raceClass = Applied(key);

        Assert.Equal(5, raceClass.QualifierFormat!.TimeLimitMinutes);
        Assert.Equal(RaceStartType.StaggeredGrid, raceClass.QualifierFormat.StartType);
        Assert.Equal(StaggerClock.OwnRelease, raceClass.QualifierFormat.StaggerClockMode);
        Assert.Equal(5, raceClass.Format!.TimeLimitMinutes);
        Assert.Equal(RaceStartType.Standard, raceClass.Format.StartType);
        Assert.Equal(12, raceClass.MainSize);
        Assert.Equal(3, raceClass.AMainLegs);
        Assert.Equal(2, raceClass.AMainLegsCounted);

        // The gap is the race director's (kept at the club's 7 s by AssertClubFormatKept),
        // and qualifying scoring and bump-ups are not stated.
        Assert.Equal(QualifyingMethod.BestConsecutiveLaps, raceClass.QualifyingMethod);
        Assert.Equal(3, raceClass.CountBestRounds);
        Assert.Equal(4, raceClass.BumpUpCount);
        AssertClubFormatKept(raceClass.Format);
        AssertClubFormatKept(raceClass.QualifierFormat);
    }

    [Theory]
    [InlineData("ifmar-electric-offroad-10", 5)]
    [InlineData("ifmar-electric-track-10", 5)]
    [InlineData("ifmar-electric-track-12", 8)]
    public void Ifmar_sections_keep_the_own_clock_start_and_add_lengths_finals_of_ten_and_three_legs(string key, int minutes)
    {
        var raceClass = Applied(key);

        Assert.Equal(RaceStartType.Qualifier, raceClass.QualifierFormat!.StartType);
        Assert.Equal(minutes, raceClass.QualifierFormat.TimeLimitMinutes);
        Assert.Equal(0, raceClass.QualifierFormat.LapLimit);
        Assert.Equal(minutes, raceClass.Format!.TimeLimitMinutes);
        Assert.Equal(RaceStartType.Standard, raceClass.Format.StartType);
        Assert.Equal(QualifyingMethod.TotalLaps, raceClass.QualifyingMethod);
        Assert.Equal(10, raceClass.MainSize);
        Assert.Equal(3, raceClass.AMainLegs);
        Assert.Equal(2, raceClass.AMainLegsCounted);

        // IFMAR's best-3-of-5/6 is a points count, not the app's heats count.
        Assert.Equal(3, raceClass.CountBestRounds);
        Assert.Equal(5, raceClass.ConsecutiveLapCount);
        Assert.Equal(4, raceClass.BumpUpCount);
        AssertClubFormatKept(raceClass.Format);
        AssertClubFormatKept(raceClass.QualifierFormat);
    }

    [Fact]
    public void The_ifmar_convention_preset_still_sets_only_the_start_and_the_heat_ranking()
    {
        var raceClass = Applied("ifmar-qualifying");

        Assert.Equal(RaceStartType.Qualifier, raceClass.QualifierFormat!.StartType);
        Assert.Equal(11, raceClass.QualifierFormat.TimeLimitMinutes);
        Assert.Equal(QualifyingMethod.TotalLaps, raceClass.QualifyingMethod);
        Assert.Equal(7, raceClass.MainSize);
        Assert.Equal(1, raceClass.AMainLegs);
        Assert.Equal(RaceStartType.HandReleased, raceClass.Format!.StartType);
    }

    [Fact]
    public void Reedy_sets_five_minutes_and_a_three_leg_a_main_and_leaves_every_start_alone()
    {
        var raceClass = Applied("reedy-tc-2019-open");

        Assert.Equal(5, raceClass.QualifierFormat!.TimeLimitMinutes);
        Assert.Equal(5, raceClass.Format!.TimeLimitMinutes);
        Assert.Equal(3, raceClass.AMainLegs);
        Assert.Equal(2, raceClass.AMainLegsCounted);

        Assert.Equal(RaceStartType.HandReleased, raceClass.QualifierFormat.StartType);
        Assert.Equal(RaceStartType.HandReleased, raceClass.Format.StartType);
        Assert.Equal(QualifyingMethod.BestConsecutiveLaps, raceClass.QualifyingMethod);
        Assert.Equal(3, raceClass.CountBestRounds);
        Assert.Equal(7, raceClass.MainSize);
        Assert.Equal(4, raceClass.BumpUpCount);
        AssertClubFormatKept(raceClass.Format);
        AssertClubFormatKept(raceClass.QualifierFormat);
    }

    [Fact]
    public void No_preset_switches_on_round_points()
    {
        // ROAR's and EFRA's round points give the top qualifier 0 where the
        // app gives 1, which can swap two drivers - see SanctioningPresets.
        Assert.DoesNotContain(SanctioningPresets.All, p => p.Method == QualifyingMethod.RoundPoints);
    }

    [Theory]
    [InlineData("roar-electric-10")]
    [InlineData("roar-electric-12")]
    [InlineData("efra-electric-10")]
    [InlineData("efra-electric-12")]
    [InlineData("nzrca-ep-offroad-10")]
    [InlineData("nzrca-ep-onroad-5min")]
    [InlineData("ifmar-electric-offroad-10")]
    [InlineData("ifmar-electric-track-10")]
    [InlineData("ifmar-electric-track-12")]
    public void Every_rulebook_preset_cites_its_edition_and_says_to_check_it(string key)
    {
        var preset = Preset(key);

        Assert.Matches(@"20[12][0-9]", preset.Source);
        Assert.EndsWith("Check against your current rulebook before a sanctioned meeting.", preset.Source);
        Assert.False(string.IsNullOrWhiteSpace(preset.Caveats));
    }

    [Fact]
    public void The_reedy_preset_says_it_is_the_2019_format_and_to_check_this_years()
    {
        var preset = Preset("reedy-tc-2019-open");

        Assert.Contains("2019", preset.Source);
        Assert.Contains("Check against this year's published format before the event.", preset.Source);
    }

    [Fact]
    public void The_applied_list_names_the_a_main_legs()
    {
        var changes = Preset("efra-electric-10").ApplyTo(ClubClass(), new RaceFormat());

        Assert.Contains("A-main: 3 legs", changes);
        Assert.Contains("A-main legs that count: best 2", changes);
        Assert.Contains("Drivers per main: 10", changes);
    }

    [Fact]
    public void Preset_keys_are_unique()
    {
        var keys = SanctioningPresets.All.Select(p => p.Key.ToLowerInvariant()).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
    }
}
