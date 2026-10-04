using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace HeatCaller;

/// <summary>
/// The one recipe every PIN hash the relay is given is made with, and the
/// key a manager phone signs with: PBKDF2-HMAC-SHA256, 100,000 rounds, over
/// "purpose|club|salt", with a random salt per club.
///
/// WHY. A PIN hash is published so that a copy of a relay document is not a
/// copy of the PIN. One SHA-256 over four to eight digits did not manage
/// that: every PIN at a club fell to a million hashes - well under a second
/// - and all of them together, since they shared everything but the digits
/// (25 Sep security audit). A salt per club and a hundred thousand rounds
/// make each guess cost what a hundred thousand did, and nothing worked out
/// for one club says anything about another.
///
/// What it does not do is make a short PIN strong. A six-digit PIN is a
/// million guesses; at this cost that is minutes on a graphics card, not
/// forever. Treat the manager PIN as known if the relay is ever
/// compromised, and change it.
///
/// MUST MATCH THE RELAY. server.ts's PIN_KDF_ITERATIONS and pinKdf(), and
/// the manage page's derivePinKey(), make the same bytes from the same
/// PIN; the vectors in PinKdfTests are held by the relay's pinsalt tests
/// too. Change one and nobody signs in.
/// </summary>
internal static class PinKdf
{
    /// <summary>Rounds. The relay pays this per sign-in attempt too, so it is not set higher.</summary>
    public const int Iterations = 100_000;

    /// <summary>Salt length in bytes; published as hex.</summary>
    public const int SaltBytes = 32;

    /// <summary>The staff check-in PIN.</summary>
    public const string CheckIn = "hg-checkin2";

    /// <summary>A racer's own check-in PIN.</summary>
    public const string Racer = "hg-racer2";

    /// <summary>The race-day manager PIN, as the relay checks it at sign-in.</summary>
    public const string Manager = "hg-manager2";

    /// <summary>A sign-up invite code.</summary>
    public const string Signup = "hg-signup2";

    /// <summary>The key a manager phone signs its requests with - never published, never sent: both ends make it from the PIN.</summary>
    public const string ManageKey = "hg-manage-key2";

    /// <summary>
    /// Every answer made so far. The check-in document is rebuilt every
    /// couple of seconds with a hash for every racer, and at a tenth of a
    /// second each that would be seconds of work per publish; the answers
    /// only change when a PIN, the club or the salt does. Bounded, so a
    /// season of rotated PINs cannot grow it for ever.
    /// </summary>
    private static readonly ConcurrentDictionary<string, string> Made = new(StringComparer.Ordinal);

    private const int MaxRemembered = 5000;

    /// <summary>The hash (or key), lower-case hex. The secret is trimmed, as every PIN always has been.</summary>
    public static string Hash(string purpose, string club, string saltHex, string secret)
    {
        var trimmed = secret.Trim();
        var cacheKey = string.Join("\n", purpose, club, saltHex, trimmed);

        if (Made.TryGetValue(cacheKey, out var known))
        {
            return known;
        }

        var bytes = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(trimmed),
            Encoding.UTF8.GetBytes($"{purpose}|{club}|{saltHex}"),
            Iterations,
            HashAlgorithmName.SHA256,
            32);

        var hex = Convert.ToHexString(bytes).ToLowerInvariant();

        if (Made.Count >= MaxRemembered)
        {
            Made.Clear();
        }

        Made[cacheKey] = hex;
        return hex;
    }

    /// <summary>Whether a salt is one this app made: 32 bytes as lower-case hex.</summary>
    public static bool IsSalt(string? salt) =>
        salt is { Length: SaltBytes * 2 } && salt.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    /// <summary>
    /// The club's salt, made the first time it is needed and kept in the
    /// settings from then on. Not a secret - it is published beside every
    /// hash - so it is an ordinary setting and travels in backups as one.
    /// Changing it would change every hash at once and sign every phone
    /// out, so nothing does.
    ///
    /// Empty when a new salt could not be saved: every caller then makes the
    /// older unsalted hash, which the relay still tries, and the next call
    /// makes a salt again.
    /// </summary>
    public static string EnsureSalt(AppSettings settings)
    {
        if (IsSalt(settings.RelayPinSalt))
        {
            return settings.RelayPinSalt;
        }

        settings.RelayPinSalt = Convert.ToHexString(RandomNumberGenerator.GetBytes(SaltBytes)).ToLowerInvariant();

        if (!settings.Save())
        {
            // Not handed out, as RelayCheckIn does with a badge key that did
            // not reach the disk. Save's answer used to be ignored, and the
            // salt went on being used for the rest of the session: sign-up
            // invites made then kept it in signups.json, the next launch made
            // a different one, and every one of those codes was "invalid" on
            // the sign-up page for good, with nothing to say why.
            settings.RelayPinSalt = string.Empty;

            if (!_saidNotSaved)
            {
                _saidNotSaved = true;
                SessionLog.Write("[relay] The club's PIN salt couldn't be saved to settings.json, so PIN and invite hashes stay unsalted until it can be.");
            }

            return string.Empty;
        }

        _saidNotSaved = false;
        return settings.RelayPinSalt;
    }

    // Said once per run of failures, not on every publish that asks.
    private static bool _saidNotSaved;
}
