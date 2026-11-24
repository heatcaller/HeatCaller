namespace HeatCaller;

/// <summary>
/// Money at the desk on Who's Racing (11 Nov 2026, race-night build plan,
/// Phase 4): the Paid column, and settling drivers from the right-click
/// menu. Tracked by hand - HeatCaller never takes a payment. See DeskMoney.
/// </summary>
public sealed partial class CheckInControl
{
    /// <summary>
    /// Tonight's race day and whether it charges, or null with no race day
    /// open. Charges is the day's entry fee or a class's own (12 Nov 2026,
    /// setup plan step 8) - it used to be the day's fee alone, so a club that
    /// priced only its classes would never have seen the Paid column. See
    /// DeskMoney.Charges.
    /// </summary>
    private (RaceEvent Day, bool Charges)? TonightsFees() =>
        _events?.Active is { IsLegacy: false } day ? (day, DeskMoney.Charges(day, ClassNamed, _raceClasses.All)) : null;

    /// <summary>A class by name from the club's classes, for its fees.</summary>
    private RaceClass? ClassNamed(string name) => DeskMoney.ClassLookup(_raceClasses.All)(name);

    /// <summary>What this driver owes tonight, with only the race day's fees - see the overload.</summary>
    internal static decimal FeeFor(DriverProfile profile, RaceEvent day) => FeeFor(profile, day, null);

    /// <summary>
    /// What this driver owes tonight: their dearest class's entry fee, and the
    /// extra-class fee for each other class of tonight's they race - each
    /// class's own fees where it has them (12 Nov 2026, setup plan step 8),
    /// the race day's otherwise. See DeskMoney.FeeFor.
    ///
    /// With no classes ticked for the night, the driver's main class is what
    /// they're paying for - one class, as it always was.
    /// </summary>
    internal static decimal FeeFor(DriverProfile profile, RaceEvent day, Func<string, RaceClass?>? classNamed)
    {
        var names = day.ClassNames is { Count: > 0 } tonight
            ? tonight.Where(profile.RunsClass).ToList()
            : new List<string>();

        if (names.Count == 0)
        {
            names.Add(profile.ClassName ?? string.Empty);
        }

        return DeskMoney.FeeFor(names.Select(name => DeskMoney.FeeOf(classNamed?.Invoke(name), day.EntryFee, day.ExtraClassFee)));
    }

    /// <summary>
    /// A driver's own entry money for their phone - the same sum the Paid
    /// column shows, or null on a night that charges nothing.
    ///
    /// The phone used to be given the race day's fees alone, and only when
    /// the day itself had one: a junior in a $5 class was told $15, and on a
    /// night priced by its classes the phone said nothing about money at all.
    /// </summary>
    internal static DeskMoney.PhoneMoney? PhoneMoneyFor(
        DriverProfile profile, RaceEvent? day, Func<string, RaceClass?> classNamed, EntryPayment? payment,
        IEnumerable<RaceClass>? clubClasses = null) =>
        day is not null && DeskMoney.Charges(day, classNamed, clubClasses)
            ? DeskMoney.ForPhone(FeeFor(profile, day, classNamed), payment)
            : null;

    /// <summary>Fills a row's Paid cell.</summary>
    private void ShowPaid(DataGridViewRow row)
    {
        if (_payments is null || _grid.Columns["paid"] is not { } column || row.Tag is not DriverProfile profile
            || TonightsFees() is not { Charges: true } fees)
        {
            return;
        }

        var payment = _payments.Get(fees.Day.Id, profile.Transponder);
        var cell = row.Cells[column.Index];
        var fee = FeeFor(profile, fees.Day, ClassNamed);
        cell.Value = DeskMoney.Words(payment, fee);
        cell.Style.ForeColor = DeskMoney.StillOwed(payment, fee) > 0 ? AppTheme.Warning : AppTheme.Success;
    }

