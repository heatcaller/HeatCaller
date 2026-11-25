using System.Runtime.InteropServices;

namespace HeatCaller.Tests;

/// <summary>
/// The columns list's rows stay drawn as the mouse moves down it (25 Nov
/// 2026, Asher's recording of Race Control's columns list: "it's all
/// flashy"). The rows the mouse crossed were wiped and left for later, and
/// the description tip - a Windows tooltip, which puts back a copy of what
/// was under it rather than asking for a redraw - came down over them and
/// left them empty. Now a move draws the rows it changed there and then, so
/// nothing is waiting to be drawn when the tip moves.
/// </summary>
public sealed class ColumnChecklistFlashTests
{
    private const int WmMouseMove = 0x0200;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetUpdateRect(IntPtr hWnd, IntPtr rect, bool erase);

    [Fact]
    public void A_move_onto_a_new_row_leaves_nothing_waiting_to_be_drawn()
    {
        var waiting = StaThread.Run(() =>
        {
            using var form = new Form
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-20000, -20000),
                Size = new Size(500, 700),
                ShowInTaskbar = false,
            };

            var choices = new ColumnChoices(KpiCatalog.DefaultRaceControl, KpiCatalog.All);
            var list = new ColumnChecklist(choices) { ShowDescriptionTips = true, Dock = DockStyle.Fill };
            form.Controls.Add(list);

            form.Show();
            StaThread.Settle();
            list.Update();

            var seen = new List<bool>();

            for (var i = 0; i < Math.Min(6, list.RowCount); i++)
            {
                var row = list.GetItemRectangle(i);
                var at = new Point(row.Left + row.Width / 2, row.Top + row.Height / 2);

                // Straight to the list, with nothing run in between: what the
                // move leaves for later is still there to be seen.
                SendMessage(list.Handle, WmMouseMove, IntPtr.Zero, (IntPtr)((at.Y << 16) | at.X));
                seen.Add(GetUpdateRect(list.Handle, IntPtr.Zero, false));
            }

            form.Close();
            return seen;
        }, TimeSpan.FromMinutes(2), () => "moving the mouse down the columns list");

        Assert.Equal(6, waiting.Count);
        Assert.All(waiting, w => Assert.False(w));
    }

    /// <summary>
    /// Every row shows its words (25 Nov 2026, Asher's screenshot after #351:
    /// only Pos still had its name). Each row is drawn off-screen with that
    /// row's own starting point, and the words were drawn as if the row
    /// started at the top of the list, so all but the first row's landed
    /// outside the row and were never seen.
    /// </summary>
    [Fact]
    public void Every_row_shows_its_words()
    {
        var rows = StaThread.Run(() =>
        {
            using var form = new Form
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-20000, -20000),
                Size = new Size(500, 900),
                ShowInTaskbar = false,
            };

            var choices = new ColumnChoices(KpiCatalog.DefaultRaceControl, KpiCatalog.All);
            var list = new ColumnChecklist(choices) { Dock = DockStyle.Fill };
            form.Controls.Add(list);

            form.Show();
            StaThread.Settle();

            using var bmp = new Bitmap(list.Width, list.Height);
            list.DrawToBitmap(bmp, new Rectangle(Point.Empty, list.Size));
            var back = list.BackColor.ToArgb();
            var found = new List<(int Row, bool HasWords)>();

            for (var i = 0; i < Math.Min(10, list.RowCount); i++)
            {
                var r = list.GetItemRectangle(i);
                if (r.Bottom > bmp.Height)
                {
                    break;
                }

                // The words sit between the tick box and the arrows: anything
                // there but the row's background is the words.
                var words = false;
                for (var x = r.Left + 50; x < r.Left + 140 && !words; x++)
                {
                    for (var y = r.Top + 2; y < r.Bottom - 2 && !words; y++)
                    {
                        words = bmp.GetPixel(x, y).ToArgb() != back;
                    }
                }

                found.Add((i, words));
            }

            form.Close();
            return found;
        }, TimeSpan.FromMinutes(2), () => "drawing the columns list's rows");

        Assert.True(rows.Count >= 8, $"only {rows.Count} rows were drawn");
        Assert.All(rows, r => Assert.True(r.HasWords, $"row {r.Row} has no words"));
    }

    /// <summary>
    /// Asher, after the fix above: "still flashy but just a little at the top
    /// with the ticked options". Windows filled a row with the plain
    /// background before it was drawn, so a ticked row's pink box blinked.
    /// Now the fill goes only where there is no row.
    /// </summary>
    [Fact]
    public void Clearing_the_list_leaves_the_rows_alone_and_fills_only_below_them()
    {
        var seen = StaThread.Run(() =>
        {
            using var form = new Form
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-20000, -20000),
                Size = new Size(500, 700),
                ShowInTaskbar = false,
            };

            // A few columns in a tall list, so there is room below the rows.
            var offered = KpiCatalog.All.Take(4).ToList();
            var choices = new ColumnChoices(offered.Take(2).Select(d => d.Key), offered);
            var list = new ColumnChecklist(choices) { Dock = DockStyle.Fill };
            form.Controls.Add(list);

            form.Show();
            StaThread.Settle();

            var marker = Color.FromArgb(255, 1, 2, 3);
            using var bmp = new Bitmap(list.ClientSize.Width, list.ClientSize.Height);

            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(marker);
                var hdc = g.GetHdc();

                try
                {
                    SendMessage(list.Handle, 0x0014, hdc, IntPtr.Zero);
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }
            }

            var firstRow = list.GetItemRectangle(0);
            var below = list.BelowTheRows();

            var result = (
                RowKept: bmp.GetPixel(firstRow.Left + 3, firstRow.Top + firstRow.Height / 2),
                BelowFilled: bmp.GetPixel(below.Left + 3, below.Top + below.Height / 2),
                RoomBelow: below.Height,
                Back: list.BackColor);

            form.Close();
            return result;
        }, TimeSpan.FromMinutes(2), () => "clearing the columns list");

        Assert.True(seen.RoomBelow > 0, "the list should be taller than its rows for this test");
        Assert.Equal(Color.FromArgb(255, 1, 2, 3).ToArgb(), seen.RowKept.ToArgb());
        Assert.Equal(seen.Back.ToArgb(), seen.BelowFilled.ToArgb());
    }
}
