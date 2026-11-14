namespace HeatCaller.Tests;

/// <summary>
/// Setup plan step 8 (12 Nov 2026), the starting half: one Start Tonight
/// window behind every door that starts a race day, and no empty race day
/// opened by itself on a fresh install.
/// </summary>
public sealed class NightSetupStep8StartTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static RaceEvent LastWeek() => new()
    {
        Name = "Club Night 4 Nov 2026",
        Shape = RaceDayShape.ClubNight,
        VenueName = "Riverside",
        ClassNames = new List<string> { "Stock", "Mod" },
        StartedUtc = new DateTime(2026, 11, 4, 18, 0, 0, DateTimeKind.Utc),
    };

    private static NightTemplate Template(string name, params DayOfWeek[] days) =>
        new() { Name = name, ClassNames = new List<string> { "Stock" }, Weekdays = days.ToList() };

    // --- One way to start a night --------------------------------------------

    [Fact]
    public void A_club_s_first_night_offers_only_the_two_questions()
    {
        var options = StartTonight.Options(null, null);

        Assert.Single(options);
        Assert.Equal(StartTonightKind.Different, options[0].Kind);
        Assert.Equal(0, StartTonight.Preselect(options, null));
    }

    [Fact]
    public void The_choices_are_like_last_time_then_each_template_then_something_different()
    {
        var options = StartTonight.Options(LastWeek(), new[] { Template("Tuesday club night"), Template("Points round") });

        Assert.Equal(
            new[] { StartTonightKind.LikeLast, StartTonightKind.Template, StartTonightKind.Template, StartTonightKind.Different },
            options.Select(o => o.Kind));

        Assert.StartsWith("Like last time (", options[0].Caption);
        Assert.Contains("Club Night 4 Nov 2026", options[0].Detail);
        Assert.Contains("Stock, Mod", options[0].Detail);
        Assert.Equal("Tuesday club night", options[1].Caption);
        Assert.Equal("Something different...", options[3].Caption);
    }

    [Fact]
    public void A_last_day_with_no_classes_is_not_offered_to_copy()
    {
        var empty = LastWeek();
        empty.ClassNames.Clear();

        Assert.DoesNotContain(StartTonight.Options(empty, null), o => o.Kind == StartTonightKind.LikeLast);
    }

    [Fact]
    public void It_opens_on_tonight_s_template_then_like_last_time()
    {
        var tuesday = Template("Tuesday club night", DayOfWeek.Tuesday);
        var options = StartTonight.Options(LastWeek(), new[] { Template("Points round"), tuesday });

        Assert.Equal(2, StartTonight.Preselect(options, tuesday));
        Assert.Equal(0, StartTonight.Preselect(options, null));
    }

    [Fact]
    public void A_door_that_asks_for_a_choice_opens_on_it()
    {
        var options = StartTonight.Options(LastWeek(), new[] { Template("Tuesday club night"), Template("Points round") });

        Assert.Equal(2, StartTonight.Preselect(options, null, StartTonightKind.Template, "points ROUND"));
        Assert.Equal(3, StartTonight.Preselect(options, null, StartTonightKind.Different));
        Assert.Equal(0, StartTonight.Preselect(options, Template("Tuesday club night"), StartTonightKind.LikeLast));

        // A template that has gone since falls back to the usual pick.
        Assert.Equal(0, StartTonight.Preselect(options, null, StartTonightKind.Template, "Deleted"));
    }

    [Fact]
    public void The_window_hands_back_what_was_picked_and_greys_the_questions_unless_it_is_something_different() => OnUiThread(() =>
    {
        var options = StartTonight.Options(LastWeek(), new[] { Template("Tuesday club night") });

        using (var likeLast = new NewRaceDayForm(options, 0, new[] { "Stock", "Mod" }, new[] { "Stock" }, RaceDayShape.RaceDay))
        {
            Assert.Equal("Start Tonight", likeLast.Text);
            Assert.Equal(StartTonightKind.LikeLast, likeLast.Choice.Kind);
        }

        using (var template = new NewRaceDayForm(options, 1, new[] { "Stock" }, new[] { "Stock" }, RaceDayShape.RaceDay))
        {
            Assert.Equal("Tuesday club night", template.Choice.Template!.Name);
        }

        using var different = new NewRaceDayForm(options, 2, new[] { "Stock", "Mod" }, new[] { "Mod" }, RaceDayShape.ClubNight);
        Assert.Equal(StartTonightKind.Different, different.Choice.Kind);
        Assert.Equal(RaceDayShape.ClubNight, different.Shape);
        Assert.Equal(new[] { "Mod" }, different.ClassNames);
    });

    [Fact]
    public void The_old_two_question_window_is_still_just_the_two_questions() => OnUiThread(() =>
    {
        using var form = new NewRaceDayForm(new[] { "Stock" }, Array.Empty<string>(), RaceDayShape.RaceDay);

        Assert.Equal(StartTonightKind.Different, form.Choice.Kind);
        Assert.Equal(new[] { "Stock" }, form.ClassNames);
    });

    [Fact]
    public void Every_door_opens_the_same_window()
    {
        var day = Source("RaceDayEventControl.cs");
        var templates = Source("RaceDayEventControl.Templates.cs");
        var pages = Source("MainForm.Pages.cs");

        // The Calendar's New Race Day and Next Steps' "like last time" and
        // template cards - doors that already know which choice they mean.
        Assert.Contains("StartTonightWindow(wanted, templateName);", day);
        Assert.Contains("public bool StartLikeLastDay() => StartTonightWindow(StartTonightKind.LikeLast, null);", day);
        Assert.Contains("StartTonightWindow(StartTonightKind.Template, name)", templates);

        // Setup's Templates tab (14 Nov 2026) took over New Race Day: its
        // Start goes the one way every door goes (StartTonightAs), and only
        // Something different... opens the window, for its two questions.
        Assert.DoesNotContain("New Race Day\"", day);
        var shelfStart = Between(templates, "private void StartFromShelf(StartTonightOption option)", "/// <summary>When..., Duplicate and Delete");
        Assert.Contains("StartTonightAs(option,", shelfStart);
        Assert.Contains("StartTonightWindow(StartTonightKind.Different, null)", shelfStart);

        // The one-press template start no longer asks its own question.
        Assert.DoesNotContain("Start tonight from a template", templates);

        // Set Up Tonight with no day open starts one in the window first, and
        // "Start a Race Day" no longer opens the wizard instead.
        var openSetup = Between(pages, "_openRaceDaySetup = startStep =>", "using var wizard");
        Assert.Contains("eventTab.StartNewDayFromElsewhere()", openSetup);

        // "Start a Race Day" goes to Setup's Templates tab - or, in the desk
        // view that can't go there, the window.
        var startRaceDay = Between(pages, "_startRaceDay = () =>", "tabRaceDay.SelectedIndexChanged");
        Assert.Contains("eventTab.ShowTab(RaceDayEventControl.TemplatesTab)", startRaceDay);
        Assert.Contains("_startRaceDayInWindow?.Invoke()", startRaceDay);
        Assert.DoesNotContain("_openRaceDaySetup", startRaceDay);

        var inWindow = Between(pages, "_startRaceDayInWindow = () =>", "eventTab.ComingTonight");
        Assert.Contains("eventTab.StartNewDayFromElsewhere()", inWindow);
    }

    // --- No empty first day on a fresh install --------------------------------

    [Fact]
    public void A_fresh_install_has_no_race_day_until_one_is_started()
    {
        var events = RaceEventStore.Load();

        Assert.Empty(events.Real);
        Assert.Null(events.Active);

        // Nothing opens one by itself any more.
        Assert.Null(typeof(EventSpine).GetMethod("StartFirstDayIfNever"));
        Assert.DoesNotContain("EventSpine.StartFirstDayIfNever(", Source("MainForm.cs"));

        // Starting tonight is the first real day.
        var first = EventSpine.StartEvent(events, EventSpine.DefaultDayName(DateTime.Now), classNames: new[] { "Stock" });
        Assert.Same(first, events.Active);
        Assert.Single(events.Real);
    }

    [Fact]
    public void With_no_race_day_the_program_offers_to_start_tonight()
    {
        var step = ProgramNextStep.Evaluate(null, Array.Empty<RoundDefinition>(), Array.Empty<ProgramNextStep.RacingClass>(), 3,
            SessionHistoryStore.Load(), new ProgramNextStep.SetupFacts(0, 0, 0, false, 0));

        Assert.Equal(ProgramStage.NoEvent, step.Stage);
        Assert.Equal("Start Tonight...", step.ActionCaption);
    }

    [Fact]
    public void An_install_that_already_has_an_empty_day_keeps_it_and_the_wizard_offer_looks_past_it()
    {
        var events = RaceEventStore.Load();
        var ghost = EventSpine.StartEvent(events, "Race Day 1 Nov 2026");

        Assert.True(MainForm.IsEmptyDay(ghost));
        Assert.Same(ghost, events.Active);

        // The first-run wizard now goes straight on to the offer.
        Assert.Contains("OfferRaceDaySetupOnce();", Between(Source("MainForm.cs"), "else if (wizard.TakeTour)", "startupSteps.Add("));
    }

    private static void OnUiThread(Action body) =>
        StaThread.Run(() =>
        {
            body();
            return true;
        }, TimeSpan.FromMinutes(1), () => "setup plan step 8");

    private static string RepoRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);

            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            throw new DirectoryNotFoundException("No folder with check.cmd in it above " + AppContext.BaseDirectory);
        }
    }

    private static string Source(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName));

    private static string Between(string source, string from, string until)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"\"{from}\" is not in the file any more - this test needs its name updated.");

        var end = source.IndexOf(until, start + from.Length, StringComparison.Ordinal);
        Assert.True(end >= 0, $"\"{until}\" is not after \"{from}\" any more.");

        return source.Substring(start, end - start);
    }
}
