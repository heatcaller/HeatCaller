using System.Security.Cryptography;
using System.Text;

namespace HeatCaller;

/// <summary>
/// The one piece the race-day manager's relay tunnel needs that check-in's
/// own helpers do not already cover: hashing the manager PIN the same way
/// the relay does.
///
/// Everything else about the tunnel - the request/response shapes, the
/// answer cache that stops a retried request from running twice - lives in
/// RelayPublisher, next to CollectCheckIns; this file is deliberately as
/// small as RelayCheckIn.PinHash is, and for the same reason: a hash this
/// laptop and the relay must agree on byte-for-byte belongs somewhere it
/// cannot silently drift from the other one's copy.
/// </summary>
internal static class RelayManage
{
    /// <summary>
    /// What the relay compares a typed manager PIN against. Must match the
    /// relay's own managePinHash() exactly - its tests hold the vector
    /// sha256("hg-manager|acme-rc|123456") = 009b3c90...a89e61, and so must
    /// this. A prefix of its own, entirely apart from check-in's "hg-checkin"
    /// and "hg-racer": a club whose manager PIN happens to equal its check-in
    /// PIN must not let one open the other.
    /// </summary>
    public static string ManagePinHash(string club, string pin, string salt = "") =>
        PinKdf.IsSalt(salt)
            ? PinKdf.Hash(PinKdf.Manager, club, salt, pin)
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"hg-manager|{club}|{pin.Trim()}"))).ToLowerInvariant();
}
