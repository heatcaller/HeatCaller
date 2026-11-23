using System.Drawing;
using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// A list in a grid cell is the app's own drop-down (22 Nov 2026, Asher):
/// Edit Result's Outcome column was the grid's own list, Windows' pale grey
/// box and arrow in every row of a dark window. The theme now draws every
/// grid's list cells (AppTheme.StyleGrid) - the cell's own background, the
/// pick and the app's chevron - and one click opens the list.
/// </summary>
public sealed class GridListCellTests
{
    private static SessionRecord Session() => new()
    {
        SessionType = RaceSessionType.Race,
        Label = "Stock - A-Main",
        Results =
        {
            new SessionResultEntry { Position = 1, Transponder = "1111111", DriverName = "Max Power", Laps = 12, ElapsedMs = 150_000, LapTimesMs = Enumerable.Repeat(12_500L, 12).ToList() },
            new SessionResultEntry { Position = 2, Transponder = "2222222", DriverName = "Sam Price", Laps = 11, ElapsedMs = 151_000, LapTimesMs = Enumerable.Repeat(13_700L, 11).ToList() },
        },
    };

    private static DataGridView EditGrid(ResultEditForm form) =>
        (DataGridView)typeof(ResultEditForm).GetField("_editGrid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

    [Fact]
    public void Edit_results_outcome_list_is_painted_on_the_cells_own_background()
    {
        var seen = StaThread.Run(() =>
        {
            using var form = new ResultEditForm(Session());

            form.Show();
            StaThread.Settle();

            var grid = EditGrid(form);
            var column = grid.Columns["outcome"]!.Index;

            // The second row: striped, so a box in Windows' colour behind
            // the pick shows against it. Sampled inside the cell's padding
            // and above the words, where the grid's own list had its field.
            var cell = grid.Rows[1].Cells[column];
            var at = grid.GetCellDisplayRectangle(column, 1, cutOverflow: false);

            int Sample()
            {
                StaThread.Settle();

                using var picture = new Bitmap(grid.Width, grid.Height);
                grid.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));

                return picture.GetPixel(at.Left + 40, at.Top + 5).ToArgb();
            }

            // Nothing chosen, so no cell is in the selection colour.
            grid.CurrentCell = null;
            grid.ClearSelection();

            var resting = Sample();

            cell.Selected = true;

            var chosen = Sample();

            return (
                isList: AppTheme.IsListCell(grid, 1, column),
                isText: AppTheme.IsListCell(grid, 1, grid.Columns["lapPenalty"]!.Index),
                resting,
                chosen,
                stripe: cell.InheritedStyle.BackColor.ToArgb(),
                selection: cell.InheritedStyle.SelectionBackColor.ToArgb());
        }, TimeSpan.FromSeconds(60), () => "painting Edit Result");

        Assert.True(seen.isList);
        Assert.False(seen.isText);
        Assert.Equal(seen.stripe, seen.resting);
        Assert.Equal(seen.selection, seen.chosen);
        Assert.NotEqual(seen.stripe, seen.selection);
    }

    [Fact]
    public void One_click_on_the_outcome_cell_puts_the_list_in_it()
    {
        var seen = StaThread.Run(() =>
        {
            using var form = new ResultEditForm(Session());

            form.Show();
            StaThread.Settle();

            var grid = EditGrid(form);
            var column = grid.Columns["outcome"]!.Index;

            // As a click does: the cell becomes the current one, then the
            // grid says it was clicked.
            grid.CurrentCell = grid.Rows[1].Cells[column];

            var before = grid.IsCurrentCellInEditMode;

            typeof(DataGridView).GetMethod("OnCellClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(grid, new object[] { new DataGridViewCellEventArgs(column, 1) });

            StaThread.Settle();

            var editing = grid.IsCurrentCellInEditMode && grid.EditingControl is ComboBox;

            // And a pick still takes effect as it is made, and the list
            // closing hands the cell back - no field left in it.
            if (grid.EditingControl is ComboBox list)
            {
                list.DroppedDown = true;
                list.SelectedItem = "DQ";
                list.DroppedDown = false;
                StaThread.Settle();
            }

            return (before, editing, picked: grid.Rows[1].Cells[column].Value as string, after: grid.IsCurrentCellInEditMode);
        }, TimeSpan.FromSeconds(60), () => "clicking Edit Result's Outcome");

        Assert.False(seen.before);
        Assert.True(seen.editing);
        Assert.Equal("DQ", seen.picked);
        Assert.False(seen.after);
    }

    private static CsvMappingForm CsvImport()
    {
        var lines = new[] { "Name,Transponder,Class", "Max Power,1111111,Stock", "Sam Price,2222222,Stock" };
        var headers = DriverImport.ParseLine(lines[0], ',');
        var rows = lines.Skip(1).Select(line => DriverImport.ParseLine(line, ',')).ToList();

        return new CsvMappingForm("drivers.csv", headers, rows, DriverImport.GuessAll(headers), Array.Empty<Season>());
    }

    private static DataGridView CsvGrid(CsvMappingForm form) =>
        (DataGridView)typeof(CsvMappingForm).GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

    /// <summary>
    /// The CSV import window's lists were themed by handlers of its own
    /// until the theme took them over: they still are the app's drop-down -
    /// no box in Windows' colour behind the pick, and one click opens one.
    /// </summary>
    [Fact]
    public void The_csv_import_windows_lists_are_still_the_apps_own()
    {
        var seen = StaThread.Run(() =>
        {
            using var form = CsvImport();

            form.Show();
            StaThread.Settle();

            var grid = CsvGrid(form);
            var cell = grid.Rows[0].Cells[1];
            var at = grid.GetCellDisplayRectangle(1, 0, cutOverflow: false);

            grid.CurrentCell = null;
            grid.ClearSelection();
            StaThread.Settle();

            using var picture = new Bitmap(grid.Width, grid.Height);
            grid.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));

            // Along the cell above the words and in the chevron's zone: the
            // grid's own list has a button there, a few pixels in.
            var zone = DropArrow.ZoneIn(at.Width, at.Height);
            var colours = new[] { at.Left + 40, at.Left + zone.Left + 2, at.Right - 4 }
                .Select(x => picture.GetPixel(x, at.Top + 5).ToArgb())
                .Distinct()
                .ToList();

            grid.CurrentCell = cell;

            typeof(DataGridView).GetMethod("OnCellClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(grid, new object[] { new DataGridViewCellEventArgs(1, 0) });

            StaThread.Settle();

            return (
                isList: AppTheme.IsListCell(grid, 0, 1),
                belowIsList: AppTheme.IsListCell(grid, 1, 1),
                colours,
                back: cell.InheritedStyle.BackColor.ToArgb(),
                editing: grid.IsCurrentCellInEditMode && grid.EditingControl is ComboBox);
        }, TimeSpan.FromSeconds(60), () => "painting the CSV import window");

        Assert.True(seen.isList);
        Assert.False(seen.belowIsList);
        Assert.Equal(new[] { seen.back }, seen.colours);
        Assert.True(seen.editing);
    }
}
