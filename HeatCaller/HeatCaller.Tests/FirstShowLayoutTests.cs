using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace HeatCaller.Tests;

/// <summary>
/// Two things the user-guide screenshots caught on 19 Nov 2026.
///
/// Connection's Race tools card, the first time the page was shown at 1280
/// wide, was given room for its finish-camera buttons on one line; they
/// wrapped onto two, and Photo Finish was cut off under the card's foot
/// until something laid the page out again. CardColumns asked the card its
/// height at the START of the card's layout, before the row wrapped.
///
/// And a grid wider than its card - Championship's ten-column standings -
/// showed Windows' white horizontal bar: the slim drawn bar read a grid's
/// scroll range from its window, where a grid keeps none, and stayed
/// hidden.
/// </summary>
public sealed class FirstShowLayoutTests
{
    private static volatile string _now = "starting";

    private static void Settle(int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);

        do
        {
            StaThread.Settle();
            Thread.Sleep(15);
        }
        while (DateTime.UtcNow < until);
    }

    [Fact]
    public void Connections_race_tools_card_holds_its_wrapped_buttons_the_first_time_it_is_shown()
    {
        var problem = StaThread.Run(
            () =>
            {
                using var data = new TempAppData();

                new AppSettings
                {
                    FontScale = 1f,
                    StartMaximized = false,
                    HasCompletedFirstRun = true,
                    HasSeenRaceDaySetup = true,
                    AutoBackupEnabled = false,
                }.Save();

                _now = "building the window";
                var window = new MainForm();

                try
                {
                    window.LayoutCheckMode = true;
                    window.StartPosition = FormStartPosition.Manual;
                    window.Location = new Point(-20000, -20000);
                    window.Show();
                    Settle(300);

                    window.SizeContentForLayoutCheck(new Size(1280, 800));
                    Settle(300);

                    // Straight to Connection: the first time it is shown.
                    _now = "showing Connection";
                    window.ShowPageForLayoutCheck("Connection");
                    Settle(700);

                    var page = window.LayoutCheckPages.First(p => p.Name == "Connection").Page;
                    var tools = Descendants(page).OfType<Section>().First(s => s.Name == "Race tools");
                    var wanted = tools.GetPreferredSize(new Size(tools.Width, 0)).Height;
                    var lowest = Descendants(tools).Where(c => c.Visible && c is Button)
                        .Max(c => tools.PointToClient(c.PointToScreen(new Point(0, c.Height))).Y);

                    return tools.Height >= wanted && lowest <= tools.Height
                        ? null
                        : $"Race tools is {tools.Height}px tall; it needs {wanted}px, and its last button ends at {lowest}px.";
                }
                finally
                {
                    window.Close();
                    window.Dispose();
                }
            },
            TimeSpan.FromMinutes(4),
            () => _now);

        Assert.Null(problem);
    }

    [Fact]
    public void A_grid_wider_than_its_box_gets_the_slim_bar_along_its_foot()
    {
        var problem = StaThread.Run(
            () =>
            {
                using var window = new Form
                {
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-20000, -20000),
                    FormBorderStyle = FormBorderStyle.None,
                    ShowInTaskbar = false,
                    Size = new Size(420, 320),
                };

                var holder = new Panel { Bounds = new Rectangle(0, 0, 340, 300) };
                window.Controls.Add(holder);

                var grid = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    RowHeadersVisible = false,
                    AllowUserToAddRows = false,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                };

                // Ten columns that cannot be narrower than 60px: 600px of
                // table in a 340px box, as the standings are at 1280 wide.
                for (var i = 0; i < 10; i++)
                {
                    grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = $"C{i}", MinimumWidth = 60 });
                }

                holder.Controls.Add(grid);
                AppTheme.StyleGrid(grid);

                _now = "showing the grid";
                window.Show();
                Settle(1500);

                var native = grid.Controls.OfType<HScrollBar>().FirstOrDefault();
                var slim = holder.Controls.OfType<ThinScrollBar>().FirstOrDefault(bar => bar.Width > bar.Height);

                if (native is not { Visible: true })
                {
                    return "the grid never showed a horizontal bar of its own, so there is nothing to cover";
                }

                if (slim is not { Visible: true })
                {
                    return "the grid's own white horizontal bar is showing, with no slim bar over it";
                }

                // Dragging it moves the grid across, in pixels.
                typeof(ThinScrollBar)
                    .GetMethod("Send", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(slim, new object[] { 5, 120 });
                Settle(200);

                return grid.HorizontalScrollingOffset == 120
                    ? null
                    : $"dragging the slim bar to 120 left the grid at {grid.HorizontalScrollingOffset}px";
            },
            TimeSpan.FromMinutes(2),
            () => _now);

        Assert.Null(problem);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;

            foreach (var inner in Descendants(child))
            {
                yield return inner;
            }
        }
    }
}
