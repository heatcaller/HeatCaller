using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// A few seconds of a driver's own race audio, played inside the piece
/// about their night.
///
/// WHAT THIS FINISHES (13 Oct 2026). `RaceHighlights` has cut a clip per
/// driver per race since the recording feature shipped - the seconds
/// where that driver was called, with a lead-in and a tail - and written
/// them to `<night>\Highlights\`. Nothing ever played one. The roadmap
/// named this as the obvious next thing to do to `DriverCut` and said why
/// it was a job in audio rather than in facts: formats, offsets, joins.
///
/// THROUGH THE BUS, WHICH IS THE WHOLE OF THE DESIGN. There is no silent
/// render anywhere in this app: the only path from something to audio is
/// to play it with a recorder tapping the bus (the decision
/// AnnouncerControl records, and the reason a piece is MADE by being
/// played). So the clip is handed to <see cref="AudioBus"/> while
/// BookendRecording is listening, exactly as the show's opener, bed and
/// sting are - and three problems solve themselves:
///
///   the format question. A clip is 44.1 kHz and mono OR stereo depending
///   on which bit rate its encode settled at, and the announcer's own
///   voice is 24 kHz mono from Kokoro or whatever ElevenLabs' plan
///   default is. AudioBus.PlayAsync resamples and up-mixes everything to
///   the mix format on the way in, so none of that is this file's
///   business;
///
///   the recording question. The tap is on the bus, so a clip played
///   through it is in the saved piece without anything being stitched;
///
///   and the single-listener question. AudioBus holds exactly one
///   listener, so opening a second output to play a clip would have made
///   it inaudible in the file AND not the mix the room heard.
///
/// NOTHING IS CONCATENATED, which is the other half. A byte-level join
/// would have had to reckon with CutWav's half-second seam being in the
/// file but not in DriverClip.LengthMs, and with DecodeMp3 throwing away
/// eighty milliseconds off the front of every mp3. Both are real; neither
/// is worth meeting when the bus will mix for you.
/// </summary>
internal static class DriversOwnClip
{
    /// <summary>
    /// How much of a clip is played.
    ///
    /// Twenty seconds against a spoken piece of sixty to ninety. A clip
    /// may be up to <see cref="RaceHighlights.MaxPerDriverMs"/> - a
    /// hundred and fifty seconds - and three races of that is seven and a
    /// half minutes of audio wrapped round a minute of words, which is
    /// not a piece about somebody's night, it is the night played back.
    ///
    /// FROM THE START of the clip rather than the end, because
    /// RaceHighlights cuts each window with a seven-second lead-in in
    /// front of the moment: twenty seconds from the top is the run-up and
    /// the thing itself.
    /// </summary>
    internal const double MostSeconds = 20;

    /// <summary>
    /// How loud, against the show's own 0.9 for an opener and 0.8 for a
    /// sting. This is the real sound of the room rather than production
    /// music, so it plays at the opener's level rather than under
    /// anything.
    /// </summary>
    internal const float Level = 0.9f;

    /// <summary>The folder RaceHighlights writes a night's clips into.</summary>
    internal const string FolderName = "Highlights";

