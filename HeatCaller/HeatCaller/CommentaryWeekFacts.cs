namespace HeatCaller;

/// <summary>
/// The shape of a driver's last few WEEKS in one class, as the club's own
/// filed race nights have it.
///
/// WHY THIS IS NOT THE FORM GUIDE (11 Oct 2026)
///
/// <see cref="CommentaryHistoryFacts.FormGuide"/> reads a driver's last few
/// placings out in order, most recent first - "fourth, second, first,
/// sixth". That is a form guide, and it is what the depth plan's Tier 1
/// item 2 was marked done against. It was not what the plan asked for.
///
/// What the plan asked for was the shape of the last month: "third podium
/// in four weeks, and the two he missed he did not finish". The difference
/// is not detail, it is the UNIT. A form guide counts races; this counts
/// NIGHTS, and a night is what a club member's memory is organised by. "He
/// has been on the podium three weeks running" is a thing a regular would
/// say to another regular in the pits. "Fourth, second, first, sixth" is a
/// results sheet read aloud.
///
/// Counting nights is also what made it hard, and why DriverForm could not
/// be extended to do it: a night is a DATE, and the shape needs the nights
/// the driver was absent from as much as the ones they raced. DriverForm
/// carries counts and a list of placings with no window on it, so a run of
/// three podiums in it could be three weeks or three years, and a driver
/// who missed a month looks identical to one who did not.
///
/// WHAT EVERY LINE HERE HAS TO SURVIVE
///
/// A regular's own memory. These are claims about somebody standing in the
/// room, made to a room of people who were also there - which is a harder
/// audience than any statistic in this app has had. So:
///
///   THE WINDOW IS IN THE LINE. "Three weeks running" without the window
///   is a claim about a career. Every line below says the number of nights
///   it counted and the class it counted them in, for the reason
///   CommentaryRaceFacts states at length about scope.
///
///   NIGHTS THE CLUB RAN, NOT NIGHTS ON A CALENDAR. A club that skipped a
///   fortnight for a holiday has not had a driver miss two weeks. The
///   window is the club's own last few race nights in this class, so a
///   run of three is three of the club's nights whether they fell in
///   three weeks or five.
///
///   A RUN IS BROKEN BY A NIGHT THEY WERE OUT AT AND DID NOT MANAGE IT -
///   never by a night they missed. A driver away for a fortnight has not
///   had his podium run ended by being on holiday, and a room that was
///   there knows it.
///
/// Built from what <see cref="CommentaryReaders.RecentWeeks"/> worked out
/// and handed over flat, so every builder here is a table test that cannot
/// reach a store.
/// </summary>
public static class CommentaryWeekFacts
{
    /// <summary>
    /// How many of the club's recent race nights in a class the window
    /// covers.
    ///
    /// Six. A club racing most weeks is about six weeks of racing, which is
    /// the span a member actually holds - "lately" rather than "this
    /// season". Wider and a run of three stops being news; narrower and a
    /// club racing fortnightly has no window at all.
    /// </summary>
    public const int NightsInWindow = 6;

    /// <summary>
    /// How many of the club's nights in the window a driver has to have
    /// been out at before anything here is said about them.
    ///
    /// Three. Two nights is not a shape - every line below would be a
    /// claim about somebody's whole short history at the club, which is
    /// the superlative-with-nothing-behind-it fault every threshold in
    /// CommentaryRaceFacts exists to avoid.
    /// </summary>
    public const int LeastNightsOut = 3;

    /// <summary>
    /// How many nights running make a run.
    ///
    /// Three. Two in a row is a coincidence, and the same number
    /// CommentaryRaceFacts.LeastRunToCall settles on for the same reason.
    /// </summary>
    public const int LeastNightsRunning = 3;

    /// <summary>
    /// How many of these lines one segment may carry.
    ///
    /// Two. Each is a sentence about a person and a form guide names three
    /// drivers; unchecked this would be six sentences about six weeks,
    /// which is the thing a crowd stops listening to.
    /// </summary>
    public const int MostLines = 2;

