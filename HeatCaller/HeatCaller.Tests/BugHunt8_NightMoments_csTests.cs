namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 8, NightMoments (20 Oct 2026): the Charge moment counted the raw
/// grid slot, ghosts and no-shows included, against a finishing place closed
/// up over the real cars - so it said "four places" where the Charge badge
/// saw two, and the other way round. It now counts the grid and the finish
/// the way AchievementEvaluator's GridOf and PlaceOf do.
/// </summary>
public sealed class BugHunt8_NightMoments_csTests : IDisposable
{
    private readonly TempAppData _appData = new();

    public void Dispose() => _appData.Dispose();

    private static readonly DateTime Green = new(2026, 10, 20, 19, 0, 0, DateTimeKind.Utc);

    private static readonly long[] ThreeLaps = { 10000, 9000, 9000 };

    private static SessionResultEntry Row(string transponder, int position, int startPosition, long[]? laps = null)
    {
        var times = laps ?? ThreeLaps;

        return new SessionResultEntry
        {
            Transponder = transponder,
            DriverName = transponder,
            Position = position,
            Laps = times.Length,
            LapTimesMs = times.ToList(),
            StartPosition = startPosition,
            ElapsedMs = times.Sum() + position * 100,
        };
    }

    private static SessionRecord Race(params SessionResultEntry[] rows) =>
        new()
        {
            Id = "a-main",
            SessionType = RaceSessionType.Race,
            Label = "A-Main",
            StartedAtUtc = Green,
            EndedAtUtc = Green.AddMinutes(8),
            Results = rows.ToList(),
        };

    private static List<NightMoments.Moment> Charges(SessionRecord race) =>
        NightMoments.ForRace(race).Where(one => one.What is NightMoments.Kind.Charge).ToList();

    [Fact]
    public void Ghosts_dragged_to_the_front_of_the_grid_do_not_make_a_charge()
    {
        // Two pace ghosts in slots 1 and 2. Dan lines up in slot 6, which is
        // fourth of the real cars, and finishes second: two places, not four.
        var found = Charges(Race(
            Row(Ghosts.Prefix + "1", 0, 1),
            Row(Ghosts.Prefix + "2", 0, 2),
            Row("ann", 1, 3),
            Row("dan", 2, 6),
            Row("bob", 3, 4),
            Row("cat", 4, 5),
            Row("eve", 5, 7)));

        Assert.Empty(found);
    }

    [Fact]
    public void No_shows_on_the_front_rows_do_not_make_a_charge()
    {
        // A hand-released roster: the two front-row drivers never left the
        // line. They held slots, not places, as the badge's GridOf says.
        var found = Charges(Race(
            Row("ann", 1, 3),
            Row("dan", 2, 6),
            Row("bob", 3, 4),
            Row("cat", 4, 5),
            Row("eve", 5, 7),
            Row("nan", 6, 1, Array.Empty<long>()),
            Row("ned", 7, 2, Array.Empty<long>())));

        Assert.Empty(found);
    }

    [Fact]
    public void A_real_charge_is_counted_among_the_real_cars()
    {
        // A ghost on pole. Bill from slot 6 (fifth real car) to P1 is the
        // badge's four places; Sam from slot 7 (sixth real car) to P3 is
        // three. Counted off the raw slots it was five and four - two
        // charges, and the wrong figure on Bill's.
        var found = Charges(Race(
            Row(Ghosts.Prefix + "1", 0, 1),
            Row("bill", 1, 6),
            Row("dot", 2, 2),
            Row("sam", 3, 7),
            Row("ann", 4, 3),
            Row("kit", 5, 4),
            Row("lou", 6, 5)));

        var charge = Assert.Single(found);

        Assert.Equal("bill", charge.Transponder);
        Assert.Equal(4, charge.Places);
    }

    [Fact]
    public void Four_real_places_from_the_back_of_a_full_real_grid_is_still_a_charge()
    {
        // The plain case the old arithmetic got right must stay right.
        var found = Charges(Race(
            Row("bill", 1, 5),
            Row("dot", 2, 1),
            Row("sam", 3, 2),
            Row("ann", 4, 3),
            Row("kit", 5, 4)));

        var charge = Assert.Single(found);

        Assert.Equal("bill", charge.Transponder);
        Assert.Equal(4, charge.Places);
    }
}
