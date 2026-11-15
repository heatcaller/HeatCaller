namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 19 (14 Nov 2026), shared (mixed-class) heats.
///
/// #27: the Big Screen's UP NEXT line named a shared heat by its main class
/// alone, so the other class's drivers didn't see their race coming.
///
/// #46: Redo Remaining Heats never wrote down which class it dealt a driver
/// into a shared heat as. A driver who races both classes, dealt into their
/// Stock heat by Stock and into the shared heat by Mod, was then placed by
/// their profile (Stock first) and their Mod run scored as Stock.
/// </summary>
public sealed class BugHunt19MixedHeatsTests
{
    private const string Stock = "Stock";
    private const string Mod = "Mod";

    // --- #27 ------------------------------------------------------------------

    [Fact]
    public void The_Big_Screen_names_both_classes_of_a_shared_heat()
    {
        var shared = Heat(Stock, "Q2 Heat 3");
        shared.SharedClasses = new List<string> { Mod };
        var mod = Heat(Mod, "Q2 Heat 1");

        var line = RaceControlControl.UpNextLine(new[] { shared, mod }, id => id == shared.Id ? "about 20:42" : null);

        Assert.Equal("UP NEXT:  Stock + Mod Q2 Heat 3 about 20:42  •  Mod Q2 Heat 1", line);
    }

    // --- #46 ------------------------------------------------------------------

    [Fact]
    public void Redo_records_the_class_a_two_class_driver_was_dealt_into_a_shared_heat_as()
    {
        // D races Stock and Mod. Stock has its own heat and a heat it shares
        // with Mod; Mod runs only in the shared one.
        var own = Heat(Stock, "Q2 Heat 1", "S1", "S2", "S3", "D");
        var shared = Heat(Stock, "Q2 Heat 2", "S4", "S5", "M1", "M2", "D");
        shared.SharedClasses = new List<string> { Mod };
        shared.SharedClassOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["S4"] = Stock, ["S5"] = Stock, ["M1"] = Mod, ["M2"] = Mod, ["D"] = Mod,
        };

        var rosters = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [Stock] = new() { "D", "S1", "S2", "S3", "S4", "S5" },
            [Mod] = new() { "D", "M1", "M2" },
        };

        var redo = RaceControlControl.RedealAcrossClasses(
            new[] { own, shared },
            className => rosters[className],
            _ => true,
            // As with a profile listing Stock first: no record, so Stock.
            (round, transponder) => MixedHeats.ClassOfDriver(round, transponder));

        Assert.Contains("D", redo.Rosters[shared.Id]);

        var stockDealtDHere = !redo.Rosters[own.Id].Contains("D");
        var classOf = redo.ClassOf[shared.Id];

        // Every driver in the shared heat is written down, as the class that put them there.
        Assert.Equal(Sorted(redo.Rosters[shared.Id]), Sorted(classOf.Keys));
        Assert.Equal(new[] { "M1", "M2" }, Sorted(classOf.Where(kv => kv.Value == Mod && kv.Key != "D").Select(kv => kv.Key)));
        Assert.Equal(stockDealtDHere ? Stock : Mod, classOf["D"]);

        // Once written onto the heat, D is scored in that class.
        shared.AssignedTransponders = redo.Rosters[shared.Id];
        shared.SharedClassOf = classOf;
        Assert.Equal(stockDealtDHere ? Stock : Mod, MixedHeats.ClassOfDriver(shared, "D"));
    }

    [Fact]
    public void A_Mod_run_in_a_shared_heat_is_scored_as_Mod_when_Stock_dealt_the_driver_into_its_own_heat()
    {
        // The shared heat has no record for D yet (they were in Stock's own
        // heat before), so only the redo can say D runs Mod there. Their
        // profile lists Stock first - here, the heat's main class - so
        // without a record they would be scored as Stock.
        var own = Heat(Stock, "Q2 Heat 1", "D", "S1", "S2", "S3");
        var shared = Heat(Stock, "Q2 Heat 2", "S4", "S5", "S6", "M1", "M2");
        shared.SharedClasses = new List<string> { Mod };
        shared.SharedClassOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["S4"] = Stock, ["S5"] = Stock, ["S6"] = Stock, ["M1"] = Mod, ["M2"] = Mod,
        };

        var rosters = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            // In this order the Q2 deal puts D in Stock's own heat.
            [Stock] = new() { "S1", "D", "S2", "S3", "S4", "S5", "S6" },
            [Mod] = new() { "D", "M1", "M2" },
        };

        var redo = RaceControlControl.RedealAcrossClasses(
            new[] { own, shared },
            className => rosters[className],
            _ => true,
            (round, transponder) => MixedHeats.ClassOfDriver(round, transponder));

        Assert.Contains("D", redo.Rosters[own.Id]);
        Assert.Contains("D", redo.Rosters[shared.Id]);
        Assert.Equal(Mod, redo.ClassOf[shared.Id]["D"]);

        // Written onto the heat as Redo writes it, D's run there is Mod's.
        shared.AssignedTransponders = redo.Rosters[shared.Id];
        shared.SharedClassOf = redo.ClassOf[shared.Id];
        Assert.Equal(Mod, MixedHeats.ClassOfDriver(shared, "D"));
    }

    [Fact]
    public void A_one_class_heat_gets_no_class_record()
    {
        var heat = Heat(Stock, "Q2 Heat 1", "S1", "S2");

        var redo = RaceControlControl.RedealAcrossClasses(
            new[] { heat },
            _ => new[] { "S1", "S2" },
            _ => true,
            (round, transponder) => MixedHeats.ClassOfDriver(round, transponder));

        Assert.True(redo.Rosters.ContainsKey(heat.Id));
        Assert.Empty(redo.ClassOf);
    }

    private static RoundDefinition Heat(string className, string label, params string[] drivers) => new()
    {
        ClassName = className,
        RoundLabel = label,
        Kind = RoundKind.Qualifier,
        AssignedTransponders = drivers.ToList(),
    };

    private static List<string> Sorted(IEnumerable<string> drivers) =>
        drivers.OrderBy(t => t, StringComparer.Ordinal).ToList();
}
