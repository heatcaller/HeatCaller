namespace HeatCaller;

/// <summary>"A marshal who doesn't turn up:" - the club's rule (12 Nov 2026, setup plan step 8). See MarshalRule.</summary>
public enum MarshalNoShowRule
{
    /// <summary>Logged for the record, nothing more - every no-show before this.</summary>
    Nothing,

    /// <summary>The driver loses their best counted qualifying result tonight in that class (the default).</summary>
    LosesBestQualifier,

    /// <summary>A time penalty, added to their next race when it is saved.</summary>
    TimePenalty,
}

/// <summary>
/// One marshal no-show (12 Nov 2026, setup plan step 8), kept on the round
/// they should have marshalled (RoundDefinition.NoShowMarshals) beside the
/// "Name (Post)" text the event report has listed since D4.
///
/// The rule in force when it was logged is kept with it, so changing the
/// club's rule later does not rewrite a ruling already made, and the
/// transponder is kept so the rule can find the driver's results.
/// </summary>
public sealed class MarshalNoShow
{
    /// <summary>"Hugh Turn (Turn 3)" - as the banner and MarshalNoShows name them.</summary>
    public string Who { get; set; } = string.Empty;

    public string Transponder { get; set; } = string.Empty;

    /// <summary>The class whose qualifying the rule takes a result from - the driver's class in the round they marshalled, else their own.</summary>
    public string ClassName { get; set; } = string.Empty;

    public MarshalNoShowRule Rule { get; set; } = MarshalNoShowRule.LosesBestQualifier;

    /// <summary>The time penalty under <see cref="MarshalNoShowRule.TimePenalty"/>, in seconds.</summary>
    public int PenaltySeconds { get; set; }

    /// <summary>The saved race the time penalty went on, once it has - null until their next race is saved.</summary>
    public string? AppliedToSessionId { get; set; }

    public DateTime LoggedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// The club's marshal rule, applied (12 Nov 2026, setup plan step 8).
///
/// WHY: missing turn marshals hold the night up more than missing drivers,
/// and D4 only logged them. Most clubs have a rule - you lose your best
/// qualifier, or take a time penalty - which the desk had to apply by hand,
/// from memory, at the end of the night, and usually didn't. Now the rule
/// chosen on Setup is applied as the no-show is logged: the qualifying
/// ranking drops the driver's best counted result (QualifyingCalculator
/// reads <see cref="ForfeitsFor"/>, so the standings, the mains, the phones
/// and the event report all agree), or their next saved race carries the
/// penalty as a correction with its reason (Race History shows it). Taking
/// the no-show back off undoes either.
/// </summary>
public static class MarshalRule
{
    public const int DefaultPenaltySeconds = 10;

    /// <summary>The longest penalty Setup offers - a minute is already a ruling, not a nudge.</summary>
    public const int MostPenaltySeconds = 120;

    /// <summary>What the Setup picker shows for each rule.</summary>
    public static string Words(MarshalNoShowRule rule) => rule switch
    {
        MarshalNoShowRule.Nothing => "Nothing",
        MarshalNoShowRule.TimePenalty => "Time penalty in their next race",
        _ => "Loses their best qualifying result",
    };

    /// <summary>What the rule does, in a line - under the rule on Setup's Club rules page.</summary>
    public static string Effect(MarshalNoShowRule rule) => rule switch
    {
        MarshalNoShowRule.Nothing => "The no-show is logged for the record, and nothing more.",
        MarshalNoShowRule.TimePenalty => "The seconds go on the driver's next race when it is saved.",
        _ => "The driver's best qualifying result tonight is taken from their class.",
    };

    /// <summary>A new no-show under the club's rule as it stands.</summary>
    public static MarshalNoShow Log(string who, string transponder, string className, MarshalNoShowRule rule, int penaltySeconds, DateTime nowUtc) => new()
    {
        Who = who,
        Transponder = transponder,
        ClassName = className,
        Rule = rule,
        PenaltySeconds = rule == MarshalNoShowRule.TimePenalty ? Math.Clamp(penaltySeconds, 1, MostPenaltySeconds) : 0,
        LoggedUtc = nowUtc,
    };

    /// <summary>
    /// The reason shown beside the ruling: "marshal no-show, Q3 Heat 2 (Turn 1)".
    /// </summary>
    public static string Reason(RoundDefinition round, MarshalNoShow noShow)
    {
        var post = noShow.Who.IndexOf('(') is var open and >= 0 && noShow.Who.EndsWith(')')
            ? " " + noShow.Who[open..]
            : string.Empty;

        return $"marshal no-show, {round.RoundLabel}{post}";
    }

    /// <summary>What Race Control's log says the rule did: "Q3 Heat 2: Hugh Turn didn't marshal - loses their best Stock qualifier."</summary>
    public static string Consequence(MarshalNoShow noShow) => noShow.Rule switch
    {
        MarshalNoShowRule.LosesBestQualifier => string.IsNullOrWhiteSpace(noShow.ClassName)
            ? "loses their best qualifying result tonight"
            : $"loses their best {noShow.ClassName} qualifying result tonight",
        MarshalNoShowRule.TimePenalty => $"{noShow.PenaltySeconds} seconds on their next race",
        _ => "logged (the club's rule is Nothing)",
    };

