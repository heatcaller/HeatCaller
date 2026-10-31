using System.Globalization;
using System.Net.Mail;
using System.Text;

namespace HeatCaller;

/// <summary>
/// A driver's night, emailed to them (22 Oct 2026, driver experience,
/// item 11): their races, their report card and the link to their night
/// page, sent from this laptop through the club's own mailbox, the way the
/// PIN emails are (PinMailer).
///
/// EVERYTHING HERE IS PURE: who is emailed, what it says and the address it
/// links to are worked out from what it is handed, with no disk, no network
/// and no clock. MainForm.NightEmail gathers the night and sends it; the
/// Club Website page asks before anything goes.
///
/// WHO, and every one of these has to be true (22 Oct 2026, driver
/// experience): the club offers it (NightEmailOffered), the driver asked for
/// it (DriverProfile.EmailMyNight), has an address in their profile's Email
/// box, raced tonight, has a name, is not a rental car - and has not already
/// been emailed this night (NightEmailSentFor / NightEmailSentTo), so a
/// second press, or the automatic send after a manual one, never emails
/// anybody twice. Only the Email box counts, not Contact: Contact is a note
/// the desk keeps, and the driver asked for their night at their email.
///
/// THE TRANSPONDER NEVER GOES IN THE EMAIL. It is the key everything here is
/// looked up by and nothing a driver needs; the night link carries the
/// badge id (RelayCheckIn.IdFor) instead, in the "#d=" fragment form, which
/// no server ever sees in its logs (pages.ts, the night page).
/// </summary>
internal static class NightEmail
{
    /// <summary>One email, ready for PinMailer.SendBatchAsync. Key is the driver's transponder, for marking them sent - it is never in the email itself.</summary>
    internal sealed record Recipient(string Transponder, string To, string ToName, string FirstName);

    /// <summary>
    /// Who would be emailed tonight, as the Club Website page asks before it
    /// sends: the messages themselves, and the drivers left out and why, so
    /// the question can say so. <see cref="Problem"/> is why nothing can be
    /// sent at all, or null.
    /// </summary>
    internal sealed record Plan(
        string Night,
        IReadOnlyList<(string Key, string To, string ToName, string Subject, string Body)> Messages,
        int AlreadySent,
        int NoAddress,
        string? Problem)
    {
        /// <summary>Nothing to send, and why.</summary>
        public static Plan Nothing(string night, string? problem) =>
            new(night, Array.Empty<(string, string, string, string, string)>(), 0, 0, problem);
    }

    /// <summary>
    /// One car's part of a night email, when the driver raced a second
    /// transponder as a car of its own (22 Oct 2026): its heading
    /// (<see cref="CarHeading"/>), its facts and its report card.
    /// </summary>
    internal sealed record EmailCar(string Heading, IReadOnlyList<string> Facts, IReadOnlyList<string>? Coach);

    /// <summary>"Main car (B6.4)", or the label alone when the results don't say which car.</summary>
    internal static string CarHeading(string label, string? name) =>
        string.IsNullOrWhiteSpace(name) ? label.Trim() : $"{label.Trim()} ({name.Trim()})";

    /// <summary>Who would be emailed, and the ones who asked and won't be.</summary>
    internal sealed record Choice(IReadOnlyList<Recipient> ToSend, int AlreadySent, int NoAddress);

    /// <summary>The night as NightEmailSentFor keeps it: yyyy-MM-dd, invariant.</summary>
    internal static string NightKey(DateTime nightLocal) =>
        nightLocal.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Everybody to email tonight: see WHO at the top of this class.
    /// <paramref name="racedTonight"/> is the transponders of everybody who
    /// raced (ghosts already left out); each is counted once whatever its
    /// case or spacing.
    /// </summary>
    internal static Choice Who(
        AppSettings settings,
        Func<string, DriverProfile?> profileOf,
        IEnumerable<string> racedTonight,
        string night)
    {
        if (!settings.NightEmailOffered)
        {
            return new Choice(Array.Empty<Recipient>(), 0, 0);
        }

        var toSend = new List<Recipient>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var alreadySent = 0;
        var noAddress = 0;

        foreach (var raw in racedTonight)
        {
            var transponder = (raw ?? string.Empty).Trim();

            if (transponder.Length == 0 || !seen.Add(transponder))
            {
                continue;
            }

            var profile = profileOf(transponder);

            if (profile is null || !profile.EmailMyNight || profile.IsRental)
            {
                continue;
            }

            var name = (profile.DriverName ?? string.Empty).Trim();

            // A car nobody has named has no night (DriverNight.Document), so
            // there is nothing to email either.
            if (name.Length == 0)
            {
                continue;
            }

            if (IsSent(settings, night, transponder))
            {
                alreadySent++;
                continue;
            }

            var address = AddressOf(profile.Email);

            if (address is null)
            {
                noAddress++;
                continue;
            }

            toSend.Add(new Recipient(transponder, address, name, FirstName(name)));
        }

        return new Choice(toSend, alreadySent, noAddress);
    }

