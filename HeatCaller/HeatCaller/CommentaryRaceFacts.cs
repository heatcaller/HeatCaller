using System.Text;

namespace HeatCaller;

/// <summary>
/// The quickest lap ever set on a layout, as the club's own track plan
/// keeps it.
///
/// WHY THE TRACK PLAN AND NOT A WALK OF THE LAP TIMES
///
/// TrackLayout.RecordLapMs is the club's record, maintained by the one
/// place allowed to set it (RaceControlControl.CheckAndAnnounceTrackRecord),
/// which already refuses a demo lap, a practice lap, a ghost, a rental and
/// a driver flagged out of records - and refuses a lap too quick to be
/// believed, saying so in the log. A second walk of the saved lap times
/// here would quietly disagree with the Track Layouts page, the Big Screen
/// and the relay's own record line, and the figure a club has seen on its
/// own screen is the figure it will hold the announcer to.
///
/// Worked out by the caller and handed over flat, like every other record
/// in this layer - see CommentaryHistoryFacts' own remarks on why.
/// </summary>
/// <param name="LayoutName">The layout, as the club named it. The line says it, because a record is only a record somewhere.</param>
/// <param name="LapSeconds">The record lap. Seconds, so the builder is a table test and never touches a clock.</param>
/// <param name="Holder">Who set it, as it was saved on the night.</param>
/// <param name="SetDescribed">"March", "March 2024" - the caller's words, because only something holding a calendar can tell one from the other.</param>
/// <param name="RacesOnLayout">Every race the club has filed on this layout. A layout with one race on it has no record worth the name.</param>
/// <param name="RacesSince">Races the club has filed on this layout since the record was set - what "nobody has beaten it since" counts.</param>
/// <param name="SetTonight">
/// The record was set on the night in hand, so it is news rather than
/// history.
///
/// Carried rather than left to the reader, so the builder refuses it too:
/// a record set an hour ago read out as "the outright record, set in
/// October - nobody has beaten it since" is the timing system describing
/// the last heat as though it were a piece of club history, in front of
/// the driver who set it.
/// </param>
public sealed record VenueRecord(
    string LayoutName,
    double LapSeconds,
    string Holder,
    string SetDescribed,
    int RacesOnLayout,
    int RacesSince,
    bool SetTonight);

/// <summary>
/// How strong the field in a race is, against the club's own season.
///
/// COUNTED OVER THE WHOLE ROUND, NOT OVER THE DRIVERS THE PROMPT NAMES.
/// A race intro names the first eight on the grid and no more, but "the
/// biggest field this class has had" is a fact about the field - a
/// ten-car race in which eight are named is still a ten-car race, and
/// saying eight would be the one thing these lines must never be, which
/// is wrong. The counts below are therefore over every entry; the names
/// stay the prompt's business.
/// </summary>
/// <param name="ClassName">The class, so every line can say which races it counted.</param>
/// <param name="Entries">How many cars are out in this race.</param>
/// <param name="RacesCounted">How many of this class's races the club has filed. A club in its first week has nothing to compare with.</param>
/// <param name="BiggestSinceDescribed">
/// When this class last had a field at least this big - the caller's
/// words. Null when it has never had one, which is a different and
/// bigger claim and is worded as one.
/// </param>
/// <param name="RacesSinceThatBig">How many of this class's races have run since that one. "The biggest since last week" is not a fact worth a sentence.</param>
/// <param name="MainWinners">How many of this race's entries have won a main in this class at this club this calendar year.</param>
public sealed record FieldStrength(
    string ClassName,
    int Entries,
    int RacesCounted,
    string? BiggestSinceDescribed,
    int RacesSinceThatBig,
    int MainWinners);

