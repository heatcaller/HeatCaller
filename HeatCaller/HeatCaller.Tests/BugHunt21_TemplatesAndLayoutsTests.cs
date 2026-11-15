using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 21, Setup's night settings, templates and Track Layouts:
/// #7 - a new day took its breaks, entries-close and out-by from the last day
/// at the venue that HAD them, so ones the director took off came back;
/// #14 - the template editor showed a flat 3 rounds and heat size 8 for a
/// template that never set its own, not what a night from it would use;
/// #15 - renaming a template orphaned the nights started from it, so its
/// forecast lost their history;
/// #27 - Use Tonight moved the day to the new layout but left tonight's
/// unrun rounds on the old one.
/// </summary>
public sealed class BugHunt21_TemplatesAndLayoutsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static void OnUiThread(Action body, string where) =>
        StaThread.Run(() =>
        {
            body();
            return true;
        }, TimeSpan.FromMinutes(1), () => where);

    // --- #7 ------------------------------------------------------------------------

    [Fact]
    public void Breaks_and_times_taken_off_last_week_stay_off_this_week()
    {
        var store = RaceEventStore.Load();

        var twoWeeksAgo = EventSpine.StartEvent(store, "Two weeks ago", venueName: "Clubhouse");
        twoWeeksAgo.StartedUtc = DateTime.UtcNow.AddDays(-14);
        twoWeeksAgo.EntriesCloseLocalTime = TimeSpan.FromHours(18.5);
        twoWeeksAgo.OutByLocalTime = TimeSpan.FromHours(22);
        twoWeeksAgo.Breaks = new List<PlannedBreak> { new() { AfterRound = 2, Minutes = 10, What = "water the track" } };
        twoWeeksAgo.FinishedUtc = DateTime.UtcNow.AddDays(-14).AddHours(4);
        store.Upsert(twoWeeksAgo);

        // Last week the director took all three off.
        var lastWeek = EventSpine.StartEvent(store, "Last week", venueName: "Clubhouse");
        lastWeek.StartedUtc = DateTime.UtcNow.AddDays(-7);
        lastWeek.EntriesCloseLocalTime = null;
        lastWeek.OutByLocalTime = null;
        lastWeek.Breaks = new List<PlannedBreak>();
        lastWeek.FinishedUtc = DateTime.UtcNow.AddDays(-7).AddHours(4);
        store.Upsert(lastWeek);

        var tonight = EventSpine.StartEvent(store, "Tonight", venueName: "Clubhouse");

        Assert.Null(tonight.EntriesCloseLocalTime);
        Assert.Null(tonight.OutByLocalTime);
        Assert.Empty(tonight.Breaks);
    }

    [Fact]
    public void Last_week_s_breaks_and_times_still_carry_over()
    {
        var store = RaceEventStore.Load();

        var lastWeek = EventSpine.StartEvent(store, "Last week", venueName: "Clubhouse");
        lastWeek.StartedUtc = DateTime.UtcNow.AddDays(-7);
        lastWeek.OutByLocalTime = TimeSpan.FromHours(22);
        lastWeek.EntriesCloseLocalTime = TimeSpan.FromHours(18.5);
        lastWeek.Breaks = new List<PlannedBreak> { new() { AfterRound = 2, Minutes = 10, What = "water the track" } };
        store.Upsert(lastWeek);

        var tonight = EventSpine.StartEvent(store, "Tonight", venueName: "Clubhouse");

        Assert.Equal(TimeSpan.FromHours(22), tonight.OutByLocalTime);
        Assert.Equal(TimeSpan.FromHours(18.5), tonight.EntriesCloseLocalTime);
        Assert.Equal("water the track", Assert.Single(tonight.Breaks).What);
    }

    // --- #14 -----------------------------------------------------------------------

    private static NightFields FieldsOf(TemplateEditForm form) =>
        (NightFields)typeof(TemplateEditForm).GetField("_fields", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

    [Fact]
    public void A_template_with_no_rounds_or_heat_size_shows_the_night_s_own_and_can_save_8()
    {
        OnUiThread(() =>
        {
            var template = new NightTemplate { Name = "Tuesday club night", ClassNames = new List<string> { "Stock" } };

            using var form = new TemplateEditForm(
                template, isNew: false, Array.Empty<string>(), new[] { "Stock" },
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<Season>(), forecast: null,
                roundsUnset: 5, heatSizeUnset: 6);

            var fields = FieldsOf(form);

            Assert.Equal(5, (int)fields.Rounds.Value);
            Assert.Equal(6, (int)fields.HeatSize.Value);

            // Shown, not written.
            Assert.Equal(0, form.AsItStands.QualifyingRounds);
            Assert.Equal(0, form.AsItStands.DriversPerHeat);

            // 8 is now a change, so it's kept.
            fields.HeatSize.Value = 8;
            Assert.Equal(8, form.AsItStands.DriversPerHeat);
        }, "Template editor, the night's own numbers");
    }

    [Fact]
    public void A_template_card_with_no_rounds_says_last_night_s()
    {
        OnUiThread(() =>
        {
            var classes = RaceClassStore.Load();
            classes.Upsert(new RaceClass { Name = "Stock" });

            var events = RaceEventStore.Load();
            events.Upsert(new RaceEvent
            {
                Name = "Last week",
                ClassNames = new List<string> { "Stock" },
                QualifyingRounds = 5,
                StartedUtc = DateTime.UtcNow.AddDays(-7),
                FinishedUtc = DateTime.UtcNow.AddDays(-7).AddHours(4),
            });

            using var page = new RaceDayEventControl(
                events, VenueStore.Load(), TrackLayoutStore.Load(), SeasonStore.Load(), CheckInStore.Load(),
                RoundDefinitionStore.Load(), DayScheduleStore.Load(), classes, undo: null, history: null, new AppSettings());

            var rounds = typeof(RaceDayEventControl).GetMethod("Rounds", BindingFlags.Instance | BindingFlags.NonPublic, new[] { typeof(int) })!;

            Assert.Equal(5, (int)rounds.Invoke(page, new object[] { 0 })!);
            Assert.Equal(2, (int)rounds.Invoke(page, new object[] { 2 })!);
        }, "Template card rounds");
    }

    // --- #15 -----------------------------------------------------------------------

    [Fact]
    public void Renaming_a_template_carries_its_nights_with_it()
    {
        var store = RaceEventStore.Load();
        var started = new RaceEvent { Name = "Last week", TemplateName = "Tuesday club night", FinishedUtc = DateTime.UtcNow };
        var other = new RaceEvent { Name = "Points", TemplateName = "Points round", FinishedUtc = DateTime.UtcNow };
        store.Upsert(started);
        store.Upsert(other);

        Assert.Equal(1, store.RenameTemplate("tuesday club night ", "Tuesday night"));

        var reloaded = RaceEventStore.Load();
        Assert.Equal("Tuesday night", reloaded.Get(started.Id)!.TemplateName);
        Assert.Equal("Points round", reloaded.Get(other.Id)!.TemplateName);

        Assert.Equal(0, store.RenameTemplate("Tuesday night", "Tuesday night"));
    }

    [Fact]
    public void Editing_a_template_follows_a_rename_onto_its_nights()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", "RaceDayEventControl.Templates.cs"));

        Assert.Contains("_events.RenameTemplate(template.Name, edited.Name)", source);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("No check.cmd above the test folder.");
    }

    // --- #27 -----------------------------------------------------------------------

    [Fact]
    public void Use_tonight_moves_the_unrun_rounds_and_leaves_the_raced_ones() =>
        StaThread.Run(() =>
        {
            var store = TrackLayoutStore.Load();
            store.Upsert(new TrackLayout { Name = "Drift 2.0" });
            store.Upsert(new TrackLayout { Name = "Home Track" });

            var rounds = RoundDefinitionStore.Load();
            var raced = new RoundDefinition { ClassName = "Stock", RoundLabel = "Q1 Heat 1", TrackLayoutName = "Drift 2.0", LinkedSessionId = "s1" };
            var toRun = new RoundDefinition { ClassName = "Stock", RoundLabel = "Q2 Heat 1", TrackLayoutName = "Drift 2.0" };
            var lastWeeks = new RoundDefinition { ClassName = "Stock", RoundLabel = "Q2 Heat 1", TrackLayoutName = "Drift 2.0" };
            rounds.Add(raced);
            rounds.Add(toRun);
            rounds.Add(lastWeeks);

            var events = RaceEventStore.Load();
            var day = new RaceEvent { Name = "Friday Club Night", TrackLayoutName = "Drift 2.0", RoundIds = new List<string> { raced.Id, toRun.Id } };
            events.Upsert(day);
            events.SetActive(day.Id);

            var undo = new UndoService();
            var settings = new AppSettings { LastViewedTrackLayoutName = "Home Track" };

            using (var page = new TrackLayoutManagerControl(store, settings, undo, null, events, rounds))
            {
                page.UseTonight();

                Assert.Equal("Home Track", events.Get(day.Id)!.TrackLayoutName);
                Assert.Equal("Home Track", rounds.Get(toRun.Id)!.TrackLayoutName);
                Assert.Equal("Drift 2.0", rounds.Get(raced.Id)!.TrackLayoutName);
                Assert.Equal("Drift 2.0", rounds.Get(lastWeeks.Id)!.TrackLayoutName);

                undo.Undo();

                Assert.Equal("Drift 2.0", events.Get(day.Id)!.TrackLayoutName);
                Assert.Equal("Drift 2.0", rounds.Get(toRun.Id)!.TrackLayoutName);
            }

            return 0;
        }, TimeSpan.FromMinutes(1), () => "Use Tonight with rounds");
}
