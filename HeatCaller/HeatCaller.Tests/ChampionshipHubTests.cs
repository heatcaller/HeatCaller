namespace HeatCaller.Tests;

/// <summary>
/// The Championship page reworked (13 Nov 2026, mock-up C with D's "Can they
/// still win?" card): cards for the championships, four views for the one
/// picked, and who can still win it - worked out with the announcer's proof.
/// </summary>
public sealed class ChampionshipHubTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>
    /// A season of <paramref name="planned"/> rounds, one race a race day,
    /// with the finishing orders given - each a list of names, first to last.
    /// </summary>
    private static (Season Season, List<SessionRecord> Races, RaceEventStore Events) Season(int planned, params string[][] orders)
    {
        var events = RaceEventStore.Load();
        var races = new List<SessionRecord>();
        var start = new DateTime(2026, 9, 4, 19, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < orders.Length; i++)
        {
            var round = new RoundDefinition { ClassName = "Stock", Kind = RoundKind.Main, RoundLabel = "A-Main" };

            var day = new RaceEvent { Name = $"Round {i + 1}" };
            day.RoundIds.Add(round.Id);
            events.Upsert(day);

            races.Add(new SessionRecord
            {
                SessionType = RaceSessionType.Race,
                RoundId = round.Id,
                StartedAtUtc = start.AddDays(14 * i),
                Results = orders[i].Select((name, at) => new SessionResultEntry
                {
                    Position = at + 1,
                    DriverName = name,
                    Transponder = name,
                    Laps = 20 - at,
                }).ToList(),
            });
        }

        var season = new Season { Name = "Winter Series", PlannedRounds = planned };

        return (season, races, events);
    }

    [Fact]
    public void With_rounds_left_a_driver_close_enough_is_still_in_it_and_one_too_far_back_is_not()
    {
        // Default scheme: a win is worth the most. Al wins three, Annie is
        // second three times, Cy last three times; two rounds left.
        var (season, races, events) = Season(5,
            new[] { "Al", "Annie", "Cy" },
            new[] { "Al", "Annie", "Cy" },
            new[] { "Al", "Annie", "Cy" });

        var (race, why) = CommentaryReaders.TitleRaceFor(season, races, events, null);

        Assert.True(race is not null, why);
        Assert.Equal(3, race!.RoundsCounted);
        Assert.Equal(2, race.RoundsLeft);
        Assert.Equal("Al", race.Rows[0].Driver);
        Assert.True(race.Rows[0].CanWin);

        var best = season.PointsForPosition(season.BestPayingPosition());
        var annie = race.Rows.Single(r => r.Driver == "Annie");
        var cy = race.Rows.Single(r => r.Driver == "Cy");

        // Whoever is within two wins of Al can still catch him.
        Assert.Equal(race.Rows[0].Points - annie.Points <= 2 * best, annie.CanWin);
        Assert.Equal(race.Rows[0].Points - cy.Points <= 2 * best, cy.CanWin);
    }

    [Fact]
    public void Once_the_season_is_over_only_the_leader_is_in_it()
    {
        var (season, races, events) = Season(2,
            new[] { "Al", "Annie" },
            new[] { "Al", "Annie" });

        var (race, _) = CommentaryReaders.TitleRaceFor(season, races, events, null);

        Assert.NotNull(race);
        Assert.Equal(0, race!.RoundsLeft);
        Assert.Equal(new[] { true, false }, race.Rows.Select(r => r.CanWin));
    }

    [Fact]
    public void With_no_season_length_it_says_what_to_set_rather_than_guessing()
    {
        var (season, races, events) = Season(0,
            new[] { "Al", "Annie" },
            new[] { "Al", "Annie" });

        var (race, why) = CommentaryReaders.TitleRaceFor(season, races, events, null);

        Assert.Null(race);
        Assert.Contains("how many rounds", why);
    }

    [Fact]
    public void The_page_opens_on_the_standings_view_and_the_other_views_can_be_picked() =>
        StaThread.Run(() =>
        {
            var seasons = SeasonStore.Load();
            seasons.Upsert(new Season { Name = "Winter Series", AutoInclude = true });

            using var page = new SeasonsControl(seasons, SessionHistoryStore.Load(), new UndoService(),
                RoundDefinitionStore.Load(), RaceClassStore.Load(), RaceEventStore.Load(), ProfileStore.Load());

            Assert.Equal("standings", page.Shows.View);
            Assert.Equal(new[] { "standings", "races", "points", "carried" }, page.Shows.Chips.Select(c => c.Key));

            page.ShowView("points");
            Assert.Equal("points", page.Shows.View);

            // No length set: the card asks for it rather than guessing.
            Assert.Contains("how many rounds", page.Shows.TitleLine);
            return 0;
        }, TimeSpan.FromMinutes(1), () => "the championship page");
}
