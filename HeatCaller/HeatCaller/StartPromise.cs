namespace HeatCaller;

/// <summary>
/// NEVER MORE THAN 2 MINUTES EARLY (12 Nov 2026, setup plan step 8 - the open
/// question in Part 4 of plans/race-night-setup-plan.md, "what does an
/// 'about' time promise?").
///
/// WHY: drivers read "about 20:42" off their phone or the heat sheet and go
/// back to the pits to work on their cars. A night running quick used to
/// start their heat at 20:35 by itself, and somebody missed their race. The
/// rule the director's review suggested, and this keeps:
///
///  - When the heats are posted - or, on a night that doesn't post, when
///    the first race goes out - each heat's estimated start is remembered on
///    the round (RoundDefinition.PromisedStartUtc). That is the promise.
///  - Auto start never starts a race more than <see cref="Early"/> before
///    its promise, even when the night is early: the count waits ("waiting
///    for its time", NEXT RACE AT 20:40 on the Big Screen). Start now still
///    starts it, because pressing it says the director has looked. A club
///    setting (AppSettings.AutoStartNeverEarly), on by default.
///  - Later estimates show the promise when the night is early, so a phone
///    never says 20:35 for a heat that will wait until 20:40 - see
///    <see cref="Shown"/> and NightPlan.Projection.ShownStartOf.
///
/// A late night is a different matter: a promise is "not before", never
/// "not after". Pure, so it is tested without a window.
/// </summary>
public static class StartPromise
{
    /// <summary>The most a race may start before the time drivers were given.</summary>
    public static readonly TimeSpan Early = TimeSpan.FromMinutes(2);

    /// <summary>A start rounded to the minute, as "about 20:42" shows it - so the promise is exactly what drivers read.</summary>
    public static DateTime Rounded(DateTime utc) =>
        new((utc.Ticks + TimeSpan.TicksPerMinute / 2) / TimeSpan.TicksPerMinute * TimeSpan.TicksPerMinute, DateTimeKind.Utc);

    /// <summary>
    /// Remembers each race's estimated start as its promise. Practice and
    /// races already run are left alone, and so is any race with no
    /// estimate. Returns the rounds changed, so the caller saves just those.
    /// </summary>
    /// <param name="dayRounds">Tonight's program.</param>
    /// <param name="startOf">A round id to its estimated start, or null (NightPlan.Projection.StartOf).</param>
    /// <param name="overwrite">True at posting, which promises afresh; false keeps a promise already made.</param>
    /// <param name="skipRoundId">A race not to promise - the one going out now.</param>
    public static List<RoundDefinition> Stamp(IEnumerable<RoundDefinition> dayRounds, Func<string, DateTime?> startOf, bool overwrite, string? skipRoundId = null)
    {
        var changed = new List<RoundDefinition>();

        foreach (var round in dayRounds)
        {
            if (round is null || round.IsRecorded || round.Kind == RoundKind.Practice
                || string.Equals(round.Id, skipRoundId, StringComparison.Ordinal)
                || (!overwrite && round.PromisedStartUtc is not null)
                || startOf(round.Id) is not { } start)
            {
                continue;
            }

            var promise = Rounded(start);

            if (round.PromisedStartUtc != promise)
            {
                round.PromisedStartUtc = promise;
                changed.Add(round);
            }
        }

        return changed;
    }

    /// <summary>
    /// Whether tonight's starts still need promising when a race goes out:
    /// on a night that doesn't post its heats, the first race - nothing run
    /// yet and nothing promised yet.
    /// </summary>
    public static bool OwedAtFirstRace(RaceEvent? day, IReadOnlyList<RoundDefinition> dayRounds) =>
        day is { IsLegacy: false, HeatsPostedUtc: null }
        && !dayRounds.Any(r => r is not null && r.Kind != RoundKind.Practice && (r.IsRecorded || r.PromisedStartUtc is not null));

    /// <summary>
    /// The earliest Auto start may start the race: its promise less
    /// <see cref="Early"/>. Null with the setting off or no promise.
    /// </summary>
    public static DateTime? NotBefore(RoundDefinition? round, bool neverEarly) =>
        neverEarly && round?.PromisedStartUtc is { } promise ? promise - Early : null;

    /// <summary>The start to show for a race: the estimate, or the promise when the night is running early.</summary>
    public static DateTime Shown(DateTime estimateUtc, DateTime? promisedUtc) =>
        promisedUtc is { } promise && promise > estimateUtc ? promise : estimateUtc;
}
