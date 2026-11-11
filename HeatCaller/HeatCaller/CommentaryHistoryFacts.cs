namespace HeatCaller;

/// <summary>
/// A driver as the club's history knows them.
///
/// SCOPED BY WHOEVER FILLED IT IN. CommentaryReaders counts these over
/// one class's races when it knows the class, and over everything the
/// club has run when it does not - and the builders are told which, so
/// the line says so. "Sam has won the last two races here" is false the
/// moment Sam also runs Open and lost one in between; "the last two Box
/// Stock races here" is true either way, and the first fact a driver can
/// disprove is the last one anybody believes.
///
/// Worked out by the caller from DriverStats, RaceJournal and
/// SessionHistoryStore, and handed over flat. The builders below choose
/// what is worth saying and word it; they never go looking, which is what
/// keeps them a table test and keeps the readers behind them free to be
/// as slow as they like.
/// </summary>
/// <param name="DriverId">However the caller identifies a driver - a transponder, a profile id.</param>
/// <param name="Name">As it should be said.</param>
/// <param name="RacesHere">Starts at this club, across every class. Zero means a debut.</param>
/// <param name="WinsHere">Wins at this club, across every class.</param>
/// <param name="PodiumsHere">Podiums at this club, across every class.</param>
/// <param name="RecentFinishes">Finishing positions, most recent FIRST, across every class. A DNF is zero.</param>
/// <param name="LastWinDescribed">"March", "last season", "their first meeting" - the caller's words, because only it knows the calendar.</param>
/// <param name="ChampionshipPosition">Where they stand in the season, when there is one.</param>
public readonly record struct DriverForm(
    string DriverId,
    string Name,
    int RacesHere,
    int WinsHere,
    int PodiumsHere,
    IReadOnlyList<int>? RecentFinishes = null,
    string? LastWinDescribed = null,
    int? ChampionshipPosition = null);

/// <summary>
/// How two drivers have got on against each other, across everything they both race.
/// </summary>
/// <param name="AId">One of them.</param>
/// <param name="BId">The other.</param>
/// <param name="AAhead">Races where A finished ahead of B.</param>
/// <param name="BAhead">Races where B finished ahead of A.</param>
/// <param name="LastMetDescribed">"in the second heat", "at the last meeting" - the caller's words.</param>
/// <param name="LastMetMoment">
/// One thing the saved result PROVES happened in that race, already
/// worded - "Dave took the lead on the last lap in the A-Main". Null is
/// the usual answer: most races prove nothing worth a sentence.
///
/// THE LONG CALLBACK (13 Oct 2026), and the reason it took this long is
/// worth a line: <see cref="LastMetDescribed"/> has been spoken as "They
/// last met ..." since the rivalry line was written and was populated by
/// NOBODY - declared, consumed, never set. The counting reader already
/// walked every shared past race and kept two integers. See PastMeetings.
/// </param>
/// <param name="LastMetFrontRow">
/// Whether they started that race first and second, in either order -
/// which is what lets the line say "the last time these two shared a
/// front row" rather than "the last time these two met".
/// </param>
public readonly record struct HeadToHead(
    string AId,
    string BId,
    int AAhead,
    int BAhead,
    string? LastMetDescribed = null,
    string? LastMetMoment = null,
    bool LastMetFrontRow = false);

/// <summary>
/// A race's drivers as the club's history knew them BEFORE it, with the
/// class those numbers were counted over.
///
/// The three travel together or not at all. A grid handed over without
/// its scope is a set of lines that cannot say which races they counted,
/// and that is the one way a true fact turns into a false one - see
/// CommentaryHistoryFacts.Of for the wording it decides.
/// </summary>
/// <param name="Grid">The drivers, as CommentaryReaders.GridHistory worked them out.</param>
/// <param name="Rivalries">The pairs worth offering a rivalry line.</param>
/// <param name="Scope">The class the counts were scoped to; null or blank for the club as a whole.</param>
/// <param name="NightKey">
/// The race day these numbers were read on, when the caller knows it -
/// which makes the facts built from them once-a-NIGHT rather than
/// once-a-race.
///
/// WHY IT TRAVELS WITH THE HISTORY
///
/// It is the caller that read the stores, and the caller is the only thing
/// that knows which race day is open - the builders hold no state and the
/// prompt builders have no event store. Carried here rather than as one
/// more argument on every method that takes a RaceForm, because it is a
/// fact ABOUT this reading of the history and belongs with it.
///
/// Null is the old behaviour exactly: every fact keyed on its race, fresh
/// again in the next heat. See CommentaryHistoryFacts.ForIntro for what
/// that cost once the race intro started carrying this history itself.
/// </param>
public sealed record RaceForm(
    IReadOnlyList<DriverForm> Grid,
    IReadOnlyList<HeadToHead> Rivalries,
    string? Scope = null,
    string? NightKey = null);

