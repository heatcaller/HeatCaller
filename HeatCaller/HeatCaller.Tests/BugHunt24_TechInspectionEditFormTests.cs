namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, the tech inspection dialog: a new inspection opens on the
/// first driver in the list and fills in their car and class; picking the
/// driver actually at the table kept the first driver's car (the box was no
/// longer blank) and, when the picked driver had no class, the first
/// driver's class.
/// </summary>
public sealed class BugHunt24_TechInspectionEditFormTests
{
    private static readonly string[] Classes = { "Stock", "Modified" };

    [Fact]
    public void A_car_filled_in_from_the_first_driver_gives_way_to_the_picked_drivers_car()
    {
        // Aaron's car went in when the dialog opened on him; Zed is picked.
        Assert.Equal("TLR 22", TechInspectionEditForm.CarAfterDriverChange("B6.4", "B6.4", "TLR 22"));
    }

    [Fact]
    public void A_car_filled_in_from_the_first_driver_is_cleared_when_the_picked_driver_has_none()
    {
        Assert.Equal(string.Empty, TechInspectionEditForm.CarAfterDriverChange("B6.4", "B6.4", string.Empty));
    }

    [Fact]
    public void A_car_typed_by_hand_stays_through_a_change_of_driver()
    {
        Assert.Equal("Losi 22X", TechInspectionEditForm.CarAfterDriverChange("Losi 22X", "B6.4", "TLR 22"));

        // An inspection being edited: nothing was filled in by the dialog.
        Assert.Equal("B6.4", TechInspectionEditForm.CarAfterDriverChange("B6.4", string.Empty, "TLR 22"));
    }

    [Fact]
    public void A_blank_car_box_takes_the_picked_drivers_car()
    {
        Assert.Equal("TLR 22", TechInspectionEditForm.CarAfterDriverChange("", string.Empty, "TLR 22"));
    }

    [Fact]
    public void The_picked_drivers_class_is_found_in_any_capitals()
    {
        Assert.Equal("Modified", TechInspectionEditForm.ClassAfterDriverChange(Classes, "Stock", "Stock", "modified"));
    }

    [Fact]
    public void A_class_picked_from_the_first_driver_is_cleared_when_the_picked_driver_has_none()
    {
        Assert.Null(TechInspectionEditForm.ClassAfterDriverChange(Classes, "Stock", "Stock", string.Empty));
        Assert.Null(TechInspectionEditForm.ClassAfterDriverChange(Classes, "Stock", "Stock", "Deleted class"));
    }

    [Fact]
    public void A_class_picked_by_hand_or_by_check_in_stays_when_the_picked_driver_has_none()
    {
        Assert.Equal("Modified", TechInspectionEditForm.ClassAfterDriverChange(Classes, "Modified", "Stock", string.Empty));

        // An inspection being edited: the dialog picked nothing itself.
        Assert.Equal("Stock", TechInspectionEditForm.ClassAfterDriverChange(Classes, "Stock", null, string.Empty));
    }
}
