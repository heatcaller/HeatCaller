namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10 (23 Oct 2026): a closed race day made current to look at
/// its results must not throw away tonight's open room vote.
/// </summary>
public sealed class BugHunt10_RoomVoteTests : IDisposable
{
    private readonly TempAppData _appData = new();

    public void Dispose() => _appData.Dispose();

    private static RaceEvent OpenDay(string id) => new() { Id = id };

    private static RaceEvent ClosedDay(string id) => new() { Id = id, FinishedUtc = DateTime.UtcNow.AddDays(-7) };

    private static VoteOption[] Grid() => new[]
    {
        new VoteOption("A4821B", "Bill Hargrove"),
        new VoteOption("B9930C", "Sam Teague"),
    };

    [Fact]
    public void Looking_at_a_closed_day_mid_night_keeps_tonights_vote_and_its_votes()
    {
        var vote = RoomVote.Load();

        vote.FollowDay(OpenDay("tonight"));
        Assert.Null(vote.OpenFor("a-main", "Stock A-Main", "Stock", Grid()));
        Assert.Null(vote.Cast("phone-1", "A4821B"));

        vote.FollowDay(ClosedDay("last-week"));
        Assert.Equal("tonight", vote.DayId);

        vote.FollowDay(OpenDay("tonight"));

        Assert.Equal("tonight", vote.DayId);
        Assert.Equal("a-main", vote.OnRaceKey);
        Assert.Equal(1, vote.Votes);
    }

    [Fact]
    public void A_real_new_day_still_clears_last_weeks_vote()
    {
        var vote = RoomVote.Load();

        vote.FollowDay(OpenDay("last-week"));
        Assert.Null(vote.OpenFor("a-main", "Stock A-Main", "Stock", Grid()));

        vote.FollowDay(OpenDay("tonight"));

        Assert.Equal("tonight", vote.DayId);
        Assert.Null(vote.OnRaceKey);
    }

    [Fact]
    public void A_vote_opened_before_the_day_survives_looking_at_last_week_then_opening_tonight()
    {
        var vote = RoomVote.Load();

        vote.NewMeeting(null);
        Assert.Null(vote.OpenFor("a-main", "Stock A-Main", "Stock", Grid()));

        vote.FollowDay(ClosedDay("last-week"));
        Assert.Equal(string.Empty, vote.DayId);

        vote.FollowDay(OpenDay("tonight"));

        Assert.Equal("tonight", vote.DayId);
        Assert.Equal("a-main", vote.OnRaceKey);
    }

    [Fact]
    public void Starting_up_on_a_closed_day_clears_last_weeks_vote()
    {
        var vote = RoomVote.Load();

        vote.NewMeeting("last-week");
        Assert.Null(vote.OpenFor("a-main", "Stock A-Main", "Stock", Grid()));

        var lastWeek = ClosedDay("last-week");
        vote.StartUp(lastWeek, id => id == lastWeek.Id ? lastWeek : null);

        Assert.Equal(string.Empty, vote.DayId);
        Assert.Null(vote.OnRaceKey);
    }

    [Fact]
    public void Restarting_mid_night_while_looking_at_last_week_keeps_tonights_vote()
    {
        var tonight = OpenDay("tonight");
        var lastWeek = ClosedDay("last-week");
        var vote = RoomVote.Load();

        vote.FollowDay(tonight);
        Assert.Null(vote.OpenFor("a-main", "Stock A-Main", "Stock", Grid()));

        vote.StartUp(lastWeek, id => id == tonight.Id ? tonight : id == lastWeek.Id ? lastWeek : null);

        Assert.Equal("tonight", vote.DayId);
        Assert.Equal("a-main", vote.OnRaceKey);
    }

    [Fact]
    public void Starting_up_on_tonights_open_day_puts_the_vote_on_it()
    {
        var vote = RoomVote.Load();

        vote.StartUp(null, _ => null);
        Assert.Null(vote.OpenFor("a-main", "Stock A-Main", "Stock", Grid()));

        vote.StartUp(OpenDay("tonight"), _ => null);

        Assert.Equal("tonight", vote.DayId);
        Assert.Equal("a-main", vote.OnRaceKey);
    }

    // 6 Nov 2026: a vote still open when its result is filed is closed
    // by Finished, and that must wake the relay like any other close.
    [Fact]
    public void Filing_the_result_of_a_still_open_vote_raises_Closed_once()
    {
        var vote = RoomVote.Load();
        var closed = 0;
        vote.Closed += () => closed++;

        vote.FollowDay(OpenDay("tonight"));
        Assert.Null(vote.OpenFor("a-main", "Stock A-Main", "Stock", Grid()));

        vote.Finished("a-main", "A4821B", "Bill Hargrove");
        Assert.Equal(1, closed);

        vote.Finished("a-main", "A4821B", "Bill Hargrove");
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Filing_the_result_of_a_vote_closed_at_the_green_does_not_raise_Closed_again()
    {
        var vote = RoomVote.Load();

        vote.FollowDay(OpenDay("tonight"));
        Assert.Null(vote.OpenFor("a-main", "Stock A-Main", "Stock", Grid()));
        vote.StartedRace("a-main");

        var closed = 0;
        vote.Closed += () => closed++;

        vote.Finished("a-main", "A4821B", "Bill Hargrove");

        Assert.Equal(0, closed);
    }
}