/// <summary>
/// A race day the club ran on this date in another year, as the session
/// history knows it.
///
/// Flat, counted and handed over, like every other record in this file -
/// the builder below words it and never goes looking. Everything in it is
/// read from races the club actually ran and saved: there is no "on this
/// day the club was founded", because nothing on this laptop knows that
/// and a founding date read out over the PA would be the one kind of
/// colour that cannot be invented.
/// </summary>
/// <param name="YearsAgo">How many years back, at least one.</param>
/// <param name="Races">How many races it ran. Practice and demo sessions are left out by the reader.</param>
/// <param name="Drivers">How many different drivers raced that day. Zero when the records name none.</param>
/// <param name="HeadlineLabel">The race worth naming, as the club labelled it. Blank for none.</param>
/// <param name="HeadlineWinner">Who won that race, as the result was SAVED - see CommentaryReaders.OnThisDay for why the saved name and not the profile's.</param>
public sealed record ClubRaceDay(
    int YearsAgo,
    int Races,
    int Drivers,
    string? HeadlineLabel = null,
    string? HeadlineWinner = null);

/// <summary>
/// The championship a race is about to count towards, as the table stands
/// BEFORE the race is run - what this one has at stake.
///
/// WHY THERE IS NO "A WIN HERE AND HE LEADS" IN HERE
///
/// It is the line a club would most like to hear and it is not this app's
/// to say. What a result does to a table depends on where everyone ELSE
/// finishes, and the drop rule ("best 5 of 7") means there is no
/// arithmetic shortcut that survives - Championship.SwingFor recomputes
/// the whole table twice rather than subtract, for exactly that reason. A
/// predicted swing that turns out wrong is the timing system telling
/// forty people something untrue over the PA, which is worse than saying
/// nothing. So this carries the positions, the totals and the gaps as
/// they ARE, and the prompt that reads it forbids turning any of it into
/// a prediction.
/// </summary>
/// <param name="SeasonName">The championship, as the club named it.</param>
/// <param name="RoundsCounted">How many races the table already counts - NOT including this one, which has not run.</param>
/// <param name="LeaderName">Whoever leads it as things stand.</param>
/// <param name="LeaderInThisRace">Whether that leader is on this grid. False means the gap needs them named.</param>
/// <param name="OnThisGrid">The drivers in this race who are in the table, best placed first.</param>
public sealed record ChampionshipAtStake(
    string SeasonName,
    int RoundsCounted,
    string LeaderName,
    bool LeaderInThisRace,
    IReadOnlyList<ChampionshipStanding> OnThisGrid);

/// <summary>
/// The history that turns an intro into a broadcast.
///
/// WHY THIS IS WHERE THE DEPTH IS
///
/// "Sam is on pole for the A-main" is a fact the scoreboard already shows.
/// "Sam is on pole for the A-main, and has been second here four times
/// this season without winning it" is the same fact with a reason to care
/// about it, and nobody in the room knew the second half.
///
/// WHAT IT WILL NOT SAY
///
/// Everything in here has a threshold, and the thresholds are the whole
/// design. A head-to-head of three-two is not a rivalry; two podiums in
/// five races is not a run of form. A segment that dresses up ordinary
/// numbers as drama is the fastest way to make every line sound the same,
/// because the audience quickly learns the superlatives mean nothing. So
/// each builder returns null far more often than it returns a fact, and
/// the tests below are mostly about the nulls.
/// </summary>
public static class CommentaryHistoryFacts
{
    /// <summary>How many starts before never having won is a drought rather than a start.</summary>
    public const int DroughtStarts = 8;

    /// <summary>How many meetings two drivers need before their record against each other is a record.</summary>
    public const int RivalryMeetings = 5;

    /// <summary>And how one-sided it has to be, as a share, before it is worth remarking on.</summary>
    public const double RivalryLopsided = 0.75;

    /// <summary>How many races in a row make a run rather than a coincidence.</summary>
    public const int StreakRaces = 3;

    /// <summary>
    /// How many history lines a RECAP carries.
    ///
    /// Three where an intro takes four, and deliberately fewer: the recap
    /// already carries a finishing order, a closeness, the winner's
    /// sponsors, a fastest lap, the rivalry outcomes and the championship,
    /// all for a local llama3.2:3b to hold at once. The recap's own word
    /// budget is the limit that bites first, and a fact that is squeezed
    /// out of the answer cost the prompt its room for nothing.
    /// </summary>
    public const int RecapFacts = 3;

    /// <summary>
    /// How many starts before "they had N starts here" is a record rather
    /// than a sentence about somebody's second night out.
    /// </summary>
    public const int CareerStarts = 5;

    /// <summary>
    /// How many recent results a driver needs before they have "form" at
    /// all. The same bar as a streak, for the same reason: two results is
    /// a coincidence, and a form guide built on one is a sentence about
    /// nothing that teaches a crowd to stop listening to the rest.
    /// </summary>
    public const int FormRaces = StreakRaces;

    /// <summary>
    /// How many drivers a form guide talks about.
    ///
    /// Three, where an intro takes four and a recap three. A form guide is
    /// one or two sentences out of a local llama3.2:3b, and a grid's worth
    /// of placings handed to a 3B model is how one driver's results get
    /// read out against another driver's name.
    /// </summary>
    public const int FormGuideFacts = 3;

