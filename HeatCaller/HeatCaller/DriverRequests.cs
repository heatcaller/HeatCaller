using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeatCaller;

/// <summary>
/// One thing a driver asked the desk for from their own phone: a change to
/// their own details, the battery pack they're running, or a query about one
/// of tonight's results. (22 Oct 2026, driver experience.)
///
/// Kept exactly as the relay handed it over (see
/// <see cref="DriverRequestStore.ParseInbox"/>), plus what the desk answered.
/// Nothing in here is ever acted on by itself: the desk approves, notes or
/// declines it in <see cref="DriverRequestsForm"/>.
/// </summary>
internal sealed class DriverRequest
{
    /// <summary>The relay's id for it - 24 lower-case hex characters. The phone was given the first six (<see cref="Ref"/>).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The driver's badge id (RelayCheckIn.IdFor) - never their transponder.</summary>
    public string Badge { get; set; } = string.Empty;

    /// <summary><see cref="DriverAsk.Details"/>, <see cref="DriverAsk.Pack"/> or <see cref="DriverAsk.Query"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>How the relay checked who sent it: <see cref="DriverAsk.ByPin"/> or <see cref="DriverAsk.ByBadge"/>.</summary>
    public string Proof { get; set; } = DriverAsk.ByBadge;

    /// <summary>The text fields they sent (announcer, pronouns, about, email; pack; race, m). An empty value means "clear it".</summary>
    public Dictionary<string, string> Texts { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The yes/no fields they sent (emailMyNight, noPhoto, noVideo, initials, guardian).</summary>
    public Dictionary<string, bool> Flags { get; set; } = new(StringComparer.Ordinal);

    /// <summary>When the relay took it from their phone, by the relay's clock.</summary>
    public DateTime AskedUtc { get; set; }

    /// <summary>When this laptop filed it.</summary>
    public DateTime FiledUtc { get; set; }

    /// <summary>One of the four phone states - see <see cref="DriverAsk"/>.</summary>
    public string State { get; set; } = DriverAsk.Waiting;

    /// <summary>What the desk said back - a decline's reason. Empty for none.</summary>
    public string Said { get; set; } = string.Empty;

    public DateTime? AnsweredUtc { get; set; }

    /// <summary>
    /// Declined because the same driver sent a newer details request. The
    /// phone sees "declined" with <see cref="DriverRequestStore.ReplacedSaid"/>;
    /// the desk sees "Replaced".
    /// </summary>
    public bool Replaced { get; set; }

    [JsonIgnore]
    public string Ref => DriverAsk.RefOf(Id);

    [JsonIgnore]
    public bool IsWaiting => State == DriverAsk.Waiting;

    /// <summary>A parent or guardian ticked "I'm making this change for them". Recorded and shown; nothing more is decided by it.</summary>
    [JsonIgnore]
    public bool Guardian => Flags.TryGetValue("guardian", out var guardian) && guardian;

    public string Text(string key) => Texts.TryGetValue(key, out var value) ? value : string.Empty;

    internal DriverRequest Copy() => new()
    {
        Id = Id,
        Badge = Badge,
        Kind = Kind,
        Proof = Proof,
        Texts = new Dictionary<string, string>(Texts, StringComparer.Ordinal),
        Flags = new Dictionary<string, bool>(Flags, StringComparer.Ordinal),
        AskedUtc = AskedUtc,
        FiledUtc = FiledUtc,
        State = State,
        Said = Said,
        AnsweredUtc = AnsweredUtc,
        Replaced = Replaced,
    };
}

/// <summary>
/// Every request drivers have sent from their phones, in
/// driver-requests.json. (22 Oct 2026, driver experience.)
///
/// THREAD-SAFE, like ClubNotes: requests are filed on the relay publisher's
/// own thread (RelayPublisher.Requests) while the desk reads and answers them
/// on the UI thread, and the phone's answers are read on the roster tick.
/// Every read hands out copies; every change goes through a method here,
/// under the lock, and the file is written under it too.
///
/// FILE FIRST, THEN CONFIRM: <see cref="FileAll"/> says which ids are safely
/// on disk, and only those are confirmed to the relay. A request that
/// couldn't be saved stays on the relay and comes again next poll, and the
/// duplicate is recognised by its id.
/// </summary>
internal sealed class DriverRequestStore
{
    /// <summary>The most requests held. Answered ones go first when it fills.</summary>
    internal const int MostHeld = 500;

    /// <summary>How long an answered request is kept (for the desk's list and the phone's status).</summary>
    internal static readonly TimeSpan KeepAnsweredFor = TimeSpan.FromDays(30);

    /// <summary>How far back the phone is shown its requests' answers (driverlive asks).</summary>
    internal static readonly TimeSpan AnswersShownFor = TimeSpan.FromHours(24);

    /// <summary>The most answers one phone is shown.</summary>
    internal const int MostAnswersShown = 10;

    /// <summary>
    /// The most result queries one driver can send in a night. One more is
    /// declined straight away with <see cref="TooManySaid"/>: the desk has a
    /// race night to run.
    /// </summary>
    internal const int MostQueriesPerNight = 10;

    internal const string TooManySaid = "Too many";

    internal const string ReplacedSaid = "Replaced by your newer request.";

    private sealed class RequestsFile
    {
        public List<DriverRequest> Requests { get; set; } = new();
    }

    private readonly string _path;
    private readonly RequestsFile _file;
    private readonly object _gate = new();
    private int _version;

    /// <summary>True while the last write of the list failed - what is in memory isn't all on disk. Under the lock.</summary>
    private bool _unsaved;

    private DriverRequestStore(string path, RequestsFile file)
    {
        _path = path;
        _file = file;
    }

    /// <summary>True when the file was there but couldn't be read - then nothing is written, and nothing collected is confirmed (see FileAll).</summary>
    internal bool LoadedUnreadable { get; private init; }

    /// <summary>Goes up on every change, for the roster tick and the desk window to notice one cheaply.</summary>
    internal int Version => Volatile.Read(ref _version);

    internal static DriverRequestStore Load()
    {
        var path = Path.Combine(AppPaths.Root, "driver-requests.json");
        var unreadable = false;

        try
        {
            if (File.Exists(path))
            {
                var file = JsonSerializer.Deserialize<RequestsFile>(SafeFile.ReadAllTextWithRetry(path));

                if (file is not null)
                {
                    file.Requests = (file.Requests ?? new List<DriverRequest>())
                        .Where(r => r is not null && r.Id.Length > 0)
                        .ToList();

                    foreach (var request in file.Requests)
                    {
                        request.Texts ??= new Dictionary<string, string>(StringComparer.Ordinal);
                        request.Flags ??= new Dictionary<string, bool>(StringComparer.Ordinal);
                        request.Said ??= string.Empty;
                        request.Badge ??= string.Empty;
                        request.Kind ??= string.Empty;
                        request.Proof ??= DriverAsk.ByBadge;

                        if (!DriverAsk.IsState(request.State))
                        {
                            request.State = DriverAsk.Waiting;
                        }
                    }

                    var store = new DriverRequestStore(path, file);
                    store.Prune(DateTime.UtcNow);
                    return store;
                }
            }
        }
        catch (Exception ex)
        {
            unreadable = SafeFile.HoldWritesAfterUnreadable(path, ex);
        }

        return new DriverRequestStore(path, new RequestsFile()) { LoadedUnreadable = unreadable };
    }

    /// <summary>Written under the lock. False when it didn't reach the disk.</summary>
    private bool Save()
    {
        if (LoadedUnreadable)
        {
            return false;
        }

        // WHY (23 Oct 2026, bug hunt 10): remembered, so FileAll doesn't
        // confirm a request the relay offers again while it is still only in
        // memory after a failed write (see FileAll). A later good save of the
        // whole list clears it.
        var ok = SafeFile.WriteAllText(_path, JsonSerializer.Serialize(_file, new JsonSerializerOptions { WriteIndented = true }));
        _unsaved = !ok;
        return ok;
    }

    private void Changed() => Interlocked.Increment(ref _version);

    /// <summary>Every request held, oldest first, as copies.</summary>
    internal IReadOnlyList<DriverRequest> All
    {
        get
        {
            lock (_gate)
            {
                return _file.Requests.OrderBy(r => r.AskedUtc).Select(r => r.Copy()).ToList();
            }
        }
    }

    /// <summary>The requests still waiting for the desk, oldest first, as copies.</summary>
    internal IReadOnlyList<DriverRequest> Waiting
    {
        get
        {
            lock (_gate)
            {
                return _file.Requests.Where(r => r.IsWaiting).OrderBy(r => r.AskedUtc).Select(r => r.Copy()).ToList();
            }
        }
    }

