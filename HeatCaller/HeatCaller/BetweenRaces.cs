using System.Globalization;

namespace HeatCaller;

/// <summary>How much the announcer says in the gap between races - see <see cref="GapRules.Level"/>.</summary>
internal enum GapLevel
{
    /// <summary>Call-ups, marshals and the countdown. Nothing else.</summary>
    Quiet,

    /// <summary>Quiet, plus the hand-over and the club's own notices.</summary>
    Light,

    /// <summary>Everything the boxes allow - what the gap always had, plus call-ups.</summary>
    Normal,

    /// <summary>Normal with more room: two more lines a gap and sponsor reads twice as often.</summary>
    Busy,
}

/// <summary>How long a model-written line in the gap may run - see <see cref="GapRules.Length"/>.</summary>
internal enum GapLineLength
{
    Short,
    Medium,
    Long,
}

/// <summary>Every kind of line the gap between races can carry, each with its own tick box.</summary>
internal enum GapLine
{
    /// <summary>"Heat 3 for Box Stock, you're up next. Head to the drivers stand."</summary>
    CallUp,

    /// <summary>"Heat 4 drivers, you're two races away. Get your cars ready."</summary>
    TwoOut,

    /// <summary>"Marshals for Heat 3: Sam on Turn 1, Bill on Turn 2."</summary>
    Marshals,

    /// <summary>"Still waiting on Frank for Heat 3. Frank, check in at the desk please."</summary>
    NotCheckedIn,

    /// <summary>"That was Heat 2. Still to come, Heat 3." - the director's free hand-over.</summary>
    HandOver,

    /// <summary>The model-written line about who is in the next race.</summary>
    ComingUpNext,

    /// <summary>The rest of the Through the night segments: form guide, qualifying report and so on.</summary>
    Segment,

    /// <summary>A sponsor's paid read on rotation between races.</summary>
    Sponsor,

    /// <summary>Tonight's club notes: dedications, the raffle, birthdays.</summary>
    ClubNote,

    /// <summary>"5 minutes to Heat 3." - the run sheet's countdown.</summary>
    Countdown,

    /// <summary>One of the club's own lines with no race in it: "Pit lane is closed while the track is reset."</summary>
    Notice,

    /// <summary>"Racing resumes at 7:45, in about 20 minutes." - once, at the top of a long break.</summary>
    LongBreak,
}

/// <summary>
/// THE GAP BETWEEN RACES, AS A CLUB SETS IT (31 Oct 2026, asked for by
/// Asher: "plan on adding more control to the reminder calls that come after
/// the show recap and before the next heat/main. there should be options to
/// control how much it talk, how frequently, when it does, what it says").
///
/// Everything on Voice → AI Commentary → Between races, read once into one
/// value so the Race Control caller, the Announcer screen's director and the
/// Hear the gap preview all judge a line by the same numbers. Built from
/// AppSettings with <see cref="From"/>, written back with <see cref="SaveTo"/>,
/// and changed whole by a <see cref="GapPreset"/>.
///
/// Before this the gap was decided by numbers fixed in code - twenty seconds
/// before a Coming Up, ninety between sponsor reads, one hand-over a gap -
/// and with Start races On Air ticked (the default) it was silent from the
/// recap's sign-off until On Air, however long that was.
/// </summary>
internal sealed record GapRules
{
    /// <summary>Most lines in one gap, at the least and the most the card allows.</summary>
    public const int FewestLines = 1, MostLinesAllowed = 10;

    /// <summary>The space between lines, in seconds, at the least and most.</summary>
    public const int ShortestSpace = 10, LongestSpace = 120;

    /// <summary>The call-up repeat, in seconds, at the least and most - 0 is Never.</summary>
    public const int ShortestRepeat = 30, LongestRepeat = 300;

    /// <summary>Call-ups per race, at the least and most.</summary>
    public const int FewestCallUps = 1, MostCallUps = 6;

    /// <summary>The quiet after the recap and before the call, in seconds, at most.</summary>
    public const int LongestQuiet = 60;

    /// <summary>
    /// How close to its time the next race has to be before call-ups start
    /// again in a long break - the break was announced, so the room is told
    /// once and then left alone until racing is near.
    /// </summary>
    public static readonly TimeSpan LongBreakCallUpsFrom = TimeSpan.FromMinutes(5);

    /// <summary>The marks the run sheet countdown can be told to call, in minutes.</summary>
    public static readonly IReadOnlyList<int> AllMarks = new[] { 10, 5, 2, 1 };

    public GapLevel Level { get; init; } = GapLevel.Normal;
    public GapLineLength Length { get; init; } = GapLineLength.Medium;
    public int MostLines { get; init; } = 4;
    public int SpaceSeconds { get; init; } = 30;

    /// <summary>Seconds between repeats of the next race's call-up; 0 is Never (said once).</summary>
    public int CallUpRepeatSeconds { get; init; } = 120;

    public int CallUpsPerRace { get; init; } = 3;
    public int SponsorEveryMinutes { get; init; } = 2;
    public int QuietAfterRecapSeconds { get; init; } = 10;
    public int QuietBeforeCallSeconds { get; init; } = 20;

    /// <summary>Call-ups, marshals and the club's notices still go out when On Air will open the next race.</summary>
    public bool CallUpsWithOnAir { get; init; } = true;

    public bool LongBreakNotice { get; init; } = true;
    public int LongBreakMinutes { get; init; } = 10;

    /// <summary>The countdown calls its marks by itself from the run sheet, without anyone pressing Start.</summary>
    public bool AutoCountdown { get; init; }

    /// <summary>Which marks the automatic countdown calls, in minutes, largest first.</summary>
    public IReadOnlyList<int> CountdownMarks { get; init; } = new[] { 5, 1 };

    public bool CountdownTimesUp { get; init; }

    public bool CallUps { get; init; } = true;
    public bool TwoOut { get; init; }
    public bool Marshals { get; init; } = true;
    public bool NotCheckedIn { get; init; }
    public bool HandOver { get; init; } = true;
    public bool ComingUpNext { get; init; } = true;
    public bool Segments { get; init; } = true;
    public bool ClubNotes { get; init; } = true;
    public bool Countdown { get; init; } = true;

    /// <summary>The club's own lines - see <see cref="GapWords.Sort"/> for which become what.</summary>
    public IReadOnlyList<string> CustomLines { get; init; } = Array.Empty<string>();

    /// <summary>The defaults: what a club that never opens the card gets - the Race day preset.</summary>
    public static GapRules Default { get; } = new();

    /// <summary>Most lines in a gap with the level taken into account: Busy has two more.</summary>
    public int LinesAllowed => Math.Clamp(Level == GapLevel.Busy ? MostLines + 2 : MostLines, FewestLines, MostLinesAllowed + 2);

    /// <summary>Least time between sponsor reads in the gap: Busy halves it, to no less than a minute.</summary>
    public TimeSpan SponsorSpacing =>
        TimeSpan.FromSeconds(Math.Max(60, SponsorEveryMinutes * 60 / (Level == GapLevel.Busy ? 2 : 1)));

    /// <summary>
    /// Whether this kind of line may be said in the gap at all - its box and
    /// the level together. Quiet is the call-ups and the countdown; Light adds
    /// the hand-over and the club's notices; Normal and Busy are whatever is
    /// ticked. Sponsor reads have no box here: the Sponsors page decides
    /// whether there are any, and this only says whether the level has room.
    /// </summary>
    public bool Allows(GapLine kind)
    {
        var ticked = kind switch
        {
            GapLine.CallUp => CallUps,
            GapLine.TwoOut => TwoOut,
            GapLine.Marshals => Marshals,
            GapLine.NotCheckedIn => NotCheckedIn,
            GapLine.HandOver => HandOver,
            GapLine.ComingUpNext => ComingUpNext,
            GapLine.Segment => Segments,
            GapLine.Sponsor => true,
            GapLine.ClubNote => ClubNotes,
            GapLine.Countdown => Countdown,
            GapLine.Notice => CustomLines.Count > 0,
            GapLine.LongBreak => LongBreakNotice,
            _ => false,
        };

        if (!ticked)
        {
            return false;
        }

        return Level switch
        {
            GapLevel.Quiet => IsACall(kind),
            GapLevel.Light => IsACall(kind) || kind is GapLine.HandOver or GapLine.Notice,
            _ => true,
        };
    }