/// <summary>
/// How the night has come to one driver: their middle flying lap in their
/// latest race against their first.
///
/// THE SAME MEASUREMENT THE TRACK'S OWN FIGURE IS MADE OF, and that is
/// the whole point of it being this shape. TonightAtTheTrack already
/// works a driver's change out - it is what the class-wide evolution
/// figure is the middle of - and a second implementation here would walk
/// the same laps to a slightly different answer, which is the fault to
/// avoid: a corrected result would move one and not the other, and
/// nothing would ever say so. So the reader asks
/// TonightAtTheTrack.PaceTonight and this only carries the answer.
/// </summary>
/// <param name="DriverId">The transponder.</param>
/// <param name="Name">As it should be said.</param>
/// <param name="FirstRace">Their first race tonight, as the club labelled it - the race the change is counted FROM, so the line can say it.</param>
/// <param name="DeltaMs">Their middle lap now against then. Negative when they are quicker now.</param>
/// <param name="Races">How many of tonight's races they had enough laps in to be measured. Two is the fewest that means anything.</param>
public sealed record DriverTonight(
    string DriverId,
    string Name,
    string FirstRace,
    long DeltaMs,
    int Races);

/// <summary>
/// What the club's own garage and results know about the car a finisher
/// raced.
///
/// WHY NOTHING IN HERE NAMES A CAR
///
/// The recap's result lines already carry "(Car)" after a driver's name,
/// from the result as it was saved or the driver's profile, and the prompt
/// tells the model to "use the car names exactly as written". The Garage
/// knows a car by a different name again - its own Name, or its bodyshell
/// (Car.DisplayName) - and handing a 3B model two names for one car is
/// how the recap invents a second driver, which is exactly the fault
/// CommentaryService.AsSpoken exists to prevent in the same prompt. So
/// these lines say "that car" and "their car" and let the result lines
/// keep the naming: they build on the car the recap already has rather
/// than putting a second one beside it.
/// </summary>
/// <param name="DriverId">The transponder.</param>
/// <param name="Name">As it should be said.</param>
/// <param name="ClubOwned">The club owns the car, rather than a driver - see Car.IsClubCar.</param>
/// <param name="OnLoanForThisDay">It is out to this driver for this race day, rather than theirs - see Car.DriverFor.</param>
/// <param name="InGarageSince">"March 2025" - the caller's words. Null when the garage has no car for them, or has had it too short a while to call it theirs.</param>
/// <param name="FirstRaceInClass">The class of the race just run, when it is their first in it at this club. Null otherwise.</param>
/// <param name="OnlyClassBefore">
/// The one class every earlier start of theirs here was in.
///
/// Null the moment there were two, and the fact is dropped - "every one
/// of their starts here was in Box Stock" is false for a driver who has
/// also run Open, and a driver who can disprove a line is a driver who
/// stops believing the next one.
/// </param>
/// <param name="StartsBefore">Their starts at this club before this race. A second night out is not a switch of class.</param>
public sealed record RaceCar(
    string DriverId,
    string Name,
    bool ClubOwned = false,
    bool OnLoanForThisDay = false,
    string? InGarageSince = null,
    string? FirstRaceInClass = null,
    string? OnlyClassBefore = null,
    int StartsBefore = 0);

