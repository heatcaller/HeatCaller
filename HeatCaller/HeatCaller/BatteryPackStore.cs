using System.Text.Json;

namespace HeatCaller;

/// <summary>A battery pack in the rotation.</summary>
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

    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? "(unlabelled)" : Label;
}

/// <summary>
/// Battery packs, and which pack was in the car for a given session.
///
/// Scope is deliberately narrow. A full maintenance log - cycle counts,
/// charge records, capacity readings - needs someone to log every charge
/// every time, and that discipline reliably lapses after a couple of race
/// days. Stale data is worse than none, because it gets trusted.
///
/// What this does instead is answer one question that pays off on its own:
/// am I slower on a particular pack? That needs a single choice per session
/// and nothing else.
///
/// The session link is held here rather than on SessionRecord so recorded
/// results stay untouched - a pack assignment is a note about equipment,
/// not part of the timing data.
/// </summary>
public sealed class BatteryPackStore
{
    private readonly List<BatteryPack> _packs;
    private readonly Dictionary<string, string> _sessionPacks;

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "battery-packs.json");

    private BatteryPackStore(List<BatteryPack> packs, Dictionary<string, string>? sessionPacks)
    {
        _packs = packs;
        _sessionPacks = sessionPacks is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(sessionPacks, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class PackFile
    {
        public List<BatteryPack> Packs { get; set; } = new();

        /// <summary>Session id to pack id.</summary>
        public Dictionary<string, string> SessionPacks { get; set; } = new();
    }

    public static BatteryPackStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var file = JsonSerializer.Deserialize<PackFile>(SafeFile.ReadAllTextWithRetry(FilePath));
                if (file is not null)
                {
                    return new BatteryPackStore(file.Packs, file.SessionPacks);
                }
            }
        }
        catch (Exception ex)
        {
            // Unreadable file: start empty rather than refusing to open.
            SafeFile.PreserveUnreadable(FilePath, ex);
        }

        return new BatteryPackStore(new List<BatteryPack>(), null);
    }

    public void Save()
    {
        SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(
            new PackFile { Packs = _packs, SessionPacks = new Dictionary<string, string>(_sessionPacks) },
            new JsonSerializerOptions { WriteIndented = true }));
    }

    // By number, not character by character: "Pack 2" before "Pack 10"
    // (NaturalOrder).
    public IReadOnlyList<BatteryPack> All =>
        _packs.OrderBy(p => p.Retired).ThenBy(p => p.Label, NaturalOrder.Instance).ToList();

    public IReadOnlyList<BatteryPack> InRotation =>
        _packs.Where(p => !p.Retired).OrderBy(p => p.Label, NaturalOrder.Instance).ToList();

    public BatteryPack? Get(string? id) => id is null ? null : _packs.FirstOrDefault(p => p.Id == id);

    public void Upsert(BatteryPack pack)
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

        Save();
    }

    public void Delete(string id)
    {
        _packs.RemoveAll(p => p.Id == id);

        foreach (var sessionId in _sessionPacks.Where(kv => kv.Value == id).Select(kv => kv.Key).ToList())
        {
            _sessionPacks.Remove(sessionId);
        }

        Save();
    }

    /// <summary>Which pack was used for a session, if recorded.</summary>
    public BatteryPack? PackForSession(string sessionId) =>
        _sessionPacks.TryGetValue(sessionId, out var packId) ? Get(packId) : null;

    public void AssignSession(string sessionId, string? packId)
    {
        if (string.IsNullOrWhiteSpace(packId))
        {
            _sessionPacks.Remove(sessionId);
        }
        else
        {
            _sessionPacks[sessionId] = packId;
        }

        Save();
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
    /// much less affected by traffic and mistakes.
    /// </summary>
    public List<PackPace> ComparePace(string transponder, SessionHistoryStore history, int minSessions = 1)
    {
        var byPack = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in history.All)
        {
            // A saved demo race's lap times are invented, so they say
            // nothing about how a real pack performs.
            if (session.IsDemoSession() || !_sessionPacks.TryGetValue(session.Id, out var packId))
            {
                continue;
            }

            var result = session.Results.FirstOrDefault(r =>
                string.Equals(r.Transponder, transponder, StringComparison.OrdinalIgnoreCase) && r.FastLapMs > 0);

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
    // Each undo puts back only the pack or session tag its action changed.
    // The whole-file Snapshot/Restore below also wiped out every pack added
    // and every session tagged since, when an older step was undone.

    /// <summary>A detached copy of a pack, for an edit dialog to work on and an undo to put back.</summary>
    public static BatteryPack CopyOf(BatteryPack pack) =>
        JsonSerializer.Deserialize<BatteryPack>(JsonSerializer.Serialize(pack))!;

    /// <summary>Where a pack sits in the list, so a deleted one can go back in the same place.</summary>
    public int IndexOf(string id) => _packs.FindIndex(p => p.Id == id);

    /// <summary>The pack id a session is tagged with, even one whose pack has gone - or null.</summary>
    public string? PackIdForSession(string sessionId) =>
        _sessionPacks.TryGetValue(sessionId, out var packId) ? packId : null;

    /// <summary>The sessions tagged with a pack - what deleting it would untag.</summary>
    public IReadOnlyList<string> SessionsTaggedWith(string packId) =>
        _sessionPacks.Where(kv => kv.Value == packId).Select(kv => kv.Key).ToList();

    /// <summary>
    /// The undo half of Upsert: <paramref name="before"/> goes back over the
    /// pack with this id, or - when it is null, because the Upsert added the
    /// pack - that pack is removed (and any session tagged with it since is
    /// untagged, as Delete does). Returns false when the pack is gone.
    /// </summary>
    public bool UndoUpsert(string id, BatteryPack? before)
    {
        var index = IndexOf(id);

        if (index < 0)
        {
            return false;
        }

        if (before is null)
        {
            Delete(id);
            return true;
        }

        _packs[index] = before;
        Save();
        return true;
    }

    /// <summary>
    /// The undo half of Delete: the pack back in its old place, and its
    /// session tags back on those sessions - except a session tagged with
    /// another pack since, which keeps its newer tag.
    /// </summary>
    public bool RestoreDeleted(BatteryPack pack, int index, IReadOnlyList<string> sessionIds)
    {
        if (IndexOf(pack.Id) >= 0)
        {
            return false;
        }

        _packs.Insert(Math.Clamp(index, 0, _packs.Count), pack);

        foreach (var sessionId in sessionIds)
        {
            _sessionPacks.TryAdd(sessionId, pack.Id);
        }

        Save();
        return true;
    }

    public string Snapshot() =>
        JsonSerializer.Serialize(new PackFile { Packs = _packs, SessionPacks = new Dictionary<string, string>(_sessionPacks) });

    public void Restore(string snapshot)
    {
        try
        {
            var file = JsonSerializer.Deserialize<PackFile>(snapshot);
            if (file is null)
            {
                return;
            }

            _packs.Clear();
            _packs.AddRange(file.Packs);

            _sessionPacks.Clear();
            foreach (var (key, value) in file.SessionPacks)
            {
                _sessionPacks[key] = value;
            }

            Save();
        }
        catch
        {
            // A failed restore leaves the current state alone.
        }
    }
}
