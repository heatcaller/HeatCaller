using System.Net.Mail;
using System.Security.Cryptography;

namespace HeatCaller;

/// <summary>A PIN that was emailed: whose, and which one - so a PIN replaced while the email was going out is not marked as sent.</summary>
internal sealed record PinMailed(string Transponder, string Pin);

/// <summary>
/// Every racer's personal check-in PIN: six digits that check in that
/// racer and nobody else on the club's online check-in page.
///
/// Static helpers over AppSettings and ProfileStore rather than a store of
/// their own, because the PINs are secrets and AppSettings is where the
/// secrets are sealed. Everything here runs on the UI thread, like every
/// other change to settings.
///
/// Only the relay ever sees a PIN, and only as a hash - see
/// RelayCheckIn.RacerPinHash.
/// </summary>
internal static class RacerPins
{
    public const int PinLength = 6;

    /// <summary>
    /// How long the track has to have been quiet before a set that has
    /// fallen due is replaced. Longer than any gap between heats of one
    /// meeting, shorter than the night between two. See RotateIfDue.
    /// </summary>
    public static readonly TimeSpan QuietBeforeNewSet = TimeSpan.FromHours(4);

    // --------------------------------------------------------------- email

    /// <summary>
    /// Where a racer's PIN can be emailed: the Email box when it holds a
    /// real address, else the Contact box when that happens to be one
    /// (older profiles only had Contact), else null.
    /// </summary>
    public static string? EmailFor(DriverProfile? profile)
    {
        if (profile is null)
        {
            return null;
        }

        return AsEmail(profile.Email) ?? AsEmail(profile.Contact);
    }