/// <summary>
/// A race's SETTING as the club's own stores know it, for a race intro:
/// the venue's outright record, how strong the field is, what the night
/// has done to the drivers' pace so far, the storyline the evening is
/// being told as, and the one thing the club's results prove about what
/// this race could decide.
///
/// THE SIX TRAVEL TOGETHER OR NOT AT ALL, for the reason RaceForm's own
/// summary gives about the drivers' history. It is the caller that read
/// the stores, and the caller is the only thing that knows which race day
/// is open - the builders hold no state and the prompt builders have no
/// event store. Carried here rather than as three more arguments on the
/// intro, because the race day is a fact ABOUT this reading of the stores
/// and belongs with it.
/// </summary>
/// <param name="Venue">The layout's outright lap record, or null where the club has none worth the name.</param>
/// <param name="Field">The field against the club's own season, or null where there is too little on file to compare.</param>
/// <param name="Tonight">What the night has done to the drivers' pace, biggest change first.</param>
/// <param name="Thread">
/// THE NIGHT'S THREAD, where the club's results support one - see
/// NightThread.
///
/// NOT THIS RACE'S SETTING, AND IT TRAVELS HERE ANYWAY. The venue, the
/// field and the drivers' nights are all facts about the race in front of
/// the room; the thread is a framing of the EVENING, and a tidier app
/// would hand it over separately. It rides here because the alternative is
/// worse in the two ways that matter: it is read on the same UI-thread
/// pass by the same caller, from the same stores, for the same prompt - and
/// a ninth argument on CommentaryService.BuildRoundIntroAsync is a seam
/// the next reader has to learn. What it does NOT share is the budget:
/// the thread gets a section and a cap of its own rather than a share of
/// the three places above, so it can never squeeze a record out of the
/// prompt. See CommentaryService.MaxThreadChars.
/// </param>
/// <param name="NightKey">
/// The race day these were read on, when the caller knows it - which
/// makes the venue's record and the field once-a-NIGHT rather than
/// once-a-race. Null is the old behaviour exactly, and is what keeps a
/// table test of the builders honest about one race at a time. See
/// <see cref="CommentaryRaceFacts.ForIntro"/>.
/// </param>
/// <param name="Conditional">
/// THE CHAMPIONSHIP CONDITIONAL - one NAMED finishing order and what it
/// would do to the table, where the club's own results prove it for every
/// arrangement of the rest of the field. See
/// <see cref="RaceConditional"/>, and
/// CommentaryReaders.ChampionshipConditional for the proof.
///
/// IT RIDES HERE FOR THE THREAD'S REASON AND WITH THE THREAD'S EXCEPTION.
/// The venue, the field and the drivers' nights are facts about the race in
/// front of the room; this is a claim about a race that has not been run,
/// which is a different kind of thing again. It travels here because it is
/// read on the same UI-thread pass by the same caller, from the same
/// stores, for the same prompt - and a tenth argument on
/// CommentaryService.BuildRoundIntroAsync is a seam the next reader has to
/// learn. What it does NOT share is the budget: it gets a section and a cap
/// of its own, so it can never squeeze a record out of the prompt and a
/// record can never squeeze it. See CommentaryService.MaxConditionalChars.
///
/// APPENDED LAST ON PURPOSE, as the thread was before it. Every existing
/// "new RaceScene(...)" is positional, and a field inserted anywhere else
/// would silently hand one record's value to another's parameter at every
/// one of those call sites.
///
/// Null is the usual answer and every way of being null is correct - a
/// practice night, a club with no season, a championship three rounds short
/// of meaning anything, and above all a claim that could not be proved
/// universal. The intro then reads exactly as it did before there was any
/// of this.
/// </param>
public sealed record RaceScene(
    VenueRecord? Venue = null,
    FieldStrength? Field = null,
    IReadOnlyList<DriverTonight>? Tonight = null,
    string? NightKey = null,
    NightThread? Thread = null,
    RaceConditional? Conditional = null);

/// <summary>
/// The finishers' cars in a race just run, for its recap, with the race
/// day they were read on - see <see cref="RaceScene"/> for why the night
/// travels with them rather than arriving separately.
/// </summary>
/// <param name="Cars">The cars, as CommentaryReaders.Cars worked them out - in finishing order.</param>
/// <param name="NightKey">The race day, so a car line is said once an evening rather than once a race.</param>
public sealed record RaceCars(
    IReadOnlyList<RaceCar> Cars,
    string? NightKey = null);

