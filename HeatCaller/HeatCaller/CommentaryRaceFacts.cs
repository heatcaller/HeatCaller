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
/// One driver's own history on the layout a race is being run on.
///
/// WHY THIS IS THE LINE NOBODY CAN BEAT (11 Oct 2026)
///
/// The outright record on a layout belongs to one driver, and a club of
/// forty hears it as a fact about somebody else. THIS one belongs to
/// whoever is being talked about: everybody has a best lap on this layout,
/// including the driver who finishes eighth every week, and a human
/// announcer cannot hold forty drivers' bests across four layouts in his
/// head. It is the cheapest sentence in the whole broadcast that is about
/// the listener.
///
/// THE SCOPE IS IN THE LINE, as it is for everything in this file: "his
/// best here" is a claim about ONE layout, and said without the layout it
/// is a different and false claim about his career.
/// </summary>
/// <param name="DriverId">The transponder.</param>
/// <param name="Name">What the announcer calls them - already the profile's spoken name, never a transponder.</param>
/// <param name="BestSeconds">Their quickest lap ever on this layout, before tonight.</param>
/// <param name="BestSetDescribed">
/// When they set it, as the caller worded it - "August", or "March 2025".
/// The month and not the day: a best lap is not an event, and "in August"
/// is how somebody would say it.
/// </param>
/// <param name="RacesOnLayout">How many races they have run on this layout. What makes the figure mean anything - see <see cref="CommentaryRaceFacts.LeastRacesForOwnBest"/>.</param>
/// <param name="BeatenTonight">
/// They have already gone quicker than <paramref name="BestSeconds"/> in a
/// race on this layout tonight - the driver's own <see cref="VenueRecord.SetTonight"/>.
/// The figure stays what it was before tonight; the builder refuses it,
/// because "his quickest lap here is" a time the Big Screen has just shown
/// him beating is false.
/// </param>
public sealed record DriverOnLayout(
    string DriverId,
    string Name,
    double BestSeconds,
    string BestSetDescribed,
    int RacesOnLayout,
    bool BeatenTonight = false);

/// <summary>
/// What the club's whole filed history says about the layout a race is
/// being run on, beyond the one outright record <see cref="VenueRecord"/>
/// carries.
///
/// Two things, and both of them are things no human in the room could
/// know: every driver on this grid's own best lap here, and who has been
/// winning here lately.
/// </summary>
/// <param name="LayoutName">The layout, so every line can say its own scope.</param>
/// <param name="RacesOnLayout">How many races the club has filed on it. The threshold everything here is held to.</param>
/// <param name="Bests">Each driver on this grid who has enough history here to be worth a sentence. May be empty, which is the usual answer at a club in its first weeks.</param>
/// <param name="WinnerName">
/// Who has won every one of the last <paramref name="WinnerRun"/> races on
/// this layout. Empty when the last two races went to different drivers,
/// which is most of the time and is not a fact.
/// </param>
/// <param name="WinnerRun">How many in a row. Below <see cref="CommentaryRaceFacts.LeastRunToCall"/> this is not a run, it is a coincidence.</param>
public sealed record LayoutLegend(
    string LayoutName,
    int RacesOnLayout,
    IReadOnlyList<DriverOnLayout> Bests,
    string WinnerName,
    int WinnerRun);
