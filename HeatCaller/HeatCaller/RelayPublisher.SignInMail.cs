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
    /// The relay (root and club) and the highest ask already emailed there
    /// whose ack hasn't landed yet (bug hunt 19): a failed ack leaves the
    /// asks on the relay, and the next pass must not email them again.
    /// Cleared once an ack lands, as the relay has dropped them by then.
    /// </summary>
    private (string Relay, long UpTo) _signInMailedUnacked = ("", 0);

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

    /// <summary>
    /// Sends one sign-in link email: to, subject, body. PinMailer when null;
    /// a test swaps it to see what would have gone, and when.
    /// </summary>
    internal Func<string, string, string, Task<MailResult>>? SendSignInMail { get; set; }

    /// <summary>The collected emails, one at a time, off the worker thread. Never throws.</summary>
    private async Task SendSignInMailsAsync(IReadOnlyList<(string To, string Subject, string Body)> mails)
    {
        var settings = _settings;
        var send = SendSignInMail ?? ((to, subject, body) => PinMailer.SendOneAsync(settings, to, subject, body, CancellationToken.None));

        // Off the worker before the first send starts.
        await Task.Yield();

        foreach (var (to, subject, body) in mails)
        {
            try
            {
                var sent = await send(to, subject, body).ConfigureAwait(false);
                Log?.Invoke(sent.Ok
                    ? "[relay] Emailed a driver the link to their page, as they asked on Me."
                    : $"[relay] Couldn't email a driver the link to their page: {sent.Message}");
            }
            catch (Exception ex)
            {
                Log?.Invoke($"[relay] Couldn't email a driver the link to their page: {Plain(ex)}");
            }
        }
    }

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
            var mails = new List<(string To, string Subject, string Body)>();
            var relay = $"{target.Root}|{target.Club}";
            var mailedUpTo = _signInMailedUnacked.Relay == relay ? _signInMailedUnacked.UpTo : 0;
            var upTo = asks.Max(a => a.N);

            foreach (var ask in asks)
            {
                if (ask.N <= mailedUpTo)
                {
                    continue;
                }

                var transponder = TransponderForEmail(ask.Email);
                var link = transponder is null ? null : NightEmail.NightLink(_settings, transponder);

                if (link is null)
                {
                    continue;
                }

                var (subject, body) = SignInMailMessage(clubName, link);
                mails.Add((ask.Email, subject, body));
            }

            // Sent off this thread (bug hunt 18): this is the worker that
            // drains the live frames, and a send - seconds normally, up to a
            // minute against a slow server, longer behind a PIN or invite
            // batch holding the mailbox - froze the phones' board and had
            // lap calls dropped as stale. Collected and acknowledged here;
            // the mail goes on its own, one at a time, as DriverRequests'
            // notices do.
            if (mails.Count > 0)
            {
                _ = SendSignInMailsAsync(mails);
            }

            // Remembered before the ack, which can time out or fail.
            _signInMailedUnacked = (relay, Math.Max(mailedUpTo, upTo));

            using var ack = new HttpRequestMessage(HttpMethod.Post, $"{target.Root}/api/v1/c/{club}/signin/email/ack");
            ack.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
            ack.Content = new StringContent(JsonSerializer.Serialize(new { upTo }), Encoding.UTF8, "application/json");
            using var acked = Send(ack, token);

            if (acked.IsSuccessStatusCode)
            {
                _signInMailedUnacked = ("", 0);
            }
            else
            {
                Log?.Invoke($"[relay] Couldn't tell the relay the sign-in link asks were collected (HTTP {(int)acked.StatusCode}). Trying again in a moment.");
            }
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
