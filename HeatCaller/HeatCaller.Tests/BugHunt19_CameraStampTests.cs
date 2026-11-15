namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 19 (14 Nov 2026), #35: the time written on each photo-finish
/// frame put the milliseconds after AM/PM in 12-hour mode ("7:14:05 PM.123").
///
/// The page's script runs in the browser, which a test here can't drive, so
/// that is checked as the laptop serves it.
/// </summary>
public sealed class BugHunt19_CameraStampTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void The_frame_stamp_has_the_browser_place_the_milliseconds_before_am_or_pm()
    {
        var page = new WebServerService(new RaceTiming(new RaceControlSession()), ProfileStore.Load()).BuildCameraPage();

        var at = page.IndexOf("stamp = d.toLocaleTimeString(", StringComparison.Ordinal);
        Assert.True(at >= 0, "the camera page should stamp each frame");
        var line = page[at..page.IndexOf('\n', at)];

        Assert.Contains("fractionalSecondDigits: 3", line);
        Assert.Contains("hour12: !window.HC24", line);

        // Nothing tacked on after the formatted time - that lands after the PM.
        Assert.DoesNotContain("getMilliseconds", line);
        Assert.EndsWith("});", line.TrimEnd());
    }
}
