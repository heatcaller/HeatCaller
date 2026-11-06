namespace HeatCaller;

/// <summary>
/// The driver experience on the club's own Wi-Fi pages (22 Oct 2026, driver
/// experience, package W1): what the web server is handed so the follow
/// card can explain penalties (item 1) and check a transponder (item 4),
/// and so the OBS overlay and broadcast graphics can carry captions (item
/// 9). The pages themselves, the call-up timing (item 3) and the pit board
/// (item 10) are all in WebServerService.
/// </summary>
/// <remarks>
/// EVERYTHING HERE RUNS ON THE UI THREAD and only hands the web server
/// either a copy (PublishPenalties) or something with a lock of its own
/// (TransponderWatch, CaptionLog). The state document that reads them is
/// built on the UI thread too - see WebServerService.PublishState.
///
/// Threaded (24 Oct 2026, timing layer, P8) that document is built off the
/// UI (MainForm.LiveState), so SightingOf and Captions are asked from a
/// pool thread: both were already safe there - TransponderWatch and
/// CaptionLog lock, and ProfileStore.PrimaryForAlternate reads the frozen
/// index.
/// </remarks>
public partial class MainForm
{
    partial void WireWiFiExtras()
    {
        // When a driver's transponder was last heard, and how well, for
        // "Check my transponder". Through TransponderWatch, which records
        // every pass, practice included, under its own lock.
        _webServer.SightingOf = SightingForWiFi;

        // The announcer's last few lines, for the captions strip. As heard:
        // the LAN pages are in the room - see WebServerService.Captions.
        _webServer.Captions = () => CaptionLog.Shared.Last(WebServerService.CaptionsInState);

        // Tonight's penalties at once, so a laptop restarted mid-evening
        // shows them before the next race is filed.
        PublishPenaltiesToWiFi();

        // WHY (2026-11-05, Bug hunt 11): a race deleted from Race History
        // (or with its whole day) left its penalty lines on the follow card
        // and its report-card lines on /coach until the next race was filed
        // - both are copies, rebuilt only when a race was filed or edited.
        // Rebuilt here too, from the history the race is already gone from.
        _sessionHistory.SessionDeleted += _ =>
        {
            PublishCoachCards();
            PublishPenaltiesToWiFi();
        };
    }

    partial void RaceFiledWiFiExtras(SessionRecord session)
    {
        // The whole night again rather than just this race: a correction
        // to an earlier race (ResultEditor) arrives here too, and the night
        // is a handful of races, so working it all out is cheaper than
        // keeping a copy of it in step.
        PublishPenaltiesToWiFi();
    }

    /// <summary>
    /// A sighting as the follow card wants it: when, and "good", "weak" or
    /// "unknown". Null when the transponder has not been heard inside
    /// TransponderWatch's window.
    /// </summary>
    /// <remarks>
    /// A pass from a driver's second car is filed under their main
    /// transponder (TransponderWatch.AlternateTransponderOwner), and the
    /// follow list can name either, so the main one is asked for too.
    /// </remarks>
    private (DateTime SeenUtc, string Signal)? SightingForWiFi(string transponder)
    {
        if (string.IsNullOrWhiteSpace(transponder))
        {
            return null;
        }

        var sighting = _transponderWatch.Of(transponder);

        if (sighting is null && _profiles.PrimaryForAlternate(transponder) is { } primary)
        {
            sighting = _transponderWatch.Of(primary);
        }

        if (sighting is null)
        {
            return null;
        }

        return (sighting.LastSeenUtc, SignalWord(sighting));
    }

    /// <summary>
    /// "good", "weak" or "unknown" - the same three words the relay's
    /// driverlive "seen" uses. Unknown is a decoder that reports no signal
    /// at all (an I-Lap), which is not the same as a weak one.
    /// </summary>
    // WHY (23 Oct 2026, bug hunt 10): Weak is a ratio over many passes, so a
    // remounted transponder kept reading weak; the check now reports the last
    // pass heard.
    internal static string SignalWord(TransponderWatch.Sighting sighting) =>
        !sighting.LastSignalKnown ? "unknown"
        : sighting.LastPassWeak ? "weak"
        : "good";

    /// <summary>
    /// Every penalty line from tonight's races, by transponder, handed to
    /// the follow card (WebServerService.PublishPenalties). Each line says
    /// which race it was in: "Q1 Heat 2: +5 s - jump start".
    /// </summary>
    /// <remarks>
    /// Tonight is TonightAtTheTrack's night (6am to 6am), the one the coach
    /// cards use. Demo races are rehearsals and are left out, as they are
    /// from every result. Never throws: the follow card is a courtesy, and
    /// this runs inside the filing path.
    /// </remarks>
    private void PublishPenaltiesToWiFi()
    {
        try
        {
            _webServer.PublishPenalties(PenaltiesTonight(_sessionHistory.All, DateTime.Now));
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[web] Could not work out tonight's penalties for the phones: {ex.Message}");
        }
    }

    /// <summary>The working-out behind PublishPenaltiesToWiFi, kept apart so it can be tested without a window.</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> PenaltiesTonight(
        IEnumerable<SessionRecord> history, DateTime nowLocal)
    {
        var tonight = TonightAtTheTrack.NightOf(nowLocal);
        var byDriver = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var races = history
            .Where(s => s is not null && !s.IsNotScored())
            .Where(s => TonightAtTheTrack.NightOf(s.StartedAtUtc.ToLocalTime()) == tonight)
            .OrderBy(s => s.StartedAtUtc);

        foreach (var session in races)
        {
            var label = RaceLabelForPenalties(session);

            foreach (var entry in session.Results ?? new List<SessionResultEntry>())
            {
                var who = (entry?.Transponder ?? string.Empty).Trim();

                if (entry is null || who.Length == 0 || GhostService.IsGhost(who))
                {
                    continue;
                }

                var lines = PenaltyWords.For(entry, session);

                if (lines.Count == 0)
                {
                    continue;
                }

                if (!byDriver.TryGetValue(who, out var list))
                {
                    list = new List<string>();
                    byDriver[who] = list;
                }

                list.AddRange(lines.Select(line => label + ": " + line));
            }
        }

        return byDriver.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The race's name on one line - "Q1 Heat 2" - or "Race" / "Practice" when it has none.</summary>
    private static string RaceLabelForPenalties(SessionRecord session)
    {
        var label = string.Join(" ", (session.Label ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (label.Length > 0)
        {
            return label;
        }

        return session.SessionType == RaceSessionType.Practice ? "Practice" : "Race";
    }
}
