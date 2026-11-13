using System.Collections.Concurrent;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// One line the announcer said, and where in the race's recording it began -
/// what a highlight video's captions are made from.
/// </summary>
/// <param name="AtMs">Milliseconds from the start of the recording FILE (the same clock as RaceHighlights.Window).</param>
/// <param name="Text">The words as a screen shows them: no [audio tags].</param>
internal sealed record HeardLine(long AtMs, string Text);

/// <summary>The colours a highlight video is drawn in, read from the club's Look on the UI thread.</summary>
internal sealed record HighlightPalette(
    Color Background,
    Color Surface,
    Color Accent,
    Color Primary,
    Color Text,
    Color Muted,
    Color Good);

/// <summary>
/// Putting each driver's own mp3 clip on their private night page on the
/// relay (22 Oct 2026, driver experience) - gathered on the UI thread at
/// the flag and handed to the thread that cuts the clips. Null unless the
/// club ticked "share drivers' clips" (RaceAudioStore.ShareDriverClips) AND
/// offers drivers their nights.
///
/// BEHIND THE BADGE, NOT PUBLIC. A clip is listed only on that driver's
/// own night, which only their badge code opens, so the relay is told whose
/// it is by badge id (RelayCheckIn.IdFor) and never by transponder or name.
/// A driver with no badge id here gets no upload.
/// </summary>
/// <param name="RaceId">The session's id (SessionRecord.Id, 32 hex), so the night page can put the clip on that race's card. Empty when it is not one.</param>
/// <param name="RaceLabel">The race's own name - "Q1 Heat 2".</param>
/// <param name="DriverIds">Badge ids by transponder - see <see cref="DriverIdsFor"/>.</param>
/// <param name="Publisher">The relay.</param>
internal sealed record ClipShare(
    string RaceId,
    string RaceLabel,
    IReadOnlyDictionary<string, string> DriverIds,
    RelayPublisher Publisher)
{
    /// <summary>
    /// The badge ids for a race's drivers (22 Oct 2026, driver experience),
    /// by transponder. The same rule a driver's night has (MainForm.Relay
    /// PublishNightsFor): a car nobody named gets nothing, as no one has its
    /// badge. Ghosts are not people, and a rental transponder is somebody
    /// else's next week - their badge would open this driver's recording -
    /// so neither gets one.
    /// </summary>
    public static IReadOnlyDictionary<string, string> DriverIdsFor(
        IEnumerable<SessionResultEntry> results,
        Func<string, DriverProfile?> profileOf,
        string? secret)
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(secret))
        {
            return ids;
        }

        foreach (var row in results)
        {
            var transponder = (row?.Transponder ?? string.Empty).Trim();

            if (transponder.Length == 0 || ids.ContainsKey(transponder) || GhostService.IsGhost(transponder))
            {
                continue;
            }

            var profile = profileOf(transponder);

            if (profile is null || profile.IsRental || string.IsNullOrWhiteSpace(profile.DriverName))
            {
                continue;
            }

            // The driver's MAIN transponder, not the row's (22 Oct 2026,
            // security review): a driver out on a second transponder has a
            // row under that one when it was timed as a car of its own, but
            // their badge - the check-in roster's, MainForm.RebuildBadgeIds -
            // is made from profile.Transponder. profileOf (ProfileStore.Get)
            // already resolves an alternate to its owner.
            var main = (profile.Transponder ?? string.Empty).Trim();

            if (main.Length == 0 || GhostService.IsGhost(main))
            {
                continue;
            }

            ids[transponder] = RelayCheckIn.IdFor(main, secret);
        }

        return ids;
    }
}

/// <summary>What a video shows about one driver beyond the result: their photo and number, read from their profile on the UI thread.</summary>
internal sealed record HighlightDriver(string PhotoFile, string Number);

/// <summary>
/// Everything a highlight video needs that the recording does not carry,
/// gathered on the UI thread at the flag (the profiles, the Look, the club's
/// logo) and handed to the background thread that makes the videos.
/// </summary>
/// <param name="Who">Which drivers get one - see <see cref="HighlightVideo.Who"/>.</param>
/// <param name="Handle">The club's social handle for the end card, or empty.</param>
/// <param name="LogoFile">The club logo's file name (AppSettings.RelayLogoFile), or empty.</param>
/// <param name="Palette">The Look's colours.</param>
/// <param name="Drivers">Photo and number by transponder.</param>
/// <remarks>
/// 28 Oct 2026, asked for by Asher ("Remove the podium videos for
/// downloading from the relay and just have locally"): there is no Share
/// any more. Every video stays on this PC, so each one is drawn as a video
/// kept here always was - the drivers' own names and photos, every line
/// the announcer said - and none of the online redactions (public names,
/// withheld captions, a "no photo online" driver drawn blank) apply.
/// </remarks>
internal sealed record HighlightVideoOptions(
    HighlightVideo.Who Who,
    string Handle,
    string LogoFile,
    HighlightPalette Palette,
    IReadOnlyDictionary<string, HighlightDriver> Drivers)
{
    /// <summary>Whether a driver who finished in this position gets a video.</summary>
    public bool Wants(int position) => Who switch
    {
        HighlightVideo.Who.Winner => position == 1,
        HighlightVideo.Who.Podium => position is >= 1 and <= 3,
        HighlightVideo.Who.Everyone => position >= 1,
        _ => false,
    };
}

/// <summary>
/// THE SHAREABLE HIGHLIGHT VIDEO (21 Oct 2026): a driver's race as a short
/// upright video for their phone - the announcer calling them, the race
/// drawn underneath it in the club's Look, and the words on screen.
///
/// Three scenes, under one stretch of audio cut from the race's recording
/// exactly as the driver's mp3 clip is (RaceHighlights): a title card (the
/// club, the race, the driver), the race itself (their position, a pop when
/// they pass somebody, a battle banner, the lap and their last lap, the top
/// of the order, the voice as a waveform), and an end card (the result, the
/// top five, the club's handle).
///
/// EVERYTHING ON SCREEN IS WORKED OUT FROM THE SAVED RESULT, the same record
/// the clips are cut from: positions from each driver's lap times, laid on
/// the recording's clock with the green's offset, as the clips are. Nothing
/// is invented and nothing is a second account of the race.
///
/// THE CAPTIONS are the one thing the record does not hold: what the
/// announcer said, and when. The recorder keeps that for the length of one
/// race (RaceAudioRecorder.NoteLine, from the single point where a line is
/// definitely about to be heard - the one listening-along uses), and it goes
/// with the recording to here. Most social video is watched with the sound
/// off; without the words, the call is lost on most of the people it is for.
///
/// MADE WITH WINDOWS' OWN ENCODER (Media Foundation's sink writer, H.264 and
/// AAC in an mp4), through the interop NAudio already ships - the same
/// system that makes the mp3s, so nothing new is installed at a club. A PC
/// without the H.264 encoder (Windows "N" editions) gets no video and a line
/// in the console saying so; the clip and the race are untouched.
///
/// Run on the recording's own background thread after the race is filed.
/// Videos are made one at a time across races, and one being made pauses
/// between frames while <see cref="RaceLive"/> is set, so the encoding
/// waits out the next race rather than competing with it. A video failing
/// costs the night nothing.
/// </summary>
internal static class HighlightVideo
{
    /// <summary>Which drivers get a video.</summary>
    public enum Who
    {
        Off = 0,
        Winner = 1,
        Podium = 2,
        Everyone = 3,
    }

    public const int Width = 1080;
    public const int Height = 1920;
    public const int FramesPerSecond = 30;

    /// <summary>The longest a video runs. Past it, the earliest stretches go first - the finish is the part worth keeping.</summary>
    public const long MaxLengthMs = 55_000;

    /// <summary>How much of the podium's long tail (RaceHighlights.PodiumTailMs) a video keeps: the finish call and the first of the result, not the whole interview.</summary>
    public const long PodiumKeepMs = 22_000;

