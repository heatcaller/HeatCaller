namespace HeatCaller.Tests;

/// <summary>
/// A first green before entries closed, on a night whose heats were never
/// posted (13 Nov 2026, bug hunt 17).
///
/// Never posted, the measure runs to the first green. When that came before
/// the close, the line still read "heats posted 10:00 before entries
/// closed" - of heats nobody posted.
/// </summary>
public sealed class BugHunt17_NightMeasuresTests
{
    private static readonly DateTime Seven = new(2026, 11, 7, 19, 0, 0, DateTimeKind.Utc);

    private static DateTime At(double minutes) => Seven.AddMinutes(minutes);

    private static SessionRecord Race(string label, double start) => new()
    {
        Id = "s-" + label,
        SessionType = RaceSessionType.Race,
        Label = label,
        StartedAtUtc = At(start),
        EndedAtUtc = At(start + 5),
    };

    [Fact]
    public void A_green_before_the_close_with_heats_never_posted_says_first_green()
    {
        var day = new RaceEvent { EntriesCloseLocalTime = At(10).ToLocalTime().TimeOfDay };

        var figures = NightMeasures.For(day, new[] { Race("H1", 0) });

        Assert.True(figures.HeatsNotPosted);
        Assert.Equal(TimeSpan.FromMinutes(-10), figures.EntriesToHeats);
        Assert.Equal("first green 10:00 before entries closed (heats not posted)", NightMeasures.Line(figures));
    }

    [Fact]
    public void Heats_that_were_posted_before_the_close_still_say_posted()
    {
        var day = new RaceEvent { EntriesCloseLocalTime = At(-10).ToLocalTime().TimeOfDay, HeatsPostedUtc = At(-15) };

        Assert.Equal("heats posted 5:00 before entries closed", NightMeasures.Line(NightMeasures.For(day, Array.Empty<SessionRecord>())));
    }
}