    /// <summary>
    /// How many of one driver's recent results a form line reads out.
    ///
    /// Four. Longer is not more informative out loud - nobody holds six
    /// placings in their head off a PA - and it is the length at which the
    /// line stops being a sentence and becomes a list.
    /// </summary>
    public const int FormGuideRaces = 4;

    /// <summary>
    /// How many past race days "this day at the club" carries.
    ///
    /// Two, and the segment is told to pick one. The spare is there so the
    /// pack is not empty when the night has already spent the first -
    /// which, for a once-a-meeting flourish, is mostly theoretical and
    /// costs two lines of prompt to be safe about.
    /// </summary>
    public const int ThisDayFacts = 2;

    /// <summary>A placing counts as a podium at or above this.</summary>
    private const int Podium = 3;

    /// <summary>
    /// The longest a rivalry's last-meeting moment may run (10 Nov 2026,
    /// bug hunt 16). Room for DriverCut.Describe's longest sentence - the
    /// lap record one - with a 40-character name and a 40-character label,
    /// and some to spare; only a guard, never meant to cut a real one.
    /// </summary>
    private const int MaxMomentChars = 200;

    /// <summary>
    /// The class as an adjective - "Box Stock " - or nothing.
    ///
    /// Every line here is built from numbers the reader scoped to one
    /// class, so every line has to say which. "Sam has won the last two
    /// races here" is false the moment Sam also runs Open and lost one in
    /// between; "the last two Box Stock races here" is true either way.
    /// Blank when the reader could not scope it, and then the line is
    /// about the club as a whole - which is what it says.
    /// </summary>
    private static string Of(string? scope)
    {
        var cleaned = CommentaryFacts.Clean(scope);

        return cleaned.Length > 0 ? cleaned + " " : string.Empty;
    }

    /// <summary>
    /// Somebody's first ever start at this club.
    ///
    /// The highest payload per character in the whole layer: it costs one
    /// integer to know, it is never not true when it is true, and it is
    /// the line the driver's family remembers.
    /// </summary>
    public static FactBlock? Debut(DriverForm driver, string raceKey, string? scope = null)
    {
        if (driver.RacesHere > 0)
        {
            return null;
        }

        var name = CommentaryFacts.Clean(driver.Name);

        return name.Length == 0
            ? null
            : new FactBlock(
                $"debut:{raceKey}:{driver.DriverId}",
                $"{name} is making their first ever {Of(scope)}start at this club.",
                FactTheme.Driver(driver.DriverId));
    }

    /// <summary>
    /// A run of results, good or bad.
    ///
    /// The bad one matters as much as the good. "Second four times in a
    /// row and still hasn't won it" is a better story than a fourth win,
    /// and it is the kind of thing only the app could know.
    /// </summary>
    public static FactBlock? Streak(DriverForm driver, string raceKey, string? scope = null)
    {
        var finishes = driver.RecentFinishes;

        if (finishes is null || finishes.Count < StreakRaces)
        {
            return null;
        }

        var name = CommentaryFacts.Clean(driver.Name);

        if (name.Length == 0)
        {
            return null;
        }

        var key = $"streak:{raceKey}:{driver.DriverId}";

        // Themed on the driver, so the form guide, the intro and the recap
        // cannot all land on the same person's season inside their own
        // separate budgets - see FactBlock.Theme.
        var theme = FactTheme.Driver(driver.DriverId);

        // Wins first - the shortest run that is still a run.
        var wins = RunOf(finishes, p => p == 1);

        if (wins >= 2)
        {
            return new FactBlock(key, $"{name} has won the last {Spell(wins)} {Of(scope)}races here.", theme);
        }

        // The nearly-man: a run of the same placing, never first.
        var sameSpot = finishes[0];

        if (sameSpot is > 1 and <= Podium)
        {
            var run = RunOf(finishes, p => p == sameSpot);

            if (run >= StreakRaces)
            {
                return new FactBlock(key,
                    $"{name} has finished {CommentaryFacts.Ordinal(sameSpot)} in the last {Spell(run)} {Of(scope)}races here, and hasn't won one.",
                    theme);
            }
        }

        var podiums = RunOf(finishes, p => p is >= 1 and <= Podium);

        if (podiums >= StreakRaces)
        {
            return new FactBlock(key, $"{name} has been on the podium in the last {Spell(podiums)} {Of(scope)}races here.", theme);
        }

        // A run of retirements is a story too, and the one most likely to
        // be on the driver's mind on the grid.
        var outs = RunOf(finishes, p => p <= 0);

        if (outs >= 2)
        {
            // "Either" only fits two.
            var which = outs == 2 ? "either" : "any";

            return new FactBlock(key, $"{name} hasn't finished {which} of the last {Spell(outs)} {Of(scope)}races.", theme);
        }

        return null;
    }

