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

        /// <summary>The tag a transponder's row carries on the relay's public board (WebServerService.RelayRowTag), or null.</summary>
        public Func<string, string?>? RowTagOf { get; init; }

        /// <summary>The target lap race control set for a transponder tonight, in ms (DriverTargets.TargetMs), or null.</summary>
        public Func<string, long?>? TargetOf { get; init; }

        /// <summary>
        /// Whether a marshal (main transponder) has said from their phone
        /// they're at their post for a round (12 Nov 2026, setup plan step 8 -
        /// RaceControlControl.MarshalIsIn): (round id, main).
        /// </summary>
        public Func<string, string, bool>? MarshalIn { get; init; }

        /// <summary>Asks to cover a marshal post still open (MarshalSwaps.Offers) - 12 Nov 2026.</summary>
        public IReadOnlyList<MarshalSwaps.Offer> CoverOffers { get; init; } = Array.Empty<MarshalSwaps.Offer>();

        /// <summary>A badge id's main transponder, or null - for the names on an ask to cover.</summary>
        public Func<string, string?>? MainOfBadge { get; init; }

        /// <summary>
        /// Whether a driver (main transponder) has paid tonight's entry, when
        /// the race day has a fee (12 Nov 2026, setup plan step 8 - DeskMoney.ForPhone).
        /// Only ever their own: it goes in their own entry.
        /// </summary>
        public Func<string, DeskMoney.PhoneMoney?>? MoneyOf { get; init; }
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

        // The next race to start: the first not on track (12 Nov 2026).
        var nextIndex = -1;

        for (var i = 0; i < upcoming.Count; i++)
        {
            if (!upcoming[i].OnTrack)
            {
                nextIndex = i;
                break;
            }
        }

        var upcomingRounds = upcoming.Select(u => u.RoundId).Where(id => !string.IsNullOrEmpty(id)).ToHashSet(StringComparer.Ordinal);
        var offers = inputs.CoverOffers ?? Array.Empty<MarshalSwaps.Offer>();

        string ShortNameOfMain(string? t) => t is null ? string.Empty : MarshalSwaps.ShortName(profileOf(t)?.DriverName);

        string ShortNameOfBadge(string badge) => ShortNameOfMain(inputs.MainOfBadge?.Invoke(badge));

        // Who a marshal can ask to cover their post (MarshalSwaps.Candidates):
        // by key and short name, never by transponder or badge code.
        // JUNIORS SWAP THROUGH THE DESK (15 Nov 2026, plan 4.4): a junior is
        // offered nobody to ask (their phone points them at the desk), and is
        // never offered to anyone else - no driver-to-driver asks with a child.
        List<Dictionary<string, object?>>? CoverChoices(string main, string roundId, int away)
        {
            if (profileOf(main)?.IsJunior == true)
            {
                return null;
            }

            var driving = byMain[away].Grid;
            var marshalling = duties
                .Where(d => d is not null && string.Equals(d.RoundId, roundId, StringComparison.Ordinal))
                .Select(d => Who(d.Transponder)?.Main ?? d.Transponder);

            var list = new List<Dictionary<string, object?>>();

            foreach (var t in MarshalSwaps.Candidates(checkedIn, driving, marshalling, main))
            {
                if (Who(t) is not { } who || ShortNameOfMain(who.Main) is not { Length: > 0 } name
                    || profileOf(who.Main)?.IsJunior == true)
                {
                    continue;
                }

                list.Add(new Dictionary<string, object?> { ["k"] = MarshalSwaps.KeyOf(who.Badge), ["name"] = name });
            }

            return list
                .OrderBy(c => (string)c["name"]!, StringComparer.CurrentCultureIgnoreCase)
                .Take(MarshalSwaps.MostChoices)
                .ToList();
        }

        Dictionary<string, object?>? CoverAskFor(string badge) =>
            offers.LastOrDefault(o => o.ToBadge == badge && o.State == MarshalSwaps.Asked && upcomingRounds.Contains(o.RoundId)) is { } o
                ? new Dictionary<string, object?>
                {
                    ["offer"] = o.Id,
                    ["from"] = ShortNameOfBadge(o.FromBadge) is { Length: > 0 } from ? from : "A driver",
                    ["race"] = o.Title,
                    ["post"] = o.Post,
                }
                : null;

        Dictionary<string, object?>? AskedCoverOf(string badge) =>
            offers.LastOrDefault(o => o.FromBadge == badge && upcomingRounds.Contains(o.RoundId)) is { } o
                ? new Dictionary<string, object?>
                {
                    ["race"] = o.Title,
                    ["post"] = o.Post,
                    ["to"] = ShortNameOfBadge(o.ToBadge),
                    ["state"] = o.State,
                }
                : null;

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

                // Their races after the next, with when each should go (19
                // Nov 2026, driver phone plan item 10 step 4): for "Add my
                // races" on their own page, which makes the calendar file on
                // the phone. Only races with an estimate; the title and the
                // time, nothing about anyone else.
                if (LaterOf(byMain, main) is { Count: > 0 } later)
                {
                    entry["later"] = later;
                }
            }

            if (marshals.TryGetValue(main, out var marshal))
            {
                var duty = new Dictionary<string, object?>
                {
                    ["race"] = marshal.Duty.RoundTitle,
                    ["post"] = marshal.Duty.PostName,
                    ["away"] = marshal.Away,
                };

                // Marshals from their phones (12 Nov 2026, setup plan step
                // 8): the round, so "I'm at my post" and "Ask someone to
                // cover" can name it; whether it is the next race to start
                // (not the one on track), when it is expected to, and whether
                // they've said they're in. A round id names a race, not anyone.
                duty["round"] = marshal.Duty.RoundId;

                if (marshal.Away == nextIndex)
                {
                    duty["next"] = true;
                }

                if (upcoming[marshal.Away].EtaUtc is { } dutyEta && !upcoming[marshal.Away].OnTrack)
                {
                    duty["eta"] = Iso(RoundToMinute(AsUtc(dutyEta)));
                }

                if (inputs.MarshalIn?.Invoke(marshal.Duty.RoundId, main) == true)
                {
                    duty["in"] = true;
                }

                if (CoverChoices(main, marshal.Duty.RoundId, marshal.Away) is { Count: > 0 } choices)
                {
                    duty["choices"] = choices;
                }
                else if (profileOf(main)?.IsJunior == true)
                {
                    // Their phone says "Need a swap? Ask at the desk." instead.
                    duty["swapAtDesk"] = true;
                }

                entry["marshal"] = duty;
            }

            // Asks to cover a post (12 Nov 2026): one asked of them, and the
            // last one they sent, while its race is still to run.
            if (CoverAskFor(badge) is { } coverAsk)
            {
                entry["coverAsk"] = coverAsk;
            }

            if (AskedCoverOf(badge) is { } askedCover)
            {
                entry["askedCover"] = askedCover;
            }

            // Tonight's entry fee, paid or owing - their own, never anyone
            // else's (12 Nov 2026, setup plan step 8).
            if (inputs.MoneyOf?.Invoke(main) is { } money)
            {
                entry["entry"] = new Dictionary<string, object?>
                {
                    ["fee"] = money.Fee,
                    ["paid"] = money.Paid,
                    ["words"] = money.Words,
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

            if (CarsOf(main, profileOf, inputs.RowTagOf, inputs.TargetOf) is { Count: > 0 } cars)
            {
                entry["cars"] = cars;
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

    /// <summary>The most later races one driver's entry carries.</summary>
    internal const int MostLater = 6;

    /// <summary>
    /// A driver's races after their next one (the first in the list they're
    /// on), each with its estimate: race title and eta. Races with no
    /// estimate are left out - a calendar entry needs a time.
    /// </summary>
    internal static List<Dictionary<string, object?>> LaterOf(IReadOnlyList<UpcomingRace> byMain, string main)
    {
        var later = new List<Dictionary<string, object?>>();
        var first = -1;

        for (var i = 0; i < byMain.Count; i++)
        {
            if (byMain[i].Grid.Contains(main, StringComparer.OrdinalIgnoreCase))
            {
                first = i;
                break;
            }
        }

        for (var i = first < 0 ? byMain.Count : first + 1; i < byMain.Count && later.Count < MostLater; i++)
        {
            var race = byMain[i];

            if (race.OnTrack || race.EtaUtc is not { } eta || !race.Grid.Contains(main, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            later.Add(new Dictionary<string, object?>
            {
                ["race"] = race.Title,
                ["eta"] = Iso(RoundToMinute(AsUtc(eta))),
            });
        }

        return later;
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
    /// <summary>
    /// The driver's own cars as the public board knows them (29 Oct 2026,
    /// spoken laps on My Night): each transponder's row tag, and its target
    /// lap when race control set one. The night page finds the driver's row
    /// on the board by tag, so it never has to go by a name somebody else
    /// may share, and says how the lap went against the target.
    ///
    /// Only the tags are sent, never a transponder, and only here, behind
    /// the driver's own badge code. Retired transponders are left out: they
    /// are not on track. Empty when the laptop has no tags to give.
    /// </summary>
    internal static List<Dictionary<string, object?>> CarsOf(
        string main,
        Func<string, DriverProfile?> profileOf,
        Func<string, string?>? rowTagOf,
        Func<string, long?>? targetOf)
    {
        var cars = new List<Dictionary<string, object?>>();

        if (rowTagOf is null)
        {
            return cars;
        }

        var profile = profileOf(main);
        IEnumerable<string> ids = profile is null ? new[] { main } : profile.AllTransponderIds.Where(t => !profile.IsRetired(t));
        var tags = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in ids)
        {
            if (GhostService.IsGhost(id) || rowTagOf(id) is not { Length: > 0 } tag || !tags.Add(tag))
            {
                continue;
            }

            var car = new Dictionary<string, object?> { ["tag"] = tag };

            if (targetOf?.Invoke(id) is long target && target > 0)
            {
                car["target"] = target;
            }

            cars.Add(car);
        }

        return cars;
    }

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

            // When the desk answered (11 Nov 2026), so the Me card on the
            // phone shows the answer while it is fresh. Only on an answer.
            if (answer.State != DriverAsk.Waiting && answer.AnsweredUtc is { } at)
            {
                one["at"] = DateTime.SpecifyKind(at, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
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
    /// <param name="crew">
    /// The crew drivers may pick (15 Nov 2026, driver crew plan), or null
    /// when the club has not switched "Drivers can pick their own crew" on -
    /// and then no driver's entry says anything about a crew.
    /// </param>
    public static string MeDocument(
        IEnumerable<DriverProfile> profiles,
        Func<string, string?> badgeOf,
        Func<DriverProfile, PackView?>? packsOf = null,
        CrewForPhone? crew = null)
    {
        var drivers = new Dictionary<string, object?>(StringComparer.Ordinal);

        // THE VOICES ONCE, beside the drivers, not in every entry: this
        // document is capped for everybody together (MostMeBytes), and an
        // ElevenLabs account's list repeated eighty times would crowd
        // drivers out of it. The relay hands each phone the list with its
        // own entry.
        var voices = crew is null ? null : CrewVoicesOf(crew);
        var voicesBytes = voices is null ? 0 : Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(voices)) + 16;
        var budget = MostMeBytes - 64 - voicesBytes;
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

            // A JUNIOR (15 Nov 2026, plans 1.1 and 2.2): their phone's details
            // form offers only the announcer name - pronouns, about line and
            // email are entered at the desk with a parent - and the relay
            // refuses the rest from their badge, so it is told who is one.
            // Their about line is treated as their crew notes are (CrewOf):
            // without a parent's consent only whether it is set goes online.
            if (profile.IsJunior)
            {
                me["junior"] = true;
            }

            if (profile.IsJunior && !profile.CrewNotesParentConsent)
            {
                if (!string.IsNullOrWhiteSpace(profile.AboutLine))
                {
                    me["aboutSet"] = true;
                }
            }
            else
            {
                AddText(me, "about", profile.AboutLine);
            }

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

            if (crew is not null)
            {
                me["crew"] = CrewOf(profile, crew);
            }

            var bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(me)) + badge!.Length + 4;

            if (used + bytes > budget)
            {
                break;
            }

            used += bytes;
            drivers[badge] = me;
        }

        var document = new Dictionary<string, object?> { ["drivers"] = InBadgeOrder(drivers) };

        if (voices is not null)
        {
            document["crewVoices"] = voices;
        }

        return JsonSerializer.Serialize(document);
    }

    /// <summary>
    /// What the phone is told about the crew drivers may pick: the engine in
    /// use and the voices on it they may choose from (DriverCrew.Pickable).
    /// </summary>
    public sealed record CrewForPhone(VoiceEngine Engine, IReadOnlyList<CrewVoiceOption> Voices);

    /// <summary>The most crew voices a phone is offered - the relay's own cap.</summary>
    public const int MostCrewVoices = 200;

    /// <summary>The crewVoices list: each voice's key, its name, and its sample when it has an https one.</summary>
    private static List<Dictionary<string, object?>> CrewVoicesOf(CrewForPhone crew) =>
        crew.Voices
            .Take(MostCrewVoices)
            .Select(v =>
            {
                var one = new Dictionary<string, object?>
                {
                    ["k"] = DriverCrew.KeyOf(v.Pick),
                    ["n"] = Truncate(DriverCrew.Clean(string.IsNullOrWhiteSpace(v.Pick.Name) ? v.Pick.Id : v.Pick.Name), 60),
                };

                if (Uri.TryCreate(v.Sample, UriKind.Absolute, out var sample) && sample.Scheme == Uri.UriSchemeHttps)
                {
                    one["s"] = sample.AbsoluteUri;
                }

                return one;
            })
            .ToList();

    private static string Truncate(string text, int most) => text.Length <= most ? text : text[..most].TrimEnd();

    /// <summary>
    /// One driver's "crew" object: their names and notes, and each voice as
    /// the key of a voice they may pick now - empty for the club's own, and
    /// for a pick that is hidden, reserved or on another engine. A pick for
    /// another engine also says "repick", so the phone can ask them to
    /// choose again rather than quietly showing the club's voice.
    /// </summary>
    internal static Dictionary<string, object?> CrewOf(DriverProfile profile, CrewForPhone crew)
    {
        var offered = new HashSet<string>(crew.Voices.Select(v => DriverCrew.KeyOf(v.Pick)), StringComparer.Ordinal);
        var repick = false;

        string KeyFor(CrewVoicePick? pick)
        {
            if (!DriverCrew.IsAPick(pick))
            {
                return string.Empty;
            }

            if (DriverCrew.EngineOf(pick) != crew.Engine)
            {
                repick = true;
                return string.Empty;
            }

            var key = DriverCrew.KeyOf(pick!);
            return offered.Contains(key) ? key : string.Empty;
        }

        var one = new Dictionary<string, object?>
        {
            ["junior"] = profile.IsJunior,
            // Under 13, or a junior with no age band yet (15 Nov 2026,
            // children's privacy plan 2.1): the phone hides the crew name
            // boxes and the relay refuses a name - they're picked at the desk.
            ["under13"] = profile.TreatAsUnder13,
            ["principal"] = new Dictionary<string, object?>
            {
                ["name"] = DriverCrew.Clean(profile.CrewPrincipalName),
                ["voice"] = KeyFor(profile.CrewPrincipalVoice),
            },
            ["tech"] = new Dictionary<string, object?>
            {
                ["name"] = DriverCrew.Clean(profile.CrewTechName),
                ["voice"] = KeyFor(profile.CrewTechVoice),
            },
        };

        var goal = DriverCrew.GoalTonight(profile, DateTime.Now);
        var thanks = DriverCrew.Clean(profile.CrewThanks);
        var car = DriverCrew.Clean(profile.CrewCarStory);

        // A JUNIOR'S WORDS LEAVE THIS LAPTOP ONLY ONCE A PARENT AGREED (15 Nov
        // 2026, plan 1.5): without CrewNotesParentConsent the relay is told
        // only whether each note is set - true or false, never the text - so
        // their phone can say "set at the desk" and nothing a child said is
        // held online. An adult's, or a junior's with consent, go as text.
        if (profile.IsJunior && !profile.CrewNotesParentConsent)
        {
            one["goal"] = goal.Length > 0;
            one["thanks"] = thanks.Length > 0;
            one["car"] = car.Length > 0;
        }
        else
        {
            one["goal"] = goal;
            one["thanks"] = thanks;
            one["car"] = car;
        }

        if (repick)
        {
            one["repick"] = true;
        }

        // A junior's notes are typed at the desk, never sent from a phone
        // (15 Nov 2026 - the switch that let a club take them was removed):
        // the phone shows no note boxes and the relay refuses one.
        if (profile.IsJunior)
        {
            one["notes"] = false;
        }

        return one;
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
