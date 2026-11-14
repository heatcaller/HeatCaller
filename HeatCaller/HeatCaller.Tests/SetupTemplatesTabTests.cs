namespace HeatCaller.Tests;

/// <summary>
/// Setup's three tabs (14 Nov 2026, direction A): Templates where a night
/// starts, Tonight for the day's own settings, Club rules for every night -
/// and nothing on Templates starting a second night over an open one.
/// </summary>
public sealed class SetupTemplatesTabTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static NightTemplate Points() => new()
    {
        Name = "Points round",
        ClassNames = new List<string> { "Stock", "Mod" },
        QualifyingRounds = 3,
        DriversPerHeat = 8,
        FirstRaceLocalTime = TimeSpan.FromHours(10),
        OutByLocalTime = TimeSpan.FromHours(17),
        EntryFee = 25,
        ExtraClassFee = 10,
        Weekdays = Enum.GetValues<DayOfWeek>().ToList(),
    };

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

    private static void OnUiThread(Action body) =>
        StaThread.Run(() =>
        {
            body();
            return true;
        }, TimeSpan.FromMinutes(1), () => "Setup's Templates tab");

    [Fact]
    public void With_no_night_open_Setup_opens_on_Templates_with_tonights_template_on_the_banner()
    {
        OnUiThread(() =>
        {
            var settings = new AppSettings { NightTemplates = new List<NightTemplate> { Points() } };
            using var page = Build(RaceEventStore.Load(), settings);

            Assert.Equal(RaceDayEventControl.TemplatesTab, page.SelectedTab);
            Assert.Equal("Points round is set for tonight", page.BannerTitle);
            Assert.True(page.BannerOffersStart);

            // The template, then something different - no last night to go like.
            Assert.Equal(2, page.ShelfCards.Count);
            Assert.All(page.ShelfCards, card => Assert.True(card.CanStart));
        });
    }

    [Fact]
    public void Once_a_night_is_open_Setup_shows_Tonight_and_Templates_refuses_to_start_another()
    {
        OnUiThread(() =>
        {
            var settings = new AppSettings { NightTemplates = new List<NightTemplate> { Points() } };
            var events = RaceEventStore.Load();
            using var page = Build(events, settings);

            Assert.True(page.StartNewDayFromWizard(RaceDayShape.RaceDay, new[] { "Stock" }));
            page.RefreshAll();

            Assert.Equal(RaceDayEventControl.TonightTab, page.SelectedTab);
            Assert.EndsWith("is open", page.BannerTitle);
            Assert.False(page.BannerOffersStart);
            Assert.All(page.ShelfCards, card => Assert.False(card.CanStart));
        });
    }

    [Fact]
    public void A_tab_picked_by_hand_stays_picked_across_a_refresh()
    {
        OnUiThread(() =>
        {
            using var page = Build(RaceEventStore.Load(), new AppSettings());

            page.ShowTab(RaceDayEventControl.ClubRulesTab);
            page.RefreshAll();

            Assert.Equal(RaceDayEventControl.ClubRulesTab, page.SelectedTab);
        });
    }

    // --- What the cards say --------------------------------------------------------

    [Fact]
    public void A_cards_lines_say_classes_heats_times_breaks_and_fees_and_leave_out_what_is_not_set()
    {
        var lines = TemplateCardText.Lines(
            clubNight: false, new[] { "Stock", "Mod" }, rounds: 3, perHeat: 8, FirstRoundSeeding.Pace,
            entriesClose: null, firstRace: TimeSpan.FromHours(10), outBy: TimeSpan.FromHours(17),
            breaks: null, entryFee: 25, extraFee: 10);

        Assert.Equal("Stock, Mod", lines[0]);
        Assert.StartsWith("3 heats each, up to 8 a heat", lines[1]);
        Assert.StartsWith("First race", lines[2]);
        Assert.Contains("out by", lines[2]);
        Assert.DoesNotContain("ntries close", lines[2]);
        Assert.Contains("each extra class", lines[3]);
        Assert.Equal(4, lines.Count);
    }

    [Fact]
    public void A_club_nights_card_counts_races_not_heats()
    {
        var lines = TemplateCardText.Lines(true, new[] { "Stock" }, 4, 10, FirstRoundSeeding.Pace, null, null, null, null, 0, 0);

        Assert.Equal("4 races each, up to 10 a race", lines[1]);
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void A_cards_caption_says_tonight_the_kind_of_night_and_when_it_is_on()
    {
        var template = new NightTemplate { Name = "Club", Shape = RaceDayShape.ClubNight, Weekdays = new List<DayOfWeek> { DayOfWeek.Tuesday } };
        var option = new StartTonightOption(StartTonightKind.Template, "Club", "", Template: template);

        Assert.StartsWith("Tonight · Club night · ", TemplateCardText.Caption(option, tonight: true));
        Assert.EndsWith("No nights set", TemplateCardText.Caption(new StartTonightOption(StartTonightKind.Template, "X", "", Template: new NightTemplate()), false));
    }

    [Fact]
    public void A_copy_gets_a_name_no_other_template_has()
    {
        Assert.Equal("Points (copy)", TemplateCardText.CopyName("Points", new[] { "Points" }));
        Assert.Equal("Points (copy 2)", TemplateCardText.CopyName("Points", new[] { "Points", "points (COPY)" }));
    }

    [Fact]
    public void The_refusal_names_the_open_night_and_points_to_Close_Day()
    {
        var words = TemplateCardText.RefusedWords("Points round 14 Nov");

        Assert.Contains("\"Points round 14 Nov\" is still open", words);
        Assert.Contains("Close Day", words);
    }

    // --- The editor ----------------------------------------------------------------

    [Theory]
    [InlineData("", 2, "Give the template a name")]
    [InlineData("Tuesday club night ", 2, "already a template called")]
    [InlineData("Points round", 0, "Tick at least one class")]
    public void The_editor_wont_save_a_template_without_a_name_a_free_name_and_a_class(string name, int classes, string says)
    {
        Assert.Contains(says, TemplateEditForm.Problem(name, classes, new[] { "tuesday club night" }));
    }

    [Fact]
    public void The_editor_saves_a_good_template()
    {
        Assert.Null(TemplateEditForm.Problem("Points round", 2, new[] { "Tuesday club night" }));
    }
}
