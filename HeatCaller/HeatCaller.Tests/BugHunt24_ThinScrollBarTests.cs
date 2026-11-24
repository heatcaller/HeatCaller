using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24: the themed scrollbar over a grid counted hidden rows, so the
/// Race Day run sheet with its recorded races folded away showed a bar with
/// nothing to scroll, and the wheel and track clicks on it asked for hidden
/// rows and did nothing. And the wheel over the bar threw away every turn
/// under a full notch, so a touchpad did nothing there.
/// </summary>
public sealed class BugHunt24_ThinScrollBarTests
{
    private const int SB_LINEDOWN = 1;
    private const int SB_PAGEDOWN = 3;

    private static void OnUiThread(Action body) =>
        StaThread.Run(() =>
        {
            body();
            return true;
        }, TimeSpan.FromMinutes(1), () => "bug hunt 24, thin scroll bar");

    /// <summary>30 rows of 20px, rows 1 to 19 hidden - the run sheet's fold - leaving 11 visible.</summary>
    private static DataGridView FoldedGrid()
    {
        var grid = new DataGridView
        {
            AllowUserToAddRows = false,
            ColumnHeadersVisible = false,
            RowHeadersVisible = false,
        };

        grid.RowTemplate.Height = 20;
        grid.Columns.Add("race", "Race");

        for (var i = 0; i < 30; i++)
        {
            grid.Rows.Add($"Race {i}");
        }

        for (var i = 1; i <= 19; i++)
        {
            grid.Rows[i].Visible = false;
        }

        return grid;
    }

    private static (Form Host, DataGridView Grid) Shown(int gridHeight)
    {
        var host = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-20000, -20000),
            ClientSize = new Size(400, 600),
        };

        var grid = FoldedGrid();
        grid.Bounds = new Rectangle(0, 0, 300, gridHeight);
        host.Controls.Add(grid);

        ThinScrollBar.Attach(grid);
        host.Show();
        StaThread.Settle();
        ThinScrollBar.SyncNow();

        return (host, grid);
    }

    [Fact]
    public void A_grid_range_counts_only_visible_rows()
    {
        // 13 visible rows, all 13 on screen: nothing to scroll, however many are hidden.
        Assert.False(ThinScrollBar.GridRange(13, 13, 0).Scrollable);

        var range = ThinScrollBar.GridRange(30, 13, 5);
        Assert.True(range.Scrollable);
        Assert.Equal(29, range.Max);
        Assert.Equal(5, range.Pos);
    }

    [Fact]
    public void A_grid_step_is_kept_to_the_last_full_page_of_visible_rows()
    {
        Assert.Equal(1, ThinScrollBar.GridWantedPlace(SB_LINEDOWN, 0, 5, 11, 0));
        Assert.Equal(6, ThinScrollBar.GridWantedPlace(SB_PAGEDOWN, 5, 5, 11, 0));
        Assert.Equal(6, ThinScrollBar.GridWantedPlace(SB_LINEDOWN, 6, 5, 11, 0));
    }

    [Fact]
    public void Places_among_visible_rows_skip_the_hidden_ones()
    {
        OnUiThread(() =>
        {
            using var grid = FoldedGrid();

            Assert.Equal(11, ThinScrollBar.GridVisibleRows(grid));
            Assert.Equal(0, ThinScrollBar.GridVisibleBefore(grid, 0));
            Assert.Equal(1, ThinScrollBar.GridVisibleBefore(grid, 20));
            Assert.Equal(5, ThinScrollBar.GridVisibleBefore(grid, 24));

            Assert.Equal(0, ThinScrollBar.GridRowAtPlace(grid, 0));
            Assert.Equal(20, ThinScrollBar.GridRowAtPlace(grid, 1));
            Assert.Equal(29, ThinScrollBar.GridRowAtPlace(grid, 10));
            Assert.Equal(-1, ThinScrollBar.GridRowAtPlace(grid, 11));
        });
    }

    [Fact]
    public void A_folded_grid_whose_visible_rows_all_fit_shows_no_bar()
    {
        OnUiThread(() =>
        {
            // Room for 20 rows; 11 are visible and 19 hidden.
            var (host, grid) = Shown(gridHeight: 20 * 20 + 4);

            try
            {
                Assert.True(grid.DisplayedRowCount(false) >= 11);
                Assert.DoesNotContain(host.Controls.OfType<ThinScrollBar>(), bar => bar.Visible);
            }
            finally
            {
                host.Dispose();
            }
        });
    }

    [Fact]
    public void A_line_down_on_the_bar_steps_over_the_hidden_rows()
    {
        OnUiThread(() =>
        {
            // Room for 5 of the 11 visible rows, so it does scroll.
            var (host, grid) = Shown(gridHeight: 5 * 20 + 4);

            try
            {
                var bar = Assert.Single(host.Controls.OfType<ThinScrollBar>());
                Assert.True(bar.Visible);
                Assert.Equal(0, grid.FirstDisplayedScrollingRowIndex);

                typeof(ThinScrollBar)
                    .GetMethod("Send", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(bar, new object[] { SB_LINEDOWN, 0 });

                // The next visible row, not hidden row 1 (which the grid refuses).
                Assert.Equal(20, grid.FirstDisplayedScrollingRowIndex);
            }
            finally
            {
                host.Dispose();
            }
        });
    }

    [Fact]
    public void Small_wheel_turns_add_up_to_lines()
    {
        var left = 0;
        var total = 0;

        for (var i = 0; i < 4; i++)
        {
            total += ThinScrollBar.WheelSteps(30, 3, ref left);
        }

        // Four quarter-notches are one notch: three lines, not none.
        Assert.Equal(3, total);
    }

    [Fact]
    public void A_full_notch_is_still_its_lines_either_way()
    {
        var left = 0;
        Assert.Equal(3, ThinScrollBar.WheelSteps(120, 3, ref left));
        Assert.Equal(0, left);
        Assert.Equal(-3, ThinScrollBar.WheelSteps(-120, 3, ref left));
        Assert.Equal(0, left);
    }

    [Fact]
    public void A_notch_and_a_half_keeps_the_half_for_the_next_turn()
    {
        var left = 0;
        var total = ThinScrollBar.WheelSteps(180, 3, ref left);
        total += ThinScrollBar.WheelSteps(60, 3, ref left);

        Assert.Equal(6, total);
    }

    [Fact]
    public void A_turn_the_other_way_drops_what_was_left()
    {
        var left = 0;
        Assert.Equal(2, ThinScrollBar.WheelSteps(90, 3, ref left));

        // 10 left over upward; a small turn down must not spend it upward.
        Assert.Equal(0, ThinScrollBar.WheelSteps(-30, 3, ref left));
        Assert.True(left <= 0);
    }
}
