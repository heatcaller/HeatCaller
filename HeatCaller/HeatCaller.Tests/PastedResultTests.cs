namespace HeatCaller.Tests;

/// <summary>
/// Paste a Result (1 Nov 2026): a race result copied off LiveRC's results
/// page, read into finishing order, laps, total time and fast lap, and put
/// into Race History as an imported race that never scores.
/// </summary>
public class PastedResultTests
{
    /// <summary>
    /// LiveRC's result table as a browser copies it: the headings, then a
    /// car number in front of each driver's name in capitals, laps and total
    /// time in one cell, and the lap the fast lap was set on after it.
    /// </summary>
    private const string LiveRcResult =
        "Version:0.9\r\nStartHTML:0000000105\r\nEndHTML:0000009999\r\nStartFragment:0000000141\r\nEndFragment:0000009999\r\n"
        + "SourceURL:https://lcrc.liverc.com/results/?p=view_race_result&id=123456\r\n"
        + "<html><body><!--StartFragment-->"
        + "<table><thead><tr><th>Pos</th><th>Driver</th><th>Qual</th><th>Laps/Time</th><th>Behind</th>"
        + "<th>Fastest Lap<br>(Lap #)</th><th>Avg Lap</th><th>Consistency</th></tr></thead><tbody>"
        + "<tr><td>2</td><td>7 JOSH BARTHOLOMEW</td><td>3</td><td>18/10:14.002</td><td>3.371</td><td>31.502 (9)</td><td>33.000</td><td>91.2%</td></tr>"
        + "<tr><td>1</td><td>4 COLTON FURNARI</td><td>1</td><td>18/10:10.631</td><td>-</td><td>31.234 (5)</td><td>32.800</td><td>93.1%</td></tr>"
        + "<tr><td>3</td><td>12 SEAN MCDONALD-O'BRIEN</td><td>2</td><td>17/10:05.900</td><td>1 Lap</td><td>32.010 (3)</td><td>34.100</td><td>88.0%</td></tr>"
        + "<tr><td>4</td><td></td><td></td><td>12/10:20.000</td><td>6 Laps</td><td>35.100 (2)</td><td></td><td></td></tr>"
        + "</tbody></table><!--EndFragment--></body></html>";

    private static PastedResult.Race ReadLiveRc()
    {
        var table = PastedTable.Read(LiveRcResult, null);
        Assert.NotNull(table);

        var race = PastedResult.Read(table!);
        Assert.NotNull(race);
        return race!;
    }

    [Fact]
    public void A_LiveRC_result_comes_in_finishing_order()
    {
        var race = ReadLiveRc();

        Assert.Equal(new[] { "Colton Furnari", "Josh Bartholomew", "Sean McDonald-O'Brien" }, race.Rows.Select(r => r.Name));
        Assert.Equal(new[] { 1, 2, 3 }, race.Rows.Select(r => r.Position));
    }

    [Fact]
    public void Laps_total_time_and_fast_lap_are_read_from_LiveRCs_cells()
    {
        var winner = ReadLiveRc().Rows[0];

        Assert.Equal(18, winner.Laps);
        Assert.Equal(610_631, winner.ElapsedMs);
        Assert.Equal(31_234, winner.FastLapMs);
    }

    [Fact]
    public void A_row_with_no_driver_is_left_out_and_said_so()
    {
        var race = ReadLiveRc();

        Assert.Equal(3, race.Rows.Count);
        Assert.Single(race.Problems);
        Assert.Contains("no driver", race.Problems[0]);
    }

    [Fact]
    public void A_driver_list_is_not_a_result()
    {
        var table = PastedTable.Read(
            "<table><tr><th>Driver</th><th>Transponder</th></tr><tr><td>Sam Lee</td><td>1234567</td></tr></table>",
            null);

        Assert.NotNull(table);
        Assert.Null(PastedResult.Read(table!));
    }

    [Fact]
    public void A_result_typed_in_a_spreadsheet_reads_too()
    {
        var table = PastedTable.Read(null, "Pos\tDriver\tLaps\tTime\tBest Lap\n1\tSam Lee\t20\t5:02.100\t14.880\n2\tAlex Kim\t19\t5:04.300\t15.010\n");

        Assert.NotNull(table);
        var race = PastedResult.Read(table!);

        Assert.NotNull(race);
        Assert.Equal(2, race!.Rows.Count);
        Assert.Equal(20, race.Rows[0].Laps);
        Assert.Equal(302_100, race.Rows[0].ElapsedMs);
        Assert.Equal(14_880, race.Rows[0].FastLapMs);
    }

    [Theory]
    [InlineData("31.234", 31_234)]
    [InlineData("31.234 (5)", 31_234)]
    [InlineData("1:02.5", 62_500)]
    [InlineData("10:10.631", 610_631)]
    [InlineData("1:05:00.000", 3_900_000)]
    [InlineData("45", 45_000)]
    public void Times_are_read_as_a_results_page_writes_them(string cell, long expected)
    {
        Assert.True(PastedResult.TryTime(cell, out var ms));
        Assert.Equal(expected, ms);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("DNS")]
    public void A_cell_with_no_time_reads_as_none(string cell)
    {
        Assert.False(PastedResult.TryTime(cell, out var ms));
        Assert.Equal(0, ms);
    }

