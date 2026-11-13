using System.Globalization;
using System.Text.Json;

namespace HeatCaller;

/// <summary>How a driver's entry was settled at the desk.</summary>
public enum PaidHow
{
    /// <summary>Not paid yet.</summary>
    Owing,

    Cash,

    Card,

    /// <summary>Free tonight: a marshal, a volunteer, a junior - the note says which.</summary>
    Comp,
}

/// <summary>One driver's entry on one race day.</summary>
public sealed class EntryPayment
{
    public PaidHow How { get; set; }

    /// <summary>What was taken, in the club's money. Zero for a comp or owing.</summary>
    public decimal Amount { get; set; }

    /// <summary>For a comp: why ("Marshal", "Junior"). Optional.</summary>
    public string Note { get; set; } = string.Empty;

    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// MONEY AT THE DESK (11 Nov 2026, race-night build plan, Phase 4) - tracked
/// by hand only, as decided: HeatCaller never takes a payment. It records
/// who paid, how, and who is still owing, so the treasurer's count at the end
/// of the night is a number on the report rather than a shoebox.
///
/// The fees are the race day's (RaceEvent.EntryFee and ExtraClassFee): the
/// first class, then each class after it. Zero entry fee means the club
/// doesn't use this, and nothing about money is shown anywhere.
/// </summary>
public static class DeskMoney
{
    /// <summary>What a driver in <paramref name="classes"/> classes owes tonight.</summary>
    public static decimal FeeFor(int classes, decimal entryFee, decimal extraClassFee) =>
        classes <= 0 || entryFee <= 0 ? 0 : entryFee + Math.Max(0, classes - 1) * Math.Max(0, extraClassFee);

    /// <summary>
    /// One of tonight's classes as the desk prices it (12 Nov 2026, setup plan
    /// step 8): what it costs as a driver's entry, and as an extra class.
    /// </summary>
    public readonly record struct ClassFee(decimal Entry, decimal Extra);

    /// <summary>
    /// What a class costs tonight: its own fees where it has them, the race
    /// day's where it hasn't. An extra class with its own entry fee and no
    /// extra fee of its own costs the day's extra-class fee or its own entry
    /// fee, whichever is less - a $5 Juniors class is never charged $10 as
    /// somebody's second class.
    /// </summary>
    public static ClassFee FeeOf(RaceClass? raceClass, decimal dayEntryFee, decimal dayExtraClassFee)
    {
        var entry = Math.Max(0, raceClass?.EntryFee ?? dayEntryFee);

        var extra = raceClass?.ExtraClassFee is { } own
            ? own
            : raceClass?.EntryFee is not null ? Math.Min(dayExtraClassFee, entry) : dayExtraClassFee;

        return new ClassFee(entry, Math.Max(0, extra));
    }

    /// <summary>
    /// What a driver owes for these classes tonight (12 Nov 2026, setup plan
    /// step 8): the DEAREST class's entry fee, then each other class at its
    /// extra-class fee.
    ///
    /// WHY THE DEAREST FIRST: with one fee for the night it didn't matter
    /// which class was "first". With "Juniors $5, Mod $20" it does, and the
    /// rule has to be one the desk can say out loud and get the same answer
    /// as the screen: your entry is your dearest class, and anything else you
    /// race tonight is an extra. It's also what a price board means by "first
    /// class $20, each extra $10". With no class fees of its own this is
    /// exactly the old rule - entry fee plus the extra fee per class after the
    /// first, and nothing at all when the entry fee is nothing.
    /// </summary>
    public static decimal FeeFor(IEnumerable<ClassFee> classes)
    {
        var list = classes.OrderByDescending(c => c.Entry).ToList();

        if (list.Count == 0 || list[0].Entry <= 0)
        {
            return 0;
        }

        return list[0].Entry + list.Skip(1).Sum(c => c.Extra);
    }

    /// <summary>
    /// Whether tonight charges anything at all (12 Nov 2026, setup plan step
    /// 8): the day has an entry fee, or one of its classes has its own. What
    /// shows the Paid column, the Driver Paid button and the report's money.
    /// </summary>
    public static bool Charges(RaceEvent? day, Func<string, RaceClass?> classNamed) =>
        day is { IsLegacy: false } real
        && (real.EntryFee > 0 || (real.ClassNames ?? new List<string>()).Any(name => classNamed(name)?.EntryFee is > 0));

    /// <summary>
    /// A class's own fees in a few words, for the Classes page - "$5.00",
    /// "$20.00, $10.00 as an extra class" - or "" when it charges the race
    /// day's (12 Nov 2026, setup plan step 8).
    /// </summary>
    public static string ClassFeeWords(RaceClass raceClass) => (raceClass.EntryFee, raceClass.ExtraClassFee) switch
    {
        ({ } entry, { } extra) => $"{Money(entry)}, {Money(extra)} as an extra class",
        ({ } entry, null) => Money(entry),
        (null, { } extra) => $"{Money(extra)} as an extra class",
        _ => string.Empty,
    };

    /// <summary>
    /// What tonight's classes charged, for the event report - "Fees: Juniors
    /// $5.00, Mod $20.00; the rest $15.00; other extra classes $10.00." - or null
    /// when no class of tonight's has a fee of its own, where the day's fee
    /// is all there is to say (12 Nov 2026, setup plan step 8).
    /// </summary>
    public static string? FeesLine(RaceEvent day, Func<string, RaceClass?> classNamed)
    {
        var own = (day.ClassNames ?? new List<string>())
            .Select(classNamed)
            .OfType<RaceClass>()
            .Where(c => c.EntryFee is not null || c.ExtraClassFee is not null)
            .ToList();

        if (own.Count == 0)
        {
            return null;
        }

        var parts = new List<string> { string.Join(", ", own.Select(c => $"{c.Name} {ClassFeeWords(c)}")) };

        if (own.Count < (day.ClassNames?.Count ?? 0))
        {
            parts.Add($"the rest {Money(day.EntryFee)}");
        }

        parts.Add($"other extra classes {Money(day.ExtraClassFee)}");

        return $"Fees: {string.Join("; ", parts)}.";
    }

