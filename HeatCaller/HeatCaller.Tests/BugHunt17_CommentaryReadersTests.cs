namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, CommentaryReaders. What the hunt found, all of it about the
/// two newest things on a race night - mixed-class (shared) heats and an
/// A-main run in legs - which the commentary's readers still read the old
/// way:
///
///  - A driver's form counted a shared heat on the overall order and under
///    the heat's main class only, so the Stock winner who was fourth
///    overall had no win, and the Mod drivers' heats did not exist.
///  - The qualifying report's "are the heats all run" gate matched the
///    round's main class only, so a class that shared its heats never got
///    its pole report - or got it a round early.
///  - The run of mains on a layout credited a meeting to whoever won the
///    LAST A-main leg rather than the A-main's overall winner.
///  - The championship conditional priced a shared heat as one field, when
///    the filed race scores each class on its own.
/// </summary>
public sealed class BugHunt17_CommentaryReadersTests : IDisposable
{
    // Declared first: every store below loads from wherever it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly DateTime Now = new(2026, 11, 13, 19, 0, 0, DateTimeKind.Local);

    private const string Stock = "Stock";
    private const string Mod = "Mod";
    private const string Layout = "Clubhouse Loop";

    private static SessionResultEntry Row(string transponder, int position, string? cls = null) => new()
    {
        Transponder = transponder,
        DriverName = transponder,
        Position = position,
        Laps = 12,
        ClassName = cls,
    };

