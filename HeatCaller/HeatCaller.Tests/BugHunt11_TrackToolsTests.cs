namespace HeatCaller.Tests;

/// <summary>
/// Sector records on a plan whose finish line spans a two-tile lane - one
/// FinishLine decal per tile, as "Build me a track" lays it. Moving a split
/// loop must still put the old records aside (5 Nov 2026, bug hunt 11).
/// </summary>
public sealed class BugHunt11_TrackToolsTests
{
    private static readonly DateTime Day = new(2026, 11, 5, 18, 0, 0, DateTimeKind.Utc);

    private static RcpTrackDesign TwoTileFinish(out RcpDecal split)
    {
        var design = new RcpTrackDesign { TileSizeMm = 500, RouteClosed = true };

        foreach (var (x, y) in new[] { (0.5f, 0.5f), (9.5f, 0.5f), (9.5f, 5.5f), (0.5f, 5.5f) })
        {
            design.Route.Add(new RcpRoutePoint { X = x, Y = y });
        }

        design.Decals.Add(new RcpDecal { Col = 2, Row = 0, Kind = RcpDecalKind.FinishLine });
        design.Decals.Add(new RcpDecal { Col = 2, Row = 1, Kind = RcpDecalKind.FinishLine });
        split = new RcpDecal { Col = 9, Row = 2, Kind = RcpDecalKind.SplitLoop1 };
        design.Decals.Add(split);
        return design;
    }

    [Fact]
    public void A_finish_line_two_tiles_wide_still_keys_the_cut_on_where_the_loops_sit()
    {
        var track = new TrackLayout { Name = "Built", Design = TwoTileFinish(out var split) };

        Assert.Equal("2:2,0;9,2", track.CutFor(2));

        split.Row = 3;
        Assert.Equal("2:2,0;9,3", track.CutFor(2));
    }

    [Fact]
    public void Moving_a_split_loop_on_a_two_tile_finish_puts_the_old_records_aside()
    {
        var track = new TrackLayout { Name = "Built", Design = TwoTileFinish(out var split) };
        track.OfferSectors(new long[] { 4_000, 6_000 }, "Maya", Day);
        Assert.NotNull(track.CurrentSectorRecords()[0]);

        split.Row = 3;
        Assert.Null(track.CurrentSectorRecords()[0]);

        split.Row = 2;
        Assert.Equal("Maya", track.CurrentSectorRecords()[0]!.Holder);
    }

    // 5 Nov 2026, Bug hunt 11 review: a record lap saved while the cut was
    // still the bare count keeps its record-pace sectors after the upgrade
    // keys the cut on the tiles - until a lap is run with the loops there,
    // after which moving a loop puts them aside like the sector records.
    [Fact]
    public void A_record_lap_saved_with_the_bare_count_keeps_its_sectors_after_the_upgrade()
    {
        var track = new TrackLayout
        {
            Name = "Built",
            Design = TwoTileFinish(out var split),
            RecordLapMs = 10_000,
            RecordSectorsMs = new List<long> { 4_000, 6_000 },
            RecordSectorCut = "2",
        };

        Assert.Equal(new long[] { 4_000, 6_000 }, track.RecordSectorsAsCutNow(2));

        track.OfferSectors(new long[] { 4_100, 6_100 }, "Maya", Day);
        Assert.Equal("2:2,0;9,2", track.RecordSectorCut);
        Assert.Equal(new long[] { 4_000, 6_000 }, track.RecordSectorsAsCutNow(2));

        split.Row = 3;
        Assert.Null(track.RecordSectorsAsCutNow(2));
    }

    // 5 Nov 2026, Bug hunt 11 review: the finish tile the cut keys on is the
    // top-left one, whatever order the decals were saved in.
    [Fact]
    public void The_cut_does_not_depend_on_the_order_of_the_finish_tiles()
    {
        var design = TwoTileFinish(out _);
        var forward = new TrackLayout { Name = "Built", Design = design }.CutFor(2);
        var measured = RcpPlan.MeasureSectors(design);

        (design.Decals[0], design.Decals[1]) = (design.Decals[1], design.Decals[0]);

        Assert.Equal("2:2,0;9,2", forward);
        Assert.Equal(forward, new TrackLayout { Name = "Built", Design = design }.CutFor(2));
        Assert.Equal(measured!.Value.Sectors, RcpPlan.MeasureSectors(design)!.Value.Sectors);
    }

    [Fact]
    public void Two_split_one_loops_still_leave_the_bare_count()
    {
        var track = new TrackLayout { Name = "Built", Design = TwoTileFinish(out _) };
        track.Design!.Decals.Add(new RcpDecal { Col = 9, Row = 4, Kind = RcpDecalKind.SplitLoop1 });

        Assert.Equal("2", track.CutFor(2));
    }

