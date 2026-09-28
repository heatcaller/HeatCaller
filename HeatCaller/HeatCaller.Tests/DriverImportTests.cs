namespace HeatCaller.Tests;

/// <summary>
/// Importing drivers - and their championship points so far - from any CSV
/// (27 Sep 2026): the separator worked out, the lines split, each column's
/// heading guessed, the points read; and carried points added to a
/// championship's table.
/// </summary>
public sealed class DriverImportTests
{
    [Theory]
    [InlineData("Transponder", ImportColumn.Transponder)]
    [InlineData("TX", ImportColumn.Transponder)]
    [InlineData("Transponder #", ImportColumn.Transponder)]
    [InlineData("MyLaps Transponder", ImportColumn.Transponder)]
    [InlineData("First Name", ImportColumn.FirstName)]
    [InlineData("Surname", ImportColumn.LastName)]
    [InlineData("Driver", ImportColumn.DriverName)]
    [InlineData("  Racer Name ", ImportColumn.DriverName)]
    [InlineData("E-Mail", ImportColumn.Email)]
    [InlineData("Team / Club", ImportColumn.Team)]
    [InlineData("Car #", ImportColumn.RaceNumber)]
    [InlineData("Class", ImportColumn.ClassName)]
    [InlineData("Category", ImportColumn.ClassName)]
    [InlineData("Pts", ImportColumn.PointsSoFar)]
    [InlineData("Total Points", ImportColumn.PointsSoFar)]
    [InlineData("Pos", ImportColumn.Ignore)]
    [InlineData("Best Lap", ImportColumn.Ignore)]
    public void A_heading_is_guessed(string heading, ImportColumn expected)
    {
        Assert.Equal(expected, DriverImport.Guess(heading));
    }

    [Fact]
    public void Each_kind_is_guessed_for_the_first_column_only()
    {
        Assert.Equal(
            new[] { ImportColumn.Ignore, ImportColumn.DriverName, ImportColumn.Ignore, ImportColumn.Transponder, ImportColumn.PointsSoFar },
            DriverImport.GuessAll(new[] { "Pos", "Name", "Driver", "Transponder", "Points" }));
    }

    [Theory]
    [InlineData("Name,Transponder,Class", ',')]
    [InlineData("Name;Transponder;Class", ';')]
    [InlineData("Name\tTransponder\tClass", '\t')]
    [InlineData("\"Smith; Jr\",Transponder", ',')]
    [InlineData("Name", ',')]
    public void The_separator_is_the_one_the_heading_uses(string heading, char expected)
    {
        Assert.Equal(expected, DriverImport.DetectDelimiter(heading));
    }

    [Fact]
    public void Quoted_fields_keep_their_separators_and_quotes()
    {
        Assert.Equal(
            new[] { "Smith; Jr", "8467BE", "He said \"go\"", string.Empty },
            DriverImport.ParseLine("\"Smith; Jr\";8467BE;\"He said \"\"go\"\"\";", ';'));
    }

    [Theory]
    [InlineData("45", 45)]
    [InlineData(" 45 ", 45)]
    [InlineData("45.0", 45)]
    [InlineData("45,5", 46)]
    [InlineData("45.500", 46)]
    [InlineData("45 pts", 45)]
    [InlineData("45pts", 45)]
    [InlineData("1,234", 1234)]
    [InlineData("1 234", 1234)]
    [InlineData("1.234.567", 1234567)]
    [InlineData("-3", -3)]
    public void Points_are_read_as_whole_numbers(string text, int expected)
    {
        Assert.True(DriverImport.TryParsePoints(text, out var points));
        Assert.Equal(expected, points);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("pts")]
    [InlineData("DNS")]
    [InlineData("12-4")]
    public void Anything_else_carries_no_points(string text)
    {
        Assert.False(DriverImport.TryParsePoints(text, out _));
    }

    [Fact]
    public void A_first_and_last_name_make_one_name()
    {
        Assert.Equal("Sam Smith", DriverImport.JoinName(" Sam ", "Smith"));
        Assert.Equal("Smith", DriverImport.JoinName("", "Smith"));
        Assert.Equal(string.Empty, DriverImport.JoinName(null, " "));
    }

    // --- Carried points in a championship ---------------------------------------------

    private static SessionRecord Race(string id, params (string Name, int Position)[] finishers) => new()
    {
        Id = id,
        SessionType = RaceSessionType.Race,
        Label = "A-Main",
        StartedAtUtc = new DateTime(2026, 9, 20, 18, 0, 0, DateTimeKind.Utc),
        Results = finishers.Select(f => new SessionResultEntry
        {
            Transponder = "T-" + f.Name,
            DriverName = f.Name,
            Position = f.Position,
            Laps = 10,
        }).ToList(),
    };

    [Fact]
    public void Carried_points_are_added_after_the_best_rounds_rule_and_stand_on_their_own()
    {
        var season = new Season
        {
            Name = "Winter",
            CountBestRounds = 1,
            CarriedPoints =
            {
                new CarriedPointsEntry { DriverName = "Asher", Points = 40 },
                new CarriedPointsEntry { DriverName = "frank", Points = 55 },
                new CarriedPointsEntry { DriverName = "Zed", Points = 12 },
                new CarriedPointsEntry { DriverName = "Nobody", Points = 0 },
            },
        };

        var sessions = new[]
        {
            Race("r1", ("Asher", 1), ("Frank", 2)),
            Race("r2", ("Frank", 1), ("Asher", 2)),
        };

        var table = SeasonStore.ComputeStandings(season, sessions);

        // Best one round each (10), plus what they carried in.
        Assert.Equal(new[] { "Frank", "Asher", "Zed" }, table.Select(e => e.DriverName));
        Assert.Equal(new[] { 65, 50, 12 }, table.Select(e => e.TotalPoints));
        Assert.Equal(new[] { 55, 40, 12 }, table.Select(e => e.CarriedIn));

        // The carried points are not a round, and are never dropped.
        Assert.Equal(1, table[0].DroppedRounds);
        Assert.Equal(0, table[2].SessionsCounted);
        Assert.Equal(int.MaxValue, table[2].BestFinish);
    }

    [Fact]
    public void A_championship_with_nothing_carried_in_scores_as_it_always_did()
    {
        var season = new Season { Name = "Summer" };
        var table = SeasonStore.ComputeStandings(season, new[] { Race("r1", ("Asher", 1), ("Frank", 2)) });

        Assert.Equal(new[] { 10, 9 }, table.Select(e => e.TotalPoints));
        Assert.All(table, e => Assert.Equal(0, e.CarriedIn));
    }
}
