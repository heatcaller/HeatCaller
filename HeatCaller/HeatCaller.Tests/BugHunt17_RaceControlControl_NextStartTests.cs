namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), RaceControlControl.NextStart.cs.
///
/// A HOLD NEVER REDREW THE TIMETABLE. NextStartMoved was raised when the
/// start differed from the tick before by more than half a minute. A Hold
/// slides the start one second every tick, so two ticks were never more than
/// a second apart and the event never fired, however long the hold ran. The
/// move is measured from the start last told now.
/// </summary>
public sealed class BugHunt17_RaceControlControl_NextStartTests
{
    private static readonly DateTime Start = new(2026, 11, 13, 19, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void A_start_sliding_a_second_a_tick_is_told_once_it_is_half_a_minute_out()
    {
        RaceControlControl.NextStartFact? told = null;

        Assert.True(RaceControlControl.MovedSinceTold(ref told, Fact(0)));

        var toldAt = new List<int>();

        for (var second = 1; second <= 70; second++)
        {
            if (RaceControlControl.MovedSinceTold(ref told, Fact(second)))
            {
                toldAt.Add(second);
            }
        }

        Assert.Equal(new[] { 31, 62 }, toldAt);
        Assert.Equal(Start.AddSeconds(62), told!.StartsAtUtc);
    }

    [Fact]
    public void A_count_that_only_wobbles_is_not_told_again()
    {
        RaceControlControl.NextStartFact? told = Fact(0);

        for (var tick = 0; tick < 120; tick++)
        {
            Assert.False(RaceControlControl.MovedSinceTold(ref told, Fact(tick % 2)));
        }

        Assert.Equal(Start, told!.StartsAtUtc);
    }

    [Fact]
    public void Another_race_a_push_back_and_the_gap_closing_are_still_told_at_once()
    {
        RaceControlControl.NextStartFact? told = Fact(0);

        Assert.True(RaceControlControl.MovedSinceTold(ref told, Fact(0) with { PushedBackMinutes = 1 }));
        Assert.True(RaceControlControl.MovedSinceTold(ref told, new RaceControlControl.NextStartFact("q2", Start, 1)));
        Assert.True(RaceControlControl.MovedSinceTold(ref told, null));
        Assert.Null(told);
        Assert.False(RaceControlControl.MovedSinceTold(ref told, null));
    }

    [Fact]
    public void The_gap_tick_measures_from_the_start_last_told()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", "RaceControlControl.NextStart.cs"));

        Assert.Contains("if (MovedSinceTold(ref _nextStartTold, fact))", source, StringComparison.Ordinal);
    }

    private static RaceControlControl.NextStartFact Fact(int secondsLater) =>
        new("q1", Start.AddSeconds(secondsLater), 0);

    private static string RepoRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);

            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            throw new DirectoryNotFoundException("No folder with check.cmd in it above " + AppContext.BaseDirectory);
        }
    }
}
