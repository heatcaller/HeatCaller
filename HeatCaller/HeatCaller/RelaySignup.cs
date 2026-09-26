using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeatCaller;

/// <summary>Where one invite to sign up online stands.</summary>
public enum SignupInviteState
{
    /// <summary>Sent (or shown), and not yet answered. Works until it expires.</summary>
    Open = 0,

    /// <summary>The desk approved the sign-up it was used for; <see cref="SignupInvite.Transponder"/> is the driver it became.</summary>
    Approved = 1,

    /// <summary>The desk turned the sign-up down.</summary>
    Rejected = 2,

    /// <summary>Cancelled at the desk before it was used.</summary>
    Cancelled = 3,
}

/// <summary>
/// One invite to sign up online: a single-use code for one new driver,
/// emailed to them (or read out at the desk), that opens the club's
/// sign-up page for that one sign-up and nothing else.
///
/// The code itself is a secret and is kept sealed with the rest of the
/// store's codes (see <see cref="SignupStore"/>); only its hash is ever
/// published, exactly as the racers' own PINs are. The hash is worked out
/// from the code when it is needed and never written to disk - see Hash.
/// </summary>
public sealed class SignupInvite
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The code, as typed on the sign-up page. Sealed on disk - see SignupStore.</summary>
    [JsonIgnore]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// What the relay knows the code by: sha256("hg-signup|club|code"), or
    /// empty when the code itself isn't known here (sealed on another PC).
    ///
    /// Worked out from the code, and kept in memory only. It used to be
    /// saved in signups.json next to the sealed code - but it is an
    /// unsalted hash of eight digits, so anyone holding the file could try
    /// all hundred million codes in moments and read every code straight
    /// back, making the sealing pointless. The relay still needs this exact
    /// hash (the published document carries it, and sign-ups come back
    /// marked with it), so it is made here on demand. A "Hash" left in a
    /// file written by an older version is simply ignored when read, and
    /// gone after the next save.
    /// </summary>
    [JsonIgnore]
    public string Hash
    {
        get
        {
            if (Code.Length == 0)
            {
                return string.Empty;
            }

            // Cached against what it was made from, so a Club, Code or Salt
            // set afterwards (as loading does) can't leave a stale hash behind.
            var madeFrom = $"{Club}\n{Code}\n{Salt}";
            if (!string.Equals(_hashMadeFrom, madeFrom, StringComparison.Ordinal))
            {
                _hash = SignupStore.InviteHash(Club, Code, Salt);
                _hashMadeFrom = madeFrom;
            }

            return _hash;
        }
    }

    private string _hash = string.Empty;
    private string? _hashMadeFrom;

    /// <summary>The club the hash was made for. A different club id on the Club Website page makes every hash wrong.</summary>
    public string Club { get; set; } = string.Empty;

    /// <summary>
    /// The club's PIN salt this invite is hashed with (PinKdf), or empty for
    /// an invite made before there was one - which keeps the old unsalted
    /// hash it was published under until it expires, rather than changing
    /// identity under a sign-up already waiting on the relay. The relay tries
    /// both (server.ts's inviteHashFor). Not a secret.
    /// </summary>
    public string Salt { get; set; } = string.Empty;

    /// <summary>Where the invite was emailed, or empty for one shown at the desk.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Who it is for, in the desk's own words - "Sam's dad", "walk-up at the desk".</summary>
    public string Note { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresUtc { get; set; }
    public DateTime? EmailedUtc { get; set; }

    public SignupInviteState State { get; set; } = SignupInviteState.Open;
    public DateTime? AnsweredUtc { get; set; }

    /// <summary>The driver an approved sign-up became.</summary>
    public string Transponder { get; set; } = string.Empty;

    /// <summary>What the phone was told with the answer, kept so the answer can be sent again exactly.</summary>
    public string AnswerMessage { get; set; } = string.Empty;
    public bool AnswerCheckedIn { get; set; }

    [JsonIgnore]
    public bool IsExpired => State == SignupInviteState.Open && DateTime.UtcNow >= ExpiresUtc;

    /// <summary>Still usable on the sign-up page.</summary>
    [JsonIgnore]
    public bool IsUsable => State == SignupInviteState.Open && DateTime.UtcNow < ExpiresUtc;

    /// <summary>Who the invite is for, as a list shows it.</summary>
    [JsonIgnore]
    public string ForText =>
        Note.Length > 0 && Email.Length > 0 ? $"{Note} ({Email})"
        : Email.Length > 0 ? Email
        : Note.Length > 0 ? Note
        : "(given out at the desk)";

    [JsonIgnore]
    public string StateText => State switch
    {
        SignupInviteState.Approved => "Signed up",
        SignupInviteState.Rejected => "Turned down",
        SignupInviteState.Cancelled => "Cancelled",
        _ when IsExpired => "Expired",
        _ => EmailedUtc is null && Email.Length > 0 ? "Not emailed yet" : "Waiting",
    };
}

