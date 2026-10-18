namespace HeatCaller;

/// <summary>
/// The broadcast's introductions at staging (18 Oct 2026): before a main,
/// what qualifying decided; then every driver on the grid, pole first, with
/// their number, home club and a line of form. Queued as the race is staged,
/// and the automatic start waits for them - see <see cref="IntroWait"/>.
///
/// Switched on and off, and held for the mains, on the Voice page
/// ("Driver introductions", "Qualifying recap"). The words are DriverIntros';
/// the speaking is the announcer's (AnnouncementService.Intros).
/// </summary>
public sealed partial class RaceControlControl
{
    /// <summary>How long the automatic start allows each introduction before giving up on the audio and counting down anyway.</summary>
    private static readonly TimeSpan PerIntroduction = TimeSpan.FromSeconds(7);

    /// <summary>How long it allows the qualifying recap.</summary>
    private static readonly TimeSpan ForTheRecap = TimeSpan.FromSeconds(15);

    /// <summary>The most the automatic start will wait at staging for the introductions, however big the grid.</summary>
    private static readonly TimeSpan MostForIntroductions = TimeSpan.FromSeconds(150);

    /// <summary>
    /// Queues the qualifying recap and the driver introductions for the race
    /// just staged. Returns how long the automatic start should be prepared
    /// to wait for them, or null when nothing was queued.
    /// </summary>
    /// <param name="boardBeforeStaging">
    /// Who was on the board before Stage cleared it - the only field a
    /// freeform race (no round selected) has to introduce. Read by the
    /// caller before staging, because staging empties the board.
    /// </param>
    private TimeSpan? SpeakIntroductions(IReadOnlyList<string> boardBeforeStaging)
    {
        if (_race.IsDemoMode || _race.IsPracticeMode || _race.State != RaceState.Staging)
        {
            return null;
        }

        // Nothing to read the history for when neither is switched on - the
        // form lines walk every saved race, and staging is on the UI thread.
        if (!_announcer.WillCall(AnnouncementEventType.DriverIntro) && !_announcer.WillCall(AnnouncementEventType.QualifyingRecap))
        {
            return null;
        }

        try
        {
            var round = _activeRound?.Invoke();
            var roundRoster = round?.AssignedTransponders?
                .Where(t => !string.IsNullOrWhiteSpace(t) && !GhostService.IsGhost(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            // A round's roster is its grid, in order (GridForStagger reads
            // the same list). Without a round there is no grid, so the
            // drivers are introduced without a slot rather than one being
            // made up - see DriverIntros.GridPhrase.
            var hasGrid = roundRoster.Count > 0;
            var field = hasGrid
                ? roundRoster
                : (boardBeforeStaging ?? Array.Empty<string>())
                    .Where(t => !string.IsNullOrWhiteSpace(t) && !GhostService.IsGhost(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            if (field.Count == 0)
            {
                return null;
            }

            var isMain = round is { Kind: RoundKind.Main };

            // Read once: every driver's form comes from the same history.
            var history = _sessionHistory.All;
            var now = DateTime.UtcNow;

            var grid = field.Select((transponder, i) =>
            {
                var profile = _profiles.Get(transponder);
                var screen = !string.IsNullOrWhiteSpace(profile?.DriverName) ? profile!.DriverName : transponder;
                var spoken = profile is not null && !string.IsNullOrWhiteSpace(profile.SpokenName) ? profile.SpokenName : screen;

                return new IntroDriver(
                    transponder,
                    hasGrid ? i + 1 : 0,
                    spoken,
                    screen,
                    profile?.RaceNumber ?? string.Empty,
                    profile?.HomeTrack ?? string.Empty,
                    DriverIntros.Stat(history, transponder, now));
            }).ToList();

            var recapQueued = isMain && round is not null && SpeakQualifyingRecap(round, field);
            var introductions = _announcer.SpeakDriverIntros(grid, isMain);

            if (!recapQueued && introductions == 0)
            {
                return null;
            }

            return IntroWait(introductions, recapQueued);
        }
        catch (Exception ex)
        {
            // Never the reason a race does not start.
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [voice] The grid introductions were skipped: {ex.Message}");
            return null;
        }
    }

    /// <summary>How long staging may wait for <paramref name="introductions"/> introductions and, when queued, the recap.</summary>
    internal static TimeSpan IntroWait(int introductions, bool recap)
    {
        var wait = TimeSpan.FromSeconds(20)
            + PerIntroduction * Math.Max(0, introductions)
            + (recap ? ForTheRecap : TimeSpan.Zero);

        return wait > MostForIntroductions ? MostForIntroductions : wait;
    }

    /// <summary>
    /// What tonight's qualifying decided for the cars in <paramref name="main"/>,
    /// in qualifying order - the calculator's own result, with the class's
    /// own method and tonight's rounds only, exactly as the grid sheet ranks
    /// it. False when it was not queued.
    /// </summary>
    private bool SpeakQualifyingRecap(RoundDefinition main, IReadOnlyList<string> field)
    {
        if (_rounds is null || string.IsNullOrWhiteSpace(main.ClassName))
        {
            return false;
        }

        var raceClass = _raceClasses.All.FirstOrDefault(c => string.Equals(c.Name, main.ClassName, StringComparison.OrdinalIgnoreCase));

        // Tonight's rounds and no others. Without a race day there is no
        // "tonight", and a null filter would mean every heat the class has
        // ever run - the August-pole mistake MainForm.Commentary describes -
        // so an empty one is passed instead, and there is no recap.
        IReadOnlyCollection<string> tonight = _events?.QualifyingScope() ?? (IReadOnlyCollection<string>)Array.Empty<string>();

        var entries = QualifyingCalculator.Compute(
            main.ClassName, _rounds, _sessionHistory, _profiles, raceClass, tonight);

        var inThisMain = new HashSet<string>(field, StringComparer.OrdinalIgnoreCase);

        var order = entries
            .Where(e => inThisMain.Contains(e.Transponder))
            .ToList();

        if (order.Count == 0)
        {
            return false;
        }

        // The calculator's BestConsecutiveMs is a TOTAL over every heat that
        // counted (best two of three, say), so the recap is handed one run's
        // worth - the average lap times the run's length - or "averaging" a
        // lap would come out two or three times too slow.
        var lapCount = Math.Max(1, raceClass?.ConsecutiveLapCount ?? 3);

        var cars = order.Select(e =>
        {
            var profile = _profiles.Get(e.Transponder);
            var screen = !string.IsNullOrWhiteSpace(profile?.DriverName) ? profile!.DriverName
                : !string.IsNullOrWhiteSpace(e.DriverName) ? e.DriverName
                : e.Transponder;
            var spoken = profile is not null && !string.IsNullOrWhiteSpace(profile.SpokenName) ? profile.SpokenName : screen;

            return new RecapCar(spoken, screen, e.BestLaps, e.BestElapsedMs, e.ConsecutiveAverageMs(lapCount) * lapCount, e.BestFastLapMs);
        }).ToList();

        return _announcer.SpeakQualifyingRecap(
            cars,
            raceClass?.QualifyingMethod ?? QualifyingMethod.TotalLaps,
            lapCount,
            order[0].Transponder);
    }
}