    private const long TitleMs = 2500;
    private const long EndCardMs = 4500;

    /// <summary>The newest video made for each driver this session, for the leaderboard drawer's Share video.</summary>
    private static readonly ConcurrentDictionary<string, string> Latest = new(StringComparer.OrdinalIgnoreCase);

    // WHY (22 Oct 2026, bug hunt 9): every race's Publish ran on its own
    // pool thread, so when heats finished faster than their videos, two or
    // three 1080p encodes ran at once - and on into the next live race,
    // which the summary promised never happened. Now one video is made at a
    // time across the whole session (the rest queue, in race order), and a
    // video being made sits still between frames while RaceLive is set.
    private static readonly SemaphoreSlim VideoGate = new(1, 1);

    /// <summary>
    /// Set while a race is on track (true at the green, false when it is
    /// filed or abandoned). A video being made waits between frames until
    /// it clears; the frames' times come from their index, so the file is
    /// the same either way.
    /// </summary>
    public static bool RaceLive
    {
        get => Interlocked.Read(ref _liveSinceMs) != 0;
        set => Interlocked.Exchange(ref _liveSinceMs, value ? Math.Max(1, Environment.TickCount64) : 0);
    }

    // WHY (22 Oct 2026, bug hunt 9 review): the 30 minutes used to be each
    // video's own allowance, so a flag left on by mistake held every
    // queued video for 30 minutes apiece - ten drivers' videos, five hours.
    // Now the flag itself goes stale 30 minutes after the green that set
    // it, so a stuck flag costs the queue 30 minutes once; the next green
    // starts the count again. 0 = no race on track, otherwise
    // Environment.TickCount64 at the green.
    private static long _liveSinceMs;

    /// <summary>How long after the green <see cref="RaceLive"/> is believed - longer than any club race, so a flag left on by mistake does not stall the night's videos.</summary>
    private static readonly TimeSpan LongestPause = TimeSpan.FromMinutes(30);

    // WHY (22 Oct 2026, bug hunt 9): a PC without Windows' H.264/AAC
    // encoder used to repeat the whole attempt - read the wav, build the
    // painter, fail to open the writer - and log three sentences for every
    // driver of every race, about 150 identical lines over a club night.
    // The first refusal is remembered for the session and logged once.
    private static volatile bool _noEncoder;

    /// <summary>True once this PC's video encoder has refused this session - the caller can skip cutting the video's wav.</summary>
    public static bool EncoderUnavailable => _noEncoder;

    /// <summary>Raised on the background thread when a video lands - the driver's name and the file.</summary>
    public static event Action<string, string>? Made;

    /// <summary>
    /// The name a video draws for a car: the result's own name, or the
    /// transponder when nobody named the car. (A podium video bound for the
    /// club's page drew public names instead, from 22 Oct 2026, bug hunt 9,
    /// until the videos stopped going online on 28 Oct 2026.)
    /// </summary>
    internal static string ShownName(string transponder, string? driverName) =>
        string.IsNullOrWhiteSpace(driverName) ? transponder : driverName;

    /// <summary>
    /// How a video's name ends while it is still being written.
    /// </summary>
    // WHY (13 Nov 2026, bug hunt 17): a video was written straight to its
    // own name, so closing the app part way through one left an mp4 in the
    // Highlights folder that looked finished and would not play. Now it is
    // written as "<name>.part.mp4" and renamed only once it is whole, and
    // RaceAudioRecorder.SweepOrphans removes a part left behind. Still ending
    // ".mp4" on purpose: Windows' writer picks the container from the file's
    // extension, and the night folders' tidy-up (RaceAudioStore.
    // DeleteOurOwnFrom) only removes what ends in .mp4.
    internal const string PartEnding = ".part.mp4";

    /// <summary>The name <paramref name="mp4Path"/> is written under until it is finished - see <see cref="PartEnding"/>.</summary>
    internal static string PartPathFor(string mp4Path) =>
        Path.Combine(
            Path.GetDirectoryName(mp4Path) ?? string.Empty,
            Path.GetFileNameWithoutExtension(mp4Path) + PartEnding);