/// <summary>
/// The records that are about a RACE rather than about a career: the
/// venue's own lap record, how strong the field is, how the night has come
/// to a driver, and the car they raced.
///
/// WHY THESE FOUR SIT TOGETHER
///
/// Each one answers the complaint the depth plan was written for in the
/// same way - by saying something true that nobody in the room knew - and
/// each one is a RECORD, which is the only reason any of it lands. The
/// consequence is the discipline this whole file is built round, stated
/// once here because every builder below keeps it:
///
///   THE SCOPE IS IN THE LINE. "Nobody has gone quicker" means nothing
///   without "on this layout"; "the biggest field" means nothing without
///   "in this class"; "quicker now" means nothing without "than in their
///   first race tonight". A true figure read out without the scope it was
///   counted over is a false claim, and it is the kind a driver can
///   disprove in one sentence. Every line below says its own scope, for
///   the same reason CommentaryHistoryFacts.Of puts the class into every
///   history line.
///
///   DEMO AND PRACTICE SESSIONS COUNT FOR NOTHING. A demo race's laps and
///   finishing order are invented and a practice session has no result
///   anybody would call a finish, so the readers behind these leave both
///   out - the rule CommentaryReaders.RacesIn states and these honour.
///
///   A THRESHOLD ON EVERYTHING. A club in its first week, and a layout
///   drawn this afternoon, have no records, and every builder here comes
///   back null for them rather than dressing one race up as a season. The
///   tests are mostly about those nulls, exactly as the history layer's
///   are.
///
/// Worked out by the readers and handed over flat, so every builder here
/// is a table test that cannot reach a store.
/// </summary>
public static class CommentaryRaceFacts
{
    /// <summary>
    /// How many races the club has to have run on a layout before its
    /// record is a record.
    ///
    /// Three. A layout drawn this afternoon and raced on once holds a
    /// "record" that is simply the only lap anybody has driven on it, and
    /// "the outright lap record on this layout" said about it is a
    /// superlative with nothing behind it - which teaches a crowd that the
    /// superlatives mean nothing, the fault CommentaryHistoryFacts' whole
    /// set of thresholds exists to avoid.
    /// </summary>
    public const int LeastRacesOnLayout = 3;

    /// <summary>
    /// How many races have to have run since a record - or since a field
    /// this big - before "and nobody has since" is worth saying.
    ///
    /// Three, for the same reason. "The biggest field since last week" and
    /// "unbeaten in the one race since" are both true and both say
    /// nothing, and a line that says nothing is what the club owner
    /// actually complained about.
    /// </summary>
    public const int LeastRacesSince = 3;

    /// <summary>
    /// How many cars make a FIELD worth a sentence. Four. Three cars out
    /// is a race; it is not an entry anybody is going to remark on, and
    /// "the biggest field the club has had" about three of them is the
    /// superlative with nothing behind it again.
    /// </summary>
    public const int LeastEntries = 4;

    /// <summary>
    /// How many of a class's races the club needs on file before the size
    /// of tonight's entry means anything.
    ///
    /// Four. This is the bar that makes a club in its first week get
    /// nothing at all: with two races on file every field is "the biggest
    /// since the opener", which is a sentence about the club having only
    /// just started rather than about the race.
    /// </summary>
    public const int LeastRacesForField = 4;

    /// <summary>
    /// How many of a grid have to have won a main before saying so is a
    /// statement about the field.
    ///
    /// Two. One previous winner on a grid is a fact about one driver -
    /// which the history layer says far better, by name - and reading it
    /// out as though it described the field is dressing an ordinary number
    /// up as drama.
    /// </summary>
    public const int LeastMainWinners = 2;

    /// <summary>
    /// How much a driver's middle lap has to have moved tonight before it
    /// is worth a sentence, in seconds.
    ///
    /// Fifteen hundredths. Below that it is traffic and tyre pickup rather
    /// than a driver finding the track, and "a twentieth quicker than in
    /// his first heat" is the broadcast reading its own measurement error
    /// out over the PA.
    /// </summary>
    public const double LeastChangeSeconds = 0.15;

    /// <summary>
    /// How many starts a driver needs at this club before moving class is
    /// a move rather than a career just beginning.
    ///
    /// Four. "His first Open race here - both his starts before tonight
    /// were Box Stock" is a sentence about somebody's second week, and the
    /// bar is the same judgement CommentaryHistoryFacts.CareerStarts makes
    /// about a career worth reading out at all.
    /// </summary>
    public const int LeastStartsBeforeSwitch = 4;

    /// <summary>
    /// How many of these an INTRO carries.
    ///
    /// Three, where the drivers' own history takes four
    /// (CommentaryHistoryFacts.ForIntro's most) and the recap's takes
    /// three. The intro already carries a round label, a class, up to
    /// eight names, the track, the rivalries, the driver details, the
    /// club's history and the championship, all for a local llama3.2:3b
    /// to hold at once - and a fact squeezed out of the ANSWER cost the
    /// prompt its room for nothing.
    ///
    /// The two once-a-night facts in here - the venue's record and the
    /// field - are offered first and are spent for the night by the first
    /// intro that carries them (see <see cref="ForIntro"/>), so from the
    /// second heat onwards these three places belong to the drivers' own
    /// night. That is the cap paying for itself rather than biting.
    /// </summary>
    public const int SceneFacts = 3;

