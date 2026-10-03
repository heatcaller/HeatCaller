namespace HeatCaller;

/// <summary>What kind of thing a badge is earned for - used to group them on screen.</summary>
public enum AchievementCategory
{
    /// <summary>Judged within a single race.</summary>
    Race,

    /// <summary>Career totals across every session ever recorded.</summary>
    Milestone,

    /// <summary>Beating your own history rather than anyone else's.</summary>
    Progress,

    /// <summary>Earned against other people.</summary>
    Rivalry,
}

/// <summary>
/// What a badge needs that a driver cannot bring on their own - which decides
/// whether a home track is offered it (AppSettings.RacingAt). Not persisted:
/// worked out from the catalogue (AchievementCatalogue.NeedsOf), so it can be
/// re-judged freely.
/// </summary>
public enum AchievementNeeds
{
    /// <summary>Earnable racing alone - clean laps, personal bests, lap totals, the calendar.</summary>
    Alone,

    /// <summary>At least one other real car in the race - a win, a podium, a gap, beating somebody.</summary>
    Rival,

    /// <summary>A club-sized field or roster: five or six cars in one race, or ten or more different opponents.</summary>
    Club,

    /// <summary>Only earned at a home track, where practice is judged too (practice laps and practice days).</summary>
    Home,
}

/// <summary>Where the app is used - which decides which badges are offered and whether practice earns any.</summary>
public enum RacingSetting
{
    /// <summary>A club: every badge but the home ones, earned in races only. The default, and every install from before this existed.</summary>
    Club = 0,

    /// <summary>A home track - a family or a few friends: no club-sized badges, and practice earns the ones that can be earned alone.</summary>
    Home = 1,
}

/// <summary>
/// Roughly how hard a badge is to come by, which is all the tier is for:
/// it picks the medal's colour so a page of badges has a shape to it,
/// rather than being forty identical discs.
///
/// Persisted nowhere - tiers can be re-graded freely as the difficulty of
/// something becomes clear in practice.
/// </summary>
public enum AchievementTier
{
    Bronze,
    Silver,
    Gold,
    Legend,
}

/// <summary>
/// One badge, as a definition rather than something anybody has earned.
///
/// The catalogue is code rather than a file on purpose: a badge is a rule,
/// and a rule needs something to evaluate it. Making them user-editable
/// would mean either a scripting language or a fixed set of parameters
/// pretending to be flexible, and neither is worth it until the built-in
/// ones have been lived with.
///
/// <see cref="Id"/> is what gets persisted, so it must never change once a
/// badge has shipped - the name, description, tier and artwork can be
/// reworded or redrawn freely.
/// </summary>
/// <param name="Icon">
/// The single-character fallback, kept for anywhere that can only show
/// text - a log line, an exported sheet. The app itself draws
/// <paramref name="Art"/> instead; see <see cref="BadgeArt"/>.
/// </param>
/// <param name="Art">Which emblem <see cref="BadgeArt.DrawEmblem"/> draws for this badge.</param>
public sealed record AchievementDefinition(
    string Id,
    string Name,
    string Description,
    AchievementCategory Category,
    string Icon,
    AchievementTier Tier = AchievementTier.Bronze,
    string Art = "star")
{
    /// <summary>What it needs beyond the driver themselves - see AchievementNeeds. Filled from AchievementCatalogue.NeedsOf.</summary>
    public AchievementNeeds Needs { get; init; } = AchievementNeeds.Alone;
}

/// <summary>One badge, earned, by one driver.</summary>
public sealed class UnlockedAchievement
{
    public string Id { get; set; } = string.Empty;
    public DateTime UnlockedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>The session it was earned in, so a badge can be traced back to the race that produced it.</summary>
    public string? SessionId { get; set; }

    /// <summary>What it was worth at the time - "12 laps", "4.586" - for badges where the number is the story.</summary>
    public string Detail { get; set; } = string.Empty;
}

/// <summary>Everything one driver has earned.</summary>
public sealed class DriverAchievements
{
    public string Transponder { get; set; } = string.Empty;
    public List<UnlockedAchievement> Unlocked { get; set; } = new();

