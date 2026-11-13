using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// The club's next race night, told to the drivers before it happens (12 Nov
/// 2026, setup plan D13 and C1).
///
/// WHY: the race director's review said the biggest saving at the desk
/// happens before the night. The club says which classes are on and when
/// entries close, and drivers say they're coming, so the desk knows who to
/// expect and the queue is short. If the night is off (rain, a flooded
/// track), one press tells everybody who usually comes, instead of a dozen
/// texts. This file is the announcement itself. The page that shows it
/// is relay/src/pages.ts's nextNightHtml, and the drivers' answers are in
/// PreEntryStore.
/// </summary>
public sealed class AnnouncedNight
{
    /// <summary>The night, as a local date.</summary>
    public DateTime Day { get; set; }

    /// <summary>"Club Night", "Race Day", or the club's own name for it.</summary>
    public string Title { get; set; } = string.Empty;

    public string Venue { get; set; } = string.Empty;

    /// <summary>The classes on, in running order.</summary>
    public List<string> Classes { get; set; } = new();

    public TimeSpan? FirstRace { get; set; }

    public TimeSpan? EntriesClose { get; set; }

    /// <summary>Drivers can say they're coming from the Me page (setup plan C1).</summary>
    public bool TakingEntries { get; set; } = true;

    /// <summary>The night is called off.</summary>
    public bool Off { get; set; }

    /// <summary>The club's words when it's off: "Racing's off tonight - the track is flooded."</summary>
    public string OffText { get; set; } = string.Empty;

    /// <summary>The day as the relay and the pre-entries key it: "2026-11-18".</summary>
    public string DayKey => NextNight.KeyOf(Day);
}

