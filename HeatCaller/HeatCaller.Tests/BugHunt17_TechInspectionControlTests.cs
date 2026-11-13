namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, the scrutineering log: an inspection from another day says
/// which day. Every row used to show the time alone, so Saturday's and
/// Sunday's inspections of one car could not be told apart.
/// </summary>
public sealed class BugHunt17_TechInspectionControlTests
{
    private static readonly DateTime Today = new(2026, 11, 15, 14, 0, 0);

    [Fact]
    public void An_inspection_made_today_shows_the_time_alone()
    {
        var when = new DateTime(2026, 11, 15, 9, 5, 0);
        var text = TechInspectionControl.WhenText(when, Today);

        Assert.DoesNotContain(when.ToShortDateString(), text);
        Assert.Contains("9:05", text);
    }

    [Fact]
    public void An_inspection_from_another_day_shows_its_date_too()
    {
        var when = new DateTime(2026, 11, 14, 9, 5, 0);
        var text = TechInspectionControl.WhenText(when, Today);

        Assert.StartsWith(when.ToShortDateString(), text);
        Assert.Contains("9:05", text);
    }
}