    /// <summary>
    /// Somebody who keeps getting close and hasn't won here.
    ///
    /// Only after enough starts that it is a pattern rather than a career
    /// just beginning, and only with podiums behind it - "has never won in
    /// eight starts" about a driver who has never been near the podium is
    /// not a story, it is a slight.
    /// </summary>
    public static FactBlock? ChasingAFirstWin(DriverForm driver, string raceKey, string? scope = null)
    {
        if (driver.WinsHere > 0 || driver.RacesHere < DroughtStarts || driver.PodiumsHere == 0)
        {
            return null;
        }

        var name = CommentaryFacts.Clean(driver.Name);

        if (name.Length == 0)
        {
            return null;
        }

        var podiums = driver.PodiumsHere == 1 ? "a podium" : $"{driver.PodiumsHere} podiums";

        return new FactBlock($"firstwin:{raceKey}:{driver.DriverId}",
            $"{name} is still looking for a first {Of(scope)}win here, after {driver.RacesHere} starts and {podiums}.",
            FactTheme.Driver(driver.DriverId));
    }

    /// <summary>
    /// What gets appended to a rivalry line about the last time these two
    /// actually raced each other - the long callback.
    ///
    /// THREE SHAPES, and the first is the one the feature is for:
    ///
    ///   a shared FRONT ROW with something provable in it - "The last time
    ///   these two shared a front row, Dave took the lead on the last lap
    ///   in the A-Main." The grid slot is what makes it a story rather
    ///   than a statistic;
    ///
    ///   a meeting with something provable in it - "They last met three
    ///   weeks ago. Dave took the lead on the last lap in the A-Main.";
    ///
    ///   and a meeting that proves nothing, which is most of them - "They
    ///   last met in the A-Main three weeks ago." The clause carries the
    ///   label in that case and only that case, so the room is never told
    ///   it twice - see PastMeetings.Clause.
    ///
    /// Empty when the pair have no recallable meeting, and then the
    /// rivalry line reads exactly as it did before any of this.
    ///
    /// The moment is left capitalised after the comma on purpose: it
    /// starts with a driver's name, and a driver called Dave does not
    /// become "dave" because of where the comma fell.
    /// </summary>
    private static string LastMeeting(HeadToHead record)
    {
        // AT ITS OWN LENGTH, not CleanLong's sixty characters (10 Nov 2026,
        // bug hunt 16). The moment is a whole sentence DriverCut.Describe
        // already built from a 40-character name and a 40-character label,
        // and sixty cut it mid-number: "won in Round 3 Box Stock A-Main by
        // 0.0" - a winning margin of nothing, said as a fact. Field still
        // takes out the brackets; it just no longer chops the sentence.
        var moment = record.LastMetMoment is { Length: > 0 } what
            ? CommentaryService.Field(what, MaxMomentChars)
            : string.Empty;

        if (record.LastMetFrontRow && moment.Length > 0)
        {
            return $" The last time these two shared a front row, {moment}";
        }

        if (record.LastMetDescribed is not { Length: > 0 } last)
        {
            return string.Empty;
        }

        var met = $" They last met {CommentaryFacts.CleanLong(last)}.";

        return moment.Length > 0 ? $"{met} {moment}" : met;
    }

    /// <summary>
    /// Two drivers whose record against each other is worth knowing.
    ///
    /// Worth knowing means one of two things: one of them nearly always
    /// comes out ahead, or they are dead level over a lot of races. A
    /// four-three record is neither, and saying it out loud is how a
    /// broadcast starts sounding like a database being read aloud.
    /// </summary>
    public static FactBlock? Rivalry(HeadToHead record, Func<string, string?> nameFor, string raceKey, string? scope = null)
    {
        var met = record.AAhead + record.BAhead;

        if (met < RivalryMeetings)
        {
            return null;
        }

        var a = CommentaryFacts.Clean(nameFor(record.AId));
        var b = CommentaryFacts.Clean(nameFor(record.BId));

        if (a.Length == 0 || b.Length == 0)
        {
            return null;
        }

        var key = $"h2h:{raceKey}:{CommentaryFacts.Pair(record.AId, record.BId)}";

        // The PAIR, not either driver: a head-to-head said once does not
        // mean neither of them may be talked about again, and a battle
        // call about the same two does. See FactTheme.Rivalry.
        var theme = FactTheme.Rivalry(record.AId, record.BId);
        var when = LastMeeting(record);

        var share = (double)Math.Max(record.AAhead, record.BAhead) / met;

        if (share >= RivalryLopsided)
        {
            var (winner, loser, score) = record.AAhead >= record.BAhead
                ? (a, b, $"{record.AAhead}-{record.BAhead}")
                : (b, a, $"{record.BAhead}-{record.AAhead}");

            return new FactBlock(key, $"{winner} has finished ahead of {loser} {score} in their {met} {Of(scope)}meetings here.{when}", theme);
        }

        if (record.AAhead == record.BAhead)
        {
            return new FactBlock(key, $"{a} and {b} are dead level at {record.AAhead} apiece over {met} {Of(scope)}meetings here.{when}", theme);
        }

        return null;
    }

