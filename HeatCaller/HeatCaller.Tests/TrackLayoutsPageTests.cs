namespace HeatCaller.Tests;

/// <summary>
/// Track Layouts reworked (13 Nov 2026, mock-up B with D's "racing on
/// tonight"): a list of layouts with their plans, the one picked on a stage
/// with its facts, and Use Tonight, which sets the open race day's track.
/// </summary>
public sealed class TrackLayoutsPageTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static (TrackLayoutManagerControl Page, RaceEventStore Events, RaceEvent Day, UndoService Undo) Page(string tonight = "Drift 2.0")
    {
        var store = TrackLayoutStore.Load();
        store.Upsert(new TrackLayout
        {
            Name = "Drift 2.0",
            RecordLapMs = 7412,
            RecordHolderName = "Al Gorithim",
            MarshalPostCount = 2,
            MarshalPostNames = new() { "Hairpin" },
            AverageLapSeconds = 8.9,
        });
        store.Upsert(new TrackLayout { Name = "Home Track" });

        var events = RaceEventStore.Load();
        var day = new RaceEvent { Name = "Friday Club Night", TrackLayoutName = tonight };
        events.Upsert(day);
        events.SetActive(day.Id);

        var undo = new UndoService();
        var settings = new AppSettings { LastViewedTrackLayoutName = "Home Track" };

        return (new TrackLayoutManagerControl(store, settings, undo, null, events), events, day, undo);
    }

    [Fact]
    public void It_lists_every_layout_and_opens_on_the_one_looked_at_last() =>
        StaThread.Run(() =>
        {
            var (page, _, _, _) = Page();

            using (page)
            {
                Assert.Equal(new[] { "Drift 2.0", "Home Track" }, page.Shows.Listed.OrderBy(n => n));
                Assert.Equal("Home Track", page.Shows.Picked);

                // Not tonight's, so no tag.
                Assert.Equal(string.Empty, page.Shows.Tonight);
            }

            return 0;
        }, TimeSpan.FromMinutes(1), () => "the page opening");

    [Fact]
    public void Use_tonight_sets_the_race_day_s_track_and_undo_puts_it_back() =>
        StaThread.Run(() =>
        {
            var (page, events, day, undo) = Page();

            using (page)
            {
                page.UseTonight();

                Assert.Equal("Home Track", events.Get(day.Id)!.TrackLayoutName);

                undo.Undo();

                Assert.Equal("Drift 2.0", events.Get(day.Id)!.TrackLayoutName);
            }

            return 0;
        }, TimeSpan.FromMinutes(1), () => "Use Tonight");

    [Fact]
    public void The_facts_say_the_record_and_what_is_not_set() =>
        StaThread.Run(() =>
        {
            var (page, _, _, _) = Page();

            using (page)
            {
                var drift = TrackLayoutStore.Load().All.Single(t => t.Name == "Drift 2.0");
                var facts = page.FactsFor(drift);

                var record = Assert.Single(facts, f => f.Key == "record");
                Assert.Equal("7.412s", record.Value);
                Assert.Contains("Al Gorithim", record.Detail);

                Assert.Equal("8.9s", Assert.Single(facts, f => f.Key == "lap").Value);
                Assert.Equal("not set", Assert.Single(facts, f => f.Key == "length").Detail);
                Assert.Equal("2", Assert.Single(facts, f => f.Key == "posts").Value);
                Assert.Equal("No plan", Assert.Single(facts, f => f.Key == "pieces").Value);
            }

            return 0;
        }, TimeSpan.FromMinutes(1), () => "the facts strip");

    [Fact]
    public void A_button_given_a_kind_keeps_it_when_the_theme_is_applied_again() =>
        StaThread.Run(() =>
        {
            using var host = new Form();
            var button = new Button { Text = "+ New Layout" };
            host.Controls.Add(button);

            AppTheme.StyleButton(button, AppTheme.ButtonKind.Primary);
            var pink = button.BackColor;

            AppTheme.ApplyRecursively(host);

            Assert.Equal(pink, button.BackColor);
            return 0;
        }, TimeSpan.FromMinutes(1), () => "a button keeping its kind");
}
