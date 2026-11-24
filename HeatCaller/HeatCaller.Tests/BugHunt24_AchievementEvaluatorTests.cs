namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), AchievementEvaluator:
///  - #76: a credited start lap's dash (the entry after its zero) is not a
///    flying lap for Finished Strong, Machine, Swiss Watch, Unbroken or the
///    clean runs;
///  - #77: old sessions saved with no start time no longer stop a race's
///    badges and running totals being saved when a personal best walks them.
/// </summary>
public sealed class BugHunt24_AchievementEvaluatorTests : IDisposable
{
    private const string Layout = "Club Oval";

    private static readonly DateTime Day = new(2026, 9, 10, 18, 0, 0, DateTimeKind.Utc);

    // Declared first: everything below reads and writes where it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static SessionRecord Race(DateTime start, long[] laps, bool startLapCredited = false, RaceSessionType type = RaceSessionType.Race) => new()
    {
        SessionType = type,
        Label = type == RaceSessionType.Practice ? "Practice" : "Heat",
        TrackLayoutName = Layout,
        StartedAtUtc = start,
        EndedAtUtc = start == default ? default : start.AddMinutes(5),
        RecordsIncidents = true,
        Results = new List<SessionResultEntry>
        {
            new()
            {
                Position = 1,
                Transponder = "A",
                DriverName = "Driver A",
                CarModel = "Red",
                Laps = laps.Length,
                FastLapMs = laps.Skip(startLapCredited ? 2 : 1).Min(),
                ElapsedMs = laps.Sum(),
                LapTimesMs = laps.ToList(),
                StartLapCredited = startLapCredited,
            },
        },
    };

    private static long[] Repeat(long lapMs, int count) => Enumerable.Repeat(lapMs, count).ToArray();

    private static List<string> BadgesFor(SessionRecord race) =>
        AchievementEvaluator.EvaluateFinishedRace(race, Array.Empty<SessionRecord>(), AchievementStore.Load())
            .Select(u => u.Badge.Id)
            .ToList();

    // --- #76: the dash after a credited start lap --------------------------

    [Fact]
    public void A_credited_start_lap_with_a_long_dash_then_an_even_race_is_not_Finished_Strong_and_earns_Machine_Swiss_Watch_and_Unbroken()
    {
        // Read 8 s before the green on a 10 s lap: the dash is 18 s, then ten
        // dead-even laps. The dash used to be taken for the first flying lap.
        var laps = new long[] { 0, 18_000 }.Concat(Repeat(10_000, 10)).ToArray();
        var race = Race(Day, laps, startLapCredited: true);

        var unlocks = AchievementEvaluator.EvaluateFinishedRace(race, Array.Empty<SessionRecord>(), AchievementStore.Load());
        var badges = unlocks.Select(u => u.Badge.Id).ToList();

        Assert.DoesNotContain(AchievementCatalogue.FinishedStrong, badges);
        Assert.Contains(AchievementCatalogue.Consistent, badges);
        Assert.Contains(AchievementCatalogue.SwissWatch, badges);
        Assert.Contains(AchievementCatalogue.Unbroken, badges);
        Assert.Equal("10 laps", unlocks.Single(u => u.Badge.Id == AchievementCatalogue.Unbroken).Detail);
    }

    [Fact]
    public void A_credited_start_lap_with_a_short_dash_still_earns_a_real_Finished_Strong()
    {
        // A 3 s dash, then five laps of 11 s and five of 10 s: a second a lap
        // quicker over the back half. The short dash dragged the first half
        // down and refused it.
        var laps = new long[] { 0, 3_000 }.Concat(Repeat(11_000, 5)).Concat(Repeat(10_000, 5)).ToArray();
        var race = Race(Day, laps, startLapCredited: true);

        var unlocks = AchievementEvaluator.EvaluateFinishedRace(race, Array.Empty<SessionRecord>(), AchievementStore.Load());

        var strong = Assert.Single(unlocks, u => u.Badge.Id == AchievementCatalogue.FinishedStrong);
        Assert.Equal("1.000s a lap quicker", strong.Detail);
    }

    [Fact]
    public void Without_a_credited_start_lap_only_the_opening_lap_is_skipped_as_before()
    {
        var laps = new long[] { 11_500 }.Concat(Repeat(10_000, 11)).ToArray();
        var badges = BadgesFor(Race(Day, laps));

        Assert.DoesNotContain(AchievementCatalogue.FinishedStrong, badges);
        Assert.Contains(AchievementCatalogue.Consistent, badges);
        Assert.Contains(AchievementCatalogue.SwissWatch, badges);
        Assert.Contains(AchievementCatalogue.Unbroken, badges);
    }

    // --- #77: sessions saved with no start time ----------------------------

    [Fact]
    public void A_personal_best_over_two_old_sessions_with_no_start_time_still_saves_the_race()
    {
        // Two old races with no start time on the layout, the second quicker
        // than the first, so walking the personal bests asks the second one
        // for its night. Six hours before "no date" threw (in any time zone
        // west of UTC+6) and nothing for the race was saved.
        var history = new List<SessionRecord>
        {
            Race(default, new long[] { 13_000 }.Concat(Repeat(12_000, 5)).ToArray()),
            Race(default, new long[] { 12_000 }.Concat(Repeat(11_000, 5)).ToArray()),
        };

        var tonight = Race(Day, new long[] { 11_000 }.Concat(Repeat(10_000, 5)).ToArray());
        var context = new AchievementContext { RacingAt = RacingSetting.Home };

        var unlocks = AchievementEvaluator.EvaluateFinishedRace(tonight, history, AchievementStore.Load(), context: context);

        Assert.NotEmpty(unlocks);
        Assert.Equal(1, AchievementStore.Load().For("A").RacesFinished);
    }

    [Fact]
    public void A_practice_personal_best_over_two_old_sessions_with_no_start_time_still_saves_the_laps()
    {
        var history = new List<SessionRecord>
        {
            Race(default, new long[] { 13_000 }.Concat(Repeat(12_000, 5)).ToArray()),
            Race(default, new long[] { 12_000 }.Concat(Repeat(11_000, 5)).ToArray()),
        };

        var practice = Race(Day, new long[] { 11_000 }.Concat(Repeat(10_000, 5)).ToArray(), type: RaceSessionType.Practice);
        var context = new AchievementContext { RacingAt = RacingSetting.Home };

        AchievementEvaluator.EvaluatePractice(practice, history, AchievementStore.Load(), context);

        Assert.Equal(6, AchievementStore.Load().For("A").TotalLaps);
    }
}
