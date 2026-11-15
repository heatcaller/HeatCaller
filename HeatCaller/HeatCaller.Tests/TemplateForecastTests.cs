namespace HeatCaller.Tests;

/// <summary>
/// The forecast on Setup's Templates tab (14 Nov 2026): where the turnout
/// comes from, and what a night not yet started would come to.
/// </summary>
public sealed class TemplateForecastTests
{
    private static readonly TimeSpan Five = TimeSpan.FromMinutes(5);

    private static RoundDefinition Round(string className, params string[] transponders) =>
        new() { ClassName = className, AssignedTransponders = transponders.ToList() };

    private static RaceEvent Day(string? template, int daysAgo, params RoundDefinition[] rounds)
    {
        var day = new RaceEvent
        {
            Name = $"Night {daysAgo}",
            TemplateName = template,
            StartedUtc = new DateTime(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc).AddDays(-daysAgo),
        };

        day.RoundIds = rounds.Select(r => r.Id).ToList();
        Rounds[day.Id] = rounds.ToList();
        return day;
    }

    // Each test builds its own days, so the ids never collide.
    private static readonly Dictionary<string, List<RoundDefinition>> Rounds = new();

    private static IEnumerable<RoundDefinition> RoundsOf(RaceEvent day) =>
        Rounds.TryGetValue(day.Id, out var rounds) ? rounds : Enumerable.Empty<RoundDefinition>();

    private static string[] Drivers(string prefix, int count) =>
        Enumerable.Range(1, count).Select(i => $"{prefix}{i}").ToArray();

    // --- Turnout -----------------------------------------------------------------

    [Fact]
    public void Phones_saying_they_are_coming_win_over_past_nights()
    {
        var past = new[] { Day("Points", 7, Round("Stock", Drivers("s", 10))) };
        var coming = new List<PreEntry>
        {
            new() { Transponder = "1", Classes = new List<string> { "Stock" } },
            new() { Transponder = "2", Classes = new List<string> { "Stock", "Mod" } },
        };

        var turnout = TemplateForecast.Turnout("Points", new[] { "Stock", "Mod" }, past, RoundsOf, coming);

        Assert.NotNull(turnout);
        Assert.Equal(2, turnout!.PerClass["Stock"]);
        Assert.Equal(1, turnout.PerClass["Mod"]);
        Assert.Equal("said they're coming", turnout.Source);
    }

    [Fact]
    public void Nights_from_the_same_template_are_averaged_and_others_ignored()
    {
        var past = new[]
        {
            Day("Points", 7, Round("Stock", Drivers("a", 10))),
            Day("Club night", 3, Round("Stock", Drivers("b", 30))),
            Day("points", 21, Round("Stock", Drivers("c", 13))),
        };

        var turnout = TemplateForecast.Turnout("Points", new[] { "Stock" }, past, RoundsOf);

        Assert.NotNull(turnout);
        Assert.Equal(12, turnout!.PerClass["Stock"]); // (10 + 13) / 2, rounded half away
        Assert.Equal("the last 2 nights like it", turnout.Source);
    }

    [Fact]
    public void Only_the_last_six_nights_from_a_template_count()
    {
        var past = Enumerable.Range(1, 6).Select(w => Day("Points", w * 7, Round("Stock", Drivers($"w{w}", 8))))
            .Append(Day("Points", 70, Round("Stock", Drivers("old", 40))))
            .ToList();

        var turnout = TemplateForecast.Turnout("Points", new[] { "Stock" }, past, RoundsOf);

        Assert.Equal(8, turnout!.PerClass["Stock"]);
    }

    [Fact]
    public void Without_a_night_from_the_template_the_last_night_that_ran_these_classes_is_used()
    {
        var past = new[]
        {
            Day(null, 3, Round("Mini-Z", Drivers("m", 6))),
            Day(null, 10, Round("Stock", Drivers("s", 9)), Round("Mod", Drivers("d", 4))),
        };

        var turnout = TemplateForecast.Turnout("Points", new[] { "Stock", "Mod" }, past, RoundsOf);

        Assert.NotNull(turnout);
        Assert.Equal(9, turnout!.PerClass["Stock"]);
        Assert.Equal(4, turnout.PerClass["Mod"]);
        Assert.Equal("last time", turnout.Source);
    }

    [Fact]
    public void A_driver_in_two_heats_of_a_class_counts_once_and_practice_not_at_all()
    {
        var counts = TemplateForecast.CountsOf(new[]
        {
            Round("Stock", "1", "2"),
            Round("Stock", "2", "3"),
            new RoundDefinition { ClassName = "Stock", Kind = RoundKind.Practice, AssignedTransponders = new List<string> { "9" } },
        });

        Assert.Equal(3, counts["Stock"]);
    }

