namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, entry money on the driver's phone: worked out with the
/// classes' own fees, as the desk's Paid column is, where it used to be
/// given the race day's fees alone.
/// </summary>
public sealed class BugHunt17_CheckInControl_MoneyTests
{
    private static readonly Func<string, RaceClass?> Classes = DeskMoney.ClassLookup(new[]
    {
        new RaceClass { Name = "Juniors", EntryFee = 5m },
        new RaceClass { Name = "Mod", EntryFee = 20m },
        new RaceClass { Name = "Stock" },
    });

    [Fact]
    public void The_phone_is_told_the_class_s_own_fee_not_the_day_s()
    {
        var day = new RaceEvent { EntryFee = 15m, ExtraClassFee = 10m, ClassNames = new List<string> { "Juniors", "Mod", "Stock" } };

        var junior = new DriverProfile { Transponder = "T1", ClassName = "Juniors" };
        var twoClasses = new DriverProfile { Transponder = "T2", ClassName = "Stock", Classes = new List<string> { "Mod" } };

        Assert.Equal(DeskMoney.Money(5m), CheckInControl.PhoneMoneyFor(junior, day, Classes, null)?.Fee);
        Assert.Equal(DeskMoney.Money(30m), CheckInControl.PhoneMoneyFor(twoClasses, day, Classes, null)?.Fee);
    }

    [Fact]
    public void A_night_priced_only_by_its_classes_still_shows_on_the_phone()
    {
        var day = new RaceEvent { EntryFee = 0m, ClassNames = new List<string> { "Mod", "Stock" } };
        var mod = new DriverProfile { Transponder = "T1", ClassName = "Mod" };

        var owing = CheckInControl.PhoneMoneyFor(mod, day, Classes, null);

        Assert.NotNull(owing);
        Assert.False(owing!.Paid);
        Assert.Equal(DeskMoney.Money(20m), owing.Fee);

        var paid = CheckInControl.PhoneMoneyFor(mod, day, Classes, new EntryPayment { How = PaidHow.Cash, Amount = 20m });

        Assert.True(paid!.Paid);
    }

    [Fact]
    public void Nothing_is_said_about_money_on_a_night_that_charges_nothing()
    {
        var free = new RaceEvent { EntryFee = 0m, ClassNames = new List<string> { "Stock" } };
        var stock = new DriverProfile { Transponder = "T1", ClassName = "Stock" };

        Assert.Null(CheckInControl.PhoneMoneyFor(stock, free, Classes, null));
        Assert.Null(CheckInControl.PhoneMoneyFor(stock, null, Classes, null));
    }
}
