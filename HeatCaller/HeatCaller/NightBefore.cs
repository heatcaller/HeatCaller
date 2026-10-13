namespace HeatCaller;

/// <summary>
/// The night before - a preview of a meeting that has not happened,
/// written from the entry list and meant for the club's own page.
///
/// THE ONE PIECE WHOSE JOB IS TO GET PEOPLE TO THE TRACK
///
/// Everything else the announcer writes is for people who are already in
/// the building, or for people being told afterwards what they missed.
/// This is the only thing HeatCaller makes on a day the app is otherwise
/// shut, and the only one whose success is measured in who turns up.
///
/// IT IS A PLAN, NOT A REPORT, AND THAT IS THE WHOLE DIFFICULTY
///
/// Every other piece in this layer is written from saved results - things
/// that happened, to people who were there. This one is written from
/// RoundDefinition.AssignedTransponders, which is **what the club
/// intends**, and intent is not a fact about tomorrow. Three drivers will
/// scratch, somebody will turn up who is not on the list, and the
/// programme will be reordered twice before the first heat.
///
/// The app already draws this exact distinction elsewhere and it is worth
/// quoting, because it is the reason this file is written the way it is.
/// RaceControlControl.Broadcast's room-vote grid:
///
///   THE GRID COMES FROM THE SESSION, NOT THE PROGRAM. What the room is
///   voting on is the cars actually staged: a program's assigned list is
///   what the club intended, and a driver who did not turn up must not be
///   on the ballot.
///
/// A preview has only the intent and has no fallback, because nobody has
/// crossed the loop yet. So the honest answer is not to check harder - it
/// cannot be checked - but to SAY WHAT IT IS. Every fact this file builds
/// is worded as a plan: "entered", never "racing"; "due out at", never
/// "goes out at"; "nineteen cars on the entry list", never "nineteen cars
/// will be out". The Instruction below tells the model the same thing in
/// the same words, which is the pattern the mid-meeting standings already
/// use for a provisional order.
///
/// WHAT IT DELIBERATELY WILL NOT SAY, AND WHY EACH ONE IS LEFT OUT
///
/// 1. **Who is in any main.** A main built from the heats has no roster
///    until the heats are in - CommentaryReaders says so where it hands a
///    main over with a count of none. A preview naming an A-main grid
///    would be inventing one.
///
/// 2. **How strong the field is.** CommentaryReaders.Field is honest
///    inside a hall and wrong here, and the reason is subtle enough to be
///    worth writing down: it compares the ENTRY count against the STARTER
///    counts of past races. In the building those are minutes apart and
///    the cars are on the rostrum. The night before, with three scratches
///    to come, "the biggest Box Stock entry since March" is a claim the
///    meeting itself disproves. The reader's own standard says it: ghosts
///    are left out because "the biggest entry this class has had" is a
///    claim about cars with people behind them, and an entry that does
///    not show up is the same overstatement.
///
/// 3. **Any prediction at all.** Not who will win, not what a result
///    would do to a table. That ban is older than this file and is stated
///    at length on CommentaryHistoryFacts: what a result does to a table
///    depends on where everyone else finishes, and a drop rule means
///    there is no arithmetic shortcut that survives.
///
/// 4. **A time for anything but the first race.** A club's own timetable
///    allows freeform entries - "~10:15", "TBD" - and the running order
///    moves all day. The first race is the one time a club commits to.
///
/// WHAT IT DOES SAY, AND WHY EACH ONE SURVIVES BEING A DAY EARLY
///
/// The shape of the programme (a plan, worded as one), how many are
/// entered per class (a plan), the venue's outright lap record (a fact
/// about the past, which cannot go stale), and what the championship has
/// riding on it - the one genuinely strong preview fact, because
/// TitleOnTheLine's proof is already about rounds REMAINING and already
/// plays a rival's best case against the leader's worst. A rival who
/// stays home scores nothing, which only strengthens "cannot be caught".
///
/// The one care needed there is the NAMES: the stakes name the drivers
/// still in it, and "only Bill and Sam can win it tomorrow" about a Sam
/// who is not entered is the fault NightStory already guards against -
/// a claim about a driver who is not here is not false, it is ridiculous,
/// and a room notices that as fast as it notices a wrong fact. So the
/// stakes are read against the entry list, which is what TitleOnTheLine
/// takes anyway.
///
/// AND IT HAS TO COME DOWN. A preview left on a club's page describes a
/// meeting that has since happened, in the future tense, for as long as
/// nothing replaces it. See Withdrawn.
/// </summary>
internal static class NightBefore
{
    /// <summary>What the piece is called, wherever it is filed or shown.</summary>
    internal const string Label = "The Night Before";

