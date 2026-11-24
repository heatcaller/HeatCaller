namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): picking a pack for a race that was removed
/// (from the phone) while the picker was open wrote "no pack recorded" over
/// the race's real pack. BatteryActions.TagSession now refuses instead.
/// </summary>
public sealed class BugHunt24_BatteryFormsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static BatteryPack Add(BatteryPackStore packs, string id, string owner)
    {
        var pack = new BatteryPack { Id = id, Label = id, OwnerTransponder = owner };
        packs.Upsert(pack);
        return pack;
    }

    [Fact]
    public void TagSession_ForAPackThatHasGone_LeavesTheRecordedPackAndNoUndoStep()
    {
        var packs = BatteryPackStore.Load();
        var undo = new UndoService();
        Add(packs, "w", "100");
        Add(packs, "u", "100");
        packs.Tag("1", "100", "w", PackTagSource.DriverHub);
        packs.Delete("u");
        var steps = undo.Count;

        Assert.False(BatteryActions.TagSession(packs, undo, "1", "Heat 1", "100", "Maya", "u", PackTagSource.DriverHub, intoCar: false));

        Assert.Equal("w", packs.PackIdFor("1", "100"));
        Assert.Equal(steps, undo.Count);
    }

    [Fact]
    public void TagSession_FromRaceControl_ForAPackThatHasGone_LeavesTheCarAlone()
    {
        var packs = BatteryPackStore.Load();
        var undo = new UndoService();
        Add(packs, "w", "100");
        Add(packs, "u", "100");
        packs.SetCurrent("100", "w");
        packs.Tag("1", "100", "w", PackTagSource.InTheCar);
        packs.Delete("u");
        var steps = undo.Count;

        Assert.False(BatteryActions.TagSession(packs, undo, "1", "Heat 1", "100", "Maya", "u", PackTagSource.RaceControl, intoCar: true));

        Assert.Equal("w", packs.PackIdFor("1", "100"));
        Assert.Equal("w", packs.CurrentPackId("100"));
        Assert.Equal(steps, undo.Count);
    }

    [Fact]
    public void TagSession_WithNoPack_StillClearsTheRecordedPack()
    {
        var packs = BatteryPackStore.Load();
        var undo = new UndoService();
        Add(packs, "w", "100");
        packs.Tag("1", "100", "w", PackTagSource.DriverHub);

        Assert.True(BatteryActions.TagSession(packs, undo, "1", "Heat 1", "100", "Maya", null, PackTagSource.DriverHub, intoCar: false));

        Assert.Null(packs.PackIdFor("1", "100"));
        Assert.Equal(1, undo.Count);
    }
}
