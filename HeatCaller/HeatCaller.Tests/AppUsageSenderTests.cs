using System.Net;
using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Sending the usage totals (19 Nov 2026, round 3): only finished days, in
/// the relay's shape, a few at a time, deleted once the relay has them and
/// kept when it can't be reached - and nothing at all unless the box is on,
/// the current Terms were accepted and the relay is set up.
/// </summary>
public sealed class AppUsageSenderTests : IDisposable
{
    private static readonly AppUsage.Context Evening = new(RaceDay: false, DeskView: false);
    private static readonly DateTime Today = new(2026, 11, 19, 18, 0, 0);
    private static readonly (string Root, string Club, string Token) Relay = ("https://relay.test", "acme-rc", "acme-token");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hc-usage-send-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private AppUsage New(params DriverProfile[] drivers) => new(Path.Combine(_folder, AppUsage.FileName), () => drivers, "0.63.0");

    /// <summary>A minute of Live Timing on each of these days before today.</summary>
    private AppUsage WithDays(params int[] daysAgo)
    {
        var usage = New();

        foreach (var ago in daysAgo.Append(0))
        {
            var at = Today.AddDays(-ago);
            usage.Enter("Live Timing", "", at, Evening);
            usage.Mark(at.AddSeconds(15), inFront: true, active: true, Evening);
            usage.Mark(at.AddSeconds(30), inFront: true, active: true, Evening);
            usage.Enter("Setup", "Templates", at.AddSeconds(30), Evening);
        }

        return usage;
    }

    private static Task<(AppUsageSender.Outcome Outcome, int Days)> Send(AppUsage usage, Answers relay) =>
        AppUsageSender.SendAsync(usage, Relay, Today, new HttpClient(relay));

    [Fact]
    public async Task Finished_days_go_and_are_deleted_today_stays()
    {
        var usage = WithDays(2, 1);
        var relay = new Answers(_ => HttpStatusCode.NoContent);

        var (outcome, days) = await Send(usage, relay);

        Assert.Equal(AppUsageSender.Outcome.Sent, outcome);
        Assert.Equal(2, days);
        Assert.Equal([DateOnly.FromDateTime(Today)], usage.Days.Select(d => d.Date));
        Assert.NotNull(usage.LastSentUtc);

        var request = Assert.Single(relay.Requests);
        Assert.Equal("https://relay.test/api/v1/c/acme-rc/app-usage", request.Url);
        Assert.Equal("Bearer acme-token", request.Authorization);
    }

    [Fact]
    public async Task The_report_is_the_relays_shape_and_nothing_else()
    {
        var usage = WithDays(1);
        var relay = new Answers(_ => HttpStatusCode.NoContent);

        await Send(usage, relay);

        using var json = JsonDocument.Parse(relay.Requests.Single().Body);
        var root = json.RootElement;
        Assert.Equal(["version", "days"], root.EnumerateObject().Select(p => p.Name));

        var day = root.GetProperty("days").EnumerateArray().Single();
        Assert.Equal("2026-11-18", day.GetProperty("date").GetString());
        Assert.Equal(["date", "version", "rows", "events"], day.EnumerateObject().Select(p => p.Name));

        var row = day.GetProperty("rows").EnumerateArray().First(r => r.GetProperty("page").GetString() == "live_timing");
        Assert.Equal(
            ["page", "tab", "raceDay", "deskView", "opens", "activeSeconds", "shownSeconds"],
            row.EnumerateObject().Select(p => p.Name));
        Assert.Equal(30, row.GetProperty("activeSeconds").GetDouble());
    }

    [Fact]
    public async Task Nothing_finished_asks_nothing_of_the_relay()
    {
        var usage = WithDays();
        var relay = new Answers(_ => HttpStatusCode.NoContent);

        Assert.Equal((AppUsageSender.Outcome.NothingWaiting, 0), await Send(usage, relay));
        Assert.Empty(relay.Requests);
    }

