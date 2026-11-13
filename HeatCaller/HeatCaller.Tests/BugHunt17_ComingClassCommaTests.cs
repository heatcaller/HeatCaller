namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: a class with a comma in its name ("Buggy, 2WD") could never
/// be entered from the phone's "I'm coming" card. The ticked classes come as
/// one comma-separated line, so the Me page sends such a name with the comma
/// as a space - and the laptop looked for the name exactly as the club typed
/// it. See DriverRequestDesk.ClassesOf and meComingNight in relay/src/mespaces.ts.
/// </summary>
public sealed class BugHunt17_ComingClassCommaTests
{
    private static readonly string[] OnTheNight = { "Buggy, 2WD", "Stock" };

    [Fact]
    public void A_class_with_a_comma_is_found_the_way_the_page_sends_it()
    {
        Assert.Equal(new[] { "Buggy, 2WD" }, DriverRequestDesk.ClassesOf("Buggy 2WD", OnTheNight));
        Assert.Equal(new[] { "Buggy, 2WD", "Stock" }, DriverRequestDesk.ClassesOf("buggy 2wd,Stock", OnTheNight));
    }

    [Fact]
    public void An_older_page_s_two_spaces_for_the_comma_match_as_well()
    {
        Assert.Equal(new[] { "Buggy, 2WD" }, DriverRequestDesk.ClassesOf("Buggy  2WD", OnTheNight));
    }

    [Fact]
    public void A_name_that_is_only_part_of_it_still_does_not_match()
    {
        Assert.Empty(DriverRequestDesk.ClassesOf("Buggy", OnTheNight));
        Assert.Empty(DriverRequestDesk.ClassesOf("Buggy2WD", OnTheNight));
    }
}
