using System.Net.Http.Headers;
using System.Text;

namespace HeatCaller;

/// <summary>
/// Sends the finished days of <see cref="AppUsage"/> to the club's relay
/// (19 Nov 2026, usage totals round 3): POST app-usage with the publish
/// token, which relay/src/appusage.ts checks and keeps for the relay admin
/// app's Product page.
///
/// Only when all of these hold (<see cref="WhyNot"/>):
/// - the box Send HeatCaller anonymous usage totals is on (UsageSharing.IsOn);
/// - this isn't the rehearsal copy or a test run (AppPaths.IsRedirected);
/// - the Terms accepted on this PC are the ones in this build - revision 3
///   or later, which say that usage totals are sent;
/// - the relay is on and set up with a club and token.
///
/// A day goes once it is finished (before today) and is deleted from the PC
/// once the relay has it. Nothing is lost when the relay can't be reached:
/// the day waits on disk and is tried again, until it is 31 days old.
/// </summary>
internal static class AppUsageSender
{
    /// <summary>Days in one report: a busy day is tens of kilobytes; the relay takes 512 KB.</summary>
    internal const int DaysPerReport = 3;

    /// <summary>Reports in one go - enough for the 31 days held, under the relay's 40 a day.</summary>
    internal const int MostReportsPerTry = 11;

    /// <summary>How the try went.</summary>
    internal enum Outcome
    {
        /// <summary>Every finished day went (Days says how many).</summary>
        Sent,

        /// <summary>No finished day was waiting: nothing was asked of the relay.</summary>
        NothingWaiting,

        /// <summary>The relay couldn't be reached, or asked to be tried later; the days wait.</summary>
        TryLater,
    }

    /// <summary>Why nothing may be sent, in a few words for the log, or null when it may.</summary>
    internal static string? WhyNot(bool sharingOn, bool rehearsal, bool termsAccepted, bool relayReady) =>
        !sharingOn ? "the box is off"
        : rehearsal ? "this is the rehearsal copy or a test run"
        : !termsAccepted ? "the current Terms haven't been accepted on this PC"
        : !relayReady ? "the relay isn't set up"
        : null;

    /// <summary>The relay to send to: the publishing relay, when it is on and set up; otherwise null.</summary>
    internal static (string Root, string Club, string Token)? Target(AppSettings settings) =>
        settings.RelayEnabled ? UsageMeters.CommentaryTarget(settings) : null;

    /// <summary>
    /// Sends every finished day, <see cref="DaysPerReport"/> at a time, and
    /// drops each batch the relay takes. Call it on the UI thread, where
    /// the tracker touches the totals: the awaits come back to it. The
    /// caller saves the file afterwards. Never throws, a cancel included.
    /// </summary>
    internal static async Task<(Outcome Outcome, int Days)> SendAsync(
        AppUsage usage,
        (string Root, string Club, string Token) target,
        DateTime now,
        HttpClient http,
        CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(now);
        var sent = 0;

        for (var report = 0; report < MostReportsPerTry; report++)
        {
            var batch = usage.Finished(today).Take(DaysPerReport).ToList();

            if (batch.Count == 0)
            {
                break;
            }

            var dates = batch.Select(day => day.Date).ToList();
            var status = await PostAsync(target, AppUsage.ReportOf(batch), http, ct);

            if (status is >= 200 and < 300)
            {
                usage.Drop(dates, DateTime.UtcNow);
                sent += dates.Count;
                continue;
            }

            if (status == 400)
            {
                // The relay will never take these (too big, or unreadable):
                // sending them again tomorrow would only be refused again.
                usage.Drop(dates, null);
                SessionLog.Write($"[usage] The relay couldn't read {dates.Count} day(s) of usage totals; they were deleted.");
                continue;
            }

            SessionLog.Write(status == 0
                ? "[usage] The relay didn't answer; the usage totals will be tried again later."
                : $"[usage] The relay answered {status} to the usage totals; they will be tried again later.");

            return (Outcome.TryLater, sent);
        }

        return sent == 0 ? (Outcome.NothingWaiting, 0) : (Outcome.Sent, sent);
    }

    /// <summary>The relay's status code, or 0 when it didn't answer.</summary>
    private static async Task<int> PostAsync((string Root, string Club, string Token) target, string json, HttpClient http, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{target.Root}/api/v1/c/{Uri.EscapeDataString(target.Club)}/app-usage")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);

            using var response = await http.SendAsync(request, ct);
            return (int)response.StatusCode;
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
