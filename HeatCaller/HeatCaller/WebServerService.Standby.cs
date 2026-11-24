using System.Globalization;
using System.Net;

namespace HeatCaller;

/// <summary>
/// The timing PC's side of the warm standby (11 Nov 2026, race-night build
/// plan, Phase 6.2 - see WarmStandby): hands a copy of the race data to a
/// standby PC on the track's network.
///
/// WHY here, on the Local Screens server: it is already running on race
/// night, already listening on the track's network, and already has the
/// PIN checking (PinAccepted, with its per-device and whole-PIN limits) the
/// phones go through. A second listener would be a second port to open in
/// the firewall and a second thing to get wrong.
///
/// Guarded four ways, every one of them checked on every request:
///   1. Off unless the club ticks "Let a standby PC copy this one"
///      (StandbyServeEnabled) - answered 404 then, so nothing on the
///      network learns the address exists.
///   2. Only to an address on the track's own network (WarmStandby.IsLanAddress).
///   3. Only with the standby PIN, eight characters or more, in the X-Pin
///      header - never the address - with its own wrong-guess limit.
///   4. Read-only: GET, and the handler reads files and writes nothing.
/// </summary>
public sealed partial class WebServerService
{
    /// <summary>Hand out the copy at all. Set from AppSettings.StandbyServeEnabled.</summary>
    public bool StandbyServeEnabled { get; set; }

    /// <summary>The PIN a standby must give. Set from AppSettings.StandbyPin.</summary>
    public string StandbyPin
    {
        get => _standbyPin;
        set => _standbyPin = PinChanged(_standbyPin, value, _standbyPinLockout);
    }

    private string _standbyPin = string.Empty;

    /// <summary>
    /// The whole-PIN limit for the standby PIN: twenty wrong in five
    /// minutes from everybody together. Strict, because only one PC ever
    /// has a reason to know it and it types it from settings, never by
    /// hand - so a wrong one is a misconfigured PC or somebody guessing.
    /// </summary>
    private readonly PinLockout _standbyPinLockout = new(20);

    /// <summary>The last copy made, kept a moment so two standbys (or a retry) asking at once build it once.</summary>
    private (byte[] Zip, int Files, DateTime AtUtc)? _standbyCopy;

    private readonly object _standbyCopyLock = new();

    /// <summary>How long a copy is handed out again before a fresh one is made.</summary>
    private static readonly TimeSpan StandbyCopyFor = TimeSpan.FromSeconds(3);

    /// <summary>When a standby last collected a copy, for the Standby PC card on this PC. Null until one has.</summary>
    public DateTime? StandbyLastServedUtc { get; private set; }

    /// <summary>Who that was.</summary>
    public string? StandbyLastServedTo { get; private set; }

    private void HandleStandbySnapshot(HttpListenerContext context)
    {
        if (!StandbyServeEnabled)
        {
            context.Response.StatusCode = 404;
            WriteText(context, "Not found.");
            return;
        }

        if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 405;
            WriteText(context, "Read only.");
            return;
        }

        if (!WarmStandby.IsLanAddress(context.Request.RemoteEndPoint?.Address))
        {
            context.Response.StatusCode = 403;
            WriteJson(context, "{\"ok\":false,\"error\":\"The race data is only copied to PCs on the track's own network.\"}");
            return;
        }

        if (!WarmStandby.IsUsablePin(StandbyPin))
        {
            context.Response.StatusCode = 403;
            WriteJson(context, "{\"ok\":false,\"error\":\"The timing PC has no standby PIN of eight letters or digits yet. Set one on its Backup & Restore page.\"}");
            return;
        }

        if (!PinAccepted(context, StandbyPin, required: true, scope: "standby", isUsable: WarmStandby.IsUsablePin))
        {
            return;
        }

        byte[] zip;
        int files;
        DateTime atUtc;

        try
        {
            lock (_standbyCopyLock)
            {
                if (_standbyCopy is not { } kept || DateTime.UtcNow - kept.AtUtc >= StandbyCopyFor)
                {
                    using var buffer = new MemoryStream();
                    var count = BackupService.WriteStandbySnapshot(buffer);
                    kept = (buffer.ToArray(), count, DateTime.UtcNow);
                    _standbyCopy = kept;
                }

                (zip, files, atUtc) = kept;
            }
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = 503;
            WriteJson(context, "{\"ok\":false,\"error\":\"The timing PC couldn't make a copy just now. The standby will try again.\"}");

            try
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [standby] Couldn't make a copy for the standby PC: {ex.Message}");
            }
            catch
            {
                // A request thread; the log must not be what fails it.
            }

            return;
        }

        StandbyLastServedUtc = DateTime.UtcNow;
        StandbyLastServedTo = ClientKey(context);

        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/zip";
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers[WarmStandby.TakenAtHeader] = atUtc.ToString("O", CultureInfo.InvariantCulture);

        // WHY (23 Nov 2026, bug hunt 24, F325): how old the copy is (it can
        // be reused for a few seconds), on THIS PC's clock, so the standby
        // never takes one PC's clock from the other's. Its own stamp alone
        // made every copy look minutes or hours old on a standby whose clock
        // disagreed - see WarmStandby.CopyTakenOnThisClock.
        context.Response.Headers[WarmStandby.AgeHeader] = WarmStandby.ServedAgeHeaderValue(atUtc, DateTime.UtcNow);
        context.Response.Headers[WarmStandby.PrimaryNameHeader] = Environment.MachineName;
        context.Response.Headers["X-HeatCaller-Files"] = files.ToString(CultureInfo.InvariantCulture);
        context.Response.ContentLength64 = zip.Length;
        context.Response.OutputStream.Write(zip, 0, zip.Length);

        // Closed like every other answer this server writes, so the request
        // is let go of now and not whenever the garbage collector gets to it.
        context.Response.OutputStream.Close();
    }
}
