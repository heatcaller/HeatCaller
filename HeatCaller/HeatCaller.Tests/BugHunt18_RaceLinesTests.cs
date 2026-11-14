namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 18, two RaceLines faults.
///
/// A shared (mixed-class) heat: "Best ... finish this year" and "Best lap
/// quicker than your ..." took the class from the round, which for a shared
/// heat is its main class only, and compared overall places. A Mod driver
/// folded into a Stock heat was told "First Stock win this year". They now
/// use the class stamped on the driver's row and their place in that class.
///
/// "This year" was the UTC year, so races near New Year were counted in the
/// wrong one. It is now the club's local calendar year.
/// </summary>
public sealed class BugHunt18_RaceLinesTests
{
    private const string Me = "3012345";
    private const string Stocker1 = "3000001";
    private const string Stocker2 = "3000002";
    private const string Modder = "3000003";
    private const string Stock = "stock-round";
    private const string Mod = "mod-round";

    private static readonly DateTime Tonight = new(2026, 11, 11, 19, 0, 0, DateTimeKind.Utc);

    private static readonly Func<string, RoundDefinition?> Rounds = id => id switch
    {
        // A shared round keeps its main class as ClassName.
        Stock => new RoundDefinition { Id = Stock, ClassName = "Stock", RoundLabel = "Heat" },
        Mod => new RoundDefinition { Id = Mod, ClassName = "Mod", RoundLabel = "Heat" },
        _ => null,
    };

    private static string ClassOf(SessionRecord s) =>
        s.RoundId is { } id && Rounds(id) is { } round ? round.ClassName : string.Empty;

    private static string LabelOf(SessionRecord s) => s.Label;

    private static SessionResultEntry Row(string who, int pos, long best, string? stamped = null) => new()
    {
        Transponder = who,
        DriverName = who,
        Position = pos,
        Laps = 1,
        LapTimesMs = new List<long> { 2000, best },
        FastLapMs = best,
        ClassName = stamped,
    };

    private static SessionRecord Race(string label, DateTime at, string round, params SessionResultEntry[] rows) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        SessionType = RaceSessionType.Race,
        Label = label,
        RoundId = round,
        TrackLayoutName = "Club Oval",
        StartedAtUtc = at,
        EndedAtUtc = at.AddMinutes(5),
        Results = rows.ToList(),
    };

    /// <summary>A shared Stock-plus-Mod heat: two Stock cars, me and another Mod, in the places given.</summary>
    private static SessionRecord Shared(string label, DateTime at, int myPlace, int otherModPlace, long myBest = 9500)
    {
        var stockPlaces = Enumerable.Range(1, 4).Except(new[] { myPlace, otherModPlace }).ToList();

        return Race(label, at, Stock,
            Row(Stocker1, stockPlaces[0], 9000, "Stock"),
            Row(Stocker2, stockPlaces[1], 9000, "Stock"),
            Row(Me, myPlace, myBest, "Mod"),
            Row(Modder, otherModPlace, 9600, "Mod"));
    }

    private static IReadOnlyList<string> For(SessionRecord race, IEnumerable<SessionRecord>? tonight = null, IEnumerable<SessionRecord>? history = null) =>
        RaceLines.For(Me, race, tonight ?? new[] { race }, history, ClassOf, LabelOf);

    [Fact]
    public void A_mod_driver_in_a_shared_stock_heat_is_never_told_about_a_stock_finish()
    {
        // Three earlier shared heats: 2nd of the two Mods each time (3rd/4th overall).
        var earlier = new[]
        {
            Shared("Q1", Tonight.AddDays(-21), myPlace: 4, otherModPlace: 3),
            Shared("Q1", Tonight.AddDays(-14), myPlace: 3, otherModPlace: 1),
            Shared("Q1", Tonight.AddDays(-7), myPlace: 4, otherModPlace: 2),
        };

        // Tonight: first of the Mods but 3rd overall - their first Mod win.
        var main = Shared("A Main", Tonight, myPlace: 3, otherModPlace: 4);
        Assert.Equal(new[] { "First Mod win this year" }, For(main, history: earlier));

        // First overall but the earlier shared heats were all Mod races for
        // them, so it is still a Mod win - never "First Stock win".
        var first = Shared("A Main", Tonight, myPlace: 1, otherModPlace: 4);
        Assert.Equal(new[] { "First Mod win this year" }, For(first, history: earlier));
    }

    [Fact]
    public void Their_own_mod_races_and_shared_heats_count_as_one_class_history()
    {
        var earlier = new[]
        {
            Race("Mod Heat", Tonight.AddDays(-21), Mod, Row(Me, 3, 9500), Row(Modder, 1, 9400)),
            Race("Mod Heat", Tonight.AddDays(-14), Mod, Row(Me, 2, 9500), Row(Modder, 1, 9400)),
            Shared("Q1", Tonight.AddDays(-7), myPlace: 4, otherModPlace: 1),
        };

        // A one-class Mod race: 1st, against Mod places 3, 2, 2.
        var win = Race("Mod Main", Tonight, Mod, Row(Me, 1, 9500), Row(Modder, 2, 9400));
        Assert.Equal(new[] { "First Mod win this year" }, For(win, history: earlier));
    }

    [Fact]
    public void Quicker_than_their_first_race_tonight_in_their_own_class_not_the_shared_heats_main_class()
    {
        var heat1 = Race("Mod Heat 1", Tonight, Mod, Row(Me, 2, 10000), Row(Modder, 1, 9900));
        var heat2 = Shared("Heat 2", Tonight.AddMinutes(30), myPlace: 3, otherModPlace: 4, myBest: 9500);

        Assert.Contains("Best lap 0.50s quicker than your Mod Heat 1", For(heat2, tonight: new[] { heat1, heat2 }));
    }

    [Fact]
    public void This_year_is_the_clubs_local_year_not_utcs()
    {
        static DateTime Local(int y, int mo, int d, int h) =>
            new DateTime(y, mo, d, h, 0, 0, DateTimeKind.Local).ToUniversalTime();

        // Three races on 31 Dec local, late evening - already 1 Jan in UTC west of Greenwich.
        var newYearsEve = new[]
        {
            Race("Heat 1", Local(2026, 12, 31, 20), Mod, Row(Me, 5, 9500)),
            Race("Heat 2", Local(2026, 12, 31, 21), Mod, Row(Me, 6, 9500)),
            Race("Heat 3", Local(2026, 12, 31, 22), Mod, Row(Me, 7, 9500)),
        };

        // The first race of the new year has nothing from this year to beat.
        var newYear = Race("Heat 1", Local(2027, 1, 1, 20), Mod, Row(Me, 4, 9500));
        Assert.Empty(For(newYear, history: newYearsEve));

        // And the last race of the old year is measured against the old year's.
        var december = Enumerable.Range(1, 3)
            .Select(i => Race("Heat 1", Local(2026, 12, 31 - i, 20), Mod, Row(Me, 6, 9500)))
            .ToList();
        var last = Race("A Main", Local(2026, 12, 31, 23), Mod, Row(Me, 4, 9500));
        Assert.Equal(new[] { "Best Mod finish this year" }, For(last, history: december));
    }
}
