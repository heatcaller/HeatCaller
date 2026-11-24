namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): the tidy that takes out the words around an
/// empty placeholder.
///
/// An aside set off by two commas - "{driver}, in the {car}, takes the
/// lead!" - lost only the comma in front, so a driver with no car model was
/// read as "Sam, takes the lead!"; behind an audio tag the same line came
/// out "[excited], Sam leads!". And "Fast lap for {team}'s {driver}!" with
/// no team was read as "Fast lap's Sam!", because the "for {team}" pattern
/// matched first and left the "'s" behind.
/// </summary>
public sealed class BugHunt24_AnnouncementTextTests
{
    private const string M = AnnouncementText.Missing;

    private static DriverProfile Profile(string team = "", string sponsors = "") =>
        new() { Transponder = "1234", DriverName = "Sam", Team = team, Sponsors = sponsors };

    private static string Fill(string template, DriverProfile profile) =>
        template.Replace("{driver}", "Sam").WithDriverDetails(profile).TidyTemplate();

    [Fact]
    public void An_empty_car_aside_between_two_commas_takes_both_commas()
    {
        Assert.Equal("Sam takes the lead!", AnnouncementText.TidyTemplate($"Sam, in the {M}, takes the lead!"));
    }

    [Fact]
    public void An_empty_team_aside_between_two_commas_takes_both_commas()
    {
        Assert.Equal("Sam takes the lead!", Fill("{driver}, racing for {team}, takes the lead!", Profile()));
    }

    [Fact]
    public void An_empty_sponsors_aside_between_two_commas_takes_both_commas()
    {
        Assert.Equal("Sam takes the lead!", Fill("{driver}, sponsored by {sponsors}, takes the lead!", Profile()));
    }

    [Fact]
    public void An_empty_clause_straight_after_an_audio_tag_leaves_no_comma()
    {
        Assert.Equal("[excited] Sam leads!", AnnouncementText.TidyTemplate($"[excited] In the {M}, Sam leads!"));
    }

    [Fact]
    public void A_comma_that_was_not_part_of_an_aside_stays()
    {
        Assert.Equal("Sam takes the lead, ahead of Bill!",
            AnnouncementText.TidyTemplate($"Sam takes the lead in his {M}, ahead of Bill!"));
        Assert.Equal("Sam leads, Bill second.",
            AnnouncementText.TidyTemplate($"Sam leads, in the {M}, Bill second."));
    }

    [Fact]
    public void A_filled_aside_is_untouched()
    {
        Assert.Equal("Sam, in the MR-03, takes the lead!", AnnouncementText.TidyTemplate("Sam, in the MR-03, takes the lead!"));
        Assert.Equal("Sam, racing for Team Neon, takes the lead!",
            Fill("{driver}, racing for {team}, takes the lead!", Profile(team: "Team Neon")));
    }

    [Theory]
    [InlineData("Fast lap for {team}'s {driver}!", "Fast lap for Sam!")]
    [InlineData("Fast lap for {team}’s {driver}!", "Fast lap for Sam!")]
    [InlineData("A flier from {team}'s {driver}!", "A flier from Sam!")]
    [InlineData("A flier by {team}'s {driver}!", "A flier by Sam!")]
    public void A_possessive_team_after_a_small_word_drops_the_team_and_its_s(string template, string expected)
    {
        var said = Fill(template, Profile());

        Assert.Equal(expected, said);
        Assert.DoesNotContain("'s", said);
    }

    [Fact]
    public void A_possessive_team_with_a_team_set_reads_in_full()
    {
        Assert.Equal("Fast lap for Team Neon's Sam!", Fill("Fast lap for {team}'s {driver}!", Profile(team: "Team Neon")));
    }

    [Fact]
    public void Team_and_sponsors_joined_by_and_both_empty_leaves_no_and()
    {
        Assert.Equal("Sam takes the win!", Fill("{driver} takes the win for {team} and {sponsors}!", Profile()));
        Assert.Equal("Sam takes the win for Team Neon!",
            Fill("{driver} takes the win for {team} and {sponsors}!", Profile(team: "Team Neon")));
    }

    [Fact]
    public void The_shapes_that_already_worked_still_read_the_same()
    {
        Assert.Equal("Sam takes the lead!", Fill("{driver} takes the lead racing for {team}!", Profile()));
        Assert.Equal("Sam takes the lead!", Fill("{driver} ({team}) takes the lead!", Profile()));
        Assert.Equal("Sam takes the lead!", Fill("{team}'s {driver} takes the lead!", Profile()));
    }
}
