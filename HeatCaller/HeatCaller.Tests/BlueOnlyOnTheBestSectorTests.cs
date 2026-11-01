using System.Drawing;

namespace HeatCaller.Tests;

/// <summary>
/// 1 Nov 2026: split times showed blue for two drivers at once, where blue
/// is meant to mark only the quickest of anyone. The board columns already
/// told the two apart (Highlight for the session's quickest sector, Good
/// for a driver's own best); the colours did not. The Big Screen painted
/// both in the look's accent, and Synthwave's "good" was a cyan a shade
/// off its accent - so a driver's own best read as a second session best.
/// </summary>
public sealed class BlueOnlyOnTheBestSectorTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private sealed class NoDrivers : IDriverLookup
    {
        public DriverProfile? Get(string transponder) => null;
    }

    private static void Cross(RaceControlSession race, string car, uint at) =>
        race.ProcessLapEvent(new DirectLapEvent { TransponderId = car, DeviceTimeMs = at, SignalHits = 5, SignalStrength = 100 });

    [Fact]
    public void Only_the_quickest_sector_of_anyone_is_highlighted_and_an_own_best_is_good()
    {
        var race = new RaceControlSession { IsDemoMode = true, SplitCount = 1 };
        race.Stage();
        race.GreenFlag(0);

        // A: one flying lap, S1 3.800 - the quickest of anyone.
        Cross(race, "A", 5_000);
        Assert.True(race.ProcessSplitPass("A", TimingPoint.Split1, 8_800));
        Cross(race, "A", 15_000);

        // B: S1 4.100, then 4.000 - their own best, still slower than A's.
        Cross(race, "B", 5_100);
        Assert.True(race.ProcessSplitPass("B", TimingPoint.Split1, 9_200));
        Cross(race, "B", 15_200);
        Assert.True(race.ProcessSplitPass("B", TimingPoint.Split1, 19_200));
        Cross(race, "B", 25_300);

        var ordered = race.GetOrderedStandings();
        var context = new KpiContext { Ordered = ordered, Profiles = new NoDrivers(), Tracker = new KpiTracker() };
        var s1 = KpiCatalog.Find("s1")!;

        var a = s1.Compute(ordered.Single(s => s.TransponderId == "A"), 0, context);
        var b = s1.Compute(ordered.Single(s => s.TransponderId == "B"), 1, context);

        Assert.Equal(KpiEmphasis.Highlight, a.Emphasis);
        Assert.Equal(KpiEmphasis.Good, b.Emphasis);
    }

    [Fact]
    public void The_big_screen_paints_an_own_best_in_the_good_colour_not_the_accent()
    {
        var look = Looks.Resolve(Looks.Find(Looks.SynthwaveKey)!);

        Assert.Equal(look.Accent, BigScreenEmphasis.Colour(KpiEmphasis.Highlight, look.Accent, look.Good));
        Assert.Equal(look.Good, BigScreenEmphasis.Colour(KpiEmphasis.Good, look.Accent, look.Good));
        Assert.Null(BigScreenEmphasis.Colour(KpiEmphasis.Bad, look.Accent, look.Good));
        Assert.Null(BigScreenEmphasis.Colour(KpiEmphasis.None, look.Accent, look.Good));
    }

    [Fact]
    public void Every_built_in_look_keeps_its_accent_and_good_colours_apart()
    {
        var alike = Looks.BuiltIn
            .Select(l => (l.Name, Look: Looks.Resolve(l)))
            .Where(l => Looks.LookAlike(l.Look.Accent, l.Look.Good))
            .Select(l => $"{l.Name}: {Looks.DeltaE(l.Look.Accent, l.Look.Good):0.0}")
            .ToList();

        Assert.Empty(alike);
    }

    [Fact]
    public void Synthwave_s_old_cyan_good_reads_as_its_accent()
    {
        // The pair the report came from: #2EE6C4 beside #2EE6FF.
        Assert.True(Looks.LookAlike(ColorTranslator.FromHtml("#2EE6FF"), ColorTranslator.FromHtml("#2EE6C4")));

        // Blue beside green, as Midnight has it, is plainly two colours.
        Assert.False(Looks.LookAlike(ColorTranslator.FromHtml("#4C8DFF"), ColorTranslator.FromHtml("#34D399")));
    }

    [Fact]
    public void A_club_look_with_a_look_alike_accent_and_good_gets_a_warning()
    {
        var colors = new LookColors { Accent = "#2EE6FF", Good = "#2EE6C4" };

        Assert.Contains(Looks.ReadabilityWarnings(colors), w => w.Contains("look alike"));
        Assert.DoesNotContain(Looks.ReadabilityWarnings(new LookColors()), w => w.Contains("look alike"));
    }
}
