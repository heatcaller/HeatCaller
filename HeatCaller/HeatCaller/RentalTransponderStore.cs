using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// A club-owned transponder loaned to drivers who don't have their own.
///
/// Tracked separately from driver check-in because it's a different thing
/// with a different lifecycle: a driver checks in once for the day, whereas
/// a rental can change hands several times and - more to the point - has to
/// come back at the end.
/// </summary>
public sealed class RentalTransponder
{
    /// <summary>The transponder ID the decoder reports. This is the identity - it's what ties a loan to lap data.</summary>
    public required string Transponder { get; set; }

    /// <summary>Club asset label, e.g. "R4" or a sticker number. What people actually call it at the counter.</summary>
    public string AssetTag { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    /// <summary>Out of service - damaged, flat battery, lost. Kept rather than deleted so its history survives.</summary>
    public bool Retired { get; set; }

    /// <summary>Who has it now, or empty if it's on the shelf.</summary>
    public string CurrentHolderName { get; set; } = string.Empty;

    /// <summary>What was taken as security. Free text - clubs use anything from a driving licence to a car key.</summary>
    public string DepositHeld { get; set; } = string.Empty;

    public DateTime? CheckedOutUtc { get; set; }

    public bool IsOut => CheckedOutUtc is not null;

    /// <summary>Completed loans, most recent last.</summary>
    public List<RentalLoan> History { get; set; } = new();

    /// <summary>What to call this unit - the asset tag if it has one, otherwise the raw ID.</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(AssetTag) ? Transponder : $"{AssetTag} ({Transponder})";

    /// <summary>How long the current loan has been running.</summary>
    public TimeSpan? OutFor => CheckedOutUtc is { } since ? DateTime.UtcNow - since : null;
}

/// <summary>One completed loan.</summary>
public sealed class RentalLoan
{
    public string HolderName { get; set; } = string.Empty;
    public string DepositHeld { get; set; } = string.Empty;
    public DateTime OutUtc { get; set; }
    public DateTime InUtc { get; set; }
    public string ReturnNotes { get; set; } = string.Empty;
}

/// <summary>Loads/saves the club's rental transponders.</summary>
public sealed class RentalTransponderStore
{
    private readonly List<RentalTransponder> _units;

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "rental-transponders.json");

    private RentalTransponderStore(List<RentalTransponder> units)
    {
        _units = units;
    }

