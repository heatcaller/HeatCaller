namespace HeatCaller;

/// <summary>
/// What the desk has answered to one thing a driver asked for from their
/// phone, as the phone is shown it (driverlive <c>asks</c>).
/// </summary>
/// <param name="Ref">The six upper-case hex characters the phone was given when it sent the ask (<see cref="DriverAsk.RefOf"/>).</param>
/// <param name="Kind">"details", "pack" or "query" - see <see cref="DriverAsk"/>.</param>
/// <param name="State">"waiting", "approved", "noted" or "declined" - see <see cref="DriverAsk"/>.</param>
/// <param name="Said">What the desk said back - a decline's reason, at most <see cref="DriverAsk.SaidMaxChars"/> characters. Empty for none.</param>
/// <remarks>
/// 22 Oct 2026 (driver experience). The contract between the requests desk
/// (DriverRequests, package Q), which makes these, and the driver's live
/// document (DriverLive, package L), which carries them to the phone. There
/// is deliberately no answer route of its own: the phone already polls its
/// live entry.
/// </remarks>
public sealed record DriverAskAnswer(string Ref, string Kind, string State, string Said);

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
    public static bool IsKind(string? kind) => kind is Details or Pack or Query or Setup;

    /// <summary>True for one of the four states, exactly as the phone reads them.</summary>
    public static bool IsState(string? state) => state is Waiting or Approved or Noted or Declined;
}