    /// <summary>
    /// The lines that are instructions rather than talk - call-ups, marshals,
    /// the countdown and the long-break notice. Kept at every level, the same
    /// rule How chatty follows for the race's own instructions.
    /// </summary>
    public static bool IsACall(GapLine kind) =>
        kind is GapLine.CallUp or GapLine.TwoOut or GapLine.Marshals or GapLine.NotCheckedIn
            or GapLine.Countdown or GapLine.LongBreak;

    /// <summary>Lines the director writes or picks (not Race Control's calls) - they stand down when On Air has the gap.</summary>
    public static bool IsTheDirectors(GapLine kind) =>
        kind is GapLine.HandOver or GapLine.ComingUpNext or GapLine.Segment or GapLine.Sponsor or GapLine.ClubNote;

    /// <summary>The settings as they stand, with the race day's own preset over them when it has one.</summary>
    public static GapRules From(AppSettings settings, string? dayPreset = null)
    {
        var rules = new GapRules
        {
            Level = LevelFrom(settings.BetweenRacesLevel),
            Length = LengthFrom(settings.BetweenRacesLineLength),
            MostLines = Math.Clamp(settings.BetweenRacesMostLines, FewestLines, MostLinesAllowed),
            SpaceSeconds = Math.Clamp(settings.BetweenRacesSpaceSeconds, ShortestSpace, LongestSpace),
            CallUpRepeatSeconds = settings.BetweenRacesCallUpRepeatSeconds <= 0
                ? 0
                : Math.Clamp(settings.BetweenRacesCallUpRepeatSeconds, ShortestRepeat, LongestRepeat),
            CallUpsPerRace = Math.Clamp(settings.BetweenRacesCallUpsPerRace, FewestCallUps, MostCallUps),
            SponsorEveryMinutes = Math.Clamp(settings.BetweenRacesSponsorMinutes, 1, 10),
            QuietAfterRecapSeconds = Math.Clamp(settings.BetweenRacesQuietAfterRecapSeconds, 0, LongestQuiet),
            QuietBeforeCallSeconds = Math.Clamp(settings.BetweenRacesQuietBeforeCallSeconds, 0, LongestQuiet),
            CallUpsWithOnAir = settings.BetweenRacesCallUpsWithOnAir,
            LongBreakNotice = settings.BetweenRacesLongBreakNotice,
            LongBreakMinutes = Math.Clamp(settings.BetweenRacesLongBreakMinutes, 5, 60),
            AutoCountdown = settings.BetweenRacesAutoCountdown,
            CountdownMarks = MarksFrom(settings.BetweenRacesCountdownMarks),
            CountdownTimesUp = settings.BetweenRacesCountdownTimesUp,
            CallUps = settings.BetweenRacesCallUps,
            TwoOut = settings.BetweenRacesTwoOut,
            Marshals = settings.BetweenRacesMarshals,
            NotCheckedIn = settings.BetweenRacesNotCheckedIn,
            HandOver = settings.BetweenRacesHandOver,
            ComingUpNext = settings.BetweenRacesComingUpNext,
            Segments = settings.BetweenRacesSegments,
            ClubNotes = settings.BetweenRacesClubNotes,
            Countdown = settings.BetweenRacesCountdown,
            CustomLines = LinesFrom(settings.BetweenRacesCustomLines),
        };

        return GapPreset.Find(dayPreset) is { } forTheDay ? forTheDay.Over(rules) : rules;
    }

    /// <summary>Writes these rules back to the settings - the card's own save, and a preset picked on it.</summary>
    public void SaveTo(AppSettings settings)
    {
        settings.BetweenRacesLevel = Level.ToString().ToLowerInvariant();
        settings.BetweenRacesLineLength = Length.ToString().ToLowerInvariant();
        settings.BetweenRacesMostLines = MostLines;
        settings.BetweenRacesSpaceSeconds = SpaceSeconds;
        settings.BetweenRacesCallUpRepeatSeconds = CallUpRepeatSeconds;
        settings.BetweenRacesCallUpsPerRace = CallUpsPerRace;
        settings.BetweenRacesSponsorMinutes = SponsorEveryMinutes;
        settings.BetweenRacesQuietAfterRecapSeconds = QuietAfterRecapSeconds;
        settings.BetweenRacesQuietBeforeCallSeconds = QuietBeforeCallSeconds;
        settings.BetweenRacesCallUpsWithOnAir = CallUpsWithOnAir;
        settings.BetweenRacesLongBreakNotice = LongBreakNotice;
        settings.BetweenRacesLongBreakMinutes = LongBreakMinutes;
        settings.BetweenRacesAutoCountdown = AutoCountdown;
        settings.BetweenRacesCountdownMarks = string.Join(",", CountdownMarks.Select(m => m.ToString(CultureInfo.InvariantCulture)));
        settings.BetweenRacesCountdownTimesUp = CountdownTimesUp;
        settings.BetweenRacesCallUps = CallUps;
        settings.BetweenRacesTwoOut = TwoOut;
        settings.BetweenRacesMarshals = Marshals;
        settings.BetweenRacesNotCheckedIn = NotCheckedIn;
        settings.BetweenRacesHandOver = HandOver;
        settings.BetweenRacesComingUpNext = ComingUpNext;
        settings.BetweenRacesSegments = Segments;
        settings.BetweenRacesClubNotes = ClubNotes;
        settings.BetweenRacesCountdown = Countdown;
        settings.BetweenRacesCustomLines = string.Join("\n", CustomLines);
    }

    internal static GapLevel LevelFrom(string? key) =>
        Enum.TryParse<GapLevel>(key, ignoreCase: true, out var level) ? level : GapLevel.Normal;

    internal static GapLineLength LengthFrom(string? key) =>
        Enum.TryParse<GapLineLength>(key, ignoreCase: true, out var length) ? length : GapLineLength.Medium;

    /// <summary>"5,1" as marks, largest first; anything not on the card's list is dropped. Blank is none.</summary>
    internal static IReadOnlyList<int> MarksFrom(string? stored) =>
        (stored ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var m) ? m : -1)
            .Where(AllMarks.Contains)
            .Distinct()
            .OrderByDescending(m => m)
            .ToList();

    /// <summary>The custom lines box, one line each, blanks and stray spaces dropped.</summary>
    internal static IReadOnlyList<string> LinesFrom(string? stored) =>
        (stored ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0)
            .Take(30)
            .ToList();

    /// <summary>
    /// What the model is told about length for a line between races - one
    /// short sentence for Short, room for three or four for Long, nothing for
    /// Medium, which is the length the line has always been written at.
    /// </summary>
    public string LengthBrief => Length switch
    {
        GapLineLength.Short => " Between races, keep it to ONE short sentence.",
        GapLineLength.Long => " Between races there is time: you may run to three or four sentences.",
        _ => string.Empty,
    };

    /// <summary>The extra words a Long line is given, so the room it is told it has is room the word limit allows.</summary>
    public int LengthExtraWords => Length == GapLineLength.Long ? 40 : 0;
}

/// <summary>
/// One of the card's four presets - a whole set of answers in one pick
/// (31 Oct 2026). Custom lines and the flag row's buttons are the club's own
/// and no preset touches them.
/// </summary>
internal sealed record GapPreset(string Key, string Name, string What, Func<GapRules, GapRules> Over)
{
    public static GapPreset ClubNight { get; } = new("club", "Club night", "Light, call-ups every 2 minutes, no segments in the gap.",
        r => r with
        {
            Level = GapLevel.Light, Length = GapLineLength.Short, MostLines = 3, SpaceSeconds = 30,
            CallUpRepeatSeconds = 120, CallUpsPerRace = 3, QuietAfterRecapSeconds = 10, QuietBeforeCallSeconds = 20,
            CallUps = true, TwoOut = false, Marshals = true, NotCheckedIn = false, HandOver = true,
            ComingUpNext = false, Segments = false, ClubNotes = true, Countdown = true,
        });

    public static GapPreset RaceDay { get; } = new("raceday", "Race day", "Normal, every default on the card.",
        // The automatic countdown is kept, as the other three presets keep
        // it: its tick is the Timetable's too, and a day set to Race day
        // must not switch off a countdown the Timetable shows as on.
        r => GapRules.Default with
        {
            CustomLines = r.CustomLines,
            AutoCountdown = r.AutoCountdown, CountdownMarks = r.CountdownMarks, CountdownTimesUp = r.CountdownTimesUp,
        });

