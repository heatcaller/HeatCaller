namespace HeatCaller.Tests;

/// <summary>
/// Whether a layout fits the room, and the RCP kits it can be built from.
///
/// Both are arithmetic a club acts on: one decides whether they carry the
/// tiles into the hall, the other decides what they buy. A wrong answer
/// here is confidently wrong, which is the dangerous kind.
/// </summary>
public sealed class RoomFitTests
{
    private const double Foot = 304.8;
    private const double Inch = 25.4;

    private static double Ft(double feet, double inches = 0) => feet * Foot + inches * Inch;

    // --- Does it fit --------------------------------------------------------

    [Fact]
    public void A_layout_with_room_to_walk_round_fits_comfortably()
    {
        var fit = RoomFit.Check(Ft(12, 4), Ft(9, 8), Ft(20), Ft(14));

        Assert.Equal(RoomFit.Verdict.Comfortable, fit.Verdict);
        Assert.True(fit.Fits);
        Assert.Contains("7ft 8in", fit.Says);
    }

    [Fact]
    public void A_layout_that_only_just_goes_in_says_so()
    {
        // Eight inches across is not room to stand, and a track you cannot
        // reach into is not a track you can marshal.
        var fit = RoomFit.Check(Ft(12, 4), Ft(9, 8), Ft(13), Ft(10));

        Assert.Equal(RoomFit.Verdict.Tight, fit.Verdict);
        Assert.True(fit.Fits);
        Assert.Contains("only just", fit.Says);
    }

    [Fact]
    public void A_layout_that_fits_the_other_way_round_says_to_turn_it()
    {
        // Plenty of halls are long and narrow, and the answer is to turn
        // the circuit rather than to redraw it.
        var fit = RoomFit.Check(Ft(12, 4), Ft(9, 8), Ft(10), Ft(13));

        Assert.Equal(RoomFit.Verdict.TurnedOnly, fit.Verdict);
        Assert.True(fit.Fits);
        Assert.Contains("quarter turn", fit.Says);
    }

    [Fact]
    public void A_layout_too_big_either_way_says_by_how_much()
    {
        var fit = RoomFit.Check(Ft(20), Ft(13), Ft(15), Ft(12));

        Assert.Equal(RoomFit.Verdict.TooBig, fit.Verdict);
        Assert.False(fit.Fits);
        Assert.Contains("5ft", fit.Says);
        Assert.Contains("either way round", fit.Says);
    }

    [Fact]
    public void How_far_over_is_measured_against_the_better_way_round()
    {
        // 14ft x 4ft into a 5ft x 14ft room: hopeless straight on, and one
        // foot short turned. The number a club needs is the one foot.
        var fit = RoomFit.Check(Ft(14), Ft(4), Ft(5), Ft(13));

        Assert.Equal(RoomFit.Verdict.TooBig, fit.Verdict);
        Assert.Contains("1ft", fit.Says);
        Assert.DoesNotContain("9ft", fit.Says);
    }

    [Fact]
    public void Exactly_the_size_of_the_room_is_a_fit_but_a_tight_one()
    {
        var fit = RoomFit.Check(Ft(12), Ft(9), Ft(12), Ft(9));

        Assert.Equal(RoomFit.Verdict.Tight, fit.Verdict);
        Assert.True(fit.Fits);
    }

    [Fact]
    public void No_room_given_is_no_answer_rather_than_a_wrong_one()
    {
        Assert.Equal(RoomFit.Verdict.Unknown, RoomFit.Check(Ft(12), Ft(9), 0, 0).Verdict);
        Assert.Equal(RoomFit.Verdict.Unknown, RoomFit.Check(Ft(12), Ft(9), Ft(12), 0).Verdict);
        Assert.Equal(RoomFit.Verdict.Unknown, RoomFit.Check(Ft(12), Ft(9), -1, -1).Verdict);
    }

    [Fact]
    public void Nothing_laid_out_yet_is_not_a_fit()
    {
        var fit = RoomFit.Check(0, 0, Ft(12), Ft(9));

        Assert.Equal(RoomFit.Verdict.Unknown, fit.Verdict);
        Assert.False(fit.Fits);
    }