    // Bug hunt 20: a shared (mixed-class) heat's guest drivers counted for
    // the heat's main class, so a Stock + Mod night read Stock=7, Mod=0.
    [Fact]
    public void A_shared_heat_counts_each_driver_for_the_class_they_ran_in()
    {
        var shared = Round("Stock", "1", "2", "3", "4", "5", "6", "7");
        shared.SharedClasses = new List<string> { "Mod" };
        shared.SharedClassOf = new Dictionary<string, string>
        {
            ["1"] = "Stock", ["2"] = "Stock", ["3"] = "Stock",
            ["4"] = "Mod", ["5"] = "Mod", ["6"] = "Mod", ["7"] = "Mod",
        };

        var counts = TemplateForecast.CountsOf(new[] { shared });

        Assert.Equal(3, counts["Stock"]);
        Assert.Equal(4, counts["Mod"]);

        var turnout = TemplateForecast.Turnout(null, new[] { "Mod" }, new[] { Day("Points", 7, shared) }, RoundsOf);

        Assert.NotNull(turnout);
        Assert.Equal(4, turnout!.PerClass["Mod"]);
    }

    [Fact]
    public void Nothing_to_go_by_gives_no_turnout_rather_than_a_guess()
    {
        Assert.Null(TemplateForecast.Turnout("Points", new[] { "Stock" }, Array.Empty<RaceEvent>(), RoundsOf));
        Assert.Null(TemplateForecast.Turnout("Points", Array.Empty<string>(), new[] { Day("Points", 7, Round("Stock", "1")) }, RoundsOf));
    }

    // --- The night ---------------------------------------------------------------

    private static NightForecast? Night(bool clubNight, Dictionary<string, int> perClass, int rounds = 3, int perHeat = 8,
        TimeSpan? first = null, TimeSpan? outBy = null, IReadOnlyList<PlannedBreak>? breaks = null) =>
        TemplateForecast.For(clubNight, perClass, rounds, perHeat, _ => Five, _ => Five, breaks, first, outBy, TimeSpan.FromMinutes(3));

    [Fact]
    public void A_race_day_counts_heats_each_round_and_a_final_per_heat_sized_group()
    {
        var forecast = Night(false, new Dictionary<string, int> { ["Stock"] = 17, ["Mod"] = 8 })!;

        Assert.Equal(25, forecast.Drivers);
        Assert.Equal((3 + 1) * 3, forecast.Heats); // Stock 3 heats a round, Mod 1
        Assert.Equal(3 + 1, forecast.Finals);
    }

    [Fact]
    public void A_club_night_has_no_finals()
    {
        var forecast = Night(true, new Dictionary<string, int> { ["Stock"] = 10 }, rounds: 4)!;

        Assert.Equal(8, forecast.Heats);
        Assert.Equal(0, forecast.Finals);
        Assert.Contains("8 races", TemplateForecast.Line(forecast));
    }

    [Fact]
    public void The_finish_counts_every_race_the_gaps_between_and_the_breaks()
    {
        // One class of 8, 2 rounds: 2 heats + 1 final = 3 races of 5 min, 2 gaps of 3, a 30 min break.
        var forecast = Night(false, new Dictionary<string, int> { ["Stock"] = 8 }, rounds: 2,
            first: TimeSpan.FromHours(10), outBy: TimeSpan.FromHours(11),
            breaks: new[] { new PlannedBreak { Minutes = 30 } })!;

        Assert.Equal(TimeSpan.FromMinutes(15 + 6 + 30), forecast.Racing);
        Assert.Equal(TimeSpan.FromHours(10) + TimeSpan.FromMinutes(51), forecast.Finish);
        Assert.Equal(TimeSpan.FromMinutes(9), forecast.Spare);
        Assert.False(TemplateForecast.RunsLate(forecast));
        Assert.Contains("9 min to spare", TemplateForecast.Line(forecast));
    }

    [Fact]
    public void A_night_past_its_out_by_time_says_so()
    {
        var forecast = Night(false, new Dictionary<string, int> { ["Stock"] = 40 },
            first: TimeSpan.FromHours(19), outBy: TimeSpan.FromHours(20))!;

        Assert.True(TemplateForecast.RunsLate(forecast));
        Assert.Contains("past the out-by time", TemplateForecast.Line(forecast));
    }

    [Fact]
    public void Without_a_first_race_time_it_says_how_long_the_racing_is()
    {
        var forecast = Night(false, new Dictionary<string, int> { ["Stock"] = 8 }, rounds: 1)!;

        Assert.Null(forecast.Finish);
        Assert.Null(forecast.Spare);
        Assert.Contains("of racing", TemplateForecast.Line(forecast));
    }

    [Fact]
    public void Nobody_expected_gives_no_forecast()
    {
        Assert.Null(Night(false, new Dictionary<string, int> { ["Stock"] = 0 }));
        Assert.Null(Night(false, new Dictionary<string, int>()));
    }

    [Fact]
    public void The_line_names_where_the_turnout_came_from()
    {
        var forecast = Night(false, new Dictionary<string, int> { ["Stock"] = 8 }, rounds: 1)!;

        Assert.StartsWith("~8 drivers (from last time) · 1 heat + 1 final", TemplateForecast.Line(forecast, "last time"));
    }
}
