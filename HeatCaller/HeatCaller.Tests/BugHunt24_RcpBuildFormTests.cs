namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), Build me a track: when the parts typed into
/// the Parts section are what rule every worked-out layout out, the window
/// says so, rather than blaming the room and the lane width.
/// </summary>
public sealed class BugHunt24_RcpBuildFormTests
{
    private static RcpTrackDesign Room(double width, double depth, double tile) =>
        new() { TileSizeMm = tile, RoomWidthMm = width, RoomDepthMm = depth, RoomInFeet = false };

    /// <summary>A cupboard with only loose rails counted: something entered, but no tiles at all.</summary>
    private static RcpStock OnlyLooseRails()
    {
        var stock = new RcpStock();
        stock.LooseFor(500).StraightRails = 4;
        return stock;
    }

    [Fact]
    public void Parts_that_make_nothing_are_named_not_the_room()
    {
        var room = Room(9000, 6000, 500);
        var stock = OnlyLooseRails();

        // The room is big enough for worked-out shapes; the cupboard rules them all out.
        Assert.Contains(RcpLayoutGenerator.For(room, null, 2), l => !l.RcpOwn);
        Assert.DoesNotContain(RcpLayoutGenerator.For(room, stock, 2), l => !l.RcpOwn);

        var (cols, rows) = RcpLayoutGenerator.RoomTiles(room);
        var says = RcpBuildForm.Intro(room, stock, 2, Array.Empty<RcpBuiltLayout>(), cols, rows);

        Assert.True(RcpBuildForm.PartsRuledOutWorkedOut(room, stock, 2, Array.Empty<RcpBuiltLayout>()));
        Assert.Contains("Parts section", says);
        Assert.DoesNotContain("Nothing fits that room", says);
        Assert.DoesNotContain("Lane tool", says);
    }

    [Fact]
    public void With_only_RCPs_own_left_it_does_not_promise_worked_out_layouts()
    {
        var room = Room(9000, 6000, 500);
        var stock = OnlyLooseRails();
        var offered = RcpLayoutGenerator.For(room, stock, 2);

        Assert.Contains(offered, l => l.RcpOwn);
        Assert.DoesNotContain(offered, l => !l.RcpOwn);

        var (cols, rows) = RcpLayoutGenerator.RoomTiles(room);
        var says = RcpBuildForm.Intro(room, stock, 2, offered, cols, rows);

        Assert.Contains("Parts section", says);
        Assert.DoesNotContain("The rest are worked out", says);
    }

    [Fact]
    public void A_room_too_small_even_with_no_parts_entered_still_says_the_room()
    {
        // Nothing worked out fits even with no cupboard check, so the room
        // and the lane width really are the limit.
        var room = Room(2500, 2500, 500);
        var stock = OnlyLooseRails();

        Assert.DoesNotContain(RcpLayoutGenerator.For(room, null, 2), l => !l.RcpOwn);
        Assert.False(RcpBuildForm.PartsRuledOutWorkedOut(room, stock, 2, Array.Empty<RcpBuiltLayout>()));

        var (cols, rows) = RcpLayoutGenerator.RoomTiles(room);
        var says = RcpBuildForm.Intro(room, stock, 2, Array.Empty<RcpBuiltLayout>(), cols, rows);

        Assert.Contains("Nothing fits that room", says);
    }

    [Fact]
    public void No_parts_entered_keeps_the_usual_words()
    {
        var room = Room(9000, 6000, 500);
        var offered = RcpLayoutGenerator.For(room, null, 2);

        Assert.False(RcpBuildForm.PartsRuledOutWorkedOut(room, null, 2, offered));

        var (cols, rows) = RcpLayoutGenerator.RoomTiles(room);
        var says = RcpBuildForm.Intro(room, null, 2, offered, cols, rows);

        Assert.Contains("The rest are worked out", says);
        Assert.DoesNotContain("Parts section", says);
    }
}
