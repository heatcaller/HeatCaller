namespace HeatCaller;

/// <summary>
/// The protest window everywhere (11 Nov 2026). Once a class's last
/// qualifier is in, its results are provisional for the club's window
/// (AppSettings.ProtestMinutes, set on the Club Website page):
///
/// - the Schedule page's qualifying card and a driver's own qualifying line
///   say "Provisional until 20:42" (BuildQualifyingDocument);
/// - each of that class's heats on a driver's night carries the moment the
///   window closes, and the phone takes "Something wrong?" away then;
/// - the announcer says once that results are provisional and queries go
///   to the desk.
///
/// Mains, and a class still qualifying, have no window: their phones are as
/// they were. All of it reads ProtestWindow, where it is tested.
/// </summary>
public partial class MainForm
{
    /// <summary>The classes whose window has been announced, by race day and class - so it is said once.</summary>
    private readonly HashSet<string> _protestWindowsSaid = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A result has just been filed: when it was a class's last qualifier,
    /// say the window has opened and send every driver of that class's
    /// qualifiers their night again, so their heats carry the closing time.
    /// Never throws into the filing path.
    /// </summary>
    private void ProtestWindowAfterFiling(SessionRecord record)
    {
        try
        {
            if (record.IsNotScored()
                || record.RoundId is not { Length: > 0 } roundId
                || _rounds.Get(roundId) is not { Kind: RoundKind.Qualifier } round
                || _raceProgram is null
                || _raceEvents.Active is not { IsLegacy: false } day)
            {
                return;
            }

            foreach (var className in MixedHeats.ClassesOf(round))
            {
                if (_raceProgram.ProtestClosesAtUtc(className, record) is null
                    || !_protestWindowsSaid.Add($"{day.Id}|{className}"))
                {
                    continue;
                }

                var minutes = _raceProgram.ProtestMinutesTonight;
                var line = ProtestWindow.Announcement(className, minutes);
                _announcer.SpeakReminder(line, showOnScreens: true, onAir: "Notice");
                OnClientRawMessage($"[program] {className}: qualifying is in - results are provisional for {Plural.Of(minutes, "minute")}.");

                // After this filing has linked the round, so the nights see it.
                var cls = className;
                BeginInvoke(() => PublishNightsFor(DriversInClassQualifiers(cls)));
            }
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[program] The protest window could not be worked out: {ex.Message}");
        }
    }

    /// <summary>Everybody in tonight's qualifiers of a class.</summary>
    private IEnumerable<string> DriversInClassQualifiers(string className)
    {
        var scope = _raceEvents.ActiveRoundScope();

        return _rounds.All
            .Where(r => r.Kind == RoundKind.Qualifier && r.IsRecorded && (scope is null || scope.Contains(r.Id)) && MixedHeats.RunsClass(r, className))
            .Select(r => _sessionHistory.Get(r.LinkedSessionId!))
            .OfType<SessionRecord>()
            .SelectMany(DriversIn)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// A night's races with each qualifier's query deadline on - see
    /// NightRace.QueryUntil. Unchanged with queries off.
    /// </summary>
    private IReadOnlyList<NightRace> WithQueryWindows(string who, IReadOnlyList<NightRace> races)
    {
        if (!_settings.RelayResultQueries || _raceProgram is null || races.Count == 0)
        {
            return races;
        }

        try
        {
            return races.Select(race =>
            {
                if (race.Kind is not null
                    || _sessionHistory.Get(race.Sid) is not { RoundId: { Length: > 0 } roundId }
                    || _rounds.Get(roundId) is not { Kind: RoundKind.Qualifier } round
                    || _raceProgram.ProtestClosesAtUtc(MixedHeats.ClassOfDriver(round, who, _profiles)) is not { } closes)
                {
                    return race;
                }

                return race with { QueryUntil = UnixMs(closes) };
            }).ToList();
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[relay] A driver's night went without its protest windows: {ex.Message}");
            return races;
        }
    }

    /// <summary>
    /// The qualifying card's "Provisional until 20:42" while the class's
    /// window is open, as (words, Unix ms it closes) - else nulls.
    /// </summary>
    private (string? Words, long? Until) ProvisionalFor(string className)
    {
        if (_raceProgram?.ProtestClosesAtUtc(className) is { } closes && ProtestWindow.IsProvisional(closes, DateTime.UtcNow))
        {
            return (ProtestWindow.PhoneWords(closes), UnixMs(closes));
        }

        return (null, null);
    }

    private static long UnixMs(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
}
