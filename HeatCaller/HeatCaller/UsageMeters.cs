using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace HeatCaller;

/// <summary>Which monthly allowance a <see cref="Meter"/> is for.</summary>
internal enum MeterKind
{
    /// <summary>The relay's AI commentary, counted in lines.</summary>
    Commentary,

    /// <summary>The HeatCaller voice through the relay, counted in characters.</summary>
    HeatCallerVoice,

    /// <summary>The club's own ElevenLabs key, counted in characters on the club's own ElevenLabs plan.</summary>
    ElevenLabsKey,
}

/// <summary>How worried to be about a meter. Watch at 80% used, Spent at 100%.</summary>
internal enum MeterLevel
{
    /// <summary>Not read, or nothing on this plan to measure against.</summary>
    Unknown,

    Ok,

    /// <summary>80% or more of the month is used.</summary>
    Watch,

    /// <summary>The month is used up.</summary>
    Spent,
}

/// <summary>
/// One monthly allowance as it last stood: what is used, out of how much,
/// and how much of it went tonight.
/// </summary>
/// <param name="Known">False when it could not be read - <paramref name="Note"/> says why - and Used and Allowance mean nothing.</param>
/// <param name="Allowance">Including any top-up. 0 is "not on this plan".</param>
/// <param name="UsedTonight">Since the first figure seen this race night (6am to 6am, TonightAtTheTrack.NightOf).</param>
internal sealed record Meter(
    MeterKind Kind,
    bool Known,
    long Used,
    long Allowance,
    long TopUp,
    DateTime? ResetsUtc,
    DateTime AsOfUtc,
    long UsedTonight,
    string? Note)
{
    /// <summary>The fraction at which a meter is worth watching.</summary>
    internal const double WatchAt = 0.8;

    public string Name => Kind switch
    {
        MeterKind.Commentary => "AI commentary",
        MeterKind.HeatCallerVoice => "HeatCaller voice",
        _ => "ElevenLabs (your own key)",
    };

    public string Unit => Kind == MeterKind.Commentary ? "lines" : "characters";

    public long Remaining => Math.Max(0, Allowance - Used);

    public double Fraction => Allowance <= 0 ? 0 : (double)Used / Allowance;

    public MeterLevel Level =>
        !Known || Allowance <= 0 ? MeterLevel.Unknown
        : Used >= Allowance ? MeterLevel.Spent
        : Fraction >= WatchAt ? MeterLevel.Watch
        : MeterLevel.Ok;

    public bool Spent => Known && Allowance > 0 && Used >= Allowance;

    /// <summary>
    /// Whether HeatCaller can top this one up. Not the club's own
    /// ElevenLabs key: that is the club's own plan, bought on elevenlabs.io.
    /// </summary>
    public bool CanAskForMore => Kind is MeterKind.Commentary or MeterKind.HeatCallerVoice;

    /// <summary>"212 of 450 lines · 38 tonight · starts again 1 Nov" - the line under every meter.</summary>
    public string Summary()
    {
        if (!Known)
        {
            return string.IsNullOrWhiteSpace(Note) ? "Couldn't be read." : Note!;
        }

        var resets = ResetsUtc is { } date
            ? " · starts again " + date.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture)
            : string.Empty;

        if (Allowance <= 0)
        {
            return "Not on this club's plan" + resets;
        }

        var tonight = UsedTonight > 0 ? $" · {UsedTonight.ToString("N0", CultureInfo.CurrentCulture)} tonight" : string.Empty;

        return $"{Used.ToString("N0", CultureInfo.CurrentCulture)} of {Allowance.ToString("N0", CultureInfo.CurrentCulture)} {Unit}"
            + tonight
            + resets;
    }

    /// <summary>
    /// "About 6 more race nights like tonight." Null when nothing has gone
    /// tonight to measure by, or the meter was not read: a forecast made
    /// from nothing would be a guess dressed as a number.
    /// </summary>
    public string? Forecast()
    {
        if (!Known || UsedTonight <= 0 || Allowance <= 0)
        {
            return null;
        }

        if (Spent)
        {
            return "Used up for this month.";
        }

        var nights = Remaining / UsedTonight;

        return nights switch
        {
            0 => "Not quite enough left for another race night like tonight.",
            1 => "About 1 more race night like tonight.",
            _ => $"About {nights.ToString("N0", CultureInfo.CurrentCulture)} more race nights like tonight.",
        };
    }

    /// <summary>
    /// "47%". Rounded DOWN, so a meter one line short of the end never says
    /// 100% while there is still something left. "?" when it couldn't be
    /// read and "-" when there is nothing on the plan to be a percentage of.
    /// In whole numbers, not Fraction * 100: the double for 29/100 times 100
    /// is 28.999..., which rounded down said "28%".
    /// </summary>
    public string ShortPercent() =>
        !Known ? "?"
        : Allowance <= 0 ? "-"
        : (Math.Min(Used, long.MaxValue / 100) * 100 / Allowance).ToString(CultureInfo.InvariantCulture) + "%";
}

