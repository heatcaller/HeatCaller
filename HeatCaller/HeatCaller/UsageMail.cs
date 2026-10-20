using System.Net.Mail;

namespace HeatCaller;

/// <summary>
/// The two emails a club gets about each monthly allowance: at 80% used, and
/// when it is used up. UsageMeters decides WHEN (once per meter, per month,
/// per level, remembered across restarts); this only writes and sends.
///
/// Through the club's own mailbox (PinMailer), and only when that is set up
/// and UsageEmailsOn is ticked. On a background task, and quiet about it: a
/// failure is a line in the console log, never a message box in the middle
/// of a race - the window and the sidebar chip say the same thing anyway.
/// </summary>
internal static class UsageMail
{
    /// <summary>
    /// Sends the email for <paramref name="meter"/> reaching
    /// <paramref name="level"/>, if the club wants it. Returns at once;
    /// never throws.
    /// </summary>
    internal static void Send(UsageMeters meters, Meter meter, MeterLevel level)
    {
        try
        {
            if (level is not (MeterLevel.Watch or MeterLevel.Spent) || meters.Settings is not { } settings)
            {
                return;
            }

            if (!settings.UsageEmailsOn)
            {
                return;
            }

            if (AppPaths.IsRehearsal)
            {
                return;
            }

            if (meters.NoticesUnknown)
            {
                meters.Log($"[usage] {Subject(meter, level)} - not emailed: the record of which emails have already gone this month couldn't be read.");
                return;
            }

            if (PinMailer.ConfigurationProblem(settings) is { } problem)
            {
                meters.Log($"[usage] {Subject(meter, level)} - not emailed: {problem}");
                return;
            }

            var to = AddressFor(settings);

            // Whether there is a model here to take over - the same places the
            // Commentary page builds it from - so a used-up month with none
            // is not emailed as "the model on this PC is writing instead".
            var localModel = meter.Kind != MeterKind.Commentary
                || BundledLlmClient.IsModelAvailableWith(settings.CommentaryLocalModelPath);

            var subject = Subject(meter, level, localModel);
            var body = Body(meter, level, PinMailer.ClubName(settings), localModel);

            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await PinMailer.SendOneAsync(settings, to, subject, body, CancellationToken.None).ConfigureAwait(false);

                    meters.Log(result.Ok
                        ? $"[usage] Emailed {to}: {subject}."
                        : $"[usage] Couldn't email {to} that {Lower(subject)}: {result.Message}");
                }
                catch (Exception ex)
                {
                    meters.Log($"[usage] Couldn't email {to} that {Lower(subject)}: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            meters.Log($"[usage] Couldn't email about {meter.Name}: {ex.Message}");
        }
    }

    /// <summary>UsageEmailTo when it is a real address, otherwise the club's own mailbox.</summary>
    internal static string AddressFor(AppSettings settings)
    {
        var chosen = (settings.UsageEmailTo ?? string.Empty).Trim();

        return chosen.Length > 0 && IsAddress(chosen)
            ? chosen
            : settings.SmtpUser.Trim();
    }

    /// <summary>
    /// A plain email address and nothing else - "treasurer@club.org", not
    /// "Pat <pat@club.org>" or "pat@club" - the same test the sign-up form
    /// puts a racer's address to. MailAddress alone takes a display name
    /// and an address with no dot in its domain, and the This Month window
    /// must not tell a club "To pat@club" for an address no email reaches.
    /// </summary>
    internal static bool IsAddress(string? text)
    {
        var value = (text ?? string.Empty).Trim();

        return value.Length > 0
               && MailAddress.TryCreate(value, out var address)
               && string.Equals(address.Address, value, StringComparison.Ordinal)
               && address.Host.Contains('.');
    }

    /// <summary>
    /// "80% of this month's AI commentary is used", or "This month's AI
    /// commentary is used up - the model on this PC is writing instead" -
    /// saying what the club will hear instead, so nobody has to open the app
    /// to find out whether the night is in trouble. Only what is true:
    /// commentary with no model on this PC (<paramref name="localModel"/>
    /// false) falls back to the normal announcements, and a club's own
    /// ElevenLabs key has Kokoro standing in only when the club chose that,
    /// so it says where more is bought instead.
    /// </summary>
    internal static string Subject(Meter meter, MeterLevel level, bool localModel = true) =>
        level == MeterLevel.Spent
            ? $"This month's {meter.Name} is used up - " + meter.Kind switch
            {
                MeterKind.Commentary => localModel
                    ? "the model on this PC is writing instead"
                    : "the normal announcements carry on",
                MeterKind.HeatCallerVoice => "the free Kokoro voice is speaking instead",
                _ => "more characters are bought on elevenlabs.io",
            }
            : $"80% of this month's {meter.Name} is used";

    internal static string Body(Meter meter, MeterLevel level, string club, bool localModel = true)
    {
        var nl = Environment.NewLine;
        var lines = new List<string>
        {
            $"{club}: {Subject(meter, level, localModel)}.",
            string.Empty,
            $"{meter.Name}: {meter.Summary()}",
        };

        if (meter.Forecast() is { } forecast)
        {
            lines.Add(forecast);
        }

        lines.Add(string.Empty);

        if (meter.CanAskForMore)
        {
            lines.Add("To ask for more, open the This Month window in HeatCaller (the allowances at the foot of the sidebar) and press Ask for More...");
        }
        else
        {
            lines.Add("This is the club's own ElevenLabs plan: more characters are bought on elevenlabs.io.");
        }

        lines.Add(string.Empty);
        lines.Add("Sent by HeatCaller. These emails can be turned off in the This Month window.");

        return string.Join(nl, lines) + nl;
    }

    private static string Lower(string sentence) =>
        sentence.Length > 0 && char.IsUpper(sentence[0]) && !sentence.StartsWith("AI", StringComparison.Ordinal)
            ? char.ToLowerInvariant(sentence[0]) + sentence[1..]
            : sentence;
}
