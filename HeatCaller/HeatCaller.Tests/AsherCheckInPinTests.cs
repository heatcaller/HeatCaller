namespace HeatCaller.Tests;

/// <summary>
/// 26 Oct 2026, asked for by Asher, the race director: "is there a reason
/// the phone sign in page you can sign in/out anyone you want without a
/// pin?"
///
/// The Wi-Fi check-in page (/checkin) was an open sign-on sheet: anybody on
/// the track's Wi-Fi - behind the wall PIN, if the club set one - could tap
/// any driver in or out. Every tap now needs that driver's own personal PIN,
/// checked by the laptop (WebServerService.CheckRacerPin, called from
/// HandleCheckIn) before anything changes. The PIN check itself is run here;
/// the HTTP handler and the page's script can't be driven from a test, so
/// their order and their headers are read from the source.
/// </summary>
public sealed class AsherCheckInPinTests : IDisposable
{
    // Declared first: everything below reads and writes where it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string Sam = "1234567";
    private const string Bill = "7654321";
    private const string SamPin = "246813";
    private const string BillPin = "975318";

    private static WebServerService Server()
    {
        var server = new WebServerService(
            new RaceTiming(new RaceControlSession()), ProfileStore.Load(), settings: new AppSettings { DisplayLook = Looks.MidnightKey });

        server.PublishRacerPins(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Sam] = SamPin,
            [Bill] = BillPin,
        });

        return server;
    }

    [Fact]
    public void Only_the_driver_s_own_pin_checks_them_in()
    {
        var server = Server();

        Assert.Equal(WebServerService.RacerPinVerdict.Accepted, server.CheckRacerPin("10.0.0.5", Sam, SamPin));
        Assert.Equal(WebServerService.RacerPinVerdict.Accepted, server.CheckRacerPin("10.0.0.5", " " + Sam + " ", " " + SamPin + " "));

        // Somebody else's PIN, a made-up one, and none at all - one answer.
        Assert.Equal(WebServerService.RacerPinVerdict.Wrong, server.CheckRacerPin("10.0.0.5", Sam, BillPin));
        Assert.Equal(WebServerService.RacerPinVerdict.Wrong, server.CheckRacerPin("10.0.0.5", Sam, "000000"));
        Assert.Equal(WebServerService.RacerPinVerdict.Wrong, server.CheckRacerPin("10.0.0.5", Sam, null));
        Assert.Equal(WebServerService.RacerPinVerdict.Wrong, server.CheckRacerPin("10.0.0.5", Sam, "  "));
    }

    [Fact]
    public void A_driver_with_no_personal_pin_cannot_be_checked_in_from_a_phone()
    {
        var server = Server();

        Assert.Equal(WebServerService.RacerPinVerdict.Wrong, server.CheckRacerPin("10.0.0.5", "5555555", "246813"));

        // Racer PINs switched off: the host publishes none, and nobody can.
        server.PublishRacerPins(null);
        Assert.Equal(WebServerService.RacerPinVerdict.Wrong, server.CheckRacerPin("10.0.0.5", Sam, SamPin));
    }

    [Fact]
    public void Whether_a_driver_has_a_pin_is_not_given_away_for_free()
    {
        // 26 Oct 2026, review: "no PIN sent" and "no PIN issued" were answered
        // uncounted, each in its own words, so a phone could ask about every
        // driver in turn. Now both are counted like any wrong PIN, and
        // answered exactly as one.
        var server = Server();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(WebServerService.RacerPinVerdict.Wrong, server.CheckRacerPin("10.0.0.7", "5555555", null));
            Assert.Equal(WebServerService.RacerPinVerdict.Wrong, server.CheckRacerPin("10.0.0.7", Sam, null));
        }

        Assert.Equal(WebServerService.RacerPinVerdict.TooManyFromDevice, server.CheckRacerPin("10.0.0.7", "5555555", null));
        Assert.Equal(WebServerService.RacerPinVerdict.TooManyFromDevice, server.CheckRacerPin("10.0.0.7", Sam, SamPin));

        // And the handler has one refusal for all of them.
        var check = Between(Source("WebServerService.cs"), "private bool RacerPinAccepted(", "/// Whether a PIN is good enough to guard the remote");
        Assert.DoesNotContain("NoPinIssued", check);
        Assert.DoesNotContain("Missing", check);
        Assert.Contains("That isn't this driver's check-in PIN. No personal PIN? Check in at the desk.", check);

        var verdicts = Enum.GetNames<WebServerService.RacerPinVerdict>();
        Assert.DoesNotContain("NoPinIssued", verdicts);
        Assert.DoesNotContain("Missing", verdicts);
    }

    [Fact]
    public void A_new_set_of_pins_replaces_the_old_one_at_once()
    {
        var server = Server();

        server.PublishRacerPins(new Dictionary<string, string> { [Sam] = "135792" });

        Assert.Equal(WebServerService.RacerPinVerdict.Wrong, server.CheckRacerPin("10.0.0.5", Sam, SamPin));
        Assert.Equal(WebServerService.RacerPinVerdict.Accepted, server.CheckRacerPin("10.0.0.6", Sam, "135792"));
        Assert.Equal(WebServerService.RacerPinVerdict.Wrong, server.CheckRacerPin("10.0.0.6", Bill, BillPin));
    }

    [Fact]
    public void Ten_wrong_pins_from_one_phone_stop_that_phone_even_with_the_right_one()
    {
        var server = Server();

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(WebServerService.RacerPinVerdict.Wrong, server.CheckRacerPin("10.0.0.9", Sam, $"{100000 + i}"));
        }

        Assert.Equal(WebServerService.RacerPinVerdict.TooManyFromDevice, server.CheckRacerPin("10.0.0.9", Sam, SamPin));

        // Another phone is not held up by it.
        Assert.Equal(WebServerService.RacerPinVerdict.Accepted, server.CheckRacerPin("10.0.0.10", Sam, SamPin));
    }

    [Fact]
    public void Guessing_from_many_phones_closes_racer_pins_for_everybody_and_knowing_your_own_earns_no_pass()
    {
        var server = Server();

        // A phone that has used its own PIN right - which, for the wall and
        // remote PINs, would make it trusted past the lockout.
        Assert.Equal(WebServerService.RacerPinVerdict.Accepted, server.CheckRacerPin("10.0.1.1", Bill, BillPin));

        // A hundred wrong, five a phone from twenty phones.
        for (var phone = 0; phone < 20; phone++)
        {
            for (var guess = 0; guess < 5; guess++)
            {
                server.CheckRacerPin($"10.0.2.{phone}", Sam, $"{200000 + phone * 10 + guess}");
            }
        }

        Assert.Equal(WebServerService.RacerPinVerdict.LockedOut, server.CheckRacerPin("10.0.3.1", Sam, SamPin));
        Assert.Equal(WebServerService.RacerPinVerdict.LockedOut, server.CheckRacerPin("10.0.1.1", Bill, BillPin));
    }

    [Fact]
    public void The_handler_checks_the_driver_s_pin_before_it_changes_or_says_anything()
    {
        var handler = Between(Source("WebServerService.cs"), "private void HandleCheckIn(", "private void AssumeCheckIn(");

        var notOnList = handler.IndexOf("Not a driver on this list", StringComparison.Ordinal);
        var pin = handler.IndexOf("if (!RacerPinAccepted(context, known))", StringComparison.Ordinal);
        var alreadySo = handler.IndexOf("if (known.CheckedIn == wanted)", StringComparison.Ordinal);
        var applied = handler.IndexOf("handler(known.Transponder, wanted);", StringComparison.Ordinal);

        Assert.True(notOnList >= 0 && pin > notOnList, "the racer PIN is checked once the driver is known");
        Assert.True(alreadySo > pin, "before \"already so\", which would otherwise say who is here");
        Assert.True(applied > pin, "and before the check-in is handed to the laptop");

        // The PIN rides in a header of its own, never the address.
        var check = Between(Source("WebServerService.cs"), "private bool RacerPinAccepted(", "/// Whether a PIN is good enough to guard the remote");
        Assert.Contains("context.Request.Headers[\"X-Racer-Pin\"]", check);
        Assert.DoesNotContain("QueryString", check);
    }

    [Fact]
    public void The_page_asks_for_the_driver_s_pin_on_every_tap_and_never_keeps_it()
    {
        var page = new WebServerService(
            new RaceTiming(new RaceControlSession()), ProfileStore.Load(), settings: new AppSettings { DisplayLook = Looks.MidnightKey })
            .BuildCheckInPage();

        var toggle = Between(page, "async function toggle(d){", "async function send(payload){");
        var send = Between(page, "async function send(payload){", "var camBtn");

        foreach (var tap in new[] { toggle, send })
        {
            Assert.Contains("askRacerPin(", tap);
            Assert.Contains("'X-Racer-Pin': racerPin", tap);
            // Never in the address: nothing the fetch's URL is built from.
            Assert.DoesNotContain("encodeURIComponent(racerPin)", tap);
            Assert.DoesNotContain("pin=", tap, StringComparison.OrdinalIgnoreCase);
        }

        var ask = Between(page, "function askRacerPin(", "async function toggle(d){");
        Assert.DoesNotContain("localStorage", ask);
        Assert.DoesNotContain("sessionStorage", ask);
    }

    [Fact]
    public void The_laptop_hands_the_page_the_racers_pins_and_makes_them_for_a_wifi_only_club()
    {
        var relay = Source("MainForm.Relay.cs");

        var publish = Between(relay, "private void PublishCheckInRoster()", "private void PublishCheckInRosterSoon()");
        Assert.Contains("_webServer.PublishRacerPins(", publish);
        Assert.Contains("!_settings.RelayRacerPinsEnabled ? null : _settings.RacerPins", publish);

        var keep = Between(relay, "private void KeepRacerPinsInStep()", "private bool RunRacerPinsPass()");
        Assert.Contains("!(_settings.RelayCheckInEnabled || _webServer.IsRunning)", keep);
    }

    // --- Reading the source --------------------------------------------------------------

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

        Assert.True(end > start, $"\"{until}\" no longer follows \"{from}\" - this test needs its bounds updated.");

        return source[start..end];
    }
}
