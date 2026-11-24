namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the Rentals page: a loan line in Loan History
/// says the day a unit came back when it was not the day it went out.
/// </summary>
[Collection(AppTimeCollection.Name)]
public sealed class BugHunt24_RentalTransponderControlTests
{
    private static DateTime LocalToUtc(int month, int day, int hour, int minute) =>
        new DateTime(2026, month, day, hour, minute, 0, DateTimeKind.Local).ToUniversalTime();

    // ------------------------------------------------------------ #293 loan history return date

    [Fact]
    public void A_loan_returned_on_a_later_day_shows_the_return_date()
    {
        var was = AppTime.Use24Hour;
        try
        {
            AppTime.Use24Hour = false;
            var loan = new RentalLoan
            {
                HolderName = "Sam",
                OutUtc = LocalToUtc(11, 14, 20, 10),
                InUtc = LocalToUtc(11, 20, 19, 5),
            };

            var line = RentalTransponderControl.LoanLine(loan);

            var back = new DateTime(2026, 11, 20, 19, 5, 0, DateTimeKind.Local);
            Assert.Contains("-> " + AppTime.DateAndTime(back), line);
            Assert.Contains(back.ToShortDateString(), line);
            Assert.EndsWith("Sam", line);
        }
        finally
        {
            AppTime.Use24Hour = was;
        }
    }

    [Fact]
    public void A_loan_returned_the_same_day_shows_only_the_return_time()
    {
        var was = AppTime.Use24Hour;
        try
        {
            AppTime.Use24Hour = false;
            var loan = new RentalLoan
            {
                HolderName = "Sam",
                OutUtc = LocalToUtc(11, 14, 18, 10),
                InUtc = LocalToUtc(11, 14, 21, 45),
                ReturnNotes = "antenna bent",
            };

            var line = RentalTransponderControl.LoanLine(loan);

            var outAt = new DateTime(2026, 11, 14, 18, 10, 0, DateTimeKind.Local);
            var back = new DateTime(2026, 11, 14, 21, 45, 0, DateTimeKind.Local);
            Assert.Equal(
                $"{AppTime.DateAndTime(outAt)} -> {AppTime.TimeOfDay(back)}   Sam   [antenna bent]",
                line);
        }
        finally
        {
            AppTime.Use24Hour = was;
        }
    }
}
