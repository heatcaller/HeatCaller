using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HeatCaller;

/// <summary>
/// USAGE TOTALS, COUNTED ON THIS PC (19 Nov 2026, round 1 of
/// plans/app-usage-analytics-plan.md). Which pages and tabs were open, how
/// many times, and for how long, added up per day - and nothing else. Round 1
/// only counts and keeps them here; nothing is sent anywhere yet.
///
/// TOTALS, NOT A TRAIL. A day is a short list of rows, one per page, tab and
/// context (a race day open, Desk View), each with three numbers: opens,
/// active seconds and on-screen seconds. No time of day, no order. Nobody can
/// read from it what was done when.
///
/// TWO CLOCKS. On screen: HeatCaller is the window in front. Active: on
/// screen, and the keyboard or mouse used in the last two minutes
/// (<see cref="ActiveWithin"/>). A laptop left on Live Timing all evening is
/// on screen for hours and active for minutes, and the two are kept apart so
/// neither is mistaken for the other. On-screen time stops at six hours a
/// row a day.
///
/// NEVER A DRIVER. Page names are the sidebar's own, and tab names are the
/// captions code gives them - but a caption can carry a driver's name (a tab
/// titled after the driver it shows), so every caption is checked against
/// every driver's names first and, if any word of one is in it, it is kept as
/// "unknown_driver" and nothing of the name survives (<see cref="Scrub"/>).
/// Juniors are never named here, and neither is anyone else.
///
/// UI thread only, like MainForm, which owns it (through AppUsageTracker).
/// The file is written whole each time (temp file, then replace).
/// </summary>
internal sealed class AppUsage
{
    /// <summary>The file, under AppPaths.Root.</summary>
    internal const string FileName = "app-usage.json";

    /// <summary>Input this recent counts as someone at the laptop.</summary>
    internal static readonly TimeSpan ActiveWithin = TimeSpan.FromMinutes(2);

    /// <summary>A gap between two marks longer than this is the PC asleep or the window stuck, and counts as nothing.</summary>
    internal static readonly TimeSpan LongestGap = TimeSpan.FromSeconds(60);

    /// <summary>On-screen time a row may gather in one day.</summary>
    internal const int MostShownSecondsPerRow = 6 * 3600;

    /// <summary>Days kept here; older ones are dropped when the file is saved.</summary>
    internal const int DaysKept = 31;

    /// <summary>What a caption holding a driver's name is kept as.</summary>
    internal const string UnknownDriver = "unknown_driver";

    /// <summary>The longest name kept for a page or tab.</summary>
    private const int LongestName = 48;

    /// <summary>
    /// Everything that may be counted besides pages and tabs. A name not on
    /// this list is ignored, so a typo can't start counting something new.
    /// </summary>
    internal static readonly IReadOnlySet<string> Events = new HashSet<string>(StringComparer.Ordinal)
    {
        // A page opened from the sidebar's search box.
        "nav_search_used",
        "manage_navigation_opened",
        "warmup_flag",
        "photo_finish_opened",
        "overlay_editor_opened",
        "chart_exported",
        "tour_finished",
        "tour_skipped",
    };

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly Func<IEnumerable<DriverProfile>> _drivers;
    private readonly string _version;
    private Store _store;

    private (string Page, string Tab)? _current;
    private DateTime? _lastMark;
    private bool _lastInFront;
    private bool _lastActive;

    /// <param name="path">The file to keep the totals in.</param>
    /// <param name="drivers">Every driver, read each time a caption is checked (Scrub).</param>
    /// <param name="version">The HeatCaller version, kept on each day.</param>
    internal AppUsage(string path, Func<IEnumerable<DriverProfile>> drivers, string version)
    {
        _path = path;
        _drivers = drivers;
        _version = version;
        _store = Load(path);
    }

    /// <summary>The default file, in the app's data folder.</summary>
    internal static string DefaultPath => Path.Combine(AppPaths.Root, FileName);

