using System.Security.Cryptography;
using System.Text;

namespace HeatCaller;

/// <summary>
/// A check-in tap collected from the relay: which driver (by their
/// online id, not their transponder), in or out, and when it was made.
/// </summary>
public sealed record RelayCheckInTap(string Id, bool Wanted, DateTime AtUtc);

/// <summary>
/// The pieces online check-in and the iPhone-friendly badges share.
///
/// The central idea is the driver's ONLINE ID: a keyed hash of their
/// transponder. It is what the relay knows a driver by, what a badge's QR
/// code carries, and what comes back when somebody taps "check in".
///
///   - It is not the transponder. A transponder id on a public address
///     is a tracking key that follows somebody from meeting to meeting.
///   - It cannot be guessed. Eighty bits of HMAC, keyed by a secret that
///     never leaves this laptop - so holding a badge is what lets you
///     check that driver in, and nothing short of it does.
///   - It does not change on its own. Same transponder, same key, same
///     id, every meeting - so a badge printed once keeps working all
///     season, until the club chooses to make every id handed out so far
///     stop working (Reissue All Badges - see Reissue).
/// </summary>
internal static class RelayCheckIn
{
    /// <summary>
    /// The key, made the first time it is needed and kept from then on -
    /// replaced only by Reissue All Badges (see Reissue, and
    /// AppSettings.RelayCheckInSecret).
    /// </summary>
    public static string EnsureSecret(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.RelayCheckInSecret))
        {
            return settings.RelayCheckInSecret;
        }

        // Nothing minted while keys from a restore are still waiting to be
        // recovered - the same guard CanReissue and the other four minters
        // already have, and this was the one that did not.
        //
        // AppSettings preserves sealed secrets all-or-nothing on
        // HasAnySecret(), so writing ONE new field re-seals the blob with only
        // that field in it. This runs off MainForm's two-second roster timer,
        // which keeps pumping while the recovery prompt is open: on a restore
        // to a replacement laptop it minted a badge key within two seconds of
        // startup and saved, taking the relay token, the manager PIN, both web
        // PINs, the SMTP password, the backup password and every racer PIN out
        // of the file while the volunteer was still reading the dialog.
        //
        // Callers already cope with a blank key - see MainForm.RebuildBadgeIds
        // - and the real key comes back with the recovery.
        if (SecretRecovery.Pending)
        {
            return string.Empty;
        }

        settings.RelayCheckInSecret = NewSecret();

        if (!settings.Save())
        {
            // Not handed out. An id built from a key that is not on disk works
            // until the next restart and then stops, taking every badge
            // printed from it with it.
            settings.RelayCheckInSecret = string.Empty;
            return string.Empty;
        }

        return settings.RelayCheckInSecret;
    }

    /// <summary>
    /// Whether there is a key for Reissue All Badges to replace: one has
    /// been made (online check-in has been switched on at some point - no
    /// badge carries an online id until then), and no keys from a restore
    /// are still waiting to be recovered, which would put the old key back
    /// over a new one (see SecretRecovery.Pending).
    /// </summary>
    public static bool CanReissue(AppSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.RelayCheckInSecret) && !SecretRecovery.Pending;

    /// <summary>
    /// Replaces the key with a new one and saves it - Reissue All Badges on
    /// the Club Website page. Every online id is made from the key, so every one
    /// changes: every badge printed so far, and every check-in link anybody
    /// has kept, stops working. That is the point. A badge hands its id out
    /// for good, and so does the staff list - it shows every driver, id
    /// and all, to anybody with the staff PIN - so a new key is the only
    /// way to take ids back from a lost badge or from somebody who should
    /// no longer have them. The caller has the ids worked out again and the
    /// check-in list sent (RelaySettingsControl.BadgesReissued).
    ///
    /// Returns false, with the old key put back, when the new one could not
    /// be saved. As in EnsureSecret: ids made from a key that is not on disk
    /// work until the next restart and then stop, and the old key comes back
    /// from settings.json with them - so every badge printed in between dies
    /// and the lost badge the reissue was meant to revoke works again. Better
    /// that the reissue plainly did not happen, and the caller says so.
    /// </summary>
    public static bool Reissue(AppSettings settings)
    {
        var previous = settings.RelayCheckInSecret;

        settings.RelayCheckInSecret = NewSecret();

        if (!settings.Save())
        {
            settings.RelayCheckInSecret = previous;
            return false;
        }

        return true;
    }

    /// <summary>A new key: thirty-two random bytes, as hex. The one place a key is made - the first one and every reissue alike.</summary>
    private static string NewSecret() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    /// <summary>A driver's online id: twenty hex characters. Case and spacing in the transponder do not change it.</summary>
    public static string IdFor(string transponder, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(transponder.Trim().ToUpperInvariant()));

        return Convert.ToHexString(hash, 0, 10).ToLowerInvariant();
    }

    /// <summary>
    /// What the relay compares a typed PIN against. Must match the relay's
    /// pinHash() exactly. With the club's salt, the salted slow recipe
    /// (PinKdf - the vectors are in PinKdfTests and the relay's pinsalt
    /// tests); without one, the old single SHA-256, whose vector is
    /// sha256("hg-checkin|acme-rc|4321") = b79a728e...afc0e. The club is in
    /// it so the same PIN at two clubs hashes apart.
    /// </summary>
    public static string PinHash(string club, string pin, string salt = "") =>
        PinKdf.IsSalt(salt)
            ? PinKdf.Hash(PinKdf.CheckIn, club, salt, pin)
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"hg-checkin|{club}|{pin.Trim()}"))).ToLowerInvariant();

    /// <summary>
    /// Whether a PIN is still published at all: four to eight ASCII
    /// digits, which is what the relay accepts. Anything less is published
    /// as no PIN, which keeps the list closed - a three-digit PIN guarding
    /// every driver's name and photo is a thousand guesses.
    ///
    /// Four and five digits stay here on purpose, so a club that set one
    /// before the six-digit rule keeps a working desk rather than finding
    /// the list shut on race day. New PINs are held to IsStrongPin, and a
    /// shorter one already set is worth a warning (see IsWeakPin).
    /// </summary>
    public static bool IsUsablePin(string? pin) =>
        pin is { Length: >= 4 and <= 8 } && pin.All(char.IsAsciiDigit);

    /// <summary>
    /// The fewest digits a NEW staff PIN may have. The relay lets anybody
    /// try fifty PINs before closing sign-in for the club, and then keeps
    /// closing it for longer each time - about a hundred guesses a day for
    /// somebody who keeps at it. Against four digits that is weeks; against
    /// six it is years.
    /// </summary>
    public const int MinNewPinLength = 6;

    /// <summary>Whether a staff PIN is long enough to set now: six to eight ASCII digits.</summary>
    public static bool IsStrongPin(string? pin) =>
        pin is { Length: >= MinNewPinLength and <= 8 } && pin.All(char.IsAsciiDigit);

    /// <summary>
    /// A staff PIN that still works but is shorter than a new one may be:
    /// four or five digits, set before the six-digit rule. Worth telling
    /// the club to change, not worth closing the list over.
    /// </summary>
    public static bool IsWeakPin(string? pin) =>
        IsUsablePin(pin) && !IsStrongPin(pin);

    /// <summary>
    /// What the relay compares a typed PIN against to find the ONE racer it
    /// checks in - published as "rp" on that racer's entry in the check-in
    /// list. Must match the relay's racer-PIN hash exactly:
    /// sha256("hg-racer|" + club + "|" + pin), club in lower case.
    ///
    /// A different prefix from the staff PIN's, so a racer's PIN and the
    /// staff PIN can never hash alike even if they were the same digits.
    /// </summary>
    public static string RacerPinHash(string club, string pin, string salt = "") =>
        PinKdf.IsSalt(salt)
            ? PinKdf.Hash(PinKdf.Racer, club.ToLowerInvariant(), salt, pin)
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("hg-racer|" + club.ToLowerInvariant() + "|" + pin))).ToLowerInvariant();

    /// <summary>The club's online check-in page, where a racer types their PIN - or null when online check-in is not set up to answer there.</summary>
    public static string? CheckInPageUrl(AppSettings settings)
    {
        var root = RelayPublisher.NormaliseBaseUrl(settings.RelayBaseUrl);
        var club = settings.RelayClub.Trim().ToLowerInvariant();

        if (!settings.RelayEnabled || !settings.RelayCheckInEnabled || root.Length == 0 || club.Length == 0)
        {
            return null;
        }

        return $"{root}/c/{Uri.EscapeDataString(club)}/checkin";
    }

    /// <summary>The link a badge carries, or null when online check-in is not set up to answer it.</summary>
    public static string? BadgeUrl(AppSettings settings, string transponder)
    {
        var root = RelayPublisher.NormaliseBaseUrl(settings.RelayBaseUrl);

        // Lower case, as the publisher sends it and the relay holds it.
        var club = settings.RelayClub.Trim().ToLowerInvariant();

        if (!settings.RelayEnabled || !settings.RelayCheckInEnabled || root.Length == 0 || club.Length == 0
            || string.IsNullOrWhiteSpace(transponder))
        {
            return null;
        }

        // No key, no badge. EnsureSecret answers empty while a restore's keys
        // are still to be recovered, and an id hashed under an empty key is a
        // real-looking id that matches nothing - printed onto a badge it would
        // keep failing long after the recovery put the real key back.
        var secret = EnsureSecret(settings);

        if (string.IsNullOrWhiteSpace(secret))
        {
            return null;
        }

        var id = IdFor(transponder, secret);

        return $"{root}/c/{Uri.EscapeDataString(club)}/checkin?d={id}";
    }

    /// <summary>
    /// The online id in a scanned badge link, or null if what was scanned
    /// is not one. Deliberately does not care which relay or club the link
    /// names: a badge printed before the club moved relay still names the
    /// right driver, and the id alone is what identifies them.
    /// </summary>
    public static string? IdFromScan(string scanned)
    {
        if (!Uri.TryCreate(scanned.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !uri.AbsolutePath.TrimEnd('/').EndsWith("/checkin", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            if (pair.StartsWith("d=", StringComparison.Ordinal))
            {
                var id = pair[2..].ToLowerInvariant();
                return id.Length == 20 && id.All(Uri.IsHexDigit) ? id : null;
            }
        }

        return null;
    }
}
