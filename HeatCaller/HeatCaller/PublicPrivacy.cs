using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace HeatCaller;

/// <summary>
/// Who has asked to be kept off the internet, and how: their photo, their
/// podium videos, or their name (initials only). One immutable register for
/// the whole app, swapped whole (22 Oct 2026, driver experience, item 12).
/// </summary>
/// <remarks>
/// WHY A PROCESS-WIDE REGISTER. Every public document the relay gets is
/// built on a different thread - the UI thread, the web server's, the
/// publisher's - and almost all of them name drivers through
/// RelayPublisher.DisplayNames. Handing the profiles to every one of those
/// would be a dozen couplings and a lock each; reading one immutable object
/// through a volatile field costs nothing and can't be half-updated. The
/// privacy package rebuilds it on the roster tick when a profile changes
/// (MainForm.Privacy.cs).
///
/// ONLINE ONLY. The Wi-Fi pages, Big Screen and the PA are in the room and
/// still use the full name and photo; the profile form says so.
///
/// BEST EFFORT FOR FREE TEXT. Names in documents are matched exactly (a
/// row's name or transponder). Spoken and written lines can only be
/// searched: <see cref="MentionsWithheld"/> errs on the private side (a
/// first name another driver shares also counts), and <see cref="Redact"/>
/// replaces only the full and announcer names, so a bare first name in AI
/// prose can slip through.
/// </remarks>
internal sealed class PublicPrivacy
{
    /// <summary>One driver's three flags and the names they go by.</summary>
    private sealed record Entry(string Transponder, string Name, string AnnouncerName, bool NoPhoto, bool NoVideo, bool Initials);

    /// <summary>Everybody's, keyed by every transponder they own.</summary>
    private readonly Dictionary<string, Entry> _byTransponder;

    /// <summary>Drivers shown by initials, keyed by full name and by announcer name (single-spaced, any case).</summary>
    private readonly Dictionary<string, Entry> _initialsByName;

    /// <summary>The full and announcer names of those drivers, for <see cref="Redact"/>; null when nobody asked.</summary>
    private readonly Regex? _names;

    /// <summary>What each form of a name in <see cref="_names"/> becomes.</summary>
    private readonly Dictionary<string, string> _namesTo;

    /// <summary>Every name and each word of it for those drivers, for <see cref="MentionsWithheld"/>; null when nobody asked.</summary>
    private readonly Redactor? _mentions;

    private PublicPrivacy(IReadOnlyList<Entry> entries)
    {
        _byTransponder = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        _initialsByName = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        // Any case (22 Oct 2026, security review): "sam parker" in AI prose
        // or a caption is as much their name as "Sam Parker", and the three
        // forms below missed it. Every form of one name maps to the same
        // initials, so folding case loses nothing.
        _namesTo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var people = new List<Redactor.Person>();
        var fingerprint = new StringBuilder();

        foreach (var entry in entries.OrderBy(e => e.Transponder, StringComparer.OrdinalIgnoreCase))
        {
            fingerprint.Append(entry.Transponder.ToUpperInvariant()).Append('|')
                .Append(entry.NoPhoto ? '1' : '0').Append(entry.NoVideo ? '1' : '0').Append(entry.Initials ? '1' : '0').Append('|')
                .Append(entry.Name).Append('|').Append(entry.AnnouncerName).Append('\n');

            if (!entry.Initials)
            {
                continue;
            }

            var initials = Initials(entry.Name);
            var names = new[] { entry.Name, entry.AnnouncerName }
                .Select(SingleSpaced)
                .Where(n => n.Count(char.IsLetter) >= 2)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var name in names)
            {
                _initialsByName.TryAdd(name, entry);

                foreach (var form in new[] { name, name.ToUpperInvariant(), TitleCase(name) })
                {
                    _namesTo.TryAdd(form, initials);
                }
            }

            if (names.Count > 0)
            {
                people.Add(new Redactor.Person(names, Array.Empty<string>()));
            }
        }

