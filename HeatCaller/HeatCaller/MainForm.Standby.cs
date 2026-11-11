namespace HeatCaller;

/// <summary>
/// The warm standby PC and the offline line, wired up (11 Nov 2026,
/// race-night build plan, Phase 6.2 and 6.3 - see WarmStandby and
/// OfflineFirst). Here because MainForm owns the three things they join:
/// the Local Screens server, the relay publisher and the Backup &amp;
/// Restore page.
/// </summary>
public partial class MainForm
{
    /// <summary>The standby side's copier. Made once with the Backup page; started only while this PC is a standby.</summary>
    private StandbyMirror? _standbyMirror;

    /// <summary>Makes the mirror, hands it and the server's facts to the page, and applies the settings. UI thread.</summary>
    private void SetUpStandby(BackupRestoreControl page)
    {
        _standbyMirror = new StandbyMirror(StandbyTarget);
        _standbyMirror.Logged += OnClientRawMessage;

        page.StandbyMirror = _standbyMirror;
        page.StandbyServeAddress = () => _webServer.IsRunning && WebServerService.GetLocalIPv4() is { } ip
            ? $"{ip}:{_webServer.Port}"
            : null;
        page.StandbyLastServed = () => (_webServer.StandbyLastServedUtc, _webServer.StandbyLastServedTo);
        page.StandbySettingsChanged += ApplyStandbySettings;

        ApplyStandbySettings();
    }

    /// <summary>
    /// What the mirror copies from, read from the settings on every ask.
    /// Read off the UI thread (the mirror's timer), but only two strings,
    /// each replaced whole when it changes.
    /// </summary>
    private StandbyMirror.Target StandbyTarget()
    {
        var address = WarmStandby.SnapshotAddress(_settings.StandbyPrimaryAddress, 8080, out var problem);
        return new StandbyMirror.Target(address, _settings.StandbyPin, problem);
    }

    /// <summary>
    /// The settings passed on: the server hands out copies only while this
    /// PC is the timing PC, and the mirror runs only while it is a standby
    /// (and has not taken over - the mirror checks that itself).
    /// </summary>
    private void ApplyStandbySettings()
    {
        _webServer.StandbyPin = _settings.StandbyPin;
        _webServer.StandbyServeEnabled = _settings.StandbyServeEnabled && !_settings.StandbyModeEnabled;

        if (_standbyMirror is null)
        {
            return;
        }

        if (_settings.StandbyModeEnabled)
        {
            _standbyMirror.Start();
        }
        else
        {
            _standbyMirror.Stop();
        }
    }

    /// <summary>The Next Up bar's offline-first line - see OfflineFirst.BarLine.</summary>
    private string? ResilienceLineForBar() =>
        OfflineFirst.BarLine(_relayPublisher.Status, _standbyMirror?.Status, DateTime.UtcNow);
}
