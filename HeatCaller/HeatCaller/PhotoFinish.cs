using System.Globalization;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// The webcam photo finish: a camera pointed at the line, and the frames
/// either side of each car taking the flag kept, so a finish too close to
/// call - or a car the decoder may have missed - can be looked at
/// afterwards.
///
/// The camera is a page (/camera) opened in a browser on this laptop: the
/// browser has the webcam, keeps the last few seconds of it in memory, and
/// asks this class every quarter-second which moments it should keep. When
/// a car takes the flag a shot is asked for at that moment (by the wall
/// clock - the page and the app are on the same PC, so they share it), and
/// the page sends back the frames from a second before to half a
/// second after. They are saved under photo-finish\, a folder a night and
/// one a shot, and shown in the Photo Finish window.
///
/// Why a browser page rather than the camera opened here: the browser
/// already knows how to talk to every webcam there is, and there is then no
/// camera code in the app to go wrong on race night. Opening a camera needs
/// a secure page, which "localhost" is and the club Wi-Fi address is not,
/// so the camera endpoints answer only this PC.
///
/// A shot is asked for at every crossing of a real car, marked as a finish
/// when the car is taking the flag (IsFinishCrossing) - including the
/// crossing that brought the flag out. The camera page keeps the finishes,
/// or every crossing when its own tick box says so.
///
/// Requests and frames arrive on the web server's threads, crossings on the
/// UI thread: everything here is under one lock.
/// </summary>
public sealed class PhotoFinish
{
    /// <summary>The most shots kept waiting for the camera - a page that stops asking does not grow this forever.</summary>
    internal const int ShotsKept = 300;

    /// <summary>The largest frame accepted, in bytes. A 1080p JPEG is well under this.</summary>
    internal const int LargestFrame = 2_000_000;

    /// <summary>How long after its last poll the camera page still counts as connected.</summary>
    internal static readonly TimeSpan CameraQuietAfter = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How many race nights of frames are kept on disk - tonight's always
    /// among them. See PruneOldNights.
    /// </summary>
    internal const int KeepNights = 6;

    /// <summary>One prune at a time, across every PhotoFinish there is - <c>_diskGate</c> is only this one's.</summary>
    private static readonly object PruneGate = new();

    /// <summary>
    /// One moment worth a picture.
    /// </summary>
    /// <param name="AtUnixMs">When the crossing was counted, in Unix milliseconds - the clock the camera page stamps its frames with.</param>
    /// <param name="Finish">Crossed under the checkered flag. False for an ordinary lap, which the camera only keeps when told to.</param>
    public sealed record Shot(int Id, long AtUnixMs, string Transponder, string Name, int Position, int Lap, bool Finish);

    private readonly object _gate = new();
    private readonly object _diskGate = new();
    private readonly List<Shot> _shots = new();
    private readonly Dictionary<int, string> _folders = new();
    private int _nextId = 1;

    /// <summary>1 once this run has started pruning the old nights - see SaveFrame.</summary>
    private int _pruneStarted;

    /// <summary>This run of the app, for the camera page: a new one means the shots are numbered from 1 again.</summary>
    private readonly string _run = Guid.NewGuid().ToString("N")[..8];
    private DateTime _lastPollUtc = DateTime.MinValue;

    /// <summary>Where shots are kept: a folder a night under the app's data folder.</summary>
    public static string Root => Path.Combine(AppPaths.Root, FolderName);

    /// <summary>The name of <see cref="Root"/> in the data folder. Backups leave it out (see BackupService.IsLeftOutOfBackup).</summary>
    internal const string FolderName = "photo-finish";

    /// <summary>Said once a shot is saved, for the console. Any thread.</summary>
    public event Action<string>? Saved;

    /// <summary>A driver's name for a shot, from the transponder. Set by the host.</summary>
    public Func<string, string>? NameOf { get; set; }

    /// <summary>Whether the camera page has asked for shots in the last few seconds.</summary>
    public bool CameraConnected
    {
        get
        {
            lock (_gate)
            {
                return DateTime.UtcNow - _lastPollUtc < CameraQuietAfter;
            }
        }
    }

