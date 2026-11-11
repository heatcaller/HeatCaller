namespace HeatCaller.Tests;

/// <summary>
/// A black flag whose DQ was taken back off in Edit Result, or refused at
/// filing, leaves its BlackFlag warning on the entry. The race and night
/// recaps used to say "disqualified" from the warning alone; they must say
/// the result as it stands (10 Nov 2026, bug hunt 16).
/// </summary>
public sealed class BugHunt16_BlackFlagReversedTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly DateTime Start = new(2026, 11, 6, 19, 0, 0, DateTimeKind.Utc);

    private static string Named(string transponder) => transponder;

    private static SessionResultEntry Row(string who, int position, int laps, long ms) => new()
    {
        Transponder = who,
        DriverName = who,
        Position = position,
        Laps = laps,
        ElapsedMs = ms,
        FastLapMs = 20_000,
    };

    private static SessionRecord Race(params SessionResultEntry[] rows)
    {
        var race = new SessionRecord
        {
            Label = "A-Main",
            SessionType = RaceSessionType.Race,
            StartedAtUtc = Start,
            EndedAtUtc = Start.AddMinutes(5),
        };

        race.Results.AddRange(rows);
        return race;
    }

    private static SessionResultEntry Flagged(string outcome)
    {
        var sam = Row("Sam", 2, 12, 305_000);
        sam.Warnings.Add(new RaceWarning { Number = 3, Reason = "Rough driving", Action = WarningAction.BlackFlag });
        sam.Outcome = outcome;
        return sam;
    }

    [Fact]
    public void The_night_does_not_call_a_reinstated_driver_disqualified()
    {
        var race = Race(Row("Bill", 1, 12, 302_000), Flagged(string.Empty));
        race.Edits.Add(new ResultEdit { Transponder = "Sam", Change = "finisher -> DQ", Reason = "Black flag from race control (warning limit reached)" });
        race.Edits.Add(new ResultEdit { Transponder = "Sam", Change = "DQ -> finisher", Reason = "Wrong driver flagged" });

        var lines = CommentaryOfficialFacts.ForTheNight(new[] { race }, Named);

        Assert.DoesNotContain(lines, line => line.Contains("disqualified"));
    }

    [Fact]
    public void The_night_still_says_a_standing_black_flag_dq()
    {
        var race = Race(Row("Bill", 1, 12, 302_000), Flagged(ResultEditor.Dq));

        var lines = CommentaryOfficialFacts.ForTheNight(new[] { race }, Named);

        Assert.Contains("In A-Main, Sam was black-flagged and disqualified.", lines);
    }

    [Fact]
    public void The_race_recap_says_the_black_flag_without_a_dq_that_is_not_on_the_result()
    {
        var parts = CommentaryOfficialFacts.WhatTheyGot(Flagged(string.Empty), session: null);

        Assert.Equal("black-flagged on warning 3 for rough driving", Assert.Single(parts));
    }

    [Fact]
    public void The_race_recap_keeps_and_disqualified_while_the_dq_stands()
    {
        var parts = CommentaryOfficialFacts.WhatTheyGot(Flagged(ResultEditor.Dq), session: null);

        Assert.Equal("black-flagged on warning 3 for rough driving, and disqualified", Assert.Single(parts));
    }
}
