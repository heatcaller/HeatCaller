using System.Collections.Concurrent;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026): a position report read out in a heat classes
/// share names class places only ("Mo leads Mod, Sam leads Stock"), but it
/// was cut off for any change in the OVERALL order - so two cars of
/// different classes swapping on the road cut it mid-sentence and
/// "corrected" it with an overall place and the same class places it had
/// just read. It is now cut only when a class place changes, and the
/// correction names the mover's place in its class.
/// </summary>
public sealed class BugHunt17_AnnouncementService_PositionReportCutTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly Dictionary<string, string> ModAndStock = new(StringComparer.OrdinalIgnoreCase)
    {
        ["102"] = "Mod",
        ["101"] = "Stock",
        ["103"] = "Mod",
        ["104"] = "Stock",
    };

    // --- Which change cuts it -----------------------------------------------

    [Fact]
    public void Two_cars_of_different_classes_swapping_change_no_class_place()
    {
        Assert.Equal(-1, AnnouncementService.FirstChangedClassPlace(["102", "101", "103", "104"], ["102", "101", "104", "103"], ModAndStock));
        Assert.Equal(-1, AnnouncementService.FirstChangedClassPlace(["102", "101", "103", "104"], ["101", "102", "103", "104"], ModAndStock));
    }

    [Fact]
    public void A_pass_within_a_class_is_the_change_at_the_car_that_got_through()
    {
        // Bea past Sam for the Stock lead: she is second on the road.
        Assert.Equal(1, AnnouncementService.FirstChangedClassPlace(["102", "101", "103", "104"], ["102", "104", "101", "103"], ModAndStock));
    }

    [Fact]
    public void A_shorter_or_different_field_is_still_a_change()
    {
        Assert.Equal(2, AnnouncementService.FirstChangedClassPlace(["102", "101", "103"], ["102", "101"], ModAndStock));
        Assert.Equal(2, AnnouncementService.FirstChangedClassPlace(["102", "101", "103"], ["102", "101", "104"], ModAndStock));
    }

    [Fact]
    public void A_car_the_class_map_does_not_know_goes_by_its_place_overall()
    {
        Assert.Equal(-1, AnnouncementService.FirstChangedClassPlace(["102", "900", "101"], ["102", "900", "101"], ModAndStock));
        Assert.Equal(0, AnnouncementService.FirstChangedClassPlace(["102", "900", "101"], ["900", "102", "101"], ModAndStock));
    }

    // --- The words ---------------------------------------------------------

    [Fact]
    public void The_correction_names_the_place_in_the_class()
    {
        Assert.Equal("[shouting] Hold on - Bea takes the lead in Stock! So it's {positions}.",
            AnnouncementService.ReportCorrectionTemplate("Bea", 1, byRuling: false, "Stock"));
        Assert.Equal("[excited] Hold on - Bea is through into second in Stock! So it's {positions}.",
            AnnouncementService.ReportCorrectionTemplate("Bea", 2, byRuling: false, "Stock"));
        Assert.Equal("[excited] And with that ruling, Bea is up into second in Stock! So it's {positions}.",
            AnnouncementService.ReportCorrectionTemplate("Bea", 2, byRuling: true, "Stock"));

        // A one-class heat reads as it always did.
        Assert.Equal("[excited] Hold on - Frank is through into second! So it's {positions}.",
            AnnouncementService.ReportCorrectionTemplate("Frank", 2, byRuling: false));
    }

    // --- In the announcer ---------------------------------------------------

    private static RaceStanding Car(string transponder, int laps, long elapsedMs) => new()
    {
        Transponder = transponder,
        Laps = laps,
        ElapsedMs = elapsedMs,
        Status = "active",
        Id = transponder,
    };

    /// <summary>
    /// Every call and sound off; Bill (Mod), Sam (Stock), Frank (Mod) and Bea
    /// (Stock) on lap 4 in that order, and the report reading all four.
    /// </summary>
    private static (AnnouncementService Announcer, ConcurrentQueue<string> Shown, ConcurrentQueue<string> Log) SharedHeatBeingRead()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "101", DriverName = "Sam" });
        profiles.Upsert(new DriverProfile { Transponder = "102", DriverName = "Bill" });
        profiles.Upsert(new DriverProfile { Transponder = "103", DriverName = "Frank" });
        profiles.Upsert(new DriverProfile { Transponder = "104", DriverName = "Bea" });

        var voice = VoiceSettingsStore.Load();
        foreach (var config in voice.Current.Events.Values)
        {
            config.Enabled = false;
            config.EnabledInPractice = false;
        }

        var sounds = SoundEffectsStore.Load();
        foreach (var effect in sounds.Current.Effects.Values)
        {
            effect.Enabled = false;
        }

        var announcer = new AnnouncementService(profiles, voice, sounds)
        {
            SharedHeatClasses = () => ModAndStock,
        };

        var shown = new ConcurrentQueue<string>();
        var log = new ConcurrentQueue<string>();
        announcer.AnnouncementTextReady += shown.Enqueue;
        announcer.StatusMessage += log.Enqueue;

        announcer.OnStandingUpdated(Car("102", 4, 40_000));
        announcer.OnStandingUpdated(Car("101", 4, 40_400));
        announcer.OnStandingUpdated(Car("103", 4, 41_000));
        announcer.OnStandingUpdated(Car("104", 4, 41_500));
        announcer.PutPositionReportOnAirForTest(["102", "101", "103", "104"], 4);

        return (announcer, shown, log);
    }

    private static bool WasCut(ConcurrentQueue<string> log) =>
        log.Any(line => line.StartsWith("[voice] Position report ", StringComparison.Ordinal)
            && (line.Contains("cut off", StringComparison.Ordinal) || line.Contains("dropped before it spoke", StringComparison.Ordinal)));

    [Fact]
    public void A_pass_between_classes_leaves_the_report_alone()
    {
        var (announcer, shown, log) = SharedHeatBeingRead();

        // Bill and Sam cross, then Bea (Stock) crosses before Frank (Mod):
        // she is third on the road, and still second in Stock.
        announcer.OnStandingUpdated(Car("102", 5, 50_000));
        announcer.OnStandingUpdated(Car("101", 5, 50_400));
        announcer.OnStandingUpdated(Car("104", 5, 50_800));
        announcer.Muted = true;

        Assert.False(WasCut(log));
        Assert.Empty(shown);
    }

    [Fact]
    public void A_pass_within_a_class_is_corrected_with_the_class_place()
    {
        var (announcer, shown, log) = SharedHeatBeingRead();

        // Bill crosses, then Bea before Sam: she leads Stock.
        announcer.OnStandingUpdated(Car("102", 5, 50_000));
        announcer.OnStandingUpdated(Car("104", 5, 50_300));
        announcer.Muted = true;

        Assert.True(WasCut(log));
        Assert.Equal(
            new[] { "Hold on - Bea takes the lead in Stock! So it's Bill leads Mod, Bea leads Stock, Sam second in Stock, Frank second in Mod." },
            shown.ToList());
    }
}
