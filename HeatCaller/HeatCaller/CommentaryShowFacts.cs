namespace HeatCaller;

/// <summary>
/// The night as it stands before anything has run.
/// </summary>
/// <param name="Classes">How many classes are racing.</param>
/// <param name="Drivers">How many drivers are in, after check-in.</param>
/// <param name="Rounds">How many rounds the programme holds.</param>
/// <param name="VenueName">Where, when the club wants it said.</param>
/// <param name="TravelledFrom">How many drivers came from outside the club's own membership, where that is known.</param>
/// <param name="ChampionshipNote">"Two rounds left and four points covering the top three" - the caller's words, because only the season knows.</param>
public readonly record struct NightAhead(
    int Classes,
    int Drivers,
    int Rounds,
    string? VenueName = null,
    int? TravelledFrom = null,
    string? ChampionshipNote = null);

/// <summary>
/// The night as it finished.
/// </summary>
/// <param name="Races">How many races actually ran.</param>
/// <param name="Laps">How many laps were turned in total.</param>
/// <param name="FastestLapSeconds">The quickest lap anybody did, or zero when none was set.</param>
/// <param name="FastestLapHolder">Who did it.</param>
/// <param name="ClosestFinishSeconds">The smallest winning margin of the night, or zero when there was nothing to measure.</param>
/// <param name="ClosestFinishWinner">Who won it.</param>
/// <param name="Debutants">How many drivers raced here for the first time tonight.</param>
/// <param name="ChampionshipNote">"The title goes down to the last round" - the caller's words.</param>
public readonly record struct NightDone(
    int Races,
    int Laps,
    double FastestLapSeconds = 0,
    string? FastestLapHolder = null,
    double ClosestFinishSeconds = 0,
    string? ClosestFinishWinner = null,
    int? Debutants = null,
    string? ChampionshipNote = null);

