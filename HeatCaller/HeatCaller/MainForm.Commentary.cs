namespace HeatCaller;

/// <summary>
/// MainForm's side of the recap's broadcast (27 Sep 2026): what the Garage
/// has on the winning car, for the mechanic's interview, and the voices the
/// Commentary page offers the mechanic. Here because both need stores and
/// voice clients only MainForm holds.
/// </summary>
public partial class MainForm
{
    /// <summary>A line for the Test Voice button beside the mechanic's voice - the kind of thing the mechanic says.</summary>
    private const string MechanicTestLine = "Thanks! We went up a tooth on the pinion before the main, and you could see it down the straight.";

    /// <summary>Hooks the recap's broadcast up to Race Control and the Commentary page. Called once, when both exist.</summary>
    private void WireRecapBroadcast(RaceControlControl raceControl, CommentaryControl commentary)
    {
        raceControl.MechanicSetupFacts = MechanicSetupFactsFor;

        commentary.EngineInUse = () => _voiceStore.Current.EffectiveEngine;
        commentary.LoadVoices = MechanicVoiceChoicesAsync;

        // Heard even while the announcer is muted - it was asked for by hand.
        commentary.SpeakAsMechanic = voice => _announcer.SpeakTest(MechanicTestLine, _voiceStore.Current.Speed, voice);
    }

    /// <summary>
    /// Hands the director the readers it cannot reach on its own.
    ///
    /// Each one is read fresh when the segment is considered, not held:
    /// a driver's record changes as the night goes on, and a championship
    /// changes with every result filed. All of it is arithmetic over
    /// stores already open, so it costs nothing to ask and is only asked
    /// when a segment is actually being weighed up.
    ///
    /// A null reader means that segment says nothing - see
    /// CommentaryDirector - so this is also the list of what is wired.
    /// </summary>
    private void WireCommentaryReaders(CommentaryDirector? director)
    {
        if (director is null)
        {
            return;
        }

        // Who is on track, and the class their record should be counted
        // over. Taken from the session rather than the programme: it is
        // the drivers actually staged, which is what the intro is about.
        director.GridHistory = () => CommentaryReaders.GridHistory(
            _race.GetOrderedStandings().Select(s => s.TransponderId).ToList(),
            _activeRound?.ClassName,
            _profiles,
            _sessionHistory,
            _rounds);

        // The club's own qualifying method, not the calculator's default -
        // a report that ranks by total laps at a club that qualifies on a
        // consecutive-lap window would read out the wrong grid.
        director.Qualifying = () => CommentaryReaders.Qualifying(
            _activeRound?.ClassName ?? string.Empty,
            _rounds,
            _sessionHistory,
            _profiles,
            ClassOnTrack());

        director.Championship = () => CommentaryReaders.Championship(
            ChampionshipLive.Compute(
                LastFiledResults(),
                _activeRound,
                _seasonStore,
                _rounds,
                _raceEvents,
                _sessionHistory,
                _profiles));
    }

    /// <summary>The class the active round belongs to, for the qualifying method and its lap counts.</summary>
    private RaceClass? ClassOnTrack()
    {
        var name = _activeRound?.ClassName;

        return string.IsNullOrWhiteSpace(name)
            ? null
            : _raceClasses.All.FirstOrDefault(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The results of the last race filed, which is what the championship
    /// check is about.
    ///
    /// The saved record rather than the live standings: the check runs
    /// after a class's last main, by which point the race is over and
    /// filed, and the live session may already have been reset for the
    /// next one.
    /// </summary>
    private IReadOnlyList<SessionResultEntry> LastFiledResults()
    {
        var all = _sessionHistory.All;

        for (var i = all.Count - 1; i >= 0; i--)
        {
            if (all[i].SessionType != RaceSessionType.Practice && all[i].Results.Count > 0)
            {
                return all[i].Results;
            }
        }

        return Array.Empty<SessionResultEntry>();
    }

    /// <summary>
    /// The setup lines for the winner of a saved race - see MechanicBrief.
    /// Asked by Race Control on the UI thread, where the Garage's stores
    /// are read.
    /// </summary>
    private IReadOnlyList<string> MechanicSetupFactsFor(SessionRecord record, RoundDefinition? round)
    {
        var winner = record.Results.OrderBy(r => r.Position).FirstOrDefault();

        if (winner is null)
        {
            return Array.Empty<string>();
        }

        var driverName = _profiles.Get(winner.Transponder)?.DriverName;

        if (string.IsNullOrWhiteSpace(driverName))
        {
            driverName = winner.DriverName;
        }

        // The race day it was run on, for a car lent for that day: the
        // round's own day, or the day in hand for a race that isn't a round.
        var eventId = (round is null ? null : _raceEvents.EventForRound(round.Id)?.Id) ?? _raceEvents.Active?.Id;

        return MechanicBrief.SetupLines(
            winner.Transponder,
            driverName ?? string.Empty,
            round?.ClassName,
            eventId,
            record.EndedAtUtc,
            _cars.All,
            _setupLog.All,
            _chassisTemplates.Get);
    }

    /// <summary>
    /// The voices an engine offers, for the mechanic's list on the
    /// Commentary page. Throws with a sentence the page can show when it
    /// can't list them.
    /// </summary>
    private async Task<IReadOnlyList<CommentaryControl.VoiceChoice>> MechanicVoiceChoicesAsync(VoiceEngine engine)
    {
        switch (engine)
        {
            case VoiceEngine.Windows:
            {
                // SAPI's list, off the UI thread - the first read of it can take a moment.
                var installed = await Task.Run(() => WindowsVoice.InstalledVoices(refresh: true));

                if (installed.Count == 0)
                {
                    throw new InvalidOperationException(WindowsVoice.UnavailableReason ?? "no Windows voice is installed on this PC");
                }

                return installed
                    .Select(v => new CommentaryControl.VoiceChoice(v.Name, v.Name, string.Empty, v.Display))
                    .ToList();
            }

            case VoiceEngine.Kokoro:
            {
                // Read from the names of the files beside the exe - nothing
                // to download or ask for, so this can't fail.
                return KokoroSpeech.InstalledVoices()
                    .Select(v => new CommentaryControl.VoiceChoice(v.Name, v.Name, string.Empty, v.Display))
                    .ToList();
            }

            case VoiceEngine.HeatCaller:
            {
                var catalogue = await _heatCallerVoice.CatalogueAsync();

                if (!catalogue.Enabled)
                {
                    throw new InvalidOperationException("the HeatCaller voice isn't included for this club yet");
                }

                return catalogue.Voices
                    .Select(v => new CommentaryControl.VoiceChoice(v.Id, v.Name, v.Rev, v.ToString()))
                    .ToList();
            }

            default:
            {
                var key = _voiceStore.Current.ApiKey;

                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new InvalidOperationException("there's no ElevenLabs API key on the Voice Settings page");
                }

                var voices = await new ElevenLabsClient().GetVoicesAsync(key);

                return voices
                    .Select(v => new CommentaryControl.VoiceChoice(v.VoiceId, v.Name, string.Empty, v.Name))
                    .ToList();
            }
        }
    }
}
