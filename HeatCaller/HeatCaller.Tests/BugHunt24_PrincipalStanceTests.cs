namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), F198: the Team Principal's stance compared a
/// finish inside one race's field with a place in the whole season table,
/// so a driver well down a table bigger than tonight's fields was
/// "vindicated" by finishing last.
/// </summary>
public sealed class BugHunt24_PrincipalStanceTests
{
    private static TeamNight Night(int field, int[] finishes, int podiums = 0) =>
        new("sam", "Sam", finishes.Length, finishes, field, finishes.Count(f => f == 1), podiums, 0, 0,
            true, 2.5, 0);

    [Fact]
    public void BugHunt24_A_driver_twelfth_in_the_table_who_finished_last_of_eight_is_not_vindicated()
    {
        var stance = PrincipalStance.For(Night(8, new[] { 8, 7 }), bestFinishTonight: 7, championshipPosition: 12);

        Assert.NotEqual(PrincipalStance.Vindicated, stance);
        Assert.Equal(PrincipalStance.Blunt, stance);
    }

    [Fact]
    public void BugHunt24_A_podium_from_deep_in_a_bigger_table_is_pleased_not_vindicated()
    {
        var stance = PrincipalStance.For(Night(8, new[] { 2, 4 }, podiums: 1), bestFinishTonight: 2, championshipPosition: 12);

        Assert.Equal(PrincipalStance.Pleased, stance);
    }

    [Fact]
    public void BugHunt24_The_table_stances_still_work_when_the_standing_fits_in_tonights_field()
    {
        Assert.Equal(
            PrincipalStance.Vindicated,
            PrincipalStance.For(Night(10, new[] { 2, 5 }, podiums: 1), bestFinishTonight: 2, championshipPosition: 8));

        Assert.Equal(
            PrincipalStance.Defensive,
            PrincipalStance.For(Night(10, new[] { 9, 10 }), bestFinishTonight: 9, championshipPosition: 2));
    }

    [Fact]
    public void BugHunt24_An_unknown_field_size_keeps_the_table_comparison()
    {
        Assert.True(PrincipalStance.TableComparesWithTonight(12, 0));
        Assert.True(PrincipalStance.TableComparesWithTonight(8, 8));
        Assert.False(PrincipalStance.TableComparesWithTonight(12, 8));
    }
}
