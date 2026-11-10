using System.Collections.Concurrent;

namespace HeatCaller.Tests;

/// <summary>
/// A position report the order changes under while it is being read out
/// (9 Nov 2026). Asher: "cut it off and wire it like the other examples to
/// correct it. the next report may take some time to come in." See
/// AnnouncementService.PositionReportCut.
/// </summary>
public sealed class PositionReportCutTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static RaceStanding Car(string transponder, int laps, long elapsedMs, bool adjusted = false) => new()
    {
        Transponder = transponder,
        Laps = laps,
        ElapsedMs = elapsedMs,
        Status = adjusted ? "adjusted" : "active",
        Id = transponder,
    };

    // --- Which change cuts it -----------------------------------------------

    [Fact]
    public void The_same_cars_in_the_same_order_have_not_changed()
    {
        Assert.Equal(-1, AnnouncementService.FirstChangedPlace(["102", "101", "103"], ["102", "101", "103"]));
    }

    [Fact]
    public void The_first_place_a_different_car_is_named_is_the_change()
    {
        Assert.Equal(0, AnnouncementService.FirstChangedPlace(["102", "101", "103"], ["101", "102", "103"]));
        Assert.Equal(1, AnnouncementService.FirstChangedPlace(["102", "101", "103"], ["102", "103", "101"]));
    }

    [Fact]
    public void A_shorter_order_changes_where_it_runs_out()
    {
        Assert.Equal(2, AnnouncementService.FirstChangedPlace(["102", "101", "103"], ["102", "101"]));
    }

    // --- The words ---------------------------------------------------------

    [Fact]
    public void The_correction_names_the_car_that_moved_and_its_place()
    {
        Assert.Equal("[excited] Hold on - Sam takes the lead! So it's {positions}.",
            AnnouncementService.ReportCorrectionTemplate("Sam", 1, byRuling: false));
        Assert.Equal("[excited] Hold on - Frank is through into second! So it's {positions}.",
            AnnouncementService.ReportCorrectionTemplate("Frank", 2, byRuling: false));
        Assert.Equal("[excited] And with that ruling, Sam takes the lead! So it's {positions}.",
            AnnouncementService.ReportCorrectionTemplate("Sam", 1, byRuling: true));
    }

    // --- In the announcer ---------------------------------------------------

    /// <summary>An announcer with every call and sound off, Sam (101), Bill (102) and Frank (103) on file, and what it puts on the screens.</summary>
    private static (AnnouncementService Announcer, ConcurrentQueue<string> Shown, ConcurrentQueue<string> Log) Announcer(string? leaderChange = null)
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "101", DriverName = "Sam" });
        profiles.Upsert(new DriverProfile { Transponder = "102", DriverName = "Bill" });
        profiles.Upsert(new DriverProfile { Transponder = "103", DriverName = "Frank" });

        var voice = VoiceSettingsStore.Load();
        foreach (var config in voice.Current.Events.Values)
        {
            config.Enabled = false;
            config.EnabledInPractice = false;
        }

        if (leaderChange is not null)
        {
            voice.Current.Events[nameof(AnnouncementEventType.LeaderChange)] = new AnnouncementEventConfig { Enabled = true, Weight = 100, Templates = { leaderChange } };
        }

        var sounds = SoundEffectsStore.Load();
        foreach (var effect in sounds.Current.Effects.Values)
        {
            effect.Enabled = false;
        }

        var announcer = new AnnouncementService(profiles, voice, sounds);
        var shown = new ConcurrentQueue<string>();
        var log = new ConcurrentQueue<string>();
        announcer.AnnouncementTextReady += shown.Enqueue;
        announcer.StatusMessage += log.Enqueue;

        return (announcer, shown, log);
    }

    /// <summary>Bill, Sam, Frank on lap 4, and the report reading them out.</summary>
    private static void BillSamFrankBeingRead(AnnouncementService announcer)
    {
        announcer.OnStandingUpdated(Car("102", 4, 40_000));
        announcer.OnStandingUpdated(Car("101", 4, 40_400));
        announcer.OnStandingUpdated(Car("103", 4, 41_000));
        announcer.PutPositionReportOnAirForTest(["102", "101", "103"], 3);
    }

    private static bool WasCut(ConcurrentQueue<string> log) =>
        log.Any(line => line.StartsWith("[voice] Position report ", StringComparison.Ordinal)
            && (line.Contains("cut off", StringComparison.Ordinal) || line.Contains("dropped before it spoke", StringComparison.Ordinal)));

    [Fact]
    public void A_report_being_read_is_cut_and_corrected_when_the_lead_changes()
    {
        var (announcer, shown, log) = Announcer();
        BillSamFrankBeingRead(announcer);

        // Sam crosses onto lap 5 first - he is through into the lead.
        announcer.OnStandingUpdated(Car("101", 5, 50_100));
        announcer.Muted = true;

        Assert.Single(log, line => line.StartsWith("[voice] Position report ", StringComparison.Ordinal)
            && line.Contains("Sam got through into first", StringComparison.Ordinal));
        Assert.Equal(new[] { "Hold on - Sam takes the lead! So it's Sam leads, Bill second, Frank third." }, shown.ToList());
    }

    [Fact]
    public void A_pass_further_back_is_corrected_with_its_place()
    {
        var (announcer, shown, _) = Announcer();
        BillSamFrankBeingRead(announcer);

        // Bill crosses - the order is still Bill, Sam, Frank - then Frank crosses before Sam.
        announcer.OnStandingUpdated(Car("102", 5, 50_000));
        announcer.OnStandingUpdated(Car("103", 5, 50_300));
        announcer.Muted = true;

        Assert.Equal(new[] { "Hold on - Frank is through into second! So it's Bill leads, Frank second, Sam third." }, shown.ToList());
    }

    [Fact]
    public void A_car_crossing_again_without_changing_place_leaves_the_report_alone()
    {
        var (announcer, shown, log) = Announcer();
        BillSamFrankBeingRead(announcer);

        // The leader starts another lap: the order being read is still the order.
        announcer.OnStandingUpdated(Car("102", 5, 50_000));
        announcer.OnStandingUpdated(Car("101", 5, 50_400));
        announcer.Muted = true;

        Assert.False(WasCut(log));
        Assert.Empty(shown);
    }

    [Fact]
    public void A_report_is_only_corrected_once()
    {
        var (announcer, shown, log) = Announcer();
        BillSamFrankBeingRead(announcer);

        announcer.OnStandingUpdated(Car("101", 5, 50_100));

        // Frank then gets past Bill too - the report was already cut.
        announcer.OnStandingUpdated(Car("103", 5, 50_300));
        announcer.Muted = true;

        Assert.Single(log, line => line.StartsWith("[voice] Position report ", StringComparison.Ordinal)
            && line.Contains("got through", StringComparison.Ordinal));
        Assert.Single(shown);
    }

    [Fact]
    public void A_report_already_cut_by_a_flag_or_reset_is_not_cut_again()
    {
        var (announcer, shown, log) = Announcer();
        BillSamFrankBeingRead(announcer);

        // Everything cut - whatever is in the speaker now is not the report.
        announcer.SilenceNow("test");

        announcer.OnStandingUpdated(Car("101", 5, 50_100));
        announcer.Muted = true;

        Assert.False(WasCut(log));
        Assert.DoesNotContain(shown, text => text.Contains("Hold on", StringComparison.Ordinal));
    }

    [Fact]
    public void Nothing_is_cut_when_no_report_is_being_read()
    {
        var (announcer, shown, log) = Announcer();

        announcer.OnStandingUpdated(Car("102", 4, 40_000));
        announcer.OnStandingUpdated(Car("101", 4, 40_400));
        announcer.OnStandingUpdated(Car("101", 5, 50_100));
        announcer.Muted = true;

        Assert.False(WasCut(log));
        Assert.Empty(shown);
    }

    [Fact]
    public void The_correction_for_the_lead_is_said_instead_of_the_lead_change_call()
    {
        var (announcer, shown, _) = Announcer(leaderChange: "{driver} now leads");
        BillSamFrankBeingRead(announcer);

        announcer.OnStandingUpdated(Car("101", 5, 50_100));

        // The lead change call waits in the per-lap batch before it decides.
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && !shown.Any(text => text.Contains("now leads", StringComparison.Ordinal)))
        {
            Thread.Sleep(50);
        }

        announcer.Muted = true;

        Assert.Equal(new[] { "Hold on - Sam takes the lead! So it's Sam leads, Bill second, Frank third." }, shown.ToList());
    }

    // --- Dropped while it was rendered: queued once more (9 Nov 2026) -------

    [Fact]
    public void A_report_dropped_while_rendered_is_queued_once_more_from_the_board()
    {
        var source = Source("AnnouncementService.LapCalls.cs");

        // Only when the order moving is what dropped it, nothing was heard, and it is the first try.
        Assert.Contains("movedWhileRendered = true;", source, StringComparison.Ordinal);
        Assert.Contains("if (movedWhileRendered && !heard && !isRetry)", source, StringComparison.Ordinal);

        // Worded again from the board as it then stands, as an ordinary report.
        Assert.Contains("var againText = WordRunningOrder(again, template, sayGaps, placesOnly);", source, StringComparison.Ordinal);
        Assert.Contains("Work(again, againText, isRetry: true)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_report_queued_again_passes_the_same_gates_and_keeps_the_sponsors_ration()
    {
        var source = Source("AnnouncementService.LapCalls.cs");
        var requeue = source[source.IndexOf("bool RequeuePositionReport()", StringComparison.Ordinal)..];
        requeue = requeue[..requeue.IndexOf("var accepted = EnqueueVoiceWork", StringComparison.Ordinal)];

        Assert.Contains("HeldByCaution(AnnouncementEventType.PositionReport, config)", requeue, StringComparison.Ordinal);
        Assert.Contains("!DialLetsItSpeak(AnnouncementEventType.PositionReport, config)", requeue, StringComparison.Ordinal);
        Assert.Contains("lost: releaseMention", requeue, StringComparison.Ordinal);

        // The first try gives the ration back only when it was not handed on.
        Assert.Contains("if (!heard && !handedOn)", source, StringComparison.Ordinal);
    }

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }

    [Fact]
    public void A_ruling_that_changes_the_order_is_corrected_as_a_ruling()
    {
        var (announcer, shown, log) = Announcer();
        BillSamFrankBeingRead(announcer);

        // Ten seconds for Bill: Sam leads on the board, but passed nobody.
        announcer.OnStandingUpdated(Car("102", 4, 50_000, adjusted: true));
        announcer.Muted = true;

        Assert.Single(log, line => line.StartsWith("[voice] Position report ", StringComparison.Ordinal)
            && line.Contains("a correction by hand put Sam into first", StringComparison.Ordinal));
        Assert.Equal(new[] { "And with that ruling, Sam takes the lead! So it's Sam leads, Frank second, Bill third." }, shown.ToList());
    }
}
