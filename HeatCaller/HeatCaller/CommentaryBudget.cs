using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// How far down the ladder a segment sits. The tier decides three separate
/// things: how much headroom it must find before it may run, how far it
/// falls back when the relay can't write it, and the order the night goes
/// quiet in when the month is running out.
///
/// Stored as an int in AppSettings - append new members at the end only.
/// </summary>
public enum SegmentTier
{
    /// <summary>Race intros and recaps. What commentary IS. Never silenced by budget.</summary>
    Essential = 0,

    /// <summary>The live feel - battles, records, what's coming up, the championship.</summary>
    Core = 1,

    /// <summary>Depth - form guides, the qualifying report, honours, the mechanic.</summary>
    Colour = 2,

    /// <summary>The furniture - cold open, this day in history, the night's numbers.</summary>
    Flourish = 3,
}

/// <summary>
/// Every kind of thing the brain can be asked to write.
///
/// Named rather than numbered in the saved state, so inserting one here
/// does not silently re-label a club's half-finished night.
/// </summary>
public enum SegmentKind
{
    RaceIntro,
    RaceRecap,
    BadgesEarned,

    BattleWatch,
    PaceNote,
    ComingUpNext,
    ChampionshipCheck,

    FormGuide,
    QualifyingReport,
    Honours,
    MechanicInterview,
    GridHistory,
    ClubAside,
    RoomVerdict,

    ColdOpen,
    ThisDayInHistory,
    SignOffNumbers,
    MidMeetingStandings,

    // Appended rather than placed beside BattleWatch, though the saved
    // state is by name: an int anywhere downstream would otherwise be
    // re-labelled, and the end of the list costs nothing.
    Horizon,

    // The rolling record watch (7 Nov 2026, known issues): a record coming,
    // where PaceNote is a record gone. Appended for Horizon's reason.
    RecordWatch,

    // The booth live in a quiet stretch (11 Nov 2026): a throw to the
    // co-host in pit lane, mid-race. Appended for Horizon's reason.
    PitLaneLive,
}

/// <summary>
/// What a segment should do when the relay won't write it.
///
/// The ladder is not "always fall back as far as you can". A flourish
/// written by a 3B model on the race laptop is worse than silence: the
/// segments only work because they sound like a broadcast, and one clumsy
/// "this day in club history" costs more of that illusion than the good
/// segments buy. So the better the line has to be, the less willing we are
/// to let something else write it.
/// </summary>
public enum BrainFallback
{
    /// <summary>Don't say it at all.</summary>
    None,

    /// <summary>Let the bundled model on this PC write it - rough phrasing beats silence for this one.</summary>
    LocalModel,

    /// <summary>Fall all the way to the fixed announcement line. This must happen.</summary>
    Templates,
}

/// <summary>
/// What a segment costs and when it is allowed to interrupt.
/// </summary>
/// <param name="Kind">Which segment.</param>
/// <param name="Tier">Where it sits on the ladder.</param>
/// <param name="Lines">Brain writes it costs - the mechanic interview is two.</param>
/// <param name="Speaks">True when it goes to the voice, which costs a second allowance and takes airtime.</param>
/// <param name="NeedsBy">How long after its trigger it is still worth saying. Past this it is dropped unsaid.</param>
/// <param name="MinGap">Won't start within this of the last spoken line.</param>
/// <param name="MaxPerMeeting">How many times a night, at most.</param>
/// <param name="MaxPerClass">How many times per class, at most.</param>
/// <param name="Title">What the settings page calls it.</param>
/// <param name="What">One line on what it does, under the tick box.</param>
/// <param name="CanBeExchange">
/// Whether this one works as a two-hander with the co-host. A fight or a
/// form guide does; a record lap does not - there is no time for a second
/// voice before the moment has gone, and "coming up next" is a continuity
/// announcement rather than a conversation.
/// </param>
public sealed record SegmentSpec(
    SegmentKind Kind,
    SegmentTier Tier,
    int Lines,
    bool Speaks,
    TimeSpan NeedsBy,
    TimeSpan MinGap,
    int MaxPerMeeting,
    int MaxPerClass,
    string Title,
    string What,
    bool CanBeExchange = false);

/// <summary>
/// The answer to "may this run right now" - and, when it may, the ticket
/// that holds its share of the budget until it is settled or let go.
/// </summary>
/// <param name="Allowed">True when the segment may be written.</param>
/// <param name="Reason">Why not, in the words the console says. Empty when allowed.</param>
/// <param name="Fallback">What to do when the relay itself then refuses.</param>
/// <param name="Ticket">The reservation to settle or release. Zero when not allowed.</param>
/// <summary>
/// WHY a segment was refused, as a value rather than as a sentence.
///
/// WHY BOTH THIS AND <see cref="BudgetVerdict.Reason"/> (12 Oct 2026). The
/// reason is a sentence written for the club's console - "only 4s since the
/// last line", "this is the top main at Chatty, which allows 9" - and it is
/// the right thing to show somebody. It is the wrong thing to COUNT, and
/// anything that counted it would have to match on its words.
///
/// This app already has one place that matches on the words of a sentence
/// it was handed (MainForm.Audio watching the announcer's text for "takes
/// the lead") and the roadmap records it as the fragile shortcut rather than
/// as a pattern to follow. A tally of a night's refusals needs to group
/// them, and naming the setting that would release one needs to know which
/// one it is, so the budget says which rather than leaving it to be read
/// back out of prose. See NightAccount.
///
/// PUBLIC because BudgetVerdict is: a public record may not name an
/// internal type, which is CS0053 and has landed four times on this project
/// in a week.
/// </summary>
public enum BudgetRefusal
{
    /// <summary>Not refused at all.</summary>
    None,

    /// <summary>The club has this segment unticked, or the tier switched off.</summary>
    SwitchedOff,

    /// <summary>Its cap for the night, or for this class, is spent.</summary>
    ItsCapForTheNight,

    /// <summary>Every fact it could be built from has already been said.</summary>
    NothingNewToSay,

    /// <summary>Too soon after the last line - see SegmentSpec.MinGap.</summary>
    TooSoonAfterTheLast,

    /// <summary>The last ten minutes are already as full as the race and the club's dial allow.</summary>
    TheAirtimeCap,

    /// <summary>The relay has as many lines in flight as it will take.</summary>
    TheRelayIsBusy,

    /// <summary>The month's allowance is down to what is held back for intros and recaps.</summary>
    TheMonthsAllowance,

    /// <summary>Not enough headroom left in the night's own budget.</summary>
    TheNightsHeadroom,

    /// <summary>Held by the Between races settings - how much, how often and when (31 Oct 2026).</summary>
    BetweenRaces,
}

/// <param name="Allowed">Whether it may be written.</param>
/// <param name="Reason">Why not, as a sentence for the club's console. Empty when allowed.</param>
/// <param name="Fallback">What to write it with - see <see cref="BrainFallback"/>.</param>
/// <param name="Ticket">The reservation to settle or release. Nought when nothing was taken.</param>
/// <param name="Why">
/// Why not, as a value - see <see cref="BudgetRefusal"/>. Defaulted so
/// nothing that builds a verdict had to change.
/// </param>
public sealed record BudgetVerdict(
    bool Allowed,
    string Reason,
    BrainFallback Fallback,
    long Ticket,
    BudgetRefusal Why = BudgetRefusal.None);

