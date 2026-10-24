using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// What kind of night a race day is - which decides how much of the app it
/// needs to show.
///
/// The app supported two ways of working and never said so. Every night got
/// the full structure - qualifying rounds, TQ, mains seeded from them,
/// bump-ups, tiers - so a club running a simple evening either used all of
/// it or worked around it, and the words on screen described a day they
/// were not having.
///
/// IT BELONGS TO THE DAY, NOT TO THE INSTALL. The obvious version of this
/// is a setting picked once when the app is set up, and it is wrong for the
/// clubs that have both kinds of night: they would be flipping it twice a
/// month, and it would be wrong on the night that mattered. The person who
/// knows which kind of night it is, is the person starting the day.
///
/// RaceDay is zero on purpose. A day saved before this existed has no such
/// property in its JSON, reads back as RaceDay, and behaves exactly as it
/// always has.
/// </summary>
public enum RaceDayShape
{
    /// <summary>Qualifying heats, then finals seeded from them, with bump-ups between the tiers. The whole structure.</summary>
    RaceDay = 0,

    /// <summary>
    /// A flat list of races - each class out however many times, and that
    /// is the night. No finals, no seeding, no bump-ups, no tiers.
    /// </summary>
    ClubNight = 1,
}

/// <summary>
/// A named event - a club night, a two-day regional, a season final.
///
/// Exists because qualifying had no boundary: it gathered every recorded
/// round for a class, so a second race day would silently fold in the
/// first day's heats and seed the mains from a mixture of both. An event
/// says which rounds belong together.
///
/// Membership is an explicit set of round IDs rather than a date range.
/// Dates sound simpler but break on the cases that actually happen - an
/// event running past midnight, a practice day nobody wants counted, or two
/// clubs sharing a track on one date.
/// </summary>
public sealed class RaceEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Set when the event is closed off. Null while it's still running.</summary>
    public DateTime? FinishedUtc { get; set; }

    /// <summary>Rounds belonging to this event.</summary>
    public List<string> RoundIds { get; set; } = new();

    /// <summary>
    /// The championship this event counts towards, or empty for a
    /// standalone night.
    ///
    /// Empty is the common case and deliberately the default: most club
    /// racing is not a series, and an app that insists on a championship
    /// before it will run a Tuesday practice is an app nobody opens on a
    /// Tuesday.
    /// </summary>
    public string ChampionshipId { get; set; } = string.Empty;

    /// <summary>Where it is being run. Free text, matched against the venue list when one exists.</summary>
    public string VenueName { get; set; } = string.Empty;

    /// <summary>The layout the event is being run on - the default for every round in it, overridable per round.</summary>
    public string TrackLayoutName { get; set; } = string.Empty;

    /// <summary>Which classes are running. Empty means "whatever turns up", which is how a practice night works.</summary>
    public List<string> ClassNames { get; set; } = new();

    /// <summary>
    /// What kind of night this is - see <see cref="RaceDayShape"/>. Decides
    /// how much of the programme machinery the day shows.
    /// </summary>
    public RaceDayShape Shape { get; set; } = RaceDayShape.RaceDay;

    /// <summary>Shorthand for the test made all over the UI. A day is the full shape unless it says otherwise.</summary>
    public bool IsClubNight => Shape == RaceDayShape.ClubNight;

    /// <summary>How many qualifying rounds are planned per class. What the run sheet counts down.</summary>
    public int QualifyingRounds { get; set; } = 3;

    /// <summary>
    /// How many drivers share a heat when the programme is built.
    ///
    /// Zero means "whatever was used last" (RaceEventStore.HeatSizeFor), which is the right default for
    /// a club that runs the same shape every week and never thinks about
    /// it. It lives on the race day rather than in settings because a
    /// twelve-car club night and a four-car practice session are different
    /// days, not different preferences.
    /// </summary>
    public int DriversPerHeat { get; set; }

    /// <summary>
    /// Whether building the programme counts only drivers who have checked
    /// in, or everyone entered in the class.
    ///
    /// Set from the day's Shape when it is started: ON for a race day, which
    /// has a door, and OFF for a club night, where whoever turns up races.
    /// Changed freely afterwards on The Day.
    ///
    /// It was false for every new day, which made Who's Racing a lie - a
    /// whole page whose purpose is ticking people in, where the tick changed
    /// nothing until somebody found a checkbox on another tab. The reason it
    /// was false is in the history: a club with no sign-on desk would build a
    /// programme with nobody in it "and have no idea why". That has since
    /// been fixed at the other end - ProgramStage.NobodyCheckedIn names the
    /// setting, says which drivers it is excluding and offers to turn it off
    /// for today - so the default can now be the one that makes the page
    /// mean what it says.
    /// </summary>
    public bool CheckedInDriversOnly { get; set; }

    /// <summary>When the first race is meant to go out. Local time of day; the run sheet measures drift against it.</summary>
    public TimeSpan? FirstRaceLocalTime { get; set; }

    // --- Rolling into the next race -------------------------------------
    //
    // Whether finishing a race arms the next one by itself, and which race
    // it reaches for. This used to be one club-wide app setting on a strip
    // at the bottom of the programme, which put a decision made once at the
    // start of a night among the buttons pressed every ten minutes - and
    // made it impossible to run one night hands-off and the next by hand.
    //
    // NULL AND EMPTY ARE REAL STATES. They mean "this day never said", and
    // the club-wide setting answers instead - which is what every race day
    // written before these fields existed holds, so nothing changed for
    // anyone on upgrade. See AutoAdvanceOn and AutoAdvanceScopeOr.

    /// <summary>Roll straight into the next race, or null to follow the club's setting.</summary>
    public bool? AutoAdvanceRounds { get; set; }

    /// <summary>"drivers", "any" or "class" - or empty to follow the club's setting.</summary>
    public string AutoAdvanceScope { get; set; } = string.Empty;

    /// <summary>The class it is pinned to when the scope is "class".</summary>
    public string AutoAdvanceClassName { get; set; } = string.Empty;

    /// <summary>Whether to roll into the next race, with the club's setting as the answer when this day never said.</summary>
    public bool AutoAdvanceOn(bool clubDefault) => AutoAdvanceRounds ?? clubDefault;

    /// <summary>This day's scope, or the club's when it never said.</summary>
    public string AutoAdvanceScopeOr(string clubDefault) =>
        string.IsNullOrWhiteSpace(AutoAdvanceScope) ? clubDefault : AutoAdvanceScope;

    /// <summary>
    /// This day's pinned class, or the club's - taken together with the
    /// scope, never separately. A day that set its own scope owns its class
    /// too, including when that class is deliberately blank.
    /// </summary>
    public string AutoAdvanceClassOr(string clubDefault) =>
        string.IsNullOrWhiteSpace(AutoAdvanceScope) ? clubDefault : AutoAdvanceClassName;

    /// <summary>
    /// Marks the one event created to hold everything that pre-dates
    /// events being the spine. It is not a real meeting and is kept out of
    /// pickers by default - see RaceEventStore.LegacyEventId.
    /// </summary>
    public bool IsLegacy { get; set; }

    public bool IsOpen => FinishedUtc is null;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "(unnamed event)" : Name;
}

