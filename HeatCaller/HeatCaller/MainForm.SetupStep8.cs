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
    private void LogMarshalNoShow(RoundDefinition round, string who, string transponder)
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

        var roundId = saved.Id;
        _undo.Capture($"Marshal no-show: {who}", () => TakeMarshalNoShowBack(roundId, who, withUndo: false));

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
                    LogMarshalNoShow(round, who, transponder);
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
    /// </summary>
    private string NoShowClassOf(RoundDefinition marshalled, string transponder)
    {
        var scope = _raceEvents.ActiveRoundScope();

        var lastRaced = _rounds.All
            .Where(r => r.Order < marshalled.Order
                        && r.Kind == RoundKind.Qualifier
                        && (scope is null || scope.Contains(r.Id))
                        && r.AssignedTransponders.Contains(transponder, StringComparer.OrdinalIgnoreCase))
            .LastOrDefault();

        return lastRaced is not null
            ? MixedHeats.ClassOfDriver(lastRaced, transponder, _profiles)
            : MixedHeats.ClassOfDriver(marshalled, transponder, _profiles);
    }
}
