namespace HeatCaller.Tests;

/// <summary>
/// One set of night fields for Setup's Tonight tab and the Edit Template
/// window (NightFields, 14 Nov 2026): a template goes through the editor and
/// comes back with every setting it had, and both pages build their fields
/// from NightFields rather than their own copies.
/// </summary>
public sealed class NightFieldsTests
{
    private static readonly string[] Classes = { "Stock", "Mod", "Juniors" };

    /// <summary>A template with every setting off its default, so a field the editor drops shows.</summary>
    private static NightTemplate Everything() => new()
    {
        Name = "Points round",
        Shape = RaceDayShape.ClubNight,
        VenueName = "Riverside Raceway",
        TrackLayoutName = "Long clay",
        ChampionshipId = string.Empty,
        ClassNames = new List<string> { "Stock", "Juniors" },
        ClassRunningOrder = new List<string> { "Juniors", "Stock" },
        RotateClassOrder = true,
        QualifyingRounds = 4,
        DriversPerHeat = 6,
        FirstRoundSeeding = FirstRoundSeeding.Random,
        ShareHeatsUnder = 3,
        RacesOffBetween = 2,
        EntryFee = 25,
        ExtraClassFee = 10,
        BetweenRacesPreset = GapPreset.All[0].Key,
        CheckedInDriversOnly = true,
        FirstRaceLocalTime = TimeSpan.FromHours(10),
        EntriesCloseLocalTime = TimeSpan.FromHours(9.5),
        OutByLocalTime = TimeSpan.FromHours(17),
        Breaks = new List<PlannedBreak> { new() { AfterRound = 2, Minutes = 30, What = "Lunch" } },
        Weekdays = new List<DayOfWeek> { DayOfWeek.Saturday },
        Dates = new List<DateTime> { new(2026, 12, 13) },
    };

    private static string Json(NightTemplate template) => System.Text.Json.JsonSerializer.Serialize(template);

    private static T OnUiThread<T>(Func<T> body) =>
        StaThread.Run(body, TimeSpan.FromMinutes(1), () => "the night fields");

    [Fact]
    public void A_template_comes_back_from_the_editor_with_every_setting_it_had()
    {
        var original = Everything();

        var back = OnUiThread(() =>
        {
            using var form = new TemplateEditForm(original, isNew: false, Array.Empty<string>(), Classes,
                new[] { "Riverside Raceway" }, new[] { "Long clay" }, Array.Empty<Season>(), forecast: null);

            return form.AsItStands;
        });

        Assert.Equal(Json(original), Json(back));
    }

    [Fact]
    public void Every_template_setting_is_changed_from_its_default_in_the_round_trip_test()
    {
        // A setting added to NightTemplate and left out of Everything() above
        // would pass the round trip by being its default both ways. So every
        // property must differ from a new template's.
        var fresh = new NightTemplate();
        var full = Everything();

        var same = typeof(NightTemplate).GetProperties()
            .Where(p => p.Name != nameof(NightTemplate.ChampionshipId))
            .Where(p => System.Text.Json.JsonSerializer.Serialize(p.GetValue(fresh)) == System.Text.Json.JsonSerializer.Serialize(p.GetValue(full)))
            .Select(p => p.Name)
            .ToList();

        Assert.True(same.Count == 0, "Give these a non-default value in Everything(), and check the editor handles them: " + string.Join(", ", same));
    }

    [Fact]
    public void A_class_the_template_names_that_the_club_no_longer_has_is_kept()
    {
        var original = Everything();
        original.ClassNames = new List<string> { "Stock", "Gone" };
        original.ClassRunningOrder = new List<string>();

        var back = OnUiThread(() =>
        {
            using var form = new TemplateEditForm(original, isNew: false, Array.Empty<string>(), Classes,
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<Season>(), forecast: null);

            return form.AsItStands;
        });

        Assert.Equal(new[] { "Stock", "Gone" }, back.ClassNames);
    }

    [Fact]
    public void Tonight_and_the_template_window_both_build_on_the_shared_fields()
    {
        var tonight = Source("RaceDayEventControl.cs");
        var window = Source("TemplateEditForm.cs");

        foreach (var source in new[] { tonight, window })
        {
            foreach (var rows in new[] { "AddNightRows(", "AddTimesRows(", "AddHeatsRows(", "AddMoneyRows(", "AddRunRows(" })
            {
                Assert.Contains("_fields." + rows, source);
            }

            Assert.Contains("_fields.WriteTo(", source);
        }

        // Neither builds a field of its own for a setting the template keeps.
        foreach (var own in new[] { "new NumericUpDown", "new TimePicker", "new CheckedListBox", "GapPreset.All" })
        {
            Assert.DoesNotContain(own, window);
        }

        Assert.DoesNotContain("_numHeatSize", tonight);
        Assert.DoesNotContain("_cmbGapPreset", tonight);
        Assert.DoesNotContain("_lstClasses", tonight);
    }

    private static string Source(string fileName)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "check.cmd")))
        {
            folder = folder.Parent;
        }

        Assert.NotNull(folder);
        return File.ReadAllText(Path.Combine(folder!.FullName, "HeatCaller", "HeatCaller", fileName));
    }
}
