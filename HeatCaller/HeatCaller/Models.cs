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

    /// <summary>
    /// Laps this car has completed while leading, as RaceControlSession
    /// counted them at each crossing (RacerStanding.LapsLed). Zero from a
    /// source that does not count them. Read by the lead change call for
    /// "after six laps in front".
    /// </summary>
    public int LapsLed { get; init; }

    /// <summary>
    /// Penalty race control has given this car so far (jump start, penalty
    /// buttons, warning time) - RacerStanding.PenaltyMs, already inside
    /// ElapsedMs. Zero from a source that does not carry it.
    ///
    /// 20 Oct 2026: read by the provisional pole call under best-consecutive
    /// laps, which works from lap times and so never saw a penalty.
    /// </summary>
    public long PenaltyMs { get; init; }

    /// <summary>
    /// Every lap time the session holds for this car (RacerStanding.LapTimesMs,
    /// copied), or null from a source that does not carry them.
    ///
    /// WHY (8 Nov 2026): the announcer kept its own book of when each car
    /// crossed each lap, writing the elapsed time against the lap count on
    /// every update - corrections included. A penalty then moved only the
    /// current lap's crossing and a lap added by hand got a "crossing" at the
    /// same time as the lap before, while the board works every crossing out
    /// from the lap times (RaceGaps.CrossingMs): penalty applied to all of
    /// them, a hand-added lap left out. The voice said "two seconds back"
    /// under a board showing "-". With the lap times here the announcer
    /// works its crossings out by the board's own rule.
    /// </summary>
    public IReadOnlyList<long>? LapTimesMs { get; init; }

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

    /// <summary>
    /// The driver's main class - what their profile, the check-in sheet,
    /// badges, results headings and the announcer give as "their class".
    ///
    /// Kept as the one main answer, as CarModel is for cars, rather than
    /// replaced by the list below: about seventy places read it, and most of
    /// them want a single answer. The ones that ask "does this driver run
    /// class X?" - who goes in a class's heats, who a class's pages list -
    /// ask <see cref="RunsClass"/> instead, which counts the others too.
    /// </summary>
    public string ClassName { get; set; } = string.Empty;

    /// <summary>
    /// Any other classes this driver races, besides the main one.
    ///
    /// Someone running a stock car and a modified car is in two classes,
    /// and one class per profile left two bad choices: two profiles, which
    /// split their history and personal bests in two (the reason Cars is a
    /// list), or adding them to the other class's heats by hand every week.
    ///
    /// The main class is not repeated here - it is <see cref="ClassName"/>.
    /// Changing the main class therefore never drops these, and a class in
    /// both places is counted once (<see cref="AllClasses"/>).
    /// </summary>
    public List<string> Classes { get; set; } = new();

    /// <summary>
    /// Every class this driver races, the main one first. Older profiles
    /// have a ClassName and no list at all; this folds the two together, as
    /// AllCars does, so no caller has to.
    /// </summary>
    public IReadOnlyList<string> AllClasses
    {
        get
        {
            var all = new List<string>();

            if (!string.IsNullOrWhiteSpace(ClassName))
            {
                all.Add(ClassName.Trim());
            }

            foreach (var other in Classes ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(other)
                    && !all.Any(c => string.Equals(c, other.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    all.Add(other.Trim());
                }
            }

            return all;
        }
    }

    /// <summary>
    /// Whether this driver races <paramref name="className"/> - their main
    /// class or one of the others. By name, ignoring case and spaces at the
    /// ends, as every class comparison in the app is.
    /// </summary>
    public bool RunsClass(string? className) =>
        !string.IsNullOrWhiteSpace(className)
        && AllClasses.Any(c => string.Equals(c, className.Trim(), StringComparison.OrdinalIgnoreCase));
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
    ///
    /// It takes a SPELLING as readily as a nickname, and that was the half
    /// of it nobody knew was here: the voice reads what is written, so
    /// "Jacques" typed in here as "Zhock" comes out right while the driver
    /// stays Jacques on every heat sheet, badge and result. Nothing checks
    /// that what is in here is a real name, because a respelling is not one.
    ///
    /// Nothing used to be able to TRY a respelling either - a club typed a
    /// guess and found out whether it worked when the name went out over
    /// the PA. NameCheckForm reads these out on demand, and the Announcer
    /// name box on a driver's profile reads whatever is in it right now.
    /// </summary>
    public string AnnouncerName { get; set; } = string.Empty;

    /// <summary>
    /// The driver's pronouns - "he/him", "she/her", "they/them", or their
    /// own - set under the Announcer name on their profile (21 Oct 2026).
    ///
    /// Handed to the AI commentary with their name in every prompt that
    /// names them, so it stops guessing from the name (see
    /// CommentaryService.WithPronouns), and used by the few sentences the
    /// app writes about a driver itself (see Pronouns.For). Blank, as every
    /// profile saved before this date is, changes nothing.
    /// </summary>
    public string Pronouns { get; set; } = string.Empty;

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

    // --- Papers: the waiver and the membership (12 Nov 2026, setup plan D9) ---
    //
    // Recorded by hand at the desk, like money: HeatCaller never collects a
    // signature or checks a national body's records. Organiser use only, like
    // Contact - never published. See DeskPapers.

    /// <summary>When this driver last signed the club's waiver (the local date), or null for never.</summary>
    public DateTime? WaiverSignedOn { get; set; }

    /// <summary>Under 18: the waiver needs a parent or guardian, named in <see cref="WaiverGuardian"/>.</summary>
    public bool IsJunior { get; set; }

    /// <summary>Who signed a junior's waiver for them. Blank for an adult, or a junior's not signed yet.</summary>
    public string WaiverGuardian { get; set; } = string.Empty;

    /// <summary>Their membership number - the club's, or a national body's for its racing insurance. Blank for none.</summary>
    public string MembershipNumber { get; set; } = string.Empty;

    /// <summary>When that membership runs out (the local date), or null when it isn't known.</summary>
    public DateTime? MembershipExpires { get; set; }

    // --- New to racing (12 Nov 2026, setup plan D12) ---------------------

    /// <summary>
    /// New to racing: the desk shows it beside their name, and the announcer
    /// welcomes them once a night so the field gives them room. Ticked on
    /// Driver Hub; a driver first seen today counts as new without it. See
    /// NewDrivers.
    /// </summary>
    public bool IsNovice { get; set; }

    /// <summary>Who's looking after them tonight - shown at the desk. Blank for nobody.</summary>
    public string Mentor { get; set; } = string.Empty;

    // --- Online privacy and the driver's own words (22 Oct 2026) ---------
    //
    // WHY (22 Oct 2026, driver experience): a driver can ask from their own
    // phone to be kept off the internet in three ways. A phone can only
    // turn these ON; turning one off is a desk edit on the profile. None of
    // them changes anything in the room - the Wi-Fi pages, Big Screen and
    // PA are in the building and still use the full name and photo. They
    // are enforced for the relay by PublicPrivacy.

    /// <summary>Leave their photo out of everything that goes online (the check-in list).</summary>
    public bool NoPhotoOnline { get; set; }

    /// <summary>
    /// Don't put podium videos of them online. Since 28 Oct 2026 no podium
    /// video goes online at all (they stay on this PC), so this is always met;
    /// kept so a driver's ask and older profile files still round-trip.
    /// </summary>
    public bool NoVideoOnline { get; set; }

    /// <summary>Show them online by initials only ("Sam Parker" becomes "S.P.").</summary>
    public bool InitialsOnline { get; set; }

    /// <summary>
    /// They'd like their night emailed to them (to <see cref="Email"/>) when
    /// the club sends night emails. Off until they or the desk say so.
    /// </summary>
    public bool EmailMyNight { get; set; }

    /// <summary>
    /// A line in their own words for the AI commentary ("Racing since I
    /// was eight; my dad builds the cars"). At most
    /// <see cref="AboutLineMaxChars"/> characters. Never published online.
    /// </summary>
    public string AboutLine { get; set; } = string.Empty;

    /// <summary>Longest <see cref="AboutLine"/> the desk or a phone can set.</summary>
    public const int AboutLineMaxChars = 120;

    /// <summary>
    /// Every transponder this driver has besides the main one - a second
    /// car's, a replacement, a spare - and, when it has been given one, an
    /// entry for the main transponder too (to retire it, or to say which
    /// car it is fitted to).
    ///
    /// The main transponder (<see cref="Transponder"/>) stays the driver's
    /// key for good, even once it is retired: results, badges, check-ins,
    /// PINs and the rest are filed under it, and it is only an id - nothing
    /// needs a decoder to keep reporting it. A lap from any of these is
    /// filed under the main one (ProfileStore.PrimaryForAlternate), so a
    /// driver has one row, one best and one history whichever they carry.
    ///
    /// A retired transponder still belongs to the driver - its laps stay
    /// theirs, and one turning up again is still them - it is just marked
    /// as no longer in use.
    /// </summary>
    public List<OwnedTransponder> Transponders { get; set; } = new();

    /// <summary>
    /// The one extra transponder that older files, the phone's race-day
    /// page and the old "Alt transponder" box know about: the first extra
    /// one that is not retired, or empty.
    ///
    /// Still written to profiles.json, so a file opened by an older copy of
    /// the app keeps the driver's second car - and reading an older file
    /// turns its alt transponder into an entry in <see cref="Transponders"/>.
    ///
    /// Setting it only ever ADDS: the id becomes the driver's first extra
    /// transponder in use (added, or brought back into use and moved to the
    /// front), so the getter returns it. Nothing else is removed, and empty,
    /// or the main transponder, changes nothing. Code that means to replace
    /// or remove one works on the list - see RaceDayOpsEntries.
    /// </summary>
    public string AlternateTransponder
    {
        get => ExtraTransponders.FirstOrDefault(t => !t.Retired)?.Id ?? string.Empty;
        set
        {
            var id = value?.Trim() ?? string.Empty;

            if (id.Length == 0 || string.Equals(id, Transponder?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var list = Transponders ??= new List<OwnedTransponder>();

            if (string.Equals(AlternateTransponder, id, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var entry = TransponderEntry(id);

            if (entry is null)
            {
                entry = new OwnedTransponder { Id = id };
            }
            else
            {
                list.Remove(entry);
                entry.Retired = false;
                entry.RetiredUtc = null;
            }

            list.Insert(0, entry);
        }
    }

    /// <summary>The entries for transponders other than the main one, in the order they were added.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<OwnedTransponder> ExtraTransponders =>
        (Transponders ?? new List<OwnedTransponder>())
        .Where(t => t is not null
                    && !string.IsNullOrWhiteSpace(t.Id)
                    && !string.Equals(t.Id.Trim(), Transponder?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The main transponder and every extra one, retired ones included, each once.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> AllTransponderIds
    {
        get
        {
            var ids = new List<string>();

            if (!string.IsNullOrWhiteSpace(Transponder))
            {
                ids.Add(Transponder.Trim());
            }

            foreach (var entry in ExtraTransponders)
            {
                var id = entry.Id.Trim();

                if (!ids.Contains(id, StringComparer.OrdinalIgnoreCase))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }
    }

    /// <summary>The entry for this transponder (main or extra), or null - the main one has an entry only once it is retired or fitted to a car.</summary>
    public OwnedTransponder? TransponderEntry(string id)
    {
        var wanted = id?.Trim() ?? string.Empty;

        return wanted.Length == 0
            ? null
            : (Transponders ?? new List<OwnedTransponder>())
              .FirstOrDefault(t => t is not null && string.Equals(t.Id?.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether this transponder (main or extra) is marked as no longer in use.</summary>
    public bool IsRetired(string id) => TransponderEntry(id)?.Retired == true;

    /// <summary>
    /// The entry for this transponder, made when it has none. Only for one
    /// of this driver's own - the main one, or an extra already listed.
    /// </summary>
    public OwnedTransponder EntryToChange(string id)
    {
        var wanted = id.Trim();

        if (TransponderEntry(wanted) is { } entry)
        {
            return entry;
        }

        var made = new OwnedTransponder { Id = wanted };
        (Transponders ??= new List<OwnedTransponder>()).Add(made);
        return made;
    }

    /// <summary>
    /// The driver's transponders for the SPLIT loop - read by the decoder
    /// partway round the track (an I-Lap beside an EasyLap line, say),
    /// which can't read the line's transponders. Each is fitted to one of
    /// their cars (CarId), which is how a split pass finds the row it
    /// belongs to on the board.
    ///
    /// Kept apart from <see cref="Transponders"/> on purpose: a split
    /// transponder never counts a lap, never makes a row of its own and
    /// never answers ProfileStore.Get, and its number is only unique among
    /// split transponders - I-Lap 1234 and EasyLap 1234 are different
    /// transponders. See ProfileStore.SplitOwnerOf.
    /// </summary>
    public List<OwnedTransponder> SplitTransponders { get; set; } = new();

    /// <summary>The entry for this split transponder, or null.</summary>
    public OwnedTransponder? SplitEntry(string id)
    {
        var wanted = id?.Trim() ?? string.Empty;

        return wanted.Length == 0
            ? null
            : (SplitTransponders ?? new List<OwnedTransponder>())
              .FirstOrDefault(t => t is not null && string.Equals(t.Id?.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Marks a rental or loaner transponder, which shouldn't accrue personal bests.</summary>
    public bool IsRental { get; set; }

    /// <summary>
    /// Excluded from track records and season points - for guests, staff
    /// demo cars, or anyone running outside the competition.
    /// </summary>
    public bool ExcludeFromRecords { get; set; }

    /// <summary>
    /// A driver visiting from another club (7 Nov 2026, known issues) - set
    /// by the club, never inferred. The cold open's "seven of them visiting"
    /// counts these and nothing else: HomeTrack is free text and cannot be
    /// compared with a venue, and ExcludeFromRecords covers staff cars too.
    /// False on every profile saved before it existed, which says nothing.
    /// </summary>
    public bool Visitor { get; set; }

    /// <summary>
    /// File name of their photo inside the DriverPhotos folder, or empty
    /// for none. A name, not a path: the app-data folder can move between
    /// machines with a backup, and the profiles file should not care
    /// where a particular disk keeps it.
    /// </summary>
    public string PhotoFileName { get; set; } = string.Empty;

    /// <summary>
    /// Keep their photo off the Big Screen (1 Nov 2026, asked for by Asher:
    /// "on a driver by driver basis if it's a kid"). Their name still goes up
    /// on the driver introductions' card, with their initials where the photo
    /// would be - the name is already said over the PA and on the board, and
    /// the photo is the part a parent asks about. Set at the desk on the
    /// driver's profile. Online photos have their own switch, NoPhotoOnline.
    /// </summary>
    public bool NoPhotoOnBigScreen { get; set; }

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
    ///
    /// This one property is the whole of that decision, deliberately: it is
    /// what the voice queue reads when it fills {driver}
    /// (AnnouncementService.VoiceQueue.cs), and it is what the two places
    /// that LET A CLUB HEAR a name read as well - the Hear It beside the
    /// Announcer name box and NameCheckForm's roster. A check that worked
    /// out the name its own way would be a check that agrees with itself
    /// and disagrees with the PA.
    /// </summary>
    public string SpokenName =>
        !string.IsNullOrWhiteSpace(AnnouncerName) ? AnnouncerName
        : !string.IsNullOrWhiteSpace(DriverName) ? DriverName
        : Transponder;
}

/// <summary>
/// One transponder a driver owns - see DriverProfile.Transponders.
/// </summary>
public sealed class OwnedTransponder
{
    /// <summary>The id the decoder reports.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The Garage car it is fitted to (Car.Id), or empty when it moves
    /// between cars or nobody has said.
    /// </summary>
    public string CarId { get; set; } = string.Empty;

    /// <summary>No longer in use. Still the driver's: its laps stay theirs.</summary>
    public bool Retired { get; set; }

    /// <summary>When it was given to the driver, when that is known.</summary>
    public DateTime? AddedUtc { get; set; }

    /// <summary>When it was retired, when it is.</summary>
    public DateTime? RetiredUtc { get; set; }
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