    public static RentalTransponderStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<List<RentalTransponder>>(SafeFile.ReadAllTextWithRetry(FilePath));
                if (loaded is not null)
                {
                    return new RentalTransponderStore(loaded);
                }
            }
        }
        catch (Exception ex)
        {
            // Unreadable file: start empty rather than refusing to open.
            SafeFile.PreserveUnreadable(FilePath, ex);
        }

        return new RentalTransponderStore(new List<RentalTransponder>());
    }

    public void Save()
    {
        SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(_units, new JsonSerializerOptions { WriteIndented = true }));
    }

    public IReadOnlyList<RentalTransponder> All =>
        _units.OrderBy(u => u.Retired).ThenBy(u => u.AssetTag).ThenBy(u => u.Transponder).ToList();

    /// <summary>Units currently on loan - what still has to come back.</summary>
    public IReadOnlyList<RentalTransponder> OutNow =>
        _units.Where(u => u.IsOut).OrderBy(u => u.CheckedOutUtc).ToList();

    public RentalTransponder? Get(string transponder) =>
        _units.FirstOrDefault(u => string.Equals(u.Transponder, transponder, StringComparison.OrdinalIgnoreCase));

    /// <summary>True if this transponder is a club rental rather than someone's own.</summary>
    public bool IsRental(string transponder) => Get(transponder) is not null;

    public void Upsert(RentalTransponder unit)
    {
        var index = _units.FindIndex(u => string.Equals(u.Transponder, unit.Transponder, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _units[index] = unit;
        }
        else
        {
            _units.Add(unit);
        }

        Save();
    }

    public void Delete(string transponder)
    {
        _units.RemoveAll(u => string.Equals(u.Transponder, transponder, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    /// <summary>Loans a unit out. Returns false if it's already out - double-issuing one transponder would corrupt lap data for both drivers.</summary>
    public bool CheckOut(string transponder, string holderName, string deposit)
    {
        var unit = Get(transponder);
        if (unit is null || unit.IsOut || unit.Retired)
        {
            return false;
        }

        unit.CurrentHolderName = holderName;
        unit.DepositHeld = deposit;
        unit.CheckedOutUtc = DateTime.UtcNow;
        Save();
        return true;
    }

    /// <summary>Takes a unit back and files the loan in its history.</summary>
    public bool CheckIn(string transponder, string returnNotes)
    {
        var unit = Get(transponder);
        if (unit is null || !unit.IsOut)
        {
            return false;
        }

        unit.History.Add(new RentalLoan
        {
            HolderName = unit.CurrentHolderName,
            DepositHeld = unit.DepositHeld,
            OutUtc = unit.CheckedOutUtc!.Value,
            InUtc = DateTime.UtcNow,
            ReturnNotes = returnNotes,
        });

        unit.CurrentHolderName = string.Empty;
        unit.DepositHeld = string.Empty;
        unit.CheckedOutUtc = null;
        Save();
        return true;
    }

    // --- Undo -------------------------------------------------------------
    //
    // Each undo puts back the one unit it is about, rather than restoring a
    // snapshot of the whole list: undoing an older step used to wipe out
    // every loan, return and edit made to any unit since.

    /// <summary>Where a unit sits in the list, so a deleted one can go back in the same place.</summary>
    public int IndexOf(string transponder) =>
        _units.FindIndex(u => string.Equals(u.Transponder, transponder, StringComparison.OrdinalIgnoreCase));

    /// <summary>Undo of a delete: the unit, history and all, back where it was - unless one on its transponder has been added since.</summary>
    public bool RestoreDeleted(RentalTransponder unit, int index)
    {
        if (Get(unit.Transponder) is not null)
        {
            return false;
        }

        _units.Insert(Math.Clamp(index, 0, _units.Count), unit);
        Save();
        return true;
    }

    /// <summary>Undo of an edit: the asset tag, notes and retired flag go back. Its loans are left as they are now.</summary>
    public bool RevertDetails(RentalTransponder before)
    {
        if (Get(before.Transponder) is not { } unit)
        {
            return false;
        }

        unit.AssetTag = before.AssetTag;
        unit.Notes = before.Notes;
        unit.Retired = before.Retired;
        Save();
        return true;
    }

    /// <summary>Undo of a check-out: back on the shelf - only while it is still out on that same loan.</summary>
    public bool UndoCheckOut(string transponder, DateTime checkedOutUtc)
    {
        if (Get(transponder) is not { } unit || unit.CheckedOutUtc != checkedOutUtc)
        {
            return false;
        }

        unit.CurrentHolderName = string.Empty;
        unit.DepositHeld = string.Empty;
        unit.CheckedOutUtc = null;
        Save();
        return true;
    }

    /// <summary>
    /// Undo of a check-in: the loan comes back out of the history and the
    /// unit is out with the same holder and deposit again - only while it is
    /// still on the shelf with that loan the last one filed.
    /// </summary>
    public bool UndoCheckIn(string transponder, RentalLoan loan)
    {
        if (Get(transponder) is not { } unit || unit.IsOut || unit.History.Count == 0)
        {
            return false;
        }

        var last = unit.History[^1];

        if (last.OutUtc != loan.OutUtc || last.InUtc != loan.InUtc)
        {
            return false;
        }

        unit.History.RemoveAt(unit.History.Count - 1);
        unit.CurrentHolderName = last.HolderName;
        unit.DepositHeld = last.DepositHeld;
        unit.CheckedOutUtc = last.OutUtc;
        Save();
        return true;
    }

    /// <summary>Snapshot for undo - loans are easy to click by accident and annoying to reconstruct.</summary>
    public string Snapshot() => JsonSerializer.Serialize(_units);

    public void Restore(string snapshot)
    {
        try
        {
            var restored = JsonSerializer.Deserialize<List<RentalTransponder>>(snapshot);
            if (restored is null)
            {
                return;
            }

            _units.Clear();
            _units.AddRange(restored);
            Save();
        }
        catch
        {
            // A failed restore leaves the current state alone.
        }
    }
}
