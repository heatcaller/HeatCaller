namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, desk money (23 Nov 2026): Paid Cash or Paid Card over a pick
/// that mixed settled drivers with drivers still owing refiled the settled
/// ones too, so a "Cash $15.00 + Card $10.00" split read "Card $25.00" and
/// the cash and card totals no longer matched the till (#161). The rule of
/// who a press settles is DeskMoney.WhoToSettle.
/// </summary>
public sealed class BugHunt24_DeskMoneyTests
{
    private const decimal Fee = 25m;

    // Paid $15 cash, then a $10 top-up by card after a second class was ticked.
    private static readonly EntryPayment Split = new() { How = PaidHow.Card, Amount = 25m, OtherWay = 15m };

    private static readonly EntryPayment Marshal = new() { How = PaidHow.Comp, Amount = 0, Note = "Marshal" };

    private static IReadOnlyList<string> Who(PaidHow how, Dictionary<string, EntryPayment?> payments) =>
        DeskMoney.WhoToSettle(payments.Keys.ToList(), how, name => payments[name], _ => Fee);

    [Fact]
    public void Paid_card_over_a_family_leaves_the_one_who_paid_part_cash_part_card_out()
    {
        var payments = new Dictionary<string, EntryPayment?> { ["Dad"] = Split, ["Kid1"] = null, ["Kid2"] = null };

        Assert.Equal(new[] { "Kid1", "Kid2" }, Who(PaidHow.Card, payments));
        Assert.Equal(new[] { "Kid1", "Kid2" }, Who(PaidHow.Cash, payments));
    }

    [Fact]
    public void Paid_cash_over_a_mixed_pick_leaves_a_comped_marshal_out()
    {
        var payments = new Dictionary<string, EntryPayment?> { ["Dad"] = Marshal, ["Kid"] = null };

        Assert.Equal(new[] { "Kid" }, Who(PaidHow.Cash, payments));
    }

    [Fact]
    public void A_driver_owing_only_a_top_up_is_still_settled_in_a_mixed_pick()
    {
        var paidForOne = new EntryPayment { How = PaidHow.Cash, Amount = 15m };
        var payments = new Dictionary<string, EntryPayment?> { ["Dad"] = Split, ["Kid"] = paidForOne };

        Assert.Equal(new[] { "Kid" }, Who(PaidHow.Card, payments));
    }

    [Fact]
    public void A_pick_of_only_settled_drivers_is_all_put_right()
    {
        var payments = new Dictionary<string, EntryPayment?> { ["Dad"] = Split, ["Mum"] = Marshal };

        Assert.Equal(new[] { "Dad", "Mum" }, Who(PaidHow.Card, payments));
    }

    [Fact]
    public void Comp_and_still_owing_take_everybody_picked()
    {
        var payments = new Dictionary<string, EntryPayment?> { ["Dad"] = Split, ["Kid"] = null };

        Assert.Equal(new[] { "Dad", "Kid" }, Who(PaidHow.Comp, payments));
        Assert.Equal(new[] { "Dad", "Kid" }, Who(PaidHow.Owing, payments));
    }

    [Fact]
    public void A_split_driver_put_right_alone_is_still_filed_as_the_whole_fee_one_way()
    {
        var settled = DeskMoney.Settle(Split, PaidHow.Card, Fee, string.Empty, DateTime.UtcNow);

        Assert.Equal(PaidHow.Card, settled.Payment!.How);
        Assert.Equal(25m, settled.Payment.Amount);
        Assert.Equal(0m, settled.Payment.OtherWay);
    }
}