/// <summary>
/// THE NIGHT'S THREAD: the one storyline an evening is told as, as far as
/// the club's own saved results will support it.
///
/// WHY THIS ONE IS NOT LIKE THE OTHER FACTS IN THE APP
///
/// Every other record in this layer is derived TRUTH - a lap record, a
/// field, a career, the car. This is a FRAMING, and that is the whole of
/// its difficulty. The depth plan's own example is "can anyone stop Dave
/// tonight", and the rule that makes such a thing shippable is one
/// sentence long:
///
///   THE SHAPE IS INVENTED. THE CLAIM INSIDE IT NEVER IS.
///
/// "Can anybody stop that run tonight?" is a question, and a question
/// cannot be false. "Dave has won the last race of the night at each of
/// the club's last three race days" is a claim, and it is counted out of
/// the club's own filed results or it is not said at all. A storyline
/// about real people that the scoreboard does not support is the timing
/// system telling the room something untrue in front of forty people, and
/// the timing is what a club trusts this app with - see "What HeatCaller
/// is" in the roadmap. "Billy is on for a clean sweep" is sayable only
/// when Billy has actually won everything so far.
///
/// AND IT HAS TO BE ABLE TO BE WRONG-FOOTED
///
/// A thread that keeps asking "can anyone stop Dave" after Dave went out
/// in heat three is worse than no thread at all: it is the one failure a
/// room notices instantly and laughs at. So NOTHING ABOUT A THREAD IS
/// REMEMBERED. There is no stored storyline anywhere in this app to go
/// stale - this record is read fresh from the filed results every time it
/// is asked (CommentaryReaders.NightStory), which means the moment Dave is
/// beaten the reader says so in <see cref="BeatenBy"/> and the builders
/// below TURN the thread instead of repeating it. The contradiction is not
/// detected; it is simply never not looked at.
///
/// A THREAD THAT CANNOT BE SUPPORTED IS NO THREAD. A night with no shape -
/// everyone winning one thing each - gets nothing rather than a
/// manufactured rivalry, which is why there is no pair or rivalry form of
/// this record: one subject or silence. A club in its first week and a
/// club with four drivers both get nothing, by
/// <see cref="CommentaryShowFacts.LeastDaysOnFile"/> and
/// <see cref="CommentaryShowFacts.LeastRivals"/>.
///
/// A sealed record rather than the two structs above it in this file,
/// because this one is read as a nullable answer like every other
/// store-backed record in the commentary layer (VenueRecord,
/// FieldStrength) and is carried as one on RaceScene.
/// </summary>
/// <param name="SubjectId">The transponder the run belongs to. Part of every key, so two subjects in one night cannot use each other's up.</param>
/// <param name="Name">As it should be SAID - the announcer's name for them, so the thread and the result lines never name two different drivers.</param>
/// <param name="DaysWon">
/// How many of the club's last race days in a row the subject won the last
/// race of, TONIGHT EXCLUDED.
///
/// Tonight is left out of this on purpose and it is the reason the thread
/// cannot drift mid-evening: a run counted over finished nights is the
/// same number in the sixth heat as it was at the cold open, so the
/// subject of the thread cannot change under the room's feet. What tonight
/// has done to it is the four fields below.
/// </param>
/// <param name="DaysOnFile">Race days the club has filed, tonight excluded - the bar that makes a club in its first week get nothing.</param>
/// <param name="Rivals">How many OTHER drivers raced across the days in the run - the bar that makes a four-driver club get nothing.</param>
/// <param name="StartedTonight">Tonight's filed races the subject was in. Zero before they have been out.</param>
/// <param name="WonTonight">How many of those they won. Equal to StartedTonight exactly while they are unbeaten.</param>
/// <param name="BeatenBy">
/// The first driver to finish ahead of the subject tonight, as the result
/// saved their name - or empty where the result has no name on it, which
/// the builder has wording for. NULL, and only null, means not beaten:
/// that is the one field the whole wrong-footing turns on.
/// </param>
/// <param name="WonLastTonight">The subject won the last race of tonight - measured the same way the run was, so extending it is the same claim.</param>
/// <param name="NightOver">
/// Every round on tonight's programme has been recorded and at least one
/// race has been filed.
///
/// The sign-off's resolution is refused without it, and that is not
/// tidiness. "His run ended tonight" said while the A-main is still to
/// come is the same fault as the thread the night contradicted, told
/// backwards - and the room is standing there waiting for that race.
/// </param>
public sealed record NightThread(
    string SubjectId,
    string Name,
    int DaysWon,
    int DaysOnFile,
    int Rivals,
    int StartedTonight = 0,
    int WonTonight = 0,
    string? BeatenBy = null,
    bool WonLastTonight = false,
    bool NightOver = false);

/// <summary>
/// The two ends of the broadcast: the cold open and the sign-off.
///
/// Both are flourishes - the first thing CommentaryBudget stops paying
/// for when a month runs short - and both are worth having anyway,
/// because they are what make an evening feel like a show rather than a
/// series of races with announcements over them. They bracket the night,
/// and a bracket is noticed mostly when it is missing.
///
/// WHAT MAKES A SIGN-OFF WORK
///
/// Numbers nobody was counting. Everybody in the room watched the racing;
/// nobody knows that four hundred and twelve laps were turned, or that
/// the closest finish all night was six hundredths. Those are facts only
/// the timing system holds, and reading them back is the cheapest way to
/// make an evening feel like it was recorded rather than just attended.
///
/// Every field is optional and nothing is invented. A night with no laps
/// recorded gets a short sign-off rather than a padded one.
///
/// AND THE THREAD THAT RUNS BETWEEN THEM (11 Oct 2026)
///
/// The bookends are also where the evening's one storyline is set and
/// paid off - <see cref="ThreadAhead"/> at the top, <see cref="ThreadDone"/>
/// at the end, with <see cref="ThreadSoFar"/> referred to in between by a
/// segment that is mostly about something else. It lives here because it
/// IS the bracket: a cold open that sets something up and a sign-off that
/// resolves it is the difference between a show and a series of races with
/// announcements over them, which is the whole argument above.
///
/// It is also the one thing in this app that invents a SHAPE, and
/// NightThread states the rule that makes that safe at length. Read it
/// before changing a word of the three builders.
/// </summary>
public static class CommentaryShowFacts
{
    /// <summary>A margin has to be inside this before "the closest finish of the night" means anything.</summary>
    public const double CloseFinishSeconds = 0.5;

