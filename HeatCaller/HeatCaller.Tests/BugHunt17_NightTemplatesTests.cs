namespace HeatCaller.Tests;

/// <summary>
/// Bug Hunt 17 (13 Nov 2026): renaming a class follows into the night
/// templates and into a race day's class running order. Left behind, a night
/// started from a template left the renamed class out, and the renamed class
/// ran last on every day that had an order set.
/// </summary>
public sealed class BugHunt17_NightTemplatesTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_renamed_class_is_renamed_in_every_template_that_lists_it()
    {
        var tuesday = new NightTemplate { Name = "Tuesday", ClassNames = { "Stock", "Mod" }, ClassRunningOrder = { "mod", "Stock" } };
        var other = new NightTemplate { Name = "Trucks", ClassNames = { "Truck" } };
        var templates = new List<NightTemplate> { tuesday, other };

        Assert.True(NightTemplates.RenameClass(templates, "Mod", "Modified"));

        Assert.Equal(new[] { "Stock", "Modified" }, tuesday.ClassNames);
        Assert.Equal(new[] { "Modified", "Stock" }, tuesday.ClassRunningOrder);
        Assert.Equal(new[] { "Truck" }, other.ClassNames);

        // A night started from it now races the class under its new name.
        var tonight = new RaceEvent { Name = "Tonight" };
        NightTemplates.ApplyTo(tuesday, tonight);
        Assert.Contains("Modified", tonight.ClassNames);
        Assert.DoesNotContain("Mod", tonight.ClassNames);
    }

    [Fact]
    public void Nothing_to_rename_changes_nothing_and_says_so()
    {
        var template = new NightTemplate { Name = "Tuesday", ClassNames = { "Stock" } };

        Assert.False(NightTemplates.RenameClass(new List<NightTemplate> { template }, "Mod", "Modified"));
        Assert.False(NightTemplates.RenameClass(new List<NightTemplate> { template }, "Stock", " "));
        Assert.False(NightTemplates.RenameClass(null, "Stock", "Stocker"));
        Assert.Equal(new[] { "Stock" }, template.ClassNames);
    }

    [Fact]
    public void A_template_that_listed_both_names_lists_the_new_one_once()
    {
        var template = new NightTemplate { Name = "Tuesday", ClassNames = { "Mod", "Modified" } };

        Assert.True(NightTemplates.RenameClass(new List<NightTemplate> { template }, "Mod", "Modified"));

        Assert.Equal(new[] { "Modified" }, template.ClassNames);
    }

    [Fact]
    public void A_race_day_s_running_order_follows_the_rename_and_its_undo()
    {
        var store = RaceEventStore.Load();

        // One day lists its classes; the other races every class, so lists
        // none, and still has an order.
        var listed = new RaceEvent { Name = "Listed", ClassNames = { "Stock", "Mod" }, ClassRunningOrder = { "Mod", "Stock" } };
        var everyClass = new RaceEvent { Name = "Every class", ClassRunningOrder = { "Mod" } };
        store.Upsert(listed);
        store.Upsert(everyClass);

        var leaveAlone = store.WithClassName("Modified");
        var moved = store.RenameClass("Mod", "Modified");

        Assert.Equal(new[] { "Modified", "Stock" }, store.Get(listed.Id)!.ClassRunningOrder);
        Assert.Equal(new[] { "Modified" }, store.Get(everyClass.Id)!.ClassRunningOrder);

        // Saved, even for the day whose ClassNames did not change.
        Assert.Equal(new[] { "Modified" }, RaceEventStore.Load().Get(everyClass.Id)!.ClassRunningOrder);

        store.UndoRenameClass("Modified", "Mod", leaveAlone, moved);

        Assert.Equal(new[] { "Mod", "Stock" }, store.Get(listed.Id)!.ClassRunningOrder);
        Assert.Equal(new[] { "Stock", "Mod" }, store.Get(listed.Id)!.ClassNames);
        Assert.Equal(new[] { "Mod" }, store.Get(everyClass.Id)!.ClassRunningOrder);
    }
}
