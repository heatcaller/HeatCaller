namespace HeatCaller.Tests;

/// <summary>
/// The recap show's sign-off always names the show when the club has given
/// it one (17 Oct 2026) - see RaceControlControl.WithShowName.
/// </summary>
public sealed class SignOffShowNameTests
{
    [Fact]
    public void A_sign_off_without_the_show_gets_it_on_the_end()
    {
        var line = RaceControlControl.WithShowName("Up next, the A-Main - stay with us!", "Friday Night Thunder");

        Assert.Equal("Up next, the A-Main - stay with us! This has been Friday Night Thunder!", line);
    }

    [Fact]
    public void A_sign_off_that_already_names_the_show_is_left_alone()
    {
        const string said = "That's all from friday night thunder - see you next week!";

        Assert.Equal(said, RaceControlControl.WithShowName(said, "Friday Night Thunder"));
    }

    [Fact]
    public void No_show_name_typed_changes_nothing()
    {
        Assert.Equal("Stay with us!", RaceControlControl.WithShowName("Stay with us!", ""));
        Assert.Equal("Stay with us!", RaceControlControl.WithShowName("Stay with us!", null));
    }

    [Fact]
    public void A_line_with_no_closing_stop_gets_one_before_the_tag()
    {
        var line = RaceControlControl.WithShowName("Stay with us", "Friday Night Thunder.");

        Assert.Equal("Stay with us. This has been Friday Night Thunder!", line);
    }
}
