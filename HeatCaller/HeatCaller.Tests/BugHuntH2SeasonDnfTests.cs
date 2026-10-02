namespace HeatCaller.Tests;

/// <summary>
/// A result marked DNF or DQ in Edit Result can still carry laps - a DNF
/// keeps them - and the Championship table used to treat it as a finish:
/// 0 in the DNF column, and a podium (or a win) for the place it sat in.
/// The place's points are kept on purpose (see ResultEditor.Group); only
/// the DNF / win / podium counts change.
/// </summary>
public sealed class BugHuntH2SeasonDnfTests
{
    private static SessionRecord Race(string id, params (string Name, int Position, int Laps, string Outcome)[] rows) => new()
    {
        Id = id,
        SessionType = RaceSessionType.Race,
        Label = "A-Main",
        StartedAtUtc = new DateTime(2026, 9, 20, 18, 0, 0, DateTimeKind.Utc),
        Results = rows.Select(r => new SessionResultEntry
        {
            Transponder = "T-" + r.Name,
            DriverName = r.Name,
            Position = r.Position,
            Laps = r.Laps,
            Outcome = r.Outcome,
        }).ToList(),
    };

    [Fact]
    public void A_dnf_with_laps_counts_as_a_dnf_not_a_podium_and_keeps_its_points()
    {
        var season = new Season { Name = "Summer" };

        var table = SeasonStore.ComputeStandings(season, new[]
        {
            // Lower case on purpose: the outcome is read through NormaliseOutcome.
            Race("r1", ("Asher", 1, 20, ""), ("Frank", 2, 19, ""), ("Sam", 3, 12, "dnf")),
        });

        var sam = table.Single(e => e.DriverName == "Sam");

        Assert.Equal(1, sam.Dnfs);
        Assert.Equal(0, sam.Podiums);
        Assert.Equal(0, sam.Wins);
        Assert.Equal(season.PointsForPosition(3), sam.TotalPoints);
        Assert.Equal(1, sam.SessionsCounted);

        var asher = table.Single(e => e.DriverName == "Asher");
        Assert.Equal(1, asher.Wins);
        Assert.Equal(1, asher.Podiums);
        Assert.Equal(0, asher.Dnfs);
    }

    [Fact]
    public void A_dnf_leader_with_laps_is_not_given_the_win()
    {
        var season = new Season { Name = "Summer" };

        var table = SeasonStore.ComputeStandings(season, new[]
        {
            Race("r1", ("Sam", 1, 20, "DNF"), ("Asher", 2, 19, "")),
        });

        var sam = table.Single(e => e.DriverName == "Sam");

        Assert.Equal(0, sam.Wins);
        Assert.Equal(0, sam.Podiums);
        Assert.Equal(1, sam.Dnfs);
        Assert.Equal(season.PointsForPosition(1), sam.TotalPoints);
    }

    [Fact]
    public void A_zero_lap_row_is_still_a_dnf_with_no_points_as_before()
    {
        var season = new Season { Name = "Summer" };

        var table = SeasonStore.ComputeStandings(season, new[]
        {
            Race("r1", ("Asher", 1, 20, ""), ("Sam", 2, 0, "DQ")),
        });

        var sam = table.Single(e => e.DriverName == "Sam");

        Assert.Equal(1, sam.Dnfs);
        Assert.Equal(0, sam.TotalPoints);
        Assert.Equal(0, sam.SessionsCounted);
    }
}