    /// <summary>
    /// The lines worth saying about these drivers' recent weeks, best
    /// first.
    ///
    /// ORDERED BY WHAT IT IS WORTH HEARING, not by driver. A podium run is
    /// news; perfect attendance is colour; a driver back after a month is
    /// somewhere between the two. The cap then takes the top of that
    /// order, so a segment that can carry two carries the two best rather
    /// than whichever drivers happened to be first on the grid.
    ///
    /// Empty is the usual answer at a club in its first weeks and every
    /// way of being empty is correct. The form guide then reads exactly as
    /// it did before there was any of this.
    /// </summary>
    /// <param name="weeks">What the reader worked out, one entry per driver it could say anything about.</param>
    /// <param name="className">Whose nights these were counted over. Blank counts the club's nights as a whole.</param>
    /// <param name="key">Which night - see <see cref="OneDriver"/> for why these are the night's and not the race's.</param>
    /// <param name="most">How many at most. <see cref="MostLines"/>.</param>
    public static IReadOnlyList<FactBlock> ForTheWeeks(
        IReadOnlyList<DriverWeeks>? weeks,
        string? className,
        string key,
        int most = MostLines)
    {
        var found = new List<(int Worth, FactBlock Block)>();

        foreach (var driver in weeks ?? Array.Empty<DriverWeeks>())
        {
            if (OneDriver(driver, className, key) is { } line)
            {
                found.Add((WorthOf(driver), line));
            }
        }

        var cap = Math.Max(1, most);

        return found
            .OrderByDescending(one => one.Worth)
            .Take(cap)
            .Select(one => one.Block)
            .ToList();
    }

    /// <summary>
    /// How much a driver's recent weeks are worth hearing about - bigger
    /// is better. Only ever used to ORDER the lines, never to decide
    /// whether one is true.
    ///
    /// A podium run beats everything, and a longer one beats a shorter.
    /// Then a driver who has not missed a night, then one who finished
    /// everything they started, then one who is back after a while away.
    /// </summary>
    private static int WorthOf(DriverWeeks driver)
    {
        if (driver.PodiumNightsRunning >= LeastNightsRunning)
        {
            return 1000 + driver.PodiumNightsRunning;
        }

        if (driver.NightsOut >= NightsInWindow && driver.NightsOut == driver.NightsTheClubRan)
        {
            return 500 + driver.NightsOut;
        }

        if (driver.FinishedEverything && driver.RacesStarted >= LeastRacesForClean)
        {
            return 300 + driver.RacesStarted;
        }

        return driver.NightsMissed;
    }

    /// <summary>
    /// How many races a driver has to have started in the window before
    /// "finished every one of them" is worth a sentence.
    ///
    /// Six. A club runs two or three races a night, so six is about three
    /// nights of racing - and "he has finished both of his races" is the
    /// kind of true a room laughs at.
    /// </summary>
    public const int LeastRacesForClean = 6;

    /// <summary>
    /// The one thing worth saying about this driver's last few nights, or
    /// null.
    ///
    /// ONE LINE PER DRIVER, and the best of what is true about them. A
    /// driver on a podium run who has also not missed a night gets the
    /// run: two sentences about one person inside one segment is the
    /// repetition the themes exist to prevent, and the second would be the
    /// weaker claim anyway.
    ///
    /// Null four ways, each of them the honest answer: no name to say it
    /// about, too few nights out for a shape (see
    /// <see cref="LeastNightsOut"/>), a window the club has not raced
    /// enough nights of, and nothing true that clears its own bar.
    /// </summary>
    /// <param name="driver">What the reader worked out about them.</param>
    /// <param name="className">The class, which the line must name where there is one.</param>
    /// <param name="key">
    /// Which night. Every line here is true for the whole evening - a run
    /// of three nights is still three until the club races again - so a
    /// race-scoped key would have a club running six heats hear the same
    /// sentence in all six. The same argument CommentaryRaceFacts.ForIntro
    /// makes for its night key.
    /// </param>
    public static FactBlock? OneDriver(DriverWeeks driver, string? className, string key)
    {
        var who = CommentaryFacts.Clean(driver.Name);

        if (who.Length == 0 || driver.NightsOut < LeastNightsOut || driver.NightsTheClubRan < LeastNightsOut)
        {
            return null;
        }

        var text = Said(driver, who, Scope(className));

        if (text.Length == 0)
        {
            return null;
        }

        // THEMED ON THE DRIVER, which is what these are about - so the
        // room does not get this and a form line about the same person
        // back to back, and so one driver's week does not silence another's.
        return new FactBlock($"weeks:{key}:{driver.DriverId}", text, FactTheme.Driver(driver.DriverId));
    }

    /// <summary>
    /// What to call the races these nights were counted over - "Box Stock
    /// nights" where there is a class, and "race nights" where the club
    /// files nothing against one.
    ///
    /// The scope goes into the sentence rather than being left implied,
    /// for the reason stated on the class: at a club running two classes,
    /// "three nights running" said about one class's podiums is a claim a
    /// driver can disprove by naming the other class, and a driver who can
    /// disprove a line stops believing the next one.
    /// </summary>
    private static string Scope(string? className)
    {
        var cls = CommentaryFacts.Clean(className);

        return cls.Length > 0 ? cls + " nights" : "race nights";
    }

