namespace HeatCaller;

/// <summary>
/// What the club has in the cupboard: boxed RCP sets and loose parts
/// (Track Designer overhaul, Phase 4, 25 Oct 2026).
///
/// Entered once and kept with the club's settings (AppSettings.TrackStock),
/// not in each plan, because it is the same cupboard for every layout. The
/// designer then shows, part by part, how much of it a plan uses - and a
/// plan drawn past what the club owns is reported, never refused.
///
/// Tiles come in two sizes that do not mix (30 cm and 50 cm), so a box
/// counts only toward plans of its own tile size, and loose parts are kept
/// per size.
///
/// Honest about what isn't known: some of RCP's boxes come with a full
/// parts list and some with only a tile count (RcpKit.BreakdownPublished).
/// A box with only a tile count adds its tiles and nothing else, and the
/// check says the rest of that box couldn't be counted rather than
/// inventing what is in it.
/// </summary>
public sealed class RcpStock
{
    /// <summary>How many of each boxed set, by RcpKit.Name.</summary>
    public Dictionary<string, int> Sets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Loose parts, one entry per tile size.</summary>
    public List<RcpLooseParts> Loose { get; set; } = new();

    /// <summary>Nothing entered at all - the designer then checks nothing against it.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty =>
        Sets.Values.All(count => count <= 0) && Loose.All(parts => parts.IsEmpty);

    public RcpStock Clone() => new()
    {
        Sets = new Dictionary<string, int>(Sets, StringComparer.OrdinalIgnoreCase),
        Loose = Loose.Select(parts => parts.Clone()).ToList(),
    };

    /// <summary>
    /// Mends what a settings file can hand back: nulls, negative counts,
    /// a set no longer sold kept (its name still means what it meant), two
    /// loose entries for one size merged into one.
    /// </summary>
    public RcpStock Tidied()
    {
        var sets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, count) in Sets ?? new Dictionary<string, int>())
        {
            if (!string.IsNullOrWhiteSpace(name) && count > 0)
            {
                sets[name] = (sets.TryGetValue(name, out var was) ? was : 0) + count;
            }
        }

        var loose = new List<RcpLooseParts>();

        foreach (var parts in (Loose ?? new List<RcpLooseParts>()).Where(p => p is not null))
        {
            var clean = parts.Clone().Tidied();

            if (loose.FirstOrDefault(p => Math.Abs(p.TileSizeMm - clean.TileSizeMm) < 1) is { } same)
            {
                same.Add(clean);
            }
            else
            {
                loose.Add(clean);
            }
        }

        return new RcpStock { Sets = sets, Loose = loose };
    }

    /// <summary>The loose parts for a tile size - made, empty, when there are none yet.</summary>
    public RcpLooseParts LooseFor(double tileSizeMm)
    {
        if (Loose.FirstOrDefault(p => Math.Abs(p.TileSizeMm - tileSizeMm) < 1) is { } parts)
        {
            return parts;
        }

        var made = new RcpLooseParts { TileSizeMm = tileSizeMm };
        Loose.Add(made);
        return made;
    }

    /// <summary>
    /// Everything owned for plans of one tile size: the boxes of that size
    /// added up, plus the loose parts of that size.
    /// </summary>
    public RcpOwned OwnedFor(double tileSizeMm)
    {
        var owned = new RcpOwned();

        foreach (var (name, count) in Sets)
        {
            if (count <= 0 || RcpKit.ByName(name) is not { } kit || Math.Abs(kit.TileSizeMm - tileSizeMm) >= 1)
            {
                continue;
            }

            owned.Tiles += kit.TotalTiles * count;

            if (kit.BreakdownPublished)
            {
                owned.OuterCurveTiles += kit.OuterCurves * count;
                owned.InnerCornerTiles += kit.InnerCorners * count;
                owned.StraightRails += kit.StraightRails * count;
                owned.TurnRails += kit.OuterCurveRails * count;
                owned.InnerCornerPieces += kit.InnerCornerPieces * count;
            }
            else
            {
                owned.Unlisted.Add(kit.Name);
            }
        }

        if (Loose.FirstOrDefault(p => Math.Abs(p.TileSizeMm - tileSizeMm) < 1) is { } loose)
        {
            owned.Tiles += loose.Tiles + loose.OuterCurveTiles + loose.InnerCornerTiles;
            owned.OuterCurveTiles += loose.OuterCurveTiles;
            owned.InnerCornerTiles += loose.InnerCornerTiles;
            owned.StraightRails += loose.StraightRails;
            owned.TurnRails += loose.TurnRails;
            owned.InnerCornerPieces += loose.InnerCornerPieces;
            owned.EndCaps += loose.EndCaps;
            owned.EndCapsCounted = loose.EndCaps > 0;
        }

        owned.Anything = owned.Tiles > 0 || owned.StraightRails > 0 || owned.TurnRails > 0
                         || owned.InnerCornerPieces > 0 || owned.EndCaps > 0;

        return owned;
    }

    /// <summary>
    /// A plan against what the club owns, part by part: used, owned, left.
    /// Empty when nothing is owned in the plan's tile size - with nothing
    /// entered there is nothing to be short of, and saying "short of 56
    /// tiles" to a club that simply hasn't filled this in would be false.
    /// </summary>
    public IReadOnlyList<RcpStockLine> Check(RcpInventory used, double tileSizeMm)
    {
        var owned = OwnedFor(tileSizeMm);

        if (!owned.Anything)
        {
            return Array.Empty<RcpStockLine>();
        }

        // A box whose parts RCP doesn't publish adds its tiles and nothing
        // else, so every other part's "owned" is only part of the truth:
        // those rows say what is used and what is counted, and are never
        // called short (review, 25 Oct 2026) - as the kit check never does.
        var partsKnown = owned.Unlisted.Count == 0;

        var lines = new List<RcpStockLine>
        {
            new("Tiles, all kinds", used.Tiles, owned.Tiles),
            new("Outer curve tiles", used.OuterCurveTiles, owned.OuterCurveTiles, partsKnown),
            new("Inner corner tiles", used.InnerCornerTiles, owned.InnerCornerTiles, partsKnown),
            new("Straight rails", used.StraightRails, owned.StraightRails, partsKnown),
            new("Turn rails", used.OuterTurnRails, owned.TurnRails, partsKnown),
            new("Inner corner pieces", used.InnerCornerPieces, owned.InnerCornerPieces, partsKnown),
        };

        // End caps aren't in any box's parts list, so they are only checked
        // once the club has counted some loose.
        if (owned.EndCapsCounted)
        {
            lines.Add(new RcpStockLine("End caps", used.EndCaps, owned.EndCaps));
        }

        return lines;
    }
}