    private static string? AsEmail(string? text)
    {
        var value = (text ?? string.Empty).Trim();

        // TryCreate is lenient - it takes "Name <a@b.c>" and addresses with
        // no dot in the domain. Only a bare address that parses back to
        // itself counts; a Contact box saying "Dad: 07700 900123, a@b.c"
        // is somebody's note, not an address to send to.
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

    // ---------------------------------------------------------------- PINs

    /// <summary>
    /// Gives every racer without a PIN one, repairs any PIN that is no
    /// longer fit (a clash with another racer, the staff PIN, a Wi-Fi PIN
    /// or the manager PIN), and forgets the PINs of racers whose profile has gone.
    /// True when anything changed - the caller saves settings.
    /// </summary>
    public static bool EnsurePins(AppSettings settings, ProfileStore profiles)
    {
        // WHY (2026-11-05, Bug hunt 11: an unreadable profile store is not an
        // empty club): profiles.json held by a backup or sync tool at launch
        // loads as an empty store marked LoadedUnreadable. Every PIN then
        // looked like a deleted driver's and was dropped, and settings.json -
        // readable and writable - was saved without them; the next launch
        // gave every racer a new PIN and every one already handed out
        // stopped working. Nothing changes until the drivers can be read.
        if (profiles.LoadedUnreadable)
        {
            return false;
        }

        var changed = false;
        var current = Racers(profiles);
        var pins = settings.RacerPins;

        foreach (var gone in pins.Keys.Where(t => !current.Contains(t)).ToList())
        {
            // Kept aside for this session, so a driver whose delete is
            // undone gets their own PIN back - the one they may already
            // have been emailed - instead of a new one they don't know.
            Forget(settings, gone, pins[gone]);
            pins.Remove(gone);
            changed = true;
        }

        var reserved = ReservedPins(settings);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var needing = new List<string>();

        // Every PIN worth keeping is claimed first, so a new one handed out
        // below can never collide with - and so replace - somebody's
        // perfectly good PIN that just happened to be looked at later.
        foreach (var transponder in current)
        {
            if (pins.TryGetValue(transponder, out var pin)
                && IsFit(pin)
                && !reserved.Contains(pin)
                && taken.Add(pin))
            {
                continue;
            }

            needing.Add(transponder);
        }

        foreach (var transponder in needing)
        {
            // Back from a delete that was undone: their old PIN again, if
            // it is from the current set and still free and fit.
            if (!pins.ContainsKey(transponder)
                && TakeForgotten(settings, transponder) is { } back
                && IsFit(back.Pin)
                && !reserved.Contains(back.Pin)
                && taken.Add(back.Pin))
            {
                pins[transponder] = back.Pin;
                ForgetEmailed(settings, transponder);

                if (back.Emailed)
                {
                    settings.RacerPinsEmailed.Add(transponder);
                }

                changed = true;
                continue;
            }

            // Missing, or unfit. A repaired PIN is a new PIN, so the racer
            // has not been emailed THIS one.
            var fresh = NewPin(taken, reserved);
            pins[transponder] = fresh;
            taken.Add(fresh);
            ForgetEmailed(settings, transponder);
            changed = true;
        }

        var before = settings.RacerPinsEmailed.Count;
        settings.RacerPinsEmailed.RemoveAll(t => !pins.ContainsKey(t));
        changed |= settings.RacerPinsEmailed.Count != before;

        // A club that never rotates still wants "made on" in the status
        // line, so the first set is stamped when it is made.
        if (settings.RacerPinsIssuedUtc is null && pins.Count > 0)
        {
            settings.RacerPinsIssuedUtc = DateTime.UtcNow;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// A brand-new set for everyone when the rotation says one is due.
    /// True when it made one - the caller saves settings.
    ///
    /// Days are counted in local calendar days, not hours: "weekly" made at
    /// 7pm must not fall due at 7pm the next race night, halfway through
    /// the meeting, and send everybody a new PIN between heats. It changes
    /// the first time the app runs on the day it is due instead.
    ///
    /// Calendar days alone were not quite enough, though: a meeting still
    /// running at midnight is on the NEXT day, and "daily" then changed
    /// every PIN between two heats of the same evening. So a set that has
    /// fallen due also waits for the track to go quiet - no racing for
    /// QuietBeforeNewSet - before it is replaced. A laptop started fresh
    /// on race morning has had a night of quiet, and changes at once as
    /// it always did.
    /// </summary>
    /// <param name="lastRaceActivityUtc">
    /// When a race last ran or finished, or null when there has been none
    /// (or the caller does not know) - which lets a due set change at once.
    /// </param>
    public static bool RotateIfDue(AppSettings settings, ProfileStore profiles, DateTime nowUtc, DateTime? lastRaceActivityUtc = null)
    {
        // Bug hunt 11 (2026-11-05): never over an unreadable profile store -
        // a "new set" for nobody would cancel every racer's PIN. See EnsurePins.
        if (!settings.RelayRacerPinsEnabled || settings.RacerPinRotation == PinRotation.Never || profiles.LoadedUnreadable)
        {
            return false;
        }

        if (NextChangeLocal(settings) is { } due && nowUtc.ToLocalTime().Date < due)
        {
            return false;
        }

        // Due, but racing recently: the same meeting may still be going.
        // Held, not skipped - the first pass after the quiet makes it.
        if (lastRaceActivityUtc is { } lastRace && nowUtc - lastRace < QuietBeforeNewSet)
        {
            return false;
        }

        IssueNewSet(settings, profiles, nowUtc);
        return true;
    }

    // ------------------------------------------------------ Undo support

    /// <summary>
    /// PINs taken from racers whose profile went, this session only: the
    /// PIN, whether it had been emailed, and which set it belonged to. An
    /// undone delete brings the profile back, and EnsurePins then hands the
    /// driver this PIN rather than a new one. Only while the set it came
    /// from is still the current one - a PIN from a replaced set must stay
    /// dead. Capped, oldest first, since nothing that old can be undone.
    /// </summary>
    private static readonly List<(string Transponder, string Pin, bool Emailed, DateTime? Set)> Forgotten = new();

    private const int MaxForgotten = 200;

    /// <summary>
    /// Forget this driver (15 Nov 2026, privacy plan 2.5): their PINs gone
    /// for good - from the settings, from who has been emailed, and from
    /// the PINs kept aside for a deleted driver's return, so none comes
    /// back if the transponder is added again. Does not save.
    /// </summary>
    internal static void ForgetFor(AppSettings settings, IEnumerable<string> transponders)
    {
        foreach (var id in transponders)
        {
            settings.RacerPins.Remove(id);
            settings.RacerPinsEmailed?.RemoveAll(t => string.Equals(t, id, StringComparison.OrdinalIgnoreCase));
            Forgotten.RemoveAll(f => string.Equals(f.Transponder, id, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static void Forget(AppSettings settings, string transponder, string pin)
    {
        Forgotten.RemoveAll(f => string.Equals(f.Transponder, transponder, StringComparison.OrdinalIgnoreCase));
        Forgotten.Add((transponder, pin, IsEmailed(settings, transponder), settings.RacerPinsIssuedUtc));

        if (Forgotten.Count > MaxForgotten)
        {
            Forgotten.RemoveAt(0);
        }
    }

    private static (string Pin, bool Emailed)? TakeForgotten(AppSettings settings, string transponder)
    {
        var index = Forgotten.FindIndex(f => string.Equals(f.Transponder, transponder, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            return null;
        }

        var found = Forgotten[index];
        Forgotten.RemoveAt(index);

        return found.Set == settings.RacerPinsIssuedUtc ? (found.Pin, found.Emailed) : null;
    }

    /// <summary>Every racer's PIN as it stands, with the set's date and who has been emailed - taken before RegenerateAll, for its undo.</summary>
    public sealed record PinSet(Dictionary<string, string> Pins, DateTime? IssuedUtc, List<string> Emailed);

    public static PinSet CaptureSet(AppSettings settings) => new(
        new Dictionary<string, string>(settings.RacerPins, StringComparer.OrdinalIgnoreCase),
        settings.RacerPinsIssuedUtc,
        settings.RacerPinsEmailed.ToList());

    /// <summary>
    /// The undo half of RegenerateAll: the old PINs back, locally. Anything
    /// already sent can't be recalled - racers emailed their new PIN have a
    /// PIN that stops working again - and the caller has to save settings
    /// and republish the check-in list for the relay to take the old PINs.
    /// EnsurePins then tidies up: a driver added since gets a PIN, one
    /// deleted since loses theirs, and a PIN that clashes with a staff or
    /// Wi-Fi PIN set since is replaced.
    /// </summary>
    public static void RestoreSet(AppSettings settings, ProfileStore profiles, PinSet set)
    {
        settings.RacerPins = new Dictionary<string, string>(set.Pins, StringComparer.OrdinalIgnoreCase);
        settings.RacerPinsIssuedUtc = set.IssuedUtc;
        settings.RacerPinsEmailed = set.Emailed.ToList();
        EnsurePins(settings, profiles);
    }

    /// <summary>
    /// The set exactly as captured, and nothing more - for taking back PINs
    /// that could not be saved. Unlike RestoreSet it does not run EnsurePins
    /// afterwards, which would only hand out the same unsaved PINs again.
    /// </summary>
    public static void PutBackExactly(AppSettings settings, PinSet set)
    {
        settings.RacerPins = new Dictionary<string, string>(set.Pins, StringComparer.OrdinalIgnoreCase);
        settings.RacerPinsIssuedUtc = set.IssuedUtc;
        settings.RacerPinsEmailed = set.Emailed.ToList();
    }

    /// <summary>A brand-new set now, whatever the rotation says - the "New PINs now" button. Returns how many were made.</summary>
    public static int RegenerateAll(AppSettings settings, ProfileStore profiles)
    {
        // Bug hunt 11 (2026-11-05): refused over an unreadable profile store,
        // where it would make a set for nobody and cancel every PIN. The
        // "New PINs" button shows this as "Couldn't make new PINs: ...".
        if (profiles.LoadedUnreadable)
        {
            throw new InvalidOperationException("the drivers file (profiles.json) couldn't be read when HeatCaller started. "
                + "Close anything that might have it open (a backup or sync tool is the usual cause) and restart HeatCaller.");
        }

        return IssueNewSet(settings, profiles, DateTime.UtcNow);
    }

    private static int IssueNewSet(AppSettings settings, ProfileStore profiles, DateTime nowUtc)
    {
        var reserved = ReservedPins(settings);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var fresh = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var transponder in Racers(profiles))
        {
            // Not the racer's own old PIN either: "new PINs" that leave
            // somebody's unchanged would leave the one that leaked working.
            var old = settings.RacerPins.TryGetValue(transponder, out var previous) ? previous : null;
            string pin;

            do
            {
                pin = NewPin(taken, reserved);
            }
            while (pin == old);

            fresh[transponder] = pin;
            taken.Add(pin);
        }

        settings.RacerPins = fresh;
        settings.RacerPinsIssuedUtc = nowUtc;
        settings.RacerPinsEmailed.Clear();

        return fresh.Count;
    }

    /// <summary>The local date the current set is replaced on, or null when it never is (or there is no set yet).</summary>
    public static DateTime? NextChangeLocal(AppSettings settings)
    {
        if (settings.RacerPinsIssuedUtc is not { } issued)
        {
            return null;
        }

        var issuedDay = issued.ToLocalTime().Date;

        return settings.RacerPinRotation switch
        {
            PinRotation.Daily => issuedDay.AddDays(1),
            PinRotation.Weekly => issuedDay.AddDays(7),
            PinRotation.Monthly => issuedDay.AddDays(30),
            _ => null,
        };
    }

    /// <summary>The last local date the current PINs are certain to work, for the email. Null when they do not expire.</summary>
    public static DateTime? ValidUntil(AppSettings settings) =>
        settings.RelayRacerPinsEnabled && NextChangeLocal(settings) is { } next ? next.AddDays(-1) : null;

    /// <summary>
    /// Marks PINs as emailed - only where the racer's PIN is still the one
    /// that was sent. Returns how many were marked; the caller saves.
    /// </summary>
    public static int MarkEmailed(AppSettings settings, IEnumerable<PinMailed> sent)
    {
        var marked = 0;

        foreach (var item in sent)
        {
            if (settings.RacerPins.TryGetValue(item.Transponder, out var pin)
                && pin == item.Pin
                && !settings.RacerPinsEmailed.Contains(item.Transponder, StringComparer.OrdinalIgnoreCase))
            {
                settings.RacerPinsEmailed.Add(item.Transponder);
                marked++;
            }
        }

        return marked;
    }

    public static bool IsEmailed(AppSettings settings, string transponder) =>
        settings.RacerPinsEmailed.Contains(transponder, StringComparer.OrdinalIgnoreCase);

    /// <summary>Racers who have a PIN and an address but have not been emailed their current PIN.</summary>
    public static List<string> AwaitingEmail(AppSettings settings, ProfileStore profiles) =>
        profiles.All
            .Where(p => settings.RacerPins.ContainsKey(p.Transponder)
                        && !IsEmailed(settings, p.Transponder)
                        && EmailFor(p) is not null)
            .Select(p => p.Transponder)
            .ToList();

    /// <summary>
    /// Whether a PIN is one worth handing out: six ASCII digits, and not
    /// one of the handful people guess first - 000000, 123456, 654321,
    /// 121212, 123123 and their kind.
    /// </summary>
    public static bool IsFit(string? pin)
    {
        if (pin is not { Length: PinLength } || !pin.All(char.IsAsciiDigit))
        {
            return false;
        }

        // All one digit.
        if (pin.All(c => c == pin[0]))
        {
            return false;
        }

        // A run up or down, wrapping past 9 or 0 as well: 123456, 654321,
        // 789012, 210987.
        var up = true;
        var down = true;

        for (var i = 1; i < pin.Length; i++)
        {
            var step = (pin[i] - pin[i - 1] + 10) % 10;
            up &= step == 1;
            down &= step == 9;
        }

        if (up || down)
        {
            return false;
        }

        // A pair or a triple said again: 121212, 123123.
        if (pin[..2] == pin[2..4] && pin[2..4] == pin[4..])
        {
            return false;
        }

        return pin[..3] != pin[3..];
    }

    private static string NewPin(HashSet<string> taken, HashSet<string> reserved)
    {
        // A million numbers against a club's hundred racers: a clash is
        // rare and a second draw ends it. The cap is only there so a bug
        // could never become a hang.
        for (var attempt = 0; attempt < 100_000; attempt++)
        {
            var pin = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

            if (IsFit(pin) && !taken.Contains(pin) && !reserved.Contains(pin))
            {
                return pin;
            }
        }

        throw new InvalidOperationException("Could not find an unused check-in PIN.");
    }

    /// <summary>
    /// PINs a racer may never be given: the staff PIN, which opens the whole
    /// list, the two Wi-Fi PINs, one of which throws flags, and the race
    /// manager's PIN (six digits or more, so a six-digit one can clash). A
    /// racer whose own PIN happened to be one of those would hold far more
    /// than their own check-in. EnsurePins treats a PIN that clashes with
    /// any of these as unfit and replaces it, so one handed out before the
    /// manager PIN was set (or before it was on this list) is repaired too.
    /// </summary>
    private static HashSet<string> ReservedPins(AppSettings settings) =>
        new[] { settings.RelayCheckInPin, settings.WebServerPin, settings.WebServerCheckInPin, settings.ManagerPin }
            .Select(p => (p ?? string.Empty).Trim())
            .Where(p => p.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Whether some racer already has this as their personal PIN. A new
    /// staff, Wi-Fi or manager PIN is refused when it is (RelaySettingsControl,
    /// WebServerControl.SavePins). EnsurePins then used to give the racer a
    /// new PIN, but the old one may already have been emailed - and typed at
    /// the check-in page it opened the whole list, the manager or the remote.
    /// </summary>
    internal static bool IsHeldByRacer(AppSettings settings, string pin)
    {
        var wanted = (pin ?? string.Empty).Trim();

        return wanted.Length > 0 && settings.RacerPins.Values.Contains(wanted, StringComparer.Ordinal);
    }

    /// <summary>Every racer who gets a PIN: every real driver (All leaves the ghosts out), by transponder.</summary>
    private static HashSet<string> Racers(ProfileStore profiles) =>
        profiles.All
            .Select(p => p.Transponder)
            .Where(t => !string.IsNullOrWhiteSpace(t) && !GhostService.IsGhost(t))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static void ForgetEmailed(AppSettings settings, string transponder) =>
        settings.RacerPinsEmailed.RemoveAll(t => string.Equals(t, transponder, StringComparison.OrdinalIgnoreCase));
}
