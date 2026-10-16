using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeatCaller;

/// <summary>
/// The kinds of station imaging a soundscape can carry - the pieces an FM
/// station plays between the talking.
///
/// NAMING. "Stinger" here is the short musical hit a station plays between
/// two segments. StingerLibrary - the recorded VOICE lines for the fixed race
/// calls - is a different thing that happens to share the word, and nothing
/// here touches it.
/// </summary>
internal enum ImagingElement
{
    /// <summary>The sung or produced station ID - the longest and biggest piece.</summary>
    Jingle,

    /// <summary>A short produced ID that sweeps from one thing into the next.</summary>
    Sweeper,

    /// <summary>The short piece either side of a break, going out to it or coming back.</summary>
    Bumper,

    /// <summary>A hit of a few seconds that punctuates two segments.</summary>
    Stinger,

    /// <summary>Music that loops underneath the talking.</summary>
    Bed,
}

/// <summary>
/// The points in the run of show where a piece of imaging can fire, in the
/// order a race night hears them.
///
/// The club's run of show (16 Oct 2026):
/// jingle/sweeper -> opening -> bumper -> staging -> race over -> bumper ->
/// stop set -> jingle/sweeper -> race recap open -> race recap bed -> bumper.
///
/// The first three belong to Race Control's On Air button, which plays them
/// and then stages the race. Stage on its own runs the race as it always
/// has, without them.
/// </summary>
internal enum ImagingCue
{
    /// <summary>Top of the race - the first thing On Air plays, before the opening.</summary>
    TopOfRace,

    /// <summary>
    /// The music under the opening and the staging call (16 Oct 2026): in at
    /// the jingle's post, ducked under the voice, out when the countdown
    /// starts - so no voice before the race is ever in the clear.
    /// </summary>
    OpeningBed,

    /// <summary>After the opening, before the race is staged.</summary>
    AfterOpening,

    /// <summary>After the race is over, going into the stop set (the commercial break).</summary>
    AfterRaceOver,

    /// <summary>Coming back out of the stop set, before the recap opens.</summary>
    BackFromBreak,

    /// <summary>The open of the race recap show - lands in front of the recap host.</summary>
    RecapOpen,

    /// <summary>The music that runs under the recap host for the whole recap show.</summary>
    RecapBed,

    /// <summary>Between the parts of the recap show - recap, honours, colour, interview, sign-off.</summary>
    BetweenRecapParts,

    /// <summary>The way out of the recap show, after its last word.</summary>
    AfterRecap,
}

/// <summary>What a point in the run of show plays.</summary>
internal enum ImagingChoice
{
    Off,
    Jingle,
    Sweeper,
    JingleOrSweeper,
    Bumper,
    Stinger,
    Bed,
}

/// <summary>What the Broadcast Imaging page saves - imaging.json under the club's data.</summary>
internal sealed class ImagingSettings
{
    /// <summary>The master switch. On: this is how the broadcast is meant to sound.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How loud the jingles, sweepers, bumpers and stingers play, 0 to 100.</summary>
    public int Level { get; set; } = 80;

    /// <summary>
    /// Where each piece's post is, in seconds from its top, by file name -
    /// the club's own marks from the Imaging page. A piece with none here
    /// takes the one shipped beside it (posts.json in its soundscape's
    /// folder), and failing that the post is measured (see
    /// BroadcastImaging.PostOf).
    /// </summary>
    public Dictionary<string, double>? Posts { get; set; } = new();

    /// <summary>What each point in the run of show plays, by ImagingCue name. A missing one plays its default.</summary>
    public Dictionary<string, ImagingChoice>? RunOfShow { get; set; } = new();

    /// <summary>
    /// The files a club has chosen, by soundscape id and then by element
    /// name. Only what the club changed is here: an element that is missing
    /// plays the soundscape's shipped defaults, and Restore Defaults is
    /// taking it back out.
    /// </summary>
    public Dictionary<string, Dictionary<string, List<string>>>? Soundscapes { get; set; } = new();
}

/// <summary>
/// The station imaging for each soundscape - jingles, sweepers, bumpers,
/// stingers and music beds - and the run of show that decides where each one
/// plays.
///
/// WHERE THE FILES ARE. The ones that ship sit in Assets\Imaging\&lt;Soundscape&gt;\
/// beside the exe, named "&lt;Soundscape&gt; &lt;Element&gt; &lt;N&gt;.mp3" - the element is
/// read from the name, so adding one to a soundscape's folder is all it
/// takes. Each soundscape only ever plays its own folder's files. A club's
/// own files are copied into the club's data under Imaging\&lt;Soundscape&gt;\ and
/// named the same way, so a file moved or renamed on the PC afterwards does
/// not take a jingle out of the show.
///
/// A club's own copy of a built-in soundscape starts from the built-in's
/// files - the one its sound pack belongs to.
///
/// NEVER THE SAME ONE TWICE. Every pick is from a shuffled bag of that
/// point's files: each is played once before any is played again, and a new
/// bag never starts with the one that just played.
///
/// STATIC, for the reason ShowOnAir and CommercialBreak are: the pieces are
/// asked for by Race Control's after-race sequence, by the show treatment
/// and by a page on the Sounds tab, and threading a service through all
/// three would widen constructors that have no opinion about jingles.
///
/// EVERYTHING HERE IS BEST-EFFORT. A missing file, a file that will not
/// load, no audio device: nothing plays, a line goes in the console, and the
/// race goes on exactly as it would have.
/// </summary>
internal static class BroadcastImaging
{
    /// <summary>The audio this can play - the same list the show's own music takes.</summary>
    public static readonly string[] Extensions = ShowMusic.Extensions;

