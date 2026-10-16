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
    /// The longest any one piece may hold the show up, in seconds. A piece
    /// longer than this is faded out at this point so the voice can carry
    /// on - a 40-second jingle in front of every race is a long wait.
    /// </summary>
    public int LongestHoldSeconds { get; set; } = 30;

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

    private static readonly object Gate = new();

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

    /// <summary>The longest one piece may hold the show, in seconds.</summary>
    internal static int LongestHoldSeconds
    {
        get
        {
            lock (Gate)
            {
                return Math.Clamp(Loaded().LongestHoldSeconds, MinHoldSeconds, MaxHoldSeconds);
            }
        }
        set
        {
            lock (Gate)
            {
                Loaded().LongestHoldSeconds = Math.Clamp(value, MinHoldSeconds, MaxHoldSeconds);
                SaveLocked();
            }

            Saved();
        }
    }

    internal const int MinHoldSeconds = 3;

    internal const int MaxHoldSeconds = 60;

    // ==================== The run of show ====================

    /// <summary>Every point, in the order the night hears them.</summary>
    internal static readonly IReadOnlyList<ImagingCue> Cues = Enum.GetValues<ImagingCue>();

    /// <summary>What a point plays out of the box - the club's run of show.</summary>
    internal static ImagingChoice DefaultFor(ImagingCue cue) => cue switch
    {
        ImagingCue.TopOfRace => ImagingChoice.JingleOrSweeper,
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
    internal static IReadOnlyList<ImagingChoice> ChoicesFor(ImagingCue cue) => cue == ImagingCue.RecapBed
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
        ImagingCue.TopOfRace => "the first thing On Air plays, before the opening",
        ImagingCue.AfterOpening => "after the opening, just before On Air stages the race",
        ImagingCue.AfterRaceOver => "after the finish and the teaser, going into the stop set",
        ImagingCue.BackFromBreak => "coming back out of the stop set, before the recap",
        ImagingCue.RecapOpen => "lands in front of the recap host",
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

            while (ClipOrder.Count > ClipsKept && ClipOrder.First is { } oldest)
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

    // ==================== Playing ====================

    /// <summary>
    /// Plays the next piece for a point and holds until it has played - or
    /// for <see cref="LongestHoldSeconds"/>, when it fades the rest out so the
    /// show can carry on. Returns false when nothing played or it was cut
    /// short by <paramref name="ct"/>, which fades it out.
    ///
    /// The club's playlist steps aside while it plays (ShowOnAir), the way it
    /// does for the show's own music.
    /// </summary>
    internal static async Task<bool> PlayAsync(ImagingCue cue, CancellationToken ct = default)
    {
        if (Muted || ct.IsCancellationRequested)
        {
            return false;
        }

        var clip = Next(cue);

        if (clip is null)
        {
            return false;
        }

        return await PlayClipAsync(clip, Level / 100f, Describe(cue), ct).ConfigureAwait(false);
    }

    /// <summary>Whether everything is muted - nothing plays then.</summary>
    private static bool Muted
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
    /// One piece, once, at a level - held for its length up to the longest
    /// hold, then faded if there is more of it. See <see cref="PlayAsync"/>.
    /// </summary>
    internal static async Task<bool> PlayClipAsync(ShowClip clip, float level, string what, CancellationToken ct)
    {
        var gain = new SmoothGainSampleProvider(new ShowOneShot(clip), 1f, downSeconds: FadeSeconds);
        var stop = new CancellationTokenSource();
        var released = 0;

        // Exactly once, however it ends - the club's music must come back.
        void Release()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                ShowOnAir.End();
            }
        }

        // Fades it, then takes it off the bus - and gives the music back.
        void FadeOut()
        {
            gain.Target = 0f;

            _ = Task.Delay(TimeSpan.FromSeconds(FadeSeconds + 0.1)).ContinueWith(_ =>
            {
                try
                {
                    stop.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Already played out.
                }

                Release();
            }, TaskScheduler.Default);
        }

        ShowOnAir.Begin();

        Task playing;

        try
        {
            playing = AudioBus.PlayAsync(gain, level, stop.Token);
        }
        catch (Exception ex)
        {
            Release();
            stop.Dispose();
            Status?.Invoke($"[imaging] The {what} couldn't play: {ex.Message}");
            return false;
        }

        _ = playing.ContinueWith(done =>
        {
            _ = done.Exception;
            Release();
            stop.Dispose();
        }, TaskScheduler.Default);

        var longest = TimeSpan.FromSeconds(LongestHoldSeconds);
        var hold = clip.Length < longest ? clip.Length : longest;

        try
        {
            await Task.Delay(hold, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            FadeOut();
            return false;
        }

        // Longer than the show will wait for: the rest is faded so the next
        // thing can start, rather than left ringing on under it.
        if (clip.Length - hold > TimeSpan.FromSeconds(0.25))
        {
            FadeOut();
        }

        return true;
    }

    /// <summary>
    /// Plays one file for the page's Play button, with nothing held for it.
    /// Hand back the result to <see cref="StopPreview"/> to stop it.
    /// </summary>
    internal static CancellationTokenSource? Preview(string path, out string? problem)
    {
        var clip = Load(path, out problem);

        if (clip is null)
        {
            return null;
        }

        var stop = new CancellationTokenSource();

        try
        {
            _ = AudioBus.PlayAsync(new ShowOneShot(clip), Level / 100f, stop.Token)
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