    internal int WaitingCount
    {
        get
        {
            lock (_gate)
            {
                return _file.Requests.Count(r => r.IsWaiting);
            }
        }
    }

    internal DriverRequest? Get(string id)
    {
        lock (_gate)
        {
            return _file.Requests.FirstOrDefault(r => r.Id == id)?.Copy();
        }
    }

    internal bool IsWaiting(string id)
    {
        lock (_gate)
        {
            return _file.Requests.Any(r => r.Id == id && r.IsWaiting);
        }
    }

    /// <summary>
    /// Files what one inbox poll brought, and says which ids may be
    /// confirmed to the relay: those filed now or held already - and only
    /// once they are on disk. With the file unwritable, none are: they stay
    /// on the relay and come again.
    ///
    /// Two rules are applied as each is filed, oldest first:
    /// a details request marks the same driver's older waiting details
    /// request Replaced; and a query past <see cref="MostQueriesPerNight"/>
    /// that night is declined with <see cref="TooManySaid"/>.
    /// </summary>
    internal IReadOnlyList<string> FileAll(IEnumerable<DriverRequest> incoming, DateTime nowUtc, out int filed)
    {
        filed = 0;
        var confirmable = new List<string>();

        lock (_gate)
        {
            foreach (var ask in incoming.OrderBy(a => a.AskedUtc))
            {
                if (_file.Requests.Any(r => r.Id == ask.Id))
                {
                    confirmable.Add(ask.Id);
                    continue;
                }

                // Full of requests nobody has answered: left on the relay,
                // which holds at most 200 for the club and then tells phones
                // the desk is busy. Answered ones make room first (Prune).
                Prune(nowUtc);

                if (_file.Requests.Count >= MostHeld)
                {
                    continue;
                }

                var request = ask.Copy();
                request.FiledUtc = nowUtc;
                request.State = DriverAsk.Waiting;
                request.Said = string.Empty;
                request.AnsweredUtc = null;
                request.Replaced = false;

                if (request.Kind == DriverAsk.Details)
                {
                    var older = _file.Requests.Where(r => r.IsWaiting && r.Kind == DriverAsk.Details && r.Badge == request.Badge).ToList();

                    // WHY (23 Oct 2026, bug hunt 10): the phone sends only
                    // what changed since its last send, so a newer request
                    // does not repeat the older one's fields - replacing the
                    // older threw its changes away (a parent's "no photo
                    // online" among them) while the phone was told it was
                    // replaced. Now what the older asked for is carried into
                    // the one that survives first (CarryForward), oldest
                    // first so the newest values win.
                    foreach (var old in older.Where(r => r.AskedUtc <= request.AskedUtc).OrderBy(r => r.AskedUtc))
                    {
                        if (CarryForward(old, request))
                        {
                            MarkReplaced(old, nowUtc);
                        }
                    }

                    // Only those it is newer than. One that arrived late but
                    // was asked earlier is itself the one replaced - into the
                    // newest one waiting.
                    if (older.Where(r => r.AskedUtc > request.AskedUtc).OrderByDescending(r => r.AskedUtc).FirstOrDefault() is { } keep
                        && CarryForward(request, keep))
                    {
                        MarkReplaced(request, nowUtc);
                    }
                }
                else if (request.Kind == DriverAsk.Crew)
                {
                    // A newer crew request takes in an older one still
                    // waiting (15 Nov 2026), the way details do: the phone
                    // sends only what changed, so the older one's fields ride
                    // along and only the newest one waits.
                    var older = _file.Requests.Where(r => r.IsWaiting && r.Kind == DriverAsk.Crew && r.Badge == request.Badge).ToList();

                    foreach (var old in older.Where(r => r.AskedUtc <= request.AskedUtc).OrderBy(r => r.AskedUtc))
                    {
                        CarryForwardCrew(old, request);
                        MarkReplaced(old, nowUtc);
                    }

                    if (older.Where(r => r.AskedUtc > request.AskedUtc).OrderByDescending(r => r.AskedUtc).FirstOrDefault() is { } keep)
                    {
                        CarryForwardCrew(request, keep);
                        MarkReplaced(request, nowUtc);
                    }
                }
                else if (request.Kind == DriverAsk.Query)
                {
                    var night = NightOf(request.AskedUtc);
                    var tonight = _file.Requests.Count(r => r.Kind == DriverAsk.Query && r.Badge == request.Badge && NightOf(r.AskedUtc) == night);

                    if (tonight >= MostQueriesPerNight)
                    {
                        request.State = DriverAsk.Declined;
                        request.Said = TooManySaid;
                        request.AnsweredUtc = nowUtc;
                    }
                }

                _file.Requests.Add(request);
                confirmable.Add(request.Id);
                filed++;
            }

            if (filed > 0)
            {
                Changed();

                if (!Save())
                {
                    // In memory, so the desk can still see and answer them
                    // tonight - but not confirmed, so the relay keeps them.
                    confirmable.Clear();
                }
            }
            // WHY (23 Oct 2026, bug hunt 10): a request offered again after
            // its save failed is held - but only in memory, and confirming it
            // let the relay delete it before it ever reached the disk. Saved
            // again first now, and confirmed only once that works.
            else if (confirmable.Count > 0 && (LoadedUnreadable || (_unsaved && !Save())))
            {
                confirmable.Clear();
            }
        }

        return confirmable;
    }

    /// <summary>What an older crew request asked for, carried into the newer one: each field the newer one doesn't have, and the guardian tick. Under the lock.</summary>
    internal static void CarryForwardCrew(DriverRequest from, DriverRequest into)
    {
        foreach (var (key, text) in from.Texts)
        {
            into.Texts.TryAdd(key, text);
        }

        if (from.Guardian)
        {
            into.Flags["guardian"] = true;
        }
    }

    /// <summary>
    /// Takes fields out of a request still waiting - the voice picks, once
    /// applied by themselves (15 Nov 2026, driver crew). False when it is no
    /// longer waiting.
    /// </summary>
    /// <param name="mark">A field to set to "1" on the request as well, or null.</param>
    internal bool DropTexts(string id, IEnumerable<string> keys, string? mark = null)
    {
        lock (_gate)
        {
            if (_file.Requests.FirstOrDefault(r => r.Id == id) is not { IsWaiting: true } request)
            {
                return false;
            }

            foreach (var key in keys)
            {
                request.Texts.Remove(key);
            }

            if (mark is not null)
            {
                request.Texts[mark] = "1";
            }

            Changed();
            Save();
            return true;
        }
    }

    private static void MarkReplaced(DriverRequest request, DateTime nowUtc)
    {
        request.State = DriverAsk.Declined;
        request.Said = ReplacedSaid;
        request.AnsweredUtc = nowUtc;
        request.Replaced = true;
    }

    private static readonly string[] PrivacyFlags = { "noPhoto", "noVideo", "initials" };

    /// <summary>
    /// Carries what an older details request asked for into the newer one
    /// that replaces it (23 Oct 2026, bug hunt 10): a text field the newer
    /// one doesn't have, each privacy tick ON in either (they are only ever
    /// proposed on), emailMyNight only when the newer one doesn't say, and
    /// the guardian tick from either. True when everything rode along, so
    /// <paramref name="from"/> can be marked replaced.
    ///
    /// An email change rides only between two PIN-proven requests - it must
    /// never end up on a badge-only one. When it can't, it is the one thing
    /// left in <paramref name="from"/>, which stays waiting for the desk
    /// (false): nothing the driver asked for is dropped, and nothing else is
    /// applied twice. Under the lock.
    /// </summary>
    internal static bool CarryForward(DriverRequest from, DriverRequest into)
    {
        var leftEmail = false;

        foreach (var (key, text) in from.Texts)
        {
            if (into.Texts.ContainsKey(key))
            {
                continue;
            }

            if (key == "email" && (from.Proof != DriverAsk.ByPin || into.Proof != DriverAsk.ByPin))
            {
                leftEmail = true;
                continue;
            }

            into.Texts[key] = text;
        }

        foreach (var key in PrivacyFlags)
        {
            if (from.Flags.TryGetValue(key, out var on) && on)
            {
                into.Flags[key] = true;
            }
        }

        // WHY (23 Oct 2026, bug hunt 10 review): "email my night" waits on
        // with an email that can't ride along - on the badge-only request it
        // would be switched off for want of an address (ApplyDetails), and
        // the address it was asked with is still waiting in the older one.
        var emailMyNightWaits = false;
        var hasEmailMyNight = from.Flags.TryGetValue("emailMyNight", out var emailMyNight);

        if (hasEmailMyNight && !into.Flags.ContainsKey("emailMyNight"))
        {
            if (leftEmail)
            {
                emailMyNightWaits = true;
            }
            else
            {
                into.Flags["emailMyNight"] = emailMyNight;
            }
        }

        if (from.Guardian)
        {
            into.Flags["guardian"] = true;
        }

        if (!leftEmail)
        {
            return true;
        }

        // Only the email waits on; the rest is in the newer request now.
        var email = from.Text("email");
        var guardian = from.Guardian;
        from.Texts.Clear();
        from.Texts["email"] = email;
        from.Flags.Clear();

        if (guardian)
        {
            from.Flags["guardian"] = true;
        }

        if (emailMyNightWaits)
        {
            from.Flags["emailMyNight"] = emailMyNight;
        }

        return false;
    }

