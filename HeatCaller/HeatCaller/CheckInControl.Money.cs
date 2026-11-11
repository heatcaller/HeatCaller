namespace HeatCaller;

/// <summary>
/// Money at the desk on Who's Racing (11 Nov 2026, race-night build plan,
/// Phase 4): the Paid column, and settling drivers from the right-click
/// menu. Tracked by hand - HeatCaller never takes a payment. See DeskMoney.
/// </summary>
public sealed partial class CheckInControl
{
    /// <summary>Tonight's race day and its fees, or null with no race day open.</summary>
    private (RaceEvent Day, decimal Entry, decimal Extra)? TonightsFees() =>
        _events?.Active is { IsLegacy: false } day ? (day, day.EntryFee, day.ExtraClassFee) : null;

    /// <summary>What this driver owes tonight: the entry fee, and the extra-class fee for each of tonight's classes after the first.</summary>
    internal static decimal FeeFor(DriverProfile profile, RaceEvent day)
    {
        var classes = day.ClassNames is { Count: > 0 } names
            ? names.Count(profile.RunsClass)
            : 1;

        return DeskMoney.FeeFor(Math.Max(1, classes), day.EntryFee, day.ExtraClassFee);
    }

    /// <summary>Fills a row's Paid cell.</summary>
    private void ShowPaid(DataGridViewRow row)
    {
        if (_payments is null || _grid.Columns["paid"] is not { } column || row.Tag is not DriverProfile profile
            || TonightsFees() is not { Entry: > 0 } fees)
        {
            return;
        }

        var payment = _payments.Get(fees.Day.Id, profile.Transponder);
        var cell = row.Cells[column.Index];
        cell.Value = DeskMoney.Words(payment, FeeFor(profile, fees.Day));
        cell.Style.ForeColor = (payment?.How ?? PaidHow.Owing) == PaidHow.Owing ? AppTheme.Warning : AppTheme.Success;
    }

    /// <summary>The right-click menu's Paid cash / Paid card / Comp / Owing, for the drivers picked.</summary>
    private void AddPaymentItems(ContextMenuStrip menu, IReadOnlyList<DriverProfile> drivers)
    {
        if (_payments is null || TonightsFees() is not { Entry: > 0 } fees)
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
        Item($"Paid Cash{who}", PaidHow.Cash);
        Item($"Paid Card{who}", PaidHow.Card);

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

        var before = drivers.ToDictionary(p => p.Transponder, p => payments.Get(day.Id, p.Transponder), StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;

        foreach (var profile in drivers)
        {
            payments.Set(day.Id, profile.Transponder, how == PaidHow.Owing
                ? null
                : new EntryPayment
                {
                    How = how,
                    Amount = how is PaidHow.Cash or PaidHow.Card ? FeeFor(profile, day) : 0,
                    Note = note,
                    AtUtc = now,
                });
        }

        _undo.Capture($"Mark {Plural.Of(drivers.Count, "driver")} {how.ToString().ToLowerInvariant()}", () =>
        {
            foreach (var (transponder, payment) in before)
            {
                payments.Set(day.Id, transponder, payment);
            }

            RefreshDriverList();
        });

        RefreshDriverList();
        SetScanStatus(how == PaidHow.Owing
            ? $"{Plural.Of(drivers.Count, "driver")} marked as still owing."
            : $"{Plural.Of(drivers.Count, "driver")} marked {(how == PaidHow.Comp ? "comp" : $"paid by {how.ToString().ToLowerInvariant()}")}.",
            AppTheme.Success);
    }
}
