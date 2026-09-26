using System.Security.Cryptography;
using System.Text;

namespace HeatCaller;

/// <summary>
/// One phone allowed to drive the race-day manager through the relay, and
/// the key it signs its requests with.
///
/// The key is 32 random bytes as hex. It is minted here, handed to the
/// phone once by QR code at the desk, and kept sealed with the rest of the
/// secrets. It is never published, never sent to the relay, and never
/// leaves this laptop again by any path but that QR.
/// </summary>
public sealed class ManagerDevice
{
    /// <summary>What the phone calls itself on the wire. Public, and meaningless on its own.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>What the club calls it - "Dave's phone". For the list on screen; never checked.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The signing key, hex. Sealed with the other secrets.</summary>
    public string KeyHex { get; set; } = string.Empty;

    public DateTime EnrolledUtc { get; set; }

    /// <summary>When a signed request from it was last accepted, or null if never.</summary>
    public DateTime? LastSeenUtc { get; set; }
}

/// <summary>
/// Proof that a manager request came from a phone this club enrolled, and
/// not merely from the relay saying so.
///
/// WHY THIS EXISTS. A phone signs in to the RELAY with the manager PIN and
/// the relay then queues {reqId, body} for this laptop to collect and run.
/// The laptop's only evidence that a phone was ever involved is that the
/// relay says so. That is a fine arrangement while the relay is honest and
/// a poor one otherwise: whoever holds the relay host or its volume can
/// write into that queue directly and drive the race-day manager - set up
/// the day, change entries, post results. Race control is refused over the
/// relay whatever happens (RaceDayService.Run, and the manage page drops
/// the race tab outright), so the blast radius is bounded; bounded is not
/// the same as shut.
///
/// So the phone proves itself to THIS LAPTOP as well as to the relay. Every
/// request carries an HMAC-SHA256 over what is being asked for, keyed on a
/// secret only this laptop and that phone hold.
///
/// WHY THE KEY IS NOT THE PIN. The obvious key is the manager PIN, and it
/// is worthless. The relay holds sha256("hg-manager|club|pin") over a
/// six-digit space, so a relay that wanted to forge a request could work
/// back to the PIN in well under a second and sign whatever it liked -
/// forgeable by exactly the party the proof exists to keep out. Salting the
/// stored hash raises that from seconds to GPU-hours per club; it does not
/// make it impossible. A random 256-bit key the relay has never seen in any
/// form does.
///
/// WHAT IS SIGNED. The club, the device, the request id, a timestamp, and
/// the exact payload text the phone means the laptop to run - joined with
/// newlines under a version tag, so a signature for one club, one device or
/// one request can never be lifted onto another. The laptop then runs the
/// SIGNED payload and ignores the relay's own copy of it entirely: the
/// relay rebuilds the body it forwards, and a proof over text that is not
/// the text being executed proves nothing.
/// </summary>
internal static class RelayManagerDevices
{
    /// <summary>Key length. 256 bits, HMAC-SHA256's own block-filling size - no stretching, no truncation.</summary>
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

    /// <summary>Version tag on the signed text. A second scheme would use a second tag and be a different signature.</summary>
    private const string Scheme = "hg-manage-mac1";

    /// <summary>A new device id and key. The id is public; the key is handed over once and then only ever compared against.</summary>
    public static (string Id, string KeyHex) Mint()
    {
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(KeyBytes)).ToLowerInvariant();