    /// <summary>Follows <paramref name="race"/>: every counted crossing of a real car. UI thread.</summary>
    public void Attach(RaceTiming race)
    {
        race.LapProcessed += (standing, valid, _) =>
        {
            if (!valid || race.IsDemoMode || race.IsPracticeMode || GhostService.IsGhost(standing.TransponderId))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var finish = IsFinishCrossing(race.IsFinishing, race.Flag, race.LapLimit, standing.Laps);
            Crossed(standing.TransponderId, PositionOf(race, standing), standing.Laps, now, finish);
        };
    }

    /// <summary>
    /// Whether a crossing is a car taking the flag: one while the race is
    /// finishing (the checkered is out, even with a yellow over it), or the
    /// one that reaches the lap limit - which is counted a moment before it
    /// puts the flag out, so the flag isn't out yet when it is asked.
    /// </summary>
    internal static bool IsFinishCrossing(bool finishing, FlagState flag, int? lapLimit, int laps) =>
        finishing || flag == FlagState.Checkered || (lapLimit is { } limit && limit > 0 && laps >= limit);

    private static int PositionOf(RaceTiming race, RacerStanding? standing)
    {
        if (standing is null)
        {
            return 0;
        }

        var ordered = race.GetOrderedStandings();

        for (var i = 0; i < ordered.Count; i++)
        {
            if (ReferenceEquals(ordered[i], standing) || ordered[i].TransponderId == standing.TransponderId)
            {
                return i + 1;
            }
        }

        return 0;
    }

    /// <summary>A car crossed the line at <paramref name="atUnixMs"/>: a shot, a finish one when <paramref name="finish"/>.</summary>
    internal Shot Crossed(string transponder, int position, int lap, long atUnixMs, bool finish)
    {
        lock (_gate)
        {
            return Add(atUnixMs, transponder, position, lap, finish);
        }
    }

    private Shot Add(long atUnixMs, string transponder, int position, int lap, bool finish)
    {
        string name;

        try
        {
            name = NameOf?.Invoke(transponder) ?? transponder;
        }
        catch
        {
            name = transponder;
        }

        var shot = new Shot(_nextId++, atUnixMs, transponder, string.IsNullOrWhiteSpace(name) ? transponder : name, position, lap, finish);
        _shots.Add(shot);

        if (_shots.Count > ShotsKept)
        {
            foreach (var gone in _shots.Take(_shots.Count - ShotsKept))
            {
                _folders.Remove(gone.Id);
            }

            _shots.RemoveRange(0, _shots.Count - ShotsKept);
        }

        return shot;
    }

    /// <summary>The shots asked for after <paramref name="afterId"/> - the camera page's poll. Web server thread.</summary>
    public IReadOnlyList<Shot> After(int afterId)
    {
        lock (_gate)
        {
            _lastPollUtc = DateTime.UtcNow;
            return _shots.Where(s => s.Id > afterId).ToList();
        }
    }

    /// <summary>The poll's answer as JSON: the shots, and the newest id so a page opened late starts from now.</summary>
    public string AfterJson(int afterId)
    {
        var shots = After(afterId);
        int latest;

        lock (_gate)
        {
            latest = _nextId - 1;
        }

        return JsonSerializer.Serialize(new
        {
            run = _run,
            latest,
            shots = shots.Select(s => new { id = s.Id, at = s.AtUnixMs, name = s.Name, pos = s.Position, lap = s.Lap, finish = s.Finish }),
        });
    }

    /// <summary>Whether <paramref name="bytes"/> is a JPEG of a size worth keeping.</summary>
    internal static bool LooksLikeJpeg(byte[] bytes) =>
        bytes.Length is > 3 and <= LargestFrame && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;

