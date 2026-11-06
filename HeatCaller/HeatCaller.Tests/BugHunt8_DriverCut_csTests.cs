namespace HeatCaller.Tests;

/// <summary>
/// BUG HUNT 8 (20 Oct 2026) - a driver's piece claimed a charge from a grid
/// slot that could not exist in that race. The per-race sentence refused a
/// StartPosition bigger than the field, but the moments block took
/// NightMoments' charge at face value.
/// </summary>
public sealed class BugHunt8_DriverCut_csTests : IDisposable
{
    private readonly TempAppData _appData = new();

    public void Dispose() => _appData.Dispose();

    private static readonly DateTime Green = new(2026, 10, 20, 19, 0, 0, DateTimeKind.Utc);

    private static SessionResultEntry Row(string name, int position, int startPosition) =>
        new()
        {
            Transponder = name.ToLowerInvariant(),
            DriverName = name,
            Position = position,
            Laps = 3,
            LapTimesMs = new List<long> { 10000 + position * 100, 9000 + position * 100, 9000 + position * 100 },
            StartPosition = startPosition,
            ElapsedMs = 28000 + position * 300,
        };

    private static SessionRecord FiveCarHeat(int samStartedFrom) =>
        new()
        {
            Id = "heat-2",
            SessionType = RaceSessionType.Race,
            Label = "Heat 2",
            TrackLayoutName = "Winter Indoor",
            StartedAtUtc = Green,
            EndedAtUtc = Green.AddMinutes(8),
            Results = new List<SessionResultEntry>
            {
                Row("Sam", 1, samStartedFrom),
                Row("Bill", 2, 1),
                Row("Ann", 3, 2),
                Row("Joe", 4, 3),
                Row("Kim", 5, 4),
            },
        };

    [Fact]
    public void A_stale_grid_slot_bigger_than_the_field_is_ranked_over_the_real_cars()
    {
        // Eighth on the grid of a five-car heat is left over from a bigger
        // race before it - "made up 7 places" never happened.
        // 6 Nov 2026, Bug hunt 11b review: the slot is now ranked over the
        // real cars, as the night page does - so slot 8 behind four cars is
        // 5th, "up from fifth" and four places, never "up from eighth" or seven.
        // (A stated roster keeps a no-show's slot, so a slot past the rows
        // is not always stale.)
        var facts = DriverCut.FactsFor("sam", "Sam", new List<SessionRecord> { FiveCarHeat(8) });

        Assert.False(facts.Empty);
        Assert.DoesNotContain("7 places", facts.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("eighth", facts.Text, StringComparison.Ordinal);
        Assert.Contains("up from fifth", facts.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_real_charge_from_the_back_of_the_field_is_still_said()
    {
        // Fifth of five to first is four places, and every one of them real.
        var facts = DriverCut.FactsFor("sam", "Sam", new List<SessionRecord> { FiveCarHeat(5) });

        Assert.Contains("made up 4 places", facts.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_slot_check_wants_the_race_and_the_driver_in_it()
    {
        var heat = FiveCarHeat(5);
        var charge = new NightMoments.Moment(
            NightMoments.Kind.Charge, "heat-2", "Heat 2", "sam", "Sam", 0, 0, 4, string.Empty, Green);

        Assert.True(DriverCut.ChargeFromARealSlot(charge, new[] { heat }, "sam"));
        Assert.False(DriverCut.ChargeFromARealSlot(charge with { SessionId = "elsewhere" }, new[] { heat }, "sam"));
        Assert.False(DriverCut.ChargeFromARealSlot(charge, new[] { heat }, "nobody"));
        // 6 Nov 2026, Bug hunt 11b review: a slot past the rows is ranked
        // over the real cars, not refused; only no slot at all is no charge.
        Assert.True(DriverCut.ChargeFromARealSlot(charge, new[] { FiveCarHeat(6) }, "sam"));
        Assert.False(DriverCut.ChargeFromARealSlot(charge, new[] { FiveCarHeat(0) }, "sam"));
    }
}
