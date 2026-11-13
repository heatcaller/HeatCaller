namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), RaceControlControl.NextUp.cs.
/// </summary>
public sealed class BugHunt17_RaceControlControl_NextUpTests
{
    // --- The Quick Race stamp on the 12-hour clock ---------------------------

    /// <summary>
    /// Quick Race writes its stamp with AppTime.TimeOfDay - "Quick Race
    /// 7:05 PM" unless 24-hour time is ticked - and the check for it read
    /// "HH:mm" only. On the default clock no stamp was recognised, so it was
    /// never released and the races after it were filed under it.
    /// </summary>
    [Theory]
    [InlineData("h:mm tt", 19, 5)]
    [InlineData("h:mm tt", 9, 30)]
    [InlineData("h:mm tt", 0, 1)]
    [InlineData("HH:mm", 19, 5)]
    [InlineData("HH:mm", 7, 5)]
    public void A_Quick_Race_stamp_is_released_on_either_clock(string clock, int hour, int minute)
    {
        // The two formats AppTime.TimeOfDay writes, in the culture it writes them in.
        var stamp = $"Quick Race {new DateTime(2026, 11, 13, hour, minute, 0).ToString(clock)}";

        Assert.Equal(stamp, RaceControlControl.QuickRaceStampToRelease(stamp));
    }

    [Theory]
    [InlineData("Quick Race practice")]
    [InlineData("Quick Race 7")]
    [InlineData("Quick Race 25:99")]
    [InlineData("Stock - Q1 Heat 1")]
    public void A_name_that_is_not_the_stamp_is_still_kept(string label)
    {
        Assert.Null(RaceControlControl.QuickRaceStampToRelease(label));
    }

    // --- Redo Remaining Heats and shared (mixed-class) heats -----------------

    private const string Stock = "Stock";
    private const string Mod = "Mod";

    /// <summary>
    /// Stock has two heats in the round and Mod's one small heat was put into
    /// Stock's Heat 2 with Run Together. Redo dealt the Mod drivers over both
    /// heats as if they were Stock, and those in Heat 1 - not shared - were
    /// scored as Stock.
    /// </summary>
    [Fact]
    public void Redo_keeps_the_other_class_in_the_heat_it_shares()
    {
        var heat1 = Heat(Stock, "Q2 Heat 1", "S1", "S2", "S3", "S4");
        var shared = Shared(Stock, Mod, "Q2 Heat 2", "S5", "S6", "S7", "S8", "M1", "M2", "M3");

        var redo = Redo(new[] { heat1, shared }, "S1 S2 S3 S4 S5 S6 S7 S8 M1 M2 M3");

        Assert.DoesNotContain(redo.Rosters[heat1.Id], t => t.StartsWith('M'));
        Assert.Equal(new[] { "M1", "M2", "M3" }, Sorted(redo.Rosters[shared.Id].Where(t => t.StartsWith('M'))));
        Assert.Equal(
            new[] { "S1", "S2", "S3", "S4", "S5", "S6", "S7", "S8" },
            Sorted(redo.Rosters[heat1.Id].Concat(redo.Rosters[shared.Id]).Where(t => t.StartsWith('S'))));
        Assert.Equal(4, redo.Rosters[heat1.Id].Count);
    }

    /// <summary>
    /// The other way round: the shared heat's main class is Mod, and Stock
    /// also has a heat of its own in the round. Redo dealt every Stock driver
    /// into Stock's own heat and left the ones in the shared heat there too -
    /// two heats of the same round.
    /// </summary>
    [Fact]
    public void Redo_never_leaves_a_driver_in_two_heats_of_a_round_when_their_class_shares_another_class_s_heat()
    {
        var own = Heat(Stock, "Q2 Heat 1", "S1", "S2", "S3", "S4");
        var shared = Shared(Mod, Stock, "Q2 Heat 2", "M1", "M2", "M3", "S5", "S6", "S7", "S8");

        var redo = Redo(new[] { own, shared }, "S1 S2 S3 S4 S5 S6 S7 S8 M1 M2 M3");

        var stock = redo.Rosters[own.Id].Concat(redo.Rosters[shared.Id]).Where(t => t.StartsWith('S')).ToList();

        Assert.Equal(new[] { "S1", "S2", "S3", "S4", "S5", "S6", "S7", "S8" }, Sorted(stock));
        Assert.Equal(new[] { "M1", "M2", "M3" }, Sorted(redo.Rosters[shared.Id].Where(t => t.StartsWith('M'))));
        Assert.DoesNotContain(redo.Rosters[own.Id], t => t.StartsWith('M'));
    }