/// <summary>
/// A sign-up collected from the relay: what a new driver typed on the
/// sign-up page, and which invite they used (by its hash).
/// </summary>
/// <param name="TransponderKind">"own", "rental" or "unknown".</param>
public sealed record RelaySignupRequest(
    string Hash,
    string Name,
    string ClassName,
    string TransponderKind,
    string Transponder,
    string Car,
    string Email,
    bool Guardian,
    DateTime SubmittedUtc)
{
    public bool WantsRental => TransponderKind == "rental";
    public bool HasOwnTransponder => TransponderKind == "own" && Transponder.Length > 0;

    public string TransponderText =>
        HasOwnTransponder ? Transponder
        : WantsRental ? "Wants a rental"
        : "Doesn't know yet";

    /// <summary>
    /// The relay's own submittedAt, exactly as it sent it - empty if it
    /// sent none.
    ///
    /// SubmittedUtc is that time moved onto this laptop's clock, for
    /// showing, and the offset it is moved by is re-measured on every
    /// answer from the relay - so it comes out a little different each
    /// time the same sign-up is collected. Anything asking "is this the same
    /// sign-up as last time?" uses this instead (see SameSignup).
    /// </summary>
    public string SubmittedAtRaw { get; init; } = string.Empty;

    /// <summary>Which sign-up this is, as the relay holds it: the invite's hash and the relay's own time for it.</summary>
    public string Identity => $"{Hash}|{SubmittedAtRaw}";

    /// <summary>
    /// The same sign-up with the same details as <paramref name="other"/>.
    /// Everything but SubmittedUtc, which drifts from one collection to the
    /// next without the sign-up changing - compared on it, every collection
    /// looked like a change, and the Sign-ups window rebuilt its lists every
    /// ten seconds.
    /// </summary>
    public bool SameSignup(RelaySignupRequest other) =>
        this with { SubmittedUtc = default } == other with { SubmittedUtc = default };
}

/// <summary>
/// The club's invites to sign up online, and whether sign-up is open at
/// all. Kept in its own file beside the other stores; the codes are sealed
/// to this Windows account with DPAPI (SecretBox), as every other secret
/// is. A file whose codes cannot be opened here still opens, but its
/// invites cannot be shown, resent, published or matched to a sign-up -
/// the hashes are worked out from the codes and never stored (see
/// SignupInvite.Hash). Sign-ups made with them wait on the relay for the
/// PC that can open the codes (see RelaySignupDesk).
///
/// UI thread only, like every other store.
/// </summary>
public sealed class SignupStore
{
    /// <summary>How long an invite works. Long enough to sign up the week before a meeting; the relay keeps a waiting sign-up exactly as long.</summary>
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(14);

    /// <summary>Eight digits: distinct from the six-digit racer PINs at a glance, and a long way from guessable.</summary>
    public const int CodeLength = 8;

    /// <summary>Answered or cancelled invites are forgotten this long after, so the list stays about the club's current newcomers.</summary>
    private static readonly TimeSpan KeepAnswered = TimeSpan.FromDays(60);

    private sealed class FileShape
    {
        public bool Enabled { get; set; }
        public List<SignupInvite> Invites { get; set; } = new();