/// <summary>
/// Decides whether a commentary segment may be written right now, and in
/// what order the night goes quiet when it can't all be afforded.
///
/// WHY THIS EXISTS BEFORE THE SEGMENTS IT GOVERNS
///
/// The relay meters commentary in lines per club per month, and a club
/// night with the full set of segments can spend most of a week's worth in
/// an evening. Without a governor the failure is the worst one available:
/// the first half of a meeting is a broadcast, the mains are silent, and
/// nobody can tell why. Built first, every segment added afterwards is
/// born with a tier and a cost and degrades the same way as the rest.
///
/// FOUR THINGS ARE SCARCE, NOT ONE
///
/// Relay lines, the gap between races, voice characters, and airtime -
/// the human ceiling on how much talking an evening can stand. They run
/// out independently, so this is a set of cheap gates with the expensive
/// one last: never pay for a line that was going to be discarded.
///
/// PACED AGAINST THE PROGRAMME, NOT THE CLOCK
///
/// Nobody knows how long a race night will be when it starts, but the
/// programme knows how many rounds there are and how many have run. The
/// budget is released against that progress, so a long night and a short
/// one both finish having spent about their share.
///
/// NOTHING HERE TOUCHES A STORE OR A CLOCK IT DOESN'T OWN: every input is
/// handed in and the clock is injectable, so the whole ladder is a table
/// test rather than a race night somebody has to sit through.
///
/// The relay stays the authority on whether a club may spend at all - it
/// enforces the monthly cap itself and this cannot overrule it. This
/// decides only whether to spend NOW, because only the app knows how long
/// the night is and how long it has been since anyone heard a voice.
/// </summary>
public sealed class CommentaryBudget
{
    /// <summary>
    /// What each tier must find in headroom per line it costs.
    ///
    /// This IS the degradation. A flourish has to find two and a half
    /// times the headroom its line actually costs, so flourishes starve
    /// first, colour next, core later, and essentials never - out of one
    /// comparison rather than a mode machine with states to get wrong.
    /// </summary>
    private static double Weight(SegmentTier tier) => tier switch
    {
        SegmentTier.Essential => 1.0,
        SegmentTier.Core => 1.0,
        SegmentTier.Colour => 1.5,
        _ => 2.5,
    };

    /// <summary>
    /// How far a segment falls when the relay won't write it. See
    /// BrainFallback - the better the line has to be, the less willing we
    /// are to let the local model have a go at it.
    /// </summary>
    public static BrainFallback FallbackFor(SegmentTier tier) => tier switch
    {
        SegmentTier.Essential => BrainFallback.Templates,
        SegmentTier.Core => BrainFallback.LocalModel,
        _ => BrainFallback.None,
    };

    /// <summary>
    /// The share of the month held back for essentials alone. Below this,
    /// nothing but intros and recaps is written whatever the pace says, so
    /// a club that burns its month in week two still has an announcer for
    /// the rest of it.
    /// </summary>
    public const double EssentialReserve = 0.20;

    /// <summary>What Describe says while the model on this PC is writing in place of a spent relay.</summary>
    public const string LocalWritingSentence =
        "This month's relay lines are used up - the model on this PC is writing tonight's commentary.";

    /// <summary>
    /// The share of a night's allotment released before a single round has
    /// run. Without it progress is zero at the cold open, headroom is zero,
    /// and the night can never start talking.
    /// </summary>
    public const double OpeningFloat = 0.15;

    /// <summary>
    /// How many spoken segments a ten-minute stretch may hold, whatever the
    /// budget allows - for a race whose size is not known.
    ///
    /// STILL SIX, and still the number for a club with no programme. What
    /// changed on 11 Oct 2026 is that a race the programme CAN size moves
    /// off it: quieter in practice and the heats, half again in the top
    /// main. See <see cref="RaceStature"/>, which sets out why a cap that
    /// never moves is what made every race of an evening sound alike, and
    /// <see cref="Stature"/> for how this one is told.
    /// </summary>
    public const int AirtimeCap = 6;

    /// <summary>What the relay allows per club at once (MAX_IN_FLIGHT_PER_CLUB on its side).</summary>
    public const int RelayInFlightCap = 2;

