namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): Undo of Run Together must not take a shared
/// heat apart while it is on track.
/// </summary>
public sealed class BugHunt24_MixedHeatsTests : IDisposable
{
    // Declared first: the store below loads from wherever it points.
    private readonly TempAppData _data = new();

    private readonly RoundDefinitionStore _rounds = RoundDefinitionStore.Load();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Undo_of_run_together_is_refused_while_the_shared_heat_is_on_track()
    {
        var stock = Heat("Stock", "Q1 Heat 1", "S1", "S2", "S3");
        var mod = Heat("Mod", "Q1 Heat 1", "M1", "M2", "M3", "M4");
        var undo = MixedHeats.RunTogetherIn(_rounds, stock, mod, null, null, out var refusal);
        Assert.Null(refusal);
        Assert.NotNull(undo);

        Assert.False(MixedHeats.UndoRunTogether(_rounds, undo!, stock.Id, out var notUndone));
        Assert.Contains("on track", notUndone);

        // Nothing changed: still the one shared heat, with every driver and class.
        var shared = Assert.Single(_rounds.All);
        Assert.Equal(stock.Id, shared.Id);
        Assert.True(MixedHeats.IsShared(shared));
        Assert.Equal(7, shared.AssignedTransponders.Count);
        Assert.Equal("Mod", MixedHeats.ClassOfDriver(shared, "M2"));
        Assert.Null(_rounds.Get(mod.Id));
    }

    [Fact]
    public void Undo_of_run_together_still_works_when_another_heat_is_on_track()
    {
        var stock = Heat("Stock", "Q1 Heat 1", "S1");
        var mod = Heat("Mod", "Q1 Heat 1", "M1");
        var other = Heat("Buggy", "Q1 Heat 1", "B1");
        var undo = MixedHeats.RunTogetherIn(_rounds, stock, mod, null, null, out _)!;

        Assert.True(MixedHeats.UndoRunTogether(_rounds, undo, other.Id, out var notUndone));
        Assert.Null(notUndone);
        Assert.Equal(3, _rounds.All.Count);
        Assert.False(MixedHeats.IsShared(_rounds.Get(stock.Id)!));
        Assert.NotNull(_rounds.Get(mod.Id));
    }

    private RoundDefinition Heat(string className, string label, params string[] transponders)
    {
        var round = new RoundDefinition { ClassName = className, RoundLabel = label, Kind = RoundKind.Qualifier, AssignedTransponders = transponders.ToList() };
        _rounds.Add(round);
        return round;
    }
}
