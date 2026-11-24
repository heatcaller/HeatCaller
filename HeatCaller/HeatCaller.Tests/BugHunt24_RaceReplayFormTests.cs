namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the race replay.
///
/// F260: laps counted by hand have no lap time, so the replay cannot play
/// them, and it said nothing - a winner whose transponder died after lap 8
/// of 20 was drawn finishing last. The lanes still draw only what was
/// measured; the replay now marks that driver and says those laps are not
/// played back.
///
/// F261: each tick moved the race on a fixed 33 ms times the speed, however
/// late the tick came, so "1x" ran slower than the race did.
/// </summary>
public sealed class BugHunt24_RaceReplayFormTests
{
    private static SessionResultEntry Timed(int position, int laps) => new()
    {
        Position = position,
        DriverName = $"Driver {position}",
        Laps = laps,
        LapTimesMs = Enumerable.Repeat(20_000L, laps).ToList(),
    };

    [Fact]
    public void BugHunt24_LapsCountedByHand_AreMarkedAndTheReplaySaysTheyAreNotPlayedBack()
    {
        // The winner: 20 laps, 12 of them by hand, so only 8 timed.
        var winner = new SessionResultEntry
        {
            Position = 1,
            DriverName = "Sam",
            Laps = 20,
            LapsByHand = 12,
            LapTimesMs = Enumerable.Repeat(20_000L, 8).ToList(),
        };

        var note = RaceReplayForm.HandLapsNote(new[] { winner, Timed(2, 19) });

        Assert.Contains("counted by hand", note);
        Assert.Contains("not played back", note);
        Assert.StartsWith(HandCountedLaps.Mark, note);

        Assert.Equal("Sam" + HandCountedLaps.Mark, RaceReplayForm.LaneName("Sam", HandCountedLaps.In(winner)));
        Assert.Equal("Driver 2", RaceReplayForm.LaneName("Driver 2", HandCountedLaps.In(Timed(2, 19))));
    }

    [Fact]
    public void BugHunt24_ALapAddedByEditResult_AlsoGetsTheNote()
    {
        var edited = Timed(1, 10);
        edited.Laps = 11;
        edited.AddedLaps = 1;

        Assert.NotEqual(string.Empty, RaceReplayForm.HandLapsNote(new[] { edited }));
    }

    [Fact]
    public void BugHunt24_ARaceWithEveryLapTimed_HasNoNoteAndNoMark()
    {
        Assert.Equal(string.Empty, RaceReplayForm.HandLapsNote(new[] { Timed(1, 20), Timed(2, 19) }));
        Assert.Equal("Driver 1", RaceReplayForm.LaneName("Driver 1", 0));

        // A race saved before lap times were kept has laps and no times, but
        // nothing was counted by hand: no note for that either.
        var old = new SessionResultEntry { Position = 1, DriverName = "Old", Laps = 20 };
        Assert.Equal(string.Empty, RaceReplayForm.HandLapsNote(new[] { old }));
    }

    [Fact]
    public void BugHunt24_TheReplayMovesOnByTheTimeThatReallyPassed_NotAFixed33Ms()
    {
        // A tick 47 ms after the last moves the race 47 ms at 1x and 188 at
        // 4x - the old code gave 33 and 132 whatever the gap.
        Assert.Equal(47, RaceReplayForm.ReplayStep(47, 1));
        Assert.Equal(188, RaceReplayForm.ReplayStep(47, 4));
        Assert.Equal(16 * 33, RaceReplayForm.ReplayStep(33, 16));

        // Ten seconds of 47 ms ticks at 1x plays ten seconds of race, not
        // about seven.
        var played = 0L;
        for (var real = 0L; real + 47 <= 10_000; real += 47)
        {
            played += RaceReplayForm.ReplayStep(47, 1);
        }

        Assert.InRange(played, 9_950, 10_000);
    }

    [Fact]
    public void BugHunt24_ALongStall_DoesNotLeapTheRaceAhead()
    {
        Assert.Equal(RaceReplayForm.StepCapMs, RaceReplayForm.ReplayStep(5_000, 1));
        Assert.Equal(RaceReplayForm.StepCapMs * 2, RaceReplayForm.ReplayStep(5_000, 2));
        Assert.Equal(0, RaceReplayForm.ReplayStep(-5, 4));
    }
}
