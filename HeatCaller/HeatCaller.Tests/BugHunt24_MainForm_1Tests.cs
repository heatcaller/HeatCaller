namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the main window's own rules: a protest window
/// announced long after it closed (#204), Forget this driver and the
/// shortened names phones follow (#201), demo and simulated laps counting as
/// "heard tonight" (#203), the wait to close taking a race nobody was asked
/// about as agreed (#198), "off on the phones" with the club website
/// switched off (#202), and the console's "Copied N lines" (#199).
/// </summary>
public sealed class BugHunt24_MainForm_1Tests
{
    private static readonly DateTime Now = new(2026, 11, 23, 20, 0, 0, DateTimeKind.Utc);

    private static string Source(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", file));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }

    // ------------------------------------------------------------ #204

    [Fact]
    public void A_window_that_has_already_closed_is_not_announced()
    {
        // A race that ended three hours ago, with a five-minute window.
        var closed = Now.AddHours(-3).AddMinutes(5);

        Assert.False(MainForm.ProtestWindowIsStillToSay(closed, Now, roundIsTonights: true));
    }

    [Fact]
    public void A_window_of_another_race_days_round_is_not_announced()
    {
        Assert.False(MainForm.ProtestWindowIsStillToSay(Now.AddMinutes(4), Now, roundIsTonights: false));
    }

    [Fact]
    public void A_race_just_filed_tonight_still_gets_its_call()
    {
        Assert.True(MainForm.ProtestWindowIsStillToSay(Now.AddMinutes(5), Now, roundIsTonights: true));
    }

    [Fact]
    public void The_announcement_asks_whether_the_window_is_still_open_before_speaking()
    {
        var source = Source("MainForm.ProtestWindow.cs");
        var asks = source.IndexOf("!ProtestWindowIsStillToSay(window.ClosesAtUtc, DateTime.UtcNow,", StringComparison.Ordinal);
        var speaks = source.IndexOf("_announcer.SpeakReminder(line", StringComparison.Ordinal);

        Assert.True(asks > 0, "ProtestWindowAfterFiling no longer asks ProtestWindowIsStillToSay.");
        Assert.True(asks < speaks, "The check must come before the announcer speaks.");
    }

    // ------------------------------------------------------------ #201

    [Fact]
    public void Forgetting_a_driver_names_the_shortened_forms_phones_follow()
    {
        var names = MainForm.RelayForgetNames(new[] { "Sam Parker" }, "Sam Parker");

        Assert.Equal("Sam Parker", names[0]);
        Assert.Contains("Sam P", names);
        Assert.Contains("Sam Pa", names);
        Assert.True(names.Count <= RelayPublisher.ForgetMaxNames);
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void A_long_name_keeps_its_shortest_forms_within_what_one_call_carries()
    {
        var names = MainForm.RelayForgetNames(new[] { "Alexandra Featherstonehaugh", "Alex Featherstonehaugh" }, "A.F.");

        Assert.Equal(RelayPublisher.ForgetMaxNames, names.Count);
        Assert.Equal("Alexandra Featherstonehaugh", names[0]);
        Assert.Equal("Alex Featherstonehaugh", names[1]);
        Assert.Equal("A.F.", names[2]);
        Assert.Contains("Alexandra F", names);
        Assert.Contains("Alex F", names);
        Assert.Contains("Alexandra Fe", names);
        Assert.Contains("Alex Fe", names);
    }

    [Fact]
    public void A_one_word_name_and_blanks_add_nothing()
    {
        Assert.Equal(new[] { "Sam" }, MainForm.RelayForgetNames(new[] { "Sam", " ", string.Empty }, null));
    }

    [Fact]
    public void The_forget_sends_the_names_from_the_rule()
    {
        Assert.Contains("var names = RelayForgetNames(outcome.Names, shown);", Source("MainForm.Forget.cs"));
    }

    // ------------------------------------------------------------ #203

    private static SessionRecord Saved(string transponder, Action<SessionRecord>? change = null)
    {
        var session = new SessionRecord
        {
            EndedAtUtc = Now,
            Results = { new SessionResultEntry { Transponder = transponder, LapTimesMs = { 21000, 20500 } } },
        };

        change?.Invoke(session);
        return session;
    }

    [Fact]
    public void A_saved_demo_race_or_a_pasted_in_result_does_not_count_as_heard_tonight()
    {
        var sessions = new[]
        {
            Saved("100"),
            Saved("200", s => s.IsDemo = true),
            Saved("300", s => s.Label = SessionRecord.DemoLabelPrefix + " Stock A-Main"),
            Saved("400", s => s.ImportedFrom = "LiveRC"),
        };

        var heard = TonightsSightings.InSessions(MainForm.RacesThatProveALoopCrossing(sessions), Now.AddHours(-6));

        Assert.Equal(new[] { "100" }, heard.OrderBy(t => t).ToArray());
    }

    [Fact]
    public void A_simulated_decoders_pass_is_not_a_sighting()
    {
        var real = new DirectLapEvent { TransponderId = "100" };

        Assert.True(MainForm.PassCountsAsHeard(real));
        Assert.False(MainForm.PassCountsAsHeard(real.AsSimulated()));
        Assert.False(MainForm.PassCountsAsHeard(new DirectLapEvent { TransponderId = "100", IsSynthetic = true }));
    }

    [Fact]
    public void Heard_tonight_reads_only_races_off_the_loop_and_real_passes()
    {
        var setup = Source("MainForm.NightSetup.cs");
        var ready = Source("MainForm.ReadyToRace.cs");

        Assert.Contains("TonightsSightings.InSessions(RacesThatProveALoopCrossing(_sessionHistory.All), since)", setup);
        Assert.Contains("if (PassCountsAsHeard(lap))", setup);
        Assert.Contains("TonightsSightings.InSessions(RacesThatProveALoopCrossing(_sessionHistory.All),", ready);
        Assert.DoesNotContain("InSessions(_sessionHistory.All", setup);
        Assert.DoesNotContain("InSessions(_sessionHistory.All", ready);
    }

    // ------------------------------------------------------------ #198

    [Fact]
    public void A_race_that_started_under_the_clips_question_was_not_agreed_to()
    {
        var agreed = MainForm.AgreedToCloseOnRace(raceOnTrackBeforeQuestion: false, raceOnTrackAfter: true);

        Assert.False(agreed);

        // ...so the first tick of the wait stops it rather than closing mid-race.
        Assert.True(MainForm.RaceStartedWhileWaitingForClips(raceOnTrack: true, closingOnRunningRace: agreed));
    }

    [Fact]
    public void A_race_running_before_the_clips_question_is_still_the_one_agreed_to()
    {
        Assert.True(MainForm.AgreedToCloseOnRace(raceOnTrackBeforeQuestion: true, raceOnTrackAfter: true));
        Assert.False(MainForm.AgreedToCloseOnRace(raceOnTrackBeforeQuestion: true, raceOnTrackAfter: false));
        Assert.False(MainForm.AgreedToCloseOnRace(raceOnTrackBeforeQuestion: false, raceOnTrackAfter: false));
    }

    [Fact]
    public void The_race_is_looked_at_before_the_clips_question_goes_up()
    {
        var wait = Source("MainForm.ClosingWait.cs");
        var before = wait.IndexOf("var raceOnTrackBeforeQuestion = _race.State is", StringComparison.Ordinal);
        var question = wait.IndexOf("var answer = AppDialog.Choose(", StringComparison.Ordinal);

        Assert.True(before > 0, "The race is no longer read before the question.");
        Assert.True(before < question, "The race must be read before the question goes up.");
        Assert.Contains("_closingOnRunningRace = AgreedToCloseOnRace(raceOnTrackBeforeQuestion, _closingOnRunningRace);", wait);
    }

    // ------------------------------------------------------------ #202

    [Theory]
    [InlineData(false, true, null)]
    [InlineData(true, true, "Told 12 drivers the night is off.")]
    [InlineData(true, false, "The mailbox refused the sign-in.")]
    public void With_the_club_website_off_calling_a_night_off_does_not_say_it_is_off_on_the_phones(bool emailsTried, bool emailsOk, string? message)
    {
        var words = MainForm.CalledOffWords(websiteOn: false, emailsTried, emailsOk, message, pushed: null);

        Assert.NotNull(words);
        Assert.DoesNotContain("off on the phones", words);
        Assert.Contains("the club website is switched off", words);
        Assert.Contains("the phones won't see it until it's on", words);

        if (message is not null)
        {
            Assert.Contains(message, words);
            Assert.Contains(emailsOk ? "The emails went: " : "The emails didn't all go: ", words);
        }
    }

    [Fact]
    public void With_the_club_website_on_the_called_off_words_are_as_they_were()
    {
        Assert.Null(MainForm.CalledOffWords(true, emailsTried: false, emailsOk: true, null, pushed: null));
        Assert.Equal("The night is off on the phones - and sent to 9 phones.",
            MainForm.CalledOffWords(true, emailsTried: false, emailsOk: true, null, pushed: "sent to 9 phones."));
        Assert.Equal("The night is off on the phones, and the emails went: Told 12 drivers the night is off.",
            MainForm.CalledOffWords(true, emailsTried: true, emailsOk: true, "Told 12 drivers the night is off.", pushed: null));
        Assert.Equal("The night is off on the phones, but the emails didn't all go: Refused.\r\n\r\nPush: sent to 9 phones.",
            MainForm.CalledOffWords(true, emailsTried: true, emailsOk: false, "Refused.", pushed: "sent to 9 phones."));
    }

    [Fact]
    public void Calling_it_off_says_the_words_from_the_rule()
    {
        var source = Source("MainForm.NextNight.cs");

        Assert.Contains("CalledOffWords(websiteOn, emailsTried: false, emailsOk: true, null, pushed)", source);
        Assert.Contains("CalledOffWords(websiteOn, emailsTried: true, result.Ok, result.Message, pushed)", source);
    }

    // ------------------------------------------------------------ #199

    [Theory]
    [InlineData("a\r\n", 1)]
    [InlineData("a\r\nb\r\n", 2)]
    [InlineData("a\r\nb", 2)] // a selection that stops mid-line
    [InlineData("a\r\n\r\nb\r\n", 3)] // a blank line inside still counts
    [InlineData("a\nb\n", 2)]
    [InlineData("a", 1)]
    [InlineData("", 0)]
    public void The_console_counts_the_lines_copied_without_the_line_break_at_the_end(string text, int lines)
    {
        Assert.Equal(lines, MainForm.ConsoleLineCount(text));
    }

    [Fact]
    public void Ten_console_lines_are_ten()
    {
        var text = string.Concat(Enumerable.Range(1, 10).Select(i => $"line {i}" + Environment.NewLine));

        Assert.Equal(10, MainForm.ConsoleLineCount(text));
    }

    [Fact]
    public void Both_copies_of_the_console_use_the_count()
    {
        Assert.Contains("Plural.Of(ConsoleLineCount(text), \"line\")", Source("MainForm.Console.cs"));
        Assert.Contains("Plural.Of(ConsoleLineCount(text), \"visible line\")", Source("MainForm.cs"));
    }
}
