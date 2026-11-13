namespace HeatCaller.Tests;

/// <summary>
/// Bug Hunt 17 (13 Nov 2026): a driver credited with the start lap at the
/// green has two start entries in their saved laps - the zero, and the rest
/// of the start timed from their crossing before the green. Pace and the
/// pack figures counted the second as a flying lap. The result now says who
/// was credited, since a qualifier's laps start with a zero as well and the
/// lap after that one is real.
/// </summary>
public sealed class BugHunt17_PaceAnalysisTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void The_rest_of_a_credited_start_is_not_a_flying_lap()
    {
        var jumper = new SessionResultEntry
        {
            Transponder = "7",
            StartLapCredited = true,
            LapTimesMs = { 0, 4_000, 11_000, 11_200, 11_100 },
        };

        var row = PaceAnalysis.For(jumper);

        Assert.Equal(3, row.CountedLaps);
        Assert.Equal(11_000, row.BestLapMs);
        Assert.Equal(11_100, row.AverageLapMs);
    }

    [Fact]
    public void A_qualifier_s_lap_after_its_zero_still_counts()
    {
        // The same laps, not credited: a qualifier's clock starts at the
        // first crossing, so its start entry is zero and the next is a lap.
        var qualifier = new SessionResultEntry { Transponder = "7", LapTimesMs = { 0, 10_900, 11_000, 11_200 } };

        var row = PaceAnalysis.For(qualifier);

        Assert.Equal(3, row.CountedLaps);
        Assert.Equal(10_900, row.BestLapMs);
    }

    [Fact]
    public void An_ordinary_start_lap_is_left_out_as_before()
    {
        var entry = new SessionResultEntry { Transponder = "7", LapTimesMs = { 2_500, 11_000, 11_200 } };

        Assert.Equal(1, entry.StartSlots(entry.LapTimesMs));
        Assert.Equal(2, PaceAnalysis.For(entry).CountedLaps);

        // A credit withdrawn or edited away, so the laps no longer start with the zero: one start entry.
        var noZero = new SessionResultEntry { StartLapCredited = true, LapTimesMs = { 2_500, 11_000 } };
        Assert.Equal(1, noZero.StartSlots(noZero.LapTimesMs));
    }

    [Fact]
    public void Filing_a_race_keeps_who_was_credited_with_the_start_lap()
    {
        var credited = new RacerStanding { TransponderId = "A", Laps = 3, StartLapCredited = true };
        credited.LapTimesMs.AddRange(new long[] { 0, 4_000, 11_000 });

        var ordinary = new RacerStanding { TransponderId = "B", Laps = 2 };
        ordinary.LapTimesMs.AddRange(new long[] { 2_000, 11_000 });

        var rows = RaceFiling.ResultsFrom(new[] { credited, ordinary }, ProfileStore.Load());

        Assert.True(rows[0].StartLapCredited);
        Assert.False(rows[1].StartLapCredited);
    }

    [Fact]
    public void A_pack_s_best_lap_is_not_the_rest_of_a_credited_start()
    {
        var profiles = ProfileStore.Load();
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "100", DriverName = "Maya" }, out _));

        var packs = BatteryPackStore.Load();
        packs.Upsert(new BatteryPack { Id = "a", Label = "a", OwnerTransponder = "100" });

        var session = new SessionRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Label = "Heat",
            TrackLayoutName = "Club",
            StartedAtUtc = DateTime.UtcNow.AddHours(-1),
            Results = new List<SessionResultEntry>
            {
                new()
                {
                    Transponder = "100",
                    Laps = 7,
                    FastLapMs = 10_000,
                    StartLapCredited = true,
                    LapTimesMs = new List<long> { 0, 4_000, 10_000, 10_100, 10_200, 10_100, 10_000 },
                },
            },
        };

        packs.Tag(session.Id, "100", "a", PackTagSource.DriverHub);

        var report = PackAnalytics.For("100", packs, SessionHistoryStore.FromSessions(new List<SessionRecord> { session }), profiles);

        Assert.Equal(10.0, Assert.Single(report.Packs).BestLap, 3);
    }
}