    /// <summary>
    /// Keeps one frame of shot <paramref name="shotId"/>, taken
    /// <paramref name="offsetMs"/> from the moment of the crossing. False
    /// when there is no such shot or the frame is not a JPEG. Web server
    /// thread.
    /// </summary>
    public bool SaveFrame(int shotId, int offsetMs, byte[] jpeg)
    {
        if (!LooksLikeJpeg(jpeg) || Math.Abs(offsetMs) > 5_000)
        {
            return false;
        }

        string folder;
        Shot shot;

        lock (_gate)
        {
            var found = _shots.FirstOrDefault(s => s.Id == shotId);

            if (found is null)
            {
                return false;
            }

            shot = found;
            folder = _folders.TryGetValue(shotId, out var known) ? known : FolderFor(Root, shot);
            _folders[shotId] = folder;
        }

        string? said = null;

        // One write at a time: a second camera tab posts the same frames,
        // and two writers on one file fail each other.
        lock (_diskGate)
        {
            Directory.CreateDirectory(folder);

            // The frame first, then the details: the Photo Finish window
            // counts shots by their details, so a shot it counts always has
            // a frame to show. Written with the first frame - or a later one,
            // if writing them failed the first time: without them the shot is
            // never listed.
            WriteAside(Path.Combine(folder, FrameName(offsetMs)), jpeg);

            var details = Path.Combine(folder, "shot.json");

            if (!File.Exists(details))
            {
                var at = DateTimeOffset.FromUnixTimeMilliseconds(shot.AtUnixMs).UtcDateTime;
                var info = new SavedShot(folder, shot.Name, shot.Transponder, shot.Position, shot.Lap, shot.Finish, at);
                WriteAside(details, System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(info)));

                said = shot.Finish
                    ? $"[camera] Photo finish kept for {shot.Name} (P{shot.Position}, lap {shot.Lap}) - Connection → Photo Finish to look."
                    : $"[camera] Crossing kept for {shot.Name} (lap {shot.Lap}).";
            }
        }

        if (said is not null)
        {
            Saved?.Invoke(said);

            // WHY (20 Oct 2026, Bug Hunt 8, #16): frames were written here
            // every race night and nothing ever deleted them - a season of
            // finishes (gigabytes a night with "Keep every crossing") filled
            // the laptop's disk, and then race history and settings stopped
            // saving. Now the first shot this run kept also clears out the
            // nights older than the last KeepNights, once, in the background
            // so the web server thread isn't held up by it. Started from
            // here rather than at startup so it only runs when the camera is
            // in use, which is the only time this folder grows.
            if (Interlocked.Exchange(ref _pruneStarted, 1) == 0)
            {
                _ = Task.Run(() => PruneOldNights(line => Saved?.Invoke(line)));
            }
        }

