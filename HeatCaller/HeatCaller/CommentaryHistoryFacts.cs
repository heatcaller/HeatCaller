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
public readonly record struct HeadToHead(
    string AId,
    string BId,
    int AAhead,
    int BAhead,
    string? LastMetDescribed = null);

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

    /// <summary>A placing counts as a podium at or above this.</summary>
    private const int Podium = 3;

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
            : new FactBlock($"debut:{raceKey}:{driver.DriverId}", $"{name} is making their first ever {Of(scope)}start at this club.");
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

        // Wins first - the shortest run that is still a run.
        var wins = RunOf(finishes, p => p == 1);

        if (wins >= 2)
        {
            return new FactBlock(key, $"{name} has won the last {Spell(wins)} {Of(scope)}races here.");
        }

        // The nearly-man: a run of the same placing, never first.
        var sameSpot = finishes[0];

        if (sameSpot is > 1 and <= Podium)
        {
            var run = RunOf(finishes, p => p == sameSpot);

            if (run >= StreakRaces)
            {
                return new FactBlock(key,
                    $"{name} has finished {CommentaryFacts.Ordinal(sameSpot)} in the last {Spell(run)} {Of(scope)}races here, and hasn't won one.");
            }
        }

        var podiums = RunOf(finishes, p => p is >= 1 and <= Podium);

        if (podiums >= StreakRaces)
        {
            return new FactBlock(key, $"{name} has been on the podium in the last {Spell(podiums)} {Of(scope)}races here.");
        }

        // A run of retirements is a story too, and the one most likely to
        // be on the driver's mind on the grid.
        var outs = RunOf(finishes, p => p <= 0);

        if (outs >= 2)
        {
            return new FactBlock(key, $"{name} hasn't finished either of the last {Spell(outs)} {Of(scope)}races.");
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
            $"{name} is still looking for a first {Of(scope)}win here, after {driver.RacesHere} starts and {podiums}.");
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
        var when = record.LastMetDescribed is { Length: > 0 } last
            ? $" They last met {CommentaryFacts.CleanLong(last)}."
            : string.Empty;

        var share = (double)Math.Max(record.AAhead, record.BAhead) / met;

        if (share >= RivalryLopsided)
        {
            var (winner, loser, score) = record.AAhead >= record.BAhead
                ? (a, b, $"{record.AAhead}-{record.BAhead}")
                : (b, a, $"{record.BAhead}-{record.AAhead}");

            return new FactBlock(key, $"{winner} has finished ahead of {loser} {score} in their {met} {Of(scope)}meetings here.{when}");
        }

        if (record.AAhead == record.BAhead)
        {
            return new FactBlock(key, $"{a} and {b} are dead level at {record.AAhead} apiece over {met} {Of(scope)}meetings here.{when}");
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
    /// <param name="raceKey">Which race, so the same fact is fresh again in the main.</param>
    /// <param name="most">How many facts at most. Four is already more than one sentence can carry.</param>
    public static IReadOnlyList<FactBlock> ForIntro(
        IReadOnlyList<DriverForm> grid,
        IReadOnlyList<HeadToHead> rivalries,
        string raceKey,
        string? scope = null,
        int most = 4)
    {
        var names = grid.ToDictionary(d => d.DriverId, d => d.Name, StringComparer.Ordinal);
        var found = new List<FactBlock>();

        foreach (var driver in grid)
        {
            if (Debut(driver, raceKey, scope) is { } debut)
            {
                found.Add(debut);
            }
        }

        foreach (var pair in rivalries)
        {
            if (Rivalry(pair, id => names.GetValueOrDefault(id), raceKey, scope) is { } rivalry)
            {
                found.Add(rivalry);
            }
        }

        foreach (var driver in grid)
        {
            if (ChasingAFirstWin(driver, raceKey, scope) is { } chasing)
            {
                found.Add(chasing);
            }
        }

        foreach (var driver in grid)
        {
            if (Streak(driver, raceKey, scope) is { } streak)
            {
                found.Add(streak);
            }
        }

        return found.Count <= most ? found : found.Take(most).ToList();
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
    /// </summary>
    private static string Spell(int count) => count switch
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
