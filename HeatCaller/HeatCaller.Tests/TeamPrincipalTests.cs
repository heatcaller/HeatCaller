namespace HeatCaller.Tests;

/// <summary>
/// The Team Principal (17 Oct 2026): a second interview guest beside the
/// Technical Lead, before a race and after one. What can be pinned without a
/// race running: who they speak for, how they are introduced, that their
/// interview splits into the two voices, and that a club's settings start off.
/// </summary>
public sealed class TeamPrincipalTests
{
    private static readonly string[] Grid = { "A", "B", "C", "D" };

    private static string? Pick(
        Func<string, bool>? hasTeam = null,
        Func<string, bool>? inRivalry = null,
        Func<string, int?>? standing = null,
        bool teamsOnly = false,
        params string[] heard) =>
        CommentaryService.PickPrincipalFocus(
            Grid,
            hasTeam ?? (_ => false),
            inRivalry ?? (_ => false),
            standing ?? (_ => null),
            teamsOnly,
            heard);

    [Fact]
    public void With_nothing_to_choose_between_them_it_is_the_front_of_the_grid()
    {
        Assert.Equal("A", Pick());
    }

    [Fact]
    public void A_driver_in_a_rivalry_on_this_grid_comes_first()
    {
        Assert.Equal("C", Pick(inRivalry: id => id == "C", standing: id => id == "B" ? 1 : null));
    }

    [Fact]
    public void Then_the_best_placed_in_the_championship()
    {
        Assert.Equal("D", Pick(standing: id => id switch { "B" => 4, "D" => 2, _ => null }));
    }

    [Fact]
    public void Then_a_driver_with_a_team()
    {
        Assert.Equal("B", Pick(hasTeam: id => id == "B"));
    }

    [Fact]
    public void Teams_only_leaves_out_a_driver_without_one()
    {
        Assert.Equal("D", Pick(hasTeam: id => id == "D", inRivalry: id => id == "A", teamsOnly: true));
        Assert.Null(Pick(teamsOnly: true));
    }

    [Fact]
    public void Each_drivers_principal_is_heard_once_a_night()
    {
        Assert.Equal("B", Pick(heard: new[] { "A" }));
        Assert.Equal("D", Pick(heard: new[] { "a", "B", "c" }));
        Assert.Null(Pick(heard: Grid));
    }

    [Fact]
    public void They_are_the_teams_principal_or_the_drivers_own()
    {
        Assert.Equal("the team principal of Apex Racing", CommentaryService.PrincipalRole("Apex Racing", "Asher"));
        Assert.Equal("Asher's team principal", CommentaryService.PrincipalRole(string.Empty, "Asher"));
    }

    [Theory]
    [InlineData("TEAM PRINCIPAL")]
    [InlineData("Principal")]
    public void Their_interview_splits_into_the_two_voices(string label)
    {
        var text =
            $"HOST: Joining us now, what does that win mean to the team? {label}: It means everything, we worked hard for it. "
            + $"HOST: And where does it leave you in the championship? {label}: Three points clear, and we want more.";

        var interview = CommentaryService.ParseInterview(
            text, "Sam", new[] { "Asher", "Sam" }, new[] { "TEAM PRINCIPAL", "PRINCIPAL" });

        Assert.NotNull(interview);
        Assert.Equal(2, interview!.Count);
        Assert.Equal("It means everything, we worked hard for it.", interview[0].Answer);
        Assert.Equal("Three points clear, and we want more.", interview[1].Answer);
    }

    [Fact]
    public void A_club_starts_with_the_team_principal_off_and_heats_included()
    {
        var settings = new AppSettings();

        Assert.False(settings.PrincipalBeforeRace);
        Assert.False(settings.PrincipalAfterRace);
        Assert.True(settings.PrincipalInHeats);
        Assert.False(settings.PrincipalTeamsOnly);
        Assert.Equal(string.Empty, settings.PrincipalName);
    }

    [Fact]
    public void A_voice_left_blank_by_a_hand_edited_file_reads_as_none()
    {
        var settings = new AppSettings { PrincipalElevenLabsVoiceId = null!, PrincipalKokoroVoiceName = "af_heart" };

        var voice = settings.PrincipalVoice;

        Assert.Equal(string.Empty, voice.ElevenLabsVoiceId);
        Assert.Equal("af_heart", voice.KokoroVoiceName);
    }
}
