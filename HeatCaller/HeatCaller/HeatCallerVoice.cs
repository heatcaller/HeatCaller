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
/// <param name="Cloned">
/// The relay says this voice is a CLONE of a real person - a club's own
/// announcer. Nothing is spoken in it until the club has recorded that
/// they agreed; see VoiceConsent.
///
/// FROM THE RELAY AND NOWHERE ELSE, which is the point. The laptop is
/// never told whose voice it is, nor the real ElevenLabs id behind it -
/// only that consent is required. A laptop that decided this for itself
/// would decide wrong the first time somebody ran an old build, so it is
/// asked rather than assumed. The default is false only because a relay
/// that does not mention it has no clones to mention.
/// </param>
/// <param name="ClonedStated">
/// Whether the relay actually said true or false about Cloned. False when
/// the field was absent (an older relay): that is "not told", and the
/// sticky clone list in VoiceSettingsControl must not take a voice off on
/// the strength of it.
/// </param>
internal sealed record HeatCallerVoiceChoice(string Id, string Name, string What, string Rev, bool Cloned = false, bool ClonedStated = false)
{
    public override string ToString() => string.IsNullOrWhiteSpace(What) ? Name : $"{Name} - {What}";
}

/// <summary>
/// One voice in the relay's free sample list: the id to send when previewing
/// or choosing it, the name to show, and where its recording is - as the
/// relay gave it, relative to the relay's own root.
///
/// Apart from HeatCallerVoiceChoice above on purpose. A sample needs no
/// token and no plan, so the list of them answers for a club that has never
/// signed up - and a sample carries no revision, because nothing is keyed by
/// it: a sample is played once and thrown away, never saved as a line.
/// </summary>
internal sealed record HeatCallerVoiceSample(string Id, string Name, string Url)
{
    /// <summary>What a picker shows. Overridden for the same reason HeatCallerVoiceChoice overrides it: a record's own ToString prints its fields.</summary>
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Id : Name;
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

/// <summary>Why the relay would not speak a line. The app treats every one of them the same way: the free Kokoro voice says it instead.</summary>
internal enum HeatCallerRefusal
{
    /// <summary>Something else - a bad answer, a server error.</summary>
    Unknown,

    /// <summary>This relay has no ElevenLabs key: the HeatCaller voice is not switched on at all.</summary>
    Off,

    /// <summary>Not on this club's plan.</summary>
    NotIncluded,

    /// <summary>The month's characters are spent. Everything speaks with the Kokoro voice until it resets.</summary>
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

    /// <summary>
    /// The voice is a clone of a real person and this PC has no record
    /// that they agreed to it - see VoiceConsent.
    ///
    /// REFUSED BEFORE ANYTHING LEAVES THE LAPTOP, unlike every other
    /// refusal here, which are all answers from the relay. That is
    /// deliberate: a line already sent is a line already rendered in
    /// somebody's voice, and asking afterwards whether that was allowed is
    /// asking too late.
    /// </summary>
    NoConsent,
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
    /// still goes to the Kokoro voice, but the refusal says nothing about
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
/// HEAR IT BEFORE YOU BUY IT. Three of the endpoints below are about
/// choosing a voice rather than racing with one, and two of them carry no
/// token at all:
///
///   - Samples (<see cref="SamplesAsync"/>, <see cref="SampleAsync"/>) are
///     the relay's own fixed recording of each voice. No token, so they play
///     for a club that has never signed up and has no allowance to spend -
///     which is the whole point: a club should hear the voices before it is
///     asked to commit to anything.
///   - A preview (<see cref="PreviewAsync"/>) speaks the club's OWN words in
///     the chosen voice, with the token, charged to a small daily preview
///     allowance the relay keeps APART from the race-night voice budget. The
///     night's characters are for the night; trying wording out must never
///     eat into them, and when the preview allowance is gone the relay says
///     429 and the app says so in words rather than quietly spending the
///     other one.
///
/// NOTHING HERE EVER THROWS PAST ITS OWN DOOR except as
/// HeatCallerVoiceException, which the announcer turns into "the Kokoro
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
    /// <remarks>
    /// Settable so the tests can serve the relay's answers without a relay,
    /// exactly as KokoroSpeech.Http lets them serve a model download. The
    /// app never sets it.
    /// </remarks>
    internal static HttpClient Http { get; set; } = new(new HttpClientHandler { AllowAutoRedirect = false })
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

