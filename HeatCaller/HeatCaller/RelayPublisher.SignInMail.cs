using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// "EMAIL ME A LINK TO MY PAGE" (14 Nov 2026, driver phone plan item 17,
/// Asher: "yes to email me a sign in link. make it optional").
///
/// A driver whose phone forgot them types the email the club has for them on
/// Me; the relay queues it (relay/src/signinmail.ts). This collects the
/// queue on its own thirty-second tick, looks each address up among the
/// club's drivers, and emails a match their own night's link
/// (NightEmail.NightLink) from the club's mailbox. An address that matches
/// nobody gets nothing - and the phone was told the same thing either way, so
/// nothing says who the club has. Everything collected is acknowledged, sent
/// or not, so the relay never asks twice.
///
/// ONLY WHILE ON. The relay offers the link only when the check-in document
/// says so (RelayDocuments: RelayEmailSignInLink and the mail set up), and
/// this reads the queue only then too.
/// </summary>
public sealed partial class RelayPublisher
{
    private static readonly TimeSpan SignInMailPollEvery = TimeSpan.FromSeconds(30);

    private long _nextSignInMailTick;

    /// <summary>
    /// The transponder of the one driver with this email address, or null for
    /// none or more than one. Set by MainForm; null until then, and then
    /// nothing is collected.
    /// </summary>
    internal Func<string, string?>? TransponderForEmail { get; set; }

    /// <summary>Whether this laptop should be collecting the queue at all.</summary>
    internal static bool SignInMailWanted(AppSettings settings) =>
        settings.RelayEmailSignInLink && settings.RelayCheckInEnabled && settings.PublishDriverNightsToTheRelay && PinMailer.IsConfigured(settings);

    /// <summary>One waiting ask, as the relay's inbox gives it.</summary>
    internal sealed record SignInMailAsk(long N, string Email);

    /// <summary>The asks in an inbox answer; anything unreadable is left out.</summary>
    internal static IReadOnlyList<SignInMailAsk> ParseSignInMailInbox(string json)
    {
        var asks = new List<SignInMailAsk>();

        try
        {
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return asks;
            }

            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("n", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt64(out var number) && number > 0
                    && item.TryGetProperty("email", out var email) && email.ValueKind == JsonValueKind.String
                    && email.GetString() is { Length: > 0 and <= 254 } address)
                {
                    asks.Add(new SignInMailAsk(number, address.Trim()));
                }
            }
        }
        catch (JsonException)
        {
            // Nothing readable: nothing collected.
        }

        return asks;
    }

    /// <summary>The email a driver is sent: their club, and the link to their own page.</summary>
    internal static (string Subject, string Body) SignInMailMessage(string clubName, string link) =>
        ($"Your page at {clubName}",
         $"Here's the link to your own page at {clubName}, as you asked:\r\n\r\n{link}\r\n\r\n"
         + "Open it on your phone and it remembers you. Adding the club's page to your Home Screen keeps you signed in.\r\n\r\n"
         + "If you didn't ask for this, you can ignore it - nothing has changed.\r\n");

    /// <summary>Once per pass, from the worker loop: on its own tick, only while wanted.</summary>
    partial void CollectSignInMailPass(CancellationToken token)
    {
        if (!SignInMailWanted(_settings) || TransponderForEmail is null)
        {
            return;
        }

        if (Environment.TickCount64 < _nextSignInMailTick)
        {
            return;
        }

        _nextSignInMailTick = Environment.TickCount64 + (long)SignInMailPollEvery.TotalMilliseconds;
        var target = _target;

        if (!IsSafeForToken(target.Root))
        {
            return;
        }

        var club = Uri.EscapeDataString(target.Club);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{target.Root}/api/v1/c/{club}/signin/email/inbox");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
            using var response = Send(request, token);

            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var asks = ParseSignInMailInbox(response.Content.ReadAsStringAsync(token).GetAwaiter().GetResult());

            if (asks.Count == 0)
            {
                return;
            }

            var clubName = PinMailer.ClubName(_settings);

            foreach (var ask in asks)
            {
                var transponder = TransponderForEmail(ask.Email);
                var link = transponder is null ? null : NightEmail.NightLink(_settings, transponder);

                if (link is null)
                {
                    continue;
                }

                var (subject, body) = SignInMailMessage(clubName, link);
                var sent = PinMailer.SendOneAsync(_settings, ask.Email, subject, body, token).GetAwaiter().GetResult();
                Log?.Invoke(sent.Ok
                    ? "[relay] Emailed a driver the link to their page, as they asked on Me."
                    : $"[relay] Couldn't email a driver the link to their page: {sent.Message}");
            }

            using var ack = new HttpRequestMessage(HttpMethod.Post, $"{target.Root}/api/v1/c/{club}/signin/email/ack");
            ack.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
            ack.Content = new StringContent(JsonSerializer.Serialize(new { upTo = asks.Max(a => a.N) }), Encoding.UTF8, "application/json");
            using var _ = Send(ack, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[relay] Couldn't collect sign-in link asks: {Plain(ex)}");
        }
    }
}
