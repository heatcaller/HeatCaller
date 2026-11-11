namespace HeatCaller;

/// <summary>
/// Importing tonight's ENTRIES, not just driver profiles (11 Nov 2026,
/// race-night build plan 7.1): an RCSignup export, a LiveRC entry list
/// copied off the page, a LiveTime driver export or any CSV - each row one
/// driver in one class - matched onto the drivers already here, their
/// classes added, and everyone on the list checked in to tonight's race
/// day if the desk wants that.
///
/// WHY A SEPARATE IMPORT. Driver Hub's Import from CSV is a roster import:
/// it makes profiles and needs a transponder on every row. An entry list is
/// a different question - "who is racing tonight, in what" - and the rows
/// that matter most on it are the regulars, who are already here. So:
/// <list type="bullet">
/// <item>a row is matched by transponder, and when it has none, by name
///   (an RCSignup sheet often has no transponder for a regular who has one
///   on file);</item>
/// <item>a class on the row is ADDED to what the driver races, never put in
///   place of their main class - entering Mod tonight does not take them
///   out of Stock;</item>
/// <item>nothing already on a profile is changed without being shown: blank
///   fields are filled in, and a field the sheet says differently is kept
///   as it is and listed as kept, unless "Replace details that differ" is
///   ticked on the preview;</item>
/// <item>a row that can't be matched safely - a transponder that belongs to
///   somebody with another name, a name two drivers share, a new
///   transponder for a name already here - is skipped and says why, rather
///   than guessed at.</item>
/// </list>
///
/// Everything here is pure: the rows and the profiles go in, a plan comes
/// out (who is new, who is updated and how, who is skipped and why), and
/// the Who's Racing page shows it (EntryImportForm) before anything is
/// saved. The plan carries copies, never the live profiles, so cancelling
/// the preview changes nothing at all.
/// </summary>
internal static class EntryImport
{
    /// <summary>What a row of the entry list does.</summary>
    internal enum EntryAction
    {
        /// <summary>A driver nobody here is - a new profile.</summary>
        New,

        /// <summary>A driver already here, whose profile the row changes (a class added, a blank filled in).</summary>
        Update,

        /// <summary>A driver already here, with nothing on the row that changes them.</summary>
        Unchanged,

        /// <summary>Not imported - see the entry's Reason.</summary>
        Skip,
    }

    /// <summary>One row of the entry list, read through the column picks.</summary>
    internal sealed record EntryRow(
        int Line,
        string Name,
        string Transponder,
        IReadOnlyList<string> Classes,
        string RaceNumber,
        string Car,
        string AnnouncerName,
        string Team,
        string Sponsors,
        string Email,
        string Notes);

    /// <summary>What one row will do, for the preview.</summary>
    /// <param name="Line">The row's place in the file or table.</param>
    /// <param name="Name">The driver's name - the profile's when matched, else the sheet's.</param>
    /// <param name="Transponder">The driver's main transponder (what check-in goes by), or the sheet's for a skipped row.</param>
    /// <param name="Classes">The classes the row enters them in, as the club's classes spell them.</param>
    /// <param name="Changes">What changes on the profile, and what was kept, in words.</param>
    /// <param name="Reason">Why a skipped row was skipped; null otherwise.</param>
    internal sealed record PlannedEntry(
        int Line,
        EntryAction Action,
        string Name,
        string Transponder,
        IReadOnlyList<string> Classes,
        IReadOnlyList<string> Changes,
        string? Reason);

