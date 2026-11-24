namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): the recap's show measured its winning margin
/// to a DNF or disqualified car, so a walkover got the bigger open while the
/// words were told there was no finishing margin.
/// </summary>
public sealed class BugHunt24_ShowRunningOrderTests
{
    private static SessionResultEntry Car(int position, string name, int laps, long elapsedMs, string outcome = "") => new()
    {
        Position = position,
        Transponder = position.ToString(),
        DriverName = name,
        Laps = laps,
        ElapsedMs = elapsedMs,
        FastLapMs = 14_500,
        LapTimesMs = Enumerable.Repeat(15_000L, laps).ToList(),
        Outcome = outcome,
    };

    private static SessionRecord Race(params SessionResultEntry[] cars) => new()
    {
        SessionType = RaceSessionType.Race,
        TrackLayoutName = "Club Oval",
        StartedAtUtc = new DateTime(2026, 11, 23, 19, 0, 0, DateTimeKind.Utc),
        EndedAtUtc = new DateTime(2026, 11, 23, 19, 5, 0, DateTimeKind.Utc),
        Results = cars.ToList(),
    };

    [Theory]
    [InlineData(ResultEditor.Dq)]
    [InlineData(ResultEditor.Dnf)]
    public void BugHunt24_ShowDrama_two_car_race_with_an_unclassified_second_has_no_margin(string outcome)
    {
        var race = Race(Car(1, "Ann", 10, 150_000), Car(2, "Bob", 10, 150_200, outcome));

        var drama = ShowDrama.Of(race, layout: null);

        Assert.Null(drama.WinningMarginSeconds);
        Assert.False(drama.Notable);
        Assert.StartsWith("only one classified finisher", CommentaryService.DescribeMargin(race.Results.OrderBy(r => r.Position).ToList()));
    }

    [Fact]
    public void BugHunt24_ShowDrama_margin_is_measured_to_the_first_classified_runner_up()
    {
        var race = Race(
            Car(1, "Ann", 10, 150_000),
            Car(2, "Bob", 10, 150_200, ResultEditor.Dnf),
            Car(3, "Cat", 10, 155_000));

        var drama = ShowDrama.Of(race, layout: null);

        Assert.Equal(5.0, drama.WinningMarginSeconds!.Value, 3);
        Assert.False(drama.Notable);
    }

    [Fact]
    public void BugHunt24_ShowDrama_a_real_photo_finish_is_still_notable()
    {
        var drama = ShowDrama.Of(Race(Car(1, "Ann", 10, 150_000), Car(2, "Bob", 10, 150_200)), layout: null);

        Assert.Equal(0.2, drama.WinningMarginSeconds!.Value, 3);
        Assert.True(drama.Notable);
    }
}
