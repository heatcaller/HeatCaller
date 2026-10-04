namespace HeatCaller;

/// <summary>
/// Which round a finished race should complete when nobody picked one -
/// "Start the next race automatically". Moved out of RaceControlControl so
/// the choice can be tested.
///
/// The default scope matches the class of the drivers who were actually on
/// track. With several classes in one programme, taking the next round by
/// overall order can credit a race to the wrong class entirely - the heats
/// interleave, so "next" is rarely the right answer.
///
/// Only ever tonight's rounds, and none at all with no race day open. It
/// used to fall back to every unrun round the club had ever made once the
/// day was closed (25 Sep audit): new rounds always sort last, so a C-Main
/// left unrun when the light went last week sat at the front of the queue,
/// and the next race run without a race day open was filed against it -
/// counted in last week's meeting, missing from tonight's.
/// </summary>
internal static class RoundPicker
{
    /// <summary>The scope that takes the class of whoever was on track. The default.</summary>
    public const string ByDrivers = "drivers";

    /// <summary>The scope pinned to one class.</summary>
    public const string ByClass = "class";

    /// <param name="scope"><see cref="ByDrivers"/>, <see cref="ByClass"/>, or anything else for the next round overall.</param>
    /// <param name="pinnedClass">The class <see cref="ByClass"/> is pinned to; blank means the next round overall.</param>
    /// <param name="classesOnTrack">The classes the racers' profiles give - every class each one races - null or blank where one gives none.</param>
    /// <param name="log">Where to say why nothing was picked, when that is not obvious.</param>
    /// <param name="transpondersOnTrack">
    /// Who was on track, by main transponder. With <see cref="ByDrivers"/>, the
    /// unrecorded round whose roster holds the most of them is the answer,
    /// before any class is guessed at.
    /// </param>
    /// <returns>The round, or null - with no race day open, always null.</returns>
    public static RoundDefinition? Next(
        RoundDefinitionStore rounds,
        RaceEventStore? events,
        string scope,
        string pinnedClass,
        IEnumerable<string?> classesOnTrack,
        Action<string> log,
        IEnumerable<string>? transpondersOnTrack = null)
    {
        // A real race day or nothing. RaceFiling says so in the log when the
        // race is saved without a round.
        if (!RaceDayOpen(events))
        {
            return null;
        }

        var unrecorded = rounds.Unrecorded(events!.DayScope());

        switch (scope)
        {
            case ByClass:
            {
                if (string.IsNullOrWhiteSpace(pinnedClass))
                {
                    return unrecorded.FirstOrDefault();
                }

                var match = unrecorded.FirstOrDefault(r => string.Equals(r.ClassName, pinnedClass, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    log($"[session] The next race is pinned to class \"{pinnedClass}\", which has no unrecorded rounds left.");
                }

                return match;
            }

            case ByDrivers:
            {
                // The heat these drivers were put in, when one of tonight's
                // unrun rounds has them: the round whose roster holds the most
                // of the cars that were on track, the earliest in the
                // programme on a tie. Rosters are what a heat IS, so this
                // answers the question directly. Guessing from each driver's
                // class came second-hand: a heat run mostly by drivers added
                // to it from another class was filed against that other
                // class's next round, and the next round of the right class
                // was taken even when the drivers on track were the ones in
                // a later heat.
                var onTrack = (transpondersOnTrack ?? Enumerable.Empty<string>())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (onTrack.Count > 0)
                {
                    var theirs = unrecorded
                        .Select(r => (Round: r, Overlap: r.AssignedTransponders.Count(onTrack.Contains)))
                        .Where(x => x.Overlap > 0)
                        .OrderByDescending(x => x.Overlap)
                        .Select(x => x.Round)
                        .FirstOrDefault();

                    if (theirs is not null)
                    {
                        return theirs;
                    }
                }

                // No round has them: whichever class most of the drivers on track belong to.
                // A majority rather than the first match, so one driver with
                // a stale or missing class doesn't decide it.
                var className = classesOnTrack
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .GroupBy(n => n!, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault();

                if (className is null)
                {
                    log("[session] Couldn't tell which class was racing, so the next race could not be picked by class - none of the drivers on track "
                        + "have a class set on their profile. Falling back to the next round overall.");
                    return unrecorded.FirstOrDefault();
                }

                var match = unrecorded.FirstOrDefault(r => string.Equals(r.ClassName, className, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    log($"[session] Class \"{className}\" was racing, but it has no unrecorded rounds left.");
                }

                return match;
            }

            default:
                return unrecorded.FirstOrDefault();
        }
    }

    /// <summary>A real race day is open - not the legacy holder, not nothing.</summary>
    public static bool RaceDayOpen(RaceEventStore? events) => events?.Active is { IsLegacy: false };
}