    /// <summary>
    /// How many drivers' nights the reader offers an intro. Two, which is
    /// one more than the three places above will usually have room for -
    /// the spare is there so a driver whose line was already said tonight
    /// does not leave the section empty.
    /// </summary>
    public const int TonightFacts = 2;

    /// <summary>
    /// How many car lines a RECAP carries.
    ///
    /// Two, and that is the whole of what the recap grows by. It already
    /// carries a finishing order with cars in it, a closeness, the
    /// winner's sponsors, a fastest lap, the rivalry outcomes, the
    /// drivers' history and a championship table; four new kinds of fact
    /// could double it, and the recap's own word budget is the limit that
    /// bites first. In finishing order, so the winner's is the one that
    /// survives a squeeze.
    /// </summary>
    public const int CarFacts = 2;

    /// <summary>
    /// The quickest lap ever set on this layout, and that nobody has
    /// beaten it.
    ///
    /// ACROSS EVERY CLASS, AND THE LINE SAYS SO. The club keeps one
    /// outright record per layout - RaceControlControl sets it from
    /// whatever class is on track - so this must not be worded as a class
    /// record. "The outright lap record on this layout" is true; "the
    /// Box Stock record" would be a claim the store cannot support, and
    /// the driver who holds the real one would be the first to say so.
    /// </summary>
    /// <param name="record">The layout's record as the track plan keeps it.</param>
    /// <param name="key">Which race, or which night - see <see cref="ForIntro"/> for why this one is the night's.</param>
    public static FactBlock? LayoutRecord(VenueRecord record, string key)
    {
        // A record set tonight is tonight's news, not the venue's history,
        // and a layout with almost nothing run on it has no record worth
        // the name. See VenueRecord.SetTonight and LeastRacesOnLayout.
        if (record.SetTonight || record.LapSeconds <= 0 || record.RacesOnLayout < LeastRacesOnLayout)
        {
            return null;
        }

        var layout = CommentaryFacts.Clean(record.LayoutName);
        var holder = CommentaryFacts.Clean(record.Holder);
        var when = CommentaryFacts.CleanLong(record.SetDescribed);

        // The holder's name and the month are both part of the claim. A
        // record with nobody's name on it, or with no date, is not a line
        // this app can stand behind, so it is not said at all.
        if (layout.Length == 0 || holder.Length == 0 || when.Length == 0)
        {
            return null;
        }

        // Invariant: on a comma-decimal PC "11,150s" escapes the
        // spoken-time rewrite, which looks for a point, and goes out over
        // the PA as "eleven thousand one hundred fifty seconds". See
        // CommentaryFacts.Invariant.
        var text = new StringBuilder(
            $"The outright lap record on this layout, {layout}, is {CommentaryFacts.Invariant(record.LapSeconds, "0.000")}s, set by {holder} in {when}.");

        if (record.RacesSince >= LeastRacesSince)
        {
            text.Append($" Nobody has beaten it in the {Plural.Of(record.RacesSince, "race")} the club has run on this layout since.");
        }

        // Themed on the VENUE and not on the driver who holds the record,
        // and the difference matters: the subject of this line is the
        // track. Cooling it under the holder's name would silence the
        // layout for a night because somebody mentioned them in a form
        // guide, and would leave two lines about the same track free to go
        // out back to back - which is the wrong way round. See
        // FactBlock.Theme.
        return new FactBlock($"venue:{key}:{layout}", text.ToString(), FactTheme.Venue(record.LayoutName));
    }

