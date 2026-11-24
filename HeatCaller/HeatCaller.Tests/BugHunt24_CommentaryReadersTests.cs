using System.Globalization;

namespace HeatCaller.Tests;

/// <summary>
/// BUG HUNT 24 (23 Nov 2026) - CommentaryReaders.
///
/// #141 (F092): TitleOnTheLine priced tonight as one race when a B-main was
/// staged with the A-main still to run, so a bump-up who scores twice was
/// under-priced and the intro could say "nobody left can reach him".
///
/// #13 (F093): Cars read a shared heat by its main class, so a Mod driver in
/// a Stock + Mod heat was announced as making their "first Stock race".
///
/// #142 (F094): NightStory read a shared heat overall, so a Stock winner
/// behind a Mod car on the road was "beaten" by that Mod driver.
/// </summary>
public sealed class BugHunt24_CommentaryReadersTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string Stock = "Box Stock";
    private const string Mod = "Mod";

    private static readonly DateTime Now = new(2026, 10, 10, 19, 0, 0, DateTimeKind.Local);

    private static string Id(string name) => name.ToLowerInvariant();

    // ------------------------------------------------------------------
    // #141 (F092) - TitleOnTheLine with a second counting race tonight
    // ------------------------------------------------------------------

    /// <summary>
    /// Four one-main race days of Bill beating Sam (BugHunt8's fixture), then
    /// tonight's B-main staged - with or without an A-main still to run on
    /// the same race day.
    /// </summary>
    private static TitleStakes? AskBeforeBMain(bool aMainStillToRun)
    {
        var profiles = ProfileStore.Load();

        foreach (var name in new[] { "Bill", "Sam", "Carl", "Dot" })
        {
            profiles.Upsert(new DriverProfile { Transponder = Id(name), DriverName = name });
        }

        var rounds = RoundDefinitionStore.Load();
        var events = RaceEventStore.Load();
        var races = new List<SessionRecord>();
        var when = new DateTime(2026, 5, 1, 19, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 4; i++)
        {
            var past = new RoundDefinition
            {
                ClassName = Stock,
                RoundLabel = "A-Main",
                Kind = RoundKind.Main,
                MainTier = 0,
            };

            var race = new SessionRecord
            {
                Id = "r" + i.ToString(CultureInfo.InvariantCulture),
                SessionType = RaceSessionType.Race,
                Label = "A-Main",
                RoundId = past.Id,
                StartedAtUtc = when.AddDays(7 * i),
                EndedAtUtc = when.AddDays(7 * i).AddMinutes(8),
                Results = new[] { "Bill", "Sam", "Carl", "Dot" }
                    .Select((name, place) => new SessionResultEntry
                    {
                        Transponder = Id(name),
                        DriverName = name,
                        Position = place + 1,
                        Laps = 12,
                    })
                    .ToList(),
            };

            past.LinkedSessionId = race.Id;
            rounds.Add(past);
            races.Add(race);

            events.Upsert(new RaceEvent
            {
                Name = "Round " + (i + 1).ToString(CultureInfo.InvariantCulture),
                StartedUtc = when.AddDays(7 * i),
                FinishedUtc = when.AddDays(7 * i).AddHours(4),
                RoundIds = new List<string> { past.Id },
                ClassNames = new List<string> { Stock },
            });
        }

        var bMain = new RoundDefinition
        {
            ClassName = Stock,
            RoundLabel = "B-Main",
            Kind = RoundKind.Main,
            MainTier = 1,
            AssignedTransponders = new[] { "Bill", "Sam", "Carl", "Dot" }.Select(Id).ToList(),
        };

        rounds.Add(bMain);

        var tonight = new List<string> { bMain.Id };

        if (aMainStillToRun)
        {
            var aMain = new RoundDefinition
            {
                ClassName = Stock,
                RoundLabel = "A-Main",
                Kind = RoundKind.Main,
                MainTier = 0,
            };

            rounds.Add(aMain);
            tonight.Add(aMain.Id);
        }

        events.Upsert(new RaceEvent
        {
            Name = "Tonight",
            StartedUtc = when.AddDays(7 * 4),
            RoundIds = tonight,
            ClassNames = new List<string> { Stock },
        });

        var seasons = SeasonStore.Load();

        seasons.Upsert(new Season
        {
            Name = "Winter Series",
            ClassName = Stock,
            AutoInclude = true,
            MainsOnly = true,
            CountBestRounds = 4,
            PlannedRounds = 6,
            PointsScheme = Enumerable.Range(1, 10)
                .Select(p => new PointsSchemeEntry { Position = p, Points = 11 - p })
                .ToList(),
        });

        return CommentaryReaders.TitleOnTheLine(
            bMain, bMain.AssignedTransponders, seasons, rounds, events,
            SessionHistoryStore.FromSessions(races), profiles);
    }

    [Fact]
    public void TitleOnTheLine_BMainStaged_AMainStillToRun_SaysNothing()
    {
        // Tonight holds two races the season counts, and a B-main winner who
        // bumps up scores in both. The proof prices tonight as one race, so
        // it must not speak. Before the fix it answered.
        Assert.Null(AskBeforeBMain(aMainStillToRun: true));
    }

    [Fact]
    public void TitleOnTheLine_OnlyMainTonight_StillAnswers()
    {
        var stakes = AskBeforeBMain(aMainStillToRun: false);

        Assert.NotNull(stakes);
        Assert.Equal(4, stakes!.RoundsCounted);
        Assert.Equal(2, stakes.RoundsLeft);
    }

    // ------------------------------------------------------------------
    // #13 (F093) - Cars and shared heats
    // ------------------------------------------------------------------

    private static SessionRecord Filed(
        DateTime startedLocal,
        string roundId,
        params (string Transponder, string Name, int Position, string? Class)[] results) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            SessionType = RaceSessionType.Race,
            Label = "A-Main",
            RoundId = roundId,
            StartedAtUtc = startedLocal.ToUniversalTime(),
            EndedAtUtc = startedLocal.AddMinutes(8).ToUniversalTime(),
            Results = results
                .Select(r => new SessionResultEntry
                {
                    Transponder = r.Transponder,
                    DriverName = r.Name,
                    Position = r.Position,
                    Laps = 12,
                    ClassName = r.Class ?? string.Empty,
                })
                .ToList(),
        };

    private static ProfileStore SamAndBill()
    {
        var profiles = ProfileStore.Load();

        profiles.Upsert(new DriverProfile { Transponder = "A", DriverName = "Sam" });
        profiles.Upsert(new DriverProfile { Transponder = "B", DriverName = "Bill" });

        return profiles;
    }

    private static RoundDefinition AddRound(RoundDefinitionStore rounds, string cls, params string[] shared)
    {
        var round = new RoundDefinition
        {
            ClassName = cls,
            RoundLabel = "A-Main",
            Kind = RoundKind.Main,
            SharedClasses = shared.Length > 0 ? shared.ToList() : null,
        };

        rounds.Add(round);
        return round;
    }

    [Fact]
    public void Cars_SharedHeat_ModDriverIsNotGivenAFirstStockRace()
    {
        var rounds = RoundDefinitionStore.Load();
        var past = new List<SessionRecord>();

        // Four Mod-only starts for Sam.
        for (var i = 1; i <= 4; i++)
        {
            var modRound = AddRound(rounds, Mod);
            past.Add(Filed(Now.AddDays(-7 * i), modRound.Id, ("A", "Sam", 1, null), ("C", "Cy", 2, null)));
        }

        // Tonight: a Stock + Mod shared heat, Sam running Mod and winning
        // on the road.
        var shared = AddRound(rounds, Stock, Mod);
        var tonight = Filed(Now.AddMinutes(-20), shared.Id, ("A", "Sam", 1, Mod), ("B", "Bill", 2, Stock));

        var found = CommentaryReaders.Cars(
            tonight,
            Stock,
            null,
            SamAndBill(),
            SessionHistoryStore.FromSessions(past.Append(tonight).ToList()),
            rounds,
            Array.Empty<Car>(),
            Now);

        var sam = found.Single(c => c.DriverId == "A");

        // Before the fix: FirstRaceInClass "Box Stock" - "Sam's first Stock
        // race" said over the PA about a Mod driver racing Mod.
        Assert.Null(sam.FirstRaceInClass);
        Assert.Equal(Mod, sam.OnlyClassBefore);
        Assert.Equal(4, sam.StartsBefore);
    }

    [Fact]
    public void Cars_PastSharedHeatsRunAsMod_CountAsModStarts()
    {
        var rounds = RoundDefinitionStore.Load();
        var past = new List<SessionRecord>();

        // Four shared heats (main class Stock), Sam stamped Mod in each.
        for (var i = 1; i <= 4; i++)
        {
            var shared = AddRound(rounds, Stock, Mod);
            past.Add(Filed(Now.AddDays(-7 * i), shared.Id, ("A", "Sam", 1, Mod), ("B", "Bill", 2, Stock)));
        }

        var modRound = AddRound(rounds, Mod);
        var tonight = Filed(Now.AddMinutes(-20), modRound.Id, ("A", "Sam", 1, null), ("C", "Cy", 2, null));

        var found = CommentaryReaders.Cars(
            tonight,
            Mod,
            null,
            SamAndBill(),
            SessionHistoryStore.FromSessions(past.Append(tonight).ToList()),
            rounds,
            Array.Empty<Car>(),
            Now);

        var sam = found.Single(c => c.DriverId == "A");

        // Before the fix: FirstRaceInClass "Mod", OnlyClassBefore "Box Stock".
        Assert.Null(sam.FirstRaceInClass);
        Assert.Equal(Mod, sam.OnlyClassBefore);
    }

    [Fact]
    public void Cars_RealMoveFromStockToMod_IsStillAFirstModRace()
    {
        var rounds = RoundDefinitionStore.Load();
        var past = new List<SessionRecord>();

        for (var i = 1; i <= 4; i++)
        {
            var stockRound = AddRound(rounds, Stock);
            past.Add(Filed(Now.AddDays(-7 * i), stockRound.Id, ("A", "Sam", 1, null), ("B", "Bill", 2, null)));
        }

        // Tonight Sam runs Mod in a shared heat whose main class is Stock.
        var shared = AddRound(rounds, Stock, Mod);
        var tonight = Filed(Now.AddMinutes(-20), shared.Id, ("A", "Sam", 1, Mod), ("B", "Bill", 2, Stock));

        var found = CommentaryReaders.Cars(
            tonight,
            Stock,
            null,
            SamAndBill(),
            SessionHistoryStore.FromSessions(past.Append(tonight).ToList()),
            rounds,
            Array.Empty<Car>(),
            Now);

        var sam = found.Single(c => c.DriverId == "A");
        var bill = found.Single(c => c.DriverId == "B");

        Assert.Equal(Mod, sam.FirstRaceInClass);
        Assert.Equal(Stock, sam.OnlyClassBefore);

        // Bill ran Stock as always: nothing new.
        Assert.Null(bill.FirstRaceInClass);
    }

    // ------------------------------------------------------------------
    // #142 (F094) - NightStory and shared heats
    // ------------------------------------------------------------------

    private static SessionRecord NightRace(
        DateTime startedLocal,
        string label,
        params (string Transponder, string Name, int Position, string? Class)[] results) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            SessionType = RaceSessionType.Race,
            Label = label,
            StartedAtUtc = startedLocal.ToUniversalTime(),
            EndedAtUtc = startedLocal.AddMinutes(8).ToUniversalTime(),
            Results = results
                .Select(r => new SessionResultEntry
                {
                    Transponder = r.Transponder,
                    DriverName = r.Name,
                    Position = r.Position,
                    Laps = 12,
                    ClassName = r.Class ?? string.Empty,
                })
                .ToList(),
        };

    /// <summary>Five earlier race nights, Dave winning the last race of each against five others.</summary>
    private static List<SessionRecord> DaveRun()
    {
        var races = new List<SessionRecord>();

        for (var week = 1; week <= 5; week++)
        {
            races.Add(NightRace(Now.AddDays(-7 * week), "A-Main",
                ("dave", "Dave", 1, null),
                ("a", "Ann", 2, null),
                ("b", "Bill", 3, null),
                ("c", "Carl", 4, null),
                ("d", "Dot", 5, null),
                ("e", "Eve", 6, null)));
        }

        return races;
    }

    private static NightThread? Story(List<SessionRecord> races)
    {
        var profiles = ProfileStore.Load();

        profiles.Upsert(new DriverProfile { Transponder = "dave", DriverName = "Dave" });
        profiles.Upsert(new DriverProfile { Transponder = "a", DriverName = "Ann" });
        profiles.Upsert(new DriverProfile { Transponder = "m", DriverName = "Mo" });

        var rounds = RoundDefinitionStore.Load();

        rounds.Add(new RoundDefinition
        {
            ClassName = Stock,
            RoundLabel = "A-Main",
            Kind = RoundKind.Main,
            SharedClasses = new List<string> { Mod },
            AssignedTransponders = new List<string> { "dave", "a", "m" },
        });

        return CommentaryReaders.NightStory(rounds, SessionHistoryStore.FromSessions(races), profiles, nowLocal: Now);
    }

    [Fact]
    public void NightStory_SharedHeatClassWinner_IsNotBeatenByTheOtherClass()
    {
        var races = DaveRun();

        // Tonight: Mo (Mod) first on the road, Dave first in Stock.
        races.Add(NightRace(Now.AddHours(-1), "Heat 1",
            ("m", "Mo", 1, Mod),
            ("dave", "Dave", 2, Stock),
            ("a", "Ann", 3, Stock)));

        var story = Story(races);

        Assert.NotNull(story);

        // Before the fix: WonTonight 0 and BeatenBy "Mo".
        Assert.Equal(1, story!.StartedTonight);
        Assert.Equal(1, story.WonTonight);
        Assert.Null(story.BeatenBy);
    }

    [Fact]
    public void NightStory_SharedHeat_BeatenByTheWinnerOfTheirOwnClass()
    {
        var races = DaveRun();

        // Mo (Mod) first on the road, Ann ahead of Dave in Stock: Ann beat
        // him, not Mo.
        races.Add(NightRace(Now.AddHours(-1), "Heat 1",
            ("m", "Mo", 1, Mod),
            ("a", "Ann", 2, Stock),
            ("dave", "Dave", 3, Stock)));

        var story = Story(races);

        Assert.NotNull(story);
        Assert.Equal(1, story!.StartedTonight);
        Assert.Equal(0, story.WonTonight);
        Assert.Equal("Ann", story.BeatenBy);
    }
}
