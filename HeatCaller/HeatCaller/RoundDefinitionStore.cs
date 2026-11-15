using System.Text.Json;

namespace HeatCaller;

/// <summary>Loads/saves the race program (all RoundDefinitions) to %AppData%\HeatCaller\race-rounds.json.</summary>
public sealed class RoundDefinitionStore
{
    private readonly List<RoundDefinition> _rounds;

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "race-rounds.json");

    private RoundDefinitionStore(List<RoundDefinition> rounds)
    {
        _rounds = rounds;
    }

    public static RoundDefinitionStore Load()
    {
        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<RoundDefinition>>(SafeFile.ReadAllTextWithRetry(FilePath));
                if (list is not null)
                {
                    // WHY (20 Oct 2026): a hand-edited or restored file can
                    // hold a null entry. It loaded without complaint and then
                    // every reader of the programme (All, Get, Add, the Round
                    // box, auto-advance) threw on it all night. Dropped here,
                    // the same as SeasonStore.Load does.
                    list.RemoveAll(r => r is null);
                    return new RoundDefinitionStore(list);
                }

                // Present, parsed, and not the shape this store writes. Not
                // a new install either, so the same rule applies. A file
                // holding the literal "null" parses to nothing without
                // throwing: it used to fall straight through to the empty
                // store below with the flag left false, so the first heat
                // added or recorded replaced the whole race programme.
                unreadable = true;
            }
        }
        catch (Exception ex)
        {
            // Corrupt/unreadable file: start fresh rather than crashing on launch.
            unreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
        }

        return new RoundDefinitionStore(new List<RoundDefinition>()) { LoadedUnreadable = unreadable };
    }

    /// <summary>
    /// True when the file was there at startup but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. What this store holds is then a
    /// guess, and nothing it does this session is saved over the club's
    /// real file.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    private void Save()
    {
        if (LoadedUnreadable)
        {
            // See LoadedUnreadable: the club's real file stays as it is.
            return;
        }

        var json = JsonSerializer.Serialize(_rounds, new JsonSerializerOptions { WriteIndented = true });
        SafeFile.WriteAllText(FilePath, json);
    }

    public IReadOnlyList<RoundDefinition> All => _rounds.OrderBy(r => r.Order).ToList();

    /// <summary>
    /// Every round this class runs in, in programme order.
    ///
    /// WHY (11 Nov 2026, Phase 4.1, mixed-class heats): a heat Stock and Mod
    /// share is one of Stock's rounds AND one of Mod's, so it is in both
    /// lists (MixedHeats.RunsClass). Whoever scores from the list scores only
    /// the class's own drivers in it - see MixedHeats.Counts. For a program
    /// with no shared heat this is the same list it always was.
    /// </summary>
    public IReadOnlyList<RoundDefinition> ForClass(string className) =>
        _rounds.Where(r => MixedHeats.RunsClass(r, className))
            .OrderBy(r => r.Order).ToList();

    /// <summary>
    /// Every round still to be run, in programme order.
    ///
    /// <paramref name="withinRoundIds"/> keeps the answer to ONE race day -
    /// pass the active event's RoundIds, or null for every day at once.
    /// Unscoped is right only where there is no real race day open; with
    /// one open it is wrong twice over. The "Round:" box on Race Control
    /// offered last month's unrun heats alongside tonight's, and
    /// auto-advance, which takes the first unrecorded round of the class
    /// that was racing, would pick one of them and file tonight's A-main
    /// against a heat from a meeting three weeks ago - which is also how a
    /// result stops counting towards the qualifying it was run for.
    ///
    /// Callers pass null when the active event is the legacy holder:
    /// nothing is scoped then, and filtering to the holder's own list would
    /// hide any round made since.
    /// </summary>
    public IReadOnlyList<RoundDefinition> Unrecorded(IReadOnlySet<string>? withinRoundIds = null) =>
        _rounds
            .Where(r => !r.IsRecorded && (withinRoundIds is null || withinRoundIds.Contains(r.Id)))
            .OrderBy(r => r.Order)
            .ToList();

    public RoundDefinition? Get(string id) => _rounds.FirstOrDefault(r => r.Id == id);

    public void Add(RoundDefinition round)
    {
        round.Order = _rounds.Count == 0 ? 0 : _rounds.Max(r => r.Order) + 1;
        _rounds.Add(round);
        Save();
    }

    public void Update(RoundDefinition round)
    {
        var idx = _rounds.FindIndex(r => r.Id == round.Id);
        if (idx >= 0)
        {
            _rounds[idx] = round;
            Save();
        }
    }

    public void Delete(string id)
    {
        _rounds.RemoveAll(r => r.Id == id);
        Save();
    }

    /// <summary>
    /// Puts rounds back exactly as they were, KEEPING their Order - for an
    /// undo of something that deleted several of them.
    ///
    /// Add cannot do this: it hands out a fresh Order at the end of the
    /// list, which is right for a round being created and wrong for one
    /// being restored. Undoing a delete with Add would have put the day's
    /// rounds back in the store but at the bottom of the running order, all
    /// of them after every round that came later - the heats back, the
    /// evening's shape gone.
    ///
    /// Skips any id already there, so an undo pressed twice, or after the
    /// round was rebuilt by hand, does not duplicate it.
    /// </summary>
    public void RestoreAll(IEnumerable<RoundDefinition> rounds)
    {
        var added = false;

        foreach (var round in rounds)
        {
            if (_rounds.Any(r => string.Equals(r.Id, round.Id, StringComparison.Ordinal)))
            {
                continue;
            }

            _rounds.Add(round);
            added = true;
        }

        if (added)
        {
            Save();
        }
    }

    /// <summary>
    /// Puts deleted rounds back WHERE THEY WERE, for undo.
    ///
    /// Add is no good for this: it hands out the next number at the end of
    /// the programme, so an undone delete came back as the last round of
    /// the day instead of in its slot. Each round here keeps the Order it
    /// had when it was taken out. Only when that number is now taken - a
    /// reorder since renumbers from zero - is everything from that point
    /// on moved down one to make room, which lands it in front of whatever
    /// took its place. When the number is free nothing else moves, so
    /// restoring several rounds in a row cannot push a later one past the
    /// round it used to follow.
    ///
    /// Rounds are put back lowest Order first for the same reason, and a
    /// round that is somehow still in the store is left alone rather than
    /// added twice.
    /// </summary>
    public void Restore(IEnumerable<RoundDefinition> rounds)
    {
        var changed = false;

        foreach (var round in rounds.OrderBy(r => r.Order).ToList())
        {
            if (_rounds.Any(r => r.Id == round.Id))
            {
                continue;
            }

            if (_rounds.Any(r => r.Order == round.Order))
            {
                foreach (var later in _rounds.Where(r => r.Order >= round.Order))
                {
                    later.Order++;
                }
            }

            _rounds.Add(round);
            changed = true;
        }

        if (changed)
        {
            Save();
        }
    }

    /// <summary>
    /// Moves every round from one class name to another, for a class
    /// rename. Rounds hold their class by name, so without this a renamed
    /// class lost its whole programme and its qualifying with it. Returns
    /// the ids of the rounds that moved, so an undo can move back exactly
    /// those and nothing that already carried the new name.
    /// </summary>
    public IReadOnlyList<string> RenameClass(string oldName, string newName)
    {
        var moved = new List<string>();

        foreach (var round in _rounds)
        {
            if (string.Equals(round.ClassName, oldName, StringComparison.OrdinalIgnoreCase))
            {
                round.ClassName = newName;
                moved.Add(round.Id);
            }

            // WHY (11 Nov 2026, Phase 4.1): a class sharing a heat is named
            // there too, and in who-runs-in-what. Missed, a renamed Mod would
            // drop out of every heat it shares with Stock.
            if (RenameShared(round, oldName, newName) && !moved.Contains(round.Id))
            {
                moved.Add(round.Id);
            }

            // Bug hunt 19: a marshal no-show names the class whose best
            // qualifier it costs (MarshalRule.ForfeitsFor). Left on the old
            // name, a mid-night rename handed the driver that result back.
            if (RenameNoShows(round, oldName, newName) && !moved.Contains(round.Id))
            {
                moved.Add(round.Id);
            }
        }

        if (moved.Count > 0)
        {
            Save();
        }

        return moved;
    }

    /// <summary>A class renamed on the round's marshal no-shows (bug hunt 19). True when anything changed.</summary>
    private static bool RenameNoShows(RoundDefinition round, string from, string to)
    {
        var changed = false;

        foreach (var noShow in round.NoShowMarshals ?? new List<MarshalNoShow>())
        {
            if (string.Equals(noShow.ClassName?.Trim(), from.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                noShow.ClassName = to;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>A class renamed where it shares a heat (Phase 4.1). True when anything changed.</summary>
    private static bool RenameShared(RoundDefinition round, string from, string to)
    {
        var changed = false;

        if (round.SharedClasses is { Count: > 0 } others)
        {
            for (var i = 0; i < others.Count; i++)
            {
                if (string.Equals(others[i]?.Trim(), from.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    others[i] = to;
                    changed = true;
                }
            }
        }

        if (round.SharedClassOf is { Count: > 0 } map)
        {
            foreach (var key in map.Keys.ToList())
            {
                if (string.Equals(map[key]?.Trim(), from.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    map[key] = to;
                    changed = true;
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// The ids of the rounds carrying this class name now. Taken just
    /// before a rename, so its undo can leave alone a round that was
    /// already under the new name.
    /// </summary>
    public IReadOnlyList<string> WithClassName(string className) =>
        _rounds
            .Where(r => string.Equals(r.ClassName, className, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Id)
            .ToList();

    /// <summary>
    /// The undo half of RenameClass: every round carrying
    /// <paramref name="newName"/> NOW goes back to <paramref name="oldName"/>,
    /// except those in <paramref name="leaveAlone"/>, which already carried
    /// the new name before the rename. Judged by what each round holds now,
    /// so a round built for the renamed class after the rename goes back
    /// with it instead of being stranded under a name that stops existing.
    /// </summary>
    public void UndoRenameClass(string newName, string oldName, IReadOnlyCollection<string> leaveAlone)
    {
        var keep = leaveAlone.ToHashSet(StringComparer.Ordinal);
        var changed = false;

        foreach (var round in _rounds.Where(r => string.Equals(r.ClassName, newName, StringComparison.OrdinalIgnoreCase)
                                                 && !keep.Contains(r.Id)))
        {
            round.ClassName = oldName;
            changed = true;
        }

        // The shared half of RenameClass (Phase 4.1), by the same rule.
        foreach (var round in _rounds.Where(r => !keep.Contains(r.Id)))
        {
            changed |= RenameShared(round, newName, oldName);
            changed |= RenameNoShows(round, newName, oldName);
        }

        if (changed)
        {
            Save();
        }
    }

    /// <summary>
    /// Removes not-yet-recorded Qualifier rounds for a class - used before
    /// regenerating heats.
    ///
    /// <paramref name="withinRoundIds"/> keeps the clear to those rounds,
    /// which is how a build stays inside today's race day: the store holds
    /// every day's rounds, and an unrun heat from an earlier day belongs to
    /// that day, not to tonight's rebuild. Null clears the whole class, as
    /// this always did.
    /// </summary>
    public void ClearUnrecordedQualifiers(string className, IReadOnlySet<string>? withinRoundIds = null)
    {
        // A heat this class shares (11 Nov 2026, Phase 4.1) is one of its
        // heats, so a rebuild replaces it - and the build makes it again.
        _rounds.RemoveAll(r => MixedHeats.RunsClass(r, className)
            && r.Kind == RoundKind.Qualifier && !r.IsRecorded
            && (withinRoundIds is null || withinRoundIds.Contains(r.Id)));
        Save();
    }

    /// <summary>
    /// Links a round to the session that recorded it. Returns false if the
    /// round isn't in the store - callers were logging success either way,
    /// which made a failed link indistinguishable from a working one.
    /// </summary>
    public bool MarkRecorded(string roundId, string sessionId)
    {
        var round = Get(roundId);
        if (round is null)
        {
            return false;
        }

        // When the result was saved (12 Nov 2026, setup plan step 8): the
        // filing part of the gap after this race, for NightPace. The first
        // save only - linking the same session again (a correction) isn't
        // the result going in.
        if (!string.Equals(round.LinkedSessionId, sessionId, StringComparison.Ordinal) || round.SavedAtUtc is null)
        {
            round.SavedAtUtc = DateTime.UtcNow;
        }

        round.LinkedSessionId = sessionId;
        Update(round);
        return true;
    }

    /// <summary>
    /// Unlinks every round pointing at a session that is no longer in
    /// history. Returns the rounds it cleared, so an undo can put the
    /// links back exactly as they were.
    ///
    /// Deleting a race left its round still claiming to be recorded -
    /// IsRecorded is just "has a session id" - and pointing at a session
    /// nobody can fetch. The round then stayed out of the "Round:" box and
    /// out of auto-advance, so the race could not simply be re-run; the
    /// programme showed it done with no result behind it; qualifying
    /// counted the round and scored nothing for it, which is not the same
    /// as not counting it at all; and the event report read the heat as
    /// run. The only way back was to build the round again by hand.
    /// </summary>
    public IReadOnlyList<string> ClearRecorded(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return Array.Empty<string>();
        }

        var cleared = _rounds
            .Where(r => string.Equals(r.LinkedSessionId, sessionId, StringComparison.Ordinal))
            .ToList();

        if (cleared.Count == 0)
        {
            return Array.Empty<string>();
        }

        foreach (var round in cleared)
        {
            round.LinkedSessionId = null;
        }

        Save();

        return cleared.Select(r => r.Id).ToList();
    }

    /// <summary>
    /// Moves a block of rounds so that it sits immediately in front of
    /// <paramref name="anchorId"/>, or at the very end when that is null.
    ///
    /// An ANCHOR rather than an index, deliberately. The Program tab can be
    /// filtered to one class, so the row somebody drops onto is row 3 of
    /// what they can see and round 11 of the programme - and an index into
    /// a list that is about to have rounds lifted out of it stops meaning
    /// anything the moment the first one is removed. Naming the round to
    /// land in front of is true in both lists and survives the removal.
    ///
    /// The moved rounds keep their order relative to each other, which is
    /// what makes dragging a multi-row selection behave the way it looks.
    /// </summary>
    public void MoveBefore(IReadOnlyList<string> ids, string? anchorId)
    {
        var moving = ids
            .Select(Get)
            .OfType<RoundDefinition>()
            .OrderBy(r => r.Order)
            .ToList();

        if (moving.Count == 0)
        {
            return;
        }

        var movingIds = moving.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        var rest = All.Where(r => !movingIds.Contains(r.Id)).ToList();

        // Dropped onto one of the rows being dragged: the anchor went with
        // the block, so there is nothing to land in front of. Falling
        // through to the end would be wrong - that is a move nobody asked
        // for - so leave the programme alone.
        if (anchorId is not null && movingIds.Contains(anchorId))
        {
            return;
        }

        var at = anchorId is null ? rest.Count : rest.FindIndex(r => r.Id == anchorId);

        if (at < 0)
        {
            at = rest.Count;
        }

        rest.InsertRange(at, moving);

        // Renumbered from zero rather than nudged. Order values have
        // arrived from several places over the life of a programme and are
        // not guaranteed contiguous or even unique; a reorder is the
        // natural moment to make them both, and it costs one pass.
        for (var i = 0; i < rest.Count; i++)
        {
            rest[i].Order = i;
        }

        Save();
    }

    /// <summary>
    /// Every round's place in the running order, by id - taken before a
    /// reorder so Undo can put the order back with RestoreOrder.
    /// </summary>
    public Dictionary<string, int> OrderSnapshot()
    {
        var orders = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var round in _rounds)
        {
            orders[round.Id] = round.Order;
        }

        return orders;
    }

    /// <summary>
    /// The undo half of a reorder. The whole programme's order rather than
    /// just the rounds that were moved, because MoveBefore renumbers every
    /// round from zero. Only rounds still in the store are touched: one
    /// deleted since stays deleted, and one added since keeps the Order Add
    /// gave it, at or near the end.
    /// </summary>
    public void RestoreOrder(IReadOnlyDictionary<string, int> orders)
    {
        var changed = false;

        foreach (var round in _rounds)
        {
            if (orders.TryGetValue(round.Id, out var order) && round.Order != order)
            {
                round.Order = order;
                changed = true;
            }
        }

        if (changed)
        {
            Save();
        }
    }

    // MoveUp and MoveDown used to live here, swapping Order values with the
    // neighbour in the WHOLE store. They have gone: the Program tab can be
    // filtered to one class, and swapping with a neighbour nobody can see
    // looked from the screen like the button had done nothing. Move Up,
    // Move Down and a drag all now go through MoveBefore above, against the
    // list the user is actually looking at.
}