        return (id, key);
    }

    /// <summary>
    /// What the phone puts in its QR code: the manager page, with the
    /// enrolment in the URL's FRAGMENT.
    ///
    /// The fragment, not the query, and that is the whole point of the
    /// shape. A fragment is never sent to the server, so the key does not
    /// reach the relay in a request line, a log, a proxy or a Referer
    /// header on the way past. The page reads it, stores it, and takes it
    /// straight back out of the address bar.
    /// </summary>
    public static string EnrolUrl(string managePageUrl, string id, string keyHex) =>
        $"{managePageUrl.TrimEnd('/')}#e={id}.{keyHex}";

    /// <summary>
    /// The exact text a signature is taken over. Both ends build this the
    /// same way or nothing verifies, so it is spelled out once here and
    /// once in the manager page's sign() - and pinned by a vector both
    /// sides' tests hold, the same arrangement RelayManage.ManagePinHash
    /// has with the relay's own managePinHash:
    ///
    ///   key       00112233445566778899aabbccddeeff repeated twice
    ///   club      acme-rc
    ///   device    0123456789abcdef01234567
    ///   reqId     req-1
    ///   sentAt    2026-09-25T10:00:00.000Z
    ///   payload   {"op":"Hello","args":{}}
    ///   =         f4d1ecd7e3b432f34b3b3541037b91f4577f0938e78faf4011daa76ead0440a4
    ///
    /// Newline separated, with the scheme tag first: no field can be slid
    /// into the next one, and a signature made under a future scheme could
    /// never be mistaken for one made under this.
    /// </summary>
    public static string SignedText(string club, string deviceId, string reqId, string timestamp, string payload) =>
        string.Join("\n", Scheme, club, deviceId, reqId, timestamp, payload);

    /// <summary>The signature itself, lower-case hex.</summary>
    public static string Sign(string keyHex, string club, string deviceId, string reqId, string timestamp, string payload)
    {
        var key = FromHex(keyHex);

        if (key is null)
        {
            return string.Empty;
        }

        using var mac = new HMACSHA256(key);

        return Convert.ToHexString(
            mac.ComputeHash(Encoding.UTF8.GetBytes(SignedText(club, deviceId, reqId, timestamp, payload))))
            .ToLowerInvariant();
    }

    /// <summary>
    /// Whether this request really came from the device it names.
    ///
    /// Everything is checked here and nothing is assumed of the caller: the
    /// device is one we enrolled, the timestamp reads as a time and is
    /// recent by our own clock, and the signature matches the text we
    /// rebuild rather than the text we were handed.
    /// </summary>
    public static bool Verify(
        IEnumerable<ManagerDevice> enrolled,
        string club,
        string deviceId,
        string reqId,
        string timestamp,
        string payload,
        string offeredMac,
        DateTime nowUtc,
        out ManagerDevice? device,
        out string refusal)
    {
        device = null;
        refusal = string.Empty;

        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(offeredMac) || string.IsNullOrWhiteSpace(timestamp))
        {
            refusal = "That request did not come from a phone this club has enrolled. Scan the enrolment code at the desk.";
            return false;
        }

        var known = enrolled.FirstOrDefault(d => string.Equals(d.Id, deviceId, StringComparison.Ordinal));

        if (known is null || FromHex(known.KeyHex) is null)
        {
            refusal = "That phone is no longer enrolled for the race-day manager. Scan the enrolment code at the desk again.";
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

        // Both directions. A request from the future is as much a sign of
        // something wrong as a stale one, and a clock set forward would
        // otherwise mint requests good for as long as it was wrong by.
        if ((nowUtc - sentUtc).Duration() > MaxAge)
        {
            refusal = "That request is too old to run - the phone's clock may be wrong, or it sat too long on the way. Try again.";
            return false;
        }

        var expected = Sign(known.KeyHex, club, deviceId, reqId, timestamp, payload);

        // Fixed-time, and over bytes rather than strings: an ordinary
        // comparison returns as soon as two characters differ, which is a
        // measurable amount of information about the right answer to
        // anybody who can ask often enough - and the relay can.
        if (expected.Length != offeredMac.Length
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected),
                Encoding.ASCII.GetBytes(offeredMac.ToLowerInvariant())))
        {
            refusal = "That request was not signed by the phone it claims to be from, so it has not been run.";
            return false;
        }

        device = known;
        return true;
    }

    // ------------------------------------------------------ PIN signing (v2)

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