    [Fact]
    public void Laps_and_time_in_one_cell_are_split()
    {
        Assert.True(PastedResult.TryLapsAndTime("18/10:10.631", out var laps, out var ms));
        Assert.Equal(18, laps);
        Assert.Equal(610_631, ms);
    }

    [Theory]
    [InlineData("4 COLTON FURNARI", "Colton Furnari")]
    [InlineData("FURNARI, COLTONCOLTON FURNARI", "Colton Furnari")]
    [InlineData("Sam Lee", "Sam Lee")]
    [InlineData("JAMES SMITH III", "James Smith III")]
    public void Driver_cells_are_tidied_into_names(string cell, string expected)
    {
        Assert.Equal(expected, PastedResult.DriverName(cell));
    }

    [Fact]
    public void A_copy_made_on_LiveRC_says_so_and_keeps_no_address()
    {
        Assert.Equal("LiveRC", PastedResult.SourceName(LiveRcResult));
        Assert.Equal("another results page", PastedResult.SourceName("SourceURL:https://liverc.com.example.org/results\r\n<table></table>"));
        Assert.Equal("another results page", PastedResult.SourceName(null));
    }

    [Fact]
    public void The_race_title_is_split_into_class_and_round()
    {
        var title = PastedResult.TitleFrom("Race 33: Exp. 1-8 Electric Buggy A-Main\nPos\tDriver");

        Assert.Equal("Exp. 1-8 Electric Buggy A-Main", title);
        Assert.Equal(("Exp. 1-8 Electric Buggy", "A-Main"), PastedResult.SplitTitle(title, Array.Empty<string>()));
        Assert.Equal(("Stock Buggy", "Heat 2"), PastedResult.SplitTitle("stock buggy Heat 2", new[] { "Stock Buggy" }));
        Assert.Equal((string.Empty, "Club Night"), PastedResult.SplitTitle("Club Night", new[] { "Stock Buggy" }));
    }

    [Fact]
    public void An_imported_race_is_marked_so_it_never_scores()
    {
        var race = ReadLiveRc();
        var session = PastedResult.ToSession(race, "Stock Buggy", "A-Main", new DateTime(2026, 10, 3, 14, 30, 0), "LiveRC", _ => null);

        Assert.Equal(RaceSessionType.Race, session.SessionType);
        Assert.Equal("Stock Buggy A-Main", session.Label);
        Assert.Equal("LiveRC", session.ImportedFrom);
        Assert.True(session.IsImported());
        Assert.True(session.IsNotScored());
        Assert.False(session.IsDemoSession());
        Assert.Equal(3, session.Results.Count);
        Assert.Equal("Colton Furnari", session.Results[0].DriverName);
        Assert.Equal(1, session.Results[0].Position);
    }

    [Fact]
    public void A_race_run_here_still_scores()
    {
        var session = new SessionRecord { SessionType = RaceSessionType.Race };

        Assert.False(session.IsImported());
        Assert.False(session.IsNotScored());
    }

    [Fact]
    public void A_driver_matched_by_name_takes_their_profile()
    {
        var profiles = new List<DriverProfile>
        {
            new() { DriverName = "Colton Furnari", Transponder = "4000137", CarModel = "MR-03" },
            new() { DriverName = "Josh Bartholomew", Transponder = "4000274" },
            new() { DriverName = "Josh Bartholomew", Transponder = "4000411" },
        };

        var session = PastedResult.ToSession(ReadLiveRc(), string.Empty, string.Empty, DateTime.Now, "LiveRC",
            name => PastedResult.MatchByName(profiles, name));

        Assert.Equal("Imported race", session.Label);
        Assert.Equal("4000137", session.Results[0].Transponder);
        Assert.Equal("MR-03", session.Results[0].CarModel);

        // Two profiles with one name: neither is given the other's result.
        Assert.Equal("Josh Bartholomew", session.Results[1].DriverName);
        Assert.Equal(string.Empty, session.Results[1].Transponder);
    }

    [Fact]
    public void An_imported_result_cant_be_corrected_in_place()
    {
        var session = PastedResult.ToSession(ReadLiveRc(), "Stock Buggy", "A-Main", DateTime.Now, "LiveRC", _ => null);

        Assert.Equal(ResultEditor.ImportedProblem, ResultEditor.Validate(session, Array.Empty<ResultAdjustment>()));
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(31_234, "31.234")]
    [InlineData(610_631, "10:10.631")]
    [InlineData(3_900_000, "1:05:00.000")]
    public void The_preview_writes_times_as_a_results_list_does(long ms, string expected)
    {
        Assert.Equal(expected, PasteResultForm.TimeText(ms));
    }
}