    private static readonly TimeSpan AirtimeWindow = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Every segment's cost and manners. One table, so a new segment is one
    /// row here and cannot be added without declaring what it costs.
    /// </summary>
    private static readonly IReadOnlyDictionary<SegmentKind, SegmentSpec> Specs =
        new SegmentSpec[]
        {
            // --- Essential: the thing itself ---
            new(SegmentKind.RaceIntro,  SegmentTier.Essential, 1, true,  TimeSpan.FromSeconds(45), TimeSpan.Zero,              int.MaxValue, int.MaxValue, "Race intros", "Introduces each race as it goes to the grid."),
            new(SegmentKind.RaceRecap,  SegmentTier.Essential, 1, true,  TimeSpan.FromSeconds(60), TimeSpan.Zero,              int.MaxValue, int.MaxValue, "Race recaps", "Calls the result once the flag is out."),

            // The honours - the badges a race unlocked, read out by a third
            // voice as a segment of the recap's broadcast. See RaceHonours.
            //
            // ESSENTIAL, AND NOT BECAUSE IT IS IMPORTANT. The tier decides
            // three things and this row needs all three of the Essential
            // answers for reasons that have nothing to do with how good the
            // segment is:
            //
            //   1. NO SECOND SWITCH. Everything below Essential gets a tick
            //      box on the Commentary page built out of this table, and
            //      this segment already has one of its own in the Broadcast
            //      card beside the mechanic's - it needs to be there,
            //      because the reader's name and voice are set on the same
            //      two rows. Two switches for the same thing in different
            //      places, saying different things, is how a settings page
            //      starts lying to people; SetOff says so at length and
            //      refuses an Essential for exactly this reason.
            //
            //   2. NOTHING ASKS THE BRAIN FOR IT. CommentaryDirector.Order
            //      skips the Essentials, which is right: this one is written
            //      by HeatCaller out of the badges themselves and would only
            //      be tried, found to have no reader, and dropped. Its Lines
            //      is 1 because the table insists every row cost something,
            //      and it is never spent - nothing reserves against this
            //      kind, the same way ContinuityShow.HandOver bills itself
            //      as ComingUpNext without taking a line off the night.
            //
            //   3. NEVER SILENCED BY THE BUDGET, which here is simply true:
            //      a quiet month cannot take away a segment that costs
            //      nothing to write.
            //
            // Its deadline is the recap's, because it follows the recap: an
            // honours read a minute after the room has stopped thinking
            // about that race is a segment arriving at the wrong race. No
            // minimum gap - it is one part of a sequence that paces itself
            // (RaceControlControl.SpeakHonoursAsync waits for each line to
            // have been heard), and a gap here would be a second opinion
            // about that. Not an exchange in CanBeExchange's sense: it is
            // two voices, but the second is the honours reader rather than
            // the co-host, and nothing about it is written in one request
            // and split.
            new(SegmentKind.BadgesEarned, SegmentTier.Essential, 1, true, TimeSpan.FromSeconds(60), TimeSpan.Zero, int.MaxValue, int.MaxValue, "Achievements earned", "After a race, a third voice reads out the achievements it unlocked."),

            // --- Core: the live feel ---
            // The battle watch is the shortest-lived thing here: a line
            // about laps four to nine is worthless by lap twenty, however
            // well written, so it is thrown away rather than spoken late.
            new(SegmentKind.BattleWatch,        SegmentTier.Core, 1, true, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(40), 8,  4, "Battle watch", "Calls a fight when two cars have been locked together for a while.", true),
            // Its gap is shorter than its life, so a record lap that lands
            // just after another line can still be said before it is stale.
            new(SegmentKind.PaceNote,           SegmentTier.Core, 1, true, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(25), 10, 5, "Record laps", "Calls a lap when a record falls - the race's, the night's or the layout's."),
            // THE FORWARD-LOOKING ONE (15 Oct 2026) - what a gap is doing
            // and how much road is left: "half a second to find, three laps
            // to find it, taking two tenths a lap". See RaceHorizon.
            //
            // As short-lived as the battle watch and for the same reason -
            // a rate read on lap six is wrong by lap nine - with a gap a
            // little longer than its life, so it does not land on top of the
            // battle call about the same two cars. Four a race at most, and
            // RaceHorizon's own two-minute cooldown per pair keeps those
            // four about different fights.
            //
            // ONE VOICE, NOT A TWO-HANDER. A second voice asked to react to
            // "he has three laps to find half a second" reaches for "and
            // he'll do it", which is the one sentence RaceHorizon exists
            // never to say.
            new(SegmentKind.Horizon,            SegmentTier.Core, 1, true, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), 8,  4, "What's still to decide", "Points the room at a gap that is coming down - or going out - with the laps running out."),
            // A RECORD COMING, BEFORE IT FALLS (7 Nov 2026, known issues) -
            // RecordWatch, heard in the console only since 22 Oct. Pace, not
            // one lap: two of the last three inside the band. It escalates
            // at most three times per driver per race and never goes back
            // down, so six a meeting and three a class is room for two
            // drivers' stories. Its life is short for the battle watch's
            // reason - "on the doorstep" is stale three laps later.
            new(SegmentKind.RecordWatch,        SegmentTier.Core, 1, true, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30), 6,  3, "Record in danger", "Calls a driver whose last few laps are closing on the layout record - before it falls."),
            new(SegmentKind.ComingUpNext,       SegmentTier.Core, 1, true, TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(20), 12, 12, "Coming up next", "Says what is on next, in the gap between races."),
            new(SegmentKind.ChampionshipCheck,  SegmentTier.Core, 1, true, TimeSpan.FromMinutes(3),  TimeSpan.FromSeconds(20), 6,  1, "Championship check", "Where the season stands, after a class's last main.", true),

            // --- Colour: depth ---
            new(SegmentKind.FormGuide,         SegmentTier.Colour, 1, true, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(30), 6, 2, "Form guide", "How the drivers in a main have been going lately.", true),
            new(SegmentKind.QualifyingReport,  SegmentTier.Colour, 1, true, TimeSpan.FromMinutes(3),  TimeSpan.FromSeconds(30), 6, 1, "Qualifying report", "What qualifying decided, before the mains go out.", true),
            new(SegmentKind.Honours,           SegmentTier.Colour, 1, true, TimeSpan.FromMinutes(2),  TimeSpan.FromSeconds(45), 5, 3, "Milestones", "A hundredth race, a first podium, a club record."),
            new(SegmentKind.MechanicInterview, SegmentTier.Colour, 2, true, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(15), 6, 2, "Technical Lead's interview", "The announcer asks the winning car's Technical Lead two questions.", true),
            // THE BOOTH LIVE IN A QUIET STRETCH (11 Nov 2026, voice speed
            // plan, phase 4 #7, asked for by Asher). Once a race at most, in
            // a stretch with nothing being said and no fight within a second,
            // never in the last two laps - see CommentaryDirector's
            // PitLaneLive case. Always a two-hander: it is a throw to pit
            // lane, so with no co-host it is not written at all. Arrives
            // switched OFF (AppSettings.ArrivePitLaneLiveOff): it spends the
            // month's relay lines in a race, where the other segments are
            // what the race gives them.
            new(SegmentKind.PitLaneLive,       SegmentTier.Colour, 2, true, TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(30), 8, 4, "Pit lane, live", "In a quiet stretch of a race, the announcer throws to the co-host in pit lane for a quick word about one driver's car or team. Once a race at most, never in the last two laps. Spends relay lines.", true),
            // THE GRID HISTORY, RETUNED 10 Oct 2026 - from 8 a meeting and
            // 4 a class down to 3 and 1, with its gap trebled.
            //
            // It was built when it was the ONLY route to any driver
            // history: CommentaryHistoryFacts.ForIntro had exactly one
            // caller, this case, and a debut, a rivalry, a drought and a
            // run of form could all be heard only through it. Eight a
            // night was generous for that job and still not enough - on
            // rehearsal 5 (10 Oct 2026) the log carried "Skipped grid
            // history - 6 lines in the last ten minutes already" roughly
            // four hundred times, so the one segment carrying every piece
            // of driver history was refused all evening after the first
            // few, and nothing else had any history to give.
            //
            // The race intro now carries that same history itself, capped
            // at four facts, costing no allowance at all because it rides
            // inside a request the intro was making anyway - and keyed on
            // the race DAY, so SaidTonight treats a fact the intro has
            // spent as spent here too. That leaves this segment a genuine
            // but much smaller job: the history the intro did not get to
            // (it takes four facts and only for the drivers its own facts
            // name), said in a gap of its own.
            //
            // So it is a top-up and is budgeted as one. Three a night and
            // one per class, with ninety seconds' clearance rather than
            // thirty - the old gap could put a history line in the air
            // seconds after the intro's own history, which is the
            // repetition this whole change is about. Retuned rather than
            // retired: the member is on the Commentary page as a tick box,
            // in ShowBlock's captions and in ShowTreatment's list, and a
            // segment that still has something true and unsaid to offer is
            // not worth breaking three other files to remove.
            new(SegmentKind.GridHistory,       SegmentTier.Colour, 1, true, TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(90), 3, 1, "Grid history", "A debut or a rivalry the race intro did not get to.", true),

            // THE ONE SEGMENT WHOSE SUBJECT IS THE ROOM (12 Oct 2026).
            //
            // MaxPerClass is int.MaxValue and not 1, which every other
            // Colour row sets: a note is not ABOUT a class, and six
            // dedications on a night when a club ran one class would
            // otherwise become one. Six per meeting is the real cap, and
            // it is high because the club decides how many notes there
            // are by typing them - the budget's job here is only to stop
            // an evening becoming a message service.
            //
            // NOT a two-hander. A dedication is sincere; two commentators
            // bantering over "Jack's first race since his operation" is
            // the one place this app's second voice would be actively
            // unwelcome.
            new(SegmentKind.ClubAside,         SegmentTier.Colour, 1, true, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(45), 6, int.MaxValue, "Club notes", "A dedication, a birthday, the raffle - whatever the night has that the timing cannot know."),

            // A TWO-HANDER, and the only Colour segment that is. The
            // room being wrong is something for two commentators to enjoy
            // at each other - which is the opposite of the club aside
            // above, where a second voice would be unwelcome.
            //
            // ONCE PER CLASS, because the vote is opened per race and a
            // club that voted on every heat would turn the best line of
            // the night into a running feature. Six a meeting is a cap on
            // a club that ran six classes, not a target.
            new(SegmentKind.RoomVerdict,       SegmentTier.Colour, 2, true, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(30), 6, 1, "The room's verdict", "What the room voted would happen, held up against what did.", true),

            // --- Flourish: the furniture ---
            new(SegmentKind.ColdOpen,            SegmentTier.Flourish, 1, true, TimeSpan.FromMinutes(5), TimeSpan.Zero,             1, 1, "Tonight Open", "Sets the evening out at the first On Air, then hands on to the show open.", true),
            new(SegmentKind.ThisDayInHistory,    SegmentTier.Flourish, 1, true, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(60),  1, 1, "This day at the club", "What happened here on this date in other years."),
            new(SegmentKind.SignOffNumbers,      SegmentTier.Flourish, 1, true, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(15),  1, 1, "Sign-off numbers", "The night's totals, fastest lap and closest finish.", true),
            new(SegmentKind.MidMeetingStandings, SegmentTier.Flourish, 1, true, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(60),  2, 2, "Halfway standings", "Where things stand partway through the heats."),
        }
        .ToDictionary(s => s.Kind);

    /// <summary>What a segment costs and when it may interrupt. See Specs.</summary>
    public static SegmentSpec Spec(SegmentKind kind) => Specs[kind];

    /// <summary>Every segment there is, for the settings page and for tests that must cover them all.</summary>
    public static IReadOnlyList<SegmentSpec> AllSpecs { get; } = Specs.Values.ToList();

    private readonly Func<DateTime> _clock;

    /// <summary>
    /// Taken by every member that reads or writes the night.
    ///
    /// The director's pass leaves the UI thread at its first await, so
    /// from then on Reserve, Settle and Release run on a thread-pool
    /// thread, while the UI thread's half-second tick is reading and
    /// rewriting the same collections - and the relay's count arrives on
    /// whichever thread its answer came back on. Plain dictionaries
    /// written from two threads at once can be corrupted or throw, and a
    /// switched-off segment could be judged in the gap between a clear and
    /// its re-add. Nothing in here waits on anything while holding it.
    /// </summary>
    private readonly object _gate = new();

    // --- What the relay last told us. Zero allowance means "not heard
    // from yet", which is deliberately treated as no budget gate at all:
    // refusing to write before we know what the club has would silence a
    // club with plenty, and the relay enforces the real cap regardless.
    private int _usedThisMonth;
    private int _allowance;

    // What the relay's count stood at before anything this laptop wrote
    // tonight - see NightAllotment. Null until the relay has said anything
    // tonight; cleared by a new night.
    private int? _usedBeforeTonight;

    // Set once Restore has read the saved night for the current day - see
    // Restore, which is called on a half-second tick.
    private bool _nightRestored;

    // --- The night ---
    private string _dayId = string.Empty;
    private int _roundsRun;
    private int _roundsTotal;
    private double _spentTonight;
    private double _entitledHighWater;

    // The part of _spentTonight the model on this PC wrote while the
    // relay's month was used up (19 Oct 2026). Those lines cost the club
    // no relay lines, so they are kept out of the night's headroom: a
    // top-up landing mid-night must find the night's share of it still
    // there, not already eaten by the local lines written before it came.
    // 6 Nov 2026, Bug hunt 11b: saved with the night (SavedNight.Free).
    // It used to be in memory only, on the idea that charging them again
    // after a restart was merely cautious - but MonthlyUsage seeds where
    // the month stood before tonight from the relay's count minus what
    // tonight has charged, and the relay never counted these lines, so the
    // seed came out low and the rest of the night was mis-paced.
    private double _freeTonight;

    private readonly HashSet<SegmentKind> _off = new();
    private readonly Dictionary<SegmentKind, int> _perMeeting = new();
    private readonly Dictionary<string, int> _perClass = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DateTime> _spokenAt = new();
    private readonly Dictionary<long, Reservation> _open = new();

    /// <summary>
    /// The reservations in <see cref="_open"/> whose line has come back and
    /// is waiting to be SAID - see <see cref="Written"/>. Still held, so
    /// still refundable, but no longer a request in the air.
    /// </summary>
    private readonly HashSet<long> _written = new();

    private long _nextTicket = 1;
    private int _nightsLeft = 4;
    private DateTime? _lastSpoken;

    /// <summary>
    /// A line held but not yet written. Holds its LINES, not its weighted
    /// cost: the weight is the gate a tier has to clear, the lines are the
    /// real money, and a hold has to be worth exactly what settling it
    /// will cost or the books don't balance.
    /// </summary>
    /// <param name="Free">Taken while the model on this PC was writing the spent month - see _freeTonight.</param>
    private sealed record Reservation(SegmentKind Kind, string? ClassName, int Lines, bool Free = false);

    public CommentaryBudget(Func<DateTime>? clock = null) => _clock = clock ?? (() => DateTime.Now);

    /// <summary>
    /// How many more race days this month the allotment has to stretch
    /// over. Read from the club's schedule where there is one; four - a
    /// weekly club - where there isn't. Never below one, or the first night
    /// of the month would be handed the whole allowance.
    /// </summary>
    public int NightsLeftThisMonth
    {
        get => _nightsLeft;
        set => _nightsLeft = Math.Max(1, value);
    }

    /// <summary>
    /// About how many of a month's race days are still to come, tonight's
    /// included, for a club that races <paramref name="nightsPerMonth"/>
    /// times a month (14 Oct 2026).
    ///
    /// WHY THIS IS WORKED OUT AND NOT JUST THE SETTING. NightAllotment
    /// divides what is left of the month by this, and it used to be handed
    /// the nights-per-month figure as it stood - which never counts down.
    /// So the last night of the month got a quarter of what was left rather
    /// than all of it: 400 lines came out as 100, 75, 56 and 42, and about
    /// a third of the allowance was never spent at all.
    ///
    /// The club's nights spread evenly over the month, counted from today:
    /// a weekly club on the 22nd of a 31-day month has ten days left, which
    /// is about 1.3 of its four nights - so two, tonight and next week.
    /// Rounded UP so a night is never handed more than its share, never
    /// more than the setting, and never below one, or the last night of the
    /// month would divide by nothing.
    /// </summary>
    public static int NightsLeft(int nightsPerMonth, DateTime today)
    {
        var perMonth = Math.Max(1, nightsPerMonth);
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
        var daysLeft = daysInMonth - today.Day + 1;
        var left = (int)Math.Ceiling(perMonth * (double)daysLeft / daysInMonth);

        return Math.Clamp(left, 1, perMonth);
    }

    /// <summary>
    /// The operator's override for a meeting that matters - a regional, a
    /// club's open day. Spends against next week on purpose, so it is a
    /// tick somebody makes rather than a default.
    /// </summary>
    public bool KeepEverythingRunning { get; set; }

    /// <summary>True when this club has switched this segment off for good. See SetOff.</summary>
    public bool IsOff(SegmentKind kind)
    {
        lock (_gate)
        {
            return _off.Contains(kind);
        }
    }

    /// <summary>
    /// Everything this club has switched off, for the settings page and for
    /// saving. A copy, so a caller enumerating it cannot meet a change made
    /// on another thread halfway through.
    /// </summary>
    public IReadOnlyCollection<SegmentKind> SwitchedOff
    {
        get
        {
            lock (_gate)
            {
                return _off.ToArray();
            }
        }
    }

    /// <summary>
    /// Switches one segment off for good, or back on.
    ///
    /// Not a budget decision and nothing to do with the ladder: a club
    /// that never wants a cold open says so once and never hears one,
    /// however quiet the month is.
    ///
    /// An essential cannot be switched off here. Race intros and recaps
    /// have their own tick boxes under "What it writes", which decide
    /// whether anything asks for one at all - and a second switch for the
    /// same thing, in a different place, saying something different, is
    /// how a settings page starts lying to people.
    /// </summary>
    public void SetOff(SegmentKind kind, bool off)
    {
        if (Spec(kind).Tier == SegmentTier.Essential)
        {
            return;
        }

        lock (_gate)
        {
            if (off)
            {
                _off.Add(kind);
            }
            else
            {
                _off.Remove(kind);
            }
        }
    }

    /// <summary>
    /// The switched-off segments as AppSettings holds them.
    ///
    /// The names of what is OFF rather than what is on, and names rather
    /// than numbers. Both matter: a segment added in a later version is
    /// then on by default - a club should not have to go and find a tick
    /// box to get something they already pay for - and inserting a member
    /// into SegmentKind cannot silently re-label what somebody switched
    /// off last season.
    /// </summary>
    public static string SaveSwitches(IEnumerable<SegmentKind> off) => string.Join(",", off.Distinct().Select(k => k.ToString()));

    /// <summary>
    /// Reads that list back. Anything unrecognised is ignored rather than
    /// throwing - a setting written by a newer version that has since been
    /// rolled back must not stop the announcer.
    /// </summary>
    public static IReadOnlyCollection<SegmentKind> ParseSwitches(string? saved)
    {
        var off = new HashSet<SegmentKind>();

        if (string.IsNullOrWhiteSpace(saved))
        {
            return off;
        }

        foreach (var name in saved.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<SegmentKind>(name, ignoreCase: true, out var kind)
                && Specs.ContainsKey(kind)
                && Specs[kind].Tier != SegmentTier.Essential)
            {
                off.Add(kind);
            }
        }

        return off;
    }

    /// <summary>Applies a saved list of switched-off segments. See SaveSwitches.</summary>
    public void RestoreSwitches(string? saved)
    {
        var off = ParseSwitches(saved);

        lock (_gate)
        {
            _off.Clear();

            foreach (var kind in off)
            {
                _off.Add(kind);
            }
        }
    }

    /// <summary>
    /// The lowest tier this club wants at all. A club that never wants the
    /// furniture sets this to Colour and the flourishes are gone for good,
    /// rather than turning up whenever the month happens to be quiet.
    /// </summary>
    public SegmentTier LowestTier { get; set; } = SegmentTier.Flourish;

    /// <summary>
    /// Asked before paying for a line: true when every fact this segment
    /// would use has already been spent tonight.
    ///
    /// Supplied by the fact layer rather than owned here - the budget
    /// knows what things cost, not what has been said about whom. Null
    /// means nothing is tracking it yet and the question is skipped.
    /// </summary>
    public Func<SegmentKind, string?, bool>? NothingNewToSay { get; set; }

    /// <summary>How many brain requests are in the air right now. See RelayInFlightCap.</summary>
    public int InFlight
    {
        get
        {
            lock (_gate)
            {
                // Lines written and waiting to be said are not in the air.
                return _open.Count - _written.Count;
            }
        }
    }

    /// <summary>Lines written tonight, settled only - a reservation in flight does not count.</summary>
    public int SpentTonight
    {
        get
        {
            lock (_gate)
            {
                return (int)Math.Round(_spentTonight);
            }
        }
    }

    /// <summary>What the club has left this month, as the relay last reported it. Zero when it hasn't said.</summary>
    public int RemainingThisMonth
    {
        get
        {
            lock (_gate)
            {
                return _allowance <= 0 ? 0 : Math.Max(0, _allowance - _usedThisMonth);
            }
        }
    }

    /// <summary>True once the relay has told us what the club's allowance is. Before that there is no budget gate.</summary>
    public bool AllowanceKnown => _allowance > 0;

    // WHY (23 Oct 2026, bug hunt 10): talking points outside the budget need
    // the allowance to honour the essential reserve the director keeps.
    /// <summary>The club's month allowance, as the relay last reported it. Zero when it hasn't said.</summary>
    public int MonthAllowance
    {
        get
        {
            lock (_gate)
            {
                return _allowance;
            }
        }
    }

    /// <summary>
    /// Asked whether the model on this PC is writing because the relay's
    /// lines for the month are used up - FallbackCommentaryBrain.TakingOver,
    /// handed in by the director. Null, or false, is today's budget.
    ///
    /// WHY THE BUDGET HAS TO KNOW (19 Oct 2026). Two of its gates exist only
    /// to make relay lines last the month: the reserve held for intros and
    /// recaps (TheMonthsAllowance) and the night's share of what is left
    /// (TheNightsHeadroom). With the month spent, both are shut for good -
    /// nothing is left and the night's share of nothing is nothing - so the
    /// live segments the local model is there to write were refused before
    /// it was ever asked, and the switch-over would have carried the intros
    /// and recaps and nothing else. While the local model is writing, those
    /// two gates step aside for the tiers it may write (Core); every other
    /// gate - the airtime cap, the gaps, the night's caps - is untouched, so
    /// the night is paced exactly as before. Colour and flourish stay
    /// relay-only (BrainFallback.None) and are refused for the month here,
    /// rather than reserved, sent and left unsaid one by one.
    ///
    /// Asked BEFORE _gate is taken, never under it (AskLocal): it reaches
    /// into the relay client and the usage store, each with a lock of its
    /// own, and a budget lock held across someone else's is how two threads
    /// end up waiting for each other mid-race.
    /// </summary>
    public Func<bool>? LocalModelTakesOver { get; set; }

    /// <summary>LocalModelTakesOver, asked safely. Never with _gate held - see there.</summary>
    private bool AskLocal()
    {
        try
        {
            return LocalModelTakesOver?.Invoke() == true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The month is spent by the relay's own count and the model on this PC
    /// is writing in its place - see LocalModelTakesOver, whose answer is
    /// handed in as <paramref name="localNow"/>. Called with _gate held.
    /// </summary>
    private bool LocalWritingTheMonth(bool localNow) =>
        localNow && _allowance > 0 && _usedThisMonth >= _allowance;

    /// <summary>True while the relay's month is used up and the model on this PC is writing tonight's commentary. See LocalModelTakesOver.</summary>
    public bool LocalModelWriting
    {
        get
        {
            var localNow = AskLocal();

            lock (_gate)
            {
                return LocalWritingTheMonth(localNow);
            }
        }
    }

    /// <summary>
    /// How much of tonight's programme has run, and how much of it there
    /// is, as NightShape was last told - what the entitlement is released
    /// against. Readable so a settings page can show where the night is
    /// up to, and so a test can check the reader reached here at all.
    /// </summary>
    public int RoundsRun => _roundsRun;

    /// <inheritdoc cref="RoundsRun"/>
    public int RoundsTotal => _roundsTotal;

    /// <summary>
    /// About how many lines tonight may have. Shown to the operator as
    /// "about", because the arithmetic behind it moves as the month does.
    ///
    /// The month AS IT STOOD BEFORE TONIGHT, not as it stands now. The
    /// relay's count goes up with every line this laptop writes, and
    /// Headroom takes tonight's lines off again, so dividing what is left
    /// now charged every line twice: a 100-line share topped out at about
    /// 80, and "of about N lines" shrank as the night talked. What is
    /// added back is only what the relay's count has actually moved since
    /// tonight began, and never more than this laptop has spent and holds,
    /// so a second machine's lines and a line Ollama wrote still come off.
    /// </summary>
    public double NightAllotment
    {
        get
        {
            lock (_gate)
            {
                if (_allowance <= 0)
                {
                    return 0;
                }

                // Relay lines only: what the model on this PC wrote for a spent
                // month never moved the relay's count - see _freeTonight.
                var mine = ChargedLines();
                var moved = _usedBeforeTonight is { } before ? Math.Max(0, _usedThisMonth - before) : 0;
                var usedBeforeTonight = Math.Max(0, _usedThisMonth - Math.Min(mine, moved));

                return Math.Max(0, _allowance - usedBeforeTonight) / NightsLeftThisMonth;
            }
        }
    }

    /// <summary>
    /// What the night has earned the right to spend so far - its allotment
    /// released against how much of the programme has run.
    ///
    /// Only ever goes up. Seeding the mains mid-evening makes the
    /// programme longer, which pushes progress backwards; without the high
    /// water mark the budget would claw itself back and the night would go
    /// quiet at exactly the point it gets interesting.
    /// </summary>
    public double Entitled
    {
        get
        {
            lock (_gate)
            {
                if (_allowance <= 0)
                {
                    return double.PositiveInfinity;
                }

                var progress = _roundsTotal <= 0
                    ? 0
                    : Math.Clamp((double)_roundsRun / _roundsTotal, 0, 1);

                // The opening share is the dial's (3 Nov 2026): Non-stop
                // releases the whole night at once - see ShowChatter.
                var opening = ShowChatter.OpeningFloatFor(Chatter);
                var released = NightAllotment * (opening + ((1 - opening) * progress));

                return Math.Max(_entitledHighWater, released);
            }
        }
    }

    /// <summary>What is left of the night's entitlement, after what is settled and what is reserved.</summary>
    public double Headroom
    {
        get
        {
            lock (_gate)
            {
                return Entitled - ChargedLines();
            }
        }
    }

    /// <summary>What tonight has cost in relay lines, settled and held - everything but the free local lines (see _freeTonight). Called with _gate held.</summary>
    private double ChargedLines() =>
        Math.Max(0, _spentTonight - _freeTonight) + _open.Values.Where(r => !r.Free).Sum(r => r.Lines);

    /// <summary>
    /// The relay's own count, taken from its answer to a write. The club's
    /// true figures, not an estimate kept on this laptop, so a club that
    /// races on two machines still paces against one month.
    /// </summary>
    public void MonthlyUsage(int used, int allowance)
    {
        if (allowance <= 0)
        {
            return;
        }

        lock (_gate)
        {
            _usedThisMonth = Math.Max(0, used);
            _allowance = allowance;

            // The first word from the relay tonight fixes where the month
            // stood before this laptop wrote anything - its count already
            // includes what is settled and what is in flight, including a
            // night picked back up after a restart. See NightAllotment.
            // WHY (23 Oct 2026, bug hunt 10): the relay's month can roll over
            // mid-night (it counts in UTC), and its count then falls below
            // where tonight began. Start tonight's new-month lines from the
            // new count, or they are counted twice in NightAllotment.
            if (_usedBeforeTonight is { } before && _usedThisMonth < before)
            {
                _usedBeforeTonight = _usedThisMonth;
            }

            _usedBeforeTonight ??= Math.Max(0, _usedThisMonth - (int)Math.Round(ChargedLines()));
        }
    }

    /// <summary>How much of tonight's programme has run. Called whenever the programme is refreshed.</summary>
    public void NightShape(int roundsRun, int roundsTotal)
    {
        lock (_gate)
        {
            _roundsRun = Math.Max(0, roundsRun);
            _roundsTotal = Math.Max(0, roundsTotal);

            // Read once here so the high water mark keeps up as the night runs,
            // rather than only when something asks to be written.
            var earned = Entitled;

            _entitledHighWater = Math.Max(_entitledHighWater, double.IsInfinity(earned) ? 0 : earned);
        }
    }

    /// <summary>
    /// A new race day. Everything counted per night starts again; what the
    /// relay has told us about the month does not.
    /// </summary>
    public void NewMeeting(string dayId)
    {
        lock (_gate)
        {
            StartNight(dayId);
        }
    }

    /// <summary>NewMeeting's body. Called with _gate held.</summary>
    private void StartNight(string dayId)
    {
        if (string.Equals(_dayId, dayId, StringComparison.Ordinal))
        {
            return;
        }

        _dayId = dayId ?? string.Empty;
        _roundsRun = 0;
        _roundsTotal = 0;

        // A fresh night knows nothing about what is on it yet. Without
        // this, a new day opened straight after an A-main would pace its
        // first practice session as a final.
        Stature = RaceStature.Unknown;

        // THE DIAL IS NOT RESET. It is the club's own setting rather than
        // anything about tonight, and a new day opened mid-evening must not
        // quietly put the announcer back to Standard - see ShowChatter.

        _spentTonight = 0;
        _freeTonight = 0;
        _entitledHighWater = 0;
        _lastSpoken = null;
        _usedBeforeTonight = null;
        _nightRestored = false;

        _perMeeting.Clear();
        _perClass.Clear();
        _spokenAt.Clear();
        _open.Clear();
        _written.Clear();
    }

    /// <summary>
    /// May this segment be written right now, and at what cost - without
    /// taking anything. For the settings page and for a caller that wants
    /// to know before it goes to the trouble of building the facts.
    /// </summary>
    public BudgetVerdict Peek(SegmentKind kind, string? className = null)
    {
        var localNow = AskLocal();

        lock (_gate)
        {
            return Judge(kind, className, take: false, localNow);
        }
    }

    /// <summary>
    /// Whether the asking is happening INSIDE a programme - the post-race
    /// sequence, which is a run of segments back to back by design.
    ///
    /// Set for the length of one ask and cleared again; see
    /// <see cref="InsideAProgramme"/> for the one gate it lifts and the
    /// several it does not.
    /// </summary>
    private bool _insideAProgramme;

    /// <summary>
    /// How big the race in front of the announcer is - see
    /// <see cref="RaceStature"/>. <see cref="RaceStature.Unknown"/> until
    /// something says otherwise, which is the flat cap this has always had.
    ///
    /// A PROPERTY SET ON A TICK, not a constructor argument and not an
    /// event. The round under the announcer changes while the app runs -
    /// a club picks a different one, seeds the mains mid-evening, drops
    /// back to a freeform race - and a value taken once at construction
    /// would have the budget pacing the A-main as whatever was selected
    /// when the announcer screen first opened. Read on every judgement, so
    /// there is nothing in here that can go stale.
    ///
    /// Volatile rather than locked: it is a small readonly struct written
    /// from the UI thread and read from whichever thread is judging, and
    /// nothing downstream cares whether it sees this race's value or the
    /// last one's for a single ask. A lock here would be a lock taken on
    /// every segment of every race for a value that cannot tear.
    /// </summary>
    public RaceStature Stature
    {
        get
        {
            // ONE READ, so the two halves cannot disagree - see _stature.
            var packed = _stature;

            return new((RaceBilling)(packed >> 1), (packed & 1) == 1);
        }

        set => _stature = Packed(value.Billing, value.LastOfTheNight);
    }

    /// <summary>
    /// How much the club wants said - the key of one of
    /// <see cref="ShowChatter.All"/>. Blank is Standard, which is the
    /// pacing exactly as it shipped.
    ///
    /// SEPARATE FROM <see cref="Stature"/> ON PURPOSE (12 Oct 2026). The
    /// stature is a fact about the race in front of the announcer, read off
    /// the club's programme. This is a matter of taste, typed on the
    /// Commentary page. Folding the two together would mean a club turning
    /// the dial up had to be modelled as every race being bigger than it
    /// is, and then the log could not say which of the two had moved.
    ///
    /// A string rather than the enum, and volatile for the same reason the
    /// stature's halves are: set from the UI thread, read from whichever
    /// thread is judging, and a reference assignment cannot tear. An
    /// unknown key reads as Standard, so a settings file from a later
    /// build cannot pick a volume nobody chose.
    /// </summary>
    public string Chatter
    {
        get => _chatter;
        set => _chatter = value ?? string.Empty;
    }

    private volatile string _chatter = string.Empty;

    /// <summary>
    /// <see cref="Stature"/>'s two halves in ONE volatile int, billing in
    /// the high bits and last-of-the-night in bit zero.
    ///
    /// WHY NOT TWO FIELDS, which is what this was (13 Oct 2026). A struct
    /// cannot be volatile in C#, so the parts were held apart - and the
    /// comment here weighed the cost honestly: "the worst a reader can see
    /// is one half of a change that landed between its two reads." True,
    /// and it chose that over "a lock taken for every segment of every
    /// race all evening." Both halves of that trade were real.
    ///
    /// It just was not the only choice available. Two values that fit in
    /// an int together can be read and written as one, which is atomic
    /// without being locked - so the reader can no longer see the billing
    /// of the race in front of it beside last-night's last-of-the-night
    /// flag, and nothing waits for anything. An enum over int and a bool
    /// are 33 bits in principle; RaceBilling has a handful of members, so
    /// shifting by one loses nothing that exists.
    ///
    /// Set from the UI thread as the round under the announcer changes,
    /// read from whichever thread is gathering a segment.
    /// </summary>
    private volatile int _stature = Packed(RaceBilling.Unknown, false);

    /// <summary>The two halves as one int - see <see cref="_stature"/>.</summary>
    private static int Packed(RaceBilling billing, bool lastOfTheNight) =>
        ((int)billing << 1) | (lastOfTheNight ? 1 : 0);

    /// <summary>
    /// Asks as part of a programme rather than as filler between races,
    /// which lifts ONE gate: the minimum gap since the last spoken line.
    ///
    /// WHY THAT GATE IS WRONG INSIDE A PROGRAMME. It exists so the room is
    /// not talked at continuously between races - a perfectly good rule for
    /// filler, which is what the ladder normally feeds. A programme is the
    /// opposite thing: the recap, the honours, the pit lane and the sign-off
    /// are deliberately back to back, and the mark between them is the
    /// pacing. Asked from inside one, the gap has by definition just been
    /// closed by the programme's own previous line, so every colour segment
    /// was refused with "only 2s since the last line" - and then became due
    /// the moment the programme went quiet, which is precisely how they came
    /// to play AFTER the sign-off as shows of their own.
    ///
    /// WHAT IS STILL ENFORCED, and this is the point of lifting one gate
    /// rather than reaching for KeepEverythingRunning: switched off, already
    /// said tonight, already said for this class, nothing new to say, the
    /// ten-minute airtime cap, the relay being busy, and the whole of the
    /// month's allowance. A programme gets to be a programme; it does not
    /// get to be free.
    /// </summary>
    public BudgetVerdict ReserveForProgramme(SegmentKind kind, string? className = null)
    {
        var localNow = AskLocal();

        lock (_gate)
        {
            _insideAProgramme = true;

            try
            {
                return Judge(kind, className, take: true, localNow);
            }
            finally
            {
                _insideAProgramme = false;
            }
        }
    }

    /// <summary>
    /// May this segment be written right now - and if so, hold its share
    /// until it is settled or let go.
    ///
    /// Reserved rather than simply counted because the relay allows two
    /// requests per club at once: two segments can both find the last line
    /// of headroom in the same moment, and the second one must not. Mirrors
    /// the relay's own reserve-then-refund for the same reason.
    /// </summary>
    public BudgetVerdict Reserve(SegmentKind kind, string? className = null)
    {
        var localNow = AskLocal();

        lock (_gate)
        {
            return Judge(kind, className, take: true, localNow);
        }
    }

    /// <summary>The line was written. The reservation becomes spend, and the airtime clock restarts.</summary>
    public void Settle(long ticket, bool spoken = true)
    {
        lock (_gate)
        {
            _written.Remove(ticket);

            if (!_open.Remove(ticket, out var held))
            {
                return;
            }

            _spentTonight += held.Lines;

            if (held.Free)
            {
                _freeTonight += held.Lines;
            }

            if (!spoken)
            {
                return;
            }

            var now = _clock();

            _lastSpoken = now;
            _spokenAt.Add(now);

            Trim(now);
        }
    }

    /// <summary>
    /// The relay charged this many lines for the segment (7 Nov 2026, Bug
    /// hunt 13). A two-hander that came back untagged is asked for again in
    /// one voice, and the relay keeps both charges, where the reservation
    /// held only the segment's own share. Grows the held share to match, so
    /// the settle or the release that follows counts what the relay counted.
    /// Never shrinks it, and unknown tickets are ignored.
    /// </summary>
    public void Charged(long ticket, int lines)
    {
        lock (_gate)
        {
            if (_open.TryGetValue(ticket, out var held) && lines > held.Lines)
            {
                _open[ticket] = held with { Lines = lines };
            }
        }
    }

    /// <summary>
    /// The line has come back from the writer and is waiting for the host
    /// to say it (14 Oct 2026). Still held - it is settled when it is SAID
    /// and released if the host drops it - but no longer counted as a
    /// request in the air, so a programme writing two or three colour
    /// segments ahead of saying them is not turned away by
    /// RelayInFlightCap for lines that finished with the relay long ago.
    /// Unknown tickets are ignored.
    /// </summary>
    public void Written(long ticket)
    {
        lock (_gate)
        {
            if (_open.ContainsKey(ticket))
            {
                _written.Add(ticket);
            }
        }
    }

    /// <summary>
    /// A segment already settled as said turned out not to be heard - the
    /// voice threw it away after the director had let it go (3 Nov 2026).
    /// Gives back its turn for the night and for the class, as
    /// <see cref="Release"/> does; the lines stay spent, because the relay
    /// kept them.
    /// </summary>
    public void GiveBackTurn(SegmentKind kind, string? className)
    {
        lock (_gate)
        {
            if (_perMeeting.TryGetValue(kind, out var meeting) && meeting > 0)
            {
                _perMeeting[kind] = meeting - 1;
            }

            if (className is { Length: > 0 })
            {
                var key = Key(kind, className);

                if (_perClass.TryGetValue(key, out var forClass) && forClass > 0)
                {
                    _perClass[key] = forClass - 1;
                }
            }
        }
    }

    /// <summary>
    /// The segment will not be said. Its once-a-night and per-class turns
    /// are always given back, so it can still be said later tonight.
    ///
    /// The lines are given back only when nothing was written (the relay
    /// refused, the write failed, or the facts turned out to be nothing):
    /// the relay refunds its own count only for an empty answer or a failed
    /// write. Pass <paramref name="written"/> true when a line DID come
    /// back and is being dropped (too late, held, the race moved on, the
    /// host dropped it) - the relay kept that charge, so tonight keeps it
    /// too, as a settle that was not spoken.
    /// </summary>
    public void Release(long ticket, bool written = false)
    {
        lock (_gate)
        {
            _written.Remove(ticket);

            if (!_open.Remove(ticket, out var held))
            {
                return;
            }

            // WHY (23 Oct 2026, bug hunt 10): a line the relay wrote and the
            // app then dropped was handed back whole, but the relay keeps the
            // charge for any line it returned (brain.ts write refunds only an
            // empty answer or a throw). Tonight's headroom and SpentTonight
            // under-counted every dropped line, the director wrote another at
            // once, and the month ran out before its last nights. A written
            // drop is now charged like an unspoken settle - the airtime clock
            // is left alone, because nothing was said.
            if (written)
            {
                _spentTonight += held.Lines;

                if (held.Free)
                {
                    _freeTonight += held.Lines;
                }
            }

            // The counters are taken at reserve time so two requests in the
            // air cannot both be the last one allowed. A release has to give
            // them back, or a relay timeout would spend a segment's only turn
            // of the night without a word being said.
            if (_perMeeting.TryGetValue(held.Kind, out var meeting) && meeting > 0)
            {
                _perMeeting[held.Kind] = meeting - 1;
            }

            if (held.ClassName is { Length: > 0 } className)
            {
                var key = Key(held.Kind, className);

                if (_perClass.TryGetValue(key, out var perClass) && perClass > 0)
                {
                    _perClass[key] = perClass - 1;
                }
            }
        }
    }

    /// <summary>
    /// The one line the Commentary page shows about tonight.
    ///
    /// Deliberately says "about": the allotment moves with the month and a
    /// number presented as exact would be wrong by the second race.
    /// </summary>
    public string Describe()
    {
        var localNow = AskLocal();

        lock (_gate)
        {
            return Described(localNow);
        }
    }

    /// <summary>Describe's body. Called with _gate held.</summary>
    private string Described(bool localNow)
    {
        if (LocalWritingTheMonth(localNow))
        {
            var written = SpentTonight == 1 ? "Tonight: 1 line written." : $"Tonight: {SpentTonight} lines written.";

            return $"{written} {LocalWritingSentence}";
        }

        if (!AllowanceKnown)
        {
            return SpentTonight == 1
                ? "Tonight: 1 line written."
                : $"Tonight: {SpentTonight} lines written.";
        }

        var allotment = (int)Math.Round(NightAllotment);
        var head = $"Tonight: {SpentTonight} of about {allotment} lines";

        if (KeepEverythingRunning)
        {
            return head + " - everything running, spending against next week.";
        }

        if (RemainingThisMonth < _allowance * EssentialReserve)
        {
            return head + " - the month is nearly out, so only intros and recaps are being written.";
        }

        var quietest = QuietestRunningTier();

        return quietest switch
        {
            SegmentTier.Flourish => head + " - everything running.",
            SegmentTier.Colour => head + " - the extras are paused.",
            SegmentTier.Core => head + " - running the live segments only.",
            _ => head + " - paused everything but intros and recaps.",
        };
    }

    /// <summary>The lowest tier that could still find headroom for one line. What Describe reports.</summary>
    private SegmentTier QuietestRunningTier()
    {
        var room = Headroom;

        foreach (var tier in new[] { SegmentTier.Flourish, SegmentTier.Colour, SegmentTier.Core })
        {
            if (tier <= LowestTier && room >= Weight(tier))
            {
                return tier;
            }
        }

        return SegmentTier.Essential;
    }

    /// <param name="localNow">LocalModelTakesOver's answer, asked before _gate was taken.</param>
    private BudgetVerdict Judge(SegmentKind kind, string? className, bool take, bool localNow)
    {
        var spec = Specs[kind];
        var fallback = FallbackFor(spec.Tier);
        var now = _clock();

        Trim(now);

        BudgetVerdict No(string reason, BudgetRefusal why) => new(false, reason, fallback, 0, why);

        // ---- The free gates, in the order that costs least to ask. ----

        if (_off.Contains(kind) || spec.Tier > LowestTier)
        {
            return No("this club has it switched off", BudgetRefusal.SwitchedOff);
        }

        // THE LIMITS AND THE GAP MOVE WITH THE CLUB'S DIAL (3 Nov 2026) -
        // see ShowChatter.ScaledLimit. They were the table's own numbers at
        // every stop, so Non-stop on a night of Stock heats ran out of
        // battle watches by the second heat and nothing said why.
        var dial = Chatter;
        var dialName = ShowChatter.For(dial).Name;
        var perNight = ShowChatter.ScaledLimit(spec.MaxPerMeeting, dial);
        var perClass = ShowChatter.ScaledLimit(spec.MaxPerClass, dial);

        if (Count(_perMeeting, kind) >= perNight)
        {
            return No($"already said tonight - {perNight} a night at {dialName}", BudgetRefusal.ItsCapForTheNight);
        }

        if (className is { Length: > 0 } && ForClass(kind, className) >= perClass)
        {
            return No($"already said for {className} - {perClass} per class at {dialName}", BudgetRefusal.ItsCapForTheNight);
        }

        // An essential is never refused - not for budget, not for airtime,
        // not because the relay is busy. A race has been staged and the
        // announcer has to say so; "six lines in the last ten minutes" is
        // not a reason to send a heat out in silence. It still SPENDS,
        // which is the whole mechanism: a night heavy on intros squeezes
        // the extras out by itself, with no special case for it anywhere
        // else in here.
        if (spec.Tier == SegmentTier.Essential)
        {
            return Allow(spec, className, take, fallback, free: LocalWritingTheMonth(localNow));
        }

        if (NothingNewToSay?.Invoke(kind, className) == true)
        {
            return No("nothing new to say about it", BudgetRefusal.NothingNewToSay);
        }

        // NOT INSIDE A PROGRAMME, where back-to-back is the whole idea -
        // see ReserveForProgramme, which explains why this one gate is
        // lifted there and lists the ones that are not.
        var gap = ShowChatter.ScaledGap(spec.MinGap, dial);

        if (!_insideAProgramme && spec.Speaks && _lastSpoken is { } last && now - last < gap)
        {
            return No(
                $"only {(now - last).TotalSeconds:0}s since the last line - it waits {gap.TotalSeconds:0}s at {dialName}",
                BudgetRefusal.TooSoonAfterTheLast);
        }

        // THE CAP MOVES WITH THE RACE (11 Oct 2026). Quieter in practice
        // and the heats, half again in the top main - see RaceStature. A
        // club with no programme is still on the flat six, which is what
        // AirtimeCap says.
        //
        // AND THEN WITH THE CLUB'S DIAL (12 Oct 2026). The race decides the
        // SHAPE of the evening and the club decides the volume it sits at -
        // see ShowChatter, which also explains why the two are kept apart.
        // The curve survives every stop on the dial: Non-stop in a heat is
        // still quieter than Non-stop in the top main.
        //
        // Read ONCE, so the cap and the reason given for it describe the
        // same race - see _stature.
        var stature = Stature;
        var shape = stature.AirtimeCap;
        var ceiling = ShowChatter.Scaled(shape, Chatter);

        if (spec.Speaks && _spokenAt.Count >= ceiling)
        {
            // SAYS WHICH OF THE TWO MOVED IT, because a club that has
            // turned the dial down and then wonders why the night is quiet
            // has to be able to read the answer rather than guess at it.
            var why = ceiling == shape
                ? stature.Billing == RaceBilling.Unknown
                    ? string.Empty
                    : $" - this is {stature.Describe}, which allows {ceiling}"
                : $" - this is {stature.Describe} at {ShowChatter.For(Chatter).Name}, which allows {ceiling}";

            return No($"{_spokenAt.Count} lines in the last ten minutes already{why}", BudgetRefusal.TheAirtimeCap);
        }

        // Only the requests actually in the air - a line written and
        // waiting to be said has finished with the relay. See Written.
        if (_open.Count - _written.Count >= RelayInFlightCap && spec.Tier > SegmentTier.Core)
        {
            return No("the relay is busy with something that matters more", BudgetRefusal.TheRelayIsBusy);
        }

        // ---- Then the one that costs money. ----

        // Unless nothing is being spent: the relay's month is used up and
        // the model on this PC is writing. The two relay-line gates below
        // step aside for what it may write, and a relay-only tier is
        // refused for the month here - see LocalModelTakesOver.
        if (LocalWritingTheMonth(localNow))
        {
            return fallback == BrainFallback.None
                ? No("this month's relay lines are used up, and this one is only written by the relay", BudgetRefusal.TheMonthsAllowance)
                : Allow(spec, className, take, fallback, free: true);
        }

        if (KeepEverythingRunning)
        {
            return Allow(spec, className, take, fallback);
        }

        if (AllowanceKnown && RemainingThisMonth < _allowance * EssentialReserve)
        {
            return No($"only {RemainingThisMonth} lines left this month, held for intros and recaps", BudgetRefusal.TheMonthsAllowance);
        }

        var room = Headroom;
        var cost = spec.Lines * Weight(spec.Tier);

        if (room < cost)
        {
            return No($"{room:0.#} lines of headroom, this needs {cost:0.#}", BudgetRefusal.TheNightsHeadroom);
        }

        return Allow(spec, className, take, fallback);
    }

    private BudgetVerdict Allow(SegmentSpec spec, string? className, bool take, BrainFallback fallback, bool free = false)
    {
        if (!take)
        {
            return new BudgetVerdict(true, string.Empty, fallback, 0);
        }

        var ticket = _nextTicket++;

        _open[ticket] = new Reservation(spec.Kind, className, spec.Lines, free);

        _perMeeting[spec.Kind] = Count(_perMeeting, spec.Kind) + 1;

        if (className is { Length: > 0 })
        {
            var key = Key(spec.Kind, className);
            _perClass[key] = _perClass.GetValueOrDefault(key) + 1;
        }

        return new BudgetVerdict(true, string.Empty, fallback, ticket);
    }

    private static int Count(Dictionary<SegmentKind, int> counts, SegmentKind kind) => counts.GetValueOrDefault(kind);

    private int ForClass(SegmentKind kind, string className) => _perClass.GetValueOrDefault(Key(kind, className));

    private static string Key(SegmentKind kind, string className) => $"{kind}\u001f{className}";

    private void Trim(DateTime now)
    {
        if (_spokenAt.Count == 0)
        {
            return;
        }

        _spokenAt.RemoveAll(at => now - at >= AirtimeWindow);
    }

    // ------------------------------------------------------------------
    // Carrying the night across a restart
    //
    // A race day outlives the app: a laptop is closed between the heats
    // and the mains often enough that losing the night's spend would hand
    // the second half of the evening a whole fresh allotment. Saved as the
    // night's own figures only - the month's come back from the relay with
    // the first line written, which is the one number we must not guess.
    // ------------------------------------------------------------------

    private sealed record SavedNight(
        string DayId,
        double Spent,
        double HighWater,
        Dictionary<string, int>? PerMeeting,
        Dictionary<string, int>? PerClass,
        double Free = 0);

    /// <summary>Tonight's spend, for AppSettings to hold across a restart. See Restore.</summary>
    public string Save()
    {
        lock (_gate)
        {
            // 17 Oct 2026, Bug Hunt 8, A08-2: the counts are taken at reserve
            // time, so a line still in the air or waiting on the host was
            // saved as said - and a restart before the next save treated an
            // unheard once-a-night segment as done. The file gets the counts
            // without the open holds (floored at zero, as Release does); the
            // in-memory counts keep them, so the caps still bind in-process.
            // _spentTonight is charged in Settle, so it already matches.
            var perMeeting = new Dictionary<SegmentKind, int>(_perMeeting);
            var perClass = new Dictionary<string, int>(_perClass, StringComparer.OrdinalIgnoreCase);

            foreach (var held in _open.Values)
            {
                if (perMeeting.TryGetValue(held.Kind, out var meeting) && meeting > 0)
                {
                    perMeeting[held.Kind] = meeting - 1;
                }

                if (held.ClassName is { Length: > 0 } className)
                {
                    var key = Key(held.Kind, className);

                    if (perClass.TryGetValue(key, out var forClass) && forClass > 0)
                    {
                        perClass[key] = forClass - 1;
                    }
                }
            }

            return JsonSerializer.Serialize(new SavedNight(
                _dayId,
                _spentTonight,
                _entitledHighWater,
                perMeeting.ToDictionary(p => p.Key.ToString(), p => p.Value),
                new Dictionary<string, int>(perClass),
                // 6 Nov 2026, Bug hunt 11b: the free local lines go with the
                // spend they are part of - see _freeTonight. Settled only,
                // like Spent: a free hold still open is not in either.
                _freeTonight));
        }
    }

    /// <summary>
    /// Picks the night back up, but only when it is the same race day -
    /// last week's spend must not be charged to this week. Anything
    /// unreadable is treated as a fresh night rather than throwing: a
    /// corrupt setting is not a reason to lose the announcer.
    ///
    /// ONCE PER NIGHT. The director calls this from the host's half-second
    /// tick, and the saved copy is only rewritten after a line is spoken -
    /// so reading it again on the same day put every count back to the
    /// last save: a segment reserved and in flight lost its count, the
    /// per-night and per-class caps above one never bound, and the high
    /// water mark fell back between saves.
    /// </summary>
    public void Restore(string? saved, string dayId)
    {
        // NORMALISED ONCE, HERE, and that is the whole of this line's job
        // (13 Oct 2026). The comparison below used to read
        // "dayId ?? string.Empty" inline - and a null-coalesce on a
        // parameter declared non-nullable tells the compiler's flow
        // analysis that the parameter may be null from that point on, so
        // every use of it further down became CS8604. One warning, no
        // behaviour: the defensiveness was right and only its placing was
        // wrong.
        var day = dayId ?? string.Empty;

        lock (_gate)
        {
            if (_nightRestored && string.Equals(_dayId, day, StringComparison.Ordinal))
            {
                return;
            }

            StartNight(day);
            _nightRestored = true;

            RestoreNight(saved, day);
        }
    }

    /// <summary>Restore's body, once the night is known to need it. Called with _gate held.</summary>
    private void RestoreNight(string? saved, string dayId)
    {
        if (string.IsNullOrWhiteSpace(saved))
        {
            return;
        }

        SavedNight? night;

        try
        {
            night = JsonSerializer.Deserialize<SavedNight>(saved);
        }
        catch (JsonException)
        {
            return;
        }

        if (night is null || !string.Equals(night.DayId, dayId, StringComparison.Ordinal))
        {
            return;
        }

        _spentTonight = Math.Max(0, night.Spent);
        _entitledHighWater = Math.Max(0, night.HighWater);

        // 6 Nov 2026, Bug hunt 11b: the free local lines come back too, or
        // a restart charges them as relay lines - see _freeTonight. A file
        // saved before this has no Free and reads as 0, the old behaviour.
        // Never more than the spend it is part of.
        _freeTonight = Math.Clamp(night.Free, 0, _spentTonight);

        // The relay's next count is taken against the spend just picked
        // back up, not against the empty night that was there a moment ago.
        _usedBeforeTonight = null;

        foreach (var (name, count) in night.PerMeeting ?? new Dictionary<string, int>())
        {
            if (Enum.TryParse<SegmentKind>(name, out var kind))
            {
                _perMeeting[kind] = count;
            }
        }

        foreach (var (key, count) in night.PerClass ?? new Dictionary<string, int>())
        {
            _perClass[key] = count;
        }
    }
}
