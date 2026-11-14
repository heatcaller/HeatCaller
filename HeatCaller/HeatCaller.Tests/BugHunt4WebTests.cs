using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 4: the track's Wi-Fi pages and the PINs that guard them.
///
/// Check-in after a restore whose keys could not be had back (web-1), the
/// phone pages' polls piling up on weak Wi-Fi (web-2), a correction from
/// the phone that changes nothing (web-4), a sponsor slide the live results
/// page can't show (web-5), and a staff, manager or Wi-Fi PIN that is
/// already a racer's own (signup-2).
///
/// The pages' scripts run in a phone's browser, which a test here can't
/// drive, so those are checked as the laptop serves them.
/// </summary>
public sealed class BugHunt4WebTests : IDisposable
{
    // Declared first: everything below reads and writes where it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- web-1: check-in after the saved PINs were locked ----------------------------------

    [Fact]
    public void Check_in_stays_closed_after_the_backup_s_keys_are_given_up()
    {
        // Sealed on another PC: SecretBox.Open refuses it here.
        var settings = new AppSettings { Secrets = "garbage" };
        Assert.True(settings.SecretsLocked);

        var server = Server(settings);
        Assert.NotNull(server.CheckInClosedReason);

        // What SecretRecovery.GiveUp does when there is no secrets.hgk, or
        // the backup password is skipped: the lock is cleared.
        settings.ApplySecrets(new AppSettings.SecretFields());
        Assert.False(settings.SecretsLocked);

        // Nobody has set a check-in PIN, so it must still be shut.
        Assert.True(settings.CheckInPinNeedsEntering);
        Assert.NotNull(server.CheckInClosedReason);
    }

    [Fact]
    public void Check_in_stays_closed_after_a_restart_once_the_keys_were_given_up()
    {
        var settings = new AppSettings { Secrets = "garbage" };
        settings.ApplySecrets(new AppSettings.SecretFields());
        Assert.True(settings.Save());

        var tonight = AppSettings.Load();

        Assert.False(tonight.SecretsLocked);
        Assert.True(tonight.CheckInPinNeedsEntering);
        Assert.NotNull(Server(tonight).CheckInClosedReason);
    }

    [Fact]
    public void A_file_from_the_old_pc_closes_check_in_whatever_its_own_flag_says()
    {
        // The old PC wrote false, after the secrets it sealed - the order a
        // save writes them in.
        File.WriteAllText(_data.PathOf("settings.json"), "{\"Secrets\":\"garbage\",\"CheckInPinNeedsEntering\":false}");

        var settings = AppSettings.Load();

        Assert.True(settings.SecretsLocked);
        Assert.True(settings.CheckInPinNeedsEntering);
    }

    [Fact]
    public void Setting_the_check_in_pin_again_is_what_opens_check_in()
    {
        var settings = new AppSettings { Secrets = "garbage" };
        settings.ApplySecrets(new AppSettings.SecretFields());

        // Local Screens, with a check-in PIN typed in (WebServerControl.SavePins).
        settings.CheckInPinRestored();

        Assert.False(settings.CheckInPinNeedsEntering);

        // And a PIN cleared after that is the club's choice: open to anybody.
        var server = Server(settings);
        server.CheckInPin = string.Empty;
        Assert.Null(server.CheckInClosedReason);

        // Saved that way too.
        Assert.True(settings.Save());
        Assert.False(AppSettings.Load().CheckInPinNeedsEntering);
    }

    [Fact]
    public void Check_in_closed_after_the_keys_were_given_up_says_to_set_a_pin_rather_than_unlock_them()
    {
        var settings = new AppSettings { Secrets = "garbage" };
        var server = Server(settings);
        Assert.Contains("backup password", server.CheckInClosedReason);

        // Given up: there is nothing left to unlock.
        settings.ApplySecrets(new AppSettings.SecretFields());

        var reason = server.CheckInClosedReason;
        Assert.NotNull(reason);
        Assert.DoesNotContain("backup password", reason);
        Assert.Contains("Local Screens", reason);
    }

    [Fact]
    public void A_laptop_whose_keys_opened_has_check_in_open_with_no_pin_as_before()
    {
        var settings = new AppSettings();

        Assert.False(settings.CheckInPinNeedsEntering);
        Assert.Null(Server(settings).CheckInClosedReason);
    }

    // --- web-2: the phone pages' polls ------------------------------------------------------

    [Fact]
    public void The_phone_remote_polls_one_at_a_time_and_gives_up_on_a_poll_that_hangs()
    {
        var tick = FunctionBody(Pages().BuildRemotePage(), "async function tick(");

        Assert.Contains("if (ticking) return;", tick);
        Assert.Contains("fetch('/api/state', { signal: stop.signal })", tick);
        Assert.Contains("ticking = false;", tick);
    }

    [Fact]
    public void A_flag_press_on_the_remote_gives_up_and_says_connection_lost_rather_than_sending_for_ever()
    {
        var go = FunctionBody(Pages().BuildRemotePage(), "async function go(");

        Assert.Contains("setTimeout(() => stop.abort(), 12000)", go);
        Assert.Contains("signal: stop.signal", go);
        Assert.Contains("'Connection lost.'", go);
    }

