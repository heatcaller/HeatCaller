using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Loads/saves DriverProfile records to %AppData%\HeatCaller\profiles.json,
/// keyed by transponder id. Separate from AppSettings since this is racer
/// data that should persist across seasons, not just connection settings.
/// </summary>
public sealed class ProfileStore
{
    private readonly Dictionary<string, DriverProfile> _profiles;

    /// <summary>
    /// Alternate transponder -> the primary transponder of the driver who
    /// owns it. Case-insensitive, like the primary lookup, for the same
    /// reason: the decoder reports uppercase hex and people type either.
    ///
    /// Rebuilt as a whole new dictionary and swapped in by reference on
    /// every change rather than edited in place, so a lookup on another
    /// thread (the announcer's) never sees one half-built.
    /// </summary>
    private volatile Dictionary<string, string> _alternateOwners = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Main transponder -> the cars that driver had as of the last time the
    /// store was saved (or loaded). What CarsAdded compares a save with.
    ///
    /// It used to compare with the profile object being replaced. But
    /// several places change the stored profile itself and then save it
    /// (the CSV import merges a sheet's columns into it), so "before" and
    /// "after" were the very same object, there was nothing to compare,
    /// and a car the import gave an existing driver never reached the
    /// Garage. A copy of the names taken at every save does not move when
    /// somebody edits the profile in place.
    ///
    /// Rebuilt whole and swapped in by reference, like _alternateOwners.
    /// </summary>
    private volatile Dictionary<string, string[]> _savedCars = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Which driver a profile object is a version of - see PutBack.
    ///
    /// Held beside the objects rather than on DriverProfile, so it is never
    /// written to profiles.json: it only has to last as long as the undo
    /// list does, which is this session. Weak, so a version nothing else
    /// holds any more is let go with its entry.
    /// </summary>
    private static readonly ConditionalWeakTable<DriverProfile, Lineage> Lineages = new();

    /// <summary>One driver's identity, shared by every version of their profile. Compared by reference only.</summary>
    private sealed class Lineage
    {
    }

    /// <summary>The driver this object is a version of - a new one the first time an object is asked about.</summary>
    private static Lineage LineageOf(DriverProfile profile) =>
        Lineages.GetValue(profile, _ => new Lineage());

