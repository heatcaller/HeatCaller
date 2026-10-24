using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// The two private documents a driver's phone reads from the relay, keyed
/// by badge id (22 Oct 2026, driver experience, items 2-4, 8, 12-14):
///
///  - <b>driverlive</b>, <c>{at, drivers:{badge: entry}}</c>: their next
///    race, the corner they marshal, when their transponder was last heard,
///    whether they are checked in, whether tonight is their first night, and
///    what the desk has answered to things they asked for.
///  - <b>driverme</b>, <c>{drivers:{badge: me}}</c>: their own details, as
///    the "Your details" form on their night page starts from.
///
/// PURE, so it can be tested without a form: MainForm.DriverLive gathers
/// the inputs on the UI thread and sends what this returns.
///
/// WHAT NEVER GOES IN. A transponder - every key is the badge id, and every
/// race and post is named by title. Other drivers' names. Ghosts and
/// rental transponders, which are nobody's to follow. Every field that does
/// not apply is left out rather than sent empty or false, so a quiet
/// driver's entry is a few bytes.
/// </summary>
internal static class DriverLive
{
    /// <summary>The relay's cap for driverlive (types.ts). Entries past it are left out, least relevant first.</summary>
    public const int MostLiveBytes = 256 * 1024;

    /// <summary>The relay's cap for driverme.</summary>
    public const int MostMeBytes = 512 * 1024;

    /// <summary>How many races ahead a phone is told about.</summary>
    public const int UpcomingCount = 12;

    /// <summary>The most of a driver's answered asks carried, newest last.</summary>
    public const int MostAsks = 10;

    /// <summary>The most battery packs offered on the phone (BatteryPackStore's picker shows as many).</summary>
    public const int MostPacks = 12;

    /// <summary>
    /// What a sighting's time is rounded UP to. Rounded so a car circulating
    /// in practice does not change the document on every pass; up rather
    /// than to the nearest, so a pass a moment after the driver pressed
    /// "Check my transponder" can never read as before it.
    /// </summary>
    public static readonly TimeSpan SeenRounding = TimeSpan.FromSeconds(10);

    /// <summary>What the transponder watch knows about one car (TransponderWatch.Sighting, copied).</summary>
    public sealed record Seen(DateTime LastSeenUtc, bool SignalKnown, bool Weak);

    /// <summary>One battery pack a driver can pick.</summary>
    public sealed record PackChoice(string Id, string Label);

    /// <summary>A driver's packs: the one in their car now (or null) and what they can choose from.</summary>
    public sealed record PackView(string? CurrentId, IReadOnlyList<PackChoice> Choices);

    /// <summary>Everything driverlive is built from. Every list is in the order it was given.</summary>
    public sealed class LiveInputs
    {
        public DateTime NowUtc { get; init; }

        /// <summary>The races still to run, in running order (RaceControlControl.UpcomingRaces(<see cref="UpcomingCount"/>)).</summary>
        public IReadOnlyList<UpcomingRace> Upcoming { get; init; } = Array.Empty<UpcomingRace>();

        /// <summary>Tonight's marshal duties (MarshalAssigner.DutiesForDay).</summary>
        public IReadOnlyList<MarshalAssigner.MarshalDuty> Duties { get; init; } = Array.Empty<MarshalAssigner.MarshalDuty>();

        /// <summary>Who has raced tonight, by transponder.</summary>
        public IEnumerable<string> RacedTonight { get; init; } = Array.Empty<string>();

        /// <summary>Who is checked in tonight, by transponder.</summary>
        public IEnumerable<string> CheckedIn { get; init; } = Array.Empty<string>();

        /// <summary>Everyone who might have asks to hear back about, by transponder - each is in only when <see cref="AsksOf"/> has something for them.</summary>
        public IEnumerable<string> AskCandidates { get; init; } = Array.Empty<string>();

        /// <summary>The profile a transponder belongs to (any of theirs), or null.</summary>
        public Func<string, DriverProfile?> ProfileOf { get; init; } = _ => null;

        /// <summary>A driver's badge id from their MAIN transponder, or null when they have none.</summary>
        public Func<string, string?> BadgeOf { get; init; } = _ => null;

