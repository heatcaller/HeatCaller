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

        var made = new RcpLooseParts { TileSizeMm = tileSizeMm, PiecesVersion = RcpLooseParts.CurrentPieces };
        Loose.Add(made);
        return made;
    }

    /// <summary>
    /// Everything owned for plans of one tile size, as PIECES (railed tiles,
    /// Phase 4, 13 Nov 2026): the rails stay on their tiles between race
    /// nights (Asher, 7 Nov 2026), so the cupboard holds rail tiles, not
    /// tiles and rails. A boxed set still comes as loose parts, and counts
    /// as the pieces it makes (KitPieces); the loose parts entered are
    /// pieces already.
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

            if (!kit.BreakdownPublished)
            {
                // Only a tile count: tiles, and nothing said about which.
                owned.Tiles += kit.TotalTiles * count;
                owned.UnsortedTiles += kit.TotalTiles * count;
                owned.Unlisted.Add(kit.Name);
                continue;
            }

            var pieces = KitPieces(kit);

            for (var i = 0; i < count; i++)
            {
                owned.Add(pieces);
            }

            if (pieces.StraightRails > 0 && !owned.Notes.Contains(kit.Name))
            {
                owned.Notes.Add(kit.Name);
            }
        }

        if (Loose.FirstOrDefault(p => Math.Abs(p.TileSizeMm - tileSizeMm) < 1) is { } loose)
        {
            owned.Add(loose);
            owned.EndCapsCounted = loose.EndCaps > 0;
        }

        owned.Anything = owned.Tiles > 0 || owned.StraightRails > 0 || owned.TurnRails > 0
                         || owned.CornerPieces > 0 || owned.EndCaps > 0;

        return owned;
    }

    /// <summary>
    /// The pieces one boxed set makes: each start/finish tile and then each
    /// straight tile with a straight rail bolted on, the rest of the
    /// straight tiles plain; each outer curve tile with its turn rail; each
    /// inner corner tile with its piece. What is over - more rails than
    /// tiles to put them on - stays loose, and the stock check says so.
    /// </summary>
    public static RcpLooseParts KitPieces(RcpKit kit)
    {
        var rails = kit.StraightRails;
        var finish = Math.Min(kit.StartFinish, rails);
        rails -= finish;
        var railTiles = Math.Min(kit.Straights, rails);
        rails -= railTiles;

        return new RcpLooseParts
        {
            TileSizeMm = kit.TileSizeMm,
            Tiles = kit.Straights - railTiles + (kit.StartFinish - finish),
            FinishTiles = finish,
            RailTiles = railTiles,
            OuterCurveTiles = kit.OuterCurves,
            InnerCornerTiles = kit.InnerCorners,
            StraightRails = rails,
            TurnRails = Math.Max(0, kit.OuterCurveRails - kit.OuterCurves),
            InnerCornerPieces = Math.Max(0, kit.InnerCornerPieces - kit.InnerCorners),
            PiecesVersion = RcpLooseParts.CurrentPieces,
        };
    }

    /// <summary>
    /// A plan against what the club owns, piece for piece: used, owned,
    /// left - "2 more rail tiles", not "2 more tiles and 2 more straight
    /// rails". Empty when nothing is owned in the plan's tile size: with
    /// nothing entered there is nothing to be short of, and saying "short of
    /// 56 tiles" to a club that simply hasn't filled this in would be false.
    /// A piece the plan doesn't use and the club doesn't own isn't listed.
    /// </summary>
    public IReadOnlyList<RcpStockLine> Check(RcpInventory used, double tileSizeMm)
    {
        var owned = OwnedFor(tileSizeMm);

        if (!owned.Anything)
        {
            return Array.Empty<RcpStockLine>();
        }

        // A box whose parts RCP doesn't publish adds tiles and nothing else,
        // so every piece's "owned" is only part of the truth: those rows say
        // what is used and what is counted, and are never called short
        // (review, 25 Oct 2026) - as the kit check never does.
        var known = owned.Unlisted.Count == 0;
        var pieces = used.Pieces;

        var lines = new List<RcpStockLine>
        {
            new("Tiles, all kinds", used.Tiles, owned.Tiles),
        };

        void Line(string part, int needed, int have, bool always = false)
        {
            if (always || needed > 0 || have > 0)
            {
                lines.Add(new RcpStockLine(part, needed, have, known));
            }
        }

        // A blank tile is floor with no bolt holes, so a plain one does in
        // its place: they are one line.
        Line("Plain and blank tiles", pieces.Plain + pieces.Blank, owned.PlainTiles, always: true);

        // A finish rail tile the plan has no finish line for is a rail tile all
        // the same: the spare ones count towards the rail tiles.
        var spareFinish = Math.Max(0, owned.FinishTiles - pieces.Finish);

        Line("Rail tiles", pieces.Rail, owned.RailTiles + Math.Min(spareFinish, Math.Max(0, pieces.Rail - owned.RailTiles)), always: true);
        Line("Finish rail tiles", pieces.Finish, owned.FinishTiles);
        Line("Arched rail tiles", pieces.Arched, owned.ArchedTiles);
        Line("Outer curve tiles", pieces.OuterCurve, owned.OuterCurveTiles, always: true);
        Line("Inner corner tiles", pieces.InnerCorner, owned.InnerCornerTiles, always: true);

        // The loose rail parts: a second rail on a tile, or a rail with no
        // tile, is a straight rail off its own; a turn no tile carries is a
        // turn rail; dividers' ends and rail posts take the corner piece.
        Line("Loose straight rails", pieces.SecondRails + pieces.LooseRails, owned.StraightRails);
        Line("Loose turn rails", pieces.LooseTurns, owned.TurnRails);
        Line("Corner rail pieces", used.PostQuarters + used.DividerEndQuarters, owned.CornerPieces);

        // End caps aren't in any box's parts list, so they are only checked
        // once the club has counted some loose.
        if (owned.EndCapsCounted)
        {
            lines.Add(new RcpStockLine("End caps", used.EndCaps, owned.EndCaps));
        }

        return lines;
    }
}

