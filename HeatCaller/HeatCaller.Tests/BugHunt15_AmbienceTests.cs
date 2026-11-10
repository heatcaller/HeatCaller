using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15 (9 Nov 2026), B16: a finish celebration's settle timer had no
/// token for which celebration it belonged to. A checkered flag thrown early,
/// undone and then given again inside the window had the first timer find the
/// crowd at Peak again and fade it to Idle part-way through the real cheer.
/// </summary>
public sealed class BugHunt15_AmbienceTests
{
    private static FieldInfo FieldOf(object owner, string name) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(owner.GetType().Name, name);

    [Fact]
    public async Task A_stale_celebration_timer_does_not_cut_a_later_one_short()
    {
        using var crowd = new AmbienceService();

        // Running with no bed: the mood bookkeeping is what is under test,
        // and ApplyIntensity simply finds no layers to move.
        FieldOf(crowd, "_running").SetValue(crowd, true);

        // The checkered a lap early: a 1 s celebration starts.
        crowd.CelebrateFinishThenSettle(1);

        // Undone - the race goes back to Racing...
        crowd.SetIntensity(AmbienceService.Intensity.Racing);
        await Task.Delay(400);

        // ...and the real finish comes with a 2 s celebration of its own.
        crowd.CelebrateFinishThenSettle(2);

        // Past the first timer's end, well inside the second celebration.
        await Task.Delay(1200);
        Assert.Equal(AmbienceService.Intensity.Peak, crowd.CurrentIntensity);

        // The second one still settles the crowd when IT ends.
        await Task.Delay(1600);
        Assert.Equal(AmbienceService.Intensity.Idle, crowd.CurrentIntensity);

        FieldOf(crowd, "_running").SetValue(crowd, false);
    }
}
