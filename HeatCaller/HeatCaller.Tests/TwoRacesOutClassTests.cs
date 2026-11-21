namespace HeatCaller.Tests;

/// <summary>
/// Two races out names its class (21 Nov 2026). Straight after GT's Q1 Heat 1
/// had run, the room heard "Q1 Heat 1 drivers, you're two races away" - which
/// meant Mod's Q1 Heat 1, but sounded like the race that had just finished.
/// </summary>
public sealed class TwoRacesOutClassTests
{
    [Fact]
    public void The_two_races_out_call_says_the_class_with_the_round()
    {
        Assert.Equal("Mod, Q1 Heat 1 drivers, you're two races away. Get your cars ready.", GapWords.TwoOut("Q1 Heat 1", "Mod"));
    }

    [Fact]
    public void A_race_with_no_class_is_called_by_its_round_alone()
    {
        Assert.Equal("Heat 4 drivers, you're two races away. Get your cars ready.", GapWords.TwoOut("Heat 4"));
        Assert.Equal("Heat 4 drivers, you're two races away. Get your cars ready.", GapWords.TwoOut(" Heat 4 ", "  "));
    }

    [Fact]
    public void A_heat_two_classes_share_is_called_by_both()
    {
        // Bug hunt 23: "Stock, Heat 4 drivers" left Mod's drivers in the same heat out of the call.
        var shared = new RoundDefinition
        {
            ClassName = "Stock",
            SharedClasses = new List<string> { "Mod" },
            RoundLabel = "Heat 4",
            Kind = RoundKind.Qualifier,
        };

        Assert.Equal("Stock and Mod", GapWords.SpokenClassOf(shared));
        Assert.Equal("Stock and Mod, Heat 4 drivers, you're two races away. Get your cars ready.",
            GapWords.TwoOut(shared.RoundLabel, GapWords.SpokenClassOf(shared)));

        Assert.Equal("Mod", GapWords.SpokenClassOf(new RoundDefinition { ClassName = "Mod", RoundLabel = "Heat 5" }));
        Assert.Equal(string.Empty, GapWords.SpokenClassOf(null));
    }

    [Fact]
    public void The_line_the_gap_says_carries_the_class_of_the_race_two_out()
    {
        var facts = new GapFacts
        {
            NextId = "gt-h2",
            Round = "Q1 Heat 2",
            ClassName = "GT",
            TwoOutRound = "Q1 Heat 1",
            TwoOutClassName = "Mod",
            TwoOutId = "mod-h1",
        };

        var said = GapPreview.TextFor(GapLine.TwoOut, new GapRules(), facts, DateTime.UtcNow, new GapState());

        Assert.Equal("Mod, Q1 Heat 1 drivers, you're two races away. Get your cars ready.", said);
    }
}