    /// <summary>
    /// Gives a finished video its real name. Tried for a second: Windows can
    /// still be letting go of the file just after the writer has closed it.
    /// Never over a file already there - throws instead, as File.Move does.
    /// </summary>
    internal static void MoveIntoPlace(string partPath, string mp4Path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(partPath, mp4Path);
                return;
            }
            catch (IOException) when (attempt < 5 && File.Exists(partPath) && !File.Exists(mp4Path))
            {
                Thread.Sleep(200);
            }
        }
    }

    /// <summary>The newest video made for a driver this session, or null.</summary>
    public static string? LatestFor(string transponder) =>
        Latest.TryGetValue(transponder, out var path) && File.Exists(path) ? path : null;

    /// <summary>
    /// The stretches of the recording a video plays: the driver's clip, with
    /// the podium's long tail cut down and the whole kept under
    /// <see cref="MaxLengthMs"/> by dropping from the front.
    /// </summary>
    /// <param name="flagFileMs">
    /// Where the flag falls in the recording, in the file's own ms (green
    /// plus RaceHighlights.FlagAtMsFrom), or null when not known. A long
    /// window is cut round it (22 Oct 2026, bug hunt): cut from its start,
    /// a window that began well before the flag - marks merge into the
    /// podium's - lost the finish call entirely.
    /// </param>
    public static List<RaceHighlights.Window> VideoWindows(IReadOnlyList<RaceHighlights.Window> clip, long? flagFileMs = null)
    {
        const long Longest = PodiumKeepMs + RaceHighlights.LeadInMs + RaceHighlights.TailMs;
        const long Kept = PodiumKeepMs + RaceHighlights.LeadInMs;

        RaceHighlights.Window Cut(RaceHighlights.Window w)
        {
            if (w.LengthMs <= Longest)
            {
                return w;
            }

            // The flag inside it: the lead-in before it and the finish after.
            if (flagFileMs is { } flag && flag >= w.StartMs && flag <= w.EndMs)
            {
                return new RaceHighlights.Window(
                    Math.Max(w.StartMs, flag - RaceHighlights.LeadInMs),
                    Math.Min(w.EndMs, flag + PodiumKeepMs));
            }

            // Any other long stretch: its last part, where it comes to a head.
            return new RaceHighlights.Window(w.EndMs - Kept, w.EndMs);
        }

        var windows = clip.Select(Cut)
            .Where(w => w.LengthMs > 0)
            .ToList();

        // Half a second of seam between stretches - see RaceAudioRecorder.CutWav.
        long Total() => windows.Sum(w => w.LengthMs) + Math.Max(0, windows.Count - 1) * 500;

        while (windows.Count > 1 && Total() > MaxLengthMs)
        {
            windows.RemoveAt(0);
        }

        if (windows.Count == 1 && windows[0].LengthMs > MaxLengthMs)
        {
            var only = windows[0];
            windows[0] = new RaceHighlights.Window(only.EndMs - MaxLengthMs, only.EndMs);
        }

        return windows;
    }

    /// <summary>
    /// Makes one driver's video from a wav already cut to
    /// <paramref name="windows"/>. Returns the mp4's path, or null after
    /// saying why in the log.
    /// </summary>
    public static string? Make(
        string audioWavPath,
        string mp4Path,
        IReadOnlyList<RaceHighlights.Window> windows,
        long greenAtMs,
        SessionRecord record,
        string transponder,
        IReadOnlyDictionary<string, long>? releaseOffsetsMs,
        IReadOnlyList<HeardLine> heard,
        HighlightVideoOptions options,
        Action<string> log)
    {
        var row = record.Results.FirstOrDefault(r =>
            string.Equals(r.Transponder, transponder, StringComparison.OrdinalIgnoreCase));

        if (row is null || _noEncoder)
        {
            return null;
        }

        var previousPriority = Thread.CurrentThread.Priority;
        var gateTaken = false;

        // Written here, and renamed to mp4Path once it is whole - see PartEnding.
        var partPath = PartPathFor(mp4Path);

        try
        {
            // Below everything the night is doing: this is a courtesy.
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

            // One video at a time across races (see VideoGate), and none
            // started while a race is on track.
            VideoGate.Wait();
            gateTaken = true;

            WaitOutRace();

            if (_noEncoder)
            {
                // Found missing by the video ahead of this one in the queue.
                return null;
            }

            using var reader = new WaveFileReader(audioWavPath);

            if (reader.WaveFormat.BitsPerSample != 16)
            {
                log($"{AppTime.LogStampNow()}  [video] No video for {row.DriverName}: the recording is not 16-bit audio.");
                return null;
            }

            var pcm = new byte[reader.Length];
            var read = 0;

            while (read < pcm.Length)
            {
                var got = reader.Read(pcm, read, pcm.Length - read);

                if (got <= 0)
                {
                    break;
                }

                read += got;
            }

            var format = reader.WaveFormat;
            var lengthMs = (long)read * 1000 / Math.Max(1, format.AverageBytesPerSecond);

            if (lengthMs < 3000)
            {
                return null;
            }

            // WHY (22 Oct 2026, bug hunt 9): the writer is opened before the
            // timeline, captions and painter are built, so a PC that cannot
            // encode finds out before doing that work - and says so once
            // for the session, not once per driver (see _noEncoder).
            Mp4Writer opened;

            try
            {
                // A part of the same name can only be one an earlier close left.
                TryDeleteMp4(partPath);

                opened = Mp4Writer.Open(partPath, Width, Height, FramesPerSecond, format);
            }
            catch (Exception ex) when (ex is Mp4Writer.NoEncoderException or DllNotFoundException or EntryPointNotFoundException)
            {
                if (!_noEncoder)
                {
                    _noEncoder = true;
                    log($"{AppTime.LogStampNow()}  [video] No highlight videos on this PC this session ({(ex.InnerException ?? ex).Message}): "
                        + "Windows' video encoder refused - usually an \"N\" edition of Windows without the Media Feature Pack. "
                        + "Audio clips are unaffected.");
                }

                TryDeleteMp4(partPath);
                return null;
            }

            using var writer = opened;

            var timeline = new RaceTimeline(record, releaseOffsetsMs);
            // The heard lines as they were said. (23 Oct 2026, bug hunt 10,
            // filtered them for a podium video bound for the club's page;
            // since 28 Oct 2026 no video goes there, so none is filtered.)
            var captions = Captions.Build(heard, windows);
            var envelope = Envelope(pcm, read, format);
            var frames = (int)(lengthMs * FramesPerSecond / 1000);

            using var painter = new Painter(record, row, options, timeline, captions, envelope, windows, greenAtMs, lengthMs);

            var bytesPerFrameOfAudio = format.AverageBytesPerSecond / FramesPerSecond;
            bytesPerFrameOfAudio -= bytesPerFrameOfAudio % format.BlockAlign;
            var audioAt = 0;

            for (var frame = 0; frame < frames; frame++)
            {
                var videoMs = (long)frame * 1000 / FramesPerSecond;

                // A race went green while this was being made: wait it out.
                WaitOutRace();

                writer.WriteFrame(painter.Paint(videoMs), frame);

                // The audio up to the end of this frame, so the two streams
                // reach the writer side by side rather than one ahead.
                var audioEnd = Math.Min(read, (frame + 1) * bytesPerFrameOfAudio);

                if (frame == frames - 1)
                {
                    audioEnd = read;
                }

                if (audioEnd > audioAt)
                {
                    writer.WriteAudio(pcm, audioAt, audioEnd - audioAt);
                    audioAt = audioEnd;
                }
            }

            writer.Finish();

            // Whole now: the file is let go of (a second Dispose, at the end
            // of this block, does nothing) and given its real name.
            writer.Dispose();
            MoveIntoPlace(partPath, mp4Path);

            Latest[transponder] = mp4Path;

            try
            {
                Made?.Invoke(row.DriverName, mp4Path);
            }
            catch
            {
                // A listener's problem, not the video's.
            }

            return mp4Path;
        }
        catch (Exception ex)
        {
            log($"{AppTime.LogStampNow()}  [video] No video for {row.DriverName} ({ex.Message}). "
                + "Their audio clip is unaffected. A PC without Windows' video encoder - an \"N\" edition of Windows "
                + "without the Media Feature Pack - cannot make these.");

            TryDeleteMp4(partPath);
            return null;
        }
        finally
        {
            if (gateTaken)
            {
                VideoGate.Release();
            }

            Thread.CurrentThread.Priority = previousPriority;
        }
    }

    /// <summary>Sleeps while <see cref="RaceLive"/> is set and no older than <see cref="LongestPause"/>.</summary>
    private static void WaitOutRace()
    {
        while (Interlocked.Read(ref _liveSinceMs) is var since
            && since != 0
            && Environment.TickCount64 - since < (long)LongestPause.TotalMilliseconds)
        {
            Thread.Sleep(250);
        }
    }

    private static void TryDeleteMp4(string mp4Path)
    {
        try
        {
            if (File.Exists(mp4Path))
            {
                File.Delete(mp4Path);
            }
        }
        catch
        {
            // Left behind; it plays as far as it got, or not at all.
        }
    }

    /// <summary>How loud the audio is in each frame, 0 to 1 - the waveform under the race scene.</summary>
    private static float[] Envelope(byte[] pcm, int length, WaveFormat format)
    {
        var bytesPerFrame = Math.Max(format.BlockAlign, format.AverageBytesPerSecond / FramesPerSecond);
        bytesPerFrame -= bytesPerFrame % format.BlockAlign;

        var count = Math.Max(1, length / Math.Max(1, bytesPerFrame));
        var levels = new float[count];
        var peak = 0f;

        for (var i = 0; i < count; i++)
        {
            double sum = 0;
            var samples = 0;
            var end = Math.Min(length - 1, (i + 1) * bytesPerFrame);

            for (var at = i * bytesPerFrame; at + 1 <= end; at += 2 * Math.Max(1, format.Channels))
            {
                var s = BitConverter.ToInt16(pcm, at) / 32768.0;
                sum += s * s;
                samples++;
            }

            levels[i] = samples == 0 ? 0 : (float)Math.Sqrt(sum / samples);
            peak = Math.Max(peak, levels[i]);
        }

        if (peak <= 0)
        {
            return levels;
        }

        for (var i = 0; i < count; i++)
        {
            levels[i] = Math.Clamp(levels[i] / peak, 0f, 1f);
        }

        return levels;
    }

    // ---------------------------------------------------------------------
    // Where everyone was, at any moment of the race
    // ---------------------------------------------------------------------

    /// <summary>
    /// The running order at any moment, from each driver's lap times - the
    /// same arithmetic RaceHighlights lays the clips out with.
    /// </summary>
    internal sealed class RaceTimeline
    {
        internal sealed record Car(string Transponder, string Name, int FinalPosition, List<long> Crossings, List<long> LapTimes);

        /// <summary>One car at one moment.</summary>
        internal sealed record Place(Car Car, int Position, int Laps, long LastCrossingMs);

        private readonly List<Car> _cars;

        public RaceTimeline(SessionRecord record, IReadOnlyDictionary<string, long>? releaseOffsetsMs)
        {
            _cars = record.Results
                .Where(r => !string.IsNullOrWhiteSpace(r.Transponder))
                .Select(r => new Car(
                    r.Transponder,
                    ShownName(r.Transponder, r.DriverName),
                    r.Position,
                    RaceHighlights.CrossingTimesMs(r.LapTimesMs, Offset(releaseOffsetsMs, r.Transponder)),
                    r.LapTimesMs.ToList()))
                .ToList();

            FlagAtMs = RaceHighlights.FlagAtMsFrom(record, releaseOffsetsMs);
            FastestMs = _cars
                .SelectMany(c => c.LapTimes.Skip(1))
                .Where(t => t > 0)
                .DefaultIfEmpty(0)
                .Min();
        }

        /// <summary>When the last car was counted, in ms after the green.</summary>
        public long FlagAtMs { get; }

        /// <summary>The race's fastest lap, or 0.</summary>
        public long FastestMs { get; }

        private static long Offset(IReadOnlyDictionary<string, long>? offsets, string transponder) =>
            offsets is not null && offsets.TryGetValue(transponder, out var offset) ? offset : 0;

        /// <summary>The order at <paramref name="raceMs"/> after the green. At and after the flag, the result as filed.</summary>
        public List<Place> At(long raceMs)
        {
            if (raceMs >= FlagAtMs && FlagAtMs > 0)
            {
                return _cars
                    .OrderBy(c => c.FinalPosition <= 0 ? int.MaxValue : c.FinalPosition)
                    .Select((c, i) => new Place(c, i + 1, c.Crossings.Count, c.Crossings.Count > 0 ? c.Crossings[^1] : 0))
                    .ToList();
            }

            return _cars
                .Select(c =>
                {
                    var laps = c.Crossings.Count(t => t <= raceMs);
                    return (Car: c, Laps: laps, Last: laps > 0 ? c.Crossings[laps - 1] : long.MaxValue);
                })
                .OrderByDescending(x => x.Laps)
                .ThenBy(x => x.Last)
                .ThenBy(x => x.Car.FinalPosition <= 0 ? int.MaxValue : x.Car.FinalPosition)
                .Select((x, i) => new Place(x.Car, i + 1, x.Laps, x.Last == long.MaxValue ? 0 : x.Last))
                .ToList();
        }
    }

    // ---------------------------------------------------------------------
    // Captions
    // ---------------------------------------------------------------------

    /// <summary>The words on screen, timed to the VIDEO's clock and broken into a few words at a time.</summary>
    internal static class Captions
    {
        internal sealed record Chunk(long FromMs, long ToMs, string Text);

        /// <summary>About how long a word takes to say, for timing the chunks of a line.</summary>
        private const long PerWordMs = 360;

        /// <summary>A caption's most words at once - two lines on an upright phone.</summary>
        private const int WordsPerChunk = 6;

        /// <summary>Maps the heard lines (recording clock) onto the video, which plays only some stretches of the recording.</summary>
        public static List<Chunk> Build(IReadOnlyList<HeardLine> heard, IReadOnlyList<RaceHighlights.Window> windows)
        {
            var chunks = new List<Chunk>();

            if (heard.Count == 0 || windows.Count == 0)
            {
                return chunks;
            }

            var ordered = heard.OrderBy(h => h.AtMs).ToList();

            for (var i = 0; i < ordered.Count; i++)
            {
                var line = ordered[i];
                var words = line.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (words.Length == 0)
                {
                    continue;
                }

                // Said until the next line starts, or for as long as its
                // words take, whichever is first.
                var spoken = words.Length * PerWordMs + 400;
                var end = i + 1 < ordered.Count ? Math.Min(ordered[i + 1].AtMs, line.AtMs + spoken) : line.AtMs + spoken;
                end = Math.Max(end, line.AtMs + 800);

                var pieces = (words.Length + WordsPerChunk - 1) / WordsPerChunk;
                var span = (end - line.AtMs) / (double)pieces;

                for (var p = 0; p < pieces; p++)
                {
                    var from = line.AtMs + (long)(span * p);
                    var to = line.AtMs + (long)(span * (p + 1));
                    var text = string.Join(' ', words.Skip(p * WordsPerChunk).Take(WordsPerChunk));

                    // Onto the video's clock: only the parts inside a window
                    // the video plays are shown.
                    if (ToVideo(from, windows) is { } videoFrom)
                    {
                        var videoTo = ToVideo(Math.Max(from, to - 1), windows) ?? videoFrom + (to - from);
                        chunks.Add(new Chunk(videoFrom, Math.Max(videoFrom + 300, videoTo), text));
                    }
                }
            }

            return chunks;
        }

        /// <summary>Where a moment of the recording falls in the video, or null when the video does not play it.</summary>
        public static long? ToVideo(long fileMs, IReadOnlyList<RaceHighlights.Window> windows)
        {
            long at = 0;

            foreach (var w in windows)
            {
                if (fileMs >= w.StartMs && fileMs < w.EndMs)
                {
                    return at + (fileMs - w.StartMs);
                }

                at += w.LengthMs + 500;
            }

            return null;
        }

        /// <summary>Where a moment of the video falls in the recording.</summary>
        public static long ToFile(long videoMs, IReadOnlyList<RaceHighlights.Window> windows)
        {
            long at = 0;

            foreach (var w in windows)
            {
                if (videoMs < at + w.LengthMs)
                {
                    return w.StartMs + Math.Max(0, videoMs - at);
                }

                at += w.LengthMs;

                // The half-second seam: held on the end of the window.
                if (videoMs < at + 500)
                {
                    return w.EndMs;
                }

                at += 500;
            }

            return windows.Count > 0 ? windows[^1].EndMs : 0;
        }
    }

    // ---------------------------------------------------------------------
    // The frames
    // ---------------------------------------------------------------------

    /// <summary>Draws each frame of one driver's video. Not thread-safe: one per video, on one thread.</summary>
    private sealed class Painter : IDisposable
    {
        private static readonly Color Gold = Color.FromArgb(245, 197, 24);

        private readonly SessionRecord _record;
        private readonly SessionResultEntry _row;
        private readonly HighlightVideoOptions _options;
        private readonly HighlightPalette _p;
        private readonly RaceTimeline _timeline;
        private readonly List<Captions.Chunk> _captions;
        private readonly float[] _envelope;
        private readonly IReadOnlyList<RaceHighlights.Window> _windows;
        private readonly long _greenAtMs;
        private readonly long _lengthMs;

        private readonly Bitmap _frame = new(Width, Height, PixelFormat.Format32bppRgb);
        private readonly Graphics _g;
        private readonly Image? _logo;
        private readonly Image? _photo;

        private readonly FontFamily _display;
        private readonly FontFamily _body;

        public Painter(
            SessionRecord record,
            SessionResultEntry row,
            HighlightVideoOptions options,
            RaceTimeline timeline,
            List<Captions.Chunk> captions,
            float[] envelope,
            IReadOnlyList<RaceHighlights.Window> windows,
            long greenAtMs,
            long lengthMs)
        {
            _record = record;
            _row = row;
            _options = options;
            _p = options.Palette;
            _timeline = timeline;
            _captions = captions;
            _envelope = envelope;
            _windows = windows;
            _greenAtMs = greenAtMs;
            _lengthMs = lengthMs;

            _g = Graphics.FromImage(_frame);
            _g.SmoothingMode = SmoothingMode.AntiAlias;
            _g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            _g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // System faces rather than the app's own: those belong to the UI
            // thread, and GDI+ objects shared across threads throw.
            _display = Family("Segoe UI Black", "Arial Black", "Segoe UI");
            _body = Family("Segoe UI Semibold", "Segoe UI", "Arial");

            _logo = string.IsNullOrWhiteSpace(options.LogoFile) ? null : ClubLogo.Load(options.LogoFile);

            // WHY (22 Oct 2026, bug hunt 9): this used to copy the photo out
            // of DriverPhotos' shared thumbnail cache at "an odd size the UI
            // never draws" - but nothing kept the UI off 441px (it reaches it
            // under --dpi-aware), and two threads using one GDI+ image throw.
            // Every video's 441px thumbnail also stayed cached all session,
            // used once. The video now reads its own copy, which no other
            // thread ever sees and which Dispose frees (DriverPhotos.OwnedThumb).
            if (options.Drivers.TryGetValue(row.Transponder, out var driver) && !string.IsNullOrWhiteSpace(driver.PhotoFile))
            {
                _photo = DriverPhotos.OwnedThumb(driver.PhotoFile, 441);
            }
        }

        private static FontFamily Family(params string[] names)
        {
            foreach (var name in names)
            {
                try
                {
                    return new FontFamily(name);
                }
                catch (ArgumentException)
                {
                    // Not on this PC - try the next.
                }
            }

            return new FontFamily(GenericFontFamilies.SansSerif);
        }

        public void Dispose()
        {
            _g.Dispose();
            _frame.Dispose();
            _logo?.Dispose();
            _photo?.Dispose();
            _display.Dispose();
            _body.Dispose();
        }

        private string Name => ShownName(_row.Transponder, _row.DriverName);

        private string Number =>
            _options.Drivers.TryGetValue(_row.Transponder, out var d) && !string.IsNullOrWhiteSpace(d.Number) ? d.Number.Trim() : string.Empty;

        public Bitmap Paint(long videoMs)
        {
            var titleMs = _lengthMs >= 9000 ? TitleMs : 1500;
            var endMs = _lengthMs >= 9000 ? EndCardMs : 2500;

            if (videoMs < titleMs)
            {
                TitleCard(videoMs / (float)titleMs);
            }
            else if (videoMs >= _lengthMs - endMs)
            {
                EndCard((videoMs - (_lengthMs - endMs)) / (float)endMs);
            }
            else
            {
                RaceScene(videoMs);
            }

            Caption(videoMs);
            return _frame;
        }

        // --- The title card ------------------------------------------------

        private void TitleCard(float t)
        {
            Backdrop(withFloor: true);

            var y = 150;

            if (_logo is not null)
            {
                var box = Fit(_logo.Size, new Rectangle(Width / 2 - 150, y, 300, 300));
                _g.DrawImage(_logo, box);
                y += 340;
            }
            else
            {
                y += 60;
            }

            Centre(Upper(_record.TrackLayoutName), _body, 40, _p.Primary, y);
            y += 70;

            Centre(Upper(string.IsNullOrWhiteSpace(_record.Label) ? "Race" : _record.Label), _display, 104, _p.Accent, y, glow: true);
            y += 190;

            // The driver slides up into place over the first half second.
            var rise = (int)(80 * (1 - Math.Clamp(t * 3, 0f, 1f)));
            Avatar(new Rectangle(Width / 2 - 230, y + rise, 460, 460), 14);
            y += 520;

            var number = Number;
            Centre(number.Length > 0 ? $"#{number}   {Upper(Name)}" : Upper(Name), _display, 78, _p.Text, y + rise);
            y += 120;

            if (!string.IsNullOrWhiteSpace(_row.CarModel))
            {
                Centre(_row.CarModel, _body, 40, _p.Muted, y + rise);
            }
        }

        // --- The race ------------------------------------------------------

        private void RaceScene(long videoMs)
        {
            Backdrop(withFloor: false);

            var fileMs = Captions.ToFile(videoMs, _windows);
            var raceMs = fileMs - _greenAtMs;
            var order = _timeline.At(raceMs);
            var me = order.FirstOrDefault(p => string.Equals(p.Car.Transponder, _row.Transponder, StringComparison.OrdinalIgnoreCase));
            var before = _timeline.At(raceMs - 1800)
                .FirstOrDefault(p => string.Equals(p.Car.Transponder, _row.Transponder, StringComparison.OrdinalIgnoreCase));
            var finished = _timeline.FlagAtMs > 0 && raceMs >= _timeline.FlagAtMs;

            // Header
            Text(Upper(string.IsNullOrWhiteSpace(_record.Label) ? "Race" : _record.Label) + (finished ? " · FINISH" : " · LIVE"),
                _body, 38, _p.Primary, new Rectangle(70, 80, 760, 60), StringAlignment.Near);
            Pill(new Rectangle(Width - 300, 78, 230, 64), Color.FromArgb(212, 46, 68), "● ON AIR", Color.White);

            if (me is null)
            {
                return;
            }

            // The big position, and the one before it struck through when it changed.
            var moved = before is not null && before.Position != me.Position && before.Laps > 0;
            var pop = moved ? 1f + 0.12f * (float)Math.Sin(Math.Clamp((raceMs % 1800) / 1800.0, 0, 1) * Math.PI) : 1f;
            var colour = me.Position == 1 ? Gold : _p.Text;

            if (moved)
            {
                Text($"P{before!.Position}", _display, 120, _p.Muted, new Rectangle(70, 250, 320, 220), StringAlignment.Center, strike: true);
                Text("→", _body, 110, _p.Primary, new Rectangle(370, 250, 180, 220), StringAlignment.Center);
            }

            Text($"P{me.Position}", _display, 300 * pop, colour,
                new Rectangle(moved ? 520 : 0, 180, moved ? 520 : Width, 380), StringAlignment.Center, glow: me.Position == 1);

            Centre(Moment(me, before, raceMs, finished), _body, 46, _p.Text, 590);

            // A battle: the car in front or behind within a second on the same lap.
            if (!finished && Battle(order, me) is { } battle)
            {
                var band = new Rectangle(70, 690, Width - 140, 150);

                using (var fill = new LinearGradientBrush(band, _p.Accent, Color.FromArgb(176, 107, 255), LinearGradientMode.Horizontal))
                using (var path = Round(band, 26))
                {
                    _g.FillPath(fill, path);
                }

                Text(battle.Title, _body, 34, Color.FromArgb(26, 11, 46), new Rectangle(band.X + 36, band.Y + 18, band.Width - 72, 50), StringAlignment.Near);
                Text(battle.Line, _display, 50, Color.FromArgb(26, 11, 46), new Rectangle(band.X + 36, band.Y + 64, band.Width - 72, 70), StringAlignment.Near);
            }

            // Lap and last lap.
            var lastLap = me.Laps >= 2 && me.Laps - 1 < me.Car.LapTimes.Count ? me.Car.LapTimes[me.Laps - 1] : 0;
            Tile(new Rectangle(70, 880, 450, 220), "LAP", me.Laps.ToString(), _p.Text);
            Tile(new Rectangle(560, 880, 450, 220), "LAST LAP", lastLap > 0 ? $"{lastLap / 1000.0:0.000}" : "—",
                lastLap > 0 && lastLap == _timeline.FastestMs ? _p.Accent : _p.Primary);

            // The top of the order, with this driver in it.
            Standings(order, me, new Rectangle(70, 1140, Width - 140, 290));

            Waveform(videoMs, new Rectangle(70, 1700, Width - 140, 90));

            // Progress through the video.
            var bar = new Rectangle(70, 1810, Width - 140, 12);
            Fill(bar, _p.Surface, 6);
            Fill(new Rectangle(bar.X, bar.Y, (int)(bar.Width * Math.Clamp(videoMs / (double)Math.Max(1, _lengthMs), 0, 1)), bar.Height), _p.Accent, 6);
        }

        /// <summary>What is happening to this driver, in a few words.</summary>
        private string Moment(RaceTimeline.Place me, RaceTimeline.Place? before, long raceMs, bool finished)
        {
            if (finished)
            {
                return me.Position == 1 ? "TAKES THE WIN" : $"FINISHES P{me.Position}";
            }

            if (before is not null && before.Laps > 0 && before.Position != me.Position)
            {
                return me.Position == 1 ? "TAKES THE LEAD"
                    : me.Position < before.Position ? $"UP TO P{me.Position}"
                    : $"DOWN TO P{me.Position}";
            }

            // A lap in the last two seconds that was their best of the race.
            if (me.Laps >= 2 && raceMs - me.LastCrossingMs < 2200)
            {
                var lap = me.Car.LapTimes[me.Laps - 1];
                var best = me.Car.LapTimes.Skip(1).Take(me.Laps - 1).Where(t => t > 0).DefaultIfEmpty(0).Min();

                if (lap > 0 && lap == best)
                {
                    return lap == _timeline.FastestMs ? $"FASTEST LAP · {lap / 1000.0:0.000}" : $"PERSONAL BEST · {lap / 1000.0:0.000}";
                }
            }

            return me.Laps <= 0 ? "AWAY FROM THE GRID" : $"LAP {me.Laps}";
        }

        private static (string Title, string Line)? Battle(List<RaceTimeline.Place> order, RaceTimeline.Place me)
        {
            RaceTimeline.Place? ahead = me.Position >= 2 ? order[me.Position - 2] : null;
            RaceTimeline.Place? behind = me.Position < order.Count ? order[me.Position] : null;

            if (ahead is not null && ahead.Laps == me.Laps && me.Laps > 0)
            {
                var gap = me.LastCrossingMs - ahead.LastCrossingMs;

                if (gap is >= 0 and < 1200)
                {
                    return (me.Position == 2 ? "BATTLE FOR THE LEAD" : $"BATTLE FOR P{ahead.Position}", $"CHASING {Upper(Surname(ahead.Car.Name))} · {gap / 1000.0:0.0}s");
                }
            }

            if (behind is not null && behind.Laps == me.Laps && me.Laps > 0)
            {
                var gap = behind.LastCrossingMs - me.LastCrossingMs;

                if (gap is >= 0 and < 1200)
                {
                    return ($"BATTLE FOR P{me.Position}", $"{Upper(Surname(behind.Car.Name))} RIGHT BEHIND · {gap / 1000.0:0.0}s");
                }
            }

            return null;
        }

        private void Standings(List<RaceTimeline.Place> order, RaceTimeline.Place me, Rectangle box)
        {
            Fill(box, _p.Surface, 26);

            var rows = order.Take(3).ToList();

            if (me.Position > 3)
            {
                rows = order.Take(2).Append(me).ToList();
            }

            var rowHeight = (box.Height - 30) / 3;

            for (var i = 0; i < rows.Count; i++)
            {
                var place = rows[i];
                var r = new Rectangle(box.X + 16, box.Y + 15 + i * rowHeight, box.Width - 32, rowHeight - 6);
                var mine = ReferenceEquals(place, me) || place.Car.Transponder == me.Car.Transponder;

                if (mine)
                {
                    Fill(r, Color.FromArgb(70, _p.Accent), 14);
                    Fill(new Rectangle(r.X, r.Y, 8, r.Height), place.Position == 1 ? Gold : _p.Accent, 4);
                }

                Text(place.Position.ToString(), _display, 40, mine ? Gold : _p.Text, new Rectangle(r.X + 24, r.Y, 70, r.Height), StringAlignment.Near);
                Text(place.Car.Name, _body, 40, _p.Text, new Rectangle(r.X + 100, r.Y, r.Width - 340, r.Height), StringAlignment.Near);

                var leader = order[0];
                var gapText = place.Position == 1 ? "LEADER"
                    : place.Laps < leader.Laps ? $"+{leader.Laps - place.Laps}L"
                    : $"+{Math.Max(0, place.LastCrossingMs - LeaderCrossingAtLap(leader, place.Laps)) / 1000.0:0.0}s";

                Text(gapText, _body, 34, _p.Muted, new Rectangle(r.Right - 230, r.Y, 210, r.Height), StringAlignment.Far);
            }
        }

        private static long LeaderCrossingAtLap(RaceTimeline.Place leader, int laps) =>
            laps >= 1 && laps <= leader.Car.Crossings.Count ? leader.Car.Crossings[laps - 1] : leader.LastCrossingMs;

        private void Waveform(long videoMs, Rectangle box)
        {
            const int bars = 54;
            var frame = (int)(videoMs * FramesPerSecond / 1000);
            var barWidth = box.Width / (float)bars;

            using var lit = new SolidBrush(_p.Primary);
            using var dim = new SolidBrush(Color.FromArgb(90, _p.Primary));

            for (var i = 0; i < bars; i++)
            {
                // The half on the left is what has just been said.
                var at = frame - bars / 2 + i;
                var level = at >= 0 && at < _envelope.Length ? _envelope[at] : 0f;
                var h = Math.Max(6, level * box.Height);

                _g.FillRectangle(i <= bars / 2 ? lit : dim, box.X + i * barWidth + 2, box.Y + (box.Height - h) / 2, barWidth - 4, h);
            }
        }

        // --- The end card --------------------------------------------------

        private void EndCard(float t)
        {
            Backdrop(withFloor: true);

            Centre(Upper((string.IsNullOrWhiteSpace(_record.Label) ? "Race" : _record.Label) + " · Result"), _body, 40, _p.Primary, 120);

            var position = _row.Position;
            Centre(position == 1 ? "WINNER" : $"P{position}", _display, 150, position == 1 ? Gold : _p.Accent, 190, glow: true);
            Centre(Upper(Name), _display, 82, _p.Text, 390);

            var facts = new List<string> { Plural.Of(_row.Laps, "lap") };

            if (_row.FastLapMs > 0)
            {
                facts.Add($"best {_row.FastLapMs / 1000.0:0.000}");
            }

            if (_row.LapsLed > 0)
            {
                facts.Add($"led {Plural.Of(_row.LapsLed, "lap")}");
            }

            Centre(string.Join("  ·  ", facts), _body, 40, _p.Muted, 510);

            var box = new Rectangle(70, 620, Width - 140, 560);
            Fill(box, Color.FromArgb(220, _p.Surface), 30);

            var rows = _record.Results.Where(r => r.Position > 0).OrderBy(r => r.Position).Take(5).ToList();
            var rowHeight = (box.Height - 40) / 5;

            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var line = new Rectangle(box.X + 30, box.Y + 20 + i * rowHeight, box.Width - 60, rowHeight);
                var mine = string.Equals(r.Transponder, _row.Transponder, StringComparison.OrdinalIgnoreCase);
                var ink = mine ? Gold : _p.Text;

                // Rows arrive one after another over the first second.
                if (t * 6 < i * 0.6f)
                {
                    continue;
                }

                Text(r.Position.ToString(), _display, 46, ink, new Rectangle(line.X, line.Y, 80, line.Height), StringAlignment.Near);
                Text(ShownName(r.Transponder, r.DriverName), _body, 46, ink,
                    new Rectangle(line.X + 90, line.Y, line.Width - 340, line.Height), StringAlignment.Near);
                Text(r.FastLapMs > 0 ? $"{r.FastLapMs / 1000.0:0.000}" : "—", _body, 38, _p.Muted,
                    new Rectangle(line.Right - 240, line.Y, 240, line.Height), StringAlignment.Far);
            }

            var handle = (_options.Handle ?? string.Empty).Trim();

            if (handle.Length > 0)
            {
                Centre(handle, _display, 60, _p.Text, 1300);
            }

            Credit(1390);
        }

        /// <summary>
        /// HeatCaller's sign-off under the club's handle (31 Oct 2026, Asher:
        /// the mark is always on everywhere): "Timed and called by" over the
        /// full-colour logo and HEATCALLER, as the relay's share cards have
        /// it. The logo carries its own dark square, so it reads on any look;
        /// HEAT takes the look's text colour and CALLER HeatCaller's magenta,
        /// which reads on a light look and a dark one alike.
        /// </summary>
        private void Credit(int y)
        {
            Centre("Timed and called by", _body, 30, _p.Muted, y);

            using var font = new Font(_display, 48, FontStyle.Regular, GraphicsUnit.Pixel);
            var width = HeatCallerMark.MeasureSignOff(_g, font);

            HeatCallerMark.DrawSignOff(_g, font, new PointF((Width - width) / 2f, y + 52), sun: null, _p.Text, HeatCallerMark.Magenta);
        }

        // --- The captions --------------------------------------------------

        private void Caption(long videoMs)
        {
            var chunk = _captions.LastOrDefault(c => videoMs >= c.FromMs && videoMs < c.ToMs);

            if (chunk is null)
            {
                return;
            }

            using var font = new Font(_display, 64, FontStyle.Regular, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

            var area = new Rectangle(70, 1460, Width - 140, 220);
            var measured = _g.MeasureString(chunk.Text, font, area.Width - 60, format);
            var box = new Rectangle(
                area.X + (int)((area.Width - measured.Width) / 2) - 30,
                area.Y + (int)((area.Height - measured.Height) / 2) - 16,
                (int)measured.Width + 60,
                (int)measured.Height + 32);

            Fill(box, Color.FromArgb(215, 10, 4, 24), 22);

            using var ink = new SolidBrush(Color.White);
            _g.DrawString(chunk.Text, font, ink, new RectangleF(box.X + 10, box.Y, box.Width - 20, box.Height), format);
        }

        // --- Drawing helpers -----------------------------------------------

        private void Backdrop(bool withFloor)
        {
            var all = new Rectangle(0, 0, Width, Height);

            using (var sky = new LinearGradientBrush(all, Darken(_p.Background, 0.15f), Mix(_p.Background, _p.Accent, 0.22f), LinearGradientMode.Vertical))
            {
                _g.FillRectangle(sky, all);
            }

            if (!withFloor)
            {
                return;
            }

            // A perspective grid floor - the Look's accent, faint.
            using var line = new Pen(Color.FromArgb(70, _p.Accent), 3);
            var horizon = 1250;
            var vanishX = Width / 2;

            for (var i = -12; i <= 12; i++)
            {
                _g.DrawLine(line, vanishX, horizon, vanishX + i * 180, Height);
            }

            for (var k = 1; k <= 12; k++)
            {
                var y = horizon + (int)((Height - horizon) * Math.Pow(k / 12.0, 2));
                _g.DrawLine(line, 0, y, Width, y);
            }
        }

        private void Avatar(Rectangle bounds, int ring)
        {
            using (var ringPen = new Pen(_p.Primary, ring))
            {
                _g.DrawEllipse(ringPen, Rectangle.Inflate(bounds, ring / 2, ring / 2));
            }

            using var circle = new GraphicsPath();
            circle.AddEllipse(bounds);

            if (_photo is not null)
            {
                var state = _g.Save();
                _g.SetClip(circle);
                _g.DrawImage(_photo, bounds);
                _g.Restore(state);
                return;
            }

            using (var fill = new SolidBrush(DriverPhotos.ColourFor(Name)))
            {
                _g.FillPath(fill, circle);
            }

            Text(DriverPhotos.InitialsFor(Name), _display, bounds.Height * 0.36f, Color.White, bounds, StringAlignment.Center);
        }

        private void Tile(Rectangle box, string caption, string value, Color valueColour)
        {
            Fill(box, _p.Surface, 26);
            Text(caption, _body, 34, _p.Muted, new Rectangle(box.X, box.Y + 18, box.Width, 50), StringAlignment.Center);
            Text(value, _display, 110, valueColour, new Rectangle(box.X, box.Y + 60, box.Width, box.Height - 70), StringAlignment.Center);
        }

        private void Pill(Rectangle box, Color fill, string text, Color ink)
        {
            Fill(box, fill, box.Height / 2);
            Text(text, _body, 30, ink, box, StringAlignment.Center);
        }

        private void Fill(Rectangle box, Color colour, int radius)
        {
            if (box.Width <= 0 || box.Height <= 0)
            {
                return;
            }

            using var brush = new SolidBrush(colour);
            using var path = Round(box, radius);
            _g.FillPath(brush, path);
        }

        private void Centre(string text, FontFamily family, float size, Color colour, int y, bool glow = false) =>
            Text(text, family, size, colour, new Rectangle(40, y, Width - 80, (int)(size * 1.5f)), StringAlignment.Center, glow: glow);

        private void Text(string text, FontFamily family, float size, Color colour, Rectangle box, StringAlignment align, bool glow = false, bool strike = false)
        {
            if (string.IsNullOrEmpty(text) || box.Width <= 0 || box.Height <= 0)
            {
                return;
            }

            var style = strike ? FontStyle.Strikeout : FontStyle.Regular;

            // Shrunk until it fits the box on one line, so a long name is never cut off.
            var fitted = size;

            using (var probe = new Font(family, fitted, style, GraphicsUnit.Pixel))
            {
                var width = _g.MeasureString(text, probe).Width;

                if (width > box.Width)
                {
                    fitted = Math.Max(18, size * box.Width / width * 0.97f);
                }
            }

            using var font = new Font(family, fitted, style, GraphicsUnit.Pixel);
            using var format = new StringFormat
            {
                Alignment = align,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
            };

            if (glow)
            {
                using var halo = new SolidBrush(Color.FromArgb(60, colour));

                for (var d = 6; d >= 2; d -= 2)
                {
                    _g.DrawString(text, font, halo, new RectangleF(box.X - d, box.Y, box.Width, box.Height), format);
                    _g.DrawString(text, font, halo, new RectangleF(box.X + d, box.Y, box.Width, box.Height), format);
                }
            }

            using var brush = new SolidBrush(colour);
            _g.DrawString(text, font, brush, box, format);
        }

        private static GraphicsPath Round(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            var d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));

            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            return path;
        }

        private static Rectangle Fit(Size image, Rectangle box)
        {
            var scale = Math.Min(box.Width / (float)Math.Max(1, image.Width), box.Height / (float)Math.Max(1, image.Height));
            var w = (int)(image.Width * scale);
            var h = (int)(image.Height * scale);

            return new Rectangle(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
        }

        private static Color Mix(Color a, Color b, float amount) => Color.FromArgb(
            (int)(a.R + (b.R - a.R) * amount),
            (int)(a.G + (b.G - a.G) * amount),
            (int)(a.B + (b.B - a.B) * amount));

        private static Color Darken(Color c, float amount) => Mix(c, Color.Black, amount);

        private static string Upper(string? text) => (text ?? string.Empty).Trim().ToUpperInvariant();

        private static string Surname(string name)
        {
            // The whole name when the last word is only an initial - a public
            // name shortened to "Samuel H" - or for "Unnamed driver" (22 Oct 2026).
            var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 1 && parts[^1].Length > 1 && name != RelayDocuments.UnnamedDriver ? parts[^1] : name;
        }
    }

    // ---------------------------------------------------------------------
    // The mp4
    // ---------------------------------------------------------------------

    /// <summary>
    /// H.264 video and AAC audio into an mp4, through Media Foundation's
    /// sink writer. The interfaces are NAudio's (NAudio.MediaFoundation);
    /// the few creation calls NAudio keeps internal are declared here.
    /// The attribute and format ids are Windows' own, from mfapi.h.
    /// </summary>
    private sealed class Mp4Writer : IDisposable
    {
        private static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        private static readonly Guid Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        private static readonly Guid AvgBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
        private static readonly Guid InterlaceMode = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        private static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
        private static readonly Guid FrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        private static readonly Guid PixelAspectRatio = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
        private static readonly Guid DefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        private static readonly Guid AllSamplesIndependent = new("c9173739-5e56-461c-b713-46fb995cb95f");
        private static readonly Guid AudioChannels = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
        private static readonly Guid AudioSamplesPerSecond = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
        private static readonly Guid AudioAvgBytesPerSecond = new("1aab75c8-cfef-451c-ab95-ac034b8e1731");
        private static readonly Guid AudioBlockAlignment = new("322de230-9eeb-43bd-ab7a-ff412251541d");
        private static readonly Guid AudioBitsPerSample = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");

        private static readonly Guid MediaTypeVideo = new("73646976-0000-0010-8000-00aa00389b71");
        private static readonly Guid MediaTypeAudio = new("73647561-0000-0010-8000-00aa00389b71");
        private static readonly Guid VideoH264 = new("34363248-0000-0010-8000-00aa00389b71");
        private static readonly Guid VideoRgb32 = new("00000016-0000-0010-8000-00aa00389b71");
        private static readonly Guid AudioAac = new("00001610-0000-0010-8000-00aa00389b71");
        private static readonly Guid AudioPcm = new("00000001-0000-0010-8000-00aa00389b71");

        private const int ProgressiveVideo = 2;
        private const int VideoBitrate = 6_000_000;

        [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
        private static extern void MFCreateMediaType(out IMFMediaType mediaType);

        [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
        private static extern void MFCreateSample(out IMFSample sample);

        [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
        private static extern void MFCreateMemoryBuffer(int maxLength, out IMFMediaBuffer buffer);

        private readonly IMFSinkWriter _writer;
        private readonly int _video;
        private readonly int _audio;
        private readonly int _width;
        private readonly int _height;
        private readonly long _frameDuration;
        private readonly WaveFormat _audioFormat;
        private readonly byte[] _frameBytes;
        private long _audioBytesWritten;
        private bool _finished;

        private Mp4Writer(IMFSinkWriter writer, int video, int audio, int width, int height, int fps, WaveFormat audioFormat)
        {
            _writer = writer;
            _video = video;
            _audio = audio;
            _width = width;
            _height = height;
            _frameDuration = 10_000_000L / fps;
            _audioFormat = audioFormat;
            _frameBytes = new byte[width * height * 4];
        }

        /// <summary>
        /// Thrown by <see cref="Open"/> when Windows will not set up the
        /// H.264 or AAC encoder - a property of the PC, not of one video.
        /// A file that cannot be created is not this.
        /// </summary>
        internal sealed class NoEncoderException(Exception inner)
            : Exception(inner.Message, inner);

        public static Mp4Writer Open(string path, int width, int height, int fps, WaveFormat audioFormat)
        {
            MediaFoundationApi.Startup();

            MediaFoundationInterop.MFCreateSinkWriterFromURL(path, null!, null!, out var writer);

            // WHY (22 Oct 2026, bug hunt 9): the four media types were
            // released only when everything succeeded, so a refusal part way
            // left them to the finalizer. They are released either way now.
            IMFMediaType? videoOut = null, videoIn = null, audioOut = null, audioIn = null;

            try
            {
                int video, audio;

                try
                {
                    // Video: H.264 out, RGB32 in.
                    MFCreateMediaType(out videoOut);
                    videoOut.SetGUID(MajorType, MediaTypeVideo);
                    videoOut.SetGUID(Subtype, VideoH264);
                    videoOut.SetUINT32(AvgBitrate, VideoBitrate);
                    videoOut.SetUINT32(InterlaceMode, ProgressiveVideo);
                    videoOut.SetUINT64(FrameSize, Pack(width, height));
                    videoOut.SetUINT64(FrameRate, Pack(fps, 1));
                    videoOut.SetUINT64(PixelAspectRatio, Pack(1, 1));
                    writer.AddStream(videoOut, out video);

                    MFCreateMediaType(out videoIn);
                    videoIn.SetGUID(MajorType, MediaTypeVideo);
                    videoIn.SetGUID(Subtype, VideoRgb32);
                    videoIn.SetUINT32(InterlaceMode, ProgressiveVideo);
                    videoIn.SetUINT64(FrameSize, Pack(width, height));
                    videoIn.SetUINT64(FrameRate, Pack(fps, 1));
                    videoIn.SetUINT64(PixelAspectRatio, Pack(1, 1));

                    // Positive: the rows run top to bottom, as a Bitmap's do.
                    videoIn.SetUINT32(DefaultStride, width * 4);
                    videoIn.SetUINT32(AllSamplesIndependent, 1);
                    writer.SetInputMediaType(video, videoIn, null!);

                    // Audio: AAC out, the recording's own 16-bit PCM in.
                    MFCreateMediaType(out audioOut);
                    audioOut.SetGUID(MajorType, MediaTypeAudio);
                    audioOut.SetGUID(Subtype, AudioAac);
                    audioOut.SetUINT32(AudioBitsPerSample, 16);
                    audioOut.SetUINT32(AudioSamplesPerSecond, audioFormat.SampleRate);
                    audioOut.SetUINT32(AudioChannels, audioFormat.Channels);

                    // 128kbps - one of the four rates Windows' AAC encoder offers.
                    audioOut.SetUINT32(AudioAvgBytesPerSecond, 16000);
                    writer.AddStream(audioOut, out audio);

                    MFCreateMediaType(out audioIn);
                    audioIn.SetGUID(MajorType, MediaTypeAudio);
                    audioIn.SetGUID(Subtype, AudioPcm);
                    audioIn.SetUINT32(AudioBitsPerSample, 16);
                    audioIn.SetUINT32(AudioSamplesPerSecond, audioFormat.SampleRate);
                    audioIn.SetUINT32(AudioChannels, audioFormat.Channels);
                    audioIn.SetUINT32(AudioBlockAlignment, audioFormat.BlockAlign);
                    audioIn.SetUINT32(AudioAvgBytesPerSecond, audioFormat.AverageBytesPerSecond);
                    writer.SetInputMediaType(audio, audioIn, null!);
                }
                catch (COMException ex)
                {
                    // The sink writer exists, so the file was fine: it is
                    // the encoders Windows would not give it.
                    throw new NoEncoderException(ex);
                }

                writer.BeginWriting();

                return new Mp4Writer(writer, video, audio, width, height, fps, audioFormat);
            }
            catch
            {
                Marshal.ReleaseComObject(writer);
                throw;
            }
            finally
            {
                foreach (var type in new[] { videoOut, videoIn, audioOut, audioIn })
                {
                    if (type is not null)
                    {
                        Marshal.ReleaseComObject(type);
                    }
                }
            }
        }

        private static long Pack(int high, int low) => ((long)high << 32) | (uint)low;

        public void WriteFrame(Bitmap frame, int index)
        {
            var bits = frame.LockBits(new Rectangle(0, 0, _width, _height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);

            try
            {
                var rowBytes = _width * 4;

                for (var y = 0; y < _height; y++)
                {
                    Marshal.Copy(bits.Scan0 + y * bits.Stride, _frameBytes, y * rowBytes, rowBytes);
                }
            }
            finally
            {
                frame.UnlockBits(bits);
            }

            WriteSample(_video, _frameBytes, 0, _frameBytes.Length, index * _frameDuration, _frameDuration);
        }

        public void WriteAudio(byte[] pcm, int offset, int count)
        {
            if (count <= 0)
            {
                return;
            }

            var time = _audioBytesWritten * 10_000_000L / _audioFormat.AverageBytesPerSecond;
            var duration = (long)count * 10_000_000L / _audioFormat.AverageBytesPerSecond;

            WriteSample(_audio, pcm, offset, count, time, duration);
            _audioBytesWritten += count;
        }

        private void WriteSample(int stream, byte[] data, int offset, int count, long time, long duration)
        {
            MFCreateMemoryBuffer(count, out var buffer);

            try
            {
                buffer.Lock(out var pointer, out _, out _);

                try
                {
                    Marshal.Copy(data, offset, pointer, count);
                }
                finally
                {
                    buffer.Unlock();
                }

                buffer.SetCurrentLength(count);

                MFCreateSample(out var sample);

                try
                {
                    sample.AddBuffer(buffer);
                    sample.SetSampleTime(time);
                    sample.SetSampleDuration(duration);
                    _writer.WriteSample(stream, sample);
                }
                finally
                {
                    Marshal.ReleaseComObject(sample);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(buffer);
            }
        }

        public void Finish()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            _writer.DoFinalize();
        }

        public void Dispose()
        {
            try
            {
                Marshal.ReleaseComObject(_writer);
            }
            catch
            {
                // Already gone.
            }
        }
    }
}
