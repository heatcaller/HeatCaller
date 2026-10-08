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
            found.Add(new FactBlock($"signoff:{dayKey}:fastest",
                $"The fastest lap of the night went to {fastest}: {night.FastestLapSeconds:0.000}s."));
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
                $"The closest finish all night was {closest}'s, by {night.ClosestFinishSeconds:0.00}s."));
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

    /// <summary>"one class", "six classes" - the number and its noun, agreeing.</summary>
    private static string Count(int howMany, string one, string many) =>
        howMany == 1 ? $"1 {one}" : $"{howMany} {many}";
}