    /// <summary>The right-click menu's Paid cash / Paid card / Comp / Owing, for the drivers picked.</summary>
    private void AddPaymentItems(ContextMenuStrip menu, IReadOnlyList<DriverProfile> drivers)
    {
        if (_payments is not { } payments || TonightsFees() is not { Charges: true } fees)
        {
            return;
        }

        menu.Items.Add(new ToolStripSeparator());

        void Item(string text, PaidHow how, string note = "")
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (_, _) => Settle(fees.Day, drivers, how, note);
            menu.Items.Add(item);
        }

        var who = drivers.Count == 1 ? string.Empty : $" ({drivers.Count})";

        // WHY (23 Nov 2026, bug hunt 24): Paid Cash and Paid Card settle only
        // the drivers still owing (see Settle), so their count says that many
        // - it used to count every driver picked, settled ones too.
        var toSettle = DeskMoney.WhoToSettle(drivers, PaidHow.Cash, p => payments.Get(fees.Day.Id, p.Transponder), p => FeeFor(p, fees.Day, ClassNamed));
        var paidWho = drivers.Count == 1 ? string.Empty : $" ({toSettle.Count})";
        Item($"Paid Cash{paidWho}", PaidHow.Cash);
        Item($"Paid Card{paidWho}", PaidHow.Card);

        var comp = new ToolStripMenuItem($"Comp{who}");

        foreach (var reason in new[] { "Marshal", "Volunteer", "Junior", "Other" })
        {
            var sub = new ToolStripMenuItem(reason);
            var why = reason == "Other" ? string.Empty : reason;
            sub.Click += (_, _) => Settle(fees.Day, drivers, PaidHow.Comp, why);
            comp.DropDownItems.Add(sub);
        }

