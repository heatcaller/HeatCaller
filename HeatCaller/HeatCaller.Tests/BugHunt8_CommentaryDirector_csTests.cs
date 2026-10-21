using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 8, CommentaryDirector (20 Oct 2026): the programme's colour
/// requests must not move the flag-time stop-set counter, and the club's
/// lowest tier must reach the budget.
/// </summary>
public sealed class BugHunt8_CommentaryDirector_csTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static CommentaryDirector NewDirector(AppSettings settings)
    {
        var commentary = new CommentaryService(ProfileStore.Load(), settings);
        return new CommentaryDirector(commentary, settings, () => new DateTime(2026, 10, 20, 19, 0, 0));
    }

    private static FieldInfo Field(string name) =>
        typeof(CommentaryDirector).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(name);

    /// <summary>
    /// A cancelled token makes ForTheProgrammeAsync stop at its wait for the
    /// pass - after the race-over transition, before any model call.
    /// </summary>
    private static async Task AskForColour(CommentaryDirector director)
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => director.ForTheProgrammeAsync(cts.Token));
    }

    [Fact]
    public async Task A_second_colour_request_after_the_flag_leaves_the_stop_set_marker_alone()
    {
        var director = NewDirector(new AppSettings { CommentaryEnabled = true });
        director.NewMeeting("day-1");
        director.RaceStarted("heat-1", "Box Stock");
        director.RaceFinished();

        // Stand-in for "a break began after the flag": the marker no longer
        // matches the live count. A colour request must not re-stamp it.
        var breaks = Field("_breaksAtFlag");
        breaks.SetValue(director, -99);

        await AskForColour(director);

        Assert.Equal(-99, (int)breaks.GetValue(director)!);
    }

    [Fact]
    public async Task A_colour_request_before_the_tick_still_ends_the_race()
    {
        var director = NewDirector(new AppSettings { CommentaryEnabled = true });
        director.NewMeeting("day-1");
        director.RaceStarted("heat-1", "Box Stock");

        await AskForColour(director);

        Assert.False((bool)Field("_racing").GetValue(director)!);
        Assert.True((bool)Field("_afterARace").GetValue(director)!);
    }

    [Theory]
    [InlineData((int)SegmentTier.Colour, SegmentTier.Colour)]
    [InlineData((int)SegmentTier.Essential, SegmentTier.Essential)]
    [InlineData(99, SegmentTier.Flourish)]
    [InlineData(-5, SegmentTier.Essential)]
    public void The_clubs_lowest_tier_reaches_the_budget(int stored, SegmentTier expected)
    {
        var director = NewDirector(new AppSettings { CommentaryEnabled = true, CommentaryLowestTier = stored });

        director.NewMeeting("day-1");

        Assert.Equal(expected, director.Budget.LowestTier);
    }
}