    /// <summary>
    /// The race night a request was asked on - 6am to 6am, like
    /// <see cref="TonightAtTheTrack.NightOf"/> - what "in a night" means for
    /// the query cap.
    /// WHY (23 Oct 2026, bug hunt 10): this was the calendar date, so a club
    /// night running past midnight gave each driver a second ten queries.
    /// </summary>
    internal static DateTime NightOf(DateTime utc) => TonightAtTheTrack.NightOf(DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime());

    /// <summary>
    /// The desk's answer. False, and nothing changed, when it is not
    /// waiting any more - answered on another path, or replaced by a newer
    /// one since the desk opened it.
    /// </summary>
    internal bool Answer(string id, string state, string said, DateTime nowUtc)
    {
        if (!DriverAsk.IsState(state) || state == DriverAsk.Waiting)
        {
            return false;
        }

        lock (_gate)
        {
            if (_file.Requests.FirstOrDefault(r => r.Id == id) is not { IsWaiting: true } request)
            {
                return false;
            }

            var text = (said ?? string.Empty).Trim();

            request.State = state;
            request.Said = text.Length > DriverAsk.SaidMaxChars ? text[..DriverAsk.SaidMaxChars] : text;
            request.AnsweredUtc = nowUtc;
            Changed();
            Save();
            return true;
        }
    }

    /// <summary>
    /// Writes down what the laptop worked out while filing a request, beside
    /// what the phone sent (12 Nov 2026, setup plan step 8): a marshal swap
    /// keeps who was asked and which post, since the phone only sent a key.
    /// Kept on this laptop only. False for a request it doesn't have.
    /// </summary>
    internal bool Note(string id, IReadOnlyDictionary<string, string> texts)
    {
        lock (_gate)
        {
            if (_file.Requests.FirstOrDefault(r => r.Id == id) is not { } request)
            {
                return false;
            }

            foreach (var (key, value) in texts)
            {
                request.Texts[key] = value;
            }

            Changed();
            Save();
            return true;
        }
    }

    /// <summary>Back to waiting - for an undo of the desk's answer here. The phone is shown "waiting" again on its next poll.</summary>
    internal bool Reopen(string id)
    {
        lock (_gate)
        {
            if (_file.Requests.FirstOrDefault(r => r.Id == id) is not { IsWaiting: false, Replaced: false } request)
            {
                return false;
            }

            request.State = DriverAsk.Waiting;
            request.Said = string.Empty;
            request.AnsweredUtc = null;
            Changed();
            Save();
            return true;
        }
    }

    /// <summary>Throws one away - for a badge that belongs to nobody on this laptop.</summary>
    internal bool Remove(string id)
    {
        lock (_gate)
        {
            if (_file.Requests.RemoveAll(r => r.Id == id) == 0)
            {
                return false;
            }

            Changed();
            Save();
            return true;
        }
    }

    /// <summary>
    /// What the phone holding this badge is shown about its requests
    /// (driverlive asks): those of the last <see cref="AnswersShownFor"/>,
    /// the newest <see cref="MostAnswersShown"/> at most, newest last.
    /// WHY (23 Oct 2026, bug hunt 10): this was newest first, but DriverLive
    /// and the night page both keep the LAST few as the newest - so a
    /// driver's newest answers were the ones cut off their phone.
    /// </summary>
    internal IReadOnlyList<DriverAskAnswer> AnswersFor(string badge, DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(badge))
        {
            return Array.Empty<DriverAskAnswer>();
        }

        var since = nowUtc - AnswersShownFor;

        lock (_gate)
        {
            return _file.Requests
                .Where(r => r.Badge == badge && (r.FiledUtc >= since || (r.AnsweredUtc ?? DateTime.MinValue) >= since))
                .OrderByDescending(r => r.AskedUtc)
                .Take(MostAnswersShown)
                .OrderBy(r => r.AskedUtc)
                .Select(r => new DriverAskAnswer(r.Ref, r.Kind, r.State, r.Said, r.AnsweredUtc))
                .Where(a => a.Ref.Length > 0)
                .ToList();
        }
    }

