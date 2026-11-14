namespace HeatCaller;

/// <summary>
/// A driver's report card, as MainForm gathers it (22 Oct 2026) - see
/// <see cref="CoachReport"/>, which does all the arithmetic. This file only
/// fetches what it is worked out from: the saved sessions, the setup change
/// logged tonight for the car the driver ran, and the names other drivers
/// go by on the page it is for.
/// </summary>
public partial class MainForm
{
    /// <summary>
    /// The report card for one driver tonight, a line at a time - empty
    /// when they have not run tonight, or when anything it reads cannot be
    /// read. Never throws: a report card is a courtesy, and the callers are
    /// the night publisher on the filing path, the night email, Driver Hub
    /// and Race History.
    /// </summary>
    /// <param name="transponder">The driver's transponder.</param>
    /// <param name="publicNames">
    /// True for a page that goes online (the driver's own night on the
    /// relay): the other driver named in the benchmark line is named the
    /// way the club's public pages name people, shortened when "Shorten
    /// published names" is ticked. The driver's own night is private to
    /// them; the person they are being compared with is not the reader.
    /// </param>
    /// <param name="night">The night to report on - tonight when null.</param>
    internal IReadOnlyList<string> CoachReportFor(string transponder, bool publicNames, DateTime? night = null)
    {
        try
        {
            var history = _sessionHistory.All;
            var thisNight = TonightAtTheTrack.NightOf(DateTime.Now);
            var reported = (night ?? thisNight).Date;

            return CoachReport.Lines(new CoachReport.Input(
                transponder,
                history,
                reported,
                utc => TonightAtTheTrack.NightOf(utc.ToLocalTime()),
                NamerFor(history, reported, publicNames),
                CoachSetupChange(transponder, reported),
                utc => utc.ToLocalTime().ToString("t", System.Globalization.CultureInfo.CurrentCulture),
                reported == thisNight ? "tonight" : "that night"));
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// How another driver is named on the card. On the club's own screens,
    /// by the name on their profile; online, through the same shortening the
    /// public results use, applied to everybody who raced tonight at once so
    /// two Sams stay told apart (see RelayPublisher.DisplayNames).
    /// </summary>
    private Func<string, string> NamerFor(IReadOnlyList<SessionRecord> history, DateTime night, bool publicNames)
    {
        string Full(string transponder) =>
            _profiles.Get(transponder)?.DriverName is { Length: > 0 } named ? named : string.Empty;

        if (!publicNames)
        {
            return Full;
        }

        var everyone = history
            .Where(s => TonightAtTheTrack.NightOf(s.StartedAtUtc.ToLocalTime()) == night)
            .SelectMany(s => s.Results)
            .Select(r => (r.Transponder ?? string.Empty).Trim())
            .Where(t => t.Length > 0 && !GhostService.IsGhost(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var shortened = RelayPublisher.DisplayNames(
            everyone.Select(t => RelayDocuments.PublicNameOrUnnamed(Full(t), t)).ToList(),
            _settings.RelayAbbreviateNames,
            everyone.Select(t => (string?)t).ToList());

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < everyone.Count; i++)
        {
            map[everyone[i]] = shortened[i];
        }

        return transponder => map.TryGetValue(transponder, out var name) ? name : string.Empty;
    }

    /// <summary>
    /// The newest setup logged THAT NIGHT for a car this driver was in on the
    /// day - their own, or one lent to them for the race day - with what
    /// changed from the setup before it on that car. Null when nothing was
    /// logged tonight: a setup from last week is not a change they can see
    /// in tonight's laps.
    /// </summary>
    private CoachReport.SetupChange? CoachSetupChange(string transponder, DateTime night)
    {
        // WHY (22 Oct 2026): this always asked about the race day open NOW,
        // so a card opened from Race History for last Tuesday let tonight's
        // loan decide whose car it was - the borrower got the owner's
        // Tuesday setup change and the owner lost theirs. Now tonight asks
        // about the day in hand, and an earlier night asks about the race
        // day started that night (none = null, and DriverFor falls back to
        // the owner). A car remembers only one loan, so an old loan counts
        // only while the car still holds it - but tonight's never answers
        // for an earlier night. Same idea as RecapCarFactsFor.
        var eventId = night == TonightAtTheTrack.NightOf(DateTime.Now)
            ? _raceEvents.Active?.Id
            : _raceEvents.All
                .FirstOrDefault(e => !e.IsLegacy && TonightAtTheTrack.NightOf(e.StartedUtc.ToLocalTime()) == night)?.Id;

        var carIds = new HashSet<string>(
            _cars.All
                .Where(car => string.Equals(car.DriverFor(eventId), transponder, StringComparison.OrdinalIgnoreCase))
                .Select(car => car.Id),
            StringComparer.Ordinal);

        if (carIds.Count == 0)
        {
            return null;
        }

        var setups = _setupLog.All;

        var latest = setups
            .Where(entry => !string.IsNullOrEmpty(entry.CarId) && carIds.Contains(entry.CarId)
                            && TonightAtTheTrack.NightOf(entry.RecordedAtUtc.ToLocalTime()) == night)
            .OrderByDescending(entry => entry.RecordedAtUtc)
            .FirstOrDefault();

        if (latest is null)
        {
            return null;
        }

        // "Pinion 22 → 23, Front springs Soft → Medium" - or nothing when it
        // is the car's first setup, or nothing on the sheet changed.
        var changes = SetupDiff.SincePrevious(latest, setups, _chassisTemplates.Get)?
            .Where(change => change.OnSheet)
            .ToList();

        var what = changes is { Count: > 0 } ? SetupDiff.Describe(changes, max: 2) : null;

        return new CoachReport.SetupChange(latest.RecordedAtUtc, what);
    }

    /// <summary>
    /// Race Control's Coach menu: "Chase Sam's best (ghost)" (22 Oct 2026).
    /// The same as the Practice page's Chase a Best Lap for that driver - an
    /// own-best ghost put on, or brought up to their latest best, ghost
    /// racing switched on, and one Undo to put the ghosts back. Saving the
    /// ghosts puts it on track in a running practice (OnGhostStoreSaved).
    /// </summary>
    /// <returns>What happened, for the console.</returns>
    private string? ChaseOwnBestFromRaceControl(string transponder)
    {
        if (string.IsNullOrWhiteSpace(transponder) || GhostService.IsGhost(transponder))
        {
            return null;
        }

        var profile = _profiles.Get(transponder);
        // Bug hunt 11 (2026-11-05): the Undo puts back only what the chase
        // changed, not a snapshot of every ghost - see OwnBestGhost.ChaseWithUndo.
        var (ghost, refreshed, undo) = OwnBestGhost.ChaseWithUndo(_ghostStore.Current, transponder, profile?.DriverName, profile?.CarModel);

        var store = _ghostStore;
        _undo.Capture(refreshed ? $"Bring {ghost.Name} up to date" : $"Chase {ghost.Name}", () =>
        {
            undo(store.Current);
            store.Save();
        });

        _ghostStore.Save();

        return refreshed
            ? $"{ghost.Name} brought up to their latest best - beat that."
            : $"{ghost.Name} is on: a ghost at their own best lap on this layout. It runs in practice and races while ghost racing is on.";
    }
}
