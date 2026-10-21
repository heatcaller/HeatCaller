namespace HeatCaller.Tests;

/// <summary>
/// The show's fitting (bug hunt 8, 20 Oct 2026).
///
/// The first segment's overrun exemption was judged with the settle and the
/// mark a look-back bridge in front of it had added, so a segment that would
/// have gone out on its own was refused, the bridge came off at the end, and
/// the block said nothing at all.
/// </summary>
public sealed class BugHunt8_ShowBlock_csTests
{
    private static readonly ShowBlockCost Cost = new(
        TimeSpan.Zero,
        TimeSpan.FromSeconds(0.45),
        TimeSpan.FromSeconds(4));

    [Fact]
    public void A_bridge_in_front_does_not_cost_a_punctuated_first_segment_its_exemption()
    {
        // Room is 23 - 3 headroom = 20s. The recap alone is 26s, inside the
        // 8s allowance (28s). Behind a 1s bridge it costs 26 + 0.45 + 4 =
        // 30.45s, past 28s - so the bridge comes off and the recap goes out
        // alone, rather than nothing going out.
        var bridge = ShowItem.Bridge("still to come", () => { }, estimate: TimeSpan.FromSeconds(1));
        var recap = ShowItem.Spoken("recap", () => { }, punctuateBefore: true, estimate: TimeSpan.FromSeconds(26));

        var fit = ShowBlock.Fit(new[] { bridge, recap }, TimeSpan.FromSeconds(23), Cost);

        Assert.Same(recap, Assert.Single(fit.Items));
        Assert.Same(bridge, Assert.Single(fit.Dropped));
        Assert.Equal(TimeSpan.FromSeconds(26), fit.Spent);
    }

    [Fact]
    public void A_bridge_that_keeps_the_segment_inside_the_bound_stays_in()
    {
        // Room 20s, allowance to 28s. 1 + 0.45 + 4 + 20 = 25.45s: over room
        // but inside the allowance, so both go out.
        var bridge = ShowItem.Bridge("still to come", () => { }, estimate: TimeSpan.FromSeconds(1));
        var recap = ShowItem.Spoken("recap", () => { }, punctuateBefore: true, estimate: TimeSpan.FromSeconds(20));

        var fit = ShowBlock.Fit(new[] { bridge, recap }, TimeSpan.FromSeconds(23), Cost);

        Assert.Equal(2, fit.Items.Count);
        Assert.Same(bridge, fit.Items[0]);
        Assert.Same(recap, fit.Items[1]);
        Assert.Empty(fit.Dropped);
    }

    [Fact]
    public void A_segment_too_long_even_alone_still_closes_the_block()
    {
        var bridge = ShowItem.Bridge("still to come", () => { }, estimate: TimeSpan.FromSeconds(1));
        var recap = ShowItem.Spoken("recap", () => { }, punctuateBefore: true, estimate: TimeSpan.FromSeconds(29));

        var fit = ShowBlock.Fit(new[] { bridge, recap }, TimeSpan.FromSeconds(23), Cost);

        Assert.Empty(fit.Items);
    }
}
