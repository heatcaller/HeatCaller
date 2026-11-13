using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// One push notification to every phone signed up for the club's alerts
/// (12 Nov 2026, setup plan step 7): "racing's off tonight", sent when a
/// night is called off, beside the email to the regulars. The relay's
/// <c>POST api/v1/c/&lt;club&gt;/push/broadcast</c>, with the laptop's token.
///
/// Phones sign up for alerts on the Me page, and the relay lets their
/// sign-ups go when the club closes drivers' nights, so this reaches the
/// phones signed up since then. The email is the sure way; this is the
/// quick one.
/// </summary>
public sealed partial class RelayPublisher
{
    /// <summary>The longest title and body the relay takes.</summary>
    internal const int BroadcastTitleMax = 60;

    internal const int BroadcastBodyMax = 240;

    /// <summary>The request's body, cut to the relay's limits.</summary>
    internal static string BroadcastBody(string title, string body, string tag) =>
        JsonSerializer.Serialize(new
        {
            title = Cut(title, BroadcastTitleMax),
            body = Cut(body, BroadcastBodyMax),
            tag,
        });

    /// <summary>
    /// One line, cut to fit: the relay refuses any control character, line
    /// breaks included, and the club's words come from a box that takes them.
    /// </summary>
    private static string Cut(string text, int max)
    {
        var line = System.Text.RegularExpressions.Regex.Replace(new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray()), @"\s+", " ").Trim();

        return line.Length <= max ? line : line[..(max - 1)].TrimEnd() + "…";
    }

    /// <summary>
    /// Sends the push. Returns what happened in one sentence for the console
    /// and the call-off dialog - never throws.
    /// </summary>
    public Task<string> BroadcastPushAsync(string title, string body, CancellationToken token) => Task.Run(() =>
    {
        var target = _target;

        if (!_settings.RelayEnabled || !IsConfigured())
        {
            return "no push: the club website isn't set up.";
        }

        if (!IsSafeForToken(target.Root))
        {
            return "no push: " + UnsafeAddressMessage;
        }

        try
        {
            var club = Uri.EscapeDataString(target.Club);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{target.Root}/api/v1/c/{club}/push/broadcast");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
            request.Content = new StringContent(BroadcastBody(title, body, "nextnight"), Encoding.UTF8, "application/json");

            using var response = Send(request, token);
            var text = response.Content.ReadAsStringAsync(token).GetAwaiter().GetResult();

            return BroadcastAnswer((int)response.StatusCode, text);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return "no push: stopped.";
        }
        catch (Exception ex)
        {
            return "no push: " + Plain(ex);
        }
    }, token);

    /// <summary>The relay's answer as one sentence.</summary>
    internal static string BroadcastAnswer(int status, string body)
    {
        if (status == 404)
        {
            return "no push: the relay is older than this - redeploy it (fly deploy --ha=false).";
        }

        if (status == 429)
        {
            return "no push: three have gone in the last hour already.";
        }

        if (status == 503)
        {
            return "no push: alerts aren't set up on the relay.";
        }

        if (status is < 200 or >= 300)
        {
            return $"no push: the relay answered HTTP {status}.";
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var sent = root.TryGetProperty("sent", out var s) && s.TryGetInt32(out var n) ? n : 0;
            var badges = root.TryGetProperty("badges", out var b) && b.TryGetInt32(out var m) ? m : 0;

            return badges == 0
                ? "a push went out, but no phone is signed up for alerts just now."
                : $"a push went to {Plural.Of(sent, "phone")} ({Plural.Of(badges, "driver")} signed up for alerts).";
        }
        catch (JsonException)
        {
            return "a push went out.";
        }
    }
}
