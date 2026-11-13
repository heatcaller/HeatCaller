namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), marshal swaps. Two marshals of one race each
/// asked the same free driver, both got a yes, and that driver was put on
/// both posts - the roster showed every corner covered with one of them
/// empty. A cover already on a post of the round is now refused.
/// </summary>
public sealed class BugHunt17_MarshalSwapsTests
{
    private static RoundDefinition Race() => new() { RoundLabel = "Q2 Heat 3", AssignedTransponders = new List<string> { "9" } };

    [Fact]
    public void A_driver_covering_one_post_cannot_cover_a_second_in_the_same_race()
    {
        var round = Race();

        Assert.Null(MarshalSwaps.Cover(round, "MAX", "SAM"));
        Assert.NotNull(MarshalSwaps.Cover(round, "JO", "sam"));

        Assert.Equal("SAM", round.MarshalCovers!["MAX"]);
        Assert.False(round.MarshalCovers.ContainsKey("JO"));
    }

    [Fact]
    public void A_driver_already_marshalling_the_race_cannot_cover_another_post()
    {
        var round = Race();

        // Jo is on a post of this race; Max's ask to Jo was made before that.
        Assert.NotNull(MarshalSwaps.Cover(round, "MAX", "JO", marshalling: new[] { "MAX", "JO" }));
        Assert.Null(round.MarshalCovers);

        Assert.Null(MarshalSwaps.Cover(round, "MAX", "SAM", marshalling: new[] { "MAX", "JO" }));
        Assert.Equal("SAM", round.MarshalCovers!["MAX"]);
    }

    [Fact]
    public void Taking_a_post_back_and_passing_a_cover_on_still_work()
    {
        var round = Race();

        Assert.Null(MarshalSwaps.Cover(round, "MAX", "SAM"));

        // The same swap again is no second post.
        Assert.Null(MarshalSwaps.Cover(round, "MAX", "SAM", marshalling: new[] { "SAM", "JO" }));

        // Sam, covering Max's post, passes it on to Lee.
        Assert.Null(MarshalSwaps.Cover(round, "SAM", "LEE", marshalling: new[] { "SAM", "JO" }));
        Assert.Equal("LEE", round.MarshalCovers!["MAX"]);

        // And Lee hands it back to Max, whose post it is.
        Assert.Null(MarshalSwaps.Cover(round, "LEE", "MAX", marshalling: new[] { "LEE", "JO" }));
        Assert.Null(round.MarshalCovers);
    }
}