    [Fact]
    public async Task A_few_days_go_in_each_report()
    {
        var usage = WithDays(7, 6, 5, 4, 3, 2, 1);
        var relay = new Answers(_ => HttpStatusCode.NoContent);

        var (_, days) = await Send(usage, relay);

        Assert.Equal(7, days);
        Assert.Equal([3, 3, 1], relay.Requests.Select(r => JsonDocument.Parse(r.Body).RootElement.GetProperty("days").GetArrayLength()));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_relay_that_says_later_keeps_the_days(HttpStatusCode answer)
    {
        var usage = WithDays(2, 1);

        var (outcome, days) = await Send(usage, new Answers(_ => answer));

        Assert.Equal(AppUsageSender.Outcome.TryLater, outcome);
        Assert.Equal(0, days);
        Assert.Equal(3, usage.Days.Count);
        Assert.Null(usage.LastSentUtc);
    }

    [Fact]
    public async Task A_relay_that_cant_be_reached_keeps_the_days()
    {
        var usage = WithDays(1);

        var (outcome, _) = await Send(usage, new Answers(_ => throw new HttpRequestException("offline")));

        Assert.Equal(AppUsageSender.Outcome.TryLater, outcome);
        Assert.Equal(2, usage.Days.Count);
    }

    [Fact]
    public async Task Days_the_relay_cant_read_are_dropped_not_sent_forever()
    {
        var usage = WithDays(1);

        await Send(usage, new Answers(_ => HttpStatusCode.BadRequest));

        Assert.Equal([DateOnly.FromDateTime(Today)], usage.Days.Select(d => d.Date));
        Assert.Null(usage.LastSentUtc);
    }

    [Fact]
    public async Task Stops_at_the_first_refusal_and_keeps_what_was_left()
    {
        var usage = WithDays(5, 4, 3, 2, 1);
        var calls = 0;

        var (outcome, days) = await Send(usage, new Answers(_ => ++calls == 1 ? HttpStatusCode.NoContent : HttpStatusCode.ServiceUnavailable));

        Assert.Equal(AppUsageSender.Outcome.TryLater, outcome);
        Assert.Equal(3, days);
        Assert.Equal(3, usage.Days.Count); // two finished days and today
    }

    [Fact]
    public async Task Whats_sent_is_saved_and_survives_a_restart()
    {
        var usage = WithDays(1);
        await Send(usage, new Answers(_ => HttpStatusCode.NoContent));
        usage.Save(Today);

        var again = New();
        Assert.Single(again.Days);
        Assert.Equal(usage.LastSentUtc, again.LastSentUtc);
        Assert.DoesNotContain("lastSentUtc", again.ReportJson(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_driver_is_named_in_what_goes()
    {
        var junior = new DriverProfile { Transponder = "3001", DriverName = "Billy Smith", AnnouncerName = "Billy the Kid", IsJunior = true };
        var usage = New(junior);
        var yesterday = Today.AddDays(-1);
        usage.Enter("Driver Details", "Billy Smith", yesterday, Evening);
        usage.Enter("Driver Details", "Billy's Cars", yesterday.AddSeconds(5), Evening);
        var relay = new Answers(_ => HttpStatusCode.NoContent);

        await Send(usage, relay);

        var body = relay.Requests.Single().Body;
        Assert.DoesNotContain("billy", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("smith", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unknown_driver", body, StringComparison.Ordinal);
    }

    // --- When nothing may go ----------------------------------------------------

    [Fact]
    public void Sends_only_with_the_box_on_outside_rehearsal_with_the_terms_and_a_relay()
    {
        Assert.Null(AppUsageSender.WhyNot(sharingOn: true, rehearsal: false, termsAccepted: true, relayReady: true));
        Assert.NotNull(AppUsageSender.WhyNot(sharingOn: false, rehearsal: false, termsAccepted: true, relayReady: true));
        Assert.NotNull(AppUsageSender.WhyNot(sharingOn: true, rehearsal: true, termsAccepted: true, relayReady: true));
        Assert.NotNull(AppUsageSender.WhyNot(sharingOn: true, rehearsal: false, termsAccepted: false, relayReady: true));
        Assert.NotNull(AppUsageSender.WhyNot(sharingOn: true, rehearsal: false, termsAccepted: true, relayReady: false));
    }

    [Fact]
    public void No_target_without_the_relay_on_and_set_up()
    {
        var settings = new AppSettings
        {
            RelayEnabled = false,
            RelayBaseUrl = "https://relay.example.com",
            RelayClub = "acme-rc",
            RelayToken = "acme-token",
        };

        Assert.Null(AppUsageSender.Target(settings));

        settings.RelayEnabled = true;
        settings.RelayToken = "";
        Assert.Null(AppUsageSender.Target(settings));
    }

    [Fact]
    public void The_preview_says_when_days_last_went()
    {
        Assert.Equal("Nothing has been sent from this PC yet.", UsagePreviewForm.LastSent(null));
        Assert.StartsWith("Last sent to your club's relay: ", UsagePreviewForm.LastSent(new DateTime(2026, 11, 19, 3, 0, 0, DateTimeKind.Utc)), StringComparison.Ordinal);
    }

    /// <summary>A relay that answers each request with what <c>answer</c> says, and remembers what came.</summary>
    private sealed class Answers(Func<HttpRequestMessage, HttpStatusCode> answer) : HttpMessageHandler
    {
        internal List<(string Url, string? Authorization, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));
            return new HttpResponseMessage(answer(request));
        }
    }
}
