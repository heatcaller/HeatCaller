using System.Text.Json;
using System.Text.Json.Nodes;

namespace HeatCaller;

/// <summary>Loads/saves the day's event schedule to %AppData%\HeatCaller\day-schedule.json.</summary>
public sealed class DayScheduleStore
{
    private readonly List<ScheduleEntry> _entries;

    /// <summary>
    /// Ids of the lines AutoCompleteRecorded ticked itself - as opposed to a
    /// director's own tick - so it can take back only its own ticks.
    ///
    /// WHY (23 Oct 2026, bug hunt 10): the auto-tick only ever ticked. Delete
    /// a heat's race from Race History (the demo hint's way to clear a
    /// rehearsal, or the way to re-run a mis-recorded heat) and the round went
    /// back to unrun while its timetable line stayed done: Up Next, the
    /// announcer, the countdown and every phone's timetable skipped it. Kept
    /// by id because Update and UpdateMany swap the line objects. Saved in
    /// the same file as an extra "AutoCompleted": true on the line (see Save),
    /// which older builds simply ignore and a missing one reads as false, so
    /// files written before this load exactly as they did.
    /// </summary>
    private readonly HashSet<string> _autoTicked = new(StringComparer.Ordinal);

    /// <summary>
    /// The start stamp each auto-untick took off, by line id, so that undoing
    /// the race delete that caused it gives the line its start back.
    ///
    /// WHY (23 Oct 2026, bug hunt 10 review): the untick clears the stamp so a
    /// re-run is timed from its own start, but Race History's undo relinks the
    /// race and the line was then ticked again with no start at all, so
    /// "running late" fell back to an older line. Put back only when the line
    /// has no start of its own by then - a re-run's real start is never
    /// overwritten. Memory only: an undo does not outlive the session either.
    /// </summary>
    private readonly Dictionary<string, DateTime> _startsTakenBack = new(StringComparer.Ordinal);

    /// <summary>
    /// Generated lines SyncToProgram took off because their round left the
    /// programme, by event and round id, so a round that comes back gets its
    /// own line back.
    ///
    /// WHY (2026-11-05, Bug hunt 11: undoing a round delete lost its line):
    /// deleting a round dropped its line, and the undo - or undoing a heats
    /// build that replaced it - brought the round back under the same id to
    /// a brand new, blank line: the typed 19:40 and "started 19:42 · 3 min
    /// late" were gone, and running late was measured from an older heat.
    /// Round ids are fresh Guids, so the same id only comes back through an
    /// undo. Memory only, like _startsTakenBack: an undo does not outlive
    /// the session either.
    /// </summary>
    private readonly Dictionary<(string EventId, string RoundId), ScheduleEntry> _parkedLines = new();