    // --- Feet and inches ----------------------------------------------------

    [Theory]
    [InlineData(0, "0in")]
    [InlineData(25.4, "1in")]
    [InlineData(304.8, "1ft")]
    [InlineData(304.8 * 3 + 25.4 * 6, "3ft 6in")]
    public void Lengths_read_as_a_club_measures_a_room(double mm, string expected) =>
        Assert.Equal(expected, RoomFit.Feet(mm));

    [Fact]
    public void Eleven_and_a_bit_inches_rounds_to_a_foot_not_twelve_inches()
    {
        // "3ft 12in" is the kind of thing that makes people stop trusting
        // a readout.
        Assert.Equal("1ft", RoomFit.Feet(11.7 * Inch));
        Assert.Equal("4ft", RoomFit.Feet(Ft(3, 11.8)));
    }

    [Fact]
    public void Feet_and_inches_go_back_to_millimetres()
    {
        Assert.Equal(Ft(12, 4), RoomFit.FromFeet(12, 4), 3);
        Assert.Equal(0, RoomFit.FromFeet(0, 0), 3);
    }

    // --- The kits -----------------------------------------------------------

    [Fact]
    public void Every_kit_RCP_sells_is_offered()
    {
        Assert.Equal(8, RcpKit.All.Count);

        foreach (var kit in RcpKit.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(kit.Name));
            Assert.False(string.IsNullOrWhiteSpace(kit.Footprint));
            Assert.True(kit.TileSizeMm is 300 or 500, $"{kit.Name} is neither a 30cm nor a 50cm set");
            Assert.True(kit.TotalTiles > 0, $"{kit.Name} says it has no tiles in it");
        }
    }

    [Fact]
    public void The_kits_a_saved_plan_may_already_name_are_still_there()
    {
        // A plan remembers the kit it was checked against BY NAME. Renaming
        // one would quietly un-check every plan that used it.
        Assert.NotNull(RcpKit.ByName("30cm Mini-96"));
        Assert.NotNull(RcpKit.ByName("50cm Wide-L / Oblong"));
    }

    [Fact]
    public void A_published_breakdown_adds_up_to_the_tiles_in_the_box()
    {
        var mini = RcpKit.ByName("30cm Mini-96")!;

        Assert.True(mini.BreakdownPublished);
        Assert.Equal(96, mini.TotalTiles);
        Assert.Equal(72, mini.TotalStraightRails);
        Assert.Equal(24, mini.TotalTurnRails);
    }

    [Fact]
    public void A_kit_RCP_only_gives_a_tile_count_for_says_so()
    {
        var oval = RcpKit.ByName("50cm Oval")!;

        Assert.False(oval.BreakdownPublished);
        Assert.Equal(28, oval.TotalTiles);
    }

    [Fact]
    public void Rails_are_not_called_short_against_a_kit_that_never_published_them()
    {
        // The whole point of BreakdownPublished. "46 more straight rails
        // (need 46, kit has 0)" is a shortfall invented out of nothing,
        // and it reads exactly as confidently as a true one.
        var needs = new RcpInventory { Tiles = 20, StraightRails = 46, TurnRails = 8 };

        var lines = needs.Shortfalls(RcpKit.ByName("50cm Oval")!);

        Assert.DoesNotContain(lines, l => l.Contains("straight rails (need"));
        Assert.DoesNotContain(lines, l => l.Contains("turn rails (need"));
        Assert.Contains(lines, l => l.Contains("doesn't publish"));
    }

    [Fact]
    public void Rails_are_still_checked_against_a_kit_that_did_publish_them()
    {
        var needs = new RcpInventory { Tiles = 20, StraightRails = 200, TurnRails = 8 };

        var lines = needs.Shortfalls(RcpKit.ByName("30cm Mini-96")!);

        Assert.Contains(lines, l => l.Contains("straight rails"));
    }

    [Fact]
    public void A_layout_inside_the_kit_is_short_of_nothing()
    {
        var needs = new RcpInventory { Tiles = 40, StraightRails = 30, TurnRails = 10 };

        Assert.Empty(needs.Shortfalls(RcpKit.ByName("30cm Mini-96")!));
    }
}