    /// <summary>
    /// The cold open, for the top of the night.
    ///
    /// Said once, before a wheel has turned, so there is nothing to draw
    /// on but the shape of the evening - and that is the point: it is the
    /// only moment where "six classes and forty-one drivers" is news.
    /// </summary>
    public static FactBlock? ColdOpen(NightAhead night, string dayKey)
    {
        if (night.Drivers <= 0 && night.Classes <= 0)
        {
            return null;
        }

        var parts = new List<string>();

        if (night.Drivers > 0 && night.Classes > 0)
        {
            parts.Add($"{night.Drivers} drivers across {Count(night.Classes, "class", "classes")}");
        }
        else if (night.Drivers > 0)
        {
            parts.Add($"{night.Drivers} drivers");
        }
        else
        {
            parts.Add(Count(night.Classes, "class", "classes"));
        }

        // Straight after the drivers, so "of them" has drivers to refer
        // to rather than races.
        if (night.TravelledFrom is { } travelled && travelled > 0)
        {
            parts.Add($"{travelled} of them visiting");
        }

        if (night.Rounds > 0)
        {
            parts.Add($"{Count(night.Rounds, "race", "races")} to run");
        }

        var where = CommentaryFacts.Clean(night.VenueName);
        var opening = where.Length > 0
            ? $"Tonight at {where}: {string.Join(", ", parts)}."
            : $"Tonight: {string.Join(", ", parts)}.";

        var season = CommentaryFacts.CleanLong(night.ChampionshipNote);

        return new FactBlock(
            $"coldopen:{dayKey}",
            season.Length > 0 ? $"{opening} {season}" : opening);
    }

    /// <summary>
    /// The night's numbers, for the way out.
    ///
    /// Returned as separate blocks rather than one paragraph so FactPack
    /// can drop the tail if the room runs short, and so the order is the
    /// priority: the totals, then the fastest lap, then the closest
    /// finish, then anything about the season.
    /// </summary>
    public static IReadOnlyList<FactBlock> SignOff(NightDone night, string dayKey)
    {
        var found = new List<FactBlock>();

        if (night.Races > 0)
        {
            found.Add(new FactBlock($"signoff:{dayKey}:totals", night.Laps > 0
                ? $"That is {Count(night.Races, "race", "races")} and {Count(night.Laps, "lap", "laps")} tonight."
                : $"That is {Count(night.Races, "race", "races")} tonight."));
        }

        var fastest = CommentaryFacts.Clean(night.FastestLapHolder);

        if (night.FastestLapSeconds > 0 && fastest.Length > 0)
        {
            // Every time and margin below goes in with a decimal POINT
            // whatever the PC's region. On a comma-decimal laptop this was
            // written "12,345s", the model repeated it, and the PA read out
            // "twelve thousand three hundred forty-five seconds". See
            // CommentaryFacts.Invariant.
            found.Add(new FactBlock($"signoff:{dayKey}:fastest",
                $"The fastest lap of the night went to {fastest}: {CommentaryFacts.Invariant(night.FastestLapSeconds, "0.000")}s."));
        }

        // Only when it was actually close. "The closest finish of the
        // night was two and a half seconds" is a statistic about a dull
        // evening, and saying it out loud makes it a worse one.
        var closest = CommentaryFacts.Clean(night.ClosestFinishWinner);

        if (night.ClosestFinishSeconds > 0
            && night.ClosestFinishSeconds <= CloseFinishSeconds
            && closest.Length > 0)
        {
            found.Add(new FactBlock($"signoff:{dayKey}:closest",
                $"The closest finish all night was {closest}'s, by {CommentaryFacts.Invariant(night.ClosestFinishSeconds, "0.00")}s."));
        }

        if (night.Debutants is { } debuts && debuts > 0)
        {
            found.Add(new FactBlock($"signoff:{dayKey}:debuts", debuts == 1
                ? "One driver raced here for the first time tonight."
                : $"{debuts} drivers raced here for the first time tonight."));
        }

        var season = CommentaryFacts.CleanLong(night.ChampionshipNote);

        if (season.Length > 0)
        {
            found.Add(new FactBlock($"signoff:{dayKey}:season", season));
        }

        return found;
    }