    /// <summary>
    /// This driver's clip from tonight, or null when there is not one -
    /// which is most of the time and is not a fault.
    ///
    /// WHY THIS SCANS RATHER THAN LOOKS UP. Nothing indexes a clip:
    /// `DriverClip` is a plan that lives inside one method and is never
    /// persisted, so the only record a clip exists is its file name. The
    /// name ends in the driver's own, run through
    /// <see cref="PlainFileName.SafePart"/> - which is lossy, so two
    /// drivers whose names differ only in punctuation would collide. That
    /// is as good as this can honestly be without a manifest, and a
    /// manifest is a second record of the night, which is the thing
    /// RaceHighlights opens by refusing.
    ///
    /// THE MOST RECENT ONE where a driver raced more than once: their
    /// latest race is the freshest memory, and picking their best-placed
    /// would mean reading positions out of file names.
    ///
    /// AND A WAV COUNTS, which is not a detail. RaceAudioRecorder cuts each
    /// clip as a wav and then encodes it, and a clip the encoder REFUSED is
    /// deliberately left where it is as a wav and logged - so on a machine
    /// without Media Foundation every clip of every night is a wav and
    /// nothing else. Looking only for mp3s would have meant this feature
    /// silently did nothing at all on exactly those machines.
    ///
    /// The extension is checked here rather than passed to the enumerator,
    /// because Windows matches a "*.mp3" pattern against 8.3 names too and
    /// will hand back a "clip.mp3something" - documented behaviour of
    /// EnumerateFiles, and the reason this asks for everything and reads the
    /// extension itself.
    ///
    /// Any failure at all - no folder, a locked disk, a store that cannot
    /// be read - comes back null. A clip is a courtesy, and the piece
    /// plays without it exactly as it did before this existed.
    /// </summary>
    internal static string? FindFor(string? folderForNight, string? driverName)
    {
        if (string.IsNullOrWhiteSpace(folderForNight) || string.IsNullOrWhiteSpace(driverName))
        {
            return null;
        }

        var safe = PlainFileName.SafePart(driverName, "driver");

        if (safe.Length == 0)
        {
            return null;
        }

        try
        {
            var folder = Path.Combine(folderForNight, FolderName);

            if (!Directory.Exists(folder))
            {
                return null;
            }

            string? best = null;
            var bestAt = DateTime.MinValue;

            foreach (var path in Directory.EnumerateFiles(folder))
            {
                if (!Playable(path) || !IsFor(path, safe))
                {
                    continue;
                }

                var at = File.GetLastWriteTimeUtc(path);

                if (best is null || at > bestAt)
                {
                    best = path;
                    bestAt = at;
                }
            }

            return best;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The two things a clip is ever written as - see the note in
    /// <see cref="FindFor"/> about the encoder being refused.
    /// </summary>
    private static bool Playable(string path)
    {
        var extension = Path.GetExtension(path);

        return extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".wav", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether this file is a clip of that driver's.
    ///
    /// The name RaceHighlights writes is
    /// "&lt;base&gt; - P&lt;position&gt; &lt;safe driver name&gt;", and a collision
    /// adds " (2)" before the extension
    /// (<see cref="RaceHighlights.UnusedPath"/>).
    ///
    /// THE WHOLE SHAPE IS CHECKED rather than just the end of it, and the
    /// difference matters at a real club. A plain "ends with the name"
    /// would hand Dave Smith the clip of a club-mate called Big Dave
    /// Smith - one of the ordinary ways names overlap, and the piece would
    /// have played somebody else's thirty seconds under Dave's name with
    /// nothing anywhere saying so. So what sits in front of the name has to
    /// be " - P" and then the position's digits and then one space: the
    /// exact thing <see cref="RaceHighlights.ClipNameFor"/> writes, and
    /// nothing a club's own words can imitate by accident.
    ///
    /// The position itself is not read. It is not known here, and matching
    /// on it would mean working out where somebody finished by parsing the
    /// app's own file names.
    ///
    /// Searched from the END of the name, because the base name can
    /// legitimately contain the same shape: a club that calls a race
    /// "P1 Shootout" writes
    /// "2026-10-13 1942 - P1 Shootout - P1 Dave Smith".
    /// </summary>
    internal static bool IsFor(string path, string safeDriverName)
    {
        if (safeDriverName.Length == 0)
        {
            return false;
        }

        var name = WithoutCopyNumber(Path.GetFileNameWithoutExtension(path));

        if (!name.EndsWith(safeDriverName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // The space between the position and the name.
        var space = name.Length - safeDriverName.Length - 1;

        if (space < 0 || name[space] != ' ')
        {
            return false;
        }

        // Back over the position's digits, however many there are.
        var at = space - 1;
        var digits = 0;

        while (at >= 0 && char.IsAsciiDigit(name[at]))
        {
            at--;
            digits++;
        }

        // And the " - P" in front of them.
        return digits > 0
            && at >= 3
            && name[at] == 'P'
            && name.AsSpan(at - 3, 3).SequenceEqual(" - ");
    }

    /// <summary>Drops a trailing " (2)" that a name collision added.</summary>
    private static string WithoutCopyNumber(string name)
    {
        if (!name.EndsWith(')'))
        {
            return name;
        }

        var opened = name.LastIndexOf(" (", StringComparison.Ordinal);

        if (opened <= 0)
        {
            return name;
        }

        var inside = name[(opened + 2)..^1];

        return inside.Length > 0 && inside.All(char.IsAsciiDigit) ? name[..opened] : name;
    }

    /// <summary>
    /// The first <see cref="MostSeconds"/> of a clip, as something the bus
    /// will play.
    ///
    /// A PROVIDER OF ITS OWN because ShowOneShot plays a clip to its end
    /// and there is no way to ask it for less - which is right for an
    /// opener and wrong for something that may be two and a half minutes
    /// long. This counts in FRAMES throughout, never in samples: a budget
    /// that landed half a frame in would put the channels out of step for
    /// the rest of it, which is the mistake ShowOneShot's own constructor
    /// documents.
    /// </summary>
    internal sealed class Excerpt : ISampleProvider
    {
        private readonly float[] _samples;
        private readonly int _until;

        private int _at;

        internal Excerpt(ShowClip clip, double mostSeconds = MostSeconds)
        {
            _samples = clip.Samples;
            WaveFormat = clip.Format;

            var channels = Math.Max(1, clip.Channels);
            var frames = Math.Min(clip.Frames, (int)Math.Max(0, mostSeconds * clip.SampleRate));

            _until = Math.Min(_samples.Length, frames * channels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var left = Math.Max(0, _until - _at);
            var taking = Math.Min(count, left);

            if (taking <= 0)
            {
                return 0;
            }

            Array.Copy(_samples, _at, buffer, offset, taking);
            _at += taking;

            return taking;
        }
    }
}
