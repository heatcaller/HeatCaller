namespace HeatCaller;

/// <summary>
/// What the desk has answered to one thing a driver asked for from their
/// phone, as the phone is shown it (driverlive <c>asks</c>).
/// </summary>
/// <param name="Ref">The six upper-case hex characters the phone was given when it sent the ask (<see cref="DriverAsk.RefOf"/>).</param>
/// <param name="Kind">"details", "pack" or "query" - see <see cref="DriverAsk"/>.</param>
/// <param name="State">"waiting", "approved", "noted" or "declined" - see <see cref="DriverAsk"/>.</param>
/// <param name="Said">What the desk said back - a decline's reason, at most <see cref="DriverAsk.SaidMaxChars"/> characters. Empty for none.</param>
/// <param name="AnsweredUtc">When the desk answered it; null while it waits. Sent to the phone as "at" (11 Nov 2026), so the Me card shows a fresh answer and lets an old one go.</param>
/// <remarks>
/// 22 Oct 2026 (driver experience). The contract between the requests desk
/// (DriverRequests, package Q), which makes these, and the driver's live
/// document (DriverLive, package L), which carries them to the phone. There
/// is deliberately no answer route of its own: the phone already polls its
/// live entry.
/// </remarks>
public sealed record DriverAskAnswer(string Ref, string Kind, string State, string Said, DateTime? AnsweredUtc = null);

/// <summary>
/// The words and limits of a driver's own requests, shared by the laptop
/// and matching the relay's driverasks.ts (22 Oct 2026, driver experience).
/// Every name here is on the wire, so none of them may change.
/// </summary>
internal static class DriverAsk
{
    // --- Kinds ------------------------------------------------------------

    /// <summary>Their own details: announcer name, pronouns, about line, email, privacy.</summary>
    public const string Details = "details";

    /// <summary>Which of their battery packs they are running.</summary>
    public const string Pack = "pack";

    /// <summary>"Something wrong?" about one of tonight's results.</summary>
    public const string Query = "query";

    /// <summary>
    /// A setup change for the Garage's setup log (4 Nov 2026, from the relay
    /// roadmap): the sheet's five short parts and a note. Filed by itself -
    /// see DriverRequestDesk.FileSetup - since it changes nothing but adds a
    /// line to their own log.
    /// </summary>
    public const string Setup = "setup";

    /// <summary>
    /// One of their own battery packs added, edited, retired, back in use or
    /// removed (5 Nov 2026, Asher: "They should be able to add new battery
    /// packs and remove/retire old battery packs from the relay"). Applied by
    /// itself, as every kind below is - see DriverRequestDesk.FileWaitingWrites.
    /// </summary>
    public const string PackEdit = "packedit";

    /// <summary>One of their own cars added, edited, retired, raced tonight, or its photos changed (5 Nov 2026).</summary>
    public const string Car = "car";

    /// <summary>A car photo from their phone - collected through its own route, filed as a request so its answer reaches the phone (5 Nov 2026).</summary>
    public const string Photo = "photo";

    /// <summary>
    /// "I need 5 more minutes" before their next race (11 Nov 2026, race-night
    /// build plan, Phase 1). Waits for the desk; approved, the race goes back
    /// five minutes when it is the next one out - see RaceControlControl.GiveDriverMoreTime.
    /// </summary>
    public const string Late = "late";

    /// <summary>"My car's broken - skip me" in their next race (11 Nov 2026). Waits for the desk; approved, they come out of it - see RaceControlControl.SkipDriverInNextRace.</summary>
    public const string Skip = "skip";

    /// <summary>The kinds the laptop applies by itself, with no desk in between - the relay's DRIVER_WRITE_KINDS, and photos.</summary>
    public static bool IsWrite(string? kind) => kind is Pack or PackEdit or Car or Setup or Photo;

    // --- States -----------------------------------------------------------

    public const string Waiting = "waiting";
    public const string Approved = "approved";

    /// <summary>A query the desk has read and added to the race's notes. A result is never changed by a query.</summary>
    public const string Noted = "noted";

    public const string Declined = "declined";

    // --- How the relay checked who sent it ----------------------------------

    /// <summary>Signed in with their personal PIN.</summary>
    public const string ByPin = "pin";

    /// <summary>Only the badge code.</summary>
    public const string ByBadge = "badge";

    // --- Text limits (the relay enforces the same) --------------------------

    public const int AnnouncerMaxChars = 40;
    public const int PronounsMaxChars = 24;
    public const int AboutMaxChars = DriverProfile.AboutLineMaxChars;
    public const int EmailMaxChars = 120;
    public const int QueryMaxChars = 300;

    /// <summary>One part of a setup sheet from the phone (tires, gearing, springs, ride height, motor).</summary>
    public const int SetupPartMaxChars = 80;

    /// <summary>A setup change's note from the phone.</summary>
    public const int SetupNotesMaxChars = 300;

    // 5 Nov 2026, the garage from the phone - the relay's driverasks.ts.
    public const int PackLabelMaxChars = 40;
    public const int PackChemistryMaxChars = 16;
    public const int CarNameMaxChars = 60;
    public const int CarClassMaxChars = 40;
    public const int CarBodyMaxChars = 60;
    public const int CarNumberMaxChars = 8;
    public const int SheetValueMaxChars = 80;
    public const int SheetValuesMost = 80;

    /// <summary>The prefix a setup sheet value's field id comes under: "v.front-spring".</summary>
    public const string SheetValuePrefix = "v.";

    /// <summary>The longest reason the desk can send back with a decline.</summary>
    public const int SaidMaxChars = 120;

    /// <summary>
    /// The reference a phone is given for an ask: the first six hex
    /// characters of its relay id, in capitals ("A1B2C3"). Empty for an id
    /// too short to have one.
    /// </summary>
    public static string RefOf(string? askId)
    {
        var id = (askId ?? string.Empty).Trim();

        return id.Length < 6 || !id.Take(6).All(Uri.IsHexDigit)
            ? string.Empty
            : id[..6].ToUpperInvariant();
    }

    /// <summary>True for one of the four kinds, exactly as the relay spells them.</summary>
    public static bool IsKind(string? kind) => kind is Details or Pack or Query or Setup or PackEdit or Car or Photo or Late or Skip;

    /// <summary>True for one of the four states, exactly as the phone reads them.</summary>
    public static bool IsState(string? state) => state is Waiting or Approved or Noted or Declined;
}