    /// <summary>
    /// How long the piece is.
    ///
    /// Three hundred, between the night's recap at four hundred and a
    /// driver's own piece at a hundred and fifty. A preview has less to go
    /// on than either - no results at all - and the temptation to pad is
    /// correspondingly larger, which is the argument for the shorter end
    /// rather than the longer.
    /// </summary>
    internal const int Words = 300;

    /// <summary>
    /// What to ask the prompt builder for on top of the club's own
    /// per-line limit, so the preview lands on <see cref="Words"/>
    /// whatever that limit is set to. The same arithmetic as
    /// NightRecap.ExtraWordsOver and for its reasons - read those.
    /// </summary>
    internal static int ExtraWordsOver(int clubLimit) =>
        Math.Max(0, Words - Math.Max(0, clubLimit));

    /// <summary>
    /// How much room the facts get.
    ///
    /// Four thousand. Smaller than the night's recap, which has a whole
    /// evening of results to carry, and larger than a driver's own piece,
    /// which has one person's.
    /// </summary>
    internal const int MostChars = 4000;

    /// <summary>Split into pieces the announcer can read, if a club ever wants it spoken. The recap's splitter - see NightRecap.Chunks.</summary>
    internal static IReadOnlyList<string> Chunks(string? script) => NightRecap.Chunks(script);

    /// <summary>
    /// The fewest entered cars a class is worth mentioning with.
    ///
    /// Four, matching CommentaryRaceFacts' own bar for an entry being
    /// worth a sentence. A class with two entries named in a preview
    /// reads as a club advertising that nobody is coming.
    /// </summary>
    internal const int LeastEntered = 4;

    /// <summary>
    /// How the model is told to write it.
    ///
    /// THE PLAN CLAUSE IS THE ONE THAT IS NOT IN ANY OTHER INSTRUCTION IN
    /// THIS APP, and it is the whole reason this one is written out rather
    /// than borrowed. Every other piece describes something that has
    /// happened; this describes something intended. Telling the model the
    /// status and making it SAY the status is the pattern the mid-meeting
    /// standings already use - "the order is provisional and nobody has
    /// qualified for anything yet, say so" - and this extends it.
    ///
    /// It is also the one piece that will be read by people who were not
    /// invited by anybody, on a public page, days later. So the ban on
    /// predicting is restated here even though the facts carry no
    /// prediction to work from: a model with an entry list and no results
    /// is at its most inventive.
    /// </summary>
    internal const string Instruction =
        "Write this as a short preview of a race meeting that HAS NOT HAPPENED YET, for the club's own web page - "
        + "it is read by members deciding whether to come and by people who have never been. "
        + "EVERYTHING HERE IS A PLAN, NOT A RESULT, AND YOU MUST WORD IT AS ONE. Say drivers are entered rather "
        + "than racing, say a race is due out rather than that it goes out, and say how many cars are on the "
        + "entry list rather than how many will be out. Never write about any of it in the past tense. "
        + "Do not predict who will win, who will qualify, or what any result would do to a championship. Do not "
        + "name anybody as being in a final or a main - those grids do not exist yet. "
        + "Open with when it is and what is on, and finish on a reason to come. Use a few short paragraphs. "
        + "Everything you say must come from the facts above - every name, time, count and record. Do not add a "
        + "driver, a class, a time, a lap record or a championship position that is not there, and where the "
        + "facts are thin, write a shorter preview rather than filling it out.";

