namespace HeatCaller.Tests;

/// <summary>
/// The 19:47 demo heat, 8 Nov 2026. The number check threw away seven true
/// record-lap lines because it could not read a thousandth the way the
/// model writes one ("five-oh-nine-six"), and let the recap's wrong
/// "five-oh-six" through for a 5.096. And Dee Ploy's 5.807 was called the
/// race's best lap with Annie Wayout's 5.783 already on the board.
/// </summary>
public sealed class LapTimesAsWrittenTests
{
    private const string RecordFacts =
        "Terry Byte has just set 5.096s, the fastest lap of the race. The previous fastest was Dee Ploy's 5.169s.";

    [Theory]
    [InlineData("a five-oh-nine-six", 5.096)]
    [InlineData("a five oh nine six", 5.096)]
    [InlineData("a five-sixteen-nine", 5.169)]
    [InlineData("a five-twenty-five-eight", 5.258)]
    [InlineData("a five one six nine", 5.169)]
    [InlineData("a five-oh-six", 5.06)]
    public void Reads_a_thousandth_the_way_the_model_writes_it(string text, double expected)
    {
        var figures = CommentaryNumberCheck.FiguresIn(text, forFacts: false).Select(f => Math.Round(f.Value, 3));

        Assert.Equal(new[] { expected }, figures);
    }

    [Theory]
    [InlineData("Terry Byte just dialed in a five-oh-nine-six and that's nearly seven hundredths quicker than Dee Ploy's five-sixteen-nine - he's got the hot hand right now!")]
    [InlineData("Terry Byte absolutely RIPS that lap - five-oh-nine-six, and he's just sliced seven hundredths clean off Dee Ploy's benchmark!")]
    [InlineData("Terry Byte with a five oh nine")]
    [InlineData("Terry Byte with a five ten")]
    [InlineData("Terry Byte with a five oh ninety-six")]
    public void Tonights_true_record_lap_lines_pass(string line)
    {
        Assert.Null(CommentaryNumberCheck.Unsupported(line, RecordFacts));
    }

    [Fact]
    public void A_record_lap_with_its_old_holder_both_said_to_the_thousandth_passes()
    {
        Assert.Null(CommentaryNumberCheck.Unsupported(
            "Dee Ploy just stormed through with a five-sixteen-nine - that's nearly nine hundredths quicker than Terry Byte's five-twenty-five-eight, and she's taken the fastest lap!",
            "Dee Ploy has just set 5.169s, the fastest lap of the race. The previous fastest was Terry Byte's 5.258s."));
    }

    [Fact]
    public void The_recaps_five_oh_six_for_a_5_096_fails_even_with_a_five_and_a_six_in_the_facts()
    {
        const string facts = "5 cars. Terry Byte led 16 laps, Dee Ploy led 6 laps. Fastest lap: Terry Byte 5.096s.";

        Assert.Equal("five-oh-six", CommentaryNumberCheck.Unsupported(
            "Byte dominated early, setting the fastest lap of the entire race with a five-oh-six!", facts));
    }

    [Fact]
    public void A_gap_keeps_its_looser_tolerance()
    {
        Assert.Null(CommentaryNumberCheck.Unsupported("Terry by 1.12 seconds", "Terry leads Dee by 1.1s."));
    }

    // ------------------------------------------------------------------
    // The fastest-lap call is only said while it is still the fastest
    // ------------------------------------------------------------------

    private static RaceStanding Car(string transponder, long fastLapMs) =>
        new() { Transponder = transponder, Name = transponder, FastLapMs = fastLapMs };

    [Fact]
    public void A_quicker_best_on_the_board_beats_the_call()
    {
        var board = new[] { Car("sfewf", 5807), Car("ewrwer", 5783), Car("Terry Byte", 6307) };

        Assert.Equal("ewrwer", AnnouncementService.QuickerBest("sfewf", 5807, board)?.Transponder);
    }

    [Fact]
    public void The_holder_of_the_fastest_lap_is_not_beaten()
    {
        var board = new[] { Car("sfewf", 5807), Car("ewrwer", 5783), Car("Terry Byte", 0) };

        Assert.Null(AnnouncementService.QuickerBest("ewrwer", 5783, board));
    }

    [Fact]
    public void The_drivers_own_later_best_does_not_beat_its_call()
    {
        var board = new[] { Car("sfewf", 5169), Car("ewrwer", 5901) };

        Assert.Null(AnnouncementService.QuickerBest("sfewf", 5807, board));
    }
}
