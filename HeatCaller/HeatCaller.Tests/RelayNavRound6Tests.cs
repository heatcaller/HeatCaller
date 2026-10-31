namespace HeatCaller.Tests;

/// <summary>
/// RELAY NAVIGATION, ROUND 6 (30 Oct 2026, Asher: "build it with newcomer"):
/// the club's home page has a "New here?" card - where the club races, when,
/// and how to join - typed in the app and sent in the site document.
/// </summary>
public sealed class RelayNavRound6Tests
{
    [Fact]
    public void The_newcomer_lines_go_trimmed_under_the_names_the_relay_reads()
    {
        var lines = RelayDocuments.Newcomer("  Kent Hobbies, 12 Main St ", "Tuesdays from 6pm", " Bring your car ");

        Assert.NotNull(lines);
        Assert.Equal("Kent Hobbies, 12 Main St", lines!["where"]);
        Assert.Equal("Tuesdays from 6pm", lines["when"]);
        Assert.Equal("Bring your car", lines["join"]);
    }

    [Fact]
    public void One_line_is_enough_for_a_card_and_the_others_go_empty()
    {
        var lines = RelayDocuments.Newcomer(null, "Tuesdays from 6pm", "");

        Assert.NotNull(lines);
        Assert.Equal(string.Empty, lines!["where"]);
        Assert.Equal(string.Empty, lines["join"]);
    }

    [Fact]
    public void All_three_empty_sends_no_card()
    {
        Assert.Null(RelayDocuments.Newcomer("", "   ", null));
    }

    [Fact]
    public void A_new_settings_file_has_no_card()
    {
        var settings = new AppSettings();

        Assert.Null(RelayDocuments.Newcomer(settings.RelayNewcomerWhere, settings.RelayNewcomerWhen, settings.RelayNewcomerJoin));
    }
}
