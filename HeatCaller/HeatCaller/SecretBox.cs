using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Keeps API keys, tokens and PINs out of plain text on disk.
///
/// Two locks, for two different jobs:
///
///   Seal / Open         Windows DPAPI, tied to this Windows account. Used
///                       for everything written to %AppData%. Nothing to
///                       remember and nothing to type - but a sealed value
///                       can only be opened by the same account on the same
///                       PC, so it cannot be the only copy that travels in
///                       a backup.
///
///   PasswordSeal /      AES-GCM with a key stretched from the club's backup
///   PasswordOpen        password (PBKDF2-SHA256). Used only for the copy of
///                       the secrets inside a backup, so a backup restored on
///                       a new laptop can get its keys - and the badge key
///                       every printed badge depends on - back.
///
/// Both produce text, so they sit in JSON like any other value.
/// </summary>
internal static class SecretBox
{
    private const string DpapiPrefix = "dpapi2:";

    // Values sealed before the app was called HeatCaller. Still opened, with
    // the entropy they were sealed with; the next save seals them afresh
    // under DpapiPrefix.
    private const string LegacyDpapiPrefix = "dpapi1:";

    // Not a secret - DPAPI's own key is. This only stops another program
    // running as the same user from opening our values by accident through
    // the same call with no entropy.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HeatCaller.v2");

    /// <summary>Sealed text for a value, or empty for an empty value.</summary>
    public static string Seal(string? plain)
    {
        if (string.IsNullOrEmpty(plain))
        {
            return string.Empty;
        }

        var sealedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
        return DpapiPrefix + Convert.ToBase64String(sealedBytes);
    }

    /// <summary>
    /// The value inside sealed text. Empty in, empty out. Throws
    /// CryptographicException when it was sealed by another Windows account
    /// or PC - the caller decides what that means.
    /// </summary>
    public static string Open(string? sealedText)
    {
        if (string.IsNullOrEmpty(sealedText))
        {
            return string.Empty;
        }

        string prefix;
        byte[] entropy;

        if (sealedText.StartsWith(DpapiPrefix, StringComparison.Ordinal))
        {
            prefix = DpapiPrefix;
            entropy = Entropy;
        }
        else if (sealedText.StartsWith(LegacyDpapiPrefix, StringComparison.Ordinal))
        {
            prefix = LegacyDpapiPrefix;
            entropy = Encoding.UTF8.GetBytes(LegacyNames.SecretEntropy);
        }
        else
        {
            throw new CryptographicException("Not a sealed value.");
        }

        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(sealedText[prefix.Length..]);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Sealed value is damaged.", ex);
        }

        return Encoding.UTF8.GetString(ProtectedData.Unprotect(raw, entropy, DataProtectionScope.CurrentUser));
    }

    private const int PasswordIterations = 210_000;

    private sealed class PasswordEnvelope
    {
        public int V { get; set; } = 1;
        public string Kdf { get; set; } = "pbkdf2-sha256";
        public int Iterations { get; set; }
        public string Salt { get; set; } = string.Empty;
        public string Nonce { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
    }

    /// <summary>Encrypts text with a password, as JSON text safe to store anywhere.</summary>
    public static string PasswordSeal(string plain, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordIterations, HashAlgorithmName.SHA256, 32);

        var data = Encoding.UTF8.GetBytes(plain);
        var cipher = new byte[data.Length];
        var tag = new byte[16];

        using (var aes = new AesGcm(key, 16))
        {
            aes.Encrypt(nonce, data, cipher, tag);
        }

        CryptographicOperations.ZeroMemory(key);

        return JsonSerializer.Serialize(new PasswordEnvelope
        {
            Iterations = PasswordIterations,
            Salt = Convert.ToBase64String(salt),
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Data = Convert.ToBase64String(cipher),
        });
    }

    /// <summary>
    /// The text inside a PasswordSeal envelope, or null when the password
    /// is wrong or the envelope is damaged. Never throws.
    /// </summary>
    public static string? PasswordOpen(string envelopeJson, string password)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<PasswordEnvelope>(envelopeJson);
            if (envelope is null || envelope.V != 1 || envelope.Iterations is < 10_000 or > 5_000_000)
            {
                return null;
            }

            var salt = Convert.FromBase64String(envelope.Salt);
            var nonce = Convert.FromBase64String(envelope.Nonce);
            var tag = Convert.FromBase64String(envelope.Tag);
            var cipher = Convert.FromBase64String(envelope.Data);

            if (nonce.Length != 12 || tag.Length != 16)
            {
                return null;
            }

            var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, envelope.Iterations, HashAlgorithmName.SHA256, 32);
            var plain = new byte[cipher.Length];

            try
            {
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(nonce, cipher, tag, plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }

            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            // Wrong password (AuthenticationTagMismatchException), bad
            // base64, bad JSON - all mean the same thing to the caller.
            return null;
        }
    }
}