    /// <summary>The name the auto-tick flag is saved under on a line in the file.</summary>
    private const string AutoTickedProperty = "AutoCompleted";

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "day-schedule.json");

    private DayScheduleStore(List<ScheduleEntry> entries)
    {
        _entries = entries;
    }

    /// <summary>Which event's timetable this is showing. Kept in step with the event store.</summary>
    public string CurrentEventId { get; set; } = RaceEventStore.LegacyEventId;

    /// <summary>
    /// The current event's rounds, in programme order, by id. Set once by
    /// EventSpine.Bind.
    ///
    /// This is the store's only route to the running order, and it is a hook
    /// rather than a constructor argument because the timetable and the rounds
    /// are loaded independently and neither should have to know the other's
    /// load order.
    ///
    /// Unset - which is what a backup restore or a test gets - falls back to
    /// the file's own order. That is what this store used to be, so nothing
    /// breaks; it simply stops being clever.
    /// </summary>
    public Func<IReadOnlyList<string>>? ProgramOrder { get; set; }

    /// <summary>One round on the programme, as much of it as the timetable needs.</summary>
    public readonly record struct ProgramLine(string RoundId, string ClassName, string Label);

    public static DayScheduleStore Load()
    {
        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var text = SafeFile.ReadAllTextWithRetry(FilePath);
                var list = JsonSerializer.Deserialize<List<ScheduleEntry>>(text);
                if (list is not null)
                {
                    var store = new DayScheduleStore(list);
                    store._autoTicked.UnionWith(ReadAutoTicked(text));

                    // Lines written before they carried an anchor get one from
                    // where they sit in the file, which is exactly where they
                    // were being drawn - so the sheet looks the same after the
                    // upgrade as it did before it.
                    if (AnchorLegacyLines(list))
                    {
                        store.Save();
                    }

                    return store;
                }

                // Present, parsed, and not the shape this store writes. Not
                // a new install either, so the same rule applies. A file
                // holding the literal "null" parses to nothing without
                // throwing: it used to fall straight through to the empty
                // store below with the flag left false, so the first
                // schedule change replaced every day's run sheet.
                // (17 Oct 2026, Bug Hunt 8, A25-2 - as RoundDefinitionStore.)
                unreadable = true;
            }
        }
        catch (Exception ex)
        {
            // Unreadable file: start empty rather than refusing to open -
            // but, when it could not be read at all or no copy of it could
            // be kept, never save over it this session (see LoadedUnreadable).
            unreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
        }

        return new DayScheduleStore(new List<ScheduleEntry>()) { LoadedUnreadable = unreadable };
    }

    /// <summary>
    /// True when the file was there at startup but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. The file holds every race day's
    /// run sheet, with its typed times, ticks and actual start stamps:
    /// nothing anybody could type back in. It used to be loaded as empty and
    /// then written over whole at the first change - a program edit, or
    /// simply the first race started - so a backup or sync tool holding it
    /// open for a moment at launch was enough to lose every other day's
    /// sheet. Tonight's sheet still works in memory for the rest of the
    /// session; it is just not saved.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    private void Save()
    {
        // Not written: the real file is almost certainly fine, and saving
        // this session's lines would replace every other day's. The console
        // has already told the operator why and asked for a restart.
        if (LoadedUnreadable)
        {
            return;
        }

        var json = _entries.Any(e => _autoTicked.Contains(e.Id))
            ? SerializeWithAutoTicks()
            : JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });

        // Through SafeFile like every other store: a bare WriteAllText
        // threw out of every schedule edit on a locked or full disk, and a
        // crash partway through left a truncated timetable behind.
        SafeFile.WriteAllText(FilePath, json);
    }

    /// <summary>
    /// The lines as Save has always written them, plus "AutoCompleted": true
    /// on each line the app ticked itself (see _autoTicked). Only taken when
    /// there is such a line, so a file with none is written exactly as before.
    /// </summary>
    private string SerializeWithAutoTicks()
    {
        var array = JsonSerializer.SerializeToNode(_entries) as JsonArray;
        if (array is null || array.Count != _entries.Count)
        {
            return JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
        }

        for (var i = 0; i < _entries.Count; i++)
        {
            if (_autoTicked.Contains(_entries[i].Id) && array[i] is JsonObject line)
            {
                line[AutoTickedProperty] = true;
            }
        }

        return array.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// The ids of the lines the file marks as ticked by the app itself. Read
    /// from the same text the lines came from; anything not shaped as
    /// expected is simply not a flag, so it can never stop the file loading.
    /// </summary>
    internal static IReadOnlyList<string> ReadAutoTicked(string json)
    {
        var ids = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return ids;
            }

            foreach (var line in doc.RootElement.EnumerateArray())
            {
                if (line.ValueKind == JsonValueKind.Object
                    && line.TryGetProperty(AutoTickedProperty, out var flag)
                    && flag.ValueKind == JsonValueKind.True
                    && line.TryGetProperty("Id", out var id)
                    && id.ValueKind == JsonValueKind.String
                    && id.GetString() is { Length: > 0 } text)
                {
                    ids.Add(text);
                }
            }
        }
        catch (JsonException)
        {
            // Not readable as flags - the lines themselves already loaded.
        }

        return ids;
    }

    /// <summary>
    /// This event's timetable, in manually-set list order rather than
    /// sorted by time - see ScheduleEntry's remarks.
    ///
    /// Scoped to the event. It used to be every line ever entered, which
    /// meant last week's timetable was still on screen at the start of
    /// this race day and had to be cleared by hand.
    /// </summary>
    public IReadOnlyList<ScheduleEntry> All
    {
        get
        {
            var mine = _entries
                .Where(e => string.Equals(e.EventId, CurrentEventId, StringComparison.Ordinal))
                .ToList();

            if (ProgramOrder?.Invoke() is not { } order)
            {
                return mine;
            }

            var rank = new Dictionary<string, int>(StringComparer.Ordinal);

            for (var i = 0; i < order.Count; i++)
            {
                rank[order[i]] = i;
            }

            // OrderBy is stable, so two lines sharing a slot - two of the
            // club's own lines between the same pair of rounds - stay in the
            // order they are stored in, which is what Move swaps.
            return mine.OrderBy(e => SlotOf(e, rank, order.Count)).ToList();
        }
    }

    /// <summary>
    /// Where a line sits in the running order.
    ///
    /// Rounds take the odd slots and the club's own lines the even ones, so a
    /// line anchored after n rounds (slot 2n) lands between round n-1 (slot
    /// 2n-1) and round n (slot 2n+1) without either of them having to know.
    ///
    /// A generated line whose round has gone goes last rather than nowhere -
    /// its status cell says what happened to it, and a line that vanishes
    /// silently is worse than one that explains itself.
    /// </summary>
    private static int SlotOf(ScheduleEntry entry, Dictionary<string, int> rank, int rounds)
    {
        if (entry.IsGenerated)
        {
            return rank.TryGetValue(entry.RoundId, out var at) ? at * 2 + 1 : int.MaxValue;
        }

        return EffectiveAnchor(entry, rounds) * 2;
    }

    /// <summary>
    /// A typed line's anchor, kept inside the programme it is anchored against.
    ///
    /// Clamped rather than corrected in the file: rounds come and go all
    /// evening, and rewriting everybody's anchor on every build would turn a
    /// read into a write. An anchor past the end simply means "at the end".
    /// </summary>
    private static int EffectiveAnchor(ScheduleEntry entry, int rounds) =>
        entry.AfterRounds < 0 ? rounds : Math.Clamp(entry.AfterRounds, 0, rounds);

    /// <summary>
    /// Gives every un-anchored hand-typed line the anchor its position in the
    /// file implies: the number of generated lines ahead of it.
    ///
    /// Run once, at load, per event. Returns true if anything changed, so the
    /// caller knows whether to save. GroupBy keeps source order inside a group,
    /// which is what makes "ahead of it" mean anything here.
    /// </summary>
    private static bool AnchorLegacyLines(List<ScheduleEntry> entries)
    {
        var changed = false;

        foreach (var group in entries.GroupBy(e => e.EventId, StringComparer.Ordinal))
        {
            var ahead = 0;

            foreach (var entry in group)
            {
                if (entry.IsGenerated)
                {
                    ahead++;
                    continue;
                }

                if (entry.AfterRounds < 0)
                {
                    entry.AfterRounds = ahead;
                    changed = true;
                }
            }
        }

        return changed;
    }

    /// <summary>Every line, whichever event it belongs to. For backup and for the migration.</summary>
    public IReadOnlyList<ScheduleEntry> Everything => _entries;

    /// <summary>The lines this event generated from its program, in program order.</summary>
    public IReadOnlyList<ScheduleEntry> Generated =>
        All.Where(e => e.IsGenerated).ToList();

    public void Add(ScheduleEntry entry)
    {
        if (string.IsNullOrEmpty(entry.EventId))
        {
            entry.EventId = CurrentEventId;
        }

        _entries.Add(entry);
        Save();
    }

    /// <summary>
    /// Brings this event's generated lines into line with the programme:
    /// a line per round, the round's own wording, and nothing else touched.
    ///
    /// Reconciled, not replaced, and that is the whole point. The old version
    /// deleted every generated line and appended a fresh set, which threw away
    /// three things that belong to the round rather than to the rebuild: the
    /// clock time somebody had typed into its slot, whether it had been ticked,
    /// and the moment it actually started. Applying a bump-up would wipe
    /// "started 19:42 · 3 min late" off every heat that had already run, and
    /// nothing said so. A round's line is the round's line; it is created once
    /// and it survives until the round does not.
    ///
    /// The club's own lines - doors open, lunch, presentation - are not the
    /// programme's to touch, and they keep their place through this because
    /// they are anchored by a count of rounds rather than by where they sit in
    /// the file. See ScheduleEntry.AfterRounds.
    ///
    /// Returns true if anything actually changed, so a caller can tell a real
    /// rebuild from a no-op.
    /// </summary>
    public bool SyncToProgram(IReadOnlyList<ProgramLine> program)
    {
        var changed = false;

        var byRound = new Dictionary<string, ScheduleEntry>(StringComparer.Ordinal);
        var wanted = new HashSet<string>(program.Select(p => p.RoundId), StringComparer.Ordinal);

        var generated = _entries
            .Where(e => string.Equals(e.EventId, CurrentEventId, StringComparison.Ordinal) && e.IsGenerated)
            .ToList();

        foreach (var line in generated)
        {
            // A round that has gone loses its line, and so does a second line
            // claiming a round that already has one - which nothing should
            // write, but a half-finished older rebuild could have left behind.
            if (!wanted.Contains(line.RoundId))
            {
                // Bug hunt 11 (2026-11-05): kept aside for an undo - see _parkedLines.
                _parkedLines[(CurrentEventId, line.RoundId)] = line;
                _entries.Remove(line);
                changed = true;
            }
            else if (!byRound.TryAdd(line.RoundId, line))
            {
                _entries.Remove(line);
                changed = true;
            }
        }

        foreach (var line in program)
        {
            // Bug hunt 11 (2026-11-05): a round back under its old id - an
            // undone delete or heats build - takes its own line back, with
            // its time, tick and start, unless a line already holds that id.
            if (!byRound.ContainsKey(line.RoundId)
                && _parkedLines.Remove((CurrentEventId, line.RoundId), out var parked)
                && !_entries.Any(e => string.Equals(e.Id, parked.Id, StringComparison.Ordinal)))
            {
                parked.EventId = CurrentEventId;
                _entries.Add(parked);
                byRound[line.RoundId] = parked;
                changed = true;
            }

            // Its line is on the sheet, so nothing parked for it is wanted.
            _parkedLines.Remove((CurrentEventId, line.RoundId));

            if (byRound.TryGetValue(line.RoundId, out var existing))
            {
                // Only the wording, and only when it moved - a round renamed
                // by a class rename, or a main whose tier changed.
                if (string.Equals(existing.ClassName, line.ClassName, StringComparison.Ordinal)
                    && string.Equals(existing.Description, line.Label, StringComparison.Ordinal))
                {
                    continue;
                }

                existing.ClassName = line.ClassName;
                existing.Description = line.Label;
                changed = true;

                continue;
            }

            _entries.Add(new ScheduleEntry
            {
                EventId = CurrentEventId,
                ClassName = line.ClassName,
                Description = line.Label,
                RoundId = line.RoundId,
            });

            changed = true;
        }

        if (changed)
        {
            Save();
        }

        return changed;
    }

    /// <summary>Marks the line for a round as started, which is what running late is measured from.</summary>
    public void MarkStarted(string roundId, DateTime startedUtc)
    {
        var line = _entries.FirstOrDefault(e =>
            string.Equals(e.EventId, CurrentEventId, StringComparison.Ordinal)
            && string.Equals(e.RoundId, roundId, StringComparison.Ordinal));

        if (line is null || line.ActualStartUtc is not null)
        {
            return;
        }

        line.ActualStartUtc = startedUtc;
        Save();
    }

    public void Update(ScheduleEntry entry)
    {
        var index = _entries.FindIndex(e => e.Id == entry.Id);
        if (index >= 0)
        {
            _entries[index] = entry;
            Save();
        }
    }

    /// <summary>
    /// Puts back a whole run of changed lines, writing the file ONCE.
    ///
    /// Update writes on every call, which is right for the one line a
    /// dialog changed and wrong for a pass down the sheet: filling in the
    /// times on a forty-line race day would otherwise be forty writes of
    /// the same file, most of them describing a half-finished state, and
    /// any of them able to fail on a locked disk partway through.
    /// </summary>
    public void UpdateMany(IEnumerable<ScheduleEntry> entries)
    {
        var changed = false;

        foreach (var entry in entries)
        {
            var index = _entries.FindIndex(e => e.Id == entry.Id);

            if (index >= 0)
            {
                _entries[index] = entry;
                changed = true;
            }
        }

        if (changed)
        {
            Save();
        }
    }

    /// <summary>One line's time before and after a pass that stamped it - what undoing that pass needs.</summary>
    public readonly record struct TimeChange(string Id, string Was, string Now);

    /// <summary>
    /// Puts back the times a pass down the sheet wrote, writing the file
    /// once. Returns how many lines were put back.
    ///
    /// Exists because Snapshot and Restore cannot undo a change made to a
    /// line in place. A snapshot holds the very same line objects the sheet
    /// does, so by the time an undo runs it already carries the new times,
    /// and Restore deliberately keeps a line still on the sheet as it is
    /// now. Undoing "Fill in the times" through them put the race day's
    /// start time back and left every line restamped, while saying it had
    /// worked. The old value has to be written down as text before the pass,
    /// which is what a TimeChange is.
    ///
    /// A line whose time has been changed again since - typed over by hand
    /// after the fill - is left alone, the same rule Restore keeps: an undo
    /// takes back the one thing it names, not an edit made after it. A line
    /// deleted since is simply skipped. Lines are found by id across every
    /// event, so an undo made after switching race days still reaches the
    /// day it was made on.
    /// </summary>
    public int RestoreTimes(IEnumerable<TimeChange> changes)
    {
        var restored = 0;

        foreach (var change in changes)
        {
            var line = _entries.FirstOrDefault(e => string.Equals(e.Id, change.Id, StringComparison.Ordinal));

            if (line is null || !string.Equals(line.ScheduledTime, change.Now, StringComparison.Ordinal))
            {
                continue;
            }

            line.ScheduledTime = change.Was;
            restored++;
        }

        if (restored > 0)
        {
            Save();
        }

        return restored;
    }

    public void Delete(string id)
    {
        _entries.RemoveAll(e => e.Id == id);
        Save();
    }

    public void SetComplete(string id, bool complete)
    {
        var entry = _entries.FirstOrDefault(e => e.Id == id);
        if (entry is not null)
        {
            entry.IsComplete = complete;

            // WHY (23 Oct 2026, bug hunt 10): a director's own tick or untick
            // is theirs - AutoCompleteRecorded never takes it back.
            _autoTicked.Remove(entry.Id);
            Save();
        }
    }

    /// <summary>
    /// Moves one of the club's own lines up (-1) or down (+1) the running
    /// order. Returns false when nothing moved.
    ///
    /// A generated line is not movable from here, and refuses rather than
    /// pretending: it IS a round, so it moves by moving the round on the
    /// programme, and the sheet follows. That refusal is the fix for the thing
    /// that made these two lists drift - there used to be a Move Up on each
    /// page, working against two separate orderings, and pressing the one on
    /// this page changed a sequence that the next rebuild silently discarded.
    ///
    /// Stepping over a round changes the anchor; stepping over another typed
    /// line in the same slot swaps the two where they are stored, which is the
    /// tie-break All sorts by.
    /// </summary>
    public bool MoveTypedLine(string id, int direction)
    {
        if (direction == 0)
        {
            return false;
        }

        var order = All;
        var at = -1;

        for (var i = 0; i < order.Count; i++)
        {
            if (string.Equals(order[i].Id, id, StringComparison.Ordinal))
            {
                at = i;
                break;
            }
        }

        if (at < 0 || order[at].IsGenerated)
        {
            return false;
        }

        var step = direction < 0 ? -1 : 1;
        var beside = at + step;

        if (beside < 0 || beside >= order.Count)
        {
            return false;
        }

        var entry = order[at];
        var neighbour = order[beside];
        var rounds = ProgramOrder?.Invoke().Count ?? 0;
        var mine = EffectiveAnchor(entry, rounds);

        if (neighbour.IsGenerated)
        {
            var moved = Math.Clamp(mine + step, 0, rounds);

            if (moved == mine)
            {
                return false;
            }

            // Into the slot on the round's far side - as its first line going
            // down, its last going up, which is the one place along. The slot
            // is shared with any lines already there, and a shared slot is
            // ordered by storage, which this used to leave alone: the line
            // landed wherever the file happened to hold it, several places
            // past where the press looked like it would go. Those lines sit
            // right beyond the round on screen; the nearest is put beside.
            // Storage first, as below, so a failure leaves the anchor alone.
            var across = beside + step;

            if (across >= 0 && across < order.Count
                && !order[across].IsGenerated
                && EffectiveAnchor(order[across], rounds) == moved
                && !PlaceBeside(entry, order[across], -step))
            {
                return false;
            }

            entry.AfterRounds = moved;
        }
        else if (EffectiveAnchor(neighbour, rounds) != mine)
        {
            // Adjacent on screen but anchored differently - two typed lines
            // with a round's slot between them that has no line of its own.
            //
            // Taking the neighbour's anchor puts them in the SAME slot, and
            // a shared slot is then broken by storage order, which this had
            // not touched. So the press either did nothing at all or jumped
            // the line two places, depending on which way round the two
            // happened to sit in the file - while the anchor it threw away
            // was the line's real place in the evening, so a round added
            // later moved it somewhere nobody had asked for. Put it beside
            // the neighbour in storage too, and the move is the one the
            // press looks like.
            // Storage first: a failure there must leave the anchor alone
            // too, or the line has half-moved with nothing written down.
            if (!PlaceBeside(entry, neighbour, step))
            {
                return false;
            }

            entry.AfterRounds = EffectiveAnchor(neighbour, rounds);
        }
        else
        {
            var here = _entries.FindIndex(e => string.Equals(e.Id, entry.Id, StringComparison.Ordinal));
            var there = _entries.FindIndex(e => string.Equals(e.Id, neighbour.Id, StringComparison.Ordinal));

            if (here < 0 || there < 0)
            {
                return false;
            }

            (_entries[here], _entries[there]) = (_entries[there], _entries[here]);
        }

        Save();
        return true;
    }

    /// <summary>
    /// Lifts a line out of the file and puts it back immediately before
    /// (<paramref name="step"/> negative) or after the given neighbour.
    /// False when either has gone since the order was read, which leaves
    /// the file untouched.
    ///
    /// Storage order is the tie-break All uses inside one slot, so this is
    /// what makes "up" and "down" mean anything for two lines that share
    /// one. Written as remove-then-insert rather than a swap because the
    /// two are not always next to each other in the file - only on screen.
    /// </summary>
    private bool PlaceBeside(ScheduleEntry entry, ScheduleEntry neighbour, int step)
    {
        var here = _entries.FindIndex(e => string.Equals(e.Id, entry.Id, StringComparison.Ordinal));

        if (here < 0 || _entries.FindIndex(e => string.Equals(e.Id, neighbour.Id, StringComparison.Ordinal)) < 0)
        {
            return false;
        }

        _entries.RemoveAt(here);

        // Found again AFTER the removal: taking the line out shifts every
        // index behind it, and inserting at the stale one puts it on the
        // wrong side of the neighbour.
        var there = _entries.FindIndex(e => string.Equals(e.Id, neighbour.Id, StringComparison.Ordinal));

        if (there < 0)
        {
            _entries.Insert(Math.Min(here, _entries.Count), entry);
            return false;
        }

        _entries.Insert(step < 0 ? there : there + 1, entry);
        return true;
    }

    /// <summary>Clears THIS event's timetable. Other events keep theirs.</summary>
    public void ClearAll()
    {
        _entries.RemoveAll(e => string.Equals(e.EventId, CurrentEventId, StringComparison.Ordinal));

        // Bug hunt 11 (2026-11-05): a cleared sheet stays cleared - a round
        // undeleted after this gets a fresh line, not one from before the clear.
        foreach (var key in _parkedLines.Keys.Where(k => string.Equals(k.EventId, CurrentEventId, StringComparison.Ordinal)).ToList())
        {
            _parkedLines.Remove(key);
        }

        Save();
    }

    /// <summary>
    /// Files every line that has no event onto the legacy one.
    ///
    /// Called by the migration at startup. Without it the old global
    /// schedule would simply vanish the first time the app opened after
    /// the change, which is a worse outcome than showing it under a
    /// clearly-labelled legacy heading.
    ///
    /// "No event" means a line written before lines carried an event at all
    /// - a blank EventId. It used to mean any event that could not be found,
    /// and as this runs on every launch, deleting an event (or undoing its
    /// creation) tipped its whole run sheet into the legacy timetable the
    /// next time the app opened. A line whose event has gone is left where
    /// it is: nothing shows it, and it comes back with the event if the
    /// delete is undone.
    /// </summary>
    public int AdoptOrphans()
    {
        var orphans = _entries.Where(e => string.IsNullOrEmpty(e.EventId)).ToList();

        foreach (var entry in orphans)
        {
            entry.EventId = RaceEventStore.LegacyEventId;
        }

        if (orphans.Count > 0)
        {
            Save();
        }

        return orphans.Count;
    }

    /// <summary>
    /// Follows a class being renamed, on every event's timetable rather than
    /// just the current one, so an old race day still reads with the class
    /// name the club now uses. Matched ignoring case, like class names
    /// everywhere else. The free-text description is left alone - it is
    /// the club's own wording. No-op (and no save) when no line matched.
    ///
    /// Returns the ids of the lines that moved, for the undo (see
    /// UndoRenameClass).
    /// </summary>
    public IReadOnlyList<string> RenameClass(string oldName, string newName)
    {
        var moved = new List<string>();

        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
        {
            return moved;
        }

        foreach (var entry in _entries.Where(e =>
                     string.Equals(e.ClassName, oldName, StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(e.ClassName, newName, StringComparison.Ordinal)))
        {
            entry.ClassName = newName;
            moved.Add(entry.Id);
        }

        if (moved.Count > 0)
        {
            Save();
        }

        return moved;
    }

    /// <summary>
    /// The ids of the lines carrying this class name now, on every event's
    /// timetable. Taken just before a rename, so its undo can leave alone a
    /// line that already carried the new name - a class is typed free onto
    /// a line, so "GT" can be on the run sheet before any class is called
    /// that.
    /// </summary>
    public IReadOnlyList<string> WithClassName(string className) =>
        _entries
            .Where(e => string.Equals(e.ClassName, className, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Id)
            .ToList();

    /// <summary>
    /// The undo half of RenameClass: every line carrying
    /// <paramref name="newName"/> NOW goes back to <paramref name="oldName"/>,
    /// except those in <paramref name="leaveAlone"/>, which already carried
    /// the new name before the rename.
    ///
    /// It used to be done by renaming back, which moved EVERY line with the
    /// new name to the old one, the ones that had it all along included.
    /// Judged by what each line holds now, like the drivers and rounds: a
    /// line changed to another class since keeps it, and a line added for
    /// the renamed class since goes back with it.
    /// </summary>
    public void UndoRenameClass(string newName, string oldName, IReadOnlyCollection<string> leaveAlone)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        var keep = leaveAlone.ToHashSet(StringComparer.Ordinal);
        var changed = false;

        foreach (var entry in _entries.Where(e =>
                     string.Equals(e.ClassName, newName, StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(e.ClassName, oldName, StringComparison.Ordinal)
                     && !keep.Contains(e.Id)))
        {
            entry.ClassName = oldName;
            changed = true;
        }

        if (changed)
        {
            Save();
        }
    }

    /// <summary>
    /// Ticks off every generated line whose round has been recorded - the
    /// self-maintenance a line earns by knowing which heat it is - and takes
    /// its own tick back off a line whose round is no longer recorded. One
    /// save however many lines changed.
    ///
    /// WHY (23 Oct 2026, bug hunt 10): it used to tick only, so deleting a
    /// heat's race left the heat unrun on the programme and done on the
    /// timetable. Only a tick this method made is undone (see _autoTicked):
    /// a line the director ticked by hand - a heat skipped, say - stays
    /// done. Undoing one also clears its start stamp, because MarkStarted
    /// never overwrites one and the re-run would otherwise be measured from
    /// the rehearsal's start time.
    ///
    /// WHY isKnown (13 Nov 2026, bug hunt 17): a round the programme cannot
    /// find is not the same as a round that is no longer recorded. With
    /// race-rounds.json locked at launch the programme loads empty, every
    /// round read as "not recorded", and tonight's whole sheet was unticked
    /// and its start times wiped and saved. A tick is only taken back when
    /// the round is there to say it is unrun.
    /// </summary>
    /// <param name="isRecorded">Whether a round has a race saved against it.</param>
    /// <param name="isKnown">Whether the programme has the round at all; left out, every round is taken as known.</param>
    public int AutoCompleteRecorded(Func<string, bool> isRecorded, Func<string, bool>? isKnown = null)
    {
        var changed = 0;

        foreach (var entry in _entries.Where(e =>
                     string.Equals(e.EventId, CurrentEventId, StringComparison.Ordinal)
                     && e.IsGenerated))
        {
            if (!entry.IsComplete)
            {
                // A line unticked some other way since is not ours any more.
                _autoTicked.Remove(entry.Id);

                if (isRecorded(entry.RoundId))
                {
                    entry.IsComplete = true;
                    _autoTicked.Add(entry.Id);

                    // The start an untick took off, back - see _startsTakenBack.
                    if (_startsTakenBack.Remove(entry.Id, out var started) && entry.ActualStartUtc is null)
                    {
                        entry.ActualStartUtc = started;
                    }

                    changed++;
                }
            }
            else if (_autoTicked.Contains(entry.Id) && !isRecorded(entry.RoundId)
                     && (isKnown is null || isKnown(entry.RoundId)))
            {
                entry.IsComplete = false;

                if (entry.ActualStartUtc is { } was)
                {
                    _startsTakenBack[entry.Id] = was;
                }

                entry.ActualStartUtc = null;
                _autoTicked.Remove(entry.Id);
                changed++;
            }
        }

        if (changed > 0)
        {
            Save();
        }

        return changed;
    }

    /// <summary>One event's run sheet as it stood, for undo - which event, as well as which lines.</summary>
    public sealed class ScheduleSnapshot
    {
        public ScheduleSnapshot(string eventId, List<ScheduleEntry> entries)
        {
            EventId = eventId;
            Entries = entries;
        }

        public string EventId { get; }

        public List<ScheduleEntry> Entries { get; }
    }

    /// <summary>Snapshot of this event's schedule - used to capture state before a clear, delete or rebuild so it can be undone.</summary>
    public ScheduleSnapshot Snapshot() => new(CurrentEventId, All.ToList());

    /// <summary>
    /// Puts an event's run sheet back as a Snapshot recorded it. Pairs with
    /// Snapshot to undo a Clear Schedule, a delete or a Build from Program.
    ///
    /// Into the event the snapshot came from, not whichever is current at
    /// the moment of the undo - an undo made after switching events used to
    /// move one event's run sheet into another's and wipe that one's own.
    /// Lines typed by hand since the snapshot are kept, after the restored
    /// ones: undoing a clear must not also throw away "lunch" added after
    /// it. Generated lines are not kept - they are exactly what undoing a
    /// Build from Program takes away. A line still on the sheet is kept as
    /// it is now, so an edit or a tick made since is not undone with it.
    /// </summary>
    public void Restore(ScheduleSnapshot snapshot)
    {
        var eventId = snapshot.EventId;

        var live = new Dictionary<string, ScheduleEntry>(StringComparer.Ordinal);
        foreach (var entry in _entries.Where(e => string.Equals(e.EventId, eventId, StringComparison.Ordinal)))
        {
            live.TryAdd(entry.Id, entry);
        }

        var restoredIds = new HashSet<string>(snapshot.Entries.Select(e => e.Id), StringComparer.Ordinal);

        var restored = snapshot.Entries
            .Select(e => live.TryGetValue(e.Id, out var current) ? current : e)
            .ToList();

        // In their order on the sheet.
        var typedSince = _entries
            .Where(e => string.Equals(e.EventId, eventId, StringComparison.Ordinal)
                        && !e.IsGenerated
                        && !restoredIds.Contains(e.Id))
            .ToList();

        _entries.RemoveAll(e => string.Equals(e.EventId, eventId, StringComparison.Ordinal));

        foreach (var entry in restored.Concat(typedSince))
        {
            entry.EventId = eventId;
            _entries.Add(entry);
        }

        Save();
    }

    /// <summary>
    /// First entry of THIS event not yet marked complete, in schedule
    /// order - what "up next" means throughout this feature.
    ///
    /// Asked of All rather than of the raw list: unscoped, the first
    /// unfinished line of some previous race day would be reported as what
    /// is next today.
    /// </summary>
    public ScheduleEntry? NextUp => All.FirstOrDefault(e => !e.IsComplete);

    /// <summary>
    /// How far behind its slot the day is running, in minutes - positive
    /// late, negative early. Null when nothing has started yet, or the line
    /// that did had no time against it.
    ///
    /// Measured from the LAST line actually started rather than from the
    /// first, because drift is a thing that accumulates and is corrected: a
    /// day forty minutes down at lunch and pulled back to ten by the mains
    /// is running ten late, not forty. That is also the number the timetable
    /// shows against each line - one calculation, ScheduleDrift.MinutesOff,
    /// which handles a club night running past midnight.
    ///
    /// Exists so the one number a director actually watches during a night
    /// can be on Today, where they already are, instead of only on a page
    /// they have to go and open.
    /// </summary>
    public int? MinutesLate()
    {
        // 17 Oct 2026, Bug Hunt 8, A25-3: every started line, timed or not.
        // Untimed ones used to be filtered out first, so a running line with
        // no time took the drift from an older timed one instead of giving
        // null; MinutesOff already returns null for a blank time.
        var started = All
            .Where(e => e.ActualStartUtc is not null)
            .OrderByDescending(e => e.ActualStartUtc)
            .FirstOrDefault();

        return started?.ActualStartUtc is { } when
            ? ScheduleDrift.MinutesOff(started.ScheduledTime, when.ToLocalTime())
            : null;
    }
}
