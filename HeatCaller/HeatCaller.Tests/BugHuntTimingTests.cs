using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt, timing slice: the EasyLap framer resynchronising onto a byte
/// inside a message, and the crash journal dropping a write it held back.
/// </summary>
public sealed class BugHuntTimingTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- EasyLapFramer -------------------------------------------------------------------

    /// <summary>A real pass from rehearsal 3: 84C1C4 at 19,474,124ms, with a sequence byte of our choosing.</summary>
    private static byte[] Pass(byte sequence, byte t1, byte t2, byte t3) =>
        new byte[] { 0x0d, sequence, t1, t2, t3, 0x00, 0x00, 0xcc, 0x26, 0x29, 0x01, 0x3e, 0xf8 };

    /// <summary>The bytes as HID reports of at most four data bytes, the way the decoder sends them.</summary>
    private static IEnumerable<byte[]> Reports(IEnumerable<byte> stream)
    {
        foreach (var chunk in stream.Chunk(4))
        {
            var report = new byte[64];
            report[0] = (byte)chunk.Length;
            chunk.CopyTo(report, 1);
            yield return report;
        }
    }

    [Fact]
    public void A_connection_that_starts_inside_a_pass_does_not_read_a_phantom_car_from_it()
    {
        var passes = new List<DirectLapEvent>();
        var log = new List<string>();
        var framer = new EasyLapFramer(log.Add, passes.Add);

        // The tail of a pass whose sequence byte is 0x0D - the first byte the
        // new connection sees - then a whole pass from another car. Taken on
        // trust, the tail plus the next pass's first byte read as a pass
        // from "C1C400", and the real pass after it was lost.
        var tail = Pass(0x0d, 0x84, 0xc1, 0xc4).Skip(1);
        var next = Pass(0xe4, 0x84, 0xfb, 0xd4);

        foreach (var report in Reports(tail.Concat(next)))
        {
            framer.AddReport(report);
        }

        var pass = Assert.Single(passes);
        Assert.Equal("84FBD4", pass.TransponderId);
        Assert.Equal(19_474_124u, pass.DeviceTimeMs);
        Assert.Contains(log, l => l.StartsWith("[framing] Skipped", StringComparison.Ordinal));
    }

    [Fact]
    public void After_a_clear_the_next_frame_is_checked_before_it_is_believed()
    {
        var passes = new List<DirectLapEvent>();
        var framer = new EasyLapFramer(_ => { }, passes.Add);

        // In step: two whole passes, read as they come.
        foreach (var report in Reports(Pass(0x01, 0x84, 0xc1, 0xc4).Concat(Pass(0x02, 0x84, 0xfb, 0xd4)).Concat(Pass(0x03, 0x84, 0xc1, 0xc4))))
        {
            framer.AddReport(report);
        }

        Assert.Equal(3, passes.Count);

        // A report that could not be handled, then the stream picks up part
        // way through a pass whose sequence byte is 0x0D.
        framer.Clear();
        passes.Clear();

        foreach (var report in Reports(Pass(0x0d, 0x84, 0xc1, 0xc4).Skip(1).Concat(Pass(0x05, 0x84, 0xfb, 0xd4))))
        {
            framer.AddReport(report);
        }

        var pass = Assert.Single(passes);
        Assert.Equal("84FBD4", pass.TransponderId);
    }

    [Fact]
    public void A_pass_that_starts_the_stream_cleanly_is_read_at_once()
    {
        var passes = new List<DirectLapEvent>();
        var framer = new EasyLapFramer(_ => { }, passes.Add);

        // Nothing after it yet - it must not wait for the next message to
        // be called.
        foreach (var report in Reports(Pass(0xe4, 0x84, 0xc1, 0xc4)))
        {
            framer.AddReport(report);
        }

        Assert.Equal("84C1C4", Assert.Single(passes).TransponderId);
    }

    // --- RaceJournal ---------------------------------------------------------------------

    private string JournalPath => _data.PathOf("race-in-progress.json");

    private List<string> JournalledCars() =>
        JsonSerializer.Deserialize<RaceJournal.Entry>(File.ReadAllText(JournalPath))!
            .Standings.Select(s => s.TransponderId).ToList();

    private static void Cross(RaceControlSession race, string car, uint atMs) =>
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = car,
            DeviceTimeMs = atMs,
            SignalHits = 1,
            SignalStrength = 100,
            IsSynthetic = true,
        });

    [Fact]
    public void A_crossing_the_throttle_held_back_is_written_once_the_throttle_allows()
    {
        var race = new RaceControlSession();
        race.Stage();
        race.GreenFlag(0);
        Assert.Equal(RaceState.Racing, race.State);

        var journal = new RaceJournal();

        Cross(race, "AAAAAA", 5_000);
        journal.Record(race, DateTime.UtcNow, string.Empty, force: true);
        Assert.False(journal.IsBehind);

        // The second car, a moment later: held back by the throttle.
        Cross(race, "BBBBBB", 5_300);
        journal.Record(race, DateTime.UtcNow, string.Empty);

        Assert.True(journal.IsBehind);
        Assert.DoesNotContain("BBBBBB", JournalledCars());

        // What the race clock's tick does: ask again, not forced, once the
        // throttle has passed - no further crossing needed.
        Thread.Sleep(500);
        journal.Record(race, DateTime.UtcNow, string.Empty);

        Assert.False(journal.IsBehind);
        Assert.Contains("BBBBBB", JournalledCars());
    }

    [Fact]
    public void Clearing_the_journal_leaves_nothing_owed()
    {
        var race = new RaceControlSession();
        race.Stage();
        race.GreenFlag(0);

        var journal = new RaceJournal();

        Cross(race, "AAAAAA", 5_000);
        journal.Record(race, DateTime.UtcNow, string.Empty, force: true);
        Cross(race, "BBBBBB", 5_300);
        journal.Record(race, DateTime.UtcNow, string.Empty);
        Assert.True(journal.IsBehind);

        journal.Clear();

        Assert.False(journal.IsBehind);
    }
}