    /// <summary>
    /// What goes on the club's page once the meeting has started, in place
    /// of the preview.
    ///
    /// A PREVIEW HAS TO COME DOWN, and nothing else in this app has had to
    /// think about that. Every other published document is replaced by a
    /// newer version of itself - a result by a corrected result, a roster
    /// by a fresh roster - so "take it down" has never been a case. A
    /// preview is the first thing HeatCaller publishes that becomes WRONG
    /// by the passage of time rather than by being superseded: left alone
    /// it tells the public, in the future tense, about a meeting that
    /// finished hours ago.
    ///
    /// Empty rather than dropped, which is the rule RelayPublisher already
    /// learned for a withdrawn roster: dropping a channel leaves the last
    /// thing sent standing on the relay for good, because there is nothing
    /// newer to replace it with. So the preview is replaced by nothing,
    /// and the page shows nothing where it was.
    /// </summary>
    internal const string Withdrawn = "";

    /// <summary>
    /// The document that takes a preview down: the same shape with no
    /// words in it.
    ///
    /// Published rather than the channel being dropped, for the reason
    /// above. The page treats empty words as "draw nothing" and no expiry
    /// is needed on a document that says nothing - see previewHtml in
    /// relay/src/pages.ts, which checks the words before the date.
    /// </summary>
    internal const string WithdrawnDocument = "{\"text\":\"\"}";

    /// <summary>
    /// How long a published preview stands before the PAGE stops drawing
    /// it, measured from the first race.
    ///
    /// THIS IS A BACKSTOP, NOT THE MECHANISM. The laptop withdraws a
    /// preview when the meeting starts, and on an ordinary evening that is
    /// what takes it down. This covers the evening that is not ordinary:
    /// the club packed up, switched the PC off and went home, so nothing
    /// ever withdrew it - which is the one case the laptop cannot fix and
    /// exactly when a preview is at its most stale.
    ///
    /// Six hours from the first race rather than a fixed hour of the day,
    /// because a club night starting at seven and a Sunday meeting
    /// starting at ten are both covered by "the meeting will be over by
    /// now". It does not have to be tight: by the time it matters the
    /// laptop has almost always already taken it down, and the only cost
    /// of being generous is a preview standing a few hours into a meeting
    /// whose laptop never came up.
    /// </summary>
    internal static readonly TimeSpan StandsFor = TimeSpan.FromHours(6);

    /// <summary>
    /// When the page should stop drawing this preview, in UTC.
    ///
    /// A DAY WITH NO FIRST RACE TIME STANDS ALL DAY. Measuring six hours
    /// from an unset time means measuring from midnight, and a preview for
    /// a ten o'clock start would have come down at six in the morning on
    /// the day of the meeting - the one morning it is most wanted. So a
    /// day that has not said when it starts keeps its preview until the
    /// day itself is out.
    /// </summary>
    internal static DateTime UntilUtc(DateTime dayLocal, TimeSpan? firstRaceLocal)
    {
        var until = firstRaceLocal is { } first
            ? dayLocal.Date + first + StandsFor
            : dayLocal.Date.AddDays(1);

        // SpecifyKind rather than a cast: these dates are built from a race
        // day's own local date and time, whatever Kind the DateTime came
        // through with. ToUniversalTime already reads Unspecified as local,
        // but a date whose Kind came through as Utc it leaves exactly as it
        // is - which on this side of the Atlantic is the preview coming down
        // four or five hours early.
        return DateTime.SpecifyKind(until, DateTimeKind.Local).ToUniversalTime();
    }

