using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// One voice the HeatCaller relay offers, as its catalogue lists it.
///
/// Id is what is saved and sent; Rev is the relay's fingerprint for
/// everything that shapes the voice, and is part of a saved line's key so
/// a voice re-tuned on the relay is rendered again rather than played from
/// a take that no longer matches.
/// </summary>
internal sealed record HeatCallerVoiceChoice(string Id, string Name, string What, string Rev)
{
    public override string ToString() => string.IsNullOrWhiteSpace(What) ? Name : $"{Name} - {What}";
}

/// <summary>Where a club stands with its HeatCaller voice this month. Characters, because that is what ElevenLabs counts.</summary>
internal sealed record HeatCallerAllowance(int Used, int Allowance, DateTime? ResetsUtc)
{
    public bool Spent => Allowance > 0 && Used >= Allowance;

    /// <summary>"34,120 of 100,000 characters this month" - for the Voice Settings page.</summary>
    public string Describe() =>
        Allowance <= 0
            ? "No allowance on this plan."
            : $"{Used:N0} of {Allowance:N0} characters this month"
              + (ResetsUtc is { } resets ? $" - starts again {resets.ToLocalTime():d MMM}." : ".");
}

/// <summary>What the relay said about this club's HeatCaller voice.</summary>
internal sealed record HeatCallerCatalogue(bool Enabled, IReadOnlyList<HeatCallerVoiceChoice> Voices, HeatCallerAllowance Allowance);

/// <summary>Why the relay would not speak a line. The app treats every one of them the same way: the Windows voice says it instead.</summary>
internal enum HeatCallerRefusal
{
    /// <summary>Something else - a bad answer, a server error.</summary>
    Unknown,

    /// <summary>This relay has no ElevenLabs key: the HeatCaller voice is not switched on at all.</summary>
    Off,

    /// <summary>Not on this club's plan.</summary>
    NotIncluded,

    /// <summary>The month's characters are spent. Everything speaks with the Windows voice until it resets.</summary>
    Allowance,

    /// <summary>A voice this club may not use - usually one taken out of the catalogue since it was chosen.</summary>
    UnknownVoice,

    /// <summary>The line itself: empty, or longer than the relay speaks at once.</summary>
    Text,

    /// <summary>The relay or ElevenLabs was busy or would not answer. Worth trying again later.</summary>
    Busy,

    /// <summary>The relay could not be reached at all.</summary>
    Unreachable,

    /// <summary>The relay does not know this club, or the token is wrong.</summary>
    NotAllowed,
}

/// <summary>Thrown by <see cref="HeatCallerVoiceClient"/> with a reason the announcer can act on and a sentence a person can read.</summary>
internal sealed class HeatCallerVoiceException : Exception
{
    public HeatCallerVoiceException(HeatCallerRefusal refusal, string message, HeatCallerAllowance? allowance = null)
        : base(message)
    {
        Refusal = refusal;
        Allowance = allowance;
    }

    public HeatCallerRefusal Refusal { get; }

    /// <summary>Where the allowance stood when it was refused, when the relay said.</summary>
    public HeatCallerAllowance? Allowance { get; }

    /// <summary>
    /// True when the request was asked of the relay, voice or token as they
    /// were BEFORE Voice Settings or the Club Website page changed them. The line
    /// still goes to the Windows voice, but the refusal says nothing about
    /// the relay now in use, so the announcer does not rest over it. Set by
    /// the announcer, which is what knows when a change was made.
    /// </summary>
    public bool AskedBeforeReset { get; init; }
}

