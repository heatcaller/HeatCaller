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

    /// <summary>
    /// Of Amount, the part taken the other way: card when How is Cash, cash
    /// when How is Card. Zero - as in every file saved before it - when it
    /// was all taken one way.
    ///
    /// WHY (15 Nov 2026, bug hunt 21): a driver who paid $15 cash and later
    /// $10 card for a class ticked since was rewritten as "Card $25", and the
    /// cash box came up $15 short of the night's count. See DeskMoney.Settle.
    /// </summary>
    public decimal OtherWay { get; set; }

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
    ///
    /// With no classes ticked every class runs, and each driver pays for
    /// their main class (CheckInControl.FeeFor) - so any of the club's
    /// classes with its own fee counts (bug hunt 20). Only checked was the
    /// ticked list, which is empty then, so a club pricing only its classes
    /// tracked no money that night though each driver's fee came out at $20.
    /// </summary>
    public static bool Charges(RaceEvent? day, Func<string, RaceClass?> classNamed, IEnumerable<RaceClass>? clubClasses = null) =>
        day is { IsLegacy: false } real
        && (real.EntryFee > 0
            || (real.ClassNames is { Count: > 0 } ticked
                ? ticked.Any(name => classNamed(name)?.EntryFee is > 0)
                : (clubClasses ?? Enumerable.Empty<RaceClass>()).Any(c => c.EntryFee is > 0)));

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
            // Paid for less than the fee is now - a class ticked since: the rest is owed.
            if (payment?.How is PaidHow.Cash or PaidHow.Card && StillOwed(payment, fee) is > 0 and var more)
            {
                owing.Add((name, more));
            }

            switch (payment?.How ?? PaidHow.Owing)
            {
                case PaidHow.Cash or PaidHow.Card:
                    cash += CashOf(payment);
                    card += CardOf(payment);
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

    /// <summary>A driver's own entry money for their phone: the fee in words, whether it's settled, and the line the phone shows.</summary>
    public sealed record PhoneMoney(string Fee, bool Paid, string Words);

    /// <summary>
    /// What a driver's own check-in card and Me page say about tonight's
    /// entry (12 Nov 2026, setup plan step 8): "Paid ✓" once the desk has
    /// them as paid by cash or card or comped, else "Entry: $15.00 - pay at
    /// the desk". Null on a night with no fee - nothing is said. Never how
    /// they paid or how much anyone else owes: it goes only in their own
    /// private live entry, behind their badge code.
    /// </summary>
    public static PhoneMoney? ForPhone(decimal fee, EntryPayment? payment)
    {
        if (fee <= 0)
        {
            return null;
        }

        var owed = StillOwed(payment, fee);

        var money = Money(fee);

        return owed <= 0
            // "Thank you" since 15 Nov 2026 (Asher: "add "thank you" to the paid note").
            ? new PhoneMoney(money, true, "Paid ✓ - thank you!")
            : new PhoneMoney(money, false, $"Entry: {Money(owed)} - pay at the desk");
    }

    /// <summary>
    /// What a driver still owes tonight against their fee now: all of it when
    /// they haven't paid, nothing when comped, and when paid by cash or card
    /// whatever the fee has gone up by since (bug hunt 20) - a second class
    /// ticked after paying for one left its extra-class fee uncollected, the
    /// driver showing as paid and ready.
    /// </summary>
    public static decimal StillOwed(EntryPayment? payment, decimal fee) => (payment?.How ?? PaidHow.Owing) switch
    {
        PaidHow.Comp => 0,
        PaidHow.Cash or PaidHow.Card => Math.Max(0, fee - payment!.Amount),
        _ => Math.Max(0, fee),
    };

    /// <summary>The part of a payment taken in cash - see EntryPayment.OtherWay.</summary>
    public static decimal CashOf(EntryPayment? payment) => payment?.How switch
    {
        PaidHow.Cash => payment!.Amount - OtherWayOf(payment),
        PaidHow.Card => OtherWayOf(payment!),
        _ => 0,
    };

    /// <summary>The part of a payment taken by card - see EntryPayment.OtherWay.</summary>
    public static decimal CardOf(EntryPayment? payment) => payment?.How switch
    {
        PaidHow.Card => payment!.Amount - OtherWayOf(payment),
        PaidHow.Cash => OtherWayOf(payment!),
        _ => 0,
    };

    /// <summary>OtherWay as far as it can be true: never below nothing, nor more than was taken.</summary>
    private static decimal OtherWayOf(EntryPayment payment) => Math.Clamp(payment.OtherWay, 0, Math.Max(0, payment.Amount));

    /// <summary>
    /// A driver settled one way at the desk: the payment to file (null for
    /// still owing), and what was taken just now.
    ///
    /// A driver already paid by cash or card who owes more - a class ticked
    /// since - is a TOP-UP (15 Nov 2026, bug hunt 21): only what they still
    /// owe is taken, and it is kept by the way it was taken, beside what they
    /// paid before. It used to file the whole fee again under the new way, so
    /// "$15 cash, then $10 card" read "Card $25" and the cash box was out.
    ///
    /// Anyone else - owing, comped, or a driver who has paid in full being
    /// put right ("that was card, not cash") - is filed as the whole fee
    /// taken this way, as it always was.
    /// </summary>
    public static (EntryPayment? Payment, decimal Taken, bool TopUp) Settle(EntryPayment? before, PaidHow how, decimal fee, string note, DateTime atUtc)
    {
        switch (how)
        {
            case PaidHow.Owing:
                return (null, 0, false);
            case PaidHow.Comp:
                return (new EntryPayment { How = how, Amount = 0, Note = note, AtUtc = atUtc }, 0, false);
        }

        if (before?.How is PaidHow.Cash or PaidHow.Card && StillOwed(before, fee) is > 0 and var more)
        {
            var cash = CashOf(before) + (how == PaidHow.Cash ? more : 0);
            var card = CardOf(before) + (how == PaidHow.Card ? more : 0);

            return (new EntryPayment
            {
                How = how,
                Amount = cash + card,
                OtherWay = how == PaidHow.Cash ? card : cash,
                Note = note,
                AtUtc = atUtc,
            }, more, true);
        }

        return (new EntryPayment { How = how, Amount = fee, Note = note, AtUtc = atUtc }, fee, false);
    }

    /// <summary>
    /// Of the drivers picked for Paid Cash or Paid Card, the ones a press
    /// should settle: when anybody picked still owes, only those; when nobody
    /// does, everybody (the desk putting right how somebody paid). Comp and
    /// Still Owing are explicit changes of state, so they take everybody.
    ///
    /// WHY (23 Nov 2026, bug hunt 24): the right-click menu and the picked
    /// bar settled every driver picked. A family of three with one already
    /// paid "Cash $15.00 + Card $10.00" and two owing, then Paid Card, refiled
    /// the settled one as "Card $25.00" - cash $15 under and card $15 over the
    /// till - and the same loop wrote Cash over a comp and lost its reason.
    /// The Driver Paid slip already kept to this rule (bug hunt 17).
    /// </summary>
    public static IReadOnlyList<T> WhoToSettle<T>(IReadOnlyList<T> picked, PaidHow how, Func<T, EntryPayment?> paymentOf, Func<T, decimal> feeOf)
    {
        if (how is not (PaidHow.Cash or PaidHow.Card))
        {
            return picked.ToList();
        }

        var owing = picked.Where(driver => StillOwed(paymentOf(driver), feeOf(driver)) > 0).ToList();

        return owing.Count > 0 ? owing : picked.ToList();
    }

    /// <summary>Money as the club writes it, in this PC's currency format: "$15.00".</summary>
    public static string Money(decimal amount) => amount.ToString("C", CultureInfo.CurrentCulture);

    /// <summary>How a cash or card payment was taken: "Cash $15.00", or "Cash $15.00 + Card $10.00" for a top-up taken the other way.</summary>
    private static string TakenWords(EntryPayment payment) =>
        OtherWayOf(payment) > 0
            ? $"Cash {Money(CashOf(payment))} + Card {Money(CardOf(payment))}"
            : $"{payment.How} {Money(payment.Amount)}";

    /// <summary>The Paid column: "Cash $15.00", "Card $25.00", "Cash $15.00 + Card $10.00", "Comp - Marshal", "Owes $15.00", or "" with nothing owed.</summary>
    public static string Words(EntryPayment? payment, decimal fee) => (payment?.How ?? PaidHow.Owing) switch
    {
        PaidHow.Cash or PaidHow.Card when StillOwed(payment, fee) is > 0 and var more =>
            $"Owes {Money(more)} more ({TakenWords(payment!)} paid)",
        PaidHow.Cash or PaidHow.Card => TakenWords(payment!),
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

        var unreadable = false;

        try
        {
            if (File.Exists(path))
            {
                if (JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, EntryPayment>>>(SafeFile.ReadAllTextWithRetry(path)) is { } read)
                {
                    foreach (var (eventId, payments) in read)
                    {
                        if (payments is null)
                        {
                            continue;
                        }

                        // One at a time, the last one winning: two keys that
                        // differ only by case would throw out of the
                        // dictionary's own constructor and lose the whole file.
                        var day = new Dictionary<string, EntryPayment>(StringComparer.OrdinalIgnoreCase);

                        foreach (var (transponder, payment) in payments)
                        {
                            if (payment is not null)
                            {
                                day[transponder] = payment;
                            }
                        }

                        loaded[eventId] = day;
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
            // A file that can't be read leaves the desk with nothing marked
            // paid, which the treasurer will notice; it never stops the app.
            //
            // WHY (13 Nov 2026, bug hunt 17): and it is never saved over.
            // The first driver marked paid used to write that one payment
            // over every earlier race day's takings (see LoadedUnreadable).
            loaded.Clear();
            unreadable = SafeFile.HoldWritesAfterUnreadable(path, ex);
        }

        return new EntryPaymentStore(loaded, path) { LoadedUnreadable = unreadable };
    }

    /// <summary>
    /// True when the file was there at startup but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. What the desk marks this session
    /// is then kept in memory only, and nothing is saved over the club's
    /// real file.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    /// <summary>Raised after any change, for the desk and the report.</summary>
    public event Action? Changed;

    /// <summary>How this driver settled on this race day, or null for not at all.</summary>
    public EntryPayment? Get(string eventId, string transponder) =>
        _byEvent.TryGetValue(eventId, out var day) && day.TryGetValue(transponder, out var payment) ? payment : null;

    /// <summary>The transponders with a payment on file for this race day, whether or not they ever ran.</summary>
    public IReadOnlyList<string> TranspondersFor(string eventId) =>
        _byEvent.TryGetValue(eventId, out var day) ? day.Keys.ToList() : new List<string>();

    /// <summary>Every payment held for these transponders, on any day - for Export this driver's data (15 Nov 2026, privacy plan 2.5).</summary>
    public IReadOnlyList<(string EventId, string Transponder, EntryPayment Payment)> AllFor(IEnumerable<string> transponders)
    {
        var ids = transponders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _byEvent
            .SelectMany(day => day.Value.Where(p => ids.Contains(p.Key)).Select(p => (day.Key, p.Key, p.Value)))
            .ToList();
    }

    /// <summary>Every payment held for these transponders, on any day, gone - Forget this driver (15 Nov 2026). Returns how many.</summary>
    public int ForgetDrivers(IEnumerable<string> transponders)
    {
        var ids = transponders.ToList();
        var gone = 0;

        foreach (var day in _byEvent.Values)
        {
            foreach (var id in ids)
            {
                if (day.Remove(id))
                {
                    gone++;
                }
            }
        }

        if (gone > 0)
        {
            Save();
            Changed?.Invoke();
        }

        return gone;
    }

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
        if (_path is null || LoadedUnreadable)
        {
            // See LoadedUnreadable: the club's real file stays as it is.
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
