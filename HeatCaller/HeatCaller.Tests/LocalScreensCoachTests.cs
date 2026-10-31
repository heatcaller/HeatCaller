namespace HeatCaller.Tests;

/// <summary>
/// The phone coach page on Local Screens (29 Oct 2026).
///
/// /coach was served from 22 Oct 2026 but never listed with the other
/// phone pages, so a club looking on Local Screens for the page that reads
/// a driver's laps in their ear could not find it. It is now a row of its
/// own, "Driver coach:", with the Copy and QR code buttons the other phone
/// pages have. Windows only, like the other tests that build a page: a
/// window can't be made on Linux.
/// </summary>
public sealed class LocalScreensCoachTests : IDisposable
{
    // Declared first: everything below reads and writes where it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Local_Screens_lists_the_coach_page_under_check_in_with_copy_and_qr_code()
    {
        var settings = new AppSettings { DisplayLook = Looks.MidnightKey };
        var server = new WebServerService(new RaceTiming(new RaceControlSession()), ProfileStore.Load(), settings: settings);
        using var publisher = new RelayPublisher(new AppSettings { RelayEnabled = false });
        using var page = new WebServerControl(server, settings, () => { }, publisher, () => { });

        var captions = Descendants(page).OfType<Label>().ToList();
        var coach = captions.SingleOrDefault(label => label.Text == "Driver coach:");

        Assert.NotNull(coach);

        // In the same column as the other addresses, the row after check-in.
        var grid = Assert.IsAssignableFrom<TableLayoutPanel>(coach!.Parent);
        var checkIn = captions.Single(label => label.Text == "Driver check-in:");

        Assert.Same(grid, checkIn.Parent);
        Assert.Equal(grid.GetRow(checkIn) + 1, grid.GetRow(coach));

        // Its field: the address box, then Copy and QR code.
        var field = grid.Controls.Cast<Control>()
            .Single(control => !ReferenceEquals(control, coach)
                               && grid.GetRow(control) == grid.GetRow(coach)
                               && grid.GetColumn(control) == 1);
        var parts = field.Controls.Cast<Control>().ToList();

        var box = Assert.IsType<TextBox>(parts[0]);
        Assert.True(box.ReadOnly);

        // Empty until the server is running - the same as every other address.
        Assert.Equal(string.Empty, box.Text);
        Assert.Equal(new[] { "Copy", "QR code" }, parts.OfType<Button>().Select(button => button.Text).ToArray());
    }

    [Fact]
    public void The_coach_address_is_the_live_results_address_with_coach_on_the_end()
    {
        Assert.Equal("http://192.168.1.20:8080/coach", WebServerControl.CoachUrl("http://192.168.1.20:8080/"));

        // The address the poster tells drivers to scan leads to the page
        // that has the tick it names.
        var page = new WebServerService(
            new RaceTiming(new RaceControlSession()),
            ProfileStore.Load(),
            settings: new AppSettings { DisplayLook = Looks.MidnightKey }).BuildCoachPage();

        Assert.Contains("Say my lap time", page);
        Assert.Contains("Say my lap time", WebServerControl.CoachPosterSubtitle);
        Assert.Contains("Same Wi-Fi", WebServerControl.CoachPosterSubtitle);
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;

            foreach (var below in Descendants(child))
            {
                yield return below;
            }
        }
    }
}
