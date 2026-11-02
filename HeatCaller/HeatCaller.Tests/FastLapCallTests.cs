namespace HeatCaller.Tests;

/// <summary>
/// The race's fastest lap call (1 Nov 2026). It was dropped whenever another
/// line was playing, so the last one heard was often not the lap on the
/// board - see AnnouncementService.IsWorthWaitingFor.
/// </summary>
public class FastLapCallTests
{
    [Fact]
    public void The_fastest_lap_waits_its_turn_rather_than_being_dropped()
    {
        Assert.True(AnnouncementService.IsWorthWaitingFor(AnnouncementEventType.FastLap));

        // Waiting is all it gets: a flag still clears it.
        Assert.False(AnnouncementService.SurvivesAFlag(AnnouncementEventType.FastLap));
    }
}
