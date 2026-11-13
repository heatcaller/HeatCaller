namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, the Driver Paid slip and a driver who has already settled.
///
/// WHY. The slip totalled and settled everyone on it. A family of three with
/// dad comped earlier as a marshal read "$45.00 to take" with $30 owed, and
/// Paid Cash then wrote Cash over his comp: a fee more in the night's takings
/// than was in the till, and the comp reason gone. Only the drivers still
/// owing are totalled and settled now - unless nobody on the slip owes, which
/// is the desk putting right how somebody paid.
/// </summary>
public sealed class BugHunt17_DriverPaidFormTests
{
    private static readonly DriverProfile Dad = new() { Transponder = "1", DriverName = "Dad" };
    private static readonly DriverProfile Kid = new() { Transponder = "2", DriverName = "Kid" };
    private static readonly DriverProfile Sis = new() { Transponder = "3", DriverName = "Sis" };

    private static string Words(DriverProfile profile) =>
        ReferenceEquals(profile, Dad) ? "Comp - Marshal" : "Owes " + DeskMoney.Money(15);

    [Fact]
    public void A_driver_already_comped_is_left_out_of_a_press_of_paid()
    {
        var settled = DriverPaidForm.ToSettle(new[] { Dad, Kid, Sis }, Words);

        Assert.Equal(new[] { Kid, Sis }, settled);
    }

    [Fact]
    public void A_slip_of_only_settled_drivers_still_changes_how_they_paid()
    {
        Assert.Equal(new[] { Dad }, DriverPaidForm.ToSettle(new[] { Dad }, Words));
    }

    [Fact]
    public void Owing_and_nothing_owed_are_not_settled_but_cash_card_and_comp_are()
    {
        Assert.False(DriverPaidForm.AlreadySettled(string.Empty));
        Assert.False(DriverPaidForm.AlreadySettled(DeskMoney.Words(null, 15)));
        Assert.True(DriverPaidForm.AlreadySettled(DeskMoney.Words(new EntryPayment { How = PaidHow.Cash, Amount = 15 }, 15)));
        Assert.True(DriverPaidForm.AlreadySettled(DeskMoney.Words(new EntryPayment { How = PaidHow.Card, Amount = 15 }, 15)));
        Assert.True(DriverPaidForm.AlreadySettled(DeskMoney.Words(new EntryPayment { How = PaidHow.Comp, Note = "Marshal" }, 15)));
        Assert.True(DriverPaidForm.AlreadySettled(DeskMoney.Words(new EntryPayment { How = PaidHow.Comp }, 15)));
    }

    [Fact]
    public void The_total_is_what_is_still_owed_and_says_who_is_left_alone()
    {
        Assert.Equal("Nobody on the slip yet.", DriverPaidForm.TotalWords(0, 0, 0));
        Assert.Equal($"3 drivers - {DeskMoney.Money(45)} to take", DriverPaidForm.TotalWords(3, 0, 45));
        Assert.Equal($"3 drivers - {DeskMoney.Money(30)} to take (1 already settled, left as it is)", DriverPaidForm.TotalWords(3, 1, 30));
        Assert.StartsWith("1 driver - nothing to take", DriverPaidForm.TotalWords(1, 1, 0));
    }
}
