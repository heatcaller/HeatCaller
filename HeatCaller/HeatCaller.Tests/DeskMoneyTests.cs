using System.Globalization;

namespace HeatCaller.Tests;

/// <summary>
/// Race-night build plan, Phase 4 (11 Nov 2026): money at the desk, tracked
/// by hand - fees, who paid how, comps, and who still owes. See DeskMoney.
/// </summary>
public sealed class DeskMoneyTests
{
    [Fact]
    public void The_fee_is_the_entry_then_the_extra_class_fee_for_each_class_after()
    {
        Assert.Equal(15m, DeskMoney.FeeFor(1, 15m, 10m));
        Assert.Equal(25m, DeskMoney.FeeFor(2, 15m, 10m));
        Assert.Equal(35m, DeskMoney.FeeFor(3, 15m, 10m));
        Assert.Equal(0m, DeskMoney.FeeFor(2, 0m, 10m));
        Assert.Equal(0m, DeskMoney.FeeFor(0, 15m, 10m));
    }

    [Fact]
    public void The_night_counts_cash_card_comps_and_who_still_owes()
    {
        var totals = DeskMoney.Count(new (string, decimal, EntryPayment?)[]
        {
            ("Sam", 15m, new EntryPayment { How = PaidHow.Cash, Amount = 15m }),
            ("Jo", 25m, new EntryPayment { How = PaidHow.Card, Amount = 25m }),
            ("Al", 15m, new EntryPayment { How = PaidHow.Comp, Note = "Marshal" }),
            ("Bea", 15m, null),
            ("Cy", 0m, null),
        });

        Assert.Equal(15m, totals.Cash);
        Assert.Equal(25m, totals.Card);
        Assert.Equal(40m, totals.Taken);
        Assert.Equal(1, totals.Comps);
        Assert.Equal(new[] { ("Bea", 15m) }, totals.Owing);
        Assert.Equal(15m, totals.OwingAmount);
    }

    [Fact]
    public void The_paid_column_says_how_each_driver_settled()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("en-US");

        try
        {
            Assert.Equal("Cash $15.00", DeskMoney.Words(new EntryPayment { How = PaidHow.Cash, Amount = 15m }, 15m));
            Assert.Equal("Card $25.00", DeskMoney.Words(new EntryPayment { How = PaidHow.Card, Amount = 25m }, 25m));
            Assert.Equal("Comp - Junior", DeskMoney.Words(new EntryPayment { How = PaidHow.Comp, Note = "Junior" }, 15m));
            Assert.Equal("Comp", DeskMoney.Words(new EntryPayment { How = PaidHow.Comp }, 15m));
            Assert.Equal("Owes $15.00", DeskMoney.Words(null, 15m));
            Assert.Equal(string.Empty, DeskMoney.Words(null, 0m));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Payments_are_kept_per_race_day_and_cleared_back_to_owing()
    {
        var store = EntryPaymentStore.InMemory();
        var changes = 0;
        store.Changed += () => changes++;

        store.Set("day1", "T1", new EntryPayment { How = PaidHow.Cash, Amount = 15m });

        Assert.Equal(PaidHow.Cash, store.Get("day1", "t1")!.How);
        Assert.Null(store.Get("day2", "T1"));

        store.Set("day1", "T1", null);

        Assert.Null(store.Get("day1", "T1"));
        Assert.Equal(2, changes);
    }

    [Fact]
    public void A_driver_owes_for_each_of_tonights_classes_they_race()
    {
        var day = new RaceEvent { EntryFee = 15m, ExtraClassFee = 10m, ClassNames = new List<string> { "Stock", "Mod", "Open" } };
        var two = new DriverProfile { Transponder = "T1", ClassName = "Stock", Classes = new List<string> { "Mod" } };
        var one = new DriverProfile { Transponder = "T2", ClassName = "Open" };

        Assert.Equal(25m, CheckInControl.FeeFor(two, day));
        Assert.Equal(15m, CheckInControl.FeeFor(one, day));
    }

    [Fact]
    public void The_closing_checklist_names_who_still_owes()
    {
        var facts = new NightClose.Facts(Array.Empty<string>(), 0, 0, DateTime.UtcNow, false, new[] { "Sam", "Jo" });

        Assert.Equal(new[] { "2 drivers still owe their entry (Sam, Jo)" }, NightClose.Left(facts, DateTime.UtcNow.AddHours(-1)));
    }

    [Fact]
    public void A_new_race_day_charges_nothing_so_money_stays_out_of_sight()
    {
        var day = new RaceEvent();

        Assert.Equal(0m, day.EntryFee);
        Assert.Equal(0m, day.ExtraClassFee);
    }
}
