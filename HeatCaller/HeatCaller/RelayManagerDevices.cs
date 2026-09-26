using System.Security.Cryptography;
using System.Text;

namespace HeatCaller;

/// <summary>
/// Proof that a manager request arriving through the relay was written by
/// somebody who knows the manager PIN, and not made up by the relay.
///
/// WHY THIS EXISTS. A phone signs in to the RELAY with the manager PIN and
/// the relay then queues {reqId, body} for this laptop to collect and run.
/// Taken on the relay's word, whoever held the relay host or its volume
/// could write into that queue and drive the race-day manager - set up the
/// day, change entries, correct results. Race control is refused over the
/// relay whatever happens (RaceDayService.Run), so that was bounded; it was
/// not shut.
///
/// So every request is signed with a key made from the manager PIN
/// (PinKeyHex) - the phone makes it at sign-in, this laptop from its own
/// copy - and RelayPublisher.RunnableBody runs nothing that does not verify.
/// The relay never sees the key: it holds a hash of the PIN made for a
/// different purpose.
///
/// HISTORY. From 25 Sep each phone was enrolled at the desk with a random
/// key shown as a QR code. That could not be worked back from anything the
/// relay held; a key made from a six-digit PIN can, in minutes on a graphics
/// card, by a relay that sets out to (see PinKdf). On 26 Sep the club chose
/// the PIN - one thing to type, nothing to scan - knowing that trade. The
/// enrolment screen, the enrolled-key checks and the stored keys went with it.
///
/// WHAT IS SIGNED. The club, the phone's id (a label, not a credential), the
/// request id, a timestamp, and the exact payload text the phone means the
/// laptop to run - joined with newlines under a version tag, so a signature
/// for one club or one request can never be lifted onto another. The laptop
/// then runs the SIGNED payload and ignores the relay's own copy of it.
/// </summary>
internal static class RelayManagerDevices
{
    /// <summary>Key length. 256 bits, HMAC-SHA256's own block-filling size.</summary>
    public const int KeyBytes = 32;

    /// <summary>
    /// How far a request's timestamp may be from this laptop's clock.
    ///
    /// Judged against the LAPTOP's clock, deliberately, and not against the
    /// relay-corrected one the rest of this tunnel uses: the relay is the
    /// party being checked here, so a window it can slide is not a window.
    /// Five minutes each way covers a phone whose clock is a little out and
    /// a relay round trip on a bad connection, and leaves a stolen request
    /// useless within the length of a heat.
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    /// <summary>Version tag on text signed with the PIN's key. Never the same as the enrolled-key scheme's, so one can never pass for the other.</summary>
    private const string PinScheme = "hg-manage-mac2";

    /// <summary>
    /// The key a manager phone signs with: made from the manager PIN, the
    /// club and the club's salt (PinKdf.ManageKey). The phone makes it at
    /// sign-in from the PIN it has just typed; this laptop from its own copy.
    /// It is never sent anywhere. The relay holds a hash of the same PIN made
    /// with a different purpose, so holding that is not holding this.
    ///
    /// Replaces the QR-enrolled phone keys (26 Sep): the manager signs in
    /// with the PIN and nothing else. The trade is stated in PinKdf - a relay
    /// that set out to could recover a six-digit PIN from its stored hash in
    /// minutes and then sign as a manager, where a random enrolled key could
    /// not be recovered at all.
    /// </summary>
    public static string PinKeyHex(string managerPin, string club, string saltHex) =>
        PinKdf.Hash(PinKdf.ManageKey, club, saltHex, managerPin);

    /// <summary>
    /// The exact text a PIN signature is taken over - the enrolled scheme's
    /// shape under its own tag, pinned by a vector the relay's pinsalt tests
    /// and PinKdfTests both hold:
    ///
    ///   PIN       123456, salt 00112233445566778899aabbccddeeff twice, club acme-rc
    ///   key       dfc7298c66de999315ed4b72e1219af794b8020f71b9021f49a6d45b61154588
    ///   device    0123456789abcdef01234567
    ///   reqId     req-1
    ///   sentAt    2026-09-25T10:00:00.000Z
    ///   payload   {"op":"Hello","args":{}}
    ///   =         9f61d72c1ef12a9eac91e8ee9746f99e351549988ac2fbb9aa5b4ead48e25495
    /// </summary>
    public static string SignWithPinKey(string keyHex, string club, string deviceId, string reqId, string timestamp, string payload)
    {
        var key = FromHex(keyHex);

        if (key is null)
        {
            return string.Empty;
        }

        using var mac = new HMACSHA256(key);

        return Convert.ToHexString(
            mac.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", PinScheme, club, deviceId, reqId, timestamp, payload))))
            .ToLowerInvariant();
    }

    /// <summary>
    /// Whether this request was signed with the key the manager PIN makes -
    /// so written by somebody who knew the PIN, and not merely handed over by
    /// the relay. The same checks as Verify, against the PIN's key instead of
    /// an enrolled one: a phone id of the relay's own shape (a label, not a
    /// credential), a readable and recent timestamp by this laptop's clock,
    /// and the signature over the text rebuilt here.
    /// </summary>
    public static bool VerifyPinSigned(
        string managerPin,
        string saltHex,
        string club,
        string deviceId,
        string reqId,
        string timestamp,
        string payload,
        string offeredMac,
        DateTime nowUtc,
        out string refusal)
    {
        refusal = string.Empty;

        if (string.IsNullOrWhiteSpace(offeredMac) || string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(payload))
        {
            refusal = "That request was not signed with the manager PIN, so it has not been run. "
                      + "On the manager page, sign out and sign in again with the PIN.";
            return false;
        }

        if (deviceId.Length != 24 || !deviceId.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
        {
            refusal = "That request did not say which phone sent it, so it has not been run.";
            return false;
        }

        if (!DateTime.TryParse(
                timestamp,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var sentUtc))
        {
            refusal = "That request was not stamped with a readable time.";
            return false;
        }

        if ((nowUtc - sentUtc).Duration() > MaxAge)
        {
            refusal = "That request is too old to run - the phone's clock may be wrong, or it sat too long on the way. Try again.";
            return false;
        }

        var expected = SignWithPinKey(PinKeyHex(managerPin, club, saltHex), club, deviceId, reqId, timestamp, payload);

        // Fixed-time, as Verify: the relay can ask as often as it likes.
        if (expected.Length == 0
            || expected.Length != offeredMac.Length
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected),
                Encoding.ASCII.GetBytes(offeredMac.ToLowerInvariant())))
        {
            refusal = "That request was not signed with this club's manager PIN, so it has not been run. "
                      + "If the PIN was changed, sign out on the manager page and sign in again with the new one.";
            return false;
        }

        return true;
    }

    /// <summary>Hex to bytes, or null for anything that is not an even run of hex digits of the right length.</summary>
    private static byte[]? FromHex(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length != KeyBytes * 2)
        {
            return null;
        }

        try
        {
            return Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
