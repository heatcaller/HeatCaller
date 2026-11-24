namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (F273): a phantom split taken off by hand with -1 lap stayed
/// the driver's best in the record watch, so every later lap was judged
/// against a 2.0 s "best" no record could ever take, and the record-in-danger
/// call for that driver went silent for the rest of the race.
/// RecordWatch.Forget now rebuilds the driver from the laps that are left.
/// </summary>
public sealed class BugHunt24_RecordWatchTests
{
    private const string Driver = "SAM";

    private static long Ms(double seconds) => (long)Math.Round(seconds * 1000);

    /// <summary>A 5.04 s record, judged the way Race Control judges one on plain numbers.</summary>
    private static RecordWatch Watch()
    {
        var watch = new RecordWatch
        {
            LayoutRecord = () => (5.040, "Dave"),
            RecordWouldStand = (_, lap, record) => RecordPlausibility.Doubt(Ms(lap), 0, Ms(record), null) is null,
        };

        watch.Reset();
        return watch;
    }

    private static List<RecordThreat> Feed(RecordWatch watch, params double[] laps)
    {
        var said = new List<RecordThreat>();

        foreach (var lap in laps)
        {
            if (watch.Saw(new LapSeen(Driver, lap)) is { } threat)
            {
                said.Add(threat);
            }
        }

        return said;
    }

    [Fact]
    public void A_lap_taken_off_stops_being_the_drivers_best()
    {
        var watch = Watch();

        // A double read splits one lap into 2.0 s and 3.1 s.
        Assert.Empty(Feed(watch, 5.30, 5.25, 2.0, 3.1));

        // The director presses -1 lap: the two fold back into 5.1 s.
        watch.Forget(Driver, new[] { 5.30, 5.25, 5.1 });

        Assert.Equal(3, watch.Seen(Driver));

        var said = Feed(watch, 5.08, 5.07, 5.06);

        var threat = Assert.Single(said);
        Assert.Equal(RecordThreatLevel.OnTheDoorstep, threat.Level);
        Assert.Equal(5.08, threat.BestThisRace, 3);
    }

    [Fact]
    public void Forgetting_every_lap_leaves_the_driver_starting_again()
    {
        var watch = Watch();

        Feed(watch, 5.30, 2.0);
        watch.Forget(Driver, Array.Empty<double>());

        Assert.Equal(0, watch.Seen(Driver));

        // Two warm-up laps again, then pace on the doorstep is said.
        var said = Feed(watch, 5.30, 5.25, 5.08, 5.07);

        Assert.Contains(said, t => t.Level == RecordThreatLevel.OnTheDoorstep);
    }

    [Fact]
    public void Forgetting_a_lap_does_not_say_the_same_level_twice()
    {
        var watch = Watch();

        Assert.Contains(Feed(watch, 5.30, 5.25, 5.08, 5.07), t => t.Level == RecordThreatLevel.OnTheDoorstep);

        watch.Forget(Driver, new[] { 5.30, 5.25, 5.08 });

        Assert.DoesNotContain(Feed(watch, 5.07, 5.08), t => t.Level == RecordThreatLevel.OnTheDoorstep);
    }
}
