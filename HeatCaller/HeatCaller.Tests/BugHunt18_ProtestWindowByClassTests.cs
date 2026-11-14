namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 18 (14 Nov 2026): on a mixed-class night each class's protest
/// window opens and closes on its own. The desk now judges a query against
/// the asking driver's class (it is handed their transponder), and the Big
/// Screen reads a shared heat as provisional while any of its classes is -
/// not only the heat's main class.
/// </summary>
public sealed class BugHunt18_ProtestWindowByClassTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string ModBadge = "0123456789abcdef0123";

    private static readonly DateTime Tonight = new(2026, 11, 14, 19, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_desk_judges_a_query_by_the_asking_drivers_own_class()
    {
        var profiles = ProfileStore.Load();
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "200", DriverName = "Mo Dified" }, out _));

        var heat = new SessionRecord
        {
            Id = new string('a', 32),
            Label = "Q1 Heat 1 (Stock + Mod)",
            StartedAtUtc = Tonight,
            Results = new List<SessionResultEntry>
            {
                new() { Position = 1, Transponder = "200", DriverName = "Mo Dified", Laps = 12, FastLapMs = 11_200 },
            },
        };

        var badges = new Dictionary<string, string> { [ModBadge] = "200" };
        var asked = new List<string?>();

        var desk = new DriverRequestDesk(DriverRequestStore.Load(), new AppSettings(), profiles, null, SessionHistoryStore.FromSessions(new[] { heat }), new UndoService(), _ => { },
            badge => badges.TryGetValue(badge, out var t) ? t : null)
        {
            // Mod's window is open; Stock's (the heat's main class) is not.
            QueryInWindow = (race, askedUtc, who) =>
            {
                asked.Add(who);
                return who == "200";
            },
        };

        var query = new DriverRequest { Id = "00000a" + new string('0', 18), Badge = ModBadge, Kind = DriverAsk.Query, AskedUtc = Tonight.AddMinutes(5), Proof = DriverAsk.ByBadge };
        query.Texts["race"] = heat.Id;
        query.Texts["m"] = "Lap 3 missing";

        Assert.True(desk.InProtestWindow(query));
        Assert.Equal(new[] { "200" }, asked);
    }

    [Fact]
    public void Main_form_passes_the_driver_to_the_desk_check_and_reads_every_class_for_the_board()
    {
        var protest = Source("MainForm.ProtestWindow.cs");

        Assert.Contains("private bool QueryInProtestWindow(SessionRecord race, DateTime askedUtc, string? who) =>", protest);
        Assert.Contains("RaceProtestClosesAtUtc(race, who)", protest);

        // No driver (the Big Screen): the latest window over every class the heat runs.
        Assert.Contains(".Select(className => _raceProgram.RoundProtestWindow(round, className)?.ClosesAtUtc)", protest);
        Assert.DoesNotContain("who is null ? round.ClassName", protest);
    }

    /// <summary>The app source, for checks on how the pieces are wired in.</summary>
    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }
}
