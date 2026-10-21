namespace HeatCaller.Tests;

/// <summary>
/// The usage meters' screens, the parts that can be judged without a
/// window: what the sidebar chip says and in which colour, Mission
/// Control's Allowances tile, when a line offers Ask for More... or the
/// ElevenLabs link, and what the This Month window says about requests and
/// emails. The window itself is held to its layout by the window sweep
/// (WindowCatalog.RaceNightScreens).
/// </summary>
public sealed class UsageUiTests
{
    private static readonly DateTime Resets = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Meter Measured(MeterKind kind, long used, long allowance, long tonight = 0) =>
        new(kind, Known: true, used, allowance, TopUp: 0, Resets, DateTime.UtcNow, tonight, Note: null);

    private static Meter Unreadable(MeterKind kind, string note) =>
        new(kind, Known: false, Used: 0, Allowance: 0, TopUp: 0, null, DateTime.UtcNow, UsedTonight: 0, note);

    private static MissionControl.Inputs Night(MissionControl.AllowanceState? allowances) => new(
        LineDecoder: "EasyLap",
        LineState: DecoderConnectionState.Connected,
        HeartbeatAge: TimeSpan.FromMilliseconds(400),
        LastPassAge: TimeSpan.FromSeconds(3),
        SplitState: null,
        CarsHeard: 6,
        WeakCars: Array.Empty<string>(),
        Roster: new[] { "Asher", "Frank" },
        RosterMissing: Array.Empty<string>(),
        TimingProblem: null,
        Fading: Array.Empty<string>(),
        Relay: null,
        VoiceMuted: false,
        Allowances: allowances);

    private static MissionControl.Tile Allowances(MissionControl.AllowanceState? allowances) =>
        MissionControl.Assess(Night(allowances)).Single(t => t.Key == "allowances");

    private static MissionControl.AllowanceState State(bool localModel, params Meter[] meters) => new(meters, localModel);

    // --- The sidebar chip ---

    [Fact]
    public void The_chip_names_each_measured_meter_with_its_share()
    {
        var meters = new[] { Measured(MeterKind.Commentary, 47, 100), Measured(MeterKind.HeatCallerVoice, 34, 100) };

        Assert.Equal("AI 47% · Voice 34%", UsageGlance.ChipText(meters));
        Assert.Equal(MeterLevel.Ok, UsageGlance.Worst(meters));
    }

    [Fact]
    public void The_chip_leaves_out_what_is_not_on_the_plan_and_says_nothing_with_none()
    {
        var meters = new[] { Measured(MeterKind.Commentary, 12, 0) };

        Assert.Equal(string.Empty, UsageGlance.ChipText(meters));
        Assert.Equal(MeterLevel.Unknown, UsageGlance.Worst(meters));
        Assert.Equal(string.Empty, UsageGlance.ChipText(Array.Empty<Meter>()));
    }

    [Fact]
    public void A_meter_that_could_not_be_read_is_on_the_chip_as_a_question_mark_with_the_reason_on_hover()
    {
        var meters = new[]
        {
            Measured(MeterKind.Commentary, 42, 100),
            Unreadable(MeterKind.ElevenLabsKey, "This key can't read its plan."),
        };

        Assert.Equal("AI 42% · ElevenLabs ?", UsageGlance.ChipText(meters));

        // Its colour is the measured ones' - a question mark is not a warning.
        Assert.Equal(MeterLevel.Ok, UsageGlance.Worst(meters));
        Assert.Contains("This key can't read its plan.", UsageGlance.Tooltip(meters));
    }

    [Fact]
    public void The_chip_takes_the_worst_level_and_its_colour()
    {
        var low = new[] { Measured(MeterKind.Commentary, 90, 100), Measured(MeterKind.HeatCallerVoice, 10, 100) };
        var gone = new[] { Measured(MeterKind.Commentary, 90, 100), Measured(MeterKind.HeatCallerVoice, 100, 100) };

        Assert.Equal(MeterLevel.Watch, UsageGlance.Worst(low));
        Assert.Equal(MeterLevel.Spent, UsageGlance.Worst(gone));

        Assert.Equal(AppTheme.Warning, UsageChip.InkFor(MeterLevel.Watch, rail: false));
        Assert.Equal(AppTheme.Danger, UsageChip.InkFor(MeterLevel.Spent, rail: true));
        Assert.Equal(AppTheme.SidebarTextMuted, UsageChip.InkFor(MeterLevel.Ok, rail: false));
        Assert.Equal(AppTheme.Success, UsageChip.InkFor(MeterLevel.Ok, rail: true));
    }