/// <summary>
/// RCP pieces of one tile size, as the cupboard holds them: tiles with their
/// rails on (railed tiles, Phase 4, 13 Nov 2026), and the rail parts that
/// are loose.
/// </summary>
public sealed class RcpLooseParts
{
    /// <summary>The PiecesVersion a file saved now has: its counts are pieces.</summary>
    public const int CurrentPieces = 1;

    public double TileSizeMm { get; set; } = 500;

    /// <summary>Plain tiles - no rail on - blank ones included.</summary>
    public int Tiles { get; set; }

    /// <summary>Tiles with a straight rail bolted on.</summary>
    public int RailTiles { get; set; }

    /// <summary>Tiles with the finish-line rail bolted on.</summary>
    public int FinishTiles { get; set; }

    /// <summary>Tiles with the arched rail bolted on.</summary>
    public int ArchedTiles { get; set; }

    /// <summary>Outer curve tiles, each with its turn rail.</summary>
    public int OuterCurveTiles { get; set; }

    /// <summary>Inner corner tiles, each with its corner piece.</summary>
    public int InnerCornerTiles { get; set; }

    /// <summary>Straight rails not on a tile.</summary>
    public int StraightRails { get; set; }

    /// <summary>Turn rails not on an outer curve tile.</summary>
    public int TurnRails { get; set; }

    /// <summary>Corner rail pieces (the inner corner piece) not on a tile - for divider ends and rail posts.</summary>
    public int InnerCornerPieces { get; set; }

    public int EndCaps { get; set; }