    /// <summary>
    /// Marks <paramref name="profile"/> as a later version of the driver
    /// <paramref name="before"/> was - for a save that replaces the stored
    /// object with a new one (the edit form builds a fresh profile).
    /// </summary>
    private static void ContinueLineage(DriverProfile profile, DriverProfile? before)
    {
        if (before is not null && !ReferenceEquals(before, profile))
        {
            Lineages.AddOrUpdate(profile, LineageOf(before));
        }
    }

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "profiles.json");

    private ProfileStore(Dictionary<string, DriverProfile> profiles)
    {
        _profiles = profiles;
        RebuildAlternateIndex();
        RememberSavedCars();
    }

    public static ProfileStore Load()
    {
        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var json = SafeFile.ReadAllTextWithRetry(FilePath);
                var list = JsonSerializer.Deserialize<List<DriverProfile>>(json);
                if (list is not null)
                {
                    // Case-insensitive: the direct EasyLap decoder always
                    // reports transponder IDs as uppercase hex (e.g.
                    // "8467BE"), but a profile could reasonably have been
                    // typed in as lowercase - hex digits are naturally
                    // ambiguous in case to type by hand. Without this, a
                    // profile that looked correctly entered would silently
                    // fail to match, and announcements would fall back to
                    // reading out the raw transponder ID instead of the
                    // configured driver name. Built with a plain loop
                    // (last one wins) rather than ToDictionary, so an old
                    // save that happens to have two entries differing only
                    // by case can't throw a duplicate-key exception here.
                    var profiles = new Dictionary<string, DriverProfile>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in list)
                    {
                        // A null entry or a profile with no transponder
                        // can't be keyed, and would otherwise throw here and
                        // lose every other driver in the file with it.
                        if (p is null || string.IsNullOrWhiteSpace(p.Transponder))
                        {
                            continue;
                        }

                        FillMissingFields(p);
                        profiles[p.Transponder] = p;
                    }
                    return new ProfileStore(profiles);
                }
            }
        }
        catch (Exception ex)
        {
            // Corrupt/unreadable file: start fresh rather than crashing on launch.
            unreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
        }

        return new ProfileStore(new Dictionary<string, DriverProfile>(StringComparer.OrdinalIgnoreCase)) { LoadedUnreadable = unreadable };
    }

    /// <summary>
    /// Puts back the defaults a hand-edited, restored or externally written
    /// profiles.json can have nulled out, e.g. "Cars": null or
    /// "DriverName": null.
    ///
    /// The serializer honors an explicit null by calling the setter, so the
    /// "= new()" and "= string.Empty" initializers in DriverProfile do not
    /// protect against it. Every reader in the app takes these as never
    /// null - AllCars calls Cars.Where, which the constructor reaches
    /// through RememberSavedCars - so one null used to throw inside Load's
    /// try. That counted as an unreadable file: the store started empty
    /// with writes allowed, and the next save replaced the whole roster
    /// with whoever was added afterwards. Repairing the one entry here
    /// means a bad field costs nothing instead of costing every driver in
    /// the file, which is what the per-entry loop above is there for.
    /// </summary>
    private static void FillMissingFields(DriverProfile profile)
    {
        // Null entries inside the list are dropped too: AllCars skips them,
        // but other code walks Cars directly and trims each name.
        profile.Cars = (profile.Cars ?? new List<string>())
            .Where(car => car is not null)
            .ToList();

        // The same for the other classes: a file written before the list
        // existed has none, which is an empty list, not a null.
        profile.Classes = (profile.Classes ?? new List<string>())
            .Where(c => c is not null)
            .ToList();

        profile.DriverName ??= string.Empty;
        profile.CarModel ??= string.Empty;
        profile.ClassName ??= string.Empty;
        profile.SetupConfiguration ??= string.Empty;
        profile.Notes ??= string.Empty;
        profile.AnnouncerName ??= string.Empty;
        profile.RaceNumber ??= string.Empty;
        profile.HomeTrack ??= string.Empty;
        profile.SkillLevel ??= string.Empty;
        profile.Contact ??= string.Empty;
        profile.Email ??= string.Empty;
        // Entries with no id are dropped, ids trimmed, and each listed once
        // (the first entry for an id wins) - the lookups below assume it.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        profile.Transponders = (profile.Transponders ?? new List<OwnedTransponder>())
            .Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Id))
            .Select(t =>
            {
                t.Id = t.Id.Trim();
                t.CarId ??= string.Empty;
                return t;
            })
            .Where(t => seen.Add(t.Id))
            .ToList();
        profile.PhotoFileName ??= string.Empty;
        profile.Sponsors ??= string.Empty;
        profile.Team ??= string.Empty;
    }

    /// <summary>
    /// True when the file was there at startup but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. What this store holds is then a
    /// guess, and nothing it does this session is saved over the club's
    /// real file.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    public void Save()
    {
        // Before the check below: even a store that is not writing its file
        // has to know what it last "saved", or every later save would look
        // like it added every car again.
        RememberSavedCars();

        if (LoadedUnreadable)
        {
            // See LoadedUnreadable: the club's real file stays as it is.
            return;
        }

        var json = JsonSerializer.Serialize(_profiles.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });
        SafeFile.WriteAllText(FilePath, json);
    }

    /// <summary>
    /// The driver a transponder belongs to - by their main transponder
    /// first, then by their alternate one.
    ///
    /// The alternate used to be stored and shown but never looked up, so a
    /// driver running their second car was announced, listed and saved as
    /// a stranger. Resolving it HERE means every one of the many places
    /// that turns a transponder into a name - announcements, leaderboard,
    /// results, practice, the Wi-Fi pages - gets it without being touched.
    ///
    /// A main transponder always wins: if one profile's alternate is also
    /// somebody else's main transponder, the id belongs to that somebody.
    /// </summary>
    public DriverProfile? Get(string transponder)
    {
        if (_profiles.TryGetValue(transponder, out var profile))
        {
            return profile;
        }

        return PrimaryForAlternate(transponder) is { } primary
               && _profiles.TryGetValue(primary, out var owner)
            ? owner
            : null;
    }

    /// <summary>
    /// The car a row on the race board is in tonight, when the Garage knows
    /// better than the driver's active car: the car the transponder heard is
    /// fitted to. Given by MainForm, which has the race and the garage; null
    /// (or answering null) leaves every row on the profile's CarModel.
    /// </summary>
    public Func<string, string?>? RaceCarName { get; set; }

    /// <summary>
    /// What car to announce and record for a row on the race board - the
    /// row's transponder: the car the transponder heard is fitted to, and
    /// otherwise the driver's active car (CarModel). Empty for nobody.
    /// </summary>
    public string CarModelFor(string rowTransponder)
    {
        if (string.IsNullOrWhiteSpace(rowTransponder))
        {
            return string.Empty;
        }

        if (RaceCarName?.Invoke(rowTransponder) is { Length: > 0 } fitted)
        {
            return fitted;
        }

        return Get(rowTransponder)?.CarModel ?? string.Empty;
    }

    /// <summary>
    /// When this id is a driver's ALTERNATE transponder (and nobody's main
    /// one), that driver's main transponder; otherwise null.
    ///
    /// For the lap pipeline, which must file a lap from the second car in
    /// the same row as the first - so the driver has one set of laps, one
    /// best and one position, not two.
    /// </summary>
    public string? PrimaryForAlternate(string transponder)
    {
        if (string.IsNullOrWhiteSpace(transponder))
        {
            return null;
        }

        var id = transponder.Trim();

        if (_profiles.ContainsKey(id))
        {
            return null;
        }

        return _alternateOwners.TryGetValue(id, out var primary) ? primary : null;
    }

    /// <summary>
    /// Why this profile can't be saved alongside the others, or null when it
    /// can.
    ///
    /// Profiles are keyed by transponder, so saving a NEW driver on a
    /// transponder somebody already has used to replace that somebody
    /// outright - name, photo, history links and all - without a word. The
    /// same goes for an alternate transponder that is already another
    /// driver's: the lap would have two owners.
    ///
    /// <paramref name="editing"/> is the main transponder of the profile
    /// being edited (it is allowed to clash with itself), or null when the
    /// profile is new.
    /// </summary>
    public string? DescribeClash(DriverProfile candidate, string? editing)
    {
        var main = candidate.Transponder?.Trim() ?? string.Empty;

        // An extra entry repeating the main transponder is left out by
        // ExtraTransponders, so it can never clash with the driver itself.
        var mine = candidate.AllTransponderIds;

        foreach (var other in _profiles.Values)
        {
            if (editing is not null && string.Equals(other.Transponder, editing, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var otherMain = other.Transponder?.Trim() ?? string.Empty;
            var theirs = other.AllTransponderIds;
            var who = DescribeOwner(other);

            foreach (var id in mine)
            {
                if (!theirs.Contains(id, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var isMyMain = string.Equals(id, main, StringComparison.OrdinalIgnoreCase);
                var isTheirMain = string.Equals(id, otherMain, StringComparison.OrdinalIgnoreCase);

                return (isMyMain, isTheirMain) switch
                {
                    (true, true) => $"Transponder {id} already belongs to {who}. Edit {who}'s profile instead, or change or delete it first.",
                    (true, false) => $"Transponder {id} is already one of {who}'s transponders. Remove it from {who}'s profile first.",
                    (false, true) => $"Transponder {id} already belongs to {who}. A transponder can only belong to one driver.",
                    _ => $"Transponder {id} is already one of {who}'s transponders. Remove it from {who}'s profile first.",
                };
            }
        }

        return null;
    }

    /// <summary>
    /// The driver who has this transponder - as their main one or one of
    /// their others, retired or not - or null when nobody does. Unlike Get,
    /// it also finds an id the timing index leaves out because two drivers
    /// list it, so a new claim on it can be refused.
    /// </summary>
    public DriverProfile? OwnerOf(string transponder)
    {
        var id = transponder?.Trim() ?? string.Empty;

        if (id.Length == 0)
        {
            return null;
        }

        if (_profiles.TryGetValue(id, out var main))
        {
            return main;
        }

        return _profiles.Values.FirstOrDefault(p => p.AllTransponderIds.Contains(id, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The driver with this SPLIT-loop transponder, and its entry - or null.
    /// Only split transponders are looked at: a line transponder with the
    /// same number is a different transponder (see
    /// DriverProfile.SplitTransponders).
    ///
    /// Null too when MORE than one driver claims it - which the editors
    /// refuse, but an undo or a transfer could once let through. The first
    /// claimant used to win, silently: the other driver's split times went
    /// into the wrong row, a sector record was kept under the wrong name and
    /// "read at the split twice without crossing the line" invited a lap to
    /// be added that was never run. Unowned, the read shows as an unknown
    /// transponder at the split - visible, and fixed on a Transponders tab -
    /// as a contested line alternate does (RebuildAlternateIndex).
    /// </summary>
    public (DriverProfile Driver, OwnedTransponder Entry)? SplitOwnerOf(string transponder)
    {
        var owners = SplitOwnersOf(transponder);
        return owners.Count == 1 ? owners[0] : null;
    }

    /// <summary>Every driver claiming this split transponder - more than one only when something has gone wrong.</summary>
    public IReadOnlyList<(DriverProfile Driver, OwnedTransponder Entry)> SplitOwnersOf(string transponder)
    {
        var id = transponder?.Trim() ?? string.Empty;
        var owners = new List<(DriverProfile Driver, OwnedTransponder Entry)>();

        if (id.Length == 0)
        {
            return owners;
        }

        foreach (var profile in _profiles.Values)
        {
            if (profile.SplitEntry(id) is { } entry)
            {
                owners.Add((profile, entry));
            }
        }

        return owners;
    }

    /// <summary>
    /// Why this driver's split transponders can't be saved - one of them is
    /// already another driver's split transponder - or null when they can.
    /// <paramref name="editing"/> is the driver's main transponder.
    /// </summary>
    public string? DescribeSplitClash(DriverProfile candidate, string? editing)
    {
        foreach (var entry in candidate.SplitTransponders ?? new List<OwnedTransponder>())
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Id))
            {
                continue;
            }

            foreach (var owner in SplitOwnersOf(entry.Id))
            {
                if (!string.Equals(owner.Driver.Transponder, editing, StringComparison.OrdinalIgnoreCase))
                {
                    return $"Split transponder {entry.Id.Trim()} is already {DescribeOwner(owner.Driver)}'s. Remove it from {DescribeOwner(owner.Driver)} first.";
                }
            }
        }

        return null;
    }

    /// <summary>The name to put in a clash message - a driver name if there is one, otherwise the transponder.</summary>
    private static string DescribeOwner(DriverProfile profile) =>
        string.IsNullOrWhiteSpace(profile.DriverName) ? $"the profile for {profile.Transponder}" : profile.DriverName.Trim();

    /// <summary>
    /// Every REAL driver. Ghost cars are excluded.
    ///
    /// Ghosts are stored as profiles so that announcements, the
    /// leaderboard and the big screen name them without special-casing -
    /// but that meant they also appeared in check-in, tech inspection,
    /// race rosters, handicaps and every other driver list, which is
    /// wrong everywhere. Filtering here fixes all thirteen of those
    /// places at once rather than patching each one.
    /// </summary>
    public IReadOnlyList<DriverProfile> All => _profiles.Values
        .Where(p => !GhostService.IsGhost(p.Transponder))
        .OrderBy(p => p.DriverName)
        .ThenBy(p => p.Transponder)
        .ToList();

    /// <summary>
    /// Everyone who can be entered into a session - real drivers AND ghost
    /// cars.
    ///
    /// The distinction that matters is PHYSICAL vs RACEABLE, not real vs
    /// virtual. Check-in, tech inspection, battery packs and the setup log
    /// are about a car that physically exists, so a ghost there is
    /// nonsense. Rosters, demo mode, rivalries and handicaps are about who
    /// is in the race - and a ghost is exactly as valid an entrant as
    /// anyone else.
    /// </summary>
    public IReadOnlyList<DriverProfile> AllRaceable => _profiles.Values
        .OrderBy(p => GhostService.IsGhost(p.Transponder))   // real drivers first
        .ThenBy(p => p.DriverName)
        .ThenBy(p => p.Transponder)
        .ToList();

    /// <summary>Every profile including ghosts, unsorted by kind.</summary>
    public IReadOnlyList<DriverProfile> AllIncludingGhosts => _profiles.Values
        .OrderBy(p => p.DriverName)
        .ThenBy(p => p.Transponder)
        .ToList();

    /// <summary>
    /// Adds a NEW driver, refusing - rather than replacing anybody - when
    /// either of its transponders is already someone else's.
    ///
    /// Use this, not Upsert, wherever a profile is being created: Upsert
    /// replaces whatever is stored under the same transponder, which is
    /// right for an edit and exactly wrong for an add.
    /// </summary>
    public bool TryAdd(DriverProfile profile, out string refusal)
    {
        refusal = DescribeClash(profile, editing: null) ?? string.Empty;

        if (refusal.Length > 0)
        {
            return false;
        }

        StampFirstSeen(profile);
        _profiles[profile.Transponder] = profile;
        RebuildAlternateIndex();
        Save();

        // A new driver: every car on them is new.
        RaiseCarsAdded(profile, Array.Empty<string>());
        return true;
    }

    /// <summary>
    /// Records the day a driver joined, the first time the store sees their
    /// transponder - so the profile card can say when they became active
    /// whichever way they were added: typed in, imported from a sheet, set
    /// up by the first-run wizard, or signed up through the relay.
    ///
    /// Only on a transponder the store does not already hold. A profile
    /// already on file keeps the date it has, and one saved before this was
    /// recorded keeps none rather than being back-dated to today - telling a
    /// five-year regular they joined this morning would be worse than saying
    /// nothing. For those the card falls back to the first day they were
    /// timed, which is a real date.
    /// </summary>
    private void StampFirstSeen(DriverProfile profile)
    {
        if (profile.FirstSeenUtc is null
            && !string.IsNullOrWhiteSpace(profile.Transponder)
            && !_profiles.ContainsKey(profile.Transponder))
        {
            profile.FirstSeenUtc = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Saves a profile over the one stored under the same main transponder.
    /// That is an EDIT of that driver - to create a driver, use TryAdd.
    ///
    /// It can never replace a different driver: the only record it writes
    /// over is the one with this exact transponder. An alternate that
    /// clashes with another driver is still stored as typed, but is then
    /// ignored for timing (see RebuildAlternateIndex) rather than handing
    /// laps to the wrong person.
    /// </summary>
    public void Upsert(DriverProfile profile)
    {
        // Read before the save below replaces it - the cars this driver had
        // as last saved (see _savedCars).
        var had = CarsAsSaved(profile.Transponder);

        _profiles.TryGetValue(profile.Transponder, out var before);
        ContinueLineage(profile, before);

        StampFirstSeen(profile);
        _profiles[profile.Transponder] = profile;
        RebuildAlternateIndex();
        Save();

        RaiseCarsAdded(profile, had);
    }

    /// <summary>Adds or replaces several profiles, writing the file once rather than once per profile.</summary>
    public void UpsertMany(IEnumerable<DriverProfile> profiles)
    {
        var saved = new List<(DriverProfile Profile, IReadOnlyList<string> Had)>();

        foreach (var profile in profiles)
        {
            _profiles.TryGetValue(profile.Transponder, out var before);
            ContinueLineage(profile, before);

            StampFirstSeen(profile);
            _profiles[profile.Transponder] = profile;
            saved.Add((profile, CarsAsSaved(profile.Transponder)));
        }

        RebuildAlternateIndex();
        Save();

        foreach (var (profile, had) in saved)
        {
            RaiseCarsAdded(profile, had);
        }
    }

    /// <summary>
    /// Raised when a driver is created, or saved with a car name they did
    /// not have before - carrying ONLY the new names.
    /// </summary>
    /// <remarks>
    /// The Garage listens, and adds a car for each (see GarageSync). It is
    /// handed the new names rather than the whole list on purpose: a car
    /// deleted from the Garage by hand is still named on the profile, and
    /// passing every name on every save would bring it back the next time
    /// anybody changed that driver's race number.
    ///
    /// May be raised off the UI thread - drivers are also added by the
    /// phone's race-day page and by relay signups.
    /// </remarks>
    public event Action<DriverProfile, IReadOnlyList<string>>? CarsAdded;

    /// <summary>
    /// <paramref name="had"/> is the cars this driver had as last saved
    /// (see _savedCars) - not read off the profile object being replaced,
    /// which may be this very object, changed in place before the save.
    /// </summary>
    private void RaiseCarsAdded(DriverProfile profile, IReadOnlyList<string> had)
    {
        if (CarsAdded is null)
        {
            return;
        }

        var added = profile.AllCars
            .Where(car => !had.Any(old => string.Equals(old, car, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (added.Count > 0)
        {
            CarsAdded?.Invoke(profile, added);
        }
    }

    /// <summary>The cars the driver on this main transponder had when the store was last saved, or none.</summary>
    private IReadOnlyList<string> CarsAsSaved(string transponder) =>
        _savedCars.TryGetValue(transponder, out var cars) ? cars : Array.Empty<string>();

    /// <summary>Takes the copy of every driver's cars that CarsAdded compares the next save with.</summary>
    private void RememberSavedCars()
    {
        var cars = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var (transponder, profile) in _profiles)
        {
            cars[transponder] = profile.AllCars.ToArray();
        }

        _savedCars = cars;
    }

    public void Delete(string transponder)
    {
        _profiles.Remove(transponder);
        RebuildAlternateIndex();
        Save();
    }

    /// <summary>
    /// Puts deleted profiles back - the undo half of Delete - without ever
    /// writing over, or sharing a transponder with, a driver who holds one
    /// of their transponders now.
    ///
    /// The undo used to put them back with Upsert, which replaces whatever
    /// is stored under the same main transponder: a driver added on that
    /// transponder after the delete was wiped out by the undo - name, photo
    /// and all. And a driver who had since taken one of the transponders as
    /// their alt transponder, or been given the deleted driver's alt one as
    /// their main, ended up sharing it with the profile brought back: one
    /// car's laps with two owners, which a transponder belonging to one
    /// driver only is meant to rule out.
    ///
    /// So a profile only comes back when neither of its transponders is
    /// anybody else's now, as a main or an alt transponder. It is judged
    /// against the profiles as they are before any of these goes back: the
    /// deleted ones were all saved together before the delete, so nothing
    /// between two of them is new. The ones that can come back are put back
    /// together, with one save; the ones that cannot are returned, with why,
    /// and nothing of theirs is written. A profile that is somehow back
    /// already - the very same record - is left as it is.
    /// </summary>
    public IReadOnlyList<(DriverProfile Profile, string Reason)> RestoreDeleted(IReadOnlyCollection<DriverProfile> deleted)
    {
        var restoringTogether = new HashSet<DriverProfile>(deleted, ReferenceEqualityComparer.Instance);
        var refused = new List<(DriverProfile Profile, string Reason)>();
        var restoring = new List<DriverProfile>();

        foreach (var profile in deleted)
        {
            if (_profiles.TryGetValue(profile.Transponder, out var present) && ReferenceEquals(present, profile))
            {
                continue;
            }

            // Split transponders as well as line ones: a split transponder
            // given to somebody else since the delete would otherwise come
            // back with two drivers claiming it (see SplitOwnerOf).
            if ((DescribeTakenSince(profile, restoringTogether)
                 ?? DescribeSplitClash(profile, editing: profile.Transponder)) is { } reason)
            {
                refused.Add((profile, reason));
            }
            else
            {
                restoring.Add(profile);
            }
        }

        if (restoring.Count > 0)
        {
            foreach (var profile in restoring)
            {
                _profiles[profile.Transponder] = profile;
            }

            RebuildAlternateIndex();
            Save();
        }

        return refused;
    }

    /// <summary>
    /// A detached copy of a profile, for an undo to put back later. Taken
    /// through the same JSON the file uses, so every field comes along, and
    /// nothing done to the live profile afterwards - several places edit
    /// one in place (a CSV import, a quick photo change, a class rename) -
    /// reaches the copy.
    ///
    /// The copy is marked as a version of the same driver as the original,
    /// which is how PutBack tells that driver apart from somebody given the
    /// same transponder later.
    /// </summary>
    public static DriverProfile Snapshot(DriverProfile profile)
    {
        var copy = JsonSerializer.Deserialize<DriverProfile>(JsonSerializer.Serialize(profile))!;
        Lineages.AddOrUpdate(copy, LineageOf(profile));
        return copy;
    }

    /// <summary>
    /// The undo half of an edit or an import, in one save: each profile in
    /// <paramref name="before"/> goes back over the one stored under its
    /// main transponder, and each transponder in <paramref name="added"/>
    /// is removed.
    ///
    /// Only a record that is still there is touched. A driver deleted since
    /// stays deleted - that delete has an undo of its own - rather than
    /// being brought back half-way by this one. Returns how many profiles
    /// were changed.
    ///
    /// "Still there" means the same DRIVER, not just something stored under
    /// the same transponder. This used to check only the transponder, so
    /// after edit Sam, delete Sam, add Alex on Sam's transponder, the edit's
    /// undo wrote Sam's old details over Alex - name, photo and all - with
    /// no undo to bring him back. Each copy in <paramref name="before"/>
    /// came from Snapshot, which marks it as a version of the driver it was
    /// taken from; it only goes back over a later version of that same
    /// driver (the record the edit saved, or one changed in place since, or
    /// one put back by an undo). Anybody else on that transponder is left
    /// alone, as if the driver had simply been deleted - which they were.
    ///
    /// <paramref name="added"/> is only transponders, so it cannot tell a
    /// driver the import added from one added by hand on the same
    /// transponder after that driver was deleted. Use the overload that
    /// takes the added profiles themselves wherever they are known.
    /// </summary>
    public int PutBack(IReadOnlyCollection<DriverProfile> before, IReadOnlyCollection<string> added) =>
        PutBack(before, added.Select(transponder => (transponder, (DriverProfile?)null)).ToList());

    /// <summary>
    /// PutBack, with the drivers the change added given as the profiles it
    /// stored rather than as transponders: each is removed only while the
    /// record under its transponder is still that driver (see the other
    /// overload). A driver added by hand on the same transponder after the
    /// imported one was deleted stays, rather than being deleted by an
    /// undo that was never about them.
    /// </summary>
    public int PutBack(IReadOnlyCollection<DriverProfile> before, IReadOnlyCollection<DriverProfile> added) =>
        PutBack(before, added.Select(profile => (profile.Transponder, (DriverProfile?)profile)).ToList());

    private int PutBack(IReadOnlyCollection<DriverProfile> before, IReadOnlyList<(string Transponder, DriverProfile? Profile)> added)
    {
        var changed = 0;

        foreach (var profile in before)
        {
            if (_profiles.TryGetValue(profile.Transponder, out var present) && IsSameDriver(present, profile))
            {
                _profiles[profile.Transponder] = profile;
                changed++;
            }
        }

        foreach (var (transponder, profile) in added)
        {
            if (_profiles.TryGetValue(transponder, out var present)
                && (profile is null || IsSameDriver(present, profile))
                && _profiles.Remove(transponder))
            {
                changed++;
            }
        }

        if (changed > 0)
        {
            RebuildAlternateIndex();
            Save();
        }

        return changed;
    }

    /// <summary>
    /// Whether two profile objects are versions of one driver - see PutBack.
    /// </summary>
    private static bool IsSameDriver(DriverProfile a, DriverProfile b) =>
        ReferenceEquals(a, b) || ReferenceEquals(LineageOf(a), LineageOf(b));

    /// <summary>
    /// Which of a deleted profile's transponders somebody else holds now,
    /// and who - or null when neither is anybody else's. Profiles in
    /// <paramref name="ignore"/> (the ones being put back with it) don't
    /// count.
    /// </summary>
    private string? DescribeTakenSince(DriverProfile profile, HashSet<DriverProfile> ignore)
    {
        var main = profile.Transponder?.Trim() ?? string.Empty;
        var mine = profile.AllTransponderIds;

        foreach (var other in _profiles.Values)
        {
            if (ignore.Contains(other))
            {
                continue;
            }

            var otherMain = other.Transponder?.Trim() ?? string.Empty;
            var theirs = other.AllTransponderIds;
            var who = DescribeOwner(other);

            foreach (var id in mine)
            {
                if (!theirs.Contains(id, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var isMyMain = string.Equals(id, main, StringComparison.OrdinalIgnoreCase);
                var isTheirMain = string.Equals(id, otherMain, StringComparison.OrdinalIgnoreCase);

                return isTheirMain
                    ? $"{(isMyMain ? "transponder" : "the other transponder")} {id} belongs to {who} now"
                    : $"{(isMyMain ? "transponder" : "the other transponder")} {id} is one of {who}'s transponders now";
            }
        }

        return null;
    }

    /// <summary>
    /// Moves every driver from one class name to another, for a class
    /// rename. A driver's class is held as its name, so renaming the class
    /// alone left every one of its drivers labelled with a class that no
    /// longer existed - out of its rosters, heats and standings. Ghosts
    /// are included: they race in a class like anyone else.
    ///
    /// Returns the transponders of the drivers that moved, so an undo can
    /// move back exactly those and not a driver who already carried the new
    /// name (left over from a deleted class, say).
    /// </summary>
    public IReadOnlyList<string> RenameClass(string oldName, string newName)
    {
        var moved = new List<string>();

        foreach (var profile in _profiles.Values)
        {
            var hit = false;

            if (string.Equals(profile.ClassName?.Trim(), oldName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                profile.ClassName = newName;
                hit = true;
            }

            // Their other classes too - a driver entered for the renamed class
            // as a second class is in it just as much. A list that already
            // had the new name as well is merged into one entry.
            if (profile.Classes is { Count: > 0 } others
                && others.Any(c => string.Equals(c.Trim(), oldName.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                profile.Classes = others
                    .Select(c => string.Equals(c.Trim(), oldName.Trim(), StringComparison.OrdinalIgnoreCase) ? newName : c)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                hit = true;
            }

            if (hit)
            {
                moved.Add(profile.Transponder);
            }
        }

        if (moved.Count > 0)
        {
            Save();
        }

        return moved;
    }

    /// <summary>
    /// The transponders of the drivers carrying this class name now. Taken
    /// just before a rename, so its undo can tell a driver who was already
    /// under the new name (left over from a deleted class, say) from one
    /// who arrived there with the rename or after it.
    /// </summary>
    public IReadOnlyList<string> WithClassName(string className) =>
        _profiles.Values
            .Where(p => p.RunsClass(className))
            .Select(p => p.Transponder)
            .ToList();

    /// <summary>
    /// The undo half of RenameClass: every driver carrying
    /// <paramref name="newName"/> NOW goes back to <paramref name="oldName"/>,
    /// except those in <paramref name="leaveAlone"/>, who already carried
    /// the new name before the rename.
    ///
    /// Judged by what each driver holds now rather than by the list the
    /// rename moved. A driver moved to another class since the rename is
    /// not dragged back, and a driver added to the renamed class since is
    /// not stranded under a name that is about to stop existing.
    /// </summary>
    public void UndoRenameClass(string newName, string oldName, IReadOnlyCollection<string> leaveAlone)
    {
        var keep = leaveAlone.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var profile in _profiles.Values)
        {
            if (keep.Contains(profile.Transponder))
            {
                continue;
            }

            if (string.Equals(profile.ClassName?.Trim(), newName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                profile.ClassName = oldName;
                changed = true;
            }

            if (profile.Classes is { Count: > 0 } others
                && others.Any(c => string.Equals(c.Trim(), newName.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                profile.Classes = others
                    .Select(c => string.Equals(c.Trim(), newName.Trim(), StringComparison.OrdinalIgnoreCase) ? oldName : c)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                changed = true;
            }
        }

        if (changed)
        {
            Save();
        }
    }

    /// <summary>
    /// Rebuilds the alternate-transponder index from the profiles.
    ///
    /// An alternate is left OUT when it can't be trusted to mean one
    /// driver: when it is also somebody's main transponder (the main one
    /// wins, and Get checks that first anyway), when it is the profile's
    /// own main transponder, or when two profiles both list it - older
    /// files, saved before clashes were refused, can hold that. A lap from
    /// such a transponder then arrives as an unknown car, which is
    /// visible and fixable, instead of silently going to whichever of the
    /// two drivers happened to be read last.
    /// </summary>
    private void RebuildAlternateIndex()
    {
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in _profiles.Values)
        {
            // Every transponder the driver has besides the main one, retired
            // ones included: a retired transponder is still theirs, and laps
            // from one that turns up again are still theirs too.
            foreach (var entry in profile.ExtraTransponders)
            {
                var alternate = entry.Id.Trim();

                if (_profiles.ContainsKey(alternate))
                {
                    continue;
                }

                if (owners.TryGetValue(alternate, out var already))
                {
                    if (!string.Equals(already, profile.Transponder, StringComparison.OrdinalIgnoreCase))
                    {
                        ambiguous.Add(alternate);
                    }

                    continue;
                }

                owners[alternate] = profile.Transponder;
            }
        }

        foreach (var alternate in ambiguous)
        {
            owners.Remove(alternate);
        }

        _alternateOwners = owners;
    }
}