/// <summary>One request for a top-up this club has made this month, as the relay last listed it.</summary>
internal sealed record TopUpAsk(string Id, MeterKind Kind, DateTime AskedUtc, string Note, bool Open);

/// <summary>
/// Where each of the club's monthly allowances stands - AI commentary, the
/// HeatCaller voice and the club's own ElevenLabs key - in one place, for
/// every page that shows them and for the 80%/100% emails.
///
/// WHO TELLS IT. Anything that already hears a figure says so with Report:
/// the commentary client after each line, the voice client after each
/// spoken line and catalogue, and RefreshAsync, which asks the relay and
/// ElevenLabs directly. Report is cheap and takes a lock, so it is called
/// from whatever thread the answer arrived on.
///
/// WHAT IT REMEMBERS, in usage-meters.json: the last figure for each meter
/// (so the window opens with something in it before the relay has been
/// asked), the first figure seen this race night (so "38 tonight" survives
/// a restart mid-meeting), and which 80% and 100% emails have gone this
/// month (so a restart does not send them again).
///
/// Figures that only move the count are saved a couple of seconds later
/// rather than at once: the voice reports after every spoken line, and a
/// file written for each one is a disk kept busy for nothing. What must
/// not be lost - a new night's starting figure, an email sent - is saved
/// straight away.
/// </summary>
internal sealed class UsageMeters
{
    private const string FileName = "usage-meters.json";

    /// <summary>The one the app uses. Its file is under AppPaths.Root, read when it is first used.</summary>
    public static UsageMeters Shared { get; } = new();