/// <summary>
/// Who has been winning one class's MAINS on this layout, counted in
/// MEETINGS - see <see cref="CommentaryReaders.MainsHere"/>.
///
/// THE RUN IS THE BAR, NOT THE NEWS. Everything in here is about the
/// meetings BEFORE tonight: the fact builder is handed tonight's winner
/// separately and decides whether the night extended the run or ended it.
/// A record that counted tonight in would make every winner their own
/// precedent.
/// </summary>
/// <param name="LayoutName">The layout, which every line must name - a run "here" means nothing without it.</param>
/// <param name="ClassName">The class whose mains were counted. In the line too: a club with three classes has three mains a night.</param>
/// <param name="Meetings">How many meetings the club has run a main of this class on this layout, before tonight.</param>
/// <param name="HolderName">The announcer's name for whoever won the last <see cref="HolderMeetings"/> of them. Never a transponder.</param>
/// <param name="HolderId">Their transponder - for comparing against tonight's winner, and for the fact's theme.</param>
/// <param name="HolderMeetings">How many meetings in a row they won it. Never zero: the reader answers null rather than a run of none.</param>
public sealed record MainsOnLayout(
    string LayoutName,
    string ClassName,
    int Meetings,
    string HolderName,
    string HolderId,
    int HolderMeetings);


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
/// <param name="BiggerTonight">
/// A race in this class earlier tonight had more cars than this one.
/// Tonight is skipped when looking for "the biggest since" (see
/// CommentaryReaders.Field), so without this a 10-car heat an hour after a
/// 12-car one would be called the biggest field since last week.
/// </param>
public sealed record FieldStrength(
    string ClassName,
    int Entries,
    int RacesCounted,
    string? BiggestSinceDescribed,
    int RacesSinceThatBig,
    int MainWinners,
    bool BiggerTonight = false);

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
/// What a driver's race pace tonight is against their PRACTICE pace
/// tonight - "he was a second off this in practice".
///
/// The same bargain <see cref="DriverTonight"/> strikes, for the same
/// reason: TonightAtTheTrack.PracticeAgainstNow works the figure out and
/// this only carries the answer, so there is one implementation of "how
/// much quicker is he" and not two drifting apart.
///
/// WHY THERE IS NO CLASS IN HERE. Practice has no round and so no class.
/// TonightAtTheTrack.DriverPracticeToNow says at length what that costs
/// and why saying otherwise would have been worse; the short version is
/// that a line built from this says "in practice" and never names a
/// class, because the app cannot know the driver practised the car they
/// are racing.
/// </summary>
/// <param name="DriverId">The transponder.</param>
/// <param name="Name">As it should be said.</param>
/// <param name="DeltaMs">Their middle lap racing against practising. Negative when they are quicker now.</param>
/// <param name="Runs">How many of tonight's practice runs they had enough laps in to be measured.</param>
public sealed record DriverAgainstPractice(
    string DriverId,
    string Name,
    long DeltaMs,
    int Runs);

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
/// <param name="Practice">
/// What the grid has found since practice tonight, as
/// CommentaryReaders.AgainstPractice worked it out - or null, which is the
/// usual answer and every way of being null is correct: a club that does
/// not run its practice through HeatCaller, a first race of the night,
/// nobody on this grid having both practised and raced yet.
///
/// APPENDED LAST, for the reason the thread and the conditional both were
/// and which this record states above: every "new RaceScene(...)" is
/// positional, so a field inserted anywhere else would silently hand one
/// record's value to another's parameter at every call site.
/// </param>
/// <param name="Stakes">
/// WHAT IS STILL POSSIBLE IN THE CHAMPIONSHIP, over the rounds that
/// remain - "only Bill and Sam can still win it", "nobody left can reach
/// him". Proved by CommentaryReaders.TitleOnTheLine, and null for every
/// season whose length the club has not typed. See TitleStakes and
/// CommentaryStakesFacts.
///
/// LAST, so every caller written before 12 Oct 2026 still compiles and
/// still means what it meant.
///
/// THIS WAS A SUMMARY INSIDE THE PARAMETER LIST until 13 Oct 2026, which
/// is CS1587: a doc comment there is not on a language element, so the
/// compiler drops it and none of the above reached the documentation or a
/// tooltip. It read perfectly well in the file, which is exactly why it
/// sat there for a month. Every other parameter here is documented with a
/// param tag, and now so is this one.
/// </param>
public sealed record RaceScene(
    VenueRecord? Venue = null,
    FieldStrength? Field = null,
    IReadOnlyList<DriverTonight>? Tonight = null,
    string? NightKey = null,
    NightThread? Thread = null,
    RaceConditional? Conditional = null,
    IReadOnlyList<DriverAgainstPractice>? Practice = null,
    LayoutLegend? Legend = null,
    TitleStakes? Stakes = null);

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
    /// How many races a driver has to have run on a layout before their own
    /// best lap on it is a figure rather than a coincidence.
    ///
    /// Four. Three would let a driver's second-ever visit to a layout be
    /// "his best here", which is true and worthless - everybody's second
    /// lap on a new track is their best. Four means they have been round
    /// it enough times that the figure is their measure of the place, which
    /// is what makes "he has never gone quicker here" worth hearing.
    /// </summary>
    public const int LeastRacesForOwnBest = 4;

    /// <summary>
    /// How many wins in a row on one layout make a run worth calling.
    ///
    /// Three. Two in a row is a coincidence at a club where ten drivers
    /// share the wins, and calling it one is the superlative-with-nothing-
    /// behind-it fault again - with the added cost that the driver who won
    /// the race before those two is standing right there.
    /// </summary>
    public const int LeastRunToCall = 3;

    /// <summary>
    /// How many drivers' own bests on the layout a single intro may carry.
    ///
    /// Two. Each one is a sentence about a person, and the prompt names
    /// eight drivers - unchecked, this section alone would be eight
    /// sentences of lap times, which is the thing a crowd stops listening
    /// to. Two is a remark about the pair worth remarking on; the cap on
    /// <see cref="ForIntro"/> then decides whether both survive.
    /// </summary>
    public const int OwnBestFacts = 2;

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
    /// A driver's OWN best lap on the layout being raced on, and how many
    /// times they have been round it.
    ///
    /// THE ONE THAT IS ABOUT THE LISTENER. The outright record on a layout
    /// belongs to one driver and the rest of the room hears it as somebody
    /// else's news. This belongs to whoever is being talked about, and the
    /// driver who finishes eighth every week has one too - see
    /// <see cref="DriverOnLayout"/>.
    ///
    /// WHAT IT DELIBERATELY DOES NOT SAY. It does not predict, compare two
    /// drivers, or suggest anybody is due. It states a figure and the
    /// number of races it was measured over, and leaves the announcer to
    /// make something of it - which is the discipline every builder in
    /// this file keeps.
    ///
    /// Null three ways, each of them the honest answer: no name to say it
    /// about, no time to say, and too few races on this layout for the
    /// figure to be their measure of the place (see
    /// <see cref="LeastRacesForOwnBest"/>).
    /// </summary>
    /// <param name="driver">The driver's history here, as the reader worked it out.</param>
    /// <param name="layoutName">The layout, which the line must name - see the note on this class about scope.</param>
    /// <param name="key">Which night - see <see cref="ForIntro"/>. A driver's best here is the same all evening until they beat it.</param>
    public static FactBlock? BestOnThisLayout(DriverOnLayout driver, string layoutName, string key)
    {
        var who = CommentaryFacts.Clean(driver.Name);
        var layout = CommentaryFacts.Clean(layoutName);
        var when = CommentaryFacts.CleanLong(driver.BestSetDescribed);

        if (who.Length == 0 || layout.Length == 0 || driver.BestSeconds <= 0)
        {
            return null;
        }

        // Said in the present tense, so a best already beaten tonight is
        // not said at all - the reason LayoutRecord refuses SetTonight.
        if (driver.BeatenTonight)
        {
            return null;
        }

        if (driver.RacesOnLayout < LeastRacesForOwnBest)
        {
            return null;
        }

        // Invariant, for the reason LayoutRecord states at length: on a
        // comma-decimal PC "11,402s" escapes the spoken-time rewrite and
        // goes out over the PA as eleven thousand four hundred seconds.
        var text = new StringBuilder(
            $"{who}'s own quickest lap on this layout, {layout}, is {CommentaryFacts.Invariant(driver.BestSeconds, "0.000")}s");

        // The month only where the caller could date it. A figure with no
        // date is still a true figure, unlike the outright record, which
        // cannot be told apart from one set an hour ago without one.
        text.Append(when.Length > 0 ? $", set in {when}" : string.Empty);

        text.Append($", over the {Plural.Of(driver.RacesOnLayout, "race")} they have run on it.");

        // THEMED ON THE DRIVER, not on the venue - and the difference is
        // the opposite way round from LayoutRecord's. The subject of this
        // line is the person: two of these about the same driver must not
        // go out back to back, and it must not be silenced because
        // somebody else's record was read out about the same track. See
        // FactBlock.Theme.
        return new FactBlock(
            $"ownbest:{key}:{layout}:{driver.DriverId}",
            text.ToString(),
            FactTheme.Driver(driver.DriverId));
    }

    /// <summary>
    /// Who has been winning on this layout - the unbroken run of one
    /// driver taking every race the club has filed on it lately.
    ///
    /// ANOTHER CLAIM NO HUMAN HOLDS. "The last five races on this layout
    /// have all gone to Bill" is countable only by something that has read
    /// every result the club ever filed, and it is the line that gives a
    /// race a shape before it starts: everybody in the room now knows what
    /// would be remarkable about the next twenty minutes.
    ///
    /// RACES, AND THE LINE SAYS RACES. Not mains, not a championship -
    /// the count is over every race filed on this layout, because that is
    /// what the reader can prove from the history alone. Wording it as
    /// "the last five A-mains" would be a claim the store cannot support,
    /// and the driver who won the heat in between would be the first to
    /// say so. The same discipline LayoutRecord keeps about classes.
    ///
    /// Null when there is no run - which is the usual answer at a club
    /// where the wins go round, and is not a disappointment: a layout
    /// nobody owns has nothing to say here and the intro reads as it did.
    /// </summary>
    /// <param name="legend">The layout's history, as the reader worked it out.</param>
    /// <param name="key">Which night - a run is the same fact all evening until somebody ends it.</param>
    public static FactBlock? WhoWinsHere(LayoutLegend legend, string key)
    {
        var who = CommentaryFacts.Clean(legend.WinnerName);
        var layout = CommentaryFacts.Clean(legend.LayoutName);

        if (who.Length == 0 || layout.Length == 0)
        {
            return null;
        }

        if (legend.WinnerRun < LeastRunToCall || legend.RacesOnLayout < LeastRacesOnLayout)
        {
            return null;
        }

        var text =
            $"The last {Spell(legend.WinnerRun)} races the club has run on this layout, {layout}, have all gone to {who}.";

        // THEMED ON THE VENUE and not on the winner, which is the opposite
        // of BestOnThisLayout above and deliberate: the subject of this
        // line is the track. Themed on the winner it would silence the
        // layout for a night because somebody mentioned them in a form
        // guide, and would let this and the outright record go out back to
        // back about the same piece of tarmac.
        return new FactBlock($"winshere:{key}:{layout}", text, FactTheme.Venue(legend.LayoutName));
    }

    /// <summary>
    /// How big this class's entry is against the club's own season - the
    /// line that frames a race before a wheel has turned.
    /// </summary>
    /// <param name="field">The field as the reader counted it.</param>
    /// <param name="key">Which night - see <see cref="ForIntro"/>.</param>
    public static FactBlock? Entry(FieldStrength field, string key)
    {
        // A bigger field earlier tonight makes every "biggest" here false.
        if (field.Entries < LeastEntries || field.RacesCounted < LeastRacesForField || field.BiggerTonight)
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
    /// What a driver has found since practice - the Tier 1 item the depth
    /// plan actually asked for, built 11 Oct 2026: "he was a second off
    /// this in practice".
    ///
    /// THE SCOPE IS "IN PRACTICE" AND NOT A CLASS, and that is not
    /// laziness. A practice session has no round, so no class, so the app
    /// genuinely does not know whether the driver practised the car they
    /// are racing - see TonightAtTheTrack.DriverPracticeToNow, which sets
    /// out why reading practice through a helper of its own was the only
    /// honest way to build this at all. The line says what is true.
    ///
    /// Keyed on the RACE, like <see cref="Tonight"/>: the figure moves
    /// every time a race is filed, which is the point of it.
    /// </summary>
    /// <param name="driver">Their practice against their racing, as TonightAtTheTrack measured it.</param>
    /// <param name="raceKey">The race, for the reason Tonight's own note gives.</param>
    public static FactBlock? AgainstPractice(DriverAgainstPractice driver, string raceKey)
    {
        // One practice run is plenty - unlike the race-to-race figure,
        // which needs two races because one race is not a change. Here the
        // two sides are a practice run and a race, so one of each is a
        // comparison.
        if (driver.Runs < 1)
        {
            return null;
        }

        var by = Math.Abs(driver.DeltaMs) / 1000.0;

        if (by < LeastChangeSeconds)
        {
            return null;
        }

        var name = CommentaryFacts.Clean(driver.Name);

        if (name.Length == 0)
        {
            return null;
        }

        var way = driver.DeltaMs < 0 ? "quicker" : "slower";

        return new FactBlock(
            $"practice:{raceKey}:{driver.DriverId}",
            $"{name}'s middle lap in their latest race tonight was {CommentaryFacts.Invariant(by, "0.00")}s {way} "
            + "than their middle lap in practice tonight.",
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
        int most = SceneFacts,
        IReadOnlyList<DriverAgainstPractice>? practice = null)
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

        // AFTER the race-to-race figure, so that where a club has both the
        // one measured over races survives the cap. Race-to-race is the
        // stronger claim: both sides are races of the same class, where
        // practice has no class at all - see DriverAgainstPractice.
        //
        // AND THE TWO RARELY COMPETE, which is why this lands rather than
        // being crowded out by the cap. Race-to-race needs a driver to have
        // had TWO races tonight, so it says nothing before the third race
        // of a class. Practice-to-now needs one practice run and one race,
        // so it is the pace fact that EXISTS at the second race - the slot
        // where the app previously had nothing to say about anybody's pace
        // at all.
        foreach (var driver in practice ?? Array.Empty<DriverAgainstPractice>())
        {
            if (AgainstPractice(driver, raceKey) is { } since)
            {
                found.Add(since);
            }
        }

        var cap = Math.Max(1, most);

        return found.Count <= cap ? found : found.Take(cap).ToList();
    }

    /// <summary>
    /// The layout's own section of an intro: who has been winning here, and
    /// whose own best lap here is worth stating.
    ///
    /// A SECTION OF ITS OWN, not more items for <see cref="ForIntro"/>, and
    /// that is the whole reason this method exists. ForIntro's cap is three
    /// and the field, the record and the pace figures already compete for
    /// them - appended there, every line below would have been built on
    /// every race of the evening and heard on none of them. The conditional
    /// made the same argument on the same day and got the same answer: its
    /// own section, its own character cap, so it can never squeeze a record
    /// out of the prompt and a record can never squeeze it. See
    /// CommentaryService.MaxLayoutChars.
    ///
    /// THE RUN FIRST. "The last five races here have all gone to Bill"
    /// gives the race a shape before it starts - the room now knows what
    /// would be remarkable about the next twenty minutes - where a lap time
    /// is a figure. So if only one line survives the cap, that is the one.
    ///
    /// Empty is the usual answer at a club in its first weeks, and every
    /// way of being empty is correct: a new layout, a club where the wins
    /// go round, a grid nobody has raced here four times. The intro then
    /// reads exactly as it did before there was any of this.
    /// </summary>
    /// <param name="legend">The layout's history, as CommentaryReaders.LayoutStory worked it out. Null at a club with no layout named.</param>
    /// <param name="key">
    /// Which night. Everything here is true for the whole evening - a run
    /// of wins is the same run until somebody ends it, and a driver's best
    /// here is the same until they beat it - so a race-scoped key would
    /// have a club running six heats hear the same line in all six. The
    /// same argument <see cref="ForIntro"/> makes for its night key.
    /// </param>
    /// <param name="most">How many at most. Three: the run, and up to <see cref="OwnBestFacts"/> drivers' own bests.</param>
    /// <summary>
    /// How many meetings the club must have run a class's main on a layout
    /// before a run there is worth saying.
    ///
    /// Four, so a layout laid out last month cannot produce "he has won
    /// the main here at every meeting" off two evenings. Higher than
    /// <see cref="LeastRunToCall"/> on purpose: the run is the claim, and
    /// a run that IS the whole history of the layout is not a run, it is
    /// the layout being new.
    /// </summary>
    public const int LeastMeetingsHere = 4;

    /// <summary>
    /// The run of main wins on this layout, as tonight's result leaves it -
    /// either extended, or ended.
    ///
    /// THE LINE NO HUMAN ANNOUNCER COULD SAY. Nobody standing at a club
    /// track holds "who won the main on this layout at each of the last
    /// fourteen meetings" in their head, across three classes and four
    /// layouts and two years of Thursday nights. The app does, because it
    /// filed every one of them - and this is the sentence that proves the
    /// announcer is reading the club's own history rather than being
    /// enthusiastic about a lap time.
    ///
    /// ENDED IS THE BETTER HALF, and it is the one that did not exist
    /// before 11 Oct 2026: WhoWinsHere can say a run is running, and
    /// nothing could say one had just stopped. "That run ends tonight" is
    /// the moment the whole feature is for, and it is a moment that only
    /// happens once.
    ///
    /// NOTHING IS EMBROIDERED. The run counted is the run BEFORE tonight,
    /// so the extended line says one more than the reader found and the
    /// ended line says exactly what the reader found. Neither claims a
    /// meeting that is not in the count.
    /// </summary>
    /// <param name="mains">The mains' history here, as CommentaryReaders.MainsHere worked it out.</param>
    /// <param name="winnerId">Tonight's winner, as a transponder. Empty when nobody won in a way worth counting.</param>
    /// <param name="winnerName">The announcer's name for them. Empty is no fact, not an anonymous one.</param>
    /// <param name="key">Which night - the recap passes the race day (14 Oct 2026), so with the layout and class in the fact's key the line is said once a meeting, not once per A-main leg.</param>
    public static FactBlock? RunOfMainsHere(MainsOnLayout mains, string? winnerId, string? winnerName, string key)
    {
        var layout = CommentaryFacts.Clean(mains.LayoutName);
        var cls = CommentaryFacts.Clean(mains.ClassName);
        var holder = CommentaryFacts.Clean(mains.HolderName);
        var winner = CommentaryFacts.Clean(winnerName);
        var who = CommentaryFacts.Clean(winnerId);

        if (layout.Length == 0 || cls.Length == 0 || holder.Length == 0 || winner.Length == 0 || who.Length == 0)
        {
            return null;
        }

        if (mains.HolderMeetings < LeastRunToCall || mains.Meetings < LeastMeetingsHere)
        {
            return null;
        }

        var sameDriver = string.Equals(who, CommentaryFacts.Clean(mains.HolderId), StringComparison.OrdinalIgnoreCase);

        // EXTENDED: the run counted is the one before tonight, so tonight
        // makes it one longer. Said about the person, which is why it is
        // themed on them below.
        if (sameDriver)
        {
            return new FactBlock(
                $"mainsrun:{key}:{layout}:{cls}",
                $"That is {Spell(mains.HolderMeetings + 1)} club meetings in a row that {winner} has won the "
                + $"{cls} main on this layout, {layout}.",
                FactTheme.Driver(who));
        }

        // ENDED. Both names are in it and the count is exactly what the
        // reader found - the meetings the run actually covered, not
        // counting tonight, which is the meeting that ended it.
        return new FactBlock(
            $"mainsrun:{key}:{layout}:{cls}",
            $"{holder} had won the {cls} main on this layout, {layout}, at each of the last "
            + $"{Spell(mains.HolderMeetings)} club meetings. {winner} has ended that run tonight.",

            // THEMED ON THE VENUE, not on either driver - and the choice is
            // the same one WhoWinsHere makes. The subject of this sentence
            // is what happens on this piece of tarmac; two drivers are named
            // in it and neither of them is what it is about. Themed on the
            // winner it would be held back because a form guide mentioned
            // them, which is the wrong line to lose.
            FactTheme.Venue(mains.LayoutName));
    }

    /// <summary>
    /// The mains' own history here as a recap can use it - at most one
    /// line, because a run extended and a run ended are the same sentence
    /// from two sides and only one of them is ever true.
    /// </summary>
    /// <param name="mains">Null at a club with no layout named, no class, no programme, or nothing filed. Then nothing.</param>
    /// <param name="winnerId">Tonight's winner's transponder.</param>
    /// <param name="winnerName">And the announcer's name for them.</param>
    /// <param name="key">This race's key - see RunOfMainsHere.</param>
    public static IReadOnlyList<FactBlock> ForTheMainsHere(
        MainsOnLayout? mains,
        string? winnerId,
        string? winnerName,
        string key)
    {
        if (mains is null || RunOfMainsHere(mains, winnerId, winnerName, key) is not { } run)
        {
            return Array.Empty<FactBlock>();
        }

        return new[] { run };
    }

    public static IReadOnlyList<FactBlock> ForTheLayout(LayoutLegend? legend, string key, int most = 1 + OwnBestFacts)
    {
        var found = new List<FactBlock>();

        if (legend is null)
        {
            return found;
        }

        if (WhoWinsHere(legend, key) is { } run)
        {
            found.Add(run);
        }

        var bests = 0;

        foreach (var driver in legend.Bests)
        {
            if (bests >= OwnBestFacts)
            {
                break;
            }

            if (BestOnThisLayout(driver, legend.LayoutName, key) is { } best)
            {
                found.Add(best);
                bests++;
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
