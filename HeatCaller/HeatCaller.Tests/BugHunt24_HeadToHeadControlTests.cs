namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, Head to Head's career table. Its "Fastest lap of session" row
/// only counted which of the two was quicker, so it read like the Matchup
/// card's "Fastest lap of the race" when it was really the card's "Quicker
/// best lap" - and it handed a best lap tied to the millisecond to driver B,
/// where the card counts a tie for neither.
/// </summary>
public sealed class BugHunt24_HeadToHeadControlTests
{
    [Fact]
    public void Career_row_is_named_as_the_matchup_card_names_the_same_measure()
    {
        Assert.Equal("Quicker best lap", HeadToHeadControl.QuickerBestLapRow);
    }

    [Theory]
    [InlineData(20_000, 21_000, 1)]
    [InlineData(21_000, 20_000, -1)]
    [InlineData(20_500, 20_500, 0)]
    [InlineData(0, 20_000, 0)]
    [InlineData(20_000, 0, 0)]
    public void A_tied_best_lap_counts_for_neither_driver(long a, long b, int expected)
    {
        Assert.Equal(expected, HeadToHeadControl.QuickerLapSide(a, b));
    }

    [Theory]
    [InlineData(20_000, 21_000)]
    [InlineData(21_000, 20_000)]
    [InlineData(20_500, 20_500)]
    [InlineData(0, 20_000)]
    public void Career_row_agrees_with_the_matchup_cards_rule(long a, long b)
    {
        var fa = new RaceCoach.Finisher(new SessionResultEntry { Transponder = "A", Laps = 10, FastLapMs = a }, 1);
        var fb = new RaceCoach.Finisher(new SessionResultEntry { Transponder = "B", Laps = 10, FastLapMs = b }, 2);

        var card = RaceCoach.RowBeat(fa, fb, RaceCoach.Beat.QuickerBestLap);
        var expected = card switch { true => 1, false => -1, null => 0 };

        Assert.Equal(expected, HeadToHeadControl.QuickerLapSide(a, b));
    }
}
