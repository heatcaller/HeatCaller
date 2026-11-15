namespace HeatCaller.Tests;

/// <summary>
/// The leaderboard's columns as ticks and arrows (15 Nov 2026): the list a
/// column heading's right-click drops down on Race Control, and the full
/// picker's list. Shown columns first in board order, a divider, then the
/// rest in the catalogue's order; a ticked column joins the right-hand end of
/// the board; the last column can't be hidden.
/// </summary>
public sealed class ColumnChoicesTests
{
    private static ColumnChoices Choices(params string[] shown) => new(shown, KpiCatalog.All);

    [Fact]
    public void Shown_columns_come_first_in_board_order_then_a_divider_then_the_rest_in_catalogue_order()
    {
        var choices = Choices("laps", "driver", "pos");

        var rows = choices.Rows;

        Assert.Equal(new[] { "laps", "driver", "pos" }, rows.Take(3).Select(r => r.Column!.Key));
        Assert.All(rows.Take(3), r => Assert.True(r.Shown));
        Assert.True(rows[3].IsDivider);

        var hidden = rows.Skip(4).Select(r => r.Column!.Key).ToList();
        var expected = KpiCatalog.All.Select(d => d.Key).Where(k => k is not ("laps" or "driver" or "pos")).ToList();
        Assert.Equal(expected, hidden);
        Assert.All(rows.Skip(4), r => Assert.False(r.Shown));
    }

    [Fact]
    public void Ticking_a_hidden_column_puts_it_at_the_right_hand_end_of_the_board()
    {
        var choices = Choices("pos", "driver");

        Assert.True(choices.Toggle("best"));

        Assert.Equal(new[] { "pos", "driver", "best" }, choices.ShownKeys);
    }

    [Fact]
    public void Unticking_a_shown_column_sends_it_back_to_its_catalogue_place_among_the_hidden()
    {
        var choices = Choices("pos", "driver", "laps");

        Assert.True(choices.Toggle("driver"));

        Assert.Equal(new[] { "pos", "laps" }, choices.ShownKeys);
        var hidden = choices.Hidden.Select(d => d.Key).ToList();
        Assert.Equal(KpiCatalog.All.Select(d => d.Key).Where(k => k is not ("pos" or "laps")).ToList(), hidden);
    }

    [Fact]
    public void The_last_shown_column_cannot_be_hidden()
    {
        var choices = Choices("driver");

        Assert.False(choices.CanToggle("driver"));
        Assert.False(choices.Toggle("driver"));
        Assert.Equal(new[] { "driver" }, choices.ShownKeys);
    }

    [Fact]
    public void Moving_swaps_with_the_neighbour_and_stops_at_the_ends()
    {
        var choices = Choices("pos", "driver", "laps");

        Assert.True(choices.Move("laps", -1));
        Assert.Equal(new[] { "pos", "laps", "driver" }, choices.ShownKeys);

        Assert.False(choices.CanMove("pos", -1));
        Assert.False(choices.Move("pos", -1));
        Assert.False(choices.Move("driver", 1));

        // A hidden column has no place on the board to move from.
        Assert.False(choices.Move("best", -1));
    }

    [Fact]
    public void Every_change_is_announced_and_a_reset_to_what_is_already_shown_is_not()
    {
        var choices = Choices("pos", "driver");
        var changes = 0;
        choices.Changed += (_, _) => changes++;

        choices.Toggle("laps");
        choices.Move("laps", -1);
        choices.Set(new[] { "pos", "laps", "driver" });
        Assert.Equal(2, changes);

        choices.Set(KpiCatalog.DefaultRaceControl);
        Assert.Equal(3, changes);
        Assert.Equal(KpiCatalog.DefaultRaceControl, choices.ShownKeys);
    }

    [Fact]
    public void Columns_a_surface_cannot_show_and_unknown_keys_are_left_out()
    {
        var offered = KpiCatalog.All.Where(d => d.Key != "car").ToList();
        var choices = new ColumnChoices(new[] { "pos", "car", "no-such-column", "pos" }, offered);

        Assert.Equal(new[] { "pos" }, choices.ShownKeys);
        Assert.DoesNotContain(choices.Rows, r => r.Column?.Key == "car");
        Assert.False(choices.Toggle("car"));
    }

    [Fact]
    public void Nothing_shown_has_no_divider()
    {
        var choices = Choices();

        Assert.DoesNotContain(choices.Rows, r => r.IsDivider);
        Assert.Equal(KpiCatalog.All.Count, choices.Rows.Count);
    }

    [Fact]
    public void The_list_keeps_the_picked_column_picked_as_it_moves_and_changes_group()
    {
        var seen = StaThread.Run(() =>
        {
            var choices = Choices("pos", "driver", "laps");
            using var form = new Form();
            using var list = new ColumnChecklist(choices);
            form.Controls.Add(list);
            _ = list.Handle;

            list.SelectColumn("laps");
            list.MoveSelected(-1);
            var afterMove = (picked: list.SelectedColumn?.Key, order: choices.ShownKeys);

            list.ToggleSelected();
            return (afterMove, pickedAfterHide: list.SelectedColumn?.Key, shown: choices.IsShown("laps"));
        }, TimeSpan.FromSeconds(30), () => "moving and hiding a column in the list");

        Assert.Equal("laps", seen.afterMove.picked);
        Assert.Equal(new[] { "pos", "laps", "driver" }, seen.afterMove.order);
        Assert.Equal("laps", seen.pickedAfterHide);
        Assert.False(seen.shown);
    }

    [Fact]
    public void The_divider_cannot_be_picked()
    {
        var seen = StaThread.Run(() =>
        {
            var choices = Choices("pos", "driver");
            using var form = new Form();
            using var list = new ColumnChecklist(choices);
            form.Controls.Add(list);
            _ = list.Handle;

            list.SelectColumn("driver");
            list.SelectedIndex = 2; // the divider, walking down

            return (index: list.SelectedIndex, column: list.SelectedColumn?.Key);
        }, TimeSpan.FromSeconds(30), () => "picking the divider");

        Assert.Equal(3, seen.index);
        Assert.NotNull(seen.column);
    }
}
