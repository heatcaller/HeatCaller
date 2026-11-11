using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 16 (10 Nov 2026), the Championship page. Coming back to the
/// Standings tab ran RefreshAll, which dropped the championship being looked
/// at and showed the first in the list instead.
/// </summary>
public sealed class BugHunt16_SeasonsRefreshTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Coming_back_to_the_page_stays_on_the_championship_being_looked_at()
    {
        var seasons = SeasonStore.Load();
        seasons.Upsert(new Season { Name = "Summer Series" });
        var winter = new Season { Name = "Winter Series" };
        seasons.Upsert(winter);

        var list = typeof(SeasonsControl).GetField("_lstSeasons", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var (before, after) = StaThread.Run(() =>
        {
            using var page = new SeasonsControl(
                seasons,
                SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>()),
                new UndoService(),
                RoundDefinitionStore.Load(),
                RaceClassStore.Load());
            _ = page.Handle;

            var box = (ListBox)list.GetValue(page)!;
            box.SelectedIndex = 1; // Winter Series - the list is by name
            var before = box.SelectedItem?.ToString();

            page.RefreshAll();

            return (before, box.SelectedItem?.ToString());
        }, TimeSpan.FromSeconds(30), () => "refreshing the Championship page");

        Assert.Contains("Winter Series", before);
        Assert.Contains("Winter Series", after);
    }
}