    [Fact]
    public void The_chip_s_tooltip_says_each_meter_in_full()
    {
        var commentary = Measured(MeterKind.Commentary, 47, 100);
        var tip = UsageGlance.Tooltip(new[] { commentary });

        Assert.Contains(commentary.Name, tip);
        Assert.Contains("Click", tip);
    }

    // --- Mission Control's Allowances tile ---

    [Fact]
    public void Nothing_metered_is_off_and_keeps_a_healthy_night_all_clear()
    {
        Assert.Equal(MissionControl.Level.Off, Allowances(null).Level);
        Assert.Equal(MissionControl.Level.Off, Allowances(State(localModel: true)).Level);
        Assert.Equal(MissionControl.Level.Off,
            Allowances(State(localModel: true, Unreadable(MeterKind.ElevenLabsKey, "Can't read its plan."))).Level);

        Assert.Equal("All clear. Ready to race.", MissionControl.Headline(MissionControl.Assess(Night(null))));
    }

    [Fact]
    public void Plenty_left_is_good()
    {
        var tile = Allowances(State(true, Measured(MeterKind.Commentary, 47, 100), Measured(MeterKind.HeatCallerVoice, 34, 100)));

        Assert.Equal(MissionControl.Level.Good, tile.Level);
        Assert.Equal("AI 47%", tile.Value);
    }

    [Fact]
    public void A_meter_at_eighty_percent_is_worth_a_look_and_is_named()
    {
        var voice = Measured(MeterKind.HeatCallerVoice, 85, 100);
        var tile = Allowances(State(true, Measured(MeterKind.Commentary, 20, 100), voice));

        Assert.Equal(MissionControl.Level.Watch, tile.Level);
        Assert.Contains(voice.Name, tile.Detail);
        Assert.DoesNotContain(Measured(MeterKind.Commentary, 20, 100).Name, tile.Detail);
    }

    [Fact]
    public void The_heatcaller_voice_used_up_is_a_problem_because_kokoro_is_speaking()
    {
        var tile = Allowances(State(true, Measured(MeterKind.HeatCallerVoice, 100, 100)));

        Assert.Equal(MissionControl.Level.Problem, tile.Level);
        Assert.Contains("Kokoro is speaking", tile.Detail);
    }

    [Fact]
    public void Commentary_used_up_is_a_look_while_the_model_here_writes_and_a_problem_without_it()
    {
        var spent = Measured(MeterKind.Commentary, 450, 450);

        Assert.Equal(MissionControl.Level.Watch, Allowances(State(localModel: true, spent)).Level);
        Assert.Contains("model on this PC", Allowances(State(localModel: true, spent)).Detail);

        Assert.Equal(MissionControl.Level.Problem, Allowances(State(localModel: false, spent)).Level);
    }

    [Fact]
    public void The_worst_allowance_is_the_one_the_tile_says()
    {
        var tile = Allowances(State(true, Measured(MeterKind.Commentary, 450, 450), Measured(MeterKind.HeatCallerVoice, 100, 100)));

        Assert.Equal(MissionControl.Level.Problem, tile.Level);
        Assert.Contains("Kokoro", tile.Detail);
    }

    // --- What a line offers ---

    [Fact]
    public void Ask_for_more_is_offered_for_heatcaller_s_allowances_once_they_run_low()
    {
        Assert.False(UsageLine.OffersAsk(null));
        Assert.False(UsageLine.OffersAsk(Measured(MeterKind.Commentary, 10, 100)));
        Assert.True(UsageLine.OffersAsk(Measured(MeterKind.Commentary, 80, 100)));
        Assert.True(UsageLine.OffersAsk(Measured(MeterKind.HeatCallerVoice, 100, 100)));

        // The club's own key is topped up on its own plan.
        Assert.False(UsageLine.OffersAsk(Measured(MeterKind.ElevenLabsKey, 100, 100)));
        Assert.True(UsageLine.OffersUpgrade(Measured(MeterKind.ElevenLabsKey, 100, 100)));
        Assert.False(UsageLine.OffersUpgrade(Measured(MeterKind.ElevenLabsKey, 10, 100)));
        Assert.False(UsageLine.OffersUpgrade(Measured(MeterKind.HeatCallerVoice, 100, 100)));
    }

