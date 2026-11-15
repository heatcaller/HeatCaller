using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 20: a race line named a transponder number as the car held off;
/// the night's finish left out a folded-in class's finals; a divider with a
/// finish or arched tile on one side lost its other lane's rail; and a
/// stream already live when the OBS link came up was stamped at connect time.
/// </summary>
public sealed class BugHunt20_FinishNamesRailsVideoTests
{
    private static SessionResultEntry Row(string who, string name, int pos, params long[] laps) => new()
    {
        Transponder = who,
        DriverName = name,
        Position = pos,
        Laps = Math.Max(0, laps.Length - 1),
        LapTimesMs = laps.ToList(),
        FastLapMs = laps.Skip(1).DefaultIfEmpty(0).Min(),
    };

    private static SessionRecord Race(params SessionResultEntry[] rows) => new()
    {
        SessionType = RaceSessionType.Race,
        Label = "Q1 Heat 1",
        StartedAtUtc = new DateTime(2026, 11, 13, 19, 0, 0, DateTimeKind.Utc),
        EndedAtUtc = new DateTime(2026, 11, 13, 19, 5, 0, DateTimeKind.Utc),
        Results = rows.ToList(),
    };

    [Fact]
    public void A_car_filed_under_its_transponder_number_is_never_named_as_held_off()
    {
        var me = Row("3012345", "Asher Kent", 4, 2000, 10000, 10000, 10000, 10000);
        var rental = Row("4417823", "4417823", 5, 2300, 10000, 10000, 10000, 10000);

        Assert.Null(RaceLines.HeldOffLine(Race(me, rental), me));

        // A named car on the same gaps still is.
        var sam = Row("3099999", "Sam Parker", 5, 2300, 10000, 10000, 10000, 10000);
        Assert.Equal("Held off Sam for 4 laps", RaceLines.HeldOffLine(Race(me, sam), me));
    }

    [Fact]
    public void A_class_folded_into_a_shared_heat_still_has_its_finals_counted()
    {
        var shared = new RoundDefinition
        {
            Id = "q1",
            ClassName = "Stock",
            Kind = RoundKind.Qualifier,
            AssignedTransponders = { "s1", "s2", "s3", "m1", "m2", "m3", "m4" },
            SharedClasses = new List<string> { "Mod" },
            SharedClassOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["s1"] = "Stock", ["s2"] = "Stock", ["s3"] = "Stock",
                ["m1"] = "Mod", ["m2"] = "Mod", ["m3"] = "Mod", ["m4"] = "Mod",
            },
        };

        var awaiting = RaceProgramControl.ClassesAwaitingMains(new[] { shared }, null);

        Assert.Equal(new[] { ("Stock", 3), ("Mod", 4) }, awaiting);

        // Once Stock's main is built, Mod's is still to come.
        var stockMain = new RoundDefinition { Id = "a", ClassName = "Stock", Kind = RoundKind.Main, AssignedTransponders = { "s1", "s2", "s3" } };

        Assert.Equal(new[] { ("Mod", 4) }, RaceProgramControl.ClassesAwaitingMains(new[] { shared, stockMain }, null));
    }

    [Fact]
    public void A_divider_with_a_finish_tile_on_one_side_still_bolts_the_other_lanes_rail()
    {
        // Cell (0,0) is a finish tile facing east onto the divider between
        // it and cell (1,0), a plain tile.
        var design = new RcpTrackDesign
        {
            Tiles =
            {
                new RcpTile { Col = 0, Row = 0, Piece = RcpPiece.Finish, Facing = RcpEdge.East },
                new RcpTile { Col = 1, Row = 0 },
            },
            Walls = { new RcpWall { Col = 1, Row = 0, Vertical = true, Double = true } },
        };

        var report = RcpPieces.Adopt(design);

        Assert.Equal(RcpPiece.Finish, design.At(0, 0)!.Piece);
        Assert.Equal((RcpPiece.Rail, RcpEdge.West), (design.At(1, 0)!.Piece, design.At(1, 0)!.Facing));
        Assert.Equal(0, report.SecondRails);
        Assert.Equal(0, report.RailsWithNoTile);
    }

    private static readonly DateTime At = new(2026, 11, 14, 19, 5, 0, DateTimeKind.Utc);

    private static JsonElement Status(bool active, double? durationMs = null) =>
        JsonDocument.Parse("{\"op\":7,\"d\":{\"requestType\":\"GetStreamStatus\",\"requestId\":\"hc-status\",\"responseData\":{\"outputActive\":"
            + (active ? "true" : "false") + (durationMs is { } ms ? ",\"outputDuration\":" + ms : string.Empty) + "}}}").RootElement;

    [Fact]
    public void A_stream_already_live_when_the_link_comes_up_starts_when_OBS_went_live()
    {
        var stream = new StreamTonight();

        // Live on the streaming PC for ten minutes before the laptop linked up.
        var status = ObsLink.StreamStatus(Status(active: true, durationMs: 10 * 60 * 1000), At)!.Value;
        stream.ObsSays(status.Streaming, status.LiveSinceUtc, At);

        var piece = Assert.Single(stream.Pieces);
        Assert.Equal(At.AddMinutes(-10), piece.StartedUtc);
        Assert.True(stream.Live);
    }

    [Fact]
    public void A_restart_after_the_old_piece_closed_starts_at_OBS_start_but_never_before_that_piece_ended()
    {
        var stream = new StreamTonight();
        stream.Started(At.AddMinutes(-60));
        stream.Stopped(At.AddMinutes(-20));

        var status = ObsLink.StreamStatus(Status(active: true, durationMs: 10 * 60 * 1000), At)!.Value;
        stream.ObsSays(status.Streaming, status.LiveSinceUtc, At);

        Assert.Equal(2, stream.Pieces.Count);
        Assert.Equal(At.AddMinutes(-10), stream.Pieces[1].StartedUtc);

        // OBS saying it went live before the last piece ended: from now.
        var other = new StreamTonight();
        other.Started(At.AddMinutes(-60));
        other.Stopped(At.AddMinutes(-5));
        other.ObsSays(true, At.AddMinutes(-10), At);

        Assert.Equal(At, other.Pieces[1].StartedUtc);
    }
}
