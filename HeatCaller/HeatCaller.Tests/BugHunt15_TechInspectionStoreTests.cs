namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15 (B19), the tech inspection store: a null entry or null
/// lists in tech-inspections.json must not make every tech check throw,
/// and a file holding the literal "null" must not be saved over.
/// </summary>
public sealed class BugHunt15_TechInspectionStoreTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string Good =
        "{\"Transponder\":\"T1\",\"ClassName\":\"Stock\",\"Result\":0,\"EventId\":\"\",\"InspectedAtUtc\":\"2099-01-01T00:00:00Z\"}";

    private void WriteFile(string json)
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(_data.PathOf("tech-inspections.json"), json);
    }

    [Fact]
    public void A_null_entry_is_dropped_and_tech_checks_still_work()
    {
        WriteFile("[null, " + Good + "]");

        var store = TechInspectionStore.Load();

        Assert.False(store.LoadedUnreadable);
        Assert.Single(store.All);
        Assert.NotNull(store.Today("T1", "Stock"));
        Assert.Single(store.CountingNow());
    }

    [Fact]
    public void Null_lists_and_strings_are_filled_in()
    {
        WriteFile("[{\"Transponder\":\"T1\",\"Id\":null,\"ClassName\":null,\"EventId\":null,\"ItemsPassed\":null,\"ItemsFailed\":null,\"Notes\":null,\"Inspector\":null,\"DriverName\":null,\"CarModel\":null}]");

        var store = TechInspectionStore.Load();
        var only = Assert.Single(store.All);

        Assert.NotNull(only.Id);
        Assert.NotNull(only.ItemsPassed);
        Assert.NotNull(only.ItemsFailed);
        Assert.Equal(string.Empty, only.ClassName);
        Assert.Equal(string.Empty, only.EventId);
        Assert.Equal(string.Empty, only.Notes);
        Assert.Null(store.Today("T1", "Stock"));
    }

    [Fact]
    public void A_file_holding_literal_null_is_not_saved_over()
    {
        WriteFile("null");

        var store = TechInspectionStore.Load();
        Assert.True(store.LoadedUnreadable);

        store.Upsert(new TechInspection { Transponder = "T2", ClassName = "Stock" });

        Assert.Equal("null", File.ReadAllText(_data.PathOf("tech-inspections.json")));
    }

    [Fact]
    public void Restore_repairs_a_snapshot_with_a_null_entry()
    {
        var store = TechInspectionStore.Load();
        store.Restore("[null, " + Good + "]");

        Assert.Single(store.All);
        Assert.NotNull(store.Today("T1", "Stock"));
    }
}