    /// <summary>
    /// How big this class's entry is against the club's own season - the
    /// line that frames a race before a wheel has turned.
    /// </summary>
    /// <param name="field">The field as the reader counted it.</param>
    /// <param name="key">Which night - see <see cref="ForIntro"/>.</param>
    public static FactBlock? Entry(FieldStrength field, string key)
    {
        if (field.Entries < LeastEntries || field.RacesCounted < LeastRacesForField)
        {
            return null;
        }

        var cls = CommentaryFacts.Clean(field.ClassName);

        // Without the class there is no scope, and without a scope this
        // line is "the biggest field the club has had" about one class's
        // heat - which is false at any club running two classes. Silence
        // rather than a claim that cannot say what it counted.
        if (cls.Length == 0)
        {
            return null;
        }

        var since = CommentaryFacts.CleanLong(field.BiggestSinceDescribed);

        if (since.Length > 0)
        {
            // "The biggest since last week" is true and says nothing. See
            // LeastRacesSince.
            if (field.RacesSinceThatBig < LeastRacesSince)
            {
                return null;
            }

            return new FactBlock(
                $"entry:{key}:{cls}",
                $"{Plural.Of(field.Entries, "car")} out in this {cls} race - the biggest {cls} field the club has had since {since}, "
                + $"{Plural.Of(field.RacesSinceThatBig, "race")} ago in this class.",
                FactTheme.Field(field.ClassName));
        }

        // Nothing in the class has ever been this big, which is the bigger
        // claim - so it says how many races it is counted over, and the
        // room can check it.
        return new FactBlock(
            $"entry:{key}:{cls}",
            $"{Plural.Of(field.Entries, "car")} out in this {cls} race - the biggest {cls} field the club has had in the "
            + $"{Plural.Of(field.RacesCounted, "race")} it has run in this class.",
            FactTheme.Field(field.ClassName));
    }

    /// <summary>
    /// How many of this grid have won a main in this class this year.
    ///
    /// THE YEAR, THE CLASS AND THE CLUB, all three in the line, because
    /// every one of them is doing work. "Six of these eight have won a
    /// main" is false the moment one of them won a main somewhere else, or
    /// in another class, or four seasons ago - and the reader counts none
    /// of those.
    /// </summary>
    public static FactBlock? MainWinners(FieldStrength field, string key)
    {
        // Never more than are out there: a count above the entry would be
        // arithmetic the room can see is wrong, and it would mean the
        // reader had counted somebody who is not in this race.
        if (field.MainWinners < LeastMainWinners
            || field.Entries < LeastEntries
            || field.MainWinners > field.Entries)
        {
            return null;
        }

        var cls = CommentaryFacts.Clean(field.ClassName);

        if (cls.Length == 0)
        {
            return null;
        }

        return new FactBlock(
            $"mains:{key}:{cls}",
            $"{Spell(field.MainWinners)} of the {Spell(field.Entries)} drivers in this race have won a {cls} main at this club this year.",
            FactTheme.Field(field.ClassName));
    }

    /// <summary>
    /// What the night has done to one driver's pace - the figure the app
    /// has worked out since track evolution was written and has never said
    /// about a person.
    /// </summary>
    /// <param name="driver">Their night as TonightAtTheTrack measured it.</param>
    /// <param name="raceKey">
    /// The RACE and not the night. Unlike the venue and the field above,
    /// this changes every time a race is filed - which is the whole point
    /// of it - so it is a fresh fact in the next heat. The same
    /// distinction CommentaryHistoryFacts.FormLine draws against ForIntro.
    /// </param>
    public static FactBlock? Tonight(DriverTonight driver, string raceKey)
    {
        // One race tonight is not a change, however the laps came out.
        if (driver.Races < 2)
        {
            return null;
        }

        var by = Math.Abs(driver.DeltaMs) / 1000.0;

        if (by < LeastChangeSeconds)
        {
            return null;
        }

        var name = CommentaryFacts.Clean(driver.Name);
        var first = CommentaryFacts.CleanLong(driver.FirstRace);

        // The race it is counted from is the scope. Without it the line is
        // "he is half a second quicker", which is quicker than what?
        if (name.Length == 0 || first.Length == 0)
        {
            return null;
        }

        var way = driver.DeltaMs < 0 ? "quicker" : "slower";

        return new FactBlock(
            $"tonight:{raceKey}:{driver.DriverId}",
            $"{name}'s middle lap is {CommentaryFacts.Invariant(by, "0.00")}s {way} now than it was in their first race tonight, {first}.",
            FactTheme.Driver(driver.DriverId));
    }

