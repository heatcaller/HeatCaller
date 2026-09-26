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

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "profiles.json");

    private ProfileStore(Dictionary<string, DriverProfile> profiles)
    {
        _profiles = profiles;
        RebuildAlternateIndex();
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
    /// True when the file was there at startup but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. What this store holds is then a
    /// guess, and nothing it does this session is saved over the club's
    /// real file.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    public void Save()
    {
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
        var primary = candidate.Transponder?.Trim() ?? string.Empty;
        var alternate = candidate.AlternateTransponder?.Trim() ?? string.Empty;

        if (alternate.Length > 0 && string.Equals(alternate, primary, StringComparison.OrdinalIgnoreCase))
        {
            return $"The alt transponder {alternate} is the same as the main one. Leave Alt transponder blank, or enter the second car's transponder.";
        }

        foreach (var other in _profiles.Values)
        {
            if (editing is not null && string.Equals(other.Transponder, editing, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var otherAlternate = other.AlternateTransponder?.Trim() ?? string.Empty;
            var who = DescribeOwner(other);

            if (string.Equals(other.Transponder, primary, StringComparison.OrdinalIgnoreCase))
            {
                return $"Transponder {primary} already belongs to {who}. Edit {who}'s profile instead, or change or delete it first.";
            }

            if (otherAlternate.Length > 0 && string.Equals(otherAlternate, primary, StringComparison.OrdinalIgnoreCase))
            {
                return $"Transponder {primary} is already {who}'s alt transponder. Remove it from {who}'s profile first.";
            }

            if (alternate.Length > 0 && string.Equals(other.Transponder, alternate, StringComparison.OrdinalIgnoreCase))
            {
                return $"The alt transponder {alternate} already belongs to {who}. A transponder can only belong to one driver.";
            }

            if (alternate.Length > 0 && string.Equals(otherAlternate, alternate, StringComparison.OrdinalIgnoreCase))
            {
                return $"The alt transponder {alternate} is already {who}'s alt transponder. Remove it from {who}'s profile first.";
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
        RaiseCarsAdded(profile, before: null);
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
        // Read before it is replaced - it is the only record of which cars
        // this driver already had.
        _profiles.TryGetValue(profile.Transponder, out var before);

        StampFirstSeen(profile);
        _profiles[profile.Transponder] = profile;
        RebuildAlternateIndex();
        Save();

        RaiseCarsAdded(profile, before);
    }

    /// <summary>Adds or replaces several profiles, writing the file once rather than once per profile.</summary>
    public void UpsertMany(IEnumerable<DriverProfile> profiles)
    {
        var saved = new List<(DriverProfile Profile, DriverProfile? Before)>();

        foreach (var profile in profiles)
        {
            _profiles.TryGetValue(profile.Transponder, out var before);

            StampFirstSeen(profile);
            _profiles[profile.Transponder] = profile;
            saved.Add((profile, before));
        }

        RebuildAlternateIndex();
        Save();

        foreach (var (profile, before) in saved)
        {
            RaiseCarsAdded(profile, before);
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

    private void RaiseCarsAdded(DriverProfile profile, DriverProfile? before)
    {
        if (CarsAdded is null)
        {
            return;
        }

        // Saved over itself - a change made to the stored object in place,
        // then written. There is no earlier copy to compare with, and the
        // in-place edits in this app (photo, class rename) never touch cars.
        if (ReferenceEquals(before, profile))
        {
            return;
        }

        var had = before?.AllCars ?? Array.Empty<string>();

        var added = profile.AllCars
            .Where(car => !had.Any(old => string.Equals(old, car, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (added.Count > 0)
        {
            CarsAdded?.Invoke(profile, added);
        }
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

            if (DescribeTakenSince(profile, restoringTogether) is { } reason)
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
    /// </summary>
    public static DriverProfile Snapshot(DriverProfile profile) =>
        JsonSerializer.Deserialize<DriverProfile>(JsonSerializer.Serialize(profile))!;

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
    /// </summary>
    public int PutBack(IReadOnlyCollection<DriverProfile> before, IReadOnlyCollection<string> added)
    {
        var changed = 0;

        foreach (var profile in before)
        {
            if (_profiles.ContainsKey(profile.Transponder))
            {
                _profiles[profile.Transponder] = profile;
                changed++;
            }
        }

        foreach (var transponder in added)
        {
            if (_profiles.Remove(transponder))
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
    /// Which of a deleted profile's transponders somebody else holds now,
    /// and who - or null when neither is anybody else's. Profiles in
    /// <paramref name="ignore"/> (the ones being put back with it) don't
    /// count.
    /// </summary>
    private string? DescribeTakenSince(DriverProfile profile, HashSet<DriverProfile> ignore)
    {
        var main = profile.Transponder?.Trim() ?? string.Empty;
        var alternate = profile.AlternateTransponder?.Trim() ?? string.Empty;

        // An alt transponder repeating the profile's own main one is a quirk
        // of an older file, not somebody else's claim - the alternate index
        // leaves it out too.
        if (string.Equals(alternate, main, StringComparison.OrdinalIgnoreCase))
        {
            alternate = string.Empty;
        }

        foreach (var other in _profiles.Values)
        {
            if (ignore.Contains(other))
            {
                continue;
            }

            var otherMain = other.Transponder?.Trim() ?? string.Empty;
            var otherAlternate = other.AlternateTransponder?.Trim() ?? string.Empty;
            var who = DescribeOwner(other);

            if (main.Length > 0 && string.Equals(otherMain, main, StringComparison.OrdinalIgnoreCase))
            {
                return $"transponder {main} belongs to {who} now";
            }

            if (main.Length > 0 && string.Equals(otherAlternate, main, StringComparison.OrdinalIgnoreCase))
            {
                return $"transponder {main} is {who}'s alt transponder now";
            }

            if (alternate.Length > 0 && string.Equals(otherMain, alternate, StringComparison.OrdinalIgnoreCase))
            {
                return $"the alt transponder {alternate} belongs to {who} now";
            }

            if (alternate.Length > 0 && string.Equals(otherAlternate, alternate, StringComparison.OrdinalIgnoreCase))
            {
                return $"the alt transponder {alternate} is {who}'s alt transponder now";
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
            if (string.Equals(profile.ClassName, oldName, StringComparison.OrdinalIgnoreCase))
            {
                profile.ClassName = newName;
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
            .Where(p => string.Equals(p.ClassName, className, StringComparison.OrdinalIgnoreCase))
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
            if (string.Equals(profile.ClassName, newName, StringComparison.OrdinalIgnoreCase)
                && !keep.Contains(profile.Transponder))
            {
                profile.ClassName = oldName;
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
            var alternate = profile.AlternateTransponder?.Trim() ?? string.Empty;

            if (alternate.Length == 0
                || string.Equals(alternate, profile.Transponder, StringComparison.OrdinalIgnoreCase)
                || _profiles.ContainsKey(alternate))
            {
                continue;
            }

            if (!owners.TryAdd(alternate, profile.Transponder))
            {
                ambiguous.Add(alternate);
            }
        }

        foreach (var alternate in ambiguous)
        {
            owners.Remove(alternate);
        }

        _alternateOwners = owners;
    }
}