    /// <summary>Whether this driver has already been emailed this night.</summary>
    internal static bool IsSent(AppSettings settings, string night, string transponder) =>
        string.Equals(settings.NightEmailSentFor, night, StringComparison.Ordinal)
        && settings.NightEmailSentTo.Any(t => string.Equals(t?.Trim(), transponder.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Records who was emailed this night, so nobody is sent it twice. A new
    /// night starts the list again - last week's list is no use, and it
    /// would only grow. True when settings changed and want saving. Call on
    /// the UI thread, where settings are changed.
    /// </summary>
    internal static bool MarkSent(AppSettings settings, string night, IEnumerable<string> transponders)
    {
        var changed = false;

        if (!string.Equals(settings.NightEmailSentFor, night, StringComparison.Ordinal))
        {
            settings.NightEmailSentFor = night;
            settings.NightEmailSentTo = new List<string>();
            changed = true;
        }

        foreach (var raw in transponders)
        {
            var transponder = (raw ?? string.Empty).Trim();

            if (transponder.Length == 0 || IsSent(settings, night, transponder))
            {
                continue;
            }

            settings.NightEmailSentTo.Add(transponder);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// The address in a profile's Email box, or null when it is not one a
    /// mail could go to. The same rule RacerPins.EmailFor keeps: a bare
    /// address that reads back as itself, with a dot in its domain.
    /// </summary>
    internal static string? AddressOf(string? email)
    {
        var value = (email ?? string.Empty).Trim();

        if (value.Length == 0
            || value.Contains(' ')
            || !value.Contains('@')
            || !MailAddress.TryCreate(value, out var address)
            || !string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase)
            || !address.Host.Contains('.'))
        {
            return null;
        }

        return address.Address;
    }

    /// <summary>
    /// The driver's own night page, in the "#d=" form - or null when the club
    /// isn't publishing drivers' nights to a relay, so there is no page to
    /// link to and the email simply says nothing about one.
    ///
    /// The badge key is read, never made here (not RelayCheckIn.EnsureSecret):
    /// with no key there are no badges, and no night pages keyed by them.
    /// </summary>
    internal static string? NightLink(AppSettings settings, string transponder)
    {
        if (!settings.RelayEnabled || !settings.PublishDriverNightsToTheRelay || string.IsNullOrWhiteSpace(transponder))
        {
            return null;
        }

        var secret = settings.RelayCheckInSecret;
        var root = RelayPublisher.NormaliseBaseUrl(settings.RelayBaseUrl);

        // Lower case, as the publisher sends it and the relay holds it.
        var club = settings.RelayClub.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(secret) || root.Length == 0 || club.Length == 0)
        {
            return null;
        }

        var badge = RelayCheckIn.IdFor(transponder, secret);

        return DriverNight.IsADriverId(badge)
            ? $"{root}/c/{Uri.EscapeDataString(club)}/night#d={badge}"
            : null;
    }

    /// <summary>The subject line: "Your night at Acme RC - Tuesday 22 October".</summary>
    internal static string Subject(string club, DateTime nightLocal) =>
        $"Your night at {club} - {nightLocal.ToString("dddd d MMMM", CultureInfo.CurrentCulture)}";

    /// <summary>
    /// The email itself. Plain text, like the PIN email, so it reads the same
    /// in every mail app.
    ///
    /// The facts are DriverCut's own lines, word for word, as the night page
    /// has them (DriverNight.Lines) - nothing here writes a sentence about
    /// how their night went. The report card is CoachReport's lines with
    /// other drivers named the way the public pages name them.
    /// </summary>
    /// <param name="firstName">Their first name, or empty for a plain "Hi,".</param>
    /// <param name="club">The club's name (PinMailer.ClubName).</param>
    /// <param name="nightLocal">The night - see TonightAtTheTrack.NightOf.</param>
    /// <param name="dayName">What the club called the race day, or null.</param>
    /// <param name="facts">Their races (DriverNight.Lines over DriverCut.FactsFor).</param>
    /// <param name="coach">Their report card, or null.</param>
    /// <param name="nightLink">Their night page (NightLink), or null when there isn't one.</param>
    internal static string Build(
        string firstName,
        string club,
        DateTime nightLocal,
        string? dayName,
        IReadOnlyList<string> facts,
        IReadOnlyList<string>? coach,
        string? nightLink,
        IReadOnlyList<EmailCar>? cars = null)
    {
        var nl = Environment.NewLine;
        var text = new StringBuilder();
        var first = (firstName ?? string.Empty).Trim();

        text.Append(first.Length > 0 ? $"Hi {first}," : "Hi,").Append(nl).Append(nl);

        var when = nightLocal.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture);
        var title = (dayName ?? string.Empty).Trim();

        text.Append(title.Length > 0
                ? $"Here's your night at {club}: {title}, {when}."
                : $"Here's your night at {club} on {when}.")
            .Append(nl).Append(nl);

        // One car: the email as it always was. Two or more (a second
        // transponder raced as a car of its own, 22 Oct 2026): each car's
        // races and report card under its own heading, never added up.
        var parts = cars is { Count: > 0 }
            ? cars.Where(car => car is not null).ToList()
            : new List<EmailCar> { new(string.Empty, facts, coach) };

        foreach (var part in parts)
        {
            var heading = (part.Heading ?? string.Empty).Trim();
            var suffix = heading.Length > 0 ? $" - {heading}" : string.Empty;

            AppendBlock(text, "YOUR RACES" + suffix, part.Facts, nl);
            AppendBlock(text, "YOUR REPORT CARD" + suffix, part.Coach, nl);
        }

        // "You made the podium - if a podium video was made, it's on your
        // night page" went on 28 Oct 2026 (asked for by Asher): the podium
        // videos stay on the club's laptop and are not on the night page.
        if (nightLink is { Length: > 0 } link)
        {
            text.Append("Your night page, with all of this:").Append(nl);
            text.Append($"  {link}").Append(nl);
            text.Append("This link is yours - it can also check you in, so don't share it.").Append(nl);

            // Me and the club's own page (29 Oct 2026, relay navigation round
            // 4): the relay's Me tab remembers a phone once its night has
            // opened there, and the club's home is where live timing, the
            // running order and results are. Its address is the night
            // link's own, up to /night.
            text.Append("Open it once on your phone and Me, on the club's pages, brings you back to it from then on.").Append(nl).Append(nl);

            var at = link.IndexOf("/night#d=", StringComparison.Ordinal);

            if (at > 0)
            {
                text.Append("Live timing, the running order and results:").Append(nl);
                text.Append($"  {link[..at]}").Append(nl).Append(nl);
            }
        }

        text.Append("You're getting this because you asked for your night by email. To stop, ask at the desk.")
            .Append(nl).Append(nl);
        text.Append(club).Append(nl).Append(nl);

        // HeatCaller's one line at the foot (31 Oct 2026, the mark on
        // everything the app makes). Plain words, no link, so the email
        // still reads as the club's.
        text.Append(HeatCallerMark.EmailLine).Append(nl);

        return text.ToString();
    }

    /// <summary>A heading and its lines, then a blank line - nothing at all when there are no lines.</summary>
    private static void AppendBlock(StringBuilder text, string heading, IReadOnlyList<string>? lines, string nl)
    {
        var kept = (lines ?? Array.Empty<string>()).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()).ToList();

        if (kept.Count == 0)
        {
            return;
        }

        text.Append(heading).Append(nl);

        foreach (var line in kept)
        {
            text.Append(line).Append(nl);
        }

        text.Append(nl);
    }

    /// <summary>The first word of a name, for "Hi Sam,".</summary>
    internal static string FirstName(string? name)
    {
        var words = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return words.Length == 0 ? string.Empty : words[0];
    }
}

/// <summary>
/// How the Club Website page reaches the night emails MainForm sends
/// (22 Oct 2026, driver experience, item 11): what tonight's send would
/// be, for the question it asks first; the send itself, which records who
/// was emailed; and whether one is under way.
///
/// Set by MainForm.WireNightEmail on RelaySettingsControl.NightEmails,
/// because the page is built by MainForm.Pages and the night is MainForm's
/// to gather (tonight's races, the report cards, the badge key). Null in a
/// window that has no MainForm behind it, and the button then does nothing.
/// </summary>
internal sealed record NightEmailDesk(
    Func<NightEmail.Plan> Plan,
    Func<IProgress<string>?, CancellationToken, Task<MailResult>> SendAsync,
    Func<bool> IsSending);
