using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// The position report in a heat classes share (13 Nov 2026, mixed-class
/// heats): the same cars in the same overall order, each called by their
/// place in their own class, with no gaps - which would be measured to an
/// overall leader in another class.
/// </summary>
public sealed class PositionReportByClassTests
{
    private static AnnouncementService Announcer(IReadOnlyDictionary<string, string>? classes)
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "201", DriverName = "Mo" });
        profiles.Upsert(new DriverProfile { Transponder = "202", DriverName = "Sam" });
        profiles.Upsert(new DriverProfile { Transponder = "203", DriverName = "Bea" });

        var announcer = new AnnouncementService(profiles, VoiceSettingsStore.Load(), SoundEffectsStore.Load())
        {
            SharedHeatClasses = () => classes,
        };

        return announcer;
    }

    private static PositionReportOrder.Row Row(string transponder, int laps, long elapsedMs, long? gapMs) =>
        new(new RaceStanding { Transponder = transponder, Laps = laps, ElapsedMs = elapsedMs }, new RaceGap(0, gapMs), new RaceGap(0, gapMs));

    private static readonly PositionReportOrder.Row[] Order =
    {
        Row("201", 10, 80_000, null),
        Row("202", 10, 82_000, 2_000),
        Row("203", 10, 83_500, 3_500),
    };

    private static string Word(AnnouncementService announcer, string template, bool sayGaps) =>
        (string)typeof(AnnouncementService)
            .GetMethod("WordRunningOrder", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(announcer, new object[] { Order, template, sayGaps, false })!;

    private static readonly Dictionary<string, string> ModAndStock = new(StringComparer.OrdinalIgnoreCase)
    {
        ["201"] = "Mod",
        ["202"] = "Stock",
        ["203"] = "Stock",
    };

    [Fact]
    public void A_shared_heat_calls_each_car_by_its_place_in_its_own_class()
    {
        var line = Word(Announcer(ModAndStock), "{positions}.", sayGaps: false);

        Assert.Contains("Mo leads Mod", line);
        Assert.Contains("Sam leads Stock", line);
        Assert.Contains("Bea second in Stock", line);
    }

    [Fact]
    public void A_shared_heat_says_no_gaps_and_tidies_the_line_round_them()
    {
        var line = Word(Announcer(ModAndStock), "{positions}. {gaps}.", sayGaps: true);

        Assert.DoesNotContain("back", line);
        Assert.DoesNotContain("{gaps}", line);
        Assert.Contains("Bea second in Stock", line);
    }

    [Fact]
    public void A_one_class_heat_is_called_as_it_always_was()
    {
        var line = Word(Announcer(null), "{positions}.", sayGaps: false);

        Assert.Contains("Mo leads", line);
        Assert.DoesNotContain(" in ", line);
        Assert.DoesNotContain("Stock", line);
    }

    [Fact]
    public void A_car_the_class_map_does_not_know_is_called_by_its_place_overall()
    {
        var classes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["201"] = "Mod", ["202"] = "Stock" };
        var line = Word(Announcer(classes), "{positions}.", sayGaps: false);

        Assert.Contains("Bea third", line);
    }
}