    /// <summary>
    /// 0 for a file saved before the cupboard was counted in pieces, when
    /// tiles and rails were counted apart. Tidied turns that into pieces:
    /// each straight rail paired with a plain tile, and anything left over
    /// shown as it is.
    /// </summary>
    public int PiecesVersion { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty =>
        Tiles <= 0 && RailTiles <= 0 && FinishTiles <= 0 && ArchedTiles <= 0 && OuterCurveTiles <= 0 && InnerCornerTiles <= 0
        && StraightRails <= 0 && TurnRails <= 0 && InnerCornerPieces <= 0 && EndCaps <= 0;

    public RcpLooseParts Clone() => (RcpLooseParts)MemberwiseClone();

    internal RcpLooseParts Tidied()
    {
        Tiles = Math.Max(0, Tiles);
        RailTiles = Math.Max(0, RailTiles);
        FinishTiles = Math.Max(0, FinishTiles);
        ArchedTiles = Math.Max(0, ArchedTiles);
        OuterCurveTiles = Math.Max(0, OuterCurveTiles);
        InnerCornerTiles = Math.Max(0, InnerCornerTiles);
        StraightRails = Math.Max(0, StraightRails);
        TurnRails = Math.Max(0, TurnRails);
        InnerCornerPieces = Math.Max(0, InnerCornerPieces);
        EndCaps = Math.Max(0, EndCaps);
        TileSizeMm = TileSizeMm > 0 ? TileSizeMm : 500;

        if (PiecesVersion < CurrentPieces)
        {
            // Counted before the pieces: the rails went on the tiles, so
            // pair them up. Each outer curve tile takes its turn rail and
            // each inner corner tile its piece off the loose counts.
            var paired = Math.Min(StraightRails, Tiles);
            RailTiles += paired;
            Tiles -= paired;
            StraightRails -= paired;
            TurnRails = Math.Max(0, TurnRails - OuterCurveTiles);
            InnerCornerPieces = Math.Max(0, InnerCornerPieces - InnerCornerTiles);
            PiecesVersion = CurrentPieces;
        }

        return this;
    }

    internal void Add(RcpLooseParts other)
    {
        Tiles += other.Tiles;
        RailTiles += other.RailTiles;
        FinishTiles += other.FinishTiles;
        ArchedTiles += other.ArchedTiles;
        OuterCurveTiles += other.OuterCurveTiles;
        InnerCornerTiles += other.InnerCornerTiles;
        StraightRails += other.StraightRails;
        TurnRails += other.TurnRails;
        InnerCornerPieces += other.InnerCornerPieces;
        EndCaps += other.EndCaps;
    }
}

/// <summary>Every piece owned for one tile size, added up.</summary>
public sealed class RcpOwned
{
    /// <summary>Every tile, of any piece.</summary>
    public int Tiles { get; set; }

    /// <summary>Tiles from boxes that list only a tile count, so not sorted into pieces.</summary>
    public int UnsortedTiles { get; set; }

    public int RailTiles { get; set; }
    public int FinishTiles { get; set; }
    public int ArchedTiles { get; set; }
    public int OuterCurveTiles { get; set; }
    public int InnerCornerTiles { get; set; }

    /// <summary>Plain and blank tiles: every sorted tile that carries nothing.</summary>
    public int PlainTiles => Tiles - UnsortedTiles - RailTiles - FinishTiles - ArchedTiles - OuterCurveTiles - InnerCornerTiles;

    /// <summary>Loose straight rails.</summary>
    public int StraightRails { get; set; }

    /// <summary>Loose turn rails.</summary>
    public int TurnRails { get; set; }

    /// <summary>Loose corner rail pieces.</summary>
    public int CornerPieces { get; set; }

    public int EndCaps { get; set; }
    public bool EndCapsCounted { get; set; }
    public bool Anything { get; set; }

    /// <summary>Boxes owned whose parts RCP doesn't list: their tiles are counted and nothing else.</summary>
    public List<string> Unlisted { get; } = new();

    /// <summary>Boxes owned with more straight rails than tiles to bolt them to: the rest are counted loose.</summary>
    public List<string> Notes { get; } = new();

    internal void Add(RcpLooseParts parts)
    {
        Tiles += parts.Tiles + parts.RailTiles + parts.FinishTiles + parts.ArchedTiles + parts.OuterCurveTiles + parts.InnerCornerTiles;
        RailTiles += parts.RailTiles;
        FinishTiles += parts.FinishTiles;
        ArchedTiles += parts.ArchedTiles;
        OuterCurveTiles += parts.OuterCurveTiles;
        InnerCornerTiles += parts.InnerCornerTiles;
        StraightRails += parts.StraightRails;
        TurnRails += parts.TurnRails;
        CornerPieces += parts.InnerCornerPieces;
        EndCaps += parts.EndCaps;
    }
}

/// <summary>One part of a plan against the cupboard.</summary>
public sealed record RcpStockLine(string Part, int Used, int Owned, bool Known = true)
{
    public int Left => Owned - Used;

    /// <summary>Short only when what is owned is fully known - see RcpStock.Check.</summary>
    public bool Short => Known && Used > Owned;
}
