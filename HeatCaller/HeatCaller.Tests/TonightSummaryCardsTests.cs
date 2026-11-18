using System.Drawing;
using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>
/// Setup's Tonight tab as summary cards (17 Nov 2026, mock-up B): a banner
/// with what tonight is, then cards that say what is set and open their
/// fields on Change - and the times saying "Not set" rather than showing a
/// grayed-out time with a tick after it.
/// </summary>
public sealed class TonightSummaryCardsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static RaceDayEventControl Build(RaceEventStore events, AppSettings settings)
    {
        var classes = RaceClassStore.Load();

        foreach (var name in new[] { "Stock", "Mod" })
        {
            if (!classes.All.Any(c => c.Name == name))
            {
                classes.Upsert(new RaceClass { Name = name });
            }
        }

        return new RaceDayEventControl(
            events, VenueStore.Load(), TrackLayoutStore.Load(), SeasonStore.Load(), CheckInStore.Load(),
            RoundDefinitionStore.Load(), DayScheduleStore.Load(), classes, undo: null, history: null, settings);
    }

    private static RaceEventStore OneNight()
    {
        var events = RaceEventStore.Load();
        var day = new RaceEvent
        {
            Name = "Thursday club night",
            ClassNames = new List<string> { "Stock", "Mod" },
            DriversPerHeat = 6,
            EntryFee = 20m,
            ExtraClassFee = 10m,
            OutByLocalTime = TimeSpan.FromHours(17),
        };

        events.Upsert(day);
        events.SetActive(day.Id);
        events.Save();

        return events;
    }

    private static void OnUiThread(Action body) =>
        StaThread.Run(() =>
        {
            body();
            return true;
        }, TimeSpan.FromMinutes(1), () => "Tonight's summary cards");

    /// <summary>Shown off-screen, so Visible reads true for what is on show.</summary>
    private static Form Host(Control page)
    {
        var form = new Form
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-20000, -20000),
            Size = new Size(1500, 1200),
            ShowInTaskbar = false,
        };

        page.Dock = DockStyle.Fill;
        form.Controls.Add(page);
        form.Show();
        Application.DoEvents();

        return form;
    }

    [Fact]
    public void Every_card_starts_folded_and_Change_opens_its_fields_until_Done()
    {
        OnUiThread(() =>
        {
            using var page = Build(OneNight(), new AppSettings());
            using var form = Host(page);

            page.RefreshAll();
            ((IPagedControl)page).ShownPage = RaceDayEventControl.TonightTab;
            Application.DoEvents();

            Assert.Equal(new[] { "The night", "Times", "Heats", "Money", "Running the night" }, page.TonightCards.Select(card => card.Name));
            Assert.All(page.TonightCards, card => Assert.False(card.Open));
            Assert.All(page.TonightCards, card => Assert.Equal("Change", card.Toggle.Text));

            var heats = page.TonightCards.Single(card => card.Name == "Heats");

            Assert.False(page.TonightFields.Classes.Visible);

            heats.Toggle.PerformClick();
            Application.DoEvents();

            Assert.True(heats.Open);
            Assert.Equal("Done", heats.Toggle.Text);
            Assert.True(page.TonightFields.Classes.Visible);

            heats.Toggle.PerformClick();
            Application.DoEvents();

            Assert.False(heats.Open);
            Assert.False(page.TonightFields.Classes.Visible);
        });
    }

    [Fact]
    public void The_summaries_say_what_the_night_holds_and_follow_a_change()
    {
        OnUiThread(() =>
        {
            using var page = Build(OneNight(), new AppSettings());
            using var form = Host(page);

            page.RefreshAll();
            Application.DoEvents();

            Assert.Equal("Thursday club night", page.HeroName);
            Assert.Contains("2 classes", page.HeroMeta);
            Assert.Equal(new[] { "Stock", "Mod" }.OrderBy(n => n), page.ClassesSummary.OrderBy(n => n));
            Assert.Equal(20m.ToString("C") + " · " + 10m.ToString("C") + " each extra class", page.MoneySummary);

            var times = page.TimesSummary;

            Assert.Equal("Not set", times[0]);
            Assert.Equal("Not set", times[1]);
            Assert.Equal(DateTime.Today.AddHours(17).ToString("t"), times[3]);

            // Setting the first race shows on its tile at once.
            page.TonightFields.FirstRace.Value = TimeSpan.FromHours(10);
            page.TonightFields.FirstRaceSet.Checked = true;
            Application.DoEvents();

            Assert.Equal(DateTime.Today.AddHours(10).ToString("t"), page.TimesSummary[1]);
        });
    }

    [Fact]
    public void A_time_with_none_says_Not_set_and_Set_a_time_gives_it_one_that_Clear_takes_away()
    {
        OnUiThread(() =>
        {
            var fields = new NightFields(new TextSize());
            var grid = new FieldGrid();

            fields.AddTimesRows(grid);

            using var form = Host(grid);

            var row = fields.FirstRace.Parent!;
            Button ButtonSaying(string text) => row.Controls.OfType<Button>().Single(b => b.Text == text);
            Label notSet = row.Controls.OfType<Label>().Single(l => l.Text == "Not set");

            // No time: the words, not a grayed-out 10:00 with a tick after it.
            Assert.False(fields.FirstRaceSet.Checked);
            Assert.False(fields.FirstRaceSet.Visible);
            Assert.False(fields.FirstRace.Visible);
            Assert.True(notSet.Visible);

            ButtonSaying("Set a time").PerformClick();
            Application.DoEvents();

            Assert.True(fields.FirstRaceSet.Checked);
            Assert.True(fields.FirstRace.Visible);
            Assert.False(notSet.Visible);

            ButtonSaying("Clear").PerformClick();
            Application.DoEvents();

            Assert.False(fields.FirstRaceSet.Checked);
            Assert.False(fields.FirstRace.Visible);
            Assert.True(notSet.Visible);
        });
    }
}