    [Fact]
    public void The_manager_and_check_in_pages_poll_one_at_a_time()
    {
        var server = Pages();

        var manage = server.BuildManagePage();
        Assert.Contains("if (document.activeElement === pinBox || polling) { return; }", manage);
        Assert.Contains("signal: stop.signal", FunctionBody(manage, "function call("));

        var load = FunctionBody(server.BuildCheckInPage(), "async function load(");
        Assert.Contains("if (loading) { return; }", load);
        Assert.Contains("signal: stop.signal", load);

        // A PIN finished while a poll of the half-typed one was out: the look
        // the PIN box asked for was dropped, and the old poll's refusal was
        // put down to the box as it was by then - the right PIN, refused
        // until the page was reloaded. The refusal is the PIN sent's, and a
        // box changed meanwhile is looked at once that poll is back.
        Assert.Contains("var sentPin = pinBox.value;", load);
        Assert.Contains("headers: { 'X-Pin': sentPin }", load);
        Assert.Contains("badPin = sentPin;", load);
        Assert.DoesNotContain("badPin = pinBox.value", load);
        Assert.Contains("if (pinBox.value !== sentPin) { setTimeout(load, 0); }", load);
    }

    // --- web-4: a correction from the phone that changes nothing ---------------------------

    [Fact]
    public void The_phone_asks_for_the_total_laps_added_not_a_change()
    {
        var edit = FunctionBody(Pages().BuildManagePage(), "function openEditResult(");

        Assert.Contains("'Total laps added for '", edit);
        Assert.DoesNotContain("0 = no change", edit);
    }

    /// <summary>Windows only: the race-day manager answers on its window's own thread.</summary>
    [Fact]
    public void A_phone_correction_that_changes_nothing_says_so_rather_than_done()
    {
        var answer = StaThread.Run(() =>
        {
            using var window = new Control();
            _ = window.Handle;

            var settings = new AppSettings { AllowResultEditing = true };
            var history = SessionHistoryStore.Load();

            var record = new SessionRecord
            {
                Label = "A-Main",
                SessionType = RaceSessionType.Race,
                StartedAtUtc = DateTime.UtcNow,
                Results = new List<SessionResultEntry> { Entry("S01", 1), Entry("S02", 2) },
            };

            // Sam already has the lap he was missed on added.
            ResultEditor.Apply(record, new[] { new ResultAdjustment("S01", 1, 0, 0, "") }, "Missed pass", "Laptop");
            Assert.True(history.Add(record));

            var phone = new RaceDayService(new RaceDayServiceHost
            {
                UiThread = window,
                Settings = settings,
                Undo = new UndoService(),
                Race = new RaceTiming(new RaceControlSession()),
                Events = RaceEventStore.Load(),
                Classes = RaceClassStore.Load(),
                Profiles = ProfileStore.Load(),
                Rounds = RoundDefinitionStore.Load(),
                History = history,
                CheckIns = CheckInStore.Load(),
                Tech = TechInspectionStore.Load(),
                Schedule = DayScheduleStore.Load(),

                // Neither is touched by a result correction.
                Program = null!,
                RaceControl = null!,

                ActiveRound = () => null,
                StartRound = (_, _, _) => "Not from this test.",
                SetCheckedIn = (_, _) => false,
                RepublishSession = _ => { },
                RefreshDesk = () => { },
                Log = _ => { },
            });

            // "1" typed for one more lap: the total he already has.
            var request = JsonSerializer.Serialize(new
            {
                op = "result.adjust",
                args = new
                {
                    sessionId = record.Id,
                    reason = "Another missed pass",
                    changes = new[] { new { driverId = phone.DriverIdFor("S01"), addedLaps = 1 } },
                },
            });

            return phone.HandleJsonAsync(request, RaceDayChannel.WiFi, CancellationToken.None).GetAwaiter().GetResult();
        }, TimeSpan.FromSeconds(30), () => "asking the race-day manager for a correction");

        using var document = JsonDocument.Parse(answer);
        var root = document.RootElement;

        // It used to answer ok with no message, and the phone said "Done".
        Assert.False(root.GetProperty("ok").GetBoolean(), answer);
        Assert.Contains("Nothing to change", root.GetProperty("error").GetString());
    }

    // --- signup-2: a PIN that is already a racer's own -------------------------------------

    [Fact]
    public void A_pin_a_racer_already_holds_is_recognised_and_a_longer_one_is_not()
    {
        var settings = new AppSettings();
        settings.RacerPins["T1"] = "482913";

        Assert.True(RacerPins.IsHeldByRacer(settings, "482913"));
        Assert.True(RacerPins.IsHeldByRacer(settings, " 482913 "));
        Assert.False(RacerPins.IsHeldByRacer(settings, "48291300"));
        Assert.False(RacerPins.IsHeldByRacer(settings, "482914"));
        Assert.False(RacerPins.IsHeldByRacer(settings, string.Empty));
    }

    // --- Helpers ----------------------------------------------------------------------------

    private static WebServerService Server(AppSettings settings) =>
        new(new RaceTiming(new RaceControlSession()), ProfileStore.Load(), settings: settings);

    /// <summary>
    /// A server to build the pages from. Set to one of the app's own looks
    /// rather than "match the app": that reads the running theme, whose fonts
    /// need GDI+, and the page is the same script either way.
    /// </summary>
    private static WebServerService Pages() => Server(new AppSettings { DisplayLook = Looks.MidnightKey });

    /// <summary>One function of a page's script, from its first line to the brace that closes it at the left margin.</summary>
    private static string FunctionBody(string page, string start)
    {
        var from = page.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"No \"{start}\" in the page.");

        var to = page.IndexOf("\n}", from, StringComparison.Ordinal);
        Assert.True(to > from, $"\"{start}\" never closes.");

        return page[from..to];
    }

    private static SessionResultEntry Entry(string transponder, int position) => new()
    {
        Position = position,
        Transponder = transponder,
        DriverName = transponder,
        Laps = 3,
        LapTimesMs = new List<long> { 1_400, 12_000 + position * 100, 12_000 + position * 100 },
        ElapsedMs = 1_400 + 2 * (12_000 + position * 100),
        FastLapMs = 12_000 + position * 100,
        Status = "active",
    };
}
