namespace HeatCaller;

/// <summary>
/// Whose crew is speaking (15 Nov 2026, driver crew plan): the driver's own
/// Team Principal and Technical Lead, when they have one and the club lets
/// drivers pick, or the club's. One place, so the interview before a race,
/// the one after it and the one written ahead all name and voice the same
/// person. The rules are DriverCrew's; this reads the stores they need.
/// On the UI thread, like every other reader on this page.
/// </summary>
public sealed partial class RaceControlControl
{
    /// <summary>One of a driver's crew, as the interview will have them.</summary>
    /// <param name="Name">What the announcer calls them; blank for the role alone.</param>
    /// <param name="Voice">The voice they speak in, or null for the announcer's own.</param>
    /// <param name="Why">How the driver's own pick was dealt with, for the console; empty when there was none.</param>
    internal sealed record CrewMember(string Name, SecondVoice? Voice, string Why);

    /// <summary>
    /// Whether a driver's picked voice is still on the engine's list
    /// (MainForm.CrewVoiceStillOffered). Null, every pick is taken as there.
    /// </summary>
    internal Func<CrewVoicePick, bool>? CrewVoiceStillThere { get; set; }

    /// <summary>
    /// The voices a driver may pick for their crew, for the Edit Driver
    /// dialog's voice drop-downs (19 Nov 2026, MainForm.CrewVoicesForDesk).
    /// Null, or a null answer, leaves them off.
    /// </summary>
    internal Func<IReadOnlyList<CrewVoiceOption>?>? CrewVoices { get; set; }

    /// <summary>The Team Principal (or, with <paramref name="principal"/> false, the Technical Lead) of the driver with this transponder.</summary>
    private CrewMember CrewOf(string? transponder, bool principal)
    {
        var profile = string.IsNullOrWhiteSpace(transponder) ? null : _profiles.Get(transponder);
        var allowed = _settings.DriversPickCrew;
        var clubsName = principal ? _settings.PrincipalName : _settings.RecapMechanicName;
        var own = !allowed ? null : principal ? profile?.CrewPrincipalName : profile?.CrewTechName;
        var engine = _announcer.VoiceEngineInUse;
        var hidden = new HashSet<string>(_settings.CrewVoicesHidden ?? new List<string>(), StringComparer.Ordinal);
        var reserved = DriverCrew.Reserved(_settings, _announcer.AnnouncerCrewKey(engine), engine);

        var (voice, why) = DriverCrew.VoiceFor(
            principal ? _settings.PrincipalVoice : _settings.MechanicVoice,
            principal ? profile?.CrewPrincipalVoice : profile?.CrewTechVoice,
            engine,
            allowed,
            hidden,
            reserved,
            CrewVoiceStillThere);

        // Said as spelled for the ear (19 Nov 2026): their name's "said as"
        // spelling when it has one, as a driver's announcer name is - the
        // name the model is given is the one the voice will read.
        var saidAs = !allowed ? null : principal ? profile?.CrewPrincipalSaidAs : profile?.CrewTechSaidAs;

        return new CrewMember(DriverCrew.SpokenNameFor(own, saidAs, clubsName), voice, why);
    }
}
