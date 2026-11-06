using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeatCaller;

/// <summary>A battery pack - a driver's own, or the club's.</summary>
public sealed class BatteryPack
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>What's written on it - "3", "Blue tape", "A2".</summary>
    public string Label { get; set; } = string.Empty;

    public string Chemistry { get; set; } = "NiMH";
    public string Notes { get; set; } = string.Empty;

    /// <summary>Out of rotation. Kept rather than deleted so its pace history survives.</summary>
    public bool Retired { get; set; }

    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whose pack it is: the driver's main transponder (their profile's
    /// key), or empty for the club's. Packs are not tied to a car - they
    /// move between a driver's cars - so this is the only link. Every pack
    /// from before packs had owners starts as the club's (see
    /// BatteryPackStore.ConvertOnce).
    /// </summary>
    public string OwnerTransponder { get; set; } = string.Empty;

    /// <summary>Who it has belonged to before, oldest first - a transfer adds one.</summary>
    public List<PackTransfer> Transfers { get; set; } = new();

    [JsonIgnore]
    public bool IsClubPack => string.IsNullOrWhiteSpace(OwnerTransponder);

    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? "(unlabeled)" : Label;
}

/// <summary>A pack changing hands for good - the owner it had, and when.</summary>
public sealed class PackTransfer
{
    /// <summary>The owner before, or empty for the club.</summary>
    public string FromTransponder { get; set; } = string.Empty;

    public DateTime WhenUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>How a session's pack came to be recorded for a driver.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PackTagSource
{
    /// <summary>The pack in the car at the time, recorded when the session was filed.</summary>
    InTheCar,

    /// <summary>Picked on Race Control (right-click a row, Pack).</summary>
    RaceControl,

    /// <summary>Picked on the driver's Batteries tab in Driver Hub.</summary>
    DriverHub,

    /// <summary>A tag from before tags were per driver, on a session one driver ran alone.</summary>
    Converted,

    /// <summary>Picked by the driver on their phone, through the relay.</summary>
    Phone,
}

/// <summary>Which pack a driver ran a session on - or, with no pack id, that none is recorded on purpose.</summary>
public sealed class PackTag
{
    /// <summary>The pack, or empty for "no pack recorded" chosen by hand - which hides a session-wide tag from before tags were per driver.</summary>
    public string PackId { get; set; } = string.Empty;

    public PackTagSource Source { get; set; }

    public DateTime SetUtc { get; set; } = DateTime.UtcNow;

    public PackTag Copy() => new() { PackId = PackId, Source = Source, SetUtc = SetUtc };
}

/// <summary>
/// Battery packs, whose they are, which pack is in each driver's car now,
/// and which pack each driver ran each session on.
///
/// Scope is deliberately narrow. A full maintenance log - cycle counts,
/// charge records, capacity readings - needs someone to log every charge
/// every time, and that discipline reliably lapses after a couple of race
/// days. Stale data is worse than none, because it gets trusted.
///
/// What this does instead is answer one question that pays off on its own:
/// am I slower on a particular pack? That needs one pack per driver per
/// session - and, so nobody has to remember to pick one every time, each
/// driver's pack in the car now (CurrentPackId), recorded for them when a
/// session is filed (AutoTag). A pack picked by hand for a session always
/// beats that: AutoTag never writes over a tag.
///
/// Tags used to be one per SESSION, so in a heat of eight only one pack
/// could be recorded. Those are kept (the file's SessionPacks) and still
/// count for every driver in that session, exactly as they always did,
/// unless the driver has a tag of their own; ConvertOnce turns the ones on
/// a session one driver ran alone - where the tag is certain - into theirs.
///
/// The pack in the car is kept here rather than on the driver's profile,
/// so that everything about packs is in one file, saved and undone
/// together, and putting a pack in a car from the phone never writes the
/// whole profile.
///
/// The session links are held here rather than on SessionRecord so recorded
/// results stay untouched - a pack is a note about equipment, not part of
/// the timing data.
///
/// Used from the phone's race-day page as well as the desk, so every read
/// and write is under a lock, and Changed may be raised off the UI thread.
/// </summary>
public sealed class BatteryPackStore
{
    private readonly object _gate = new();

    /// <summary>Held from taking the file's contents to writing them, so two saves land in the order their contents were taken.</summary>
    private readonly object _saveGate = new();

    private readonly List<BatteryPack> _packs;

