using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HeatCaller;

/// <summary>
/// TONIGHT'S STREAM (14 Nov 2026, race video plan Phase 0). The club streams
/// from a second PC with OBS to its own YouTube channel; YouTube holds the
/// video. This laptop keeps only what lines the races up with it: each piece
/// of the stream - started and stopped on THIS laptop's clock (from OBS's
/// WebSocket, ObsLink, or the "Stream went live" button), so the streaming
/// PC's clock never matters - its YouTube link once pasted, and the club's
/// nudge for that piece; and the races hidden from video (a take-down).
///
/// A stream that drops and restarts is two pieces; each gets its own link,
/// matched in order. Kept in stream-tonight.json, so a restart mid-night
/// loses nothing, and pieces from an earlier day are let go when it loads.
///
/// Thread-safe: OBS's events arrive on the link's own thread, the desk edits
/// on the UI thread, and the publisher reads on its own.
/// </summary>
public sealed class StreamTonight
{
    public sealed class Piece
    {
        [JsonPropertyName("started")] public DateTime StartedUtc { get; set; }
        [JsonPropertyName("ended")] public DateTime? EndedUtc { get; set; }
        [JsonPropertyName("videoId")] public string VideoId { get; set; } = string.Empty;
        [JsonPropertyName("nudge")] public double NudgeSeconds { get; set; }
        [JsonPropertyName("hidden")] public bool Hidden { get; set; }
    }

    private sealed class Saved
    {
        [JsonPropertyName("pieces")] public List<Piece> Pieces { get; set; } = new();
        [JsonPropertyName("hidden")] public List<string> HiddenRaces { get; set; } = new();
    }

    private static readonly Regex YouTubeId = new("^[A-Za-z0-9_-]{11}$", RegexOptions.Compiled);
    private readonly object _gate = new();
    private readonly string? _path;
    private Saved _saved = new();

    /// <summary>Raised (on any thread) whenever a piece starts, stops or changes.</summary>
    public event Action? Changed;

    public StreamTonight(string? path = null, DateTime? nowUtc = null)
    {
        _path = path;

        if (path is not null && File.Exists(path))
        {
            try
            {
                _saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(path)) ?? new Saved();
            }
            catch (Exception)
            {
                _saved = new Saved();
            }
        }

        // Only tonight's: pieces that started more than 20 hours ago are
        // another night's, and so are their hidden races.
        var cutoff = (nowUtc ?? DateTime.UtcNow).AddHours(-20);