    // 6 Nov 2026, Bug hunt 11b review: sessions saved before the cut was
    // keyed on the tiles have SectorCut "2"; the first one filed after has
    // "2:2,0;9,2". The best sectors must read the bare count as that first
    // placement - the loops never moved - and still keep a moved loop apart.
    private const string Me = "T3";
    private const string Placed = "2:2,0;9,2";
    private const string Moved = "2:2,0;9,3";

    private static SessionResultEntry RowWithSectors(params long[][] sectorsPerLap)
    {
        var flying = sectorsPerLap.Select(s => s.Sum()).ToArray();

        return new SessionResultEntry
        {
            Transponder = Me,
            DriverName = "Sam",
            Position = 1,
            LapTimesMs = new[] { 20_000L }.Concat(flying).ToList(),
            Laps = flying.Length,
            FastLapMs = flying.Min(),
            SectorTimesMs = new List<long[]?> { null }.Concat(sectorsPerLap.Select(s => (long[]?)s)).ToList(),
        };
    }

    private static SessionRecord Heat(int hour, string cut, params long[][] sectorsPerLap) => new()
    {
        SessionType = RaceSessionType.Race,
        TrackLayoutName = "Built",
        StartedAtUtc = Day.AddHours(hour - 18),
        EndedAtUtc = Day.AddHours(hour - 18).AddMinutes(6),
        SectorCut = cut,
        Results = new List<SessionResultEntry> { RowWithSectors(sectorsPerLap) },
    };

    // Before the upgrade: quick through sector one. After: quick through two.
    private static SessionRecord BeforeUpgrade(int hour) => Heat(hour, "2", new[] { 3_800L, 5_000L }, new[] { 3_800L, 5_000L }, new[] { 3_800L, 5_000L });

    private static SessionRecord After(int hour, string cut) => Heat(hour, cut, new[] { 4_000L, 4_900L }, new[] { 4_000L, 4_900L }, new[] { 4_000L, 4_900L });

    private static IReadOnlyList<string> Card(params SessionRecord[] history) =>
        CoachReport.Lines(new CoachReport.Input(Me, history, Day.Date, utc => utc.Date, _ => string.Empty, null, _ => "7:40 pm"));

    private static DriverStats Stats(params SessionRecord[] history) => DriverStats.Compute(
        new DriverStatsQuery
        {
            Transponder = Me,
            Transponders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Me },
            DriverName = "Sam",
        },
        new DriverStatsSnapshot
        {
            Sessions = history,
            RoundClasses = new Dictionary<string, string>(),
            Rivalries = Array.Empty<(string A, string B)>(),
            Championships = Array.Empty<(string Season, IReadOnlyList<SeasonStandingEntry> Table)>(),
        });

    [Fact]
    public void The_report_card_reads_a_bare_count_session_as_the_first_placement()
    {
        var card = Card(BeforeUpgrade(19), After(20, Placed));

        // Bests 3.800 + 4.900 across both heats: an ideal lap under the 8.800 best.
        Assert.Contains(card, line => line.Contains("ideal lap of 8.700s", StringComparison.Ordinal));

        // The earlier heat's laps lose time in sector two, the later's in
        // sector one (twice as much) - both measured against the same bests.
        Assert.Contains(card, line => line.StartsWith("Sector one is where most of your time goes", StringComparison.Ordinal)
                                      && line.Contains("0.10s", StringComparison.Ordinal));
    }

    [Fact]
    public void The_report_card_keeps_a_bare_count_session_apart_from_a_moved_loop()
    {
        var card = Card(BeforeUpgrade(19), After(20, Placed), After(21, Moved));

        Assert.DoesNotContain(card, line => line.Contains("ideal lap", StringComparison.Ordinal));
        Assert.DoesNotContain(card, line => line.StartsWith("Sector", StringComparison.Ordinal));
    }

    [Fact]
    public void Driver_stats_best_sectors_take_in_the_nights_before_the_upgrade()
    {
        var stats = Stats(BeforeUpgrade(19), After(20, Placed));

        Assert.Equal(new long[] { 3_800, 4_900 }, Assert.Single(stats.BestSectors).Sectors);
    }

    [Fact]
    public void Driver_stats_best_sectors_keep_a_moved_loop_apart_from_the_bare_count()
    {
        var stats = Stats(BeforeUpgrade(19), After(20, Placed), After(21, Moved));

        Assert.Equal(new long[] { 4_000, 4_900 }, Assert.Single(stats.BestSectors).Sectors);
    }
}
