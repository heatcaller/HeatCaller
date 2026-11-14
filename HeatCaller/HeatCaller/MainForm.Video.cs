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
        RelaySettingsControl.Stream = _streamTonight;
        RelaySettingsControl.Obs = _obsLink;
        RelaySettingsControl.DeadVideos = () => _relayPublisher.DeadVideos;
        FormClosed += (_, _) => _obsLink?.Dispose();
    }

    /// <summary>The "stream" document: tonight's pieces, and the races hidden or with a junior in them.</summary>
    private string StreamDocument()
    {
        var stream = _streamTonight ?? new StreamTonight();
        return stream.Document(_settings, JuniorRacesToday());
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
