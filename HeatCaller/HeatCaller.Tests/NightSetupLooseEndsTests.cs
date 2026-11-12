namespace HeatCaller.Tests;

/// <summary>
/// Setting up the night, step 7 - the loose ends (12 Nov 2026): one
/// transponder moved between two classes, a driver's second transponder,
/// and templates set for particular nights.
/// </summary>
public sealed class NightSetupLooseEndsTests
{
    private static RoundDefinition Heat(string className, string label, params string[] cars) => new()
    {
        ClassName = className,
        RoundLabel = label,
        Kind = RoundKind.Qualifier,
        AssignedTransponders = cars.ToList(),
    };

    // --- One transponder, two classes ---------------------------------------------

    [Fact]
    public void A_car_counts_as_heard_on_any_of_its_driver_s_transponders()
    {
        var driver = new DriverProfile
        {
            Transponder = "100",
            Transponders = { new OwnedTransponder { Id = "100" }, new OwnedTransponder { Id = "200", CarId = "mod" } },
        };

        var ids = TonightsSightings.IdsOf("100", driver);

        Assert.Equal(new[] { "100", "200" }, ids);
        Assert.True(TonightsSightings.HeardAny(ids, id => id == "200"));
        Assert.False(TonightsSightings.HeardAny(ids, _ => false));
        Assert.Equal(new[] { "300" }, TonightsSightings.IdsOf("300", null));
    }

    [Fact]
    public void One_transponder_in_two_classes_never_shares_a_heat_with_itself()
    {
        var stock = Heat("Stock", "Q1 Heat 1", "100", "101");
        var mod = Heat("Mod", "Q1 Heat 1", "100", "102");

        var combined = MixedHeats.CombineSmallClasses(new[] { stock, mod }, under: 5, heatSize: 8);

        Assert.Equal(2, combined.Count);
    }

    [Fact]
    public void One_transponder_in_two_classes_gets_a_race_off_between_its_heats()
    {
        var stockLast = new List<string> { "100", "101" };
        var modFirst = new List<string> { "100", "102" };
        var modSecond = new List<string> { "103", "104" };

        var order = RestOrder.Arrange(new IReadOnlyCollection<string>[] { modFirst, modSecond }, before: stockLast);

        Assert.Equal(new[] { 1, 0 }, order);
        Assert.Equal(new[] { "100" }, AutoStartClock.BackToBack(stockLast, modFirst));
    }

    [Fact]
    public void A_driver_racing_two_classes_is_asked_about_once_per_class_and_heard_once()
    {
        var rounds = new Dictionary<string, List<RoundDefinition>>
        {
            ["Stock"] = new() { Heat("Stock", "Q1 Heat 1", "101") },
            ["Mod"] = new() { Heat("Mod", "Q1 Heat 1", "102") },
        };

        var arrivals = LateArrivals.Find(
            new[] { ("Stock", (IReadOnlyList<string>)new[] { "101", "100" }), ("Mod", (IReadOnlyList<string>)new[] { "102", "100" }) },
            name => rounds[name], null, null);

        Assert.Equal(new[] { "Stock", "Mod" }, arrivals.Select(a => a.ClassName));

        var allHeats = rounds.Values.SelectMany(r => r).Append(Heat("Mod", "Q2 Heat 1", "100")).Append(Heat("Stock", "Q2 Heat 1", "100"));
        Assert.Equal(new[] { "100" }, HeatPosting.NotHeard(allHeats, t => t != "100"));
    }

    // --- Templates on dates ---------------------------------------------------------

    private static NightTemplate Template(string name, DayOfWeek[]? weekdays = null, DateTime[]? dates = null) => new()
    {
        Name = name,
        Shape = RaceDayShape.ClubNight,
        ClassNames = { "Stock" },
        Weekdays = (weekdays ?? Array.Empty<DayOfWeek>()).ToList(),
        Dates = (dates ?? Array.Empty<DateTime>()).ToList(),
    };

