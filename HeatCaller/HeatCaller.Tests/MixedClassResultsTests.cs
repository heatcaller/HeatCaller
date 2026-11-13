namespace HeatCaller.Tests;

/// <summary>
/// A shared heat's results by class (13 Nov 2026, mixed-class heats): each
/// row's class and its place in that class, for Race History, the printed
/// sheet, the export and the phones - the same split the championship
/// scores (MixedHeats.ScoringFields).
/// </summary>
public sealed class MixedClassResultsTests
{
    private static SessionResultEntry Row(int position, string transponder, string name, string? cls) =>
        new() { Position = position, Transponder = transponder, DriverName = name, ClassName = cls, Laps = 20 - position, ElapsedMs = 300_000 };

    private static List<SessionResultEntry> Shared() => new()
    {
        Row(1, "201", "Mo", "Mod"),
        Row(2, "202", "Sam", "Stock"),
        Row(3, "203", "Bea", "Stock"),
        Row(4, "204", "Al", "Mod"),
    };

    [Fact]
    public void Each_row_of_a_shared_heat_gets_its_class_and_its_place_in_it()
    {
        var results = Shared();
        var places = MixedHeats.ClassPlacesOf(results, null);

        Assert.Equal(("Mod", (int?)1), places[results[0]]);
        Assert.Equal(("Stock", (int?)1), places[results[1]]);
        Assert.Equal(("Stock", (int?)2), places[results[2]]);
        Assert.Equal(("Mod", (int?)2), places[results[3]]);
    }

    [Fact]
    public void A_one_class_race_has_the_rounds_class_and_no_class_place()
    {
        var results = new List<SessionResultEntry> { Row(1, "201", "Mo", null), Row(2, "202", "Sam", null) };
        var round = new RoundDefinition { ClassName = "Stock" };

        var places = MixedHeats.ClassPlacesOf(results, round);

        Assert.All(results, r => Assert.Equal(("Stock", (int?)null), places[r]));
        Assert.All(results, r => Assert.Equal(((string?)null, (int?)null), MixedHeats.ClassPlacesOf(results, null)[r]));
    }

    [Fact]
    public void The_export_gives_each_drivers_own_class_and_class_place()
    {
        var session = new SessionRecord { Results = Shared() };
        var csv = ResultsExporter.Build(ResultsExporter.Format.ResultsCsv, new[] { session }, null, ProfileStore.Load());
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();

        Assert.EndsWith(",LapsCountedByHand,ClassPosition", lines[0]);

        var bea = lines.Single(l => l.Contains(",Bea,"));
        var cells = bea.Split(',');
        Assert.Equal("Stock", cells[4]);
        Assert.Equal("2", cells[^1]);

        var json = ResultsExporter.Build(ResultsExporter.Format.Json, new[] { session }, null, ProfileStore.Load());
        Assert.Contains("\"classPosition\": 2", json);
        Assert.Contains("\"className\": \"Stock\"", json);
    }

    [Fact]
    public void The_printed_sheet_and_the_relay_rows_read_the_same_places()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", "ResultsSheetRenderer.cs"));
        Assert.Contains("MixedHeats.ClassPlacesOf(section.Results, null)", source);

        var relay = File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", "MainForm.Relay.cs"));
        Assert.Contains("ClsPos = classPlaces[r].Place", relay);
    }

    [Fact]
    public void Places_are_said_as_ordinals()
    {
        Assert.Equal(new[] { "1st", "2nd", "3rd", "4th", "11th", "12th", "13th", "21st", "22nd" },
            new[] { 1, 2, 3, 4, 11, 12, 13, 21, 22 }.Select(MixedHeats.Ordinal));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }
}