    /// <summary>
    /// The drivers who lose their best qualifier in <paramref name="className"/>,
    /// with why - by transponder, case-blind. Only no-shows on rounds inside
    /// <paramref name="roundFilter"/> count when one is given (tonight's), so
    /// last week's no-show never costs this week's result.
    /// </summary>
    public static Dictionary<string, string> ForfeitsFor(string className, IEnumerable<RoundDefinition> rounds, IReadOnlyCollection<string>? roundFilter)
    {
        var forfeits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var round in rounds)
        {
            if (round.NoShowMarshals is not { Count: > 0 } noShows || (roundFilter is not null && !roundFilter.Contains(round.Id)))
            {
                continue;
            }

            foreach (var noShow in noShows)
            {
                if (noShow.Rule == MarshalNoShowRule.LosesBestQualifier
                    && !string.IsNullOrWhiteSpace(noShow.Transponder)
                    && string.Equals(noShow.ClassName, className, StringComparison.OrdinalIgnoreCase))
                {
                    // One lost result a night, however many posts they missed:
                    // the rule is "your best qualifier", not one per miss.
                    forfeits.TryAdd(noShow.Transponder, Reason(round, noShow));
                }
            }
        }

        return forfeits;
    }

    /// <summary>
    /// The time penalties a race about to be saved should carry: every
    /// no-show under the time-penalty rule, not yet applied, of a driver in
    /// <paramref name="results"/>, logged on a round other than this race's
    /// own and inside <paramref name="roundFilter"/> - their NEXT race. In
    /// the order they were logged.
    /// </summary>
    public static List<(RoundDefinition Round, MarshalNoShow NoShow)> PendingPenalties(
        IEnumerable<SessionResultEntry> results, string? thisRoundId, IEnumerable<RoundDefinition> rounds, IReadOnlyCollection<string>? roundFilter)
    {
        var racing = results.Select(r => r.Transponder).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return rounds
            .Where(r => r.NoShowMarshals is { Count: > 0 }
                        && !string.Equals(r.Id, thisRoundId, StringComparison.Ordinal)
                        && (roundFilter is null || roundFilter.Contains(r.Id)))
            .SelectMany(r => r.NoShowMarshals!.Select(n => (Round: r, NoShow: n)))
            .Where(x => x.NoShow.Rule == MarshalNoShowRule.TimePenalty
                        && x.NoShow.AppliedToSessionId is null
                        && racing.Contains(x.NoShow.Transponder))
            .OrderBy(x => x.NoShow.LoggedUtc)
            .ToList();
    }

    /// <summary>
    /// Puts each pending penalty on the race about to be saved, as a result
    /// correction with its reason (so Race History shows it and it can be
    /// taken off like any other), and marks the no-show applied. Returns a
    /// line per penalty for the log. The caller saves the rounds.
    /// </summary>
    public static List<string> ApplyPenalties(SessionRecord record, IReadOnlyList<(RoundDefinition Round, MarshalNoShow NoShow)> pending)
    {
        var lines = new List<string>();

        foreach (var (round, noShow) in pending)
        {
            var entry = record.Results.FirstOrDefault(r => string.Equals(r.Transponder, noShow.Transponder, StringComparison.OrdinalIgnoreCase));

            if (entry is null)
            {
                continue;
            }

            var now = ResultEditor.CurrentOf(entry);
            var adjustment = now with { TimePenaltyMs = Math.Min(ResultEditor.MaxTimePenaltyMs, now.TimePenaltyMs + noShow.PenaltySeconds * 1000L) };

            if (ResultEditor.Apply(record, new[] { adjustment }, Reason(round, noShow), "Marshal rule") > 0)
            {
                noShow.AppliedToSessionId = record.Id;
                lines.Add($"[marshals] {noShow.Who}: +{noShow.PenaltySeconds}s on this race ({Reason(round, noShow)}).");
            }
        }

        return lines;
    }

    /// <summary>
    /// Takes an applied time penalty back off its race - the undo of a
    /// no-show logged by mistake. True when the race changed; the caller
    /// saves it (SessionHistoryStore.SaveEdited).
    /// </summary>
    public static bool TakePenaltyOff(SessionRecord record, RoundDefinition round, MarshalNoShow noShow)
    {
        var entry = record.Results.FirstOrDefault(r => string.Equals(r.Transponder, noShow.Transponder, StringComparison.OrdinalIgnoreCase));

        if (entry is null || noShow.PenaltySeconds <= 0)
        {
            return false;
        }

        var now = ResultEditor.CurrentOf(entry);
        var adjustment = now with { TimePenaltyMs = Math.Max(0, now.TimePenaltyMs - noShow.PenaltySeconds * 1000L) };

        return ResultEditor.Apply(record, new[] { adjustment }, "No-show taken off: " + Reason(round, noShow), "Marshal rule") > 0;
    }
}
