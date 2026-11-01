namespace HeatCaller.Tests;

/// <summary>
/// The running order a position report reads, and the time it gives second
/// place (1 Nov 2026).
///
/// The report is worded the instant the leader crosses and then waits for
/// the speaker. On a short Mini-Z lap the cars behind cross in that time, so
/// it named a car second that the screen already had third, and gave second
/// place a lap-old time. It is now read again just before it is spoken, and
/// worded again when anything in it has moved.
/// </summary>
public sealed class PositionReportOrderTests
{
    // A five-car-length scrap behind a clear leader: Ann leads, Bob a second
    // back, Cal 1.3 seconds behind Bob. Six-second laps.
    private readonly Dictionary<string, Dictionary<int, long>> _crossings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RaceStanding> _board = new(StringComparer.OrdinalIgnoreCase);

    private void Cross(string who, int lap, double seconds)
    {
        var ms = (long)Math.Round(seconds * 1000);

        if (!_crossings.TryGetValue(who, out var laps))
        {
            laps = new Dictionary<int, long>();
            _crossings[who] = laps;
        }

        laps[lap] = ms;
        _board[who] = new RaceStanding { Transponder = who, Name = who, Laps = lap, ElapsedMs = ms };
    }

    private long? CrossingMs(string who, int lap) =>
        _crossings.TryGetValue(who, out var laps) && laps.TryGetValue(lap, out var t) ? t : null;

    private List<PositionReportOrder.Row> ReadTop(int depth = 3, Func<string, bool>? leaveOut = null) =>
        PositionReportOrder.Read(PositionReportOrder.Rank(_board.Values, leaveOut), depth, CrossingMs);

    private void FourLapsOfTheScrap()
    {
        for (var lap = 1; lap <= 4; lap++)
        {
            Cross("Ann", lap, 6.0 * lap);
            Cross("Bob", lap, 6.0 * lap + 1.0);
            Cross("Cal", lap, 6.0 * lap + 2.3);
        }
    }

    private static string[] Names(IEnumerable<PositionReportOrder.Row> rows) =>
        rows.Select(r => r.Standing.Transponder).ToArray();

    [Fact]
    public void A_car_that_passes_second_place_and_crosses_first_is_read_second()
    {
        FourLapsOfTheScrap();

        // The leader crosses the fifth lap: the report is worded now.
        Cross("Ann", 5, 30.0);
        var worded = ReadTop();
        Assert.Equal(new[] { "Ann", "Bob", "Cal" }, Names(worded));

        // While it waits for the speaker, Cal gets by Bob and is over the
        // line first. The board now has Cal second.
        Cross("Cal", 5, 31.2);
        Cross("Bob", 5, 31.5);
        var now = ReadTop();

        Assert.Equal(new[] { "Ann", "Cal", "Bob" }, Names(now));
        Assert.True(PositionReportOrder.Moved(worded, now), "the report would still name Bob second");
    }

    [Fact]
    public void The_time_given_for_second_place_is_the_newest_one()
    {
        FourLapsOfTheScrap();
        Cross("Ann", 5, 30.0);
        var worded = ReadTop();

        // A second back at the end of lap four - a lap old by the time it is said.
        Assert.Equal(1_000, worded[1].Gap.GapMs);

        // Bob crosses the fifth lap 1.8 seconds behind before the report is heard.
        Cross("Bob", 5, 31.8);
        var now = ReadTop();

        Assert.Equal(new[] { "Ann", "Bob", "Cal" }, Names(now));
        Assert.Equal(1_800, now[1].Gap.GapMs);
        Assert.True(PositionReportOrder.Moved(worded, now), "the report would still give Bob last lap's gap");
    }

    [Fact]
    public void Nothing_crossed_while_it_waited_so_the_words_stand()
    {
        FourLapsOfTheScrap();
        Cross("Ann", 5, 30.0);
        var worded = ReadTop();

        Assert.False(PositionReportOrder.Moved(worded, ReadTop()));
    }

    [Fact]
    public void A_car_further_down_than_the_report_reads_does_not_reword_it()
    {
        FourLapsOfTheScrap();
        Cross("Dee", 3, 21.0);
        Cross("Ann", 5, 30.0);
        var worded = ReadTop();

        // Fourth place crossing changes nothing a top-three report says.
        Cross("Dee", 4, 30.5);
        Assert.False(PositionReportOrder.Moved(worded, ReadTop()));
    }

    [Fact]
    public void Second_place_is_timed_to_the_leader_and_third_to_second()
    {
        FourLapsOfTheScrap();
        Cross("Ann", 5, 30.0);
        var rows = ReadTop();

        Assert.Equal(new RaceGap(0, null), rows[0].Gap);
        Assert.Equal(1_000, rows[1].Gap.GapMs);
        Assert.Equal(1_300, rows[2].Gap.GapMs);
    }

    [Fact]
    public void A_lapped_car_is_given_its_laps_down_not_a_time()
    {
        FourLapsOfTheScrap();

        // Ed is a full lap and more behind: Ann has crossed twice since Ed did.
        Cross("Ed", 3, 23.5);
        Cross("Ann", 5, 30.0);
        var rows = ReadTop(depth: 4);

        Assert.Equal("Ed", rows[3].Standing.Transponder);
        Assert.Equal(1, rows[3].Gap.LapsDown);
        Assert.Equal(1, rows[3].VsLeader.LapsDown);
    }

    [Fact]
    public void A_car_left_out_is_not_read()
    {
        FourLapsOfTheScrap();
        Cross("Ann", 5, 30.0);

        var rows = ReadTop(leaveOut: t => t == "Ann");

        Assert.Equal(new[] { "Bob", "Cal" }, Names(rows));
        Assert.Equal(new RaceGap(0, null), rows[0].Gap);
    }

    [Fact]
    public void A_penalty_given_while_it_waited_rewords_it()
    {
        FourLapsOfTheScrap();
        Cross("Ann", 5, 30.0);
        var worded = ReadTop();

        // Half a second on Bob from race control: same laps, same place,
        // more time - so the time said for him has changed.
        _board["Bob"] = new RaceStanding { Transponder = "Bob", Name = "Bob", Laps = 4, ElapsedMs = 25_500 };
        var now = ReadTop();

        Assert.Equal(new[] { "Ann", "Bob", "Cal" }, Names(now));
        Assert.True(PositionReportOrder.Moved(worded, now));
    }
}