    [Fact]
    public void Tonight_s_template_is_the_one_set_for_the_date_before_the_one_set_for_the_weekday()
    {
        var tuesday = new DateTime(2026, 11, 17);
        var club = Template("Tuesday club night", new[] { DayOfWeek.Tuesday });
        var points = Template("Points round", dates: new[] { new DateTime(2026, 12, 1) });

        Assert.Same(club, NightTemplates.ForDay(new[] { club, points }, tuesday));
        Assert.Same(points, NightTemplates.ForDay(new[] { club, points }, new DateTime(2026, 12, 1)));
        Assert.Null(NightTemplates.ForDay(new[] { club, points }, new DateTime(2026, 11, 18)));
        Assert.Null(NightTemplates.ForDay(null, tuesday));
    }

    [Fact]
    public void The_picker_says_which_nights_a_template_is_for()
    {
        var club = Template("Tuesday club night", new[] { DayOfWeek.Tuesday });
        var twice = Template("Midweek", new[] { DayOfWeek.Thursday, DayOfWeek.Tuesday });

        Assert.EndsWith(" - Tuesdays", NightTemplates.Describe(club));
        Assert.Equal(string.Empty, NightTemplates.WhenWords(Template("Never")));
        Assert.StartsWith(System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(DayOfWeek.Tuesday) + " and ", NightTemplates.WhenWords(twice));

        var future = Template("Points round", dates: new[] { DateTime.Today.AddDays(10), DateTime.Today.AddDays(-30) });
        Assert.Equal(DateTime.Today.AddDays(10).ToString("d MMM"), NightTemplates.WhenWords(future));
    }

    [Fact]
    public void A_template_made_from_a_day_is_set_for_no_nights_until_it_is_given_some()
    {
        var template = NightTemplates.From(new RaceEvent { Name = "Tonight", ClassNames = { "Stock" } }, "Club night");

        Assert.Empty(template.Weekdays);
        Assert.Empty(template.Dates);
    }
}

/// <summary>"Racing's off" by push (12 Nov 2026, setup plan step 7): what the laptop sends the relay, and how it reads the answer.</summary>
public sealed class RacingOffPushTests
{
    [Fact]
    public void The_push_is_one_line_cut_to_the_relay_s_limits()
    {
        var json = RelayPublisher.BroadcastBody(new string('T', 80), "Racing's off -\r\nthe track\tis flooded.", "nextnight");

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var title = doc.RootElement.GetProperty("title").GetString()!;
        var body = doc.RootElement.GetProperty("body").GetString()!;

        Assert.Equal(RelayPublisher.BroadcastTitleMax, title.Length);
        Assert.Equal("Racing's off - the track is flooded.", body);
        Assert.DoesNotContain(body, char.IsControl);
        Assert.Equal("nextnight", doc.RootElement.GetProperty("tag").GetString());
    }

    [Fact]
    public void The_relay_s_answer_is_said_plainly()
    {
        Assert.Equal("a push went to 7 phones (5 drivers signed up for alerts).", RelayPublisher.BroadcastAnswer(200, "{\"ok\":true,\"badges\":5,\"sent\":7,\"failed\":0}"));
        Assert.StartsWith("a push went out, but no phone is signed up", RelayPublisher.BroadcastAnswer(200, "{\"ok\":true,\"badges\":0,\"sent\":0,\"failed\":0}"));
        Assert.Contains("redeploy", RelayPublisher.BroadcastAnswer(404, string.Empty));
        Assert.Contains("three", RelayPublisher.BroadcastAnswer(429, string.Empty));
        Assert.Contains("aren't set up", RelayPublisher.BroadcastAnswer(503, string.Empty));
    }

    [Fact]
    public void The_off_push_names_the_club_and_the_night()
    {
        var night = new AnnouncedNight { Day = new DateTime(2026, 11, 18), OffText = "Flooded track - sorry!" };

        var (title, body) = NextNight.OffPush(night, "Upstate Miniz");

        Assert.Equal($"Upstate Miniz: no racing {NextNight.When(night.Day)}", title);
        Assert.Equal("Flooded track - sorry!", body);
    }
}