    /// <summary>
    /// The client the relay is asked with. A short timeout: a usage figure
    /// is never worth waiting for, and RefreshAsync runs every few minutes
    /// anyway. Redirects off because the token rides on every request.
    /// Settable so the tests can answer for the relay, as
    /// HeatCallerVoiceClient.Http does; the app never sets it.
    /// </summary>
    internal static HttpClient Http { get; set; } = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(10),
        MaxResponseContentBufferSize = 1024 * 1024,
    };

    /// <summary>What a figure-only change waits before it is written. Zero writes at once (the tests).</summary>
    private readonly TimeSpan _saveDelay;

    private readonly string? _folder;
    private readonly Func<DateTime> _utcNow;

    private readonly object _gate = new();
    private readonly object _saveGate = new();

    private readonly Dictionary<MeterKind, Entry> _meters = new();
    private readonly Dictionary<MeterKind, long> _nightStart = new();
    private readonly HashSet<string> _sent = new(StringComparer.Ordinal);
    private string _night = string.Empty;
    private List<TopUpAsk> _requests = new();
    private bool _loaded;
    private int _savePending;

    /// <summary>
    /// The file was there but could not be read, and must not be written
    /// over this session - see EnsureLoaded. Under _gate once loaded.
    /// </summary>
    private bool _holdWrites;

    private AppSettings? _settings;
    private Action<string>? _log;

    private UsageMeters()
    {
        _folder = null;
        _utcNow = () => DateTime.UtcNow;
        _saveDelay = TimeSpan.FromSeconds(2);
    }

    /// <summary>For the tests: a folder of its own and a clock they set. Saves are written at once.</summary>
    internal UsageMeters(string folder, Func<DateTime> utcNow)
    {
        _folder = folder;
        _utcNow = utcNow;
        _saveDelay = TimeSpan.Zero;
    }

    /// <summary>Raised on whatever thread made the change, after any change to a figure, a note or the requests. Marshal before touching a control.</summary>
    public event Action? Changed;

    /// <summary>
    /// The first time this month a meter reaches Watch or Spent - once per
    /// meter, per month, per level, remembered across restarts. A meter that
    /// goes straight to Spent raises Spent only. Any thread.
    /// </summary>
    public event Action<Meter, MeterLevel>? LevelReached;

    /// <summary>The settings Initialise was given - what UsageMail reads its address from.</summary>
    internal AppSettings? Settings => _settings;

    /// <summary>
    /// True when usage-meters.json was there but could not be read - held
    /// open by a backup or sync tool at startup, or damaged - so which of
    /// this month's 80% and 100% emails have already gone is not known.
    /// UsageMail sends none while it is: an email the club already had,
    /// sent again on every restart while the file stays locked, is worse
    /// than one missed (the chip and the window still say it).
    /// </summary>
    internal bool NoticesUnknown { get; private set; }

    /// <summary>Why the last RefreshAsync could not read the relay, or null when it could (or had nothing to ask).</summary>
    public string? LastProblem { get; private set; }

    public Meter? Get(MeterKind kind)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _meters.TryGetValue(kind, out var entry) ? Build(kind, entry) : null;
        }
    }

    /// <summary>The meters there is a figure (or a note) for, in MeterKind order.</summary>
    public IReadOnlyList<Meter> All
    {
        get
        {
            lock (_gate)
            {
                EnsureLoaded();
                return _meters.OrderBy(m => m.Key).Select(m => Build(m.Key, m.Value)).ToList();
            }
        }
    }

    /// <summary>This month's top-up requests from the relay's last answer, newest first.</summary>
    public IReadOnlyList<TopUpAsk> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToList();
            }
        }
    }

    /// <summary>
    /// Loads what was saved and switches on the 80%/100% emails. Called once
    /// at startup; calling it again only replaces the settings and the log.
    /// </summary>
    internal void Initialise(AppSettings settings, Action<string> log)
    {
        bool first;

        lock (_gate)
        {
            EnsureLoaded();
            first = _settings is null;
            _settings = settings;
            _log = log;
        }

        if (first)
        {
            LevelReached += (meter, level) => UsageMail.Send(this, meter, level);
        }
    }

    /// <summary>Writes a line to the console log given to Initialise, if there is one. Never throws.</summary>
    internal void Log(string line)
    {
        Action<string>? log;

        lock (_gate)
        {
            log = _log;
        }

        try
        {
            log?.Invoke(line);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>A figure from whoever heard one. Thread-safe and cheap.</summary>
    /// <param name="topUp">Null when the caller was not told; the last top-up heard this month is kept.</param>
    public void Report(MeterKind kind, long used, long allowance, DateTime? resetsUtc, long? topUp = null)
    {
        used = Math.Max(0, used);
        allowance = Math.Max(0, allowance);

        var reached = new List<(Meter Meter, MeterLevel Level)>();
        bool changed;
        bool saveNow;

        lock (_gate)
        {
            EnsureLoaded();

            var nowUtc = _utcNow();
            saveNow = RollNight(nowUtc);

            _meters.TryGetValue(kind, out var before);
            var knownBefore = before is { Known: true } ? before : null;

            // A figure without its reset date (the speak headers can leave it
            // off) keeps the date it had, while that date is still ahead and
            // the count has not gone back - which is what a new month looks like.
            var resets = resetsUtc is { } given
                ? AsUtc(given)
                : knownBefore is { ResetsUtc: { } kept } && kept > nowUtc && used >= knownBefore.Used ? kept : (DateTime?)null;

            var period = PeriodOf(resets, nowUtc);

            var top = topUp is { } t
                ? Math.Max(0, t)
                : knownBefore is not null && knownBefore.Period == period ? knownBefore.TopUp : 0;

            var after = new Entry
            {
                Known = true,
                Used = used,
                Allowance = allowance,
                TopUp = top,
                ResetsUtc = resets,
                AsOfUtc = nowUtc,
                Period = period,
            };

            // Tonight's starting figure: the first one seen tonight - and
            // worked out again when the month turns over or the count goes
            // DOWN, since "used tonight" measured straight across either
            // would be nonsense.
            var restart = knownBefore is null || knownBefore.Period != period || used < knownBefore.Used;

            if (restart || !_nightStart.ContainsKey(kind))
            {
                // What tonight had already used is carried across the month
                // turning over mid-night - at midnight UTC on the 1st, which
                // is a US club's evening, and 1am for a UK club in summer
                // time. Started again from nothing, "38 tonight" fell to "2
                // tonight" and the forecast made from it promised a month of
                // race nights. The new month's count is all tonight's - it
                // started from nothing during it - so tonight is what went
                // before plus all of it: the start goes below zero by what
                // went before, and Build counts from it all the same.
                //
                // A count that went down within the same month is taken for
                // a figure that arrived late - the five-minute usage answer
                // leaving the relay a moment before a line's own figure and
                // landing after it, one lower - and keeps tonight's start,
                // which is still the truth. (Build never counts below zero.)
                var next = used;

                if (knownBefore is not null && _nightStart.TryGetValue(kind, out var oldStart))
                {
                    next = knownBefore.Period == period
                        ? oldStart
                        : -Math.Max(0, knownBefore.Used - oldStart);
                }

                if (!_nightStart.TryGetValue(kind, out var start) || start != next)
                {
                    _nightStart[kind] = next;
                    saveNow = true;
                }
            }

            changed = before is null || !before.SameFiguresAs(after);
            _meters[kind] = after;

            var meter = Build(kind, after);

            if (meter.Level is MeterLevel.Watch or MeterLevel.Spent)
            {
                var watch = NoticeKey(kind, period, MeterLevel.Watch);

                if (meter.Level == MeterLevel.Spent)
                {
                    if (_sent.Add(NoticeKey(kind, period, MeterLevel.Spent)))
                    {
                        reached.Add((meter, MeterLevel.Spent));
                        saveNow = true;
                    }

                    // Spent covers the 80% one too: a top-up that brings the
                    // meter back under 100% must not then send "80% used"
                    // for a month that has already been told it ran out.
                    saveNow |= _sent.Add(watch);
                }
                else if (_sent.Add(watch))
                {
                    reached.Add((meter, MeterLevel.Watch));
                    saveNow = true;
                }
            }
        }

        if (saveNow)
        {
            Save();
        }
        else if (changed)
        {
            SaveSoon();
        }

        if (changed)
        {
            RaiseChanged();
        }

        foreach (var (meter, level) in reached)
        {
            RaiseLevelReached(meter, level);
        }
    }

    /// <summary>The meter couldn't be read, for a reason worth showing - the ElevenLabs key that can't read its own plan.</summary>
    public void Unavailable(MeterKind kind, string note)
    {
        bool changed;

        lock (_gate)
        {
            EnsureLoaded();

            var after = new Entry { Known = false, Note = note, AsOfUtc = _utcNow(), Period = string.Empty };
            changed = !_meters.TryGetValue(kind, out var before) || !before.SameFiguresAs(after);
            _meters[kind] = after;
            changed |= _nightStart.Remove(kind);
        }

        if (changed)
        {
            Save();
            RaiseChanged();
        }
    }

    /// <summary>
    /// No longer measured: the voice engine was switched away, the relay
    /// switched off. The emails already sent this month are still
    /// remembered, so switching back does not send them twice.
    /// </summary>
    public void Forget(MeterKind kind)
    {
        bool changed;

        lock (_gate)
        {
            EnsureLoaded();
            changed = _meters.Remove(kind);
            changed |= _nightStart.Remove(kind);
        }

        if (changed)
        {
            Save();
            RaiseChanged();
        }
    }

    // --- Asking -------------------------------------------------------------

    /// <summary>
    /// Asks the relay (GET usage) and, when the voice is the club's own
    /// ElevenLabs key, ElevenLabs itself, and reports what they say. Meters
    /// whose source is switched off are forgotten. A relay that can't be
    /// reached leaves the last figures as they were - an old figure, with
    /// its time on it, beats a blank on race night.
    ///
    /// Reads the settings and the voice before its first await, so call it
    /// from the UI thread. Never throws, not even for a cancel.
    /// </summary>
    public async Task RefreshAsync(AppSettings settings, VoiceProfile? voice, CancellationToken ct = default)
    {
        try
        {
            await RefreshCoreAsync(settings, voice, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                Log($"[usage] Couldn't refresh the monthly allowances ({ex.Message}).");
            }
        }
    }

    private async Task RefreshCoreAsync(AppSettings settings, VoiceProfile? voice, CancellationToken ct)
    {
        // Everything from settings and the voice read here, before the first
        // await: both belong to the UI thread.
        var commentaryTarget = CommentaryTarget(settings);
        var voiceTarget = new HeatCallerVoiceClient(settings).RelayTarget;

        var commentaryOn = settings.CommentaryEnabled && settings.CommentaryUseRelay && commentaryTarget is not null;

        // With no voice given there is nothing to say the engine is something
        // else, so the relay's figure is shown when the relay has one.
        var voiceOn = voiceTarget is not null && (voice is null || voice.EffectiveEngine == VoiceEngine.HeatCaller);

        var sameRelay = commentaryTarget is { } c && voiceTarget is { } v
            && string.Equals(c.Root, v.Root, StringComparison.OrdinalIgnoreCase)
            && string.Equals(c.Club, v.Club, StringComparison.Ordinal);

        var elevenLabsKey = voice is not null && voice.EffectiveEngine == VoiceEngine.ElevenLabs
            ? (voice.ApiKey ?? string.Empty).Trim()
            : string.Empty;

        if (!commentaryOn)
        {
            Forget(MeterKind.Commentary);
        }

        if (!voiceOn)
        {
            Forget(MeterKind.HeatCallerVoice);
        }

        if (voice is not null && elevenLabsKey.Length == 0)
        {
            Forget(MeterKind.ElevenLabsKey);
        }

        var requests = new List<TopUpAsk>();
        var askedAny = false;
        string? problem = null;

        if (commentaryTarget is { } commentary && (commentaryOn || (voiceOn && sameRelay)))
        {
            askedAny = true;
            var (answer, why) = await GetUsageAsync(commentary, ct).ConfigureAwait(false);

            if (answer is not null)
            {
                if (commentaryOn)
                {
                    Apply(MeterKind.Commentary, answer.Commentary);
                }

                if (voiceOn && sameRelay)
                {
                    Apply(MeterKind.HeatCallerVoice, answer.Voice);
                }

                requests.AddRange(answer.Requests.Where(r =>
                    (r.Kind == MeterKind.Commentary && commentaryOn) || (r.Kind == MeterKind.HeatCallerVoice && voiceOn && sameRelay)));
            }
            else
            {
                problem = why;
            }
        }

        if (voiceTarget is { } voiceRelay && voiceOn && !sameRelay)
        {
            askedAny = true;
            var (answer, why) = await GetUsageAsync(voiceRelay, ct).ConfigureAwait(false);

            if (answer is not null)
            {
                Apply(MeterKind.HeatCallerVoice, answer.Voice);
                requests.AddRange(answer.Requests.Where(r => r.Kind == MeterKind.HeatCallerVoice));
            }
            else
            {
                problem ??= why;
            }
        }

        SetRequests(requests, askedAny, problem);

        if (elevenLabsKey.Length > 0)
        {
            try
            {
                var plan = await new ElevenLabsClient().GetSubscriptionAsync(elevenLabsKey, ct).ConfigureAwait(false);
                Report(MeterKind.ElevenLabsKey, plan.CharacterCount, plan.CharacterLimit, plan.ResetsUtc, 0);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                Unavailable(MeterKind.ElevenLabsKey, ElevenLabsCantReadPlan);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Unreachable, or an answer of another shape: the last figure
                // stands, as for the relay.
                Log($"[usage] Couldn't read the ElevenLabs plan ({ex.Message}).");

                // ...but with NO figure yet there is nothing to stand, and the
                // meter used to be simply missing - Voice Settings, This Month
                // and the chip all silent, with the reason only in the
                // console (19 Oct 2026). Said where the meter would be
                // instead, until a figure comes back.
                if (Get(MeterKind.ElevenLabsKey) is not { Known: true })
                {
                    Unavailable(MeterKind.ElevenLabsKey, CouldNotReadPlan(ex));
                }
                else
                {
                    // The old figure is kept, but This Month must not say
                    // "Checked at" over it as if it were fresh (22 Oct 2026,
                    // bug hunt) - the failure only reached the console.
                    bool changed;

                    lock (_gate)
                    {
                        changed = LastProblem is null;
                        LastProblem ??= "ElevenLabs didn't answer - the figure shown is the last one it gave.";
                    }

                    if (changed)
                    {
                        RaiseChanged();
                    }
                }
            }
        }
    }

    /// <summary>
    /// What the meter says when ElevenLabs could not be asked or answered
    /// oddly, and there is no earlier figure to show: why, in a sentence,
    /// and that it is asked again.
    /// </summary>
    internal static string CouldNotReadPlan(Exception ex) => ex switch
    {
        TaskCanceledException or TimeoutException =>
            "ElevenLabs didn't answer in time - this month's figure is asked for again every few minutes.",
        HttpRequestException { StatusCode: { } status } =>
            $"ElevenLabs wouldn't show this key's plan ({(int)status}) - it is asked again every few minutes.",
        HttpRequestException =>
            "Couldn't reach ElevenLabs to read this key's plan - it is asked again every few minutes.",
        _ => "ElevenLabs answered with something that isn't a plan - it is asked again every few minutes.",
    };

    /// <summary>What a key without the User: read permission gets instead of its figures.</summary>
    internal const string ElevenLabsCantReadPlan =
        "This key can't read its plan - give it the User: read permission on elevenlabs.io to see it here.";

    private void SetRequests(List<TopUpAsk> requests, bool asked, string? problem)
    {
        bool changed;

        lock (_gate)
        {
            // A relay that didn't answer leaves the list as it was; one that
            // was not asked at all (nothing switched on) has nothing to list.
            if (asked && problem is not null && requests.Count == 0)
            {
                changed = LastProblem != problem;
                LastProblem = problem;
            }
            else
            {
                var next = requests.OrderByDescending(r => r.AskedUtc).ToList();
                changed = !next.SequenceEqual(_requests) || LastProblem != problem;
                _requests = next;
                LastProblem = problem;
            }
        }

        if (changed)
        {
            RaiseChanged();
        }
    }

    private void Apply(MeterKind kind, Block? block)
    {
        if (block is null)
        {
            // An answer with nothing about this meter says nothing about it.
            return;
        }

        if (!block.Enabled)
        {
            // Not on this club's plan, or not offered by this relay at all:
            // nothing to measure, so nothing to show.
            Forget(kind);
            return;
        }

        // A relay that could not read its own count (22 Oct 2026, bug hunt)
        // says so with known:false and a used of 0. Taken as a figure, that
        // showed a spent month as empty and lifted the commentary's spent
        // hold - so the last real figure stays instead.
        if (!block.Known)
        {
            return;
        }

        Report(kind, block.Used, block.Allowance, block.ResetsUtc, block.TopUp);
    }

    /// <summary>
    /// The commentary relay, exactly as RelayCommentaryClient finds it: the
    /// club's publish relay, club and token, and nothing when a restored
    /// backup's address is still held or the token would go somewhere unsafe.
    /// </summary>
    internal static (string Root, string Club, string Token)? CommentaryTarget(AppSettings settings)
    {
        var root = RelayPublisher.NormaliseBaseUrl(settings.RelayBaseUrl);
        var club = (settings.RelayClub ?? string.Empty).Trim().ToLowerInvariant();
        var token = (settings.RelayToken ?? string.Empty).Trim();

        if (root.Length == 0 || club.Length == 0 || token.Length == 0 || !RelayPublisher.IsValidClubId(club))
        {
            return null;
        }

        if (RestoredSettings.IsHeld(RestoredSettings.Part.PublishRelay))
        {
            return null;
        }

        if (!RelayPublisher.IsSafeForToken(root))
        {
            return null;
        }

        return (root, club, token);
    }

    /// <summary>GET usage on one relay. The answer, or null and a plain reason.</summary>
    private static async Task<(UsageAnswer? Answer, string? Why)> GetUsageAsync(
        (string Root, string Club, string Token) target,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{target.Root}/api/v1/c/{Uri.EscapeDataString(target.Club)}/usage");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);

        HttpResponseMessage response;

        try
        {
            response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return (null, "The relay didn't answer.");
        }

        using (response)
        {
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                return (null, "The relay didn't accept this club's token.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (null, "This relay can't show the monthly allowances yet.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return (null, $"The relay couldn't say how much is used ({(int)response.StatusCode}).");
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (ParseUsage(body) is { } answer)
            {
                return (answer, null);
            }

            return (null, "The relay's answer couldn't be read.");
        }
    }

    /// <summary>One meter's block in the relay's usage answer.</summary>
    internal sealed record Block(bool Enabled, long Used, long Allowance, long TopUp, DateTime? ResetsUtc, bool Known = true);

    /// <summary>The relay's whole usage answer.</summary>
    internal sealed record UsageAnswer(Block? Commentary, Block? Voice, IReadOnlyList<TopUpAsk> Requests);

    /// <summary>Reads GET usage's (or manage/usage's) body. Null when it is not that shape at all.</summary>
    internal static UsageAnswer? ParseUsage(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var requests = new List<TopUpAsk>();

            if (root.TryGetProperty("requests", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in list.EnumerateArray())
                {
                    if (ParseAsk(entry) is { } ask)
                    {
                        requests.Add(ask);
                    }
                }
            }

            return new UsageAnswer(BlockOf(root, "commentary"), BlockOf(root, "voice"), requests);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>One top-up request as the relay writes it, or null when it isn't one.</summary>
    internal static TopUpAsk? ParseAsk(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        MeterKind? kind = Text(entry, "meter") switch
        {
            "commentary" => MeterKind.Commentary,
            "voice" => MeterKind.HeatCallerVoice,
            _ => null,
        };

        if (kind is null)
        {
            return null;
        }

        return new TopUpAsk(
            Text(entry, "id"),
            kind.Value,
            When(entry, "askedAt") ?? DateTime.MinValue,
            Text(entry, "note"),
            !string.Equals(Text(entry, "status"), "done", StringComparison.OrdinalIgnoreCase));
    }

    private static Block? BlockOf(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var block) || block.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new Block(
            block.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True,
            Whole(block, "used"),
            Whole(block, "allowance"),
            Whole(block, "topUp"),
            When(block, "resetsAt"),
            // Absent from an older relay, which never said: taken as known.
            !(block.TryGetProperty("known", out var known) && known.ValueKind == JsonValueKind.False));
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static long Whole(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? Math.Max(0, number)
            : 0;

    private static DateTime? When(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && DateTime.TryParse(
            value.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var when)
            ? when
            : null;

    // --- Inside -------------------------------------------------------------

    /// <summary>What is kept for one meter, in memory and on disk.</summary>
    private sealed class Entry
    {
        public bool Known { get; set; }
        public long Used { get; set; }
        public long Allowance { get; set; }
        public long TopUp { get; set; }
        public DateTime? ResetsUtc { get; set; }
        public DateTime AsOfUtc { get; set; }
        public string? Note { get; set; }

        /// <summary>Which month the figure belongs to - see PeriodOf.</summary>
        public string Period { get; set; } = string.Empty;

        /// <summary>Everything but the time it was heard: a figure heard again is not a change.</summary>
        public bool SameFiguresAs(Entry other) =>
            Known == other.Known
            && Used == other.Used
            && Allowance == other.Allowance
            && TopUp == other.TopUp
            && ResetsUtc == other.ResetsUtc
            && string.Equals(Note, other.Note, StringComparison.Ordinal)
            && string.Equals(Period, other.Period, StringComparison.Ordinal);
    }

    /// <summary>usage-meters.json.</summary>
    private sealed class SavedFile
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, Entry> Meters { get; set; } = new();

        /// <summary>The race night NightStart is for, "yyyy-MM-dd".</summary>
        public string Night { get; set; } = string.Empty;
        public Dictionary<string, long> NightStart { get; set; } = new();

        /// <summary>"Commentary|2026-10|Watch" for each email already sent.</summary>
        public List<string> Sent { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Settled when the file is first read and kept: a save made a moment
    /// later (SaveSoon) must land in the file it was read from, even in the
    /// test run, where AppPaths.Root moves between tests.
    /// </summary>
    private string? _path;

    private string FilePath => _path ??= Path.Combine(_folder ?? AppPaths.Root, FileName);

    private Meter Build(MeterKind kind, Entry entry)
    {
        var tonight = entry.Known
            && _night == NightKey(_utcNow())
            && _nightStart.TryGetValue(kind, out var start)
                ? Math.Max(0, entry.Used - start)
                : 0;

        // LAST MONTH'S FIGURE IS NOT THIS MONTH'S (22 Oct 2026, bug hunt). A
        // figure kept on disk past its reset - a used-up October read on 3
        // November with the relay not answering - showed "used up" on the
        // chip and Mission Control all night, though the commentary and the
        // voice had already moved on. Shown as unknown until the relay says.
        var now = _utcNow();
        var expired = entry.Known
            && (entry.ResetsUtc is { } resets ? resets <= now : entry.Period.Length > 0 && entry.Period != PeriodOf(null, now));

        if (expired)
        {
            return new Meter(kind, false, 0, entry.Allowance, entry.TopUp, entry.ResetsUtc, entry.AsOfUtc, 0,
                "Last month's figure - waiting for this month's from the relay.");
        }

        return new Meter(kind, entry.Known, entry.Used, entry.Allowance, entry.TopUp, entry.ResetsUtc, entry.AsOfUtc, tonight, entry.Note);
    }

    /// <summary>
    /// Which month a figure belongs to, as "yyyy-MM": the month before the
    /// reset when the reset is known (a relay's 1 Nov reset is October's
    /// figure; an ElevenLabs plan resetting on the 7th is that month's
    /// period), otherwise this UTC month. The same answer either way for a
    /// relay, so a figure that arrives once with its reset date and once
    /// without does not look like a new month.
    /// </summary>
    internal static string PeriodOf(DateTime? resetsUtc, DateTime nowUtc) =>
        (resetsUtc is { } resets ? resets.AddDays(-1) : nowUtc).ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static string NightKey(DateTime nowUtc) =>
        TonightAtTheTrack.NightOf(nowUtc.ToLocalTime()).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string NoticeKey(MeterKind kind, string period, MeterLevel level) =>
        $"{kind}|{period}|{level}";

    /// <summary>A new race night forgets last night's starting figures. True when it did.</summary>
    private bool RollNight(DateTime nowUtc)
    {
        var night = NightKey(nowUtc);

        if (_night == night)
        {
            return false;
        }

        _night = night;
        _nightStart.Clear();
        return true;
    }

    private static DateTime AsUtc(DateTime moment) => moment.Kind switch
    {
        DateTimeKind.Utc => moment,
        DateTimeKind.Local => moment.ToUniversalTime(),
        _ => DateTime.SpecifyKind(moment, DateTimeKind.Utc),
    };

    /// <summary>Reads the file the first time anything is asked. Under _gate.</summary>
    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        var path = FilePath;

        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var saved = JsonSerializer.Deserialize<SavedFile>(SafeFile.ReadAllTextWithRetry(path), JsonOptions);

            if (saved is null)
            {
                // WHY (23 Oct 2026, bug hunt 10): a file holding the literal
                // "null" parses to nothing without throwing. It used to
                // return here with NoticesUnknown left false and no record
                // of this month's emails, so the 80%/100% emails went again.
                // It is now treated as unreadable, like the other stores
                // (bug hunt 5): the catch below starts empty, keeps a copy
                // and sends no usage email this session.
                throw new InvalidDataException("usage-meters.json holds no data.");
            }

            foreach (var (name, entry) in saved.Meters)
            {
                if (Enum.TryParse<MeterKind>(name, out var kind) && Enum.IsDefined(kind) && entry is not null)
                {
                    entry.AsOfUtc = AsUtc(entry.AsOfUtc);
                    entry.ResetsUtc = entry.ResetsUtc is { } r ? AsUtc(r) : null;
                    entry.Period ??= string.Empty;
                    _meters[kind] = entry;
                }
            }

            _night = saved.Night ?? string.Empty;

            foreach (var (name, start) in saved.NightStart)
            {
                if (Enum.TryParse<MeterKind>(name, out var kind) && Enum.IsDefined(kind))
                {
                    _nightStart[kind] = start;
                }
            }

            foreach (var key in saved.Sent ?? new List<string>())
            {
                _sent.Add(key);
            }
        }
        catch (Exception ex)
        {
            // Mostly a record of figures the relay will give again: start
            // empty and carry on. But it also holds which emails have gone
            // this month, so none are sent this session (NoticesUnknown);
            // and a file that could not be READ at all - locked for a
            // moment at startup, the usual case - is most likely fine, so it
            // is not written over either, and the next start reads it.
            _meters.Clear();
            _nightStart.Clear();
            _sent.Clear();
            _night = string.Empty;
            NoticesUnknown = true;
            _holdWrites = SafeFile.HoldWritesAfterUnreadable(path, ex);
        }
    }

    /// <summary>Writes the file now. Never throws.</summary>
    internal void Save()
    {
        lock (_saveGate)
        {
            string json;
            string path;

            lock (_gate)
            {
                if (!_loaded || _holdWrites)
                {
                    return;
                }

                // Only the last few months of sent emails are worth keeping.
                var oldest = _utcNow().AddMonths(-3).ToString("yyyy-MM", CultureInfo.InvariantCulture);
                _sent.RemoveWhere(key => key.Split('|') is { Length: 3 } parts && string.CompareOrdinal(parts[1], oldest) < 0);

                var file = new SavedFile
                {
                    Meters = _meters.ToDictionary(m => m.Key.ToString(), m => m.Value),
                    Night = _night,
                    NightStart = _nightStart.ToDictionary(m => m.Key.ToString(), m => m.Value),
                    Sent = _sent.OrderBy(k => k, StringComparer.Ordinal).ToList(),
                };

                try
                {
                    json = JsonSerializer.Serialize(file, JsonOptions);
                }
                catch (Exception)
                {
                    return;
                }

                path = FilePath;
            }

            SafeFile.WriteAllText(path, json);
        }
    }

    /// <summary>A save a moment from now, gathering up the figures that arrive in between.</summary>
    private void SaveSoon()
    {
        if (_saveDelay <= TimeSpan.Zero)
        {
            Save();
            return;
        }

        if (Interlocked.Exchange(ref _savePending, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_saveDelay).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _savePending, 0);
                Save();
            }
        });
    }

    // One subscriber's fault must never reach whoever reported the figure -
    // the voice client reports from the middle of speaking a line.
    private void RaiseChanged()
    {
        foreach (var handler in Changed?.GetInvocationList() ?? Array.Empty<Delegate>())
        {
            try
            {
                ((Action)handler)();
            }
            catch (Exception ex)
            {
                Log($"[usage] A page showing the allowances failed to update ({ex.Message}).");
            }
        }
    }

    private void RaiseLevelReached(Meter meter, MeterLevel level)
    {
        foreach (var handler in LevelReached?.GetInvocationList() ?? Array.Empty<Delegate>())
        {
            try
            {
                ((Action<Meter, MeterLevel>)handler)(meter, level);
            }
            catch (Exception ex)
            {
                Log($"[usage] Couldn't act on {meter.Name} reaching {level} ({ex.Message}).");
            }
        }
    }
}