    /// <summary>The whole import, as it will happen.</summary>
    /// <param name="Entries">One per row, in the sheet's order.</param>
    /// <param name="Before">Copies of the existing profiles the import changes, as they are now - for the undo.</param>
    /// <param name="Saves">Every profile to save: the changed ones and the new ones, each once.</param>
    /// <param name="Added">The new profiles (also in <paramref name="Saves"/>).</param>
    /// <param name="CheckIns">The main transponder of every driver on the list who wasn't skipped, each once.</param>
    /// <param name="UnknownClasses">Classes on the sheet that aren't on the Classes page.</param>
    internal sealed record EntryPlan(
        IReadOnlyList<PlannedEntry> Entries,
        IReadOnlyList<DriverProfile> Before,
        IReadOnlyList<DriverProfile> Saves,
        IReadOnlyList<DriverProfile> Added,
        IReadOnlyList<string> CheckIns,
        IReadOnlyList<string> UnknownClasses)
    {
        public int NewDrivers => Added.Count;

        public int UpdatedDrivers => Saves.Count - Added.Count;

        public int Skipped => Entries.Count(e => e.Action == EntryAction.Skip);

        /// <summary>Drivers already here that the list names and doesn't change.</summary>
        public int UnchangedDrivers =>
            Entries.Where(e => e.Action == EntryAction.Unchanged).Select(e => e.Transponder)
                .Except(Saves.Select(s => s.Transponder), StringComparer.OrdinalIgnoreCase)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

        /// <summary>"3 new drivers, 2 updated, 5 already here with nothing to change, 1 row skipped."</summary>
        public string Summary()
        {
            var parts = new List<string>
            {
                Plural.Of(NewDrivers, "new driver"),
                $"{UpdatedDrivers} updated",
                $"{UnchangedDrivers} already here with nothing to change",
            };

            if (Skipped > 0)
            {
                parts.Add($"{Plural.Of(Skipped, "row")} skipped");
            }

            return string.Join(", ", parts) + ".";
        }
    }

    /// <summary>
    /// The rows read through the column picks. A row with nothing in it that
    /// an entry needs - no name and no transponder - still comes through, so
    /// the preview can say it was skipped rather than lose it.
    /// </summary>
    internal static List<EntryRow> ReadRows(
        IReadOnlyList<List<string>> rows,
        IReadOnlyList<int> lineNumbers,
        IReadOnlyList<ImportColumn> columns,
        IReadOnlyCollection<string> knownClasses)
    {
        int Col(ImportColumn column)
        {
            for (var i = 0; i < columns.Count; i++)
            {
                if (columns[i] == column)
                {
                    return i;
                }
            }

            return -1;
        }

        var name = Col(ImportColumn.DriverName);
        var first = Col(ImportColumn.FirstName);
        var last = Col(ImportColumn.LastName);
        var transponder = Col(ImportColumn.Transponder);
        var className = Col(ImportColumn.ClassName);
        var number = Col(ImportColumn.RaceNumber);
        var car = Col(ImportColumn.Car);
        var announcer = Col(ImportColumn.AnnouncerName);
        var team = Col(ImportColumn.Team);
        var sponsors = Col(ImportColumn.Sponsors);
        var email = Col(ImportColumn.Email);
        var notes = Col(ImportColumn.Notes);

        var read = new List<EntryRow>(rows.Count);

        for (var r = 0; r < rows.Count; r++)
        {
            var fields = rows[r];

            string Field(int index) => index >= 0 && index < fields.Count ? (fields[index] ?? string.Empty).Trim() : string.Empty;

            var driver = name >= 0 && Field(name).Length > 0
                ? TidyName(Field(name))
                : DriverImport.JoinName(Field(first), Field(last));

            read.Add(new EntryRow(
                r < lineNumbers.Count ? lineNumbers[r] : r + 2,
                driver,
                Field(transponder),
                SplitClasses(Field(className), knownClasses),
                Field(number),
                Field(car),
                Field(announcer),
                Field(team),
                Field(sponsors),
                Field(email),
                Field(notes)));
        }

        return read;
    }

