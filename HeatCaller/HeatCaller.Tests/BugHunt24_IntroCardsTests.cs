namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): the driver introductions' Big Screen cards.
/// A driver named in passing in a sentence of its own, still about the
/// pole-sitter, got their card there instead of at their own line.
/// </summary>
public sealed class BugHunt24_IntroCardsTests
{
    private static readonly string[] Grid = { "Sam Parker", "Bill Hargrove" };

    private static List<(int Driver, int At)> Placed(string text) =>
        IntroCards.Plan(text, Grid).Select(p => (p.Driver, p.At)).ToList();

    [Fact]
    public void A_driver_named_in_the_pole_sitters_second_sentence_waits_for_their_own_line()
    {
        const string text = "From pole, it's Sam Parker. Parker was a tenth clear of Bill Hargrove in qualifying. Starting second, Bill Hargrove.";

        var placed = Placed(text);

        Assert.Equal(new[] { 0, 1 }, placed.Select(p => p.Driver));
        Assert.Equal(0, placed[0].At);
        Assert.Equal(text.IndexOf("Starting second", StringComparison.Ordinal), placed[1].At);
    }

    [Fact]
    public void A_second_sentence_that_opens_with_he_is_still_the_pole_sitters()
    {
        const string text = "From pole, it's Sam Parker. He was a tenth clear of Bill Hargrove in qualifying. Starting second, Bill Hargrove.";

        var placed = Placed(text);

        Assert.Equal(new[] { 0, 1 }, placed.Select(p => p.Driver));
        Assert.Equal(text.IndexOf("Starting second", StringComparison.Ordinal), placed[1].At);
    }

    [Fact]
    public void An_introduction_followed_by_more_about_the_same_driver_is_not_held_back()
    {
        const string text = "Sam Parker on pole. Alongside him, Bill Hargrove. Hargrove has been quick all night.";

        var placed = Placed(text);

        Assert.Equal(new[] { 0, 1 }, placed.Select(p => p.Driver));
        Assert.Equal(text.IndexOf("Alongside", StringComparison.Ordinal), placed[1].At);
    }

    [Fact]
    public void A_passing_mention_nobody_names_again_still_gets_the_card_there()
    {
        // Nothing later names Bill, so his only mention is where his card goes.
        const string text = "From pole, it's Sam Parker. He was a tenth clear of Bill Hargrove in qualifying.";

        var placed = Placed(text);

        Assert.Equal(new[] { 0, 1 }, placed.Select(p => p.Driver));
        Assert.Equal(text.IndexOf("He was", StringComparison.Ordinal), placed[1].At);
    }
}
