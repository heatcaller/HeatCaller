namespace HeatCaller.Tests;

/// <summary>
/// The mechanic's first-setup line is built from whole items that fit the
/// interview's line limit, so no value is cut in half on its way to the
/// model (2 Oct 2026).
/// </summary>
public sealed class BugHuntCommentaryTests
{
    private static readonly DateTime RaceEnd = new(2026, 9, 27, 20, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyList<string> Lines(params (string Id, string Value)[] values)
    {
        var entry = TestSheet.Setup(RaceEnd.AddHours(-1), values);
        entry.CarId = "car-1";

        var car = new Car { Id = "car-1", Name = "car-1", OwnerTransponder = "T1", ClassName = "Mini-Z" };

        return MechanicBrief.SetupLines("T1", "Asher", "Mini-Z", null, RaceEnd, new[] { car }, new[] { entry }, TestSheet.For);
    }

    [Fact]
    public void An_item_that_would_overflow_the_first_setup_line_is_left_off_whole()
    {
        var longSprings = "Soft " + new string('x', 150);

        var lines = Lines(("pinion", "22"), ("springs", longSprings));

        Assert.Equal(
            new[] { "Setup logged for this race day, the first one logged for this car, so there is nothing to compare it with: Pinion 22T" },
            lines);
        Assert.All(lines, line => Assert.True(line.Length <= MechanicBrief.MaxLineChars));
    }

    [Fact]
    public void A_first_item_too_long_to_fit_is_not_read_out_at_all()
    {
        var lines = Lines(("springs", "Soft " + new string('x', 150)));

        Assert.Equal(
            new[] { "Setup logged for this race day, the first one logged for this car; its settings are too long to read out here." },
            lines);
    }
}