    /// <summary>
    /// The one thing worth saying about the car a finisher raced.
    ///
    /// One line per driver and never two: the recap is the longest segment
    /// of the night already, and a driver who gets both "his first Open
    /// race here" and "he has had that car since March" has had the recap
    /// turned into a profile of one entrant.
    ///
    /// THE SETUP LOG IS DELIBERATELY NOT IN HERE. The mechanic's interview
    /// reads it (MechanicBrief) and the interview runs straight after the
    /// recap, so a recap that also announced the setup change would be the
    /// two halves of one broadcast saying the same thing a minute apart -
    /// which is the complaint, not the fix.
    /// </summary>
    /// <param name="car">The car as the garage and the club's results know it.</param>
    /// <param name="key">Which night - see <see cref="ForRecap"/>.</param>
    /// <remarks>
    /// NOT called "Car": there is already a type by that name in this
    /// namespace (the garage's own), and a method with the same name
    /// hides it for every simple-name lookup inside this class - so the
    /// next person to want a Car in here would get an error nobody could
    /// place.
    /// </remarks>
    public static FactBlock? CarRaced(RaceCar car, string key)
    {
        var name = CommentaryFacts.Clean(car.Name);

        if (name.Length == 0)
        {
            return null;
        }

        var moved = CommentaryFacts.Clean(car.FirstRaceInClass);
        var before = CommentaryFacts.Clean(car.OnlyClassBefore);

        // Best first: a driver changing class is the story, and the car's
        // standing is decoration beside it.
        //
        // Only when every earlier start was in ONE other class, and enough
        // of them to be a move - see RaceCar.OnlyClassBefore and
        // LeastStartsBeforeSwitch. The comparison is case-insensitive
        // because a class name is typed, and "box stock" beside "Box
        // Stock" would otherwise be read out as a change of class.
        if (moved.Length > 0
            && before.Length > 0
            && car.StartsBefore >= LeastStartsBeforeSwitch
            && !string.Equals(moved, before, StringComparison.OrdinalIgnoreCase))
        {
            return new FactBlock(
                $"class:{key}:{car.DriverId}",
                $"That was {name}'s first {moved} race at this club - every one of their {Plural.Of(car.StartsBefore, "start")} here before this one was in {before}.",
                FactTheme.Driver(car.DriverId));
        }

        if (car.OnLoanForThisDay)
        {
            return new FactBlock(
                $"car:{key}:{car.DriverId}",
                car.ClubOwned
                    ? $"The car {name} raced is the club's own, out to them for this race day."
                    : $"The car {name} raced is not their own - it is on loan to them for this race day.",
                FactTheme.Driver(car.DriverId));
        }

        var since = CommentaryFacts.CleanLong(car.InGarageSince);

        if (since.Length > 0)
        {
            return new FactBlock(
                $"car:{key}:{car.DriverId}",
                $"{name} has had that car in the club's garage since {since}.",
                FactTheme.Driver(car.DriverId));
        }

        return null;
    }