    /// <summary>
    /// What tonight's result means for the driver who just took it - the
    /// recap's half of the history.
    /// </summary>
    /// <param name="driver">Their record BEFORE this race. The caller must not have counted tonight in yet, or every win is their first.</param>
    /// <param name="finishPosition">Where they came. One is a win.</param>
    public static FactBlock? ResultInContext(DriverForm driver, int finishPosition, string raceKey, string? scope = null)
    {
        var name = CommentaryFacts.Clean(driver.Name);

        if (name.Length == 0 || finishPosition <= 0)
        {
            return null;
        }

        var key = $"result:{raceKey}:{driver.DriverId}";

        if (finishPosition == 1)
        {
            if (driver.WinsHere == 0)
            {
                return new FactBlock(key, driver.RacesHere > 1
                    ? $"That is {name}'s first ever {Of(scope)}win here, at the {CommentaryFacts.Ordinal(driver.RacesHere + 1)} attempt."
                    : $"That is {name}'s first ever {Of(scope)}win here.");
            }

            if (driver.LastWinDescribed is { Length: > 0 } since)
            {
                return new FactBlock(key,
                    $"{name}'s {CommentaryFacts.Ordinal(driver.WinsHere + 1)} {Of(scope)}win here, and the first since {CommentaryFacts.CleanLong(since)}.");
            }

            return new FactBlock(key, $"{name}'s {CommentaryFacts.Ordinal(driver.WinsHere + 1)} {Of(scope)}win here.");
        }

        if (finishPosition <= Podium && driver.PodiumsHere == 0)
        {
            return new FactBlock(key, $"{name}'s first ever {Of(scope)}podium here.");
        }

        return null;
    }

    /// <summary>
    /// A finisher's record at this club as it stood BEFORE tonight - the
    /// plain numbers, said plainly.
    ///
    /// THE ONLY DRIVER-FORM FACT THAT CANNOT CONTRADICT A RESULT
    ///
    /// Streak and ChasingAFirstWin are both written in the present tense
    /// and both counted from the races BEFORE this one - "and hasn't won
    /// one", "is still looking for a first win here". Either of them read
    /// out over a recap of a race that driver has just WON sounds like the
    /// timing system getting it wrong in front of the room, which is the
    /// one thing it must never sound like. So the recap gets this instead:
    /// what the numbers were, and when they were counted, in the line
    /// itself so the model cannot quietly move it to the present.
    /// </summary>
    /// <param name="driver">Their record BEFORE this race - the same caller promise ResultInContext needs.</param>
    public static FactBlock? RecordBeforeTonight(DriverForm driver, string raceKey, string? scope = null)
    {
        if (driver.RacesHere < CareerStarts)
        {
            return null;
        }

        var name = CommentaryFacts.Clean(driver.Name);

        if (name.Length == 0)
        {
            return null;
        }

        // "no wins" rather than "0 wins": these lines are written to be
        // spoken, and a read-out zero is how a recap starts sounding like a
        // spreadsheet.
        var wins = driver.WinsHere == 0 ? "no wins" : Plural.Of(driver.WinsHere, "win");
        var podiums = driver.PodiumsHere == 0 ? "no podiums" : Plural.Of(driver.PodiumsHere, "podium");

        return new FactBlock($"career:{raceKey}:{driver.DriverId}",
            $"Before this race {name} had {Plural.Of(driver.RacesHere, "start")} in {Of(scope)}races here, with {wins} and {podiums}.",
            FactTheme.Driver(driver.DriverId));
    }