    [Fact]
    public void Redo_deals_a_late_entry_into_a_heat_their_class_only_shares_and_drops_a_no_show()
    {
        // One heat a round for both classes, as the build pairs them.
        var shared = Shared(Stock, Mod, "Q2 Heat 1", "S1", "S2", "S3", "M1", "M2");

        // M3 checked in late; S3 and M2 have gone home.
        var redo = Redo(new[] { shared }, "S1 S2 M1 M3");

        Assert.Equal(new[] { "M1", "M3", "S1", "S2" }, Sorted(redo.Rosters[shared.Id]));
    }

    [Fact]
    public void A_class_with_nobody_checked_in_keeps_its_drivers_in_a_shared_heat()
    {
        var shared = Shared(Stock, Mod, "Q2 Heat 1", "S1", "S2", "M1", "M2");

        var redo = Redo(new[] { shared }, "S1 S2 S3");

        Assert.Equal(new[] { "M1", "M2", "S1", "S2", "S3" }, Sorted(redo.Rosters[shared.Id]));
        Assert.Equal(new[] { Mod }, redo.NobodyIn);
    }

    [Fact]
    public void One_class_heats_are_dealt_exactly_as_before()
    {
        var q1h1 = Heat(Stock, "Q1 Heat 1", "S1", "S2", "S3", "S4");
        q1h1.LinkedSessionId = Guid.NewGuid().ToString("N");
        var q1h2 = Heat(Stock, "Q1 Heat 2", "S5", "S6");
        var q2h1 = Heat(Stock, "Q2 Heat 1");
        var q2h2 = Heat(Stock, "Q2 Heat 2");
        var mod = Heat(Mod, "Q2 Heat 1", "M1", "X9");
        var nobody = Heat("Truck", "Q2 Heat 1", "T1");

        var program = new[] { q1h1, q1h2, q2h1, q2h2, mod, nobody };
        var checkedIn = "S1 S2 S3 S4 S5 S6 S7 M1 M2 X9";

        var redo = Redo(program, checkedIn);

        var stockOnly = RaceControlControl.RedealRemainingHeats(
            new[] { q1h1, q1h2, q2h1, q2h2 }, new[] { "S1", "S2", "S3", "S4", "S5", "S6", "S7" });

        Assert.Equal(stockOnly.Keys.OrderBy(k => k), redo.Rosters.Keys.Where(k => k != mod.Id).OrderBy(k => k));

        foreach (var (id, roster) in stockOnly)
        {
            Assert.Equal(roster, redo.Rosters[id]);
        }

        // X9 was added to the Mod heat by hand, and stays in it.
        Assert.Equal(new[] { "M1", "M2", "X9" }, Sorted(redo.Rosters[mod.Id]));
        Assert.Equal(new[] { "Truck" }, redo.NobodyIn);
        Assert.Empty(redo.LeftAlone);
    }

    private static RaceControlControl.RedoRosters Redo(IReadOnlyList<RoundDefinition> program, string checkedIn)
    {
        var here = checkedIn.Split(' ').ToHashSet(StringComparer.OrdinalIgnoreCase);

        // S... race Stock, M... race Mod, T... Truck; anyone else races nothing.
        static string ClassOf(string transponder) => transponder[0] switch
        {
            'S' => Stock,
            'M' => Mod,
            'T' => "Truck",
            _ => string.Empty,
        };

        return RaceControlControl.RedealAcrossClasses(
            program,
            className => here.Where(t => ClassOf(t) == className).OrderBy(t => t, StringComparer.Ordinal).ToList(),
            here.Contains,
            (round, transponder) => MixedHeats.ClassOfDriver(round, transponder));
    }

    private static RoundDefinition Heat(string className, string label, params string[] drivers) => new()
    {
        ClassName = className,
        RoundLabel = label,
        Kind = RoundKind.Qualifier,
        AssignedTransponders = drivers.ToList(),
    };

    /// <summary>A heat two classes share, each driver recorded in their class as Run Together records them.</summary>
    private static RoundDefinition Shared(string main, string other, string label, params string[] drivers)
    {
        var heat = Heat(main, label, drivers);
        heat.SharedClasses = new List<string> { other };
        heat.SharedClassOf = drivers.ToDictionary(t => t, t => t[0] == 'M' ? Mod : Stock, StringComparer.OrdinalIgnoreCase);
        return heat;
    }

    private static List<string> Sorted(IEnumerable<string> drivers) =>
        drivers.OrderBy(t => t, StringComparer.Ordinal).ToList();
}