    private static SessionRecord Race(RoundDefinition round, DateTime endedLocal, params SessionResultEntry[] results) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        SessionType = RaceSessionType.Race,
        RoundId = round.Id,
        TrackLayoutName = Layout,
        StartedAtUtc = endedLocal.ToUniversalTime().AddMinutes(-6),
        EndedAtUtc = endedLocal.ToUniversalTime(),
        Results = results.ToList(),
    };

    private static ProfileStore Profiles(params string[] names)
    {
        var profiles = ProfileStore.Load();

        foreach (var name in names)
        {
            profiles.Upsert(new DriverProfile { Transponder = name, DriverName = name });
        }

        return profiles;
    }

    private static RoundDefinition SharedHeat(string label) => new()
    {
        ClassName = Stock,
        SharedClasses = new List<string> { Mod },
        RoundLabel = label,
        Kind = RoundKind.Qualifier,
    };

    // ------------------------------------------------------------------
    // Form: a shared heat counts for each class, by the place in that class
    // ------------------------------------------------------------------

    [Fact]
    public void A_shared_heats_class_winner_has_a_win_however_many_of_the_other_class_finished_ahead()
    {
        var profiles = Profiles("Sam", "Jo", "Alex", "Mia", "Ned");
        var rounds = RoundDefinitionStore.Load();
        var heat = SharedHeat("Q1");
        rounds.Add(heat);

        // Three Mod cars home first, then the two Stock cars.
        var history = SessionHistoryStore.FromSessions(new[]
        {
            Race(heat, Now.AddDays(-7),
                Row("Alex", 1, Mod), Row("Mia", 2, Mod), Row("Ned", 3, Mod), Row("Sam", 4, Stock), Row("Jo", 5, Stock)),
        });

        var (grid, _) = CommentaryReaders.GridHistory(new[] { "Sam", "Jo" }, Stock, profiles, history, rounds, DateTime.UtcNow);

        var sam = grid.Single(d => d.DriverId == "Sam");
        Assert.Equal(1, sam.RacesHere);
        Assert.Equal(1, sam.WinsHere);
        Assert.Equal(1, sam.PodiumsHere);
        Assert.Equal(new[] { 1 }, sam.RecentFinishes);

        Assert.Equal(new[] { 2 }, grid.Single(d => d.DriverId == "Jo").RecentFinishes);
    }

    [Fact]
    public void A_shared_heat_is_one_of_the_other_classs_races_too()
    {
        var profiles = Profiles("Sam", "Alex", "Mia");
        var rounds = RoundDefinitionStore.Load();
        var heat = SharedHeat("Q1");
        rounds.Add(heat);

        var history = SessionHistoryStore.FromSessions(new[]
        {
            Race(heat, Now.AddDays(-7), Row("Sam", 1, Stock), Row("Alex", 2, Mod), Row("Mia", 3, Mod)),
        });

        // Asked for Mod - as the Mod main's intro asks - the heat used to
        // be nobody's, because its round's main class is Stock.
        var (grid, _) = CommentaryReaders.GridHistory(new[] { "Alex", "Mia" }, Mod, profiles, history, rounds, DateTime.UtcNow);

        var alex = grid.Single(d => d.DriverId == "Alex");
        Assert.Equal(1, alex.RacesHere);
        Assert.Equal(1, alex.WinsHere);
        Assert.Equal(new[] { 2 }, grid.Single(d => d.DriverId == "Mia").RecentFinishes);
    }

    [Fact]
    public void A_race_with_no_class_stamps_is_placed_exactly_as_before()
    {
        var profiles = Profiles("Sam", "Jo");
        var rounds = RoundDefinitionStore.Load();
        var heat = new RoundDefinition { ClassName = Stock, RoundLabel = "Q1", Kind = RoundKind.Qualifier };
        rounds.Add(heat);

        var history = SessionHistoryStore.FromSessions(new[]
        {
            Race(heat, Now.AddDays(-7), Row("Jo", 1), Row("Sam", 2)),
        });

        var (grid, _) = CommentaryReaders.GridHistory(new[] { "Sam" }, Stock, profiles, history, rounds, DateTime.UtcNow);

        Assert.Equal(0, grid[0].WinsHere);
        Assert.Equal(new[] { 2 }, grid[0].RecentFinishes);
    }

    [Fact]
    public void A_run_of_top_three_nights_counts_the_place_within_the_class()
    {
        var profiles = Profiles("Sam", "Alex", "Mia", "Ned");
        var rounds = RoundDefinitionStore.Load();
        var sessions = new List<SessionRecord>();

        // Three nights, Sam the only Stock car and fourth on the road each time.
        for (var week = 1; week <= 3; week++)
        {
            var heat = SharedHeat($"Q{week}");
            rounds.Add(heat);
            sessions.Add(Race(heat, Now.AddDays(-7 * week),
                Row("Alex", 1, Mod), Row("Mia", 2, Mod), Row("Ned", 3, Mod), Row("Sam", 4, Stock)));
        }

        var weeks = CommentaryReaders.RecentWeeks(
            new[] { "Sam" }, Stock, profiles, SessionHistoryStore.FromSessions(sessions), rounds, Now);

        Assert.Equal(3, Assert.Single(weeks).PodiumNightsRunning);
    }

    // ------------------------------------------------------------------
    // ...and only for the class each driver ran it in (the review of 87)
    // ------------------------------------------------------------------

    [Fact]
    public void A_class_win_in_a_shared_heat_is_not_form_in_the_other_class()
    {
        var profiles = Profiles("Sam", "Alex");
        var rounds = RoundDefinitionStore.Load();
        var sessions = new List<SessionRecord>();

        // Three shared heats: Alex wins Mod on the road, Sam wins Stock behind him.
        for (var week = 1; week <= 3; week++)
        {
            var heat = SharedHeat($"Q{week}");
            rounds.Add(heat);
            sessions.Add(Race(heat, Now.AddDays(-7 * week), Row("Alex", 1, Mod), Row("Sam", 2, Stock)));
        }

        var history = SessionHistoryStore.FromSessions(sessions);

        // Asked for Stock with the whole grid, as the shared heat's own
        // intro used to: Alex's three Mod wins were handed over as three
        // Stock wins, beside Sam's three from the same races.
        var (stock, _) = CommentaryReaders.GridHistory(new[] { "Sam", "Alex" }, Stock, profiles, history, rounds, DateTime.UtcNow);

        var sam = stock.Single(d => d.DriverId == "Sam");
        Assert.Equal(3, sam.RacesHere);
        Assert.Equal(3, sam.WinsHere);

        var alexInStock = stock.Single(d => d.DriverId == "Alex");
        Assert.Equal(0, alexInStock.RacesHere);
        Assert.Equal(0, alexInStock.WinsHere);
        Assert.Equal(0, alexInStock.PodiumsHere);
        Assert.Empty(alexInStock.RecentFinishes!);

        // And they are still his in the class he ran them in.
        var (mod, _) = CommentaryReaders.GridHistory(new[] { "Sam", "Alex" }, Mod, profiles, history, rounds, DateTime.UtcNow);

        Assert.Equal(3, mod.Single(d => d.DriverId == "Alex").WinsHere);
        Assert.Equal(0, mod.Single(d => d.DriverId == "Sam").RacesHere);
    }

    [Fact]
    public void Two_drivers_in_different_classes_of_a_shared_heat_have_not_met_in_either_class()
    {
        var profiles = Profiles("Sam", "Alex");
        var rounds = RoundDefinitionStore.Load();
        var sessions = new List<SessionRecord>();

        // Enough meetings to clear the rivalry bar, every one of them a
        // shared heat with Alex in Mod ahead of Sam in Stock.
        for (var week = 1; week <= CommentaryHistoryFacts.RivalryMeetings + 1; week++)
        {
            var heat = SharedHeat($"Q{week}");
            rounds.Add(heat);
            sessions.Add(Race(heat, Now.AddDays(-7 * week), Row("Alex", 1, Mod), Row("Sam", 2, Stock)));
        }

        var history = SessionHistoryStore.FromSessions(sessions);

        var (_, inStock) = CommentaryReaders.GridHistory(new[] { "Sam", "Alex" }, Stock, profiles, history, rounds, DateTime.UtcNow);
        var (_, inMod) = CommentaryReaders.GridHistory(new[] { "Sam", "Alex" }, Mod, profiles, history, rounds, DateTime.UtcNow);

        Assert.Empty(inStock);
        Assert.Empty(inMod);

        // Across the club as a whole they did share a track, and that
        // record is untouched.
        var (_, clubWide) = CommentaryReaders.GridHistory(new[] { "Sam", "Alex" }, null, profiles, history, rounds, DateTime.UtcNow);

        Assert.Single(clubWide);
    }

    [Fact]
    public void Nights_out_in_a_shared_heat_count_for_the_class_the_driver_ran_in_only()
    {
        var profiles = Profiles("Sam", "Alex");
        var rounds = RoundDefinitionStore.Load();
        var sessions = new List<SessionRecord>();

        for (var week = 1; week <= 3; week++)
        {
            var heat = SharedHeat($"Q{week}");
            rounds.Add(heat);
            sessions.Add(Race(heat, Now.AddDays(-7 * week), Row("Alex", 1, Mod), Row("Sam", 2, Stock)));
        }

        var history = SessionHistoryStore.FromSessions(sessions);

        // Alex has not had a Stock night out at all, let alone three in the top three.
        var stock = CommentaryReaders.RecentWeeks(new[] { "Sam", "Alex" }, Stock, profiles, history, rounds, Now);

        Assert.Equal("Sam", Assert.Single(stock).DriverId);

        var alex = Assert.Single(CommentaryReaders.RecentWeeks(new[] { "Alex" }, Mod, profiles, history, rounds, Now));

        Assert.Equal(3, alex.NightsOut);
        Assert.Equal(3, alex.RacesStarted);
        Assert.Equal(3, alex.PodiumNightsRunning);
    }

    // ------------------------------------------------------------------
    // The field: a shared heat is as big as the class's own cars in it
    // ------------------------------------------------------------------

    private static readonly string[] FiveMod = { "M1", "M2", "M3", "M4", "M5" };

    /// <summary>A nine-car shared heat: five Stock cars and four of the Mod ones.</summary>
    private static SessionResultEntry[] NineWithFourMod() => new[]
    {
        Row("S1", 1, Stock), Row("M1", 2, Mod), Row("S2", 3, Stock), Row("M2", 4, Mod), Row("S3", 5, Stock),
        Row("M3", 6, Mod), Row("S4", 7, Stock), Row("M4", 8, Mod), Row("S5", 9, Stock),
    };

    [Fact]
    public void A_shared_heat_is_sized_by_the_classs_own_cars_when_a_field_is_compared()
    {
        var rounds = RoundDefinitionStore.Load();

        var modOnly = new RoundDefinition { ClassName = Mod, RoundLabel = "Mod Q1", Kind = RoundKind.Qualifier };
        var lastWeek = SharedHeat("Q1");
        var tonight = SharedHeat("Q1");
        rounds.Add(modOnly);
        rounds.Add(lastWeek);
        rounds.Add(tonight);

        var fortnightAgo = Now.AddDays(-14);

        var history = SessionHistoryStore.FromSessions(new[]
        {
            // The last night Mod really had five cars out.
            Race(modOnly, fortnightAgo, FiveMod.Select((t, i) => Row(t, i + 1)).ToArray()),

            // Nine on the track since, twice - but four of them Mod.
            Race(lastWeek, Now.AddDays(-7), NineWithFourMod()),
            Race(tonight, Now.AddHours(-1), NineWithFourMod()),
        });

        var field = CommentaryReaders.Field(FiveMod, Mod, history, rounds, Now);

        Assert.NotNull(field);
        Assert.Equal(5, field!.Entries);

        // Tonight's shared heat had four Mod cars, not nine: nothing
        // bigger has run tonight, so the line is not silenced.
        Assert.False(field.BiggerTonight);

        // And last week's is walked past, to the night Mod had five.
        Assert.Equal(
            fortnightAgo.ToString("d MMMM", System.Globalization.CultureInfo.InvariantCulture),
            field.BiggestSinceDescribed);
        Assert.Equal(2, field.RacesSinceThatBig);
    }

    [Fact]
    public void A_race_with_no_class_stamps_is_sized_exactly_as_before()
    {
        var rounds = RoundDefinitionStore.Load();
        var heat = new RoundDefinition { ClassName = Mod, RoundLabel = "Q1", Kind = RoundKind.Qualifier };
        rounds.Add(heat);

        var lastWeek = Now.AddDays(-7);

        var history = SessionHistoryStore.FromSessions(new[]
        {
            Race(heat, lastWeek, FiveMod.Select((t, i) => Row(t, i + 1)).ToArray()),
        });

        var field = CommentaryReaders.Field(new[] { "M1", "M2", "M3", "M4" }, Mod, history, rounds, Now);

        Assert.NotNull(field);
        Assert.Equal(
            lastWeek.ToString("d MMMM", System.Globalization.CultureInfo.InvariantCulture),
            field!.BiggestSinceDescribed);
        Assert.Equal(0, field.RacesSinceThatBig);
    }

    // ------------------------------------------------------------------
    // The qualifying gate: a shared heat is one of the class's heats
    // ------------------------------------------------------------------

    [Fact]
    public void A_class_that_shares_every_heat_still_finishes_its_qualifying()
    {
        var rounds = RoundDefinitionStore.Load();
        var q1 = SharedHeat("Q1");
        var q2 = SharedHeat("Q2");
        rounds.Add(q1);
        rounds.Add(q2);

        Assert.True(rounds.MarkRecorded(q1.Id, "s1"));
        Assert.False(CommentaryReaders.QualifyingFinished(Mod, rounds));

        Assert.True(rounds.MarkRecorded(q2.Id, "s2"));
        Assert.True(CommentaryReaders.QualifyingFinished(Mod, rounds));
        Assert.True(CommentaryReaders.QualifyingFinished(Stock, rounds));
    }

    [Fact]
    public void A_class_with_a_shared_heat_still_to_run_is_not_finished_after_its_own_heat()
    {
        var rounds = RoundDefinitionStore.Load();
        var own = new RoundDefinition { ClassName = Mod, RoundLabel = "Q1", Kind = RoundKind.Qualifier };
        var shared = SharedHeat("Q2");
        rounds.Add(own);
        rounds.Add(shared);

        Assert.True(rounds.MarkRecorded(own.Id, "s1"));

        // Mod's Q2 is the shared heat. Pole is not settled yet.
        Assert.False(CommentaryReaders.QualifyingFinished(Mod, rounds));
    }

    // ------------------------------------------------------------------
    // The run of mains: an A-main in legs goes to its overall winner
    // ------------------------------------------------------------------

    private static RoundDefinition AMain(int leg = 0, int legs = 0) => new()
    {
        ClassName = Stock,
        RoundLabel = leg > 0 ? AMainLegs.LegLabel(leg) : "A-Main",
        Kind = RoundKind.Main,
        MainTier = 0,
        MainLeg = leg,
        MainLegCount = legs,
    };

    /// <summary>One night's A-main in three legs, a few minutes apart, each won by the driver named.</summary>
    private static void Legs(RoundDefinitionStore rounds, List<SessionRecord> sessions, DateTime night, params string[] winners)
    {
        for (var leg = 1; leg <= winners.Length; leg++)
        {
            var round = AMain(leg, winners.Length);
            rounds.Add(round);

            var others = new[] { "Sam", "Bill", "Dot" }.Where(n => n != winners[leg - 1]).ToArray();
            var session = Race(round, night.AddMinutes(20 * leg), Row(winners[leg - 1], 1), Row(others[0], 2), Row(others[1], 3));

            sessions.Add(session);
            rounds.MarkRecorded(round.Id, session.Id);
        }
    }

    [Fact]
    public void A_meeting_whose_a_main_ran_in_legs_goes_to_the_overall_winner_not_the_last_legs()
    {
        var profiles = Profiles("Sam", "Bill", "Dot");
        var rounds = RoundDefinitionStore.Load();
        var sessions = new List<SessionRecord>();

        // Two ordinary meetings Sam won, then last week's in three legs:
        // Sam took legs one and two, Bill the third.
        foreach (var weeksAgo in new[] { 3, 2 })
        {
            var main = AMain();
            rounds.Add(main);
            var session = Race(main, Now.AddDays(-7 * weeksAgo), Row("Sam", 1), Row("Bill", 2), Row("Dot", 3));
            sessions.Add(session);
            rounds.MarkRecorded(main.Id, session.Id);
        }

        Legs(rounds, sessions, Now.AddDays(-7), "Sam", "Sam", "Bill");

        var mains = CommentaryReaders.MainsHere(
            SessionHistoryStore.FromSessions(sessions), rounds, profiles, Layout, Stock, Now);

        Assert.NotNull(mains);
        Assert.Equal("Sam", mains!.HolderId);
        Assert.Equal(3, mains.HolderMeetings);
        Assert.Equal(3, mains.Meetings);
    }

    [Theory]
    [InlineData("Bill", false)] // the last leg went to somebody else: no true line to hand the recap
    [InlineData("Sam", true)]
    public void Tonights_last_leg_is_only_held_against_the_run_when_its_winner_won_the_a_main(string lastLeg, bool said)
    {
        var profiles = Profiles("Sam", "Bill", "Dot");
        var rounds = RoundDefinitionStore.Load();
        var sessions = new List<SessionRecord>();

        var main = AMain();
        rounds.Add(main);
        var past = Race(main, Now.AddDays(-7), Row("Sam", 1), Row("Bill", 2), Row("Dot", 3));
        sessions.Add(past);
        rounds.MarkRecorded(main.Id, past.Id);

        // Tonight: Sam wins the A-main overall on legs one and two.
        Legs(rounds, sessions, Now.AddHours(-2), "Sam", "Sam", lastLeg);

        var mains = CommentaryReaders.MainsHere(
            SessionHistoryStore.FromSessions(sessions), rounds, profiles, Layout, Stock, Now);

        Assert.Equal(said, mains is not null);
    }

    // ------------------------------------------------------------------
    // The championship conditional: nothing claimed about a shared heat
    // ------------------------------------------------------------------

    [Fact]
    public void The_conditional_is_not_offered_for_a_shared_heat()
    {
        var profiles = Profiles("carl", "bill", "dot", "eve");
        var rounds = RoundDefinitionStore.Load();
        var races = new List<SessionRecord>();

        // Bill 61, Carl 58, Dot 55, Eve 36 - CommentaryConditionalTests'
        // own three mains, where Carl winning with Bill second changes the
        // lead.
        var counted = new[]
        {
            new[] { "bill", "carl", "dot", "eve" },
            new[] { "carl", "bill", "dot", "eve" },
            new[] { "dot", "bill", "carl", "eve" },
        };

        for (var i = 0; i < counted.Length; i++)
        {
            var main = AMain();
            var race = Race(main, Now.AddDays(-7 * (counted.Length - i)),
                counted[i].Select((name, place) => Row(name, place + 1)).ToArray());

            main.LinkedSessionId = race.Id;
            rounds.Add(main);
            races.Add(race);
        }

        var next = AMain();
        next.AssignedTransponders = new List<string> { "carl", "bill", "dot", "eve" };
        rounds.Add(next);

        var seasons = SeasonStore.Load();

        seasons.Upsert(new Season
        {
            Name = "Winter Series",
            ClassName = Stock,
            AutoInclude = true,
            MainsOnly = true,
            PointsScheme = new[] { (1, 25), (2, 18), (3, 15), (4, 12) }
                .Select(row => new PointsSchemeEntry { Position = row.Item1, Points = row.Item2 })
                .ToList(),
        });

        var history = SessionHistoryStore.FromSessions(races);

        RaceConditional? Ask() => CommentaryReaders.ChampionshipConditional(
            next, next.AssignedTransponders, seasons, rounds, null, history, profiles);

        // The same race as one class's own: the sentence is proved and offered.
        Assert.NotNull(Ask());

        // Shared with another class, each class is scored as its own field
        // when the race is filed, so "wins" and "second" stop meaning one
        // set of points.
        next.SharedClasses = new List<string> { Mod };

        Assert.Null(Ask());
    }
}
