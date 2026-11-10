using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// "Ask for More...": a club asking HeatCaller for a top-up of its AI
/// commentary or HeatCaller voice this month.
///
/// Two halves, because either alone can be missed. The request is RECORDED
/// on the relay (POST usage/topup-request), where the operator's
/// topup-requests command lists it and a top-up marks it done. And a
/// person is TOLD: an email to support from the club's own mailbox when one
/// is set up - so the reply goes straight back to the club - otherwise the
/// club's email program opens with the request already written.
/// </summary>
internal static class TopUpRequest
{
    /// <summary>The longest note the relay keeps.</summary>
    internal const int MostNoteChars = 500;

    /// <summary>
    /// How a mailto: is opened. AppSupport.Open in the app; the tests put
    /// something here that only remembers it, so a test run never opens a
    /// mail program.
    /// </summary>
    internal static Action<string> OpenMailTo { get; set; } = AppSupport.Open;

    /// <summary>
    /// Raised, on any thread, when the relay answered the ask with an
    /// automatic top-up there and then - so the window can read the meters
    /// again and stop resting the voice on a month that is no longer spent.
    /// </summary>
    internal static event Action<MeterKind>? AutoToppedUp;

    /// <summary>
    /// Records the request and tells the operator. Returns one plain
    /// sentence for the window. Reads settings before its first await, so
    /// call it from the UI thread. Never throws (a cancel included).
    /// </summary>
    public static async Task<string> SendAsync(AppSettings settings, Meter meter, string? note, CancellationToken ct = default)
    {
        try
        {
            return await SendCoreAsync(settings, meter, note, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return "The request couldn't be sent. Try again, or email " + AppSupport.Email + ".";
        }
    }

    private static async Task<string> SendCoreAsync(AppSettings settings, Meter meter, string? note, CancellationToken ct)
    {
        if (!meter.CanAskForMore)
        {
            return "This is the club's own ElevenLabs plan - more characters are bought on elevenlabs.io.";
        }

        if (AppPaths.IsRehearsal)
        {
            return "Nothing was sent - this is the rehearsal copy of HeatCaller.";
        }

        var text = (note ?? string.Empty).Trim();

        if (text.Length > MostNoteChars)
        {
            text = text[..MostNoteChars];
        }

        // Everything from settings, now, before the first await.
        var target = meter.Kind == MeterKind.Commentary
            ? UsageMeters.CommentaryTarget(settings)
            : new HeatCallerVoiceClient(settings).RelayTarget;

        var club = target?.Club ?? (settings.RelayClub ?? string.Empty).Trim().ToLowerInvariant();
        var clubName = PinMailer.ClubName(settings);
        var mailReady = PinMailer.IsConfigured(settings);
        var clubAddress = settings.SmtpUser.Trim();
        var what = meter.Kind == MeterKind.Commentary ? "AI commentary" : "HeatCaller voice";

        var recorded = target is { } relay
            ? await RecordAsync(relay, meter.Kind, text, ct).ConfigureAwait(false)
            : new Recorded(false, false, "This PC isn't connected to the club's relay.");

        // 2026-11-09 Bug hunt 15: the relay can answer an ask on the spot
        // (autoTopUp in its reply). It was never read, so support was still
        // emailed - and an operator acting on that email gave the same
        // top-up twice - and a re-ask it answered said "still open".
        if (recorded.AutoGiven > 0)
        {
            AutoToppedUp?.Invoke(meter.Kind);

            return $"HeatCaller gave this club {recorded.AutoGiven.ToString("N0", CultureInfo.InvariantCulture)} more {meter.Unit} of {what} for this month straight away - nothing more to do.";
        }

        if (recorded.Already)
        {
            // The operator already has it, and was told the first time.
            return $"Already asked this month - the request for more {what} is still open. You'll hear back by email.";
        }

        var subject = $"Top-up request: {(club.Length > 0 ? club : clubName)} - {meter.Name}";
        var body = Body(clubName, club, meter, text, recorded);

        if (mailReady)
        {
            var sent = await PinMailer.SendOneAsync(settings, AppSupport.Email, subject, body, ct).ConfigureAwait(false);

            if (sent.Ok)
            {
                return recorded.Ok
                    ? $"Asked for more {what} - you'll hear back at {clubAddress}."
                    : $"Asked for more {what} by email (the relay couldn't take the request: {recorded.Why}) - you'll hear back at {clubAddress}.";
            }

            // The club's mailbox wouldn't send it: hand it to their email
            // program instead, as if mail were not set up at all.
        }

        OpenMailTo(MailTo(subject, body));

        return (recorded.Ok
                ? $"Asked for more {what}. "
                : $"The relay couldn't take the request ({recorded.Why}). ")
            + $"Your email program should now be open with the request written out - press Send. If nothing opened, email {AppSupport.Email}.";
    }

    /// <summary>What the relay said to the request.</summary>
    private sealed record Recorded(bool Ok, bool Already, string Why, long AutoGiven = 0);

    private static async Task<Recorded> RecordAsync(
        (string Root, string Club, string Token) target,
        MeterKind kind,
        string note,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { meter = kind == MeterKind.Commentary ? "commentary" : "voice", note });

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{target.Root}/api/v1/c/{Uri.EscapeDataString(target.Club)}/usage/topup-request")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);

        HttpResponseMessage response;

        try
        {
            response = await UsageMeters.Http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new Recorded(false, false, "it didn't answer");
        }

        using (response)
        {
            if ((int)response.StatusCode == 429)
            {
                return new Recorded(false, false, "this month's requests are used up");
            }

            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                return new Recorded(false, false, "it didn't accept this club's token");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new Recorded(false, false, "it can't take requests yet");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new Recorded(false, false, $"it answered {(int)response.StatusCode}");
            }

            try
            {
                var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var json = JsonDocument.Parse(text);

                var root = json.RootElement;
                var isObject = root.ValueKind == JsonValueKind.Object;

                var already = isObject
                    && root.TryGetProperty("already", out var flag)
                    && flag.ValueKind == JsonValueKind.True;

                // What the relay gave there and then, when it answered the ask itself.
                var autoGiven = isObject
                    && root.TryGetProperty("autoTopUp", out var auto)
                    && auto.ValueKind == JsonValueKind.Number
                    && auto.TryGetInt64(out var amount)
                    && amount > 0
                        ? amount
                        : 0;

                return new Recorded(true, already, string.Empty, autoGiven);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                // A 2xx that isn't JSON still took the request.
                return new Recorded(true, false, string.Empty);
            }
        }
    }

    private static string Body(string clubName, string club, Meter meter, string note, Recorded recorded)
    {
        var nl = "\r\n";
        var figures = meter.Known && meter.Allowance > 0
            ? $"{meter.Used.ToString("N0", CultureInfo.InvariantCulture)} of {meter.Allowance.ToString("N0", CultureInfo.InvariantCulture)} {meter.Unit}"
              + (meter.TopUp > 0 ? $" (including a top-up of {meter.TopUp.ToString("N0", CultureInfo.InvariantCulture)})" : string.Empty)
              + (meter.ResetsUtc is { } resets ? $", starts again {resets.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" : string.Empty)
            : "not known on this PC";

        var sb = new StringBuilder();
        sb.Append("A club is asking for a top-up.").Append(nl).Append(nl);
        // The club id is all support needs to find the club, so it is all
        // that goes when there is one. The name shown here is the mailbox's
        // From name when one is set, which is as often a person's own name
        // as the club's - and it is no business of a top-up request.
        sb.Append("Club: ").Append(club.Length > 0 ? club : clubName).Append(nl);
        sb.Append("Meter: ").Append(meter.Name).Append(" (").Append(meter.Kind == MeterKind.Commentary ? "commentary" : "voice").Append(')').Append(nl);
        sb.Append("Used: ").Append(figures).Append(nl);
        sb.Append("Note: ").Append(note.Length > 0 ? note : "(none)").Append(nl);
        sb.Append("App: HeatCaller ").Append(AppVersion.Short).Append(nl);
        sb.Append("Recorded on the relay: ").Append(recorded.Ok ? "yes" : $"no ({recorded.Why})").Append(nl);

        return sb.ToString();
    }

    /// <summary>A mailto: to support with the subject and body filled in, every part escaped.</summary>
    internal static string MailTo(string subject, string body) =>
        $"{AppSupport.MailTo}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
}
