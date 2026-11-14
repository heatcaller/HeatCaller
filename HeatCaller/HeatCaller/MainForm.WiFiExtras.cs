namespace HeatCaller;

/// <summary>
/// What the club's own Wi-Fi pages are handed from the window (22 Oct 2026,
/// driver experience, package W1): the announcer's last few lines, so the
/// OBS overlay and broadcast graphics can carry captions (item 9).
/// </summary>
/// <remarks>
/// The follow card's penalty lines (item 1) and "Check my transponder"
/// (item 4) were handed over here too, until the Live Results page they were
/// on was taken off the LAN (13 Nov 2026). The driver's own night page on the
/// relay still has both - see DriverLive.
///
/// Threaded (24 Oct 2026, timing layer, P8) the state document is built off
/// the UI (MainForm.LiveState), so Captions is asked from a pool thread:
/// CaptionLog locks, so that is safe.
/// </remarks>
public partial class MainForm
{
    partial void WireWiFiExtras()
    {
        // The announcer's last few lines, for the captions strip. As heard:
        // the LAN pages are in the room - see WebServerService.Captions.
        _webServer.Captions = () => CaptionLog.Shared.Last(WebServerService.CaptionsInState);
    }
}
