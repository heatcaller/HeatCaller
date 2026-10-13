namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 7, odds and ends: the cloud backup light after a service is
/// ticked or unticked, and the badges that count races "in a day" judging
/// the club night rather than the calendar day.
/// </summary>
public sealed class BugHunt7MiscTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 5, 20, 0, 0, DateTimeKind.Utc);

    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- The cloud light when the ticked services change -------------------

    [Fact]
    public void Ticking_a_new_service_is_not_green_on_the_strength_of_the_others()
    {
        // OneDrive has been copying fine; Google Drive has never had a copy.
        var settings = new AppSettings { LastCloudBackupUtc = Now.AddHours(-1), LastCloudBackupProblem = string.Empty };

        CloudBackupStatus.ServiceSwitched(settings, switchedOn: true);

        Assert.Null(settings.LastCloudBackupUtc);

        var state = CloudBackupStatus.Assess(new CloudBackupStatus.Facts(
            new[] { new CloudBackupStatus.Service("OneDrive", "OneDrive", true), new CloudBackupStatus.Service("Google Drive", "Google Drive", true) },
            settings.LastCloudBackupUtc,
            settings.LastCloudBackupProblem,
            Now));

        Assert.Equal(MissionControl.Level.Watch, state.Level);
        Assert.Equal("Not yet", state.Headline);
    }

    [Fact]
    public void Ticking_a_new_service_keeps_a_failure_about_the_ones_still_ticked()
    {
        var settings = new AppSettings { LastCloudBackupProblem = "Not copied to OneDrive: There is not enough space on the disk." };

        CloudBackupStatus.ServiceSwitched(settings, switchedOn: true);

        Assert.Equal("Not copied to OneDrive: There is not enough space on the disk.", settings.LastCloudBackupProblem);
    }

    [Fact]
    public void Unticking_a_service_drops_a_failure_that_may_have_been_about_it()
    {
        var settings = new AppSettings
        {
            LastCloudBackupUtc = Now.AddDays(-2),
            LastCloudBackupProblem = "Not copied to OneDrive: There is not enough space on the disk.",
        };

        CloudBackupStatus.ServiceSwitched(settings, switchedOn: false);

        Assert.Equal(string.Empty, settings.LastCloudBackupProblem);
        Assert.Null(settings.LastCloudBackupUtc);
    }

    [Fact]
    public void Unticking_a_service_after_a_good_copy_keeps_the_time()
    {
        // The copy that reached every service reached the ones left as well.
        var settings = new AppSettings { LastCloudBackupUtc = Now.AddHours(-1), LastCloudBackupProblem = string.Empty };

        CloudBackupStatus.ServiceSwitched(settings, switchedOn: false);

        Assert.Equal(Now.AddHours(-1), settings.LastCloudBackupUtc);
        Assert.Equal(string.Empty, settings.LastCloudBackupProblem);
    }

    // --- "In a day" is the club night --------------------------------------

    [Fact]
    public void Five_races_either_side_of_midnight_are_one_night_for_Iron_Driver()
    {
        // Saturday 17 October 2026: three before midnight, two after.
        var history = new[]
        {
            Race(Local(17, 20, 0), Entry("ALICE", 1), Entry("BOB", 2)),
            Race(Local(17, 21, 0), Entry("ALICE", 1), Entry("BOB", 2)),
            Race(Local(17, 22, 30), Entry("ALICE", 1), Entry("BOB", 2)),
            Race(Local(18, 0, 5), Entry("ALICE", 2), Entry("BOB", 1)),
        };

        var tonight = Race(Local(18, 0, 45), Entry("ALICE", 2), Entry("BOB", 1));

        var unlocks = Evaluate(tonight, history);

        Assert.Contains(unlocks, u => u.Transponder == "ALICE" && u.Badge.Id == AchievementCatalogue.IronDriver);
    }

    [Fact]
    public void Two_wins_either_side_of_midnight_are_Double_Trouble()
    {
        var history = new[] { Race(Local(17, 23, 30), Entry("ALICE", 1), Entry("BOB", 2)) };
        var tonight = Race(Local(18, 0, 30), Entry("ALICE", 1), Entry("BOB", 2));

        var unlocks = Evaluate(tonight, history);

        Assert.Contains(unlocks, u => u.Transponder == "ALICE" && u.Badge.Id == AchievementCatalogue.DoubleTrouble);
    }

    [Fact]
    public void A_Saturday_night_that_runs_past_midnight_is_not_a_weekend()
    {
        var history = new[] { Race(Local(17, 22, 30), Entry("ALICE", 1), Entry("BOB", 2)) };
        var tonight = Race(Local(18, 0, 15), Entry("ALICE", 1), Entry("BOB", 2));

        var unlocks = Evaluate(tonight, history);

        Assert.DoesNotContain(unlocks, u => u.Badge.Id == AchievementCatalogue.WeekendWarrior);
    }

    [Fact]
    public void Saturday_night_and_Sunday_afternoon_are_still_a_weekend()
    {
        var history = new[] { Race(Local(17, 22, 30), Entry("ALICE", 1), Entry("BOB", 2)) };
        var tonight = Race(Local(18, 14, 0), Entry("ALICE", 1), Entry("BOB", 2));

        var unlocks = Evaluate(tonight, history);

        Assert.Contains(unlocks, u => u.Transponder == "ALICE" && u.Badge.Id == AchievementCatalogue.WeekendWarrior);
    }

    // --- Helpers ---------------------------------------------------------------

    /// <summary>A start time on the clubhouse clock in October 2026, as the UTC a saved race keeps.</summary>
    private static DateTime Local(int day, int hour, int minute) =>
        new DateTime(2026, 10, day, hour, minute, 0, DateTimeKind.Local).ToUniversalTime();

    private static SessionRecord Race(DateTime startedUtc, params SessionResultEntry[] results) => new()
    {
        Label = "Heat",
        SessionType = RaceSessionType.Race,
        TrackLayoutName = "Club Oval",
        StartedAtUtc = startedUtc,
        EndedAtUtc = startedUtc.AddMinutes(6),
        Results = results.ToList(),
    };

    private static SessionResultEntry Entry(string transponder, int position) => new()
    {
        Transponder = transponder,
        DriverName = transponder,
        Position = position,
        StartPosition = position,
        Laps = 12,
        ElapsedMs = 300_000 + position * 1_000,
        FastLapMs = 10_000 + position * 100,
        LapTimesMs = Enumerable.Repeat(10_500L + position * 100, 11).Append(10_000L + position * 100).Prepend(3_000L).ToList(),
    };

    private static List<AchievementUnlock> Evaluate(SessionRecord record, params SessionRecord[] history) =>
        AchievementEvaluator.EvaluateFinishedRace(record, history, AchievementStore.Load());
}