/// <summary>Loose RCP parts of one tile size.</summary>
public sealed class RcpLooseParts
{
    public double TileSizeMm { get; set; } = 500;

    /// <summary>Plain (straight) tiles, blank ones included.</summary>
    public int Tiles { get; set; }

    public int OuterCurveTiles { get; set; }
    public int InnerCornerTiles { get; set; }
    public int StraightRails { get; set; }

    /// <summary>Outer curve rails - the turn rails round the outside of a bend.</summary>
    public int TurnRails { get; set; }

    public int InnerCornerPieces { get; set; }
    public int EndCaps { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty =>
        Tiles <= 0 && OuterCurveTiles <= 0 && InnerCornerTiles <= 0 && StraightRails <= 0
        && TurnRails <= 0 && InnerCornerPieces <= 0 && EndCaps <= 0;

    public RcpLooseParts Clone() => (RcpLooseParts)MemberwiseClone();

    internal RcpLooseParts Tidied()
    {
        Tiles = Math.Max(0, Tiles);
        OuterCurveTiles = Math.Max(0, OuterCurveTiles);
        InnerCornerTiles = Math.Max(0, InnerCornerTiles);
        StraightRails = Math.Max(0, StraightRails);
        TurnRails = Math.Max(0, TurnRails);
        InnerCornerPieces = Math.Max(0, InnerCornerPieces);
        EndCaps = Math.Max(0, EndCaps);
        TileSizeMm = TileSizeMm > 0 ? TileSizeMm : 500;
        return this;
    }

    internal void Add(RcpLooseParts other)
    {
        Tiles += other.Tiles;
        OuterCurveTiles += other.OuterCurveTiles;
        InnerCornerTiles += other.InnerCornerTiles;
        StraightRails += other.StraightRails;
        TurnRails += other.TurnRails;
        InnerCornerPieces += other.InnerCornerPieces;
        EndCaps += other.EndCaps;
    }
}

/// <summary>Everything owned for one tile size, added up.</summary>
public sealed class RcpOwned
{
    public int Tiles { get; set; }
    public int OuterCurveTiles { get; set; }
    public int InnerCornerTiles { get; set; }
    public int StraightRails { get; set; }
    public int TurnRails { get; set; }
    public int InnerCornerPieces { get; set; }
    public int EndCaps { get; set; }
    public bool EndCapsCounted { get; set; }
    public bool Anything { get; set; }

    /// <summary>Boxes owned whose parts RCP doesn't list: their tiles are counted and nothing else.</summary>
    public List<string> Unlisted { get; } = new();
}

/// <summary>One part of a plan against the cupboard.</summary>
public sealed record RcpStockLine(string Part, int Used, int Owned, bool Known = true)
{
    public int Left => Owned - Used;

    /// <summary>Short only when what is owned is fully known - see RcpStock.Check.</summary>
    public bool Short => Known && Used > Owned;
}