        /// <summary>Invite id -> code, sealed with SecretBox.</summary>
        public string SealedCodes { get; set; } = string.Empty;
    }

    private readonly List<SignupInvite> _invites;

    /// <summary>The sealed text as read, kept so a file sealed elsewhere is not overwritten with nothing until a new code is made.</summary>
    private string _sealedAsRead;

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "signups.json");

    private SignupStore(bool enabled, List<SignupInvite> invites, string sealedAsRead)
    {
        Enabled = enabled;
        _invites = invites;
        _sealedAsRead = sealedAsRead;
    }

    /// <summary>Let new drivers sign up online with an invite. Off until the club switches it on.</summary>
    public bool Enabled { get; set; }

    /// <summary>True when the codes were sealed on another PC or Windows account and could not be opened here.</summary>
    public bool CodesLocked { get; private set; }

    public static SignupStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var shape = JsonSerializer.Deserialize<FileShape>(SafeFile.ReadAllTextWithRetry(FilePath));

                if (shape is not null)
                {
                    var store = new SignupStore(shape.Enabled, shape.Invites ?? new List<SignupInvite>(), shape.SealedCodes ?? string.Empty);
                    store.OpenCodes();
                    store.Forget(DateTime.UtcNow);
                    return store;
                }
            }
        }
        catch (Exception ex)
        {
            // Unreadable: start empty rather than refusing to open. The
            // relay keeps any waiting sign-up; the desk can invite again.
            SafeFile.PreserveUnreadable(FilePath, ex);
        }

        return new SignupStore(false, new List<SignupInvite>(), string.Empty);
    }

    private void OpenCodes()
    {
        if (_sealedAsRead.Length == 0)
        {
            return;
        }

        try
        {
            var codes = JsonSerializer.Deserialize<Dictionary<string, string>>(SecretBox.Open(_sealedAsRead));

            foreach (var invite in _invites)
            {
                if (codes is not null && codes.TryGetValue(invite.Id, out var code))
                {
                    invite.Code = code;
                }
            }
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            CodesLocked = true;
        }
    }

    public void Save()
    {
        var codes = _invites
            .Where(i => i.Code.Length > 0)
            .ToDictionary(i => i.Id, i => i.Code, StringComparer.Ordinal);

        var sealedCodes = _sealedAsRead;

        // Sealed afresh unless this file's codes came from somewhere they
        // cannot be opened and nothing new has been made since: then the
        // original goes back untouched, still openable where it was made.
        if (!CodesLocked || codes.Count > 0)
        {
            try
            {
                sealedCodes = codes.Count == 0 ? string.Empty : SecretBox.Seal(JsonSerializer.Serialize(codes));
                _sealedAsRead = sealedCodes;
                CodesLocked = false;
            }
            catch (CryptographicException)
            {
                // Close to unheard of. The codes already sealed in the file
                // are written back as they were.
            }
        }

        var shape = new FileShape { Enabled = Enabled, Invites = _invites, SealedCodes = sealedCodes };
        SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(shape, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<SignupInvite> All => _invites.OrderByDescending(i => i.CreatedUtc).ToList();

    public SignupInvite? Get(string id) => _invites.FirstOrDefault(i => i.Id == id);

    /// <summary>The invite a relay sign-up was made with, for this club - or null.</summary>
    public SignupInvite? ByHash(string hash, string club) =>
        _invites.FirstOrDefault(i => string.Equals(i.Hash, hash, StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(i.Club, club, StringComparison.Ordinal));

    /// <summary>
    /// A new invite with a fresh code - unique among this club's codes, never
    /// an easy one (RacerPins.IsFit), and never one of the desk's own PINs.
    /// Not saved: the caller saves once it has made all it wants.
    /// </summary>
    public SignupInvite Create(AppSettings settings, string email, string note)
    {
        var club = settings.RelayClub.Trim().ToLowerInvariant();
        var reserved = new[] { settings.RelayCheckInPin, settings.WebServerPin, settings.WebServerCheckInPin, settings.ManagerPin }
            .Select(p => (p ?? string.Empty).Trim())
            .Where(p => p.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var taken = _invites.Select(i => i.Hash).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Salted and slow, like every PIN hash the relay is given (PinKdf).
        var salt = PinKdf.EnsureSalt(settings);

        for (var attempt = 0; attempt < 100_000; attempt++)
        {
            var code = RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
            var hash = InviteHash(club, code, salt);

            if (!RacerPins.IsFit(code[..6]) || reserved.Contains(code) || taken.Contains(hash))
            {
                continue;
            }

            var now = DateTime.UtcNow;
            var invite = new SignupInvite
            {
                Code = code,
                Club = club,
                Salt = salt,
                Email = email.Trim(),
                Note = note.Trim(),
                CreatedUtc = now,
                ExpiresUtc = now + InviteLifetime,
            };

            _invites.Add(invite);
            return invite;
        }

        throw new InvalidOperationException("Could not find an unused invite code.");
    }

    /// <summary>Answered and cancelled invites past KeepAnswered, and expired ones as long after they expired.</summary>
    private void Forget(DateTime nowUtc)
    {
        _invites.RemoveAll(i =>
            (i.State != SignupInviteState.Open && (i.AnsweredUtc ?? i.CreatedUtc) < nowUtc - KeepAnswered)
            || (i.State == SignupInviteState.Open && i.ExpiresUtc < nowUtc - KeepAnswered));
    }

    /// <summary>
    /// What the relay compares a typed invite code against. Must match the
    /// relay's signupPinHash() exactly: with a salt, the salted slow recipe
    /// (PinKdf, vectors in PinKdfTests); without, the old
    /// sha256("hg-signup|acme-rc|482913") = 770b79ba...fbee1e6e.
    /// </summary>
    public static string InviteHash(string club, string code, string salt = "") =>
        PinKdf.IsSalt(salt)
            ? PinKdf.Hash(PinKdf.Signup, club, salt, code)
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"hg-signup|{club}|{code.Trim()}"))).ToLowerInvariant();

    /// <summary>The club's sign-up page, or null when the relay is not set up.</summary>
    public static string? PageUrl(AppSettings settings)
    {
        var root = RelayPublisher.NormaliseBaseUrl(settings.RelayBaseUrl);
        var club = settings.RelayClub.Trim().ToLowerInvariant();

        if (!settings.RelayEnabled || root.Length == 0 || club.Length == 0)
        {
            return null;
        }

        return $"{root}/c/{Uri.EscapeDataString(club)}/signup";
    }

    /// <summary>The link an invite email carries: the sign-up page with the code after the #, which never reaches a server.</summary>
    public static string? InviteUrl(AppSettings settings, SignupInvite invite) =>
        PageUrl(settings) is { } page && invite.Code.Length > 0 ? $"{page}#code={invite.Code}" : null;

    /// <summary>
    /// The signup document for the relay: whether sign-up is open, the
    /// classes a new driver can pick, and the hash and expiry of every
    /// usable invite. Nothing else - no names, no emails.
    /// </summary>
    public string Document(AppSettings settings, string eventName, IReadOnlyList<string> classes)
    {
        var club = settings.RelayClub.Trim().ToLowerInvariant();

        // None at all while sign-up is off, so a club that closes it takes
        // every code off the relay rather than leaving them waiting there.
        var invites = _invites
            .Where(i => Enabled && i.IsUsable && i.Hash.Length > 0 && string.Equals(i.Club, club, StringComparison.Ordinal))
            .OrderBy(i => i.CreatedUtc)
            .Select(i => new
            {
                h = i.Hash,
                x = i.ExpiresUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
            })
            .ToList();

        return JsonSerializer.Serialize(new
        {
            enabled = Enabled,
            @event = eventName,
            classes,
            invites,

            // Tells the relay to hash a typed code with the salt too. Invites
            // made before there was one are still listed under their old hash,
            // and the relay tries both.
            pinSalt = PinKdf.EnsureSalt(settings),
        });
    }
}