    /// <summary>A class by name, ignoring case - how the desk finds a class's fees.</summary>
    public static Func<string, RaceClass?> ClassLookup(IEnumerable<RaceClass>? classes)
    {
        var all = (classes ?? Enumerable.Empty<RaceClass>()).ToList();

        return name => all.FirstOrDefault(c => string.Equals(c.Name?.Trim(), name?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The night's money, as the report and the closing checklist give it.</summary>
    /// <param name="Cash">Cash taken.</param>
    /// <param name="Card">Card taken.</param>
    /// <param name="Comps">Drivers let in free.</param>
    /// <param name="Owing">Drivers still owing, with what each owes.</param>
    public sealed record Totals(decimal Cash, decimal Card, int Comps, IReadOnlyList<(string Name, decimal Owes)> Owing)
    {
        public decimal Taken => Cash + Card;

        public decimal OwingAmount => Owing.Sum(o => o.Owes);
    }

    /// <summary>
    /// The night's money from tonight's drivers - each with their name, what
    /// they owe and how they settled (null for not at all).
    /// </summary>
    public static Totals Count(IEnumerable<(string Name, decimal Fee, EntryPayment? Payment)> drivers)
    {
        decimal cash = 0, card = 0;
        var comps = 0;
        var owing = new List<(string, decimal)>();

        foreach (var (name, fee, payment) in drivers)
        {
            switch (payment?.How ?? PaidHow.Owing)
            {
                case PaidHow.Cash:
                    cash += payment!.Amount;
                    break;
                case PaidHow.Card:
                    card += payment!.Amount;
                    break;
                case PaidHow.Comp:
                    comps++;
                    break;
                default:
                    if (fee > 0)
                    {
                        owing.Add((name, fee));
                    }

                    break;
            }
        }

        return new Totals(cash, card, comps, owing);
    }

    /// <summary>Money as the club writes it, in this PC's currency format: "$15.00".</summary>
    public static string Money(decimal amount) => amount.ToString("C", CultureInfo.CurrentCulture);

    /// <summary>The Paid column: "Cash $15.00", "Card $25.00", "Comp - Marshal", "Owes $15.00", or "" with nothing owed.</summary>
    public static string Words(EntryPayment? payment, decimal fee) => (payment?.How ?? PaidHow.Owing) switch
    {
        PaidHow.Cash => $"Cash {Money(payment!.Amount)}",
        PaidHow.Card => $"Card {Money(payment!.Amount)}",
        PaidHow.Comp => payment!.Note.Length > 0 ? $"Comp - {payment.Note}" : "Comp",
        _ => fee > 0 ? $"Owes {Money(fee)}" : string.Empty,
    };
}

/// <summary>Who paid what, per race day. %AppData%\HeatCaller\entry-payments.json.</summary>
public sealed class EntryPaymentStore
{
    private readonly Dictionary<string, Dictionary<string, EntryPayment>> _byEvent;
    private readonly string? _path;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private EntryPaymentStore(Dictionary<string, Dictionary<string, EntryPayment>> byEvent, string? path)
    {
        _byEvent = byEvent;
        _path = path;
    }

    /// <summary>An empty store that never saves - for tests.</summary>
    public static EntryPaymentStore InMemory() => new(new Dictionary<string, Dictionary<string, EntryPayment>>(StringComparer.Ordinal), null);

    public static EntryPaymentStore Load()
    {
        var path = Path.Combine(AppPaths.Root, "entry-payments.json");
        var loaded = new Dictionary<string, Dictionary<string, EntryPayment>>(StringComparer.Ordinal);

        try
        {
            if (File.Exists(path)
                && JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, EntryPayment>>>(File.ReadAllText(path)) is { } read)
            {
                foreach (var (eventId, payments) in read)
                {
                    if (payments is not null)
                    {
                        loaded[eventId] = new Dictionary<string, EntryPayment>(
                            payments.Where(p => p.Value is not null), StringComparer.OrdinalIgnoreCase);
                    }
                }
            }
        }
        catch (Exception)
        {
            // A file that can't be read leaves the desk with nothing marked
            // paid, which the treasurer will notice; it never stops the app.
        }

        return new EntryPaymentStore(loaded, path);
    }

    /// <summary>Raised after any change, for the desk and the report.</summary>
    public event Action? Changed;

    /// <summary>How this driver settled on this race day, or null for not at all.</summary>
    public EntryPayment? Get(string eventId, string transponder) =>
        _byEvent.TryGetValue(eventId, out var day) && day.TryGetValue(transponder, out var payment) ? payment : null;

    /// <summary>Records how a driver settled; null clears it back to owing.</summary>
    public void Set(string eventId, string transponder, EntryPayment? payment)
    {
        if (!_byEvent.TryGetValue(eventId, out var day))
        {
            day = new Dictionary<string, EntryPayment>(StringComparer.OrdinalIgnoreCase);
            _byEvent[eventId] = day;
        }

        if (payment is null)
        {
            day.Remove(transponder);
        }
        else
        {
            day[transponder] = payment;
        }

        Save();
        Changed?.Invoke();
    }

    private void Save()
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            SafeFile.WriteAllText(_path, JsonSerializer.Serialize(_byEvent, Json));
        }
        catch (Exception)
        {
            // As with the other stores: a failed save is retried with the next change.
        }
    }
}