    /// <summary>
    /// A name cell as a name: "Black, Kaleb" - how LiveTime and many club
    /// spreadsheets write a whole name in one column - is "Kaleb Black".
    /// Only a single comma with words either side is turned round; "Smith,
    /// Jr." style suffixes are left as they are.
    /// </summary>
    internal static string TidyName(string cell)
    {
        var text = string.Join(" ", (cell ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var parts = text.Split(',');

        if (parts.Length != 2)
        {
            return text;
        }

        var lastName = parts[0].Trim();
        var firstName = parts[1].Trim();

        if (lastName.Length == 0 || firstName.Length == 0 || Suffixes.Contains(firstName.TrimEnd('.')))
        {
            return text;
        }

        return firstName + " " + lastName;
    }

    private static readonly HashSet<string> Suffixes = new(StringComparer.OrdinalIgnoreCase) { "Jr", "Sr", "II", "III", "IV" };

    /// <summary>
    /// A name as it is compared: lower case, no dots or apostrophes, one
    /// space between words, and "Last, First" turned round. "Kaleb Black",
    /// "BLACK, KALEB" and "kaleb  black" are the same driver; "K. Black" is
    /// not (an initial could be anyone - the preview would rather skip it).
    /// </summary>
    internal static string NameKey(string? name)
    {
        var tidy = TidyName(name ?? string.Empty).ToLowerInvariant();
        var kept = new System.Text.StringBuilder(tidy.Length);

        foreach (var c in tidy)
        {
            if (c is '.' or '\'' or '’')
            {
                continue;
            }

            kept.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return string.Join(" ", kept.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// A class cell as the classes it names, each as the club's Classes
    /// page spells it ("stock buggy" is "Stock Buggy"). A cell can hold
    /// more than one: split on ';', '|' or a line break always, and on ','
    /// only when every part is a class the club has - a class with a comma
    /// in its name stays whole. A class the club hasn't got is kept as the
    /// sheet spells it.
    /// </summary>
    internal static IReadOnlyList<string> SplitClasses(string? cell, IReadOnlyCollection<string> knownClasses)
    {
        var text = (cell ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return Array.Empty<string>();
        }

        var parts = text.Split(new[] { ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var split = new List<string>();

        foreach (var part in parts)
        {
            var byComma = part.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (byComma.Length > 1 && byComma.All(p => KnownClass(p, knownClasses) is not null))
            {
                split.AddRange(byComma);
            }
            else
            {
                split.Add(part);
            }
        }

        var result = new List<string>();

        foreach (var part in split)
        {
            var spelled = KnownClass(part, knownClasses) ?? part;

            if (!result.Contains(spelled, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(spelled);
            }
        }

        return result;
    }

    /// <summary>The club's spelling of a class, ignoring case and spaces - or null when it hasn't got one by that name.</summary>
    internal static string? KnownClass(string name, IReadOnlyCollection<string> knownClasses)
    {
        var key = string.Join(" ", (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return knownClasses.FirstOrDefault(known =>
            string.Equals(string.Join(" ", known.Split(' ', StringSplitOptions.RemoveEmptyEntries)), key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The import, worked out and not done: what every row will do, the
    /// profiles to save (copies - <paramref name="profiles"/> is never
    /// touched) and who to check in.
    /// </summary>
    /// <param name="profiles">Every driver here now.</param>
    /// <param name="knownClasses">The classes on the Classes page.</param>
    /// <param name="replaceDiffering">
    /// True to put the sheet's race number, car, team and so on in place of
    /// a different one already on a profile. False (the default on the
    /// preview) keeps what is there and lists it as kept.
    /// </param>
    internal static EntryPlan Plan(
        IReadOnlyList<EntryRow> rows,
        IReadOnlyList<DriverProfile> profiles,
        IReadOnlyCollection<string> knownClasses,
        bool replaceDiffering)
    {
        // The working copies, by main transponder: an existing driver's the
        // first time a row reaches them, a new driver's when one is made.
        // Later rows for the same driver work on the same copy, so a
        // driver in two classes on two rows ends up in both.
        var working = new Dictionary<string, DriverProfile>(StringComparer.OrdinalIgnoreCase);
        var before = new List<DriverProfile>();
        var added = new List<DriverProfile>();
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var checkIns = new List<string>();
        var entries = new List<PlannedEntry>();
        var unknown = new List<string>();

        // Working copies first, so a driver this sheet has already changed
        // is matched as the sheet left them.
        IEnumerable<DriverProfile> Everyone() =>
            working.Values.Concat(profiles.Where(p => !working.ContainsKey(p.Transponder)));

        DriverProfile Working(DriverProfile existing)
        {
            if (working.TryGetValue(existing.Transponder, out var copy))
            {
                return copy;
            }

            before.Add(ProfileStore.Snapshot(existing));
            copy = ProfileStore.Snapshot(existing);
            working[existing.Transponder] = copy;
            return copy;
        }

        PlannedEntry Skip(EntryRow row, string reason) =>
            new(row.Line, EntryAction.Skip, row.Name, row.Transponder, row.Classes, Array.Empty<string>(), reason);

        foreach (var row in rows)
        {
            foreach (var cls in row.Classes)
            {
                if (KnownClass(cls, knownClasses) is null && !unknown.Contains(cls, StringComparer.OrdinalIgnoreCase))
                {
                    unknown.Add(cls);
                }
            }

            var key = NameKey(row.Name);
            DriverProfile? target = null;
            var isNew = false;

            if (row.Transponder.Length == 0 && key.Length == 0)
            {
                entries.Add(Skip(row, "no name or transponder"));
                continue;
            }

            if (row.Transponder.Length > 0)
            {
                // A main transponder wins over somebody's other one, as it
                // does in timing (ProfileStore.Get).
                var everyone = Everyone().ToList();
                var owner = everyone.FirstOrDefault(p => string.Equals(p.Transponder?.Trim(), row.Transponder, StringComparison.OrdinalIgnoreCase));
                var claimants = owner is null
                    ? everyone.Where(p => p.AllTransponderIds.Contains(row.Transponder, StringComparer.OrdinalIgnoreCase)).ToList()
                    : new List<DriverProfile> { owner };

                if (claimants.Count > 1)
                {
                    entries.Add(Skip(row, $"{row.Transponder} is listed by {claimants.Count} drivers here - sort it out on Driver Hub first"));
                    continue;
                }

                if (claimants.Count == 1)
                {
                    var found = claimants[0];
                    var foundKey = NameKey(found.DriverName);

                    // Somebody else on a transponder already in use: a mistake
                    // on the sheet, or a transponder that changed hands. Either
                    // way the desk decides - merging renamed the driver here.
                    if (key.Length > 0 && foundKey.Length > 0 && key != foundKey)
                    {
                        entries.Add(Skip(row, $"{row.Transponder} belongs to {found.DriverName.Trim()} here (the sheet says {row.Name})"));
                        continue;
                    }

                    target = found;
                }
                else
                {
                    // A transponder nobody here has, on a name somebody here
                    // has: a new transponder for a regular, or a second driver
                    // with the same name. Not guessed at.
                    var namesake = key.Length == 0 ? null : everyone.FirstOrDefault(p => NameKey(p.DriverName) == key);

                    if (namesake is not null)
                    {
                        entries.Add(Skip(row,
                            $"{namesake.DriverName.Trim()} is already here on {namesake.Transponder} - the sheet's {row.Transponder} is new. "
                            + "Add it on their Transponders tab, or fix the sheet"));
                        continue;
                    }

                    target = new DriverProfile { Transponder = row.Transponder, DriverName = row.Name };
                    isNew = true;
                }
            }
            else
            {
                var namesakes = Everyone().Where(p => NameKey(p.DriverName) == key).ToList();

                if (namesakes.Count == 0)
                {
                    entries.Add(Skip(row, $"no transponder on the row, and no driver here is called {row.Name}"));
                    continue;
                }

                if (namesakes.Count > 1)
                {
                    entries.Add(Skip(row, $"{namesakes.Count} drivers here are called {row.Name} - put the transponder on the row"));
                    continue;
                }

                target = namesakes[0];
            }

            var changes = new List<string>();
            DriverProfile copy;

            if (isNew)
            {
                copy = target!;
                working[copy.Transponder] = copy;
                added.Add(copy);
            }
            else
            {
                copy = Working(target!);
            }

            Merge(copy, row, isNew, replaceDiffering, changes);

            var action = isNew
                ? EntryAction.New
                : changes.Any(IsChange) ? EntryAction.Update : EntryAction.Unchanged;

            if (!isNew && changes.Any(IsChange))
            {
                changed.Add(copy.Transponder);
            }

            if (!checkIns.Contains(copy.Transponder, StringComparer.OrdinalIgnoreCase))
            {
                checkIns.Add(copy.Transponder);
            }

            entries.Add(new PlannedEntry(
                row.Line,
                action,
                string.IsNullOrWhiteSpace(copy.DriverName) ? row.Name : copy.DriverName.Trim(),
                copy.Transponder,
                row.Classes,
                changes.Count == 0 && !isNew ? new[] { "nothing to change" } : changes,
                null));
        }

        // Only the existing drivers whose copies changed are saved, or have
        // a "before" for the undo; one the sheet names and leaves alone is
        // not written at all.
        var saves = working.Values
            .Where(p => added.Contains(p) || changed.Contains(p.Transponder))
            .ToList();

        var beforeChanged = before
            .Where(b => changed.Contains(b.Transponder))
            .ToList();

        return new EntryPlan(entries, beforeChanged, saves, added, checkIns, unknown);
    }

    /// <summary>The words a kept field is listed with - not a change.</summary>
    private const string KeptPrefix = "keeps ";

    private static bool IsChange(string change) => !change.StartsWith(KeptPrefix, StringComparison.Ordinal);

    /// <summary>The row put into the driver's copy, saying in <paramref name="changes"/> what it did and what it kept.</summary>
    private static void Merge(DriverProfile copy, EntryRow row, bool isNew, bool replaceDiffering, List<string> changes)
    {
        if (!isNew && string.IsNullOrWhiteSpace(copy.DriverName) && row.Name.Length > 0)
        {
            copy.DriverName = row.Name;
            changes.Add($"name {row.Name}");
        }

        // A class is added to what they race, never put in place of their
        // main class. The first class a driver gets is their main one.
        foreach (var cls in row.Classes)
        {
            if (copy.RunsClass(cls))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(copy.ClassName))
            {
                copy.ClassName = cls;
                changes.Add($"class {cls}");
            }
            else
            {
                copy.Classes = (copy.Classes ?? new List<string>()).Append(cls).ToList();
                changes.Add($"adds class {cls}");
            }
        }

        // A car: a new one goes in their garage; the one they are announced
        // in changes only when it was blank, or when the desk asked.
        if (row.Car.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(copy.CarModel))
            {
                copy.CarModel = row.Car;
                changes.Add($"car {row.Car}");
            }
            else if (!string.Equals(copy.CarModel.Trim(), row.Car, StringComparison.OrdinalIgnoreCase))
            {
                if (replaceDiffering)
                {
                    var was = copy.CarModel.Trim();
                    copy.SetActiveCar(row.Car);
                    changes.Add($"car {was} → {row.Car}");
                }
                else if (!copy.AllCars.Any(c => string.Equals(c, row.Car, StringComparison.OrdinalIgnoreCase)))
                {
                    copy.Cars = (copy.Cars ?? new List<string>()).Append(row.Car).ToList();
                    changes.Add($"adds car {row.Car} to their garage");
                }
            }
        }

        MergeField(copy.RaceNumber, row.RaceNumber, "race number", replaceDiffering, changes, v => copy.RaceNumber = v);
        MergeField(copy.AnnouncerName, row.AnnouncerName, "announcer name", replaceDiffering, changes, v => copy.AnnouncerName = v);
        MergeField(copy.Team, row.Team, "team", replaceDiffering, changes, v => copy.Team = v);
        MergeField(copy.Sponsors, row.Sponsors, "sponsors", replaceDiffering, changes, v => copy.Sponsors = v);
        MergeField(copy.Email, row.Email, "email", replaceDiffering, changes, v => copy.Email = v);
        MergeField(copy.Notes, row.Notes, "notes", replaceDiffering, changes, v => copy.Notes = v);
    }

    /// <summary>
    /// One field: a blank one is filled in; one that says the same changes
    /// nothing; one that says something else is kept (and listed as kept)
    /// unless <paramref name="replace"/>.
    /// </summary>
    private static void MergeField(string? current, string sheet, string what, bool replace, List<string> changes, Action<string> set)
    {
        if (sheet.Length == 0)
        {
            return;
        }

        var now = (current ?? string.Empty).Trim();

        if (now.Length == 0)
        {
            set(sheet);
            changes.Add($"{what} {sheet}");
        }
        else if (!string.Equals(now, sheet, StringComparison.OrdinalIgnoreCase))
        {
            if (replace)
            {
                set(sheet);
                changes.Add($"{what} {now} → {sheet}");
            }
            else
            {
                changes.Add($"{KeptPrefix}{what} {now} (the sheet says {sheet})");
            }
        }
    }
}