/// <summary>Loads/saves events and tracks which one new rounds join.</summary>
public sealed class RaceEventStore
{
    /// <summary>
    /// The event that holds everything recorded before events became the
    /// spine of the app.
    ///
    /// A fixed id rather than a generated one so the migration is
    /// idempotent: it can run on every launch and find its own work from
    /// last time instead of creating a second Legacy event each time.
    /// </summary>
    public const string LegacyEventId = "legacy-event";

    private readonly List<RaceEvent> _events;
    private string? _activeEventId;

    /// <summary>Raised when the current event changes, so anything scoped to it can re-read.</summary>
    public event EventHandler? CurrentChanged;

    /// <summary>
    /// Raised when a day is closed off by somebody closing it - never by an
    /// undo putting back a close (Close with a finishedUtc), which would
    /// otherwise do what closing the day does a second time. After the save
    /// and after CurrentChanged, so a handler sees the day as closed.
    ///
    /// WHY (22 Oct 2026, driver experience): the drivers' night emails can go
    /// by themselves when the race day closes (AppSettings.NightEmailAutomatic,
    /// MainForm.NightEmail). Raised on whatever thread closed the day; a
    /// handler that touches the window posts to it.
    /// </summary>
    public event Action<RaceEvent>? Closed;

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "events.json");

    private RaceEventStore(List<RaceEvent> events, string? activeEventId)
    {
        _events = events;
        _activeEventId = activeEventId;
    }

    /// <summary>
    /// Set when events.json was there and could not be read, so nothing saves
    /// an empty store over it.
    ///
    /// This is the store where a failed read was most expensive. Load returned
    /// an empty one with no way to tell it apart from a genuinely new install,
    /// and MainForm's constructor then ran EventSpine.Migrate and
    /// StartFirstDayIfNever unconditionally - three writes, in the same
    /// constructor, before any window appeared. A backup tool holding the file
    /// open past the retry budget on race morning was enough to replace every
    /// named race day and every RoundIds membership list with a legacy holder
    /// and a fresh empty day. The next launch then had no event for any round
    /// and filed the club's whole history under the legacy holder - the
    /// unbounded-qualifying fault race days were introduced to fix.
    ///
    /// Every other store in the tree already waits for a real user change
    /// after a failed read; ThemeStore states the rule out loud. This one
    /// obeys it now too.
    /// </summary>
    private bool _loadedUnreadable;

    /// <summary>
    /// True when events.json exists but could not be read this launch. The
    /// startup migration reads it: there is nothing safe to migrate INTO a
    /// store that does not know what it holds.
    /// </summary>
    public bool LoadedUnreadable => _loadedUnreadable;

    private sealed class EventFile
    {
        public List<RaceEvent> Events { get; set; } = new();
        public string? ActiveEventId { get; set; }
    }

    public static RaceEventStore Load()
    {
        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var file = JsonSerializer.Deserialize<EventFile>(SafeFile.ReadAllTextWithRetry(FilePath));
                if (file is not null)
                {
                    return new RaceEventStore(Repair(file.Events), file.ActiveEventId);
                }

                // Present, parsed, and not the shape this store writes. Not a
                // new install either, so the same rule applies.
                unreadable = true;
            }
        }
        catch (Exception ex)
        {
            // Unreadable file: start empty rather than refusing to open - but
            // remember, so nothing writes that emptiness back. See
            // _loadedUnreadable.
            SafeFile.PreserveUnreadable(FilePath, ex);
            unreadable = true;
        }

        return new RaceEventStore(new List<RaceEvent>(), null) { _loadedUnreadable = unreadable };
    }

    /// <summary>
    /// Puts back what an explicit JSON null took out of a parsed events.json.
    ///
    /// WHY (23 Oct 2026, bug hunt 10): the deserializer calls a setter with an
    /// explicit null, so the "= new()" defaults did not protect this file.
    /// "Events": null, a null day in the list, or a day with "RoundIds": null
    /// parsed without error and then threw in EventSpine.Migrate in MainForm's
    /// constructor - "HeatCaller couldn't start" on every launch, with nothing
    /// naming events.json. The other stores got this repair in bug hunt 8
    /// (A23-1); this one now does the same: null days are dropped, null lists
    /// become empty, null entries in them are dropped, and null text becomes
    /// empty. The app never writes nulls, so a file it saved is unchanged.
    /// </summary>
    private static List<RaceEvent> Repair(List<RaceEvent>? events)
    {
        var list = events ?? new List<RaceEvent>();
        list.RemoveAll(e => e is null);

        foreach (var e in list)
        {
            e.Id ??= Guid.NewGuid().ToString("N");
            e.Name ??= string.Empty;
            e.Notes ??= string.Empty;
            e.ChampionshipId ??= string.Empty;
            e.VenueName ??= string.Empty;
            e.TrackLayoutName ??= string.Empty;
            e.AutoAdvanceScope ??= string.Empty;
            e.AutoAdvanceClassName ??= string.Empty;
            e.RoundIds = (e.RoundIds ?? new List<string>()).Where(id => id is not null).ToList();
            e.ClassNames = (e.ClassNames ?? new List<string>()).Where(n => n is not null).ToList();
        }

        return list;
    }

    public void Save()
    {
        if (_loadedUnreadable)
        {
            // Everything this store holds this session is a guess. Writing it
            // would make the guess permanent - see _loadedUnreadable.
            return;
        }

        SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(
            new EventFile { Events = _events, ActiveEventId = _activeEventId },
            new JsonSerializerOptions { WriteIndented = true }));
    }

    public IReadOnlyList<RaceEvent> All =>
        _events.OrderByDescending(e => e.IsOpen).ThenByDescending(e => e.StartedUtc).ToList();

    public RaceEvent? Get(string? id) =>
        id is null ? null : _events.FirstOrDefault(e => e.Id == id);

    /// <summary>The heat size a club's first race day builds with, before any day has one of its own.</summary>
    public const int DefaultDriversPerHeat = 8;

    /// <summary>
    /// How many drivers share a heat on <paramref name="day"/>: its own
    /// number, or - for a day that never set one (RaceEvent.DriversPerHeat
    /// zero: started from the phone, or at first launch) - the most recent
    /// race day's that did, or 8 when none has. 2 to 20.
    ///
    /// One answer for everything that shows or uses it - Race Day → Setup
    /// and Set Up Tonight show it, the program builds with it. They used to
    /// disagree: the box showed 8 for such a day while the program built
    /// with a remembered size (AppSettings.LastHeatSize) that nothing ever
    /// wrote, 6 on a new install (docs check, 30 Sep).
    /// </summary>
    public int HeatSizeFor(RaceEvent? day)
    {
        if (day is { DriversPerHeat: > 0 })
        {
            return Math.Clamp(day.DriversPerHeat, 2, 20);
        }

        var last = _events
            .Where(e => !e.IsLegacy && e.DriversPerHeat > 0 && !ReferenceEquals(e, day))
            .OrderByDescending(e => e.StartedUtc)
            .FirstOrDefault();

        return Math.Clamp(last?.DriversPerHeat ?? DefaultDriversPerHeat, 2, 20);
    }

    /// <summary>
    /// The event new rounds join, if any. Null means rounds aren't being
    /// grouped - which is correct for a club that runs one night at a time
    /// and never needs the distinction.
    /// </summary>
    public RaceEvent? Active => Get(_activeEventId);

    /// <summary>
    /// The rounds that belong to the race day now open, as a set to filter
    /// by - or NULL when nothing should be filtered, which is both "no day
    /// open" and "the legacy holder is what's open".
    ///
    /// The holder returns null on purpose. It is where rounds go when no
    /// real day is running, so filtering to its list would hide any round
    /// made since - and a store that answers "these and no others" for the
    /// holder is how the whole programme quietly empties.
    ///
    /// The shape the round store's Unrecorded() and the qualifying
    /// calculator's roundFilter both take, so everything that asks "what is
    /// on tonight" asks it the same way.
    /// </summary>
    public IReadOnlySet<string>? ActiveRoundScope() =>
        Active is { IsLegacy: false } active
            ? active.RoundIds.ToHashSet(StringComparer.Ordinal)
            : null;

    /// <summary>
    /// The rounds qualifying counts: the race day now open's - and none at
    /// all when no race day is open. Never null.
    ///
    /// Deliberately not ActiveRoundScope, whose null means "don't filter".
    /// That is the right answer for a list of rounds to show or run, and the
    /// wrong one for a ranking: after End Race Day it made the standings and
    /// seeding rank every heat the class had ever run - months-old paces
    /// included - as tonight's order, and Seed Mains built an A-main from
    /// drivers who were not at the track (25 Sep audit).
    /// </summary>
    public IReadOnlySet<string> QualifyingScope() => DayScope();

    /// <summary>
    /// The open race day's rounds, and none when no race day is open. Never
    /// null. For anything that must not reach past tonight: what qualifying
    /// counts (QualifyingScope), and which round a race is filed against
    /// automatically (RoundPicker).
    /// </summary>
    public IReadOnlySet<string> DayScope() => ActiveRoundScope() ?? NoRounds;

    private static readonly IReadOnlySet<string> NoRounds = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// The event everything scoped to "this meeting" belongs to - check-ins,
    /// tech, the run sheet.
    ///
    /// Never null, which is the point. Active is allowed to be null because
    /// grouping rounds is optional, but check-in has to be filed against
    /// SOMETHING or it goes back to being one global list that has to be
    /// cleared by hand. Falling back to the legacy event gives every record
    /// a home without forcing anyone to create an event first.
    /// </summary>
    public string CurrentId => _activeEventId ?? LegacyEventId;

    /// <summary>The current event as an object, creating the legacy one if that is what we are on.</summary>
    public RaceEvent Current => Get(CurrentId) ?? EnsureLegacy();

    public void SetActive(string? eventId)
    {
        if (_activeEventId == eventId)
        {
            return;
        }

        _activeEventId = eventId;
        Save();
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Events counting towards a championship, newest first.</summary>
    public IReadOnlyList<RaceEvent> ForChampionship(string championshipId) =>
        _events
            .Where(e => string.Equals(e.ChampionshipId, championshipId, StringComparison.Ordinal))
            .OrderByDescending(e => e.StartedUtc)
            .ToList();

    /// <summary>Everything except the legacy holder - what a picker should offer.</summary>
    public IReadOnlyList<RaceEvent> Real =>
        All.Where(e => !e.IsLegacy).ToList();

    /// <summary>
    /// The legacy event, made if it is not there yet.
    ///
    /// Dated well in the past so it sorts to the bottom of any list, and
    /// left open so rounds that still have nowhere else to go keep landing
    /// in it rather than failing.
    /// </summary>
    public RaceEvent EnsureLegacy()
    {
        var existing = Get(LegacyEventId);

        if (existing is not null)
        {
            return existing;
        }

        var legacy = new RaceEvent
        {
            Id = LegacyEventId,
            Name = "Before events (legacy)",
            Notes = "Everything recorded before race days were tracked as events. "
                  + "Kept so old check-ins, inspections and results still have somewhere to belong.",
            StartedUtc = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IsLegacy = true,
        };

        _events.Add(legacy);
        Save();

        return legacy;
    }

    public void Upsert(RaceEvent raceEvent)
    {
        var index = _events.FindIndex(e => e.Id == raceEvent.Id);
        if (index >= 0)
        {
            _events[index] = raceEvent;
        }
        else
        {
            _events.Add(raceEvent);
        }

        Save();
    }

    public void Delete(string id)
    {
        _events.RemoveAll(e => e.Id == id);

        var wasCurrent = _activeEventId == id;

        if (wasCurrent)
        {
            _activeEventId = null;
        }

        Save();

        if (wasCurrent)
        {
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Adds rounds to the active event, if there is one. No-op otherwise.</summary>
    public void AddRoundsToActive(IEnumerable<string> roundIds)
    {
        var active = Active;
        if (active is null)
        {
            return;
        }

        var added = false;
        foreach (var id in roundIds.Where(id => !active.RoundIds.Contains(id)))
        {
            active.RoundIds.Add(id);
            added = true;
        }

        if (added)
        {
            Save();
        }
    }

    /// <summary>
    /// Puts a round back into a SPECIFIC event - the undo half of
    /// RemoveRound. AddRoundsToActive is no good for restores: the
    /// active event can have changed between the delete and the undo.
    /// </summary>
    public void AttachRound(string eventId, string roundId)
    {
        var raceEvent = Get(eventId);

        if (raceEvent is null || raceEvent.RoundIds.Contains(roundId))
        {
            return;
        }

        raceEvent.RoundIds.Add(roundId);
        Save();
    }

    public void RemoveRound(string roundId)
    {
        var changed = false;
        foreach (var raceEvent in _events.Where(e => e.RoundIds.Remove(roundId)))
        {
            changed = true;
        }

        if (changed)
        {
            Save();
        }
    }

    /// <summary>
    /// Follows a class being renamed, in every event - closed ones included,
    /// so reopening an old event does not bring back a class name that no
    /// longer exists.
    ///
    /// Matched ignoring case because class names are typed by hand and
    /// compared that way everywhere else. If an event already listed the new
    /// name as well, the duplicate is dropped rather than listed twice.
    /// No-op (and no save) when no event listed the old name.
    ///
    /// Returns the ids of the events that listed the old name, for the
    /// undo (see UndoRenameClass).
    ///
    /// Raises nothing: like Upsert, this edits events in place, and
    /// CurrentChanged is only for WHICH event is current changing.
    /// </summary>
    public IReadOnlyList<string> RenameClass(string oldName, string newName)
    {
        var moved = new List<string>();

        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
        {
            return moved;
        }

        foreach (var raceEvent in _events)
        {
            if (raceEvent.ClassNames is null
                || !raceEvent.ClassNames.Any(c => string.Equals(c, oldName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            raceEvent.ClassNames = raceEvent.ClassNames
                .Select(c => string.Equals(c, oldName, StringComparison.OrdinalIgnoreCase) ? newName : c)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            moved.Add(raceEvent.Id);
        }

        if (moved.Count > 0)
        {
            Save();
        }

        return moved;
    }

    /// <summary>
    /// The ids of the events listing this class now. Taken just before a
    /// rename, so its undo can leave alone an event that already listed the
    /// new name.
    /// </summary>
    public IReadOnlyList<string> WithClassName(string className) =>
        _events
            .Where(e => e.ClassNames is not null
                        && e.ClassNames.Any(c => string.Equals(c, className, StringComparison.OrdinalIgnoreCase)))
            .Select(e => e.Id)
            .ToList();

    /// <summary>
    /// The undo half of RenameClass: every event listing
    /// <paramref name="newName"/> NOW lists <paramref name="oldName"/> in its
    /// place - except those in <paramref name="leaveAlone"/>, which already
    /// listed the new name before the rename.
    ///
    /// It used to be done by renaming back, which moved EVERY event listing
    /// the new name to the old one - an event that had listed the new name
    /// all along (left over from a deleted class of that name) lost it to
    /// the undo. Judged by what each event lists now, like the drivers and
    /// rounds: an event whose classes have been changed since keeps the
    /// change, and one that has had the renamed class added since goes back
    /// with it rather than being left with a name that is about to stop
    /// existing.
    ///
    /// An event can hold several class names, and one that listed BOTH names
    /// before the rename had them merged into one by it (<paramref name="moved"/>
    /// says which events the rename changed). Such an event gets the old
    /// name back beside the new one it already had.
    /// </summary>
    public void UndoRenameClass(string newName, string oldName, IReadOnlyCollection<string> leaveAlone, IReadOnlyCollection<string> moved)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        var keep = leaveAlone.ToHashSet(StringComparer.Ordinal);
        var merged = moved.Where(id => keep.Contains(id)).ToHashSet(StringComparer.Ordinal);
        var changed = false;

        foreach (var raceEvent in _events)
        {
            if (raceEvent.ClassNames is null
                || !raceEvent.ClassNames.Any(c => string.Equals(c, newName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (!keep.Contains(raceEvent.Id))
            {
                raceEvent.ClassNames = raceEvent.ClassNames
                    .Select(c => string.Equals(c, newName, StringComparison.OrdinalIgnoreCase) ? oldName : c)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                changed = true;
            }
            else if (merged.Contains(raceEvent.Id)
                     && !raceEvent.ClassNames.Any(c => string.Equals(c, oldName, StringComparison.OrdinalIgnoreCase)))
            {
                raceEvent.ClassNames = raceEvent.ClassNames.Append(oldName).ToList();
                changed = true;
            }
        }

        if (changed)
        {
            Save();
        }
    }

    /// <summary>
    /// Moves each race day's auto-advance pin (AutoAdvanceClassName) from
    /// one class name to another, for a class rename. Returns the ids of the
    /// days whose pin moved.
    ///
    /// Kept apart from RenameClass, which only ever rewrote ClassNames: a
    /// day pinned to a renamed class went on naming the old one, matched no
    /// round, and auto-advance quietly stopped picking races for that day
    /// ("pinned to class ..., which has no unrecorded rounds left"). Only
    /// the club-wide default was being followed (MainForm's Renamed handler).
    /// A day can be pinned to a class it does not list, so the pin has its
    /// own before-and-after rather than riding on ClassNames'.
    /// </summary>
    public IReadOnlyList<string> RenamePinnedClass(string oldName, string newName)
    {
        var moved = new List<string>();

        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
        {
            return moved;
        }

        foreach (var raceEvent in _events)
        {
            if (string.Equals(raceEvent.AutoAdvanceClassName?.Trim(), oldName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                raceEvent.AutoAdvanceClassName = newName;
                moved.Add(raceEvent.Id);
            }
        }

        if (moved.Count > 0)
        {
            Save();
        }

        return moved;
    }

    /// <summary>The ids of the days pinned to this class now - taken just before a rename, for its undo.</summary>
    public IReadOnlyList<string> WithPinnedClass(string className) =>
        _events
            .Where(e => string.Equals(e.AutoAdvanceClassName?.Trim(), className.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Id)
            .ToList();

    /// <summary>
    /// The undo half of RenamePinnedClass: every day pinned to
    /// <paramref name="newName"/> NOW goes back to <paramref name="oldName"/>,
    /// except those in <paramref name="leaveAlone"/>, which were pinned to
    /// the new name before the rename.
    /// </summary>
    public void UndoRenamePinnedClass(string newName, string oldName, IReadOnlyCollection<string> leaveAlone)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        var keep = leaveAlone.ToHashSet(StringComparer.Ordinal);
        var changed = false;

        foreach (var raceEvent in _events)
        {
            if (string.Equals(raceEvent.AutoAdvanceClassName?.Trim(), newName.Trim(), StringComparison.OrdinalIgnoreCase)
                && !keep.Contains(raceEvent.Id))
            {
                raceEvent.AutoAdvanceClassName = oldName;
                changed = true;
            }
        }

        if (changed)
        {
            Save();
        }
    }

    /// <summary>Which event a round belongs to, or null.</summary>
    public RaceEvent? EventForRound(string roundId) =>
        _events.FirstOrDefault(e => e.RoundIds.Contains(roundId));

    /// <summary>
    /// Closes an event off. Its rounds stay linked so results remain
    /// reportable - closing only stops new rounds joining it.
    /// <paramref name="finishedUtc"/> is for undoing a Reopen, which puts
    /// back the time it was really closed rather than stamping it now.
    /// </summary>
    public void Close(string id, DateTime? finishedUtc = null)
    {
        var raceEvent = Get(id);
        if (raceEvent is null)
        {
            return;
        }

        raceEvent.FinishedUtc = finishedUtc ?? DateTime.UtcNow;

        var wasCurrent = _activeEventId == id;

        if (wasCurrent)
        {
            _activeEventId = null;
        }

        Save();

        // Closing the current event drops "current" back to the legacy
        // holder, and everything scoped to the current event has to hear
        // about that the same way it hears about SetActive.
        if (wasCurrent)
        {
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }

        // Only a real close, not an undo of a Reopen putting the old time
        // back - see Closed. (22 Oct 2026, driver experience.)
        if (finishedUtc is null)
        {
            Closed?.Invoke(raceEvent);
        }
    }

    /// <param name="makeCurrentIfNoneIs">
    /// True when reopening should also point the app back at this day,
    /// PROVIDED nothing else is current.
    ///
    /// WHY IT IS NOT UNCONDITIONAL. Closing the current day clears
    /// "current" (see Close), so reopening it left a day that was open and
    /// current-of-nothing: check-ins, scrutineering, the programme and the
    /// timetable all still filed against the legacy holder, the sidebar
    /// still out of race-night order, and not one word on screen to say so.
    /// Pressing Close Day and then Reopen Day did not get you back to where
    /// you started - it got you somewhere that looked like it.
    ///
    /// WHY IT IS NOT ALWAYS EITHER. Reopening an old day from the picker
    /// while tonight is running must not steal the evening, and an undo of
    /// "close this day" must not make a day current that was not current
    /// when it was closed. Both of those pass false and decide for
    /// themselves.
    /// </param>
    public void Reopen(string id, bool makeCurrentIfNoneIs = false)
    {
        var raceEvent = Get(id);

        if (raceEvent is null)
        {
            return;
        }

        raceEvent.FinishedUtc = null;
        Save();

        // SetActive raises CurrentChanged, which is what puts every scoped
        // store - and the sidebar - back onto this day.
        if (makeCurrentIfNoneIs && !raceEvent.IsLegacy && _activeEventId is null)
        {
            SetActive(id);
            return;
        }

        // Reopening the day that is ALREADY current changes no id, so
        // SetActive would do nothing - but "the current day is open again"
        // is exactly the news the sidebar's race-night grouping is waiting
        // for, and anything else asking whether tonight is running. Rare,
        // because closing the current day clears current; reachable from an
        // events.json whose active day was saved closed.
        if (string.Equals(_activeEventId, id, StringComparison.Ordinal))
        {
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Works out which days an event actually ran on, from its rounds'
    /// recorded sessions. Derived rather than stored, so it can't drift out
    /// of step with what was really run.
    /// </summary>
    public List<DateTime> DaysFor(RaceEvent raceEvent, RoundDefinitionStore rounds, SessionHistoryStore history) =>
        raceEvent.RoundIds
            .Select(rounds.Get)
            .Where(r => r?.LinkedSessionId is not null)
            .Select(r => history.Get(r!.LinkedSessionId!))
            .Where(s => s is not null)
            .Select(s => s!.StartedAtUtc.ToLocalTime().Date)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

    /// <summary>
    /// Puts back an event that was deleted - the undo half of Delete. The
    /// event is put back as it was, less any of its rounds that have since
    /// been filed under another event: a round belongs to one event, and
    /// the later filing is the one somebody chose. No-op if an event with
    /// that id is already here.
    /// </summary>
    public void Reinstate(RaceEvent raceEvent)
    {
        if (Get(raceEvent.Id) is not null)
        {
            return;
        }

        raceEvent.RoundIds = raceEvent.RoundIds
            .Where(roundId => EventForRound(roundId) is null)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        _events.Add(raceEvent);
        Save();
    }

    public string Snapshot() =>
        JsonSerializer.Serialize(new EventFile { Events = _events, ActiveEventId = _activeEventId });

    /// <summary>
    /// Puts the events back as a Snapshot recorded them.
    ///
    /// Two things are deliberately NOT taken from the snapshot. Which rounds
    /// an event holds is kept as it is now: rounds generated after the
    /// snapshot was taken are real races, and a restore that dropped them
    /// out of their event quietly changed that event's qualifying. And
    /// anything scoped to the current event - check-in, tech, the run sheet
    /// - is told when the current event changes, exactly as SetActive tells
    /// it; setting the id without saying so left those pages showing the
    /// event that was current before the restore.
    /// </summary>
    public void Restore(string snapshot)
    {
        var currentChanged = false;

        try
        {
            var file = JsonSerializer.Deserialize<EventFile>(snapshot);
            if (file is null)
            {
                return;
            }

            var restored = file.Events ?? new List<RaceEvent>();

            foreach (var raceEvent in restored)
            {
                if (Get(raceEvent.Id) is { } live)
                {
                    raceEvent.RoundIds = live.RoundIds;
                }
            }

            // An event only in the snapshot (deleted since) keeps its rounds,
            // less any that have been filed under another event since.
            var claimed = new HashSet<string>(
                restored.Where(e => Get(e.Id) is not null).SelectMany(e => e.RoundIds),
                StringComparer.Ordinal);

            foreach (var raceEvent in restored.Where(e => Get(e.Id) is null))
            {
                raceEvent.RoundIds = raceEvent.RoundIds.Where(id => !claimed.Contains(id)).ToList();
            }

            var previousCurrentId = CurrentId;

            _events.Clear();
            _events.AddRange(restored);
            _activeEventId = file.ActiveEventId;
            Save();

            currentChanged = CurrentId != previousCurrentId;
        }
        catch
        {
            // A failed restore leaves the current state alone.
        }

        // Outside the try, so a listener's failure isn't mistaken for a
        // failed restore and swallowed.
        if (currentChanged)
        {
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