    /// <summary>
    /// The two dates a preview is offered for when it is written, the
    /// first being the one a club most likely means.
    ///
    /// ASKED, BECAUSE NOTHING ELSE KNOWS. A race day has no date of its own
    /// - RaceEvent.StartedUtc is the moment it was opened, which for a
    /// preview written the night before is the night before - so the
    /// preview used to state that, and expire from it, which had it come
    /// down before it was ever drawn.
    ///
    /// Tomorrow first, because that is what the button is called. Today
    /// second while the first race is still to come (or no time is set);
    /// once it has gone a preview for today is no use, and the day after
    /// tomorrow is offered instead. A meeting further off than that is
    /// written nearer the time rather than given a date this guessed.
    /// </summary>
    internal static IReadOnlyList<DateTime> DaysItCouldBeFor(DateTime nowLocal, TimeSpan? firstRaceLocal)
    {
        var today = nowLocal.Date;

        var todayStillToCome = firstRaceLocal is not { } first || today + first > nowLocal;

        return new[] { today.AddDays(1), todayStillToCome ? today : today.AddDays(2) };
    }

    /// <summary>A date as the button offering it reads - "Tomorrow, Saturday 17 October".</summary>
    internal static string DayCaption(DateTime day, DateTime todayLocal)
    {
        var date = day.ToString("dddd d MMMM", System.Globalization.CultureInfo.CurrentCulture);

        if (day.Date == todayLocal.Date)
        {
            return "Today, " + date;
        }

        return day.Date == todayLocal.Date.AddDays(1) ? "Tomorrow, " + date : date;
    }

    /// <summary>
    /// The one line under the heading: when it is, as a club would say it.
    ///
    /// Empty where there is no date worth stating, so the page leaves the
    /// line out rather than drawing "Invalid date".
    /// </summary>
    internal static string WhenLine(DateTime dayLocal, TimeSpan? firstRaceLocal)
    {
        var day = dayLocal.ToString("dddd d MMMM", System.Globalization.CultureInfo.CurrentCulture);

        return firstRaceLocal is { } first
            ? day + ", from " + (DateTime.Today + first).ToString("t", System.Globalization.CultureInfo.CurrentCulture)
            : day;
    }

    /// <summary>
    /// The preview as the club's page receives it.
    ///
    /// THE RELAY NEVER READS THIS. Every name here is an agreement with
    /// previewHtml in relay/src/pages.ts and nothing in between
    /// understands it, which is the rule relay/src/types.ts opens with -
    /// so these four names and that function have to be changed together,
    /// and the relay's own tests assert the shape rather than its meaning.
    ///
    /// Returns <see cref="WithdrawnDocument"/> for a preview with no words
    /// in it, so a model that came back empty takes the old one down
    /// rather than publishing a heading over nothing.
    /// </summary>
    internal static string Document(
        string? dayName,
        DateTime dayLocal,
        TimeSpan? firstRaceLocal,
        string? script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return WithdrawnDocument;
        }