    /// <summary>
    /// The best true sentence about this driver's weeks, in the order
    /// <see cref="WorthOf"/> ranks them - or empty when nothing clears its
    /// own bar, which is the usual answer.
    /// </summary>
    private static string Said(DriverWeeks driver, string who, string nights)
    {
        // A RUN OF PODIUM NIGHTS. The strongest of these by a long way,
        // and the one a regular would actually say out loud.
        if (driver.PodiumNightsRunning >= LeastNightsRunning)
        {
            return $"{who} has finished in the top three on each of the club's last "
                   + $"{CommentaryHistoryFacts.Spell(driver.PodiumNightsRunning)} {nights}.";
        }

        // NOT MISSED ONE. Colour rather than news, and worth it: the
        // driver it is true of is nearly always somebody no superlative
        // in this app will ever be about.
        if (driver.NightsOut >= NightsInWindow && driver.NightsOut == driver.NightsTheClubRan)
        {
            return $"{who} has been out at every one of the club's last "
                   + $"{CommentaryHistoryFacts.Spell(driver.NightsOut)} {nights}.";
        }

        // FINISHED EVERYTHING THEY STARTED. The quiet virtue, and the one
        // thing a mid-pack regular is reliably better at than the fast
        // drivers - which is exactly why it is worth saying.
        if (driver.FinishedEverything && driver.RacesStarted >= LeastRacesForClean)
        {
            return $"{who} has finished every one of the {Plural.Of(driver.RacesStarted, "race")} they have "
                   + $"started over the club's last {CommentaryHistoryFacts.Spell(driver.NightsOut)} {nights}.";
        }

        // BACK AFTER A WHILE AWAY, where the caller could date it. Last,
        // because it is the only one of the four that is about absence,
        // and a room would rather hear what somebody did than what they
        // missed.
        var lastOut = CommentaryFacts.CleanLong(driver.LastOutDescribed);

        if (driver.NightsMissed >= LeastNightsRunning && lastOut.Length > 0)
        {
            return $"{who} is back after missing the club's last "
                   + $"{CommentaryHistoryFacts.Spell(driver.NightsMissed)} {nights} - "
                   + $"their last was {lastOut}.";
        }

        return string.Empty;
    }
}

/// <summary>
/// The shape of one driver's last few race nights in one class - see
/// <see cref="CommentaryWeekFacts"/> for what it is for and why it counts
/// nights rather than races.
///
/// Every figure is over the SAME window, which is the club's own last
/// <see cref="CommentaryWeekFacts.NightsInWindow"/> race nights in the
/// class - never a span of calendar weeks. A club that skipped a fortnight
/// has not had a driver miss two weeks.
/// </summary>
/// <param name="DriverId">The transponder.</param>
/// <param name="Name">What the announcer calls them - the profile's spoken name, never a transponder.</param>
/// <param name="NightsTheClubRan">How many nights the club actually raced this class in the window. Fewer than the window at a club in its first weeks.</param>
/// <param name="NightsOut">How many of those nights this driver raced on.</param>
/// <param name="NightsMissed">How many they did not. <c>NightsTheClubRan - NightsOut</c>, carried rather than derived so a builder cannot get the subtraction the wrong way round.</param>
/// <param name="PodiumNightsRunning">
/// The unbroken run of the most recent nights they were out at where their
/// best finish of the night was in the top three.
///
/// BROKEN ONLY BY A NIGHT THEY WERE THERE FOR. A driver away for a
/// fortnight has not had his run ended by being on holiday, and a room
/// that was there knows it - see the note on
/// <see cref="CommentaryWeekFacts"/>.
/// </param>
/// <param name="RacesStarted">How many races they started across the window - two or three a night at most clubs.</param>
/// <param name="FinishedEverything">They finished every one of those races: no retirement, no disqualification.</param>
/// <param name="LastOutDescribed">
/// The day they were last out, as a commentator would date it - "3
/// October". Only meaningful when they have missed nights since; empty
/// when the caller could not date it, and the line about absence is then
/// not said at all.
/// </param>
public sealed record DriverWeeks(
    string DriverId,
    string Name,
    int NightsTheClubRan,
    int NightsOut,
    int NightsMissed,
    int PodiumNightsRunning,
    int RacesStarted,
    bool FinishedEverything,
    string LastOutDescribed);
