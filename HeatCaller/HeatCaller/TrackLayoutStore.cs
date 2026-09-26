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
            }
        }
        catch (Exception ex)
        {
            // Corrupt/unreadable file: start fresh rather than crashing on launch.
            SafeFile.PreserveUnreadable(FilePath, ex);
        }

        return new TrackLayoutStore(new Dictionary<string, TrackLayout>());
    }

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

        if (t.Design is { } design)
        {
            design.Tiles = (design.Tiles ?? new List<RcpTile>()).Where(x => x is not null).ToList();
            design.Walls = (design.Walls ?? new List<RcpWall>()).Where(x => x is not null).ToList();
            design.Curves = (design.Curves ?? new List<RcpCurve>()).Where(x => x is not null).ToList();
            design.Decals = (design.Decals ?? new List<RcpDecal>()).Where(x => x is not null).ToList();
            design.Route = (design.Route ?? new List<RcpRoutePoint>()).Where(x => x is not null).ToList();
            design.KitName ??= string.Empty;
            design.BackdropPath ??= string.Empty;

            foreach (var tile in design.Tiles)
            {
                tile.Label ??= string.Empty;
            }
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
        var json = JsonSerializer.Serialize(_layouts.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });
        SafeFile.WriteAllText(FilePath, json);

        Changed?.Invoke();
    }

    public IReadOnlyList<TrackLayout> All => _layouts.Values.OrderBy(t => t.Name).ToList();

    public TrackLayout? Get(string id) => _layouts.TryGetValue(id, out var t) ? t : null;

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