    // ------------------------------------------------------------------
    // THE NIGHT'S THREAD (11 Oct 2026)
    //
    // Set at the cold open, referred to between races, resolved at the
    // sign-off. See NightThread for the discipline all three keep, and
    // CommentaryReaders.NightStory for where the truth inside the framing
    // is counted.
    // ------------------------------------------------------------------

    /// <summary>
    /// How many race days in a row the subject has to have won before
    /// there is a run worth building an evening on.
    ///
    /// Two. One is "he won last week", which is a result rather than a
    /// story, and an evening framed on it is the manufactured rivalry this
    /// whole feature is written to refuse.
    /// </summary>
    public const int LeastDaysWon = 2;

    /// <summary>
    /// How many race days the club needs on file before any of this means
    /// anything.
    ///
    /// Four, and this is the bar that makes a club in its first week get
    /// nothing at all. With two nights filed, whoever won the second one
    /// has "won the last race of the night at each of the club's last two
    /// race days" - a sentence about the club having only just started,
    /// dressed up as a run. The same judgement
    /// CommentaryRaceFacts.LeastRacesForField makes about a field.
    /// </summary>
    public const int LeastDaysOnFile = 4;

    /// <summary>
    /// How many OTHER drivers have to have raced across the days in the run
    /// before beating them is a run.
    ///
    /// Four, so a club with four drivers in total gets nothing cleanly:
    /// three rivals is not a field anybody had to be stopped by, and "can
    /// anybody stop that run" asked of three people is the superlative
    /// with nothing behind it that every threshold in this layer exists to
    /// refuse.
    /// </summary>
    public const int LeastRivals = 4;

    /// <summary>
    /// How many races the subject has to have been out in tonight before
    /// "still unbeaten" is worth a sentence.
    ///
    /// Two. One race won is not the night testing a run, and the thread
    /// said again after it would be the cold open's sentence a second time
    /// with a count on the end. A CONTRADICTION needs no such bar - being
    /// beaten is news the first time it happens, which is why only this
    /// half of ThreadSoFar waits.
    /// </summary>
    public const int LeastRacesTested = 2;

    /// <summary>
    /// The thread set at the TOP of the night, before a wheel has turned -
    /// the storyline the evening is offered as.
    ///
    /// Nothing has run tonight, so there is nothing of tonight's in it:
    /// what makes a thread possible at the cold open is the club's last
    /// few race days, which is exactly the material that makes "can
    /// anybody stop him tonight" a real question rather than a
    /// decoration.
    ///
    /// NO THEME, deliberately. At the cold open the thread IS the segment
    /// rather than colour on it, and the one fact a segment is about must
    /// never be held back for variety's sake - see FactBlock.Theme. Only
    /// <see cref="ThreadSoFar"/> rides along in somebody else's segment,
    /// and only it carries a theme.
    /// </summary>
    /// <param name="story">The night's shape as the club's results have it.</param>
    /// <param name="dayKey">Tonight, so last week's thread does not use this one up.</param>
    public static FactBlock? ThreadAhead(NightThread story, string dayKey)
    {
        if (NoThread(story) || Said(story.Name) is not { } name)
        {
            return null;
        }

        return new FactBlock(
            Stage(story, dayKey, "open"),
            $"{name} has won the last race of the night at each of the club's last {Spell(story.DaysWon)} race days."
            + " Can anybody stop that run tonight?");
    }