        /// <summary>When a main transponder was last heard, or null.</summary>
        public Func<string, Seen?>? SeenOf { get; init; }

        /// <summary>Whether tonight is a main transponder's first night (DriverFirsts.IsFirstNight).</summary>
        public Func<string, bool>? FirstNight { get; init; }

        /// <summary>The desk's answers for a badge id (MainForm._askAnswersFor) - the last 24 hours' worth.</summary>
        public Func<string, IReadOnlyList<DriverAskAnswer>>? AsksOf { get; init; }
    }

    /// <summary>
    /// A driver as the documents know them: their main transponder (which
    /// stays on the laptop) and their badge id (which is the key). Null for
    /// anybody who is not to be in them: no profile, a ghost, a rental, no
    /// badge.
    /// </summary>
    internal static (string Main, string Badge)? Identify(string? transponder, Func<string, DriverProfile?> profileOf, Func<string, string?> badgeOf)
    {
        var raw = transponder?.Trim();

        if (string.IsNullOrEmpty(raw) || GhostService.IsGhost(raw))
        {
            return null;
        }

        var profile = profileOf(raw);

        // A rental or loaner transponder is the club's, worn by whoever had
        // it tonight - following it would follow somebody else next week.
        if (profile is null || profile.IsRental)
        {
            return null;
        }

        var main = profile.Transponder?.Trim();

        if (string.IsNullOrEmpty(main) || GhostService.IsGhost(main))
        {
            return null;
        }

        var badge = badgeOf(main);

        return DriverNight.IsADriverId(badge) ? (main, badge!) : ((string Main, string Badge)?)null;
    }

    /// <summary>
    /// The driverlive document. Who is in it: everyone on the grid of the
    /// next <see cref="UpcomingCount"/> races, their marshals, everyone
    /// checked in or raced tonight, and anyone the desk has answered lately
    /// - in that order, which is also the order they are kept in should
    /// the document ever outgrow <see cref="MostLiveBytes"/>.
    /// </summary>
    public static string Document(LiveInputs inputs)
    {
        var profileOf = inputs.ProfileOf;
        var badgeOf = inputs.BadgeOf;

        (string Main, string Badge)? Who(string? transponder) => Identify(transponder, profileOf, badgeOf);

        var upcoming = inputs.Upcoming ?? Array.Empty<UpcomingRace>();

        // Each grid by main transponder, so a driver on the grid under a
        // second car's transponder is still called up as themselves.
        var byMain = upcoming
            .Select(race => race with
            {
                Grid = race.Grid.Select(t => Who(t)?.Main ?? string.Empty).ToList(),
            })
            .ToList();

        var callUps = PhoneFollow.CallUps(byMain);

        // The first corner each driver marshals among the races ahead, and
        // how far ahead it is: its place in the list, so 0 is the race on
        // track now (or the next one when nothing is).
        var duties = inputs.Duties ?? Array.Empty<MarshalAssigner.MarshalDuty>();
        var marshals = new Dictionary<string, (MarshalAssigner.MarshalDuty Duty, int Away)>(StringComparer.OrdinalIgnoreCase);
        var marshalOrder = new List<string>();

        for (var i = 0; i < upcoming.Count; i++)
        {
            var roundId = upcoming[i].RoundId;

            if (string.IsNullOrEmpty(roundId))
            {
                continue;
            }

            foreach (var duty in duties.Where(d => d is not null && string.Equals(d.RoundId, roundId, StringComparison.Ordinal)))
            {
                if (Who(duty.Transponder) is { } marshal && !marshals.ContainsKey(marshal.Main))
                {
                    marshals[marshal.Main] = (duty, i);
                    marshalOrder.Add(duty.Transponder);
                }
            }
        }

        var checkedIn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in inputs.CheckedIn ?? Array.Empty<string>())
        {
            if (Who(t) is { } one)
            {
                checkedIn.Add(one.Main);
            }
        }