        foreach (var entry in entries)
        {
            _byTransponder.TryAdd(entry.Transponder, entry);
        }

        Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString())));

        // Only what Differs looks at, for the flagged drivers - see FlaggedFingerprint.
        var flagged = new StringBuilder();

        foreach (var entry in _byTransponder.Values
                     .Where(e => e.NoPhoto || e.NoVideo || e.Initials)
                     .OrderBy(e => e.Transponder, StringComparer.OrdinalIgnoreCase))
        {
            flagged.Append(entry.Transponder.ToUpperInvariant()).Append('|')
                .Append(entry.NoPhoto ? '1' : '0').Append(entry.NoVideo ? '1' : '0').Append(entry.Initials ? '1' : '0').Append('|')
                .Append(entry.Initials ? entry.Name : string.Empty).Append('|')
                .Append(entry.Initials ? entry.AnnouncerName : string.Empty).Append('\n');
        }

        FlaggedFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(flagged.ToString())));

        if (_namesTo.Count > 0)
        {
            // Longest first, so "Sam Parker" goes whole before a shorter
            // form can take the start of it; whole words only.
            var alternatives = _namesTo.Keys
                .OrderByDescending(k => k.Length)
                .ThenBy(k => k, StringComparer.Ordinal)
                .Select(Regex.Escape);

            _names = new Regex(
                $@"(?<![\p{{L}}\p{{N}}])(?:{string.Join("|", alternatives)})(?![\p{{L}}\p{{N}}])",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        }

        if (people.Count > 0)
        {
            _mentions = Redactor.For(people, userProfileFolder: null);
        }
    }

    /// <summary>Nobody has asked for anything. What <see cref="Current"/> is until the first register is built.</summary>
    public static PublicPrivacy Empty { get; } = new(Array.Empty<Entry>());

    private static PublicPrivacy _current = Empty;

    /// <summary>
    /// The register in use. Read from any thread; swapped whole by the
    /// privacy package's roster tick, so a reader always sees one complete
    /// register, never half of two.
    /// </summary>
    public static PublicPrivacy Current
    {
        get => Volatile.Read(ref _current);
        set => Volatile.Write(ref _current, value ?? Empty);
    }

    /// <summary>
    /// The register for these profiles. Ghosts are never anybody; profiles
    /// with no flag set are still recorded, so <see cref="Changed"/> can see
    /// a flag going OFF as well as on.
    /// </summary>
    /// <param name="retained">
    /// Flagged drivers whose profile has gone (see <see cref="Retain"/>),
    /// still protected. Added AFTER the live profiles: by name always, by
    /// transponder only where no live profile holds that id now.
    /// </param>
    public static PublicPrivacy From(IEnumerable<DriverProfile>? profiles, IEnumerable<PrivacyRetainedDriver>? retained = null)
    {
        var entries = new List<Entry>();

        // WHY (13 Nov 2026, bug hunt 17) every main transponder first, the
        // extras after: the constructor keeps the FIRST entry per id. An
        // older profiles file can list one driver's main transponder among
        // another's extras, and the rest of the app gives that id to the
        // driver whose main it is (ProfileStore.Get). In one pass, by name
        // order, "Aaron"'s extra hid "Zoe"'s main and her photo went online.
        var extras = new List<Entry>();

        foreach (var profile in profiles ?? Enumerable.Empty<DriverProfile>())
        {
            if (profile is null || string.IsNullOrWhiteSpace(profile.Transponder) || GhostService.IsGhost(profile.Transponder))
            {
                continue;
            }

            var name = SingleSpaced(profile.DriverName);
            var announcer = SingleSpaced(profile.AnnouncerName);
            var main = profile.Transponder.Trim();

            foreach (var id in profile.AllTransponderIds)
            {
                var entry = new Entry(id, name, announcer, profile.NoPhotoOnline, profile.NoVideoOnline, profile.InitialsOnline);

                (string.Equals(id.Trim(), main, StringComparison.OrdinalIgnoreCase) ? entries : extras).Add(entry);
            }
        }

        entries.AddRange(extras);

        // WHY (23 Oct 2026, bug hunt 10): the register was built from the
        // profiles that exist NOW, so deleting a flagged driver's profile
        // (an end-of-season tidy, a duplicate) dropped their protection -
        // and the privacy batch then republished every saved result of
        // theirs with the full name. A removed driver's flags are kept
        // (AppSettings.PrivacyRetained) and still count here.
        //
        // Newest first (13 Nov 2026, bug hunt 17): Retain adds to the end,
        // and where two removed drivers had the same transponder in turn it
        // stays with the later one, as it was while their profile existed.
        foreach (var gone in (retained ?? Enumerable.Empty<PrivacyRetainedDriver>()).Reverse())
        {
            if (gone is null || !(gone.NoPhoto || gone.NoVideo || gone.Initials))
            {
                continue;
            }

            var name = SingleSpaced(gone.Name);
            var announcer = SingleSpaced(gone.AnnouncerName);

            foreach (var id in (gone.Transponders ?? new List<string>())
                         .Select(t => (t ?? string.Empty).Trim())
                         .Where(t => t.Length > 0)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                // Always added, so their names are caught. A transponder a
                // live driver holds now stays theirs: the constructor keeps
                // the FIRST entry per id, and the live ones are first.
                entries.Add(new Entry(id, name, announcer, gone.NoPhoto, gone.NoVideo, gone.Initials));
            }
        }

        return new PublicPrivacy(entries);
    }

    /// <summary>
    /// The removed drivers whose flags are still kept: <paramref name="kept"/>
    /// plus every flagged driver in <paramref name="before"/> none of whose
    /// transponders any of <paramref name="profiles"/> owns any more - less
    /// any kept driver a live profile now goes by the same name as (that
    /// profile is them again, and its own ticks decide). Returns the list
    /// unchanged, and <paramref name="changed"/> false, when nothing moved.
    /// </summary>
    internal static List<PrivacyRetainedDriver> Retain(
        PublicPrivacy before,
        IEnumerable<DriverProfile>? profiles,
        IReadOnlyList<PrivacyRetainedDriver>? kept,
        out bool changed)
    {
        var current = (profiles ?? Enumerable.Empty<DriverProfile>())
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Transponder) && !GhostService.IsGhost(p.Transponder))
            .ToList();

        var owned = new HashSet<string>(current.SelectMany(p => p.AllTransponderIds), StringComparer.OrdinalIgnoreCase);
        var liveNames = new HashSet<string>(
            current.Select(p => SingleSpaced(p.DriverName)).Where(n => n.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        changed = false;
        var result = new List<PrivacyRetainedDriver>();

        foreach (var gone in kept ?? Array.Empty<PrivacyRetainedDriver>())
        {
            if (gone is null || liveNames.Contains(SingleSpaced(gone.Name)))
            {
                changed = true;
                continue;
            }

            result.Add(gone);
        }

        // WHY (13 Nov 2026, bug hunt 17) by transponder AND name: a kept
        // driver's transponder can be handed to a second flagged driver who
        // is deleted in turn. By transponder alone the second was taken as
        // kept already, lost their protection, and their saved results were
        // republished under the full name.
        static string Holding(string? transponder, string? name) =>
            (transponder ?? string.Empty).Trim().ToUpperInvariant() + "\n" + SingleSpaced(name).ToUpperInvariant();

        var already = new HashSet<string>(
            result.SelectMany(g => (g.Transponders ?? new List<string>()).Select(t => Holding(t, g.Name))),
            StringComparer.Ordinal);

        // One driver per name and flags, holding every id of theirs that is
        // gone - an entry is per transponder.
        var newlyGone = before._byTransponder.Values
            .Where(e => (e.NoPhoto || e.NoVideo || e.Initials)
                        && !owned.Contains(e.Transponder)
                        && !already.Contains(Holding(e.Transponder, e.Name))
                        && !liveNames.Contains(e.Name))
            .GroupBy(e => (Name: e.Name.ToUpperInvariant(), Announcer: e.AnnouncerName.ToUpperInvariant(), e.NoPhoto, e.NoVideo, e.Initials))
            .OrderBy(g => g.Key.Name, StringComparer.Ordinal);

        foreach (var group in newlyGone)
        {
            var first = group.First();

            result.Add(new PrivacyRetainedDriver
            {
                Transponders = group.Select(e => e.Transponder).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList(),
                Name = first.Name,
                AnnouncerName = first.AnnouncerName,
                NoPhoto = first.NoPhoto,
                NoVideo = first.NoVideo,
                Initials = first.Initials,
            });

            changed = true;
        }

        return changed ? result : (kept as List<PrivacyRetainedDriver> ?? result);
    }

    /// <summary>
    /// For each of <paramref name="names"/>, in order, the transponder of a
    /// saved row published under it (<paramref name="rows"/>, matched
    /// trimmed and in any case), or null - one flagged initials-only when
    /// any is, so where a spare transponder puts two under one name the
    /// choice errs on the private side. For RelayPublisher.DisplayNames'
    /// third argument.
    /// </summary>
    /// <remarks>
    /// WHY (23 Oct 2026, bug hunt 10): the standings, the results index and
    /// the tracks page are built from SAVED rows, which keep the name a
    /// driver had on the night. Matched by name alone, a driver renamed
    /// since and then shown by initials was published under the old full
    /// name. The transponder is only looked up here, in the local register;
    /// it never goes into a document.
    /// </remarks>
    internal IReadOnlyList<string?> TransponderForEach(
        IReadOnlyList<string> names,
        IEnumerable<(string? Name, string? Transponder)> rows)
    {
        var chosen = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);

        foreach (var (rowName, rowTransponder) in rows)
        {
            var key = (rowName ?? string.Empty).Trim();
            var id = (rowTransponder ?? string.Empty).Trim();

            if (key.Length == 0 || id.Length == 0)
            {
                continue;
            }

            if (!chosen.TryGetValue(key, out var have) || (!InitialsForTransponder(have) && InitialsForTransponder(id)))
            {
                chosen[key] = id;
            }
        }

        return names
            .Select(n => chosen.TryGetValue((n ?? string.Empty).Trim(), out var id) ? id : null)
            .ToList();
    }

    /// <summary>
    /// Changes whenever any driver's flags or names change - and only then -
    /// so the roster tick can tell cheaply whether there is anything to do.
    /// </summary>
    public string Fingerprint { get; }

    /// <summary>
    /// Changes only when a FLAGGED driver's flags, or the names of one shown
    /// by initials, change - what the privacy batch has to republish for.
    /// Kept in AppSettings.PrivacyAppliedFingerprint, so a start-up can tell
    /// a change the last run never got round to (23 Oct 2026, bug hunt 10).
    /// </summary>
    public string FlaggedFingerprint { get; }

    /// <summary>Every transponder whose driver has any flag set, in order.</summary>
    public IReadOnlyList<string> FlaggedTransponders =>
        _byTransponder.Values
            .Where(e => e.NoPhoto || e.NoVideo || e.Initials)
            .Select(e => e.Transponder)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>True when anybody at all is shown by initials - so a caller can skip the name work for most clubs, on most nights.</summary>
    public bool AnyInitials => _initialsByName.Count > 0;

    /// <summary>True when <paramref name="name"/> is the full or announcer name of a driver shown online by initials only.</summary>
    public bool InitialsFor(string? name) =>
        !string.IsNullOrWhiteSpace(name) && _initialsByName.ContainsKey(SingleSpaced(name));

    /// <summary>True when the driver with this transponder (main or extra) is shown online by initials only.</summary>
    public bool InitialsForTransponder(string? transponder) =>
        Find(transponder)?.Initials == true;

    /// <summary>True when the driver with this transponder asked for no photo online.</summary>
    public bool NoPhoto(string? transponder) => Find(transponder)?.NoPhoto == true;

    /// <summary>True when the driver with this transponder asked for no podium videos online (always met since 28 Oct 2026: none go online).</summary>
    public bool NoVideo(string? transponder) => Find(transponder)?.NoVideo == true;

    /// <summary>
    /// The name as it may go online: their initials when they asked
    /// (matched by transponder when one is given, which survives a rename,
    /// otherwise by name), the name unchanged otherwise.
    /// </summary>
    public string PublicName(string? name, string? transponder = null)
    {
        var shown = name ?? string.Empty;

        if (InitialsForTransponder(transponder))
        {
            var entry = Find(transponder)!;
            return Initials(string.IsNullOrWhiteSpace(shown) ? entry.Name : shown);
        }

        return InitialsFor(shown) ? Initials(shown) : shown;
    }

    /// <summary>
    /// "Sam Parker" to "S.P.": the first letter or digit of each word, in
    /// capitals, each followed by a full stop. Quotes and brackets round a
    /// nickname are skipped. Empty for a name with nothing to take.
    /// </summary>
    public static string Initials(string? name)
    {
        var initials = new StringBuilder();

        foreach (var word in (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var first = word.FirstOrDefault(char.IsLetterOrDigit);

            if (first != default)
            {
                initials.Append(char.ToUpperInvariant(first)).Append('.');
            }
        }

        return initials.ToString();
    }

    /// <summary>
    /// <paramref name="text"/> with the full and announcer name of every
    /// driver shown by initials replaced by their initials - for public
    /// prose (captions, the night-before preview). Whole words only; as
    /// typed, in capitals, or with a capital and the rest small.
    /// </summary>
    public string Redact(string? text)
    {
        var line = text ?? string.Empty;

        return _names is null || line.Length == 0
            ? line
            : _names.Replace(line, m => _namesTo.TryGetValue(m.Value, out var to) ? to : m.Value);
    }

    /// <summary>
    /// True when <paramref name="text"/> mentions, by any name or any word of
    /// a name, a driver shown by initials - so a spoken line can be kept from
    /// listeners at home, since audio can't be redacted. Errs on the private
    /// side: another driver sharing that first name counts too.
    /// </summary>
    public bool MentionsWithheld(string? text)
    {
        if (_mentions is null || string.IsNullOrEmpty(text))
        {
            return false;
        }

        // Against the redactor with nobody in it, so an email address or a
        // folder path in the line (which any redactor takes out) is not
        // mistaken for a name.
        var withPeople = _mentions.Apply(text, out _);
        var withNobody = Redactor.None.Apply(text, out _);

        return !string.Equals(withPeople, withNobody, StringComparison.Ordinal);
    }

    /// <summary>
    /// The transponders (each driver's main one and their extras) whose flags
    /// or names differ between two registers, either way - a driver new to
    /// <paramref name="after"/> with a flag set, a flag turned on or off, or a
    /// flagged driver renamed. What the privacy package republishes for.
    /// </summary>
    public static IReadOnlyList<string> Changed(PublicPrivacy? before, PublicPrivacy? after)
    {
        var old = before ?? Empty;
        var now = after ?? Empty;

        if (string.Equals(old.Fingerprint, now.Fingerprint, StringComparison.Ordinal))
        {
            return Array.Empty<string>();
        }

        var changed = new List<string>();

        foreach (var id in old._byTransponder.Keys.Concat(now._byTransponder.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            old._byTransponder.TryGetValue(id, out var was);
            now._byTransponder.TryGetValue(id, out var isNow);

            if (Differs(was, isNow))
            {
                changed.Add(id);
            }
        }

        return changed.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Whether one driver's entry differs in a way that matters online. A
    /// driver with no flag at all who comes or goes, or is renamed, changes
    /// nothing anybody has published, so isn't counted.
    /// </summary>
    private static bool Differs(Entry? was, Entry? isNow)
    {
        var wasFlagged = was is { } w && (w.NoPhoto || w.NoVideo || w.Initials);
        var nowFlagged = isNow is { } n && (n.NoPhoto || n.NoVideo || n.Initials);

        if (!wasFlagged && !nowFlagged)
        {
            return false;
        }

        if (was is null || isNow is null)
        {
            return true;
        }

        return was.NoPhoto != isNow.NoPhoto
               || was.NoVideo != isNow.NoVideo
               || was.Initials != isNow.Initials
               || (isNow.Initials && (!string.Equals(was.Name, isNow.Name, StringComparison.Ordinal)
                                      || !string.Equals(was.AnnouncerName, isNow.AnnouncerName, StringComparison.Ordinal)));
    }

    private Entry? Find(string? transponder) =>
        !string.IsNullOrWhiteSpace(transponder) && _byTransponder.TryGetValue(transponder.Trim(), out var entry) ? entry : null;

    private static string SingleSpaced(string? text) =>
        string.Join(" ", (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>"sam parker" and "SAM PARKER" to "Sam Parker", with a capital after a hyphen or apostrophe too - as Redactor does.</summary>
    private static string TitleCase(string text) =>
        string.Join(" ", text.Split(' ').Select(word =>
        {
            var chars = word.ToLowerInvariant().ToCharArray();

            for (var i = 0; i < chars.Length; i++)
            {
                if (i == 0 || chars[i - 1] is '-' or '\'' or '’')
                {
                    chars[i] = char.ToUpperInvariant(chars[i]);
                }
            }

            return new string(chars);
        }));
}