        menu.Items.Add(comp);
        Item($"Still Owing{who}", PaidHow.Owing);
    }

    /// <summary>Settles the drivers one way, as one undo.</summary>
    private void Settle(RaceEvent day, IReadOnlyList<DriverProfile> drivers, PaidHow how, string note)
    {
        if (_payments is not { } payments)
        {
            return;
        }

        // WHY (23 Nov 2026, bug hunt 24): Paid Cash or Paid Card over a pick
        // that mixed settled and owing drivers refiled the settled ones too - a
        // "Cash $15.00 + Card $10.00" split became "Card $25.00" (cash $15 under,
        // card $15 over the till) and Cash was written over a comp, losing its
        // reason. Only the drivers still owing are settled now, as the Driver
        // Paid slip already did; the undo, the desk log and the status line
        // count just them.
        var picked = drivers.Count;
        drivers = DeskMoney.WhoToSettle(drivers, how, p => payments.Get(day.Id, p.Transponder), p => FeeFor(p, day, ClassNamed));
        var leftAlone = picked - drivers.Count;

        var before = drivers.ToDictionary(p => p.Transponder, p => payments.Get(day.Id, p.Transponder), StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;

        // A top-up takes only what is still owed, kept by the way it was
        // taken (bug hunt 21) - see DeskMoney.Settle.
        (EntryPayment? Payment, decimal Taken, bool TopUp) settled = default;

        foreach (var profile in drivers)
        {
            settled = DeskMoney.Settle(payments.Get(day.Id, profile.Transponder), how, FeeFor(profile, day, ClassNamed), note, now);
            payments.Set(day.Id, profile.Transponder, settled.Payment);
        }

        _undo.Capture($"Mark {Plural.Of(drivers.Count, "driver")} {how.ToString().ToLowerInvariant()}", () =>
        {
            foreach (var (transponder, payment) in before)
            {
                payments.Set(day.Id, transponder, payment);
            }

            RefreshDriverList();
        });

        LogDesk(drivers.Count == 1 ? DisplayName(drivers[0]) : Plural.Of(drivers.Count, "driver"), how switch
        {
            PaidHow.Owing => "marked as still owing",
            PaidHow.Comp => note.Length > 0 ? $"comp ({note})" : "comp",
            // What was taken just now (bug hunt 21): a top-up's $10, not the whole fee again.
            _ => drivers.Count == 1
                ? $"paid {DeskMoney.Money(settled.Taken)}{(settled.TopUp ? " more" : string.Empty)} {how.ToString().ToLowerInvariant()}"
                : $"paid by {how.ToString().ToLowerInvariant()}",
        });

        RefreshDriverList();
        SetScanStatus(SettledWords(drivers.Count, leftAlone, how), AppTheme.Success);
    }

    /// <summary>
    /// The status line after Paid Cash, Paid Card, Comp or Still Owing:
    /// "2 drivers marked paid by card. 1 already settled, left as it is."
    /// </summary>
    internal static string SettledWords(int settled, int leftAlone, PaidHow how)
    {
        var words = how == PaidHow.Owing
            ? $"{Plural.Of(settled, "driver")} marked as still owing."
            : $"{Plural.Of(settled, "driver")} marked {(how == PaidHow.Comp ? "comp" : $"paid by {how.ToString().ToLowerInvariant()}")}.";

        return leftAlone > 0
            ? $"{words} {leftAlone} already settled, left as {(leftAlone == 1 ? "it is" : "they are")}."
            : words;
    }

    /// <summary>
    /// The Driver Paid button (11 Nov 2026): opens the slip that a
    /// double-click on a name adds to. Shown only on a race day that charges,
    /// like the Paid column - see RefreshPaidSlip.
    /// </summary>
    private readonly Button _btnDriverPaid = new() { Text = "Driver Paid...", Visible = false };

    /// <summary>The Driver Paid slip while it is open, or null.</summary>
    private DriverPaidForm? _paidForm;

    /// <summary>The slip, while open. For the tests.</summary>
    internal DriverPaidForm? PaidForm => _paidForm;

    /// <summary>Opens the Driver Paid slip, or brings it forward if it is open already.</summary>
    internal void OpenDriverPaid()
    {
        if (_payments is not { } payments || TonightsFees() is not { Charges: true })
        {
            return;
        }

        if (_paidForm is { IsDisposed: false } open)
        {
            open.Activate();
            return;
        }

        var form = new DriverPaidForm(
            profile => TonightsFees() is { } fees ? FeeFor(profile, fees.Day, ClassNamed) : 0,
            profile => TonightsFees() is { } fees
                ? DeskMoney.Words(payments.Get(fees.Day.Id, profile.Transponder), FeeFor(profile, fees.Day, ClassNamed))
                : string.Empty,
            (drivers, how, note) =>
            {
                if (TonightsFees() is { } fees)
                {
                    Settle(fees.Day, drivers, how, note);
                }
            },
            profile => _profiles.Get(profile.Transponder) ?? profile,
            profile => TonightsFees() is { } fees
                ? DeskMoney.StillOwed(payments.Get(fees.Day.Id, profile.Transponder), FeeFor(profile, fees.Day, ClassNamed))
                : 0);

        form.FormClosed += (_, _) => _paidForm = null;
        _paidForm = form;

        // Non-modal, so the roster behind it can still be double-clicked.
        if (FindForm() is { } owner)
        {
            form.Show(owner);
        }
        else
        {
            form.Show();
        }
    }

    /// <summary>Adds the drivers to the Driver Paid slip, if it is open.</summary>
    internal void AddToPaidSlip(IReadOnlyList<DriverProfile> drivers)
    {
        if (_paidForm is { IsDisposed: false } form && drivers.Count > 0)
        {
            form.AddDrivers(drivers);
        }
    }

    /// <summary>
    /// After the list is redrawn: the button shown only on a night that
    /// charges, and the slip kept up to date - or closed, with nothing to
    /// charge for.
    /// </summary>
    private void RefreshPaidSlip(bool charging)
    {
        if (_btnDriverPaid.Visible != charging)
        {
            _btnDriverPaid.Visible = charging;
        }

        if (_paidForm is not { IsDisposed: false } form)
        {
            return;
        }

        if (charging)
        {
            form.RefreshRows();
        }
        else
        {
            form.Close();
        }
    }
}