        return System.Text.Json.JsonSerializer.Serialize(new
        {
            kicker = "The night before",
            title = string.IsNullOrWhiteSpace(dayName) ? "Next race day" : dayName.Trim(),
            when = WhenLine(dayLocal, firstRaceLocal),

            // Trimmed to the same ceiling the model was asked for. A model
            // that ignored the word count must not be able to put eight
            // kilobytes on a club's front page.
            text = Trimmed(script),
            until = UntilUtc(dayLocal, firstRaceLocal).ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        });
    }

    /// <summary>
    /// The script, no longer than <see cref="MostChars"/>, cut at a
    /// paragraph rather than mid-word.
    ///
    /// Cutting at a paragraph matters more here than it would in the
    /// console: this is the version strangers read, and a preview that
    /// stops mid-sentence reads as a club whose website is broken.
    /// </summary>
    private static string Trimmed(string script)
    {
        var text = script.Trim();

        if (text.Length <= MostChars)
        {
            return text;
        }

        var cut = text[..MostChars];
        var para = cut.LastIndexOf("\n\n", StringComparison.Ordinal);

        return (para > MostChars / 2 ? cut[..para] : cut).TrimEnd();
    }

    /// <summary>
    /// The facts a meeting that has not happened comes to, or an empty
    /// pack when there is nothing worth a preview.
    ///
    /// SAID: NULL, like the night's recap and a driver's own piece. There
    /// is nothing to filter against - no segment has said anything about a
    /// meeting that has not started - and a preview built from what the
    /// announcer had not got round to mentioning would be a strange
    /// document.
    ///
    /// EVERY BLOCK IS WORDED AS A PLAN. That is done here, in the facts,
    /// and not left to the Instruction: a fact block is data the model
    /// describes, and a model handed "nineteen cars" will write "nineteen
    /// cars will be out" however the instruction is phrased. Handed
    /// "nineteen cars are on the entry list", it writes that.
    /// </summary>
    /// <param name="dayName">What the club calls the meeting. Empty is fine - the preview then speaks of "the next race day".</param>
    /// <param name="whenLocal">When it starts, in local time. Default means no date is claimed.</param>
    /// <param name="firstRace">The first race's time as the club wrote it, which may be freeform or blank. See the class summary.</param>
    /// <param name="toRun">The programme still to run, in order - RoundDefinitionStore.Unrecorded scoped to the day.</param>
    /// <param name="venue">The venue's outright lap record, or null. A fact about the past, so it cannot go stale.</param>
    /// <param name="stakes">What the championship has riding on it, read against the entry list, or null. It carries its own names, which is why this method takes no name lookup.</param>
    internal static PackedFacts FactsFor(
        string? dayName,
        DateTime whenLocal,
        string? firstRace,
        IReadOnlyList<RoundDefinition>? toRun,
        VenueRecord? venue,
        TitleStakes? stakes)
    {
        var blocks = new List<FactBlock>();
        var called = CommentaryFacts.Clean(dayName);
        var key = whenLocal == default
            ? "nightbefore"
            : "nightbefore:" + whenLocal.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        // ---------------------------------------------------------------
        //  When it is, and what is on
        // ---------------------------------------------------------------

        var races = toRun?.Where(one => one is not null).ToList() ?? new List<RoundDefinition>();

        // NOTHING DOWN TO RUN IS NOTHING TO PREVIEW, and this guard is the
        // whole of that judgement rather than a defensive check.
        //
        // Without it, a race day whose programme is empty - set up but not
        // filled in, or every race already run - produced one block
        // saying when it was and no reason at all to come. A club that
        // posts "there is a race day on Saturday and we cannot tell you
        // anything about it" has advertised nothing, which is the fault
        // this file's own summary warns about and then walked into: the
        // day line was built before anything had been checked.
        //
        // The caller guards this too and says which it was in words a club
        // can act on. Both, because a guard in one caller is not a
        // promise - the next caller would not know to make it.
        if (races.Count == 0)
        {
            return FactPack.Build(Array.Empty<FactBlock>(), said: null, MostChars);
        }

        var classes = races
            .Select(one => CommentaryFacts.Clean(one.ClassName))
            .Where(one => one.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var opening = new System.Text.StringBuilder();

        opening.Append(called.Length > 0 ? called : "The club's next race day");

        if (whenLocal != default)
        {
            opening.Append($" is planned for {whenLocal.ToString("dddd d MMMM", System.Globalization.CultureInfo.CurrentCulture)}");
        }
        else
        {
            opening.Append(" is planned");
        }

        // THE ONLY TIME A CLUB COMMITS TO, and said exactly as they typed
        // it - a club's own timetable allows "~10:15" and "TBD", and
        // tidying either into something definite would be this file
        // inventing a certainty the club did not express.
        var first = CommentaryFacts.Clean(firstRace);

        if (first.Length > 0)
        {
            opening.Append($", first race due out at {first}");
        }

        opening.Append('.');

        if (races.Count > 0)
        {
            opening.Append($" The programme as it stands has {Plural.Of(races.Count, "race")} down to run");

            if (classes.Count > 0)
            {
                opening.Append($", across {Plural.Of(classes.Count, "class", "classes")}: {string.Join(", ", classes)}");
            }

            // SAID EVERY TIME THE PROGRAMME IS MENTIONED. A club reorders
            // its programme all evening and adds races on the day, so a
            // count stated as settled is a count the meeting disproves.
            opening.Append(". That is the plan rather than a promise - a programme changes on the day.");
        }

        blocks.Add(new FactBlock(key + ":day", opening.ToString().Trim()));

        // ---------------------------------------------------------------
        //  Who is entered, per class
        // ---------------------------------------------------------------

        foreach (var className in classes)
        {
            // THE DISTINCT CARS ENTERED ACROSS THE CLASS'S RACES, not the
            // sum of its rosters: a club running four heats of the same ten
            // drivers has ten entries, and adding the rosters up would
            // advertise forty.
            var entered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var round in races)
            {
                if (!string.Equals(CommentaryFacts.Clean(round.ClassName), className, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var one in round.AssignedTransponders)
                {
                    // Ghosts are not entries, for the reason
                    // CommentaryReaders.Field gives: a count of cars is a
                    // claim about cars with people behind them.
                    //
                    // Through Ghosts rather than GhostService, which is
                    // what that class exists for: GhostService is the
                    // ghost SIMULATOR and wants four stores before it will
                    // construct, and this file wants the one string test.
                    if (one is { Length: > 0 } transponder
                        && !string.IsNullOrWhiteSpace(transponder)
                        && !Ghosts.IsGhost(transponder))
                    {
                        entered.Add(transponder.Trim());
                    }
                }
            }

            if (entered.Count < LeastEntered)
            {
                continue;
            }

            blocks.Add(new FactBlock(
                key + ":entered:" + className.ToLowerInvariant(),
                $"{Plural.Of(entered.Count, "car")} on the entry list for {className} so far. "
                + "An entry list is not a result sheet - some will not make it and others will enter on the day.",
                FactTheme.Field(className)));
        }

        // ---------------------------------------------------------------
        //  The venue's record - the one fact here that cannot go stale
        // ---------------------------------------------------------------

        if (venue is { } record && CommentaryFacts.Clean(record.Holder) is { Length: > 0 } holder)
        {
            blocks.Add(new FactBlock(
                key + ":venue",
                $"The outright lap record on {CommentaryFacts.Clean(record.LayoutName)} is "
                + $"{record.LapSeconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)}s, "
                + $"set by {holder}, {CommentaryFacts.Clean(record.SetDescribed)}.",
                FactTheme.Venue(record.LayoutName)));
        }

        // ---------------------------------------------------------------
        //  What the championship has riding on it
        // ---------------------------------------------------------------

        if (stakes is { } riding && CommentaryStakesFacts.ForTheTitle(riding, key + ":title") is { } title)
        {
            blocks.Add(title);
        }

        return FactPack.Build(blocks, said: null, MostChars);
    }

    /// <summary>
    /// The names on the entry list, for the stakes to be read against.
    ///
    /// EVERY ROUND'S ROSTER TOGETHER, because a championship contender
    /// entered only in the second heat is still entered. Ghosts out, each
    /// transponder once, in the order the programme first names them so
    /// the answer does not move between two calls.
    /// </summary>
    internal static IReadOnlyList<string> EnteredIn(IReadOnlyList<RoundDefinition>? toRun)
    {
        var seen = new List<string>();
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var round in toRun ?? Array.Empty<RoundDefinition>())
        {
            if (round is null)
            {
                continue;
            }

            foreach (var one in round.AssignedTransponders)
            {
                if (one is { Length: > 0 } transponder
                    && !string.IsNullOrWhiteSpace(transponder)
                    && !Ghosts.IsGhost(transponder)
                    && known.Add(transponder.Trim()))
                {
                    seen.Add(transponder.Trim());
                }
            }
        }

        return seen;
    }
}
