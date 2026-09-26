namespace HeatCaller;

/// <summary>
/// A single racer's current standing, from a Neon Timing Protocol
/// {"cmd":"event","evt":"race","type":"standing",...} message.
/// Field names below match the wire format observed from Next Level
/// Timing 4.9.1 (see events/race.md in the neon-timing-protocol repo for
/// the general shape; exact field presence can vary by event source).
/// </summary>
public sealed class RaceStanding
{
    public required string Transponder { get; init; }
    public string? Name { get; init; }
    public int Laps { get; init; }
    public long FastLapMs { get; init; }
    public long ElapsedMs { get; init; }
    public string? Status { get; init; }
    public string? Id { get; init; }

    /// <summary>Driver-facing label: prefer Name, fall back to the transponder id.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Transponder : Name!;
}

/// <summary>
/// A user-saved profile for a transponder: who's driving it, what car/model
/// it is, its setup configuration, and free-form notes. Persisted locally
/// and looked up by transponder id whenever we render race data, so a
/// racer's saved name/car shows up automatically once their transponder
/// starts sending events - no manual matching required during a session.
/// </summary>
public sealed class DriverProfile
{
    public required string Transponder { get; set; }
    public string DriverName { get; set; } = string.Empty;
    /// <summary>
    /// The car currently being run - what announcements, results and
    /// records use.
    ///
    /// Deliberately kept as the "active" car rather than replaced by the
    /// list below: 34 places across the app read this, and every one of
    /// them wants a single answer to "what is this driver in right now".
    /// The list adds the others without disturbing any of that.
    /// </summary>
    public string CarModel { get; set; } = string.Empty;

    /// <summary>
    /// Every car this driver runs, the active one included.
    ///
    /// Someone with a stock car and a modified car shouldn't need two
    /// profiles - that splits their lap history and personal bests in two.
    /// </summary>
    public List<string> Cars { get; set; } = new();

    /// <summary>
    /// The full garage, guaranteed to include the active car.
    ///
    /// Older profiles have a CarModel and no list at all, so this folds the
    /// two together rather than making every caller handle the migration.
    /// </summary>
    public IReadOnlyList<string> AllCars
    {
        get
        {
            var all = Cars
                .Where(car => !string.IsNullOrWhiteSpace(car))
                .Select(car => car.Trim())
                .ToList();

            if (!string.IsNullOrWhiteSpace(CarModel)
                && !all.Any(car => string.Equals(car, CarModel.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                all.Insert(0, CarModel.Trim());
            }

            return all;
        }
    }

    /// <summary>
    /// Switches the active car, adding it to the garage if it's new.
    /// Returns false if the name is blank.
    /// </summary>
    public bool SetActiveCar(string car)
    {
        if (string.IsNullOrWhiteSpace(car))
        {
            return false;
        }

        var trimmed = car.Trim();

        if (!Cars.Any(c => string.Equals(c, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            Cars.Add(trimmed);
        }

        CarModel = trimmed;
        return true;
    }
    public string ClassName { get; set; } = string.Empty;
    public string SetupConfiguration { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    // --- Added fields -----------------------------------------------------
    // Chosen for what the app can USE, not just record: each of these feeds
    // an announcement, a report or a piece of race-day admin. A field
    // nothing reads is just a form to fill in.

    /// <summary>
    /// What the announcer calls them, when it differs from their real name.
    ///
    /// Nicknames are what a crowd responds to, and some names the voice
    /// simply mispronounces - this is the fix for both without renaming
    /// the driver everywhere else.
    /// </summary>
    public string AnnouncerName { get; set; } = string.Empty;

    /// <summary>Racing number, for callouts and results sheets.</summary>
    public string RaceNumber { get; set; } = string.Empty;

    /// <summary>Home club or town - useful colour for intros at an open meeting.</summary>
    public string HomeTrack { get; set; } = string.Empty;

    /// <summary>
    /// Skill bracket, for seeding heats and for handicap decisions.
    /// Free text rather than an enum so clubs can use their own words.
    /// </summary>
    public string SkillLevel { get; set; } = string.Empty;

    /// <summary>Contact for the organiser - never announced, never exported to public results.</summary>
    public string Contact { get; set; } = string.Empty;

    /// <summary>
    /// Where their personal check-in PIN is emailed. Organiser use only,
    /// like Contact: never announced, never published to the relay or the
    /// Wi-Fi pages, never in a results export. See RacerPins.EmailFor.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Transponder they also run, so a second car doesn't look like a stranger.</summary>
    public string AlternateTransponder { get; set; } = string.Empty;

    /// <summary>Marks a rental or loaner transponder, which shouldn't accrue personal bests.</summary>
    public bool IsRental { get; set; }

    /// <summary>
    /// Excluded from track records and season points - for guests, staff
    /// demo cars, or anyone running outside the competition.
    /// </summary>
    public bool ExcludeFromRecords { get; set; }

    /// <summary>
    /// File name of their photo inside the DriverPhotos folder, or empty
    /// for none. A name, not a path: the app-data folder can move between
    /// machines with a backup, and the profiles file should not care
    /// where a particular disk keeps it.
    /// </summary>
    public string PhotoFileName { get; set; } = string.Empty;

    /// <summary>
    /// Who backs them - "Kyosho, PN Racing, Dad". Shown on the driver
    /// card, ready for the announcer's intros: the field that makes a
    /// club racer feel like a works driver, which is its entire job.
    /// </summary>
    public string Sponsors { get; set; } = string.Empty;

    /// <summary>Team or club they run under, for intros and results colour.</summary>
    public string Team { get; set; } = string.Empty;

    /// <summary>When they were added, for "new this season" reporting.</summary>
    public DateTime? FirstSeenUtc { get; set; }

    /// <summary>
    /// The name the announcer should use: the announcer name when set,
    /// otherwise the driver name, otherwise the transponder.
    /// </summary>
    public string SpokenName =>
        !string.IsNullOrWhiteSpace(AnnouncerName) ? AnnouncerName
        : !string.IsNullOrWhiteSpace(DriverName) ? DriverName
        : Transponder;
}

/// <summary>
/// A gate/lap pass, from a
/// {"cmd":"event","evt":"race","type":"racer_passed_gate",...} message.
/// </summary>
public sealed class GatePassEvent
{
    public required string Transponder { get; init; }
    public string? Gate { get; init; }
    public string? GateType { get; init; }
    /// <summary>Fastest lap of the race so far.</summary>
    public bool Fast { get; init; }
    public bool Streak { get; init; }
    public bool Valid { get; init; }

    /// <summary>
    /// When set, this is the AUTHORITATIVE current streak count for this
    /// transponder, and AnnouncementService uses it directly instead of
    /// deriving its own count from the Streak flag above. This exists
    /// because the Neon Timing API only gives a bare "is this pass part of
    /// a streak" boolean (so AnnouncementService has to count for itself),
    /// but the direct EasyLap decoder path computes its own, more
    /// sophisticated streak logic (including breaking on unusually slow
    /// laps) and needs the announcer to stay in sync with that, not run a
    /// second, simpler count in parallel.
    /// </summary>
    public int? StreakCountOverride { get; init; }

    /// <summary>
    /// The exact duration of the lap this pass completed, when the source
    /// knows it (the direct decoder path does). Null for the first pass of
    /// a race - green to line is not a lap - and for sources that don't
    /// supply one, in which case AnnouncementService derives it itself.
    /// </summary>
    public long? LapTimeMs { get; init; }
}
