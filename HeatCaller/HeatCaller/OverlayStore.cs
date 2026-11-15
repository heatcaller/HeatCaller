using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// THE CLUB'S OWN STREAM OVERLAYS (14 Nov 2026, stream overlay plan section
/// 4, Asher: "the ability to roll your own"): overlays.json beside
/// sponsors.json. Each layout is a copy of a preset the club has changed,
/// under an id that never changes - its address is /overlay/&lt;id&gt;, so
/// renaming one never breaks an OBS source.
///
/// COPIES IN, COPIES OUT. The Wi-Fi server reads a layout on its own thread
/// while the editor changes it on the UI thread, so nothing outside this
/// class ever holds one of the layouts kept here. Each change bumps that
/// layout's version, which an open overlay page watches to redraw itself -
/// so a club editing with OBS open sees the change on the stream as it
/// makes it.
///
/// A file that can't be read leaves the presets working and is never saved
/// over this session (SafeFile.HoldWritesAfterUnreadable), as sponsors.json.
/// </summary>
public sealed class OverlayStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly List<OverlayLayout> _layouts;
    private readonly Dictionary<string, int> _versions = new(StringComparer.Ordinal);

    /// <summary>Raised after any change, on the thread that made it.</summary>
    public event Action? Changed;

    /// <summary>True when the file was there but could not be read: nothing is saved over it.</summary>
    public bool LoadedUnreadable { get; private init; }

    /// <summary>What went wrong reading the file, in words, or empty.</summary>
    public string Problem { get; private init; } = string.Empty;

    private OverlayStore(string path, List<OverlayLayout> layouts)
    {
        _path = path;
        _layouts = layouts;
    }

    public static string DefaultPath => Path.Combine(AppPaths.Root, "overlays.json");

    public static OverlayStore Load(string? path = null)
    {
        path ??= DefaultPath;

        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<List<OverlayLayout>>(SafeFile.ReadAllTextWithRetry(path)) ?? new List<OverlayLayout>();

                // What can be kept, kept: a layout with no usable id, or one
                // that would take a preset's address, is dropped; a broken
                // element is dropped, not the layout.
                var kept = loaded
                    .Where(l => l is not null && IsCustomId(l.Id) && OverlayLayouts.BuiltIn(l.Id) is null)
                    .GroupBy(l => l.Id, StringComparer.Ordinal)
                    .Select(g => Tidy(g.First()))
                    .ToList();

                return new OverlayStore(path, kept);
            }
        }
        catch (Exception ex)
        {
            var held = SafeFile.HoldWritesAfterUnreadable(path, ex);
            return new OverlayStore(path, new List<OverlayLayout>()) { LoadedUnreadable = held, Problem = "Your stream overlays couldn't be read: " + ex.Message };
        }

        return new OverlayStore(path, new List<OverlayLayout>());
    }

    /// <summary>Every one of the club's layouts, as copies, in the order made.</summary>
    public IReadOnlyList<OverlayLayout> Layouts
    {
        get
        {
            lock (_gate)
            {
                return _layouts.Select(l => l.Copy()).ToList();
            }
        }
    }

    /// <summary>One layout by id as a copy, or null. Safe from any thread.</summary>
    public OverlayLayout? Get(string? id)
    {
        lock (_gate)
        {
            return _layouts.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.Ordinal))?.Copy();
        }
    }

    /// <summary>How many times this layout has changed this session - an overlay page redraws when it moves. Safe from any thread.</summary>
    public int Version(string? id)
    {
        lock (_gate)
        {
            return id is not null && _versions.TryGetValue(id, out var v) ? v : 0;
        }
    }

    /// <summary>
    /// A new layout copied from <paramref name="from"/> (a preset or one of
    /// the club's own), called <paramref name="name"/>, under a new id.
    /// Returns the copy.
    /// </summary>
    public OverlayLayout AddCopy(OverlayLayout from, string name)
    {
        var copy = from.Copy();
        copy.Name = string.IsNullOrWhiteSpace(name) ? from.Name + " (copy)" : name.Trim();

        // A preset with no elements of its own (the Results table is the old
        // overlay page) is copied as the Broadcast preset's arrangement.
        if (copy.Elements.Count == 0)
        {
            copy.Elements = OverlayLayouts.BuiltIn(OverlayLayouts.BroadcastId)!.Copy().Elements;
        }

        lock (_gate)
        {
            copy.Id = NewId(copy.Name);
            _layouts.Add(Tidy(copy));
            Bump(copy.Id);
        }

        Save();
        Changed?.Invoke();
        return copy.Copy();
    }

    /// <summary>Puts this layout in place of the one with its id - every change the editor makes comes through here.</summary>
    public void Update(OverlayLayout layout)
    {
        lock (_gate)
        {
            var at = _layouts.FindIndex(l => string.Equals(l.Id, layout.Id, StringComparison.Ordinal));

            if (at < 0)
            {
                return;
            }

            var keep = Tidy(layout.Copy());
            keep.Id = _layouts[at].Id;
            _layouts[at] = keep;
            Bump(keep.Id);
        }

        Save();
        Changed?.Invoke();
    }

    /// <summary>Takes a layout off. Its address then says it has gone, in words, rather than showing a blank source.</summary>
    public bool Remove(string id)
    {
        bool removed;

        lock (_gate)
        {
            removed = _layouts.RemoveAll(l => string.Equals(l.Id, id, StringComparison.Ordinal)) > 0;
            Bump(id);
        }

        if (removed)
        {
            Save();
            Changed?.Invoke();
        }

        return removed;
    }

    private void Bump(string id) => _versions[id] = (_versions.TryGetValue(id, out var v) ? v : 0) + 1;

    private void Save()
    {
        if (LoadedUnreadable)
        {
            return;
        }

        string json;

        lock (_gate)
        {
            json = JsonSerializer.Serialize(_layouts, new JsonSerializerOptions { WriteIndented = true });
        }

        try
        {
            SafeFile.WriteAllText(_path, json);
        }
        catch
        {
            // Kept in memory; the next change tries again.
        }
    }

    /// <summary>"my-stream-3f2a": the name as an address, and four hex digits so two of the same name never share one.</summary>
    private string NewId(string name)
    {
        var slug = new string((name ?? string.Empty).ToLowerInvariant()
                .Select(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') ? c : '-')
                .ToArray())
            .Trim('-');

        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        if (slug.Length > 24)
        {
            slug = slug[..24].Trim('-');
        }

        if (slug.Length == 0)
        {
            slug = "overlay";
        }

        string id;

        do
        {
            id = slug + "-" + Guid.NewGuid().ToString("N")[..4];
        }
        while (_layouts.Any(l => l.Id == id) || OverlayLayouts.BuiltIn(id) is not null);

        return id;
    }

    /// <summary>An address part the store makes: lower-case letters, digits and dashes, ending in four hex digits.</summary>
    internal static bool IsCustomId(string? id) =>
        id is { Length: >= 6 and <= 40 }
        && id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')
        && id[^5] == '-';

    /// <summary>The layout with every value inside its range, every element kind at most once, and the mark always on.</summary>
    internal static OverlayLayout Tidy(OverlayLayout layout)
    {
        layout.Name = string.IsNullOrWhiteSpace(layout.Name) ? "My overlay" : layout.Name.Trim();
        layout.About = layout.About ?? string.Empty;
        layout.Glass = double.IsFinite(layout.Glass) ? Math.Clamp(layout.Glass, 0.4, 1) : 0.82;
        layout.TextScale = double.IsFinite(layout.TextScale) ? Math.Clamp(layout.TextScale, 0.8, 1.6) : 1;

        layout.Elements = (layout.Elements ?? new List<OverlayElement>())
            .Where(e => e is not null && Enum.IsDefined(e.Kind) && Enum.IsDefined(e.Anchor))
            .GroupBy(e => e.Kind)
            .Select(g => g.First())
            .ToList();

        foreach (var e in layout.Elements)
        {
            e.X = double.IsFinite(e.X) ? Math.Clamp(e.X, -50, 50) : 2;
            e.Y = double.IsFinite(e.Y) ? Math.Clamp(e.Y, -50, 50) : 2;
            e.Scale = double.IsFinite(e.Scale) ? Math.Clamp(e.Scale, 0.5, 2) : 1;
            e.Rows = Math.Clamp(e.Rows, 0, 20);
            e.During &= OverlayDuring.Staging | OverlayDuring.Racing | OverlayDuring.Finished | OverlayDuring.Between;
        }

        // HeatCaller's mark stays on (the 31 Oct "always on everywhere" pick):
        // movable, never off.
        if (layout.Element(OverlayElementKind.Mark) is { } mark)
        {
            mark.On = true;
            mark.During = OverlayDuring.Always;
        }
        else
        {
            layout.Elements.Add(new OverlayElement { Kind = OverlayElementKind.Mark, Anchor = OverlayAnchor.BottomRight, X = 2, Y = 2 });
        }

        return layout;
    }
}