/// <summary>
/// Asks the club's HeatCaller relay to speak a line.
///
/// WHY THE RELAY AND NOT ELEVENLABS. In this mode the ElevenLabs account is
/// HeatCaller's, not the club's, so the key is on the relay and nothing
/// here ever sees it. The credential this sends is the club's own publish
/// token - the one the Club Website page already holds - so there is nothing
/// extra for a club to set up, and a club that has no relay has no
/// HeatCaller voice either.
///
/// WHAT IT IS ALLOWED TO ASK FOR. A voice, by the short id the catalogue
/// gave it, and the words. The model, the voice settings and the real
/// ElevenLabs voice are the relay's and are never sent from here - that is
/// what keeps a club's night sounding the way it was designed to, and
/// keeps the bill predictable.
///
/// NOTHING HERE EVER THROWS PAST ITS OWN DOOR except as
/// HeatCallerVoiceException, which the announcer turns into "the Windows
/// voice says this one".
/// </summary>
internal sealed class HeatCallerVoiceClient
{
    /// <summary>
    /// A client of its own, not RelayPublisher's.
    ///
    /// The publisher's is tuned for small JSON documents on a background
    /// thread: eight seconds, and a one-megabyte response cap. A rendered
    /// line is neither - it can take most of half a minute on a cold relay
    /// and comes back as audio - so a shared client would either cut
    /// renders short or make every publish wait far too long before giving
    /// up. Redirects are off for the same reason as the publisher's: the
    /// token rides on every request.
    /// </summary>
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(35),
        MaxResponseContentBufferSize = 8 * 1024 * 1024,
    };

    /// <summary>The longest line the relay will take. Its own cap is the one that counts; this saves a pointless round trip.</summary>
    internal const int MaxCharacters = 600;

    private readonly AppSettings _settings;

    internal HeatCallerVoiceClient(AppSettings settings) => _settings = settings;

    /// <summary>
    /// True when the voice is pointed at a relay of its own rather than the
    /// one the club publishes to. Said on the page and in the log, because
    /// hearing lines from a relay nobody else is using should never be a
    /// surprise.
    /// </summary>
    internal bool UsingVoiceRelay => RelayPublisher.NormaliseBaseUrl(_settings.VoiceRelayBaseUrl).Length > 0;

    /// <summary>Where this laptop's relay is, or null when the Club Website page has not been set up. Read once per call - the settings change on the UI thread.</summary>
    private (string Root, string Club, string Token)? Target()
    {
        // A voice relay of its own, when one is set: the address is what
        // switches it on, and what is left blank falls back to the
        // publishing settings - the club id always, the token only where
        // that is safe (see below). Publishing is never affected either
        // way; that is the whole point of keeping the two apart.
        var voiceRoot = RelayPublisher.NormaliseBaseUrl(_settings.VoiceRelayBaseUrl);
        var ownRelay = voiceRoot.Length > 0;

        var publishRoot = RelayPublisher.NormaliseBaseUrl(_settings.RelayBaseUrl);
        var root = ownRelay ? voiceRoot : publishRoot;

        // The club id is not a credential, so it always falls back.
        var club = ownRelay && !string.IsNullOrWhiteSpace(_settings.VoiceRelayClub)
            ? _settings.VoiceRelayClub.Trim().ToLowerInvariant()
            : (_settings.RelayClub ?? string.Empty).Trim().ToLowerInvariant();

        // The TOKEN is, and it is the one that can publish and delete as the
        // club - so it is only borrowed where borrowing it cannot hand it to
        // a stranger: the club's own relay (where it already goes), or a
        // relay on this laptop itself (localhost, 127.x.x.x, ::1). Anywhere
        // else the token has to be typed in on purpose. A mistyped hostname
        // that happens to resolve must not be a way to post the club's
        // publish token to it, on a page whose hint says only the voice
        // uses these. "The club's own network" used to count too, but a
        // .local or .lan name or a 192.168 address is whatever answers to it
        // on whichever Wi-Fi the laptop is on - and plain http is allowed
        // there, so the token went to it in the clear (see
        // RelayPublisher.IsLoopbackRoot). A typed token still may go over
        // plain http to a private IP address, as for the publisher - never
        // to a .local or .lan name (RelayPublisher.IsSafeForToken).
        var mayBorrowToken =
            !ownRelay
            || string.Equals(root, publishRoot, StringComparison.OrdinalIgnoreCase)
            || RelayPublisher.IsLoopbackRoot(root);

        var ownToken = ownRelay ? (_settings.VoiceRelayToken ?? string.Empty).Trim() : string.Empty;

        var token = ownToken.Length > 0
            ? ownToken
            : mayBorrowToken ? (_settings.RelayToken ?? string.Empty).Trim() : string.Empty;

        if (root.Length == 0 || club.Length == 0 || token.Length == 0 || !RelayPublisher.IsValidClubId(club))
        {
            return null;
        }

        // An address a restored backup brought in, not yet checked: no token
        // goes to it. See RestoredSettings - and NoRelay, which says so.
        if (HeldAfterRestore(root, publishRoot) is not null)
        {
            return null;
        }

        // The token never leaves this process over plain http to anywhere
        // but the local network - the same gate the publisher uses.
        if (!RelayPublisher.IsSafeForToken(root))
        {
            return null;
        }

        return (root, club, token);
    }

    /// <summary>True when there is a relay to ask at all. Not a promise that the club's plan includes the voice - only the relay knows that.</summary>
    internal bool RelayConfigured => Target() is not null;

    /// <summary>
    /// Why there is nothing to ask. Two different situations wearing the
    /// same refusal: sending a club to the Club Website page when the fault is in
    /// the voice relay boxes on the page they are looking at would be a
    /// wild goose chase.
    /// </summary>
    private HeatCallerVoiceException NoRelay() => new(
        HeatCallerRefusal.Off,
        HeldAfterRestore(TargetRoot(), RelayPublisher.NormaliseBaseUrl(_settings.RelayBaseUrl))
        ?? (UsingVoiceRelay
            ? "The voice relay on the Voice Settings page can't be used yet: it needs a club id and a token, and unless it is "
              + "this club's own relay or a relay on this PC (localhost), the token has to be typed in rather than "
              + "left blank. A plain http address is only accepted for this PC or your own network."
            : "The Club Website page isn't set up yet, so there's no HeatCaller relay to speak through."));

    /// <summary>The address Target asks: the voice relay's own when one is set, otherwise the club's.</summary>
    private string TargetRoot()
    {
        var voiceRoot = RelayPublisher.NormaliseBaseUrl(_settings.VoiceRelayBaseUrl);
        return voiceRoot.Length > 0 ? voiceRoot : RelayPublisher.NormaliseBaseUrl(_settings.RelayBaseUrl);
    }

    /// <summary>
    /// Why <paramref name="root"/> may not be asked yet, or null when it may:
    /// it came from a restored backup and has not been checked where it is
    /// shown (RestoredSettings). The club's relay is checked on the Club
    /// Website page - the voice speaks through it whenever the voice relay
    /// is that same address, however it was typed; a voice relay of its own
    /// is checked on Voice Settings.
    /// </summary>
    private static string? HeldAfterRestore(string root, string publishRoot)
    {
        var isClubRelay = string.Equals(root, publishRoot, StringComparison.OrdinalIgnoreCase);

        if (isClubRelay && RestoredSettings.IsHeld(RestoredSettings.Part.PublishRelay))
        {
            return "The Club Website page's relay settings came from a restored backup and haven't been checked yet, so the "
                   + "voice isn't asking the relay. Check them there, then press Save.";
        }

        if (!isClubRelay && RestoredSettings.IsHeld(RestoredSettings.Part.VoiceRelay))
        {
            return RestoredSettings.VoiceRelayMessage;
        }

        return null;
    }

    /// <summary>
    /// The voices this club may use and where its allowance stands. Cheap,
    /// and safe to call from the UI thread's async handlers: the Voice
    /// Settings page asks as it opens and when the club presses Refresh.
    /// </summary>
    internal async Task<HeatCallerCatalogue> CatalogueAsync(CancellationToken cancel = default)
    {
        if (Target() is not { } target)
        {
            throw NoRelay();
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{target.Root}/api/v1/c/{Uri.EscapeDataString(target.Club)}/voice/catalogue");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);

        HttpResponseMessage response;

        try
        {
            response = await Http.SendAsync(request, cancel).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancel.IsCancellationRequested)
        {
            // HttpClient's own timeout arrives as a TaskCanceledException
            // with nobody having cancelled anything. That is the relay not
            // answering, and it has to leave here as a refusal like any
            // other - the announcer and the Voice Settings page only know
            // what to do with HeatCallerVoiceException. Only a cancel the
            // caller asked for goes out as a cancel.
            throw new HeatCallerVoiceException(HeatCallerRefusal.Unreachable, $"The relay didn't answer ({Plain(ex)}).");
        }

        using (response)
        {
            // The catalogue itself read whole (to a generous cap): cut at the
            // 8 KB that is plenty for a refusal, a catalogue of a few dozen
            // voices stopped being JSON and read as "couldn't be read".
            var body = await ReadTextAsync(response, cancel, response.IsSuccessStatusCode ? CatalogueMaxBytes : RefusalMaxBytes)
                .ConfigureAwait(false);

            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                throw new HeatCallerVoiceException(
                    HeatCallerRefusal.NotAllowed,
                    "The relay didn't accept this club's token. Check the Club Website page.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // An older relay that has never heard of the voice. Said
                // plainly rather than as a 404, because the club can do
                // something about it: ask whoever runs the relay.
                throw new HeatCallerVoiceException(
                    HeatCallerRefusal.Off,
                    "This relay doesn't offer the HeatCaller voice yet.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HeatCallerVoiceException(
                    HeatCallerRefusal.Unknown,
                    $"The relay couldn't list the voices ({(int)response.StatusCode}).");
            }

            try
            {
                using var json = JsonDocument.Parse(body);
                var root = json.RootElement;
                var voices = new List<HeatCallerVoiceChoice>();

                if (root.TryGetProperty("voices", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in list.EnumerateArray())
                    {
                        var id = Text(entry, "id");

                        if (id.Length == 0)
                        {
                            continue;
                        }

                        voices.Add(new HeatCallerVoiceChoice(
                            id,
                            Text(entry, "name") is { Length: > 0 } name ? name : id,
                            Text(entry, "what"),
                            Text(entry, "rev")));
                    }
                }

                return new HeatCallerCatalogue(
                    root.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True,
                    voices,
                    AllowanceFrom(root));
            }
            // InvalidOperationException too: valid JSON of the wrong shape - an
            // array, or a voice listed as a bare string - throws that rather
            // than a JsonException from TryGetProperty, and nothing but a
            // HeatCallerVoiceException may leave this class.
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                throw new HeatCallerVoiceException(HeatCallerRefusal.Unknown, "The relay's answer couldn't be read.");
            }
        }
    }

    /// <summary>
    /// One line, spoken. Hands back MP3 bytes and what the relay said the
    /// allowance now is.
    /// </summary>
    /// <param name="shared">
    /// True only for a line with nothing filled into it - a flag call, a
    /// staging line - which the relay may keep for every club. A line with
    /// a driver's name in it must always be false: it is that club's, and
    /// some of the names are children's.
    /// </param>
    /// <returns>
    /// The audio, where the allowance stood, and whether the relay served it
    /// from its own cache. A cached line costs nothing and proves nothing
    /// about the allowance, which is why the announcer is told.
    /// </returns>
    internal async Task<(byte[] Audio, HeatCallerAllowance? Allowance, bool Cached)> SpeakAsync(
        string voiceId,
        string text,
        bool shared,
        CancellationToken cancel = default)
    {
        if (Target() is not { } target)
        {
            throw NoRelay();
        }

        if (string.IsNullOrWhiteSpace(voiceId))
        {
            throw new HeatCallerVoiceException(HeatCallerRefusal.UnknownVoice, "No HeatCaller voice is chosen.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new HeatCallerVoiceException(HeatCallerRefusal.Text, "There was nothing to say.");
        }

        // Audio tags are sent as written - the relay knows which of its
        // voices can use them and takes them out for the ones that can't.
        // So this counts the line WITHOUT them: the least the relay could
        // count, for a voice that can't use tags. It is only a quick no for
        // a line no voice could take. For a v3 voice the relay keeps the
        // tags and counts them too, so a tagged line can pass here and still
        // be refused there; that is why the announcer cuts a long line into
        // pieces measured WITH the tags before it ever reaches this method
        // (AnnouncementService.SplitForHeatCaller), and a piece that fits
        // that way fits on either kind of voice. Counting the tags here
        // instead would refuse lines a non-v3 voice speaks perfectly well.
        if (VoiceProfile.StripAudioTags(text).Length > MaxCharacters)
        {
            throw new HeatCallerVoiceException(
                HeatCallerRefusal.Text,
                $"That line is longer than the {MaxCharacters} characters the HeatCaller voice speaks at once.");
        }

        var payload = JsonSerializer.Serialize(new { voice = voiceId, text, shared });

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{target.Root}/api/v1/c/{Uri.EscapeDataString(target.Club)}/voice/speak")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);

        HttpResponseMessage response;

        try
        {
            response = await Http.SendAsync(request, cancel).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancel.IsCancellationRequested)
        {
            // HttpClient's own timeout arrives as a TaskCanceledException
            // with nobody having cancelled anything. That is the relay not
            // answering, and it has to leave here as a refusal like any
            // other - the announcer and the Voice Settings page only know
            // what to do with HeatCallerVoiceException. Only a cancel the
            // caller asked for goes out as a cancel.
            throw new HeatCallerVoiceException(HeatCallerRefusal.Unreachable, $"The relay didn't answer ({Plain(ex)}).");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                var audio = await response.Content.ReadAsByteArrayAsync(cancel).ConfigureAwait(false);

                if (audio.Length == 0)
                {
                    throw new HeatCallerVoiceException(HeatCallerRefusal.Unknown, "The relay answered with no audio.");
                }

                return (
                    audio,
                    AllowanceFromHeaders(response),
                    string.Equals(Header(response, "x-voice-cache"), "hit", StringComparison.OrdinalIgnoreCase));
            }

            var body = await ReadTextAsync(response, cancel).ConfigureAwait(false);
            var (refusal, message, allowance) = ReadRefusal(response, body);

            throw new HeatCallerVoiceException(refusal, message, allowance);
        }
    }

    /// <summary>Turns the relay's refusal into something the announcer can act on. Never shows a raw status code to a club if the relay gave words.</summary>
    private static (HeatCallerRefusal Refusal, string Message, HeatCallerAllowance? Allowance) ReadRefusal(
        HttpResponseMessage response,
        string body)
    {
        var said = string.Empty;
        var named = string.Empty;
        HeatCallerAllowance? allowance = null;

        try
        {
            using var json = JsonDocument.Parse(body);
            said = Text(json.RootElement, "error");
            named = Text(json.RootElement, "refusal");

            if (json.RootElement.TryGetProperty("allowance", out _))
            {
                allowance = AllowanceFrom(json.RootElement);
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // An answer that is not JSON - a proxy's error page, say - or
            // JSON that isn't an object, which TryGetProperty throws on.
            // Either way the status code says what it can; left to escape,
            // this was not a HeatCallerVoiceException, so the line never
            // went to the Windows voice.
        }

        var refusal = named switch
        {
            "off" => HeatCallerRefusal.Off,
            "not-included" => HeatCallerRefusal.NotIncluded,
            "unknown-voice" => HeatCallerRefusal.UnknownVoice,
            "text" => HeatCallerRefusal.Text,
            "allowance" => HeatCallerRefusal.Allowance,
            "busy" => HeatCallerRefusal.Busy,
            "upstream" => HeatCallerRefusal.Busy,
            _ => (int)response.StatusCode switch
            {
                401 or 403 => HeatCallerRefusal.NotAllowed,
                402 => HeatCallerRefusal.Allowance,
                404 => HeatCallerRefusal.Off,
                429 or 503 => HeatCallerRefusal.Busy,
                _ => HeatCallerRefusal.Unknown,
            },
        };

        var message = said.Length > 0
            ? said
            : $"The HeatCaller voice couldn't speak that line ({(int)response.StatusCode}).";

        return (refusal, message, allowance);
    }

    private static HeatCallerAllowance AllowanceFrom(JsonElement root)
    {
        // Two shapes, because the catalogue nests it and a refusal does not.
        var holder = root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object ? usage : root;

        return new HeatCallerAllowance(
            Whole(holder, "used"),
            Whole(holder, "allowance"),
            When(holder, "resetsAt"));
    }

    private static HeatCallerAllowance? AllowanceFromHeaders(HttpResponseMessage response)
    {
        var used = Header(response, "x-voice-used");
        var allowance = Header(response, "x-voice-allowance");

        if (used is null || allowance is null)
        {
            return null;
        }

        return new HeatCallerAllowance(
            int.TryParse(used, out var u) ? u : 0,
            int.TryParse(allowance, out var a) ? a : 0,
            DateTime.TryParse(
                Header(response, "x-voice-resets"),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var resets)
                ? resets
                : null);
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    /// <summary>How much of a refusal's body is read - its words, and nothing else is wanted from it.</summary>
    private const int RefusalMaxBytes = 8 * 1024;

    /// <summary>How much of the voice catalogue is read. Far past any real catalogue; only here so the cap is a number, not the client's 8 MB.</summary>
    private const int CatalogueMaxBytes = 256 * 1024;

    /// <summary>Reads a body, capped: a relay answering with something enormous must not be read into memory whole.</summary>
    private static async Task<string> ReadTextAsync(HttpResponseMessage response, CancellationToken cancel, int maxBytes = RefusalMaxBytes)
    {
        try
        {
            var bytes = await response.Content.ReadAsByteArrayAsync(cancel).ConfigureAwait(false);

            return Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, maxBytes));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return string.Empty;
        }
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int Whole(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : 0;

    private static DateTime? When(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && DateTime.TryParse(
            value.GetString(),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var when)
            ? when
            : null;

    private static string Plain(Exception ex) => ex switch
    {
        TaskCanceledException => "it did not answer in time",
        HttpRequestException http when http.InnerException is not null => http.InnerException.Message,
        _ => ex.Message,
    };
}
