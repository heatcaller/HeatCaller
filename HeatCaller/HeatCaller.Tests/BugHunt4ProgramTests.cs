namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 4, the program: Redo Remaining Heats (RaceControlControl.
/// RedealRemainingHeats) must not deal a driver into a round they have
/// already raced, and must mix evenly split classes between rounds the way
/// building the heats does; and a best-consecutive class that counts more
/// than one heat must rank drivers with no full run on their best heats.
/// </summary>
public sealed class BugHunt4ProgramTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    private readonly RoundDefinitionStore _rounds = RoundDefinitionStore.Load();
    private readonly ProfileStore _profiles = ProfileStore.Load();
    private readonly List<SessionRecord> _sessions = new();

    private const string Stock = "Stock";

    public void Dispose() => _data.Dispose();

    // --- Redo Remaining Heats: drivers who have already raced the round -----

    [Fact]
    public void Redo_after_part_of_a_round_deals_its_other_heats_only_the_drivers_who_have_not_raced_it()
    {
        var q1h1 = Heat("Q1 Heat 1", recorded: true, "A", "B", "C", "D", "E", "F", "G", "H");
        var q1h2 = Heat("Q1 Heat 2");

        var dealt = RaceControlControl.RedealRemainingHeats(new[] { q1h1, q1h2 }, Drivers("ABCDEFGHIJKLMNOP"));

        Assert.Equal(Drivers("IJKLMNOP"), Sorted(dealt[q1h2.Id]));
        Assert.False(dealt.ContainsKey(q1h1.Id));
    }

    [Fact]
    public void Redo_on_a_one_round_night_does_not_put_the_drivers_from_Heat_1_into_Heat_2()
    {
        var heat1 = Heat("Heat 1", recorded: true, "A", "B", "C", "D", "E", "F", "G", "H");
        var heat2 = Heat("Heat 2");

        var dealt = RaceControlControl.RedealRemainingHeats(new[] { heat1, heat2 }, Drivers("ABCDEFGHIJKLMNOP"));

        Assert.Equal(Drivers("IJKLMNOP"), Sorted(dealt[heat2.Id]));
    }

    [Fact]
    public void Redo_after_the_first_of_three_heats_shares_the_rest_of_the_class_evenly_between_the_other_two()
    {
        var h1 = Heat("Q1 Heat 1", recorded: true, "A", "B", "C", "D", "E", "F");
        var h2 = Heat("Q1 Heat 2");
        var h3 = Heat("Q1 Heat 3");

        var dealt = RaceControlControl.RedealRemainingHeats(new[] { h1, h2, h3 }, Drivers("ABCDEFGHIJKLMNOPQR"));

        Assert.DoesNotContain(dealt[h2.Id].Concat(dealt[h3.Id]), t => h1.AssignedTransponders.Contains(t));
        Assert.Equal(Drivers("GHIJKLMNOPQR"), Sorted(dealt[h2.Id].Concat(dealt[h3.Id])));
        Assert.True(Math.Abs(dealt[h2.Id].Count - dealt[h3.Id].Count) <= 1);
    }

    [Fact]
    public void Redo_leaves_a_heat_as_it_is_when_everyone_checked_in_has_already_raced_its_round()
    {
        var q1h1 = Heat("Q1 Heat 1", recorded: true, "A", "B", "C", "D");
        var q1h2 = Heat("Q1 Heat 2", recorded: false, "X", "Y");
        var q2h1 = Heat("Q2 Heat 1");

        var dealt = RaceControlControl.RedealRemainingHeats(new[] { q1h1, q1h2, q2h1 }, Drivers("ABCD"));

        Assert.False(dealt.ContainsKey(q1h2.Id));
        Assert.Equal(Drivers("ABCD"), Sorted(dealt[q2h1.Id]));
    }

    [Fact]
    public void Redo_still_deals_everyone_into_a_heat_added_by_hand_beside_numbered_rounds()
    {
        // "Heat 5" belongs to no particular round here (Move Drivers Between
        // Heats reads it the same way), so nobody is kept out of it.
        var q1h1 = Heat("Q1 Heat 1", recorded: true, "A", "B", "C", "D");
        var q1h2 = Heat("Q1 Heat 2");
        var extra = Heat("Heat 5");

        var dealt = RaceControlControl.RedealRemainingHeats(new[] { q1h1, q1h2, extra }, Drivers("ABCDEFGH"));

        Assert.Equal(Drivers("EFGH"), Sorted(dealt[q1h2.Id]));
        Assert.Equal(Drivers("ABCDEFGH"), Sorted(dealt[extra.Id]));
    }

    // --- Redo Remaining Heats: evenly split classes ---------------------------

    [Theory]
    [InlineData("Q{0} Heat {1}")]
    [InlineData("Race {0} Heat {1}")]
    public void Redo_deals_an_evenly_split_class_into_new_groups_each_round_as_building_the_heats_does(string pattern)
    {
        var roster = Enumerable.Range(1, 16).Select(i => $"T{i:00}").ToList();
        var q1 = RaceProgramControl.DealQualifyingHeats(roster, 8, 0);

        var day = new List<RoundDefinition>
        {
            Heat(string.Format(pattern, 1, 1), recorded: true, q1[0].ToArray()),
            Heat(string.Format(pattern, 1, 2), recorded: true, q1[1].ToArray()),
            Heat(string.Format(pattern, 2, 1)),
            Heat(string.Format(pattern, 2, 2)),
            Heat(string.Format(pattern, 3, 1)),
            Heat(string.Format(pattern, 3, 2)),
        };

        var dealt = RaceControlControl.RedealRemainingHeats(day, roster);

        var q2 = new List<List<string>> { dealt[day[2].Id], dealt[day[3].Id] };
        var q3 = new List<List<string>> { dealt[day[4].Id], dealt[day[5].Id] };

        Assert.Equal(3, new[] { Groups(q1), Groups(q2), Groups(q3) }.Distinct().Count());

        // The very heats a build would have dealt for Q2 and Q3.
        Assert.Equal(RaceProgramControl.DealQualifyingHeats(roster, 8, 1), q2);
        Assert.Equal(RaceProgramControl.DealQualifyingHeats(roster, 8, 2), q3);
    }

    [Theory]
    [InlineData(9, 4)]
    [InlineData(5, 4)]
    [InlineData(16, 2)]
    [InlineData(3, 3)]
    public void Dealing_into_a_set_number_of_heats_fills_every_one_of_them_and_keeps_them_level(int drivers, int heatCount)
    {
        var roster = Enumerable.Range(1, drivers).Select(i => $"T{i:00}").ToList();

        for (var round = 0; round < 3; round++)
        {
            var heats = RaceProgramControl.DealQualifyingHeatsInto(roster, heatCount, round);

            Assert.Equal(heatCount, heats.Count);
            Assert.All(heats, h => Assert.NotEmpty(h));
            Assert.True(heats.Max(h => h.Count) - heats.Min(h => h.Count) <= 1);
            Assert.Equal(roster, heats.SelectMany(h => h).OrderBy(t => t, StringComparer.Ordinal));
        }
    }

    // --- Best consecutive laps, best N, nobody with a full run ---------------

    [Fact]
    public void Best_two_heats_with_no_full_run_count_the_heats_with_the_most_laps_not_the_earliest()
    {
        RunHeat("Q1", Result("A", 1, 15_000, 1_400), Result("B", 2, 26_000, 1_400, 12_000));
        RunHeat("Q2", Result("A", 1, 15_000, 1_400), Result("B", 1, 15_000, 1_400));
        RunHeat("Q3", Result("A", 3, 26_000, 1_400, 12_000, 12_000), Result("B", 0, 0));

        var entries = Compute(new RaceClass
        {
            Name = Stock,
            QualifyingMethod = QualifyingMethod.BestConsecutiveLaps,
            ConsecutiveLapCount = 3,
            CountBestRounds = 2,
        });

        // A's best two are Q3 + one lap: 4 laps. B's are 2 + 1: 3 laps.
        Assert.Equal(new[] { "A", "B" }, entries.Select(e => e.Transponder).ToArray());

        var a = entries.Single(e => e.Transponder == "A");
        Assert.Equal(4, a.BestLaps);
        Assert.DoesNotContain("Q3", a.DroppedSummary);
    }

    // --- Helpers --------------------------------------------------------------

    private static RoundDefinition Heat(string label, bool recorded = false, params string[] drivers) => new()
    {
        ClassName = Stock,
        RoundLabel = label,
        Kind = RoundKind.Qualifier,
        LinkedSessionId = recorded ? Guid.NewGuid().ToString("N") : null,
        AssignedTransponders = drivers.ToList(),
    };

    private static List<string> Drivers(string letters) => letters.Select(c => c.ToString()).ToList();

    private static List<string> Sorted(IEnumerable<string> drivers) =>
        drivers.OrderBy(t => t, StringComparer.Ordinal).ToList();

    /// <summary>The groups of a round, ignoring heat numbers and grid order.</summary>
    private static string Groups(List<List<string>> heats) =>
        string.Join(" | ", heats.Select(h => string.Join(",", h.OrderBy(t => t, StringComparer.Ordinal)))
            .OrderBy(g => g, StringComparer.Ordinal));

    private static SessionResultEntry Result(string transponder, int laps, long elapsedMs, params long[] lapTimes) => new()
    {
        Transponder = transponder,
        DriverName = transponder,
        Laps = laps,
        ElapsedMs = elapsedMs,
        LapTimesMs = lapTimes.ToList(),
        FastLapMs = lapTimes.Skip(1).DefaultIfEmpty(0).Min(),
    };

    /// <summary>A heat as the app leaves it once raced: in the store, with its session saved and linked.</summary>
    private void RunHeat(string label, params SessionResultEntry[] results)
    {
        var round = new RoundDefinition { ClassName = Stock, RoundLabel = label, Kind = RoundKind.Qualifier };
        _rounds.Add(round);

        var session = new SessionRecord
        {
            Label = label,
            SessionType = RaceSessionType.Race,
            RoundId = round.Id,
            StartedAtUtc = DateTime.UtcNow,
            Results = results.ToList(),
        };

        _sessions.Add(session);
        Assert.True(_rounds.MarkRecorded(round.Id, session.Id));
    }

    private List<QualifyingCalculator.Entry> Compute(RaceClass raceClass) =>
        QualifyingCalculator.Compute(Stock, _rounds, SessionHistoryStore.FromSessions(_sessions), _profiles, raceClass, null, null);
}