    /// <summary>The days held, oldest first.</summary>
    internal IReadOnlyList<Day> Days => _store.Days;

    /// <summary>
    /// A page (and the tab on it, or "") has come on screen. Closes the time
    /// on the last one first, with what the last mark knew. Counts an open
    /// only when the page or tab is different from the one before.
    /// </summary>
    internal void Enter(string page, string tab, DateTime now, Context context)
    {
        Mark(now, _lastInFront, _lastActive, context);

        // A page or tab coming on screen is somebody at the laptop (or
        // HeatCaller starting up in front of them): the time from here
        // counts until the next tick says otherwise.
        _lastInFront = true;
        _lastActive = true;

        var key = (Scrub(page), Scrub(tab));

        if (key.Item1.Length == 0)
        {
            return;
        }

        if (_current != key)
        {
            _current = key;
            RowFor(now, key, context).Opens++;
        }
    }

    /// <summary>
    /// The clock's tick: gives the time since the last mark to the page on
    /// screen, and remembers whether HeatCaller is in front and in use for
    /// the next one.
    /// </summary>
    internal void Mark(DateTime now, bool inFront, bool active, Context context)
    {
        if (_lastMark is { } last && _current is { } key)
        {
            var gap = now - last;

            if (gap > TimeSpan.Zero && gap <= LongestGap && _lastInFront)
            {
                var row = RowFor(now, key, context);
                var seconds = gap.TotalSeconds;

                row.ShownSeconds = Math.Min(MostShownSecondsPerRow, row.ShownSeconds + seconds);

                if (_lastActive)
                {
                    row.ActiveSeconds = Math.Min(row.ShownSeconds, row.ActiveSeconds + seconds);
                }
            }
        }

        _lastMark = now;
        _lastInFront = inFront;
        _lastActive = inFront && active;
    }

    /// <summary>Counts one of <see cref="Events"/>; anything else is ignored.</summary>
    internal void Count(string name, DateTime now)
    {
        if (!Events.Contains(name))
        {
            return;
        }

        var day = DayFor(now);
        day.Events[name] = day.Events.GetValueOrDefault(name) + 1;
    }

    /// <summary>Gives the time since the last mark to the page on screen, as the last mark found it - before a save, so closing loses nothing.</summary>
    internal void Flush(DateTime now, Context context) => Mark(now, _lastInFront, _lastActive, context);

    /// <summary>Forgets every day held (the box was unticked).</summary>
    internal void Clear()
    {
        _store = new Store();
        _current = null;
        _lastMark = null;
    }

    /// <summary>Whether saving is frozen for a restore; a test can stand in for SafeFile.IsFrozen, which can't be undone in-process.</summary>
    internal Func<bool> SavingFrozen { get; init; } = () => SafeFile.IsFrozen;

