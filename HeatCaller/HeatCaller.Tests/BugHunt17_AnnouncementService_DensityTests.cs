using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: what a new race forgets of the last one - the final minute,
/// the count of passes made ready, the milestones heard - was cleared at
/// staging alone. A race started with Green or Countdown and no Stage after
/// a timed one was still "in the final minute", so the gap for the lead was
/// called every lap from its second. ResetSession, which the green that
/// starts a race calls, now clears them too.
/// </summary>
public sealed class BugHunt17_AnnouncementService_DensityTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static FieldInfo FieldOf(object owner, string name) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(owner.GetType().Name, name);

    private static AnnouncementService Announcer() =>
        new(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load());

    [Fact]
    public void A_race_started_without_staging_is_not_still_in_the_final_minute()
    {
        var announcer = Announcer();

        // The last race, a timed one, reached its final minute.
        FieldOf(announcer, "_inTheFinalMinute").SetValue(announcer, true);

        // Green straight from Finished: Race Control resets the session and
        // never calls NotifyRaceStaging.
        announcer.ResetSession();

        Assert.False((bool)FieldOf(announcer, "_inTheFinalMinute").GetValue(announcer)!);
    }

    [Fact]
    public void A_race_started_without_staging_gets_its_own_passes_and_milestones()
    {
        var announcer = Announcer();

        FieldOf(announcer, "_predictionsThisRace").SetValue(announcer, AnnouncementService.MostPredictionsPerRace);
        var heard = (HashSet<string>)FieldOf(announcer, "_milestonesHeard").GetValue(announcer)!;
        heard.Add("race|101");

        announcer.ResetSession();

        Assert.Equal(0, (int)FieldOf(announcer, "_predictionsThisRace").GetValue(announcer)!);
        Assert.Empty(heard);
    }
}