    // Running totals, kept here rather than recomputed from the whole of
    // session history every time. History is the source of truth for
    // results; these are a cache of the handful of numbers the milestone
    // badges care about, and recomputing them from every session ever run
    // would get slower with every club night.
    public int RacesFinished { get; set; }
    public int TotalLaps { get; set; }
    public int Wins { get; set; }
    public int Podiums { get; set; }

    /// <summary>Track layouts this driver holds, or has held, the record on.</summary>
    public List<string> TrackRecords { get; set; } = new();

    public bool Has(string id) => Unlocked.Any(u => string.Equals(u.Id, id, StringComparison.Ordinal));
}

/// <summary>
/// Every badge the app knows about.
///
/// Ids are permanent. Adding a badge is safe at any time; renaming an id is
/// not, because a driver who earned it would silently lose it.
/// </summary>
public static class AchievementCatalogue
{
    // --- In a race ---
    public const string CleanTen = "clean-ten";
    public const string CleanTwentyFive = "clean-twentyfive";
    public const string FastestLap = "fastest-lap";
    public const string Flawless = "flawless";
    public const string WireToWire = "wire-to-wire";
    public const string ComeFromBehind = "come-from-behind";
    public const string PhotoFinish = "photo-finish";
    public const string LateShow = "late-show";
    public const string PoleConverted = "pole-converted";
    public const string LapClear = "lap-clear";
    public const string FinishedStrong = "finished-strong";
    public const string QuickAndTidy = "quick-and-tidy";
    public const string DistanceRunner = "distance-runner";
    public const string CleanFifty = "clean-fifty";
    public const string Double = "double-up";
    public const string HatTrick = "hat-trick";
    public const string Charge = "charge";
    public const string LastLapHero = "last-lap-hero";
    public const string RocketStart = "rocket-start";
    public const string NailBiter = "nail-biter";
    public const string GrandSlam = "grand-slam";
    public const string ComebackKid = "comeback-kid";
    public const string NeverSayDie = "never-say-die";
    public const string CountryMile = "country-mile";
    public const string SlowAndSteady = "slow-and-steady";
    public const string PaceSetter = "pace-setter";

    // --- Career ---
    public const string FirstRace = "first-race";
    public const string TenRaces = "ten-races";
    public const string FiftyRaces = "fifty-races";
    public const string HundredRaces = "hundred-races";
    public const string ThousandLaps = "thousand-laps";
    public const string FiveThousandLaps = "fivethousand-laps";
    public const string FirstWin = "first-win";
    public const string TenWins = "ten-wins";
    public const string TwentyFiveWins = "twentyfive-wins";
    public const string FirstPodium = "first-podium";
    public const string TenPodiums = "ten-podiums";
    public const string TrackRecordHolder = "track-record";
    public const string ThreeTrackRecords = "three-track-records";
    public const string OneYearOn = "one-year-on";
    public const string NightOwl = "night-owl";
    public const string TwentyFiveRaces = "twentyfive-races";
    public const string FiftyWins = "fifty-wins";
    public const string TwentyFivePodiums = "twentyfive-podiums";
    public const string TenThousandLaps = "tenthousand-laps";
    public const string RookieSensation = "rookie-sensation";
    public const string DoubleTrouble = "double-trouble";
    public const string IronDriver = "iron-driver";
    public const string WeekendWarrior = "weekend-warrior";
    public const string EarlyBird = "early-bird";
    public const string FridayThe13th = "friday-13th";
    public const string FestiveSpirit = "festive-spirit";
    public const string Globetrotter = "globetrotter";
    public const string CarCollector = "car-collector";
    public const string KingOfTheHill = "king-of-the-hill";
    public const string Bridesmaid = "always-bridesmaid";
    public const string FourLeafClover = "four-leaf-clover";