    /// <summary>
    /// The race's own setting, for a race INTRO - best first.
    ///
    /// Best first is not decoration: FactPack keeps the order it is given
    /// and drops the tail when the prompt runs out of room, so this order
    /// is what survives a squeeze. The field comes first because it is
    /// what an intro is FOR - it frames a race before a wheel turns - then
    /// the venue's record, then what the night has done to the drivers.
    /// </summary>
    /// <param name="venue">The layout's record, or null where the club has none worth the name.</param>
    /// <param name="field">The field, or null at a club with too little on file to compare.</param>
    /// <param name="tonight">The drivers' own nights, best first as the reader found them.</param>
    /// <param name="raceKey">Which race - what the drivers' nights are keyed on, because those change race to race.</param>
    /// <param name="nightKey">
    /// The race day, when the caller knows it. The venue's record and the
    /// field are true for the whole EVENING - a record set in March is
    /// still March's in the sixth heat, and the entry does not change
    /// between heats - so keyed on the race they would be a different fact
    /// in every one of them, and a club running six heats would hear the
    /// same record read out six times. That is the fault
    /// CommentaryHistoryFacts.ForIntro's remarks set out at length, and
    /// this is the same fix. Left null, every key is the race's, which is
    /// what keeps a table test of this honest about one race at a time.
    /// </param>
    /// <param name="most">How many at most. Three - see <see cref="SceneFacts"/>.</param>
    public static IReadOnlyList<FactBlock> ForIntro(
        VenueRecord? venue,
        FieldStrength? field,
        IReadOnlyList<DriverTonight>? tonight,
        string raceKey,
        string? nightKey = null,
        int most = SceneFacts)
    {
        // One decision for the two builders that are true for the night.
        // See the remarks.
        var night = string.IsNullOrWhiteSpace(nightKey) ? raceKey : nightKey;

        var found = new List<FactBlock>();

        if (field is { } strength)
        {
            if (Entry(strength, night) is { } entry)
            {
                found.Add(entry);
            }

            if (MainWinners(strength, night) is { } winners)
            {
                found.Add(winners);
            }
        }

        if (venue is { } record && LayoutRecord(record, night) is { } lap)
        {
            found.Add(lap);
        }

        foreach (var driver in tonight ?? Array.Empty<DriverTonight>())
        {
            if (Tonight(driver, raceKey) is { } pace)
            {
                found.Add(pace);
            }
        }

        var cap = Math.Max(1, most);

        return found.Count <= cap ? found : found.Take(cap).ToList();
    }

    /// <summary>
    /// The cars a RECAP may mention, in finishing order.
    ///
    /// In finishing order because the winner is the one driver the recap
    /// is certain to name anyway, so their car is the one worth the room -
    /// and FactPack keeps the order it is given, so it is the line that
    /// survives the character cap.
    /// </summary>
    /// <param name="cars">The finishers' cars, as the garage and the club's results know them.</param>
    /// <param name="finished">Who finished where in the race just run. A retirement is zero or less and is skipped.</param>
    /// <param name="nightKey">
    /// The race day, when the caller knows it. Both of these are true for
    /// the whole evening - a car is still the club's in the main, and a
    /// driver's first Open race here is their first all night - so a
    /// race-scoped key would have the heat's recap and the main's recap
    /// each say it. See <see cref="ForIntro"/>.
    /// </param>
    /// <param name="most">How many at most. Two - see <see cref="CarFacts"/>.</param>
    public static IReadOnlyList<FactBlock> ForRecap(
        IReadOnlyList<RaceCar> cars,
        IReadOnlyList<(string DriverId, int Position)> finished,
        string nightKey,
        int most = CarFacts)
    {
        var found = new List<FactBlock>();

        if (cars.Count == 0 || finished.Count == 0)
        {
            return found;
        }

        // OrdinalIgnoreCase: a DriverId here is a transponder, and that is
        // how every other comparison in this app reads one.
        var known = new Dictionary<string, RaceCar>(StringComparer.OrdinalIgnoreCase);

        foreach (var car in cars)
        {
            if (!string.IsNullOrWhiteSpace(car.DriverId))
            {
                known[car.DriverId] = car;
            }
        }

        var order = finished
            .Where(f => f.Position > 0 && !string.IsNullOrWhiteSpace(f.DriverId))
            .OrderBy(f => f.Position)
            .ToList();

        var cap = Math.Max(1, most);

        foreach (var (driverId, _) in order)
        {
            if (known.TryGetValue(driverId, out var car) && CarRaced(car, nightKey) is { } block)
            {
                found.Add(block);
            }

            if (found.Count >= cap)
            {
                break;
            }
        }

        return found;
    }

    /// <summary>
    /// "two", "six" - small counts read as words, because these lines are
    /// written to be spoken rather than shown.
    ///
    /// CommentaryHistoryFacts' own, not a second copy: two spellings of
    /// the same count in one broadcast is the sort of difference nobody
    /// notices in review and everybody hears on the night.
    /// </summary>
    private static string Spell(int count) => CommentaryHistoryFacts.Spell(count);
}
