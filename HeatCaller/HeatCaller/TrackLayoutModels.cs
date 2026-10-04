namespace HeatCaller;

/// <summary>The quickest time anyone has gone through one sector of a layout - see TrackLayout.SectorRecordsByCut.</summary>
public sealed class SectorRecord
{
    /// <summary>Which sector, from 1.</summary>
    public int Sector { get; set; }

    public long Ms { get; set; }

    public string Holder { get; set; } = string.Empty;

    public DateTime? SetUtc { get; set; }

    public SectorRecord Clone() => new() { Sector = Sector, Ms = Ms, Holder = Holder, SetUtc = SetUtc };
}

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
    /// How the lap was cut (<see cref="CutFor"/>) when RecordSectorsMs were
    /// taken - where the split loops sat. Null for a record kept before this
    /// was, which is taken as it always was.
    ///
    /// Without it, moving a split loop left "record pace at the split"
    /// comparing tonight's times to the loop's old place: a loop moved
    /// nearer the line had every car "up 2.9 seconds on the record pace".
    /// </summary>
    public string? RecordSectorCut { get; set; }

    /// <summary>
    /// The length of each sector in feet, from the split loops placed on the
    /// layout's plan and the racing line through them (RcpPlan.MeasureSectors)
    /// - or null without a plan that measures.
    /// </summary>
    public double[]? SectorLengthsFeet() =>
        Design is { } design && RcpPlan.MeasureSectors(design) is { Sectors: { } sectors }
            ? sectors.Select(tiles => RcpPlan.TilesToFeet(design, tiles)).ToArray()
            : null;

    /// <summary>
    /// The quickest anyone has ever gone through each sector of this layout -
    /// the line to Split 1, and so on - in a race, kept apart for each way
    /// the lap has been cut (<see cref="CutFor"/>). Move a split loop and
    /// the records set with it where it was are put aside, not lost: move it
    /// back and they count again. Null before any.
    /// </summary>
    public Dictionary<string, List<SectorRecord>>? SectorRecordsByCut { get; set; }

    /// <summary>The cut of the last lap offered (<see cref="OfferSectors"/>) - which records are this layout's now. Null before any.</summary>
    public string? LatestSectorCut { get; set; }

    /// <summary>A sector this much quicker than its record isn't believed: a quarter off a sector is a misread, not a drive.</summary>
    public const double LeastBelievableSectorShare = 0.75;

    /// <summary>
    /// How a lap of <paramref name="sectors"/> sectors is cut on this layout:
    /// the number of sectors and, when the plan has its finish line and the
    /// split loops for them on it (one of each), the tiles they sit on - so
    /// records set with a loop on one tile are not compared with times from
    /// another. Redrawing the racing line or changing the tile size leaves
    /// the cut alone: the loops still measure the same stretch of track.
    /// Records set before the loops were on the plan carry over to the
    /// first placement a lap is offered with (OfferSectors).
    /// </summary>
    public string CutFor(int sectors)
    {
        var count = sectors.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (sectors < 2 || sectors > 4 || Design?.Decals is not { } decals)
        {
            return count;
        }

        var kinds = new[] { RcpDecalKind.FinishLine, RcpDecalKind.SplitLoop1, RcpDecalKind.SplitLoop2, RcpDecalKind.SplitLoop3 };
        var cells = new List<string>();

        foreach (var kind in kinds.Take(sectors))
        {
            var marked = decals.Where(d => d is not null && d.Kind == kind).ToList();

            if (marked.Count != 1)
            {
                return count;
            }

            cells.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{marked[0].Col},{marked[0].Row}"));
        }

        return count + ":" + string.Join(";", cells);
    }

    /// <summary>
    /// The cut to save with a session of these results on <paramref name="layout"/>
    /// (null for none chosen): for the sector count of the latest lap with
    /// sectors - or null when no lap has any.
    /// </summary>
    public static string? CutOfResults(TrackLayout? layout, IEnumerable<SessionResultEntry> results)
    {
        var count = results
            .Select(r => Enumerable.Range(0, r.LapTimesMs.Count).Reverse().Select(r.SectorsOf).FirstOrDefault(s => s is not null)?.Length ?? 0)
            .DefaultIfEmpty(0)
            .Max();

        return count < 2
            ? null
            : layout?.CutFor(count) ?? count.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>How many sectors a cut has: the number it starts with, or 0.</summary>
    public static int CountOfCut(string? cut)
    {
        if (string.IsNullOrEmpty(cut))
        {
            return 0;
        }

        var colon = cut.IndexOf(':');
        var head = colon < 0 ? cut : cut[..colon];

        return int.TryParse(head, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count) ? count : 0;
    }

    /// <summary>Whether any sector record is kept, under any cut.</summary>
    public bool HasSectorRecords() =>
        SectorRecordsByCut is { } byCut && byCut.Values.Any(list => list is not null && list.Any(r => r is not null));

    /// <summary>Every sector record kept, under every cut - with the number of sectors its cut has.</summary>
    public IEnumerable<(string Cut, int Count, SectorRecord Record)> AllSectorRecords()
    {
        if (SectorRecordsByCut is null)
        {
            yield break;
        }

        foreach (var (cut, list) in SectorRecordsByCut)
        {
            if (list is null)
            {
                continue;
            }

            foreach (var record in list)
            {
                if (record is not null)
                {
                    yield return (cut, CountOfCut(cut), record);
                }
            }
        }
    }

    /// <summary>
    /// The layout's sector records now: those for the number of sectors the
    /// last lap offered had, cut the way the plan cuts the lap now - by
    /// sector (index 0 = sector 1), null where there is none. Empty before
    /// any lap has been offered.
    /// </summary>
    public SectorRecord?[] CurrentSectorRecords() => CurrentSectorRecords(CountOfCut(LatestSectorCut));

    /// <summary>The sector records for laps cut into this many sectors the way the lap is cut now, by sector (index 0 = sector 1), or null where there is none.</summary>
    public SectorRecord?[] CurrentSectorRecords(int sectors)
    {
        var records = new SectorRecord?[Math.Clamp(sectors, 0, 4)];

        if (sectors < 2 || sectors > 4 || SectorRecordsByCut is null)
        {
            return records;
        }

        // Records set before the loops were on the plan count for the
        // first placement drawn - see OfferSectors, which moves them there.
        var bare = sectors.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if ((!SectorRecordsByCut.TryGetValue(CutFor(sectors), out var list) || list is null)
            && (!SectorRecordsByCut.TryGetValue(bare, out list) || list is null))
        {
            return records;
        }

        foreach (var record in list)
        {
            if (record is not null && record.Sector >= 1 && record.Sector <= sectors && record.Ms > 0
                && (records[record.Sector - 1] is not { } other || other.Ms > record.Ms))
            {
                records[record.Sector - 1] = record;
            }
        }

        return records;
    }

    /// <summary>
    /// A lap's sectors, set by <paramref name="holder"/>: each one quicker
    /// than the sector's record (or with none yet) becomes the record, for
    /// the way the lap is cut now. Returns the sectors (from 1) whose record
    /// it beat - not the ones it was simply the first to set. A sector more
    /// than a quarter quicker than its record, or than <paramref name="typical"/>
    /// (what the sector usually takes, when known - Sectors.Typical), is
    /// left alone as a misread (<see cref="LeastBelievableSectorShare"/>):
    /// the first record of a sector has nothing else to be judged by.
    /// </summary>
    public IReadOnlyList<int> OfferSectors(long[] sectors, string holder, DateTime whenUtc, long[]? typical = null)
    {
        var beaten = new List<int>();

        if (sectors.Length < 2 || sectors.Length > 4 || sectors.Any(ms => ms <= 0))
        {
            return beaten;
        }

        var cut = CutFor(sectors.Length);
        SectorRecordsByCut ??= new Dictionary<string, List<SectorRecord>>(StringComparer.Ordinal);

        // The split loops put on the plan for the first time: the records
        // set before (kept under the bare count) were set with the loops
        // where they have just been drawn, so they carry over to them.
        var bare = sectors.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (cut != bare
            && !SectorRecordsByCut.ContainsKey(cut)
            && SectorRecordsByCut.Remove(bare, out var earlier)
            && earlier is not null)
        {
            SectorRecordsByCut[cut] = earlier;
        }

        var current = CurrentSectorRecords(sectors.Length);
        LatestSectorCut = cut;

        if (!SectorRecordsByCut.TryGetValue(cut, out var list) || list is null)
        {
            list = new List<SectorRecord>();
            SectorRecordsByCut[cut] = list;
        }

        for (var i = 0; i < sectors.Length; i++)
        {
            if (current[i] is { } record
                && (record.Ms <= sectors[i] || sectors[i] < record.Ms * LeastBelievableSectorShare))
            {
                continue;
            }

            var hasUsual = typical is { } known && known.Length == sectors.Length && known[i] > 0;

            if (hasUsual && sectors[i] < typical![i] * LeastBelievableSectorShare)
            {
                continue;
            }

            // Nothing to judge it by at all - no record for this sector yet,
            // and too few laps through it this session to say what is usual
            // (Sectors.Typical needs three). Both checks above used to be
            // skipped then, so a cut's very first sector laps went straight
            // in: one split heard two seconds early became a 1.00 s record no
            // real sector could ever beat, kept on the layout for good. With
            // nothing else to go by, the lap itself is the yardstick: a sector
            // that is a sliver of its own lap (under a quarter of an even
            // share) is not taken as a first record. A loop really placed
            // that close to the line still gets its record a few laps in,
            // once the usual times are there to judge it by.
            if (current[i] is null && !hasUsual && sectors[i] < sectors.Sum() / (sectors.Length * 4.0))
            {
                continue;
            }

            if (current[i] is not null)
            {
                beaten.Add(i + 1);
            }

            list.RemoveAll(r => r is null || r.Sector == i + 1);
            list.Add(new SectorRecord { Sector = i + 1, Ms = sectors[i], Holder = holder, SetUtc = whenUtc });
        }

        list.Sort((a, b) => a.Sector.CompareTo(b.Sector));
        return beaten;
    }

    /// <summary>
    /// Puts sector records taken away back (an undo): each under its own
    /// cut, where that sector has no record now or a slower one.
    /// </summary>
    public void RestoreSectorRecords(Dictionary<string, List<SectorRecord>>? taken)
    {
        if (taken is null)
        {
            return;
        }

        SectorRecordsByCut ??= new Dictionary<string, List<SectorRecord>>(StringComparer.Ordinal);

        foreach (var (cut, records) in taken)
        {
            if (records is null)
            {
                continue;
            }

            if (!SectorRecordsByCut.TryGetValue(cut, out var list) || list is null)
            {
                list = new List<SectorRecord>();
                SectorRecordsByCut[cut] = list;
            }

            foreach (var record in records.Where(r => r is not null))
            {
                var now = list.FirstOrDefault(r => r is not null && r.Sector == record.Sector);

                if (now is null || now.Ms > record.Ms)
                {
                    list.RemoveAll(r => r is null || r.Sector == record.Sector);
                    list.Add(record.Clone());
                }
            }

            list.Sort((a, b) => a.Sector.CompareTo(b.Sector));
        }
    }

    /// <summary>A copy of the sector records under every cut, for an undo to put back - or null without any.</summary>
    public Dictionary<string, List<SectorRecord>>? SectorRecordsCopy() =>
        SectorRecordsByCut?.ToDictionary(
            pair => pair.Key,
            pair => (pair.Value ?? new List<SectorRecord>()).Where(r => r is not null).Select(r => r.Clone()).ToList(),
            StringComparer.Ordinal);

    /// <summary>The record lap's sectors, when they still belong to it.</summary>
    public long[]? RecordSectors() => Sectors.Checked(RecordSectorsMs?.ToArray(), RecordLapMs);

    /// <summary>
    /// The record lap's sectors when they still belong to it AND were taken
    /// with the split loops where they are now (RecordSectorCut) - or null.
    /// </summary>
    public long[]? RecordSectorsAsCutNow(int sectors) =>
        RecordSectors() is { } record
        && record.Length == sectors
        && (RecordSectorCut is null || RecordSectorCut == CutFor(sectors))
            ? record
            : null;

    /// <summary>Keeps a record lap's sectors with the cut they were taken with - null clears both.</summary>
    public void KeepRecordSectors(IReadOnlyList<long>? sectors)
    {
        RecordSectorsMs = sectors?.ToList();
        RecordSectorCut = sectors is { Count: >= 2 } ? CutFor(sectors.Count) : null;
    }

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
