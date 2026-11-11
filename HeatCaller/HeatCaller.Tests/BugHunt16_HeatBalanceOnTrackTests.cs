namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 16: with no race day open, the heat out on track was treated
/// like an old recorded heat - it counted for "one heat per qualifying
/// round" only when it was made in the last 18 hours. A Q1 heat on track
/// that was made earlier than that let its drivers be dropped into another
/// Q1 heat and run Q1 twice. The heat on track now always counts.
/// </summary>
public sealed class BugHunt16_HeatBalanceOnTrackTests
{
    private static RoundDefinition Heat(string label, params string[] drivers) => new()
    {
        ClassName = "Stock",
        RoundLabel = label,
        Kind = RoundKind.Qualifier,
        AssignedTransponders = drivers.ToList(),

        // Made the evening before - outside the no-race-day window.
        CreatedAtUtc = DateTime.UtcNow - TimeSpan.FromHours(30),
    };

    [Fact]
    public void A_driver_in_the_heat_on_track_cannot_be_dropped_into_another_heat_of_that_round()
    {
        var onTrack = Heat("Q1 Heat 1", "A", "B");
        var other = Heat("Q1 Heat 2", "C");

        var heats = new List<HeatBalanceForm.HeatState>
        {
            new(onTrack, Locked: true, onTrack.AssignedTransponders, OnTrack: true),
            new(other, Locked: false, other.AssignedTransponders),
        };

        var refusal = HeatBalanceForm.DescribeMoveBlocker(
            heats, source: null, target: other, "A", roundsAreOneRaceDay: false, t => t);

        Assert.NotNull(refusal);
        Assert.Contains("on track in Q1 Heat 1", refusal);
    }

    [Fact]
    public void An_old_recorded_heat_still_does_not_count_with_no_race_day()
    {
        var old = Heat("Q1 Heat 1", "A");
        var other = Heat("Q1 Heat 2", "C");

        var heats = new List<HeatBalanceForm.HeatState>
        {
            new(old, Locked: true, old.AssignedTransponders),
            new(other, Locked: false, other.AssignedTransponders),
        };

        Assert.Null(HeatBalanceForm.DescribeMoveBlocker(
            heats, source: null, target: other, "A", roundsAreOneRaceDay: false, t => t));
    }

    [Fact]
    public void The_heat_on_track_counts_as_racing_whenever_it_was_made()
    {
        var round = Heat("Q1 Heat 1", "A");

        Assert.True(HeatBalanceForm.HeatCounts(round, locked: true, onTrack: true, roundsAreOneRaceDay: false));
        Assert.False(HeatBalanceForm.HeatCounts(round, locked: true, onTrack: false, roundsAreOneRaceDay: false));
        Assert.True(HeatBalanceForm.HeatCounts(round, locked: false, onTrack: false, roundsAreOneRaceDay: false));
    }
}
