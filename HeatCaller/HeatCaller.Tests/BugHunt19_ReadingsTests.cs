namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 19, two places where the page contradicted itself.
///
/// #26: Head to Head's "just shades it" reading, with the pace even,
/// printed the score in A–B order, so "Sam just shades it at the flag,
/// 4–5" read as Sam behind. The leader's count now comes first, as in the
/// line beside it and in Leads().
///
/// #31: a championship that ended level on points named the tiebreak's
/// pick "Champion" on its card, while Can they still win? beside it said
/// the season ended level. The card now says "Level" and names them all.
/// </summary>
public sealed class BugHunt19_ReadingsTests
{
    [Fact]
    public void Just_shades_it_with_even_pace_puts_the_leaders_count_first()
    {
        var pair = new RaceCoach.PairRecord { A = "Leo", B = "Sam", Shared = 9, AheadA = 4, AheadB = 5 };

        Assert.Equal(
            "Sam just shades it at the flag, 5–4, and the pace is even too. Every race is a fresh fight.",
            RaceCoach.Reading(pair));
    }

    [Fact]
    public void Just_shades_it_with_one_quicker_still_puts_the_leaders_count_first()
    {
        var pair = new RaceCoach.PairRecord { A = "Leo", B = "Sam", Shared = 9, AheadA = 4, AheadB = 5, QuickerA = 3, QuickerB = 6 };

        Assert.StartsWith("Sam just shades it at the flag, 5–4, but Sam", RaceCoach.Reading(pair));
    }

    private static SeasonStandingEntry Entry(string name, int points, int wins) =>
        new() { DriverName = name, TotalPoints = points, Wins = wins };

    [Fact]
    public void A_finished_season_level_on_points_is_not_given_a_champion_on_its_card()
    {
        var standings = new List<SeasonStandingEntry> { Entry("Alice", 120, 4), Entry("Bob", 120, 2), Entry("Cy", 90, 1) };

        Assert.Equal(("Level ", "Alice and Bob"), SeasonsControl.CardLeader(standings, finished: true));
    }

    [Fact]
    public void Three_level_at_the_end_are_all_named()
    {
        var standings = new List<SeasonStandingEntry> { Entry("Alice", 120, 4), Entry("Bob", 120, 2), Entry("Cy", 120, 1) };

        Assert.Equal(("Level ", "Alice, Bob and Cy"), SeasonsControl.CardLeader(standings, finished: true));
    }

    [Fact]
    public void A_clear_winner_is_still_champion_and_a_running_season_still_has_a_leader()
    {
        var clear = new List<SeasonStandingEntry> { Entry("Alice", 130, 4), Entry("Bob", 120, 2) };
        var tied = new List<SeasonStandingEntry> { Entry("Alice", 120, 4), Entry("Bob", 120, 2) };

        Assert.Equal(("Champion ", "Alice"), SeasonsControl.CardLeader(clear, finished: true));
        Assert.Equal(("Leader ", "Alice"), SeasonsControl.CardLeader(tied, finished: false));
        Assert.Equal((string.Empty, string.Empty), SeasonsControl.CardLeader(new List<SeasonStandingEntry>(), finished: true));
    }
}