    /// <summary>Session id to pack id: the tags from before tags were per driver.</summary>
    private readonly Dictionary<string, string> _sessionPacks;

    /// <summary>"{session id}|{driver's main transponder}" to that driver's tag.</summary>
    private readonly Dictionary<string, PackTag> _driverPacks;

    /// <summary>A driver's main transponder to the pack in their car now.</summary>
    private readonly Dictionary<string, string> _currentPacks;

    /// <summary>Whether ConvertOnce has run on this file.</summary>
    private bool _perDriver;

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "battery-packs.json");

    private BatteryPackStore(PackFile? file)
    {
        file ??= new PackFile();
        _packs = (file.Packs ?? new()).Where(p => p is not null).ToList();

        foreach (var pack in _packs)
        {
            Repair(pack);
        }

        _sessionPacks = new Dictionary<string, string>(file.SessionPacks ?? new(), StringComparer.OrdinalIgnoreCase);
        _driverPacks = new Dictionary<string, PackTag>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, tag) in file.DriverPacks ?? new())
        {
            if (tag is not null && key.Contains('|'))
            {
                tag.PackId ??= string.Empty;
                _driverPacks[key] = tag;
            }
        }

        _currentPacks = new Dictionary<string, string>(file.CurrentPacks ?? new(), StringComparer.OrdinalIgnoreCase);
        _perDriver = file.PerDriverTags;
    }

    /// <summary>
    /// Fills in what every pack must have, on load and on every Upsert.
    /// WHY (2026-11-05, Bug hunt 11): a hand-edited or restored
    /// battery-packs.json can say "Notes": null where the app never writes
    /// one, and it loaded as it was - then Driver Hub's Batteries tab and
    /// Garage's Club Batteries both threw on pack.Notes.Replace, so neither
    /// could be opened until the file was fixed by hand. Repaired as
    /// RentalTransponderStore (bug hunt 10), RivalryStore, ThemeStore and
    /// VenueStore do; Label and Chemistry with it, to their defaults.
    /// </summary>
    private static void Repair(BatteryPack pack)
    {
        pack.OwnerTransponder = pack.OwnerTransponder?.Trim() ?? string.Empty;
        pack.Transfers ??= new();
        pack.Label ??= string.Empty;
        pack.Chemistry ??= "NiMH";
        pack.Notes ??= string.Empty;
    }

    private sealed class PackFile
    {
        public List<BatteryPack> Packs { get; set; } = new();

        /// <summary>Session id to pack id - the tags from before they were per driver.</summary>
        public Dictionary<string, string> SessionPacks { get; set; } = new();

        /// <summary>"{session id}|{driver's main transponder}" to the pack they ran it on.</summary>
        public Dictionary<string, PackTag> DriverPacks { get; set; } = new();

        /// <summary>A driver's main transponder to the pack in their car now.</summary>
        public Dictionary<string, string> CurrentPacks { get; set; } = new();

        /// <summary>Set once ConvertOnce has run - see there.</summary>
        public bool PerDriverTags { get; set; }
    }

    public static BatteryPackStore Load()
    {
        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var file = JsonSerializer.Deserialize<PackFile>(SafeFile.ReadAllTextWithRetry(FilePath));
                if (file is not null)
                {
                    return new BatteryPackStore(file);
                }
            }
        }
        catch (Exception ex)
        {
            // Unreadable file: start empty rather than refusing to open -
            // but, when it could not be read at all or no copy of it could
            // be kept, never save over it this session (see LoadedUnreadable).
            unreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
        }

        return new BatteryPackStore(null) { LoadedUnreadable = unreadable };
    }

    /// <summary>
    /// True when the file was there at startup but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. The file holds the packs, whose
    /// they are and which pack ran in which session: nothing anybody could
    /// type back in. It used to be loaded as empty and then written over
    /// whole at the first change, so a backup or antivirus tool holding it
    /// open for a moment at launch was enough to lose all of it. What this
    /// store holds is then only what was added this session, and none of it
    /// is saved.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    /// <summary>
    /// Raised after every change is saved - from the desk, Race Control,
    /// check-in, the phone or an undo. May be raised off the UI thread (the
    /// phone's race-day page), so a page listening marshals before it
    /// touches a control.
    /// </summary>
    public event Action? Changed;

    /// <summary>Writes the file and raises Changed - outside the lock, so a listener reading the store back never waits on the writer.</summary>
    private void SaveAndRaise()
    {
        Save();

        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            // A page that fails to redraw must not undo the save, nor come
            // back out through whatever changed the pack.
            System.Diagnostics.Debug.WriteLine($"A battery pack listener failed: {ex}");
        }
    }

    public void Save()
    {
        // Not written: the real file is almost certainly fine, and saving
        // this session's near-empty list would replace it. The console
        // has already told the operator why and asked for a restart.
        if (LoadedUnreadable)
        {
            return;
        }

        lock (_saveGate)
        {
            string json;

            lock (_gate)
            {
                json = JsonSerializer.Serialize(FileNow(), new JsonSerializerOptions { WriteIndented = true });
            }

            SafeFile.WriteAllText(FilePath, json);
        }
    }

    private PackFile FileNow() => new()
    {
        Packs = _packs.ToList(),
        SessionPacks = new Dictionary<string, string>(_sessionPacks),
        DriverPacks = _driverPacks.ToDictionary(kv => kv.Key, kv => kv.Value.Copy()),
        CurrentPacks = new Dictionary<string, string>(_currentPacks),
        PerDriverTags = _perDriver,
    };

    // --- The packs ----------------------------------------------------------

    // By number, not character by character: "Pack 2" before "Pack 10"
    // (NaturalOrder).
    public IReadOnlyList<BatteryPack> All
    {
        get
        {
            lock (_gate)
            {
                return _packs.OrderBy(p => p.Retired).ThenBy(p => p.Label, NaturalOrder.Instance).ToList();
            }
        }
    }

    public IReadOnlyList<BatteryPack> InRotation => All.Where(p => !p.Retired).ToList();

    public BatteryPack? Get(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        lock (_gate)
        {
            return _packs.FirstOrDefault(p => p.Id == id);
        }
    }

    /// <summary>
    /// A driver's own packs, retired ones included: owned under their main
    /// transponder or any other of theirs, as a car's owner can be.
    /// </summary>
    public IReadOnlyList<BatteryPack> OwnedBy(DriverProfile? profile)
    {
        if (profile is null)
        {
            return new List<BatteryPack>();
        }

        var ids = profile.AllTransponderIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return All.Where(p => !p.IsClubPack && ids.Contains(p.OwnerTransponder)).ToList();
    }

    /// <summary>The club's packs, retired ones included.</summary>
    public IReadOnlyList<BatteryPack> ClubPacks => All.Where(p => p.IsClubPack).ToList();

    /// <summary>
    /// What a driver can be running: their own packs in rotation, then the
    /// club's - and, at the end, <paramref name="alsoPackId"/> when it is
    /// neither (retired, or somebody else's since), so a picker can still
    /// show the pack recorded selected.
    /// </summary>
    public IReadOnlyList<BatteryPack> ChoicesFor(DriverProfile? profile, string? alsoPackId = null)
    {
        var list = OwnedBy(profile).Where(p => !p.Retired)
            .Concat(ClubPacks.Where(p => !p.Retired))
            .ToList();

        if (Get(alsoPackId) is { } also && list.All(p => p.Id != also.Id))
        {
            list.Add(also);
        }

        return list;
    }

    /// <summary>Adds a pack, or saves one edited (by id). Retired, it stops being in anybody's car.</summary>
    public void Upsert(BatteryPack pack)
    {
        Repair(pack);

        lock (_gate)
        {
            var index = _packs.FindIndex(p => p.Id == pack.Id);
            if (index >= 0)
            {
                _packs[index] = pack;
            }
            else
            {
                _packs.Add(pack);
            }

            if (pack.Retired)
            {
                ClearCurrentLocked(pack.Id, keep: null);
            }
        }

        SaveAndRaise();
    }

    /// <summary>Removes a pack, every session tag with it, and it from anybody's car.</summary>
    public void Delete(string id)
    {
        lock (_gate)
        {
            _packs.RemoveAll(p => p.Id == id);

            foreach (var key in _sessionPacks.Where(kv => kv.Value == id).Select(kv => kv.Key).ToList())
            {
                _sessionPacks.Remove(key);
            }

            foreach (var key in _driverPacks.Where(kv => kv.Value.PackId == id).Select(kv => kv.Key).ToList())
            {
                _driverPacks.Remove(key);
            }

            ClearCurrentLocked(id, keep: null);
        }

        SaveAndRaise();
    }

    /// <summary>
    /// Gives a pack to a driver (their main transponder) or to the club
    /// (empty), for good. The old owner is added to its transfers, and it
    /// comes out of anybody's car but the new owner's. Its sessions stay as
    /// they were: they were run on it, whoever has it now. Returns why not,
    /// or null.
    /// </summary>
    public string? Transfer(string packId, string toTransponder)
    {
        var to = toTransponder?.Trim() ?? string.Empty;

        lock (_gate)
        {
            var pack = _packs.FirstOrDefault(p => p.Id == packId);

            if (pack is null)
            {
                return "That pack has been deleted.";
            }

            if (string.Equals(pack.OwnerTransponder, to, StringComparison.OrdinalIgnoreCase))
            {
                return to.Length == 0 ? "It is the club's already." : "It is theirs already.";
            }

            // A new object rather than the stored one changed in place: a
            // copy handed out earlier (Remember) must not change with it.
            var moved = CopyOf(pack);
            moved.Transfers.Add(new PackTransfer { FromTransponder = pack.OwnerTransponder, WhenUtc = DateTime.UtcNow });
            moved.OwnerTransponder = to;
            _packs[_packs.IndexOf(pack)] = moved;

            ClearCurrentLocked(packId, keep: to.Length == 0 ? null : to);
        }

        SaveAndRaise();
        return null;
    }

    /// <summary>Takes the pack out of every driver's car but <paramref name="keep"/>'s. Under the lock.</summary>
    private void ClearCurrentLocked(string packId, string? keep)
    {
        foreach (var driver in _currentPacks.Where(kv => kv.Value == packId).Select(kv => kv.Key).ToList())
        {
            if (keep is null || !string.Equals(driver, keep, StringComparison.OrdinalIgnoreCase))
            {
                _currentPacks.Remove(driver);
            }
        }
    }

    // --- The pack in the car now -------------------------------------------

    /// <summary>
    /// The key a driver's tags and pack in the car are filed under: their
    /// main transponder, from any of theirs - so a second car's row counts
    /// as them. A transponder nobody has is its own key.
    /// </summary>
    public static string DriverKey(ProfileStore? profiles, string? transponder)
    {
        var trimmed = transponder?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        return profiles?.Get(trimmed)?.Transponder?.Trim() is { Length: > 0 } main ? main : trimmed;
    }

    /// <summary>The pack in this driver's car now (their main transponder), or null.</summary>
    public BatteryPack? CurrentPack(string driverKey) => Get(CurrentPackId(driverKey));

    /// <summary>The id of the pack in this driver's car now - or null.</summary>
    public string? CurrentPackId(string driverKey)
    {
        lock (_gate)
        {
            return _currentPacks.TryGetValue(driverKey?.Trim() ?? string.Empty, out var id) ? id : null;
        }
    }

    /// <summary>Whose car a pack is in now - their main transponders. A club pack can be in several.</summary>
    public IReadOnlyList<string> InCarsOf(string packId)
    {
        lock (_gate)
        {
            return _currentPacks.Where(kv => kv.Value == packId).Select(kv => kv.Key).ToList();
        }
    }

    /// <summary>
    /// Puts a pack in this driver's car (their main transponder), or none.
    /// Sessions already recorded are left as they are. False when that is
    /// what was there already, or the pack is not there to put in.
    /// </summary>
    public bool SetCurrent(string driverKey, string? packId)
    {
        var key = driverKey?.Trim() ?? string.Empty;

        if (key.Length == 0)
        {
            return false;
        }

        lock (_gate)
        {
            _currentPacks.TryGetValue(key, out var was);

            if (string.IsNullOrEmpty(packId))
            {
                if (was is null)
                {
                    return false;
                }

                _currentPacks.Remove(key);
            }
            else
            {
                if (was == packId || _packs.All(p => p.Id != packId))
                {
                    return false;
                }

                _currentPacks[key] = packId;
            }
        }

        SaveAndRaise();
        return true;
    }

    // --- Which pack a session was run on -----------------------------------

    private static string TagKey(string sessionId, string driverKey) => $"{sessionId}|{driverKey.Trim()}";

    /// <summary>This driver's own tag for a session - null when they have none (a session-wide one from before may still count, see PackFor).</summary>
    public PackTag? TagFor(string sessionId, string driverKey)
    {
        lock (_gate)
        {
            return _driverPacks.TryGetValue(TagKey(sessionId, driverKey), out var tag) ? tag.Copy() : null;
        }
    }

    /// <summary>
    /// The pack a driver ran a session on: their own tag, or - when they
    /// have none - the session's tag from before tags were per driver,
    /// which counts for everyone in it, as it always did. A tag of "no pack
    /// recorded" picked by hand hides that one. Null when none is recorded
    /// or the pack has since been deleted.
    /// </summary>
    public BatteryPack? PackFor(string sessionId, string driverKey) => Get(PackIdFor(sessionId, driverKey));

    /// <summary>The id behind PackFor - or null.</summary>
    public string? PackIdFor(string sessionId, string driverKey)
    {
        lock (_gate)
        {
            if (_driverPacks.TryGetValue(TagKey(sessionId, driverKey), out var tag))
            {
                return tag.PackId.Length == 0 ? null : tag.PackId;
            }

            return _sessionPacks.TryGetValue(sessionId, out var old) ? old : null;
        }
    }

    /// <summary>Whether what PackFor gives comes from the session-wide tag from before tags were per driver.</summary>
    public bool IsSessionWideTag(string sessionId, string driverKey)
    {
        lock (_gate)
        {
            return !_driverPacks.ContainsKey(TagKey(sessionId, driverKey)) && _sessionPacks.ContainsKey(sessionId);
        }
    }

    /// <summary>
    /// Records which pack a driver ran a session on, picked by hand - or,
    /// with no pack, that none is recorded. Written over whatever was there:
    /// a pick by hand always wins.
    /// </summary>
    public void Tag(string sessionId, string driverKey, string? packId, PackTagSource source)
    {
        lock (_gate)
        {
            _driverPacks[TagKey(sessionId, driverKey)] = new PackTag
            {
                PackId = packId ?? string.Empty,
                Source = source,
                SetUtc = DateTime.UtcNow,
            };
        }

        SaveAndRaise();
    }

    /// <summary>The undo half of Tag: the driver's tag for the session back as it was - <paramref name="before"/>, or none.</summary>
    public void RestoreTag(string sessionId, string driverKey, PackTag? before)
    {
        lock (_gate)
        {
            if (before is null)
            {
                _driverPacks.Remove(TagKey(sessionId, driverKey));
            }
            else
            {
                _driverPacks[TagKey(sessionId, driverKey)] = before.Copy();
            }
        }

        SaveAndRaise();
    }

    /// <summary>
    /// When a session is filed: each driver in it with a pack in the car and
    /// no tag yet is tagged with that pack. A tag already there - picked on
    /// Race Control during the race - is left alone. Not for a demo race,
    /// whose laps are invented. Returns how many were tagged.
    /// </summary>
    public int AutoTag(SessionRecord session, ProfileStore? profiles)
    {
        if (session.IsNotScored())
        {
            return 0;
        }

        // Worked out before the lock: the profiles have a lock of their own.
        var keys = session.Results
            .Where(r => !string.IsNullOrWhiteSpace(r.Transponder))
            .Select(r => DriverKey(profiles, r.Transponder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tagged = 0;

        lock (_gate)
        {
            foreach (var key in keys)
            {
                var tagKey = TagKey(session.Id, key);

                if (_driverPacks.ContainsKey(tagKey)
                    || !_currentPacks.TryGetValue(key, out var packId)
                    || _packs.FirstOrDefault(p => p.Id == packId) is not { Retired: false })
                {
                    continue;
                }

                _driverPacks[tagKey] = new PackTag { PackId = packId, Source = PackTagSource.InTheCar, SetUtc = DateTime.UtcNow };
                tagged++;
            }
        }

        if (tagged > 0)
        {
            SaveAndRaise();
        }

        return tagged;
    }

    /// <summary>
    /// Once per file, when tags became per driver: a session-wide tag on a
    /// session exactly one driver ran (laps on the board) becomes that
    /// driver's own - there it is certain whose pack it was. The rest stay
    /// session-wide, and still count as they always did. Each pack, which
    /// before had no owner, becomes the driver's when every tag converted
    /// for it is that one driver's; the rest stay the club's.
    ///
    /// Not run while the history, the profiles or this file could not be
    /// read - it would be working from nothing - and so tried again next
    /// time. Returns how many tags were converted and how many packs given
    /// an owner.
    /// </summary>
    public (int Converted, int Owned) ConvertOnce(SessionHistoryStore history, ProfileStore profiles)
    {
        if (LoadedUnreadable || history.LoadedUnreadable || profiles.LoadedUnreadable)
        {
            return (0, 0);
        }

        List<KeyValuePair<string, string>> old;

        lock (_gate)
        {
            if (_perDriver)
            {
                return (0, 0);
            }

            old = _sessionPacks.ToList();
        }

        // Whose each tagged session was, worked out before the lock: the
        // profiles have a lock of their own.
        var soleDriver = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (sessionId, _) in old)
        {
            if (history.Get(sessionId) is not { } session)
            {
                continue;
            }

            var drivers = session.Results
                .Where(r => r.Laps > 0 && !string.IsNullOrWhiteSpace(r.Transponder))
                .Select(r => DriverKey(profiles, r.Transponder))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (drivers.Count == 1)
            {
                soleDriver[sessionId] = drivers[0];
            }
        }

        var onFile = profiles.All.Select(p => p.Transponder.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var converted = 0;
        var owned = 0;

        lock (_gate)
        {
            if (_perDriver)
            {
                return (0, 0);
            }

            var driversByPack = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            foreach (var (sessionId, driver) in soleDriver)
            {
                if (!_sessionPacks.TryGetValue(sessionId, out var packId))
                {
                    continue;
                }

                // A driver with a tag of their own for it already keeps it;
                // the session-wide one is no longer needed either way.
                if (_driverPacks.TryAdd(TagKey(sessionId, driver), new PackTag { PackId = packId, Source = PackTagSource.Converted, SetUtc = DateTime.UtcNow }))
                {
                    converted++;
                }

                _sessionPacks.Remove(sessionId);

                if (!driversByPack.TryGetValue(packId, out var set))
                {
                    driversByPack[packId] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                set.Add(driver);
            }

            foreach (var (packId, drivers) in driversByPack)
            {
                var index = _packs.FindIndex(p => p.Id == packId);

                // Not a pack still tagged on a session several drivers ran:
                // it was being shared, and stays the club's.
                if (drivers.Count == 1 && index >= 0 && _packs[index].IsClubPack && onFile.Contains(drivers.First())
                    && !_sessionPacks.ContainsValue(packId))
                {
                    var theirs = CopyOf(_packs[index]);
                    theirs.OwnerTransponder = drivers.First();
                    _packs[index] = theirs;
                    owned++;
                }
            }

            _perDriver = true;
        }

        SaveAndRaise();
        return (converted, owned);
    }

    /// <summary>How many sessions a pack is recorded on, for anyone - its own tags and the session-wide ones from before.</summary>
    public int SessionsOn(string packId)
    {
        lock (_gate)
        {
            return _driverPacks.Where(kv => kv.Value.PackId == packId)
                       .Select(kv => kv.Key[..kv.Key.IndexOf('|')])
                       .Concat(_sessionPacks.Where(kv => kv.Value == packId).Select(kv => kv.Key))
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .Count();
        }
    }

    public sealed record PackPace(
        BatteryPack Pack,
        int Sessions,
        double BestLapSeconds,
        double AverageBestLapSeconds,
        double GapToBestPackSeconds);

    /// <summary>
    /// Compares pace across packs for one driver.
    ///
    /// Scoped to a driver because a pack's apparent pace is meaningless
    /// across different people - a quick driver on a tired pack still beats
    /// a slow driver on a fresh one, and averaging them together would say
    /// more about who used which pack than about the packs.
    ///
    /// Uses each session's best lap rather than average, since a best lap is
    /// much less affected by traffic and mistakes. The pack is the driver's
    /// own tag, or the session's from before tags were per driver (PackFor).
    /// With the profiles, a result under any of the driver's transponders
    /// counts as theirs.
    /// </summary>
    public List<PackPace> ComparePace(string transponder, SessionHistoryStore history, int minSessions = 1, ProfileStore? profiles = null)
    {
        var key = DriverKey(profiles, transponder);
        var byPack = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in history.All)
        {
            // A saved demo race's lap times are invented, so they say
            // nothing about how a real pack performs.
            if (session.IsNotScored() || PackIdFor(session.Id, key) is not { } packId)
            {
                continue;
            }

            var result = session.Results.FirstOrDefault(r =>
                string.Equals(DriverKey(profiles, r.Transponder), key, StringComparison.OrdinalIgnoreCase) && r.FastLapMs > 0);

            if (result is null)
            {
                continue;
            }

            if (!byPack.TryGetValue(packId, out var list))
            {
                list = new List<double>();
                byPack[packId] = list;
            }

            list.Add(result.FastLapMs / 1000.0);
        }

        var rows = byPack
            .Where(kv => kv.Value.Count >= Math.Max(1, minSessions) && Get(kv.Key) is not null)
            .Select(kv => new
            {
                Pack = Get(kv.Key)!,
                Sessions = kv.Value.Count,
                Best = kv.Value.Min(),
                AvgBest = kv.Value.Average(),
            })
            .OrderBy(x => x.AvgBest)
            .ToList();

        if (rows.Count == 0)
        {
            return new List<PackPace>();
        }

        var quickest = rows[0].AvgBest;

        return rows.Select(x => new PackPace(
            x.Pack,
            x.Sessions,
            Math.Round(x.Best, 3),
            Math.Round(x.AvgBest, 3),
            Math.Round(x.AvgBest - quickest, 3))).ToList();
    }

    // --- Undo -------------------------------------------------------------
    //
    // Each undo puts back only the pack, tag or pack in the car its action
    // changed. A whole-file snapshot would also wipe out every pack added
    // and every session tagged since, when an older step was undone.

    /// <summary>A detached copy of a pack, for an edit dialog to work on and an undo to put back.</summary>
    public static BatteryPack CopyOf(BatteryPack pack) =>
        JsonSerializer.Deserialize<BatteryPack>(JsonSerializer.Serialize(pack))!;

    /// <summary>
    /// Everything about one pack an action can change: the pack itself (or
    /// null, when the action is adding it), where it sits in the list, the
    /// sessions tagged with it and whose car it is in.
    /// </summary>
    public sealed record PackMemo(
        string PackId,
        BatteryPack? Pack,
        int Index,
        IReadOnlyList<KeyValuePair<string, PackTag>> DriverTags,
        IReadOnlyList<string> SessionTags,
        IReadOnlyList<string> InCarsOf);

    /// <summary>What an action on this pack is about to change - taken before it, for its undo (PutBack).</summary>
    public PackMemo Remember(string packId)
    {
        lock (_gate)
        {
            var index = _packs.FindIndex(p => p.Id == packId);

            return new PackMemo(
                packId,
                index < 0 ? null : CopyOf(_packs[index]),
                index,
                _driverPacks.Where(kv => kv.Value.PackId == packId)
                    .Select(kv => new KeyValuePair<string, PackTag>(kv.Key, kv.Value.Copy())).ToList(),
                _sessionPacks.Where(kv => kv.Value == packId).Select(kv => kv.Key).ToList(),
                _currentPacks.Where(kv => kv.Value == packId).Select(kv => kv.Key).ToList());
        }
    }

    /// <summary>
    /// The undo half of adding, editing, retiring, transferring or deleting
    /// a pack: the pack as <paramref name="memo"/> had it - or removed, when
    /// the action added it - its tags back where nothing newer has replaced
    /// them, and back in the cars it was in where nothing else has been put
    /// since. False, changing nothing, when there is nothing to put it back
    /// over: an edited pack deleted since, or a deleted pack already back.
    /// </summary>
    public bool PutBack(PackMemo memo, bool wasDeleted = false)
    {
        if (memo.Pack is null)
        {
            if (Get(memo.PackId) is null)
            {
                return false;
            }

            Delete(memo.PackId);
            return true;
        }

        lock (_gate)
        {
            var index = _packs.FindIndex(p => p.Id == memo.PackId);

            if (wasDeleted ? index >= 0 : index < 0)
            {
                return false;
            }

            var pack = CopyOf(memo.Pack);

            if (index >= 0)
            {
                _packs[index] = pack;
            }
            else
            {
                _packs.Insert(Math.Clamp(memo.Index, 0, _packs.Count), pack);
            }

            foreach (var (key, tag) in memo.DriverTags)
            {
                _driverPacks.TryAdd(key, tag.Copy());
            }

            foreach (var sessionId in memo.SessionTags)
            {
                _sessionPacks.TryAdd(sessionId, memo.PackId);
            }

            foreach (var driver in memo.InCarsOf)
            {
                _currentPacks.TryAdd(driver, memo.PackId);
            }
        }

        SaveAndRaise();
        return true;
    }
}
