namespace HeatCaller;

/// <summary>
/// The protest window everywhere (11 Nov 2026). Once a round's results are
/// in, they are provisional for the club's window (AppSettings.ProtestMinutes,
/// set on the Club Website page):
///
/// - the Schedule page's qualifying card and a driver's own qualifying line
///   say "Provisional until 20:42" (BuildQualifyingDocument);
/// - each race on a driver's night carries the moment its window closes,
///   and the phone takes "Something wrong?" away then;
/// - the announcer says once, per round, that results are provisional and
///   queries go to the desk;
/// - the Big Screen's finished board says "PROVISIONAL UNTIL 20:42";
/// - a query asked inside a window goes to the top of Driver Requests.
///
/// 12 Nov 2026, setup plan step 8: a window now opens after EVERY
/// qualifying round of a class (once all that round's heats are in) and
/// after every main, not only after the last qualifier. Seeding the finals
/// still waits only for the last qualifying round's window. All of it reads
/// ProtestWindow, where it is tested.
/// </summary>
public partial class MainForm
{
    /// <summary>The windows that have been announced, by race day, class and round - so each is said once.</summary>
    private readonly HashSet<string> _protestWindowsSaid = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A result has just been filed: when it completed a qualifying round of
    /// a class, or was a main, say the window has opened and send every
    /// driver of that class's races tonight their night again, so their races
    /// carry the closing time. Never throws into the filing path.
    /// </summary>
    private void ProtestWindowAfterFiling(SessionRecord record)
    {
        try
        {
            if (record.IsNotScored()
                || record.RoundId is not { Length: > 0 } roundId
                || _rounds.Get(roundId) is not { Kind: RoundKind.Qualifier or RoundKind.Main } round
                || _raceProgram is null
                || _raceEvents.Active is not { IsLegacy: false } day)
            {
                return;
            }

            foreach (var className in MixedHeats.ClassesOf(round))
            {
                if (_raceProgram.RoundProtestWindow(round, className, record) is not { } window
                    || !_protestWindowsSaid.Add($"{day.Id}|{className}|{window.Key}"))
                {
                    continue;
                }

                var minutes = _raceProgram.ProtestMinutesTonight;
                var line = ProtestWindow.Announcement(className, minutes, window);
                _announcer.SpeakReminder(line, showOnScreens: true, onAir: "Notice");
                OnClientRawMessage($"[program] {className}: {window.What} is in - results are provisional for {Plural.Of(minutes, "minute")}.");

                // After this filing has linked the round, so the nights see it.
                var cls = className;
                BeginInvoke(() => PublishNightsFor(DriversInClassRaces(cls)));
            }
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[program] The protest window could not be worked out: {ex.Message}");
        }
    }

    /// <summary>Everybody in tonight's recorded qualifiers and mains of a class.</summary>
    private IEnumerable<string> DriversInClassRaces(string className)
    {
        var scope = _raceEvents.ActiveRoundScope();

        return _rounds.All
            .Where(r => r.Kind is RoundKind.Qualifier or RoundKind.Main && r.IsRecorded && (scope is null || scope.Contains(r.Id)) && MixedHeats.RunsClass(r, className))
            .Select(r => _sessionHistory.Get(r.LinkedSessionId!))
            .OfType<SessionRecord>()
            .SelectMany(DriversIn)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// When a race's own window closes (setup plan step 8): its round's - the
    /// qualifying round it is part of, or the main - for the driver's class.
    /// With no driver (the Big Screen), the latest window of any class the
    /// heat runs, so a shared heat reads provisional while any of its classes
    /// does (bug hunt 18). Null for no window, a practice, or a round still
    /// being run.
    /// </summary>
    private DateTime? RaceProtestClosesAtUtc(SessionRecord race, string? who = null)
    {
        if (_raceProgram is null
            || race.RoundId is not { Length: > 0 } roundId
            || _rounds.Get(roundId) is not { Kind: RoundKind.Qualifier or RoundKind.Main } round)
        {
            return null;
        }

        if (who is null)
        {
            return MixedHeats.ClassesOf(round)
                .Select(className => _raceProgram.RoundProtestWindow(round, className)?.ClosesAtUtc)
                .Max();
        }

        return _raceProgram.RoundProtestWindow(round, MixedHeats.ClassOfDriver(round, who, _profiles))?.ClosesAtUtc;
    }

    /// <summary>
    /// A night's races with each one's query deadline on - see
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
                    || _sessionHistory.Get(race.Sid) is not { } session
                    || RaceProtestClosesAtUtc(session, who) is not { } closes)
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
    /// latest qualifying round's window is open, as (words, Unix ms it
    /// closes) - else nulls.
    /// </summary>
    private (string? Words, long? Until) ProvisionalFor(string className)
    {
        if (_raceProgram?.LatestProtestWindow(className, RoundKind.Qualifier) is { } window && ProtestWindow.IsProvisional(window.ClosesAtUtc, DateTime.UtcNow))
        {
            return (ProtestWindow.PhoneWords(window.ClosesAtUtc), UnixMs(window.ClosesAtUtc));
        }

        return (null, null);
    }

    /// <summary>
    /// The Big Screen's finished board (setup plan step 8): "PROVISIONAL
    /// UNTIL 20:42" while the window of the race on the board - tonight's
    /// last recorded race - is open. Null otherwise.
    /// </summary>
    private string? ProvisionalBoardLine()
    {
        // The Big Screen asks on every refresh tick; the answer only moves
        // when a race is filed or the window closes, so a few seconds' cache
        // keeps the screen's tick cheap.
        var now = DateTime.UtcNow;

        if (_provisionalBoardCache is { } cached && now - cached.AtUtc < TimeSpan.FromSeconds(3))
        {
            return cached.Line;
        }

        var line = ProvisionalBoardLineNow();
        _provisionalBoardCache = (now, line);
        return line;
    }

    private (DateTime AtUtc, string? Line)? _provisionalBoardCache;

    private string? ProvisionalBoardLineNow()
    {
        try
        {
            var scope = _raceEvents.ActiveRoundScope();

            if (_raceProgram is null || scope is null)
            {
                return null;
            }

            var latest = _rounds.All
                .Where(r => r.Kind is RoundKind.Qualifier or RoundKind.Main && r.IsRecorded && scope.Contains(r.Id))
                .Select(r => (Round: r, Session: _sessionHistory.Get(r.LinkedSessionId!)))
                .Where(x => x.Session is not null && !x.Session.IsNotScored())
                .MaxBy(x => x.Session!.EndedAtUtc);

            if (latest.Session is null || RaceProtestClosesAtUtc(latest.Session) is not { } closes || !ProtestWindow.IsProvisional(closes, DateTime.UtcNow))
            {
                return null;
            }

            return ProtestWindow.BoardWords(closes);
        }
        catch (Exception)
        {
            return null; // a board line is never worth a broken screen
        }
    }

    /// <summary>
    /// Whether a driver's query came in while its race's window - for the
    /// asking driver's class - was open; Driver Requests puts those first.
    /// </summary>
    private bool QueryInProtestWindow(SessionRecord race, DateTime askedUtc, string? who) =>
        ProtestWindow.AskedInside(askedUtc, race.EndedAtUtc, RaceProtestClosesAtUtc(race, who));

    private static long UnixMs(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
}
