using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), F017: a crowd stopped by the mute, the Stop
/// button or a lost sound device kept the mood it stopped at, and the next
/// Start forced every layer to that old level in one step before the race's
/// mood faded it down - a crowd muted in the finish cheer opened at full
/// roar over the next staging call. SetOpeningMood lets the caller say which
/// mood a stopped crowd opens at.
/// </summary>
public sealed class BugHunt24_AmbienceServiceTests
{
    private static FieldInfo FieldOf(object owner, string name) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(owner.GetType().Name, name);

    [Fact]
    public async Task BugHunt24_StoppedInTheFinishCheer_OpensAtTheMoodAsked_AndTheOldTimerLeavesItAlone()
    {
        using var crowd = new AmbienceService();

        // Running with no bed: the mood bookkeeping is what is under test,
        // and ApplyIntensity simply finds no layers to move.
        FieldOf(crowd, "_running").SetValue(crowd, true);
        crowd.CelebrateFinishThenSettle(1);
        Assert.Equal(AmbienceService.Intensity.Peak, crowd.CurrentIntensity);

        // Muted mid-cheer: the crowd is off.
        FieldOf(crowd, "_running").SetValue(crowd, false);

        // The next heat is staged; the caller says what the crowd opens at.
        Assert.True(crowd.SetOpeningMood(AmbienceService.Intensity.Anticipation));

        // No layers are set up, so nothing opens - but the mood the next
        // open would force is the race's, not the cheer's.
        crowd.Start(ignoreEnabledSetting: true);
        Assert.Equal(AmbienceService.Intensity.Anticipation, crowd.CurrentIntensity);
        Assert.Equal(AmbienceService.Intensity.Anticipation, crowd.EffectiveIntensity);
        Assert.False(crowd.IsSurging);

        // The old celebration's timer runs out and does not touch it.
        await Task.Delay(1500);
        Assert.Equal(AmbienceService.Intensity.Anticipation, crowd.CurrentIntensity);
    }

    [Fact]
    public void BugHunt24_SetOpeningMood_LeavesARunningCrowdAlone()
    {
        using var crowd = new AmbienceService();

        FieldOf(crowd, "_running").SetValue(crowd, true);
        crowd.SetIntensity(AmbienceService.Intensity.Racing);

        // A running crowd follows SetIntensity, which fades; this must not
        // move its mood behind the fade's back.
        Assert.False(crowd.SetOpeningMood(AmbienceService.Intensity.Idle));
        Assert.Equal(AmbienceService.Intensity.Racing, crowd.CurrentIntensity);

        FieldOf(crowd, "_running").SetValue(crowd, false);
    }
}
