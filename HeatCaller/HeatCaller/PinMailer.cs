using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

namespace HeatCaller;

/// <summary>What a batch of PIN emails, or a test email, came to.</summary>
internal sealed class MailResult
{
    /// <summary>False when the send could not start, or had to stop - the sign-in was refused, the server could not be reached.</summary>
    public bool Ok { get; init; }

    /// <summary>One sentence for the status line or the top of the result dialog.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Whose PIN went, and which PIN - for RacerPins.MarkEmailed, on the UI thread.</summary>
    public IReadOnlyList<PinMailed> Sent { get; init; } = Array.Empty<PinMailed>();

    /// <summary>"Name (address): why", one per racer whose email did not go.</summary>
    public IReadOnlyList<string> Failures { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The racers whose OWN address was at fault - not a valid address, or
    /// refused for good by the server - so an automatic send need not keep
    /// retrying a dead address every ten minutes.
    ///
    /// Only some of <see cref="Failures"/>, on purpose. It used to be every
    /// one of them, and MainForm skips these racers for the rest of the
    /// session: so a fault on the club's side - Gmail's daily sending limit
    /// reached halfway through, a sender address the server will not accept
    /// - left every racer it touched without their PIN until the laptop was
    /// restarted, long after the limit had reset. A fault that is not the
    /// racer's is simply tried again on the next automatic pass.
    /// </summary>
    public IReadOnlyList<string> FailedTransponders { get; init; } = Array.Empty<string>();

    /// <summary>Names of racers who have a PIN but no email address to send it to.</summary>
    public IReadOnlyList<string> NoEmail { get; init; } = Array.Empty<string>();

    public bool Cancelled { get; init; }
}

/// <summary>
/// Emails racers their check-in PINs, straight from this laptop through
/// the club's own mailbox - Gmail, iCloud, Outlook.com - over SMTP with
/// STARTTLS. No cloud service in between: the PINs leave this laptop only
/// inside the emails themselves.
///
/// Settings and profiles belong to the UI thread. Both public methods read
/// everything they need BEFORE their first await, so call them on the UI
/// thread; the sending itself then runs on the thread pool, and nothing in
/// settings is changed from here. The caller applies what was sent (see
/// RacerPins.MarkEmailed) back on the UI thread and saves.
///
/// Neither a PIN nor the password is ever logged or put in a message.
/// </summary>
internal static class PinMailer
{
    /// <summary>Between messages. Gmail and friends throttle a mailbox that suddenly sends a hundred emails in a second.</summary>
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(1500);

    /// <summary>How long one message may take before the server is treated as unreachable.</summary>
    private static readonly TimeSpan PerMessageTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// One send at a time, across the whole app: the Email PINs button and
    /// the automatic send after a new set must never both be emailing the
    /// same racers at once.
    /// </summary>
    private static readonly SemaphoreSlim Busy = new(1, 1);

    /// <summary>The mail presets the Club Website page offers: name, server, port.</summary>
    public static readonly IReadOnlyList<(string Name, string Host, int Port)> Presets = new[]
    {
        ("Gmail", "smtp.gmail.com", 587),
        ("iCloud", "smtp.mail.me.com", 587),
        ("Outlook.com", "smtp-mail.outlook.com", 587),
    };

    public static bool IsSending => Busy.CurrentCount == 0;

    /// <summary>
    /// Why email cannot be sent with these settings, or null when it can -
    /// including a mail server that came from a restored backup and has not
    /// been checked yet (RestoredSettings). Every send asks this, the
    /// automatic ones included; only the test email asks SetupProblem
    /// alone, because sending it IS the check.
    /// </summary>
    public static string? ConfigurationProblem(AppSettings settings) =>
        SetupProblem(settings)
        ?? (RestoredSettings.IsHeld(RestoredSettings.Part.Mail) ? RestoredSettings.MailMessage(settings.SmtpHost) : null);

    /// <summary>What is missing or wrong in the mail settings themselves, or null when nothing is.</summary>
    public static string? SetupProblem(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SmtpHost))
        {
            return "No mail server is set.";
        }

