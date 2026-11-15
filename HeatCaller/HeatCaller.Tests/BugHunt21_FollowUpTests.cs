namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 21 (15 Nov 2026), loose ends left by the fixes: "spick and span"
/// is not a slur, a season at a club that never closes its race days still
/// finishes, and the roster's "first night" badge is only on drivers who are
/// here.
/// </summary>
public sealed class BugHunt21_FollowUpTests
{
    [Theory]
    [InlineData("That pit table is spick and span tonight.")]
    [InlineData("Car's spick-and-span after the rebuild!")]
    [InlineData("Spick 'n' span, ready for the A-main.")]
    public void Spick_and_span_is_an_everyday_phrase(string line) =>
        Assert.Null(ScreenWords.HateTermIn(line));

    [Fact]
    public void Spick_on_its_own_is_still_caught() =>
        Assert.NotNull(ScreenWords.HateTermIn("What a spick."));

    [Fact]
    public void A_race_day_left_open_after_its_night_is_over_is_done()
    {
        var events = RaceEventStore.Load();
        var round = new RoundDefinition { ClassName = "Stock", Kind = RoundKind.Main, RoundLabel = "A-Main" };
        var day = new RaceEvent { Name = "Round 8" };
        day.RoundIds.Add(round.Id);
        events.Upsert(day);

        var raced = new DateTime(2026, 11, 14, 21, 0, 0, DateTimeKind.Local);
        var races = new List<SessionRecord>
        {
            new() { SessionType = RaceSessionType.Race, RoundId = round.Id, StartedAtUtc = raced.ToUniversalTime() },
        };

        // Later that night, and just after midnight, the night is still on.
        Assert.True(SeasonsControl.AnyOnAnOpenDay(races, events, raced.AddHours(1)));
        Assert.True(SeasonsControl.AnyOnAnOpenDay(races, events, raced.AddHours(4)));

        // The next afternoon the day was never closed, but its night is over.
        Assert.False(SeasonsControl.AnyOnAnOpenDay(races, events, raced.AddHours(18)));
    }

    [Fact]
    public void The_roster_badges_only_drivers_who_are_here()
    {
        var source = File.ReadAllText(Path.Combine(SourceRoot(), "HeatCaller", "HeatCaller", "CheckInControl.cs"));

        Assert.Contains("(here ? NewDrivers.Badge(profile, before) : string.Empty)", source);
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("check.cmd not found above the test build.");
    }
}
