namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, Move Drivers Between Heats.
///
/// #186: a driver seated in a heat under his second car's transponder was
/// shown twice - once as not in a heat - and could be dropped into a second
/// heat of the same round under his main one. The form now files every id
/// under its owner's main transponder, as the phone's move does.
///
/// #20: which heats were locked was worked out once, when the window
/// opened, so a heat that went on track (Auto start) or was raced while the
/// window stayed open had its drivers rewritten by Save. Save now asks again.
/// </summary>
public sealed class BugHunt24_HeatBalanceFormTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static RoundDefinition Heat(string label, params string[] drivers) => new()
    {
        ClassName = "Stock",
        RoundLabel = label,
        Kind = RoundKind.Qualifier,
        AssignedTransponders = drivers.ToList(),
    };

    /// <summary>Max: main transponder A, second car B.</summary>
    private static (ProfileStore Store, DriverProfile Max) MaxWithTwoCars()
    {
        var store = ProfileStore.Load();
        var max = new DriverProfile { Transponder = "A", DriverName = "Max Power" };
        max.Transponders.Add(new OwnedTransponder { Id = "B" });
        Assert.True(store.TryAdd(max, out _));
        return (store, max);
    }

    // ---- #186: a driver seated under his second transponder ----

    [Fact]
    public void A_driver_seated_under_his_second_transponder_is_one_driver_on_the_form()
    {
        var (store, max) = MaxWithTwoCars();

        var universe = HeatBalanceForm.UniverseOf(
            new[] { Heat("Q1 Heat 1", "B"), Heat("Q1 Heat 2") },
            new[] { max },
            store);

        // Was two entries, A and B, both "Max Power".
        Assert.Single(universe);
        Assert.Equal("Max Power", universe["A"]);
    }

    [Fact]
    public void A_driver_seated_under_his_second_transponder_counts_as_in_a_heat()
    {
        var (store, _) = MaxWithTwoCars();

        var seated = HeatBalanceForm.SeatedIds(new[] { "B" }, t => HeatBalanceForm.MainOf(store, t));

        Assert.Contains("A", seated);
        Assert.Contains("B", seated);
    }

    [Fact]
    public void A_driver_seated_under_his_second_transponder_cannot_be_put_in_another_heat_of_that_round()
    {
        var (store, _) = MaxWithTwoCars();
        string MainOf(string t) => HeatBalanceForm.MainOf(store, t);

        var one = Heat("Q1 Heat 1", "B");
        var two = Heat("Q1 Heat 2");

        var heats = HeatBalanceForm.ByMainTransponder(new List<HeatBalanceForm.HeatState>
        {
            new(one, Locked: false, one.AssignedTransponders),
            new(two, Locked: false, two.AssignedTransponders),
        }, MainOf);

        var refusal = HeatBalanceForm.DescribeMoveBlocker(
            heats, source: null, target: two, MainOf("A"), roundsAreOneRaceDay: true, _ => "Max Power");

        Assert.NotNull(refusal);
        Assert.Contains("already in Q1 Heat 1", refusal);
    }

    [Fact]
    public void A_driver_seated_under_his_second_transponder_keeps_his_own_pace()
    {
        var (store, _) = MaxWithTwoCars();
        var pace = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase) { ["A"] = 17_500, ["C"] = 19_000 };

        var withAlternates = HeatBalanceForm.PaceWithSeatedAlternates(
            new[] { "B", "C", "D" }, pace, t => HeatBalanceForm.MainOf(store, t));

        Assert.Equal(17_500, withAlternates["B"]);
        Assert.Equal(19_000, withAlternates["C"]);
        Assert.False(withAlternates.ContainsKey("D"));
    }

    [Fact]
    public void The_form_maps_every_heat_id_to_the_main_transponder()
    {
        var source = Source("HeatBalanceForm.cs");

        Assert.Contains("UniverseOf(rounds, classRoster, profiles)", source, StringComparison.Ordinal);
        Assert.Contains("MainOf(drag.Transponder)", source, StringComparison.Ordinal);
        Assert.Contains("racing.UnionWith(SeatedIds(column.Current, MainOf))", source, StringComparison.Ordinal);
        Assert.Contains("PaceWithSeatedAlternates(drivers, _pace, MainOf)", source, StringComparison.Ordinal);
    }

    // ---- #20: a heat that moved on while the window was open ----

    private static HeatBalanceForm.RosterChange MoveIntoHeat3(RoundDefinition heat3) =>
        new(heat3.Id, heat3.AssignedTransponders.ToList(), heat3.AssignedTransponders.Append("LATE").ToList());

    [Fact]
    public void Save_is_refused_when_a_changed_heat_went_on_track_while_the_window_was_open()
    {
        var heat3 = Heat("Q1 Heat 3", "A", "C");
        var changes = new[] { MoveIntoHeat3(heat3) };

        var refusal = HeatBalanceForm.DescribeStaleSave(changes, id => id == heat3.Id ? heat3 : null, onTrackNow: heat3.Id);

        Assert.NotNull(refusal);
        Assert.Contains("Q1 Heat 3 went on track", refusal);
        Assert.Contains("Nothing was saved", refusal);
    }

    [Fact]
    public void Save_is_refused_when_a_changed_heat_was_raced_and_saved_while_the_window_was_open()
    {
        var heat3 = Heat("Q1 Heat 3", "A", "C");
        var changes = new[] { MoveIntoHeat3(heat3) };
        heat3.LinkedSessionId = "session-1";

        var refusal = HeatBalanceForm.DescribeStaleSave(changes, id => id == heat3.Id ? heat3 : null, onTrackNow: null);

        Assert.NotNull(refusal);
        Assert.Contains("Q1 Heat 3 was raced and saved", refusal);
    }

    [Fact]
    public void Save_is_refused_when_a_changed_heat_had_its_drivers_changed_elsewhere()
    {
        var heat3 = Heat("Q1 Heat 3", "A", "C");
        var changes = new[] { MoveIntoHeat3(heat3) };
        heat3.AssignedTransponders.Remove("C");

        var refusal = HeatBalanceForm.DescribeStaleSave(changes, id => id == heat3.Id ? heat3 : null, onTrackNow: null);

        Assert.NotNull(refusal);
        Assert.Contains("changed somewhere else", refusal);
    }

    [Fact]
    public void Save_is_refused_when_a_changed_heat_was_deleted()
    {
        var heat3 = Heat("Q1 Heat 3", "A", "C");

        Assert.NotNull(HeatBalanceForm.DescribeStaleSave(new[] { MoveIntoHeat3(heat3) }, _ => null, onTrackNow: null));
    }

    [Fact]
    public void Save_goes_ahead_when_nothing_moved_underneath()
    {
        var heat2 = Heat("Q1 Heat 2", "D");
        var heat3 = Heat("Q1 Heat 3", "A", "C");
        var changes = new[] { MoveIntoHeat3(heat3) };

        // The heat on track is another one - the one just run.
        Assert.Null(HeatBalanceForm.DescribeStaleSave(changes, id => id == heat3.Id ? heat3 : null, onTrackNow: heat2.Id));
    }

    [Fact]
    public void Save_asks_again_before_it_writes()
    {
        var source = Source("HeatBalanceForm.cs");
        var commit = source[source.IndexOf("private void Commit()", StringComparison.Ordinal)..];

        Assert.Contains("DescribeStaleSave(changes,", commit, StringComparison.Ordinal);
        Assert.True(commit.IndexOf("DescribeStaleSave(changes,", StringComparison.Ordinal)
                    < commit.IndexOf("Changes = changes;", StringComparison.Ordinal));
    }

    /// <summary>The app source, for checks on how the pieces are wired in.</summary>
    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }
}