    /// <summary>
    /// The history worth putting in a race intro, best first.
    ///
    /// Best first is not decoration: FactPack keeps blocks in the order it
    /// is given them and drops the tail when the room runs out, so the
    /// order here is what survives a squeeze. A debut beats a rivalry
    /// beats a run of form, because that is the order a commentator would
    /// reach for them.
    /// </summary>
    /// <param name="grid">The drivers in the race, as the history knows them.</param>
    /// <param name="rivalries">Pairs worth checking - usually the front row and anyone the caller knows has previous.</param>
    /// <param name="raceKey">Which race. Used only when no <paramref name="nightKey"/> is given - see the remarks.</param>
    /// <param name="most">How many facts at most. Four is already more than one sentence can carry.</param>
    /// <param name="nightKey">
    /// The race day, when the caller wants these facts to be once-a-night
    /// rather than once-a-race. See the remarks - a caller speaking an
    /// intro before every heat must pass it.
    /// </param>
    /// <remarks>
    /// WHY THE KEY CAN BE THE NIGHT'S AND NOT THE RACE'S (10 Oct 2026)
    ///
    /// Every fact this returns is true for the whole EVENING, not for one
    /// race: a driver making their first ever start is making it once, a
    /// head-to-head record of seven-two is seven-two all night, a drought
    /// of eight starts is the same drought in the main. Keyed on the race,
    /// the same fact is a different key in every heat - which is exactly
    /// what the race key was FOR while this had one caller, the grid
    /// history segment, whose own budget kept it to a few a night.
    ///
    /// The race intro is not that. It is an Essential: it runs before
    /// every race, it is never silenced by budget, and since 10 Oct 2026
    /// it carries this history itself. So a club running six heats with
    /// the same ten entries heard "Billy is making their first ever Box
    /// Stock start" six times - a sharper version of the very complaint
    /// this work answers, made worse rather than better by the new depth.
    /// SaidTonight cannot help with a key that changes every race.
    ///
    /// So the caller says which it wants. An intro spoken per race passes
    /// the race day and each of these lands once a night; the grid history
    /// segment passes it too, so a fact the intro has already spent is not
    /// offered back a second time by another segment. Left null, the keys
    /// are exactly what they were, which is what keeps every table test of
    /// this builder honest about one race at a time.
    /// </remarks>
    public static IReadOnlyList<FactBlock> ForIntro(
        IReadOnlyList<DriverForm> grid,
        IReadOnlyList<HeadToHead> rivalries,
        string raceKey,
        string? scope = null,
        int most = 4,
        string? nightKey = null)
    {
        // One decision for all four builders below, because all four are
        // true for the night rather than for the race. See the remarks.
        var key = string.IsNullOrWhiteSpace(nightKey) ? raceKey : nightKey;

        var names = grid.ToDictionary(d => d.DriverId, d => d.Name, StringComparer.Ordinal);
        var found = new List<FactBlock>();

        foreach (var driver in grid)
        {
            if (Debut(driver, key, scope) is { } debut)
            {
                found.Add(debut);
            }
        }

        foreach (var pair in rivalries)
        {
            if (Rivalry(pair, id => names.GetValueOrDefault(id), key, scope) is { } rivalry)
            {
                found.Add(rivalry);
            }
        }

        foreach (var driver in grid)
        {
            if (ChasingAFirstWin(driver, key, scope) is { } chasing)
            {
                found.Add(chasing);
            }
        }

        foreach (var driver in grid)
        {
            if (Streak(driver, key, scope) is { } streak)
            {
                found.Add(streak);
            }
        }

        return found.Count <= most ? found : found.Take(most).ToList();
    }

    /// <summary>
    /// One driver's recent results, read out in order - the form guide's
    /// whole substance, and the segment that could never say anything
    /// until 10 Oct 2026.
    ///
    /// WHY IT IS A LIST OF PLACINGS AND NOT A VERDICT
    ///
    /// "On good form" is a judgement, and a judgement about a driver read
    /// out over a PA is the kind of thing the driver's family argues with.
    /// "Second, first, fourth, second" is the club's own saved results in
    /// the club's own order, and nobody in the room can contradict it -
    /// which is the only reason any of this layer lands at all. The model
    /// is free to call it a good run; the numbers it is calling that are
    /// exact.
    ///
    /// Null for anyone with less than <see cref="FormRaces"/> behind them,
    /// which is what makes a club in its first week get a form guide about
    /// nobody rather than a form guide about one race.
    /// </summary>
    /// <param name="driver">Their record as the history knows it - most recent result FIRST, as DriverForm promises.</param>
    /// <param name="raceKey">Which race. The race and NOT the night: a heat gets filed between races, so the window really does change.</param>
    public static FactBlock? FormLine(DriverForm driver, string raceKey, string? scope = null)
    {
        var finishes = driver.RecentFinishes;

        if (finishes is null || finishes.Count < FormRaces)
        {
            return null;
        }

        var name = CommentaryFacts.Clean(driver.Name);

        if (name.Length == 0)
        {
            return null;
        }

        var read = finishes.Take(FormGuideRaces).ToList();

        // "most recent first" said in the line itself, not left to the
        // order of the words. A model handed four numbers and no direction
        // reads them whichever way makes the better sentence, and a run of
        // form read backwards is a different and false story.
        // AND WHERE THEY STAND IN THE TITLE (7 Nov 2026), when the caller
        // knows - the before-this-race table, so it is a place they hold
        // now, not one this race will give them.
        var title = driver.ChampionshipPosition is int place && place > 0
            ? $" {name} is {CommentaryFacts.Ordinal(place)} in the championship going into this race."
            : string.Empty;

        return new FactBlock(
            $"form:{raceKey}:{driver.DriverId}",
            $"{name}'s last {Spell(read.Count)} {Of(scope)}races here, most recent first: {string.Join(", ", read.Select(Placing))}.{title}",
            FactTheme.Driver(driver.DriverId));
    }