    /// <summary>How long a piece takes to fade when it is cut short.</summary>
    private const double FadeSeconds = 1.2;

    /// <summary>The fewest clips held loaded at once. A Classic jingle is around 15 MB held.</summary>
    private const int ClipsKept = 12;

    /// <summary>The most audio held loaded at once, in samples - 64 million floats is 256 MB, two beds and the short pieces.</summary>
    private const long SamplesKept = 64L * 1024 * 1024;

    private static readonly object Gate = new();

    private static int _onAirsBegun;

    /// <summary>
    /// How many times On Air has been pressed this run. The commentary
    /// director keeps paid reads to the gap after a race, and On Air being
    /// pressed is that gap closing. See CommentaryDirector.ReadsBelongHere.
    /// </summary>
    public static int OnAirsBegun => Volatile.Read(ref _onAirsBegun);

    /// <summary>On Air has been pressed - the next race's top is coming, so the gap after the last race is over.</summary>
    public static void OnAirBegins() => Interlocked.Increment(ref _onAirsBegun);

    private static ImagingSettings _settings = new();

    /// <summary>The file the settings above came from - Root can move to a rehearsal copy.</summary>
    private static string? _loadedFrom;

    /// <summary>What is left in each point's shuffled bag, by soundscape and cue.</summary>
    private static readonly Dictionary<string, Queue<string>> Bags = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What each point played last, so a new bag never starts with it.</summary>
    private static readonly Dictionary<string, string> LastPlayed = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, ShowClip> Clips = new(StringComparer.OrdinalIgnoreCase);

    private static readonly LinkedList<string> ClipOrder = new();

    private static readonly Random Shuffle = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The soundscape in use. Set by MainForm, which has the store.</summary>
    internal static Func<Theme?>? ActiveSoundscape { get; set; }

    /// <summary>Whether all sound is muted. Set by MainForm; nothing plays while it says true.</summary>
    internal static Func<bool>? IsMuted { get; set; }

    /// <summary>Lines for the developer console.</summary>
    internal static event Action<string>? Status;

    /// <summary>Raised when the settings or a soundscape's files change, so the page can show it.</summary>
    internal static event Action? Changed;

    private static string FilePath => Path.Combine(AppPaths.Root, "imaging.json");

    /// <summary>Where the shipped pieces are - one folder per soundscape.</summary>
    internal static string DefaultsRoot => Path.Combine(AppContext.BaseDirectory, "Assets", "Imaging");

    /// <summary>Where a club's own pieces are copied to - one folder per soundscape.</summary>
    internal static string ClubRoot => Path.Combine(AppPaths.Root, "Imaging");

    // ==================== Settings ====================

    /// <summary>The settings, read from disk the first time and whenever the data folder has moved.</summary>
    private static ImagingSettings Loaded()
    {
        var path = FilePath;

        if (string.Equals(path, _loadedFrom, StringComparison.OrdinalIgnoreCase))
        {
            return _settings;
        }

        var loaded = new ImagingSettings();

        try
        {
            if (File.Exists(path))
            {
                loaded = JsonSerializer.Deserialize<ImagingSettings>(SafeFile.ReadAllTextWithRetry(path), Json) ?? new ImagingSettings();
            }
        }
        catch (Exception ex)
        {
            // Unreadable: start from the defaults rather than refusing to
            // open, and say so. The file is left where it is.
            Status?.Invoke($"[imaging] Couldn't read {path} ({ex.Message}) - using the defaults.");
        }

        // A hand-edited file can say null for either.
        loaded.RunOfShow ??= new Dictionary<string, ImagingChoice>();
        loaded.Soundscapes ??= new Dictionary<string, Dictionary<string, List<string>>>();
        loaded.Posts ??= new Dictionary<string, double>();

        _settings = loaded;
        _loadedFrom = path;
        Bags.Clear();

        return _settings;
    }

