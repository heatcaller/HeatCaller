namespace HeatCaller.Tests;

/// <summary>
/// Bug Hunt 15, B2: the transponder health record and passes with no
/// strength figure (an OpenStint's) or no reality (a simulated decoder's).
/// </summary>
public sealed class BugHunt15_TransponderHealthTests
{
    private static readonly DateTime Day = new(2026, 9, 1);

    private static void Night(TransponderHealthStore store, string id, DateTime day, int strength, int hits = 30, int passes = 10, bool simulated = false)
    {
        for (var i = 0; i < passes; i++)
        {
            store.Record(new DirectLapEvent { TransponderId = id, SignalStrength = strength, SignalHits = hits, IsSimulated = simulated },
                day.Date.AddHours(19).AddMinutes(i));
        }
    }

    [Fact]
    public void A_night_of_OpenStint_passes_with_no_strength_is_not_called_fading_against_P3_nights()
    {
        var store = TransponderHealthStore.InMemory();

        Night(store, "4385919", Day, 110);
        Night(store, "4385919", Day.AddDays(7), 112);
        Night(store, "4385919", Day.AddDays(14), 108);

        // OpenStint: real hits, no strength figure.
        Night(store, "4385919", Day.AddDays(21), 0);

        var health = store.Assess("4385919");

        Assert.Equal(TransponderHealthStore.Verdict.Good, health.Verdict);
        Assert.Equal(0, health.Drop);
        Assert.Empty(store.Fading(TimeSpan.FromDays(31), Day.AddDays(21).AddHours(20)));
    }

    [Fact]
    public void Strength_is_averaged_over_the_passes_that_carried_one()
    {
        var store = TransponderHealthStore.InMemory();

        Night(store, "4385919", Day, 110);
        Night(store, "4385919", Day.AddDays(7), 112);
        Night(store, "4385919", Day.AddDays(14), 108);

        // A night with real P3 passes and as many with no strength figure:
        // the zeros used to halve the average and call it fading.
        Night(store, "4385919", Day.AddDays(21), 110);
        Night(store, "4385919", Day.AddDays(21), 0);

        var health = store.Assess("4385919");

        Assert.Equal(TransponderHealthStore.Verdict.Good, health.Verdict);
        Assert.Equal(110, health.Latest!.AverageStrength);
        Assert.Equal(20, health.Latest.Readings);
    }

    [Fact]
    public void A_real_strength_drop_is_still_fading()
    {
        var store = TransponderHealthStore.InMemory();

        Night(store, "4385919", Day, 120);
        Night(store, "4385919", Day.AddDays(7), 120);
        Night(store, "4385919", Day.AddDays(14), 120);
        Night(store, "4385919", Day.AddDays(21), 0);
        Night(store, "4385919", Day.AddDays(28), 80);

        var health = store.Assess("4385919");

        Assert.Equal(TransponderHealthStore.Verdict.Fading, health.Verdict);
        Assert.InRange(health.Drop, 0.32, 0.35);
    }

    [Fact]
    public void A_night_saved_before_strength_readings_were_counted_still_has_its_strength()
    {
        var old = new TransponderHealthStore.Night { Date = "2026-09-01", Readings = 10, TotalStrength = 1100, TotalHits = 300 };

        Assert.Equal(110, old.AverageStrength);
        Assert.True(old.HasStrength);
    }

    [Fact]
    public void A_simulated_decoder_s_passes_are_never_recorded()
    {
        var store = TransponderHealthStore.InMemory();

        Night(store, "4385919", Day, 110, simulated: true);

        var health = store.Assess("4385919");

        Assert.Equal(TransponderHealthStore.Verdict.NotEnoughData, health.Verdict);
        Assert.Equal(0, health.NightsOnRecord);
    }

    [Fact]
    public void A_pass_marked_simulated_stays_marked_through_its_copies()
    {
        var pass = new DirectLapEvent { TransponderId = "4385919", SignalHits = 20 }.AsSimulated();

        Assert.True(pass.IsSimulated);
        Assert.True(pass.Stamped(DecoderConnection.OpenStint, 5, TimingPoint.Line).IsSimulated);
        Assert.True(pass.WithTransponder("84FBD4").IsSimulated);
    }
}