        if (_saved.Pieces.Count > 0 && _saved.Pieces.All(p => p.StartedUtc < cutoff))
        {
            _saved = new Saved();
        }
    }

    /// <summary>The default file, beside settings.json.</summary>
    public static string DefaultPath => Path.Combine(AppPaths.Root, "stream-tonight.json");

    public IReadOnlyList<Piece> Pieces
    {
        get { lock (_gate) { return _saved.Pieces.Select(Copy).ToList(); } }
    }

    public IReadOnlyCollection<string> HiddenRaces
    {
        get { lock (_gate) { return _saved.HiddenRaces.ToList(); } }
    }

    /// <summary>Whether a piece is streaming now.</summary>
    public bool Live
    {
        get { lock (_gate) { return _saved.Pieces.Count > 0 && _saved.Pieces[^1].EndedUtc is null; } }
    }

    /// <summary>The stream went live: a new piece, unless one is open already.</summary>
    public void Started(DateTime atUtc)
    {
        lock (_gate)
        {
            if (_saved.Pieces.Count > 0 && _saved.Pieces[^1].EndedUtc is null)
            {
                return;
            }

            if (_saved.Pieces.Count >= 10)
            {
                return;
            }

            _saved.Pieces.Add(new Piece { StartedUtc = atUtc });
            Save();
        }

        Changed?.Invoke();
    }

    /// <summary>The stream stopped: the open piece ends.</summary>
    public void Stopped(DateTime atUtc)
    {
        lock (_gate)
        {
            if (_saved.Pieces.Count == 0 || _saved.Pieces[^1].EndedUtc is not null)
            {
                return;
            }

            _saved.Pieces[^1].EndedUtc = atUtc < _saved.Pieces[^1].StartedUtc ? _saved.Pieces[^1].StartedUtc : atUtc;
            Save();
        }

        Changed?.Invoke();
    }

    /// <summary>A YouTube link (or bare id) for piece <paramref name="index"/>. False when it isn't one.</summary>
    public bool SetLink(int index, string link)
    {
        var id = VideoIdFrom(link);

        if (id is null && link.Trim().Length > 0)
        {
            return false;
        }

        lock (_gate)
        {
            if (index < 0 || index >= _saved.Pieces.Count)
            {
                return false;
            }

            _saved.Pieces[index].VideoId = id ?? string.Empty;
            Save();
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>Moves every race in piece <paramref name="index"/> by <paramref name="seconds"/> (V5).</summary>
    public void Nudge(int index, double seconds)
    {
        lock (_gate)
        {
            if (index < 0 || index >= _saved.Pieces.Count)
            {
                return;
            }

            _saved.Pieces[index].NudgeSeconds = Math.Clamp(_saved.Pieces[index].NudgeSeconds + seconds, -600, 600);
            Save();
        }

        Changed?.Invoke();
    }

    /// <summary>Hides one race's video everywhere, or shows it again (V6).</summary>
    public void SetRaceHidden(string sessionId, bool hidden)
    {
        lock (_gate)
        {
            _saved.HiddenRaces.RemoveAll(s => string.Equals(s, sessionId, StringComparison.OrdinalIgnoreCase));

            if (hidden)
            {
                _saved.HiddenRaces.Add(sessionId);
            }

            Save();
        }

        Changed?.Invoke();
    }

    /// <summary>The 11-character id out of any YouTube link - watch?v=, youtu.be/, /live/, /shorts/, /embed/ - or a bare id; null for anything else.</summary>
    public static string? VideoIdFrom(string? link)
    {
        var text = (link ?? string.Empty).Trim();

        if (YouTubeId.IsMatch(text))
        {
            return text;
        }

        if (!Uri.TryCreate(text.Contains("://", StringComparison.Ordinal) ? text : "https://" + text, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();

        if (host is "youtu.be" or "www.youtu.be")
        {
            var first = uri.AbsolutePath.Trim('/').Split('/')[0];
            return YouTubeId.IsMatch(first) ? first : null;
        }

        if (host is not ("youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com"))
        {
            return null;
        }

        foreach (var part in uri.Query.TrimStart('?').Split('&'))
        {
            if (part.StartsWith("v=", StringComparison.Ordinal) && YouTubeId.IsMatch(part[2..]))
            {
                return part[2..];
            }
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/');

        return segments.Length >= 2 && segments[0] is "live" or "shorts" or "embed" && YouTubeId.IsMatch(segments[1]) ? segments[1] : null;
    }

    /// <summary>
    /// The private "stream" document for the relay (relay/src/video.ts):
    /// whether video is on, public results or own nights only, the lead-in,
    /// the pieces, and the races hidden or with a junior in them.
    /// </summary>
    public string Document(AppSettings settings, IEnumerable<string> juniorRaces)
    {
        lock (_gate)
        {
            return JsonSerializer.Serialize(new
            {
                on = settings.VideoEnabled,
                publicResults = settings.VideoOnPublicResults,
                leadIn = Math.Clamp(settings.VideoLeadInSeconds, 0, 30),
                pieces = _saved.Pieces.Select(p => new
                {
                    videoId = p.VideoId,
                    startedUtc = p.StartedUtc.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                    endedUtc = p.EndedUtc?.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                    nudge = p.NudgeSeconds,
                    hidden = p.Hidden,
                }).ToList(),
                hidden = _saved.HiddenRaces.ToList(),
                juniors = juniorRaces.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            });
        }
    }

    private static Piece Copy(Piece p) => new()
    {
        StartedUtc = p.StartedUtc,
        EndedUtc = p.EndedUtc,
        VideoId = p.VideoId,
        NudgeSeconds = p.NudgeSeconds,
        Hidden = p.Hidden,
    };

    private void Save()
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_saved));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception)
        {
            // Kept in memory for tonight; written again on the next change.
        }
    }
}