    /// <summary>Writes the file, dropping days older than <see cref="DaysKept"/>. Never throws: losing a count must never cost the club anything.</summary>
    internal void Save(DateTime now)
    {
        var oldest = DateOnly.FromDateTime(now).AddDays(-(DaysKept - 1));
        _store.Days.RemoveAll(day => day.Date < oldest);

        // A restore froze saving (SafeFile.Freeze) and is restarting: the
        // closing window's save would put these pre-restore totals back
        // over the restored file (or re-make one the restore set aside).
        if (SavingFrozen())
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_store, Json));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SessionLog.Write($"[usage] The usage totals couldn't be saved ({ex.GetType().Name}); they will be tried again.");
        }
    }

    /// <summary>The report as it would go, for See what's sent: every day held, in the shape the relay takes.</summary>
    internal string ReportJson() => ReportOf(_store.Days);

    /// <summary>
    /// The body of POST app-usage for these days: {version, days} - the
    /// relay's shape (relay/src/appusage.ts), and nothing from the file
    /// besides the days themselves.
    /// </summary>
    internal static string ReportOf(IEnumerable<Day> days) =>
        JsonSerializer.Serialize(new Report { Days = days.ToList() }, Json);

    /// <summary>When days were last taken by the relay, or null if none ever were (on this PC since the box was last ticked).</summary>
    internal DateTime? LastSentUtc => _store.LastSentUtc;

    /// <summary>The days before <paramref name="today"/>, oldest first: the ones finished, and ready to go.</summary>
    internal List<Day> Finished(DateOnly today) =>
        _store.Days.Where(day => day.Date < today).OrderBy(day => day.Date).ToList();

    /// <summary>
    /// Forgets these days once the relay has them (<paramref name="sentUtc"/>
    /// set), or once it has said it never will take them (null). Not saved
    /// until the next <see cref="Save"/>.
    /// </summary>
    internal void Drop(IReadOnlyCollection<DateOnly> dates, DateTime? sentUtc)
    {
        _store.Days.RemoveAll(day => dates.Contains(day.Date));

        if (sentUtc is { } sent)
        {
            _store.LastSentUtc = sent;
        }
    }

    /// <summary>The same in words, newest day first, rows by active time.</summary>
    internal string ReportText()
    {
        if (_store.Days.Count == 0)
        {
            return "Nothing has been counted yet.";
        }

        var text = new StringBuilder();

        foreach (var day in _store.Days.OrderByDescending(d => d.Date))
        {
            text.Append(day.Date.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture))
                .Append(" (HeatCaller ").Append(day.Version).Append(", text size ").Append(day.TextSize).AppendLine("%)");

            foreach (var row in day.Rows.OrderByDescending(r => r.ActiveSeconds).ThenByDescending(r => r.ShownSeconds))
            {
                text.Append("  ").Append(row.Page);

                if (row.Tab.Length > 0)
                {
                    text.Append(" > ").Append(row.Tab);
                }

                text.Append(": ").Append(Minutes(row.ActiveSeconds)).Append(" in use, ")
                    .Append(Minutes(row.ShownSeconds)).Append(" on screen, opened ")
                    .Append(row.Opens).Append(row.Opens == 1 ? " time" : " times");

                if (row.RaceDay)
                {
                    text.Append(", race day open");
                }

                if (row.DeskView)
                {
                    text.Append(", Desk View");
                }

                text.AppendLine();
            }

            foreach (var (name, count) in day.Events.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                text.Append("  ").Append(name).Append(": ").Append(count).AppendLine();
            }

            text.AppendLine();
        }

        return text.ToString().TrimEnd();
    }

    private static string Minutes(double seconds) =>
        seconds < 60 ? $"{(int)seconds} s" : $"{(int)Math.Round(seconds / 60)} min";

    /// <summary>
    /// A page or tab name as it is kept: no driver's name (any word of a
    /// driver's name or announcer name, three letters or more, makes the
    /// whole caption <see cref="UnknownDriver"/>), then lower case, letters
    /// only, words joined by "_" - which also drops counts such as "Inbox (3)".
    /// </summary>
    internal string Scrub(string caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
        {
            return "";
        }

        var words = Words(caption);

        if (words.Count == 0)
        {
            return "";
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var driver in _drivers())
        {
            foreach (var word in Words(driver.DriverName).Concat(Words(driver.AnnouncerName)))
            {
                if (word.Length >= 3)
                {
                    names.Add(word);
                    names.Add(Fold(word));
                }
            }
        }

        // A name, or a name with a possessive "s" ("Billy's" is one word, "Billys").
        if (words.Any(word => names.Contains(word) || names.Contains(Fold(word))
            || (word.Length > 3 && word.EndsWith('s') && (names.Contains(word[..^1]) || names.Contains(Fold(word[..^1]))))))
        {
            return UnknownDriver;
        }

        // Plain a-z only, as the relay takes: accents folded ("Réglages" →
        // "reglages"), any other letter left out.
        var slug = string.Join("_", words.Select(Fold).Where(word => word.Length > 0));
        return slug.Length <= LongestName ? slug : slug[..LongestName].TrimEnd('_');
    }

    /// <summary>A word in plain lower-case a-z: accents taken off, anything else dropped.</summary>
    private static string Fold(string word)
    {
        var plain = new StringBuilder(word.Length);

        foreach (var c in word.Normalize(NormalizationForm.FormD))
        {
            var lower = char.ToLowerInvariant(c);

            if (lower is >= 'a' and <= 'z')
            {
                plain.Append(lower);
            }
        }

        return plain.ToString();
    }

    /// <summary>The words in a caption or name; an apostrophe joins rather than splits ("Who's" is one word).</summary>
    private static List<string> Words(string text) =>
        Regex.Matches((text ?? "").Replace("'", "").Replace("’", ""), @"\p{L}+").Select(m => m.Value).ToList();

    private Day DayFor(DateTime now)
    {
        var date = DateOnly.FromDateTime(now);
        var day = _store.Days.FirstOrDefault(d => d.Date == date);

        if (day is null)
        {
            day = new Day { Date = date, Version = _version };
            _store.Days.Add(day);
        }

        day.Version = _version;
        return day;
    }

    private Row RowFor(DateTime now, (string Page, string Tab) key, Context context)
    {
        var day = DayFor(now);
        day.TextSize = context.TextSize;
        var row = day.Rows.FirstOrDefault(r =>
            r.Page == key.Page && r.Tab == key.Tab && r.RaceDay == context.RaceDay && r.DeskView == context.DeskView);

        if (row is null)
        {
            row = new Row { Page = key.Page, Tab = key.Tab, RaceDay = context.RaceDay, DeskView = context.DeskView };
            day.Rows.Add(row);
        }

        return row;
    }

    private static Store Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<Store>(File.ReadAllText(path), Json) ?? new Store();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            SessionLog.Write($"[usage] The usage totals file couldn't be read ({ex.GetType().Name}); starting again.");
        }

        return new Store();
    }

    /// <summary>What a row is counted under besides its page and tab.</summary>
    /// <param name="TextSize">The text size setting as a percentage (AppSettings.FontScale), kept on the day.</param>
    internal readonly record struct Context(bool RaceDay, bool DeskView, int TextSize = 100)
    {
        /// <summary>A FontScale as the percentage kept: to the nearest 5, from 50 to 300.</summary>
        internal static int Percent(float scale) =>
            float.IsFinite(scale) ? Math.Clamp((int)Math.Round(scale * 20) * 5, 50, 300) : 100;
    }

    internal sealed class Store
    {
        public int Version { get; set; } = 1;
        public List<Day> Days { get; set; } = [];

        /// <summary>When the relay last took days (null when it never has). Kept on this PC only, never sent.</summary>
        public DateTime? LastSentUtc { get; set; }
    }

    /// <summary>What goes to the relay: the days and nothing else.</summary>
    internal sealed class Report
    {
        public int Version { get; set; } = 1;
        public List<Day> Days { get; set; } = [];
    }

    internal sealed class Day
    {
        public DateOnly Date { get; set; }
        public string Version { get; set; } = "";
        public List<Row> Rows { get; set; } = [];
        public Dictionary<string, int> Events { get; set; } = new(StringComparer.Ordinal);

        /// <summary>The text size setting, as a percentage, the last time anything was counted that day.</summary>
        public int TextSize { get; set; } = 100;
    }

    internal sealed class Row
    {
        public string Page { get; set; } = "";
        public string Tab { get; set; } = "";
        public bool RaceDay { get; set; }
        public bool DeskView { get; set; }
        public int Opens { get; set; }
        public double ActiveSeconds { get; set; }
        public double ShownSeconds { get; set; }
    }
}