    // --- Progress ---
    public const string PersonalBest = "personal-best";
    public const string BigStep = "big-step";
    public const string ThreeMeetingsImproving = "three-improving";
    public const string Consistent = "consistent";
    public const string SwissWatch = "swiss-watch";
    public const string Unbroken = "unbroken";
    public const string HotStreak = "hot-streak";
    public const string Sharpshooter = "sharpshooter";
    public const string CleanSlate = "clean-slate";
    public const string BounceBack = "bounce-back";

    // --- Against others ---
    public const string BeatARival = "beat-a-rival";
    public const string PodiumRegular = "podium-regular";
    public const string RacedEveryone = "raced-everyone";
    public const string FullHouse = "full-house";
    public const string TwentyOpponents = "twenty-opponents";
    public const string FiftyOpponents = "fifty-opponents";
    public const string GiantKiller = "giant-killer";
    public const string StreakBreaker = "streak-breaker";
    public const string Payback = "payback";
    public const string OldFriends = "old-friends";

    // --- Home and garage (Oct 2026) ---
    public const string GhostBuster = "ghost-buster";
    public const string ShadowBoxer = "shadow-boxer";
    public const string PracticeMakesPerfect = "practice-makes-perfect";
    public const string RegularAtTheTrack = "regular-at-the-track";
    public const string DialledIn = "dialled-in";
    public const string FreshLegs = "fresh-legs";
    public const string GarageTour = "garage-tour";
    public const string PackRotation = "pack-rotation";
    public const string BackToBack = "back-to-back";

    /// <summary>The badges that need at least one other real car in the race.</summary>
    private static readonly HashSet<string> NeedRival = new(StringComparer.Ordinal)
    {
        FastestLap, WireToWire, ComeFromBehind, PhotoFinish, LateShow, PoleConverted, LapClear, QuickAndTidy,
        Double, HatTrick, Charge, RocketStart, NailBiter, GrandSlam, NeverSayDie, CountryMile, SlowAndSteady,
        PaceSetter, FirstWin, TenWins, TwentyFiveWins, FiftyWins, FirstPodium, TenPodiums, TwentyFivePodiums,
        RookieSensation, DoubleTrouble, KingOfTheHill, Bridesmaid, FourLeafClover, BounceBack, BeatARival,
        PodiumRegular, GiantKiller, StreakBreaker, Payback, OldFriends,
    };

    /// <summary>The badges that need a club-sized field (five or six cars) or roster (ten or more opponents).</summary>
    private static readonly HashSet<string> NeedClub = new(StringComparer.Ordinal)
    {
        FullHouse, ComebackKid, RacedEveryone, TwentyOpponents, FiftyOpponents,
    };

    /// <summary>The badges only a home track earns - they are judged on practice, which a club's never is.</summary>
    private static readonly HashSet<string> NeedHome = new(StringComparer.Ordinal)
    {
        PracticeMakesPerfect, RegularAtTheTrack,
    };

    /// <summary>What a badge needs beyond the driver themselves.</summary>
    public static AchievementNeeds NeedsOf(string id) =>
        NeedClub.Contains(id) ? AchievementNeeds.Club
        : NeedRival.Contains(id) ? AchievementNeeds.Rival
        : NeedHome.Contains(id) ? AchievementNeeds.Home
        : AchievementNeeds.Alone;

    /// <summary>
    /// Whether a badge is on offer where the app is used: a club is offered
    /// everything but the home badges, a home track everything but the
    /// club-sized ones. A badge somebody has earned is shown whatever this
    /// says - see the Achievements page.
    /// </summary>
    public static bool IsOffered(AchievementDefinition badge, RacingSetting where) =>
        where == RacingSetting.Home ? badge.Needs != AchievementNeeds.Club : badge.Needs != AchievementNeeds.Home;