        return true;
    }

    /// <summary>
    /// Deletes the nights of frames older than the newest
    /// <see cref="KeepNights"/> under <see cref="Root"/>. Never throws. Any thread.
    /// </summary>
    public static void PruneOldNights(Action<string>? log = null) =>
        PruneOldNights(Root, DateTime.Now, KeepNights, log);

    /// <summary>
    /// Deletes the night folders under <paramref name="root"/> after the
    /// newest <paramref name="keepNights"/>. Only folders named as a night
    /// ("2026-10-03") are counted or touched, so a folder someone put there
    /// by hand is left alone, and tonight's night (by
    /// <paramref name="nowLocal"/>) or anything newer is never deleted - the
    /// camera may be writing into it. A night that can't be deleted (a frame
    /// open in the Photo Finish window) is said and skipped, and tried again
    /// next run. Returns how many nights went. Never throws.
    /// </summary>
    internal static int PruneOldNights(string root, DateTime nowLocal, int keepNights, Action<string>? log = null)
    {
        lock (PruneGate)
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    return 0;
                }

                var tonight = TonightAtTheTrack.NightOf(nowLocal).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                var old = Directory.GetDirectories(root)
                    .Select(folder => (Folder: folder, Name: Path.GetFileName(folder)))
                    .Where(one => DateTime.TryParseExact(one.Name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    .OrderByDescending(one => one.Name, StringComparer.Ordinal)
                    .Skip(Math.Max(1, keepNights))
                    .Where(one => string.CompareOrdinal(one.Name, tonight) < 0)
                    .ToList();

                var gone = 0;

                foreach (var (folder, name) in old)
                {
                    try
                    {
                        Directory.Delete(folder, recursive: true);
                        gone++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        log?.Invoke($"[camera] Couldn't clear out the photo finish frames from {name} ({ex.Message}) - tried again next time.");
                    }
                }

                if (gone > 0)
                {
                    log?.Invoke($"[camera] Cleared out {gone} old night{(gone == 1 ? string.Empty : "s")} of photo finish frames - the last {Math.Max(1, keepNights)} are kept.");
                }

                return gone;
            }
            catch (Exception ex)
            {
                // Listing the folder failed: nothing deleted, and nothing
                // here is worth stopping the camera for.
                log?.Invoke($"[camera] Couldn't check for old photo finish frames ({ex.Message}).");
                return 0;
            }
        }
    }

    /// <summary>Written aside and moved into place, so the Photo Finish window never reads a file half-written.</summary>
    private static void WriteAside(string path, byte[] bytes)
    {
        var aside = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";

        try
        {
            File.WriteAllBytes(aside, bytes);
            File.Move(aside, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(aside))
            {
                File.Delete(aside);
            }
        }
    }

    /// <summary>"photo-finish\2026-10-03\21-14-05.123 P1 Sam Smith" - by the night it was, the time and who.</summary>
    internal static string FolderFor(string root, Shot shot)
    {
        var local = DateTimeOffset.FromUnixTimeMilliseconds(shot.AtUnixMs).LocalDateTime;
        var night = TonightAtTheTrack.NightOf(local).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var safe = new string(shot.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();

        if (safe.Length > 40)
        {
            safe = safe[..40].Trim();
        }

        var place = shot.Position > 0 ? $"P{shot.Position} " : string.Empty;
        return Path.Combine(root, night, $"{local:HH-mm-ss.fff} {place}{safe} #{shot.Id}");
    }

    /// <summary>"-0400.jpg", "+0120.jpg" - sorted by name, the frames are in time order.</summary>
    internal static string FrameName(int offsetMs) =>
        (offsetMs < 0 ? "-" : "+") + Math.Abs(offsetMs).ToString("0000", CultureInfo.InvariantCulture) + ".jpg";

    /// <summary>The offset a frame was taken at, from its file name, or null for a file that isn't a frame.</summary>
    internal static int? OffsetOf(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);

        if (name.Length < 2 || name[0] is not ('-' or '+')
            || !int.TryParse(name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var ms))
        {
            return null;
        }

        return name[0] == '-' ? -ms : ms;
    }

    /// <summary>A shot on disk, as the Photo Finish window lists it.</summary>
    public sealed record SavedShot(string Folder, string Name, string Transponder, int Position, int Lap, bool Finish, DateTime AtUtc)
    {
        /// <summary>The frames, in time order, with the offset each was taken at.</summary>
        public IReadOnlyList<(int OffsetMs, string File)> Frames()
        {
            var frames = new List<(int OffsetMs, string File)>();

            if (!Directory.Exists(Folder))
            {
                return frames;
            }

            foreach (var file in Directory.GetFiles(Folder, "*.jpg"))
            {
                if (OffsetOf(file) is { } offset)
                {
                    frames.Add((offset, file));
                }
            }

            return frames.OrderBy(f => f.OffsetMs).ToList();
        }

        /// <summary>"21:14:05.123  P1  Sam Smith  lap 12", as listed.</summary>
        public string Title =>
            $"{AtUtc.ToLocalTime():HH:mm:ss.fff}  {(Position > 0 ? $"P{Position}  " : string.Empty)}{Name}  lap {Lap}{(Finish ? string.Empty : "  (lap)")}";
    }

    /// <summary>The nights with shots kept, newest first.</summary>
    public static IReadOnlyList<string> Nights() =>
        Directory.Exists(Root)
            ? Directory.GetDirectories(Root).Select(Path.GetFileName).OfType<string>().OrderByDescending(n => n, StringComparer.Ordinal).ToList()
            : Array.Empty<string>();

    /// <summary>The shots kept on <paramref name="night"/> ("2026-10-03"), in the order they were taken. A shot whose details can't be read is left out.</summary>
    public static IReadOnlyList<SavedShot> ShotsOn(string night)
    {
        var folder = Path.Combine(Root, night);

        if (!Directory.Exists(folder))
        {
            return Array.Empty<SavedShot>();
        }

        var list = new List<SavedShot>();

        foreach (var dir in Directory.GetDirectories(folder))
        {
            try
            {
                if (JsonSerializer.Deserialize<SavedShot>(File.ReadAllText(Path.Combine(dir, "shot.json"))) is { } shot)
                {
                    list.Add(shot with { Folder = dir });
                }
            }
            catch
            {
                // Half-written or hand-edited: not listed.
            }
        }

        return list.OrderBy(shot => shot.AtUtc).ToList();
    }
}