    private static void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(_settings, Json));
        }
        catch (Exception ex)
        {
            Status?.Invoke($"[imaging] Couldn't save the imaging settings: {ex.Message}");
        }
    }

    private static void Saved()
    {
        Changed?.Invoke();
    }

    /// <summary>The master switch.</summary>
    internal static bool Enabled
    {
        get
        {
            lock (Gate)
            {
                return Loaded().Enabled;
            }
        }
        set
        {
            lock (Gate)
            {
                Loaded().Enabled = value;
                SaveLocked();
            }

            Saved();
        }
    }

    /// <summary>How loud the pieces play, 0 to 100.</summary>
    internal static int Level
    {
        get
        {
            lock (Gate)
            {
                return Math.Clamp(Loaded().Level, 0, 100);
            }
        }
        set
        {
            lock (Gate)
            {
                Loaded().Level = Math.Clamp(value, 0, 100);
                SaveLocked();
            }

            Saved();
        }
    }

    // ==================== The run of show ====================

    /// <summary>Every point, in the order the night hears them.</summary>
    internal static readonly IReadOnlyList<ImagingCue> Cues = Enum.GetValues<ImagingCue>();

    /// <summary>What a point plays out of the box - the club's run of show.</summary>
    internal static ImagingChoice DefaultFor(ImagingCue cue) => cue switch
    {
        ImagingCue.TopOfRace => ImagingChoice.JingleOrSweeper,
        ImagingCue.OpeningBed => ImagingChoice.Bed,
        ImagingCue.AfterOpening => ImagingChoice.Bumper,
        ImagingCue.AfterRaceOver => ImagingChoice.Bumper,
        ImagingCue.BackFromBreak => ImagingChoice.JingleOrSweeper,
        ImagingCue.RecapOpen => ImagingChoice.Jingle,
        ImagingCue.RecapBed => ImagingChoice.Bed,
        ImagingCue.BetweenRecapParts => ImagingChoice.Stinger,
        ImagingCue.AfterRecap => ImagingChoice.Bumper,
        _ => ImagingChoice.Off,
    };

    /// <summary>What a point can be set to. The bed loops under a voice, so it is the only thing the bed's point can play - and nothing else can.</summary>
    internal static IReadOnlyList<ImagingChoice> ChoicesFor(ImagingCue cue) => cue is ImagingCue.RecapBed or ImagingCue.OpeningBed
        ? new[] { ImagingChoice.Off, ImagingChoice.Bed }
        : new[]
        {
            ImagingChoice.Off,
            ImagingChoice.Jingle,
            ImagingChoice.Sweeper,
            ImagingChoice.JingleOrSweeper,
            ImagingChoice.Bumper,
            ImagingChoice.Stinger,
        };

    /// <summary>What a point plays now.</summary>
    internal static ImagingChoice ChoiceFor(ImagingCue cue)
    {
        lock (Gate)
        {
            if (Loaded().RunOfShow!.TryGetValue(cue.ToString(), out var choice) && ChoicesFor(cue).Contains(choice))
            {
                return choice;
            }

            return DefaultFor(cue);
        }
    }

    internal static void SetChoice(ImagingCue cue, ImagingChoice choice)
    {
        lock (Gate)
        {
            var runOfShow = Loaded().RunOfShow!;

            if (choice == DefaultFor(cue))
            {
                runOfShow.Remove(cue.ToString());
            }
            else
            {
                runOfShow[cue.ToString()] = choice;
            }

            SaveLocked();
        }

        Saved();
    }

    /// <summary>Puts every point in the run of show back to what it plays out of the box.</summary>
    internal static void RestoreRunOfShow()
    {
        lock (Gate)
        {
            Loaded().RunOfShow!.Clear();
            SaveLocked();
        }

        Saved();
    }

    /// <summary>The elements a choice draws from.</summary>
    internal static IReadOnlyList<ImagingElement> ElementsOf(ImagingChoice choice) => choice switch
    {
        ImagingChoice.Jingle => new[] { ImagingElement.Jingle },
        ImagingChoice.Sweeper => new[] { ImagingElement.Sweeper },
        ImagingChoice.JingleOrSweeper => new[] { ImagingElement.Jingle, ImagingElement.Sweeper },
        ImagingChoice.Bumper => new[] { ImagingElement.Bumper },
        ImagingChoice.Stinger => new[] { ImagingElement.Stinger },
        ImagingChoice.Bed => new[] { ImagingElement.Bed },
        _ => Array.Empty<ImagingElement>(),
    };

    internal static string Describe(ImagingChoice choice) => choice switch
    {
        ImagingChoice.Off => "Nothing",
        ImagingChoice.Jingle => "A jingle",
        ImagingChoice.Sweeper => "A sweeper",
        ImagingChoice.JingleOrSweeper => "A jingle or a sweeper",
        ImagingChoice.Bumper => "A bumper",
        ImagingChoice.Stinger => "A stinger",
        ImagingChoice.Bed => "A music bed",
        _ => choice.ToString(),
    };

    internal static string Describe(ImagingCue cue) => cue switch
    {
        ImagingCue.TopOfRace => "Top of the race",
        ImagingCue.OpeningBed => "Opening bed",
        ImagingCue.AfterOpening => "After the opening",
        ImagingCue.AfterRaceOver => "Race over",
        ImagingCue.BackFromBreak => "Back from the stop set",
        ImagingCue.RecapOpen => "Race recap open",
        ImagingCue.RecapBed => "Race recap bed",
        ImagingCue.BetweenRecapParts => "Between recap parts",
        ImagingCue.AfterRecap => "After the recap",
        _ => cue.ToString(),
    };

    /// <summary>When a point fires, in words for the page.</summary>
    internal static string When(ImagingCue cue) => cue switch
    {
        ImagingCue.TopOfRace => "the first thing On Air plays - the opening hits its post",
        ImagingCue.OpeningBed => "in at the jingle's post, under the opening and the staging call, out at the countdown",
        ImagingCue.AfterOpening => "straight off the opening's last word - the staging call hits its post",
        ImagingCue.AfterRaceOver => "after the finish and the teaser, going into the stop set",
        ImagingCue.BackFromBreak => "coming back out of the stop set, before the recap",
        ImagingCue.RecapOpen => "the recap host hits its post, and it crossfades into the recap bed",
        ImagingCue.RecapBed => "loops under the recap host for the whole recap show",
        ImagingCue.BetweenRecapParts => "between the recap, honours, colour, interview and sign-off",
        ImagingCue.AfterRecap => "after the recap show's last word",
        _ => string.Empty,
    };

    internal static string Describe(ImagingElement element) => element switch
    {
        ImagingElement.Jingle => "Jingle",
        ImagingElement.Sweeper => "Sweeper",
        ImagingElement.Bumper => "Bumper",
        ImagingElement.Stinger => "Stinger",
        ImagingElement.Bed => "Music bed",
        _ => element.ToString(),
    };

    /// <summary>The word a file's name carries for an element - "Classic Jingle 1".</summary>
    internal static string FileWord(ImagingElement element) => element == ImagingElement.Bed ? "Bed" : element.ToString();

    // ==================== The files ====================

    /// <summary>The soundscape whose shipped folder a soundscape plays - its own for a built-in, its pack's for a club's copy.</summary>
    internal static string DefaultsName(Theme theme)
    {
        if (theme.IsBuiltIn)
        {
            return theme.Name;
        }

        // Classic is the Synthwave pack now (16 Oct 2026), and the Classic
        // tones belong to no soundscape of their own any more.
        return (theme.SoundPack ?? string.Empty).ToLowerInvariant() switch
        {
            "" or "synthwave" => "Classic",
            _ => SoundPacks.Find(theme.SoundPack)?.Name ?? "Classic",
        };
    }

    /// <summary>The element a file's name says it is, or null for a name that says none.</summary>
    internal static ImagingElement? ElementNamed(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);

        // Longest word first is not needed - no element's word is inside
        // another's - but "Bed" is checked as a whole word so a file called
        // "Embedded" is not taken for one.
        foreach (var element in Enum.GetValues<ImagingElement>())
        {
            var word = FileWord(element);

            if (element == ImagingElement.Bed)
            {
                if (name.Split(' ', '-', '_').Any(part => string.Equals(part, word, StringComparison.OrdinalIgnoreCase)))
                {
                    return element;
                }

                continue;
            }

            if (name.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                return element;
            }
        }

        return null;
    }

    /// <summary>The pieces a soundscape ships with for one element, in name order.</summary>
    internal static IReadOnlyList<string> DefaultFiles(Theme theme, ImagingElement element)
    {
        try
        {
            var folder = Path.Combine(DefaultsRoot, DefaultsName(theme));

            if (!Directory.Exists(folder))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateFiles(folder)
                .Where(path => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .Where(path => ElementNamed(path) == element)
                .OrderBy(path => Path.GetFileName(path), NaturalOrder.Instance)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Whether the club has changed this soundscape's files for this element.</summary>
    internal static bool IsCustom(Theme theme, ImagingElement element)
    {
        lock (Gate)
        {
            return Loaded().Soundscapes!.TryGetValue(theme.Id, out var elements)
                && elements.ContainsKey(element.ToString());
        }
    }

    /// <summary>Whether the club has changed any of this soundscape's files.</summary>
    internal static bool IsCustom(Theme theme)
    {
        lock (Gate)
        {
            return Loaded().Soundscapes!.TryGetValue(theme.Id, out var elements) && elements.Count > 0;
        }
    }

    /// <summary>The files one element of a soundscape plays: the club's list if it has one, otherwise the shipped ones.</summary>
    internal static IReadOnlyList<string> FilesFor(Theme theme, ImagingElement element)
    {
        lock (Gate)
        {
            if (Loaded().Soundscapes!.TryGetValue(theme.Id, out var elements)
                && elements.TryGetValue(element.ToString(), out var chosen))
            {
                // Only a local path this app could have written. The list is
                // an ordinary file a restored backup or a hand edit can fill
                // with anything, and merely checking a \\server\share path
                // exists hands that server this account's password hash -
                // see ShowMusic.Trustworthy, which this follows.
                return (chosen ?? new List<string>()).Where(Trustworthy).ToList();
            }
        }

        return DefaultFiles(theme, element);
    }

    private static bool Trustworthy(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || PlainFileName.IsNetworkPath(path))
        {
            return false;
        }

        try
        {
            return Path.IsPathFullyQualified(path);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Sets one element's list for a soundscape, so it no longer follows the shipped files.</summary>
    private static void SetFiles(Theme theme, ImagingElement element, IEnumerable<string> files)
    {
        lock (Gate)
        {
            var soundscapes = Loaded().Soundscapes!;

            if (!soundscapes.TryGetValue(theme.Id, out var elements) || elements is null)
            {
                elements = new Dictionary<string, List<string>>();
                soundscapes[theme.Id] = elements;
            }

            elements[element.ToString()] = files.ToList();
            ForgetBagsLocked(theme);
            SaveLocked();
        }

        Saved();
    }

    /// <summary>
    /// Copies a club's file in as one of a soundscape's pieces, named
    /// "&lt;Soundscape&gt; &lt;Element&gt; &lt;N&gt;", and adds it to the list. Returns false and
    /// says why in plain words when it cannot be played or copied.
    /// </summary>
    internal static bool Add(Theme theme, ImagingElement element, string from, out string? problem)
    {
        problem = null;

        var extension = Path.GetExtension(from);

        if (!Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            problem = $"{Path.GetFileName(from)} isn't a kind of audio this can play. Use one of: {string.Join(", ", Extensions)}.";
            return false;
        }

        // Loaded once to be sure it plays, so a club finds out now rather
        // than at the top of a race.
        if (ShowClip.Load(from, out problem) is null)
        {
            return false;
        }

        var current = FilesFor(theme, element).ToList();
        var folder = Path.Combine(ClubRoot, PlainFileName.SafePart(theme.Name, "Soundscape"));

        try
        {
            Directory.CreateDirectory(folder);

            var stem = $"{PlainFileName.SafePart(theme.Name, "Soundscape")} {FileWord(element)}";
            var number = current.Count + 1;
            string into;

            do
            {
                into = Path.Combine(folder, $"{stem} {number}{extension.ToLowerInvariant()}");
                number++;
            }
            while (File.Exists(into) || current.Contains(into, StringComparer.OrdinalIgnoreCase));

            File.Copy(from, into, overwrite: false);

            current.Add(into);
        }
        catch (Exception ex)
        {
            problem = $"Couldn't copy it into {folder}: {ex.Message}";
            return false;
        }

        SetFiles(theme, element, current);

        return true;
    }

    /// <summary>Takes one file out of a soundscape's element. The shipped file itself is never deleted - Restore Defaults brings it back.</summary>
    internal static void Remove(Theme theme, ImagingElement element, string path)
    {
        var left = FilesFor(theme, element)
            .Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase))
            .ToList();

        SetFiles(theme, element, left);
        Forget(path);
    }

    /// <summary>Puts one element of a soundscape back to the files it ships with.</summary>
    internal static void RestoreDefaults(Theme theme, ImagingElement element)
    {
        lock (Gate)
        {
            if (Loaded().Soundscapes!.TryGetValue(theme.Id, out var elements))
            {
                elements.Remove(element.ToString());

                if (elements.Count == 0)
                {
                    Loaded().Soundscapes!.Remove(theme.Id);
                }
            }

            ForgetBagsLocked(theme);
            SaveLocked();
        }

        Saved();
    }

    /// <summary>Puts every element of a soundscape back to the files it ships with.</summary>
    internal static void RestoreDefaults(Theme theme)
    {
        lock (Gate)
        {
            Loaded().Soundscapes!.Remove(theme.Id);
            ForgetBagsLocked(theme);
            SaveLocked();
        }

        Saved();
    }

    // ==================== Picking ====================

    /// <summary>The soundscape in use, or null when none is.</summary>
    internal static Theme? Active
    {
        get
        {
            try
            {
                return ActiveSoundscape?.Invoke();
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Whether a point in the run of show has anything to play right now:
    /// imaging on, a soundscape in use, the point not switched off, and at
    /// least one file for it.
    /// </summary>
    internal static bool Has(ImagingCue cue) => Candidates(cue, out _).Count > 0;

    /// <summary>The files a point could play right now, and the soundscape they belong to.</summary>
    private static IReadOnlyList<string> Candidates(ImagingCue cue, out Theme? theme)
    {
        theme = null;

        if (!Enabled || Active is not { } active)
        {
            return Array.Empty<string>();
        }

        theme = active;

        var choice = ChoiceFor(cue);

        return ElementsOf(choice)
            .SelectMany(element => FilesFor(active, element))
            .Where(path =>
            {
                try
                {
                    return File.Exists(path);
                }
                catch
                {
                    return false;
                }
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The next file for a point, from its shuffled bag - every file once
    /// before any is played again, and never the one that just played.
    /// Null when there is nothing to play.
    /// </summary>
    internal static string? NextFile(ImagingCue cue)
    {
        var candidates = Candidates(cue, out var theme);

        if (candidates.Count == 0 || theme is null)
        {
            return null;
        }

        var key = $"{theme.Id}|{cue}";

        lock (Gate)
        {
            if (!Bags.TryGetValue(key, out var bag))
            {
                bag = new Queue<string>();
                Bags[key] = bag;
            }

            // Anything taken out of the list since the bag was filled is
            // skipped, and an empty bag is filled again.
            while (bag.Count > 0)
            {
                var next = bag.Dequeue();

                if (candidates.Contains(next, StringComparer.OrdinalIgnoreCase))
                {
                    LastPlayed[key] = next;
                    return next;
                }
            }

            var shuffled = candidates.OrderBy(_ => Shuffle.Next()).ToList();

            // A new bag never opens with the one the last bag closed on.
            if (shuffled.Count > 1
                && LastPlayed.TryGetValue(key, out var last)
                && string.Equals(shuffled[0], last, StringComparison.OrdinalIgnoreCase))
            {
                var swapWith = 1 + Shuffle.Next(shuffled.Count - 1);
                (shuffled[0], shuffled[swapWith]) = (shuffled[swapWith], shuffled[0]);
            }

            foreach (var path in shuffled.Skip(1))
            {
                bag.Enqueue(path);
            }

            LastPlayed[key] = shuffled[0];
            return shuffled[0];
        }
    }

    /// <summary>The next piece for a point, loaded - see <see cref="NextFile"/>. Null when there is nothing to play or it would not load.</summary>
    internal static ShowClip? Next(ImagingCue cue)
    {
        var path = NextFile(cue);

        if (path is null)
        {
            return null;
        }

        var clip = Load(path, out var problem);

        if (clip is null)
        {
            Status?.Invoke($"[imaging] {Describe(cue)}: {problem ?? "couldn't load " + Path.GetFileName(path)}");
        }
        else
        {
            Status?.Invoke($"[imaging] {Describe(cue)}: {Path.GetFileName(path)}");
        }

        return clip;
    }

    /// <summary>A file loaded once and kept, up to <see cref="ClipsKept"/> of them.</summary>
    internal static ShowClip? Load(string path, out string? problem)
    {
        problem = null;

        lock (Gate)
        {
            if (Clips.TryGetValue(path, out var held))
            {
                ClipOrder.Remove(path);
                ClipOrder.AddLast(path);
                return held;
            }
        }

        // Outside the lock: loading a long jingle is a moment's work, and
        // nothing else should wait behind it.
        var clip = ShowClip.Load(path, out problem);

        if (clip is null)
        {
            return null;
        }

        lock (Gate)
        {
            Clips[path] = clip;
            ClipOrder.Remove(path);
            ClipOrder.AddLast(path);

            // By count AND by size: a five-minute music bed is over 100 MB
            // held, and twelve of those would be most of a race laptop's
            // memory. The newest is always kept, whatever its size.
            while (ClipOrder.Count > 1
                   && (ClipOrder.Count > ClipsKept || Clips.Values.Sum(c => (long)c.Samples.Length) > SamplesKept)
                   && ClipOrder.First is { } oldest)
            {
                Clips.Remove(oldest.Value);
                ClipOrder.RemoveFirst();
            }
        }

        return clip;
    }

    private static void Forget(string path)
    {
        lock (Gate)
        {
            Clips.Remove(path);
            ClipOrder.Remove(path);
        }
    }

    private static void ForgetBagsLocked(Theme theme)
    {
        foreach (var key in Bags.Keys.Where(k => k.StartsWith(theme.Id + "|", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            Bags.Remove(key);
        }
    }

    // ==================== Posts ====================

    /// <summary>The shipped posts, read once per soundscape folder.</summary>
    private static readonly Dictionary<string, Dictionary<string, double>> ShippedPosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The club's own mark for a piece's post, or null when it has none.
    /// By file name, so a mark survives the file being copied in again.
    /// </summary>
    internal static double? ClubPost(string path)
    {
        lock (Gate)
        {
            return Loaded().Posts!.TryGetValue(Path.GetFileName(path), out var at) ? at : null;
        }
    }

    /// <summary>Sets - or, with null, clears - the club's own mark for a piece's post.</summary>
    internal static void SetPost(string path, double? seconds)
    {
        lock (Gate)
        {
            var posts = Loaded().Posts!;

            if (seconds is { } at && at > 0)
            {
                posts[Path.GetFileName(path)] = Math.Round(at, 2);
            }
            else
            {
                posts.Remove(Path.GetFileName(path));
            }

            SaveLocked();
        }

        Saved();
    }

    /// <summary>The post shipped with a piece - posts.json beside it - or null.</summary>
    internal static double? ShippedPost(string path)
    {
        try
        {
            var folder = Path.GetDirectoryName(path);

            if (string.IsNullOrEmpty(folder))
            {
                return null;
            }

            Dictionary<string, double>? posts;

            lock (Gate)
            {
                if (!ShippedPosts.TryGetValue(folder, out posts))
                {
                    posts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                    var file = Path.Combine(folder, "posts.json");

                    if (File.Exists(file))
                    {
                        foreach (var (name, at) in JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(file)) ?? new())
                        {
                            posts[name] = at;
                        }
                    }

                    ShippedPosts[folder] = posts;
                }
            }

            return posts.TryGetValue(Path.GetFileName(path), out var post) ? post : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Where the next thing starts over a piece: its POST.
    ///
    /// On a jingle that is the instant the sung part stops, and the voice
    /// starts talking right on it - hitting the post, which is the whole
    /// trick of it. Whatever the piece still has to play after its post is
    /// crossfaded into what comes next, never cut.
    ///
    /// The club's own mark first, then the one shipped beside the piece,
    /// then a measured one: where the piece stops being its body and starts
    /// ringing out (ShowShape) - which on a piece with no ring-out is its
    /// end.
    /// </summary>
    internal static TimeSpan PostOf(string path, ShowClip clip)
    {
        var length = clip.Length.TotalSeconds;
        var marked = ClubPost(path) ?? ShippedPost(path);

        var at = marked ?? (double)clip.Shape.LoopEnd / clip.SampleRate;

        return TimeSpan.FromSeconds(Math.Clamp(at, Math.Min(0.3, length), Math.Max(0, length)));
    }

    /// <summary>Where a piece's post came from, for the page.</summary>
    internal static string PostSource(string path) =>
        ClubPost(path) is not null ? "set" : ShippedPost(path) is not null ? "shipped" : "measured";

    // ==================== Playing ====================

    /// <summary>The next piece for a point, loaded, with the file it came from - see <see cref="NextFile"/>.</summary>
    internal static (ShowClip Clip, string Path)? NextPiece(ImagingCue cue)
    {
        var path = NextFile(cue);

        if (path is null)
        {
            return null;
        }

        var clip = Load(path, out var problem);

        if (clip is null)
        {
            Status?.Invoke($"[imaging] {Describe(cue)}: {problem ?? "couldn't load " + Path.GetFileName(path)}");
            return null;
        }

        Status?.Invoke($"[imaging] {Describe(cue)}: {Path.GetFileName(path)}");
        return (clip, path);
    }

    /// <summary>
    /// Starts the next piece for a point and hands back the handle to it -
    /// null when there is nothing to play, or everything is muted. It plays
    /// whole, from its top: nothing here ever cuts a piece short or fires
    /// an ending of its own. See <see cref="ImagingPlay"/>.
    /// </summary>
    internal static ImagingPlay? Start(ImagingCue cue)
    {
        if (Muted || NextPiece(cue) is not { } piece)
        {
            return null;
        }

        return ImagingPlay.Start(piece.Clip, PostOf(piece.Path, piece.Clip), Level / 100f, Describe(cue), Status);
    }

    /// <summary>
    /// Plays the next piece for a point and returns at its POST, where the
    /// next thing goes. With <paramref name="crossfadeAtPost"/> the rest of
    /// the piece is then crossfaded away under what comes next; without it
    /// the piece plays out whole, for a point nothing follows at once.
    /// Returns false when nothing played or <paramref name="ct"/> called it
    /// off, which crossfades it away.
    /// </summary>
    internal static async Task<bool> PlayAsync(ImagingCue cue, CancellationToken ct = default, bool crossfadeAtPost = true)
    {
        if (ct.IsCancellationRequested || Start(cue) is not { } playing)
        {
            return false;
        }

        try
        {
            await playing.AtPost.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            playing.CrossfadeOut();
            return false;
        }

        if (crossfadeAtPost)
        {
            playing.CrossfadeOut();
        }

        return true;
    }

    /// <summary>
    /// Plays the next piece for a point and returns when the WHOLE of it
    /// has played - for an edge where what follows has a soundtrack of its
    /// own and must not start over the top of it (the bumper into the stop
    /// set, whose sponsor videos bring their own audio). Returns false when
    /// nothing played or <paramref name="ct"/> called it off, which
    /// crossfades it away.
    /// </summary>
    internal static async Task<bool> PlayWholeAsync(ImagingCue cue, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested || Start(cue) is not { } playing)
        {
            return false;
        }

        try
        {
            await playing.Played.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            playing.CrossfadeOut();
            return false;
        }

        return true;
    }

    /// <summary>Whether everything is muted - nothing plays then.</summary>
    internal static bool Muted
    {
        get
        {
            try
            {
                return IsMuted?.Invoke() ?? false;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Plays one file for the page's Play button, with nothing held for it.
    /// Hand back the result to <see cref="StopPreview"/> to stop it.
    /// </summary>
    internal static CancellationTokenSource? Preview(string path, out string? problem, double fromSeconds = 0)
    {
        var clip = Load(path, out problem);

        if (clip is null)
        {
            return null;
        }

        var stop = new CancellationTokenSource();

        try
        {
            var from = (int)(Math.Max(0, fromSeconds) * clip.SampleRate);

            _ = AudioBus.PlayAsync(new ShowOneShot(clip, from), Level / 100f, stop.Token)
                .ContinueWith(done => _ = done.Exception, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            problem = $"It couldn't play: {ex.Message}";
            stop.Dispose();
            return null;
        }

        return stop;
    }

    internal static void StopPreview(CancellationTokenSource? playing)
    {
        if (playing is null)
        {
            return;
        }

        try
        {
            playing.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already gone.
        }
    }
}

/// <summary>
/// One piece of imaging playing - see BroadcastImaging.Start.
///
/// It plays WHOLE, from its top, as the producer made it. Nothing here cuts
/// a piece at a time of its own choosing or fires an ending of its own: the
/// next thing starts at the piece's post (<see cref="AtPost"/>), and
/// whatever the piece still has to play after that is crossfaded away under
/// it (<see cref="CrossfadeOut"/>) - the sound never drops to nothing in
/// between.
///
/// The club's playlist steps aside while it plays (ShowOnAir), as it does for
/// the show's own music, and comes back exactly once however it ends.
/// </summary>
internal sealed class ImagingPlay
{
    /// <summary>How long the rest of a piece takes to cross into what follows it.</summary>
    internal const double CrossfadeSeconds = 1.2;

    private readonly SmoothGainSampleProvider _gain;

    /// <summary>Takes it off the bus once it has been crossfaded away. Never disposed: a late cancel must not throw.</summary>
    private readonly CancellationTokenSource _stop = new();

    private int _fading;
    private int _released;

    private ImagingPlay(SmoothGainSampleProvider gain, TimeSpan post, TimeSpan length)
    {
        _gain = gain;
        Post = post;
        Length = length;
    }

    /// <summary>Where the next thing goes, from the piece's top.</summary>
    internal TimeSpan Post { get; }

    internal TimeSpan Length { get; }

    /// <summary>Completes at the post.</summary>
    internal Task AtPost { get; private set; } = Task.CompletedTask;

    /// <summary>Completes when the piece has played out or been crossfaded away.</summary>
    internal Task Played { get; private set; } = Task.CompletedTask;

    internal static ImagingPlay? Start(ShowClip clip, TimeSpan post, float level, string what, Action<string>? status)
    {
        var gain = new SmoothGainSampleProvider(new ShowOneShot(clip), 1f, downSeconds: CrossfadeSeconds, upSeconds: 0.3);
        var play = new ImagingPlay(gain, post, clip.Length);

        ShowOnAir.Begin();

        try
        {
            play.Played = AudioBus.PlayAsync(gain, level, play._stop.Token);
        }
        catch (Exception ex)
        {
            play.Release();
            status?.Invoke($"[imaging] The {what} couldn't play: {ex.Message}");
            return null;
        }

        play.AtPost = Task.Delay(post);

        _ = play.Played.ContinueWith(done =>
        {
            _ = done.Exception;
            play.Release();
        }, TaskScheduler.Default);

        return play;
    }

    /// <summary>
    /// Crosses whatever is left of the piece into what is starting over it:
    /// ridden down over <see cref="CrossfadeSeconds"/>, then taken off. Safe
    /// to call more than once, and on a piece that has already finished.
    /// </summary>
    internal void CrossfadeOut()
    {
        if (Interlocked.Exchange(ref _fading, 1) == 1)
        {
            return;
        }

        _gain.Target = 0f;

        _ = Task.Delay(TimeSpan.FromSeconds(CrossfadeSeconds + 0.1)).ContinueWith(_ =>
        {
            try
            {
                _stop.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already gone.
            }

            Release();
        }, TaskScheduler.Default);
    }

    private void Release()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            ShowOnAir.End();
        }
    }
}

/// <summary>
/// A music bed running under the voice outside the recap show - the opening
/// bed, under On Air's opening and the staging call (16 Oct 2026), so no
/// voice before the race is ever in the clear.
///
/// Ducked under the voice whenever the announcer is talking, and dipped
/// under a bumper fired over it. It comes in from nothing at the jingle's
/// post and leaves with a short fade when <see cref="Finish"/> is called -
/// never by jumping to an ending of its own.
/// </summary>
internal sealed class ImagingBed
{
    private readonly ShowBed _bed;
    private readonly SmoothGainSampleProvider _gain;
    private readonly AnnouncementService _announcer;
    private readonly float _level;
    private readonly float _underVoice;

    private Action<bool>? _onSpeaking;
    private volatile bool _speaking;
    private volatile bool _dipped;
    private int _finished;
    private int _released;

    private ImagingBed(ShowBed bed, SmoothGainSampleProvider gain, AnnouncementService announcer, float level, float underVoice)
    {
        _bed = bed;
        _gain = gain;
        _announcer = announcer;
        _level = level;
        _underVoice = underVoice;
    }

    /// <summary>
    /// Starts the next bed for a point, coming up from nothing. Null when
    /// there is no bed for it, or everything is muted.
    /// </summary>
    /// <param name="level">The bed on its own, 0 to 1 - the show bed level on the Commentary page.</param>
    /// <param name="duck">How far it drops under the voice, as a fraction of <paramref name="level"/>.</param>
    internal static ImagingBed? Start(ImagingCue cue, AnnouncementService announcer, float level, float duck)
    {
        if (BroadcastImaging.Muted || BroadcastImaging.NextPiece(cue) is not { } piece)
        {
            return null;
        }

        var bed = new ShowBed(piece.Clip);
        var gain = new SmoothGainSampleProvider(bed, 0f, downSeconds: 0.5, upSeconds: 0.8);
        var run = new ImagingBed(bed, gain, announcer, Math.Clamp(level, 0f, 1f), Math.Clamp(level * duck, 0f, 1f));

        // A plain delegate held in a field, so the -= in Finish takes off
        // exactly what was put on - see ShowTreatment.OpenShowAsync.
        run._onSpeaking = now =>
        {
            run._speaking = now;
            run.Settle();
        };

        announcer.SpeakingChanged += run._onSpeaking;

        ShowOnAir.Begin();

        Task playing;

        try
        {
            playing = AudioBus.PlayAsync(gain, 1f);
        }
        catch
        {
            announcer.SpeakingChanged -= run._onSpeaking;
            bed.CutShort();
            run.Release();
            return null;
        }

        _ = playing.ContinueWith(done =>
        {
            _ = done.Exception;
            run.Release();
        }, TaskScheduler.Default);

        run.Settle();

        return run;
    }

    /// <summary>Under a bumper fired over it (true), or back up when the bumper has gone (false).</summary>
    internal void Dip(bool under)
    {
        _dipped = under;
        Settle();
    }

    private void Settle()
    {
        if (Volatile.Read(ref _finished) == 1)
        {
            return;
        }

        _gain.Target = _speaking || _dipped ? _underVoice : _level;
    }

    /// <summary>
    /// Takes the bed away with a short fade - the countdown has started, the
    /// race has moved on, or On Air was called off. Safe to call more than once.
    /// </summary>
    internal void Finish()
    {
        if (Interlocked.Exchange(ref _finished, 1) == 1)
        {
            return;
        }

        if (_onSpeaking is { } onSpeaking)
        {
            _announcer.SpeakingChanged -= onSpeaking;
        }

        _bed.CutShort();

        // The bed reports itself done once its fade has played. In case a
        // device going away means it never does, the music is given back
        // anyway.
        _ = Task.Delay(TimeSpan.FromSeconds(ShowBed.CutShortSeconds + 3)).ContinueWith(_ => Release(), TaskScheduler.Default);
    }

    private void Release()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            ShowOnAir.End();
        }
    }
}
