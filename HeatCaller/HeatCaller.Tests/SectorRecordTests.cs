namespace HeatCaller.Tests;

/// <summary>
/// What sector times leave behind: each layout's sector records
/// (TrackLayout.OfferSectors), a driver's best sectors on their card
/// (DriverStats.BestSectors), a saved result's best sectors (Sectors.Bests),
/// and the lap export's sector columns.
/// </summary>
public sealed class SectorRecordTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly DateTime Day = new(2026, 10, 3, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_sector_record_falls_only_to_a_quicker_time_and_says_which_it_beat()
    {
        var track = new TrackLayout { Name = "Oval" };

        // The first sectors set the records - beating nobody.
        Assert.Empty(track.OfferSectors(new long[] { 4_000, 6_000 }, "Maya", Day));

        // Quicker through sector 2 only.
        Assert.Equal(new[] { 2 }, track.OfferSectors(new long[] { 4_100, 5_800 }, "Dev", Day));

        var records = track.CurrentSectorRecords();
        Assert.Equal(2, records.Length);
        Assert.Equal((4_000L, "Maya"), (records[0]!.Ms, records[0]!.Holder));
        Assert.Equal((5_800L, "Dev"), (records[1]!.Ms, records[1]!.Holder));
    }

    [Fact]
    public void Laps_cut_a_different_way_keep_records_of_their_own_and_the_old_ones_are_put_aside()
    {
        var track = new TrackLayout { Name = "Oval" };
        track.OfferSectors(new long[] { 4_000, 6_000 }, "Maya", Day);

        // A second split loop: three sectors, a different cut.
        Assert.Empty(track.OfferSectors(new long[] { 3_000, 3_000, 4_500 }, "Dev", Day));

        // The layout's records now are the three-sector ones...
        var now = track.CurrentSectorRecords();
        Assert.Equal(3, now.Length);
        Assert.Equal(3_000, now[0]!.Ms);

        // ...and the two-sector ones are kept for when the loop comes out.
        Assert.Equal("Maya", track.CurrentSectorRecords(2)[0]!.Holder);
    }

    private static RcpTrackDesign Rectangle(out RcpDecal split)
    {
        var design = new RcpTrackDesign { TileSizeMm = 500, RouteClosed = true };

        foreach (var (x, y) in new[] { (0.5f, 0.5f), (9.5f, 0.5f), (9.5f, 5.5f), (0.5f, 5.5f) })
        {
            design.Route.Add(new RcpRoutePoint { X = x, Y = y });
        }

        design.Decals.Add(new RcpDecal { Col = 2, Row = 0, Kind = RcpDecalKind.FinishLine });
        split = new RcpDecal { Col = 9, Row = 2, Kind = RcpDecalKind.SplitLoop1 };
        design.Decals.Add(split);
        return design;
    }

    [Fact]
    public void Moving_a_split_loop_on_the_plan_puts_the_old_records_aside_until_it_moves_back()
    {
        var track = new TrackLayout { Name = "Oval", Design = Rectangle(out var split) };
        track.OfferSectors(new long[] { 4_000, 6_000 }, "Maya", Day);
        Assert.NotNull(track.CurrentSectorRecords()[0]);

        // The loop moves a tile: the sectors measure different track now.
        split.Row = 3;
        Assert.Null(track.CurrentSectorRecords()[0]);

        // Back where it was: the records count again.
        split.Row = 2;
        Assert.Equal("Maya", track.CurrentSectorRecords()[0]!.Holder);
    }

    [Fact]
    public void Redrawing_the_line_or_changing_the_tile_size_keeps_the_records()
    {
        var design = Rectangle(out _);
        var track = new TrackLayout { Name = "Oval", Design = design };
        track.OfferSectors(new long[] { 4_000, 6_000 }, "Maya", Day);

        design.TileSizeMm = 600;
        design.Route.Insert(1, new RcpRoutePoint { X = 5f, Y = 1.5f });

        Assert.NotNull(track.CurrentSectorRecords()[0]);
    }

    [Fact]
    public void A_sector_a_quarter_quicker_than_its_record_is_not_believed()
    {
        var track = new TrackLayout { Name = "Oval" };
        track.OfferSectors(new long[] { 4_000, 6_000 }, "Maya", Day);

        // 2.9s through a 4.0s sector is a misread; the 5.9s beside it stands.
        Assert.Equal(new[] { 2 }, track.OfferSectors(new long[] { 2_900, 5_900 }, "Dev", Day));

        var records = track.CurrentSectorRecords();
        Assert.Equal((4_000L, "Maya"), (records[0]!.Ms, records[0]!.Holder));
        Assert.Equal((5_900L, "Dev"), (records[1]!.Ms, records[1]!.Holder));
    }

    [Fact]
    public void Records_set_before_the_loops_were_drawn_carry_over_to_them()
    {
        var track = new TrackLayout { Name = "Oval" };
        track.OfferSectors(new long[] { 4_000, 6_000 }, "Maya", Day);

        // The finish line and the split loop go on the plan afterwards.
        track.Design = Rectangle(out _);
        Assert.Equal("Maya", track.CurrentSectorRecords()[0]!.Holder);

        // The next lap moves them under the placement for good.
        Assert.Equal(new[] { 1 }, track.OfferSectors(new long[] { 3_900, 6_100 }, "Dev", Day));
        Assert.Equal("2:2,0;9,2", track.LatestSectorCut);
        Assert.Equal(new[] { "2:2,0;9,2" }, track.SectorRecordsByCut!.Keys);
        Assert.Equal("Maya", track.CurrentSectorRecords()[1]!.Holder);
    }

    [Fact]
    public void The_first_record_of_a_sector_is_judged_by_what_it_usually_takes()
    {
        var track = new TrackLayout { Name = "Oval" };

        // A 10s lap read as 1s + 9s: the split heard too early.
        Assert.Empty(track.OfferSectors(new long[] { 1_000, 9_000 }, "Maya", Day, typical: new long[] { 4_000, 6_000 }));

        var records = track.CurrentSectorRecords();
        Assert.Null(records[0]);
        Assert.Equal(9_000, records[1]!.Ms);
    }

    [Fact]
    public void What_a_sector_usually_takes_needs_three_laps_and_a_misread_cannot_move_it()
    {
        var race = new RaceControlSession { IsDemoMode = true, SplitCount = 1 };
        race.Stage();
        race.GreenFlag(0);

        void Cross(uint at) => race.ProcessLapEvent(new DirectLapEvent { TransponderId = "P1", DeviceTimeMs = at, SignalHits = 5, SignalStrength = 100 });

        Cross(5_000);
        race.ProcessSplitPass("P1", TimingPoint.Split1, 9_000);    // 4.0 + 6.0
        Cross(15_000);
        race.ProcessSplitPass("P1", TimingPoint.Split1, 19_100);   // 4.1 + 5.9
        Cross(25_000);

        Assert.Null(Sectors.Typical(race.GetOrderedStandings(), 2));

        race.ProcessSplitPass("P1", TimingPoint.Split1, 26_000);   // 1.0 + 9.0: misread
        Cross(35_000);

        Assert.Equal(new long[] { 4_000, 6_000 }, Sectors.Typical(race.GetOrderedStandings(), 2));
    }

    [Fact]
    public void An_undo_puts_taken_records_back_without_losing_quicker_ones_set_since()
    {
        var track = new TrackLayout { Name = "Oval" };
        track.OfferSectors(new long[] { 4_000, 6_000 }, "Maya", Day);
        var taken = track.SectorRecordsCopy();

        // Reset, then a heat sets S1 quicker than before and S2 slower.
        track.SectorRecordsByCut = null;
        track.OfferSectors(new long[] { 3_900, 6_500 }, "Dev", Day);

        track.RestoreSectorRecords(taken);

        var records = track.CurrentSectorRecords();
        Assert.Equal((3_900L, "Dev"), (records[0]!.Ms, records[0]!.Holder));
        Assert.Equal((6_000L, "Maya"), (records[1]!.Ms, records[1]!.Holder));
    }

    [Fact]
    public void Bad_sectors_set_no_record()
    {
        var track = new TrackLayout { Name = "Oval" };

        Assert.Empty(track.OfferSectors(new long[] { 4_000 }, "Maya", Day));
        Assert.Empty(track.OfferSectors(new long[] { 4_000, 0 }, "Maya", Day));
        Assert.Null(track.SectorRecordsByCut);
        Assert.False(track.HasSectorRecords());
        Assert.Empty(track.CurrentSectorRecords());
    }

    [Fact]
    public void A_hand_edited_file_with_broken_sector_records_still_loads()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(
            Path.Combine(AppPaths.Root, "track-layouts.json"),
            """
            [ { "Id": "a", "Name": "Oval", "LatestSectorCut": "2",
                "SectorRecordsByCut": { "2": [ null, { "Sector": 0, "Ms": 4000 }, { "Sector": 1, "Ms": 4000, "Holder": null } ],
                                        "3": null } } ]
            """);

        var track = Assert.Single(TrackLayoutStore.Load().All);

        var record = Assert.Single(track.AllSectorRecords());
        Assert.Equal(("2", 1, string.Empty), (record.Cut, record.Record.Sector, record.Record.Holder));
        Assert.Equal(4_000, track.CurrentSectorRecords()[0]!.Ms);
    }

    private static SessionResultEntry Result(string transponder, string name, params (long Lap, long[]? Sectors)[] laps) => new()
    {
        Transponder = transponder,
        DriverName = name,
        Position = 1,
        Laps = laps.Length,
        FastLapMs = laps.Skip(1).Select(l => l.Lap).DefaultIfEmpty(0).Min(),
        LapTimesMs = laps.Select(l => l.Lap).ToList(),
        SectorTimesMs = laps.Select(l => l.Sectors).ToList(),
    };

    [Fact]
    public void A_saved_result_gives_its_best_sectors_but_not_the_start_lap()
    {
        var result = Result("T1", "Asher",
            (2_000, new long[] { 1_000, 1_000 }),     // the start lap: never counts
            (10_000, new long[] { 4_000, 6_000 }),
            (10_100, new long[] { 3_900, 6_200 }),
            (9_999, new long[] { 4_000, 6_000 }));     // doesn't add up: not used

        Assert.Equal(new long[] { 3_900, 6_000 }, Sectors.Bests(result));
    }

    [Fact]
    public void The_driver_card_shows_best_sectors_by_layout_and_the_sector_records_they_hold()
    {
        var session = new SessionRecord
        {
            SessionType = RaceSessionType.Race,
            Label = "Heat",
            TrackLayoutName = "Oval",
            StartedAtUtc = Day,
            EndedAtUtc = Day.AddMinutes(5),
            Results = new List<SessionResultEntry>
            {
                Result("T1", "Asher",
                    (5_000, null),
                    (10_000, new long[] { 4_000, 6_000 }),
                    (10_100, new long[] { 3_900, 6_200 })),
            },
        };

        var stats = DriverStats.Compute(
            new DriverStatsQuery
            {
                Transponder = "T1",
                Transponders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "T1" },
                DriverName = "Asher",
            },
            new DriverStatsSnapshot
            {
                Sessions = new[] { session },
                RoundClasses = new Dictionary<string, string>(),
                Rivalries = Array.Empty<(string A, string B)>(),
                Championships = Array.Empty<(string Season, IReadOnlyList<SeasonStandingEntry> Table)>(),
                SectorRecords = new[] { ("Oval", 1, 3_900L, "Asher"), ("Oval", 2, 5_500L, "Dev") },
            });

        var best = Assert.Single(stats.BestSectors);
        Assert.Equal("Oval", best.Layout);
        Assert.Equal(new long[] { 3_900, 6_000 }, best.Sectors);
        Assert.Equal(9_900, best.IdealMs);

        var held = Assert.Single(stats.SectorRecordsHeld);
        Assert.Equal((1, 3_900L), (held.Sector, held.Ms));
    }

    [Fact]
    public void Best_sectors_on_the_driver_card_leave_out_nights_with_the_loop_elsewhere()
    {
        SessionRecord Night(DateTime when, string cut, params (long Lap, long[]? Sectors)[] laps) => new()
        {
            SessionType = RaceSessionType.Race,
            Label = "Heat",
            TrackLayoutName = "Oval",
            StartedAtUtc = when,
            EndedAtUtc = when.AddMinutes(5),
            SectorCut = cut,
            Results = new List<SessionResultEntry> { Result("T1", "Asher", laps) },
        };

        var stats = DriverStats.Compute(
            new DriverStatsQuery
            {
                Transponder = "T1",
                Transponders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "T1" },
                DriverName = "Asher",
            },
            new DriverStatsSnapshot
            {
                Sessions = new[]
                {
                    // Last week, with the loop on another tile: a short S1.
                    Night(Day.AddDays(-7), "2:2,0;5,0", (5_000, null), (10_000, new long[] { 2_000, 8_000 })),
                    Night(Day, "2:2,0;9,2", (5_000, null), (10_000, new long[] { 4_000, 6_000 })),
                },
                RoundClasses = new Dictionary<string, string>(),
                Rivalries = Array.Empty<(string A, string B)>(),
                Championships = Array.Empty<(string Season, IReadOnlyList<SeasonStandingEntry> Table)>(),
            });

        Assert.Equal(new long[] { 4_000, 6_000 }, Assert.Single(stats.BestSectors).Sectors);
    }

    [Fact]
    public void The_lap_export_gets_a_column_per_sector_only_when_there_are_sectors()
    {
        var profiles = ProfileStore.Load();

        var withSectors = new SessionRecord
        {
            Label = "Heat",
            StartedAtUtc = Day,
            Results = new List<SessionResultEntry>
            {
                Result("T1", "Asher", (5_000, null), (10_000, new long[] { 4_000, 6_000 })),
            },
        };

        var lines = ResultsExporter.Build(ResultsExporter.Format.LapCsv, new[] { withSectors }, null, profiles)
            .TrimStart('﻿')
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.EndsWith("CumulativeSeconds,S1Seconds,S2Seconds", lines[0]);
        Assert.EndsWith("5.000,,", lines[1]);
        Assert.EndsWith("10.000,15.000,4.000,6.000", lines[2]);

        var without = new SessionRecord
        {
            Label = "Heat",
            StartedAtUtc = Day,
            Results = new List<SessionResultEntry> { Result("T1", "Asher", (5_000, null), (10_000, null)) },
        };

        var plain = ResultsExporter.Build(ResultsExporter.Format.LapCsv, new[] { without }, null, profiles).TrimStart('﻿');
        Assert.EndsWith("CumulativeSeconds", plain.Split(Environment.NewLine)[0]);
    }
}
