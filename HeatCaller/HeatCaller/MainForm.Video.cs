namespace HeatCaller;

/// <summary>
/// RACE VIDEO ON YOUTUBE (14 Nov 2026, plans/race-video-access-plan.md).
/// Tonight's stream (StreamTonight) is kept here and watched through OBS's
/// WebSocket (ObsLink); the publisher sends it to the relay as the private
/// "stream" document, from which the relay works out each race's moment in
/// the club's YouTube video. The Club Website page's Race video card reads
/// and edits it through RelaySettingsControl.Stream and .Obs.
/// </summary>
public sealed partial class MainForm
{
    private StreamTonight? _streamTonight;
    private ObsLink? _obsLink;

    partial void WireRaceVideo()
    {
        _streamTonight = new StreamTonight(StreamTonight.DefaultPath);
        _obsLink = new ObsLink(() => _settings, _streamTonight, line => OnClientRawMessage($"{AppTime.LogStampNow()}  {line}"));
        _obsLink.Start();

        // Switch OBS scenes with the race (stream overlay plan 6.1): only
        // when the club turned it on, never for practice or a demo, and the
        // scene for each part of the night as the club named it.
        _race.StateChanged += state =>
        {
            if (_settings.ObsSwitchScenes && !_race.IsPracticeMode && !_race.IsDemoMode
                && ObsScenes.For(_settings, state) is { Length: > 0 } scene)
            {
                _obsLink?.SwitchTo(scene);
            }
        };
        RelaySettingsControl.Stream = _streamTonight;
        RelaySettingsControl.Obs = _obsLink;
        RelaySettingsControl.DeadVideos = () => _relayPublisher.DeadVideos;
        RelaySettingsControl.RacesTonight = VideoRacesTonight;

        // A driver's "ask the club to hide this video", approved on the
        // Driver Requests window (V6): that race's video goes everywhere at
        // once, the next time the stream document is sent.
        if (_driverRequestDesk is { } desk)
        {
            desk.HideRaceVideo = sid => _streamTonight?.SetRaceHidden(sid, hidden: true);
        }
        FormClosed += (_, _) => _obsLink?.Dispose();
    }

    /// <summary>The "stream" document: tonight's pieces, and the races hidden or with a junior in them.</summary>
    private string StreamDocument()
    {
        var stream = _streamTonight ?? new StreamTonight();
        return stream.Document(_settings, JuniorRacesToday());
    }

    /// <summary>
    /// For the closing checklist (look-back, 14 Nov 2026): tonight's stream
    /// pieces still without a YouTube link, and links YouTube refused. Null
    /// when race video is off or nothing was streamed.
    /// </summary>
    private NightClose.VideoLeft? VideoLeftTonight()
    {
        var pieces = _streamTonight?.Pieces ?? Array.Empty<StreamTonight.Piece>();

        if (!_settings.VideoEnabled || pieces.Count == 0)
        {
            return null;
        }

        var dead = _relayPublisher.DeadVideos;
        return new NightClose.VideoLeft(
            pieces.Count(p => !p.Hidden && p.VideoId.Length == 0),
            pieces.Count(p => !p.Hidden && p.VideoId.Length > 0 && dead.Contains(p.VideoId)));
    }

    /// <summary>Tonight's races (the last 20 hours), newest first, for the Race video card's "Hide races..." (V6).</summary>
    private IReadOnlyList<(string Id, string Label)> VideoRacesTonight()
    {
        var since = DateTime.UtcNow.AddHours(-20);
        return _sessionHistory.SavedView().Saved
            .Where(s => s.StartedAtUtc >= since)
            .OrderByDescending(s => s.StartedAtUtc)
            .Select(s => (s.Id, $"{AppTime.TimeOfDay(s.StartedAtUtc.ToLocalTime())}  {(string.IsNullOrWhiteSpace(s.Label) ? "Race" : s.Label.Trim())}"))
            .ToList();
    }

    /// <summary>
    /// Today's races with a junior in them, by session id - the relay never
    /// shows these on public results (Asher, 14 Nov 2026: "juniors stay on
    /// their own night"). From the profiles' own junior flag (the guardian's
    /// waiver), never a name.
    /// </summary>
    private IEnumerable<string> JuniorRacesToday()
    {
        var since = DateTime.UtcNow.AddHours(-20);

        foreach (var session in _sessionHistory.SavedView().Saved.Where(s => s.StartedAtUtc >= since))
        {
            if (session.Results.Any(r => _profiles.GetForAnyThread(r.Transponder) is { IsJunior: true }))
            {
                yield return session.Id;
            }
        }
    }
}
