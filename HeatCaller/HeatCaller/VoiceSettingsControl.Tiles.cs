namespace HeatCaller;

/// <summary>
/// The tiles over the Voice and Race Calls tabs (17 Nov 2026, page jazz-up):
/// where each stands at a glance, read from the page's own controls as they
/// are on screen. Nothing here is saved, and nothing here changes what the
/// page does - the tiles only follow it.
/// </summary>
public sealed partial class VoiceSettingsControl
{
    /// <summary>The Voice tab's tiles: which engine speaks, in what voice, whether Kokoro can stand in, and the race calls' switch.</summary>
    private readonly StatTile[] _voiceTiles = { new(), new(), new(), new() };
    private static readonly string[] VoiceTileCaptions = { "Speaks with", "Announcer voice", "Kokoro on this PC", "Race calls" };

    /// <summary>The Race Calls tab's tiles: how many calls are ticked for races and for practice, the pacing, and the most a lap.</summary>
    private readonly StatTile[] _raceCallTiles = { new(), new(), new(), new() };
    private static readonly string[] RaceCallTileCaptions = { "On in races", "On in practice", "Pacing", "Most per lap" };

    /// <summary>The cards split off Speak with (BuildEngineCard), placed by BuildLayout. Null until then.</summary>
    private Section? _raceCallsCard;
    private Section? _juniorsCard;
    private Section? _consentCard;

    /// <summary>The Voice tab's tiles, from the page as it is now.</summary>
    private void ShowVoiceTiles()
    {
        if (IsDisposed)
        {
            return;
        }

        var heatCaller = _rdoHeatCaller.Checked;
        var elevenLabs = _rdoElevenLabs.Checked;

        VoicePagePieces.Show(_voiceTiles[0],
            heatCaller ? "HeatCaller voice" : elevenLabs ? "ElevenLabs" : _rdoKokoro.Checked ? "Kokoro" : null,
            VoiceTileCaptions[0]);

        var voice = heatCaller ? VoicePagePieces.Picked(_cmbHeatCallerVoice)
            : elevenLabs ? VoicePagePieces.Picked(_cmbVoice)
            : VoicePagePieces.Picked(_cmbKokoroVoice);

        // "Michael (American, male)" as "Michael": the tile is narrow, and
        // the rest is on the Cast tab's picker.
        if (voice is not null && voice.IndexOf(" (", StringComparison.Ordinal) is > 0 and var bracket)
        {
            voice = voice[..bracket];
        }

        VoicePagePieces.Show(_voiceTiles[1], voice, VoiceTileCaptions[1]);

        var (kokoro, kokoroTone) = KokoroSpeech.IsDownloading ? ("Downloading", VoicePagePieces.Tone.Warn)
            : KokoroSpeech.IsAvailable ? ("Ready", VoicePagePieces.Tone.Good)
            : ("Not ready", VoicePagePieces.Tone.Bad);

        VoicePagePieces.Show(_voiceTiles[2], kokoro, VoiceTileCaptions[2], kokoroTone);

        var (calls, callsTone) = RaceCallsSwitch();
        VoicePagePieces.Show(_voiceTiles[3], calls, VoiceTileCaptions[3], callsTone);
    }

    /// <summary>"On", "Races only", "Practice only" or "Off" - the switch on the Voice tab's Race calls card.</summary>
    private (string Text, VoicePagePieces.Tone Tone) RaceCallsSwitch() =>
        (_chkRaceCallsRaces.Checked, _chkRaceCallsPractice.Checked) switch
        {
            (true, true) => ("On", VoicePagePieces.Tone.Good),
            (true, false) => ("Races only", VoicePagePieces.Tone.Plain),
            (false, true) => ("Practice only", VoicePagePieces.Tone.Plain),
            _ => ("Off", VoicePagePieces.Tone.Muted),
        };

    /// <summary>The Race Calls tab's tiles, from the rows' ticks and the How often card as they are now.</summary>
    private void ShowRaceCallTiles()
    {
        if (IsDisposed)
        {
            return;
        }

        var total = _eventRows.Count;
        var races = _eventRows.Values.Count(row => row.Race.Checked);
        var practice = _eventRows.Values.Count(row => row.Practice.Checked);

        // The switch on the Voice tab gates every row at once: off, the
        // ticks are kept but none is said.
        string Count(int on, bool switchedOn) =>
            total == 0 ? "-" : switchedOn ? $"{on} of {total}" : "Switched off";

        VoicePagePieces.Show(_raceCallTiles[0], Count(races, _chkRaceCallsRaces.Checked), RaceCallTileCaptions[0],
            _chkRaceCallsRaces.Checked ? VoicePagePieces.Tone.Plain : VoicePagePieces.Tone.Muted);
        VoicePagePieces.Show(_raceCallTiles[1], Count(practice, _chkRaceCallsPractice.Checked), RaceCallTileCaptions[1],
            _chkRaceCallsPractice.Checked ? VoicePagePieces.Tone.Plain : VoicePagePieces.Tone.Muted);

        VoicePagePieces.Show(_raceCallTiles[2], AnnouncerDensity.For(_appSettings.AnnouncerDensity).Name, RaceCallTileCaptions[2]);

        var most = (int)_numMaxPerLap.Value;
        VoicePagePieces.Show(_raceCallTiles[3], Plural.Of(most, "call"), RaceCallTileCaptions[3]);
    }
}
