namespace HeatCaller.Tests;

/// <summary>
/// Two track layouts may not share a name. Everything downstream finds a
/// layout by its name alone (the Race Control picker, the all-time record,
/// the Demo pace), so a second "Club Oval" read and wrote the first one's
/// record. TrackLayoutStore.IsNameTaken is what the edit dialog and the
/// Track Designer ask before saving.
/// </summary>
public sealed class BugHuntTrackNamesTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_name_another_layout_has_is_taken_ignoring_case_and_spaces()
    {
        var store = TrackLayoutStore.Load();
        var oval = new TrackLayout { Name = "Club Oval" };
        store.Upsert(oval);

        Assert.True(store.IsNameTaken("Club Oval", null));
        Assert.True(store.IsNameTaken("  club oval ", null));
        Assert.True(store.IsNameTaken("CLUB OVAL", new TrackLayout().Id));
    }

    [Fact]
    public void A_layout_keeping_its_own_name_is_not_a_clash()
    {
        var store = TrackLayoutStore.Load();
        var oval = new TrackLayout { Name = "Club Oval" };
        store.Upsert(oval);

        Assert.False(store.IsNameTaken("Club Oval", oval.Id));
        Assert.False(store.IsNameTaken("club oval", oval.Id));
    }

    [Fact]
    public void A_new_or_blank_name_is_not_taken()
    {
        var store = TrackLayoutStore.Load();
        store.Upsert(new TrackLayout { Name = "Club Oval" });

        Assert.False(store.IsNameTaken("Club Oval Reverse", null));
        Assert.False(store.IsNameTaken("   ", null));
        Assert.False(store.IsNameTaken(string.Empty, null));
    }
}
