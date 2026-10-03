namespace HeatCaller;

/// <summary>
/// A user-defined track layout (Mini-Z tracks are frequently reconfigured,
/// so this just lets the user name/describe whichever layout is currently
/// set up - there's no way to read this from the Neon Timing API itself).
/// </summary>
public sealed class TrackLayout
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// How many corner marshal posts this layout needs. Zero means marshals
    /// aren't tracked for it. Lives on the layout rather than the class,
    /// because the number of posts is a property of the track - every class
    /// racing on it needs the same corners covered.
    /// </summary>
    public int MarshalPostCount { get; set; }

    /// <summary>
    /// Optional names for the posts - "Hairpin", "Sweeper", "Back straight".
    /// Falls back to "Corner 1", "Corner 2" and so on when empty, since a
    /// numbered post is more useful than none at all.
    /// </summary>
    public List<string> MarshalPostNames { get; set; } = new();

    /// <summary>The post's name, or a numbered fallback.</summary>
    public string PostName(int index) =>
        index >= 0 && index < MarshalPostNames.Count && !string.IsNullOrWhiteSpace(MarshalPostNames[index])
            ? MarshalPostNames[index]
            : $"Corner {index + 1}";

    // All-time fastest lap ever recorded on this track layout, across every
    // session, independent of any single race's own "fastest lap of the
    // race". RecordLapMs of 0 means no record has been set yet.
    public long RecordLapMs { get; set; }
    public string RecordHolderName { get; set; } = string.Empty;
    public DateTime? RecordSetUtc { get; set; }

    /// <summary>
    /// The record lap's sector times, when it was set with a split loop -
    /// for "record pace at the split". Only used while they add up to
    /// RecordLapMs (Sectors.Checked), so a record changed any other way
    /// leaves them unused rather than wrong. Null without.
    /// </summary>
    public List<long>? RecordSectorsMs { get; set; }

    /// <summary>
    /// The length of each sector in feet, from the split loops placed on the
    /// layout's plan and the racing line through them (RcpPlan.MeasureSectors)
    /// - or null without a plan that measures.
    /// </summary>
    public double[]? SectorLengthsFeet() =>
        Design is { } design && RcpPlan.MeasureSectors(design) is { Sectors: { } sectors }
            ? sectors.Select(tiles => RcpPlan.TilesToFeet(design, tiles)).ToArray()
            : null;

    /// <summary>The record lap's sectors, when they still belong to it.</summary>
    public long[]? RecordSectors() => Sectors.Checked(RecordSectorsMs?.ToArray(), RecordLapMs);

    // Optional reference figures for this track, used by Demo Mode to
    // simulate a realistic pace instead of an arbitrary made-up one.
    // AverageLapSeconds is the one that actually drives simulated pacing;
    // TrackLengthFeet is purely informational (no assumed speed to convert
    // it into a lap time). Null/0 means "not set".
    public double? AverageLapSeconds { get; set; }
    public double? TrackLengthFeet { get; set; }

    /// <summary>
    /// Original filename of an attached photo or drawing (track map, CAD
    /// export, phone photo of the layout, whatever), or null if none is
    /// attached. The actual file lives on disk under
    /// %AppData%\HeatCaller\TrackAttachments\{Id}\ - only the name
    /// is stored here, kept small and out of the JSON settings file.
    /// </summary>
    public string? AttachmentFileName { get; set; }

    /// <summary>
    /// The tile-by-tile plan of this layout, drawn in the track designer.
    ///
    /// Kept with the layout rather than in a file of its own: the plan IS
    /// the layout - the tiles, the barriers and the line round them - and
    /// splitting it out would mean a track and its plan could be separated
    /// by a rename, a restore or a backup.
    ///
    /// Null for every track that predates the designer, and for any track
    /// somebody has only ever photographed.
    /// </summary>
    public RcpTrackDesign? Design { get; set; }

    /// <summary>
    /// Which picture stands for this track: the drawn plan, or the photo
    /// somebody attached.
    ///
    /// Both are kept - a plan says what the layout IS and a photo says what
    /// it looked like on the night, and neither replaces the other. This
    /// only decides which one gets shown where a single image is wanted.
    /// </summary>
    public bool PreferPlanImage { get; set; }
}

/// <summary>
/// One place that answers "what picture goes with this track".
///
/// Every page that shows a track used to load the attached photo directly,
/// which meant a track with a drawn plan and no photo showed nothing at all,
/// and adding the choice between the two would have meant changing each of
/// them separately.
/// </summary>
public static class TrackImagery
{
    /// <summary>
    /// The track's main picture, or null if it has neither.
    ///
    /// Falls back rather than refusing: a track with only a plan shows the
    /// plan whatever the preference says, and a track with only a photo
    /// shows the photo.
    /// </summary>
    public static Image? MainPicture(TrackLayout track, Size size, Color background)
    {
        var hasPlan = track.Design is { IsEmpty: false };

        if (track.PreferPlanImage && hasPlan)
        {
            return RcpTrackPainter.Render(track.Design!, size, background);
        }

        var photo = TrackAttachmentStore.LoadPreviewImage(track);

        if (photo is not null)
        {
            return photo;
        }

        return hasPlan ? RcpTrackPainter.Render(track.Design!, size, background) : null;
    }

    /// <summary>What the main picture currently is, for a caption or a radio button.</summary>
    public static string Describe(TrackLayout track)
    {
        var hasPlan = track.Design is { IsEmpty: false };
        var hasPhoto = !string.IsNullOrWhiteSpace(track.AttachmentFileName);

        if (!hasPlan && !hasPhoto)
        {
            return "no picture yet";
        }

        if (track.PreferPlanImage && hasPlan)
        {
            return "showing the plan";
        }

        return hasPhoto ? "showing the photo" : "showing the plan";
    }
}
