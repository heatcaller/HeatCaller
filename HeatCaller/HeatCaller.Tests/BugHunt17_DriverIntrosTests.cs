namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, the qualifying recap on the rulebook points scales.
///
/// WHY. The recap took 0 points for "no score". On the IFMAR and EFRA scales
/// the top qualifier of a round scores 0, so a pole sitter who took TQ in
/// every counted round lost their score, second place lost the gap to them,
/// and two cars level on 0 were never said to be level on points.
/// </summary>
public sealed class BugHunt17_DriverIntrosTests
{
    [Fact]
    public void A_pole_sitter_on_zero_points_keeps_the_score_and_the_gap_behind()
    {
        var cars = new List<RecapCar>
        {
            new("Alice", "Alice", 14, 300_000, 0, 11_000, 2, Points: 0),
            new("Bob", "Bob", 15, 300_000, 0, 11_000, 2, Points: 2),
        };

        var (spoken, screen) = DriverIntros.Recap(cars, QualifyingMethod.RoundPoints, 3);

        Assert.Equal("From qualifying: Alice takes pole with 0 points. Bob lines up second, 2 points back.", screen);
        Assert.Equal("From qualifying: Alice takes pole with zero points. Bob lines up second, two points back.", spoken);
    }

    [Fact]
    public void Two_cars_level_on_zero_are_said_to_be_level_on_points()
    {
        var cars = new List<RecapCar>
        {
            new("Alice", "Alice", 14, 300_000, 0, 11_000, 2, Points: 0),
            new("Bob", "Bob", 15, 300_000, 0, 11_000, 2, Points: 0),
        };

        Assert.Contains("Bob lines up second, level on points.", DriverIntros.Recap(cars, QualifyingMethod.RoundPoints, 3).Screen);
    }

    [Fact]
    public void A_class_that_does_not_qualify_on_points_never_reads_them()
    {
        var cars = new List<RecapCar>
        {
            new("Alice", "Alice", 14, 300_000, 0, 11_000),
            new("Bob", "Bob", 13, 300_000, 0, 11_000),
        };

        Assert.DoesNotContain("point", DriverIntros.Recap(cars, QualifyingMethod.TotalLaps, 3).Screen);
    }
}