        if (settings.SmtpPort == 465)
        {
            // SmtpClient only speaks STARTTLS; it cannot open the TLS-first
            // connection port 465 expects, and would hang there until the
            // timeout rather than fail clearly.
            return "Port 465 isn't supported - use 587, which every mainstream provider also offers.";
        }

        if (settings.SmtpPort is < 1 or > 65535)
        {
            return "The mail server's port isn't valid - it is usually 587.";
        }

        if (!MailAddress.TryCreate(settings.SmtpUser.Trim(), out _))
        {
            return "Enter the club's email address.";
        }

        if (string.IsNullOrEmpty(settings.SmtpPassword))
        {
            return "Enter the mailbox's app password.";
        }

        return null;
    }

    public static bool IsConfigured(AppSettings settings) => ConfigurationProblem(settings) is null;

    /// <summary>
    /// Whether SendPinsAsync would actually send a racer their PIN right now
    /// - the same tests it makes before it sends anything: the mailbox set
    /// up, personal PINs on, a check-in page to point the racer at (which
    /// needs publishing to the relay on, with its address and club), and
    /// not the rehearsal copy, which never sends email.
    ///
    /// For anything that tells a racer "your PIN is on its way" before the
    /// sending has happened. Approving a sign-up did that on IsConfigured
    /// alone, so with publishing switched off, or in a rehearsal, the phone
    /// was promised an email that SendPinsAsync then refused to send.
    /// Busy-ness (IsSending) is left to the caller, which says why.
    /// </summary>
    public static bool CanSendPinsNow(AppSettings settings) =>
        IsConfigured(settings)
        && settings.RelayRacerPinsEnabled
        && RelayCheckIn.CheckInPageUrl(settings) is not null
        && !AppPaths.IsRehearsal;

    /// <summary>
    /// The club's name as the emails give it. There is no separate setting
    /// for it: the From name when one is set, otherwise the club id made
    /// readable ("acme-rc" becomes "Acme Rc").
    /// </summary>
    public static string ClubName(AppSettings settings)
    {
        var fromName = settings.SmtpFromName.Trim();

        if (fromName.Length > 0)
        {
            return fromName;
        }

        var words = settings.RelayClub.Trim()
            .Split(new[] { '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]);

        var name = string.Join(" ", words);

        return name.Length > 0 ? name : "the club";
    }

    /// <summary>A test message to the club's own mailbox: proves the server, address and app password in one go.</summary>
    public static async Task<MailResult> SendTestAsync(AppSettings settings, CancellationToken cancel)
    {
        // Not ConfigurationProblem: a restored server is checked by pressing
        // this, deliberately, with the server's name on the page beside it.
        if (SetupProblem(settings) is { } problem)
        {
            return new MailResult { Ok = false, Message = problem };
        }

        var server = Server.From(settings);
        var club = ClubName(settings);

        var message = new Outgoing(
            string.Empty,
            string.Empty,
            server.User,
            server.User,
            $"Test email from {club}",
            $"This is a test from HeatCaller.{Environment.NewLine}{Environment.NewLine}"
            + $"If you can read this, {club} can email racers their check-in PINs from this laptop.{Environment.NewLine}");

        if (!await Busy.WaitAsync(0, CancellationToken.None).ConfigureAwait(false))
        {
            return new MailResult { Ok = false, Message = "PINs are being emailed right now - try the test once that has finished." };
        }

        try
        {
            return await Task.Run(() => SendAllAsync(server, new[] { message }, Array.Empty<string>(), null, cancel, isTest: true), CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            Busy.Release();
        }
    }

    /// <summary>
    /// One email per racer who has a PIN and an email address, each with
    /// their own PIN - to everyone, or only to <paramref name="onlyTransponders"/>.
    /// A racer whose email is refused is noted and the rest carry on; a
    /// refused sign-in or an unreachable server stops the lot, since every
    /// message after it would fail the same way.
    /// </summary>
    public static async Task<MailResult> SendPinsAsync(
        AppSettings settings,
        ProfileStore profiles,
        IEnumerable<string>? onlyTransponders,
        IProgress<string>? progress,
        CancellationToken cancel)
    {
        // Everything read here, on the caller's (UI) thread, before the
        // first await.
        if (ConfigurationProblem(settings) is { } problem)
        {
            return new MailResult { Ok = false, Message = problem };
        }

        if (!settings.RelayRacerPinsEnabled)
        {
            return new MailResult { Ok = false, Message = "Personal PINs are switched off." };
        }

        var page = RelayCheckIn.CheckInPageUrl(settings);

        if (page is null)
        {
            return new MailResult
            {
                Ok = false,
                Message = "Online check-in isn't set up and switched on yet, so there is nowhere to use a PIN.",
            };
        }

        var only = onlyTransponders?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var server = Server.From(settings);
        var club = ClubName(settings);
        var validUntil = RacerPins.ValidUntil(settings);
        var messages = new List<Outgoing>();
        var noEmail = new List<string>();

        foreach (var profile in profiles.All)
        {
            if ((only is not null && !only.Contains(profile.Transponder))
                || !settings.RacerPins.TryGetValue(profile.Transponder, out var pin))
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();
            var address = RacerPins.EmailFor(profile);

            if (address is null)
            {
                noEmail.Add(name);
                continue;
            }

            messages.Add(new Outgoing(
                profile.Transponder,
                pin,
                address,
                name,
                $"Your check-in PIN for {club}",
                Body(FirstName(profile), club, pin, page, validUntil)));
        }

        if (messages.Count == 0)
        {
            return new MailResult
            {
                Ok = true,
                Message = noEmail.Count == 0 ? "Nobody to email." : "Nobody with a PIN has an email address.",
                NoEmail = noEmail,
            };
        }

        if (!await Busy.WaitAsync(0, CancellationToken.None).ConfigureAwait(false))
        {
            return new MailResult { Ok = false, Message = "PINs are already being emailed - wait for that to finish.", NoEmail = noEmail };
        }

        try
        {
            return await Task.Run(() => SendAllAsync(server, messages, noEmail, progress, cancel, isTest: false), CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            Busy.Release();
        }
    }

    /// <summary>
    /// One email per invite that has an address: the code, and a link that
    /// opens the club's sign-up page with the code already filled in.
    /// <see cref="MailResult.Sent"/> carries each invite's id where a PIN
    /// email carries a transponder, for the caller to mark it emailed.
    /// Everything is read before the first await, so call this on the UI
    /// thread; the sending itself runs on the thread pool.
    /// </summary>
    public static async Task<MailResult> SendInvitesAsync(
        AppSettings settings,
        IReadOnlyList<SignupInvite> invites,
        IProgress<string>? progress,
        CancellationToken cancel)
    {
        if (ConfigurationProblem(settings) is { } problem)
        {
            return new MailResult { Ok = false, Message = problem };
        }

        if (SignupStore.PageUrl(settings) is null)
        {
            return new MailResult
            {
                Ok = false,
                Message = "Publishing to the relay isn't set up and switched on yet, so there is no sign-up page to send anyone to.",
            };
        }

        var server = Server.From(settings);
        var club = ClubName(settings);
        var messages = new List<Outgoing>();
        var noEmail = new List<string>();

        foreach (var invite in invites)
        {
            var link = SignupStore.InviteUrl(settings, invite);

            if (invite.Email.Length == 0 || link is null)
            {
                noEmail.Add(invite.ForText);
                continue;
            }

            messages.Add(new Outgoing(
                invite.Id,
                invite.Code,
                invite.Email,
                invite.Note.Length > 0 ? invite.Note : invite.Email,
                $"Your invite to race with {club}",
                InviteBody(club, invite.Code, link, invite.ExpiresUtc)));
        }

        if (messages.Count == 0)
        {
            return new MailResult { Ok = true, Message = "Nobody to email.", NoEmail = noEmail };
        }

        if (!await Busy.WaitAsync(0, CancellationToken.None).ConfigureAwait(false))
        {
            return new MailResult { Ok = false, Message = "PINs are being emailed right now - try again once that has finished.", NoEmail = noEmail };
        }

        try
        {
            return await Task.Run(
                    () => SendAllAsync(server, messages, noEmail, progress, cancel, isTest: false, summaryFor: n => $"Emailed {Plural.Of(n, "invite")}."),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            Busy.Release();
        }
    }

    // -------------------------------------------------------------- sending

    /// <summary>The mail server and sign-in, copied out of settings so the send never reads settings off the UI thread.</summary>
    private sealed record Server(string Host, int Port, string User, string Password, string FromName)
    {
        public static Server From(AppSettings settings) => new(
            settings.SmtpHost.Trim(),
            settings.SmtpPort,
            settings.SmtpUser.Trim(),
            settings.SmtpPassword,
            ClubName(settings));
    }

    /// <summary>One message, ready to go. Transponder and PIN are empty for the test.</summary>
    private sealed record Outgoing(string Transponder, string Pin, string To, string ToName, string Subject, string Body);

    private static async Task<MailResult> SendAllAsync(
        Server server,
        IReadOnlyList<Outgoing> messages,
        IReadOnlyList<string> noEmail,
        IProgress<string>? progress,
        CancellationToken cancel,
        bool isTest,
        Func<int, string>? summaryFor = null)
    {
        var sent = new List<PinMailed>();
        var failures = new List<string>();
        var failedTransponders = new List<string>();

        MailResult Finish(bool ok, string message, bool cancelled = false) => new()
        {
            Ok = ok,
            Message = message,
            Sent = sent,
            Failures = failures,
            FailedTransponders = failedTransponders,
            NoEmail = noEmail,
            Cancelled = cancelled,
        };

        // A rehearsal copy never emails anybody. Its drivers are the club's
        // real drivers, copied, so a PIN sent from a rehearsal is a real
        // email to a real racer with a PIN that means nothing. See Rehearsal.
        if (AppPaths.IsRehearsal)
        {
            return Finish(false, "Nothing was emailed - this is the rehearsal copy of HeatCaller, which never sends email.");
        }

        if (!MailAddress.TryCreate(server.User, server.FromName, out var sender))
        {
            return Finish(false, "The club's email address isn't a valid address.");
        }

        using var client = new SmtpClient(server.Host, server.Port)
        {
            // STARTTLS on 587: the connection starts plain and is upgraded
            // before the password is sent. Refused outright if the server
            // will not upgrade - the password never crosses in the clear.
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,

            // Before Credentials: setting it afterwards would wipe them.
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(server.User, server.Password),
        };

        for (var i = 0; i < messages.Count; i++)
        {
            var outgoing = messages[i];

            if (i > 0)
            {
                try
                {
                    await Task.Delay(Pause, cancel).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return Finish(false, $"Stopped after {Plural.Of(sent.Count, "email")}.", cancelled: true);
                }
            }

            if (!isTest)
            {
                progress?.Report($"Emailing {i + 1} of {messages.Count}...");
            }

            if (!MailAddress.TryCreate(outgoing.To, outgoing.ToName, out var to))
            {
                failures.Add($"{outgoing.ToName} ({outgoing.To}): not a valid address");
                failedTransponders.Add(outgoing.Transponder);
                continue;
            }

            using var mail = new MailMessage(sender, to)
            {
                Subject = outgoing.Subject,
                Body = outgoing.Body,
                IsBodyHtml = false,
                SubjectEncoding = Encoding.UTF8,
                BodyEncoding = Encoding.UTF8,
            };

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(PerMessageTimeout);

            try
            {
                await client.SendMailAsync(mail, timeout.Token).ConfigureAwait(false);

                if (!isTest)
                {
                    sent.Add(new PinMailed(outgoing.Transponder, outgoing.Pin));
                }
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                return Finish(false, $"Stopped after {Plural.Of(sent.Count, "email")}.", cancelled: true);
            }
            catch (OperationCanceledException)
            {
                return Finish(false, $"{server.Host} didn't answer in time. {SentSoFar(sent.Count)}");
            }
            catch (SmtpFailedRecipientException ex)
            {
                // This one address is refused - a typo, a closed mailbox.
                // Nothing to do with the others.
                failures.Add($"{outgoing.ToName} ({outgoing.To}): {Plain(ex)}");

                // Marked as a dead address only when the refusal is for good
                // and about the address. A server can refuse a recipient for
                // its own reasons too - full for now (4xx), or over the
                // club's sending limit (5.4.5) - and those racers are fine.
                if (IsDeadAddress(ex))
                {
                    failedTransponders.Add(outgoing.Transponder);
                }
            }
            catch (SmtpException ex) when (IsSignInRefused(ex))
            {
                return Finish(false, SignInRefused(server.Host) + " " + SentSoFar(sent.Count));
            }
            catch (SmtpException ex) when (IsUnreachable(ex))
            {
                return Finish(false, $"Couldn't send through {server.Host}:{server.Port.ToString(CultureInfo.InvariantCulture)} - {Plain(ex)} {SentSoFar(sent.Count)}");
            }
            catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException)
            {
                // Not added to failedTransponders: nothing here says the
                // racer's address is the problem - a sender the server will
                // not take, a sending limit, a message it would not accept -
                // so the next automatic pass tries them again.
                failures.Add($"{outgoing.ToName} ({outgoing.To}): {Plain(ex)}");

                // The same fault three times running is the server, not the
                // racers - stop rather than fail a hundred emails one by one.
                if (failures.Count >= 3 && sent.Count == 0)
                {
                    return Finish(false, $"Stopped: the first three emails all failed ({Plain(ex)}).");
                }
            }
        }

        if (isTest)
        {
            return Finish(failures.Count == 0, failures.Count == 0
                ? $"Test email sent to {server.User}. Check that mailbox."
                : $"The test email didn't go: {failures[0]}");
        }

        var summary = summaryFor?.Invoke(sent.Count) ?? $"Emailed {Plural.Of(sent.Count, "racer")} their PIN.";

        if (failures.Count > 0)
        {
            summary += $" {Plural.Of(failures.Count, "email")} didn't go.";
        }

        return Finish(true, summary);
    }

    private static string SentSoFar(int count) =>
        count == 0 ? "Nothing was sent." : $"{Plural.Of(count, "email")} went before it stopped.";

    private static string SignInRefused(string host) =>
        host.Contains("gmail", StringComparison.OrdinalIgnoreCase) || host.Contains("google", StringComparison.OrdinalIgnoreCase)
            ? "Gmail refused the sign-in: use an app password, not the normal one."
            : $"{host} refused the sign-in: check the club email address, and use an app password, not the normal one.";

    /// <summary>
    /// A refused sign-in. SmtpClient has no status of its own for 535 -
    /// the code Gmail, iCloud and Outlook all answer a wrong password with -
    /// so the number and the server's own words are both looked at.
    /// </summary>
    private static bool IsSignInRefused(SmtpException ex)
    {
        var code = (int)ex.StatusCode;

        if (code is 530 or 534 or 535)
        {
            return true;
        }

        // Not the word "authentication" on its own: a TLS failure says
        // "Authentication failed" too, and that is the connection, not the
        // password.
        var text = ex.Message;

        return text.Contains("5.7.8", StringComparison.Ordinal)
               || text.Contains("5.7.9", StringComparison.Ordinal)
               || text.Contains("5.7.3", StringComparison.Ordinal)
               || text.Contains("not authenticated", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Authentication unsuccessful", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Username and Password", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a refused recipient is refused because of the address itself
    /// - no such mailbox, not allowed, gone - rather than something passing
    /// on the server's side. Only permanent (5xx) mailbox refusals count,
    /// and not Gmail's "5.4.5" daily-limit answer, which arrives as a 550
    /// refusal of whichever recipient it happened to be sent with.
    /// </summary>
    private static bool IsDeadAddress(SmtpFailedRecipientException ex)
    {
        if (ex.StatusCode is not (SmtpStatusCode.MailboxUnavailable
            or SmtpStatusCode.UserNotLocalTryAlternatePath
            or SmtpStatusCode.MailboxNameNotAllowed))
        {
            return false;
        }

        var text = ex.Message;

        return !text.Contains("5.4.5", StringComparison.Ordinal)
               && !text.Contains("limit", StringComparison.OrdinalIgnoreCase)
               && !text.Contains("quota", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The server could not be reached or would not talk securely - every message after this would fail too.</summary>
    private static bool IsUnreachable(SmtpException ex) =>
        ex.StatusCode is SmtpStatusCode.GeneralFailure or SmtpStatusCode.ServiceNotAvailable
        || ex.InnerException is SocketException or IOException or AuthenticationException or WebException;

    /// <summary>One readable sentence from a mail exception: the innermost message is usually the one that says what went wrong.</summary>
    private static string Plain(Exception ex)
    {
        var inner = ex;

        while (inner.InnerException is not null)
        {
            inner = inner.InnerException;
        }

        var text = inner.Message.Trim();

        return text.Length == 0 ? ex.Message.Trim() : text;
    }

    // --------------------------------------------------------------- content

    private static string FirstName(DriverProfile profile)
    {
        var name = profile.DriverName.Trim();

        if (name.Length == 0)
        {
            return string.Empty;
        }

        return name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
    }

    /// <summary>An invite email. Plain text, like the PIN email, and for the same reason.</summary>
    private static string InviteBody(string club, string code, string link, DateTime expiresUtc)
    {
        var nl = Environment.NewLine;
        var text = new StringBuilder();

        text.Append("Hi,").Append(nl).Append(nl);
        text.Append($"You're invited to sign up to race with {club}.").Append(nl).Append(nl);
        text.Append("To sign up, open this link on your phone:").Append(nl);
        text.Append($"  {link}").Append(nl).Append(nl);
        text.Append($"If it asks for a code, your sign-up code is: {code}").Append(nl).Append(nl);
        text.Append("Fill in your name, class and transponder (or say you'd like to rent one), and the timing desk will ")
            .Append("approve you. Once they have, your own check-in PIN is emailed to you.").Append(nl).Append(nl);
        text.Append($"This invite works once, until {expiresUtc.ToLocalTime().ToString("dddd d MMMM", CultureInfo.CurrentCulture)}.")
            .Append(nl).Append(nl);
        text.Append(club).Append(nl);

        return text.ToString();
    }

    /// <summary>The email itself. Plain text: it has to read the same in every mail app, including the ones that block pictures and styling.</summary>
    private static string Body(string firstName, string club, string pin, string page, DateTime? validUntil)
    {
        var nl = Environment.NewLine;
        var text = new StringBuilder();

        text.Append(firstName.Length > 0 ? $"Hi {firstName}," : "Hi,").Append(nl).Append(nl);
        text.Append($"Your check-in PIN for {club} is: {pin}").Append(nl).Append(nl);
        text.Append("To check in on race day:").Append(nl);
        text.Append($"  1. Open {page}").Append(nl);
        text.Append("  2. Enter your PIN").Append(nl);
        text.Append("  3. Tap Check In").Append(nl).Append(nl);
        text.Append("Or just scan your QR check-in with your phone's camera.").Append(nl).Append(nl);

        if (validUntil is { } until)
        {
            text.Append($"This PIN works until {until.ToString("dddd d MMMM", CultureInfo.CurrentCulture)}. You'll be sent a new one after that.")
                .Append(nl).Append(nl);
        }

        text.Append("Don't share it - it checks in only you.").Append(nl).Append(nl);
        text.Append(club).Append(nl);

        return text.ToString();
    }
}
