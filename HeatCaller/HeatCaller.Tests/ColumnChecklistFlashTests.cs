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
}
