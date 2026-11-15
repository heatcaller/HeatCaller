namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 19, Setup's Templates tab and the template editor:
/// #5 - saving a template that never set its heat size or rounds wrote the
/// 8 and 3 the editor only showed, so nights from it stopped using the
/// club's own numbers;
/// #9 - looking at a past closed night made it current, and the tab then
/// offered to start a second night beside tonight's open one;
/// #33 - an out-by time at or after midnight was forecast as about a day
/// past it.
/// </summary>
public sealed class BugHunt19_TemplatesTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static void OnUiThread(Action body, string where) =>
        StaThread.Run(() =>
        {
            body();
            return true;
        }, TimeSpan.FromMinutes(1), () => where);

    // --- #5 ------------------------------------------------------------------------

    [Fact]
    public void A_template_that_never_set_its_heat_size_or_rounds_keeps_them_unset_when_saved()
    {
        OnUiThread(() =>
        {
            NightTemplate? current = null;
            var template = new NightTemplate { Name = "Tuesday club night", ClassNames = new List<string> { "Stock" }, EntryFee = 10 };

            using var form = new TemplateEditForm(
                template, isNew: false, Array.Empty<string>(), new[] { "Stock" },
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<Season>(),
                t =>
                {
                    current = t;
                    return null;
                });

            // The editor works the template out as it opens, for the forecast.
            Assert.NotNull(current);
            Assert.Equal(0, current!.DriversPerHeat);
            Assert.Equal(0, current.QualifyingRounds);
        }, "Template editor, heat size not set");
    }

    [Fact]
    public void A_heat_size_or_rounds_changed_in_the_editor_is_saved()
    {
        Assert.Equal(6, TemplateEditForm.ShownNotWritten(saved: 0, shown: 8, onScreen: 6));
        Assert.Equal(0, TemplateEditForm.ShownNotWritten(saved: 0, shown: 8, onScreen: 8));
        Assert.Equal(8, TemplateEditForm.ShownNotWritten(saved: 8, shown: 8, onScreen: 8));
        Assert.Equal(4, TemplateEditForm.ShownNotWritten(saved: 3, shown: 3, onScreen: 4));
    }

    // --- #9 ------------------------------------------------------------------------

    private static RaceDayEventControl Build(RaceEventStore events, AppSettings settings)
    {
        var classes = RaceClassStore.Load();

        if (!classes.All.Any(c => c.Name == "Stock"))
        {
            classes.Upsert(new RaceClass { Name = "Stock" });
        }

        return new RaceDayEventControl(
            events, VenueStore.Load(), TrackLayoutStore.Load(), SeasonStore.Load(), CheckInStore.Load(),
            RoundDefinitionStore.Load(), DayScheduleStore.Load(), classes, undo: null, history: null, settings);
    }

    [Fact]
    public void Looking_at_a_past_night_does_not_let_Templates_start_a_second_one()
    {
        OnUiThread(() =>
        {
            var settings = new AppSettings
            {
                NightTemplates = new List<NightTemplate>
                {
                    new() { Name = "Points round", ClassNames = new List<string> { "Stock" }, Weekdays = Enum.GetValues<DayOfWeek>().ToList() },
                },
            };
            var events = RaceEventStore.Load();
            using var page = Build(events, settings);

            Assert.True(page.StartNewDayFromWizard(RaceDayShape.RaceDay, new[] { "Stock" }));

            // Last Tuesday, closed, picked to look at its results.
            var past = new RaceEvent
            {
                Name = "Last Tuesday",
                ClassNames = new List<string> { "Stock" },
                StartedUtc = DateTime.UtcNow.AddDays(-7),
                FinishedUtc = DateTime.UtcNow.AddDays(-7).AddHours(4),
            };
            events.Upsert(past);
            events.SetActive(past.Id);
            page.RefreshAll();

            Assert.EndsWith("is open", page.BannerTitle);
            Assert.False(page.BannerOffersStart);
            Assert.All(page.ShelfCards, card => Assert.False(card.CanStart));
        }, "Templates tab, past night current");
    }

    // --- #33 -----------------------------------------------------------------------

    [Theory]
    [InlineData(0, 40)]   // out by midnight, finishes 23:20
    [InlineData(0.5, 70)] // out by 00:30
    public void An_out_by_time_after_midnight_is_after_a_late_finish(double outByHours, int spareMinutes)
    {
        // One class of 8, 1 round: 1 heat + 1 final of 5 min and a 3 min gap - 13 min, from 23:07.
        var forecast = TemplateForecast.For(
            false, new Dictionary<string, int> { ["Stock"] = 8 }, 1, 8,
            _ => TimeSpan.FromMinutes(5), _ => TimeSpan.FromMinutes(5), null,
            new TimeSpan(23, 7, 0), TimeSpan.FromHours(outByHours), TimeSpan.FromMinutes(3))!;

        Assert.Equal(new TimeSpan(23, 20, 0), forecast.Finish);
        Assert.Equal(TimeSpan.FromMinutes(spareMinutes), forecast.Spare);
        Assert.False(TemplateForecast.RunsLate(forecast));
        Assert.Contains("to spare", TemplateForecast.Line(forecast));
    }

    [Fact]
    public void A_night_running_past_midnight_beyond_its_out_by_still_says_so()
    {
        // Out by 23:30, finishes 00:03 the next morning.
        var forecast = TemplateForecast.For(
            false, new Dictionary<string, int> { ["Stock"] = 8 }, 1, 8,
            _ => TimeSpan.FromMinutes(5), _ => TimeSpan.FromMinutes(5), null,
            new TimeSpan(23, 50, 0), TimeSpan.FromHours(23.5), TimeSpan.FromMinutes(3))!;

        Assert.Equal(TimeSpan.FromMinutes(-33), forecast.Spare);
        Assert.True(TemplateForecast.RunsLate(forecast));
    }
}
