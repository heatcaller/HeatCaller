namespace HeatCaller;

/// <summary>
/// What the announcer says after a Pole Shootout run or an Invitational
/// main (19 Nov 2026, race-night build plan row 14b) - short, because the
/// next car is already on its way out: the run's time and where it puts the
/// driver, the pole once the last run is in, and after an Invitational main
/// who leads (or who has won) on points. The grids themselves are set by
/// the programme once the race is filed (RaceProgramControl.KeepFinalsFormatsInStep).
/// </summary>
public sealed partial class RaceControlControl
{
    /// <summary>What is said for a shootout run or an Invitational main, and the recap's fact line.</summary>
    /// <param name="RunLine">The run's own read, said where the race's result would be - only with the flag result read on.</param>
    /// <param name="Decided">The pole, or the Invitational standings - said whatever the flag result setting, as the A-Main overall is.</param>
    /// <param name="Fact">For the recap, or null.</param>
    private sealed record FinalsFormatRead(string? RunLine, string? Decided, string? Fact);

    /// <summary>
    /// The read for <paramref name="round"/> just raced, or null when it is
    /// neither a shootout run nor an Invitational main. Asked before the
    /// round is linked to <paramref name="record"/>, so the record is handed in.
    /// </summary>
    private FinalsFormatRead? FinalsFormatAtTheFlag(SessionRecord record, RoundDefinition round)
    {
        if (_rounds is null)
        {
            return null;
        }

        var scope = _events?.ActiveRoundScope();
        var tonight = _rounds.All.Where(r => scope is null || scope.Contains(r.Id)).ToList();
        var raceClass = _raceClasses.All.FirstOrDefault(c => string.Equals(c.Name, round.ClassName, StringComparison.OrdinalIgnoreCase));

        string Spoken(string transponder) => RelayDocuments.PublicNameOrUnnamed(SpokenDriverName(transponder), transponder);

        if (round.Kind == RoundKind.Shootout)
        {
            var runs = PoleShootout.RunsOf(tonight.Append(round), round.ClassName);
            var standing = PoleShootout.Standings(runs, _sessionHistory.Get, PoleShootout.TimedLapsFor(raceClass), _profiles.PrimaryForAlternate, (round, record));

            var driver = _profiles.PrimaryForAlternate(round.AssignedTransponders.FirstOrDefault() ?? string.Empty)
                         ?? round.AssignedTransponders.FirstOrDefault() ?? string.Empty;

            var runLine = PoleShootout.RunCall(Spoken(driver), standing, driver);

            if (!standing.Complete || standing.Pole is not { } pole)
            {
                return new FinalsFormatRead(runLine, null, null);
            }

            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [program] {PoleShootout.Name} pole: {pole.DriverName}, {PoleShootout.TimeWords(pole.TimeMs)}.");

            return new FinalsFormatRead(
                runLine,
                PoleShootout.PoleCall(Spoken(pole.Transponder), pole.TimeMs),
                PoleShootout.Fact(standing, t => Spoken(t.Transponder)));
        }

        if (round is { Kind: RoundKind.Main, InvitationalMain: > 0 })
        {
            var series = Invitational.SeriesOf(round, tonight);
            var table = Invitational.Standings(series, _sessionHistory.Get, raceClass, _profiles.PrimaryForAlternate,
                t => _profiles.Get(t)?.DriverName ?? t, (round, record));

            if (table?.Leader is null)
            {
                return null;
            }

            // WHY (23 Nov 2026, bug hunt 24): said MainsRun, so with a main skipped the log
            // read "after main 2 of 5" for main 3 - the same wrong number the call had.
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [program] {Invitational.ShortName} after main {table.MainNumber} of {table.Mains}: "
                                  + $"{table.Leader.DriverName}, {table.Leader.Words()}.");

            return new FinalsFormatRead(
                null,
                Invitational.Call(table, s => Spoken(s.Transponder)),
                Invitational.Fact(table, s => Spoken(s.Transponder)));
        }

        return null;
    }
}
