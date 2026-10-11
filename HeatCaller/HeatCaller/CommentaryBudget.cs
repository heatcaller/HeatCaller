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

    ColdOpen,
    ThisDayInHistory,
    SignOffNumbers,
    MidMeetingStandings,
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

    /// <summary>Let Ollama write it - rough phrasing beats silence for this one.</summary>
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
public sealed record BudgetVerdict(bool Allowed, string Reason, BrainFallback Fallback, long Ticket);

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

    /// <summary>
    /// The share of a night's allotment released before a single round has
    /// run. Without it progress is zero at the cold open, headroom is zero,
    /// and the night can never start talking.
    /// </summary>
    public const double OpeningFloat = 0.15;

    /// <summary>How many spoken segments a ten-minute stretch may hold, whatever the budget allows.</summary>
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
            new(SegmentKind.BadgesEarned, SegmentTier.Essential, 1, true, TimeSpan.FromSeconds(60), TimeSpan.Zero, int.MaxValue, int.MaxValue, "Badges earned", "After a race, a third voice reads out the badges it unlocked."),

            // --- Core: the live feel ---
            // The battle watch is the shortest-lived thing here: a line
            // about laps four to nine is worthless by lap twenty, however
            // well written, so it is thrown away rather than spoken late.
            new(SegmentKind.BattleWatch,        SegmentTier.Core, 1, true, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(40), 8,  4, "Battle watch", "Calls a fight when two cars have been locked together for a while.", true),
            // Its gap is shorter than its life, so a record lap that lands
            // just after another line can still be said before it is stale.
            new(SegmentKind.PaceNote,           SegmentTier.Core, 1, true, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(25), 10, 5, "Record laps", "Calls a lap when a record falls - the race's, the night's or the layout's."),
            new(SegmentKind.ComingUpNext,       SegmentTier.Core, 1, true, TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(20), 12, 12, "Coming up next", "Says what is on next, in the gap between races."),
            new(SegmentKind.ChampionshipCheck,  SegmentTier.Core, 1, true, TimeSpan.FromMinutes(3),  TimeSpan.FromSeconds(20), 6,  1, "Championship check", "Where the season stands, after a class's last main.", true),

            // --- Colour: depth ---
            new(SegmentKind.FormGuide,         SegmentTier.Colour, 1, true, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(30), 6, 2, "Form guide", "How the drivers in a main have been going lately.", true),
            new(SegmentKind.QualifyingReport,  SegmentTier.Colour, 1, true, TimeSpan.FromMinutes(3),  TimeSpan.FromSeconds(30), 6, 1, "Qualifying report", "What qualifying decided, before the mains go out.", true),
            new(SegmentKind.Honours,           SegmentTier.Colour, 1, true, TimeSpan.FromMinutes(2),  TimeSpan.FromSeconds(45), 5, 3, "Milestones", "A hundredth race, a first podium, a club record."),
            new(SegmentKind.MechanicInterview, SegmentTier.Colour, 2, true, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(15), 6, 2, "Mechanic's interview", "The announcer asks the winning car's mechanic two questions.", true),
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

            // --- Flourish: the furniture ---
            new(SegmentKind.ColdOpen,            SegmentTier.Flourish, 1, true, TimeSpan.FromMinutes(5), TimeSpan.Zero,             1, 1, "Cold open", "Sets the evening out before the first race.", true),
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

    // --- What the relay last told us. Zero allowance means "not heard
    // from yet", which is deliberately treated as no budget gate at all:
    // refusing to write before we know what the club has would silence a
    // club with plenty, and the relay enforces the real cap regardless.
    private int _usedThisMonth;
    private int _allowance;

    // --- The night ---
    private string _dayId = string.Empty;
    private int _roundsRun;
    private int _roundsTotal;
    private double _spentTonight;
    private double _entitledHighWater;

    private readonly HashSet<SegmentKind> _off = new();
    private readonly Dictionary<SegmentKind, int> _perMeeting = new();
    private readonly Dictionary<string, int> _perClass = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DateTime> _spokenAt = new();
    private readonly Dictionary<long, Reservation> _open = new();

    private long _nextTicket = 1;
    private int _nightsLeft = 4;
    private DateTime? _lastSpoken;

    /// <summary>
    /// A line held but not yet written. Holds its LINES, not its weighted
    /// cost: the weight is the gate a tier has to clear, the lines are the
    /// real money, and a hold has to be worth exactly what settling it
    /// will cost or the books don't balance.
    /// </summary>
    private sealed record Reservation(SegmentKind Kind, string? ClassName, int Lines);

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
    /// The operator's override for a meeting that matters - a regional, a
    /// club's open day. Spends against next week on purpose, so it is a
    /// tick somebody makes rather than a default.
    /// </summary>
    public bool KeepEverythingRunning { get; set; }

    /// <summary>True when this club has switched this segment off for good. See SetOff.</summary>
    public bool IsOff(SegmentKind kind) => _off.Contains(kind);

    /// <summary>Everything this club has switched off, for the settings page and for saving.</summary>
    public IReadOnlyCollection<SegmentKind> SwitchedOff => _off;

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

        if (off)
        {
            _off.Add(kind);
        }
        else
        {
            _off.Remove(kind);
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
        _off.Clear();

        foreach (var kind in ParseSwitches(saved))
        {
            _off.Add(kind);
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
    public int InFlight => _open.Count;

    /// <summary>Lines written tonight, settled only - a reservation in flight does not count.</summary>
    public int SpentTonight => (int)Math.Round(_spentTonight);

    /// <summary>What the club has left this month, as the relay last reported it. Zero when it hasn't said.</summary>
    public int RemainingThisMonth => _allowance <= 0 ? 0 : Math.Max(0, _allowance - _usedThisMonth);

    /// <summary>True once the relay has told us what the club's allowance is. Before that there is no budget gate.</summary>
    public bool AllowanceKnown => _allowance > 0;

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
    /// </summary>
    public double NightAllotment => _allowance <= 0
        ? 0
        : (double)RemainingThisMonth / NightsLeftThisMonth;

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
            if (_allowance <= 0)
            {
                return double.PositiveInfinity;
            }

            var progress = _roundsTotal <= 0
                ? 0
                : Math.Clamp((double)_roundsRun / _roundsTotal, 0, 1);

            var released = NightAllotment * (OpeningFloat + ((1 - OpeningFloat) * progress));

            return Math.Max(_entitledHighWater, released);
        }
    }

    /// <summary>What is left of the night's entitlement, after what is settled and what is reserved.</summary>
    public double Headroom => Entitled - _spentTonight - _open.Values.Sum(r => r.Lines);

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

        _usedThisMonth = Math.Max(0, used);
        _allowance = allowance;
    }

    /// <summary>How much of tonight's programme has run. Called whenever the programme is refreshed.</summary>
    public void NightShape(int roundsRun, int roundsTotal)
    {
        _roundsRun = Math.Max(0, roundsRun);
        _roundsTotal = Math.Max(0, roundsTotal);

        // Read once here so the high water mark keeps up as the night runs,
        // rather than only when something asks to be written.
        var earned = Entitled;

        _entitledHighWater = Math.Max(_entitledHighWater, double.IsInfinity(earned) ? 0 : earned);
    }

    /// <summary>
    /// A new race day. Everything counted per night starts again; what the
    /// relay has told us about the month does not.
    /// </summary>
    public void NewMeeting(string dayId)
    {
        if (string.Equals(_dayId, dayId, StringComparison.Ordinal))
        {
            return;
        }

        _dayId = dayId ?? string.Empty;
        _roundsRun = 0;
        _roundsTotal = 0;
        _spentTonight = 0;
        _entitledHighWater = 0;
        _lastSpoken = null;

        _perMeeting.Clear();
        _perClass.Clear();
        _spokenAt.Clear();
        _open.Clear();
    }

    /// <summary>
    /// May this segment be written right now, and at what cost - without
    /// taking anything. For the settings page and for a caller that wants
    /// to know before it goes to the trouble of building the facts.
    /// </summary>
    public BudgetVerdict Peek(SegmentKind kind, string? className = null) => Judge(kind, className, take: false);

    /// <summary>
    /// May this segment be written right now - and if so, hold its share
    /// until it is settled or let go.
    ///
    /// Reserved rather than simply counted because the relay allows two
    /// requests per club at once: two segments can both find the last line
    /// of headroom in the same moment, and the second one must not. Mirrors
    /// the relay's own reserve-then-refund for the same reason.
    /// </summary>
    public BudgetVerdict Reserve(SegmentKind kind, string? className = null) => Judge(kind, className, take: true);

    /// <summary>The line was written. The reservation becomes spend, and the airtime clock restarts.</summary>
    public void Settle(long ticket, bool spoken = true)
    {
        if (!_open.Remove(ticket, out var held))
        {
            return;
        }

        _spentTonight += held.Lines;

        if (!spoken)
        {
            return;
        }

        var now = _clock();

        _lastSpoken = now;
        _spokenAt.Add(now);

        Trim(now);
    }

    /// <summary>
    /// Nothing was written - the relay refused, the line came back too
    /// late to be worth saying, or the facts turned out to be nothing. The
    /// hold is given back whole; the relay refunds its own count the same
    /// way.
    /// </summary>
    public void Release(long ticket)
    {
        if (!_open.Remove(ticket, out var held))
        {
            return;
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

    /// <summary>
    /// The one line the Commentary page shows about tonight.
    ///
    /// Deliberately says "about": the allotment moves with the month and a
    /// number presented as exact would be wrong by the second race.
    /// </summary>
    public string Describe()
    {
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

    private BudgetVerdict Judge(SegmentKind kind, string? className, bool take)
    {
        var spec = Specs[kind];
        var fallback = FallbackFor(spec.Tier);
        var now = _clock();

        Trim(now);

        BudgetVerdict No(string reason) => new(false, reason, fallback, 0);

        // ---- The free gates, in the order that costs least to ask. ----

        if (_off.Contains(kind) || spec.Tier > LowestTier)
        {
            return No("this club has it switched off");
        }

        if (Count(_perMeeting, kind) >= spec.MaxPerMeeting)
        {
            return No("already said tonight");
        }

        if (className is { Length: > 0 } && ForClass(kind, className) >= spec.MaxPerClass)
        {
            return No($"already said for {className}");
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
            return Allow(spec, className, take, fallback);
        }

        if (NothingNewToSay?.Invoke(kind, className) == true)
        {
            return No("nothing new to say about it");
        }

        if (spec.Speaks && _lastSpoken is { } last && now - last < spec.MinGap)
        {
            return No($"only {(now - last).TotalSeconds:0}s since the last line");
        }

        if (spec.Speaks && _spokenAt.Count >= AirtimeCap)
        {
            return No($"{_spokenAt.Count} lines in the last ten minutes already");
        }

        if (_open.Count >= RelayInFlightCap && spec.Tier > SegmentTier.Core)
        {
            return No("the relay is busy with something that matters more");
        }

        // ---- Then the one that costs money. ----

        if (KeepEverythingRunning)
        {
            return Allow(spec, className, take, fallback);
        }

        if (AllowanceKnown && RemainingThisMonth < _allowance * EssentialReserve)
        {
            return No($"only {RemainingThisMonth} lines left this month, held for intros and recaps");
        }

        var room = Headroom;
        var cost = spec.Lines * Weight(spec.Tier);

        if (room < cost)
        {
            return No($"{room:0.#} lines of headroom, this needs {cost:0.#}");
        }

        return Allow(spec, className, take, fallback);
    }

    private BudgetVerdict Allow(SegmentSpec spec, string? className, bool take, BrainFallback fallback)
    {
        if (!take)
        {
            return new BudgetVerdict(true, string.Empty, fallback, 0);
        }

        var ticket = _nextTicket++;

        _open[ticket] = new Reservation(spec.Kind, className, spec.Lines);

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
        Dictionary<string, int>? PerClass);

    /// <summary>Tonight's spend, for AppSettings to hold across a restart. See Restore.</summary>
    public string Save() => JsonSerializer.Serialize(new SavedNight(
        _dayId,
        _spentTonight,
        _entitledHighWater,
        _perMeeting.ToDictionary(p => p.Key.ToString(), p => p.Value),
        new Dictionary<string, int>(_perClass)));

    /// <summary>
    /// Picks the night back up, but only when it is the same race day -
    /// last week's spend must not be charged to this week. Anything
    /// unreadable is treated as a fresh night rather than throwing: a
    /// corrupt setting is not a reason to lose the announcer.
    /// </summary>
    public void Restore(string? saved, string dayId)
    {
        NewMeeting(dayId);

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