    /// <summary>
    /// The form guide for a race's grid, best story first.
    ///
    /// BEST STORY MEANS BEST RECENT RESULT, and that is a choice worth
    /// stating: a form guide that read the grid out in grid order would
    /// spend its one or two sentences on whoever happened to be in the
    /// first slot. The driver with a win in the window is the one a
    /// commentator reaches for, so they are the one that survives the cap
    /// and the character squeeze - FactPack keeps the order it is given.
    ///
    /// A retirement sorts last rather than first, which is why it is
    /// compared as the worst possible placing: "did not finish, did not
    /// finish" is a story, but it is not the one to lead a form guide
    /// with.
    /// </summary>
    /// <param name="grid">The drivers in the race, as the history knows them.</param>
    /// <param name="raceKey">Which race - see <see cref="FormLine"/>.</param>
    /// <param name="scope">The class the counts were scoped to, so the lines say which races they counted.</param>
    /// <param name="most">How many drivers at most. Three - see <see cref="FormGuideFacts"/>.</param>
    public static IReadOnlyList<FactBlock> FormGuide(
        IReadOnlyList<DriverForm> grid,
        string raceKey,
        string? scope = null,
        int most = FormGuideFacts)
    {
        var found = new List<FactBlock>();

        var ranked = grid
            .Where(form => form.RecentFinishes is { } recent && recent.Count >= FormRaces)
            .OrderBy(form => Best(form.RecentFinishes!))
            .ThenByDescending(form => form.RecentFinishes!.Count)
            .ToList();

        foreach (var driver in ranked)
        {
            if (FormLine(driver, raceKey, scope) is { } line)
            {
                found.Add(line);
            }

            if (found.Count >= Math.Max(1, most))
            {
                break;
            }
        }

        return found;
    }

    /// <summary>
    /// What happened at this club on this date in other years - the
    /// second segment that had a tick box, a title and a budget and could
    /// never say a word.
    ///
    /// Everything in it is read from races the club ran and saved. There
    /// is no invented club lore in here and there must not be: a rivalry
    /// the app described as older than its records go reads as atmosphere
    /// to whoever wrote it and as a false claim to the driver who was
    /// there. See CommentaryReaders.OnThisDay for what is counted.
    ///
    /// Empty for a club in its first year, which is most clubs that have
    /// just installed this - and empty is the right answer, said once
    /// here rather than padded into "nothing much happened".
    /// </summary>
    /// <param name="days">The past race days, most recent first, as the reader found them.</param>
    /// <param name="dayKey">Tonight, so the same look-back is fresh next year.</param>
    /// <param name="most">How many at most. Two - see <see cref="ThisDayFacts"/>.</param>
    public static IReadOnlyList<FactBlock> ThisDayAtTheClub(
        IReadOnlyList<ClubRaceDay> days,
        string dayKey,
        int most = ThisDayFacts)
    {
        var found = new List<FactBlock>();

        foreach (var day in days)
        {
            // A day with no races in it is not a day the club raced, and
            // "this year" is not another year.
            if (day.YearsAgo < 1 || day.Races < 1)
            {
                continue;
            }

            // Capitalised, because this opens the sentence. Spell writes
            // "two" for the body of a line, the prompt shows these facts
            // exactly as written, and a model handed a lower-case opener
            // copies it - so the PA got "two years ago today the club..."
            // with no capital anywhere in the line.
            var spelled = Spell(day.YearsAgo);

            var ago = day.YearsAgo == 1
                ? "A year ago today"
                : $"{char.ToUpperInvariant(spelled[0])}{spelled[1..]} years ago today";

            var text = new System.Text.StringBuilder($"{ago} the club ran {Plural.Of(day.Races, "race")} here");

            text.Append(day.Drivers > 0 ? $", with {Plural.Of(day.Drivers, "driver")} out." : ".");

            var winner = CommentaryFacts.Clean(day.HeadlineWinner);

            if (winner.Length > 0)
            {
                var label = CommentaryFacts.CleanLong(day.HeadlineLabel);

                text.Append(label.Length > 0 ? $" {winner} won the {label}." : $" {winner} won it.");
            }

            found.Add(new FactBlock(
                $"thisday:{dayKey}:{day.YearsAgo}",
                text.ToString(),
                FactTheme.PastDay(day.YearsAgo)));

            if (found.Count >= Math.Max(1, most))
            {
                break;
            }
        }

        return found;
    }

