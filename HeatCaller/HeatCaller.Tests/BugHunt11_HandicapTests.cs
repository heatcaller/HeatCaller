namespace HeatCaller.Tests;

/// <summary>
/// 6 Nov 2026, Bug hunt 11: a time-credit handicap scales every finisher to
/// the winner's distance and judges who was still running at the end of the
/// racing - and both now come from the finishers alone. A disqualified car
/// with an extra lap, or timed after the flag, used to set them, and could
/// change who won on handicap.
/// </summary>
public sealed class BugHunt11_HandicapTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static HandicapStore Credit(string transponder, double seconds)
    {
        var handicaps = HandicapStore.Load();
        handicaps.Upsert(new DriverHandicap { Transponder = transponder, TimeCreditSeconds = seconds });
        return handicaps;
    }

    private static string[] Finishers(IEnumerable<HandicapCalculator.Adjusted> adjusted) =>
        adjusted.Where(a => a.Result.Outcome != ResultEditor.Dq).Select(a => a.Result.Transponder).ToArray();

    [Fact]
    public void Time_credit_winner_is_the_same_without_the_DQd_car()
    {
        var handicaps = Credit("B", 31);

        // A: 300 s over 30 laps. B: 297 s over 27 laps, 330 s at the same
        // pace over 30, less 31 s of credit = 299 s, so B wins.
        var results = new List<SessionResultEntry>
        {
            new() { Position = 1, Transponder = "A", Laps = 30, ElapsedMs = 300_000 },
            new() { Position = 2, Transponder = "B", Laps = 27, ElapsedMs = 297_000 },
        };

        var adjusted = HandicapCalculator.Apply(results, handicaps, HandicapMode.TimeCredit);

        Assert.Equal(new[] { "B", "A" }, Finishers(adjusted));
    }

    [Fact]
    public void A_DQd_car_with_more_laps_does_not_change_the_time_credit_winner()
    {
        var handicaps = Credit("B", 31);

        // C was disqualified after 31 laps. Scaling to C's 31 laps put A and
        // B level at 310 s and handed A the win on laps.
        var results = new List<SessionResultEntry>
        {
            new() { Position = 1, Transponder = "A", Laps = 30, ElapsedMs = 300_000 },
            new() { Position = 2, Transponder = "B", Laps = 27, ElapsedMs = 297_000 },
            new() { Position = 3, Transponder = "C", Laps = 31, ElapsedMs = 305_000, Outcome = ResultEditor.Dq },
        };

        var adjusted = HandicapCalculator.Apply(results, handicaps, HandicapMode.TimeCredit);

        Assert.Equal(new[] { "B", "A", "C" }, adjusted.Select(a => a.Result.Transponder));
        Assert.Equal(1, adjusted.Single(a => a.Result.Transponder == "B").AdjustedPosition);
    }

    [Fact]
    public void A_DQd_car_timed_late_does_not_move_the_running_at_end_cut_off()
    {
        var handicaps = Credit("B", 12);

        // B finished a lap down, timed at 99 s - within two of B's 11 s laps
        // of A's 100 s, so B was still running: 110 s over 10 laps, less 12 s
        // = 98 s, ahead of A. C, disqualified, was timed on to 125 s; taken
        // as the end of the racing it left neither A nor B "still running",
        // so B dropped behind A on laps.
        var results = new List<SessionResultEntry>
        {
            new() { Position = 1, Transponder = "A", Laps = 10, ElapsedMs = 100_000 },
            new() { Position = 2, Transponder = "B", Laps = 9, ElapsedMs = 99_000 },
            new() { Position = 3, Transponder = "C", Laps = 10, ElapsedMs = 125_000, Outcome = ResultEditor.Dq },
        };

        var withC = HandicapCalculator.Apply(results, handicaps, HandicapMode.TimeCredit);
        var withoutC = HandicapCalculator.Apply(
            results.Where(r => r.Transponder != "C").ToList(), handicaps, HandicapMode.TimeCredit);

        Assert.Equal(new[] { "B", "A" }, Finishers(withoutC));
        Assert.Equal(Finishers(withoutC), Finishers(withC));
        Assert.Equal("C", withC[^1].Result.Transponder);
    }

    [Fact]
    public void A_field_with_no_finishers_still_ranks_without_crashing()
    {
        var handicaps = Credit("B", 30);

        var results = new List<SessionResultEntry>
        {
            new() { Position = 1, Transponder = "A", Laps = 12, ElapsedMs = 120_000, Outcome = ResultEditor.Dnf },
            new() { Position = 2, Transponder = "B", Laps = 11, ElapsedMs = 110_000, Outcome = ResultEditor.Dnf },
            new() { Position = 3, Transponder = "C", Laps = 14, ElapsedMs = 130_000, Outcome = ResultEditor.Dq },
        };

        var adjusted = HandicapCalculator.Apply(results, handicaps, HandicapMode.TimeCredit);

        // Nobody is projected; the DNFs keep the distance order.
        Assert.Equal(new[] { "A", "B", "C" }, adjusted.Select(a => a.Result.Transponder));
    }
}