    /// <summary>
    /// Every badge <see cref="AnswersFor"/> has something for - the same
    /// last-<see cref="AnswersShownFor"/> rule - so driverlive includes a
    /// driver who asked today but isn't racing, checked in or on a grid
    /// (22 Oct 2026, driver experience).
    /// </summary>
    internal IReadOnlyList<string> BadgesWithAnswers(DateTime nowUtc)
    {
        var since = nowUtc - AnswersShownFor;

        lock (_gate)
        {
            return _file.Requests
                .Where(r => r.Badge.Length > 0 && r.Ref.Length > 0
                    && (r.FiledUtc >= since || (r.AnsweredUtc ?? DateTime.MinValue) >= since))
                .Select(r => r.Badge)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <summary>Drops answered requests past <see cref="KeepAnsweredFor"/>, then the oldest answered while over <see cref="MostHeld"/>. Under the lock.</summary>
    private void Prune(DateTime nowUtc)
    {
        var before = _file.Requests.Count;
        var cutoff = nowUtc - KeepAnsweredFor;

        _file.Requests.RemoveAll(r => !r.IsWaiting && (r.AnsweredUtc ?? r.FiledUtc) < cutoff);

        if (_file.Requests.Count >= MostHeld)
        {
            var spare = _file.Requests.Count - MostHeld + 1;

            foreach (var old in _file.Requests.Where(r => !r.IsWaiting).OrderBy(r => r.AnsweredUtc ?? r.FiledUtc).Take(spare).ToList())
            {
                _file.Requests.Remove(old);
            }
        }

        if (_file.Requests.Count != before)
        {
            Changed();
        }
    }

    // ------------------------------------------------------------ the inbox

    /// <summary>
    /// The relay's <c>GET driver/inbox</c> answer, read. Requests whose id
    /// can't be confirmed (not 24 hex) are skipped. Ones with a good id but
    /// nothing usable in them - a badge or kind of the wrong shape - are in
    /// <paramref name="unusable"/>: confirmed and dropped, so the inbox
    /// empties rather than offering them for ever.
    /// </summary>
    internal static List<DriverRequest> ParseInbox(string json, DateTime nowUtc, out List<string> unusable)
    {
        unusable = new List<string>();
        var found = new List<DriverRequest>();

        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return found;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String
                || idElement.GetString() is not { Length: 24 } id
                || !id.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                continue;
            }

            var badge = StringOf(item, "b");
            var kind = StringOf(item, "k");

            if (!DriverNight.IsADriverId(badge) || !DriverAsk.IsKind(kind)
                || !item.TryGetProperty("f", out var fields) || fields.ValueKind != JsonValueKind.Object)
            {
                unusable.Add(id);
                continue;
            }

            var request = new DriverRequest
            {
                Id = id,
                Badge = badge,
                Kind = kind,
                Proof = StringOf(item, "v") == DriverAsk.ByPin ? DriverAsk.ByPin : DriverAsk.ByBadge,
                AskedUtc = DateTime.TryParse(StringOf(item, "at"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)
                    ? DateTime.SpecifyKind(at, DateTimeKind.Utc)
                    : nowUtc,
            };

            foreach (var field in fields.EnumerateObject())
            {
                if (field.Value.ValueKind == JsonValueKind.String)
                {
                    var limit = LimitOf(field.Name);

                    if (limit > 0)
                    {
                        var text = (field.Value.GetString() ?? string.Empty).Trim();
                        request.Texts[field.Name] = text.Length > limit ? text[..limit] : text;
                    }
                }
                else if ((field.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) && IsFlag(field.Name))
                {
                    request.Flags[field.Name] = field.Value.GetBoolean();
                }
            }

            found.Add(request);
        }

        return found;
    }

    private static string StringOf(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    /// <summary>The longest each text field can be - the relay's own limits - or 0 for a field this laptop doesn't know.</summary>
    internal static int LimitOf(string field) => field switch
    {
        "announcer" => DriverAsk.AnnouncerMaxChars,
        "pronouns" => DriverAsk.PronounsMaxChars,
        "about" => DriverAsk.AboutMaxChars,
        "email" => DriverAsk.EmailMaxChars,
        "pack" => 32 + 8,
        "race" => 32 + 8,
        "m" => DriverAsk.QueryMaxChars,
        "tires" or "gearing" or "springs" or "ride" or "motor" => DriverAsk.SetupPartMaxChars,
        "notes" => DriverAsk.SetupNotesMaxChars,

        // 15 Nov 2026, their own crew and notes (DriverAsk.Crew).
        "pName" or "tName" => DriverCrew.NameMaxChars,
        "pVoice" or "tVoice" => 12,
        "goal" => DriverCrew.GoalMaxChars,
        "thanks" => DriverCrew.ThanksMaxChars,
        "story" => DriverCrew.CarStoryMaxChars,

        // 12 Nov 2026, "I'm coming" (setup plan C1).
        "day" => 10,
        "classes" => 200,

        // 12 Nov 2026, marshals from their phones (setup plan step 8). The
        // round is a Guid "N"; "to" a MarshalSwaps key; "offer" a relay id.
        "round" => 32 + 8,
        "to" => MarshalSwaps.KeyLength,
        "offer" => 24 + 8,
        "answer" => 3,

        // 14 Nov 2026, race video V5: "early" or "late", and about how many seconds.
        "d" => 5,
        "s" => 3,

        // 5 Nov 2026, the garage from the phone.
        "op" => 16,
        "car" or "sheet" or "entry" or "chassis" => 32 + 8,
        "photo" => 24,
        "label" => DriverAsk.PackLabelMaxChars,
        "chem" => DriverAsk.PackChemistryMaxChars,
        "name" or "chassisOther" => DriverAsk.CarNameMaxChars,
        "cls" => DriverAsk.CarClassMaxChars,
        "body" => DriverAsk.CarBodyMaxChars,
        "number" => DriverAsk.CarNumberMaxChars,
        _ when IsSheetValue(field) => DriverAsk.SheetValueMaxChars,
        _ => 0,
    };

    /// <summary>"v.front-spring": one setup sheet value, by its field's id.</summary>
    internal static bool IsSheetValue(string field) =>
        field.StartsWith(DriverAsk.SheetValuePrefix, StringComparison.Ordinal)
        && field.Length is > 2 and <= 50
        && field.Skip(2).All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    internal static bool IsFlag(string field) => field is "emailMyNight" or "noPhoto" or "noVideo" or "initials" or "guardian";
}

/// <summary>
/// The desk's half of drivers' requests: who sent each one, what it would
/// change, and what happens when the desk approves, notes or declines it.
/// (22 Oct 2026, driver experience.)
///
/// UI thread only, like the stores it changes (profiles, packs, history).
/// Every answer goes back to the phone through the driver's live document
/// (driverlive asks, built from <see cref="DriverRequestStore.AnswersFor"/>),
/// so there is nothing to send from here.
///
/// NO NAMES IN THE LOG, as RelaySignupDesk: the session log goes into
/// support bundles.
/// </summary>
internal sealed partial class DriverRequestDesk
{
    /// <summary>The details fields a phone can propose, in the order the desk is shown them.</summary>
    internal static readonly string[] DetailFields =
    {
        "announcer", "pronouns", "about", "email", "emailMyNight", "noPhoto", "noVideo", "initials",
    };

    private readonly DriverRequestStore _store;
    private readonly AppSettings _settings;
    private readonly ProfileStore _profiles;
    private readonly BatteryPackStore? _packs;
    private readonly SessionHistoryStore _history;
    private readonly UndoService _undo;
    private readonly Action<string> _log;
    private readonly Func<string, string?> _transponderOf;

    public DriverRequestDesk(
        DriverRequestStore store,
        AppSettings settings,
        ProfileStore profiles,
        BatteryPackStore? packs,
        SessionHistoryStore history,
        UndoService undo,
        Action<string> log,
        Func<string, string?> transponderOf)
    {
        _store = store;
        _settings = settings;
        _profiles = profiles;
        _packs = packs;
        _history = history;
        _undo = undo;
        _log = log;
        _transponderOf = transponderOf;

        SendNotice = (to, subject, body) => PinMailer.SendOneAsync(_settings, to, subject, body, CancellationToken.None);
    }

    public DriverRequestStore Store => _store;

    /// <summary>
    /// Sends the "your email was changed" notice to the OLD address. PinMailer
    /// by default; a test swaps it to see what would have gone.
    /// </summary>
    public Func<string, string, string, Task<MailResult>> SendNotice { get; set; }

    /// <summary>What went wrong collecting requests from the relay, or null. Set by the host.</summary>
    public Func<string?>? RelayProblem { get; set; }

    // ------------------------------------------------------- setup changes

    /// <summary>
    /// The Garage's setup log, which a setup change from a phone is filed
    /// into (4 Nov 2026). Set by the host; without it a setup request waits
    /// for the desk like any other.
    /// </summary>
    public SetupLogStore? Setups { get; set; }

    /// <summary>The Garage's cars, so a setup lands on the car they are running when there is one.</summary>
    public CarStore? Cars { get; set; }

    /// <summary>The race day in hand, for a car lent for the day. Null outside one.</summary>
    public Func<string?>? EventId { get; set; }

    /// <summary>The track layout being raced on, written on the setup as the desk's own are.</summary>
    public Func<string?>? TrackName { get; set; }

    /// <summary>The parts of a setup sheet a phone can send, in the order they are shown, with the desk's words for them.</summary>
    internal static readonly (string Key, string Caption)[] SetupParts =
    {
        ("tires", "Tires"), ("gearing", "Gearing"), ("springs", "Springs"), ("ride", "Ride height"), ("motor", "Motor"), ("notes", "Notes"),
    };

    /// <summary>"Tires: soft; Springs: blue" - what a setup request says, for the desk's list.</summary>
    internal static string SetupWords(DriverRequest request)
    {
        var parts = SetupParts
            .Where(p => request.Text(p.Key).Length > 0)
            .Select(p => $"{p.Caption}: {request.Text(p.Key)}")
            .ToList();

        return parts.Count == 0 ? "nothing" : string.Join("; ", parts);
    }

    /// <summary>
    /// Files every setup request still waiting, as <see cref="FileSetup"/>
    /// does one. Returns how many went into the log. Called on the UI
    /// thread's roster tick, so a change sent from a phone is in the Driver
    /// Hub within seconds of the relay handing it over.
    /// </summary>
    public int FileWaitingSetups()
    {
        if (Setups is null)
        {
            return 0;
        }

        var filed = 0;

        foreach (var request in _store.Waiting.Where(r => r.Kind == DriverAsk.Setup).ToList())
        {
            if (FileSetup(request, out _))
            {
                filed++;
            }
        }

        return filed;
    }

    /// <summary>
    /// A SETUP CHANGE FROM THE PHONE, INTO THE GARAGE'S LOG (4 Nov 2026,
    /// Asher: "when you make an setup log change on the relay it should
    /// update in the driver hub and feed into the technical lead interview").
    ///
    /// Filed without the desk, unlike every other request: it changes nothing
    /// anybody else sees, only adds a line to the driver's own setup log, and
    /// the badge it came with is the driver's own. It is a setup entry like
    /// one typed at the desk - the five parts of the old sheet and the note
    /// - so the Driver Hub lists it under their setups (a setup is theirs by
    /// its car or their name), and <see cref="MechanicBrief.SetupLines"/>
    /// gives the technical lead the newest one on the winner's car or name.
    ///
    /// ON THEIR CAR when they have exactly one for the day, the car the
    /// technical lead looks at; otherwise on their name, which the brief and
    /// the hub both read. Recorded at the time the phone sent it, so the
    /// brief's "logged on race day" holds.
    ///
    /// A badge nobody on this laptop has, or a driver with no name, stays
    /// waiting for the desk to discard - nothing is filed against nobody.
    /// </summary>
    public bool FileSetup(DriverRequest request, out string refusal)
    {
        if (request.Kind != DriverAsk.Setup || !_store.IsWaiting(request.Id))
        {
            refusal = "That request has already been answered.";
            return false;
        }

        if (Setups is null)
        {
            refusal = "The setup log isn't open on this laptop.";
            return false;
        }

        if (DriverOf(request) is not { } profile)
        {
            refusal = "Nobody on this laptop has that badge. Discard it.";
            return false;
        }

        var name = (profile.DriverName ?? string.Empty).Trim();

        if (name.Length == 0)
        {
            refusal = "That driver has no name to file a setup under. Give them one in the Driver Hub first.";
            return false;
        }

        if (SetupParts.All(p => request.Text(p.Key).Length == 0))
        {
            refusal = "The request says nothing changed.";
            return false;
        }

        Car? car = null;

        try
        {
            var cars = Cars?.DrivenBy(profile.Transponder, EventId?.Invoke()) ?? Array.Empty<Car>();
            car = cars.Count == 1 ? cars[0] : null;
        }
        catch
        {
            car = null;
        }

        var entry = new SetupEntry
        {
            RecordedAtUtc = request.AskedUtc,
            DriverName = name,
            TrackLayoutName = (TrackName?.Invoke() ?? string.Empty).Trim(),
            CarModel = car is not null && !string.IsNullOrWhiteSpace(car.Name) ? car.Name.Trim() : (profile.CarModel ?? string.Empty).Trim(),
            CarId = car?.Id,
            Tires = request.Text("tires"),
            Gearing = request.Text("gearing"),
            Springs = request.Text("springs"),
            RideHeight = request.Text("ride"),
            Motor = request.Text("motor"),
            Notes = request.Text("notes"),
        };

        Setups.Upsert(entry);
        _store.Answer(request.Id, DriverAsk.Approved, "In your setup log", DateTime.UtcNow);

        // No names: the session log goes into support bundles.
        _log("[driver requests] A driver logged a setup change from their phone - it is in their setup log in the Driver Hub.");

        refusal = string.Empty;
        return true;
    }

    // ------------------------------------------------------------- who

    /// <summary>The driver a request is from, or null when the badge is nobody's on this laptop.</summary>
    public DriverProfile? DriverOf(DriverRequest request)
    {
        if (_transponderOf(request.Badge) is not { Length: > 0 } transponder)
        {
            return null;
        }

        return _profiles.Get(transponder) is { } profile
               && string.Equals(profile.Transponder, transponder, StringComparison.OrdinalIgnoreCase)
            ? profile
            : null;
    }

    public string NameFor(DriverRequest request) => DriverOf(request) is { } profile ? NameOf(profile) : "Unknown driver";

    private static string NameOf(DriverProfile profile) =>
        string.IsNullOrWhiteSpace(profile.DriverName) ? "(no name)" : profile.DriverName.Trim();

    /// <summary>"Sam Parker" to "Sam P." - how a query is signed in the race's notes.</summary>
    internal static string ShortName(string name)
    {
        var words = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return words.Length switch
        {
            0 => "A driver",
            1 => words[0],
            _ => $"{words[0]} {char.ToUpperInvariant(words[^1][0])}.",
        };
    }

    // ------------------------------------------------------------ the state

    /// <summary>Why requests aren't reaching the desk, for the window's state line, and whether it is a fault.</summary>
    public (string Text, bool Bad) StateLine()
    {
        if (_store.LoadedUnreadable)
        {
            return ("The driver requests file (driver-requests.json) couldn't be read when HeatCaller started. Requests still arrive, "
                + "but are left on the club's website until HeatCaller is restarted and can read it.", true);
        }

        if (!_settings.RelayDriverRequests)
        {
            return ("Off. Drivers can't send requests from their phones. Switch on Driver requests on the Club Website page.", false);
        }

        if (!_settings.RelayEnabled)
        {
            return ("Publishing to the relay is off, so no requests can arrive. Set it up on the Club Website page first.", true);
        }

        if (!_settings.PublishDriverNightsToTheRelay || !_settings.RelayCheckInEnabled || string.IsNullOrWhiteSpace(_settings.RelayCheckInSecret))
        {
            return ("Drivers send requests from their own night page, which needs drivers' own nights and online check-in on "
                + "(Club Website page). Until both are, nobody can send one.", true);
        }

        if (RelayProblem?.Invoke() is { Length: > 0 } problem)
        {
            return (problem, true);
        }

        return ("On. Requests from drivers' phones appear here within about 15 seconds. Nothing changes until you approve it, "
            + "and the driver's phone shows your answer.", false);
    }

    // ------------------------------------------------------------ the words

    /// <summary>What a request asks for, one line per change ("Announcer name: 'Zhock' (was 'Jacques')").</summary>
    public IReadOnlyList<string> Describe(DriverRequest request)
    {
        var profile = DriverOf(request);
        var lines = new List<string>();

        switch (request.Kind)
        {
            case DriverAsk.Details:
                foreach (var field in FieldsOf(request, profile))
                {
                    lines.Add(field.IsFlag
                        ? $"{field.Caption}: {(field.ProposedFlag ? "yes" : "no")}" + (profile is null ? string.Empty : $" (was {(field.CurrentFlag ? "yes" : "no")})")
                        : $"{field.Caption}: " + (field.Proposed.Length == 0 ? "cleared" : $"'{field.Proposed}'")
                          + (profile is null ? string.Empty : field.Current.Length == 0 ? " (was empty)" : $" (was '{field.Current}')"));
                }

                if (lines.Count == 0)
                {
                    lines.Add("No changes");
                }

                break;

            case DriverAsk.Pack:
                lines.Add("Battery pack: " + PackWords(request, profile));
                break;

            case DriverAsk.Query:
                lines.Add($"Query about {RaceWords(request)}: \"{request.Text("m")}\"");
                break;

            case DriverAsk.Setup:
                lines.Add("Setup change: " + SetupWords(request));
                break;

            // Their own crew and notes (15 Nov 2026).
            case DriverAsk.Crew:
                lines.AddRange(CrewLines(request, profile));
                break;

            case DriverAsk.Late:
                lines.Add($"Needs {RaceControlControl.LateMinutes} more minutes before their next race" + NoteWords(request));
                break;

            // Race video (14 Nov 2026, V5, V6).
            case DriverAsk.VideoOff:
                lines.Add(VideoOffWords(RaceWords(request), request.Text("d"), request.Text("s")));
                break;

            case DriverAsk.VideoHide:
                lines.Add($"Hide the video of {RaceWords(request)}");
                break;

            case DriverAsk.Skip:
                lines.Add("Car broken - skip them in their next race" + NoteWords(request));
                break;

            case DriverAsk.Coming:
                lines.Add(request.Text("classes").Length == 0
                    ? $"Not coming on {request.Text("day")} after all"
                    : $"Coming on {request.Text("day")}: {request.Text("classes").Replace(",", ", ")}");
                break;

            // Marshals from their phones (12 Nov 2026, setup plan step 8).
            case DriverAsk.Post:
            case DriverAsk.Swap:
            case DriverAsk.Cover:
                lines.Add(MarshalWords(request));
                break;

            default:
                lines.Add(WriteWords(request));
                break;
        }

        return lines;
    }

    public string Summary(DriverRequest request) => string.Join("; ", Describe(request));

    /// <summary>The notes column: who checked them, a guardian, an unknown badge, a query about a race they weren't in.</summary>
    public string NoteFor(DriverRequest request)
    {
        var notes = new List<string>();

        // First, so it reads before anything else (setup plan step 8).
        if (InProtestWindow(request))
        {
            notes.Add(InProtestWindowWords);
        }

        if (DriverOf(request) is null)
        {
            notes.Add("Unknown driver - nobody on this laptop has that badge");
        }

        notes.Add(request.Proof == DriverAsk.ByPin ? "Checked by their personal PIN" : "Checked by their badge code");

        if (request.Guardian)
        {
            notes.Add("A parent or guardian sent it");
        }

        if (request.Kind == DriverAsk.Crew)
        {
            notes.AddRange(CrewNotes(request, DriverOf(request)));
        }

        if (request.Kind == DriverAsk.Query && RaceOf(request) is not null && !InThatRace(request))
        {
            notes.Add("Not in that race");
        }
        else if (request.Kind == DriverAsk.Query && RaceOf(request) is null)
        {
            notes.Add("A race this laptop doesn't have");
        }

        return string.Join("; ", notes);
    }

    /// <summary>The answer column: "Approved", "Noted", "Declined: reason", "Replaced".</summary>
    public static string AnswerWords(DriverRequest request)
    {
        if (request.Replaced)
        {
            return "Replaced by a newer request";
        }

        return request.State switch
        {
            DriverAsk.Approved => "Approved",
            DriverAsk.Noted => "Noted",
            DriverAsk.Declined => request.Said.Length > 0 ? $"Declined: {request.Said}" : "Declined",
            _ => "Waiting",
        };
    }

    public static string KindWords(string kind) => kind switch
    {
        DriverAsk.Details => "Their details",
        DriverAsk.Pack => "Battery pack",
        DriverAsk.Query => "Result query",
        DriverAsk.Setup => "Setup sheet",
        DriverAsk.PackEdit => "Their packs",
        DriverAsk.Car => "Their garage",
        DriverAsk.Photo => "Car photo",
        DriverAsk.VideoOff => "Video in the wrong place",
        DriverAsk.VideoHide => "Hide a race video",
        DriverAsk.Late => "More time",
        DriverAsk.Skip => "Skip my race",
        DriverAsk.Coming => "Coming next race night",
        DriverAsk.Post => "At my marshal post",
        DriverAsk.Swap => "Cover my post?",
        DriverAsk.Cover => "Marshal swap",
        DriverAsk.Crew => "Crew and notes",
        _ => kind,
    };

    // ------------------------------------------------------------ details

    /// <summary>One change a details request proposes, beside what the profile has now.</summary>
    internal sealed record DetailField(string Key, string Caption, string Current, string Proposed, bool IsFlag, bool CurrentFlag, bool ProposedFlag, int MaxChars);

    /// <summary>
    /// The changes a details request proposes, in <see cref="DetailFields"/>
    /// order. A privacy flag is only ever proposed ON (the relay refuses
    /// false), and the guardian tick is not a change at all.
    /// </summary>
    internal IReadOnlyList<DetailField> FieldsOf(DriverRequest request, DriverProfile? profile)
    {
        var fields = new List<DetailField>();

        foreach (var key in DetailFields)
        {
            if (DriverRequestStore.IsFlag(key))
            {
                if (request.Flags.TryGetValue(key, out var proposed) && (proposed || key == "emailMyNight"))
                {
                    fields.Add(new DetailField(key, CaptionOf(key), string.Empty, string.Empty, IsFlag: true,
                        CurrentFlag: profile is not null && FlagOf(profile, key), ProposedFlag: proposed, MaxChars: 0));
                }
            }
            else if (request.Texts.TryGetValue(key, out var text))
            {
                fields.Add(new DetailField(key, CaptionOf(key), profile is null ? string.Empty : TextOf(profile, key), text,
                    IsFlag: false, CurrentFlag: false, ProposedFlag: false, MaxChars: DriverRequestStore.LimitOf(key)));
            }
        }

        return fields;
    }

    internal static string CaptionOf(string key) => key switch
    {
        "announcer" => "Announcer name",
        "pronouns" => "Pronouns",
        "about" => "About them",
        "email" => "Email",
        "emailMyNight" => "Email them their night",
        "noPhoto" => "No photo online",
        "noVideo" => "No podium videos online",
        "initials" => "Initials only online",
        _ => key,
    };

    private static string TextOf(DriverProfile profile, string key) => key switch
    {
        "announcer" => profile.AnnouncerName ?? string.Empty,
        "pronouns" => profile.Pronouns ?? string.Empty,
        "about" => profile.AboutLine ?? string.Empty,
        "email" => profile.Email ?? string.Empty,
        _ => string.Empty,
    };

    private static bool FlagOf(DriverProfile profile, string key) => key switch
    {
        "emailMyNight" => profile.EmailMyNight,
        "noPhoto" => profile.NoPhotoOnline,
        "noVideo" => profile.NoVideoOnline,
        "initials" => profile.InitialsOnline,
        _ => false,
    };

    /// <summary>
    /// What the desk ticked in the approve dialog: each text field as the
    /// desk left it in its box, and each flag ticked. A field not in either
    /// is left as it is.
    /// </summary>
    internal sealed record DetailsChoice(IReadOnlyDictionary<string, string> Texts, IReadOnlyDictionary<string, bool> Flags);

    /// <summary>
    /// Applies the ticked changes to the driver's profile: a snapshot, the
    /// change, Upsert, and an undo that puts the snapshot back while the
    /// profile is still as this left it. A changed email also sends a notice
    /// to the OLD address. False, with the reason, and nothing changed, when
    /// it can't be applied.
    /// </summary>
    public bool ApplyDetails(DriverRequest request, DetailsChoice choice, out string refusal)
    {
        if (request.Kind != DriverAsk.Details || !_store.IsWaiting(request.Id))
        {
            refusal = "That request has already been answered, or replaced by a newer one from the same driver.";
            return false;
        }

        if (DriverOf(request) is not { } profile)
        {
            refusal = "Nobody on this laptop has that badge, so there is no profile to change. Discard it.";
            return false;
        }

        if (choice.Texts.Count == 0 && choice.Flags.Count == 0)
        {
            refusal = "Tick at least one change to approve, or decline the request.";
            return false;
        }

        var before = ProfileStore.Snapshot(profile);
        var updated = ProfileStore.Snapshot(profile);
        var oldEmail = (profile.Email ?? string.Empty).Trim();

        foreach (var (key, raw) in choice.Texts)
        {
            var limit = DriverRequestStore.LimitOf(key);
            var text = (raw ?? string.Empty).Trim();

            if (limit > 0 && text.Length > limit)
            {
                text = text[..limit];
            }

            switch (key)
            {
                case "announcer":
                    updated.AnnouncerName = text;
                    break;
                case "pronouns":
                    updated.Pronouns = text;
                    break;
                case "about":
                    updated.AboutLine = text;
                    break;
                case "email":
                    // Only from a phone that proved the personal PIN (22 Oct
                    // 2026, security review). The relay already refuses an
                    // email change on the badge code alone; checked here too
                    // so an ask that says otherwise can't move where the PIN
                    // itself is sent.
                    if (request.Proof != DriverAsk.ByPin)
                    {
                        refusal = "An email change needs the driver's personal PIN, and this request came with the badge code only. Change it in their profile if they ask at the desk.";
                        return false;
                    }

                    if (text.Length > 0 && !System.Net.Mail.MailAddress.TryCreate(text, out _))
                    {
                        refusal = $"\"{text}\" doesn't look like an email address. Fix it, or untick it.";
                        return false;
                    }

                    updated.Email = text;
                    break;
            }
        }

        foreach (var (key, on) in choice.Flags)
        {
            switch (key)
            {
                case "emailMyNight":
                    updated.EmailMyNight = on;
                    break;

                // ON only, as the relay takes them. Turning one off is an
                // edit on the driver's profile, never a phone's.
                case "noPhoto" when on:
                    updated.NoPhotoOnline = true;
                    break;
                case "noVideo" when on:
                    updated.NoVideoOnline = true;
                    break;
                case "initials" when on:
                    updated.InitialsOnline = true;
                    break;
            }
        }

        // Emailing their night needs somewhere to send it.
        if (updated.EmailMyNight && string.IsNullOrWhiteSpace(updated.Email))
        {
            updated.EmailMyNight = false;
        }

        var name = NameOf(profile);
        var transponderKey = profile.Transponder;
        // Read back once it is saved (below), so the undo compares against
        // exactly what the store holds.
        var after = JsonSerializer.Serialize(updated);
        var profiles = _profiles;
        var store = _store;
        var id = request.Id;

        // The pattern MainForm.UnknownTransponders uses: only while the driver
        // is still as this left them - otherwise a later edit would be thrown
        // away with it. Their phone has been told yes by then and is shown
        // "waiting" again after the undo.
        _undo.Capture($"Apply {name}'s own changes (their phone was told yes)", () =>
        {
            var nowOnFile = profiles.Get(transponderKey);

            if (nowOnFile is null || JsonSerializer.Serialize(nowOnFile) != after)
            {
                throw new InvalidOperationException(
                    $"{name} has been changed or removed since, and undoing this now would undo that too. Nothing was changed.");
            }

            profiles.PutBack(new[] { before }, Array.Empty<string>());
            store.Reopen(id);
        });

        _profiles.Upsert(updated);

        if (_profiles.Get(transponderKey) is { } saved)
        {
            after = JsonSerializer.Serialize(saved);
        }

        _store.Answer(request.Id, DriverAsk.Approved, string.Empty, DateTime.UtcNow);
        _log("[driver requests] Applied a driver's own changes from their phone.");

        var newEmail = (updated.Email ?? string.Empty).Trim();

        if (oldEmail.Length > 0 && !string.Equals(oldEmail, newEmail, StringComparison.OrdinalIgnoreCase))
        {
            _ = TellTheOldAddressAsync(oldEmail, name, newEmail);
        }

        refusal = string.Empty;
        return true;
    }

    /// <summary>
    /// The notice to the address a driver's email was changed FROM - so a
    /// change made with somebody else's PIN never goes unnoticed. Masked:
    /// whoever reads the old inbox is not told the new address.
    /// </summary>
    internal static (string Subject, string Body) OldEmailNotice(string name, string newEmail, DateTime whenLocal)
    {
        var nl = Environment.NewLine;
        var to = newEmail.Length == 0 ? "removed" : $"changed to {MaskEmail(newEmail)}";

        return ("Your email address at the race club was changed",
            $"Hello {name},{nl}{nl}"
            + $"The email address on your driver profile was {to} at the timing desk on {whenLocal.ToString("dddd d MMMM", CultureInfo.CurrentCulture)}, "
            + $"as asked from your phone.{nl}{nl}"
            + $"If you didn't ask for this, tell the timing desk at your next race night.");
    }

    /// <summary>"sam@gmail.com" to "s•••@gmail.com".</summary>
    internal static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');

        return at <= 0 ? "•••" : $"{email[0]}•••{email[at..]}";
    }

    /// <remarks>
    /// Always tried: PinMailer answers "email isn't set up" when it isn't,
    /// and that is logged - the desk should know the old address wasn't told.
    /// </remarks>
    private async Task TellTheOldAddressAsync(string oldEmail, string name, string newEmail)
    {
        try
        {
            var (subject, body) = OldEmailNotice(name, newEmail, DateTime.Now);
            var result = await SendNotice(oldEmail, subject, body);

            _log(result.Ok
                ? "[driver requests] Told a driver's old email address that it was changed."
                : $"[driver requests] Couldn't tell a driver's old email address that it was changed: {result.Message}");
        }
        catch (Exception ex)
        {
            _log($"[driver requests] Couldn't tell a driver's old email address that it was changed: {ex.Message}");
        }
    }

    // ------------------------------------------------------------ packs

    private string PackWords(DriverRequest request, DriverProfile? profile)
    {
        var pack = request.Text("pack");

        if (pack == "none")
        {
            return "none in the car";
        }

        if (_packs?.Get(pack) is not { } found)
        {
            return "a pack this laptop doesn't have";
        }

        var current = profile is null ? null : _packs.CurrentPack(BatteryPackStore.DriverKey(_profiles, profile.Transponder));

        return $"'{found.DisplayName}'" + (current is null ? " (none in the car now)" : $" (was '{current.DisplayName}')");
    }

    /// <summary>
    /// Puts the pack they picked in their car - SetCurrent, with one undo
    /// step that puts the old pack back (as BatteryActions.PutInCar's does)
    /// and reopens the request. A pack that isn't one of their choices any
    /// more is refused.
    /// </summary>
    public bool ApprovePack(DriverRequest request, out string refusal)
    {
        if (request.Kind != DriverAsk.Pack || !_store.IsWaiting(request.Id))
        {
            refusal = "That request has already been answered.";
            return false;
        }

        if (DriverOf(request) is not { } profile)
        {
            refusal = "Nobody on this laptop has that badge. Discard it.";
            return false;
        }

        if (_packs is null)
        {
            refusal = "Battery packs aren't kept on this laptop.";
            return false;
        }

        var wanted = request.Text("pack");
        string? packId = null;

        if (wanted != "none")
        {
            if (_packs.Get(wanted) is not { } pack || _packs.ChoicesFor(profile).All(p => p.Id != pack.Id))
            {
                refusal = $"That pack isn't one {NameOf(profile)} can run any more (retired, or somebody else's now). Decline it and ask them which.";
                return false;
            }

            packId = pack.Id;
        }

        // WHY (23 Oct 2026, bug hunt 10): this used BatteryActions.PutInCar,
        // whose undo put the old pack back but never reopened the request -
        // the phone and the Answered list kept saying "approved" - and which
        // left no undo step at all when the pack was in the car already. Now
        // one undo step here does both, as ApplyDetails, Note and Decline
        // do, and only while the car still has the pack this put in.
        BatteryPackStore packs = _packs;
        var store = _store;
        var id = request.Id;
        var driverKey = BatteryPackStore.DriverKey(_profiles, profile.Transponder);
        var driverName = NameOf(profile);
        var previous = packs.CurrentPackId(driverKey);
        var moved = !string.Equals(previous ?? string.Empty, packId ?? string.Empty, StringComparison.Ordinal);
        var label = packs.Get(packId)?.DisplayName;
        var what = !moved
            ? "Approve a driver's pack (their phone was told yes)"
            : label is null ? $"Phone: Take the pack out of {driverName}'s car" : $"Phone: Put pack {label} in {driverName}'s car";

        _undo.Capture(what, () =>
        {
            if (moved)
            {
                if (!string.Equals(packs.CurrentPackId(driverKey) ?? string.Empty, packId ?? string.Empty, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"{driverName}'s pack in the car has been changed again since, and undoing this now would undo that too. Nothing was changed.");
                }

                packs.SetCurrent(driverKey, previous);
            }

            store.Reopen(id);
        });

        if (moved)
        {
            packs.SetCurrent(driverKey, packId);
        }

        _store.Answer(request.Id, DriverAsk.Approved, string.Empty, DateTime.UtcNow);
        _log("[driver requests] Changed a driver's battery pack, as they asked from their phone.");

        refusal = string.Empty;
        return true;
    }

    // ------------------------------------------------------------ queries

    /// <summary>The saved race a query is about, or null.</summary>
    public SessionRecord? RaceOf(DriverRequest request) =>
        request.Text("race") is { Length: > 0 } id ? _history.Get(id) : null;

    /// <summary>
    /// Whether a query about this race, asked then, came in while the race's
    /// protest window was open (12 Nov 2026, setup plan step 8), given the
    /// asking driver's transponder (null when their badge is not known): on
    /// a mixed-class night each class's window closes on its own (bug hunt
    /// 18). Set by MainForm; null leaves every query where it falls.
    /// </summary>
    public Func<SessionRecord, DateTime, string?, bool>? QueryInWindow { get; set; }

    /// <summary>The marker Driver Requests shows on such a query, first in its notes.</summary>
    public const string InProtestWindowWords = "In the protest window";

    /// <summary>
    /// A query asked inside its race's protest window: it goes to the top of
    /// the waiting list, marked, because its answer can still change a
    /// result before it is final - and the finals are seeded from it.
    /// </summary>
    public bool InProtestWindow(DriverRequest request)
    {
        try
        {
            return request.Kind == DriverAsk.Query
                   && QueryInWindow is { } inWindow
                   && RaceOf(request) is { } race
                   && inWindow(race, request.AskedUtc, _transponderOf(request.Badge));
        }
        catch (Exception)
        {
            return false; // the order of a list is never worth a broken page
        }
    }

    /// <summary>Whether the driver has a row in the race their query is about.</summary>
    public bool InThatRace(DriverRequest request)
    {
        if (RaceOf(request) is not { } session || DriverOf(request) is not { } profile)
        {
            return false;
        }

        return session.Results.Any(row => string.Equals(
            BatteryPackStore.DriverKey(_profiles, row.Transponder), profile.Transponder, StringComparison.OrdinalIgnoreCase));
    }

    private string RaceWords(DriverRequest request) =>
        RaceOf(request) is { } session
            ? (string.IsNullOrWhiteSpace(session.Label) ? session.StartedAtUtc.ToLocalTime().ToString("ddd ", CultureInfo.CurrentCulture) + AppTime.TimeOfDay(session.StartedAtUtc.ToLocalTime()) : session.Label.Trim())
            : "a race this laptop doesn't have";

    /// <summary>The line a query adds to its race's notes: "[Driver query 19:42] Sam P.: ...".</summary>
    internal static string NoteLine(DriverRequest request, string driverName) =>
        $"[Driver query {AppTime.TimeOfDay(request.AskedUtc.ToLocalTime())}] {ShortName(driverName)}: {request.Text("m")}";

    /// <summary>
    /// Noted: the query goes into the race's notes (Race History shows them),
    /// once, and the phone is told "noted". The result itself is never
    /// changed by a query - correcting it is the result editor's job.
    /// </summary>
    public bool Note(DriverRequest request, out string refusal)
    {
        if (request.Kind != DriverAsk.Query || !_store.IsWaiting(request.Id))
        {
            refusal = "That request has already been answered.";
            return false;
        }

        if (RaceOf(request) is not { } session)
        {
            refusal = "This laptop doesn't have the race that query is about, so there are no notes to add it to. Decline it with a reason instead.";
            return false;
        }

        var line = NoteLine(request, DriverOf(request) is { } profile ? NameOf(profile) : "A driver");
        var notes = session.Notes ?? string.Empty;

        // Exactly once: an undo and a second Noted must not stack copies.
        // WHY (23 Oct 2026, bug hunt 10): matched as a whole line, and the
        // undo below takes the line out only if THIS Noted put it there. Two
        // queries with the same words in the same minute (a resend on bad
        // Wi-Fi) share one line, and undoing the second removed the first
        // one's; a substring match also let a shorter query's undo cut a
        // piece out of a longer one's line.
        var added = false;

        if (IndexOfLine(notes, line) < 0)
        {
            session.Notes = notes.Length == 0 ? line : notes.TrimEnd() + Environment.NewLine + line;

            if (!_history.SaveChanges())
            {
                session.Notes = notes;
                refusal = "The race's notes couldn't be saved, so the query wasn't added. Try again in a moment.";
                return false;
            }

            added = true;
        }

        var history = _history;
        var store = _store;
        var id = request.Id;
        var sessionId = session.Id;

        _undo.Capture("Note a driver's result query", () =>
        {
            if (added && history.Get(sessionId) is { } race && race.Notes is { } now && IndexOfLine(now, line) >= 0)
            {
                race.Notes = WithoutLine(now, line);
                history.SaveChanges();
            }

            store.Reopen(id);
        });

        _store.Answer(request.Id, DriverAsk.Noted, string.Empty, DateTime.UtcNow);
        _log("[driver requests] Added a driver's result query to the race's notes.");

        refusal = string.Empty;
        return true;
    }

    /// <summary>Where <paramref name="line"/> is in <paramref name="notes"/> as a whole line of its own (the last such), or -1.</summary>
    internal static int IndexOfLine(string notes, string line)
    {
        if (line.Length == 0)
        {
            return -1;
        }

        var at = notes.LastIndexOf(line, StringComparison.Ordinal);

        while (at >= 0)
        {
            var end = at + line.Length;
            var starts = at == 0 || notes[at - 1] == '\n';
            var ends = end == notes.Length || notes[end] == '\r' || notes[end] == '\n';

            if (starts && ends)
            {
                return at;
            }

            if (at == 0)
            {
                break;
            }

            // The next one back that starts before this one (overlaps too).
            at = notes.LastIndexOf(line, at + line.Length - 2, StringComparison.Ordinal);
        }

        return -1;
    }

    /// <summary><paramref name="notes"/> with that whole line taken out, and the line break that joined it - everything else exactly as it was.</summary>
    internal static string WithoutLine(string notes, string line)
    {
        var at = IndexOfLine(notes, line);

        if (at < 0)
        {
            return notes;
        }

        var start = at;
        var end = at + line.Length;

        if (start > 0)
        {
            // The break before it: "\n" or "\r\n".
            start--;

            if (start > 0 && notes[start - 1] == '\r')
            {
                start--;
            }
        }
        else if (end < notes.Length)
        {
            // First line: the break after it instead.
            if (notes[end] == '\r')
            {
                end++;
            }

            if (end < notes.Length && notes[end] == '\n')
            {
                end++;
            }
        }

        return notes[..start] + notes[end..];
    }

    // ------------------------------------------------------------ more time, skip my race

    /// <summary>
    /// Does what an approved "Need 5 minutes" or "Skip me" asks, in Race
    /// Control: (kind, transponder, name) to what happened, in words for the
    /// driver. Set by MainForm (11 Nov 2026, race-night build plan, Phase 1).
    /// </summary>
    public Func<string, string, string, string>? RaceAsk { get; set; }

    /// <summary>Hides one race's video by its session id (MainForm.Video) - for an approved "hide this video".</summary>
    public Action<string>? HideRaceVideo { get; set; }

    /// <summary>"Video of Q1 Heat 2 starts in the wrong place - it opens too early, by about 20 s".</summary>
    internal static string VideoOffWords(string race, string direction, string seconds)
    {
        var how = direction switch { "early" => "it opens too early", "late" => "it opens too late", _ => string.Empty };
        var by = int.TryParse(seconds, NumberStyles.None, CultureInfo.InvariantCulture, out var s) && s is >= 1 and <= 600 ? $"about {s} s" : string.Empty;
        var detail = how.Length > 0 && by.Length > 0 ? $"{how}, by {by}" : how.Length > 0 ? how : by.Length > 0 ? $"off by {by}" : string.Empty;
        return $"Video of {race} starts in the wrong place" + (detail.Length > 0 ? " - " + detail : string.Empty)
            + ". Nudge its stream piece on the Relay tab's Race video card.";
    }

    /// <summary>Noted: "the video starts in the wrong place" was seen. Nothing changes by itself - the club nudges the piece.</summary>
    public bool NoteVideoOff(DriverRequest request, out string refusal)
    {
        if (request.Kind != DriverAsk.VideoOff || !_store.IsWaiting(request.Id))
        {
            refusal = "That request has already been answered.";
            return false;
        }

        refusal = string.Empty;
        return _store.Answer(request.Id, DriverAsk.Noted, "Thanks - the club will line the video up.", DateTime.UtcNow)
            || Refuse(out refusal);
    }

    /// <summary>Approved: HeatCaller stops showing that race's video, everywhere, at once.</summary>
    public bool ApproveVideoHide(DriverRequest request, out string refusal)
    {
        var race = request.Text("race");

        if (request.Kind != DriverAsk.VideoHide || !_store.IsWaiting(request.Id))
        {
            refusal = "That request has already been answered.";
            return false;
        }

        if (HideRaceVideo is not { } hide || race.Length != 32)
        {
            refusal = "Race video isn't set up on this laptop, so there's nothing to hide.";
            return false;
        }

        hide(race);
        refusal = string.Empty;
        return _store.Answer(request.Id, DriverAsk.Approved, "Hidden. To remove the video itself, the club edits it on YouTube.", DateTime.UtcNow)
            || Refuse(out refusal);
    }

    private static bool Refuse(out string refusal)
    {
        refusal = "That request has already been answered.";
        return false;
    }

    /// <summary>" - "their note"", or empty with no note.</summary>
    private static string NoteWords(DriverRequest request) =>
        request.Text("m") is { Length: > 0 } note ? $" - \"{note}\"" : string.Empty;

    /// <summary>
    /// Approves a "Need 5 minutes" or a "Skip me": Race Control acts on it,
    /// and the driver's phone is told what happened.
    /// </summary>
    public bool ApproveRaceAsk(DriverRequest request, out string refusal)
    {
        if (request.Kind is not (DriverAsk.Late or DriverAsk.Skip) || !_store.IsWaiting(request.Id))
        {
            refusal = "That request has already been answered.";
            return false;
        }

        if (DriverOf(request) is not { } profile)
        {
            refusal = "Nobody on this laptop has that badge. Discard it.";
            return false;
        }

        if (RaceAsk is not { } act)
        {
            refusal = "Race Control isn't open, so nothing can be changed for it yet.";
            return false;
        }

        var said = act(request.Kind, profile.Transponder, NameOf(profile));

        if (said.Length > DriverAsk.SaidMaxChars)
        {
            said = said[..DriverAsk.SaidMaxChars];
        }

        if (!_store.Answer(request.Id, DriverAsk.Approved, said, DateTime.UtcNow))
        {
            refusal = "That request has already been answered.";
            return false;
        }

        _log($"[driver requests] {KindWords(request.Kind)} for {NameOf(profile)}: {said}");
        refusal = string.Empty;
        return true;
    }

    // ------------------------------------------------------------ the rest

    /// <summary>Declines it, with a reason for their phone (at most <see cref="DriverAsk.SaidMaxChars"/>).</summary>
    public bool Decline(DriverRequest request, string reason)
    {
        if (!_store.Answer(request.Id, DriverAsk.Declined, reason, DateTime.UtcNow))
        {
            return false;
        }

        var store = _store;
        var id = request.Id;
        _undo.Capture("Decline a driver's request (their phone was told no)", () => store.Reopen(id));
        _log("[driver requests] Declined a driver's request.");
        return true;
    }

    /// <summary>Throws away a request from a badge nobody on this laptop has. Nothing is told to any phone.</summary>
    public bool Discard(DriverRequest request)
    {
        if (DriverOf(request) is not null)
        {
            return false;
        }

        var removed = _store.Remove(request.Id);

        if (removed)
        {
            _log("[driver requests] Discarded a request from a badge nobody on this laptop has.");
        }

        return removed;
    }
}