    /// <summary>
    /// Whether the consent gate in SpeakAsync would let a line out in this
    /// voice right now. For the takes that never reach SpeakAsync - the
    /// saved and prepared ones, rendered in the voice on an earlier night -
    /// so that taking a consent back stops those too, not only new lines.
    /// The same rule, read from the same settings.
    /// </summary>
    internal bool ConsentAllows(string? voiceId) =>
        VoiceConsent.Allows(VoiceConsent.MaySpeakWith(
            voiceId, _settings.HeatCallerVoiceIsCloned(voiceId), _settings.HeatCallerVoiceConsent()));

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
                            Text(entry, "rev"),

                            // Only the literal true. Anything else -
                            // absent, a string, a number - is not a clone,
                            // which is the same opt-in rule the relay's own
                            // config parser applies at the other end.
                            entry.TryGetProperty("cloned", out var cloned)
                                && cloned.ValueKind == JsonValueKind.True,
                            entry.TryGetProperty("cloned", out var stated)
                                && stated.ValueKind is JsonValueKind.True or JsonValueKind.False));
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
        // THE CONSENT BEHIND A CLONED VOICE, checked here and nowhere
        // else on the way out, because this is the one method every spoken
        // line in the app passes through on its way to the relay.
        //
        // BEFORE THE REQUEST, not after. Every other refusal in this class
        // is the relay's answer to something already sent - and a line
        // already sent is a line already rendered in somebody's voice, so
        // asking then whether that was allowed is asking too late.
        //
        // Fails closed: anything short of a recorded, unrevoked consent
        // naming somebody, FOR THIS VOICE, refuses - and a refusal here is
        // not a silence. It throws like every other one, the caller falls
        // back to the free Kokoro voice, and the console says which of
        // VoiceConsent's reasons it was and what to do about it.
        var allowed = VoiceConsent.MaySpeakWith(
            voiceId, _settings.HeatCallerVoiceIsCloned(voiceId), _settings.HeatCallerVoiceConsent());

        if (!VoiceConsent.Allows(allowed))
        {
            throw new HeatCallerVoiceException(
                HeatCallerRefusal.NoConsent,
                "Nothing was spoken in the club's own voice because " + VoiceConsent.Because(allowed));
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

    // --- Hear it before you buy it ------------------------------------------
    //
    // The three paths below are fixed by the relay's own contract and are
    // written out here rather than built from the club path above: the two
    // sample paths carry NO token and no club id, because they have to
    // answer for a laptop that has never been signed up - which is exactly
    // the laptop whose owner is deciding whether to sign up. Named as
    // constants so that, if the relay ever moves them, this is the one place
    // that has to know.

    /// <summary>The relay's catalogue of free samples. No token, no club.</summary>
    private const string SamplesPath = "/voice/samples";

    /// <summary>One free sample, by voice id. No token, no club. The catalogue gives this url itself; this is the fallback shape.</summary>
    private const string SamplePath = "/voice/sample/";

    /// <summary>A preview of the club's own words, on the preview allowance. WITH the club's voice token.</summary>
    private const string PreviewPath = "/voice/preview";

    /// <summary>The longest line a preview will carry. The relay caps it too; this saves a pointless round trip and a refusal a club has to read.</summary>
    internal const int MaxPreviewCharacters = 300;

    /// <summary>
    /// The relay root the SAMPLES are asked of: the voice relay's own when
    /// one is set, otherwise the club's.
    ///
    /// No club id and no token, so none of Target's credential rules apply -
    /// a sample is a fixed recording anybody may fetch, and that is what
    /// makes "hear it before you buy it" work on a laptop with nothing set
    /// up but an address. The restored-backup hold is still honoured: an
    /// address a restore brought in has not been looked at by anybody yet,
    /// and this laptop should not be reaching for it until it has.
    /// </summary>
    private string? SampleRoot()
    {
        var root = TargetRoot();

        if (root.Length == 0)
        {
            return null;
        }

        return HeldAfterRestore(root, RelayPublisher.NormaliseBaseUrl(_settings.RelayBaseUrl)) is null ? root : null;
    }

    /// <summary>True when there is an address to ask for samples. Needs no club and no token - see <see cref="SampleRoot"/>.</summary>
    internal bool SamplesReachable => SampleRoot() is not null;

    /// <summary>
    /// Why there is nowhere to ask for samples. Its own sentence rather than
    /// NoRelay's: that one sends a club to the Club Website page to set up
    /// publishing, and a club that only wants to HEAR the voices has not got
    /// that far yet and should not be told it must.
    /// </summary>
    private HeatCallerVoiceException NoSampleRelay() => new(
        HeatCallerRefusal.Off,
        HeldAfterRestore(TargetRoot(), RelayPublisher.NormaliseBaseUrl(_settings.RelayBaseUrl))
        ?? "There's no relay address to fetch the voice samples from yet. Fill the relay address in on the Club Website page - "
           + "the samples themselves are free and need no sign-up.");

    /// <summary>
    /// The free sample list. No token: this answers before a club has signed
    /// up, has a plan, or has any allowance at all.
    ///
    /// An empty list is a perfectly good answer - a relay with no samples
    /// recorded yet - and is not an error. What the page does with that is
    /// its business (it hides the Hear it buttons and says why).
    /// </summary>
    internal async Task<IReadOnlyList<HeatCallerVoiceSample>> SamplesAsync(CancellationToken cancel = default)
    {
        if (SampleRoot() is not { } root)
        {
            throw NoSampleRelay();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, root + SamplesPath);

        HttpResponseMessage response;

        try
        {
            response = await Http.SendAsync(request, cancel).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancel.IsCancellationRequested)
        {
            // HttpClient's own timeout arrives as a TaskCanceledException
            // with nobody having cancelled anything - see CatalogueAsync.
            throw new HeatCallerVoiceException(HeatCallerRefusal.Unreachable, $"The relay didn't answer ({Plain(ex)}).");
        }

        using (response)
        {
            var body = await ReadTextAsync(response, cancel, response.IsSuccessStatusCode ? CatalogueMaxBytes : RefusalMaxBytes)
                .ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // A relay from before samples existed. Said plainly rather
                // than as a 404: the club can ask whoever runs the relay.
                throw new HeatCallerVoiceException(
                    HeatCallerRefusal.Off,
                    "This relay doesn't offer voice samples yet.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HeatCallerVoiceException(
                    HeatCallerRefusal.Unknown,
                    $"The relay couldn't list the samples ({(int)response.StatusCode}).");
            }

            try
            {
                using var json = JsonDocument.Parse(body);
                var samples = new List<HeatCallerVoiceSample>();

                if (json.RootElement.TryGetProperty("samples", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in list.EnumerateArray())
                    {
                        var id = Text(entry, "id");

                        if (id.Length == 0)
                        {
                            continue;
                        }

                        // The url the relay gave, kept as it came. It is the
                        // relay's to decide, and guessing it here would go
                        // wrong the first time it changed - the fallback
                        // shape is only for a relay that leaves it out.
                        var url = Text(entry, "url");

                        samples.Add(new HeatCallerVoiceSample(
                            id,
                            Text(entry, "name") is { Length: > 0 } name ? name : id,
                            url.Length > 0 ? url : SamplePath + Uri.EscapeDataString(id)));
                    }
                }

                return samples;
            }
            // InvalidOperationException too: valid JSON of the wrong shape -
            // see CatalogueAsync, where the same thing had to be caught.
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                throw new HeatCallerVoiceException(HeatCallerRefusal.Unknown, "The relay's answer couldn't be read.");
            }
        }
    }

    /// <summary>
    /// One voice's free sample, as MP3 bytes. No token, nothing charged to
    /// anybody.
    /// </summary>
    /// <param name="urlOrId">
    /// The url the sample list gave for this voice (relative to the relay's
    /// root, which is the only form the relay sends), or the voice id on its
    /// own. An absolute address is refused rather than followed: the token
    /// does not ride on this request, but a relay answer that could send
    /// this laptop anywhere is still not something to act on.
    /// </param>
    internal async Task<byte[]> SampleAsync(string urlOrId, CancellationToken cancel = default)
    {
        if (SampleRoot() is not { } root)
        {
            throw NoSampleRelay();
        }

        if (string.IsNullOrWhiteSpace(urlOrId))
        {
            throw new HeatCallerVoiceException(HeatCallerRefusal.UnknownVoice, "No voice was named.");
        }

        var tail = urlOrId.Trim();

        if (tail.Contains("://", StringComparison.Ordinal) || tail.StartsWith("//", StringComparison.Ordinal))
        {
            throw new HeatCallerVoiceException(
                HeatCallerRefusal.Unknown,
                "The relay gave a sample address somewhere else entirely, so it wasn't fetched.");
        }

        var path = tail.StartsWith('/') ? tail : SamplePath + Uri.EscapeDataString(tail);

        using var request = new HttpRequestMessage(HttpMethod.Get, root + path);

        HttpResponseMessage response;

        try
        {
            response = await Http.SendAsync(request, cancel).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancel.IsCancellationRequested)
        {
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

                return audio;
            }

            // 404 is the relay's own answer for "no sample for that voice",
            // with a reason in the body - read like any other refusal so the
            // club sees the relay's words rather than a number.
            var body = await ReadTextAsync(response, cancel).ConfigureAwait(false);
            var (refusal, message, _) = ReadRefusal(response, body);

            throw new HeatCallerVoiceException(
                response.StatusCode == System.Net.HttpStatusCode.NotFound ? HeatCallerRefusal.UnknownVoice : refusal,
                message);
        }
    }

    /// <summary>
    /// The club's own words, spoken in one of the relay's voices, charged to
    /// the relay's small DAILY PREVIEW allowance - never to the club's
    /// race-night voice budget.
    /// </summary>
    /// <remarks>
    /// The separation is the relay's and is not negotiable from here: this
    /// endpoint spends the preview allowance and nothing else, and a spent
    /// preview allowance comes back as <see cref="HeatCallerRefusal.Busy"/>
    /// from a 429 with a sentence a club can read. The app must NOT answer
    /// that by asking SpeakAsync for the same line - the night's characters
    /// are for the night, and a page where "try it again" quietly eats the
    /// race budget is a page nobody can trust.
    /// </remarks>
    internal async Task<byte[]> PreviewAsync(string voiceId, string text, CancellationToken cancel = default)
    {
        if (Target() is not { } target)
        {
            throw NoRelay();
        }

        if (string.IsNullOrWhiteSpace(voiceId))
        {
            throw new HeatCallerVoiceException(HeatCallerRefusal.UnknownVoice, "No HeatCaller voice is chosen.");
        }
        // THE SAME GATE ON THE PREVIEW, and it is worth saying why rather
        // than treating it as thoroughness.
        //
        // A preview is a render of the voice, so previewing a cloned voice
        // with no consent on file renders a real person saying something
        // just as surely as a race call does. It is not the step where a
        // club DISCOVERS the voice either - the relay only offers a club a
        // cloned voice once its operator has put it on that club's list,
        // which means the club already arranged the clone. So there is no
        // chicken and egg here to be generous about: record the consent,
        // then listen.
        var mayPreview = VoiceConsent.MaySpeakWith(
            voiceId, _settings.HeatCallerVoiceIsCloned(voiceId), _settings.HeatCallerVoiceConsent());

        if (!VoiceConsent.Allows(mayPreview))
        {
            throw new HeatCallerVoiceException(
                HeatCallerRefusal.NoConsent,
                "That voice was not previewed because " + VoiceConsent.Because(mayPreview));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new HeatCallerVoiceException(HeatCallerRefusal.Text, "There was nothing to say.");
        }

        // Counted without the audio tags, for the same reason SpeakAsync
        // counts that way: the relay keeps the tags for a voice that can use
        // them and takes them out for one that can't, so this is the least it
        // could count. It is only a quick no for a line no voice could take.
        if (VoiceProfile.StripAudioTags(text).Length > MaxPreviewCharacters)
        {
            throw new HeatCallerVoiceException(
                HeatCallerRefusal.Text,
                $"A preview can be up to {MaxPreviewCharacters} characters. Shorten the line and try again.");
        }

        // voiceId and text, and nothing else. The relay fixes the model and
        // the voice settings for a preview exactly as it does for a race
        // line, so what a club hears here is what it will get.
        var payload = JsonSerializer.Serialize(new { voiceId, text });

        using var request = new HttpRequestMessage(HttpMethod.Post, target.Root + PreviewPath)
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

                return audio;
            }

            var body = await ReadTextAsync(response, cancel).ConfigureAwait(false);
            var (refusal, message, _) = ReadRefusal(response, body);

            // A spent preview allowance arrives as 429, which ReadRefusal
            // reads as Busy - "try later" - and that is right, because it
            // does start again. Said in the preview's own words, though: a
            // club reading "the HeatCaller voice is busy" would press the
            // button again all evening. NOTHING here falls through to
            // SpeakAsync; see the remarks above.
            throw new HeatCallerVoiceException(
                refusal,
                response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && body.Length == 0
                    ? "Today's previews are used up. They start again tomorrow - and this is a separate allowance, "
                      + "so your race-night voice is untouched."
                    : message);
        }
    }

    /// <summary>True when a refusal is the preview allowance being used up for today, rather than anything a club can fix now.</summary>
    internal static bool IsPreviewAllowanceSpent(HeatCallerVoiceException ex) =>
        ex.Refusal is HeatCallerRefusal.Busy or HeatCallerRefusal.Allowance;

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
            // went to the Kokoro voice.
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