    [Fact]
    public void A_line_s_title_says_used_up_or_the_share()
    {
        var spent = Measured(MeterKind.Commentary, 450, 450);
        var half = Measured(MeterKind.Commentary, 50, 100);

        Assert.Equal($"{spent.Name} - used up", UsageLine.TitleFor(spent));
        Assert.Equal($"{half.Name} - {half.ShortPercent()} used", UsageLine.TitleFor(half));
        Assert.Equal(spent.Name, UsageLine.TitleFor(Unreadable(MeterKind.Commentary, "Couldn't reach the relay.")));
    }

    [Fact]
    public void The_bar_is_full_at_most_and_says_its_level()
    {
        var showing = StaThread.Run(() =>
        {
            using var bar = new UsageMeterBar();
            bar.Show(1.4, MeterLevel.Spent);
            return bar.Showing;
        }, TimeSpan.FromSeconds(30), () => "building a usage bar");

        Assert.Equal((1.0, MeterLevel.Spent), showing);
    }

    // --- The This Month window's words ---

    [Fact]
    public void Open_requests_are_listed_and_finished_ones_are_not()
    {
        var asked = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var commentaryName = Measured(MeterKind.Commentary, 0, 1).Name;

        var text = ThisMonthForm.DescribeRequests(new[]
        {
            new TopUpAsk("r_1", MeterKind.Commentary, asked, string.Empty, Open: true),
            new TopUpAsk("r_2", MeterKind.HeatCallerVoice, asked, string.Empty, Open: false),
        });

        Assert.Equal($"Asked for more {commentaryName} on 19 Oct - waiting.", text);
    }

    [Fact]
    public void A_relay_that_did_not_answer_is_said_rather_than_checked()
    {
        var said = ThisMonthForm.CheckedWording("The relay didn't answer.", "21:04");

        Assert.StartsWith("The relay didn't answer.", said);
        Assert.Contains("21:04", said);
        Assert.DoesNotContain("Checked", said);
    }

    [Fact]
    public void The_emails_say_where_they_go_or_where_to_set_email_up()
    {
        Assert.Contains("Club Website", ThisMonthForm.DescribeEmailTo(new AppSettings { SmtpUser = string.Empty }));

        var settings = new AppSettings { SmtpUser = "club@example.org", SmtpPassword = "app-password" };

        Assert.Contains("club@example.org", ThisMonthForm.DescribeEmailTo(settings));

        settings.UsageEmailTo = "treasurer@example.org";

        Assert.Contains("treasurer@example.org", ThisMonthForm.DescribeEmailTo(settings));
    }

    [Fact]
    public void The_send_to_box_answers_as_the_address_is_typed()
    {
        var settings = new AppSettings { SmtpUser = "club@example.org", SmtpPassword = "app-password" };

        // Blank: the club's own mailbox.
        Assert.Contains("To club@example.org", ThisMonthForm.DescribeEmailTo(settings, "   "));

        // Half typed: said, and the club's mailbox named as where they go meanwhile.
        var half = ThisMonthForm.DescribeEmailTo(settings, "treasurer@");
        Assert.Contains("isn't an email address yet", half);
        Assert.Contains("club@example.org", half);

        // Finished: that address, trimmed.
        Assert.Contains("To treasurer@example.org,", ThisMonthForm.DescribeEmailTo(settings, " treasurer@example.org "));
    }

    [Fact]
    public void Only_a_plain_address_with_a_dotted_domain_counts()
    {
        Assert.True(UsageMail.IsAddress("treasurer@example.org"));
        Assert.True(UsageMail.IsAddress("  pat.smith@club.co.uk "));

        Assert.False(UsageMail.IsAddress(""));
        Assert.False(UsageMail.IsAddress("treasurer@"));
        Assert.False(UsageMail.IsAddress("pat@club"));
        Assert.False(UsageMail.IsAddress("Pat <pat@club.org>"));
        Assert.False(UsageMail.IsAddress(null));

        // A display-name form falls back to the club's mailbox, as half an address does.
        var settings = new AppSettings { SmtpUser = "club@example.org", UsageEmailTo = "Pat <pat@club.org>" };
        Assert.Equal("club@example.org", UsageMail.AddressFor(settings));
    }
}
