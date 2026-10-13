namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 7, the sidebar and the keys that move around the app: where a
/// dragged page lands on a group heading, the rail's mark, and which
/// focused controls keep Ctrl+Z for themselves.
/// </summary>
public sealed class BugHunt7NavTests
{
    // --- A page dropped on a group heading -----------------------------------

    /// <summary>
    /// Four pages in two groups, so the list has a heading above A and one
    /// above C. Nothing is shown and nothing scrolls, so a y is a y.
    /// </summary>
    private static SidebarNavList TwoGroups()
    {
        var list = new SidebarNavList
        {
            SectionOf = name => name is "A" or "B" ? "ONE" : "TWO",
        };

        list.SetItems(new[] { "A", "B", "C", "D" });

        return list;
    }

    private static int H => SidebarNavList.HeaderHeight;

    private static int I => SidebarNavList.ItemHeight;

    [Fact]
    public void A_page_dropped_on_a_heading_goes_to_the_top_of_that_group_not_the_bottom_of_the_list()
    {
        var target = StaThread.Run(() =>
        {
            using var list = TwoGroups();

            // D dragged up and let go on the TWO heading, just above C.
            return list.DropTargetIndex(H + 2 * I + 1, sourceIndex: 3);
        }, TimeSpan.FromSeconds(30), () => "dropping on a heading");

        Assert.Equal(2, target);
    }

    [Fact]
    public void A_page_dropped_on_the_first_heading_goes_to_the_top_of_the_list()
    {
        var target = StaThread.Run(() =>
        {
            using var list = TwoGroups();

            return list.DropTargetIndex(1, sourceIndex: 2);
        }, TimeSpan.FromSeconds(30), () => "dropping on the first heading");

        Assert.Equal(0, target);
    }

    [Fact]
    public void A_page_dragged_down_onto_a_heading_lands_just_above_it()
    {
        var target = StaThread.Run(() =>
        {
            using var list = TwoGroups();

            // A taken out first, so the row just above C is index 1 by then.
            return list.DropTargetIndex(H + 2 * I + 1, sourceIndex: 0);
        }, TimeSpan.FromSeconds(30), () => "dragging down onto a heading");

        Assert.Equal(1, target);
    }

    [Fact]
    public void A_drop_on_a_row_or_below_the_last_one_is_as_before()
    {
        var (onRow, pastEnd) = StaThread.Run(() =>
        {
            using var list = TwoGroups();

            return (list.DropTargetIndex(H + I + 1, sourceIndex: 3),
                    list.DropTargetIndex(2 * H + 4 * I + 5, sourceIndex: 0));
        }, TimeSpan.FromSeconds(30), () => "dropping on a row");

        Assert.Equal(1, onRow);
        Assert.Equal(3, pastEnd);
    }

    // --- The rail's mark -------------------------------------------------------

    [Fact]
    public void The_rail_shows_HC_for_the_app_not_HE()
    {
        Assert.Equal("HC", SidebarHeader.InitialsOf("HEATCALLER", string.Empty));
    }

    [Fact]
    public void Other_titles_keep_their_initials()
    {
        Assert.Equal("MZ", SidebarHeader.InitialsOf("Mini", "Zone"));
        Assert.Equal("CL", SidebarHeader.InitialsOf("Club", string.Empty));
    }

    // --- Ctrl+Z ---------------------------------------------------------------

    [Fact]
    public void Ctrl_Z_goes_to_the_app_from_a_drop_down_list_or_a_read_only_box()
    {
        var answers = StaThread.Run(() =>
        {
            using var dropDownList = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            using var typedCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
            using var readOnlyBox = new TextBox { ReadOnly = true };
            using var textBox = new TextBox();
            using var numberBox = new NumericUpDown();
            using var button = new Button();

            return new[]
            {
                MainForm.HasOwnUndo(dropDownList),
                MainForm.HasOwnUndo(typedCombo),
                MainForm.HasOwnUndo(readOnlyBox),
                MainForm.HasOwnUndo(textBox),
                MainForm.HasOwnUndo(numberBox),
                MainForm.HasOwnUndo(button),
                MainForm.HasOwnUndo(null),
            };
        }, TimeSpan.FromSeconds(30), () => "asking who owns Ctrl+Z");

        Assert.Equal(new[] { false, true, false, true, true, false, false }, answers);
    }
}