    public static IReadOnlyList<AchievementDefinition> All { get; } = Tagged(new List<AchievementDefinition>
    {
        // --- Single race ---
        new(CleanTen, "Metronome", "Ten consecutive clean laps in one race.",
            AchievementCategory.Race, "⏱", AchievementTier.Bronze, "stopwatch"),
        new(CleanTwentyFive, "Unshakeable", "Twenty-five consecutive clean laps in one race.",
            AchievementCategory.Race, "⚙", AchievementTier.Gold, "gear"),
        new(FastestLap, "Quickest of the Day", "Set the fastest lap of a race.",
            AchievementCategory.Race, "⚡", AchievementTier.Silver, "bolt"),
        new(Flawless, "Spotless", "Finished a race without a single rejected lap.",
            AchievementCategory.Race, "✨", AchievementTier.Bronze, "sparkle"),
        new(WireToWire, "Wire to Wire", "Led every lap of a race from the green to the flag.",
            AchievementCategory.Race, "♛", AchievementTier.Gold, "crown"),
        new(ComeFromBehind, "Through the Field", "Won a race from outside the top three on the grid.",
            AchievementCategory.Race, "⇑", AchievementTier.Gold, "chevron-up-double"),
        new(PhotoFinish, "Photo Finish", "Finished within a second of the car next to you.",
            AchievementCategory.Race, "▣", AchievementTier.Silver, "camera"),
        new(LateShow, "Late Show", "Won a race you led for no more than one lap.",
            AchievementCategory.Race, "⧗", AchievementTier.Gold, "hourglass"),
        new(PoleConverted, "Converted", "Started on pole and won.",
            AchievementCategory.Race, "⚑", AchievementTier.Silver, "checkered-flag"),
        new(LapClear, "A Lap Clear", "Won a race by a full lap or more.",
            AchievementCategory.Race, "◎", AchievementTier.Gold, "route"),
        new(FinishedStrong, "Finished Strong", "Second half of a race quicker than the first.",
            AchievementCategory.Race, "◪", AchievementTier.Bronze, "chart-up"),
        new(QuickAndTidy, "Quick and Tidy", "Fastest lap of the race, with nothing rejected.",
            AchievementCategory.Race, "✔", AchievementTier.Silver, "shield"),
        new(DistanceRunner, "Distance Runner", "Thirty laps or more in a single race.",
            AchievementCategory.Race, "∞", AchievementTier.Bronze, "infinity"),
        new(CleanFifty, "Machine", "Fifty consecutive clean laps in one race.",
            AchievementCategory.Race, "◉", AchievementTier.Legend, "gear"),
        new(Double, "The Double", "Won a race and set its fastest lap.",
            AchievementCategory.Race, "◆", AchievementTier.Gold, "trophy"),
        new(HatTrick, "Hat-trick", "Won three races in a row.",
            AchievementCategory.Race, "\u2261", AchievementTier.Gold, "crown"),
        new(Charge, "Through the Pack", "Finished four or more places ahead of where you started.",
            AchievementCategory.Race, "\u21d7", AchievementTier.Silver, "arrow-up"),
        new(LastLapHero, "Last Lap Hero", "Your quickest lap of the race was the final one.",
            AchievementCategory.Race, "\u29d6", AchievementTier.Silver, "bolt"),
        new(RocketStart, "Rocket Start", "Quickest opening lap of the race, from third or further back.",
            AchievementCategory.Race, "\u2197", AchievementTier.Silver, "rocket"),
        new(NailBiter, "Nail-Biter", "Won a race by less than two tenths of a second.",
            AchievementCategory.Race, "\u223f", AchievementTier.Gold, "pulse"),
        new(GrandSlam, "Grand Slam", "Pole, the win, the fastest lap - and led every single lap.",
            AchievementCategory.Race, "\u25c6", AchievementTier.Legend, "gem"),
        new(ComebackKid, "Comeback Kid", "Started last of five or more and still made the podium.",
            AchievementCategory.Race, "\u2934", AchievementTier.Gold, "swoosh"),
        new(NeverSayDie, "Never Say Die", "At the back at the flag, but within a lap of the winner.",
            AchievementCategory.Race, "\u2665", AchievementTier.Bronze, "heart"),
        new(CountryMile, "By a Country Mile", "Won by five seconds or more, on the same lap as second place.",
            AchievementCategory.Race, "\u25ce", AchievementTier.Silver, "binoculars"),
        new(SlowAndSteady, "Slow and Steady", "Won a race while two or more rivals had a quicker best lap.",
            AchievementCategory.Race, "\u25d2", AchievementTier.Silver, "tortoise"),
        new(PaceSetter, "Pace Setter", "Fastest lap of the race, three races running.",
            AchievementCategory.Race, "\u26a1", AchievementTier.Gold, "twin-bolt"),

        // --- Career milestones ---
        new(FirstRace, "First Timer", "Finished your first race.",
            AchievementCategory.Milestone, "⚑", AchievementTier.Bronze, "checkered-flag"),
        new(TenRaces, "Regular", "Finished ten races.",
            AchievementCategory.Milestone, "⚐", AchievementTier.Bronze, "medal"),
        new(FiftyRaces, "Veteran", "Finished fifty races.",
            AchievementCategory.Milestone, "⚐", AchievementTier.Silver, "medal"),
        new(HundredRaces, "Club Fixture", "Finished a hundred races.",
            AchievementCategory.Milestone, "★", AchievementTier.Gold, "star"),
        new(ThousandLaps, "Long Hauler", "A thousand laps completed, all told.",
            AchievementCategory.Milestone, "∞", AchievementTier.Silver, "route"),
        new(FiveThousandLaps, "Marathoner", "Five thousand laps completed, all told.",
            AchievementCategory.Milestone, "∞", AchievementTier.Legend, "route"),
        new(FirstWin, "Winner", "Won a race.",
            AchievementCategory.Milestone, "♔", AchievementTier.Silver, "trophy"),
        new(TenWins, "Serial Winner", "Won ten races.",
            AchievementCategory.Milestone, "♕", AchievementTier.Gold, "trophy"),
        new(TwentyFiveWins, "Dominator", "Won twenty-five races.",
            AchievementCategory.Milestone, "♕", AchievementTier.Legend, "crown"),
        new(FirstPodium, "On the Box", "Your first podium finish.",
            AchievementCategory.Milestone, "⛰", AchievementTier.Bronze, "podium"),
        new(TenPodiums, "Podium Habit", "Ten podium finishes.",
            AchievementCategory.Milestone, "⛰", AchievementTier.Gold, "podium"),
        new(TrackRecordHolder, "Record Holder", "Set an all-time track record on any layout.",
            AchievementCategory.Milestone, "◉", AchievementTier.Gold, "disc"),
        new(ThreeTrackRecords, "Record Collector", "Hold, or have held, records on three layouts.",
            AchievementCategory.Milestone, "◉", AchievementTier.Legend, "disc"),
        new(OneYearOn, "Anniversary", "Still racing a year after your first race.",
            AchievementCategory.Milestone, "▦", AchievementTier.Silver, "calendar"),
        new(NightOwl, "Night Owl", "Raced after ten at night.",
            AchievementCategory.Milestone, "☾", AchievementTier.Bronze, "moon"),
        new(TwentyFiveRaces, "Regular", "Twenty-five races finished.",
            AchievementCategory.Milestone, "\u2690", AchievementTier.Silver, "medal"),
        new(FiftyWins, "Half a Century", "Fifty race wins.",
            AchievementCategory.Milestone, "\u2605", AchievementTier.Legend, "trophy"),
        new(TwentyFivePodiums, "Ever Present", "Twenty-five podium finishes.",
            AchievementCategory.Milestone, "\u265b", AchievementTier.Gold, "podium"),
        new(TenThousandLaps, "Ten Thousand", "Ten thousand laps completed.",
            AchievementCategory.Milestone, "\u221e", AchievementTier.Legend, "route"),
        new(RookieSensation, "Rookie Sensation", "Won a race within your first five.",
            AchievementCategory.Milestone, "\u2605", AchievementTier.Gold, "shooting-star"),
        new(DoubleTrouble, "Double Trouble", "Won two races on the same day.",
            AchievementCategory.Milestone, "\u2681", AchievementTier.Silver, "dice"),
        new(IronDriver, "Iron Driver", "Five races in a single day.",
            AchievementCategory.Milestone, "\u2692", AchievementTier.Silver, "anvil"),
        new(WeekendWarrior, "Weekend Warrior", "Raced on a Saturday and again on the Sunday.",
            AchievementCategory.Milestone, "\u25a6", AchievementTier.Bronze, "calendar"),
        new(EarlyBird, "Early Bird", "On track before nine in the morning.",
            AchievementCategory.Milestone, "\u2600", AchievementTier.Bronze, "sunrise"),
        new(FridayThe13th, "Friday the 13th", "Raced on a Friday the 13th - and lived to tell the tale.",
            AchievementCategory.Milestone, "\u263d", AchievementTier.Silver, "cat"),
        new(FestiveSpirit, "Festive Spirit", "Raced over Christmas, or on New Year's Eve or Day.",
            AchievementCategory.Milestone, "\u2744", AchievementTier.Silver, "snowflake"),
        new(Globetrotter, "Globetrotter", "Raced on three different track layouts.",
            AchievementCategory.Milestone, "\u2295", AchievementTier.Bronze, "globe"),
        new(CarCollector, "Car Collector", "Raced three different cars.",
            AchievementCategory.Milestone, "\u25ad", AchievementTier.Bronze, "car"),
        new(KingOfTheHill, "King of the Hill", "Ten race wins on the same layout.",
            AchievementCategory.Milestone, "\u25b2", AchievementTier.Legend, "summit"),
        new(Bridesmaid, "Always the Bridesmaid", "Five second-place finishes. Your day is coming.",
            AchievementCategory.Milestone, "\u273f", AchievementTier.Silver, "rosette"),
        new(FourLeafClover, "Four-Leaf Clover", "Three fourth-place finishes. The podium must be due.",
            AchievementCategory.Milestone, "\u2618", AchievementTier.Bronze, "clover"),

        // --- Personal progress ---
        new(PersonalBest, "New Heights", "Beat your own best lap on a layout.",
            AchievementCategory.Progress, "↗", AchievementTier.Bronze, "arrow-up"),
        new(BigStep, "Big Step", "Beat your own best on a layout by a quarter of a second.",
            AchievementCategory.Progress, "↗", AchievementTier.Silver, "chart-up"),
        new(ThreeMeetingsImproving, "On the Up", "Improved your best lap three meetings running.",
            AchievementCategory.Progress, "⇗", AchievementTier.Gold, "chevron-up-double"),
        new(Consistent, "Machine", "Every lap of a race within 10% of your own average for it.",
            AchievementCategory.Progress, "◉", AchievementTier.Silver, "gear"),
        new(SwissWatch, "Swiss Watch", "Every lap of a race within 5% of your own average for it.",
            AchievementCategory.Progress, "⏱", AchievementTier.Legend, "stopwatch"),
        new(Unbroken, "Unbroken", "A whole race without once breaking a clean run.",
            AchievementCategory.Progress, "✔", AchievementTier.Gold, "shield"),
        new(HotStreak, "Hot Streak", "Beat your own best on a layout three times in one race.",
            AchievementCategory.Progress, "\u2668", AchievementTier.Gold, "flame"),
        new(Sharpshooter, "Sharpshooter", "Set a new personal best on three different layouts.",
            AchievementCategory.Progress, "\u25ce", AchievementTier.Gold, "target"),
        new(CleanSlate, "Clean Slate", "A spotless race after one with three or more rejected laps.",
            AchievementCategory.Progress, "\u2727", AchievementTier.Bronze, "broom"),
        new(BounceBack, "Bounce Back", "Back on the podium after five races off it.",
            AchievementCategory.Progress, "\u2934", AchievementTier.Bronze, "bounce"),

        // --- Rivalry and social ---
        new(BeatARival, "Bragging Rights", "Finished ahead of a driver you're marked as rivals with.",
            AchievementCategory.Rivalry, "⚔", AchievementTier.Silver, "swords"),
        new(PodiumRegular, "Podium Regular", "Three podium finishes.",
            AchievementCategory.Rivalry, "♒", AchievementTier.Silver, "podium"),
        new(RacedEveryone, "Knows Everyone", "Raced against ten different drivers.",
            AchievementCategory.Rivalry, "☺", AchievementTier.Bronze, "people"),
        new(TwentyOpponents, "Circuit Regular", "Raced against twenty different drivers.",
            AchievementCategory.Rivalry, "☺", AchievementTier.Gold, "people"),
        new(FiftyOpponents, "The Whole Club", "Raced against fifty different drivers.",
            AchievementCategory.Rivalry, "\u265f", AchievementTier.Gold, "people"),
        new(FullHouse, "Full House", "Won a race with six or more on track.",
            AchievementCategory.Rivalry, "♔", AchievementTier.Gold, "trophy"),
        new(GiantKiller, "Giant Killer", "Finished ahead of the club's top race winner.",
            AchievementCategory.Rivalry, "\u03a5", AchievementTier.Silver, "slingshot"),
        new(StreakBreaker, "Streak Breaker", "Beat a driver who had won their last three races.",
            AchievementCategory.Rivalry, "\u2702", AchievementTier.Gold, "chain-broken"),
        new(Payback, "Payback", "Beat the winner of your last race, the very next time out.",
            AchievementCategory.Rivalry, "\u21a9", AchievementTier.Bronze, "boomerang"),
        new(OldFriends, "Old Friends", "Twenty races against the same driver.",
            AchievementCategory.Rivalry, "\u263a", AchievementTier.Silver, "rings"),

        // --- Home and garage (Oct 2026) ---
        new(GhostBuster, "Ghost Buster", "Beat a ghost running your own best lap, in practice or a race.",
            AchievementCategory.Progress, "\u25cc", AchievementTier.Silver, "ghost"),
        new(ShadowBoxer, "Shadow Boxer", "Finished a race ahead of a live-pace ghost - one whose pace moves as the race goes.",
            AchievementCategory.Race, "\u25cc", AchievementTier.Silver, "ghost"),
        new(PracticeMakesPerfect, "Practice Makes Perfect", "A hundred laps in one practice session.",
            AchievementCategory.Milestone, "\u221e", AchievementTier.Silver, "route"),
        new(RegularAtTheTrack, "Regular at the Track", "Practised on ten different days.",
            AchievementCategory.Milestone, "\u25a6", AchievementTier.Gold, "calendar"),
        new(DialledIn, "Dialled In", "A personal best within three sessions of logging a setup on that car.",
            AchievementCategory.Progress, "\u2692", AchievementTier.Silver, "wrench"),
        new(FreshLegs, "Fresh Legs", "A personal best on a pack other than the one you run most.",
            AchievementCategory.Progress, "\u26a1", AchievementTier.Bronze, "battery"),
        new(GarageTour, "Garage Tour", "Personal bests in three different cars.",
            AchievementCategory.Progress, "\u25ad", AchievementTier.Gold, "car"),
        new(PackRotation, "Pack Rotation", "Ran three or more of your own packs in one day.",
            AchievementCategory.Milestone, "\u26a1", AchievementTier.Bronze, "battery"),
        new(BackToBack, "Back to Back", "New personal bests on two days in a row.",
            AchievementCategory.Progress, "\u21d7", AchievementTier.Silver, "chevron-up-double"),
    });

    /// <summary>Each badge with what it needs filled in (NeedsOf).</summary>
    private static IReadOnlyList<AchievementDefinition> Tagged(List<AchievementDefinition> badges) =>
        badges.Select(b => b with { Needs = NeedsOf(b.Id) }).ToList();

    /// <summary>
    /// Spotless, Wire to Wire and Through the Field each need something a
    /// saved result didn't used to carry - rejected passes, who led each
    /// lap, and where everyone started. The session record keeps all three
    /// now, but only for races recorded since: see
    /// SessionRecord.RecordsIncidents, which is what stops every race ever
    /// run retroactively looking spotless.
    /// </summary>
    public static AchievementDefinition? Get(string id) =>
        All.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));

    /// <summary>The badge's name, or its raw id if it's from a newer version than this one.</summary>
    public static string NameOf(string id) => Get(id)?.Name ?? id;
}
