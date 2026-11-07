namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 13 (7 Nov 2026): a lane number from a file that ran a new lane
/// off the end of an int, a straight timed flat out when only one of its
/// corners was at top speed, and the Track Layouts list losing its
/// who-holds-each-sector-record notes to the theme pass.
/// </summary>
public sealed class BugHunt13_TrackDesignerTests
{
    [Fact]
    public void A_huge_lane_number_from_a_file_still_leaves_room_for_a_new_lane()
    {
        var design = new RcpTrackDesign { TileSizeMm = 500 };
        design.Tiles.Add(new RcpTile { Col = 0, Row = 0, Lane = int.MaxValue });

        var (back, _, problem) = RcpPlanFile.Read(RcpPlanFile.Write(design, "Odd"));

        Assert.Null(problem);
        Assert.True(back!.Tiles.Single().Lane <= RcpPlanFile.MostLane);

        // One more than any on the plan - a real lane, not a wrapped-round
        // negative that every lane check reads as plain floor.
        var lane = RcpAutoRails.NewLane(back);
        Assert.True(lane > back.Tiles.Single().Lane, $"{lane}");
    }

    [Fact]
    public void An_ordinary_lane_number_from_a_file_is_left_as_it_is()
    {
        var design = new RcpTrackDesign { TileSizeMm = 500 };
        design.Tiles.Add(new RcpTile { Col = 0, Row = 0, Lane = 7 });

        var (back, _, _) = RcpPlanFile.Read(RcpPlanFile.Write(design, "Plain"));

        Assert.Equal(7, back!.Tiles.Single().Lane);
    }

    [Fact]
    public void A_straight_from_a_top_speed_corner_into_a_slow_one_still_brakes()
    {
        var car = RcpLapModel.Car.MiniZ;

        // 3 m from a corner taken at top speed (5 m/s) into a square one
        // (1.7 m/s): flat out until the braking point, then braking down.
        var brakeDistance = (5.0 * 5.0 - 1.7 * 1.7) / (2 * car.Brake);
        var expected = (3 - brakeDistance) / 5.0 + (5.0 - 1.7) / car.Brake;

        var seconds = RcpLapModel.Straight(3, 5, 1.7, car);

        Assert.Equal(expected, seconds, 6);
        Assert.True(seconds > 3 / 5.0 + 0.1, $"{seconds}");
    }

    [Fact]
    public void A_straight_from_a_slow_corner_into_a_top_speed_one_still_accelerates()
    {
        var car = RcpLapModel.Car.MiniZ;

        var accelDistance = (5.0 * 5.0 - 1.7 * 1.7) / (2 * car.Accelerate);
        var expected = (5.0 - 1.7) / car.Accelerate + (3 - accelDistance) / 5.0;

        Assert.Equal(expected, RcpLapModel.Straight(3, 1.7, 5, car), 6);
    }

    [Fact]
    public void A_straight_between_two_top_speed_corners_is_still_flat_out()
    {
        var car = RcpLapModel.Car.MiniZ;

        Assert.Equal(3 / 5.0, RcpLapModel.Straight(3, 5, 5, car), 6);
    }

    [Fact]
    public void A_list_that_asked_for_its_item_notes_keeps_them_after_the_theme_pass()
    {
        using var list = new ListView { ShowItemToolTips = true };
        var item = new ListViewItem("Club Oval") { ToolTipText = "Sector 1 record: 4.321 by Sam" };
        list.Items.Add(item);

        NativeTips.Silence(list);

        // Windows' own pale tip is off, and the app's themed one is on.
        Assert.False(list.ShowItemToolTips);
        Assert.True(ListTips.IsAttached(list));
        Assert.Equal("Sector 1 record: 4.321 by Sam", ListTips.NoteFor(item));

        // A second theme pass (a look change) keeps it.
        NativeTips.Silence(list);
        Assert.True(ListTips.IsAttached(list));
    }

    [Fact]
    public void A_list_that_never_asked_for_notes_gets_no_tip()
    {
        using var list = new ListView();

        NativeTips.Silence(list);

        Assert.False(ListTips.IsAttached(list));
        Assert.Null(ListTips.NoteFor(new ListViewItem("x")));
        Assert.Null(ListTips.NoteFor(null));
    }
}
