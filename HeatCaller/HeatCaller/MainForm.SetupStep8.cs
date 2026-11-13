namespace HeatCaller;

/// <summary>
/// The club's marshal rule, from the laptop's side (12 Nov 2026, setup plan
/// step 8). Race Control's Someone's missing... logs a no-show; this keeps
/// it on the round with the driver's transponder and the rule in force, so
/// QualifyingCalculator takes their best qualifier away (MarshalRule.ForfeitsFor)
/// or their next saved race carries the time penalty (RaceFilingService).
/// Taking the no-show back off - the same menu line, or Undo - puts it all
/// back, the time penalty included.
/// </summary>
public partial class MainForm
{
    /// <summary>A marshal didn't turn up: saved on the round, under the club's rule as it stands.</summary>
    private void LogMarshalNoShow(RoundDefinition round, string who, string transponder) =>
        LogMarshalNoShow(round, who, transponder, withUndo: true);

    /// <summary>
    /// The same, without an undo step when it is itself the undo of taking
    /// one off (bug hunt 17): a step pushed from inside an undo was what the
    /// next Ctrl+Z, or the rest of a several-step undo, then undid - taking
    /// the no-show straight back off instead of the older change.
    /// </summary>
    private void LogMarshalNoShow(RoundDefinition round, string who, string transponder, bool withUndo)
    {
        if (_rounds.Get(round.Id) is not { } saved)
        {
            return;
        }

        saved.MarshalNoShows ??= new List<string>();

        if (saved.MarshalNoShows.Contains(who, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        saved.MarshalNoShows.Add(who);

        MarshalNoShow? noShow = null;

        if (!string.IsNullOrWhiteSpace(transponder))
        {
            noShow = MarshalRule.Log(who, transponder, NoShowClassOf(saved, transponder),
                _settings.MarshalNoShowRule, _settings.MarshalNoShowPenaltySeconds, DateTime.UtcNow);
            saved.NoShowMarshals ??= new List<MarshalNoShow>();
            saved.NoShowMarshals.Add(noShow);
        }

        _rounds.Update(saved);

        OnClientRawMessage($"{AppTime.LogStampNow()}  [marshals] {saved.RoundLabel}: {who} didn't marshal"
                           + (noShow is null ? "." : $" - {MarshalRule.Consequence(noShow)}."));

        if (withUndo)
        {
            var roundId = saved.Id;
            _undo.Capture($"Marshal no-show: {who}", () => TakeMarshalNoShowBack(roundId, who, withUndo: false));
        }

        _raceProgram?.RefreshAll();
    }

    /// <summary>
    /// Takes a no-show back off its round, and its time penalty back off the
    /// race it went on, when it had gone on one.
    /// </summary>
    private void TakeMarshalNoShowBack(string roundId, string who, bool withUndo)
    {
        if (_rounds.Get(roundId) is not { } saved)
        {
            return;
        }

        var removedText = saved.MarshalNoShows?.RemoveAll(w => string.Equals(w, who, StringComparison.OrdinalIgnoreCase)) ?? 0;
        var noShow = saved.NoShowMarshals?.FirstOrDefault(n => string.Equals(n.Who, who, StringComparison.OrdinalIgnoreCase));

        if (removedText == 0 && noShow is null)
        {
            return;
        }

        if (noShow is not null)
        {
            saved.NoShowMarshals!.Remove(noShow);

            if (noShow.AppliedToSessionId is { } sessionId && _sessionHistory.Get(sessionId) is { } race
                && MarshalRule.TakePenaltyOff(race, saved, noShow))
            {
                _sessionHistory.SaveEdited(race);
                OnClientRawMessage($"{AppTime.LogStampNow()}  [marshals] {noShow.PenaltySeconds}s taken back off {who}'s race ({race.Label}).");
            }
        }

        if (saved.MarshalNoShows is { Count: 0 })
        {
            saved.MarshalNoShows = null;
        }

        if (saved.NoShowMarshals is { Count: 0 })
        {
            saved.NoShowMarshals = null;
        }

        _rounds.Update(saved);
        OnClientRawMessage($"{AppTime.LogStampNow()}  [marshals] {saved.RoundLabel}: the no-show for {who} is off.");

        if (withUndo && noShow is not null)
        {
            // Logging it again is the undo - the race it went on gets it back
            // when it is the next race saved, or by hand in Race History.
            var transponder = noShow.Transponder;
            _undo.Capture($"Take off the marshal no-show for {who}", () =>
            {
                if (_rounds.Get(roundId) is { } round)
                {
                    LogMarshalNoShow(round, who, transponder, withUndo: false);
                }
            });
        }

        _raceProgram?.RefreshAll();
    }

    /// <summary>
    /// Which class's qualifying a no-show costs: the class the driver last
    /// raced in tonight before the round they should have marshalled (the
    /// heat before marshals - MarshalAssigner), else their class in that
    /// round, else the round's own.
    ///
    /// A driver who has not qualified yet tonight and is not of the round's
    /// class - a Mod driver covering a post on a Stock heat before Mod's
    /// first qualifier - was filed under the round's class, where they have
    /// no result to lose, so the rule never reached them (bug hunt 17). They
    /// go under a class they qualify in tonight, or failing that their
    /// profile's, unless the round's class is one of those anyway.
    /// </summary>
    private string NoShowClassOf(RoundDefinition marshalled, string transponder) =>
        NoShowClassOf(marshalled, transponder, _rounds.All, _raceEvents.ActiveRoundScope(), _profiles);

    internal static string NoShowClassOf(RoundDefinition marshalled, string transponder,
        IEnumerable<RoundDefinition> rounds, IReadOnlySet<string>? scope, ProfileStore? profiles)
    {
        var qualifiers = rounds
            .Where(r => r.Kind == RoundKind.Qualifier
                        && (scope is null || scope.Contains(r.Id))
                        && r.AssignedTransponders.Contains(transponder, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (qualifiers.LastOrDefault(r => r.Order < marshalled.Order) is { } lastRaced)
        {
            return MixedHeats.ClassOfDriver(lastRaced, transponder, profiles);
        }

        var here = MixedHeats.ClassOfDriver(marshalled, transponder, profiles);
        bool IsHere(string? name) => string.Equals(name?.Trim(), here?.Trim(), StringComparison.OrdinalIgnoreCase);

        var tonight = qualifiers.Select(r => MixedHeats.ClassOfDriver(r, transponder, profiles)).ToList();

        if (tonight.Count > 0)
        {
            return tonight.Any(IsHere) ? here : tonight[0];
        }

        if (profiles?.Get(transponder) is { } profile)
        {
            var theirs = (profile.Classes ?? new List<string>()).Prepend(profile.ClassName)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .ToList();

            if (theirs.Count > 0 && !theirs.Any(IsHere))
            {
                return theirs[0];
            }
        }

        return here;
    }
}