        // Everybody, in order of how much the phone has to tell them.
        var people = new List<(string Main, string Badge)>();
        var badges = new HashSet<string>(StringComparer.Ordinal);
        var asks = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.Ordinal);

        void Consider(string? transponder, bool onlyWithAsks = false)
        {
            if (Who(transponder) is not { } who || badges.Contains(who.Badge))
            {
                return;
            }

            var answers = AsksFor(who.Badge, inputs.AsksOf);

            if (onlyWithAsks && answers.Count == 0)
            {
                return;
            }

            badges.Add(who.Badge);
            people.Add(who);

            if (answers.Count > 0)
            {
                asks[who.Badge] = answers;
            }
        }

        foreach (var race in upcoming)
        {
            foreach (var t in race.Grid)
            {
                Consider(t);
            }
        }

        foreach (var t in marshalOrder)
        {
            Consider(t);
        }

        foreach (var t in inputs.CheckedIn ?? Array.Empty<string>())
        {
            Consider(t);
        }

        foreach (var t in inputs.RacedTonight ?? Array.Empty<string>())
        {
            Consider(t);
        }

        foreach (var t in inputs.AskCandidates ?? Array.Empty<string>())
        {
            Consider(t, onlyWithAsks: true);
        }

        var drivers = new Dictionary<string, object?>(StringComparer.Ordinal);

        // Two braces, the "at" pair and "drivers":{} - and room to spare.
        var budget = MostLiveBytes - 256;
        var used = 0;

        foreach (var (main, badge) in people)
        {
            var entry = new Dictionary<string, object?>();

            if (callUps.TryGetValue(main, out var callUp))
            {
                entry["next"] = NextOf(callUp);
            }

            if (marshals.TryGetValue(main, out var marshal))
            {
                entry["marshal"] = new Dictionary<string, object?>
                {
                    ["race"] = marshal.Duty.RoundTitle,
                    ["post"] = marshal.Duty.PostName,
                    ["away"] = marshal.Away,
                };
            }

            if (inputs.SeenOf?.Invoke(main) is { } seen && seen.LastSeenUtc > DateTime.MinValue)
            {
                entry["seen"] = new Dictionary<string, object?>
                {
                    ["at"] = Iso(RoundUp(AsUtc(seen.LastSeenUtc), SeenRounding)),
                    ["signal"] = SignalWord(seen),
                };
            }

            if (checkedIn.Contains(main))
            {
                entry["checkedIn"] = true;
            }

            if (inputs.FirstNight?.Invoke(main) == true)
            {
                entry["firstNight"] = true;
            }

            if (asks.TryGetValue(badge, out var answered))
            {
                entry["asks"] = answered;
            }

            // Measured one by one, so the cap drops whole drivers - the
            // least relevant, last in the list - rather than the document.
            var bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(entry)) + badge.Length + 4;

            if (used + bytes > budget)
            {
                break;
            }

            used += bytes;
            drivers[badge] = entry;
        }

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            // To the minute, so a document that has not changed is the same
            // string for a minute and is not sent again - and changes once a
            // minute, which is the heartbeat that keeps it fresh on the
            // relay (it ignores one over thirty minutes old).
            ["at"] = Iso(RoundDown(AsUtc(inputs.NowUtc), TimeSpan.FromMinutes(1))),
            ["drivers"] = InBadgeOrder(drivers),
        });
    }

    /// <summary>The "next" object: the call-up, in the phone's words too.</summary>
    private static Dictionary<string, object?> NextOf(CallUp callUp)
    {
        var next = new Dictionary<string, object?>
        {
            ["race"] = callUp.Race,
            ["away"] = callUp.Away,
            ["slot"] = callUp.Slot,
        };

        if (callUp.OnTrack)
        {
            next["now"] = true;
        }

        if (callUp.Staged)
        {
            next["staged"] = true;
        }

        // To the minute: an estimate quoted to the second is a promise, and
        // one that moves every tick would send the document every tick.
        if (callUp.EtaUtc is { } eta)
        {
            next["eta"] = Iso(RoundToMinute(AsUtc(eta)));
        }

        next["words"] = PhoneFollow.Words(callUp);

        return next;
    }

    /// <summary>"good", "weak" or "unknown" - a decoder that gives no signal reading is unknown, never good.</summary>
    internal static string SignalWord(Seen seen) =>
        !seen.SignalKnown ? "unknown"
        : seen.Weak ? "weak"
        : "good";

    /// <summary>
    /// A badge's answered asks for the phone: only well-formed ones (a ref,
    /// a kind and a state the page knows), the newest <see cref="MostAsks"/>,
    /// with what the desk said only when it said something.
    /// </summary>
    private static List<Dictionary<string, object?>> AsksFor(string badge, Func<string, IReadOnlyList<DriverAskAnswer>>? asksOf)
    {
        var list = new List<Dictionary<string, object?>>();

        if (asksOf is null)
        {
            return list;
        }

        IReadOnlyList<DriverAskAnswer>? answers;

        try
        {
            answers = asksOf(badge);
        }
        catch
        {
            // The desk's store is another package's; a fault there must not
            // take everybody's next race off their phone.
            return list;
        }

        foreach (var answer in (answers ?? Array.Empty<DriverAskAnswer>()).Where(a => a is not null).TakeLast(MostAsks))
        {
            var reference = (answer.Ref ?? string.Empty).Trim().ToUpperInvariant();

            if (reference.Length != 6 || !reference.All(Uri.IsHexDigit) || !DriverAsk.IsKind(answer.Kind) || !DriverAsk.IsState(answer.State))
            {
                continue;
            }

            var one = new Dictionary<string, object?>
            {
                ["ref"] = reference,
                ["kind"] = answer.Kind,
                ["state"] = answer.State,
            };

            var said = (answer.Said ?? string.Empty).Trim();

            if (said.Length > 0)
            {
                one["said"] = said.Length > DriverAsk.SaidMaxChars ? said[..DriverAsk.SaidMaxChars] : said;
            }

            list.Add(one);
        }

        return list;
    }

    /// <summary>
    /// The driverme document: every driver's own details, as their phone's
    /// "Your details" form starts from, in the order given (which is also
    /// the order kept should it ever outgrow <see cref="MostMeBytes"/>).
    ///
    /// The email is masked ("s•••@gmail.com"): the badge code is a bearer
    /// credential, and whoever holds one should be able to recognize the
    /// address, not read it. Never their transponder.
    /// </summary>
    public static string MeDocument(
        IEnumerable<DriverProfile> profiles,
        Func<string, string?> badgeOf,
        Func<DriverProfile, PackView?>? packsOf = null)
    {
        var drivers = new Dictionary<string, object?>(StringComparer.Ordinal);
        var budget = MostMeBytes - 64;
        var used = 0;

        foreach (var profile in profiles ?? Array.Empty<DriverProfile>())
        {
            if (profile is null)
            {
                continue;
            }

            var main = profile.Transponder?.Trim() ?? string.Empty;

            if (main.Length == 0 || GhostService.IsGhost(main) || profile.IsRental)
            {
                continue;
            }

            var badge = badgeOf(main);
            var name = (profile.DriverName ?? string.Empty).Trim();

            // A car nobody has named is not somebody, as DriverNight has it.
            if (!DriverNight.IsADriverId(badge) || drivers.ContainsKey(badge!) || name.Length == 0)
            {
                continue;
            }

            var me = new Dictionary<string, object?> { ["name"] = name };

            AddText(me, "announcer", profile.AnnouncerName);
            AddText(me, "pronouns", profile.Pronouns);
            AddText(me, "about", profile.AboutLine);
            AddText(me, "email", MaskedEmail(profile.Email));

            if (profile.EmailMyNight)
            {
                me["emailMyNight"] = true;
            }

            var privacy = new Dictionary<string, object?>();

            if (profile.NoPhotoOnline)
            {
                privacy["noPhoto"] = true;
            }

            if (profile.NoVideoOnline)
            {
                privacy["noVideo"] = true;
            }

            if (profile.InitialsOnline)
            {
                privacy["initials"] = true;
            }

            if (privacy.Count > 0)
            {
                me["privacy"] = privacy;
            }

            if (PacksOf(profile, packsOf) is { } packs)
            {
                me["packs"] = packs;
            }

            var bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(me)) + badge!.Length + 4;

            if (used + bytes > budget)
            {
                break;
            }

            used += bytes;
            drivers[badge] = me;
        }

        return JsonSerializer.Serialize(new Dictionary<string, object?> { ["drivers"] = InBadgeOrder(drivers) });
    }

    /// <summary>
    /// The "packs" object, or null when there is nothing to choose from.
    /// Only ids of the shape the relay takes back in a pack ask (32 hex);
    /// at most <see cref="MostPacks"/>, and the one in their car always
    /// among them.
    /// </summary>
    private static Dictionary<string, object?>? PacksOf(DriverProfile profile, Func<DriverProfile, PackView?>? packsOf)
    {
        PackView? view;

        try
        {
            view = packsOf?.Invoke(profile);
        }
        catch
        {
            return null;
        }

        if (view is null)
        {
            return null;
        }

        var current = IsPackId(view.CurrentId) ? view.CurrentId!.Trim() : null;

        var all = (view.Choices ?? Array.Empty<PackChoice>())
            .Where(c => c is not null && IsPackId(c.Id))
            .GroupBy(c => c.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var choices = all.Take(MostPacks).ToList();

        if (current is not null
            && !choices.Any(c => string.Equals(c.Id.Trim(), current, StringComparison.OrdinalIgnoreCase))
            && all.FirstOrDefault(c => string.Equals(c.Id.Trim(), current, StringComparison.OrdinalIgnoreCase)) is { } inCar)
        {
            if (choices.Count >= MostPacks)
            {
                choices.RemoveAt(choices.Count - 1);
            }

            choices.Add(inCar);
        }

        if (choices.Count == 0 && current is null)
        {
            return null;
        }

        var packs = new Dictionary<string, object?>();

        if (current is not null)
        {
            packs["current"] = current;
        }

        packs["choices"] = choices
            .Select(c => new Dictionary<string, object?>
            {
                ["id"] = c.Id.Trim(),
                ["label"] = string.IsNullOrWhiteSpace(c.Label) ? "(unlabeled)" : c.Label.Trim(),
            })
            .ToList();

        return packs;
    }

    /// <summary>32 hex characters - a pack id as BatteryPack makes them, and as the relay checks them.</summary>
    internal static bool IsPackId(string? id)
    {
        var trimmed = id?.Trim();
        return trimmed is { Length: 32 } && trimmed.All(Uri.IsHexDigit);
    }

    /// <summary>
    /// "sam.parker@gmail.com" as "s•••@gmail.com": enough to recognize your
    /// own address, not enough to read somebody else's. Empty for none;
    /// "•••" for something that is not an address.
    /// </summary>
    internal static string MaskedEmail(string? email)
    {
        var text = (email ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return string.Empty;
        }

        var at = text.LastIndexOf('@');

        return at <= 0 || at == text.Length - 1
            ? "•••"
            : $"{text[0]}•••{text[at..]}";
    }

    /// <summary>
    /// The drivers kept, in badge order. Kept in priority order up to here
    /// for the size cap; written in a fixed order so the same drivers make
    /// the same string whatever order the check-in sheet or the profile
    /// list happened to give them in - which is what "only sent when it
    /// changed" compares.
    /// </summary>
    private static Dictionary<string, object?> InBadgeOrder(Dictionary<string, object?> drivers) =>
        drivers
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static void AddText(Dictionary<string, object?> into, string key, string? value)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length > 0)
        {
            into[key] = text;
        }
    }

    private static string Iso(DateTime utc) =>
        utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static DateTime RoundDown(DateTime utc, TimeSpan step) =>
        new(utc.Ticks - utc.Ticks % step.Ticks, DateTimeKind.Utc);

    private static DateTime RoundUp(DateTime utc, TimeSpan step) =>
        utc.Ticks % step.Ticks == 0 ? new DateTime(utc.Ticks, DateTimeKind.Utc) : RoundDown(utc, step) + step;

    private static DateTime RoundToMinute(DateTime utc) =>
        RoundDown(utc + TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1));

    /// <summary>A time with no kind is taken as the UTC it was made as.</summary>
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