    /// <summary>
    /// The history worth putting in a race RECAP, best first - the half of
    /// this layer that had no caller at all until 10 Oct 2026.
    ///
    /// WHAT THE RECAP WAS MISSING
    ///
    /// The recap is the longest and most-heard segment of the night, and
    /// everything it had was the finishing order and a note on how close
    /// it was. Two races with the same ten drivers read out the same way,
    /// which is exactly what the club owner heard. ResultInContext - "he
    /// has never finished this high" - was written for precisely this
    /// moment and nothing ever called it.
    ///
    /// WHAT IS DELIBERATELY NOT HERE
    ///
    /// Debut ("is making their first ever start") is present tense about a
    /// race that has finished. Streak and ChasingAFirstWin can contradict
    /// the result outright - see RecordBeforeTonight. Rivalry reads as a
    /// career record and would be heard as including tonight, so a 7-2
    /// would go out over the PA when the true figure is now 8-2; the recap
    /// already has the rivalry's actual OUTCOME from
    /// CommentaryService.RivalryOutcomesFor, which is the better fact and
    /// cannot be wrong. So the recap gets the two builders that can only
    /// ever be true after a flag, and no others.
    /// </summary>
    /// <param name="grid">The drivers as the history knew them BEFORE this race. Counting tonight in makes every win their first.</param>
    /// <param name="finished">Who finished where in the race just run. A retirement is zero or less and is skipped.</param>
    /// <param name="raceKey">Which race, so the same fact is fresh again at the next meeting.</param>
    /// <param name="most">How many facts at most. Three - see <see cref="RecapFacts"/>.</param>
    /// <param name="nightKey">
    /// The race day, for the one fact in here that is true for the whole
    /// evening rather than for this race - the winner's career before
    /// tonight. See <see cref="ForIntro"/> for the argument; the
    /// difference is that a RESULT really is once per race, so
    /// ResultInContext keeps the race's own key and "that is his first
    /// ever win here" can still be said about the next race he wins.
    /// </param>
    public static IReadOnlyList<FactBlock> ForRecap(
        IReadOnlyList<DriverForm> grid,
        IReadOnlyList<(string DriverId, int Position)> finished,
        string raceKey,
        string? scope = null,
        int most = RecapFacts,
        string? nightKey = null)
    {
        var found = new List<FactBlock>();

        // In finishing order, so the winner's line is the one that
        // survives a squeeze - FactPack keeps the order it is given.
        var order = finished
            .Where(f => f.Position > 0 && !string.IsNullOrWhiteSpace(f.DriverId))
            .OrderBy(f => f.Position)
            .ToList();

        if (grid.Count == 0 || order.Count == 0)
        {
            return found;
        }

        // OrdinalIgnoreCase: a DriverId here is a transponder, and that is
        // how every other comparison in this app reads one.
        var form = new Dictionary<string, DriverForm>(StringComparer.OrdinalIgnoreCase);

        foreach (var driver in grid)
        {
            if (!string.IsNullOrWhiteSpace(driver.DriverId))
            {
                form[driver.DriverId] = driver;
            }
        }

        foreach (var (driverId, position) in order)
        {
            if (form.TryGetValue(driverId, out var driver)
                && ResultInContext(driver, position, raceKey, scope) is { } result)
            {
                // THE WINNER'S LINE CARRIES NO THEME AND EVERY OTHER ONE
                // DOES, and the difference is the whole of FactBlock.Theme.
                // "That is Sam's first ever win here" is what the recap IS;
                // a variety rule that could hold it back because the intro
                // mentioned Sam ten minutes ago would be a far worse fault
                // than the repetition it exists to stop. P2's and P3's
                // lines are colour on top of a result the recap reads out
                // anyway, so those may be set aside.
                // Compared on the driver and not on the position: a
                // corrected result can leave two entries sharing a place,
                // and the winner is whoever the order puts first.
                found.Add(string.Equals(driverId, order[0].DriverId, StringComparison.OrdinalIgnoreCase)
                    ? result
                    : result with { Theme = FactTheme.Driver(driverId) });
            }
        }

        // Then the winner's record, last: the winner is the one driver the
        // recap is certain to name anyway, so it is the one career worth
        // the room, and it goes behind every result line because what just
        // happened beats what had happened before.
        if (form.TryGetValue(order[0].DriverId, out var winner)
            && RecordBeforeTonight(winner, string.IsNullOrWhiteSpace(nightKey) ? raceKey : nightKey, scope) is { } career)
        {
            found.Add(career);
        }

        return found.Count <= most ? found : found.Take(most).ToList();
    }

    /// <summary>
    /// A placing as a form guide reads it out - or what a retirement is
    /// called, which is a word and never a zero. DriverForm holds a DNF as
    /// a zero and "nought" read over a PA is a timing fault, not a result.
    /// </summary>
    private static string Placing(int finish) =>
        finish <= 0 ? "did not finish" : CommentaryFacts.Ordinal(finish);

    /// <summary>
    /// The best placing in a window, with a retirement counted as the
    /// worst there is rather than as a zero - which would otherwise sort
    /// every driver who broke down to the top of a form guide.
    /// </summary>
    private static int Best(IReadOnlyList<int> finishes)
    {
        var best = int.MaxValue;

        foreach (var finish in finishes)
        {
            if (finish > 0 && finish < best)
            {
                best = finish;
            }
        }

        return best;
    }

    /// <summary>How many of the most recent results in a row match.</summary>
    private static int RunOf(IReadOnlyList<int> finishes, Func<int, bool> matches)
    {
        var run = 0;

        foreach (var finish in finishes)
        {
            if (!matches(finish))
            {
                break;
            }

            run++;
        }

        return run;
    }

    /// <summary>
    /// "two", "three" - small counts read as words in speech, and these
    /// lines are written to be spoken rather than shown.
    ///
    /// Internal rather than private since 11 Oct 2026: CommentaryRaceFacts
    /// reads counts out too, and two spellings of the same count in one
    /// broadcast - "six of the 8 drivers" - is the sort of difference
    /// nobody notices in review and everybody hears on the night.
    /// </summary>
    internal static string Spell(int count) => count switch
    {
        2 => "two",
        3 => "three",
        4 => "four",
        5 => "five",
        6 => "six",
        7 => "seven",
        8 => "eight",
        9 => "nine",
        _ => count.ToString(),
    };
}