    public static GapPreset BigEvent { get; } = new("big", "Big event", "Busy, segments and sponsor reads on, two-races-out call-up on.",
        r => r with
        {
            Level = GapLevel.Busy, Length = GapLineLength.Medium, MostLines = 6, SpaceSeconds = 25,
            CallUpRepeatSeconds = 120, CallUpsPerRace = 3, SponsorEveryMinutes = 2,
            CallUps = true, TwoOut = true, Marshals = true, NotCheckedIn = true, HandOver = true,
            ComingUpNext = true, Segments = true, ClubNotes = true, Countdown = true,
        });

    public static GapPreset JustCallUps { get; } = new("calls", "Just the call-ups", "Quiet, call-ups and marshals only.",
        r => r with
        {
            Level = GapLevel.Quiet, MostLines = 4, SpaceSeconds = 20, CallUpRepeatSeconds = 90, CallUpsPerRace = 3,
            CallUps = true, TwoOut = false, Marshals = true, NotCheckedIn = false, HandOver = false,
            ComingUpNext = false, Segments = false, ClubNotes = false, Countdown = true,
        });

    public static IReadOnlyList<GapPreset> All { get; } = new[] { ClubNight, RaceDay, BigEvent, JustCallUps };

    public static GapPreset? Find(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : All.FirstOrDefault(p => string.Equals(p.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The preset these rules are exactly, or null - the card then says Custom.</summary>
    public static GapPreset? Matching(GapRules rules) =>
        All.FirstOrDefault(p => Same(p.Over(rules), rules));

    /// <summary>Records compare their lists by reference, so the lists are compared here by what is in them.</summary>
    private static bool Same(GapRules a, GapRules b) =>
        a with { CountdownMarks = b.CountdownMarks, CustomLines = b.CustomLines } == b
        && a.CountdownMarks.SequenceEqual(b.CountdownMarks)
        && a.CustomLines.SequenceEqual(b.CustomLines);
}

/// <summary>What the gap knows about the race it is calling - Race Control fills it in, the preview makes one up.</summary>
internal sealed record GapFacts
{
    /// <summary>The next race's id, so a new next race starts its call-ups from nothing. Null when the program has none.</summary>
    public string? NextId { get; init; }

    /// <summary>"Heat 3".</summary>
    public string Round { get; init; } = string.Empty;

    /// <summary>"Box Stock", or empty.</summary>
    public string ClassName { get; init; } = string.Empty;

    /// <summary>The next race's drivers, as the announcer says them.</summary>
    public IReadOnlyList<string> Drivers { get; init; } = Array.Empty<string>();

    /// <summary>Who marshals it and where: ("Sam", "Turn 1"). Post empty when there are no named posts.</summary>
    public IReadOnlyList<(string Name, string Post)> Marshals { get; init; } = Array.Empty<(string, string)>();

    /// <summary>Drivers in it the check-in list shows as not checked in - and only those.</summary>
    public IReadOnlyList<string> NotCheckedIn { get; init; } = Array.Empty<string>();

    /// <summary>The race after the next one, "Heat 4", or empty.</summary>
    public string TwoOutRound { get; init; } = string.Empty;

    /// <summary>
    /// The race after next's class, "Mod", or empty. Said with the round
    /// (21 Nov 2026): "Q1 Heat 1 drivers" alone, straight after another
    /// class's Q1 Heat 1 has run, sounds like the race that just finished.
    /// </summary>
    public string TwoOutClassName { get; init; } = string.Empty;

    /// <summary>The race after the next one's id.</summary>
    public string? TwoOutId { get; init; }

    /// <summary>The track tonight, for {track}.</summary>
    public string Track { get; init; } = string.Empty;

    /// <summary>When the next race is due by the run sheet (in UTC), pushed back by the race director - null when nobody can say.</summary>
    public DateTime? DueUtc { get; init; }

    public bool HasNext => NextId is not null;
}

/// <summary>
/// The gap's own bookkeeping: when it opened, when the floor went quiet, what
/// has been said and when - and from that, whether a line may go now and,
/// when not, why. One instance runs the night (<see cref="Shared"/>), asked by
/// Race Control's calls and by the Announcer screen's director, so the two
/// count toward the same caps and keep the same spacing. The preview runs
/// one of its own.
///
/// PURE: every answer takes the time it is asked at, so the rules are tested
/// without a clock or a window. UI thread, both callers - the lock is only
/// so a read from elsewhere is never torn.
/// </summary>
internal sealed class GapState
{
    private readonly object _gate = new();

    /// <summary>The night's one gap, shared by Race Control and the Announcer screen.</summary>
    public static GapState Shared { get; } = new();

    /// <summary>How long a countdown mark stays due after its time comes (MarkDue).</summary>
    public static readonly TimeSpan MarkWindow = TimeSpan.FromSeconds(45);

    private string? _raceKey;
    private DateTime? _floorFreeSinceUtc;
    private int _lines;
    private DateTime? _lastLineUtc;
    private GapLine? _lastKind;
    private DateTime? _lastSponsorUtc;
    private bool _noticeSaid;
    private bool _longBreakSaid;
    private bool _handOverSaid;

    private string? _nextId;
    private int _callUps;
    private DateTime? _lastCallUpUtc;

    // The last call-up actually said, for WhyNotCallUpAgain only.
    // (10 Nov 2026, bug hunt 16) _lastCallUpUtc also moves on a Skip, so the
    // repeat waits its full time - but a skip puts nothing on air, so Call Up
    // pressed after it was refused as "Already called up" when it never was.
    private DateTime? _lastSaidCallUpUtc;
    private DateTime? _callUpWaitingSinceUtc;
    private DateTime? _callUpsNotBeforeUtc;
    private bool _marshalsSaid;
    private bool _notCheckedInSaid;
    private readonly HashSet<string> _twoOutSaidFor = new(StringComparer.Ordinal);
    // WHY (23 Nov 2026, bug hunt 24): each mark said, with the due time it
    // was said for (null when not known). It was a plain set, so a mark said
    // stayed said after the race was pushed back: the room heard "1 minute to
    // Heat 3", a driver got 5 more minutes, and no mark was called again - the
    // last word was a minute that had become six. See MarkDue.
    private readonly Dictionary<int, DateTime?> _marksSaid = new();

    // The due time MarkDue was last asked about, for a SaidMark that does
    // not pass its own.
    private DateTime? _lastMarkDueUtc;
    private int _pushedBackMinutes;

    /// <summary>A refusal already put in the console this gap, so each is said once rather than every second.</summary>
    private readonly HashSet<string> _toldConsole = new(StringComparer.Ordinal);

    // What Race Control last worked out, for the Announcer screen's director
    // to be judged by without reaching into Race Control - see Publish.
    private GapRules _published = new();
    private GapFacts _publishedFacts = new();
    private bool _publishedHeld;
    private bool _publishedOnAir;

    /// <summary>Whether a gap is open - a race has finished and the next is not called yet.</summary>
    public bool IsOpen
    {
        get { lock (_gate) { return _raceKey is not null; } }
    }

    /// <summary>Lines said in this gap.</summary>
    public int Lines
    {
        get { lock (_gate) { return _lines; } }
    }

    /// <summary>Call-ups said for the next race.</summary>
    public int CallUps
    {
        get { lock (_gate) { return _callUps; } }
    }

    /// <summary>
    /// Whether a call-up has actually been said for the next race - a Skip
    /// counts toward CallUps but puts nothing on air.
    /// </summary>
    /// <remarks>
    /// WHY (23 Nov 2026, bug hunt 24): the call-up's wording read CallUps > 0,
    /// so after Skip Next Call-up the first call the room heard was a repeat:
    /// "Reminder, Heat 3 is up next" for a race it was never told about.
    /// </remarks>
    public bool CallUpSaid
    {
        get { lock (_gate) { return _lastSaidCallUpUtc is not null; } }
    }

    /// <summary>
    /// Whether the Call Up button's call-up is followed by the marshals:
    /// ticked, somebody posted, and not called for this race already. With
    /// the call-up's box unticked the marshals go by themselves early in the
    /// gap, and a Call Up pressed after that said them a second time.
    /// </summary>
    public bool MarshalsFollowACallUp(GapRules rules, GapFacts facts)
    {
        lock (_gate)
        {
            return rules.Marshals && facts.Marshals.Count > 0 && !_marshalsSaid;
        }
    }

    /// <summary>Minutes the race director has pushed the next race back (Call Up's right-click).</summary>
    public int PushedBackMinutes
    {
        get { lock (_gate) { return _pushedBackMinutes; } }
    }

    /// <summary>
    /// Opens the gap after the race named <paramref name="raceKey"/>, or
    /// leaves it alone when that race's gap is already open. A new race
    /// starts every count from nothing, apart from the sponsor's spacing,
    /// which runs across gaps as it always has.
    /// </summary>
    public void Open(string raceKey, DateTime nowUtc)
    {
        lock (_gate)
        {
            if (string.Equals(_raceKey, raceKey, StringComparison.Ordinal))
            {
                return;
            }

            _raceKey = raceKey;
            _floorFreeSinceUtc = null;
            _lines = 0;
            _lastLineUtc = null;
            _lastKind = null;
            _noticeSaid = false;
            _longBreakSaid = false;
            _handOverSaid = false;
            _toldConsole.Clear();
        }
    }

    /// <summary>Closes the gap - the next race was called, or the race was reset.</summary>
    public void Close()
    {
        lock (_gate)
        {
            _raceKey = null;
            _floorFreeSinceUtc = null;
        }
    }

    /// <summary>
    /// Says which race is next. A different one from last time starts its
    /// call-ups, marshals, countdown marks and push-back from nothing.
    /// </summary>
    public void NextRace(string? nextId)
    {
        lock (_gate)
        {
            if (string.Equals(_nextId, nextId, StringComparison.Ordinal))
            {
                return;
            }

            _nextId = nextId;
            _callUps = 0;
            _lastCallUpUtc = null;
            _lastSaidCallUpUtc = null;
            _callUpsNotBeforeUtc = null;
            _callUpWaitingSinceUtc = null;
            _marshalsSaid = false;
            _notCheckedInSaid = false;
            _marksSaid.Clear();
            _pushedBackMinutes = 0;
        }
    }

    /// <summary>Something is on air - the recap, a segment, a line. The quiet after it starts when it stops.</summary>
    public void FloorBusy()
    {
        lock (_gate)
        {
            _floorFreeSinceUtc = null;
        }
    }

    /// <summary>
    /// Race Control's reading of the gap, once a second: the rules with the
    /// race day's preset over them, the next race, whether talk is held and
    /// whether On Air has the gap. The director is judged by it
    /// (WhyNotForTheDirector) from whatever thread it writes on.
    /// </summary>
    public void Publish(GapRules rules, GapFacts facts, bool held, bool onAirHasTheGap)
    {
        lock (_gate)
        {
            _published = rules;
            _publishedFacts = facts;
            _publishedHeld = held;
            _publishedOnAir = onAirHasTheGap;
        }
    }

    /// <summary>
    /// Why the director may not say a line of this kind now, by what Race
    /// Control last published - or null when it may, and always null with no
    /// gap open (before the first race the director runs as it always has).
    /// </summary>
    public string? WhyNotForTheDirector(GapLine kind, DateTime nowUtc)
    {
        GapRules rules;
        GapFacts facts;
        bool held, onAir;

        lock (_gate)
        {
            if (_raceKey is null)
            {
                return null;
            }

            rules = _published;
            facts = _publishedFacts;
            held = _publishedHeld;
            onAir = _publishedOnAir;
        }

        return WhyNot(kind, rules, nowUtc, facts, held, onAir);
    }

    /// <summary>
    /// The director's line went out - booked only while a gap is open, so
    /// a line before the first race does not count toward the first gap.
    /// </summary>
    public void SaidByTheDirector(GapLine kind, DateTime nowUtc)
    {
        if (IsOpen)
        {
            Said(kind, nowUtc);
        }
    }

    /// <summary>
    /// How long the director's line should be (the card's Line length), as
    /// a sentence for the prompt and the extra words it is given - for the
    /// filler in an open gap only: nothing while the recap show holds the
    /// floor, nothing over a race, so the show's own parts are untouched.
    /// </summary>
    public (string Brief, int ExtraWords) LengthFor(SegmentKind kind)
    {
        lock (_gate)
        {
            if (_raceKey is null || _floorFreeSinceUtc is null || CommentaryDirector.InRace(kind))
            {
                return (string.Empty, 0);
            }

            return (_published.LengthBrief, _published.LengthExtraWords);
        }
    }

    /// <summary>The director's segment kinds as the gap counts them.</summary>
    public static GapLine LineFor(SegmentKind kind) => kind switch
    {
        SegmentKind.ComingUpNext => GapLine.ComingUpNext,
        SegmentKind.ClubAside => GapLine.ClubNote,
        _ => GapLine.Segment,
    };

    /// <summary>Nothing is on air. The first call starts the quiet after the recap.</summary>
    public void FloorFree(DateTime nowUtc)
    {
        lock (_gate)
        {
            _floorFreeSinceUtc ??= nowUtc;
        }
    }

    /// <summary>
    /// Hold Talk let go: talk picks up after the Space between lines, as
    /// though a line had just ended, rather than the instant the hold comes
    /// off (the plan's section 8). Counts no line toward the gap's limit.
    /// </summary>
    public void Resumed(DateTime nowUtc)
    {
        lock (_gate)
        {
            if (_raceKey is not null)
            {
                _lastLineUtc = nowUtc;
            }
        }
    }

    /// <summary>A line went out. <paramref name="manual"/> is the Call Up button, which also restarts the repeat.</summary>
    public void Said(GapLine kind, DateTime nowUtc)
    {
        lock (_gate)
        {
            _lines++;
            _lastLineUtc = nowUtc;
            _lastKind = kind;

            switch (kind)
            {
                case GapLine.CallUp:
                    _callUps++;
                    _lastCallUpUtc = nowUtc;
                    _lastSaidCallUpUtc = nowUtc;
                    break;
                case GapLine.Marshals:
                    _marshalsSaid = true;
                    break;
                case GapLine.NotCheckedIn:
                    _notCheckedInSaid = true;
                    break;
                case GapLine.Sponsor:
                    _lastSponsorUtc = nowUtc;
                    break;
                case GapLine.Notice:
                    _noticeSaid = true;
                    break;
                case GapLine.LongBreak:
                    _longBreakSaid = true;
                    break;
                case GapLine.HandOver:
                    _handOverSaid = true;
                    break;
            }
        }
    }

    /// <summary>The two-races-out call went out for that race - said once per race, whichever gap it falls in.</summary>
    public void SaidTwoOut(string raceId, DateTime nowUtc)
    {
        Said(GapLine.TwoOut, nowUtc);

        lock (_gate)
        {
            _twoOutSaidFor.Add(raceId);
        }
    }

    /// <summary>A countdown mark was called (0 is time's up). Marks are not lines: they do not count toward the cap.</summary>
    /// <param name="dueUtc">The due time it was called for; left out, the one MarkDue was last asked about.</param>
    public void SaidMark(int minutes, DateTime nowUtc, DateTime? dueUtc = null)
    {
        lock (_gate)
        {
            _marksSaid[minutes] = dueUtc ?? _lastMarkDueUtc;
            _lastLineUtc = nowUtc;
        }
    }

    /// <summary>
    /// Push the next race back a minute: its run-sheet time moves, and so
    /// does the next automatic call-up.
    /// </summary>
    public void PushBack(DateTime nowUtc, int minutes = 1)
    {
        lock (_gate)
        {
            _pushedBackMinutes += minutes;
            var from = _callUpsNotBeforeUtc is { } held && held > nowUtc ? held : nowUtc;
            _callUpsNotBeforeUtc = from.AddMinutes(minutes);
        }
    }

    /// <summary>
    /// Skip the next automatic call-up: it counts as said, so the next one
    /// waits its full repeat time (or never comes, when that was the last).
    /// </summary>
    public void SkipCallUp(DateTime nowUtc)
    {
        lock (_gate)
        {
            _callUps++;
            _lastCallUpUtc = nowUtc;
        }
    }

    /// <summary>
    /// How soon after a call-up a press of Call Up is taken as the same
    /// press again rather than a second call - see <see cref="WhyNotCallUpAgain"/>.
    /// </summary>
    public const int CallUpAgainAfterSeconds = 30;

    /// <summary>How long a queued call-up counts as still waiting, at most - see WhyNotCallUpAgain.</summary>
    public static readonly TimeSpan CallUpLongestWait = TimeSpan.FromMinutes(2);

    /// <summary>
    /// A call-up went to the announcer and has not been taken off its queue
    /// yet - so a press now would only stack a second one behind it.
    /// </summary>
    public void CallUpQueued(DateTime nowUtc)
    {
        lock (_gate)
        {
            _callUpWaitingSinceUtc = nowUtc;
        }
    }

    /// <summary>The announcer took a queued call-up - see <see cref="CallUpQueued"/>.</summary>
    public void CallUpDequeued()
    {
        lock (_gate)
        {
            _callUpWaitingSinceUtc = null;
        }
    }

    /// <summary>
    /// Why Call Up (the button, F12, the phone) should not call the next
    /// race up again now, or null when it may.
    ///
    /// WHY (1 Nov 2026, Asher: three A-Main call-ups 7 s apart): Call Up says
    /// the call-up whatever the caps and spacing, as the race director asked
    /// for it - but the voice takes a second or two to start, so a press
    /// looks like it did nothing, and pressing again called the race again.
    /// Now a press while a call-up is still waiting to be said, or within
    /// <see cref="CallUpAgainAfterSeconds"/> of the last one (Call Up's own
    /// or the automatic one), is answered instead. A press after that still
    /// goes, whatever the caps, as before.
    /// </summary>
    public string? WhyNotCallUpAgain(string round, DateTime nowUtc)
    {
        lock (_gate)
        {
            // Bounded, because the queue can bin a line without ever running it
            // (a flag clears the decks) and then nothing says it was taken.
            if (_callUpWaitingSinceUtc is { } since && nowUtc - since < CallUpLongestWait)
            {
                return $"Already called up {round} - it's still waiting to be said.";
            }

            // The last one said, not skipped - a skip said nothing (10 Nov 2026, bug hunt 16).
            if (_lastSaidCallUpUtc is { } last && nowUtc >= last
                && nowUtc - last < TimeSpan.FromSeconds(CallUpAgainAfterSeconds))
            {
                return $"Already called up {round} {(nowUtc - last).TotalSeconds:0}s ago.";
            }

            return null;
        }
    }

    /// <summary>True the first time a reason is offered in this gap - so the console hears each refusal once.</summary>
    public bool FirstTimeTold(string reason)
    {
        lock (_gate)
        {
            return _toldConsole.Add(reason);
        }
    }

    /// <summary>
    /// Why <paramref name="kind"/> may not be said now, or null when it may.
    /// The reasons are in the console's words ("inside the quiet before the
    /// call"). In this order, cheapest and most absolute first:
    ///   held; switched off; On Air has the gap; something on air; the quiet
    ///   after the recap; the quiet before the call; the gap's line cap; the
    ///   space between lines; then the kind's own rule.
    /// Call-ups get through the line cap - they count toward it, so the more
    /// call-ups a gap needs, the less filler it gets - and a marshal call
    /// straight after its call-up is part of the same call, so the space
    /// between lines does not hold it.
    /// </summary>
    /// <param name="onAirHasTheGap">The recap has just been on air and On Air opens the next race (RaceControlControl.OnAirFollowsTheRecap).</param>
    public string? WhyNot(GapLine kind, GapRules rules, DateTime nowUtc, GapFacts facts, bool held, bool onAirHasTheGap)
    {
        lock (_gate)
        {
            if (_raceKey is null)
            {
                return "no race has finished";
            }

            if (held)
            {
                return "between-race talk is held";
            }

            if (!rules.Allows(kind))
            {
                return rules.Level is GapLevel.Quiet or GapLevel.Light && !GapRules.IsACall(kind)
                    ? $"Between races is set to {rules.Level}"
                    : "its box is unticked on Between races";
            }

            if (onAirHasTheGap)
            {
                if (GapRules.IsTheDirectors(kind))
                {
                    return "On Air opens the next race and says it there";
                }

                if (!rules.CallUpsWithOnAir)
                {
                    return "On Air opens the next race, and call-ups after the recap are unticked";
                }
            }

            if (_floorFreeSinceUtc is not { } freeSince)
            {
                return "something is on air";
            }

            if (nowUtc - freeSince < TimeSpan.FromSeconds(rules.QuietAfterRecapSeconds))
            {
                return "inside the quiet after the recap";
            }

            if (kind != GapLine.Countdown && facts.DueUtc is { } due && due > nowUtc
                && due - nowUtc < TimeSpan.FromSeconds(rules.QuietBeforeCallSeconds))
            {
                return "inside the quiet before the call";
            }

            var attached = kind == GapLine.Marshals && _lastKind == GapLine.CallUp;

            if (!GapRules.IsACall(kind) && _lines >= rules.LinesAllowed)
            {
                return $"the gap has had its {Plural.Of(rules.LinesAllowed, "line")}";
            }

            if (!attached && _lastLineUtc is { } last && nowUtc - last < TimeSpan.FromSeconds(rules.SpaceSeconds))
            {
                return $"only {(nowUtc - last).TotalSeconds:0}s since the last line";
            }

            // The call-up first: the director's filler waits until the next
            // race has been called once, when a call-up is due to go at all.
            if (GapRules.IsTheDirectors(kind) && kind != GapLine.HandOver && _callUps == 0 && facts.HasNext
                && rules.Allows(GapLine.CallUp) && WhyNotACallUp(rules, nowUtc, facts) is null)
            {
                return "the call-up goes first";
            }

            // Marshals and not-checked-in wait for the call-up only when one
            // can go: with the call-up's box unticked they would wait all gap.
            return kind switch
            {
                GapLine.CallUp => WhyNotACallUp(rules, nowUtc, facts),
                GapLine.Marshals => !facts.HasNext ? "the program has nothing next"
                    : facts.Marshals.Count == 0 ? "nobody is posted to marshal it"
                    : _marshalsSaid ? "the marshals have been called"
                    : _callUps == 0 && rules.Allows(GapLine.CallUp) ? "the call-up goes first"
                    : null,
                GapLine.NotCheckedIn => !facts.HasNext ? "the program has nothing next"
                    : facts.NotCheckedIn.Count == 0 ? "everyone in it is checked in"
                    : _notCheckedInSaid ? "already said"
                    : _callUps == 0 && rules.Allows(GapLine.CallUp) ? "the call-up goes first"
                    : null,
                GapLine.TwoOut => facts.TwoOutId is not { } twoOut ? "nothing is two races out"
                    : _twoOutSaidFor.Contains(twoOut) ? "already said for that race"
                    : _callUps == 0 && facts.HasNext && rules.Allows(GapLine.CallUp) ? "the call-up goes first"
                    : null,
                GapLine.HandOver => _handOverSaid ? "already handed over this gap" : null,
                GapLine.Sponsor => _lastSponsorUtc is { } read && nowUtc - read < rules.SponsorSpacing
                    ? $"a sponsor read went out {(nowUtc - read).TotalSeconds:0}s ago"
                    : null,
                GapLine.Notice => _noticeSaid ? "the club's notice has been said this gap"
                    : GapWords.Sort(rules.CustomLines).Notices.Count == 0 ? "none of the custom lines is a notice"
                    // WHY (23 Nov 2026, bug hunt 24): a notice that cannot be
                    // filled in ("{minutes}" with no run-sheet time) came out
                    // empty, but was still booked as said - a silent line that
                    // used up one of the gap's lines and held the next real
                    // one for the space between lines. Now it waits until one
                    // fills, so a time that turns up later in the gap still
                    // gets it said.
                    : !GapWords.AnyNoticeFills(facts, rules, nowUtc) ? "none of the club's notices can be filled in right now"
                    : null,
                GapLine.LongBreak => _longBreakSaid ? "already said"
                    : facts.DueUtc is not { } dueAt || dueAt - nowUtc < TimeSpan.FromMinutes(rules.LongBreakMinutes)
                        ? "this is not a long break"
                        : null,
                _ => null,
            };
        }
    }

    private string? WhyNotACallUp(GapRules rules, DateTime nowUtc, GapFacts facts)
    {
        if (!facts.HasNext)
        {
            return "the program has nothing next";
        }

        if (_callUps >= rules.CallUpsPerRace)
        {
            return $"{Plural.Of(_callUps, "call-up")} already, the most Between races allows";
        }

        if (_callUps >= 1 && rules.CallUpRepeatSeconds == 0)
        {
            return "called once, and Repeat the call-up is Never";
        }

        if (_callUpsNotBeforeUtc is { } notBefore && nowUtc < notBefore)
        {
            return "the race was pushed back";
        }

        if (_lastCallUpUtc is { } last && nowUtc - last < TimeSpan.FromSeconds(rules.CallUpRepeatSeconds))
        {
            return $"next repeat in {(TimeSpan.FromSeconds(rules.CallUpRepeatSeconds) - (nowUtc - last)).TotalSeconds:0}s";
        }

        // In a long break the room was told when racing resumes; the call-up
        // waits until that is near.
        if (rules.LongBreakNotice && facts.DueUtc is { } due && due - nowUtc > GapRules.LongBreakCallUpsFrom
            && due - nowUtc >= TimeSpan.FromMinutes(rules.LongBreakMinutes))
        {
            return "a long break - the call-up waits until racing is near";
        }

        return null;
    }

    /// <summary>
    /// The countdown mark due now, in minutes (0 is time's up), or null.
    /// Only with the automatic countdown on, a run-sheet time for the next
    /// race, and the floor free. A mark is due from the moment its time
    /// comes until 45 seconds after - one missed under a long recap is let
    /// go rather than said late, when it would be wrong.
    /// </summary>
    public int? MarkDue(GapRules rules, DateTime nowUtc, GapFacts facts, bool held)
    {
        if (!rules.AutoCountdown || !rules.Countdown || held || facts.DueUtc is not { } due || !facts.HasNext)
        {
            return null;
        }

        lock (_gate)
        {
            _lastMarkDueUtc = due;

            if (_floorFreeSinceUtc is null)
            {
                return null;
            }

            var marks = rules.CountdownTimesUp ? rules.CountdownMarks.Append(0) : rules.CountdownMarks;

            foreach (var mark in marks)
            {
                var at = due - TimeSpan.FromMinutes(mark);

                if (nowUtc >= at && nowUtc - at < MarkWindow && !MarkSaidFor(mark, due))
                {
                    return mark;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// WHY (23 Nov 2026, bug hunt 24): a mark counts as said only for the
    /// due time it was said for. Once the race is pushed back (a minute from
    /// Call Up, or a driver's "Need 5 minutes") the due moves by whole
    /// minutes and the mark is due again at its new time, so the room hears
    /// the countdown move with the race. Half a minute's give, so the small
    /// drift of a late run sheet or a held Auto start does not say it twice;
    /// and a push-back that does not move the start (Auto start waiting on a
    /// promised time) leaves the due where it was, so nothing repeats. Under
    /// the lock.
    /// </summary>
    private bool MarkSaidFor(int mark, DateTime due) =>
        _marksSaid.TryGetValue(mark, out var saidFor)
        && (saidFor is not { } said || due - said < TimeSpan.FromSeconds(30));

    /// <summary>
    /// The next of Race Control's own lines that may go now, in the order a
    /// race director says them: the long-break notice, the call-up, the
    /// marshals, who is not checked in, two races out, then the club's
    /// notice. Null when none may; <paramref name="heldBack"/> is then why
    /// the call-up is not going, for the console and the Broadcast window.
    /// </summary>
    public GapLine? NextCall(GapRules rules, DateTime nowUtc, GapFacts facts, bool held, bool onAirHasTheGap, out string? heldBack)
    {
        heldBack = null;

        foreach (var kind in CallOrder)
        {
            var why = WhyNot(kind, rules, nowUtc, facts, held, onAirHasTheGap);

            if (why is null)
            {
                return kind;
            }

            if (kind == GapLine.CallUp)
            {
                heldBack = why;
            }
        }

        return null;
    }

    /// <summary>Race Control's lines, in the order they are tried.</summary>
    public static IReadOnlyList<GapLine> CallOrder { get; } = new[]
    {
        GapLine.LongBreak, GapLine.CallUp, GapLine.Marshals, GapLine.NotCheckedIn, GapLine.TwoOut, GapLine.Notice,
    };

    /// <summary>
    /// When the next call-up is due, for the Broadcast window's row: "Call-up
    /// for Heat 3 in 40s", "Call-up for Heat 3 now", or why there will be no
    /// more. Empty with no gap open.
    /// </summary>
    public string DueNext(GapRules rules, DateTime nowUtc, GapFacts facts, bool held, bool onAirHasTheGap)
    {
        if (!IsOpen)
        {
            return string.Empty;
        }

        if (held)
        {
            return "Between-race talk held - press Resume Talk or F10";
        }

        if (!facts.HasNext)
        {
            return "Nothing next in the program";
        }

        var what = $"Call-up for {facts.Round}";
        var why = WhyNot(GapLine.CallUp, rules, nowUtc, facts, held, onAirHasTheGap);

        if (why is null)
        {
            return $"{what} now";
        }

        DateTime? at;

        lock (_gate)
        {
            if (_callUps >= rules.CallUpsPerRace || (_callUps >= 1 && rules.CallUpRepeatSeconds == 0) || !rules.Allows(GapLine.CallUp))
            {
                return $"No more call-ups for {facts.Round} ({why})";
            }

            var candidates = new List<DateTime>();

            if (_floorFreeSinceUtc is { } free)
            {
                candidates.Add(free.AddSeconds(rules.QuietAfterRecapSeconds));
            }

            if (_lastLineUtc is { } last)
            {
                candidates.Add(last.AddSeconds(rules.SpaceSeconds));
            }

            if (_lastCallUpUtc is { } call)
            {
                candidates.Add(call.AddSeconds(rules.CallUpRepeatSeconds));
            }

            if (_callUpsNotBeforeUtc is { } notBefore)
            {
                candidates.Add(notBefore);
            }

            at = _floorFreeSinceUtc is null ? null : candidates.Count > 0 ? candidates.Max() : nowUtc;
        }

        // Only the waits that end by themselves get a time; the rest say why.
        var waiting = why.StartsWith("only", StringComparison.Ordinal)
                      || why.StartsWith("next repeat", StringComparison.Ordinal)
                      || why.StartsWith("inside the quiet after", StringComparison.Ordinal)
                      || why.StartsWith("the race was pushed", StringComparison.Ordinal);

        if (waiting && at is { } when && when > nowUtc)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling((when - nowUtc).TotalSeconds));
            return $"{what} in {GapWords.Duration(seconds)}";
        }

        return $"{what} held - {why}";
    }
}

/// <summary>
/// What the gap's lines say - HeatCaller's own wording, written here and not
/// by a model, so they cost nothing and say the same thing whatever the
/// relay is doing. The built-in phrasings come from a shuffled bag, like the
/// hand-over, so the same sentence is not heard twice in a row; a club's own
/// lines take the place of the built-in ones when it has written them.
/// </summary>
internal static class GapWords
{
    /// <summary>The On Air bar while Hold Talk is on and nothing is going out.</summary>
    public const string TalkHeld = "Between-race talk held - press Resume Talk or F10";

    /// <summary>What Skip would skip, at the right of the On Air bar: "Skip: Race Recap".</summary>
    public static string SkipNote(string segment) => $"Skip: {segment}";

    /// <summary>Why the call-up waits when the phones and Start Next name different races - the console's words.</summary>
    public static string PhoneAndPaDisagree(string startNext, string? phonesSay) =>
        phonesSay is null
            ? $"{startNext} has nobody on its grid, so no phone shows it up next"
            : $"the drivers' phones show {phonesSay} up next, and Start Next would start {startNext}";

    private static readonly string[] FirstCalls =
    {
        "{race}, you're up next. Head to the drivers stand.",
        "Next up, {race}. Drivers, to the stand please.",
        "{race} is next. Drivers, make your way to the stand.",
    };

    private static readonly string[] RepeatCalls =
    {
        "Reminder, {race} is up next. Drivers to the stand please.",
        "Still waiting on {race}. Drivers, to the stand now please.",
        "{race}, this is your call. Drivers to the stand.",
    };

    private static readonly Dictionary<string, int> LastPicked = new(StringComparer.Ordinal);
    private static readonly Random Shuffle = new();

    /// <summary>"Heat 3 for Box Stock", or "Heat 3" with no class.</summary>
    public static string Race(string round, string className) =>
        string.IsNullOrWhiteSpace(className) ? round.Trim() : $"{round.Trim()} for {className.Trim()}";

    /// <summary>The call-up - the club's own wording when it has some, else one of the built-in phrasings.</summary>
    /// <param name="repeat">Not the first call for this race - a repeat says so.</param>
    public static string CallUp(GapFacts facts, GapRules rules, bool repeat, DateTime nowUtc)
    {
        var own = Sort(rules.CustomLines).CallUps;

        if (own.Count > 0 && Pick("own-callup", own) is { } template && Fill(template, facts, nowUtc) is { Length: > 0 } filled)
        {
            return filled;
        }

        var bag = repeat ? RepeatCalls : FirstCalls;
        return (Pick(repeat ? "repeat" : "first", bag) ?? bag[0]).Replace("{race}", Race(facts.Round, facts.ClassName), StringComparison.Ordinal);
    }

    /// <summary>A round's class as the announcer says it - "Stock and Mod" for a shared heat - or empty with no round or class.</summary>
    internal static string SpokenClassOf(RoundDefinition? round) =>
        round is null ? string.Empty : (MixedHeats.SpokenClasses(round) ?? string.Empty).Trim();

    /// <summary>"Box Stock, Heat 4 drivers, you're two races away. Get your cars ready." - "Heat 4 drivers, ..." with no class.</summary>
    public static string TwoOut(string round, string className = "") =>
        string.IsNullOrWhiteSpace(className)
            ? $"{round.Trim()} drivers, you're two races away. Get your cars ready."
            : $"{className.Trim()}, {round.Trim()} drivers, you're two races away. Get your cars ready.";

    /// <summary>"Marshals for Heat 3: Sam on Turn 1, Bill on Turn 2." - or the names alone when the posts have none.</summary>
    public static string Marshals(GapFacts facts, GapRules rules, DateTime nowUtc)
    {
        var own = Sort(rules.CustomLines).MarshalCalls;

        if (own.Count > 0 && Pick("own-marshals", own) is { } template && Fill(template, facts, nowUtc) is { Length: > 0 } filled)
        {
            return filled;
        }

        var named = facts.Marshals.Where(m => m.Post.Trim().Length > 0).ToList();

        if (named.Count == facts.Marshals.Count && named.Count > 0)
        {
            return $"Marshals for {facts.Round.Trim()}: " + string.Join(", ", named.Select(m => $"{m.Name} on {m.Post.Trim()}")) + ".";
        }

        return $"Marshals for {facts.Round.Trim()}: {AnnouncementText.SpokenList(facts.Marshals.Select(m => m.Name).ToList())}.";
    }

    /// <summary>
    /// "Still waiting on Frank for Heat 3. Frank, check in at the desk please."
    /// More than four names is a count: a list that long is a roll call, not a nudge.
    /// </summary>
    public static string NotCheckedIn(GapFacts facts)
    {
        var names = facts.NotCheckedIn;

        return names.Count switch
        {
            1 => $"Still waiting on {names[0]} for {facts.Round.Trim()}. {names[0]}, check in at the desk please.",
            <= 4 => $"Still waiting on {AnnouncementText.SpokenList(names)} for {facts.Round.Trim()}. Please check in at the desk.",
            _ => $"Still waiting on {names.Count} drivers for {facts.Round.Trim()}. Check in at the desk please.",
        };
    }

    /// <summary>"Racing resumes at 7:45 PM, in about 20 minutes, with Heat 3."</summary>
    public static string LongBreak(GapFacts facts, DateTime nowUtc)
    {
        var due = facts.DueUtc ?? nowUtc;
        var minutes = Math.Max(1, (int)Math.Round((due - nowUtc).TotalMinutes));
        var at = due.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);

        return facts.HasNext
            ? $"Racing resumes at {at}, in about {Plural.Of(minutes, "minute")}, with {facts.Round.Trim()}."
            : $"Racing resumes at {at}, in about {Plural.Of(minutes, "minute")}.";
    }

    /// <summary>The countdown's marks, in the Timetable countdown's own words.</summary>
    public static string Mark(int minutes, string round) => minutes switch
    {
        0 => $"Time's up for {round.Trim()}!",
        1 => $"1 minute to {round.Trim()}.",
        _ => $"{minutes} minutes to {round.Trim()}.",
    };

    /// <summary>The club's next notice from the shuffled bag, filled in - empty when there is none that fills.</summary>
    public static string Notice(GapFacts facts, GapRules rules, DateTime nowUtc)
    {
        var notices = Sort(rules.CustomLines).Notices;

        if (Pick("notice", notices) is null)
        {
            return string.Empty;
        }

        // WHY (23 Nov 2026, bug hunt 24): a random first pick (never the last
        // one said), then every notice in turn from there. It made
        // notices.Count random picks, which only avoid the last pick, so with
        // three notices it could go A, C, A and never try the one that fills.
        lock (LastPicked)
        {
            var start = LastPicked.TryGetValue("notice", out var picked) ? picked : 0;

            for (var i = 0; i < notices.Count; i++)
            {
                var index = (start + i) % notices.Count;

                if (Fill(notices[index], facts, nowUtc) is { Length: > 0 } filled)
                {
                    LastPicked["notice"] = index;
                    return filled;
                }
            }
        }

        return string.Empty;
    }

    /// <summary>Whether any of the club's notices fills in now - see GapState.WhyNot's notice rule.</summary>
    public static bool AnyNoticeFills(GapFacts facts, GapRules rules, DateTime nowUtc) =>
        Sort(rules.CustomLines).Notices.Any(n => Fill(n, facts, nowUtc).Length > 0);

    /// <summary>The club's lines, sorted by what they are for.</summary>
    internal sealed record Sorted(IReadOnlyList<string> CallUps, IReadOnlyList<string> MarshalCalls, IReadOnlyList<string> Notices);

    /// <summary>
    /// Which of the club's lines is which, by the placeholders in it: one
    /// with {marshals} is the marshal call; one with {round}, {class} or
    /// {drivers} is the call-up; anything else - {minutes}, {track} or a
    /// plain sentence - is a notice, said once a gap.
    /// </summary>
    public static Sorted Sort(IReadOnlyList<string> lines)
    {
        var callUps = new List<string>();
        var marshals = new List<string>();
        var notices = new List<string>();

        foreach (var line in lines)
        {
            if (line.Contains("{marshals}", StringComparison.OrdinalIgnoreCase))
            {
                marshals.Add(line);
            }
            else if (line.Contains("{round}", StringComparison.OrdinalIgnoreCase)
                     || line.Contains("{class}", StringComparison.OrdinalIgnoreCase)
                     || line.Contains("{drivers}", StringComparison.OrdinalIgnoreCase))
            {
                callUps.Add(line);
            }
            else
            {
                notices.Add(line);
            }
        }

        return new Sorted(callUps, marshals, notices);
    }

    /// <summary>The placeholders a custom line may use, for the card's hint.</summary>
    public static IReadOnlyList<string> Placeholders { get; } = new[] { "{class}", "{round}", "{marshals}", "{drivers}", "{minutes}", "{track}" };

    /// <summary>The examples the card offers to put in the box.</summary>
    public static IReadOnlyList<string> Examples { get; } = new[]
    {
        "{round} for {class} is next. Drivers, to the stand please.",
        "Marshals for {round}: {marshals}.",
        "Racing resumes in about {minutes} minutes.",
        "Pit lane is closed while the track is being reset.",
    };

    /// <summary>
    /// A custom line with its placeholders filled. Empty when a placeholder
    /// it uses has nothing behind it - "Marshals for Heat 3: ." is worse than
    /// not saying it, so the line is skipped and the next one tried.
    /// </summary>
    public static string Fill(string template, GapFacts facts, DateTime nowUtc)
    {
        var minutes = facts.DueUtc is { } due && due > nowUtc
            ? Math.Max(1, (int)Math.Round((due - nowUtc).TotalMinutes)).ToString(CultureInfo.InvariantCulture)
            : string.Empty;

        var values = new (string Token, string Value)[]
        {
            ("{class}", facts.ClassName.Trim()),
            ("{round}", facts.Round.Trim()),
            ("{marshals}", AnnouncementText.SpokenList(facts.Marshals.Select(m => m.Name).ToList())),
            ("{drivers}", AnnouncementText.SpokenList(facts.Drivers)),
            ("{minutes}", minutes),
            ("{track}", facts.Track.Trim()),
        };

        var text = template;

        foreach (var (token, value) in values)
        {
            if (!text.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (value.Length == 0)
            {
                return string.Empty;
            }

            text = text.Replace(token, value, StringComparison.OrdinalIgnoreCase);
        }

        return text.Trim();
    }

    /// <summary>"40s", "2 min", "2 min 10s".</summary>
    public static string Duration(int seconds) => seconds switch
    {
        < 60 => $"{seconds}s",
        _ when seconds % 60 == 0 => $"{seconds / 60} min",
        _ => $"{seconds / 60} min {seconds % 60}s",
    };

    /// <summary>One from the bag, never the one picked last time from the same bag when there is a choice.</summary>
    private static string? Pick(string bag, IReadOnlyList<string> from)
    {
        if (from.Count == 0)
        {
            return null;
        }

        lock (LastPicked)
        {
            var last = LastPicked.TryGetValue(bag, out var l) ? l : -1;
            var index = from.Count == 1 ? 0 : Shuffle.Next(from.Count - 1);

            if (from.Count > 1 && index >= last && last >= 0)
            {
                index++;
            }

            index = Math.Clamp(index, 0, from.Count - 1);
            LastPicked[bag] = index;
            return from[index];
        }
    }
}

/// <summary>
/// HEAR THE GAP (31 Oct 2026). What a gap would sound like with these
/// settings, worked out by the same <see cref="GapState"/> the night uses -
/// so the preview cannot promise a line the night would refuse. A gap of a
/// few minutes is played second by second; the lines come back with when
/// each would be said ("0:10 hand-over"), for the card's written running
/// order, and are then spoken back to back so the whole thing takes under a
/// minute to hear.
/// </summary>
internal static class GapPreview
{
    /// <summary>One line of the running order.</summary>
    internal sealed record Step(TimeSpan At, GapLine Kind, string Text, bool WrittenByTheModel);

    /// <summary>Roughly how long a line takes to say, so the floor is busy while it is.</summary>
    private static readonly TimeSpan SpeakingFor = TimeSpan.FromSeconds(5);

    /// <summary>Sample names for a PC with no program open.</summary>
    public static GapFacts Sample(DateTime startUtc, TimeSpan untilDue) => new()
    {
        NextId = "sample-next",
        Round = "Heat 3",
        ClassName = "Box Stock",
        Drivers = new[] { "Sam", "Bill", "Frank", "Alex" },
        Marshals = new[] { ("Jo", "Turn 1"), ("Chris", "Turn 2"), ("Pat", "the hairpin") },
        NotCheckedIn = new[] { "Frank" },
        TwoOutRound = "Heat 4",
        TwoOutClassName = "Box Stock",
        TwoOutId = "sample-two-out",
        Track = "the club track",
        DueUtc = startUtc + untilDue,
    };

    /// <summary>
    /// The gap from the recap's last line to the grid call, as these rules
    /// would fill it. <paramref name="withTheDirector"/> adds the lines the
    /// Announcer screen would put in (hand-over, Coming up next, a segment, a
    /// sponsor read, a club note) where the rules leave room - with AI
    /// commentary off only the hand-over and sponsor reads are possible.
    /// </summary>
    public static IReadOnlyList<Step> Run(GapRules rules, GapFacts facts, DateTime startUtc, bool withTheDirector, bool commentaryOn,
        bool sponsorReads, bool onAirOpensTheNextRace)
    {
        var state = new GapState();
        state.Open("preview", startUtc);
        state.NextRace(facts.NextId);

        var steps = new List<Step>();
        var end = facts.DueUtc ?? startUtc + TimeSpan.FromMinutes(4);
        DateTime? busyUntil = null;

        var directorOrder = new List<GapLine> { GapLine.HandOver };

        if (commentaryOn)
        {
            directorOrder.Add(GapLine.ComingUpNext);
            directorOrder.Add(GapLine.Segment);
        }

        if (sponsorReads)
        {
            directorOrder.Add(GapLine.Sponsor);
        }

        if (commentaryOn)
        {
            directorOrder.Add(GapLine.ClubNote);
        }

        var directorSaid = new HashSet<GapLine>();

        for (var now = startUtc; now <= end; now = now.AddSeconds(1))
        {
            if (busyUntil is { } until && now < until)
            {
                state.FloorBusy();
                continue;
            }

            state.FloorFree(now);

            void Add(GapLine kind, string text, bool model)
            {
                steps.Add(new Step(now - startUtc, kind, text, model));
                busyUntil = now + SpeakingFor;
            }

            if (state.MarkDue(rules, now, facts, held: false) is { } mark)
            {
                state.SaidMark(mark, now, facts.DueUtc);
                Add(GapLine.Countdown, GapWords.Mark(mark, facts.Round), false);
                continue;
            }

            if (state.NextCall(rules, now, facts, held: false, onAirOpensTheNextRace, out _) is { } call)
            {
                var text = TextFor(call, rules, facts, now, state);

                if (call == GapLine.TwoOut && facts.TwoOutId is { } twoOut)
                {
                    state.SaidTwoOut(twoOut, now);
                }
                else
                {
                    state.Said(call, now);
                }

                if (text.Length > 0)
                {
                    Add(call, text, false);
                }

                continue;
            }

            if (!withTheDirector)
            {
                continue;
            }

            foreach (var kind in directorOrder)
            {
                // The director's own segments are one each in a gap, as
                // the ladder only has so much due at once.
                if (kind != GapLine.Sponsor && directorSaid.Contains(kind))
                {
                    continue;
                }

                if (state.WhyNot(kind, rules, now, facts, held: false, onAirOpensTheNextRace) is null)
                {
                    directorSaid.Add(kind);
                    state.Said(kind, now);
                    Add(kind, DirectorStandIn(kind, facts), kind is GapLine.ComingUpNext or GapLine.Segment or GapLine.ClubNote);
                    break;
                }
            }
        }

        return steps;
    }

    /// <summary>The words for one of Race Control's lines, as the night would say them.</summary>
    internal static string TextFor(GapLine kind, GapRules rules, GapFacts facts, DateTime nowUtc, GapState state) => kind switch
    {
        // WHY (23 Nov 2026, bug hunt 24): CallUpSaid, not CallUps > 0 - a
        // skipped call-up made the first one said a "Reminder".
        GapLine.CallUp => GapWords.CallUp(facts, rules, repeat: state.CallUpSaid, nowUtc),
        GapLine.Marshals => GapWords.Marshals(facts, rules, nowUtc),
        GapLine.NotCheckedIn => GapWords.NotCheckedIn(facts),
        GapLine.TwoOut => GapWords.TwoOut(facts.TwoOutRound, facts.TwoOutClassName),
        GapLine.Notice => GapWords.Notice(facts, rules, nowUtc),
        GapLine.LongBreak => GapWords.LongBreak(facts, nowUtc),
        _ => string.Empty,
    };

    /// <summary>What stands in, in the preview, for a line the director would write or pick on the night.</summary>
    private static string DirectorStandIn(GapLine kind, GapFacts facts) => kind switch
    {
        GapLine.HandOver => $"That was the last race. Still to come, {GapWords.Race(facts.Round, facts.ClassName)}.",
        GapLine.ComingUpNext => $"A Coming up next line about {facts.Round.Trim()} goes here.",
        GapLine.Segment => "A segment from Through the night, like the form guide, goes here.",
        GapLine.Sponsor => "A sponsor's read goes here.",
        GapLine.ClubNote => "One of tonight's club notes goes here.",
        _ => string.Empty,
    };

    /// <summary>"hand-over", "call-up" - a kind as the running order names it.</summary>
    public static string Name(GapLine kind) => kind switch
    {
        GapLine.CallUp => "call-up",
        GapLine.TwoOut => "two races out",
        GapLine.Marshals => "marshals",
        GapLine.NotCheckedIn => "not checked in",
        GapLine.HandOver => "hand-over",
        GapLine.ComingUpNext => "Coming up next",
        GapLine.Segment => "segment",
        GapLine.Sponsor => "sponsor read",
        GapLine.ClubNote => "club note",
        GapLine.Countdown => "countdown",
        GapLine.Notice => "club notice",
        GapLine.LongBreak => "long break",
        _ => kind.ToString(),
    };

    /// <summary>"0:10 hand-over · 0:40 call-up · 0:45 marshals" - the card's written running order.</summary>
    public static string RunningOrder(IReadOnlyList<Step> steps) =>
        steps.Count == 0
            ? "Nothing would be said in this gap with these settings."
            : string.Join(" · ", steps.Select(s => $"{(int)s.At.TotalMinutes}:{s.At.Seconds:00} {Name(s.Kind)}"));
}

/// <summary>
/// "Start the countdown by itself from the run sheet" is one setting that
/// more than one control can show. Every one writes it here and hears it
/// here, so each shows what another did (10 Nov 2026, bug hunt 16). Before,
/// each read the setting once when it was built, and a card's next unrelated
/// edit saved its stale tick back over it, so the run sheet's countdown
/// calls stopped without a word.
///
/// The Timetable's Reminder countdown card had the second tick; it was taken
/// out on 13 Nov 2026, leaving the Between races card as the place to set it.
/// </summary>
internal static class AutoCountdownTick
{
    /// <summary>The setting changed, for these settings. A control showing other settings leaves its tick alone.</summary>
    public static event Action<AppSettings>? Changed;

    /// <summary>Saves the tick and tells every control showing it. Nothing happens when it already says so.</summary>
    public static void Set(AppSettings settings, bool on)
    {
        if (settings.BetweenRacesAutoCountdown == on)
        {
            return;
        }

        settings.BetweenRacesAutoCountdown = on;
        settings.Save();
        Changed?.Invoke(settings);
    }
}
