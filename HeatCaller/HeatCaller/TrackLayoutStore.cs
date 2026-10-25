using System.Text.Json;

namespace HeatCaller;

/// <summary>Loads/saves TrackLayout records to %AppData%\HeatCaller\track-layouts.json.</summary>
public sealed class TrackLayoutStore
{
    private readonly Dictionary<string, TrackLayout> _layouts;

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "track-layouts.json");

    private TrackLayoutStore(Dictionary<string, TrackLayout> layouts)
    {
        _layouts = layouts;
    }

    public static TrackLayoutStore Load()
    {
        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<TrackLayout>>(SafeFile.ReadAllTextWithRetry(FilePath));
                if (list is not null)
                {
                    // Not ToDictionary: one duplicated or missing id in a
                    // hand-edited or restored file would throw and lose
                    // every layout. Last one wins.
                    var layouts = new Dictionary<string, TrackLayout>();
                    foreach (var t in list)
                    {
                        if (t is not null && !string.IsNullOrEmpty(t.Id))
                        {
                            FillNulls(t);
                            layouts[t.Id] = t;
                        }
                    }
                    return new TrackLayoutStore(layouts);
                }

                // Present, parsed, and not the shape this store writes. Not
                // a new install either, so the same rule applies. A file
                // holding the literal "null" is four bytes and does not
                // throw: it used to fall straight through to the empty store
                // below with the flag left false, so the first layout added
                // tonight - or a lap record set mid-race - replaced every
                // track plan in the file.
                unreadable = true;
            }
        }
        catch (Exception ex)
        {
            // Unreadable file: start empty rather than refusing to open -
            // but, when it could not be read at all or no copy of it could
            // be kept, never save over it this session (see LoadedUnreadable).
            unreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
        }

        return new TrackLayoutStore(new Dictionary<string, TrackLayout>()) { LoadedUnreadable = unreadable };
    }

    /// <summary>
    /// True when the file was there at startup but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. The file holds every track plan,
    /// its attachment, marshal posts, all-time lap record and Demo pace:
    /// nothing anybody could type back in. It used to be loaded as empty and
    /// then written over whole at the first change - adding tonight's track,
    /// or even a new lap record set mid-race - so a backup or sync tool
    /// holding it open for a moment at launch was enough to lose all of it.
    /// What this store holds is then only what was added this session, and
    /// none of it is saved.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    /// <summary>
    /// Puts back what a hand-edited or restored file left as null. The model
    /// promises lists and text, and the pages take it at its word: a null
    /// "MarshalPostNames" threw as soon as the marshal rota asked a post's
    /// name (PostName), and a plan with "Tiles": null (or a null tile in the
    /// list) threw every time the track's picture was drawn.
    /// </summary>
    private static void FillNulls(TrackLayout t)
    {
        t.Name ??= string.Empty;
        t.Notes ??= string.Empty;
        t.RecordHolderName ??= string.Empty;
        t.MarshalPostNames ??= new List<string>();

        // Sector records: a null list, a null record or one for no sector
        // would throw or show as nonsense on the Track Layouts page.
        if (t.SectorRecordsByCut is { } byCut)
        {
            foreach (var cut in byCut.Keys.ToList())
            {
                var kept = (byCut[cut] ?? new List<SectorRecord>())
                    .Where(r => r is not null && r.Sector >= 1 && r.Ms > 0)
                    .ToList();

                foreach (var record in kept)
                {
                    record.Holder ??= string.Empty;
                }

                if (kept.Count == 0)
                {
                    byCut.Remove(cut);
                }
                else
                {
                    byCut[cut] = kept;
                }
            }
        }

        // The plan itself: mended as a plan opened from a .hctrack file is
        // (RcpPlanFile.Repair, 25 Oct 2026), so the two can never disagree.
        if (t.Design is { } design)
        {
            RcpPlanFile.Repair(design);
        }
    }

    /// <summary>
    /// Raised after every save - a layout added, edited, renamed, deleted,
    /// or its track record changed.
    /// </summary>
    /// <remarks>
    /// Raised from Save rather than from Upsert and Delete, because Save is
    /// the one place every change passes through; an edit made in place and
    /// then saved would slip past the other two.
    ///
    /// That includes a new track record set mid-race, so a listener that
    /// rebuilds a list should check whether the list actually changed before
    /// rebuilding it - see MainForm.OnTrackLayoutsChanged.
    ///
    /// Before this existed, anything showing a list of layouts was only as
    /// fresh as the last time something happened to refresh it by hand, and
    /// the Race Control track picker was refreshed from the wrong page
    /// entirely - so a track created on Track Layouts did not appear there
    /// until the app was restarted.
    /// </remarks>
    public event Action? Changed;

    public void Save()
    {
        // Not written while LoadedUnreadable: the real file is almost
        // certainly fine, and saving this session's near-empty set would
        // replace it. The console has already told the operator why and
        // asked for a restart. Changed is still raised, so a layout added
        // this session shows up in the pickers like any other until then.
        if (!LoadedUnreadable)
        {
            var json = JsonSerializer.Serialize(_layouts.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });
            SafeFile.WriteAllText(FilePath, json);
        }

        Changed?.Invoke();
    }

    public IReadOnlyList<TrackLayout> All => _layouts.Values.OrderBy(t => t.Name).ToList();

    public TrackLayout? Get(string id) => _layouts.TryGetValue(id, out var t) ? t : null;

    /// <summary>
    /// True when a layout other than the one with <paramref name="exceptId"/>
    /// already goes by <paramref name="name"/> - compared trimmed and without
    /// regard to case. Pass null for a layout that isn't saved yet.
    /// </summary>
    /// <remarks>
    /// Everything downstream finds a layout by its name alone: the Race
    /// Control track picker lists names, and the all-time record and the
    /// Demo pace are looked up with the first layout whose name matches. Two
    /// layouts called "Club Oval" therefore showed up twice in the picker,
    /// and either entry read and wrote the record of whichever one sorted
    /// first - the other's record was never set, and the first one's could
    /// be overwritten by laps driven on a different layout. Case is ignored
    /// too, because "Club Oval" and "club oval" side by side in a list are
    /// the same track to anyone picking from it.
    /// </remarks>
    public bool IsNameTaken(string name, string? exceptId)
    {
        var wanted = (name ?? string.Empty).Trim();
        if (wanted.Length == 0)
        {
            return false;
        }

        return _layouts.Values.Any(t =>
            t.Id != exceptId
            && string.Equals((t.Name ?? string.Empty).Trim(), wanted, StringComparison.OrdinalIgnoreCase));
    }

    public void Upsert(TrackLayout layout)
    {
        _layouts[layout.Id] = layout;
        Save();
    }

    /// <summary>
    /// Removes a layout. Returns where its attachment folder was set aside
    /// (null when it had none), for an undo to hand to
    /// TrackAttachmentStore.RestoreForTrack.
    /// </summary>
    public string? Delete(string id)
    {
        _layouts.Remove(id);
        Save();

        // Stash rather than destroy - deleting a layout is undoable, and
        // wiping the image here would leave the restored record pointing at
        // a file that no longer exists.
        return TrackAttachmentStore.StashForTrack(id);
    }
}