    /// <summary>
    /// The thread as it stands PARTWAY through the night, for a segment
    /// that is mostly about something else - the race intro.
    ///
    /// THIS IS THE ONE THAT CAN BE WRONG-FOOTED, AND THE WHOLE POINT OF IT
    /// IS THAT IT NOTICES. The night is asked again every time, so there
    /// are only two answers it can give:
    ///
    ///   HOLDING - the subject has been out and has won everything they
    ///   started. The run is referred to and the count goes up.
    ///
    ///   TURNED - somebody has beaten them. The run goes into the past
    ///   tense and the line says who did it. This is not a failure of the
    ///   thread; it is the thread paying off early, and it is better
    ///   broadcast than the version that held.
    ///
    /// What it will never do is ask "can anybody stop him" after somebody
    /// has, because that sentence only exists in <see cref="ThreadAhead"/>
    /// and the cold open happens once.
    ///
    /// AND IT CAN SAY NOTHING. Before the subject has been out, and on the
    /// odd night where they were in a race nobody won, there is no honest
    /// line and this returns null - which is the answer the rest of this
    /// layer gives far more often than not.
    ///
    /// THEMED ON THE SUBJECT, which is the opposite case from the venue's
    /// record. Here the thread is colour inside somebody else's segment,
    /// so cooling everything else about that driver for a segment or two
    /// is exactly right: the next line should not also be about Dave. See
    /// FactTheme's class summary for why it is the driver's own tag and not
    /// a thread tag of its own.
    /// </summary>
    /// <param name="story">The night's shape, read again - see the remarks.</param>
    /// <param name="dayKey">Tonight.</param>
    public static FactBlock? ThreadSoFar(NightThread story, string dayKey)
    {
        if (NoThread(story) || Said(story.Name) is not { } name)
        {
            return null;
        }

        var run = $"the last race of the night at the club's last {Spell(story.DaysWon)} race days";

        if (story.BeatenBy is not null)
        {
            // Null and only null is unbeaten, so an empty name here is a
            // result saved without one rather than a night that went to
            // plan. The correction is the part that matters and it is made
            // either way; only the credit is dropped.
            var beat = CommentaryFacts.Clean(story.BeatenBy);

            return new FactBlock(
                Stage(story, dayKey, "turned"),
                beat.Length > 0
                    ? $"{name} won {run}, but {beat} has already beaten them tonight."
                    : $"{name} won {run}, but has already been beaten tonight.",
                FactTheme.Driver(story.SubjectId));
        }

        // Unbeaten, and the counts have to agree with that before the word
        // is used. They come apart on one real night - the subject was in a
        // race NOBODY won, so there is no finisher to credit and no run
        // broken either - and "unbeaten in three races tonight" when one of
        // them was abandoned is a claim the room watched not happen.
        if (story.StartedTonight < LeastRacesTested || story.WonTonight != story.StartedTonight)
        {
            return null;
        }

        return new FactBlock(
            Stage(story, dayKey, "won:" + story.WonTonight.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            $"{name} is unbeaten in {Spell(story.StartedTonight)} races tonight, after winning {run}.",
            FactTheme.Driver(story.SubjectId));
    }

    /// <summary>
    /// The thread RESOLVED, for the sign-off - the one line the room has
    /// been waiting for since the cold open.
    ///
    /// Only once the night is actually over (NightThread.NightOver): a run
    /// declared kept or ended with the A-main still to come is the same
    /// fault as the thread the night contradicted, and the room is
    /// standing there waiting for that race.
    ///
    /// KEPT IS CHECKED FIRST, and that order is the fault it avoids. A
    /// driver beaten in a heat who still won the last race of the night did
    /// not have their run ended, and "his run ended tonight" said about
    /// them is the one thing a sign-off cannot get wrong - everybody in the
    /// room watched the opposite happen forty seconds ago.
    ///
    /// AND IT RESOLVES NOTHING IT CANNOT. A subject who was never beaten
    /// and did not win the last race either - they were not in it - gets
    /// silence. There is a true sentence to be had there and every version
    /// of it says something about WHY they were not in it, which this app
    /// does not know.
    ///
    /// No theme, for ThreadAhead's reason: at the sign-off the thread is
    /// the segment.
    /// </summary>
    /// <param name="story">The night's shape as it finished.</param>
    /// <param name="dayKey">Tonight.</param>
    public static FactBlock? ThreadDone(NightThread story, string dayKey)
    {
        if (NoThread(story) || !story.NightOver || Said(story.Name) is not { } name)
        {
            return null;
        }

        if (story.WonLastTonight)
        {
            return new FactBlock(
                Stage(story, dayKey, "kept"),
                $"{name} has now won the last race of the night at {Spell(story.DaysWon + 1)} club race days in a row.");
        }

        if (story.BeatenBy is null)
        {
            return null;
        }

        var beat = CommentaryFacts.Clean(story.BeatenBy);

        return new FactBlock(
            Stage(story, dayKey, "ended"),
            beat.Length > 0
                // "Along the way" and not "beat them to it", because the
                // name here is the FIRST driver to get past them tonight,
                // who need not be whoever won the last race. Two true
                // claims joined, and the join says which is which - a
                // small model given "ended tonight - Billy beat them"
                // will hand Billy the finale.
                ? $"{name}'s run of {Spell(story.DaysWon)} club race days ended tonight; {beat} beat them along the way."
                : $"{name}'s run of {Spell(story.DaysWon)} club race days ended tonight.");
    }

    /// <summary>
    /// True when there is no run here worth an evening - the three
    /// thresholds, and a subject with no transponder behind it.
    ///
    /// Judged in the builder as well as in the reader, the way
    /// CommentaryReaders.MidMeeting's own remarks set out: the builder
    /// keeps the promise for a table test and the reader saves walking the
    /// club's whole history for a night that was never going to say
    /// anything.
    /// </summary>
    private static bool NoThread(NightThread story) =>
        string.IsNullOrWhiteSpace(story.SubjectId)
        || story.DaysWon < LeastDaysWon
        || story.DaysOnFile < LeastDaysOnFile
        || story.Rivals < LeastRivals;

    /// <summary>
    /// The subject's name as it will be spoken, or null where there is
    /// none.
    ///
    /// A thread with nobody's name in it is not a story, it is a shape -
    /// and the whole claim is about a person, so there is nothing to say
    /// without one.
    /// </summary>
    private static string? Said(string? name)
    {
        var clean = CommentaryFacts.Clean(name);

        return clean.Length == 0 ? null : clean;
    }

    /// <summary>
    /// A thread's key: tonight, the subject, and which end of the evening
    /// it is.
    ///
    /// THE SUBJECT IS IN IT so that two subjects in one night cannot use
    /// each other's line up - which happens the moment a club files an old
    /// race mid-evening and the run moves to somebody else. THE STAGE is
    /// in it because the cold open, a reference between races and the
    /// resolution are three different things to say, and only "said once"
    /// per stage is right: ThreadSoFar's holding key carries the count so
    /// the run can be referred to again as it grows, and its turned key
    /// does not, so being beaten is reported exactly once however many
    /// races follow.
    /// </summary>
    private static string Stage(NightThread story, string dayKey, string stage) =>
        $"thread:{dayKey}:{story.SubjectId.Trim()}:{stage}";

    /// <summary>
    /// "two", "three" - small counts read as words, because these lines
    /// are written to be spoken rather than shown.
    ///
    /// CommentaryHistoryFacts' own, not a second copy: two spellings of
    /// the same count in one broadcast is the sort of difference nobody
    /// notices in review and everybody hears on the night.
    /// </summary>
    private static string Spell(int count) => CommentaryHistoryFacts.Spell(count);

    /// <summary>"one class", "six classes" - the number and its noun, agreeing.</summary>
    private static string Count(int howMany, string one, string many) =>
        howMany == 1 ? $"1 {one}" : $"{howMany} {many}";
}