/// <summary>The announcement as the phones read it, and the words around it. Pure.</summary>
public static class NextNight
{
    /// <summary>A day as the relay and the pre-entries key it: "2026-11-18".</summary>
    public static string KeyOf(DateTime localDay) => localDay.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>"Tue 18 Nov" - the day as a driver reads it.</summary>
    public static string When(DateTime localDay) => localDay.ToString("ddd d MMM", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>
    /// When the card comes down by itself: the end of the night (6am the next
    /// morning, the night's turnover), so a laptop packed up and taken home
    /// never leaves last week's night on the phones.
    /// </summary>
    public static DateTime UntilUtc(DateTime localDay) =>
        DateTime.SpecifyKind(localDay.Date.AddDays(1).AddHours(6), DateTimeKind.Local).ToUniversalTime();

    /// <summary>
    /// The "nextnight" snapshot's body - the field names relay/src/pages.ts
    /// reads: day, title, when, venue, classes, firstRace, entriesClose, off,
    /// offText, takingEntries, until.
    /// </summary>
    /// <param name="phonesCanSay">
    /// Whether drivers' phones can send "I'm coming" at all - the club's driver
    /// requests switch (12 Nov 2026). Off, the night isn't "taking entries" on
    /// the phones, so Home doesn't offer a Coming? link that leads nowhere.
    /// </param>
    public static string Document(AnnouncedNight night, bool phonesCanSay = true)
    {
        static string? Clock(TimeSpan? time) =>
            time is { } t ? $"{(int)t.TotalHours % 24:00}:{t.Minutes:00}" : null;

        return JsonSerializer.Serialize(new
        {
            day = night.DayKey,
            title = night.Title.Trim(),
            when = When(night.Day),
            venue = night.Venue.Trim(),
            classes = night.Classes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList(),
            firstRace = Clock(night.FirstRace),
            entriesClose = Clock(night.EntriesClose),
            off = night.Off,
            offText = night.Off ? night.OffText.Trim() : string.Empty,
            takingEntries = night.TakingEntries && !night.Off && phonesCanSay,
            until = UntilUtc(night.Day).ToString("o", System.Globalization.CultureInfo.InvariantCulture),
        });
    }

    /// <summary>The card taken down: a night already over. The page draws nothing for it.</summary>
    public static string Withdrawn() =>
        JsonSerializer.Serialize(new { day = "", until = DateTime.UnixEpoch.ToString("o", System.Globalization.CultureInfo.InvariantCulture) });

    /// <summary>
    /// The night a new announcement starts from: the day after the last race
    /// day plus a week (the same weekday next week), or today when that's
    /// already past, with the last day's title, venue, classes and times.
    /// </summary>
    public static AnnouncedNight FromLastDay(RaceEvent? last, DateTime today)
    {
        var day = last is null ? today.Date : last.StartedUtc.ToLocalTime().Date.AddDays(7);

        while (day < today.Date)
        {
            day = day.AddDays(7);
        }

        var classes = last is null
            ? new List<string>()
            : ClassRunningOrder.Arrange(last.ClassNames, n => n, last.ClassRunningOrder, rotate: false, round: 0);

        return new AnnouncedNight
        {
            Day = day,
            Title = last is { IsClubNight: true } ? "Club Night" : "Race Day",
            Venue = last?.VenueName ?? string.Empty,
            Classes = classes,
            FirstRace = last?.FirstRaceLocalTime,
            EntriesClose = last?.EntriesCloseLocalTime,
        };
    }

    /// <summary>The push that tells every phone signed up for alerts the night is off (setup plan step 7): a title and the club's words.</summary>
    public static (string Title, string Body) OffPush(AnnouncedNight night, string clubName)
    {
        var club = string.IsNullOrWhiteSpace(clubName) ? string.Empty : clubName.Trim() + ": ";
        var words = string.IsNullOrWhiteSpace(night.OffText) ? $"Racing is off on {When(night.Day)}." : night.OffText.Trim();

        return ($"{club}no racing {When(night.Day)}", words);
    }

    /// <summary>The email that tells a regular the night is off.</summary>
    public static (string Subject, string Body) OffEmail(AnnouncedNight night, string clubName, string driverName)
    {
        var club = string.IsNullOrWhiteSpace(clubName) ? "The club" : clubName.Trim();
        var subject = $"{club}: no racing {When(night.Day)}";
        var words = string.IsNullOrWhiteSpace(night.OffText) ? $"Racing is off on {When(night.Day)}." : night.OffText.Trim();

        var body = $"Hi {driverName},\r\n\r\n{words}\r\n\r\nSee you at the next one.\r\n\r\n{club}";
        return (subject, body);
    }

    /// <summary>
    /// Who to email when a night is called off: drivers with an email address
    /// who have raced at the club in the last <paramref name="weeks"/> weeks,
    /// and anybody who said they were coming to that night. Each once.
    /// Ghosts are left out; the caller hands in only drivers who can race.
    /// </summary>
    public static List<DriverProfile> Regulars(
        IEnumerable<DriverProfile> profiles,
        IEnumerable<SessionRecord> history,
        IEnumerable<string> saidComing,
        DateTime nowUtc,
        int weeks = 8)
    {
        var since = nowUtc.AddDays(-7 * weeks);
        var raced = history
            .Where(s => s is not null && s.EndedAtUtc >= since)
            .SelectMany(s => s.Results ?? new List<SessionResultEntry>())
            .Select(r => r.Transponder)
            .Concat(saidComing)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return profiles
            .Where(p => p is not null
                        && !GhostService.IsGhost(p.Transponder)
                        && !string.IsNullOrWhiteSpace(p.Email)
                        && raced.Contains(p.Transponder))
            .GroupBy(p => p.Email.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }
}

/// <summary>One driver's "I'm coming" for a night (setup plan C1).</summary>
public sealed class PreEntry
{
    public string Transponder { get; set; } = string.Empty;

    public List<string> Classes { get; set; } = new();

    public DateTime SaidUtc { get; set; }
}

/// <summary>
/// Who said they're coming, night by night (setup plan C1).
/// %AppData%\HeatCaller\pre-entries.json, keyed by NextNight.KeyOf.
/// Filled from the Me page's "I'm coming", by way of DriverRequests.
/// </summary>
public sealed class PreEntryStore
{
    private readonly Dictionary<string, Dictionary<string, PreEntry>> _byNight;
    private readonly string? _path;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private PreEntryStore(Dictionary<string, Dictionary<string, PreEntry>> byNight, string? path)
    {
        _byNight = byNight;
        _path = path;
    }

    /// <summary>An empty store that never saves - for tests.</summary>
    public static PreEntryStore InMemory() => new(new Dictionary<string, Dictionary<string, PreEntry>>(StringComparer.Ordinal), null);

    public static PreEntryStore Load()
    {
        var path = Path.Combine(AppPaths.Root, "pre-entries.json");
        var loaded = new Dictionary<string, Dictionary<string, PreEntry>>(StringComparer.Ordinal);

        var unreadable = false;

        try
        {
            if (File.Exists(path))
            {
                if (JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, PreEntry>>>(SafeFile.ReadAllTextWithRetry(path)) is { } read)
                {
                    foreach (var (night, entries) in read)
                    {
                        if (entries is null)
                        {
                            continue;
                        }

                        // One at a time, the last one winning: two keys that
                        // differ only by case would throw out of the
                        // dictionary's own constructor and lose the whole file.
                        var coming = new Dictionary<string, PreEntry>(StringComparer.OrdinalIgnoreCase);

                        foreach (var (transponder, entry) in entries)
                        {
                            if (entry is not null)
                            {
                                coming[transponder] = entry;
                            }
                        }

                        loaded[night] = coming;
                    }
                }
                else
                {
                    // Present, and not the shape this store writes (the
                    // literal "null", say). Not a new install either.
                    unreadable = true;
                }
            }
        }
        catch (Exception ex)
        {
            // An unreadable file means nobody is expected, which the desk
            // will notice at once; it never stops the app.
            //
            // WHY (13 Nov 2026, bug hunt 17): and it is never saved over.
            // The first phone "I'm coming" used to replace everyone who had
            // already said so (see LoadedUnreadable).
            loaded.Clear();
            unreadable = SafeFile.HoldWritesAfterUnreadable(path, ex);
        }

        return new PreEntryStore(loaded, path) { LoadedUnreadable = unreadable };
    }

    /// <summary>
    /// True when the file was there at startup but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. Answers this session are then
    /// kept in memory only, and nothing is saved over the club's real file.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    /// <summary>Raised after any change, for Who's Racing.</summary>
    public event Action? Changed;

    /// <summary>Who said they're coming to a night.</summary>
    public IReadOnlyList<PreEntry> For(string nightKey) =>
        _byNight.TryGetValue(nightKey, out var night) ? night.Values.ToList() : Array.Empty<PreEntry>();

    /// <summary>A driver's answer: their classes, or none for "not coming after all".</summary>
    public void Set(string nightKey, string transponder, IReadOnlyList<string> classes, DateTime saidUtc)
    {
        if (!_byNight.TryGetValue(nightKey, out var night))
        {
            night = new Dictionary<string, PreEntry>(StringComparer.OrdinalIgnoreCase);
            _byNight[nightKey] = night;
        }

        if (classes.Count == 0)
        {
            night.Remove(transponder);
        }
        else
        {
            night[transponder] = new PreEntry { Transponder = transponder, Classes = classes.ToList(), SaidUtc = saidUtc };
        }

        // Nights more than a few weeks gone are let go.
        foreach (var old in _byNight.Keys.Where(k => string.CompareOrdinal(k, NextNight.KeyOf(DateTime.Today.AddDays(-60))) < 0).ToList())
        {
            _byNight.Remove(old);
        }

        Save();
        Changed?.Invoke();
    }

    private void Save()
    {
        if (_path is null || LoadedUnreadable)
        {
            // See LoadedUnreadable: the club's real file stays as it is.
            return;
        }

        try
        {
            SafeFile.WriteAllText(_path, JsonSerializer.Serialize(_byNight, Json));
        }
        catch (Exception)
        {
            // Retried with the next change, as the other stores are.
        }
    }
}

/// <summary>What the desk says about tonight's pre-entries (setup plan C1). Pure.</summary>
public static class ComingTonight
{
    /// <summary>
    /// What Who's Racing says: "12 said they're coming · 9 here ·
    /// still to come: Al Gorithim, Hugh Turn and 1 more." Null when nobody
    /// said they were coming.
    /// </summary>
    public static string? Line(IReadOnlyList<PreEntry> coming, Func<string, bool> checkedIn, Func<string, string> nameOf)
    {
        if (coming.Count == 0)
        {
            return null;
        }

        var here = coming.Count(c => checkedIn(c.Transponder));
        var waiting = coming.Where(c => !checkedIn(c.Transponder)).Select(c => nameOf(c.Transponder)).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();

        var line = $"{coming.Count} said they're coming · {here} here";

        if (waiting.Count == 0)
        {
            return line + " · everyone who said they would is here.";
        }

        var shown = waiting.Take(3).ToList();
        var more = waiting.Count - shown.Count;
        var list = more > 0
            ? $"{string.Join(", ", shown)} and {more} more"
            : shown.Count == 1 ? shown[0] : $"{string.Join(", ", shown.Take(shown.Count - 1))} and {shown[^1]}";

        return $"{line} · still to come: {list}.";
    }

    /// <summary>The phone's answer to "I'm coming": "You're down for Tue 18 Nov: Stock and Mod." or "Taken off Tue 18 Nov."</summary>
    public static string Said(DateTime night, IReadOnlyList<string> classes) =>
        classes.Count == 0
            ? $"Taken off {NextNight.When(night)}. Hope to see you at the next one."
            : $"You're down for {NextNight.When(night)}: {Join(classes)}. Check in when you arrive.";

    private static string Join(IReadOnlyList<string> names) => names.Count switch
    {
        0 => string.Empty,
        1 => names[0],
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
    };
}
