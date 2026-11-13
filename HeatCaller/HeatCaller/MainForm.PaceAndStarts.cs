namespace HeatCaller;

/// <summary>
/// Night pace and start times (12 Nov 2026, setup plan step 8). This file
/// connects the pieces that need the whole app:
///
/// - doors open: the first check-in of the night is stamped on the race day
///   (RaceEvent.DoorsOpenUtc), for the race director's "doors open to first
///   green" (NightMeasures);
/// - the gap's log: what Race Control's desk did between races (Hold, +1
///   min, Start now, a driver's five minutes, a wait for the promised time)
///   and when each race was called up, kept on the round for NightPace;
/// - the promised starts on a night that doesn't post its heats, made when
///   the first race stages (StartPromise);
/// - the estimated starts for the Big Screen's up-next line - the phones'
///   own, so the room and the pocket agree.
/// </summary>
public partial class MainForm
{
    private void WirePaceAndStarts(RaceProgramControl raceProgram, RaceControlControl raceControl)
    {
        // Doors open. Any route a check-in comes by - the desk, a phone, the
        // relay's sign-up desk - so back onto the UI thread first.
        _checkInStore.CheckedIn += eventId => OnUiThread(() =>
        {
            if (_raceEvents.Get(eventId) is { } day && NightMeasures.StampDoorsOpen(day, DateTime.UtcNow))
            {
                _raceEvents.Upsert(day);
                OnClientRawMessage($"{AppTime.LogStampNow()}  [program] Doors open: the first check-in of {day.DisplayName}.");
            }
        });

        raceControl.GapNoted += (roundId, note) =>
        {
            if (_rounds.Get(roundId) is not { } round)
            {
                return;
            }

            round.GapNotes ??= new List<GapNote>();
            round.GapNotes.Add(note);
            _rounds.Update(round);
        };

        raceControl.RaceCalledUp += roundId =>
        {
            if (_rounds.Get(roundId) is not { } round)
            {
                return;
            }

            // The first call-up of THIS gap: one from an earlier gap (the
            // order changed and it didn't run then) is replaced.
            if (round.CalledAtUtc is not { } called || (RollCall.LastFinishUtc is { } finish && called < finish))
            {
                round.CalledAtUtc = DateTime.UtcNow;
                _rounds.Update(round);
            }
        };

        // The phones' estimates, cached there, for the Big Screen.
        raceControl.EstimatedStart = roundId => EstimatedStartsForPhones()(roundId);

        // The next race when Race Control will really start it (12 Nov 2026):
        // an approved "Need 5 minutes", +1 min or Hold moves every "about"
        // time after it - see RaceControlControl.NextStart. A move clears the
        // cached estimates so the phones and Big Screen get them at once.
        raceProgram.NextStart = () => raceControl.NextStartForPlan;
        raceControl.NextStartMoved += () => _phoneEstimates = null;

        // A night that doesn't post its heats gives its times at the first race.
        _race.StateChanged += state =>
        {
            if (state is RaceState.Staging or RaceState.Countdown && !_race.IsPracticeMode && !_race.IsDemoMode)
            {
                OnUiThread(() => raceProgram.PromiseStartsAtFirstRace(_activeRound?.Id));
            }
        };
    }
}
